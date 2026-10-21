using UnityEngine;

namespace Hollowmere.AI.Senses
{
    public enum BeliefSource { Seen, Heard, Told }

    /// <summary>
    /// "I think the player is here", with how sure the agent was when it learned it.
    /// Confidence fades exponentially: c(t) = c0 x e^(-(t - t0) / tau).
    /// </summary>
    public readonly struct Belief
    {
        public readonly Vector3 Position;
        public readonly float Confidence;     // how sure, at the moment it was learned (0 to 1)
        public readonly BeliefSource Source;
        public readonly float Time;           // when it was learned, in game seconds

        public Belief(Vector3 position, float confidence, BeliefSource source, float time)
        {
            Position = position;
            Confidence = confidence;
            Source = source;
            Time = time;
        }

        /// <summary>How sure the agent still is at time 'now'.</summary>
        public float ConfidenceAt(float now, float tau)
        {
            return Confidence * Mathf.Exp(-(now - Time) / tau);
        }
    }
}
