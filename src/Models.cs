using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SuperD3Hud
{
    /// <summary>JSON persistence with a safe fallback for corrupted files.</summary>
    public static class JsonStore
    {
        public static T Load<T>(string path, T fallback) where T : class
        {
            try
            {
                if (!File.Exists(path)) return fallback;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return fallback;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                ser.RecursionLimit = 100;
                T value = ser.Deserialize<T>(json);
                return value ?? fallback;
            }
            catch
            {
                try
                {
                    if (File.Exists(path)) File.Copy(path, path + ".bad", true);
                }
                catch { }
                return fallback;
            }
        }

        public static void Save<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            ser.RecursionLimit = 100;
            string json = ser.Serialize(value);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public static string Serialize(object value)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Serialize(value);
        }

        public static T Deserialize<T>(string json)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Deserialize<T>(json);
        }
    }

    public class HotkeyBinding
    {
        public string Action { get; set; }
        public string Key { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public bool Enabled { get; set; }

        public HotkeyBinding()
        {
            Action = "";
            Key = "F1";
            Enabled = true;
        }

        public HotkeyBinding Clone()
        {
            return new HotkeyBinding
            {
                Action = Action,
                Key = Key,
                Ctrl = Ctrl,
                Shift = Shift,
                Alt = Alt,
                Enabled = Enabled
            };
        }

        public string Describe()
        {
            List<string> parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(Key);
            return string.Join("+", parts.ToArray());
        }
    }

    public class MacroSettings
    {
        /// <summary>
        /// Default pause between two input actions. A very short delay keeps a
        /// long click grid fast; the engine adds a random spread of 1-5 ms so
        /// the timing is never perfectly periodic.
        /// </summary>
        public const int DefaultStepDelayMs = 3;

        private int _lockedLeftColumns;
        private int _lockedRightColumns;
        private int _gemUpgradeCount;
        private int _gambleStopFreeSlots;

        public bool Enabled { get; set; }
        public bool OnlyWhenGameForeground { get; set; }
        public int StepDelayMs { get; set; }
        public List<HotkeyBinding> Hotkeys { get; set; }
        public Dictionary<string, string> Scripts { get; set; }

        public bool SmartMode { get; set; }
        public bool SalvageBulkButtons { get; set; }
        public bool SalvageLegendaries { get; set; }
        public bool ProtectAncient { get; set; }
        public bool ProtectPrimal { get; set; }

        public int LockedLeftColumns
        {
            get { return _lockedLeftColumns; }
            set { _lockedLeftColumns = Clamp(value, 0, 10); }
        }

        public int LockedRightColumns
        {
            get { return _lockedRightColumns; }
            set { _lockedRightColumns = Clamp(value, 0, 10); }
        }

        public bool EmpoweredGR { get; set; }

        public int GemUpgradeCount
        {
            get { return _gemUpgradeCount; }
            set { _gemUpgradeCount = Clamp(value, 1, 5); }
        }

        public int GambleStopFreeSlots
        {
            get { return _gambleStopFreeSlots; }
            set { _gambleStopFreeSlots = Clamp(value, 0, 20); }
        }

        public bool AbortOnMouseMove { get; set; }

        public MacroSettings()
        {
            Enabled = true;
            OnlyWhenGameForeground = true;
            StepDelayMs = DefaultStepDelayMs;
            Hotkeys = new List<HotkeyBinding>();
            Scripts = new Dictionary<string, string>();

            SmartMode = true;
            SalvageBulkButtons = true;
            SalvageLegendaries = true;
            ProtectAncient = true;
            ProtectPrimal = true;
            _lockedLeftColumns = 0;
            _lockedRightColumns = 2;
            EmpoweredGR = false;
            _gemUpgradeCount = 4;
            _gambleStopFreeSlots = 2;
            AbortOnMouseMove = true;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Converts left/right locked column counts into TurboHUD's protected
        /// backpack rectangle (e.g. LockedRightColumns=2 -> Rectangle(8, 0, 2, 6)).
        /// </summary>
        public static Rectangle LockAreaForColumns(int lockedLeft, int lockedRight)
        {
            int left = Clamp(lockedLeft, 0, 10);
            int right = Clamp(lockedRight, 0, 10);
            if (left == 0 && right == 0) return Rectangle.Empty;
            if (left + right >= 10) return new Rectangle(0, 0, 10, 6);
            if (right > 0 && (left == 0 || right >= left))
                return new Rectangle(10 - right, 0, right, 6);
            return new Rectangle(0, 0, left, 6);
        }

        public static MacroSettings CreateDefault()
        {
            MacroSettings s = new MacroSettings();
            s.Hotkeys.Add(Make("Salvage", "F1"));
            s.Hotkeys.Add(Make("InvDrop", "F2"));
            s.Hotkeys.Add(Make("Gamble", "F3"));
            s.Hotkeys.Add(Make("LeftClick", "F4"));
            s.Hotkeys.Add(Make("OpenGR", "F5"));
            s.Hotkeys.Add(Make("OpenRift", "F5", false, true, false));
            s.Hotkeys.Add(Make("AcceptGR", "F5", true, false, false));
            s.Hotkeys.Add(Make("GemUps", "F6"));
            s.Hotkeys.Add(Make("CubeUpgrade", "F1", false, true, false));
            s.Hotkeys.Add(Make("Pause", "F7"));
            s.Hotkeys.Add(Make("LeaveGame", "F8"));
            foreach (string action in MacroActions.All)
            {
                if (action == "Pause") continue;
                s.Scripts[action] = MacroActions.DefaultScript(action);
            }
            return s;
        }

        private static HotkeyBinding Make(string action, string key)
        {
            return Make(action, key, false, false, false);
        }

        private static HotkeyBinding Make(string action, string key, bool ctrl, bool shift, bool alt)
        {
            HotkeyBinding b = new HotkeyBinding();
            b.Action = action;
            b.Key = key;
            b.Ctrl = ctrl;
            b.Shift = shift;
            b.Alt = alt;
            return b;
        }

        public static MacroSettings Load()
        {
            bool existed = File.Exists(AppPaths.MacrosFile);
            MacroSettings s = JsonStore.Load<MacroSettings>(AppPaths.MacrosFile, null);
            bool created = s == null;
            if (s == null) s = CreateDefault();
            if (s.Hotkeys == null) s.Hotkeys = new List<HotkeyBinding>();
            if (s.Scripts == null) s.Scripts = new Dictionary<string, string>();
            bool migrated = false;

            HashSet<string> missingBeforeLoad = new HashSet<string>(StringComparer.Ordinal);
            foreach (string action in MacroActions.All)
            {
                if (action == "Pause") continue;
                if (!s.Scripts.ContainsKey(action) || string.IsNullOrEmpty(s.Scripts[action]))
                {
                    missingBeforeLoad.Add(action);
                    s.Scripts[action] = MacroActions.DefaultScript(action);
                    migrated = true;
                    continue;
                }
                // Scripts written for the old 1920x1080 reference are moved
                // to the current one, so user edits are not lost.
                if (MacroActions.IsLegacyScript(action, s.Scripts[action]))
                {
                    s.Scripts[action] = MacroActions.RescaleScript(s.Scripts[action]);
                    migrated = true;
                }
                else if (MacroActions.IsPreviousDefaultScript(action, s.Scripts[action]))
                {
                    s.Scripts[action] = MacroActions.DefaultScript(action);
                    migrated = true;
                }
            }

            if (missingBeforeLoad.Contains("OpenRift"))
                migrated |= EnsureDefaultHotkey(s, "OpenRift", "F5", false, true, false);
            if (missingBeforeLoad.Contains("AcceptGR"))
                migrated |= EnsureDefaultHotkey(s, "AcceptGR", "F5", true, false, false);
            if (missingBeforeLoad.Contains("CubeUpgrade"))
                migrated |= EnsureDefaultHotkey(s, "CubeUpgrade", "F1", false, true, false);

            // The old default was a fixed 40 ms. It is migrated to the fast
            // humanised value, otherwise existing installs would keep the slow
            // timing the user explicitly asked to remove.
            if (s.StepDelayMs == 40 || s.StepDelayMs <= 0)
            {
                s.StepDelayMs = DefaultStepDelayMs;
                migrated = true;
            }

            // Materialise defaults on first run so the file is easy to inspect/edit.
            if (created || !existed || migrated)
            {
                try { s.Save(); }
                catch { }
            }
            return s;
        }

        private static bool EnsureDefaultHotkey(MacroSettings s, string action, string key, bool ctrl, bool shift, bool alt)
        {
            for (int i = 0; i < s.Hotkeys.Count; i++)
            {
                HotkeyBinding existing = s.Hotkeys[i];
                if (existing != null && existing.Action == action) return false;
            }
            for (int i = 0; i < s.Hotkeys.Count; i++)
            {
                HotkeyBinding existing = s.Hotkeys[i];
                if (existing == null) continue;
                if (string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase)
                    && existing.Ctrl == ctrl && existing.Shift == shift && existing.Alt == alt)
                    return false;
            }
            s.Hotkeys.Add(Make(action, key, ctrl, shift, alt));
            return true;
        }

        public void Save()
        {
            JsonStore.Save(AppPaths.MacrosFile, this);
        }
    }

    public static class MacroActions
    {
        /// <summary>Reference client area the default scripts are written for.</summary>
        public const int ReferenceWidth = 2560;
        public const int ReferenceHeight = 1440;

        public static readonly string[] All =
        {
            "Salvage", "InvDrop", "Gamble", "LeftClick",
            "OpenGR", "OpenRift", "AcceptGR", "GemUps", "CubeUpgrade",
            "LeaveGame", "Pause"
        };

        public static string DisplayName(string action)
        {
            switch (action)
            {
                case "Salvage": return "Разобрать предметы (Salvage)";
                case "InvDrop": return "Выбросить инвентарь (InvDrop)";
                case "Gamble": return "Азарт / гемблинг (Gamble)";
                case "LeftClick": return "Спам левой кнопкой (LClick)";
                case "OpenGR": return "Открыть Greater Rift";
                case "OpenRift": return "Открыть Нефалемский портал (Rift)";
                case "AcceptGR": return "Принять портал (Accept GR)";
                case "GemUps": return "Прокачать камни (Gem Ups)";
                case "CubeUpgrade": return "Куб Канаи: улучшение редких";
                case "LeaveGame": return "Покинуть игру (Leave Game)";
                case "Pause": return "Пауза макросов";
                default: return action;
            }
        }

        public static string ShortName(string action)
        {
            switch (action)
            {
                case "Salvage": return "Salvage";
                case "InvDrop": return "InvDrop";
                case "Gamble": return "Gamble";
                case "LeftClick": return "LeftClick";
                case "OpenGR": return "OpenGR";
                case "OpenRift": return "OpenRift";
                case "AcceptGR": return "AcceptGR";
                case "GemUps": return "GemUps";
                case "CubeUpgrade": return "CubeUpgrade";
                case "LeaveGame": return "LeaveGame";
                case "Pause": return "Pause";
                default: return action;
            }
        }

        /// <summary>
        /// Distinctive fragments of the scripts that were shipped before the
        /// coordinate reference was changed to 2560x1440. When a stored
        /// script still contains one of them, its coordinates are rescaled
        /// instead of being thrown away, so user edits survive the upgrade.
        /// </summary>
        public static string[] LegacySignatures(string action)
        {
            switch (action)
            {
                case "Salvage": return new string[] { "MOVE 512 482", "GRID 1477 585 50 50 10 6 13" };
                case "InvDrop": return new string[] { "GRIDDROP 1477 585 50 50 10 6 700 394" };
                case "Gamble": return new string[] { "MOVE 168 210", "MOVE 508 490" };
                case "OpenGR": return new string[] { "MOVE 264 470", "MOVE 263 841" };
                case "GemUps": return new string[] { "MOVE 274 547", "MOVE 1900 84" };
                case "LeaveGame": return new string[] { "MOVE 241 485" };
                default: return new string[0];
            }
        }

        /// <summary>
        /// True when the stored script still uses the old 1920x1080 reference.
        /// </summary>
        public static bool IsLegacyScript(string action, string script)
        {
            if (string.IsNullOrEmpty(script)) return false;
            foreach (string signature in LegacySignatures(action))
            {
                if (script.Contains(signature)) return true;
            }
            return false;
        }

        /// <summary>
        /// Rescales the coordinates of a script that was written for the old
        /// 1920x1080 reference to the current 2560x1440 reference.
        /// </summary>
        public static string RescaleScript(string script)
        {
            if (string.IsNullOrEmpty(script)) return script;

            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int comment = line.IndexOf('#');
                string tail = "";
                if (comment >= 0)
                {
                    tail = line.Substring(comment);
                    line = line.Substring(0, comment);
                }
                line = line.Trim();
                if (line.Length == 0) { lines[i] = tail; continue; }

                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToUpperInvariant();

                if ((cmd == "MOVE" || cmd == "MOVEL" || cmd == "MOVER" || cmd == "MOVEC") && parts.Length == 3)
                {
                    lines[i] = cmd + " " + Scale(parts[1]) + " " + Scale(parts[2]) + tail;
                }
                else if (cmd == "GRID" && parts.Length >= 7)
                {
                    lines[i] = "GRID " + Scale(parts[1]) + " " + Scale(parts[2]) + " " +
                               Scale(parts[3]) + " " + Scale(parts[4]) + " " +
                               parts[5] + " " + parts[6] +
                               (parts.Length > 7 ? " " + parts[7] : "") + tail;
                }
                else if (cmd == "GRIDDROP" && parts.Length >= 9)
                {
                    lines[i] = "GRIDDROP " + Scale(parts[1]) + " " + Scale(parts[2]) + " " +
                               Scale(parts[3]) + " " + Scale(parts[4]) + " " +
                               parts[5] + " " + parts[6] + " " +
                               Scale(parts[7]) + " " + Scale(parts[8]) + tail;
                }
                else
                {
                    lines[i] = line + tail;
                }
            }
            string result = string.Join("\n", lines);
            result = result.Replace("1920x1080", MacroEngine.ReferenceWidth + "x" + MacroEngine.ReferenceHeight);
            result = result.Replace("1920×1080", MacroEngine.ReferenceWidth + "x" + MacroEngine.ReferenceHeight);
            return result;
        }

        private static string Scale(string value)
        {
            double number;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out number))
                return value;
            double scaled = Math.Round(number * ReferenceWidth / 1920.0);
            return scaled.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when a stored script matches an unmodified previous v1.8.0
        /// default script, so upgrades can adopt smart commands without touching
        /// user-edited scripts.
        /// </summary>
        public static bool IsPreviousDefaultScript(string action, string script)
        {
            if (string.IsNullOrEmpty(script)) return false;
            string previous = PreviousDefaultScript(action);
            if (string.IsNullOrEmpty(previous)) return false;
            return NormalizeLineEndings(script) == NormalizeLineEndings(previous);
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static string PreviousDefaultScript(string action)
        {
            switch (action)
            {
                case "Salvage":
                    return
"# Разобрать предметы. Координаты рассчитаны на 2560x1440 и масштабируются под окно игры.\n" +
"# GRID x y stepX stepY columns rows confirmKey\n" +
"FOCUS\n" +
"MOVE 683 643\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 220 393\n" +
"CLICK\n" +
"SLEEP 150\n" +
"GRID 1969 780 67 67 10 6 13\n" +
"SLEEP 200\n" +
"MOVE 672 827\n" +
"CLICK\n";

                case "InvDrop":
                    return
"# Открыть инвентарь, перетащить каждый предмет в точку сброса.\n" +
"# Координаты рассчитаны на 2560x1440.\n" +
"FOCUS\n" +
"KEY 73\n" +
"SLEEP 400\n" +
"GRIDDROP 1969 780 67 67 10 6 933 525\n" +
"SLEEP 200\n" +
"KEY 73\n";

                case "Gamble":
                    return
"# Покупка у Кадалы. Координаты рассчитаны на 2560x1440.\n" +
"FOCUS\n" +
"MOVE 224 280\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 677 301\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 372 420\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 677 653\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 224 420\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 677 469\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 372 280\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 677 556\n" +
"CLICK\n" +
"SLEEP 120\n" +
"MOVE 224 847\n" +
"CLICK\n";

                case "OpenGR":
                    return
"# Быстрое открытие Greater Rift у обелиска. Координаты для 2560x1440.\n" +
"FOCUS\n" +
"MOVE 352 627\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 351 1121\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 1280 720\n" +
"CLICK\n";

                case "GemUps":
                    return
"# Прокачка легендарных камней у Урши. Координаты для 2560x1440.\n" +
"FOCUS\n" +
"MOVE 365 729\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 140 853\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 684 685\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 167 684\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 684 685\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 2533 112\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 684 685\n" +
"CLICK\n" +
"SLEEP 150\n" +
"MOVE 2533 684\n" +
"CLICK\n";

                case "LeaveGame":
                    return
"# Esc -> кнопка выхода из игры. Координаты рассчитаны на 2560x1440.\n" +
"FOCUS\n" +
"KEY 27\n" +
"SLEEP 400\n" +
"MOVE 321 647\n" +
"CLICK\n";

                default:
                    return "";
            }
        }

        public static string DefaultScript(string action)
        {
            switch (action)
            {
                case "Salvage":
                    return
"# Умный разбор у кузнеца (кнопки белого/синего/жёлтого + наковальня для легендарок).\n" +
"# Учитывает заблокированные колонки и защиту Древних/Первозданных предметов.\n" +
"FOCUS\n" +
"SMARTSALVAGE\n";

                case "InvDrop":
                    return
"# Открыть инвентарь и выбросить разблокированные/незащищённые предметы.\n" +
"FOCUS\n" +
"KEY 73\n" +
"SLEEP 350\n" +
"SMARTDROP\n" +
"SLEEP 150\n" +
"KEY 73\n";

                case "Gamble":
                    return
"# Покупка у Кадалы под курсором с остановкой при заполнении рюкзака.\n" +
"FOCUS\n" +
"SMARTGAMBLE 60\n";

                case "LeftClick":
                    return
"# Одиночный клик левой кнопкой в текущей позиции курсора.\n" +
"CLICK\n";

                case "OpenGR":
                    return
"# Быстрое открытие Greater Rift у обелиска (с учётом настройки Empowered GR).\n" +
"FOCUS\n" +
"OPENGR\n";

                case "OpenRift":
                    return
"# Быстрое открытие Нефалемского портала у обелиска.\n" +
"FOCUS\n" +
"OPENRIFT\n";

                case "AcceptGR":
                    return
"# Принять приглашение в Greater Rift по центру экрана.\n" +
"FOCUS\n" +
"ACCEPTGR\n";

                case "GemUps":
                    return
"# Прокачка легендарных камней у Урши (число улучшений из настроек).\n" +
"FOCUS\n" +
"GEMUPS\n";

                case "CubeUpgrade":
                    return
"# Куб Канаи (стр. 3): улучшение редких предметов в легендарные.\n" +
"FOCUS\n" +
"CUBEUPGRADE 20\n";

                case "LeaveGame":
                    return
"# Esc -> кнопка выхода из игры. Координаты рассчитаны на 2560x1440.\n" +
"FOCUS\n" +
"KEY 27\n" +
"SLEEP 400\n" +
"MOVEL 321 647\n" +
"CLICK\n";

                default:
                    return "";
            }
        }
    }
}


