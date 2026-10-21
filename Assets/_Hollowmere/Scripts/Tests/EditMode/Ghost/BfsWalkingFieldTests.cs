using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class BfsWalkingFieldTests
    {
        [Test]
        public void OpenRoom_StepsAreChebyshevDistance()
        {
            var grid = TestGrids.FromRows(
                ".......",
                ".......",
                ".......",
                ".......",
                ".......",
                ".......",
                ".......");
            var bfs = new BfsField(grid);
            int centre = TestGrids.Cell(grid, 3, 3);

            int reached = bfs.BuildWalkingField(centre);

            Assert.AreEqual(49, reached);
            Assert.AreEqual(0, bfs.Steps(centre));
            Assert.AreEqual(1, bfs.Steps(TestGrids.Cell(grid, 4, 4)), "a diagonal neighbour is one move");
            Assert.AreEqual(3, bfs.Steps(TestGrids.Cell(grid, 0, 0)), "a corner is three moves away");
            Assert.AreEqual(3f, bfs.WalkingDistance(TestGrids.Cell(grid, 6, 3)), 1e-5f);
        }

        [Test]
        public void ADiagonalMove_CountsItsRealLength()
        {
            var grid = TestGrids.FromRows(
                ".......",
                ".......",
                ".......",
                ".......",
                ".......",
                ".......",
                ".......");
            var bfs = new BfsField(grid);
            int centre = TestGrids.Cell(grid, 3, 3);

            bfs.BuildWalkingField(centre);

            Assert.AreEqual(1.4f, bfs.WalkingDistance(TestGrids.Cell(grid, 4, 4)), 1e-5f, "one diagonal");
            Assert.AreEqual(4.2f, bfs.WalkingDistance(TestGrids.Cell(grid, 0, 0)), 1e-5f, "three diagonals, not 3 m");
            Assert.AreEqual(3.4f, bfs.WalkingDistance(TestGrids.Cell(grid, 6, 4)), 1e-5f, "two straight and one diagonal");

            // Never more than 1% short of the straight line (1.4 for sqrt 2), and at most the 8-direction
            // detour (about 8% longer). Plain BFS was up to 29% short.
            for (int i = 0; i < grid.Count; i++)
            {
                float straightLine = Vector3.Distance(grid.CellCentre(i), grid.CellCentre(centre));
                float walked = bfs.WalkingDistance(i);
                Assert.GreaterOrEqual(walked, straightLine * 0.985f, "cell " + i);
                Assert.LessOrEqual(walked, straightLine * 1.09f + 1e-4f, "cell " + i);
            }
        }

        [Test]
        public void MaxSteps_IsADistance_SoDiagonalsRunOutSooner()
        {
            var grid = TestGrids.FromRows(
                "...",
                "...",
                "...");
            var bfs = new BfsField(grid);

            bfs.BuildWalkingField(TestGrids.Cell(grid, 0, 2), maxSteps: 2);

            Assert.AreEqual(2f, bfs.WalkingDistance(TestGrids.Cell(grid, 2, 2)), 1e-5f, "2 m straight: in");
            Assert.AreEqual(1.4f, bfs.WalkingDistance(TestGrids.Cell(grid, 1, 1)), 1e-5f, "one diagonal: in");
            Assert.AreEqual(BfsField.Unreached, bfs.Steps(TestGrids.Cell(grid, 2, 0)), "two diagonals are 2.8 m: out");
        }

        [Test]
        public void Wall_ForcesDetourThroughDoorway_WithoutCuttingCorners()
        {
            var grid = TestGrids.FromRows(
                "#########",
                "#...#...#",
                "#...#...#",
                "#.......#",     // the doorway is x = 4 on this row
                "#...#...#",
                "#########");
            var bfs = new BfsField(grid);
            int from = TestGrids.Cell(grid, 2, 1);
            int to = TestGrids.Cell(grid, 6, 1);

            bfs.BuildWalkingField(from);

            // Straight across would be 4 moves; the wall forces the doorway, and the doorway can only be
            // entered and left straight on (diagonals may not cut the wall's corners): 2 + 1 + 1 + 2 = 6.
            Assert.AreEqual(6, bfs.Steps(to));
        }

        [Test]
        public void SealedRoom_IsUnreachable()
        {
            var grid = TestGrids.FromRows(
                "#######",
                "#..#..#",
                "#..#..#",
                "#######");
            var bfs = new BfsField(grid);

            bfs.BuildWalkingField(TestGrids.Cell(grid, 1, 1));

            Assert.AreEqual(BfsField.Unreached, bfs.Steps(TestGrids.Cell(grid, 5, 1)));
            Assert.IsTrue(float.IsPositiveInfinity(bfs.WalkingDistance(TestGrids.Cell(grid, 5, 1))));
        }

        [Test]
        public void MaxSteps_StopsTheSearch()
        {
            var grid = TestGrids.FromRows(".......");
            var bfs = new BfsField(grid);

            bfs.BuildWalkingField(TestGrids.Cell(grid, 0, 0), maxSteps: 2);

            Assert.AreEqual(2, bfs.Steps(TestGrids.Cell(grid, 2, 0)));
            Assert.AreEqual(BfsField.Unreached, bfs.Steps(TestGrids.Cell(grid, 3, 0)));
        }

        [Test]
        public void SourceInsideAWall_StartsFromTheNearestWalkableCell()
        {
            var grid = TestGrids.FromRows(
                "#####",
                "#...#",
                "#####");
            var bfs = new BfsField(grid);

            int reached = bfs.BuildWalkingField(TestGrids.Cell(grid, 0, 1));   // the west wall

            Assert.AreEqual(3, reached);
            Assert.AreEqual(TestGrids.Cell(grid, 1, 1), bfs.Source);
        }

        [Test]
        public void Ramp_LinksTwoStoreys_ButAHeightJumpDoesNot()
        {
            // Two storeys of 4 x 1 cells. Storey 0 is a ramp rising 0 -> 1.8 m; storey 1 is a floor at 2 m.
            float[] walkY = { 0f, 0.6f, 1.2f, 1.8f, 2f, 2f, 2f, 2f };
            var flags = new CellFlags[8];
            for (int i = 0; i < 8; i++) flags[i] = CellFlags.Inside | CellFlags.Walkable;
            var grid = new GhostGrid(Vector3.zero, 1f, 4, 1, new[] { 0f, 2f }, flags, walkY);
            var bfs = new BfsField(grid);

            bfs.BuildWalkingField(grid.Index(0, 0, 0));

            Assert.AreEqual(3, bfs.Steps(grid.Index(3, 0, 0)), "up the ramp");
            Assert.AreEqual(4, bfs.Steps(grid.Index(3, 1, 0)), "onto the floor above from the ramp top");
            Assert.AreEqual(6, bfs.Steps(grid.Index(0, 1, 0)), "round to the cell above the start");

            // Flatten the ramp: now the storey above is a 2 m jump everywhere and cannot be reached.
            for (int x = 0; x < 4; x++) grid.WalkY[x] = 0f;
            bfs.BuildWalkingField(grid.Index(0, 0, 0));
            Assert.AreEqual(BfsField.Unreached, bfs.Steps(grid.Index(0, 1, 0)));
        }

        [Test]
        public void NeedsRebuild_OnlyWhenTheSourceMovesMoreThanOneCell()
        {
            var grid = new GhostGrid(Vector3.zero, 1f, 10, 10, new[] { 0f, 3f });
            var bfs = new BfsField(grid);
            for (int i = 0; i < grid.Count; i++) grid.Flags[i] = CellFlags.Inside | CellFlags.Walkable;
            bfs.BuildWalkingField(grid.Index(5, 0, 5));

            Assert.IsFalse(bfs.NeedsRebuild(grid.Index(5, 0, 5)));
            Assert.IsFalse(bfs.NeedsRebuild(grid.Index(6, 0, 6)), "one cell of drift");
            Assert.IsTrue(bfs.NeedsRebuild(grid.Index(7, 0, 5)), "two cells");
            Assert.IsTrue(bfs.NeedsRebuild(grid.Index(5, 1, 5)), "another storey");
        }
    }
}
