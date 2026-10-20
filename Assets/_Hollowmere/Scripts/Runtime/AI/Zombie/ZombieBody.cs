using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hollowmere.AI.Senses;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Thin MonoBehaviour adapter connecting the pure C# ZombieBrain to Unity scene systems.
    /// - Owns the NavMeshAgent and feeds position/locomotion.
    /// - Ticks the brain in a background coroutine at configurable Hz (WaitForSeconds(1f / config.brainTickRate)).
    /// - Manages sensory NoiseSystem registration in OnEnable / OnDisable.
    /// - Emits state change and attack events for visual and animation listeners.
    /// - Emits zombie groan acoustic alert on entering Chase state.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieBody : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Zombie tuning ScriptableObject asset.")]
        [SerializeField] private ZombieConfig config;

        private NavMeshAgent agent;
        private ZombieBrain brain;
        private Coroutine brainTickCoroutine;
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

        /// <summary>Name of the actively running Behaviour Tree leaf node.</summary>
        public string CurrentStateName => brain?.ActiveNode != null ? brain.ActiveNode.Name : "None";

        /// <summary>Whether a groan has been emitted during the current Chase activation.</summary>
        public bool GroanEmitted => _groanEmitted;

        /// <summary>Fired when the active BT node transitions (e.g. Dormant -> Chase).</summary>
        public event Action<string> OnStateChanged;

        /// <summary>Fired when an attack swing is initiated.</summary>
        public event Action OnAttack;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();

            // Instantiate decoupled pure C# brain
            brain = new ZombieBrain(config, agent, transform);

            brain.OnStateChanged += HandleStateChanged;
            brain.OnAttack += () => OnAttack?.Invoke();
        }

        private void OnEnable()
        {
            // Register mental memory as an acoustic listener with the global NoiseSystem
            brain?.Memory?.OnEnable();

            _lastStuckCheckPosition = transform.position;
            _stuckTimer = 0f;

            // Start periodic AI decision loop
            brainTickCoroutine = StartCoroutine(BrainTickRoutine());
        }

        private void OnDisable()
        {
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
