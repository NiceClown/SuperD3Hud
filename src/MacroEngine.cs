using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;

namespace SuperD3Hud
{
    public enum MacroAnchor
    {
        Auto = 0,
        Left = 1,
        Right = 2,
        Center = 3
    }

    /// <summary>
    /// Executes small, human-readable macro scripts. Supported commands:
    ///   FOCUS, MOVE x y, MOVEL x y, MOVER x y, MOVEC x y, CLICK [right], CLICKR,
    ///   KEY vk, SLEEP ms, GRID x y dx dy cols rows [confirmKey],
    ///   GRIDDROP x y dx dy cols rows dropX dropY,
    ///   SMARTSALVAGE, OPENGR [empowered], OPENRIFT, ACCEPTGR, GEMUPS [count],
    ///   SMARTGAMBLE [maxClicks], SMARTDROP, CUBEUPGRADE [maxRuns].
    ///
    /// Coordinates in the scripts are written for a 2560x1440 client area and
    /// are mapped onto the real game window at run time using Diablo III UI
    /// vertical scaling and left/right/center edge anchoring.
    /// </summary>
    public class MacroEngine
    {
        /// <summary>Reference client area the default scripts are written for.</summary>
        public const int ReferenceWidth = 2560;
        public const int ReferenceHeight = 1440;

        public const int BackpackColumns = 10;
        public const int BackpackRows = 6;

        /// <summary>Default 2560x1440 Diablo III UI coordinates.</summary>
        private const int DefaultBackpackX = 1903;
        private const int DefaultBackpackY = 780;
        private const int DefaultBackpackStep = 67;
        private const int DefaultDropX = 933;
        private const int DefaultDropY = 525;

        private const int BlacksmithSalvageTabX = 683;
        private const int BlacksmithSalvageTabY = 643;
        private const int BlacksmithAnvilX = 220;
        private const int BlacksmithAnvilY = 393;
        private const int BlacksmithBulkWhiteX = 427;
        private const int BlacksmithBulkBlueX = 513;
        private const int BlacksmithBulkYellowX = 599;
        private const int BlacksmithBulkY = 393;

        private const int ObeliskNephalemRiftX = 352;
        private const int ObeliskNephalemRiftY = 482;
        private const int ObeliskGreaterRiftX = 352;
        private const int ObeliskGreaterRiftY = 627;
        private const int ObeliskEmpoweredX = 225;
        private const int ObeliskEmpoweredY = 1036;
        private const int ObeliskAcceptX = 351;
        private const int ObeliskAcceptY = 1121;

        private const int AcceptGrX = 1109;
        private const int AcceptGrY = 869;

        private const int UrshiUpgradeX = 365;
        private const int UrshiUpgradeY = 729;

        private const int CubeFillX = 955;
        private const int CubeFillY = 1121;
        private const int CubeTransmuteX = 315;
        private const int CubeTransmuteY = 1105;

        private const int VkReturn = 13;
        private const int VkEscape = 0x1B;
        private const double MouseAbortDistanceSq = 180.0 * 180.0;

        /// <summary>Smallest delay a step may use, in milliseconds.</summary>
        public const int MinStepDelay = 1;
        /// <summary>Largest delay the default humanised profile uses.</summary>
        public const int MaxStepDelay = 5;

        private MacroSettings _settings;
        private Thread _thread;
        private volatile bool _cancel;
        private volatile bool _paused;
        private volatile bool _macroSendingEscape;
        private bool _inMouseWatchLoop;
        private bool _hasPlacedCursor;
        private Point _lastPlacedCursor;
        private string _abortReason;

        // Mapping from the reference client area (2560x1440) to the real one.
        private Rectangle _clientRect;
        private bool _mapped;

        public Func<Game.InventorySnapshot> InventoryProvider { get; set; }

        public event Action<string> Log;

        public MacroEngine(MacroSettings settings)
        {
            _settings = settings;
        }

        public bool IsRunning
        {
            get { return _thread != null && _thread.IsAlive; }
        }

        public bool IsPaused { get { return _paused; } }

        public MacroSettings Settings
        {
            get { return _settings; }
            set { _settings = value; }
        }

        public void UpdateSettings(MacroSettings settings)
        {
            _settings = settings;
        }

        public void TogglePause()
        {
            _paused = !_paused;
            Emit(_paused ? "Макросы на паузе." : "Макросы возобновлены.");
        }

        public void Stop()
        {
            _cancel = true;
            Thread t = _thread;
            if (t != null && t.IsAlive) t.Join(1500);
        }

