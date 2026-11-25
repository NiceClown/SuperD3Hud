using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SuperD3Hud.Game
{
    /// <summary>Which HUD parts are read from the game.</summary>
    public class GameFeatures
    {
        public bool Actors = true;
        public bool Buffs = true;
        public bool Skills = true;
        public bool Inventory = true;

        public GameFeatures Clone()
        {
            return new GameFeatures
            {
                Actors = Actors,
                Buffs = Buffs,
                Skills = Skills,
                Inventory = Inventory
            };
        }
    }

    /// <summary>
    /// Polls the game in the background and keeps the latest snapshots of
    /// monsters, buffs, skills and inventory.
    ///
    /// The service owns one read-only handle per game session. Heavy reads
    /// (actors, inventory) run at a lower rate than light ones (player, buffs,
    /// skills), so the HUD stays responsive without hammering the game.
    /// Everything fails soft: a bad frame only updates the status text.
    /// </summary>
    public class GameService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly ActorModule _actors = new ActorModule();
        private readonly BuffModule _buffs = new BuffModule();
        private readonly SkillModule _skills = new SkillModule();
        private readonly InventoryModule _inventory = new InventoryModule();
        private readonly CooldownReader _cooldowns = new CooldownReader();
        private readonly GameFeatures _features = new GameFeatures();

        private Thread _worker;
        private volatile bool _running;
        private volatile bool _cancel;

        private GameMemoryReader _memory;
        private int _processId;
        private IntPtr _baseAddress;
        private long _sessionStartTicks;

        private string _status = "Ожидание игры...";
        private string _lastError = "";
        private int _revision;

        private DateTime _nextFast = DateTime.MinValue;
        private DateTime _nextActors = DateTime.MinValue;
        private DateTime _nextInventory = DateTime.MinValue;
        private DateTime _nextSessionCheck = DateTime.MinValue;

        public event Action<string> Log;

        public GameFeatures Features { get { return _features; } }

        public bool IsRunning { get { return _running; } }

        public int Revision { get { lock (_sync) return _revision; } }

        public string Status { get { lock (_sync) return _status; } }

        public string LastError { get { lock (_sync) return _lastError; } }

        public bool GameRunning
        {
            get
            {
                try { return InputSender.IsGameRunning(); }
                catch { return false; }
            }
        }

        public bool IsAttached { get { return _memory != null; } }

        /// <summary>Snapshot of every module, safe to read from the UI thread.</summary>
        public void GetState(out ActorSnapshot actors, out BuffSnapshot buffs,
                             out SkillSnapshot skills, out InventorySnapshot inventory)
        {
            lock (_sync)
            {
                actors = _actors.Snapshot;
                buffs = _buffs.Snapshot;
                skills = _skills.Snapshot;
                inventory = _inventory.Snapshot;
            }
        }

        public ActorSnapshot Actors { get { lock (_sync) return _actors.Snapshot; } }

        public BuffSnapshot Buffs { get { lock (_sync) return _buffs.Snapshot; } }

        public SkillSnapshot Skills { get { lock (_sync) return _skills.Snapshot; } }

        public InventorySnapshot Inventory { get { lock (_sync) return _inventory.Snapshot; } }

        // ------------------------------------------------------------------
        // Player position
        // ------------------------------------------------------------------

        private const double PlayerSmoothTauSec = 0.045;
        private const float PlayerSnapDistSq = 35f * 35f;

        private readonly Stopwatch _playerClock = Stopwatch.StartNew();
        private float _playerX;
        private float _playerY;
        private float _displayPlayerX;
        private float _displayPlayerY;
        private double _lastSmoothSec;
        private bool _displayInitialized;
        private bool _playerKnown;

        /// <summary>
        /// The player position for drawing. Exponentially tracks the latest
        /// memory read using high-resolution Stopwatch elapsed time, eliminating
        /// overshoot on stops/turns while gliding smoothly at 60 Hz.
        /// </summary>
        public void GetSmoothPlayer(out float x, out float y)
        {
            lock (_sync)
            {
                if (!_playerKnown)
                {
                    x = _playerX;
                    y = _playerY;
                    return;
                }

                double nowSec = _playerClock.Elapsed.TotalSeconds;
                if (!_displayInitialized)
                {
                    _displayPlayerX = _playerX;
                    _displayPlayerY = _playerY;
                    _displayInitialized = true;
                    _lastSmoothSec = nowSec;
                }
                else
                {
                    double dt = nowSec - _lastSmoothSec;
                    _lastSmoothSec = nowSec;

                    float dx = _playerX - _displayPlayerX;
                    float dy = _playerY - _displayPlayerY;
                    float distSq = dx * dx + dy * dy;

                    if (distSq > PlayerSnapDistSq || dt > 0.5)
                    {
                        _displayPlayerX = _playerX;
                        _displayPlayerY = _playerY;
                    }
                    else if (distSq < 1e-6f)
                    {
                        _displayPlayerX = _playerX;
                        _displayPlayerY = _playerY;
                    }
                    else if (dt > 0.0)
                    {
                        double alpha = 1.0 - Math.Exp(-dt / PlayerSmoothTauSec);
                        if (alpha > 1.0) alpha = 1.0;
                        _displayPlayerX += (float)(dx * alpha);
                        _displayPlayerY += (float)(dy * alpha);
                    }
                }

                x = _displayPlayerX;
                y = _displayPlayerY;
            }
        }

        /// <summary>True when the player position is known.</summary>
        public bool PlayerKnown { get { lock (_sync) return _playerKnown; } }

        private void RememberPlayer(float x, float y)
        {
            lock (_sync)
            {
                _playerX = x;
                _playerY = y;
                if (!_playerKnown || !_displayInitialized)
                {
                    _displayPlayerX = x;
                    _displayPlayerY = y;
                    _displayInitialized = true;
                    _lastSmoothSec = _playerClock.Elapsed.TotalSeconds;
                }
                else
                {
                    float dx = x - _displayPlayerX;
                    float dy = y - _displayPlayerY;
                    if (dx * dx + dy * dy > PlayerSnapDistSq)
                    {
                        _displayPlayerX = x;
                        _displayPlayerY = y;
                        _lastSmoothSec = _playerClock.Elapsed.TotalSeconds;
                    }
                }
                _playerKnown = true;
            }
        }

        /// <summary>Feeds a pressed key to the cooldown tracker.</summary>
        public void OnKey(int virtualKey)
        {
            try { _skills.OnKey(virtualKey); }
            catch { }

            try
            {
                int slot = _skills.SlotForKey(virtualKey);
                if (slot >= 0) _cooldowns.OnKey(slot);
            }
            catch { }
        }

        /// <summary>Sets the real cooldown of one skill slot.</summary>
        public void SetCooldown(int slot, double seconds)
        {
            try { _skills.SetCooldown(slot, seconds); }
            catch { }
        }

        public double GetCooldown(int slot)
        {
            try { return _skills.GetCooldown(slot); }
            catch { return 0; }
        }

        /// <summary>Descriptor pair currently used to read cooldowns, 0 when unknown.</summary>
        public int CooldownDescriptor { get { return _cooldowns.DurationDescriptor; } }

        public int CooldownStartDescriptor { get { return _cooldowns.StartDescriptor; } }

        /// <summary>Human readable state of the cooldown reader.</summary>
        public string CooldownStatus { get { return _cooldowns.Status; } }

        /// <summary>True when cooldowns are read from memory instead of estimated.</summary>
        public bool CooldownsFromMemory { get { return _cooldowns.IsActive; } }

        /// <summary>Restores the descriptor pair found in a previous session.</summary>
        public void RestoreCooldowns(int durationDescriptor, int startDescriptor)
        {
            try { _cooldowns.Restore(durationDescriptor, startDescriptor); }
            catch { }
        }

        /// <summary>Forgets the descriptor pair and searches again.</summary>
        public void ForgetCooldowns()
        {
            try { _cooldowns.Forget(); }
            catch { }
        }

        /// <summary>Remaining cooldown seconds per skill slot, read from the game.</summary>
        public Dictionary<int, double> CooldownRemaining()
        {
            try
            {
                Dictionary<int, double> mem = _cooldowns.RemainingBySlot();
                if (mem != null && mem.Count > 0) return mem;

                Dictionary<int, double> result = new Dictionary<int, double>();
                SkillSnapshot snap = _skills.Snapshot;
                if (snap != null && snap.Skills != null)
                {
                    for (int i = 0; i < snap.Skills.Count; i++)
                    {
                        SkillCooldown sc = snap.Skills[i];
                        if (sc != null && sc.RemainingSeconds > 0.05)
                            result[sc.Slot] = sc.RemainingSeconds;
                    }
                }
                return result;
            }
            catch { return new Dictionary<int, double>(); }
        }

        /// <summary>Cooldown duration seconds per skill slot.</summary>
        public Dictionary<int, double> CooldownDurations()
        {
            try
            {
                Dictionary<int, double> mem = _cooldowns.DurationBySlot();
                if (mem != null && mem.Count > 0) return mem;

                Dictionary<int, double> result = new Dictionary<int, double>();
                SkillSnapshot snap = _skills.Snapshot;
                if (snap != null && snap.Skills != null)
                {
                    for (int i = 0; i < snap.Skills.Count; i++)
                    {
                        SkillCooldown sc = snap.Skills[i];
                        if (sc != null && sc.DurationSeconds > 0.05)
                            result[sc.Slot] = sc.DurationSeconds;
                    }
                }
                return result;
            }
            catch { return new Dictionary<int, double>(); }
        }

        /// <summary>Cooldown finish tick per skill slot, read from the game.</summary>
        public Dictionary<int, int> CooldownFinishTicks()
        {
            try
            {
                Dictionary<int, int> mem = _cooldowns.FinishTickBySlot();
                if (mem != null && mem.Count > 0) return mem;

                Dictionary<int, int> result = new Dictionary<int, int>();
                SkillSnapshot snap = _skills.Snapshot;
                if (snap != null && snap.Skills != null)
                {
                    for (int i = 0; i < snap.Skills.Count; i++)
                    {
                        SkillCooldown sc = snap.Skills[i];
                        if (sc != null && sc.FinishTick > 0)
                            result[sc.Slot] = sc.FinishTick;
                    }
                }
                return result;
            }
            catch { return new Dictionary<int, int>(); }
        }

        /// <summary>Current internal game tick counter (60 ticks/second).</summary>
        public int GameTick
        {
            get
            {
                try { return _reader != null ? _reader.ReadGameTick() : 0; }
                catch { return 0; }
            }
        }

        public void Start()
        {
            if (_running) return;
            _cancel = false;
            _running = true;
            _worker = new Thread(Loop);
            _worker.IsBackground = true;
            _worker.Name = "GameService";
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

        /// <summary>Forces the next tick to read everything again.</summary>
        public void ApplySettings()
        {
            _nextFast = DateTime.MinValue;
            _nextActors = DateTime.MinValue;
            _nextInventory = DateTime.MinValue;
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
                    Sleep(1000);
                }
            }
            CloseSession();
            _running = false;
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;

            if (_memory == null || now >= _nextSessionCheck)
            {
                _nextSessionCheck = now.AddMilliseconds(1000);
                if (!EnsureSession()) return;
            }

            int world = 0;
            int slot = -1;
            try
            {
                ActorRecord player;
                if (_memory != null && _reader != null && _reader.TryReadLocalPlayer(out player))
                {
                    world = player.WorldId;
                    slot = _reader.LocalPlayerSlot;
                    // The position is read every loop, so the overlays can
                    // follow the character without waiting for the slow
                    // actor poll.
                    if (!float.IsNaN(player.X) && !float.IsNaN(player.Y))
                        RememberPlayer(player.X, player.Y);
                }
            }
            catch { }

            if (now >= _nextFast)
            {
                _nextFast = now.AddMilliseconds(25);
                PollFast(world, slot);
            }

            if (now >= _nextActors)
            {
                // The monsters are read often enough that a marker does not
                // lag behind; the movement between two reads is continued by
                // the overlays.
                _nextActors = now.AddMilliseconds(120);
                PollActors(world);
            }

            if (now >= _nextInventory)
            {
                _nextInventory = now.AddMilliseconds(900);
                PollInventory(world);
            }

            Sleep(15);
        }

        private GameReader _reader;

        private bool EnsureSession()
        {
            Process game = FindGameProcess();
            if (game == null)
            {
                SetStatus("Игра не запущена.");
                CloseSession();
                Sleep(800);
                return false;
            }

            try
            {
                int pid = game.Id;
                IntPtr moduleBase = game.MainModule.BaseAddress;
                long startTicks;
                try { startTicks = game.StartTime.ToUniversalTime().Ticks; }
                catch { startTicks = 0; }

                bool same = _memory != null && pid == _processId && startTicks == _sessionStartTicks;
                if (!same)
                {
                    CloseSession();
                    _memory = new GameMemoryReader(pid);
                    _processId = pid;
                    _baseAddress = moduleBase;
                    _sessionStartTicks = startTicks;
                    _reader = new GameReader(_memory, _baseAddress);

                    _actors.Reset();
                    _buffs.Reset();
                    _skills.Reset();
                    _inventory.Reset();
                    _nextFast = DateTime.MinValue;
                    _nextActors = DateTime.MinValue;
                    _nextInventory = DateTime.MinValue;
                    SetStatus("Подключено к игре.");
                }
                return _reader != null;
            }
            catch (Exception ex)
            {
                CloseSession();
                SetError(ex.Message);
                Sleep(800);
                return false;
            }
            finally { game.Dispose(); }
        }

        private void PollFast(int world, int slot)
        {
            GameReader reader = _reader;
            if (reader == null) return;

            try
            {
                if (_features.Buffs)
                {
                    BuffSnapshot buffs = _buffs.Poll(reader, world, slot);
                    lock (_sync) { _revision++; }
                }
                if (_features.Skills)
                {
                    ActorRecord player;
                    if (reader.TryReadLocalPlayer(out player))
                    {
                        // Real cooldowns, when the descriptor pair is known,
                        // override the input estimate inside the skill module.
                        int gameTick = reader.ReadGameTick();
                        GameReader.EquippedSkill[] equipped = reader.ReadEquippedSkills();
                        _cooldowns.Poll(reader, player, gameTick, equipped);
                        _skills.ApplyMemoryCooldowns(
                            _cooldowns.RemainingBySlot(), _cooldowns.DurationBySlot(), _cooldowns.FinishTickBySlot());
                    }
                    _skills.Poll(reader, slot);
                    lock (_sync) { _revision++; }
                }

                lock (_sync)
                {
                    if (string.IsNullOrEmpty(_lastError)) _status = "HUD работает.";
                }
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }

        private void PollActors(int world)
        {
            if (!_features.Actors) return;
            GameReader reader = _reader;
            if (reader == null || world == 0) return;

            try
            {
                _actors.Poll(reader, world, true);
                lock (_sync) { _revision++; }
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }

        private void PollInventory(int world)
        {
            if (!_features.Inventory) return;
            GameReader reader = _reader;
            if (reader == null) return;

            try
            {
                _inventory.Poll(reader, world);
                lock (_sync) { _revision++; }
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
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
                    try { handler("HUD: " + message); }
                    catch { }
                }
            }
            SetStatus(message);
        }

        private void CloseSession()
        {
            GameMemoryReader memory = _memory;
            _memory = null;
            _reader = null;
            _processId = 0;
            _baseAddress = IntPtr.Zero;
            _sessionStartTicks = 0;
            lock (_sync)
            {
                _playerKnown = false;
                _displayInitialized = false;
                _lastSmoothSec = 0.0;
            }
            if (memory != null)
            {
                try { memory.Dispose(); }
                catch { }
            }
        }

        private static Process FindGameProcess()
        {
            foreach (string name in new string[] { "Diablo III64", "Diablo III" })
            {
                Process[] list;
                try { list = Process.GetProcessesByName(name); }
                catch { continue; }
                foreach (Process p in list)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) return p;
                    }
                    catch { }
                }
                if (list.Length > 0) return list[0];
            }
            return null;
        }

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
