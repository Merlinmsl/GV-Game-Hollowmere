using Hollowmere.AI.Ghost;
using Hollowmere.AI.Senses;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    /// <summary>The modes as the brain carries them out: flee, strike, freeze, weapon tactics.</summary>
    public class GhostModeTests
    {
        const int SizeX = 21, SizeZ = 15;

        // A walled room: a ring of wall cells round an open floor.
        static GhostBrain NewBrain(out GhostGrid grid, ModeTuning? modes = null)
        {
            grid = new GhostGrid(Vector3.zero, 1f, SizeX, SizeZ, new[] { 0f });
            for (int z = 0; z < SizeZ; z++)
            for (int x = 0; x < SizeX; x++)
            {
                bool wall = x == 0 || z == 0 || x == SizeX - 1 || z == SizeZ - 1;
                grid.Flags[grid.Index(x, 0, z)] = CellFlags.Inside | (wall ? CellFlags.Solid : CellFlags.Walkable);
            }
            return new GhostBrain(new InfluenceMap(grid), GhostTuning.Default, modes ?? ModeTuning.Servant);
        }

        static Vector3 Feet(int x, int z) => new Vector3(x + 0.5f, 0f, z + 0.5f);
        static readonly Vector3 Player = Feet(10, 7);

        [Test]
        public void AimedAt_FleesAtOnce_IntoAWallOutOfTheAim()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            Vector3 ghost = Feet(14, 7);
            brain.See(Player, Vector3.right, WeaponKind.IceGun, false, now: 1f);      // aiming straight at the ghost

            brain.Think(ghost, now: 1f);

            Assert.AreEqual(GhostMode.Flee, brain.Mode);
            Assert.IsTrue(brain.LastInputs.Aimed);
            Assert.IsTrue(grid.Is(brain.TargetCell, CellFlags.Solid), "a refuge inside a wall");
            var aim = new AimCone(Player + Vector3.up * PositionQuery.EyeHeight, Vector3.right, 12f, 15f);
            Assert.IsFalse(aim.Contains(grid.CellCentre(brain.TargetCell)), "and out of the aim");
            Assert.AreEqual(1, brain.RouteVersion, "the flight route is ready at once, no A* slices");
            Assert.AreEqual(grid.CellCentre(brain.TargetCell), brain.Waypoints[brain.WaypointCount - 1]);
        }

        [Test]
        public void Flee_HoldsForItsMinimumTime_WhileTheAimWavers()
        {
            GhostBrain brain = NewBrain(out _);
            Vector3 ghost = Feet(14, 7);
            brain.See(Player, Vector3.right, WeaponKind.IceGun, false, now: 1f);
            brain.Think(ghost, now: 1f);

            brain.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 1.4f);   // aim swung away
            brain.Think(ghost, now: 1.4f);
            Assert.AreEqual(GhostMode.Flee, brain.Mode, "within fleeMinSeconds");

            brain.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 3f);
            brain.Think(ghost, now: 3f);
            Assert.AreNotEqual(GhostMode.Flee, brain.Mode, "dwell over and no longer aimed at");
        }

        [Test]
        public void Strike_LandsWhenItsEyesPutThePlayerInReach_ThenFadesAway()
        {
            GhostBrain brain = NewBrain(out _);
            Vector3 ghost = Feet(11, 7);                                                 // a metre behind the player
            Vector3 struckAt = Vector3.zero;
            int strikes = 0;
            brain.Struck += at => { strikes++; struckAt = at; };
            brain.See(Player, Vector3.left, WeaponKind.Pistol, false, now: 1f);          // looking away from the ghost

            brain.Think(ghost, now: 1f);

            Assert.AreEqual(GhostMode.Strike, brain.Mode);
            Assert.AreEqual(1, strikes);
            Assert.AreEqual(Player, struckAt);

            brain.Think(ghost, now: 1.2f);
            Assert.AreEqual(GhostMode.Flee, brain.Mode, "hit and fade into the wall");

            brain.See(Player, Vector3.left, WeaponKind.Pistol, false, now: 3f);
            brain.Think(ghost, now: 3f);
            Assert.AreEqual(1, strikes, "cooldown: no second strike yet");
        }

        [Test]
        public void Strike_ClosesInButDoesNotLand_OnAStaleSighting()
        {
            GhostBrain brain = NewBrain(out GhostGrid grid);
            int strikes = 0;
            brain.Struck += at => strikes++;
            brain.See(Player, Vector3.left, WeaponKind.Pistol, false, now: 0f);

            brain.Think(Feet(11, 7), now: 0.8f);                                         // last saw them 0.8 s ago

            Assert.AreEqual(GhostMode.Strike, brain.Mode);
            Assert.AreEqual(0, strikes, "its eyes have not confirmed the player is still there");
            Assert.AreEqual(grid.CellAt(Player + Vector3.up * 0.1f), brain.TargetCell, "closing on the belief");
        }

        [Test]
        public void IceGun_FreezesIt_ThenItThawsAndFlees()
        {
            GhostBrain brain = NewBrain(out _);
            Vector3 ghost = Feet(14, 7);
            int freed = 0;
            brain.Freed += () => freed++;
            brain.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 1f);
            brain.Think(ghost, now: 1f);

            brain.Freeze(3f, now: 1.1f);
            Assert.AreEqual(GhostMode.Frozen, brain.Mode);
            Assert.AreEqual(-1, brain.TargetCell);

            brain.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 2f);
            Assert.IsFalse(brain.Think(ghost, now: 2f));
            Assert.AreEqual(GhostMode.Frozen, brain.Mode, "still frozen");
            Assert.AreEqual(0, freed);

            brain.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 4.2f);
            brain.Think(ghost, now: 4.2f);
            Assert.AreEqual(1, freed);
            Assert.AreEqual(GhostMode.Flee, brain.Mode, "thawed: it flees");
        }

        [Test]
        public void WeaponTactics_CarefulWithTheIceGun_BoldOtherwise()
        {
            GhostBrain careful = NewBrain(out _);
            careful.See(Player, Vector3.forward, WeaponKind.IceGun, false, now: 1f);     // held, aimed elsewhere
            careful.Think(Feet(16, 3), now: 1f);

            GhostBrain bold = NewBrain(out _);
            bold.See(Player, Vector3.forward, WeaponKind.Pistol, false, now: 1f);
            bold.Think(Feet(16, 3), now: 1f);

            Assert.AreEqual(GhostMode.Stalk, careful.Mode);
            Assert.AreEqual(ModeTuning.Servant.carefulRange.min, careful.Scorer.Query.Range.min, 1e-4f, "keeps further off");
            Assert.AreEqual(GhostTuning.Default.weights.cover * ModeTuning.Servant.carefulCoverScale,
                            careful.Scorer.Query.Weights.cover, 1e-4f, "hugs cover");
            Assert.AreEqual(GhostTuning.Default.range.min, bold.Scorer.Query.Range.min, 1e-4f);
            Assert.AreEqual(GhostTuning.Default.weights.cover, bold.Scorer.Query.Weights.cover, 1e-4f);
        }

        [Test]
        public void AHeardIceGunShot_AlsoMakesItCareful()
        {
            GhostBrain brain = NewBrain(out _);
            brain.Hear(Player, NoiseKind.IceGun, now: 1f);

            Assert.IsTrue(brain.KnowsIceGun(1f));
            Assert.IsFalse(brain.KnowsIceGun(1f + ModeTuning.Servant.iceGunMemory + 1f), "forgotten in time");
        }

        [Test]
        public void Profile_CarriesTuningModesAndLeash()
        {
            var profile = ScriptableObject.CreateInstance<GhostProfile>();
            profile.Set(GhostTuning.Default, ModeTuning.Mara, 40f, "test");

            Assert.AreEqual(ModeTuning.Mara.aggression, profile.Modes.aggression);
            Assert.AreEqual(40f, profile.Leash);
            Object.DestroyImmediate(profile);
        }
    }
}
