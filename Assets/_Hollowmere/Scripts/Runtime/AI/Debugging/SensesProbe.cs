using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Debugging
{
    /// <summary>TEMPORARY test agent: hears, sees and remembers, and shows it above its head.</summary>
    public sealed class SensesProbe : MonoBehaviour, INoiseListener
    {
        [SerializeField] SightSensor sight = new SightSensor();
        [Tooltip("Seconds between thoughts. Agents do not think every frame.")]
        [SerializeField] float thinkInterval = 0.25f;

        readonly AgentMemory memory = new AgentMemory();
        float lastThinkTime;

        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => 1f;

        void OnEnable()
        {
            NoiseSystem.Register(this);
            lastThinkTime = Time.time;
        }

        void OnDisable() => NoiseSystem.Unregister(this);

        public void OnHeard(in Noise noise, float pathDistance)
        {
            if (noise.Source == gameObject) return;
            memory.Remember(noise.Position, AgentMemory.HeardConfidence, BeliefSource.Heard, Time.time);
        }

        void Update()
        {
            if (Time.time - lastThinkTime < thinkInterval) return;
            float deltaTime = Time.time - lastThinkTime;
            lastThinkTime = Time.time;

            IPlayerState player = PlayerRegistry.Current;
            if (sight.Tick(transform, player, deltaTime))
                memory.Remember(player.Position, AgentMemory.SeenConfidence, BeliefSource.Seen, Time.time);
        }

        void OnGUI()
        {
            Camera view = Camera.main;
            if (view == null) return;
            Vector3 screen = view.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
            if (screen.z < 0f) return;

            string belief = memory.HasBelief(Time.time)
                ? memory.Current.Source + "  " + memory.ConfidenceAt(Time.time).ToString("F2")
                  + "  " + Vector3.Distance(memory.Current.Position, transform.position).ToString("F0") + " m away"
                : "no idea where you are";
            GUI.Label(new Rect(screen.x - 90f, Screen.height - screen.y, 260f, 60f),
                name + "\nSight " + sight.Meter.ToString("F2") + (sight.InView ? "  (in view)" : "") + "\n" + belief);
        }
    }
}
