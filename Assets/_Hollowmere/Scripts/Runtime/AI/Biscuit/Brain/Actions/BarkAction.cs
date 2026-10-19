using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// A loud warning that works from anywhere. But it is a real noise of 25 m in the senses layer,
    /// so everything in earshot hears it and comes. The cost counts who would hear it, weighted by
    /// how well each kind hears: the dead 1, the creature 1.6 (it hears from 40 m), ghosts 0 (they
    /// do not chase noise). Near the creature, or in a street full of the dead, Biscuit goes quiet
    /// and runs back to growl instead.
    /// </summary>
    public sealed class BarkAction : BiscuitAction
    {
        float doneAt;

        public BarkAction()
            : base("Bark", new Condition(Fact.EnemyNearby), new Condition(Fact.PlayerWarned)) { }

        public override float Cost(BiscuitBrain brain)
        {
            BiscuitMemory memory = brain.Memory;
            BiscuitTuning tuning = brain.Tuning;
            Vector3 here = brain.Perception.Position;
            float now = brain.Perception.Now;

            float listeners = 0f;
            for (int i = 0; i < memory.Count; i++)
            {
                EnemyBelief belief = memory[i];
                float hearing = tuning.HearingOf(belief.Kind);
                if (hearing <= 0f || memory.ConfidenceOf(belief, now) < tuning.forgetBelow) continue;

                // Straight-line distance is never longer than the walking route the sound takes, so
                // this errs on the side of caution: it may count one too many, never one too few.
                float reach = Loudness.Bark * hearing;
                if ((belief.Position - here).sqrMagnitude <= reach * reach) listeners += hearing;
            }

            // The first listener is the one being warned about; every one after it gets woken.
            return tuning.barkCost + tuning.barkCostPerExtraListener * Mathf.Max(0f, listeners - 1f);
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
