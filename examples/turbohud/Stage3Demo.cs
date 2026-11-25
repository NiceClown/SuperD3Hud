using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SharpDX.DirectInput;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace TurboPlugins
{
    /// <summary>Проверка этапа 3: инвентарь, ввод, очередь и служебные контроллеры.</summary>
    public class Stage3Demo : BasePlugin, IInGameTopPainter
    {
        /// <summary>Краткое описание для списка плагинов.</summary>
        public string Description
        {
            get { return "Демонстрация этапа 3: инвентарь, ввод, очередь и служебные контроллеры."; }
        }

        private IFont _font;
        private bool _queued;
        private bool _logged;

        public override void Load(IController hud)
        {
            base.Load(hud);
            if (Hud == null || Hud.Render == null) return;
            _font = Hud.Render.CreateFont("Segoe UI", 11f, 240, 220, 180, 255,
                                         false, false, true);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || Hud == null || _font == null) return;

            int inventoryCount = 0;
            int floorCount = 0;
            IItem first = null;
            if (Hud.Inventory != null)
            {
                IEnumerable<IItem> inventory = Hud.Inventory.ItemsInInventory;
                if (inventory != null)
                {
                    foreach (IItem item in inventory)
                    {
                        if (item == null) continue;
                        inventoryCount++;
                        if (first == null) first = item;
                    }
                }

                IEnumerable<IItem> floor = Hud.Inventory.ItemsOnFloor;
                if (floor != null)
                {
                    foreach (IItem item in floor)
                        if (item != null) floorCount++;
                }
            }

            RectangleF itemRect = RectangleF.Empty;
            ISnoItem itemSno = null;
            if (first != null && Hud.Inventory != null)
            {
                itemRect = Hud.Inventory.GetItemRect(first);
                itemSno = Hud.Inventory.GetSnoItem(first.AcdId);
            }

            bool shiftDown = false;
            IKeyEvent keyEvent = null;
            if (Hud.Input != null)
            {
                shiftDown = Hud.Input.IsKeyDown(Keys.LShiftKey);
                keyEvent = Hud.Input.CreateKeyEvent(true, Key.LeftShift, false, false, true);
            }

            int queueCount = 0;
            if (Hud.Queue != null)
            {
                if (!_queued)
                {
                    Hud.Queue.AddItem(new Stage3QueueItem());
                    _queued = true;
                }
                IEnumerable<Stage3QueueItem> queued = Hud.Queue.GetItems<Stage3QueueItem>();
                if (queued != null)
                    foreach (Stage3QueueItem item in queued)
                        if (item != null) queueCount++;
            }

            ITexture texture = Hud.Texture == null ? null : Hud.Texture.GetTexture("stage3-placeholder");
            IPerfCounter counter = Hud.Stat == null ? null : Hud.Stat.RenderPerfCounter;
            double renderCount = counter == null ? 0d : counter.LastCount;
            bool speak = Hud.Sound != null && Hud.Sound.IsSpeakEnabled;

            if (!_logged && Hud.TextLog != null)
            {
                Hud.TextLog.Log("stage3-demo.log", "Stage3 controllers exercised", true, true);
                _logged = true;
            }

            string snoName = itemSno == null ? "none" : (itemSno.NameEnglish ?? "");
            string eventText = keyEvent == null ? "none" : (keyEvent.IsPressed ? "down" : "up");
            string textureText = texture == null ? "none" : texture.Width.ToString("0");
            _font.DrawText("Stage3 inv=" + inventoryCount + " floor=" + floorCount
                + " rect=" + itemRect.Width.ToString("0") + " sno=" + snoName
                + " shift=" + shiftDown + " key=" + eventText + " queue=" + queueCount
                + " tex=" + textureText + " perf=" + renderCount.ToString("0")
                + " speak=" + speak, 18f, 124f);
        }
    }

    internal sealed class Stage3QueueItem : IQueueItem
    {
        private readonly DateTime _queuedOn;
        private readonly TimeSpan _lifeTime;

        public Stage3QueueItem()
        {
            _queuedOn = DateTime.Now;
            _lifeTime = TimeSpan.FromSeconds(30);
        }

        public DateTime QueuedOn { get { return _queuedOn; } }
        public TimeSpan LifeTime { get { return _lifeTime; } }
    }
}
