using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// A loud warning that works from anywhere. But it is a real noise of 25 m in the senses layer,
    /// so every one of the dead in earshot hears it and comes. Each extra one in earshot makes the
    /// bark dearer, so in a crowded street Biscuit runs back and growls instead.
    /// </summary>
    public sealed class BarkAction : BiscuitAction
    {
        float doneAt;

        public BarkAction()
            : base("Bark", new Condition(Fact.EnemyNearby), new Condition(Fact.PlayerWarned)) { }

        public override float Cost(BiscuitBrain brain)
        {
            // Straight-line earshot is never smaller than the real (walking) one, so this errs on
            // the side of caution: it may count one too many, never one too few.
            int inEarshot = brain.Memory.CountWithin(brain.Perception.Position, Loudness.Bark,
                                                     brain.Tuning.forgetBelow, brain.Perception.Now);
            return brain.Tuning.barkCost + brain.Tuning.barkCostPerExtraDead * Mathf.Max(0, inEarshot - 1);
        }

        public override void Start(BiscuitBrain brain)
        {
            brain.Body.Stop();
            brain.Body.Bark();
            doneAt = brain.Perception.Now + brain.Tuning.barkDuration;
        }

        public override ActionStatus Tick(BiscuitBrain brain)
        {
            if (brain.Perception.Now < doneAt) return ActionStatus.Running;
            brain.MarkWarned();
            return ActionStatus.Success;
        }
    }
}
