using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>What the ghost knows that matters for choosing a mode, all from its own senses and memory.</summary>
    public struct ModeInputs
    {
        public bool Believes;
        /// <summary>Belief confidence now, 0..1.</summary>
        public float Confidence;
        /// <summary>1 just seen, falling to 0 as the sighting goes stale (or 0 when the belief is not from sight).</summary>
        public float Freshness;
        /// <summary>Metres from the ghost to where it believes the player is.</summary>
        public float Distance;
        /// <summary>How lit or watched the ghost's own cell is, 0..1.</summary>
        public float Exposure;
        /// <summary>1 when the player was last seen looking straight away from the ghost, 0 straight at it.</summary>
        public float FacingAway;
        /// <summary>Seconds spent stalking or waiting in ambush since the last strike or loss of the player.</summary>
        public float HuntSeconds;
        /// <summary>The ghost has just seen the ice gun aimed at itself.</summary>
        public bool Aimed;
        /// <summary>The ghost knows the player has the ice gun.</summary>
        public bool IceGunHeld;
        /// <summary>The strike cooldown is over.</summary>
        public bool StrikeReady;
    }

    /// <summary>
    /// Utility AI over the ghost's modes. Each mode's utility is a PRODUCT of response curves, so any
    /// one consideration at 0 vetoes the mode (an aimed ice gun forbids Strike however close the ghost is):
    ///
    ///   Stalk  = stalkBase * C
    ///   Ambush = ambushWeight * C * (1 - F) * (0.5 + 0.5 G)
    ///   Strike = strikeWeight * C * Ready * Near(D) * (0.25 + 0.75 B) * (1 - Aimed) * Tactic * Patience
    ///   Flee   = fleeWeight * max(Aimed, IceGun * E * 0.6 * (1 - G))
    ///
    ///   C confidence, F sighting freshness, D distance, B facing away, E exposure, G aggression,
    ///   Near(D)  = 1 - t^2, t = (D - strikeDistance) / (strikeReach - strikeDistance), clamped
    ///   Tactic   = carefulStrikeScale with the ice gun about, boldStrikeScale otherwise
    ///   Patience = 0.5 rising to 1 over patienceSeconds of hunting
    ///
    /// The highest utility wins; the current mode gets switchMargin on top (hysteresis).
    /// </summary>
    public static class ModeSelector
    {
        /// <summary>Utilities indexed by (int)GhostMode; Idle and Frozen are not chosen here and stay 0.</summary>
        public const int Count = 6;

        public static void Evaluate(in ModeInputs i, in ModeTuning t, float[] utility)
        {
            for (int m = 0; m < Count; m++) utility[m] = 0f;
            if (!i.Believes) return;

            float aimed = i.Aimed ? 1f : 0f;
            utility[(int)GhostMode.Stalk] = t.stalkBase * i.Confidence;
            utility[(int)GhostMode.Ambush] = t.ambushWeight * i.Confidence * (1f - i.Freshness) * (0.5f + 0.5f * t.aggression);
            utility[(int)GhostMode.Strike] = t.strikeWeight * i.Confidence * (i.StrikeReady ? 1f : 0f) * Near(i.Distance, t)
                                             * (0.25f + 0.75f * i.FacingAway) * (1f - aimed)
                                             * (i.IceGunHeld ? t.carefulStrikeScale : t.boldStrikeScale) * Patience(i.HuntSeconds, t);
            utility[(int)GhostMode.Flee] = t.fleeWeight * Mathf.Max(aimed, (i.IceGunHeld ? 1f : 0f) * i.Exposure * 0.6f * (1f - t.aggression));
        }

        /// <summary>The mode to be in: Idle without a belief, otherwise the best utility with hysteresis.</summary>
        public static GhostMode Choose(in ModeInputs i, in ModeTuning t, GhostMode current, float[] utility)
        {
            Evaluate(i, t, utility);
            if (!i.Believes) return GhostMode.Idle;

            GhostMode best = GhostMode.Stalk;
            float bestValue = float.NegativeInfinity;
            for (int m = (int)GhostMode.Stalk; m <= (int)GhostMode.Flee; m++)
            {
                float value = utility[m] + (m == (int)current ? t.switchMargin : 0f);
                if (value > bestValue) { best = (GhostMode)m; bestValue = value; }
            }
            return best;
        }

        /// <summary>1 within strike distance, easing to 0 at strike reach (a quadratic response curve).</summary>
        public static float Near(float distance, in ModeTuning t)
        {
            float span = Mathf.Max(t.strikeReach - t.strikeDistance, 1e-3f);
            float x = Mathf.Clamp01((distance - t.strikeDistance) / span);
            return 1f - x * x;
        }

        /// <summary>0.5 when the hunt starts, rising to 1 after patienceSeconds.</summary>
        public static float Patience(float huntSeconds, in ModeTuning t) =>
            0.5f + 0.5f * Mathf.Clamp01(huntSeconds / Mathf.Max(t.patienceSeconds, 1e-3f));
    }
}
