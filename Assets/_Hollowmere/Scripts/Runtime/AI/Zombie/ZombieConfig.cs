using UnityEngine;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// All tuning numbers for the Runner Zombie in one place.
    /// Assign the SO_ZombieConfig asset to ZombieBody in the Inspector.
    /// Change values here during playtesting — never hardcode them in logic scripts.
    /// </summary>
    [CreateAssetMenu(menuName = "Hollowmere/AI/ZombieConfig", fileName = "SO_ZombieConfig")]
    public class ZombieConfig : ScriptableObject
    {
        // ─────────────────────────────────────────────────────────────────────────
        // SIGHT
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Sight")]

        // 14 m: village alleys feel claustrophobic without being unfair
        [Tooltip("Maximum distance in metres at which a zombie can see the player. " +
                 "14 m chosen so the flooded village alleys feel claustrophobic without being unfair.")]
        public float viewRange = 14f;

        [Tooltip("Half-angle of the view cone in degrees. 70° gives a human-like forward field; " +
                 "peripheral vision is handled by hearing instead.")]
        public float viewAngle = 70f;

        [Tooltip("Base rate at which the detection meter fills per second at point-blank range " +
                 "under full light (flashlight on). Multiplied by lightFactor, distanceFactor, " +
                 "and postureFactor each sight tick. " +
                 "At 0.35/s the player has ~3 s to leave the cone before detection.")]
        public float detectionBaseRate = 0.35f;

        [Tooltip("How many times per second the sight check runs. " +
                 "4 Hz keeps it well under the 2 ms AI budget. " +
                 "Movement and animation still run every frame.")]
        public float sightTickRate = 4f;

        // ─────────────────────────────────────────────────────────────────────────
        // HEARING
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Hearing")]

        [Tooltip("Multiplier applied to every incoming noise's loudness. " +
                 "1.0 = normal ears (matches the NoiseSystem table in Docs/AI). " +
                 "The creature reuses this brain with 1.6 here.")]
        [Range(0f, 2f)]
        public float hearingAcuity = 1.0f;

        // ─────────────────────────────────────────────────────────────────────────
        // MEMORY / BELIEF DECAY
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Memory")]

        // At tau=12, c=0.37 after 12 s, c<0.1 after 28 s — zombie searches ~24 s then gives up
        [Tooltip("Exponential decay time constant τ (tau) in seconds. " +
                 "confidence = c₀ × exp(−t / tau). " +
                 "At tau = 12 s:  after 12 s → 37 %,  after 24 s → 14 %,  after 28 s < 10 %. " +
                 "This means the zombie searches for roughly 24–28 seconds then gives up — " +
                 "long enough to be scary, short enough to let the player escape.")]
        [Min(1f)]
        public float beliefTau = 12f;

        [Tooltip("Confidence below this threshold means the zombie has 'forgotten'. " +
                 "It clears the belief and returns to Dormant. " +
                 "0.1 = 10 % certainty — keeping it above 0 prevents a zombie from " +
                 "wandering forever on stale information.")]
        [Range(0.01f, 0.3f)]
        public float minConfidence = 0.1f;

        // ─────────────────────────────────────────────────────────────────────────
        // STARTING CONFIDENCE PER BELIEF SOURCE
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Belief Confidence — Starting Values")]

        [Tooltip("Confidence when the zombie directly sees the player. " +
                 "1.0 = absolute certainty; this triggers Chase immediately.")]
        [Range(0.5f, 1f)]
        public float confidenceSeen = 1.0f;

        [Tooltip("Confidence when the zombie hears the player (footstep, gunshot, etc.). " +
                 "0.6 means it goes to Investigate, not Chase.")]
        [Range(0.1f, 0.9f)]
        public float confidenceHeard = 0.6f;

        [Tooltip("Confidence when another zombie's groan tells this one where the player might be. " +
                 "0.4 is lower than heard because the groan carries second-hand information.")]
        [Range(0.1f, 0.7f)]
        public float confidenceTold = 0.4f;

        // ─────────────────────────────────────────────────────────────────────────
        // BEHAVIOUR TREE THRESHOLDS
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Behaviour Tree — Thresholds")]

        // 0.8 requires recent strong sighting — below 0.7 = chases weak sounds; above 0.9 = never chases heard noises
        [Tooltip("Minimum belief confidence required to enter the Chase node. " +
                 "0.8 means the zombie must have seen the player recently (c₀ = 1.0 decays " +
                 "to 0.8 after about 2.7 s at tau = 12). " +
                 "Below this, it Investigates instead. " +
                 "If raised above 0.9: zombie barely ever chases heard noises. " +
                 "If lowered below 0.6: zombie chases weak, stale sounds — feels unfair.")]
        [Range(0.5f, 1f)]
        public float chaseThreshold = 0.8f;

        [Tooltip("Distance in metres at which the zombie can melee-attack the player. " +
                 "1.8 m ≈ arm's reach for a zombie-height character on the NavMesh.")]
        [Min(0.5f)]
        public float attackRange = 1.8f;

        [Tooltip("Cooldown in seconds between melee attacks. " +
                 "Prevents the zombie from spamming hits when the player stands next to it.")]
        [Min(0.1f)]
        public float attackCooldown = 1.5f;

        // ─────────────────────────────────────────────────────────────────────────
        // INVESTIGATE / SEARCH
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Investigate — Ring Search")]

        [Tooltip("Number of waypoints visited during the ring search after arriving at the belief position. " +
                 "3 gives a thorough sweep without being too slow.")]
        [Range(1, 6)]
        public int searchPoints = 3;

        [Tooltip("Radius of the first search ring around the belief position, in metres.")]
        [Min(1f)]
        public float searchRingStart = 3f;

        [Tooltip("How much the ring radius grows with each additional search point.")]
        [Min(0.5f)]
        public float searchRingStep = 2f;

        [Tooltip("How many seconds the zombie looks around at each search waypoint " +
                 "before moving to the next.")]
        [Min(0.5f)]
        public float searchLookTime = 1.2f;

        // ─────────────────────────────────────────────────────────────────────────
        // GROAN (ALERT MECHANIC)
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Groan — Emergent Horde Alert")]

        [Tooltip("Loudness of the zombie's groan in metres of walking distance. " +
                 "Must match Loudness.Groan in Noise.cs (20 m). " +
                 "Do not change this without also updating the shared constant.")]
        public float groanLoudness = 20f;

        // ─────────────────────────────────────────────────────────────────────────
        // PERFORMANCE
        // ─────────────────────────────────────────────────────────────────────────

        [Header("Performance")]

        [Tooltip("How many times per second ZombieBrain.Tick is called via AiScheduler. " +
                 "8 Hz gives responsive behaviour while keeping all AI under 2 ms/frame.")]
        [Range(5f, 20f)]
        public float brainTickRate = 8f;

        [Tooltip("How many seconds without moving (position delta < 0.1 m) before the zombie is " +
                 "considered stuck. On stuck: belief is cleared and state returns to Dormant.")]
        [Min(1f)]
        public float stuckTimeout = 3f;
    }
}
