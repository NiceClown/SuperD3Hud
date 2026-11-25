using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SharpDX.Direct2D1;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Drawing controller. Plugins create brushes and fonts here; every call is
    /// forwarded to the host's GDI+ canvas, so a plugin needs no Direct2D
    /// device of its own.
    /// </summary>
    public class HostRenderController : IRenderController
    {
        private static readonly SharpDX.Direct2D1.Factory _d2dFactory = new SharpDX.Direct2D1.Factory();

        private readonly HostContext _context;
        private readonly HostCanvas _canvas;
        private readonly HostWindow _window;
        private readonly Dictionary<string, IUiElement> _uiElements =
            new Dictionary<string, IUiElement>(StringComparer.OrdinalIgnoreCase);
        private bool _renderEnabled = true;

        public HostRenderController(HostContext context, HostCanvas canvas, HostWindow window = null)
        {
            _context = context;
            _canvas = canvas ?? new HostCanvas();
            _window = window;
        }

        public bool IsRenderEnabled
        {
            get { return _renderEnabled; }
            set { _renderEnabled = value; }
        }

        public bool UiHidden { get { return false; } }
        public bool IsAnyBlockingUiElementVisible { get { return false; } }

        public IBrush CreateBrush(int a, int r, int g, int b, float strokeWidth,
                                  DashStyle dash = DashStyle.Solid,
                                  CapStyle startCap = CapStyle.Flat,
                                  CapStyle endCap = CapStyle.Flat)
        {
            try
            {
                return new HostBrush(_canvas, _window, a, r, g, b,
                                     SanitizeStrokeWidth(strokeWidth), dash, startCap, endCap);
            }
            catch
            {
                // Plugin arguments are untrusted. Always return a usable brush.
                return new HostBrush(_canvas, _window, 255, 255, 255, 255, 1f,
                                     DashStyle.Solid, CapStyle.Flat, CapStyle.Flat);
            }
        }

        public IFont CreateFont(string fontFamily, float size, int a, int r, int g, int b,
                                bool bold, bool italic, bool standardShadow)
        {
            int shadowA = standardShadow ? 220 : 0;
            return CreateFontSafe(fontFamily, size, a, r, g, b, bold, italic,
                                  shadowA, 0, 0, 0, standardShadow);
        }

        public IFont CreateFont(string fontFamily, float size, int a, int r, int g, int b,
                                bool bold, bool italic, int shadowA, int shadowR, int shadowG,
                                int shadowB, bool shadowIsHeavy)
        {
            return CreateFontSafe(fontFamily, size, a, r, g, b, bold, italic,
                                  shadowA, shadowR, shadowG, shadowB, shadowIsHeavy);
        }

        public IUiElement GetUiElement(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            IUiElement element;
            if (_uiElements.TryGetValue(path, out element)) return element;

            element = CreateKnownUiElement(path);
            if (element == null) element = new UiElement(path);
            _uiElements[path] = element;
            return element;
        }

        private int WindowWidthOrDefault
        {
            get { return _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth; }
        }

        private int WindowHeightOrDefault
        {
            get { return _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight; }
        }

        private bool IsInGameHudVisible()
        {
            return _context != null && _context.GameRunning && !_context.LargeMapOpen;
        }

        private IUiElement CreateKnownUiElement(string path)
        {
            if (string.Equals(path, "Root.NormalLayer.game_dialog_backgroundScreenPC.game_progressBar_healthBall", StringComparison.OrdinalIgnoreCase))
            {
                return new UiElement(path,
                    delegate { return IsInGameHudVisible(); },
                    delegate
                    {
                        int w = WindowWidthOrDefault;
                        int h = WindowHeightOrDefault;
                        float scale = h / 1080f;
                        // Health orb center is w*0.5 - 412*scale; width=180*scale compensates for
                        // ResourceOverGlobePlugin painting at Left + 0.6*Width so text sits centered in the globe.
                        float orbCenterX = w * 0.5f - 412f * scale;
                        float boxW = 180f * scale;
                        float boxH = 144f * scale;
                        float y = h - 158f * scale;
                        return new RectangleF(orbCenterX - boxW * 0.6f, y, boxW, boxH);
                    });
            }

            if (string.Equals(path, "Root.NormalLayer.game_dialog_backgroundScreenPC.game_progressBar_manaBall", StringComparison.OrdinalIgnoreCase))
            {
                return new UiElement(path,
                    delegate { return IsInGameHudVisible(); },
                    delegate
                    {
                        int w = WindowWidthOrDefault;
                        int h = WindowHeightOrDefault;
                        float scale = h / 1080f;
                        float orbCenterX = w * 0.5f + 412f * scale;
                        float size = 144f * scale;
                        float y = h - 158f * scale;
                        return new RectangleF(orbCenterX - size * 0.5f, y, size, size);
                    });
            }

            if (string.Equals(path, "Root.NormalLayer.game_dialog_backgroundScreenPC.latency_meter", StringComparison.OrdinalIgnoreCase))
            {
                return new UiElement(path,
                    delegate { return IsInGameHudVisible(); },
                    delegate
                    {
                        int w = WindowWidthOrDefault;
                        int h = WindowHeightOrDefault;
                        float scale = h / 1080f;
                        return new RectangleF(w * 0.5f + 392f * scale, h - 58f * scale, 18f * scale, 46f * scale);
                    });
            }

            if (string.Equals(path, "*portrait-bottom", StringComparison.OrdinalIgnoreCase))
            {
                return new UiElement(path,
                    delegate { return IsInGameHudVisible(); },
                    delegate
                    {
                        int h = WindowHeightOrDefault;
                        float scale = h / 1080f;
                        return new RectangleF(18f * scale, 148f * scale, 96f * scale, 22f * scale);
                    });
            }

            if (string.Equals(path, "Root.NormalLayer.minimap_dialog_backgroundScreen.minimap_dialog_pve.BoostWrapper.BoostsDifficultyStackPanel.clock", StringComparison.OrdinalIgnoreCase))
            {
                return new UiElement(path,
                    delegate { return IsInGameHudVisible(); },
                    delegate
                    {
                        int w = WindowWidthOrDefault;
                        int h = WindowHeightOrDefault;
                        float scale = h / 1080f;
                        return new RectangleF(w - 120f * scale, 18f * scale, 100f * scale, 24f * scale);
                    });
            }

            return null;
        }

        private IUiElement GetOrCreateUiElement(string path, Func<bool> visibleFunc, Func<RectangleF> rectFunc)
        {
            if (string.IsNullOrEmpty(path)) return null;

            IUiElement element;
            if (_uiElements.TryGetValue(path, out element))
            {
                UiElement hostElement = element as UiElement;
                if (hostElement != null)
                {
                    if (visibleFunc != null) hostElement.VisibleFunc = visibleFunc;
                    if (rectFunc != null) hostElement.RectFunc = rectFunc;
                }
                return element;
            }

            element = new UiElement(path, visibleFunc, rectFunc);
            _uiElements[path] = element;
            return element;
        }

        public IUiElement RegisterUiElement(string path, IUiElement collectOnlyWhenThisIsVisible,
                                            IUiElement collectOnlyWhenThisIsInvisible,
                                            float inflateXby = 0, float inflateYby = 0)
        {
            return GetUiElement(path);
        }

        public IUiElement InGameBottomHudUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.game_bottom_hud",
                    delegate { return _context != null && _context.GameRunning; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        float bw = h * 0.90f;
                        float bh = h * 0.15f;
                        float bx = (w - bw) * 0.5f;
                        float by = h - bh;
                        return new RectangleF(bx, by, bw, bh);
                    });
            }
        }

        public IUiElement MonsterHpBarUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.monster_hp_bar",
                    delegate { return _context != null && _context.GameRunning; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        float bw = h * 0.45f;
                        float bh = h * 0.06f;
                        float bx = (w - bw) * 0.5f;
                        float by = h * 0.02f;
                        return new RectangleF(bx, by, bw, bh);
                    });
            }
        }

        public IUiElement GetPlayerSkillUiElement(ActionKey key)
        {
            return GetOrCreateUiElement("Root.NormalLayer.skill_button_" + ((int)key).ToString(),
                delegate { return IsInGameHudVisible(); },
                delegate
                {
                    int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                    int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                    float scale = h / 1080f;
                    float btnSize = 54f * scale;
                    float half = btnSize * 0.5f;
                    float y = h - 77f * scale;

                    // Calibrated directly against Diablo III's bottom skill bar (skull sits between LMB and RMB).
                    float cx;
                    switch (key)
                    {
                        case ActionKey.Skill1:
                            cx = w * 0.5f - 301.5f * scale;
                            break;
                        case ActionKey.Skill2:
                            cx = w * 0.5f - 234.0f * scale;
                            break;
                        case ActionKey.Skill3:
                            cx = w * 0.5f - 167.0f * scale;
                            break;
                        case ActionKey.Skill4:
                            cx = w * 0.5f - 100.0f * scale;
                            break;
                        case ActionKey.LeftSkill:
                            cx = w * 0.5f - 30.5f * scale;
                            break;
                        case ActionKey.RightSkill:
                            cx = w * 0.5f + 32.0f * scale;
                            break;
                        case ActionKey.Heal:
                            cx = w * 0.5f + 102.0f * scale;
                            break;
                        case ActionKey.TownPortal:
                            cx = w * 0.5f + 160.0f * scale;
                            break;
                        default:
                            return new RectangleF(-1000f, y, btnSize, btnSize);
                    }
                    return new RectangleF(cx - half, y, btnSize, btnSize);
                });
        }

        public IUiElement MinimapUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.minimap",
                    delegate { return _context != null && _context.GameRunning && !_context.LargeMapOpen; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        // Same geometry as MapOverlay.TryGetMinimapRect: the native
                        // minimap is a 350 unit square scaled by height / 1200.
                        float k = h / 1200f;
                        float mw = 350f * k;
                        float mx = w - 1f - 377f * k;
                        float my = 54f * k;
                        return new RectangleF(mx, my, mw, mw);
                    });
            }
        }

        public IUiElement NephalemRiftBarUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.nephalem_rift_bar",
                    delegate { return _context != null && _context.GameRunning && (_context.SpecialArea == 1 || _context.RiftPercentage > 0); },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        float mw = h * 0.28f;
                        float mx = w - mw - h * 0.015f;
                        float my = h * 0.015f + mw + h * 0.035f;
                        return new RectangleF(mx, my, mw, 22f * (h / 1080f));
                    });
            }
        }

        public IUiElement GreaterRiftBarUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.greater_rift_bar",
                    delegate { return _context != null && _context.GameRunning && (_context.SpecialArea == 2 || _context.RiftPercentage > 0); },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        float mw = h * 0.28f;
                        float mx = w - mw - h * 0.015f;
                        float my = h * 0.015f + mw + h * 0.035f;
                        return new RectangleF(mx, my, mw, 22f * (h / 1080f));
                    });
            }
        }

        public IUiElement ChallengeRiftBarUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.challenge_rift_bar",
                    delegate { return _context != null && _context.GameRunning && _context.SpecialArea == 3; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        float mw = h * 0.28f;
                        float mx = w - mw - h * 0.015f;
                        float my = h * 0.015f + mw + h * 0.035f;
                        return new RectangleF(mx, my, mw, 22f * (h / 1080f));
                    });
            }
        }
        public IUiElement ChallengeRiftAheadPanelUiElement { get { return GetUiElement("Root.NormalLayer.challenge_rift_ahead_panel"); } }
        public IUiElement ParagonLevelUpSplashTextUiElement { get { return GetUiElement("Root.NormalLayer.paragon_level_up_splash"); } }
        public IUiElement BuffBarExtendedBuffsUiElement { get { return GetUiElement("Root.NormalLayer.buff_bar_extended"); } }
        public IEnumerable<IUiElement> BuffBarUiElements { get { return new IUiElement[0]; } }

        public IUiElement WorldMapUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.world_map",
                    delegate { return _context != null && _context.LargeMapOpen; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        return new RectangleF(0f, 0f, w, h);
                    });
            }
        }

        public IUiElement ActMapUiElement
        {
            get
            {
                return GetOrCreateUiElement("Root.NormalLayer.act_map",
                    delegate { return _context != null && _context.LargeMapOpen; },
                    delegate
                    {
                        int w = _context == null || _context.WindowWidth <= 0 ? 1920 : _context.WindowWidth;
                        int h = _context == null || _context.WindowHeight <= 0 ? 1080 : _context.WindowHeight;
                        return new RectangleF(0f, 0f, w, h);
                    });
            }
        }

        public PathGeometry CreateGeometry()
        {
            try
            {
                return new PathGeometry(_d2dFactory);
            }
            catch
            {
                return null;
            }
        }

        public void GetMinimapCoordinates(float x, float y, out float mapX, out float mapY)
        {
            float px = _context == null ? 0f : _context.PlayerX;
            float py = _context == null ? 0f : _context.PlayerY;
            if (float.IsNaN(px) || float.IsInfinity(px)) px = 0f;
            if (float.IsNaN(py) || float.IsInfinity(py)) py = 0f;
            if (float.IsNaN(x) || float.IsInfinity(x)) x = px;
            if (float.IsNaN(y) || float.IsInfinity(y)) y = py;

            // Coordinates are rotated 45 degrees to match Diablo III's top-down minimap:
            float dx = x - px;
            float dy = y - py;
            float mmX = (dy - dx) * 0.70710678f;
            float mmY = (dx + dy) * 0.70710678f;

            float scale = MinimapScale;
            RectangleF rect = (_context != null && _context.LargeMapOpen)
                ? WorldMapUiElement.Rectangle
                : MinimapUiElement.Rectangle;

            float centerX = rect.Left + rect.Width * 0.5f;
            float centerY = rect.Top + rect.Height * 0.5f;

            mapX = centerX + mmX * scale;
            mapY = centerY + mmY * scale;
        }

        public float MinimapScale
        {
            get
            {
                if (_context != null && _context.LargeMapOpen)
                {
                    int h = _context.WindowHeight;
                    float largeScale = _context.LargeMapScale > 0.01f ? _context.LargeMapScale : 1f;
                    return (h > 0 ? (float)h / 1200f : 1f) * largeScale;
                }
                RectangleF mmRect = MinimapUiElement.Rectangle;
                float baseWidth = mmRect.Width > 0 ? mmRect.Width : 300f;
                return baseWidth / 350f;
            }
        }

        public void SetHint(string text, string specialLocation = null)
        {
            if (_context != null) _context.Hint = text;
        }

        public void TurnOnAliasing() { }
        public void TurnOffAliasing() { }

        public void CaptureScreenToFile(string subFolder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            try
            {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           string.IsNullOrEmpty(subFolder) ? "screenshots" : subFolder);
                Directory.CreateDirectory(root);

                string path = Path.Combine(root, fileName);
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    path += ".png";

                int width = _context == null ? 0 : _context.WindowWidth;
                int height = _context == null ? 0 : _context.WindowHeight;
                int x = _context == null ? 0 : _context.WindowX;
                int y = _context == null ? 0 : _context.WindowY;
                if (width <= 0 || height <= 0) return;

                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                        g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
                    bitmap.Save(path, ImageFormat.Png);
                }
            }
            catch
            {
                // A screenshot is a convenience, never a reason to break a frame.
            }
        }

        private static float SanitizeStrokeWidth(float width)
        {
            if (float.IsNaN(width) || float.IsInfinity(width)) return 1f;
            if (width < -1000f) return -1000f;
            return width > 1000f ? 1000f : width;
        }

        private HostFont CreateFontSafe(string fontFamily, float size, int a, int r, int g, int b,
                                        bool bold, bool italic, int shadowA, int shadowR,
                                        int shadowG, int shadowB, bool shadowIsHeavy)
        {
            string family = NormalizeFontFamily(fontFamily);
            float safeSize = size;
            if (float.IsNaN(safeSize) || float.IsInfinity(safeSize) || safeSize < 4f) safeSize = 12f;
            if (safeSize > 512f) safeSize = 512f;
            return new HostFont(_canvas, family, safeSize, a, r, g, b, bold, italic,
                                shadowA, shadowR, shadowG, shadowB, shadowIsHeavy);
        }

        private static string NormalizeFontFamily(string family)
        {
            if (string.IsNullOrEmpty(family)) return "Segoe UI";
            try
            {
                using (FontFamily probe = new FontFamily(family)) { }
                return family;
            }
            catch
            {
                return "Segoe UI";
            }
        }
    }
}
