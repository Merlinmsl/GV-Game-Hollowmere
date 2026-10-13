using System;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// A* search over world states. A node is a state (a set of facts), an edge is an action whose
    /// needs the state meets, and the cost of a path is the sum of its action costs.
    ///
    /// Heuristic: the goal facts still unmet, times the cheapest action cost, divided by the most
    /// goal facts any one action can set. No action can close the gap faster than that, so the
    /// estimate never exceeds the true cost (admissible) and never drops by more than one action's
    /// cost per step (consistent). A* therefore returns the cheapest plan, and a state never needs
    /// to be opened twice.
    ///
    /// Every array is allocated once. A search number marks which entries belong to the current
    /// search, so nothing is cleared between plans and planning allocates nothing.
    /// </summary>
    public sealed class GoapPlanner
    {
        /// <summary>Longest plan the planner will build.</summary>
        public const int MaxDepth = 9;
        public const int MaxActions = 16;

        const int States = WorldState.StateCount;

        readonly float[] costSoFar = new float[States];
        readonly ushort[] cameFrom = new ushort[States];
        readonly sbyte[] cameBy = new sbyte[States];
        readonly byte[] depth = new byte[States];
        readonly int[] reachedIn = new int[States];   // search number in which costSoFar was set
        readonly int[] closedIn = new int[States];    // search number in which the state was expanded
        int search;

        // Open list: a binary min-heap on f = g + h. A state is pushed again when a cheaper route to
        // it is found and the stale copy is skipped when popped, so the heap needs room for one push
        // per action per state.
        readonly float[] heapF = new float[States * MaxActions];
        readonly ushort[] heapState = new ushort[States * MaxActions];
        int heapCount;

        /// <summary>States taken off the open list in the last search. Shown in the debug view.</summary>
        public int NodesExpanded { get; private set; }

        /// <summary>Tests switch this off to get true cheapest costs (Dijkstra) to compare against.</summary>
        public bool UseHeuristic { get; set; } = true;

        /// <param name="costs">The cost of each action for this plan, worked out by the caller.</param>
        /// <param name="usable">Whether each action may be used in this plan.</param>
        /// <param name="plan">Receives the actions in order. Must hold MaxDepth entries.</param>
        /// <returns>False when no sequence of actions reaches the goal.</returns>
        public bool Plan(ushort start, Condition goal, BiscuitAction[] actions, int actionCount,
                         float[] costs, bool[] usable, BiscuitAction[] plan,
                         out int planLength, out float planCost)
        {
            if (actionCount > MaxActions) throw new ArgumentException("Too many actions for the planner.");

            search++;
            heapCount = 0;
            NodesExpanded = 0;
            planLength = 0;
            planCost = 0f;

            float scale = HeuristicScale(goal, actions, actionCount, costs, usable);

            reachedIn[start] = search;
            costSoFar[start] = 0f;
            depth[start] = 0;
            Push(start, goal.CountUnmet(start) * scale);

            while (heapCount > 0)
            {
                ushort state = Pop();
                if (closedIn[state] == search) continue;   // a stale copy
                closedIn[state] = search;
                NodesExpanded++;

                if (goal.IsMetBy(state))
                {
                    planCost = costSoFar[state];
                    planLength = depth[state];
                    for (int i = planLength - 1; i >= 0; i--)   // walk back from the goal
                    {
                        plan[i] = actions[cameBy[state]];
                        state = cameFrom[state];
                    }
                    return true;
                }

                if (depth[state] >= MaxDepth) continue;

                for (int a = 0; a < actionCount; a++)
                {
                    if (!usable[a] || !actions[a].Needs.IsMetBy(state)) continue;

                    ushort next = actions[a].Gives.ApplyTo(state);
                    if (next == state || closedIn[next] == search) continue;   // changes nothing, or done

                    float g = costSoFar[state] + costs[a];
                    if (reachedIn[next] == search && g >= costSoFar[next]) continue;   // no cheaper

                    reachedIn[next] = search;
                    costSoFar[next] = g;
                    cameFrom[next] = state;
                    cameBy[next] = (sbyte)a;
                    depth[next] = (byte)(depth[state] + 1);
                    Push(next, g + goal.CountUnmet(next) * scale);
                }
            }
            return false;
        }

        // Cheapest usable action cost per goal fact it can set. With every goal wanting one fact and
        // the cheapest action costing 1, h is simply "1 if the goal is unmet, else 0".
        float HeuristicScale(Condition goal, BiscuitAction[] actions, int actionCount, float[] costs, bool[] usable)
        {
            if (!UseHeuristic) return 0f;

            float cheapest = float.PositiveInfinity;
            int mostFactsSet = 0;
            for (int a = 0; a < actionCount; a++)
            {
                if (!usable[a]) continue;
                cheapest = Math.Min(cheapest, costs[a]);
                mostFactsSet = Math.Max(mostFactsSet, WorldState.CountBits(actions[a].Gives.Mask & goal.Mask));
            }
            if (mostFactsSet == 0 || float.IsInfinity(cheapest)) return 0f;
            return Math.Max(0f, cheapest) / mostFactsSet;
        }

        void Push(ushort state, float f)
        {
            if (heapCount == heapF.Length) return;   // cannot happen: see the size of the heap above

            int i = heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heapF[parent] <= f) break;
                heapF[i] = heapF[parent];
                heapState[i] = heapState[parent];
                i = parent;
            }
            heapF[i] = f;
            heapState[i] = state;
        }

        ushort Pop()
        {
            ushort top = heapState[0];
            heapCount--;
            if (heapCount == 0) return top;

            float f = heapF[heapCount];
            ushort last = heapState[heapCount];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= heapCount) break;
                if (child + 1 < heapCount && heapF[child + 1] < heapF[child]) child++;
                if (heapF[child] >= f) break;
                heapF[i] = heapF[child];
                heapState[i] = heapState[child];
                i = child;
            }
            heapF[i] = f;
            heapState[i] = last;
            return top;
        }
    }
}
