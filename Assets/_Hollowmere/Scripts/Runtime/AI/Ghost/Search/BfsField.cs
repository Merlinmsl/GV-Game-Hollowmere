using System;

namespace Hollowmere.AI.Ghost
{
    /// <summary>What a breadth-first search is looking for. A struct goal keeps the search allocation-free.</summary>
    public interface ICellGoal
    {
        bool IsGoal(int cell);
    }

    /// <summary>
    /// Breadth-first search over the ghost grid, used two ways:
    ///  1. A WALKING-DISTANCE FIELD: from the player's believed cell, over the cells the player can walk,
    ///     the number of moves to every reachable cell. Every move costs the same, so BFS's first visit is
    ///     the shortest route, and one pass answers "how far would the player have to walk to get here?"
    ///     for every candidate cell at once: O(cells reached).
    ///  2. NEAREST-GOAL: from the ghost, through any cell of the house (walls too), the first cell that
    ///     passes a goal test, such as "inside a wall and out of the ice gun's aim". The ring-by-ring order
    ///     guarantees the first match is the nearest; a depth limit caps the cost.
    /// Visited cells are tagged with a stamp instead of clearing arrays, so a search costs only what it
    /// reaches, never the size of the grid. Allocation-free after construction.
    /// </summary>
    public sealed class BfsField
    {
        public const int Unreached = -1;

        readonly GhostGrid grid;
        readonly int[] steps;
        readonly int[] parent;
        readonly int[] stamp;
        readonly int[] queue;
        int currentStamp;

        /// <summary>The cell the last search started from, or -1.</summary>
        public int Source { get; private set; } = -1;

        /// <summary>Cells the last search reached: its cost, for the profiler and the debug view.</summary>
        public int CellsVisited { get; private set; }

        public BfsField(GhostGrid grid)
        {
            this.grid = grid;
            steps = new int[grid.Count];
            parent = new int[grid.Count];
            stamp = new int[grid.Count];
            queue = new int[grid.Count];
        }

        public GhostGrid Grid => grid;

        /// <summary>Moves from the source to this cell in the last search, or Unreached.</summary>
        public int Steps(int cell) => cell >= 0 && stamp[cell] == currentStamp ? steps[cell] : Unreached;

        /// <summary>Walking distance in metres from the source, or infinity when unreachable.</summary>
        public float WalkingDistance(int cell)
        {
            int s = Steps(cell);
            return s < 0 ? float.PositiveInfinity : s * grid.CellSize;
        }

        /// <summary>
        /// Does the field need rebuilding for a new believed cell? Only when it moved more than one cell or
        /// changed storey: one cell of drift changes every distance by at most one move.
        /// </summary>
        public bool NeedsRebuild(int newSource)
        {
            if (Source < 0 || newSource < 0) return true;
            grid.Coords(Source, out int ax, out int al, out int az);
            grid.Coords(newSource, out int bx, out int bl, out int bz);
            return al != bl || Math.Max(Math.Abs(ax - bx), Math.Abs(az - bz)) > 1;
        }

        // ---------------------------------------------------------------- 1. walking-distance field

        /// <summary>
        /// Walking distance from 'source' over the player's walkable cells. 8 neighbours on a storey, plus
        /// the storeys above and below where a stair or ramp keeps the step small. Diagonals may not cut a
        /// wall's corner. A source the player cannot stand on starts from the nearest walkable cell.
        /// Returns the number of cells reached.
        /// </summary>
        public int BuildWalkingField(int source, int maxSteps = int.MaxValue)
        {
            source = grid.NearestWalkable(source);
            if (!Begin(source)) return 0;

            int head = 0, tail = 0;
            queue[tail++] = source;
            while (head < tail)
            {
                int cell = queue[head++];
                int next = steps[cell] + 1;
                if (next > maxSteps) continue;

                grid.Coords(cell, out int cx, out int cl, out int cz);
                for (int dl = -1; dl <= 1; dl++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0 && dl == 0) continue;
                    int x = cx + dx, l = cl + dl, z = cz + dz;
                    if (!grid.InBounds(x, l, z)) continue;
                    int neighbour = grid.Index(x, l, z);
                    if (stamp[neighbour] == currentStamp || !grid.CanStep(cell, neighbour)) continue;

                    // No squeezing diagonally past a corner: both side cells must be walkable too.
                    if (dx != 0 && dz != 0 &&
                        (!grid.CanStep(cell, grid.Index(cx + dx, l, cz)) || !grid.CanStep(cell, grid.Index(cx, l, cz + dz))))
                        continue;

                    Visit(neighbour, cell, next);
                    queue[tail++] = neighbour;
                }
            }
            CellsVisited = tail;
            return tail;
        }

        // ---------------------------------------------------------------- 2. nearest goal (ghost moves)

        /// <summary>
        /// The nearest cell passing 'goal', moving through any inside cell of the house (26 neighbours: a
        /// ghost goes through walls and floors), at most 'maxDepth' moves away. -1 when none is that close.
        /// The source itself counts, so a ghost already in a safe place stays put.
        /// </summary>
        public int FindNearest<TGoal>(int source, int maxDepth, ref TGoal goal) where TGoal : struct, ICellGoal
        {
            if (!Begin(source)) return -1;

            int head = 0, tail = 0;
            queue[tail++] = source;
            while (head < tail)
            {
                int cell = queue[head++];
                if (goal.IsGoal(cell)) { CellsVisited = tail; return cell; }

                int next = steps[cell] + 1;
                if (next > maxDepth) continue;

                grid.Coords(cell, out int cx, out int cl, out int cz);
                for (int dl = -1; dl <= 1; dl++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0 && dl == 0) continue;
                    int x = cx + dx, l = cl + dl, z = cz + dz;
                    if (!grid.InBounds(x, l, z)) continue;
                    int neighbour = grid.Index(x, l, z);
                    if (stamp[neighbour] == currentStamp || !grid.Is(neighbour, CellFlags.Inside)) continue;
                    Visit(neighbour, cell, next);
                    queue[tail++] = neighbour;
                }
            }
            CellsVisited = tail;
            return -1;
        }

        // ---------------------------------------------------------------- routes

        /// <summary>
        /// The route from the last search's source to 'cell', source first, written into 'route'.
        /// Returns its length, or 0 when the cell was not reached or the buffer is too small.
        /// </summary>
        public int TracePath(int cell, int[] route)
        {
            int length = Steps(cell) + 1;
            if (length <= 0 || length > route.Length) return 0;
            for (int i = length - 1; i >= 0; i--)
            {
                route[i] = cell;
                cell = parent[cell];
            }
            return length;
        }

        // ---------------------------------------------------------------- helpers

        bool Begin(int source)
        {
            CellsVisited = 0;
            currentStamp++;
            Source = source;
            if (source < 0) return false;
            Visit(source, -1, 0);
            return true;
        }

        void Visit(int cell, int from, int moves)
        {
            stamp[cell] = currentStamp;
            steps[cell] = moves;
            parent[cell] = from;
        }
    }
}
