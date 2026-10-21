using UnityEditor;
using UnityEngine;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Hollowmere > Props > Set Up Core Models. Applies the Core Developer guide's import settings to the
    /// custom props, builds their URP materials from the baked maps and makes their prefabs with box colliders.
    /// Safe to run again.
    /// </summary>
    public static class PropSetup
    {
        const string ModelRoot = "Assets/_Hollowmere/Art/Models";
        const string MaterialRoot = "Assets/_Hollowmere/Art/Materials/Props";

        struct Prop
        {
            public string Name;
            public int TextureSize;      // 2048 for the hero gun seen close up, 1024 for props
            public bool IsStatic;        // furniture that never moves gets lightmap UVs and static flags
            public string PrefabFolder;
        }

        static readonly Prop[] Props =
        {
            new Prop { Name = "IceGun", TextureSize = 2048, IsStatic = false, PrefabFolder = "Assets/_Hollowmere/Prefabs/Interactables" },
            new Prop { Name = "SpecimenJar", TextureSize = 1024, IsStatic = true, PrefabFolder = "Assets/_Hollowmere/Prefabs/Environment" },
        };

        [MenuItem("Hollowmere/Props/Set Up Core Models", priority = 220)]
        public static void SetUpAll()
        {
            if (!AssetDatabase.IsValidFolder(MaterialRoot))
                AssetDatabase.CreateFolder("Assets/_Hollowmere/Art/Materials", "Props");

            foreach (var prop in Props)
                SetUp(prop);
            AssetDatabase.SaveAssets();
        }

        static void SetUp(Prop prop)
        {
            string folder = $"{ModelRoot}/{prop.Name}";
            string textures = $"{folder}/Textures/T_{prop.Name}_";
            ConfigureTexture(textures + "BaseMap.png", prop.TextureSize, TextureImporterType.Default, true);
            ConfigureTexture(textures + "Normal.png", prop.TextureSize, TextureImporterType.NormalMap, false);
            ConfigureTexture(textures + "MaskMap.png", prop.TextureSize, TextureImporterType.Default, false);

            var material = BuildMaterial(prop.Name, textures);

            string modelPath = $"{folder}/SM_{prop.Name}.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.globalScale = 1f;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.isReadable = false;
            importer.generateSecondaryUV = prop.IsStatic;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_" + prop.Name), material);
            importer.SaveAndReimport();

            BuildPrefab(prop, AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        }

        internal static void ConfigureTexture(string path, int size, TextureImporterType type, bool sRGB)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = sRGB;   // the mask map holds data, not colour, so it must stay linear
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Compressed;   // Normal Quality
            importer.SaveAndReimport();
        }

        // URP Lit reads metallic from red and smoothness from alpha of the metallic map, and occlusion from green,
        // so the one packed mask map feeds both slots.
        internal static Material BuildMaterial(string name, string textures, string folder = MaterialRoot)
        {
            string path = $"{folder}/M_{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;

            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "BaseMap.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "Normal.png");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "MaskMap.png");

            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_MainTex", baseMap);
            material.SetColor("_BaseColor", Color.white);
            SetMap(material, "_BumpMap", normal, "_NORMALMAP");
            material.SetFloat("_BumpScale", 1f);
            SetMap(material, "_MetallicGlossMap", mask, "_METALLICSPECGLOSSMAP");
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Smoothness", 1f);
            SetMap(material, "_OcclusionMap", mask, "_OCCLUSIONMAP");
            material.SetFloat("_OcclusionStrength", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetMap(Material material, string property, Texture texture, string keyword)
        {
            material.SetTexture(property, texture);
            if (texture != null) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        // An empty root with the model inside, so the model keeps its import rotation, and a box collider
        // (never a mesh collider for a small prop) fitted to the mesh.
        static void BuildPrefab(Prop prop, GameObject model)
        {
            var root = new GameObject("PF_" + prop.Name);
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = "Model";
                var mesh = body.GetComponentInChildren<MeshFilter>().sharedMesh;

                // The gun's barrel must point along +Z, the way the player faces. Turn it round if the export did not.
                if (prop.Name == "IceGun" && mesh.bounds.center.z < 0f)
                    body.transform.localRotation *= Quaternion.Euler(0f, 180f, 0f);

                Bounds bounds = body.GetComponentInChildren<MeshRenderer>().bounds;   // the root sits at the origin
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;

                if (prop.IsStatic)
                {
                    var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                StaticEditorFlags.OccludeeStatic;
                    GameObjectUtility.SetStaticEditorFlags(root, flags);
                    GameObjectUtility.SetStaticEditorFlags(body, flags);
                }

                PrefabUtility.SaveAsPrefabAsset(root, $"{prop.PrefabFolder}/PF_{prop.Name}.prefab");
                Debug.Log($"PF_{prop.Name}: {mesh.GetIndexCount(0) / 3} triangles, size {bounds.size}, " +
                          $"mesh centre z {mesh.bounds.center.z:F3}, textures {prop.TextureSize}px");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
