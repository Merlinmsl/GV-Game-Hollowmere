using System.Collections.Generic;

namespace Hollowmere.AI.Zombie.BehaviourTree
{
    /// <summary>
    /// Execution status returned by a Behaviour Tree node on each tick.
    /// </summary>
    public enum BtStatus
    {
        /// <summary>The node is currently performing its task across multiple ticks.</summary>
        Running,

        /// <summary>The node has completed its task successfully.</summary>
        Success,

        /// <summary>The node failed its task or its precondition was not met.</summary>
        Failure
    }

    /// <summary>
    /// Abstract base class for all Behaviour Tree nodes in the Runner Zombie brain.
    /// Pure C# class decoupled from UnityEngine and MonoBehaviour.
    /// Designed for zero heap allocations per tick to stay strictly within the 2 ms AI budget.
    /// </summary>
    public abstract class BtNode
    {
        /// <summary>Descriptive name of this node for logging and the F2 debug overlay.</summary>
        public string Name { get; set; }

        /// <summary>Result returned by this node on its most recent tick.</summary>
        public BtStatus LastStatus { get; protected set; } = BtStatus.Failure;

        protected BtNode(string name = null)
        {
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
        }

        /// <summary>
        /// Evaluates this node for the current AI tick.
        /// Must not allocate memory on the managed heap.
        /// </summary>
        /// <returns>Running, Success, or Failure.</returns>
        public abstract BtStatus Tick();

        /// <summary>
        /// Resets the internal state of this node.
        /// Called when an active branch is preempted by a higher-priority node or finishes.
        /// </summary>
        public virtual void Reset()
        {
            LastStatus = BtStatus.Failure;
        }

        /// <summary>
        /// Returns the deepest actively running leaf node for F2 debug inspection.
        /// </summary>
        public virtual BtNode GetActiveNode()
        {
            return this;
        }
    }

    /// <summary>
    /// Abstract base class for composite nodes that manage child nodes (e.g. Selector, Sequence).
    /// </summary>
    public abstract class BtComposite : BtNode
    {
        protected readonly List<BtNode> children = new List<BtNode>();

        /// <summary>Read-only access to child nodes.</summary>
        public IReadOnlyList<BtNode> Children => children;

        /// <summary>The currently active child node during execution, if any.</summary>
        public abstract BtNode ActiveChild { get; }

        protected BtComposite(string name = null, params BtNode[] initialChildren) : base(name)
        {
            if (initialChildren != null)
            {
                for (int i = 0; i < initialChildren.Length; i++)
                {
                    if (initialChildren[i] != null)
                    {
                        children.Add(initialChildren[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Fluent helper to append a child node to this composite.
        /// </summary>
        public BtComposite AddChild(BtNode child)
        {
            if (child != null)
            {
                children.Add(child);
            }
            return this;
        }

        public override void Reset()
        {
            base.Reset();
            for (int i = 0; i < children.Count; i++)
            {
                children[i].Reset();
            }
        }

        public override BtNode GetActiveNode()
        {
            BtNode active = ActiveChild;
            return active != null ? active.GetActiveNode() : this;
        }
    }
}
