using UnityEngine;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// The sensory origin of an agent's belief.
    /// Used by the decision tree and groan propagation logic.
    /// </summary>
    public enum BeliefSource
    {
        None = 0,

        /// <summary>Direct line-of-sight visual detection (base confidence ~1.0).</summary>
        Seen = 1,

        /// <summary>Acoustic detection via path-distance sound propagation (base confidence ~0.6).</summary>
        Heard = 2,

        /// <summary>Groan received from a neighbouring zombie alert (base confidence ~0.4).</summary>
        Told = 3
    }

    /// <summary>
    /// Represents the runner zombie's mental model of target location and certainty.
    /// Implemented as a readonly struct to guarantee zero heap allocations per tick.
    /// The zombie never inspects the player's true transform; all decisions use this belief.
    /// </summary>
    public readonly struct ZombieBelief
    {
        /// <summary>Estimated world position where the target was detected.</summary>
        public readonly Vector3 Position;

        /// <summary>Current confidence level ranging from 0.0 (forgotten/unknown) to 1.0 (certain).</summary>
        public readonly float Confidence;

        /// <summary>Initial un-decayed confidence c0 when this belief was established.</summary>
        public readonly float InitialConfidence;

        /// <summary>Timestamp (in seconds) when this belief was created or last refreshed.</summary>
        public readonly float Timestamp;

        /// <summary>Sensory channel through which this belief was established.</summary>
        public readonly BeliefSource Source;

        /// <summary>True if this belief contains valid sensory data and positive confidence.</summary>
        public bool IsValid => Confidence > 0f && Source != BeliefSource.None;

        /// <summary>Empty default belief representing an unaware zombie.</summary>
        public static ZombieBelief Empty => new ZombieBelief(Vector3.zero, 0f, 0f, BeliefSource.None, 0f);

        public ZombieBelief(Vector3 position, float confidence, float timestamp, BeliefSource source, float initialConfidence = -1f)
        {
            Position = position;
            Confidence = Mathf.Clamp01(confidence);
            InitialConfidence = initialConfidence >= 0f ? Mathf.Clamp01(initialConfidence) : Confidence;
            Timestamp = timestamp;
            Source = source;
        }

        public override string ToString()
        {
            return $"ZombieBelief(Pos: {Position}, Conf: {Confidence:F2} (c0: {InitialConfidence:F2}), Src: {Source}, Time: {Timestamp:F1}s)";
        }
    }
}
