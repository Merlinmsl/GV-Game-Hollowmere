using Hollowmere.AI.Ghost;
using NUnit.Framework;

namespace Hollowmere.Tests.Ghost
{
    public class ModeSelectorTests
    {
        readonly float[] utility = new float[ModeSelector.Count];

        // Seen just now, sure of it, 6 m off, player side-on, nothing threatening.
        static ModeInputs Seen() => new ModeInputs
        {
            Believes = true,
            Confidence = 1f,
            Freshness = 1f,
            Distance = 6f,
            FacingAway = 0.5f,
            StrikeReady = true,
        };

        GhostMode Choose(ModeInputs i, ModeTuning t, GhostMode current = GhostMode.Idle) =>
            ModeSelector.Choose(i, t, current, utility);

        [Test]
        public void NoBelief_IsIdle()
        {
            Assert.AreEqual(GhostMode.Idle, Choose(new ModeInputs(), ModeTuning.Servant));
        }

        [Test]
        public void SeenAtADistance_Stalks()
        {
            Assert.AreEqual(GhostMode.Stalk, Choose(Seen(), ModeTuning.Servant));
        }

        [Test]
        public void HeardButNotSeen_LiesInWait()
        {
            ModeInputs i = Seen();
            i.Confidence = 0.6f;
            i.Freshness = 0f;
            Assert.AreEqual(GhostMode.Ambush, Choose(i, ModeTuning.Servant));
        }

        [Test]
        public void CloseBehindAPlayerLookingAway_Strikes()
        {
            ModeInputs i = Seen();
            i.Distance = 1.5f;
            i.FacingAway = 1f;
            Assert.AreEqual(GhostMode.Strike, Choose(i, ModeTuning.Servant));
        }

        [Test]
        public void IceGunHeld_MakesItCareful_StrikesLessReadily()
        {
            ModeInputs i = Seen();
            i.Distance = 4f;                                 // side-on, at the start of the hunt
            Assert.AreEqual(GhostMode.Strike, Choose(i, ModeTuning.Servant), "bold: goes for it");

            i.IceGunHeld = true;
            Assert.AreNotEqual(GhostMode.Strike, Choose(i, ModeTuning.Servant), "careful: waits");

            i.FacingAway = 1f;
            i.HuntSeconds = ModeTuning.Servant.patienceSeconds;
            Assert.AreEqual(GhostMode.Strike, Choose(i, ModeTuning.Servant), "but strikes once the back is turned");
        }

        [Test]
        public void Cooldown_VetoesStrike()
        {
            ModeInputs i = Seen();
            i.Distance = 1.5f;
            i.FacingAway = 1f;
            i.StrikeReady = false;
            ModeSelector.Evaluate(i, ModeTuning.Servant, utility);
            Assert.AreEqual(0f, utility[(int)GhostMode.Strike], "a product: one zero vetoes the mode");
        }

        [Test]
        public void PlayerOutsideTheHouse_VetoesStrike()
        {
            ModeInputs i = Seen();
            i.Distance = 1.5f;
            i.FacingAway = 1f;
            i.PlayerOutside = true;
            ModeSelector.Evaluate(i, ModeTuning.Mara, utility);
            Assert.AreEqual(0f, utility[(int)GhostMode.Strike], "ghosts never leave the house");
        }

        [Test]
        public void AimedAt_FleeBeatsEverything_EvenAStrikeChance()
        {
            ModeInputs i = Seen();
            i.Distance = 1.5f;
            i.FacingAway = 1f;
            i.IceGunHeld = true;
            i.Aimed = true;
            Assert.AreEqual(GhostMode.Flee, Choose(i, ModeTuning.Servant, GhostMode.Strike));
            Assert.AreEqual(GhostMode.Flee, Choose(i, ModeTuning.Mara, GhostMode.Strike), "even bold Mara");
        }

        [Test]
        public void Hysteresis_KeepsTheCurrentModeOnANearTie()
        {
            ModeInputs i = Seen();
            i.Confidence = 0.6f;
            i.Freshness = 0.1f;                              // Ambush only just ahead of Stalk
            ModeSelector.Evaluate(i, ModeTuning.Servant, utility);
            Assert.Greater(utility[(int)GhostMode.Ambush], utility[(int)GhostMode.Stalk]);

            Assert.AreEqual(GhostMode.Stalk, Choose(i, ModeTuning.Servant, GhostMode.Stalk), "not worth switching");
            Assert.AreEqual(GhostMode.Ambush, Choose(i, ModeTuning.Servant, GhostMode.Idle));
        }

        [Test]
        public void Mara_StrikesFromFurtherOff_ThanAServant()
        {
            ModeInputs i = Seen();
            i.Distance = 9f;
            i.FacingAway = 1f;
            i.HuntSeconds = 6f;

            Assert.AreEqual(GhostMode.Strike, Choose(i, ModeTuning.Mara, GhostMode.Stalk));
            Assert.AreEqual(GhostMode.Stalk, Choose(i, ModeTuning.Servant, GhostMode.Stalk));
        }

        [Test]
        public void AServantsPatienceRunsOut()
        {
            ModeInputs i = Seen();
            i.Distance = 4f;
            i.FacingAway = 0.25f;                            // mostly facing the ghost

            i.HuntSeconds = 0f;
            Assert.AreEqual(GhostMode.Stalk, Choose(i, ModeTuning.Servant, GhostMode.Stalk));
            i.HuntSeconds = 20f;
            Assert.AreEqual(GhostMode.Strike, Choose(i, ModeTuning.Servant, GhostMode.Stalk));
        }

        [Test]
        public void ResponseCurves_HaveTheirDocumentedShape()
        {
            ModeTuning t = ModeTuning.Servant;
            Assert.AreEqual(1f, ModeSelector.Near(1f, t), 1e-4f, "within strike distance");
            Assert.AreEqual(0f, ModeSelector.Near(t.strikeReach + 1f, t), 1e-4f, "beyond reach");
            Assert.AreEqual(0.75f, ModeSelector.Near(t.strikeDistance + 0.5f * (t.strikeReach - t.strikeDistance), t), 1e-4f, "1 - 0.5^2");
            Assert.AreEqual(0.5f, ModeSelector.Patience(0f, t), 1e-4f);
            Assert.AreEqual(1f, ModeSelector.Patience(t.patienceSeconds * 2f, t), 1e-4f);
        }
    }
}
