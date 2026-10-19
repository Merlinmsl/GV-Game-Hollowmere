using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>Marks a lamp as one that makes the player easier to see when standing near it.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class IlluminationSource : MonoBehaviour
    {
        Light lamp;

        void Awake() => lamp = GetComponent<Light>();
        void OnEnable() => Illumination.Register(lamp);
        void OnDisable() => Illumination.Unregister(lamp);
    }
}
