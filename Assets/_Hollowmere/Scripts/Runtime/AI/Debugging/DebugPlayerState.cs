using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Debugging
{
    /// <summary>
    /// TEMPORARY player state for the debug walker. Test keys:
    /// F flashlight, C crouch, Q switch weapon, K lose health, L heal, N use ammo, M refill ammo.
    /// </summary>
    public sealed class DebugPlayerState : MonoBehaviour, IPlayerState
    {
        Transform head;
        float health = 1f;
        float ammo = 1f;
        WeaponKind weapon = WeaponKind.Pistol;
        bool flashlight;
        bool crouching;

        public Vector3 Position => transform.position;
        public Vector3 LookDirection => head != null ? head.forward : transform.forward;
        public float Health01 => health;
        public float Ammo01 => ammo;
        public WeaponKind HeldWeapon => weapon;
        public bool FlashlightOn => flashlight;
        public bool IsCrouching => crouching;

        void Awake()
        {
            Camera view = GetComponentInChildren<Camera>();
            if (view != null) head = view.transform;
        }

        void OnEnable() => PlayerRegistry.Set(this);
        void OnDisable() => PlayerRegistry.Clear(this);

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.fKey.wasPressedThisFrame) flashlight = !flashlight;
            if (keyboard.cKey.wasPressedThisFrame) crouching = !crouching;
            if (keyboard.qKey.wasPressedThisFrame) weapon = (WeaponKind)(((int)weapon + 1) % 3);
            if (keyboard.kKey.wasPressedThisFrame) health = Mathf.Clamp01(health - 0.2f);
            if (keyboard.lKey.wasPressedThisFrame) health = Mathf.Clamp01(health + 0.2f);
            if (keyboard.nKey.wasPressedThisFrame) ammo = Mathf.Clamp01(ammo - 0.2f);
            if (keyboard.mKey.wasPressedThisFrame) ammo = 1f;
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 100, 900, 40),
                "PLAYER STATE   F light | C crouch | Q weapon | K/L health | N/M ammo\n" +
                "Health " + health.ToString("F1") + "  Ammo " + ammo.ToString("F1") + "  Weapon " + weapon +
                "  Flashlight " + (flashlight ? "on" : "off") + "  " + (crouching ? "Crouching" : "Standing"));
        }
    }
}





