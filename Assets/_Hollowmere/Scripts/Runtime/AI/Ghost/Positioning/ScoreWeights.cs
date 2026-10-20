using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// How much each consideration counts in a cell's position score. Every term is 0..1, so a weight is
    /// directly "how many points this consideration is worth at most". Profiles (Mara, servants) and the
    /// weapon tactics table change these; nothing else in the score is tuned by hand.
    /// </summary>
    [Serializable]
    public struct ScoreWeights
    {
        [Tooltip("Being behind where the player was last seen looking.")]
        public float behind;
        [Tooltip("Being in a dark cell (1 - light).")]
        public float dark;
        [Tooltip("Being somewhere the player has not looked recently (1 - watched).")]
        public float unseen;
        [Tooltip("Being beside a wall to slip into.")]
        public float cover;
        [Tooltip("Being at the preferred walking distance from the player.")]
        public float range;
        [Tooltip("Staying near this ghost's haunt anchor.")]
        public float home;
        [Tooltip("Penalty for being inside the ice gun's aim.")]
        public float threat;
        [Tooltip("Penalty for being near another ghost's chosen cell.")]
        public float crowd;

        public static ScoreWeights Default => new ScoreWeights
        {
            behind = 1.0f,
            dark = 1.0f,
            unseen = 1.2f,
            cover = 0.4f,
            range = 1.5f,
            home = 0.5f,
            threat = 3.0f,
            crowd = 1.0f,
        };
    }

    /// <summary>
    /// The walking distance a ghost wants to keep from the player: full marks between Min and Max metres,
    /// falling linearly to nothing over Softness metres either side. Stalk keeps back; Strike closes in.
    /// </summary>
    [Serializable]
    public struct RangeBand
    {
        public float min;
        public float max;
        public float softness;

        public RangeBand(float min, float max, float softness)
        {
            this.min = min;
            this.max = max;
            this.softness = softness;
        }

        public static RangeBand Stalk => new RangeBand(4f, 8f, 4f);

        /// <summary>1 inside the band, 0 beyond the soft edges or when unreachable (a trapezoid response curve).</summary>
        public float Fit(float metres)
        {
            if (float.IsPositiveInfinity(metres)) return 0f;
            float soft = Mathf.Max(softness, 1e-3f);
            if (metres < min) return Mathf.Clamp01(1f - (min - metres) / soft);
            if (metres > max) return Mathf.Clamp01(1f - (metres - max) / soft);
            return 1f;
        }
    }
}
