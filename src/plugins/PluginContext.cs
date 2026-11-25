using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SuperD3Hud.Plugins
{
    /// <summary>Mutable game state shared by all plugins. Written by the launcher.</summary>
    public class GameState : IGameState
    {
        public bool GameRunning { get; private set; }
        public bool MapAvailable { get; private set; }
        public int SceneCount { get; private set; }
        public int CellCount { get; private set; }
        public bool PlayerKnown { get; private set; }
        public double PlayerX { get; private set; }
        public double PlayerY { get; private set; }
        public int WorldId { get; private set; }
        public string AreaName { get; private set; }

        private readonly List<SuperD3Hud.Game.MonsterMarker> _monsters =
            new List<SuperD3Hud.Game.MonsterMarker>();
        private readonly List<SuperD3Hud.Game.LootMarker> _loot =
            new List<SuperD3Hud.Game.LootMarker>();
        private readonly List<SuperD3Hud.Game.BuffEntry> _buffs =
            new List<SuperD3Hud.Game.BuffEntry>();
        private readonly List<SuperD3Hud.Game.SkillCooldown> _skills =
            new List<SuperD3Hud.Game.SkillCooldown>();

        public IList<SuperD3Hud.Game.MonsterMarker> Monsters { get { return _monsters; } }
        public IList<SuperD3Hud.Game.LootMarker> Loot { get { return _loot; } }
        public IList<SuperD3Hud.Game.BuffEntry> Buffs { get { return _buffs; } }
        public IList<SuperD3Hud.Game.SkillCooldown> Skills { get { return _skills; } }
        public double Health { get; private set; }
        public int InventoryUsed { get; private set; }
        public int InventoryTotal { get; private set; }

        public GameState()
        {
            AreaName = "";
            Health = -1;
        }

        internal void Set(bool gameRunning, bool mapAvailable, int sceneCount, int cellCount,
                          bool playerKnown, double playerX, double playerY, int worldId, string areaName)
        {
            GameRunning = gameRunning;
            MapAvailable = mapAvailable;
            SceneCount = sceneCount;
            CellCount = cellCount;
            PlayerKnown = playerKnown;
            PlayerX = playerX;
            PlayerY = playerY;
            WorldId = worldId;
            AreaName = areaName ?? "";
        }

        /// <summary>
        /// Replaces the HUD part of the state: monsters, loot, buffs, skills and
        /// the backpack. Lists are copied, so the caller keeps ownership.
        /// </summary>
        internal void SetHud(IList<SuperD3Hud.Game.MonsterMarker> monsters,
                             IList<SuperD3Hud.Game.LootMarker> loot,
                             IList<SuperD3Hud.Game.BuffEntry> buffs,
                             IList<SuperD3Hud.Game.SkillCooldown> skills,
                             double health, int inventoryUsed, int inventoryTotal)
        {
            _monsters.Clear();
            if (monsters != null) _monsters.AddRange(monsters);

            _loot.Clear();
            if (loot != null) _loot.AddRange(loot);

            _buffs.Clear();
            if (buffs != null) _buffs.AddRange(buffs);

            _skills.Clear();
            if (skills != null) _skills.AddRange(skills);

            Health = health;
            InventoryUsed = inventoryUsed;
            InventoryTotal = inventoryTotal;
        }

        public bool HasBuff(int powerSno)
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i].PowerSno == powerSno && _buffs[i].Active) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Per-plugin context: journal, own settings and the game state. Settings
    /// live in config\plugin_settings so a plugin never writes outside the
    /// launcher folder.
    /// </summary>
    public class PluginContext : IPluginContext
    {
        private readonly string _name;
        private readonly Action<string> _log;
        private readonly IGameState _game;
        private readonly string _settingsPath;
        private readonly Dictionary<string, string> _values;
        private readonly object _sync = new object();

        public PluginContext(string name, Action<string> log, IGameState game, string settingsDir)
        {
            _name = name;
            _log = log;
            _game = game;

            string safe = name ?? "plugin";
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            _settingsPath = Path.Combine(settingsDir, "local_" + safe + ".json");
            _values = Load();
        }

        public string Name { get { return _name; } }

        public IGameState Game { get { return _game; } }

        public void Log(string message)
        {
            if (_log == null || string.IsNullOrEmpty(message)) return;
            try { _log(_name + ": " + message); }
            catch { }
        }

        public string GetSetting(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            lock (_sync)
            {
                string value;
                if (_values.TryGetValue(key, out value)) return value;
                return fallback;
            }
        }

        public void SetSetting(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_sync)
            {
                _values[key] = value ?? "";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    File.WriteAllText(_settingsPath, ser.Serialize(_values), new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private Dictionary<string, string> Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return new Dictionary<string, string>();
                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, string> values = ser.Deserialize<Dictionary<string, string>>(json);
                return values ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }
    }

    /// <summary>Drawing surface handed to plugins each frame.</summary>
    public class PluginCanvas : IPluginCanvas, IDisposable
    {
        private readonly Graphics _g;
        private readonly int _width;
        private readonly int _height;
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        public PluginCanvas(Graphics graphics, int width, int height)
        {
            _g = graphics;
            _width = width;
            _height = height;
        }

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        public void DrawText(string text, float x, float y, Color color, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (SolidBrush brush = new SolidBrush(color))
                _g.DrawString(text, GetFont(size, bold), brush, x, y);
        }

        public void DrawTextRight(string text, float right, float y, Color color, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            SizeF size2 = MeasureText(text, size, bold);
            DrawText(text, right - size2.Width, y, color, size, bold);
        }

        public void DrawRect(float x, float y, float width, float height, Color color, float thickness)
        {
            if (width <= 0 || height <= 0) return;
            using (Pen pen = new Pen(color, Math.Max(0.5f, thickness)))
                _g.DrawRectangle(pen, x, y, width, height);
        }

        public void FillRect(float x, float y, float width, float height, Color color)
        {
            if (width <= 0 || height <= 0) return;
            using (SolidBrush brush = new SolidBrush(color))
                _g.FillRectangle(brush, x, y, width, height);
        }

        public void DrawLine(float x1, float y1, float x2, float y2, Color color, float thickness)
        {
            using (Pen pen = new Pen(color, Math.Max(0.5f, thickness)))
                _g.DrawLine(pen, x1, y1, x2, y2);
        }

        public void FillCircle(float x, float y, float radius, Color color)
        {
            if (radius <= 0) return;
            using (SolidBrush brush = new SolidBrush(color))
                _g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
        }

        public SizeF MeasureText(string text, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return _g.MeasureString(text, GetFont(size, bold));
        }

        private Font GetFont(float size, bool bold)
        {
            string key = size.ToString(CultureInfo.InvariantCulture) + (bold ? "b" : "n");
            Font font;
            if (_fonts.TryGetValue(key, out font)) return font;
            font = new Font("Segoe UI", Math.Max(5f, size), bold ? FontStyle.Bold : FontStyle.Regular);
            _fonts[key] = font;
            return font;
        }

        /// <summary>Releases the fonts created for this frame.</summary>
        public void Dispose()
        {
            foreach (Font font in _fonts.Values)
            {
                try { font.Dispose(); }
                catch { }
            }
            _fonts.Clear();
        }
    }
}
