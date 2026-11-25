using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SuperD3Hud
{
    /// <summary>
    /// Drives the map: it captures the game state on a background thread and
    /// stores the result. The overlay window is owned by the UI thread and
    /// pulls the data from here, because WinForms windows may only be touched
    /// from the thread that created them.
    ///
    /// The reader is strictly read-only and is bound to one game session at a
    /// time: when the game restarts, the old process handle is dropped and a
    /// new one is opened on the next tick.
    /// </summary>
    public class MapService : IDisposable
    {
        private readonly MapSettings _settings;
        private readonly object _sync = new object();

        private Thread _worker;
        private volatile bool _running;
        private volatile bool _cancel;

        private List<MapPolygon> _polygons = new List<MapPolygon>();
        private MapPoint _player;
        private bool _hasPlayer;
        private string _status = "Ожидание игры...";
        private string _lastError = "";
        private int _revision;

        private DateTime _nextSceneCapture = DateTime.MinValue;
        private DateTime _nextPlayerCapture = DateTime.MinValue;
        private DateTime _nextSessionCheck = DateTime.MinValue;
        private MapSnapshot _snapshot;
        private int _walkableCount;
        private int _approximateCount;
        private int _worldId;
        private readonly Dictionary<int, SceneAsset> _assetCache = new Dictionary<int, SceneAsset>();

        // One open handle per game session. Reopening the process ten times a
        // second would be wasteful, so the handle is kept until it goes stale.
        private GameMemoryReader _memory;
        private int _processId;
        private IntPtr _baseAddress;
        private long _sessionStartTicks;
        private string _gameVersion = "";
        private string _gameHash = "";

        // Handle used only to ask whether the session's process is still alive.
        // Holding it also pins the process object, so a reused pid can never be
        // mistaken for the old game. It replaces a GetProcessesByName plus a full
        // module enumeration four times a second, which could also fail while
        // the game was loading and tear the session down (MR-6).
        private SafeProcessHandle _liveHandle;

        // The asset cache holds navmesh geometry of the game process it was
        // filled from. It survives transient read errors (the session closes
        // and reopens on the same process) and is only dropped when a
        // different process is opened.
        private int _assetCachePid;
        private long _assetCacheStartTicks;

        // World the current polygons were built for, and the number of failed
        // geometry captures in a row. Both are touched by the worker only.
        private int _builtWorldId;
        private bool _builtWorldFromPlayer;
        private int _captureFailures;

        // Build support is computed by the worker and only read by the UI.
        // Computing it on the UI thread meant file-system probes every frame
        // and, on a cache miss, a SHA-256 of the 26 MB executable (MR-5, TH-7).
        private volatile bool _buildSupported;
        private volatile bool _buildChecked;
        private volatile string _gameExecutable = "";
        private DateTime _nextBuildCheck = DateTime.MinValue;

        public event Action<string> Log;

        public MapService(MapSettings settings)
        {
            _settings = settings;
        }

        public MapSettings Settings { get { return _settings; } }

        /// <summary>Incremented whenever the visible state changed.</summary>
        public int Revision
        {
            get { lock (_sync) return _revision; }
        }

        /// <summary>Snapshot of the current map state, safe to read from the UI thread.</summary>
        public void GetState(out List<MapPolygon> polygons, out MapPoint player,
                             out bool hasPlayer, out string status)
        {
            lock (_sync)
            {
                polygons = _polygons;
                player = _player;
                hasPlayer = _hasPlayer;
                status = _status;
            }
        }

        /// <summary>Small summary used by the plugin host.</summary>
        public void GetSummary(out int sceneCount, out int cellCount, out int worldId)
        {
            lock (_sync)
            {
                sceneCount = _snapshot == null ? 0 : _snapshot.Scenes.Count;
                cellCount = _walkableCount;
                worldId = _worldId;
            }
        }

        /// <summary>Number of scene polygons drawn from bounds because navmesh
        /// assets were not available in the current capture.</summary>
        public int ApproximateCount { get { lock (_sync) return _approximateCount; } }

        public string Status { get { lock (_sync) return _status; } }

        public string LastError { get { lock (_sync) return _lastError; } }

        public bool IsRunning { get { return _running; } }

        /// <summary>
        /// True when the installed game build is supported by the reader.
        /// This is a plain volatile read of the value the worker computed, so
        /// it is safe to call every frame. It is false until the worker has
        /// checked the build for the first time (see <see cref="BuildChecked"/>).
        /// </summary>
        public bool BuildSupported
        {
            get { return _buildSupported; }
        }

        /// <summary>True once the worker has checked the installed build at least once.</summary>
        public bool BuildChecked
        {
            get { return _buildChecked; }
        }

        /// <summary>Path of the client executable, as last found by the worker.</summary>
        public string GameExecutable
        {
            get
            {
                if (_buildChecked) return _gameExecutable ?? "";
                // Before the worker's first check, fall back to the file probes
                // (no hashing), so the settings page does not claim the game
                // is missing during the first moments after start.
                try { return MapReader.FindGameExecutable(); }
                catch { return ""; }
            }
        }

        public void Start()
        {
            if (_running) return;
            _cancel = false;
            _running = true;
            _worker = new Thread(Loop);
            _worker.IsBackground = true;
            _worker.Name = "MapService";
            _worker.Start();
        }

        public void Stop()
        {
            _cancel = true;
            Thread t = _worker;
            if (t != null && t.IsAlive) t.Join(2000);
            _running = false;
            CloseSession();
        }

        /// <summary>Forces the next tick to re-read the geometry.</summary>
        public void ApplySettings()
        {
            lock (_sync)
            {
                _nextSceneCapture = DateTime.MinValue;
                _nextPlayerCapture = DateTime.MinValue;
            }
        }

        private void Loop()
        {
            while (!_cancel)
            {
                try { Tick(); }
                catch (ThreadAbortException) { return; }
                catch (Exception ex)
                {
                    SetError(ex.Message);
                    CloseSession();
                    ClearMapState();
                    Sleep(1000);
                }
            }
            CloseSession();
            _running = false;
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;

            if (now < _nextSessionCheck)
            {
                // Nothing to do: sleep until the next scheduled capture.
                DateTime next = _nextSceneCapture < _nextPlayerCapture ? _nextSceneCapture : _nextPlayerCapture;
                if (now < next)
                {
                    int wait = (int)(next - now).TotalMilliseconds;
                    Sleep(Math.Max(10, Math.Min(60, wait)));
                    return;
                }
            }
            _nextSessionCheck = now.AddMilliseconds(250);

            if (!EnsureSession())
                return;

            bool needScene = now >= _nextSceneCapture;
            bool needPlayer = now >= _nextPlayerCapture;
            if (!needScene && !needPlayer) return;

            if (needScene)
            {
                _nextSceneCapture = now.AddSeconds(1.0);
                CaptureGeometry();
            }

            if (needPlayer && _memory != null)
            {
                // The player marker follows the character, so the position is
                // read often; the reader only touches the local actor.
                _nextPlayerCapture = now.AddMilliseconds(25);
                CapturePlayer();
            }
        }

        /// <summary>
        /// Makes sure a read-only handle to the running game is open. Returns
        /// false when the game is not running or the build is unsupported, in
        /// which case the state is updated to explain why.
        ///
        /// While a session is open this costs one GetExitCodeProcess call; the
        /// processes are enumerated again only after the game has exited.
        /// </summary>
        private bool EnsureSession()
        {
            DateTime now = DateTime.UtcNow;

            if (_memory != null)
            {
                if (IsSessionAlive())
                {
                    // The configured path can change while the game runs, so
                    // the build is re-checked now and then. The hash itself is
                    // cached per file identity, so this is only a few file stats.
                    if (now >= _nextBuildCheck) RefreshBuild(now);
                    if (_buildSupported) return true;
                }
                CloseSession();
                ClearMapState();
            }

            RefreshBuild(now);
            if (string.IsNullOrEmpty(_gameExecutable))
            {
                SetStatus("Diablo III не найдена.");
                Sleep(1000);
                return false;
            }

            if (!_buildSupported)
            {
                SetStatus("Сборка игры не поддерживается ридером карты.");
                Sleep(3000);
                return false;
            }

            int pid;
            IntPtr moduleBase;
            string version;
            long startTicks;
            if (!FindGameProcess(out pid, out moduleBase, out version, out startTicks))
            {
                // Also the case while the game is starting and its module list
                // cannot be enumerated yet (ERROR_PARTIAL_COPY): just try later.
                SetStatus("Игра не запущена.");
                Sleep(1000);
                return false;
            }

            try
            {
                OpenSession(pid, moduleBase, version, startTicks);
                return true;
            }
            catch (Exception ex)
            {
                CloseSession();
                SetError(ex.Message);
                Sleep(1000);
                return false;
            }
        }

        /// <summary>Looks the executable up and publishes whether its build is supported.</summary>
        private void RefreshBuild(DateTime now)
        {
            _nextBuildCheck = now.AddSeconds(5);
            string exe = "";
            bool supported = false;
            try
            {
                exe = MapReader.FindGameExecutable() ?? "";
                if (exe.Length > 0) supported = MapReader.IsSupportedBuild(exe);
            }
            catch { supported = false; }
            _gameExecutable = exe;
            _buildSupported = supported;
            _buildChecked = true;
        }

        private void OpenSession(int pid, IntPtr moduleBase, string version, long startTicks)
        {
            CloseSession();
            // PROCESS_QUERY_LIMITED_INFORMATION is all GetExitCodeProcess needs.
            SafeProcessHandle live = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (live == null || live.IsInvalid)
            {
                if (live != null) live.Dispose();
                throw new InvalidOperationException("Не удалось открыть процесс игры.");
            }
            try
            {
                _memory = new GameMemoryReader(pid);
            }
            catch
            {
                live.Dispose();
                throw;
            }
            _liveHandle = live;
            _processId = pid;
            _baseAddress = moduleBase;
            _sessionStartTicks = startTicks;
            _gameVersion = version ?? "";
            _gameHash = MapReader.HashExecutableCached(_gameExecutable);

            // Navmesh assets belong to the process they were read from: keep
            // them when the same game is reopened after a transient error,
            // drop them for a new game process.
            if (pid != _assetCachePid || startTicks != _assetCacheStartTicks)
            {
                lock (_sync) { _assetCache.Clear(); }
                _assetCachePid = pid;
                _assetCacheStartTicks = startTicks;
            }

            lock (_sync) { _snapshot = null; }
            _builtWorldId = 0;
            _captureFailures = 0;
            _nextSceneCapture = DateTime.MinValue;
            _nextPlayerCapture = DateTime.MinValue;
        }

        /// <summary>True while the process of the open session has not exited.</summary>
        private bool IsSessionAlive()
        {
            SafeProcessHandle h = _liveHandle;
            if (h == null || h.IsInvalid || h.IsClosed) return false;
            try
            {
                uint code;
                if (!GetExitCodeProcess(h, out code)) return false;
                return code == StillActive;
            }
            catch { return false; }
        }

        /// <summary>
        /// Drops the drawn map and the player marker and bumps the revision, so
        /// the overlay stops showing the previous world while a new one loads.
        /// </summary>
        private void ClearMapState()
        {
            lock (_sync)
            {
                bool hadContent = _polygons.Count > 0 || _hasPlayer || _snapshot != null;
                _snapshot = null;
                if (hadContent)
                {
                    _polygons = new List<MapPolygon>();
                    _hasPlayer = false;
                    _walkableCount = 0;
                    _approximateCount = 0;
                    _worldId = 0;
                    _revision++;
                }
            }
            _builtWorldId = 0;
        }

        private void CaptureGeometry()
        {
            try
            {
                MapSnapshot snapshot = MapReader.Capture(
                    _memory, _baseAddress, _processId, _gameVersion, _gameHash);

                // Read player world and position BEFORE building geometry
                double px = 0, py = 0;
                bool hasPlayer = false;
                try
                {
                    hasPlayer = PlayerReader.TryRead(_memory, _baseAddress, snapshot, out px, out py);
                }
                catch { }

                // Merge with persistent asset cache so previously seen navcells are retained
                lock (_sync)
                {
                    if (snapshot.Assets != null)
                    {
                        foreach (SceneAsset a in snapshot.Assets)
                        {
                            if (a != null && a.SceneSno > 0 && a.Cells != null && a.Cells.Count > 0)
                                _assetCache[a.SceneSno] = a;
                        }
                    }

                    HashSet<int> presentSnos = new HashSet<int>();
                    if (snapshot.Assets != null)
                        foreach (SceneAsset a in snapshot.Assets) presentSnos.Add(a.SceneSno);

                    foreach (SceneRecord s in snapshot.Scenes)
                    {
                        if (!presentSnos.Contains(s.SceneSno))
                        {
                            SceneAsset cached;
                            if (_assetCache.TryGetValue(s.SceneSno, out cached))
                            {
                                snapshot.Assets.Add(cached);
                                presentSnos.Add(s.SceneSno);
                            }
                        }
                    }
                }

                List<MapPolygon> polygons = MapGeometry.Build(snapshot);
                int walkable = 0;
                int approximate = 0;
                foreach (MapPolygon p in polygons) if (!p.CachedFootprint) walkable++;
                foreach (MapPolygon p in polygons)
                    if (!p.CachedFootprint && p.Approximate) approximate++;
                int world = snapshot.PlayerWorldId != 0 ? snapshot.PlayerWorldId : (snapshot.Scenes.Count > 0 ? snapshot.Scenes[0].WorldId : 0);

                _builtWorldId = world;
                _builtWorldFromPlayer = snapshot.PlayerWorldId != 0;
                _captureFailures = 0;
                lock (_sync)
                {
                    _snapshot = snapshot;
                    _polygons = polygons;
                    _walkableCount = walkable;
                    _approximateCount = approximate;
                    _worldId = world;
                    // The marker always follows this capture: a position kept
                    // from the previous world must not be drawn on the new map.
                    if (hasPlayer) _player = new MapPoint(px, py);
                    _hasPlayer = hasPlayer;
                    _status = BuildStatus(snapshot, polygons);
                    _lastError = "";
                    _revision++;
                }
            }
            catch (MapUnavailableException)
            {
                // Loading screen, menu or the gap between two worlds. This is a
                // normal state, so it is shown as status and not logged as an
                // error: a log line from this thread on every zone change is
                // what first reached the UI log box (MR-1). The old map goes at once.
                _captureFailures++;
                if (!IsSessionAlive())
                {
                    CloseSession();
                    ClearMapState();
                    SetStatus("Игра не запущена.");
                    return;
                }
                ClearMapState();
                lock (_sync) { _lastError = ""; }
                SetStatus("Локация загружается...");
                // Check again soon, so the new map appears right after loading.
                _nextSceneCapture = DateTime.UtcNow.AddMilliseconds(250);
            }
            catch (Exception ex)
            {
                _captureFailures++;
                SetError(ex.Message);
                if (!IsSessionAlive())
                {
                    CloseSession();
                    ClearMapState();
                }
                else if (_captureFailures >= 2)
                {
                    // One bad read while scenes stream in is tolerated; a second
                    // one in a row (about a second later) means the old geometry
                    // can no longer be trusted.
                    ClearMapState();
                }
            }
        }

        private void CapturePlayer()
        {
            MapSnapshot snapshot;
            lock (_sync) snapshot = _snapshot;
            if (_memory == null || snapshot == null) return;

            double px = 0;
            double py = 0;
            bool ok;
            try
            {
                ok = PlayerReader.TryRead(_memory, _baseAddress, snapshot, out px, out py);
            }
            catch
            {
                // A stale handle is expected when the game closes; the next
                // session check reopens it.
                ok = false;
                if (!IsSessionAlive())
                {
                    CloseSession();
                    ClearMapState();
                    return;
                }
            }

            // TryRead stores the player's current world in the snapshot. When it
            // differs from the world the polygons were built for, the player is
            // in another area: drop the old map instead of drawing the new
            // position on top of it, and capture the new geometry right away.
            int playerWorld = snapshot.PlayerWorldId;
            if (ok && playerWorld != 0 && _builtWorldId != 0 && playerWorld != _builtWorldId)
            {
                if (!_builtWorldFromPlayer)
                {
                    // The geometry was built without knowing the player's world
                    // (its first scene's world was used), so the mismatch may be
                    // a guess gone wrong rather than an area change: recapture
                    // now and let that capture decide.
                    _builtWorldFromPlayer = true;
                    _builtWorldId = playerWorld;
                    _nextSceneCapture = DateTime.MinValue;
                    return;
                }
                ClearMapState();
                SetStatus("Локация загружается...");
                _nextSceneCapture = DateTime.MinValue;
                return;
            }

            lock (_sync)
            {
                if (ok)
                {
                    _player = new MapPoint(px, py);
                    _hasPlayer = true;
                }
                else
                {
                    _hasPlayer = false;
                }
                _revision++;
            }
        }

        private static string BuildStatus(MapSnapshot snapshot, List<MapPolygon> polygons)
        {
            int walkable = 0;
            int approximate = 0;
            foreach (MapPolygon p in polygons) if (!p.CachedFootprint) walkable++;
            foreach (MapPolygon p in polygons)
                if (!p.CachedFootprint && p.Approximate) approximate++;
            return string.Format(
                approximate > 0
                    ? "Сцен: {0}, геометрия: {1} (приближённо: {2}), версия игры: {3}"
                    // {3}, not {2}: the argument list is shared with the format
                    // above, and {2} is the approximate count (always 0 here).
                    : "Сцен: {0}, ячеек: {1}, версия игры: {3}",
                snapshot.Scenes.Count, walkable, approximate, snapshot.GameVersion);
        }

        private void SetStatus(string text)
        {
            lock (_sync)
            {
                if (_status == text) return;
                _status = text;
                _revision++;
            }
        }

        private void SetError(string message)
        {
            bool changed;
            lock (_sync)
            {
                changed = _lastError != message;
                _lastError = message;
                _revision++;
            }
            if (changed)
            {
                Action<string> handler = Log;
                if (handler != null)
                {
                    try { handler("Карта: " + message); }
                    catch { }
                }
            }
            SetStatus(message);
        }

        private void CloseSession()
        {
            GameMemoryReader memory = _memory;
            _memory = null;
            _processId = 0;
            _baseAddress = IntPtr.Zero;
            _sessionStartTicks = 0;
            _gameVersion = "";
            _gameHash = "";
            // The asset cache is deliberately kept: a transient read error
            // closes the session, and re-reading every navmesh afterwards would
            // be wasted work. OpenSession drops it when the process changes.
            if (memory != null)
            {
                try { memory.Dispose(); }
                catch { }
            }
            SafeProcessHandle live = _liveHandle;
            _liveHandle = null;
            if (live != null)
            {
                try { live.Dispose(); }
                catch { }
            }
        }

        /// <summary>
        /// Finds the running game and reads its main module once. Every
        /// Process object returned by GetProcessesByName is disposed, because
        /// each one holds a native handle until the finalizer runs.
        /// </summary>
        private static bool FindGameProcess(out int pid, out IntPtr moduleBase,
                                            out string version, out long startTicks)
        {
            pid = 0;
            moduleBase = IntPtr.Zero;
            version = "";
            startTicks = 0;
            foreach (string name in new string[] { "Diablo III64", "Diablo III" })
            {
                Process[] list;
                try { list = Process.GetProcessesByName(name); }
                catch { continue; }
                bool found = false;
                foreach (Process p in list)
                {
                    try
                    {
                        if (found) continue;
                        // MainModule enumerates the modules on every access in
                        // .NET Framework, so it is read exactly once.
                        ProcessModule module = p.MainModule;
                        if (module == null) continue;
                        IntPtr baseAddress = module.BaseAddress;
                        string fileVersion;
                        try { fileVersion = module.FileVersionInfo.FileVersion ?? ""; }
                        catch { fileVersion = ""; }
                        long start;
                        try { start = p.StartTime.ToUniversalTime().Ticks; }
                        catch { start = 0; }
                        pid = p.Id;
                        moduleBase = baseAddress;
                        version = fileVersion;
                        startTicks = start;
                        found = true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                if (found) return true;
            }
            return false;
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint StillActive = 259;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        private static void Sleep(int ms)
        {
            int left = ms;
            while (left > 0)
            {
                Thread.Sleep(Math.Min(50, left));
                left -= 50;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
