using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Bakes the lighting of every zone into ONE shared Adaptive Probe Volume baking set.
    ///
    /// Why one set: Unity only keeps one baking set loaded at a time. If each zone had its own,
    /// only the first zone loaded would get its baked light when zones are streamed together.
    ///
    /// How to bake: the whole world in one pass, with Unity closed, from the command line. That is
    /// the method that works on a 16 GB laptop at 3 m probe spacing (about a minute):
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Hollowmere.EditorTools.WorldLighting.BakeWholeWorldFromCommandLine -spacing 3 -logFile Logs/bake-world.log
    /// Then check the log for "Probe cells per zone": every zone must have more than zero.
    /// Baking inside the open editor at 2 m spacing ran out of memory and froze Unity.
    ///
    /// The zone-by-zone path (BakeFromCommandLine, menu item 2) is kept for reference, but it did
    /// not reliably register the probe cells of zones baked after the first.
    /// </summary>
    public static class WorldLighting
    {
        const string ZonesFolder = "Assets/_Hollowmere/Scenes/Zones/";
        const string SetPath = "Assets/_Hollowmere/Settings/Lighting/Hollowmere Baking Set.asset";

        static readonly string[] Zones = { "Z1_Outskirts", "C1_EstateRoad", "Z2_MarrowHouse", "Z3_Undercroft", "Z4_Waterworks" };

        static string ScenePath(string zone) => ZonesFolder + zone + ".unity";

        [MenuItem("Hollowmere/World/Lighting/1 Put All Zones In One Baking Set", priority = 60)]
        public static void SetUpSharedSet()
        {
            var shared = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
            if (shared == null)
            {
                // Start from the village's set, so its settings and its existing bake are kept.
                string villageSet = ZonesFolder + Zones[0] + "/" + Zones[0] + " Baking Set.asset";
                string error = AssetDatabase.MoveAsset(villageSet, SetPath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("Could not create the shared set: " + error);
                shared = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
            }

            var serialized = new SerializedObject(shared);
            serialized.FindProperty("singleSceneMode").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Unity remembers which set each scene belongs to. A zone's old single-scene set must go,
            // or Unity keeps mapping the zone to it and leaves the zone out of the shared bake.
            var lookup = typeof(ProbeVolumeBakingSet).GetMethod("GetBakingSetForScene", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            var addScene = typeof(ProbeVolumeBakingSet).GetMethod("AddScene", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string zone in Zones)
            {
                string guid = AssetDatabase.AssetPathToGUID(ScenePath(zone));
                string ownPath = ZonesFolder + zone + "/" + zone + " Baking Set.asset";
                var own = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(ownPath);
                if (own != null && own != shared)
                {
                    own.RemoveScene(guid);
                    AssetDatabase.DeleteAsset(ownPath);
                }

                var mapped = lookup.Invoke(null, new object[] { guid }) as ProbeVolumeBakingSet;
                if (mapped != shared)
                {
                    if (mapped != null) mapped.RemoveScene(guid);
                    if (Contains(shared, guid)) shared.RemoveScene(guid);
                    addScene.Invoke(shared, new object[] { guid, null });
                }
                mapped = lookup.Invoke(null, new object[] { guid }) as ProbeVolumeBakingSet;
                Debug.Log("Hollowmere lighting: " + zone + " is mapped to " + (mapped != null ? mapped.name : "no set"));
            }

            EditorUtility.SetDirty(shared);
            AssetDatabase.SaveAssets();
            Debug.Log("Hollowmere lighting: shared set has " + shared.sceneGUIDs.Count + " scenes, probe spacing " + shared.minDistanceBetweenProbes + " m.");
        }

        [MenuItem("Hollowmere/World/Lighting/2 Bake Every Zone (one at a time)", priority = 61)]
        public static void BakeEveryZone()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Bake(Zones);
        }

        /// <summary>Entry point for a headless bake. "-zones A,B" limits it to those zones.</summary>
        public static void BakeFromCommandLine()
        {
            string[] wanted = Zones;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-zones") wanted = args[i + 1].Split(',');

            bool ok = Bake(wanted);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        const string WorldScene = "Assets/_Hollowmere/Scenes/World.unity";
        const string WorldSettings = "Assets/_Hollowmere/Settings/Lighting/LS_World.lighting";

        /// <summary>
        /// Entry point for a headless bake of the whole world in one pass: the World scene and every
        /// zone loaded together. Needs a lot of memory. "-spacing 3" sets the probe spacing in metres.
        /// </summary>
        public static void BakeWholeWorldFromCommandLine()
        {
            float spacing = 0f;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-spacing") spacing = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);

            bool ok = BakeWholeWorld(spacing);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Hollowmere/World/Lighting/3 Bake The Whole World In One Pass (heavy)", priority = 62)]
        public static void BakeWholeWorldFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BakeWholeWorld(0f);
        }

        static bool BakeWholeWorld(float spacing)
        {
            SetUpSharedSet();
            var shared = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
            string worldGuid = AssetDatabase.AssetPathToGUID(WorldScene);
            if (!Contains(shared, worldGuid)) shared.TryAddScene(worldGuid);   // the active scene must be in the set
            if (spacing > 0f) shared.minDistanceBetweenProbes = spacing;
            EditorUtility.SetDirty(shared);
            AssetDatabase.SaveAssets();

            var world = EditorSceneManager.OpenScene(WorldScene, OpenSceneMode.Single);
            foreach (string zone in Zones) EditorSceneManager.OpenScene(ScenePath(zone), OpenSceneMode.Additive);
            EditorSceneManager.SetActiveScene(world);

            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(WorldSettings);
            if (settings == null)
            {
                settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<LightingSettings>("Assets/_Hollowmere/Settings/Lighting/LS_Z1_Outskirts.lighting"));
                settings.name = "LS_World";
                AssetDatabase.CreateAsset(settings, WorldSettings);
            }
            Lightmapping.lightingSettings = settings;

            // Each zone has its own moon for previewing it alone. Only the World's moon may light the bake.
            var previewMoons = new List<Light>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene == world) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var light in root.GetComponentsInChildren<Light>(true))
                        if (light.type == LightType.Directional && light.enabled) { light.enabled = false; previewMoons.Add(light); }
            }

            // Old probe data baked with a different layout makes Unity refuse the probe stage, so start clean.
            // Clearing reloads the set asset, so fetch it again afterwards.
            Lightmapping.Clear();
            AssetDatabase.SaveAssets();
            shared = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
            if (shared == null) throw new InvalidOperationException("The shared baking set is missing after clearing.");

            // Register every loaded zone and its probe volume bounds with the shared set. Without this,
            // zones that were moved in from another set are left out of the bake.
            var ensure = typeof(ProbeVolumeBakingSet).GetMethod("EnsurePerSceneData", BindingFlags.Instance | BindingFlags.NonPublic);
            var updateBounds = typeof(ProbeVolumeBakingSet).GetMethod("UpdateSceneBounds", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                string guid = AssetDatabase.AssetPathToGUID(scene.path);
                if (updateBounds != null) updateBounds.Invoke(shared, new object[] { scene, guid, false });
                if (ensure != null) ensure.Invoke(shared, new object[] { scene, guid });
            }

            Debug.Log("Hollowmere lighting: ===== baking the whole world, " + EditorSceneManager.sceneCount + " scenes, probe spacing "
                      + shared.minDistanceBetweenProbes + " m, " + previewMoons.Count + " preview moons off =====");
            var started = DateTime.Now;
            bool ok = Lightmapping.Bake();

            foreach (var light in previewMoons) light.enabled = true;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log("Hollowmere lighting: whole world " + (ok ? "baked" : "FAILED") + " in "
                      + (DateTime.Now - started).TotalMinutes.ToString("F1") + " min. " + CellReport(shared));
            return ok;
        }

        static bool Bake(string[] zones)
        {
            SetUpSharedSet();
            var shared = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
            bool allOk = true;

            foreach (string zone in zones)
            {
                Debug.Log("Hollowmere lighting: ===== baking " + zone + " =====");
                var scene = EditorSceneManager.OpenScene(ScenePath(zone), OpenSceneMode.Single);
                var started = DateTime.Now;

                bool ok = Lightmapping.Bake();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                Debug.Log("Hollowmere lighting: " + zone + (ok ? " baked" : " FAILED") + " in "
                          + (DateTime.Now - started).TotalSeconds.ToString("F0") + " s. " + CellReport(shared));
                allOk &= ok;
                if (!ok) break;   // do not carry on over a broken set
            }

            Debug.Log("Hollowmere lighting: finished, " + (allOk ? "all requested zones baked." : "with a failure, see above."));
            return allOk;
        }

        static bool Contains(ProbeVolumeBakingSet set, string guid)
        {
            foreach (string existing in set.sceneGUIDs)
                if (existing == guid) return true;
            return false;
        }

        // How many probe cells the set holds for each zone: zero means that zone has no baked light yet.
        static string CellReport(ProbeVolumeBakingSet set)
        {
            var method = typeof(ProbeVolumeBakingSet).GetMethod("GetSceneCellIndexList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) return "";

            var parts = new List<string>();
            foreach (string zone in Zones)
            {
                var cells = method.Invoke(set, new object[] { AssetDatabase.AssetPathToGUID(ScenePath(zone)) }) as List<int>;
                parts.Add(zone + " " + (cells != null ? cells.Count : 0));
            }
            return "Probe cells per zone: " + string.Join(", ", parts) + ".";
        }
    }
}
