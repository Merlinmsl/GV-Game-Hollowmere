using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree action node representing aggressive pursuit (Chase).
    /// Active when the zombie has high certainty of the target's position (belief.confidence >= 0.8).
    /// Continually updates NavMeshAgent.destination to belief.Position.
    /// Returns:
    /// - Running while belief.confidence >= chaseThreshold (0.8).
    /// - Failure when confidence drops below the threshold, handing control back to the root
    ///   Selector so it can smoothly transition down to Investigate or Dormant.
    /// </summary>
    public class ChaseNode : BtNode
    {
        private readonly ZombieMemory memory;
        private readonly ZombieConfig config;
        private NavMeshAgent agent;
        private readonly float fallbackChaseThreshold;

        private Vector3 lastDestination = Vector3.positiveInfinity;

        /// <summary>
        /// Threshold confidence required to sustain chase behaviour (default 0.8).
        /// </summary>
        public float ChaseThreshold => config != null ? config.chaseThreshold : fallbackChaseThreshold;

        /// <summary>
        /// Creates a ChaseNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store.</param>
        /// <param name="agent">NavMeshAgent driving physical pursuit locomotion.</param>
        /// <param name="config">Zombie tuning configuration ScriptableObject.</param>
        /// <param name="fallbackChaseThreshold">Fallback threshold if config is null (default 0.8).</param>
        public ChaseNode(ZombieMemory memory, NavMeshAgent agent = null, ZombieConfig config = null, float fallbackChaseThreshold = 0.8f) 
            : base("Chase")
        {
            this.memory = memory;
            this.agent = agent;
            this.config = config;
            this.fallbackChaseThreshold = fallbackChaseThreshold;
        }

        /// <summary>
        /// Binds the NavMeshAgent reference from ZombieBody.
        /// </summary>
        public void SetNavMeshAgent(NavMeshAgent navMeshAgent)
        {
            agent = navMeshAgent;
        }

        /// <summary>
        /// Evaluates chase pursuit:
        /// - If confidence drops below ChaseThreshold (0.8), returns Failure to hand back to selector.
        /// - While confidence >= ChaseThreshold, tracks belief.Position and returns Running.
        /// Zero allocations during execution.
        /// </summary>
        public override BtStatus Tick()
        {
            if (memory == null || agent == null)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            ZombieBelief belief = memory.GetBelief();

            // When confidence drops below threshold (e.g. decayed over time or target broke line of sight),
            // return Failure to hand control back to the selector (falls through to Investigate)
            if (!belief.IsValid || belief.Confidence < ChaseThreshold)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // Pursue belief position (last seen or high-confidence sound)
            if ((belief.Position - lastDestination).sqrMagnitude > 0.1f)
            {
                lastDestination = belief.Position;
                Vector3 destination = belief.Position;

                if (NavMesh.SamplePosition(belief.Position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                {
                    destination = hit.position;
                }

                if (agent.isOnNavMesh)
                {
                    agent.SetDestination(destination);
                }
            }

            // Agent is actively pursuing with high confidence
            LastStatus = BtStatus.Running;
            return BtStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            lastDestination = Vector3.positiveInfinity;
        }
    }
}
