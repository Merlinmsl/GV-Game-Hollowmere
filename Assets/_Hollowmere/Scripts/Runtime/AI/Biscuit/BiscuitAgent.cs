using System;
using System.Collections.Generic;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Biscuit's body, the only MonoBehaviour. It senses (hearing through NoiseSystem, smell through
    /// ScentRegistry), feeds the brain 8 times a second, and carries out what the brain decides with
    /// the NavMeshAgent. It decides nothing itself. Animation can listen to OnStateChanged.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class BiscuitAgent : MonoBehaviour, INoiseListener, IBiscuitBody
    {
        const string DogAgentType = "Dog";

        // A sniff follows at most this many scent trails (path searches) per think: the AI plan
        // allows 3 path searches per frame for everyone together.
        const int PathSearchesPerSniffStep = 3;

        static readonly float[] RetreatAngles = { 0f, 45f, -45f, 90f, -90f };

        [SerializeField] BiscuitTuning tuning = new BiscuitTuning();
        [Tooltip("Optional. Played when Biscuit barks.")]
        [SerializeField] AudioSource barkSound;
        [Tooltip("Optional. Played when Biscuit growls.")]
        [SerializeField] AudioSource growlSound;

        NavMeshAgent agent;
        NavMeshQueryFilter dogFilter;
        float nextThinkTime;
        float sniffRange;
        int sniffCursor;
        Transform taggedPlayer;
        float nextPlayerSearchTime;

        public BiscuitBrain Brain { get; private set; }
        public BiscuitTuning Tuning => tuning;

        /// <summary>The action Biscuit just started ("" when it stops one). For the Animator.</summary>
        public event Action<string> OnStateChanged;
        public event Action OnBark;
        public event Action OnGrowl;

        public bool HasPath => agent != null && agent.hasPath;
        public Vector3 Destination => agent != null ? agent.destination : transform.position;

        // INoiseListener
        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => tuning.hearingAcuity;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            UseDogNavMesh();

            Brain = new BiscuitBrain(tuning, this);
            Brain.ActionChanged += RaiseStateChanged;

            // Start at a random point in the think cycle, so agents do not all think on the same frame.
            nextThinkTime = Time.time + Random.value / tuning.thinkRate;
        }

        void Start()
        {
            if (!agent.isOnNavMesh) WarpNear(transform.position);
        }

        void OnEnable()
        {
            NoiseSystem.Register(this);
            ScentRegistry.Removed += Brain.OnSourceGone;
        }

        void OnDisable()
        {
            NoiseSystem.Unregister(this);
            ScentRegistry.Removed -= Brain.OnSourceGone;
        }

        void Update()
        {
            float now = Time.time;
            if (now < nextThinkTime) return;

            // Keep a steady rhythm; after a long hitch, carry on from now instead of catching up.
            nextThinkTime += 1f / tuning.thinkRate;
            if (nextThinkTime < now) nextThinkTime = now + 1f / tuning.thinkRate;

            SmellAround(now);
            Brain.Think(Perceive(now));
        }

        BiscuitPerception Perceive(float now)
        {
            var p = new BiscuitPerception { Now = now, Position = transform.position };

            // Biscuit is at the player's side, so like the Director it may read the player directly.
            IPlayerState player = PlayerRegistry.Current;
            if (player != null)
            {
                p.HasPlayer = true;
                p.PlayerPosition = player.Position;
                p.PlayerLook = player.LookDirection;
                p.PlayerHealth01 = player.Health01;
                return p;
            }

            // Until a player registers an IPlayerState (the debug walker does not), follow whatever is
            // tagged Player, at full health. Looked up at most once a second, never every think.
            if (taggedPlayer == null && now >= nextPlayerSearchTime)
            {
                nextPlayerSearchTime = now + 1f;
                GameObject tagged = GameObject.FindWithTag("Player");
                if (tagged != null) taggedPlayer = tagged.transform;
            }
            if (taggedPlayer != null)
            {
                p.HasPlayer = true;
                p.PlayerPosition = taggedPlayer.position;
                p.PlayerLook = taggedPlayer.forward;
                p.PlayerHealth01 = 1f;
            }
            return p;
        }

        // Close up, Biscuit smells the dead without stopping: through a door, round a corner.
        void SmellAround(float now)
        {
            Vector3 nose = transform.position;
            float range2 = tuning.noseRange * tuning.noseRange;
            IReadOnlyList<ScentSource> sources = ScentRegistry.All;
            for (int i = 0; i < sources.Count; i++)
            {
                ScentSource source = sources[i];
                if ((source.Position - nose).sqrMagnitude <= range2)
                    Brain.OnSmelt(source.Id, source.Position, now);
            }
        }

        public void OnHeard(in Noise noise, float pathDistance)
        {
            if (noise.Source == gameObject) return;   // its own bark

            // Only the dead interest Biscuit: anything it can smell, or a groan from anything.
            int id;
            if (noise.Source != null && ScentRegistry.TryGet(noise.Source, out ScentSource source))
                id = source.Id;
            else if (noise.Kind == NoiseKind.Groan)
                id = noise.Source != null ? noise.Source.GetInstanceID() : 0;
            else
                return;

            Brain.OnHeard(id, noise.Position, Time.time);
        }

        // ---------------------------------------------------------------- IBiscuitBody

        public void MoveTo(Vector3 destination, float stopDistance, bool run)
        {
            if (!agent.isOnNavMesh) return;

            agent.isStopped = false;
            agent.stoppingDistance = stopDistance;
            agent.speed = run ? tuning.runSpeed : tuning.walkSpeed;

            // Only ask for a new route when the target really moved: every SetDestination is a path search.
            bool noRoute = !agent.hasPath && !agent.pathPending;
            if (noRoute || (agent.destination - destination).sqrMagnitude > 0.25f)
                agent.SetDestination(destination);
        }

        public void Stop()
        {
            if (!agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
        }

        public bool HasArrived =>
            agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

        // A partial path ends short of the target (a locked door in between). Once Biscuit is at the
        // end of it there is nowhere further to go, so the action has failed.
        public bool PathFailed =>
            !agent.isOnNavMesh ||
            (!agent.pathPending &&
             (agent.pathStatus == NavMeshPathStatus.PathInvalid ||
              (agent.pathStatus == NavMeshPathStatus.PathPartial && HasArrived)));

        public void WarpNear(Vector3 point)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 4f, dogFilter)) agent.Warp(hit.position);
        }

        public void Growl(Vector3 towards)
        {
            Vector3 direction = towards - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(direction);

            if (growlSound != null) growlSound.Play();
            OnGrowl?.Invoke();
        }

        public void Bark()
        {
            NoiseSystem.Emit(transform.position, Loudness.Bark, NoiseKind.Bark, gameObject);
            if (barkSound != null) barkSound.Play();
            OnBark?.Invoke();
        }

        public void BeginSniff(float range)
        {
            sniffRange = range;
            sniffCursor = 0;
        }

        public bool SniffStep()
        {
            Vector3 nose = transform.position;
            float range2 = sniffRange * sniffRange;
            IReadOnlyList<ScentSource> sources = ScentRegistry.All;
            int searches = 0;

            while (sniffCursor < sources.Count && searches < PathSearchesPerSniffStep)
            {
                ScentSource source = sources[sniffCursor++];

                // Cheap test first, as in NoiseSystem: the walking route is never shorter than the straight line.
                if ((source.Position - nose).sqrMagnitude > range2) continue;

                // Scent follows the walkable routes, like sound: a wall between them hides it.
                searches++;
                if (NoiseSystem.PathDistance(source.Position, nose) <= sniffRange)
                    Brain.OnSmelt(source.Id, source.Position, Time.time);
            }
            return sniffCursor >= sources.Count;
        }

        public bool TryFindRetreatPoint(Vector3 threat, float distance, out Vector3 point)
        {
            Vector3 here = transform.position;
            Vector3 away = here - threat;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;

            // Straight away first, then 45 and 90 degrees to either side if a wall is in the way.
            float safe2 = tuning.dangerDistance * tuning.dangerDistance;
            for (int i = 0; i < RetreatAngles.Length; i++)
            {
                Vector3 candidate = here + Quaternion.Euler(0f, RetreatAngles[i], 0f) * away * distance;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, dogFilter) &&
                    (hit.position - threat).sqrMagnitude > safe2)
                {
                    point = hit.position;
                    return true;
                }
            }

            point = here;
            return false;
        }

        public bool TryFindLurePoint(Vector3 player, Vector3 threat, float distance, out Vector3 point)
        {
            Vector3 toThreat = threat - player;
            toThreat.y = 0f;
            toThreat = toThreat.sqrMagnitude > 0.01f ? toThreat.normalized : Vector3.forward;

            // Best is off to one side of the player: the dead turn away from the player to follow the
            // bark, and Biscuit does not have to run through them to get there.
            point = player;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                float towardsThreat = Vector3.Dot(direction, toThreat);
                if (towardsThreat > 0.5f) continue;   // that way runs into them

                if (!NavMesh.SamplePosition(player + direction * distance, out NavMeshHit hit, 3f, dogFilter))
                    continue;

                float score = 1f - Mathf.Abs(towardsThreat);
                if (score <= bestScore) continue;
                bestScore = score;
                point = hit.position;
            }
            return bestScore > float.NegativeInfinity;
        }

        // ---------------------------------------------------------------- setup

        // The project bakes one NavMesh per agent type. Only the Dog one is linked between zones,
        // which is what lets Biscuit follow the player everywhere.
        void UseDogNavMesh()
        {
            int dog = FindAgentType(DogAgentType);
            if (dog == int.MinValue)
            {
                Debug.LogWarning("BiscuitAgent: this project has no 'Dog' NavMesh agent type.", this);
            }
            else if (agent.agentTypeID != dog)
            {
                Debug.LogWarning("BiscuitAgent: set the NavMeshAgent's Agent Type to Dog on the prefab " +
                                 "(Hollowmere > Biscuit > Set Up Biscuit Prefab). Switching it now.", this);
                agent.agentTypeID = dog;
            }
            dogFilter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
        }

        static int FindAgentType(string name)
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == name) return id;
            }
            return int.MinValue;
        }

        void RaiseStateChanged(string action) => OnStateChanged?.Invoke(action);
    }
}
