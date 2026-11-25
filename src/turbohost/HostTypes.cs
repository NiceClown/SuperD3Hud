using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// A stopwatch-like object handed to plugins. It counts real time and
    /// survives being started and stopped many times per frame.
    /// </summary>
    public class Watch : IWatch
    {
        private readonly object _sync = new object();
        private DateTime _startedUtc;
        private TimeSpan _accumulated;
        private bool _running;

        public Watch()
        {
            _startedUtc = DateTime.UtcNow;
            _running = true;
        }

        public bool IsRunning
        {
            get { lock (_sync) return _running; }
        }

        public long ElapsedMilliseconds
        {
            get
            {
                lock (_sync)
                {
                    TimeSpan total = _accumulated;
                    if (_running) total += DateTime.UtcNow - _startedUtc;
                    return (long)total.TotalMilliseconds;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_running) return;
                _startedUtc = DateTime.UtcNow;
                _running = true;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_running) return;
                _accumulated += DateTime.UtcNow - _startedUtc;
                _running = false;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _accumulated = TimeSpan.Zero;
                _startedUtc = DateTime.UtcNow;
            }
        }

        public void Restart()
        {
            lock (_sync)
            {
                _accumulated = TimeSpan.Zero;
                _startedUtc = DateTime.UtcNow;
                _running = true;
            }
        }

        /// <summary>True when more than the given time passed, then restarts.</summary>
        public bool TimerTest(int milliseconds)
        {
            if (milliseconds <= 0) return true;
            if (ElapsedMilliseconds < milliseconds) return false;
            Restart();
            return true;
        }
    }

    /// <summary>A performance counter placeholder; the host does not profile.</summary>
    public class PerfCounter : IPerfCounter
    {
        public double LastCount { get; set; }
        public double LastValue { get; set; }
    }

    /// <summary>Minimal world coordinate used by every actor and decorator.</summary>
    public class WorldCoordinate : IWorldCoordinate
    {
        public static HostWindow CurrentWindow;

        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }

        public WorldCoordinate()
        {
        }

        public WorldCoordinate(float x, float y, float z)
        {
            Set(x, y, z);
        }

        public WorldCoordinate(IWorldCoordinate source)
        {
            if (source != null) Set(source);
        }

        public bool IsValid
        {
            get
            {
                return !float.IsNaN(X) && !float.IsNaN(Y) && !float.IsNaN(Z)
                    && !float.IsInfinity(X) && !float.IsInfinity(Y) && !float.IsInfinity(Z);
            }
        }

        public void Set(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public void Set(IWorldCoordinate otherCoordinate)
        {
            if (otherCoordinate == null) { Set(0, 0, 0); return; }
            Set(otherCoordinate.X, otherCoordinate.Y, otherCoordinate.Z);
        }

        public void Add(IWorldCoordinate otherCoordinate)
        {
            if (otherCoordinate == null) return;
            X += otherCoordinate.X;
            Y += otherCoordinate.Y;
            Z += otherCoordinate.Z;
        }

        public IWorldCoordinate Offset(float x, float y, float z)
        {
            return new WorldCoordinate(X + x, Y + y, Z + z);
        }

        public bool Equals(IWorldCoordinate otherWorldCoordinate)
        {
            if (otherWorldCoordinate == null) return false;
            return Math.Abs(X - otherWorldCoordinate.X) < 0.001f
                && Math.Abs(Y - otherWorldCoordinate.Y) < 0.001f
                && Math.Abs(Z - otherWorldCoordinate.Z) < 0.001f;
        }

        public float XYDistanceTo(IWorldCoordinate otherCoordinate)
        {
            if (otherCoordinate == null) return float.MaxValue;
            return XYDistanceTo(otherCoordinate.X, otherCoordinate.Y);
        }

        public float XYDistanceTo(float x, float y)
        {
            float dx = X - x;
            float dy = Y - y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public float XYZDistanceTo(IWorldCoordinate otherWorldCoordinate)
        {
            if (otherWorldCoordinate == null) return float.MaxValue;
            return XYZDistanceTo(otherWorldCoordinate.X, otherWorldCoordinate.Y, otherWorldCoordinate.Z);
        }

        public float XYZDistanceTo(float x, float y, float z)
        {
            float dx = X - x;
            float dy = Y - y;
            float dz = Z - z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public float ZDiffTo(IWorldCoordinate wc)
        {
            if (wc == null) return 0;
            return Z - wc.Z;
        }

        public bool IsOnScreen(double r = 1)
        {
            if (!IsValid) return false;
            if (CurrentWindow != null)
            {
                float sx, sy;
                CurrentWindow.WorldToScreenCoordinate(X, Y, Z, out sx, out sy);
                int w = CurrentWindow.Size.Width;
                int h = CurrentWindow.Size.Height;
                float margin = (float)(64.0 * r);
                return sx >= -margin && sx <= w + margin && sy >= -margin && sy <= h + margin;
            }
            return true;
        }

        public IScreenCoordinate ToScreenCoordinate(bool raw = false, bool precise = false)
        {
            if (CurrentWindow != null)
                return CurrentWindow.WorldToScreenCoordinate(X, Y, Z, raw, precise);
            return new ScreenCoordinate(0, 0);
        }

        public void SetScreenCoordinate(IScreenCoordinate sc, bool raw = false, bool precise = false)
        {
            if (CurrentWindow != null && sc != null)
            {
                IWorldCoordinate wc = CurrentWindow.ScreenToWorldCoordinate(sc.X, sc.Y, Z);
                if (wc != null) Set(wc.X, wc.Y, wc.Z);
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", X, Y, Z);
        }

        public string ToStringCompact()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}", X, Y);
        }

        public string ToStringCompactPrecise()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}, {1:0.00}", X, Y);
        }
    }

    /// <summary>A screen coordinate in client pixels.</summary>
    public class ScreenCoordinate : IScreenCoordinate
    {
        public float X { get; set; }
        public float Y { get; set; }

        public ScreenCoordinate(float x, float y)
        {
            X = x;
            Y = y;
        }

        public void Set(float x, float y)
        {
            X = x; Y = y;
        }

        public float DistanceToCursor()
        {
            return 0;
        }

        public IScreenCoordinate Offset(float ox, float oy)
        {
            return new ScreenCoordinate(X + ox, Y + oy);
        }

        public IWorldCoordinate ToWorldCoordinate(float? customZ = null)
        {
            return new WorldCoordinate(0, 0, customZ.HasValue ? customZ.Value : 0f);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0}, {1:0}", X, Y);
        }
    }

    /// <summary>A UI element of the game interface.</summary>
    public class UiElement : IUiElement
    {
        private readonly string _path;
        public Func<bool> VisibleFunc;
        public Func<RectangleF> RectFunc;

        public UiElement(string path, Func<bool> visibleFunc = null, Func<RectangleF> rectFunc = null)
        {
            _path = path ?? "";
            VisibleFunc = visibleFunc;
            RectFunc = rectFunc;
        }

        public string Path { get { return _path; } }
        public bool Visible { get { return VisibleFunc != null ? VisibleFunc() : false; } }
        public long LastVisibleSystemTick { get { return Visible ? DateTime.UtcNow.Ticks : 0; } }
        public RectangleF Rectangle { get { return RectFunc != null ? RectFunc() : RectangleF.Empty; } }
        public int AnimState { get { return 0; } }
        public uint TextureSno { get { return 0; } }
        public float MinimapOffsetX { get { return 0; } }
        public float MinimapOffsetY { get { return 0; } }
        public uint LegendaryGemAcdId { get { return 0; } }
        public uint AcdId { get { return 0; } }
        public IUiElement ReplacementWhenNotVisible { get { return null; } }

        public bool CoordinateInsideRectangle(int x, int y)
        {
            RectangleF r = Rectangle;
            return r.Contains(x, y);
        }
        public void Refresh() { }
        public string ReadText(System.Text.Encoding encoding, bool removeColors) { return ""; }
    }
}
