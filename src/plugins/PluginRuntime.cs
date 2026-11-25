using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SuperD3Hud.Plugins
{
    /// <summary>A plugin instance together with its source and settings.</summary>
    public class PluginEntry
    {
        public string FilePath;
        public string RelativePath;
        public string ClassName;
        public string Description;
        public string Author;
        public string Version;
        public bool Enabled;
        public bool Compiled;
        public string Error;
        public HudPlugin Instance;

        public string StateText
        {
            get
            {
                if (!Compiled) return "Ошибка";
                return Enabled ? "Включён" : "Отключён";
            }
        }
    }

    /// <summary>
    /// The plugin host of SuperD3Hud. It compiles the .cs files of the local
    /// plugin folder, creates one instance of every plugin and calls its hooks
    /// while the game runs. Nothing here depends on a third-party HUD.
    /// </summary>
    public class PluginRuntime : IDisposable
    {
        public const string DefaultFolderName = "plugins-local";

        private readonly object _sync = new object();
        private readonly List<PluginEntry> _plugins = new List<PluginEntry>();
        private readonly Dictionary<string, PluginEntry> _byPath =
            new Dictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IPluginContext> _contexts =
            new Dictionary<string, IPluginContext>(StringComparer.OrdinalIgnoreCase);

        private readonly GameState _game = new GameState();
        private readonly string _folder;
        private DateTime _lastTick = DateTime.UtcNow;
        private bool _loaded;

        public event Action<string> Log;

        public PluginRuntime()
        {
            _folder = Path.Combine(AppPaths.AppRoot, DefaultFolderName);
        }

        public string Folder { get { return _folder; } }

        public IGameState Game { get { return _game; } }

        /// <summary>Snapshot of the plugin list, safe to read from the UI thread.</summary>
        public List<PluginEntry> Plugins
        {
            get { lock (_sync) return new List<PluginEntry>(_plugins); }
        }

        /// <summary>True when at least one plugin is enabled and can draw.</summary>
        public bool AnyEnabled
        {
            get
            {
                lock (_sync)
                {
                    foreach (PluginEntry entry in _plugins)
                        if (entry.Enabled && entry.Instance != null) return true;
                    return false;
                }
            }
        }

        // ------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates the plugin folder with two examples on first run, then
        /// compiles and instantiates everything that is enabled.
        /// </summary>
        public void Load()
        {
            try { Directory.CreateDirectory(_folder); }
            catch { }

            EnsureExamples();

            lock (_sync)
            {
                foreach (PluginEntry entry in _plugins)
                    UnloadInstance(entry);
                _plugins.Clear();
                _byPath.Clear();
                _contexts.Clear();
            }

            List<CompiledPlugin> compiled = PluginCompiler.CompileFolder(_folder, Emit);
            Dictionary<string, bool> states = LoadStates();

            lock (_sync)
            {
                foreach (CompiledPlugin result in compiled)
                {
                    PluginEntry entry = new PluginEntry();
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
                            HudPlugin instance = (HudPlugin)Activator.CreateInstance(result.PluginType);
                            entry.Instance = instance;

                            // A plugin can ask to start disabled, which is
                            // useful for examples and experimental plugins.
                            if (!known) entry.Enabled = instance.EnabledByDefault;
                            enabled = entry.Enabled;

                            entry.Description = instance.Description;
                            entry.Author = instance.Author;
                            entry.Version = instance.Version;

                            PluginContext context = new PluginContext(
                                result.ClassName, Emit, _game, AppPaths.PluginSettingsDir);
                            _contexts[entry.RelativePath] = context;

                            if (enabled)
                            {
                                instance.Context = context;
                                try { instance.Load(); }
                                catch (Exception ex) { entry.Error = "Load: " + ex.Message; }
                            }
                        }
                        catch (Exception ex)
                        {
                            entry.Error = "Создание: " + ex.Message;
                        }
                    }
                    else
                    {
                        Emit("Плагин " + entry.ClassName + ": " + entry.Error);
                    }

                    if (string.IsNullOrEmpty(entry.Description) && entry.Instance == null)
                        entry.Description = "Плагин не скомпилирован: " + (entry.Error ?? "неизвестная ошибка");

                    _plugins.Add(entry);
                    _byPath[entry.RelativePath] = entry;
                }
            }

            _plugins.Sort(delegate(PluginEntry a, PluginEntry b)
            {
                return string.Compare(a.ClassName, b.ClassName, StringComparison.OrdinalIgnoreCase);
            });

            _loaded = true;
            Emit("Плагины SuperD3Hud: загружено " + CountLoaded() + " из " + _plugins.Count + ".");
        }

        private int CountLoaded()
        {
            int n = 0;
            foreach (PluginEntry entry in _plugins) if (entry.Instance != null) n++;
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

        /// <summary>
        /// Creates two small example plugins the first time the folder is used,
        /// so the plugin system is visible and testable right away.
        /// </summary>
        private void EnsureExamples()
        {
            try
            {
                string clockPath = Path.Combine(_folder, "ClockPlugin.cs");
                if (File.Exists(clockPath)) { try { File.Delete(clockPath); } catch { } }

                string marker = Path.Combine(_folder, ".created");
                if (File.Exists(marker)) return;
                if (Directory.GetFiles(_folder, "*.cs", SearchOption.AllDirectories).Length > 0)
                {
                    File.WriteAllText(marker, "plugins already present", Encoding.UTF8);
                    return;
                }

                File.WriteAllText(Path.Combine(_folder, "SessionTimerPlugin.cs"),
                    ExampleTimer, new UTF8Encoding(true));
                File.WriteAllText(marker, "created " + DateTime.Now.ToString("u"), Encoding.UTF8);
            }
            catch { }
        }

        private const string ExampleClock = @"using System;
using System.Drawing;
using SuperD3Hud.Plugins;

namespace LocalPlugins
{
    /// <summary>Показывает текущее время в углу экрана.</summary>
    public class ClockPlugin : HudPlugin
    {
        public override bool EnabledByDefault { get { return false; } }

        public override string Description
        {
            get { return ""Показывает часы в правом нижнем углу, чтобы не отвлекаться от игры.""; }
        }

        public override void Draw(IPluginCanvas canvas)
        {
            string text = DateTime.Now.ToString(""HH:mm"");
            float size = 15f;
            SizeF box = canvas.MeasureText(text, size, true);
            float x = canvas.Width - box.Width - 24;
            float y = canvas.Height - box.Height - 60;

            canvas.FillRect(x - 8, y - 4, box.Width + 16, box.Height + 8, Color.FromArgb(150, 0, 0, 0));
            canvas.DrawText(text, x, y, Color.FromArgb(240, 240, 240), size, true);
        }
    }
}
";

        private const string ExampleTimer = @"using System;
using System.Drawing;
using SuperD3Hud.Plugins;

namespace LocalPlugins
{
    /// <summary>Считает время с момента запуска лаунчера.</summary>
    public class SessionTimerPlugin : HudPlugin
    {
        private double _seconds;

        public override string Description
        {
            get { return ""Показывает, сколько времени прошло с момента запуска программы."" ; }
        }

        public override bool EnabledByDefault { get { return false; } }

        public override void Update(double seconds)
        {
            _seconds += seconds;
        }

        public override void Draw(IPluginCanvas canvas)
        {
            int total = (int)_seconds;
            string text = string.Format(""{0:00}:{1:00}:{2:00}"", total / 3600, (total / 60) % 60, total % 60);
            canvas.DrawText(text, 20, 20, Color.FromArgb(220, 232, 163, 61), 12f, false);
        }
    }
}
";

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private string StatesPath
        {
            get { return Path.Combine(AppPaths.ConfigDir, "plugins_local_state.txt"); }
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
                    foreach (PluginEntry entry in _plugins)
                    {
                        text.Append(entry.Enabled ? "E" : "D").Append('|').AppendLine(entry.RelativePath);
                    }
                }
                Directory.CreateDirectory(AppPaths.ConfigDir);
                File.WriteAllText(StatesPath, text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>Enables or disables a plugin and remembers the choice.</summary>
        public void SetEnabled(PluginEntry entry, bool enabled)
        {
            if (entry == null) return;
            lock (_sync)
            {
                if (entry.Enabled == enabled) return;
                entry.Enabled = enabled;

                if (entry.Instance != null)
                {
                    IPluginContext context;
                    _contexts.TryGetValue(entry.RelativePath, out context);

                    if (enabled)
                    {
                        entry.Instance.Context = context;
                        try { entry.Instance.Load(); }
                        catch (Exception ex) { Emit("Плагин " + entry.ClassName + ": " + ex.Message); }
                    }
                    else
                    {
                        try { entry.Instance.Unload(); }
                        catch { }
                    }
                }
            }
            SaveStates();
        }

        private void UnloadInstance(PluginEntry entry)
        {
            if (entry == null || entry.Instance == null) return;
            try { entry.Instance.Unload(); }
            catch { }
            entry.Instance = null;
        }

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        /// <summary>Feeds the current game state and calls Update on the plugins.</summary>
        public void Tick(bool gameRunning, bool mapAvailable, int sceneCount, int cellCount,
                         bool playerKnown, double playerX, double playerY, int worldId, string areaName)
        {
            Tick(gameRunning, mapAvailable, sceneCount, cellCount, playerKnown,
                 playerX, playerY, worldId, areaName, null, null, null, null, -1, 0, 0);
        }

        /// <summary>
        /// Feeds the current game state, including the HUD data, and calls
        /// Update on every enabled plugin.
        /// </summary>
        public void Tick(bool gameRunning, bool mapAvailable, int sceneCount, int cellCount,
                         bool playerKnown, double playerX, double playerY, int worldId, string areaName,
                         IList<SuperD3Hud.Game.MonsterMarker> monsters,
                         IList<SuperD3Hud.Game.LootMarker> loot,
                         IList<SuperD3Hud.Game.BuffEntry> buffs,
                         IList<SuperD3Hud.Game.SkillCooldown> skills,
                         double health, int inventoryUsed, int inventoryTotal)
        {
            if (!_loaded) return;

            DateTime now = DateTime.UtcNow;
            double seconds = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            if (seconds < 0 || seconds > 2) seconds = 0.1;

            _game.Set(gameRunning, mapAvailable, sceneCount, cellCount,
                      playerKnown, playerX, playerY, worldId, areaName);
            _game.SetHud(monsters, loot, buffs, skills, health, inventoryUsed, inventoryTotal);

            List<PluginEntry> snapshot;
            lock (_sync) snapshot = new List<PluginEntry>(_plugins);

            foreach (PluginEntry entry in snapshot)
            {
                if (!entry.Enabled || entry.Instance == null) continue;
                try { entry.Instance.Update(seconds); }
                catch (Exception ex)
                {
                    entry.Error = "Update: " + ex.Message;
                    Emit("Плагин " + entry.ClassName + ": " + entry.Error);
                }
            }
        }

        /// <summary>Draws every enabled plugin on the canvas.</summary>
        public void Draw(IPluginCanvas canvas)
        {
            if (!_loaded || canvas == null) return;

            List<PluginEntry> snapshot;
            lock (_sync) snapshot = new List<PluginEntry>(_plugins);

            foreach (PluginEntry entry in snapshot)
            {
                if (!entry.Enabled || entry.Instance == null) continue;
                try { entry.Instance.Draw(canvas); }
                catch (Exception ex)
                {
                    entry.Error = "Draw: " + ex.Message;
                    Emit("Плагин " + entry.ClassName + ": " + entry.Error);
                }
            }
        }

        public void UnloadAll()
        {
            lock (_sync)
            {
                foreach (PluginEntry entry in _plugins) UnloadInstance(entry);
            }
            _loaded = false;
        }

        public void Dispose()
        {
            UnloadAll();
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
