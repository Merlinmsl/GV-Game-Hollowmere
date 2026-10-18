using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// When the player is badly hurt and an enemy is close, run off to one side and bark so it
    /// follows the noise instead. There is no special code for this in the zombies: the bark is an
    /// ordinary noise and their own hearing sends them to it. Ghosts do not chase noise, so Biscuit
    /// does not try this on them.
    /// </summary>
    public sealed class LureAction : BiscuitAction
    {
        float readyAt = float.NegativeInfinity;
        bool hasSpot;
        bool arrived;
        int barks;
        float nextBarkAt;

        public LureAction()
            : base("Lure", new Condition(Fact.EnemyNearby | Fact.PlayerInTrouble),
                   new Condition(Fact.None, Fact.PlayerInTrouble)) { }

        public override bool IsUsable(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            return p.HasPlayer && p.Now >= readyAt &&
                   brain.Memory.CountWithin(p.PlayerPosition, brain.Tuning.troubleDistance,
                                            brain.Tuning.sureConfidence, p.Now, KindFilter.NotGhosts) > 0;
        }

        public override float Cost(BiscuitBrain brain) => brain.Tuning.lureCost;

        public override void Start(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            barks = 0;
            arrived = false;
            hasSpot = false;

            if (!brain.Memory.TryGetClosest(p.PlayerPosition, brain.Tuning.sureConfidence, p.Now,
                                            KindFilter.NotGhosts, out EnemyBelief threat, out _))
                return;
            if (!brain.Body.TryFindLurePoint(p.PlayerPosition, threat.Position, brain.Tuning.lureDistance,
                                             out Vector3 spot))
                return;

            hasSpot = true;
            brain.Body.MoveTo(spot, 0.5f, true);
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (!hasSpot || brain.Body.PathFailed) return ActionStatus.Failure;

            float now = brain.Perception.Now;
            if (!arrived)
            {
                if (!brain.Body.HasArrived) return ActionStatus.Running;
                arrived = true;
                nextBarkAt = now;
            }

            if (now < nextBarkAt) return ActionStatus.Running;
            brain.Body.Bark();
            barks++;
            nextBarkAt = now + brain.Tuning.lureBarkInterval;
            if (barks < brain.Tuning.lureBarks) return ActionStatus.Running;

            brain.MarkWarned();
            return ActionStatus.Success;
        }

        // However it ended, do not lure again straight away: the dead need time to come to the bark.
        public override void Stop(BiscuitBrain brain) => readyAt = brain.Perception.Now + brain.Tuning.lureCooldown;
    }
}
