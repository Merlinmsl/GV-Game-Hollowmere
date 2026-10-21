using System;
using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class AStarTests
    {
        static readonly float Root2 = Mathf.Sqrt(2f);

        static GhostGrid Open(int sizeX, int sizeZ) => TestGrids.FromRows(Rows('.', sizeX, sizeZ));

        static string[] Rows(char c, int sizeX, int sizeZ)
        {
            var rows = new string[sizeZ];
            for (int i = 0; i < sizeZ; i++) rows[i] = new string(c, sizeX);
            return rows;
        }

        static int CountSolid(GhostGrid grid, int[] route, int length)
        {
            int solid = 0;
            for (int i = 0; i < length; i++) if (grid.Is(route[i], CellFlags.Solid)) solid++;
            return solid;
        }

        // Reference answer: Dijkstra over every inside cell with the same step costs, O(n^2), no heuristic.
        static float DijkstraCost(InfluenceMap map, int start, int goal, RouteCosts costs)
        {
            GhostGrid grid = map.Grid;
            var dist = new float[grid.Count];
            var done = new bool[grid.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
            dist[start] = 0f;
            while (true)
            {
                int cell = -1;
                for (int i = 0; i < dist.Length; i++)
                    if (!done[i] && !float.IsPositiveInfinity(dist[i]) && (cell < 0 || dist[i] < dist[cell])) cell = i;
                if (cell < 0 || cell == goal) break;
                done[cell] = true;
                grid.Coords(cell, out int cx, out int cl, out int cz);
                for (int dl = -1; dl <= 1; dl++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.InBounds(cx + dx, cl + dl, cz + dz)) continue;
                    int next = grid.Index(cx + dx, cl + dl, cz + dz);
                    if (next == cell || !grid.Is(next, CellFlags.Inside)) continue;
                    if (dx != 0 && dz != 0 && (!grid.Is(grid.Index(cx + dx, cl + dl, cz), CellFlags.Inside) ||
                                               !grid.Is(grid.Index(cx, cl + dl, cz + dz), CellFlags.Inside))) continue;
                    float length = Vector3.Distance(grid.CellCentre(cell), grid.CellCentre(next));
                    float step = length * 0.5f * (costs.PerMetre(map, cell, 0f) + costs.PerMetre(map, next, 0f));
                    dist[next] = Mathf.Min(dist[next], dist[cell] + step);
                }
            }
            return dist[goal];
        }

        [Test]
        public void OpenRoom_ShortestRoute_IsDiagonalsThenStraight()
        {
            GhostGrid grid = Open(8, 6);
            var astar = new AStarSearch(new InfluenceMap(grid));

            Assert.AreEqual(RouteStatus.Found, astar.FindPath(grid.Index(0, 0, 0), grid.Index(5, 0, 3), RouteCosts.Shortest, 0f));

            Assert.AreEqual(3f * Root2 + 2f, astar.RouteCost, 1e-4f);
            Assert.AreEqual(6, astar.CopyRoute(new int[32]), "5 moves, 6 cells");
        }

        [Test]
        public void Cost_MatchesDijkstra_OnAHouseWithLightAndWalls()
        {
            var grid = TestGrids.FromRows(
                "##########",
                "#....#...#",
                "#.##.#.#.#",
                "#.#..#.#.#",
                "#.#.##.#.#",
                "#...#....#",
                "##########");
            var map = new InfluenceMap(grid);
            for (int c = 0; c < grid.Count; c++) map.SetBakedLight(c, (c * 37 % 11) / 10f);   // uneven, repeatable light
            var astar = new AStarSearch(map);
            int start = TestGrids.Cell(grid, 1, 1), goal = TestGrids.Cell(grid, 8, 5);

            foreach (RouteCosts costs in new[] { RouteCosts.Default, RouteCosts.Shortest })
            {
                Assert.AreEqual(RouteStatus.Found, astar.FindPath(start, goal, costs, 0f));
                Assert.AreEqual(DijkstraCost(map, start, goal, costs), astar.RouteCost, 1e-3f, "A* is optimal");
            }
        }

        [Test]
        public void GoesRoundALitWatchedRoom_ButStraightThroughWhenOnlyLengthCounts()
        {
            GhostGrid grid = Open(11, 7);
            var map = new InfluenceMap(grid);
            for (int row = 1; row <= 5; row++)
            for (int x = 3; x <= 7; x++) map.SetBakedLight(TestGrids.Cell(grid, x, row), 1f);   // a lit block in the middle
            map.MarkWatched(grid.CellCentre(TestGrids.Cell(grid, 5, 3)), Vector3.right, now: 0f, range: 3f, coneDegrees: 360f);
            var astar = new AStarSearch(map);
            int start = TestGrids.Cell(grid, 0, 3), goal = TestGrids.Cell(grid, 10, 3);
            var route = new int[64];

            astar.FindPath(start, goal, RouteCosts.Default, 0f);
            int length = astar.CopyRoute(route);
            for (int i = 0; i < length; i++)
                Assert.AreEqual(0f, map.Light(route[i]), "the careful route keeps to the dark edge");

            astar.FindPath(start, goal, RouteCosts.Shortest, 0f);
            Assert.AreEqual(11, astar.CopyRoute(route), "straight across");
        }

        [Test]
        public void PrefersTravellingInsideWalls()
        {
            var grid = TestGrids.FromRows(
                "#########",
                ".........",
                ".........");
            var astar = new AStarSearch(new InfluenceMap(grid));
            var route = new int[32];
            int start = TestGrids.Cell(grid, 0, 1), goal = TestGrids.Cell(grid, 8, 1);

            astar.FindPath(start, goal, new RouteCosts { solidFactor = 0.5f }, 0f);
            Assert.GreaterOrEqual(CountSolid(grid, route, astar.CopyRoute(route)), 5, "slips along inside the wall");

            astar.FindPath(start, goal, RouteCosts.Shortest, 0f);
            Assert.AreEqual(0, CountSolid(grid, route, astar.CopyRoute(route)), "no reason to when walls cost the same");
        }

        [Test]
        public void GhostRoute_PassesThroughWalls_ButNeverLeavesTheHouse()
        {
            var grid = TestGrids.FromRows(
                "#######   ",
                "#..#..#   ",
                "#..#..#   ",
                "#######   ");
            var astar = new AStarSearch(new InfluenceMap(grid));
            var route = new int[32];

            Assert.AreEqual(RouteStatus.Found, astar.FindPath(TestGrids.Cell(grid, 1, 1), TestGrids.Cell(grid, 5, 1), RouteCosts.Default, 0f));
            int length = astar.CopyRoute(route);
            for (int i = 0; i < length; i++) Assert.IsTrue(grid.Is(route[i], CellFlags.Inside));

            Assert.AreEqual(RouteStatus.Failed, astar.FindPath(TestGrids.Cell(grid, 1, 1), TestGrids.Cell(grid, 8, 1), RouteCosts.Default, 0f),
                            "a goal outside the house");
        }

        [Test]
        public void Diagonals_DoNotCutPastTheOutsideOfTheHouse()
        {
            var grid = TestGrids.FromRows(
                "..  ",
                "....");
            var astar = new AStarSearch(new InfluenceMap(grid));

            astar.FindPath(TestGrids.Cell(grid, 1, 0), TestGrids.Cell(grid, 2, 1), RouteCosts.Shortest, 0f);

            Assert.AreEqual(2f, astar.RouteCost, 1e-4f, "down then across, not diagonally past the outside corner");
        }

        [Test]
        public void ChangesStorey_ThroughTheFloor()
        {
            var flags = new CellFlags[2 * 5 * 5];
            for (int i = 0; i < flags.Length; i++) flags[i] = CellFlags.Inside | CellFlags.Walkable;
            var grid = new GhostGrid(Vector3.zero, 1f, 5, 5, new[] { 0f, 4f }, flags);
            var astar = new AStarSearch(new InfluenceMap(grid));
            var route = new int[32];

            Assert.AreEqual(RouteStatus.Found, astar.FindPath(grid.Index(0, 0, 0), grid.Index(4, 1, 0), RouteCosts.Shortest, 0f));

            int length = astar.CopyRoute(route);
            grid.Coords(route[length - 1], out _, out int layer, out _);
            Assert.AreEqual(1, layer);
            // Three steps across, then one diagonal step across and 4 m up through the floor.
            Assert.AreEqual(3f + Mathf.Sqrt(1f + 16f), astar.RouteCost, 1e-4f);
        }

        [Test]
        public void TimeSlicing_RespectsTheBudget_AndGivesTheSameRoute()
        {
            GhostGrid grid = Open(30, 30);
            var map = new InfluenceMap(grid);
            for (int c = 0; c < grid.Count; c++) map.SetBakedLight(c, (c * 13 % 7) / 6f);
            var astar = new AStarSearch(map);
            int start = grid.Index(0, 0, 0), goal = grid.Index(29, 0, 17);

            astar.FindPath(start, goal, RouteCosts.Default, 0f);
            float oneShot = astar.RouteCost;
            int oneShotExpanded = astar.NodesExpanded;

            astar.Begin(start, goal, RouteCosts.Default, 0f);
            int previous = 0;
            while (astar.Step(25) == RouteStatus.Searching)
            {
                Assert.LessOrEqual(astar.NodesExpanded - previous, 25, "never more than the budget per slice");
                previous = astar.NodesExpanded;
            }

            Assert.AreEqual(RouteStatus.Found, astar.Status);
            Assert.AreEqual(oneShot, astar.RouteCost, 1e-4f);
            Assert.AreEqual(oneShotExpanded, astar.NodesExpanded);
            Assert.Greater(astar.Slices, 1);
        }

        [Test]
        public void NodeCap_GivesAPartialRouteTowardsTheGoal()
        {
            GhostGrid grid = Open(40, 3);
            var astar = new AStarSearch(new InfluenceMap(grid));
            int start = grid.Index(0, 0, 1), goal = grid.Index(39, 0, 1);

            Assert.AreEqual(RouteStatus.Partial, astar.FindPath(start, goal, RouteCosts.Default, 0f, maxNodes: 30));

            grid.Coords(astar.End, out int endX, out _, out _);
            Assert.Greater(endX, 5, "got well on the way");
            Assert.AreEqual(start, AStarRouteStart(astar));
        }

        static int AStarRouteStart(AStarSearch astar)
        {
            var route = new int[64];
            return astar.CopyRoute(route) > 0 ? route[0] : -1;
        }

        [Test]
        public void HeuristicKeepsTheSearchFocused()
        {
            GhostGrid grid = Open(40, 40);
            var astar = new AStarSearch(new InfluenceMap(grid));

            astar.FindPath(grid.Index(0, 0, 20), grid.Index(39, 0, 20), RouteCosts.Shortest, 0f);

            Assert.Less(astar.NodesExpanded, grid.Count / 4, "Dijkstra would expand most of the 1600 cells");
        }

        [Test]
        public void Search_AllocatesNothingAfterWarmUp()
        {
            GhostGrid grid = Open(30, 30);
            var astar = new AStarSearch(new InfluenceMap(grid));
            var route = new int[128];
            astar.FindPath(grid.Index(0, 0, 0), grid.Index(29, 0, 29), RouteCosts.Default, 0f);
            astar.CopyRoute(route);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                astar.Begin(grid.Index(0, 0, i), grid.Index(29, 0, 29 - i), RouteCosts.Default, 0f);
                while (astar.Step(50) == RouteStatus.Searching) { }
                astar.CopyRoute(route);
            }
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
