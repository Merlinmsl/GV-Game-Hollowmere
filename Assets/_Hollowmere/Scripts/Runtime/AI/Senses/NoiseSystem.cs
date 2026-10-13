using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Senses
{
    /// <summary>
    /// Delivers noises to the listeners that can hear them.
    /// A noise is heard when the WALKING distance from the noise to the listener is within
    /// loudness x hearing acuity. Sound therefore goes round walls through doorways, and a wall
    /// between two rooms muffles it without any audio physics.
    /// </summary>
    public static class NoiseSystem
    {
        // How far from the NavMesh a position may be and still count as standing on it.
        const float SnapToNavMesh = 3f;
        // A position off the NavMesh (a ghost inside a wall) falls back to the straight line, made longer.
        const float OffMeshMuffle = 1.5f;

        static readonly List<INoiseListener> listeners = new List<INoiseListener>();
        static readonly Vector3[] corners = new Vector3[64];
        static NavMeshPath path;

        /// <summary>Raised after every noise, with how many listeners heard it. For the Director and debug views.</summary>
        public static event Action<Noise, int> Emitted;

        public static int ListenerCount => listeners.Count;

        // Static data survives between Play sessions when domain reload is off, so clear it each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            listeners.Clear();
            Emitted = null;
            path = null;
        }

        public static void Register(INoiseListener listener)
        {
            if (!listeners.Contains(listener)) listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener)
        {
            listeners.Remove(listener);
        }

        /// <summary>Makes a noise. Returns how many listeners heard it.</summary>
        public static int Emit(Vector3 position, float loudness, NoiseKind kind, GameObject source)
        {
            var noise = new Noise(position, loudness, kind, source);
            int heardBy = 0;

            // Backwards, so a listener may unregister itself inside OnHeard.
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                INoiseListener listener = listeners[i];
                float reach = loudness * listener.HearingAcuity;
                if (reach <= 0f) continue;

                // Cheap test first: the path is never shorter than the straight line,
                // so if the straight line is already too far there is no need to search.
                Vector3 ear = listener.EarPosition;
                if ((ear - position).sqrMagnitude > reach * reach) continue;

                float distance = PathDistance(position, ear);
                if (distance > reach) continue;

                listener.OnHeard(noise, distance);
                heardBy++;
            }

            Emitted?.Invoke(noise, heardBy);
            return heardBy;
        }

        /// <summary>Walking distance between two points on the NavMesh. Infinity when there is no route.</summary>
        public static float PathDistance(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, SnapToNavMesh, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit end, SnapToNavMesh, NavMesh.AllAreas))
                return Vector3.Distance(from, to) * OffMeshMuffle;

            if (path == null) path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
                return float.PositiveInfinity;

            int count = path.GetCornersNonAlloc(corners);
            float length = 0f;
            for (int i = 1; i < count; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }
    }
}