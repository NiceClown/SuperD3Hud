using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public class HudPage : UserControl
    {
        private static readonly string[] Locales =
        {
            "ruRU", "enUS", "deDE", "esES", "esMX", "frFR", "itIT",
            "koKR", "plPL", "ptBR", "ptPT", "zhCN", "zhTW"
        };
        private static readonly string[] Servers = { "US", "EU", "KR", "TW", "CN" };
        private static readonly string[] KnownKeys =
        {
            "locale", "server", "ptr", "http", "dx10", "bit", "nowin7x64", "Cloud", "cache"
        };

        private readonly MainForm _main;
        private IniFile _ini;
        private ComboBox _locale;
        private ComboBox _server;
        private NumericUpDown _cache;
        private NumericUpDown _bit;
        private CheckBox _ptr;
        private CheckBox _http;
        private CheckBox _dx10;
        private CheckBox _nowin;
        private CheckBox _cloud;
        private DataGridView _grid;
        private Label _pathLabel;
        private FlowLayoutPanel _flow;

        public HudPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            Reload();
        }

        private void Build()
        {
            Controls.Add(Ui.Header("Настройки HUD", "Правка hud.ini и локальные резервные копии. Файл читается и пишется напрямую."));

            _flow = new FlowLayoutPanel();
            _flow.Dock = DockStyle.Fill;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.AutoScroll = true;
            _flow.BackColor = Color.Transparent;
            _flow.Padding = new Padding(0, 12, 0, 0);
            _flow.Resize += delegate { FitCards(); };
            Controls.Add(_flow);
            _flow.BringToFront();

            // Path card
            Panel pathCard = Ui.Card();
            pathCard.Width = 900;
            pathCard.Height = 108;
            Label t = Ui.Section("Файл конфигурации");
            t.Location = new Point(16, 12);
            _pathLabel = Ui.Muted("");
            _pathLabel.Location = new Point(16, 38);
            _pathLabel.MaximumSize = new Size(860, 18);
            FlowLayoutPanel tb = Ui.Toolbar();
            tb.Location = new Point(12, 62);
            tb.Controls.Add(Ui.Secondary("Перечитать", delegate { Reload(); }));
            tb.Controls.Add(Ui.Secondary("Открыть hud.ini", delegate { OpenHudIni(); }));
            tb.Controls.Add(Ui.Secondary("Резервная копия", delegate { Backup(); }));
            tb.Controls.Add(Ui.Secondary("Исправить app.config", delegate { FixAppConfig(); }));
            pathCard.Controls.Add(t);
            pathCard.Controls.Add(_pathLabel);
            pathCard.Controls.Add(tb);
            _flow.Controls.Add(pathCard);

            // Settings card
            Panel settings = Ui.Card();
            settings.Width = 900;
            settings.Height = 236;
            Label st = Ui.Section("Основные параметры");
            st.Location = new Point(16, 12);
            settings.Controls.Add(st);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Location = new Point(16, 42);
            table.Size = new Size(860, 92);
            table.ColumnCount = 4;
            table.RowCount = 3;
            table.BackColor = Color.Transparent;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));

            table.Controls.Add(FieldLabel("Язык (locale)"), 0, 0);
            _locale = Ui.Combo();
            _locale.Width = 220;
            foreach (string l in Locales) _locale.Items.Add(l);
            table.Controls.Add(_locale, 1, 0);

            table.Controls.Add(FieldLabel("Сервер"), 2, 0);
            _server = Ui.Combo();
            _server.Width = 220;
            foreach (string s in Servers) _server.Items.Add(s);
            table.Controls.Add(_server, 3, 0);

            table.Controls.Add(FieldLabel("Кэш (cache)"), 0, 1);
            _cache = MakeNumeric();
            table.Controls.Add(_cache, 1, 1);

            table.Controls.Add(FieldLabel("Биты (bit)"), 2, 1);
            _bit = MakeNumeric();
            table.Controls.Add(_bit, 3, 1);

            FlowLayoutPanel checks = new FlowLayoutPanel();
            checks.AutoSize = true;
            checks.BackColor = Color.Transparent;
            checks.WrapContents = true;
            checks.Dock = DockStyle.Fill;
            _ptr = Ui.DarkCheck("PTR");
            _http = Ui.DarkCheck("HTTP");
            _dx10 = Ui.DarkCheck("DirectX 10");
            _nowin = Ui.DarkCheck("Без проверки Windows 7 x64");
            _cloud = Ui.DarkCheck("Cloud");
            checks.Controls.Add(_ptr);
            checks.Controls.Add(_http);
            checks.Controls.Add(_dx10);
            checks.Controls.Add(_nowin);
            checks.Controls.Add(_cloud);
            table.Controls.Add(checks, 0, 2);
            table.SetColumnSpan(checks, 4);
            settings.Controls.Add(table);

            FlowLayoutPanel sb = Ui.Toolbar();
            sb.Location = new Point(12, 150);
            sb.Controls.Add(Ui.Primary("Сохранить hud.ini", delegate { Save(); }));
            sb.Controls.Add(Ui.Secondary("Сбросить изменения", delegate { Reload(); }));
            settings.Controls.Add(sb);

            Label note = Ui.Muted("Секция [log] и служебные ключи не изменяются. Перед записью создаётся копия в config\\backups.");
            note.Location = new Point(16, 192);
            note.AutoSize = false;
            note.Size = new Size(860, 20);
            settings.Controls.Add(note);
            _flow.Controls.Add(settings);

            // Advanced grid
            Panel adv = Ui.Card();
            adv.Width = 900;
            adv.Height = 330;
            Label at = Ui.Section("Все ключи секции [config]");
            at.Location = new Point(16, 12);
            Label am = Ui.Muted("Можно добавлять, удалять и редактировать любые ключи. Известные параметры выше имеют приоритет.");
            am.Location = new Point(16, 38);
            _grid = new DataGridView();
            _grid.Location = new Point(16, 64);
            _grid.Size = new Size(860, 210);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grid.AllowUserToAddRows = true;
            _grid.AllowUserToDeleteRows = true;
            _grid.Columns.Add("key", "Ключ");
            _grid.Columns.Add("value", "Значение");
            _grid.Columns[0].FillWeight = 35;
            _grid.Columns[1].FillWeight = 65;
            Ui.StyleGrid(_grid);
            adv.Controls.Add(at);
            adv.Controls.Add(am);
            adv.Controls.Add(_grid);
            _flow.Controls.Add(adv);

            FitCards();
        }

        private static Label FieldLabel(string text)
        {
            Label l = Ui.Body(text);
            l.Margin = new Padding(0, 6, 6, 0);
            return l;
        }

        private static NumericUpDown MakeNumeric()
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0;
            n.Maximum = 100000;
            n.Width = 220;
            n.BackColor = Ui.Panel2;
            n.ForeColor = Ui.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = Ui.FontBody;
            return n;
        }

        private void FitCards()
        {
            Ui.FitCards(_flow, 620);
        }

        public void Reload()
        {
            string path = AppPaths.HudIni;
            if (string.IsNullOrEmpty(path))
            {
                // No other HUD is installed: the page has nothing to edit,
                // and the launcher itself hosts the plugins.
                _pathLabel.Text = "hud.ini не найден: другой HUD не установлен";
                return;
            }
            _pathLabel.Text = path;
            if (!File.Exists(path))
            {
                _main.Log("hud.ini не найден: " + path);
                return;
            }
            _ini = new IniFile(path);
            _ini.Load();

            SetCombo(_locale, _ini.Get("config", "locale", "ruRU"));
            SetCombo(_server, _ini.Get("config", "server", "US"));
            _cache.Value = Clamp(_ini.GetInt("config", "cache", 0), _cache);
            _bit.Value = Clamp(_ini.GetInt("config", "bit", 0), _bit);
            _ptr.Checked = _ini.GetBool("config", "ptr", false);
            _http.Checked = _ini.GetBool("config", "http", false);
            _dx10.Checked = _ini.GetBool("config", "dx10", false);
            _nowin.Checked = _ini.GetBool("config", "nowin7x64", false);
            _cloud.Checked = _ini.GetBool("config", "Cloud", false);

            _grid.Rows.Clear();
            foreach (KeyValuePair<string, string> kv in _ini.GetSection("config"))
            {
                if (IsKnown(kv.Key)) continue;
                _grid.Rows.Add(kv.Key, kv.Value);
            }
            _main.SetStatus("hud.ini загружен.");
        }

        private static bool IsKnown(string key)
        {
            foreach (string k in KnownKeys)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void SetCombo(ComboBox combo, string value)
        {
            int idx = combo.Items.IndexOf(value);
            if (idx < 0)
            {
                combo.Items.Add(value);
                idx = combo.Items.Count - 1;
            }
            combo.SelectedIndex = idx;
        }

        private static decimal Clamp(int value, NumericUpDown n)
        {
            if (value < n.Minimum) return n.Minimum;
            if (value > n.Maximum) return n.Maximum;
            return value;
        }

        private void Save()
        {
            try
            {
                if (_ini == null) Reload();
                if (_ini == null) return;
                string backup = ProcessRunner.BackupHudIni();

                _ini.Set("config", "locale", Convert.ToString(_locale.SelectedItem));
                _ini.Set("config", "server", Convert.ToString(_server.SelectedItem));
                _ini.Set("config", "cache", ((int)_cache.Value).ToString());
                _ini.Set("config", "bit", ((int)_bit.Value).ToString());
                _ini.SetBool("config", "ptr", _ptr.Checked);
                _ini.SetBool("config", "http", _http.Checked);
                _ini.SetBool("config", "dx10", _dx10.Checked);
                _ini.SetBool("config", "nowin7x64", _nowin.Checked);
                _ini.SetBool("config", "Cloud", _cloud.Checked);

                // Remove unknown keys first, then re-add those present in the grid.
                List<string> existing = new List<string>();
                foreach (KeyValuePair<string, string> kv in _ini.GetSection("config"))
                    if (!IsKnown(kv.Key)) existing.Add(kv.Key);
                foreach (string key in existing) _ini.Remove("config", key);

                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    string key = Convert.ToString(row.Cells[0].Value);
                    string val = Convert.ToString(row.Cells[1].Value);
                    if (string.IsNullOrEmpty(key) || key.Trim().Length == 0) continue;
                    if (IsKnown(key.Trim())) continue;
                    _ini.Set("config", key.Trim(), val == null ? "" : val.Trim());
                }

                _ini.Save();
                _main.Log("hud.ini сохранён" + (string.IsNullOrEmpty(backup) ? "." : ". Резервная копия: " + Path.GetFileName(backup)));
                _main.SetStatus("hud.ini сохранён.");
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenHudIni()
        {
            if (!File.Exists(AppPaths.HudIni))
            {
                MessageBox.Show(this, "hud.ini не найден.", "Файл", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo(AppPaths.HudIni) { UseShellExecute = true });
        }

        private void Backup()
        {
            try
            {
                string file = ProcessRunner.BackupHudIni();
                if (string.IsNullOrEmpty(file))
                {
                    MessageBox.Show(this, "hud.ini не найден.", "Резервная копия");
                    return;
                }
                _main.Log("Резервная копия: " + file);
                _main.SetStatus("Резервная копия создана.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FixAppConfig()
        {
            try
            {
                ProcessRunner.FixAppConfig();
                _main.Log("app.config: путь Roslyn исправлен на " + AppPaths.RoslynDir);
                _main.SetStatus("app.config исправлен.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "app.config", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}



