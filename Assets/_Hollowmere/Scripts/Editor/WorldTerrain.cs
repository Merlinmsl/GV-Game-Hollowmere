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
        const int SplatResolution = 256;
        const string TerrainFolder = "Assets/_Hollowmere/Art/Terrain/";

        // World layout along +Z: flooded village, estate road climbing the hill, Marrow estate.
        public const float VillageNorthEdge = 62f;
        public const float EstateSouthEdge = 197f;
        public const float EstateElevation = 9f;
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
            h = Mathf.Lerp(h, Road(x, z, broad, fine), Mathf.SmoothStep(0, 1, (z - VillageNorthEdge) / 12f));
            h = Mathf.Lerp(h, Estate(x, z, broad, fine), Mathf.SmoothStep(0, 1, (z - 183f) / 12f));
            return h;
        }

        // Flooded basin just below the water line, wooded banks either side, dry road behind the start.
        static float Village(float x, float z, float broad, float fine)
        {
            float basin = -0.16f + (broad - 0.5f) * 0.22f + (fine - 0.5f) * 0.07f;
            float outsideX = Mathf.Max(0, Mathf.Abs(x) - 43f);
            float outsideSouth = Mathf.Max(0, -20f - z);
            float sideBank = Mathf.SmoothStep(0, 1, outsideX / 16f) * (4.5f + broad * 3.5f);
            float southRoad = Mathf.SmoothStep(0, 1, outsideSouth / 14f) * (1.6f + broad * 2.2f);
            return basin + Mathf.Max(sideBank, southRoad) + fine * 0.25f * Mathf.Clamp01((outsideX + outsideSouth) / 8f);
        }

        // The estate road: leaves the water, then climbs steadily in a cutting between wooded slopes.
        static float Road(float x, float z, float broad, float fine)
        {
            float climb = Mathf.SmoothStep(0, 1, (z - 70f) / 118f);
            float roadLevel = -0.16f + 0.6f * Mathf.SmoothStep(0, 1, (z - VillageNorthEdge) / 5f) + (EstateElevation - 0.44f) * climb;
            float fromRoad = Mathf.Abs(x - RoadCentreX(z));
            float slopes = Mathf.SmoothStep(0, 1, (fromRoad - 4f) / 20f) * (3f + broad * 4.5f);
            return roadLevel + slopes + (fine - 0.5f) * 0.12f + fine * 0.3f * Mathf.Clamp01((fromRoad - 4f) / 10f);
        }

        // The hilltop: a level lawn inside the estate walls, rising woods outside them.
        static float Estate(float x, float z, float broad, float fine)
        {
            float lawn = EstateElevation + (broad - 0.5f) * 0.10f + (fine - 0.5f) * 0.04f;
            float outside = Mathf.Max(Mathf.Max(0, Mathf.Abs(x) - 40f), Mathf.Max(0, z - EstateSouthEdge - 94f));
            return lawn + Mathf.SmoothStep(0, 1, outside / 16f) * (3.5f + broad * 3.5f);
        }

        /// <summary>Weight of the moss/grass layer (0 = bare mud, 1 = full moss) at a world position.</summary>
        static float MossWeight(float x, float z)
        {
            float patch = Mathf.PerlinNoise(x * 0.11f + 40f, z * 0.11f + 17f);

            if (z > EstateSouthEdge - 2f)
            {
                float lz = z - EstateSouthEdge;          // estate-local Z, gate at -2
                bool drive = Mathf.Abs(x) < 2.4f && lz < 30f;
                bool westPath = x > -26.5f && x < -23f && lz > 26f && lz < 70f;
                bool backPath = lz > 66.5f && lz < 70f && x > -26.5f && x < -6f;
                bool frontPath = lz > 26f && lz < 29.5f && x > -26.5f && x < 2.4f;
                bool underHouse = x > -21f && x < 25f && lz > 31f && lz < 73f;
                if (drive || westPath || backPath || frontPath || underHouse) return 0.08f;
                return Mathf.Clamp01(0.55f + patch * 0.7f);
            }

            if (z > VillageNorthEdge + 4f && Mathf.Abs(x - RoadCentreX(z)) < 2.6f) return 0.1f;   // dirt road
            float height = Height(x, z);
            return Mathf.Clamp01((height - (WaterLevel - 0.06f)) / 0.5f) * Mathf.Clamp01(0.35f + patch * 1.1f);
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
