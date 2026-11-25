using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Reads the buffs and debuffs of the local player.
    ///
    /// The game stores every active power as an attribute of the player actor
    /// group. The key is built from the power SNO and the icon index:
    ///
    ///     key = (powerSno &lt;&lt; 12) | (0x2FF + iconIndex),  iconIndex 0..31
    ///
    /// and the value is the stack count. Walking every attribute therefore
    /// yields the complete list of active powers without a hard-coded power
    /// catalogue: a power is active when the highest of its icon values is
    /// greater than zero.
    ///
    /// The module never throws. A corrupted or racing frame becomes a snapshot
    /// with an error status, and the HUD keeps drawing the last good frame.
    /// </summary>
    public sealed class BuffModule : IBuffModule
    {
        /// <summary>First attribute key of a buff icon slot.</summary>
        private const uint BuffIconFirst = 0x2FF;
        /// <summary>Last attribute key of a buff icon slot.</summary>
        private const uint BuffIconLast = 0x31F;
        /// <summary>Largest power SNO the attribute layout can encode.</summary>
        private const int MaxPowerSno = 0xFFFFF;
        /// <summary>Stack values above this are corrupted data, not stacks.</summary>
        private const uint MaxStacks = 1000000;
        /// <summary>Hard cap on the entries of one snapshot.</summary>
        private const int MaxEntries = 80;

        /// <summary>Power SNO of Convention of Elements.</summary>
        private const int ConventionOfElements = 430674;

        /// <summary>
        /// Element names of Convention of Elements, indexed by icon 1..7.
        /// Index 0 is unused because icon 0 is not an element.
        /// </summary>
        private static readonly string[] ElementNames = new string[]
        {
            "",
            "Тайна",    // 1 Arcane
            "Холод",    // 2 Cold
            "Огонь",    // 3 Fire
            "Свет",     // 4 Holy
            "Молния",   // 5 Lightning
            "Физика",   // 6 Physical
            "Яд"        // 7 Poison
        };

        private BuffSnapshot _snapshot;

        /// <summary>Last captured snapshot; never null.</summary>
        public BuffSnapshot Snapshot
        {
            get
            {
                if (_snapshot != null) return _snapshot;

                BuffSnapshot empty = new BuffSnapshot();
                empty.Buffs = new List<BuffEntry>();
                empty.Status = "Игрок не найден";
                empty.CapturedUtc = DateTime.UtcNow;
                return empty;
            }
        }

        /// <summary>
        /// Reads one frame of buffs for the local player.
        /// Never throws; on failure returns the previous snapshot with an
        /// error status so the HUD can keep its last good frame.
        /// </summary>
        public BuffSnapshot Poll(GameReader reader, int worldId, int localPlayerSlot)
        {
            try
            {
                if (reader == null)
                    throw new ArgumentNullException("reader");

                ActorRecord me;
                if (!reader.TryReadLocalPlayer(out me))
                {
                    BuffSnapshot missing = new BuffSnapshot();
                    missing.WorldId = worldId;
                    missing.Buffs = new List<BuffEntry>();
                    missing.Status = "Игрок не найден";
                    missing.CapturedUtc = DateTime.UtcNow;
                    _snapshot = missing;
                    return missing;
                }

                List<KeyValuePair<uint, uint>> attributes = reader.ReadAllAttributes(me.GroupId);

                Dictionary<int, PowerAccumulator> powers =
                    new Dictionary<int, PowerAccumulator>();

                for (int i = 0; i < attributes.Count; i++)
                {
                    uint key = attributes[i].Key;
                    uint value = attributes[i].Value;

                    uint iconKey = key & 0xFFF;
                    if (iconKey < BuffIconFirst || iconKey > BuffIconLast) continue;

                    int powerSno = (int)(key >> 12);
                    if (powerSno <= 0 || powerSno > MaxPowerSno) continue;

                    // Stack counts are small; a huge value is garbage.
                    if (value > MaxStacks) continue;

                    int iconIndex = (int)(iconKey - BuffIconFirst);

                    PowerAccumulator accumulator;
                    if (!powers.TryGetValue(powerSno, out accumulator))
                    {
                        accumulator = new PowerAccumulator();
                        accumulator.PowerSno = powerSno;
                        powers.Add(powerSno, accumulator);
                    }
                    accumulator.Add(iconIndex, value);
                }

                List<BuffEntry> entries = new List<BuffEntry>(powers.Count);
                int activeCount = 0;

                foreach (PowerAccumulator accumulator in powers.Values)
                {
                    BuffEntry entry = new BuffEntry();
                    entry.PowerSno = accumulator.PowerSno;
                    entry.Name = BuffPowers.NameOf(accumulator.PowerSno);
                    entry.Stacks = accumulator.MaxValue > int.MaxValue
                        ? int.MaxValue
                        : (int)accumulator.MaxValue;
                    entry.Active = accumulator.MaxValue > 0;
                    entry.Element = accumulator.Element();

                    if (entry.Active) activeCount++;
                    entries.Add(entry);
                }

                entries.Sort(CompareBuffs);

                if (entries.Count > MaxEntries)
                    entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

                BuffSnapshot snapshot = new BuffSnapshot();
                snapshot.WorldId = worldId;
                snapshot.Buffs = entries;
                snapshot.Status = string.Format("Баффов: {0}", activeCount);
                snapshot.CapturedUtc = DateTime.UtcNow;
                _snapshot = snapshot;
                return snapshot;
            }
            catch (Exception ex)
            {
                BuffSnapshot fallback = _snapshot != null
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
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Active entries first, then more stacks first, then by name. The
        /// power SNO is the final tie-break so the order is deterministic.
        /// </summary>
        private static int CompareBuffs(BuffEntry a, BuffEntry b)
        {
            if (a.Active != b.Active)
                return a.Active ? -1 : 1;
            if (a.Stacks != b.Stacks)
                return a.Stacks > b.Stacks ? -1 : 1;

            int byName = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            if (byName != 0) return byName;

            if (a.PowerSno != b.PowerSno)
                return a.PowerSno < b.PowerSno ? -1 : 1;
            return 0;
        }

        private static BuffSnapshot CreateEmpty(int worldId)
        {
            BuffSnapshot snapshot = new BuffSnapshot();
            snapshot.WorldId = worldId;
            snapshot.Buffs = new List<BuffEntry>();
            snapshot.Status = "";
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        private static BuffSnapshot Clone(BuffSnapshot source)
        {
            BuffSnapshot copy = new BuffSnapshot();
            copy.WorldId = source.WorldId;
            copy.Buffs = new List<BuffEntry>(source.Buffs);
            copy.Status = source.Status;
            copy.CapturedUtc = source.CapturedUtc;
            return copy;
        }

        /// <summary>
        /// Highest stack count of one power plus the icon bookkeeping needed
        /// to resolve the Convention of Elements rotation.
        /// </summary>
        private sealed class PowerAccumulator
        {
            public int PowerSno;
            public uint MaxValue;
            /// <summary>Number of element icons (1..7) with a positive value.</summary>
            public int ElementIcons;
            /// <summary>Icon index of the last positive element icon.</summary>
            public int ElementIcon;

            public void Add(int iconIndex, uint value)
            {
                if (value > MaxValue) MaxValue = value;

                // Icons 1..7 are the Convention of Elements elements; icon 0
                // is the base icon and carries no element.
                if (iconIndex >= 1 && iconIndex <= 7 && value > 0)
                {
                    ElementIcons++;
                    ElementIcon = iconIndex;
                }
            }

            /// <summary>
            /// Element name when exactly one element icon is active, empty
            /// otherwise. Only Convention of Elements uses this field.
            /// </summary>
            public string Element()
            {
                if (PowerSno != ConventionOfElements) return "";
                if (ElementIcons != 1) return "";
                if (ElementIcon < 1 || ElementIcon >= ElementNames.Length) return "";
                return ElementNames[ElementIcon];
            }
        }
    }
}
