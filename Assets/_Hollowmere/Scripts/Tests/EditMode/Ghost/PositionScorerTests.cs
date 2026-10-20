using Hollowmere.AI.Ghost;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class PositionScorerTests
    {
        // A scorer over 'grid' with the player believed at 'playerCell', looking along 'look'. All weights
        // start at zero, so each test switches on only the term it checks.
        static PositionScorer ScorerFor(GhostGrid grid, int playerCell, Vector3 look, out InfluenceMap map)
        {
            map = new InfluenceMap(grid);
            var walk = new BfsField(grid);
            walk.BuildWalkingField(playerCell);
            var scorer = new PositionScorer(map) { WalkField = walk };
            scorer.Query = new PositionQuery
            {
                PlayerEye = Eye(grid, playerCell),
                LookDirection = look,
                HasLook = true,
                Range = new RangeBand(0f, 100f, 1f),
            };
            return scorer;
        }

        static Vector3 Eye(GhostGrid grid, int cell) =>
            grid.CellCentre(cell) + Vector3.up * (PositionQuery.EyeHeight - GhostGrid.BodyHeight);

        static GhostGrid OpenRoom() => TestGrids.FromRows(
            ".........",
            ".........",
            ".........",
            ".........",
            ".........",
            ".........",
            ".........");

        [Test]
        public void Behind_IsOneBehindTheLook_HalfToTheSide_ZeroInFront()
        {
            GhostGrid grid = OpenRoom();
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 4, 3), Vector3.right, out _);

            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 7, 3)).Behind, 1e-4f, "straight ahead");
            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 1, 3)).Behind, 1e-4f, "directly behind");
            Assert.AreEqual(0.5f, scorer.Explain(TestGrids.Cell(grid, 4, 0)).Behind, 1e-4f, "to the side");
        }

        [Test]
        public void Behind_IsNeutralWhenTheLookWasNeverSeen()
        {
            GhostGrid grid = OpenRoom();
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 4, 3), Vector3.right, out _);
            scorer.Query.HasLook = false;

            Assert.AreEqual(0.5f, scorer.Explain(TestGrids.Cell(grid, 1, 3)).Behind, 1e-4f);
        }

        [Test]
        public void Dark_And_Unseen_ComeFromTheInfluenceMap()
        {
            GhostGrid grid = OpenRoom();
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 1, 3), Vector3.right, out InfluenceMap map);
            int lit = TestGrids.Cell(grid, 6, 1);
            map.SetBakedLight(lit, 0.75f);
            map.MarkWatched(Eye(grid, TestGrids.Cell(grid, 1, 3)), Vector3.right, now: 10f);
            scorer.Query.Now = 14f;                                                   // half the fade later

            Assert.AreEqual(0.25f, scorer.Explain(lit).Dark, 1e-4f);
            Assert.AreEqual(0.5f, scorer.Explain(TestGrids.Cell(grid, 5, 3)).Unseen, 1e-4f, "looked at 4 s ago");
            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 0, 0)).Unseen, 1e-4f, "behind the viewer, never seen");
        }

        [Test]
        public void Cover_CountsWallsAround()
        {
            var grid = TestGrids.FromRows(
                "#####",
                "#...#",
                "#...#",
                "#...#",
                "#####");
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 2, 2), Vector3.right, out _);

            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 2, 2)).Cover, 1e-4f, "middle of the room");
            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 2, 1)).Cover, 1e-4f, "against a wall");
            Assert.IsFalse(scorer.IsCandidate(TestGrids.Cell(grid, 0, 2)), "inside a wall is not a position");
        }

        [Test]
        public void Range_UsesWalkingDistance_NotStraightLine()
        {
            var grid = TestGrids.FromRows(
                "#########",
                "#...#...#",
                "#...#...#",
                "#.......#",
                "#...#...#",
                "#########");
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 3, 1), Vector3.right, out _);
            scorer.Query.Range = new RangeBand(5f, 7f, 1f);

            // Two cells away in a straight line, but the wall makes it a 6 m walk: in the band.
            ScoreBreakdown acrossTheWall = scorer.Explain(TestGrids.Cell(grid, 5, 1));
            Assert.AreEqual(6f, acrossTheWall.WalkingDistance, 1e-4f);
            Assert.AreEqual(1f, acrossTheWall.Range, 1e-4f);
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 1, 1)).Range, 1e-4f, "2 m: too close");
        }

        [Test]
        public void RangeBand_IsATrapezoid()
        {
            var band = new RangeBand(4f, 8f, 2f);
            Assert.AreEqual(0f, band.Fit(1f), 1e-4f);
            Assert.AreEqual(0.5f, band.Fit(3f), 1e-4f);
            Assert.AreEqual(1f, band.Fit(6f), 1e-4f);
            Assert.AreEqual(0.5f, band.Fit(9f), 1e-4f);
            Assert.AreEqual(0f, band.Fit(float.PositiveInfinity), "unreachable");
        }

        [Test]
        public void Threat_IsTheIceGunCone_BlockedByWalls()
        {
            var grid = TestGrids.FromRows(
                ".........",
                ".........",
                ".....#...",
                ".........",
                ".........");
            int player = TestGrids.Cell(grid, 1, 2);
            var scorer = ScorerFor(grid, player, Vector3.right, out _);
            scorer.Query.ThreatActive = true;
            scorer.Query.Aim = new AimCone(Eye(grid, player), Vector3.right, 15f, 15f);

            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 4, 2)).Threat, "in the aim, in the open");
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 7, 2)).Threat, "behind the pillar");
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 4, 0)).Threat, "outside the cone");

            scorer.Query.ThreatActive = false;
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 4, 2)).Threat, "gun not aimed");
        }

        [Test]
        public void Home_IsFullWithinTheLeash_AndGoneAtTwiceIt()
        {
            GhostGrid grid = TestGrids.FromRows(".........");
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 0, 0), Vector3.right, out _);
            scorer.Query.HasAnchor = true;
            scorer.Query.Anchor = grid.CellCentre(TestGrids.Cell(grid, 0, 0));
            scorer.Query.Leash = 2f;

            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 2, 0)).Home, 1e-4f);
            Assert.AreEqual(0.5f, scorer.Explain(TestGrids.Cell(grid, 3, 0)).Home, 1e-4f);
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 6, 0)).Home, 1e-4f);
        }

        [Test]
        public void Crowd_PenalisesCellsNearAnotherGhostsTarget()
        {
            GhostGrid grid = TestGrids.FromRows(".........");
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 0, 0), Vector3.right, out _);
            scorer.Query.Reserved = new[] { TestGrids.Cell(grid, 4, 0) };
            scorer.Query.ReservedCount = 1;
            scorer.Query.CrowdRadius = 4f;

            Assert.AreEqual(1f, scorer.Explain(TestGrids.Cell(grid, 4, 0)).Crowd, 1e-4f);
            Assert.AreEqual(0.5f, scorer.Explain(TestGrids.Cell(grid, 6, 0)).Crowd, 1e-4f);
            Assert.AreEqual(0f, scorer.Explain(TestGrids.Cell(grid, 8, 0)).Crowd, 1e-4f);
        }

        [Test]
        public void Total_IsTheWeightedSum()
        {
            GhostGrid grid = OpenRoom();
            var scorer = ScorerFor(grid, TestGrids.Cell(grid, 4, 3), Vector3.right, out InfluenceMap map);
            int cell = TestGrids.Cell(grid, 1, 1);
            map.SetBakedLight(cell, 0.3f);
            scorer.Query.Weights = new ScoreWeights
                { behind = 1f, dark = 2f, unseen = 0.5f, cover = 4f, range = 1.5f, home = 3f, threat = 5f, crowd = 6f };
            scorer.Query.Range = new RangeBand(2f, 4f, 2f);

            ScoreBreakdown b = scorer.Explain(cell);
            float expected = 1f * b.Behind + 2f * b.Dark + 0.5f * b.Unseen + 4f * b.Cover + 1.5f * b.Range
                           + 3f * b.Home - 5f * b.Threat - 6f * b.Crowd;
            Assert.AreEqual(expected, b.Total, 1e-4f);
            Assert.AreEqual(expected, scorer.Score(cell), 1e-4f);
        }
    }
}
