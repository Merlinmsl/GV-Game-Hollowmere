using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Biscuit.Debugging
{
    /// <summary>
    /// TEMPORARY stand-in for a zombie, to test Biscuit before the real zombie is merged. Stands
    /// still until it hears a noise, then walks to where the noise was, groaning every few seconds.
    /// It carries a ScentSource, so Biscuit can smell it. Not part of the game.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(ScentSource))]
    public sealed class TestDead : MonoBehaviour, INoiseListener
    {
        [SerializeField] float hearingAcuity = 1f;
        [SerializeField] float groanInterval = 6f;
        [Tooltip("Goes still again this long after the last noise it heard, once it has arrived.")]
        [SerializeField] float giveUpAfter = 8f;

        NavMeshAgent agent;
        bool awake;
        float nextGroanTime;
        float giveUpTime;

        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => hearingAcuity;

        void Awake() => agent = GetComponent<NavMeshAgent>();
        void OnEnable() => NoiseSystem.Register(this);
        void OnDisable() => NoiseSystem.Unregister(this);

        public void OnHeard(in Noise noise, float pathDistance)
        {
            if (noise.Source == gameObject || noise.Kind == NoiseKind.Groan) return;

            if (agent.isOnNavMesh) agent.SetDestination(noise.Position);
            if (!awake) nextGroanTime = Time.time;
            awake = true;
            giveUpTime = Time.time + giveUpAfter;
        }

        void Update()
        {
            if (!awake) return;

            if (Time.time >= nextGroanTime)
            {
                NoiseSystem.Emit(transform.position, Loudness.Groan, NoiseKind.Groan, gameObject);
                nextGroanTime = Time.time + groanInterval;
            }

            if (Time.time >= giveUpTime && !agent.pathPending && agent.remainingDistance < 0.5f) awake = false;
        }
    }
}
