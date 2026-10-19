using UnityEngine;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Manages the mental state and target belief of a single Runner Zombie.
    /// Pure C# class decoupled from MonoBehaviour, designed to be unit-testable.
    /// In Day 1 skeleton: stores the current belief and provides UpdateBelief / GetBelief accessors.
    /// Exponential decay and INoiseListener sensory inputs are scheduled for subsequent iterations.
    /// </summary>
    public class ZombieMemory
    {
        private ZombieBelief currentBelief = ZombieBelief.Empty;

        /// <summary>
        /// Gets the current belief held in memory.
        /// </summary>
        public ZombieBelief CurrentBelief => currentBelief;

        /// <summary>
        /// True if the zombie currently possesses an active belief above zero confidence.
        /// </summary>
        public bool HasBelief => currentBelief.IsValid;

        public ZombieMemory()
        {
            currentBelief = ZombieBelief.Empty;
        }

        /// <summary>
        /// Retrieves the zombie's current belief.
        /// </summary>
        public ZombieBelief GetBelief()
        {
            return currentBelief;
        }

        /// <summary>
        /// Updates the current belief with a new target location, confidence, and source.
        /// </summary>
        /// <param name="position">World-space position where the target was sensed.</param>
        /// <param name="confidence">Confidence level (0 to 1).</param>
        /// <param name="source">Sensory modality (Seen, Heard, Told).</param>
        public void UpdateBelief(Vector3 position, float confidence, BeliefSource source)
        {
            float timestamp = Time.time;
            UpdateBelief(position, confidence, source, timestamp);
        }

        /// <summary>
        /// Overload supporting explicit timestamp for deterministic unit testing.
        /// </summary>
        public void UpdateBelief(Vector3 position, float confidence, BeliefSource source, float timestamp)
        {
            currentBelief = new ZombieBelief(position, confidence, timestamp, source);
        }

        /// <summary>
        /// Directly replaces the current belief with an existing belief instance.
        /// </summary>
        public void UpdateBelief(in ZombieBelief newBelief)
        {
            currentBelief = newBelief;
        }

        /// <summary>
        /// Clears the memory, resetting belief to Empty (e.g. after search exhausts or target lost).
        /// </summary>
        public void ClearBelief()
        {
            currentBelief = ZombieBelief.Empty;
        }
    }
}
