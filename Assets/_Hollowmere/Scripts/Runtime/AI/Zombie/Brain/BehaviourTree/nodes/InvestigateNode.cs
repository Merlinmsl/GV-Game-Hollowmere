using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree action node for investigating a suspected noise or target location.
    /// Drives the NavMeshAgent toward belief.Position.
    /// Returns:
    /// - Running while the agent is navigating along the path.
    /// - Success once the agent arrives within the stopping/arrival radius.
    /// - Failure if there is no active belief or the path is invalid.
    /// </summary>
    public class InvestigateNode : BtNode
    {
        private readonly ZombieMemory memory;
        private NavMeshAgent agent;
        private readonly float arrivalDistance;

        private Vector3 currentTargetPosition = Vector3.positiveInfinity;

        /// <summary>
        /// Creates an InvestigateNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store.</param>
        /// <param name="agent">NavMeshAgent driving physical locomotion.</param>
        /// <param name="arrivalDistance">Threshold in metres to consider destination reached.</param>
        public InvestigateNode(ZombieMemory memory, NavMeshAgent agent = null, float arrivalDistance = 1.5f) : base("Investigate")
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
        /// Evaluates investigation movement:
        /// 1. Reads current belief; fails if belief is missing or confidence is 0.
        /// 2. Sets NavMeshAgent destination (snapped to NavMesh) if not already moving there.
        /// 3. Returns Running while traveling, and Success once within arrivalDistance.
        /// Zero heap allocations during execution.
        /// </summary>
        public override BtStatus Tick()
        {
            if (memory == null || agent == null)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            ZombieBelief belief = memory.GetBelief();

            // Fail if no active belief or confidence has decayed to zero
            if (!belief.IsValid)
            {
                currentTargetPosition = Vector3.positiveInfinity;
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // If the belief target changed, sample NavMesh and update destination
            if ((belief.Position - currentTargetPosition).sqrMagnitude > 0.25f)
            {
                currentTargetPosition = belief.Position;
                Vector3 destination = belief.Position;

                // Wrap destination in NavMesh.SamplePosition to sample nearest walkable point before pathing
                // Handles sounds originating off-mesh, elevated on roofs, or outside boundary walls
                if (NavMesh.SamplePosition(belief.Position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    destination = hit.position;
                }
                else if (NavMesh.SamplePosition(belief.Position, out hit, 15.0f, NavMesh.AllAreas))
                {
                    destination = hit.position;
                }

                if (agent.isOnNavMesh)
                {
                    agent.SetDestination(destination);
                }
            }

            // Edge case: partial NavMesh path — go to reachable endpoint.
            // Check agent.pathStatus == PathPartial, allow movement to partial destination.
            // Only fail if path is completely invalid or agent is off NavMesh.
            if (!agent.isOnNavMesh || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // Check if navigation has reached arrival destination (complete or partial endpoint)
            if (!agent.pathPending)
            {
                float stoppingThreshold = Mathf.Max(agent.stoppingDistance, arrivalDistance);

                if (agent.remainingDistance <= stoppingThreshold)
                {
                    // Target position or partial reachable endpoint reached successfully
                    currentTargetPosition = Vector3.positiveInfinity;
                    LastStatus = BtStatus.Success;
                    return BtStatus.Success;
                }
            }

            // Still traveling towards belief location
            LastStatus = BtStatus.Running;
            return BtStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            currentTargetPosition = Vector3.positiveInfinity;

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                agent.ResetPath();
            }
        }
    }
}
