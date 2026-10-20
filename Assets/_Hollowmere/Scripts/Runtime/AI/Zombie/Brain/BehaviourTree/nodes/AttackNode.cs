using System;
using UnityEngine;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Behaviour tree action node representing melee attacks.
    /// Highest-priority actionable branch in the root Selector.
    /// Executes when target belief is within melee attackRange (1.8m) and line-of-sight is confirmed.
    /// - Fires the OnAttack event so ZombieBody / ZombieAnimator can trigger animations and damage.
    /// - Returns Running while the player remains within attack range.
    /// - Returns Failure if the player steps outside attackRange, handing control back to Selector
    ///   so it can immediately transition to ChaseNode.
    /// </summary>
    public class AttackNode : BtNode
    {
        private readonly ZombieMemory memory;
        private readonly ZombieConfig config;
        private Transform zombieTransform;
        private readonly float fallbackAttackRange;
        private readonly float attackInterval;
        private readonly Action onAttackCallback;

        private float nextAttackTime;

        /// <summary>
        /// Event fired when an attack swing/strike is initiated.
        /// Listened to by ZombieBody and animation controllers.
        /// </summary>
        public event Action OnAttack;

        /// <summary>
        /// Effective attack radius in metres.
        /// </summary>
        public float AttackRange => config != null ? config.attackRange : fallbackAttackRange;

        /// <summary>
        /// Creates an AttackNode.
        /// </summary>
        /// <param name="memory">The zombie's mental memory store.</param>
        /// <param name="zombieTransform">Transform of the zombie GameObject for distance and facing checks.</param>
        /// <param name="config">Zombie tuning configuration ScriptableObject.</param>
        /// <param name="fallbackAttackRange">Melee distance threshold if config is null (default 1.8m).</param>
        /// <param name="attackInterval">Minimum seconds between successive melee swings (default 1.2s).</param>
        /// <param name="onAttack">Optional direct callback delegate for attack execution.</param>
        public AttackNode(
            ZombieMemory memory, 
            Transform zombieTransform = null, 
            ZombieConfig config = null, 
            float fallbackAttackRange = 1.8f,
            float attackInterval = 1.2f,
            Action onAttack = null) 
            : base("Attack")
        {
            this.memory = memory;
            this.zombieTransform = zombieTransform;
            this.config = config;
            this.fallbackAttackRange = fallbackAttackRange;
            this.attackInterval = attackInterval;
            this.onAttackCallback = onAttack;
        }

        /// <summary>
        /// Binds the zombie body transform.
        /// </summary>
        public void SetZombieTransform(Transform transform)
        {
            zombieTransform = transform;
        }

        /// <summary>
        /// Evaluates melee attack conditions:
        /// - Fails if belief is missing or distance > attackRange.
        /// - Rotates toward target and fires OnAttack if in range.
        /// - Returns Running while in range.
        /// Zero allocations during execution.
        /// </summary>
        public override BtStatus Tick()
        {
            if (memory == null)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            ZombieBelief belief = memory.GetBelief();

            if (!belief.IsValid)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            Vector3 currentPos = zombieTransform != null ? zombieTransform.position : Vector3.zero;
            Vector3 diff = belief.Position - currentPos;
            diff.y = 0f; // Evaluate ground distance

            float effectiveRange = AttackRange;

            // If player leaves attack range, return Failure to hand back to Selector (which drops to Chase)
            if (diff.sqrMagnitude > effectiveRange * effectiveRange)
            {
                LastStatus = BtStatus.Failure;
                return BtStatus.Failure;
            }

            // Face target directly while striking
            if (zombieTransform != null && diff.sqrMagnitude > 0.001f)
            {
                zombieTransform.rotation = Quaternion.LookRotation(diff);
            }

            // Trigger attack swing at interval rate
            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackInterval;
                OnAttack?.Invoke();
                onAttackCallback?.Invoke();
            }

            LastStatus = BtStatus.Running;
            return BtStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            // Don't reset nextAttackTime to prevent attack cooldown bypass on rapid transitions
        }
    }
}
