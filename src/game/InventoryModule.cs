using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Reads the backpack (10x6 grid) of the local player.
    ///
    /// An item actor belongs to the backpack when it is an item, its owner is
    /// the local player and its slot is zero. The grid position is validated
    /// before the entry is accepted, and the whole module is capped at 120
    /// entries so a corrupted actor container cannot flood the overlay.
    ///
    /// Name resolution is the only expensive part of a poll, so at most 40
    /// lookups are made per frame. Names that were already resolved are kept
    /// in a small cache, which means a full backpack is labelled over a few
    /// frames instead of stalling one frame.
    ///
    /// The module never throws: a failed frame becomes the previous snapshot
    /// with an error status.
    /// </summary>
    public sealed class InventoryModule : IInventoryModule
    {
        private const int BackpackColumns = 10;
        private const int BackpackRows = 6;
        private const int TotalSlots = BackpackColumns * BackpackRows;
        private const int MaxItems = 120;
        private const int MaxDefinitionCalls = 40;

        private const uint MaxQuality = 9u;
        private const uint MaxAncientRank = 2u;

        private InventorySnapshot _snapshot;
        private ItemDefinitions _definitions;
        private IGameMemoryReader _memory;
        private IntPtr _baseAddress;
        private readonly Dictionary<int, string> _names;

        public InventoryModule()
        {
            _names = new Dictionary<int, string>();
        }

        /// <summary>Last captured snapshot; never null.</summary>
        public InventorySnapshot Snapshot
        {
            get
            {
                if (_snapshot != null) return _snapshot;

                InventorySnapshot empty = CreateEmpty();
                empty.Status = "Игрок не найден";
                empty.CapturedUtc = DateTime.UtcNow;
                return empty;
            }
        }

        /// <summary>
        /// Reads one frame of the backpack. Never throws; on failure returns
        /// the previous snapshot with an error status.
        /// </summary>
        public InventorySnapshot Poll(GameReader reader, int worldId)
        {
            try
            {
                if (reader == null) throw new ArgumentNullException("reader");

                EnsureDefinitions(reader);

                ActorRecord me;
                if (!reader.TryReadLocalPlayer(out me))
                {
                    InventorySnapshot missing = CreateEmpty();
                    missing.Status = "Игрок не найден";
                    missing.CapturedUtc = DateTime.UtcNow;
                    _snapshot = missing;
                    return missing;
                }

                List<ActorRecord> actors = reader.ReadActors();
                List<ItemEntry> items = new List<ItemEntry>();
                int definitionCalls = 0;

                for (int i = 0; i < actors.Count; i++)
                {
                    if (items.Count >= MaxItems) break;

                    ActorRecord record = actors[i];
                    if (!record.IsItem) continue;
                    if (record.OwnerId != me.Id) continue;
                    if (record.Slot != 0) continue;
                    // The grid is 10x6; anything outside is not a backpack
                    // cell and must not reach the overlay.
                    if (record.Column < 0 || record.Column >= BackpackColumns) continue;
                    if (record.Row < 0 || record.Row >= BackpackRows) continue;

                    ItemEntry entry = new ItemEntry();
                    entry.Column = record.Column;
                    entry.Row = record.Row;
                    entry.Gbid = record.Gbid;
                    entry.Quality = ReadQuality(reader, record.GroupId);
                    entry.Rank = ReadRank(reader, record.GroupId);
                    entry.Name = ResolveName(record.Gbid, ref definitionCalls);
                    items.Add(entry);
                }

                InventorySnapshot snapshot = new InventorySnapshot();
                snapshot.Items = items;
                snapshot.UsedSlots = items.Count;
                snapshot.TotalSlots = TotalSlots;
                snapshot.Status = BuildStatus(items.Count, TotalSlots);
                snapshot.CapturedUtc = DateTime.UtcNow;
                _snapshot = snapshot;
                return snapshot;
            }
            catch (Exception ex)
            {
                InventorySnapshot fallback = _snapshot != null
                    ? Clone(_snapshot)
                    : CreateEmpty();
                fallback.Status = "Ошибка чтения: " + ex.Message;
                return fallback;
            }
        }

        /// <summary>Forgets the last snapshot and the resolved names.</summary>
        public void Reset()
        {
            _snapshot = null;
            _names.Clear();
        }

        // ------------------------------------------------------------------
        // Definition source
        // ------------------------------------------------------------------

        /// <summary>
        /// Keeps exactly one definition resolver for the current process. The
        /// reader is recreated when the game is restarted, so the identity of
        /// the memory interface and the module base are both checked.
        /// </summary>
        private void EnsureDefinitions(GameReader reader)
        {
            IGameMemoryReader memory = reader.Memory;
            IntPtr baseAddress = reader.BaseAddress;

            if (_definitions != null &&
                object.ReferenceEquals(_memory, memory) &&
                _baseAddress == baseAddress)
                return;

            _memory = memory;
            _baseAddress = baseAddress;
            _definitions = new ItemDefinitions(memory, baseAddress);
            _names.Clear();
        }

        /// <summary>
        /// Returns a cached name or asks the definition resolver, at most
        /// <see cref="MaxDefinitionCalls"/> times per poll. Names are cached
        /// only when they are not empty, so a transient failure is retried on
        /// the next frame instead of being remembered forever.
        /// </summary>
        private string ResolveName(int gbid, ref int calls)
        {
            if (gbid <= 0) return "";

            string cached;
            if (_names.TryGetValue(gbid, out cached)) return cached;

            // The public plugin API maps the same GBID to a readable name.
            // Prefer it whenever it is a real name rather than an identifier.
            string readable = SuperD3Hud.TurboHost.HostNames.ItemName((uint)gbid);
            if (!string.IsNullOrEmpty(readable) && readable.IndexOf('_') < 0)
            {
                _names[gbid] = readable;
                return readable;
            }

            if (calls >= MaxDefinitionCalls) return "";

            calls++;
            string name = _definitions.Name(gbid);
            if (!string.IsNullOrEmpty(name)) _names[gbid] = name;
            return name;
        }

        // ------------------------------------------------------------------
        // Attributes and helpers
        // ------------------------------------------------------------------

        private static int ReadQuality(GameReader reader, int groupId)
        {
            uint? raw = reader.ReadAttribute(groupId, AttributeKeys.ItemQuality);
            if (!raw.HasValue) return 0;
            if (raw.Value > MaxQuality) return 0;
            return (int)raw.Value;
        }

        private static int ReadRank(GameReader reader, int groupId)
        {
            uint? raw = reader.ReadAttribute(groupId, AttributeKeys.AncientRank);
            if (!raw.HasValue) return 0;
            if (raw.Value > MaxAncientRank) return 0;
            return (int)raw.Value;
        }

        private static string BuildStatus(int used, int total)
        {
            int free = total - used;
            if (free < 0) free = 0;
            return string.Format("Предметов: {0} из {1}, свободно {2}", used, total, free);
        }

        private static InventorySnapshot CreateEmpty()
        {
            InventorySnapshot snapshot = new InventorySnapshot();
            snapshot.Items = new List<ItemEntry>();
            snapshot.UsedSlots = 0;
            snapshot.TotalSlots = TotalSlots;
            snapshot.Status = "";
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        private static InventorySnapshot Clone(InventorySnapshot source)
        {
            InventorySnapshot copy = new InventorySnapshot();
            copy.Items = new List<ItemEntry>(source.Items);
            copy.UsedSlots = source.UsedSlots;
            copy.TotalSlots = source.TotalSlots;
            copy.Status = source.Status;
            copy.CapturedUtc = source.CapturedUtc;
            return copy;
        }
    }
}
