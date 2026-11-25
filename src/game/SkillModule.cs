using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Tracks skill cooldowns and the health of the local player.
    ///
    /// Skill cooldowns are NOT read from the game: this build has no verified
    /// offsets for the cooldown table, and guessing them would produce a HUD
    /// that silently lies. Instead the module times the known duration of a
    /// skill from the moment the user pressed its key. The UI supplies the
    /// real duration through <see cref="SetCooldown"/>; a slot with a zero
    /// duration means "cooldown unknown" and is always ready.
    ///
    /// Health is the only value taken from memory: it is the verified
    /// attribute <see cref="AttributeKeys.Health"/> of the local player group.
    /// Maximum health and both resources have no verified source in this
    /// build, so they stay at -1 instead of being estimated.
    ///
    /// The module never throws: a failed frame becomes the previous snapshot
    /// with an error status, so the overlay keeps drawing the last good frame.
    /// </summary>
    public sealed class SkillModule : ISkillModule
    {
        /// <summary>Virtual key of the left mouse button.</summary>
        private const int LeftMouseButton = 0x01;
        /// <summary>Virtual key of the right mouse button.</summary>
        private const int RightMouseButton = 0x02;

        /// <summary>Slot 0 left mouse, 1 right mouse, 2..5 keys 1..4.</summary>
        private const int SlotCount = 6;

        /// <summary>Largest accepted cooldown duration, in seconds.</summary>
        private const double MaxCooldownSeconds = 3600.0;

        /// <summary>Labels of the six slots, indexed by slot.</summary>
        private static readonly string[] KeyLabels = new string[]
        {
            "ЛКМ",
            "ПКМ",
            "1",
            "2",
            "3",
            "4"
        };

        private readonly object _sync = new object();
        private readonly SlotState[] _slots;
        private readonly Dictionary<int, int> _bindings;
        private SkillSnapshot _snapshot;

        public SkillModule()
        {
            _slots = new SlotState[SlotCount];
            for (int slot = 0; slot < SlotCount; slot++)
                _slots[slot] = new SlotState();

            _bindings = new Dictionary<int, int>();

            // Default bindings: mouse buttons and the four skill keys (both numbers 1..4 and QWER).
            BindKey(LeftMouseButton, 0);
            BindKey(RightMouseButton, 1);
            BindKey(0x31, 2);   // '1'
            BindKey(0x51, 2);   // 'Q'
            BindKey(0x32, 3);   // '2'
            BindKey(0x57, 3);   // 'W'
            BindKey(0x33, 4);   // '3'
            BindKey(0x45, 4);   // 'E'
            BindKey(0x34, 5);   // '4'
            BindKey(0x52, 5);   // 'R'
        }

        /// <summary>
        /// Last captured snapshot; never null. Before the first poll it
        /// contains six ready slots and the status "Игрок не найден".
        /// </summary>
        public SkillSnapshot Snapshot
        {
            get
            {
                lock (_sync)
                {
                    if (_snapshot != null) return _snapshot;
                    return CreateEmpty();
                }
            }
        }

        /// <summary>
        /// Sets the real cooldown duration of a slot. Values outside
        /// 0..3600 seconds are ignored, as are invalid slots.
        ///
        /// A running countdown keeps the duration captured when its key was
        /// pressed, so an unrelated UI refresh cannot corrupt it. Setting the
        /// duration to zero cancels the countdown, because a zero duration
        /// means the cooldown is unknown.
        /// </summary>
        public void SetCooldown(int slot, double seconds)
        {
            if (slot < 0 || slot >= SlotCount) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            if (seconds < 0.0 || seconds > MaxCooldownSeconds) return;

            lock (_sync)
            {
                SlotState state = _slots[slot];

                if (seconds <= 0.0)
                {
                    state.DurationSeconds = 0.0;
                    state.Running = false;
                    state.StartDurationSeconds = 0.0;
                    state.StartedUtc = DateTime.MinValue;
                }
                else
                {
                    state.DurationSeconds = seconds;
                    if (!state.Running)
                        state.StartDurationSeconds = seconds;
                }
            }
        }

        /// <summary>
        /// Returns the configured cooldown duration of a slot, or zero for an
        /// invalid slot.
        /// </summary>
        public double GetCooldown(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return 0.0;

            lock (_sync)
            {
                return _slots[slot].DurationSeconds;
            }
        }

        /// <summary>
        /// Binds a virtual key to a slot. A key is always bound to at most one
        /// slot and a slot has at most one key, so rebinding replaces the old
        /// pair. Invalid keys and slots are ignored.
        /// </summary>
        public void BindKey(int virtualKey, int slot)
        {
            if (virtualKey <= 0) return;
            if (slot < 0 || slot >= SlotCount) return;

            lock (_sync)
            {
                _bindings[virtualKey] = slot;
            }
        }

        /// <summary>
        /// Returns the skill slot bound to a virtual key, or -1 when the key is
        /// not a skill key.
        /// </summary>
        public int SlotForKey(int virtualKey)
        {
            lock (_sync)
            {
                int slot;
                return _bindings.TryGetValue(virtualKey, out slot) ? slot : -1;
            }
        }

        /// <summary>
        /// Starts the cooldown of the slot bound to the key. A repeated press
        /// while the timer is still running does not restart it. Keys with an
        /// unknown (zero) duration are ignored.
        /// </summary>
        public void OnKey(int virtualKey)
        {
            lock (_sync)
            {
                int slot;
                if (!_bindings.TryGetValue(virtualKey, out slot)) return;
                if (slot < 0 || slot >= SlotCount) return;

                SlotState state = _slots[slot];
                double duration = state.DurationSeconds;
                if (duration <= 0.0) return;

                DateTime now = DateTime.UtcNow;
                if (state.Running)
                {
                    if (RemainingSeconds(state, now) > 0.0) return;
                    state.Running = false;
                }

                // The duration is captured here so later UI updates cannot
                // move the end of the countdown that is already in flight.
                state.StartedUtc = now;
                state.StartDurationSeconds = duration;
                state.Running = true;
            }
        }

        /// <summary>
        /// Advances the timers and reads the health of the local player.
        /// Never throws; on failure returns the previous snapshot with an
        /// error status so the HUD can keep its last good frame.
        /// </summary>
        public SkillSnapshot Poll(GameReader reader, int localPlayerSlot)
        {
            try
            {
                if (reader == null)
                    throw new ArgumentNullException("reader");

                DateTime now = DateTime.UtcNow;
                int gameTick = reader.ReadGameTick();
                GameReader.EquippedSkill[] equipped = reader.ReadEquippedSkills();
                List<SkillCooldown> skills = new List<SkillCooldown>(SlotCount);

                lock (_sync)
                {
                    for (int slot = 0; slot < SlotCount; slot++)
                    {
                        SlotState state = _slots[slot];
                        double remaining = RemainingSeconds(state, now);

                        // Retire a finished countdown once, so the snapshot
                        // reports the configured duration again.
                        if (state.Running && remaining <= 0.0)
                        {
                            state.Running = false;
                            state.StartDurationSeconds = 0.0;
                        }

                        double duration = state.Running
                            ? state.StartDurationSeconds
                            : state.DurationSeconds;
                        if (duration < 0.0) duration = 0.0;

                        double fraction = 0.0;
                        if (state.Running && duration > 0.0)
                        {
                            fraction = remaining / duration;
                            if (fraction < 0.0) fraction = 0.0;
                            if (fraction > 1.0) fraction = 1.0;
                        }

                        // A value read from the game always wins over the
                        // input estimate, because it includes cooldown
                        // reduction and any external reset.
                        bool fromMemory = false;
                        double memoryRemaining;
                        if (state.MemoryRemaining >= 0.0 && now - state.MemoryUpdatedUtc < MemoryFreshness)
                        {
                            memoryRemaining = state.MemoryRemaining;
                            fromMemory = true;

                            if (state.MemoryDuration > 0.0) duration = state.MemoryDuration;
                            else if (duration <= 0.0) duration = memoryRemaining;

                            fraction = duration > 0.0 ? memoryRemaining / duration : 0.0;
                            if (fraction < 0.0) fraction = 0.0;
                            if (fraction > 1.0) fraction = 1.0;
                            remaining = memoryRemaining;
                        }

                        SkillCooldown skill = new SkillCooldown();
                        skill.Slot = slot;
                        skill.Key = KeyLabels[slot];
                        skill.RemainingSeconds = remaining;
                        skill.DurationSeconds = duration;
                        skill.Fraction = fraction;
                        skill.IsEstimated = !fromMemory;
                        if (equipped != null && slot < equipped.Length)
                        {
                            skill.PowerSno = equipped[slot].PowerSno;
                            skill.Rune = equipped[slot].Rune;
                        }
                        if (fromMemory && state.MemoryFinishTick > 0)
                            skill.FinishTick = state.MemoryFinishTick;
                        else if (gameTick > 0 && remaining > 0)
                            skill.FinishTick = gameTick + (int)Math.Round(remaining * 60.0);
                        skills.Add(skill);
                    }
                }

                SkillSnapshot snapshot = new SkillSnapshot();
                snapshot.Skills = skills;
                snapshot.GameTick = gameTick;
                if (equipped != null)
                {
                    for (int i = 0; i < Math.Min(6, equipped.Length); i++)
                    {
                        snapshot.EquippedPowerSnos[i] = equipped[i].PowerSno;
                        snapshot.EquippedRunes[i] = equipped[i].Rune;
                    }
                }
                snapshot.HealthMax = -1.0;
                snapshot.PrimaryResourceType = -1;
                snapshot.PrimaryResource = -1.0;
                snapshot.PrimaryResourceMax = -1.0;
                snapshot.PrimaryResourceRegen = 0.0;
                snapshot.SecondaryResourceType = -1;
                snapshot.SecondaryResource = -1.0;
                snapshot.SecondaryResourceMax = -1.0;
                snapshot.SecondaryResourceRegen = 0.0;

                // localPlayerSlot is accepted for interface symmetry; the
                // health source is the actor record, which is validated here.
                ActorRecord me;
                if (!reader.TryReadLocalPlayer(out me))
                {
                    snapshot.Health = -1.0;
                    snapshot.Status = "Игрок не найден";
                    snapshot.CapturedUtc = DateTime.UtcNow;

                    lock (_sync)
                    {
                        _snapshot = snapshot;
                    }
                    return snapshot;
                }

                snapshot.PlayerSno = me.Sno;

                GameAttributeTable attrTable = reader.OpenAttributes();
                uint[] baseKeys = new uint[]
                {
                    AttributeKeys.Health,
                    AttributeKeys.HealthMax,
                    AttributeKeys.ResourceTypePrimary,
                    AttributeKeys.ResourceTypeSecondary
                };
                uint?[] baseVals = new uint?[baseKeys.Length];
                bool gotBase = attrTable != null && reader.TryReadAttributes(attrTable, me.GroupId, baseKeys, baseVals);

                float? health = gotBase ? GameReader.DecodeHealth(baseVals[0]) : reader.ReadHealth(me.GroupId);
                if (health.HasValue)
                {
                    snapshot.Health = (double)health.Value;
                    snapshot.Status = string.Format("Здоровье: {0:0}", health.Value);
                }
                else
                {
                    snapshot.Health = -1.0;
                    snapshot.Status = "Здоровье: неизвестно";
                }

                if (gotBase)
                {
                    float? healthMax = GameReader.DecodeHealth(baseVals[1]);
                    if (healthMax.HasValue && healthMax.Value > 0f)
                        snapshot.HealthMax = (double)healthMax.Value;

                    if (baseVals[2].HasValue)
                    {
                        int priType = unchecked((int)baseVals[2].Value);
                        if (priType >= 0 && priType <= 8)
                        {
                            snapshot.PrimaryResourceType = priType;
                            double cur, max, regen;
                            if (ReadResourceSet(reader, attrTable, me.GroupId, priType, out cur, out max, out regen))
                            {
                                snapshot.PrimaryResource = cur;
                                snapshot.PrimaryResourceMax = max;
                                snapshot.PrimaryResourceRegen = regen;
                            }
                        }
                    }

                    if (baseVals[3].HasValue)
                    {
                        int secType = unchecked((int)baseVals[3].Value);
                        if (secType >= 0 && secType <= 8)
                        {
                            snapshot.SecondaryResourceType = secType;
                            double cur, max, regen;
                            if (ReadResourceSet(reader, attrTable, me.GroupId, secType, out cur, out max, out regen))
                            {
                                snapshot.SecondaryResource = cur;
                                snapshot.SecondaryResourceMax = max;
                                snapshot.SecondaryResourceRegen = regen;
                            }
                        }
                    }
                }

                snapshot.CapturedUtc = DateTime.UtcNow;

                lock (_sync)
                {
                    _snapshot = snapshot;
                }
                return snapshot;
            }
            catch (Exception ex)
            {
                SkillSnapshot fallback;
                lock (_sync)
                {
                    fallback = _snapshot != null ? Clone(_snapshot) : CreateEmpty();
                }
                fallback.Status = "Ошибка чтения: " + ex.Message;
                return fallback;
            }
        }

        /// <summary>Stops every running timer and forgets the last snapshot.</summary>
        public void Reset()
        {
            lock (_sync)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    SlotState state = _slots[slot];
                    state.Running = false;
                    state.StartDurationSeconds = 0.0;
                    state.StartedUtc = DateTime.MinValue;
                }
                _snapshot = null;
            }
        }

        // ------------------------------------------------------------------
        // Timer helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Seconds left on a countdown, never negative. Uses the duration
        /// captured at the key press, not the current configured value.
        /// </summary>
        private static double RemainingSeconds(SlotState state, DateTime now)
        {
            if (!state.Running) return 0.0;

            double elapsed = (now - state.StartedUtc).TotalSeconds;
            // A clock that moved backwards must not extend the countdown.
            if (elapsed < 0.0) elapsed = 0.0;

            double remaining = state.StartDurationSeconds - elapsed;
            if (remaining <= 0.0) return 0.0;
            return remaining;
        }

        /// <summary>
        /// Empty snapshot used before the first poll and after Reset. The
        /// caller must hold <see cref="_sync"/> because slot durations are
        /// read from the shared state.
        /// </summary>
        private SkillSnapshot CreateEmpty()
        {
            List<SkillCooldown> skills = new List<SkillCooldown>(SlotCount);
            for (int slot = 0; slot < SlotCount; slot++)
            {
                double duration = _slots[slot].DurationSeconds;
                if (duration < 0.0) duration = 0.0;

                SkillCooldown skill = new SkillCooldown();
                skill.Slot = slot;
                skill.Key = KeyLabels[slot];
                skill.RemainingSeconds = 0.0;
                skill.DurationSeconds = duration;
                skill.Fraction = 0.0;
                skill.IsEstimated = true;
                skills.Add(skill);
            }

            SkillSnapshot snapshot = new SkillSnapshot();
            snapshot.Skills = skills;
            snapshot.Health = -1.0;
            snapshot.HealthMax = -1.0;
            snapshot.PrimaryResource = -1.0;
            snapshot.PrimaryResourceMax = -1.0;
            snapshot.SecondaryResource = -1.0;
            snapshot.SecondaryResourceMax = -1.0;
            snapshot.Status = "Игрок не найден";
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        /// <summary>Deep copy, so a fallback frame cannot alias the old one.</summary>
        private static SkillSnapshot Clone(SkillSnapshot source)
        {
            SkillSnapshot copy = new SkillSnapshot();
            copy.Skills = new List<SkillCooldown>(source.Skills.Count);
            for (int i = 0; i < source.Skills.Count; i++)
            {
                SkillCooldown sourceSkill = source.Skills[i];
                SkillCooldown skill = new SkillCooldown();
                skill.Slot = sourceSkill.Slot;
                skill.Key = sourceSkill.Key;
                skill.RemainingSeconds = sourceSkill.RemainingSeconds;
                skill.DurationSeconds = sourceSkill.DurationSeconds;
                skill.Fraction = sourceSkill.Fraction;
                skill.IsEstimated = sourceSkill.IsEstimated;
                skill.PowerSno = sourceSkill.PowerSno;
                skill.Rune = sourceSkill.Rune;
                skill.FinishTick = sourceSkill.FinishTick;
                copy.Skills.Add(skill);
            }
            copy.GameTick = source.GameTick;
            copy.PlayerSno = source.PlayerSno;
            if (source.EquippedPowerSnos != null)
                copy.EquippedPowerSnos = (int[])source.EquippedPowerSnos.Clone();
            if (source.EquippedRunes != null)
                copy.EquippedRunes = (int[])source.EquippedRunes.Clone();
            copy.PrimaryResourceType = source.PrimaryResourceType;
            copy.PrimaryResource = source.PrimaryResource;
            copy.PrimaryResourceMax = source.PrimaryResourceMax;
            copy.PrimaryResourceRegen = source.PrimaryResourceRegen;
            copy.SecondaryResourceType = source.SecondaryResourceType;
            copy.SecondaryResource = source.SecondaryResource;
            copy.SecondaryResourceMax = source.SecondaryResourceMax;
            copy.SecondaryResourceRegen = source.SecondaryResourceRegen;
            copy.Health = source.Health;
            copy.HealthMax = source.HealthMax;
            copy.Status = source.Status;
            copy.CapturedUtc = source.CapturedUtc;
            return copy;
        }

        private static bool ReadResourceSet(GameReader reader, GameAttributeTable table, int groupId,
                                            int resourceType, out double cur, out double max, out double regen)
        {
            cur = -1.0;
            max = -1.0;
            regen = 0.0;
            if (reader == null || table == null || resourceType < 0) return false;

            uint[] keys = new uint[]
            {
                AttributeKeys.MakeKey(resourceType, AttributeKeys.ResourceCurId),
                AttributeKeys.MakeKey(resourceType, AttributeKeys.ResourceMaxId),
                AttributeKeys.MakeKey(resourceType, AttributeKeys.ResourceRegenId)
            };
            uint?[] vals = new uint?[3];
            if (!reader.TryReadAttributes(table, groupId, keys, vals)) return false;

            float? curF = GameReader.DecodeHealth(vals[0]);
            float? maxF = GameReader.DecodeHealth(vals[1]);
            float? regF = GameReader.DecodeHealth(vals[2]);

            bool any = false;
            if (maxF.HasValue && maxF.Value >= 0f)
            {
                max = (double)maxF.Value;
                any = true;
            }
            if (curF.HasValue && curF.Value >= 0f)
            {
                cur = max > 0.0 ? Math.Min(max, (double)curF.Value) : (double)curF.Value;
                any = true;
            }
            else if (max > 0.0)
            {
                cur = max;
                any = true;
            }
            if (regF.HasValue && regF.Value >= 0f)
                regen = (double)regF.Value;

            return any;
        }

        /// <summary>
        /// Mutable timer of one slot. Guarded by the module lock.
        /// </summary>
        private sealed class SlotState
        {
            /// <summary>Duration configured by the UI, in seconds.</summary>
            public double DurationSeconds;
            /// <summary>Duration captured when the countdown was started.</summary>
            public double StartDurationSeconds;
            /// <summary>Moment the countdown was started, in UTC.</summary>
            public DateTime StartedUtc;
            /// <summary>True while the countdown is in flight.</summary>
            public bool Running;

            /// <summary>Remaining seconds read from the game, -1 when unknown.</summary>
            public double MemoryRemaining = -1.0;
            /// <summary>Duration read from the game, 0 when unknown.</summary>
            public double MemoryDuration;
            public int MemoryFinishTick;
            /// <summary>Moment the memory value was captured.</summary>
            public DateTime MemoryUpdatedUtc;
        }

        /// <summary>How long a value read from the game stays trusted.</summary>
        private static readonly TimeSpan MemoryFreshness = TimeSpan.FromMilliseconds(600);

        /// <summary>
        /// Applies cooldowns read from the game. Values are keyed by skill slot
        /// and are preferred over the input estimate while they stay fresh.
        /// </summary>
        public void ApplyMemoryCooldowns(Dictionary<int, double> remainingBySlot,
                                         Dictionary<int, double> durationBySlot,
                                         Dictionary<int, int> finishTickBySlot = null)
        {
            if (remainingBySlot == null) return;
            DateTime now = DateTime.UtcNow;

            lock (_sync)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    SlotState state = _slots[slot];
                    double remaining;
                    if (!remainingBySlot.TryGetValue(slot, out remaining) || remaining <= 0.0)
                    {
                        // Memory confirms no active cooldown: clear any stale countdown immediately.
                        bool hadMemory = state.MemoryRemaining >= 0.0;
                        state.MemoryRemaining = -1.0;
                        state.MemoryDuration = 0.0;
                        state.MemoryFinishTick = 0;
                        if (hadMemory)
                        {
                            state.Running = false;
                            state.StartDurationSeconds = 0.0;
                        }
                        continue;
                    }

                    state.MemoryRemaining = remaining;
                    state.MemoryUpdatedUtc = now;
                    state.Running = false;
                    state.StartDurationSeconds = 0.0;

                    int finishTick = 0;
                    if (finishTickBySlot != null && finishTickBySlot.TryGetValue(slot, out finishTick))
                        state.MemoryFinishTick = finishTick;

                    double duration;
                    if (durationBySlot != null && durationBySlot.TryGetValue(slot, out duration) && duration > 0.0)
                        state.MemoryDuration = duration;
                    else if (remaining > state.MemoryDuration)
                        state.MemoryDuration = remaining;
                }
            }
        }

        /// <summary>Drops the memory values, e.g. when the reader lost the game.</summary>
        public void ClearMemoryCooldowns()
        {
            lock (_sync)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    _slots[slot].MemoryRemaining = -1.0;
                    _slots[slot].MemoryDuration = 0.0;
                }
            }
        }
    }
}
