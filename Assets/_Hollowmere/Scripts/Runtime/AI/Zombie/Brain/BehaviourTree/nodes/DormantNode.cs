using UnityEngine;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree leaf node representing the dormant/idle stance.
    /// The zombie stands still, listens for noises, and keeps a narrow forward view cone.
    /// Always returns Running so it acts as the fallback baseline leaf of the root Selector.
    /// If memory contains a belief with confidence > 0, it subtly turns toward that direction.
    /// </summary>
    public class DormantNode : BtNode
    {
        private readonly ZombieMemory memory;
        private Transform bodyTransform;
        private readonly float turnSpeed;

        /// <summary>
        /// Creates a DormantNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store.</param>
        /// <param name="bodyTransform">Optional reference to the zombie GameObject transform for orientation.</param>
        /// <param name="turnSpeed">Speed of horizontal slerp rotation towards sound cues (rad/sec or factor).</param>
        public DormantNode(ZombieMemory memory, Transform bodyTransform = null, float turnSpeed = 2.0f) : base("Dormant")
        {
            this.memory = memory;
            this.bodyTransform = bodyTransform;
            this.turnSpeed = turnSpeed;
        }

        /// <summary>
        /// Binds or updates the body transform for rotation.
        /// </summary>
        public void SetBodyTransform(Transform transform)
        {
            bodyTransform = transform;
        }

        /// <summary>
        /// Evaluates dormant logic:
        /// - Checks if an acoustic or sensory belief exists (confidence > 0).
        /// - Slightly rotates the body toward the belief position without moving forward.
        /// - Always returns BtStatus.Running.
        /// Zero allocations during execution.
        /// </summary>
        public override BtStatus Tick()
        {
            if (memory != null && bodyTransform != null)
            {
                ZombieBelief belief = memory.GetBelief();

                if (belief.Confidence > 0f)
                {
                    Vector3 direction = belief.Position - bodyTransform.position;
                    direction.y = 0f; // Keep rotation horizontal on the ground plane

                    if (direction.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(direction);
                        bodyTransform.rotation = Quaternion.Slerp(
                            bodyTransform.rotation,
                            targetRotation,
                            turnSpeed * Time.deltaTime
                        );
                    }
                }
            }

            LastStatus = BtStatus.Running;
            return BtStatus.Running;
        }
    }
}
