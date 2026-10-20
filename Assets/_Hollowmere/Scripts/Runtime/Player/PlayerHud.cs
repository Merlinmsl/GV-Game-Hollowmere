using UnityEngine;
using Hollowmere.AI.Senses;
using Hollowmere.World;

namespace Hollowmere.Player
{
    /// <summary>
    /// Bottom-right HUD: the player's health bar and, when a weapon is held, its name and ammo bar.
    /// Drawn with OnGUI like the damage flash and death screen in PlayerHealth, and sized from the
    /// screen height so it looks the same at any resolution. Hidden while the map is open or the
    /// player is dead.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHud : MonoBehaviour
    {
        [Tooltip("Bar width as a share of the screen height.")]
        [SerializeField] float barWidth = 0.32f;
        [Tooltip("Gap from the screen edges as a share of the screen height.")]
        [SerializeField] float margin = 0.03f;
        [SerializeField] Color healthColor = new Color(0.72f, 0.1f, 0.1f, 0.95f);
        [SerializeField] Color lowHealthColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField] Color pistolColor = new Color(0.85f, 0.75f, 0.45f, 0.95f);
        [SerializeField] Color iceColor = new Color(0.45f, 0.8f, 1f, 0.95f);

        PlayerHealth health;
        WeaponSystem weapons;
        MapScreen map;
        Texture2D white;
        GUIStyle labelStyle;
        float shownHealth01 = 1f;      // eases toward the real value so a hit reads as a drop

        void Awake()
        {
            health = GetComponent<PlayerHealth>();
            weapons = GetComponent<WeaponSystem>();
            white = Texture2D.whiteTexture;
        }

        void Start() => map = FindFirstObjectByType<MapScreen>();

        void Update()
        {
            shownHealth01 = Mathf.MoveTowards(shownHealth01, health.Health01, Time.unscaledDeltaTime * 1.5f);
        }

        void OnGUI()
        {
            if (health == null || health.IsDead) return;
            if (map != null && map.IsOpen) return;

            float unit = Screen.height;
            float w = unit * barWidth, h = unit * 0.022f, gap = unit * margin;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight };
            labelStyle.fontSize = Mathf.RoundToInt(unit * 0.022f);

            float x = Screen.width - gap - w;
            float y = Screen.height - gap - h;

            // Health: a dark frame, the recent loss in pale red, then the real value.
            bool low = health.Health01 <= 0.3f;
            Color bar = low ? Color.Lerp(healthColor, lowHealthColor, Mathf.PingPong(Time.unscaledTime * 2f, 1f)) : healthColor;
            DrawBar(new Rect(x, y, w, h), health.Health01, shownHealth01, bar);
            Label(new Rect(x, y - h * 1.6f, w, h * 1.5f), $"HEALTH  {Mathf.CeilToInt(health.CurrentHealth)}");

            // Weapon and ammo above it.
            if (weapons == null || weapons.CurrentWeapon == WeaponKind.None) return;
            bool ice = weapons.CurrentWeapon == WeaponKind.IceGun;
            float ay = y - h * 3.4f;
            DrawBar(new Rect(x + w * 0.4f, ay, w * 0.6f, h * 0.6f), weapons.Ammo01, weapons.Ammo01, ice ? iceColor : pistolColor);
            Label(new Rect(x, ay - h * 1.6f, w, h * 1.5f), ice ? "ICE GUN" : "PISTOL");
        }

        void DrawBar(Rect r, float value01, float trail01, Color fill)
        {
            Color prev = GUI.color;
            float border = Mathf.Max(1f, r.height * 0.12f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(r.x - border, r.y - border, r.width + border * 2f, r.height + border * 2f), white);
            if (trail01 > value01)
            {
                GUI.color = new Color(1f, 0.85f, 0.8f, 0.5f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * trail01, r.height), white);
            }
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value01), r.height), white);
            GUI.color = prev;
        }

        void Label(Rect r, string text)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, labelStyle);
            GUI.color = new Color(0.9f, 0.88f, 0.82f, 1f);
            GUI.Label(r, text, labelStyle);
            GUI.color = prev;
        }
    }
}
