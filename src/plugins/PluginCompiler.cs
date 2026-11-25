using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;

namespace SuperD3Hud.Plugins
{
    /// <summary>Result of compiling one plugin source file.</summary>
    public class CompiledPlugin
    {
        public string FilePath;
        public string ClassName;
        public Type PluginType;
        public string Error;
        public bool Ok { get { return PluginType != null && string.IsNullOrEmpty(Error); } }
    }

    /// <summary>
    /// Compiles plugin sources at run time with the C# compiler that ships with
    /// the .NET Framework. No external SDK, no third-party HUD and no network
    /// access are involved: a plugin is just a .cs file next to the launcher.
    /// </summary>
    public static class PluginCompiler
    {
        /// <summary>Compiles every .cs file of the given folder.</summary>
        public static List<CompiledPlugin> CompileFolder(string folder, Action<string> log)
        {
            List<CompiledPlugin> results = new List<CompiledPlugin>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return results;

            string[] files;
            try { files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                if (log != null) log("Плагины: не удалось прочитать папку — " + ex.Message);
                return results;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                CompiledPlugin result = Compile(file, log);
                if (result != null) results.Add(result);
            }
            return results;
        }

        /// <summary>Compiles a single plugin file into an in-memory assembly.</summary>
        public static CompiledPlugin Compile(string file, Action<string> log)
        {
            CompiledPlugin result = new CompiledPlugin();
            result.FilePath = file;
            result.ClassName = Path.GetFileNameWithoutExtension(file);

            string source;
            try { source = File.ReadAllText(file, Encoding.UTF8); }
            catch (Exception ex)
            {
                result.Error = "Не удалось прочитать файл: " + ex.Message;
                return result;
            }

            if (source.Trim().Length == 0)
            {
                result.Error = "Пустой файл.";
                return result;
            }

            try
            {
                using (CSharpCodeProvider provider = new CSharpCodeProvider(
                    new Dictionary<string, string> { { "CompilerVersion", "v4.0" } }))
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
                    // A plugin uses the public API of the launcher itself.
                    options.ReferencedAssemblies.Add(typeof(HudPlugin).Assembly.Location);

                    CompilerResults compiled = provider.CompileAssemblyFromSource(options, source);
                    if (compiled.Errors.HasErrors)
                    {
                        StringBuilder text = new StringBuilder();
                        foreach (CompilerError error in compiled.Errors)
                        {
                            if (error.IsWarning) continue;
                            if (text.Length > 0) text.Append("; ");
                            text.Append("строка ").Append(error.Line).Append(": ").Append(error.ErrorText);
                        }
                        result.Error = text.Length > 0 ? text.ToString() : "Неизвестная ошибка компиляции.";
                        return result;
                    }

                    Type found = null;
                    foreach (Type type in compiled.CompiledAssembly.GetTypes())
                    {
                        if (type.IsAbstract || !type.IsClass) continue;
                        if (!typeof(HudPlugin).IsAssignableFrom(type)) continue;
                        found = type;
                        break;
                    }

                    if (found == null)
                    {
                        result.Error = "В файле нет класса, наследующего HudPlugin.";
                        return result;
                    }

                    result.PluginType = found;
                    result.ClassName = found.Name;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }
    }
}
