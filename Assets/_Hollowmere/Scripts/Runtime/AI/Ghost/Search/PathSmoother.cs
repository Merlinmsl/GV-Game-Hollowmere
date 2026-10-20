using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Turns an A* cell route (a zig-zag of cell centres) into a few straight legs: string pulling.
    /// From each kept waypoint it looks ahead along the route and keeps going while the straight line to
    /// the next route cell is still "as good as the route": every cell the line crosses is inside the house
    /// and no more exposed than the most exposed cell of that stretch of route. So smoothing never cuts
    /// across a lit or watched patch that A* went round, and never leaves the house. O(route length x
    /// cells per line); allocation-free.
    /// </summary>
    public sealed class PathSmoother
    {
        // Small slack so tiny differences in exposure do not stop a straight line.
        const float Tolerance = 0.02f;

        readonly InfluenceMap map;
        readonly GhostGrid grid;

        public PathSmoother(InfluenceMap map)
        {
            this.map = map;
            grid = map.Grid;
        }

        /// <summary>
        /// Writes the smoothed waypoints (cell centres, first = route start, last = route end) into
        /// 'waypoints' and returns how many. A route longer than the buffer keeps its last waypoint.
        /// </summary>
        public int Smooth(int[] route, int length, RouteCosts costs, float now, Vector3[] waypoints)
        {
            if (length <= 0 || waypoints.Length == 0) return 0;
            int count = 0;
            waypoints[count++] = grid.CellCentre(route[0]);
            if (length == 1) return count;

            int anchor = 0;
            while (anchor < length - 1)
            {
                // Walk ahead while the straight line from the anchor stays as good as the route.
                int reach = anchor + 1;
                float worst = Mathf.Max(costs.PerMetre(map, route[anchor], now), costs.PerMetre(map, route[reach], now));
                for (int j = anchor + 2; j < length; j++)
                {
                    worst = Mathf.Max(worst, costs.PerMetre(map, route[j], now));
                    if (!LineIsAsGood(route[anchor], route[j], worst, costs, now)) break;
                    reach = j;
                }

                if (count == waypoints.Length) count--;           // out of room: overwrite, keep the end
                waypoints[count++] = grid.CellCentre(route[reach]);
                anchor = reach;
            }
            return count;
        }

        /// <summary>
        /// Does the straight line between two cells cross only inside cells no more exposed than 'limit'?
        /// Walks every cell the line touches across the floor plan, exactly (a grid traversal, Amanatides
        /// and Woo), not by sampling, so a line grazing a corner cannot slip past it. Where the line passes
        /// exactly through a corner, both cells beside it must be good too.
        /// </summary>
        public bool LineIsAsGood(int from, int to, float limit, RouteCosts costs, float now)
        {
            Vector3 a = grid.CellCentre(from), b = grid.CellCentre(to);
            grid.Coords(from, out int x, out _, out int z);
            grid.Coords(to, out int endX, out _, out int endZ);

            float dx = (b.x - a.x) / grid.CellSize, dz = (b.z - a.z) / grid.CellSize;
            int stepX = dx > 0f ? 1 : -1, stepZ = dz > 0f ? 1 : -1;
            // Cell centres sit half a cell from every edge, so the first edge on each axis is half a step away.
            float deltaX = dx != 0f ? 1f / Mathf.Abs(dx) : float.PositiveInfinity;
            float deltaZ = dz != 0f ? 1f / Mathf.Abs(dz) : float.PositiveInfinity;
            float nextX = deltaX * 0.5f, nextZ = deltaZ * 0.5f;

            int guard = grid.SizeX + grid.SizeZ + 2;
            while ((x != endX || z != endZ) && guard-- > 0)
            {
                float t;
                if (Mathf.Abs(nextX - nextZ) < 1e-5f)
                {
                    // Exactly through a corner: the two cells either side of it count as touched.
                    t = nextX;
                    if (!Good(x + stepX, z, LayerOnLine(a, b, t), limit, costs, now) ||
                        !Good(x, z + stepZ, LayerOnLine(a, b, t), limit, costs, now)) return false;
                    x += stepX; z += stepZ;
                    nextX += deltaX; nextZ += deltaZ;
                }
                else if (nextX < nextZ) { t = nextX; x += stepX; nextX += deltaX; }
                else { t = nextZ; z += stepZ; nextZ += deltaZ; }

                if (!Good(x, z, LayerOnLine(a, b, t), limit, costs, now)) return false;
            }

            // A change of storey along the line (through a floor) must also pass at the far end's storey.
            grid.Coords(to, out _, out int endLayer, out _);
            return Good(endX, endZ, endLayer, limit, costs, now);
        }

        bool Good(int x, int z, int layer, float limit, RouteCosts costs, float now)
        {
            if (!grid.InBounds(x, layer, z)) return false;
            int cell = grid.Index(x, layer, z);
            return grid.Is(cell, CellFlags.Inside) && costs.PerMetre(map, cell, now) <= limit + Tolerance;
        }

        // The storey at a fraction along the line. A height between two storeys belongs to the lower one
        // until it is near the upper floor (GhostGrid.LayerAt), so a line through a floor changes storey once.
        int LayerOnLine(Vector3 a, Vector3 b, float t) =>
            grid.LayerAt(Mathf.Lerp(a.y, b.y, Mathf.Clamp01(t)) - GhostGrid.BodyHeight + 0.01f);
    }
}