        public bool Run(string action)
        {
            if (string.IsNullOrEmpty(action)) return false;
            if (action == "Pause")
            {
                TogglePause();
                return true;
            }
            if (!_settings.Enabled)
            {
                Emit("Макросы отключены в настройках.");
                return false;
            }
            if (_paused)
            {
                Emit("Макросы на паузе. Нажмите горячую клавишу паузы.");
                return false;
            }
            if (IsRunning)
            {
                Emit("Предыдущий макрос ещё выполняется.");
                return false;
            }

            string script;
            if (!_settings.Scripts.TryGetValue(action, out script) || string.IsNullOrEmpty(script))
            {
                Emit("Для действия " + action + " не задан сценарий.");
                return false;
            }

            if (_settings.OnlyWhenGameForeground)
            {
                if (!InputSender.IsGameForeground())
                {
                    Emit("Diablo III не в фокусе — макрос пропущен.");
                    return false;
                }
            }

            _cancel = false;
            string actionCopy = action;
            string scriptCopy = script;
            _thread = new Thread(delegate() { Execute(actionCopy, scriptCopy); });
            _thread.IsBackground = true;
            _thread.Name = "Macro-" + action;
            _thread.Start();
            return true;
        }

        public bool RunScript(string action, string script)
        {
            if (IsRunning) return false;
            _cancel = false;
            string actionCopy = string.IsNullOrEmpty(action) ? "Custom" : action;
            _thread = new Thread(delegate() { Execute(actionCopy, script); });
            _thread.IsBackground = true;
            _thread.Start();
            return true;
        }

        /// <summary>Checks a script without sending any input. Returns human-readable errors.</summary>
        public static List<string> Validate(string script)
        {
            List<string> errors = new List<string>();
            if (string.IsNullOrEmpty(script)) return errors;
            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                line = line.Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = p[0].ToUpperInvariant();
                try
                {
                    switch (cmd)
                    {
                        case "FOCUS":
                            break;
                        case "MOVE":
                        case "MOVEL":
                        case "MOVER":
                        case "MOVEC":
                            Require(p, 3); Int(p[1]); Int(p[2]);
                            break;
                        case "CLICK":
                            break;
                        case "CLICKR":
                            break;
                        case "KEY":
                            Require(p, 2); InputSender.ParseKey(p[1]);
                            break;
                        case "SLEEP":
                            Require(p, 2); Int(p[1]);
                            break;
                        case "GRID":
                            Require(p, 7);
                            Int(p[1]); Int(p[2]); Int(p[3]); Int(p[4]); Int(p[5]); Int(p[6]);
                            if (p.Length > 7) InputSender.ParseKey(p[7]);
                            break;
                        case "GRIDDROP":
                            Require(p, 9);
                            for (int i = 1; i <= 8; i++) Int(p[i]);
                            break;
                        case "SMARTSALVAGE":
                            if (p.Length > 1)
                            {
                                Require(p, 5);
                                Int(p[1]); Int(p[2]); Int(p[3]); Int(p[4]);
                            }
                            break;
                        case "OPENGR":
                            if (p.Length > 1) ParseBoolArg(p[1], "empowered", "normal");
                            break;
                        case "OPENRIFT":
                            break;
                        case "ACCEPTGR":
                            if (p.Length > 1)
                            {
                                Require(p, 3);
                                Int(p[1]); Int(p[2]);
                            }
                            break;
                        case "GEMUPS":
                            if (p.Length > 1)
                            {
                                int count = Int(p[1]);
                                if (count < 1 || count > 5)
                                    throw new ArgumentException("Число улучшений должно быть 1..5.");
                            }
                            break;
                        case "SMARTGAMBLE":
                            if (p.Length > 1)
                            {
                                int clicks = Int(p[1]);
                                if (clicks < 1)
                                    throw new ArgumentException("Число кликов должно быть >= 1.");
                            }
                            break;
                        case "SMARTDROP":
                            if (p.Length > 1)
                            {
                                Require(p, 7);
                                for (int i = 1; i <= 6; i++) Int(p[i]);
                            }
                            break;
                        case "CUBEUPGRADE":
                            if (p.Length > 1)
                            {
                                int maxRuns = Int(p[1]);
                                if (maxRuns < 1)
                                    throw new ArgumentException("Число циклов должно быть >= 1.");
                                if (p.Length > 2)
                                {
                                    Require(p, 6);
                                    Int(p[2]); Int(p[3]); Int(p[4]); Int(p[5]);
                                }
                            }
                            break;
                        default:
                            throw new ArgumentException("неизвестная команда " + cmd);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("строка " + (n + 1) + ": " + ex.Message);
                }
            }
            return errors;
        }

