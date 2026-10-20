using UnityEngine;

namespace Hollowmere.Combat
{
    /// <summary>
    /// Implemented by any entity that can receive weapon or environmental damage.
    /// Used by players, zombies, and physical destructibles.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>Current normalized health (0.0 = dead, 1.0 = full).</summary>
        float Health01 { get; }

        /// <summary>True if health has reached zero or the entity is dead.</summary>
        bool IsDead { get; }

        /// <summary>
        /// Applies damage to this entity.
        /// </summary>
        /// <param name="amount">Amount of damage to inflict.</param>
        /// <param name="hitPoint">World space location of the impact.</param>
        /// <param name="hitDirection">Direction the attack came from.</param>
        /// <param name="attacker">GameObject that originated the damage.</param>
        void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection, GameObject attacker = null);
    }
}
