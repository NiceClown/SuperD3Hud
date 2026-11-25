using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SuperD3Hud
{
    /// <summary>
    /// Creates Windows shortcuts (.lnk) to the launcher without any external
    /// tool: the shell automation object is used through late binding, so no
    /// interop assembly is needed.
    /// </summary>
    public static class Shortcut
    {
        /// <summary>
        /// Creates or updates a shortcut to the running executable.
        /// Returns the path of the created shortcut, or an empty string.
        /// </summary>
        public static string Create(string shortcutPath, string description)
        {
            if (string.IsNullOrEmpty(shortcutPath)) return "";

            string target = ExecutablePath();
            if (string.IsNullOrEmpty(target) || !File.Exists(target)) return "";

            try
            {
                string dir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return "";

                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object link = shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod, null, shell,
                        new object[] { shortcutPath });
                    if (link == null) return "";

                    Type linkType = link.GetType();
                    linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link,
                        new object[] { target });
                    linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link,
                        new object[] { Path.GetDirectoryName(target) });
                    linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link,
                        new object[] { target + ",0" });
                    if (!string.IsNullOrEmpty(description))
                    {
                        linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link,
                            new object[] { description });
                    }
                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    if (Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                }
                return shortcutPath;
            }
            catch { return ""; }
        }

        /// <summary>Full path of the running launcher executable.</summary>
        public static string ExecutablePath()
        {
            try
            {
                Assembly assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
                    string location = assembly.Location;
                    if (!string.IsNullOrEmpty(location)) return location;
                }
                return System.Windows.Forms.Application.ExecutablePath;
            }
            catch { return ""; }
        }

        /// <summary>
        /// Creates a shortcut in the folder that contains the program folder,
        /// i.e. one level above the launcher, which is where a user looks first.
        /// </summary>
        public static string CreateNextToProgramFolder()
        {
            string exe = ExecutablePath();
            if (string.IsNullOrEmpty(exe)) return "";

            try
            {
                // exe lives in <program>\bin, so the folder the user browses
                // is the parent of the program folder.
                string binDir = Path.GetDirectoryName(exe);
                string programDir = Path.GetDirectoryName(binDir);
                string root = Path.GetDirectoryName(programDir);
                if (string.IsNullOrEmpty(root)) root = programDir;

                string path = Path.Combine(root, "SuperD3Hud.lnk");
                return Create(path, "SuperD3Hud — лаунчер Diablo III");
            }
            catch { return ""; }
        }

        /// <summary>Creates a shortcut on the desktop.</summary>
        public static string CreateOnDesktop()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop)) return "";
                string path = Path.Combine(desktop, "SuperD3Hud.lnk");
                return Create(path, "SuperD3Hud — лаунчер Diablo III");
            }
            catch { return ""; }
        }
    }
}
