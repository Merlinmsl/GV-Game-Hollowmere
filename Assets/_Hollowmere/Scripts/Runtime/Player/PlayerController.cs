using UnityEngine;
using Hollowmere.AI.Senses;

namespace Hollowmere.Player
{
    /// <summary>
    /// First-person physics-driven Player Controller for Hollowmere.
    /// Responsible for CharacterController movement, mouse look, physics interaction, and sensory emission.
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
