using System;
using UnityEngine;
using UnityEngine.AI;
using Hollowmere.AI.Senses;
using Hollowmere.AI.Director;
using Hollowmere.AI.Scheduling;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Thin MonoBehaviour adapter connecting the pure C# ZombieBrain to Unity scene systems.
    /// - Owns the NavMeshAgent and feeds position/locomotion.
    /// - Schedules thinking through the shared AiScheduler (IAiThinker) with zero per-tick GC allocations.
    /// - Evaluates player sight through the shared SightSensor (illumination, darkness, crouching rules).
    /// - Manages sensory NoiseSystem registration in OnEnable / OnDisable.
    /// - Emits configurable zombie groan acoustic alert (config.groanLoudness) on entering Chase state.
    /// - Implements IDirectable for GameDirector AI waking and coordination.
    /// - Triggers Animator parameters: Attack, Hit, and Dead; supports weapon damage via TakeDamage().
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieBody : MonoBehaviour, IDirectable, IAiThinker
    {
        [Header("Configuration")]
        [Tooltip("Zombie tuning ScriptableObject asset.")]
        [SerializeField] private ZombieConfig config;

        [Header("Senses")]
        [Tooltip("Vision sensor using shared lighting, darkness, and posture rules.")]
        [SerializeField] private SightSensor sight = new SightSensor();

        [Header("Animation")]
        [Tooltip("Optional Animator reference. If null, auto-discovered in children.")]
        [SerializeField] private Animator animator;

        [Header("Health & Damage")]
        [Tooltip("Maximum hit points before death.")]
        [SerializeField] private float maxHealth = 100f;
        private float currentHealth;

        private NavMeshAgent agent;
        private ZombieBrain brain;
        private bool _groanEmitted = false;

        // Stuck detection (Edge case: zombie stuck > 3s)
        private Vector3 _lastStuckCheckPosition;
        private float _stuckTimer = 0f;

        /// <summary>Access to the decoupled pure C# brain instance.</summary>
        public ZombieBrain Brain => brain;

        /// <summary>Access to the brain's mental memory store.</summary>
        public ZombieMemory Memory => brain?.Memory;

        /// <summary>Tuning configuration reference.</summary>
        public ZombieConfig Config => config;

        /// <summary>NavMeshAgent component driving physical navigation.</summary>
        public NavMeshAgent Agent => agent;

        /// <summary>Vision sensor reference.</summary>
        public SightSensor Sight => sight;

        /// <summary>Current accumulated sight detection meter (0.0 to 1.0) from SightSensor.</summary>
        public float SightMeter => sight.Meter;

        /// <summary>Name of the actively running Behaviour Tree leaf node.</summary>
        public string CurrentStateName => brain?.ActiveNode != null ? brain.ActiveNode.Name : "None";

        /// <summary>Whether a groan has been emitted during the current Chase activation.</summary>
        public bool GroanEmitted => _groanEmitted;

        /// <summary>Whether the zombie has been killed.</summary>
        public bool IsDead => currentHealth <= 0f;

        /// <summary>Fired when the active BT node transitions (e.g. Dormant -> Chase).</summary>
        public event Action<string> OnStateChanged;

        /// <summary>Fired when an attack swing is initiated.</summary>
        public event Action OnAttack;

        // ─────────────────────────────────────────────────────────────────────────
        // IDirectable IMPLEMENTATION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Current world position of the zombie for the Director.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// True if the zombie currently has a belief or groan to follow.
        /// Does not read leaf node name to prevent false wakes before first thought.
        /// </summary>
        public bool IsAwake => (brain != null && brain.Memory != null) && (brain.Memory.HasBelief || brain.Memory.HasGroanBelief);

        /// <summary>
        /// Wake up and direct toward this point, remembered as a 'Told' belief.
        /// Only replaces existing belief if told confidence is greater than or equal to current belief.
        /// </summary>
        public void WakeToward(Vector3 point)
        {
            float toldConf = (config != null && config.confidenceTold > 0f) ? config.confidenceTold : 0.4f;
            if (brain != null && brain.Memory != null)
            {
                if (!brain.Memory.HasBelief || toldConf >= brain.Memory.CurrentBelief.Confidence)
                {
                    brain.Memory.UpdateBelief(point, toldConf, BeliefSource.Told);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // IAiThinker IMPLEMENTATION (SHARED AiScheduler)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds between thoughts, derived from config.brainTickRate (e.g. 8 Hz = 0.125 s).
        /// </summary>
        public float ThinkInterval => (config != null && config.brainTickRate > 0f) ? 1.0f / config.brainTickRate : 0.125f;

        /// <summary>
        /// Evaluates sight sensor, stuck timer, and behaviour tree decisions through AiScheduler.
        /// Staggered across frames and measured for performance profiling with zero GC allocations.
        /// </summary>
        public void Think(float deltaTime)
        {
            if (IsDead) return;

            // 1. Evaluate player sight through shared SightSensor
            IPlayerState player = PlayerRegistry.Current ?? PlayerRegistry.Instance;
            if (sight.Tick(transform, player, deltaTime))
            {
                float seenConf = (config != null && config.confidenceSeen > 0f) ? config.confidenceSeen : 1.0f;
                brain?.Memory?.UpdateBelief(player.Position, seenConf, BeliefSource.Seen);
            }

            // 2. Navigation & Brain evaluation
            if (brain != null && agent != null && agent.isOnNavMesh)
            {
                // Edge case: zombie stuck > 3 s — clear belief and return to Dormant
                if (brain.Memory != null && (brain.Memory.HasBelief || brain.Memory.HasGroanBelief))
                {
                    float distanceMoved = Vector3.Distance(transform.position, _lastStuckCheckPosition);
                    if (distanceMoved < 0.1f)
                    {
                        _stuckTimer += deltaTime;
                        float timeout = (config != null && config.stuckTimeout > 0f) ? config.stuckTimeout : 3.0f;
                        if (_stuckTimer >= timeout)
                        {
                            brain.Memory.ClearBelief();
                            brain.Memory.ClearGroanBelief();
                            if (agent.hasPath) agent.ResetPath();
                            _stuckTimer = 0f;
                            _lastStuckCheckPosition = transform.position;
                        }
                    }
                    else
                    {
                        _stuckTimer = 0f;
                        _lastStuckCheckPosition = transform.position;
                    }
                }
                else
                {
                    _stuckTimer = 0f;
                    _lastStuckCheckPosition = transform.position;
                }

                brain.Tick();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DAMAGE & ANIMATION TRIGGERS
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies damage from weapons. Triggers Hit reaction or Dead state upon lethal damage.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            currentHealth -= amount;
            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                SetDead(true);
            }
            else
            {
                TriggerHit();
            }
        }

        private void HandleAttack()
        {
            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }
            OnAttack?.Invoke();
        }

        /// <summary>Triggers the Hit reaction animation.</summary>
        public void TriggerHit()
        {
            if (animator != null)
            {
                animator.SetTrigger("Hit");
            }
        }

        /// <summary>Sets the Dead state parameter on the Animator and halts agent navigation & AI loop.</summary>
        public void SetDead(bool dead = true)
        {
            if (animator != null)
            {
                animator.SetBool("Dead", dead);
            }

            if (dead)
            {
                currentHealth = 0f;
                DirectableRegistry.Unregister(this);
                AiScheduler.Unregister(this);

                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                {
                    agent.ResetPath();
                }

                if (agent != null)
                {
                    agent.enabled = false;
                }
            }
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            currentHealth = maxHealth;

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // Instantiate decoupled pure C# brain
            brain = new ZombieBrain(config, agent, transform);

            brain.OnStateChanged += HandleStateChanged;
            brain.OnAttack += HandleAttack;
        }

        private void OnEnable()
        {
            if (IsDead) return;

            // Register with the GameDirector registry
            DirectableRegistry.Register(this);

            // Register with the shared AiScheduler for zero-allocation staggered thinking
            AiScheduler.Register(this);

            // Register mental memory as an acoustic listener with the global NoiseSystem
            brain?.Memory?.OnEnable();

            _lastStuckCheckPosition = transform.position;
            _stuckTimer = 0f;
        }

        private void OnDisable()
        {
            // Unregister from GameDirector registry
            DirectableRegistry.Unregister(this);

            // Unregister from AiScheduler
            AiScheduler.Unregister(this);

            // Unregister acoustic listener to prevent dangling callbacks
            brain?.Memory?.OnDisable();

            _groanEmitted = false;

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                agent.ResetPath();
            }
        }

        private const NoiseKind ZombieGroan = NoiseKind.Groan;

        /// <summary>
        /// Handles state transitions reported by ZombieBrain.
        /// Emits a zombie groan noise using config.groanLoudness on entering Chase state from non-Chase.
        /// </summary>
        private void HandleStateChanged(string newState)
        {
            if (newState == "Chase")
            {
                if (!_groanEmitted)
                {
                    float loudness = (config != null && config.groanLoudness > 0f) ? config.groanLoudness : 20f;
                    NoiseSystem.Emit(transform.position, loudness, ZombieGroan, gameObject);
                    _groanEmitted = true;
                }
            }
            else
            {
                _groanEmitted = false;
            }

            OnStateChanged?.Invoke(newState);
        }
    }
}
