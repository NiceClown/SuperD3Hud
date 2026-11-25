using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Reads monsters and ground loot from the actor container.
    ///
    /// The module never throws: one corrupted or racing frame is turned into a
    /// snapshot carrying an error status, so the HUD keeps drawing the last
    /// good frame instead of crashing. Every list is capped, and monsters are
    /// ordered by importance (bosses and elites first, then by distance to the
    /// player), so a crowded rift cannot flood the overlay.
    /// </summary>
    public sealed class ActorModule : IActorModule
    {
        private const int MaxMonsters = 600;
        private const int MaxLoot = 200;
        /// <summary>Item names are expensive to resolve, so only the first ones get one.</summary>
        private const int MaxLootNames = 40;

        /// <summary>Item quality of a legendary or set item.</summary>
        private const uint LegendaryQuality = 9u;
        /// <summary>Ancient rank above this value is treated as corrupted data.</summary>
        private const uint MaxAncientRank = 2u;

        /// <summary>
        /// Rift guardian SNOs. A monster with one of these ids is a boss even
        /// when the game did not flag its rarity as boss.
        /// </summary>
        private static readonly HashSet<int> GuardianSnos = CreateGuardianSnos();
        private static readonly Dictionary<int, uint> PowerToAffix = CreatePowerToAffix();
        private static readonly Dictionary<uint, string> AffixRussianNames = CreateAffixRussianNames();
        private static readonly uint[] DeterministicAffixPool = new uint[]
        {
            2625377780u, // Arcane
            4172983340u, // Desecrator
            2542537664u, // Electrified
            3775118u,    // Fast
            4131130388u, // Frozen
            3830498332u, // Juggernaut
            106438735u,  // Molten
            106654229u,  // Mortar
            3569101591u, // Shielding
            458872904u,  // Vortex
            481181063u,  // Waller
            1156956365u, // Wormhole
            2961013602u, // Plagued
            394214687u,  // Illusionist
            1886876669u, // FrozenPulse
            4244410831u, // Thunderstorm
            3855260060u, // FireChains
            4267280439u  // Jailer
        };

        private ActorSnapshot _snapshot;
        private int _lastTrackedWorldId = -1;
        private int _specialArea = 0;
        private double _riftProgress = 0d;
        private int _lastMonsterCount = 0;
        private readonly DamageTracker _damage = new DamageTracker();
        // Where the interactive objects were the last time they were read, to
        // tell a standing pylon from a walking companion.
        private readonly Dictionary<int, MapPoint> _lastInteractive = new Dictionary<int, MapPoint>();
        // The interactive objects that were already there, and the one that
        // appeared after the guardian of the rift died: that is the exit.
        private readonly HashSet<int> _knownInteractive = new HashSet<int>();
        private int _exitActorId = 0;
        private bool _guardianSeen;
        private bool _guardianGone;

        // Item names and set membership come from the game's balance table.
        // The instance is created lazily, because it needs a live reader.
        private ItemDefinitions _definitions;
        private IGameMemoryReader _definitionsMemory;
        private IntPtr _definitionsBase;

        /// <summary>Last successfully captured snapshot; never null.</summary>
        public ActorSnapshot Snapshot
        {
            get
            {
                if (_snapshot != null) return _snapshot;

                ActorSnapshot empty = new ActorSnapshot();
                empty.Status = "Мир не определён";
                empty.CapturedUtc = DateTime.UtcNow;
                return empty;
            }
        }

        /// <summary>
        /// Reads one frame of monsters and, optionally, ground loot.
        /// Never throws; on failure returns the previous snapshot with an
        /// error status so the HUD can keep its last good frame.
        /// </summary>
        public ActorSnapshot Poll(GameReader reader, int worldId, bool includeLoot)
        {
            try
            {
                if (reader == null)
                    throw new ArgumentNullException("reader");

                // Without a world id the actor list cannot be filtered, and an
                // unfiltered list would mix monsters from every loaded level.
                if (worldId <= 0)
                {
                    ActorSnapshot unknown = CreateEmpty(worldId);
                    unknown.Status = "Мир не определён";
                    _snapshot = unknown;
                    return unknown;
                }

                List<ActorRecord> actors = reader.ReadActors();

                ActorRecord player;
                bool hasPlayer = reader.TryReadLocalPlayer(out player);
                float playerX = hasPlayer ? player.X : 0f;
                float playerY = hasPlayer ? player.Y : 0f;
                bool hasPlayerPosition = hasPlayer && ValidPosition(playerX, playerY);

                List<MonsterCandidate> candidates = new List<MonsterCandidate>();
                for (int i = 0; i < actors.Count; i++)
                {
                    ActorRecord record = actors[i];
                    if (!record.IsMonster) continue;
                    if (record.WorldId != worldId) continue;
                    if (!ValidPosition(record.X, record.Y)) continue;
                    if (reader.IsDeleted(record.GroupId)) continue;

                    float? health = reader.ReadHealth(record.GroupId);
                    if (!health.HasValue) continue;
                    if (health.Value <= 0f) continue;

                    bool boss = record.Rarity == (int)MonsterRarity.Boss
                                || GuardianSnos.Contains(record.Sno);
                    bool elite = IsEliteRarity(record.Rarity);

                    MonsterMarker marker = new MonsterMarker();
                    marker.Id = record.Id;
                    marker.Sno = record.Sno;
                    marker.Rarity = record.Rarity;
                    marker.X = record.X;
                    marker.Y = record.Y;
                    marker.Health = health.Value;
                    marker.IsElite = elite;
                    marker.IsBoss = boss;
                    if (boss) marker.Label = "Босс";
                    else if (elite) marker.Label = "Элита";
                    else marker.Label = "";

                    if (elite || boss)
                    {
                        AssignAffixes(reader, record, marker);
                    }

                    MonsterCandidate candidate = new MonsterCandidate();
                    candidate.Marker = marker;
                    candidate.Priority = (boss || elite) ? 0 : 1;
                    candidate.Distance = hasPlayerPosition
                        ? DistanceSquared(record.X, record.Y, playerX, playerY)
                        : 0d;
                    candidates.Add(candidate);
                }

                candidates.Sort(CompareMonsters);

                int shownMonsters = Math.Min(candidates.Count, MaxMonsters);
                List<MonsterMarker> monsters = new List<MonsterMarker>(shownMonsters);
                int elites = 0;
                for (int i = 0; i < shownMonsters; i++)
                {
                    MonsterMarker marker = candidates[i].Marker;
                    monsters.Add(marker);
                    if (marker.IsElite) elites++;
                }

                // The damage meter needs every monster of the area, not only
                // the ones that are shown on the map.
                List<MonsterMarker> allMonsters = new List<MonsterMarker>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++) allMonsters.Add(candidates[i].Marker);

                List<LootMarker> loot = new List<LootMarker>();
                if (includeLoot)
                    ReadLoot(reader, actors, worldId, loot);

                // The guardian of a rift is a boss with a special id. It is
                // looked for before the objects of the level are collected:
                // the exit of the rift appears exactly when it dies.
                int currentSpecialArea = 0;
                bool hasGuardian = false;
                for (int i = 0; i < actors.Count; i++)
                {
                    ActorRecord r = actors[i];
                    if (r.WorldId != worldId) continue;
                    if (GuardianSnos.Contains(r.Sno))
                    {
                        hasGuardian = true;
                        currentSpecialArea = 2; // Greater Rift
                    }
                }

                if (worldId != _lastTrackedWorldId)
                {
                    _lastTrackedWorldId = worldId;
                    _riftProgress = 0d;
                    _lastMonsterCount = candidates.Count;
                    _specialArea = currentSpecialArea;
                    _guardianSeen = false;
                    _guardianGone = false;
                    _exitActorId = 0;
                    _knownInteractive.Clear();
                }

                if (hasGuardian)
                {
                    _guardianSeen = true;
                    _guardianGone = false;
                }
                else if (_guardianSeen)
                {
                    _guardianGone = true;
                }

                // The other players of the group: every hero in the actor
                // container that is not the local one.
                List<PartyMember> party = new List<PartyMember>();
                for (int i = 0; i < actors.Count; i++)
                {
                    ActorRecord record = actors[i];
                    if (!record.IsPlayer) continue;
                    if (record.WorldId != worldId) continue;
                    if (hasPlayer && record.Id == player.Id) continue;
                    if (!ValidPosition(record.X, record.Y)) continue;

                    PartyMember member = new PartyMember();
                    member.Id = record.Id;
                    member.X = record.X;
                    member.Y = record.Y;
                    member.Name = "Союзник " + (party.Count + 1);
                    party.Add(member);
                }

                // The objects that can be used: the exit of a rift, the
                // pylons of a greater rift, the waypoints. The game keeps them
                // in the actor container next to the monsters and the loot.
                List<InteractiveMarker> interactive = new List<InteractiveMarker>();
                var seenNow = new HashSet<int>();
                for (int i = 0; i < actors.Count; i++)
                {
                    ActorRecord record = actors[i];
                    if (record.WorldId != worldId) continue;
                    if (!ValidPosition(record.X, record.Y)) continue;

                    bool isExit = IsExitSno(record.Sno);
                    if (!isExit)
                    {
                        if (!IsInteractiveType(record.BalanceType)) continue;
                        // A follower of the character (a pet) walks with him and
                        // is not a point of interest.
                        if (hasPlayerPosition &&
                            DistanceSquared(record.X, record.Y, playerX, playerY) < 144d) continue;
                        // Something that walks around on its own is no pylon.
                        MapPoint previous;
                        if (_lastInteractive.TryGetValue(record.Id, out previous))
                        {
                            float ddx = (float)(previous.X - record.X);
                            float ddy = (float)(previous.Y - record.Y);
                            if (ddx * ddx + ddy * ddy > 4f) continue;
                        }
                    }

                    // The exit of a rift appears when the guardian dies. The
                    // game does not name it in a way we could look up, so it is
                    // recognised by that: an object that shows up in a rift
                    // after its guardian was seen and is gone now.
                    bool isNew = !_knownInteractive.Contains(record.Id);
                    if (!isExit && _guardianGone && isNew && _exitActorId == 0)
                    {
                        _exitActorId = record.Id;
                        isExit = true;
                    }
                    seenNow.Add(record.Id);
                    InteractiveMarker marker = new InteractiveMarker();
                    marker.Id = record.Id;
                    marker.Sno = record.Sno;
                    marker.X = record.X;
                    marker.Y = record.Y;
                    if (isExit)
                    {
                        marker.Kind = "exit";
                        marker.Name = "Выход";
                    }
                    else
                    {
                        marker.Kind = "pylon";
                        marker.Name = "Пилон";
                    }
                    interactive.Add(marker);
                }
                _knownInteractive.Clear();
                foreach (int id in seenNow) _knownInteractive.Add(id);
                _lastInteractive.Clear();
                for (int i = 0; i < interactive.Count; i++)
                    _lastInteractive[interactive[i].Id] = new MapPoint(interactive[i].X, interactive[i].Y);

                // A rift has nothing but the exit: the pylons of a greater
                // rift are five, so a single object can only be the way out.
                if (_exitActorId == 0 && _guardianGone && interactive.Count == 1)
                {
                    interactive[0].Kind = "exit";
                    interactive[0].Name = "Выход";
                    _exitActorId = interactive[0].Id;
                }

                if (hasGuardian)
                {
                    _riftProgress = 100.0;
                    _specialArea = 2;
                }
                else if (_specialArea > 0)
                {
                    if (_lastMonsterCount > 0 && candidates.Count < _lastMonsterCount)
                    {
                        int killed = _lastMonsterCount - candidates.Count;
                        _riftProgress = Math.Min(99.9, _riftProgress + killed * 0.75);
                    }
                    _lastMonsterCount = candidates.Count;
                }

                ActorSnapshot snapshot = new ActorSnapshot();
                snapshot.WorldId = worldId;
                snapshot.HasPlayer = hasPlayer;
                snapshot.PlayerX = hasPlayer ? playerX : 0f;
                snapshot.PlayerY = hasPlayer ? playerY : 0f;
                snapshot.Monsters = monsters;
                snapshot.Loot = loot;
                snapshot.PartyMembers = party;
                snapshot.Interactive = interactive;

                // The damage meter watches the health of the monsters.
                _damage.Update(allMonsters, worldId, _specialArea > 0,
                               hasPlayerPosition ? playerX : 0f,
                               hasPlayerPosition ? playerY : 0f, party);
                snapshot.DamageActive = _damage.Active;
                snapshot.DamageTotal = _damage.Total;
                snapshot.DamageDps = _damage.Dps;
                snapshot.DamageEntries = new List<DamageEntry>(_damage.Entries);
                snapshot.SpecialArea = _specialArea;
                snapshot.RiftPercentage = _riftProgress;
                snapshot.CurrentQuestProgress = _riftProgress;
                snapshot.MaxQuestProgress = 100.0;
                snapshot.Status = BuildStatus(monsters.Count, elites, loot.Count, includeLoot);
                if (_specialArea > 0)
                {
                    string areaName = _specialArea == 2 ? "ВП" : "Нефалемский портал";
                    snapshot.Status = string.Format("{0}: {1:F1}%, ", areaName, _riftProgress) + snapshot.Status;
                }
                snapshot.CapturedUtc = DateTime.UtcNow;
                _snapshot = snapshot;
                return snapshot;
            }
            catch (Exception ex)
            {
                ActorSnapshot fallback = _snapshot != null
                    ? Clone(_snapshot)
                    : CreateEmpty(worldId);
                fallback.Status = "Ошибка чтения: " + ex.Message;
                return fallback;
            }
        }

        /// <summary>Forgets the last snapshot.</summary>
        public void Reset()
        {
            _snapshot = null;
            _damage.Reset();
        }

        // ------------------------------------------------------------------
        // Loot
        // ------------------------------------------------------------------

        private void ReadLoot(GameReader reader, List<ActorRecord> actors,
                                     int worldId, List<LootMarker> loot)
        {
            for (int i = 0; i < actors.Count; i++)
            {
                if (loot.Count >= MaxLoot) return;

                ActorRecord record = actors[i];
                if (!record.IsItem) continue;
                if (record.WorldId != worldId) continue;
                // Owner -1 means the item lies on the ground, not in a bag.
                if (record.OwnerId != -1) continue;
                if (!ValidPosition(record.X, record.Y)) continue;

                uint? quality = reader.ReadAttribute(record.GroupId, AttributeKeys.ItemQuality);
                if (!quality.HasValue) continue;
                if (quality.Value != LegendaryQuality) continue;

                // A missing rank is a normal legendary, not corrupted data.
                uint? ancient = reader.ReadAttribute(record.GroupId, AttributeKeys.AncientRank);
                int rank = 0;
                if (ancient.HasValue)
                {
                    if (ancient.Value > MaxAncientRank) continue;
                    rank = (int)ancient.Value;
                }

                LootMarker marker = new LootMarker();
                marker.Id = record.Id;
                marker.Gbid = record.Gbid;
                marker.X = record.X;
                marker.Y = record.Y;
                marker.Rank = rank;

                // Name and set membership come from the game's item balance
                // table. Both are best-effort: an unresolved item still shows
                // up, only without a name.
                ItemDefinitions definitions = GetDefinitions(reader);
                if (definitions != null && loot.Count < MaxLootNames)
                {
                    try { marker.Name = definitions.Name(record.Gbid); }
                    catch { marker.Name = ""; }

                    bool? isSet = null;
                    try { isSet = definitions.IsSet(record.Gbid); }
                    catch { }
                    marker.IsSet = isSet == true;
                }

                // The balance table sometimes yields the internal code name
                // ("Unique_Mace_2H_001_x1"). The public plugin API carries the
                // readable name of the same GBID, so it wins whenever the
                // memory name looks like a code or is missing.
                if (string.IsNullOrEmpty(marker.Name) || marker.Name.IndexOf('_') >= 0)
                {
                    string readable = SuperD3Hud.TurboHost.HostNames.ItemName((uint)record.Gbid);
                    if (!string.IsNullOrEmpty(readable) && readable.IndexOf('_') < 0)
                        marker.Name = readable;
                }

                if (rank == 2) marker.Label = marker.IsSet ? "Первобытный комплект" : "Первобытный";
                else if (rank == 1) marker.Label = marker.IsSet ? "Древний комплект" : "Древний";
                else marker.Label = marker.IsSet ? "Комплект" : "Легендарка";
                loot.Add(marker);
            }
        }

        /// <summary>
        /// Returns the item definition reader for the current game session,
        /// recreating it when the process or module base changed.
        /// </summary>
        private ItemDefinitions GetDefinitions(GameReader reader)
        {
            if (reader == null) return null;
            if (_definitions != null &&
                ReferenceEquals(_definitionsMemory, reader.Memory) &&
                _definitionsBase == reader.BaseAddress)
                return _definitions;

            _definitions = new ItemDefinitions(reader.Memory, reader.BaseAddress);
            _definitionsMemory = reader.Memory;
            _definitionsBase = reader.BaseAddress;
            return _definitions;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static bool IsEliteRarity(int rarity)
        {
            // RareMinion (3) is a servant of a rare elite, not an elite itself.
            return rarity == (int)MonsterRarity.Champion
                || rarity == (int)MonsterRarity.RareElite
                || rarity == (int)MonsterRarity.Unique
                || rarity == (int)MonsterRarity.Boss;
        }

        private static bool ValidPosition(float x, float y)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) return false;
            if (float.IsNaN(y) || float.IsInfinity(y)) return false;
            if (Math.Abs(x) > 1000000f || Math.Abs(y) > 1000000f) return false;
            return true;
        }

        /// <summary>
        /// The balance types the game uses for the objects that are neither
        /// monsters, nor items, nor the character.
        ///
        /// The types that were seen on the client:
        /// <list type="bullet">
        /// <item><description>-1 — a monster,</description></item>
        /// <item><description>2 — an item lying on the floor,</description></item>
        /// <item><description>4 — a companion that follows the hero,</description></item>
        /// <item><description>7 — the hero itself,</description></item>
        /// </list>
        /// Everything else (1, 3, 5, 6) is a prop of the level: the portal of
        /// a rift, the pylons of a greater rift, doors and shrines. Only those
        /// are worth putting on the map; the items are marked separately and
        /// the companion walks along with the hero.
        /// </summary>
        private static bool IsInteractiveType(int balanceType)
        {
            // Type 0 is the portal of a rift: the game does not put it into
            // any other category.
            return balanceType == 0 || balanceType == 1 || balanceType == 3
                || balanceType == 5 || balanceType == 6;
        }

        /// <summary>
        /// True for the portal a rift is entered and left through. The numbers
        /// are the identifiers of the game that were seen on the client.
        /// </summary>
        private static bool IsExitSno(int sno)
        {
            uint value = (uint)sno;
            return value == 2177805394u || value == 450956006u || value == 385623u || value == 385624u;
        }

        private static double DistanceSquared(float ax, float ay, float bx, float by)
        {
            double dx = (double)ax - (double)bx;
            double dy = (double)ay - (double)by;
            return dx * dx + dy * dy;
        }

        private static int CompareMonsters(MonsterCandidate a, MonsterCandidate b)
        {
            if (a.Priority != b.Priority)
                return a.Priority < b.Priority ? -1 : 1;
            if (a.Distance != b.Distance)
                return a.Distance < b.Distance ? -1 : 1;
            // Deterministic tie-break for an unstable sort.
            if (a.Marker.Id != b.Marker.Id)
                return a.Marker.Id < b.Marker.Id ? -1 : 1;
            return 0;
        }

        private static string BuildStatus(int monsters, int elites, int loot, bool includeLoot)
        {
            string text = string.Format("Монстров: {0} (элита {1})", monsters, elites);
            if (includeLoot)
                text += string.Format(", добыча: {0}", loot);
            return text;
        }

        private static ActorSnapshot CreateEmpty(int worldId)
        {
            ActorSnapshot snapshot = new ActorSnapshot();
            snapshot.WorldId = worldId;
            snapshot.Monsters = new List<MonsterMarker>();
            snapshot.Loot = new List<LootMarker>();
            snapshot.Status = "";
            snapshot.SpecialArea = 0;
            snapshot.RiftPercentage = 0d;
            snapshot.CurrentQuestProgress = 0d;
            snapshot.MaxQuestProgress = 100d;
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        private static ActorSnapshot Clone(ActorSnapshot source)
        {
            ActorSnapshot copy = new ActorSnapshot();
            copy.WorldId = source.WorldId;
            copy.HasPlayer = source.HasPlayer;
            copy.PlayerX = source.PlayerX;
            copy.PlayerY = source.PlayerY;
            copy.Monsters = new List<MonsterMarker>(source.Monsters);
            copy.Loot = new List<LootMarker>(source.Loot);
            copy.PartyMembers = new List<PartyMember>(source.PartyMembers);
            copy.Interactive = new List<InteractiveMarker>(source.Interactive);
            copy.Status = source.Status;
            copy.SpecialArea = source.SpecialArea;
            copy.RiftPercentage = source.RiftPercentage;
            copy.CurrentQuestProgress = source.CurrentQuestProgress;
            copy.MaxQuestProgress = source.MaxQuestProgress;
            copy.TimedEventEndTick = source.TimedEventEndTick;
            copy.CapturedUtc = source.CapturedUtc;
            return copy;
        }

        private static void AssignAffixes(GameReader reader, ActorRecord record, MonsterMarker marker)
        {
            if (marker.Affixes == null) marker.Affixes = new List<uint>();
            if (marker.AffixNames == null) marker.AffixNames = new List<string>();

            HashSet<uint> seen = new HashSet<uint>();

            // 1. Try reading live attributes
            if (reader != null && record.GroupId != -1 && record.GroupId != 0)
            {
                try
                {
                    List<KeyValuePair<uint, uint>> attrs = reader.ReadAllAttributes(record.GroupId);
                    for (int i = 0; i < attrs.Count; i++)
                    {
                        int powerSno = (int)(attrs[i].Key >> 12);
                        uint affix;
                        if (PowerToAffix.TryGetValue(powerSno, out affix))
                        {
                            if (seen.Add(affix))
                            {
                                marker.Affixes.Add(affix);
                                string nameRu;
                                if (!AffixRussianNames.TryGetValue(affix, out nameRu))
                                    nameRu = "Элита";
                                marker.AffixNames.Add(nameRu);
                            }
                        }
                    }
                }
                catch { }
            }

        }

        private static Dictionary<int, uint> CreatePowerToAffix()
        {
            Dictionary<int, uint> map = new Dictionary<int, uint>();
            // Arcane (2625377780u)
            map[214594] = 2625377780u; map[214791] = 2625377780u; map[221130] = 2625377780u;
            map[221219] = 2625377780u; map[384426] = 2625377780u;
            // Avenger (1165197192u)
            map[226292] = 1165197192u; map[226289] = 1165197192u;
            // Desecrator (4172983340u)
            map[70874] = 4172983340u; map[156106] = 4172983340u; map[221131] = 4172983340u; map[156105] = 4172983340u;
            // Electrified (2542537664u)
            map[81420] = 2542537664u; map[109899] = 2542537664u; map[365083] = 2542537664u;
            // ExtraHealth (2782485594u)
            map[70650] = 2782485594u;
            // Fast (3775118u)
            map[70849] = 3775118u;
            // Frozen (4131130388u)
            map[90144] = 4131130388u; map[231149] = 4131130388u; map[231157] = 4131130388u;
            // HealthLink (1799201764u)
            map[71239] = 1799201764u;
            // Illusionist (394214687u)
            map[71108] = 394214687u; map[264185] = 394214687u;
            // Jailer (4267280439u)
            map[222743] = 4267280439u; map[222744] = 4267280439u; map[222745] = 4267280439u;
            // Juggernaut (3830498332u)
            map[455436] = 3830498332u;
            // Knockback (2206426855u)
            map[70655] = 2206426855u;
            // MissileDampening (2882216553u)
            map[91028] = 2882216553u; map[376860] = 2882216553u;
            // Molten (106438735u)
            map[90314] = 106438735u; map[109898] = 106438735u;
            // Mortar (106654229u)
            map[215756] = 106654229u; map[215757] = 106654229u; map[447584] = 106654229u;
            // Necrotic (3527404023u)
            map[488914] = 3527404023u; map[489153] = 3527404023u;
            // Nightmarish (3049048382u)
            map[247258] = 3049048382u;
            // Plagued (2961013602u)
            map[90566] = 2961013602u; map[231115] = 2961013602u;
            // Reflect (2920375063u)
            map[230877] = 2920375063u; map[285770] = 2920375063u;
            // Shielding (3569101591u)
            map[226437] = 3569101591u; map[226438] = 3569101591u;
            // Teleporter (3787260902u)
            map[155958] = 3787260902u; map[155959] = 3787260902u;
            // Thunderstorm (4244410831u)
            map[336177] = 4244410831u; map[336178] = 4244410831u; map[336179] = 4244410831u;
            // Vampiric (395423867u)
            map[70309] = 395423867u;
            // Vortex (458872904u)
            map[120306] = 458872904u; map[221132] = 458872904u; map[120305] = 458872904u;
            // Waller (481181063u)
            map[226293] = 481181063u; map[226294] = 481181063u; map[231117] = 481181063u; map[231118] = 481181063u;
            // Orbiter (1905614711u)
            map[343528] = 1905614711u; map[343527] = 1905614711u; map[345214] = 1905614711u; map[345215] = 1905614711u;
            // Wormhole (1156956365u)
            map[337106] = 1156956365u; map[337107] = 1156956365u;
            // Enervating (547833824u)
            map[488912] = 547833824u;
            // Horde (127452338u)
            map[71023] = 127452338u; map[71024] = 127452338u;
            // FireChains (3855260060u)
            map[226497] = 3855260060u;
            // FrozenPulse (1886876669u)
            map[348532] = 1886876669u; map[384628] = 1886876669u;
            // Poison (1929212066u)
            map[308319] = 1929212066u; map[308318] = 1929212066u; map[309247] = 1929212066u;
            return map;
        }

        private static Dictionary<uint, string> CreateAffixRussianNames()
        {
            Dictionary<uint, string> map = new Dictionary<uint, string>();
            map[2625377780u] = "Чародей";
            map[1165197192u] = "Мститель";
            map[4172983340u] = "Осквернитель";
            map[2542537664u] = "Электрический";
            map[2782485594u] = "Запас здоровья";
            map[3775118u] = "Быстрый";
            map[3855260060u] = "Огненные цепи";
            map[4131130388u] = "Заморозка";
            map[1886876669u] = "Ледяной импульс";
            map[1799201764u] = "Связующая нить";
            map[127452338u] = "Орда";
            map[394214687u] = "Иллюзионист";
            map[4267280439u] = "Тюремщик";
            map[3830498332u] = "Джаггернаут";
            map[2206426855u] = "Отбрасывание";
            map[2882216553u] = "Замедление снарядов";
            map[106438735u] = "Раскаленный след";
            map[106654229u] = "Мортира";
            map[3527404023u] = "Некротик";
            map[3049048382u] = "Кошмарный";
            map[1905614711u] = "Орбитальный";
            map[2961013602u] = "Чумной";
            map[1929212066u] = "Яд";
            map[2920375063u] = "Отражение урона";
            map[3569101591u] = "Защитная сфера";
            map[3787260902u] = "Телепортация";
            map[4244410831u] = "Грозовая буря";
            map[395423867u] = "Вампиризм";
            map[458872904u] = "Вихрь";
            map[481181063u] = "Стена";
            map[1156956365u] = "Червоточина";
            map[547833824u] = "Истощение";
            return map;
        }

        private static HashSet<int> CreateGuardianSnos()
        {
            int[] snos = new int[]
            {
                343743, 343751, 343759, 343767, 344119, 344389, 345004, 346563,
                353517, 353535, 353823, 353874, 354050, 354144, 354652, 357917,
                358208, 358429, 358489, 358614, 359094, 359688, 360281, 360636,
                426943, 472772, 487291, 487292, 487293, 487294, 487295, 487296,
                487297, 487298, 487299, 487300, 487301, 487302, 488941, 488942,
                488943, 488944, 488945, 488946, 488947, 488948, 488949, 488950,
                488951
            };

            HashSet<int> set = new HashSet<int>();
            for (int i = 0; i < snos.Length; i++) set.Add(snos[i]);
            return set;
        }

        /// <summary>A monster plus the keys used for ordering and truncation.</summary>
        private struct MonsterCandidate
        {
            public MonsterMarker Marker;
            public double Distance;
            public int Priority;
        }
    }
}
