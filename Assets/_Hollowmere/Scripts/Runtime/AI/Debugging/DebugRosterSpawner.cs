using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.AI.Debugging
{
    /// <summary>
    /// TEMPORARY: fills a zone with dormant grey stand-ins when it loads, a few around each
    /// "SpawnPoint_" marker, so the Director has dead to wake before the real zombies exist.
    /// The stand-ins are moved into the zone's own scene, so they unload with it.
    /// Remove this once the real enemies are placed in the zones.
    /// </summary>
    public sealed class DebugRosterSpawner : MonoBehaviour
    {
        [Tooltip("Zones that get stand-ins.")]
        [SerializeField] string[] zones = { "Z1_Outskirts" };
        [SerializeField] string markerPrefix = "SpawnPoint_";
        [Tooltip("Stand-ins placed around each marker.")]
        [SerializeField] int perMarker = 3;
        [Tooltip("How far from the marker they are scattered, in metres.")]
        [SerializeField] float scatter = 5f;

        readonly HashSet<int> filled = new HashSet<int>();
        int count;

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void Start()
        {
            // Zones already open when Play starts.
            for (int i = 0; i < SceneManager.sceneCount; i++) Fill(SceneManager.GetSceneAt(i));
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Fill(scene);

        void Fill(Scene scene)
        {
            if (!scene.isLoaded || filled.Contains(scene.handle)) return;
            if (System.Array.IndexOf(zones, scene.name) < 0) return;
            filled.Add(scene.handle);

            var markers = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name.StartsWith(markerPrefix)) markers.Add(child);

            foreach (Transform marker in markers)
            {
                for (int i = 0; i < perMarker; i++)
                {
                    Vector2 offset = Random.insideUnitCircle * scatter;
                    Vector3 near = marker.position + new Vector3(offset.x, 0f, offset.y);
                    count++;
                    StandInAgent agent = StandInAgent.Create(near, Random.Range(0f, 360f), "Dead " + count);
                    SceneManager.MoveGameObjectToScene(agent.gameObject, scene);
                }
            }
        }
    }
}
