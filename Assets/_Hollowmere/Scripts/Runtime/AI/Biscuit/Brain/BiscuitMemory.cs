using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>One enemy that Biscuit has smelt or heard.</summary>
    public struct EnemyBelief
    {
        public int SourceId;        // the ScentSource id, or whatever made the noise
        public EnemyKind Kind;
        public Vector3 Position;    // where it was when last sensed, not where it is now
        public float Confidence0;   // confidence at Time
        public float Time;
        public bool Smelt;          // smelt at least once, not only heard
    }

    /// <summary>
    /// What Biscuit believes about the enemies nearby. Biscuit never reads an enemy's true
    /// position: it remembers where it last smelt or heard one, and the belief fades as
    /// c0 x e^(-t / tau). Fixed size, so remembering never allocates.
    /// </summary>
    public sealed class BiscuitMemory
    {
        public const int Capacity = 16;

        readonly EnemyBelief[] beliefs = new EnemyBelief[Capacity];
        readonly BiscuitTuning tuning;

        public BiscuitMemory(BiscuitTuning tuning)
        {
            this.tuning = tuning;
        }

        public int Count { get; private set; }

        /// <summary>
        /// Goes up by one for every enemy Biscuit did not already know about. A warning covers
        /// every threat met before it.
        /// </summary>
        public int ThreatsMet { get; private set; }

        public EnemyBelief this[int index] => beliefs[index];

        public float ConfidenceOf(in EnemyBelief belief, float now) =>
            belief.Confidence0 * Mathf.Exp(-(now - belief.Time) / tuning.beliefTau);

        public void Remember(int sourceId, EnemyKind kind, Vector3 position, float confidence, bool smelt, float now)
        {
            for (int i = 0; i < Count; i++)
            {
                if (beliefs[i].SourceId != sourceId) continue;

                // A weaker sense now (heard after smelt) must not lower what Biscuit is sure of.
                beliefs[i].Confidence0 = Mathf.Max(ConfidenceOf(beliefs[i], now), confidence);
                beliefs[i].Position = position;
                beliefs[i].Time = now;
                if (smelt)
                {
                    beliefs[i].Smelt = true;
                    beliefs[i].Kind = kind;   // a smell says what it is; a groan only guesses
                }
                return;
            }

            int slot = Count;
            if (slot == Capacity)
            {
                // Full: replace the faintest belief, but only with a stronger one.
                slot = Faintest(now, out float faintest);
                if (faintest >= confidence) return;
            }
            else
            {
                Count++;
            }

            beliefs[slot] = new EnemyBelief
            {
                SourceId = sourceId, Kind = kind, Position = position,
                Confidence0 = confidence, Time = now, Smelt = smelt,
            };
            ThreatsMet++;
        }

        /// <summary>Gives up on beliefs that have faded below the threshold.</summary>
        public void ForgetFaded(float now)
        {
            for (int i = Count - 1; i >= 0; i--)
                if (ConfidenceOf(beliefs[i], now) < tuning.forgetBelow) RemoveAt(i);
        }

        /// <summary>Forgets one source, for example when its zone unloads or it is destroyed.</summary>
        public void Forget(int sourceId)
        {
            for (int i = Count - 1; i >= 0; i--)
                if (beliefs[i].SourceId == sourceId) RemoveAt(i);
        }

        /// <summary>The belief closest to a point that is at least this sure. False when there is none.</summary>
        public bool TryGetClosest(Vector3 point, float minConfidence, float now,
                                  out EnemyBelief closest, out float distance) =>
            TryGetClosest(point, minConfidence, now, KindFilter.Any, out closest, out distance);

        public bool TryGetClosest(Vector3 point, float minConfidence, float now, KindFilter filter,
                                  out EnemyBelief closest, out float distance)
        {
            closest = default;
            distance = float.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                if (!filter.Matches(beliefs[i].Kind) || ConfidenceOf(beliefs[i], now) < minConfidence) continue;
                float d = Vector3.Distance(point, beliefs[i].Position);
                if (d >= distance) continue;
                distance = d;
                closest = beliefs[i];
            }
            return distance < float.PositiveInfinity;
        }

        public int CountWithin(Vector3 point, float radius, float minConfidence, float now,
                               KindFilter filter = KindFilter.Any)
        {
            int count = 0;
            float radius2 = radius * radius;
            for (int i = 0; i < Count; i++)
            {
                if (filter.Matches(beliefs[i].Kind) &&
                    ConfidenceOf(beliefs[i], now) >= minConfidence &&
                    (beliefs[i].Position - point).sqrMagnitude <= radius2)
                    count++;
            }
            return count;
        }

        public float StrongestConfidence(float now)
        {
            float strongest = 0f;
            for (int i = 0; i < Count; i++)
                strongest = Mathf.Max(strongest, ConfidenceOf(beliefs[i], now));
            return strongest;
        }

        int Faintest(float now, out float confidence)
        {
            int faintest = 0;
            confidence = float.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                float c = ConfidenceOf(beliefs[i], now);
                if (c >= confidence) continue;
                confidence = c;
                faintest = i;
            }
            return faintest;
        }

        // Order does not matter, so move the last belief into the gap.
        void RemoveAt(int index)
        {
            Count--;
            beliefs[index] = beliefs[Count];
        }
    }
}
