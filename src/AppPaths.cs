using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SuperD3Hud
{
    /// <summary>
    /// Central place for all paths used by the suite. Everything is resolved
    /// relative to the folder where the launcher executable lives.
    /// </summary>
    public static class AppPaths
    {
        public static string AppRoot { get; private set; }
        public static AppSettings Settings { get; private set; }
        public static bool ReadOnly { get; private set; }

        public static string ConfigDir { get { return Path.Combine(AppRoot, "config"); } }
        public static string BackupsDir { get { return Path.Combine(ConfigDir, "backups"); } }
        public static string PluginSettingsDir { get { return Path.Combine(ConfigDir, "plugin_settings"); } }
        public static string SettingsFile { get { return Path.Combine(ConfigDir, "settings.ini"); } }
        public static string MacrosFile { get { return Path.Combine(ConfigDir, "macros.json"); } }
        public static string NotesFile { get { return Path.Combine(ConfigDir, "plugin_notes.json"); } }

        public static void Initialize()
        {
            Initialize(false);
        }

        /// <summary>
        /// Resolves all paths and loads the settings. In read-only mode (used by
        /// the command-line diagnostics) no file is written: directories are
        /// still created, but the settings are never saved back.
        /// </summary>
        public static void Initialize(bool readOnly)
        {
            ReadOnly = readOnly;
            AppRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(AppRoot)) AppRoot = Environment.CurrentDirectory;
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(PluginSettingsDir);
            Settings = AppSettings.Load();
            if (string.IsNullOrEmpty(Settings.HudRoot)) Settings.HudRoot = FindHudRoot();
            if (string.IsNullOrEmpty(Settings.D3Path)) Settings.D3Path = FindGameExe();
            if (!ReadOnly) Settings.Save();
        }

        /// <summary>
        /// One-time preparation of a fresh install: the map is switched on, the
        /// game path is filled in and a shortcut is placed next to the program
        /// folder. Everything is best-effort, so a failure never blocks start-up.
        /// </summary>
        public static void EnsureFirstRun()
        {
            try
            {
                if (Settings.ShortcutCreated) return;

                Shortcut.CreateNextToProgramFolder();
                Settings.ShortcutCreated = true;
                Settings.Save();
            }
            catch { }
        }

        public static void ReloadSettings()
        {
            Settings = AppSettings.Load();
        }

        /// <summary>Test hook: redirect all paths to a sandbox folder.</summary>
        internal static void UseSandbox(string root)
        {
            AppRoot = root;
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(PluginSettingsDir);
            Settings = new AppSettings();
            Settings.HudRoot = Path.Combine(root, "hud");
            Settings.D3Path = "";
            Settings.Save();
        }

        // ------------------------------------------------------------------
        // HUD engine
        // ------------------------------------------------------------------

        public static string HudRoot
        {
            get { return NormalizeHudRoot(Settings.HudRoot); }
        }

        /// <summary>
        /// Folder that holds the engine executable. The engine may sit either
        /// directly in the configured folder or in a nested engine folder.
        /// </summary>
        public static string HudDir
        {
            get
            {
                string root = HudRoot;
                if (string.IsNullOrEmpty(root)) return "";

                if (HudEngine.IsEngineFolder(root)) return root;

                foreach (string name in HudEngine.FolderNames)
                {
                    string nested = Path.Combine(root, name);
                    if (HudEngine.IsEngineFolder(nested)) return nested;
                }
                return root;
            }
        }

        /// <summary>Full path of the engine executable, or an empty string.</summary>
        public static string HudExe
        {
            get
            {
                string configured = Settings.HudExePath;
                if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                    return configured;

                string dir = HudDir;
                if (string.IsNullOrEmpty(dir)) return "";
                string found = HudEngine.FindExecutable(dir);
                if (!string.IsNullOrEmpty(found)) return found;

                // Keep the expected name so callers can show a useful message.
                return Path.Combine(dir, HudEngine.ExecutableNames[0]);
            }
        }

        public static string HudIni { get { return Combine(HudRoot, "hud.ini"); } }
        public static string PluginsConfig { get { return Combine(HudRoot, "PluginsConfig.dat"); } }
        /// <summary>
        /// Folder with the plugins of a classic HUD engine.
        ///
        /// When no engine is installed the folder is created next to the
        /// program itself: a portable installation keeps everything together,
        /// and the plugin page still has something to show.
        /// </summary>
        public static string PluginsDir
        {
            get
            {
                string dir = HudDir;
                if (string.IsNullOrEmpty(dir)) dir = AppRoot;
                return Combine(dir, "plugins");
            }
        }

        public static string DisabledPluginsDir
        {
            get
            {
                string dir = HudDir;
                if (string.IsNullOrEmpty(dir)) dir = AppRoot;
                return Combine(dir, "plugins.disabled");
            }
        }

        public static string AppConfig { get { return Combine(HudDir, "app.config"); } }
        public static string RoslynDir { get { return Combine(HudDir, "roslyn"); } }

        public static string HudConfigDir { get { return Combine(HudDir, "config"); } }
        public static string ConfigXml { get { return Combine(HudConfigDir, "config.xml"); } }
        public static string HotkeysXml { get { return Combine(HudConfigDir, "hotkeys.xml"); } }
        public static string PickitSc { get { return Combine(HudConfigDir, "pickit_sc_70.ini"); } }
        public static string PickitHc { get { return Combine(HudConfigDir, "pickit_hc_70.ini"); } }

        /// <summary>
        /// True when the folder contains a HUD engine, either directly or in a
        /// nested engine folder.
        /// </summary>
        public static bool IsHudRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return HudEngine.ContainsEngine(dir);
        }

        /// <summary>
        /// Turns a folder that points inside the engine into the outer folder
        /// that the launcher uses as its root.
        /// </summary>
        public static string NormalizeHudRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            dir = dir.Trim().TrimEnd('\\', '/');
            if (!Directory.Exists(dir)) return dir;

            if (!HudEngine.IsEngineFolder(dir)) return dir;

            string name = Path.GetFileName(dir);
            foreach (string engineFolder in HudEngine.FolderNames)
            {
                if (!string.Equals(name, engineFolder, StringComparison.OrdinalIgnoreCase)) continue;
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent != null) return parent.FullName;
            }
            return dir;
        }

        public static string FindHudRoot()
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(Settings.HudRoot)) candidates.Add(Settings.HudRoot);
            candidates.Add(AppRoot);

            // Walk up a few levels: an engine is often installed next to the
            // launcher or next to the folder that contains it.
            string current = AppRoot;
            for (int depth = 0; depth < 3; depth++)
            {
                string parent;
                try { parent = Path.GetFullPath(Path.Combine(current, "..")); }
                catch { break; }
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                candidates.Add(parent);
                current = parent;
            }

            foreach (string raw in candidates)
            {
                string c;
                try { c = Path.GetFullPath(raw); } catch { continue; }
                if (IsHudRoot(c)) return NormalizeHudRoot(c);
            }

            // Then look one level down from every ancestor, which covers an
            // engine that lives in its own folder beside the launcher.
            current = AppRoot;
            for (int depth = 0; depth < 3; depth++)
            {
                string parent;
                try { parent = Path.GetFullPath(Path.Combine(current, "..")); }
                catch { break; }
                if (string.IsNullOrEmpty(parent) || parent == current) break;

                string[] children;
                try { children = Directory.GetDirectories(parent); }
                catch { children = new string[0]; }

                foreach (string sub in children)
                {
                    try
                    {
                        if (IsHudRoot(sub)) return NormalizeHudRoot(sub);
                    }
                    catch { }
                }
                current = parent;
            }
            return "";
        }

        // ------------------------------------------------------------------
        // Diablo III
        // ------------------------------------------------------------------

        public static string FindGameExe()
        {
            string fromRegistry = FindGameFromRegistry();
            if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry;

            string[] common =
            {
                @"C:\Program Files (x86)\Diablo III\Diablo III.exe",
                @"C:\Program Files\Diablo III\Diablo III.exe",
                @"C:\Program Files (x86)\Diablo III\Diablo III64.exe",
                @"C:\Program Files\Diablo III\Diablo III64.exe",
                @"D:\Program Files (x86)\Diablo III\Diablo III.exe",
                @"D:\Program Files (x86)\Diablo III\Diablo III64.exe",
                @"D:\Program Files\Diablo III\Diablo III.exe",
                @"D:\Program Files\Diablo III\Diablo III64.exe",
                @"D:\Games\Diablo III\Diablo III.exe",
                @"D:\Games\Diablo III\Diablo III64.exe"
            };
            foreach (string p in common)
                if (File.Exists(p)) return p;
            return "";
        }

        private static string FindGameFromRegistry()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Diablo III",
                @"SOFTWARE\Blizzard Entertainment\Diablo III",
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Diablo III Public Test",
                @"SOFTWARE\Blizzard Entertainment\Diablo III Public Test"
            };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive hive in hives)
            {
                foreach (string keyName in keys)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                        using (RegistryKey key = baseKey.OpenSubKey(keyName))
                        {
                            if (key == null) continue;
                            object val = key.GetValue("InstallPath");
                            if (val == null) continue;
                            string dir = Convert.ToString(val);
                            if (string.IsNullOrEmpty(dir)) continue;
                            string[] names = { "Diablo III64.exe", "Diablo III.exe" };
                            foreach (string n in names)
                            {
                                string exe = Path.Combine(dir, n);
                                if (File.Exists(exe)) return exe;
                            }
                        }
                    }
                    catch { }
                }
            }
            return "";
        }

        /// <summary>
        /// Candidate folders where Diablo III may be installed. The 64-bit
        /// client lives in the "x64" subfolder of the game directory.
        /// </summary>
        public static List<string> GameInstallDirs()
        {
            List<string> dirs = new List<string>();

            string configured = Settings.D3Path;
            if (!string.IsNullOrEmpty(configured))
            {
                try
                {
                    if (Directory.Exists(configured)) dirs.Add(configured);
                    else
                    {
                        string parent = Path.GetDirectoryName(configured);
                        if (!string.IsNullOrEmpty(parent)) dirs.Add(parent);
                    }
                }
                catch { }
            }

            string fromRegistry = FindGameInstallDirFromRegistry();
            if (!string.IsNullOrEmpty(fromRegistry)) dirs.Add(fromRegistry);

            string[] common =
            {
                @"C:\Program Files (x86)\Diablo III",
                @"C:\Program Files\Diablo III",
                @"D:\Program Files (x86)\Diablo III",
                @"D:\Program Files\Diablo III",
                @"D:\Games\Diablo III",
                @"E:\Games\Diablo III",
                @"E:\Program Files (x86)\Diablo III"
            };
            foreach (string c in common) dirs.Add(c);

            List<string> unique = new List<string>();
            foreach (string d in dirs)
            {
                try
                {
                    if (Directory.Exists(d) && !unique.Contains(d)) unique.Add(d);
                }
                catch { }
            }
            return unique;
        }

        private static string FindGameInstallDirFromRegistry()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Diablo III",
                @"SOFTWARE\Blizzard Entertainment\Diablo III",
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\Diablo III Public Test",
                @"SOFTWARE\Blizzard Entertainment\Diablo III Public Test"
            };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive hive in hives)
            {
                foreach (string keyName in keys)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                        using (RegistryKey key = baseKey.OpenSubKey(keyName))
                        {
                            if (key == null) continue;
                            object val = key.GetValue("InstallPath");
                            if (val == null) continue;
                            string dir = Convert.ToString(val);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                        }
                    }
                    catch { }
                }
            }
            return "";
        }

        public static string Combine(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            return Path.Combine(dir, name);
        }
    }

    /// <summary>Persistent launcher settings (config/settings.ini).</summary>
    public class AppSettings
    {
        public string HudRoot { get; set; }
        public string HudExePath { get; set; }
        public string D3Path { get; set; }
        public string LaunchMode { get; set; }       // "engine" or "loader"
        public string CustomLauncher { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool CheckForUpdates { get; set; }
        public string UpdateRepo { get; set; }        // "owner/name" on GitHub
        public bool ShortcutCreated { get; set; }
        /// <summary>Show profiles and the HUD-engine pages in the sidebar.</summary>
        public bool AdvancedMode { get; set; }
        public MapSettings Map { get; set; }
        public Game.HudSettings Hud { get; set; }

        public AppSettings()
        {
            HudRoot = "";
            HudExePath = "";
            D3Path = "";
            LaunchMode = "engine";
            CustomLauncher = "";
            MinimizeToTray = true;
            CheckForUpdates = true;
            UpdateRepo = "";
            ShortcutCreated = false;
            AdvancedMode = false;
            Map = new MapSettings();
            Hud = new Game.HudSettings();
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            IniFile ini = new IniFile(AppPaths.SettingsFile);
            ini.Load();
            s.HudRoot = ini.Get("general", "HudRoot", s.HudRoot);
            s.HudExePath = ini.Get("general", "HudExePath", s.HudExePath);
            s.D3Path = ini.Get("general", "D3Path", s.D3Path);
            s.LaunchMode = ini.Get("general", "LaunchMode", s.LaunchMode);
            s.CustomLauncher = ini.Get("general", "CustomLauncher", s.CustomLauncher);
            s.MinimizeToTray = ini.GetBool("general", "MinimizeToTray", s.MinimizeToTray);
            s.CheckForUpdates = ini.GetBool("general", "CheckForUpdates", s.CheckForUpdates);
            s.UpdateRepo = ini.Get("general", "UpdateRepo", s.UpdateRepo);
            s.ShortcutCreated = ini.GetBool("general", "ShortcutCreated", s.ShortcutCreated);
            s.AdvancedMode = ini.GetBool("general", "AdvancedMode", s.AdvancedMode);

            MapSettings m = s.Map;
            m.Enabled = ini.GetBool("map", "Enabled", m.Enabled);
            m.ShowMinimap = ini.GetBool("map", "ShowMinimap", m.ShowMinimap);
            m.ShowPanel = ini.GetBool("map", "ShowPanel", m.ShowPanel);
            m.Scale = ReadDouble(ini, "map", "Scale", m.Scale);
            m.OffsetX = ReadDouble(ini, "map", "OffsetX", m.OffsetX);
            m.OffsetY = ReadDouble(ini, "map", "OffsetY", m.OffsetY);
            m.Opacity = (int)ReadDouble(ini, "map", "Opacity", m.Opacity);
            m.PanelX = (int)ReadDouble(ini, "map", "PanelX", m.PanelX);
            m.PanelY = (int)ReadDouble(ini, "map", "PanelY", m.PanelY);
            m.PanelSize = (int)ReadDouble(ini, "map", "PanelSize", m.PanelSize);
            m.PanelLocked = ini.GetBool("map", "PanelLocked", m.PanelLocked);

            m.ShowLargeMap = ini.GetBool("map", "ShowLargeMap", m.ShowLargeMap);
            m.LargeScale = ReadDouble(ini, "map", "LargeScale", m.LargeScale);
            m.LargeOffsetX = ReadDouble(ini, "map", "LargeOffsetX", m.LargeOffsetX);
            m.LargeOffsetY = ReadDouble(ini, "map", "LargeOffsetY", m.LargeOffsetY);
            m.LargeAuto = ini.GetBool("map", "LargeAuto", m.LargeAuto);
            m.LargeFit = ini.GetBool("map", "LargeFit", m.LargeFit);
            m.LargeFollow = ini.GetBool("map", "LargeFollow", m.LargeFollow);
            m.Configured = ini.GetBool("map", "Configured", m.Configured);

            s.Hud = new Game.HudSettings();
            s.Hud.Load(ini);

            // The map used to be off by default. A configuration written by an
            // older version has no "Configured" marker, so the new default
            // (working out of the box) is applied once.
            if (!m.Configured && !ini.GetBool("map", "Configured", false))
            {
                m.Enabled = true;
                m.ShowMinimap = true;
                m.ShowLargeMap = true;
                m.LargeAuto = true;
                // The big map follows the hero, like the map of the game does.
                // The mode that fits the whole area into the screen was the
                // old default, but it is a still picture: the hero does not
                // move with it.
                m.LargeFit = false;
                m.LargeFollow = true;
            }
            return s;
        }

        private static double ReadDouble(IniFile ini, string section, string key, double fallback)
        {
            string raw = ini.Get(section, key, "");
            double value;
            if (!string.IsNullOrEmpty(raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out value))
                return value;
            return fallback;
        }

        public void Save()
        {
            IniFile ini = new IniFile(AppPaths.SettingsFile);
            ini.Load();
            ini.Set("general", "HudRoot", HudRoot ?? "");
            ini.Set("general", "HudExePath", HudExePath ?? "");
            ini.Set("general", "D3Path", D3Path ?? "");
            ini.Set("general", "LaunchMode", LaunchMode ?? "engine");
            ini.Set("general", "CustomLauncher", CustomLauncher ?? "");
            ini.SetBool("general", "MinimizeToTray", MinimizeToTray);
            ini.SetBool("general", "CheckForUpdates", CheckForUpdates);
            ini.Set("general", "UpdateRepo", UpdateRepo ?? "");
            ini.SetBool("general", "ShortcutCreated", ShortcutCreated);
            ini.SetBool("general", "AdvancedMode", AdvancedMode);

            MapSettings m = Map ?? new MapSettings();
            ini.SetBool("map", "Enabled", m.Enabled);
            ini.SetBool("map", "ShowMinimap", m.ShowMinimap);
            ini.SetBool("map", "ShowPanel", m.ShowPanel);
            ini.Set("map", "Scale", m.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "OffsetX", m.OffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "OffsetY", m.OffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "Opacity", m.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "PanelX", m.PanelX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "PanelY", m.PanelY.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "PanelSize", m.PanelSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.SetBool("map", "PanelLocked", m.PanelLocked);
            ini.SetBool("map", "ShowLargeMap", m.ShowLargeMap);
            ini.Set("map", "LargeScale", m.LargeScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "LargeOffsetX", m.LargeOffsetX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.Set("map", "LargeOffsetY", m.LargeOffsetY.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ini.SetBool("map", "LargeAuto", m.LargeAuto);
            ini.SetBool("map", "LargeFit", m.LargeFit);
            ini.SetBool("map", "LargeFollow", m.LargeFollow);
            ini.SetBool("map", "Configured", m.Configured);

            Game.HudSettings hud = Hud ?? new Game.HudSettings();
            hud.Save(ini);
            ini.Save();
        }
    }
}


