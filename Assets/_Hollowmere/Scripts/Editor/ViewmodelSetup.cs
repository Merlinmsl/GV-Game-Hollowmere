using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Hollowmere.AI.Senses;
using Hollowmere.Player;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Hollowmere > Weapons > Set Up Viewmodels. Imports the pistol and the first-person arms, builds the
    /// effect materials, makes one viewmodel prefab per weapon (gun, arms, muzzle flash or ice spray,
    /// sounds) and puts both under PF_Player's Head camera in WeaponSystem's Pistol Model / Ice Gun Model
    /// slots, with the crosshair on the player. Safe to run again: everything is rebuilt in place.
    /// </summary>
    public static class ViewmodelSetup
    {
        const string Models = "Assets/_Hollowmere/Art/Models";
        const string Vfx = "Assets/_Hollowmere/Art/VFX";
        const string Sounds = "Assets/_Hollowmere/Audio/Weapons";
        const string PrefabFolder = "Assets/_Hollowmere/Prefabs/Player";
        const string PlayerPrefab = "Assets/_Hollowmere/Prefabs/PF_Player.prefab";
        public const string PistolPrefab = PrefabFolder + "/PF_Viewmodel_Pistol.prefab";
        public const string IceGunPrefab = PrefabFolder + "/PF_Viewmodel_IceGun.prefab";

        // Where each grip sits in front of the eye (right, up, forward) and how the gun is tilted. The same
        // numbers drive the Blender previews in SourceArt/FPArms/Screenshots.
        public static readonly Vector3 PistolOffset = new Vector3(0.095f, -0.098f, 0.215f);
        public static readonly Vector3 IceGunOffset = new Vector3(0.120f, -0.140f, 0.285f);
        public static readonly Vector3 HoldEuler = new Vector3(2f, -3f, 3f);

        [MenuItem("Hollowmere/Weapons/Set Up Viewmodels", priority = 230)]
        public static void SetUpAll()
        {
            ImportArt();
            var flash = ParticleMaterial("M_MuzzleFlash", "T_MuzzleFlash", additive: true);
            var flashSide = ParticleMaterial("M_MuzzleFlashSide", "T_MuzzleFlash_Side", additive: true);
            var mist = ParticleMaterial("M_IceMist", "T_SoftPuff", additive: false);
            var smoke = ParticleMaterial("M_GunSmoke", "T_SoftPuff", additive: false);
            EnsureFolder(PrefabFolder);
            var pistol = BuildPistol(flash, flashSide, smoke);
            var iceGun = BuildIceGun(mist);
            HookIntoPlayer(pistol, iceGun);
            AssetDatabase.SaveAssets();
            Debug.Log("[ViewmodelSetup] Viewmodels built and placed under PF_Player's Head camera.");
        }

        // ------------------------------------------------------------------ art import

        static void ImportArt()
        {
            ImportTextures("Pistol", 2048);
            ImportTextures("FPArms", 2048);
            var pistolMaterial = PropSetup.BuildMaterial("Pistol", $"{Models}/Pistol/Textures/T_Pistol_");
            var armsMaterial = PropSetup.BuildMaterial("FPArms", $"{Models}/FPArms/Textures/T_FPArms_",
                                                       "Assets/_Hollowmere/Art/Materials/Characters");
            ImportModel($"{Models}/Pistol/SM_Pistol.fbx", "M_Pistol", pistolMaterial);
            ImportModel($"{Models}/FPArms/SM_FPArms_Pistol.fbx", "M_FPArms", armsMaterial);
            ImportModel($"{Models}/FPArms/SM_FPArms_IceGun.fbx", "M_FPArms", armsMaterial);

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { $"{Vfx}/Textures" }))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Sounds }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.forceToMono = true;
                importer.loadInBackground = false;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;   // short clips that must play instantly
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        static void ImportTextures(string name, int size)
        {
            string prefix = $"{Models}/{name}/Textures/T_{name}_";
            PropSetup.ConfigureTexture(prefix + "BaseMap.png", size, TextureImporterType.Default, true);
            PropSetup.ConfigureTexture(prefix + "Normal.png", size, TextureImporterType.NormalMap, false);
            PropSetup.ConfigureTexture(prefix + "MaskMap.png", size, TextureImporterType.Default, false);
        }

        static void ImportModel(string path, string sourceMaterial, Material material)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale = 1f;
            importer.meshCompression = ModelImporterMeshCompression.Low;   // seen a hand's length from the eye
            importer.isReadable = false;
            importer.generateSecondaryUV = false;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterial), material);
            importer.SaveAndReimport();
        }

        // Unlit particle material: additive for fire, alpha-blended for smoke and mist.
        static Material ParticleMaterial(string name, string texture, bool additive)
        {
            EnsureFolder($"{Vfx}/Materials");
            string path = $"{Vfx}/Materials/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Vfx}/Textures/{texture}.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);    // URP blend modes: 0 alpha, 2 additive
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ viewmodels

        static GameObject BuildPistol(Material flash, Material flashSide, Material smokeMaterial)
        {
            var root = NewViewmodel("PF_Viewmodel_Pistol", out var pose, out var audio, out var fill);
            try
            {
                var gun = Place($"{Models}/Pistol/SM_Pistol.fbx", "Pistol", pose);
                var arms = Place($"{Models}/FPArms/SM_FPArms_Pistol.fbx", "Arms", pose);
                Transform muzzle = gun.transform.Find("Muzzle");

                var fx = new GameObject("MuzzleFlash").transform;
                fx.SetParent(muzzle, false);
                fx.rotation = pose.rotation;      // face along the barrel whatever the exported empty's axes
                Quad("Front", fx, flash, new Vector3(0f, 0f, 0.004f), Quaternion.identity, new Vector3(0.12f, 0.12f, 1f));
                var side = Quaternion.Euler(0f, -90f, 0f);    // the flame texture runs from the muzzle forward
                Quad("Side", fx, flashSide, new Vector3(0f, 0f, 0.08f), side, new Vector3(0.16f, 0.08f, 1f));
                Quad("Top", fx, flashSide, new Vector3(0f, 0f, 0.08f), Quaternion.Euler(0f, 0f, 90f) * side,
                     new Vector3(0.16f, 0.08f, 1f));
                fx.gameObject.SetActive(false);

                var light = ShotLight(muzzle, new Color(1f, 0.72f, 0.42f), 7f);
                var smoke = Smoke(muzzle, smokeMaterial);

                var so = new SerializedObject(root.GetComponent<WeaponViewmodel>());
                so.FindProperty("kind").enumValueIndex = (int)WeaponKind.Pistol;
                Set(so, "pose", pose);
                Set(so, "fillLight", fill);
                Set(so, "slide", gun.transform.Find("Pistol_Slide"));
                Set(so, "magazine", gun.transform.Find("Pistol_Magazine"));
                Set(so, "leftArm", arms.transform.Find("LeftArm_Pistol"));
                so.FindProperty("magazineOut").vector3Value = new Vector3(0f, -0.155f, -0.039f);   // down the grip
                so.FindProperty("leftArmReach").vector3Value = new Vector3(-0.04f, -0.16f, -0.08f);
                Set(so, "muzzleFlash", fx.gameObject);
                Set(so, "shotLight", light);
                so.FindProperty("shotLightIntensity").floatValue = 4f;
                so.FindProperty("flashSeconds").floatValue = 0.05f;
                SetEffects(so, smoke);
                so.FindProperty("kickBack").floatValue = 0.035f;
                so.FindProperty("kickUp").floatValue = 7f;
                so.FindProperty("reloadSeconds").floatValue = 1.5f;
                SetSounds(so, audio, "SFX_Pistol_Fire", "SFX_Pistol_Empty", "SFX_Pistol_Reload");
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, PistolPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject BuildIceGun(Material mist)
        {
            var root = NewViewmodel("PF_Viewmodel_IceGun", out var pose, out var audio, out var fill);
            try
            {
                var gun = Place($"{Models}/IceGun/SM_IceGun.fbx", "IceGun", pose);
                var arms = Place($"{Models}/FPArms/SM_FPArms_IceGun.fbx", "Arms", pose);

                // The nozzle: the front of the barrel, on the barrel's axis (2.9 cm above the grip origin).
                Bounds bounds = gun.GetComponentInChildren<Renderer>().bounds;
                var nozzle = new GameObject("Nozzle").transform;
                nozzle.SetParent(gun.transform, false);
                nozzle.position = new Vector3(0f, 0.029f, bounds.max.z - 0.002f);
                nozzle.rotation = pose.rotation;

                var light = ShotLight(nozzle, new Color(0.6f, 0.85f, 1f), 5f);
                var spray = IceSpray(nozzle, mist);
                var crystals = IceCrystals(nozzle, mist);

                var so = new SerializedObject(root.GetComponent<WeaponViewmodel>());
                so.FindProperty("kind").enumValueIndex = (int)WeaponKind.IceGun;
                Set(so, "pose", pose);
                Set(so, "fillLight", fill);
                Set(so, "leftArm", arms.transform.Find("LeftArm_IceGun"));
                so.FindProperty("leftArmReach").vector3Value = new Vector3(0.0f, -0.1f, -0.06f);
                Set(so, "shotLight", light);
                so.FindProperty("shotLightIntensity").floatValue = 2.5f;
                so.FindProperty("flashSeconds").floatValue = 0.25f;
                SetEffects(so, spray, crystals);
                so.FindProperty("kickBack").floatValue = 0.02f;
                so.FindProperty("kickUp").floatValue = 3f;
                so.FindProperty("reloadSeconds").floatValue = 1.4f;
                SetSounds(so, audio, "SFX_IceGun_Fire", "SFX_IceGun_Empty", "SFX_IceGun_Recharge");
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, IceGunPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject NewViewmodel(string name, out Transform pose, out AudioSource audio, out Light fill)
        {
            var root = new GameObject(name);
            root.AddComponent<WeaponViewmodel>();
            audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;        // the player's own gun: heard in the head, not placed in the world
            audio.dopplerLevel = 0f;
            audio.volume = 0.85f;
            audio.priority = 64;
            pose = new GameObject("Pose").transform;
            pose.SetParent(root.transform, false);

            // The torch's spill on the gun: just above the eye, reaching only the viewmodel's rendering layer.
            var go = new GameObject("FillLight");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(-0.1f, 0.12f, -0.12f);
            fill = go.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.95f, 0.86f);
            fill.range = 2f;
            fill.intensity = 0.3f;
            fill.shadows = LightShadows.None;
            // URP reads a light's rendering layers from its additional data, not from Light.renderingLayerMask.
            fill.GetUniversalAdditionalLightData().renderingLayers = WeaponViewmodel.RenderingLayer;
            return root;
        }

        static GameObject Place(string modelPath, string name, Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            instance.name = name;
            return instance;
        }

        static GameObject Save(GameObject root, string path)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // Arms that cast shadows would black out the middle of the torch beam.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                if (renderer is MeshRenderer) renderer.renderingLayerMask = WeaponViewmodel.RenderingLayer;
            }
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }

        static void Set(SerializedObject so, string field, Object value) => so.FindProperty(field).objectReferenceValue = value;

        static void SetEffects(SerializedObject so, params ParticleSystem[] effects)
        {
            var list = so.FindProperty("fireEffects");
            list.arraySize = effects.Length;
            for (int i = 0; i < effects.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = effects[i];
        }

        static void SetSounds(SerializedObject so, AudioSource audio, string fire, string empty, string reload)
        {
            Set(so, "audioSource", audio);
            Set(so, "fireClip", Clip(fire));
            Set(so, "emptyClip", Clip(empty));
            Set(so, "reloadClip", Clip(reload));
            Set(so, "equipClip", Clip("SFX_Weapon_Equip"));
        }

        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Sounds}/{name}.wav");

        // ------------------------------------------------------------------ effects

        static void Quad(string name, Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Light ShotLight(Transform parent, Color colour, float range)
        {
            var go = new GameObject("ShotLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.GetUniversalAdditionalLightData().renderingLayers = 1u | WeaponViewmodel.RenderingLayer;   // the room and the gun
            light.enabled = false;
            return light;
        }

        static ParticleSystem Particles(string name, Transform parent, Material material, float maxScreenSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // the cloud stays put when the player turns
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = maxScreenSize;
            return ps;
        }

        static Gradient Fade(Color start, Color end, params (float time, float alpha)[] alphas)
        {
            var gradient = new Gradient();
            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
            gradient.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) }, alphaKeys);
            return gradient;
        }

        static ParticleSystem.MinMaxCurve Grow(float from, float to) =>
            new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));

        static ParticleSystem Smoke(Transform muzzle, Material material)
        {
            var ps = Particles("Smoke", muzzle, material, 0.6f);
            ps.transform.rotation = muzzle.parent.rotation;
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new Color(0.75f, 0.74f, 0.72f, 0.22f);
            main.gravityModifier = -0.03f;
            main.maxParticles = 30;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.004f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.5f, 3f);
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(Color.white, Color.white, (0f, 0f), (0.08f, 1f), (1f, 0f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 1.5f;
            return ps;
        }

        static ParticleSystem IceSpray(Transform nozzle, Material material)
        {
            var ps = Particles("IceSpray", nozzle, material, 1f);
            var main = ps.main;
            main.duration = 0.28f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.97f, 1f, 0.7f), new Color(0.72f, 0.88f, 1f, 0.6f));
            main.gravityModifier = 0.12f;       // cold mist sinks
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.rateOverTime = 400f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;                   // a little inside WeaponSystem's 10 degree freeze cone
            shape.radius = 0.01f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 2f;
            limit.dampen = 0.08f;              // the jet slows and billows into a cloud
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Grow(0.6f, 5f);
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(Color.white, new Color(0.8f, 0.9f, 1f), (0f, 0f), (0.06f, 0.85f), (0.6f, 0.4f), (1f, 0f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;
            return ps;
        }

        static ParticleSystem IceCrystals(Transform nozzle, Material material)
        {
            var ps = Particles("IceCrystals", nozzle, material, 0.05f);
            var main = ps.main;
            main.duration = 0.22f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(11f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.startColor = new Color(0.95f, 0.98f, 1f, 1f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 160f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.008f;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(Color.white, Color.white, (0f, 1f), (0.7f, 1f), (1f, 0f));
            return ps;
        }

        // ------------------------------------------------------------------ the player

        static void HookIntoPlayer(GameObject pistolPrefab, GameObject iceGunPrefab)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var head = root.transform.Find("Head");
                var old = head.Find("Viewmodels");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var holder = new GameObject("Viewmodels").transform;
                holder.SetParent(head, false);

                var pistol = (GameObject)PrefabUtility.InstantiatePrefab(pistolPrefab, holder);
                pistol.transform.localPosition = PistolOffset;
                pistol.transform.localRotation = Quaternion.Euler(HoldEuler);
                pistol.SetActive(false);          // WeaponSystem shows the one in hand
                var iceGun = (GameObject)PrefabUtility.InstantiatePrefab(iceGunPrefab, holder);
                iceGun.transform.localPosition = IceGunOffset;
                iceGun.transform.localRotation = Quaternion.Euler(HoldEuler);
                iceGun.SetActive(false);

                var so = new SerializedObject(root.GetComponent<WeaponSystem>());
                Set(so, "pistolModel", pistol);
                Set(so, "iceGunModel", iceGun);
                so.ApplyModifiedPropertiesWithoutUndo();
                if (root.GetComponent<Crosshair>() == null) root.AddComponent<Crosshair>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ screenshots

        /// <summary>
        /// Renders the player's camera with each weapon, at rest and firing, in a plain dim room. Saved to
        /// SourceArt/FPArms/Screenshots so the in-engine look sits next to the Blender renders.
        /// </summary>
        [MenuItem("Hollowmere/Weapons/Save Viewmodel Screenshots", priority = 231)]
        public static void SaveScreenshots()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.065f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 1.5f, 5f);
            wall.transform.localScale = new Vector3(12f, 3f, 0.2f);
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.intensity = 0.3f;
            moon.color = new Color(0.7f, 0.78f, 1f);
            moon.transform.rotation = Quaternion.Euler(40f, 30f, 0f);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab));
            var camera = player.GetComponentInChildren<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.017f, 0.022f);
            foreach (var torch in camera.GetComponentsInChildren<Light>(true))
                if (torch.type == LightType.Spot) torch.enabled = true;       // the player's torch, switched on
            var so = new SerializedObject(player.GetComponent<WeaponSystem>());
            var pistol = (GameObject)so.FindProperty("pistolModel").objectReferenceValue;
            var iceGun = (GameObject)so.FindProperty("iceGunModel").objectReferenceValue;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SourceArt", "FPArms", "Screenshots"));
            Directory.CreateDirectory(folder);

            foreach (var (model, other, label) in new[] { (pistol, iceGun, "pistol"), (iceGun, pistol, "icegun") })
            {
                other.SetActive(false);
                model.SetActive(true);
                Shoot(camera, Path.Combine(folder, $"06_unity_{label}.png"));
                var vm = new SerializedObject(model.GetComponent<WeaponViewmodel>());
                var flash = vm.FindProperty("muzzleFlash").objectReferenceValue as GameObject;
                var light = vm.FindProperty("shotLight").objectReferenceValue as Light;
                if (vm.FindProperty("fillLight").objectReferenceValue is Light fill) fill.enabled = true;
                if (flash != null) flash.SetActive(true);
                if (light != null)
                {
                    light.enabled = true;
                    light.intensity = vm.FindProperty("shotLightIntensity").floatValue;
                }
                var effects = vm.FindProperty("fireEffects");
                for (int i = 0; i < effects.arraySize; i++)
                    (effects.GetArrayElementAtIndex(i).objectReferenceValue as ParticleSystem)?.Simulate(0.18f, true, true);
                Shoot(camera, Path.Combine(folder, $"07_unity_{label}_firing.png"));
                if (flash != null) flash.SetActive(false);
                if (light != null) light.enabled = false;
            }
            Debug.Log($"[ViewmodelSetup] Screenshots saved to {folder}");
        }

        static void Shoot(Camera camera, string path)
        {
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            target.Release();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
