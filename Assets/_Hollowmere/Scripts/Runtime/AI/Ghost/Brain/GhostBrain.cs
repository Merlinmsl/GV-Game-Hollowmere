using System;
using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// One ghost's mind, in plain C# (no scene, no physics), so it can be unit-tested:
    ///   perceptions in (See / Hear / Told)  ->  belief and remembered sighting
    ///   Think (scheduler rate)              ->  mode, target cell (utility score + hill climbing),
    ///                                           and a route search request
    ///   StepRoute (every frame, budgeted)   ->  A* slices, then smoothed waypoints for the body
    /// It never reads the player's true state: only what See, Hear and Told hand it.
    /// Allocation-free after construction.
    /// </summary>
    public sealed class GhostBrain
    {
        public const int MaxWaypoints = 64;

        readonly InfluenceMap map;
        readonly GhostGrid grid;
        readonly BfsField walkField;
        readonly PositionScorer scorer;
        readonly HillClimber climber;
        readonly AStarSearch astar;
        readonly PathSmoother smoother;
        readonly int[] route = new int[512];
        readonly Vector3[] waypoints = new Vector3[MaxWaypoints];
        float nextReplan;

        public GhostTuning Tuning;

        /// <summary>The shared belief model: seen 1.0, heard 0.6, told 0.4, fading with tau = 8 s.</summary>
        public readonly AgentMemory Memory = new AgentMemory();

        public PlayerSighting LastSighting { get; private set; }
        /// <summary>The weapon the ghost last knew the player to have, from a sighting or a shot it heard.</summary>
        public WeaponKind KnownWeapon { get; private set; }
        public float KnownWeaponTime { get; private set; } = float.NegativeInfinity;

        public GhostMode Mode { get; private set; } = GhostMode.Idle;
        public event Action<GhostMode, GhostMode> ModeChanged;

        /// <summary>The cell the ghost is heading for, or -1.</summary>
        public int TargetCell { get; private set; } = -1;
        public HillClimber.Result LastDecision { get; private set; }

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

        public GhostBrain(InfluenceMap map, GhostTuning tuning, int randomSeed = 1234)
        {
            this.map = map;
            grid = map.Grid;
            Tuning = tuning;
            walkField = new BfsField(grid);
            scorer = new PositionScorer(map) { WalkField = walkField };
            climber = new HillClimber(scorer, randomSeed);
            astar = new AStarSearch(map);
            smoother = new PathSmoother(map);
        }

        /// <summary>
        /// Runs one throwaway decision and route around 'at', then wipes every trace of it. The first call
        /// of the search code pays a one-off compile cost (several ms in the editor); doing it while the
        /// scene loads keeps that spike out of play. Raises no events and leaves the map untouched.
        /// </summary>
        public void WarmUp(Vector3 at)
        {
            Memory.Remember(at, AgentMemory.HeardConfidence, BeliefSource.Heard, 0f);
            ChoosePosition(0f, -1);
            int here = grid.ClampedCellAt(at + Vector3.up * 0.1f);
            if (LastDecision.Cell >= 0)
            {
                astar.Begin(here, LastDecision.Cell, Tuning.costs, 0f, Tuning.maxRouteNodes);
                astar.Step(int.MaxValue);
                smoother.Smooth(route, astar.CopyRoute(route), Tuning.costs, 0f, waypoints);
            }
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

        /// <summary>Is the last sighting recent and about the current belief, so its look direction still applies?</summary>
        public bool SightingStillApplies(float now) =>
            LastSighting.Valid && Memory.HasBelief(now) &&
            LastSighting.Time >= Memory.Current.Time - 1e-3f && LastSighting.Age(now) <= Tuning.lookMemory;

        // ---------------------------------------------------------------- thinking

        /// <summary>
        /// Decide at the scheduler rate: the mode, the target cell, and whether to start a new route search
        /// (returned true). The search itself runs in StepRoute.
        /// </summary>
        public bool Think(Vector3 ghostPosition, float now)
        {
            bool believes = Memory.HasBelief(now);
            bool wasStalking = Mode == GhostMode.Stalk;
            if (!believes && Mode != GhostMode.Idle) map.SetFlashlight(false, Vector3.zero, Vector3.forward);
            SetMode(believes ? GhostMode.Stalk : GhostMode.Idle);

            int goal = believes ? ChoosePosition(now, wasStalking ? TargetCell : -1) : HomeCell();
            bool changed = goal != TargetCell;
            TargetCell = goal;
            if (goal < 0) return false;

            int here = grid.ClampedCellAt(ghostPosition + Vector3.up * 0.1f);
            if (goal == here) return false;
            if (!changed && now < nextReplan) return false;

            astar.Begin(here, goal, Tuning.costs, now, Tuning.maxRouteNodes);
            nextReplan = now + Tuning.replanInterval;
            return true;
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

        // The best place to be near the believed player: utility score optimised by hill climbing.
        // 'current' is the stalking target being kept (hysteresis), or -1.
        int ChoosePosition(float now, int current)
        {
            Belief belief = Memory.Current;
            int beliefCell = grid.ClampedCellAt(belief.Position + Vector3.up * 0.1f);

            // The walking-distance field only reaches as far as the range term can still score.
            if (walkField.NeedsRebuild(grid.NearestWalkable(beliefCell)))
            {
                int maxSteps = Mathf.CeilToInt((Tuning.range.max + Tuning.range.softness) / grid.CellSize) + 2;
                walkField.BuildWalkingField(beliefCell, maxSteps);
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
                Weights = Tuning.weights,
                Range = Tuning.range,
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
