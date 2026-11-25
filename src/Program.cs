using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SuperD3Hud
{
    public static class Program
    {
        public const string Version = "1.8.0";

        internal static bool IsRenderMode;
        internal static string StartupPage;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        public static int Main(string[] args)
        {
            InstallCrashHandlers();

            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
                return SelfTest();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--render", StringComparison.OrdinalIgnoreCase))
                return Render(args.Length > 1 ? args[1] : null);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--functional", StringComparison.OrdinalIgnoreCase))
                return FunctionalTest();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--map-preview", StringComparison.OrdinalIgnoreCase))
                return MapPreview(args.Length > 1 ? args[1] : null);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--map-check", StringComparison.OrdinalIgnoreCase))
                return MapCheck();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--plugins", StringComparison.OrdinalIgnoreCase))
                return PluginCheck();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--describe", StringComparison.OrdinalIgnoreCase))
                return DescribePlugins();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--check-updates", StringComparison.OrdinalIgnoreCase))
                return CheckUpdates(args.Length > 1 ? args[1] : null);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--game-check", StringComparison.OrdinalIgnoreCase))
                return GameCheck();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--hud-preview", StringComparison.OrdinalIgnoreCase))
                return HudPreview(args.Length > 1 ? args[1] : null);
            if (args != null && args.Length > 0 && string.Equals(args[0], "--cooldowns", StringComparison.OrdinalIgnoreCase))
                return CooldownProbe();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--turbohud", StringComparison.OrdinalIgnoreCase))
                return TurboHudCheck();
            if (args != null && args.Length > 0 && string.Equals(args[0], "--turbohud-live", StringComparison.OrdinalIgnoreCase))
                return TurboHudLiveCheck();

            if (args != null)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "--page", StringComparison.OrdinalIgnoreCase))
                        StartupPage = args[i + 1];
                }
            }

            try { SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                AppPaths.Initialize(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show("Не удалось запустить SuperD3Hud:\n\n" + ex.ToString(),
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>
        /// Catches unhandled errors so they are written to logs\crash.log
        /// instead of disappearing. The UI thread handler never shows a dialog
        /// and never exits; the process continues running.
        /// </summary>
        private static void InstallCrashHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
        }

        /// <summary>
        /// Appends one timestamped crash report to logs\crash.log. The writer
        /// creates the folder on demand and never throws.
        /// </summary>
        private static void LogCrash(Exception ex)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                StringBuilder text = new StringBuilder();
                text.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]");
                text.AppendLine(ex == null ? "Unknown error" : ex.ToString());
                text.AppendLine();
                File.AppendAllText(Path.Combine(dir, "crash.log"), text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>
        /// Renders every page to PNG without showing the window and without
        /// touching the mouse or keyboard. Used to verify layout and Cyrillic text.
        /// </summary>
        private static int Render(string outDir)
        {
            try { AttachConsole(-1); }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }

            IsRenderMode = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.Initialize(true);
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(AppPaths.AppRoot, "render");
            Directory.CreateDirectory(outDir);

            string[] pages = { "dashboard", "map", "overlay", "hud", "files", "plugins", "macros", "profiles", "about" };
            int failures = 0;

            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                form.Size = new System.Drawing.Size(1240, 820);
                // Show the window far off-screen: DrawToBitmap needs a realized,
                // visible window to render child controls, but nothing must appear
                // on the user's desktop and no input must be sent.
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                form.PerformLayout();
                Application.DoEvents();

                foreach (string page in pages)
                {
                    try
                    {
                        form.ShowPage(page);
                        form.PerformLayout();
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(250);
                        Application.DoEvents();

                        // Render every tab of the page, not only the first one.
                        TabControl tabs = FindTabControl(form);
                        int tabCount = tabs == null ? 1 : tabs.TabPages.Count;
                        for (int tab = 0; tab < tabCount; tab++)
                        {
                            if (tabs != null)
                            {
                                tabs.SelectedIndex = tab;
                                form.PerformLayout();
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(120);
                                Application.DoEvents();
                            }

                            string suffix = tabCount > 1 ? "-tab" + (tab + 1) : "";
                            LayoutDump.Write(form, Path.Combine(outDir, page + suffix + "-layout.txt"));
                            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                            {
                                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                                string file = Path.Combine(outDir, page + suffix + ".png");
                                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                                Console.WriteLine("rendered " + file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine("render " + page + " failed: " + ex.Message);
                    }
                }
            }

            Console.WriteLine(failures == 0 ? "RENDER: OK" : "RENDER: FAILURES=" + failures);
            return failures == 0 ? 0 : 2;
        }

        /// <summary>
        /// Renders the overlay with a synthetic dungeon, so the drawing path can
        /// be checked visually without the game running. Writes PNG files into
        /// the given folder.
        /// </summary>
        private static int MapPreview(string outDir)
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(AppPaths.AppRoot, "render");
            try { Directory.CreateDirectory(outDir); }
            catch { }

            try
            {
                List<MapPolygon> polygons = BuildPreviewGeometry();

                MapSettings minimapSettings = new MapSettings();
                minimapSettings.ShowMinimap = true;
                minimapSettings.Opacity = 120;

                List<Game.MonsterMarker> monsters = new List<Game.MonsterMarker>();
                List<Game.LootMarker> loot = new List<Game.LootMarker>();
                BuildPreviewMarkers(monsters, loot);

                using (MapOverlay overlay = new MapOverlay(minimapSettings))
                {
                    overlay.SetMarkers(monsters, loot);
                    overlay.UpdateContent(polygons, new MapPoint(0, 0), true, "");
                    using (System.Drawing.Bitmap bmp = overlay.RenderToBitmap(350, 350))
                    {
                        string file = Path.Combine(outDir, "overlay-minimap.png");
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        Console.WriteLine("rendered " + file);
                    }
                }

                MapSettings panelSettings = new MapSettings();
                panelSettings.ShowMinimap = false;
                panelSettings.ShowPanel = true;
                panelSettings.Opacity = 120;

                using (MapOverlay overlay = new MapOverlay(panelSettings))
                {
                    overlay.SetMarkers(monsters, loot);
                    overlay.UpdateContent(polygons, new MapPoint(0, 0), true, "");
                    using (System.Drawing.Bitmap bmp = overlay.RenderToBitmap(420, 420))
                    {
                        string file = Path.Combine(outDir, "overlay-panel.png");
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        Console.WriteLine("rendered " + file);
                    }
                }

                // Full-screen map mode.
                MapSettings largeSettings = new MapSettings();
                largeSettings.ShowMinimap = false;
                largeSettings.ShowPanel = false;
                largeSettings.ShowLargeMap = true;
                largeSettings.Opacity = 110;

                using (MapOverlay overlay = new MapOverlay(largeSettings, true))
                {
                    overlay.SetMarkers(monsters, loot);
                    overlay.UpdateContent(polygons, new MapPoint(0, 0), true, "");
                    using (System.Drawing.Bitmap bmp = overlay.RenderToBitmap(2560, 1440))
                    {
                        string file = Path.Combine(outDir, "overlay-large.png");
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        Console.WriteLine("rendered " + file);
                    }
                }

                // Exercise the real layered window, not only the offscreen
                // bitmap: this proves the window can be composited on screen.
                Console.WriteLine("Проверка layered-окна...");
                using (MapOverlay live = new MapOverlay(panelSettings))
                {
                    live.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    live.Location = new System.Drawing.Point(-20000, -20000);
                    live.Size = new System.Drawing.Size(320, 320);
                    live.Show();
                    System.Windows.Forms.Application.DoEvents();
                    live.UpdateContent(polygons, new MapPoint(0, 0), true, "");
                    System.Windows.Forms.Application.DoEvents();

                    if (live.LastError.Length > 0)
                    {
                        Console.WriteLine("WARN  layered window: " + live.LastError);
                    }
                    else
                    {
                        Console.WriteLine("PASS  layered window composited");
                    }
                    live.Hide();
                }

                Console.WriteLine("MAP PREVIEW: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MAP PREVIEW: " + ex);
                return 2;
            }
        }

        /// <summary>
        /// Builds a small dungeon-like set of walkable cells: a wide room, two
        /// corridors and a cached outline, which is enough to judge the look.
        /// </summary>
        private static List<MapPolygon> BuildPreviewGeometry()
        {
            List<MapPolygon> polygons = new List<MapPolygon>();

            // Central hall with an interior pillar hole (-10..15, -10..15).
            AddRoomWithHole(polygons, -50, -40, 50, 40, -10, -10, 15, 15);
            // North-east corridor and chamber.
            AddRoom(polygons, 50, -10, 120, 10);
            AddRoom(polygons, 120, -35, 190, 35);
            // South-west corridor and side room.
            AddRoom(polygons, -120, -10, -50, 10);
            AddRoom(polygons, -160, -70, -90, -10);

            return polygons;
        }

        private static void AddRoomWithHole(List<MapPolygon> polygons,
                                            double x0, double y0, double x1, double y1,
                                            double hx0, double hy0, double hx1, double hy1)
        {
            const double step = 5.0;
            for (double y = y0; y < y1 - 0.001; y += step)
            {
                for (double x = x0; x < x1 - 0.001; x += step)
                {
                    double nx = Math.Min(x + step, x1);
                    double ny = Math.Min(y + step, y1);
                    double cx = (x + nx) * 0.5;
                    double cy = (y + ny) * 0.5;
                    if (cx > hx0 && cx < hx1 && cy > hy0 && cy < hy1) continue;

                    MapPolygon cell = new MapPolygon();
                    cell.Points = new MapPoint[]
                    {
                        MapGeometry.Project(x, y),
                        MapGeometry.Project(nx, y),
                        MapGeometry.Project(nx, ny),
                        MapGeometry.Project(x, ny)
                    };
                    polygons.Add(cell);
                }
            }
        }

        /// <summary>
        /// Synthetic markers for the offscreen preview: one elite, one boss and
        /// four loot qualities, so the marker drawing can be judged by eye.
        /// </summary>
        private static void BuildPreviewMarkers(List<Game.MonsterMarker> monsters,
                                                List<Game.LootMarker> loot)
        {
            Game.MonsterMarker champion = new Game.MonsterMarker();
            champion.Id = 1;
            champion.Rarity = 1;
            champion.X = -28;
            champion.Y = 20;
            champion.IsElite = true;
            champion.Label = "Чемпион";
            champion.Affixes.Add(2542537664u);
            champion.AffixNames.Add("Электрический");
            champion.Affixes.Add(4131130388u);
            champion.AffixNames.Add("Заморозка");
            monsters.Add(champion);

            Game.MonsterMarker boss = new Game.MonsterMarker();
            boss.Id = 2;
            boss.Rarity = 7;
            boss.X = 148;
            boss.Y = 0;
            boss.IsElite = true;
            boss.IsBoss = true;
            boss.Label = "Босс";
            boss.Affixes.Add(3830498332u);
            boss.AffixNames.Add("Джаггернаут");
            boss.Affixes.Add(4172983340u);
            boss.AffixNames.Add("Осквернитель");
            monsters.Add(boss);

            loot.Add(MakeLoot(1, "Легендарка", 0, false, -24, -24));
            loot.Add(MakeLoot(2, "Древний", 1, false, 28, -22));
            loot.Add(MakeLoot(3, "Первобытный", 2, false, 134, 24));
            loot.Add(MakeLoot(4, "Комплект", 0, true, 174, -22));
        }

        private static Game.LootMarker MakeLoot(int id, string label, int rank, bool isSet,
                                                float x, float y)
        {
            Game.LootMarker item = new Game.LootMarker();
            item.Id = id;
            item.Label = label;
            item.Name = label;
            item.Rank = rank;
            item.IsSet = isSet;
            item.X = x;
            item.Y = y;
            return item;
        }

        private static void AddRoom(List<MapPolygon> polygons, double x0, double y0, double x1, double y1)        {
            const double step = 2.5;
            for (double y = y0; y < y1 - 0.001; y += step)
            {
                for (double x = x0; x < x1 - 0.001; x += step)
                {
                    double nx = Math.Min(x + step, x1);
                    double ny = Math.Min(y + step, y1);
                    MapPolygon cell = new MapPolygon();
                    cell.Points = new MapPoint[]
                    {
                        MapGeometry.Project(x, y),
                        MapGeometry.Project(nx, y),
                        MapGeometry.Project(nx, ny),
                        MapGeometry.Project(x, ny)
                    };
                    polygons.Add(cell);
                }
            }
        }

        /// <summary>
        /// Reads the map once from the running game and prints what was found.
        /// This is the command a user can run to prove the reader works on
        /// their installation.
        /// </summary>
        private static int MapCheck()
        {
            try { AttachConsole(-1); }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " — проверка карты");
            string exe = MapReader.FindGameExecutable();
            if (string.IsNullOrEmpty(exe))
            {
                Console.WriteLine("FAIL  клиент Diablo III не найден");
                return 2;
            }

            Console.WriteLine("Клиент: " + exe);
            string hash = MapReader.HashExecutable(exe);
            Console.WriteLine("SHA-256: " + hash);
            Console.WriteLine("Ожидается: " + MapReader.SupportedHash);

            if (!MapReader.IsSupportedBuild(exe))
            {
                Console.WriteLine("FAIL  сборка не поддерживается ридером (ожидается " +
                                  MapReader.SupportedVersion + ")");
                return 2;
            }
            Console.WriteLine("PASS  сборка поддерживается");

            if (!InputSender.IsGameRunning())
            {
                Console.WriteLine("STOP  игра не запущена — снимок карты невозможен");
                return 3;
            }

            try
            {
                MapSnapshot snapshot = MapReader.Capture();
                List<MapPolygon> polygons = MapGeometry.Build(snapshot);

                int walkable = 0;
                foreach (MapPolygon p in polygons) if (!p.CachedFootprint) walkable++;

                Console.WriteLine("PASS  снимок снят");
                Console.WriteLine("      процессов: " + snapshot.ProcessId);
                Console.WriteLine("      версия:    " + snapshot.GameVersion);
                Console.WriteLine("      сцен:      " + snapshot.Scenes.Count);
                Console.WriteLine("      кэш сцен:  " + snapshot.CachedScenes.Count);
                Console.WriteLine("      ресурсов:  " + snapshot.Assets.Count);
                Console.WriteLine("      ячеек:     " + walkable);
                Console.WriteLine("      без данных:" + snapshot.MissingAssetSnos.Length);

                foreach (SceneRecord scene in snapshot.Scenes)
                {
                    Console.WriteLine(string.Format(
                        "      сцена {0,4}: id={1} world={2} sno={3} bounds={4:0.0},{5:0.0}..{6:0.0},{7:0.0}",
                        scene.Slot, scene.SceneId, scene.WorldId, scene.SceneSno,
                        scene.Bounds[0], scene.Bounds[1], scene.Bounds[2], scene.Bounds[3]));
                }

                Process game = InputSender.FindGameProcess();
                if (game != null)
                {
                    try
                    {
                        using (GameMemoryReader memory = new GameMemoryReader(game.Id))
                        {
                            double px, py;
                            if (PlayerReader.TryRead(memory, game.MainModule.BaseAddress,
                                                     snapshot, out px, out py))
                            {
                                Console.WriteLine(string.Format(
                                    "PASS  позиция игрока: {0:0.00}, {1:0.00}", px, py));
                            }
                            else
                            {
                                Console.WriteLine("WARN  позиция игрока не определена (персонаж в меню или загрузке)");
                            }
                        }
                    }
                    finally { game.Dispose(); }
                }

                if (walkable == 0)
                {
                    Console.WriteLine("WARN  геометрия пуста — откройте игру в мире, а не в меню");
                    return 4;
                }

                Console.WriteLine("RESULT: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// Compiles and runs the local plugins without showing a window. This
        /// is the check that proves the plugin host works on its own.
        /// </summary>
        private static int PluginCheck()
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " — локальные плагины");
            Console.WriteLine("Папка: " + Path.Combine(AppPaths.AppRoot, Plugins.PluginRuntime.DefaultFolderName));

            try
            {
                using (Plugins.PluginRuntime runtime = new Plugins.PluginRuntime())
                {
                    runtime.Log += delegate(string text) { Console.WriteLine("      " + text); };
                    runtime.Load();

                    List<Plugins.PluginEntry> list = runtime.Plugins;
                    Console.WriteLine("Найдено плагинов: " + list.Count);

                    int failures = 0;
                    foreach (Plugins.PluginEntry entry in list)
                    {
                        if (entry.Compiled)
                        {
                            Console.WriteLine("PASS  " + entry.ClassName +
                                              "  [" + entry.StateText + "]");
                            Console.WriteLine("      " + entry.Description);
                        }
                        else
                        {
                            failures++;
                            Console.WriteLine("FAIL  " + entry.ClassName + ": " + entry.Error);
                        }
                    }

                    if (list.Count == 0)
                    {
                        Console.WriteLine("FAIL  в папке нет ни одного плагина");
                        failures++;
                    }

                    Console.WriteLine(failures == 0 ? "PLUGINS: OK" : "PLUGINS: FAILURES=" + failures);
                    return failures == 0 ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex);
                return 2;
            }
        }

        /// <summary>
        /// Loads the TurboHUD compatibility host, reports every plugin and
        /// renders one frame offscreen. This proves the public plugin API can be
        /// compiled against and painted without the vendor engine.
        /// </summary>
        private static int TurboHudCheck()
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " - TurboHUD API host");

            // Prefer the working folder. When it has no plugins yet, check the
            // shipped examples instead, so the host is still exercised.
            string folder = Path.Combine(AppPaths.AppRoot,
                TurboHud.TurboHostRuntime.DefaultFolderName);
            try
            {
                if (!Directory.Exists(folder) ||
                    Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length == 0)
                {
                    string examples = Path.Combine(AppPaths.AppRoot, "examples", "turbohud");
                    if (Directory.Exists(examples) &&
                        Directory.GetFiles(examples, "*.cs", SearchOption.AllDirectories).Length > 0)
                        folder = examples;
                }
            }
            catch { }

            Console.WriteLine("Folder: " + folder);

            try
            {
                using (TurboHud.TurboHostRuntime runtime = new TurboHud.TurboHostRuntime(folder))
                {
                    runtime.Log += delegate(string text) { Console.WriteLine("      " + text); };
                    runtime.Load();

                    List<TurboHud.TurboPluginEntry> list = runtime.Plugins;
                    Console.WriteLine("Plugins found: " + list.Count);

                    int failures = 0;
                    foreach (TurboHud.TurboPluginEntry entry in list)
                    {
                        if (entry.Compiled)
                        {
                            Console.WriteLine("PASS  " + entry.ClassName +
                                              (entry.Enabled ? "  [enabled]" : "  [disabled]"));
                            if (!string.IsNullOrEmpty(entry.Description))
                                Console.WriteLine("      " + entry.Description);
                        }
                        else
                        {
                            failures++;
                            Console.WriteLine("FAIL  " + entry.ClassName + ": " + entry.Error);
                        }
                    }

                    if (list.Count == 0)
                    {
                        Console.WriteLine("FAIL  no plugin in the folder");
                        failures++;
                    }

                    // Render one frame with a synthetic state so the painters
                    // are exercised even without a running game.
                    runtime.Context.GameRunning = true;
                    runtime.Context.PlayerKnown = true;
                    runtime.Context.PlayerX = 100f;
                    runtime.Context.PlayerY = 200f;
                    runtime.Context.WindowWidth = 800;
                    runtime.Context.WindowHeight = 600;
                    runtime.Context.Actors = new Game.ActorSnapshot
                    {
                        Monsters = new List<Game.MonsterMarker>
                        {
                            new Game.MonsterMarker
                            {
                                Id = 1,
                                X = 100f,
                                Y = 200f,
                                Health = 1000f,
                                IsElite = true,
                                Rarity = 2,
                                Sno = 12345
                            }
                        }
                    };
                    runtime.Context.Revision++;

                    int painted = 0;
                    using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(800, 600))
                    {
                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            g.Clear(System.Drawing.Color.Transparent);
                            runtime.Draw(g, 800, 600);
                        }

                        for (int y = 0; y < 600; y += 4)
                        {
                            for (int x = 0; x < 800; x += 4)
                            {
                                if (bitmap.GetPixel(x, y).A > 0) painted++;
                            }
                        }
                    }

                    Console.WriteLine("Painted samples: " + painted);
                    if (painted == 0)
                    {
                        Console.WriteLine("WARN  painters drew nothing in the test frame");
                    }

                    Console.WriteLine(failures == 0 ? "TURBOHUD: OK" : "TURBOHUD: FAILURES=" + failures);
                    return failures == 0 ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex);
                return 2;
            }
        }

        /// <summary>
        /// Feeds live game data into the TurboHUD compatibility host and prints
        /// what a plugin would see. This is the check that proves the host maps
        /// the memory reader onto the public API.
        /// </summary>
        private static int TurboHudLiveCheck()
        {
            try { AttachConsole(-1); }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " - TurboHUD live host");

            string exe = MapReader.FindGameExecutable();
            if (string.IsNullOrEmpty(exe) || !MapReader.IsSupportedBuild(exe))
            {
                Console.WriteLine("FAIL  unsupported or missing game build");
                return 2;
            }
            if (!InputSender.IsGameRunning())
            {
                Console.WriteLine("STOP  the game is not running");
                return 3;
            }

            try
            {
                Process game = InputSender.FindGameProcess();
                if (game == null) { Console.WriteLine("FAIL  game process not found"); return 2; }

                int pid = game.Id;
                IntPtr baseAddress = game.MainModule.BaseAddress;
                game.Dispose();

                using (GameMemoryReader memory = new GameMemoryReader(pid))
                {
                    Game.GameReader reader = new Game.GameReader(memory, baseAddress);

                    Game.ActorRecord player;
                    if (!reader.TryReadLocalPlayer(out player))
                    {
                        Console.WriteLine("WARN  player not available (menu or loading)");
                        return 4;
                    }
                    Console.WriteLine("PASS  player id=" + player.Id + " sno=" + player.Sno +
                                      " world=" + player.WorldId +
                                      " pos=" + player.X.ToString("0.0") + "," + player.Y.ToString("0.0"));

                    Game.ActorSnapshot actors = new Game.ActorModule().Poll(reader, player.WorldId, true);
                    Game.BuffSnapshot buffs = new Game.BuffModule().Poll(reader, player.WorldId, 0);
                    Game.SkillSnapshot skills = new Game.SkillModule().Poll(reader, reader.LocalPlayerSlot);
                    Game.InventorySnapshot inventory = new Game.InventoryModule().Poll(reader, player.WorldId);

                    TurboHost.HostContext context = new TurboHost.HostContext();
                    context.GameRunning = true;
                    context.MapAvailable = true;
                    context.PlayerKnown = true;
                    context.PlayerX = player.X;
                    context.PlayerY = player.Y;
                    context.WorldId = player.WorldId;
                    context.Actors = actors;
                    context.Buffs = buffs;
                    context.Skills = skills;
                    context.Inventory = inventory;
                    context.WindowWidth = 2560;
                    context.WindowHeight = 1440;
                    context.WorldToScreenScale = 1440f / 120f;
                    context.Revision++;

                    TurboHost.HostCanvas canvas = new TurboHost.HostCanvas();
                    TurboHost.HostController hud = new TurboHost.HostController(context, canvas);

                    int monsters = 0;
                    foreach (Turbo.Plugins.IMonster monster in hud.Game.Monsters)
                        if (monster != null) monsters++;

                    int alive = 0;
                    foreach (Turbo.Plugins.IMonster monster in hud.Game.AliveMonsters)
                        if (monster != null) alive++;

                    int items = 0;
                    foreach (Turbo.Plugins.IItem item in hud.Game.Items)
                        if (item != null) items++;

                    Turbo.Plugins.IPlayer me = hud.Game.Me;
                    Console.WriteLine("      host player: " +
                        (me == null ? "null" : me.FloorCoordinate.X.ToString("0.0") + "," +
                                                   me.FloorCoordinate.Y.ToString("0.0")));
                    Console.WriteLine("      host monsters: " + monsters + " (alive " + alive + ")");
                    Console.WriteLine("      host items: " + items);
                    Console.WriteLine("      host inventory used: " + hud.Game.InventorySpaceUsed);
                    Console.WriteLine("      host window: " + hud.Window.Size.Width + "x" + hud.Window.Size.Height);
                    Console.WriteLine("      host buffs: " + CountBuffs(context));

                    if (me == null || monsters == 0)
                    {
                        Console.WriteLine("FAIL  the host did not receive actors");
                        return 2;
                    }

                    Console.WriteLine("RESULT: OK");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex.Message);
                return 2;
            }
        }

        private static int CountBuffs(TurboHost.HostContext context)
        {
            if (context == null || context.Buffs == null || context.Buffs.Buffs == null) return 0;
            int count = 0;
            foreach (Game.BuffEntry entry in context.Buffs.Buffs)
                if (entry != null && entry.Active) count++;
            return count;
        }

        /// <summary>
        /// Writes a description for every plugin file of the HUD folder into
        /// config\plugin_descriptions.json and reports the coverage.
        /// </summary>
        private static int DescribePlugins()
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);

            List<PluginInfo> plugins = PluginManager.Scan();
            Console.WriteLine("Плагинов найдено: " + plugins.Count);

            int missing = 0;
            foreach (PluginInfo info in plugins)
            {
                if (string.IsNullOrEmpty(info.Description)) missing++;
            }
            Console.WriteLine("Без описания до разбора: " + missing);

            int added = PluginCatalog.FillMissingDescriptions(plugins);
            Console.WriteLine("Добавлено описаний: " + added);
            Console.WriteLine("Файл: " + Path.Combine(AppPaths.ConfigDir, "plugin_descriptions.json"));

            // Re-scan to prove that every plugin now has text.
            PluginCatalog.Invalidate();
            List<PluginInfo> after = PluginManager.Scan();
            int stillEmpty = 0;
            foreach (PluginInfo info in after)
            {
                if (string.IsNullOrEmpty(info.Description)) stillEmpty++;
            }

            Console.WriteLine("Всего: " + after.Count + ", без описания: " + stillEmpty);

            int shown = 0;
            foreach (PluginInfo info in after)
            {
                if (shown >= 12) break;
                Console.WriteLine("  " + info.Name + " — " + info.Description);
                shown++;
            }

            if (stillEmpty > 0) return 2;
            Console.WriteLine("DESCRIPTIONS: OK");
            return 0;
        }

        /// <summary>
        /// Checks GitHub Releases for a newer version. Used by the launcher on
        /// start-up and available as a command for diagnostics.
        /// </summary>
        private static int CheckUpdates(string repo)
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);

            if (string.IsNullOrEmpty(repo)) repo = AppPaths.Settings.UpdateRepo;
            Console.WriteLine("SuperD3Hud " + Version + " — проверка обновлений");
            Console.WriteLine("Репозиторий: " + (string.IsNullOrEmpty(repo) ? "<не задан>" : repo));

            if (string.IsNullOrEmpty(repo))
            {
                Console.WriteLine("STOP  репозиторий не задан в config\\settings.ini (UpdateRepo)");
                return 3;
            }

            UpdateInfo info = UpdateChecker.Check(repo, Version);
            if (!string.IsNullOrEmpty(info.Error))
            {
                Console.WriteLine("FAIL  " + info.Error);
                return 2;
            }

            Console.WriteLine("Текущая версия: " + info.CurrentVersion);
            Console.WriteLine("Последняя:      " + info.LatestVersion);
            if (info.Available)
            {
                Console.WriteLine("UPDATE  доступна новая версия");
                Console.WriteLine("        " + info.Url);
                return 0;
            }

            Console.WriteLine("OK  у вас последняя версия");
            return 0;
        }

        /// <summary>
        /// Reads the whole HUD state once from the running game and prints it.
        /// This is the diagnostic a user can run to prove that monsters, buffs,
        /// skills and the backpack are readable on their installation.
        /// </summary>
        private static int GameCheck()
        {
            try { AttachConsole(-1); }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " — проверка HUD");

            string exe = MapReader.FindGameExecutable();
            if (string.IsNullOrEmpty(exe))
            {
                Console.WriteLine("FAIL  клиент Diablo III не найден");
                return 2;
            }
            Console.WriteLine("Клиент: " + exe);
            if (!MapReader.IsSupportedBuild(exe))
            {
                Console.WriteLine("FAIL  сборка не поддерживается (ожидается " + MapReader.SupportedVersion + ")");
                return 2;
            }
            Console.WriteLine("PASS  сборка поддерживается");

            if (!InputSender.IsGameRunning())
            {
                Console.WriteLine("STOP  игра не запущена — читать нечего");
                return 3;
            }

            try
            {
                Process game = InputSender.FindGameProcess();
                if (game == null)
                {
                    Console.WriteLine("FAIL  процесс игры не найден");
                    return 2;
                }

                int pid = game.Id;
                IntPtr baseAddress = game.MainModule.BaseAddress;
                game.Dispose();

                using (GameMemoryReader memory = new GameMemoryReader(pid))
                {
                    Game.GameReader reader = new Game.GameReader(memory, baseAddress);

                    Game.ActorRecord player;
                    if (!reader.TryReadLocalPlayer(out player))
                    {
                        Console.WriteLine("WARN  игрок не определён (меню или загрузка)");
                        return 4;
                    }
                    Console.WriteLine(string.Format(
                        "PASS  игрок: id={0} sno={1} мир={2} позиция {3:0.0},{4:0.0}",
                        player.Id, player.Sno, player.WorldId, player.X, player.Y));

                    List<Game.ActorRecord> actors = reader.ReadActors();
                    Console.WriteLine("      актёров: " + actors.Count);

                    Game.ActorModule actorModule = new Game.ActorModule();
                    Game.ActorSnapshot actorsSnapshot = actorModule.Poll(reader, player.WorldId, true);
                    Console.WriteLine("      монстров: " + actorsSnapshot.Monsters.Count +
                                      ", добыча: " + actorsSnapshot.Loot.Count);
                    foreach (Game.MonsterMarker monster in actorsSnapshot.Monsters)
                    {
                        if (!monster.IsElite && !monster.IsBoss) continue;
                        Console.WriteLine(string.Format(
                            "        {0} sno={1} hp={2:0} на {3:0.0},{4:0.0}",
                            monster.Label, monster.Sno, monster.Health, monster.X, monster.Y));
                    }

                    Game.BuffModule buffModule = new Game.BuffModule();
                    Game.BuffSnapshot buffs = buffModule.Poll(reader, player.WorldId, 0);
                    int activeBuffs = 0;
                    int shownBuffs = 0;
                    foreach (Game.BuffEntry entry in buffs.Buffs)
                        if (entry.Active && !Game.BuffPowers.IsInternal(entry.PowerSno)) shownBuffs++;
                    foreach (Game.BuffEntry entry in buffs.Buffs) if (entry.Active) activeBuffs++;
                    Console.WriteLine("      баффов активно: " + shownBuffs +
                                      " (всего с служебными: " + activeBuffs + ")");
                    foreach (Game.BuffEntry entry in buffs.Buffs)
                    {
                        if (!entry.Active) continue;
                        if (Game.BuffPowers.IsInternal(entry.PowerSno)) continue;
                        Console.WriteLine("        " + entry.Name +
                                          (entry.Stacks > 1 ? " x" + entry.Stacks : "") +
                                          (string.IsNullOrEmpty(entry.Element) ? "" : " · " + entry.Element));
                    }

                    Game.SkillModule skillModule = new Game.SkillModule();
                    Game.SkillSnapshot skills = skillModule.Poll(reader, reader.LocalPlayerSlot);
                    Console.WriteLine("      здоровье: " +
                        (skills.Health >= 0 ? ((int)Math.Round(skills.Health)).ToString() : "неизвестно"));

                    Game.InventoryModule inventoryModule = new Game.InventoryModule();
                    Game.InventorySnapshot inventory = inventoryModule.Poll(reader, player.WorldId);
                    Console.WriteLine("      рюкзак: " + inventory.UsedSlots + " из " + inventory.TotalSlots);
                    foreach (Game.ItemEntry item in inventory.Items)
                    {
                        Console.WriteLine("        [" + item.Column + "," + item.Row + "] " +
                                          (string.IsNullOrEmpty(item.Name) ? "предмет" : item.Name) +
                                          " (качество " + item.Quality + ", ранг " + item.Rank + ")");
                    }
                }

                Console.WriteLine("RESULT: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// Renders the built-in HUD with synthetic data, so its layout can be
        /// checked without the game running.
        /// </summary>
        private static int HudPreview(string outDir)
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(AppPaths.AppRoot, "render");
            try { Directory.CreateDirectory(outDir); }
            catch { }

            try
            {
                Game.HudSettings settings = new Game.HudSettings();
                settings.Enabled = true;

                Game.BuffSnapshot buffs = new Game.BuffSnapshot();
                AddBuff(buffs, 430674, "Convention of Elements", 1, "Огонь");
                AddBuff(buffs, 483552, "Squirt's Necklace", 4, "");
                AddBuff(buffs, 428348, "Stricken", 12, "");
                AddBuff(buffs, 777001, "Сила #777001", 1, "");

                Game.SkillSnapshot skills = new Game.SkillSnapshot();
                skills.Health = 184320;
                string[] keys = { "ЛКМ", "ПКМ", "1", "2", "3", "4" };
                double[] durations = { 0, 0, 12, 6, 30, 8 };
                double[] remaining = { 0, 0, 7.4, 0, 21.5, 0 };
                for (int i = 0; i < 6; i++)
                {
                    Game.SkillCooldown slot = new Game.SkillCooldown();
                    slot.Slot = i;
                    slot.Key = keys[i];
                    slot.DurationSeconds = durations[i];
                    slot.RemainingSeconds = remaining[i];
                    slot.Fraction = durations[i] > 0 ? remaining[i] / durations[i] : 0;
                    slot.IsEstimated = true;
                    skills.Skills.Add(slot);
                }

                Game.InventorySnapshot inventory = new Game.InventorySnapshot();
                inventory.UsedSlots = 37;
                inventory.TotalSlots = 60;

                Game.ActorSnapshot actors = new Game.ActorSnapshot();
                actors.SpecialArea = 2; // Greater Rift
                actors.RiftPercentage = 76.4;
                actors.DamageActive = true;
                actors.DamageTotal = 1842650000000d;
                actors.DamageDps = 214300000000d;
                actors.DamagePeakDps = 342100000000d;
                Game.DamageEntry own = new Game.DamageEntry();
                own.Name = Game.DamageTracker.LocalPlayerName;
                own.Damage = 1290000000000d;
                own.Dps = 150000000000d;
                own.Share = 0.70;
                own.IsLocalPlayer = true;
                actors.DamageEntries.Add(own);
                Game.DamageEntry mate = new Game.DamageEntry();
                mate.Name = "Союзник 1";
                mate.Damage = 552650000000d;
                mate.Dps = 64300000000d;
                mate.Share = 0.30;
                actors.DamageEntries.Add(mate);

                using (Game.HudOverlay overlay = new Game.HudOverlay(settings))
                {
                    overlay.Size = new System.Drawing.Size(1280, 720);
                    overlay.UpdateContent(buffs, skills, inventory, actors);

                    using (System.Drawing.Bitmap bmp = RenderHud(overlay, 1280, 720))
                    {
                        string file = Path.Combine(outDir, "hud-overlay.png");
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        Console.WriteLine("rendered " + file);
                    }
                }

                Console.WriteLine("HUD PREVIEW: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("HUD PREVIEW: " + ex);
                return 2;
            }
        }

        private static void AddBuff(Game.BuffSnapshot snapshot, int power, string name, int stacks, string element)
        {
            Game.BuffEntry entry = new Game.BuffEntry();
            entry.PowerSno = power;
            entry.Name = name;
            entry.Stacks = stacks;
            entry.Active = true;
            entry.Element = element;
            snapshot.Buffs.Add(entry);
        }

        private static System.Drawing.Bitmap RenderHud(Game.HudOverlay overlay, int width, int height)
        {
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(width, height);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(28, 26, 22));
                // A faint backdrop stands in for the game so the overlay is visible.
                using (System.Drawing.SolidBrush brush = new System.Drawing.SolidBrush(
                    System.Drawing.Color.FromArgb(40, 90, 70, 50)))
                    g.FillRectangle(brush, 0, 0, width, height);
            }
            overlay.RenderTo(bmp);
            return bmp;
        }

        /// <summary>
        /// Dumps every power-keyed attribute of the local player, so the
        /// cooldown descriptors can be identified on a live client.
        ///
        /// Diablo III keys a per-power attribute as
        /// <c>(powerSno &lt;&lt; 12) | descriptor</c>. Cooldowns are stored this
        /// way, and so are the buff icons; this command prints them all with
        /// their descriptor numbers, so the exact pair can be confirmed.
        /// </summary>
        private static int CooldownProbe()
        {
            try { AttachConsole(-1); }
            catch { }
            AppPaths.Initialize(true);

            Console.WriteLine("SuperD3Hud " + Version + " — атрибуты кулдаунов");
            if (!InputSender.IsGameRunning())
            {
                Console.WriteLine("STOP  игра не запущена");
                return 3;
            }

            try
            {
                Process game = InputSender.FindGameProcess();
                if (game == null) { Console.WriteLine("FAIL  процесс не найден"); return 2; }
                int pid = game.Id;
                IntPtr baseAddress = game.MainModule.BaseAddress;
                game.Dispose();

                using (GameMemoryReader memory = new GameMemoryReader(pid))
                {
                    Game.GameReader reader = new Game.GameReader(memory, baseAddress);
                    Game.ActorRecord player;
                    if (!reader.TryReadLocalPlayer(out player))
                    {
                        Console.WriteLine("WARN  игрок не определён");
                        return 4;
                    }

                    List<KeyValuePair<uint, uint>> attributes = reader.ReadAllAttributes(player.GroupId);
                    Console.WriteLine("Атрибутов у игрока: " + attributes.Count);
                    Console.WriteLine();

                    // Group by descriptor: descriptor -> list of (power, value).
                    SortedDictionary<int, List<KeyValuePair<int, uint>>> byDescriptor =
                        new SortedDictionary<int, List<KeyValuePair<int, uint>>>();

                    foreach (KeyValuePair<uint, uint> pair in attributes)
                    {
                        int descriptor = (int)(pair.Key & 0xFFF);
                        int power = (int)(pair.Key >> 12);
                        if (power < 1000 || power > 0xFFFFF) continue;

                        List<KeyValuePair<int, uint>> list;
                        if (!byDescriptor.TryGetValue(descriptor, out list))
                        {
                            list = new List<KeyValuePair<int, uint>>();
                            byDescriptor[descriptor] = list;
                        }
                        list.Add(new KeyValuePair<int, uint>(power, pair.Value));
                    }

                    Console.WriteLine("Дескрипторы с ключом-силой (power << 12 | descriptor):");
                    foreach (KeyValuePair<int, List<KeyValuePair<int, uint>>> entry in byDescriptor)
                    {
                        bool seed = entry.Key >= 0x168 && entry.Key <= 0x170;
                        string mark = seed ? " <-- кандидат" : "";
                        Console.WriteLine(string.Format(
                            "  0x{0:X3}  записей {1}{2}", entry.Key, entry.Value.Count, mark));

                        int shown = 0;
                        foreach (KeyValuePair<int, uint> item in entry.Value)
                        {
                            if (shown >= 4) break;
                            Console.WriteLine(string.Format(
                                "        power {0,-8} значение {1}", item.Key, item.Value));
                            shown++;
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine("Иконки баффов ожидаются в 0x2FF..0x31F, кулдауны — рядом с 0x16B.");
                    Console.WriteLine("RESULT: OK");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + ex.Message);
                return 2;
            }
        }

        private static TabControl FindTabControl(Control root)        {
            foreach (Control child in root.Controls)
            {
                TabControl tc = child as TabControl;
                if (tc != null && tc.Visible && tc.TabPages.Count > 1) return tc;
                TabControl nested = FindTabControl(child);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>
        /// Exercises every write path against a throw-away sandbox copy, so the
        /// real HUD installation is never modified.
        /// </summary>
        private static int FunctionalTest()
        {
            try { AttachConsole(-1); }
            catch { }

            int failures = 0;
            Action<string, bool> check = delegate(string name, bool ok)
            {
                if (!ok) failures++;
                Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            };
            Action<string> info = delegate(string text) { Console.WriteLine("      " + text); };

            string sandbox = Path.Combine(Path.GetTempPath(), "superd3hud-functional-" + DateTime.Now.Ticks);
            try
            {
                Directory.CreateDirectory(sandbox);
                string hud = Path.Combine(sandbox, "hud");
                string engineDir = Path.Combine(hud, "HudEngine");
                string plugins = Path.Combine(engineDir, "plugins");
                Directory.CreateDirectory(Path.Combine(plugins, "Default"));
                Directory.CreateDirectory(Path.Combine(plugins, "User"));
                Directory.CreateDirectory(Path.Combine(engineDir, "roslyn"));

                File.WriteAllText(Path.Combine(engineDir, "HudEngine.exe"), "stub");
                File.WriteAllText(Path.Combine(engineDir, "app.config"),
                    "<?xml version=\"1.0\"?>\r\n<configuration>\r\n  <appSettings>\r\n" +
                    "    <add key=\"aspnet:RoslynCompilerLocation\" value=\"C:\\wrong\\path\" />\r\n" +
                    "  </appSettings>\r\n</configuration>\r\n");
                File.WriteAllText(Path.Combine(hud, "hud.ini"),
                    "[config]\r\nptr=0\r\nlocale=enUS\r\nhttp=0\r\ndx10=0\r\nbit=0\r\nnowin7x64=1\r\nserver=EU\r\nCloud=0\r\ncache=2\r\n3288=1\r\n" +
                    "[log]\r\nlogin=12345\r\nwindows=999\r\n");

                File.WriteAllText(Path.Combine(plugins, "Default", "AlphaPlugin.cs"),
                    "using System;\r\nnamespace Hud.Plugins.Default\r\n{\r\n" +
                    "    /// <summary>Первый тестовый плагин.</summary>\r\n" +
                    "    public class AlphaPlugin : BasePlugin, IAfterCollectHandler\r\n    {\r\n    }\r\n}\r\n");
                File.WriteAllText(Path.Combine(plugins, "User", "BetaPlugin.cs"),
                    "namespace Hud.Plugins.User\r\n{\r\n    /// <summary>Второй тестовый плагин.</summary>\r\n" +
                    "    public class BetaPlugin : BasePlugin\r\n    {\r\n    }\r\n}\r\n");

                AppPaths.UseSandbox(sandbox);
                check("sandbox HUD root detected", AppPaths.HudRoot == hud);
                check("engine executable resolved",
                    AppPaths.HudExe.EndsWith("HudEngine.exe") && File.Exists(AppPaths.HudExe));
                check("engine folder resolved", AppPaths.HudDir == engineDir);

                // --- Plugin scan -------------------------------------------
                List<PluginInfo> found = PluginManager.Scan();
                check("scan finds 2 plugins", found.Count == 2);
                PluginInfo alpha = null;
                PluginInfo beta = null;
                foreach (PluginInfo p in found)
                {
                    if (p.Name == "AlphaPlugin") alpha = p;
                    if (p.Name == "BetaPlugin") beta = p;
                }
                check("AlphaPlugin parsed", alpha != null);
                check("BetaPlugin parsed", beta != null);
                if (alpha != null)
                {
                    check("namespace parsed", alpha.Namespace == "Hud.Plugins.Default");
                    check("summary parsed", alpha.Description.Contains("Первый тестовый плагин"));
                    check("category parsed", alpha.Category == "Default");
                }

                // --- Plugin disable / enable --------------------------------
                if (alpha != null)
                {
                    PluginManager.SetEnabled(alpha, false);
                    check("disabled file moved out of plugins",
                        !File.Exists(Path.Combine(plugins, "Default", "AlphaPlugin.cs")) &&
                        File.Exists(Path.Combine(engineDir, "plugins.disabled", "Default", "AlphaPlugin.cs")));
                    List<PluginInfo> afterDisable = PluginManager.Scan();
                    bool disabledSeen = false;
                    foreach (PluginInfo p in afterDisable)
                        if (p.Name == "AlphaPlugin" && !p.Enabled) disabledSeen = true;
                    check("disabled plugin still listed as off", disabledSeen);

                    PluginInfo moved = null;
                    foreach (PluginInfo p in afterDisable) if (p.Name == "AlphaPlugin") moved = p;
                    if (moved != null) PluginManager.SetEnabled(moved, true);
                    check("re-enabled file back in plugins", File.Exists(Path.Combine(plugins, "Default", "AlphaPlugin.cs")));
                }

                // --- Plugin notes and settings ------------------------------
                if (beta != null)
                {
                    Dictionary<string, string> notes = PluginManager.LoadNotes();
                    notes[beta.RelativePath] = "моя заметка";
                    PluginManager.SaveNotes(notes);
                    Dictionary<string, string> reread = PluginManager.LoadNotes();
                    check("plugin note persisted", reread.ContainsKey(beta.RelativePath) && reread[beta.RelativePath] == "моя заметка");

                    string json = "{\r\n  \"enabled\": true,\r\n  \"weight\": 5\r\n}\r\n";
                    PluginManager.SaveSettingsText(beta, json);
                    check("plugin settings persisted", PluginManager.LoadSettingsText(beta).Contains("weight"));
                    bool rejected = false;
                    try { PluginManager.SaveSettingsText(beta, "{ not json"); }
                    catch (InvalidOperationException) { rejected = true; }
                    check("invalid JSON rejected", rejected);
                }

                // --- hud.ini ------------------------------------------------
                IniFile ini = new IniFile(AppPaths.HudIni);
                ini.Load();
                check("hud.ini locale read", ini.Get("config", "locale", "") == "enUS");
                check("hud.ini log section preserved", ini.Get("log", "login", "") == "12345");
                ini.Set("config", "locale", "ruRU");
                ini.Set("config", "server", "US");
                ini.SetBool("config", "ptr", true);
                ini.Set("config", "NewKey", "NewValue");
                ini.Save();
                IniFile rereadIni = new IniFile(AppPaths.HudIni);
                rereadIni.Load();
                check("hud.ini locale written", rereadIni.Get("config", "locale", "") == "ruRU");
                check("hud.ini bool written", rereadIni.GetBool("config", "ptr", false));
                check("hud.ini new key written", rereadIni.Get("config", "NewKey", "") == "NewValue");
                check("hud.ini log untouched", rereadIni.Get("log", "windows", "") == "999");
                string rawIni = File.ReadAllText(AppPaths.HudIni);
                check("hud.ini has both sections", rawIni.Contains("[config]") && rawIni.Contains("[log]"));

                string backup = ProcessRunner.BackupHudIni();
                check("hud.ini backup created", !string.IsNullOrEmpty(backup) && File.Exists(backup));

                // --- app.config repair --------------------------------------
                ProcessRunner.FixAppConfig();
                string fixedConfig = File.ReadAllText(AppPaths.AppConfig);
                check("roslyn path repaired", fixedConfig.Contains(AppPaths.RoslynDir) && !fixedConfig.Contains("C:\\wrong\\path"));

                // --- Macros -------------------------------------------------
                MacroSettings macros = MacroSettings.CreateDefault();
                check("default hotkeys 11", macros.Hotkeys.Count == 11);
                check("default scripts 10", macros.Scripts.Count == 10);
                foreach (KeyValuePair<string, string> kv in macros.Scripts)
                {
                    List<string> errors = MacroEngine.Validate(kv.Value);
                    check("script valid: " + kv.Key, errors.Count == 0);
                    foreach (string e in errors) info(e);
                }
                check("broken script detected",
                    MacroEngine.Validate("MOVE 1\nCLICK\nBOGUS 1 2").Count == 2);
                check("bad key detected",
                    MacroEngine.Validate("KEY NotAKey").Count == 1);

                macros.Save();
                MacroSettings loaded = MacroSettings.Load();
                check("macros json round-trip", loaded.Hotkeys.Count == 11 && loaded.Scripts.Count == 10);
                check("macros defaults kept", loaded.Scripts["Salvage"].Contains("SMARTSALVAGE"));

                // --- Hotkey parsing -----------------------------------------
                check("ParseKey F1", InputSender.ParseKey("F1") == (int)Keys.F1);
                check("ParseKey 13", InputSender.ParseKey("13") == 13);
                check("ParseKey ESC", InputSender.ParseKey("ESC") == (int)Keys.Escape);
                check("ParseKey I", InputSender.ParseKey("I") == (int)Keys.I);

                // --- Profiles -----------------------------------------------
                string zip = ProfileManager.Export("test-profile");
                check("profile zip created", File.Exists(zip));
                if (File.Exists(zip))
                {
                    using (FileStream fs = File.OpenRead(zip))
                    using (System.IO.Compression.ZipArchive za = new System.IO.Compression.ZipArchive(fs))
                    {
                        List<string> names = new List<string>();
                        foreach (System.IO.Compression.ZipArchiveEntry entry in za.Entries) names.Add(entry.FullName);
                        check("profile contains settings.ini", names.Contains("settings.ini"));
                        check("profile contains macros.json", names.Contains("macros.json"));
                        check("profile contains hud.ini", names.Contains("hud.ini"));
                        check("profile contains plugin_state", names.Contains("plugin_state.txt"));
                        check("profile contains plugin settings", names.Contains("plugin_settings/User_BetaPlugin.cs.json"));
                    }

                    // Change state, then restore.
                    IniFile broken = new IniFile(AppPaths.HudIni);
                    broken.Load();
                    broken.Set("config", "locale", "deDE");
                    broken.Save();
                    string result = ProfileManager.Import(zip);
                    info(result);
                    IniFile restored = new IniFile(AppPaths.HudIni);
                    restored.Load();
                    check("profile restore reverts hud.ini", restored.Get("config", "locale", "") == "ruRU");
                    check("profile list sees backup", ProfileManager.ListBackups().Count >= 1);
                }

                // --- Path detection -----------------------------------------
                check("hud root validation", AppPaths.IsHudRoot(hud));
                check("hud root normalisation", AppPaths.NormalizeHudRoot(engineDir) == hud);

                // --- Legacy engine folder ----------------------------------
                // An installation that keeps the engine in its original folder
                // must still be recognised, so existing users are not broken.
                string legacy = Path.Combine(sandbox, "legacy");
                string legacyEngine = Path.Combine(legacy, "TurboHUD");
                Directory.CreateDirectory(legacyEngine);
                File.WriteAllText(Path.Combine(legacyEngine, "TurboHUD.exe"), "stub");
                check("legacy engine folder detected", AppPaths.IsHudRoot(legacy));
                AppPaths.Settings.HudRoot = legacy;
                check("legacy engine executable found",
                    AppPaths.HudExe.EndsWith("TurboHUD.exe") && File.Exists(AppPaths.HudExe));
                AppPaths.Settings.HudRoot = hud;

                // --- Works without any HUD engine ---------------------------
                // The map, macros and profiles must not depend on a HUD
                // installation, so an empty configuration is a valid state.
                AppPaths.Settings.HudRoot = "";
                AppPaths.Settings.HudExePath = "";
                check("no engine: empty executable path", AppPaths.HudExe == "");
                check("no engine: plugins folder next to the program",
                      AppPaths.PluginsDir == Path.Combine(sandbox, "plugins"));
                check("no engine: plugin scan is empty", PluginManager.Scan().Count == 0);
                check("no engine: folder is not an engine", !AppPaths.IsHudRoot(""));
                AppPaths.Settings.HudRoot = hud;
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("FATAL " + ex);
            }
            finally
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "FUNCTIONAL: OK" : "FUNCTIONAL: FAILURES=" + failures);
            return failures == 0 ? 0 : 2;
        }

        private static int SelfTest()
        {
            try { AttachConsole(-1); }
            catch { }
            StringBuilder report = new StringBuilder();
            int failures = 0;

            Action<string> line = delegate(string text)
            {
                report.AppendLine(text);
                try { Console.WriteLine(text); }
                catch { }
            };

            try
            {
                AppPaths.Initialize(true);
                line("SuperD3Hud " + Version + " self-test");
                line("AppRoot: " + AppPaths.AppRoot);
                line("Config:  " + AppPaths.ConfigDir);
                line("HUD root: " + (string.IsNullOrEmpty(AppPaths.HudRoot) ? "<not found>" : AppPaths.HudRoot));
                line("HUD exe:  " + (string.IsNullOrEmpty(AppPaths.HudExe) ? "<not found>" : AppPaths.HudExe + "  exists=" + File.Exists(AppPaths.HudExe)));
                line("D3 exe:   " + (string.IsNullOrEmpty(AppPaths.Settings.D3Path) ? "<not found>" : AppPaths.Settings.D3Path + "  exists=" + File.Exists(AppPaths.Settings.D3Path)));

                MacroSettings macros = MacroSettings.Load();
                line("Hotkeys:  " + macros.Hotkeys.Count);
                foreach (HotkeyBinding b in macros.Hotkeys)
                {
                    line("  " + b.Describe() + " -> " + b.Action + (b.Enabled ? "" : " (disabled)"));
                    try { InputSender.ParseKey(b.Key); }
                    catch (Exception ex)
                    {
                        failures++;
                        line("  ERROR: " + ex.Message);
                    }
                }
                foreach (KeyValuePair<string, string> kv in macros.Scripts)
                {
                    List<string> errors = MacroEngine.Validate(kv.Value);
                    if (errors.Count == 0)
                    {
                        line("Script " + kv.Key + ": OK");
                    }
                    else
                    {
                        failures += errors.Count;
                        foreach (string e in errors) line("Script " + kv.Key + ": " + e);
                    }
                }

                List<PluginInfo> plugins = PluginManager.Scan();
                line("Plugins:  " + plugins.Count);
                int shown = 0;
                foreach (PluginInfo p in plugins)
                {
                    line("  [" + (p.Enabled ? "on " : "off") + "] " + p.Name + "  (" + p.RelativePath + ")");
                    shown++;
                    if (shown >= 12) break;
                }

                // JSON round-trip check
                string temp = Path.Combine(AppPaths.ConfigDir, "selftest.json");
                JsonStore.Save(temp, macros);
                MacroSettings back = JsonStore.Load<MacroSettings>(temp, null);
                if (back == null || back.Hotkeys == null)
                {
                    failures++;
                    line("JSON round-trip: FAILED");
                }
                else
                {
                    line("JSON round-trip: OK");
                }
                try { File.Delete(temp); } catch { }

                // Smart automation settings JSON round-trip (non-default + legacy defaults)
                string smartTemp = Path.Combine(AppPaths.ConfigDir, "selftest-smart-macros.json");
                try
                {
                    MacroSettings custom = MacroSettings.CreateDefault();
                    custom.SmartMode = false;
                    custom.SalvageBulkButtons = false;
                    custom.SalvageLegendaries = false;
                    custom.ProtectAncient = false;
                    custom.ProtectPrimal = true;
                    custom.LockedLeftColumns = 1;
                    custom.LockedRightColumns = 3;
                    custom.EmpoweredGR = true;
                    custom.GemUpgradeCount = 5;
                    custom.GambleStopFreeSlots = 7;
                    custom.AbortOnMouseMove = false;
                    JsonStore.Save(smartTemp, custom);

                    MacroSettings customBack = JsonStore.Load<MacroSettings>(smartTemp, null);
                    bool smartRoundTrip = customBack != null
                        && !customBack.SmartMode
                        && !customBack.SalvageBulkButtons
                        && !customBack.SalvageLegendaries
                        && !customBack.ProtectAncient
                        && customBack.ProtectPrimal
                        && customBack.LockedLeftColumns == 1
                        && customBack.LockedRightColumns == 3
                        && customBack.EmpoweredGR
                        && customBack.GemUpgradeCount == 5
                        && customBack.GambleStopFreeSlots == 7
                        && !customBack.AbortOnMouseMove;
                    line(smartRoundTrip ? "PASS  smart macro settings JSON round-trip" : "FAIL  smart macro settings JSON round-trip");
                    if (!smartRoundTrip) failures++;

                    // Verify legacy JSON without smart fields gets safe defaults
                    File.WriteAllText(smartTemp, "{\"Enabled\":true,\"StepDelayMs\":3}", new UTF8Encoding(false));
                    MacroSettings legacyDefaults = JsonStore.Load<MacroSettings>(smartTemp, null);
                    bool safeDefaults = legacyDefaults != null
                        && legacyDefaults.SmartMode
                        && legacyDefaults.SalvageBulkButtons
                        && legacyDefaults.SalvageLegendaries
                        && legacyDefaults.ProtectAncient
                        && legacyDefaults.ProtectPrimal
                        && legacyDefaults.LockedLeftColumns == 0
                        && legacyDefaults.LockedRightColumns == 2
                        && !legacyDefaults.EmpoweredGR
                        && legacyDefaults.GemUpgradeCount == 4
                        && legacyDefaults.GambleStopFreeSlots == 2
                        && legacyDefaults.AbortOnMouseMove;
                    line(safeDefaults ? "PASS  legacy macros JSON preserves safe smart defaults" : "FAIL  legacy macros JSON preserves safe smart defaults");
                    if (!safeDefaults) failures++;
                }
                finally
                {
                    try { File.Delete(smartTemp); } catch { }
                }

                // --- Local plugin host --------------------------------------
                line("Local plugins: " + Path.Combine(AppPaths.AppRoot,
                    Plugins.PluginRuntime.DefaultFolderName));
                using (Plugins.PluginRuntime runtime = new Plugins.PluginRuntime())
                {
                    runtime.Log += delegate(string text) { line("      " + text); };
                    runtime.Load();
                    List<Plugins.PluginEntry> local = runtime.Plugins;
                    line("Local plugins found: " + local.Count);
                    if (local.Count == 0)
                    {
                        failures++;
                        line("FAIL  no local plugins were created");
                    }
                    foreach (Plugins.PluginEntry entry in local)
                    {
                        if (!entry.Compiled)
                        {
                            failures++;
                            line("FAIL  " + entry.ClassName + ": " + entry.Error);
                            continue;
                        }
                        line("PASS  " + entry.ClassName + " [" + entry.StateText + "]");
                        if (string.IsNullOrEmpty(entry.Description))
                        {
                            failures++;
                            line("FAIL  no description for " + entry.ClassName);
                        }
                    }

                    // Enable/disable round-trip must be remembered.
                    if (local.Count > 0)
                    {
                        Plugins.PluginEntry first = local[0];
                        bool original = first.Enabled;
                        runtime.SetEnabled(first, !original);
                        bool flipped = first.Enabled != original;
                        runtime.SetEnabled(first, original);
                        if (!flipped || first.Enabled != original)
                        {
                            failures++;
                            line("FAIL  plugin state is not toggled");
                        }
                        else line("PASS  plugin state toggles");
                    }
                }

                // --- Plugin descriptions ------------------------------------
                List<PluginInfo> described = PluginManager.Scan();
                if (described.Count > 0)
                {
                    int missing = 0;
                    foreach (PluginInfo info in described)
                        if (string.IsNullOrEmpty(info.Description)) missing++;
                    if (missing > 0)
                    {
                        failures++;
                        line("FAIL  plugins without description: " + missing);
                    }
                    else
                    {
                        line("PASS  all " + described.Count + " plugins have a description");
                    }
                }

                // --- Update check --------------------------------------------
                // A local comparison proves the version logic without network.
                line(UpdateChecker.CompareVersions("1.0.0", "1.1.0") < 0
                    ? "PASS  version compare: older" : "FAIL  version compare: older");
                if (!(UpdateChecker.CompareVersions("1.0.0", "1.1.0") < 0)) failures++;
                line(UpdateChecker.CompareVersions("1.2.0", "1.1.9") > 0
                    ? "PASS  version compare: newer" : "FAIL  version compare: newer");
                if (!(UpdateChecker.CompareVersions("1.2.0", "1.1.9") > 0)) failures++;
                line(UpdateChecker.CompareVersions("1.1.0", "1.1.0") == 0
                    ? "PASS  version compare: equal" : "FAIL  version compare: equal");
                if (UpdateChecker.CompareVersions("1.1.0", "1.1.0") != 0) failures++;
                line(UpdateChecker.CompareVersions("1.1", "1.1.1") < 0
                    ? "PASS  version compare: shorter" : "FAIL  version compare: shorter");
                if (!(UpdateChecker.CompareVersions("1.1", "1.1.1") < 0)) failures++;
                line(UpdateChecker.NormalizeVersion("v2.3.4-beta") == "2.3.4"
                    ? "PASS  version normalize: tag" : "FAIL  version normalize: tag");
                if (UpdateChecker.NormalizeVersion("v2.3.4-beta") != "2.3.4") failures++;

                // --- HUD modules ---------------------------------------------
                Game.ActorModule actorModule = new Game.ActorModule();
                line(actorModule.Snapshot != null ? "PASS  actor module has a snapshot" : "FAIL  actor module snapshot is null");
                if (actorModule.Snapshot == null) failures++;

                Game.BuffModule buffModule = new Game.BuffModule();
                line(buffModule.Snapshot != null ? "PASS  buff module has a snapshot" : "FAIL  buff module snapshot is null");
                if (buffModule.Snapshot == null) failures++;

                Game.InventoryModule inventoryModule = new Game.InventoryModule();
                line(inventoryModule.Snapshot != null ? "PASS  inventory module has a snapshot" : "FAIL  inventory module snapshot is null");
                if (inventoryModule.Snapshot == null) failures++;

                Game.SkillModule skillModule = new Game.SkillModule();
                skillModule.SetCooldown(2, 12.0);
                bool cooldownSet = Math.Abs(skillModule.GetCooldown(2) - 12.0) < 0.001;
                line(cooldownSet ? "PASS  cooldown is configurable" : "FAIL  cooldown is not configurable");
                if (!cooldownSet) failures++;

                skillModule.SetCooldown(2, -5);
                bool rejected = Math.Abs(skillModule.GetCooldown(2) - 12.0) < 0.001;
                line(rejected ? "PASS  invalid cooldown rejected" : "FAIL  invalid cooldown accepted");
                if (!rejected) failures++;

                // CooldownReader: legacy migration (594/595 -> 362/363), ready sentinel (-1), active ticks, charges, potion.
                Game.CooldownReader cdReader = new Game.CooldownReader();
                cdReader.Restore(594, 595);
                bool migratedDesc = cdReader.DurationDescriptor == Game.AttributeKeys.PowerCooldownDescriptor
                    && cdReader.StartDescriptor == Game.AttributeKeys.PowerCooldownStartDescriptor;
                line(migratedDesc ? "PASS  legacy cooldown descriptor 594/595 migrated to 362/363"
                                  : "FAIL  legacy cooldown descriptor not migrated");
                if (!migratedDesc) failures++;

                Game.GameReader.EquippedSkill[] mockEq = new Game.GameReader.EquippedSkill[6];
                mockEq[0].PowerSno = 77552;
                mockEq[5].PowerSno = 302846;
                mockEq[2].PowerSno = 129217; // sentry (charge skill test)
                int mockTick = 100000;
                List<KeyValuePair<uint, uint>> mockAttrs = new List<KeyValuePair<uint, uint>>();
                // Slot 0 ready sentinel (-1 / int.MaxValue)
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(77552, (uint)Game.AttributeKeys.PowerCooldownDescriptor),
                    unchecked((uint)-1)));
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(77552, (uint)Game.AttributeKeys.PowerCooldownStartDescriptor),
                    (uint)int.MaxValue));
                // Slot 5 active cooldown: +600 ticks (10s left of 11s total)
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(302846, (uint)Game.AttributeKeys.PowerCooldownDescriptor),
                    (uint)(mockTick + 600)));
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(302846, (uint)Game.AttributeKeys.PowerCooldownStartDescriptor),
                    (uint)(mockTick - 60)));
                // Slot 2 charge-based recharge: 0 charges, next charge in +300 ticks (5s left of 8s)
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(129217, (uint)Game.AttributeKeys.SkillChargesDescriptor), 0u));
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(129217, (uint)Game.AttributeKeys.NextChargeGainedDescriptor),
                    (uint)(mockTick + 300)));
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(129217, (uint)Game.AttributeKeys.RechargeStartDescriptor),
                    (uint)(mockTick - 180)));
                // Health potion (slot 6): +900 ticks (15s left of 30s)
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(Game.AttributeKeys.HealthPotionPowerSno, (uint)Game.AttributeKeys.PowerCooldownDescriptor),
                    (uint)(mockTick + 900)));
                mockAttrs.Add(new KeyValuePair<uint, uint>(
                    Game.AttributeKeys.MakeKey(Game.AttributeKeys.HealthPotionPowerSno, (uint)Game.AttributeKeys.PowerCooldownStartDescriptor),
                    (uint)(mockTick - 900)));

                cdReader.PollAttributes(mockAttrs, mockTick, mockEq);
                Dictionary<int, double> remSlots = cdReader.RemainingBySlot();
                Dictionary<int, double> durSlots = cdReader.DurationBySlot();
                bool cdOk = !remSlots.ContainsKey(0)
                    && remSlots.ContainsKey(5) && Math.Abs(remSlots[5] - 10.0) < 0.01
                    && durSlots.ContainsKey(5) && Math.Abs(durSlots[5] - 11.0) < 0.01
                    && remSlots.ContainsKey(2) && Math.Abs(remSlots[2] - 5.0) < 0.01
                    && remSlots.ContainsKey(6) && Math.Abs(remSlots[6] - 15.0) < 0.01;
                line(cdOk ? "PASS  cooldown reader decodes 0x16A ticks, sentinels, charges, and potion"
                          : "FAIL  cooldown reader decoding");
                if (!cdOk) failures++;

                line(Game.BuffPowers.NameOf(430674) == "Convention of Elements"
                    ? "PASS  known buff power has a name"
                    : "FAIL  known buff power name");
                if (Game.BuffPowers.NameOf(430674) != "Convention of Elements") failures++;

                line(Game.BuffPowers.NameOf(999999).IndexOf("999999", StringComparison.Ordinal) >= 0
                    ? "PASS  unknown buff power is labelled"
                    : "FAIL  unknown buff power label");
                if (Game.BuffPowers.NameOf(999999).IndexOf("999999", StringComparison.Ordinal) < 0) failures++;

                // --- Damage meter ---------------------------------------------
                // The meter counts the drops of the health of the monsters and
                // credits the damage to the closest player.
                Game.DamageTracker tracker = new Game.DamageTracker();
                Game.MonsterMarker hit = new Game.MonsterMarker();
                hit.Id = 1;
                hit.Health = 1000f;
                hit.X = 10f;
                hit.Y = 0f;
                List<Game.MonsterMarker> wave = new List<Game.MonsterMarker>();
                wave.Add(hit);
                tracker.Update(wave, 5, true, 0f, 0f, null);

                hit.Health = 700f;
                wave = new List<Game.MonsterMarker>();
                wave.Add(hit);
                tracker.Update(wave, 5, true, 0f, 0f, null);

                bool counted = tracker.Total >= 299.9 && tracker.Total <= 300.1;
                line(counted ? "PASS  damage meter counts the damage" : "FAIL  damage meter total: " + tracker.Total);
                if (!counted) failures++;

                bool credited = tracker.Entries.Count == 1
                    && tracker.Entries[0].IsLocalPlayer
                    && tracker.Entries[0].Damage >= 299.9;
                line(credited ? "PASS  damage meter credits the player" : "FAIL  damage meter credits");
                if (!credited) failures++;

                // A monster that disappears died with the health it had: the
                // damage of the drop plus the rest of its health.
                wave = new List<Game.MonsterMarker>();
                tracker.Update(wave, 5, true, 0f, 0f, null);
                bool died = tracker.Total >= 999.9 && tracker.Total <= 1000.1;
                line(died ? "PASS  damage meter counts the killing blow" : "FAIL  damage meter death: " + tracker.Total);
                if (!died) failures++;

                // A new area starts the meter from scratch.
                wave = new List<Game.MonsterMarker>();
                tracker.Update(wave, 6, false, 0f, 0f, null);
                bool off = !tracker.Active && tracker.Total == 0;
                line(off ? "PASS  damage meter resets in a new area" : "FAIL  damage meter reset");
                if (!off) failures++;

                // One-shot kill within combat range counts full HP; far despawn adds 0; DPS uses elapsed window >= 1s.
                Game.MonsterMarker nearMob = new Game.MonsterMarker { Id = 10, Health = 500000f, X = 25f, Y = 0f };
                Game.MonsterMarker farMob = new Game.MonsterMarker { Id = 11, Health = 900000f, X = 150f, Y = 0f };
                wave = new List<Game.MonsterMarker> { nearMob, farMob };
                tracker.Update(wave, 7, true, 0f, 0f, null);
                tracker.Update(new List<Game.MonsterMarker>(), 7, true, 0f, 0f, null);
                bool oneShotOk = Math.Abs(tracker.Total - 500000d) < 1d && tracker.Dps > 400000d;
                line(oneShotOk ? "PASS  damage meter counts near one-shot kills, ignores far despawns, accurate initial DPS"
                               : "FAIL  damage meter one-shot/DPS: total=" + tracker.Total + " dps=" + tracker.Dps);
                if (!oneShotOk) failures++;

                // Healing does not wipe prior tracked damage on a monster.
                Game.MonsterMarker healMob = new Game.MonsterMarker { Id = 12, Health = 1000f, MaxHealth = 1000f, X = 10f, Y = 0f };
                tracker.Update(new List<Game.MonsterMarker> { healMob }, 8, true, 0f, 0f, null);
                healMob.Health = 600f;
                tracker.Update(new List<Game.MonsterMarker> { healMob }, 8, true, 0f, 0f, null);
                healMob.Health = 800f; // healed +200
                tracker.Update(new List<Game.MonsterMarker> { healMob }, 8, true, 0f, 0f, null);
                healMob.Health = 500f; // damaged +300
                tracker.Update(new List<Game.MonsterMarker> { healMob }, 8, true, 0f, 0f, null);
                tracker.Update(new List<Game.MonsterMarker>(), 8, true, 0f, 0f, null); // killed +500 => total 1200
                bool healOk = Math.Abs(tracker.Total - 1200d) < 1d;
                line(healOk ? "PASS  damage meter preserves prior damage across monster heals"
                            : "FAIL  damage meter heal total: " + tracker.Total);
                if (!healOk) failures++;

                // HUD settings survive a save/load round-trip.
                string hudIniPath = Path.Combine(AppPaths.ConfigDir, "selftest-hud.ini");
                try
                {
                    Game.HudSettings saved = new Game.HudSettings();
                    saved.Enabled = false;
                    saved.ShowBuffs = false;
                    saved.Opacity = 123;
                    saved.Scale = 1.35;
                    saved.BuffCorner = 2;
                    saved.Cooldowns[3] = 20.0;

                    IniFile hudIni = new IniFile(hudIniPath);
                    hudIni.Load();
                    saved.Save(hudIni);
                    hudIni.Save();

                    IniFile reread = new IniFile(hudIniPath);
                    reread.Load();
                    Game.HudSettings loaded = new Game.HudSettings();
                    loaded.Load(reread);

                    bool same = !loaded.Enabled && !loaded.ShowBuffs && loaded.Opacity == 123
                        && Math.Abs(loaded.Scale - 1.35) < 0.001
                        && loaded.BuffCorner == 2
                        && Math.Abs(loaded.Cooldowns[3] - 20.0) < 0.001;
                    line(same ? "PASS  HUD settings round-trip" : "FAIL  HUD settings round-trip");
                    if (!same) failures++;
                }
                finally
                {
                    try { File.Delete(hudIniPath); } catch { }
                }

                // --- Map reader --------------------------------------------
                MapTests.Run(
                    delegate(string name, bool ok)
                    {
                        if (!ok) failures++;
                        line((ok ? "PASS  " : "FAIL  ") + name);
                    },
                    line);

                // --- Macro coordinates --------------------------------------
                line("Macro reference: " + MacroEngine.ReferenceWidth + "x" + MacroEngine.ReferenceHeight);
                string legacy = "FOCUS\nMOVE 512 482\nGRID 1477 585 50 50 10 6 13\n";
                string rescaled = MacroActions.RescaleScript(legacy);
                if (!rescaled.Contains("MOVE 683 643") || !rescaled.Contains("GRID 1969 780 67 67 10 6 13"))
                {
                    failures++;
                    line("FAIL  legacy macro coordinates are rescaled");
                }
                else
                {
                    line("PASS  legacy macro coordinates are rescaled");
                }
                foreach (KeyValuePair<string, string> kv in macros.Scripts)
                {
                    if (MacroActions.IsLegacyScript(kv.Key, kv.Value))
                    {
                        failures++;
                        line("FAIL  script still uses the old reference: " + kv.Key);
                    }
                }

                // --- Smart macro commands validation ------------------------
                string validSmartScript =
                    "MOVEL 352 627\n" +
                    "MOVER 1903 780\n" +
                    "MOVEC 1109 869\n" +
                    "CLICKR\n" +
                    "SMARTSALVAGE\n" +
                    "SMARTSALVAGE 1903 780 67 67\n" +
                    "OPENGR\n" +
                    "OPENGR empowered\n" +
                    "OPENRIFT\n" +
                    "ACCEPTGR\n" +
                    "GEMUPS\n" +
                    "GEMUPS 5\n" +
                    "SMARTGAMBLE\n" +
                    "SMARTGAMBLE 60\n" +
                    "SMARTDROP\n" +
                    "CUBEUPGRADE\n" +
                    "CUBEUPGRADE 15\n";
                List<string> smartCmdErrors = MacroEngine.Validate(validSmartScript);
                if (smartCmdErrors.Count == 0)
                {
                    line("PASS  MacroEngine.Validate accepts all smart/anchored commands");
                }
                else
                {
                    failures += smartCmdErrors.Count;
                    foreach (string err in smartCmdErrors) line("FAIL  smart command validate: " + err);
                }

                string invalidSmartScript =
                    "MOVEL 100\n" +
                    "MOVER x y\n" +
                    "MOVEC 1280\n" +
                    "OPENGR invalid_arg\n" +
                    "GEMUPS 6\n" +
                    "SMARTGAMBLE 0\n" +
                    "CUBEUPGRADE 0\n";
                List<string> smartCmdRejected = MacroEngine.Validate(invalidSmartScript);
                if (smartCmdRejected.Count == 7)
                {
                    line("PASS  MacroEngine.Validate rejects invalid smart/anchored arguments");
                }
                else
                {
                    failures++;
                    line("FAIL  expected 7 validation errors for invalid smart args, got " + smartCmdRejected.Count);
                }

                // --- Smart salvage item filtering ---------------------------
                MacroSettings filterSettings = new MacroSettings();
                filterSettings.SalvageLegendaries = true;
                filterSettings.ProtectAncient = true;
                filterSettings.ProtectPrimal = true;
                filterSettings.LockedLeftColumns = 1;
                filterSettings.LockedRightColumns = 2;

                Game.ItemEntry normalLeg = new Game.ItemEntry { Column = 3, Row = 2, Quality = 9, Rank = 0, Name = "Normal Leg" };
                Game.ItemEntry ancientLeg = new Game.ItemEntry { Column = 3, Row = 3, Quality = 9, Rank = 1, Name = "Ancient Leg" };
                Game.ItemEntry primalLeg = new Game.ItemEntry { Column = 4, Row = 1, Quality = 9, Rank = 2, Name = "Primal Leg" };
                Game.ItemEntry lockedLeftLeg = new Game.ItemEntry { Column = 0, Row = 0, Quality = 9, Rank = 0, Name = "Locked Left" };
                Game.ItemEntry lockedRightLeg8 = new Game.ItemEntry { Column = 8, Row = 0, Quality = 9, Rank = 0, Name = "Locked Right 8" };
                Game.ItemEntry lockedRightLeg9 = new Game.ItemEntry { Column = 9, Row = 5, Quality = 9, Rank = 0, Name = "Locked Right 9" };
                Game.ItemEntry rareItem = new Game.ItemEntry { Column = 2, Row = 0, Quality = 6, Rank = 0, Name = "Rare" };

                bool salvageFilterOk =
                    MacroEngine.ShouldSalvageItem(normalLeg, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(ancientLeg, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(primalLeg, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(lockedLeftLeg, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(lockedRightLeg8, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(lockedRightLeg9, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(rareItem, filterSettings)
                    && MacroEngine.ShouldCubeUpgradeItem(rareItem, filterSettings)
                    && !MacroEngine.ShouldCubeUpgradeItem(normalLeg, filterSettings);

                filterSettings.ProtectAncient = false;
                bool ancientToggleOk =
                    MacroEngine.ShouldSalvageItem(ancientLeg, filterSettings)
                    && !MacroEngine.ShouldSalvageItem(primalLeg, filterSettings);

                if (salvageFilterOk && ancientToggleOk)
                {
                    line("PASS  smart salvage filtering protects primal, ancient and locked columns");
                }
                else
                {
                    failures++;
                    line("FAIL  smart salvage filtering mismatch");
                }

                // --- Macro timing -------------------------------------------
                bool inBand = true;
                bool varies = false;
                int firstValue = MacroEngine.Humanize(MacroSettings.DefaultStepDelayMs);
                for (int i = 0; i < 200; i++)
                {
                    int value = MacroEngine.Humanize(MacroSettings.DefaultStepDelayMs);
                    if (value < MacroEngine.MinStepDelay || value > MacroEngine.MaxStepDelay)
                        inBand = false;
                    if (value != firstValue) varies = true;
                }
                if (!inBand)
                {
                    failures++;
                    line("FAIL  step delay leaves the 1-5 ms band");
                }
                else line("PASS  step delay stays within 1-5 ms");

                if (!varies)
                {
                    failures++;
                    line("FAIL  step delay is not randomised");
                }
                else line("PASS  step delay is randomised");

                int longDelay = MacroEngine.HumanizeLong(30);
                if (longDelay < 25 || longDelay > 35)
                {
                    failures++;
                    line("FAIL  long delay jitter out of range: " + longDelay);
                }
                else line("PASS  long delay jitter in range");

                // Prove that a 1-5 ms pause is really that short: without the
                // raised timer resolution Windows would round it up to ~15 ms.
                InputSender.BeginPreciseTiming();
                DateTime timingStart = DateTime.UtcNow;
                for (int i = 0; i < 50; i++)
                    Thread.Sleep(MacroEngine.Humanize(MacroSettings.DefaultStepDelayMs));
                double average = (DateTime.UtcNow - timingStart).TotalMilliseconds / 50.0;
                InputSender.EndPreciseTiming();
                line("Average step pause: " + average.ToString("0.0") + " ms");
                if (average > 12.0)
                {
                    failures++;
                    line("FAIL  short pauses are rounded up (timer resolution not raised)");
                }
                else line("PASS  short pauses are honoured");

                // Buff names and the internal-flag filter, checked on the SNOs
                // that were actually seen in a live game.
                if (Game.BuffPowers.IsInternal(430674) || Game.BuffPowers.IsInternal(428348)
                    || Game.BuffPowers.IsInternal(483552))
                {
                    failures++;
                    line("FAIL  a real buff is treated as an internal flag");
                }
                else if (!Game.BuffPowers.IsInternal(134334) || !Game.BuffPowers.IsInternal(370781))
                {
                    failures++;
                    line("FAIL  an internal game flag is shown as a buff");
                }
                else
                {
                    line("PASS  buff names are readable and internal flags are hidden");
                }

                line(failures == 0 ? "RESULT: OK" : "RESULT: FAILURES=" + failures);
            }
            catch (Exception ex)
            {
                failures++;
                line("FATAL: " + ex);
            }

            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDir);
                File.WriteAllText(Path.Combine(AppPaths.ConfigDir, "selftest.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch { }

            return failures == 0 ? 0 : 2;
        }
    }
}

