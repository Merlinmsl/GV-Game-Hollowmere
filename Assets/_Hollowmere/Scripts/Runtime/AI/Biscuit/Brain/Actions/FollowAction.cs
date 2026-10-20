using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Go back to the player's side. Costs 1 plus a little per metre, so the planner weighs the
    /// time it takes to get there against a bark that works from anywhere.
    /// </summary>
    public sealed class FollowAction : BiscuitAction
    {
        public FollowAction() : base("Follow", new Condition(Fact.None), new Condition(Fact.NearPlayer)) { }

        public override bool IsUsable(BiscuitBrain brain) => brain.Perception.HasPlayer;

        public override float Cost(BiscuitBrain brain) =>
            1f + brain.Perception.DistanceToPlayer * brain.Tuning.followCostPerMetre;

        public override void Start(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;

            // The player teleported, fell or got far ahead: catch up at once, just behind them.
            if (p.DistanceToPlayer > brain.Tuning.warpDistance)
            {
                Vector3 behind = p.PlayerLook;
                behind.y = 0f;
                behind = behind.sqrMagnitude > 0.01f ? -behind.normalized : Vector3.back;
                brain.Body.WarpNear(p.PlayerPosition + behind * brain.Tuning.followStopDistance);
            }

            MoveToPlayer(brain);
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (brain.Body.PathFailed) return ActionStatus.Failure;   // no route: a locked door, say
            MoveToPlayer(brain);   // the player keeps moving, so keep aiming at them
            return ActionStatus.Running;
        }

        static void MoveToPlayer(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            brain.Body.MoveTo(p.PlayerPosition, brain.Tuning.followStopDistance,
                              p.DistanceToPlayer > brain.Tuning.runDistance);
        }
    }
}
