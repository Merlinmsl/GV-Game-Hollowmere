using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hollowmere.AI.Senses;
using Hollowmere.AI.Director;
using Hollowmere.Combat;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Thin MonoBehaviour adapter connecting the pure C# ZombieBrain to Unity scene systems.
    /// - Owns the NavMeshAgent and feeds position/locomotion.
    /// - Ticks the brain in a background coroutine at configurable Hz (WaitForSeconds(1f / config.brainTickRate)).
    /// - Manages sensory NoiseSystem registration in OnEnable / OnDisable.
    /// - Emits state change and attack events for visual and animation listeners.
    /// - Emits zombie groan acoustic alert on entering Chase state.
    /// - Implements IDirectable for GameDirector AI waking and coordination.
    /// - Implements IDamageable for taking firearm damage and dying when HP reaches 0.
    /// - Implements IIceable for freezing in place when hit by the Ice Gun.
    /// - Triggers Animator parameters: Attack, Hit, and Dead.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieBody : MonoBehaviour, IDirectable, IDamageable, IIceable
    {
        [Header("Configuration")]
        [Tooltip("Zombie tuning ScriptableObject asset.")]
        [SerializeField] private ZombieConfig config;

        [Header("Health & Combat")]
        [Tooltip("Maximum health points for this zombie.")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Animation")]
        [Tooltip("Optional Animator reference. If null, auto-discovered in children.")]
        [SerializeField] private Animator animator;

        private NavMeshAgent agent;
        private ZombieBrain brain;
        private Coroutine brainTickCoroutine;
        private Coroutine freezeCoroutine;
        private bool _groanEmitted = false;
        private float currentHealth = 100f;

        public float Health01 => Mathf.Clamp01(currentHealth / maxHealth);
        public bool IsDead { get; private set; }

        // Stuck detection (Edge case: zombie stuck > 3s)
        private Vector3 _lastStuckCheckPosition;
        private float _stuckTimer = 0f;

        // Sight detection meter
        private float _sightMeter = 0f;
        private float _sightTimer = 0f;

        /// <summary>Access to the decoupled pure C# brain instance.</summary>
        public ZombieBrain Brain => brain;

        /// <summary>Access to the brain's mental memory store.</summary>
        public ZombieMemory Memory => brain?.Memory;

        /// <summary>Tuning configuration reference.</summary>
        public ZombieConfig Config => config;

        /// <summary>NavMeshAgent component driving physical navigation.</summary>
        public NavMeshAgent Agent => agent;

        /// <summary>Current accumulated sight detection meter (0.0 to 1.0).</summary>
        public float SightMeter => _sightMeter;

        /// <summary>Name of the actively running Behaviour Tree leaf node.</summary>
        public string CurrentStateName => brain?.ActiveNode != null ? brain.ActiveNode.Name : "None";

        /// <summary>Whether a groan has been emitted during the current Chase activation.</summary>
        public bool GroanEmitted => _groanEmitted;

        /// <summary>Fired when the active BT node transitions (e.g. Dormant -> Chase).</summary>
        public event Action<string> OnStateChanged;

        /// <summary>Fired when an attack swing is initiated.</summary>
        public event Action OnAttack;

        // ─────────────────────────────────────────────────────────────────────────
        // IDirectable IMPLEMENTATION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Current world position of the zombie for the Director.</summary>
        public Vector3 Position => transform.position;

        /// <summary>True if the zombie is currently active (not in the Dormant state).</summary>
        public bool IsAwake => CurrentStateName != "Dormant";

        /// <summary>
        /// Wake up and direct toward this point, remembered as a 'Told' belief.
        /// </summary>
        public void WakeToward(Vector3 point)
        {
            float toldConf = (config != null && config.confidenceTold > 0f) ? config.confidenceTold : 0.4f;
            brain?.Memory?.UpdateBelief(point, toldConf, BeliefSource.Told);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ANIMATION TRIGGERS
        // ─────────────────────────────────────────────────────────────────────────

        private void HandleAttack()
        {
            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }
            OnAttack?.Invoke();

            // Deal melee damage to the player if within reach
            IPlayerState player = PlayerRegistry.Instance;
            if (player is MonoBehaviour mb)
            {
                float meleeReach = config != null ? config.attackRange + 0.5f : 2.3f;
                if (Vector3.Distance(transform.position, player.Position) <= meleeReach)
                {
                    var damageable = mb.GetComponent<IDamageable>();
                    damageable?.TakeDamage(25f, player.Position, (player.Position - transform.position).normalized, gameObject);
                }
            }
        }

        /// <summary>Triggers the Hit reaction animation.</summary>
        public void TriggerHit()
        {
            if (animator != null)
            {
                animator.SetTrigger("Hit");
            }
        }

        /// <summary>
        /// Inflicts firearm damage, notifies memory of the attacker's position,
        /// and transitions to death state if HP is exhausted.
        /// </summary>
        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection, GameObject attacker = null)
        {
            if (IsDead) return;

            currentHealth = Mathf.Max(0f, currentHealth - amount);

            if (attacker != null)
            {
                brain?.Memory?.UpdateBelief(attacker.transform.position, 1.0f, BeliefSource.Seen);
            }

            if (currentHealth <= 0f)
            {
                IsDead = true;
                SetDead(true);

                // Disable physical colliders and agent so player isn't blocked by corpse
                var cols = GetComponentsInChildren<Collider>();
                foreach (var c in cols) c.enabled = false;
                if (agent != null && agent.enabled) agent.enabled = false;
            }
            else
            {
                TriggerHit();
            }
        }

        /// <summary>
        /// Freezes zombie in place when hit by the Ice Gun spray.
        /// </summary>
        public void ReceiveIce(float seconds)
        {
            if (IsDead) return;
            if (freezeCoroutine != null) StopCoroutine(freezeCoroutine);
            freezeCoroutine = StartCoroutine(FreezeRoutine(seconds));
        }

        private IEnumerator FreezeRoutine(float seconds)
        {
            float prevSpeed = agent != null ? agent.speed : 3.5f;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.speed = 0f;
            }

            float prevAnimSpeed = animator != null ? animator.speed : 1f;
            if (animator != null) animator.speed = 0f;

            yield return new WaitForSeconds(seconds);

            if (animator != null) animator.speed = prevAnimSpeed;
            if (agent != null && agent.isOnNavMesh && !IsDead)
            {
                agent.speed = prevSpeed;
                agent.isStopped = false;
            }

            freezeCoroutine = null;
        }

        /// <summary>Sets the Dead state parameter on the Animator and stops agent navigation & AI loop.</summary>
        public void SetDead(bool dead = true)
        {
            if (animator != null)
            {
                animator.SetBool("Dead", dead);
            }

            if (dead)
            {
                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                {
                    agent.ResetPath();
                }

                if (brainTickCoroutine != null)
                {
                    StopCoroutine(brainTickCoroutine);
                    brainTickCoroutine = null;
                }
            }
        }

        private void Awake()
        {
            currentHealth = maxHealth;
            agent = GetComponent<NavMeshAgent>();

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
            // Register with the GameDirector registry
            DirectableRegistry.Register(this);

            // Register mental memory as an acoustic listener with the global NoiseSystem
            brain?.Memory?.OnEnable();

            _lastStuckCheckPosition = transform.position;
            _stuckTimer = 0f;

            // Start periodic AI decision loop
            brainTickCoroutine = StartCoroutine(BrainTickRoutine());
        }

        private void OnDisable()
        {
            // Unregister from GameDirector registry
            DirectableRegistry.Unregister(this);

            // Unregister acoustic listener to prevent dangling callbacks
            brain?.Memory?.OnDisable();

            _groanEmitted = false;

            if (brainTickCoroutine != null)
            {
                StopCoroutine(brainTickCoroutine);
                brainTickCoroutine = null;
            }

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                agent.ResetPath();
            }
        }

        private void Update()
        {
            // 4 Hz cone + raycast check: every 0.25 s
            float sightRate = (config != null && config.sightTickRate > 0f) ? config.sightTickRate : 4.0f;
            float sightInterval = 1.0f / sightRate;
            _sightTimer += Time.deltaTime;

            if (_sightTimer >= sightInterval)
            {
                _sightTimer -= sightInterval;
                EvaluateSightCheck(sightInterval);
            }
        }

        /// <summary>
        /// Evaluates 4 Hz vision cone and raycast visibility check against the player.
        /// Accumulates sight detection meter; on meter >= 1.0, updates belief to Seen (1.0).
        /// </summary>
        private void EvaluateSightCheck(float dt)
        {
            IPlayerState player = PlayerRegistry.Instance;
            if (player == null)
            {
                _sightMeter = Mathf.Max(0f, _sightMeter - dt * 0.5f);
                return;
            }

            Vector3 eyePos = transform.position + Vector3.up * 1.6f;
            Vector3 playerTarget = player.Position + Vector3.up * (player.IsCrouching ? 0.7f : 1.2f);
            Vector3 toPlayer = playerTarget - eyePos;
            float distance = toPlayer.magnitude;

            float range = (config != null && config.viewRange > 0f) ? config.viewRange : 14.0f;
            if (distance > range)
            {
                _sightMeter = Mathf.Max(0f, _sightMeter - dt * 0.5f);
                return;
            }

            float viewAngle = (config != null && config.viewAngle > 0f) ? config.viewAngle : 70.0f;
            float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
            if (angle > viewAngle)
            {
                _sightMeter = Mathf.Max(0f, _sightMeter - dt * 0.5f);
                return;
            }

            // Raycast check to test line of sight occlusion
            bool lineOfSight = false;
            if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= distance - 0.5f || hit.collider.CompareTag("Player") || hit.collider.GetComponentInParent<IPlayerState>() != null)
                {
                    lineOfSight = true;
                }
            }
            else
            {
                lineOfSight = true;
            }

            if (lineOfSight)
            {
                float baseRate = (config != null && config.detectionBaseRate > 0f) ? config.detectionBaseRate : 0.35f;
                float lightFactor = player.FlashlightOn ? 1.0f : 0.6f;
                float postureFactor = player.IsCrouching ? 0.6f : 1.0f;
                float distFactor = Mathf.Clamp01(1f - (distance / range));
                float accumulation = baseRate * lightFactor * postureFactor * (0.5f + 0.5f * distFactor) * dt * 4f;

                _sightMeter = Mathf.Clamp01(_sightMeter + accumulation);

                if (_sightMeter >= 1.0f)
                {
                    float seenConf = (config != null && config.confidenceSeen > 0f) ? config.confidenceSeen : 1.0f;
                    brain?.Memory?.UpdateBelief(player.Position, seenConf, BeliefSource.Seen);
                }
            }
            else
            {
                _sightMeter = Mathf.Max(0f, _sightMeter - dt * 0.5f);
            }
        }

        private const NoiseKind ZombieGroan = NoiseKind.Groan;

        /// <summary>
        /// Handles state transitions reported by ZombieBrain.
        /// Emits a zombie groan noise on entering Chase state from non-Chase to alert nearby zombies,
        /// guarded by _groanEmitted to ensure only one groan is emitted per state transition.
        /// </summary>
        /// <param name="newState">The newly activated behaviour tree state name.</param>
        private void HandleStateChanged(string newState)
        {
            if (newState == "Chase")
            {
                if (!_groanEmitted)
                {
                    NoiseSystem.Emit(transform.position, 20f, ZombieGroan, gameObject);
                    _groanEmitted = true;
                }
            }
            else
            {
                _groanEmitted = false;
            }

            OnStateChanged?.Invoke(newState);
        }

        /// <summary>
        /// Periodic AI evaluation routine running at config.brainTickRate (default 8 Hz).
        /// Decouples AI reasoning from frame rate to strictly respect the 2 ms performance budget.
        /// </summary>
        private IEnumerator BrainTickRoutine()
        {
            while (true)
            {
                float tickRate = (config != null && config.brainTickRate > 0f) ? config.brainTickRate : 8.0f;
                float tickInterval = 1.0f / tickRate;
                yield return new WaitForSeconds(tickInterval);

                if (brain != null && agent != null)
                {
                    // Guard against ticks before agent is successfully placed on NavMesh
                    if (agent.isOnNavMesh)
                    {
                        // Edge case: zombie stuck > 3 s — clear belief and return to Dormant
                        if (brain.Memory != null && (brain.Memory.HasBelief || brain.Memory.HasGroanBelief))
                        {
                            float distanceMoved = Vector3.Distance(transform.position, _lastStuckCheckPosition);
                            if (distanceMoved < 0.1f)
                            {
                                _stuckTimer += tickInterval;
                                float timeout = (config != null && config.stuckTimeout > 0f) ? config.stuckTimeout : 3.0f;
                                if (_stuckTimer >= timeout)
                                {
                                    brain.Memory.ClearBelief();
                                    brain.Memory.ClearGroanBelief();
                                    if (agent.hasPath)
                                    {
                                        agent.ResetPath();
                                    }
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
            }
        }
    }
}
