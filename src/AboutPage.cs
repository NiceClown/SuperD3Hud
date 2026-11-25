using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public class AboutPage : UserControl
    {
        private readonly MainForm _main;
        private TextBox _updateRepo;
        private Label _updateStatus;

        public AboutPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
        }

        private void Build()
        {
            Controls.Add(Ui.Header("О программе",
                "SuperD3Hud — самостоятельный лаунчер, плагины и собственная карта для Diablo III."));

            Panel host;
            TableLayoutPanel body = Ui.PageBody(out host);
            Controls.Add(host);
            host.BringToFront();

            Ui.CardRow(body, BuildWhatCard());
            Ui.CardRow(body, BuildTurboCard());
            Ui.CardRow(body, BuildMapCard());
            Ui.CardRow(body, BuildHonestyCard());
            Ui.CardRow(body, BuildFilesCard());

            _updateRepo.Text = AppPaths.Settings.UpdateRepo;
            if (string.IsNullOrEmpty(AppPaths.Settings.UpdateRepo))
                SetUpdateStatus("Проверка обновлений не настроена. Укажите репозиторий ниже.", Ui.MutedColor);
            else
                SetUpdateStatus("Проверка включена: " + AppPaths.Settings.UpdateRepo, Ui.MutedColor);
        }

        private TableLayoutPanel BuildWhatCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Что это", out stack);

            Ui.Row(stack, Ui.Hint(
                "SuperD3Hud — самостоятельный лаунчер для Diablo III. Запустите игру, и карта с плагинами " +
                "уже работают; окно программы нужно только для дополнительных настроек."));

            Ui.Row(stack, Ui.Hint("• Карта: миникарта, большая карта по M, отдельная панель"));
            Ui.Row(stack, Ui.Hint("• Свои плагины: компилируются и выполняются самим лаунчером"));
            Ui.Row(stack, Ui.Hint("• Макросы: глобальные горячие клавиши и редактируемые сценарии"));
            Ui.Row(stack, Ui.Hint("• Профили: экспорт и импорт всех настроек в zip"));
            Ui.Row(stack, Ui.Hint("• Обновления: автоматическая проверка релизов на GitHub"));
            Ui.Row(stack, Ui.Hint("• Сторонний HUD — по желанию: запуск, hud.ini, файлы и .cs-плагины"));

            Ui.Row(stack, Ui.Hint(
                "Версия: " + Program.Version + "   •   Платформа: .NET Framework 4.8"));

            return card;
        }

        private TableLayoutPanel BuildTurboCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Плагины TurboHUD", out stack);

            Ui.Row(stack, Ui.Hint(
                "С версии 1.5.0 SuperD3Hud понимает публичный plugin-API TurboHUD. " +
                "Совместимость собрана из открытых исходников интерфейсов: сторонний движок " +
                "не используется, закрытые файлы не расшифровываются."));

            Ui.Row(stack, Ui.Hint(
                "• Своя Turbo.Plugins.dll из 160 открытых файлов интерфейсов"));
            Ui.Row(stack, Ui.Hint(
                "• Свой BasePlugin, совместимый с плагинами TurboHUD"));
            Ui.Row(stack, Ui.Hint(
                "• Папка plugins-turbohud: компиляция и запуск .cs при старте"));
            Ui.Row(stack, Ui.Hint(
                "• Хуки AfterCollect, BeforeRender, PaintTopInGame и PaintWorld"));

            return card;
        }

        private TableLayoutPanel BuildMapCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Карта", out stack);

            Ui.Row(stack, Ui.Hint(
                "Карта — это собственная реализация, а не обёртка над закрытым ядром стороннего оверлея. " +
                "Она читает сцены и навигационные ячейки из памяти игры и рисует их своим оверлеем."));

            Ui.Row(stack, Ui.Hint(
                "Чтение только на чтение: PROCESS_VM_READ. Память игры не изменяется, код не внедряется, " +
                "потоки не приостанавливаются."));

            Ui.Row(stack, Ui.Hint(
                "Поддерживаемая сборка: " + MapReader.SupportedVersion + ". Проверка идёт по SHA-256 клиента " +
                "и по байтам итератора сцен, поэтому на другой версии карта отключается."));

            return card;
        }

        private TableLayoutPanel BuildHonestyCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Границы и честность", out stack);

            Ui.Row(stack, Ui.Hint(
                "SuperD3Hud не расшифровывает закрытые плагины, не подменяет проверку подписки и не обращается " +
                "к серверам сторонних разработчиков."));

            Ui.Row(stack, Ui.Hint(
                "Лаунчер работает с тем, что уже установлено на компьютере, и управляет только открытыми " +
                ".cs-плагинами. Коммерческие плагины сторонних авторов требуют действующей лицензии у них."));

            Ui.Row(stack, Ui.Hint(
                "Макросы — это собственная реализация горячих клавиш. Используйте их на свой риск " +
                "и в рамках правил игры."));

            return card;
        }

        private TableLayoutPanel BuildFilesCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Обновления и ярлык", out stack);

            _updateStatus = Ui.Hint("");
            Ui.Row(stack, _updateStatus);

            TableLayoutPanel row = new TableLayoutPanel();
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.BackColor = Color.Transparent;
            row.Margin = new Padding(0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            row.Controls.Add(Ui.Field("Репозиторий обновлений"), 0, 0);
            _updateRepo = Ui.Input();
            _updateRepo.Width = 250;
            _updateRepo.Margin = new Padding(0, 2, 0, 2);
            Ui.Tip(_updateRepo, "Например: owner/repo. С этого адреса берётся последний релиз GitHub.");
            row.Controls.Add(_updateRepo, 1, 0);
            Ui.Row(stack, row, true);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Проверить сейчас", delegate { CheckUpdates(); }),
                Ui.Secondary("Создать ярлык", delegate { CreateShortcut(); })));

            Ui.Row(stack, Ui.Hint(
                "Исходный код лаунчера распространяется по лицензии MIT. Сторонние программы " +
                "(HUD-движки, GodTool, Diablo III) принадлежат их правообладателям и не входят в этот проект."));

            Button config = Ui.Secondary("Открыть config", delegate { PluginManager.OpenFolder(AppPaths.ConfigDir); });
            Button readme = Ui.Secondary("Открыть README", delegate { OpenReadme(); });
            Ui.Tip(config, "Настройки, профили, заметки и резервные копии.");
            Ui.Row(stack, Ui.Buttons(config, readme));

            return card;
        }

        private void CheckUpdates()
        {
            string repo = _updateRepo.Text.Trim();
            AppPaths.Settings.UpdateRepo = repo;
            AppPaths.Settings.Save();

            if (string.IsNullOrEmpty(repo))
            {
                SetUpdateStatus("Укажите репозиторий в виде owner/repo.", Ui.Accent);
                return;
            }

            SetUpdateStatus("Проверка GitHub...", Ui.MutedColor);
            Application.DoEvents();

            UpdateInfo info = UpdateChecker.Check(repo, Program.Version);
            if (!string.IsNullOrEmpty(info.Error))
            {
                SetUpdateStatus("Не удалось проверить: " + info.Error, Ui.DangerColor);
                return;
            }

            if (info.Available)
                SetUpdateStatus("Доступна версия " + info.LatestVersion + " (у вас " +
                                Program.Version + ").", Ui.Accent);
            else
                SetUpdateStatus("У вас последняя версия (" + Program.Version + ").", Ui.Ok);
        }

        private void SetUpdateStatus(string text, Color color)
        {
            if (_updateStatus == null) return;
            _updateStatus.Text = text;
            _updateStatus.ForeColor = color;
        }

        private void CreateShortcut()
        {
            string path = Shortcut.CreateNextToProgramFolder();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "Не удалось создать ярлык.", "Ярлык");
                return;
            }
            MessageBox.Show(this, "Ярлык создан:\n" + path, "Ярлык",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenReadme()
        {
            string path = Path.Combine(AppPaths.AppRoot, "README.md");
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "README.md не найден.", "README");
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "README"); }
        }
    }
}
