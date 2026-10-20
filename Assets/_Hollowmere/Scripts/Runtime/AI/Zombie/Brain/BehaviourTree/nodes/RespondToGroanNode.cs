using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree action node for responding to a groan alert from another zombie.
    /// Drives the horde convergence mechanic without group-coordination code.
    /// Checks the separate groanBelief stored in ZombieMemory.
    /// Returns:
    /// - Failure if no groan belief exists, allowing the root Selector to fall through to Dormant.
    /// - Running while navigating toward the groan source position.
    /// - Success upon arriving at the groan location, clearing the groan belief from memory.
    /// </summary>
    public class RespondToGroanNode : BtNode
    {
        private readonly ZombieMemory memory;
        private NavMeshAgent agent;
        private readonly float arrivalDistance;

        private static readonly NavMeshQueryFilter HumanoidFilter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };

        private Vector3 currentTargetPosition = Vector3.positiveInfinity;

        /// <summary>
        /// Creates a RespondToGroanNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store holding the groanBelief.</param>
        /// <param name="agent">NavMeshAgent driving physical locomotion.</param>
        /// <param name="arrivalDistance">Threshold in metres to consider groan origin reached.</param>
        public RespondToGroanNode(ZombieMemory memory, NavMeshAgent agent = null, float arrivalDistance = 2.0f) 
            : base("RespondToGroan")
        {
            this.memory = memory;
            this.agent = agent;
            this.arrivalDistance = arrivalDistance;
        }

        /// <summary>
        /// Binds the NavMeshAgent reference from ZombieBody.
        /// </summary>
        public void SetNavMeshAgent(NavMeshAgent navMeshAgent)
        {
            agent = navMeshAgent;
        }

        /// <summary>
        /// Evaluates response to horde groan:
        /// 1. Checks separate groanBelief in memory; returns Failure if none exists.
        /// 2. Directs NavMeshAgent toward groan position (snapped to NavMesh).
        /// 3. Returns Running while moving, Success upon arrival.
        /// Zero heap allocations during execution.
        /// </summary>
        public override BtStatus Tick()
        {
            if (memory == null)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // Checks separate _groanBelief in memory; returns Failure if no groan belief
            if (!memory.HasGroanBelief)
            {
                currentTargetPosition = Vector3.positiveInfinity;
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            if (agent == null)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            ZombieBelief groanBelief = memory.GroanBelief;

            // Update destination if groan target position changed
            if ((groanBelief.Position - currentTargetPosition).sqrMagnitude > 0.25f)
            {
                currentTargetPosition = groanBelief.Position;
                Vector3 destination = groanBelief.Position;

                if (NavMesh.SamplePosition(groanBelief.Position, out NavMeshHit hit, 3.0f, HumanoidFilter))
                {
                    destination = hit.position;
                }

                if (agent.isOnNavMesh)
                {
                    agent.SetDestination(destination);
                }
            }

            if (!agent.isOnNavMesh || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // Check if agent arrived at the groan source
            if (!agent.pathPending)
            {
                float threshold = Mathf.Max(agent.stoppingDistance, arrivalDistance);

                if (agent.remainingDistance <= threshold)
                {
                    // Arrived at groan source; clear the alert and return Success
                    memory.ClearGroanBelief();
                    currentTargetPosition = Vector3.positiveInfinity;
                    LastStatus = BtStatus.Success;
                    return BtStatus.Success;
                }
            }

            LastStatus = BtStatus.Running;
            return BtStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            currentTargetPosition = Vector3.positiveInfinity;
        }
    }
}
