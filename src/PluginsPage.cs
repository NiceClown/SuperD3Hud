using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>
    /// Two kinds of plugins live here:
    ///
    /// * "Мои плагины" — plugins of SuperD3Hud itself. They are compiled and
    ///   executed by the launcher, so they work without any third-party HUD.
    /// * "Файлы HUD" — .cs files of an installed HUD engine. The launcher only
    ///   lists, enables and disables them; it never executes that code.
    /// </summary>
    public class PluginsPage : UserControl
    {
        private readonly MainForm _main;

        // Local plugins
        private ListView _localList;
        private TextBox _localSearch;
        private Label _localSummary;
        private Label _localName;
        private Label _localPath;
        private Label _localDesc;
        private Label _localState;
        private Label _localError;
        private Button _localToggle;
        private Plugins.PluginEntry _localSelected;
        private bool _localRefreshing;

        // TurboHUD compatibility plugins
        private ListView _turboList;
        private TextBox _turboSearch;
        private Label _turboSummary;
        private TurboHud.TurboPluginEntry _turboSelected;
        private bool _turboRefreshing;

        // Engine plugins
        private List<PluginInfo> _all = new List<PluginInfo>();
        private PluginInfo _selected;
        private ListView _list;
        private TextBox _search;
        private Label _summary;
        private Label _detailName;
        private Label _detailPath;
        private Label _detailDesc;
        private TextBox _notes;
        private TextBox _settings;
        private Dictionary<string, string> _notesMap;
        private bool _loadingDetails;
        private bool _engineRefreshing;
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            // ListView raises ItemChecked while its handle is being destroyed;
            // toggling a plugin from that callback rebuilds the list and makes
            // Dispose throw.
            _disposed = true;
            base.Dispose(disposing);
        }

        public PluginsPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            _notesMap = PluginManager.LoadNotes();
            Build();
            RefreshLocal();
            RefreshList();
        }

        private void Build()
        {
            Panel header = Ui.Header("Плагины",
                "Включайте и выключайте дополнения. В списке видно, что делает каждый плагин.");
            header.Height = 100;
            Label tip = Ui.Hint(
                "Галочка слева включает и выключает плагин. Серые строки выключены, красные — не собрались. " +
                "Подведите мышь к строке, чтобы прочитать полное описание.");
            tip.Dock = DockStyle.Bottom;
            tip.AutoSize = false;
            tip.Height = 36;
            tip.Padding = new Padding(2, 2, 0, 0);
            header.Controls.Add(tip);
            Controls.Add(header);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9.5f);
            tabs.Padding = new Point(12, 5);
            tabs.Margin = new Padding(0, 8, 0, 0);
            Controls.Add(tabs);
            tabs.BringToFront();

            TabPage local = new TabPage("Плагины SuperD3Hud");
            local.BackColor = Ui.Bg;
            local.Padding = new Padding(0, 8, 0, 0);
            tabs.TabPages.Add(local);
            BuildLocalTab(local);

            TabPage turbo = new TabPage("Плагины TurboHUD");
            turbo.BackColor = Ui.Bg;
            turbo.Padding = new Padding(0, 8, 0, 0);
            tabs.TabPages.Add(turbo);
            BuildTurboTab(turbo);

            TabPage engine = new TabPage("Движок HUD");
            engine.BackColor = Ui.Bg;
            engine.Padding = new Padding(0, 8, 0, 0);
            tabs.TabPages.Add(engine);
            BuildEngineTab(engine);
        }

        /// <summary>A search box with a small "Поиск:" caption.</summary>
        private static Panel SearchBox(TextBox box)
        {
            Panel host = new Panel();
            host.Width = 270;
            host.Height = 32;
            host.BackColor = Color.Transparent;

            Label caption = Ui.Muted("Поиск:");
            caption.Location = new Point(0, 8);
            caption.AutoSize = true;
            host.Controls.Add(caption);

            box.Location = new Point(52, 3);
            box.Width = 216;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            host.Controls.Add(box);
            return host;
        }

        /// <summary>True when the filter is empty or matches one of the fields.</summary>
        private static bool Matches(string filter, params string[] fields)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            foreach (string field in fields)
            {
                if (!string.IsNullOrEmpty(field) &&
                    field.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Most TurboHUD plugins do not describe themselves, so the description
        /// is built from the class name; without it the column would be empty.
        /// </summary>
        private static string TurboDescription(TurboHud.TurboPluginEntry entry)
        {
            if (entry == null) return "";
            if (!string.IsNullOrEmpty(entry.Description)) return entry.Description;
            try
            {
                PluginInfo info = new PluginInfo();
                info.Name = entry.ClassName ?? "";
                info.ClassName = entry.ClassName ?? "";
                info.Category = "";
                string text = PluginCatalog.Describe(info);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { }
            return "Плагин TurboHUD «" + entry.ClassName + "».";
        }

        // ------------------------------------------------------------------
        // Local plugins
        // ------------------------------------------------------------------

        private void BuildLocalTab(TabPage host)
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 40;
            bar.BackColor = Color.Transparent;

            _localSearch = Ui.Input();
            _localSearch.Width = 210;
            _localSearch.TextChanged += delegate { RefreshLocal(); };
            Ui.Tip(_localSearch, "Поиск по названию, описанию и файлу плагина.");
            Panel localSearch = SearchBox(_localSearch);
            localSearch.Location = new Point(0, 4);
            bar.Controls.Add(localSearch);

            FlowLayoutPanel localButtons = Ui.Toolbar();
            localButtons.Location = new Point(286, 4);
            localButtons.Controls.Add(Ui.Secondary("Перечитать", delegate { ReloadLocal(); }));
            localButtons.Controls.Add(Ui.Secondary("Открыть папку", delegate { PluginManager.OpenFolder(_main.PluginRuntime.Folder); }));
            localButtons.Controls.Add(Ui.Secondary("Импорт .cs", delegate { ImportLocal(); }));
            bar.Controls.Add(localButtons);
            host.Controls.Add(bar);

            _localSummary = Ui.Muted("Плагины не найдены.");
            _localSummary.Dock = DockStyle.Bottom;
            _localSummary.Height = 26;
            _localSummary.Padding = new Padding(2, 6, 0, 0);
            Ui.Tip(_localSummary, "Папка: " + _main.PluginRuntime.Folder);
            host.Controls.Add(_localSummary);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Ui.Bg;
            split.SplitterWidth = 10;
            split.SizeChanged += delegate
            {
                try
                {
                    int target = (int)(split.Width * 0.62);
                    if (target > 320 && target < split.Width - 320) split.SplitterDistance = target;
                }
                catch { }
            };
            host.Controls.Add(split);
            split.BringToFront();

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Ui.Panel;
            left.Padding = new Padding(10);
            split.Panel1.Controls.Add(left);

            _localList = new ListView();
            _localList.Dock = DockStyle.Fill;
            _localList.Columns.Add("Плагин", 190);
            _localList.Columns.Add("Состояние", 90);
            _localList.Columns.Add("Что делает", 300);
            _localList.CheckBoxes = true;
            _localList.ShowItemToolTips = true;
            Ui.StyleListView(_localList);
            _localList.SelectedIndexChanged += delegate { ShowLocal(); };
            _localList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { LocalChecked(e); };
            _localList.Resize += delegate { FitLocalColumns(); };
            left.Controls.Add(_localList);

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Ui.Panel;
            right.Padding = new Padding(16);
            split.Panel2.Controls.Add(right);

            _localName = Ui.Section("Выберите плагин");
            _localName.Dock = DockStyle.Top;
            _localName.Height = 34;
            _localName.AutoSize = false;
            _localName.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

            _localState = Ui.Muted("");
            _localState.Dock = DockStyle.Top;
            _localState.Height = 22;
            _localState.AutoSize = false;

            _localPath = Ui.Muted("");
            _localPath.Dock = DockStyle.Top;
            _localPath.Height = 36;
            _localPath.AutoSize = false;
            _localPath.AutoEllipsis = true;

            _localDesc = Ui.Body("");
            _localDesc.Dock = DockStyle.Top;
            _localDesc.Height = 110;
            _localDesc.AutoSize = false;
            _localDesc.MaximumSize = new Size(420, 110);

            _localError = Ui.Muted("");
            _localError.Dock = DockStyle.Top;
            _localError.Height = 60;
            _localError.AutoSize = false;
            _localError.ForeColor = Ui.DangerColor;

            _localToggle = Ui.Primary("Включить плагин", delegate { ToggleLocal(); });
            _localToggle.AutoSize = true;
            _localToggle.Enabled = false;
            Panel toggleHost = new Panel();
            toggleHost.Dock = DockStyle.Top;
            toggleHost.Height = 40;
            toggleHost.BackColor = Color.Transparent;
            _localToggle.Location = new Point(0, 2);
            toggleHost.Controls.Add(_localToggle);

            right.Controls.Add(_localError);
            right.Controls.Add(_localDesc);
            right.Controls.Add(_localPath);
            right.Controls.Add(_localState);
            right.Controls.Add(toggleHost);
            right.Controls.Add(_localName);

            Label note = Ui.Hint(
                "Эти плагины компилируются и выполняются самим SuperD3Hud: сторонний движок не нужен. " +
                "Файлы лежат в plugins-local, настройки — в config. Галочка в списке включает и выключает плагин.");
            note.Dock = DockStyle.Bottom;
            note.AutoSize = false;
            note.Height = 64;
            note.MaximumSize = new Size(420, 64);
            right.Controls.Add(note);
        }

        private void FitLocalColumns()
        {
            if (_localList == null || _localList.Columns.Count < 3) return;
            int width = _localList.ClientSize.Width;
            if (width < 260) return;
            int state = 90;
            int remaining = width - state - 26;
            int name = (int)(remaining * 0.42);
            int desc = remaining - name;
            if (name < 120) name = 120;
            if (desc < 120) desc = 120;
            _localList.Columns[0].Width = name;
            _localList.Columns[1].Width = state;
            _localList.Columns[2].Width = desc;
        }

        public void RefreshLocal()
        {
            if (_main.PluginRuntime == null || _localList == null) return;

            List<Plugins.PluginEntry> entries = _main.PluginRuntime.Plugins;
            string keepPath = _localSelected == null ? null : _localSelected.FilePath;
            string filter = _localSearch == null ? "" : _localSearch.Text.Trim();

            _localRefreshing = true;
            try
            {
                _localList.BeginUpdate();
                _localList.Items.Clear();

                int enabled = 0;
                int broken = 0;
                int shown = 0;
                foreach (Plugins.PluginEntry entry in entries)
                {
                    if (entry.Enabled && entry.Compiled) enabled++;
                    if (!entry.Compiled) broken++;
                    if (!Matches(filter, entry.ClassName, entry.Description, entry.RelativePath)) continue;

                    ListViewItem item = new ListViewItem(entry.ClassName);
                    item.SubItems.Add(entry.StateText);
                    item.SubItems.Add(entry.Description ?? "");
                    item.Tag = entry;
                    item.Checked = entry.Compiled && entry.Enabled;
                    item.ToolTipText = entry.Description ?? "";
                    item.ForeColor = !entry.Compiled ? Ui.DangerColor
                                    : (entry.Enabled ? Ui.Text : Ui.MutedColor);
                    _localList.Items.Add(item);
                    shown++;
                }
                _localList.EndUpdate();

                string summary = string.Format(
                    "Плагинов: {0}   Включено: {1}   С ошибками: {2}   Показано: {3}   Папка: {4}",
                    entries.Count, enabled, broken, shown, Path.GetFileName(_main.PluginRuntime.Folder));
                _localSummary.Text = summary;
            }
            finally
            {
                _localRefreshing = false;
            }

            ListViewItem select = null;
            if (keepPath != null)
            {
                foreach (ListViewItem item in _localList.Items)
                {
                    Plugins.PluginEntry entry = item.Tag as Plugins.PluginEntry;
                    if (entry != null && string.Equals(entry.FilePath, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        select = item;
                        break;
                    }
                }
            }
            if (select == null && _localList.Items.Count > 0) select = _localList.Items[0];
            if (select != null && _localList.SelectedItems.Count == 0) select.Selected = true;
        }

        private void ShowLocal()
        {
            if (_localList.SelectedItems.Count == 0)
            {
                _localSelected = null;
                _localName.Text = "Выберите плагин";
                _localState.Text = "";
                _localPath.Text = "";
                _localDesc.Text = "";
                _localError.Text = "";
                _localToggle.Text = "Включить плагин";
                _localToggle.Enabled = false;
                return;
            }

            Plugins.PluginEntry entry = _localList.SelectedItems[0].Tag as Plugins.PluginEntry;
            _localSelected = entry;
            if (entry == null) return;

            _localName.Text = entry.ClassName;
            _localState.Text = entry.StateText +
                (string.IsNullOrEmpty(entry.Version) ? "" : "   •   версия " + entry.Version) +
                (string.IsNullOrEmpty(entry.Author) ? "" : "   •   " + entry.Author);
            _localState.ForeColor = entry.Compiled
                ? (entry.Enabled ? Ui.Ok : Ui.MutedColor)
                : Ui.DangerColor;
            _localPath.Text = entry.FilePath;
            _localDesc.Text = entry.Description;
            _localError.Text = entry.Compiled ? "" : "Ошибка: " + (entry.Error ?? "неизвестно");

            _localToggle.Enabled = entry.Compiled;
            _localToggle.Text = !entry.Compiled ? "Плагин не собрался"
                : (entry.Enabled ? "Отключить плагин" : "Включить плагин");
        }

        private void LocalChecked(ItemCheckedEventArgs e)
        {
            if (_disposed || _main.IsShuttingDown || _localRefreshing) return;
            Plugins.PluginEntry entry = e.Item == null ? null : e.Item.Tag as Plugins.PluginEntry;
            if (entry == null) return;

            if (!entry.Compiled)
            {
                _localRefreshing = true;
                try { e.Item.Checked = false; }
                finally { _localRefreshing = false; }
                MessageBox.Show(this, "Плагин не собрался: " + (entry.Error ?? "неизвестно"),
                    "Плагин", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (entry.Enabled == e.Item.Checked) return;

            try
            {
                _main.PluginRuntime.SetEnabled(entry, e.Item.Checked);
                _main.Log((e.Item.Checked ? "Включён" : "Отключён") + " плагин: " + entry.ClassName);
                _main.SetStatus(e.Item.Checked ? "Плагин включён." : "Плагин отключён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагин", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshLocal();
        }

        private void ToggleLocal()
        {
            if (_localSelected == null || _main.PluginRuntime == null) return;
            try
            {
                bool state = !_localSelected.Enabled;
                _main.PluginRuntime.SetEnabled(_localSelected, state);
                _main.Log((state ? "Включён" : "Отключён") + " плагин: " + _localSelected.ClassName);
                _main.SetStatus(state ? "Плагин включён." : "Плагин отключён.");
                RefreshLocal();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагин", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReloadLocal()
        {
            if (_main.PluginRuntime == null) return;
            try
            {
                _main.PluginRuntime.Load();
                RefreshLocal();
                _main.SetStatus("Плагины перечитаны.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагины", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------
        // TurboHUD compatibility plugins
        // ------------------------------------------------------------------

        private void BuildTurboTab(TabPage host)
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 40;
            bar.BackColor = Color.Transparent;

            _turboSearch = Ui.Input();
            _turboSearch.Width = 210;
            _turboSearch.TextChanged += delegate { RefreshTurbo(); };
            Ui.Tip(_turboSearch, "Поиск по названию, описанию и файлу плагина.");
            Panel turboSearch = SearchBox(_turboSearch);
            turboSearch.Location = new Point(0, 4);
            bar.Controls.Add(turboSearch);

            FlowLayoutPanel turboButtons = Ui.Toolbar();
            turboButtons.Location = new Point(286, 4);
            turboButtons.Controls.Add(Ui.Secondary("Перечитать", delegate { ReloadTurbo(); }));
            turboButtons.Controls.Add(Ui.Secondary("Открыть папку", delegate { OpenTurboFolder(); }));
            turboButtons.Controls.Add(Ui.Secondary("Импорт .cs", delegate { ImportTurbo(); }));
            turboButtons.Controls.Add(Ui.Secondary("Импорт из TurboHUD", delegate { ImportFromTurboHUD(); }));
            bar.Controls.Add(turboButtons);
            host.Controls.Add(bar);

            _turboSummary = Ui.Muted("Плагины TurboHUD не найдены.");
            _turboSummary.Dock = DockStyle.Bottom;
            _turboSummary.Height = 26;
            _turboSummary.Padding = new Padding(2, 6, 0, 0);
            if (_main.TurboRuntime != null) Ui.Tip(_turboSummary, "Папка: " + _main.TurboRuntime.Folder);
            host.Controls.Add(_turboSummary);

            _turboList = new ListView();
            _turboList.Dock = DockStyle.Fill;
            _turboList.Columns.Add("Плагин", 190);
            _turboList.Columns.Add("Состояние", 90);
            _turboList.Columns.Add("Что делает", 320);
            _turboList.CheckBoxes = true;
            _turboList.ShowItemToolTips = true;
            Ui.StyleListView(_turboList);
            _turboList.SelectedIndexChanged += delegate { ShowTurbo(); };
            _turboList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { TurboChecked(e); };
            host.Controls.Add(_turboList);
            _turboList.BringToFront();

            Label note = Ui.Hint(
                "Это .cs-плагины для публичного API TurboHUD: SuperD3Hud компилирует их сам. " +
                "Галочка включает и выключает плагин, состояние запоминается. Если экран перегружен, " +
                "выключите лишние. Папка: plugins-turbohud рядом с программой.");
            note.Dock = DockStyle.Bottom;
            note.AutoSize = false;
            note.Height = 44;
            host.Controls.Add(note);

            RefreshTurbo();
        }

        public void RefreshTurbo()
        {
            if (_turboList == null) return;
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null)
            {
                _turboList.Items.Clear();
                _turboSummary.Text = "Хост TurboHUD не запущен.";
                return;
            }

            List<TurboHud.TurboPluginEntry> entries = runtime.Plugins;
            string keepPath = _turboSelected == null ? null : _turboSelected.RelativePath;
            string filter = _turboSearch == null ? "" : _turboSearch.Text.Trim();

            _turboRefreshing = true;
            try
            {
                _turboList.BeginUpdate();
                _turboList.Items.Clear();

                int enabled = 0;
                int broken = 0;
                int shown = 0;
                foreach (TurboHud.TurboPluginEntry entry in entries)
                {
                    if (entry.Enabled && entry.Compiled) enabled++;
                    if (!entry.Compiled) broken++;
                    string description = TurboDescription(entry);
                    if (!Matches(filter, entry.ClassName, description, entry.RelativePath)) continue;

                    ListViewItem item = new ListViewItem(entry.ClassName);
                    item.SubItems.Add(!entry.Compiled ? "Ошибка" : (entry.Enabled ? "Включён" : "Отключён"));
                    item.SubItems.Add(description);
                    item.Tag = entry;
                    item.Checked = entry.Compiled && entry.Enabled;
                    item.ToolTipText = description;
                    item.ForeColor = !entry.Compiled ? Ui.DangerColor
                                    : (entry.Enabled ? Ui.Text : Ui.MutedColor);
                    _turboList.Items.Add(item);
                    shown++;
                }
                _turboList.EndUpdate();

                _turboSummary.Text = string.Format(
                    "Плагинов TurboHUD: {0}   Включено: {1}   С ошибками: {2}   Показано: {3}   Папка: {4}",
                    entries.Count, enabled, broken, shown, Path.GetFileName(runtime.Folder));
            }
            finally
            {
                _turboRefreshing = false;
            }

            ListViewItem select = null;
            if (keepPath != null)
            {
                foreach (ListViewItem item in _turboList.Items)
                {
                    TurboHud.TurboPluginEntry entry = item.Tag as TurboHud.TurboPluginEntry;
                    if (entry != null && string.Equals(entry.RelativePath, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        select = item;
                        break;
                    }
                }
            }
            if (select == null && _turboList.Items.Count > 0) select = _turboList.Items[0];
            if (select != null && _turboList.SelectedItems.Count == 0) select.Selected = true;
        }

        private void ShowTurbo()
        {
            if (_turboList.SelectedItems.Count == 0)
            {
                _turboSelected = null;
                return;
            }
            _turboSelected = _turboList.SelectedItems[0].Tag as TurboHud.TurboPluginEntry;
        }

        private void TurboChecked(ItemCheckedEventArgs e)
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (_disposed || _main.IsShuttingDown || _turboRefreshing || runtime == null) return;

            TurboHud.TurboPluginEntry entry = e.Item == null ? null : e.Item.Tag as TurboHud.TurboPluginEntry;
            if (entry == null) return;

            if (!entry.Compiled)
            {
                _turboRefreshing = true;
                try { e.Item.Checked = false; }
                finally { _turboRefreshing = false; }
                MessageBox.Show(this, "Плагин не собрался: " + (entry.Error ?? "неизвестно"),
                    "Плагин TurboHUD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (entry.Enabled == e.Item.Checked) return;

            try
            {
                runtime.SetEnabled(entry, e.Item.Checked);
                _main.Log((e.Item.Checked ? "Включён" : "Отключён") + " плагин TurboHUD: " + entry.ClassName);
                _main.SetStatus(e.Item.Checked ? "Плагин TurboHUD включён." : "Плагин TurboHUD отключён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагин TurboHUD", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshTurbo();
        }

        private void ReloadTurbo()
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null) return;
            try
            {
                runtime.Load();
                RefreshTurbo();
                _main.SetStatus("Плагины TurboHUD перечитаны.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагины TurboHUD", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenTurboFolder()
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null) return;
            try { PluginManager.OpenFolder(runtime.Folder); }
            catch { }
        }

        private void ImportTurbo()
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null) return;

            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Импорт плагина TurboHUD";
                dlg.Filter = "C# плагины (*.cs)|*.cs|Все файлы|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string folder = runtime.Folder;
                    Directory.CreateDirectory(folder);
                    int count = 0;
                    foreach (string file in dlg.FileNames)
                    {
                        string dest = Path.Combine(folder, Path.GetFileName(file));
                        File.Copy(file, dest, true);
                        _main.Log("Импортирован плагин TurboHUD: " + Path.GetFileName(file));
                        count++;
                    }
                    ReloadTurbo();
                    _main.SetStatus("Импортировано плагинов: " + count);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Returns a plausible source folder with TurboHUD plugins, derived
        /// from the configured HUD location instead of a fixed path.
        /// </summary>
        private static string FindEnginePluginsFolder(string targetFolder)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(AppPaths.HudRoot))
            {
                candidates.Add(Path.Combine(AppPaths.HudRoot, "TurboHUD", "plugins"));
                candidates.Add(Path.Combine(AppPaths.HudRoot, "plugins"));
            }
            if (!string.IsNullOrEmpty(AppPaths.PluginsDir)) candidates.Add(AppPaths.PluginsDir);

            string target = null;
            try { if (!string.IsNullOrEmpty(targetFolder)) target = Path.GetFullPath(targetFolder); }
            catch { }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!Directory.Exists(candidate)) continue;
                    string full = Path.GetFullPath(candidate);
                    if (target != null && string.Equals(full, target, StringComparison.OrdinalIgnoreCase)) continue;
                    return full;
                }
                catch { }
            }
            return "";
        }

        private void ImportFromTurboHUD()
        {
            TurboHud.TurboHostRuntime runtime = _main.TurboRuntime;
            if (runtime == null) return;

            // Never use a path from the developer's machine: the engine can be
            // installed anywhere. Look at the configured HUD root first.
            string candidateDir = FindEnginePluginsFolder(runtime.Folder);
            string sourceDir = null;

            if (!string.IsNullOrEmpty(candidateDir))
            {
                DialogResult res = MessageBox.Show(this,
                    "Найдена папка с плагинами TurboHUD:\n" + candidateDir + "\n\nСкопировать плагины оттуда?",
                    "Импорт из TurboHUD",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (res == DialogResult.Cancel) return;
                if (res == DialogResult.Yes)
                {
                    sourceDir = candidateDir;
                }
            }

            if (string.IsNullOrEmpty(sourceDir))
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Выберите папку plugins установленного TurboHUD";
                    fbd.ShowNewFolderButton = false;
                    if (fbd.ShowDialog(this) != DialogResult.OK) return;
                    sourceDir = fbd.SelectedPath;
                }
            }

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return;

            try
            {
                string targetFolder = runtime.Folder;
                Directory.CreateDirectory(targetFolder);

                string[] csFiles = Directory.GetFiles(sourceDir, "*.cs", SearchOption.AllDirectories);
                if (csFiles.Length == 0)
                {
                    MessageBox.Show(this, "В выбранной папке нет .cs файлов.", "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int copied = 0;
                foreach (string src in csFiles)
                {
                    string relative = src.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string dest = Path.Combine(targetFolder, relative);
                    string destSubDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
                        Directory.CreateDirectory(destSubDir);

                    File.Copy(src, dest, true);
                    copied++;
                }

                ReloadTurbo();
                _main.SetStatus("Импортировано из TurboHUD: " + copied + " файлов.");
                MessageBox.Show(this,
                    "Успешно скопировано файлов: " + copied + "\nв папку: " + targetFolder,
                    "Импорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка импорта: " + ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportLocal()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Импорт плагина SuperD3Hud";
                dlg.Filter = "C# плагины|*.cs|Все файлы|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string folder = _main.PluginRuntime.Folder;
                    Directory.CreateDirectory(folder);
                    foreach (string file in dlg.FileNames)
                    {
                        string dest = Path.Combine(folder, Path.GetFileName(file));
                        int n = 1;
                        while (File.Exists(dest))
                        {
                            dest = Path.Combine(folder,
                                Path.GetFileNameWithoutExtension(file) + "_" + n + ".cs");
                            n++;
                        }
                        File.Copy(file, dest);
                        _main.Log("Импортирован плагин: " + dest);
                    }
                    ReloadLocal();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Rebuilds the description file for the engine plugins.</summary>
        private void GenerateDescriptions()
        {
            try
            {
                int added = PluginCatalog.FillMissingDescriptions(PluginManager.Scan());
                PluginCatalog.Invalidate();
                RefreshList();
                _main.SetStatus("Описания плагинов обновлены: добавлено " + added + ".");
                MessageBox.Show(this,
                    "Описания плагинов обновлены.\n\nДобавлено: " + added +
                    "\nФайл: config\\plugin_descriptions.json\n\n" +
                    "Его можно открыть и отредактировать: свои формулировки имеют приоритет.",
                    "Описания", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Описания", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------
        // Engine plugins
        // ------------------------------------------------------------------

        private void BuildEngineTab(TabPage host)
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 40;
            bar.BackColor = Color.Transparent;

            _search = Ui.Input();
            _search.Width = 210;
            _search.Margin = new Padding(0, 3, 8, 0);
            _search.TextChanged += delegate { RefreshList(); };
            Ui.Tip(_search, "Поиск по названию, категории и описанию.");

            Panel searchHost = SearchBox(_search);
            searchHost.Location = new Point(0, 4);

            FlowLayoutPanel buttons = Ui.Toolbar();
            buttons.Location = new Point(286, 4);
            buttons.Controls.Add(Ui.Secondary("Обновить", delegate { RefreshList(); }));
            buttons.Controls.Add(Ui.Secondary("Импорт .cs", delegate { ImportPlugins(); }));
            buttons.Controls.Add(Ui.Secondary("Открыть файл", delegate { OpenSelected(); }));
            buttons.Controls.Add(Ui.Secondary("Папка плагинов", delegate { PluginManager.OpenFolder(AppPaths.PluginsDir); }));
            buttons.Controls.Add(Ui.Secondary("Описания", delegate { GenerateDescriptions(); }));

            bar.Controls.Add(buttons);
            bar.Controls.Add(searchHost);
            host.Controls.Add(bar);

            _summary = Ui.Muted("Плагины не найдены.");
            _summary.Dock = DockStyle.Bottom;
            _summary.Height = 26;
            _summary.Padding = new Padding(2, 6, 0, 0);
            if (!string.IsNullOrEmpty(AppPaths.PluginsDir)) Ui.Tip(_summary, "Папка: " + AppPaths.PluginsDir);
            host.Controls.Add(_summary);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Ui.Bg;
            split.SplitterWidth = 10;
            split.SizeChanged += delegate
            {
                try
                {
                    int target = (int)(split.Width * 0.66);
                    if (target > 300 && target < split.Width - 300) split.SplitterDistance = target;
                }
                catch { }
            };
            host.Controls.Add(split);
            split.BringToFront();

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Ui.Panel;
            left.Padding = new Padding(10);
            split.Panel1.Controls.Add(left);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Плагин", 190);
            _list.Columns.Add("Состояние", 86);
            _list.Columns.Add("Категория", 120);
            _list.Columns.Add("Что делает", 260);
            _list.CheckBoxes = true;
            _list.ShowItemToolTips = true;
            Ui.StyleListView(_list);
            _list.SelectedIndexChanged += delegate { ShowSelected(); };
            _list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { EngineChecked(e); };
            _list.Resize += delegate { FitColumns(); };
            left.Controls.Add(_list);

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Ui.Panel;
            right.Padding = new Padding(14);
            split.Panel2.Controls.Add(right);

            _detailName = Ui.Section("Выберите плагин");
            _detailName.Dock = DockStyle.Top;
            _detailName.Height = 56;
            _detailName.AutoSize = false;
            _detailName.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

            _detailPath = Ui.Muted("");
            _detailPath.Dock = DockStyle.Top;
            _detailPath.Height = 38;
            _detailPath.AutoSize = false;
            _detailPath.AutoEllipsis = true;

            _detailDesc = Ui.Body("");
            _detailDesc.Dock = DockStyle.Top;
            _detailDesc.Height = 110;
            _detailDesc.AutoSize = false;
            _detailDesc.MaximumSize = new Size(400, 110);

            right.Controls.Add(_detailDesc);
            right.Controls.Add(_detailPath);
            right.Controls.Add(_detailName);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9f);
            tabs.Padding = new Point(10, 4);
            right.Controls.Add(tabs);
            tabs.BringToFront();

            TabPage notesPage = new TabPage("Заметка");
            notesPage.BackColor = Ui.Panel;
            notesPage.Padding = new Padding(8);
            _notes = MakeEditor();
            _notes.Dock = DockStyle.Fill;
            FlowLayoutPanel nb = Ui.Toolbar();
            nb.Dock = DockStyle.Bottom;
            nb.Controls.Add(Ui.Primary("Сохранить заметку", delegate { SaveNotes(); }));
            notesPage.Controls.Add(_notes);
            notesPage.Controls.Add(nb);
            tabs.TabPages.Add(notesPage);

            TabPage settingsPage = new TabPage("JSON");
            settingsPage.BackColor = Ui.Panel;
            settingsPage.Padding = new Padding(8);
            _settings = MakeEditor();
            _settings.Dock = DockStyle.Fill;
            FlowLayoutPanel sb = Ui.Toolbar();
            sb.Dock = DockStyle.Bottom;
            sb.Controls.Add(Ui.Primary("Сохранить JSON", delegate { SaveSettings(); }));
            sb.Controls.Add(Ui.Secondary("Сбросить", delegate { ReloadSettings(); }));
            settingsPage.Controls.Add(_settings);
            settingsPage.Controls.Add(sb);
            tabs.TabPages.Add(settingsPage);

            TabPage infoPage = new TabPage("Справка");
            infoPage.BackColor = Ui.Panel;
            infoPage.Padding = new Padding(10);
            Label info = Ui.Muted(
                "Это плагины установленного HUD-движка. SuperD3Hud не исполняет их и не меняет код: " +
                "он только переносит .cs-файл между папками plugins и plugins.disabled и хранит заметки. " +
                "Галочка в списке включает и выключает плагин.\n\n" +
                "Описание каждого плагина собирается автоматически и хранится в config\\plugin_descriptions.json. " +
                "Откройте этот файл, чтобы исправить или уточнить любой текст — ваши формулировки имеют приоритет.\n\n" +
                "Свои плагины, которые работают без стороннего движка, лежат на вкладке «Плагины SuperD3Hud».");
            info.Dock = DockStyle.Fill;
            info.AutoSize = false;
            info.Padding = new Padding(2, 4, 8, 4);
            infoPage.Controls.Add(info);
            tabs.TabPages.Add(infoPage);
        }

        private static TextBox MakeEditor()
        {
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ScrollBars = ScrollBars.Both;
            t.AcceptsReturn = true;
            t.WordWrap = false;
            t.Font = Ui.FontMono;
            t.BackColor = Ui.Panel2;
            t.ForeColor = Ui.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            return t;
        }

        private void FitColumns()
        {
            if (_list == null || _list.Columns.Count < 4) return;
            int width = _list.ClientSize.Width;
            if (width < 300) return;
            int state = 86, category = 120;
            int remaining = width - state - category - 26;
            int name = (int)(remaining * 0.45);
            int desc = remaining - name;
            if (name < 120) name = 120;
            if (desc < 120) desc = 120;
            _list.Columns[0].Width = name;
            _list.Columns[1].Width = state;
            _list.Columns[2].Width = category;
            _list.Columns[3].Width = desc;
        }

        public void RefreshList()
        {
            _all = PluginManager.Scan();
            string filter = _search == null ? "" : _search.Text.Trim();
            string keepPath = _selected == null ? null : _selected.RelativePath;

            _engineRefreshing = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                int enabled = 0;
                int shown = 0;
                foreach (PluginInfo p in _all)
                {
                    if (p.Enabled) enabled++;
                    if (!Matches(filter, p.Name, p.RelativePath, p.Category, p.Description)) continue;

                    ListViewItem item = new ListViewItem(p.Name);
                    item.SubItems.Add(p.StateText);
                    item.SubItems.Add(p.Category);
                    item.SubItems.Add(p.Description ?? "");
                    item.Tag = p;
                    item.Checked = p.Enabled;
                    item.ToolTipText = p.Description ?? "";
                    item.ForeColor = p.Enabled ? Ui.Text : Ui.MutedColor;
                    _list.Items.Add(item);
                    shown++;
                }
                _list.EndUpdate();

                _summary.Text = string.Format("Всего: {0}   Включено: {1}   Показано: {2}   Папка: {3}",
                    _all.Count, enabled, shown, Path.GetFileName(AppPaths.PluginsDir));
            }
            finally
            {
                _engineRefreshing = false;
            }

            ListViewItem select = null;
            if (keepPath != null)
            {
                foreach (ListViewItem item in _list.Items)
                {
                    PluginInfo p = item.Tag as PluginInfo;
                    if (p != null && string.Equals(p.RelativePath, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        select = item;
                        break;
                    }
                }
            }
            if (select == null && _list.Items.Count > 0) select = _list.Items[0];
            if (select != null && _list.SelectedItems.Count == 0) select.Selected = true;
        }

        private void ShowSelected()
        {
            if (_list.SelectedItems.Count == 0)
            {
                _selected = null;
                _detailName.Text = "Выберите плагин";
                _detailPath.Text = "";
                _detailDesc.Text = "";
                _notes.Text = "";
                _settings.Text = "";
                return;
            }
            PluginInfo p = _list.SelectedItems[0].Tag as PluginInfo;
            _selected = p;
            if (p == null) return;
            _loadingDetails = true;
            _detailName.Text = p.Name + (p.Enabled ? "  ●" : "  ○");
            _detailName.ForeColor = p.Enabled ? Ui.Text : Ui.MutedColor;
            _detailPath.Text = p.FullPath;
            _detailDesc.Text = p.Description;
            string note;
            _notes.Text = _notesMap.TryGetValue(p.RelativePath, out note) ? note : "";
            _settings.Text = PluginManager.LoadSettingsText(p);
            _loadingDetails = false;
        }

        private void EngineChecked(ItemCheckedEventArgs e)
        {
            if (_disposed || _main.IsShuttingDown || _engineRefreshing) return;
            PluginInfo plugin = e.Item == null ? null : e.Item.Tag as PluginInfo;
            if (plugin == null) return;
            if (plugin.Enabled == e.Item.Checked) return;

            try
            {
                PluginManager.SetEnabled(plugin, e.Item.Checked);
                _main.Log((e.Item.Checked ? "Включён" : "Отключён") + " плагин: " + plugin.RelativePath);
                _main.SetStatus(e.Item.Checked ? "Плагин HUD включён." : "Плагин HUD отключён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Плагин", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private void ImportPlugins()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Импорт плагинов HUD";
                dlg.Filter = "C# плагины|*.cs|Все файлы|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    foreach (string file in dlg.FileNames)
                    {
                        string dest = PluginManager.Import(file);
                        _main.Log("Импортирован плагин: " + dest);
                    }
                    RefreshList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenSelected()
        {
            if (_selected == null) return;
            try { PluginManager.OpenInEditor(_selected); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Открыть", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveNotes()
        {
            if (_selected == null || _loadingDetails) return;
            _notesMap[_selected.RelativePath] = _notes.Text;
            PluginManager.SaveNotes(_notesMap);
            _main.SetStatus("Заметка сохранена.");
        }

        private void SaveSettings()
        {
            if (_selected == null) return;
            try
            {
                PluginManager.SaveSettingsText(_selected, _settings.Text);
                _main.SetStatus("Локальные настройки сохранены.");
                _main.Log("Настройки плагина сохранены: " + _selected.RelativePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "JSON", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ReloadSettings()
        {
            if (_selected == null) return;
            _settings.Text = PluginManager.LoadSettingsText(_selected);
        }
    }
}
