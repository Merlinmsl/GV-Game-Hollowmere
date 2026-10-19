using System;
using UnityEngine;
using Hollowmere.AI.Senses;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Manages the mental state, sensory input, and target belief of a single Runner Zombie.
    /// Implements INoiseListener to receive path-distance acoustic events from NoiseSystem.
    /// Manages exponential confidence decay over time: confidence = c0 * exp(-elapsed / tau).
    /// Stores an independent groanBelief for emergent horde coordination.
    /// Pure C# class decoupled from MonoBehaviour, designed for deterministic unit testing.
    /// </summary>
    public class ZombieMemory : INoiseListener
    {
        private readonly ZombieConfig config;
        private ZombieBelief currentBelief = ZombieBelief.Empty;
        private ZombieBelief _groanBelief = ZombieBelief.Empty;
        private float initialConfidence = 0f;

        private Func<Vector3> earPositionProvider;
        private Vector3 staticEarPosition = Vector3.zero;
        private float hearingAcuityOverride = -1f;

        private const float FallbackTau = 12.0f;
        private const float FallbackMinConfidence = 0.1f;

        // ─────────────────────────────────────────────────────────────────────────
        // CONFIGURATION PROPERTIES
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Exponential decay time constant tau in seconds (default 12s from ZombieConfig).
        /// </summary>
        public float BeliefTau => (config != null && config.beliefTau > 0.001f) ? config.beliefTau : FallbackTau;

        /// <summary>
        /// Minimum confidence threshold before giving up search and clearing belief (default 0.1).
        /// </summary>
        public float MinConfidence => config != null ? config.minConfidence : FallbackMinConfidence;

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
        /// Gets the current primary belief held in memory.
        /// </summary>
        public ZombieBelief CurrentBelief => currentBelief;

        /// <summary>
        /// True if the zombie currently possesses an active primary belief above zero confidence.
        /// </summary>
        public bool HasBelief => currentBelief.IsValid;

        /// <summary>
        /// Separate belief stored when a groan alert is received from a neighbouring zombie.
        /// Used by RespondToGroanNode to converge the horde without group-coordination code.
        /// </summary>
        public ZombieBelief GroanBelief => _groanBelief;

        /// <summary>
        /// True if an active groan belief exists above zero confidence.
        /// </summary>
        public bool HasGroanBelief => _groanBelief.IsValid;

        public ZombieMemory(ZombieConfig config = null)
        {
            this.config = config;
            currentBelief = ZombieBelief.Empty;
            _groanBelief = ZombieBelief.Empty;
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
        // MEMORY DECAY EVALUATION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fades belief confidence exponentially based on elapsed time:
        /// confidence = c0 * exp(-elapsed / tau).
        /// Clears belief to Empty when confidence drops below MinConfidence (0.1).
        /// Zero heap allocations during execution.
        /// </summary>
        public void Tick()
        {
            Tick(Time.time);
        }

        /// <summary>
        /// Explicit timestamp overload of Tick for deterministic offline unit testing.
        /// </summary>
        /// <param name="currentTime">Current timestamp in seconds.</param>
        public void Tick(float currentTime)
        {
            float tau = BeliefTau;

            // 1. Decay primary belief
            if (currentBelief.IsValid)
            {
                float elapsed = Mathf.Max(0f, currentTime - currentBelief.Timestamp);
                float c0 = initialConfidence > 0f ? initialConfidence : currentBelief.InitialConfidence;
                float decayedConfidence = c0 * Mathf.Exp(-elapsed / tau);

                if (decayedConfidence < MinConfidence)
                {
                    ClearBelief();
                }
                else
                {
                    currentBelief = new ZombieBelief(
                        currentBelief.Position,
                        decayedConfidence,
                        currentBelief.Timestamp,
                        currentBelief.Source,
                        c0
                    );
                }
            }

            // 2. Decay separate groan belief
            if (_groanBelief.IsValid)
            {
                float groanElapsed = Mathf.Max(0f, currentTime - _groanBelief.Timestamp);
                float c0Groan = _groanBelief.InitialConfidence > 0f ? _groanBelief.InitialConfidence : _groanBelief.Confidence;
                float decayedGroanConfidence = c0Groan * Mathf.Exp(-groanElapsed / tau);

                if (decayedGroanConfidence < MinConfidence)
                {
                    ClearGroanBelief();
                }
                else
                {
                    _groanBelief = new ZombieBelief(
                        _groanBelief.Position,
                        decayedGroanConfidence,
                        _groanBelief.Timestamp,
                        _groanBelief.Source,
                        c0Groan
                    );
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SENSORY RECEPTION
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps an acoustic noise type to an initial belief confidence level.
        /// </summary>
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
        public void OnHeard(in Noise noise, float pathDistance)
        {
            LastHeardNoise = noise;
            LastHeardDistance = pathDistance;
            HasHeardNoise = true;

            float incomingConfidence = ConfidenceFromKind(noise.Kind);
            BeliefSource source = noise.Kind == NoiseKind.Groan ? BeliefSource.Told : BeliefSource.Heard;

            // Separate groan belief tracking for horde convergence
            if (noise.Kind == NoiseKind.Groan)
            {
                if (incomingConfidence > _groanBelief.Confidence)
                {
                    _groanBelief = new ZombieBelief(noise.Position, incomingConfidence, Time.time, BeliefSource.Told, incomingConfidence);
                }
            }

            // Update primary belief if higher confidence
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
            initialConfidence = Mathf.Clamp01(confidence);
            currentBelief = new ZombieBelief(position, initialConfidence, timestamp, source, initialConfidence);
        }

        /// <summary>
        /// Sets groan belief with explicit timestamp for testing.
        /// </summary>
        public void SetGroanBelief(Vector3 position, float confidence, float timestamp)
        {
            float conf = Mathf.Clamp01(confidence);
            _groanBelief = new ZombieBelief(position, conf, timestamp, BeliefSource.Told, conf);
        }

        /// <summary>
        /// Directly replaces the current belief with an existing belief instance.
        /// </summary>
        public void UpdateBelief(in ZombieBelief newBelief)
        {
            initialConfidence = newBelief.InitialConfidence > 0f ? newBelief.InitialConfidence : newBelief.Confidence;
            currentBelief = newBelief;
        }

        /// <summary>
        /// Clears the primary memory, resetting belief to Empty.
        /// </summary>
        public void ClearBelief()
        {
            initialConfidence = 0f;
            currentBelief = ZombieBelief.Empty;
        }

        /// <summary>
        /// Clears the groan belief once the zombie responds or arrives at the groan location.
        /// </summary>
        public void ClearGroanBelief()
        {
            _groanBelief = ZombieBelief.Empty;
        }
    }
}
