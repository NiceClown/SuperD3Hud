using System;
using System.Collections.Generic;
using System.Diagnostics;
using SuperD3Hud.Game;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// The game controller. Every list is derived from the snapshot our own
    /// readers captured, and rebuilt only when the snapshot changes, so a
    /// plugin that walks the actor list every frame stays cheap.
    /// </summary>
    public class HostGameController : IGameController
    {
        private readonly HostContext _context;
        private readonly HostSnoController _sno;

        private int _cachedRevision = -1;
        private HostPlayer _me;
        private List<IMonster> _monsters;
        private List<IMonster> _aliveMonsters;
        private List<IItem> _items;
        private List<IActor> _actors;
        private List<IPlayer> _players;
        private readonly object _tickSync = new object();
        private readonly long _tickStarted;
        private long _lastTick;
        private bool _cacheWasInGame;

        private static readonly IQuest[] EmptyQuests = new IQuest[0];
        private static readonly IHeadStone[] EmptyHeadStones = new IHeadStone[0];
        private static readonly IMonsterPack[] EmptyMonsterPacks = new IMonsterPack[0];
        private static readonly IShrine[] EmptyShrines = new IShrine[0];
        private static readonly IPortal[] EmptyPortals = new IPortal[0];
        private static readonly IClickableActor[] EmptyClickables = new IClickableActor[0];
        private static readonly ISceneHint[] EmptySceneHints = new ISceneHint[0];
        private static readonly IBanner[] EmptyBanners = new IBanner[0];
        private static readonly IMarker[] EmptyMarkers = new IMarker[0];
        private static readonly IWaypoint[] EmptyWaypoints = new IWaypoint[0];
        private static readonly IMonster[] EmptyMonsters = new IMonster[0];

        public HostGameController(HostContext context, HostSnoController sno)
        {
            _context = context;
            _sno = sno;
            _tickStarted = Stopwatch.GetTimestamp();
            if (_context != null)
                _context.CurrentGameTickProvider = delegate { return CurrentGameTick; };
            _me = null;
            _monsters = new List<IMonster>();
            _aliveMonsters = new List<IMonster>();
            _items = new List<IItem>();
            _actors = new List<IActor>();
            _players = new List<IPlayer>();
            _cacheWasInGame = false;
        }

        /// <summary>Set by the host after construction.</summary>
        public IActorQueryController ActorQuery { get; set; }

        public GameDifficulty GameDifficulty { get { return GameDifficulty.unknown; } }

        public long CurrentRealTimeMilliseconds
        {
            get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }
        }

        public int CurrentGameTick
        {
            get
            {
                if (_context == null) return 0;
                if (_context.CurrentGameTick > 0) return _context.CurrentGameTick;
                if (_context.Skills != null && _context.Skills.GameTick > 0) return _context.Skills.GameTick;

                long elapsed = (long)((Stopwatch.GetTimestamp() - _tickStarted) * 60.0 /
                                      Stopwatch.Frequency);
                if (elapsed < 0) elapsed = 0;

                lock (_tickSync)
                {
                    if (elapsed < _lastTick) elapsed = _lastTick;
                    _lastTick = elapsed;
                    return elapsed > int.MaxValue ? int.MaxValue : (int)elapsed;
                }
            }
        }

        public bool IsLoading
        {
            get
            {
                return _context != null && _context.GameRunning
                    && (!_context.MapAvailable || !_context.PlayerKnown);
            }
        }
        public bool IsPaused { get { return false; } }
        public bool IsInGame { get { return _context != null && _context.GameRunning; } }
        public bool IsInTown { get { return false; } }
        public MapMode MapMode
        {
            get { return _context != null && _context.LargeMapOpen ? MapMode.Map : MapMode.Minimap; }
        }
        public SpecialArea SpecialArea
        {
            get
            {
                if (_context == null) return SpecialArea.None;
                switch (_context.SpecialArea)
                {
                    case 1: return SpecialArea.Rift;
                    case 2: return SpecialArea.GreaterRift;
                    case 3: return SpecialArea.ChallengeRift;
                    default:
                        if (_context.RiftPercentage > 0)
                            return SpecialArea.Rift;
                        return SpecialArea.None;
                }
            }
        }
        public string ServerIpAddress { get { return ""; } }

        public double CurrentQuestProgress { get { return _context != null ? _context.CurrentQuestProgress : 0d; } }
        public double MaxQuestProgress { get { return _context != null ? _context.MaxQuestProgress : 100d; } }
        public double RiftPercentage { get { return _context != null ? _context.RiftPercentage : 0d; } }

        public int CurrentTimedEventStartTick { get { return 0; } }
        public int CurrentTimedEventEndTick { get { return _context != null ? _context.TimedEventEndTick : 0; } }
        public int CurrentTimedEventEndTickMod { get { return 0; } }
        public double AverageLatency { get { return 0d; } }
        public double CurrentLatency { get { return 0d; } }
        public int CurrentAct
        {
            get
            {
                // HostContext has no act field. Act I is the least surprising
                // best effort while in game; outside a game the value is unknown.
                return IsInGame ? 0 : -1;
            }
        }

        public IPlayer Me { get { return Player; } }

        public IEnumerable<IQuest> Quests { get { return EmptyQuests; } }
        public IEnumerable<IQuest> Bounties { get { return EmptyQuests; } }

        public IEnumerable<IItem> Items { get { EnsureCache(); return _items; } }

        public IEnumerable<IPlayer> Players
        {
            get
            {
                EnsureCache();
                return _players;
            }
        }

        public IEnumerable<IHeadStone> HeadStones { get { return EmptyHeadStones; } }
        public int NumberOfPlayersInGame { get { EnsureCache(); return _players.Count; } }

        public IEnumerable<IActor> Actors { get { EnsureCache(); return _actors; } }
        public IEnumerable<IMonster> Monsters { get { EnsureCache(); return _monsters; } }
        public IEnumerable<IMonster> AliveMonsters { get { EnsureCache(); return _aliveMonsters; } }
        public IEnumerable<IMonsterPack> MonsterPacks { get { return EmptyMonsterPacks; } }

        public bool IsEliteOnScreen
        {
            get
            {
                EnsureCache();
                for (int i = 0; i < _monsters.Count; i++)
                    if (_monsters[i].IsElite && _monsters[i].IsAlive && IsOnScreen(_monsters[i]))
                        return true;
                return false;
            }
        }

        public bool IsEliteNearby
        {
            get
            {
                EnsureCache();
                for (int i = 0; i < _monsters.Count; i++)
                    if (_monsters[i].IsElite && _monsters[i].IsAlive
                        && _monsters[i].CentralXyDistanceToMe < 50d) return true;
                return false;
            }
        }

        public bool IsGoblinOnScreen { get { return false; } }

        public MonsterPriority MaxPriorityOnScreen
        {
            get
            {
                EnsureCache();
                MonsterPriority max = MonsterPriority.none;
                for (int i = 0; i < _monsters.Count; i++)
                {
                    if (!_monsters[i].IsAlive || !IsOnScreen(_monsters[i])) continue;
                    HostMonster hostMonster = _monsters[i] as HostMonster;
                    MonsterPriority priority = MonsterPriority.normal;
                    if (hostMonster != null && hostMonster.Marker != null && hostMonster.Marker.IsBoss)
                        priority = MonsterPriority.boss;
                    else if (_monsters[i].IsElite)
                        priority = MonsterPriority.high;
                    if (priority > max) max = priority;
                }
                return max;
            }
        }

        public IEnumerable<IShrine> Shrines { get { return EmptyShrines; } }
        public IEnumerable<IPortal> Portals { get { return EmptyPortals; } }
        public IEnumerable<IClickableActor> NormalChests { get { return EmptyClickables; } }
        public IEnumerable<IClickableActor> ResplendentChests { get { return EmptyClickables; } }
        public IEnumerable<IClickableActor> Doors { get { return EmptyClickables; } }
        public IEnumerable<ISceneHint> SceneHints { get { return EmptySceneHints; } }
        public IEnumerable<IBanner> Banners { get { return EmptyBanners; } }
        public IEnumerable<IMarker> Markers { get { return EmptyMarkers; } }
        public IEnumerable<IWaypoint> ActMapWaypoints { get { return EmptyWaypoints; } }
        public BountyAct ActMapCurrentAct { get { return BountyAct.None; } }

        public int InventorySpaceUsed
        {
            get
            {
                if (_context == null || _context.Inventory == null) return 0;
                return Math.Max(0, _context.Inventory.UsedSlots);
            }
        }

        public IMonster SelectedMonster1
        {
            get
            {
                if (_context == null || _context.Actors == null || _context.Actors.Monsters == null) return null;

                // TurboHUD means the monster under the mouse, whose health bar
                // the game shows at the top of the screen. The game does not
                // expose that here, so the monster closest to the cursor on
                // screen is used, and only when the cursor is actually on it.
                // Returning a monster nobody points at made plugins draw its
                // health permanently in an empty spot.
                HostWindow window = WorldCoordinate.CurrentWindow;
                if (window == null || window.Size.Height <= 0 || !window.CursorInsideGameWindow()) return null;

                float cx = window.CursorX;
                float cy = window.CursorY;
                float ui = window.Size.Height / 1080f;
                float pick = 55f * ui;
                float bestSq = pick * pick;
                IMonster candidate = null;

                foreach (IMonster m in AliveMonsters)
                {
                    if (m == null || m.FloorCoordinate == null) continue;
                    IWorldCoordinate fc = m.FloorCoordinate;
                    float sx, sy;
                    window.WorldToScreenCoordinate(fc.X, fc.Y, fc.Z, out sx, out sy);
                    // The body stands above the floor point; aim at its middle.
                    sy -= 35f * ui;
                    float dx = sx - cx;
                    float dy = sy - cy;
                    float d = dx * dx + dy * dy;
                    if (d < bestSq)
                    {
                        bestSq = d;
                        candidate = m;
                    }
                }
                return candidate;
            }
        }
        public IMonster SelectedMonster2 { get { return SelectedMonster1; } }
        public IActor SelectedActor { get { return null; } }
        public double ExperiencePerHourToday { get { return 0d; } }

        public IStatTracker CurrentAccountTotalOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentAccountYesterdayOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentAccountTodayOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroTotal { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroYesterday { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroToday { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroTotalOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroYesterdayOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }
        public IStatTracker CurrentHeroTodayOnCurrentDifficulty { get { return HostStubs.Get<IStatTracker>(); } }

        public IEnumerable<IMonster> Obstacles { get { return EmptyMonsters; } }

        // ------------------------------------------------------------------
        // Derived state
        // ------------------------------------------------------------------

        private HostPlayer _fallbackMe;

        private IPlayer Player
        {
            get
            {
                EnsureCache();
                if (_me != null) return _me;
                if (_fallbackMe == null) _fallbackMe = new HostPlayer(_context, _sno, 0f, 0f);
                return _fallbackMe;
            }
        }

        private bool IsOnScreen(IMonster monster)
        {
            if (monster == null || !monster.IsOnScreen) return false;

            HostMonster hostMonster = monster as HostMonster;
            if (hostMonster == null || hostMonster.Marker == null || _context == null)
                return true;

            int width = _context.WindowWidth;
            int height = _context.WindowHeight;
            if (width <= 0 || height <= 0) return true;

            // The same projection the plugins draw with, so a monster that is
            // outside the window is not drawn half off the screen.
            HostWindow window = WorldCoordinate.CurrentWindow;
            if (window == null) return true;

            float screenX, screenY;
            window.WorldToScreenCoordinate(hostMonster.Marker.X, hostMonster.Marker.Y, 0f,
                                          out screenX, out screenY);
            return screenX >= -64f && screenX <= width + 64f
                && screenY >= -64f && screenY <= height + 64f;
        }

        private void EnsureCache()
        {
            if (_context == null)
            {
                if (_cachedRevision == -1) return;
                _me = null;
                _monsters = new List<IMonster>();
                _aliveMonsters = new List<IMonster>();
                _items = new List<IItem>();
                _actors = new List<IActor>();
                _players = new List<IPlayer>();
                _cacheWasInGame = false;
                _cachedRevision = -1;
                return;
            }

            // A failed/disconnected service update may not increment Revision.
            // Do not expose the previous session's actors in that case.
            if (!_context.GameRunning)
            {
                if (_cachedRevision == _context.Revision && !_cacheWasInGame) return;
                _me = null;
                _monsters = new List<IMonster>();
                _aliveMonsters = new List<IMonster>();
                _items = new List<IItem>();
                _actors = new List<IActor>();
                _players = new List<IPlayer>();
                _cachedRevision = _context.Revision;
                _cacheWasInGame = false;
                return;
            }

            if (_cachedRevision == _context.Revision && _cacheWasInGame) return;
            _cachedRevision = _context.Revision;
            _cacheWasInGame = true;

            ActorSnapshot actors = _context.Actors;
            if (actors == null)
            {
                _me = null;
                _monsters = new List<IMonster>();
                _aliveMonsters = new List<IMonster>();
                _items = new List<IItem>();
                _actors = new List<IActor>();
                _players = new List<IPlayer>();
                _cacheWasInGame = true;
                return;
            }

            bool hasPlayer = actors.HasPlayer || _context.PlayerKnown;
            float playerX = actors.HasPlayer ? actors.PlayerX : _context.PlayerX;
            float playerY = actors.HasPlayer ? actors.PlayerY : _context.PlayerY;
            _me = hasPlayer ? new HostPlayer(_context, _sno, playerX, playerY) : null;
            _players = new List<IPlayer>();
            if (_me != null) _players.Add(_me);

            _monsters = new List<IMonster>();
            List<MonsterMarker> markers = actors.Monsters;
            if (markers != null)
            {
                for (int i = 0; i < markers.Count; i++)
                    if (markers[i] != null) _monsters.Add(new HostMonster(_context, _sno, markers[i]));
            }

            _aliveMonsters = new List<IMonster>();
            for (int i = 0; i < _monsters.Count; i++)
                if (((IMonster)_monsters[i]).IsAlive) _aliveMonsters.Add(_monsters[i]);

            _items = new List<IItem>();
            List<LootMarker> loot = actors.Loot;
            if (loot != null)
            {
                for (int i = 0; i < loot.Count; i++)
                {
                    LootMarker marker = loot[i];
                    if (marker == null) continue;
                    _items.Add(new HostItem(_context, _sno, marker.Gbid, marker.IsSet ? 9 : 9,
                                            marker.Rank, marker.Name, marker.X, marker.Y,
                                            0, 0, true));
                }
            }

            if (_context.Inventory != null && _context.Inventory.Items != null)
            {
                List<ItemEntry> entries = _context.Inventory.Items;
                for (int i = 0; i < entries.Count; i++)
                {
                    ItemEntry entry = entries[i];
                    if (entry == null) continue;
                    _items.Add(new HostItem(_context, _sno, entry.Gbid, entry.Quality, entry.Rank,
                                            entry.Name, 0f, 0f, entry.Column, entry.Row, false));
                }
            }

            _actors = new List<IActor>();
            if (_me != null) _actors.Add(_me);
            for (int i = 0; i < _monsters.Count; i++) _actors.Add(_monsters[i]);
            for (int i = 0; i < _items.Count; i++) _actors.Add(_items[i]);
        }
    }
}
