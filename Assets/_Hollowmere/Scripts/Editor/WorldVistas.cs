using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Builds the far-off scenery the player sees from a distance, before the real zone is loaded:
    /// the silhouette of Marrow House on its hill, a lit window, and the red light at the Waterworks.
    /// The silhouette is photographed from the real zone, so run this again after the zone changes shape.
    /// Menu: Hollowmere > World > Rebuild Vistas.
    /// </summary>
    public static class WorldVistas
    {
        const string WorldScene = "Assets/_Hollowmere/Scenes/World.unity";
        const string MarrowScene = "Assets/_Hollowmere/Scenes/Zones/Z2_MarrowHouse.unity";
        const string ShaderName = "Hollowmere/Vista";
        const string TextureFolder = "Assets/_Hollowmere/Art/Textures/Environment";
        const string MaterialFolder = "Assets/_Hollowmere/Art/Materials/Environment";
        const string MarrowMask = TextureFolder + "/T_Vista_MarrowHill.png";

        // The slice of the world that the Marrow House silhouette covers, seen from the south.
        const float PanelWidth = 160f;
        const float PanelHeight = 50f;
        const float PanelCentreY = 27f;
        const float PanelZ = 228f;            // the front face of the house
        const float CaptureFromZ = 181f;      // just inside the zone, so the estate road is left out
        const float CaptureDepth = 135f;
        const int MaskWidth = 2048;
        const int MaskHeight = 640;

        // The silhouette is shown larger than life so that it rises above the forest when seen from
        // the village. It has faded out long before the player is close enough to compare.
        const float ShownScale = 1.9f;
        const float ShownBaseY = 8f;

        [MenuItem("Hollowmere/World/Rebuild Vistas", priority = 41)]
        public static void RebuildAll()
        {
            var world = SceneManager.GetSceneByPath(WorldScene);
            if (!world.isLoaded)
            {
                Debug.LogError("Open the World scene first (Hollowmere > World > Open World).");
                return;
            }

            CaptureMarrowMask();
            BuildVistaObjects(world);
            EditorSceneManager.MarkSceneDirty(world);
            Debug.Log("Vistas rebuilt.");
        }

        /// <summary>Photographs the Marrow House zone from the south and saves its outline as a mask.</summary>
        static void CaptureMarrowMask()
        {
            var zone = SceneManager.GetSceneByPath(MarrowScene);
            bool openedHere = !zone.isLoaded;
            if (openedHere) zone = EditorSceneManager.OpenScene(MarrowScene, OpenSceneMode.Additive);

            var cameraObject = new GameObject("VistaCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, PanelCentreY, CaptureFromZ), Quaternion.identity);
            camera.orthographic = true;
            camera.orthographicSize = PanelHeight * 0.5f;
            camera.aspect = PanelWidth / PanelHeight;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = CaptureDepth;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            camera.enabled = false;

            var target = new RenderTexture(MaskWidth, MaskHeight, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;

            bool fogWasOn = RenderSettings.fog;
            RenderSettings.fog = false;

            // The same view on black and on white: a pixel that does not change is covered by geometry.
            Color32[] onBlack = RenderOnto(camera, target, Color.black);
            Color32[] onWhite = RenderOnto(camera, target, Color.white);

            RenderSettings.fog = fogWasOn;
            camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject);
            target.Release();
            Object.DestroyImmediate(target);
            if (openedHere) EditorSceneManager.CloseScene(zone, true);

            var mask = new Color32[MaskWidth * MaskHeight];
            for (int x = 0; x < MaskWidth; x++)
            {
                bool below = false;
                for (int y = MaskHeight - 1; y >= 0; y--)
                {
                    int i = y * MaskWidth + x;
                    int change = Mathf.Max(onWhite[i].r - onBlack[i].r, Mathf.Max(onWhite[i].g - onBlack[i].g, onWhite[i].b - onBlack[i].b));
                    byte covered = (byte)(255 - Mathf.Clamp(change, 0, 255));
                    // Everything under the skyline is solid ground, so close any gaps below it.
                    if (covered > 200 && y < MaskHeight * 0.45f) below = true;
                    mask[i] = new Color32(255, 255, 255, below ? (byte)255 : covered);
                }
            }

            var texture = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false);
            texture.SetPixels32(mask);
            texture.Apply();
            Directory.CreateDirectory(TextureFolder);
            File.WriteAllBytes(MarrowMask, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(MarrowMask, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MarrowMask);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static Color32[] RenderOnto(Camera camera, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = readback.GetPixels32();
            Object.DestroyImmediate(readback);
            return pixels;
        }

        static void BuildVistaObjects(Scene world)
        {
            GameObject root = null;
            foreach (var candidate in world.GetRootGameObjects())
                if (candidate.name == "_Vistas") root = candidate;
            if (root != null) Object.DestroyImmediate(root);

            root = new GameObject("_Vistas");
            SceneManager.MoveGameObjectToScene(root, world);

            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MarrowMask);

            // Marrow House and its hill, as a dark shape against the sky.
            var silhouette = VistaMaterial("M_Vista_Silhouette", m =>
            {
                m.SetTexture("_MainTex", mask);
                // A little paler than the fogged tree line in front of it, darker than the sky behind.
                // The colour property is HDR, so it must be given in linear space.
                m.SetColor("_Color", new Color(0.17f, 0.20f, 0.235f, 1f).linear);
                m.SetFloat("_Opacity", 0.95f);
                m.SetFloat("_FadeStart", 150f);
                m.SetFloat("_FadeEnd", 100f);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            });
            Panel(root, "Vista_MarrowHill", silhouette,
                Shown(0f, PanelCentreY, PanelZ), new Vector2(PanelWidth, PanelHeight) * ShownScale);

            // One lit window on the upper floor: somebody is home.
            var window = VistaMaterial("M_Vista_WindowGlow", m =>
            {
                m.SetFloat("_Glow", 1f);
                m.SetFloat("_Billboard", 1f);
                m.SetColor("_Color", new Color(9f, 3.6f, 0.7f, 1f));
                m.SetFloat("_Opacity", 0.9f);
                m.SetFloat("_FadeStart", 150f);
                m.SetFloat("_FadeEnd", 100f);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            });
            Panel(root, "Vista_MarrowWindow", window, Shown(6f, 35.2f, PanelZ - 0.5f), new Vector2(9f, 9f));

            // The warning light on the Waterworks radio mast, north-west of the house.
            var beacon = VistaMaterial("M_Vista_Beacon", m =>
            {
                m.SetFloat("_Glow", 1f);
                m.SetFloat("_Billboard", 1f);
                m.SetColor("_Color", new Color(5f, 0.25f, 0.15f, 1f));
                m.SetFloat("_Opacity", 1f);
                m.SetFloat("_FadeStart", 60f);
                m.SetFloat("_FadeEnd", 40f);
                m.SetFloat("_PulseSpeed", 2.1f);
                m.SetFloat("_PulseMin", 0.12f);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            });
            Panel(root, "Vista_WaterworksBeacon", beacon, WaterworksBeacon, new Vector2(9f, 9f));
        }

        /// <summary>Where the top of the Waterworks radio mast will stand. Zone 4 is built around this point.</summary>
        public static readonly Vector3 WaterworksBeacon = new Vector3(-160f, 82f, 245f);

        /// <summary>Maps a real position on the Marrow House panel to where the enlarged silhouette shows it.</summary>
        static Vector3 Shown(float x, float y, float z)
        {
            float panelBottom = PanelCentreY - PanelHeight * 0.5f;
            return new Vector3(x * ShownScale, ShownBaseY + (y - panelBottom) * ShownScale, z);
        }

        static Material VistaMaterial(string name, System.Action<Material> setup)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(ShaderName));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(ShaderName);
            setup(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Panel(GameObject root, string name, Material material, Vector3 position, Vector2 size)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = name;
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(root.transform, false);
            panel.transform.position = position;
            panel.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = panel.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
