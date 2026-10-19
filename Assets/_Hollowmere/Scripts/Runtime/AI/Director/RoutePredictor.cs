using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Director
{
    /// <summary>
    /// Predicts the way the player will go: the shortest walking route (A* on the NavMesh) from
    /// the player to the zone's exit. The Director uses it to wake the dead AHEAD of the player
    /// and close to their path, never behind them where it would be pointless.
    ///   Project(p) gives how far along the route the point nearest to p is, and how far p is from the route.
    ///   PointAt(d) gives the point d metres along the route.
    /// </summary>
    public sealed class RoutePredictor
    {
        const int MaxCorners = 128;

        readonly Vector3[] corners = new Vector3[MaxCorners];
        readonly float[] distanceAt = new float[MaxCorners];   // route length up to each corner
        NavMeshPath path;                                        // made on first use: Unity forbids it in constructors
        int count;

        public bool HasRoute => count >= 2;
        public float Length => count > 0 ? distanceAt[count - 1] : 0f;
        public int CornerCount => count;
        public Vector3 Corner(int index) => corners[index];

        /// <summary>Plans the route. A route that stops short of the goal is kept: it still shows the direction.</summary>
        public bool Plan(Vector3 from, Vector3 goal)
        {
            count = 0;
            var humanoid = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 3f, humanoid)) return false;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit end, 6f, humanoid)) return false;

            if (path == null) path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, humanoid, path)) return false;
            if (path.status == NavMeshPathStatus.PathInvalid) return false;

            count = path.GetCornersNonAlloc(corners);
            distanceAt[0] = 0f;
            for (int i = 1; i < count; i++)
                distanceAt[i] = distanceAt[i - 1] + Vector3.Distance(corners[i - 1], corners[i]);
            return HasRoute;
        }

        /// <summary>How far along the route the closest route point to p lies; lateral is p's distance from the route.</summary>
        public float Project(Vector3 p, out float lateral)
        {
            lateral = float.PositiveInfinity;
            float bestAlong = 0f;
            for (int i = 1; i < count; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i];
                Vector3 segment = b - a;
                float length = segment.magnitude;
                float t = length > 0.001f ? Mathf.Clamp01(Vector3.Dot(p - a, segment) / (length * length)) : 0f;
                float distance = Vector3.Distance(p, a + segment * t);
                if (distance < lateral)
                {
                    lateral = distance;
                    bestAlong = distanceAt[i - 1] + t * length;
                }
            }
            return bestAlong;
        }

        public Vector3 PointAt(float along)
        {
            if (count == 0) return Vector3.zero;
            if (along <= 0f) return corners[0];
            for (int i = 1; i < count; i++)
            {
                if (along > distanceAt[i]) continue;
                float length = distanceAt[i] - distanceAt[i - 1];
                float t = length > 0.001f ? (along - distanceAt[i - 1]) / length : 0f;
                return Vector3.Lerp(corners[i - 1], corners[i], t);
            }
            return corners[count - 1];
        }
    }
}
