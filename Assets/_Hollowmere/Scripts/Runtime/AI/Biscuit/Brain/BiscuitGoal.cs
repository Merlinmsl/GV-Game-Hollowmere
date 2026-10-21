using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Something Biscuit wants to be true. Its priority (0 to 1) changes with the situation; each
    /// think the brain plans for the highest-priority goal that is not met yet and can be reached.
    /// </summary>
    public abstract class BiscuitGoal
    {
        protected BiscuitGoal(string name, Condition wants)
        {
            Name = name;
            Wants = wants;
        }

        public string Name { get; }
        public Condition Wants { get; }

        /// <summary>The priority from the last think, for the debug view.</summary>
        public float Priority { get; private set; }

        public float Evaluate(BiscuitBrain brain) => Priority = Mathf.Clamp01(Score(brain));

        protected abstract float Score(BiscuitBrain brain);
    }

    /// <summary>Get away from an enemy that is right next to Biscuit. Beats everything.</summary>
    public sealed class StaySafeGoal : BiscuitGoal
    {
        public StaySafeGoal() : base("Stay safe", new Condition(Fact.None, Fact.InDanger)) { }

        protected override float Score(BiscuitBrain brain) => brain.Has(Fact.InDanger) ? 1f : 0f;
    }

    /// <summary>Draw the enemies away from a badly hurt player.</summary>
    public sealed class ProtectPlayerGoal : BiscuitGoal
    {
        public ProtectPlayerGoal() : base("Protect player", new Condition(Fact.None, Fact.PlayerInTrouble)) { }

        protected override float Score(BiscuitBrain brain) => brain.Has(Fact.PlayerInTrouble) ? 0.9f : 0f;
    }

    /// <summary>Tell the player about enemies before the player can see them.</summary>
    public sealed class WarnPlayerGoal : BiscuitGoal
    {
        public WarnPlayerGoal() : base("Warn player", new Condition(Fact.PlayerWarned)) { }

        // The surer Biscuit is, the more a warning matters: 0.68 for a sound, 0.8 for a fresh smell.
        protected override float Score(BiscuitBrain brain)
        {
            if (!brain.Has(Fact.EnemyNearby) || brain.Has(Fact.PlayerWarned)) return 0f;
            return 0.5f + 0.3f * brain.Memory.StrongestConfidence(brain.Perception.Now);
        }
    }

    /// <summary>Bring ammunition to a player who is nearly out.</summary>
    public sealed class ResupplyGoal : BiscuitGoal
    {
        public ResupplyGoal() : base("Resupply", new Condition(Fact.None, Fact.PlayerLowOnAmmo)) { }

        // Below a warning about a fresh smell (0.8), above showing a page (0.45). Only worth wanting
        // when there is something to fetch, and not again straight after a delivery.
        protected override float Score(BiscuitBrain brain)
        {
            if (!brain.Has(Fact.PlayerLowOnAmmo) || !brain.ResupplyReady) return 0f;
            bool somethingToFetch = brain.Has(Fact.CarryingAmmo) || brain.Has(Fact.KnowsAmmo) ||
                                    brain.Perception.AmmoSpotsLeft > 0;
            return somethingToFetch ? 0.6f : 0f;
        }
    }

    /// <summary>Lead the player to one of Marrow's pages, but only while it is quiet.</summary>
    public sealed class ShowEvidenceGoal : BiscuitGoal
    {
        public ShowEvidenceGoal() : base("Show evidence", new Condition(Fact.PlayerAtEvidence)) { }

        protected override float Score(BiscuitBrain brain)
        {
            if (!brain.Perception.HasPlayer || brain.Has(Fact.EnemyNearby)) return 0f;
            return brain.Has(Fact.KnowsEvidence) || brain.Perception.PagesLeft > 0 ? 0.45f : 0f;
        }
    }

    /// <summary>Sniff the area while it is quiet.</summary>
    public sealed class ScoutGoal : BiscuitGoal
    {
        public ScoutGoal() : base("Scout", new Condition(Fact.AreaScouted)) { }

        protected override float Score(BiscuitBrain brain) =>
            brain.Perception.HasPlayer && !brain.Has(Fact.EnemyNearby) ? 0.35f : 0f;
    }

    /// <summary>Stay at the player's side. Always wanted, and more the further away the player is.</summary>
    public sealed class StayCloseGoal : BiscuitGoal
    {
        public StayCloseGoal() : base("Stay close", new Condition(Fact.NearPlayer)) { }

        // 0.29 at 3 m, 0.5 at 10 m, 0.95 at 25 m: a player who runs ahead wins over everything but danger.
        protected override float Score(BiscuitBrain brain) =>
            brain.Perception.HasPlayer ? 0.2f + 0.03f * brain.Perception.DistanceToPlayer : 0f;
    }
}
