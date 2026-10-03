using UnityEngine;

namespace Hollowmere.World
{
    /// <summary>
    /// Marks an object that exists only so a zone scene can be opened and viewed on its own:
    /// its test camera, its moon, its global post-processing volume.
    /// When the zone is loaded as part of the streamed world, the World scene provides those
    /// instead, so this object switches itself off.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class StandalonePreviewOnly : MonoBehaviour
    {
        void Awake()
        {
            if (FindAnyObjectByType<ZoneStreamer>() != null)
                gameObject.SetActive(false);
        }
    }
}
