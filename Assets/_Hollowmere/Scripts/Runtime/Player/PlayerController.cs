using UnityEngine;
using UnityEngine.InputSystem;
using Hollowmere.AI.Senses;

namespace Hollowmere.Player
{
    /// <summary>
    /// First-person physics-driven Player Controller for Hollowmere.
    /// Responsible for CharacterController WASD movement, mouse-look camera rotation,
    /// sprinting, crouching, jumping, and gravity.
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

        [Header("Look Settings")]
        [Tooltip("Mouse look sensitivity multiplier.")]
        [SerializeField] private float mouseSensitivity = 0.12f;

        [Tooltip("Maximum vertical look angle in degrees (clamps pitch between -angle and +angle).")]
        [SerializeField] private float verticalClampAngle = 80.0f;

        [Header("Movement Settings")]
        [Tooltip("Walking speed in m/s.")]
        [SerializeField] private float walkSpeed = 3.5f;

        [Tooltip("Running speed in m/s (holding Left Shift).")]
        [SerializeField] private float runSpeed = 7.0f;

        [Tooltip("Crouching speed in m/s (holding Left Ctrl / C).")]
        [SerializeField] private float crouchSpeed = 1.8f;

        [Tooltip("Jump peak height in metres.")]
        [SerializeField] private float jumpHeight = 1.2f;

        [Tooltip("Downward gravitational acceleration (m/s²).")]
        [SerializeField] private float gravity = -18.0f;

        [Header("Crouch Geometry")]
        [SerializeField] private float standingHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.1f;
        [SerializeField] private float standingHeadY = 1.6f;
        [SerializeField] private float crouchHeadY = 0.9f;

        // ─────────────────────────────────────────────────────────────────────────
        // STATE PROPERTIES (Exposed for AI senses, footstep emissions & UI)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Forward aim vector from camera / head, or player forward if head is missing.</summary>
        public Vector3 LookDirection => headTransform != null ? headTransform.forward : transform.forward;

        /// <summary>Horizontal ground speed in m/s.</summary>
        public float CurrentSpeed => characterController != null
            ? new Vector2(characterController.velocity.x, characterController.velocity.z).magnitude
            : 0f;

        /// <summary>True when CharacterController is contacting the ground surface.</summary>
        public bool IsGrounded => characterController != null && characterController.isGrounded;

        /// <summary>True when the player is actively sprinting.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True when player is in crouching stance.</summary>
        public bool IsCrouching { get; private set; }

        /// <summary>Full 3D velocity vector of the CharacterController.</summary>
        public Vector3 Velocity => characterController != null ? characterController.velocity : Vector3.zero;

        private CharacterController characterController;
        private float verticalVelocity;
        private float pitch;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            characterController.height = standingHeight;
            characterController.radius = 0.35f;
            characterController.stepOffset = 0.55f;
            characterController.center = new Vector3(0f, standingHeight * 0.5f, 0f);

            if (headTransform == null)
            {
                Transform head = transform.Find("Head");
                if (head != null)
                {
                    headTransform = head;
                }
                else if (Camera.main != null && Camera.main.transform.IsChildOf(transform))
                {
                    headTransform = Camera.main.transform;
                }
            }
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
        }

        private void Update()
        {
            ProcessLook();
            ProcessMovement();
        }

        /// <summary>
        /// Reads mouse delta input, rotates body horizontally (yaw),
        /// and tilts camera vertically (pitch) clamped within ±verticalClampAngle.
        /// </summary>
        private void ProcessLook()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // Cursor lock toggle convenience
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (Cursor.lockState != CursorLockMode.Locked || mouse == null)
            {
                return;
            }

            Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;

            // Horizontal yaw: rotate player body left/right around Y axis
            transform.Rotate(0f, lookDelta.x, 0f);

            // Vertical pitch: tilt head/camera up/down clamped between [-verticalClampAngle, +verticalClampAngle]
            pitch = Mathf.Clamp(pitch - lookDelta.y, -verticalClampAngle, verticalClampAngle);

            if (headTransform != null)
            {
                headTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }
        }

        /// <summary>
        /// Reads WASD input from Unity's InputSystem, evaluates stance speeds,
        /// applies gravity/jumping, and moves via CharacterController.
        /// </summary>
        private void ProcessMovement()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 1. Read input axes
            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float right = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);

            bool wantsCrouch = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
            bool wantsSprint = keyboard.leftShiftKey.isPressed && forward > 0.1f && !wantsCrouch;
            bool wantsJump = keyboard.spaceKey.wasPressedThisFrame;

            // 2. Handle crouching stance transitions with ceiling obstacle check
            HandleCrouch(wantsCrouch);

            // 3. Determine target horizontal speed
            float targetSpeed = walkSpeed;
            if (IsCrouching)
            {
                targetSpeed = crouchSpeed;
                IsRunning = false;
            }
            else if (wantsSprint)
            {
                targetSpeed = runSpeed;
                IsRunning = true;
            }
            else
            {
                targetSpeed = walkSpeed;
                IsRunning = false;
            }

            // 4. Calculate world-space horizontal movement relative to player heading
            Vector3 moveDir = (transform.forward * forward + transform.right * right);
            moveDir = Vector3.ClampMagnitude(moveDir, 1f);

            // 5. Vertical velocity & gravity
            if (characterController.isGrounded)
            {
                // Keep slight downward bias so the controller remains glued to slopes and stairs
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2.0f;
                }

                // Jump execution (v = sqrt(2 * h * g))
                if (wantsJump && !IsCrouching)
                {
                    verticalVelocity = Mathf.Sqrt(2.0f * jumpHeight * Mathf.Abs(gravity));
                }
            }
            else
            {
                // Apply constant gravity acceleration when airborne
                verticalVelocity += gravity * Time.deltaTime;
            }

            // 6. Execute combined CharacterController movement
            Vector3 finalVelocity = moveDir * targetSpeed;
            finalVelocity.y = verticalVelocity;

            characterController.Move(finalVelocity * Time.deltaTime);
        }

        /// <summary>
        /// Adjusts controller height and camera position for crouch stance.
        /// Performs an upward sphere-cast before standing to prevent clipping into ceilings.
        /// </summary>
        private void HandleCrouch(bool wantsCrouch)
        {
            if (wantsCrouch)
            {
                IsCrouching = true;
                characterController.height = crouchHeight;
                characterController.center = new Vector3(0f, crouchHeight * 0.5f, 0f);

                if (headTransform != null)
                {
                    Vector3 localPos = headTransform.localPosition;
                    localPos.y = crouchHeadY;
                    headTransform.localPosition = localPos;
                }
            }
            else if (IsCrouching)
            {
                // Verify overhead clearance before standing
                float castDistance = standingHeight - crouchHeight;
                Vector3 castOrigin = transform.position + Vector3.up * crouchHeight;
                bool ceilingBlocked = Physics.SphereCast(castOrigin, characterController.radius * 0.9f, Vector3.up, out _, castDistance);

                if (!ceilingBlocked)
                {
                    IsCrouching = false;
                    characterController.height = standingHeight;
                    characterController.center = new Vector3(0f, standingHeight * 0.5f, 0f);

                    if (headTransform != null)
                    {
                        Vector3 localPos = headTransform.localPosition;
                        localPos.y = standingHeadY;
                        headTransform.localPosition = localPos;
                    }
                }
            }
        }
    }
}
