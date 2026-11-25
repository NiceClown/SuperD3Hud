using System;
using System.Collections.Generic;

namespace SuperD3Hud
{
    /// <summary>A point in the isometric map plane.</summary>
    public struct MapPoint
    {
        public double X;
        public double Y;

        public MapPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static MapPoint operator -(MapPoint a, MapPoint b)
        {
            return new MapPoint(a.X - b.X, a.Y - b.Y);
        }
    }

    /// <summary>A walkable polygon in the map plane.</summary>
    public class MapPolygon
    {
        public MapPoint[] Points;
        public bool CachedFootprint;
        /// <summary>
        /// Kept for snapshot compatibility; <see cref="MapGeometry.Build"/>
        /// emits only exact walkable navigation cells and never approximate bounds.
        /// </summary>
        public bool Approximate;
    }

    /// <summary>
    /// Turns the raw scene data into drawable geometry.
    ///
    /// The game stores a scene as a set of navigation cells in local space.
    /// Each cell is rotated by the scene quaternion, moved by the scene mesh
    /// origin (MeshMinX/MeshMinY)
    /// and finally projected into the isometric plane the minimap uses.
    /// </summary>
    public static class MapGeometry
    {
        private const double Diagonal = 0.7071067811865476;   // 1 / sqrt(2)

        /// <summary>
        /// Projects a world point into the map plane used for drawing.
        ///
        /// The game minimap rotates the world by 45 degrees. The standalone
        /// panel uses the same rotation but compresses the vertical axis by
        /// half, which makes the result twice as wide as it is high.
        /// </summary>
        public static MapPoint Project(double x, double y)
        {
            return new MapPoint((y - x) * Diagonal, (x + y) * Diagonal * 0.5);
        }

        /// <summary>
        /// Converts a map point into the coordinate system of the native
        /// minimap of the game: the same rotation without the compression.
        /// </summary>
        public static MapPoint ToNative(MapPoint p)
        {
            return new MapPoint(p.X, p.Y * 2.0);
        }

        /// <summary>
        /// Rotates a vector by a unit quaternion given as x, y, z, w.
        /// </summary>
        public static void Rotate(float[] q, float x, float y, float z,
                                  out float rx, out float ry, out float rz)
        {
            double qx = q[0], qy = q[1], qz = q[2], qw = q[3];

            // t = 2 * (q.xyz x v)
            double tx = 2.0 * (qy * z - qz * y);
            double ty = 2.0 * (qz * x - qx * z);
            double tz = 2.0 * (qx * y - qy * x);

            rx = (float)(x + qw * tx + (qy * tz - qz * ty));
            ry = (float)(y + qw * ty + (qz * tx - qx * tz));
            rz = (float)(z + qw * tz + (qx * ty - qy * tx));
        }

