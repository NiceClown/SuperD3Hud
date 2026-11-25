using System;
using System.Globalization;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Settings of the built-in HUD overlay: which parts are shown and how the
    /// skill cooldowns are calibrated. Stored in config\settings.ini under the
    /// [overlay] section.
    /// </summary>
    public class HudSettings
    {
        public bool Enabled = true;

        public bool ShowBuffs = true;
        public bool ShowSkills = true;
        /// <summary>
        /// Off by default: the globe of the game already shows the health, and
        /// the text would lie over the skill bar.
        /// </summary>
        public bool ShowHealth = false;
        public bool ShowInventory = true;
        public bool ShowMonsterMarkers = true;
        public bool ShowLootMarkers = true;
        /// <summary>The damage meter of the group (in a rift).</summary>
        public bool ShowDamageMeter = true;

        /// <summary>Overall opacity of the HUD, 0..255.</summary>
        public int Opacity = 220;
        /// <summary>Text scale, 0.6..2.0.</summary>
        public double Scale = 1.0;
        /// <summary>Corner where the buff list is anchored.</summary>
        public int BuffCorner = 1;      // 0 left-top, 1 right-top, 2 left-bottom, 3 right-bottom
        public int BuffOffsetX = 16;
        public int BuffOffsetY = 120;

        // Cooldown length of the six skill slots, seconds. Zero means unknown.
        public double[] Cooldowns = new double[] { 0.0, 0.0, 8.0, 8.0, 8.0, 15.0 };

        /// <summary>
        /// Attribute descriptor pair used to read real cooldowns from the game.
        /// Zero means "not discovered yet".
        /// </summary>
        public int CooldownDescriptor;
        public int CooldownStartDescriptor;

        /// <summary>Virtual key that opens the large map; Tab by default.</summary>
        public int LargeMapKey = 0x09;
        /// <summary>Virtual key that opens the waypoint/quest map; M by default.</summary>
        public int WaypointKey = 0x4D;

        public HudSettings Clone()
        {
            HudSettings copy = new HudSettings();
            copy.Enabled = Enabled;
            copy.ShowBuffs = ShowBuffs;
            copy.ShowSkills = ShowSkills;
            copy.ShowHealth = ShowHealth;
            copy.ShowInventory = ShowInventory;
            copy.ShowMonsterMarkers = ShowMonsterMarkers;
            copy.ShowLootMarkers = ShowLootMarkers;
            copy.ShowDamageMeter = ShowDamageMeter;
            copy.Opacity = Opacity;
            copy.Scale = Scale;
            copy.BuffCorner = BuffCorner;
            copy.BuffOffsetX = BuffOffsetX;
            copy.BuffOffsetY = BuffOffsetY;
            copy.Cooldowns = (double[])Cooldowns.Clone();
            copy.CooldownDescriptor = CooldownDescriptor;
            copy.CooldownStartDescriptor = CooldownStartDescriptor;
            copy.LargeMapKey = LargeMapKey;
            copy.WaypointKey = WaypointKey;
            return copy;
        }

        public void Load(IniFile ini)
        {
            Enabled = ini.GetBool("overlay", "Enabled", Enabled);
            ShowBuffs = ini.GetBool("overlay", "ShowBuffs", ShowBuffs);
            ShowSkills = ini.GetBool("overlay", "ShowSkills", ShowSkills);
            ShowHealth = ini.GetBool("overlay", "ShowHealth", ShowHealth);
            ShowInventory = ini.GetBool("overlay", "ShowInventory", ShowInventory);
            ShowMonsterMarkers = ini.GetBool("overlay", "ShowMonsterMarkers", ShowMonsterMarkers);
            ShowLootMarkers = ini.GetBool("overlay", "ShowLootMarkers", ShowLootMarkers);
            ShowDamageMeter = ini.GetBool("overlay", "ShowDamageMeter", ShowDamageMeter);
            Opacity = ini.GetInt("overlay", "Opacity", Opacity);
            Scale = ReadDouble(ini, "overlay", "Scale", Scale);
            BuffCorner = ini.GetInt("overlay", "BuffCorner", BuffCorner);
            BuffOffsetX = ini.GetInt("overlay", "BuffOffsetX", BuffOffsetX);
            BuffOffsetY = ini.GetInt("overlay", "BuffOffsetY", BuffOffsetY);

            for (int i = 0; i < Cooldowns.Length; i++)
                Cooldowns[i] = ReadDouble(ini, "overlay", "Cooldown" + i, Cooldowns[i]);

            CooldownDescriptor = ini.GetInt("overlay", "CooldownDescriptor", CooldownDescriptor);
            CooldownStartDescriptor = ini.GetInt("overlay", "CooldownStartDescriptor", CooldownStartDescriptor);
            LargeMapKey = ini.GetInt("overlay", "LargeMapKey", LargeMapKey);
            WaypointKey = ini.GetInt("overlay", "WaypointKey", WaypointKey);

            if (LargeMapKey <= 0) LargeMapKey = 0x09;
            if (WaypointKey <= 0) WaypointKey = 0x4D;

            if (Opacity < 0) Opacity = 0;
            if (Opacity > 255) Opacity = 255;
            if (Scale < 0.6) Scale = 0.6;
            if (Scale > 2.0) Scale = 2.0;
            if (BuffCorner < 0 || BuffCorner > 3) BuffCorner = 1;
        }

        public void Save(IniFile ini)
        {
            ini.SetBool("overlay", "Enabled", Enabled);
            ini.SetBool("overlay", "ShowBuffs", ShowBuffs);
            ini.SetBool("overlay", "ShowSkills", ShowSkills);
            ini.SetBool("overlay", "ShowHealth", ShowHealth);
            ini.SetBool("overlay", "ShowInventory", ShowInventory);
            ini.SetBool("overlay", "ShowMonsterMarkers", ShowMonsterMarkers);
            ini.SetBool("overlay", "ShowLootMarkers", ShowLootMarkers);
            ini.SetBool("overlay", "ShowDamageMeter", ShowDamageMeter);
            ini.Set("overlay", "Opacity", Opacity.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "Scale", Scale.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "BuffCorner", BuffCorner.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "BuffOffsetX", BuffOffsetX.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "BuffOffsetY", BuffOffsetY.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < Cooldowns.Length; i++)
                ini.Set("overlay", "Cooldown" + i,
                        Cooldowns[i].ToString(CultureInfo.InvariantCulture));

            ini.Set("overlay", "CooldownDescriptor", CooldownDescriptor.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "CooldownStartDescriptor", CooldownStartDescriptor.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "LargeMapKey", LargeMapKey.ToString(CultureInfo.InvariantCulture));
            ini.Set("overlay", "WaypointKey", WaypointKey.ToString(CultureInfo.InvariantCulture));
        }

        private static double ReadDouble(IniFile ini, string section, string key, double fallback)
        {
            string raw = ini.Get(section, key, "");
            double value;
            if (!string.IsNullOrEmpty(raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
            return fallback;
        }
    }
}
