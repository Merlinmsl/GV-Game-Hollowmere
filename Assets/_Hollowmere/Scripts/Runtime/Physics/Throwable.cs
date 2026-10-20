using UnityEngine;
using Hollowmere.AI.Senses;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.Interactables
{
    public enum ThrowableType
    {
        Bottle,
        Can,
        Rock
    }

    /// <summary>
    /// Interactive throwable object script.
    /// Allows the player to pick up objects (F key), hold in front of camera,
    /// and release/throw with physics impulse force in the camera's forward direction.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class Throwable : MonoBehaviour
    {
        [Header("Throwable Properties")]
        [Tooltip("Type of throwable object.")]
        [SerializeField] private ThrowableType itemType = ThrowableType.Bottle;

        [Tooltip("Forward impulse force applied when thrown.")]
        [SerializeField] private float throwForce = 14.0f;

        [Tooltip("Maximum distance from player to allow pick up.")]
        [SerializeField] private float pickUpDistance = 2.5f;

        [Tooltip("Offset position relative to camera when held.")]
        [SerializeField] private Vector3 holdOffset = new Vector3(0.3f, -0.25f, 0.7f);

        [Header("Landing Impact Noise Settings")]
        [Tooltip("Minimum collision velocity required to emit impact noise.")]
        [SerializeField] private float impactVelocityThreshold = 1.5f;

        [Tooltip("Loudness when bottle impacts/breaks (metres). Bottles are louder than cans.")]
        [SerializeField] private float bottleLoudness = 18.0f;

        [Tooltip("Loudness when can impacts/clatters (metres).")]
        [SerializeField] private float canLoudness = 10.0f;

        [Tooltip("Loudness when rock impacts (metres).")]
        [SerializeField] private float rockLoudness = 12.0f;

        [Tooltip("Optional impact audio clip.")]
        [SerializeField] private AudioClip impactAudioClip;

        [Tooltip("Optional AudioSource component.")]
        [SerializeField] private AudioSource audioSource;

        private Rigidbody rb;
        private Collider col;
        private bool isHeld = false;
        private Transform holderCamera;

        /// <summary>Whether this object is currently held by the player.</summary>
        public bool IsHeld => isHeld;

        /// <summary>Object type (Bottle, Can, Rock).</summary>
        public ThrowableType ItemType => itemType;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            // Fix: increase solver iterations to prevent throwable falling through floor on fast throw
            if (rb != null)
            {
                rb.solverIterations = 8;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        private void Update()
        {
            CheckPickUpOrThrowInput();
        }

        private void LateUpdate()
        {
            if (isHeld && holderCamera != null)
            {
                transform.position = holderCamera.TransformPoint(holdOffset);
                transform.rotation = holderCamera.rotation;
            }
        }

        private void CheckPickUpOrThrowInput()
        {
            bool fPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                fPressed = true;
            }
#endif
            if (Input.GetKeyDown(KeyCode.F))
            {
                fPressed = true;
            }

            if (fPressed)
            {
                if (isHeld)
                {
                    Throw();
                }
                else
                {
                    TryPickUp();
                }
            }
        }

        /// <summary>
        /// Attempts to pick up the throwable if within range and in front of player.
        /// </summary>
        public bool TryPickUp()
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            float dist = Vector3.Distance(cam.transform.position, transform.position);
            if (dist <= pickUpDistance)
            {
                PickUp(cam.transform);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attaches object to player view and sets kinematic.
        /// </summary>
        public void PickUp(Transform cameraTransform)
        {
            holderCamera = cameraTransform;
            isHeld = true;

            rb.isKinematic = true;
            if (col != null)
            {
                col.enabled = false;
            }
        }

        /// <summary>
        /// Releases object, restores physics simulation, and applies forward throw force.
        /// </summary>
        public void Throw()
        {
            if (!isHeld) return;

            Vector3 throwDirection = holderCamera != null ? holderCamera.forward : transform.forward;

            isHeld = false;
            holderCamera = null;

            if (col != null)
            {
                col.enabled = true;
            }

            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Random.insideUnitSphere * 5f;
            rb.AddForce(throwDirection * throwForce, ForceMode.Impulse);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isHeld) return;

            float impactVelocity = collision.relativeVelocity.magnitude;
            if (impactVelocity >= impactVelocityThreshold)
            {
                float loudness = itemType switch
                {
                    ThrowableType.Bottle => bottleLoudness,
                    ThrowableType.Can => canLoudness,
                    ThrowableType.Rock => rockLoudness,
                    _ => 12.0f
                };

                if (audioSource != null && impactAudioClip != null)
                {
                    audioSource.PlayOneShot(impactAudioClip);
                }

                // Impact creates acoustic noise event; attracts nearby zombies to landing position
                NoiseSystem.Emit(transform.position, loudness, NoiseKind.Interaction, gameObject);
            }
        }
    }
}
