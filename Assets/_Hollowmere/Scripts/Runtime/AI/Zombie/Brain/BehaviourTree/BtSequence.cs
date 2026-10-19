namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Sequence composite node (AND gate).
    /// Executes child nodes in sequence from first to last.
    /// Fails immediately if any child fails.
    /// Returns Running and stays on the active child if a child returns Running.
    /// Succeeds only when all child nodes succeed.
    /// </summary>
    public class BtSequence : BtComposite
    {
        private int currentChildIndex = 0;

        /// <summary>The child node currently being evaluated.</summary>
        public override BtNode ActiveChild =>
            (currentChildIndex >= 0 && currentChildIndex < children.Count) ? children[currentChildIndex] : null;

        public int CurrentChildIndex => currentChildIndex;

        public BtSequence(string name = null, params BtNode[] initialChildren) : base(name, initialChildren) { }

        /// <summary>
        /// Ticks through child nodes in sequence.
        /// Zero allocations during tick evaluation.
        /// </summary>
        public override BtStatus Tick()
        {
            while (currentChildIndex < children.Count)
            {
                BtStatus status = children[currentChildIndex].Tick();

                if (status == BtStatus.Running)
                {
                    LastStatus = BtStatus.Running;
                    return BtStatus.Running;
                }

                if (status == BtStatus.Failure)
                {
                    Reset();
                    LastStatus = BtStatus.Failure;
                    return BtStatus.Failure;
                }

                // Child succeeded; advance to the next step
                currentChildIndex++;
            }

            // All child nodes finished with Success
            Reset();
            LastStatus = BtStatus.Success;
            return BtStatus.Success;
        }

        public override void Reset()
        {
            base.Reset();
            currentChildIndex = 0;
        }
    }
}
