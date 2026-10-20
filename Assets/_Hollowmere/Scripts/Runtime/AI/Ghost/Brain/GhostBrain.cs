using System;
using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// One ghost's mind, in plain C# (no scene, no physics), so it can be unit-tested:
    ///   perceptions in (See / Hear / Told / Freeze) ->  belief, remembered sighting, known weapon
    ///   Think (scheduler rate)  ->  mode by utility (ModeSelector), then what the mode needs:
    ///       Idle    home to the haunt anchor
    ///       Stalk   best hidden cell near the player (utility score + hill climbing), weapon tactics applied
    ///       Ambush  the same search with ambush weights: close, dark, unwatched, beside a wall
    ///       Strike  close on the belief; strike only when its eyes confirm the player is within reach
    ///       Flee    BFS to the nearest wall cell out of the aim, route taken at once (no A* wait)
    ///       Frozen  hit by the ice gun: nothing until it thaws, then flees
    ///   StepRoute (every frame, budgeted)  ->  A* slices, then smoothed waypoints for the body
    /// Edge cases, each a fallback inside the same loop rather than a scripted state:
    ///   no dark cell near      -> lurk in the nearest wall cell (BFS) until a hidden place opens up
    ///   player cornered        -> wall-surface cells become candidates: wait in the wall behind them
    ///   player outside         -> focus on the inside cell nearest them (the threshold); no strike
    ///   belief lost            -> home to the anchor, then wander slowly around it at random
    ///   A* node cap reached    -> take the partial route, re-plan sooner; no route at all -> drift straight
    ///   spawned in a wall/out  -> plan from the nearest inside cell; home snaps to the nearest floor
    ///   aim flicking           -> each aim renews the flee dwell; a refuge is kept while still out of the aim
    /// It never reads the player's true state: only what See, Hear and Told hand it.
    /// Allocation-free after construction.
    /// </summary>
    public sealed class GhostBrain
    {
        public const int MaxWaypoints = 64;

        readonly InfluenceMap map;
        readonly GhostGrid grid;
        readonly BfsField walkField;
        readonly BfsField fleeField;
        readonly PositionScorer scorer;
        readonly HillClimber climber;
        readonly AStarSearch astar;
        readonly PathSmoother smoother;
        readonly int[] route = new int[512];
        readonly Vector3[] waypoints = new Vector3[MaxWaypoints];
        readonly float[] utility = new float[ModeSelector.Count];
        readonly System.Random random;
        float nextReplan;
        float nextWander;
        int focusCell = -1;
        Vector3 focusFeet;

        // What the last full position search was based on (change-driven re-search).
        int searchFocus = -1;
        Vector3 searchLook;
        bool searchHasLook, searchThreat, searchCareful, searchCornered;
        float searchScore;
        float nextFullSearch;
        bool warming;
        float forcedFleeUntil = float.NegativeInfinity;
        float frozenUntil;
        float lastStrike = float.NegativeInfinity;
        float huntingSince;

        public GhostTuning Tuning;
        public ModeTuning Modes;

        /// <summary>The shared belief model: seen 1.0, heard 0.6, told 0.4, fading with tau = 8 s.</summary>
        public readonly AgentMemory Memory = new AgentMemory();

        public PlayerSighting LastSighting { get; private set; }
        /// <summary>The weapon the ghost last knew the player to have, from a sighting or a shot it heard.</summary>
        public WeaponKind KnownWeapon { get; private set; }
        public float KnownWeaponTime { get; private set; } = float.NegativeInfinity;

        public GhostMode Mode { get; private set; } = GhostMode.Idle;
        public event Action<GhostMode, GhostMode> ModeChanged;
        /// <summary>The ghost struck at the player, who its eyes placed at this position.</summary>
        public event Action<Vector3> Struck;
        /// <summary>The ice gun's hold on the ghost has worn off.</summary>
        public event Action Freed;

        /// <summary>The cell the ghost is heading for, or -1.</summary>
        public int TargetCell { get; private set; } = -1;
        public HillClimber.Result LastDecision { get; private set; }
        /// <summary>The inputs and utilities of the last mode choice, for the debug panel and the viva.</summary>
        public ModeInputs LastInputs { get; private set; }
        public float Utility(GhostMode mode) => utility[(int)mode];

        // Story: where this ghost belongs. Set by the body.
        public bool HasAnchor;
        public Vector3 Anchor;
        public float Leash = 6f;

        // Cells other ghosts are heading for (multi-ghost reservation).
        public int[] Reserved;
        public int ReservedCount;

        /// <summary>The player is believed outside the house: the ghost waits at the threshold instead.</summary>
        public bool PlayerOutside { get; private set; }
        /// <summary>The believed player cell has few open cells round it (a corner or a dead end).</summary>
        public bool PlayerCornered { get; private set; }
        /// <summary>Even the best place near the player was lit or watched, so the ghost is waiting in a wall.</summary>
        public bool Lurking { get; private set; }
        /// <summary>How the last route search ended (Partial when its node cap cut it short).</summary>
        public RouteStatus LastRouteStatus { get; private set; } = RouteStatus.Idle;
        /// <summary>Share of full speed the body should move at in the current mode.</summary>
        public float SpeedScale { get; private set; } = 1f;

        /// <summary>
        /// Set by the body: false when another ghost in the house already did a heavy step this frame (a
        /// walk-field rebuild or a full position search). A ghost with a target then keeps it and does its
        /// heavy step on its next thought, so one frame never pays for two.
        /// </summary>
        public bool MayRunFullSearch = true;
        /// <summary>Did the last Think do a heavy step (walk-field rebuild or full position search)?</summary>
        public bool LastThinkHeavy { get; private set; }
        public int FullSearches { get; private set; }
        public int SearchesSkipped { get; private set; }

        public Vector3[] Waypoints => waypoints;
        public int WaypointCount { get; private set; }
        /// <summary>Goes up each time a new route is ready, so the body knows to pick it up.</summary>
        public int RouteVersion { get; private set; }

        public InfluenceMap Map => map;
        public BfsField WalkField => walkField;
        public PositionScorer Scorer => scorer;
        public HillClimber Climber => climber;
        public AStarSearch Router => astar;

        public GhostBrain(InfluenceMap map, GhostTuning tuning, ModeTuning modes, int randomSeed = 1234)
        {
            this.map = map;
            grid = map.Grid;
            Tuning = tuning;
            Modes = modes;
            walkField = new BfsField(grid);
            fleeField = new BfsField(grid);
            scorer = new PositionScorer(map) { WalkField = walkField };
            climber = new HillClimber(scorer, randomSeed);
            astar = new AStarSearch(map);
            smoother = new PathSmoother(map);
            random = new System.Random(randomSeed);
        }

        public GhostBrain(InfluenceMap map, GhostTuning tuning, int randomSeed = 1234)
            : this(map, tuning, ModeTuning.Servant, randomSeed) { }

        /// <summary>
        /// Runs the real thinking paths once around 'at' (lying in wait for a noise, fleeing an aimed ice gun,
        /// drifting home), then wipes every trace of them. Under Mono the first call of each method pays a
        /// one-off compile cost (about 3 ms for the first thought); doing it while the scene loads keeps that
        /// spike out of play. Raises no events and never writes the shared map (no See).
        /// </summary>
        public void WarmUp(Vector3 at)
        {
            warming = true;
            WarmSight();
            bool hadAnchor = HasAnchor;
            Vector3 anchor = Anchor;

            // A noise at the ghost: mode choice, hill climbing, the A* search run to the end.
            Memory.Remember(at, AgentMemory.HeardConfidence, BeliefSource.Heard, 0f);
            Think(at, 0f);
            StepRoute(int.MaxValue, 0f);

            // Seen two metres away aiming the ice gun at its chest: the flee path (BFS, trace, smoothing).
            Vector3 player = at + Vector3.forward * 2f;
            Vector3 aim = at + Vector3.up * GhostGrid.BodyHeight - (player + Vector3.up * PositionQuery.EyeHeight);
            LastSighting = new PlayerSighting(player, aim, WeaponKind.IceGun, false, 0.01f);
            KnownWeapon = WeaponKind.IceGun;
            KnownWeaponTime = 0.01f;
            Memory.Remember(player, AgentMemory.SeenConfidence, BeliefSource.Seen, 0.01f);
            Think(at, 0.01f);

            // Nothing known: home and wander.
            Memory.Forget();
            HasAnchor = true;
            Anchor = at;
            Think(at, 0.02f);
            StepRoute(int.MaxValue, 0.02f);

            // Wipe it all.
            HasAnchor = hadAnchor;
            Anchor = anchor;
            astar.Cancel();
            Memory.Forget();
            LastSighting = default;
            KnownWeapon = WeaponKind.None;
            KnownWeaponTime = float.NegativeInfinity;
            Mode = GhostMode.Idle;
            forcedFleeUntil = lastStrike = float.NegativeInfinity;
            huntingSince = nextReplan = nextWander = 0f;
            Lurking = false;
            LastRouteStatus = RouteStatus.Idle;
            SpeedScale = 1f;
            Locate(false);
            searchFocus = -1;
            nextFullSearch = 0f;
            FullSearches = SearchesSkipped = 0;
            LastThinkHeavy = false;
            LastDecision = default;
            LastInputs = default;
            for (int i = 0; i < utility.Length; i++) utility[i] = 0f;
            TargetCell = -1;
            WaypointCount = 0;
            RouteVersion = 0;
            warming = false;
        }

        // ---------------------------------------------------------------- perceptions

        /// <summary>The ghost's eyes recognised the player this tick. Only now are look, weapon and flashlight known.</summary>
        public void See(Vector3 playerPosition, Vector3 look, WeaponKind weapon, bool flashlight, float now)
        {
            Memory.Remember(playerPosition, AgentMemory.SeenConfidence, BeliefSource.Seen, now);
            LastSighting = new PlayerSighting(playerPosition, look, weapon, flashlight, now);
            KnownWeapon = weapon;
            KnownWeaponTime = now;

            MarkSight(map, playerPosition, look, flashlight, now);
        }

        // What the ghost saw the player looking at, and where their beam fell, goes on the house's map.
        static void MarkSight(InfluenceMap onMap, Vector3 playerPosition, Vector3 look, bool flashlight, float now)
        {
            Vector3 eye = playerPosition + Vector3.up * PositionQuery.EyeHeight;
            onMap.MarkWatched(eye, look, now);
            onMap.SetFlashlight(flashlight, eye, look);
        }

        static bool sightWarmed;

        // The first sighting in a session compiles the map's cone code (about 1.4 ms once). Run it on a small
        // throwaway map instead of the shared one, so warming never writes what the ghosts know.
        static void WarmSight()
        {
            if (sightWarmed) return;
            sightWarmed = true;
            var grid = new GhostGrid(Vector3.zero, 1f, 9, 9, new[] { 0f });
            for (int i = 0; i < grid.Count; i++) grid.Flags[i] = CellFlags.Inside | CellFlags.Walkable;
            var scratch = new InfluenceMap(grid);
            MarkSight(scratch, new Vector3(4.5f, 0f, 4.5f), Vector3.forward, true, 0f);
            scratch.Watched(0, 0f);
            scratch.Light(0);
        }

        /// <summary>A noise reached the ghost. Other creatures' calls say nothing about the player.</summary>
        public void Hear(Vector3 position, NoiseKind kind, float now)
        {
            if (kind == NoiseKind.Bark || kind == NoiseKind.Groan) return;
            Memory.Remember(position, AgentMemory.HeardConfidence, BeliefSource.Heard, now);

            // A shot gives away the weapon even unseen.
            if (kind == NoiseKind.IceGun || kind == NoiseKind.Gunshot)
            {
                KnownWeapon = kind == NoiseKind.IceGun ? WeaponKind.IceGun : WeaponKind.Pistol;
                KnownWeaponTime = now;
            }
        }

        /// <summary>The Director pointed somewhere, as if something was heard there.</summary>
        public void Told(Vector3 point, float now) =>
            Memory.Remember(point, AgentMemory.ToldConfidence, BeliefSource.Told, now);

        /// <summary>Hit by the ice gun: frozen in place for 'seconds', then it flees.</summary>
        public void Freeze(float seconds, float now)
        {
            frozenUntil = Mathf.Max(frozenUntil, now + seconds);
            astar.Cancel();
            TargetCell = -1;
            WaypointCount = 0;
            SetMode(GhostMode.Frozen);
        }

        /// <summary>Is the last sighting recent and about the current belief, so its look direction still applies?</summary>
        public bool SightingStillApplies(float now) =>
            LastSighting.Valid && Memory.HasBelief(now) &&
            LastSighting.Time >= Memory.Current.Time - 1e-3f && LastSighting.Age(now) <= Tuning.lookMemory;

        /// <summary>Does the ghost know (by sight or by a shot it heard) that the player has the ice gun?</summary>
        public bool KnowsIceGun(float now) =>
            KnownWeapon == WeaponKind.IceGun && now - KnownWeaponTime <= Modes.iceGunMemory;

        // ---------------------------------------------------------------- thinking

        /// <summary>
        /// Decide at the scheduler rate: the mode, the target cell, and whether to start a new route search
        /// (returned true). The search itself runs in StepRoute; a flight route is ready at once.
        /// </summary>
        public bool Think(Vector3 ghostPosition, float now)
        {
            if (Mode == GhostMode.Frozen)
            {
                if (now < frozenUntil) return false;
                forcedFleeUntil = now + Modes.fleeAfterFreeze;
                Freed?.Invoke();
            }

            LastThinkHeavy = false;
            bool believes = Memory.HasBelief(now);
            if (!believes && Mode != GhostMode.Idle && !warming) map.SetFlashlight(false, Vector3.zero, Vector3.forward);
            Locate(believes);

            ModeInputs inputs = Gather(ghostPosition, now, believes);
            LastInputs = inputs;
            GhostMode previous = Mode;
            GhostMode next = ModeSelector.Choose(inputs, Modes, previous, utility);
            // Dwell: once fleeing, keep at it. Every fresh sight of the aim renews the dwell, so an aim
            // flicking on and off the ghost cannot make it flicker between Flee and Stalk.
            if ((next == GhostMode.Flee && previous != GhostMode.Flee) || inputs.Aimed)
                forcedFleeUntil = Mathf.Max(forcedFleeUntil, now + Modes.fleeMinSeconds);
            if (believes && now < forcedFleeUntil) next = GhostMode.Flee;
            if (!IsHunting(previous) && IsHunting(next)) huntingSince = now;
            SetMode(next);
            SpeedScale = SpeedFor(next);
            if (next != GhostMode.Stalk && next != GhostMode.Ambush) Lurking = false;

            int here = GhostCell(ghostPosition);
            bool sameMode = previous == next;
            switch (next)
            {
                case GhostMode.Flee:
                    return Flee(here, ghostPosition, sameMode, now);

                case GhostMode.Strike:
                    if (TryStrike(ghostPosition, now)) return false;
                    return RouteTo(focusCell >= 0 ? StrikeCell() : HomeCell(), here, sameMode, now);

                case GhostMode.Stalk:
                case GhostMode.Ambush:
                    if (focusCell < 0) return RouteTo(HomeCell(), here, sameMode, now);   // nowhere inside near them
                    Tactics(next, now, out ScoreWeights weights, out RangeBand range);
                    int keep = sameMode ? TargetCell : -1;
                    return RouteTo(HideOrLurk(here, now, keep, weights, range), here, sameMode, now);

                default:
                    return Wander(here, !sameMode, now);
            }
        }

        /// <summary>
        /// Runs up to 'nodeBudget' cells of the route search. Returns true when a new route has just been
        /// smoothed into Waypoints.
        /// </summary>
        public bool StepRoute(int nodeBudget, float now)
        {
            if (astar.Status != RouteStatus.Searching) return false;
            using var _ = GhostProfiling.Route.Auto();
            RouteStatus status = astar.Step(nodeBudget);
            if (status != RouteStatus.Found && status != RouteStatus.Partial) return false;
            LastRouteStatus = status;

            // Node cap reached: walk the best part found, and look again soon from closer in.
            if (status == RouteStatus.Partial) nextReplan = Mathf.Min(nextReplan, now + Modes.partialReplan);

            int length = astar.CopyRoute(route);
            if (length == 0) return DirectRoute(astar.Goal);
            WaypointCount = smoother.Smooth(route, length, Tuning.costs, now, waypoints);
            RouteVersion++;
            return true;
        }

        // No usable route: a ghost passes through walls, so it can always drift straight to the cell.
        bool DirectRoute(int goal)
        {
            if (goal < 0) return false;
            waypoints[0] = grid.CellCentre(goal);
            WaypointCount = 1;
            RouteVersion++;
            LastRouteStatus = RouteStatus.Failed;
            return true;
        }

        // ---------------------------------------------------------------- modes

        static bool IsHunting(GhostMode mode) =>
            mode == GhostMode.Stalk || mode == GhostMode.Ambush || mode == GhostMode.Strike;

        // Everything the mode choice weighs, from the ghost's own knowledge.
        ModeInputs Gather(Vector3 ghostPosition, float now, bool believes)
        {
            var i = new ModeInputs { Believes = believes };
            if (!believes) return i;

            Belief belief = Memory.Current;
            bool sighting = SightingStillApplies(now);
            i.Confidence = Memory.ConfidenceAt(now);
            i.Freshness = sighting ? 1f - Mathf.Clamp01(LastSighting.Age(now) / Mathf.Max(Tuning.lookMemory, 1e-3f)) : 0f;
            i.Distance = Vector3.Distance(ghostPosition, belief.Position);
            i.IceGunHeld = KnowsIceGun(now);
            i.StrikeReady = now - lastStrike >= Modes.strikeCooldown;
            i.HuntSeconds = IsHunting(Mode) ? now - huntingSince : 0f;
            i.PlayerOutside = PlayerOutside;
            i.Exposure = Exposure(GhostCell(ghostPosition), now);

            Vector3 chest = ghostPosition + Vector3.up * GhostGrid.BodyHeight;
            if (sighting)
            {
                i.FacingAway = FacingAway(LastSighting.Position, LastSighting.Look, ghostPosition);
                i.Aimed = LastSighting.Weapon == WeaponKind.IceGun && LastSighting.Age(now) <= Modes.aimFreshness &&
                          SeenAim().Contains(chest);
            }
            else i.FacingAway = 0.5f;
            return i;
        }

        // 1 when the player looks straight away from the ghost, 0 straight at it (horizontal only).
        static float FacingAway(Vector3 player, Vector3 look, Vector3 ghost)
        {
            float dx = ghost.x - player.x, dz = ghost.z - player.z;
            float length = Mathf.Sqrt(dx * dx + dz * dz), lookLength = Mathf.Sqrt(look.x * look.x + look.z * look.z);
            if (length < 1e-3f || lookLength < 1e-3f) return 0.5f;
            return (1f - (dx * look.x + dz * look.z) / (length * lookLength)) * 0.5f;
        }

        AimCone SeenAim() => new AimCone(LastSighting.Position + Vector3.up * PositionQuery.EyeHeight, LastSighting.Look,
                                          Tuning.iceGunHalfAngle, Tuning.iceGunRange);

        // Weapon tactics: with the ice gun about the ghost keeps further off and hugs cover; otherwise bold.
        void Tactics(GhostMode mode, float now, out ScoreWeights weights, out RangeBand range)
        {
            bool ambush = mode == GhostMode.Ambush;
            weights = ambush ? Modes.ambushWeights : Tuning.weights;
            range = ambush ? Modes.ambushRange : Tuning.range;
            if (!KnowsIceGun(now)) return;

            weights.cover *= Modes.carefulCoverScale;
            weights.unseen *= Modes.carefulUnseenScale;
            if (ambush)
            {
                range.min += 1.5f;
                range.max += 1.5f;
            }
            else range = Modes.carefulRange;
        }

        // Strike lands only when the ghost's eyes put the player within reach right now.
        bool TryStrike(Vector3 ghostPosition, float now)
        {
            if (!LastSighting.Valid || LastSighting.Age(now) > 0.5f) return false;
            Vector3 offset = LastSighting.Position - ghostPosition;
            if (Mathf.Abs(offset.y) > 1.5f) return false;
            offset.y = 0f;
            if (offset.magnitude > Modes.strikeDistance) return false;

            lastStrike = now;
            forcedFleeUntil = now + Modes.fleeAfterStrike;           // hit and fade away
            Struck?.Invoke(LastSighting.Position);
            return true;
        }

        // Close in on where the player is believed to stand (never outside the house).
        int StrikeCell() => PlayerOutside ? -1 : focusCell;

        // Into the nearest wall cell out of the aim. The BFS route is taken at once: no waiting on A*.
        bool Flee(int here, Vector3 ghostPosition, bool sameMode, float now)
        {
            AimCone danger = SightingStillApplies(now) && LastSighting.Weapon == WeaponKind.IceGun
                ? SeenAim()
                : new AimCone(Memory.Current.Position + Vector3.up * PositionQuery.EyeHeight,
                              ghostPosition - Memory.Current.Position, 30f, Tuning.iceGunRange);   // away from the player
            var goal = new SafeWallGoal(grid, danger);

            // Hysteresis: a refuge still out of the aim is kept, however the aim swings elsewhere.
            if (sameMode && TargetCell >= 0 && WaypointCount > 0 && goal.IsGoal(TargetCell)) return false;

            int refuge;
            using (GhostProfiling.WallSearch.Auto()) refuge = fleeField.FindNearest(here, Modes.fleeDepth, ref goal);

            if (refuge < 0)
            {
                // No wall close enough: the best hidden place, with the threat counted triple.
                Tactics(GhostMode.Stalk, now, out ScoreWeights weights, out RangeBand range);
                weights.threat *= 3f;
                return RouteTo(ChoosePosition(now, -1, weights, range), here, false, now);
            }

            if (refuge == TargetCell && WaypointCount > 0) return false;
            TargetCell = refuge;
            astar.Cancel();
            int length = fleeField.TracePath(refuge, route);
            WaypointCount = smoother.Smooth(route, length, Tuning.costs, now, waypoints);
            RouteVersion++;
            return false;
        }

        // Starts an A* search to 'goal' when it changed, or when the re-plan interval is over.
        bool RouteTo(int goal, int here, bool sameMode, float now)
        {
            bool changed = goal != TargetCell || !sameMode;
            TargetCell = goal;
            if (goal < 0 || goal == here) return false;
            if (!changed && now < nextReplan) return false;

            astar.Begin(here, goal, Tuning.costs, now, Tuning.maxRouteNodes);
            nextReplan = now + Tuning.replanInterval;
            if (astar.Status == RouteStatus.Failed) DirectRoute(goal);
            return true;
        }

        // The best place to be near the believed player: utility score optimised by hill climbing.
        // 'current' is the target being kept (hysteresis), or -1.
        int ChoosePosition(float now, int current, ScoreWeights weights, RangeBand range)
        {
            int beliefCell = focusCell;
            if (beliefCell < 0)
            {
                LastDecision = new HillClimber.Result { Cell = -1 };
                return -1;
            }

            // Another ghost already did this frame's heavy step: with a target in hand, keep it one more thought.
            bool needsRebuild = walkField.NeedsRebuild(grid.NearestWalkable(beliefCell));
            if (needsRebuild && !MayRunFullSearch && current >= 0)
            {
                LastDecision = new HillClimber.Result { Cell = current, KeptCurrent = true };
                SearchesSkipped++;
                return current;
            }

            // The walking-distance field only reaches as far as the range term can still score.
            bool rebuilt = false;
            if (needsRebuild)
            {
                rebuilt = true;
                LastThinkHeavy = true;
                float reach = Mathf.Max(Mathf.Max(Tuning.range.max + Tuning.range.softness, Modes.carefulRange.max + Modes.carefulRange.softness),
                                        range.max + range.softness);
                using (GhostProfiling.WalkField.Auto())
                    walkField.BuildWalkingField(beliefCell, Mathf.CeilToInt(reach / grid.CellSize) + 2);
            }

            bool hasLook = SightingStillApplies(now);
            Vector3 eye = focusFeet + Vector3.up * PositionQuery.EyeHeight;
            Vector3 look = LastSighting.Look;
            scorer.Query = new PositionQuery
            {
                PlayerEye = eye,
                LookDirection = look,
                HasLook = hasLook,
                Now = now,
                Weights = weights,
                Range = range,
                ThreatActive = hasLook && LastSighting.Weapon == WeaponKind.IceGun,
                Aim = new AimCone(eye, look, Tuning.iceGunHalfAngle, Tuning.iceGunRange),
                HasAnchor = HasAnchor,
                Anchor = Anchor,
                Leash = Leash,
                Reserved = Reserved,
                ReservedCount = ReservedCount,
                CrowdRadius = Tuning.crowdRadius,
                AllowWallSkin = PlayerCornered,
            };
            climber.Config = Tuning.climb;

            // Change-driven: a full search only when what the ghost knows has changed (or the interval is
            // up), or when its target no longer holds. Otherwise one cell is re-scored instead of hundreds.
            bool threat = scorer.Query.ThreatActive, careful = KnowsIceGun(now);
            bool changed = now >= nextFullSearch || focusCell != searchFocus || hasLook != searchHasLook ||
                           threat != searchThreat || careful != searchCareful || PlayerCornered != searchCornered ||
                           (hasLook && FlatDot(look, searchLook) < 0.94f);                      // turned > 20 degrees
            // One heavy step per thought: after rebuilding the walk field, a target that still holds is
            // kept and the search waits for the next thought (the change is still pending, so it runs then).
            if (current >= 0 && scorer.IsCandidate(current) && (!changed || !MayRunFullSearch || rebuilt))
            {
                float score = scorer.Score(current);
                if (score >= searchScore - Tuning.climb.switchMargin)
                {
                    LastDecision = new HillClimber.Result { Cell = current, Score = score, Evaluations = 1, KeptCurrent = true };
                    SearchesSkipped++;
                    return current;
                }
            }

            using (GhostProfiling.Position.Auto()) LastDecision = climber.Search(current);
            searchFocus = focusCell;
            searchLook = look;
            searchHasLook = hasLook;
            searchThreat = threat;
            searchCareful = careful;
            searchCornered = PlayerCornered;
            searchScore = LastDecision.Score;
            nextFullSearch = now + Tuning.fullSearchInterval;
            LastThinkHeavy = true;
            FullSearches++;
            return LastDecision.Cell;
        }

        // Cosine of the horizontal angle between two directions (1 when they agree).
        static float FlatDot(Vector3 a, Vector3 b)
        {
            float la = Mathf.Sqrt(a.x * a.x + a.z * a.z), lb = Mathf.Sqrt(b.x * b.x + b.z * b.z);
            if (la < 1e-4f || lb < 1e-4f) return 1f;
            return (a.x * b.x + a.z * b.z) / (la * lb);
        }

        // With nothing to go on, the ghost drifts back to its haunt. An anchor inside a wall (a ghost
        // placed in one) snaps to the nearest floor, so it does not haunt from inside the wall for ever.
        int HomeCell()
        {
            if (!HasAnchor) return -1;
            int cell = grid.ClampedCellAt(Anchor + Vector3.up * 0.1f);
            if (!grid.Is(cell, CellFlags.Inside)) cell = grid.NearestWith(cell, CellFlags.Inside, Modes.thresholdSearch);
            if (cell < 0) return -1;
            int floor = grid.NearestWalkable(cell);
            return floor >= 0 ? floor : cell;
        }

        // ---------------------------------------------------------------- edge cases

        // Where the ghost reasons from. Spawned outside the house (or pushed out), it plans from the
        // nearest inside cell; inside a wall is fine, ghosts pass through walls.
        int GhostCell(Vector3 ghostPosition)
        {
            int cell = grid.ClampedCellAt(ghostPosition + Vector3.up * 0.1f);
            if (grid.Is(cell, CellFlags.Inside)) return cell;
            int inside = grid.NearestWith(cell, CellFlags.Inside, Modes.thresholdSearch);
            return inside >= 0 ? inside : cell;
        }

        // Where the believed player is, as the searches use it. Outside the house the ghost will not
        // follow: it takes the inside floor cell nearest the belief (a door or window) instead.
        void Locate(bool believes)
        {
            focusCell = -1;
            PlayerOutside = false;
            PlayerCornered = false;
            if (!believes) return;

            Vector3 position = Memory.Current.Position;
            int cell = grid.CellAt(position + Vector3.up * 0.1f);
            if (cell >= 0 && grid.Is(cell, CellFlags.Inside))
            {
                focusCell = cell;
                focusFeet = position;
            }
            else
            {
                PlayerOutside = true;
                int threshold = grid.NearestWalkable(grid.ClampedCellAt(position + Vector3.up * 0.1f), Modes.thresholdSearch);
                if (threshold < 0) return;
                focusCell = threshold;
                Vector3 centre = grid.CellCentre(threshold);
                focusFeet = new Vector3(centre.x, grid.WalkY[threshold], centre.z);
            }

            int stand = grid.NearestWalkable(focusCell);
            PlayerCornered = !PlayerOutside && stand >= 0 && grid.OpenNeighbours(stand) <= Modes.corneredOpenCells;
        }

        float Exposure(int cell, float now) => Mathf.Max(map.Light(cell), map.Watched(cell, now));

        // The best hidden place near the player; if even that is lit or watched, the nearest wall cell
        // (out of any seen aim) to wait in instead. Entering and leaving differ by 0.1, so it does not flicker.
        int HideOrLurk(int here, float now, int keep, ScoreWeights weights, RangeBand range)
        {
            int best = ChoosePosition(now, keep, weights, range);
            float limit = Lurking ? Modes.lurkExposure - 0.1f : Modes.lurkExposure;
            Lurking = best < 0 || (!grid.Is(best, CellFlags.Solid) && Exposure(best, now) >= limit);
            if (!Lurking) return best;

            AimCone threat = SightingStillApplies(now) && LastSighting.Weapon == WeaponKind.IceGun
                ? SeenAim()
                : new AimCone(Vector3.zero, Vector3.forward, 0f, 0f);                // nothing to avoid
            var goal = new SafeWallGoal(grid, threat);
            int wall;
            using (GhostProfiling.WallSearch.Auto()) wall = fleeField.FindNearest(here, Modes.fleeDepth, ref goal);
            if (wall >= 0) return wall;
            Lurking = false;                                                          // no wall in reach either
            return best;
        }

        // No belief: home first, then short slow wanders to random floor cells around the anchor.
        bool Wander(int here, bool entered, float now)
        {
            int home = HomeCell();
            if (home < 0)
            {
                TargetCell = -1;
                return false;
            }
            if (entered)
            {
                nextWander = now + Modes.wanderPause;
                return RouteTo(home, here, false, now);
            }
            if (now >= nextWander)
            {
                nextWander = now + Modes.wanderPause * (0.6f + 0.8f * (float)random.NextDouble());
                return RouteTo(WanderCell(home), here, false, now);
            }
            return RouteTo(TargetCell >= 0 ? TargetCell : home, here, true, now);
        }

        // A random floor cell within the wander radius (and the leash) of home, not claimed by another ghost.
        int WanderCell(int home)
        {
            Vector3 centre = grid.CellCentre(home);
            grid.Coords(home, out _, out int layer, out _);
            float radius = Mathf.Min(Modes.wanderRadius, Mathf.Max(Leash, 1f));
            for (int tries = 0; tries < 8; tries++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;   // uniform over the disc
                int x = Mathf.FloorToInt((centre.x + Mathf.Cos(angle) * distance - grid.Origin.x) / grid.CellSize);
                int z = Mathf.FloorToInt((centre.z + Mathf.Sin(angle) * distance - grid.Origin.z) / grid.CellSize);
                if (!grid.InBounds(x, layer, z)) continue;
                int cell = grid.Index(x, layer, z);
                if (grid.Is(cell, CellFlags.Walkable) && cell != TargetCell && !Claimed(cell)) return cell;
            }
            return home;
        }

        bool Claimed(int cell)
        {
            if (Reserved == null) return false;
            for (int i = 0; i < ReservedCount; i++)
                if (Reserved[i] == cell) return true;
            return false;
        }

        float SpeedFor(GhostMode mode)
        {
            switch (mode)
            {
                case GhostMode.Idle: return Modes.wanderSpeed;
                case GhostMode.Strike: return Modes.strikeSpeed;
                case GhostMode.Flee: return Modes.fleeSpeed;
                case GhostMode.Frozen: return 0f;
                default: return Modes.huntSpeed;
            }
        }

        void SetMode(GhostMode mode)
        {
            if (mode == Mode) return;
            GhostMode previous = Mode;
            Mode = mode;
            if (!warming) ModeChanged?.Invoke(previous, mode);
        }
    }
}
