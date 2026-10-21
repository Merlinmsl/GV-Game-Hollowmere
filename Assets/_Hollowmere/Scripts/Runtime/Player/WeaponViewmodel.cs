using UnityEngine;
using UnityEngine.InputSystem;
using Hollowmere.AI.Senses;

namespace Hollowmere.Player
{
    /// <summary>
    /// The first-person view of one weapon: the gun and the gloved arms holding it, under the Head
    /// camera. WeaponSystem turns it on and off through its Pistol Model / Ice Gun Model slots; this adds
    /// what the player feels: the draw, sway with the mouse, bob while walking, recoil, the slide and
    /// muzzle flash or the ice spray, the sounds, and a reload whenever ammo comes in (a pickup or
    /// Biscuit's delivery). Everything moves procedurally (a spring and eased curves), so there is no
    /// Animator to set up. Hollowmere > Weapons > Set Up Viewmodels builds the prefabs.
    /// </summary>
    public sealed class WeaponViewmodel : MonoBehaviour
    {
        [SerializeField] WeaponKind kind = WeaponKind.Pistol;
        [Tooltip("The child that moves. This object keeps the rest pose under the camera.")]
        [SerializeField] Transform pose;

        [Header("Moving parts")]
        [Tooltip("Pistol slide: kicks back on every shot and locks open when the last round is gone.")]
        [SerializeField] Transform slide;
        [SerializeField] float slideTravel = 0.026f;
        [Tooltip("Drops out of the grip during the reload.")]
        [SerializeField] Transform magazine;
        [Tooltip("How far the magazine drops out, in the pose's space (down the grip).")]
        [SerializeField] Vector3 magazineOut = new Vector3(0f, -0.155f, -0.039f);
        [Tooltip("Leaves the gun during the reload.")]
        [SerializeField] Transform leftArm;
        [Tooltip("Where the left arm goes during the reload, in the pose's space.")]
        [SerializeField] Vector3 leftArmReach = new Vector3(-0.04f, -0.16f, -0.08f);

        [Header("Light")]
        [Tooltip("A soft light that only reaches the viewmodel's rendering layer, on while the torch is on. The torch " +
                 "itself skips that layer, because a few centimetres from the lens it would burn the gun white.")]
        [SerializeField] Light fillLight;

        [Header("Firing")]
        [SerializeField] GameObject muzzleFlash;
        [SerializeField] Light shotLight;
        [SerializeField] float shotLightIntensity = 4f;
        [SerializeField] float flashSeconds = 0.05f;
        [Tooltip("Played on every shot: gun smoke, or the ice spray.")]
        [SerializeField] ParticleSystem[] fireEffects = new ParticleSystem[0];
        [Tooltip("Metres the gun jumps back per shot.")]
        [SerializeField] float kickBack = 0.035f;
        [Tooltip("Degrees the muzzle flips up per shot.")]
        [SerializeField] float kickUp = 7f;

