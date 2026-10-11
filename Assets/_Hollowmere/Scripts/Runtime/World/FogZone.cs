using UnityEngine;

namespace Hollowmere.World
{
    /// <summary>
    /// Changes the scene fog while the player is inside this box, and blends back when they leave.
    /// The outdoor fog is a pale night mist; seen underground it makes distant dark walls glow,
    /// so enclosed places need their own darker fog.
    /// The player is found by tag, so any player prefab works.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FogZone : MonoBehaviour
    {
        [SerializeField] Color fogColor = new Color(0.02f, 0.025f, 0.03f);
        [SerializeField] float fogDensity = 0.035f;
        [Tooltip("Seconds to blend between the outdoor fog and this one.")]
        [SerializeField] float blendSeconds = 1.5f;
        [SerializeField] string playerTag = "Player";

        // The fog the World scene started with. Shared, so several zones blend from the same base.
        static bool hasOutdoorFog;
        static Color outdoorColor;
        static float outdoorDensity;

        BoxCollider box;
        Transform player;
        float weight;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            if (!hasOutdoorFog)
            {
                outdoorColor = RenderSettings.fogColor;
                outdoorDensity = RenderSettings.fogDensity;
                hasOutdoorFog = true;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hasOutdoorFog = false;
        }

        void Update()
        {
            if (player == null)
            {
                var found = GameObject.FindGameObjectWithTag(playerTag);
                if (found == null) return;
                player = found.transform;
            }

            bool inside = box.bounds.Contains(player.position);
            float step = blendSeconds > 0f ? Time.deltaTime / blendSeconds : 1f;
            float before = weight;
            weight = Mathf.MoveTowards(weight, inside ? 1f : 0f, step);
            if (weight <= 0f && before <= 0f) return;   // not involved: leave the fog to whoever is

            float t = Mathf.SmoothStep(0f, 1f, weight);
            RenderSettings.fogColor = Color.Lerp(outdoorColor, fogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(outdoorDensity, fogDensity, t);
        }

        void OnDisable()
        {
            // The zone is being unloaded while the player is still inside it: hand the fog back.
            if (weight > 0f && hasOutdoorFog)
            {
                RenderSettings.fogColor = outdoorColor;
                RenderSettings.fogDensity = outdoorDensity;
            }
            weight = 0f;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var b = GetComponent<BoxCollider>();
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.5f, 0.6f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
#endif
    }
}