        private void Execute(string action, string script)
        {
            Emit("Запуск: " + MacroActions.DisplayName(action));
            InputSender.BeginPreciseTiming();
            _abortReason = null;
            _hasPlacedCursor = false;
            _inMouseWatchLoop = false;
            _macroSendingEscape = false;
            InputSender.ConsumeKeyPress(VkEscape);
            try
            {
                UpdateMapping();
                string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                int lineNo = 0;
                foreach (string raw in lines)
                {
                    lineNo++;
                    if (CheckAbort(_inMouseWatchLoop)) break;
                    string line = raw;
                    int comment = line.IndexOf('#');
                    if (comment >= 0) line = line.Substring(0, comment);
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    try
                    {
                        ExecuteLine(line);
                    }
                    catch (Exception ex)
                    {
                        Emit("Строка " + lineNo + ": " + ex.Message);
                        break;
                    }
                }
                if (_cancel)
                {
                    Emit(!string.IsNullOrEmpty(_abortReason) ? _abortReason : "Макрос остановлен.");
                }
                else
                {
                    Emit("Готово: " + MacroActions.DisplayName(action));
                }
            }
            catch (Exception ex)
            {
                Emit("Ошибка макроса: " + ex.Message);
            }
            finally
            {
                _inMouseWatchLoop = false;
                _macroSendingEscape = false;
                InputSender.EndPreciseTiming();
            }
        }

        /// <summary>
        /// Recomputes the mapping between the reference client area and the
        /// real one. When the game window cannot be measured, the reference
        /// size is used unchanged.
        /// </summary>
        private void UpdateMapping()
        {
            _mapped = false;
            _clientRect = Rectangle.Empty;

            Rectangle client;
            if (!InputSender.TryGetGameClientRect(out client)) return;
            if (client.Width <= 0 || client.Height <= 0) return;

            _clientRect = client;
            _mapped = true;
        }

        /// <summary>
        /// Maps a reference 2560x1440 coordinate onto a target client rectangle
        /// using Diablo III's vertical UI scaling and edge anchoring. Standard
        /// 16:9 resolutions stay pixel-identical to uniform scaling, while
        /// ultrawide (21:9) and 16:10 map left/right/center UI cleanly.
        /// </summary>
        public static Point MapPointForRect(Rectangle client, int x, int y, MacroAnchor anchor)
        {
            if (client.Width <= 0 || client.Height <= 0) return new Point(x, y);

            double scaleX = (double)client.Width / ReferenceWidth;
            double scaleY = (double)client.Height / ReferenceHeight;

            if (anchor == MacroAnchor.Auto && Math.Abs(scaleX - scaleY) < 0.001)
            {
                return new Point(
                    client.Left + (int)Math.Round(x * scaleX),
                    client.Top + (int)Math.Round(y * scaleY));
            }

            MacroAnchor effective = anchor;
            if (effective == MacroAnchor.Auto)
            {
                if (x >= 1750) effective = MacroAnchor.Right;
                else if (x <= 860) effective = MacroAnchor.Left;
                else effective = MacroAnchor.Center;
            }

            double mappedX;
            switch (effective)
            {
                case MacroAnchor.Left:
                    mappedX = x * scaleY;
                    break;
                case MacroAnchor.Right:
                    mappedX = client.Width - (ReferenceWidth - x) * scaleY;
                    break;
                case MacroAnchor.Center:
                default:
                    mappedX = client.Width / 2.0 + (x - ReferenceWidth / 2.0) * scaleY;
                    break;
            }

            return new Point(
                client.Left + (int)Math.Round(mappedX),
                client.Top + (int)Math.Round(y * scaleY));
        }

        /// <summary>Maps a reference coordinate onto the real screen.</summary>
        private Point MapPoint(int x, int y)
        {
            return MapPoint(x, y, MacroAnchor.Auto);
        }

        private Point MapPoint(int x, int y, MacroAnchor anchor)
        {
            if (!_mapped) return new Point(x, y);
            return MapPointForRect(_clientRect, x, y, anchor);
        }

        private void MoveTo(int x, int y)
        {
            MoveTo(x, y, MacroAnchor.Auto);
        }

        private void MoveTo(int x, int y, MacroAnchor anchor)
        {
            Point p = MapPoint(x, y, anchor);
            InputSender.MoveTo(p.X, p.Y);
            Point actual;
            _lastPlacedCursor = InputSender.TryGetCursorPosition(out actual) ? actual : p;
            _hasPlacedCursor = true;
        }

        private void SendKey(int vk)
        {
            if (vk == VkEscape)
            {
                _macroSendingEscape = true;
                try
                {
                    InputSender.KeyPress(vk);
                    InputSender.ConsumeKeyPress(VkEscape);
                }
                finally
                {
                    _macroSendingEscape = false;
                }
            }
            else
            {
                InputSender.KeyPress(vk);
            }
        }

