using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class BfsFleeTests
    {
        // A 9 x 7 room: walls all round, open inside.
        static GhostGrid Room() => TestGrids.FromRows(
            "#########",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#########");

        static SafeWallGoal AimEastFrom(GhostGrid grid, int cell, float halfAngle = 20f, float range = 15f) =>
            new SafeWallGoal(grid, grid.CellCentre(cell), Vector3.right, halfAngle, range);

        [Test]
        public void FindsTheNearestWallCellOutsideTheAim()
        {
            GhostGrid grid = Room();
            var bfs = new BfsField(grid);
            int ghost = TestGrids.Cell(grid, 6, 3);                      // east side of the room
            var goal = AimEastFrom(grid, TestGrids.Cell(grid, 2, 3));    // player in the west, aiming east

            int found = bfs.FindNearest(ghost, 10, ref goal);

            Assert.GreaterOrEqual(found, 0);
            Assert.IsTrue(goal.IsGoal(found), "a wall cell out of the aim");

            // Nothing safe is closer: check every cell by brute force (all cells inside, so moves = Chebyshev).
            int best = int.MaxValue;
            for (int c = 0; c < grid.Count; c++)
                if (goal.IsGoal(c)) best = Mathf.Min(best, TestGrids.Chebyshev(grid, ghost, c));
            Assert.AreEqual(best, bfs.Steps(found));
        }

        [Test]
        public void IgnoresTheWallStraightAheadInTheAim()
        {
            GhostGrid grid = Room();
            var bfs = new BfsField(grid);
            int ghost = TestGrids.Cell(grid, 7, 3);                      // right beside the east wall
            int eastWall = TestGrids.Cell(grid, 8, 3);
            var goal = AimEastFrom(grid, TestGrids.Cell(grid, 1, 3), halfAngle: 10f);

            int found = bfs.FindNearest(ghost, 10, ref goal);

            Assert.AreNotEqual(eastWall, found, "the nearest wall is in the aim: not safe");
            Assert.IsTrue(goal.IsGoal(found));
        }

        [Test]
        public void AlreadyInASafeWall_StaysPut()
        {
            GhostGrid grid = Room();
            var bfs = new BfsField(grid);
            int northWall = TestGrids.Cell(grid, 4, 0);
            var goal = AimEastFrom(grid, TestGrids.Cell(grid, 1, 3));

            Assert.AreEqual(northWall, bfs.FindNearest(northWall, 5, ref goal));
            Assert.AreEqual(0, bfs.Steps(northWall));
        }

        [Test]
        public void DepthLimit_ReturnsNoneWhenNothingSafeIsCloseEnough()
        {
            var grid = TestGrids.FromRows(
                "ooooooooo",
                "ooooooooo",
                "ooooooooo",
                "oooooooo#");
            var bfs = new BfsField(grid);
            var goal = new SafeWallGoal(grid, new Vector3(-50f, 1f, -50f), Vector3.left, 10f, 15f);   // aim far away
            int ghost = TestGrids.Cell(grid, 0, 0);

            Assert.AreEqual(-1, bfs.FindNearest(ghost, 3, ref goal), "the only wall is 8 moves away");
            Assert.AreEqual(TestGrids.Cell(grid, 8, 3), bfs.FindNearest(ghost, 8, ref goal));
        }

        [Test]
        public void TracePath_GivesConnectedMovesFromGhostToRefuge()
        {
            GhostGrid grid = Room();
            var bfs = new BfsField(grid);
            int ghost = TestGrids.Cell(grid, 4, 3);
            var goal = AimEastFrom(grid, TestGrids.Cell(grid, 1, 3));
            int found = bfs.FindNearest(ghost, 10, ref goal);
            var route = new int[32];

            int length = bfs.TracePath(found, route);

            Assert.AreEqual(bfs.Steps(found) + 1, length);
            Assert.AreEqual(ghost, route[0]);
            Assert.AreEqual(found, route[length - 1]);
            for (int i = 1; i < length; i++)
                Assert.AreEqual(1, TestGrids.Chebyshev(grid, route[i - 1], route[i]), "each move is to a neighbour");
        }

        [Test]
        public void GhostMovesThroughWalls_ThePlayerDoesNot()
        {
            var grid = TestGrids.FromRows(
                "#######",
                "#..#..#",
                "#..#..#",
                "#######");
            var bfs = new BfsField(grid);
            int west = TestGrids.Cell(grid, 1, 1), east = TestGrids.Cell(grid, 5, 1);

            bfs.BuildWalkingField(west);
            Assert.AreEqual(BfsField.Unreached, bfs.Steps(east), "the player cannot walk through the wall");

            var reachEast = new ReachCell(east);
            Assert.AreEqual(east, bfs.FindNearest(west, 10, ref reachEast), "a ghost goes straight through");
            Assert.AreEqual(4, bfs.Steps(east));
        }

        struct ReachCell : ICellGoal
        {
            readonly int target;
            public ReachCell(int target) => this.target = target;
            public bool IsGoal(int cell) => cell == target;
        }
    }
}
