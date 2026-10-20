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
        float nextReplan;
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
        }

        public GhostBrain(InfluenceMap map, GhostTuning tuning, int randomSeed = 1234)
            : this(map, tuning, ModeTuning.Servant, randomSeed) { }

        /// <summary>
        /// Runs one throwaway decision and route around 'at', then wipes every trace of it. The first call
        /// of the search code pays a one-off compile cost (several ms in the editor); doing it while the
        /// scene loads keeps that spike out of play. Raises no events and leaves the map untouched.
        /// </summary>
        public void WarmUp(Vector3 at)
        {
            Memory.Remember(at, AgentMemory.HeardConfidence, BeliefSource.Heard, 0f);
            ChoosePosition(0f, -1, Tuning.weights, Tuning.range);
            int here = grid.ClampedCellAt(at + Vector3.up * 0.1f);
            if (LastDecision.Cell >= 0)
            {
                astar.Begin(here, LastDecision.Cell, Tuning.costs, 0f, Tuning.maxRouteNodes);
                astar.Step(int.MaxValue);
                smoother.Smooth(route, astar.CopyRoute(route), Tuning.costs, 0f, waypoints);
            }
            var refuge = new SafeWallGoal(grid, new AimCone(at, Vector3.forward, 20f, 15f));
            fleeField.FindNearest(here, Modes.fleeDepth, ref refuge);
            astar.Cancel();
            Memory.Forget();
            LastDecision = default;
            TargetCell = -1;
            WaypointCount = 0;
        }

        // ---------------------------------------------------------------- perceptions

        /// <summary>The ghost's eyes recognised the player this tick. Only now are look, weapon and flashlight known.</summary>
        public void See(Vector3 playerPosition, Vector3 look, WeaponKind weapon, bool flashlight, float now)
        {
            Memory.Remember(playerPosition, AgentMemory.SeenConfidence, BeliefSource.Seen, now);
            LastSighting = new PlayerSighting(playerPosition, look, weapon, flashlight, now);
            KnownWeapon = weapon;
            KnownWeaponTime = now;

            // What the ghost saw the player looking at, and where their beam fell, goes on the house's map.
            Vector3 eye = playerPosition + Vector3.up * PositionQuery.EyeHeight;
            map.MarkWatched(eye, look, now);
            map.SetFlashlight(flashlight, eye, look);
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

            bool believes = Memory.HasBelief(now);
            if (!believes && Mode != GhostMode.Idle) map.SetFlashlight(false, Vector3.zero, Vector3.forward);

            ModeInputs inputs = Gather(ghostPosition, now, believes);
            LastInputs = inputs;
            GhostMode previous = Mode;
            GhostMode next = ModeSelector.Choose(inputs, Modes, previous, utility);
            if (believes && now < forcedFleeUntil) next = GhostMode.Flee;
            if (next == GhostMode.Flee && previous != GhostMode.Flee)
                forcedFleeUntil = Mathf.Max(forcedFleeUntil, now + Modes.fleeMinSeconds);   // dwell: no flicker
            if (!IsHunting(previous) && IsHunting(next)) huntingSince = now;
            SetMode(next);

            int here = grid.ClampedCellAt(ghostPosition + Vector3.up * 0.1f);
            switch (next)
            {
                case GhostMode.Flee:
                    return Flee(here, ghostPosition, now);

                case GhostMode.Strike:
                    if (TryStrike(ghostPosition, now)) return false;
                    return RouteTo(StrikeCell(), here, previous == next, now);

                case GhostMode.Stalk:
                case GhostMode.Ambush:
                    Tactics(next, now, out ScoreWeights weights, out RangeBand range);
                    int keep = previous == next ? TargetCell : -1;
                    return RouteTo(ChoosePosition(now, keep, weights, range), here, previous == next, now);

                default:
                    return RouteTo(HomeCell(), here, previous == next, now);
            }
        }

        /// <summary>
        /// Runs up to 'nodeBudget' cells of the route search. Returns true when a new route has just been
        /// smoothed into Waypoints.
        /// </summary>
        public bool StepRoute(int nodeBudget, float now)
        {
            if (astar.Status != RouteStatus.Searching) return false;
            RouteStatus status = astar.Step(nodeBudget);
            if (status != RouteStatus.Found && status != RouteStatus.Partial) return false;

            int length = astar.CopyRoute(route);
            WaypointCount = smoother.Smooth(route, length, Tuning.costs, now, waypoints);
            RouteVersion++;
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

            int here = grid.ClampedCellAt(ghostPosition + Vector3.up * 0.1f);
            i.Exposure = Mathf.Max(map.Light(here), map.Watched(here, now));

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

        // Close in on where the player is believed to stand.
        int StrikeCell()
        {
            int cell = grid.ClampedCellAt(Memory.Current.Position + Vector3.up * 0.1f);
            return grid.Is(cell, CellFlags.Inside) ? cell : -1;
        }

        // Into the nearest wall cell out of the aim. The BFS route is taken at once: no waiting on A*.
        bool Flee(int here, Vector3 ghostPosition, float now)
        {
            AimCone danger = SightingStillApplies(now) && LastSighting.Weapon == WeaponKind.IceGun
                ? SeenAim()
                : new AimCone(Memory.Current.Position + Vector3.up * PositionQuery.EyeHeight,
                              ghostPosition - Memory.Current.Position, 30f, Tuning.iceGunRange);   // away from the player
            var goal = new SafeWallGoal(grid, danger);
            int refuge = fleeField.FindNearest(here, Modes.fleeDepth, ref goal);

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
            return true;
        }

        // The best place to be near the believed player: utility score optimised by hill climbing.
        // 'current' is the target being kept (hysteresis), or -1.
        int ChoosePosition(float now, int current, ScoreWeights weights, RangeBand range)
        {
            Belief belief = Memory.Current;
            int beliefCell = grid.ClampedCellAt(belief.Position + Vector3.up * 0.1f);

            // The walking-distance field only reaches as far as the range term can still score.
            if (walkField.NeedsRebuild(grid.NearestWalkable(beliefCell)))
            {
                float reach = Mathf.Max(Mathf.Max(Tuning.range.max + Tuning.range.softness, Modes.carefulRange.max + Modes.carefulRange.softness),
                                        range.max + range.softness);
                walkField.BuildWalkingField(beliefCell, Mathf.CeilToInt(reach / grid.CellSize) + 2);
            }

            bool hasLook = SightingStillApplies(now);
            Vector3 eye = belief.Position + Vector3.up * PositionQuery.EyeHeight;
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
            };
            climber.Config = Tuning.climb;

            LastDecision = climber.Search(current);
            return LastDecision.Cell;
        }

        // With nothing to go on, the ghost drifts back to its haunt.
        int HomeCell()
        {
            if (!HasAnchor) return -1;
            int cell = grid.ClampedCellAt(Anchor + Vector3.up * 0.1f);
            return grid.Is(cell, CellFlags.Inside) ? cell : -1;
        }

        void SetMode(GhostMode mode)
        {
            if (mode == Mode) return;
            GhostMode previous = Mode;
            Mode = mode;
            ModeChanged?.Invoke(previous, mode);
        }
    }
}
