using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>Kind of an actor found in the game's actor container.</summary>
    public enum ActorKind
    {
        Unknown = 0,
        Monster = 1,
        Player = 2,
        Item = 3,
        Other = 4
    }

    /// <summary>Rarity of a monster, as stored by the game.</summary>
    public enum MonsterRarity
    {
        Normal = 0,
        Champion = 1,
        RareElite = 2,
        RareMinion = 3,
        Unique = 4,
        Boss = 7
    }

    /// <summary>One actor record, already validated against the container.</summary>
    public struct ActorRecord
    {
        public int Id;
        public int Sno;
        public int Gbid;
        /// <summary>GameBalance handle type: -1 monster, 2 item, 7 hero.</summary>
        public int BalanceType;
        public int WorldId;
        public int OwnerId;
        public int Slot;
        public int GroupId;
        public int Rarity;
        public int Column;
        public int Row;
        public float X;
        public float Y;
        public float Z;
        public IntPtr Address;

        public ActorKind Kind
        {
            get
            {
                if (BalanceType == -1) return ActorKind.Monster;
                if (BalanceType == 2) return ActorKind.Item;
                if (BalanceType == 7) return ActorKind.Player;
                return ActorKind.Other;
            }
        }

        public bool IsMonster { get { return BalanceType == -1; } }
        public bool IsItem { get { return BalanceType == 2; } }
        public bool IsPlayer { get { return BalanceType == 7; } }

        /// <summary>True for champion/rare/unique/boss monsters.</summary>
        public bool IsElite
        {
            get
            {
                if (!IsMonster) return false;
                return Rarity == (int)MonsterRarity.Champion
                    || Rarity == (int)MonsterRarity.RareElite
                    || Rarity == (int)MonsterRarity.Unique
                    || Rarity == (int)MonsterRarity.Boss;
            }
        }

        /// <summary>True when the game marks this record as removed.</summary>
        public bool Deleted { get; set; }
    }

    /// <summary>Attribute identifiers used by the readers.</summary>
    public static class AttributeKeys
    {
        /// <summary>Non-zero when the actor was removed from the world.</summary>
        public const uint Deleted = 0xFFFFF296;
        /// <summary>Current health of the actor.</summary>
        public const uint Health = 0xFFFFF073;
        /// <summary>Item quality; 9 is legendary or set.</summary>
        public const uint ItemQuality = 0xFFFFF17F;
        /// <summary>Ancient rank: 0 normal, 1 ancient, 2 primal.</summary>
        public const uint AncientRank = 0xFFFFF19D;
    }

    /// <summary>
    /// Read-only access to the actor and attribute containers of the running
    /// game.
    ///
    /// Every structure is validated before it is used: bounds, strides,
    /// identity of the first record and the container pointer itself. When a
    /// check fails the reader throws instead of returning plausible nonsense,
    /// so a wrong game build can never produce a silently broken HUD.
    ///
    /// All offsets below belong to the client build 2.8.0.99920 and were
    /// verified against the SHA-256 of the executable by <see cref="MapReader"/>.
    /// </summary>
    public sealed class GameReader
    {
        // Global pointers, relative to the loaded module base.
        private const int RootPointerRva = 0x16F5250;
        private const int ActorContainerOffset = 0x7B8 + 0x198;
        private const int AttributeContainerOffset = 0x7B8 + 0x180;
        private const int AttributeGroupOffset = 0x90;

        // Container layout, shared by actors and attributes.
        private const int LastSlotOffset = 0x108;
        private const int BlockShiftOffset = 0x168;
        private const int SlotArrayOffset = 0x120;

        private const int ActorStride = 0x3C0;
        private const int AttributeStride = 0x12E8;
        private const int MaxActors = 8192;

        // Actor record layout.
        private const int ActorIdOffset = 0x00;
        private const int ActorSnoOffset = 0x90;
        private const int ActorBalanceTypeOffset = 0xB0;
        private const int ActorGbidOffset = 0xB4;
        private const int ActorRarityOffset = 0xB8;
        private const int ActorPositionOffset = 0xD8;
        private const int ActorWorldOffset = 0x118;
        private const int ActorOwnerOffset = 0x120;
        private const int ActorSlotOffset = 0x124;
        private const int ActorColumnOffset = 0x128;
        private const int ActorRowOffset = 0x12C;
        private const int ActorGroupOffset = 0x130;

        private readonly IGameMemoryReader _memory;
        private readonly IntPtr _baseAddress;

        public GameReader(IGameMemoryReader memory, IntPtr baseAddress)
        {
            if (memory == null) throw new ArgumentNullException("memory");
            _memory = memory;
            _baseAddress = baseAddress;
        }

        public IGameMemoryReader Memory { get { return _memory; } }

        public IntPtr BaseAddress { get { return _baseAddress; } }

        // ------------------------------------------------------------------
        // Root and containers
        // ------------------------------------------------------------------

        /// <summary>Pointer to the object manager, or zero when not in game.</summary>
        public IntPtr Root
        {
            get
            {
                try { return _memory.Pointer(_baseAddress + RootPointerRva); }
                catch { return IntPtr.Zero; }
            }
        }

        /// <summary>Pointer to the actor container, or zero.</summary>
        public IntPtr ActorContainer
        {
            get
            {
                try
                {
                    IntPtr root = Root;
                    if (root == IntPtr.Zero) return IntPtr.Zero;

                    // The object manager stores a pointer to a holder, and the
                    // holder points at the container itself. The verified
                    // player chain of the map reader uses the same double
                    // indirection, so a single pointer here silently reads the
                    // holder and fails every validation.
                    IntPtr holder = _memory.Pointer(root + ActorContainerOffset);
                    if (holder == IntPtr.Zero) return IntPtr.Zero;
                    return _memory.Pointer(holder);
                }
                catch { return IntPtr.Zero; }
            }
        }

        /// <summary>Pointer to the attribute container, or zero.</summary>
        public IntPtr AttributeContainer
        {
            get
            {
                try
                {
                    IntPtr root = Root;
                    if (root == IntPtr.Zero) return IntPtr.Zero;
                    IntPtr holder = _memory.Pointer(root + AttributeContainerOffset);
                    if (holder == IntPtr.Zero) return IntPtr.Zero;
                    return _memory.Pointer(holder + AttributeGroupOffset);
                }
                catch { return IntPtr.Zero; }
            }
        }

        /// <summary>
        /// Reads the header of an actor-like container and validates it.
        /// Returns false when the container is empty or not available.
        /// </summary>
        private bool TryReadContainerHeader(IntPtr container, out int last, out int shift, out IntPtr table)
        {
            last = -1;
            shift = 0;
            table = IntPtr.Zero;

            if (container == IntPtr.Zero) return false;

            byte[] header = SmallBuffer();
            _memory.Read(container, header, 0x170);
            int rawLast = BitConverter.ToUInt16(header, LastSlotOffset);
            if (rawLast == 0xFFFF) return false;

            int rawShift = BitConverter.ToInt32(header, BlockShiftOffset);
            IntPtr rawTable = new IntPtr(BitConverter.ToInt64(header, SlotArrayOffset));

            if (rawLast < 0 || rawLast >= MaxActors)
                throw new InvalidOperationException("Границы контейнера актёров не прошли проверку.");
            if (rawShift < 1 || rawShift > 10)
                throw new InvalidOperationException("Сдвиг блоков актёров не прошёл проверку.");
            if (rawTable == IntPtr.Zero)
                throw new InvalidOperationException("Таблица актёров недоступна.");

            last = rawLast;
            shift = rawShift;
            table = rawTable;
            return true;
        }

        // ------------------------------------------------------------------
        // Actors
        // ------------------------------------------------------------------

        /// <summary>
        /// Bytes of an actor record the parser needs: everything up to and
        /// including the attribute group id at 0x130.
        /// </summary>
        private const int ActorRecordReadSize = 0x140;

        // Per-thread scratch buffers. An actor block is up to 1024 * 0x3C0
        // bytes, far above the 85 KB large-object threshold; allocating one
        // per block read used to push the process into a full GC on most
        // frames, which also paused the UI thread. GameReader is used from the
        // GameService worker and from diagnostics, so the buffers are kept per
        // thread instead of per instance to stay safe without locking.
        [ThreadStatic]
        private static byte[] _blockScratch;
        [ThreadStatic]
        private static byte[] _smallScratch;
        [ThreadStatic]
        private static byte[] _bucketScratch;
        [ThreadStatic]
        private static HashSet<IntPtr> _visitedScratch;

        private static byte[] BlockBuffer(int size)
        {
            byte[] buffer = _blockScratch;
            if (buffer == null || buffer.Length < size)
            {
                buffer = new byte[size];
                _blockScratch = buffer;
            }
            return buffer;
        }

        /// <summary>Scratch for headers, records, groups and hash nodes (at most 0x200 bytes).</summary>
        private static byte[] SmallBuffer()
        {
            byte[] buffer = _smallScratch;
            if (buffer == null)
            {
                buffer = new byte[0x200];
                _smallScratch = buffer;
            }
            return buffer;
        }

        /// <summary>Scratch for one attribute bucket array (512 pointers).</summary>
        private static byte[] BucketBuffer()
        {
            byte[] buffer = _bucketScratch;
            if (buffer == null)
            {
                buffer = new byte[512 * 8];
                _bucketScratch = buffer;
            }
            return buffer;
        }

        private static HashSet<IntPtr> VisitedSet()
        {
            HashSet<IntPtr> set = _visitedScratch;
            if (set == null)
            {
                set = new HashSet<IntPtr>();
                _visitedScratch = set;
            }
            set.Clear();
            return set;
        }

        /// <summary>
        /// Decodes one actor record at <paramref name="offset"/>. Returns false
        /// when the position is not a number, which marks a half-written or
        /// garbage record.
        /// </summary>
        private static bool ParseRecord(byte[] bytes, int offset, int id, IntPtr address, out ActorRecord record)
        {
            record = new ActorRecord();
            record.Id = id;
            record.Address = address;
            record.BalanceType = BitConverter.ToInt32(bytes, offset + ActorBalanceTypeOffset);
            record.Sno = BitConverter.ToInt32(bytes, offset + ActorSnoOffset);
            record.Gbid = BitConverter.ToInt32(bytes, offset + ActorGbidOffset);
            record.Rarity = BitConverter.ToInt32(bytes, offset + ActorRarityOffset);
            record.WorldId = BitConverter.ToInt32(bytes, offset + ActorWorldOffset);
            record.OwnerId = BitConverter.ToInt32(bytes, offset + ActorOwnerOffset);
            record.Slot = BitConverter.ToInt32(bytes, offset + ActorSlotOffset);
            record.Column = BitConverter.ToInt32(bytes, offset + ActorColumnOffset);
            record.Row = BitConverter.ToInt32(bytes, offset + ActorRowOffset);
            record.GroupId = BitConverter.ToInt32(bytes, offset + ActorGroupOffset);
            record.X = BitConverter.ToSingle(bytes, offset + ActorPositionOffset);
            record.Y = BitConverter.ToSingle(bytes, offset + ActorPositionOffset + 4);
            record.Z = BitConverter.ToSingle(bytes, offset + ActorPositionOffset + 8);

            if (float.IsNaN(record.X) || float.IsInfinity(record.X)) return false;
            if (float.IsNaN(record.Y) || float.IsInfinity(record.Y)) return false;
            return true;
        }

        /// <summary>
        /// Reads every valid actor record of the current world.
        ///
        /// The list is validated three times: each record identity, the block
        /// chain and finally the container pointer, so a capture that races a
        /// world change is rejected instead of mixed together.
        ///
        /// This is the expensive read (one block of up to 1024 records per
        /// call), so callers read it once per poll and share the list.
        /// </summary>
        public List<ActorRecord> ReadActors()
        {
            List<ActorRecord> result = new List<ActorRecord>();

            IntPtr container = ActorContainer;
            int last, shift;
            IntPtr table;
            if (!TryReadContainerHeader(container, out last, out shift, out table))
                return result;

            int blockSize = 1 << shift;
            byte[] bytes = BlockBuffer(Math.Min(blockSize, last + 1) * ActorStride);
            for (int first = 0; first <= last; first += blockSize)
            {
                IntPtr block = _memory.Pointer(table + (first >> shift) * 8);
                if (block == IntPtr.Zero)
                    throw new InvalidOperationException("Блок актёров недоступен.");

                int count = Math.Min(blockSize, last - first + 1);
                _memory.Read(block, bytes, count * ActorStride);

                for (int index = 0; index < count; index++)
                {
                    int offset = index * ActorStride;
                    int id = BitConverter.ToInt32(bytes, offset + ActorIdOffset);
                    if (id == -1) continue;
                    if ((id & 0xFFFF) != first + index) continue;

                    ActorRecord record;
                    if (!ParseRecord(bytes, offset, id, block + offset, out record)) continue;
                    result.Add(record);
                }
            }

            // The container must not have moved while it was being read.
            if (_memory.Pointer(table) == IntPtr.Zero && last > 0)
                throw new InvalidOperationException("Таблица актёров изменилась во время чтения.");
            if (ActorContainer != container)
                throw new InvalidOperationException("Контейнер актёров изменился во время чтения.");

            return result;
        }

        /// <summary>
        /// Finds the local player actor. The player slot is stored in the
        /// object manager, and the actor id comes from the player table.
        /// </summary>
        public bool TryReadLocalPlayer(out ActorRecord player)
        {
            int slot;
            return TryReadPlayerDirect(out player, out slot);
        }

        /// <summary>Same as <see cref="TryReadLocalPlayer(out ActorRecord)"/>, also returning the player slot.</summary>
        public bool TryReadLocalPlayer(out ActorRecord player, out int slot)
        {
            return TryReadPlayerDirect(out player, out slot);
        }

        /// <summary>
        /// Reads the local player's record straight from its container slot,
        /// the way the map's PlayerReader does: block = table[index &gt;&gt; shift],
        /// record = block + (index &amp; mask) * stride, then the id is checked.
        ///
        /// The old lookup scanned the whole actor container to find one id. It
        /// ran several times per 25 ms cycle, which in a dense area tripped the
        /// read-rate guard and blanked buffs, health and monsters for the rest
        /// of the second. This costs a handful of small reads instead. The
        /// result is identical: the scan accepted exactly the record whose
        /// slot index matches the low 16 bits of its id.
        /// </summary>
        public bool TryReadPlayerDirect(out ActorRecord player, out int slot)
        {
            player = new ActorRecord();
            slot = -1;

            IntPtr root = Root;
            if (root == IntPtr.Zero) return false;

            IntPtr singleton = _memory.Pointer(root + 0xB28);
            if (singleton == IntPtr.Zero) return false;

            byte[] small = SmallBuffer();
            _memory.Read(singleton, small, 4);
            int playerSlot = BitConverter.ToInt32(small, 0);
            if (playerSlot < 0 || playerSlot > 3) return false;

            IntPtr manager = _memory.Pointer(root + 0x7B8 + 0x140);
            if (manager == IntPtr.Zero) return false;

            _memory.Read(manager + 0x60 + playerSlot * 0xE7F0 + 4, small, 4);
            int actorId = BitConverter.ToInt32(small, 0);
            if (actorId == -1) return false;

            IntPtr holder = _memory.Pointer(root + ActorContainerOffset);
            if (holder == IntPtr.Zero) return false;
            IntPtr container = _memory.Pointer(holder);

            int last, shift;
            IntPtr table;
            if (!TryReadContainerHeader(container, out last, out shift, out table))
                return false;

            int index = actorId & 0xFFFF;
            if (index > last) return false;

            IntPtr block = _memory.Pointer(table + (index >> shift) * 8);
            if (block == IntPtr.Zero) return false;

            IntPtr address = block + (index & ((1 << shift) - 1)) * ActorStride;
            _memory.Read(address, small, ActorRecordReadSize);
            if (BitConverter.ToInt32(small, ActorIdOffset) != actorId) return false;

            ActorRecord record;
            if (!ParseRecord(small, 0, actorId, address, out record)) return false;

            player = record;
            slot = playerSlot;
            return true;
        }

        /// <summary>
        /// Reads the internal game tick counter (60 ticks/second) directly from root + 0x20.
        /// </summary>
        public int ReadGameTick()
        {
            try
            {
                IntPtr root = Root;
                if (root == IntPtr.Zero) return 0;
                byte[] small = SmallBuffer();
                _memory.Read(root + 0x20, small, 4);
                return BitConverter.ToInt32(small, 0);
            }
            catch { return 0; }
        }

        public struct EquippedSkill
        {
            public int PowerSno;
            public int Rune;
        }

        /// <summary>
        /// Reads the 6 skill slots (LMB, RMB, 1..4) from the local player's entry.
        /// Returns an array of 6 elements.
        /// </summary>
        public EquippedSkill[] ReadEquippedSkills()
        {
            EquippedSkill[] result = new EquippedSkill[6];
            try
            {
                IntPtr root = Root;
                if (root == IntPtr.Zero) return result;

                IntPtr singleton = _memory.Pointer(root + 0xB28);
                if (singleton == IntPtr.Zero) return result;

                byte[] bytes = SmallBuffer();
                _memory.Read(singleton, bytes, 4);
                int slot = BitConverter.ToInt32(bytes, 0);
                if (slot < 0 || slot > 3) return result;

                IntPtr manager = _memory.Pointer(root + 0x7B8 + 0x140);
                if (manager == IntPtr.Zero) return result;

                // The six slots are 0x10 apart; one read of the whole range
                // replaces six separate 8-byte reads.
                IntPtr entry = manager + 0x60 + slot * 0xE7F0;
                _memory.Read(entry + 0x134, bytes, 5 * 0x10 + 8);
                for (int i = 0; i < 6; i++)
                {
                    result[i].PowerSno = BitConverter.ToInt32(bytes, i * 0x10);
                    result[i].Rune = BitConverter.ToInt32(bytes, i * 0x10 + 4);
                }
            }
            catch { }
            return result;
        }

        /// <summary>Slot index of the local player, or -1.</summary>
        public int LocalPlayerSlot
        {
            get
            {
                try
                {
                    IntPtr root = Root;
                    if (root == IntPtr.Zero) return -1;
                    IntPtr singleton = _memory.Pointer(root + 0xB28);
                    if (singleton == IntPtr.Zero) return -1;
                    int slot = BitConverter.ToInt32(_memory.Read(singleton, 4), 0);
                    if (slot < 0 || slot > 3) return -1;
                    return slot;
                }
                catch { return -1; }
            }
        }

        // ------------------------------------------------------------------
        // Attributes
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads the attribute container header once, so a poll that looks up
        /// attributes of many actors does not repeat the three-pointer chain
        /// and the 0x170-byte header for every lookup. Returns null when the
        /// container is not available (menu, loading).
        ///
        /// The table is meant to live for one poll only: block pointers are
        /// cached inside it, and a group added after it was opened is simply
        /// reported as missing until the next poll.
        /// </summary>
        public GameAttributeTable OpenAttributes()
        {
            IntPtr container = AttributeContainer;
            if (container == IntPtr.Zero) return null;

            byte[] header = SmallBuffer();
            _memory.Read(container, header, 0x170);
            int rawLast = BitConverter.ToUInt16(header, LastSlotOffset);
            if (rawLast == 0xFFFF) return null;

            int shift = BitConverter.ToInt32(header, BlockShiftOffset);
            if (shift < 1 || shift > 16)
                throw new InvalidOperationException("Сдвиг контейнера атрибутов не прошёл проверку.");

            IntPtr table = new IntPtr(BitConverter.ToInt64(header, SlotArrayOffset));
            if (table == IntPtr.Zero) return null;

            return new GameAttributeTable(container, rawLast, shift, table);
        }

        /// <summary>
        /// Finds the hash buckets of one attribute group.
        ///
        /// With <paramref name="strict"/> a group outside the container or a
        /// group whose identity changed throws, which is the historical
        /// behaviour of <see cref="ReadAttribute"/>. Without it both cases just
        /// return false: in a crowded fight monsters die all the time and their
        /// groups are freed or reused between the actor read and the attribute
        /// read, and one such actor must not abort the whole frame.
        /// </summary>
        private bool TryResolveGroup(GameAttributeTable attributes, int groupId, bool strict,
                                     out IntPtr buckets, out int bucketCount)
        {
            buckets = IntPtr.Zero;
            bucketCount = 0;
            if (attributes == null || groupId == -1) return false;

            int index = groupId & 0xFFFF;
            if (index > attributes.Last)
            {
                if (strict) throw new InvalidOperationException("Группа атрибутов вне контейнера.");
                return false;
            }

            IntPtr block = attributes.Block(_memory, index >> attributes.Shift);
            if (block == IntPtr.Zero) return false;

            IntPtr group = block + (index & ((1 << attributes.Shift) - 1)) * AttributeStride;
            byte[] data = SmallBuffer();
            _memory.Read(group, data, 0x30);
            if (BitConverter.ToInt32(data, 0) != groupId)
            {
                if (strict) throw new InvalidOperationException("Идентичность группы атрибутов изменилась.");
                return false;
            }

            int flags = BitConverter.ToInt32(data, 4);
            bool external = (flags & 4) != 0;

            // The group header was just read, so the inline bucket pointer at
            // +0x28 comes from it; only external groups need one more hop.
            if (external)
            {
                IntPtr outer = new IntPtr(BitConverter.ToInt64(data, 0x10));
                if (outer == IntPtr.Zero) return false;
                buckets = _memory.Pointer(outer + 0x10);
                bucketCount = 512;
            }
            else
            {
                buckets = new IntPtr(BitConverter.ToInt64(data, 0x28));
                bucketCount = 256;
            }
            return buckets != IntPtr.Zero;
        }

        /// <summary>
        /// Walks one hash chain. The chain is bounded at 128 nodes, which also
        /// catches a cycle. Strict mode throws on a too-long chain; otherwise
        /// the value is reported as missing.
        /// </summary>
        private uint? LookupInBuckets(IntPtr buckets, int bucketCount, uint key, bool strict)
        {
            uint mask = (uint)(bucketCount - 1);
            int bucket = (int)((unchecked((uint)((int)key >> 12)) ^ key) & mask);
            IntPtr node = _memory.Pointer(buckets + bucket * 8);

            byte[] entry = SmallBuffer();
            int steps = 0;
            while (node != IntPtr.Zero)
            {
                if (++steps > 128)
                {
                    if (strict) throw new InvalidOperationException("Цепочка атрибутов повреждена.");
                    return null;
                }

                _memory.Read(node, entry, 16);
                if (BitConverter.ToUInt32(entry, 8) == key)
                    return BitConverter.ToUInt32(entry, 12);
                node = new IntPtr(BitConverter.ToInt64(entry, 0));
            }
            return null;
        }

        /// <summary>
        /// Reads one attribute of one actor group.
        ///
        /// The game stores attributes in a hash table per group, with two
        /// layouts: "external" groups keep the buckets behind one more pointer.
        /// Both are supported, and the hash chain is bounded and cycle checked.
        /// Throws when the group is outside the container, changed identity or
        /// has a corrupted chain; use <see cref="TryReadAttribute(int, uint)"/>
        /// in per-actor loops.
        /// </summary>
        public uint? ReadAttribute(int groupId, uint key)
        {
            if (groupId == -1) return null;
            GameAttributeTable attributes = OpenAttributes();
            IntPtr buckets;
            int bucketCount;
            if (!TryResolveGroup(attributes, groupId, true, out buckets, out bucketCount)) return null;
            return LookupInBuckets(buckets, bucketCount, key, true);
        }

        /// <summary>
        /// Like <see cref="ReadAttribute"/>, but a vanished, reused or corrupted
        /// group returns null instead of throwing. Memory read failures still
        /// throw, because they mean the whole session is in trouble.
        /// </summary>
        public uint? TryReadAttribute(int groupId, uint key)
        {
            if (groupId == -1) return null;
            return TryReadAttribute(OpenAttributes(), groupId, key);
        }

        /// <summary>Non-throwing lookup through a table opened once per poll.</summary>
        public uint? TryReadAttribute(GameAttributeTable attributes, int groupId, uint key)
        {
            IntPtr buckets;
            int bucketCount;
            if (!TryResolveGroup(attributes, groupId, false, out buckets, out bucketCount)) return null;
            return LookupInBuckets(buckets, bucketCount, key, false);
        }

        /// <summary>
        /// Looks up several keys of one group, resolving the group only once.
        /// <paramref name="values"/> receives one entry per key (null when the
        /// key is absent). Returns false, with every value null, when the group
        /// itself is gone.
        /// </summary>
        public bool TryReadAttributes(GameAttributeTable attributes, int groupId, uint[] keys, uint?[] values)
        {
            if (keys == null || values == null || values.Length < keys.Length)
                throw new ArgumentException("keys/values");
            for (int i = 0; i < keys.Length; i++) values[i] = null;

            IntPtr buckets;
            int bucketCount;
            if (!TryResolveGroup(attributes, groupId, false, out buckets, out bucketCount)) return false;
            for (int i = 0; i < keys.Length; i++)
                values[i] = LookupInBuckets(buckets, bucketCount, keys[i], false);
            return true;
        }

        /// <summary>Decodes a raw health attribute; null for NaN or infinity.</summary>
        public static float? DecodeHealth(uint? raw)
        {
            if (raw == null) return null;
            float value = BitConverter.ToSingle(BitConverter.GetBytes(raw.Value), 0);
            if (float.IsNaN(value) || float.IsInfinity(value)) return null;
            return value;
        }

        /// <summary>
        /// Finds the health value in an attribute list that was already read
        /// with <see cref="ReadAllAttributes(int)"/>, so the player's health
        /// does not need a second lookup. Null when the list has no health.
        /// </summary>
        public static float? FindHealth(List<KeyValuePair<uint, uint>> attributes)
        {
            if (attributes == null) return null;
            for (int i = 0; i < attributes.Count; i++)
            {
                if (attributes[i].Key == AttributeKeys.Health)
                    return DecodeHealth(attributes[i].Value);
            }
            return null;
        }

        /// <summary>Current health of an actor, or null when unknown.</summary>
        public float? ReadHealth(int groupId)
        {
            return DecodeHealth(ReadAttribute(groupId, AttributeKeys.Health));
        }

        /// <summary>True when the actor was removed from the world.</summary>
        public bool IsDeleted(int groupId)
        {
            uint? raw = ReadAttribute(groupId, AttributeKeys.Deleted);
            return raw.HasValue && raw.Value > 0;
        }

        /// <summary>
        /// Enumerates every attribute of one actor group.
        ///
        /// The hash table is walked bucket by bucket with a bounded chain and a
        /// cycle check, so a corrupted container throws instead of hanging.
        /// This is what lets the HUD list buffs without a hard-coded power
        /// catalogue: the game stores one attribute per active buff icon.
        /// </summary>
        public List<KeyValuePair<uint, uint>> ReadAllAttributes(int groupId)
        {
            if (groupId == -1) return new List<KeyValuePair<uint, uint>>();
            return ReadAllAttributes(OpenAttributes(), groupId);
        }

        /// <summary>
        /// Same walk through a table opened once per poll. The bucket array
        /// (256 or 512 pointers) is read in one call instead of one call per
        /// bucket, which turned roughly 950 syscalls per walk into about 430.
        /// </summary>
        public List<KeyValuePair<uint, uint>> ReadAllAttributes(GameAttributeTable attributes, int groupId)
        {
            List<KeyValuePair<uint, uint>> result = new List<KeyValuePair<uint, uint>>();

            IntPtr buckets;
            int bucketCount;
            if (!TryResolveGroup(attributes, groupId, false, out buckets, out bucketCount)) return result;

            byte[] bucketBytes = BucketBuffer();
            _memory.Read(buckets, bucketBytes, bucketCount * 8);

            HashSet<IntPtr> visited = VisitedSet();
            byte[] entry = SmallBuffer();
            int guard = 0;

            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                IntPtr node = new IntPtr(BitConverter.ToInt64(bucketBytes, bucket * 8));
                while (node != IntPtr.Zero)
                {
                    if (++guard > 4096) return result;
                    if (!visited.Add(node))
                        throw new InvalidOperationException("Цепочка атрибутов повреждена.");

                    _memory.Read(node, entry, 16);
                    result.Add(new KeyValuePair<uint, uint>(
                        BitConverter.ToUInt32(entry, 8),
                        BitConverter.ToUInt32(entry, 12)));

                    node = new IntPtr(BitConverter.ToInt64(entry, 0));
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Attribute container header captured once per poll; see
    /// <see cref="GameReader.OpenAttributes"/>. Not thread-safe: each poll
    /// opens its own.
    /// </summary>
    public sealed class GameAttributeTable
    {
        private readonly Dictionary<int, IntPtr> _blocks = new Dictionary<int, IntPtr>();

        internal GameAttributeTable(IntPtr container, int last, int shift, IntPtr table)
        {
            Container = container;
            Last = last;
            Shift = shift;
            Table = table;
        }

        public IntPtr Container { get; private set; }
        public int Last { get; private set; }
        public int Shift { get; private set; }
        public IntPtr Table { get; private set; }

        /// <summary>Block pointer for a block index, read once per table.</summary>
        internal IntPtr Block(IGameMemoryReader memory, int blockIndex)
        {
            IntPtr block;
            if (_blocks.TryGetValue(blockIndex, out block)) return block;
            block = memory.Pointer(Table + blockIndex * 8);
            // A zero pointer is not cached: the block may be allocated by the
            // next lookup, and a miss is cheap.
            if (block != IntPtr.Zero) _blocks[blockIndex] = block;
            return block;
        }
    }
}
