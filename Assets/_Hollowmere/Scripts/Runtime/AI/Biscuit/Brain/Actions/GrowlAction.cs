namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// A quiet warning, facing the threat: the player hears it and sees which way Biscuit looks,
    /// the dead do not. Only works at the player's side, so it is cheap but often needs a Follow first.
    /// </summary>
    public sealed class GrowlAction : BiscuitAction
    {
        float doneAt;

        public GrowlAction()
            : base("Growl", new Condition(Fact.EnemyNearby | Fact.NearPlayer), new Condition(Fact.PlayerWarned)) { }

        public override void Start(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            brain.Body.Stop();
            brain.Memory.TryGetClosest(p.Position, brain.Tuning.forgetBelow, p.Now, out EnemyBelief threat, out _);
            brain.Body.Growl(threat.Position);
            doneAt = p.Now + brain.Tuning.growlDuration;
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (brain.Perception.Now < doneAt) return ActionStatus.Running;
            brain.MarkWarned();
            return ActionStatus.Success;
        }
    }
}
