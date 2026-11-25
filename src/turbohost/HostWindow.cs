using System;
using System.Drawing;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// The game window as plugins see it. The host has no camera matrix, so
    /// world-to-screen uses a player-centred projection. The numbers were
    /// measured on a 2560x1440 window against the game's own elite markers:
    /// the ground of the game is compressed less than the old 1:2 assumed, so
    /// circles under distant monsters landed below them.
    /// </summary>
    public class HostWindow : IWindow
    {
        private const float Diagonal = 0.70710678f; // 1 / sqrt(2)

        /// <summary>
        /// How much the ground of the game is compressed on the screen. The
        /// value was measured twice: against an elite standing next to the
        /// character and against the elite's own ring further away.
        /// </summary>
        private const float DepthCompression = 0.68f;

        /// <summary>
        /// The compression of the ground, so that a circle drawn on the ground
        /// gets the same shape as the projection uses.
        /// </summary>
        public const float GroundCompression = DepthCompression;

        /// <summary>
        /// How much higher than the middle of the screen the character
        /// stands: the camera of the game looks a little behind the
        /// character. Measured on a 2560x1440 window: about 38 pixels.
        /// </summary>
        private const float CameraHeightOffset = 0.0264f;

        private readonly HostContext _context;

        public HostWindow(HostContext context)
        {
            _context = context;
            WorldCoordinate.CurrentWindow = this;
        }

        private int Width { get { return _context == null ? 0 : Math.Max(0, _context.WindowWidth); } }
        private int Height { get { return _context == null ? 0 : Math.Max(0, _context.WindowHeight); } }
        private int OffsetX { get { return _context == null ? 0 : _context.WindowX; } }
        private int OffsetY { get { return _context == null ? 0 : _context.WindowY; } }

        public IWorldCoordinate Center
        {
            get
            {
                float px = _context == null ? 0f : _context.PlayerX;
                float py = _context == null ? 0f : _context.PlayerY;
                return new WorldCoordinate(px, py, 0f);
            }
        }

        public Size Size { get { return new Size(Width, Height); } }
        public Point Offset { get { return new Point(OffsetX, OffsetY); } }
        public float Aspect { get { return Height <= 0 ? 1f : (float)Width / (float)Height; } }
        public float HeightUiRatio { get { return 0.026f; } }
        public bool IsForeground { get { return _context != null && _context.WindowForeground; } }
        public Rectangle GroundRectangle { get { return new Rectangle(0, 0, Width, Height); } }

        public int CursorX
        {
            get
            {
                try { return System.Windows.Forms.Cursor.Position.X - OffsetX; }
                catch { return -1; }
            }
        }

        public int CursorY
        {
            get
            {
                try { return System.Windows.Forms.Cursor.Position.Y - OffsetY; }
                catch { return -1; }
            }
        }

        public bool CursorInsideRect(float x, float y, float w, float h)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(w) || float.IsNaN(h)) return false;
            float left = Math.Min(x, x + w);
            float right = Math.Max(x, x + w);
            float top = Math.Min(y, y + h);
            float bottom = Math.Max(y, y + h);
            int cx = CursorX;
            int cy = CursorY;
            return cx >= left && cx <= right && cy >= top && cy <= bottom;
        }

        public IScreenCoordinate CreateScreenCoordinate(float x, float y)
        {
            return new HostScreenCoordinate(this, x, y);
        }

        public IWorldCoordinate CreateWorldCoordinate(IWorldCoordinate source)
        {
            return source == null ? new WorldCoordinate(0f, 0f, 0f) : new WorldCoordinate(source);
        }

        public IWorldCoordinate CreateWorldCoordinate(float worldX, float worldY, float worldZ)
        {
            return new WorldCoordinate(worldX, worldY, worldZ);
        }

        public void WorldToScreenCoordinate(float worldX, float worldY, float worldZ,
                                            out float screenX, out float screenY)
        {
            float scale = ProjectionScale;
            float px = _context == null ? 0f : _context.PlayerX;
            float py = _context == null ? 0f : _context.PlayerY;
            if (float.IsNaN(px) || float.IsInfinity(px)) px = 0f;
            if (float.IsNaN(py) || float.IsInfinity(py)) py = 0f;

            if (float.IsNaN(worldX) || float.IsInfinity(worldX)) worldX = px;
            if (float.IsNaN(worldY) || float.IsInfinity(worldY)) worldY = py;
            if (float.IsNaN(worldZ) || float.IsInfinity(worldZ)) worldZ = 0f;

            // Isometric 45-degree rotation with the compression of the ground:
            float dx = worldX - px;
            float dy = worldY - py;
            float projX = (dy - dx) * Diagonal;
            float projY = (dx + dy) * Diagonal * DepthCompression - worldZ * DepthCompression;

            screenX = Width * 0.5f + projX * scale;
            // The camera of the game looks a little behind the character, so
            // the character stands a little above the middle of the screen.
            screenY = Height * (0.5f - CameraHeightOffset) + projY * scale;
        }

        public IScreenCoordinate WorldToScreenCoordinate(float worldX, float worldY, float worldZ,
                                                         bool raw = false, bool ultraPrecise = false)
        {
            float x, y;
            WorldToScreenCoordinate(worldX, worldY, worldZ, out x, out y);
            return new HostScreenCoordinate(this, x, y);
        }

        public void SetScreenCoordinate(IScreenCoordinate screenCoordinate, float worldX, float worldY,
                                        float worldZ, bool raw = false, bool ultraPrecise = false)
        {
            float x, y;
            WorldToScreenCoordinate(worldX, worldY, worldZ, out x, out y);
            if (screenCoordinate != null) screenCoordinate.Set(x, y);
        }

        public bool CalculateMidPointByDistance(IWorldCoordinate targetCoordinate, float targetDistance,
                                                IWorldCoordinate midPoint)
        {
            if (targetCoordinate == null || midPoint == null) return false;
            if (float.IsNaN(targetDistance) || float.IsInfinity(targetDistance)) return false;

            float px = _context == null ? 0f : _context.PlayerX;
            float py = _context == null ? 0f : _context.PlayerY;
            if (float.IsNaN(px) || float.IsInfinity(px)) px = 0f;
            if (float.IsNaN(py) || float.IsInfinity(py)) py = 0f;
            if (float.IsNaN(targetCoordinate.X) || float.IsInfinity(targetCoordinate.X)
                || float.IsNaN(targetCoordinate.Y) || float.IsInfinity(targetCoordinate.Y)
                || float.IsNaN(targetCoordinate.Z) || float.IsInfinity(targetCoordinate.Z))
                return false;
            float dx = targetCoordinate.X - px;
            float dy = targetCoordinate.Y - py;
            if (float.IsNaN(dx) || float.IsInfinity(dx) || float.IsNaN(dy) || float.IsInfinity(dy))
                return false;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance <= 0.001f) return false;

            float ratio = Math.Max(0f, targetDistance) / distance;
            if (ratio > 1f) ratio = 1f;
            midPoint.Set(px + dx * ratio, py + dy * ratio, targetCoordinate.Z);
            return true;
        }

        public IWorldCoordinate CalculateMidPointByRatio(IWorldCoordinate targetCoordinate, float ratio)
        {
            if (targetCoordinate == null) return new WorldCoordinate(0, 0, 0);
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 0f;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;
            if (float.IsNaN(targetCoordinate.X) || float.IsInfinity(targetCoordinate.X)
                || float.IsNaN(targetCoordinate.Y) || float.IsInfinity(targetCoordinate.Y)
                || float.IsNaN(targetCoordinate.Z) || float.IsInfinity(targetCoordinate.Z))
                return new WorldCoordinate(0, 0, 0);

            float px = _context == null ? 0f : _context.PlayerX;
            float py = _context == null ? 0f : _context.PlayerY;
            if (float.IsNaN(px) || float.IsInfinity(px)) px = 0f;
            if (float.IsNaN(py) || float.IsInfinity(py)) py = 0f;
            return new WorldCoordinate(px + (targetCoordinate.X - px) * ratio,
                                       py + (targetCoordinate.Y - py) * ratio,
                                       targetCoordinate.Z * ratio);
        }

        public float InGameClickMinX { get { return 0f; } }
        public float InGameClickMaxX { get { return Width; } }
        public float InGameClickMinY { get { return 0f; } }
        public float InGameClickMaxY { get { return Height; } }

        public bool CursorInsideGroundRect()
        {
            return CursorInsideGameWindow();
        }

        public bool CursorInsideGameWindow()
        {
            int cx = CursorX;
            int cy = CursorY;
            return cx >= 0 && cx < Width && cy >= 0 && cy < Height;
        }

        /// <summary>Inverse of the host's player-centred isometric projection.</summary>
        public IWorldCoordinate ScreenToWorldCoordinate(float screenX, float screenY, float worldZ)
        {
            float scale = ProjectionScale;
            float px = _context == null ? 0f : _context.PlayerX;
            float py = _context == null ? 0f : _context.PlayerY;
            if (float.IsNaN(px) || float.IsInfinity(px)) px = 0f;
            if (float.IsNaN(py) || float.IsInfinity(py)) py = 0f;
            if (scale <= 0f) return new WorldCoordinate(px, py, worldZ);

            float sx = (screenX - Width * 0.5f) / scale;
            float sy = (screenY - Height * 0.5f) / scale + worldZ * 0.5f;

            float dx = (2f * sy - sx) / (2f * Diagonal);
            float dy = (2f * sy + sx) / (2f * Diagonal);
            return new WorldCoordinate(px + dx, py + dy, worldZ);
        }

        public float ProjectionScale
        {
            get
            {
                float scale = _context == null ? 0f : _context.WorldToScreenScale;
                if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                    scale = Height > 0 ? Height / 90f : 12f;
                return scale;
            }
        }

        /// <summary>
        /// Screen coordinates created by this window retain the inverse
        /// projection. The generic compatibility ScreenCoordinate has no
        /// reference to a window, so it cannot do this itself.
        /// </summary>
        private sealed class HostScreenCoordinate : IScreenCoordinate
        {
            private readonly HostWindow _window;
            private float _x;
            private float _y;

            public HostScreenCoordinate(HostWindow window, float x, float y)
            {
                _window = window;
                _x = x;
                _y = y;
            }

            public float X { get { return _x; } set { _x = value; } }
            public float Y { get { return _y; } set { _y = value; } }

            public void Set(float x, float y)
            {
                _x = x;
                _y = y;
            }

            public float DistanceToCursor()
            {
                if (_window == null) return float.MaxValue;
                float dx = _x - _window.CursorX;
                float dy = _y - _window.CursorY;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            public IScreenCoordinate Offset(float ox, float oy)
            {
                return new HostScreenCoordinate(_window, _x + ox, _y + oy);
            }

            public IWorldCoordinate ToWorldCoordinate(float? customZ = null)
            {
                float z = customZ.HasValue ? customZ.Value : 0f;
                return _window == null
                    ? new WorldCoordinate(0f, 0f, z)
                    : _window.ScreenToWorldCoordinate(_x, _y, z);
            }

            public override string ToString()
            {
                return string.Format("{0:0}, {1:0}", _x, _y);
            }
        }
    }
}
