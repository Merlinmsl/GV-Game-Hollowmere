using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Hollowmere.AI.Senses;
using Hollowmere.Combat;

namespace Hollowmere.Player
{
    /// <summary>
    /// Handles player weapon inventory, switching (None / Pistol / IceGun),
    /// ballistics/raycasting for firearm damage, cryogenic cone for the Ice Gun,
    /// acoustic sensory noise emissions, and ammunition tracking.
    /// Synchronizes HeldWeapon and Ammo01 with PlayerController / IPlayerState.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class WeaponSystem : MonoBehaviour
    {
        [Header("Pistol Configuration")]
        [Tooltip("Damage dealt to IDamageable on body hit.")]
        [SerializeField] private float pistolDamage = 35f;

        [Tooltip("Damage multiplier for headshots.")]
        [SerializeField] private float headshotMultiplier = 3.0f;

        [Tooltip("Maximum effective ballistic raycast distance in metres.")]
        [SerializeField] private float pistolRange = 60f;

        [Tooltip("Minimum seconds between successive shots.")]
        [SerializeField] private float pistolFireRate = 0.25f;

        [Tooltip("Starting and maximum ammunition capacity for the pistol.")]
        [SerializeField] private int maxPistolAmmo = 36;
        [SerializeField] private int currentPistolAmmo = 18;

        [Header("Ice Gun Configuration")]
        [Tooltip("Duration in seconds targets remain frozen.")]
        [SerializeField] private float freezeSeconds = 3.5f;

        [Tooltip("Maximum cryogenic spray distance in metres.")]
        [SerializeField] private float iceRange = 15f;

        [Tooltip("Cone half-angle in degrees for the freeze spray.")]
        [SerializeField] private float iceConeAngle = 10f;

        [Tooltip("Minimum seconds between successive ice bursts.")]
        [SerializeField] private float iceFireRate = 0.6f;

        [Tooltip("Cryogenic canister capacity and consumption.")]
        [SerializeField] private float maxIceAmmo = 100f;
        [SerializeField] private float currentIceAmmo = 100f;
        [SerializeField] private float iceCostPerShot = 20f;

        [Header("Visuals & Viewmodels")]
        [Tooltip("Camera Transform used for raycasting aim.")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("Optional GameObject for the visual Pistol viewmodel.")]
        [SerializeField] private GameObject pistolModel;

        [Tooltip("Optional GameObject for the visual Ice Gun viewmodel.")]
        [SerializeField] private GameObject iceGunModel;

        [Header("Layers")]
        [Tooltip("LayerMask blocking weapon raycasts (walls, environment).")]
        [SerializeField] private LayerMask hitMask = ~0;

        public WeaponKind CurrentWeapon { get; private set; } = WeaponKind.None;

        public float Ammo01
        {
            get
            {
                switch (CurrentWeapon)
                {
                    case WeaponKind.Pistol:
                        return maxPistolAmmo > 0 ? (float)currentPistolAmmo / maxPistolAmmo : 0f;
                    case WeaponKind.IceGun:
                        return maxIceAmmo > 0f ? currentIceAmmo / maxIceAmmo : 0f;
                    default:
                        return 1.0f;
                }
            }
        }

        public event Action<WeaponKind> OnWeaponChanged;
        public event Action OnFired;
        /// <summary>The trigger was pulled with nothing to fire (empty pistol, ice gun without pressure).</summary>
        public event Action OnEmpty;
        /// <summary>Ammunition came in (a pickup or Biscuit's delivery).</summary>
        public event Action OnAmmoAdded;

        private PlayerController playerController;
        private float nextPistolFireTime;
        private float nextIceFireTime;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();

            if (cameraTransform == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraTransform = cam.transform;
            }
        }

        private void Start()
        {
            UpdateWeaponState();
        }

        private void Update()
        {
            ProcessWeaponSwitching();
            ProcessFiring();
        }

        /// <summary>
        /// Reads the Q key to cycle between equipped weapons.
        /// </summary>
        private void ProcessWeaponSwitching()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            WeaponKind targetWeapon = CurrentWeapon;

