using Hollowmere.AI.Biscuit;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.Tests.Biscuit
{
    /// <summary>The whole brain against a fake body: what Biscuit decides in each situation.</summary>
    public class BiscuitBrainTests
    {
        static readonly Vector3 Here = Vector3.zero;
        static readonly Vector3 Beside = new Vector3(1f, 0f, 0f);

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

        static BiscuitPerception See(float now, Vector3 biscuit, Vector3 player, float health = 1f,
                                     float ammo = 1f, int ammoSpots = 0, int pages = 0) =>
            new BiscuitPerception
            {
                Now = now, Position = biscuit, HasPlayer = true, PlayerPosition = player,
                PlayerLook = Vector3.forward, PlayerHealth01 = health, PlayerAmmo01 = ammo,
                AmmoSpotsLeft = ammoSpots, PagesLeft = pages,
            };

        // The current plan as text, for example "Follow > Growl".
        static string PlanText(BiscuitBrain b)
        {
            var names = new string[b.PlanLength];
            for (int i = 0; i < b.PlanLength; i++) names[i] = b.Plan[i].Name;
            return string.Join(" > ", names);
        }

        // ------------------------------------------------------------ warnings

        [Test]
        public void BesideThePlayer_GrowlsRatherThanBarks()
        {
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(0f, 0f, 15f), 0f);
            brain.Think(See(0f, Here, Beside));

            Assert.AreEqual("Warn player", brain.CurrentGoal.Name);
            Assert.AreEqual("Growl", PlanText(brain));
            Assert.AreEqual(1, body.Growls);
            Assert.AreEqual(0, body.Barks);
        }

        [Test]
        public void FarFromThePlayer_BarksBecauseRunningBackTakesLonger()
        {
            // Follow 15 m costs 1 + 15 x 0.2 = 4, plus Growl 1 = 5. A bark costs 3.
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(15f, 0f, 10f), 0f);
            brain.Think(See(0f, Here, new Vector3(15f, 0f, 0f)));

            Assert.AreEqual("Bark", PlanText(brain));
            Assert.AreEqual(1, body.Barks);
        }

        [Test]
        public void WithMoreDeadInEarshot_RunsBackToGrowlInstead()
        {
            // Three of the dead in earshot: a bark costs 3 + 2 x 2 = 7. Follow 8 m (2.6) + Growl (1) = 3.6.
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(8f, 0f, 10f), 0f);
            brain.OnSmelt(2, EnemyKind.Dead, new Vector3(0f, 0f, 12f), 0f);
            brain.OnSmelt(3, EnemyKind.Dead, new Vector3(5f, 0f, -10f), 0f);
            brain.Think(See(0f, Here, new Vector3(8f, 0f, 0f)));

            Assert.AreEqual("Follow > Growl", PlanText(brain));
            Assert.AreEqual(3.6f, brain.PlanCost, 1e-4f);
            Assert.AreEqual(0, body.Barks);
            Assert.AreEqual(new Vector3(8f, 0f, 0f), body.Destination);
        }

        [Test]
        public void NewThreatAfterAWarning_IsWarnedAboutAgain()
        {
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(0f, 0f, 15f), 0f);
            brain.Think(See(0f, Here, Beside));
            brain.Think(See(1.3f, Here, Beside));   // a growl takes 1.2 s
            brain.Think(See(1.4f, Here, Beside));
            Assert.IsTrue(brain.Has(Fact.PlayerWarned));

            brain.OnSmelt(2, EnemyKind.Dead, new Vector3(10f, 0f, 0f), 1.5f);
            brain.Think(See(1.5f, Here, Beside));
            Assert.IsFalse(brain.Has(Fact.PlayerWarned));
            Assert.AreEqual(2, body.Growls);
        }

        // ------------------------------------------------------------ enemy kinds

        [Test]
        public void GhostNearby_WhinesInsteadOfGrowling()
        {
            brain.OnSmelt(1, EnemyKind.Ghost, new Vector3(0f, 0f, 10f), 0f);
            brain.Think(See(0f, Here, Beside));

            Assert.AreEqual("Whine", PlanText(brain));
            Assert.AreEqual(1, body.Whines);
            Assert.AreEqual(0, body.Growls);
            Assert.IsNotNull(body.Destination, "backs off behind the player");
        }

        [Test]
        public void CreatureInEarshot_MakesBiscuitChooseSilence()
        {
            // One zombie near a player 12 m away: Bark (3) beats running back to growl (3.4 + 1 = 4.4).
            var quietBrain = new BiscuitBrain(tuning, new FakeBiscuitBody());
            quietBrain.OnSmelt(1, EnemyKind.Dead, new Vector3(12f, 0f, 8f), 0f);
            quietBrain.Think(See(0f, Here, new Vector3(12f, 0f, 0f)));
            Assert.AreEqual("Bark", PlanText(quietBrain));

            // The same, with the creature 30 m behind Biscuit. It hears a bark from 25 x 1.6 = 40 m,
            // so a bark now costs 3 + 2 x (1 + 1.6 - 1) = 6.2 and Biscuit runs back to growl.
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(12f, 0f, 8f), 0f);
            brain.OnSmelt(2, EnemyKind.Creature, new Vector3(0f, 0f, -30f), 0f);
            brain.Think(See(0f, Here, new Vector3(12f, 0f, 0f)));
            Assert.AreEqual("Follow > Growl", PlanText(brain));
        }

        [Test]
        public void HurtPlayerWithOnlyAGhostNear_WhinesBecauseGhostsIgnoreBarks()
        {
            // The player is in trouble, but luring needs something that chases noise.
            brain.OnSmelt(1, EnemyKind.Ghost, new Vector3(1f, 0f, 6.5f), 0f);
            brain.Think(See(0f, Here, Beside, health: 0.2f));

            Assert.AreEqual("Warn player", brain.CurrentGoal.Name);
            Assert.AreEqual("Whine", PlanText(brain));
            Assert.AreEqual(0, body.Barks);
        }

        // ------------------------------------------------------------ danger

        [Test]
        public void DeadRightNextToBiscuit_HidesFirst()
        {
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(1f, 0f, 0f), 0f);
            brain.Think(See(0f, Here, new Vector3(2f, 0f, 0f)));

            Assert.AreEqual("Stay safe", brain.CurrentGoal.Name);
            Assert.AreEqual("Hide", PlanText(brain));
            Assert.IsNotNull(body.Destination);
        }

        [Test]
        public void HurtPlayerWithDeadClose_LuresThemAwayWithTwoBarks()
        {
            // Six metres from the player (trouble), but more than 4 m from Biscuit (not in danger).
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(1f, 0f, 6f), 0f);

            brain.Think(See(0f, Here, Beside, health: 0.2f));
            Assert.AreEqual("Lure", PlanText(brain));
            Assert.AreEqual(0, body.Barks, "runs to the spot first");

            body.Arrived = true;
            brain.Think(See(0.2f, Here, Beside, health: 0.2f));
            Assert.AreEqual(1, body.Barks);
            brain.Think(See(1.3f, Here, Beside, health: 0.2f));
            Assert.AreEqual(2, body.Barks);

            // Lure is now cooling down, so the same situation does not start another one at once.
            brain.Think(See(1.4f, Here, Beside, health: 0.2f));
            Assert.IsNull(brain.CurrentAction);
        }

        [Test]
        public void ActionThatFails_IsLeftOutAndAnotherWayIsFound()
        {
            // Nowhere to hide: Hide fails, Stay safe has no other plan, so Biscuit warns instead.
            body.RetreatFound = false;
            Vector3 player = new Vector3(0f, 0f, 1f);
            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(1f, 0f, 0f), 0f);

            brain.Think(See(0f, Here, player));
            Assert.AreEqual("Hide", brain.LastFailed.Name);

            brain.Think(See(0.1f, Here, player));
            Assert.AreEqual("Warn player", brain.CurrentGoal.Name);
            Assert.AreEqual(1, body.Growls);
        }

        // ------------------------------------------------------------ supplies

        [Test]
        public void LowOnAmmo_PlansSniffFetchAndBring()
        {
            brain.Think(See(0f, Here, Beside, ammo: 0.1f, ammoSpots: 1));

            Assert.AreEqual("Resupply", brain.CurrentGoal.Name);
            Assert.AreEqual("Sniff > Fetch ammo > Bring ammo", PlanText(brain));
        }

        [Test]
        public void Resupply_FetchesDeliversAndThenWaits()
        {
            Vector3 box = new Vector3(0f, 0f, 10f);
            brain.Think(See(0f, Here, Beside, ammo: 0.1f, ammoSpots: 1));   // starts sniffing

            brain.OnFoundPoint(PointKind.Ammo, 7, box);                      // and smells a box
            brain.Think(See(0.1f, Here, Beside, ammo: 0.1f, ammoSpots: 1));
            Assert.AreEqual("Fetch ammo > Bring ammo", PlanText(brain));

            brain.Think(See(1f, box, Beside, ammo: 0.1f, ammoSpots: 1));    // reaches the box
            Assert.IsTrue(brain.Carrying);

            brain.Think(See(1.1f, box, Beside, ammo: 0.1f));
            Assert.AreEqual("Bring ammo", brain.CurrentAction.Name);

            brain.Think(See(2f, new Vector3(1f, 0f, 1f), Beside, ammo: 0.1f));   // back at the player's feet
            Assert.AreEqual(1, body.Deliveries);
            Assert.IsFalse(brain.Carrying);

            // The player has not picked it up yet: Biscuit does not run off for more straight away.
            brain.Think(See(2.2f, new Vector3(1f, 0f, 1f), Beside, ammo: 0.1f, ammoSpots: 1));
            Assert.AreNotEqual("Resupply", brain.CurrentGoal?.Name);
        }

        [Test]
        public void AmmoTakenBeforeBiscuitGetsThere_PlansAgain()
        {
            brain.OnFoundPoint(PointKind.Ammo, 7, new Vector3(0f, 0f, 10f));
            brain.Think(See(0f, Here, Beside, ammo: 0.1f, ammoSpots: 1));
            Assert.AreEqual("Fetch ammo", brain.CurrentAction.Name);

            brain.OnPointGone(7);   // someone else picked it up
            brain.Think(See(0.1f, Here, Beside, ammo: 0.1f));

            Assert.AreEqual("Sniff", brain.CurrentAction.Name);
            Assert.IsNull(brain.LastFailed, "interrupted by the world, not a failure");
        }

        // ------------------------------------------------------------ evidence

        [Test]
        public void QuietWithAPageNearby_LeadsThePlayerToIt()
        {
            Vector3 page = new Vector3(0f, 0f, 12f);
            brain.Think(See(0f, Here, Beside, pages: 1));
            Assert.AreEqual("Sniff > Lead to evidence", PlanText(brain));

            brain.OnFoundPoint(PointKind.Evidence, 9, page);
            brain.Think(See(0.1f, Here, Beside, pages: 1));
            Assert.AreEqual("Lead to evidence", brain.CurrentAction.Name);
            Assert.AreEqual(page, body.Destination);

            // The player follows Biscuit to the page.
            brain.Think(See(3f, new Vector3(0f, 0f, 11f), new Vector3(0f, 0f, 10.5f), pages: 1));
            CollectionAssert.AreEqual(new[] { 9 }, body.Shown);
        }

        [Test]
        public void PageAFewMetresAway_PlansThreeSteps()
        {
            // Not at the player's side and nothing smelt yet: Follow and Sniff (either order), then Lead.
            brain.Think(See(0f, Here, new Vector3(5f, 0f, 0f), pages: 1));

            Assert.AreEqual("Show evidence", brain.CurrentGoal.Name);
            Assert.AreEqual(3, brain.PlanLength);
            Assert.AreEqual("Lead to evidence", brain.Plan[2].Name);
        }

        [Test]
        public void EnemyTurnsUpWhileLeading_StopsToWarn()
        {
            brain.OnFoundPoint(PointKind.Evidence, 9, new Vector3(0f, 0f, 12f));
            brain.Think(See(0f, Here, Beside, pages: 1));
            Assert.AreEqual("Lead to evidence", brain.CurrentAction.Name);

            brain.OnSmelt(1, EnemyKind.Dead, new Vector3(5f, 0f, -10f), 0.1f);
            brain.Think(See(0.1f, Here, Beside, pages: 1));

            Assert.AreEqual("Growl", brain.CurrentAction.Name);
            Assert.IsNull(brain.LastFailed);
        }

        // ------------------------------------------------------------ following and scouting

        [Test]
        public void NoRouteToThePlayer_FollowIsLeftOutAndBiscuitDoesSomethingElse()
        {
            // A locked door between them: Follow fails, so for a few seconds Biscuit sniffs where it is.
            body.NoRoute = true;
            Vector3 player = new Vector3(10f, 0f, 0f);

            brain.Think(See(0f, Here, player));
            Assert.AreEqual("Follow", brain.LastFailed.Name);

            brain.Think(See(0.1f, Here, player));
            Assert.AreEqual("Scout", brain.CurrentGoal.Name);
            Assert.AreEqual("Sniff", brain.CurrentAction.Name);

            // Once the ban runs out, Biscuit tries to reach the player again.
            body.NoRoute = false;
            brain.Think(See(1.7f, Here, player));   // the sniff ends
            brain.Think(See(3.2f, Here, player));
            Assert.AreEqual("Follow", brain.CurrentAction.Name);
        }

        [Test]
        public void SniffThatFindsNothing_IsNotRepeatedUntilTheAreaGoesStale()
        {
            brain.Think(See(0f, Here, Beside));
            Assert.AreEqual("Sniff", PlanText(brain));

            brain.Think(See(1.6f, Here, Beside));   // a sniff takes 1.5 s
            brain.Think(See(1.7f, Here, Beside));
            Assert.IsNull(brain.CurrentAction, "nothing left to do: the area is scouted");

            brain.Think(See(13.8f, Here, Beside));   // 12 s later the area counts as unscouted
            Assert.AreEqual("Sniff", brain.CurrentAction.Name);
            Assert.AreEqual(2, body.Sniffs);
        }

        [Test]
        public void PlayerFarAway_FollowStartsBehindThem()
        {
            brain.Think(See(0f, Here, new Vector3(100f, 0f, 0f)));

            Assert.AreEqual("Follow", PlanText(brain));
            Assert.AreEqual(1, body.Warps);
        }

        [Test]
        public void NoPlayer_Idles()
        {
            brain.Think(new BiscuitPerception { Now = 0f, Position = Here });

            Assert.IsNull(brain.CurrentGoal);
            Assert.IsNull(brain.CurrentAction);
        }

        // ------------------------------------------------------------ memory

        [Test]
        public void Beliefs_FadeAndAreForgotten()
        {
            var memory = new BiscuitMemory(tuning);
            memory.Remember(1, EnemyKind.Dead, Vector3.zero, 0.6f, false, 0f);

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
            memory.Remember(1, EnemyKind.Creature, Vector3.zero, 1f, true, 0f);
            memory.Remember(1, EnemyKind.Dead, Vector3.one, 0.6f, false, 0f);

            Assert.AreEqual(1f, memory.ConfidenceOf(memory[0], 0f), 1e-4f);
            Assert.AreEqual(EnemyKind.Creature, memory[0].Kind, "a groan does not overrule what the nose said");
            Assert.AreEqual(1, memory.ThreatsMet, "the same source is not a new threat");
        }
    }
}
