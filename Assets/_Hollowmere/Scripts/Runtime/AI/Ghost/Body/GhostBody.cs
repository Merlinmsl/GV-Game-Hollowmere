using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hollowmere.Agents;
using Hollowmere.AI.Debugging;
using Hollowmere.AI.Director;
using Hollowmere.AI.Scheduling;
using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// The ghost's body: the thin Unity side of one ghost. It owns the senses (the shared SightSensor and
    /// a NoiseSystem ear), hands what they report to the GhostBrain, and carries out the brain's decision
    /// through the route follower and GhostMovement.
    ///  - Thinks through the shared AiScheduler (IAiThinker), a few times a second, staggered across ghosts.
    ///  - Runs only a budgeted slice of the route search each frame, so planning never spikes a frame.
    ///  - Looks at a point placed on its BELIEF, never at the player: a wrong belief means looking the wrong way.
    ///  - The Director can point it somewhere (IDirectable); it then believes, weakly, that the player is there.
    ///  - The ice gun freezes it (IIceable). Who it is (Mara or a servant) comes from its GhostProfile.
    /// </summary>
    [RequireComponent(typeof(GhostMovement), typeof(GhostRouteFollower))]
    public sealed class GhostBody : MonoBehaviour, INoiseListener, IAiThinker, IDirectable, IIceable
    {
        static readonly List<GhostBody> active = new List<GhostBody>();

        /// <summary>
        /// Route-search cells ALL ghosts together may expand in one frame. Shared, so the planning cost of a
        /// frame stays the same however many ghosts there are; a ghost that finds the pool empty waits a frame.
        /// </summary>
        public static int SharedRouteNodesPerFrame = 240;
        static int budgetFrame = -1;
        static int nodesLeft;
        // The frame in which some ghost last ran a full position search: at most one per frame.
        static int searchFrame = -1;

        /// <summary>Seconds after one ghost strikes before any ghost in the house may strike again.</summary>
        public static float StrikeGapSeconds = 1.5f;
        // Until when the house-wide strike token is taken: one striker at a time.
        static float strikeTakenUntil = float.NegativeInfinity;

        [Tooltip("The house this ghost haunts. Empty: the GhostHouseVolume it starts inside.")]
        [SerializeField] GhostHouseMap house;
        [SerializeField] SightSensor sight = new SightSensor();
        [Tooltip("1 = normal hearing.")]
        [SerializeField] float hearingAcuity = 1f;
        [Tooltip("Seconds between thoughts (0.2 = five times a second).")]
        [SerializeField] float thinkInterval = 0.2f;
        [Tooltip("Most route-search cells this ghost may take from the shared per-frame pool: its planning time slice.")]
        [SerializeField] int routeBudgetPerFrame = 120;

        [Header("Who it is")]
        [Tooltip("Mara or a servant. Empty: the tuning and modes below.")]
        [SerializeField] GhostProfile profile;
        [Tooltip("Where this ghost belongs. It drifts back here when it has lost the player. Empty: where it starts.")]
        [SerializeField] Transform hauntAnchor;

        [Header("Used only without a profile")]
        [Tooltip("Metres from the anchor it is happy to stray while stalking.")]
        [SerializeField] float leash = 6f;
        [SerializeField] GhostTuning tuning = GhostTuning.Default;
        [SerializeField] ModeTuning modes = ModeTuning.Servant;

        [SerializeField] bool showLabel = true;

        const int MaxClaims = 8;

        readonly Stopwatch watch = new Stopwatch();
        readonly int[] claims = new int[MaxClaims];
        GhostBrain brain;
        GhostMovement movement;
        GhostRouteFollower follower;
        Transform lookPoint;
        Vector3 startPosition;
        bool seeing;
        bool warned;
        int routeVersion;

        /// <summary>Every ghost currently active, for the debug views and multi-ghost reservation.</summary>
        public static IReadOnlyList<GhostBody> Active => active;

        public GhostBrain Brain => brain;
        public GhostMode Mode => brain != null ? brain.Mode : GhostMode.Idle;
        public SightSensor Sight => sight;
        /// <summary>Time the last thought took, for the profiler and the debug panel.</summary>
        public float LastThinkMs { get; private set; }
        public float LastRouteSliceMs { get; private set; }

        public event Action<GhostMode, GhostMode> ModeChanged;
        /// <summary>The ghost has just recognised the player.</summary>
        public event Action Spotted;
        /// <summary>The player has just gone out of the ghost's sight.</summary>
        public event Action LostSight;
        /// <summary>The ghost struck at the player, whom it saw at this position. Damage is the player side's job.</summary>
        public event Action<Vector3> Struck;
        /// <summary>The ice gun has frozen the ghost.</summary>
        public event Action Frozen;
        /// <summary>The ice has worn off; the ghost flees.</summary>
        public event Action Freed;

        public GhostProfile Profile => profile;
        GhostTuning CurrentTuning => profile != null ? profile.Tuning : tuning;
        ModeTuning CurrentModes => profile != null ? profile.Modes : modes;
        float CurrentLeash => profile != null ? profile.Leash : leash;

        // INoiseListener / IAiThinker / IDirectable
        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => hearingAcuity;
        public float ThinkInterval => thinkInterval;
        public Vector3 Position => transform.position;
        public bool IsAwake => Mode != GhostMode.Idle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            active.Clear();
            budgetFrame = -1;
            searchFrame = -1;
            strikeTakenUntil = float.NegativeInfinity;
            sightSensorWarmed = false;
        }

        void Awake()
        {
            movement = GetComponent<GhostMovement>();
            follower = GetComponent<GhostRouteFollower>();
            startPosition = transform.position;

            // Not a child: it must stay where the belief is while the ghost moves.
            lookPoint = new GameObject(name + " look point") { hideFlags = HideFlags.DontSave }.transform;
        }

        void OnEnable()
        {
            active.Add(this);
            NoiseSystem.Register(this);
            AiScheduler.Register(this);
            DirectableRegistry.Register(this);
        }

        void OnDisable()
        {
            active.Remove(this);
            NoiseSystem.Unregister(this);
            AiScheduler.Unregister(this);
            DirectableRegistry.Unregister(this);
        }

        void OnDestroy()
        {
            if (lookPoint != null) Destroy(lookPoint.gameObject);
        }

        // Every house volume has registered (OnEnable) by now. Building and warming the brain here, not in
        // the first thought, keeps its one-off costs (grid-sized arrays, first-call compile) out of play.
        void Start() => EnsureBrain();

        bool EnsureBrain()
        {
            if (brain != null) return true;
            if (house == null) house = GhostHouseVolume.MapAt(transform.position + Vector3.up);
            if (house == null || !house.IsBaked)
            {
                if (!warned) UnityEngine.Debug.LogError($"[GhostBody] {name} is not inside a baked GhostHouseVolume and has no house map.", this);
                warned = true;
                return false;
            }
            brain = new GhostBrain(house.Runtime, CurrentTuning, CurrentModes, GetInstanceID());
            brain.WarmUp(transform.position);
            WarmSightSensor();
            brain.ModeChanged += OnModeChanged;
            brain.Struck += at =>
            {
                strikeTakenUntil = Time.time + StrikeGapSeconds;     // before the next ghost thinks
                HandleStrike(at);
            };
            brain.Freed += () => Freed?.Invoke();
            return true;
        }

        void HandleStrike(Vector3 at)
        {
            Struck?.Invoke(at);
            IPlayerState player = PlayerRegistry.Current;
            if (player is MonoBehaviour mb)
            {
                var damageable = mb.GetComponent<Hollowmere.Combat.IDamageable>();
                damageable?.TakeDamage(35f, at, (player.Position - transform.position).normalized, gameObject);
            }
        }

        static bool sightSensorWarmed;

        // The first time any sight sensor actually sees the player its code is compiled (about 2.4 ms once,
        // in the middle of play). A throwaway sensor looking at a dummy target half a metre ahead takes that
        // cost now, while the scene loads. The ghost's own sensor and the shared senses are left untouched.
        void WarmSightSensor()
        {
            if (sightSensorWarmed) return;
            sightSensorWarmed = true;
            var probe = new SightSensor();
            var target = new WarmUpTarget(transform.position + transform.forward * 0.5f);
            for (int i = 0; i < 3; i++) probe.Tick(transform, target, 0.5f);
        }

        sealed class WarmUpTarget : IPlayerState
        {
            public WarmUpTarget(Vector3 position) => Position = position;
            public Vector3 Position { get; }
            public Vector3 LookDirection => Vector3.forward;
            public float Health01 => 1f;
            public float Ammo01 => 1f;
            public WeaponKind HeldWeapon => WeaponKind.None;
            public bool FlashlightOn => false;
            public bool IsCrouching => false;
        }

        void OnModeChanged(GhostMode from, GhostMode to)
        {
            if (to == GhostMode.Frozen)
            {
                follower.Clear();
                movement.Stop();
                movement.SetLookTarget(null);
                Frozen?.Invoke();
            }
            ModeChanged?.Invoke(from, to);
        }

        public void ReceiveIce(float seconds)
        {
            if (EnsureBrain()) brain.Freeze(seconds, Time.time);
        }

        public void Think(float deltaTime)
        {
            if (!EnsureBrain()) return;
            using var _ = GhostProfiling.Think.Auto();
            watch.Restart();
            float now = Time.time;

            brain.Tuning = CurrentTuning;
            brain.Modes = CurrentModes;
            brain.HasAnchor = true;
            brain.Anchor = hauntAnchor != null ? hauntAnchor.position : startPosition;
            brain.Leash = CurrentLeash;
            ClaimOthersCells();

            // Sight: the only way the ghost learns which way the player looks and what they carry.
            IPlayerState player = PlayerRegistry.Current;
            bool seesNow;
            using (GhostProfiling.Sense.Auto())
            {
                seesNow = sight.Tick(transform, player, deltaTime);
                if (seesNow) brain.See(player.Position, player.LookDirection, player.HeldWeapon, player.FlashlightOn, now);
            }
            brain.PlayerInView = sight.InView;        // someone there, even if not yet recognised
            if (seesNow != seeing)
            {
                seeing = seesNow;
                if (seeing) Spotted?.Invoke();
                else LostSight?.Invoke();
            }

            // One full position search per frame across all ghosts; the token is only used up if taken.
            brain.MayRunFullSearch = searchFrame != Time.frameCount;
            brain.MayStrike = now >= strikeTakenUntil;
            brain.Think(transform.position, now);
            if (brain.LastThinkHeavy) searchFrame = Time.frameCount;
            movement.SetSpeedScale(brain.SpeedScale);
            Look(now);

            watch.Stop();
            LastThinkMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        // Multi-ghost reservation: the cells the other ghosts in this house are heading for. The brain
        // will not pick those cells and is pushed away from them (crowd term), so ghosts spread out and flank.
        void ClaimOthersCells()
        {
            int count = 0;
            for (int i = 0; i < active.Count && count < MaxClaims; i++)
            {
                GhostBody other = active[i];
                if (other == this || other.brain == null || other.brain.Map != brain.Map || other.brain.TargetCell < 0) continue;
                claims[count++] = other.brain.TargetCell;
            }
            brain.Reserved = claims;
            brain.ReservedCount = count;
        }

        // LateUpdate, after the scheduler's thoughts, so a frame that already paid for a position search
        // (the heaviest step) spends nothing on route search: those slices simply move to the next frame.
        void LateUpdate()
        {
            if (brain == null) return;
            if (Time.frameCount != budgetFrame)
            {
                budgetFrame = Time.frameCount;
                nodesLeft = searchFrame == Time.frameCount ? 0 : SharedRouteNodesPerFrame;
            }
            LastRouteSliceMs = 0f;
            int budget = Mathf.Min(routeBudgetPerFrame, nodesLeft);
            if (budget > 0 && brain.Router.Status == RouteStatus.Searching)
            {
                watch.Restart();
                int before = brain.Router.NodesExpanded;
                brain.StepRoute(budget, Time.time);
                nodesLeft -= Mathf.Max(1, brain.Router.NodesExpanded - before);
                watch.Stop();
                LastRouteSliceMs = (float)watch.Elapsed.TotalMilliseconds;
            }

            if (brain.RouteVersion != routeVersion)
            {
                routeVersion = brain.RouteVersion;
                follower.SetRoute(brain.Waypoints, brain.WaypointCount);
            }
        }

        // Face the believed player (at chest height), or the way it is going when it believes nothing.
        // Frozen, it does not turn at all.
        void Look(float now)
        {
            if (brain.Mode == GhostMode.Frozen) movement.SetLookTarget(null);
            else if (brain.Memory.HasBelief(now))
            {
                lookPoint.position = brain.Memory.Current.Position + Vector3.up * 1.2f;
                movement.SetLookTarget(lookPoint);
            }
            else movement.SetLookTarget(null);
        }

        public void OnHeard(in Noise noise, float pathDistance)
        {
            if (brain == null || noise.Source == gameObject) return;
            brain.Hear(noise.Position, noise.Kind, Time.time);
        }

        public void WakeToward(Vector3 point)
        {
            if (EnsureBrain()) brain.Told(point, Time.time);
        }

        void OnGUI()
        {
            if (!showLabel || brain == null) return;
            float now = Time.time;
            string belief = brain.Memory.HasBelief(now)
                ? brain.Memory.Current.Source + " " + brain.Memory.ConfidenceAt(now).ToString("F2")
                : "-";
            string edge = (brain.Lurking ? "  lurking" : "") + (brain.PlayerCornered ? "  cornered" : "") +
                          (brain.PlayerOutside ? "  outside" : "");
            DebugLabel.Above(transform, 2.3f, name + "  " + brain.Mode + edge + "\nsight " + sight.Meter.ToString("F2") + "  belief " + belief);
            // Utilities only while hunting: the panel would be noise otherwise.
            if (brain.Mode != GhostMode.Idle && brain.Mode != GhostMode.Frozen)
                DebugLabel.Above(transform, 3.1f,
                    "S " + brain.Utility(GhostMode.Stalk).ToString("F2") + "  A " + brain.Utility(GhostMode.Ambush).ToString("F2") +
                    "  K " + brain.Utility(GhostMode.Strike).ToString("F2") + "  F " + brain.Utility(GhostMode.Flee).ToString("F2"));
        }

        void OnDrawGizmosSelected()
        {
            if (brain == null) return;
            float now = Application.isPlaying ? Time.time : 0f;
            if (brain.Memory.HasBelief(now))
            {
                Gizmos.color = Color.Lerp(Color.grey, Color.red, brain.Memory.ConfidenceAt(now));
                Gizmos.DrawWireSphere(brain.Memory.Current.Position + Vector3.up, 0.5f);
            }
            if (brain.TargetCell >= 0)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireCube(brain.Map.Grid.CellCentre(brain.TargetCell), Vector3.one * 0.9f);
            }
        }
    }
}
