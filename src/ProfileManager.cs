using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SuperD3Hud
{
    /// <summary>
    /// Local profiles: everything is packed into a zip inside config\backups.
    /// No cloud, no account, no network.
    /// </summary>
    public static class ProfileManager
    {
        public static string Export(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "profile";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            Directory.CreateDirectory(AppPaths.BackupsDir);
            string file = Path.Combine(AppPaths.BackupsDir, name + ".zip");
            if (File.Exists(file)) File.Delete(file);

            using (FileStream fs = new FileStream(file, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddFile(zip, AppPaths.SettingsFile, "settings.ini");
                AddFile(zip, AppPaths.MacrosFile, "macros.json");
                AddFile(zip, AppPaths.NotesFile, "plugin_notes.json");
                AddFile(zip, AppPaths.HudIni, "hud.ini");
                AddFile(zip, AppPaths.PluginsConfig, "PluginsConfig.dat");

                AddText(zip, "plugin_state.txt", PluginManager.ExportState());

                if (Directory.Exists(AppPaths.PluginSettingsDir))
                {
                    foreach (string f in Directory.GetFiles(AppPaths.PluginSettingsDir, "*.json"))
                        AddFile(zip, f, "plugin_settings/" + Path.GetFileName(f));
                }
            }
            return file;
        }

        public static string Import(string zipFile)
        {
            if (!File.Exists(zipFile)) throw new FileNotFoundException("Архив не найден.", zipFile);
            string temp = Path.Combine(AppPaths.ConfigDir, "restore-" + DateTime.Now.Ticks);
            Directory.CreateDirectory(temp);
            try
            {
                ZipFile.ExtractToDirectory(zipFile, temp);

                int restored = 0;
                restored += RestoreFile(Path.Combine(temp, "settings.ini"), AppPaths.SettingsFile);
                restored += RestoreFile(Path.Combine(temp, "macros.json"), AppPaths.MacrosFile);
                restored += RestoreFile(Path.Combine(temp, "plugin_notes.json"), AppPaths.NotesFile);
                restored += RestoreFile(Path.Combine(temp, "hud.ini"), AppPaths.HudIni);
                restored += RestoreFile(Path.Combine(temp, "PluginsConfig.dat"), AppPaths.PluginsConfig);

                string settingsSrc = Path.Combine(temp, "plugin_settings");
                if (Directory.Exists(settingsSrc))
                {
                    Directory.CreateDirectory(AppPaths.PluginSettingsDir);
                    foreach (string f in Directory.GetFiles(settingsSrc, "*.json"))
                    {
                        File.Copy(f, Path.Combine(AppPaths.PluginSettingsDir, Path.GetFileName(f)), true);
                        restored++;
                    }
                }

                string stateFile = Path.Combine(temp, "plugin_state.txt");
                if (File.Exists(stateFile))
                {
                    PluginManager.ApplyState(File.ReadAllText(stateFile, Encoding.UTF8));
                    restored++;
                }
                return "Восстановлено файлов: " + restored;
            }
            finally
            {
                try { Directory.Delete(temp, true); }
                catch { }
            }
        }

        private static int RestoreFile(string source, string target)
        {
            if (!File.Exists(source) || string.IsNullOrEmpty(target)) return 0;
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(source, target, true);
            return 1;
        }

        public static List<string> ListBackups()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(AppPaths.BackupsDir)) return result;
            string[] files = Directory.GetFiles(AppPaths.BackupsDir, "*.zip");
            Array.Sort(files);
            Array.Reverse(files);
            foreach (string f in files) result.Add(f);
            return result;
        }

        public static void Delete(string file)
        {
            if (File.Exists(file)) File.Delete(file);
        }

        private static void AddFile(ZipArchive zip, string path, string entryName)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream output = entry.Open())
            using (FileStream input = File.OpenRead(path))
            {
                input.CopyTo(output);
            }
        }

        private static void AddText(ZipArchive zip, string entryName, string text)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream output = entry.Open())
            using (StreamWriter writer = new StreamWriter(output, new UTF8Encoding(false)))
            {
                writer.Write(text ?? "");
            }
        }
    }
}


