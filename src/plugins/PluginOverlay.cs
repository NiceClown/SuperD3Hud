using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SuperD3Hud.TurboHud;

namespace SuperD3Hud.Plugins
{
    /// <summary>
    /// A transparent, click-through window that shows the drawing of the local
    /// plugins over the game. It uses the same layered-window technique as the
    /// map overlay: the window manager composites a premultiplied surface, so
    /// the game stays visible and receives every click.
    /// </summary>
    public class PluginOverlay : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int ULW_ALPHA = 0x00000002;
        private const int AC_SRC_ALPHA = 0x00000001;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int DIB_RGB_COLORS = 0;

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

        private readonly PluginRuntime _runtime;
        private TurboHostRuntime _turboRuntime;

        public PluginOverlay(PluginRuntime runtime)
        {
            _runtime = runtime;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(400, 400);
            BackColor = Color.Black;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT |
                              WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The layered window supplies the background itself.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Redraw();
        }

        /// <summary>Last drawing error, empty when the frame was shown.</summary>
        public string LastError { get; private set; }

        /// <summary>Attaches the TurboHUD compatibility host to this overlay.</summary>
        public void SetTurboRuntime(TurboHostRuntime runtime)
        {
            _turboRuntime = runtime;
        }

        /// <summary>Paints the plugins into the window surface.</summary>
        public void Redraw()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0 || !IsHandleCreated) return;

            try
            {
                if (!EnsureSurface(width, height)) return;

                using (Graphics g = Graphics.FromImage(_surface))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    using (PluginCanvas canvas = new PluginCanvas(g, width, height))
                        _runtime.Draw(canvas);

                    if (_turboRuntime != null)
                        _turboRuntime.Draw(g, width, height);
                }

                if (!Push(width, height))
                    LastError = "Не удалось вывести плагины на экран.";
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
        /// Creates the surface the plugins draw into. The bitmap points at the
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
            if (disposing) ReleaseSurface();
            base.Dispose(disposing);
        }

        private bool Push(int width, int height)
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
                blend.BlendOp = 0;
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
    }
}
