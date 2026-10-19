using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.Agents
{
    /// <summary>
    /// Turns the path the NavMeshAgent follows into believable movement. The agent still moves the
    /// position along agent.path.corners; this script takes over rotation (a constant-rate turn
    /// toward where the agent wants to go) and feeds the real ground speed to the Animator's
    /// "Speed" parameter, which drives the Locomotion blend tree. It also slows the agent when the
    /// next path corner is far off to the side or behind, so the body turns before it speeds up.
    /// Runs after the brain so the speed it writes is the one the agent uses this frame.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [DefaultExecutionOrder(100)]
    public sealed class AgentLocomotion : MonoBehaviour
    {
        [Tooltip("Left empty, the Animator is found on this object or its children once in Awake.")]
        [SerializeField] Animator animator;
        [Tooltip("How fast the body turns, in degrees per second.")]
        [SerializeField] float turnSpeed = 240f;
        [Tooltip("Seconds the Speed parameter takes to catch up with the real speed.")]
        [SerializeField] float speedSmoothing = 0.15f;
        [Tooltip("Below this angle to the next corner, keep full speed.")]
        [SerializeField] float slowStartAngle = 30f;
        [Tooltip("At this angle or more, slow to the minimum.")]
        [SerializeField] float slowFullAngle = 120f;
        [Tooltip("Lowest share of the requested speed. Keep it above 0, or the agent stops and never turns.")]
        [SerializeField, Range(0.1f, 1f)] float minSpeedFactor = 0.25f;

        [Header("Foot matching (optional)")]
        [Tooltip("Ground speed the walk clip was animated at, in m/s. 0 leaves the clip speed alone.")]
        [SerializeField] float walkStrideSpeed = 0f;
        [Tooltip("Ground speed the run clip was animated at, in m/s. 0 leaves the clip speed alone.")]
        [SerializeField] float runStrideSpeed = 0f;
        [Tooltip("Speed of the walk slot in the Locomotion blend tree.")]
        [SerializeField] float walkThreshold = 1.2f;
        [Tooltip("Speed of the run slot in the Locomotion blend tree.")]
        [SerializeField] float runThreshold = 4.5f;

        [Header("Water")]
        [Tooltip("Share of the requested speed kept while wading through the NavMesh Water area.")]
        [SerializeField, Range(0.2f, 1f)] float waterSpeedFactor = 0.75f;
        [Tooltip("Seconds to ease into or out of the water speed.")]
        [SerializeField] float waterEaseTime = 0.5f;

        const float AreaCheckInterval = 0.2f;         // the ground under the feet changes slowly

        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int LocomotionSpeed = Animator.StringToHash("LocomotionSpeed");
        NavMeshAgent agent;
        float requestedSpeed;                         // the speed the brain asked for
        float writtenSpeed;                           // the speed we last gave the agent
        int waterMask;                                // 0 when the project has no Water area
        bool inWater;
        float waterFactor = 1f;
        float nextAreaCheck;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;             // we turn the body ourselves
            requestedSpeed = writtenSpeed = agent.speed;

            int waterArea = NavMesh.GetAreaFromName("Water");
            waterMask = waterArea >= 0 ? 1 << waterArea : 0;
            nextAreaCheck = Random.value * AreaCheckInterval;   // spread agents' checks across frames

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[AgentLocomotion] No Animator on {name} or its children.", this);
                enabled = false;
            }
        }

        void Update()
        {
            UpdateWaterFactor();
            ApplyCornerSlowdown();

            // Where the agent wants to go this frame, flattened so the body only turns, never tilts.
            Vector3 direction = agent.desiredVelocity;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.01f)       // faster than 0.1 m/s: avoids jitter when stopped
            {
                Quaternion target = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            // Feed the real ground speed to the blend tree, smoothed so it does not flicker.
            animator.SetFloat(Speed, agent.velocity.magnitude, speedSmoothing, Time.deltaTime);
            MatchFeet(animator.GetFloat(Speed));
        }

        // Plays the Locomotion state faster or slower so the clip's stride covers the ground the body
        // actually covers. Only that state uses LocomotionSpeed, so attacks and deaths keep their speed.
        void MatchFeet(float speed)
        {
            if (walkStrideSpeed <= 0f || runStrideSpeed <= 0f) return;

            float multiplier;
            if (speed <= walkThreshold)
            {
                // Idle keeps its own pace; the walk is slowed or sped up more as it blends in.
                multiplier = Mathf.Lerp(1f, walkThreshold / walkStrideSpeed, speed / walkThreshold);
            }
            else
            {
                float t = Mathf.InverseLerp(walkThreshold, runThreshold, speed);
                multiplier = speed / Mathf.Lerp(walkStrideSpeed, runStrideSpeed, t);
            }
            animator.SetFloat(LocomotionSpeed, Mathf.Clamp(multiplier, 0.25f, 3f));
        }

        void ApplyCornerSlowdown()
        {
            // If agent.speed is not what we wrote, the brain changed it: that is the new request.
            if (!Mathf.Approximately(agent.speed, writtenSpeed)) requestedSpeed = agent.speed;

            float factor = 1f;
            Vector3 toCorner = agent.steeringTarget - transform.position;
            toCorner.y = 0f;
            if (agent.hasPath && toCorner.sqrMagnitude > 0.01f)
            {
                // 0 when facing the next corner, 180 when it is straight behind.
                float angle = Vector3.Angle(transform.forward, toCorner);
                float t = Mathf.InverseLerp(slowStartAngle, slowFullAngle, angle);
                factor = Mathf.Lerp(1f, minSpeedFactor, t);
            }

            writtenSpeed = requestedSpeed * factor * waterFactor;
            agent.speed = writtenSpeed;
        }

        // Wading is slower: checks a few times a second which NavMesh area is under the agent and
        // eases the speed factor toward the water value, so stepping in or out never snaps.
        void UpdateWaterFactor()
        {
            if (waterMask == 0) return;

            if (Time.time >= nextAreaCheck)
            {
                nextAreaCheck = Time.time + AreaCheckInterval;
                inWater = NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 0.5f, agent.areaMask)
                          && (hit.mask & waterMask) != 0;
            }

            float target = inWater ? waterSpeedFactor : 1f;
            float rate = (1f - waterSpeedFactor) / Mathf.Max(waterEaseTime, 0.01f);
            waterFactor = Mathf.MoveTowards(waterFactor, target, rate * Time.deltaTime);
        }

        // Draws the path array (agent.path.corners) in the Scene view while this object is selected.
        void OnDrawGizmosSelected()
        {
            var navAgent = agent != null ? agent : GetComponent<NavMeshAgent>();
            if (navAgent == null || !navAgent.isActiveAndEnabled || !navAgent.hasPath) return;

            Vector3[] corners = navAgent.path.corners;
            Gizmos.color = Color.yellow;
            for (int i = 0; i < corners.Length - 1; i++)
                Gizmos.DrawLine(corners[i], corners[i + 1]);

            Gizmos.color = Color.cyan;
            foreach (Vector3 corner in corners)
                Gizmos.DrawSphere(corner, 0.1f);

            // The corner the agent is heading for right now, which the corner slow-down measures against.
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(navAgent.steeringTarget, 0.25f);
        }
    }
}
