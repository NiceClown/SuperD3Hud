using System;
using System.Collections.Generic;
using System.Text;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Resolves the display name and the set membership of an item balance
    /// (GBID) from the game's balance hash table.
    ///
    /// The resolution walks four game structures:
    ///
    ///     manager -> map -> bucket chain -> asset -> row -> definition
    ///
    /// and validates every step: the mask must be a power of two minus one,
    /// the chain is bounded and cycle checked, the SNO handle must fit the
    /// container, the asset must be an item asset with the expected SNO and
    /// the definition must repeat the requested GBID.
    ///
    /// This class is called from the HUD thread on every frame, so it never
    /// throws: any failed check returns an empty name or a null set flag and
    /// the caller simply draws the item without a label.
    /// </summary>
    public sealed class ItemDefinitions
    {
        // Balance manager and its hash map.
        private const int ManagerRva = 0x16F64C0;
        private const int MapOffset = 2 * 128;
        private const int MapHeaderSize = 0x18;
        private const int MapMaskOffset = 0x00;
        private const int MapBucketsOffset = 0x10;
        private const int MaxMask = 0xFFFFF;
        private const int MaxChainNodes = 128;
        private const int NodeSize = 20;
        private const int NodeGbidOffset = 8;
        private const int NodeSnoOffset = 12;
        private const int NodeRowOffset = 16;

        // SNO lookup table.
        private const int SnoLimitRva = 0x1704C48;
        private const int SnoLookupRva = 0x1704C50;
        private const int MaxSnoLimit = 10000000;

        // Item asset container.
        private const int AssetGroupRva = 0x1714180;
        private const int AssetGroupContainerOffset = 0x20;
        private const int ContainerHeaderSize = 0x128;
        private const int LastSlotOffset = 0x108;
        private const int SlotArrayOffset = 0x120;
        private const int SlotMask = 0x3FFFF;
        private const int MaxAssetSlots = 32768;
        private const int AssetSlotStride = 24;
        private const int AssetPointerOffset = 16;

        // Item asset header.
        private const int AssetHeaderSize = 0x40;
        private const int AssetSnoOffset = 0x00;
        private const int AssetTypeOffset = 0x0C;
        private const int AssetTypeItem = 2;
        private const int AssetByteLengthOffset = 0x2C;
        private const int AssetRowsPointerOffset = 0x30;

        // Definition rows.
        private const int DefinitionStride = 0x580;
        private const int DefinitionSize = 0x178;
        private const int DefinitionGbidOffset = 0x100;
        private const int DefinitionSetIdOffset = 0x174;
        private const int MaxDefinitionRows = 1000000;

        private const int MaxNameBytes = 128;

        private readonly IGameMemoryReader _memory;
        private readonly IntPtr _baseAddress;
        private readonly Dictionary<int, string> _names;
        private readonly Dictionary<int, bool> _sets;

        public ItemDefinitions(IGameMemoryReader memory, IntPtr baseAddress)
        {
            if (memory == null) throw new ArgumentNullException("memory");
            _memory = memory;
            _baseAddress = baseAddress;
            _names = new Dictionary<int, string>();
            _sets = new Dictionary<int, bool>();
        }

        /// <summary>
        /// Display name of the item, or an empty string when the definition is
        /// not available. A successful lookup is cached; a failed one is not,
        /// so it can succeed on the next call.
        /// </summary>
        public string Name(int gbid)
        {
            string cached;
            if (_names.TryGetValue(gbid, out cached)) return cached;

            string name;
            bool? isSet;
            if (!TryResolve(gbid, out name, out isSet)) return "";

            _names[gbid] = name;
            if (isSet.HasValue) _sets[gbid] = isSet.Value;
            return name;
        }

        /// <summary>
        /// True when the item belongs to a set, false when it does not and
        /// null when the data is not available.
        /// </summary>
        public bool? IsSet(int gbid)
        {
            bool cached;
            if (_sets.TryGetValue(gbid, out cached)) return cached;

            string name;
            bool? isSet;
            if (!TryResolve(gbid, out name, out isSet)) return null;

            _names[gbid] = name;
            if (isSet.HasValue) _sets[gbid] = isSet.Value;
            return isSet;
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves one GBID. Every validation failure is reported as false,
        /// never as an exception.
        /// </summary>
        private bool TryResolve(int gbid, out string name, out bool? isSet)
        {
            name = "";
            isSet = null;

            if (gbid <= 0) return false;

            try
            {
                IntPtr map;
                if (!TryResolveMap(out map)) return false;

                byte[] header = _memory.Read(map, MapHeaderSize);
                int mask = BitConverter.ToInt32(header, MapMaskOffset);
                if (mask < 0 || mask > MaxMask) return false;
                // A hash mask is always a power of two minus one.
                if ((mask & (mask + 1)) != 0) return false;

                IntPtr buckets = new IntPtr(BitConverter.ToInt64(header, MapBucketsOffset));
                if (buckets == IntPtr.Zero) return false;

                uint hash = Fnv1a(gbid);
                int bucket = (int)(hash & (uint)mask);
                IntPtr node = _memory.Pointer(buckets + bucket * 8);

                HashSet<IntPtr> visited = new HashSet<IntPtr>();
                int sno = -1;
                int row = -1;
                bool found = false;

                while (node != IntPtr.Zero)
                {
                    // Bounded chain plus cycle detection: a corrupted table
                    // cannot turn into an endless walk.
                    if (visited.Count >= MaxChainNodes || !visited.Add(node))
                        return false;

                    byte[] entry = _memory.Read(node, NodeSize);
                    if (BitConverter.ToInt32(entry, NodeGbidOffset) == gbid)
                    {
                        sno = BitConverter.ToInt32(entry, NodeSnoOffset);
                        row = BitConverter.ToInt32(entry, NodeRowOffset);
                        found = true;
                        break;
                    }
                    node = new IntPtr(BitConverter.ToInt64(entry, 0));
                }

                if (!found) return false;

                return TryResolveDefinition(sno, row, gbid, out name, out isSet);
            }
            catch
            {
                name = "";
                isSet = null;
                return false;
            }
        }

        /// <summary>Locates the balance hash map header.</summary>
        private bool TryResolveMap(out IntPtr map)
        {
            map = IntPtr.Zero;

            IntPtr manager = _memory.Pointer(_baseAddress + ManagerRva);
            if (manager == IntPtr.Zero) return false;

            IntPtr mapBase = _memory.Pointer(manager);
            if (mapBase == IntPtr.Zero) return false;

            map = mapBase + MapOffset;
            return true;
        }

        /// <summary>
        /// Follows one SNO and row through the item asset container down to
        /// the definition record and reads the name and the set id.
        /// </summary>
        private bool TryResolveDefinition(int sno, int row, int gbid,
                                          out string name, out bool? isSet)
        {
            name = "";
            isSet = null;

            if (sno < 0 || row < 0) return false;

            int snoLimit = BitConverter.ToInt32(
                _memory.Read(_baseAddress + SnoLimitRva, 4), 0);
            if (snoLimit <= 0 || snoLimit > MaxSnoLimit) return false;
            if (sno >= snoLimit) return false;

            IntPtr snoLookup = _memory.Pointer(_baseAddress + SnoLookupRva);
            if (snoLookup == IntPtr.Zero) return false;

            int handle = BitConverter.ToInt32(
                _memory.Read(snoLookup + sno * 4, 4), 0);
            // -1 (and any other negative value) means the SNO has no asset.
            if (handle < 0) return false;

            int slot = handle & SlotMask;

            IntPtr group = _memory.Pointer(_baseAddress + AssetGroupRva);
            if (group == IntPtr.Zero) return false;

            IntPtr container = _memory.Pointer(group + AssetGroupContainerOffset);
            if (container == IntPtr.Zero) return false;

            byte[] containerHeader = _memory.Read(container, ContainerHeaderSize);
            int lastSlot = BitConverter.ToInt32(containerHeader, LastSlotOffset) & SlotMask;
            if (lastSlot >= MaxAssetSlots) return false;
            if (slot > lastSlot) return false;

            IntPtr table = new IntPtr(
                BitConverter.ToInt64(containerHeader, SlotArrayOffset));
            if (table == IntPtr.Zero) return false;

            IntPtr asset = _memory.Pointer(
                table + slot * AssetSlotStride + AssetPointerOffset);
            if (asset == IntPtr.Zero) return false;

            byte[] assetHeader = _memory.Read(asset, AssetHeaderSize);
            // The asset must be the one the SNO table pointed at and must be
            // an item asset, not any other resource type.
            if (BitConverter.ToInt32(assetHeader, AssetSnoOffset) != sno) return false;
            if (BitConverter.ToInt32(assetHeader, AssetTypeOffset) != AssetTypeItem)
                return false;

            int byteLength = BitConverter.ToInt32(assetHeader, AssetByteLengthOffset);
            if (byteLength <= 0) return false;

            int rows = byteLength / DefinitionStride;
            if (rows <= 0 || rows > MaxDefinitionRows) return false;
            if (row >= rows) return false;

            IntPtr rowsPointer = new IntPtr(
                BitConverter.ToInt64(assetHeader, AssetRowsPointerOffset));
            if (rowsPointer == IntPtr.Zero) return false;

            byte[] definition = _memory.Read(
                rowsPointer + row * DefinitionStride, DefinitionSize);
            // The definition must repeat the GBID that led us here.
            if (BitConverter.ToInt32(definition, DefinitionGbidOffset) != gbid)
                return false;

            int setId = BitConverter.ToInt32(definition, DefinitionSetIdOffset);
            bool? set = setId != 0 && setId != -1;

            int nameLength = -1;
            for (int i = 0; i < MaxNameBytes; i++)
            {
                if (definition[i] != 0) continue;
                nameLength = i;
                break;
            }
            // A definition without a terminated name is treated as corrupted.
            if (nameLength < 0) return false;

            name = nameLength == 0
                ? ""
                : Encoding.UTF8.GetString(definition, 0, nameLength);
            isSet = set;
            return true;
        }

        /// <summary>FNV-1a 32-bit over the four little-endian bytes of a GBID.</summary>
        private static uint Fnv1a(int gbid)
        {
            unchecked
            {
                uint hash = 2166136261u;
                uint value = (uint)gbid;
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (byte)(value & 0xFF);
                    hash *= 16777619u;
                    value >>= 8;
                }
                return hash;
            }
        }
    }
}
