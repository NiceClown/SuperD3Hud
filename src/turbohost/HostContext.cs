using System;
using System.Collections.Generic;
using System.Drawing;
using SuperD3Hud.Game;
using Turbo.Plugins;

namespace SuperD3Hud.TurboHost
{
    /// <summary>
    /// Shared state of the TurboHUD compatibility host.
    ///
    /// Every controller reads from this object, so the data is read from the
    /// game once per frame and shared by all plugins. The host never writes to
    /// the game: it only mirrors what our own readers captured.
    /// </summary>
    public class HostContext
    {
        public HostContext()
        {
            Time = new Watch();
            StartedUtc = DateTime.UtcNow;
            Actors = new ActorSnapshot();
            Buffs = new BuffSnapshot();
            Skills = new SkillSnapshot();
            Inventory = new InventorySnapshot();
            Cooldowns = new Dictionary<int, double>();
            CooldownDurations = new Dictionary<int, double>();
            MaxQuestProgress = 100d;
        }

        /// <summary>Snapshot of actors and loot.</summary>
        public ActorSnapshot Actors
        {
            get { return _actors; }
            set { SetActors(value); }
        }

        private ActorSnapshot _actors = new ActorSnapshot();

        /// <summary>
        /// Takes over a new actor snapshot. A new object means a new read, so
        /// the positions of the previous read become the reference for the
        /// movement between the two reads.
        /// </summary>
        public void SetActors(ActorSnapshot snapshot)
        {
            ActorSnapshot previous = _actors;
            _actors = snapshot ?? new ActorSnapshot();
            RememberMonsterPositions(_actors, previous);
        }

        // ------------------------------------------------------------------
        // Monster positions between two reads
        // ------------------------------------------------------------------

        // The actors are read a few times per second. A plugin that draws a
        // circle under a monster would jump with every read, so the movement
        // of the last step is continued until the next read arrives.
        private readonly Dictionary<int, PointF> _monsterCur = new Dictionary<int, PointF>();
        private readonly Dictionary<int, PointF> _monsterPrev = new Dictionary<int, PointF>();
        private DateTime _monsterCurAt = DateTime.MinValue;
        private DateTime _monsterPrevAt = DateTime.MinValue;

        /// <summary>
        /// The position of a monster for the current frame, continued from the
        /// last read. Falls back to the read position when nothing is known.
        /// </summary>
        public void MonsterPosition(MonsterMarker marker, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (marker == null) return;

            PointF cur, prev;
            bool known = _monsterCur.TryGetValue(marker.Id, out cur);
            if (!known)
            {
                x = marker.X;
                y = marker.Y;
                return;
            }

            x = cur.X;
            y = cur.Y;
            if (!_monsterPrev.TryGetValue(marker.Id, out prev)) return;
            if (_monsterCurAt == DateTime.MinValue || _monsterPrevAt == DateTime.MinValue) return;

            double span = (_monsterCurAt - _monsterPrevAt).TotalMilliseconds;
            if (span <= 1) return;

            // The next read is not here yet, so the last step is repeated
            // once; a monster that stopped does not run away.
            double t = (DateTime.UtcNow - _monsterCurAt).TotalMilliseconds / span;
            if (t <= 0) return;
            if (t > 1) t = 1;

            x = cur.X + (cur.X - prev.X) * (float)t;
            y = cur.Y + (cur.Y - prev.Y) * (float)t;
        }

        private void RememberMonsterPositions(ActorSnapshot snapshot, ActorSnapshot previous)
        {
            if (snapshot == null) return;

            // A new snapshot means a new read of the actors; the positions of
            // the previous read become the reference for the movement.
            bool fresh = !ReferenceEquals(snapshot, previous);
            if (fresh)
            {
                _monsterPrev.Clear();
                foreach (KeyValuePair<int, PointF> kv in _monsterCur) _monsterPrev[kv.Key] = kv.Value;
                _monsterCur.Clear();
                if (snapshot.Monsters != null)
                {
                    for (int i = 0; i < snapshot.Monsters.Count; i++)
                    {
                        MonsterMarker monster = snapshot.Monsters[i];
                        if (monster == null) continue;
                        _monsterCur[monster.Id] = new PointF(monster.X, monster.Y);
                    }
                }
                _monsterPrevAt = _monsterCurAt;
                _monsterCurAt = DateTime.UtcNow;
            }
        }
        /// <summary>Snapshot of the local player's buffs.</summary>
        public BuffSnapshot Buffs { get; set; }
        /// <summary>Snapshot of skills and health.</summary>
        public SkillSnapshot Skills { get; set; }
        /// <summary>Snapshot of the backpack.</summary>
        public InventorySnapshot Inventory { get; set; }

        /// <summary>True when the game client is running.</summary>
        public bool GameRunning { get; set; }
        /// <summary>True when our map reader is attached and the build matches.</summary>
        public bool MapAvailable { get; set; }

        /// <summary>Position of the local player, if known.</summary>
        public bool PlayerKnown { get; set; }
        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public int WorldId { get; set; }
        public int SceneCount { get; set; }
        public int CellCount { get; set; }

