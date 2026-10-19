using Hollowmere.AI.Director;
using Hollowmere.AI.Scheduling;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Debugging
{
    /// <summary>
    /// TEMPORARY stand-in enemy (a grey capsule) until the real agents are ready.
    /// Lies dormant until it senses something, walks to where it believes the player is,
    /// chases when it is sure, and goes back to sleep when its belief fades.
    /// It is deliberately simple: it exists to test the senses layer and to give the Director
    /// something real to wake. The zombie owner replaces it with the real brain.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class StandInAgent : MonoBehaviour, INoiseListener, IAiThinker, IDirectable
    {
        public enum Mode { Dormant, Investigating, Chasing }

        [SerializeField] SightSensor sight = new SightSensor();
        [Tooltip("Seconds between thoughts (0.2 = five times a second).")]
        [SerializeField] float thinkInterval = 0.2f;
        [SerializeField] float walkSpeed = 1.4f;
        [SerializeField] float chaseSpeed = 3.5f;
        [Tooltip("Belief confidence at or above which it chases instead of investigating.")]
        [Range(0f, 1f)] [SerializeField] float chaseConfidence = 0.8f;

        readonly AgentMemory memory = new AgentMemory();
        NavMeshAgent agent;
        Mode mode = Mode.Dormant;

        public Mode CurrentMode => mode;
        public bool IsAwake => mode != Mode.Dormant;
        public AgentMemory Memory => memory;

        public Vector3 Position => transform.position;
        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => 1f;
        public float ThinkInterval => thinkInterval;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = walkSpeed;
        }

        void OnEnable()
        {
            NoiseSystem.Register(this);
            AiScheduler.Register(this);
            DirectableRegistry.Register(this);
        }

        void OnDisable()
        {
            NoiseSystem.Unregister(this);
            AiScheduler.Unregister(this);
            DirectableRegistry.Unregister(this);
        }

        public void OnHeard(in Noise noise, float pathDistance)
        {
            if (noise.Source == gameObject) return;
            memory.Remember(noise.Position, AgentMemory.HeardConfidence, BeliefSource.Heard, Time.time);
        }

        /// <summary>Used by the Director: wake up and head toward a point, as if told about it.</summary>
        public void WakeToward(Vector3 point)
        {
            memory.Remember(point, AgentMemory.ToldConfidence, BeliefSource.Told, Time.time);
        }

        public void Think(float deltaTime)
        {
            IPlayerState player = PlayerRegistry.Current;
            if (sight.Tick(transform, player, deltaTime))
                memory.Remember(player.Position, AgentMemory.SeenConfidence, BeliefSource.Seen, Time.time);

            float now = Time.time;
            if (!memory.HasBelief(now))
            {
                if (mode != Mode.Dormant)
                {
                    mode = Mode.Dormant;
                    agent.ResetPath();
                }
                return;
            }

            mode = memory.ConfidenceAt(now) >= chaseConfidence ? Mode.Chasing : Mode.Investigating;
            agent.speed = mode == Mode.Chasing ? chaseSpeed : walkSpeed;

            // Ask for a new path only when the target has moved, not on every thought.
            Vector3 target = memory.Current.Position;
            if (!agent.hasPath || (agent.destination - target).sqrMagnitude > 1f)
                agent.SetDestination(target);
        }

        void OnGUI()
        {
            float now = Time.time;
            string belief = memory.HasBelief(now)
                ? memory.Current.Source + " " + memory.ConfidenceAt(now).ToString("F2")
                : "-";
            DebugLabel.Above(transform, 2.4f,
                name + "  " + mode + "\nsight " + sight.Meter.ToString("F2") + "  belief " + belief);
        }

        /// <summary>Builds a grey capsule stand-in standing on the NavMesh near a point.</summary>
        public static StandInAgent Create(Vector3 near, float facingYaw, string name)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(near, out NavMeshHit hit, 3f, filter)) near = hit.position;

            // The root stands at the feet, so eye height and hearing are measured from the ground.
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(near, Quaternion.Euler(0f, facingYaw, 0f));

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up;
            body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.6f, 0.4f);
            nose.transform.localScale = Vector3.one * 0.2f;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;          // Humanoid
            agent.radius = 0.4f;
            agent.height = 1.9f;
            agent.angularSpeed = 240f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 1.2f;

            return root.AddComponent<StandInAgent>();
        }
    }
}
