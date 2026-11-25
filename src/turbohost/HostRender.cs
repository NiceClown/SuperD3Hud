using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using Turbo.Plugins;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using LineJoin = System.Drawing.Drawing2D.LineJoin;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// A brush handed to plugins. The interface speaks in Direct2D terms, but
    /// the drawing is done with GDI+ on the host's layered overlay, so a plugin
    /// needs no graphics device of its own.
    /// </summary>
    public class HostBrush : IBrush
    {
        // Created on first use: drawing is GDI+, so most brushes never need a
        // Direct2D object at all, and the factory is only built if a plugin
        // actually reads IBrush.StrokeStyle.
        private static SharpDX.Direct2D1.Factory _d2dFactory;

        // One StrokeStyle per (dash, start cap, end cap). Plugins create
        // hundreds of brushes but only a handful of combinations exist, and a
        // StrokeStyle per brush leaked one native object per CreateBrush.
        private static readonly Dictionary<int, SharpDX.Direct2D1.StrokeStyle> _strokeStyles =
            new Dictionary<int, SharpDX.Direct2D1.StrokeStyle>();

        static HostBrush()
        {
            // SharpDX 4.2 leaks the native object of any wrapper that is not
            // disposed explicitly unless this flag is on. Plugins never dispose
            // what the host hands them, so the finalizer must release it.
            SharpDX.Configuration.EnableReleaseOnFinalizer = true;
        }

        private readonly HostCanvas _canvas;
        private readonly HostWindow _window;
        private readonly Color _color;
        private readonly System.Drawing.Drawing2D.DashStyle _dash;
        private readonly SharpDX.Direct2D1.DashStyle _d2dDash;
        private readonly CapStyle _startCap;
        private readonly CapStyle _endCap;
        private float _strokeWidth;
        private float _opacity = 1f;

        public HostBrush(HostCanvas canvas, int a, int r, int g, int b, float strokeWidth, System.Drawing.Drawing2D.DashStyle dash)
            : this(canvas, null, a, r, g, b, strokeWidth, ConvertFromGdiDash(dash), CapStyle.Flat, CapStyle.Flat)
        {
        }

        public HostBrush(HostCanvas canvas, HostWindow window, int a, int r, int g, int b, float strokeWidth,
                         SharpDX.Direct2D1.DashStyle dash = SharpDX.Direct2D1.DashStyle.Solid,
                         CapStyle startCap = CapStyle.Flat,
                         CapStyle endCap = CapStyle.Flat)
        {
            _canvas = canvas;
            _window = window;
            _color = Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
            _strokeWidth = strokeWidth;
            _dash = ConvertDash(dash);
            _d2dDash = dash;
            _startCap = startCap;
            _endCap = endCap;
        }

        private static SharpDX.Direct2D1.StrokeStyle GetStrokeStyle(SharpDX.Direct2D1.DashStyle dash,
                                                                    CapStyle startCap, CapStyle endCap)
        {
            int key = ((int)dash & 0xFF) | (((int)startCap & 0xFF) << 8) | (((int)endCap & 0xFF) << 16);
            lock (_strokeStyles)
            {
                SharpDX.Direct2D1.StrokeStyle style;
                if (_strokeStyles.TryGetValue(key, out style)) return style;
                try
                {
                    if (_d2dFactory == null) _d2dFactory = new SharpDX.Direct2D1.Factory();
                    var props = new StrokeStyleProperties
                    {
                        StartCap = startCap,
                        EndCap = endCap,
                        DashCap = CapStyle.Flat,
                        LineJoin = SharpDX.Direct2D1.LineJoin.Round,
                        MiterLimit = 10f,
                        DashStyle = dash,
                        DashOffset = 0f
                    };
                    style = new StrokeStyle(_d2dFactory, props);
                }
                catch
                {
                    // Cached as null too, so a failing combination is not
                    // retried on every access.
                    style = null;
                }
                _strokeStyles[key] = style;
                return style;
            }
        }

        public float Opacity
        {
            get { return _opacity; }
            set { _opacity = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        public float StrokeWidth
        {
            get { return _strokeWidth; }
            set { _strokeWidth = value; }
        }

        public SharpDX.Direct2D1.StrokeStyle StrokeStyle
        {
            get { return GetStrokeStyle(_d2dDash, _startCap, _endCap); }
        }

        public float RealStrokeWidth
        {
            get { return Math.Abs(_strokeWidth) < 0.1f ? 1f : Math.Abs(_strokeWidth); }
        }

        private Color Effective
        {
            get
            {
                int alpha = (int)Math.Round(_color.A * _opacity);
                return Color.FromArgb(Clamp(alpha), _color.R, _color.G, _color.B);
            }
        }

        public void DrawEllipse(float x1, float y1, float r1, float r2, float strokeWidthCorrection = 0)
        {
            if (_strokeWidth <= 0f)
            {
                _canvas.FillEllipse(Effective, x1, y1, r1, r2);
            }
            else
            {
                float w = RealStrokeWidth + strokeWidthCorrection;
                _canvas.DrawEllipse(Effective, w < 0.5f ? 0.5f : w, x1, y1, r1, r2, _dash);
            }
        }

        public void DrawLine(float x1, float y1, float x2, float y2, float strokeWidthCorrection = 0)
        {
            float w = RealStrokeWidth + strokeWidthCorrection;
            _canvas.DrawLine(Effective, w < 0.5f ? 0.5f : w, x1, y1, x2, y2, _dash);
        }

        public void DrawLineGridFit(float x1, float y1, float x2, float y2, float strokeWidthCorrection = 0)
        {
            DrawLine(x1, y1, x2, y2, strokeWidthCorrection);
        }

        public void DrawLineWorld(float x1, float y1, float z1, float x2, float y2, float z2, float strokeWidthCorrection = 0)
        {
            HostWindow win = _window ?? WorldCoordinate.CurrentWindow;
            if (win == null) return;
            float sx1, sy1, sx2, sy2;
            win.WorldToScreenCoordinate(x1, y1, z1, out sx1, out sy1);
            win.WorldToScreenCoordinate(x2, y2, z2, out sx2, out sy2);
            DrawLine(sx1, sy1, sx2, sy2, strokeWidthCorrection);
        }

        public void DrawLineWorld(IWorldCoordinate wc1, IWorldCoordinate wc2, float strokeWidthCorrection = 0)
        {
            if (wc1 == null || wc2 == null) return;
            DrawLineWorld(wc1.X, wc1.Y, wc1.Z, wc2.X, wc2.Y, wc2.Z, strokeWidthCorrection);
        }

        public void DrawRectangle(RectangleF rectangle)
        {
            DrawRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        public void DrawRectangle(SharpDX.RectangleF rectangle)
        {
            DrawRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        public void DrawRectangle(float x, float y, float w, float h)
        {
            if (_strokeWidth <= 0f)
            {
                _canvas.FillRectangle(Effective, x, y, w, h);
            }
            else
            {
                _canvas.DrawRectangle(Effective, RealStrokeWidth, x, y, w, h, _dash);
            }
        }

        public void DrawRectangleGridFit(float x, float y, float w, float h)
        {
            DrawRectangle(x, y, w, h);
        }

        public void DrawWorldEllipse(float radius, int sectionCount, IWorldCoordinate coordinate)
        {
            if (coordinate == null) return;
            DrawWorldEllipse(radius, sectionCount, coordinate.X, coordinate.Y, coordinate.Z);
        }

        public void DrawWorldEllipse(float radius, int sectionCount, float x, float y, float z)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) return;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return;

            HostWindow win = _window ?? WorldCoordinate.CurrentWindow;
            if (win == null) return;
            float sx, sy;
            win.WorldToScreenCoordinate(x, y, z, out sx, out sy);
            if (float.IsNaN(sx) || float.IsNaN(sy) || float.IsInfinity(sx) || float.IsInfinity(sy)) return;
            if (Math.Abs(sx) > 100000f || Math.Abs(sy) > 100000f) return;

            float scale = win.ProjectionScale;
            float r1 = radius * scale;
            // The ground of the game is compressed by the same factor as the
            // projection, so a circle on the ground looks like this ellipse.
            float r2 = r1 * HostWindow.GroundCompression;
            if (float.IsNaN(r1) || float.IsNaN(r2) || r1 <= 0.1f || r2 <= 0.1f || r1 > 50000f || r2 > 50000f) return;

            DrawEllipse(sx, sy, r1, r2);
        }

        public void DrawWorldPlus(float radius, float x, float y, float z)
        {
            DrawLineWorld(x - radius, y, z, x + radius, y, z);
            DrawLineWorld(x, y - radius, z, x, y + radius, z);
        }

        public void DrawGeometry(SharpDX.Direct2D1.Geometry geometry)
        {
        }

        private static System.Drawing.Drawing2D.DashStyle ConvertDash(SharpDX.Direct2D1.DashStyle dash)
        {
            switch (dash)
            {
                case SharpDX.Direct2D1.DashStyle.Dash: return System.Drawing.Drawing2D.DashStyle.Dash;
                case SharpDX.Direct2D1.DashStyle.Dot: return System.Drawing.Drawing2D.DashStyle.Dot;
                case SharpDX.Direct2D1.DashStyle.DashDot: return System.Drawing.Drawing2D.DashStyle.DashDot;
                case SharpDX.Direct2D1.DashStyle.DashDotDot: return System.Drawing.Drawing2D.DashStyle.DashDotDot;
                default: return System.Drawing.Drawing2D.DashStyle.Solid;
            }
        }

        private static SharpDX.Direct2D1.DashStyle ConvertFromGdiDash(System.Drawing.Drawing2D.DashStyle dash)
        {
            switch (dash)
            {
                case System.Drawing.Drawing2D.DashStyle.Dash: return SharpDX.Direct2D1.DashStyle.Dash;
                case System.Drawing.Drawing2D.DashStyle.Dot: return SharpDX.Direct2D1.DashStyle.Dot;
                case System.Drawing.Drawing2D.DashStyle.DashDot: return SharpDX.Direct2D1.DashStyle.DashDot;
                case System.Drawing.Drawing2D.DashStyle.DashDotDot: return SharpDX.Direct2D1.DashStyle.DashDotDot;
                default: return SharpDX.Direct2D1.DashStyle.Solid;
            }
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }
    }

    /// <summary>
    /// A font handed to plugins. Text is measured with DirectWrite and drawn
    /// with GDI+; both use the same pixel size and typographic layout, so the
    /// width a plugin measures is the width that is drawn. Plugins centre
    /// labels and size their backgrounds from <c>layout.Metrics</c>, and any
    /// mismatch shows up as clipped or off-centre labels.
    /// </summary>
    public class HostFont : IFont
    {
        private static readonly SharpDX.DirectWrite.Factory _dwriteFactory = new SharpDX.DirectWrite.Factory();
        private static readonly Dictionary<string, SharpDX.DirectWrite.TextFormat> _formatCache =
            new Dictionary<string, SharpDX.DirectWrite.TextFormat>();

        /// <summary>A cached layout is dropped after this many frames without use.</summary>
        internal const int LayoutMaxAgeFrames = 60;

        /// <summary>Upper bound on cached layouts per font.</summary>
        internal const int LayoutCacheCap = 2000;

        static HostFont()
        {
            // Safety net for every layout the cache does not own (the
            // ManualDispose variant, overflow layouts, the cache of a font
            // that was collected): without this flag SharpDX 4.2 never
            // releases the native IDWriteTextLayout of an undisposed wrapper.
            // DirectWrite objects come from a shared, thread-safe factory, so
            // releasing them on the finalizer thread is safe.
            SharpDX.Configuration.EnableReleaseOnFinalizer = true;
        }

        /// <summary>
        /// What a layout was measured with, kept in <c>layout.Tag</c> so that
        /// DrawText(TextLayout, ...) draws exactly what the plugin measured.
        /// For a cached layout this object is also its cache entry.
        /// </summary>
        private sealed class LayoutInfo
        {
            public string Text;
            public float MaxWidth;
            public float MaxHeight;
            public bool WordWrap;
            public float PixelSize;
            public float MeasuredWidth;
            public float MeasuredHeight;
            public SharpDX.DirectWrite.TextLayout Layout;
            /// <summary>HostCanvas.Frame when the layout was last requested or drawn.</summary>
            public int LastFrame;
            /// <summary>True while the layout is owned by the cache.</summary>
            public bool Cached;
        }

        /// <summary>
        /// Cache key. Family, weight and style are fixed per HostFont, so the
        /// per-font cache only needs the values that can change between calls.
        /// </summary>
        private struct LayoutKey : IEquatable<LayoutKey>
        {
            public string Text;
            public float PixelSize;
            public int MaxWidth;
            public int MaxHeight;
            public bool WordWrap;

            public bool Equals(LayoutKey other)
            {
                return PixelSize == other.PixelSize && MaxWidth == other.MaxWidth &&
                       MaxHeight == other.MaxHeight && WordWrap == other.WordWrap &&
                       string.Equals(Text, other.Text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is LayoutKey && Equals((LayoutKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Text != null ? StringComparer.Ordinal.GetHashCode(Text) : 0;
                    h = h * 31 + PixelSize.GetHashCode();
                    h = h * 31 + MaxWidth;
                    h = h * 31 + MaxHeight;
                    h = h * 31 + (WordWrap ? 1 : 0);
                    return h;
                }
            }
        }

        // TurboHUD plugins call GetTextLayout for every label on every frame
        // and never dispose the result: the TurboHUD contract is that the host
        // caches it. Entries are dropped by age (see SweepLayouts), never by a
        // strict LRU: a plugin may read .Metrics of a layout it got earlier in
        // the same frame, so a layout handed out in the current frame must
        // stay alive at least until the next HostCanvas.BeginFrame.
        private readonly Dictionary<LayoutKey, LayoutInfo> _layouts = new Dictionary<LayoutKey, LayoutInfo>();
        private int _registered;

        private readonly HostCanvas _canvas;
        private readonly string _family;
        private float _size;
        private readonly Color _color;
        private readonly bool _bold;
        private readonly bool _italic;
        private Color _shadowColor;
        private bool _heavyShadow;
        private float _opacity = 1f;
        private int _maxWidth;
        private int _maxHeight;
        private bool _wordWrap;

        public HostFont(HostCanvas canvas, string family, float size, int a, int r, int g, int b,
                        bool bold, bool italic, int shadowA, int shadowR, int shadowG, int shadowB,
                        bool heavyShadow)
        {
            _canvas = canvas;
            _family = string.IsNullOrEmpty(family) ? "Segoe UI" : family;
            _size = size < 1f ? 1f : size;
            _color = Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
            _bold = bold;
            _italic = italic;
            _shadowColor = Color.FromArgb(Clamp(shadowA), Clamp(shadowR), Clamp(shadowG), Clamp(shadowB));
            _heavyShadow = heavyShadow;
        }

        public float Opacity
        {
            get { return _opacity; }
            set { _opacity = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        public int MaxHeight
        {
            get { return _maxHeight; }
            set { _maxHeight = value; }
        }

        public int MaxWidth
        {
            get { return _maxWidth; }
            set { _maxWidth = value; }
        }

        public bool WordWrap
        {
            get { return _wordWrap; }
            set { _wordWrap = value; }
        }

        public bool HeavyShadow
        {
            get { return _heavyShadow; }
            set { _heavyShadow = value; }
        }

        public void SetSize(float size)
        {
            if (size >= 1f && size <= 512f)
            {
                _size = size;
            }
        }

        public void SetMaxSize(int maxWidth, int maxHeight)
        {
            _maxWidth = maxWidth;
            _maxHeight = maxHeight;
        }

        public void SetShadowBrush(int a, int r, int g, int b, bool heavy)
        {
            _shadowColor = Color.FromArgb(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
            _heavyShadow = heavy;
        }

        /// <summary>Size in pixels for the current window, see HostCanvas.PixelSize.</summary>
        private float PixelSize
        {
            get { return _canvas != null ? _canvas.PixelSize(_size) : _size * 4f / 3f; }
        }

        public void DrawText(string text, IScreenCoordinate coordinate, bool enableLayoutCache = true)
        {
            if (coordinate == null) return;
            DrawText(text, coordinate.X, coordinate.Y, enableLayoutCache);
        }

        public void DrawText(string text, float x, float y, bool enableLayoutCache = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            Draw(text, x, y, PixelSize, _maxWidth, _maxHeight, _wordWrap, 0f);
        }

        private void Draw(string text, float x, float y, float pixelSize, float maxWidth, float maxHeight, bool wrap, float measuredWidth)
        {
            if (_canvas == null) return;
            int alpha = (int)Math.Round(_color.A * _opacity);
            Color color = Color.FromArgb(Clamp(alpha), _color.R, _color.G, _color.B);
            int shadowAlpha = (int)Math.Round(_shadowColor.A * _opacity);
            Color shadow = Color.FromArgb(Clamp(shadowAlpha), _shadowColor.R, _shadowColor.G, _shadowColor.B);
            _canvas.DrawText(text, x, y, color, _family, pixelSize, _bold, _italic,
                             shadow, _heavyShadow, maxWidth, maxHeight, wrap, measuredWidth);
        }

        /// <summary>
        /// Returns a layout owned by the host cache. The plugin must not
        /// dispose it. It stays valid at least until the next
        /// HostCanvas.BeginFrame, and is reused for as long as the same text
        /// is requested or drawn at least once every LayoutMaxAgeFrames frames.
        /// </summary>
        public SharpDX.DirectWrite.TextLayout GetTextLayout(string text)
        {
            if (text == null) text = string.Empty;
            float pixelSize = PixelSize;
            // Without a canvas there is no frame counter to age entries by.
            if (_canvas == null) return CreateLayout(text, pixelSize);

            LayoutKey key = new LayoutKey
            {
                Text = text,
                PixelSize = pixelSize,
                MaxWidth = _maxWidth,
                MaxHeight = _maxHeight,
                WordWrap = _wordWrap
            };
            int frame = _canvas.Frame;
            SharpDX.DirectWrite.TextLayout layout;

            lock (_layouts)
            {
                LayoutInfo info;
                if (_layouts.TryGetValue(key, out info))
                {
                    if (!info.Layout.IsDisposed)
                    {
                        info.LastFrame = frame;
                        return info.Layout;
                    }
                    // A plugin disposed a cached layout against the contract.
                    // Forget it and build a fresh one below.
                    info.Cached = false;
                    _layouts.Remove(key);
                }

                // At the cap, first drop whatever was not used in this frame
                // or the previous one. The previous frame must be kept too:
                // AfterCollect/BeforeRender run in TurboHostRuntime.Tick, which
                // comes before Draw's BeginFrame, so layouts handed out there
                // carry the previous frame number but are still read by the
                // paint pass that follows. olderThan = frame - 2 means "last
                // used two or more frames ago"; SweepLocked also protects
                // anything at most one frame old regardless.
                if (_layouts.Count >= LayoutCacheCap) SweepLocked(frame, unchecked(frame - 2));

                layout = CreateLayout(text, pixelSize);
                if (_layouts.Count >= LayoutCacheCap)
                {
                    // Every cached layout was handed out during this frame or
                    // the previous one (see above), so none may be disposed yet. This one stays uncached and
                    // the finalizer releases it (EnableReleaseOnFinalizer).
                    return layout;
                }

                info = (LayoutInfo)layout.Tag;
                info.LastFrame = frame;
                info.Cached = true;
                _layouts[key] = info;
            }

            // The canvas sweeps every font that has cached something, so a
            // font that stops drawing still frees its layouts.
            if (Interlocked.CompareExchange(ref _registered, 1, 0) == 0)
                _canvas.RegisterLayoutOwner(this);
            return layout;
        }

        /// <summary>
        /// Returns a fresh layout that the plugin owns and disposes itself. It
        /// is never cached.
        /// </summary>
        public SharpDX.DirectWrite.TextLayout GetTextLayoutManualDispose(string text)
        {
            return CreateLayout(text ?? string.Empty, PixelSize);
        }

        /// <summary>
        /// Disposes cached layouts last used LayoutMaxAgeFrames or more frames
        /// before <paramref name="frame"/>. Called from HostCanvas.BeginFrame.
        /// Returns the number of layouts still cached.
        /// </summary>
        internal int SweepLayouts(int frame)
        {
            lock (_layouts)
            {
                SweepLocked(frame, unchecked(frame - LayoutMaxAgeFrames));
                return _layouts.Count;
            }
        }

        /// <summary>Number of layouts currently cached by this font (diagnostics).</summary>
        public int CachedLayoutCount
        {
            get { lock (_layouts) return _layouts.Count; }
        }

        // Disposes entries whose LastFrame is at or before 'olderThan'. The
        // frame counter may wrap, so ages are compared by subtraction. An
        // entry used in 'frame' or the frame before it is never removed,
        // whatever 'olderThan' says: a plugin may still be holding it. The
        // previous frame counts because layouts obtained in the Tick hooks
        // (before Draw advances the counter) are used in the next paint pass.
        private void SweepLocked(int frame, int olderThan)
        {
            if (_layouts.Count == 0) return;
            List<LayoutKey> dead = null;
            foreach (KeyValuePair<LayoutKey, LayoutInfo> pair in _layouts)
            {
                int last = pair.Value.LastFrame;
                if (unchecked(frame - last) <= 1) continue;
                if (unchecked(olderThan - last) < 0) continue;
                if (dead == null) dead = new List<LayoutKey>();
                dead.Add(pair.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
            {
                LayoutInfo info = _layouts[dead[i]];
                _layouts.Remove(dead[i]);
                info.Cached = false;
                try { info.Layout.Dispose(); }
                catch { }
            }
        }

        private SharpDX.DirectWrite.TextLayout CreateLayout(string text, float pixelSize)
        {
            SharpDX.DirectWrite.TextFormat format = GetOrCreateFormat(pixelSize);
            float maxW = _maxWidth > 0 ? (float)_maxWidth : 10000f;
            float maxH = _maxHeight > 0 ? (float)_maxHeight : 10000f;
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(_dwriteFactory, text, format, maxW, maxH);
            try
            {
                layout.WordWrapping = _wordWrap ? SharpDX.DirectWrite.WordWrapping.Wrap
                                                : SharpDX.DirectWrite.WordWrapping.NoWrap;
            }
            catch { }

            float measuredW = 0f;
            float measuredH = 0f;
            try
            {
                TextMetrics metrics = layout.Metrics;
                measuredW = metrics.Width;
                measuredH = metrics.Height;
            }
            catch { }

            // Tag only. The ConditionalWeakTable used before added a dependent
            // GC handle per layout; it is not needed because every layout a
            // plugin can hold is created here and carries its info in Tag.
            layout.Tag = new LayoutInfo
            {
                Text = text,
                MaxWidth = _maxWidth,
                MaxHeight = _maxHeight,
                WordWrap = _wordWrap,
                PixelSize = pixelSize,
                MeasuredWidth = measuredW,
                MeasuredHeight = measuredH,
                Layout = layout
            };
            return layout;
        }

        private SharpDX.DirectWrite.TextFormat GetOrCreateFormat(float pixelSize)
        {
            string key = _family + "|" + pixelSize.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                         "|" + (_bold ? "b" : "n") + (_italic ? "i" : "n");
            lock (_formatCache)
            {
                SharpDX.DirectWrite.TextFormat fmt;
                if (_formatCache.TryGetValue(key, out fmt)) return fmt;
                var weight = _bold ? SharpDX.DirectWrite.FontWeight.Bold : SharpDX.DirectWrite.FontWeight.Normal;
                var style = _italic ? SharpDX.DirectWrite.FontStyle.Italic : SharpDX.DirectWrite.FontStyle.Normal;
                fmt = new SharpDX.DirectWrite.TextFormat(_dwriteFactory, _family, weight, style, pixelSize);
                _formatCache[key] = fmt;
                return fmt;
            }
        }

        public void DrawText(SharpDX.DirectWrite.TextLayout textLayout, float x, float y)
        {
            if (textLayout == null) return;
            // Drawing needs only the recorded info, not the native layout, so
            // the disposed check is not needed: a layout the cache already
            // swept still draws correctly if a plugin kept it in a field.
            LayoutInfo info = textLayout.Tag as LayoutInfo;
            if (info == null || string.IsNullOrEmpty(info.Text)) return;

            // A plugin that keeps a layout and only draws it, without asking
            // for it again, still counts as using it this frame, so the sweep
            // leaves it alive.
            if (info.Cached && _canvas != null) info.LastFrame = _canvas.Frame;

            // The layout keeps the size and box it was measured with, so the
            // drawn text matches the metrics the plugin used for placement.
            Draw(info.Text, x, y, info.PixelSize, info.MaxWidth, info.MaxHeight, info.WordWrap, info.MeasuredWidth);
        }

        public void DrawText(SharpDX.DirectWrite.TextLayout textLayout, IScreenCoordinate coordinate)
        {
            if (coordinate == null) return;
            DrawText(textLayout, coordinate.X, coordinate.Y);
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }
    }

    /// <summary>
    /// The GDI+ surface the host draws on. A plugin never sees it directly: it
    /// receives brushes and fonts, which forward every call here.
    /// </summary>
    public class HostCanvas
    {
        private readonly object _sync = new object();
        private Graphics _graphics;
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        // One Pen and one SolidBrush reused for every primitive (ARCH-11).
        // Recolouring them is far cheaper than creating and disposing a GDI+
        // object per call, and all use happens under _sync.
        private Pen _pen;
        private SolidBrush _solid;

        // Frame counter for the TextLayout cache, advanced by BeginFrame.
        private int _frame;

        // Fonts that hold cached layouts. Weak, so a font a plugin drops can
        // still be collected; its layouts are then released by the finalizer.
        private readonly List<WeakReference> _layoutOwners = new List<WeakReference>();

        /// <summary>
        /// Current frame number, as counted by <see cref="BeginFrame"/>. Fonts
        /// stamp cached layouts with it.
        /// </summary>
        public int Frame
        {
            get { return Volatile.Read(ref _frame); }
        }

        /// <summary>
        /// Starts a new plugin frame: call once per Draw, before any plugin
        /// paints. Advances the frame counter and disposes cached text layouts
        /// that no plugin has requested or drawn for
        /// HostFont.LayoutMaxAgeFrames frames. A layout handed out during the
        /// frame that just ended is never disposed here, since its age is 1.
        /// </summary>
        public void BeginFrame()
        {
            int frame = Interlocked.Increment(ref _frame);

            // A sweep walks every cached layout, so it runs only every 16th
            // frame. Unused layouts therefore live 60 to 75 frames, which
            // only affects memory, never safety.
            if ((frame & 15) != 0) return;
            List<HostFont> owners = null;
            lock (_layoutOwners)
            {
                for (int i = _layoutOwners.Count - 1; i >= 0; i--)
                {
                    HostFont font = _layoutOwners[i].Target as HostFont;
                    if (font == null)
                    {
                        _layoutOwners.RemoveAt(i);
                        continue;
                    }
                    if (owners == null) owners = new List<HostFont>();
                    owners.Add(font);
                }
            }
            if (owners == null) return;
            for (int i = 0; i < owners.Count; i++)
            {
                try { owners[i].SweepLayouts(frame); }
                catch { }
            }
        }

        /// <summary>Called by a HostFont the first time it caches a layout.</summary>
        internal void RegisterLayoutOwner(HostFont font)
        {
            if (font == null) return;
            lock (_layoutOwners) _layoutOwners.Add(new WeakReference(font));
        }

        /// <summary>Total layouts cached by all live fonts on this canvas (diagnostics).</summary>
        public int CachedLayoutCount
        {
            get
            {
                int total = 0;
                lock (_layoutOwners)
                {
                    for (int i = 0; i < _layoutOwners.Count; i++)
                    {
                        HostFont font = _layoutOwners[i].Target as HostFont;
                        if (font != null) total += font.CachedLayoutCount;
                    }
                }
                return total;
            }
        }

        public void Begin(Graphics graphics)
        {
            lock (_sync)
            {
                _graphics = graphics;
                if (_graphics != null)
                {
                    _graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    _graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                }
            }
        }

        public void End()
        {
            lock (_sync) _graphics = null;
        }

        public void DrawEllipse(Color color, float width, float x, float y, float r1, float r2)
        {
            DrawEllipse(color, width, x, y, r1, r2, System.Drawing.Drawing2D.DashStyle.Solid);
        }

        internal void DrawEllipse(Color color, float width, float x, float y, float r1, float r2, System.Drawing.Drawing2D.DashStyle dash)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(r1) || float.IsNaN(r2) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(r1) || float.IsInfinity(r2))
                return;
            if (r1 <= 0f || r2 <= 0f) return;

            lock (_sync)
            {
                if (_graphics == null) return;
                _graphics.DrawEllipse(PenFor(color, width, dash), x - r1, y - r2, r1 * 2f, r2 * 2f);
            }
        }

        public void FillEllipse(Color color, float x, float y, float r1, float r2)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(r1) || float.IsNaN(r2) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(r1) || float.IsInfinity(r2))
                return;
            if (r1 <= 0f || r2 <= 0f) return;

            lock (_sync)
            {
                if (_graphics == null) return;
                _graphics.FillEllipse(BrushFor(color), x - r1, y - r2, r1 * 2f, r2 * 2f);
            }
        }

        public void DrawLine(Color color, float width, float x1, float y1, float x2, float y2)
        {
            DrawLine(color, width, x1, y1, x2, y2, System.Drawing.Drawing2D.DashStyle.Solid);
        }

        internal void DrawLine(Color color, float width, float x1, float y1, float x2, float y2, System.Drawing.Drawing2D.DashStyle dash)
        {
            if (float.IsNaN(x1) || float.IsNaN(y1) || float.IsNaN(x2) || float.IsNaN(y2)) return;
            lock (_sync)
            {
                if (_graphics == null) return;
                _graphics.DrawLine(PenFor(color, width, dash), x1, y1, x2, y2);
            }
        }

        public void DrawRectangle(Color color, float width, float x, float y, float w, float h)
        {
            DrawRectangle(color, width, x, y, w, h, System.Drawing.Drawing2D.DashStyle.Solid);
        }

        internal void DrawRectangle(Color color, float width, float x, float y, float w, float h, System.Drawing.Drawing2D.DashStyle dash)
        {
            if (w <= 0f || h <= 0f) return;
            lock (_sync)
            {
                if (_graphics == null) return;
                _graphics.DrawRectangle(PenFor(color, width, dash), x, y, w, h);
            }
        }

        public void FillRectangle(Color color, float x, float y, float w, float h)
        {
            if (w <= 0f || h <= 0f) return;
            lock (_sync)
            {
                if (_graphics == null) return;
                _graphics.FillRectangle(BrushFor(color), x, y, w, h);
            }
        }

        /// <summary>
        /// Window height divided by 1080. TurboHUD font sizes are given for a
        /// 1080p window and grow with the resolution. Set by the runtime every
        /// frame.
        /// </summary>
        public float UiScale
        {
            get { return _uiScale; }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                _uiScale = value < 0.5f ? 0.5f : (value > 4f ? 4f : value);
            }
        }
        private float _uiScale = 1f;

        /// <summary>User multiplier for all plugin fonts ([turbohud] FontScale).</summary>
        public float FontScale
        {
            get { return _fontScale; }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                _fontScale = value < 0.3f ? 0.3f : (value > 4f ? 4f : value);
            }
        }
        private float _fontScale = 1f;

        /// <summary>
        /// Converts a plugin font size (points at 1080p) to pixels for the
        /// current window. Measuring and drawing both use this value.
        /// </summary>
        public float PixelSize(float size)
        {
            float px = size * (4f / 3f) * _uiScale * _fontScale;
            if (px < 5f) px = 5f;
            if (px > 400f) px = 400f;
            // Quantised so the font caches stay small while the window is resized.
            return (float)Math.Round(px * 4f) / 4f;
        }

        public void DrawText(string text, float x, float y, Color color, string family,
                            float pixelSize, bool bold, bool italic, Color shadow, bool heavy)
        {
            DrawText(text, x, y, color, family, pixelSize, bold, italic, shadow, heavy, 0f, 0f, false);
        }

        public void DrawText(string text, float x, float y, Color color, string family,
                            float pixelSize, bool bold, bool italic, Color shadow, bool heavy,
                            float maxWidth, float maxHeight, bool wrap)
        {
            DrawText(text, x, y, color, family, pixelSize, bold, italic, shadow, heavy,
                     maxWidth, maxHeight, wrap, 0f);
        }

        internal void DrawText(string text, float x, float y, Color color, string family,
                               float pixelSize, bool bold, bool italic, Color shadow, bool heavy,
                               float maxWidth, float maxHeight, bool wrap, float layoutMaxWidth)
        {
            lock (_sync)
            {
                if (_graphics == null || string.IsNullOrEmpty(text)) return;
                Font font = GetFont(family, pixelSize, bold, italic);
                StringFormat format = wrap ? _wrapFormat : _lineFormat;
                bool boxed = maxWidth > 0f || maxHeight > 0f;
                float w = maxWidth > 0f ? maxWidth : 10000f;
                float h = maxHeight > 0f ? maxHeight : 10000f;

                float scaleX = 1f;
                if (!wrap && layoutMaxWidth > 0f)
                {
                    float limitW = (maxWidth > 0f && layoutMaxWidth > maxWidth) ? maxWidth : layoutMaxWidth;
                    float gdiW = _graphics.MeasureString(text, font, new SizeF(10000f, 10000f), format).Width;
                    float overhangPad = (italic ? pixelSize * 0.22f : 0f) + 1.15f + pixelSize * 0.03f;
                    float effectiveW = gdiW + overhangPad;
                    if (effectiveW > limitW && limitW > 0.5f)
                        scaleX = Math.Max(0.5f, limitW / effectiveW);
                }

                if (scaleX < 0.999f)
                {
                    GraphicsState saved = _graphics.Save();
                    try
                    {
                        _graphics.TranslateTransform(x, y);
                        _graphics.ScaleTransform(scaleX, 1f);
                        float scaledW = boxed ? w / scaleX : w;
                        DrawTextWithShadow(text, font, color, shadow, heavy, 0f, 0f, scaledW, h, boxed, format);
                    }
                    finally
                    {
                        _graphics.Restore(saved);
                    }
                }
                else
                {
                    DrawTextWithShadow(text, font, color, shadow, heavy, x, y, w, h, boxed, format);
                }
            }
        }

        private void DrawTextWithShadow(string text, Font font, Color color, Color shadow, bool heavy,
                                        float x, float y, float w, float h, bool boxed, StringFormat format)
        {
            if (shadow.A > 0)
            {
                SolidBrush shadowBrush = BrushFor(shadow);
                if (heavy)
                {
                    // A heavy shadow is an outline: readable on any ground.
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            DrawString(text, font, shadowBrush, x + dx, y + dy, w, h, boxed, format);
                        }
                }
                else
                {
                    DrawString(text, font, shadowBrush, x + 1f, y + 1f, w, h, boxed, format);
                }
            }

            DrawString(text, font, BrushFor(color), x, y, w, h, boxed, format);
        }

        private void DrawString(string text, Font font, System.Drawing.Brush brush, float x, float y,
                                float w, float h, bool boxed, StringFormat format)
        {
            if (boxed)
                _graphics.DrawString(text, font, brush, new RectangleF(x, y, w, h), format);
            else
                _graphics.DrawString(text, font, brush, x, y, format);
        }

        public SizeF MeasureText(string text, string family, float pixelSize, bool bold, bool italic)
        {
            lock (_sync)
            {
                if (_graphics == null || string.IsNullOrEmpty(text)) return SizeF.Empty;
                return _graphics.MeasureString(text, GetFont(family, pixelSize, bold, italic),
                                               new SizeF(10000f, 10000f), _lineFormat);
            }
        }

        private static readonly StringFormat _lineFormat = MakeFormat(false);
        private static readonly StringFormat _wrapFormat = MakeFormat(true);

        private static StringFormat MakeFormat(bool wrap)
        {
            // Typographic layout matches DirectWrite: no GDI padding around
            // the text, trailing spaces count, no clipping at the box edge.
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
            if (!wrap) format.FormatFlags |= StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.None;
            return format;
        }

        private Font GetFont(string family, float pixelSize, bool bold, bool italic)
        {
            string key = family + "|" + pixelSize.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                         "|" + (bold ? "b" : "n") + (italic ? "i" : "n");
            Font font;
            if (_fonts.TryGetValue(key, out font)) return font;

            FontStyle style = FontStyle.Regular;
            if (bold) style |= FontStyle.Bold;
            if (italic) style |= FontStyle.Italic;

            // Pixels, not points: DirectWrite measures in DIPs, which are
            // pixels for a DPI-aware process. Points would draw 4/3 wider.
            try { font = new Font(family, pixelSize, style, GraphicsUnit.Pixel); }
            catch { font = new Font("Segoe UI", pixelSize, style, GraphicsUnit.Pixel); }
            _fonts[key] = font;
            return font;
        }

        // Both helpers must be called under _sync. The returned object is
        // shared, so it is only valid until the next PenFor/BrushFor call.
        private Pen PenFor(Color color, float width)
        {
            return PenFor(color, width, System.Drawing.Drawing2D.DashStyle.Solid);
        }

        private Pen PenFor(Color color, float width, System.Drawing.Drawing2D.DashStyle dash)
        {
            float w = width < 0.5f ? 0.5f : width;
            if (_pen == null)
            {
                _pen = new Pen(color, w);
                _pen.LineJoin = LineJoin.Round;
                _pen.DashStyle = dash;
                return _pen;
            }
            if (_pen.Color != color) _pen.Color = color;
            if (_pen.Width != w) _pen.Width = w;
            if (_pen.DashStyle != dash) _pen.DashStyle = dash;
            return _pen;
        }

        private SolidBrush BrushFor(Color color)
        {
            if (_solid == null) _solid = new SolidBrush(color);
            else if (_solid.Color != color) _solid.Color = color;
            return _solid;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_pen != null) { try { _pen.Dispose(); } catch { } _pen = null; }
                if (_solid != null) { try { _solid.Dispose(); } catch { } _solid = null; }
                foreach (Font font in _fonts.Values)
                {
                    try { font.Dispose(); }
                    catch { }
                }
                _fonts.Clear();
            }
        }
    }
}
