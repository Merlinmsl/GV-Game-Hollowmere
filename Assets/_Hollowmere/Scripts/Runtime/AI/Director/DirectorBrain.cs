using UnityEngine;

namespace Hollowmere.AI.Director
{
    public enum DirectorAction { HoldBack, WakeDead, StageNoise }

    /// <summary>Everything the brain needs to decide, gathered by the GameDirector each tick.</summary>
    public struct DirectorSituation
    {
        public float Stress;
        public float Target;
        public float Quietness;              // from the player profile: 1 = stealthy, 0 = loud
        public float SecondsSinceLastEvent;  // since the Director last woke the dead or staged a noise
        public int DormantCount;
        public int AwakeCount;
        public int MaxAwake;
        public float BestCandidate;          // score of the best dormant enemy to use, 0 to 1
    }

    /// <summary>
    /// Utility AI: scores each action from 0 to 1 and picks the highest.
    /// Each score is a PRODUCT of considerations, so any single "no" vetoes the action
    /// (no dead left, too many awake, no good place, the player is already tense enough).
    ///
    ///   need      = clamp01((target - stress) / 0.4)    how much more tension is wanted
    ///   excess    = clamp01((stress - target) / 0.3)    how far stress is above the target
    ///   rested    = clamp01((secondsSinceLastEvent - MinGap) / (RestSeconds - MinGap))
    ///               0 for the first 5 s after an event (a hard gap), rising to 1 at 15 s
    ///   available = 1 if a dormant enemy exists and fewer than MaxAwake are awake, else 0
    ///   room      = 1 - awake / maxAwake
    ///
    ///   WakeDead   = need x rested x available x room x best x (1 - 0.5 x quietness)
    ///   StageNoise = need x rested x available x best x (0.3 + 0.7 x quietness)
    ///   HoldBack   = 0.1 + 0.9 x excess    (a small baseline: doing nothing wins when nothing else scores)
    ///
    /// Plain C# with no Unity objects, so it can be tested on its own.
    /// </summary>
    public sealed class DirectorBrain
    {
        public const float MinGap = 5f;
        public const float RestSeconds = 15f;

        readonly float[] scores = new float[3];

        public float Score(DirectorAction action) => scores[(int)action];

        public DirectorAction Decide(in DirectorSituation s)
        {
            float need = Mathf.Clamp01((s.Target - s.Stress) / 0.4f);
            float excess = Mathf.Clamp01((s.Stress - s.Target) / 0.3f);
            float rested = Mathf.Clamp01((s.SecondsSinceLastEvent - MinGap) / (RestSeconds - MinGap));
            float available = s.DormantCount > 0 && s.AwakeCount < s.MaxAwake ? 1f : 0f;
            float room = s.MaxAwake > 0 ? 1f - (float)s.AwakeCount / s.MaxAwake : 0f;

            scores[(int)DirectorAction.WakeDead] = need * rested * available * room * s.BestCandidate * (1f - 0.5f * s.Quietness);
            scores[(int)DirectorAction.StageNoise] = need * rested * available * s.BestCandidate * (0.3f + 0.7f * s.Quietness);
            scores[(int)DirectorAction.HoldBack] = 0.1f + 0.9f * excess;

            DirectorAction best = DirectorAction.HoldBack;
            for (int i = 1; i < scores.Length; i++)
                if (scores[i] > scores[(int)best]) best = (DirectorAction)i;
            return best;
        }

        /// <summary>How many to wake at once: 1 to 3, more when far below the target.</summary>
        public static int WakeCount(in DirectorSituation s)
        {
            float need = Mathf.Clamp01((s.Target - s.Stress) / 0.4f);
            return 1 + Mathf.Min(2, Mathf.FloorToInt(need * 3f));
        }
    }
}