        [Header("Sound")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip fireClip;
        [SerializeField] AudioClip emptyClip;
        [SerializeField] AudioClip reloadClip;
        [SerializeField] AudioClip equipClip;

        [Header("Feel")]
        [Tooltip("Degrees of lag per pixel of mouse movement (at 60 fps).")]
        [SerializeField] float swayPerPixel = 0.035f;
        [SerializeField] float swayLimit = 3.5f;
        [SerializeField] float bobAmount = 0.007f;
        [Tooltip("The gun pulls in and points up when a wall is closer than this, so it never pokes through.")]
        [SerializeField] float tuckDistance = 0.6f;
        [SerializeField] float drawSeconds = 0.35f;
        [SerializeField] float reloadSeconds = 1.5f;

        /// <summary>The rendering layer the gun and arms draw on (bit 1). World lights and the torch use only the
        /// default layer (bit 0), so the viewmodel is lit by its fill light, its shot light and ambient light.</summary>
        public const uint RenderingLayer = 1u << 1;

        // Recoil is a damped spring: a shot kicks its velocity, it settles back with a little overshoot.
        const float SpringStiffness = 260f;
        const float SpringDamping = 26f;

        WeaponSystem weapons;
        PlayerController player;
        PlayerHealth health;
        Vector3 slideRest, magazineRest, leftArmRest;
        Quaternion leftArmRestRotation;
        Vector3 slideBack, magazineDrop, leftArmDrop;      // the moves above, in each part's parent space
        float kick, kickVelocity;
        float slideTime = 1f, flashTime = 1f, reloadTime = -1f, lastReloadU, drawTime;
        bool slideLocked;
        float bobPhase, tuck, lowered;
        Vector2 sway;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public WeaponKind Kind => kind;
        public bool IsReloading => reloadTime >= 0f;

        void Awake()
        {
            weapons = GetComponentInParent<WeaponSystem>();
            player = GetComponentInParent<PlayerController>();
            health = GetComponentInParent<PlayerHealth>();
            if (pose == null) pose = transform;
            if (slide != null)
            {
                slideRest = slide.localPosition;
                slideBack = PoseToParent(slide, Vector3.back * slideTravel);
            }
            if (magazine != null)
            {
                magazineRest = magazine.localPosition;
                magazineDrop = PoseToParent(magazine, magazineOut);
            }
            if (leftArm != null)
            {
                leftArmRest = leftArm.localPosition;
                leftArmRestRotation = leftArm.localRotation;
                leftArmDrop = PoseToParent(leftArm, leftArmReach);
            }
        }

        // A move written in the pose's space, turned into the part's parent space (the two never move apart).
        Vector3 PoseToParent(Transform part, Vector3 move) => part.parent.InverseTransformVector(pose.TransformVector(move));

        void OnEnable()
        {
            if (weapons != null)
            {
                weapons.OnFired += HandleFired;
                weapons.OnEmpty += HandleEmpty;
                weapons.OnAmmoAdded += HandleAmmoAdded;
                slideLocked = kind == WeaponKind.Pistol && weapons.Ammo01 <= 0f;
            }
            drawTime = 0f;
            reloadTime = -1f;
            kick = kickVelocity = 0f;
            flashTime = slideTime = 1f;
            ResetParts();
            Play(equipClip, 0.8f);
        }

        void OnDisable()
        {
            if (weapons != null)
            {
                weapons.OnFired -= HandleFired;
                weapons.OnEmpty -= HandleEmpty;
                weapons.OnAmmoAdded -= HandleAmmoAdded;
            }
            reloadTime = -1f;
            ResetParts();
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            if (shotLight != null) shotLight.enabled = false;
        }

        bool Holding => weapons != null && weapons.CurrentWeapon == kind;

        void HandleFired()
        {
            if (!Holding) return;
            if (reloadTime >= 0f)       // firing cuts a reload short
            {
                reloadTime = -1f;
                ResetParts();
            }
            kickVelocity += 20f;
            slideTime = 0f;
            flashTime = 0f;
            if (muzzleFlash != null)
            {
                muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                muzzleFlash.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);
            }
            foreach (var effect in fireEffects)
                if (effect != null) effect.Play(true);
            Play(fireClip, Random.Range(0.9f, 1f));
            if (kind == WeaponKind.Pistol && weapons.Ammo01 <= 0f)
                slideLocked = true;      // the last round leaves the slide open
        }

        void HandleEmpty()
        {
            if (!Holding) return;
            kickVelocity += 3f;
            Play(emptyClip, 1f);
        }

        void HandleAmmoAdded()
        {
            if (!Holding || !isActiveAndEnabled) return;
            reloadTime = 0f;
            lastReloadU = 0f;
            Play(reloadClip, 1f);
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            kickVelocity += (-SpringStiffness * kick - SpringDamping * kickVelocity) * dt;
            kick += kickVelocity * dt;
            drawTime += dt;
            slideTime += dt;
            flashTime += dt;

            Vector3 position = Vector3.zero;
            Vector3 euler = Vector3.zero;     // x pitches the muzzle down, y turns it right, z rolls it left

            // Draw: the gun comes up from below the screen.
            float draw = 1f - Mathf.SmoothStep(0f, 1f, drawTime / drawSeconds);
            position += new Vector3(0.02f, -0.14f, -0.03f) * draw;
            euler.x += 35f * draw;

            // Sway: the gun lags behind the mouse a little.
            Vector2 mouse = Mouse.current != null && Cursor.lockState == CursorLockMode.Locked
                ? Mouse.current.delta.ReadValue() * (1f / 60f / dt) : Vector2.zero;
            Vector2 swayTarget = Vector2.ClampMagnitude(-mouse * swayPerPixel, swayLimit);
            sway = Vector2.Lerp(sway, swayTarget, 1f - Mathf.Exp(-10f * dt));
            euler.y += sway.x;
            euler.x -= sway.y;
            euler.z += sway.x * 0.6f;

            // Bob while walking, and a slow breath when standing still.
            float speed = player != null && player.IsGrounded ? player.CurrentSpeed : 0f;
            float walk = Mathf.Clamp01(speed / 5f);
            bobPhase += dt * speed * 4.2f;
            position += new Vector3(Mathf.Sin(bobPhase) * bobAmount, (Mathf.Cos(2f * bobPhase) - 1f) * 0.6f * bobAmount, 0f) * walk;
            euler.z += Mathf.Sin(bobPhase) * 1.2f * walk;
            position.y += Mathf.Sin(Time.time * 1.6f) * 0.0012f;
            euler.x += Mathf.Sin(Time.time * 1.1f) * 0.25f;

            // Tuck: pull in and point up when walking into a wall.
            tuck = Mathf.Lerp(tuck, WallCloseness(), 1f - Mathf.Exp(-10f * dt));
            position += new Vector3(-0.02f, -0.02f, -0.1f) * tuck;
            euler.x -= 32f * tuck;
            euler.y -= 8f * tuck;

            // Lowered out of sight when the player is dead.
            lowered = Mathf.MoveTowards(lowered, health != null && health.IsDead ? 1f : 0f, dt * 1.5f);
            position.y -= 0.3f * lowered;

            if (reloadTime >= 0f) Reload(dt, ref position, ref euler);
            else MoveSlide();

            // Recoil last, so it stacks on everything else.
            position += new Vector3(0f, 0.15f, -1f) * (kick * kickBack);
            euler.x -= kick * kickUp;

            pose.localPosition = position;
            pose.localRotation = Quaternion.Euler(euler);
            UpdateFlash();
            if (fillLight != null) fillLight.enabled = player == null || player.FlashlightOn;
        }

