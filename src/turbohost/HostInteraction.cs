using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Input actions for plugins. Everything goes through the launcher's own
    /// input sender, so a plugin can click and press keys exactly like the
    /// built-in macros do.
    /// </summary>
    public class HostInteractionController : IInteractionController
    {
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseRightDown = 0x0008;
        private const uint MouseRightUp = 0x0010;
        private const uint MouseMiddleDown = 0x0020;
        private const uint MouseMiddleUp = 0x0040;
        private const uint MouseWheel = 0x0800;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

        private readonly HostContext _context;
        private readonly HostWindow _window;
        private readonly HostInputController _input;
        private readonly PerfCounter _perfCounter = new PerfCounter();
        private readonly Dictionary<ActionKey, System.Threading.Timer> _continuous = new Dictionary<ActionKey, System.Threading.Timer>();
        private readonly object _sync = new object();

        public HostInteractionController(HostContext context, HostWindow window, HostInputController input)
        {
            _context = context;
            _window = window;
            _input = input;
        }

        public IPerfCounter PerfCounter { get { return _perfCounter; } }

        public void ClickAt(MouseButtons button, RectangleF rect, double rndRatio)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            double x = rect.X + rect.Width * (0.5 + (new Random().NextDouble() - 0.5) * rndRatio);
            double y = rect.Y + rect.Height * (0.5 + (new Random().NextDouble() - 0.5) * rndRatio);
            ClickAt(button, x, y, 0, 0);
        }

        public void ClickAt(MouseButtons button, double x, double y, double rndX, double rndY)
        {
            if (!IsFinite(x) || !IsFinite(y)) return;
            double px = x;
            double py = y;
            if (IsFinite(rndX) && rndX > 0) px += (new Random().NextDouble() - 0.5) * rndX;
            if (IsFinite(rndY) && rndY > 0) py += (new Random().NextDouble() - 0.5) * rndY;

            int sx = (int)Math.Round(px) + (_context == null ? 0 : _context.WindowX);
            int sy = (int)Math.Round(py) + (_context == null ? 0 : _context.WindowY);

            try
            {
                if (!InputSender.IsGameForeground()) return;
                InputSender.MoveTo(sx, sy);
                InputSender.Click(button == MouseButtons.Right);
            }
            catch { }
        }

        public void ClickHuman(MouseButtons button, RectangleF rect, double rndRatio)
        {
            ClickAt(button, rect, rndRatio);
        }

        public void ClickHuman(MouseButtons button, double x, double y, double rndX, double rndY)
        {
            ClickAt(button, x, y, rndX, rndY);
        }

        public void MoveMouseOverUiElement(IUiElement element) { }
        public void ClickUiElement(MouseButtons button, IUiElement element, bool small = false, bool top = false) { }
        public void ClickInventoryItem(MouseButtons button, IItem item)
        {
            if (item == null) return;
            ClickAt(button, item.InventoryX * 35.0, item.InventoryY * 35.0, 0, 0);
        }

        public void MoveMouseOverInventoryItem(IItem item)
        {
            if (item == null) return;
            MouseMove(item.InventoryX * 35.0, item.InventoryY * 35.0);
        }

        public void TalkTownActor(IActor actor) { }
        public void TalkActor(IActor actor) { }

        public bool IsHotKeySet(ActionKey key)
        {
            return KeyFor(key) != 0;
        }

        public void DoAction(ActionKey key, bool standStillDown = false, int sleepAfter = 10,
                             int keyDownDelayOverride = -1, int keyDelayOverride = -1)
        {
            int vk = KeyFor(key);
            if (vk == 0) return;

            try
            {
                if (!SuperD3Hud.InputSender.IsGameForeground()) return;
                if (SuperD3Hud.InputSender.IsKeyDown(vk)) return;
                if (vk == 0x01) InputSender.Click(false);
                else if (vk == 0x02) InputSender.Click(true);
                else InputSender.KeyPress(vk);
            }
            catch { }

            if (sleepAfter > 0) Thread.Sleep(sleepAfter);
        }

        public void DoActionAutoShift(ActionKey key, int sleepAfter = 10,
                                      int keyDownDelayOverride = -1, int keyDelayOverride = -1)
        {
            DoAction(key, false, sleepAfter, keyDownDelayOverride, keyDelayOverride);
        }

        public void StartContinuousAction(ActionKey key, bool standStillDown,
                                          int keyDownDelayOverride = -1, int keyDelayOverride = -1)
        {
            int vk = KeyFor(key);
            if (vk == 0) return;

            lock (_sync)
            {
                System.Threading.Timer existing;
                if (_continuous.TryGetValue(key, out existing)) return;

                System.Threading.Timer timer = new System.Threading.Timer(delegate(object state)
                {
                    try
                    {
                        // Never send input to a window that is not the game:
                        // a plugin's continuous action must not type into the
                        // desktop or another application.
                        if (!SuperD3Hud.InputSender.IsGameForeground()) return;
                        if (SuperD3Hud.InputSender.IsKeyDown(vk)) return;
                        if (vk == 0x01) InputSender.Click(false);
                        else if (vk == 0x02) InputSender.Click(true);
                        else InputSender.KeyPress(vk);
                    }
                    catch { }
                }, null, 0, 30);

                _continuous[key] = timer;
            }
        }

        public void StopContinuousAction(ActionKey key, int keyDownDelayOverride = -1,
                                         int keyDelayOverride = -1)
        {
            lock (_sync)
            {
                System.Threading.Timer timer;
                if (!_continuous.TryGetValue(key, out timer)) return;
                _continuous.Remove(key);
                try { timer.Dispose(); } catch { }
            }
        }

        public bool IsContinuousActionStarted(ActionKey key)
        {
            lock (_sync) return _continuous.ContainsKey(key);
        }

        public void ToggleGameUi(bool visible, int keyDownDelayOverride = -1, int keyDelayOverride = -1) { }

        public void PressEnter()
        {
            try { InputSender.KeyPress(0x0D); } catch { }
        }

        public void PressEsc()
        {
            try { InputSender.KeyPress(0x1B); } catch { }
        }

        public void ShiftDown()
        {
            try { InputSender.KeyPress(0x10); } catch { }
        }

        public void ShiftUp() { }
        public void StandStillDown() { ShiftDown(); }
        public void StandStillUp() { }

        public void ScrollUp(int count)
        {
            try { mouse_event(MouseWheel, 0, 0, (uint)(120 * Math.Max(1, count)), UIntPtr.Zero); }
            catch { }
        }

        public void ScrollDown(int count)
        {
            try { mouse_event(MouseWheel, 0, 0, unchecked((uint)(-120 * Math.Max(1, count))), UIntPtr.Zero); }
            catch { }
        }

        public void StopAllContinuousActions()
        {
            lock (_sync)
            {
                foreach (System.Threading.Timer timer in _continuous.Values)
                {
                    try { timer.Dispose(); } catch { }
                }
                _continuous.Clear();
            }
        }

        public bool MouseMove(double x, double y, double rndX = 0, double rndY = 0,
                              double length = -1, bool doEvents = true)
        {
            double px = x;
            double py = y;
            if (rndX > 0) px += (new Random().NextDouble() - 0.5) * rndX;
            if (rndY > 0) py += (new Random().NextDouble() - 0.5) * rndY;

            int sx = (int)Math.Round(px) + (_context == null ? 0 : _context.WindowX);
            int sy = (int)Math.Round(py) + (_context == null ? 0 : _context.WindowY);

            try { return InputSender.MoveTo(sx, sy); }
            catch { return false; }
        }

        public void MouseDown(MouseButtons button)
        {
            try { mouse_event(DownFlag(button), 0, 0, 0, UIntPtr.Zero); }
            catch { }
        }

        public void MouseUp(MouseButtons button)
        {
            try { mouse_event(UpFlag(button), 0, 0, 0, UIntPtr.Zero); }
            catch { }
        }

        public bool NewGame() { return false; }

        public bool PressOkOnGenericModalDialog()
        {
            PressEnter();
            return true;
        }

        private static uint DownFlag(MouseButtons button)
        {
            if (button == MouseButtons.Right) return MouseRightDown;
            if (button == MouseButtons.Middle) return MouseMiddleDown;
            return MouseLeftDown;
        }

        private static uint UpFlag(MouseButtons button)
        {
            if (button == MouseButtons.Right) return MouseRightUp;
            if (button == MouseButtons.Middle) return MouseMiddleUp;
            return MouseLeftUp;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static int KeyFor(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0x01;
                case ActionKey.RightSkill: return 0x02;
                case ActionKey.Skill1: return 0x31;
                case ActionKey.Skill2: return 0x32;
                case ActionKey.Skill3: return 0x33;
                case ActionKey.Skill4: return 0x34;
                case ActionKey.Heal: return 0x51;
                case ActionKey.Move: return 0x01;
                case ActionKey.TownPortal: return 0x54;
                case ActionKey.Inventory: return 0x49;
                case ActionKey.Close: return 0x1B;
                case ActionKey.Social: return 0x0D;
                case ActionKey.WaypointMap: return 0x4D;
                case ActionKey.Map: return 0x09;
                case ActionKey.SkillsWindow: return 0x53;
                case ActionKey.ParagonWindow: return 0x50;
                default: return 0;
            }
        }
    }
}
