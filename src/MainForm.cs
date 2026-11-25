using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public class MainForm : Form
    {
        private Panel _content;
        private Label _status;
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        private readonly Dictionary<string, Button> _nav = new Dictionary<string, Button>();
        private readonly List<Control> _advancedNav = new List<Control>();
        private CheckBox _advancedToggle;
        private bool _navLoading;
        private string _activePage = "";
        private HotkeyManager _hotkeys;
        private NotifyIcon _tray;
        private bool _reallyExit;
        private bool _shuttingDown;
        private bool _timerResolutionRaised;
        private DashboardPage _dashboardPage;
        private PluginsPage _pluginsPage;
        private MacrosPage _macrosPage;
        private ProfilesPage _profilesPage;
        private HudPage _hudPage;
        private HudFilesPage _hudFilesPage;
        private MapPage _mapPage;
        private HudOverlayPage _overlayPage;
        private MapService _mapService;
        private MapOverlay _mapOverlay;
        private MapOverlay _largeOverlay;
        private LargeMapTracker _largeTracker = new LargeMapTracker();
        private Plugins.PluginRuntime _pluginRuntime;
        private Plugins.PluginOverlay _pluginOverlay;
        private TurboHud.TurboHostRuntime _turboRuntime;
        private Game.GameService _gameService;
        private Game.HudOverlay _hudOverlay;
        private static readonly int[] SkillKeys = new int[]
        {
            0x01, 0x02,             // LMB, RMB
            0x31, 0x32, 0x33, 0x34, // '1', '2', '3', '4'
            0x51, 0x57, 0x45, 0x52  // 'Q', 'W', 'E', 'R'
        };
        private readonly bool[] _skillKeyDown = new bool[SkillKeys.Length];

        /// <summary>Pages that are only shown with "Расширенные настройки".</summary>
        private static readonly string[] AdvancedPages = { "profiles", "hud", "files", "about" };

        private string _lastOverlayError = "";
        private System.Windows.Forms.Timer _mapTimer;
        private int _lastMapRevision = -1;
        private double _lastDrawX;
        private double _lastDrawY;
        private DateTime _lastDrawUtc = DateTime.MinValue;

        // ---- Thread-safe logging -------------------------------------------
        // Log() is called from the map reader, the game reader, the macro thread
        // and the update checker, but its subscribers (MacrosPage's TextBox)
        // are WinForms controls. A control touched first from a worker gets its
        // HWND created on that worker; the next UI-thread SendMessage to it then
        // blocks forever, because the worker never pumps messages (the
        // AppHangB1 on area change). So workers only enqueue here, and LogLine
        // is raised exclusively on the UI thread by DrainPendingLog().
        private readonly int _uiThreadId;
        private readonly ConcurrentQueue<string> _pendingLog = new ConcurrentQueue<string>();
        private int _pendingLogCount;
        private int _droppedLogCount;
        private const int MaxPendingLog = 2000;     // a flooding plugin must not grow memory without bound
        private const int MaxDrainPerPass = 50;     // keeps one pass short even after a burst
        private bool _draining;
        private System.Windows.Forms.Timer _uiDrainTimer;
        // Set by the macro thread; the UI thread refreshes the pause label on its next drain.
        private volatile bool _pauseUiDirty;
        // Re-entrancy guard for TickMap: a modal dialog or DoEvents inside a tick
        // pumps WM_TIMER again, and a nested tick would reuse half-updated state.
        private bool _inTick;

        public MacroSettings MacroSettings { get; private set; }
        public MacroEngine Macros { get; private set; }
        public MapService MapService { get { return _mapService; } }
        public MapSettings MapSettings { get { return AppPaths.Settings.Map; } }
        public Plugins.PluginRuntime PluginRuntime { get { return _pluginRuntime; } }
        public TurboHud.TurboHostRuntime TurboRuntime { get { return _turboRuntime; } }
        public Game.GameService GameService { get { return _gameService; } }
        public Game.HudSettings HudSettings { get { return AppPaths.Settings.Hud; } }
        public event Action<string> LogLine;

        /// <summary>Result of the last update check, null until it finishes.</summary>
        public UpdateInfo UpdateState { get; private set; }

        /// <summary>Raised on the UI thread when the update check finishes.</summary>
        public event Action<UpdateInfo> UpdateChecked;

        /// <summary>When true, no tray icon or hotkey hooks are created (used for offscreen rendering).</summary>
        public bool RenderMode { get { return Program.IsRenderMode; } }

        public MainForm()
        {
            // The form is constructed on the thread that will run its message
            // loop; remember it so Log() can tell workers from the UI thread
            // without relying on InvokeRequired (which is false on ANY thread
            // while no handle exists yet - exactly the case that caused the hang).
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;

            // Delivers log lines queued by worker threads. It runs even before
            // the map timer starts and in render mode, so no line is stranded.
            _uiDrainTimer = new System.Windows.Forms.Timer();
            _uiDrainTimer.Interval = 250;
            _uiDrainTimer.Tick += delegate { DrainPendingLog(); };
            _uiDrainTimer.Start();

            MacroSettings = SuperD3Hud.MacroSettings.Load();
            Macros = new MacroEngine(MacroSettings);
            Macros.InventoryProvider = delegate()
            {
                return _gameService != null && _gameService.IsAttached ? _gameService.Inventory : null;
            };
            SyncInventoryLockArea();
            // MacroEngine raises Log from its macro thread (MacroEngine.Emit).
            // Log() is thread-safe; the pause label is only flagged here and
            // refreshed by the UI thread.
            Macros.Log += delegate(string text) { Log(text); _pauseUiDirty = true; };

            Text = "SuperD3Hud " + Program.Version;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 820);
            MinimumSize = new Size(1020, 680);
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = Ui.FontBody;
            KeyPreview = true;
            try { Icon = Ui.CreateAppIcon(); }
            catch { }

            BuildShell();
            BuildPages();
            if (!RenderMode) BuildTray();

            ShowPage("dashboard");
            if (!RenderMode) ReloadHotkeys();
            UpdatePauseUi();

            if (!string.IsNullOrEmpty(Program.StartupPage)) ShowPage(Program.StartupPage);
        }

        private void BuildShell()
        {
            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 218;
            sidebar.BackColor = Color.FromArgb(18, 18, 22);
            sidebar.Padding = new Padding(0, 0, 0, 0);
            Controls.Add(sidebar);

            Panel sideBottom = new Panel();
            sideBottom.Dock = DockStyle.Bottom;
            sideBottom.Height = 104;
            sideBottom.BackColor = Color.Transparent;
            sidebar.Controls.Add(sideBottom);

            _advancedToggle = Ui.DarkCheck("Расширенные настройки");
            _advancedToggle.Location = new Point(20, 8);
            _advancedToggle.CheckedChanged += delegate { OnAdvancedToggled(); };
            Ui.Tip(_advancedToggle,
                "Показать профили и страницы HUD-движка. В простом режиме остаются только основные разделы.");
            sideBottom.Controls.Add(_advancedToggle);

            Label mode = new Label();
            mode.Text = "•  локальный режим";
            mode.Font = Ui.FontBody;
            mode.ForeColor = Ui.Ok;
            mode.AutoSize = true;
            mode.Location = new Point(20, 40);
            sideBottom.Controls.Add(mode);

            Label ver = new Label();
            ver.Text = "v" + Program.Version + "  •  без сети";
            ver.Font = Ui.FontBody;
            ver.ForeColor = Ui.MutedColor;
            ver.AutoSize = true;
            ver.Location = new Point(20, 62);
            sideBottom.Controls.Add(ver);

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 88;
            header.BackColor = Color.Transparent;
            sidebar.Controls.Add(header);

            Label logo = new Label();
            logo.Text = "SuperD3Hud";
            logo.Font = new Font("Segoe UI", 15.5f, FontStyle.Bold);
            logo.ForeColor = Ui.Text;
            logo.AutoSize = true;
            logo.Location = new Point(20, 18);
            header.Controls.Add(logo);

            Label suite = new Label();
            suite.Text = "D I A B L O   I I I";
            suite.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            suite.ForeColor = Ui.Accent;
            suite.AutoSize = true;
            suite.Location = new Point(22, 52);
            header.Controls.Add(suite);

            Panel navHost = new Panel();
            navHost.Dock = DockStyle.Fill;
            navHost.BackColor = Color.Transparent;
            navHost.Padding = new Padding(8, 0, 8, 0);
            sidebar.Controls.Add(navHost);
            navHost.BringToFront();

            AddNav(navHost, "dashboard", "Обзор");
            AddNav(navHost, "map", "Карта");
            AddNav(navHost, "overlay", "HUD на экране");
            AddNav(navHost, "plugins", "Плагины");
            AddNav(navHost, "macros", "Макросы");

            // The rest is hidden until the user asks for the advanced mode.
            MarkAdvanced(AddNavCaption(navHost, "Дополнительно"));
            AddNav(navHost, "profiles", "Профили");
            MarkAdvanced(AddNavCaption(navHost, "Движок HUD"));
            AddNav(navHost, "hud", "Настройки движка");
            AddNav(navHost, "files", "Файлы движка");
            MarkAdvanced(AddNavCaption(navHost, "Справка"));
            AddNav(navHost, "about", "О программе");

            _navLoading = true;
            try { _advancedToggle.Checked = AppPaths.Settings.AdvancedMode; }
            finally { _navLoading = false; }
            ApplyNavMode();

            Panel statusBar = new Panel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 28;
            statusBar.BackColor = Color.FromArgb(18, 18, 22);
            Controls.Add(statusBar);

            _status = new Label();
            _status.Text = "Готово.";
            _status.Font = Ui.FontBody;
            _status.ForeColor = Ui.MutedColor;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(18, 0, 0, 0);
            statusBar.Controls.Add(_status);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Ui.Bg;
            Controls.Add(_content);
            _content.BringToFront();
        }

        /// <summary>
        /// A small caption that groups the navigation entries below it.
        /// </summary>
        private Label AddNavCaption(Panel host, string text)
        {
            Label l = new Label();
            l.Text = text.ToUpperInvariant();
            l.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            l.ForeColor = Color.FromArgb(108, 108, 122);
            l.AutoSize = false;
            l.Height = 22;
            l.Dock = DockStyle.Top;
            l.TextAlign = ContentAlignment.BottomLeft;
            l.Padding = new Padding(14, 0, 0, 2);
            l.Margin = new Padding(0);
            host.Controls.Add(l);
            l.BringToFront();
            return l;
        }

        /// <summary>Adds a navigation control to the set shown in advanced mode.</summary>
        private void MarkAdvanced(Control control)
        {
            if (control == null) return;
            _advancedNav.Add(control);
        }

        private static bool IsAdvancedPage(string key)
        {
            foreach (string page in AdvancedPages)
                if (string.Equals(page, key, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Shows or hides the additional pages. The simple mode is the default:
        /// daily work needs only five sections.
        /// </summary>
        private void ApplyNavMode()
        {
            bool advanced = _advancedToggle != null && _advancedToggle.Checked;
            foreach (Control control in _advancedNav) control.Visible = advanced;

            if (!advanced && IsAdvancedPage(_activePage))
                ShowPage("dashboard");
        }

        private void OnAdvancedToggled()
        {
            if (_navLoading) return;

            bool advanced = _advancedToggle.Checked;
            AppPaths.Settings.AdvancedMode = advanced;
            try { AppPaths.Settings.Save(); }
            catch { }

            ApplyNavMode();
            SetStatus(advanced
                ? "Показаны дополнительные разделы."
                : "Простой режим: только основные разделы.");
        }

        private void AddNav(Panel host, string key, string text)        {
            Button b = new Button();
            b.Text = "   " + text;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.Transparent;
            b.ForeColor = Ui.MutedColor;
            b.Height = 38;
            b.Margin = new Padding(0, 1, 0, 1);
            b.Dock = DockStyle.Top;
            b.Cursor = Cursors.Hand;
            b.Tag = key;
            b.Click += delegate { ShowPage(key); };
            b.MouseEnter += delegate
            {
                if (_activePage != key) b.ForeColor = Ui.Text;
            };
            b.MouseLeave += delegate
            {
                if (_activePage != key) b.ForeColor = Ui.MutedColor;
            };
            host.Controls.Add(b);
            b.BringToFront();
            _nav[key] = b;
            if (IsAdvancedPage(key)) MarkAdvanced(b);
        }

        private void BuildPages()
        {
            _dashboardPage = new DashboardPage(this);

            // The map service is created before the page, because the page
            // reads the current build status from it.
            _mapService = new MapService(MapSettings);
            _mapService.Log += delegate(string text) { Log(text); };

            _mapPage = new MapPage(this);
            _hudPage = new HudPage(this);
            _hudFilesPage = new HudFilesPage(this);

            // The local plugin host compiles plugins-local\*.cs with the
            // compiler of the .NET Framework. It has no third-party runtime.
            // It is created before the plugins page, because that page reads
            // the compiled list immediately.
            _pluginRuntime = new Plugins.PluginRuntime();
            _pluginRuntime.Log += delegate(string text) { Log(text); };
            try { _pluginRuntime.Load(); }
            catch (Exception ex) { Log("Плагины: " + ex.Message); }

            // The TurboHUD compatibility host loads .cs plugins written against
            // the public Turbo.Plugins API. It is independent of the engine.
            _turboRuntime = new TurboHud.TurboHostRuntime();
            _turboRuntime.Log += delegate(string text) { Log(text); };
            try { _turboRuntime.Load(); }
            catch (Exception ex) { Log("TurboHUD: " + ex.Message); }

            _pluginsPage = new PluginsPage(this);
            _macrosPage = new MacrosPage(this);
            _profilesPage = new ProfilesPage(this);
            _overlayPage = new HudOverlayPage(this);
            AboutPage about = new AboutPage(this);

            AddPage("dashboard", _dashboardPage);
            AddPage("map", _mapPage);
            AddPage("overlay", _overlayPage);
            AddPage("hud", _hudPage);
            AddPage("files", _hudFilesPage);
            AddPage("plugins", _pluginsPage);
            AddPage("macros", _macrosPage);
            AddPage("profiles", _profilesPage);
            AddPage("about", about);

            // StartMap() and StartUpdateCheck() are deliberately not called here:
            // they start worker threads and a keyboard hook, and the update
            // callback needs a window handle for BeginInvoke. They run from
            // OnShown, once the handle and the message loop exist.
            if (!RenderMode) AppPaths.EnsureFirstRun();
        }

        private bool _workersStarted;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Belt and braces: create the Macros log box's HWND now, on the UI
            // thread, so no later code path can create it on a worker.
            try { if (_macrosPage != null) _macrosPage.EnsureHandles(); }
            catch { }

            if (_workersStarted || RenderMode) return;
            _workersStarted = true;
            try { StartMap(); }
            catch (Exception ex) { Log("Карта: запуск не удался: " + ex.Message); }
            try { StartUpdateCheck(); }
            catch (Exception ex) { Log("Проверка обновлений: " + ex.Message); }
        }

        /// <summary>
        /// Asks GitHub for the newest release in the background. The result is
        /// delivered on the UI thread; failures stay silent so the launcher is
        /// never blocked or interrupted by a missing network.
        /// </summary>
        public void StartUpdateCheck()
        {
            if (!AppPaths.Settings.CheckForUpdates) return;

            string repo = AppPaths.Settings.UpdateRepo;
            if (string.IsNullOrEmpty(repo)) return;

            UpdateChecker.CheckAsync(repo, Program.Version, delegate(UpdateInfo info)
            {
                // The callback runs on a background thread, so the result is
                // marshalled back to the window thread.
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ApplyUpdateInfo(info); });
                }
                catch { }
            });
        }

        private void ApplyUpdateInfo(UpdateInfo info)
        {
            UpdateState = info;
            if (info == null) return;

            if (info.Available)
                Log("Доступно обновление " + info.LatestVersion + " (у вас " + Program.Version + ").");
            else if (!string.IsNullOrEmpty(info.Error))
                Log("Проверка обновлений: " + info.Error);

            Action<UpdateInfo> handler = UpdateChecked;
            if (handler != null)
            {
                try { handler(info); }
                catch { }
            }
        }

        /// <summary>Opens the release page of the available update.</summary>
        public void OpenUpdatePage()
        {
            UpdateInfo info = UpdateState;
            if (info == null || string.IsNullOrEmpty(info.Url)) return;
            try { Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true }); }
            catch { }
        }

        /// <summary>
        /// Starts the background reader and the timer that keeps the overlay in
        /// sync with the captured state.
        /// </summary>
        private void StartMap()
        {
            if (_mapTimer == null)
            {
                _mapTimer = new System.Windows.Forms.Timer();
                // 60 Hz: the overlays redraw the smoothed position, so the
                // motion is continuous instead of stepping.
                _mapTimer.Interval = 15;
                _mapTimer.Tick += delegate { SafeTickMap(); };
            }
            // The system timer ticks every 15.6 ms by default, so a 16 ms
            // timer would fire only every other slot. A finer resolution lets
            // the overlays really run at 60 frames per second.
            if (!_timerResolutionRaised)
            {
                try { timeBeginPeriod(1); _timerResolutionRaised = true; }
                catch { }
            }
            if (!_mapTimer.Enabled) _mapTimer.Start();
            if (_largeTracker != null && !_largeTracker.StartHook())
                Log("Карта: перехват клавиш недоступен, состояние большой карты берётся опросом.");
            if (_mapService != null) _mapService.Start();

            // The HUD reads monsters, buffs, skills and inventory in its own
            // background thread; the overlay only draws the latest snapshot.
            _gameService = new Game.GameService();
            _gameService.Log += delegate(string text) { Log(text); };
            ApplyHudSettings();
            _gameService.Start();
        }

        /// <summary>Pushes the current HUD settings into the reader and overlay.</summary>
        public void ApplyHudSettings()
        {
            Game.HudSettings settings = HudSettings;
            if (_gameService != null)
            {
                Game.GameFeatures features = _gameService.Features;
                features.Actors = settings.ShowMonsterMarkers || settings.ShowLootMarkers;
                features.Buffs = settings.ShowBuffs;
                features.Skills = settings.ShowSkills || settings.ShowHealth;
                features.Inventory = settings.ShowInventory || (MacroSettings != null && MacroSettings.Enabled && MacroSettings.SmartMode);
                _gameService.ApplySettings();

                for (int i = 0; i < 6; i++)
                    _gameService.SetCooldown(i, settings.Cooldowns[i]);

                // The descriptor pair is discovered once and then remembered.
                if (settings.CooldownDescriptor > 0)
                    _gameService.RestoreCooldowns(settings.CooldownDescriptor,
                                                  settings.CooldownStartDescriptor);
            }
            if (_largeTracker != null)
                _largeTracker.Configure(settings.LargeMapKey, 0x78, settings.WaypointKey);
            if (_hudOverlay != null && !_hudOverlay.IsDisposed)
                _hudOverlay.Visible = settings.Enabled && InputSender.IsGameForeground();
        }

        /// <summary>
        /// Copies the current map state into the overlay window. Runs on the
        /// UI thread, because the overlay is a WinForms window.
        /// </summary>
        private readonly Dictionary<string, string> _guardErrors = new Dictionary<string, string>();

        /// <summary>Runs one stage of the UI tick; an exception is logged once and never stops the other stages.</summary>
        private void Guard(string stage, Action action)
        {
            try { action(); _guardErrors.Remove(stage); }
            catch (Exception ex)
            {
                string text = ex.GetType().Name + ": " + ex.Message;
                string last;
                if (!_guardErrors.TryGetValue(stage, out last) || last != text)
                {
                    _guardErrors[stage] = text;
                    Log("Ошибка (" + stage + "): " + ex.ToString().Replace("\r\n", " | "));
                }
            }
        }

        /// <summary>
        /// Runs TickMap with a re-entrancy guard and an outer catch. The code in
        /// TickMap before the first Guard is unguarded, and an exception escaping
        /// a timer delegate would surface as a ThreadExceptionDialog hidden behind
        /// the topmost overlays while the timer keeps firing.
        /// </summary>
        private void SafeTickMap()
        {
            if (_inTick) return;
            _inTick = true;
            Stopwatch watch = Stopwatch.StartNew();
            try { Guard("таймер", delegate { TickMap(); }); }
            finally
            {
                _inTick = false;
                watch.Stop();
                LogFrameTime(watch.Elapsed.TotalMilliseconds);
            }
        }

        // Frame statistics: the log shows how many frames per second the
        // overlays reach and how long a frame takes.
        private int _frameCount;
        private double _frameTimeSum;
        private DateTime _frameStatsAt = DateTime.MinValue;

        private void LogFrameTime(double milliseconds)
        {
            _frameCount++;
            _frameTimeSum += milliseconds;
            if (_frameStatsAt == DateTime.MinValue)
            {
                _frameStatsAt = DateTime.UtcNow;
                return;
            }

            double seconds = (DateTime.UtcNow - _frameStatsAt).TotalSeconds;
            if (seconds < 10) return;

            int frames = _frameCount;
            double average = _frameTimeSum / Math.Max(1, frames);
            _frameCount = 0;
            _frameTimeSum = 0;
            _frameStatsAt = DateTime.UtcNow;

            Log(string.Format("Наложения: {0:0} кадров/с, кадр {1:0.0} мс.",
                frames / seconds, average));
        }

        private void TickMap()
        {
            if (_mapService == null) return;
            MapSettings settings = MapSettings;

            bool foreground = InputSender.IsGameForeground();
            if (_largeTracker != null)
                _largeTracker.Poll(foreground, settings.LargeAuto);

            List<MapPolygon> polygons;
            MapPoint player;
            bool hasPlayer;
            string status;
            _mapService.GetState(out polygons, out player, out hasPlayer, out status);

            // The game service reads the character far more often than the map
            // service, so its position is the smooth one; the map service only
            // knows whether a position is known at all.
            float smoothX, smoothY;
            if (_gameService != null)
            {
                _gameService.GetSmoothPlayer(out smoothX, out smoothY);
                if (_gameService.PlayerKnown)
                {
                    player = new MapPoint(smoothX, smoothY);
                    hasPlayer = true;
                }
            }

            Guard("клавиши", delegate { TickSkillKeys(); });
            Guard("маркеры", delegate { ApplyMarkers(); });
            Guard("плагины", delegate { TickPlugins(polygons, player, hasPlayer); });
            Guard("HUD", delegate { TickHud(); });

            if (!settings.Enabled)
            {
                HideOverlay(_mapOverlay);
                HideOverlay(_largeOverlay);
                if (_largeTracker != null) _largeTracker.Reset();
                return;
            }

            Guard("миникарта", delegate { UpdateSmallOverlay(settings, polygons, player, hasPlayer, status); });
            Guard("большая карта", delegate { UpdateLargeOverlay(settings, polygons, player, hasPlayer); });
        }

        /// <summary>
        /// True when the overlay has to be repainted: the geometry changed, the
        /// player moved, or an animated marker needs a new frame. Skipping
        /// identical frames keeps a full-screen overlay cheap.
        /// </summary>
        private bool ShouldRedraw(int revision, MapPoint player)
        {
            DateTime now = DateTime.UtcNow;
            bool changed = revision != _lastMapRevision
                || Math.Abs(player.X - _lastDrawX) > 0.05
                || Math.Abs(player.Y - _lastDrawY) > 0.05;

            bool animated = false;
            if (_gameService != null && HudSettings.ShowLootMarkers)
                animated = _gameService.Actors.Loot.Count > 0;

            // Animated markers pulse, so they get a steady 12 Hz refresh.
            if (animated && (now - _lastDrawUtc).TotalMilliseconds >= 80) changed = true;
            if (!changed) return false;

            _lastMapRevision = revision;
            _lastDrawX = player.X;
            _lastDrawY = player.Y;
            _lastDrawUtc = now;
            return true;
        }

        /// <summary>
        /// Copies the monster and loot markers into both map overlays.
        /// </summary>
        private void ApplyMarkers()
        {
            if (_gameService == null) return;
            Game.HudSettings hud = HudSettings;

            List<Game.MonsterMarker> monsters = hud.ShowMonsterMarkers
                ? _gameService.Actors.Monsters
                : new List<Game.MonsterMarker>();
            List<Game.LootMarker> loot = hud.ShowLootMarkers
                ? _gameService.Actors.Loot
                : new List<Game.LootMarker>();

            if (_mapOverlay != null && !_mapOverlay.IsDisposed)
                _mapOverlay.SetMarkers(monsters, loot);
            if (_largeOverlay != null && !_largeOverlay.IsDisposed)
                _largeOverlay.SetMarkers(monsters, loot);

            List<Game.InteractiveMarker> interactive = _gameService.Actors.Interactive;
            if (_mapOverlay != null && !_mapOverlay.IsDisposed)
                _mapOverlay.SetInteractive(interactive);
            if (_largeOverlay != null && !_largeOverlay.IsDisposed)
                _largeOverlay.SetInteractive(interactive);
        }

        /// <summary>
        /// Detects skill key presses so the cooldown timers can start.
        /// The keys are only observed, never suppressed or injected.
        /// </summary>
        private void TickSkillKeys()
        {
            if (_gameService == null) return;

            for (int i = 0; i < SkillKeys.Length; i++)
            {
                int vk = SkillKeys[i];
                bool down = InputSender.IsKeyDown(vk);
                if (down && !_skillKeyDown[i])
                {
                    try { _gameService.OnKey(vk); }
                    catch { }
                }
                _skillKeyDown[i] = down;
            }
        }

        /// <summary>
        /// Draws the built-in HUD over the game client area.
        /// </summary>
        private void TickHud()
        {
            if (_gameService == null) return;
            Game.HudSettings settings = HudSettings;

            if (!settings.Enabled || !InputSender.IsGameForeground())
            {
                HideHud();
                return;
            }

            Rectangle client;
            if (!InputSender.TryGetGameClientRect(out client))
            {
                HideHud();
                return;
            }

            Game.ActorSnapshot actors;
            Game.BuffSnapshot buffs;
            Game.SkillSnapshot skills;
            Game.InventorySnapshot inventory;
            _gameService.GetState(out actors, out buffs, out skills, out inventory);

            if (_hudOverlay == null || _hudOverlay.IsDisposed)
            {
                _hudOverlay = new Game.HudOverlay(settings);
                _hudOverlay.Show();
            }

            if (_hudOverlay.Bounds != client) _hudOverlay.Bounds = client;
            _hudOverlay.Visible = true;
            _hudOverlay.UpdateContent(buffs, skills, inventory, actors);

            PersistCooldownDescriptor();

            string error = _hudOverlay.LastError;
            if (error.Length > 0 && error != _lastOverlayError)
            {
                _lastOverlayError = error;
                Log("HUD: " + error);
            }
        }

        /// <summary>
        /// Stores the cooldown descriptor pair as soon as the reader finds one,
        /// so the discovery is not repeated on the next launch.
        /// </summary>
        private void PersistCooldownDescriptor()
        {
            if (_gameService == null) return;
            Game.HudSettings settings = HudSettings;

            int duration = _gameService.CooldownDescriptor;
            if (duration <= 0 || duration == settings.CooldownDescriptor) return;

            settings.CooldownDescriptor = duration;
            settings.CooldownStartDescriptor = _gameService.CooldownStartDescriptor;
            try { AppPaths.Settings.Save(); }
            catch { }

            Log("Кулдауны: источник найден (атрибуты " + duration + "/" +
                settings.CooldownStartDescriptor + ").");
        }

        private void HideHud()        {
            if (_hudOverlay == null || _hudOverlay.IsDisposed) return;
            try { _hudOverlay.Visible = false; }
            catch { }
        }

        /// <summary>
        /// Feeds the local plugins with the current state and paints them over
        /// the game window.
        /// </summary>
        private void TickPlugins(List<MapPolygon> polygons, MapPoint player, bool hasPlayer)
        {
            if (_pluginRuntime == null) return;

            int scenes = 0, cells = 0, world = 0;
            if (_mapService != null) _mapService.GetSummary(out scenes, out cells, out world);

            bool gameRunning = InputSender.IsGameRunning();
            Game.ActorSnapshot actors;
            Game.BuffSnapshot buffs;
            Game.SkillSnapshot skills;
            Game.InventorySnapshot inventory;
            if (_gameService != null)
                _gameService.GetState(out actors, out buffs, out skills, out inventory);
            else
            {
                actors = new Game.ActorSnapshot();
                buffs = new Game.BuffSnapshot();
                skills = new Game.SkillSnapshot();
                inventory = new Game.InventorySnapshot();
            }

            _pluginRuntime.Tick(gameRunning, _mapService != null && _mapService.BuildSupported,
                                scenes, cells, hasPlayer, player.X, player.Y, world, "",
                                actors.Monsters, actors.Loot, buffs.Buffs, skills.Skills,
                                skills.Health, inventory.UsedSlots, inventory.TotalSlots);

            Rectangle client;
            bool haveClient = InputSender.TryGetGameClientRect(out client);

            if (_turboRuntime != null)
            {
                try
                {
                    _turboRuntime.Tick(_gameService, InputSender.IsGameForeground(),
                                       haveClient ? client.X : 0,
                                       haveClient ? client.Y : 0,
                                       haveClient ? client.Width : 0,
                                       haveClient ? client.Height : 0,
                                       _largeTracker != null && _largeTracker.Visible);
                    // The plugins draw in world space, so they get the smooth
                    // position as well; otherwise every circle jumps once per
                    // actor poll.
                    if (_gameService != null && _gameService.PlayerKnown)
                    {
                        float px, py;
                        _gameService.GetSmoothPlayer(out px, out py);
                        _turboRuntime.SetPlayerPosition(px, py);
                    }
                }
                catch (Exception ex)
                {
                    Log("TurboHUD: " + ex.Message);
                }
            }

            bool turboEnabled = _turboRuntime != null && _turboRuntime.AnyEnabled;
            if (!_pluginRuntime.AnyEnabled && !turboEnabled)
            {
                HideOverlay(_pluginOverlay);
                return;
            }

            if (!haveClient)
            {
                HideOverlay(_pluginOverlay);
                return;
            }

            if (_pluginOverlay == null || _pluginOverlay.IsDisposed)
            {
                _pluginOverlay = new Plugins.PluginOverlay(_pluginRuntime);
                _pluginOverlay.SetTurboRuntime(_turboRuntime);
                _pluginOverlay.Show();
            }

            if (_pluginOverlay.Bounds != client) _pluginOverlay.Bounds = client;

            // The plugins draw over the game, so there is nothing to draw
            // while another window is in front: the frame is skipped instead
            // of burning the processor.
            if (!InputSender.IsGameForeground())
            {
                HideOverlay(_pluginOverlay);
                return;
            }

            _pluginOverlay.Visible = true;
            _pluginOverlay.Redraw();

            string error = _pluginOverlay.LastError;
            if (error.Length > 0 && error != _lastOverlayError)
            {
                _lastOverlayError = error;
                Log("Плагины: " + error);
            }
        }

        /// <summary>Minimap-aligned or floating panel overlay.</summary>
        private void UpdateSmallOverlay(MapSettings settings, List<MapPolygon> polygons,
                                        MapPoint player, bool hasPlayer, string status)
        {
            if (!settings.ShowMinimap && !settings.ShowPanel)
            {
                HideOverlay(_mapOverlay);
                return;
            }

            if (_mapOverlay == null || _mapOverlay.IsDisposed)
            {
                _mapOverlay = new MapOverlay(settings);
                _mapOverlay.Moved += OnOverlayMoved;
                _mapOverlay.Show();
            }
            // The overlay never blocks the game: over the native minimap it is
            // always click-through, in the panel mode it follows the setting.
            _mapOverlay.ApplyClickThrough(settings.ShowMinimap || settings.PanelLocked);

            Rectangle minimap;
            bool hasMinimap = MapOverlay.TryGetMinimapRect(out minimap);

            if (_largeTracker != null && _largeTracker.Visible)
            {
                // When the full-screen map is open, the game hides its own minimap,
                // so the overlay minimap must be hidden as well.
                _mapOverlay.Visible = false;
                return;
            }

            if (settings.ShowMinimap && hasMinimap)
            {
                _mapOverlay.Bounds = minimap;
                // Over the native minimap the overlay is only visible while the
                // game is in the foreground; the separate panel keeps working.
                if (!InputSender.IsGameForeground())
                {
                    HideOverlay(_mapOverlay);
                    return;
                }
                _mapOverlay.Visible = true;
            }
            else if (settings.ShowPanel)
            {
                int size = Math.Max(120, settings.PanelSize);
                int x = settings.PanelX >= 0 ? settings.PanelX : 40;
                int y = settings.PanelY >= 0 ? settings.PanelY : 40;
                _mapOverlay.Bounds = new Rectangle(x, y, size, size);
                _mapOverlay.Visible = true;
            }
            else
            {
                _mapOverlay.Visible = false;
            }

            _mapOverlay.UpdateContent(polygons, player, hasPlayer, status);

            string overlayError = _mapOverlay.LastError;
            if (overlayError != _lastOverlayError)
            {
                _lastOverlayError = overlayError;
                if (overlayError.Length > 0) Log("Наложение карты: " + overlayError);
            }
        }

        /// <summary>
        /// Full-screen overlay for the map that opens with M. The game does not
        /// publish the state of that map, so it is mirrored from the map key.
        /// </summary>
        private void UpdateLargeOverlay(MapSettings settings, List<MapPolygon> polygons,
                                        MapPoint player, bool hasPlayer)
        {
            // The tracker is polled once per tick in TickMap. It keeps running
            // even when the large overlay is off: the minimap overlay and the
            // plugins still have to know that the game's map is open.
            if (!settings.ShowLargeMap)
            {
                HideOverlay(_largeOverlay);
                return;
            }

            Rectangle client;
            if (!_largeTracker.Visible || !InputSender.TryGetGameClientRect(out client))
            {
                HideOverlay(_largeOverlay);
                return;
            }

            if (_largeOverlay == null || _largeOverlay.IsDisposed)
            {
                _largeOverlay = new MapOverlay(settings, true);
                _largeOverlay.Show();
            }

            if (_largeOverlay.Bounds != client) _largeOverlay.Bounds = client;
            _largeOverlay.Visible = true;
            _largeOverlay.UpdateContent(polygons, player, hasPlayer, "");

            string error = _largeOverlay.LastError;
            if (error.Length > 0 && error != _lastOverlayError)
            {
                _lastOverlayError = error;
                Log("Большая карта: " + error);
            }
        }

        private static void HideOverlay(MapOverlay overlay)
        {
            if (overlay == null || overlay.IsDisposed) return;
            try { overlay.Visible = false; }
            catch { }
        }

        private static void HideOverlay(Plugins.PluginOverlay overlay)
        {
            if (overlay == null || overlay.IsDisposed) return;
            try { overlay.Visible = false; }
            catch { }
        }

        /// <summary>
        /// Called after the map settings were changed on the settings page.
        /// </summary>
        public void OnMapSettingsChanged()
        {
            if (_mapService != null) _mapService.ApplySettings();
            if (_mapPage != null) _mapPage.RefreshStatus();
            SafeTickMap();
        }

        /// <summary>
        /// Remembers where the user dragged the standalone panel.
        /// </summary>
        private void OnOverlayMoved(object sender, EventArgs e)
        {
            if (_mapOverlay == null || _mapOverlay.IsDisposed) return;
            MapSettings settings = MapSettings;
            if (!settings.ShowPanel) return;
            if (settings.PanelX == _mapOverlay.Left && settings.PanelY == _mapOverlay.Top) return;
            settings.PanelX = _mapOverlay.Left;
            settings.PanelY = _mapOverlay.Top;
            try { AppPaths.Settings.Save(); }
            catch { }
        }

        private void AddPage(string key, UserControl page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            _pages[key] = page;
        }

        public void ShowPage(string key)
        {
            UserControl page;
            if (!_pages.TryGetValue(key, out page)) return;
            foreach (KeyValuePair<string, UserControl> kv in _pages) kv.Value.Visible = kv.Key == key;
            page.Visible = true;
            page.BringToFront();
            _activePage = key;

            foreach (KeyValuePair<string, Button> kv in _nav)
            {
                bool active = kv.Key == key;
                kv.Value.BackColor = active ? Ui.Panel2 : Color.Transparent;
                kv.Value.ForeColor = active ? Ui.Accent : Ui.MutedColor;
            }

            if (key == "dashboard" && _dashboardPage != null) _dashboardPage.RefreshAll();
            if (key == "plugins" && _pluginsPage != null) _pluginsPage.RefreshList();
            if (key == "macros" && _macrosPage != null) _macrosPage.RefreshData();
            if (key == "profiles" && _profilesPage != null) _profilesPage.RefreshList();
            if (key == "hud" && _hudPage != null) _hudPage.Reload();
            if (key == "files" && _hudFilesPage != null) _hudFilesPage.ReloadAll();
            if (key == "map" && _mapPage != null) _mapPage.RefreshStatus();
            if (key == "overlay" && _overlayPage != null) _overlayPage.RefreshState();
        }

        // ------------------------------------------------------------------
        // Public helpers used by pages
        // ------------------------------------------------------------------

        /// <summary>True when called on the thread that owns this form's controls.</summary>
        public bool IsUiThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == _uiThreadId; }
        }

        public void SetStatus(string text)
        {
            if (_status == null) return;
            if (IsUiThread)
            {
                _status.Text = text;
                return;
            }
            // Same hazard as the log box: never touch a control from a worker.
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((MethodInvoker)delegate { if (_status != null) _status.Text = text; });
            }
            catch { }
        }

        /// <summary>
        /// Writes a line to logspp.log and shows it in the UI. Safe to call
        /// from any thread: the file write goes to a background writer and the
        /// LogLine event is raised only on the UI thread (see DrainPendingLog).
        /// </summary>
        public void Log(string text)
        {
            if (text == null) text = "";
            AppLogFile.Write(text);

            if (Interlocked.Increment(ref _pendingLogCount) > MaxPendingLog)
            {
                // The UI is not draining (for example the window is stuck in a
                // modal loop) while something floods. Keep the file complete but
                // drop UI lines; the drain reports how many were skipped.
                Interlocked.Decrement(ref _pendingLogCount);
                Interlocked.Increment(ref _droppedLogCount);
            }
            else
            {
                _pendingLog.Enqueue(text);
            }

            // On the UI thread the line can be shown immediately (in order with
            // anything a worker queued before it). Workers wait for the 250 ms
            // drain timer.
            if (IsUiThread) DrainPendingLog();
        }

        /// <summary>
        /// Raises LogLine for queued lines. UI thread only; at most
        /// MaxDrainPerPass lines per call so a burst cannot stall the frame.
        /// </summary>
        private void DrainPendingLog()
        {
            if (!IsUiThread || _draining) return;   // a LogLine handler that logs must not recurse
            _draining = true;
            try
            {
                int dropped = Interlocked.Exchange(ref _droppedLogCount, 0);
                if (dropped > 0) RaiseLogLine("(пропущено строк журнала в окне: " + dropped + ")");

                string line;
                for (int i = 0; i < MaxDrainPerPass && _pendingLog.TryDequeue(out line); i++)
                {
                    Interlocked.Decrement(ref _pendingLogCount);
                    RaiseLogLine(line);
                }

                if (_pauseUiDirty)
                {
                    _pauseUiDirty = false;
                    try { UpdatePauseUi(); }
                    catch { }
                }
            }
            catch { }
            finally { _draining = false; }
        }

        private void RaiseLogLine(string text)
        {
            Action<string> handler = LogLine;
            if (handler == null) return;
            // Per line, so one failing subscriber or line does not drop the rest.
            try { handler(text); }
            catch { }
        }

        public void ReloadHotkeys()
        {
            if (_hotkeys == null) _hotkeys = new HotkeyManager();
            _hotkeys.Pressed -= OnHotkey;
            _hotkeys.Pressed += OnHotkey;
            List<string> errors = _hotkeys.RegisterAll(MacroSettings.Hotkeys);
            if (errors.Count > 0)
            {
                foreach (string e in errors) Log("Горячая клавиша: " + e);
                SetStatus("Не все горячие клавиши зарегистрированы.");
            }
            else
            {
                SetStatus("Горячие клавиши обновлены.");
            }
        }

        private void OnHotkey(string action)
        {
            RunMacro(action);
        }

        public void RunMacro(string action)
        {
            Macros.Run(action);
            UpdatePauseUi();
        }

        public void TogglePause()
        {
            Macros.TogglePause();
            UpdatePauseUi();
        }

        public void StopMacros()
        {
            Macros.Stop();
            SetStatus("Макрос остановлен.");
        }

        public void UpdateMacroSettings()
        {
            Macros.UpdateSettings(MacroSettings);
            SyncInventoryLockArea();
            ApplyHudSettings();
        }

        private void SyncInventoryLockArea()
        {
            TurboHost.HostInventoryController.LockAreaProvider = delegate()
            {
                MacroSettings s = MacroSettings;
                return s == null
                    ? Rectangle.Empty
                    : MacroSettings.LockAreaForColumns(s.LockedLeftColumns, s.LockedRightColumns);
            };
        }

        private void UpdatePauseUi()
        {
            if (!IsUiThread)
            {
                // Callers from a worker only flag it; DrainPendingLog refreshes it.
                _pauseUiDirty = true;
                return;
            }
            if (_macrosPage != null) _macrosPage.UpdatePauseLabel();
        }

        public void CheckPaths()
        {
            List<string> problems = new List<string>();
            if (string.IsNullOrEmpty(AppPaths.HudRoot)) problems.Add("• не найдена папка HUD");
            else if (!System.IO.File.Exists(AppPaths.HudExe)) problems.Add("• не найден исполняемый файл HUD");
            if (string.IsNullOrEmpty(AppPaths.Settings.D3Path)) problems.Add("• не найден Diablo III.exe");
            if (problems.Count == 0)
            {
                MessageBox.Show(this, "Пути в порядке.\n\nHUD: " + AppPaths.HudRoot + "\nИгра: " + AppPaths.Settings.D3Path,
                    "Проверка путей", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus("Проверка путей: всё найдено.");
            }
            else
            {
                MessageBox.Show(this, "Не удалось найти:\n\n" + string.Join("\n", problems.ToArray()) +
                    "\n\nУкажите пути на вкладке «Обзор».", "Проверка путей",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ReloadAll()
        {
            AppPaths.ReloadSettings();
            MacroSettings = SuperD3Hud.MacroSettings.Load();
            Macros.UpdateSettings(MacroSettings);
            SyncInventoryLockArea();
            ApplyHudSettings();
            if (_dashboardPage != null) _dashboardPage.RefreshAll();
            if (_hudPage != null) _hudPage.Reload();
            if (_hudFilesPage != null) _hudFilesPage.ReloadAll();
            if (_pluginsPage != null) _pluginsPage.RefreshList();
            if (_macrosPage != null) _macrosPage.RefreshData();
            if (_profilesPage != null) _profilesPage.RefreshList();
            ReloadHotkeys();
        }

        // ------------------------------------------------------------------
        // Tray and lifetime
        // ------------------------------------------------------------------

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            try { _tray.Icon = Ui.CreateAppIcon(); }
            catch { }
            _tray.Text = "SuperD3Hud";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Показать", null, delegate { ShowFromTray(); });
            menu.Items.Add("Пауза макросов", null, delegate { TogglePause(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            ShowPage(_activePage.Length > 0 ? _activePage : "dashboard");
        }

        private void ExitApp()
        {
            _reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (!_reallyExit && e.CloseReason == CloseReason.UserClosing && AppPaths.Settings.MinimizeToTray)
                {
                    e.Cancel = true;
                    Hide();
                    try
                    {
                        _tray.ShowBalloonTip(1500, "SuperD3Hud",
                            "Лаунчер свёрнут в трей. Горячие клавиши продолжают работать.", ToolTipIcon.Info);
                    }
                    catch { }
                    return;
                }
            }
            catch { }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (_hotkeys != null) _hotkeys.Dispose();
                if (_largeTracker != null) _largeTracker.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                Macros.Stop();
                MacroSettings.Save();
                if (_mapTimer != null)
                {
                    _mapTimer.Stop();
                    _mapTimer.Dispose();
                    _mapTimer = null;
                }
                if (_uiDrainTimer != null)
                {
                    _uiDrainTimer.Stop();
                    _uiDrainTimer.Dispose();
                    _uiDrainTimer = null;
                }
                if (_mapOverlay != null && !_mapOverlay.IsDisposed)
                {
                    try { _mapOverlay.Close(); } catch { }
                    try { _mapOverlay.Dispose(); } catch { }
                    _mapOverlay = null;
                }
                if (_largeOverlay != null && !_largeOverlay.IsDisposed)
                {
                    try { _largeOverlay.Close(); } catch { }
                    try { _largeOverlay.Dispose(); } catch { }
                    _largeOverlay = null;
                }
                if (_pluginOverlay != null && !_pluginOverlay.IsDisposed)
                {
                    try { _pluginOverlay.Close(); } catch { }
                    try { _pluginOverlay.Dispose(); } catch { }
                    _pluginOverlay = null;
                }
                if (_pluginRuntime != null)
                {
                    try { _pluginRuntime.Dispose(); } catch { }
                    _pluginRuntime = null;
                }
                if (_turboRuntime != null)
                {
                    try { _turboRuntime.Dispose(); } catch { }
                    _turboRuntime = null;
                }
                if (_hudOverlay != null && !_hudOverlay.IsDisposed)
                {
                    try { _hudOverlay.Close(); } catch { }
                    try { _hudOverlay.Dispose(); } catch { }
                    _hudOverlay = null;
                }
                if (_gameService != null)
                {
                    try { _gameService.Dispose(); } catch { }
                    _gameService = null;
                }
                if (_mapService != null)
                {
                    try { _mapService.Dispose(); } catch { }
                    _mapService = null;
                }
            }
            catch { }
            // The writer thread is a background thread; push out the last lines
            // (including the services' shutdown messages) before the process ends.
            AppLogFile.Flush(2000);
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            // The drain timer is not in a components container; stop it so a
            // form disposed without being closed (render mode, tests) does not
            // keep ticking.
            if (disposing && _uiDrainTimer != null)
            {
                try { _uiDrainTimer.Stop(); _uiDrainTimer.Dispose(); }
                catch { }
                _uiDrainTimer = null;
            }

            if (disposing)
            {
                if (_timerResolutionRaised)
                {
                    try { timeEndPeriod(1); } catch { }
                    _timerResolutionRaised = false;
                }

                // .NET Framework 4.8 throws a NullReferenceException in
                // ListView.OnHandleDestroyed when a list that still holds items
                // is destroyed together with the window that owns it. The
                // lists are emptied here, while the window handle is still
                // alive, so nothing is left for the framework to trip over.
                _shuttingDown = true;
                try { ClearListViews(this); }
                catch { }
            }

            base.Dispose(disposing);
        }

        /// <summary>True while the window is being closed; pages stop reacting then.</summary>
        public bool IsShuttingDown { get { return _shuttingDown; } }

        /// <summary>
        /// Removes the items of every list inside the window. Must be called
        /// while the window handle is still valid.
        /// </summary>
        private static void ClearListViews(Control root)
        {
            if (root == null) return;
            for (int i = root.Controls.Count - 1; i >= 0; i--)
            {
                Control child = root.Controls[i];
                if (child == null) continue;
                ListView list = child as ListView;
                if (list != null)
                {
                    try { if (list.Items.Count > 0) list.Items.Clear(); }
                    catch { }
                    continue;
                }
                ClearListViews(child);
            }
        }

        // ------------------------------------------------------------------
        // --log-thread-test diagnostic
        // ------------------------------------------------------------------

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);

        private static uint WindowThread(Control control)
        {
            uint pid;
            return GetWindowThreadProcessId(control.Handle, out pid);
        }

        private void DrainAllForTest()
        {
            for (int i = 0; i < 100 && _pendingLogCount > 0; i++) DrainPendingLog();
        }

        /// <summary>
        /// Regression test for the cross-thread TextBox deadlock: builds the
        /// pages hidden, logs from workers and the UI thread, and checks that
        /// nothing blocks and that the Macros log box is owned by the UI thread.
        /// Returns the process exit code (0 = pass).
        /// </summary>
        public static int RunLogThreadTest()
        {
            int failures = 0;
            try
            {
                AppPaths.Initialize(true);
                // Render mode: no tray, hotkeys, keyboard hook or reader threads.
                Program.IsRenderMode = true;
                using (MainForm form = new MainForm())
                {
                    TextBox box = form._macrosPage.LogBoxForTest;
                    Stopwatch watch = Stopwatch.StartNew();

                    // 1. A worker logs while the Macros page has never been shown.
                    //    Before the fix this created the TextBox HWND on the worker.
                    Thread worker = new Thread(delegate()
                    {
                        for (int i = 0; i < 20; i++) form.Log("[log-thread-test] worker A " + i);
                    });
                    worker.IsBackground = true;
                    worker.Start();
                    if (!worker.Join(1000)) { failures++; Console.WriteLine("FAIL worker A blocked in Log"); }
                    if (box.IsHandleCreated)
                    {
                        failures++;
                        Console.WriteLine("FAIL log box handle was created by the worker (thread " + WindowThread(box) + ")");
                    }
                    else Console.WriteLine("ok   worker log did not touch the log box");

                    // 2. The UI thread logs; this also delivers the worker's lines.
                    form.Log("[log-thread-test] ui 1");
                    form.DrainAllForTest();
                    if (box.Text.IndexOf("worker A 19", StringComparison.Ordinal) < 0 ||
                        box.Text.IndexOf("ui 1", StringComparison.Ordinal) < 0)
                    { failures++; Console.WriteLine("FAIL lines missing from the log box"); }
                    else Console.WriteLine("ok   worker and UI lines delivered on the UI thread");

                    // 3. The owner of the HWND must be this (UI) thread.
                    form._macrosPage.EnsureHandles();
                    uint owner = WindowThread(box);
                    uint self = GetCurrentThreadId();
                    if (owner != self) { failures++; Console.WriteLine("FAIL log box owned by thread " + owner + ", UI is " + self); }
                    else Console.WriteLine("ok   log box owned by the UI thread (" + owner + ")");

                    // 4. Worker and UI log at the same time: no deadlock, nothing lost.
                    Thread worker2 = new Thread(delegate()
                    {
                        for (int i = 0; i < 200; i++) form.Log("[log-thread-test] worker B " + i);
                    });
                    worker2.IsBackground = true;
                    worker2.Start();
                    for (int i = 0; i < 50; i++) form.Log("[log-thread-test] ui B " + i);
                    if (!worker2.Join(1000)) { failures++; Console.WriteLine("FAIL worker B blocked in Log"); }
                    form.DrainAllForTest();
                    if (form._pendingLogCount != 0 || box.Text.IndexOf("worker B 199", StringComparison.Ordinal) < 0)
                    { failures++; Console.WriteLine("FAIL concurrent lines not delivered (pending " + form._pendingLogCount + ")"); }
                    else Console.WriteLine("ok   concurrent worker/UI logging delivered");

                    // 5. The box is capped so a flood cannot grow it without bound.
                    string filler = new string('x', 200);
                    for (int i = 0; i < 600; i++) form.Log("[log-thread-test] fill " + filler);
                    form.DrainAllForTest();
                    if (box.TextLength > MacrosPage.MaxLogChars) { failures++; Console.WriteLine("FAIL log box not capped: " + box.TextLength); }
                    else Console.WriteLine("ok   log box capped at " + box.TextLength + " chars");

                    watch.Stop();
                    if (watch.ElapsedMilliseconds > 1000) { failures++; Console.WriteLine("FAIL took " + watch.ElapsedMilliseconds + " ms"); }
                    else Console.WriteLine("ok   finished in " + watch.ElapsedMilliseconds + " ms");
                }

                if (!AppLogFile.Flush(3000)) { failures++; Console.WriteLine("FAIL app.log writer did not flush"); }
                else
                {
                    string path = AppLogFile.PathForTest;
                    string content = "";
                    using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.OpenOrCreate,
                               System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                    using (System.IO.StreamReader r = new System.IO.StreamReader(fs))
                        content = r.ReadToEnd();
                    if (content.IndexOf("[log-thread-test] worker B 199", StringComparison.Ordinal) < 0)
                    { failures++; Console.WriteLine("FAIL app.log misses worker lines"); }
                    else Console.WriteLine("ok   app.log written by the background writer");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("FAIL " + ex);
            }
            Console.WriteLine(failures == 0 ? "log-thread-test: PASS" : "log-thread-test: " + failures + " failure(s)");
            return failures == 0 ? 0 : 1;
        }
    }

    /// <summary>
    /// Appends lines to logs\app.log from one background thread. Callers on
    /// any thread only enqueue, so a slow disk, an antivirus scan or a log
    /// flood never blocks the UI thread or a reader. The file is opened once
    /// per batch (at most every 500 ms) rather than held open, so a second
    /// instance or a diagnostic run can still append to and rotate it.
    /// </summary>
    internal static class AppLogFile
    {
        private const long MaxBytes = 2000000;
        private const int MaxQueued = 20000;
        private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly object StartLock = new object();
        private static Thread _thread;
        private static int _queued;
        private static int _dropped;
        private static long _enqueued;
        private static long _processed;

        internal static string PathForTest { get { return LogPath(); } }

        private static string LogPath()
        {
            string root = AppPaths.AppRoot;
            if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(root, System.IO.Path.Combine("logs", "app.log"));
        }

        public static void Write(string text)
        {
            if (Interlocked.Increment(ref _queued) > MaxQueued)
            {
                // The disk is not keeping up with a flood; drop and report the count.
                Interlocked.Decrement(ref _queued);
                Interlocked.Increment(ref _dropped);
                return;
            }
            Queue.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text);
            Interlocked.Increment(ref _enqueued);
            EnsureThread();
        }

        /// <summary>Waits until everything queued so far is on disk. Returns false on timeout.</summary>
        public static bool Flush(int timeoutMs)
        {
            long target = Interlocked.Read(ref _enqueued);
            if (Interlocked.Read(ref _processed) >= target) return true;
            EnsureThread();
            Wake.Set();
            Stopwatch watch = Stopwatch.StartNew();
            while (Interlocked.Read(ref _processed) < target)
            {
                if (watch.ElapsedMilliseconds > timeoutMs) return false;
                Thread.Sleep(10);
            }
            return true;
        }

        private static void EnsureThread()
        {
            if (_thread != null) return;
            lock (StartLock)
            {
                if (_thread != null) return;
                Thread t = new Thread(Run);
                t.IsBackground = true;   // never keeps the process alive; OnFormClosed flushes
                t.Name = "SuperD3Hud log writer";
                t.Start();
                _thread = t;
            }
        }

        private static void Run()
        {
            while (true)
            {
                try
                {
                    Wake.WaitOne(500);
                    WriteBatch();
                }
                catch { }
            }
        }

        private static void WriteBatch()
        {
            if (Queue.IsEmpty && _dropped == 0) return;
            List<string> lines = new List<string>();
            string line;
            while (lines.Count < 10000 && Queue.TryDequeue(out line))
            {
                Interlocked.Decrement(ref _queued);
                lines.Add(line);
            }
            int taken = lines.Count;
            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  (журнал: пропущено строк: " + dropped + ")");

            try
            {
                string path = LogPath();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                Rotate(path);
                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Append,
                           System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                using (System.IO.StreamWriter w = new System.IO.StreamWriter(fs, new System.Text.UTF8Encoding(false)))
                {
                    foreach (string l in lines) w.WriteLine(l);
                }
            }
            catch { }   // logging must never take the app down; the lines are lost
            finally
            {
                // Counted even on failure so Flush() cannot wait forever on a locked file.
                Interlocked.Add(ref _processed, taken);
            }
        }

        /// <summary>Keeps one previous file (app.1.log) instead of deleting the history outright.</summary>
        private static void Rotate(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length <= MaxBytes) return;
                string old = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "app.1.log");
                if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                System.IO.File.Move(path, old);
            }
            catch
            {
                // Another process holds the file open: fall back to the old
                // behaviour and truncate so the log cannot grow without bound.
                try { System.IO.File.Delete(path); }
                catch { }
            }
        }
    }
}



