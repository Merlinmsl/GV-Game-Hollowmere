using System;
using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class HillClimberTests
    {
        // Most tests use a 31 x 31 open floor with the player believed in the middle, a weaker pool of
        // darkness to the east and a deeper one to the west.
        const int Size = 31;
        static readonly int Player = At(15, 15);
        static readonly int East = At(21, 15);
        static readonly int West = At(9, 15);

        static int At(int x, int z, int size = Size) => z * size + x;

        // An open floor, lit everywhere except two pools of darkness at 'pocketA' and 'pocketB'. Each pool
        // fades out over 'reach' cells, so each is a hill in the score with its own basin. Only the dark
        // term is weighted.
        static HillClimber TwoPockets(int player, int pocketA, float depthA, int pocketB, float depthB,
                                      int size = Size, float reach = 4f)
        {
            var grid = new GhostGrid(Vector3.zero, 1f, size, size, new[] { 0f });
            for (int i = 0; i < grid.Count; i++) grid.Flags[i] = CellFlags.Inside | CellFlags.Walkable;
            var map = new InfluenceMap(grid);
            for (int c = 0; c < grid.Count; c++)
            {
                float a = depthA * Mathf.Max(0f, 1f - Flat(grid, c, pocketA) / reach);
                float b = depthB * Mathf.Max(0f, 1f - Flat(grid, c, pocketB) / reach);
                map.SetBakedLight(c, 1f - Mathf.Max(a, b));
            }
            return ClimberFor(map, player);
        }

        static HillClimber ClimberFor(InfluenceMap map, int player)
        {
            var walk = new BfsField(map.Grid);
            walk.BuildWalkingField(player);
            var scorer = new PositionScorer(map) { WalkField = walk };
            scorer.Query = new PositionQuery
            {
                PlayerEye = map.Grid.CellCentre(player) + Vector3.up * (PositionQuery.EyeHeight - GhostGrid.BodyHeight),
                LookDirection = Vector3.right,
                HasLook = true,
                Weights = new ScoreWeights { dark = 1f },
                Range = new RangeBand(0f, 100f, 1f),
            };
            return new HillClimber(scorer);
        }

        static float Flat(GhostGrid grid, int a, int b)
        {
            Vector3 pa = grid.CellCentre(a), pb = grid.CellCentre(b);
            return Vector2.Distance(new Vector2(pa.x, pa.z), new Vector2(pb.x, pb.z));
        }

        // Brute force: the best score of any candidate cell within 'radius' of the player.
        static float BestByBruteForce(HillClimber climber, int player, float radius)
        {
            GhostGrid grid = climber.Scorer.Map.Grid;
            float best = float.NegativeInfinity;
            for (int c = 0; c < grid.Count; c++)
                if (climber.Scorer.IsCandidate(c) && Flat(grid, c, player) <= radius)
                    best = Mathf.Max(best, climber.Scorer.Score(c));
            return best;
        }

        [Test]
        public void OneClimb_StopsAtTheNearestHill_EvenWhenAHigherOneExists()
        {
            var climber = TwoPockets(Player, East, 0.6f, West, 1f);

            climber.BeginSearch();
            Assert.AreEqual(East, climber.Climb(At(19, 15)), "climbs to the weaker pocket: a local maximum");
        }

        [Test]
        public void Search_FromRingSeeds_FindsTheGlobalBest()
        {
            var climber = TwoPockets(Player, East, 0.6f, West, 1f);

            HillClimber.Result result = climber.Search(-1);

            Assert.AreEqual(West, result.Cell);
            Assert.AreEqual(BestByBruteForce(climber, Player, 12f), result.Score, 1e-5f, "same as scoring every cell");
            Assert.GreaterOrEqual(result.Climbs, 15, "24 ring seeds + 2 restarts, minus seeds already on a trail");
        }

        [Test]
        public void Search_ScoresFarFewerCellsThanBruteForce()
        {
            const int big = 41;
            var climber = TwoPockets(At(20, 20, big), At(26, 20, big), 0.6f, At(14, 20, big), 1f, big);

            HillClimber.Result result = climber.Search(-1);

            Assert.AreEqual(At(14, 20, big), result.Cell);
            Assert.Less(result.Evaluations, big * big / 3, "local search scores a fraction of the house");
        }

        [Test]
        public void Climb_PassesThroughAWall_AsAGhostDoes()
        {
            var grid = TestGrids.FromRows(
                "...#....",
                "...#....",
                "...#....");
            var map = new InfluenceMap(grid);
            int pocket = TestGrids.Cell(grid, 7, 1);
            for (int c = 0; c < grid.Count; c++)
                map.SetBakedLight(c, Mathf.Clamp01(Flat(grid, c, pocket) / 8f));    // darker towards the east end
            var climber = ClimberFor(map, TestGrids.Cell(grid, 0, 1));

            climber.BeginSearch();
            Assert.AreEqual(pocket, climber.Climb(TestGrids.Cell(grid, 1, 1)), "over the wall to the darkest cell");

            var buffer = new int[10];
            int count = climber.Neighbours(TestGrids.Cell(grid, 2, 1), buffer);
            CollectionAssert.Contains(new ArraySegment<int>(buffer, 0, count), TestGrids.Cell(grid, 4, 1), "the cell beyond the wall");
        }

        [Test]
        public void Search_KeepsOutOfTheIceGunsAim()
        {
            // The player aims east: the deepest dark is straight down the barrel, a weaker pocket is off at 45 degrees.
            int player = At(5, 15), downTheBarrel = At(15, 15), offToTheSide = At(11, 21);
            var climber = TwoPockets(player, offToTheSide, 0.6f, downTheBarrel, 1f);
            PositionScorer scorer = climber.Scorer;
            scorer.Query.Weights.threat = 3f;
            scorer.Query.ThreatActive = true;
            scorer.Query.Aim = new AimCone(scorer.Query.PlayerEye, Vector3.right, 15f, 15f);

            HillClimber.Result result = climber.Search(-1);

            Assert.AreEqual(0f, scorer.Explain(result.Cell).Threat, "never inside the aim");
            Assert.AreEqual(offToTheSide, result.Cell, "takes the weaker pocket instead");
        }

        [Test]
        public void ReservedCell_SendsASecondGhostElsewhere()
        {
            var climber = TwoPockets(Player, East, 0.6f, West, 1f);

            int first = climber.Search(-1).Cell;
            climber.Scorer.Query.Weights.crowd = 2f;
            climber.Scorer.Query.Reserved = new[] { first };
            climber.Scorer.Query.ReservedCount = 1;
            climber.Scorer.Query.CrowdRadius = 4f;
            int second = climber.Search(-1).Cell;

            Assert.AreEqual(West, first);
            Assert.AreEqual(East, second, "the other pocket");
        }

        [Test]
        public void Hysteresis_KeepsTheCurrentTarget_UnlessClearlyBeaten()
        {
            var climber = TwoPockets(Player, East, 0.95f, West, 1f);    // only 0.05 apart

            HillClimber.Result kept = climber.Search(East);
            Assert.AreEqual(East, kept.Cell);
            Assert.IsTrue(kept.KeptCurrent);

            climber.Config.switchMargin = 0f;
            HillClimber.Result switched = climber.Search(East);
            Assert.AreEqual(West, switched.Cell);
            Assert.IsFalse(switched.KeptCurrent);
        }

        [Test]
        public void Annealing_IsRepeatable_AndEndsOnALocalMaximum()
        {
            var a = TwoPockets(Player, East, 0.6f, West, 1f);
            var b = TwoPockets(Player, East, 0.6f, West, 1f);
            a.Config.useAnnealing = b.Config.useAnnealing = true;

            HillClimber.Result ra = a.Search(-1), rb = b.Search(-1);

            Assert.AreEqual(ra.Cell, rb.Cell, "same random seed, same answer");
            Assert.AreEqual(West, ra.Cell);
            var buffer = new int[10];
            int count = a.Neighbours(ra.Cell, buffer);
            for (int i = 0; i < count; i++)
                Assert.LessOrEqual(a.Scorer.Score(buffer[i]), ra.Score + 1e-5f, "no neighbour is better");
        }

        [Test]
        public void Search_AllocatesNothingAfterWarmUp()
        {
            var climber = TwoPockets(Player, East, 0.6f, West, 1f);
            climber.Search(-1);                                      // warm up (JIT)

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) climber.Search(West);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0, after - before);
        }
    }
}
