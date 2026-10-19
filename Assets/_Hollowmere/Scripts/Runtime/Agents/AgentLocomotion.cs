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

        static readonly int Speed = Animator.StringToHash("Speed");
        NavMeshAgent agent;
        float requestedSpeed;                         // the speed the brain asked for
        float writtenSpeed;                           // the speed we last gave the agent

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;             // we turn the body ourselves
            requestedSpeed = writtenSpeed = agent.speed;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[AgentLocomotion] No Animator on {name} or its children.", this);
                enabled = false;
            }
        }

        void Update()
        {
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

            writtenSpeed = requestedSpeed * factor;
            agent.speed = writtenSpeed;
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
