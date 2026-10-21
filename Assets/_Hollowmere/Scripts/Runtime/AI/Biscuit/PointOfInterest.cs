using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Marks ammunition or an evidence page that Biscuit can find by smell. The level does not need
    /// it: PointRegistry already reads the zones' own markers (Evidence_*, Clue_*, Pickup_Magazine_*).
    /// This is for anything else, such as the test props.
    /// </summary>
    public sealed class PointOfInterest : MonoBehaviour
    {
        [SerializeField] PointKind kind = PointKind.Evidence;
        [Tooltip("What the find is called on screen. Empty means the object's name.")]
        [SerializeField] string displayName = "";

        int id;

        void OnEnable()
        {
            id = GetInstanceID();
            PointRegistry.Add(id, kind, transform.position, displayName.Length > 0 ? displayName : name,
                              gameObject.scene.handle);
        }

        void OnDisable() => PointRegistry.Remove(id);
    }

    /// <summary>
    /// Every unused ammunition spot and unseen page in the loaded zones. When a zone loads, its
    /// _Gameplay markers are read once, by name; when it unloads, its points go. Biscuit asks this
    /// list and never searches the scene.
    /// </summary>
    public static class PointRegistry
    {
        public struct Point
        {
            public int Id;
            public PointKind Kind;
            public Vector3 Position;
            public string Name;
            public int Scene;
        }

        static readonly List<Point> points = new List<Point>();

        // Taken or shown, for good: a marker read again when its zone reloads stays used.
        static readonly HashSet<int> used = new HashSet<int>();

        /// <summary>Raised with a point's id when it is taken, shown or unloaded.</summary>
        public static event Action<int> Removed;

        public static IReadOnlyList<Point> All => points;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            points.Clear();
            used.Clear();
            Removed = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // The scenes already open when Play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ReadOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) ReadMarkers(SceneManager.GetSceneAt(i));
        }

        public static int Count(PointKind kind)
        {
            int count = 0;
            for (int i = 0; i < points.Count; i++)
                if (points[i].Kind == kind) count++;
            return count;
        }

        public static void Add(int id, PointKind kind, Vector3 position, string name, int scene)
        {
            if (used.Contains(id)) return;
            for (int i = 0; i < points.Count; i++)
                if (points[i].Id == id) return;
            points.Add(new Point { Id = id, Kind = kind, Position = position, Name = name, Scene = scene });
        }

        public static void Remove(int id)
        {
            for (int i = points.Count - 1; i >= 0; i--)
            {
                if (points[i].Id != id) continue;
                points.RemoveAt(i);
                Removed?.Invoke(id);
            }
        }

        /// <summary>Takes a point for good (picked up, or shown to the player). False if it is already gone.</summary>
        public static bool TryConsume(int id)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Id != id) continue;
                used.Add(id);
                points.RemoveAt(i);
                Removed?.Invoke(id);
                return true;
            }
            return false;
        }

        public static string NameOf(int id)
        {
            for (int i = 0; i < points.Count; i++)
                if (points[i].Id == id) return points[i].Name;
            return "";
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ReadMarkers(scene);

        static void OnSceneUnloaded(Scene scene)
        {
            for (int i = points.Count - 1; i >= 0; i--)
            {
                if (points[i].Scene != scene.handle) continue;
                int id = points[i].Id;
                points.RemoveAt(i);
                Removed?.Invoke(id);
            }
        }

        // Once per zone load: the World Builder's markers under _Gameplay, recognised by name.
        static void ReadMarkers(Scene scene)
        {
            if (!scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "_Gameplay") continue;
                foreach (Transform marker in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!TryKindFromName(marker.name, out PointKind kind)) continue;

                    // The same marker gets the same id every time its zone loads.
                    int id = Animator.StringToHash(scene.name + "/" + marker.name);
                    Add(id, kind, marker.position, Readable(marker.name), scene.handle);
                }
            }
        }

        static bool TryKindFromName(string markerName, out PointKind kind)
        {
            kind = PointKind.Evidence;
            if (markerName.StartsWith("Evidence_", StringComparison.Ordinal) ||
                markerName.StartsWith("Clue_", StringComparison.Ordinal))
                return true;

            kind = PointKind.Ammo;
            return markerName.StartsWith("Pickup_Magazine", StringComparison.Ordinal) ||
                   markerName.StartsWith("Pickup_RevolverAmmo", StringComparison.Ordinal);
        }

        // "Evidence_MaraMedicalFile" becomes "Mara medical file".
        static string Readable(string markerName)
        {
            int start = markerName.IndexOf('_') + 1;
            var text = new StringBuilder(markerName.Length + 8);
            for (int i = start; i < markerName.Length; i++)
            {
                char c = markerName[i];
                if (c == '_') { text.Append(' '); continue; }
                if (char.IsUpper(c) && text.Length > 0 && text[text.Length - 1] != ' ') text.Append(' ');
                text.Append(text.Length == 0 ? c : char.ToLowerInvariant(c));
            }
            return text.ToString();
        }
    }
}