            // Q cycles: None -> Pistol -> IceGun -> None
            if (keyboard.qKey.wasPressedThisFrame)
            {
                switch (CurrentWeapon)
                {
                    case WeaponKind.None: targetWeapon = WeaponKind.Pistol; break;
                    case WeaponKind.Pistol: targetWeapon = WeaponKind.IceGun; break;
                    case WeaponKind.IceGun: targetWeapon = WeaponKind.None; break;
                }
            }
            // No 1/2/3 keys: the number row belongs to the debug teleport keys.

            if (targetWeapon != CurrentWeapon)
            {
                CurrentWeapon = targetWeapon;
                UpdateWeaponState();
            }
        }

        private void UpdateWeaponState()
        {
            if (pistolModel != null) pistolModel.SetActive(CurrentWeapon == WeaponKind.Pistol);
            if (iceGunModel != null) iceGunModel.SetActive(CurrentWeapon == WeaponKind.IceGun);

            if (playerController != null)
            {
                playerController.HeldWeapon = CurrentWeapon;
                playerController.Ammo01 = Ammo01;
            }

            OnWeaponChanged?.Invoke(CurrentWeapon);
        }

        /// <summary>
        /// Processes trigger pulls via Left Click.
        /// </summary>
        private void ProcessFiring()
        {
            var mouse = Mouse.current;
            if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                switch (CurrentWeapon)
                {
                    case WeaponKind.Pistol:
                        FirePistol();
                        break;
                    case WeaponKind.IceGun:
                        FireIceGun();
                        break;
                }
            }
        }

        /// <summary>
        /// Discharges the ballistic firearm (hitscan).
        /// Damages zombies and pushes physics props; passes harmlessly through ghosts per lore.
        /// </summary>
        private void FirePistol()
        {
            if (Time.time < nextPistolFireTime) return;
            if (currentPistolAmmo <= 0)
            {
                Debug.Log("[WeaponSystem] Pistol: *click* (Empty)");
                OnEmpty?.Invoke();
                return;
            }

            nextPistolFireTime = Time.time + pistolFireRate;
            currentPistolAmmo--;
            if (playerController != null) playerController.Ammo01 = Ammo01;

            // Acoustic gunshot noise
            NoiseSystem.Emit(transform.position, Loudness.Gunshot, NoiseKind.Gunshot, gameObject);
            OnFired?.Invoke();

            if (cameraTransform == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, pistolRange, hitMask, QueryTriggerInteraction.Ignore))
            {
                // Lore rule: bullets pass through ethereal entities (ghosts) without harm
                var iceableOnly = hit.collider.GetComponentInParent<IIceable>();
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (iceableOnly != null && damageable == null)
                {
                    Debug.Log("[WeaponSystem] Bullet passed harmlessly through the ethereal ghost!");
                    return;
                }

                // Apply damage to IDamageable (Zombies, Barricades, destructibles)
                if (damageable != null)
                {
                    // Check if hit upper part of collider for headshot
                    bool isHeadshot = hit.point.y > (hit.collider.bounds.center.y + hit.collider.bounds.extents.y * 0.4f);
                    float finalDamage = isHeadshot ? pistolDamage * headshotMultiplier : pistolDamage;

                    damageable.TakeDamage(finalDamage, hit.point, ray.direction, gameObject);
                    Debug.Log($"[WeaponSystem] Pistol hit {hit.collider.name} ({(isHeadshot ? "HEADSHOT!" : "Body")}) for {finalDamage:F0} damage.");
                }
                else if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                {
                    // Physical impulse on loose props, crates, bottles
                    hit.rigidbody.AddForceAtPosition(ray.direction * 18.0f, hit.point, ForceMode.Impulse);
                }
            }
        }

        /// <summary>
        /// Discharges a cryogenic spray from the Ice Gun.
        /// Freezes ghosts (forcing them to freeze and flee on thaw) and freezes/slows zombies.
        /// </summary>
        private void FireIceGun()
        {
            if (Time.time < nextIceFireTime) return;
            if (currentIceAmmo < iceCostPerShot)
            {
                Debug.Log("[WeaponSystem] Ice Gun: *hiss* (No cryogenic pressure)");
                OnEmpty?.Invoke();
                return;
            }

            nextIceFireTime = Time.time + iceFireRate;
            currentIceAmmo = Mathf.Max(0f, currentIceAmmo - iceCostPerShot);
            if (playerController != null) playerController.Ammo01 = Ammo01;

            // Acoustic ice gun noise (alerts ghosts in the vicinity)
            NoiseSystem.Emit(transform.position, Loudness.IceGun, NoiseKind.IceGun, gameObject);
            OnFired?.Invoke();

            if (cameraTransform == null) return;

            Vector3 eyePos = cameraTransform.position;
            Vector3 aimDir = cameraTransform.forward;
            float cosThreshold = Mathf.Cos(iceConeAngle * Mathf.Deg2Rad);
            bool hitAny = false;

            // 1. Direct crosshair raycast (QueryTriggerInteraction.Collide so ghost trigger colliders are hit)
            Ray ray = new Ray(eyePos, aimDir);
            if (Physics.Raycast(ray, out RaycastHit directHit, iceRange, hitMask, QueryTriggerInteraction.Collide))
            {
                if (!directHit.transform.IsChildOf(transform))
                {
                    var directIceable = directHit.collider.GetComponentInParent<IIceable>();
                    if (directIceable != null)
                    {
                        directIceable.ReceiveIce(freezeSeconds);
                        hitAny = true;
                        Debug.Log($"<color=cyan><b>[WeaponSystem]</b> Direct hit froze {directHit.collider.name} for {freezeSeconds} seconds!</color>");
                    }
                }
            }

            // 2. Cone spray check (QueryTriggerInteraction.Collide so ghost trigger colliders are included)
            Collider[] colliders = Physics.OverlapSphere(eyePos, iceRange, hitMask, QueryTriggerInteraction.Collide);

            foreach (var col in colliders)
            {
                if (col.transform.IsChildOf(transform)) continue;

                var iceable = col.GetComponentInParent<IIceable>();
                if (iceable != null)
                {
                    Vector3 toTarget = col.bounds.center - eyePos;
                    float dist = toTarget.magnitude;
                    if (dist > iceRange) continue;

                    Vector3 dir = toTarget / dist;
                    if (Vector3.Dot(aimDir, dir) < cosThreshold) continue;

                    // Line of sight check: ensure solid walls do not block the spray
                    if (Physics.Raycast(eyePos, dir, dist - 0.2f, hitMask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    iceable.ReceiveIce(freezeSeconds);
                    hitAny = true;
                    Debug.Log($"<color=cyan><b>[WeaponSystem]</b> Froze {col.name} for {freezeSeconds} seconds!</color>");
                }
            }

            if (!hitAny)
            {
                Debug.Log("[WeaponSystem] Ice spray missed.");
            }
        }

        /// <summary>
        /// Replenishes ammunition pool (called by Biscuit or ammo pickup items).
        /// </summary>
        public void AddAmmo(float fraction)
        {
            int pistolBefore = currentPistolAmmo;
            float iceBefore = currentIceAmmo;
            currentPistolAmmo = Mathf.Clamp(currentPistolAmmo + Mathf.RoundToInt(fraction * maxPistolAmmo), 0, maxPistolAmmo);
            currentIceAmmo = Mathf.Clamp(currentIceAmmo + fraction * maxIceAmmo, 0f, maxIceAmmo);

            if (playerController != null)
            {
                playerController.Ammo01 = Ammo01;
            }

            if (currentPistolAmmo > pistolBefore || currentIceAmmo > iceBefore)
            {
                OnAmmoAdded?.Invoke();
            }
        }

        // The crosshair is drawn by Crosshair and the weapon and ammo by PlayerHud.
    }
}
