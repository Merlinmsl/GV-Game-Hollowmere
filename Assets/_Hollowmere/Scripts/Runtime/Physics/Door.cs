using UnityEngine;
using UnityEngine.AI;
using Hollowmere.AI.Senses;
using Hollowmere.Interaction;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.Interactables
{
    /// <summary>
    /// Interactive physical door and hatch script.
    /// Implements IInteractable for direct raycast interaction via PlayerInteractor.
    /// Supports HingeJoint physical swinging, kinematic locking, key validation from PlayerInventory,
    /// and NavMeshObstacle carving updates.
    /// Defined in SystemsEngineer.md (Sections 5 & 6).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Door : MonoBehaviour, IInteractable
    {
        [Header("Door Rotation Settings")]
        [Tooltip("Target rotation angle in degrees when fully opened.")]
        [SerializeField] private float openAngle = 90.0f;

        [Tooltip("Rotation speed in degrees per second.")]
        [SerializeField] private float openSpeed = 180.0f;

        [Tooltip("Hinge rotation axis (Vector3.up for vertical doors, Vector3.right/forward for hatches).")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Tooltip("Optional HingeJoint component if physical joint limits are preferred.")]
        [SerializeField] private HingeJoint hinge;

        [Tooltip("Optional Rigidbody for physics simulation and barge resistance.")]
        [SerializeField] private Rigidbody rb;

        [Tooltip("Optional NavMeshObstacle to carve navigation holes when open.")]
        [SerializeField] private NavMeshObstacle navObstacle;

        [Header("Locking Settings")]
        [Tooltip("Whether the door is locked and requires a key to open.")]
        [SerializeField] private bool isLocked = false;

        [Tooltip("Key identifier required to unlock this door (e.g. 'Key_MarrowHouse', 'Key_Tunnel').")]
        [SerializeField] private string requiredKey = "";

        [Header("Audio Settings")]
        [Tooltip("AudioSource component on the door.")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Sound clip played when opening the door (creak).")]
        [SerializeField] private AudioClip openClip;

        [Tooltip("Sound clip played when closing the door (slam).")]
        [SerializeField] private AudioClip closeClip;

        [Tooltip("Sound clip played when unlocking the door.")]
        [SerializeField] private AudioClip unlockClip;

        [Tooltip("Sound clip played when rattling a locked door handle.")]
        [SerializeField] private AudioClip lockedRattleClip;

        [Header("Noise Settings")]
        [Tooltip("Loudness of noise emitted when door is operated (default 10 m).")]
        [SerializeField] private float noiseLoudness = Loudness.Door;

        [Header("Proximity Fallback Settings")]
        [Tooltip("Maximum interaction distance from player if not using raycast interaction.")]
        [SerializeField] private float interactDistance = 2.5f;

        private bool isOpen = false;
        private bool isPlayerNearby = false;
        private Quaternion closedRotation;
        private Quaternion openRotation;

        /// <summary>Whether the door is currently open.</summary>
        public bool IsOpen => isOpen;

        /// <summary>Whether the door is locked.</summary>
        public bool IsLocked => isLocked;

        /// <summary>Required key name to unlock.</summary>
        public string RequiredKey => requiredKey;

        /// <summary>Whether player is currently within proximity interaction range.</summary>
        public bool IsPlayerNearby => isPlayerNearby;

        public string Prompt
        {
            get
            {
                if (isLocked)
                {
                    return string.IsNullOrEmpty(requiredKey)
                        ? "Locked"
                        : $"Locked ({requiredKey} required)";
                }
                return isOpen ? "Close Door" : "Open Door";
            }
        }

        private void Awake()
        {
            if (hinge == null) hinge = GetComponent<HingeJoint>();
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (navObstacle == null) navObstacle = GetComponent<NavMeshObstacle>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            closedRotation = transform.localRotation;
            openRotation = closedRotation * Quaternion.AngleAxis(openAngle, rotationAxis);

            ApplyLockPhysics();
            UpdateNavObstacle();
        }

        private void Update()
        {
            CheckProximityInput();
            UpdateRotation();
        }

        private void ApplyLockPhysics()
        {
            if (rb != null)
            {
                // While locked, door is kinematic so player and physics cannot barge it open
                rb.isKinematic = isLocked;
            }
        }

        private void UpdateNavObstacle()
        {
            if (navObstacle != null)
            {
                // When door is open, carving is disabled so agents can walk through
                // When closed, carving is enabled to block agents
                navObstacle.carving = !isOpen;
            }
        }

        /// <summary>
        /// Smoothly rotates the door towards target rotation angle.
        /// </summary>
        private void UpdateRotation()
        {
            Quaternion targetRot = isOpen ? openRotation : closedRotation;

            if (hinge != null && hinge.useSpring)
            {
                JointSpring spring = hinge.spring;
                spring.targetPosition = isOpen ? openAngle : 0f;
                hinge.spring = spring;
            }
            else
            {
                if (Quaternion.Angle(transform.localRotation, targetRot) > 0.1f)
                {
                    transform.localRotation = Quaternion.RotateTowards(
                        transform.localRotation,
                        targetRot,
                        openSpeed * Time.deltaTime
                    );
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // IInteractable Implementation
        // ─────────────────────────────────────────────────────────────────────────

        public bool CanInteract(PlayerInventory inventory)
        {
            if (!isLocked) return true;
            return inventory != null && inventory.HasKey(requiredKey);
        }

        public void Interact(PlayerInventory inventory)
        {
            if (isLocked)
            {
                if (inventory != null && inventory.HasKey(requiredKey))
                {
                    Unlock();
                    ToggleDoor();
                }
                else
                {
                    if (audioSource != null && lockedRattleClip != null)
                    {
                        audioSource.PlayOneShot(lockedRattleClip);
                    }
                    Debug.Log($"<color=yellow><b>[Door]</b> Door is locked! Requires key: '{requiredKey}'.</color>");
                }
                return;
            }

            ToggleDoor();
        }

        /// <summary>
        /// Unlocks the door permanently and restores physical movement.
        /// </summary>
        public void Unlock()
        {
            isLocked = false;
            ApplyLockPhysics();

            if (audioSource != null && unlockClip != null)
            {
                audioSource.PlayOneShot(unlockClip);
            }

            Debug.Log($"<color=green><b>[Door]</b> Door unlocked with key: '{requiredKey}'!</color>");
        }

        /// <summary>
        /// Toggles door between open and closed, playing audio and emitting noise.
        /// </summary>
        public void ToggleDoor()
        {
            isOpen = !isOpen;
            UpdateNavObstacle();

            if (audioSource != null)
            {
                AudioClip clipToPlay = isOpen ? openClip : closeClip;
                if (clipToPlay != null)
                {
                    audioSource.PlayOneShot(clipToPlay);
                }
            }

            // Emit noise event so nearby AI entities hear the door operating
            NoiseSystem.Emit(transform.position, noiseLoudness, NoiseKind.Interaction, gameObject);
            Debug.Log($"<color=white><b>[Door]</b> Door {(isOpen ? "Opened" : "Closed")}.</color>");
        }

        /// <summary>
        /// Optional proximity input check if player is directly in front of the door without raycasting.
        /// </summary>
        private void CheckProximityInput()
        {
            if (!isPlayerNearby && PlayerRegistry.Current != null)
            {
                if (Vector3.Distance(transform.position, PlayerRegistry.Current.Position) <= interactDistance)
                {
                    isPlayerNearby = true;
                }
            }

            if (!isPlayerNearby) return;

            bool ePressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                ePressed = true;
            }
#endif
            if (Input.GetKeyDown(KeyCode.E))
            {
                ePressed = true;
            }

            if (ePressed)
            {
                var playerObj = GameObject.FindWithTag("Player");
                PlayerInventory inv = playerObj != null ? playerObj.GetComponent<PlayerInventory>() : null;
                Interact(inv);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") || other.GetComponent<CharacterController>() != null)
            {
                isPlayerNearby = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player") || other.GetComponent<CharacterController>() != null)
            {
                isPlayerNearby = false;
            }
        }
    }
}
