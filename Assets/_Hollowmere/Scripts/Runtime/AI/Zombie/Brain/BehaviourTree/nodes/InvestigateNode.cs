using System;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree action node for investigating a suspected noise or target location.
    /// Drives the NavMeshAgent toward belief.Position, then executes a widening ring search
    /// visiting 2-3 surrounding points before giving up and clearing belief.
    /// Uses Humanoid NavMeshQueryFilter (agentTypeID = 0) to avoid dog mesh sampling bugs.
    /// Returns:
    /// - Running while the agent is navigating or looking around at ring search points.
    /// - Success once the investigation and ring search are completed.
    /// - Failure if there is no active belief or the path is invalid.
    /// </summary>
    public class InvestigateNode : BtNode
    {
        private enum SearchPhase
        {
            MovingToOrigin,
            LookingAround,
            MovingToSearchPoint,
            Completed
        }

        private static readonly NavMeshQueryFilter HumanoidFilter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };

        private readonly ZombieMemory memory;
        private readonly ZombieConfig config;
        private NavMeshAgent agent;
        private readonly float arrivalDistance;

        private Vector3 currentInvestigationOrigin = Vector3.positiveInfinity;
        private SearchPhase phase = SearchPhase.MovingToOrigin;
        private int currentSearchPointIndex = 0;
        private float lookWaitTimer = 0f;
        private float ringAngleOffset = 0f;

        /// <summary>
        /// Creates an InvestigateNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store.</param>
        /// <param name="agent">NavMeshAgent driving physical locomotion.</param>
        /// <param name="config">Zombie tuning ScriptableObject asset.</param>
        /// <param name="arrivalDistance">Threshold in metres to consider destination reached.</param>
        public InvestigateNode(ZombieMemory memory, NavMeshAgent agent = null, ZombieConfig config = null, float arrivalDistance = 1.5f) 
            : base("Investigate")
        {
            this.memory = memory;
            this.agent = agent;
            this.config = config;
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
        /// Evaluates investigation movement and widening ring search:
        /// 1. Reads current belief; fails if missing or confidence is 0.
        /// 2. If origin changed, resets search to investigate initial spot.
        /// 3. Upon reaching origin, visits searchPoints (default 3) on a widening ring.
        /// 4. After completing ring search, clears belief and succeeds (returning to Dormant).
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
                ResetSearchState();
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // If a new belief origin arrived, restart search towards the new origin
            if ((belief.Position - currentInvestigationOrigin).sqrMagnitude > 0.5f)
            {
                currentInvestigationOrigin = belief.Position;
                phase = SearchPhase.MovingToOrigin;
                currentSearchPointIndex = 0;
                lookWaitTimer = 0f;
                ringAngleOffset = UnityEngine.Random.Range(0f, 360f);

                Vector3 destination = belief.Position;
                if (NavMesh.SamplePosition(belief.Position, out NavMeshHit hit, 5.0f, HumanoidFilter) ||
                    NavMesh.SamplePosition(belief.Position, out hit, 15.0f, HumanoidFilter))
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

            float stoppingThreshold = Mathf.Max(agent.stoppingDistance, arrivalDistance);

            switch (phase)
            {
                case SearchPhase.MovingToOrigin:
                    if (!agent.pathPending && agent.remainingDistance <= stoppingThreshold)
                    {
                        phase = SearchPhase.LookingAround;
                        lookWaitTimer = 0f;
                    }
                    LastStatus = BtStatus.Running;
                    return BtStatus.Running;

                case SearchPhase.LookingAround:
                    lookWaitTimer += Time.deltaTime;
                    float lookTime = (config != null && config.searchLookTime > 0f) ? config.searchLookTime : 1.2f;

                    if (lookWaitTimer >= lookTime)
                    {
                        lookWaitTimer = 0f;
                        int totalPoints = (config != null && config.searchPoints > 0) ? config.searchPoints : 3;

                        if (currentSearchPointIndex >= totalPoints)
                        {
                            // Ring search completed without finding anything; clear belief and give up
                            phase = SearchPhase.Completed;
                            memory.ClearBelief();
                            ResetSearchState();
                            LastStatus = BtStatus.Success;
                            return BtStatus.Success;
                        }

                        // Calculate next waypoint on a widening ring
                        float ringStart = (config != null && config.searchRingStart > 0f) ? config.searchRingStart : 3.0f;
                        float ringStep = (config != null && config.searchRingStep > 0f) ? config.searchRingStep : 2.0f;
                        float radius = ringStart + (currentSearchPointIndex * ringStep);
                        float angle = ringAngleOffset + (currentSearchPointIndex * (360f / totalPoints));

                        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * radius);
                        Vector3 candidatePos = currentInvestigationOrigin + offset;

                        if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, radius + 2.0f, HumanoidFilter))
                        {
                            candidatePos = hit.position;
                        }

                        if (agent.isOnNavMesh)
                        {
                            agent.SetDestination(candidatePos);
                        }

                        currentSearchPointIndex++;
                        phase = SearchPhase.MovingToSearchPoint;
                    }

                    LastStatus = BtStatus.Running;
                    return BtStatus.Running;

                case SearchPhase.MovingToSearchPoint:
                    if (!agent.pathPending && agent.remainingDistance <= stoppingThreshold)
                    {
                        phase = SearchPhase.LookingAround;
                        lookWaitTimer = 0f;
                    }
                    LastStatus = BtStatus.Running;
                    return BtStatus.Running;

                case SearchPhase.Completed:
                default:
                    ResetSearchState();
                    LastStatus = BtStatus.Success;
                    return BtStatus.Success;
            }
        }

        private void ResetSearchState()
        {
            currentInvestigationOrigin = Vector3.positiveInfinity;
            phase = SearchPhase.MovingToOrigin;
            currentSearchPointIndex = 0;
            lookWaitTimer = 0f;
        }

        public override void Reset()
        {
            base.Reset();
            ResetSearchState();

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                agent.ResetPath();
            }
        }
    }
}