        /// <summary>Client area of the game window.</summary>
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public bool WindowForeground { get; set; }
        /// <summary>True when the large full-screen map is currently open.</summary>
        public bool LargeMapOpen { get; set; }
        /// <summary>Pixels per world unit used by the linear projection.</summary>
        public float WorldToScreenScale { get; set; }

        /// <summary>Last hint text a plugin asked the host to show.</summary>
        public string Hint { get; set; }

        /// <summary>Increments on every state update; used to cache derived lists.</summary>
        public int Revision { get; set; }

        /// <summary>Canvas the plugins paint on.</summary>
        public HostCanvas Canvas { get; set; }

        /// <summary>Uptime watch.</summary>
        public Watch Time { get; set; }
        public DateTime StartedUtc { get; set; }

        /// <summary>Cooldowns per skill slot, seconds remaining.</summary>
        public Dictionary<int, double> Cooldowns { get; set; }
        public Dictionary<int, double> CooldownDurations { get; set; }
        public Dictionary<int, int> FinishTicks { get; set; }
        public int CurrentGameTick { get; set; }

        /// <summary>Game tick provider (60 ticks/second).</summary>
        public Func<int> CurrentGameTickProvider { get; set; }

        /// <summary>0=None, 1=Rift, 2=GreaterRift, 3=ChallengeRift</summary>
        public int SpecialArea { get; set; }
        public double RiftPercentage { get; set; }
        public double CurrentQuestProgress { get; set; }
        public double MaxQuestProgress { get; set; }
        public int TimedEventEndTick { get; set; }

        /// <summary>Copies the current state of a game service.</summary>
        public void Update(GameService service, bool windowForeground,
                           int windowX, int windowY, int windowWidth, int windowHeight,
                           bool largeMapOpen = false)
        {
            WindowForeground = windowForeground;
            WindowX = windowX;
            WindowY = windowY;
            WindowWidth = windowWidth;
            WindowHeight = windowHeight;
            LargeMapOpen = largeMapOpen;
            WorldToScreenScale = windowHeight <= 0 ? 0f : windowHeight / 90f;

            if (service == null)
            {
                GameRunning = false;
                MapAvailable = false;
                PlayerKnown = false;
                return;
            }

            GameRunning = service.GameRunning;
            MapAvailable = service.IsAttached;

            ActorSnapshot actors;
            BuffSnapshot buffs;
            SkillSnapshot skills;
            InventorySnapshot inventory;
            service.GetState(out actors, out buffs, out skills, out inventory);

            SetActors(actors);
            Buffs = buffs ?? new BuffSnapshot();
            Skills = skills ?? new SkillSnapshot();
            Inventory = inventory ?? new InventorySnapshot();

            PlayerKnown = Actors.HasPlayer;
            PlayerX = Actors.PlayerX;
            PlayerY = Actors.PlayerY;
            WorldId = Actors.WorldId;
            SpecialArea = Actors.SpecialArea;
            RiftPercentage = Actors.RiftPercentage;
            CurrentQuestProgress = Actors.CurrentQuestProgress;
            MaxQuestProgress = Actors.MaxQuestProgress > 0 ? Actors.MaxQuestProgress : 100d;
            TimedEventEndTick = Actors.TimedEventEndTick;

            Cooldowns = service.CooldownRemaining();
            CooldownDurations = service.CooldownDurations();
            FinishTicks = service.CooldownFinishTicks();
            CurrentGameTick = (skills != null && skills.GameTick > 0) ? skills.GameTick : (service != null ? service.GameTick : 0);
            Revision++;
        }

        /// <summary>Milliseconds since the host started.</summary>
        public long UptimeMilliseconds
        {
            get { return Time == null ? 0 : Time.ElapsedMilliseconds; }
        }

        /// <summary>Name of an item, looked up in the live loot and inventory.</summary>
        public string ItemName(uint gbid)
        {
            if (gbid == 0) return "";

            List<LootMarker> loot = Actors == null ? null : Actors.Loot;
            if (loot != null)
            {
                for (int i = 0; i < loot.Count; i++)
                {
                    if ((uint)loot[i].Gbid == gbid && !string.IsNullOrEmpty(loot[i].Name))
                        return loot[i].Name;
                }
            }

            List<ItemEntry> items = Inventory == null ? null : Inventory.Items;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if ((uint)items[i].Gbid == gbid && !string.IsNullOrEmpty(items[i].Name))
                        return items[i].Name;
                }
            }

            return "";
        }

        /// <summary>Name of a power, looked up in the live buff snapshot.</summary>
        public string PowerName(uint sno)
        {
            if (sno == 0) return "";

            List<BuffEntry> buffs = Buffs == null ? null : Buffs.Buffs;
            if (buffs != null)
            {
                for (int i = 0; i < buffs.Count; i++)
                {
                    if ((uint)buffs[i].PowerSno == sno && !string.IsNullOrEmpty(buffs[i].Name))
                        return buffs[i].Name;
                }
            }

            return "";
        }
    }
}
