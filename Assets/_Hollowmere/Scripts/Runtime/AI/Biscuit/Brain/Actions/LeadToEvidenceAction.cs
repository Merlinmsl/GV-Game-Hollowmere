using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Take the player to one of Marrow's pages. Biscuit goes ahead, but never far: when the player
    /// falls more than 5 m behind it stops and waits. If the player wanders off past 6 m, NearPlayer
    /// breaks, the brain plans "Follow > Lead to evidence" and Biscuit comes back for them. It only
    /// leads while it is quiet: an enemy turning up breaks its needs and the brain warns instead.
    /// </summary>
    public sealed class LeadToEvidenceAction : BiscuitAction
    {
        bool moving;

        public LeadToEvidenceAction()
            : base("Lead to evidence", new Condition(Fact.KnowsEvidence | Fact.NearPlayer, Fact.EnemyNearby),
                   new Condition(Fact.PlayerAtEvidence)) { }

        public override float Cost(BiscuitBrain brain) => brain.Tuning.leadCost;

        public override float Timeout(BiscuitBrain brain) => brain.Tuning.leadTimeout;

        public override void Start(BiscuitBrain brain) => moving = false;

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (!brain.TryGetKnownPoint(PointKind.Evidence, out _, out Vector3 page)) return ActionStatus.Failure;

            BiscuitPerception p = brain.Perception;
            BiscuitTuning tuning = brain.Tuning;

            // At the page (or as close as the NavMesh lets a dog get, if it is up on a table): wait there.
            bool atPage = BiscuitBrain.FlatDistance(p.Position, page) <= tuning.pickUpDistance + 0.5f ||
                          (moving && brain.Body.HasArrived);
            if (atPage || p.DistanceToPlayer > tuning.leadWaitDistance)
            {
                if (moving) brain.Body.Stop();
                moving = false;
                return ActionStatus.Running;
            }

            brain.Body.MoveTo(page, 0.8f, false);
            moving = true;
            return ActionStatus.Running;
        }
    }
}
