using UnityEngine;
using Hollowmere.AI.Senses;

namespace Hollowmere.Interactables
{
    /// <summary>
    /// Physical barricade that can be pushed by the player with physics force.
    /// Configures high mass and linear drag on the Rigidbody so it resists movement
    /// and smoothly slows to a stop when no longer pushed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class Barricade : MonoBehaviour
    {
        [Header("Push Physics Settings")]
        [Tooltip("Force magnitude applied to push the barricade.")]
        [SerializeField] private float pushForce = 35.0f;

        [Tooltip("Linear drag applied to the Rigidbody so it slows down quickly after pushing.")]
        [SerializeField] private float drag = 4.0f;

        [Tooltip("Mass of the barricade in kilograms.")]
        [SerializeField] private float mass = 60.0f;

        [Header("Noise Settings")]
        [Tooltip("Linear speed threshold in m/s above which pushing the barricade emits noise.")]
        [SerializeField] private float speedThreshold = 0.4f;

        [Tooltip("Loudness of noise emitted when pushed (in metres of walking distance).")]
        [SerializeField] private float noiseLoudness = 12.0f;

        [Tooltip("Cooldown in seconds between noise emission pulses.")]
        [SerializeField] private float noiseCooldown = 0.6f;

        private Rigidbody rb;
        private float lastNoiseTime;

        /// <summary>The Rigidbody driving this barricade's physical motion.</summary>
        public Rigidbody Rigidbody => rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = drag;
            rb.angularDamping = 2.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private void FixedUpdate()
        {
            if (rb != null && !rb.isKinematic)
            {
                float speed = rb.linearVelocity.magnitude;
                if (speed > speedThreshold)
                {
                    if (Time.time - lastNoiseTime >= noiseCooldown)
                    {
                        lastNoiseTime = Time.time;
                        NoiseSystem.Emit(transform.position, noiseLoudness, NoiseKind.Interaction, gameObject);
                    }
                }
            }
        }

        /// <summary>
        /// Applies push force to the barricade in the specified direction.
        /// </summary>
        /// <param name="direction">World-space push direction.</param>
        /// <param name="forceMultiplier">Optional force multiplier.</param>
        public void Push(Vector3 direction, float forceMultiplier = 1.0f)
        {
            if (rb == null || rb.isKinematic) return;

            Vector3 horizontalPush = new Vector3(direction.x, 0f, direction.z).normalized;
            rb.AddForce(horizontalPush * (pushForce * forceMultiplier), ForceMode.Force);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<CharacterController>() != null)
            {
                Vector3 pushDirection = transform.position - collision.transform.position;
                pushDirection.y = 0f;
                if (pushDirection.sqrMagnitude > 0.001f)
                {
                    Push(pushDirection.normalized);
                }
            }
        }
    }
}
