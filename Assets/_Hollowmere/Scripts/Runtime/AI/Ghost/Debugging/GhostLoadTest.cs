using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// DEBUG: a repeatable load for profiling. Walks the current player round a loop of points at walking
    /// pace, makes footstep noises, and every few seconds turns to face the nearest ghost (aiming whatever
    /// the player holds at it) before turning away again. Same scenario every run, so GhostProfilerHud
    /// numbers can be compared before and after a change.
    /// </summary>
    public sealed class GhostLoadTest : MonoBehaviour
    {
        [Tooltip("World points the player walks between, in a loop.")]
        public Vector3[] route = new Vector3[0];
        public float walkSpeed = 1.6f;
        [Tooltip("Seconds between footstep noises.")]
        public float stepInterval = 0.5f;
        public float stepLoudness = 14f;
        [Tooltip("Every this many seconds the player turns to face the nearest ghost...")]
        public float faceEvery = 4f;
        [Tooltip("...for this long.")]
        public float faceFor = 1.5f;

        int target;
        float nextStep;

        void Update()
        {
            if (route.Length == 0 || !(PlayerRegistry.Current is Component player)) return;
            Transform t = player.transform;

            Vector3 goal = route[target];
            Vector3 flat = goal - t.position;
            flat.y = 0f;
            if (flat.magnitude < 0.2f) target = (target + 1) % route.Length;
            Vector3 step = Vector3.ClampMagnitude(flat, walkSpeed * Time.deltaTime);
            t.position = new Vector3(t.position.x + step.x, goal.y, t.position.z + step.z);

            bool facing = Time.time % faceEvery < faceFor;
            Vector3 look = facing ? NearestGhost(t.position) - t.position : flat;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(look);

            if (Time.time >= nextStep)
            {
                nextStep = Time.time + stepInterval;
                NoiseSystem.Emit(t.position, stepLoudness, NoiseKind.Footstep, player.gameObject);
            }
        }

        static Vector3 NearestGhost(Vector3 from)
        {
            Vector3 best = from + Vector3.forward;
            float bestSq = float.PositiveInfinity;
            var ghosts = GhostBody.Active;
            for (int i = 0; i < ghosts.Count; i++)
            {
                float sq = (ghosts[i].transform.position - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = ghosts[i].transform.position;
                }
            }
            return best;
        }
    }
}
