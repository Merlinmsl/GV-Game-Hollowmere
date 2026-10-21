using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Go to ammunition Biscuit has smelt and pick it up. Costs about the time it takes to get there;
    /// before a sniff the place is not known yet, so the planner assumes 10 m.
    /// </summary>
    public sealed class FetchAmmoAction : BiscuitAction
    {
        const float UnknownDistance = 10f;

        public FetchAmmoAction()
            : base("Fetch ammo", new Condition(Fact.KnowsAmmo, Fact.CarryingAmmo), new Condition(Fact.CarryingAmmo)) { }

        public override float Cost(BiscuitBrain brain)
        {
            float metres = brain.TryGetKnownPoint(PointKind.Ammo, out _, out Vector3 spot)
                ? Vector3.Distance(brain.Perception.Position, spot)
                : UnknownDistance;
            return 1f + metres * brain.Tuning.followCostPerMetre;
        }

        public override void Start(BiscuitBrain brain)
        {
            if (brain.TryGetKnownPoint(PointKind.Ammo, out _, out Vector3 spot)) MoveTo(brain, spot);
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (!brain.TryGetKnownPoint(PointKind.Ammo, out int id, out Vector3 spot)) return ActionStatus.Failure;

            if (brain.Body.PathFailed)
            {
                brain.IgnorePoint(PointKind.Ammo);   // Biscuit cannot get there: stop trying for it
                return ActionStatus.Failure;
            }

            if (BiscuitBrain.FlatDistance(brain.Perception.Position, spot) > brain.Tuning.pickUpDistance)
            {
                MoveTo(brain, spot);   // a closer spot may have been smelt on the way
                return ActionStatus.Running;
            }

            if (!brain.Body.TryTakeAmmo(id)) return ActionStatus.Failure;   // someone took it first
            brain.MarkCarrying();
            return ActionStatus.Success;
        }

        static void MoveTo(BiscuitBrain brain, Vector3 spot) =>
            brain.Body.MoveTo(spot, 0.4f, Vector3.Distance(brain.Perception.Position, spot) > brain.Tuning.runDistance);
    }
}
