using System.Collections.Generic;
using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>
    /// How easy the player is to see because of light: 1.0 with the flashlight on,
    /// 0.7 near a lit lamp, 0.3 in the dark. Lamps take part by carrying an IlluminationSource.
    /// </summary>
    public static class Illumination
    {
        public const float Flashlight = 1f;
        public const float NearLamp = 0.7f;
        public const float Dark = 0.3f;

        // Only the inner part of a lamp's range counts as "lit": the edge is barely brighter than dark.
        const float LitPartOfRange = 0.6f;

        static readonly List<Light> lamps = new List<Light>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => lamps.Clear();

        public static void Register(Light lamp)
        {
            if (!lamps.Contains(lamp)) lamps.Add(lamp);
        }

        public static void Unregister(Light lamp) => lamps.Remove(lamp);

        public static float LightFactor(Vector3 position, bool flashlightOn)
        {
            if (flashlightOn) return Flashlight;

            for (int i = 0; i < lamps.Count; i++)
            {
                Light lamp = lamps[i];
                if (lamp == null || !lamp.isActiveAndEnabled) continue;
                float reach = lamp.range * LitPartOfRange;
                if ((lamp.transform.position - position).sqrMagnitude <= reach * reach) return NearLamp;
            }
            return Dark;
        }
    }
}
