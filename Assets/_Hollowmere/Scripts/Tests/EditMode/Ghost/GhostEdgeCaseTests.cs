using System.Collections.Generic;
using Hollowmere.AI.Ghost;
using Hollowmere.AI.Senses;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    /// <summary>The edge cases: each one a fallback inside the brain's normal loop, never a scripted state.</summary>
    public class GhostEdgeCaseTests
    {
        const int RoomX = 21, SizeZ = 15;

        // A walled room 21 x 15 (walls on its rim). 'outside' extra columns east of it are not part of the
        // house; 'door' opens the east wall at z = 7.
        static GhostGrid Room(int outside = 0, bool door = false)
        {
            var grid = new GhostGrid(Vector3.zero, 1f, RoomX + outside, SizeZ, new[] { 0f });
            for (int z = 0; z < SizeZ; z++)
            for (int x = 0; x < RoomX; x++)
            {
                bool wall = x == 0 || z == 0 || x == RoomX - 1 || z == SizeZ - 1;
                if (door && x == RoomX - 1 && z == 7) wall = false;
                grid.Flags[grid.Index(x, 0, z)] = CellFlags.Inside | (wall ? CellFlags.Solid : CellFlags.Walkable);
            }
            return grid;
        }

        static GhostBrain NewBrain(GhostGrid grid, int seed = 1234) =>
            new GhostBrain(new InfluenceMap(grid), GhostTuning.Default, ModeTuning.Servant, seed);

        static Vector3 Feet(int x, int z) => new Vector3(x + 0.5f, 0f, z + 0.5f);
        static readonly Vector3 Player = Feet(10, 7);

        [Test]
        public void NoDarkCellNear_LurksInTheNearestWall_UntilTheLightsGo()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            for (int i = 0; i < grid.Count; i++) brain.Map.SetBakedLight(i, 1f);    // every lamp on
            brain.See(Player, Vector3.right, WeaponKind.Pistol, false, now: 1f);

            brain.Think(Feet(16, 3), now: 1f);

            Assert.AreEqual(GhostMode.Stalk, brain.Mode);
            Assert.IsTrue(brain.Lurking);
            Assert.IsTrue(grid.Is(brain.TargetCell, CellFlags.Solid), "waits inside a wall");

            for (int i = 0; i < grid.Count; i++) brain.Map.SetBakedLight(i, 0f);    // lights out
            brain.Think(Feet(16, 3), now: 1.2f);

            Assert.IsFalse(brain.Lurking);
            Assert.IsFalse(grid.Is(brain.TargetCell, CellFlags.Solid), "a dark place to stalk from again");
        }

        [Test]
        public void PlayerCornered_WaitsInTheWallBehindThem()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            Vector3 corner = Feet(1, 1);
            brain.See(corner, new Vector3(1f, 0f, 1f), WeaponKind.Pistol, false, now: 1f);   // back to the corner

            brain.Think(Feet(12, 8), now: 1f);

            Assert.IsTrue(brain.PlayerCornered);
            Assert.AreEqual(GhostMode.Stalk, brain.Mode);
            Assert.IsTrue(grid.IsWallSkin(brain.TargetCell), "the only 'behind' left is the wall's surface");
            Assert.GreaterOrEqual(brain.Scorer.Explain(brain.TargetCell).Behind, 0.2f, "beside or behind them, not in front");
        }

        [Test]
        public void NotCornered_NeverPicksAWallCell()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            brain.See(Player, Vector3.right, WeaponKind.Pistol, false, now: 1f);

            brain.Think(Feet(16, 3), now: 1f);

            Assert.IsFalse(brain.PlayerCornered);
            Assert.IsFalse(grid.Is(brain.TargetCell, CellFlags.Solid));
        }

        [Test]
        public void PlayerOutside_WaitsAtTheThreshold_AndNeverStrikes()
        {
            GhostGrid grid = Room(outside: 4, door: true);
            GhostBrain brain = NewBrain(grid);
            Vector3 outside = Feet(23, 7);
            brain.See(outside, Vector3.left, WeaponKind.Pistol, false, now: 1f);

            brain.Think(Feet(18, 9), now: 1f);

            Assert.IsTrue(brain.PlayerOutside);
            Assert.AreEqual(0f, brain.Utility(GhostMode.Strike), "ghosts never leave the house");
            Assert.AreNotEqual(GhostMode.Strike, brain.Mode);
            Assert.IsTrue(grid.Is(brain.TargetCell, CellFlags.Inside), "stays in the house");
            Assert.LessOrEqual(Vector3.Distance(grid.CellCentre(brain.TargetCell), Feet(20, 7) + Vector3.up), 10f, "near the door they left by");
        }

        [Test]
        public void BeliefLost_GoesHome_ThenWandersSlowlyAroundIt()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            Vector3 home = Feet(5, 7);
            brain.HasAnchor = true;
            brain.Anchor = home;
            brain.See(Player, Vector3.right, WeaponKind.None, false, now: 0f);
            brain.Think(Feet(12, 7), now: 0f);
            Assert.AreEqual(1f, brain.SpeedScale, "hunting at full speed");

            brain.Think(Feet(12, 7), now: 19f);                                     // forgotten
            Assert.AreEqual(GhostMode.Idle, brain.Mode);
            Assert.AreEqual(grid.CellAt(home + Vector3.up * 0.1f), brain.TargetCell, "home first");
            Assert.AreEqual(ModeTuning.Servant.wanderSpeed, brain.SpeedScale, "slowly");

            var visited = new HashSet<int>();
            float now = 19f;
            for (int i = 0; i < 6; i++)
            {
                now += 9f;                                                           // longer than any pause
                brain.Think(home, now);
                Assert.IsTrue(grid.Is(brain.TargetCell, CellFlags.Walkable));
                Assert.LessOrEqual(Vector3.Distance(grid.CellCentre(brain.TargetCell), home + Vector3.up),
                                   ModeTuning.Servant.wanderRadius + 0.75f, "within the wander radius of home");
                visited.Add(brain.TargetCell);
            }
            Assert.GreaterOrEqual(visited.Count, 3, "a random wander, not a fixed path");
        }

        [Test]
        public void RouteNodeCapReached_TakesThePartialRoute_AndReplansSooner()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            GhostTuning tuning = GhostTuning.Default;
            tuning.maxRouteNodes = 3;
            brain.Tuning = tuning;
            brain.See(Player, Vector3.right, WeaponKind.None, false, now: 1f);

            Assert.IsTrue(brain.Think(Feet(18, 2), now: 1f));
            int slices = 0;
            while (!brain.StepRoute(1000, 1f) && slices++ < 100) { }

            Assert.AreEqual(RouteStatus.Partial, brain.LastRouteStatus);
            Assert.GreaterOrEqual(brain.WaypointCount, 1, "moves along the part it found");
            Assert.AreNotEqual(grid.CellCentre(brain.TargetCell), brain.Waypoints[brain.WaypointCount - 1]);
            Assert.IsTrue(brain.Think(Feet(18, 2), now: 1f + ModeTuning.Servant.partialReplan + 0.05f),
                          "re-plans before the normal interval");
        }

        [Test]
        public void SpawnedInAWall_ComesOutToTheFloor()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            Vector3 inWall = Feet(0, 7);
            brain.HasAnchor = true;
            brain.Anchor = inWall;

            Assert.IsTrue(brain.Think(inWall, now: 1f), "a route out is planned");
            Assert.IsTrue(grid.Is(brain.TargetCell, CellFlags.Walkable), "it haunts the floor, not the wall");
        }

        [Test]
        public void SpawnedOutsideTheHouse_PlansFromTheNearestInsideCell()
        {
            GhostGrid grid = Room(outside: 4);
            GhostBrain brain = NewBrain(grid);
            Vector3 outside = Feet(23, 7);
            brain.HasAnchor = true;
            brain.Anchor = Feet(15, 7);

            Assert.IsTrue(brain.Think(outside, now: 1f));
            Assert.IsTrue(grid.Is(brain.Router.Start, CellFlags.Inside));
            int steps = 0;
            while (!brain.StepRoute(1000, 1f) && steps++ < 100) { }
            Assert.AreEqual(RouteStatus.Found, brain.LastRouteStatus);
        }

        [Test]
        public void AimFlickingOnAndOff_DoesNotMakeItFlicker()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            Vector3 ghost = Feet(14, 7);
            int modeChanges = 0;
            brain.ModeChanged += (a, b) => modeChanges++;
            var refuges = new HashSet<int>();

            for (int tick = 0; tick < 15; tick++)                                   // 3 s at 5 Hz
            {
                float now = 1f + tick * 0.2f;
                Vector3 aim = tick % 2 == 0 ? Vector3.right : Vector3.forward;      // on the ghost, off it, on...
                brain.See(Player, aim, WeaponKind.IceGun, false, now);
                brain.Think(ghost, now);
                Assert.AreEqual(GhostMode.Flee, brain.Mode, "tick " + tick);
                refuges.Add(brain.TargetCell);
            }

            Assert.AreEqual(1, modeChanges, "Idle -> Flee once, then held");
            Assert.AreEqual(1, refuges.Count, "the first refuge stays out of both aims, so it is kept");
        }

        [Test]
        public void HeardTheIceGun_HidesInOneWall_WithoutFlickering()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            for (int i = 0; i < grid.Count; i++)
                if (grid.Is(i, CellFlags.Walkable)) brain.Map.SetBakedLight(i, 1f);    // a lit room; walls stay dark
            int modeChanges = 0;
            brain.ModeChanged += (a, b) => modeChanges++;
            brain.Hear(Player, NoiseKind.IceGun, now: 1f);                             // heard, never seen

            brain.Think(Feet(13, 7), now: 1f);
            Assert.AreEqual(GhostMode.Flee, brain.Mode, "wary of a gun it heard, standing in the light");
            int refuge = brain.TargetCell;
            Assert.IsTrue(grid.Is(refuge, CellFlags.Solid));

            // It reaches the refuge and waits there: the wall cell is dark, but the floor it would step onto is not.
            Vector3 inWall = grid.CellCentre(refuge) - Vector3.up * GhostGrid.BodyHeight;
            for (float now = 1.2f; now <= 3.01f; now += 0.2f)
            {
                brain.Think(inWall, now);
                Assert.AreEqual(GhostMode.Flee, brain.Mode, "t " + now);
                Assert.AreEqual(refuge, brain.TargetCell, "the danger cone is fixed, so its refuge stays safe");
            }
            Assert.AreEqual(1, modeChanges, "Idle -> Flee once, no Ambush/Flee flicker");
        }

        [Test]
        public void ReachedTheBelievedSpot_AndNobodyIsThere_GivesUp()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            brain.See(Player, Vector3.left, WeaponKind.Pistol, false, now: 0f);
            Vector3 onTheSpot = Feet(10, 8);

            brain.Think(onTheSpot, now: 1.5f);                                       // last saw them 1.5 s ago
            Assert.IsTrue(brain.Memory.HasBelief(1.5f), "gives the eyes notHereSeconds to recognise anyone");

            brain.Think(onTheSpot, now: 1.5f + ModeTuning.Servant.notHereSeconds + 0.05f);
            Assert.AreEqual(1, brain.BeliefsDisproved);
            Assert.IsFalse(brain.Memory.HasBelief(2.6f));
            Assert.AreEqual(GhostMode.Idle, brain.Mode, "nobody here: back to haunting");
        }

        [Test]
        public void SomeoneStillInView_IsNotRuledOut()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            brain.See(Player, Vector3.left, WeaponKind.Pistol, false, now: 0f);
            brain.PlayerInView = true;                                              // the sight meter is still filling

            brain.Think(Feet(10, 8), now: 1.5f);
            brain.Think(Feet(10, 8), now: 3f);

            Assert.AreEqual(0, brain.BeliefsDisproved);
            Assert.IsTrue(brain.Memory.HasBelief(3f));
        }

        [Test]
        public void FromInsideAWall_ItCannotRuleThePlayerOut()
        {
            GhostGrid grid = Room();
            GhostBrain brain = NewBrain(grid);
            brain.See(Feet(1, 7), Vector3.right, WeaponKind.Pistol, false, now: 0f);

            brain.Think(Feet(0, 7), now: 1.5f);                                      // a metre away, inside the wall
            brain.Think(Feet(0, 7), now: 3f);

            Assert.AreEqual(0, brain.BeliefsDisproved, "it cannot see out of the wall, so it proves nothing");
        }

        [Test]
        public void TwoGhosts_NeverClaimTheSameCell()
        {
            GhostGrid grid = Room();
            var map = new InfluenceMap(grid);
            var first = new GhostBrain(map, GhostTuning.Default, ModeTuning.Servant, 7);
            var alone = new GhostBrain(map, GhostTuning.Default, ModeTuning.Servant, 7);
            var second = new GhostBrain(map, GhostTuning.Default, ModeTuning.Servant, 7);
            Vector3 start = Feet(16, 3);
            foreach (GhostBrain b in new[] { first, alone, second })
                b.See(Player, Vector3.right, WeaponKind.Pistol, false, now: 1f);

            first.Think(start, now: 1f);
            alone.Think(start, now: 1f);
            second.Reserved = new[] { first.TargetCell };
            second.ReservedCount = 1;
            second.Think(start, now: 1f);

            Assert.AreEqual(first.TargetCell, alone.TargetCell, "unaware of each other, they would pick the same cell");
            Assert.AreNotEqual(first.TargetCell, second.TargetCell, "the claim is respected");
            Assert.Less(second.Scorer.Explain(second.TargetCell).Crowd, 1f);
        }
    }
}
