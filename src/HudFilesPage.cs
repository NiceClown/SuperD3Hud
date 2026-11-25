using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace SuperD3Hud
{
    /// <summary>
    /// Editor for the engine's own plain-text configuration files:
    /// config/config.xml, config/hotkeys.xml and the pickit loot filter INI.
    /// Everything is read and written locally; nothing is sent anywhere.
    /// </summary>
    public class HudFilesPage : UserControl
    {
        private readonly MainForm _main;

        // config.xml
        private CheckBox _autoHide;
        private CheckBox _autoTerminate;
        private CheckBox _alwaysShowVersion;
        private CheckBox _paragonCapture;
        private CheckBox _tcpServer;
        private CheckBox _httpServer;
        private NumericUpDown _framerate;
        private NumericUpDown _tcpPort;
        private TextBox _httpListen;
        private TextBox _windowTitle;
        private TextBox _theme;
        private Label _configState;

        // hotkeys.xml
        private DataGridView _hotkeysGrid;
        private Label _hotkeysState;

        // pickit
        private ComboBox _pickitFile;
        private TextBox _pickitText;
        private Label _pickitState;

        public HudFilesPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            ReloadAll();
        }

        private void Build()
        {
            Controls.Add(Ui.Header("Файлы HUD",
                "config.xml, hotkeys.xml и фильтр лута pickit. Локальные файлы, без аккаунта и сети."));

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 10f);
            tabs.Padding = new Point(16, 6);
            Controls.Add(tabs);
            tabs.BringToFront();

            tabs.TabPages.Add(BuildConfigTab());
            tabs.TabPages.Add(BuildHotkeysTab());
            tabs.TabPages.Add(BuildPickitTab());
        }

        private TabPage NewTab(string title)
        {
            TabPage page = new TabPage(title);
            page.BackColor = Ui.Bg;
            page.Padding = new Padding(14);
            return page;
        }

        // ------------------------------------------------------------------
        // config.xml
        // ------------------------------------------------------------------

        private TabPage BuildConfigTab()
        {
            TabPage page = NewTab("config.xml");

            Panel card = Ui.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16);
            page.Controls.Add(card);

            Label t = Ui.Section("Общие параметры HUD");
            t.Location = new Point(16, 14);

            _autoHide = Ui.DarkCheck("Автоматически скрывать HUD вне игры (auto_hide_hud)");
            _autoHide.Location = new Point(16, 52);
            _autoTerminate = Ui.DarkCheck("Закрывать HUD вместе с игрой (auto_terminate)");
            _autoTerminate.Location = new Point(16, 76);
            _alwaysShowVersion = Ui.DarkCheck("Всегда показывать версию (always_show_version)");
            _alwaysShowVersion.Location = new Point(16, 100);
            _paragonCapture = Ui.DarkCheck("Скриншот при новом уровне парагона (paragon_capture)");
            _paragonCapture.Location = new Point(16, 124);

            Label fl = Ui.Body("Целевой FPS:");
            fl.Location = new Point(16, 160);
            _framerate = MakeNumeric(1, 300, 80);
            _framerate.Location = new Point(150, 156);

            Label wl = Ui.Body("Заголовок окна игры:");
            wl.Location = new Point(260, 160);
            _windowTitle = Ui.Input();
            _windowTitle.Location = new Point(430, 156);
            _windowTitle.Width = 300;

            Label tl = Ui.Body("Тема:");
            tl.Location = new Point(16, 196);
            _theme = Ui.Input();
            _theme.Location = new Point(150, 192);
            _theme.Width = 160;

            Label sv = Ui.Body("TCP-сервер (внутренний):");
            sv.Location = new Point(330, 196);
            _tcpServer = Ui.DarkCheck("включён");
            _tcpServer.Location = new Point(500, 196);
            _tcpPort = MakeNumeric(1, 65535, 80);
            _tcpPort.Location = new Point(590, 192);

            Label hl = Ui.Body("HTTP-сервер:");
            hl.Location = new Point(16, 232);
            _httpServer = Ui.DarkCheck("включён");
            _httpServer.Location = new Point(150, 232);
            _httpListen = Ui.Input();
            _httpListen.Location = new Point(240, 228);
            _httpListen.Width = 200;

            FlowLayoutPanel bar = Ui.Toolbar();
            bar.Location = new Point(12, 268);
            bar.Controls.Add(Ui.Primary("Сохранить config.xml", delegate { SaveConfig(); }));
            bar.Controls.Add(Ui.Secondary("Перечитать", delegate { LoadConfig(); }));
            bar.Controls.Add(Ui.Secondary("Открыть файл", delegate { OpenFile(AppPaths.ConfigXml); }));

            _configState = Ui.Muted("");
            _configState.Location = new Point(16, 310);
            _configState.AutoSize = false;
            _configState.Size = new Size(900, 20);
            _configState.AutoEllipsis = true;

            Label note = Ui.Muted("Изменения вступят в силу после перезапуска HUD. Перед записью создаётся копия в config\\backups.");
            note.Location = new Point(16, 334);
            note.AutoSize = false;
            note.Size = new Size(900, 20);

            card.Controls.Add(t);
            card.Controls.Add(_autoHide);
            card.Controls.Add(_autoTerminate);
            card.Controls.Add(_alwaysShowVersion);
            card.Controls.Add(_paragonCapture);
            card.Controls.Add(fl);
            card.Controls.Add(_framerate);
            card.Controls.Add(wl);
            card.Controls.Add(_windowTitle);
            card.Controls.Add(tl);
            card.Controls.Add(_theme);
            card.Controls.Add(sv);
            card.Controls.Add(_tcpServer);
            card.Controls.Add(_tcpPort);
            card.Controls.Add(hl);
            card.Controls.Add(_httpServer);
            card.Controls.Add(_httpListen);
            card.Controls.Add(bar);
            card.Controls.Add(_configState);
            card.Controls.Add(note);
            return page;
        }

        private static NumericUpDown MakeNumeric(int min, int max, int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Width = width;
            n.BackColor = Ui.Panel2;
            n.ForeColor = Ui.Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = Ui.FontBody;
            return n;
        }

        private void LoadConfig()
        {
            try
            {
                string path = AppPaths.ConfigXml;
                if (!File.Exists(path))
                {
                    _configState.Text = "config.xml не найден: " + path;
                    return;
                }
                XmlDocument doc = new XmlDocument();
                doc.PreserveWhitespace = false;
                doc.Load(path);

                XmlElement utility = doc.SelectSingleNode("/config/utility") as XmlElement;
                if (utility != null)
                {
                    _autoHide.Checked = Attr(utility, "auto_hide_hud") == "1";
                    _autoTerminate.Checked = Attr(utility, "auto_terminate") == "1";
                    _alwaysShowVersion.Checked = Attr(utility, "always_show_version") == "1";
                    _windowTitle.Text = Attr(utility, "diablo_window_title");
                    int fps;
                    if (int.TryParse(Attr(utility, "target_framerate"), out fps))
                        _framerate.Value = Math.Min(_framerate.Maximum, Math.Max(_framerate.Minimum, fps));
                }

                XmlElement capture = doc.SelectSingleNode("/config/utility/paragon_capture") as XmlElement;
                _paragonCapture.Checked = capture != null && Attr(capture, "enabled") == "1";

                XmlElement tcp = doc.SelectSingleNode("/config/tcp_server") as XmlElement;
                _tcpServer.Checked = tcp != null && Attr(tcp, "enabled") == "1";
                if (tcp != null)
                {
                    int port;
                    if (int.TryParse(Attr(tcp, "port"), out port))
                        _tcpPort.Value = Math.Min(_tcpPort.Maximum, Math.Max(_tcpPort.Minimum, port));
                }

                XmlElement http = doc.SelectSingleNode("/config/http_server") as XmlElement;
                _httpServer.Checked = http != null && Attr(http, "enabled") == "1";
                if (http != null) _httpListen.Text = Attr(http, "listen_on");

                XmlElement themes = doc.SelectSingleNode("/config/themes") as XmlElement;
                _theme.Text = themes == null ? "default" : Attr(themes, "current");

                _configState.Text = path;
                _configState.ForeColor = Ui.MutedColor;
            }
            catch (Exception ex)
            {
                _configState.Text = "Ошибка чтения: " + ex.Message;
                _configState.ForeColor = Ui.DangerColor;
            }
        }

        private static string Attr(XmlElement element, string name)
        {
            XmlAttribute a = element.Attributes[name];
            return a == null ? "" : a.Value;
        }

        private static void SetAttr(XmlElement element, string name, string value)
        {
            XmlAttribute a = element.Attributes[name];
            if (a == null)
            {
                a = element.OwnerDocument.CreateAttribute(name);
                element.Attributes.Append(a);
            }
            a.Value = value;
        }

        private void SaveConfig()
        {
            try
            {
                string path = AppPaths.ConfigXml;
                if (!File.Exists(path))
                {
                    MessageBox.Show(this, "config.xml не найден.", "config.xml", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                BackupFile(path);

                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                XmlElement utility = doc.SelectSingleNode("/config/utility") as XmlElement;
                if (utility == null)
                {
                    utility = doc.CreateElement("utility");
                    doc.DocumentElement.AppendChild(utility);
                }
                SetAttr(utility, "auto_hide_hud", _autoHide.Checked ? "1" : "0");
                SetAttr(utility, "auto_terminate", _autoTerminate.Checked ? "1" : "0");
                SetAttr(utility, "always_show_version", _alwaysShowVersion.Checked ? "1" : "0");
                SetAttr(utility, "target_framerate", ((int)_framerate.Value).ToString());
                if (_windowTitle.Text.Trim().Length > 0)
                    SetAttr(utility, "diablo_window_title", _windowTitle.Text.Trim());

                XmlElement capture = doc.SelectSingleNode("/config/utility/paragon_capture") as XmlElement;
                if (capture == null)
                {
                    capture = doc.CreateElement("paragon_capture");
                    utility.AppendChild(capture);
                }
                SetAttr(capture, "enabled", _paragonCapture.Checked ? "1" : "0");

                XmlElement tcp = doc.SelectSingleNode("/config/tcp_server") as XmlElement;
                if (tcp == null)
                {
                    tcp = doc.CreateElement("tcp_server");
                    doc.DocumentElement.AppendChild(tcp);
                }
                SetAttr(tcp, "enabled", _tcpServer.Checked ? "1" : "0");
                SetAttr(tcp, "port", ((int)_tcpPort.Value).ToString());

                XmlElement http = doc.SelectSingleNode("/config/http_server") as XmlElement;
                if (http == null)
                {
                    http = doc.CreateElement("http_server");
                    doc.DocumentElement.AppendChild(http);
                }
                SetAttr(http, "enabled", _httpServer.Checked ? "1" : "0");
                SetAttr(http, "listen_on", _httpListen.Text.Trim());

                XmlElement themes = doc.SelectSingleNode("/config/themes") as XmlElement;
                if (themes == null)
                {
                    themes = doc.CreateElement("themes");
                    doc.DocumentElement.AppendChild(themes);
                }
                SetAttr(themes, "current", _theme.Text.Trim());

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.Encoding = new UTF8Encoding(false);
                using (XmlWriter writer = XmlWriter.Create(path, settings))
                    doc.Save(writer);

                LoadConfig();
                _main.Log("config.xml сохранён.");
                _main.SetStatus("config.xml сохранён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "config.xml", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------
        // hotkeys.xml
        // ------------------------------------------------------------------

        private TabPage BuildHotkeysTab()
        {
            TabPage page = NewTab("hotkeys.xml");

            Panel card = Ui.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16);
            page.Controls.Add(card);

            Label t = Ui.Section("Горячие клавиши самого HUD");
            t.Location = new Point(16, 14);
            Label s = Ui.Muted("Двойной клик по ячейке — редактирование. Модификатор: пусто, Ctrl, Alt, Shift, Ctrl+Alt.");
            s.Location = new Point(16, 42);

            _hotkeysGrid = new DataGridView();
            _hotkeysGrid.Location = new Point(16, 70);
            _hotkeysGrid.Size = new Size(880, 380);
            _hotkeysGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _hotkeysGrid.AllowUserToAddRows = false;
            _hotkeysGrid.AllowUserToDeleteRows = false;
            _hotkeysGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _hotkeysGrid.Columns.Add("action", "Действие");
            _hotkeysGrid.Columns.Add("modifier", "Модификатор");
            _hotkeysGrid.Columns.Add("key", "Клавиша");
            _hotkeysGrid.Columns[0].ReadOnly = true;
            _hotkeysGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _hotkeysGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _hotkeysGrid.Columns[0].FillWeight = 50;
            _hotkeysGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _hotkeysGrid.Columns[1].FillWeight = 28;
            _hotkeysGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _hotkeysGrid.Columns[2].FillWeight = 22;
            Ui.StyleGrid(_hotkeysGrid);

            FlowLayoutPanel bar = Ui.Toolbar();
            bar.Location = new Point(12, 458);
            bar.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            bar.Controls.Add(Ui.Primary("Сохранить hotkeys.xml", delegate { SaveHotkeys(); }));
            bar.Controls.Add(Ui.Secondary("Перечитать", delegate { LoadHotkeys(); }));
            bar.Controls.Add(Ui.Secondary("Открыть файл", delegate { OpenFile(AppPaths.HotkeysXml); }));

            _hotkeysState = Ui.Muted("");
            _hotkeysState.Location = new Point(16, 500);
            _hotkeysState.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _hotkeysState.AutoSize = false;
            _hotkeysState.Size = new Size(880, 20);
            _hotkeysState.AutoEllipsis = true;

            card.Controls.Add(t);
            card.Controls.Add(s);
            card.Controls.Add(_hotkeysGrid);
            card.Controls.Add(bar);
            card.Controls.Add(_hotkeysState);
            return page;
        }

        private void LoadHotkeys()
        {
            try
            {
                string path = AppPaths.HotkeysXml;
                _hotkeysGrid.Rows.Clear();
                if (!File.Exists(path))
                {
                    _hotkeysState.Text = "hotkeys.xml не найден: " + path;
                    return;
                }
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNodeList nodes = doc.SelectNodes("/config/hotkeys/*");
                if (nodes != null)
                {
                    foreach (XmlNode node in nodes)
                    {
                        XmlElement el = node as XmlElement;
                        if (el == null) continue;
                        _hotkeysGrid.Rows.Add(el.Name, Attr(el, "modifier"), Attr(el, "key"));
                    }
                }
                _hotkeysState.Text = path;
                _hotkeysState.ForeColor = Ui.MutedColor;
            }
            catch (Exception ex)
            {
                _hotkeysState.Text = "Ошибка чтения: " + ex.Message;
                _hotkeysState.ForeColor = Ui.DangerColor;
            }
        }

        private void SaveHotkeys()
        {
            try
            {
                string path = AppPaths.HotkeysXml;
                if (!File.Exists(path))
                {
                    MessageBox.Show(this, "hotkeys.xml не найден.", "hotkeys.xml", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                BackupFile(path);

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNode hotkeys = doc.SelectSingleNode("/config/hotkeys");
                if (hotkeys == null)
                {
                    hotkeys = doc.CreateElement("hotkeys");
                    doc.DocumentElement.AppendChild(hotkeys);
                }

                foreach (DataGridViewRow row in _hotkeysGrid.Rows)
                {
                    if (row.IsNewRow) continue;
                    string action = Convert.ToString(row.Cells[0].Value);
                    string modifier = Convert.ToString(row.Cells[1].Value);
                    string key = Convert.ToString(row.Cells[2].Value);
                    if (string.IsNullOrEmpty(action)) continue;
                    if (string.IsNullOrEmpty(key)) continue;

                    XmlElement el = hotkeys.SelectSingleNode(action) as XmlElement;
                    if (el == null)
                    {
                        el = doc.CreateElement(action);
                        hotkeys.AppendChild(el);
                    }
                    SetAttr(el, "modifier", modifier == null ? "" : modifier.Trim());
                    SetAttr(el, "key", key.Trim());
                }

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = "\t";
                settings.Encoding = new UTF8Encoding(false);
                using (XmlWriter writer = XmlWriter.Create(path, settings))
                    doc.Save(writer);

                LoadHotkeys();
                _main.Log("hotkeys.xml сохранён.");
                _main.SetStatus("hotkeys.xml сохранён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "hotkeys.xml", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------
        // pickit
        // ------------------------------------------------------------------

        private TabPage BuildPickitTab()
        {
            TabPage page = NewTab("Фильтр лута (pickit)");

            Panel card = Ui.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16);
            page.Controls.Add(card);

            Label t = Ui.Section("Фильтр подбора предметов");
            t.Location = new Point(16, 14);

            _pickitFile = Ui.Combo();
            _pickitFile.Location = new Point(16, 44);
            _pickitFile.Width = 300;
            _pickitFile.Items.Add(new PickitItem("sc", "Softcore: pickit_sc_70.ini"));
            _pickitFile.Items.Add(new PickitItem("hc", "Hardcore: pickit_hc_70.ini"));
            _pickitFile.SelectedIndexChanged += delegate { LoadPickit(); };

            FlowLayoutPanel bar = Ui.Toolbar();
            bar.Location = new Point(330, 38);
            bar.Controls.Add(Ui.Primary("Сохранить", delegate { SavePickit(); }));
            bar.Controls.Add(Ui.Secondary("Перечитать", delegate { LoadPickit(); }));
            bar.Controls.Add(Ui.Secondary("Открыть файл", delegate { OpenPickit(); }));

            _pickitText = new TextBox();
            _pickitText.Multiline = true;
            _pickitText.ScrollBars = ScrollBars.Both;
            _pickitText.WordWrap = false;
            _pickitText.AcceptsTab = true;
            _pickitText.Font = Ui.FontMono;
            _pickitText.BackColor = Ui.Panel2;
            _pickitText.ForeColor = Ui.Text;
            _pickitText.BorderStyle = BorderStyle.FixedSingle;
            _pickitText.Location = new Point(16, 84);
            _pickitText.Size = new Size(880, 400);
            _pickitText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            _pickitState = Ui.Muted("");
            _pickitState.Location = new Point(16, 492);
            _pickitState.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _pickitState.AutoSize = false;
            _pickitState.Size = new Size(880, 20);
            _pickitState.AutoEllipsis = true;

            card.Controls.Add(t);
            card.Controls.Add(_pickitFile);
            card.Controls.Add(bar);
            card.Controls.Add(_pickitText);
            card.Controls.Add(_pickitState);
            return page;
        }

        private class PickitItem
        {
            public readonly string Key;
            private readonly string _text;
            public PickitItem(string key, string text) { Key = key; _text = text; }
            public override string ToString() { return _text; }
        }

        private string CurrentPickitPath()
        {
            PickitItem item = _pickitFile.SelectedItem as PickitItem;
            string key = item == null ? "sc" : item.Key;
            return key == "hc" ? AppPaths.PickitHc : AppPaths.PickitSc;
        }

        private void LoadPickit()
        {
            try
            {
                string path = CurrentPickitPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    _pickitText.Text = "";
                    _pickitState.Text = "Файл не найден: " + path;
                    _pickitState.ForeColor = Ui.DangerColor;
                    return;
                }
                _pickitText.Text = File.ReadAllText(path, Encoding.UTF8);
                _pickitState.Text = path;
                _pickitState.ForeColor = Ui.MutedColor;
            }
            catch (Exception ex)
            {
                _pickitState.Text = "Ошибка чтения: " + ex.Message;
                _pickitState.ForeColor = Ui.DangerColor;
            }
        }

        private void SavePickit()
        {
            try
            {
                string path = CurrentPickitPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    MessageBox.Show(this, "Файл фильтра не найден.", "pickit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                BackupFile(path);
                File.WriteAllText(path, _pickitText.Text, new UTF8Encoding(false));
                _main.Log("Фильтр лута сохранён: " + Path.GetFileName(path));
                _main.SetStatus("Фильтр лута сохранён.");
                LoadPickit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "pickit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenPickit()
        {
            string path = CurrentPickitPath();
            OpenFile(path);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public void ReloadAll()
        {
            LoadConfig();
            LoadHotkeys();
            if (_pickitFile.SelectedIndex < 0 && _pickitFile.Items.Count > 0) _pickitFile.SelectedIndex = 0;
            else LoadPickit();
        }

        private static void BackupFile(string path)
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            string name = Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(path, Path.Combine(AppPaths.BackupsDir, name), true);
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show(this, "Файл не найден:\n" + path, "Открыть", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Открыть", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}

