using UnityEngine;
using Hollowmere.AI.Senses;

namespace Hollowmere.Player
{
    /// <summary>
    /// First-person physics-driven Player Controller for Hollowmere (SE3032 Graphics &amp; Visualization).
    /// Responsible for CharacterController movement, mouse look, physics interaction, and sensory emission.
    /// Replaces DebugWalker in production scenes.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────────
        // CONFIGURATION & REFERENCES
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Camera & Head")]
        [Tooltip("Transform representing the player's camera / eye position.")]
        [SerializeField] private Transform headTransform;

        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float runSpeed = 7.0f;
        [SerializeField] private float crouchSpeed = 1.8f;
        [SerializeField] private float gravity = -18.0f;

        [Header("Look Settings")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float verticalClampAngle = 80.0f;

        // ─────────────────────────────────────────────────────────────────────────
        // TODO: UPCOMING SPRINT IMPLEMENTATION TASKS
        // ─────────────────────────────────────────────────────────────────────────

        // TODO (Day 2 - 14:00): Implement CharacterController movement with WASD input, sprint, crouch, and ground gravity.
        // TODO (Day 2 - 15:30): Implement mouse-look pitch/yaw rotation with vertical clamping (±80°) and cursor locking.
        // TODO (Day 2 - 16:30): Hook into PlayerNoiseEmitter to emit footsteps (Walk=5m, Run=12m, Crouch=2m) for AI listeners.
        // TODO (Day 2 - 17:30): Implement IPlayerState and register with PlayerRegistry.Register(this) / Unregister(this).
        // TODO (Day 3 - Physics): Implement physics interactions with doors (HingeJoint / physics forces) and barricades.
        // TODO (Day 4 - Throwing): Implement physics object pickup, aim, and impulse throw system.

        private CharacterController characterController;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
        }

        private void Start()
        {
            // TODO: Initialize input actions and register with PlayerRegistry
        }

        private void Update()
        {
            // TODO: Process movement and look input
        }
    }
}
