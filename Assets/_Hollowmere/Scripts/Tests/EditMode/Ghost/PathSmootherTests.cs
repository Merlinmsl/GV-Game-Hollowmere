using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class PathSmootherTests
    {
        static int Plan(InfluenceMap map, int start, int goal, RouteCosts costs, int[] route)
        {
            var astar = new AStarSearch(map);
            Assert.AreEqual(RouteStatus.Found, astar.FindPath(start, goal, costs, 0f));
            return astar.CopyRoute(route);
        }

        [Test]
        public void OpenRoom_BecomesOneStraightLeg()
        {
            var grid = TestGrids.FromRows(
                "..........",
                "..........",
                "..........",
                "..........",
                "..........");
            var map = new InfluenceMap(grid);
            var route = new int[64];
            var waypoints = new Vector3[16];
            int start = TestGrids.Cell(grid, 0, 0), goal = TestGrids.Cell(grid, 9, 3);
            int length = Plan(map, start, goal, RouteCosts.Default, route);

            int count = new PathSmoother(map).Smooth(route, length, RouteCosts.Default, 0f, waypoints);

            Assert.Greater(length, 2, "the cell route zig-zags");
            Assert.AreEqual(2, count);
            Assert.AreEqual(grid.CellCentre(start), waypoints[0]);
            Assert.AreEqual(grid.CellCentre(goal), waypoints[1]);
        }

        [Test]
        public void LShapedHouse_KeepsTheCorner_AndNeverLeavesTheHouse()
        {
            var grid = TestGrids.FromRows(
                "...     ",
                "...     ",
                "...     ",
                "........",
                "........");
            var map = new InfluenceMap(grid);
            var route = new int[64];
            var waypoints = new Vector3[16];
            int length = Plan(map, TestGrids.Cell(grid, 1, 0), TestGrids.Cell(grid, 7, 4), RouteCosts.Default, route);
            var smoother = new PathSmoother(map);

            int count = smoother.Smooth(route, length, RouteCosts.Default, 0f, waypoints);

            Assert.That(count, Is.InRange(3, 4), "start, round the inside corner, the end");
            Assert.Less(count, length, "fewer waypoints than cells");
            AssertLegsStayInside(grid, waypoints, count);
        }

        [Test]
        public void NeverCutsAcrossTheLitPatchTheRouteWentRound()
        {
            var grid = TestGrids.FromRows(
                "...........",
                "...........",
                "...........",
                "...........",
                "...........");
            var map = new InfluenceMap(grid);
            for (int row = 0; row <= 3; row++)
            for (int x = 4; x <= 6; x++) map.SetBakedLight(TestGrids.Cell(grid, x, row), 1f);   // lit, open only along the south edge
            var route = new int[64];
            var waypoints = new Vector3[16];
            RouteCosts costs = RouteCosts.Default;
            int length = Plan(map, TestGrids.Cell(grid, 0, 0), TestGrids.Cell(grid, 10, 0), costs, route);

            int count = new PathSmoother(map).Smooth(route, length, costs, 0f, waypoints);

            Assert.Greater(count, 2, "it has to bend round the light");
            for (int i = 0; i < count - 1; i++)
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    int cell = grid.CellAt(Vector3.Lerp(waypoints[i], waypoints[i + 1], t));
                    Assert.AreEqual(0f, map.Light(cell), "a smoothed leg crossed the lit patch");
                }
        }

        [Test]
        public void TooFewWaypointSlots_StillEndsAtTheGoal()
        {
            var grid = TestGrids.FromRows(
                "..#..#..#..",
                "..#..#..#..",
                "...........");
            var map = new InfluenceMap(grid);
            for (int c = 0; c < grid.Count; c++) if (grid.Is(c, CellFlags.Solid)) map.SetBakedLight(c, 1f);   // walls glow: go round
            var route = new int[64];
            var waypoints = new Vector3[2];
            int goal = TestGrids.Cell(grid, 10, 0);
            int length = Plan(map, TestGrids.Cell(grid, 0, 0), goal, RouteCosts.Default, route);

            int count = new PathSmoother(map).Smooth(route, length, RouteCosts.Default, 0f, waypoints);

            Assert.AreEqual(2, count);
            Assert.AreEqual(grid.CellCentre(goal), waypoints[1]);
        }

        static void AssertLegsStayInside(GhostGrid grid, Vector3[] waypoints, int count)
        {
            for (int i = 0; i < count - 1; i++)
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    int cell = grid.CellAt(Vector3.Lerp(waypoints[i], waypoints[i + 1], t));
                    Assert.IsTrue(cell >= 0 && grid.Is(cell, CellFlags.Inside), "a smoothed leg left the house");
                }
        }
    }
}