        private bool CheckAbort(bool checkMouse)
        {
            if (_cancel) return true;
            if (!_macroSendingEscape && InputSender.WasKeyPressedOrDown(VkEscape))
            {
                _cancel = true;
                _abortReason = "Макрос остановлен: нажата клавиша Esc.";
                return true;
            }
            MacroSettings settings = _settings;
            if (checkMouse && _hasPlacedCursor && settings != null && settings.AbortOnMouseMove)
            {
                Point cur;
                if (InputSender.TryGetCursorPosition(out cur))
                {
                    double dx = cur.X - _lastPlacedCursor.X;
                    double dy = cur.Y - _lastPlacedCursor.Y;
                    if (dx * dx + dy * dy > MouseAbortDistanceSq)
                    {
                        _cancel = true;
                        _abortReason = "Макрос остановлен: движение мыши.";
                        return true;
                    }
                }
            }
            return false;
        }

        private void ExecuteLine(string line)
        {
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string cmd = parts[0].ToUpperInvariant();

            switch (cmd)
            {
                case "FOCUS":
                    if (!InputSender.FocusGame())
                        Emit("Не удалось переключиться на окно Diablo III.");
                    break;

                case "MOVE":
                    Require(parts, 3);
                    MoveTo(Int(parts[1]), Int(parts[2]), MacroAnchor.Auto);
                    StepPause();
                    break;

                case "MOVEL":
                    Require(parts, 3);
                    MoveTo(Int(parts[1]), Int(parts[2]), MacroAnchor.Left);
                    StepPause();
                    break;

                case "MOVER":
                    Require(parts, 3);
                    MoveTo(Int(parts[1]), Int(parts[2]), MacroAnchor.Right);
                    StepPause();
                    break;

                case "MOVEC":
                    Require(parts, 3);
                    MoveTo(Int(parts[1]), Int(parts[2]), MacroAnchor.Center);
                    StepPause();
                    break;

                case "CLICK":
                    InputSender.Click(parts.Length > 1 && parts[1].Equals("right", StringComparison.OrdinalIgnoreCase));
                    StepPause();
                    break;

                case "CLICKR":
                    InputSender.Click(true);
                    StepPause();
                    break;

                case "KEY":
                    Require(parts, 2);
                    SendKey(InputSender.ParseKey(parts[1]));
                    StepPause();
                    break;

                case "SLEEP":
                    Require(parts, 2);
                    Sleep(HumanizeLong(Int(parts[1])));
                    break;

                case "GRID":
                    {
                        Require(parts, 7);
                        int x = Int(parts[1]);
                        int y = Int(parts[2]);
                        int dx = Int(parts[3]);
                        int dy = Int(parts[4]);
                        int cols = Int(parts[5]);
                        int rows = Int(parts[6]);
                        int confirm = parts.Length > 7 ? InputSender.ParseKey(parts[7]) : 0;
                        _inMouseWatchLoop = true;
                        try
                        {
                            for (int r = 0; r < rows && !CheckAbort(true); r++)
                            {
                                for (int c = 0; c < cols && !CheckAbort(true); c++)
                                {
                                    MoveTo(x + c * dx, y + r * dy, MacroAnchor.Auto);
                                    StepPause();
                                    if (CheckAbort(true)) break;
                                    InputSender.Click(false);
                                    StepPause();
                                    if (confirm != 0 && !CheckAbort(true))
                                    {
                                        SendKey(confirm);
                                        Sleep(HumanizeLong(30));
                                        if (CheckAbort(true)) break;
                                        SendKey(confirm);
                                        Sleep(HumanizeLong(30));
                                    }
                                }
                            }
                        }
                        finally { _inMouseWatchLoop = false; }
                        break;
                    }

                case "GRIDDROP":
                    {
                        Require(parts, 9);
                        int x = Int(parts[1]);
                        int y = Int(parts[2]);
                        int dx = Int(parts[3]);
                        int dy = Int(parts[4]);
                        int cols = Int(parts[5]);
                        int rows = Int(parts[6]);
                        int dropX = Int(parts[7]);
                        int dropY = Int(parts[8]);
                        _inMouseWatchLoop = true;
                        try
                        {
                            for (int r = 0; r < rows && !CheckAbort(true); r++)
                            {
                                for (int c = 0; c < cols && !CheckAbort(true); c++)
                                {
                                    MoveTo(x + c * dx, y + r * dy, MacroAnchor.Auto);
                                    StepPause();
                                    if (CheckAbort(true)) break;
                                    InputSender.Click(false);
                                    StepPause();
                                    if (CheckAbort(true)) break;
                                    MoveTo(dropX, dropY, MacroAnchor.Auto);
                                    Sleep(HumanizeLong(30));
                                    if (CheckAbort(true)) break;
                                    InputSender.Click(false);
                                    StepPause();
                                }
                            }
                        }
                        finally { _inMouseWatchLoop = false; }
                        break;
                    }

                case "SMARTSALVAGE":
                    ExecuteSmartSalvage(parts);
                    break;

                case "OPENGR":
                    ExecuteOpenGR(parts);
                    break;

                case "OPENRIFT":
                    ExecuteOpenRift();
                    break;

                case "ACCEPTGR":
                    ExecuteAcceptGR(parts);
                    break;

                case "GEMUPS":
                    ExecuteGemUps(parts);
                    break;

                case "SMARTGAMBLE":
                    ExecuteSmartGamble(parts);
                    break;

                case "SMARTDROP":
                    ExecuteSmartDrop(parts);
                    break;

                case "CUBEUPGRADE":
                    ExecuteCubeUpgrade(parts);
                    break;

                default:
                    throw new ArgumentException("Неизвестная команда: " + cmd);
            }
        }

