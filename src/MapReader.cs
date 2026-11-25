using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace SuperD3Hud
{
    /// <summary>A single navigation cell of a scene asset.</summary>
    public struct NavCell
    {
        public float MinX, MinY, MinZ;
        public float MaxX, MaxY, MaxZ;
        public ushort Flags;

        public NavCell(float minX, float minY, float minZ, float maxX, float maxY, float maxZ, ushort flags)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            Flags = flags;
        }

        /// <summary>Bit 0 marks a walkable cell.</summary>
        public bool Walkable { get { return (Flags & 1) != 0; } }
    }

    /// <summary>A scene currently loaded by the game.</summary>
    public class SceneRecord
    {
        public int Slot;
        public int SceneId;
        public int WorldId;
        public int SceneSno;
        public float[] Bounds;      // minX, minY, maxX, maxY
        public float[] Rotation;    // quaternion x, y, z, w
        public float OriginX, OriginY, OriginZ;
    }

    /// <summary>A scene remembered by the game in its reveal cache.</summary>
    public class CachedScene
    {
        public int SceneSno;
        public int SceneId;
        public int WorldId;
        public float[] Bounds;
    }

    /// <summary>Navigation geometry of one scene asset, addressed by SNO.</summary>
    public class SceneAsset
    {
        public int SceneSno;
        public List<NavCell> Cells = new List<NavCell>();
    }

    /// <summary>Everything read from the game in one capture.</summary>
    public class MapSnapshot
    {
        public int ProcessId;
        public string GameVersion = "";
        public string GameHash = "";
        public DateTime CapturedUtc;
        public List<SceneRecord> Scenes = new List<SceneRecord>();
        public List<CachedScene> CachedScenes = new List<CachedScene>();
        public List<SceneAsset> Assets = new List<SceneAsset>();
        public int[] MissingAssetSnos = new int[0];
        public int PlayerWorldId;
        public string Message = "";
    }

    /// <summary>
    /// Thrown by the reader when the game has no scene data to offer right now:
    /// a loading screen, the main menu or the moment between two worlds. It is
    /// a normal state rather than an error, so callers clear the map instead of
    /// reporting a failure. It derives from InvalidOperationException so
    /// existing catch blocks keep working.
    /// </summary>
    public class MapUnavailableException : InvalidOperationException
    {
        public MapUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Reads the navigation geometry of the running Diablo III client.
    ///
    /// The reader is strictly read-only: it opens the process with
    /// PROCESS_VM_READ, never writes memory, never injects code and never
    /// suspends a thread. Offsets belong to the client build 2.8.0.99920 and
    /// are verified against the SHA-256 of the running executable before a
    /// single byte is interpreted, so a different build fails closed instead
    /// of returning nonsense.
    /// </summary>
    public static class MapReader
    {
        /// <summary>SHA-256 of the client build the offsets were verified on.</summary>
        public const string SupportedHash = "753D5F79D12B035B7268FDB255B5B2502EC858D149B6EBB62969A6E92DD23CC4";
        public const string SupportedVersion = "2.8.0.99920";

        // Global pointers, relative to the loaded module base.
        private const int RootPointerRva = 0x16F5250;      // object manager
        private const int SceneContainerOffset = 0xAA0;    // inside the object manager
        private const int SceneAssetGroupRva = 0x1708970;
        private const int SnoLookupLimitRva = 0x1704C48;
        private const int SnoLookupArrayRva = 0x1704C50;
        private const int AreaPointerRva = 0x1672A68;      // level area with the reveal cache

        // Scene container layout.
        private const int LastSlotOffset = 0x108;
        private const int SlotArrayOffset = 0x120;
        private const int SceneStride = 0x7B8;
        private const int MaxSlots = 8192;

        // Scene record layout.
        private const int SceneIdOffset = 0x08;
        private const int WorldIdOffset = 0x0C;
        private const int SceneSnoOffset = 0xEC;
        private const int RotationOffset = 0xF0;
        // Blizzard PRTransform stores Quaternion { float W, X, Y, Z } at 0xF0..0xFF,
        // followed by Position Vector3 { float X, Y, Z } (MeshMinX/Y/Z) at 0x100..0x10B.
        private const int OriginOffset = 0x100;
        private const int BoundsOffset = 0x170;

        // Scene asset layout.
        private const int AssetSnoOffset = 0x00;
        private const int AssetCellCountOffset = 0x180;
        private const int AssetCellPointerOffset = 0x188;
        private const int AssetCellByteLengthOffset = 0x194;
        private const int AssetSlotStride = 24;
        private const int AssetSlotLimit = 32768;

        // Navigation cell layout.
        private const int CellStride = 32;
        private const int CellFlagsOffset = 24;

        // Reveal cache layout.
        private const int CacheHeadOffset = 0x20;
        private const int CacheTailOffset = 0x28;
        private const int CacheCountOffset = 0x30;
        private const int CacheItemSizeOffset = 0x40;
        private const int CacheMarkerOffset = 0x68;
        private const int CacheMarker = 0x600DF00D;
        private const int CacheNodeSize = 88;

        // Live check: the scene iterator instruction must match the inspected
        // image, otherwise the offsets below cannot be trusted.
        private const int IteratorProbeRva = 0x773AB0;
        private static readonly byte[] IteratorProbe =
            new byte[] { 0x48, 0x8B, 0x15, 0x99, 0x17, 0xF8, 0x00 };

        /// <summary>
        /// Locates the 64-bit client executable of the running game.
        /// </summary>
        public static string FindGameExecutable()
        {
            string configured = AppPaths.Settings.D3Path;
            if (!string.IsNullOrEmpty(configured))
            {
                string found = ResolveExecutable(configured);
                if (!string.IsNullOrEmpty(found)) return found;
            }
            foreach (string dir in AppPaths.GameInstallDirs())
            {
                string exe = ResolveExecutable(dir);
                if (!string.IsNullOrEmpty(exe)) return exe;
            }
            return "";
        }

        private static string ResolveExecutable(string pathOrDir)
        {
            try
            {
                if (string.IsNullOrEmpty(pathOrDir)) return "";
                if (File.Exists(pathOrDir))
                {
                    if (pathOrDir.EndsWith("Diablo III64.exe", StringComparison.OrdinalIgnoreCase))
                        return pathOrDir;
                    string sibling = Path.Combine(Path.GetDirectoryName(pathOrDir), "x64", "Diablo III64.exe");
                    return File.Exists(sibling) ? sibling : "";
                }
                if (Directory.Exists(pathOrDir))
                {
                    string nested = Path.Combine(pathOrDir, "x64", "Diablo III64.exe");
                    if (File.Exists(nested)) return nested;
                    string flat = Path.Combine(pathOrDir, "Diablo III64.exe");
                    if (File.Exists(flat)) return flat;
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Computes the SHA-256 of the client executable.
        /// </summary>
        public static string HashExecutable(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                }
            }
            catch { return ""; }
        }

        // Hashing the client is expensive, and the answer cannot change while
        // the file on disk stays the same, so the result is cached per file
        // identity (path, size, last write time).
        private static readonly object HashSync = new object();
        // Serialises the hashing itself, so two threads that miss the cache at
        // the same time (the map worker and a diagnostic, for example) do not
        // both read the 26 MB executable: the second one waits and reuses the
        // first one's result.
        private static readonly object HashComputeSync = new object();
        private static string _cachedHashPath = "";
        private static long _cachedHashLength = -1;
        private static long _cachedHashWriteTicks = -1;
        private static string _cachedHash = "";
        // A failed hash (the exe is locked by a Battle.net update, a transient
        // IO error) is cached too, with the time of the failure. Without it,
        // every caller re-read the whole file on every call while the problem
        // lasted. A failure is retried no more often than every 30 seconds.
        private static long _cachedHashFailedAt;
        private const long HashRetryTicks = 30L * TimeSpan.TicksPerSecond;

        /// <summary>
        /// Returns the SHA-256 of the client executable, reusing the previous
        /// result while the file is unchanged. Returns "" when the file cannot
        /// be hashed; that failure is remembered for 30 seconds.
        /// </summary>
        public static string HashExecutableCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            long length;
            long ticks;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) return "";
                length = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
            }
            catch { return ""; }

            string cached;
            if (TryGetCachedHash(path, length, ticks, out cached)) return cached;

            lock (HashComputeSync)
            {
                // Another thread may have finished hashing while this one waited.
                if (TryGetCachedHash(path, length, ticks, out cached)) return cached;

                string hash = HashExecutable(path);
                lock (HashSync)
                {
                    _cachedHashPath = path;
                    _cachedHashLength = length;
                    _cachedHashWriteTicks = ticks;
                    _cachedHash = hash;
                    _cachedHashFailedAt = hash.Length == 64 ? 0 : DateTime.UtcNow.Ticks;
                }
                return hash;
            }
        }

        private static bool TryGetCachedHash(string path, long length, long ticks, out string hash)
        {
            hash = "";
            lock (HashSync)
            {
                bool sameFile =
                    string.Equals(_cachedHashPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _cachedHashLength == length &&
                    _cachedHashWriteTicks == ticks;
                if (!sameFile) return false;
                if (_cachedHash.Length == 64)
                {
                    hash = _cachedHash;
                    return true;
                }
                // Same file, last attempt failed: report the failure again until
                // the retry interval has passed.
                return _cachedHashFailedAt != 0 &&
                       DateTime.UtcNow.Ticks - _cachedHashFailedAt < HashRetryTicks;
            }
        }

        /// <summary>
        /// True when the given executable is the build the offsets were verified on.
        /// </summary>
        public static bool IsSupportedBuild(string path)
        {
            string hash = HashExecutableCached(path);
            return !string.IsNullOrEmpty(hash) &&
                   string.Equals(hash, SupportedHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Captures the current map state of the running game.
        /// Throws when the game is not running or the build is not supported.
        /// </summary>
        public static MapSnapshot Capture()
        {
            Process process = FindGameProcess();
            if (process == null)
                throw new InvalidOperationException("Diablo III не запущена.");

            MapSnapshot snapshot = new MapSnapshot();
            try
            {
                ProcessModule module = process.MainModule;
                if (module == null)
                    throw new InvalidOperationException("Не удалось определить модуль игры.");

                snapshot.ProcessId = process.Id;
                snapshot.GameVersion = module.FileVersionInfo.FileVersion ?? "";
                snapshot.GameHash = HashExecutableCached(module.FileName);
                snapshot.CapturedUtc = DateTime.UtcNow;

                if (!string.Equals(snapshot.GameHash, SupportedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Сборка игры " + snapshot.GameVersion + " не поддерживается. " +
                        "Смещения проверены только для " + SupportedVersion + ".");
                }

                using (GameMemoryReader memory = new GameMemoryReader(process.Id))
                {
                    ReadInto(memory, module.BaseAddress, snapshot);
                }

                snapshot.Message = snapshot.Scenes.Count + " активных сцен, " +
                                   snapshot.Assets.Count + " ресурсов геометрии.";
                return snapshot;
            }
            finally
            {
                process.Dispose();
            }
        }

        /// <summary>
        /// Captures the map state through an already open read-only handle.
        /// The caller is responsible for the handle and for having verified
        /// the game build; the iterator probe is still repeated here.
        /// </summary>
        public static MapSnapshot Capture(GameMemoryReader memory, IntPtr baseAddress,
                                          int processId, string gameVersion, string gameHash)
        {
            MapSnapshot snapshot = new MapSnapshot();
            snapshot.ProcessId = processId;
            snapshot.GameVersion = gameVersion ?? "";
            snapshot.GameHash = gameHash ?? "";
            snapshot.CapturedUtc = DateTime.UtcNow;

            if (!string.Equals(snapshot.GameHash, SupportedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Сборка игры " + snapshot.GameVersion + " не поддерживается. " +
                    "Смещения проверены только для " + SupportedVersion + ".");
            }

            ReadInto(memory, baseAddress, snapshot);
            snapshot.Message = snapshot.Scenes.Count + " активных сцен, " +
                               snapshot.Assets.Count + " ресурсов геометрии.";
            return snapshot;
        }

        private static void ReadInto(GameMemoryReader memory, IntPtr baseAddress, MapSnapshot snapshot)
        {
            byte[] probe = memory.Read(baseAddress + IteratorProbeRva, IteratorProbe.Length);
            for (int i = 0; i < IteratorProbe.Length; i++)
            {
                if (probe[i] != IteratorProbe[i])
                    throw new InvalidOperationException(
                        "Итератор сцен в запущенной игре не совпадает с исследованной сборкой.");
            }

            ReadScenes(memory, baseAddress, snapshot);
            ReadRevealCache(memory, baseAddress, snapshot);
            ReadAssets(memory, baseAddress, snapshot);
        }

        private static Process FindGameProcess()
        {
            string[] names = { "Diablo III64", "Diablo III" };
            foreach (string name in names)
            {
                Process[] list;
                try { list = Process.GetProcessesByName(name); }
                catch { continue; }
                Process found = null;
                foreach (Process p in list)
                {
                    if (found == null)
                    {
                        try
                        {
                            if (p.MainModule != null)
                            {
                                found = p;
                                continue;
                            }
                        }
                        catch { }
                    }
                    // Every Process object holds a native handle that is only
                    // released by the finalizer otherwise.
                    p.Dispose();
                }
                if (found != null) return found;
            }
            return null;
        }

        private static void ReadScenes(GameMemoryReader memory, IntPtr baseAddress, MapSnapshot snapshot)
        {
            IntPtr root = memory.Pointer(baseAddress + RootPointerRva);
            if (root == IntPtr.Zero)
                throw new MapUnavailableException("Игровые объекты недоступны в текущем состоянии.");

            IntPtr container = memory.Pointer(root + SceneContainerOffset);
            if (container == IntPtr.Zero)
                throw new MapUnavailableException("Контейнер сцен недоступен в текущем состоянии.");

            byte[] header = memory.Read(container, 0x128);
            ushort lastSlot = BitConverter.ToUInt16(header, LastSlotOffset);
            IntPtr array = new IntPtr(BitConverter.ToInt64(header, SlotArrayOffset));
            if (lastSlot != ushort.MaxValue && (lastSlot >= MaxSlots || array == IntPtr.Zero))
                throw new InvalidOperationException("Границы контейнера сцен не прошли проверку.");

            if (lastSlot == ushort.MaxValue) return;

            for (int slot = 0; slot <= lastSlot; slot++)
            {
                byte[] bytes = memory.Read(array + slot * SceneStride, SceneStride);
                if (BitConverter.ToInt32(bytes, 0) == -1) continue;

                float[] bounds = new float[4];
                for (int i = 0; i < 4; i++)
                    bounds[i] = BitConverter.ToSingle(bytes, BoundsOffset + i * 4);

                // In Blizzard's PRTransform memory layout, the quaternion is stored as standard { X, Y, Z, W }.
                // MapGeometry.Rotate expects standard [qx, qy, qz, qw].
                float qx = BitConverter.ToSingle(bytes, RotationOffset);
                float qy = BitConverter.ToSingle(bytes, RotationOffset + 4);
                float qz = BitConverter.ToSingle(bytes, RotationOffset + 8);
                float qw = BitConverter.ToSingle(bytes, RotationOffset + 12);
                float[] rotation = new float[] { qx, qy, qz, qw };

                if (!ValidBounds(bounds) || !ValidQuaternion(rotation))
                    continue;

                SceneRecord record = new SceneRecord();
                record.Slot = slot;
                record.SceneId = BitConverter.ToInt32(bytes, SceneIdOffset);
                record.WorldId = BitConverter.ToInt32(bytes, WorldIdOffset);
                record.SceneSno = BitConverter.ToInt32(bytes, SceneSnoOffset);
                record.Bounds = bounds;
                record.Rotation = rotation;
                record.OriginX = BitConverter.ToSingle(bytes, OriginOffset);
                record.OriginY = BitConverter.ToSingle(bytes, OriginOffset + 4);
                record.OriginZ = BitConverter.ToSingle(bytes, OriginOffset + 8);
                snapshot.Scenes.Add(record);
            }
        }

        private static void ReadRevealCache(GameMemoryReader memory, IntPtr baseAddress, MapSnapshot snapshot)
        {
            IntPtr area = memory.Pointer(baseAddress + AreaPointerRva);
            if (area == IntPtr.Zero) return;

            byte[] header = memory.Read(area + 0x20, 0x50);
            int count = BitConverter.ToInt32(header, 0x10);
            if (count < 0 || count > 4096) return;
            if (BitConverter.ToInt32(header, 0x20) != CacheNodeSize) return;
            if (BitConverter.ToInt32(header, 0x48) != CacheMarker) return;

            IntPtr head = new IntPtr(BitConverter.ToInt64(header, 0));
            IntPtr tail = new IntPtr(BitConverter.ToInt64(header, 8));
            IntPtr address = head;
            IntPtr previous = IntPtr.Zero;
            HashSet<IntPtr> seen = new HashSet<IntPtr>();

            for (int i = 0; i < count; i++)
            {
                if (address == IntPtr.Zero || !seen.Add(address)) return;

                byte[] record = memory.Read(address, CacheNodeSize);
                if (new IntPtr(BitConverter.ToInt64(record, 72)) != previous) return;

                float[] bounds = new float[4];
                for (int b = 0; b < 4; b++)
                    bounds[b] = BitConverter.ToSingle(record, 16 + b * 4);
                if (!ValidBounds(bounds)) return;

                CachedScene scene = new CachedScene();
                scene.SceneSno = BitConverter.ToInt32(record, 0);
                scene.SceneId = BitConverter.ToInt32(record, 4);
                scene.WorldId = BitConverter.ToInt32(record, 8);
                scene.Bounds = bounds;
                snapshot.CachedScenes.Add(scene);

                previous = address;
                address = new IntPtr(BitConverter.ToInt64(record, 80));
            }
        }

        private static void ReadAssets(GameMemoryReader memory, IntPtr baseAddress, MapSnapshot snapshot)
        {
            IntPtr assetGroup = memory.Pointer(baseAddress + SceneAssetGroupRva);
            if (assetGroup == IntPtr.Zero) return;
            IntPtr assetContainer = memory.Pointer(assetGroup + 0x20);
            if (assetContainer == IntPtr.Zero) return;

            byte[] assetHeader = memory.Read(assetContainer, 0x128);
            int assetLastSlot = BitConverter.ToInt32(assetHeader, LastSlotOffset) & 0x3FFFF;
            if (assetLastSlot >= AssetSlotLimit) return;

            IntPtr assetArray = new IntPtr(BitConverter.ToInt64(assetHeader, SlotArrayOffset));
            if (assetArray == IntPtr.Zero) return;

            byte[] assetTable = memory.Read(assetArray, (assetLastSlot + 1) * AssetSlotStride);

            int snoLimit = BitConverter.ToInt32(memory.Read(baseAddress + SnoLookupLimitRva, 4), 0);
            if (snoLimit <= 0 || snoLimit > 10000000) return;
            IntPtr snoLookup = memory.Pointer(baseAddress + SnoLookupArrayRva);
            if (snoLookup == IntPtr.Zero) return;

            HashSet<int> wanted = new HashSet<int>();
            foreach (SceneRecord scene in snapshot.Scenes) wanted.Add(scene.SceneSno);
            foreach (CachedScene scene in snapshot.CachedScenes) wanted.Add(scene.SceneSno);

            List<int> missing = new List<int>();
            foreach (int sno in wanted)
            {
                if (sno < 0 || sno >= snoLimit) { missing.Add(sno); continue; }

                int handle = BitConverter.ToInt32(memory.Read(snoLookup + sno * 4, 4), 0);
                if (handle == -1) { missing.Add(sno); continue; }

                int index = handle & 0x3FFFF;
                if (index > assetLastSlot) { missing.Add(sno); continue; }
                if (BitConverter.ToInt32(assetTable, index * AssetSlotStride) != handle)
                {
                    missing.Add(sno);
                    continue;
                }

                IntPtr address = new IntPtr(BitConverter.ToInt64(assetTable, index * AssetSlotStride + 16));
                byte[] asset = memory.Read(address, 0x200);
                if (BitConverter.ToInt32(asset, AssetSnoOffset) != sno)
                {
                    missing.Add(sno);
                    continue;
                }

                int cellCount = BitConverter.ToInt32(asset, AssetCellCountOffset);
                int cellByteLength = BitConverter.ToInt32(asset, AssetCellByteLengthOffset);
                if (cellCount < 0 || cellCount > 32768 || cellByteLength != cellCount * CellStride)
                {
                    missing.Add(sno);
                    continue;
                }

                SceneAsset result = new SceneAsset();
                result.SceneSno = sno;

                if (cellCount > 0)
                {
                    IntPtr cellPointer = new IntPtr(BitConverter.ToInt64(asset, AssetCellPointerOffset));
                    byte[] cellData = memory.Read(cellPointer, cellByteLength);
                    for (int c = 0; c < cellCount; c++)
                    {
                        int offset = c * CellStride;
                        NavCell cell = new NavCell(
                            BitConverter.ToSingle(cellData, offset),
                            BitConverter.ToSingle(cellData, offset + 4),
                            BitConverter.ToSingle(cellData, offset + 8),
                            BitConverter.ToSingle(cellData, offset + 12),
                            BitConverter.ToSingle(cellData, offset + 16),
                            BitConverter.ToSingle(cellData, offset + 20),
                            BitConverter.ToUInt16(cellData, offset + CellFlagsOffset));
                        if (cell.MinX > cell.MaxX || cell.MinY > cell.MaxY || cell.MinZ > cell.MaxZ)
                            continue;
                        result.Cells.Add(cell);
                    }
                }
                snapshot.Assets.Add(result);
            }
            snapshot.MissingAssetSnos = missing.ToArray();
        }

        private static bool ValidBounds(float[] bounds)
        {
            for (int i = 0; i < bounds.Length; i++)
            {
                float v = bounds[i];
                if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 1000000f) return false;
            }
            return bounds[0] <= bounds[2] && bounds[1] <= bounds[3];
        }

        private static bool ValidQuaternion(float[] q)
        {
            double sum = 0;
            for (int i = 0; i < q.Length; i++)
            {
                float v = q[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) return false;
                sum += (double)v * v;
            }
            return Math.Abs(sum - 1.0) < 0.01;
        }
    }
}
