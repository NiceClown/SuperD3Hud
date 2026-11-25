using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>One cooldown observed in the game's attributes.</summary>
    public sealed class CooldownEntry
    {
        public int PowerSno;
        /// <summary>Seconds left, or -1 when it could not be computed.</summary>
        public double Remaining;
        public double Duration;
        public bool FromMemory;
    }

    /// <summary>
    /// Reads skill cooldowns from the game's attribute table.
    ///
    /// Diablo III stores a cooldown as a pair of actor attributes keyed by the
    /// power: the low 12 bits of the key select the descriptor, the upper bits
    /// carry the power SNO — the same scheme the buff icons use. The exact
    /// descriptor numbers are not published anywhere, so this reader finds them
    /// by observation:
    ///
    /// * every frame it takes the attribute map of the local player;
    /// * when a skill key was pressed shortly before, it records which
    ///   descriptors appeared or changed;
    /// * the descriptor that keeps reacting to skill presses wins, and its
    ///   neighbour is taken as the "cooldown start" companion.
    ///
    /// The pair is remembered in config\settings.ini, so the discovery runs
    /// once. A pair that produces no plausible values is discarded and the
    /// search starts again, which keeps the HUD honest on a wrong guess.
    /// </summary>
    public sealed class CooldownReader
    {
        /// <summary>Game runs at 60 ticks per second; attribute values are ticks.</summary>
        public const double TicksPerSecond = 60.0;

        /// <summary>Descriptor candidates from the public attribute table (0x272 = Power_Cooldown, 0x252 = Power_Cooldown_Start).</summary>
        private static readonly int[] SeedDescriptors = new int[] { 0x272, 0x252, 0x16B, 0x16C };

        private const int MinPower = 1000;
        private const int MaxPower = 0xFFFFF;
        /// <summary>Buff icon descriptors occupy this range and are skipped.</summary>
        private const int BuffIconLow = 0x2FF;
        private const int BuffIconHigh = 0x31F;

        private readonly object _sync = new object();

        private int _durationDescriptor = 0x272;
        private int _startDescriptor = 0x252;

        private readonly Dictionary<int, double> _remaining = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _duration = new Dictionary<int, double>();
        private readonly Dictionary<int, int> _finishTick = new Dictionary<int, int>();
        private readonly Dictionary<int, uint> _lastRaw = new Dictionary<int, uint>();
        private readonly Dictionary<int, DateTime> _anchors = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, DateTime> _seen = new Dictionary<int, DateTime>();

        private readonly Dictionary<int, int> _slotByPower = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _candidateHits = new Dictionary<int, int>();

        private DateTime _lastKeyAt = DateTime.MinValue;
        private int _lastKeySlot = -1;
        private int _keyPresses;
        private readonly HashSet<int> _keysSeenSinceKey = new HashSet<int>();

        private int _badFrames;
        private string _status = "Кулдауны: чтение из памяти";

        public int DurationDescriptor { get { lock (_sync) return _durationDescriptor; } }

        public int StartDescriptor { get { lock (_sync) return _startDescriptor; } }

        public string Status { get { lock (_sync) return _status; } }

        /// <summary>True when a descriptor pair was found and produces values.</summary>
        public bool IsActive { get { lock (_sync) return _durationDescriptor > 0; } }

        /// <summary>Remaining seconds per skill slot, as far as they are known.</summary>
        public Dictionary<int, double> RemainingBySlot()
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            lock (_sync)
            {
                foreach (KeyValuePair<int, int> pair in _slotByPower)
                {
                    double value;
                    if (_remaining.TryGetValue(pair.Key, out value) && value >= 0)
                        result[pair.Value] = value;
                }
            }
            return result;
        }

        /// <summary>Duration in seconds per skill slot.</summary>
        public Dictionary<int, double> DurationBySlot()
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            lock (_sync)
            {
                foreach (KeyValuePair<int, int> pair in _slotByPower)
                {
                    double value;
                    if (_duration.TryGetValue(pair.Key, out value) && value > 0)
                        result[pair.Value] = value;
                }
            }
            return result;
        }

        /// <summary>Game tick when each slot's cooldown finishes.</summary>
        public Dictionary<int, int> FinishTickBySlot()
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            lock (_sync)
            {
                foreach (KeyValuePair<int, int> pair in _slotByPower)
                {
                    int tick;
                    if (_finishTick.TryGetValue(pair.Key, out tick) && tick > 0)
                        result[pair.Value] = tick;
                }
            }
            return result;
        }

        /// <summary>Restores a descriptor pair found in a previous session.</summary>
        public void Restore(int durationDescriptor, int startDescriptor)
        {
            lock (_sync)
            {
                _durationDescriptor = durationDescriptor > 0 ? durationDescriptor : 0x272;
                _startDescriptor = startDescriptor > 0 ? startDescriptor : 0x252;
                _status = "Кулдауны: чтение из памяти";
            }
        }

        /// <summary>Reports that the user pressed a skill key.</summary>
        public void OnKey(int slot)
        {
            lock (_sync)
            {
                _lastKeyAt = DateTime.UtcNow;
                _lastKeySlot = slot;
                _keyPresses++;
                _keysSeenSinceKey.Clear();
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _remaining.Clear();
                _duration.Clear();
                _finishTick.Clear();
                _lastRaw.Clear();
                _anchors.Clear();
                _seen.Clear();
                _slotByPower.Clear();
                _candidateHits.Clear();
                _badFrames = 0;
                _durationDescriptor = 0x272;
                _startDescriptor = 0x252;
                _status = "Кулдауны: чтение из памяти";
            }
        }

        /// <summary>
        /// Reads one frame. Never throws: a failed frame only updates the status.
        /// </summary>
        public List<CooldownEntry> Poll(GameReader reader, ActorRecord player, int gameTick = 0, GameReader.EquippedSkill[] equipped = null)
        {
            List<CooldownEntry> result = new List<CooldownEntry>();
            if (reader == null) return result;

            try
            {
                if (gameTick <= 0) gameTick = reader.ReadGameTick();
                if (equipped == null) equipped = reader.ReadEquippedSkills();

                List<KeyValuePair<uint, uint>> attributes = reader.ReadAllAttributes(player.GroupId);
                DateTime now = DateTime.UtcNow;

                lock (_sync)
                {
                    if (equipped != null)
                    {
                        for (int i = 0; i < equipped.Length; i++)
                        {
                            if (equipped[i].PowerSno > 0)
                                _slotByPower[equipped[i].PowerSno] = i;
                        }
                    }

                    if (attributes.Count == 0)
                    {
                        _status = "Кулдауны: у игрока нет атрибутов";
                        return result;
                    }

                    if (_durationDescriptor <= 0)
                        _durationDescriptor = 0x272;
                    if (_startDescriptor <= 0)
                        _startDescriptor = 0x252;

                    ReadValues(attributes, now, result, gameTick);
                    _status = "Кулдауны: чтение из памяти (тик " + gameTick + ")";
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _badFrames++;
                    _status = "Кулдауны: " + ex.Message;
                    if (_badFrames > 50)
                    {
                        _durationDescriptor = 0x272;
                        _startDescriptor = 0x252;
                        _badFrames = 0;
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------

        private void Discover(List<KeyValuePair<uint, uint>> attributes, DateTime now)
        {
            // Only learn right after a key press: that is the only moment when
            // a new cooldown attribute can appear.
            bool recentKey = _lastKeySlot >= 0 && (now - _lastKeyAt).TotalMilliseconds < 400;
            if (!recentKey) return;

            for (int i = 0; i < attributes.Count; i++)
            {
                uint key = attributes[i].Key;
                int descriptor = (int)(key & 0xFFF);
                int power = (int)(key >> 12);

                if (power < MinPower || power > MaxPower) continue;
                if (descriptor >= BuffIconLow && descriptor <= BuffIconHigh) continue;
                if (descriptor <= 0 || descriptor > 0xFFF) continue;

                // Only values that look like a cooldown in ticks are counted:
                // this keeps unrelated per-power attributes out of the vote.
                uint value = attributes[i].Value;
                if (value < 60 || value > 3600u * 60u) continue;

                if (!_keysSeenSinceKey.Add((int)key)) continue;

                int hits;
                _candidateHits.TryGetValue(descriptor, out hits);
                _candidateHits[descriptor] = hits + 1;
            }

            // The public attribute table places Power_Cooldown and
            // Power_Cooldown_Start next to each other, so a seed candidate that
            // has been seen is accepted immediately; anything else needs three
            // independent observations.
            int best = 0;
            int bestHits = 0;

            for (int i = 0; i < SeedDescriptors.Length; i++)
            {
                int descriptor = SeedDescriptors[i];
                int hits;
                if (!_candidateHits.TryGetValue(descriptor, out hits)) continue;
                if (hits >= 1 && bestHits < hits)
                {
                    best = descriptor;
                    bestHits = hits;
                }
            }

            if (best == 0)
            {
                foreach (KeyValuePair<int, int> pair in _candidateHits)
                {
                    int needed = _keyPresses >= 3 ? 2 : 3;
                    if (pair.Value < needed) continue;
                    if (pair.Value <= bestHits) continue;
                    best = pair.Key;
                    bestHits = pair.Value;
                }
            }

            if (best == 0) return;

            _durationDescriptor = best;
            _startDescriptor = best + 1;
            _status = "Кулдауны: чтение из памяти";
        }

        // ------------------------------------------------------------------
        // Reading
        // ------------------------------------------------------------------

        private void ReadValues(List<KeyValuePair<uint, uint>> attributes, DateTime now,
                                List<CooldownEntry> result, int gameTick = 0)
        {
            // Collect the raw values of this frame first, so the loop below can
            // tell a fresh cooldown from a stale one.
            Dictionary<int, uint> durationRaw = new Dictionary<int, uint>();
            Dictionary<int, uint> startRaw = new Dictionary<int, uint>();

            for (int i = 0; i < attributes.Count; i++)
            {
                uint key = attributes[i].Key;
                int descriptor = (int)(key & 0xFFF);
                int power = (int)(key >> 12);
                if (power < MinPower || power > MaxPower) continue;

                if (descriptor == _durationDescriptor)
                    durationRaw[power] = attributes[i].Value;
                else if (descriptor == _startDescriptor)
                    startRaw[power] = attributes[i].Value;
            }

            if (durationRaw.Count == 0)
            {
                // No skills are currently on cooldown. Clean up expired cooldowns without wiping descriptors.
                _remaining.Clear();
                _duration.Clear();
                _finishTick.Clear();
                _lastRaw.Clear();
                return;
            }
            _badFrames = 0;

            foreach (KeyValuePair<int, uint> pair in durationRaw)
            {
                int power = pair.Key;
                uint raw = pair.Value;
                if (raw == 0) continue;

                uint startRawVal = 0;
                startRaw.TryGetValue(power, out startRawVal);

                double seconds = 0.0;
                double dur = 0.0;

                if (gameTick > 0 && raw > gameTick)
                {
                    // Target end game tick read directly from game memory (0x272).
                    seconds = (raw - gameTick) / TicksPerSecond;
                    dur = (startRawVal > 0 && raw > startRawVal)
                        ? (raw - startRawVal) / TicksPerSecond
                        : Math.Max(_durationValue(power), seconds);
                    _finishTick[power] = (int)raw;
                }
                else if (gameTick == 0 && raw > 0 && raw <= 600000)
                {
                    uint previous;
                    bool known = _lastRaw.TryGetValue(power, out previous);

                    if (!known || raw > previous)
                    {
                        _anchors[power] = now;
                        seconds = raw / TicksPerSecond;
                    }
                    else
                    {
                        DateTime anchor;
                        if (!_anchors.TryGetValue(power, out anchor)) anchor = now;
                        double elapsed = (now - anchor).TotalSeconds;
                        seconds = (previous > raw) ? (raw / TicksPerSecond) : (raw / TicksPerSecond - elapsed);
                    }
                    dur = Math.Max(_durationValue(power), raw / TicksPerSecond);
                }
                else
                {
                    // Cooldown finished or expired
                    _remaining.Remove(power);
                    _duration.Remove(power);
                    _finishTick.Remove(power);
                    continue;
                }

                if (seconds < 0) seconds = 0;

                _lastRaw[power] = raw;
                _remaining[power] = seconds;
                _duration[power] = dur;
                _seen[power] = now;

                if (_lastKeySlot >= 0 && (now - _lastKeyAt).TotalMilliseconds < 800)
                {
                    _slotByPower[power] = _lastKeySlot;
                }

                CooldownEntry entry = new CooldownEntry();
                entry.PowerSno = power;
                entry.Remaining = seconds;
                entry.Duration = dur;
                entry.FromMemory = true;
                result.Add(entry);
            }

            // Forget powers that were not seen for a while, so a finished
            // cooldown disappears instead of lingering at zero.
            List<int> stale = null;
            foreach (KeyValuePair<int, DateTime> pair in _seen)
            {
                if ((now - pair.Value).TotalSeconds < 3) continue;
                if (stale == null) stale = new List<int>();
                stale.Add(pair.Key);
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    _remaining.Remove(stale[i]);
                    _duration.Remove(stale[i]);
                    _finishTick.Remove(stale[i]);
                    _lastRaw.Remove(stale[i]);
                    _anchors.Remove(stale[i]);
                    _seen.Remove(stale[i]);
                }
            }
        }

        private double _durationValue(int power)
        {
            double value;
            return _duration.TryGetValue(power, out value) ? value : 0;
        }

        /// <summary>Forgets the learned descriptor pair and slot bindings.</summary>
        public void Forget()
        {
            lock (_sync)
            {
                _durationDescriptor = 0;
                _startDescriptor = 0;
                _candidateHits.Clear();
                _slotByPower.Clear();
                _status = "Кулдауны: поиск источника";
            }
        }
    }
}
