using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.World
{
    /// <summary>
    /// Keeps the world loaded around the player. Each zone is a scene placed at its real position;
    /// a zone is loaded when the player comes within its load distance and unloaded once the player
    /// is beyond its unload distance. The gap between the two distances stops a zone flickering
    /// in and out when the player stands near the threshold.
    /// Lives in the persistent World scene. The player is found by tag, so any player prefab works.
    /// </summary>
    public sealed class ZoneStreamer : MonoBehaviour
    {
        [Serializable]
        public sealed class Zone
        {
            [Tooltip("Scene name, as listed in the build settings.")]
            public string sceneName;
            [Tooltip("The world-space box this zone occupies.")]
            public Bounds bounds;
            [Tooltip("Load when the player is closer than this to the zone's box (metres).")]
            public float loadDistance = 60f;
            [Tooltip("Unload when the player is further than this. Must be larger than the load distance.")]
            public float unloadDistance = 80f;

            [NonSerialized] public State state;
        }

        public enum State { Unloaded, Loading, Loaded, Unloading }

        [SerializeField] Zone[] zones = Array.Empty<Zone>();
        [SerializeField] string playerTag = "Player";
        [Tooltip("Seconds between distance checks. Streaming does not need to run every frame.")]
        [SerializeField] float checkInterval = 0.25f;
        [Tooltip("Keep the player switched off until the zones around the start point have loaded, so it cannot fall through unloaded ground.")]
        [SerializeField] bool holdPlayerUntilReady = true;

        /// <summary>Raised with the scene name after a zone finishes loading.</summary>
        public event Action<string> ZoneLoaded;
        /// <summary>Raised with the scene name just before a zone is unloaded.</summary>
        public event Action<string> ZoneUnloading;

        /// <summary>True while no zone is in the middle of loading or unloading.</summary>
        public bool IsSettled
        {
            get
            {
                foreach (var zone in zones)
                    if (zone.state == State.Loading || zone.state == State.Unloading) return false;
                return true;
            }
        }

        Transform player;
        float nextCheck;

        IEnumerator Start()
        {
            // Zones already open (for example when testing with several scenes loaded in the Editor) count as loaded.
            foreach (var zone in zones)
                zone.state = SceneManager.GetSceneByName(zone.sceneName).isLoaded ? State.Loaded : State.Unloaded;

            FindPlayer();
            if (player == null || !holdPlayerUntilReady) yield break;

            var playerObject = player.gameObject;
            playerObject.SetActive(false);
            UpdateZones(player.position);
            yield return null;
            while (!IsSettled) yield return null;
            playerObject.SetActive(true);
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + checkInterval;

            if (player == null) FindPlayer();
            if (player != null && player.gameObject.activeInHierarchy) UpdateZones(player.position);
        }

        void FindPlayer()
        {
            var found = GameObject.FindGameObjectWithTag(playerTag);
            player = found != null ? found.transform : null;
        }

        void UpdateZones(Vector3 position)
        {
            foreach (var zone in zones)
            {
                float distance = HorizontalDistance(zone.bounds, position);
                if (zone.state == State.Unloaded && distance <= zone.loadDistance)
                    StartCoroutine(Load(zone));
                else if (zone.state == State.Loaded && distance > zone.unloadDistance)
                    StartCoroutine(Unload(zone));
            }
        }

        // Height is ignored: a zone under or over the player (the Undercroft beneath the house) is still "near".
        static float HorizontalDistance(Bounds bounds, Vector3 position)
        {
            float dx = Mathf.Max(bounds.min.x - position.x, 0f, position.x - bounds.max.x);
            float dz = Mathf.Max(bounds.min.z - position.z, 0f, position.z - bounds.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        IEnumerator Load(Zone zone)
        {
            zone.state = State.Loading;
            yield return SceneManager.LoadSceneAsync(zone.sceneName, LoadSceneMode.Additive);
            zone.state = State.Loaded;
            ZoneLoaded?.Invoke(zone.sceneName);
        }

        IEnumerator Unload(Zone zone)
        {
            zone.state = State.Unloading;
            ZoneUnloading?.Invoke(zone.sceneName);
            yield return SceneManager.UnloadSceneAsync(zone.sceneName);
            zone.state = State.Unloaded;
        }

        /// <summary>Current state of a zone, by scene name. Used by tests and debug overlays.</summary>
        public State GetState(string sceneName)
        {
            foreach (var zone in zones)
                if (zone.sceneName == sceneName) return zone.state;
            return State.Unloaded;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            foreach (var zone in zones)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireCube(zone.bounds.center, zone.bounds.size);
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
                Gizmos.DrawWireCube(zone.bounds.center, zone.bounds.size + new Vector3(zone.loadDistance, 0, zone.loadDistance) * 2f);
                Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.35f);
                Gizmos.DrawWireCube(zone.bounds.center, zone.bounds.size + new Vector3(zone.unloadDistance, 0, zone.unloadDistance) * 2f);
            }
        }
#endif
    }
}
