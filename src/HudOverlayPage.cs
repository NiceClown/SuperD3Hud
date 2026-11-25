using System;
using System.Drawing;
using System.Windows.Forms;
using SuperD3Hud.Game;

namespace SuperD3Hud
{
    /// <summary>
    /// Settings of the built-in HUD: which parts are drawn, how they look and
    /// how long the cooldown of each skill slot lasts.
    ///
    /// Cooldowns are not published by the game in a documented way, so the
    /// launcher measures them from input. The user sets the real duration of
    /// each skill once, and the timer then counts down from the key press.
    /// </summary>
    public class HudOverlayPage : UserControl
    {
        private readonly MainForm _main;

        private CheckBox _enabled;
        private CheckBox _showBuffs;
        private CheckBox _showSkills;
        private CheckBox _showHealth;
        private CheckBox _showInventory;
        private CheckBox _showMonsters;
        private CheckBox _showLoot;
        private CheckBox _showDamage;

        private NumericUpDown _opacity;
        private NumericUpDown _scale;
        private ComboBox _corner;
        private NumericUpDown _offsetX;
        private NumericUpDown _offsetY;

        private NumericUpDown[] _cooldowns = new NumericUpDown[6];
        private Label _cooldownState;
        private TextBox _largeMapKey;
        private TextBox _waypointKey;

        private Label _stateActors;
        private Label _stateBuffs;
        private Label _stateSkills;
        private Label _stateInventory;

        private TableLayoutPanel _body;
        private bool _loading;
        private Timer _timer;

        public HudOverlayPage(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Ui.Bg;
            Padding = new Padding(24, 18, 24, 18);
            Build();
            Reload();

            _timer = new Timer();
            _timer.Interval = 500;
            _timer.Tick += delegate { RefreshState(); };
            _timer.Start();
        }

        private void Build()
        {
            Controls.Add(Ui.Header("Оверлей HUD",
                "Собственный HUD: баффы, кулдауны навыков, здоровье, рюкзак и метки на карте."));

            Panel host;
            _body = Ui.PageBody(out host);
            Controls.Add(host);
            host.BringToFront();

            Ui.CardRow(_body, BuildStateCard());
            Ui.CardRow(_body, BuildPartsCard());
            Ui.CardRow(_body, BuildLookCard());
            Ui.CardRow(_body, BuildCooldownCard());
            Ui.CardRow(_body, BuildKeysCard());
            Ui.CardRow(_body, BuildNotesCard());
        }

        private TableLayoutPanel BuildStateCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Что прочитано из игры", out stack);

