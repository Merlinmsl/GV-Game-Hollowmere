using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Biscuit.Debugging
{
    /// <summary>
    /// TEMPORARY player state for testing Biscuit on the debug walker, which has none. Added by
    /// Hollowmere > Biscuit > Spawn Test Near Player. Test keys: K/L health, N/M ammo.
    /// Unlike DebugPlayerState it also takes the ammunition Biscuit brings.
    /// </summary>
    public sealed class BiscuitTestPlayer : MonoBehaviour, IPlayerState, IAmmoReceiver
    {
        Transform head;
        float health = 1f;
        float ammo = 1f;
        float thanksUntil;

        public Vector3 Position => transform.position;
        public Vector3 LookDirection => head != null ? head.forward : transform.forward;
        public float Health01 => health;
        public float Ammo01 => ammo;
        public WeaponKind HeldWeapon => WeaponKind.Pistol;
        public bool FlashlightOn => false;
        public bool IsCrouching => false;

        void Awake()
        {
            Camera view = GetComponentInChildren<Camera>();
            if (view != null) head = view.transform;
        }

        void OnEnable() => PlayerRegistry.Set(this);
        void OnDisable() => PlayerRegistry.Clear(this);

        public void ReceiveAmmo(float amount)
        {
            ammo = Mathf.Clamp01(ammo + amount);
            thanksUntil = Time.time + 2f;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.kKey.wasPressedThisFrame) health = Mathf.Clamp01(health - 0.2f);
            if (keyboard.lKey.wasPressedThisFrame) health = Mathf.Clamp01(health + 0.2f);
            if (keyboard.nKey.wasPressedThisFrame) ammo = Mathf.Clamp01(ammo - 0.2f);
            if (keyboard.mKey.wasPressedThisFrame) ammo = 1f;
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 100, 900, 40),
                "BISCUIT TEST PLAYER   K/L health | N/M ammo\n" +
                "Health " + health.ToString("F1") + "  Ammo " + ammo.ToString("F1") +
                (Time.time < thanksUntil ? "   + ammo from Biscuit" : ""));
        }
    }
}
