using UnityEngine;
using Hollowmere.AI.Debugging;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Debug overlay and Gizmo visualizer for Runner Zombie mental state.
    /// Toggleable via F2 key (guarded by AiDebug.Enabled).
    /// Displays active BT node name, confidence bar, acoustic details, and belief Gizmos.
    /// </summary>
    [RequireComponent(typeof(ZombieBody))]
    public class ZombieDebugView : MonoBehaviour
    {
        private ZombieBody body;

        private void Awake()
        {
            body = GetComponent<ZombieBody>();
        }

        private void Update()
        {
            AiDebug.CheckToggleInput();
        }

        private void OnGUI()
        {
            if (!AiDebug.Enabled || body == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 worldPos = transform.position + Vector3.up * 2.2f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z <= 0f) return;

            float screenY = Screen.height - screenPos.y;
            float screenX = screenPos.x - 60f;

            string stateName = body.CurrentStateName;
            ZombieBelief belief = body.Memory != null ? body.Memory.GetBelief() : ZombieBelief.Empty;

            // Draw active BT node name
            GUI.color = Color.white;
            GUI.Label(new Rect(screenX, screenY - 20, 140, 20), $"[{stateName}]");

            // Draw confidence bar
            float barWidth = 100f;
            float barHeight = 8f;
            Rect bgRect = new Rect(screenX, screenY, barWidth, barHeight);
            Rect fillRect = new Rect(screenX, screenY, barWidth * Mathf.Clamp01(belief.Confidence), barHeight);

            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

            Color barColor = Color.Lerp(Color.yellow, Color.red, belief.Confidence);
            GUI.color = barColor;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        private void OnDrawGizmos()
        {
            if (!AiDebug.Enabled || body == null) return;

            if (body.Memory != null && body.Memory.HasBelief)
            {
                ZombieBelief belief = body.Memory.GetBelief();
                Gizmos.color = Color.Lerp(new Color(1f, 0.92f, 0.016f, 0.6f), new Color(1f, 0f, 0f, 0.9f), belief.Confidence);
                Gizmos.DrawWireSphere(belief.Position, 0.5f);
                Gizmos.DrawSphere(belief.Position, 0.25f);
                Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, belief.Position);
            }
        }
    }
}
