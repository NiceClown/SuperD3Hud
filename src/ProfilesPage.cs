using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public class ProfilesPage : UserControl
    {
        private readonly MainForm _main;
        private TextBox _name;
        private ListView _list;

        public ProfilesPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            RefreshList();
        }

        private void Build()
        {
            Controls.Add(Ui.Header("Профили",
                "Экспорт и импорт всех локальных настроек в один zip-файл. Без облака и без аккаунта."));

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Color.Transparent;
            body.Padding = new Padding(0, 12, 0, 0);
            Controls.Add(body);
            body.BringToFront();

            Panel export = Ui.Card();
            export.Dock = DockStyle.Top;
            export.Height = 104;
            Label et = Ui.Section("Создать профиль");
            et.Location = new Point(16, 12);
            Label em = Ui.Muted("Будут сохранены: settings.ini, macros.json, hud.ini, PluginsConfig.dat, заметки и настройки плагинов.");
            em.Location = new Point(16, 38);
            _name = Ui.Input();
            _name.Location = new Point(16, 62);
            _name.Width = 300;
            _name.Text = "profile-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
            FlowLayoutPanel eb = Ui.Toolbar();
            eb.Location = new Point(320, 56);
            eb.Controls.Add(Ui.Primary("Экспорт", delegate { Export(); }));
            eb.Controls.Add(Ui.Secondary("Открыть папку", delegate { PluginManager.OpenFolder(AppPaths.BackupsDir); }));
            export.Controls.Add(et);
            export.Controls.Add(em);
            export.Controls.Add(_name);
            export.Controls.Add(eb);
            body.Controls.Add(export);

            Panel listCard = Ui.Card();
            listCard.Dock = DockStyle.Fill;
            listCard.Padding = new Padding(12);
            Label lt = Ui.Section("Сохранённые профили");
            lt.Dock = DockStyle.Top;
            lt.Height = 28;
            listCard.Controls.Add(lt);

            FlowLayoutPanel lb = Ui.Toolbar();
            lb.Dock = DockStyle.Bottom;
            lb.Controls.Add(Ui.Primary("Восстановить", delegate { Restore(); }));
            lb.Controls.Add(Ui.Secondary("Обновить", delegate { RefreshList(); }));
            lb.Controls.Add(Ui.Danger("Удалить", delegate { Delete(); }));
            listCard.Controls.Add(lb);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Файл", 420);
            _list.Columns.Add("Размер", 100);
            _list.Columns.Add("Изменён", 180);
            Ui.StyleListView(_list);
            _list.DoubleClick += delegate { Restore(); };
            listCard.Controls.Add(_list);
            _list.BringToFront();
            body.Controls.Add(listCard);

            Panel info = Ui.Card();
            info.Dock = DockStyle.Bottom;
            info.Height = 92;
            Label it = Ui.Section("Важно");
            it.Location = new Point(16, 12);
            Label im = Ui.Muted("Перед импортом закройте Diablo III и HUD. Восстановление перезапишет текущие настройки. " +
                "Резервные копии hud.ini также лежат в config\\backups.");
            im.Location = new Point(16, 38);
            im.MaximumSize = new Size(880, 40);
            info.Controls.Add(it);
            info.Controls.Add(im);
            body.Controls.Add(info);
            listCard.BringToFront();
        }

        public void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (string file in ProfileManager.ListBackups())
            {
                FileInfo fi = new FileInfo(file);
                ListViewItem item = new ListViewItem(Path.GetFileName(file));
                item.SubItems.Add((fi.Length / 1024.0).ToString("0.0") + " КБ");
                item.SubItems.Add(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                item.Tag = file;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }

        private void Export()
        {
            try
            {
                string file = ProfileManager.Export(_name.Text);
                _main.Log("Профиль создан: " + file);
                _main.SetStatus("Профиль создан.");
                RefreshList();
                MessageBox.Show(this, "Профиль сохранён:\n" + file, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Restore()
        {
            if (_list.SelectedItems.Count == 0) return;
            string file = Convert.ToString(_list.SelectedItems[0].Tag);
            if (MessageBox.Show(this,
                "Восстановить профиль?\n\n" + Path.GetFileName(file) + "\n\nТекущие настройки будут перезаписаны.",
                "Импорт", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string result = ProfileManager.Import(file);
                _main.ReloadAll();
                _main.Log("Профиль восстановлен: " + Path.GetFileName(file));
                MessageBox.Show(this, result, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Delete()
        {
            if (_list.SelectedItems.Count == 0) return;
            string file = Convert.ToString(_list.SelectedItems[0].Tag);
            if (MessageBox.Show(this, "Удалить профиль " + Path.GetFileName(file) + "?", "Удаление",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                ProfileManager.Delete(file);
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}


