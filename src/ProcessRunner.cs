using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SuperD3Hud
{
    /// <summary>
    /// Starts and stops the HUD engine and Diablo III. The engine is treated
    /// as a generic external program: SuperD3Hud never loads it, patches it or
    /// inspects it, it only launches the executable the user selected.
    /// </summary>
    public static class ProcessRunner
    {
        public static bool IsHudRunning()
        {
            foreach (string name in HudEngine.ProcessNames)
            {
                if (CountProcess(name) > 0) return true;
            }
            return false;
        }

        public static bool IsLoaderRunning()
        {
            foreach (string name in HudEngine.LoaderProcessNames)
            {
                if (CountProcess(name) > 0) return true;
            }
            return false;
        }

        public static int CountProcess(string name)
        {
            try
            {
                Process[] list = Process.GetProcessesByName(name);
                int n = list.Length;
                foreach (Process p in list) p.Dispose();
                return n;
            }
            catch { return 0; }
        }

        public static void StartHud()
        {
            if (IsHudRunning() || IsLoaderRunning())
            {
                throw new InvalidOperationException("HUD уже запущен.");
            }

            string exe;
            string workDir;
            if (AppPaths.Settings.LaunchMode == "loader" &&
                !string.IsNullOrEmpty(AppPaths.Settings.CustomLauncher) &&
                File.Exists(AppPaths.Settings.CustomLauncher))
            {
                exe = AppPaths.Settings.CustomLauncher;
                workDir = Path.GetDirectoryName(exe);
            }
            else
            {
                exe = AppPaths.HudExe;
                workDir = AppPaths.HudDir;
            }

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                throw new FileNotFoundException(
                    "Не найден исполняемый файл HUD. Укажите папку HUD или сам файл в настройках.", exe);

            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        /// <summary>
        /// Reads the tail of the engine status log. Returns an empty string
        /// when the log is missing or unreadable.
        /// </summary>
        public static string ReadStatusLog(int maxChars)
        {
            string path = Path.Combine(Path.Combine(AppPaths.HudDir, "logs"), "status.txt");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string all = reader.ReadToEnd();
                    if (all.Length <= maxChars) return all;
                    return all.Substring(all.Length - maxChars);
                }
            }
            catch { return ""; }
        }

        /// <summary>
        /// Checks whether the engine refused to start because it was launched
        /// without its vendor loader. Such builds print "wrong setup".
        /// </summary>
        public static bool DetectedWrongSetup()
        {
            string tail = ReadStatusLog(4000);
            return tail.IndexOf("wrong setup", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void StartGame()
        {
            string exe = AppPaths.Settings.D3Path;
            if (string.IsNullOrEmpty(exe)) exe = AppPaths.FindGameExe();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "-launch");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = true;
                Process.Start(psi);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("battlenet://D3") { UseShellExecute = true });
            }
            catch
            {
                throw new FileNotFoundException("Не удалось найти Diablo III.exe. Укажите путь вручную.");
            }
        }

        public static void StartBoth()
        {
            StartGame();
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                Thread.Sleep(5000);
                try { if (!IsHudRunning() && !IsLoaderRunning()) StartHud(); }
                catch { }
            });
        }

        public static int StopHud()
        {
            int killed = 0;
            foreach (string name in HudEngine.ProcessNames) killed += KillAll(name);
            foreach (string name in HudEngine.LoaderProcessNames) killed += KillAll(name);
            return killed;
        }

        private static int KillAll(string name)
        {
            int killed = 0;
            Process[] list;
            try { list = Process.GetProcessesByName(name); }
            catch { return 0; }
            foreach (Process p in list)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                        killed++;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return killed;
        }

        /// <summary>Repairs the Roslyn compiler path inside the engine app.config.</summary>
        public static void FixAppConfig()
        {
            string path = AppPaths.AppConfig;
            if (!File.Exists(path)) throw new FileNotFoundException("app.config не найден.", path);
            string text = File.ReadAllText(path, Encoding.UTF8);
            string roslyn = AppPaths.RoslynDir;
            string updated = Regex.Replace(
                text,
                @"(<add\s+key=""aspnet:RoslynCompilerLocation""\s+value="")[^""]*("")",
                m => m.Groups[1].Value + roslyn + m.Groups[2].Value,
                RegexOptions.IgnoreCase);

            if (updated == text)
            {
                // Key absent: insert it into appSettings.
                if (updated.IndexOf("<appSettings>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    updated = Regex.Replace(updated,
                        @"<appSettings>",
                        "<appSettings>\r\n    <add key=\"aspnet:RoslynCompilerLocation\" value=\"" + roslyn + "\" />",
                        RegexOptions.IgnoreCase);
                }
            }

            File.Copy(path, path + ".bak", true);
            File.WriteAllText(path, updated, new UTF8Encoding(false));
        }

        public static string BackupHudIni()
        {
            string src = AppPaths.HudIni;
            if (!File.Exists(src)) return "";
            Directory.CreateDirectory(AppPaths.BackupsDir);
            string name = "hud-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ini";
            string dest = Path.Combine(AppPaths.BackupsDir, name);
            File.Copy(src, dest, true);
            return dest;
        }

        public static string GetFileVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return info.FileVersion ?? "";
            }
            catch { return ""; }
        }

        public static string GetHudVersion() { return GetFileVersion(AppPaths.HudExe); }

        public static string GetGameVersion()
        {
            string exe = AppPaths.Settings.D3Path;
            if (string.IsNullOrEmpty(exe)) exe = AppPaths.FindGameExe();
            return GetFileVersion(exe);
        }
    }
}
