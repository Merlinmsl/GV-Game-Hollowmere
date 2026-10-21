using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.World
{
    /// <summary>
    /// The full-screen map. M opens and closes it (Esc also closes it).
    /// While it is open the game is paused: time stops, sound pauses and the mouse is freed.
    /// It shows the map of the zone the player is in, an arrow for the player, and an icon for
    /// every item still to collect in that zone, plus the zone's exit.
    /// Lives in the World scene, so it works in every zone.
    /// </summary>
    public sealed class MapScreen : MonoBehaviour
    {
        [SerializeField] ZoneMapSet maps;
        [Tooltip("Space kept free around the map, in pixels.")]
        [SerializeField] float margin = 40f;
        [Tooltip("Size of an item icon on screen, in pixels.")]
        [SerializeField] float iconSize = 30f;

        static readonly string[] KindNames = { "Weapon", "Ammo", "Key", "Evidence", "Ice canister", "Battery", "Throwable", "Exit" };
        static readonly string[] KindLetters = { "W", "A", "K", "E", "I", "B", "T", "→" };
        static readonly Color[] KindColors =
        {
            new Color(0.85f, 0.25f, 0.2f),   // weapon: red
            new Color(0.95f, 0.6f, 0.15f),   // ammo: orange
            new Color(0.95f, 0.82f, 0.25f),  // key: gold
            new Color(0.92f, 0.92f, 0.88f),  // evidence: paper white
            new Color(0.35f, 0.8f, 0.95f),   // ice canister: ice blue
            new Color(0.55f, 0.85f, 0.35f),  // battery: green
            new Color(0.65f, 0.6f, 0.55f),   // throwable bottle: grey brown
            new Color(0.3f, 0.65f, 1f),      // exit: blue
        };

        public bool IsOpen { get; private set; }

        float timeScaleBefore = 1f;
        CursorLockMode lockBefore;
        bool cursorVisibleBefore;
        Texture2D disc, arrow, pixel;
        GUIStyle letterStyle, titleStyle, textStyle, tipStyle;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.mKey.wasPressedThisFrame) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        void LateUpdate()
        {
            // A click in the game view re-locks the mouse in the player scripts; keep it free while the map is up.
            if (IsOpen && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void OnDisable()
        {
            if (IsOpen) Close();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;          // freezes physics, agents and the AI scheduler
            AudioListener.pause = true;
            lockBefore = Cursor.lockState;
            cursorVisibleBefore = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
            AudioListener.pause = false;
            Cursor.lockState = lockBefore == CursorLockMode.None ? CursorLockMode.Locked : lockBefore;
            Cursor.visible = cursorVisibleBefore;
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = -1000;            // on top of every other on-screen text
            EnsureAssets();

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.88f);
            GUI.DrawTexture(screen, pixel);
            GUI.color = Color.white;

            IPlayerState player = PlayerRegistry.Current;
            ZoneMapSet.ZoneMap zone = (maps != null && player != null) ? maps.Find(player.Position) : null;
            if (zone == null || zone.image == null)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, 40f), "No map for this place.   M  close", titleStyle);
                return;
            }

            // Fit the picture inside the screen, keeping its shape.
            float top = margin + 40f, bottom = margin + 10f;
            float availableW = Screen.width - margin * 2f - 230f;   // room for the legend on the right
            float availableH = Screen.height - top - bottom;
            float aspect = (float)zone.image.width / zone.image.height;
            float w = Mathf.Min(availableW, availableH * aspect);
            float h = w / aspect;
            var mapRect = new Rect(margin + (availableW - w) * 0.5f, top + (availableH - h) * 0.5f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(mapRect.x - 3f, mapRect.y - 3f, mapRect.width + 6f, mapRect.height + 6f), pixel);
            GUI.color = Color.white;
            GUI.DrawTexture(mapRect, zone.image, ScaleMode.StretchToFill);

            GUI.Label(new Rect(margin, margin - 4f, 900f, 40f), zone.displayName.ToUpperInvariant(), titleStyle);

            // Items in this zone that are still to collect.
            int[] counts = new int[KindNames.Length];
            string tooltip = null;
            Vector2 mouse = Event.current.mousePosition;
            foreach (MapMarker marker in MapMarker.Active)
            {
                if (marker == null || !marker.ShowOnMap || marker.gameObject.scene.name != zone.sceneName) continue;
                Vector2 p = ToScreen(marker.transform.position, zone, mapRect);
                if (!mapRect.Contains(p)) continue;
                int k = (int)marker.Type;
                counts[k]++;
                var r = new Rect(p.x - iconSize * 0.5f, p.y - iconSize * 0.5f, iconSize, iconSize);
                DrawIcon(r, k);
                if (r.Contains(mouse)) tooltip = marker.Label;
            }

            // The player: an arrow pointing the way they face.
            Vector2 me = ToScreen(player.Position, zone, mapRect);
            Vector3 look = player.LookDirection;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;   // 0 = north (up), clockwise
            float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(me.x - 22f * pulse, me.y - 22f * pulse, 44f * pulse, 44f * pulse), disc);
            Matrix4x4 before = GUI.matrix;
            GUIUtility.RotateAroundPivot(yaw, me);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(me.x - 16f, me.y - 18f, 32f, 36f), arrow);
            GUI.matrix = before;

            DrawLegend(new Rect(mapRect.xMax + 30f, mapRect.y, 210f, mapRect.height), counts);

            if (tooltip != null)
            {
                Vector2 size = tipStyle.CalcSize(new GUIContent(tooltip));
                GUI.Box(new Rect(mouse.x + 16f, mouse.y + 10f, size.x + 16f, size.y + 8f), tooltip, tipStyle);
            }

            GUI.Label(new Rect(margin, Screen.height - margin - 4f, 900f, 30f), "M or Esc  close the map      Game paused", textStyle);
        }

        static Vector2 ToScreen(Vector3 world, ZoneMapSet.ZoneMap zone, Rect mapRect)
        {
            float u = (world.x - zone.worldRect.xMin) / zone.worldRect.width;
            float v = (world.z - zone.worldRect.yMin) / zone.worldRect.height;
            return new Vector2(mapRect.x + u * mapRect.width, mapRect.y + (1f - v) * mapRect.height);
        }

        void DrawIcon(Rect r, int kind)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), disc);
            GUI.color = KindColors[kind];
            GUI.DrawTexture(r, disc);
            GUI.color = Color.white;
            GUI.Label(r, KindLetters[kind], letterStyle);
        }

        void DrawLegend(Rect area, int[] counts)
        {
            float y = area.y;
            GUI.Label(new Rect(area.x, y, area.width, 28f), "TO FIND HERE", textStyle);
            y += 34f;
            for (int k = 0; k < KindNames.Length; k++)
            {
                if (counts[k] == 0) continue;
                DrawIcon(new Rect(area.x, y, 26f, 26f), k);
                string text = k == (int)MapMarker.Kind.Exit ? KindNames[k] : KindNames[k] + "   " + counts[k];
                GUI.Label(new Rect(area.x + 36f, y + 1f, area.width - 36f, 26f), text, textStyle);
                y += 34f;
            }
            y += 10f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(area.x + 5f, y, 16f, 18f), arrow);
            GUI.Label(new Rect(area.x + 36f, y, area.width - 36f, 26f), "You", textStyle);
        }

        // ---------------------------------------------------------------- generated icons

        void EnsureAssets()
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            if (disc == null) disc = MakeDisc(64);
            if (arrow == null) arrow = MakeArrow(64);
            if (letterStyle == null)
            {
                letterStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
                letterStyle.normal.textColor = new Color(0.08f, 0.08f, 0.08f);
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = new Color(0.92f, 0.9f, 0.84f);
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 17 };
                textStyle.normal.textColor = new Color(0.9f, 0.9f, 0.86f);
                tipStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
                tipStyle.normal.textColor = Color.white;
            }
        }

        static Texture2D MakeDisc(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeArrow(int size)
        {
            // A white arrowhead pointing up, with a dark outline so it reads on any ground.
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 tip = new Vector2(size * 0.5f, size - 2f), left = new Vector2(4f, 4f), right = new Vector2(size - 4f, 4f), notch = new Vector2(size * 0.5f, size * 0.3f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right);
                    bool edge = !inside && (Near(p, tip, left) || Near(p, tip, right) || Near(p, left, notch) || Near(p, notch, right));
                    Color colour = inside ? new Color(1f, 0.95f, 0.75f, 1f) : edge ? new Color(0f, 0f, 0f, 0.9f) : Color.clear;
                    t.SetPixel(x, y, colour);
                }
            t.Apply();
            return t;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        static bool Near(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t) < 3f;
        }
    }
}