        // ------------------------------------------------------------------
        // Smart automation commands & item filtering
        // ------------------------------------------------------------------

        public static bool IsColumnLocked(int col, int lockedLeft, int lockedRight)
        {
            if (col < 0 || col >= BackpackColumns) return true;
            int left = Math.Max(0, Math.Min(BackpackColumns, lockedLeft));
            int right = Math.Max(0, Math.Min(BackpackColumns, lockedRight));
            return col < left || col >= (BackpackColumns - right);
        }

        public static bool ShouldProtectItem(Game.ItemEntry item, MacroSettings settings)
        {
            if (item == null) return true;
            if (settings == null) return false;
            if (IsColumnLocked(item.Column, settings.LockedLeftColumns, settings.LockedRightColumns))
                return true;
            if (settings.ProtectPrimal && item.Rank >= 2) return true;
            if (settings.ProtectAncient && item.Rank >= 1) return true;
            return false;
        }

        public static bool ShouldSalvageItem(Game.ItemEntry item, MacroSettings settings)
        {
            if (item == null || settings == null) return false;
            if (!settings.SalvageLegendaries) return false;
            if (item.Column < 0 || item.Column >= BackpackColumns) return false;
            if (item.Row < 0 || item.Row >= BackpackRows) return false;
            if (item.Quality < 9) return false;
            return !ShouldProtectItem(item, settings);
        }

        public static bool ShouldDropItem(Game.ItemEntry item, MacroSettings settings)
        {
            if (item == null || settings == null) return false;
            if (item.Column < 0 || item.Column >= BackpackColumns) return false;
            if (item.Row < 0 || item.Row >= BackpackRows) return false;
            return !ShouldProtectItem(item, settings);
        }

        public static bool ShouldCubeUpgradeItem(Game.ItemEntry item, MacroSettings settings)
        {
            if (item == null || settings == null) return false;
            if (item.Column < 0 || item.Column >= BackpackColumns) return false;
            if (item.Row < 0 || item.Row >= BackpackRows) return false;
            if (IsColumnLocked(item.Column, settings.LockedLeftColumns, settings.LockedRightColumns))
                return false;
            if (item.Rank > 0) return false;
            return item.Quality >= 6 && item.Quality <= 8;
        }

        private Game.InventorySnapshot SafeGetInventory()
        {
            Func<Game.InventorySnapshot> provider = InventoryProvider;
            if (provider == null) return null;
            try { return provider(); }
            catch { return null; }
        }

        private static bool IsSnapshotUsable(Game.InventorySnapshot snapshot)
        {
            if (snapshot == null) return false;
            if (snapshot.TotalSlots <= 0) return false;
            if (string.Equals(snapshot.Status, "Игрок не найден", StringComparison.Ordinal)) return false;
            return true;
        }

        private static List<Point> CollectMatchingCells(Game.InventorySnapshot snapshot,
                                                        MacroSettings settings,
                                                        Func<Game.ItemEntry, MacroSettings, bool> predicate)
        {
            List<Point> cells = new List<Point>();
            if (snapshot == null || snapshot.Items == null) return cells;

            bool[,] seen = new bool[BackpackColumns, BackpackRows];
            for (int i = 0; i < snapshot.Items.Count; i++)
            {
                Game.ItemEntry item = snapshot.Items[i];
                if (item == null || !predicate(item, settings)) continue;
                if (seen[item.Column, item.Row]) continue;
                seen[item.Column, item.Row] = true;
                cells.Add(new Point(item.Column, item.Row));
            }
            cells.Sort(delegate(Point a, Point b)
            {
                int cmp = a.Y.CompareTo(b.Y);
                return cmp != 0 ? cmp : a.X.CompareTo(b.X);
            });
            return cells;
        }

        private static List<Point> CollectUnlockedGridCells(MacroSettings settings, int maxCount)
        {
            List<Point> cells = new List<Point>();
            int left = settings != null ? settings.LockedLeftColumns : 0;
            int right = settings != null ? settings.LockedRightColumns : 0;
            for (int r = 0; r < BackpackRows; r++)
            {
                for (int c = 0; c < BackpackColumns; c++)
                {
                    if (IsColumnLocked(c, left, right)) continue;
                    cells.Add(new Point(c, r));
                    if (maxCount > 0 && cells.Count >= maxCount) return cells;
                }
            }
            return cells;
        }

