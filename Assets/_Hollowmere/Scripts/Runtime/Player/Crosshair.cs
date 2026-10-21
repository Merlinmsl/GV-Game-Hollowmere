using UnityEngine;
using Hollowmere.AI.Senses;
using Hollowmere.World;

namespace Hollowmere.Player
{
    /// <summary>
    /// The crosshair: four short ticks round a dot in the middle of the screen. The ticks spread when a
    /// shot goes off and while the player moves, then settle back, and take the colour of the weapon in
    /// hand. With no weapon only the dot shows. Drawn with OnGUI like PlayerHud and sized from the screen
    /// height; hidden while the map is open, the player is dead or the cursor is free.
    /// </summary>
    [RequireComponent(typeof(WeaponSystem))]
    public sealed class Crosshair : MonoBehaviour
    {
        [Tooltip("Tick length as a share of the screen height.")]
        [SerializeField] float tickLength = 0.011f;
        [SerializeField] float thickness = 0.0022f;
        [Tooltip("Gap between the centre and each tick, as a share of the screen height.")]
        [SerializeField] float gap = 0.007f;
        [Tooltip("Extra gap right after a shot.")]
        [SerializeField] float fireSpread = 0.018f;
        [Tooltip("Extra gap at a full sprint.")]
        [SerializeField] float moveSpread = 0.006f;
        [Tooltip("How fast the spread closes again (per second).")]
        [SerializeField] float settleRate = 7f;
        [SerializeField] Color pistolColor = new Color(0.95f, 0.92f, 0.85f, 0.85f);
        [SerializeField] Color iceGunColor = new Color(0.7f, 0.92f, 1f, 0.85f);
        [SerializeField] Color unarmedColor = new Color(0.9f, 0.9f, 0.9f, 0.45f);

        WeaponSystem weapons;
        PlayerController player;
        PlayerHealth health;
        MapScreen map;
        Texture2D white;
        float shotSpread;

        /// <summary>Spread from shots, 1 right after one and easing back to 0.</summary>
        public float ShotSpread => shotSpread;

        void Awake()
        {
            weapons = GetComponent<WeaponSystem>();
            player = GetComponent<PlayerController>();
            health = GetComponent<PlayerHealth>();
            white = Texture2D.whiteTexture;
        }

        void Start() => map = FindFirstObjectByType<MapScreen>();

        void OnEnable() => weapons.OnFired += Kick;
        void OnDisable() => weapons.OnFired -= Kick;

        void Kick() => shotSpread = Mathf.Min(shotSpread + 1f, 1.6f);

        void Update() => shotSpread *= Mathf.Exp(-settleRate * Time.deltaTime);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (health != null && health.IsDead) return;
            if (map != null && map.IsOpen) return;

            float unit = Screen.height;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float t = Mathf.Max(2f, Mathf.Round(unit * thickness));
            WeaponKind held = weapons.CurrentWeapon;

            if (held == WeaponKind.None)
            {
                Mark(cx - t * 0.5f, cy - t * 0.5f, t, t, unarmedColor);
                return;
            }

            float moving = player != null ? Mathf.Clamp01(player.CurrentSpeed / 5.6f) : 0f;
            float g = unit * (gap + fireSpread * shotSpread + moveSpread * moving);
            float len = unit * tickLength;
            Color colour = held == WeaponKind.IceGun ? iceGunColor : pistolColor;

            Mark(cx - t * 0.5f, cy - t * 0.5f, t, t, colour);           // dot
            Mark(cx - g - len, cy - t * 0.5f, len, t, colour);          // left
            Mark(cx + g, cy - t * 0.5f, len, t, colour);                // right
            Mark(cx - t * 0.5f, cy - g - len, t, len, colour);          // top
            Mark(cx - t * 0.5f, cy + g, t, len, colour);                // bottom
        }

        // A bar with a thin dark outline, so it reads on bright fog and dark walls alike.
        void Mark(float x, float y, float w, float h, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, colour.a * 0.6f);
            GUI.DrawTexture(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), white);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = previous;
        }
    }
}
