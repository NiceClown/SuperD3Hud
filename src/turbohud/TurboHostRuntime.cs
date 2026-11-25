using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using SuperD3Hud.Game;
using SuperD3Hud.TurboHost;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHud
{
    /// <summary>A TurboHUD plugin instance together with its source and state.</summary>
    public class TurboPluginEntry
    {
        // Hook stages. Each one is throttled, timed and suspended on its own:
        // a plugin whose PaintTop(BeforeClip) throws keeps its PaintWorld.
        public const int StageAfterCollect = 0;
        public const int StageBeforeRender = 1;
        public const int StageWorldGround = 2;
        public const int StageWorldMap = 3;
        public const int StageTopBeforeClip = 4;
        public const int StageTopInventory = 5;
        public const int StageTopAfterClip = 6;
        /// <summary>Number of per-frame hook stages (the size of <see cref="Hooks"/>).</summary>
        public const int HookStageCount = 7;
        // One-shot stages. They only appear in the current-plugin marker and
        // in error texts; they have no per-frame state.
        public const int StageLoad = 7;
        public const int StageCustomize = 8;

        /// <summary>Stage names, indexed by the Stage* constants. They match the old log prefixes.</summary>
        public static readonly string[] StageNames =
        {
            "AfterCollect", "BeforeRender", "PaintWorld(Ground)", "PaintWorld(Map)",
            "PaintTop(BeforeClip)", "PaintTop(Inventory)", "PaintTop(AfterClip)",
            "Load", "Customize"
        };

        public static string StageName(int stage)
        {
            return stage >= 0 && stage < StageNames.Length ? StageNames[stage] : "?";
        }

        public string FilePath;
        public string RelativePath;
        public string ClassName;
        public string Description;
        public bool Enabled;
        public bool Compiled;
        public string Error;
        public IPlugin Instance;

        /// <summary>Error throttling, suspension and timing per hook stage.</summary>
        public readonly TurboHookState[] Hooks = CreateHooks();

        private static TurboHookState[] CreateHooks()
        {
            TurboHookState[] hooks = new TurboHookState[HookStageCount];
            for (int i = 0; i < hooks.Length; i++) hooks[i] = new TurboHookState();
            return hooks;
        }

        /// <summary>True when at least one hook was suspended after repeated failures.</summary>
        public bool Suspended
        {
            get
            {
                for (int i = 0; i < Hooks.Length; i++)
                    if (Hooks[i].Disabled) return true;
                return false;
            }
        }

        /// <summary>The worst recent p95 hook time of this plugin, in milliseconds.</summary>
        public float WorstP95Ms
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < Hooks.Length; i++)
                    if (Hooks[i].P95Ms > worst) worst = Hooks[i].P95Ms;
                return worst;
            }
        }

        /// <summary>
        /// A short status line for the plugin list (PluginsPage): the
        /// suspended hooks with their last error, or the slowest hook. Empty
        /// when the plugin runs normally.
        /// </summary>
        public string RuntimeStatus
        {
            get
            {
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < Hooks.Length; i++)
                {
                    TurboHookState hook = Hooks[i];
                    if (!hook.Disabled) continue;
                    if (text.Length > 0) text.Append("; ");
                    text.Append("Приостановлен ").Append(StageNames[i]).Append(": ").Append(hook.LastError ?? "");
                }
                if (text.Length > 0) return text.ToString();

                float worst = WorstP95Ms;
                if (worst > TurboHookState.SlowP95Ms)
                    return "Медленный: p95 " + worst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " мс";
                return "";
            }
        }
    }

    /// <summary>
    /// Runtime state of one hook stage of one plugin. Only the frame thread
    /// writes it; UI readers may see a slightly stale value, which is fine for
    /// a status display.
    /// </summary>
    public sealed class TurboHookState
    {
        /// <summary>Consecutive failing calls after which the hook is suspended.</summary>
        public const int SuspendAfterFailures = 100;
        /// <summary>A repeated error is summarised at most this often.</summary>
        public const int SummaryIntervalSeconds = 60;
        /// <summary>A hook whose p95 exceeds this budget is reported as slow.</summary>
        public const float SlowP95Ms = 4f;
        /// <summary>Calls per timing window; p95 is recomputed each time the window fills.</summary>
        public const int SampleWindow = 128;

        /// <summary>The last distinct error text ("Type: message").</summary>
        public string LastError;
        public int ConsecutiveFailures;
        /// <summary>Repeats of <see cref="LastError"/> not yet written to the log.</summary>
        public int SuppressedCount;
        public long TotalFailures;
        /// <summary>Suspended for the rest of this world; cleared on world change.</summary>
        public bool Disabled;
        public int TimesSuspended;
        /// <summary>p95 and max call time of the last full timing window, in ms.</summary>
        public float P95Ms;
        public float MaxMs;

        internal long LastSummaryTimestamp;
        internal long LastSlowWarnTimestamp;
        // Allocated on the first timed call, so hooks a plugin does not
        // implement cost nothing.
        internal float[] Samples;
        internal int SampleCount;
    }

    /// <summary>
    /// Hosts TurboHUD plugins. It owns the compatibility controller, compiles
    /// the .cs files of the local TurboHUD folder, and calls the same hooks the
    /// original HUD calls: AfterCollect, BeforeRender and the painters.
    /// </summary>
    public class TurboHostRuntime : IDisposable
    {
        public const string DefaultFolderName = "plugins-turbohud";

        private readonly object _sync = new object();
        private readonly List<TurboPluginEntry> _plugins = new List<TurboPluginEntry>();
        private readonly Dictionary<string, TurboPluginEntry> _byPath =
            new Dictionary<string, TurboPluginEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly HostContext _context = new HostContext();
        private readonly HostCanvas _canvas = new HostCanvas();
        private readonly string _folder;
        private HostController _controller;
        private bool _loaded;

        // World of the previous Tick. Suspended hooks are re-enabled when it
        // changes: many plugin failures depend on the area (a shrine buff, a
        // rift boss) and a new world is a fair second chance.
        private const int NoWorld = int.MinValue;
        private int _lastWorldId = NoWorld;

        // Scratch buffer for the p95 sort; only the frame thread uses it.
        private readonly float[] _sortScratch = new float[TurboHookState.SampleWindow];

        // "Current plugin" marker for a watchdog on another thread (WP-J). It
        // is written before and cleared after every plugin call. The reader
        // gets the three values with separate reads, so it may combine an
        // entry with the stage of the next call; that only blurs a report and
        // never blames an idle plugin for a long stall, because the timestamp
        // is written first and cleared last.
        private volatile TurboPluginEntry _currentEntry;
        private volatile int _currentStage = -1;
        private long _currentSince;

        public event Action<string> Log;

        public TurboHostRuntime()
            : this(Path.Combine(AppPaths.AppRoot, DefaultFolderName))
        {
        }

        /// <summary>Creates a host over an explicit plugin folder.</summary>
        public TurboHostRuntime(string folder)
        {
            _folder = string.IsNullOrEmpty(folder)
                ? Path.Combine(AppPaths.AppRoot, DefaultFolderName)
                : folder;
            _context.Canvas = _canvas;
            _canvas.FontScale = ReadFontScale();
        }

        /// <summary>
        /// Optional [turbohud] FontScale in settings.ini: a multiplier for all
        /// plugin fonts, 1 = TurboHUD sizes at the current resolution.
        /// </summary>
        private static float ReadFontScale()
        {
            try
            {
                IniFile ini = new IniFile(AppPaths.SettingsFile);
                ini.Load();
                string raw = ini.Get("turbohud", "FontScale", "1");
                float value;
                if (float.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0f)
                    return value;
            }
            catch { }
            return 1f;
        }

        /// <summary>Fonts follow the game window: sizes are given for 1080p.</summary>
        private void UpdateUiScale(int height)
        {
            if (height > 0) _canvas.UiScale = height / 1080f;
        }

        public string Folder { get { return _folder; } }

        public HostContext Context { get { return _context; } }

        public HostController Controller { get { return _controller; } }

        public List<TurboPluginEntry> Plugins
        {
            get { lock (_sync) return new List<TurboPluginEntry>(_plugins); }
        }

        /// <summary>The plugin whose hook is running right now, or null when idle.</summary>
        public TurboPluginEntry CurrentPlugin { get { return _currentEntry; } }

        /// <summary>The Stage* constant of the running hook, or -1 when idle.</summary>
        public int CurrentStage { get { return _currentStage; } }

        /// <summary>
        /// Stopwatch.GetTimestamp() at which the running hook started, or 0
        /// when no plugin code is running. Safe to read from any thread.
        /// </summary>
        public long CurrentSinceTimestamp { get { return Interlocked.Read(ref _currentSince); } }

        /// <summary>
        /// "ClassName Stage for N ms" for the running hook, or null when idle.
        /// Intended for a watchdog on another thread.
        /// </summary>
        public string DescribeCurrent()
        {
            long since = Interlocked.Read(ref _currentSince);
            TurboPluginEntry entry = _currentEntry;
            int stage = _currentStage;
            if (since == 0 || entry == null) return null;
            long ms = (Stopwatch.GetTimestamp() - since) * 1000L / Stopwatch.Frequency;
            return entry.ClassName + " " + TurboPluginEntry.StageName(stage) + " for " + ms + " ms";
        }

        private void MarkCurrent(TurboPluginEntry entry, int stage)
        {
            Interlocked.Exchange(ref _currentSince, Stopwatch.GetTimestamp());
            _currentStage = stage;
            _currentEntry = entry;
        }

        private void ClearCurrent()
        {
            _currentEntry = null;
            _currentStage = -1;
            Interlocked.Exchange(ref _currentSince, 0L);
        }

        public bool AnyEnabled
        {
            get
            {
                lock (_sync)
                {
                    foreach (TurboPluginEntry entry in _plugins)
                        if (entry.Enabled && entry.Instance != null) return true;
                    return false;
                }
            }
        }

        // ------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------

        public void Load()
        {
            try { Directory.CreateDirectory(_folder); }
            catch { }

            EnsureExample();

            lock (_sync)
            {
                foreach (TurboPluginEntry entry in _plugins) UnloadInstance(entry);
                _plugins.Clear();
                _byPath.Clear();
            }

            _controller = new HostController(_context, _canvas);
            _controller.DebugLog += delegate(string text) { Emit("TurboHUD: " + text); };

            List<TurboCompiledPlugin> compiled = TurboPluginCompiler.CompileFolder(_folder, Emit);
            Dictionary<string, bool> states = LoadStates();

            lock (_sync)
            {
                LoadDefaultAssemblyPlugins(states);

                foreach (TurboCompiledPlugin result in compiled)
                {
                    TurboPluginEntry entry = new TurboPluginEntry();
                    entry.FilePath = result.FilePath;
                    entry.RelativePath = MakeRelative(result.FilePath);
                    entry.ClassName = result.ClassName;
                    entry.Compiled = result.Ok;
                    entry.Error = result.Error;

                    bool enabled;
                    bool known = states.TryGetValue(entry.RelativePath, out enabled);
                    entry.Enabled = enabled;

                    if (result.Ok)
                    {
                        try
                        {
                            IPlugin instance = (IPlugin)Activator.CreateInstance(result.PluginType);
                            entry.Instance = instance;
                            entry.Description = Describe(instance);

                            if (!known) entry.Enabled = true;

                            _controller.RegisterPlugin(instance);
                            MarkCurrent(entry, TurboPluginEntry.StageLoad);
                            try { instance.Load(_controller); }
                            finally { ClearCurrent(); }
                        }
                        catch (Exception ex)
                        {
                            entry.Error = "Load: " + ex.Message;
                        }
                    }
                    else
                    {
                        Emit("Plugin " + entry.ClassName + ": " + entry.Error);
                    }

                    _plugins.Add(entry);
                    _byPath[entry.RelativePath] = entry;
                }
            }

            lock (_sync) _plugins.Sort(ComparePluginOrder);

            RunCustomizers();

            _lastWorldId = NoWorld;
            _loaded = true;
            Emit("TurboHUD plugins: loaded " + CountLoaded() + " of " + _plugins.Count + ".");
        }

        private static int ComparePluginOrder(TurboPluginEntry a, TurboPluginEntry b)
        {
            int orderA = a != null && a.Instance != null ? a.Instance.Order : 0;
            int orderB = b != null && b.Instance != null ? b.Instance.Order : 0;
            int cmp = orderA.CompareTo(orderB);
            if (cmp != 0) return cmp;
            return string.Compare(a != null ? a.ClassName : "", b != null ? b.ClassName : "", StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(IPlugin plugin)
        {
            if (plugin == null) return "";

            // Many plugins expose a Description property even though the base
            // contract does not require one.
            try
            {
                System.Reflection.PropertyInfo property =
                    plugin.GetType().GetProperty("Description",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(string))
                {
                    object value = property.GetValue(plugin, null);
                    return Convert.ToString(value);
                }
            }
            catch { }

            return "";
        }

        private void LoadDefaultAssemblyPlugins(Dictionary<string, bool> states)
        {
            string defaultDll = Path.Combine(AppPaths.AppRoot, "Turbo.Plugins.Default.dll");
            if (!File.Exists(defaultDll)) return;

            try
            {
                Assembly asm = Assembly.LoadFrom(defaultDll);
                Type pluginInterface = typeof(IPlugin);

                foreach (Type type in asm.GetExportedTypes())
                {
                    if (type.IsAbstract || !type.IsClass) continue;
                    if (!pluginInterface.IsAssignableFrom(type)) continue;

                    string relPath = "Default:" + type.FullName;
                    bool enabled;
                    bool known = states.TryGetValue(relPath, out enabled);

                    TurboPluginEntry entry = new TurboPluginEntry();
                    entry.FilePath = defaultDll;
                    entry.RelativePath = relPath;
                    entry.ClassName = type.Name;
                    entry.Compiled = true;

                    try
                    {
                        IPlugin instance = (IPlugin)Activator.CreateInstance(type);
                        entry.Instance = instance;
                        entry.Description = Describe(instance);

                        if (!known)
                        {
                            // A fresh installation starts with the defaults of
                            // the plugins themselves, the same as the original
                            // TurboHUD: the optional panels stay off.
                            enabled = instance.Enabled;
                            if (type.Name.Equals("GameInfoPlugin", StringComparison.OrdinalIgnoreCase) ||
                                type.Name.Equals("DebugPlugin", StringComparison.OrdinalIgnoreCase))
                            {
                                // They need parts of the engine that the host
                                // does not have and would draw over the game.
                                enabled = false;
                            }
                        }
                        entry.Enabled = enabled;

                        _controller.RegisterPlugin(instance);
                        MarkCurrent(entry, TurboPluginEntry.StageLoad);
                        try { instance.Load(_controller); }
                        finally { ClearCurrent(); }
                    }
                    catch (Exception ex)
                    {
                        entry.Error = "Load: " + ex.Message;
                    }

                    _plugins.Add(entry);
                    _byPath[entry.RelativePath] = entry;
                }

                _plugins.Sort(ComparePluginOrder);
            }
            catch (Exception ex)
            {
                Emit("TurboHUD default plugins: " + ex.Message);
            }
        }

        private int CountLoaded()
        {
            int n = 0;
            foreach (TurboPluginEntry entry in _plugins) if (entry.Instance != null) n++;
            return n;
        }

        private string MakeRelative(string file)
        {
            try
            {
                string full = Path.GetFullPath(file);
                string root = Path.GetFullPath(_folder).TrimEnd('\\') + "\\";
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(root.Length);
                return Path.GetFileName(file);
            }
            catch { return Path.GetFileName(file); }
        }

        private static readonly string[] DemoFiles =
        {
            "Stage1Demo.cs", "Stage2Demo.cs", "Stage3Demo.cs", "Stage4Demo.cs",
            "SuperD3HudStatusPlugin.cs"
        };

        private void EnsureExample()
        {
            try
            {
                // Only the working folder is prepared. An explicit folder (for
                // example the shipped examples) is used as-is.
                if (!string.Equals(Path.GetFileName(_folder), DefaultFolderName,
                                   StringComparison.OrdinalIgnoreCase))
                    return;

                // The folder is created with a short note, not with plugins:
                // the built-in HUD already draws the game, and a user's own
                // TurboHUD plugins should be the only ones running. The stage
                // demos live in examples\turbohud and can be copied on demand.
                MigrateOldDemos();

                string readme = Path.Combine(_folder, "readme.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, FolderReadme, new UTF8Encoding(true));

                string marker = Path.Combine(_folder, ".created");
                if (!File.Exists(marker))
                    File.WriteAllText(marker, "created " + DateTime.Now.ToString("u"), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Removes the demo plugins that earlier versions copied here, but only
        /// when the folder still contains nothing but those exact files.
        /// </summary>
        private void MigrateOldDemos()
        {
            try
            {
                string marker = Path.Combine(_folder, ".created");
                if (!File.Exists(marker)) return;

                string[] files = Directory.GetFiles(_folder, "*.cs", SearchOption.AllDirectories);
                if (files.Length == 0) return;

                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    bool known = false;
                    for (int i = 0; i < DemoFiles.Length; i++)
                        if (string.Equals(name, DemoFiles[i], StringComparison.OrdinalIgnoreCase))
                            known = true;
                    if (!known) return;
                }

                foreach (string file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        private const string FolderReadme =
@"SuperD3Hud - плагины TurboHUD
============================

Сюда можно положить .cs-файлы, написанные для публичного plugin-API TurboHUD.
SuperD3Hud компилирует их сам при старте и вызывает те же хуки, что и
оригинальный HUD: AfterCollect, BeforeRender, PaintTopInGame, PaintWorld.
Сторонний движок TurboHUD для этого не нужен.

Плагин - обычный класс, реализующий Turbo.Plugins.IPlugin (обычно через
Turbo.Plugins.Default.BasePlugin). Пример:

    using Turbo.Plugins;
    using Turbo.Plugins.Default;

    public class MyPlugin : BasePlugin, IInGameTopPainter
    {
        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip) return;
            if (!Hud.Game.IsInGame) return;
            // Hud.Game.Me, Hud.Game.Monsters, Hud.Sno, Hud.Render ...
        }
    }

Готовые примеры по этапам API лежат в папке examples\turbohud рядом с
программой. Скопируйте нужный файл сюда, чтобы включить его.

Проверка: SuperD3Hud.exe --turbohud
Живая проверка на запущенной игре: SuperD3Hud.exe --turbohud-live
";

        private const string ExamplePlugin = @"using System;
using System.Drawing;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>
    /// Показывает состояние совместимости TurboHUD: позицию игрока,
    /// число монстров и подсказку о версии API.
    /// </summary>
    public class SuperD3HudStatusPlugin : BasePlugin, IAfterCollectHandler, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return ""Показывает состояние хоста TurboHUD: позицию игрока и число монстров.""; }
        }

        private IFont _font;
        private IFont _small;
        private IBrush _shadow;

        public override void Load(IController hud)
        {
            base.Load(hud);
            _font = Hud.Render.CreateFont(""Segoe UI"", 16f, 255, 232, 163, 61, true, false, true);
            _small = Hud.Render.CreateFont(""Segoe UI"", 11f, 220, 220, 220, 220, false, false, true);
            _shadow = Hud.Render.CreateBrush(180, 0, 0, 0, 2f);
        }

        public void AfterCollect()
        {
            // Данные уже собраны хостом; здесь плагин может подготовить кэш.
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip) return;
            if (!Hud.Game.IsInGame) return;

            float y = 40f;
            _shadow.DrawRectangle(20f, y - 4f, 420f, 66f);
            _font.DrawText(""SuperD3Hud — TurboHUD API"", 28f, y);

            IPlayer me = Hud.Game.Me;
            string position = me == null
                ? ""нет данных""
                : string.Format(""X {0:0}  Y {1:0}"", me.FloorCoordinate.X, me.FloorCoordinate.Y);

            int monsters = 0;
            foreach (IMonster m in Hud.Game.AliveMonsters) monsters++;

            _small.DrawText(""Игрок: "" + position + ""   Монстров: "" + monsters, 28f, y + 24f);
        }
    }
}
";

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private string StatesPath
        {
            get { return Path.Combine(AppPaths.ConfigDir, "turbohud_state.txt"); }
        }

        private Dictionary<string, bool> LoadStates()
        {
            Dictionary<string, bool> states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(StatesPath)) return states;
                foreach (string raw in File.ReadAllLines(StatesPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length < 3) continue;
                    bool enabled = line[0] == 'E' || line[0] == 'e';
                    states[line.Substring(2).Trim()] = enabled;
                }
            }
            catch { }
            return states;
        }

        private void SaveStates()
        {
            try
            {
                StringBuilder text = new StringBuilder();
                lock (_sync)
                {
                    foreach (TurboPluginEntry entry in _plugins)
                        text.Append(entry.Enabled ? "E" : "D").Append('|').AppendLine(entry.RelativePath);
                }
                Directory.CreateDirectory(AppPaths.ConfigDir);
                File.WriteAllText(StatesPath, text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public void SetEnabled(TurboPluginEntry entry, bool enabled)
        {
            if (entry == null) return;
            lock (_sync)
            {
                if (entry.Enabled == enabled) return;
                entry.Enabled = enabled;
                // Switching a plugin back on is a request to try it again, so
                // it also lifts a suspension from repeated failures.
                if (enabled) ResumeEntry(entry);
            }
            SaveStates();
        }

        private void UnloadInstance(TurboPluginEntry entry)
        {
            if (entry == null || entry.Instance == null) return;
            try { entry.Instance.Enabled = false; }
            catch { }
            entry.Instance = null;
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        /// <summary>
        /// Overrides the player position the plugins draw with. The actor poll
        /// is slow, so the caller passes the smoothed position of the frame.
        /// </summary>
        public void SetPlayerPosition(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y)) return;
            if (float.IsInfinity(x) || float.IsInfinity(y)) return;
            _context.PlayerX = x;
            _context.PlayerY = y;
            _context.PlayerKnown = true;
        }

        /// <summary>
        /// Copies the current game state into the host context and calls the
        /// data-collection hooks. Call this on the UI thread, once per frame.
        /// </summary>
        public void Tick(GameService service, bool windowForeground,
                         int windowX, int windowY, int windowWidth, int windowHeight,
                         bool largeMapOpen = false)
        {
            if (!_loaded) return;

            _context.Update(service, windowForeground, windowX, windowY, windowWidth, windowHeight, largeMapOpen);
            UpdateUiScale(windowHeight);

            List<TurboPluginEntry> snapshot;
            lock (_sync) snapshot = new List<TurboPluginEntry>(_plugins);

            int world = _context.WorldId;
            if (_lastWorldId != NoWorld && world != _lastWorldId)
                ResumeSuspended(snapshot, "world change");
            _lastWorldId = world;

            for (int i = 0; i < snapshot.Count; i++)
            {
                TurboPluginEntry entry = snapshot[i];
                RunHook(entry, TurboPluginEntry.StageAfterCollect);
                RunHook(entry, TurboPluginEntry.StageBeforeRender);
            }
        }

        /// <summary>
        /// Draws every enabled plugin on the given surface. The surface is the
        /// same layered overlay the built-in HUD uses.
        /// </summary>
        public void Draw(Graphics graphics, int width, int height)
        {
            if (!_loaded || graphics == null || _controller == null) return;

            // Lets the canvas age its per-frame caches (the TextLayout sweep)
            // before any plugin asks for a layout in this frame. A failure
            // here must not cost the frame.
            try { _canvas.BeginFrame(); }
            catch (Exception ex) { Emit("TurboHUD canvas: " + ex.Message); }

            UpdateUiScale(height);
            _canvas.Begin(graphics);
            try
            {
                List<TurboPluginEntry> snapshot;
                lock (_sync) snapshot = new List<TurboPluginEntry>(_plugins);

                // The passes run in TurboHUD order, each over all plugins, so
                // every plugin's ground layer lies under every plugin's map and
                // top layers: Ground (circles under monsters, floor decorators,
                // label registration), Map (minimap dots, shapes and markers),
                // then PaintTopInGame with BeforeClip, Inventory and AfterClip.
                for (int stage = TurboPluginEntry.StageWorldGround;
                     stage <= TurboPluginEntry.StageTopAfterClip; stage++)
                {
                    for (int i = 0; i < snapshot.Count; i++)
                        RunHook(snapshot[i], stage);
                }
            }
            finally
            {
                ClearCurrent();
                _canvas.End();
            }
        }

        /// <summary>
        /// Calls one hook of one plugin with the error throttling, the
        /// suspension and the timing of that hook. Plugins that do not
        /// implement the hook are skipped without touching its state.
        /// </summary>
        private void RunHook(TurboPluginEntry entry, int stage)
        {
            if (entry == null || !entry.Enabled) return;
            IPlugin instance = entry.Instance;
            if (instance == null) return;

            TurboHookState hook = entry.Hooks[stage];
            if (hook.Disabled) return;

            // Resolve the interface before starting the clock, so the cast is
            // not counted as plugin time and non-implementers stay untimed.
            IAfterCollectHandler afterCollect = null;
            IBeforeRenderHandler beforeRender = null;
            IInGameWorldPainter world = null;
            IInGameTopPainter top = null;
            switch (stage)
            {
                case TurboPluginEntry.StageAfterCollect:
                    afterCollect = instance as IAfterCollectHandler;
                    if (afterCollect == null) return;
                    break;
                case TurboPluginEntry.StageBeforeRender:
                    beforeRender = instance as IBeforeRenderHandler;
                    if (beforeRender == null) return;
                    break;
                case TurboPluginEntry.StageWorldGround:
                case TurboPluginEntry.StageWorldMap:
                    world = instance as IInGameWorldPainter;
                    if (world == null) return;
                    break;
                default:
                    top = instance as IInGameTopPainter;
                    if (top == null) return;
                    break;
            }

            MarkCurrent(entry, stage);
            long start = Stopwatch.GetTimestamp();
            bool failed = false;
            try
            {
                switch (stage)
                {
                    case TurboPluginEntry.StageAfterCollect: afterCollect.AfterCollect(); break;
                    case TurboPluginEntry.StageBeforeRender: beforeRender.BeforeRender(); break;
                    case TurboPluginEntry.StageWorldGround: world.PaintWorld(WorldLayer.Ground); break;
                    case TurboPluginEntry.StageWorldMap: world.PaintWorld(WorldLayer.Map); break;
                    case TurboPluginEntry.StageTopBeforeClip: top.PaintTopInGame(ClipState.BeforeClip); break;
                    case TurboPluginEntry.StageTopInventory: top.PaintTopInGame(ClipState.Inventory); break;
                    default: top.PaintTopInGame(ClipState.AfterClip); break;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                OnHookFailed(entry, stage, hook, ex);
            }
            finally
            {
                ClearCurrent();
            }

            // A failing call is timed too: a first-chance exception on every
            // frame is part of what the plugin costs.
            RecordTime(entry, stage, hook, Stopwatch.GetTimestamp() - start);
            if (!failed) hook.ConsecutiveFailures = 0;
        }

        /// <summary>
        /// Logs a hook failure without flooding: a new error text is written
        /// once, repeats are only counted and summarised at most once per
        /// minute, and after <see cref="TurboHookState.SuspendAfterFailures"/>
        /// consecutive failures the hook is suspended until the world changes.
        /// Logging used to happen on every frame, which meant 30+ synchronous
        /// log writes per second on the UI thread for one broken plugin.
        /// </summary>
        private void OnHookFailed(TurboPluginEntry entry, int stage, TurboHookState hook, Exception ex)
        {
            string text = ex.GetType().Name + ": " + ex.Message;
            string stageName = TurboPluginEntry.StageNames[stage];
            long now = Stopwatch.GetTimestamp();

            hook.ConsecutiveFailures++;
            hook.TotalFailures++;
            entry.Error = stageName + ": " + text;

            if (!string.Equals(text, hook.LastError, StringComparison.Ordinal))
            {
                // A different error: close the previous run's count first, so
                // the log still says how often the old error happened.
                if (hook.SuppressedCount > 0 && hook.LastError != null)
                    Emit("Plugin " + entry.ClassName + ": " + stageName + ": previous error repeated x" +
                         hook.SuppressedCount + ": " + hook.LastError);

                hook.LastError = text;
                hook.SuppressedCount = 0;
                hook.LastSummaryTimestamp = now;
                Emit("Plugin " + entry.ClassName + ": " + entry.Error + FirstStackFrame(ex));
            }
            else
            {
                hook.SuppressedCount++;
                if (now - hook.LastSummaryTimestamp >= TurboHookState.SummaryIntervalSeconds * Stopwatch.Frequency)
                {
                    Emit("Plugin " + entry.ClassName + ": " + stageName + ": still failing x" +
                         hook.SuppressedCount + " in the last " + TurboHookState.SummaryIntervalSeconds +
                         " s: " + text);
                    hook.SuppressedCount = 0;
                    hook.LastSummaryTimestamp = now;
                }
            }

            if (hook.ConsecutiveFailures >= TurboHookState.SuspendAfterFailures && !hook.Disabled)
            {
                hook.Disabled = true;
                hook.TimesSuspended++;
                // The repeats so far go into this line, so they are not lost
                // while the hook is silent.
                Emit("Plugin " + entry.ClassName + ": " + stageName + " suspended after " +
                     hook.ConsecutiveFailures + " consecutive failures (" + hook.TotalFailures +
                     " in total, last: " + text + "); it is retried after the next world change.");
                hook.SuppressedCount = 0;
                hook.LastSummaryTimestamp = now;
            }
        }

        /// <summary>" [at Type.Method(...)]": where the plugin threw, for the first log line of an error.</summary>
        private static string FirstStackFrame(Exception ex)
        {
            try
            {
                string trace = ex.StackTrace;
                if (string.IsNullOrEmpty(trace)) return "";
                int end = trace.IndexOf('\n');
                string line = (end >= 0 ? trace.Substring(0, end) : trace).Trim();
                return line.Length > 0 ? " [" + line + "]" : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Adds one call time to the hook's window. When the window is full
        /// its p95 is computed, and a hook over the budget is reported at
        /// most once per minute. The p95 rather than the mean is used because
        /// a hitch every few frames is what the player sees as stutter, while
        /// a few slow calls (JIT, first font creation) should not count.
        /// </summary>
        private void RecordTime(TurboPluginEntry entry, int stage, TurboHookState hook, long ticks)
        {
            float ms = (float)(ticks * 1000.0 / Stopwatch.Frequency);
            if (hook.Samples == null) hook.Samples = new float[TurboHookState.SampleWindow];
            hook.Samples[hook.SampleCount++] = ms;
            if (hook.SampleCount < TurboHookState.SampleWindow) return;

            hook.SampleCount = 0;
            Array.Copy(hook.Samples, _sortScratch, TurboHookState.SampleWindow);
            Array.Sort(_sortScratch);
            hook.P95Ms = _sortScratch[(int)((TurboHookState.SampleWindow - 1) * 0.95)];
            hook.MaxMs = _sortScratch[TurboHookState.SampleWindow - 1];

            if (hook.P95Ms <= TurboHookState.SlowP95Ms) return;
            long now = Stopwatch.GetTimestamp();
            if (hook.LastSlowWarnTimestamp != 0 &&
                now - hook.LastSlowWarnTimestamp < TurboHookState.SummaryIntervalSeconds * Stopwatch.Frequency)
                return;
            hook.LastSlowWarnTimestamp = now;

            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            Emit("Plugin " + entry.ClassName + ": " + TurboPluginEntry.StageNames[stage] + " is slow: p95 " +
                 hook.P95Ms.ToString("0.0", inv) + " ms, max " + hook.MaxMs.ToString("0.0", inv) +
                 " ms over the last " + TurboHookState.SampleWindow + " calls (budget " +
                 TurboHookState.SlowP95Ms.ToString("0", inv) + " ms).");
        }

        /// <summary>Re-enables every suspended hook and logs one line when there was any.</summary>
        private void ResumeSuspended(List<TurboPluginEntry> entries, string reason)
        {
            int resumed = 0;
            foreach (TurboPluginEntry entry in entries)
                resumed += ResumeEntry(entry);
            if (resumed > 0)
                Emit("TurboHUD: " + resumed + " suspended plugin hook(s) re-enabled after " + reason + ".");
        }

        /// <summary>
        /// Re-enables the suspended hooks of the entry and returns how many
        /// there were. Their LastError is cleared as well, so a plugin that
        /// fails again in the new world is logged once more.
        /// </summary>
        private static int ResumeEntry(TurboPluginEntry entry)
        {
            if (entry == null) return 0;
            int resumed = 0;
            foreach (TurboHookState hook in entry.Hooks)
            {
                if (!hook.Disabled) continue;
                resumed++;
                hook.Disabled = false;
                hook.ConsecutiveFailures = 0;
                hook.SuppressedCount = 0;
                hook.LastError = null;
            }
            return resumed;
        }

        /// <summary>Re-enables all suspended hooks now, for example from a UI button.</summary>
        public void ResumeSuspendedHooks()
        {
            List<TurboPluginEntry> snapshot;
            lock (_sync) snapshot = new List<TurboPluginEntry>(_plugins);
            ResumeSuspended(snapshot, "a manual reset");
        }

        /// <summary>
        /// Calls ICustomizer.Customize() on every enabled plugin, in Order
        /// sequence, once all plugins are loaded. TurboHUD does the same: a
        /// customizer changes other plugins' settings, so it must run after
        /// their Load() created the defaults it overrides.
        /// </summary>
        private void RunCustomizers()
        {
            List<TurboPluginEntry> snapshot;
            lock (_sync) snapshot = new List<TurboPluginEntry>(_plugins);

            foreach (TurboPluginEntry entry in snapshot)
            {
                if (!entry.Enabled || entry.Instance == null) continue;
                ICustomizer customizer = entry.Instance as ICustomizer;
                if (customizer == null) continue;

                MarkCurrent(entry, TurboPluginEntry.StageCustomize);
                try { customizer.Customize(); }
                catch (Exception ex)
                {
                    entry.Error = "Customize: " + ex.GetType().Name + ": " + ex.Message;
                    Emit("Plugin " + entry.ClassName + ": " + entry.Error + FirstStackFrame(ex));
                }
                finally { ClearCurrent(); }
            }
        }

        public void UnloadAll()
        {
            lock (_sync)
            {
                foreach (TurboPluginEntry entry in _plugins) UnloadInstance(entry);
            }
            _loaded = false;
        }

        public void Dispose()
        {
            UnloadAll();
            try { _canvas.Dispose(); } catch { }
        }

        private void Emit(string text)
        {
            Action<string> handler = Log;
            if (handler != null)
            {
                try { handler(text); }
                catch { }
            }
        }
    }
}
