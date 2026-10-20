using Hollowmere.AI.Ghost;
using Hollowmere.AI.Senses;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    public class GhostBrainTests
    {
        // A 21 x 15 open floor; the player stands in the middle.
        const int SizeX = 21, SizeZ = 15;

        static GhostBrain NewBrain(out GhostGrid grid)
        {
            grid = new GhostGrid(Vector3.zero, 1f, SizeX, SizeZ, new[] { 0f });
            for (int i = 0; i < grid.Count; i++) grid.Flags[i] = CellFlags.Inside | CellFlags.Walkable;
            return new GhostBrain(new InfluenceMap(grid), GhostTuning.Default);
        }

        static Vector3 Feet(int x, int z) => new Vector3(x + 0.5f, 0f, z + 0.5f);
        static readonly Vector3 Middle = Feet(10, 7);
        static readonly Vector3 GhostStart = Feet(18, 2);

        [Test]
        public void Seeing_RemembersThePlayer_AndMarksWhereTheyLook()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);

            brain.See(Middle, Vector3.right, WeaponKind.Pistol, false, now: 1f);

            Assert.AreEqual(BeliefSource.Seen, brain.Memory.Current.Source);
            Assert.AreEqual(1f, brain.Memory.ConfidenceAt(1f), 1e-4f);
            Assert.AreEqual(WeaponKind.Pistol, brain.KnownWeapon);
            Assert.AreEqual(1f, brain.Map.Watched(grid.CellAt(Feet(15, 7) + Vector3.up), 1f), "in front of the player");
            Assert.AreEqual(0f, brain.Map.Watched(grid.CellAt(Feet(4, 7) + Vector3.up), 1f), "behind the player");
        }

        [Test]
        public void Stalks_BehindWhereThePlayerWasLastSeenLooking()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            brain.See(Middle, Vector3.right, WeaponKind.None, false, now: 1f);

            brain.Think(GhostStart, now: 1f);

            Assert.AreEqual(GhostMode.Stalk, brain.Mode);
            Assert.GreaterOrEqual(brain.TargetCell, 0);
            Assert.Less(grid.CellCentre(brain.TargetCell).x, Middle.x, "the chosen cell is behind the player (west)");
            Assert.IsTrue(brain.Scorer.Query.HasLook);
        }

        [Test]
        public void HeardOnly_StalksWithoutKnowingTheLook()
        {
            GhostBrain brain = NewBrain(out _);
            brain.Hear(Middle, NoiseKind.Footstep, now: 1f);

            brain.Think(GhostStart, now: 1f);

            Assert.AreEqual(BeliefSource.Heard, brain.Memory.Current.Source);
            Assert.AreEqual(AgentMemory.HeardConfidence, brain.Memory.ConfidenceAt(1f), 1e-4f);
            Assert.AreEqual(GhostMode.Stalk, brain.Mode);
            Assert.IsFalse(brain.Scorer.Query.HasLook, "a footstep says nothing about where the player looks");
        }

        [Test]
        public void AnOldSighting_DoesNotLendItsLookToANewerNoise()
        {
            GhostBrain brain = NewBrain(out _);
            brain.See(Middle, Vector3.right, WeaponKind.None, false, now: 1f);
            brain.Hear(Feet(3, 3), NoiseKind.Door, now: 20f);       // the sighting has faded below the noise

            Assert.AreEqual(BeliefSource.Heard, brain.Memory.Current.Source);
            Assert.IsFalse(brain.SightingStillApplies(20f));
        }

        [Test]
        public void OtherCreatures_SayNothingAboutThePlayer()
        {
            GhostBrain brain = NewBrain(out _);

            brain.Hear(Middle, NoiseKind.Groan, now: 1f);
            brain.Hear(Middle, NoiseKind.Bark, now: 1f);

            Assert.IsFalse(brain.Memory.HasBelief(1f));
        }

        [Test]
        public void AShotGivesAwayTheWeapon_EvenUnseen()
        {
            GhostBrain brain = NewBrain(out _);

            brain.Hear(Middle, NoiseKind.IceGun, now: 2f);

            Assert.AreEqual(WeaponKind.IceGun, brain.KnownWeapon);
            Assert.AreEqual(2f, brain.KnownWeaponTime);
        }

        [Test]
        public void TheDirector_GivesAWeakBelief()
        {
            GhostBrain brain = NewBrain(out _);

            brain.Told(Middle, now: 1f);

            Assert.AreEqual(BeliefSource.Told, brain.Memory.Current.Source);
            Assert.AreEqual(AgentMemory.ToldConfidence, brain.Memory.ConfidenceAt(1f), 1e-4f);
        }

        [Test]
        public void ForgetsThePlayer_AndDriftsHome()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            brain.HasAnchor = true;
            brain.Anchor = GhostStart;
            GhostMode from = GhostMode.Frozen, to = GhostMode.Frozen;
            brain.ModeChanged += (a, b) => { from = a; to = b; };
            brain.See(Middle, Vector3.right, WeaponKind.None, false, now: 0f);
            brain.Think(Feet(5, 7), now: 0f);

            // Seen at 1.0, forgotten below 0.1: 1.0 * e^(-t / 8) < 0.1 after about 18.4 s.
            brain.Think(Feet(5, 7), now: 19f);

            Assert.AreEqual(GhostMode.Idle, brain.Mode);
            Assert.AreEqual(GhostMode.Stalk, from);
            Assert.AreEqual(GhostMode.Idle, to);
            Assert.AreEqual(grid.CellAt(GhostStart + Vector3.up * 0.1f), brain.TargetCell, "home to the haunt anchor");
        }

        [Test]
        public void Think_StartsARoute_ThatSlicesIntoWaypointsEndingAtTheTarget()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            brain.See(Middle, Vector3.right, WeaponKind.None, false, now: 1f);

            Assert.IsTrue(brain.Think(GhostStart, now: 1f), "a route search was started");
            int slices = 0;
            while (!brain.StepRoute(5, 1f) && slices < 1000) slices++;

            Assert.Greater(slices, 0, "a 5-cell budget needs several slices");
            Assert.GreaterOrEqual(brain.WaypointCount, 2);
            Assert.AreEqual(grid.CellCentre(brain.TargetCell), brain.Waypoints[brain.WaypointCount - 1]);
            Assert.AreEqual(1, brain.RouteVersion);
        }

        [Test]
        public void DoesNotReplan_WhileTheTargetHoldsAndTheIntervalRuns()
        {
            GhostBrain brain = NewBrain(out _);
            brain.See(Middle, Vector3.right, WeaponKind.None, false, now: 1f);
            brain.Think(GhostStart, now: 1f);
            while (!brain.StepRoute(1000, 1f)) { }

            Assert.IsFalse(brain.Think(GhostStart, now: 1.2f), "same target, interval not over");
            Assert.IsTrue(brain.Think(GhostStart, now: 2.5f), "the map may have changed: re-plan");
        }

        [Test]
        public void SeenAimingTheIceGun_KeepsOutOfTheAim()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            Vector3 aim = Vector3.left;                                      // aiming at where it would like to be
            brain.See(Middle, aim, WeaponKind.IceGun, false, now: 1f);

            brain.Think(GhostStart, now: 1f);

            Assert.IsTrue(brain.Scorer.Query.ThreatActive);
            Assert.AreEqual(0f, brain.Scorer.Explain(brain.TargetCell).Threat, "not inside the aim");
        }

        [Test]
        public void WarmUp_LeavesNoTrace()
        {
            GhostBrain brain = NewBrain(out _);
            int events = 0;
            brain.ModeChanged += (a, b) => events++;

            brain.WarmUp(GhostStart);

            Assert.IsFalse(brain.Memory.HasBelief(0f));
            Assert.AreEqual(GhostMode.Idle, brain.Mode);
            Assert.AreEqual(-1, brain.TargetCell);
            Assert.AreEqual(0, brain.RouteVersion, "no route handed to the body");
            Assert.AreEqual(0, events);
            Assert.IsFalse(brain.StepRoute(1000, 0f), "no search left running");
        }

        [Test]
        public void NoBeliefAndNoAnchor_StaysPut()
        {
            GhostBrain brain = NewBrain(out _);

            Assert.IsFalse(brain.Think(GhostStart, now: 1f));
            Assert.AreEqual(GhostMode.Idle, brain.Mode);
            Assert.AreEqual(-1, brain.TargetCell);
        }
    }
}
