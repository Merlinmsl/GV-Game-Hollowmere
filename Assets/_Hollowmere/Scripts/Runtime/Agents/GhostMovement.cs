using UnityEngine;

namespace Hollowmere.Agents
{
    /// <summary>
    /// Floating movement for ghosts. Ghosts pass through walls, so there is no NavMeshAgent: the brain
    /// gives a point (MoveTo) and this eases the body there with SmoothDamp and turns it to face the
    /// player. A slow bob and a lean into the direction of travel make it look alive; both go on the
    /// Visual child, so the root stays at the position the brain reasons about. While inside a wall
    /// the material fades out, and it fades back in when the ghost leaves.
    /// </summary>
    public sealed class GhostMovement : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("Child holding the mesh. Bob and lean are applied to it, not to this root. Left empty, the first child is used.")]
        [SerializeField] Transform visual;
        [Tooltip("What the ghost turns to face, normally the player. Left empty, it faces where it is going.")]
        [SerializeField] Transform lookTarget;
        [Tooltip("Optional: follow this transform. Handy for testing; in the game the brain calls MoveTo.")]
        [SerializeField] Transform destinationMarker;

        [Header("Movement")]
        [Tooltip("Roughly how many seconds it takes to reach the destination.")]
        [SerializeField] float smoothTime = 1.2f;
        [SerializeField] float maxSpeed = 3f;
        [Tooltip("How quickly it turns to face the look target. Higher is snappier.")]
        [SerializeField] float turnSharpness = 3f;

        [Header("Looking alive")]
        [Tooltip("Metres the body drifts up and down.")]
        [SerializeField] float bobHeight = 0.12f;
        [Tooltip("Speed of the bob, in radians per second (2 is about one bob every 3 seconds).")]
        [SerializeField] float bobSpeed = 2f;
        [Tooltip("Degrees the body tilts into the direction of travel at full speed.")]
        [SerializeField] float leanAngle = 12f;

        [Header("Walls")]
        [Tooltip("Layers that count as walls. The ghost must have no solid collider on these layers itself.")]
        [SerializeField] LayerMask wallMask = 1;
        [Tooltip("Height above the root where the wall check is made: about the middle of the body.")]
        [SerializeField] float wallCheckHeight = 1f;
        [SerializeField] float wallCheckRadius = 0.3f;
        [Tooltip("How visible the ghost stays inside a wall, as a share of its normal alpha.")]
        [SerializeField, Range(0f, 1f)] float visibilityInWall = 0.1f;
        [Tooltip("How fast it fades, in full fades per second.")]
        [SerializeField] float fadeSpeed = 3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Vector3 destination;
        Vector3 velocity;                             // SmoothDamp keeps this between frames
        Vector3 visualRestPosition;
        Quaternion visualRestRotation;
        float bobPhase;                               // random, so several ghosts do not bob in step
        Renderer[] renderers;
        Color[] baseColors;
        MaterialPropertyBlock block;
        float visibility = 1f;

        /// <summary>Where the ghost is drifting to.</summary>
        public Vector3 Destination => destination;

        /// <summary>Called by the brain: drift to this point.</summary>
        public void MoveTo(Vector3 point) => destination = point;

        /// <summary>Called by the brain: face this target, normally the player. Null faces the direction of travel.</summary>
        public void SetLookTarget(Transform target) => lookTarget = target;

        void Awake()
        {
            destination = transform.position;

            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual == null)
            {
                Debug.LogError($"[GhostMovement] {name} needs a Visual child holding the mesh.", this);
                enabled = false;
                return;
            }

            visualRestPosition = visual.localPosition;
            visualRestRotation = visual.localRotation;
            bobPhase = Random.value * Mathf.PI * 2f;

            renderers = visual.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                baseColors[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
            }
            block = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (destinationMarker != null) destination = destinationMarker.position;

            // Eases in and out: fast when far, slowing as it arrives, never faster than maxSpeed.
            transform.position = Vector3.SmoothDamp(transform.position, destination, ref velocity, smoothTime, maxSpeed);

            Turn();
            BobAndLean();
            FadeInWalls();
        }

        void Turn()
        {
            Vector3 direction = lookTarget != null ? lookTarget.position - transform.position : velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;

            // Exponential smoothing: the same feel at any frame rate.
            float t = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), t);
        }

        void BobAndLean()
        {
            float bob = Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobHeight;
            visual.localPosition = visualRestPosition + Vector3.up * bob;

            // Velocity in the ghost's own frame: forward speed tips it forward, sideways speed rolls it.
            Vector3 local = Vector3.ClampMagnitude(transform.InverseTransformDirection(velocity) / maxSpeed, 1f);
            Quaternion lean = Quaternion.Euler(local.z * leanAngle, 0f, -local.x * leanAngle);
            visual.localRotation = lean * visualRestRotation;
        }

        void FadeInWalls()
        {
            Vector3 centre = transform.position + Vector3.up * wallCheckHeight;
            bool inWall = Physics.CheckSphere(centre, wallCheckRadius, wallMask, QueryTriggerInteraction.Ignore);

            float target = inWall ? visibilityInWall : 1f;
            if (Mathf.Approximately(visibility, target)) return;
            visibility = Mathf.MoveTowards(visibility, target, fadeSpeed * Time.deltaTime);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (visibility >= 1f)
                {
                    renderers[i].SetPropertyBlock(null);  // fully visible: back to the plain material
                    continue;
                }
                Color colour = baseColors[i];
                colour.a *= visibility;
                block.SetColor(BaseColor, colour);
                renderers[i].SetPropertyBlock(block);
            }
        }

        // Shows the destination and the wall check in the Scene view while selected.
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, Application.isPlaying ? destination : transform.position);
            Gizmos.DrawWireSphere(Application.isPlaying ? destination : transform.position, 0.2f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * wallCheckHeight, wallCheckRadius);
        }
    }
}
