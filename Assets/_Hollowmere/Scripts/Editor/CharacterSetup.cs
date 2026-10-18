using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Sets up the character models in Art/Models/Characters in one go:
    /// texture import settings, URP Lit materials in Art/Materials/Characters, material remaps on
    /// each FBX, and one prefab per character in Prefabs/Agents.
    ///
    /// Menu: Hollowmere > Characters > Set Up Characters. Safe to run again: materials are updated
    /// in place and existing prefabs are never overwritten.
    /// </summary>
    public static class CharacterSetup
    {
        const string ModelRoot = "Assets/_Hollowmere/Art/Models/Characters";
        const string MaterialRoot = "Assets/_Hollowmere/Art/Materials/Characters";
        const string PrefabRoot = "Assets/_Hollowmere/Prefabs/Agents";

        static readonly string[] Characters = { "Marrow", "Zombie", "Biscuit", "Ghost" };

        enum Surface { Opaque, Cutout, Transparent }

        class Spec
        {
            public string Character, SourceMaterial, Material;
            public string BaseMap, Normal, MetallicSmoothness, Occlusion, Emission;
            public Surface Surface = Surface.Opaque;
            public float Smoothness = 0.3f, NormalStrength = 1f, OcclusionStrength = 1f, Alpha = 1f;
        }

        // SourceMaterial is the material name inside the FBX; it gets remapped to Material.
        static readonly Spec[] Specs =
        {
            new Spec { Character = "Marrow", SourceMaterial = "Marrow_Body", Material = "M_Marrow_Body",
                       BaseMap = "T_Marrow_Body_BaseMap.jpg", Smoothness = 0.25f },
            new Spec { Character = "Marrow", SourceMaterial = "Marrow_Head", Material = "M_Marrow_Head",
                       BaseMap = "T_Marrow_Head_BaseMap.jpg", Smoothness = 0.35f },
            ZombiePart("Body"),
            ZombiePart("Shirt"),
            ZombiePart("Pants"),
            ZombiePart("Hair", Surface.Cutout),
            new Spec { Character = "Biscuit", SourceMaterial = "Biscuit_Coat", Material = "M_Biscuit",
                       BaseMap = "T_Biscuit_BaseMap.png", Smoothness = 0.2f },
            new Spec { Character = "Ghost", SourceMaterial = "Ghost_Shroud", Material = "M_Ghost",
                       BaseMap = "T_Ghost_BaseMap.png", Normal = "T_Ghost_Normal.png", NormalStrength = 0.65f,
                       MetallicSmoothness = "T_Ghost_MetallicSmoothness.png",
                       Occlusion = "T_Ghost_Occlusion.png", OcclusionStrength = 0.35f,
                       Emission = "T_Ghost_Emission.png", Surface = Surface.Transparent, Alpha = 0.75f },
        };

        static Spec ZombiePart(string part, Surface surface = Surface.Opaque) => new Spec
        {
            Character = "Zombie", SourceMaterial = "Zombie_" + part, Material = "M_Zombie_" + part,
            BaseMap = $"T_Zombie_{part}_BaseMap.png", Normal = $"T_Zombie_{part}_Normal.png",
            MetallicSmoothness = $"T_Zombie_{part}_MetallicSmoothness.png", Surface = surface,
        };

        [MenuItem("Hollowmere/Characters/Set Up Characters", priority = 200)]
        public static void SetUp()
        {
            ConfigureTextures();

            var materials = new Dictionary<string, Material>();
            foreach (var spec in Specs)
                materials[spec.SourceMaterial] = BuildMaterial(spec);

            foreach (var character in Characters)
            {
                ConfigureModel(character, materials);
                SavePrefab(character);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterSetup] Done. Materials in {MaterialRoot}, prefabs in {PrefabRoot}.");
        }

        // Normal maps get the Normal map type; data maps (metallic/smoothness, occlusion) are linear.
        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                var name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.EndsWith("_Normal");
                bool linear = normal || name.EndsWith("_MetallicSmoothness") || name.EndsWith("_Occlusion");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;

                if (importer.textureType == type && importer.sRGBTexture == !linear && importer.maxTextureSize == 2048)
                    continue;

                importer.textureType = type;
                importer.sRGBTexture = !linear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        static Material BuildMaterial(Spec spec)
        {
            EnsureFolder(MaterialRoot);
            var path = $"{MaterialRoot}/{spec.Material}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            string textures = $"{ModelRoot}/{spec.Character}/Textures/";
            Texture2D Load(string file) =>
                string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(textures + file);

            material.SetTexture("_BaseMap", Load(spec.BaseMap));
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, spec.Alpha));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", spec.Smoothness);

            SetMap(material, "_BumpMap", Load(spec.Normal), "_NORMALMAP");
            material.SetFloat("_BumpScale", spec.NormalStrength);

            var metallicSmoothness = Load(spec.MetallicSmoothness);
            SetMap(material, "_MetallicGlossMap", metallicSmoothness, "_METALLICSPECGLOSSMAP");
            if (metallicSmoothness != null)
            {
                material.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness from the map's alpha
                material.SetFloat("_Smoothness", 1f);               // acts as a multiplier on the map
            }

            SetMap(material, "_OcclusionMap", Load(spec.Occlusion), "_OCCLUSIONMAP");
            material.SetFloat("_OcclusionStrength", spec.OcclusionStrength);

            var emission = Load(spec.Emission);
            SetMap(material, "_EmissionMap", emission, "_EMISSION");
            material.SetColor("_EmissionColor", emission != null ? Color.white : Color.black);
            material.globalIlluminationFlags = emission != null
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            ApplySurface(material, spec.Surface);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetMap(Material material, string property, Texture texture, string keyword)
        {
            material.SetTexture(property, texture);
            if (texture != null)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        // Mirrors what the URP Lit inspector sets for Opaque, Alpha Clipping and Transparent (Alpha blend).
        static void ApplySurface(Material material, Surface surface)
        {
            bool transparent = surface == Surface.Transparent;
            bool cutout = surface == Surface.Cutout;

            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.SetFloat("_Cull", (float)(transparent ? CullMode.Off : CullMode.Back));

            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHATEST_ON", cutout);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent
                : cutout ? (int)RenderQueue.AlphaTest
                : -1; // use the shader's default queue
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        // The models are static for now (no rig, no clips). Remap the FBX materials onto ours.
        static void ConfigureModel(string character, Dictionary<string, Material> materials)
        {
            var path = $"{ModelRoot}/{character}/SM_{character}.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[CharacterSetup] Missing model: {path}");
                return;
            }

            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            foreach (var spec in Specs)
            {
                if (spec.Character != character)
                    continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.SourceMaterial);
                importer.AddRemap(id, materials[spec.SourceMaterial]);
            }

            importer.SaveAndReimport();
        }

        static void SavePrefab(string character)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{character}/SM_{character}.fbx");
            if (model == null)
                return;

            EnsureFolder(PrefabRoot);
            var prefabPath = $"{PrefabRoot}/PF_{character}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return; // never overwrite a prefab someone may already have edited

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "PF_" + character;
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
