namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Stop and sniff: for enemies within 25 m of walking distance, and for ammunition and pages
    /// within 35 m. Whatever it finds goes into memory, which changes the facts and makes the brain
    /// plan again.
    ///
    /// The planner is told, optimistically, that a sniff may turn up ammunition and a page. That is
    /// what lets it build "Sniff > Fetch ammo > Bring ammo" before anything has been found. If the
    /// sniff finds nothing, the next step's needs fail, the brain plans again, and AreaScouted stops
    /// it sniffing the same spot again for 12 s.
    /// </summary>
    public sealed class SniffAction : BiscuitAction
    {
        float doneAt;

        public SniffAction()
            : base("Sniff", new Condition(Fact.None, Fact.AreaScouted),
                   new Condition(Fact.AreaScouted | Fact.KnowsAmmo | Fact.KnowsEvidence)) { }

        public override void Start(BiscuitBrain brain)
        {
            brain.Body.Stop();
            brain.Body.BeginSniff(brain.Tuning.sniffRange);
            doneAt = brain.Perception.Now + brain.Tuning.sniffDuration;
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            // The trails are followed a few per think, to stay inside the path budget.
            bool checkedAll = brain.Body.SniffStep();
            if (!checkedAll || brain.Perception.Now < doneAt) return ActionStatus.Running;

            brain.MarkScouted();
            return ActionStatus.Success;
        }
    }
}
