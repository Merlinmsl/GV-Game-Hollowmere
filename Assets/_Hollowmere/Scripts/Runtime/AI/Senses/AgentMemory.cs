using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>
    /// An agent's memory of where the player is. Keeps the single most convincing belief:
    /// a new clue replaces the old one only if it is at least as convincing as the old one is now.
    /// </summary>
    public sealed class AgentMemory
    {
        public const float SeenConfidence = 1f;
        public const float HeardConfidence = 0.6f;
        public const float ToldConfidence = 0.4f;

        readonly float tau;          // seconds for confidence to fall to 37 %
        readonly float forgetBelow;  // below this the agent gives up

        Belief best;
        bool hasBelief;

        public AgentMemory(float tau = 8f, float forgetBelow = 0.1f)
        {
            this.tau = tau;
            this.forgetBelow = forgetBelow;
        }

        public Belief Current => best;

        public float ConfidenceAt(float now) => hasBelief ? best.ConfidenceAt(now, tau) : 0f;

        public bool HasBelief(float now) => ConfidenceAt(now) >= forgetBelow;

        public void Remember(Vector3 position, float confidence, BeliefSource source, float now)
        {
            if (!hasBelief || confidence >= ConfidenceAt(now))
            {
                best = new Belief(position, confidence, source, now);
                hasBelief = true;
            }
        }

        public void Forget() => hasBelief = false;
    }
}
