using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.Agents
{
    /// <summary>
    /// Turns the path the NavMeshAgent follows into believable movement. The agent still moves the
    /// position along agent.path.corners; this script takes over rotation (a constant-rate turn
    /// toward where the agent wants to go) and feeds the real ground speed to the Animator's
    /// "Speed" parameter, which drives the Locomotion blend tree.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentLocomotion : MonoBehaviour
    {
        [Tooltip("Left empty, the Animator is found on this object or its children once in Awake.")]
        [SerializeField] Animator animator;
        [Tooltip("How fast the body turns, in degrees per second.")]
        [SerializeField] float turnSpeed = 240f;
        [Tooltip("Seconds the Speed parameter takes to catch up with the real speed.")]
        [SerializeField] float speedSmoothing = 0.15f;

        static readonly int Speed = Animator.StringToHash("Speed");
        NavMeshAgent agent;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;             // we turn the body ourselves

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[AgentLocomotion] No Animator on {name} or its children.", this);
                enabled = false;
            }
        }

        void Update()
        {
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
    }
}