        /// <summary>
        /// Builds the polygons of every walkable cell of the current world.
        /// </summary>
        public static List<MapPolygon> Build(MapSnapshot snapshot)
        {
            List<MapPolygon> polygons = new List<MapPolygon>();
            if (snapshot == null || snapshot.Scenes.Count == 0) return polygons;

            Dictionary<int, int> sceneCountByWorld = new Dictionary<int, int>();
            foreach (SceneRecord scene in snapshot.Scenes)
            {
                int count;
                sceneCountByWorld.TryGetValue(scene.WorldId, out count);
                sceneCountByWorld[scene.WorldId] = count + 1;
            }
            if (sceneCountByWorld.Count == 0) return polygons;

            int targetWorld = -1;
            if (snapshot.PlayerWorldId != 0 && sceneCountByWorld.ContainsKey(snapshot.PlayerWorldId))
            {
                targetWorld = snapshot.PlayerWorldId;
            }
            else if (sceneCountByWorld.Count == 1)
            {
                foreach (int w in sceneCountByWorld.Keys) { targetWorld = w; break; }
            }
            else
            {
                // When player world is unknown and multiple worlds are present, the world is unstable.
                return polygons;
            }

            Dictionary<int, SceneAsset> assets = new Dictionary<int, SceneAsset>();
            foreach (SceneAsset asset in snapshot.Assets) assets[asset.SceneSno] = asset;

            // The scenes the game keeps loaded: their navmesh is placed with
            // the transform of the scene itself.
            foreach (SceneRecord scene in snapshot.Scenes)
            {
                if (scene.WorldId != targetWorld) continue;

                SceneAsset asset;
                if (!assets.TryGetValue(scene.SceneSno, out asset) || asset.Cells.Count == 0)
                {
                    // Skip scenes whose navmesh asset is not loaded yet rather than
                    // emitting a fake rectangular bounding-box fallback.
                    continue;
                }

                foreach (NavCell cell in asset.Cells)
                {
                    if (!cell.Walkable) continue;

                    MapPoint[] corners = new MapPoint[4];
                    corners[0] = Transform(scene, cell.MinX, cell.MinY, cell.MinZ);
                    corners[1] = Transform(scene, cell.MaxX, cell.MinY, cell.MinZ);
                    corners[2] = Transform(scene, cell.MaxX, cell.MaxY, cell.MinZ);
                    corners[3] = Transform(scene, cell.MinX, cell.MaxY, cell.MinZ);

                    polygons.Add(new MapPolygon { Points = corners, CachedFootprint = false });
                }
            }

            // The scenes the game remembers as revealed but does not keep
            // loaded any more. They are the rooms the hero has already
            // walked through, and their navmesh is still in memory, so the
            // floor of the whole level can be drawn from it.
            foreach (CachedScene cached in snapshot.CachedScenes)
            {
                if (cached.WorldId != targetWorld) continue;
                if (!ValidBounds(cached.Bounds)) continue;
                if (AlreadyLoaded(snapshot.Scenes, targetWorld, cached)) continue;

                SceneAsset asset;
                if (!assets.TryGetValue(cached.SceneSno, out asset) || asset.Cells.Count == 0)
                {
                    // Skip cached scenes without an exact navmesh asset instead of
                    // emitting an approximate bounding rectangle.
                    continue;
                }

                // The cells of the asset are in the local space of the scene.
                // The world bounds tell where the scene lies: the mesh starts
                // at the corner of the bounds and is not turned.
                double lminX = double.MaxValue, lminY = double.MaxValue;
                foreach (NavCell cell in asset.Cells)
                {
                    if (cell.MinX < lminX) lminX = cell.MinX;
                    if (cell.MinY < lminY) lminY = cell.MinY;
                }

                SceneRecord scene = new SceneRecord();
                scene.SceneSno = cached.SceneSno;
                scene.WorldId = cached.WorldId;
                scene.Bounds = cached.Bounds;
                scene.Rotation = new float[] { 0f, 0f, 0f, 1f };
                scene.OriginX = (float)(cached.Bounds[0] - lminX);
                scene.OriginY = (float)(cached.Bounds[1] - lminY);

                foreach (NavCell cell in asset.Cells)
                {
                    if (!cell.Walkable) continue;

                    MapPoint[] corners = new MapPoint[4];
                    corners[0] = Transform(scene, cell.MinX, cell.MinY, cell.MinZ);
                    corners[1] = Transform(scene, cell.MaxX, cell.MinY, cell.MinZ);
                    corners[2] = Transform(scene, cell.MaxX, cell.MaxY, cell.MinZ);
                    corners[3] = Transform(scene, cell.MinX, cell.MaxY, cell.MinZ);

                    polygons.Add(new MapPolygon { Points = corners, CachedFootprint = false });
                }
            }

            return polygons;
        }

