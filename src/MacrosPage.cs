using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public class MacrosPage : UserControl
    {
        private readonly MainForm _main;
        private ListView _hotkeyList;
        private ComboBox _actionCombo;
        private TextBox _keyBox;
        private CheckBox _ctrl;
        private CheckBox _shift;
        private CheckBox _alt;
        private CheckBox _bindingEnabled;
        private CheckBox _macrosEnabled;
        private CheckBox _onlyGame;
        private NumericUpDown _delay;
        private CheckBox _smartMode;
        private CheckBox _salvageBulk;
        private CheckBox _salvageLegendaries;
        private CheckBox _protectAncient;
        private CheckBox _protectPrimal;
        private CheckBox _empoweredGR;
        private CheckBox _abortOnMouseMove;
        private NumericUpDown _lockedLeft;
        private NumericUpDown _lockedRight;
        private NumericUpDown _gemUpgrades;
        private NumericUpDown _gambleStopSlots;
        private Panel _bodyHost;
        private TableLayoutPanel _stackRoot;
        private TableLayoutPanel _smartCard;
        private FlowLayoutPanel _smartChecksFlow;
        private FlowLayoutPanel _smartNumsFlow;
        private Label _pauseLabel;
        private Button _pauseButton;
        private Label _editorTitle;
        private ComboBox _scriptCombo;
        private TextBox _scriptBox;
        private TextBox _logBox;
        private int _editingIndex = -1;
        private bool _addingNew;
        private bool _hotkeysRefreshing;
        private bool _loading;
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            // ListView raises ItemChecked while its handle is being destroyed;
            // saving and rebuilding the list from that callback corrupts the
            // control and makes Dispose throw later.
            _disposed = true;
            base.Dispose(disposing);
        }

        public MacrosPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            _main.LogLine += AppendLog;
            RefreshData();
            VisibleChanged += delegate { if (Visible) LayoutPage(); };
        }

        private void Build()
        {
            Controls.Add(Ui.Header("Макросы",
                "Выберите действие, назначьте клавишу и нажмите «Сохранить». Сценарий нужен только для нестандартных действий."));

            _bodyHost = new Panel();
            _bodyHost.Dock = DockStyle.Fill;
            _bodyHost.BackColor = Color.Transparent;
            _bodyHost.AutoScroll = true;
            _bodyHost.Padding = new Padding(0, 10, 0, 0);
            _bodyHost.SizeChanged += delegate { LayoutPage(); };
            Controls.Add(_bodyHost);
            _bodyHost.BringToFront();

            _stackRoot = new TableLayoutPanel();
            _stackRoot.Location = new Point(0, 10);
            _stackRoot.BackColor = Color.Transparent;
            _stackRoot.ColumnCount = 1;
            _stackRoot.RowCount = 4;
            _stackRoot.Margin = new Padding(0);
            _stackRoot.Padding = new Padding(0);
            _stackRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _stackRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            _stackRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));
            _stackRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _stackRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            _bodyHost.Controls.Add(_stackRoot);

            // Row 0: Options
            Panel options = Ui.Card();
            options.Dock = DockStyle.Fill;
            options.Margin = new Padding(0, 0, 0, 8);
            options.Padding = new Padding(14, 10, 14, 10);
            _macrosEnabled = Ui.DarkCheck("Макросы включены");
            _macrosEnabled.Location = new Point(14, 12);
            _macrosEnabled.CheckedChanged += delegate { ApplyOptions(); };
            _onlyGame = Ui.DarkCheck("Только когда Diablo III в фокусе");
            _onlyGame.Location = new Point(176, 12);
            _onlyGame.CheckedChanged += delegate { ApplyOptions(); };
            Label dl = Ui.Body("Задержка, мс:");
            dl.Location = new Point(424, 14);
            _delay = MakeNumeric(1, 2000, 56);
            _delay.Location = new Point(512, 10);
            _delay.ValueChanged += delegate { ApplyOptions(); };
            Label dlHint = Ui.Muted("между действиями");
            dlHint.Location = new Point(574, 14);
            _pauseLabel = Ui.Muted("Статус: активно");
            _pauseLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _pauseLabel.Location = new Point(14, 48);
            _pauseButton = Ui.Secondary("Пауза", delegate { _main.TogglePause(); });
            Ui.Tip(_pauseButton, "Приостанавливает выполнение макросов. Горячие клавиши продолжают работать.");
            FlowLayoutPanel ob = Ui.Toolbar();
            ob.Location = new Point(176, 42);
            ob.Controls.Add(_pauseButton);
            ob.Controls.Add(Ui.Secondary("Остановить выполнение", delegate { _main.StopMacros(); }));
            options.Controls.Add(_macrosEnabled);
            options.Controls.Add(_onlyGame);
            options.Controls.Add(dl);
            options.Controls.Add(_delay);
            options.Controls.Add(dlHint);
            options.Controls.Add(_pauseLabel);
            options.Controls.Add(ob);
            _stackRoot.Controls.Add(options, 0, 0);

            // Row 1: Smart Automation Card
            TableLayoutPanel smartStack;
            _smartCard = Ui.Card("Умная автоматизация", out smartStack);
            _smartCard.AutoSize = false;
            _smartCard.Dock = DockStyle.Fill;
            _smartCard.Padding = new Padding(14, 10, 14, 10);
            _smartCard.Margin = new Padding(0, 0, 0, 8);

            _smartChecksFlow = Ui.Toolbar();
            _smartChecksFlow.Margin = new Padding(0, 0, 0, 4);
            _smartMode = MakeSmartCheck("Умный режим (память)",
                "Читать живой рюкзак из памяти игры. При отключении используется сетка разблокированных колонок.");
            _salvageBulk = MakeSmartCheck("Авторазбор: белые/синие/жёлтые",
                "Нажимать кнопки массового разбора у кузнеца перед разбором легендарок.");
            _salvageLegendaries = MakeSmartCheck("Разбирать обычные легендарки",
                "Нажимать наковальню и разбирать незащищённые легендарные/комплектные предметы.");
            _protectAncient = MakeSmartCheck("Защищать Древние (Ancient)",
                "Никогда не разбирать и не выбрасывать древние предметы.");
            _protectPrimal = MakeSmartCheck("Защищать Первозданные (Primal)",
                "Никогда не разбирать и не выбрасывать первозданные предметы.");
            _empoweredGR = MakeSmartCheck("Усиленный ВП (Empowered GR)",
                "Включать галочку усиления портала при запуске Greater Rift.");
            _abortOnMouseMove = MakeSmartCheck("Стоп при движении мыши (>180 px)",
                "Мгновенно останавливать цикл макроса при ручном сдвиге курсора.");

            _smartChecksFlow.Controls.Add(_smartMode);
            _smartChecksFlow.Controls.Add(_salvageBulk);
            _smartChecksFlow.Controls.Add(_salvageLegendaries);
            _smartChecksFlow.Controls.Add(_protectAncient);
            _smartChecksFlow.Controls.Add(_protectPrimal);
            _smartChecksFlow.Controls.Add(_empoweredGR);
            _smartChecksFlow.Controls.Add(_abortOnMouseMove);
            Ui.Row(smartStack, _smartChecksFlow, true);

            _smartNumsFlow = Ui.Toolbar();
            _smartNumsFlow.Margin = new Padding(0);
            _lockedLeft = MakeNumeric(0, 10, 46);
            _lockedRight = MakeNumeric(0, 10, 46);
            _gemUpgrades = MakeNumeric(1, 5, 44);
            _gambleStopSlots = MakeNumeric(0, 20, 48);
            _lockedLeft.ValueChanged += delegate { ApplyOptions(); };
            _lockedRight.ValueChanged += delegate { ApplyOptions(); };
            _gemUpgrades.ValueChanged += delegate { ApplyOptions(); };
            _gambleStopSlots.ValueChanged += delegate { ApplyOptions(); };

            _smartNumsFlow.Controls.Add(MakeNumPair("Блок. слева:", _lockedLeft));
            _smartNumsFlow.Controls.Add(MakeNumPair("Блок. справа:", _lockedRight));
            _smartNumsFlow.Controls.Add(MakeNumPair("Улучш. камней:", _gemUpgrades));
            _smartNumsFlow.Controls.Add(MakeNumPair("Стоп гемблинга ≤ слотов:", _gambleStopSlots));
            Ui.Row(smartStack, _smartNumsFlow, true);
            _stackRoot.Controls.Add(_smartCard, 0, 1);

            // Row 3: Log at the bottom
            Panel logCard = Ui.Card();
            logCard.Dock = DockStyle.Fill;
            logCard.Margin = new Padding(0, 6, 0, 0);
            logCard.Padding = new Padding(14, 8, 14, 8);
            TableLayoutPanel logStack = new TableLayoutPanel();
            logStack.Dock = DockStyle.Fill;
            logStack.BackColor = Color.Transparent;
            logStack.ColumnCount = 1;
            logStack.RowCount = 2;
            logStack.Margin = new Padding(0);
            logStack.Padding = new Padding(0);
            logStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            logStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label lt = Ui.Section("Журнал");
            lt.Dock = DockStyle.Fill;
            lt.Margin = new Padding(0);
            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Font = Ui.FontMono;
            _logBox.BackColor = Ui.Panel2;
            _logBox.ForeColor = Ui.MutedColor;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.Dock = DockStyle.Fill;
            _logBox.Margin = new Padding(0, 2, 0, 0);
            logStack.Controls.Add(lt, 0, 0);
            logStack.Controls.Add(_logBox, 0, 1);
            logCard.Controls.Add(logStack);
            _stackRoot.Controls.Add(logCard, 0, 3);

            // Row 2: Split
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Ui.Bg;
            split.SplitterWidth = 10;
            split.Margin = new Padding(0);
            split.SizeChanged += delegate { SetSplit(split, 0.42); };
            _stackRoot.Controls.Add(split, 0, 2);

            // Left: hotkeys
            Panel left = Ui.Card();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(12);
            split.Panel1.Controls.Add(left);

            TableLayoutPanel leftStack = new TableLayoutPanel();
            leftStack.Dock = DockStyle.Fill;
            leftStack.BackColor = Color.Transparent;
            leftStack.ColumnCount = 1;
            leftStack.RowCount = 3;
            leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            leftStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
            left.Controls.Add(leftStack);

            Label ht = Ui.Section("Горячие клавиши");
            ht.Dock = DockStyle.Fill;
            leftStack.Controls.Add(ht, 0, 0);

            _hotkeyList = new ListView();
            _hotkeyList.Dock = DockStyle.Fill;
            _hotkeyList.Columns.Add("Действие", 170);
            _hotkeyList.Columns.Add("Клавиша", 96);
            _hotkeyList.CheckBoxes = true;
            _hotkeyList.ShowItemToolTips = true;
            Ui.StyleListView(_hotkeyList);
            _hotkeyList.SelectedIndexChanged += delegate { LoadBindingEditor(); };
            _hotkeyList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { HotkeyChecked(e); };
            _hotkeyList.Resize += delegate { FitHotkeyColumns(); };
            leftStack.Controls.Add(_hotkeyList, 0, 1);

            TableLayoutPanel editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Color.Transparent;
            editor.ColumnCount = 2;
            editor.RowCount = 6;
            editor.Margin = new Padding(0);
            editor.Padding = new Padding(0);
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            for (int i = 0; i < 6; i++) editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftStack.Controls.Add(editor, 0, 2);

            _editorTitle = Ui.Body("Выберите запись слева");
            _editorTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _editorTitle.Dock = DockStyle.Fill;
            editor.Controls.Add(_editorTitle, 0, 0);
            editor.SetColumnSpan(_editorTitle, 2);

            Label et = Ui.Field("Действие");
            et.Dock = DockStyle.Fill;
            editor.Controls.Add(et, 0, 1);

            Label kt = Ui.Field("Клавиша");
            kt.Dock = DockStyle.Fill;
            editor.Controls.Add(kt, 1, 1);

            _actionCombo = Ui.Combo();
            _actionCombo.Dock = DockStyle.Fill;
            _actionCombo.Margin = new Padding(0, 0, 8, 4);
            foreach (string a in MacroActions.All)
                _actionCombo.Items.Add(new ActionItem(a));
            editor.Controls.Add(_actionCombo, 0, 2);

            _keyBox = Ui.Input();
            _keyBox.Dock = DockStyle.Fill;
            _keyBox.ReadOnly = true;
            _keyBox.Cursor = Cursors.Hand;
            _keyBox.Margin = new Padding(0, 0, 0, 4);
            _keyBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
                {
                    _keyBox.Text = "";
                }
                else if (e.KeyCode != Keys.None)
                {
                    _keyBox.Text = KeyText(e.KeyCode);
                    _ctrl.Checked = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                    _shift.Checked = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                    _alt.Checked = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
                }
                e.SuppressKeyPress = true;
                e.Handled = true;
            };
            Ui.Tip(_keyBox, "Нажмите нужную клавишу. Backspace очищает поле.");
            editor.Controls.Add(_keyBox, 1, 2);

            FlowLayoutPanel modifiers = Ui.Toolbar();
            _ctrl = Ui.DarkCheck("Ctrl");
            _shift = Ui.DarkCheck("Shift");
            _alt = Ui.DarkCheck("Alt");
            _bindingEnabled = Ui.DarkCheck("Включена");
            _bindingEnabled.Checked = true;
            _ctrl.Margin = new Padding(0, 2, 6, 2);
            _shift.Margin = new Padding(0, 2, 6, 2);
            _alt.Margin = new Padding(0, 2, 6, 2);
            _bindingEnabled.Margin = new Padding(0, 2, 0, 2);
            modifiers.Controls.Add(_ctrl);
            modifiers.Controls.Add(_shift);
            modifiers.Controls.Add(_alt);
            modifiers.Controls.Add(_bindingEnabled);
            editor.Controls.Add(modifiers, 0, 3);
            editor.SetColumnSpan(modifiers, 2);

            FlowLayoutPanel eb = Ui.Toolbar();
            eb.Margin = new Padding(0, 2, 0, 2);
            Button btnSaveBinding = Ui.Primary("Сохранить", delegate { SaveBinding(); });
            Button btnNewBinding = Ui.Secondary("Новая запись", delegate { NewBinding(); });
            Button btnDelBinding = Ui.Secondary("Удалить", delegate { RemoveBinding(); });
            CompactButton(btnSaveBinding);
            CompactButton(btnNewBinding);
            CompactButton(btnDelBinding);
            eb.Controls.Add(btnSaveBinding);
            eb.Controls.Add(btnNewBinding);
            eb.Controls.Add(btnDelBinding);
            editor.Controls.Add(eb, 0, 4);
            editor.SetColumnSpan(eb, 2);

            Label hint = Ui.Hint(
                "Выберите строку слева, измените действие или клавишу и нажмите «Сохранить». " +
                "«Новая запись» очищает форму для добавления ещё одной клавиши.");
            hint.Dock = DockStyle.Fill;
            editor.Controls.Add(hint, 0, 5);
            editor.SetColumnSpan(hint, 2);

            // Right: script editor
            Panel right = Ui.Card();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(12);
            split.Panel2.Controls.Add(right);

            TableLayoutPanel stack = new TableLayoutPanel();
            stack.Dock = DockStyle.Fill;
            stack.BackColor = Color.Transparent;
            stack.ColumnCount = 1;
            stack.RowCount = 4;
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            right.Controls.Add(stack);

            Label st = Ui.Section("Сценарий действия (необязательно)");
            st.Dock = DockStyle.Fill;
            stack.Controls.Add(st, 0, 0);

            TableLayoutPanel scriptTop = new TableLayoutPanel();
            scriptTop.Dock = DockStyle.Fill;
            scriptTop.BackColor = Color.Transparent;
            scriptTop.ColumnCount = 1;
            scriptTop.RowCount = 2;
            scriptTop.Margin = new Padding(0);
            scriptTop.Padding = new Padding(0);
            scriptTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scriptTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            scriptTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _scriptCombo = Ui.Combo();
            _scriptCombo.Width = 300;
            _scriptCombo.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            _scriptCombo.Margin = new Padding(0, 2, 0, 2);
            foreach (string a in MacroActions.All)
            {
                if (a == "Pause") continue;
                _scriptCombo.Items.Add(new ActionItem(a));
            }
            _scriptCombo.SelectedIndexChanged += delegate { LoadScript(); };
            scriptTop.Controls.Add(_scriptCombo, 0, 0);

            Label scriptHint = Ui.Muted(
                "Готовый сценарий уже есть у каждой команды. Меняйте его, только если понимаете команды.");
            scriptHint.AutoSize = false;
            scriptHint.Dock = DockStyle.Fill;
            scriptHint.Margin = new Padding(0, 2, 0, 0);
            scriptTop.Controls.Add(scriptHint, 0, 1);
            stack.Controls.Add(scriptTop, 0, 1);

            _scriptBox = new TextBox();
            _scriptBox.Multiline = true;
            _scriptBox.ScrollBars = ScrollBars.Both;
            _scriptBox.AcceptsReturn = true;
            _scriptBox.AcceptsTab = true;
            _scriptBox.WordWrap = false;
            _scriptBox.Font = Ui.FontMono;
            _scriptBox.BackColor = Ui.Panel2;
            _scriptBox.ForeColor = Ui.Text;
            _scriptBox.BorderStyle = BorderStyle.FixedSingle;
            _scriptBox.Dock = DockStyle.Fill;
            stack.Controls.Add(_scriptBox, 0, 2);

            Panel scriptButtons = new Panel();
            scriptButtons.Dock = DockStyle.Fill;
            scriptButtons.BackColor = Color.Transparent;
            FlowLayoutPanel sbtn = Ui.Toolbar();
            sbtn.Dock = DockStyle.Top;
            Button btnSaveScript = Ui.Primary("Сохранить сценарий", delegate { SaveScript(); });
            Button btnResetScript = Ui.Secondary("Сбросить к стандартному", delegate { ResetScript(); });
            Button btnRunScript = Ui.Secondary("Выполнить", delegate { TestScript(); });
            CompactButton(btnSaveScript);
            CompactButton(btnResetScript);
            CompactButton(btnRunScript);
            sbtn.Controls.Add(btnSaveScript);
            sbtn.Controls.Add(btnResetScript);
            sbtn.Controls.Add(btnRunScript);
            scriptButtons.Controls.Add(sbtn);

            Label sh = Ui.Muted("Координаты для 2560x1440, пересчёт под окно игры. Задержка 1–5 мс рандомизируется.");
            sh.Dock = DockStyle.Bottom;
            sh.Height = 20;
            sh.AutoSize = false;
            sh.AutoEllipsis = true;
            scriptButtons.Controls.Add(sh);
            stack.Controls.Add(scriptButtons, 0, 3);

            if (_scriptCombo.Items.Count > 0) _scriptCombo.SelectedIndex = 0;
            LayoutPage();
        }

        private static void CompactButton(Button b)
        {
            b.Padding = new Padding(4, 0, 4, 0);
            b.Margin = new Padding(0, 0, 4, 3);
            b.Height = 30;
        }

        private CheckBox MakeSmartCheck(string text, string tip)
        {
            CheckBox cb = Ui.DarkCheck(text);
            cb.Margin = new Padding(0, 1, 14, 3);
            cb.CheckedChanged += delegate { ApplyOptions(); };
            Ui.Tip(cb, tip);
            return cb;
        }

        private static NumericUpDown MakeNumeric(int min, int max, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = 1;
            n.Width = width;
            n.Font = Ui.FontBody;
            n.BackColor = Ui.Panel2;
            n.ForeColor = Ui.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            return n;
        }

        private static Control MakeNumPair(string caption, NumericUpDown num)
        {
            FlowLayoutPanel pair = new FlowLayoutPanel();
            pair.FlowDirection = FlowDirection.LeftToRight;
            pair.WrapContents = false;
            pair.AutoSize = true;
            pair.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pair.BackColor = Color.Transparent;
            pair.Margin = new Padding(0, 0, 14, 2);
            pair.Padding = new Padding(0);

            Label l = Ui.Body(caption);
            l.Margin = new Padding(0, 4, 5, 0);
            num.Margin = new Padding(0, 1, 0, 0);
            pair.Controls.Add(l);
            pair.Controls.Add(num);
            return pair;
        }

        private void LayoutPage()
        {
            if (_bodyHost == null || _stackRoot == null || _disposed) return;
            int clientW = _bodyHost.ClientSize.Width;
            int clientH = _bodyHost.ClientSize.Height - _bodyHost.Padding.Top;
            if (clientW < 200 || clientH < 100) return;

            bool scrollVisible = _bodyHost.VerticalScroll.Visible;
            int w = clientW;
            int smartH = MeasureSmartCardHeight(w);
            _stackRoot.RowStyles[1].Height = smartH + _smartCard.Margin.Vertical;

            int minSplit = 332;
            int minTotal = 92 + (smartH + _smartCard.Margin.Vertical) + minSplit + 88;
            if (minTotal > clientH && !scrollVisible)
            {
                w = Math.Max(200, clientW - SystemInformation.VerticalScrollBarWidth - 2);
                smartH = MeasureSmartCardHeight(w);
                _stackRoot.RowStyles[1].Height = smartH + _smartCard.Margin.Vertical;
                minTotal = 92 + (smartH + _smartCard.Margin.Vertical) + minSplit + 88;
            }
            if (minTotal > clientH)
                w = Math.Max(200, w - 6);

            int targetH = Math.Max(clientH, minTotal);
            if (_stackRoot.Width != w || _stackRoot.Height != targetH)
            {
                _stackRoot.SuspendLayout();
                _stackRoot.Size = new Size(w, targetH);
                _stackRoot.ResumeLayout(true);
            }
            FitHotkeyColumns();
        }

        private int MeasureSmartCardHeight(int hostWidth)
        {
            if (_smartCard == null) return 126;
            int cardW = Math.Max(200, hostWidth - _smartCard.Margin.Horizontal);
            int innerW = Math.Max(180, cardW - _smartCard.Padding.Horizontal - 4);
            if (_smartChecksFlow != null)
                _smartChecksFlow.MaximumSize = new Size(innerW, 0);
            if (_smartNumsFlow != null)
                _smartNumsFlow.MaximumSize = new Size(innerW, 0);

            int checksH = _smartChecksFlow != null ? _smartChecksFlow.GetPreferredSize(new Size(innerW, 0)).Height : 52;
            int numsH = _smartNumsFlow != null ? _smartNumsFlow.GetPreferredSize(new Size(innerW, 0)).Height : 28;
            int h = _smartCard.Padding.Vertical + 26 + checksH + 6 + numsH + 6;
            return Math.Max(92, h);
        }

        private class ActionItem
        {
            public readonly string Key;
            public ActionItem(string key) { Key = key; }
            public override string ToString() { return MacroActions.DisplayName(Key); }
        }

        private static void SetSplit(SplitContainer split, double ratio)
        {
            try
            {
                int width = split.Width;
                if (width < 400) return;
                int target = (int)(width * ratio);
                int minLeft = Math.Max(split.Panel1MinSize, 326);
                if (target < minLeft) target = minLeft;
                int max = width - Math.Max(split.Panel2MinSize, 260) - split.SplitterWidth;
                if (target > max) target = max;
                if (target < split.Panel1MinSize) target = split.Panel1MinSize;
                if (split.SplitterDistance != target) split.SplitterDistance = target;
            }
            catch { }
        }

        private void FitHotkeyColumns()
        {
            if (_hotkeyList == null || _hotkeyList.Columns.Count < 2) return;
            int width = _hotkeyList.ClientSize.Width;
            if (width < 120) return;
            int keyWidth = 92;
            int actionWidth = width - keyWidth - 4;
            if (actionWidth < 90) actionWidth = 90;
            _hotkeyList.Columns[0].Width = actionWidth;
            _hotkeyList.Columns[1].Width = keyWidth;
        }

        public void RefreshData()
        {
            MacroSettings s = _main.MacroSettings;
            _loading = true;
            try
            {
                _macrosEnabled.Checked = s.Enabled;
                _onlyGame.Checked = s.OnlyWhenGameForeground;
                _delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, s.StepDelayMs));

                _smartMode.Checked = s.SmartMode;
                _salvageBulk.Checked = s.SalvageBulkButtons;
                _salvageLegendaries.Checked = s.SalvageLegendaries;
                _protectAncient.Checked = s.ProtectAncient;
                _protectPrimal.Checked = s.ProtectPrimal;
                _empoweredGR.Checked = s.EmpoweredGR;
                _abortOnMouseMove.Checked = s.AbortOnMouseMove;

                _lockedLeft.Value = ClampDecimal(s.LockedLeftColumns, _lockedLeft);
                _lockedRight.Value = ClampDecimal(s.LockedRightColumns, _lockedRight);
                _gemUpgrades.Value = ClampDecimal(s.GemUpgradeCount, _gemUpgrades);
                _gambleStopSlots.Value = ClampDecimal(s.GambleStopFreeSlots, _gambleStopSlots);
            }
            finally
            {
                _loading = false;
            }
            RefreshList();
            LoadScript();
            UpdatePauseLabel();
            LayoutPage();
        }

        private static decimal ClampDecimal(int value, NumericUpDown control)
        {
            decimal d = value;
            if (d < control.Minimum) return control.Minimum;
            if (d > control.Maximum) return control.Maximum;
            return d;
        }

        private void RefreshList()
        {
            int selected = _editingIndex;
            bool adding = _addingNew;
            _hotkeysRefreshing = true;
            try
            {
                _hotkeyList.BeginUpdate();
                _hotkeyList.Items.Clear();
                List<HotkeyBinding> list = _main.MacroSettings.Hotkeys;
                for (int i = 0; i < list.Count; i++)
                {
                    HotkeyBinding b = list[i];
                    ListViewItem item = new ListViewItem(MacroActions.DisplayName(b.Action));
                    item.SubItems.Add(b.Describe());
                    item.Tag = i;
                    item.ForeColor = b.Enabled ? Ui.Text : Ui.MutedColor;
                    item.ToolTipText = b.Enabled
                        ? "Клавиша включена. Снимите галочку, чтобы временно отключить."
                        : "Клавиша отключена. Поставьте галочку, чтобы включить.";
                    _hotkeyList.Items.Add(item);
                    item.Checked = b.Enabled;
                }
                _hotkeyList.EndUpdate();
            }
            finally
            {
                _hotkeysRefreshing = false;
            }

            if (!adding && selected >= 0 && selected < _hotkeyList.Items.Count)
                _hotkeyList.Items[selected].Selected = true;
            else if (!adding && _hotkeyList.Items.Count > 0 && _hotkeyList.SelectedItems.Count == 0)
                _hotkeyList.Items[0].Selected = true;
        }

        private void LoadBindingEditor()
        {
            if (_disposed || _main.IsShuttingDown) return;
            if (_hotkeyList.SelectedItems.Count == 0) return;
            int index = (int)_hotkeyList.SelectedItems[0].Tag;
            if (index < 0 || index >= _main.MacroSettings.Hotkeys.Count) return;
            _editingIndex = index;
            _addingNew = false;
            HotkeyBinding b = _main.MacroSettings.Hotkeys[index];
            _editorTitle.Text = "Запись: " + MacroActions.DisplayName(b.Action);
            SelectAction(b.Action);
            _keyBox.Text = KeyDisplay(b.Key);
            _ctrl.Checked = b.Ctrl;
            _shift.Checked = b.Shift;
            _alt.Checked = b.Alt;
            _bindingEnabled.Checked = b.Enabled;
        }

        private void SelectAction(string action)
        {
            for (int i = 0; i < _actionCombo.Items.Count; i++)
            {
                ActionItem item = _actionCombo.Items[i] as ActionItem;
                if (item != null && item.Key == action)
                {
                    _actionCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private string SelectedAction()
        {
            ActionItem item = _actionCombo.SelectedItem as ActionItem;
            return item == null ? "Salvage" : item.Key;
        }

        /// <summary>
        /// Converts a captured key to the same short names that
        /// <see cref="InputSender.ParseKey"/> accepts ("F1", "ESC", "D3"...).
        /// </summary>
        private static string KeyText(Keys key)
        {
            switch (key)
            {
                case Keys.Escape: return "ESC";
                case Keys.Space: return "SPACE";
                case Keys.Enter: return "ENTER";
                case Keys.Back: return "BACK";
                case Keys.Tab: return "TAB";
                case Keys.PageUp: return "PGUP";
                case Keys.PageDown: return "PGDN";
                case Keys.Delete: return "DEL";
                case Keys.Insert: return "INS";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.Up: return "UP";
                case Keys.Down: return "DOWN";
                case Keys.Left: return "LEFT";
                case Keys.Right: return "RIGHT";
                case Keys.Oemtilde: return "OEMTILDE";
                case Keys.OemMinus: return "OEMMINUS";
                case Keys.Oemplus: return "OEMPLUS";
                case Keys.OemOpenBrackets: return "OEMOPENBRACKETS";
                case Keys.OemCloseBrackets: return "OEMCLOSEBRACKETS";
                case Keys.OemPipe: return "OEMPIPE";
                case Keys.OemSemicolon: return "OEMSEMICOLON";
                case Keys.OemQuotes: return "OEMQUOTES";
                case Keys.Oemcomma: return "OEMCOMMA";
                case Keys.OemPeriod: return "OEMPERIOD";
                case Keys.OemQuestion: return "OEMQUESTION";
            }
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)key - (int)Keys.D0).ToString();
            return key.ToString().ToUpperInvariant();
        }

        /// <summary>Shows numeric key codes saved by older versions as names.</summary>
        private static string KeyDisplay(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            int code;
            if (int.TryParse(stored.Trim(), out code) && code > 0 && code < 256)
                return KeyText((Keys)code);
            return stored;
        }

        private int FindDuplicate(int virtualKey, bool ctrl, bool shift, bool alt, int skipIndex)
        {
            List<HotkeyBinding> list = _main.MacroSettings.Hotkeys;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == skipIndex) continue;
                HotkeyBinding b = list[i];
                if (b.Ctrl != ctrl || b.Shift != shift || b.Alt != alt) continue;
                int code;
                try { code = InputSender.ParseKey(b.Key); }
                catch { continue; }
                if (code == virtualKey) return i;
            }
            return -1;
        }

        private void SaveBinding()
        {
            string key = (_keyBox.Text ?? "").Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(this, "Сначала нажмите нужную клавишу в поле «Клавиша».",
                    "Макросы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _keyBox.Focus();
                return;
            }

            int virtualKey;
            try { virtualKey = InputSender.ParseKey(key); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Макросы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ctrl = _ctrl.Checked;
            bool shift = _shift.Checked;
            bool alt = _alt.Checked;
            bool update = !_addingNew && _editingIndex >= 0 && _editingIndex < _main.MacroSettings.Hotkeys.Count;
            int duplicate = FindDuplicate(virtualKey, ctrl, shift, alt, update ? _editingIndex : -1);
            if (duplicate >= 0)
            {
                HotkeyBinding other = _main.MacroSettings.Hotkeys[duplicate];
                MessageBox.Show(this,
                    "Клавиша уже назначена действию «" + MacroActions.DisplayName(other.Action) + "». " +
                    "Измените её там или выберите другую комбинацию.",
                    "Макросы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            HotkeyBinding b;
            if (update)
            {
                b = _main.MacroSettings.Hotkeys[_editingIndex];
            }
            else
            {
                b = new HotkeyBinding();
                _main.MacroSettings.Hotkeys.Add(b);
                _editingIndex = _main.MacroSettings.Hotkeys.Count - 1;
                _addingNew = false;
            }

            b.Action = SelectedAction();
            b.Key = key;
            b.Ctrl = ctrl;
            b.Shift = shift;
            b.Alt = alt;
            b.Enabled = _bindingEnabled.Checked;
            SaveAndReload();
            RefreshList();
            _main.SetStatus((update ? "Сохранено: " : "Добавлено: ") + b.Describe());
        }

        private void NewBinding()
        {
            _addingNew = true;
            _editingIndex = -1;
            if (_hotkeyList.SelectedItems.Count > 0) _hotkeyList.SelectedItems.Clear();
            _editorTitle.Text = "Новая запись";
            if (_actionCombo.Items.Count > 0) _actionCombo.SelectedIndex = 0;
            _keyBox.Text = "";
            _ctrl.Checked = false;
            _shift.Checked = false;
            _alt.Checked = false;
            _bindingEnabled.Checked = true;
            _keyBox.Focus();
        }

        private void HotkeyChecked(ItemCheckedEventArgs e)
        {
            if (_disposed || _main.IsShuttingDown) return;
            if (_hotkeysRefreshing || _loading || e == null || e.Item == null) return;
            int index = (int)e.Item.Tag;
            List<HotkeyBinding> list = _main.MacroSettings.Hotkeys;
            if (index < 0 || index >= list.Count) return;
            HotkeyBinding b = list[index];
            if (b.Enabled == e.Item.Checked) return;
            b.Enabled = e.Item.Checked;
            e.Item.ForeColor = b.Enabled ? Ui.Text : Ui.MutedColor;
            e.Item.ToolTipText = b.Enabled
                ? "Клавиша включена. Снимите галочку, чтобы временно отключить."
                : "Клавиша отключена. Поставьте галочку, чтобы включить.";
            if (!_addingNew && _editingIndex == index) _bindingEnabled.Checked = b.Enabled;
            SaveAndReload();
            _main.SetStatus((b.Enabled ? "Включена" : "Отключена") + " горячая клавиша: " + b.Describe());
        }

        private void RemoveBinding()
        {
            if (_addingNew)
            {
                NewBinding();
                return;
            }
            if (_editingIndex < 0 || _editingIndex >= _main.MacroSettings.Hotkeys.Count)
            {
                MessageBox.Show(this, "Выберите запись в списке слева.", "Макросы",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string name = MacroActions.DisplayName(_main.MacroSettings.Hotkeys[_editingIndex].Action);
            _main.MacroSettings.Hotkeys.RemoveAt(_editingIndex);
            _editingIndex = -1;
            _addingNew = false;
            _editorTitle.Text = "Выберите запись слева";
            SaveAndReload();
            RefreshList();
            _main.SetStatus("Удалена горячая клавиша: " + name);
        }

        private void SaveAndReload()
        {
            _main.MacroSettings.Save();
            _main.ReloadHotkeys();
        }

        private void ApplyOptions()
        {
            if (_loading) return;
            MacroSettings s = _main.MacroSettings;
            s.Enabled = _macrosEnabled.Checked;
            s.OnlyWhenGameForeground = _onlyGame.Checked;
            s.StepDelayMs = (int)_delay.Value;

            s.SmartMode = _smartMode.Checked;
            s.SalvageBulkButtons = _salvageBulk.Checked;
            s.SalvageLegendaries = _salvageLegendaries.Checked;
            s.ProtectAncient = _protectAncient.Checked;
            s.ProtectPrimal = _protectPrimal.Checked;
            s.EmpoweredGR = _empoweredGR.Checked;
            s.AbortOnMouseMove = _abortOnMouseMove.Checked;

            s.LockedLeftColumns = (int)_lockedLeft.Value;
            s.LockedRightColumns = (int)_lockedRight.Value;
            s.GemUpgradeCount = (int)_gemUpgrades.Value;
            s.GambleStopFreeSlots = (int)_gambleStopSlots.Value;

            s.Save();
            _main.UpdateMacroSettings();
        }

        private void LoadScript()
        {
            ActionItem item = _scriptCombo.SelectedItem as ActionItem;
            if (item == null) return;
            string script;
            if (_main.MacroSettings.Scripts.TryGetValue(item.Key, out script)) _scriptBox.Text = script;
            else _scriptBox.Text = "";
        }

        private void SaveScript()
        {
            ActionItem item = _scriptCombo.SelectedItem as ActionItem;
            if (item == null) return;
            _main.MacroSettings.Scripts[item.Key] = _scriptBox.Text;
            _main.MacroSettings.Save();
            _main.UpdateMacroSettings();
            _main.SetStatus("Сценарий сохранён: " + MacroActions.DisplayName(item.Key));
        }

        private void ResetScript()
        {
            ActionItem item = _scriptCombo.SelectedItem as ActionItem;
            if (item == null) return;
            _scriptBox.Text = MacroActions.DefaultScript(item.Key);
        }

        private void TestScript()
        {
            ActionItem item = _scriptCombo.SelectedItem as ActionItem;
            if (item == null) return;
            string script = _scriptBox.Text;
            if (string.IsNullOrEmpty(script)) return;
            if (!_main.MacroSettings.Enabled)
            {
                MessageBox.Show(this, "Макросы отключены. Включите их сверху.", "Макросы");
                return;
            }
            _main.MacroSettings.Scripts[item.Key] = script;
            _main.MacroSettings.Save();
            _main.RunMacro(item.Key);
        }

        public void UpdatePauseLabel()
        {
            if (_main.Macros == null) return;
            // MainForm only calls this on the UI thread; refuse anything else
            // rather than create the label's HWND on a worker.
            if (!_main.IsUiThread) return;
            bool paused = _main.Macros.IsPaused;
            _pauseLabel.Text = paused ? "Статус: ПАУЗА" : "Статус: активно";
            _pauseLabel.ForeColor = paused ? Ui.Accent : Ui.Ok;
            if (_pauseButton != null) _pauseButton.Text = paused ? "Продолжить" : "Пауза";
        }

        /// <summary>The log box keeps at most this many characters; older text is cut in half.</summary>
        internal const int MaxLogChars = 64 * 1024;

        internal TextBox LogBoxForTest { get { return _logBox; } }

        public void AppendLog(string text)
        {
            if (_logBox == null) return;

            // MainForm raises LogLine only on the UI thread. This is the last
            // line of defence: a call from a worker would create the TextBox
            // HWND on that worker (while the page is hidden it has none yet),
            // and the next UI-thread append would deadlock in SendMessage.
            // InvokeRequired cannot detect that case - it is false on every
            // thread until a handle exists - so the thread id is compared.
            if (!_main.IsUiThread) return;

            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            _logBox.AppendText(line + Environment.NewLine);

            // A failing plugin can log every frame; without a cap the box grows
            // without bound and every append gets slower (EM_REPLACESEL on a
            // huge buffer). Keep the newest half, starting at a line boundary.
            if (_logBox.TextLength > MaxLogChars)
            {
                string all = _logBox.Text;
                int start = all.Length - MaxLogChars / 2;
                int nl = all.IndexOf('\n', start);
                if (nl >= 0 && nl + 1 < all.Length) start = nl + 1;
                _logBox.Text = all.Substring(start);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
        }

        /// <summary>
        /// Creates the HWNDs of the log box and its parents on the calling (UI)
        /// thread. The page starts hidden, so without this the handles would be
        /// created lazily by whichever thread touched the box first.
        /// </summary>
        public void EnsureHandles()
        {
            if (_logBox == null || !_main.IsUiThread) return;
            // Top-down, so each control is created inside an existing parent.
            List<Control> chain = new List<Control>();
            for (Control c = _logBox; c != null; c = c.Parent) chain.Add(c);
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].Handle == IntPtr.Zero) break;
            }
        }
    }
}



