namespace Hollowmere.AI.Biscuit
{
    public enum ActionStatus { Running, Success, Failure }

    /// <summary>
    /// One thing Biscuit can do. The planner only sees Needs, Gives and Cost; the brain then runs
    /// the chosen actions one after another through Start, Tick and Stop.
    /// </summary>
    public abstract class BiscuitAction
    {
        protected BiscuitAction(string name, Condition needs, Condition gives)
        {
            Name = name;
            Needs = needs;
            Gives = gives;
        }

        public string Name { get; }

        /// <summary>Facts that must hold before the action starts, and while it runs.</summary>
        public Condition Needs { get; }

        /// <summary>Facts the action is expected to make true or false. Once they hold, it is done.</summary>
        public Condition Gives { get; }

        /// <summary>Cost for the planner, worked out once per plan from the current situation.</summary>
        public virtual float Cost(BiscuitBrain brain) => 1f;

        /// <summary>Checks facts cannot express, such as a cooldown. False leaves it out of planning.</summary>
        public virtual bool IsUsable(BiscuitBrain brain) => true;

        public virtual void Start(BiscuitBrain brain) { }

        public abstract ActionStatus Tick(BiscuitBrain brain);

        /// <summary>Called when the action ends for any reason: done, failed or interrupted.</summary>
        public virtual void Stop(BiscuitBrain brain) { }
    }
}
