using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Debugging
{
    /// <summary>TEMPORARY test listener. Flashes a red light when it hears a noise.</summary>
    public sealed class NoiseProbe : MonoBehaviour, INoiseListener
    {
        [SerializeField] float hearingAcuity = 1f;

        Light flash;
        float flashUntil;

        public Vector3 EarPosition => transform.position;
        public float HearingAcuity => hearingAcuity;

        void Awake()
        {
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = Color.red;
            flash.range = 8f;
            flash.intensity = 60f;
            flash.enabled = false;
        }

        void OnEnable() => NoiseSystem.Register(this);
        void OnDisable() => NoiseSystem.Unregister(this);

        public void OnHeard(in Noise noise, float pathDistance)
        {
            flash.enabled = true;
            flashUntil = Time.time + 1.5f;
            Debug.Log(name + " heard " + noise.Kind + " from " + pathDistance.ToString("F1") + " m away by path.");
        }

        void Update()
        {
            if (flash.enabled && Time.time > flashUntil) flash.enabled = false;
        }
    }
}









