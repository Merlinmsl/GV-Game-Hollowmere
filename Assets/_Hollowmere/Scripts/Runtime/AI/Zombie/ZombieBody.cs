using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Thin MonoBehaviour adapter connecting the pure C# ZombieBrain to Unity scene systems.
    /// - Owns the NavMeshAgent and feeds position/locomotion.
    /// - Ticks the brain in a background coroutine at configurable Hz (WaitForSeconds(1f / config.brainTickRate)).
    /// - Manages sensory NoiseSystem registration in OnEnable / OnDisable.
    /// - Emits state change and attack events for visual and animation listeners.
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

        /// <summary>Fired when the active BT node transitions (e.g. Dormant -> Chase).</summary>
        public event Action<string> OnStateChanged;

        /// <summary>Fired when an attack swing is initiated.</summary>
        public event Action OnAttack;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();

            // Instantiate decoupled pure C# brain
            brain = new ZombieBrain(config, agent, transform);

            brain.OnStateChanged += state => OnStateChanged?.Invoke(state);
            brain.OnAttack += () => OnAttack?.Invoke();
        }

        private void OnEnable()
        {
            // Register mental memory as an acoustic listener with the global NoiseSystem
            brain?.Memory?.OnEnable();

            // Start periodic AI decision loop
            brainTickCoroutine = StartCoroutine(BrainTickRoutine());
        }

        private void OnDisable()
        {
            // Unregister acoustic listener to prevent dangling callbacks
            brain?.Memory?.OnDisable();

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

        /// <summary>
        /// Periodic AI evaluation routine running at config.brainTickRate (default 8 Hz).
        /// Decouples AI reasoning from frame rate to strictly respect the 2 ms performance budget.
        /// </summary>
        private IEnumerator BrainTickRoutine()
        {
            while (true)
            {
                float tickRate = (config != null && config.brainTickRate > 0f) ? config.brainTickRate : 8.0f;
                yield return new WaitForSeconds(1.0f / tickRate);

                if (brain != null && agent != null)
                {
                    // Guard against ticks before agent is successfully placed on NavMesh
                    if (agent.isOnNavMesh)
                    {
                        brain.Tick();
                    }
                }
            }
        }
    }
}
