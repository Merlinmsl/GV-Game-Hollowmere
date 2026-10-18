using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// The warning for ghosts. Biscuit whines and backs off to the far side of the player, away from
    /// the ghost, so the player can tell where it is from where the dog is not. Quiet, like a growl.
    /// </summary>
    public sealed class WhineAction : BiscuitAction
    {
        const float BackOff = 2f;

        float doneAt;

        public WhineAction()
            : base("Whine", new Condition(Fact.GhostNearby | Fact.NearPlayer), new Condition(Fact.PlayerWarned)) { }

        public override void Start(BiscuitBrain brain)
        {
            BiscuitPerception p = brain.Perception;
            brain.Memory.TryGetClosest(p.PlayerPosition, brain.Tuning.forgetBelow, p.Now, KindFilter.Ghosts,
                                       out EnemyBelief ghost, out _);

            Vector3 away = p.PlayerPosition - ghost.Position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
            brain.Body.MoveTo(p.PlayerPosition + away * BackOff, 0.3f, false);
            brain.Body.Whine(ghost.Position);
            doneAt = p.Now + brain.Tuning.whineDuration;
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (brain.Perception.Now < doneAt) return ActionStatus.Running;
            brain.MarkWarned();
            return ActionStatus.Success;
        }
    }
}
