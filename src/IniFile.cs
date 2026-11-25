using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SuperD3Hud
{
    /// <summary>
    /// Small INI reader/writer that preserves unrelated lines and comments.
    /// Section and key names are case-insensitive.
    /// </summary>
    public class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines = new List<string>();

        public IniFile(string path)
        {
            _path = path;
        }

        public string Path { get { return _path; } }

        public bool Exists { get { return File.Exists(_path); } }

        public void Load()
        {
            _lines.Clear();
            if (!File.Exists(_path)) return;
            string[] raw = File.ReadAllLines(_path, Encoding.UTF8);
            foreach (string line in raw) _lines.Add(line);
        }

        private bool IsAppSettingsFile()
        {
            try
            {
                return string.Equals(System.IO.Path.GetFullPath(_path),
                                     System.IO.Path.GetFullPath(AppPaths.SettingsFile),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void Save()
        {
            // Diagnostics run beside a live GUI: they must not rewrite the
            // program's own settings.ini. Other ini files (sandbox copies in
            // tests, the HUD's hud.ini) are still written normally.
            if (AppPaths.ReadOnly && IsAppSettingsFile()) return;

            string dir = System.IO.Path.GetDirectoryName(_path);
            string temp = _path + ".tmp";
            string[] lines = _lines.ToArray();
            UTF8Encoding encoding = new UTF8Encoding(false);
            Exception lastError = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllLines(temp, lines, encoding);
                    if (File.Exists(_path))
                        File.Replace(temp, _path, null);
                    else
                        File.Move(temp, _path);
                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    if (attempt < 2) Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    if (attempt < 2) Thread.Sleep(50);
                }
            }

            // Best effort: never leave a partial temporary file behind.
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { }

            System.Diagnostics.Trace.WriteLine("IniFile: could not save " + _path + ": " +
                (lastError == null ? "" : lastError.Message));
        }

        public string Get(string section, string key, string fallback)
        {
            int start;
            int end;
            if (!FindSection(section, out start, out end)) return fallback;
            for (int i = start + 1; i < end; i++)
            {
                string k;
                string v;
                if (!TryParseKeyValue(_lines[i], out k, out v)) continue;
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return fallback;
        }

        public int GetInt(string section, string key, int fallback)
        {
            string v = Get(section, key, null);
            int n;
            if (v != null && int.TryParse(v.Trim(), out n)) return n;
            return fallback;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            string v = Get(section, key, null);
            if (v == null) return fallback;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }

        public void Set(string section, string key, string value)
        {
            if (value == null) value = "";
            int start;
            int end;
            if (!FindSection(section, out start, out end))
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length != 0) _lines.Add("");
                _lines.Add("[" + section + "]");
                _lines.Add(key + "=" + value);
                return;
            }

            for (int i = start + 1; i < end; i++)
            {
                string k;
                string v;
                if (!TryParseKeyValue(_lines[i], out k, out v)) continue;
                if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                _lines[i] = key + "=" + value;
                return;
            }

            // Insert before any trailing blank lines, so a new key stays inside
            // the section instead of appearing after a visual gap.
            int insertAt = end;
            while (insertAt - 1 > start && _lines[insertAt - 1].Trim().Length == 0) insertAt--;
            _lines.Insert(insertAt, key + "=" + value);
        }

        public void SetBool(string section, string key, bool value)
        {
            Set(section, key, value ? "1" : "0");
        }

        public void Remove(string section, string key)
        {
            int start;
            int end;
            if (!FindSection(section, out start, out end)) return;
            for (int i = start + 1; i < end; i++)
            {
                string k;
                string v;
                if (!TryParseKeyValue(_lines[i], out k, out v)) continue;
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines.RemoveAt(i);
                    return;
                }
            }
        }

        public List<string> Sections()
        {
            List<string> result = new List<string>();
            foreach (string line in _lines)
            {
                string t = line.Trim();
                if (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']')
                {
                    string name = t.Substring(1, t.Length - 2).Trim();
                    if (!result.Contains(name)) result.Add(name);
                }
            }
            return result;
        }

        public List<KeyValuePair<string, string>> GetSection(string section)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            int start;
            int end;
            if (!FindSection(section, out start, out end)) return result;
            for (int i = start + 1; i < end; i++)
            {
                string k;
                string v;
                if (TryParseKeyValue(_lines[i], out k, out v))
                    result.Add(new KeyValuePair<string, string>(k, v));
            }
            return result;
        }

        private bool FindSection(string section, out int start, out int end)
        {
            start = -1;
            end = _lines.Count;
            for (int i = 0; i < _lines.Count; i++)
            {
                string t = _lines[i].Trim();
                if (t.Length < 2 || t[0] != '[' || t[t.Length - 1] != ']') continue;
                string name = t.Substring(1, t.Length - 2).Trim();
                if (start < 0)
                {
                    if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase)) start = i;
                }
                else
                {
                    end = i;
                    break;
                }
            }
            return start >= 0;
        }

        private static bool TryParseKeyValue(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (line == null) return false;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#' || t[0] == '[') return false;
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;
            key = line.Substring(0, eq).Trim();
            value = line.Substring(eq + 1).Trim();
            return key.Length > 0;
        }
    }
}


