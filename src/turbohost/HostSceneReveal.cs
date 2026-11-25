using System.Drawing;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Scene reveal settings. The host's own map overlay draws the world; these
    /// flags let a plugin express its preference without touching the game.
    /// </summary>
    public class HostSceneRevealController : ISceneRevealController
    {
        private bool _minimapEnabled = true;
        private bool _mapEnabled = true;
        private float _mapOpacity = 1f;
        private float _minimapOpacity = 1f;
        private bool _minimapClip = true;
        private SolidBrush _brushKnown;
        private Brush _brushUnknown;
        private bool _displaySceneBorder;
        private Pen _sceneBorderPen;

        public bool MinimapEnabled
        {
            get { return _minimapEnabled; }
            set { _minimapEnabled = value; }
        }

        public bool MapEnabled
        {
            get { return _mapEnabled; }
            set { _mapEnabled = value; }
        }

        public float MapOpacity
        {
            get { return _mapOpacity; }
            set { _mapOpacity = ClampOpacity(value); }
        }

        public float MinimapOpacity
        {
            get { return _minimapOpacity; }
            set { _minimapOpacity = ClampOpacity(value); }
        }

        public bool MinimapClip
        {
            get { return _minimapClip; }
            set { _minimapClip = value; }
        }

        public SolidBrush BrushKnown
        {
            get { return _brushKnown; }
            set { _brushKnown = value; }
        }

        public Brush BrushUnknown
        {
            get { return _brushUnknown; }
            set { _brushUnknown = value; }
        }

        public bool DisplaySceneBorder
        {
            get { return _displaySceneBorder; }
            set { _displaySceneBorder = value; }
        }

        public Pen SceneBorderPen
        {
            get { return _sceneBorderPen; }
            set { _sceneBorderPen = value; }
        }

        private static float ClampOpacity(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    /// <summary>Avoidance state. The host does not run the game's avoidance UI.</summary>
    public class HostAvoidanceController : IAvoidanceController
    {
        public bool CurrentValue { get { return false; } }
    }
}
