using UnityEngine;

namespace Hollowmere.World
{
    /// <summary>
    /// Makes a light unsteady: a flame that wavers, or an old bulb that dips and cuts out.
    /// Optionally dims a glowing bulb mesh in step with the light, so the source matches what it casts.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class LightFlicker : MonoBehaviour
    {
        public enum Style { Flame, FailingBulb }

        [SerializeField] Style style = Style.Flame;
        [Tooltip("How far the brightness can drop, as a fraction of full (0 = steady, 1 = can go fully dark).")]
        [Range(0f, 1f)] [SerializeField] float depth = 0.35f;
        [Tooltip("How fast it wavers.")]
        [SerializeField] float speed = 6f;
        [Tooltip("Failing bulb only: average seconds between cut-outs.")]
        [SerializeField] float cutOutEvery = 4f;
        [Tooltip("Optional glowing mesh (the bulb) to dim together with the light.")]
        [SerializeField] Renderer glow;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Light source;
        float fullIntensity;
        Color fullGlow;
        MaterialPropertyBlock block;
        float seed;
        float cutOutUntil;
        float nextCutOut;

        void Awake()
        {
            source = GetComponent<Light>();
            fullIntensity = source.intensity;
            seed = Random.value * 100f;
            if (glow != null)
            {
                fullGlow = glow.sharedMaterial.GetColor(BaseColor);
                block = new MaterialPropertyBlock();
            }
            nextCutOut = Time.time + Random.Range(0.5f, cutOutEvery * 2f);
        }

        void Update()
        {
            float level;
            if (style == Style.Flame)
            {
                // Two layers of smooth noise: a slow sway and a quick shiver.
                float slow = Mathf.PerlinNoise(seed, Time.time * speed * 0.25f);
                float quick = Mathf.PerlinNoise(seed + 37f, Time.time * speed);
                level = 1f - depth * (0.6f * slow + 0.4f * quick);
            }
            else
            {
                float hum = Mathf.PerlinNoise(seed, Time.time * speed);
                level = 1f - depth * 0.5f * hum;

                if (Time.time >= nextCutOut)
                {
                    cutOutUntil = Time.time + Random.Range(0.05f, 0.45f);
                    nextCutOut = Time.time + Random.Range(cutOutEvery * 0.4f, cutOutEvery * 1.8f);
                }
                if (Time.time < cutOutUntil)
                    level *= Random.value < 0.5f ? 0.05f : 0.4f;   // stutter while it is cutting out
            }

            source.intensity = fullIntensity * level;
            if (glow != null)
            {
                block.SetColor(BaseColor, fullGlow * level);
                glow.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            if (source != null) source.intensity = fullIntensity;
        }
    }
}