        private void ExecuteSmartSalvage(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            int gridX = parts.Length >= 5 ? Int(parts[1]) : DefaultBackpackX;
            int gridY = parts.Length >= 5 ? Int(parts[2]) : DefaultBackpackY;
            int stepX = parts.Length >= 5 ? Int(parts[3]) : DefaultBackpackStep;
            int stepY = parts.Length >= 5 ? Int(parts[4]) : DefaultBackpackStep;

            Game.InventorySnapshot snapshot = settings.SmartMode ? SafeGetInventory() : null;
            bool hasSnapshot = IsSnapshotUsable(snapshot);

            _inMouseWatchLoop = true;
            try
            {
                // Switch to Blacksmith Salvage tab.
                MoveTo(BlacksmithSalvageTabX, BlacksmithSalvageTabY, MacroAnchor.Left);
                StepPause();
                if (CheckAbort(true)) return;
                InputSender.Click(false);
                Sleep(HumanizeLong(120));
                if (CheckAbort(true)) return;

                if (settings.SalvageBulkButtons)
                {
                    bool doWhite = !hasSnapshot;
                    bool doBlue = !hasSnapshot;
                    bool doYellow = !hasSnapshot;
                    if (hasSnapshot && snapshot.Items != null)
                    {
                        for (int i = 0; i < snapshot.Items.Count; i++)
                        {
                            Game.ItemEntry item = snapshot.Items[i];
                            if (item == null) continue;
                            if (item.Quality >= 0 && item.Quality <= 2) doWhite = true;
                            else if (item.Quality >= 3 && item.Quality <= 5) doBlue = true;
                            else if (item.Quality >= 6 && item.Quality <= 8) doYellow = true;
                        }
                    }

                    if (doWhite && !CheckAbort(true)) ClickBulkSalvageButton(BlacksmithBulkWhiteX);
                    if (doBlue && !CheckAbort(true)) ClickBulkSalvageButton(BlacksmithBulkBlueX);
                    if (doYellow && !CheckAbort(true)) ClickBulkSalvageButton(BlacksmithBulkYellowX);
                }

                if (!settings.SalvageLegendaries || CheckAbort(true)) return;

                List<Point> targets = hasSnapshot
                    ? CollectMatchingCells(snapshot, settings, ShouldSalvageItem)
                    : CollectUnlockedGridCells(settings, 0);

                if (targets.Count == 0)
                {
                    Emit("Умный разбор: подходящих легендарок нет.");
                    return;
                }

                MoveTo(BlacksmithAnvilX, BlacksmithAnvilY, MacroAnchor.Left);
                StepPause();
                if (CheckAbort(true)) return;
                InputSender.Click(false);
                Sleep(HumanizeLong(120));

                for (int i = 0; i < targets.Count && !CheckAbort(true); i++)
                {
                    Point cell = targets[i];
                    MoveTo(gridX + cell.X * stepX, gridY + cell.Y * stepY, MacroAnchor.Right);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    Sleep(HumanizeLong(hasSnapshot ? 55 : 35));
                    if (CheckAbort(true)) break;
                    SendKey(VkReturn);
                    Sleep(HumanizeLong(hasSnapshot ? 65 : 45));
                }
            }
            finally { _inMouseWatchLoop = false; }
        }

        private void ClickBulkSalvageButton(int buttonX)
        {
            MoveTo(buttonX, BlacksmithBulkY, MacroAnchor.Left);
            StepPause();
            if (CheckAbort(true)) return;
            InputSender.Click(false);
            Sleep(HumanizeLong(90));
            if (CheckAbort(true)) return;
            SendKey(VkReturn);
            Sleep(HumanizeLong(90));
        }

        private void ExecuteOpenGR(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            bool empowered = settings.EmpoweredGR;
            if (parts.Length > 1)
                empowered = ParseBoolArg(parts[1], "empowered", "normal");

            MoveTo(ObeliskGreaterRiftX, ObeliskGreaterRiftY, MacroAnchor.Left);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
            Sleep(HumanizeLong(120));
            if (CheckAbort(false)) return;

            if (empowered)
            {
                MoveTo(ObeliskEmpoweredX, ObeliskEmpoweredY, MacroAnchor.Left);
                StepPause();
                if (CheckAbort(false)) return;
                InputSender.Click(false);
                Sleep(HumanizeLong(100));
                if (CheckAbort(false)) return;
            }

            MoveTo(ObeliskAcceptX, ObeliskAcceptY, MacroAnchor.Left);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
            Sleep(HumanizeLong(150));
            if (CheckAbort(false)) return;

            MoveTo(ReferenceWidth / 2, ReferenceHeight / 2, MacroAnchor.Center);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
        }

