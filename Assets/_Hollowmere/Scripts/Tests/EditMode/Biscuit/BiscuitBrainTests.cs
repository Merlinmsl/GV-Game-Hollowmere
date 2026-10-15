using Hollowmere.AI.Biscuit;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Biscuit
{
    /// <summary>The whole brain against a fake body: what Biscuit decides in each situation.</summary>
    public class BiscuitBrainTests
    {
        BiscuitTuning tuning;
        FakeBiscuitBody body;
        BiscuitBrain brain;

        [SetUp]
        public void SetUp()
        {
            tuning = new BiscuitTuning();
            body = new FakeBiscuitBody();
            brain = new BiscuitBrain(tuning, body);
        }

        static BiscuitPerception See(float now, Vector3 biscuit, Vector3 player, float health = 1f) =>
            new BiscuitPerception
            {
                Now = now, Position = biscuit, HasPlayer = true,
                PlayerPosition = player, PlayerLook = Vector3.forward, PlayerHealth01 = health,
            };

        // The current plan as text, for example "Follow > Growl".
        string PlanText()
        {
            var names = new string[brain.PlanLength];
            for (int i = 0; i < brain.PlanLength; i++) names[i] = brain.Plan[i].Name;
            return string.Join(" > ", names);
        }

        [Test]
        public void BesideThePlayer_GrowlsRatherThanBarks()
        {
            brain.OnSmelt(1, new Vector3(0f, 0f, 15f), 0f);
            brain.Think(See(0f, Vector3.zero, new Vector3(1f, 0f, 0f)));

            Assert.AreEqual("Warn player", brain.CurrentGoal.Name);
            Assert.AreEqual("Growl", PlanText());
            Assert.AreEqual(1, body.Growls);
            Assert.AreEqual(0, body.Barks);
        }

        [Test]
        public void FarFromThePlayer_BarksBecauseRunningBackTakesLonger()
        {
            // Follow 15 m costs 1 + 15 x 0.2 = 4, plus Growl 1 = 5. A bark costs 3.
            brain.OnSmelt(1, new Vector3(15f, 0f, 10f), 0f);
            brain.Think(See(0f, Vector3.zero, new Vector3(15f, 0f, 0f)));

            Assert.AreEqual("Bark", PlanText());
            Assert.AreEqual(1, body.Barks);
        }

        [Test]
        public void WithMoreDeadInEarshot_RunsBackToGrowlInstead()
        {
            // Three of the dead in earshot: a bark costs 3 + 2 x 2 = 7. Follow 8 m (2.6) + Growl (1) = 3.6.
            brain.OnSmelt(1, new Vector3(8f, 0f, 10f), 0f);
            brain.OnSmelt(2, new Vector3(0f, 0f, 12f), 0f);
            brain.OnSmelt(3, new Vector3(5f, 0f, -10f), 0f);
            brain.Think(See(0f, Vector3.zero, new Vector3(8f, 0f, 0f)));

            Assert.AreEqual("Follow > Growl", PlanText());
            Assert.AreEqual(3.6f, brain.PlanCost, 1e-4f);
            Assert.AreEqual(0, body.Barks);
            Assert.AreEqual(new Vector3(8f, 0f, 0f), body.Destination);
        }

        [Test]
        public void DeadRightNextToBiscuit_HidesFirst()
        {
            brain.OnSmelt(1, new Vector3(1f, 0f, 0f), 0f);
            brain.Think(See(0f, Vector3.zero, new Vector3(2f, 0f, 0f)));

            Assert.AreEqual("Stay safe", brain.CurrentGoal.Name);
            Assert.AreEqual("Hide", PlanText());
            Assert.IsNotNull(body.Destination);
        }

        [Test]
        public void HurtPlayerWithDeadClose_LuresThemAwayWithTwoBarks()
        {
            // Six metres from the player (trouble), but more than 4 m from Biscuit (not in danger).
            Vector3 player = new Vector3(1f, 0f, 0f);
            brain.OnSmelt(1, new Vector3(1f, 0f, 6f), 0f);

            brain.Think(See(0f, Vector3.zero, player, health: 0.2f));
            Assert.AreEqual("Lure", PlanText());
            Assert.AreEqual(0, body.Barks, "runs to the spot first");

            body.Arrived = true;
            brain.Think(See(0.2f, Vector3.zero, player, health: 0.2f));
            Assert.AreEqual(1, body.Barks);
            brain.Think(See(1.3f, Vector3.zero, player, health: 0.2f));
            Assert.AreEqual(2, body.Barks);

            // Lure is now cooling down, so the same situation does not start another one at once.
            brain.Think(See(1.4f, Vector3.zero, player, health: 0.2f));
            Assert.IsNull(brain.CurrentAction);
        }

        [Test]
        public void ActionThatFails_IsLeftOutAndAnotherWayIsFound()
        {
            // Nowhere to hide: Hide fails, Stay safe has no other plan, so Biscuit warns instead.
            body.RetreatFound = false;
            Vector3 player = new Vector3(0f, 0f, 1f);
            brain.OnSmelt(1, new Vector3(1f, 0f, 0f), 0f);

            brain.Think(See(0f, Vector3.zero, player));
            Assert.AreEqual("Hide", brain.LastFailed.Name);

            brain.Think(See(0.1f, Vector3.zero, player));
            Assert.AreEqual("Warn player", brain.CurrentGoal.Name);
            Assert.AreEqual(1, body.Growls);
        }

        [Test]
        public void NoRouteToThePlayer_FollowIsLeftOutAndBiscuitDoesSomethingElse()
        {
            // A locked door between them: Follow fails, so for a few seconds Biscuit sniffs where it is.
            body.NoRoute = true;
            Vector3 player = new Vector3(10f, 0f, 0f);

            brain.Think(See(0f, Vector3.zero, player));
            Assert.AreEqual("Follow", brain.LastFailed.Name);

            brain.Think(See(0.1f, Vector3.zero, player));
            Assert.AreEqual("Scout", brain.CurrentGoal.Name);
            Assert.AreEqual("Sniff", brain.CurrentAction.Name);

            // Once the ban runs out, Biscuit tries to reach the player again.
            body.NoRoute = false;
            brain.Think(See(1.7f, Vector3.zero, player));   // the sniff ends
            brain.Think(See(3.2f, Vector3.zero, player));
            Assert.AreEqual("Follow", brain.CurrentAction.Name);
        }

        [Test]
        public void SniffThatFindsNothing_IsNotRepeatedUntilTheAreaGoesStale()
        {
            Vector3 player = new Vector3(1f, 0f, 0f);
            brain.Think(See(0f, Vector3.zero, player));
            Assert.AreEqual("Sniff", PlanText());

            brain.Think(See(1.6f, Vector3.zero, player));   // a sniff takes 1.5 s
            brain.Think(See(1.7f, Vector3.zero, player));
            Assert.IsNull(brain.CurrentAction, "nothing left to do: the area is scouted");

            brain.Think(See(13.8f, Vector3.zero, player));   // 12 s later the area counts as unscouted
            Assert.AreEqual("Sniff", brain.CurrentAction.Name);
            Assert.AreEqual(2, body.Sniffs);
        }

        [Test]
        public void NewThreatAfterAWarning_IsWarnedAboutAgain()
        {
            Vector3 player = new Vector3(1f, 0f, 0f);
            brain.OnSmelt(1, new Vector3(0f, 0f, 15f), 0f);
            brain.Think(See(0f, Vector3.zero, player));
            brain.Think(See(1.3f, Vector3.zero, player));   // a growl takes 1.2 s
            brain.Think(See(1.4f, Vector3.zero, player));
            Assert.IsTrue(brain.Has(Fact.PlayerWarned));

            brain.OnSmelt(2, new Vector3(10f, 0f, 0f), 1.5f);
            brain.Think(See(1.5f, Vector3.zero, player));
            Assert.IsFalse(brain.Has(Fact.PlayerWarned));
            Assert.AreEqual(2, body.Growls);
        }

        [Test]
        public void PlayerFarAway_FollowStartsBehindThem()
        {
            brain.Think(See(0f, Vector3.zero, new Vector3(100f, 0f, 0f)));

            Assert.AreEqual("Follow", PlanText());
            Assert.AreEqual(1, body.Warps);
        }

        [Test]
        public void NoPlayer_Idles()
        {
            brain.Think(new BiscuitPerception { Now = 0f, Position = Vector3.zero });

            Assert.IsNull(brain.CurrentGoal);
            Assert.IsNull(brain.CurrentAction);
        }

        [Test]
        public void Beliefs_FadeAndAreForgotten()
        {
            var memory = new BiscuitMemory(tuning);
            memory.Remember(1, Vector3.zero, 0.6f, false, 0f);

            Assert.AreEqual(0.6f * Mathf.Exp(-5f / 6f), memory.ConfidenceOf(memory[0], 5f), 1e-4f);

            // 0.6 x e^(-t / 6) drops below 0.1 at t = 6 ln 6 = 10.75 s.
            memory.ForgetFaded(10.5f);
            Assert.AreEqual(1, memory.Count);
            memory.ForgetFaded(11f);
            Assert.AreEqual(0, memory.Count);
        }

        [Test]
        public void HearingAfterSmelling_DoesNotLowerConfidence()
        {
            var memory = new BiscuitMemory(tuning);
            memory.Remember(1, Vector3.zero, 1f, true, 0f);
            memory.Remember(1, Vector3.one, 0.6f, false, 0f);

            Assert.AreEqual(1f, memory.ConfidenceOf(memory[0], 0f), 1e-4f);
            Assert.AreEqual(1, memory.ThreatsMet, "the same source is not a new threat");
        }
    }
}
