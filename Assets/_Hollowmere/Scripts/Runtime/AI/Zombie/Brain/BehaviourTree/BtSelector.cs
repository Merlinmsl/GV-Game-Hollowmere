namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Priority Selector composite node (OR gate).
    /// Evaluates children from top to bottom (index 0 to N-1).
    /// The first branch that returns Success or Running wins.
    /// If higher-priority branches become available while a lower branch was Running,
    /// the lower branch is preempted and cleanly reset.
    /// </summary>
    public class BtSelector : BtComposite
    {
        private int runningChildIndex = -1;

        /// <summary>
        /// The child node that is currently Running, or null if no child is active.
        /// </summary>
        public override BtNode ActiveChild =>
            (runningChildIndex >= 0 && runningChildIndex < children.Count) ? children[runningChildIndex] : null;

        public BtSelector(string name = null, params BtNode[] initialChildren) : base(name, initialChildren) { }

        /// <summary>
        /// Evaluates child branches in priority order.
        /// Zero allocations during tick evaluation.
        /// </summary>
        public override BtStatus Tick()
        {
            for (int i = 0; i < children.Count; i++)
            {
                BtStatus status = children[i].Tick();

                if (status == BtStatus.Running)
                {
                    // If a different branch was running previously (e.g. lower priority), reset it
                    if (runningChildIndex != -1 && runningChildIndex != i)
                    {
                        children[runningChildIndex].Reset();
                    }

                    runningChildIndex = i;
                    LastStatus = BtStatus.Running;
                    return BtStatus.Running;
                }

                if (status == BtStatus.Success)
                {
                    // Completed successfully; clear running state
                    if (runningChildIndex != -1 && runningChildIndex != i)
                    {
                        children[runningChildIndex].Reset();
                    }

                    runningChildIndex = -1;
                    LastStatus = BtStatus.Success;
                    return BtStatus.Success;
                }

                // If status == BtStatus.Failure, fall through to evaluate next priority child
            }

            // All children failed
            if (runningChildIndex != -1)
            {
                children[runningChildIndex].Reset();
                runningChildIndex = -1;
            }

            LastStatus = BtStatus.Failure;
            return BtStatus.Failure;
        }

        public override void Reset()
        {
            base.Reset();
            runningChildIndex = -1;
        }
    }
}