        private void ExecuteOpenRift()
        {
            MoveTo(ObeliskNephalemRiftX, ObeliskNephalemRiftY, MacroAnchor.Left);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
            Sleep(HumanizeLong(120));
            if (CheckAbort(false)) return;

            MoveTo(ObeliskAcceptX, ObeliskAcceptY, MacroAnchor.Left);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
        }

        private void ExecuteAcceptGR(string[] parts)
        {
            int x = parts.Length >= 3 ? Int(parts[1]) : AcceptGrX;
            int y = parts.Length >= 3 ? Int(parts[2]) : AcceptGrY;
            MoveTo(x, y, MacroAnchor.Center);
            StepPause();
            if (CheckAbort(false)) return;
            InputSender.Click(false);
        }

        private void ExecuteGemUps(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            int count = parts.Length > 1 ? Int(parts[1]) : settings.GemUpgradeCount;
            if (count < 1) count = 1;
            if (count > 5) count = 5;

            _inMouseWatchLoop = true;
            try
            {
                for (int i = 0; i < count && !CheckAbort(i > 0); i++)
                {
                    MoveTo(UrshiUpgradeX, UrshiUpgradeY, MacroAnchor.Left);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    if (i + 1 < count)
                        Sleep(HumanizeLong(1750));
                }
            }
            finally { _inMouseWatchLoop = false; }
        }

        private void ExecuteSmartGamble(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            int maxClicks = parts.Length > 1 ? Int(parts[1]) : 60;
            if (maxClicks < 1) maxClicks = 1;
            if (maxClicks > 500) maxClicks = 500;

            Point startPos;
            if (InputSender.TryGetCursorPosition(out startPos))
            {
                _lastPlacedCursor = startPos;
                _hasPlacedCursor = true;
            }

            _inMouseWatchLoop = true;
            try
            {
                for (int i = 0; i < maxClicks && !CheckAbort(true); i++)
                {
                    if (settings.SmartMode && InventoryProvider != null)
                    {
                        Game.InventorySnapshot snap = SafeGetInventory();
                        if (IsSnapshotUsable(snap) && snap.FreeSlots <= settings.GambleStopFreeSlots)
                        {
                            Emit(string.Format("Гемблинг остановлен: свободно слотов {0}.", snap.FreeSlots));
                            break;
                        }
                    }
                    InputSender.Click(true);
                    Sleep(HumanizeLong(30));
                }
            }
            finally { _inMouseWatchLoop = false; }
        }

        private void ExecuteSmartDrop(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            int gridX = parts.Length >= 7 ? Int(parts[1]) : DefaultBackpackX;
            int gridY = parts.Length >= 7 ? Int(parts[2]) : DefaultBackpackY;
            int stepX = parts.Length >= 7 ? Int(parts[3]) : DefaultBackpackStep;
            int stepY = parts.Length >= 7 ? Int(parts[4]) : DefaultBackpackStep;
            int dropX = parts.Length >= 7 ? Int(parts[5]) : DefaultDropX;
            int dropY = parts.Length >= 7 ? Int(parts[6]) : DefaultDropY;

            Game.InventorySnapshot snapshot = settings.SmartMode ? SafeGetInventory() : null;
            bool hasSnapshot = IsSnapshotUsable(snapshot);
            List<Point> targets = hasSnapshot
                ? CollectMatchingCells(snapshot, settings, ShouldDropItem)
                : CollectUnlockedGridCells(settings, 0);

            if (targets.Count == 0)
            {
                Emit("Сброс: подходящих предметов нет.");
                return;
            }

            _inMouseWatchLoop = true;
            try
            {
                for (int i = 0; i < targets.Count && !CheckAbort(true); i++)
                {
                    Point cell = targets[i];
                    MoveTo(gridX + cell.X * stepX, gridY + cell.Y * stepY, MacroAnchor.Right);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    StepPause();
                    if (CheckAbort(true)) break;
                    MoveTo(dropX, dropY, MacroAnchor.Center);
                    Sleep(HumanizeLong(30));
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    StepPause();
                }
            }
            finally { _inMouseWatchLoop = false; }
        }

