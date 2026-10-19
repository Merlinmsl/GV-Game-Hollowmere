using System;
using UnityEngine;
using UnityEngine.AI;
using Hollowmere.AI.Zombie.BehaviourTree;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Pure C# decision brain for the Runner Zombie.
    /// Completely decoupled from MonoBehaviour, visual components, and Unity scene lifecycles.
    /// Owns the Behaviour Tree root and ZombieMemory store.
    /// Evaluated at N Hz (configured via brainTickRate) with zero managed heap allocations per tick.
    /// </summary>
    public class ZombieBrain
    {
        private readonly ZombieConfig config;
        private readonly ZombieMemory memory;
        private readonly BtSelector rootNode;

        // Leaf action nodes
        private readonly AttackNode attackNode;
        private readonly ChaseNode chaseNode;
        private readonly InvestigateNode investigateNode;
        private readonly DormantNode dormantNode;

        private BtNode lastActiveNode;

        /// <summary>Access to the brain's mental memory store.</summary>
        public ZombieMemory Memory => memory;

        /// <summary>Access to the root Behaviour Tree node.</summary>
        public BtNode RootNode => rootNode;

        /// <summary>Active leaf node evaluated on the most recent tick.</summary>
        public BtNode ActiveNode => rootNode != null ? rootNode.GetActiveNode() : null;

        /// <summary>Fired when the active behaviour state transitions.</summary>
        public event Action<string> OnStateChanged;

        /// <summary>Fired when an attack action is triggered.</summary>
        public event Action OnAttack;

        /// <summary>
        /// Constructs the zombie behaviour tree and wires sensory memory.
        /// </summary>
        /// <param name="config">Zombie tuning configuration ScriptableObject.</param>
        /// <param name="agent">NavMeshAgent reference for locomotion.</param>
        /// <param name="bodyTransform">Transform reference for body orientation.</param>
        public ZombieBrain(ZombieConfig config, NavMeshAgent agent = null, Transform bodyTransform = null)
        {
            this.config = config;
            this.memory = new ZombieMemory(config);

            if (bodyTransform != null)
            {
                memory.SetPositionProvider(() => bodyTransform.position);
            }

            // Construct leaf nodes
            attackNode = new AttackNode(memory, bodyTransform, config);
            attackNode.OnAttack += () => OnAttack?.Invoke();

            chaseNode = new ChaseNode(memory, agent, config);
            investigateNode = new InvestigateNode(memory, agent);
            dormantNode = new DormantNode(memory, bodyTransform);

            // Construct Root Priority Selector
            // Evaluated top to bottom: first branch returning Running or Success wins
            // 1. Attack          — target within melee range
            // 2. Chase           — belief confidence >= 0.8
            // 3. Investigate     — active belief exists (0.1 <= conf < 0.8)
            // 4. Dormant         — idle baseline listening stance
            rootNode = new BtSelector("ZombieRootSelector", attackNode, chaseNode, investigateNode, dormantNode);
        }

        /// <summary>
        /// Binds physical locomotion and orientation components from ZombieBody.
        /// </summary>
        public void BindBodyComponents(NavMeshAgent agent, Transform bodyTransform)
        {
            if (bodyTransform != null)
            {
                memory.SetPositionProvider(() => bodyTransform.position);
                attackNode.SetZombieTransform(bodyTransform);
                dormantNode.SetBodyTransform(bodyTransform);
            }

            if (agent != null)
            {
                chaseNode.SetNavMeshAgent(agent);
                investigateNode.SetNavMeshAgent(agent);
            }
        }

        /// <summary>
        /// Executes one AI decision tick of the Behaviour Tree.
        /// Zero heap allocations guaranteed.
        /// </summary>
        public BtStatus Tick()
        {
            BtStatus status = rootNode.Tick();

            BtNode currentActive = ActiveNode;
            if (currentActive != lastActiveNode)
            {
                lastActiveNode = currentActive;
                if (currentActive != null)
                {
                    OnStateChanged?.Invoke(currentActive.Name);
                }
            }

            return status;
        }

        /// <summary>
        /// Resets behaviour tree state.
        /// </summary>
        public void Reset()
        {
            rootNode.Reset();
            lastActiveNode = null;
        }
    }
}
