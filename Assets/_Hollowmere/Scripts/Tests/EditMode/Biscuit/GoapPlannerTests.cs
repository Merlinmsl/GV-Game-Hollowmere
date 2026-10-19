using Hollowmere.AI.Biscuit;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Hollowmere.Tests.Biscuit
{
    /// <summary>The A* planner on its own, with Biscuit's real actions and fixed costs.</summary>
    public class GoapPlannerTests
    {
        // Indexes into the action array, in the brain's order.
        const int Follow = 0;
        const int Sniff = 1;
        const int Growl = 2;
        const int Whine = 3;
        const int Bark = 4;
        const int Fetch = 7;
        const int Bring = 8;

        static readonly Condition Warned = new Condition(Fact.PlayerWarned);
        static readonly Condition Resupplied = new Condition(Fact.None, Fact.PlayerLowOnAmmo);

        readonly GoapPlanner planner = new GoapPlanner();
        readonly BiscuitAction[] plan = new BiscuitAction[GoapPlanner.MaxDepth];
        BiscuitAction[] actions;
        float[] costs;
        bool[] usable;

        [SetUp]
        public void SetUp()
        {
            actions = new BiscuitAction[]
            {
                new FollowAction(), new SniffAction(), new GrowlAction(), new WhineAction(), new BarkAction(),
                new HideAction(), new LureAction(), new FetchAmmoAction(), new BringAmmoAction(),
                new LeadToEvidenceAction(),
            };
            costs = new[] { 1f, 1f, 1f, 1f, 3f, 1f, 5f, 3f, 1.2f, 2f };
            usable = new bool[actions.Length];
            for (int i = 0; i < usable.Length; i++) usable[i] = true;
        }

        static ushort State(params Fact[] facts)
        {
            ushort state = 0;
            foreach (Fact fact in facts) state |= (ushort)fact;
            return state;
        }

        bool Plan(ushort start, Condition goal, out int length, out float cost) =>
            planner.Plan(start, goal, actions, actions.Length, costs, usable, plan, out length, out cost);

        [Test]
        public void GoalAlreadyMet_GivesAnEmptyPlan()
        {
            Assert.IsTrue(Plan(State(Fact.PlayerWarned), Warned, out int length, out float cost));
            Assert.AreEqual(0, length);
            Assert.AreEqual(0f, cost);
        }

        [Test]
        public void AtThePlayersSide_GrowlIsCheaperThanBark()
        {
            Assert.IsTrue(Plan(State(Fact.NearPlayer, Fact.EnemyNearby), Warned, out int length, out float cost));
            Assert.AreEqual(1, length);
            Assert.AreSame(actions[Growl], plan[0]);
            Assert.AreEqual(1f, cost);
        }

        [Test]
        public void WithAGhostNear_WhineTakesThePlaceOfGrowl()
        {
            Assert.IsTrue(Plan(State(Fact.NearPlayer, Fact.EnemyNearby, Fact.GhostNearby), Warned, out int length, out _));
            Assert.AreEqual(1, length);
            Assert.AreSame(actions[Whine], plan[0]);
        }

        [Test]
        public void AwayFromThePlayer_WeighsFollowAndGrowlAgainstBark()
        {
            ushort start = State(Fact.EnemyNearby);

            // Close by: Follow (1) + Growl (1) = 2 beats Bark (3).
            costs[Follow] = 1f;
            Assert.IsTrue(Plan(start, Warned, out int length, out float cost));
            Assert.AreEqual(2, length);
            Assert.AreSame(actions[Follow], plan[0]);
            Assert.AreSame(actions[Growl], plan[1]);
            Assert.AreEqual(2f, cost, 1e-4f);

            // 20 m away: Follow (5) + Growl (1) = 6 loses to Bark (3).
            costs[Follow] = 5f;
            Assert.IsTrue(Plan(start, Warned, out length, out cost));
            Assert.AreEqual(1, length);
            Assert.AreSame(actions[Bark], plan[0]);
            Assert.AreEqual(3f, cost, 1e-4f);
        }

        [Test]
        public void LowOnAmmo_BuildsAThreeStepPlan()
        {
            // Nothing known yet: sniff for it, fetch it, bring it. 1 + 3 + 1.2 = 5.2.
            Assert.IsTrue(Plan(State(Fact.NearPlayer, Fact.PlayerLowOnAmmo), Resupplied, out int length, out float cost));
            Assert.AreEqual(3, length);
            Assert.AreSame(actions[Sniff], plan[0]);
            Assert.AreSame(actions[Fetch], plan[1]);
            Assert.AreSame(actions[Bring], plan[2]);
            Assert.AreEqual(5.2f, cost, 1e-4f);
        }

        [Test]
        public void UnusableAction_IsLeftOut()
        {
            usable[Growl] = false;
            Assert.IsTrue(Plan(State(Fact.NearPlayer, Fact.EnemyNearby), Warned, out int length, out _));
            Assert.AreEqual(1, length);
            Assert.AreSame(actions[Bark], plan[0]);
        }

        [Test]
        public void NoActionReachesTheGoal_ReturnsFalse()
        {
            // Nothing can warn the player about enemies when Biscuit knows of none.
            Assert.IsFalse(Plan(State(Fact.NearPlayer), Warned, out int length, out _));
            Assert.AreEqual(0, length);
        }

        [Test]
        public void AStarMatchesDijkstra_FromEveryStateForEveryGoal()
        {
            // An admissible heuristic must not change the answer: A* has to find the same cheapest
            // cost as a search with no heuristic at all, from all 4096 states, for each of Biscuit's goals.
            Condition[] goals =
            {
                new Condition(Fact.None, Fact.InDanger), new Condition(Fact.None, Fact.PlayerInTrouble),
                Warned, Resupplied, new Condition(Fact.PlayerAtEvidence), new Condition(Fact.AreaScouted),
                new Condition(Fact.NearPlayer),
            };
            costs = new[] { 2.6f, 1f, 1f, 1f, 7f, 1f, 5f, 3f, 1.2f, 2f };
            var dijkstra = new GoapPlanner { UseHeuristic = false };
            var dijkstraPlan = new BiscuitAction[GoapPlanner.MaxDepth];

            for (int s = 0; s < WorldState.StateCount; s++)
            {
                foreach (Condition goal in goals)
                {
                    bool found = Plan((ushort)s, goal, out _, out float aStarCost);
                    bool dijkstraFound = dijkstra.Plan((ushort)s, goal, actions, actions.Length, costs, usable,
                                                       dijkstraPlan, out _, out float dijkstraCost);
                    Assert.AreEqual(dijkstraFound, found, "from " + (Fact)s);
                    if (found) Assert.AreEqual(dijkstraCost, aStarCost, 1e-4f, "from " + (Fact)s);
                }
            }
        }

        [Test]
        public void Heuristic_SearchesNoMoreStatesThanDijkstra()
        {
            var dijkstra = new GoapPlanner { UseHeuristic = false };
            var dijkstraPlan = new BiscuitAction[GoapPlanner.MaxDepth];
            ushort start = State(Fact.PlayerLowOnAmmo);

            Plan(start, Resupplied, out _, out _);
            dijkstra.Plan(start, Resupplied, actions, actions.Length, costs, usable, dijkstraPlan, out _, out _);

            Assert.LessOrEqual(planner.NodesExpanded, dijkstra.NodesExpanded);
        }

        [Test]
        public void Planning_DoesNotAllocate()
        {
            ushort start = State(Fact.PlayerLowOnAmmo);
            Plan(start, Resupplied, out _, out _);   // warm up

            TestDelegate planOnce = () => { Plan(start, Resupplied, out _, out _); };
            Assert.That(planOnce, Is.Not.AllocatingGCMemory());
        }
    }
}
