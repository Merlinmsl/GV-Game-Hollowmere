using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>Back away from one of the dead that got too close. Done once it is no longer close.</summary>
    public sealed class HideAction : BiscuitAction
    {
        bool hasSpot;

        public HideAction()
            : base("Hide", new Condition(Fact.InDanger), new Condition(Fact.None, Fact.InDanger)) { }

        public override void Start(BiscuitBrain brain) => PickSpot(brain);

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (!hasSpot || brain.Body.PathFailed) return ActionStatus.Failure;   // cornered: plan something else

            // Got there but still in danger: the threat followed, so pick a new spot.
            if (brain.Body.HasArrived) PickSpot(brain);
            return hasSpot ? ActionStatus.Running : ActionStatus.Failure;
        }

        void PickSpot(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            hasSpot = false;

            if (!brain.Memory.TryGetClosest(p.Position, brain.Tuning.sureConfidence, p.Now,
                                            out EnemyBelief threat, out _))
                return;
            if (!brain.Body.TryFindRetreatPoint(threat.Position, brain.Tuning.hideDistance, out Vector3 spot))
                return;

            hasSpot = true;
            brain.Body.MoveTo(spot, 0.3f, true);
        }
    }
}
