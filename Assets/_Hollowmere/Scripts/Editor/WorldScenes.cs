using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Menu shortcuts for opening the zone scenes, on their own or all together as one map.
    /// Menu: Hollowmere > World.
    /// </summary>
    public static class WorldScenes
    {
        const string ZonesFolder = "Assets/_Hollowmere/Scenes/Zones/";

        // In world order, from the player's start to the end of the game.
        static readonly string[] Zones = { "Z1_Outskirts", "C1_EstateRoad", "Z2_MarrowHouse" };

        const string WorldScene = "Assets/_Hollowmere/Scenes/World.unity";

        /// <summary>
        /// The setup to press Play in: the World scene (player, moon, fog, zone streamer) with every
        /// zone open beside it, so the whole map is visible while editing. On Play the streamer
        /// unloads the zones that are far from the player.
        /// </summary>
        [MenuItem("Hollowmere/World/Open World (press Play here)", priority = 0)]
        public static void OpenWorld()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var world = EditorSceneManager.OpenScene(WorldScene, OpenSceneMode.Single);
            foreach (string zone in Zones)
                EditorSceneManager.OpenScene(ZonesFolder + zone + ".unity", OpenSceneMode.Additive);

            // The World scene must stay the active one: its fog, sky and ambient light are the ones used.
            EditorSceneManager.SetActiveScene(world);
            FrameSceneView(new Vector3(0, 0, 125), 215);
        }

        [MenuItem("Hollowmere/World/Open All Zones (whole map)", priority = 1)]
        public static void OpenAllZones()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            for (int i = 0; i < Zones.Length; i++)
                EditorSceneManager.OpenScene(ZonesFolder + Zones[i] + ".unity", i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);

            // Several cameras and audio listeners are loaded at once; that is expected while viewing the map.
            FrameSceneView(new Vector3(0, 0, 125), 215);
        }

        [MenuItem("Hollowmere/World/Open Zone/1 Flooded Outskirts", priority = 20)]
        public static void OpenOutskirts() => OpenSingle(Zones[0], new Vector3(0, 3, 20), 60);

        [MenuItem("Hollowmere/World/Open Zone/1b Estate Road", priority = 21)]
        public static void OpenEstateRoad() => OpenSingle(Zones[1], new Vector3(8, 6, 140), 50);

        [MenuItem("Hollowmere/World/Open Zone/2 Marrow House", priority = 22)]
        public static void OpenMarrowHouse() => OpenSingle(Zones[2], new Vector3(0, 15, 240), 45);

        static void OpenSingle(string zone, Vector3 lookAt, float size)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ZonesFolder + zone + ".unity", OpenSceneMode.Single);
            FrameSceneView(lookAt, size);
        }

        static void FrameSceneView(Vector3 lookAt, float size)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.orthographic = false;
            view.LookAt(lookAt, Quaternion.Euler(35, -30, 0), size, false, true);
            view.Repaint();
        }
    }
}
