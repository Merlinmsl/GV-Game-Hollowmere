using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    public enum RouteStatus
    {
        Idle,
        Searching,   // started, more slices needed
        Found,       // the cheapest route to the goal
        Partial,     // node cap reached or goal unreachable: route to the expanded cell nearest the goal
        Failed,      // nothing usable (bad goal, or no progress at all)
    }

    /// <summary>
    /// A* over the ghost grid: the cheapest route for a ghost, which moves through any inside cell of the
    /// house (26 neighbours, through walls and floors; a diagonal may not cut past an outside cell, so the
    /// route never grazes the outside of the house). Each step costs its length times how exposed the
    /// two cells are (RouteCosts), so "cheapest" means "least seen", not "shortest".
    ///  - Heuristic: straight-line distance times the cheapest cost per metre. It never overestimates and it
    ///    is consistent, so the first time the goal is taken off the open list its route is optimal and no
    ///    closed cell ever needs reopening.
    ///  - Open list: a binary min-heap on f = g + h with decrease-key, O(log n) per operation.
    ///  - Time slicing: Begin, then Step(budget) expands at most 'budget' cells and returns, so a long search
    ///    is spread over several ticks instead of spiking one frame. A total node cap ends it as Partial.
    /// Costs read the influence map at the time given to Begin. Stamp arrays: allocation-free after construction.
    /// </summary>
    public sealed class AStarSearch
    {
        readonly InfluenceMap map;
        readonly GhostGrid grid;
        readonly float[] g;
        readonly float[] f;
        readonly int[] parent;
        readonly int[] seenStamp;       // has a g value in this search
        readonly int[] closedStamp;     // expanded in this search
        readonly int[] heapIndex;       // where a cell sits in the heap, for decrease-key
        readonly int[] heap;
        int heapCount;
        int stamp;

        RouteCosts costs;
        float now;
        float cheapest;
        int maxNodes;
        Vector3 goalCentre;
        int bestCell;                   // expanded cell nearest the goal, for a partial route
        float bestH;

        public RouteStatus Status { get; private set; }
        public int Start { get; private set; } = -1;
        public int Goal { get; private set; } = -1;
        /// <summary>Cells expanded so far in this search, over all slices.</summary>
        public int NodesExpanded { get; private set; }
        /// <summary>Calls to Step this search took.</summary>
        public int Slices { get; private set; }
        /// <summary>The cell the route ends at: the goal when Found, the nearest reached cell when Partial.</summary>
        public int End { get; private set; } = -1;

        public AStarSearch(InfluenceMap map)
        {
            this.map = map;
            grid = map.Grid;
            int n = grid.Count;
            g = new float[n];
            f = new float[n];
            parent = new int[n];
            seenStamp = new int[n];
            closedStamp = new int[n];
            heapIndex = new int[n];
            heap = new int[n];
        }

        public InfluenceMap Map => map;

        /// <summary>Cost of the route found (to End), or infinity.</summary>
        public float RouteCost => End >= 0 ? g[End] : float.PositiveInfinity;

        /// <summary>Starts a new search; nothing is expanded until Step.</summary>
        public void Begin(int start, int goal, RouteCosts costs, float now, int maxNodes = 4000)
        {
            stamp++;
            heapCount = 0;
            NodesExpanded = 0;
            Slices = 0;
            Start = start;
            Goal = goal;
            End = -1;
            this.costs = costs;
            this.now = now;
            this.maxNodes = Mathf.Max(1, maxNodes);
            cheapest = costs.CheapestPerMetre;

            if (start < 0 || goal < 0 || !grid.Is(goal, CellFlags.Inside))
            {
                Status = RouteStatus.Failed;
                return;
            }

            goalCentre = grid.CellCentre(goal);
            bestCell = start;
            bestH = Heuristic(start);
            seenStamp[start] = stamp;
            g[start] = 0f;
            parent[start] = -1;
            Push(start, bestH);
            Status = RouteStatus.Searching;
        }

        /// <summary>Expands at most 'budget' cells. Returns Searching until the search ends.</summary>
        public RouteStatus Step(int budget)
        {
            if (Status != RouteStatus.Searching) return Status;
            Slices++;

            for (int expanded = 0; expanded < budget; expanded++)
            {
                if (heapCount == 0) return Finish(RouteStatus.Partial);     // goal unreachable
                int cell = Pop();
                if (cell == Goal)
                {
                    End = cell;
                    Status = RouteStatus.Found;
                    return Status;
                }

                closedStamp[cell] = stamp;
                NodesExpanded++;
                float h = f[cell] - g[cell];
                if (h < bestH) { bestH = h; bestCell = cell; }
                if (NodesExpanded >= maxNodes) return Finish(RouteStatus.Partial);

                Expand(cell);
            }
            return Status;
        }

        /// <summary>Begin plus Step until done: for tests and callers that do not need slicing.</summary>
        public RouteStatus FindPath(int start, int goal, RouteCosts costs, float now, int maxNodes = int.MaxValue)
        {
            Begin(start, goal, costs, now, maxNodes);
            return Step(int.MaxValue);
        }

        public void Cancel() => Status = RouteStatus.Idle;

        /// <summary>
        /// The route from Start to End (start first) written into 'route'. Returns its length, or 0 when
        /// there is no route or the buffer is too small.
        /// </summary>
        public int CopyRoute(int[] route)
        {
            if (End < 0) return 0;
            int length = 0;
            for (int c = End; c >= 0; c = parent[c]) length++;
            if (length > route.Length) return 0;
            int i = length - 1;
            for (int c = End; c >= 0; c = parent[c]) route[i--] = c;
            return length;
        }

        // ---------------------------------------------------------------- internals

        RouteStatus Finish(RouteStatus status)
        {
            if (bestCell == Start) { Status = RouteStatus.Failed; return Status; }
            End = bestCell;
            Status = status;
            return Status;
        }

        void Expand(int cell)
        {
            grid.Coords(cell, out int cx, out int cl, out int cz);
            float cellPerMetre = costs.PerMetre(map, cell, now);

            for (int dl = -1; dl <= 1; dl++)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0 && dl == 0) continue;
                int x = cx + dx, l = cl + dl, z = cz + dz;
                if (!grid.InBounds(x, l, z)) continue;
                int next = grid.Index(x, l, z);
                if (closedStamp[next] == stamp || !grid.Is(next, CellFlags.Inside)) continue;
                // No diagonal squeeze past the edge of the house: both cells beside the step must be inside.
                if (dx != 0 && dz != 0 &&
                    (!grid.Is(grid.Index(cx + dx, l, cz), CellFlags.Inside) || !grid.Is(grid.Index(cx, l, cz + dz), CellFlags.Inside)))
                    continue;

                float hx = dx * grid.CellSize, hz = dz * grid.CellSize, hy = grid.FloorY[l] - grid.FloorY[cl];
                float length = Mathf.Sqrt(hx * hx + hy * hy + hz * hz);
                // Half the step is in each cell, so each pays its own exposure.
                float step = length * 0.5f * (cellPerMetre + costs.PerMetre(map, next, now));
                float tentative = g[cell] + step;

                if (seenStamp[next] == stamp)
                {
                    if (tentative >= g[next]) continue;
                    g[next] = tentative;
                    parent[next] = cell;
                    f[next] = tentative + Heuristic(next);
                    SiftUp(heapIndex[next]);                       // decrease-key
                }
                else
                {
                    seenStamp[next] = stamp;
                    g[next] = tentative;
                    parent[next] = cell;
                    Push(next, tentative + Heuristic(next));
                }
            }
        }

        float Heuristic(int cell) => Vector3.Distance(grid.CellCentre(cell), goalCentre) * cheapest;

        // ---------------------------------------------------------------- binary heap on f

        void Push(int cell, float fValue)
        {
            f[cell] = fValue;
            heap[heapCount] = cell;
            heapIndex[cell] = heapCount;
            heapCount++;
            SiftUp(heapCount - 1);
        }

        int Pop()
        {
            int top = heap[0];
            heapCount--;
            if (heapCount > 0)
            {
                heap[0] = heap[heapCount];
                heapIndex[heap[0]] = 0;
                SiftDown(0);
            }
            return top;
        }

        void SiftUp(int i)
        {
            int cell = heap[i];
            float key = f[cell];
            while (i > 0)
            {
                int up = (i - 1) >> 1;
                if (f[heap[up]] <= key) break;
                heap[i] = heap[up];
                heapIndex[heap[i]] = i;
                i = up;
            }
            heap[i] = cell;
            heapIndex[cell] = i;
        }

        void SiftDown(int i)
        {
            int cell = heap[i];
            float key = f[cell];
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= heapCount) break;
                if (child + 1 < heapCount && f[heap[child + 1]] < f[heap[child]]) child++;
                if (f[heap[child]] >= key) break;
                heap[i] = heap[child];
                heapIndex[heap[i]] = i;
                i = child;
            }
            heap[i] = cell;
            heapIndex[cell] = i;
        }
    }
}