        private void ExecuteCubeUpgrade(string[] parts)
        {
            MacroSettings settings = _settings ?? new MacroSettings();
            int maxRuns = parts.Length >= 2 ? Int(parts[1]) : 20;
            if (maxRuns < 1) maxRuns = 1;
            if (maxRuns > 60) maxRuns = 60;

            int fillX = parts.Length >= 6 ? Int(parts[2]) : CubeFillX;
            int fillY = parts.Length >= 6 ? Int(parts[3]) : CubeFillY;
            int transmuteX = parts.Length >= 6 ? Int(parts[4]) : CubeTransmuteX;
            int transmuteY = parts.Length >= 6 ? Int(parts[5]) : CubeTransmuteY;

            Game.InventorySnapshot initialSnap = settings.SmartMode ? SafeGetInventory() : null;
            bool hasSnapshot = IsSnapshotUsable(initialSnap);
            List<Point> targets = hasSnapshot
                ? CollectMatchingCells(initialSnap, settings, ShouldCubeUpgradeItem)
                : CollectUnlockedGridCells(settings, maxRuns);

            if (targets.Count > maxRuns)
                targets.RemoveRange(maxRuns, targets.Count - maxRuns);

            if (targets.Count == 0)
            {
                Emit("Куб Канаи: нет подходящих редких предметов.");
                return;
            }

            DateTime initialStamp = hasSnapshot ? initialSnap.CapturedUtc : DateTime.MinValue;
            _inMouseWatchLoop = true;
            try
            {
                for (int i = 0; i < targets.Count && !CheckAbort(true); i++)
                {
                    Point cell = targets[i];
                    if (hasSnapshot && InventoryProvider != null)
                    {
                        Game.InventorySnapshot live = SafeGetInventory();
                        if (IsSnapshotUsable(live) && live.CapturedUtc != initialStamp)
                        {
                            bool stillRare = false;
                            if (live.Items != null)
                            {
                                for (int k = 0; k < live.Items.Count; k++)
                                {
                                    Game.ItemEntry item = live.Items[k];
                                    if (item != null && item.Column == cell.X && item.Row == cell.Y)
                                    {
                                        stillRare = ShouldCubeUpgradeItem(item, settings);
                                        break;
                                    }
                                }
                            }
                            if (!stillRare) continue;
                        }
                    }

                    MoveTo(fillX, fillY, MacroAnchor.Left);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    Sleep(HumanizeLong(70));
                    if (CheckAbort(true)) break;

                    MoveTo(DefaultBackpackX + cell.X * DefaultBackpackStep,
                           DefaultBackpackY + cell.Y * DefaultBackpackStep,
                           MacroAnchor.Right);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(true);
                    Sleep(HumanizeLong(70));
                    if (CheckAbort(true)) break;

                    MoveTo(transmuteX, transmuteY, MacroAnchor.Left);
                    StepPause();
                    if (CheckAbort(true)) break;
                    InputSender.Click(false);
                    Sleep(HumanizeLong(260));
                }
            }
            finally { _inMouseWatchLoop = false; }
        }

        private static bool ParseBoolArg(string value, string trueToken, string falseToken)
        {
            if (string.Equals(value, trueToken, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.Ordinal))
                return true;
            if (string.Equals(value, falseToken, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.Ordinal))
                return false;
            throw new ArgumentException("Недопустимый параметр: " + value);
        }

        /// <summary>
        /// A short pause between two input actions. The configured step delay
        /// is used as the average and the real delay is picked at random
        /// around it, so a sequence never repeats the same timing twice.
        /// </summary>
        private void StepPause()
        {
            MacroSettings s = _settings;
            Sleep(Humanize(s != null ? s.StepDelayMs : MacroSettings.DefaultStepDelayMs));
        }

        /// <summary>
        /// Turns a configured delay into a random value in the 1-5 ms band.
        /// </summary>
        public static int Humanize(int baseMs)
        {
            if (baseMs <= 0) return 0;
            int spread = Math.Max(1, baseMs / 2);
            int low = Math.Max(MinStepDelay, baseMs - spread);
            int high = baseMs + spread;
            if (high < low) high = low;
            return InputSender.NextRandom(low, high);
        }

        /// <summary>
        /// Adds a small random spread to an explicit script delay. Short waits
        /// use the same 1-5 ms band; longer ones vary by about eight percent,
        /// which keeps the script readable but not perfectly periodic.
        /// </summary>
        public static int HumanizeLong(int ms)
        {
            if (ms <= 0) return 0;
            if (ms <= MaxStepDelay) return Humanize(ms);
            int spread = Math.Max(3, ms / 12);
            int low = ms - spread;
            int high = ms + spread;
            if (low < 1) low = 1;
            return InputSender.NextRandom(low, high);
        }

        private void Sleep(int ms)
        {
            if (ms <= 0) return;
            int left = ms;
            while (left > 0 && !CheckAbort(_inMouseWatchLoop))
            {
                // Short waits are the whole point here, so the chunk is small
                // enough that a 1-5 ms pause is not rounded up to 20 ms.
                int chunk = Math.Min(5, left);
                Thread.Sleep(chunk);
                left -= chunk;
            }
        }

        private static void Require(string[] parts, int count)
        {
            if (parts.Length < count)
                throw new ArgumentException("Недостаточно аргументов для " + parts[0] + ".");
        }

        private static int Int(string s)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new ArgumentException("Ожидалось число: " + s);
            return v;
        }

        private void Emit(string text)
        {
            Action<string> handler = Log;
            if (handler != null)
            {
                try { handler(text); }
                catch { }
            }
        }
    }
}


