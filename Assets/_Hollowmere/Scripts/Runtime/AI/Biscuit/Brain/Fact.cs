using System;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// The facts Biscuit believes about the world, each true or false. A whole world state fits
    /// in one ushort, so the planner can use it as an array index and never allocates.
    /// </summary>
    [Flags]
    public enum Fact : ushort
    {
        None = 0,
        NearPlayer = 1 << 0,        // at the player's side
        EnemyNearby = 1 << 1,       // believes one of the dead is within reach of the player
        PlayerWarned = 1 << 2,      // has growled or barked about the threats met so far
        InDanger = 1 << 3,          // one of the dead is right next to Biscuit
        PlayerInTrouble = 1 << 4,   // the player is badly hurt and the dead are close to them
        AreaScouted = 1 << 5,       // has sniffed around here recently
    }

    /// <summary>
    /// Facts that must hold, or that an action makes hold. Mask says which facts matter, Values
    /// says what they must be. The same type serves as precondition, effect and goal.
    /// </summary>
    public readonly struct Condition
    {
        public readonly ushort Mask;
        public readonly ushort Values;

        public Condition(Fact mustBeTrue, Fact mustBeFalse = Fact.None)
        {
            Mask = (ushort)(mustBeTrue | mustBeFalse);
            Values = (ushort)mustBeTrue;
        }

        public bool IsMetBy(ushort state) => (state & Mask) == Values;

        /// <summary>The state after these facts have been made true or false.</summary>
        public ushort ApplyTo(ushort state) => (ushort)((state & ~Mask) | Values);

        /// <summary>How many of these facts the state does not have yet.</summary>
        public int CountUnmet(ushort state) => WorldState.CountBits((state ^ Values) & Mask);
    }

    public static class WorldState
    {
        /// <summary>Number of facts. The planner sizes its arrays as 2^FactCount.</summary>
        public const int FactCount = 6;
        public const int StateCount = 1 << FactCount;

        public static readonly Fact[] AllFacts =
        {
            Fact.NearPlayer, Fact.EnemyNearby, Fact.PlayerWarned,
            Fact.InDanger, Fact.PlayerInTrouble, Fact.AreaScouted,
        };

        public static bool Has(ushort state, Fact fact) => (state & (ushort)fact) != 0;

        public static ushort With(ushort state, Fact fact, bool value) =>
            value ? (ushort)(state | (ushort)fact) : (ushort)(state & ~(ushort)fact);

        public static int CountBits(int bits)
        {
            int count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;   // clears the lowest set bit
                count++;
            }
            return count;
        }
    }
}
