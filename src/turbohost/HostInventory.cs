using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SharpDX.DirectInput;
using SuperD3Hud.Game;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>An item in the host's work queue.</summary>
    public class HostQueueItem : IQueueItem
    {
        public DateTime QueuedOn { get; set; }
        public TimeSpan LifeTime { get; set; }

        public HostQueueItem()
        {
            QueuedOn = DateTime.Now;
            LifeTime = TimeSpan.FromSeconds(30);
        }
    }

    /// <summary>A small, expiring work queue for plugins.</summary>
    public class HostQueueController : IQueueController
    {
        private readonly object _sync = new object();
        private readonly List<IQueueItem> _items = new List<IQueueItem>();

        public void AddItem(IQueueItem item)
        {
            if (item == null) return;
            lock (_sync) _items.Add(item);
        }

        public IEnumerable<T> GetItems<T>() where T : IQueueItem
        {
            List<T> result = new List<T>();
            DateTime now = DateTime.Now;

            lock (_sync)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    IQueueItem item = _items[i];
                    bool expired = false;
                    if (item == null)
                    {
                        expired = true;
                    }
                    else
                    {
                        try
                        {
                            TimeSpan life = item.LifeTime;
                            DateTime queuedOn = item.QueuedOn;
                            // A non-positive lifetime is an explicit request for
                            // an item that is no longer current.  Guard the
                            // subtraction as plugin implementations may expose
                            // malformed DateTime values.
                            if (life <= TimeSpan.Zero || queuedOn == DateTime.MinValue)
                                expired = true;
                            else if (now >= queuedOn)
                                expired = now - queuedOn >= life;
                        }
                        catch
                        {
                            expired = true;
                        }
                    }

                    if (expired)
                    {
                        _items.RemoveAt(i);
                        continue;
                    }

                    try
                    {
                        if (item is T) result.Add((T)item);
                    }
                    catch
                    {
                        // A broken plugin item must not break queue consumers.
                    }
                }
            }

            return result;
        }
    }

    /// <summary>A key event created by a plugin.</summary>
    public class HostKeyEvent : IKeyEvent
    {
        private readonly bool _isPressed;
        private readonly Key _key;
        private readonly bool _control;
        private readonly bool _alt;
        private readonly bool _shift;

        public HostKeyEvent(bool isPressed, Key key, bool control, bool alt, bool shift)
        {
            _isPressed = isPressed;
            _key = key;
            _control = control;
            _alt = alt;
            _shift = shift;
        }

        public bool IsPressed { get { return _isPressed; } }
        public Key Key { get { return _key; } }
        public bool ControlPressed { get { return _control; } }
        public bool AltPressed { get { return _alt; } }
        public bool ShiftPressed { get { return _shift; } }

        public bool Matches(IKeyEvent otherKeyEvent)
        {
            if (otherKeyEvent == null) return false;
            return otherKeyEvent.Key == _key
                && otherKeyEvent.IsPressed == _isPressed
                && otherKeyEvent.ControlPressed == _control
                && otherKeyEvent.AltPressed == _alt
                && otherKeyEvent.ShiftPressed == _shift;
        }
    }

    /// <summary>Input controller backed by the launcher's own input sender.</summary>
    public class HostInputController : IInputController
    {
        public bool IsKeyDown(Keys key)
        {
            try { return SuperD3Hud.InputSender.IsKeyDown((int)key); }
            catch { return false; }
        }

        public IKeyEvent CreateKeyEvent(bool isPressed, Key key, bool controlPressed,
                                        bool altPressed, bool shiftPressed)
        {
            return new HostKeyEvent(isPressed, key, controlPressed, altPressed, shiftPressed);
        }
    }

    /// <summary>
    /// Backpack controller. Items come from the live snapshot; grid geometry is
    /// a fixed cell layout because the host does not read the game's UI layout.
    /// </summary>
    public class HostInventoryController : IInventoryController
    {
        private const float CellSize = 35f;

        private readonly HostContext _context;
        private readonly HostSnoController _sno;
        private readonly Func<Rectangle> _lockAreaProvider;

        /// <summary>Optional global provider returning the locked backpack columns.</summary>
        public static Func<Rectangle> LockAreaProvider { get; set; }

        public HostInventoryController(HostContext context, HostSnoController sno)
            : this(context, sno, null)
        {
        }

        public HostInventoryController(HostContext context, HostSnoController sno, Func<Rectangle> lockAreaProvider)
        {
            _context = context;
            _sno = sno;
            _lockAreaProvider = lockAreaProvider;
        }

        public int MaxStashPageCount { get { return 2; } }
        public int MaxStashTabCountPerPage { get { return 5; } }
        public int SelectedStashPageIndex { get { return 0; } }
        public int SelectedStashTabIndex { get { return 0; } }
        public int HoveredStashTabIndex { get { return 0; } }

        public int GetStashTabUsedSpace(int pageIndex, int tabIndex)
        {
            if (pageIndex < 0 || pageIndex >= MaxStashPageCount) return 0;
            if (tabIndex < 0 || tabIndex >= MaxStashTabCountPerPage) return 0;
            // The game UI is not read by this host, so an unavailable stash is
            // represented as an empty, but valid, tab rather than null data.
            return 0;
        }

        public IEnumerable<IItem> ItemsOnFloor
        {
            get
            {
                List<IItem> result = new List<IItem>();
                if (_context == null || _context.Actors == null || _context.Actors.Loot == null)
                    return result;

                List<LootMarker> loot = _context.Actors.Loot;
                for (int i = 0; i < loot.Count; i++)
                {
                    LootMarker marker = loot[i];
                    if (marker == null) continue;
                    result.Add(new HostItem(_context, _sno, marker.Gbid, 9, marker.Rank,
                                            marker.Name, marker.X, marker.Y, 0, 0, true));
                }
                return result;
            }
        }

        public IEnumerable<IItem> ItemsInStash { get { return new List<IItem>(); } }

        public IEnumerable<IItem> ItemsInInventory
        {
            get
            {
                List<IItem> result = new List<IItem>();
                if (_context == null || _context.Inventory == null || _context.Inventory.Items == null)
                    return result;

                List<ItemEntry> items = _context.Inventory.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    ItemEntry entry = items[i];
                    if (entry == null) continue;
                    result.Add(new HostItem(_context, _sno, entry.Gbid, entry.Quality, entry.Rank,
                                            entry.Name, 0f, 0f, entry.Column, entry.Row, false));
                }
                return result;
            }
        }

        private static readonly IUiElement HiddenUi = new UiElement("HiddenInventoryUi", delegate { return false; }, delegate { return RectangleF.Empty; });

        public IUiElement StashMainUiElement { get { return HiddenUi; } }
        public IUiElement GetStashPageUiElement(int index) { return HiddenUi; }
        public IUiElement GetStashTabUiElement(int index) { return HiddenUi; }
        public IUiElement InventoryMainUiElement { get { return HiddenUi; } }
        public IUiElement InventoryItemsUiElement { get { return HiddenUi; } }
        public IUiElement GetEquippedItemUiElement(ItemLocation location) { return HiddenUi; }
        public IUiElement FollowerMainUiElement { get { return HiddenUi; } }

        public RectangleF GetItemRect(IItem item)
        {
            if (item == null) return RectangleF.Empty;
            try
            {
                if (item.Location == ItemLocation.Floor)
                {
                    IScreenCoordinate screen = item.ScreenCoordinate;
                    if (screen == null) return RectangleF.Empty;
                    return new RectangleF(screen.X - CellSize * 0.5f,
                                          screen.Y - CellSize * 0.5f,
                                          CellSize, CellSize);
                }

                int x = item.InventoryX;
                int y = item.InventoryY;
                if (x < 0 || y < 0) return RectangleF.Empty;
                return new RectangleF(x * CellSize, y * CellSize, CellSize, CellSize);
            }
            catch
            {
                return RectangleF.Empty;
            }
        }

        public RectangleF GetRectInStash(int x, int y, int width, int height)
        {
            return GridRect(x, y, width, height);
        }

        public RectangleF GetRectInInventory(int x, int y, int width, int height)
        {
            return GridRect(x, y, width, height);
        }

        private static RectangleF GridRect(int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || width <= 0 || height <= 0) return RectangleF.Empty;
            return new RectangleF(x * CellSize, y * CellSize,
                                  width * CellSize, height * CellSize);
        }

        public ISnoItem GetSnoItem(uint sno)
        {
            if (_sno == null || sno == 0) return null;
            try { return _sno.SnoItem("item:" + sno.ToString(), sno); }
            catch { return null; }
        }

        public Rectangle InventoryLockArea
        {
            get
            {
                Func<Rectangle> provider = _lockAreaProvider != null ? _lockAreaProvider : LockAreaProvider;
                if (provider == null) return Rectangle.Empty;
                try { return provider(); }
                catch { return Rectangle.Empty; }
            }
        }
        public IUiElement GetHoveredItemMainUiElement() { return null; }
        public IUiElement GetHoveredItemTopUiElement() { return null; }

        // Cursor/UI hit testing is intentionally not guessed from the overlay:
        // the host has no game UI tree. Returning null is the API's safe value
        // when no item is known to be hovered.
        public IItem HoveredItem { get { return null; } }
    }
}
