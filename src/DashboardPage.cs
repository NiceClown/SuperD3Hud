using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>
    /// The first screen. It answers one question: "can I just play now?".
    /// Everything that normally needs no attention is hidden, and only the
    /// things that are broken or need a choice are shown as a call to action.
    /// </summary>
    public class DashboardPage : UserControl
    {
        private readonly MainForm _main;

        private Label _gameState;
        private Label _mapState;
        private Label _pluginsState;
        private Label _gamePath;
        private CheckBox _mapEnabled;

        private TableLayoutPanel _updateCard;
        private Label _updateText;
        private Button _updateButton;

        private bool _loading;
        private Timer _timer;

        public DashboardPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();

            _main.UpdateChecked += OnUpdateChecked;

            _timer = new Timer();
            _timer.Interval = 2000;
            _timer.Tick += delegate { RefreshState(); };
            _timer.Start();
            RefreshAll();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void Build()
        {
            Controls.Add(Ui.Header("Обзор",
                "Запустите игру — карта и плагины уже работают. Здесь только дополнительные настройки."));

            Panel host;
            TableLayoutPanel body = Ui.PageBody(out host);
            Controls.Add(host);
            host.BringToFront();

            Ui.CardRow(body, BuildUpdateCard());
            Ui.CardRow(body, BuildSummaryCard());
            Ui.CardRow(body, BuildPlayCard());
            Ui.CardRow(body, BuildMapCard());
            Ui.CardRow(body, BuildPluginsCard());
            Ui.CardRow(body, BuildReadyCard());
        }

        /// <summary>Shown only when a newer release exists on GitHub.</summary>
        private TableLayoutPanel BuildUpdateCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Доступно обновление", out stack);
            _updateCard = card;
            _updateCard.Visible = false;

            _updateText = Ui.Hint("");
            Ui.Row(stack, _updateText);

            _updateButton = Ui.Primary("Скачать обновление", delegate { _main.OpenUpdatePage(); });
            Ui.Row(stack, Ui.Buttons(_updateButton));

            return card;
        }

        /// <summary>Three state lines that answer "what is working now".</summary>
        private TableLayoutPanel BuildSummaryCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Сейчас", out stack);

            _gameState = Ui.State("• Игра: проверка...", Ui.MutedColor);
            _mapState = Ui.State("• Карта: проверка...", Ui.MutedColor);
            _pluginsState = Ui.State("• Плагины: проверка...", Ui.MutedColor);
            Ui.Row(stack, _gameState);
            Ui.Row(stack, _mapState);
            Ui.Row(stack, _pluginsState);

            return card;
        }

        private TableLayoutPanel BuildPlayCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Игра", out stack);

            _gamePath = Ui.Hint("Игра не найдена");
            _gamePath.AutoEllipsis = true;
            Ui.Row(stack, _gamePath);

            Button play = Ui.Primary("Запустить игру", delegate { StartGame(); });
            Button detect = Ui.Secondary("Найти автоматически", delegate { DetectGame(); });
            Button browse = Ui.Secondary("Выбрать .exe", delegate { BrowseGame(); });
            Ui.Tip(play, "Запускает Diablo III. Карта и плагины подключаются сами.");
            Ui.Tip(detect, "Ищет установленную игру в реестре и стандартных папках.");
            Ui.Tip(browse, "Укажите Diablo III.exe или Diablo III64.exe вручную.");
            Ui.Row(stack, Ui.Buttons(play, detect, browse));

            return card;
        }

        private TableLayoutPanel BuildMapCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Карта", out stack);

            Ui.Row(stack, Ui.Hint(
                "Миникарта, большая карта по Tab и отдельная панель. Работает сразу после запуска игры."));

            _mapEnabled = Ui.DarkCheck("Показывать карту");
            _mapEnabled.CheckedChanged += delegate { OnMapToggled(); };
            Ui.Row(stack, _mapEnabled);

            Button open = Ui.Secondary("Настроить карту", delegate { _main.ShowPage("map"); });
            Ui.Tip(open, "Масштаб, прозрачность, большая карта и проверка сборки игры.");
            Ui.Row(stack, Ui.Buttons(open));

            return card;
        }

        private TableLayoutPanel BuildPluginsCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Плагины", out stack);

            Ui.Row(stack, Ui.Hint(
                "Свои плагины SuperD3Hud работают без сторонних программ. " +
                "Добавьте .cs-файл в папку плагинов — он подхватится автоматически."));

            Button open = Ui.Secondary("Открыть плагины", delegate { _main.ShowPage("plugins"); });
            Button folder = Ui.Secondary("Папка плагинов", delegate
            {
                if (_main.PluginRuntime != null) PluginManager.OpenFolder(_main.PluginRuntime.Folder);
            });
            Button reload = Ui.Secondary("Перечитать", delegate
            {
                if (_main.PluginRuntime == null) return;
                try
                {
                    _main.PluginRuntime.Load();
                    _main.SetStatus("Плагины перечитаны.");
                    RefreshState();
                }
                catch (Exception ex) { _main.Log("Плагины: " + ex.Message); }
            });
            Ui.Tip(open, "Список плагинов, включение и описания.");
            Ui.Tip(folder, "Сюда кладутся .cs-файлы плагинов.");
            Ui.Tip(reload, "Перекомпилировать плагины после правки файлов.");
            Ui.Row(stack, Ui.Buttons(open, folder, reload));

            return card;
        }

        /// <summary>A calm closing card: nothing to do is the normal state.</summary>
        private TableLayoutPanel BuildReadyCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Готово к работе", out stack);

            Ui.Row(stack, Ui.Hint(
                "Ничего настраивать не нужно: откройте игру, и карта с плагинами появятся сами. " +
                "Макросы — в соседнем разделе. Профили и настройки HUD-движка скрыты: " +
                "включите «Расширенные настройки» внизу слева, если они понадобятся."));

            Button config = Ui.Secondary("Открыть config", delegate { PluginManager.OpenFolder(AppPaths.ConfigDir); });
            Button check = Ui.Secondary("Проверить пути", delegate { _main.CheckPaths(); });
            Ui.Tip(config, "Все настройки, профили и заметки хранятся здесь.");
            Ui.Tip(check, "Проверяет, что игра и папки найдены.");
            Ui.Row(stack, Ui.Buttons(config, check));

            return card;
        }

        // ------------------------------------------------------------------
        // Update banner
        // ------------------------------------------------------------------

        private void OnUpdateChecked(UpdateInfo info)
        {
            if (info == null || !info.Available)
            {
                if (_updateCard != null) _updateCard.Visible = false;
                return;
            }

            if (_updateText != null)
            {
                string notes = string.IsNullOrEmpty(info.Notes) ? "" : " " + FirstLine(info.Notes);
                _updateText.Text = "Версия " + info.LatestVersion + " уже доступна (у вас " +
                                   info.CurrentVersion + ")." + notes;
            }
            if (_updateCard != null) _updateCard.Visible = true;
            if (_updateButton != null) _updateButton.Enabled = !string.IsNullOrEmpty(info.Url);
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOfAny(new char[] { '\r', '\n' });
            string line = cut > 0 ? text.Substring(0, cut) : text;
            line = line.Replace("#", "").Trim();
            if (line.Length > 160) line = line.Substring(0, 160) + "…";
            return line;
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        public void RefreshAll()
        {
            bool found = !string.IsNullOrEmpty(AppPaths.Settings.D3Path);
            _gamePath.Text = found
                ? AppPaths.Settings.D3Path
                : "Игра не найдена. Нажмите «Найти автоматически».";
            _gamePath.ForeColor = found ? Ui.MutedColor : Ui.Accent;

            _loading = true;
            try
            {
                _mapEnabled.Checked = _main.MapSettings.Enabled;
            }
            finally
            {
                _loading = false;
            }

            RefreshState();
        }

        private void OnMapToggled()
        {
            if (_loading) return;
            MapSettings settings = _main.MapSettings;
            settings.Enabled = _mapEnabled.Checked;
            settings.Configured = true;
            if (settings.Enabled && !settings.ShowMinimap && !settings.ShowPanel && !settings.ShowLargeMap)
                settings.ShowMinimap = true;

            AppPaths.Settings.Save();
            _main.OnMapSettingsChanged();
            _main.SetStatus(settings.Enabled ? "Карта включена." : "Карта выключена.");
            RefreshState();
        }

        private void RefreshState()
        {
            bool game = InputSender.IsGameRunning();
            Ui.SetState(_gameState, game ? "• Игра: запущена" : "• Игра: не запущена",
                game ? Ui.Ok : Ui.MutedColor);

            MapSettings map = _main.MapSettings;
            bool supported = _main.MapService != null && _main.MapService.BuildSupported;
            if (!map.Enabled)
                Ui.SetState(_mapState, "• Карта: выключена", Ui.MutedColor);
            else if (!supported)
                Ui.SetState(_mapState, "• Карта: сборка игры не поддерживается", Ui.DangerColor);
            else if (game)
                Ui.SetState(_mapState, "• Карта: работает", Ui.Ok);
            else
                Ui.SetState(_mapState, "• Карта: готова, ждёт игру", Ui.Accent);

            int total = 0, enabled = 0, broken = 0;
            if (_main.PluginRuntime != null)
            {
                foreach (Plugins.PluginEntry entry in _main.PluginRuntime.Plugins)
                {
                    total++;
                    if (entry.Enabled && entry.Compiled) enabled++;
                    if (!entry.Compiled) broken++;
                }
            }

            if (broken > 0)
                Ui.SetState(_pluginsState,
                    "• Плагины: " + enabled + " из " + total + ", с ошибкой " + broken,
                    Ui.DangerColor);
            else if (total == 0)
                Ui.SetState(_pluginsState, "• Плагины: нет", Ui.MutedColor);
            else
                Ui.SetState(_pluginsState,
                    "• Плагины: " + enabled + " из " + total + " включено" + TurboSuffix(),
                    enabled > 0 ? Ui.Ok : Ui.MutedColor);
        }

        /// <summary>TurboHUD plugins in the same status line, when there are any.</summary>
        private string TurboSuffix()
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null) return "";

            int total = 0, enabled = 0;
            foreach (TurboHud.TurboPluginEntry entry in runtime.Plugins)
            {
                total++;
                if (entry.Enabled && entry.Compiled) enabled++;
            }
            if (total == 0) return "";
            return "   •   TurboHUD: " + enabled + " из " + total;
        }

        private void StartGame()
        {
            try
            {
                ProcessRunner.StartGame();
                _main.SetStatus("Diablo III запускается...");
                _main.Log("Запуск Diablo III.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void DetectGame()
        {
            string path = AppPaths.FindGameExe();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this,
                    "Diablo III не найден автоматически. Укажите путь вручную кнопкой «Выбрать .exe».",
                    "Поиск игры", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppPaths.Settings.D3Path = path;
            AppPaths.Settings.Save();
            RefreshAll();
            _main.SetStatus("Игра найдена.");
        }

        private void BrowseGame()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Выберите Diablo III.exe";
                dlg.Filter = "Diablo III|Diablo III*.exe|Исполняемые файлы|*.exe";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AppPaths.Settings.D3Path = dlg.FileName;
                AppPaths.Settings.Save();
                RefreshAll();
                _main.SetStatus("Путь к игре сохранён.");
            }
        }

        private void ShowError(Exception ex)
        {
            _main.Log("Ошибка: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_main != null) _main.UpdateChecked -= OnUpdateChecked;
            }
            base.Dispose(disposing);
        }
    }
}
