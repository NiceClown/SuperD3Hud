using System;
using System.Collections.Generic;
using System.Drawing;

namespace SuperD3Hud.Plugins
{
    /// <summary>
    /// Read-only view of the game state that a plugin may use. It is filled by
    /// the readers of SuperD3Hud, so a plugin never touches the game itself.
    /// </summary>
    public interface IGameState
    {
        /// <summary>True when the Diablo III client is running.</summary>
        bool GameRunning { get; }

        /// <summary>True when the map reader is active and the build is supported.</summary>
        bool MapAvailable { get; }

        /// <summary>Number of loaded scenes in the current world.</summary>
        int SceneCount { get; }

        /// <summary>Number of walkable cells of the loaded scenes.</summary>
        int CellCount { get; }

        /// <summary>True when the position of the local player is known.</summary>
        bool PlayerKnown { get; }

        /// <summary>World position of the local player.</summary>
        double PlayerX { get; }

        /// <summary>World position of the local player.</summary>
        double PlayerY { get; }

        /// <summary>Identifier of the current world, 0 when unknown.</summary>
        int WorldId { get; }

        /// <summary>Name of the current area, empty when unknown.</summary>
        string AreaName { get; }

        /// <summary>Elite monsters and bosses around the player.</summary>
        IList<Game.MonsterMarker> Monsters { get; }

        /// <summary>Legendary and set items lying on the ground.</summary>
        IList<Game.LootMarker> Loot { get; }

        /// <summary>Active buffs of the local player, most useful first.</summary>
        IList<Game.BuffEntry> Buffs { get; }

        /// <summary>Cooldown state of the six skill slots.</summary>
        IList<Game.SkillCooldown> Skills { get; }

        /// <summary>Current health of the local player, negative when unknown.</summary>
        double Health { get; }

        /// <summary>Used backpack slots.</summary>
        int InventoryUsed { get; }

        /// <summary>Total backpack slots.</summary>
        int InventoryTotal { get; }

        /// <summary>True when the given buff power is active.</summary>
        bool HasBuff(int powerSno);
    }

    /// <summary>
    /// A tiny drawing surface a plugin can paint on. Coordinates are client
    /// pixels of the game window, origin at the top left corner.
    /// </summary>
    public interface IPluginCanvas
    {
        int Width { get; }
        int Height { get; }

        void DrawText(string text, float x, float y, Color color, float size, bool bold);
        void DrawTextRight(string text, float right, float y, Color color, float size, bool bold);
        void DrawRect(float x, float y, float width, float height, Color color, float thickness);
        void FillRect(float x, float y, float width, float height, Color color);
        void DrawLine(float x1, float y1, float x2, float y2, Color color, float thickness);
        void FillCircle(float x, float y, float radius, Color color);
        SizeF MeasureText(string text, float size, bool bold);
    }

    /// <summary>
    /// Everything a plugin can use: logging, its own settings and the game
    /// state. One context is created per plugin.
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>Class name of the plugin.</summary>
        string Name { get; }

        /// <summary>Writes a line into the launcher journal.</summary>
        void Log(string message);

        /// <summary>Reads a value from the plugin's own settings file.</summary>
        string GetSetting(string key, string fallback);

        /// <summary>Stores a value in the plugin's own settings file.</summary>
        void SetSetting(string key, string value);

        /// <summary>Current game state, read-only.</summary>
        IGameState Game { get; }
    }

    /// <summary>
    /// Base class of every SuperD3Hud plugin. A plugin is a normal C# class in
    /// its own file; the launcher compiles it at start-up and calls the hooks
    /// below. It has no dependency on any third-party HUD.
    /// </summary>
    public abstract class HudPlugin
    {
        /// <summary>Context injected by the launcher before <see cref="Load"/>.</summary>
        public IPluginContext Context { get; internal set; }

        /// <summary>Human readable name, defaults to the class name.</summary>
        public virtual string DisplayName { get { return GetType().Name; } }

        /// <summary>One line that explains what the plugin does.</summary>
        public virtual string Description { get { return ""; } }

        /// <summary>Author of the plugin, optional.</summary>
        public virtual string Author { get { return ""; } }

        /// <summary>Version of the plugin, optional.</summary>
        public virtual string Version { get { return "1.0"; } }

        /// <summary>Whether the plugin is enabled when it is first seen.</summary>
        public virtual bool EnabledByDefault { get { return true; } }

        /// <summary>Called once after the plugin is created.</summary>
        public virtual void Load() { }

        /// <summary>Called about ten times per second while the game runs.</summary>
        public virtual void Update(double seconds) { }

        /// <summary>
        /// Called after the map was drawn. Paint into the given canvas; the
        /// launcher composites the result over the game window.
        /// </summary>
        public virtual void Draw(IPluginCanvas canvas) { }

        /// <summary>Called when the plugin is disabled or the launcher closes.</summary>
        public virtual void Unload() { }

        /// <summary>Convenience helper for subclasses.</summary>
        protected void Log(string message)
        {
            if (Context != null) Context.Log(message);
        }
    }
}
