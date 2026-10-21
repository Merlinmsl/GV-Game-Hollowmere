using System.Collections.Generic;
using System.IO;
using Hollowmere.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Makes the in-game map of every zone.
    ///
    /// For each zone a camera looks straight down and photographs the ground, lit evenly with the
    /// fog off (the game's own night lighting would give a black picture). The picture is then
    /// finished in three steps:
    ///   1. Exposure: brightened so the ground averages the same brightness in every zone.
    ///   2. Beyond the world: where a downward ray finds no ground (outside the terrain tiles, or
    ///      solid rock around the tunnels), generated forest or rock is painted in, blended softly,
    ///      so the edge of the world never shows.
    ///   3. Mist: drifting clouds thicken towards the picture's border, like unexplored land.
    /// The pictures go to Art/Textures/Maps and their placement to Data/SO_ZoneMaps.asset.
    ///
    /// Menu: Hollowmere > World > Map > Bake Zone Maps. Rebake after changing a zone's layout.
    /// </summary>
    public static class WorldMaps
    {
        const string MapFolder = "Assets/_Hollowmere/Art/Textures/Maps";
        const string SetPath = "Assets/_Hollowmere/Data/SO_ZoneMaps.asset";
        const int LongestSide = 2048;
        const int MaskCell = 4;                 // one ground test per 4x4 pixels

        struct Spec
        {
            public string Scene, Name;
            public Rect Ground;                 // x, z of the lower-left corner, width, depth
            public Bounds Area;                 // where the player counts as being in this zone
            public bool Underground;
        }

        static readonly Spec[] Zones =
        {
            new Spec { Scene = "Z1_Outskirts", Name = "The Flooded Village", Ground = new Rect(-115f, -80f, 230f, 210f),
                       Area = MinMax(-80f, -20f, -50f, 80f, 40f, 100f) },
            new Spec { Scene = "C1_EstateRoad", Name = "The Estate Road", Ground = new Rect(-110f, 70f, 220f, 140f),
                       Area = MinMax(-80f, -18f, 100f, 80f, 42f, 180f) },
            new Spec { Scene = "Z2_MarrowHouse", Name = "Marrow House", Ground = new Rect(-115f, 150f, 230f, 190f),
                       Area = MinMax(-80f, -8f, 180f, 80f, 52f, 310f) },
            new Spec { Scene = "Z3_Undercroft", Name = "The Undercroft", Ground = new Rect(-118f, 196f, 154f, 84f),
                       Area = MinMax(-105f, 6f, 221f, 23f, 26.5f, 255f), Underground = true },
            new Spec { Scene = "Z4_Waterworks", Name = "The Waterworks", Ground = new Rect(-275f, 150f, 230f, 190f),
                       Area = MinMax(-240f, -15f, 180f, -80f, 55f, 310f) },
        };

        static Bounds MinMax(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
            return b;
        }

        [MenuItem("Hollowmere/World/Map/Bake Zone Maps", priority = 70)]
        public static void BakeAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            WorldScenes.OpenWorld();
            AddMarkers();

            Directory.CreateDirectory(MapFolder);
            var set = AssetDatabase.LoadAssetAtPath<ZoneMapSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ZoneMapSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            var maps = new List<ZoneMapSet.ZoneMap>();
            foreach (Spec spec in Zones)
            {
                Texture2D image = Bake(spec);
                maps.Add(new ZoneMapSet.ZoneMap
                {
                    sceneName = spec.Scene, displayName = spec.Name, image = image,
                    worldRect = spec.Ground, area = spec.Area, underground = spec.Underground
                });
            }
            set.maps = maps.ToArray();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log("Hollowmere map: baked " + maps.Count + " zone maps into " + SetPath);
        }

        /// <summary>Puts a MapMarker on every item and exit marker in the zones, judged by its name.</summary>
        [MenuItem("Hollowmere/World/Map/Add Map Markers To Items", priority = 71)]
        public static void AddMarkers()
        {
            int added = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded || scene.name == "World") continue;
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name != "_Gameplay") continue;
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!MapMarker.TryKindFromName(t.name, out MapMarker.Kind kind)) continue;
                        var marker = t.GetComponent<MapMarker>();
                        if (marker == null) { marker = t.gameObject.AddComponent<MapMarker>(); added++; }
                        marker.Setup(kind, MapMarker.MakeLabel(t.name));
                        EditorUtility.SetDirty(marker);
                        changed = true;
                    }
                }
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            Debug.Log("Hollowmere map: " + added + " map markers added.");
        }

        // ---------------------------------------------------------------- one zone

        static Texture2D Bake(Spec spec)
        {
            int width = spec.Ground.width >= spec.Ground.height ? LongestSide : Mathf.RoundToInt(LongestSide * spec.Ground.width / spec.Ground.height);
            int height = spec.Ground.width >= spec.Ground.height ? Mathf.RoundToInt(LongestSide * spec.Ground.height / spec.Ground.width) : LongestSide;

            var hidden = new List<GameObject>();
            var fog = RenderSettings.fog;
            var ambientMode = RenderSettings.ambientMode;
            var ambientLight = RenderSettings.ambientLight;
            var lights = new List<GameObject>();
            GameObject cameraObject = null;
            RenderTexture target = null;
            // Terrains drop trees and detail far from the camera; from 300 m up that would be all of them.
            var terrainSettings = new List<(Terrain terrain, float trees, float billboards, float basemap)>();
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                terrainSettings.Add((terrain, terrain.treeDistance, terrain.treeBillboardDistance, terrain.basemapDistance));
                terrain.treeDistance = 5000f;
                terrain.treeBillboardDistance = 5000f;
                terrain.basemapDistance = 5000f;
            }
            // Seen from this high, every tree is a few pixels tall, so level-of-detail would hide them all.
            float lodBias = QualitySettings.lodBias;
            int maximumLod = QualitySettings.maximumLODLevel;
            QualitySettings.lodBias = 100f;
            QualitySettings.maximumLODLevel = 0;
            try
            {
                // Only what belongs on this map: never the World scene (moon, vistas, debug player).
                // Underground, only the zone itself, without its ceilings.
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    Scene scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    bool keep = scene.name != "World" && (!spec.Underground || scene.name == spec.Scene);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (!keep && root.activeSelf) { root.SetActive(false); hidden.Add(root); }
                        if (keep && spec.Underground)
                            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                                if (t.gameObject.activeSelf && t.name.Contains("Ceiling")) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
                    }
                }
                Physics.SyncTransforms();

                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
                lights.Add(MakeLight("__MapKeyLight", new Vector3(62f, -35f, 0f), 1.7f, new Color(1f, 0.96f, 0.9f), true));
                lights.Add(MakeLight("__MapFillLight", new Vector3(55f, 145f, 0f), 0.55f, new Color(0.8f, 0.85f, 1f), false));

                float cameraHeight = spec.Underground ? 40f : 300f;
                cameraObject = new GameObject("__MapCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = spec.Ground.height * 0.5f;
                camera.aspect = spec.Ground.width / spec.Ground.height;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = cameraHeight + 300f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.transform.SetPositionAndRotation(new Vector3(spec.Ground.center.x, cameraHeight, spec.Ground.center.y), Quaternion.Euler(90f, 0f, 0f));
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = true;

                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = target;
                camera.Render();

                var photo = new Texture2D(width, height, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                photo.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                photo.Apply();
                RenderTexture.active = previous;

                float[,] ground = GroundMask(spec, width, height, cameraHeight, out float[,] water);
                Texture2D finished = Finish(photo, ground, water, spec, width, height);
                Object.DestroyImmediate(photo);
                return Save(finished, spec.Scene);
            }
            finally
            {
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                foreach (GameObject light in lights) Object.DestroyImmediate(light);
                foreach (GameObject go in hidden) if (go != null) go.SetActive(true);
                QualitySettings.lodBias = lodBias;
                QualitySettings.maximumLODLevel = maximumLod;
                foreach (var t in terrainSettings)
                {
                    if (t.terrain == null) continue;
                    t.terrain.treeDistance = t.trees;
                    t.terrain.treeBillboardDistance = t.billboards;
                    t.terrain.basemapDistance = t.basemap;
                }
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                Physics.SyncTransforms();
            }
        }

        static GameObject MakeLight(string name, Vector3 euler, float intensity, Color colour, bool shadows)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = colour;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return go;
        }

        /// <summary>
        /// 1 where a ray straight down finds ground, 0 where it finds nothing; softened at the edges.
        /// Also marks the ground lying under a water surface (the flood, the reservoir, the outflow pool),
        /// because the water shader does not show in this top-down photo.
        /// </summary>
        static float[,] GroundMask(Spec spec, int width, int height, float cameraHeight, out float[,] water)
        {
            var surfaces = new List<Bounds>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                string n = r.name.ToLowerInvariant();
                if (!n.Contains("water") || n.Contains("waterworks") || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.bounds.size.y > 1f) continue;                  // a flat surface, not a building
                surfaces.Add(r.bounds);
            }

            int gw = width / MaskCell, gh = height / MaskCell;
            var mask = new float[gw, gh];
            water = new float[gw, gh];
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    float wx = spec.Ground.xMin + (x + 0.5f) / gw * spec.Ground.width;
                    float wz = spec.Ground.yMin + (y + 0.5f) / gh * spec.Ground.height;
                    if (!VisibleGround(new Vector3(wx, cameraHeight, wz), cameraHeight + 100f, out RaycastHit hit)) continue;
                    mask[x, y] = 1f;
                    foreach (Bounds b in surfaces)
                        if (wx >= b.min.x && wx <= b.max.x && wz >= b.min.z && wz <= b.max.z && hit.point.y < b.max.y)
                        {
                            water[x, y] = 1f;
                            break;
                        }
                }
            water = Blur(water, 1);
            return Blur(Blur(mask, 3), 3);
        }

        // The highest thing below that can actually be seen: terrain or a collider with a visible mesh.
        // Invisible walls (the zone boundaries) do not count as ground.
        static bool VisibleGround(Vector3 from, float distance, out RaycastHit ground)
        {
            ground = default;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                bool visible = hit.collider is TerrainCollider
                               || (hit.collider.GetComponentInParent<Renderer>() is Renderer r && r.enabled);
                if (!visible) continue;
                if (!found || hit.point.y > ground.point.y) { ground = hit; found = true; }
            }
            return found;
        }

        static float[,] Blur(float[,] m, int radius)
        {
            int w = m.GetLength(0), h = m.GetLength(1);
            var tmp = new float[w, h];
            var result = new float[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -radius; k <= radius; k++) { int xx = Mathf.Clamp(x + k, 0, w - 1); sum += m[xx, y]; n++; }
                    tmp[x, y] = sum / n;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -radius; k <= radius; k++) { int yy = Mathf.Clamp(y + k, 0, h - 1); sum += tmp[x, yy]; n++; }
                    result[x, y] = sum / n;
                }
            return result;
        }

        static float Sample(float[,] m, float u, float v)
        {
            int w = m.GetLength(0), h = m.GetLength(1);
            float fx = Mathf.Clamp(u * w - 0.5f, 0f, w - 1.001f), fy = Mathf.Clamp(v * h - 0.5f, 0f, h - 1.001f);
            int x = (int)fx, y = (int)fy;
            float tx = fx - x, ty = fy - y;
            return Mathf.Lerp(Mathf.Lerp(m[x, y], m[x + 1, y], tx), Mathf.Lerp(m[x, y + 1], m[x + 1, y + 1], tx), ty);
        }

        // ---------------------------------------------------------------- finishing the picture

        static Texture2D Finish(Texture2D photo, float[,] ground, float[,] water, Spec spec, int width, int height)
        {
            Color[] src = photo.GetPixels();

            // 1. Exposure: the average ground colour, to brighten to a common level and to tint the filler.
            Color sum = Color.black; float weight = 0f;
            for (int y = 0; y < height; y += 2)
                for (int x = 0; x < width; x += 2)
                {
                    float m = Sample(ground, (x + 0.5f) / width, (y + 0.5f) / height);
                    if (m < 0.99f) continue;
                    sum += src[y * width + x]; weight++;
                }
            Color average = weight > 0f ? sum / weight : new Color(0.15f, 0.16f, 0.14f);
            float luminance = Mathf.Max(0.01f, average.r * 0.3f + average.g * 0.59f + average.b * 0.11f);
            float exposure = Mathf.Clamp((spec.Underground ? 0.34f : 0.3f) / luminance, 0.6f, 5f);
            Color groundTint = average * exposure;

            var result = new Color[width * height];
            float edgeWidth = 0.13f * Mathf.Min(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    float wx = spec.Ground.xMin + u * spec.Ground.width;
                    float wz = spec.Ground.yMin + v * spec.Ground.height;

                    // 2. Real ground where there is ground, generated land beyond it.
                    Color real = src[y * width + x] * exposure;

                    // Ground under water: dark, slightly green flood water with faint ripples.
                    float wet = Sample(water, u, v);
                    if (wet > 0f)
                    {
                        float ripple = Mathf.PerlinNoise(wx * 0.6f, wz * 0.15f);
                        Color flood = new Color(0.06f, 0.1f, 0.11f) + new Color(0.05f, 0.07f, 0.08f) * ripple;
                        real = Color.Lerp(real, Color.Lerp(real * 0.5f, flood, 0.8f), wet * 0.85f);
                    }

                    // An irregular border between real and generated ground, so no straight tile edge shows.
                    Color filler = spec.Underground ? Rock(wx, wz, groundTint) : Forest(wx, wz, groundTint);
                    float ragged = (Fbm(wx * 0.06f + 5f, wz * 0.06f + 9f) - 0.5f) * 0.9f;
                    float m = Mathf.SmoothStep(0.25f, 0.75f, Sample(ground, u, v) + ragged * (1f - Mathf.Abs(Sample(ground, u, v) * 2f - 1f)));
                    Color c = Color.Lerp(filler, real, m);

                    // 3. Mist, thickest at the border, broken up by drifting cloud shapes.
                    float edge = 1f - Mathf.Clamp01(Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, height - 1 - y)) / edgeWidth);
                    float cloud = Fbm(wx * 0.018f + 31f, wz * 0.018f + 17f);
                    float mist = Mathf.Clamp01(edge * 1.35f + (cloud - 0.5f) * 1.1f * Mathf.Clamp01(edge + 0.25f) - 0.08f);
                    Color mistColour = spec.Underground
                        ? Color.Lerp(new Color(0.12f, 0.13f, 0.15f), new Color(0.24f, 0.25f, 0.28f), cloud)
                        : Color.Lerp(new Color(0.36f, 0.45f, 0.58f), new Color(0.74f, 0.8f, 0.88f), cloud);
                    c = Color.Lerp(c, mistColour, mist * 0.94f);
                    c.a = 1f;
                    result[y * width + x] = c;
                }

            var finished = new Texture2D(width, height, TextureFormat.RGB24, false);
            finished.SetPixels(result);
            finished.Apply();
            return finished;
        }

        // Dense conifer forest seen from above: dark canopy with lighter crowns and the odd clearing.
        static Color Forest(float x, float z, Color tint)
        {
            float broad = Fbm(x * 0.012f, z * 0.012f);
            float crowns = Mathf.PerlinNoise(x * 0.42f + 7f, z * 0.42f + 3f);
            float fine = Mathf.PerlinNoise(x * 1.3f, z * 1.3f);
            Color canopy = Color.Lerp(new Color(0.045f, 0.06f, 0.045f), new Color(0.11f, 0.14f, 0.095f), broad);
            canopy += new Color(0.07f, 0.085f, 0.055f) * Mathf.Clamp01((crowns - 0.5f) * 2.2f) * (0.7f + 0.3f * fine);
            if (broad > 0.68f) canopy = Color.Lerp(canopy, new Color(0.2f, 0.18f, 0.13f), (broad - 0.68f) * 2.2f);   // clearings
            // Use the real ground's own colour, varied by the pattern, so the two meet without a seam.
            float shade = Mathf.Clamp01((canopy.r + canopy.g + canopy.b) / 0.45f);
            Color matched = tint * Mathf.Lerp(0.8f, 1.15f, shade);
            matched.g *= 1.04f;
            return matched;
        }

        // Solid rock around the tunnels.
        static Color Rock(float x, float z, Color tint)
        {
            float n = Fbm(x * 0.05f, z * 0.05f);
            Color rock = Color.Lerp(new Color(0.05f, 0.047f, 0.043f), new Color(0.12f, 0.112f, 0.1f), n);
            return Color.Lerp(rock, tint * 0.45f, 0.25f);
        }

        static float Fbm(float x, float z)
        {
            float total = 0f, amplitude = 0.5f, frequency = 1f;
            for (int i = 0; i < 4; i++)
            {
                total += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
                frequency *= 2.03f;
                amplitude *= 0.5f;
            }
            return total / 0.9375f;
        }

        static Texture2D Save(Texture2D finished, string scene)
        {
            string path = MapFolder + "/T_Map_" + scene + ".png";
            File.WriteAllBytes(path, finished.EncodeToPNG());
            Object.DestroyImmediate(finished);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = LongestSide;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