        float WallCloseness()
        {
            Transform eye = transform.parent;
            if (eye == null) return 0f;
            int count = Physics.SphereCastNonAlloc(eye.position, 0.06f, eye.forward, hits, tuckDistance, ~0,
                                                   QueryTriggerInteraction.Ignore);
            float nearest = tuckDistance;
            Transform self = player != null ? player.transform : transform.root;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].transform.IsChildOf(self)) continue;
                if (hits[i].distance > 0f && hits[i].distance < nearest) nearest = hits[i].distance;
            }
            return 1f - nearest / tuckDistance;
        }

        void MoveSlide()
        {
            if (slide == null) return;
            // Back in 30 ms, home again in the next 50 ms; held back once the gun is empty.
            float back = slideLocked ? 1f
                : slideTime < 0.03f ? slideTime / 0.03f : Mathf.Max(0f, 1f - (slideTime - 0.03f) / 0.05f);
            slide.localPosition = slideRest + slideBack * back;
        }

        /// <summary>
        /// The reload as eased curves over time. Pistol: tilt the gun, magazine drops out as the left hand
        /// leaves, the hand brings the magazine back up, then the slide is racked. Ice gun: tilt, the left
        /// hand leaves to open the valve while the canister refills, then comes back.
        /// </summary>
        void Reload(float dt, ref Vector3 position, ref Vector3 euler)
        {
            reloadTime += dt;
            float u = reloadTime / reloadSeconds;
            if (u >= 1f)
            {
                reloadTime = -1f;
                slideLocked = false;
                ResetParts();
                return;
            }
            bool pistol = kind == WeaponKind.Pistol;
            float tilt = Envelope(u, 0f, 0.15f, 0.82f, 1f);
            position += (pistol ? new Vector3(-0.03f, 0.03f, 0f) : new Vector3(-0.02f, 0.02f, -0.02f)) * tilt;
            euler += (pistol ? new Vector3(-12f, -6f, 25f) : new Vector3(-10f, -8f, -18f)) * tilt;

            float away = Envelope(u, 0.15f, 0.32f, 0.5f, 0.68f);
            if (leftArm != null)
            {
                leftArm.localPosition = leftArmRest + leftArmDrop * away;
                leftArm.localRotation = leftArmRestRotation * Quaternion.Euler(-25f * away, 0f, 10f * away);
            }
            if (magazine != null)
                magazine.localPosition = magazineRest + magazineDrop * Envelope(u, 0.12f, 0.28f, 0.52f, 0.66f);
            if (slide != null)
            {
                // The slide is pulled back and let go; it stays back until then if it was locked open.
                float rack = Envelope(u, 0.72f, 0.8f, 0.8f, 0.84f);
                slide.localPosition = slideRest + slideBack * (slideLocked && u < 0.8f ? 1f : rack);
            }
            // Small jolts where parts click home: the magazine seating and the slide snapping forward.
            if (pistol && Crossed(0.66f, u)) kickVelocity += 4f;
            if (pistol && Crossed(0.84f, u)) kickVelocity += 6f;
            if (!pistol && Crossed(0.75f, u)) kickVelocity += 3f;
            lastReloadU = u;
        }

        bool Crossed(float mark, float u) => lastReloadU < mark && u >= mark;

        /// <summary>Eases from 0 to 1 between inStart and inEnd, holds, then back to 0 between outStart and outEnd.</summary>
        public static float Envelope(float u, float inStart, float inEnd, float outStart, float outEnd)
        {
            float rise = inEnd > inStart ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inStart, inEnd, u)) : (u >= inStart ? 1f : 0f);
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outStart, outEnd, u));
            return rise * (1f - fall);
        }

        void UpdateFlash()
        {
            if (muzzleFlash != null) muzzleFlash.SetActive(flashTime < flashSeconds);
            if (shotLight != null)
            {
                float glow = 1f - flashTime / (flashSeconds * 1.6f);
                shotLight.enabled = glow > 0f;
                shotLight.intensity = shotLightIntensity * Mathf.Max(0f, glow);
            }
        }

        void ResetParts()
        {
            if (magazine != null) magazine.localPosition = magazineRest;
            if (leftArm != null)
            {
                leftArm.localPosition = leftArmRest;
                leftArm.localRotation = leftArmRestRotation;
            }
            if (slide != null) slide.localPosition = slideRest + (slideLocked ? slideBack : Vector3.zero);
        }
    }
}
