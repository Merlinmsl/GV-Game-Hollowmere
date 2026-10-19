using System.Collections.Generic;
using Hollowmere.AI.Scheduling;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hollowmere.AI.Director
{
    /// <summary>
    /// The Director: paces the game by deciding when, where and how many of the dormant dead wake.
    /// It lives in the World scene so it survives zone changes, and thinks twice a second.
    ///
    /// Each tick it:
    ///   1. measures the player's stress (StressModel) and learns their play style (PlayerProfile);
    ///   2. moves the target tension along its cycle (TensionCurve);
    ///   3. predicts the player's route to the zone exit (RoutePredictor, A* on the NavMesh);
    ///   4. scores every dormant enemy as a candidate: hidden from the player, 15 to 35 m away by
    ///      walking distance, and ahead on the predicted route;
    ///   5. lets the DirectorBrain pick an action by utility, and carries it out.
    ///
    /// The Director is allowed to know everything about the player (it is the game's pacing, not a
    /// character), but it never moves enemies and never tells them where the player is: it wakes
    /// them toward a point ahead on the route, or makes a noise near them, and their senses do the rest.
    /// Press F2 in Play mode to see its thinking.
    /// </summary>
    public sealed class GameDirector : MonoBehaviour, IAiThinker
    {
        [SerializeField] StressModel stress = new StressModel();
        [SerializeField] TensionCurve tension = new TensionCurve();
        [SerializeField] PlayerProfile profile = new PlayerProfile();

        [Tooltip("Seconds between Director thoughts.")]
        [SerializeField] float thinkInterval = 0.5f;
        [Tooltip("Never more than this many enemies awake at once (performance and fairness).")]
        [SerializeField] int maxAwake = 12;
        [Tooltip("Dormant enemies further than this from the player (straight line) are not considered.")]
        [SerializeField] float considerRadius = 50f;
        [Tooltip("Woken enemies head for the route this many metres ahead of the player.")]
        [SerializeField] float interceptAhead = 10f;
        [Tooltip("How loud a staged noise is (a shutter banging, a boat knocking).")]
        [SerializeField] float stagedLoudness = Loudness.Groan;
        [Tooltip("Seconds between route predictions.")]
        [SerializeField] float replanInterval = 3f;
        [Tooltip("Gameplay markers whose names start with this are zone exits.")]
        [SerializeField] string exitPrefix = "ZoneExit_";
        [SerializeField] bool showDebug;

        const int TopCandidates = 3;
        const int HistoryLength = 120;      // 60 s of history at two thoughts a second

        readonly DirectorBrain brain = new DirectorBrain();
        readonly RoutePredictor route = new RoutePredictor();
        readonly List<Transform> exits = new List<Transform>();
        readonly IDirectable[] candidates = new IDirectable[TopCandidates];
        readonly float[] candidateScores = new float[TopCandidates];
        readonly float[] stressHistory = new float[HistoryLength];
        readonly float[] targetHistory = new float[HistoryLength];

        DirectorSituation situation;
        DirectorAction lastAction = DirectorAction.HoldBack;
        string lastEvent = "nothing yet";
        float lastEventTime;
        float nextReplanTime;
        float playerAlong;
        int historyIndex;
        Texture2D pixel;

        public float ThinkInterval => thinkInterval;

        void OnEnable()
        {
            AiScheduler.Register(this);
            NoiseSystem.Emitted += OnNoise;
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            FindExits();
            lastEventTime = Time.time;
        }

        void OnDisable()
        {
            AiScheduler.Unregister(this);
            NoiseSystem.Emitted -= OnNoise;
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame) showDebug = !showDebug;
        }

        // ---------------------------------------------------------------- thinking

        public void Think(float deltaTime)
        {
            IPlayerState player = PlayerRegistry.Current;
            if (player == null) return;

            int dormant = 0, awake = 0, awakeNear = 0;
            float nearSquared = stress.PressureRadius * stress.PressureRadius;
            for (int i = 0; i < DirectableRegistry.Count; i++)
            {
                IDirectable agent = DirectableRegistry.Get(i);
                if (!agent.IsAwake) { dormant++; continue; }
                awake++;
                if ((agent.Position - player.Position).sqrMagnitude <= nearSquared) awakeNear++;
            }

            stress.Update(deltaTime, player.Health01, player.Ammo01, awakeNear);
            tension.Update(deltaTime, stress.Value);
            profile.Update(deltaTime);

            if (Time.time >= nextReplanTime)
            {
                nextReplanTime = Time.time + replanInterval;
                Transform exit = NearestExit(player.Position);
                if (exit == null || !route.Plan(player.Position, exit.position)) route.Plan(player.Position, player.Position);
            }
            playerAlong = route.HasRoute ? route.Project(player.Position, out _) : 0f;

            situation = new DirectorSituation
            {
                Stress = stress.Value,
                Target = tension.Target,
                Quietness = profile.Quietness,
                SecondsSinceLastEvent = Time.time - lastEventTime,
                DormantCount = dormant,
                AwakeCount = awake,
                MaxAwake = maxAwake,
                BestCandidate = 0f
            };

            // Scoring candidates costs path searches, so only do it when more tension is wanted.
            if (situation.Target > situation.Stress && dormant > 0) FindCandidates(player);
            situation.BestCandidate = candidateScores[0];

            lastAction = brain.Decide(situation);
            if (lastAction == DirectorAction.WakeDead) WakeDead(player);
            else if (lastAction == DirectorAction.StageNoise) StageNoise(player);

            stressHistory[historyIndex] = stress.Value;
            targetHistory[historyIndex] = tension.Target;
            historyIndex = (historyIndex + 1) % HistoryLength;
        }

        void FindCandidates(IPlayerState player)
        {
            for (int i = 0; i < TopCandidates; i++) { candidates[i] = null; candidateScores[i] = 0f; }

            for (int i = 0; i < DirectableRegistry.Count; i++)
            {
                IDirectable agent = DirectableRegistry.Get(i);
                if (agent.IsAwake) continue;
                float score = ScoreCandidate(agent, player);
                if (score <= candidateScores[TopCandidates - 1]) continue;

                // Insert into the short sorted list of the best few.
                int slot = TopCandidates - 1;
                while (slot > 0 && score > candidateScores[slot - 1])
                {
                    candidates[slot] = candidates[slot - 1];
                    candidateScores[slot] = candidateScores[slot - 1];
                    slot--;
                }
                candidates[slot] = agent;
                candidateScores[slot] = score;
            }
        }

        /// <summary>
        /// How good a dormant enemy is to use now, 0 to 1:
        ///   distance: 1 between 15 and 35 m of walking distance, falling to 0 at 8 m and at 50 m
        ///   hidden:   1 if the player cannot see it, 0.2 if they can (no popping up in plain sight)
        ///   ahead:    1 if it lies ahead on the predicted route and within 6 m of it, falling with
        ///             distance from the route; 0.3 of that if it is behind the player
        ///   score = distance x hidden x (0.4 + 0.6 x ahead)
        /// </summary>
        float ScoreCandidate(IDirectable agent, IPlayerState player)
        {
            Vector3 position = agent.Position;
            if ((position - player.Position).sqrMagnitude > considerRadius * considerRadius) return 0f;

            float walking = NoiseSystem.PathDistance(player.Position, position);
            float distance = Trapezoid(walking, 8f, 15f, 35f, 50f);
            if (distance <= 0f) return 0f;

            float hidden = CanPlayerSee(player, position) ? 0.2f : 1f;

            float ahead = 0.5f;
            if (route.HasRoute)
            {
                float along = route.Project(position, out float lateral);
                float closeness = 1f - Mathf.Clamp01((lateral - 6f) / 20f);
                ahead = (along > playerAlong ? 1f : 0.3f) * closeness;
            }

            return distance * hidden * (0.4f + 0.6f * ahead);
        }

        static bool CanPlayerSee(IPlayerState player, Vector3 position)
        {
            Vector3 eye = player.Position + Vector3.up * 1.6f;
            Vector3 target = position + Vector3.up * 1.2f;
            Vector3 toTarget = target - eye;
            if (Vector3.Angle(player.LookDirection, toTarget) > 60f) return false;

            float length = toTarget.magnitude;
            if (Physics.Raycast(eye, toTarget / length, out RaycastHit hit, length, ~0, QueryTriggerInteraction.Ignore)
                && hit.distance < length - 1f)
                return false;
            return true;
        }

        static float Trapezoid(float x, float zeroLow, float oneLow, float oneHigh, float zeroHigh)
        {
            if (x <= zeroLow || x >= zeroHigh) return 0f;
            if (x < oneLow) return (x - zeroLow) / (oneLow - zeroLow);
            if (x > oneHigh) return (zeroHigh - x) / (zeroHigh - oneHigh);
            return 1f;
        }

        // ---------------------------------------------------------------- acting

        void WakeDead(IPlayerState player)
        {
            Vector3 point = route.HasRoute ? route.PointAt(playerAlong + interceptAhead) : player.Position;
            int wanted = Mathf.Min(DirectorBrain.WakeCount(situation), maxAwake - situation.AwakeCount);
            int woken = 0;
            for (int i = 0; i < TopCandidates && woken < wanted; i++)
            {
                if (candidates[i] == null || candidateScores[i] <= 0f) continue;
                candidates[i].WakeToward(point);
                woken++;
            }
            if (woken == 0) return;
            lastEventTime = Time.time;
            lastEvent = "woke " + woken + " toward the route " + interceptAhead.ToString("F0") + " m ahead";
        }

        void StageNoise(IPlayerState player)
        {
            IDirectable best = candidates[0];
            if (best == null) return;

            // Halfway between the sleeper and the route ahead, so it walks into the player's path.
            Vector3 ahead = route.HasRoute ? route.PointAt(playerAlong + interceptAhead) : player.Position;
            Vector3 point = Vector3.Lerp(best.Position, ahead, 0.5f);
            var humanoid = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 4f, humanoid)) point = hit.position;

            int heard = NoiseSystem.Emit(point, stagedLoudness, NoiseKind.Staged, gameObject);
            lastEventTime = Time.time;
            lastEvent = "staged a noise, heard by " + heard;
        }

        // ---------------------------------------------------------------- listening and exits

        void OnNoise(Noise noise, int heardBy)
        {
            // Noises made where the player stands are the player's own: they teach the profile.
            IPlayerState player = PlayerRegistry.Current;
            if (player == null || noise.Kind == NoiseKind.Staged) return;
            if ((noise.Position - player.Position).sqrMagnitude > 4f) return;
            profile.RecordNoise(noise.Loudness, noise.Kind == NoiseKind.Gunshot);
        }

        void OnSceneChanged(Scene scene, LoadSceneMode mode) => FindExits();
        void OnSceneUnloaded(Scene scene) => FindExits();

        // Runs only when a zone loads or unloads, never per frame.
        void FindExits()
        {
            exits.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (child.name.StartsWith(exitPrefix)) exits.Add(child);
            }
        }

        Transform NearestExit(Vector3 from)
        {
            Transform nearest = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < exits.Count; i++)
            {
                if (exits[i] == null) continue;
                float d = (exits[i].position - from).sqrMagnitude;
                if (d < best) { best = d; nearest = exits[i]; }
            }
            return nearest;
        }

        // ---------------------------------------------------------------- F2 debug view

        void OnGUI()
        {
            if (!showDebug) return;
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }

            const float width = 380f;
            float x = Screen.width - width - 12f, y = 12f;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(x - 8f, y - 6f, width + 16f, 330f), pixel);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y, width, 22f), "<b>DIRECTOR</b>   (F2 to hide)", Rich());
            y += 24f;
            GUI.Label(new Rect(x, y, width, 66f),
                "Phase " + tension.Phase + " (" + tension.PhaseTime.ToString("F0") + " s)\n" +
                "Stress " + stress.Value.ToString("F2") + "   Target " + tension.Target.ToString("F2") + "\n" +
                "Player " + profile.Style + "  (noise " + profile.NoiseRate.ToString("F1") + " m/s, quietness " + profile.Quietness.ToString("F2") + ")");
            y += 66f;
            GUI.Label(new Rect(x, y, width, 44f),
                "Dead: " + situation.DormantCount + " dormant, " + situation.AwakeCount + " awake (max " + maxAwake + ")\n" +
                "Route " + (route.HasRoute ? route.Length.ToString("F0") + " m to exit, you are " + playerAlong.ToString("F0") + " m along" : "none"));
            y += 48f;

            DrawScore(x, ref y, width, "Wake the dead", brain.Score(DirectorAction.WakeDead), lastAction == DirectorAction.WakeDead);
            DrawScore(x, ref y, width, "Stage a noise", brain.Score(DirectorAction.StageNoise), lastAction == DirectorAction.StageNoise);
            DrawScore(x, ref y, width, "Hold back", brain.Score(DirectorAction.HoldBack), lastAction == DirectorAction.HoldBack);
            GUI.Label(new Rect(x, y, width, 22f), "Last event: " + lastEvent + " (" + (Time.time - lastEventTime).ToString("F0") + " s ago)");
            y += 26f;

            // Stress (red) and target (white) over the last 60 seconds.
            const float graphHeight = 60f;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, width, graphHeight), pixel);
            float step = width / HistoryLength;
            for (int i = 0; i < HistoryLength; i++)
            {
                int sample = (historyIndex + i) % HistoryLength;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(x + i * step, y + graphHeight * (1f - targetHistory[sample]) - 1f, step, 2f), pixel);
                GUI.color = new Color(1f, 0.3f, 0.25f);
                GUI.DrawTexture(new Rect(x + i * step, y + graphHeight * (1f - stressHistory[sample]) - 1f, step, 2f), pixel);
            }
            GUI.color = Color.white;
        }

        void DrawScore(float x, ref float y, float width, string label, float score, bool chosen)
        {
            GUI.Label(new Rect(x, y, 130f, 20f), (chosen ? "> " : "  ") + label);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x + 135f, y + 4f, width - 185f, 12f), pixel);
            GUI.color = chosen ? new Color(1f, 0.75f, 0.2f) : new Color(0.6f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(x + 135f, y + 4f, (width - 185f) * Mathf.Clamp01(score), 12f), pixel);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + width - 45f, y, 45f, 20f), score.ToString("F2"));
            y += 22f;
        }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            return rich;
        }
    }
}
