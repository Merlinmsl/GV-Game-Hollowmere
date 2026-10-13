using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Senses
{
    /// <summary>
    /// Makes the player's movement audible: one noise per step, louder when running,
    /// louder still when wading through the flood, quiet when crouching.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerNoiseEmitter : MonoBehaviour
    {
        [Tooltip("Horizontal speed in m/s at or above which the player counts as running.")]
        [SerializeField] float runSpeed = 5f;
        [Tooltip("Seconds between steps when walking.")]
        [SerializeField] float walkStepInterval = 0.5f;
        [Tooltip("Seconds between steps when running.")]
        [SerializeField] float runStepInterval = 0.33f;

        CharacterController controller;
        IPlayerState state;
        int waterMask;
        float nextStepTime;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            state = GetComponent<IPlayerState>();
            waterMask = 1 << NavMesh.GetAreaFromName("Water");
        }

        void Update()
        {
            Vector3 velocity = controller.velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;

            // Standing still or in the air makes no footsteps.
            if (speed < 0.5f || !controller.isGrounded) return;
            if (Time.time < nextStepTime) return;

            bool running = speed >= runSpeed;
            nextStepTime = Time.time + (running ? runStepInterval : walkStepInterval);

            bool crouching = state != null && state.IsCrouching;
            float loudness = running ? Loudness.Run : crouching ? Loudness.CrouchStep : Loudness.Walk;
            NoiseKind kind = NoiseKind.Footstep;

            // Water splashes whatever the posture: crouching does not make wading quiet.
            if (InWater())
            {
                kind = NoiseKind.Wading;
                loudness = Mathf.Max(loudness, Loudness.Wade);
            }

            NoiseSystem.Emit(transform.position, loudness, kind, gameObject);
        }

        // The NavMesh already marks flooded ground as the "Water" area, so ask it.
        bool InWater()
        {
            var humanoid = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            return NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, humanoid)
                   && (hit.mask & waterMask) != 0;
        }
    }
}





