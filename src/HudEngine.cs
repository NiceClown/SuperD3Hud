using System;
using System.Collections.Generic;
using System.IO;

namespace SuperD3Hud
{
    /// <summary>
    /// Describes the HUD engine that SuperD3Hud drives.
    ///
    /// The launcher is engine-agnostic: it can start any compatible HUD
    /// executable that the user points it at, manage the plugin sources next
    /// to it and edit its plain-text configuration. Nothing here depends on a
    /// particular vendor, account or network service.
    ///
    /// The names below are compatibility hints only: they let the launcher
    /// recognise an engine that is already installed on the machine. The user
    /// can always override them by choosing the executable explicitly.
    /// </summary>
    public static class HudEngine
    {
        /// <summary>
        /// Executable names that are recognised inside an engine folder, most
        /// preferred first. The first match wins.
        /// </summary>
        public static readonly string[] ExecutableNames =
        {
            "HudEngine.exe",
            "SuperD3Hud.Engine.exe",
            "TurboHUD.exe"          // compatibility with an engine that is already installed
        };

        /// <summary>
        /// Process names of the engine itself, used to tell whether it is running.
        /// </summary>
        public static readonly string[] ProcessNames =
        {
            "HudEngine",
            "SuperD3Hud.Engine",
            "TurboHUD"
        };

        /// <summary>
        /// Process names of a vendor launcher that may host the engine.
        /// </summary>
        public static readonly string[] LoaderProcessNames =
        {
            "TurboHUD_LightningMOD",
            "HudEngineLoader"
        };

        /// <summary>
        /// Folder names that may contain the engine, used while searching.
        /// </summary>
        public static readonly string[] FolderNames =
        {
            "HudEngine",
            "SuperD3Hud.Engine",
            "TurboHUD"
        };

        /// <summary>Name of the engine executable, resolved for display.</summary>
        public static string ExecutableName
        {
            get
            {
                string exe = AppPaths.HudExe;
                if (string.IsNullOrEmpty(exe)) return ExecutableNames[0];
                return Path.GetFileName(exe);
            }
        }

        /// <summary>
        /// Returns the first engine executable found inside the given folder.
        /// </summary>
        public static string FindExecutable(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "";

            foreach (string name in ExecutableNames)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }

            // Fall back to a configured executable whose name is not in the list.
            string configured = AppPaths.Settings.HudExePath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            {
                try
                {
                    string dir = Path.GetDirectoryName(configured);
                    if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(folder),
                                      StringComparison.OrdinalIgnoreCase))
                        return configured;
                }
                catch { }
            }
            return "";
        }

        /// <summary>
        /// True when the folder looks like a HUD engine folder.
        /// </summary>
        public static bool IsEngineFolder(string folder)
        {
            return !string.IsNullOrEmpty(FindExecutable(folder));
        }

        /// <summary>
        /// True when the folder itself, or a known subfolder, contains an engine.
        /// </summary>
        public static bool ContainsEngine(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            if (IsEngineFolder(folder)) return true;
            foreach (string name in FolderNames)
            {
                string nested = Path.Combine(folder, name);
                if (IsEngineFolder(nested)) return true;
            }
            return false;
        }

        /// <summary>Names of all engine and loader processes.</summary>
        public static IEnumerable<string> AllProcessNames()
        {
            foreach (string name in ProcessNames) yield return name;
            foreach (string name in LoaderProcessNames) yield return name;
        }
    }
}
