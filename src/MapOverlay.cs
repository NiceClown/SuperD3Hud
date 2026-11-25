using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>How the map is presented on screen.</summary>
    public enum MapOverlayMode
    {
        /// <summary>Drawn directly on top of the native minimap of the game.</summary>
        Minimap = 0,
        /// <summary>A separate movable panel with the map.</summary>
        Panel = 1
    }

    /// <summary>Settings of the map overlay, stored in config/settings.ini.</summary>
    public class MapSettings
    {
        public bool Enabled { get; set; }
        public bool ShowMinimap { get; set; }
        public bool ShowPanel { get; set; }
        public double Scale { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public int Opacity { get; set; }
        public int PanelX { get; set; }
        public int PanelY { get; set; }
        public int PanelSize { get; set; }
        public bool PanelLocked { get; set; }

        /// <summary>
        /// True once the user changed the map settings themselves. Until then
        /// the launcher may apply new defaults, so an upgrade can switch the
        /// map on without overriding a deliberate choice.
        /// </summary>
        public bool Configured { get; set; }

        /// <summary>Draw the map on the full-screen map that opens with M.</summary>
        public bool ShowLargeMap { get; set; }
        /// <summary>Scale of the large map relative to the native minimap.</summary>
        public double LargeScale { get; set; }
        public double LargeOffsetX { get; set; }
        public double LargeOffsetY { get; set; }
        /// <summary>Follow the game's map key instead of a manual toggle.</summary>
        public bool LargeAuto { get; set; }
        /// <summary>
        /// Fit the whole explored map to the screen instead of matching the
        /// game zoom. When it is off the map follows the hero, exactly like
        /// the native map of the game does.
        /// </summary>
        public bool LargeFit { get; set; }
        /// <summary>
        /// Kept for the settings file: the map follows the hero unless
        /// <see cref="LargeFit"/> is set.
        /// </summary>
        public bool LargeFollow { get; set; }

        public MapSettings()
        {
            // The map works out of the box: a user starts the game and the
            // overlay is already there. Everything below is a setting to
            // change, not something that must be switched on first.
            Enabled = true;
            ShowMinimap = true;
            ShowPanel = false;
            Scale = 1.0;
            OffsetX = 0;
            OffsetY = 0;
            Opacity = 45;
            PanelX = -1;
            PanelY = -1;
            PanelSize = 340;
            PanelLocked = false;

            ShowLargeMap = true;
            // The zoom of the act map of the game, measured against the map of
            // the game itself: the corners of our geometry have to fall on the
            // outline the game has drawn. The scale is taken from the height
            // of the window, so the picture is the same on every computer.
            LargeScale = 1.0;
            // The panel of the map of the game is in the middle of the screen.
            LargeOffsetX = 0;
            LargeOffsetY = 0;
            LargeAuto = true;
            // Follow the player by default: a static "whole map" picture looks
            // frozen, while tracking is what a player expects from a map.
            LargeFit = false;
            LargeFollow = true;
        }

        public MapSettings Clone()
        {
            return new MapSettings
            {
                Enabled = Enabled,
                ShowMinimap = ShowMinimap,
                ShowPanel = ShowPanel,
                Scale = Scale,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                Opacity = Opacity,
                PanelX = PanelX,
                PanelY = PanelY,
                PanelSize = PanelSize,
                PanelLocked = PanelLocked,
                Configured = Configured,
                ShowLargeMap = ShowLargeMap,
                LargeScale = LargeScale,
                LargeOffsetX = LargeOffsetX,
                LargeOffsetY = LargeOffsetY,
                LargeAuto = LargeAuto,
                LargeFit = LargeFit,
                LargeFollow = LargeFollow
            };
        }
    }

    /// <summary>
    /// The window that draws the map.
    ///
    /// The overlay is a layered window that is composited by the window
    /// manager itself, so it can have a transparent background and still
    /// show semi-transparent geometry. It never takes the focus and, unless
    /// the user asks for it, it does not consume mouse clicks either.
    /// </summary>
    public class MapOverlay : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int GWL_EXSTYLE = -20;
        private const int ULW_ALPHA = 0x00000002;
        private const int AC_SRC_ALPHA = 0x00000001;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;

        private const int DIB_RGB_COLORS = 0;

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, int uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Cx, Cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        private MapSettings _settings = new MapSettings();
        private List<MapPolygon> _polygons = new List<MapPolygon>();
        private List<Game.MonsterMarker> _monsters = new List<Game.MonsterMarker>();
        private List<Game.LootMarker> _loot = new List<Game.LootMarker>();
        private List<Game.InteractiveMarker> _interactive = new List<Game.InteractiveMarker>();
        private MapPoint _player;
        private bool _hasPlayer;
        private string _status = "";
        private readonly bool _largeMode;
        private readonly Stopwatch _animWatch = Stopwatch.StartNew();
        private double _lastAnimRedrawSec = -1.0;

        // Cached world-space rasterized silhouette bitmaps (separate per view mode).
        private readonly SilhouetteCache _minimapCache = new SilhouetteCache();
        private readonly SilhouetteCache _largeCache = new SilhouetteCache();
        private readonly SilhouetteCache _panelCache = new SilhouetteCache();

        // Reusable minimap compositing buffer + precomputed rounded vignette edge mask.
        private Bitmap _minimapBuffer;
        private int _minimapBufferW;
        private int _minimapBufferH;
        private int[] _minimapPixels;
        private int[] _vignetteIndices;
        private byte[] _vignetteFactors;
        private int _vignetteCount;
        private readonly List<RectangleF> _placedLabels = new List<RectangleF>();

        // Track rendered settings so slider changes repaint immediately even when static.
        private int _renderedOpacity = -1;
        private double _renderedScale = -1;
        private double _renderedLargeScale = -1;
        private double _renderedOffsetX;
        private double _renderedOffsetY;
        private double _renderedLargeOffsetX;
        private double _renderedLargeOffsetY;
        private bool _renderedLargeFit;
        private bool _renderedShowMinimap;
        private bool _renderedShowPanel;
        private bool _renderedShowLargeMap;

        /// <summary>
        /// Creates the overlay. When <paramref name="largeMode"/> is true the
        /// window covers the whole game client area and draws the map of the
        /// full-screen map instead of the minimap.
        /// </summary>
        public MapOverlay(MapSettings settings)
            : this(settings, false)
        {
        }

        public MapOverlay(MapSettings settings, bool largeMode)
        {
            if (settings != null) _settings = settings;
            _largeMode = largeMode;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(360, 360);
            BackColor = Color.Black;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (_largeMode || _settings.PanelLocked) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The layered window supplies the background itself.
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        /// <summary>Raised after the user moved the panel with the mouse.</summary>
        public event EventHandler Moved;

        /// <summary>
        /// Lets the user drag the standalone panel by its surface. Dragging is
        /// only possible when click-through is off, otherwise the window never
        /// sees the mouse at all.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT &&
                !_largeMode && _settings.ShowPanel && !_settings.PanelLocked)
            {
                m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            EventHandler handler = Moved;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch { }
            }
        }

        /// <summary>
        /// Updates the geometry to draw.
        ///
        /// A repaint is skipped when nothing changed: the same list, the same
        /// player position and no animated marker. That keeps a full-screen
        /// overlay cheap while still following the player at the timer rate.
        /// </summary>
        public void UpdateContent(List<MapPolygon> polygons, MapPoint player, bool hasPlayer, string status)
        {
            List<MapPolygon> nextPolygons = polygons ?? new List<MapPolygon>();
            string nextStatus = status ?? "";

            bool sameList = ReferenceEquals(_polygons, nextPolygons) && _polygons.Count == nextPolygons.Count;
            bool samePlayer = _hasPlayer == hasPlayer
                && Math.Abs(_player.X - player.X) < 0.02
                && Math.Abs(_player.Y - player.Y) < 0.02;
            bool sameStatus = _status == nextStatus;
            bool sameSettings = _renderedOpacity == _settings.Opacity
                && Math.Abs(_renderedScale - _settings.Scale) < 1e-6
                && Math.Abs(_renderedLargeScale - _settings.LargeScale) < 1e-6
                && Math.Abs(_renderedOffsetX - _settings.OffsetX) < 1e-6
                && Math.Abs(_renderedOffsetY - _settings.OffsetY) < 1e-6
                && Math.Abs(_renderedLargeOffsetX - _settings.LargeOffsetX) < 1e-6
                && Math.Abs(_renderedLargeOffsetY - _settings.LargeOffsetY) < 1e-6
                && _renderedLargeFit == _settings.LargeFit
                && _renderedShowMinimap == _settings.ShowMinimap
                && _renderedShowPanel == _settings.ShowPanel
                && _renderedShowLargeMap == _settings.ShowLargeMap;

            _polygons = nextPolygons;
            _player = player;
            _hasPlayer = hasPlayer;
            _status = nextStatus;

            bool markersChanged = _markersDirty;
            _markersDirty = false;
            if (sameList && samePlayer && sameStatus && sameSettings && !markersChanged && !AnimationDue())
                return;

            _renderedOpacity = _settings.Opacity;
            _renderedScale = _settings.Scale;
            _renderedLargeScale = _settings.LargeScale;
            _renderedOffsetX = _settings.OffsetX;
            _renderedOffsetY = _settings.OffsetY;
            _renderedLargeOffsetX = _settings.LargeOffsetX;
            _renderedLargeOffsetY = _settings.LargeOffsetY;
            _renderedLargeFit = _settings.LargeFit;
            _renderedShowMinimap = _settings.ShowMinimap;
            _renderedShowPanel = _settings.ShowPanel;
            _renderedShowLargeMap = _settings.ShowLargeMap;

            Redraw();
        }

        /// <summary>True when animated markers or active hero state require a 60 Hz frame.</summary>
        private bool AnimationDue()
        {
            if (_loot.Count == 0 && _interactive.Count == 0 && _monsters.Count == 0 && !_hasPlayer)
                return false;

            double nowSec = _animWatch.Elapsed.TotalSeconds;
            if (_lastAnimRedrawSec >= 0.0 && (nowSec - _lastAnimRedrawSec) * 1000.0 < 13.5)
                return false;
            _lastAnimRedrawSec = nowSec;
            return true;
        }

        /// <summary>
        /// Sets the monster and loot markers drawn on top of the map.
        /// The lists are copied, so the caller may reuse its own arrays.
        /// </summary>
        private bool _markersDirty;

        // Exponentially smoothed marker display coordinates, updated each frame via Stopwatch.
        private const double MarkerSmoothTauSec = 0.055;
        private const float MarkerSnapDistSq = 35f * 35f;
        private readonly Dictionary<int, PointF> _monsterDisplay = new Dictionary<int, PointF>();
        private readonly Dictionary<int, PointF> _lootDisplay = new Dictionary<int, PointF>();
        private double _lastMarkerSmoothSec = -1.0;

        public void SetMarkers(List<Game.MonsterMarker> monsters, List<Game.LootMarker> loot)
        {
            List<Game.MonsterMarker> m = monsters ?? new List<Game.MonsterMarker>();
            List<Game.LootMarker> l = loot ?? new List<Game.LootMarker>();
            // A new list instance means a new poll: the dots must be redrawn
            // even when the player stands still, otherwise dead monsters stay.
            if (!ReferenceEquals(m, _monsters) || !ReferenceEquals(l, _loot))
            {
                PruneMonsters(m);
                PruneLoot(l);
                _markersDirty = true;
            }
            _monsters = m;
            _loot = l;
        }

        private void PruneMonsters(List<Game.MonsterMarker> list)
        {
            if (_monsterDisplay.Count == 0) return;
            if (list.Count == 0) { _monsterDisplay.Clear(); return; }
            HashSet<int> alive = new HashSet<int>();
            for (int i = 0; i < list.Count; i++) alive.Add(list[i].Id);
            List<int> stale = null;
            foreach (int key in _monsterDisplay.Keys)
            {
                if (!alive.Contains(key))
                {
                    if (stale == null) stale = new List<int>();
                    stale.Add(key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++) _monsterDisplay.Remove(stale[i]);
            }
        }

        private void PruneLoot(List<Game.LootMarker> list)
        {
            if (_lootDisplay.Count == 0) return;
            if (list.Count == 0) { _lootDisplay.Clear(); return; }
            HashSet<int> alive = new HashSet<int>();
            for (int i = 0; i < list.Count; i++) alive.Add(list[i].Id);
            List<int> stale = null;
            foreach (int key in _lootDisplay.Keys)
            {
                if (!alive.Contains(key))
                {
                    if (stale == null) stale = new List<int>();
                    stale.Add(key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++) _lootDisplay.Remove(stale[i]);
            }
        }

        private void AdvanceMarkerSmoothing()
        {
            double nowSec = _animWatch.Elapsed.TotalSeconds;
            double dt = _lastMarkerSmoothSec >= 0.0 ? (nowSec - _lastMarkerSmoothSec) : 0.0;
            _lastMarkerSmoothSec = nowSec;

            double alpha = (dt > 0.0 && dt <= 0.5)
                ? (1.0 - Math.Exp(-dt / MarkerSmoothTauSec))
                : 1.0;
            if (alpha > 1.0) alpha = 1.0;

            for (int i = 0; i < _monsters.Count; i++)
            {
                Game.MonsterMarker m = _monsters[i];
                StepDisplayPoint(_monsterDisplay, m.Id, m.X, m.Y, alpha);
            }
            for (int i = 0; i < _loot.Count; i++)
            {
                Game.LootMarker item = _loot[i];
                StepDisplayPoint(_lootDisplay, item.Id, item.X, item.Y, alpha);
            }
        }

        private static void StepDisplayPoint(Dictionary<int, PointF> map, int id,
                                             float targetX, float targetY, double alpha)
        {
            PointF cur;
            if (!map.TryGetValue(id, out cur))
            {
                map[id] = new PointF(targetX, targetY);
                return;
            }
            float dx = targetX - cur.X;
            float dy = targetY - cur.Y;
            float distSq = dx * dx + dy * dy;
            if (distSq > MarkerSnapDistSq || alpha >= 1.0 || distSq < 1e-4f)
            {
                map[id] = new PointF(targetX, targetY);
                return;
            }
            map[id] = new PointF(cur.X + (float)(dx * alpha), cur.Y + (float)(dy * alpha));
        }

        private float SmoothMonsterX(Game.MonsterMarker monster)
        {
            PointF p;
            return _monsterDisplay.TryGetValue(monster.Id, out p) ? p.X : monster.X;
        }

        private float SmoothMonsterY(Game.MonsterMarker monster)
        {
            PointF p;
            return _monsterDisplay.TryGetValue(monster.Id, out p) ? p.Y : monster.Y;
        }

        private float SmoothLootX(Game.LootMarker item)
        {
            PointF p;
            return _lootDisplay.TryGetValue(item.Id, out p) ? p.X : item.X;
        }

        private float SmoothLootY(Game.LootMarker item)
        {
            PointF p;
            return _lootDisplay.TryGetValue(item.Id, out p) ? p.Y : item.Y;
        }

        /// <summary>
        /// Renders the current content and pushes it to the layered window.
        /// Safe to call from the UI thread only.
        /// </summary>
        private void Redraw()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0) return;
            if (!IsHandleCreated) return;

            try
            {
                if (!EnsureSurface(width, height)) return;

                _lastAnimRedrawSec = _animWatch.Elapsed.TotalSeconds;
                using (Graphics g = Graphics.FromImage(_surface))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Render(g, width, height);
                }

                if (!PushLayered(width, height))
                    LastError = "Не удалось вывести наложение на экран.";
                else
                    LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        // ------------------------------------------------------------------
        // Layered window surface
        // ------------------------------------------------------------------

        private IntPtr _memDc;
        private IntPtr _dib;
        private IntPtr _bits;
        private IntPtr _oldBitmap;
        private Bitmap _surface;
        private int _surfaceWidth;
        private int _surfaceHeight;

        /// <summary>
        /// Creates the surface the overlay draws into. The bitmap points at the
        /// memory of the layered window, so a frame costs one drawing pass
        /// instead of a copy of the whole screen.
        /// </summary>
        private bool EnsureSurface(int width, int height)
        {
            if (_surface != null && _surfaceWidth == width && _surfaceHeight == height)
                return true;

            ReleaseSurface();

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return false;
            try
            {
                _memDc = CreateCompatibleDC(screenDc);
                if (_memDc == IntPtr.Zero) return false;

                BITMAPINFO info = new BITMAPINFO();
                info.Size = Marshal.SizeOf(typeof(BITMAPINFO));
                info.Width = width;
                info.Height = -height;      // top-down
                info.Planes = 1;
                info.BitCount = 32;
                info.Compression = 0;       // BI_RGB
                info.SizeImage = width * height * 4;

                _dib = CreateDIBSection(_memDc, ref info, DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero || _bits == IntPtr.Zero)
                {
                    ReleaseSurface();
                    return false;
                }

                _oldBitmap = SelectObject(_memDc, _dib);

                // GDI+ writes premultiplied alpha straight into the window
                // memory, which is what UpdateLayeredWindow expects.
                _surface = new Bitmap(width, height, width * 4,
                                      PixelFormat.Format32bppPArgb, _bits);
                _surfaceWidth = width;
                _surfaceHeight = height;
                return true;
            }
            catch
            {
                ReleaseSurface();
                return false;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void ReleaseSurface()
        {
            if (_surface != null)
            {
                try { _surface.Dispose(); }
                catch { }
                _surface = null;
            }
            if (_oldBitmap != IntPtr.Zero && _memDc != IntPtr.Zero)
            {
                try { SelectObject(_memDc, _oldBitmap); }
                catch { }
                _oldBitmap = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero)
            {
                try { DeleteObject(_dib); }
                catch { }
                _dib = IntPtr.Zero;
            }
            if (_memDc != IntPtr.Zero)
            {
                try { DeleteDC(_memDc); }
                catch { }
                _memDc = IntPtr.Zero;
            }
            _bits = IntPtr.Zero;
            _surfaceWidth = 0;
            _surfaceHeight = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseSurface();
                ReleaseMinimapBuffer();
                _minimapCache.Release();
                _largeCache.Release();
                _panelCache.Release();
            }
            base.Dispose(disposing);
        }

        /// <summary>Last drawing error, empty when the last frame was shown.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Enables or disables the click-through behaviour at run time.
        /// </summary>
        public void ApplyClickThrough(bool clickThrough)
        {
            try
            {
                if (!IsHandleCreated) return;
                IntPtr style = GetExStyle(Handle);
                long value = style.ToInt64();
                if (clickThrough) value |= WS_EX_TRANSPARENT;
                else value &= ~WS_EX_TRANSPARENT;
                SetExStyle(Handle, new IntPtr(value));
            }
            catch { }
        }

        private static IntPtr GetExStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, GWL_EXSTYLE);
            return new IntPtr(GetWindowLong32(hWnd, GWL_EXSTYLE));
        }

        private static void SetExStyle(IntPtr hWnd, IntPtr value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, GWL_EXSTYLE, value);
            else SetWindowLong32(hWnd, GWL_EXSTYLE, value.ToInt32());
        }

        /// <summary>
        /// Computes the client area of the native minimap in screen pixels.
        /// Returns false when the game window is not available.
        /// </summary>
        public static bool TryGetMinimapRect(out Rectangle screenRect)
        {
            screenRect = Rectangle.Empty;

            IntPtr hwnd;
            if (!InputSender.TryGetGameWindow(out hwnd)) return false;

            try
            {
                RECT rect;
                if (!GetClientRect(hwnd, out rect)) return false;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return false;

                POINT origin = new POINT();
                origin.X = 0;
                origin.Y = 0;
                if (!ClientToScreen(hwnd, ref origin)) return false;

                // The native minimap is a 350 unit square anchored to the top
                // right corner of the game client area.
                double scale = height / 1200.0;
                double size = 350.0 * scale;
                double left = origin.X + width - 1 - 377.0 * scale;
                double top = origin.Y + 54.0 * scale;

                screenRect = new Rectangle(
                    (int)Math.Round(left),
                    (int)Math.Round(top),
                    (int)Math.Round(size),
                    (int)Math.Round(size));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// A layered window paints through its own surface, so the normal
        /// paint cycle only re-pushes the last state. This also covers the
        /// case where the window is resized or restored by the system.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            Redraw();
        }

        /// <summary>
        /// Renders the current content into a bitmap. Used by tests and by the
        /// offscreen preview, so the drawing path can be verified without a
        /// visible window.
        /// </summary>
        public Bitmap RenderToBitmap(int width, int height)
        {
            Bitmap surface = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(surface))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Render(g, width, height);
            }
            return surface;
        }

        /// <summary>
        /// Computes a scale and offset so the whole known map fits the given
        /// client area. This does not depend on the zoom of the game's own map,
        /// so it always produces a readable picture.
        /// </summary>
        public static void FitToScreen(List<MapPolygon> polygons, int width, int height,
                                       double margin, out double scale,
                                       out double offsetX, out double offsetY)
        {
            scale = 1.0;
            offsetX = 0;
            offsetY = 0;
            if (polygons == null || width <= 0 || height <= 0) return;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (MapPolygon polygon in polygons)
            {
                if (polygon == null || polygon.CachedFootprint || polygon.Approximate || polygon.Points == null)
                    continue;
                foreach (MapPoint p in polygon.Points)
                {
                    MapPoint n = MapGeometry.ToNative(p);
                    any = true;
                    if (n.X < minX) minX = n.X;
                    if (n.Y < minY) minY = n.Y;
                    if (n.X > maxX) maxX = n.X;
                    if (n.Y > maxY) maxY = n.Y;
                }
            }
            if (!any) return;

            double extentX = maxX - minX;
            double extentY = maxY - minY;
            if (extentX <= 0.0001 || extentY <= 0.0001) return;

            double usableW = Math.Max(50.0, width - margin * 2);
            double usableH = Math.Max(50.0, height - margin * 2);
            double baseScale = height / 1200.0;
            if (baseScale <= 0.0001) return;

            scale = Math.Min(usableW / extentX, usableH / extentY) / baseScale;

            // Centre the content in the client area, in the same units the
            // drawing code uses: origin = width/2 + offset.
            double centreX = (minX + maxX) / 2.0;
            double centreY = (minY + maxY) / 2.0;
            offsetX = -centreX * scale * baseScale;
            offsetY = -centreY * scale * baseScale;
        }

        /// <summary>
        /// Draws the map into the given surface.
        /// </summary>
        private void Render(Graphics g, int width, int height)
        {
            AdvanceMarkerSmoothing();

            if (_largeMode)
            {
                DrawLargeMap(g, width, height);
                return;
            }

            if (_polygons.Count == 0)
            {
                if (_status.Length > 0 && _settings.ShowPanel)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(205, 16, 12, 10)))
                        g.FillRectangle(b, 4, 4, width - 8, 24);
                    using (Pen bp = new Pen(Color.FromArgb(190, 168, 132, 66), 1f))
                        g.DrawRectangle(bp, 4, 4, width - 8, 24);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 240, 232, 216)))
                        g.DrawString(_status, Font, b, 8, 8);
                }
                return;
            }

            if (_settings.ShowMinimap)
                DrawMinimap(g, width, height);
            if (_settings.ShowPanel)
                DrawPanel(g, width, height);
        }

        /// <summary>
        /// Draws the map on the full-screen map of the game.
        /// </summary>
        private void DrawLargeMap(Graphics g, int width, int height)
        {
            if (_polygons.Count == 0) return;

            double scale;
            double originX;
            double originY;

            if (_settings.LargeFit)
            {
                double fitScale, fitOffsetX, fitOffsetY;
                MapOverlay.FitToScreen(_polygons, width, height, 60, out fitScale, out fitOffsetX, out fitOffsetY);
                scale = fitScale * height / 1200.0;
                originX = width / 2.0 + fitOffsetX;
                originY = height / 2.0 + fitOffsetY;
            }
            else
            {
                if (!_hasPlayer) return;
                double unit = height / 1200.0;
                scale = unit * _settings.LargeScale;
                if (scale <= 0.0001) return;
                MapPoint center = MapGeometry.ToNative(MapGeometry.Project(_player.X, _player.Y));
                originX = width / 2.0 + _settings.LargeOffsetX * unit - center.X * scale;
                originY = height / 2.0 + _settings.LargeOffsetY * unit - center.Y * scale;
            }

            SilhouetteCache cache = GetOrBuildSilhouette(_largeCache, scale, true, false);
            if (cache != null && cache.Bitmap != null)
            {
                DrawSilhouette(g, cache, originX, originY);

                if (_hasPlayer)
                {
                    MapPoint me = MapGeometry.ToNative(MapGeometry.Project(_player.X, _player.Y));
                    float px = (float)(originX + me.X * scale);
                    float py = (float)(originY + me.Y * scale);
                    DrawPlayerMarker(g, px, py, 6.2f, false);
                }
            }

            Func<float, float, PointF> projectLarge = delegate(float wx, float wy)
            {
                MapPoint p = MapGeometry.ToNative(MapGeometry.Project(wx, wy));
                return new PointF((float)(originX + p.X * scale), (float)(originY + p.Y * scale));
            };
            float uiScale = Math.Max(0.85f, Math.Min(1.35f, height / 1080f));
            _placedLabels.Clear();
            DrawMarkers(g, projectLarge, 4.2f * uiScale, 1.08f * uiScale);
            DrawInteractive(g, projectLarge, 6.0f * uiScale, 1.08f * uiScale);
        }

        /// <summary>
        /// Draws the objects that can be used: the exit of the rift and the
        /// pylons of a greater rift.
        /// </summary>
        private void DrawInteractive(Graphics g, Func<float, float, PointF> project,
                                     float radius, float labelScale)
        {
            if (_interactive == null || _interactive.Count == 0) return;

            double elapsed = _animWatch.Elapsed.TotalSeconds;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(elapsed * Math.PI * 2.0 * 1.15);
            float pulse2 = 0.5f + 0.5f * (float)Math.Sin(elapsed * Math.PI * 2.0 * 1.15 + Math.PI);

            Font font = new Font("Segoe UI", Math.Max(7f, radius * 1.05f), FontStyle.Bold);
            try
            {
                foreach (Game.InteractiveMarker marker in _interactive)
                {
                    PointF p = project(marker.X, marker.Y);
                    bool exit = marker.Kind == "exit";

                    if (exit)
                    {
                        float rOuter = radius * (1.0f + 0.38f * pulse);
                        float rInner = radius * (0.58f + 0.24f * pulse2);

                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(40, 255, 206, 78)))
                            g.FillEllipse(glow, p.X - rOuter, p.Y - rOuter, rOuter * 2f, rOuter * 2f);
                        using (Pen outerRing = new Pen(Color.FromArgb((int)(215 * (0.45f + 0.55f * (1f - pulse))), 255, 208, 82), 1.8f))
                            g.DrawEllipse(outerRing, p.X - rOuter, p.Y - rOuter, rOuter * 2f, rOuter * 2f);
                        using (Pen innerRing = new Pen(Color.FromArgb(230, 255, 232, 136), 1.5f))
                            g.DrawEllipse(innerRing, p.X - rInner, p.Y - rInner, rInner * 2f, rInner * 2f);
                        using (SolidBrush rim = new SolidBrush(Color.FromArgb(225, 14, 10, 7)))
                            g.FillEllipse(rim, p.X - radius * 0.36f, p.Y - radius * 0.36f, radius * 0.72f, radius * 0.72f);
                        using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 255, 226, 118)))
                            g.FillEllipse(dot, p.X - radius * 0.24f, p.Y - radius * 0.24f, radius * 0.48f, radius * 0.48f);
                    }
                    else
                    {
                        float dw = radius * 0.76f;
                        float dh = radius * 1.02f;
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(52, 255, 212, 92)))
                            g.FillEllipse(glow, p.X - radius * 1.15f, p.Y - radius * 1.15f, radius * 2.3f, radius * 2.3f);

                        PointF[] diamond = new PointF[]
                        {
                            new PointF(p.X, p.Y - dh),
                            new PointF(p.X + dw, p.Y),
                            new PointF(p.X, p.Y + dh),
                            new PointF(p.X - dw, p.Y)
                        };
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(245, 252, 208, 78)))
                            g.FillPolygon(fill, diamond);
                        using (Pen rim = new Pen(Color.FromArgb(230, 14, 10, 7), 1.5f))
                            g.DrawPolygon(rim, diamond);

                        PointF[] core = new PointF[]
                        {
                            new PointF(p.X, p.Y - dh * 0.45f),
                            new PointF(p.X + dw * 0.45f, p.Y),
                            new PointF(p.X, p.Y + dh * 0.45f),
                            new PointF(p.X - dw * 0.45f, p.Y)
                        };
                        using (SolidBrush coreBrush = new SolidBrush(Color.FromArgb(245, 255, 246, 198)))
                            g.FillPolygon(coreBrush, core);
                    }

                    // Only the exit gets a name: the runes of an act are many,
                    // and a label on every one of them covers the map.
                    if (!exit || string.IsNullOrEmpty(marker.Name)) continue;

                    DrawClampedLabel(
                        g, marker.Name, font,
                        p.X, p.Y - radius * 1.25f - 2f, p.Y + radius * 1.25f + 2f,
                        Color.FromArgb(215, 16, 12, 10),
                        Color.FromArgb(190, 188, 146, 72),
                        Color.FromArgb(255, 255, 224, 124),
                        _placedLabels);
                }
            }
            finally
            {
                font.Dispose();
            }
        }

        /// <summary>
        /// The objects that can be used: the exit of the rift and the pylons.
        /// They are drawn with a pulsing ring and a name.
        /// </summary>
        public void SetInteractive(List<Game.InteractiveMarker> markers)
        {
            _interactive = markers ?? new List<Game.InteractiveMarker>();
            _markersDirty = true;
        }

        /// <summary>
        /// Draws the geometry aligned to the native minimap of the game with a
        /// cached world-space silhouette and a soft rounded outer vignette fade.
        /// </summary>
        private void DrawMinimap(Graphics g, int width, int height)
        {
            if (!_hasPlayer) return;

            double scale = width / 350.0 * _settings.Scale;
            if (scale <= 0.0001) return;
            MapPoint center = MapGeometry.ToNative(MapGeometry.Project(_player.X, _player.Y));
            double offsetX = _settings.OffsetX * width / 350.0;
            double offsetY = _settings.OffsetY * width / 350.0;
            double shiftX = width / 2.0 + offsetX - center.X * scale;
            double shiftY = height / 2.0 + offsetY - center.Y * scale;

            SilhouetteCache cache = GetOrBuildSilhouette(_minimapCache, scale, true, true);
            if (cache != null && cache.Bitmap != null)
            {
                if (EnsureMinimapBuffer(width, height))
                {
                    using (Graphics mg = Graphics.FromImage(_minimapBuffer))
                    {
                        mg.Clear(Color.Transparent);
                        DrawSilhouette(mg, cache, shiftX, shiftY);
                    }
                    ApplyMinimapVignette();
                    g.DrawImageUnscaled(_minimapBuffer, 0, 0);
                }
                else
                {
                    DrawSilhouette(g, cache, shiftX, shiftY);
                }

                float px = (float)(width / 2.0 + offsetX);
                float py = (float)(height / 2.0 + offsetY);
                float playerSize = Math.Max(4.2f, width * 0.0145f);
                DrawPlayerMarker(g, px, py, playerSize, true);
            }

            Func<float, float, PointF> project = delegate(float wx, float wy)
            {
                MapPoint p = MapGeometry.ToNative(MapGeometry.Project(wx, wy));
                return new PointF(
                    (float)(shiftX + p.X * scale),
                    (float)(shiftY + p.Y * scale));
            };
            _placedLabels.Clear();
            DrawMarkers(g, project, 2.6f, 0.85f);
            DrawInteractive(g, project, 3.4f, 0.9f);
        }

        /// <summary>
        /// Draws the hero marker: a gilded Diablo diamond with a smooth 60 FPS
        /// breathing gold halo. On the minimap the diamond stays translucent so
        /// the game's native facing arrow stays readable inside it.
        /// </summary>
        private void DrawPlayerMarker(Graphics g, float px, float py, float size, bool subtleMinimap)
        {
            double elapsed = _animWatch.Elapsed.TotalSeconds;
            float breathe = 0.5f + 0.5f * (float)Math.Sin(elapsed * Math.PI * 2.0 * 0.85);

            float outerR = size * (2.05f + 0.35f * breathe);
            float innerR = size * (1.35f + 0.18f * breathe);
            using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(34 + 28 * breathe), 235, 185, 72)))
                g.FillEllipse(halo, px - outerR, py - outerR, outerR * 2f, outerR * 2f);
            using (Pen haloRing = new Pen(Color.FromArgb((int)(85 + 55 * breathe), 255, 220, 125), 1.2f))
                g.DrawEllipse(haloRing, px - innerR, py - innerR, innerR * 2f, innerR * 2f);

            float hw = size * 0.85f;
            float hh = size * 1.15f;
            PointF[] diamond = new PointF[]
            {
                new PointF(px, py - hh),
                new PointF(px + hw, py),
                new PointF(px, py + hh),
                new PointF(px - hw, py)
            };

            if (subtleMinimap)
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(120, 240, 198, 96)))
                    g.FillPolygon(fill, diamond);
                using (Pen rim = new Pen(Color.FromArgb(205, 14, 10, 7), 1.5f))
                    g.DrawPolygon(rim, diamond);
                using (Pen bevel = new Pen(Color.FromArgb(230, 255, 232, 145), 1.0f))
                    g.DrawPolygon(bevel, diamond);
                return;
            }

            PointF[] outerRim = new PointF[]
            {
                new PointF(px, py - hh * 1.18f),
                new PointF(px + hw * 1.18f, py),
                new PointF(px, py + hh * 1.18f),
                new PointF(px - hw * 1.18f, py)
            };
            using (SolidBrush rimBrush = new SolidBrush(Color.FromArgb(230, 14, 10, 7)))
                g.FillPolygon(rimBrush, outerRim);
            using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(245, 236, 184, 72)))
                g.FillPolygon(bodyBrush, diamond);
            using (Pen bevelPen = new Pen(Color.FromArgb(240, 255, 236, 165), 1.0f))
                g.DrawPolygon(bevelPen, diamond);

            PointF[] core = new PointF[]
            {
                new PointF(px, py - hh * 0.42f),
                new PointF(px + hw * 0.42f, py),
                new PointF(px, py + hh * 0.42f),
                new PointF(px - hw * 0.42f, py)
            };
            using (SolidBrush coreBrush = new SolidBrush(Color.FromArgb(250, 255, 246, 205)))
                g.FillPolygon(coreBrush, core);
        }

        private static void DrawSilhouette(Graphics g, SilhouetteCache cache, double shiftX, double shiftY)
        {
            if (cache == null || cache.Bitmap == null) return;
            InterpolationMode prevInterp = g.InterpolationMode;
            g.InterpolationMode = cache.CacheRatio > 1.05
                ? InterpolationMode.HighQualityBilinear
                : InterpolationMode.Bilinear;
            try
            {
                float dx = (float)(shiftX + cache.OriginX);
                float dy = (float)(shiftY + cache.OriginY);
                float dw = (float)(cache.Bitmap.Width / cache.CacheRatio);
                float dh = (float)(cache.Bitmap.Height / cache.CacheRatio);
                g.DrawImage(cache.Bitmap, dx, dy, dw, dh);
            }
            finally
            {
                g.InterpolationMode = prevInterp;
            }
        }

        private SilhouetteCache GetOrBuildSilhouette(SilhouetteCache cache, double scale,
                                                     bool useNative, bool isMinimap)
        {
            int opacity = Clamp(_settings.Opacity, 0, 255);
            if (cache.Matches(_polygons, scale, opacity, useNative, isMinimap))
                return cache;

            cache.Release();
            cache.Polygons = _polygons;
            cache.PolygonCount = _polygons.Count;
            cache.Scale = scale;
            cache.Opacity = opacity;
            cache.UseNative = useNative;
            cache.IsMinimap = isMinimap;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            int validCount = 0;

            for (int i = 0; i < _polygons.Count; i++)
            {
                MapPolygon polygon = _polygons[i];
                if (polygon == null || polygon.CachedFootprint || polygon.Approximate) continue;
                if (polygon.Points == null || polygon.Points.Length < 3) continue;

                validCount++;
                for (int j = 0; j < polygon.Points.Length; j++)
                {
                    MapPoint p = useNative ? MapGeometry.ToNative(polygon.Points[j]) : polygon.Points[j];
                    double px = p.X * scale;
                    double py = p.Y * scale;
                    if (px < minX) minX = px;
                    if (py < minY) minY = py;
                    if (px > maxX) maxX = px;
                    if (py > maxY) maxY = py;
                }
            }

            if (validCount == 0 || maxX <= minX || maxY <= minY)
                return cache;

            const int MaxCacheDim = 2048;
            const int SupersampleMaxDim = 1536;
            double extentW = maxX - minX;
            double extentH = maxY - minY;

            // Supersample up to 2x when the silhouette fits comfortably so 45-deg
            // and 26.5-deg diagonal gold walls downsample cleanly without stair-stepping.
            double cacheRatio = 1.0;
            if (extentW * 2.0 <= SupersampleMaxDim - 24 && extentH * 2.0 <= SupersampleMaxDim - 24)
            {
                cacheRatio = 2.0;
            }
            else if (extentW > MaxCacheDim - 16 || extentH > MaxCacheDim - 16)
            {
                double avail = MaxCacheDim - 16;
                cacheRatio = Math.Min(avail / extentW, avail / extentH);
            }

            double aaScale = Math.Max(1.0, cacheRatio);
            int wallR = (int)Math.Round(2.0 * aaScale);
            int shadowCoreR = wallR + (int)Math.Round(1.0 * aaScale);
            int shadowSoftR = wallR + (int)Math.Round(1.6 * aaScale);
            int wallD2Max = wallR * wallR;
            int shadowCoreD2Max = shadowCoreR * shadowCoreR;
            int shadowSoftD2Max = shadowSoftR * shadowSoftR;
            byte clampDist = (byte)(shadowSoftR + 1);
            int pad = shadowSoftR + 2;

            int bmpW = Math.Max(1, (int)Math.Ceiling(extentW * cacheRatio) + pad * 2);
            int bmpH = Math.Max(1, (int)Math.Ceiling(extentH * cacheRatio) + pad * 2);
            if (bmpW > MaxCacheDim) bmpW = MaxCacheDim;
            if (bmpH > MaxCacheDim) bmpH = MaxCacheDim;

            double originX = minX - pad / cacheRatio;
            double originY = minY - pad / cacheRatio;
            cache.OriginX = originX;
            cache.OriginY = originY;
            cache.CacheRatio = cacheRatio;

            int pixelCount = bmpW * bmpH;
            int[] raw = new int[pixelCount];
            using (Bitmap maskBmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb))
            {
                using (Graphics mg = Graphics.FromImage(maskBmp))
                {
                    mg.Clear(Color.Transparent);
                    mg.SmoothingMode = SmoothingMode.None;

                    using (SolidBrush solid = new SolidBrush(Color.White))
                    using (Pen seamPen = new Pen(Color.White, 1.2f * (float)aaScale))
                    {
                        seamPen.LineJoin = LineJoin.Miter;
                        PointF[] quadBuf = new PointF[4];
                        for (int i = 0; i < _polygons.Count; i++)
                        {
                            MapPolygon polygon = _polygons[i];
                            if (polygon == null || polygon.CachedFootprint || polygon.Approximate) continue;
                            if (polygon.Points == null || polygon.Points.Length < 3) continue;

                            PointF[] pts = polygon.Points.Length == 4 ? quadBuf : new PointF[polygon.Points.Length];
                            for (int j = 0; j < polygon.Points.Length; j++)
                            {
                                MapPoint p = useNative ? MapGeometry.ToNative(polygon.Points[j]) : polygon.Points[j];
                                pts[j] = new PointF(
                                    (float)((p.X * scale - minX) * cacheRatio + pad),
                                    (float)((p.Y * scale - minY) * cacheRatio + pad));
                            }
                            mg.FillPolygon(solid, pts);
                            mg.DrawPolygon(seamPen, pts);
                        }
                    }
                }

                BitmapData maskData = maskBmp.LockBits(
                    new Rectangle(0, 0, bmpW, bmpH),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    if (maskData.Stride == bmpW * 4)
                    {
                        Marshal.Copy(maskData.Scan0, raw, 0, pixelCount);
                    }
                    else
                    {
                        for (int y = 0; y < bmpH; y++)
                            Marshal.Copy(IntPtr.Add(maskData.Scan0, y * maskData.Stride), raw, y * bmpW, bmpW);
                    }
                }
                finally
                {
                    maskBmp.UnlockBits(maskData);
                }
            }

            byte[] mask = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                if (((uint)raw[i] >> 24) >= 128) mask[i] = 1;
            }
            for (int y = 1; y < bmpH - 1; y++)
            {
                int row = y * bmpW;
                for (int x = 1; x < bmpW - 1; x++)
                {
                    int idx = row + x;
                    if (mask[idx] != 0) continue;
                    bool lr = ((uint)raw[idx - 1] >> 24) >= 128 && ((uint)raw[idx + 1] >> 24) >= 128;
                    bool ud = ((uint)raw[idx - bmpW] >> 24) >= 128 && ((uint)raw[idx + bmpW] >> 24) >= 128;
                    if (lr || ud) mask[idx] = 1;
                }
            }

            byte[] hDist = new byte[pixelCount];
            for (int y = 0; y < bmpH; y++)
            {
                int row = y * bmpW;
                byte d = clampDist;
                for (int x = 0; x < bmpW; x++)
                {
                    int idx = row + x;
                    if (mask[idx] != 0) d = 0;
                    else if (d < clampDist) d++;
                    hDist[idx] = d;
                }
                d = clampDist;
                for (int x = bmpW - 1; x >= 0; x--)
                {
                    int idx = row + x;
                    if (mask[idx] != 0) d = 0;
                    else if (d < clampDist) d++;
                    if (d < hDist[idx]) hDist[idx] = d;
                }
            }

            int floorAlpha = opacity <= 0 ? 0 : Clamp(22 + (opacity * 33) / 255, 22, 55);
            int floorPArgb = Premultiply(floorAlpha, 188, 158, 112);
            int wallPArgb = isMinimap
                ? Premultiply(185, 212, 180, 124)
                : Premultiply(195, 218, 186, 130);
            int shadowCorePArgb = Premultiply(175, 14, 10, 7);
            int shadowSoftPArgb = Premultiply(85, 14, 10, 7);

            Array.Clear(raw, 0, pixelCount);
            for (int y = 0; y < bmpH; y++)
            {
                int row = y * bmpW;
                int yMin = y - shadowSoftR; if (yMin < 0) yMin = 0;
                int yMax = y + shadowSoftR; if (yMax >= bmpH) yMax = bmpH - 1;

                for (int x = 0; x < bmpW; x++)
                {
                    int idx = row + x;
                    if (mask[idx] != 0)
                    {
                        raw[idx] = floorPArgb;
                        continue;
                    }

                    int minD2 = 1000;
                    for (int ny = yMin; ny <= yMax; ny++)
                    {
                        int dy = ny - y;
                        int dy2 = dy * dy;
                        if (dy2 >= minD2) continue;
                        byte dx = hDist[ny * bmpW + x];
                        if (dx <= shadowSoftR)
                        {
                            int d2 = dx * dx + dy2;
                            if (d2 < minD2)
                            {
                                minD2 = d2;
                                if (minD2 == 1) break;
                            }
                        }
                    }

                    if (minD2 <= wallD2Max)
                        raw[idx] = wallPArgb;
                    else if (minD2 <= shadowCoreD2Max)
                        raw[idx] = shadowCorePArgb;
                    else if (minD2 <= shadowSoftD2Max)
                        raw[idx] = shadowSoftPArgb;
                }
            }

            Bitmap outBmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppPArgb);
            BitmapData outData = outBmp.LockBits(
                new Rectangle(0, 0, bmpW, bmpH),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                if (outData.Stride == bmpW * 4)
                {
                    Marshal.Copy(raw, 0, outData.Scan0, pixelCount);
                }
                else
                {
                    for (int y = 0; y < bmpH; y++)
                        Marshal.Copy(raw, y * bmpW, IntPtr.Add(outData.Scan0, y * outData.Stride), bmpW);
                }
            }
            finally
            {
                outBmp.UnlockBits(outData);
            }

            cache.Bitmap = outBmp;
            return cache;
        }

        private static int Premultiply(int a, int r, int g, int b)
        {
            if (a <= 0) return 0;
            int pr = (r * a + 127) / 255;
            int pg = (g * a + 127) / 255;
            int pb = (b * a + 127) / 255;
            return (a << 24) | (pr << 16) | (pg << 8) | pb;
        }

        private bool EnsureMinimapBuffer(int width, int height)
        {
            if (width <= 0 || height <= 0) return false;
            if (_minimapBuffer != null && _minimapBufferW == width && _minimapBufferH == height)
                return true;

            ReleaseMinimapBuffer();
            _minimapBuffer = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            _minimapBufferW = width;
            _minimapBufferH = height;
            int total = width * height;
            _minimapPixels = new int[total];

            double cx = (width - 1) * 0.5;
            double cy = (height - 1) * 0.5;
            double rx = (width - 1) * 0.5;
            double ry = (height - 1) * 0.5;
            double cornerR = Math.Min(rx, ry) * 0.42;
            double fade = Math.Min(18.0, Math.Min(width, height) * 0.15);
            if (fade < 4.0) fade = 4.0;

            List<int> indices = new List<int>();
            List<byte> factors = new List<byte>();

            for (int y = 0; y < height; y++)
            {
                double ay = Math.Abs(y - cy) - (ry - cornerR);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double ax = Math.Abs(x - cx) - (rx - cornerR);
                    double outside = Math.Sqrt(Math.Max(ax, 0.0) * Math.Max(ax, 0.0) +
                                               Math.Max(ay, 0.0) * Math.Max(ay, 0.0))
                                   + Math.Min(Math.Max(ax, ay), 0.0) - cornerR;

                    if (outside <= -fade) continue;

                    byte factor;
                    if (outside >= 0.0)
                    {
                        factor = 0;
                    }
                    else
                    {
                        double t = -outside / fade;
                        double s = t * t * (3.0 - 2.0 * t);
                        int v = (int)Math.Round(s * 255.0);
                        factor = (byte)Clamp(v, 0, 255);
                    }
                    if (factor < 255)
                    {
                        indices.Add(row + x);
                        factors.Add(factor);
                    }
                }
            }

            _vignetteIndices = indices.ToArray();
            _vignetteFactors = factors.ToArray();
            _vignetteCount = _vignetteIndices.Length;
            return true;
        }

        private void ApplyMinimapVignette()
        {
            if (_minimapBuffer == null || _vignetteCount == 0) return;

            int width = _minimapBufferW;
            int height = _minimapBufferH;
            int total = width * height;
            BitmapData data = _minimapBuffer.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppPArgb);
            try
            {
                if (data.Stride == width * 4)
                {
                    Marshal.Copy(data.Scan0, _minimapPixels, 0, total);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), _minimapPixels, y * width, width);
                }

                for (int k = 0; k < _vignetteCount; k++)
                {
                    int idx = _vignetteIndices[k];
                    int p = _minimapPixels[idx];
                    if (p == 0) continue;

                    byte f = _vignetteFactors[k];
                    if (f == 0)
                    {
                        _minimapPixels[idx] = 0;
                        continue;
                    }

                    uint up = (uint)p;
                    int a = (int)((up >> 24) & 0xFF);
                    int r = (int)((up >> 16) & 0xFF);
                    int gCh = (int)((up >> 8) & 0xFF);
                    int b = (int)(up & 0xFF);

                    a = (a * f + 127) / 255;
                    r = (r * f + 127) / 255;
                    gCh = (gCh * f + 127) / 255;
                    b = (b * f + 127) / 255;
                    _minimapPixels[idx] = (a << 24) | (r << 16) | (gCh << 8) | b;
                }

                if (data.Stride == width * 4)
                {
                    Marshal.Copy(_minimapPixels, 0, data.Scan0, total);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(_minimapPixels, y * width, IntPtr.Add(data.Scan0, y * data.Stride), width);
                }
            }
            finally
            {
                _minimapBuffer.UnlockBits(data);
            }
        }

        private void ReleaseMinimapBuffer()
        {
            if (_minimapBuffer != null)
            {
                try { _minimapBuffer.Dispose(); }
                catch { }
                _minimapBuffer = null;
            }
            _minimapBufferW = 0;
            _minimapBufferH = 0;
            _minimapPixels = null;
            _vignetteIndices = null;
            _vignetteFactors = null;
            _vignetteCount = 0;
        }

        /// <summary>
        /// Draws elite/boss markers and ground loot on top of the map in
        /// Diablo III / TurboHUD style with smooth 60 Hz Stopwatch animations.
        /// </summary>
        private void DrawMarkers(Graphics g, Func<float, float, PointF> project, float radius,
                                 float labelScale)
        {
            double elapsed = _animWatch.Elapsed.TotalSeconds;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(elapsed * Math.PI * 2.0 * 1.25);
            float spin = (float)((elapsed * 200.0) % 360.0);

            Font affixFont = new Font("Segoe UI", Math.Max(7f, radius * 1.75f), FontStyle.Bold);
            try
            {
                foreach (Game.MonsterMarker monster in _monsters)
                {
                    if (!monster.IsElite && !monster.IsBoss) continue;

                    PointF p = project(SmoothMonsterX(monster), SmoothMonsterY(monster));
                    Color color;
                    float r = radius;
                    bool isBoss = monster.IsBoss || monster.Rarity == (int)Game.MonsterRarity.Boss;
                    if (isBoss)
                    {
                        color = Color.FromArgb(245, 238, 56, 56);
                        r = radius * 1.85f;
                    }
                    else if (monster.Rarity == (int)Game.MonsterRarity.Champion)
                    {
                        color = Color.FromArgb(245, 70, 150, 255);
                        r = radius * 1.35f;
                    }
                    else if (monster.Rarity == (int)Game.MonsterRarity.Unique)
                    {
                        color = Color.FromArgb(245, 255, 155, 38);
                        r = radius * 1.55f;
                    }
                    else
                    {
                        color = Color.FromArgb(245, 255, 210, 64);
                        r = radius * 1.55f;
                    }

                    if (isBoss)
                    {
                        float auraR = r * (1.28f + 0.35f * pulse);
                        using (SolidBrush auraFill = new SolidBrush(Color.FromArgb((int)(42 + 38 * pulse), 238, 56, 56)))
                            g.FillEllipse(auraFill, p.X - auraR, p.Y - auraR, auraR * 2f, auraR * 2f);
                        using (Pen auraPen = new Pen(Color.FromArgb((int)(125 + 95 * (1f - pulse)), 238, 56, 56), 1.6f))
                            g.DrawEllipse(auraPen, p.X - auraR, p.Y - auraR, auraR * 2f, auraR * 2f);
                    }
                    else
                    {
                        float glowR = r + 2.4f;
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(48, color.R, color.G, color.B)))
                            g.FillEllipse(glow, p.X - glowR, p.Y - glowR, glowR * 2f, glowR * 2f);
                    }

                    using (SolidBrush rim = new SolidBrush(Color.FromArgb(230, 14, 10, 7)))
                        g.FillEllipse(rim, p.X - r - 1.4f, p.Y - r - 1.4f, (r + 1.4f) * 2f, (r + 1.4f) * 2f);
                    using (SolidBrush innerTint = new SolidBrush(Color.FromArgb(125, color.R, color.G, color.B)))
                        g.FillEllipse(innerTint, p.X - r, p.Y - r, r * 2f, r * 2f);

                    float ringR = r * 0.78f;
                    using (Pen ring = new Pen(color, Math.Max(1.5f, r * 0.34f)))
                        g.DrawEllipse(ring, p.X - ringR, p.Y - ringR, ringR * 2f, ringR * 2f);

                    float pipR = r * 0.34f;
                    Color pipColor = Color.FromArgb(245,
                        Math.Min(255, color.R + 48),
                        Math.Min(255, color.G + 48),
                        Math.Min(255, color.B + 48));
                    using (SolidBrush pip = new SolidBrush(pipColor))
                        g.FillEllipse(pip, p.X - pipR, p.Y - pipR, pipR * 2f, pipR * 2f);

                    if (monster.AffixNames != null && monster.AffixNames.Count > 0)
                    {
                        string affText = string.Join(" · ", monster.AffixNames.ToArray());
                        DrawClampedLabel(
                            g, affText, affixFont,
                            p.X, p.Y - r - 2f, p.Y + r + 2f,
                            Color.FromArgb(215, 16, 12, 10),
                            Color.FromArgb(190, 168, 132, 66),
                            Color.FromArgb(250, 240, 232, 216),
                            _placedLabels);
                    }
                }
            }
            finally
            {
                affixFont.Dispose();
            }

            foreach (Game.LootMarker item in _loot)
            {
                PointF p = project(SmoothLootX(item), SmoothLootY(item));
                LootColors colors = LootColors.For(item);

                float circle = radius * (2.0f + 0.55f * pulse);
                using (Pen pen = new Pen(Color.FromArgb((int)(160 + 70 * pulse), colors.Circle), 1.8f))
                    g.DrawEllipse(pen, p.X - circle, p.Y - circle, circle * 2f, circle * 2f);

                float shapeSize = radius * (1.45f + 0.25f * pulse);
                if (item.Rank >= 1)
                    DrawStar(g, p, shapeSize, spin, colors.Shape);
                else
                    DrawTriangle(g, p, shapeSize, spin, colors.Shape);

                string text = string.IsNullOrEmpty(item.Name) ? item.Label : item.Name;
                if (string.IsNullOrEmpty(text)) text = item.Label;
                if (string.IsNullOrEmpty(text)) continue;

                using (Font font = new Font("Segoe UI", Math.Max(6.5f, 7.2f * labelScale), FontStyle.Bold))
                {
                    DrawClampedLabel(
                        g, text, font,
                        p.X, p.Y - circle - 2f, p.Y + circle + 2f,
                        colors.LabelBackground,
                        colors.LabelBorder,
                        colors.LabelText,
                        _placedLabels);
                }
            }
        }

        private static void DrawClampedLabel(Graphics g, string text, Font font,
                                             float centerX, float preferTopY, float fallbackBottomY,
                                             Color bg, Color border, Color textColor,
                                             List<RectangleF> occupied)
        {
            SizeF sz = g.MeasureString(text, font);
            const float padX = 3.5f;
            const float padY = 1.5f;
            float boxW = sz.Width + padX * 2f;
            float boxH = sz.Height + padY * 2f;

            RectangleF bounds = g.VisibleClipBounds;
            const float margin = 2f;

            float bx = centerX - boxW * 0.5f;
            if (bx + boxW > bounds.Right - margin) bx = bounds.Right - margin - boxW;
            if (bx < bounds.Left + margin) bx = bounds.Left + margin;

            float yTop1 = preferTopY - boxH;
            float yBot1 = fallbackBottomY;
            float step = boxH + 2f;
            float[] candidates = new float[]
            {
                yTop1,
                yBot1,
                yTop1 - step,
                yBot1 + step,
                yTop1 - step * 2f,
                yBot1 + step * 2f
            };

            float chosenY = yTop1;
            if (chosenY < bounds.Top + margin) chosenY = yBot1;
            if (chosenY + boxH > bounds.Bottom - margin) chosenY = bounds.Bottom - margin - boxH;
            if (chosenY < bounds.Top + margin) chosenY = bounds.Top + margin;

            for (int c = 0; c < candidates.Length; c++)
            {
                float cy = candidates[c];
                if (cy < bounds.Top + margin || cy + boxH > bounds.Bottom - margin) continue;
                RectangleF cand = new RectangleF(bx, cy, boxW, boxH);
                bool overlaps = false;
                if (occupied != null)
                {
                    for (int k = 0; k < occupied.Count; k++)
                    {
                        if (occupied[k].IntersectsWith(cand)) { overlaps = true; break; }
                    }
                }
                if (!overlaps)
                {
                    chosenY = cy;
                    break;
                }
            }

            if (occupied != null)
                occupied.Add(new RectangleF(bx, chosenY, boxW, boxH));

            using (SolidBrush back = new SolidBrush(bg))
                g.FillRectangle(back, bx, chosenY, boxW, boxH);
            if (border.A > 0)
            {
                using (Pen bp = new Pen(border, 1f))
                    g.DrawRectangle(bp, bx, chosenY, boxW, boxH);
            }
            using (SolidBrush tb = new SolidBrush(textColor))
                g.DrawString(text, font, tb, bx + padX, chosenY + padY);
        }

        private static void DrawTriangle(Graphics g, PointF centre, float size, float angleDegrees, Color color)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            PointF[] points = new PointF[3];
            for (int i = 0; i < 3; i++)
            {
                double a = radians + i * (Math.PI * 2.0 / 3.0);
                points[i] = new PointF(
                    centre.X + (float)(Math.Cos(a) * size),
                    centre.Y + (float)(Math.Sin(a) * size));
            }
            using (SolidBrush fill = new SolidBrush(color))
                g.FillPolygon(fill, points);
            using (Pen edge = new Pen(Color.FromArgb(220, 14, 10, 7), 1.1f))
                g.DrawPolygon(edge, points);
        }

        private static void DrawStar(Graphics g, PointF centre, float size, float angleDegrees, Color color)
        {
            DrawTriangle(g, centre, size, angleDegrees, color);
            DrawTriangle(g, centre, size, angleDegrees + 180f, color);
        }

        /// <summary>Colours of one loot marker, matching TurboHUD Legendary/Set/Primal tiers.</summary>
        private struct LootColors
        {
            public Color Circle;
            public Color Shape;
            public Color LabelText;
            public Color LabelBackground;
            public Color LabelBorder;

            public static LootColors For(Game.LootMarker item)
            {
                LootColors colors = new LootColors();

                if (item.Rank >= 2)
                {
                    colors.Circle = Color.FromArgb(255, 238, 48, 48);
                    colors.Shape = item.IsSet
                        ? Color.FromArgb(255, 96, 238, 96)
                        : Color.FromArgb(255, 255, 72, 58);
                    colors.LabelBackground = item.IsSet
                        ? Color.FromArgb(220, 22, 88, 28)
                        : Color.FromArgb(225, 118, 18, 18);
                    colors.LabelBorder = Color.FromArgb(245, 255, 64, 64);
                    colors.LabelText = Color.FromArgb(255, 255, 238, 222);
                    return colors;
                }

                if (item.IsSet)
                {
                    colors.Circle = Color.FromArgb(255, 54, 214, 68);
                    colors.Shape = item.Rank == 1
                        ? Color.FromArgb(255, 138, 255, 72)
                        : Color.FromArgb(255, 102, 240, 74);
                    colors.LabelBackground = item.Rank == 1
                        ? Color.FromArgb(215, 24, 96, 30)
                        : Color.FromArgb(215, 16, 12, 10);
                    colors.LabelBorder = item.Rank == 1
                        ? Color.FromArgb(215, 96, 235, 96)
                        : Color.FromArgb(190, 72, 184, 78);
                    colors.LabelText = item.Rank == 1
                        ? Color.FromArgb(255, 230, 255, 226)
                        : Color.FromArgb(255, 112, 236, 110);
                }
                else
                {
                    colors.Circle = item.Rank == 1
                        ? Color.FromArgb(255, 255, 152, 28)
                        : Color.FromArgb(255, 240, 132, 24);
                    colors.Shape = item.Rank == 1
                        ? Color.FromArgb(255, 255, 186, 52)
                        : Color.FromArgb(255, 255, 158, 34);
                    colors.LabelBackground = item.Rank == 1
                        ? Color.FromArgb(215, 112, 54, 12)
                        : Color.FromArgb(215, 16, 12, 10);
                    colors.LabelBorder = item.Rank == 1
                        ? Color.FromArgb(215, 255, 168, 54)
                        : Color.FromArgb(190, 206, 124, 40);
                    colors.LabelText = item.Rank == 1
                        ? Color.FromArgb(255, 255, 240, 214)
                        : Color.FromArgb(255, 255, 168, 60);
                }
                return colors;
            }
        }

        /// <summary>
        /// Draws the geometry in a standalone panel, scaled to fit its content.
        /// </summary>
        private void DrawPanel(Graphics g, int width, int height)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (MapPolygon polygon in _polygons)
            {
                if (polygon == null || polygon.CachedFootprint || polygon.Approximate || polygon.Points == null)
                    continue;
                foreach (MapPoint p in polygon.Points)
                {
                    any = true;
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
            if (!any) return;

            double extentX = maxX - minX;
            double extentY = maxY - minY;
            if (extentX <= 0.0001 || extentY <= 0.0001) return;

            double margin = 12;
            double fit = Math.Min((width - margin * 2) / extentX, (height - margin * 2) / extentY);
            double offsetX = (width - extentX * fit) / 2.0 - minX * fit;
            double offsetY = (height - extentY * fit) / 2.0 - minY * fit;

            SilhouetteCache cache = GetOrBuildSilhouette(_panelCache, fit, false, false);
            if (cache != null && cache.Bitmap != null)
            {
                DrawSilhouette(g, cache, offsetX, offsetY);

                if (_hasPlayer)
                {
                    MapPoint p = MapGeometry.Project(_player.X, _player.Y);
                    float px = (float)(p.X * fit + offsetX);
                    float py = (float)(p.Y * fit + offsetY);
                    DrawPlayerMarker(g, px, py, 5.2f, false);
                }
            }

            Func<float, float, PointF> projectPanel = delegate(float wx, float wy)
            {
                MapPoint p = MapGeometry.Project(wx, wy);
                return new PointF((float)(p.X * fit + offsetX), (float)(p.Y * fit + offsetY));
            };
            _placedLabels.Clear();
            DrawMarkers(g, projectPanel, 3.4f, 1.05f);
            DrawInteractive(g, projectPanel, 5.0f, 1.05f);
        }

        private sealed class SilhouetteCache
        {
            public Bitmap Bitmap;
            public List<MapPolygon> Polygons;
            public int PolygonCount;
            public double Scale;
            public int Opacity;
            public double OriginX;
            public double OriginY;
            public double CacheRatio;
            public bool UseNative;
            public bool IsMinimap;

            public bool Matches(List<MapPolygon> polygons, double scale, int opacity,
                                bool useNative, bool isMinimap)
            {
                return ReferenceEquals(Polygons, polygons)
                    && PolygonCount == polygons.Count
                    && Math.Abs(Scale - scale) < 1e-6
                    && Opacity == opacity
                    && UseNative == useNative
                    && IsMinimap == isMinimap;
            }

            public void Release()
            {
                if (Bitmap != null)
                {
                    try { Bitmap.Dispose(); }
                    catch { }
                    Bitmap = null;
                }
                Polygons = null;
                PolygonCount = 0;
            }
        }

        /// <summary>
        /// Hands the rendered surface to the window manager as a layered
        /// window. The alpha channel of the surface is preserved.
        /// </summary>
        private bool PushLayered(int width, int height)
        {
            if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero) return false;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return false;
            try
            {
                SetWindowPos(Handle, new IntPtr(-1), Left, Top, width, height,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);

                POINT dst = new POINT();
                dst.X = Left;
                dst.Y = Top;
                POINT src = new POINT();
                src.X = 0;
                src.Y = 0;
                SIZE size = new SIZE();
                size.Cx = width;
                size.Cy = height;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;              // AC_SRC_OVER
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AC_SRC_ALPHA;

                return UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                    _memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            catch { return false; }
            finally
            {
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