            _stateActors = Ui.State("• Монстры и добыча: ожидание", Ui.MutedColor);
            _stateBuffs = Ui.State("• Баффы: ожидание", Ui.MutedColor);
            _stateSkills = Ui.State("• Навыки и здоровье: ожидание", Ui.MutedColor);
            _stateInventory = Ui.State("• Рюкзак: ожидание", Ui.MutedColor);
            Ui.Row(stack, _stateActors);
            Ui.Row(stack, _stateBuffs);
            Ui.Row(stack, _stateSkills);
            Ui.Row(stack, _stateInventory);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Обновить", delegate { RefreshState(); }),
                Ui.Secondary("Настройки карты", delegate { _main.ShowPage("map"); })));

            return card;
        }

        private TableLayoutPanel BuildPartsCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Что показывать", out stack);

            _enabled = Ui.DarkCheck("Включить оверлей HUD");
            _enabled.Font = Ui.FontBold;
            _enabled.Margin = new Padding(0, 0, 0, 10);
            _enabled.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _enabled);

            _showBuffs = Ui.DarkCheck("Список активных баффов");
            _showBuffs.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showBuffs);

            _showSkills = Ui.DarkCheck("Кулдауны навыков");
            _showSkills.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showSkills);

            _showHealth = Ui.DarkCheck("Здоровье персонажа");
            _showHealth.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showHealth);

            _showInventory = Ui.DarkCheck("Счётчик рюкзака");
            _showInventory.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showInventory);

            _showMonsters = Ui.DarkCheck("Элита и боссы на карте");
            _showMonsters.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showMonsters);

            _showLoot = Ui.DarkCheck("Легендарки и комплекты на карте");
            _showLoot.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showLoot);

            _showDamage = Ui.DarkCheck("Счётчик урона (в портале)");
            _showDamage.CheckedChanged += delegate { Apply(); };
            Ui.Row(stack, _showDamage);

            return card;
        }

        private TableLayoutPanel BuildLookCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Вид", out stack);

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

            grid.Controls.Add(Ui.Field("Прозрачность"), 0, 0);
            _opacity = MakeNumeric(40, 255, 5, 220, 105);
            grid.Controls.Add(_opacity, 1, 0);

            grid.Controls.Add(Ui.Field("Масштаб"), 2, 0);
            _scale = MakeNumeric(0.6m, 2.0m, 0.05m, 1.00m, 105);
            grid.Controls.Add(_scale, 3, 0);

            grid.Controls.Add(Ui.Field("Угол списка баффов"), 0, 1);
            _corner = Ui.Combo();
            _corner.Width = 120;
            _corner.Margin = new Padding(0, 2, 0, 2);
            _corner.Items.Add("Слева сверху");
            _corner.Items.Add("Справа сверху");
            _corner.Items.Add("Слева снизу");
            _corner.Items.Add("Справа снизу");
            _corner.SelectedIndexChanged += delegate { Apply(); };
            grid.Controls.Add(_corner, 1, 1);

            grid.Controls.Add(Ui.Field("Отступ по X"), 2, 1);
            _offsetX = MakeNumeric(0, 2000, 5, 16, 105);
            grid.Controls.Add(_offsetX, 3, 1);

            grid.Controls.Add(Ui.Field("Отступ по Y"), 0, 2);
            _offsetY = MakeNumeric(0, 2000, 5, 120, 105);
            grid.Controls.Add(_offsetY, 1, 2);

            Ui.Row(stack, grid);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Сбросить вид", delegate { ResetLook(); })));

            return card;
        }

        private TableLayoutPanel BuildCooldownCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Кулдауны навыков", out stack);

            Ui.Row(stack, Ui.Hint(
                "SuperD3Hud сам находит источник кулдаунов в памяти игры: при первом использовании навыка " +
                "он запоминает нужные атрибуты. Длительности ниже нужны только как запасной вариант."));

            _cooldownState = Ui.State("• Кулдауны: поиск источника", Ui.MutedColor);
            Ui.Row(stack, _cooldownState);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 6;
            grid.RowCount = 2;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);

            string[] labels = { "ЛКМ", "ПКМ", "1", "2", "3", "4" };
            for (int i = 0; i < 6; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                Label label = Ui.Field(labels[i] + ", сек");
                label.Anchor = AnchorStyles.Left;
                grid.Controls.Add(label, i, 0);

                _cooldowns[i] = MakeNumeric(0m, 3600m, 0.5m, 0m, 96);
                grid.Controls.Add(_cooldowns[i], i, 1);
            }
            Ui.Row(stack, grid);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Сбросить кулдауны", delegate { ResetCooldowns(); }),
                Ui.Secondary("Найти источник заново", delegate { ForgetCooldownSource(); })));

            return card;
        }

        /// <summary>Card with the keys that open the maps in the game.</summary>
        private TableLayoutPanel BuildKeysCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Клавиши карт", out stack);

            Ui.Row(stack, Ui.Hint(
                "Большая карта в Diablo III открывается клавишей Tab, а карта врат странствий — M. " +
                "Наложение показывается только на большой карте."));

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 4;
            grid.RowCount = 1;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            grid.Controls.Add(Ui.Field("Большая карта"), 0, 0);
            _largeMapKey = Ui.Input();
            _largeMapKey.Width = 120;
            _largeMapKey.Margin = new Padding(0, 2, 0, 2);
            _largeMapKey.TextChanged += delegate { Apply(); };
            grid.Controls.Add(_largeMapKey, 1, 0);

            grid.Controls.Add(Ui.Field("Карта врат (скрывает)"), 2, 0);
            _waypointKey = Ui.Input();
            _waypointKey.Width = 120;
            _waypointKey.Margin = new Padding(0, 2, 0, 2);
            _waypointKey.TextChanged += delegate { Apply(); };
            grid.Controls.Add(_waypointKey, 3, 0);

            Ui.Row(stack, grid);

            Ui.Row(stack, Ui.Buttons(
                Ui.Secondary("Сбросить клавиши", delegate { ResetKeys(); })));

            return card;
        }

        private void ForgetCooldownSource()
        {
            if (_main.GameService == null) return;
            _main.GameService.ForgetCooldowns();

            HudSettings s = _main.HudSettings;
            s.CooldownDescriptor = 0;
            s.CooldownStartDescriptor = 0;
            AppPaths.Settings.Save();

            _main.SetStatus("Источник кулдаунов сброшен: поиск начнётся заново.");
            RefreshState();
        }

        private void ResetKeys()
        {
            _loading = true;
            try
            {
                _largeMapKey.Text = "TAB";
                _waypointKey.Text = "M";
            }
            finally
            {
                _loading = false;
            }
            Apply();
        }

        private TableLayoutPanel BuildNotesCard()
        {
            TableLayoutPanel stack;
            TableLayoutPanel card = Ui.Card("Что известно точно", out stack);

            Ui.Row(stack, Ui.Hint(
                "Монстры, элита, боссы, добыча, баффы и рюкзак читаются из памяти игры " +
                "по проверенным смещениям сборки " + MapReader.SupportedVersion + "."));

            Ui.Row(stack, Ui.Hint(
                "Кулдауны SuperD3Hud находит сам: при первом нажатии навыка он определяет нужные " +
                "атрибуты и дальше читает их напрямую, с учётом снижения перезарядки. " +
                "Пока источник не найден, работает оценка по нажатию клавиши."));

            Ui.Row(stack, Ui.Hint(
                "Здоровье читается из памяти. Максимум здоровья и ресурсы (ярость, мана, дисциплина) " +
                "пока недоступны — их источники не проверены, поэтому они честно не показываются."));

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
            n.DecimalPlaces = step < 1m ? 1 : 0;
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

        public void Reload()
        {
            HudSettings s = _main.HudSettings;
            _loading = true;
            try
            {
                _enabled.Checked = s.Enabled;
                _showBuffs.Checked = s.ShowBuffs;
                _showSkills.Checked = s.ShowSkills;
                _showHealth.Checked = s.ShowHealth;
                _showInventory.Checked = s.ShowInventory;
                _showMonsters.Checked = s.ShowMonsterMarkers;
                _showLoot.Checked = s.ShowLootMarkers;
                _showDamage.Checked = s.ShowDamageMeter;

                _opacity.Value = Clamp(_opacity, s.Opacity);
                _scale.Value = Clamp(_scale, (decimal)s.Scale);
                _corner.SelectedIndex = Math.Max(0, Math.Min(3, s.BuffCorner));
                _offsetX.Value = Clamp(_offsetX, s.BuffOffsetX);
                _offsetY.Value = Clamp(_offsetY, s.BuffOffsetY);

                for (int i = 0; i < 6; i++)
                    _cooldowns[i].Value = Clamp(_cooldowns[i], (decimal)s.Cooldowns[i]);

                _largeMapKey.Text = KeyName(s.LargeMapKey);
                _waypointKey.Text = KeyName(s.WaypointKey);
            }
            finally
            {
                _loading = false;
            }
            RefreshState();
        }

        private static decimal Clamp(NumericUpDown box, decimal value)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        public void Apply()
        {
            if (_loading) return;

            HudSettings s = _main.HudSettings;
            s.Enabled = _enabled.Checked;
            s.ShowBuffs = _showBuffs.Checked;
            s.ShowSkills = _showSkills.Checked;
            s.ShowHealth = _showHealth.Checked;
            s.ShowInventory = _showInventory.Checked;
            s.ShowMonsterMarkers = _showMonsters.Checked;
            s.ShowLootMarkers = _showLoot.Checked;
            s.ShowDamageMeter = _showDamage.Checked;

            s.Opacity = (int)_opacity.Value;
            s.Scale = (double)_scale.Value;
            s.BuffCorner = _corner.SelectedIndex < 0 ? 1 : _corner.SelectedIndex;
            s.BuffOffsetX = (int)_offsetX.Value;
            s.BuffOffsetY = (int)_offsetY.Value;

            for (int i = 0; i < 6; i++)
                s.Cooldowns[i] = (double)_cooldowns[i].Value;

            s.LargeMapKey = ParseKeyOr(_largeMapKey.Text, s.LargeMapKey);
            s.WaypointKey = ParseKeyOr(_waypointKey.Text, s.WaypointKey);

            AppPaths.Settings.Save();
            _main.ApplyHudSettings();
            _main.SetStatus("Настройки оверлея сохранены.");
        }

        /// <summary>Parses a key name, keeping the previous value on bad input.</summary>
        private static int ParseKeyOr(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            try
            {
                int value = InputSender.ParseKey(text.Trim());
                return value > 0 ? value : fallback;
            }
            catch { return fallback; }
        }

        /// <summary>Turns a virtual key back into a short readable name.</summary>
        private static string KeyName(int virtualKey)
        {
            if (virtualKey <= 0) return "";
            if (virtualKey == 0x09) return "TAB";
            if (virtualKey == 0x1B) return "ESC";
            if (virtualKey == 0x20) return "SPACE";
            if (virtualKey >= 0x30 && virtualKey <= 0x5A)
                return ((char)virtualKey).ToString();
            if (virtualKey >= 0x70 && virtualKey <= 0x87)
                return "F" + (virtualKey - 0x6F);
            return virtualKey.ToString();
        }

        private void ResetLook()
        {
            _loading = true;
            try
            {
                _opacity.Value = 220;
                _scale.Value = 1.00m;
                _corner.SelectedIndex = 1;
                _offsetX.Value = 16;
                _offsetY.Value = 120;
            }
            finally
            {
                _loading = false;
            }
            Apply();
        }

        private void ResetCooldowns()
        {
            _loading = true;
            try
            {
                for (int i = 0; i < 6; i++) _cooldowns[i].Value = 0;
            }
            finally
            {
                _loading = false;
            }
            Apply();
        }

        // ------------------------------------------------------------------
        // Live state
        // ------------------------------------------------------------------

        public void RefreshState()
        {
            GameService service = _main.GameService;
            if (service == null) return;

            bool running = InputSender.IsGameRunning();
            string prefix = running ? "" : "игра не запущена — ";

            ActorSnapshot actors = service.Actors;
            if (!running)
                Ui.SetState(_stateActors, "• Монстры и добыча: " + prefix + "нет данных", Ui.MutedColor);
            else
                Ui.SetState(_stateActors,
                    "• Монстры: " + actors.Monsters.Count + ", добыча: " + actors.Loot.Count,
                    actors.Monsters.Count > 0 ? Ui.Ok : Ui.MutedColor);

            BuffSnapshot buffs = service.Buffs;
            int active = 0;
            foreach (BuffEntry entry in buffs.Buffs) if (entry.Active) active++;
            Ui.SetState(_stateBuffs,
                "• Баффы: " + (running ? active.ToString() : prefix + "нет данных"),
                active > 0 ? Ui.Ok : Ui.MutedColor);

            SkillSnapshot skills = service.Skills;
            string health = skills.Health >= 0
                ? "здоровье " + ((int)Math.Round(skills.Health))
                : (running ? "здоровье неизвестно" : prefix + "нет данных");
            Ui.SetState(_stateSkills, "• Навыки и здоровье: " + health,
                skills.Health >= 0 ? Ui.Ok : Ui.MutedColor);

            if (_cooldownState != null)
            {
                if (service.CooldownsFromMemory)
                    Ui.SetState(_cooldownState,
                        "• Кулдауны: читаются из памяти (атрибуты " +
                        service.CooldownDescriptor + "/" + service.CooldownStartDescriptor + ")",
                        Ui.Ok);
                else if (!running)
                    Ui.SetState(_cooldownState, "• Кулдауны: " + prefix + "поиск источника", Ui.MutedColor);
                else
                    Ui.SetState(_cooldownState,
                        "• Кулдауны: " + service.CooldownStatus +
                        " — нажмите навык в игре, чтобы найти источник", Ui.Accent);
            }

            InventorySnapshot inventory = service.Inventory;
            Ui.SetState(_stateInventory,
                "• Рюкзак: " + (running
                    ? inventory.UsedSlots + " из " + inventory.TotalSlots
                    : prefix + "нет данных"),
                inventory.UsedSlots > 0 ? Ui.Ok : Ui.MutedColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
