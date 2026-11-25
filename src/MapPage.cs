using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>
    /// Settings of the map overlay: whether it is drawn, where it is drawn and
    /// how it looks. The page also shows the live state of the reader, so a
    /// user can tell at a glance whether the game build is supported.
    /// </summary>
    public class MapPage : UserControl
    {
        private readonly MainForm _main;

        private CheckBox _enabled;
        private CheckBox _showMinimap;
        private CheckBox _showPanel;
        private CheckBox _panelLocked;
        private NumericUpDown _scale;
        private NumericUpDown _offsetX;
        private NumericUpDown _offsetY;
        private NumericUpDown _opacity;
        private NumericUpDown _panelSize;

        private CheckBox _showLargeMap;
        private CheckBox _largeAuto;
        private CheckBox _largeFit;
        private NumericUpDown _largeScale;
        private NumericUpDown _largeOffsetX;
        private NumericUpDown _largeOffsetY;

        private Label _stateGame;
        private Label _stateBuild;
        private Label _stateReader;
        private Label _gamePath;
        private Label _details;

        private TableLayoutPanel _body;
        private bool _loading;
        private Timer _timer;

        public MapPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            Reload();

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { RefreshLiveState(); };
            _timer.Start();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void Build()
        {
            Controls.Add(Ui.Header("Карта",
                "Наложение карты рисуется собственным ридером памяти игры. Без сторонних оверлеев, без сети и без внедрения кода."));

            Panel host;
            _body = Ui.PageBody(out host);
            Controls.Add(host);
            host.BringToFront();
            host.SizeChanged += delegate { FitHints(); };

            Ui.CardRow(_body, BuildStatusCard());
            Ui.CardRow(_body, BuildModeCard());
            Ui.CardRow(_body, BuildLargeMapCard());
            Ui.CardRow(_body, BuildTuningCard());
            Ui.CardRow(_body, BuildNotesCard());
        }

        private void FitHints()
        {
            if (_body == null) return;
            int width = _body.ClientSize.Width - 40;
            if (width < 200) return;
            Ui.FitHints(_body, width);
        }

        /// <summary>Card with the live state of the game and the reader.</summary>
        private TableLayoutPanel BuildStatusCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Состояние", out stack);

            _stateGame = Ui.State("• Проверка игры...", Ui.MutedColor);
            _stateBuild = Ui.State("• Проверка сборки...", Ui.MutedColor);
            _stateReader = Ui.State("• Ридер не запущен", Ui.MutedColor);
            Ui.Row(stack, _stateGame);
            Ui.Row(stack, _stateBuild);
            Ui.Row(stack, _stateReader);

            _gamePath = Ui.Hint("");
            Ui.Row(stack, _gamePath);

            Ui.Row(stack, Ui.Buttons(
                Ui.Primary("Проверить игру", delegate { Probe(); }),
                Ui.Secondary("Обновить состояние", delegate { RefreshStatus(); }),
                Ui.Secondary("Плагины", delegate { _main.ShowPage("plugins"); })));

            _details = Ui.Hint("");
            Ui.Row(stack, _details);

            return card;
        }

        /// <summary>Card that selects where the map is drawn.</summary>
        private TableLayoutPanel BuildModeCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Где показывать карту", out stack);

            _enabled = Ui.DarkCheck("Включить наложение карты");
            _enabled.Font = Ui.FontBold;
            _enabled.Margin = new Padding(0, 0, 0, 10);
            _enabled.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _enabled);

            _showMinimap = Ui.DarkCheck("Поверх миникарты игры (в правом верхнем углу)");
            _showMinimap.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showMinimap);

            _showPanel = Ui.DarkCheck("Отдельная панель с картой (можно двигать)");
            _showPanel.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showPanel);

            _panelLocked = Ui.DarkCheck("Панель не перехватывает клики мышью");
            _panelLocked.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _panelLocked);

            Ui.Row(stack, Ui.Hint(
                "Можно включить оба варианта сразу. Наложение поверх миникарты не мешает управлению: " +
                "клики проходят в игру."));

            return card;
        }

        /// <summary>Card for the full-screen map that opens with Tab.</summary>
        private TableLayoutPanel BuildLargeMapCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Большая карта (Tab)", out stack);

            _showLargeMap = Ui.DarkCheck("Показывать карту на большой карте");
            _showLargeMap.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showLargeMap);

            _largeAuto = Ui.DarkCheck("Открывать вместе с картой игры по Tab (иначе по F9)");
            _largeAuto.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _largeAuto);

            _largeFit = Ui.DarkCheck("Вписывать всю карту в экран (выключено = следовать за игроком)");
            _largeFit.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _largeFit);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 4;
            grid.RowCount = 2;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            grid.Controls.Add(Ui.Field("Масштаб"), 0, 0);
            _largeScale = MakeNumeric(0.20m, 2.00m, 0.01m, 1.00m, 105);
            grid.Controls.Add(_largeScale, 1, 0);

            grid.Controls.Add(Ui.Field("Смещение по X"), 2, 0);
            _largeOffsetX = MakeNumeric(-200, 200, 5, 0, 105);
            grid.Controls.Add(_largeOffsetX, 3, 0);

            grid.Controls.Add(Ui.Field("Смещение по Y"), 0, 1);
            _largeOffsetY = MakeNumeric(-200, 200, 5, 0, 105);
            grid.Controls.Add(_largeOffsetY, 1, 1);

            Ui.Row(stack, grid);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Сбросить большую карту", delegate { ResetLargeMap(); })));

            Ui.Row(stack, Ui.Hint(
                "Наложение центрируется на герое и масштабируется от высоты окна, поэтому совмещение " +
                "не зависит от разрешения экрана. Если в игре выбран другой уровень приближения карты, " +
                "подстройте масштаб и смещение: точка игрока должна стоять на стрелке. " +
                "Смещения задаются для окна высотой 1200 точек."));

            return card;
        }

        /// <summary>Card with scale, opacity and offsets.</summary>
        private TableLayoutPanel BuildTuningCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Настройка вида", out stack);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 4;
            grid.RowCount = 3;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            grid.Controls.Add(Ui.Field("Масштаб"), 0, 0);
            _scale = MakeNumeric(0.2m, 4.0m, 0.05m, 1.00m, 110);
            grid.Controls.Add(_scale, 1, 0);

            grid.Controls.Add(Ui.Field("Прозрачность, %"), 2, 0);
            _opacity = MakeNumeric(0, 255, 1, 45, 110);
            grid.Controls.Add(_opacity, 3, 0);

            grid.Controls.Add(Ui.Field("Смещение по X"), 0, 1);
            _offsetX = MakeNumeric(-200, 200, 1, 0, 110);
            grid.Controls.Add(_offsetX, 1, 1);

            grid.Controls.Add(Ui.Field("Смещение по Y"), 2, 1);
            _offsetY = MakeNumeric(-200, 200, 1, 0, 110);
            grid.Controls.Add(_offsetY, 3, 1);

            grid.Controls.Add(Ui.Field("Размер панели, px"), 0, 2);
            _panelSize = MakeNumeric(120, 900, 10, 340, 110);
            grid.Controls.Add(_panelSize, 1, 2);

            Ui.Row(stack, grid);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Сбросить вид", delegate { ResetTuning(); })));

            Ui.Row(stack, Ui.Hint(
                "Масштаб 1.00 совпадает с миникартой игры. Смещение помогает выровнять карту, " +
                "если окно игры растянуто или обрезано."));

            return card;
        }

        /// <summary>Card that explains what the reader does and does not do.</summary>
        private TableLayoutPanel BuildNotesCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Как это работает", out stack);

            Ui.Row(stack, Ui.Hint(
                "Ридер открывает процесс Diablo III только на чтение: сцены и навигационные ячейки. " +
                "Память игры не изменяется, код не внедряется, потоки не останавливаются."));

            Ui.Row(stack, Ui.Hint(
                "Смещения проверены для сборки " + MapReader.SupportedVersion + ". На другой сборке ридер " +
                "выключается, а не читает память наугад."));

            Ui.Row(stack, Ui.Hint(
                "Показывается геометрия уже загруженных сцен и контур посещённых зон. " +
                "Полное открытие неисследованных этажей не гарантируется."));

            return card;
        }

        private NumericUpDown MakeNumeric(decimal min, decimal max, decimal step, decimal value, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Font = Ui.FontBody;
            n.BackColor = Ui.Panel2;
            n.ForeColor = Ui.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.DecimalPlaces = step < 1m ? 2 : 0;
            n.Value = value;
            n.Width = width;
            n.Height = 26;
            n.Margin = new Padding(0, 2, 0, 2);
            n.ValueChanged += delegate { Apply(); };
            return n;
        }

        // ------------------------------------------------------------------
        // Settings
        // ------------------------------------------------------------------

        /// <summary>Loads the stored settings into the controls.</summary>
        public void Reload()
        {
            MapSettings s = _main.MapSettings;
            _loading = true;
            try
            {
                _enabled.Checked = s.Enabled;
                _showMinimap.Checked = s.ShowMinimap;
                _showPanel.Checked = s.ShowPanel;
                _panelLocked.Checked = s.PanelLocked;
                _scale.Value = Clamp(_scale, (decimal)s.Scale);
                _offsetX.Value = Clamp(_offsetX, (decimal)s.OffsetX);
                _offsetY.Value = Clamp(_offsetY, (decimal)s.OffsetY);
                _opacity.Value = Clamp(_opacity, (decimal)s.Opacity);
                _panelSize.Value = Clamp(_panelSize, (decimal)s.PanelSize);
                _showLargeMap.Checked = s.ShowLargeMap;
                _largeAuto.Checked = s.LargeAuto;
                _largeFit.Checked = s.LargeFit;
                _largeScale.Value = Clamp(_largeScale, (decimal)s.LargeScale);
                _largeOffsetX.Value = Clamp(_largeOffsetX, (decimal)s.LargeOffsetX);
                _largeOffsetY.Value = Clamp(_largeOffsetY, (decimal)s.LargeOffsetY);
            }
            finally
            {
                _loading = false;
            }
            RefreshStatus();
        }

        private static decimal Clamp(NumericUpDown box, decimal value)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        /// <summary>Writes the controls back into the settings and saves them.</summary>
        public void Apply()
        {
            if (_loading) return;

            MapSettings s = _main.MapSettings;
            s.Enabled = _enabled.Checked;
            s.ShowMinimap = _showMinimap.Checked;
            s.ShowPanel = _showPanel.Checked;
            s.PanelLocked = _panelLocked.Checked;
            s.Scale = (double)_scale.Value;
            s.OffsetX = (double)_offsetX.Value;
            s.OffsetY = (double)_offsetY.Value;
            s.Opacity = (int)_opacity.Value;
            s.PanelSize = (int)_panelSize.Value;
            s.Configured = true;

            s.ShowLargeMap = _showLargeMap.Checked;
            s.LargeAuto = _largeAuto.Checked;
            s.LargeFit = _largeFit.Checked;
            s.LargeFollow = !_largeFit.Checked;
            s.LargeScale = (double)_largeScale.Value;
            s.LargeOffsetX = (double)_largeOffsetX.Value;
            s.LargeOffsetY = (double)_largeOffsetY.Value;

            // Turning the overlay on without a target would show nothing, so a
            // sensible default is selected for the user.
            if (s.Enabled && !s.ShowMinimap && !s.ShowPanel && !s.ShowLargeMap)
            {
                s.ShowMinimap = true;
                _showMinimap.Checked = true;
            }

            AppPaths.Settings.Save();
            _main.OnMapSettingsChanged();
            RefreshStatus();
        }

        private void ResetLargeMap()
        {
            _loading = true;
            try
            {
                _largeScale.Value = 1.00m;
                _largeOffsetX.Value = 0;
                _largeOffsetY.Value = 0;
            }
            finally
            {
                _loading = false;
            }
            Apply();
            _main.SetStatus("Настройки большой карты сброшены.");
        }

        private void ResetTuning()        {
            _loading = true;
            try
            {
                _scale.Value = 1.00m;
                _opacity.Value = 45;
                _offsetX.Value = 0;
                _offsetY.Value = 0;
                _panelSize.Value = 340;
            }
            finally
            {
                _loading = false;
            }
            Apply();
            _main.SetStatus("Вид карты сброшен к стандартному.");
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        /// <summary>Runs a single capture and reports what the reader found.</summary>
        private void Probe()
        {
            SetDetails("Чтение памяти...", Ui.MutedColor);
            Application.DoEvents();

            try
            {
                MapSnapshot snapshot = MapReader.Capture();
                List<MapPolygon> polygons = MapGeometry.Build(snapshot);
                int walkable = 0;
                foreach (MapPolygon p in polygons) if (!p.CachedFootprint) walkable++;

                SetDetails(string.Format(
                    "Готово: сцен {0}, ресурсов {1}, ячеек геометрии {2}, кэш сцен {3}.",
                    snapshot.Scenes.Count, snapshot.Assets.Count, walkable, snapshot.CachedScenes.Count),
                    Ui.Ok);
                _main.Log("Карта: снимок снят, сцен " + snapshot.Scenes.Count + ", геометрия " + walkable + ".");
            }
            catch (Exception ex)
            {
                SetDetails("Не удалось прочитать карту: " + ex.Message, Ui.DangerColor);
                _main.Log("Карта: " + ex.Message);
            }
            RefreshStatus();
        }

        /// <summary>Sets the single details line, keeping the card compact.</summary>
        private void SetDetails(string text, Color color)
        {
            if (_details == null) return;
            _details.Text = text ?? "";
            _details.ForeColor = color;
            _details.Visible = !string.IsNullOrEmpty(_details.Text);
        }

        /// <summary>Updates the build and game labels.</summary>
        public void RefreshStatus()
        {
            if (_main.MapService == null) return;

            bool running = InputSender.IsGameRunning();
            Ui.SetState(_stateGame,
                running ? "• Diablo III запущена" : "• Diablo III не запущена",
                running ? Ui.Ok : Ui.MutedColor);

            string exe = _main.MapService.GameExecutable;
            _gamePath.Text = string.IsNullOrEmpty(exe)
                ? "Игра не найдена. Укажите путь на вкладке «Обзор»."
                : "Клиент: " + exe;
            _gamePath.ForeColor = string.IsNullOrEmpty(exe) ? Ui.Accent : Ui.MutedColor;

            bool supported = _main.MapService.BuildSupported;
            if (string.IsNullOrEmpty(exe))
            {
                Ui.SetState(_stateBuild, "• Сборка игры неизвестна", Ui.MutedColor);
                SetDetails("", Ui.MutedColor);
            }
            else if (supported)
            {
                Ui.SetState(_stateBuild,
                    "• Сборка поддерживается (" + MapReader.SupportedVersion + ")",
                    Ui.Ok);
                SetDetails("", Ui.MutedColor);
            }
            else
            {
                Ui.SetState(_stateBuild,
                    "• Эта сборка не поддерживается ридером",
                    Ui.DangerColor);
                SetDetails(
                    "Ридер проверен на сборке " + MapReader.SupportedVersion +
                    " по SHA-256. На других версиях он отключается, чтобы не читать память наугад.",
                    Ui.Accent);
            }

            RefreshLiveState();
        }

        /// <summary>Updates the reader status line without touching the controls.</summary>
        private void RefreshLiveState()
        {
            if (_main.MapService == null || _stateReader == null) return;

            MapSettings settings = _main.MapSettings;
            string status = _main.MapService.Status;
            bool error = !string.IsNullOrEmpty(_main.MapService.LastError);

            if (!settings.Enabled)
                Ui.SetState(_stateReader, "• Наложение выключено", Ui.MutedColor);
            else if (error)
                Ui.SetState(_stateReader, "• " + status, Ui.DangerColor);
            else if (_main.MapService.BuildSupported && InputSender.IsGameRunning())
                Ui.SetState(_stateReader, "• " + status, Ui.Ok);
            else
                Ui.SetState(_stateReader, "• " + status, Ui.MutedColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
