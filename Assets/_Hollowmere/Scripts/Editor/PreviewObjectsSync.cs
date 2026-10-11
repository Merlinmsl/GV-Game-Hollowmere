using Hollowmere.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Keeps the editor view honest. Every zone carries its own moon, camera and post-processing so
    /// it can be opened alone. In Play mode those switch themselves off when the World scene is
    /// present, but while editing they used to stay on: with five zones open, five moons lit the
    /// scene and nothing looked like the game.
    /// This switches the zones' preview objects off whenever the World scene is open in the editor,
    /// and back on when it is not.
    /// </summary>
    [InitializeOnLoad]
    public static class PreviewObjectsSync
    {
        static PreviewObjectsSync()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => Sync();
            EditorSceneManager.sceneClosed += scene => EditorApplication.delayCall += Sync;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) Sync();
            };
            EditorApplication.delayCall += Sync;
        }

        [MenuItem("Hollowmere/World/Refresh Preview Objects", priority = 42)]
        public static void Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            bool worldIsOpen = Object.FindAnyObjectByType<ZoneStreamer>() != null;
            foreach (var preview in Object.FindObjectsByType<StandalonePreviewOnly>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool shouldBeActive = !worldIsOpen;
                if (preview.gameObject.activeSelf != shouldBeActive)
                    preview.gameObject.SetActive(shouldBeActive);
            }
        }
    }
}
