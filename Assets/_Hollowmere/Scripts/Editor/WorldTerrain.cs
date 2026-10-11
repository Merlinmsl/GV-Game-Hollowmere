using UnityEditor;
using UnityEngine;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Generates the heights and ground textures of every terrain tile from one world-space
    /// function, so neighbouring zone scenes meet without seams when they are streamed in.
    /// Menu: Hollowmere > World > Rebuild Terrain Tiles.
    /// </summary>
    public static class WorldTerrain
    {
        // All tiles share the same vertical range so equal heights encode to equal values.
        public const float BaseY = -2f;
        public const float HeightRange = 30f;

        const int HeightmapResolution = 257;
        const int SplatResolution = 512;
        const string TerrainFolder = "Assets/_Hollowmere/Art/Terrain/";

        // World layout along +Z: flooded village, estate road climbing the hill, Marrow estate.
        public const float VillageNorthEdge = 62f;
        public const float EstateSouthEdge = 197f;
        public const float EstateElevation = 22f;
        const float WaterLevel = 0.16f;

        struct Tile
        {
            public string asset;
            public Vector3 origin;
            public Vector3 size;

            public Tile(string asset, float x, float z, float width, float depth)
            {
                this.asset = asset;
                origin = new Vector3(x, BaseY, z);
                size = new Vector3(width, HeightRange, depth);
            }
        }

        static readonly Tile[] Tiles =
        {
            new Tile("TD_Z1_Outskirts", -80, -50, 160, 150),
            new Tile("TD_C1_EstateRoad", -80, 100, 160, 80),
            new Tile("TD_Z2_MarrowHouse", -80, 180, 160, 130),
        };

        // Openings cut through the ground (world x, z, width, depth), where a stairwell goes below the surface.
        // The stairs from Marrow's study down to the Undercroft pass through the ground under the house.
        static readonly Rect[] Holes =
        {
            new Rect(-13.8f, 237.6f, 24.6f, 3.8f),
        };

        /// <summary>X position of the estate road's centre line at a given Z (a gentle S-bend).</summary>
        public static float RoadCentreX(float z)
        {
            return 14f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((z - 72f) / 112f));
        }

        /// <summary>Ground height in metres at a world position.</summary>
        public static float Height(float x, float z)
        {
            float broad = Mathf.PerlinNoise(x * 0.045f + 11.3f, z * 0.045f + 4.7f);
            float fine = Mathf.PerlinNoise(x * 0.16f + 3.1f, z * 0.16f + 9.2f);

            float h = Village(x, z, broad, fine);
            // Away from the gate the flood pushes further north in places, so its far edge is uneven too.
            float northEdge = VillageNorthEdge + 13f * Mathf.PerlinNoise(x * 0.05f + 3f, 7.7f) * Mathf.SmoothStep(0, 1, (Mathf.Abs(x) - 25f) / 9f);
            h = Mathf.Lerp(h, Road(x, z, broad, fine), Mathf.SmoothStep(0, 1, (z - northEdge) / 12f));
            h = Mathf.Lerp(h, Estate(x, z, broad, fine), Mathf.SmoothStep(0, 1, (z - 183f) / 12f));
            return h;
        }

        // The flood is the union of two shapes: a core that every boardwalk and cabin sits inside
        // (always under water), and a larger lobed outline that gives the shore its coves and headlands.
        static readonly Vector2 BasinCentre = new Vector2(0f, 21f);
        static readonly Vector2 CoreHalfSize = new Vector2(36f, 38f);
        const float CoreCornerRadius = 20f;

        /// <summary>Distance in metres outside the flood's shoreline (0 = in the water).</summary>
        public static float OutsideShore(float x, float z)
        {
            float dx = x - BasinCentre.x, dz = z - BasinCentre.y;

            float qx = Mathf.Abs(dx) - CoreHalfSize.x + CoreCornerRadius;
            float qz = Mathf.Abs(dz) - CoreHalfSize.y + CoreCornerRadius;
            float core = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qz, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qz), 0) - CoreCornerRadius - 3f;

            // Radius of the lobed outline in this direction: three waves of different lengths plus noise.
            float angle = Mathf.Atan2(dz, dx);
            float radius = 46f + 8f * Mathf.Sin(2f * angle + 0.6f) + 7.5f * Mathf.Sin(3f * angle + 2.1f) + 5f * Mathf.Sin(5f * angle + 4f)
                         + 11f * (Mathf.PerlinNoise(x * 0.045f + 50f, z * 0.045f + 80f) - 0.5f);
            radius = Mathf.Clamp(radius, 33f, 60f);
            // Towards the arrival road the water must not reach the edge of the map.
            if (dz < 0) radius = Mathf.Min(radius, 51f + 9f * Mathf.SmoothStep(0, 1, (Mathf.Abs(dx) - 10f) / 15f));
            float lobes = Mathf.Sqrt(dx * dx + dz * dz) - radius;

            float outside = Mathf.Min(core, lobes) - 3f * (Mathf.PerlinNoise(x * 0.11f + 9f, z * 0.11f + 31f) - 0.5f);
            return Mathf.Max(0, outside);
        }

        // Flooded basin just below the water line, with wooded banks rising from an uneven shore.
        static float Village(float x, float z, float broad, float fine)
        {
            float basin = -0.16f + (broad - 0.5f) * 0.22f + (fine - 0.5f) * 0.07f;

            // Mud bars that break the surface, kept clear of the boardwalks and cabins.
            float bar = Mathf.Max(0, Mathf.PerlinNoise(x * 0.07f + 7f, z * 0.07f + 3f) - 0.66f) * 2.4f;
            basin += bar * Mathf.SmoothStep(0, 1, (DistanceToVillageStructures(x, z) - 3f) / 5f);

            float outside = OutsideShore(x, z);
            float bankHeight = 4.5f + broad * 3.5f;
            // The road the player arrives on comes down to the water in a gentle ramp, not a bank.
            if (z < BasinCentre.y)
                bankHeight *= Mathf.Lerp(0.38f, 1f, Mathf.SmoothStep(0, 1, (Mathf.Abs(x) - 4f) / 12f));
            return basin + Mathf.SmoothStep(0, 1, outside / 16f) * bankHeight + fine * 0.25f * Mathf.Clamp01(outside / 8f);
        }

        // Footprints of the things standing in the flood: x min, z min, x max, z max.
        static readonly Rect[] VillageStructures =
        {
            Rect.MinMaxRect(-32, 1.5f, -18.5f, 17.5f), Rect.MinMaxRect(18.5f, 7.5f, 32, 23.5f),
            Rect.MinMaxRect(-32, 30.5f, -18.5f, 46.5f), Rect.MinMaxRect(18.5f, 40.5f, 32, 56.5f),
            Rect.MinMaxRect(5.5f, 26.5f, 17.5f, 37.5f), Rect.MinMaxRect(-1.2f, -30, 1.2f, 74),
            Rect.MinMaxRect(1.5f, -12, 9.5f, -4.5f), Rect.MinMaxRect(-20, 4.9f, 0, 7.1f),
            Rect.MinMaxRect(0, 10.9f, 20, 13.1f), Rect.MinMaxRect(-20, 40.9f, 0, 43.1f),
            Rect.MinMaxRect(0, 50.9f, 20, 53.1f), Rect.MinMaxRect(0, 30.9f, 8, 33.1f),
            Rect.MinMaxRect(-26, 61, 26, 64.5f), Rect.MinMaxRect(4, 17, 19, 22), Rect.MinMaxRect(-17, 20, -6, 29),
        };

        static float DistanceToVillageStructures(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var r in VillageStructures)
            {
                float dx = Mathf.Max(r.xMin - x, 0, x - r.xMax);
                float dz = Mathf.Max(r.yMin - z, 0, z - r.yMax);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        // The estate road: leaves the water, then climbs steadily in a cutting between wooded slopes.
        static float Road(float x, float z, float broad, float fine)
        {
            float climb = Mathf.SmoothStep(0, 1, (z - 70f) / 118f);
            float roadLevel = -0.16f + 0.6f * Mathf.SmoothStep(0, 1, (z - VillageNorthEdge) / 5f) + (EstateElevation - 0.44f) * climb;
            float fromRoad = Mathf.Abs(x - RoadCentreX(z));
            float slopes = Mathf.SmoothStep(0, 1, (fromRoad - 4f) / 20f) * (3f + broad * 4.5f);
            slopes *= 1f - 0.75f * Mathf.SmoothStep(0, 1, (z - 150f) / 40f);   // the cutting opens out near the top
            return roadLevel + slopes + (fine - 0.5f) * 0.12f + fine * 0.3f * Mathf.Clamp01((fromRoad - 4f) / 10f);
        }

        // The hilltop: a level lawn inside the estate walls, the land falling away outside them.
        static float Estate(float x, float z, float broad, float fine)
        {
            float lawn = EstateElevation + (broad - 0.5f) * 0.10f + (fine - 0.5f) * 0.04f;
            float outside = Mathf.Max(Mathf.Max(0, Mathf.Abs(x) - 40f), Mathf.Max(0, z - EstateSouthEdge - 94f));
            return lawn - Mathf.SmoothStep(0, 1, outside / 30f) * (4f + broad * 3f) + fine * 0.4f * Mathf.Clamp01(outside / 8f);
        }

        /// <summary>
        /// How strongly a spot is worn to bare earth by a path (0 = untouched, 1 = centre of a path).
        /// Paths have uneven, feathered edges and taper off instead of stopping dead.
        /// </summary>
        static float PathWear(float x, float z)
        {
            float ragged = (Mathf.PerlinNoise(x * 0.55f + 3f, z * 0.55f + 8f) - 0.5f) * 1.3f;
            float wear = 0f;

            // The road the player arrives on, running down to the water.
            if (z < -19f)
            {
                float d = Mathf.Abs(x - 1.5f * Mathf.Sin(z * 0.15f)) + ragged;
                wear = Mathf.Max(wear, (1f - Mathf.SmoothStep(0, 1, (d - 2.3f) / 1.7f)) * Mathf.SmoothStep(0, 1, (-19f - z) / 5f));
            }

            // The estate road, then the drive from the gate to the front steps.
            if (z > VillageNorthEdge + 2f && z < EstateSouthEdge + 31f)
            {
                float centre = z < EstateSouthEdge - 2f ? RoadCentreX(z) : 0f;
                float d = Mathf.Abs(x - centre) + ragged;
                wear = Mathf.Max(wear, (1f - Mathf.SmoothStep(0, 1, (d - 2.3f) / 1.7f)) * Mathf.SmoothStep(0, 1, (z - VillageNorthEdge - 2f) / 6f));
            }

            float lz = z - EstateSouthEdge;
            // Forecourt in front of the steps.
            float court = Mathf.Sqrt(x * x / 100f + (lz - 28f) * (lz - 28f) / 20f) + ragged * 0.12f;
            wear = Mathf.Max(wear, 1f - Mathf.SmoothStep(0, 1, (court - 0.75f) / 0.4f));
            // A faint track worn round the west side of the house, fading out behind it.
            if (lz > 24f && lz < 72f)
            {
                float d = Mathf.Abs(x + 25.5f + 1.2f * Mathf.Sin(lz * 0.2f)) + ragged;
                float along = Mathf.SmoothStep(0, 1, (lz - 24f) / 5f) * (1f - Mathf.SmoothStep(0, 1, (lz - 52f) / 18f));
                wear = Mathf.Max(wear, (1f - Mathf.SmoothStep(0, 1, (d - 0.9f) / 1.5f)) * along * 0.75f);
            }
            // Bare earth under the house.
            if (x > -21f && x < 25f && lz > 31f && lz < 73f) wear = 1f;
            return wear;
        }

        /// <summary>Weight of the moss/grass layer (0 = bare mud, 1 = full moss) at a world position.</summary>
        static float MossWeight(float x, float z)
        {
            float patch = Mathf.PerlinNoise(x * 0.11f + 40f, z * 0.11f + 17f);
            float growth;
            if (z > EstateSouthEdge - 2f)
                growth = Mathf.Clamp01(0.55f + patch * 0.7f);                       // estate lawn
            else
                growth = Mathf.Clamp01((Height(x, z) - (WaterLevel - 0.06f)) / 0.5f) * Mathf.Clamp01(0.35f + patch * 1.1f);
            return growth * (1f - 0.93f * PathWear(x, z));
        }

        [MenuItem("Hollowmere/World/Rebuild Terrain Tiles")]
        public static void RebuildAll()
        {
            var mud = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainFolder + "TL_Z1_Mud.terrainlayer");
            var moss = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainFolder + "TL_Z1_Moss.terrainlayer");

            foreach (var tile in Tiles)
            {
                string path = TerrainFolder + tile.asset + ".asset";
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (data == null)
                {
                    data = new TerrainData();
                    AssetDatabase.CreateAsset(data, path);
                }

                data.heightmapResolution = HeightmapResolution;
                data.size = tile.size;
                data.alphamapResolution = SplatResolution;
                data.baseMapResolution = 512;
                data.terrainLayers = new[] { mud, moss };

                var heights = new float[HeightmapResolution, HeightmapResolution];
                for (int iz = 0; iz < HeightmapResolution; iz++)
                {
                    float z = tile.origin.z + tile.size.z * iz / (HeightmapResolution - 1f);
                    for (int ix = 0; ix < HeightmapResolution; ix++)
                    {
                        float x = tile.origin.x + tile.size.x * ix / (HeightmapResolution - 1f);
                        heights[iz, ix] = Mathf.Clamp01((Height(x, z) - BaseY) / HeightRange);
                    }
                }
                data.SetHeights(0, 0, heights);

                var splat = new float[SplatResolution, SplatResolution, 2];
                for (int iz = 0; iz < SplatResolution; iz++)
                {
                    float z = tile.origin.z + tile.size.z * (iz + 0.5f) / SplatResolution;
                    for (int ix = 0; ix < SplatResolution; ix++)
                    {
                        float x = tile.origin.x + tile.size.x * (ix + 0.5f) / SplatResolution;
                        float m = MossWeight(x, z);
                        splat[iz, ix, 0] = 1f - m;
                        splat[iz, ix, 1] = m;
                    }
                }
                data.SetAlphamaps(0, 0, splat);

                // true = solid ground, false = opening. One cell per heightmap square.
                int holeResolution = HeightmapResolution - 1;
                var solid = new bool[holeResolution, holeResolution];
                for (int iz = 0; iz < holeResolution; iz++)
                {
                    float z = tile.origin.z + tile.size.z * (iz + 0.5f) / holeResolution;
                    for (int ix = 0; ix < holeResolution; ix++)
                    {
                        float x = tile.origin.x + tile.size.x * (ix + 0.5f) / holeResolution;
                        bool open = false;
                        foreach (var hole in Holes)
                            if (hole.Contains(new Vector2(x, z))) open = true;
                        solid[iz, ix] = !open;
                    }
                }
                data.SetHoles(0, 0, solid);
                EditorUtility.SetDirty(data);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Hollowmere: rebuilt " + Tiles.Length + " terrain tiles from the world height function.");
        }

        /// <summary>World-space origin of a tile, for placing its Terrain object in the zone scene.</summary>
        public static Vector3 TileOrigin(string assetName)
        {
            foreach (var tile in Tiles)
                if (tile.asset == assetName) return tile.origin;
            throw new System.ArgumentException("Unknown terrain tile " + assetName);
        }
    }
}
