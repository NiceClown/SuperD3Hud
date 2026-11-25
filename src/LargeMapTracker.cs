using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SuperD3Hud
{
    /// <summary>
    /// Tracks whether the full-screen map of the game is open.
    ///
    /// In Diablo III the large map is bound to <b>Tab</b> and the waypoint /
    /// quest map ("карта врат странствий") to <b>M</b>. Those are two different
    /// screens: the overlay belongs on the first one only, so pressing the
    /// waypoint key hides it.
    ///
    /// The game does not publish which screen is open, so the state is mirrored
    /// from the keys. Both game settings are supported:
    ///
    /// * "toggle map" — a short tap opens or closes the map, so a short tap
    ///   flips the remembered state;
    /// * "hold to show map" — the map is visible only while the key is held.
    ///
    /// Key presses are taken from a low-level keyboard hook. Polling the key
    /// state from the UI timer alone missed short taps whenever one frame of the
    /// overlays took longer than the tap, and the overlay then disagreed with
    /// the game. Polling remains as a fallback when the hook cannot be set.
    ///
    /// The hook lives on its own background thread that does nothing but pump
    /// messages. A WH_KEYBOARD_LL callback runs on the installing thread's
    /// message loop, so on the busy UI thread every keystroke in the system
    /// (including the ones going to the game) waited for the overlay frame to
    /// finish, and after a long stall Windows silently removed the hook. The UI
    /// thread now only drains the event queue, and Poll checks that the hook is
    /// still delivering events, re-installing it (and polling meanwhile) when
    /// it is not.
    ///
    /// A manual override lets the user force the overlay on or off when the
    /// bindings in the game were changed.
    /// </summary>
    public class LargeMapTracker : IDisposable
    {
        private const int VK_RETURN = 0x0D;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_SPACE = 0x20;
        private const int VK_B = 0x42;
        private const int VK_C = 0x43;
        private const int VK_I = 0x49;
        private const int VK_L = 0x4C;
        private const int VK_O = 0x4F;
        private const int VK_P = 0x50;
        private const int VK_S = 0x53;
        private const int VK_T = 0x54;
        private const int VK_Y = 0x59;

        private static readonly int[] CloseKeys =
        {
            VK_ESCAPE, VK_SPACE, VK_RETURN, VK_I, VK_C, VK_S, VK_T, VK_B, VK_L, VK_O, VK_P, VK_Y
        };

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint WM_QUIT = 0x0012;
        private const uint WM_USER = 0x0400;
        private const uint PM_NOREMOVE = 0x0000;

        /// <summary>Thread message asking the hook thread to re-install the hook.</summary>
        private const uint WM_REHOOK = WM_USER + 0x51;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private struct KeyEvent
        {
            public int Vk;
            public bool Down;
            public DateTime At;
        }

        private int _mapKey;
        private int _toggleKey;
        private int _waypointKey;

        private bool _wasDown;
        private bool _wasEscapeDown;
        private bool _wasToggleDown;
        private bool _wasWaypointDown;
        private DateTime _downAt;
        private bool _toggled;

        // Low-level hook state. The callback (on the hook thread) only queues
        // events under _queueSync; Poll applies them on the UI thread.
        private LowLevelKeyboardProc _hookProc;
        private readonly object _queueSync = new object();
        private readonly Queue<KeyEvent> _queue = new Queue<KeyEvent>();
        private readonly bool[] _hookDown = new bool[256];
        /// <summary>Stopwatch time (ms) of the last hook callback; guarded by _queueSync.</summary>
        private long _lastHookEventMs;
        private bool _mapHeld;

        /// <summary>
        /// One hook thread and what it owns. Each thread gets its own instance,
        /// so a replaced thread that is still shutting down can only unhook its
        /// own handle and never the one of its successor.
        /// </summary>
        private sealed class HookThreadState
        {
            public Thread Thread;
            /// <summary>Win32 id of the thread, for PostThreadMessage; 0 until it runs.</summary>
            public volatile uint ThreadId;
            /// <summary>The installed hook, written only by the thread itself.</summary>
            public volatile IntPtr Hook;
            public ManualResetEvent Ready;
        }

        // Current hook thread. Only StartHook / Dispose / Poll (UI thread)
        // replace it; the thread itself writes ThreadId and Hook.
        private volatile HookThreadState _hookState;
        /// <summary>True from a re-install request until the hook thread has re-hooked.</summary>
        private volatile bool _rehookPending;
        private volatile bool _disposed;
        /// <summary>Set once the hook kept dying; the tracker then polls for good.</summary>
        private bool _hookGivenUp;
        private readonly List<long> _reinstallTimes = new List<long>();
        private int _reinstallCount;
        private long _lastThreadRestartMs;
        private long _rehookRequestedMs;
        /// <summary>True while Poll is on the key-state fallback instead of the hook.</summary>
        private bool _inFallback;
        /// <summary>True after Reset until the next foreground Poll.</summary>
        private bool _resetSinceForeground;

        // Liveness probe (UI thread only). Poll watches the tracked keys with
        // GetAsyncKeyState; a press it sees must also have produced a hook
        // callback, otherwise the hook is gone.
        private readonly bool[] _probeDown = new bool[256];
        private bool _probeSeeded;
        private long _lastPollMs;
        /// <summary>Poll time before the unconfirmed press, or -1 when nothing waits.</summary>
        private long _pendingSinceMs = -1;
        /// <summary>Poll time at which the unconfirmed press was first seen.</summary>
        private long _pendingSeenMs;

        /// <summary>True when the user forced the overlay with the manual key.</summary>
        private bool _manual;
        private DateTime _manualAt;

        private static readonly TimeSpan TapLimit = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan ManualHold = TimeSpan.FromMinutes(30);

        /// <summary>
        /// How long a press seen by polling may stay without a hook callback
        /// before the hook is considered removed. The hook thread does nothing
        /// else, so a live hook answers within a few milliseconds.
        /// </summary>
        private const long HookSilenceLimitMs = 200;
        /// <summary>
        /// Re-installs allowed within <see cref="ReinstallWindowMs"/> before the
        /// tracker stops trusting the hook. Protects against a setup where the
        /// hook never sees the keys (for example an elevated game window), which
        /// would otherwise re-install on every key press.
        /// </summary>
        private const int MaxReinstalls = 5;
        private const long ReinstallWindowMs = 5 * 60 * 1000;
        /// <summary>Minimum delay between attempts to start a dead hook thread.</summary>
        private const long ThreadRestartDelayMs = 5000;
        /// <summary>How long a re-install request may stay unanswered before the thread is replaced.</summary>
        private const long RehookTimeoutMs = 2000;

        /// <param name="mapKey">Virtual key of the large map, Tab (0x09) by default.</param>
        /// <param name="toggleKey">Virtual key of the manual override, F9 by default.</param>
        /// <param name="waypointKey">Virtual key of the waypoint map, M (0x4D) by default.</param>
        public LargeMapTracker(int mapKey, int toggleKey, int waypointKey)
        {
            _mapKey = mapKey > 0 ? mapKey : 0x09;
            _toggleKey = toggleKey > 0 ? toggleKey : 0x78;
            _waypointKey = waypointKey > 0 ? waypointKey : 0x4D;
        }

        public LargeMapTracker()
            : this(0x09, 0x78, 0x4D)
        {
        }

        /// <summary>Updates the keys from settings.</summary>
        public void Configure(int mapKey, int toggleKey, int waypointKey)
        {
            if (mapKey > 0) _mapKey = mapKey;
            if (toggleKey > 0) _toggleKey = toggleKey;
            if (waypointKey > 0) _waypointKey = waypointKey;
        }

        /// <summary>Virtual key currently used for the large map.</summary>
        public int MapKey { get { return _mapKey; } }

        /// <summary>True while the map should be shown.</summary>
        public bool Visible { get; private set; }

        /// <summary>True when the state came from the manual override.</summary>
        public bool Manual { get { return _manual; } }

        /// <summary>
        /// True when key presses come from the low-level hook. False while the
        /// hook is being re-installed, after it was given up, or when it could
        /// not be set at all.
        /// </summary>
        public bool HookActive
        {
            get { return HookHandle != IntPtr.Zero && !_rehookPending && !_hookGivenUp && IsHookThreadAlive(); }
        }

        /// <summary>How many times the hook was found dead and re-installed (diagnostics).</summary>
        public int HookReinstalls { get { return _reinstallCount; } }

        /// <summary>
        /// Installs the low-level keyboard hook on a dedicated background thread
        /// and waits briefly until it is in place. May be called from any
        /// thread; safe to call more than once.
        /// </summary>
        public bool StartHook()
        {
            if (_disposed) return false;
            if (HookHandle != IntPtr.Zero && IsHookThreadAlive()) return true;
            _hookGivenUp = false;
            StartHookThread(2000);
            return HookHandle != IntPtr.Zero;
        }

        public void Dispose()
        {
            _disposed = true;
            StopHookThread(1000);
        }

        /// <summary>The hook handle of the current hook thread, or zero.</summary>
        private IntPtr HookHandle
        {
            get
            {
                HookThreadState s = _hookState;
                return s != null ? s.Hook : IntPtr.Zero;
            }
        }

        private bool IsHookThreadAlive()
        {
            HookThreadState s = _hookState;
            return s != null && s.Thread != null && s.Thread.IsAlive;
        }

        /// <summary>
        /// Starts a new hook thread. Waits up to <paramref name="waitMs"/> for
        /// it to report whether SetWindowsHookEx succeeded; 0 does not wait.
        /// </summary>
        private void StartHookThread(int waitMs)
        {
            // A previous thread that is still running (but lost its hook) is
            // asked to quit; it is a background thread, so it cannot keep the
            // process alive even if it does not.
            StopHookThread(0);

            HookThreadState s = new HookThreadState();
            s.Ready = new ManualResetEvent(false);
            Thread t = new Thread(delegate() { HookThreadMain(s); });
            t.Name = "LargeMapTracker keyboard hook";
            t.IsBackground = true;
            // Above normal so a busy machine (the overlay drawing, the game)
            // never delays the callback past LowLevelHooksTimeout.
            t.Priority = ThreadPriority.AboveNormal;
            s.Thread = t;
            _rehookPending = false;
            _hookState = s;
            _lastThreadRestartMs = NowMs();
            try
            {
                t.Start();
                if (waitMs > 0) s.Ready.WaitOne(waitMs);
            }
            catch
            {
                if (_hookState == s) _hookState = null;
            }
        }

        private void StopHookThread(int joinMs)
        {
            HookThreadState s = _hookState;
            _hookState = null;
            _rehookPending = false;
            if (s == null) return;
            uint id = s.ThreadId;
            if (id != 0)
            {
                // GetMessage returns 0 on WM_QUIT; the thread then unhooks.
                try { PostThreadMessage(id, WM_QUIT, IntPtr.Zero, IntPtr.Zero); }
                catch { }
            }
            if (joinMs > 0 && s.Thread != null)
            {
                try { s.Thread.Join(joinMs); }
                catch { }
            }
        }

        private void HookThreadMain(HookThreadState s)
        {
            try
            {
                MSG msg;
                // A thread gets its message queue on the first message call;
                // create it now so PostThreadMessage from Dispose cannot fail
                // between installing the hook and entering GetMessage.
                PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE);
                s.ThreadId = GetCurrentThreadId();

                InstallHook(s);
                try { s.Ready.Set(); }
                catch { }
                if (s.Hook == IntPtr.Zero) return;

                // The low-level hook callback is invoked from inside GetMessage,
                // so this loop is all the thread has to do.
                while (!_disposed && _hookState == s)
                {
                    int r = GetMessage(out msg, IntPtr.Zero, 0, 0);
                    if (r == 0 || r == -1) break;
                    if (msg.hwnd == IntPtr.Zero && msg.message == WM_REHOOK)
                    {
                        // Windows may have dropped the hook without telling us;
                        // the old handle is released anyway in case it did not.
                        RemoveHook(s);
                        InstallHook(s);
                        if (_hookState == s) _rehookPending = false;
                        if (s.Hook == IntPtr.Zero) break;
                        continue;
                    }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch { }
            finally
            {
                RemoveHook(s);
                if (_hookState == s) _rehookPending = false;
                // Wake StartHook if it is still waiting on a failed start.
                try { s.Ready.Set(); }
                catch { }
            }
        }

        /// <summary>Installs the hook for <paramref name="s"/>; runs on its thread.</summary>
        private void InstallHook(HookThreadState s)
        {
            IntPtr hook = IntPtr.Zero;
            try
            {
                // One delegate for every thread, kept in a field so the GC
                // cannot collect it while Windows still holds the pointer.
                if (_hookProc == null) _hookProc = HookCallback;
                IntPtr module = IntPtr.Zero;
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                    using (ProcessModule main = process.MainModule)
                        module = GetModuleHandle(main.ModuleName);
                }
                catch { }
                if (module == IntPtr.Zero) module = GetModuleHandle(null);
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, module, 0);
            }
            catch
            {
                hook = IntPtr.Zero;
            }

            if (hook != IntPtr.Zero)
            {
                // Seed the hook's key mirror with what is held right now. A key
                // held across the re-install would otherwise look newly pressed
                // on its first auto-repeat and flip the map a second time.
                bool[] live = ReadAllKeys();
                lock (_queueSync)
                {
                    Array.Copy(live, _hookDown, _hookDown.Length);
                    _queue.Clear();
                    _lastHookEventMs = NowMs();
                }
            }
            s.Hook = hook;
        }

        /// <summary>Removes the hook of <paramref name="s"/>; runs on its thread.</summary>
        private static void RemoveHook(HookThreadState s)
        {
            IntPtr hook = s.Hook;
            s.Hook = IntPtr.Zero;
            if (hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(hook); }
                catch { }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                    if (down || up)
                    {
                        KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                        int vk = data.vkCode & 0xFF;
                        long now = NowMs();
                        lock (_queueSync)
                        {
                            // Any event, auto-repeat included, proves the hook is alive.
                            _lastHookEventMs = now;
                            // Auto-repeat sends more key-down messages; only the
                            // transitions matter.
                            if (_hookDown[vk] != down)
                            {
                                _hookDown[vk] = down;
                                if (_queue.Count < 256)
                                    _queue.Enqueue(new KeyEvent { Vk = vk, Down = down, At = DateTime.UtcNow });
                            }
                        }
                    }
                }
            }
            catch { }
            // Windows ignores the hhk argument, so which thread owns the
            // current handle does not matter here.
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        public void Close()
        {
            _toggled = false;
            _manual = false;
            Visible = false;
        }

        /// <summary>
        /// Updates the state. Must be called from the UI thread; the caller
        /// passes whether the game window has focus.
        /// </summary>
        /// <param name="gameForeground">True when the game window has focus.</param>
        /// <param name="followMapKey">
        /// When false the in-game map key is ignored and only the manual
        /// override is used, which is useful when the key was rebound.
        /// </param>
        public void Poll(bool gameForeground, bool followMapKey)
        {
            if (!gameForeground)
            {
                // The map closes when the game loses focus, and the overlay
                // must never float over another application.
                Reset();
                return;
            }

            _resetSinceForeground = false;
            long now = NowMs();
            bool hookUsable = HookHandle != IntPtr.Zero && !_rehookPending && !_hookGivenUp && IsHookThreadAlive();
            bool pressSeen = ProbeKeys(now);

            if (hookUsable && !HookConfirmed(now, pressSeen))
            {
                // The hook stopped delivering events (Windows removes a hook
                // that timed out, without telling the owner). Hand the keys the
                // hook did see over to the key-state poller, so only the
                // presses it missed are replayed, then re-install.
                SwitchToFallback();
                ReinstallHook(now);
                hookUsable = false;
            }

            if (hookUsable)
            {
                if (_inFallback)
                {
                    // Back on the hook after polling: carry over whether the
                    // map key is held, as PollFromKeyState last saw it.
                    _mapHeld = followMapKey && _wasDown;
                    _inFallback = false;
                }
                PollFromHook(followMapKey);
            }
            else
            {
                if (!_inFallback) SwitchToFallback();
                PollFromKeyState(followMapKey);
                RestartDeadHookThread(now);
            }

            _lastPollMs = now;

            // The manual override is only trusted for a while after the key was
            // pressed, so a forgotten override cannot keep a stale overlay up.
            if (_manual && DateTime.UtcNow - _manualAt > ManualHold) _manual = false;
        }

        /// <summary>
        /// Updates the polled mirror of the tracked keys and returns true when
        /// one of them went down since the previous Poll.
        /// </summary>
        private bool ProbeKeys(long now)
        {
            bool pressed = false;
            pressed |= ProbeKey(_mapKey);
            pressed |= ProbeKey(_toggleKey);
            pressed |= ProbeKey(_waypointKey);
            foreach (int vk in CloseKeys) pressed |= ProbeKey(vk);

            if (!_probeSeeded)
            {
                // The first Poll after a Reset only learns the current state;
                // a key already held then is not a fresh press.
                _probeSeeded = true;
                return false;
            }
            return pressed;
        }

        private bool ProbeKey(int vk)
        {
            vk &= 0xFF;
            bool down = IsDown(vk);
            bool pressed = down && !_probeDown[vk];
            _probeDown[vk] = down;
            return pressed;
        }

        /// <summary>
        /// Liveness check. A press seen by GetAsyncKeyState must have passed
        /// through the hook first (the system updates the async key state only
        /// after the low-level hooks ran), so a hook event later than the Poll
        /// before the press is expected. Returns false when none arrived within
        /// <see cref="HookSilenceLimitMs"/>.
        /// </summary>
        private bool HookConfirmed(long now, bool pressSeen)
        {
            if (pressSeen && _pendingSinceMs < 0)
            {
                _pendingSinceMs = _lastPollMs;
                _pendingSeenMs = now;
            }
            if (_pendingSinceMs < 0) return true;

            long lastEvent;
            lock (_queueSync) lastEvent = _lastHookEventMs;
            if (lastEvent >= _pendingSinceMs)
            {
                _pendingSinceMs = -1;
                return true;
            }
            if (now - _pendingSeenMs > HookSilenceLimitMs)
            {
                _pendingSinceMs = -1;
                return false;
            }
            // Still within the grace period: keep trusting the hook.
            return true;
        }

        /// <summary>
        /// Prepares the key-state poller to take over from the hook: its
        /// "previously down" flags become what the hook last saw, and the hook's
        /// own mirror and queue are dropped (the hook thread re-seeds them).
        /// </summary>
        private void SwitchToFallback()
        {
            lock (_queueSync)
            {
                _wasDown = _hookDown[_mapKey & 0xFF];
                _wasToggleDown = _hookDown[_toggleKey & 0xFF];
                _wasWaypointDown = _hookDown[_waypointKey & 0xFF];
                bool close = false;
                foreach (int vk in CloseKeys)
                {
                    if (_hookDown[vk]) { close = true; break; }
                }
                _wasEscapeDown = close;
                Array.Clear(_hookDown, 0, _hookDown.Length);
                _queue.Clear();
            }
            _mapHeld = false;
            _pendingSinceMs = -1;
            _inFallback = true;
        }

        /// <summary>Asks the hook thread to re-install the hook, or restarts the thread.</summary>
        private void ReinstallHook(long now)
        {
            if (_disposed) return;

            _reinstallCount++;
            if (!ReinstallBudgetLeft(now)) return;

            HookThreadState state = _hookState;
            uint id = state != null ? state.ThreadId : 0;
            bool posted = false;
            if (id != 0 && IsHookThreadAlive())
            {
                _rehookPending = true;
                _rehookRequestedMs = now;
                try { posted = PostThreadMessage(id, WM_REHOOK, IntPtr.Zero, IntPtr.Zero); }
                catch { posted = false; }
                if (!posted) _rehookPending = false;
            }
            if (!posted) StartHookThread(0);
        }

        /// <summary>
        /// Restarts the hook thread when it died (for example SetWindowsHookEx
        /// failed during a re-install), at most every few seconds.
        /// </summary>
        private void RestartDeadHookThread(long now)
        {
            if (_disposed || _hookGivenUp) return;
            if (_lastThreadRestartMs == 0) return;   // StartHook was never called
            if (_rehookPending)
            {
                // The thread only pumps messages, so it answers a re-install
                // request at once; one that does not is replaced.
                if (now - _rehookRequestedMs < RehookTimeoutMs) return;
                StartHookThread(0);
                return;
            }
            if (IsHookThreadAlive()) return;   // hooked, or still starting up
            if (now - _lastThreadRestartMs < ThreadRestartDelayMs) return;
            if (!ReinstallBudgetLeft(now)) return;
            StartHookThread(0);
        }

        /// <summary>
        /// Records one re-install attempt. When too many happened recently the
        /// hook is given up: it keeps missing presses that polling sees (or
        /// cannot be set at all), so polling is the more reliable source on
        /// this setup and the tracker stays on it.
        /// </summary>
        private bool ReinstallBudgetLeft(long now)
        {
            _reinstallTimes.Add(now);
            _reinstallTimes.RemoveAll(delegate(long t) { return now - t > ReinstallWindowMs; });
            if (_reinstallTimes.Count <= MaxReinstalls) return true;
            _hookGivenUp = true;
            StopHookThread(0);
            return false;
        }

        private void PollFromHook(bool followMapKey)
        {
            List<KeyEvent> events = new List<KeyEvent>();
            lock (_queueSync)
            {
                while (_queue.Count > 0) events.Add(_queue.Dequeue());
            }

            foreach (KeyEvent e in events)
            {
                if (e.Vk == _mapKey && followMapKey)
                {
                    if (e.Down)
                    {
                        _downAt = e.At;
                        _toggled = !_toggled;
                        _mapHeld = true;
                    }
                    else
                    {
                        // Held to view: the map closes on release.
                        if (e.At - _downAt >= TapLimit) _toggled = false;
                        _mapHeld = false;
                    }
                }
                else if (e.Vk == _waypointKey && e.Down)
                {
                    _toggled = false;
                    _manual = false;
                }
                else if (e.Vk == _toggleKey && e.Down)
                {
                    _manual = !_manual;
                    _manualAt = e.At;
                }
                else if (e.Down && followMapKey && Array.IndexOf(CloseKeys, e.Vk) >= 0)
                {
                    _toggled = false;
                    _manual = false;
                }
            }

            if (!followMapKey) _mapHeld = false;
            bool waypointDown;
            lock (_queueSync) waypointDown = _hookDown[_waypointKey & 0xFF];
            Visible = (_manual || _toggled || _mapHeld) && !waypointDown;
        }

        private void PollFromKeyState(bool followMapKey)
        {
            DateTime now = DateTime.UtcNow;
            bool down = followMapKey && IsDown(_mapKey);
            bool closeKey = false;
            if (followMapKey)
            {
                foreach (int vk in CloseKeys)
                {
                    if (IsDown(vk)) { closeKey = true; break; }
                }
            }
            bool waypoint = IsDown(_waypointKey);
            bool toggle = IsDown(_toggleKey);

            if (down && !_wasDown)
            {
                _downAt = now;
                _toggled = !_toggled;
            }
            if (!down && _wasDown)
            {
                TimeSpan held = now - _downAt;
                // If held down to view, close on release
                if (held >= TapLimit) _toggled = false;
            }
            _wasDown = down;

            // UI / game keys close the map
            if (closeKey && !_wasEscapeDown)
            {
                _toggled = false;
                _manual = false;
            }
            _wasEscapeDown = closeKey;

            if (waypoint && !_wasWaypointDown)
            {
                _toggled = false;
                _manual = false;
            }
            _wasWaypointDown = waypoint;

            if (toggle && !_wasToggleDown)
            {
                _manual = !_manual;
                _manualAt = now;
            }
            _wasToggleDown = toggle;

            Visible = (_manual || _toggled || down) && !waypoint;
        }

        /// <summary>Forgets the mirrored state, e.g. when the game closes.</summary>
        public void Reset()
        {
            _toggled = false;
            _manual = false;
            _wasDown = false;
            _wasEscapeDown = false;
            _wasToggleDown = false;
            _wasWaypointDown = false;
            _mapHeld = false;
            // The live key state is read before taking the lock, so the hook
            // callback never waits for 255 GetAsyncKeyState calls.
            bool[] live = null;
            if (!_resetSinceForeground) live = ReadAllKeys();
            lock (_queueSync)
            {
                _queue.Clear();
                // The hook's key mirror can be stale: a key-up is lost when the
                // hook was dropped or re-installed, and low-level hooks never
                // see keys released on the secure desktop (Ctrl+Alt+Del, UAC).
                // A stuck "waypoint key down" would hide the overlay for good,
                // so the mirror is re-read from the live key state. Reset runs
                // on every tick while the game is in the background, hence only
                // once per stay there.
                if (live != null) Array.Copy(live, _hookDown, _hookDown.Length);
            }
            _resetSinceForeground = true;
            _probeSeeded = false;
            _pendingSinceMs = -1;
            Visible = false;
        }

        private static bool[] ReadAllKeys()
        {
            bool[] keys = new bool[256];
            for (int vk = 1; vk < 256; vk++) keys[vk] = IsDown(vk);
            return keys;
        }

        private static long NowMs()
        {
            return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
        }

        private static bool IsDown(int vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }
    }
}
