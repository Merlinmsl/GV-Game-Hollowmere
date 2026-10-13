namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Stop and sniff for the dead within 25 m of walking distance. Whatever it finds goes into
    /// memory, which changes the facts and makes the brain plan again (usually a warning). The
    /// planner itself only counts on the area being scouted afterwards.
    /// </summary>
    public sealed class SniffAction : BiscuitAction
    {
        float doneAt;

        public SniffAction()
            : base("Sniff", new Condition(Fact.None, Fact.AreaScouted), new Condition(Fact.AreaScouted)) { }

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
