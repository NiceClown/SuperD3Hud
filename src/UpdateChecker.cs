using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SuperD3Hud
{
    /// <summary>Result of an update check.</summary>
    public class UpdateInfo
    {
        /// <summary>True when a newer release exists.</summary>
        public bool Available;
        /// <summary>Version of the newest release, without a leading "v".</summary>
        public string LatestVersion = "";
        /// <summary>Page a user can open to download the release.</summary>
        public string Url = "";
        /// <summary>Short release notes, may be empty.</summary>
        public string Notes = "";
        /// <summary>Human readable error, empty on success.</summary>
        public string Error = "";
        /// <summary>Version of the running program.</summary>
        public string CurrentVersion = "";
        /// <summary>True when the check actually ran.</summary>
        public bool Checked;
    }

    /// <summary>
    /// Checks GitHub Releases for a newer version of the launcher.
    ///
    /// The check is optional and completely silent: it runs in the background,
    /// only reads the public release list of one repository and never sends
    /// any information about the user. When no repository is configured or the
    /// network is unavailable, the launcher simply keeps working.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiTemplate = "https://api.github.com/repos/{0}/releases/latest";
        private const int TimeoutMs = 9000;

        /// <summary>
        /// GitHub only accepts TLS 1.2, while .NET Framework negotiates an
        /// older protocol by default. The value is set numerically because the
        /// compiler targets the .NET 4.0 reference assemblies, where the enum
        /// member is not declared yet.
        /// </summary>
        private const int Tls12 = 3072;

        static UpdateChecker()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)Tls12;
            }
            catch { }
        }

        /// <summary>
        /// Checks the repository given as "owner/name". Returns null when the
        /// check could not run at all.
        /// </summary>
        public static UpdateInfo Check(string repo, string currentVersion)
        {
            UpdateInfo info = new UpdateInfo();
            info.CurrentVersion = currentVersion ?? "";

            if (string.IsNullOrEmpty(repo) || repo.IndexOf('/') <= 0)
            {
                info.Error = "Не задан репозиторий обновлений.";
                return info;
            }

            try
            {
                string url = string.Format(CultureInfo.InvariantCulture, ApiTemplate, repo.Trim());
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = "SuperD3Hud/" + currentVersion;
                request.Accept = "application/vnd.github+json";
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.AllowAutoRedirect = true;

                string json;
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                info.Checked = true;

                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> release =
                    ser.Deserialize<Dictionary<string, object>>(json);
                if (release == null)
                {
                    info.Error = "Пустой ответ GitHub.";
                    return info;
                }

                string tag = GetString(release, "tag_name");
                string name = GetString(release, "name");
                info.Url = GetString(release, "html_url");
                info.Notes = GetString(release, "body");

                string latest = NormalizeVersion(tag);
                if (latest.Length == 0) latest = NormalizeVersion(name);
                info.LatestVersion = latest;

                if (latest.Length == 0)
                {
                    info.Error = "Не удалось определить версию последнего релиза.";
                    return info;
                }

                info.Available = CompareVersions(latest, currentVersion) > 0;
                return info;
            }
            catch (WebException ex)
            {
                info.Error = DescribeWebError(ex);
                return info;
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
                return info;
            }
        }

        /// <summary>Runs the check on a background thread and reports the result.</summary>
        public static void CheckAsync(string repo, string currentVersion, Action<UpdateInfo> done)
        {
            if (done == null) return;

            Thread thread = new Thread(delegate()
            {
                UpdateInfo info = null;
                try { info = Check(repo, currentVersion); }
                catch (Exception ex)
                {
                    info = new UpdateInfo();
                    info.Error = ex.Message;
                }
                try { done(info); }
                catch { }
            });
            thread.IsBackground = true;
            thread.Name = "UpdateCheck";
            thread.Start();
        }

        private static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value);
        }

        /// <summary>Strips a leading "v" and any suffix after a dash or plus.</summary>
        public static string NormalizeVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string value = text.Trim();
            if (value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
                value = value.Substring(1);
            int cut = value.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cut > 0) value = value.Substring(0, cut);
            return value.Trim();
        }

        /// <summary>
        /// Compares two dotted versions numerically. Missing parts count as 0,
        /// so "1.2" is older than "1.2.1".
        /// </summary>
        public static int CompareVersions(string left, string right)
        {
            int[] a = ParseParts(NormalizeVersion(left));
            int[] b = ParseParts(NormalizeVersion(right));

            int length = Math.Max(a.Length, b.Length);
            for (int i = 0; i < length; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static int[] ParseParts(string version)
        {
            if (string.IsNullOrEmpty(version)) return new int[0];
            string[] raw = version.Split('.');
            List<int> parts = new List<int>();
            foreach (string item in raw)
            {
                int value;
                if (int.TryParse(item.Trim(), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out value))
                    parts.Add(value);
                else
                    parts.Add(0);
            }
            return parts.ToArray();
        }

        private static string DescribeWebError(WebException ex)
        {
            if (ex.Status == WebExceptionStatus.Timeout)
                return "Истекло время ожидания ответа GitHub.";
            if (ex.Status == WebExceptionStatus.NameResolutionFailure ||
                ex.Status == WebExceptionStatus.ConnectFailure)
                return "Нет соединения с GitHub.";

            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return "Репозиторий или релиз не найдены (404).";
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    return "GitHub ограничил частоту запросов (403).";
                return "GitHub ответил кодом " + (int)response.StatusCode + ".";
            }
            return ex.Message;
        }
    }
}
