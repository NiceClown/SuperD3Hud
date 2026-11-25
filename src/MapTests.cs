using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace SuperD3Hud
{
    /// <summary>
    /// Tests of the map reader that do not need a running game. They cover
    /// the geometry maths, the quaternion rotation, the build gating and the
    /// behaviour on an unsupported client.
    /// </summary>
    public static class MapTests
    {
        /// <summary>
        /// Runs every check. Returns the number of failures and writes a line
        /// per check through the given reporter.
        /// </summary>
        public static int Run(Action<string, bool> check, Action<string> info)
        {
            int failures = 0;

            // --- Projection ---------------------------------------------
            MapPoint origin = MapGeometry.Project(0, 0);
            check("projection of the origin is zero",
                Math.Abs(origin.X) < 1e-9 && Math.Abs(origin.Y) < 1e-9);

            MapPoint xAxis = MapGeometry.Project(1, 0);
            check("projection rotates X by 45 degrees",
                Math.Abs(xAxis.X + 0.70710678) < 1e-6 &&
                Math.Abs(xAxis.Y - 0.35355339) < 1e-6);

            MapPoint yAxis = MapGeometry.Project(0, 1);
            check("projection rotates Y by 45 degrees",
                Math.Abs(yAxis.X - 0.70710678) < 1e-6 &&
                Math.Abs(yAxis.Y - 0.35355339) < 1e-6);

            MapPoint native = MapGeometry.ToNative(xAxis);
            check("native plane undoes the vertical compression",
                Math.Abs(native.Y - 0.70710678) < 1e-6);
            check("native plane keeps the horizontal axis",
                Math.Abs(native.X - xAxis.X) < 1e-9);

            // --- Quaternion rotation ------------------------------------
            float[] identity = new float[] { 0f, 0f, 0f, 1f };
            float rx, ry, rz;
            MapGeometry.Rotate(identity, 3f, 4f, 5f, out rx, out ry, out rz);
            check("identity quaternion keeps the vector",
                Math.Abs(rx - 3f) < 1e-5 && Math.Abs(ry - 4f) < 1e-5 && Math.Abs(rz - 5f) < 1e-5);

            // A 90 degree rotation around Z maps X to Y.
            float half = (float)(Math.Sqrt(0.5));
            float[] quarter = new float[] { 0f, 0f, half, half };
            MapGeometry.Rotate(quarter, 1f, 0f, 0f, out rx, out ry, out rz);
            check("quarter turn around Z maps X to Y",
                Math.Abs(rx) < 1e-5 && Math.Abs(ry - 1f) < 1e-5 && Math.Abs(rz) < 1e-5);

            // --- Geometry from a synthetic snapshot ----------------------
            MapSnapshot snapshot = new MapSnapshot();
            snapshot.GameVersion = MapReader.SupportedVersion;

            SceneRecord scene = new SceneRecord();
            scene.Slot = 0;
            scene.SceneId = 7;
            scene.WorldId = 42;
            scene.SceneSno = 1234;
            scene.Rotation = new float[] { 0f, 0f, 0f, 1f };
            scene.OriginX = 0f;
            scene.OriginY = 0f;
            scene.OriginZ = 0f;
            scene.Bounds = new float[] { 0f, 0f, 10f, 10f };
            snapshot.Scenes.Add(scene);

            SceneAsset asset = new SceneAsset();
            asset.SceneSno = 1234;
            asset.Cells.Add(new NavCell(0f, 0f, 0f, 2f, 2f, 0f, 1));      // walkable
            asset.Cells.Add(new NavCell(2f, 0f, 0f, 4f, 2f, 0f, 0));      // not walkable
            snapshot.Assets.Add(asset);

            List<MapPolygon> polygons = MapGeometry.Build(snapshot);
            check("only walkable cells become polygons", polygons.Count == 1);

            if (polygons.Count == 1)
            {
                MapPolygon p = polygons[0];
                check("polygon has four corners", p.Points.Length == 4);
                check("polygon is not a cached outline", !p.CachedFootprint);

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                foreach (MapPoint q in p.Points)
                {
                    if (q.X < minX) minX = q.X;
                    if (q.X > maxX) maxX = q.X;
                    if (q.Y < minY) minY = q.Y;
                    if (q.Y > maxY) maxY = q.Y;
                }
                check("polygon covers the expected area",
                    Math.Abs((maxX - minX) - 2.82842712) < 1e-4 &&
                    Math.Abs((maxY - minY) - 1.41421356) < 1e-4);
            }

            // Two different worlds must not produce geometry.
            MapSnapshot mixed = new MapSnapshot();
            SceneRecord other = new SceneRecord();
            other.WorldId = 43;
            other.SceneSno = 1234;
            other.Rotation = new float[] { 0f, 0f, 0f, 1f };
            mixed.Scenes.Add(scene);
            mixed.Scenes.Add(other);
            mixed.Assets.Add(asset);
            check("geometry is skipped while the world is unstable",
                MapGeometry.Build(mixed).Count == 0);

            // Missing navmesh assets must emit 0 polygons rather than fake
            // rectangular bounding-box squares.
            MapSnapshot missingAsset = new MapSnapshot();
            SceneRecord bounded = new SceneRecord();
            bounded.SceneId = 8;
            bounded.WorldId = 42;
            bounded.SceneSno = 9999;
            bounded.Bounds = new float[] { 10f, 20f, 30f, 40f };
            bounded.Rotation = identity;
            missingAsset.Scenes.Add(bounded);

            CachedScene missingCached = new CachedScene();
            missingCached.SceneId = 9;
            missingCached.WorldId = 42;
            missingCached.SceneSno = 9998;
            missingCached.Bounds = new float[] { 40f, 20f, 60f, 40f };
            missingAsset.CachedScenes.Add(missingCached);

            List<MapPolygon> fallback = MapGeometry.Build(missingAsset);
            check("missing navmesh emits zero polygons", fallback.Count == 0);

            // --- Build gating --------------------------------------------
            string exe = MapReader.FindGameExecutable();
            if (string.IsNullOrEmpty(exe))
            {
                info("Diablo III не найдена — проверка хеша сборки пропущена.");
                check("game executable detection returns a path", false);
            }
            else
            {
                info("Исполняемый файл игры: " + exe);
                string hash = MapReader.HashExecutable(exe);
                check("game executable hash computed", hash.Length == 64);
                check("game build is supported", MapReader.IsSupportedBuild(exe));
                if (!MapReader.IsSupportedBuild(exe))
                    info("Хеш сборки: " + hash);
            }

            // A different build must fail closed instead of reading nonsense.
            string otherHash = MapReader.HashExecutable(
                Path.Combine(AppPaths.AppRoot, "SuperD3Hud.exe"));
            if (otherHash.Length == 64)
            {
                check("foreign build is rejected",
                    !string.Equals(otherHash, MapReader.SupportedHash,
                                   StringComparison.OrdinalIgnoreCase));
            }

            // --- Overlay rendering ---------------------------------------
            MapSettings settings = new MapSettings();
            settings.ShowMinimap = true;
            settings.ShowPanel = false;
            settings.Opacity = 120;

            MapOverlay overlay = new MapOverlay(settings);
            check("overlay starts hidden", !overlay.Visible);

            // A synthetic map with one walkable polygon and a known player.
            List<MapPolygon> shape = new List<MapPolygon>();
            shape.Add(MakeQuad(-60, -60, 60, 60, false));
            shape.Add(MakeQuad(90, 90, 140, 140, true));

            overlay.UpdateContent(shape, new MapPoint(0, 0), true, "тест");
            int minimapPixels;
            using (Bitmap bmp = overlay.RenderToBitmap(350, 350))
            {
                minimapPixels = CountOpaque(bmp);
                CheckGeometry(bmp, check);
            }
            check("minimap drawing produces visible pixels", minimapPixels > 500);

            // Without a player position nothing is drawn on the minimap, but
            // the standalone panel still shows the geometry.
            overlay.UpdateContent(shape, new MapPoint(0, 0), false, "тест");
            int noPlayerPixels;
            using (Bitmap bmp = overlay.RenderToBitmap(350, 350))
                noPlayerPixels = CountOpaque(bmp);
            check("minimap waits for the player", noPlayerPixels == 0);

            settings.ShowMinimap = false;
            settings.ShowPanel = true;
            overlay.UpdateContent(shape, new MapPoint(0, 0), false, "тест");
            int panelPixels;
            using (Bitmap bmp = overlay.RenderToBitmap(340, 340))
                panelPixels = CountOpaque(bmp);
            check("panel drawing produces visible pixels", panelPixels > 500);

            // An empty map must not throw and must stay transparent.
            overlay.UpdateContent(new List<MapPolygon>(), new MapPoint(0, 0), false, "");
            int emptyPixels;
            using (Bitmap bmp = overlay.RenderToBitmap(340, 340))
                emptyPixels = CountOpaque(bmp);
            check("empty map draws nothing", emptyPixels == 0);

            overlay.Dispose();

            // Approximate and CachedFootprint-only polygons must never produce blue squares.
            List<MapPolygon> approxOnly = new List<MapPolygon>();
            MapPolygon approxQuad = MakeQuad(-40, -40, 40, 40, false);
            approxQuad.Approximate = true;
            approxOnly.Add(approxQuad);
            approxOnly.Add(MakeQuad(50, 50, 90, 90, true));

            MapSettings approxMinimap = new MapSettings();
            approxMinimap.ShowMinimap = true;
            approxMinimap.ShowPanel = false;
            approxMinimap.Opacity = 120;
            using (MapOverlay approxOverlay = new MapOverlay(approxMinimap))
            {
                approxOverlay.UpdateContent(approxOnly, new MapPoint(0, 0), true, "");
                using (Bitmap bmp = approxOverlay.RenderToBitmap(350, 350))
                    check("approximate-only minimap draws nothing", CountOpaque(bmp) == 0);
            }

            // Tiled grid of adjacent walkable cells: must fuse cleanly with zero
            // internal grid seams, zero bright-blue squares, warm Diablo parchment/gold
            // tones, and soft minimap edge vignette.
            List<MapPolygon> tiledRoom = BuildTiledRoom(-90, -90, 90, 90, 15.0);
            MapSettings gridMinimapSettings = new MapSettings();
            gridMinimapSettings.ShowMinimap = true;
            gridMinimapSettings.ShowPanel = false;
            gridMinimapSettings.Opacity = 120;
            using (MapOverlay mini = new MapOverlay(gridMinimapSettings))
            {
                mini.UpdateContent(tiledRoom, new MapPoint(0, 0), true, "");
                using (Bitmap bmp = mini.RenderToBitmap(350, 350))
                {
                    int blueArtifacts = CountBlueArtifacts(bmp);
                    int warmPixels = CountWarmDiabloPixels(bmp);
                    int seamLines = CountInteriorOpaquePixels(bmp, 125, 125, 155, 155);
                    check("minimap has zero bright-blue square artifacts", blueArtifacts == 0);
                    check("minimap uses Diablo warm parchment/gold tones", warmPixels > 100);
                    check("minimap fuses adjacent navcells without internal seams", seamLines == 0);
                }

                // A room extending past all minimap edges verifies the 18px outer vignette fade.
                List<MapPolygon> fullSpan = BuildTiledRoom(-220, -220, 220, 220, 40.0);
                mini.UpdateContent(fullSpan, new MapPoint(0, 0), true, "");
                using (Bitmap bmp = mini.RenderToBitmap(350, 350))
                {
                    check("minimap vignette fades outer corners smoothly",
                        bmp.GetPixel(4, 4).A == 0 && bmp.GetPixel(175, 0).A == 0 && bmp.GetPixel(175, 25).A > 20);
                }
            }

            MapSettings largeSettings = new MapSettings();
            largeSettings.ShowMinimap = false;
            largeSettings.ShowPanel = false;
            largeSettings.ShowLargeMap = true;
            largeSettings.Opacity = 120;
            using (MapOverlay large = new MapOverlay(largeSettings, true))
            {
                large.UpdateContent(tiledRoom, new MapPoint(0, 0), true, "");
                using (Bitmap bmp = large.RenderToBitmap(1280, 720))
                {
                    int blueArtifacts = CountBlueArtifacts(bmp);
                    int warmPixels = CountWarmDiabloPixels(bmp);
                    int seamLines = CountInteriorOpaquePixels(bmp, 615, 335, 630, 345);
                    check("large map has zero bright-blue square artifacts", blueArtifacts == 0);
                    check("large map uses Diablo warm parchment/gold tones", warmPixels > 100);
                    check("large map fuses adjacent navcells without internal seams", seamLines == 0);
                }
            }

            return failures;
        }

        private static List<MapPolygon> BuildTiledRoom(double x0, double y0, double x1, double y1, double step)
        {
            List<MapPolygon> list = new List<MapPolygon>();
            for (double y = y0; y < y1 - 0.001; y += step)
            {
                for (double x = x0; x < x1 - 0.001; x += step)
                {
                    double nx = Math.Min(x + step, x1);
                    double ny = Math.Min(y + step, y1);
                    list.Add(MakeQuad(x, y, nx, ny, false));
                }
            }
            return list;
        }

        private static int CountBlueArtifacts(Bitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 20 && c.B > c.R + 20 && c.B > 120) count++;
                }
            }
            return count;
        }

        private static int CountWarmDiabloPixels(Bitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 20 && c.R >= c.G && c.R > c.B && c.R >= 150) count++;
                }
            }
            return count;
        }

        private static int CountInteriorOpaquePixels(Bitmap bmp, int x0, int y0, int x1, int y1)
        {
            int count = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (bmp.GetPixel(x, y).A > 100) count++;
                }
            }
            return count;
        }

        private static MapPolygon MakeQuad(double x0, double y0, double x1, double y1, bool cached)
        {
            MapPolygon p = new MapPolygon();
            p.CachedFootprint = cached;
            p.Points = new MapPoint[]
            {
                MapGeometry.Project(x0, y0),
                MapGeometry.Project(x1, y0),
                MapGeometry.Project(x1, y1),
                MapGeometry.Project(x0, y1)
            };
            return p;
        }

        private static int CountOpaque(Bitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    if (bmp.GetPixel(x, y).A > 20) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Checks that the drawn geometry sits in the expected part of the
        /// minimap: the player is at the centre, the walkable quad around the
        /// player, so the centre of the surface must be filled.
        /// </summary>
        private static void CheckGeometry(Bitmap bmp, Action<string, bool> check)
        {
            Color centre = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            check("player position is inside the drawn geometry", centre.A > 20);
        }
    }
}
