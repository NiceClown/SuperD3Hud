using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>
    /// Writes the control tree of a page with real bounds. Used while tuning
    /// the layout, so clipping and stray empty space can be found without
    /// taking screenshots.
    /// </summary>
    public static class LayoutDump
    {
        public static void Write(Control root, string path)
        {
            StringBuilder text = new StringBuilder();
            Walk(root, 0, text);
            try { File.WriteAllText(path, text.ToString(), new UTF8Encoding(false)); }
            catch { }
        }

        private static void Walk(Control control, int depth, StringBuilder text)
        {
            text.Append(' ', depth * 2);
            text.Append(control.GetType().Name);
            text.Append("  x=").Append(control.Left);
            text.Append(" y=").Append(control.Top);
            text.Append(" w=").Append(control.Width);
            text.Append(" h=").Append(control.Height);
            if (control.AutoSize) text.Append(" auto");
            string label = control.Text;
            if (!string.IsNullOrEmpty(label))
            {
                label = label.Replace("\r", " ").Replace("\n", " ");
                if (label.Length > 48) label = label.Substring(0, 48) + "...";
                text.Append("  \"").Append(label).Append('"');
            }
            text.AppendLine();
            foreach (Control child in control.Controls) Walk(child, depth + 1, text);
        }
    }
}
