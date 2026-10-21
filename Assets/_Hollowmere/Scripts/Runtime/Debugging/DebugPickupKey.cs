using Hollowmere.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.Debugging
{
    /// <summary>
    /// TEMPORARY: X picks up the nearest map item within reach, so the map can be tested before the
    /// real pickups exist. The real pickup code does the same thing: it calls MapMarker.Collect()
    /// on the item and then hides it.
    /// </summary>
    public sealed class DebugPickupKey : MonoBehaviour
    {
        [SerializeField] float reach = 2.5f;

        string message = "";
        float messageUntil;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.xKey.wasPressedThisFrame) return;

            MapMarker nearest = null;
            float best = reach * reach;
            foreach (MapMarker marker in MapMarker.Active)
            {
                if (marker == null || marker.Type == MapMarker.Kind.Exit || !marker.ShowOnMap) continue;
                float d = (marker.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = marker; }
            }

            if (nearest == null)
            {
                Show("Nothing to pick up within " + reach + " m");
                return;
            }
            nearest.Collect();
            nearest.gameObject.SetActive(false);
            Show("Picked up: " + nearest.Label);
        }

        void Show(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 2.5f;
        }

        void OnGUI()
        {
            if (Time.unscaledTime > messageUntil) return;
            GUI.Label(new Rect(12, Screen.height - 40, 600, 30), message);
        }
    }
}
