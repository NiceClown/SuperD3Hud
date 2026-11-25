using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>One line of the damage meter: a player and their damage.</summary>
    public sealed class DamageEntry
    {
        public string Name = "";
        public double Damage;
        public double Dps;
        /// <summary>Share of the total damage, 0..1.</summary>
        public double Share;
        public bool IsLocalPlayer;
    }

    /// <summary>
    /// Counts the damage the group deals.
    ///
    /// The game does not publish the damage events, so the health of every
    /// monster is watched instead: every drop of the health is damage, and a
    /// monster that disappears from the actor list died with the health it
    /// had last. The damage is credited to the player who stands closest to
    /// the monster, which is the one who could have hit it.
    /// </summary>
    public sealed class DamageTracker
    {
        private sealed class MonsterState
        {
            public float Health;
            /// <summary>The highest health the monster had: its damage can not be more.</summary>
            public float MaxHealth;
            /// <summary>The damage that was already counted for this monster.</summary>
            public double Damage;
            /// <summary>True while the monster is losing health.</summary>
            public bool Falling;
            public float X;
            public float Y;
            public bool Seen;
        }

        private sealed class Hit
        {
            public DateTime At;
            public double Damage;
            public string Source = "";
        }

        /// <summary>How long a hit counts towards the damage per second.</summary>
        public const double DpsWindowSeconds = 10.0;

        /// <summary>
        /// How long the meter stays on the screen after the last hit. The game
        /// does not say whether the player is in a rift, so the meter shows
        /// itself while there is something to show and hides again.
        /// </summary>
        public const double RecentSeconds = 12.0;

        /// <summary>Idle gap after which a fresh combat encounter resets counters.</summary>
        public const double IdleResetSeconds = 20.0;

        /// <summary>Monsters dying within this distance count even if one-shot between polls.</summary>
        public const float MaxOneShotRange = 65.0f;
        private const float MaxOneShotRangeSq = MaxOneShotRange * MaxOneShotRange;

        private readonly Dictionary<int, MonsterState> _monsters = new Dictionary<int, MonsterState>();
        private readonly Dictionary<string, double> _damage = new Dictionary<string, double>();
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly List<DamageEntry> _entries = new List<DamageEntry>();

        private int _worldId;
        private double _total;
        private double _peakDps;
        private DateTime _lastHit = DateTime.MinValue;
        private float _playerX;
        private float _playerY;
        private List<PartyMember> _party;

        /// <summary>
        /// True while the meter has something to show: there was a hit not
        /// long ago.
        /// </summary>
        public bool Active
        {
            get
            {
                if (_total <= 0) return false;
                return (DateTime.UtcNow - _lastHit).TotalSeconds <= RecentSeconds;
            }
        }

        /// <summary>The total damage of the group.</summary>
        public double Total { get { return _total; } }

        /// <summary>Highest rolling DPS reached in the current encounter.</summary>
        public double PeakDps { get { return _peakDps; } }

        /// <summary>The damage of the last <see cref="DpsWindowSeconds"/> seconds per second.</summary>
        public double Dps
        {
            get
            {
                DateTime now = DateTime.UtcNow;
                double elapsed;
                return ComputeWindowTotal(now, out elapsed) / elapsed;
            }
        }

        /// <summary>The players with their damage, the local player first.</summary>
        public List<DamageEntry> Entries { get { return _entries; } }

        /// <summary>
        /// Reads the new state of the world. The list has to hold every
        /// monster of the area, otherwise a monster that walks out of the
        /// list would look like it died.
        /// </summary>
        public void Update(List<MonsterMarker> monsters, int worldId, bool inRift,
                            float playerX, float playerY, List<PartyMember> party)
        {
            if (_worldId != worldId)
            {
                _worldId = worldId;
                Reset();
            }

            DateTime now = DateTime.UtcNow;
            _playerX = playerX;
            _playerY = playerY;
            _party = party;

            // Track health changes of every living monster.
            HashSet<int> seen = new HashSet<int>();
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterMarker monster = monsters[i];
                    if (monster == null) continue;
                    seen.Add(monster.Id);

                    MonsterState state;
                    if (!_monsters.TryGetValue(monster.Id, out state) || !state.Seen)
                    {
                        if (state == null)
                        {
                            state = new MonsterState();
                            _monsters[monster.Id] = state;
                        }
                        state.Health = monster.Health;
                        state.MaxHealth = monster.MaxHealth > monster.Health ? monster.MaxHealth : monster.Health;
                        state.Damage = 0;
                        state.Falling = false;
                        state.X = monster.X;
                        state.Y = monster.Y;
                        state.Seen = true;
                        continue;
                    }

                    if (monster.Health > state.Health)
                    {
                        // Monster healed or shielded up: expand MaxHealth ceiling without erasing prior damage.
                        state.Health = monster.Health;
                        double neededCeiling = state.Damage + monster.Health;
                        if (neededCeiling > state.MaxHealth)
                            state.MaxHealth = (float)neededCeiling;
                        state.X = monster.X;
                        state.Y = monster.Y;
                        continue;
                    }

                    if (state.Health > monster.Health && state.Health > 0)
                    {
                        double room = Math.Max((double)state.Health, (double)state.MaxHealth - state.Damage);
                        double damage = Math.Min(state.Health - monster.Health, room);
                        if (damage > 0)
                        {
                            state.Damage += damage;
                            state.Falling = true;
                            AddDamage(now, damage, monster.X, monster.Y);
                        }
                    }

                    state.Health = monster.Health;
                    state.X = monster.X;
                    state.Y = monster.Y;
                    state.Seen = true;
                }
            }

            // A monster that disappeared either was killed (damaged or within combat range, e.g. one-shot)
            // or despawned far outside actor range.
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, MonsterState> pair in _monsters)
            {
                if (pair.Value.Seen && !seen.Contains(pair.Key) && pair.Value.Health > 0)
                    gone.Add(pair.Key);
            }
            for (int i = 0; i < gone.Count; i++)
            {
                MonsterState state = _monsters[gone[i]];
                bool inCombatRange = MinDistanceToPartySq(state.X, state.Y) <= MaxOneShotRangeSq;
                if (state.Falling || (inCombatRange && state.Health >= 10f))
                {
                    double room = Math.Max((double)state.Health, (double)state.MaxHealth - state.Damage);
                    double damage = Math.Min(Math.Max(0, state.Health), room);
                    if (damage > 0)
                    {
                        state.Damage += damage;
                        AddDamage(now, damage, state.X, state.Y);
                    }
                }
                _monsters.Remove(gone[i]);
            }

            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if ((now - _hits[i].At).TotalSeconds > DpsWindowSeconds * 2) _hits.RemoveAt(i);
            }

            BuildEntries(now);
        }

        private void AddDamage(DateTime now, double damage, float x, float y)
        {
            if (damage <= 0) return;

            if (_lastHit != DateTime.MinValue && (now - _lastHit).TotalSeconds > IdleResetSeconds)
            {
                _damage.Clear();
                _hits.Clear();
                _entries.Clear();
                _total = 0;
                _peakDps = 0;
            }

            string source = FindSource(x, y);
            double value;
            _damage.TryGetValue(source, out value);
            _damage[source] = value + damage;
            _lastHit = now;

            Hit hit = new Hit();
            hit.At = now;
            hit.Damage = damage;
            hit.Source = source;
            _hits.Add(hit);
        }

        private double ComputeWindowTotal(DateTime now, out double elapsedSeconds)
        {
            double sum = 0.0;
            DateTime oldest = DateTime.MaxValue;
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                double age = (now - _hits[i].At).TotalSeconds;
                if (age > DpsWindowSeconds) break;
                sum += _hits[i].Damage;
                if (_hits[i].At < oldest) oldest = _hits[i].At;
            }

            if (oldest == DateTime.MaxValue)
            {
                elapsedSeconds = 1.0;
                return 0.0;
            }

            double span = (now - oldest).TotalSeconds;
            if (span < 1.0) span = 1.0;
            if (span > DpsWindowSeconds) span = DpsWindowSeconds;
            elapsedSeconds = span;
            return sum;
        }

        private float MinDistanceToPartySq(float x, float y)
        {
            float best = DistanceSquared(_playerX, _playerY, x, y);
            if (_party != null)
            {
                for (int i = 0; i < _party.Count; i++)
                {
                    PartyMember member = _party[i];
                    if (member == null) continue;
                    float d = DistanceSquared(member.X, member.Y, x, y);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>
        /// The player who stands closest to the monster could have hit it, so
        /// the damage is credited to them.
        /// </summary>
        private string FindSource(float x, float y)
        {
            string best = LocalPlayerName;
            float bestDistance = DistanceSquared(_playerX, _playerY, x, y);

            if (_party != null)
            {
                for (int i = 0; i < _party.Count; i++)
                {
                    PartyMember member = _party[i];
                    if (member == null) continue;
                    float d = DistanceSquared(member.X, member.Y, x, y);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = member.Name;
                    }
                }
            }
            return best;
        }

        private static float DistanceSquared(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return dx * dx + dy * dy;
        }

        /// <summary>The name of the local player on the meter.</summary>
        public const string LocalPlayerName = "Вы";

        private void BuildEntries(DateTime now)
        {
            _entries.Clear();
            _total = 0;
            if (_damage.Count == 0) return;

            double elapsed;
            double windowSum = ComputeWindowTotal(now, out elapsed);
            double totalDps = windowSum / elapsed;
            if (totalDps > _peakDps) _peakDps = totalDps;

            Dictionary<string, double> windowBySource = new Dictionary<string, double>();
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if ((now - _hits[i].At).TotalSeconds > DpsWindowSeconds) break;
                string src = _hits[i].Source ?? "";
                double cur;
                windowBySource.TryGetValue(src, out cur);
                windowBySource[src] = cur + _hits[i].Damage;
            }

            foreach (KeyValuePair<string, double> pair in _damage)
            {
                DamageEntry entry = new DamageEntry();
                entry.Name = pair.Key;
                entry.Damage = pair.Value;
                double srcWindow;
                windowBySource.TryGetValue(pair.Key, out srcWindow);
                entry.Dps = srcWindow / elapsed;
                entry.IsLocalPlayer = pair.Key == LocalPlayerName;
                _entries.Add(entry);
                _total += pair.Value;
            }

            // The local player is always on the list, so it is visible that
            // the damage was done by the others.
            bool found = false;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].IsLocalPlayer) { found = true; break; }
            if (!found)
            {
                DamageEntry own = new DamageEntry();
                own.Name = LocalPlayerName;
                own.Damage = 0;
                own.Dps = 0;
                own.IsLocalPlayer = true;
                _entries.Add(own);
            }

            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Share = _total > 0 ? _entries[i].Damage / _total : 0;

            _entries.Sort(delegate(DamageEntry a, DamageEntry b)
            {
                int byDamage = b.Damage.CompareTo(a.Damage);
                if (byDamage != 0) return byDamage;
                return a.IsLocalPlayer ? -1 : (b.IsLocalPlayer ? 1 : 0);
            });
        }

        /// <summary>Forgets everything: a new rift or a new area.</summary>
        public void Reset()
        {
            _monsters.Clear();
            _damage.Clear();
            _hits.Clear();
            _entries.Clear();
            _total = 0;
            _peakDps = 0;
            _lastHit = DateTime.MinValue;
        }
    }
}
