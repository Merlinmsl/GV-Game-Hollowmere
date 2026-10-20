using Hollowmere.Agents;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Makes GhostMovement follow a route of waypoints using pure pursuit: each frame it projects the
    /// ghost onto the current leg and hands GhostMovement a "carrot" point LookAhead metres further along
    /// the route. GhostMovement eases towards a point and slows as it arrives, so chasing a carrot that
    /// keeps moving gives one smooth glide round the corners instead of a stop at every waypoint; only the
    /// last waypoint is actually arrived at. GhostMovement itself is unchanged: it only ever gets MoveTo.
    /// </summary>
    [RequireComponent(typeof(GhostMovement))]
    public sealed class GhostRouteFollower : MonoBehaviour
    {
        public const int MaxWaypoints = 64;

        [Tooltip("How far ahead along the route the ghost aims. Longer is faster and rounder, shorter hugs the route.")]
        [SerializeField] float lookAhead = 2.5f;
        [Tooltip("Close enough to the last waypoint to count as arrived.")]
        [SerializeField] float arriveRadius = 0.35f;
        [Tooltip("Height of this root above a storey's floor. Waypoints are cell centres, BodyHeight above the floor.")]
        [SerializeField] float rootAboveFloor = 0f;

        readonly Vector3[] points = new Vector3[MaxWaypoints];
        GhostMovement movement;
        int count;
        int leg;                         // the ghost is on the leg points[leg] -> points[leg + 1]

        public int Count => count;
        public bool HasRoute => count > 0;
        public bool Arrived { get; private set; }
        public Vector3 Waypoint(int i) => points[i];
        /// <summary>The point GhostMovement was last told to drift to.</summary>
        public Vector3 Carrot { get; private set; }

        void Awake() => movement = GetComponent<GhostMovement>();

        /// <summary>Follow these waypoints (cell centres, as from PathSmoother). The array is copied.</summary>
        public void SetRoute(Vector3[] waypoints, int n)
        {
            count = Mathf.Min(n, MaxWaypoints);
            float drop = GhostGrid.BodyHeight - rootAboveFloor;
            for (int i = 0; i < count; i++) points[i] = waypoints[i] - Vector3.up * drop;
            leg = 0;
            Arrived = count == 0;
        }

        /// <summary>Stop following; the ghost settles where it is.</summary>
        public void Clear()
        {
            count = 0;
            Arrived = true;
            if (movement != null) movement.MoveTo(transform.position);
        }

        void Update()
        {
            if (count == 0 || movement == null) return;
            Vector3 position = transform.position;

            if (count == 1)
            {
                Steer(points[0], position);
                return;
            }

            // Move on to the next leg once the ghost has passed the end of this one.
            while (leg < count - 2 && Along(leg, position) >= 1f) leg++;

            // The carrot: project onto the leg, then walk LookAhead metres on along the route.
            float t = Mathf.Clamp01(Along(leg, position));
            Vector3 carrot = Vector3.Lerp(points[leg], points[leg + 1], t);
            float left = lookAhead;
            int i = leg;
            while (left > 0f && i < count - 1)
            {
                float toEnd = Vector3.Distance(carrot, points[i + 1]);
                if (toEnd >= left)
                {
                    carrot = Vector3.MoveTowards(carrot, points[i + 1], left);
                    break;
                }
                left -= toEnd;
                carrot = points[++i];
            }
            Steer(carrot, position);
        }

        void Steer(Vector3 carrot, Vector3 position)
        {
            Carrot = carrot;
            movement.MoveTo(carrot);
            Arrived = (position - points[count - 1]).sqrMagnitude <= arriveRadius * arriveRadius;
        }

        // How far along leg i the position projects: 0 at its start, 1 at its end.
        float Along(int i, Vector3 position)
        {
            Vector3 a = points[i], segment = points[i + 1] - a;
            float lengthSq = segment.sqrMagnitude;
            return lengthSq < 1e-6f ? 1f : Vector3.Dot(position - a, segment) / lengthSq;
        }

        void OnDrawGizmosSelected()
        {
            if (count == 0) return;
            Gizmos.color = Color.magenta;
            for (int i = 0; i < count - 1; i++) Gizmos.DrawLine(points[i], points[i + 1]);
            Gizmos.DrawWireSphere(Carrot, 0.15f);
        }
    }
}
