using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>Thin wrapper around Win32 input APIs.</summary>
    public static class InputSender
    {
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Looking the game process up (GetProcessesByName + MainWindowHandle)
        // costs ~12-15 ms. The UI calls these helpers several times per frame,
        // so the result is cached. A found window is kept for as long as it
        // exists and still belongs to the same process (two cheap user32
        // calls); the processes are only enumerated again when the window is
        // gone, or every 500 ms while the game is not running (TH-6).
        private static readonly object CacheLock = new object();
        private static IntPtr _cachedHwnd = IntPtr.Zero;
        private static uint _cachedPid;
        private static long _cachedAt;
        private static bool _refreshing;
        private const long CacheTicks = 500L * 10000L;

        private static void RefreshGameCache()
        {
            long now = DateTime.UtcNow.Ticks;
            IntPtr hwnd;
            uint pid;
            lock (CacheLock)
            {
                // Another thread is enumerating right now: answer with the
                // previous result instead of waiting or reporting "not running".
                if (_refreshing) return;
                hwnd = _cachedHwnd;
                pid = _cachedPid;
                if (hwnd == IntPtr.Zero && _cachedAt != 0 && now - _cachedAt < CacheTicks)
                    return;
            }

            if (hwnd != IntPtr.Zero && WindowBelongsTo(hwnd, pid))
            {
                // A hidden window (a splash screen the game shows while it
                // starts) may not be the real one yet, so it only gets the
                // short cache lifetime. A visible window, minimized or not,
                // is kept indefinitely.
                bool visible;
                try { visible = IsWindowVisible(hwnd); }
                catch { visible = true; }
                if (visible || now - _cachedAt < CacheTicks) return;
            }

            lock (CacheLock)
            {
                if (_refreshing) return;
                _refreshing = true;
            }

            Process p = null;
            IntPtr newHwnd = IntPtr.Zero;
            uint newPid = 0;
            try
            {
                p = FindGameProcessUncached();
                if (p != null)
                {
                    newPid = (uint)p.Id;
                    newHwnd = p.MainWindowHandle;
                }
            }
            catch { }
            finally { if (p != null) p.Dispose(); }

            lock (CacheLock)
            {
                // The values are replaced only now, so concurrent callers keep
                // seeing the previous state during the enumeration rather than
                // a transient "game not running".
                _cachedHwnd = newHwnd;
                _cachedPid = newPid;
                _cachedAt = DateTime.UtcNow.Ticks;
                _refreshing = false;
            }
        }

        /// <summary>True while the window exists and is owned by the given process.</summary>
        private static bool WindowBelongsTo(IntPtr hwnd, uint pid)
        {
            if (hwnd == IntPtr.Zero || pid == 0) return false;
            try
            {
                if (!IsWindow(hwnd)) return false;
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                return owner == pid;
            }
            catch { return false; }
        }

        /// <summary>Cached handle of the game window, false when the game is not running.</summary>
        public static bool TryGetGameWindow(out IntPtr hwnd)
        {
            RefreshGameCache();
            lock (CacheLock) hwnd = _cachedHwnd;
            return hwnd != IntPtr.Zero;
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint milliseconds);

        private static readonly Random Jitter = new Random();
        private static readonly object JitterLock = new object();

        /// <summary>
        /// Raises the timer resolution to one millisecond for the duration of a
        /// macro. Without it Windows rounds every sleep up to about 15 ms, so a
        /// delay of 1-5 ms would not be honoured at all.
        /// </summary>
        public static void BeginPreciseTiming()
        {
            try { TimeBeginPeriod(1); }
            catch { }
        }

        public static void EndPreciseTiming()
        {
            try { TimeEndPeriod(1); }
            catch { }
        }

        /// <summary>Random integer in [min, max], inclusive.</summary>
        public static int NextRandom(int min, int max)
        {
            if (max <= min) return min;
            lock (JitterLock) return Jitter.Next(min, max + 1);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>True while the given virtual key is held down or was pressed recently.</summary>
        public static bool IsKeyDown(int virtualKey)
        {
            try { return (GetAsyncKeyState(virtualKey) & 0x8001) != 0; }
            catch { return false; }
        }

        /// <summary>Clears any latched transition bit for the virtual key.</summary>
        public static void ConsumeKeyPress(int virtualKey)
        {
            try { GetAsyncKeyState(virtualKey); }
            catch { }
        }

        /// <summary>True if the virtual key is currently down or was pressed since the last check.</summary>
        public static bool WasKeyPressedOrDown(int virtualKey)
        {
            try { return (GetAsyncKeyState(virtualKey) & 0x8001) != 0; }
            catch { return false; }
        }

        /// <summary>Reads the current cursor position in screen coordinates.</summary>
        public static bool TryGetCursorPosition(out System.Drawing.Point point)
        {
            point = System.Drawing.Point.Empty;
            try
            {
                POINT p;
                if (GetCursorPos(out p))
                {
                    point = new System.Drawing.Point(p.X, p.Y);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Returns the current screen cursor position.</summary>
        public static System.Drawing.Point GetCursorPosition()
        {
            System.Drawing.Point p;
            if (TryGetCursorPosition(out p)) return p;
            return System.Drawing.Point.Empty;
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        /// <summary>
        /// Screen position and size of the game client area.
        /// Returns false when the game window is not available.
        /// </summary>
        public static bool TryGetGameClientRect(out System.Drawing.Rectangle screenRect)
        {
            screenRect = System.Drawing.Rectangle.Empty;
            IntPtr hwnd;
            if (!TryGetGameWindow(out hwnd)) return false;
            try
            {

                RECT rect;
                if (!GetClientRect(hwnd, out rect)) return false;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return false;

                POINT origin = new POINT();
                origin.X = 0;
                origin.Y = 0;
                if (!ClientToScreen(hwnd, ref origin)) return false;

                screenRect = new System.Drawing.Rectangle(origin.X, origin.Y, width, height);
                return true;
            }
            catch { return false; }
        }

        public static readonly string[] GameProcessNames =
        {
            "Diablo III", "Diablo III64", "Diablo III Public Test", "Diablo III64 Public Test"
        };

        public static bool MoveTo(int x, int y)
        {
            return SetCursorPos(x, y);
        }

        public static void Click(bool right)
        {
            uint down = right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            uint up = right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
            mouse_event(down, 0, 0, 0, UIntPtr.Zero);
            // The button is held for a slightly random time, the way a hand
            // presses a mouse. A fixed 15 ms hold is a very recognisable pattern.
            Thread.Sleep(NextRandom(9, 21));
            mouse_event(up, 0, 0, 0, UIntPtr.Zero);
        }

        public static void KeyPress(int virtualKey)
        {
            byte vk = (byte)(virtualKey & 0xFF);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            Thread.Sleep(NextRandom(9, 19));
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static int ParseKey(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Пустой код клавиши.");
            string t = text.Trim();
            int numeric;
            if (int.TryParse(t, out numeric))
            {
                if (numeric < 1 || numeric > 255) throw new ArgumentException("Код клавиши должен быть 1..255.");
                return numeric;
            }

            string name = t.ToUpperInvariant();
            switch (name)
            {
                case "ESC": name = "ESCAPE"; break;
                case "ENTER": name = "ENTER"; break;
                case "RETURN": name = "ENTER"; break;
                case "SPACE": name = "SPACE"; break;
                case "PGUP": name = "PAGEUP"; break;
                case "PGDN": name = "PAGEDOWN"; break;
                case "DEL": name = "DELETE"; break;
                case "INS": name = "INSERT"; break;
                case "UP": name = "UP"; break;
                case "DOWN": name = "DOWN"; break;
                case "LEFT": name = "LEFT"; break;
                case "RIGHT": name = "RIGHT"; break;
            }
            if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), name, true);
                return (int)key;
            }
            catch
            {
                throw new ArgumentException("Неизвестная клавиша: " + text);
            }
        }

        public static Process FindGameProcess()
        {
            return FindGameProcessUncached();
        }

        private static Process FindGameProcessUncached()
        {
            foreach (string name in GameProcessNames)
            {
                Process[] list;
                try { list = Process.GetProcessesByName(name); }
                catch { continue; }
                // Prefer the process that has a window; otherwise the first one.
                // Every other Process object is disposed, since each holds a
                // native handle that only the finalizer would release.
                Process chosen = null;
                for (int i = 0; i < list.Length; i++)
                {
                    try
                    {
                        if (list[i].MainWindowHandle != IntPtr.Zero) { chosen = list[i]; break; }
                    }
                    catch { }
                }
                if (chosen == null && list.Length > 0) chosen = list[0];
                foreach (Process p in list)
                    if (!ReferenceEquals(p, chosen)) p.Dispose();
                if (chosen != null) return chosen;
            }
            return null;
        }

        public static bool IsGameRunning()
        {
            RefreshGameCache();
            lock (CacheLock) return _cachedPid != 0;
        }

        public static bool IsGameForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == 0) return false;
            RefreshGameCache();
            lock (CacheLock) return _cachedPid == pid;
        }

        public static bool FocusGame()
        {
            Process p = FindGameProcess();
            if (p == null) return false;
            try
            {
                IntPtr h = p.MainWindowHandle;
                if (h == IntPtr.Zero) return false;
                if (IsIconic(h)) ShowWindow(h, 9);
                SetForegroundWindow(h);
                Thread.Sleep(200);
                return true;
            }
            catch { return false; }
            finally { p.Dispose(); }
        }
    }
}


