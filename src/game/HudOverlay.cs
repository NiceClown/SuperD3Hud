using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// The built-in HUD: buff list, skill cooldowns, health and backpack count.
    ///
    /// It is a layered, click-through window over the game client area, built
    /// with the same technique as the map overlay: the window manager
    /// composites a premultiplied DIB-backed surface, so the game stays visible
    /// and keeps every click with zero per-frame bitmap allocations.
    /// </summary>
    public class HudOverlay : Form
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

        private const double ReadyFlashDurationSec = 0.350;
        private const double RiftDampTauSec = 0.120;

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

        private sealed class SkillAnimState
        {
            public bool Initialized;
            public bool WasNotReady;
            public double LastSnapshotRemaining;
            public double LastSnapshotDuration;
            public double DisplayRemaining;
            public double ReadyFlashStartSec = -10.0;
        }

        // Diablo III gothic obsidian & antique-gold palette
        private static readonly Color ObsidianTop = Color.FromArgb(24, 18, 15);        // #18120F
        private static readonly Color ObsidianBottom = Color.FromArgb(12, 9, 8);       // #0C0908
        private static readonly Color AntiqueGold = Color.FromArgb(168, 132, 66);      // #A88442
        private static readonly Color InnerBevel = Color.FromArgb(58, 42, 22);         // #3A2A16
        private static readonly Color CornerDiamond = Color.FromArgb(218, 178, 96);    // #DAB260
        private static readonly Color IvoryText = Color.FromArgb(242, 234, 218);       // #F2EADA
        private static readonly Color ParchmentText = Color.FromArgb(224, 206, 176);   // #E0CEB0
        private static readonly Color MutedStoneText = Color.FromArgb(174, 158, 136);  // #AE9E88
        private static readonly Color GoldHighlight = Color.FromArgb(242, 186, 84);    // #F2BA54
        private static readonly Color ReadyFlashGold = Color.FromArgb(255, 216, 108);  // #FFD86C

        private readonly HudSettings _settings;
        private BuffSnapshot _buffs;
        private SkillSnapshot _skills;
        private InventorySnapshot _inventory;
        private ActorSnapshot _actors;
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        // Persistent DIB-backed surface (reused across all frames)
        private IntPtr _memDc;
        private IntPtr _dib;
        private IntPtr _bits;
        private IntPtr _oldBitmap;
        private Bitmap _surface;
        private int _surfaceWidth;
        private int _surfaceHeight;

        // High-resolution animation state
        private readonly Stopwatch _animWatch = Stopwatch.StartNew();
        private double _lastAnimSec;
        private double _currentAnimSec;
        private double _displayRiftPct;
        private bool _hasDisplayRift;
        private int _lastRiftArea;
        private readonly Dictionary<int, SkillAnimState> _skillAnim = new Dictionary<int, SkillAnimState>();

        // Redraw skipping state
        private bool _hasRendered;
        private ulong _lastVisualHash;
        private bool _wasAnimActive;

        public HudOverlay(HudSettings settings)
        {
            _settings = settings ?? new HudSettings();
            LastError = "";

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

        /// <summary>Updates the data to draw and repaints when content or animations change.</summary>
        public void UpdateContent(BuffSnapshot buffs, SkillSnapshot skills, InventorySnapshot inventory, ActorSnapshot actors = null)
        {
            _buffs = buffs;
            _skills = skills;
            _inventory = inventory;
            _actors = actors;

            double nowSec = _animWatch.Elapsed.TotalSeconds;
            bool animActive = StepAnimations(nowSec);
            ulong visualHash = ComputeVisualHash();

            bool changed = !_hasRendered || visualHash != _lastVisualHash;
            if (!changed && !animActive && !_wasAnimActive)
                return;

            _lastVisualHash = visualHash;
            _wasAnimActive = animActive;
            Redraw();
        }

        /// <summary>Renders the current HUD content into an existing bitmap.</summary>
        public void RenderTo(Bitmap target)
        {
            if (target == null) return;
            if (_lastAnimSec <= 0.0)
                StepAnimations(_animWatch.Elapsed.TotalSeconds);

            using (Graphics g = Graphics.FromImage(target))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                Render(g, target.Width, target.Height);
            }
        }

        public void Redraw()
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 0 || height <= 0 || !IsHandleCreated) return;

            try
            {
                if (!EnsureSurface(width, height))
                {
                    LastError = "Не удалось создать поверхность HUD.";
                    return;
                }

                using (Graphics g = Graphics.FromImage(_surface))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    Render(g, width, height);
                }

                if (!Push(width, height))
                    LastError = "Не удалось вывести HUD на экран.";
                else
                {
                    LastError = "";
                    _hasRendered = true;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        // ------------------------------------------------------------------
        // Animation & Dirty Detection
        // ------------------------------------------------------------------

        private bool StepAnimations(double nowSec)
        {
            double dt = nowSec - _lastAnimSec;
            if (dt < 0.0) dt = 0.0;
            if (dt > 0.25) dt = 0.25;
            _lastAnimSec = nowSec;
            _currentAnimSec = nowSec;

            bool animActive = false;

            // 1. Rift progress bar smooth damping + sheen sweep / guardian pulse
            ActorSnapshot actors = _actors;
            if (_settings.Enabled && actors != null && (actors.SpecialArea > 0 || actors.RiftPercentage > 0))
            {
                double targetPct = Math.Min(100.0, Math.Max(0.0, actors.RiftPercentage));
                if (!_hasDisplayRift || actors.SpecialArea != _lastRiftArea ||
                    Math.Abs(targetPct - _displayRiftPct) > 40.0 || targetPct < _displayRiftPct - 5.0)
                {
                    _displayRiftPct = targetPct;
                    _hasDisplayRift = true;
                    _lastRiftArea = actors.SpecialArea;
                }
                else
                {
                    double alpha = 1.0 - Math.Exp(-dt / RiftDampTauSec);
                    _displayRiftPct += (targetPct - _displayRiftPct) * alpha;
                    if (Math.Abs(targetPct - _displayRiftPct) < 0.02)
                        _displayRiftPct = targetPct;
                }
                // Active when bar is filling/filled (sheen sweep & 100% guardian pulse)
                if (_displayRiftPct > 0.1 || Math.Abs(targetPct - _displayRiftPct) >= 0.02)
                    animActive = true;
            }
            else
            {
                _hasDisplayRift = false;
                _displayRiftPct = 0.0;
                _lastRiftArea = 0;
            }

            // 2. Skill cooldown smoothing + 350ms ready-flash transition
            SkillSnapshot skills = _skills;
            if (_settings.Enabled && _settings.ShowSkills && skills != null && skills.Skills != null)
            {
                for (int i = 0; i < skills.Skills.Count; i++)
                {
                    SkillCooldown slot = skills.Skills[i];
                    if (slot == null) continue;
                    if (slot.DurationSeconds <= 0.001 && slot.RemainingSeconds <= 0.001) continue;

                    SkillAnimState st;
                    if (!_skillAnim.TryGetValue(slot.Slot, out st))
                    {
                        st = new SkillAnimState();
                        _skillAnim[slot.Slot] = st;
                    }

                    bool isReady = slot.IsReady;
                    if (!st.Initialized)
                    {
                        st.Initialized = true;
                        st.DisplayRemaining = slot.RemainingSeconds;
                        st.LastSnapshotRemaining = slot.RemainingSeconds;
                        st.LastSnapshotDuration = slot.DurationSeconds;
                        st.WasNotReady = !isReady;
                    }
                    else if (isReady)
                    {
                        if (st.WasNotReady)
                        {
                            st.ReadyFlashStartSec = nowSec;
                            st.WasNotReady = false;
                        }
                        st.DisplayRemaining = 0.0;
                        st.LastSnapshotRemaining = 0.0;
                        st.LastSnapshotDuration = slot.DurationSeconds;
                    }
                    else
                    {
                        st.WasNotReady = true;
                        bool updated = Math.Abs(slot.RemainingSeconds - st.LastSnapshotRemaining) > 0.0005
                            || Math.Abs(slot.DurationSeconds - st.LastSnapshotDuration) > 0.0005;
                        if (updated)
                        {
                            if (slot.RemainingSeconds > st.DisplayRemaining + 0.15 ||
                                Math.Abs(slot.RemainingSeconds - st.DisplayRemaining) > 0.35)
                            {
                                st.DisplayRemaining = slot.RemainingSeconds;
                            }
                            else
                            {
                                st.DisplayRemaining += (slot.RemainingSeconds - st.DisplayRemaining) * 0.5;
                            }
                            st.LastSnapshotRemaining = slot.RemainingSeconds;
                            st.LastSnapshotDuration = slot.DurationSeconds;
                        }
                        else
                        {
                            double floorVal = Math.Max(0.01, slot.RemainingSeconds - 0.15);
                            st.DisplayRemaining = Math.Max(floorVal, st.DisplayRemaining - dt);
                        }
                    }

                    if (!isReady)
                        animActive = true;
                    else if (nowSec - st.ReadyFlashStartSec <= ReadyFlashDurationSec)
                        animActive = true;
                }
            }

            return animActive;
        }

        private ulong ComputeVisualHash()
        {
            ulong h = 14695981039346656037UL;
            h = HashInt(h, ClientSize.Width);
            h = HashInt(h, ClientSize.Height);
            h = HashInt(h, _settings.Enabled ? 1 : 0);
            if (!_settings.Enabled) return h;

            h = HashInt(h, (int)Math.Round(_settings.Scale * 100.0));
            h = HashInt(h, Clamp(_settings.Opacity, 0, 255));
            h = HashInt(h, _settings.ShowBuffs ? 1 : 0);
            h = HashInt(h, _settings.ShowSkills ? 1 : 0);
            h = HashInt(h, _settings.ShowHealth ? 1 : 0);
            h = HashInt(h, _settings.ShowInventory ? 1 : 0);
            h = HashInt(h, _settings.ShowDamageMeter ? 1 : 0);
            h = HashInt(h, _settings.BuffCorner);
            h = HashInt(h, _settings.BuffOffsetX);
            h = HashInt(h, _settings.BuffOffsetY);

            if (_settings.ShowBuffs && _buffs != null && _buffs.Buffs != null)
            {
                int count = 0;
                for (int i = 0; i < _buffs.Buffs.Count && count < 24; i++)
                {
                    BuffEntry entry = _buffs.Buffs[i];
                    if (entry == null || !entry.Active || BuffPowers.IsInternal(entry.PowerSno)) continue;
                    count++;
                    h = HashInt(h, entry.PowerSno);
                    h = HashInt(h, entry.Stacks);
                    h = HashString(h, entry.Name);
                    h = HashString(h, entry.Element);
                }
                h = HashInt(h, count);
            }

            if (_settings.ShowSkills && _skills != null && _skills.Skills != null)
            {
                for (int i = 0; i < _skills.Skills.Count; i++)
                {
                    SkillCooldown slot = _skills.Skills[i];
                    if (slot == null) continue;
                    if (slot.DurationSeconds <= 0.001 && slot.RemainingSeconds <= 0.001) continue;
                    h = HashInt(h, slot.Slot);
                    h = HashString(h, slot.Key);
                    h = HashInt(h, slot.IsReady ? 1 : 0);
                    h = HashInt(h, (int)Math.Round(slot.DurationSeconds * 10.0));
                    h = HashInt(h, (int)Math.Round(slot.RemainingSeconds * 10.0));
                }
            }

            if (_settings.ShowHealth && _skills != null)
            {
                h = HashInt(h, _skills.Health >= 0 ? (int)Math.Round(_skills.Health) : -1);
            }

            if (_settings.ShowInventory && _inventory != null)
            {
                h = HashInt(h, _inventory.UsedSlots);
                h = HashInt(h, _inventory.TotalSlots);
            }

            if (_settings.ShowDamageMeter && _actors != null && _actors.DamageActive && _actors.DamageEntries != null)
            {
                h = HashInt(h, _actors.DamageEntries.Count);
                h = HashLong(h, (long)Math.Round(_actors.DamageTotal));
                h = HashLong(h, (long)Math.Round(_actors.DamageDps));
                for (int i = 0; i < _actors.DamageEntries.Count; i++)
                {
                    DamageEntry e = _actors.DamageEntries[i];
                    if (e == null) continue;
                    h = HashString(h, e.Name);
                    h = HashLong(h, (long)Math.Round(e.Damage));
                    h = HashInt(h, (int)Math.Round(e.Share * 1000.0));
                    h = HashInt(h, e.IsLocalPlayer ? 1 : 0);
                }
            }

            if (_actors != null)
            {
                h = HashInt(h, _actors.SpecialArea);
                h = HashInt(h, (int)Math.Round(_actors.RiftPercentage * 20.0));
            }

            return h;
        }

        private static ulong HashInt(ulong h, int v)
        {
            unchecked
            {
                h ^= (uint)v;
                return h * 1099511628211UL;
            }
        }

        private static ulong HashLong(ulong h, long v)
        {
            unchecked
            {
                h ^= (ulong)v;
                return h * 1099511628211UL;
            }
        }

        private static ulong HashString(ulong h, string s)
        {
            if (string.IsNullOrEmpty(s)) return HashInt(h, 0);
            unchecked
            {
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void Render(Graphics g, int width, int height)
        {
            if (!_settings.Enabled) return;

            double scale = _settings.Scale;
            int alpha = Clamp(_settings.Opacity, 0, 255);
            if (alpha <= 0) return;

            if (_settings.ShowBuffs) DrawBuffs(g, width, height, scale, alpha);
            if (_settings.ShowSkills) DrawSkills(g, width, height, scale, alpha);
            if (_settings.ShowHealth) DrawHealth(g, width, height, scale, alpha);
            if (_settings.ShowInventory) DrawInventory(g, width, height, scale, alpha);
            if (_settings.ShowDamageMeter) DrawDamageMeter(g, width, height, scale, alpha);
            DrawRiftProgress(g, width, height, scale, alpha);
        }

        /// <summary>
        /// Draws a Diablo III gothic obsidian gradient panel (#18120F -> #0C0908)
        /// with a double hammered antique-gold/bronze border (#A88442 outer, #3A2A16 inner bevel)
        /// and optional corner diamond accents.
        /// </summary>
        private static void DrawGothicPanel(Graphics g, float x, float y, float w, float h, int alpha, double scale, bool cornerDiamonds)
        {
            if (w < 2f || h < 2f) return;

            int bgTopAlpha = Clamp(alpha * 228 / 255, 0, 255);
            int bgBotAlpha = Clamp(alpha * 240 / 255, 0, 255);
            RectangleF outer = new RectangleF(x, y, w, h);

            using (LinearGradientBrush bg = new LinearGradientBrush(
                outer,
                Color.FromArgb(bgTopAlpha, ObsidianTop),
                Color.FromArgb(bgBotAlpha, ObsidianBottom),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, outer);
            }

            // Inner warm highlight along top rim
            if (w > 6f)
            {
                using (Pen topGlow = new Pen(Color.FromArgb(Clamp(alpha * 50 / 255, 0, 255), 220, 178, 98), 1f))
                    g.DrawLine(topGlow, x + 2f, y + 1.5f, x + w - 2f, y + 1.5f);
            }

            // Inner dark-bronze bevel
            if (w > 4f && h > 4f)
            {
                using (Pen inner = new Pen(Color.FromArgb(Clamp(alpha * 235 / 255, 0, 255), InnerBevel), 1.1f))
                    g.DrawRectangle(inner, x + 1.5f, y + 1.5f, w - 3f, h - 3f);
            }

            // Outer hammered antique-gold border
            using (Pen border = new Pen(Color.FromArgb(Clamp(alpha * 225 / 255, 0, 255), AntiqueGold), 1.2f))
                g.DrawRectangle(border, x, y, w, h);

            if (cornerDiamonds && w >= 16f && h >= 14f)
            {
                float r = (float)Math.Max(2.0, 2.7 * scale);
                DrawCornerDiamond(g, x, y, r, alpha);
                DrawCornerDiamond(g, x + w, y, r, alpha);
                DrawCornerDiamond(g, x, y + h, r, alpha);
                DrawCornerDiamond(g, x + w, y + h, r, alpha);
            }
        }

        private static void DrawCornerDiamond(Graphics g, float cx, float cy, float r, int alpha)
        {
            PointF[] pts = new PointF[]
            {
                new PointF(cx, cy - r),
                new PointF(cx + r, cy),
                new PointF(cx, cy + r),
                new PointF(cx - r, cy)
            };
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(Clamp(alpha * 235 / 255, 0, 255), CornerDiamond)))
                g.FillPolygon(fill, pts);
            using (Pen rim = new Pen(Color.FromArgb(Clamp(alpha * 230 / 255, 0, 255), InnerBevel), 0.9f))
                g.DrawPolygon(rim, pts);
        }

        private static void DrawShadowText(Graphics g, string text, Font font, Color color, int alpha, float x, float y, bool heavyShadow)
        {
            if (string.IsNullOrEmpty(text)) return;
            int shadowAlpha = Clamp(alpha * 235 / 255, 0, 255);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(shadowAlpha, 6, 4, 3)))
            {
                if (heavyShadow)
                {
                    g.DrawString(text, font, shadow, x - 1f, y);
                    g.DrawString(text, font, shadow, x + 1f, y);
                    g.DrawString(text, font, shadow, x, y - 1f);
                    g.DrawString(text, font, shadow, x, y + 1f);
                    g.DrawString(text, font, shadow, x + 1f, y + 1f);
                }
                else
                {
                    g.DrawString(text, font, shadow, x + 1f, y + 1f);
                }
            }
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, color)))
                g.DrawString(text, font, brush, x, y);
        }

        /// <summary>
        /// The damage meter, in the style of the TurboHUD one: the total
        /// damage of the group, the damage per second and one line per player
        /// with their share.
        /// </summary>
        private void DrawDamageMeter(Graphics g, int width, int height, double scale, int alpha)
        {
            ActorSnapshot actors = _actors;
            if (actors == null || !actors.DamageActive) return;
            if (actors.DamageEntries == null || actors.DamageEntries.Count == 0) return;

            Font headFont = FontFor((float)(10f * scale), true);
            Font rowFont = FontFor((float)(9.5f * scale), false);

            float rowHeight = (float)(23 * scale);
            float pad = (float)(8 * scale);
            float barHeight = (float)(3.5 * scale);

            int rows = actors.DamageEntries.Count;
            float headHeight = (float)(20 * scale);
            float panelHeight = headHeight + rows * rowHeight + rowHeight + pad * 1.8f;
            float panelWidth = (float)(240 * scale);

            float x = (float)(14 * scale);
            float y = (float)(130 * scale);

            DrawGothicPanel(g, x, y, panelWidth, panelHeight, alpha, scale, true);

            float textX = x + pad;
            float textY = y + pad;

            DrawShadowText(g, "Урон группы", headFont, ParchmentText, alpha, textX, textY, false);

            string total = FormatDamage(actors.DamageTotal);
            SizeF totalSize = g.MeasureString(total, headFont);
            DrawShadowText(g, total, headFont, GoldHighlight, alpha, x + panelWidth - pad - totalSize.Width, textY, false);

            float dividerY = textY + headHeight - (float)(3 * scale);
            using (Pen div = new Pen(Color.FromArgb(Clamp(alpha * 160 / 255, 0, 255), InnerBevel), 1f))
                g.DrawLine(div, textX, dividerY, x + panelWidth - pad, dividerY);

            textY += headHeight;

            for (int i = 0; i < rows; i++)
            {
                DamageEntry entry = actors.DamageEntries[i];
                Color color = entry.IsLocalPlayer
                    ? Color.FromArgb(255, 214, 96)
                    : Color.FromArgb(148, 198, 248);
                Color barEnd = entry.IsLocalPlayer
                    ? Color.FromArgb(212, 138, 36)
                    : Color.FromArgb(84, 142, 210);

                DrawShadowText(g, entry.Name, rowFont, color, alpha, textX, textY, false);

                string value = FormatDamage(entry.Damage);
                SizeF size = g.MeasureString(value, rowFont);
                DrawShadowText(g, value, rowFont, IvoryText, alpha, x + panelWidth - pad - size.Width, textY, false);

                // Framed share bar under the name.
                float fullBarW = panelWidth - pad * 2;
                float barY = textY + (float)(15f * scale);
                using (SolidBrush trough = new SolidBrush(Color.FromArgb(Clamp(alpha * 180 / 255, 0, 255), 8, 6, 5)))
                    g.FillRectangle(trough, textX, barY, fullBarW, barHeight);

                float barWidth = fullBarW * (float)Math.Max(0, Math.Min(1, entry.Share));
                if (barWidth > 0.8f)
                {
                    using (LinearGradientBrush bar = new LinearGradientBrush(
                        new RectangleF(textX, barY, Math.Max(2f, barWidth), barHeight),
                        Color.FromArgb(Clamp(alpha * 220 / 255, 0, 255), barEnd),
                        Color.FromArgb(Clamp(alpha * 235 / 255, 0, 255), color),
                        LinearGradientMode.Horizontal))
                    {
                        g.FillRectangle(bar, textX, barY, barWidth, barHeight);
                    }
                }

                textY += rowHeight;
            }

            string dps = "Урон/с: " + FormatDamage(actors.DamageDps);
            DrawShadowText(g, dps, rowFont, MutedStoneText, alpha, textX, textY, false);
        }

        /// <summary>A short number: 1 234 567 890 becomes "1.23 Млрд".</summary>
        private static string FormatDamage(double value)
        {
            if (value >= 1000000000d) return (value / 1000000000d).ToString("0.00", CultureInfo.InvariantCulture) + " Млрд";
            if (value >= 1000000d) return (value / 1000000d).ToString("0.00", CultureInfo.InvariantCulture) + " М";
            if (value >= 1000d) return (value / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + " К";
            return ((int)Math.Round(value)).ToString();
        }

        private void DrawRiftProgress(Graphics g, int width, int height, double scale, int alpha)
        {
            ActorSnapshot actors = _actors;
            if (actors == null) return;
            if (actors.SpecialArea <= 0 && actors.RiftPercentage <= 0) return;

            float barW = (float)(264 * scale);
            float barH = (float)(24 * scale);
            float x = width - barW - (float)(30 * scale);
            float y = (float)(280 * scale);

            double pct = _hasDisplayRift ? _displayRiftPct : Math.Min(100.0, Math.Max(0.0, actors.RiftPercentage));
            pct = Math.Min(100.0, Math.Max(0.0, pct));
            bool isGR = actors.SpecialArea == 2;
            bool guardianReady = pct >= 99.9;
            string title = isGR ? "ВП" : "Портал";
            string text = string.Format("{0}: {1:F1}%", title, pct);

            // Golden pulse glow at 100% (Rift Guardian)
            if (guardianReady)
            {
                double pulse = 0.5 + 0.5 * Math.Sin(_currentAnimSec * 5.2);
                int glowAlpha = Clamp((int)Math.Round(alpha * (0.18 + 0.28 * pulse)), 0, 255);
                using (Pen outerGlow = new Pen(Color.FromArgb(glowAlpha, 255, 206, 84), (float)(5.0 * scale)))
                    g.DrawRectangle(outerGlow, x - 2f, y - 2f, barW + 4f, barH + 4f);
            }

            DrawGothicPanel(g, x, y, barW, barH, alpha, scale, true);

            float inset = (float)(3.0 * scale);
            float trackX = x + inset;
            float trackY = y + inset;
            float trackW = Math.Max(1f, barW - inset * 2f);
            float trackH = Math.Max(1f, barH - inset * 2f);
            float fillW = trackW * (float)(pct / 100.0);

            if (fillW > 0.5f)
            {
                Color c1 = guardianReady
                    ? Color.FromArgb(alpha, 198, 134, 24)
                    : (isGR ? Color.FromArgb(alpha, 114, 34, 188) : Color.FromArgb(alpha, 168, 100, 18));
                Color c2 = guardianReady
                    ? Color.FromArgb(alpha, 255, 218, 96)
                    : (isGR ? Color.FromArgb(alpha, 188, 92, 246) : Color.FromArgb(alpha, 244, 188, 64));

                RectangleF fullTrack = new RectangleF(trackX, trackY, trackW, trackH);
                RectangleF fillRect = new RectangleF(trackX, trackY, fillW, trackH);

                using (LinearGradientBrush fill = new LinearGradientBrush(fullTrack, c1, c2, LinearGradientMode.Horizontal))
                    g.FillRectangle(fill, fillRect);

                // Top specular gloss strip
                float glossH = Math.Max(1f, trackH * 0.42f);
                using (SolidBrush gloss = new SolidBrush(Color.FromArgb(Clamp(alpha * 42 / 255, 0, 255), 255, 244, 214)))
                    g.FillRectangle(gloss, trackX, trackY, fillW, glossH);

                // Animated golden/arcane sheen sweep across the filled bar
                GraphicsState savedClip = g.Save();
                try
                {
                    g.SetClip(fillRect);
                    float sweepSpan = trackW * 0.34f;
                    double phase = (_currentAnimSec * 0.55 + 0.35) % 1.0;
                    float sweepX = trackX - sweepSpan + (float)(phase * (fillW + sweepSpan * 2f));
                    RectangleF sweepRect = new RectangleF(sweepX, trackY, sweepSpan, trackH);
                    Color sheenPeak = isGR && !guardianReady
                        ? Color.FromArgb(Clamp(alpha * 95 / 255, 0, 255), 232, 196, 255)
                        : Color.FromArgb(Clamp(alpha * 110 / 255, 0, 255), 255, 236, 168);

                    using (LinearGradientBrush sheen = new LinearGradientBrush(
                        sweepRect, Color.FromArgb(0, sheenPeak), Color.FromArgb(0, sheenPeak), LinearGradientMode.Horizontal))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        cb.Colors = new Color[]
                        {
                            Color.FromArgb(0, sheenPeak),
                            sheenPeak,
                            Color.FromArgb(0, sheenPeak)
                        };
                        sheen.InterpolationColors = cb;
                        g.FillRectangle(sheen, sweepRect);
                    }
                }
                finally
                {
                    g.Restore(savedClip);
                }

                // Bright leading edge notch
                using (Pen tip = new Pen(Color.FromArgb(Clamp(alpha * 220 / 255, 0, 255), 255, 236, 178), 1.2f))
                    g.DrawLine(tip, trackX + fillW, trackY, trackX + fillW, trackY + trackH);
            }

            if (guardianReady)
            {
                double pulse = 0.5 + 0.5 * Math.Sin(_currentAnimSec * 5.2);
                int rimAlpha = Clamp((int)Math.Round(alpha * (0.55 + 0.45 * pulse)), 0, 255);
                using (Pen pulseBorder = new Pen(Color.FromArgb(rimAlpha, ReadyFlashGold), 1.5f))
                    g.DrawRectangle(pulseBorder, x, y, barW, barH);
            }

            Font font = FontFor((float)(10f * scale), true);
            SizeF sz = g.MeasureString(text, font);
            Color textColor = guardianReady ? ReadyFlashGold : IvoryText;
            DrawShadowText(g, text, font, textColor, alpha,
                x + (barW - sz.Width) * 0.5f, y + (barH - sz.Height) * 0.5f, true);
        }

        private void DrawBuffs(Graphics g, int width, int height, double scale, int alpha)
        {
            BuffSnapshot snapshot = _buffs;
            if (snapshot == null) return;

            List<BuffEntry> active = new List<BuffEntry>();
            foreach (BuffEntry entry in snapshot.Buffs)
            {
                if (!entry.Active) continue;
                // Internal game flags share the buff attribute space; showing
                // them would bury the real buffs under bookkeeping entries.
                if (BuffPowers.IsInternal(entry.PowerSno)) continue;
                active.Add(entry);
            }
            if (active.Count == 0) return;

            Font nameFont = FontFor((float)(9f * scale), false);

            float lineHeight = (float)(17 * scale);
            int maxLines = 24;
            int count = Math.Min(active.Count, maxLines);

            float boxWidth = 0;
            for (int i = 0; i < count; i++)
            {
                string text = LineOf(active[i]);
                SizeF size = g.MeasureString(text, nameFont);
                if (size.Width > boxWidth) boxWidth = size.Width;
            }
            float padX = (float)(9 * scale);
            float padY = (float)(6 * scale);
            float textIndent = (float)(12 * scale);
            float panelW = boxWidth + padX * 2f + textIndent;
            float panelH = count * lineHeight + padY * 2f;

            float x = _settings.BuffCorner == 0 || _settings.BuffCorner == 2
                ? _settings.BuffOffsetX
                : width - panelW - _settings.BuffOffsetX;
            float y = _settings.BuffCorner < 2
                ? _settings.BuffOffsetY
                : height - _settings.BuffOffsetY - panelH;

            DrawGothicPanel(g, x, y, panelW, panelH, alpha, scale, false);

            float contentX = x + padX;
            float contentY = y + padY;
            float diamondR = (float)(2.8 * scale);

            for (int i = 0; i < count; i++)
            {
                BuffEntry entry = active[i];
                float lineY = contentY + i * lineHeight;
                float cy = lineY + lineHeight * 0.45f;
                float cx = contentX + diamondR;

                PointF[] diamond = new PointF[]
                {
                    new PointF(cx, cy - diamondR),
                    new PointF(cx + diamondR, cy),
                    new PointF(cx, cy + diamondR),
                    new PointF(cx - diamondR, cy)
                };
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(alpha, GoldHighlight)))
                    g.FillPolygon(dot, diamond);
                using (Pen rim = new Pen(Color.FromArgb(alpha, InnerBevel), 0.8f))
                    g.DrawPolygon(rim, diamond);

                string text = LineOf(entry);
                DrawShadowText(g, text, nameFont, IvoryText, alpha, contentX + textIndent, lineY, false);
            }
        }

        private static string LineOf(BuffEntry entry)
        {
            string text = entry.Name;
            if (!string.IsNullOrEmpty(entry.Element)) text = text + " · " + entry.Element;
            if (entry.Stacks > 1) text = text + "  x" + entry.Stacks;
            return text;
        }

        private void DrawSkills(Graphics g, int width, int height, double scale, int alpha)
        {
            SkillSnapshot snapshot = _skills;
            if (snapshot == null) return;

            List<SkillCooldown> slots = snapshot.Skills;
            if (slots == null || slots.Count == 0) return;

            // Only slots that actually have a known cooldown are drawn. A row of
            // empty boxes tells the player nothing and looks like broken data.
            List<SkillCooldown> shown = new List<SkillCooldown>();
            for (int i = 0; i < slots.Count; i++)
            {
                SkillCooldown slot = slots[i];
                if (slot.DurationSeconds > 0.001 || slot.RemainingSeconds > 0.001)
                    shown.Add(slot);
            }
            if (shown.Count == 0) return;

            float box = (float)(46 * scale);
            float gap = (float)(7 * scale);
            float totalWidth = shown.Count * box + (shown.Count - 1) * gap;
            float startX = (width - totalWidth) / 2f;
            float startY = height - box - (float)(52 * scale);

            Font keyFont = FontFor((float)(10f * scale), true);
            Font smallKeyFont = FontFor((float)(7.5f * scale), true);
            Font timerFont = FontFor((float)(10.5f * scale), true);

            for (int i = 0; i < shown.Count; i++)
            {
                SkillCooldown slot = shown[i];
                float x = startX + i * (box + gap);

                SkillAnimState st;
                _skillAnim.TryGetValue(slot.Slot, out st);

                bool isReady = slot.IsReady;
                double rem = st != null && st.Initialized ? st.DisplayRemaining : slot.RemainingSeconds;
                double dur = slot.DurationSeconds > 0.001 ? slot.DurationSeconds : (st != null ? st.LastSnapshotDuration : 0.0);
                double frac = !isReady && dur > 0.001
                    ? Math.Min(1.0, Math.Max(0.0, rem / dur))
                    : (!isReady ? Math.Min(1.0, Math.Max(0.0, slot.Fraction)) : 0.0);

                // Ready-flash pulse (350ms) when transitioning from on-cooldown to ready
                double flashK = 0.0;
                if (isReady && st != null)
                {
                    double flashAge = _currentAnimSec - st.ReadyFlashStartSec;
                    if (flashAge >= 0.0 && flashAge <= ReadyFlashDurationSec)
                        flashK = Math.Sin((flashAge / ReadyFlashDurationSec) * Math.PI);
                }

                // Gothic dark-bronze beveled slot background
                RectangleF slotRect = new RectangleF(x, startY, box, box);
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    slotRect,
                    Color.FromArgb(Clamp(alpha * 228 / 255, 0, 255), ObsidianTop),
                    Color.FromArgb(Clamp(alpha * 242 / 255, 0, 255), ObsidianBottom),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, slotRect);
                }

                // Smooth continuous cooldown sweep (from bottom up)
                if (!isReady && frac > 0.001)
                {
                    float fill = (float)frac * (box - 4f);
                    float sweepTop = startY + box - 2f - fill;
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(Clamp(alpha * 165 / 255, 0, 255), 42, 24, 14)))
                        g.FillRectangle(shade, x + 2f, sweepTop, box - 4f, fill);
                    using (Pen edge = new Pen(Color.FromArgb(Clamp(alpha * 210 / 255, 0, 255), GoldHighlight), 1.3f))
                        g.DrawLine(edge, x + 2f, sweepTop, x + box - 2f, sweepTop);
                }

                // Golden ready-flash interior wash
                if (flashK > 0.001)
                {
                    int washA = Clamp((int)Math.Round(alpha * 0.42 * flashK), 0, 255);
                    using (SolidBrush wash = new SolidBrush(Color.FromArgb(washA, ReadyFlashGold)))
                        g.FillRectangle(wash, x + 2f, startY + 2f, box - 4f, box - 4f);
                }

                // Inner bronze bevel + outer hammered antique-gold border
                using (Pen inner = new Pen(Color.FromArgb(Clamp(alpha * 220 / 255, 0, 255), InnerBevel), 1.1f))
                    g.DrawRectangle(inner, x + 1.5f, startY + 1.5f, box - 3f, box - 3f);

                Color outerCol = isReady ? AntiqueGold : Color.FromArgb(108, 84, 48);
                using (Pen outer = new Pen(Color.FromArgb(Clamp(alpha * 230 / 255, 0, 255), outerCol), 1.3f))
                    g.DrawRectangle(outer, x, startY, box, box);

                // Golden ready-flash outer aura
                if (flashK > 0.001)
                {
                    int auraA = Clamp((int)Math.Round(alpha * flashK), 0, 255);
                    using (Pen aura = new Pen(Color.FromArgb(auraA, ReadyFlashGold), (float)(2.4 * scale)))
                        g.DrawRectangle(aura, x - 1f, startY - 1f, box + 2f, box + 2f);
                }

                string key = slot.Key;
                if (string.IsNullOrEmpty(key)) key = (slot.Slot + 1).ToString();

                if (isReady)
                {
                    SizeF keySize = g.MeasureString(key, keyFont);
                    Color kc = flashK > 0.001 ? ReadyFlashGold : IvoryText;
                    DrawShadowText(g, key, keyFont, kc, alpha,
                        x + (box - keySize.Width) * 0.5f, startY + (box - keySize.Height) * 0.5f, true);
                }
                else
                {
                    SizeF keySize = g.MeasureString(key, smallKeyFont);
                    DrawShadowText(g, key, smallKeyFont, MutedStoneText, alpha,
                        x + (box - keySize.Width) * 0.5f, startY + (float)(3 * scale), false);

                    string text = rem >= 1.0
                        ? ((int)Math.Ceiling(rem)).ToString()
                        : Math.Max(0.1, rem).ToString("0.0", CultureInfo.InvariantCulture);
                    SizeF size = g.MeasureString(text, timerFont);
                    DrawShadowText(g, text, timerFont, ReadyFlashGold, alpha,
                        x + (box - size.Width) * 0.5f, startY + (box - size.Height) * 0.5f + (float)(4 * scale), true);
                }
            }
        }

        private void DrawHealth(Graphics g, int width, int height, double scale, int alpha)
        {
            SkillSnapshot snapshot = _skills;
            if (snapshot == null || snapshot.Health < 0) return;

            Font font = FontFor((float)(10f * scale), true);
            string text = "Здоровье: " + ((int)Math.Round(snapshot.Health)).ToString();

            SizeF size = g.MeasureString(text, font);
            float padX = (float)(10 * scale);
            float padY = (float)(4 * scale);
            float panelW = size.Width + padX * 2f;
            float panelH = size.Height + padY * 2f;
            float x = (width - panelW) * 0.5f;
            float y = height - (float)(164 * scale);

            DrawGothicPanel(g, x, y, panelW, panelH, alpha, scale, false);
            DrawShadowText(g, text, font, Color.FromArgb(236, 122, 108), alpha, x + padX, y + padY, true);
        }

        private void DrawInventory(Graphics g, int width, int height, double scale, int alpha)
        {
            InventorySnapshot snapshot = _inventory;
            if (snapshot == null || snapshot.TotalSlots <= 0) return;

            Font font = FontFor((float)(9f * scale), false);
            string text = "Рюкзак " + snapshot.UsedSlots + " / " + snapshot.TotalSlots;

            SizeF size = g.MeasureString(text, font);
            float padX = (float)(9 * scale);
            float padY = (float)(4 * scale);
            float panelW = size.Width + padX * 2f;
            float panelH = size.Height + padY * 2f;
            float x = width - panelW - (float)(16 * scale);
            float y = (float)(14 * scale);

            DrawGothicPanel(g, x, y, panelW, panelH, alpha, scale, false);
            DrawShadowText(g, text, font, ParchmentText, alpha, x + padX, y + padY, false);
        }

        private Font FontFor(float size, bool bold)
        {
            string key = size.ToString("0.0") + (bold ? "b" : "n");
            Font font;
            if (_fonts.TryGetValue(key, out font)) return font;
            font = new Font("Segoe UI", Math.Max(6f, size), bold ? FontStyle.Bold : FontStyle.Regular);
            _fonts[key] = font;
            return font;
        }

        // ------------------------------------------------------------------
        // Layered output (persistent reusable DIB surface)
        // ------------------------------------------------------------------

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

                // GDI+ writes premultiplied alpha directly into the layered
                // DIB memory, avoiding per-frame allocations and CopyMemory.
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
            ReleaseNativeSurface();
        }

        private void ReleaseNativeSurface()
        {
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

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Font font in _fonts.Values)
                {
                    try { font.Dispose(); }
                    catch { }
                }
                _fonts.Clear();
                if (_surface != null)
                {
                    try { _surface.Dispose(); }
                    catch { }
                    _surface = null;
                }
            }
            ReleaseNativeSurface();
            base.Dispose(disposing);
        }
    }
}
