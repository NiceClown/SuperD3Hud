using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CSharp;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHud
{
    /// <summary>Result of compiling one TurboHUD plugin source file.</summary>
    public class TurboCompiledPlugin
    {
        public string FilePath;
        public string ClassName;
        public Type PluginType;
        public string Error;
        public bool Ok { get { return PluginType != null && string.IsNullOrEmpty(Error); } }
    }

    /// <summary>
    /// Compiles TurboHUD plugin sources at run time against the public plugin
    /// API. A plugin is an ordinary .cs file that uses Turbo.Plugins; no engine
    /// binary and no network access are involved.
    /// </summary>
    public static class TurboPluginCompiler
    {
        private static readonly Regex _errorRegex = new Regex(
            @"^(?<file>[^(]+)\((?<line>\d+)(?:,\d+)?\):\s*error\s*(?<id>[^:]+):\s*(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<TurboCompiledPlugin> CompileFolder(string folder, Action<string> log)
        {
            List<TurboCompiledPlugin> results = new List<TurboCompiledPlugin>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return results;

            string[] files;
            try { files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                if (log != null) log("TurboHUD plugins: cannot read the folder - " + ex.Message);
                return results;
            }

            if (files.Length == 0) return results;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            string roslyn = FindRoslynCsc();
            if (!string.IsNullOrEmpty(roslyn))
            {
                if (log != null) log("Using Roslyn compiler: " + roslyn);
                return CompileBatchRoslyn(new List<string>(files), folder, roslyn, log);
            }

            return CompileBatchCodeDom(new List<string>(files), folder, log);
        }

        public static TurboCompiledPlugin Compile(string file, Action<string> log)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                return new TurboCompiledPlugin
                {
                    FilePath = file,
                    ClassName = Path.GetFileNameWithoutExtension(file),
                    Error = "File not found."
                };
            }

            List<TurboCompiledPlugin> batch = CompileFolder(Path.GetDirectoryName(file), log);
            foreach (TurboCompiledPlugin p in batch)
            {
                if (string.Equals(p.FilePath, file, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return new TurboCompiledPlugin
            {
                FilePath = file,
                ClassName = Path.GetFileNameWithoutExtension(file),
                Error = "The file has no class implementing Turbo.Plugins.IPlugin."
            };
        }

        private static string FindRoslynCsc()
        {
            // The compiler travels with the program: a portable installation
            // must not depend on a folder of another computer.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "roslyn", "csc.exe"),
                Path.Combine(baseDir, "..", "roslyn", "csc.exe")
            };

            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path)) return Path.GetFullPath(path);
                }
                catch { }
            }
            return null;
        }

        private static List<TurboCompiledPlugin> CompileBatchRoslyn(
            List<string> fileList, string folder, string cscPath, Action<string> log)
        {
            List<TurboCompiledPlugin> results = new List<TurboCompiledPlugin>();
            List<string> currentFiles = new List<string>(fileList);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string tempDir = Path.Combine(baseDir, "temp_compiler");
            try { Directory.CreateDirectory(tempDir); } catch { }

            while (currentFiles.Count > 0)
            {
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                string rspPath = Path.Combine(tempDir, "args_" + id + ".rsp");
                string dllPath = Path.Combine(tempDir, "plugins_" + id + ".dll");

                List<string> rspLines = new List<string>();
                rspLines.Add("/nologo");
                rspLines.Add("/target:library");
                rspLines.Add("/platform:anycpu");
                rspLines.Add("/optimize+");
                rspLines.Add("/out:\"" + dllPath + "\"");

                rspLines.Add("/r:\"System.dll\"");
                rspLines.Add("/r:\"System.Core.dll\"");
                rspLines.Add("/r:\"System.Drawing.dll\"");
                rspLines.Add("/r:\"System.Windows.Forms.dll\"");

                string[] hudRefs = new string[]
                {
                    "Turbo.Plugins.dll",
                    "Turbo.Plugins.Default.dll",
                    "SharpDX.dll",
                    "SharpDX.Direct2D1.dll",
                    "SharpDX.Direct3D11.dll",
                    "SharpDX.DXGI.dll",
                    "SharpDX.Mathematics.dll",
                    "SharpDX.DirectInput.dll"
                };
                foreach (string r in hudRefs)
                {
                    string p = Path.Combine(baseDir, r);
                    if (File.Exists(p)) rspLines.Add("/r:\"" + p + "\"");
                }

                foreach (string f in currentFiles)
                {
                    rspLines.Add("\"" + f + "\"");
                }

                try { File.WriteAllLines(rspPath, rspLines, Encoding.UTF8); }
                catch (Exception ex)
                {
                    if (log != null) log("Cannot write response file: " + ex.Message);
                    break;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = cscPath;
                psi.Arguments = "@\"" + rspPath + "\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                string output = "";
                int exitCode = -1;
                try
                {
                    using (Process proc = Process.Start(psi))
                    {
                        output = proc.StandardOutput.ReadToEnd() + "\n" + proc.StandardError.ReadToEnd();
                        if (!proc.WaitForExit(30000))
                        {
                            try { proc.Kill(); } catch { }
                            exitCode = -2;
                        }
                        else
                        {
                            exitCode = proc.ExitCode;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log("Roslyn compiler execution failed: " + ex.Message);
                    break;
                }
                finally
                {
                    try { File.Delete(rspPath); } catch { }
                }

                if (exitCode == 0 && File.Exists(dllPath))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(dllPath);
                        Assembly asm = Assembly.Load(raw);
                        Dictionary<string, string> fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (Type type in asm.GetTypes())
                        {
                            if (type.IsAbstract || !type.IsClass) continue;
                            if (!typeof(IPlugin).IsAssignableFrom(type)) continue;

                            string matchedFile = MatchFileForType(type.Name, currentFiles, fileContents);
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = matchedFile ?? (currentFiles.Count > 0 ? currentFiles[0] : folder);
                            cp.ClassName = type.Name;
                            cp.PluginType = type;
                            results.Add(cp);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (log != null) log("Failed to load compiled assembly: " + ex.Message);
                    }
                    finally
                    {
                        try { File.Delete(dllPath); } catch { }
                        try { File.Delete(Path.ChangeExtension(dllPath, ".pdb")); } catch { }
                    }
                    break;
                }
                else
                {
                    Dictionary<string, StringBuilder> fileErrors = ParseErrors(output, currentFiles);
                    if (fileErrors.Count == 0)
                    {
                        if (log != null) log("Roslyn compilation error: " + output);
                        foreach (string f in currentFiles)
                        {
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = f;
                            cp.ClassName = Path.GetFileNameWithoutExtension(f);
                            cp.Error = string.IsNullOrEmpty(output) ? "Unknown compilation error" : output.Trim();
                            results.Add(cp);
                        }
                        break;
                    }

                    List<string> remaining = new List<string>();
                    foreach (string f in currentFiles)
                    {
                        StringBuilder err;
                        if (fileErrors.TryGetValue(f, out err))
                        {
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = f;
                            cp.ClassName = Path.GetFileNameWithoutExtension(f);
                            cp.Error = err.ToString();
                            results.Add(cp);
                        }
                        else
                        {
                            remaining.Add(f);
                        }
                    }

                    if (remaining.Count == currentFiles.Count) break;
                    currentFiles = remaining;
                }
            }

            try { Directory.Delete(tempDir, true); } catch { }
            return results;
        }

        private static List<TurboCompiledPlugin> CompileBatchCodeDom(
            List<string> files, string folder, Action<string> log)
        {
            List<TurboCompiledPlugin> results = new List<TurboCompiledPlugin>();
            List<string> currentFiles = new List<string>(files);

            while (currentFiles.Count > 0)
            {
                using (CSharpCodeProvider provider = new CSharpCodeProvider(
                    new Dictionary<string, string> { { "CompilerVersion", "v4.0" } }))
                {
                    CompilerParameters options = CreateCompilerParameters();
                    CompilerResults compiled = provider.CompileAssemblyFromFile(options, currentFiles.ToArray());

                    if (!compiled.Errors.HasErrors)
                    {
                        Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (Type type in compiled.CompiledAssembly.GetTypes())
                        {
                            if (type.IsAbstract || !type.IsClass) continue;
                            if (!typeof(IPlugin).IsAssignableFrom(type)) continue;

                            string matched = MatchFileForType(type.Name, currentFiles, cache);
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = matched ?? (currentFiles.Count > 0 ? currentFiles[0] : folder);
                            cp.ClassName = type.Name;
                            cp.PluginType = type;
                            results.Add(cp);
                        }
                        break;
                    }

                    Dictionary<string, StringBuilder> fileErrors = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
                    foreach (CompilerError err in compiled.Errors)
                    {
                        if (err.IsWarning) continue;
                        string f = FindMatchingFilePath(err.FileName, currentFiles);
                        if (f != null)
                        {
                            StringBuilder sb;
                            if (!fileErrors.TryGetValue(f, out sb))
                            {
                                sb = new StringBuilder();
                                fileErrors[f] = sb;
                            }
                            if (sb.Length > 0) sb.Append("; ");
                            sb.Append("line ").Append(err.Line).Append(": ").Append(err.ErrorText);
                        }
                    }

                    if (fileErrors.Count == 0)
                    {
                        foreach (string f in currentFiles)
                        {
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = f;
                            cp.ClassName = Path.GetFileNameWithoutExtension(f);
                            cp.Error = "Compilation error in batch.";
                            results.Add(cp);
                        }
                        break;
                    }

                    List<string> remaining = new List<string>();
                    foreach (string f in currentFiles)
                    {
                        StringBuilder sb;
                        if (fileErrors.TryGetValue(f, out sb))
                        {
                            TurboCompiledPlugin cp = new TurboCompiledPlugin();
                            cp.FilePath = f;
                            cp.ClassName = Path.GetFileNameWithoutExtension(f);
                            cp.Error = sb.ToString();
                            results.Add(cp);
                        }
                        else
                        {
                            remaining.Add(f);
                        }
                    }

                    if (remaining.Count == currentFiles.Count) break;
                    currentFiles = remaining;
                }
            }
            return results;
        }

        private static string MatchFileForType(string typeName, List<string> files, Dictionary<string, string> cache)
        {
            foreach (string f in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(f);
                if (string.Equals(fileName, typeName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            string classToken = "class " + typeName;
            foreach (string f in files)
            {
                string text;
                if (!cache.TryGetValue(f, out text))
                {
                    try { text = File.ReadAllText(f, Encoding.UTF8); }
                    catch { text = ""; }
                    cache[f] = text;
                }
                if (text.IndexOf(classToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            }

            return files.Count > 0 ? files[0] : null;
        }

        private static Dictionary<string, StringBuilder> ParseErrors(string output, List<string> files)
        {
            Dictionary<string, StringBuilder> dict = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(output)) return dict;

            string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                Match m = _errorRegex.Match(line);
                if (m.Success)
                {
                    string rawFile = m.Groups["file"].Value.Trim();
                    string lineNo = m.Groups["line"].Value;
                    string msg = m.Groups["msg"].Value.Trim();

                    string fullFile = FindMatchingFilePath(rawFile, files);
                    if (fullFile != null)
                    {
                        StringBuilder sb;
                        if (!dict.TryGetValue(fullFile, out sb))
                        {
                            sb = new StringBuilder();
                            dict[fullFile] = sb;
                        }
                        if (sb.Length > 0) sb.Append("; ");
                        sb.Append("line ").Append(lineNo).Append(": ").Append(msg);
                    }
                }
            }
            return dict;
        }

        private static string FindMatchingFilePath(string rawFile, List<string> files)
        {
            foreach (string f in files)
            {
                if (string.Equals(f, rawFile, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(f), Path.GetFileName(rawFile), StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private static CompilerParameters CreateCompilerParameters()
        {
            CompilerParameters options = new CompilerParameters();
            options.GenerateInMemory = true;
            options.GenerateExecutable = false;
            options.IncludeDebugInformation = false;
            options.TreatWarningsAsErrors = false;
            options.ReferencedAssemblies.Add("System.dll");
            options.ReferencedAssemblies.Add("System.Core.dll");
            options.ReferencedAssemblies.Add("System.Drawing.dll");
            options.ReferencedAssemblies.Add("System.Windows.Forms.dll");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AddReference(options, Path.Combine(baseDir, "Turbo.Plugins.dll"));
            AddReference(options, Path.Combine(baseDir, "Turbo.Plugins.Default.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.Direct2D1.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.Direct3D11.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.DXGI.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.Mathematics.dll"));
            AddReference(options, Path.Combine(baseDir, "SharpDX.DirectInput.dll"));
            return options;
        }

        private static void AddReference(CompilerParameters options, string path)
        {
            if (File.Exists(path)) options.ReferencedAssemblies.Add(path);
        }
    }
}
