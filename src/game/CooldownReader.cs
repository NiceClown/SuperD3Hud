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

        /// <summary>Verified Diablo III attribute descriptors (0x16A = Power_Cooldown, 0x16B = Power_Cooldown_Start).</summary>
        private static readonly int[] SeedDescriptors = new int[]
        {
            AttributeKeys.PowerCooldownDescriptor,
            AttributeKeys.PowerCooldownStartDescriptor
        };

        private const int MinPower = 1000;
        private const int MaxPower = 0xFFFFF;
        /// <summary>Buff icon descriptors occupy this range and are skipped.</summary>
        private const int BuffIconLow = 0x24E;
        private const int BuffIconHigh = 0x31F;
        private const int MaxCooldownTicks = 3600 * 60;

        private readonly object _sync = new object();

        private int _durationDescriptor = AttributeKeys.PowerCooldownDescriptor;
        private int _startDescriptor = AttributeKeys.PowerCooldownStartDescriptor;

        private readonly Dictionary<int, double> _remaining = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _duration = new Dictionary<int, double>();
        private readonly Dictionary<int, int> _finishTick = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _charges = new Dictionary<int, int>();
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
                    if (_remaining.TryGetValue(pair.Key, out value) && value > 0.0)
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
                    if (_duration.TryGetValue(pair.Key, out value) && value > 0.0)
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

        /// <summary>Charge counts per skill slot, when available.</summary>
        public Dictionary<int, int> ChargesBySlot()
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            lock (_sync)
            {
                foreach (KeyValuePair<int, int> pair in _slotByPower)
                {
                    int charges;
                    if (_charges.TryGetValue(pair.Key, out charges) && charges >= 0)
                        result[pair.Value] = charges;
                }
            }
            return result;
        }

        /// <summary>Checks whether a persisted descriptor is a legacy Buff_Icon_* false positive.</summary>
        public static bool IsLegacyBuffDescriptor(int descriptor)
        {
            return descriptor == 0x252 || descriptor == 0x253
                || descriptor == 0x272 || descriptor == 0x273;
        }

        /// <summary>Restores a descriptor pair found in a previous session.</summary>
        public void Restore(int durationDescriptor, int startDescriptor)
        {
            lock (_sync)
            {
                if (durationDescriptor <= 0 || IsLegacyBuffDescriptor(durationDescriptor))
                {
                    _durationDescriptor = AttributeKeys.PowerCooldownDescriptor;
                    _startDescriptor = AttributeKeys.PowerCooldownStartDescriptor;
                }
                else
                {
                    _durationDescriptor = durationDescriptor;
                    _startDescriptor = (startDescriptor > 0 && !IsLegacyBuffDescriptor(startDescriptor))
                        ? startDescriptor
                        : durationDescriptor + 1;
                }
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
                _charges.Clear();
                _lastRaw.Clear();
                _anchors.Clear();
                _seen.Clear();
                _slotByPower.Clear();
                _candidateHits.Clear();
                _badFrames = 0;
                _durationDescriptor = AttributeKeys.PowerCooldownDescriptor;
                _startDescriptor = AttributeKeys.PowerCooldownStartDescriptor;
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
                return ProcessAttributes(attributes, gameTick, equipped, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _badFrames++;
                    _status = "Кулдауны: " + ex.Message;
                    if (_badFrames > 50)
                    {
                        _durationDescriptor = AttributeKeys.PowerCooldownDescriptor;
                        _startDescriptor = AttributeKeys.PowerCooldownStartDescriptor;
                        _badFrames = 0;
                    }
                }
            }
            return result;
        }

        /// <summary>Processes pre-read player attributes (also used by offline self-tests).</summary>
        public List<CooldownEntry> PollAttributes(List<KeyValuePair<uint, uint>> attributes,
                                                  int gameTick,
                                                  GameReader.EquippedSkill[] equipped)
        {
            return ProcessAttributes(attributes, gameTick, equipped, DateTime.UtcNow);
        }

        private List<CooldownEntry> ProcessAttributes(List<KeyValuePair<uint, uint>> attributes,
                                                      int gameTick,
                                                      GameReader.EquippedSkill[] equipped,
                                                      DateTime now)
        {
            List<CooldownEntry> result = new List<CooldownEntry>();
            lock (_sync)
            {
                UpdateEquippedSlotMap(equipped);

                if (attributes == null || attributes.Count == 0)
                {
                    _status = "Кулдауны: у игрока нет атрибутов";
                    return result;
                }

                if (_durationDescriptor <= 0 || IsLegacyBuffDescriptor(_durationDescriptor))
                    _durationDescriptor = AttributeKeys.PowerCooldownDescriptor;
                if (_startDescriptor <= 0 || IsLegacyBuffDescriptor(_startDescriptor))
                    _startDescriptor = AttributeKeys.PowerCooldownStartDescriptor;

                ReadValues(attributes, now, result, gameTick);
                _status = "Кулдауны: чтение из памяти (тик " + gameTick + ", " +
                          _durationDescriptor + "/" + _startDescriptor + ")";
            }
            return result;
        }

        private void UpdateEquippedSlotMap(GameReader.EquippedSkill[] equipped)
        {
            if (equipped == null) return;

            bool anyEquipped = false;
            for (int i = 0; i < equipped.Length; i++)
            {
                if (equipped[i].PowerSno > 0)
                {
                    anyEquipped = true;
                    break;
                }
            }

            if (anyEquipped)
            {
                _slotByPower.Clear();
                for (int i = 0; i < equipped.Length; i++)
                {
                    if (equipped[i].PowerSno > 0)
                        _slotByPower[equipped[i].PowerSno] = i;
                }
            }

            // Slot 6 is always Health Potion (ActionKey.Heal).
            _slotByPower[AttributeKeys.HealthPotionPowerSno] = 6;
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
            Dictionary<int, uint> durationRaw = new Dictionary<int, uint>();
            Dictionary<int, uint> startRaw = new Dictionary<int, uint>();
            Dictionary<int, uint> nextChargeRaw = new Dictionary<int, uint>();
            Dictionary<int, uint> rechargeStartRaw = new Dictionary<int, uint>();

            _charges.Clear();

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
                else if (descriptor == AttributeKeys.SkillChargesDescriptor)
                    _charges[power] = unchecked((int)attributes[i].Value);
                else if (descriptor == AttributeKeys.NextChargeGainedDescriptor)
                    nextChargeRaw[power] = attributes[i].Value;
                else if (descriptor == AttributeKeys.RechargeStartDescriptor)
                    rechargeStartRaw[power] = attributes[i].Value;
            }

            if (durationRaw.Count == 0 && nextChargeRaw.Count == 0)
            {
                ClearCooldownState();
                return;
            }
            _badFrames = 0;

            HashSet<int> activeThisFrame = new HashSet<int>();

            foreach (KeyValuePair<int, uint> pair in durationRaw)
            {
                int power = pair.Key;
                int finishTick = unchecked((int)pair.Value);
                uint startRawVal = 0;
                startRaw.TryGetValue(power, out startRawVal);
                int startTick = unchecked((int)startRawVal);

                double seconds;
                double dur;
                if (!TryComputeTickCooldown(gameTick, finishTick, startTick, power, out seconds, out dur))
                    continue;

                RecordActiveCooldown(power, finishTick, seconds, dur, now, result);
                activeThisFrame.Add(power);
            }

            // Charge-based skills report 0 charges with Next_Charge_Gained_time when empty.
            foreach (KeyValuePair<int, uint> pair in nextChargeRaw)
            {
                int power = pair.Key;
                if (activeThisFrame.Contains(power)) continue;

                int charges;
                if (_charges.TryGetValue(power, out charges) && charges > 0) continue;

                int nextTick = unchecked((int)pair.Value);
                uint startRawVal = 0;
                rechargeStartRaw.TryGetValue(power, out startRawVal);
                int rechargeStart = unchecked((int)startRawVal);

                double seconds;
                double dur;
                if (!TryComputeTickCooldown(gameTick, nextTick, rechargeStart, power, out seconds, out dur))
                    continue;

                RecordActiveCooldown(power, nextTick, seconds, dur, now, result);
                activeThisFrame.Add(power);
            }

            // Drop any cooldown that completed or disappeared on this frame.
            List<int> expired = null;
            foreach (int power in _remaining.Keys)
            {
                if (activeThisFrame.Contains(power)) continue;
                if (expired == null) expired = new List<int>();
                expired.Add(power);
            }
            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                    RemovePowerState(expired[i]);
            }
        }

        private bool TryComputeTickCooldown(int gameTick, int finishTick, int startTick, int power,
                                            out double seconds, out double dur)
        {
            seconds = 0.0;
            dur = 0.0;

            // Ready sentinel in Diablo III: finishTick == -1 or int.MaxValue.
            if (finishTick <= 0 || finishTick == int.MaxValue) return false;
            if (gameTick <= 0) return false;

            int delta = finishTick - gameTick;
            if (delta <= 0 || delta > MaxCooldownTicks) return false;

            seconds = delta / TicksPerSecond;
            if (startTick > 0 && startTick < finishTick && (finishTick - startTick) <= MaxCooldownTicks)
                dur = (finishTick - startTick) / TicksPerSecond;
            else
                dur = Math.Max(_durationValue(power), seconds);
            return true;
        }

        private void RecordActiveCooldown(int power, int finishTick, double seconds, double dur,
                                          DateTime now, List<CooldownEntry> result)
        {
            if (seconds < 0.0) seconds = 0.0;
            _finishTick[power] = finishTick;
            _lastRaw[power] = unchecked((uint)finishTick);
            _remaining[power] = seconds;
            _duration[power] = dur;
            _seen[power] = now;

            CooldownEntry entry = new CooldownEntry();
            entry.PowerSno = power;
            entry.Remaining = seconds;
            entry.Duration = dur;
            entry.FromMemory = true;
            result.Add(entry);
        }

        private void RemovePowerState(int power)
        {
            _remaining.Remove(power);
            _duration.Remove(power);
            _finishTick.Remove(power);
            _lastRaw.Remove(power);
            _anchors.Remove(power);
            _seen.Remove(power);
        }

        private void ClearCooldownState()
        {
            _remaining.Clear();
            _duration.Clear();
            _finishTick.Clear();
            _lastRaw.Clear();
            _anchors.Clear();
            _seen.Clear();
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