        /// <summary>
        /// True when a scene of the game is still loaded, so the same piece of
        /// the level must not be drawn a second time from the memory of the
        /// revealed areas.
        /// </summary>
        private static bool AlreadyLoaded(List<SceneRecord> scenes, int worldId, CachedScene cached)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                SceneRecord scene = scenes[i];
                if (scene.WorldId != worldId) continue;
                if (!ValidBounds(scene.Bounds)) continue;
                if (Math.Abs(scene.Bounds[0] - cached.Bounds[0]) > 1f) continue;
                if (Math.Abs(scene.Bounds[1] - cached.Bounds[1]) > 1f) continue;
                if (Math.Abs(scene.Bounds[2] - cached.Bounds[2]) > 1f) continue;
                if (Math.Abs(scene.Bounds[3] - cached.Bounds[3]) > 1f) continue;
                return true;
            }
            return false;
        }

        private static MapPoint[] BoundsPolygon(float[] bounds)
        {
            return new MapPoint[]
            {
                Project(bounds[0], bounds[1]),
                Project(bounds[2], bounds[1]),
                Project(bounds[2], bounds[3]),
                Project(bounds[0], bounds[3])
            };
        }

        private static bool ValidBounds(float[] bounds)
        {
            if (bounds == null || bounds.Length < 4) return false;
            for (int i = 0; i < 4; i++)
            {
                if (float.IsNaN(bounds[i]) || float.IsInfinity(bounds[i])) return false;
                if (Math.Abs(bounds[i]) > 1000000f) return false;
            }
            return bounds[0] < bounds[2] && bounds[1] < bounds[3];
        }

        private static bool Overlaps(float[] a, float[] b)
        {
            if (!ValidBounds(a) || !ValidBounds(b)) return false;
            return a[0] < b[2] && a[2] > b[0] &&
                   a[1] < b[3] && a[3] > b[1];
        }

        private static MapPoint Transform(SceneRecord scene, float x, float y, float z)
        {
            float rx, ry, rz;
            Rotate(scene.Rotation, x, y, z, out rx, out ry, out rz);
            return Project(rx + scene.OriginX, ry + scene.OriginY);
        }
    }

    /// <summary>
    /// Reads the position of the local player so the map can follow them.
    /// </summary>
    public static class PlayerReader
    {
        // Offsets of the local player chain, verified on build 2.8.0.99920.
        private const int RootPointerRva = 0x16F5250;
        private const int PlayerSingletonOffset = 0xB28;
        private const int PlayerDataOffset = 0x7B8;
        private const int PlayerManagerPointerOffset = 0x140;
        private const int PlayerEntryOffset = 0x60;
        private const int PlayerEntryStride = 0xE7F0;
        private const int ActorIdOffset = 4;
        private const int ActorContainerOffset = 0x198;
        private const int ActorBlockShiftOffset = 0x168;
        private const int ActorBlockTableOffset = 0x120;
        private const int ActorRecordStride = 0x3C0;
        private const int ActorWorldIdOffset = 0x118;
        private const int ActorPositionOffset = 0xD8;

        /// <summary>
        /// Returns the world position of the local player, or false when the
        /// player cannot be identified in the current game state.
        /// </summary>
        public static bool TryRead(GameMemoryReader memory, IntPtr baseAddress,
                                   MapSnapshot snapshot, out double x, out double y)
        {
            x = 0;
            y = 0;

            IntPtr root = memory.Pointer(baseAddress + RootPointerRva);
            if (root == IntPtr.Zero) return false;

            IntPtr player = memory.Pointer(root + PlayerSingletonOffset);
            if (player == IntPtr.Zero) return false;

            int index = BitConverter.ToInt32(memory.Read(player, 4), 0);
            if (index < 0 || index > 3) return false;

            IntPtr manager = memory.Pointer(root + PlayerDataOffset + PlayerManagerPointerOffset);
            if (manager == IntPtr.Zero) return false;

            int actorId = BitConverter.ToInt32(
                memory.Read(manager + PlayerEntryOffset + index * PlayerEntryStride + ActorIdOffset, 4), 0);
            if (actorId == -1) return false;

            IntPtr container = memory.Pointer(memory.Pointer(root + PlayerDataOffset + ActorContainerOffset));
            if (container == IntPtr.Zero) return false;

            int shift = BitConverter.ToInt32(memory.Read(container + ActorBlockShiftOffset, 4), 0);
            if (shift < 1 || shift > 16) return false;

            IntPtr table = memory.Pointer(container + ActorBlockTableOffset);
            if (table == IntPtr.Zero) return false;

            int low = actorId & 0xFFFF;
            IntPtr block = memory.Pointer(table + (low >> shift) * 8);
            if (block == IntPtr.Zero) return false;

            IntPtr record = block + (low & ((1 << shift) - 1)) * ActorRecordStride;
            byte[] data = memory.Read(record, 0x120);
            if (BitConverter.ToInt32(data, 0) != actorId) return false;

            int world = BitConverter.ToInt32(data, ActorWorldIdOffset);
            if (snapshot != null) snapshot.PlayerWorldId = world;

            float fx = BitConverter.ToSingle(data, ActorPositionOffset);
            float fy = BitConverter.ToSingle(data, ActorPositionOffset + 4);
            if (float.IsNaN(fx) || float.IsNaN(fy) || float.IsInfinity(fx) || float.IsInfinity(fy))
                return false;
            x = fx;
            y = fy;
            return true;
        }

        private static bool BelongsToSnapshot(int world, MapSnapshot snapshot)
        {
            if (snapshot == null) return false;
            foreach (SceneRecord scene in snapshot.Scenes)
                if (scene.WorldId == world) return true;
            return false;
        }
    }
}
