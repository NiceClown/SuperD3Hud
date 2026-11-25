using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SuperD3Hud
{
    public class PluginInfo
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string FullPath { get; set; }
        public string RelativePath { get; set; }
        public string Category { get; set; }
        public bool Enabled { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string Description { get; set; }

        public string StateText { get { return Enabled ? "Включён" : "Отключён"; } }
        public string SizeText { get { return (Size / 1024.0).ToString("0.0") + " КБ"; } }
    }

    /// <summary>
    /// Local plugin manager. It never decrypts or modifies plugin code: it only
    /// moves .cs files between plugins\ and plugins.disabled\ and stores notes
    /// and per-plugin settings next to the launcher.
    /// </summary>
    public static class PluginManager
    {
        private static readonly Regex NamespaceRegex = new Regex(@"\bnamespace\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase);
        private static readonly Regex ClassRegex = new Regex(@"\bclass\s+([A-Za-z0-9_]+)\s*(?::\s*([^\{\r\n]+))?", RegexOptions.IgnoreCase);
        private static readonly Regex SummaryRegex = new Regex(@"<summary>(.*?)</summary>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static List<PluginInfo> Scan()
        {
            List<PluginInfo> result = new List<PluginInfo>();
            ScanRoot(AppPaths.PluginsDir, true, result);
            ScanRoot(AppPaths.DisabledPluginsDir, false, result);
            result.Sort(delegate(PluginInfo a, PluginInfo b)
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static void ScanRoot(string root, bool enabled, List<PluginInfo> result)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string[] files;
            try { files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
            catch { return; }

            foreach (string file in files)
            {
                try
                {
                    PluginInfo info = Parse(file, root, enabled);
                    if (info != null) result.Add(info);
                }
                catch { }
            }
        }

        private static PluginInfo Parse(string file, string root, bool enabled)
        {
            string text = ReadHead(file, 240);
            PluginInfo info = new PluginInfo();
            info.FullPath = file;
            info.Enabled = enabled;
            info.RelativePath = MakeRelative(root, file);
            info.Size = new FileInfo(file).Length;
            info.Modified = File.GetLastWriteTime(file);

            string relDir = Path.GetDirectoryName(info.RelativePath);
            info.Category = string.IsNullOrEmpty(relDir) ? "—" : relDir;

            Match ns = NamespaceRegex.Match(text);
            info.Namespace = ns.Success ? ns.Groups[1].Value : "";

            string className = "";
            string baseName = "";
            MatchCollection classes = ClassRegex.Matches(text);
            foreach (Match m in classes)
            {
                string candidate = m.Groups[1].Value;
                string bases = m.Groups[2].Success ? m.Groups[2].Value : "";
                if (className.Length == 0)
                {
                    className = candidate;
                    baseName = bases;
                }
                if (bases.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    className = candidate;
                    baseName = bases;
                    break;
                }
            }
            info.ClassName = className;
            info.Name = className.Length > 0 ? className : Path.GetFileNameWithoutExtension(file);

            Match summary = SummaryRegex.Match(text);
            if (summary.Success)
            {
                string d = summary.Groups[1].Value;
                d = d.Replace("///", " ").Replace("\r", " ").Replace("\n", " ");
                d = Regex.Replace(d, @"\s+", " ").Trim();
                if (d.Length > 320) d = d.Substring(0, 320) + "...";
                info.Description = d;
            }

            // Every plugin gets a description: a documented summary, a curated
            // entry or a description derived from the class name.
            info.Description = PluginCatalog.Describe(info);
            if (string.IsNullOrEmpty(info.Description))
                info.Description = "Плагин «" + info.Name + "».";

            return info;
        }

        private static string ReadHead(string file, int maxLines)
        {
            StringBuilder sb = new StringBuilder();
            using (StreamReader reader = new StreamReader(file, Encoding.UTF8, true))
            {
                string line;
                int n = 0;
                while ((line = reader.ReadLine()) != null && n < maxLines)
                {
                    sb.AppendLine(line);
                    n++;
                }
            }
            return sb.ToString();
        }

        public static void SetEnabled(PluginInfo info, bool enabled)
        {
            if (info == null) throw new ArgumentNullException("info");
            if (info.Enabled == enabled) return;
            string targetRoot = enabled ? AppPaths.PluginsDir : AppPaths.DisabledPluginsDir;
            if (string.IsNullOrEmpty(targetRoot))
                throw new InvalidOperationException("Папка HUD не найдена.");
            string target = Path.Combine(targetRoot, info.RelativePath);
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(target)) File.Delete(target);
            File.Move(info.FullPath, target);
        }

        public static string Import(string sourceFile)
        {
            if (!File.Exists(sourceFile)) throw new FileNotFoundException("Файл не найден.", sourceFile);
            string userDir = Path.Combine(AppPaths.PluginsDir, "User");
            Directory.CreateDirectory(userDir);
            string name = Path.GetFileName(sourceFile);
            string dest = Path.Combine(userDir, name);
            int n = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(userDir, Path.GetFileNameWithoutExtension(name) + "_" + n + Path.GetExtension(name));
                n++;
            }
            File.Copy(sourceFile, dest);
            return dest;
        }

        public static void OpenInEditor(PluginInfo info)
        {
            if (info == null || !File.Exists(info.FullPath)) return;
            Process.Start(new ProcessStartInfo(info.FullPath) { UseShellExecute = true });
        }

        public static void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }

        // ------------------------------------------------------------------
        // Notes and local settings
        // ------------------------------------------------------------------

        public static Dictionary<string, string> LoadNotes()
        {
            Dictionary<string, string> notes = JsonStore.Load<Dictionary<string, string>>(AppPaths.NotesFile, null);
            if (notes == null) notes = new Dictionary<string, string>();
            return notes;
        }

        public static void SaveNotes(Dictionary<string, string> notes)
        {
            JsonStore.Save(AppPaths.NotesFile, notes);
        }

        public static string SettingsPath(PluginInfo info)
        {
            string safe = info.RelativePath;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            safe = safe.Replace('\\', '_').Replace('/', '_');
            return Path.Combine(AppPaths.PluginSettingsDir, safe + ".json");
        }

        public static string LoadSettingsText(PluginInfo info)
        {
            string path = SettingsPath(info);
            if (!File.Exists(path)) return "{\r\n  \"enabled\": " + (info.Enabled ? "true" : "false") + "\r\n}\r\n";
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch { return "{ }\r\n"; }
        }

        public static void SaveSettingsText(PluginInfo info, string text)
        {
            string path = SettingsPath(info);
            try
            {
                JsonStore.Deserialize<Dictionary<string, object>>(text);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Некорректный JSON: " + ex.Message);
            }
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------
        // Profile support
        // ------------------------------------------------------------------

        public static string ExportState()
        {
            StringBuilder sb = new StringBuilder();
            foreach (PluginInfo p in Scan())
            {
                sb.Append(p.Enabled ? "E" : "D");
                sb.Append('|');
                sb.AppendLine(p.RelativePath);
            }
            return sb.ToString();
        }

        public static void ApplyState(string stateText)
        {
            if (string.IsNullOrEmpty(stateText)) return;
            List<PluginInfo> current = Scan();
            string[] lines = stateText.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length < 3) continue;
                bool wantEnabled = line[0] == 'E' || line[0] == 'e';
                string rel = line.Substring(2).Trim();
                foreach (PluginInfo p in current)
                {
                    if (!string.Equals(p.RelativePath, rel, StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.Enabled != wantEnabled)
                    {
                        try { SetEnabled(p, wantEnabled); }
                        catch { }
                    }
                    break;
                }
            }
        }

        public static string MakeRelative(string root, string file)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string fullFile = Path.GetFullPath(file);
            if (fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return fullFile.Substring(fullRoot.Length);
            return Path.GetFileName(file);
        }
    }
}


