using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>One line of the damage meter: a player and their damage.</summary>
    public sealed class DamageEntry
    {
        public string Name = "";
        public double Damage;
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
        }

        /// <summary>How long a hit counts towards the damage per second.</summary>
        public const double DpsWindowSeconds = 10.0;

        /// <summary>
        /// How long the meter stays on the screen after the last hit. The game
        /// does not say whether the player is in a rift, so the meter shows
        /// itself while there is something to show and hides again.
        /// </summary>
        public const double RecentSeconds = 8.0;

        private readonly Dictionary<int, MonsterState> _monsters = new Dictionary<int, MonsterState>();
        private readonly Dictionary<string, double> _damage = new Dictionary<string, double>();
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly List<DamageEntry> _entries = new List<DamageEntry>();

        private int _worldId;
        private double _total;
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

        /// <summary>The damage of the last <see cref="DpsWindowSeconds"/> seconds per second.</summary>
        public double Dps
        {
            get
            {
                double sum = 0;
                DateTime now = DateTime.UtcNow;
                for (int i = _hits.Count - 1; i >= 0; i--)
                {
                    if ((now - _hits[i].At).TotalSeconds > DpsWindowSeconds) break;
                    sum += _hits[i].Damage;
                }
                return sum / DpsWindowSeconds;
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

            // The health of every monster of the previous read.
            var seen = new HashSet<int>();
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterMarker monster = monsters[i];
                    if (monster == null) continue;
                    seen.Add(monster.Id);

                    MonsterState state;
                    if (!_monsters.TryGetValue(monster.Id, out state))
                    {
                        state = new MonsterState();
                        state.MaxHealth = monster.Health;
                        _monsters[monster.Id] = state;
                    }

                    if (monster.Health > state.Health)
                    {
                        // The health grew: the monster healed, a shield took
                        // the hit, or the id belongs to a new monster. In all
                        // three cases the old numbers are useless, so the
                        // monster starts over.
                        state.Health = monster.Health;
                        state.MaxHealth = monster.Health;
                        state.Damage = 0;
                        state.Falling = false;
                        state.X = monster.X;
                        state.Y = monster.Y;
                        state.Seen = true;
                        continue;
                    }

                    if (state.Health > monster.Health && state.Health > 0)
                    {
                        // A monster can not take more damage than it has
                        // health, so a bigger drop is a bad reading and is
                        // ignored.
                        double room = state.MaxHealth - state.Damage;
                        double damage = Math.Min(state.Health - monster.Health, room);
                        if (damage > 0)
                        {
                            state.Damage += damage;
                            state.Falling = true;
                            AddDamage(now, damage, monster.X, monster.Y);
                        }
                    }
                    else if (monster.Health < state.Health)
                    {
                        state.Falling = false;
                    }

                    state.Health = monster.Health;
                    state.X = monster.X;
                    state.Y = monster.Y;
                    state.Seen = true;
                }
            }

            // A monster that is gone died: its last health was the damage.
            var gone = new List<int>();
            foreach (KeyValuePair<int, MonsterState> pair in _monsters)
            {
                if (pair.Value.Seen && !seen.Contains(pair.Key) && pair.Value.Health > 0)
                    gone.Add(pair.Key);
            }
            for (int i = 0; i < gone.Count; i++)
            {
                MonsterState state = _monsters[gone[i]];
                // The monster is gone: it died with the health it still had.
                // Only a monster that was really losing health counts, so one
                // that ran away does not add a phantom hit.
                if (state.Falling)
                {
                    double room = state.MaxHealth - state.Damage;
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

            BuildEntries();
        }

        private void AddDamage(DateTime now, double damage, float x, float y)
        {
            if (damage <= 0) return;

            string source = FindSource(x, y);
            double value;
            _damage.TryGetValue(source, out value);
            _damage[source] = value + damage;
            _lastHit = now;

            Hit hit = new Hit();
            hit.At = now;
            hit.Damage = damage;
            _hits.Add(hit);
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

        private void BuildEntries()
        {
            _entries.Clear();
            _total = 0;
            if (_damage.Count == 0) return;

            foreach (KeyValuePair<string, double> pair in _damage)
            {
                DamageEntry entry = new DamageEntry();
                entry.Name = pair.Key;
                entry.Damage = pair.Value;
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
            _lastHit = DateTime.MinValue;
        }
    }
}
