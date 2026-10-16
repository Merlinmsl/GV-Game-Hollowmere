using System;
using UnityEngine;
using Hollowmere.AI.Senses;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Manages the mental state, sensory input, and target belief of a single Runner Zombie.
    /// Implements INoiseListener to receive path-distance acoustic events from NoiseSystem.
    /// Pure C# class decoupled from MonoBehaviour, designed for deterministic unit testing.
    /// </summary>
    public class ZombieMemory : INoiseListener
    {
        private readonly ZombieConfig config;
        private ZombieBelief currentBelief = ZombieBelief.Empty;

        private Func<Vector3> earPositionProvider;
        private Vector3 staticEarPosition = Vector3.zero;
        private float hearingAcuityOverride = -1f;

        // ─────────────────────────────────────────────────────────────────────────
        // INoiseListener IMPLEMENTATION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Position at the listener's feet where sound arrives.
        /// Resolves via dynamic delegate (e.g. from body transform) or static position in unit tests.
        /// </summary>
        public Vector3 EarPosition => earPositionProvider != null ? earPositionProvider() : staticEarPosition;

        /// <summary>
        /// Acoustic acuity multiplier (1.0 = normal human hearing).
        /// Pulled from ZombieConfig or overridden directly.
        /// </summary>
        public float HearingAcuity
        {
            get => hearingAcuityOverride >= 0f ? hearingAcuityOverride : (config != null ? config.hearingAcuity : 1.0f);
            set => hearingAcuityOverride = value;
        }

        // Debug inspection properties (for F2 debug overlay)
        public Noise LastHeardNoise { get; private set; }
        public float LastHeardDistance { get; private set; }
        public bool HasHeardNoise { get; private set; }

        /// <summary>
        /// Gets the current belief held in memory.
        /// </summary>
        public ZombieBelief CurrentBelief => currentBelief;

        /// <summary>
        /// True if the zombie currently possesses an active belief above zero confidence.
        /// </summary>
        public bool HasBelief => currentBelief.IsValid;

        public ZombieMemory(ZombieConfig config = null)
        {
            this.config = config;
            currentBelief = ZombieBelief.Empty;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // NOISE SYSTEM REGISTRATION LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers this listener with the static NoiseSystem.
        /// Call from ZombieBody.OnEnable().
        /// </summary>
        public void OnEnable()
        {
            NoiseSystem.Register(this);
        }

        /// <summary>
        /// Unregisters this listener from the static NoiseSystem.
        /// Call from ZombieBody.OnDisable().
        /// </summary>
        public void OnDisable()
        {
            NoiseSystem.Unregister(this);
        }

        public void Enable() => OnEnable();
        public void Disable() => OnDisable();

        /// <summary>
        /// Binds dynamic ear position provider (e.g. () => transform.position) from MonoBehaviour.
        /// </summary>
        public void SetPositionProvider(Func<Vector3> provider)
        {
            earPositionProvider = provider;
        }

        /// <summary>
        /// Sets a fixed ear position for offline unit tests.
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            staticEarPosition = position;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SENSORY RECEPTION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps an acoustic noise type to an initial belief confidence level.
        /// Higher confidence indicates sharper, unmistakable sounds (e.g. gunshots).
        /// Lower confidence indicates secondary cues (e.g. footsteps or groans from other zombies).
        ///
        /// ┌─────────────────────┬────────────┬──────────────────────────────────────────┐
        /// │ Noise Kind          │ Confidence │ Rationale                                │
        /// ├─────────────────────┼────────────┼──────────────────────────────────────────┤
        /// │ Gunshot             │ 0.60       │ Loud, unmistakable weapon discharge      │
        /// │ IceGun              │ 0.58       │ Distinctive pneumatic cryo blast         │
        /// │ Footstep (Running)  │ 0.55       │ Rapid, direct physical movement          │
        /// │ Door                │ 0.50       │ Heavy physical structure manipulation    │
        /// │ Wading              │ 0.50       │ Distinctive splashing in village water   │
        /// │ Bark                │ 0.45       │ Animal sound (Biscuit), secondary alert  │
        /// │ Groan (Horde alert) │ 0.40       │ Neighbour zombie alert (BeliefSource.Told)│
        /// │ Staged / Default    │ 0.40       │ Ambient or unclassified sound cue        │
        /// └─────────────────────┴────────────┴──────────────────────────────────────────┘
        /// </summary>
        /// <param name="kind">The sensory noise category.</param>
        /// <returns>Initial confidence score between 0.0 and 1.0.</returns>
        public static float ConfidenceFromKind(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Gunshot:
                    return 0.60f;
                case NoiseKind.IceGun:
                    return 0.58f;
                case NoiseKind.Footstep:
                    return 0.55f;
                case NoiseKind.Door:
                    return 0.50f;
                case NoiseKind.Wading:
                    return 0.50f;
                case NoiseKind.Bark:
                    return 0.45f;
                case NoiseKind.Groan:
                    return 0.40f;
                case NoiseKind.Staged:
                default:
                    return 0.40f;
            }
        }

        /// <summary>
        /// Called by NoiseSystem when a sound reaches the zombie along the NavMesh walking path.
        /// </summary>
        /// <param name="noise">The emitted noise event.</param>
        /// <param name="pathDistance">Walking distance along the NavMesh in metres.</param>
        public void OnHeard(in Noise noise, float pathDistance)
        {
            LastHeardNoise = noise;
            LastHeardDistance = pathDistance;
            HasHeardNoise = true;

            float incomingConfidence = ConfidenceFromKind(noise.Kind);
            BeliefSource source = noise.Kind == NoiseKind.Groan ? BeliefSource.Told : BeliefSource.Heard;

            if (incomingConfidence > currentBelief.Confidence)
            {
                UpdateBelief(noise.Position, incomingConfidence, source);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // BELIEF MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────────

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
