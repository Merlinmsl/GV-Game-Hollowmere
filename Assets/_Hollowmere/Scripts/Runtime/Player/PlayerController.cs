using UnityEngine;
using UnityEngine.InputSystem;
using Hollowmere.AI.Senses;

namespace Hollowmere.Player
{
    /// <summary>
    /// First-person physics-driven Player Controller for Hollowmere.
    /// Responsible for CharacterController locomotion, mouse look, crouching, jumping, gravity,
    /// flashlight control, and acoustic sensory emission via PlayerNoiseEmitter.
    /// Implements IPlayerState so AI senses, Director, and PlayerNoiseEmitter can query stance/speed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerNoiseEmitter))]
    public class PlayerController : MonoBehaviour, IPlayerState
    {
        // ─────────────────────────────────────────────────────────────────────────
        // CONFIGURATION & REFERENCES
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Camera & Head")]
        [Tooltip("Transform representing the player's camera / eye position.")]
        [SerializeField] private Transform headTransform;

        [Header("Flashlight")]
        [Tooltip("Spotlight attached to Head representing the player's flashlight.")]
        [SerializeField] private Light flashlight;

        [Header("Look Settings")]
        [Tooltip("Mouse look sensitivity multiplier.")]
        [SerializeField] private float mouseSensitivity = 0.12f;

        [Tooltip("Maximum vertical look angle in degrees (clamps pitch between -angle and +angle).")]
        [SerializeField] private float verticalClampAngle = 80.0f;

        [Header("Movement Settings")]
        [Tooltip("Walking speed in m/s.")]
        [SerializeField] private float walkSpeed = 3.5f;

        [Tooltip("Sprint speed multiplier over walk speed (speed × 1.6).")]
        [SerializeField] private float sprintMultiplier = 1.6f;

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
        // IPlayerState IMPLEMENTATION (Queried by AI senses & PlayerNoiseEmitter)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Current world-space position of the player feet.</summary>
        public Vector3 Position => transform.position;

        /// <summary>Forward aim vector from camera / head.</summary>
        public Vector3 LookDirection => headTransform != null ? headTransform.forward : transform.forward;

        /// <summary>Player normalized health (0.0 = dead, 1.0 = full).</summary>
        public float Health01 { get; set; } = 1.0f;

        /// <summary>Player normalized ammo (0.0 = empty, 1.0 = full).</summary>
        public float Ammo01 { get; set; } = 1.0f;

        /// <summary>Currently equipped weapon.</summary>
        public WeaponKind HeldWeapon { get; set; } = WeaponKind.None;

        /// <summary>True when flashlight is toggled on.</summary>
        public bool FlashlightOn => flashlight != null && flashlight.enabled;

        /// <summary>True when player is in crouching stance.</summary>
        public bool IsCrouching { get; private set; }

        // ─────────────────────────────────────────────────────────────────────────
        // LOCOMOTION STATE
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Horizontal ground speed in m/s.</summary>
        public float CurrentSpeed => characterController != null
            ? new Vector2(characterController.velocity.x, characterController.velocity.z).magnitude
            : 0f;

        /// <summary>True when CharacterController is contacting the ground surface.</summary>
        public bool IsGrounded => characterController != null && characterController.isGrounded;

        /// <summary>True when the player is actively sprinting.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Full 3D velocity vector of the CharacterController.</summary>
        public Vector3 Velocity => characterController != null ? characterController.velocity : Vector3.zero;

        /// <summary>Reference to the attached PlayerNoiseEmitter component.</summary>
        public PlayerNoiseEmitter NoiseEmitter => noiseEmitter;

        private CharacterController characterController;
        private PlayerNoiseEmitter noiseEmitter;
        private float verticalVelocity;
        private float pitch;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            noiseEmitter = GetComponent<PlayerNoiseEmitter>();

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

            if (flashlight == null && headTransform != null)
            {
                flashlight = headTransform.GetComponentInChildren<Light>();
            }
        }

        private void Start()
        {
            PlayerRegistry.Register(this);
        }

        private void OnDestroy()
        {
            PlayerRegistry.Unregister(this);
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            PlayerRegistry.Register(this);
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            PlayerRegistry.Unregister(this);
        }

        private void Update()
        {
            ProcessLook();
            ProcessMovement();
            ProcessFlashlight();
        }

        /// <summary>
        /// Toggles flashlight on/off with the F key.
        /// </summary>
        private void ProcessFlashlight()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame && flashlight != null)
            {
                flashlight.enabled = !flashlight.enabled;
            }
        }

        /// <summary>
        /// Reads mouse delta input, rotates body horizontally (yaw),
        /// and tilts camera vertically (pitch) clamped within ±verticalClampAngle.
        /// </summary>
        private void ProcessLook()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

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

            // Horizontal yaw
            transform.Rotate(0f, lookDelta.x, 0f);

            // Vertical pitch clamped to ±verticalClampAngle
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
                targetSpeed = walkSpeed * sprintMultiplier;
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
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2.0f;
                }

                if (wantsJump && !IsCrouching)
                {
                    verticalVelocity = Mathf.Sqrt(2.0f * jumpHeight * Mathf.Abs(gravity));
                }
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            // 6. Execute combined CharacterController movement (feeds velocity to PlayerNoiseEmitter)
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

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;

            // Don't push objects below player feet
            if (hit.moveDirection.y < -0.3f) return;

            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (pushDir.sqrMagnitude < 0.001f) return;

            Hollowmere.Interactables.Barricade barricade = hit.collider.GetComponentInParent<Hollowmere.Interactables.Barricade>();
            if (barricade != null)
            {
                barricade.Push(pushDir);
            }
            else
            {
                body.AddForce(pushDir.normalized * 25.0f, ForceMode.Force);
            }
        }
    }
}
