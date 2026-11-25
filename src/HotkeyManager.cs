using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuperD3Hud
{
    /// <summary>Registers global hotkeys on a hidden message window.</summary>
    public class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Dictionary<int, string> _actions = new Dictionary<int, string>();
        private readonly List<int> _ids = new List<int>();
        private bool _disposed;

        public event Action<string> Pressed;

        public HotkeyManager()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "SuperD3HudHotkeys";
            CreateHandle(cp);
        }

        public List<string> RegisterAll(IEnumerable<HotkeyBinding> bindings)
        {
            UnregisterAll();
            List<string> errors = new List<string>();
            int index = 0;
            foreach (HotkeyBinding b in bindings)
            {
                index++;
                if (b == null || !b.Enabled || string.IsNullOrEmpty(b.Action)) continue;
                int vk;
                try { vk = InputSender.ParseKey(b.Key); }
                catch (Exception ex)
                {
                    errors.Add(b.Describe() + ": " + ex.Message);
                    continue;
                }

                int mods = 0;
                if (b.Ctrl) mods |= MOD_CONTROL;
                if (b.Shift) mods |= MOD_SHIFT;
                if (b.Alt) mods |= MOD_ALT;

                int id = 0x1000 + index;
                if (RegisterHotKey(Handle, id, mods, vk))
                {
                    _ids.Add(id);
                    _actions[id] = b.Action;
                }
                else
                {
                    errors.Add(b.Describe() + ": клавиша занята другой программой.");
                }
            }
            return errors;
        }

        public void UnregisterAll()
        {
            foreach (int id in _ids)
            {
                try { UnregisterHotKey(Handle, id); }
                catch { }
            }
            _ids.Clear();
            _actions.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                string action;
                if (_actions.TryGetValue(id, out action))
                {
                    Action<string> handler = Pressed;
                    if (handler != null)
                    {
                        try { handler(action); }
                        catch { }
                    }
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterAll();
            try { DestroyHandle(); }
            catch { }
        }
    }
}


