using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(18, 15, 14);
        public static readonly Color Panel = Color.FromArgb(27, 23, 20);
        public static readonly Color Panel2 = Color.FromArgb(37, 31, 27);
        public static readonly Color Border = Color.FromArgb(74, 58, 40);
        public static readonly Color Text = Color.FromArgb(240, 234, 222);
        public static readonly Color MutedColor = Color.FromArgb(168, 154, 136);
        public static readonly Color Accent = Color.FromArgb(218, 158, 56);
        public static readonly Color AccentHover = Color.FromArgb(242, 186, 84);
        public static readonly Color DangerColor = Color.FromArgb(204, 68, 58);
        public static readonly Color Ok = Color.FromArgb(102, 196, 118);

        public static Font FontBody = new Font("Segoe UI", 9f);
        public static Font FontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static Font FontTitle = new Font("Segoe UI", 17f, FontStyle.Bold);
        public static Font FontSection = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static Font FontMono = new Font("Consolas", 9.5f);

        private static void AttachCardBorder(Control card)
        {
            if (card == null) return;
            SetDoubleBuffered(card);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                int w = card.ClientSize.Width;
                int h = card.ClientSize.Height;
                if (w < 4 || h < 4) return;
                using (Pen border = new Pen(Color.FromArgb(92, 72, 46), 1f))
                    e.Graphics.DrawRectangle(border, 0, 0, w - 1, h - 1);
                using (Pen top = new Pen(Color.FromArgb(38, 218, 158, 56), 1f))
                    e.Graphics.DrawLine(top, 1, 1, w - 2, 1);
            };
            card.Resize += delegate { card.Invalidate(); };
        }

        public static Panel Card()
        {
            Panel p = new Panel();
            p.BackColor = Panel;
            p.Padding = new Padding(16);
            p.Margin = new Padding(0, 0, 0, 12);
            AttachCardBorder(p);
            return p;
        }

        public static Label Title(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontTitle;
            l.ForeColor = Text;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Section(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontSection;
            l.ForeColor = Text;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Body(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontBody;
            l.ForeColor = Text;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Label Muted(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontBody;
            l.ForeColor = MutedColor;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static Button Primary(string text, EventHandler onClick)
        {
            return MakeButton(text, onClick, Accent, Color.FromArgb(26, 20, 12), AccentHover);
        }

        public static Button Secondary(string text, EventHandler onClick)
        {
            return MakeButton(text, onClick, Panel2, Text, Border);
        }

        public static Button Danger(string text, EventHandler onClick)
        {
            return MakeButton(text, onClick, Color.FromArgb(68, 28, 26), Color.FromArgb(255, 206, 198), DangerColor);
        }

        private static Button MakeButton(string text, EventHandler onClick, Color back, Color fore, Color hover)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = FontBold;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = hover;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Padding = new Padding(10, 0, 10, 0);
            b.Height = 32;
            b.AutoSize = true;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            if (onClick != null) b.Click += onClick;
            b.MouseEnter += delegate { b.BackColor = hover; };
            b.MouseLeave += delegate { b.BackColor = back; };
            return b;
        }

        public static TextBox Input()
        {
            TextBox t = new TextBox();
            t.Font = FontBody;
            t.BackColor = Panel2;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Height = 28;
            return t;
        }

        public static CheckBox Check(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = FontBody;
            c.ForeColor = Text;
            c.BackColor = Color.Transparent;
            c.AutoSize = true;
            c.FlatStyle = FlatStyle.Flat;
            c.FlatAppearance.BorderColor = Border;
            c.FlatAppearance.CheckedBackColor = Accent;
            c.UseVisualStyleBackColor = false;
            c.Padding = new Padding(2, 2, 2, 2);
            return c;
        }

        /// <summary>
        /// Dark-theme check box with a clearly visible mark. The default flat
        /// style renders an empty square on dark backgrounds.
        /// </summary>
        public class DarkCheckBox : CheckBox
        {
            public DarkCheckBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AutoSize = true;
                ForeColor = Ui.Text;
                BackColor = Color.Transparent;
                Font = Ui.FontBody;
                Cursor = Cursors.Hand;
                Padding = new Padding(0);
                Margin = new Padding(0, 3, 12, 3);
            }

            private const int BoxSize = 15;

            public override Size GetPreferredSize(Size proposedSize)
            {
                Size textSize = TextRenderer.MeasureText(Text, Font);
                return new Size(BoxSize + 7 + textSize.Width + 2, Math.Max(BoxSize + 4, textSize.Height + 2));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Ui.Panel);

                Rectangle box = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);
                bool hot = ClientRectangle.Contains(PointToClient(Cursor.Position));

                using (SolidBrush fill = new SolidBrush(Checked ? Ui.Accent : (hot ? Ui.Panel2 : Color.FromArgb(44, 36, 30))))
                using (Pen border = new Pen(Checked ? Ui.AccentHover : (hot ? Ui.Accent : Ui.Border)))
                {
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(border, box);
                }

                if (Checked)
                {
                    using (Pen mark = new Pen(Color.FromArgb(28, 22, 12), 2f))
                    {
                        mark.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        mark.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        Point[] pts =
                        {
                            new Point(box.Left + 3, box.Top + 8),
                            new Point(box.Left + 6, box.Top + 11),
                            new Point(box.Left + 12, box.Top + 4)
                        };
                        g.DrawLines(mark, pts);
                    }
                }

                Color fore = Enabled ? ForeColor : Ui.MutedColor;
                Rectangle textRect = new Rectangle(box.Right + 7, 0, Width - box.Right - 7, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            protected override void OnMouseEnter(EventArgs eventargs)
            {
                base.OnMouseEnter(eventargs);
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs eventargs)
            {
                base.OnMouseLeave(eventargs);
                Invalidate();
            }

            protected override void OnCheckedChanged(EventArgs e)
            {
                base.OnCheckedChanged(e);
                Invalidate();
            }
        }

        public static CheckBox DarkCheck(string text)
        {
            DarkCheckBox c = new DarkCheckBox();
            c.Text = text;
            return c;
        }

        public static ComboBox Combo()
        {
            ComboBox c = new ComboBox();
            c.Font = new Font("Segoe UI", 10f);
            c.BackColor = Panel2;
            c.ForeColor = Text;
            c.FlatStyle = FlatStyle.Flat;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 20;
            c.IntegralHeight = false;
            c.DropDownHeight = 260;
            c.DrawItem += DrawComboItem;
            return c;
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Color.FromArgb(70, 55, 30) : Panel2;
            using (SolidBrush brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index < 0) return;
            string text = Convert.ToString(combo.Items[e.Index]);
            using (SolidBrush textBrush = new SolidBrush(Text))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                Rectangle r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                e.Graphics.DrawString(text, combo.Font, textBrush, r, sf);
            }
        }

        public static FlowLayoutPanel Toolbar()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = true;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.BackColor = Color.Transparent;
            f.Padding = new Padding(0);
            f.Margin = new Padding(0);
            return f;
        }

        /// <summary>
        /// A row of buttons with a fixed height. A flow panel with auto-size
        /// measures itself unreliably inside a table layout, so the buttons are
        /// placed explicitly: the row always takes exactly one line.
        /// </summary>
        public static Panel Buttons(params Button[] items)
        {
            Panel bar = new Panel();
            bar.Height = 34;
            bar.BackColor = Color.Transparent;
            bar.Margin = new Padding(0, 2, 0, 2);

            int x = 0;
            foreach (Button b in items)
            {
                Size size = b.PreferredSize;
                b.Size = size;
                b.Location = new Point(x, (bar.Height - size.Height) / 2);
                bar.Controls.Add(b);
                x += size.Width + 8;
            }
            bar.Width = Math.Max(1, x);
            return bar;
        }

        /// <summary>Stretches every card inside a top-down flow panel to its width.</summary>
        public static void FitCards(FlowLayoutPanel flow, int minWidth)
        {
            if (flow == null) return;
            int scrollbar = flow.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth + 4 : 0;
            int width = flow.ClientSize.Width - flow.Padding.Horizontal - 20 - scrollbar;
            if (width < minWidth) width = minWidth;
            foreach (Control c in flow.Controls)
            {
                if (c.Width != width) c.Width = width;
            }
        }

        public static void StyleListView(ListView lv)
        {
            lv.BackColor = Panel2;
            lv.ForeColor = Text;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.Font = FontBody;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.MultiSelect = false;
            lv.View = View.Details;
            lv.OwnerDraw = false;
            SetDoubleBuffered(lv);
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Panel2;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Border;
            g.Font = FontBody;
            g.ForeColor = Text;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Panel;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            g.ColumnHeadersDefaultCellStyle.Font = FontBold;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.BackColor = Panel2;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 55, 30);
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.RowHeadersVisible = false;
            g.AllowUserToResizeRows = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }

        public static void SetDoubleBuffered(Control c)
        {
            try
            {
                PropertyInfo p = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (p != null) p.SetValue(c, true, null);
            }
            catch { }
        }

        /// <summary>Attaches a hover hint to a control.</summary>
        public static void Tip(Control control, string text)
        {
            if (control == null || string.IsNullOrEmpty(text)) return;
            ToolTip tip = new ToolTip();
            tip.InitialDelay = 350;
            tip.ReshowDelay = 100;
            tip.AutoPopDelay = 12000;
            tip.ShowAlways = true;
            tip.SetToolTip(control, text);
        }

        /// <summary>
        /// A compact status pill: a rounded label with a colored dot.
        /// </summary>
        public static Label Pill(string text, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FontBold;
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            l.Padding = new Padding(0, 2, 0, 2);
            return l;
        }

        /// <summary>
        /// The application icon. The icon embedded into the executable is used
        /// when present, so the window, the taskbar and the shortcut all show
        /// the same picture; otherwise a small icon is drawn at runtime.
        /// </summary>
        public static Icon CreateAppIcon()
        {
            try
            {
                Icon embedded = LoadEmbeddedIcon();
                if (embedded != null) return embedded;
            }
            catch { }
            return DrawAppIcon();
        }

        private static Icon LoadEmbeddedIcon()
        {
            try
            {
                string exe = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
                Icon icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null && icon.Width > 0) return icon;
            }
            catch { }
            return null;
        }

        private static Icon DrawAppIcon()
        {
            using (Bitmap bmp = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int r = 14;
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(63 - r * 2, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(63 - r * 2, 63 - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(0, 63 - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    using (SolidBrush back = new SolidBrush(Color.FromArgb(22, 18, 16)))
                        g.FillPath(back, path);
                    using (Pen border = new Pen(Accent, 3f))
                        g.DrawPath(border, path);
                }
                using (Font f = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush text = new SolidBrush(Accent))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("D3", f, text, new RectangleF(0, 0, 64, 64), sf);
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        public static Panel Header(string title, string subtitle)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 64;
            p.BackColor = Color.Transparent;
            Label t = Title(title);
            t.Location = new Point(0, 0);
            Label s = Muted(subtitle);
            s.Location = new Point(2, 34);
            p.Controls.Add(t);
            p.Controls.Add(s);
            p.Paint += delegate(object sender, PaintEventArgs e)
            {
                int w = p.ClientSize.Width;
                int h = p.ClientSize.Height;
                if (w < 12 || h < 12) return;
                int lineY = Math.Min(h - 2, 58);
                Rectangle lineRect = new Rectangle(0, lineY, w, 1);
                using (System.Drawing.Drawing2D.LinearGradientBrush sep = new System.Drawing.Drawing2D.LinearGradientBrush(
                    lineRect,
                    Color.FromArgb(165, Accent),
                    Color.FromArgb(0, Border),
                    System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                {
                    System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(3);
                    cb.Positions = new float[] { 0f, 0.45f, 1f };
                    cb.Colors = new Color[]
                    {
                        Color.FromArgb(165, Accent),
                        Color.FromArgb(110, Border),
                        Color.FromArgb(0, Border)
                    };
                    sep.InterpolationColors = cb;
                    e.Graphics.FillRectangle(sep, lineRect);
                }
            };
            p.Resize += delegate { p.Invalidate(); };
            return p;
        }

        // ------------------------------------------------------------------
        // Layout helpers used by the settings pages
        // ------------------------------------------------------------------

        /// <summary>
        /// A vertical stack with a single full-width column. Rows are added by
        /// the caller, which keeps the content aligned and easy to read.
        /// </summary>
        public static TableLayoutPanel Stack()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.RowCount = 0;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.BackColor = Color.Transparent;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            return t;
        }

        /// <summary>
        /// A vertical page body: a scrollable host with a single auto-sized
        /// column of cards. Every card grows with its content, so nothing is
        /// clipped and no fixed pixel heights are needed.
        /// </summary>
        public static TableLayoutPanel PageBody(out Panel host)
        {
            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Color.Transparent;
            host.AutoScroll = true;
            host.Padding = new Padding(0, 12, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel();
            body.ColumnCount = 1;
            body.RowCount = 0;
            body.AutoSize = true;
            body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            body.Dock = DockStyle.Top;
            body.BackColor = Color.Transparent;
            body.Margin = new Padding(0);
            body.Padding = new Padding(0);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.Controls.Add(body);
            return body;
        }

        /// <summary>Adds an auto-sized card row to a page body.</summary>
        public static void CardRow(TableLayoutPanel body, Control card)
        {
            int index = body.RowCount;
            body.RowCount = index + 1;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Margin = new Padding(0, 0, 0, 12);
            card.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            body.Controls.Add(card, 0, index);
        }

        /// <summary>Adds an auto-sized row to a stack.</summary>
        public static void Row(TableLayoutPanel stack, Control control)
        {
            Row(stack, control, Stretches(control));
        }

        /// <summary>Adds an auto-sized row with an explicit stretch flag.</summary>
        public static void Row(TableLayoutPanel stack, Control control, bool stretch)
        {
            int index = stack.RowCount;
            stack.RowCount = index + 1;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 0, 0, 8);
            control.Anchor = stretch
                ? (AnchorStyles.Left | AnchorStyles.Right)
                : AnchorStyles.Left;
            stack.Controls.Add(control, 0, index);
        }

        /// <summary>
        /// True for controls that should fill the whole row width. Small
        /// controls keep their natural size, otherwise they look stretched.
        /// </summary>
        private static bool Stretches(Control control)
        {
            if (control is CheckBox) return false;
            if (control is RadioButton) return false;
            if (control is Button) return false;
            if (control is NumericUpDown) return false;
            if (control is ComboBox) return false;
            if (control is TextBox) return false;
            return true;
        }

        /// <summary>Adds a row that absorbs the remaining height.</summary>
        public static void FillRow(TableLayoutPanel stack, Control control)
        {
            int index = stack.RowCount;
            stack.RowCount = index + 1;
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            control.Margin = new Padding(0);
            stack.Controls.Add(control, 0, index);
        }

        /// <summary>
        /// A muted explanation label that wraps to the width of its parent.
        /// </summary>
        public static Label Hint(string text)
        {
            Label l = Muted(text);
            l.AutoSize = true;
            l.MaximumSize = new Size(2000, 0);
            l.Margin = new Padding(0, 0, 0, 6);
            l.Tag = "hint";
            return l;
        }

        /// <summary>
        /// A colored state line, e.g. "• Игра запущена".
        /// </summary>
        public static Label State(string text, Color color)
        {
            Label l = Body(text);
            l.ForeColor = color;
            l.AutoSize = true;
            return l;
        }

        public static void SetState(Label label, string text, Color color)
        {
            if (label == null) return;
            label.Text = text;
            label.ForeColor = color;
        }

        /// <summary>
        /// A short caption above a control, e.g. "Масштаб".
        /// </summary>
        public static Label Field(string text)
        {
            Label l = Muted(text);
            l.AutoSize = true;
            l.Margin = new Padding(0, 2, 0, 2);
            return l;
        }

        /// <summary>
        /// A thin separator line.
        /// </summary>
        public static Panel Divider()
        {
            Panel p = new Panel();
            p.Height = 1;
            p.BackColor = Border;
            p.Margin = new Padding(0, 4, 0, 10);
            p.Dock = DockStyle.Top;
            return p;
        }

        /// <summary>
        /// Sizes every card in a top-down flow to its content, so a card never
        /// leaves a large empty area at the bottom or clips its last line.
        /// </summary>
        public static void FitCardHeights(FlowLayoutPanel flow)
        {
            if (flow == null) return;
            foreach (Control control in flow.Controls)
            {
                Panel card = control as Panel;
                if (card == null || !card.AutoSize) continue;
                int width = card.ClientSize.Width - card.Padding.Horizontal;
                if (width < 50) continue;
                foreach (Control child in card.Controls)
                {
                    TableLayoutPanel root = child as TableLayoutPanel;
                    if (root == null) continue;
                    root.MaximumSize = new Size(width, 0);
                    root.Width = width;
                }
            }
        }

        /// <summary>
        /// Wraps every hint label of a page to the current width of its card.
        /// </summary>
        public static void FitHints(Control root, int width)
        {
            if (root == null || width < 50) return;
            foreach (Control child in root.Controls)
            {
                if (child.Tag is string && (string)child.Tag == "hint")
                {
                    child.MaximumSize = new Size(width, 0);
                    continue;
                }
                FitHints(child, width);
            }
        }

        /// <summary>
        /// A card whose content is laid out top to bottom. The card is a
        /// table, so it grows with its content and never clips or leaves an
        /// empty area. The returned stack is the card itself.
        /// </summary>
        public static TableLayoutPanel Card(string title, out TableLayoutPanel stack)
        {
            TableLayoutPanel card = new TableLayoutPanel();
            card.ColumnCount = 1;
            card.RowCount = 0;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.BackColor = Panel;
            card.Padding = new Padding(16);
            card.Margin = new Padding(0, 0, 0, 12);
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AttachCardBorder(card);

            if (!string.IsNullOrEmpty(title))
            {
                Label l = Section(title);
                l.AutoSize = true;
                l.Margin = new Padding(0, 0, 0, 8);
                l.Anchor = AnchorStyles.Left;
                int i = card.RowCount;
                card.RowCount = i + 1;
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(l, 0, i);
            }

            card.SizeChanged += delegate
            {
                int width = card.ClientSize.Width - card.Padding.Horizontal;
                if (width > 60) FitHints(card, width);
            };

            stack = card;
            return card;
        }
    }
}


