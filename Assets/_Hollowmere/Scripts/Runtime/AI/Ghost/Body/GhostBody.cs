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
    /// </summary>
    [RequireComponent(typeof(GhostMovement), typeof(GhostRouteFollower))]
    public sealed class GhostBody : MonoBehaviour, INoiseListener, IAiThinker, IDirectable
    {
        static readonly List<GhostBody> active = new List<GhostBody>();

        [Tooltip("The house this ghost haunts. Empty: the GhostHouseVolume it starts inside.")]
        [SerializeField] GhostHouseMap house;
        [SerializeField] SightSensor sight = new SightSensor();
        [Tooltip("1 = normal hearing.")]
        [SerializeField] float hearingAcuity = 1f;
        [Tooltip("Seconds between thoughts (0.2 = five times a second).")]
        [SerializeField] float thinkInterval = 0.2f;
        [Tooltip("Route-search cells per frame: the planning time slice.")]
        [SerializeField] int routeBudgetPerFrame = 120;

        [Header("Haunt")]
        [Tooltip("Where this ghost belongs. It drifts back here when it has lost the player. Empty: where it starts.")]
        [SerializeField] Transform hauntAnchor;
        [Tooltip("Metres from the anchor it is happy to stray while stalking.")]
        [SerializeField] float leash = 6f;

        [SerializeField] GhostTuning tuning = GhostTuning.Default;
        [SerializeField] bool showLabel = true;

        readonly Stopwatch watch = new Stopwatch();
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

        // INoiseListener / IAiThinker / IDirectable
        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => hearingAcuity;
        public float ThinkInterval => thinkInterval;
        public Vector3 Position => transform.position;
        public bool IsAwake => Mode != GhostMode.Idle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => active.Clear();

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
            brain = new GhostBrain(house.Runtime, tuning, GetInstanceID());
            brain.WarmUp(transform.position);
            brain.ModeChanged += (from, to) => ModeChanged?.Invoke(from, to);
            return true;
        }

        public void Think(float deltaTime)
        {
            if (!EnsureBrain()) return;
            watch.Restart();
            float now = Time.time;

            brain.Tuning = tuning;
            brain.HasAnchor = true;
            brain.Anchor = hauntAnchor != null ? hauntAnchor.position : startPosition;
            brain.Leash = leash;

            // Sight: the only way the ghost learns which way the player looks and what they carry.
            IPlayerState player = PlayerRegistry.Current;
            bool seesNow = sight.Tick(transform, player, deltaTime);
            if (seesNow) brain.See(player.Position, player.LookDirection, player.HeldWeapon, player.FlashlightOn, now);
            if (seesNow != seeing)
            {
                seeing = seesNow;
                if (seeing) Spotted?.Invoke();
                else LostSight?.Invoke();
            }

            brain.Think(transform.position, now);
            Look(now);

            watch.Stop();
            LastThinkMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        void Update()
        {
            if (brain == null) return;
            watch.Restart();
            brain.StepRoute(routeBudgetPerFrame, Time.time);
            watch.Stop();
            LastRouteSliceMs = (float)watch.Elapsed.TotalMilliseconds;

            if (brain.RouteVersion != routeVersion)
            {
                routeVersion = brain.RouteVersion;
                follower.SetRoute(brain.Waypoints, brain.WaypointCount);
            }
        }

        // Face the believed player (at chest height), or the way it is going when it believes nothing.
        void Look(float now)
        {
            if (brain.Memory.HasBelief(now))
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
            DebugLabel.Above(transform, 2.3f, name + "  " + brain.Mode + "\nsight " + sight.Meter.ToString("F2") + "  belief " + belief);
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
