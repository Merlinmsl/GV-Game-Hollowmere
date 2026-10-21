namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Carry the ammunition back and drop it at the player's feet. Costs about the time it takes.
    /// </summary>
    public sealed class BringAmmoAction : BiscuitAction
    {
        public BringAmmoAction()
            : base("Bring ammo", new Condition(Fact.CarryingAmmo),
                   new Condition(Fact.None, Fact.CarryingAmmo | Fact.PlayerLowOnAmmo)) { }

        public override bool IsUsable(BiscuitBrain brain) => brain.Perception.HasPlayer;

        public override float Cost(BiscuitBrain brain) =>
            1f + brain.Perception.DistanceToPlayer * brain.Tuning.followCostPerMetre;

        public override void Start(BiscuitBrain brain) => MoveToPlayer(brain);

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (brain.Body.PathFailed) return ActionStatus.Failure;

            if (brain.Perception.DistanceToPlayer > brain.Tuning.deliverDistance)
            {
                MoveToPlayer(brain);   // the player keeps moving
                return ActionStatus.Running;
            }

            brain.Body.DeliverAmmo(brain.Tuning.ammoPerDelivery);
            brain.MarkDelivered();
            return ActionStatus.Success;
        }

        static void MoveToPlayer(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            brain.Body.MoveTo(p.PlayerPosition, 1f, p.DistanceToPlayer > brain.Tuning.runDistance);
        }
    }
}
