using UnityEngine;

namespace Hollowmere.AI.Debugging
{
    /// <summary>
    /// TEMPORARY: draws a readable label above an agent's head: large bold text on a dark box,
    /// so it stands out against the fog. Call only from OnGUI.
    /// </summary>
    public static class DebugLabel
    {
        const int FontSize = 18;
        const float MaxDistance = 40f;    // labels further away than this are not drawn

        static GUIStyle style;

        public static void Above(Transform target, float height, string text)
        {
            Camera view = Camera.main;
            if (view == null) return;

            Vector3 screen = view.WorldToScreenPoint(target.position + Vector3.up * height);
            if (screen.z < 0f || screen.z > MaxDistance) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = FontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false
                };
                style.normal.textColor = Color.white;
            }

            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y), text, style);
        }
    }
}
