using UnityEngine;
using Hollowmere.AI.Senses;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.Interactables
{
    /// <summary>
    /// Interactive physical door script.
    /// Detects player proximity via trigger collider or distance check.
    /// When player presses E, smoothly rotates between closed and open states
    /// using Quaternion.RotateTowards or HingeJoint spring targets.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Door : MonoBehaviour
    {
        [Header("Door Rotation Settings")]
        [Tooltip("Target rotation angle in degrees when fully opened.")]
        [SerializeField] private float openAngle = 90.0f;

        [Tooltip("Rotation speed in degrees per second.")]
        [SerializeField] private float openSpeed = 180.0f;

        [Tooltip("Hinge rotation axis (default Y-axis).")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Tooltip("Optional HingeJoint component if physical joint limits are preferred.")]
        [SerializeField] private HingeJoint hinge;

        [Header("Audio Settings")]
        [Tooltip("AudioSource component on the door.")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Sound clip played when opening the door (creak).")]
        [SerializeField] private AudioClip openClip;

        [Tooltip("Sound clip played when closing the door (slam).")]
        [SerializeField] private AudioClip closeClip;

        [Header("Noise Settings")]
        [Tooltip("Loudness of noise emitted when door is operated (default 10 m).")]
        [SerializeField] private float noiseLoudness = Loudness.Door;

        [Header("Interaction Settings")]
        [Tooltip("Maximum interaction distance from player if not using trigger collider.")]
        [SerializeField] private float interactDistance = 2.5f;

        private bool isOpen = false;
        private bool isPlayerNearby = false;
        private Quaternion closedRotation;
        private Quaternion openRotation;

        /// <summary>Whether the door is currently open.</summary>
        public bool IsOpen => isOpen;

        /// <summary>Whether player is currently within interaction range.</summary>
        public bool IsPlayerNearby => isPlayerNearby;

        private void Awake()
        {
            if (hinge == null)
            {
                hinge = GetComponent<HingeJoint>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            closedRotation = transform.localRotation;
            openRotation = closedRotation * Quaternion.AngleAxis(openAngle, rotationAxis);
        }

        private void Update()
        {
            CheckInteractionInput();
            UpdateRotation();
        }

        /// <summary>
        /// Reads E key press when player is nearby and toggles door state.
        /// </summary>
        private void CheckInteractionInput()
        {
            if (!isPlayerNearby && PlayerRegistry.Instance != null)
            {
                if (Vector3.Distance(transform.position, PlayerRegistry.Instance.Position) <= interactDistance)
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
                ToggleDoor();
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

        /// <summary>
        /// Toggles door between open and closed, playing appropriate creak/slam audio.
        /// </summary>
        public void ToggleDoor()
        {
            isOpen = !isOpen;

            if (audioSource != null)
            {
                AudioClip clipToPlay = isOpen ? openClip : closeClip;
                if (clipToPlay != null)
                {
                    audioSource.PlayOneShot(clipToPlay);
                }
            }

            // Emit noise event so nearby zombies hear the door opening/closing (NoiseKind.Interaction / Door)
            NoiseSystem.Emit(transform.position, noiseLoudness, NoiseKind.Interaction, gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player") || other.GetComponent<CharacterController>() != null)
            {
                isPlayerNearby = true;
            }
        }

        private void OnTriggerStay(Collider other)
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
