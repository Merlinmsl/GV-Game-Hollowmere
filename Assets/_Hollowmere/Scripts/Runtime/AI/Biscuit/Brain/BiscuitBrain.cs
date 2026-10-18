using System;
using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Biscuit's brain: goal-oriented action planning. Each think it
    /// 1. turns what the body sensed into facts,
    /// 2. scores every goal and plans (A*) for the best one that is not met and can be reached,
    /// 3. runs the plan one action at a time, and plans again whenever a fact changes, an action
    ///    finishes or an action fails.
    /// Plain C#: no MonoBehaviour and no scene objects, so it can be unit-tested.
    /// </summary>
    public sealed class BiscuitBrain
    {
        // A place Biscuit has smelt and may go to: ammunition or a page.
        struct KnownPoint
        {
            public bool Valid;
            public int Id;
            public Vector3 Position;
        }

        const int IgnoredCapacity = 8;

        readonly GoapPlanner planner = new GoapPlanner();
        readonly float[] costs;
        readonly bool[] usable;
        readonly float[] bannedUntil;
        readonly float[] ranking;
        readonly int[] goalOrder;
        readonly int[] ignoredPoints = new int[IgnoredCapacity];
        int ignoredCount;
        int ignoredNext;

        KnownPoint knownAmmo;
        KnownPoint knownPage;
        float actionStartTime;
        int warnedAtThreats = -1;
        float warnedTime = float.NegativeInfinity;
        Vector3 scoutedAt;
        float scoutedTime = float.NegativeInfinity;
        float resupplyReadyAt = float.NegativeInfinity;

        public BiscuitBrain(BiscuitTuning tuning, IBiscuitBody body)
        {
            Tuning = tuning;
            Body = body;
            Memory = new BiscuitMemory(tuning);

            Goals = new BiscuitGoal[]
            {
                new StaySafeGoal(), new ProtectPlayerGoal(), new WarnPlayerGoal(), new ResupplyGoal(),
                new ShowEvidenceGoal(), new ScoutGoal(), new StayCloseGoal(),
            };
            Actions = new BiscuitAction[]
            {
                new FollowAction(), new SniffAction(), new GrowlAction(), new WhineAction(), new BarkAction(),
                new HideAction(), new LureAction(), new FetchAmmoAction(), new BringAmmoAction(),
                new LeadToEvidenceAction(),
            };

            costs = new float[Actions.Length];
            usable = new bool[Actions.Length];
            bannedUntil = new float[Actions.Length];
            ranking = new float[Goals.Length];
            goalOrder = new int[Goals.Length];
            for (int i = 0; i < bannedUntil.Length; i++) bannedUntil[i] = float.NegativeInfinity;
        }

        public BiscuitTuning Tuning { get; }
        public IBiscuitBody Body { get; }
        public BiscuitMemory Memory { get; }
        public BiscuitPerception Perception { get; private set; }

        /// <summary>The facts Biscuit believes right now.</summary>
        public ushort State { get; private set; }

        public BiscuitGoal[] Goals { get; }
        public BiscuitAction[] Actions { get; }

        public BiscuitGoal CurrentGoal { get; private set; }
        public BiscuitAction CurrentAction { get; private set; }

        public BiscuitAction[] Plan { get; } = new BiscuitAction[GoapPlanner.MaxDepth];
        public int PlanLength { get; private set; }
        public int PlanStep { get; private set; }
        public float PlanCost { get; private set; }
        public int PlanNodesExpanded { get; private set; }
        public double PlanMilliseconds { get; private set; }

        public BiscuitAction LastFailed { get; private set; }
        public float LastFailedTime { get; private set; } = float.NegativeInfinity;

        /// <summary>Biscuit has ammunition in its mouth.</summary>
        public bool Carrying { get; private set; }

        /// <summary>False for a while after a delivery, so Biscuit does not fetch again at once.</summary>
        public bool ResupplyReady => Perception.Now >= resupplyReadyAt;

        /// <summary>Raised with the action's name when Biscuit starts an action, and "" when it stops one.</summary>
        public event Action<string> ActionChanged;

        /// <summary>Raised with the goal's name when Biscuit changes goal ("" for none).</summary>
        public event Action<string> GoalChanged;

        public bool Has(Fact fact) => WorldState.Has(State, fact);

        public bool IsBanned(BiscuitAction action) => Perception.Now < bannedUntil[IndexOf(action)];

        /// <summary>Distance across the ground, ignoring height: a page on a table still counts as here.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ---------------------------------------------------------------- senses (the body calls these)

        public void OnSmelt(int sourceId, EnemyKind kind, Vector3 position, float now) =>
            Memory.Remember(sourceId, kind, position, Tuning.smeltConfidence, true, now);

        public void OnHeard(int sourceId, EnemyKind kind, Vector3 position, float now) =>
            Memory.Remember(sourceId, kind, position, Tuning.heardConfidence, false, now);

        public void OnSourceGone(int sourceId) => Memory.Forget(sourceId);

        /// <summary>Biscuit smelt ammunition or a page. It remembers the closest of each.</summary>
        public void OnFoundPoint(PointKind kind, int id, Vector3 position)
        {
            if (IsIgnored(id)) return;

            ref KnownPoint known = ref Known(kind);
            if (known.Valid && known.Id != id)
            {
                Vector3 here = Perception.Position;
                if ((known.Position - here).sqrMagnitude <= (position - here).sqrMagnitude) return;
            }
            known = new KnownPoint { Valid = true, Id = id, Position = position };
        }

        /// <summary>A point was taken, shown or unloaded with its zone.</summary>
        public void OnPointGone(int id)
        {
            if (knownAmmo.Id == id) knownAmmo.Valid = false;
            if (knownPage.Id == id) knownPage.Valid = false;
        }

        public bool TryGetKnownPoint(PointKind kind, out int id, out Vector3 position)
        {
            KnownPoint known = Known(kind);
            id = known.Id;
            position = known.Position;
            return known.Valid;
        }

        // ---------------------------------------------------------------- actions call these

        /// <summary>The player now knows about every threat met so far.</summary>
        public void MarkWarned()
        {
            warnedAtThreats = Memory.ThreatsMet;
            warnedTime = Perception.Now;
        }

        public void MarkScouted()
        {
            scoutedAt = Perception.Position;
            scoutedTime = Perception.Now;
        }

        public void MarkCarrying()
        {
            Carrying = true;
            knownAmmo.Valid = false;
        }

        public void MarkDelivered()
        {
            Carrying = false;
            resupplyReadyAt = Perception.Now + Tuning.resupplyCooldown;
        }

        /// <summary>Gives up on a place Biscuit cannot reach, so it does not keep trying to.</summary>
        public void IgnorePoint(PointKind kind)
        {
            ref KnownPoint known = ref Known(kind);
            if (!known.Valid) return;

            ignoredPoints[ignoredNext] = known.Id;
            ignoredNext = (ignoredNext + 1) % IgnoredCapacity;
            if (ignoredCount < IgnoredCapacity) ignoredCount++;
            known.Valid = false;
        }

        // ---------------------------------------------------------------- thinking

        public void Think(in BiscuitPerception perception)
        {
            Perception = perception;
            Memory.ForgetFaded(perception.Now);

            ushort before = State;
            State = ComputeFacts();

            // Plan again when the world changed or the plan ran out. The planner is cheap enough
            // (a few dozen states at most) to do this whenever it is needed.
            if (State != before || PlanStep >= PlanLength) ChooseGoalAndPlan();

            if (CurrentAction == null && PlanStep < PlanLength) StartStep();
            if (CurrentAction != null) RunCurrentAction();

            // The player has reached a page, led there or not: it has been shown.
            if (Has(Fact.PlayerAtEvidence)) ShowKnownEvidence();
        }

        ushort ComputeFacts()
        {
            BiscuitPerception p = Perception;
            float now = p.Now;
            ushort s = 0;

            // Near the player: in at 3 m, out at 6 m, so Biscuit does not stop and start at one line.
            float nearLimit = Has(Fact.NearPlayer) ? Tuning.leavePlayerDistance : Tuning.nearPlayerDistance;
            s = WorldState.With(s, Fact.NearPlayer, p.HasPlayer && p.DistanceToPlayer <= nearLimit);

            Vector3 guarding = p.HasPlayer ? p.PlayerPosition : p.Position;
            s = WorldState.With(s, Fact.EnemyNearby,
                Memory.CountWithin(guarding, Tuning.enemyNearbyDistance, Tuning.forgetBelow, now) > 0);
            s = WorldState.With(s, Fact.GhostNearby,
                Memory.CountWithin(guarding, Tuning.ghostNearbyDistance, Tuning.forgetBelow, now, KindFilter.Ghosts) > 0);

            // A warning covers every threat met before it, and wears off after a while.
            s = WorldState.With(s, Fact.PlayerWarned,
                warnedAtThreats == Memory.ThreatsMet && now - warnedTime < Tuning.rewarnAfter);

            s = WorldState.With(s, Fact.InDanger, IsInDanger(p.Position, now));

            s = WorldState.With(s, Fact.PlayerInTrouble,
                p.HasPlayer && p.PlayerHealth01 < Tuning.troubleHealth &&
                Memory.CountWithin(p.PlayerPosition, Tuning.troubleDistance, Tuning.sureConfidence, now) > 0);

            s = WorldState.With(s, Fact.AreaScouted,
                now - scoutedTime < Tuning.scoutedFor &&
                (p.Position - scoutedAt).sqrMagnitude < Tuning.scoutedRadius * Tuning.scoutedRadius);

            s = WorldState.With(s, Fact.PlayerLowOnAmmo, p.HasPlayer && p.PlayerAmmo01 < Tuning.lowAmmo);
            s = WorldState.With(s, Fact.KnowsAmmo, knownAmmo.Valid);
            s = WorldState.With(s, Fact.CarryingAmmo, Carrying);
            s = WorldState.With(s, Fact.KnowsEvidence, knownPage.Valid);
            s = WorldState.With(s, Fact.PlayerAtEvidence,
                knownPage.Valid && p.HasPlayer &&
                FlatDistance(p.PlayerPosition, knownPage.Position) <= Tuning.atEvidenceDistance);

            return s;
        }

        // Each kind has its own danger distance: ghosts drift through walls, so they count from further.
        bool IsInDanger(Vector3 position, float now)
        {
            for (int i = 0; i < Memory.Count; i++)
            {
                EnemyBelief belief = Memory[i];
                if (Memory.ConfidenceOf(belief, now) < Tuning.sureConfidence) continue;
                float limit = Tuning.DangerDistanceOf(belief.Kind);
                if ((belief.Position - position).sqrMagnitude <= limit * limit) return true;
            }
            return false;
        }

        void ChooseGoalAndPlan()
        {
            float now = Perception.Now;
            for (int i = 0; i < Actions.Length; i++)
            {
                usable[i] = now >= bannedUntil[i] && Actions[i].IsUsable(this);
                costs[i] = usable[i] ? Actions[i].Cost(this) : 0f;
            }

            // Score the goals and rank them, best first. The goal already being worked on gets a
            // small bonus, so two goals with close scores do not take turns every think.
            for (int i = 0; i < Goals.Length; i++)
            {
                ranking[i] = Goals[i].Evaluate(this) + (Goals[i] == CurrentGoal ? Tuning.commitmentBonus : 0f);
                goalOrder[i] = i;
            }
            SortGoalsByRanking();

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int nodes = 0;
            BiscuitGoal chosen = null;
            int length = 0;
            float cost = 0f;

            for (int k = 0; k < goalOrder.Length && chosen == null; k++)
            {
                BiscuitGoal goal = Goals[goalOrder[k]];
                if (goal.Priority <= 0f || goal.Wants.IsMetBy(State)) continue;

                // No plan (every way there is blocked or on cooldown): try the next goal down.
                bool found = planner.Plan(State, goal.Wants, Actions, Actions.Length, costs, usable,
                                          Plan, out length, out cost);
                nodes += planner.NodesExpanded;
                if (found) chosen = goal;
            }

            PlanNodesExpanded = nodes;
            PlanMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                               System.Diagnostics.Stopwatch.Frequency;
            Adopt(chosen, chosen != null ? length : 0, cost);
        }

        void Adopt(BiscuitGoal goal, int length, float cost)
        {
            // If the new plan starts with the action already running, let it carry on rather than restart.
            bool keepRunning = CurrentAction != null && length > 0 && Plan[0] == CurrentAction;
            if (CurrentAction != null && !keepRunning) EndAction();

            PlanLength = length;
            PlanStep = 0;
            PlanCost = cost;

            if (goal == CurrentGoal) return;
            CurrentGoal = goal;
            GoalChanged?.Invoke(goal != null ? goal.Name : "");
        }

        void StartStep()
        {
            CurrentAction = Plan[PlanStep];
            actionStartTime = Perception.Now;
            CurrentAction.Start(this);
            ActionChanged?.Invoke(CurrentAction.Name);
        }

        void RunCurrentAction()
        {
            BiscuitAction action = CurrentAction;
            ActionStatus status;
            bool interrupted = false;

            if (action.Gives.IsMetBy(State))
            {
                status = ActionStatus.Success;   // its job is already done
            }
            else if (!action.Needs.IsMetBy(State))
            {
                status = ActionStatus.Failure;   // the world changed under it; not the action's fault
                interrupted = true;
            }
            else if (Perception.Now - actionStartTime > action.Timeout(this))
            {
                status = ActionStatus.Failure;
            }
            else
            {
                status = action.Tick(this);
            }

            if (status == ActionStatus.Running) return;

            EndAction();
            if (status == ActionStatus.Success)
            {
                PlanStep++;
                return;
            }

            // A real failure (no route, nowhere to hide, stuck): leave the action out for a moment so
            // the next plan finds another way to the goal, or another goal.
            if (!interrupted)
            {
                bannedUntil[IndexOf(action)] = Perception.Now + Tuning.failedActionBan;
                LastFailed = action;
                LastFailedTime = Perception.Now;
            }
            PlanLength = 0;
            PlanStep = 0;
        }

        void EndAction()
        {
            CurrentAction.Stop(this);
            Body.Stop();
            CurrentAction = null;
            ActionChanged?.Invoke("");
        }

        void ShowKnownEvidence()
        {
            Body.ShowEvidence(knownPage.Id);
            knownPage.Valid = false;
        }

        // Insertion sort: seven goals, no allocation.
        void SortGoalsByRanking()
        {
            for (int i = 1; i < goalOrder.Length; i++)
            {
                int index = goalOrder[i];
                int j = i - 1;
                while (j >= 0 && ranking[goalOrder[j]] < ranking[index])
                {
                    goalOrder[j + 1] = goalOrder[j];
                    j--;
                }
                goalOrder[j + 1] = index;
            }
        }

        int IndexOf(BiscuitAction action)
        {
            for (int i = 0; i < Actions.Length; i++)
                if (Actions[i] == action) return i;
            return -1;
        }

        ref KnownPoint Known(PointKind kind)
        {
            if (kind == PointKind.Ammo) return ref knownAmmo;
            return ref knownPage;
        }

        bool IsIgnored(int id)
        {
            for (int i = 0; i < ignoredCount; i++)
                if (ignoredPoints[i] == id) return true;
            return false;
        }
    }
}
