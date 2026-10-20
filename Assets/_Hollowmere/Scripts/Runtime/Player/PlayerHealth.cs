using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Hollowmere.Combat;

namespace Hollowmere.Player
{
    /// <summary>
    /// Manages the player's health pool, damage reception, invulnerability frames,
    /// damage screen-flash feedback, and death sequence.
    /// Implements IDamageable and synchronizes with PlayerController.Health01.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Health Settings")]
        [Tooltip("Maximum player health capacity.")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Seconds of invulnerability after taking a hit.")]
        [SerializeField] private float invulnerabilityDuration = 0.4f;

        [Header("Feedback")]
        [Tooltip("Seconds the red screen flash lingers after taking damage.")]
        [SerializeField] private float damageFlashDuration = 0.3f;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float Health01 => Mathf.Clamp01(currentHealth / maxHealth);
        public bool IsDead => currentHealth <= 0f;

        public event Action<float, float> OnHealthChanged;
        public event Action<float, GameObject> OnDamaged;
        public event Action OnDeath;

        private float currentHealth;
        private float lastDamageTime = -10f;
        private float damageFlashTimer;
        private PlayerController playerController;
        private GUIStyle deathStyle;
        private GUIStyle restartStyle;
        private Texture2D flashTexture;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            currentHealth = maxHealth;

            // 1x1 white texture for damage flash overlay
            flashTexture = new Texture2D(1, 1);
            flashTexture.SetPixel(0, 0, Color.white);
            flashTexture.Apply();
        }

        private void Start()
        {
            if (playerController != null)
            {
                playerController.Health01 = Health01;
            }
        }

        private void Update()
        {
            if (damageFlashTimer > 0f)
            {
                damageFlashTimer -= Time.deltaTime;
            }

            if (IsDead)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
            }
        }

        /// <summary>
        /// Inflicts damage on the player, applying invulnerability window.
        /// </summary>
        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection, GameObject attacker = null)
        {
            if (IsDead) return;
            if (Time.time < lastDamageTime + invulnerabilityDuration) return;

            lastDamageTime = Time.time;
            damageFlashTimer = damageFlashDuration;
            currentHealth = Mathf.Max(0f, currentHealth - amount);

            if (playerController != null)
            {
                playerController.Health01 = Health01;
            }

            OnDamaged?.Invoke(amount, attacker);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth <= 0f)
            {
                Die();
            }
        }

        /// <summary>
        /// Restores player health up to max capacity.
        /// </summary>
        public void Heal(float amount)
        {
            if (IsDead) return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

            if (playerController != null)
            {
                playerController.Health01 = Health01;
            }

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Die()
        {
            if (playerController != null)
            {
                playerController.enabled = false;
            }

            Cursor.lockState = CursorLockMode.None;
            OnDeath?.Invoke();
            Debug.Log("<color=red><b>[PlayerHealth]</b> Player has fallen. Press 'R' to restart.</color>");
        }

        private void OnGUI()
        {
            // Damage red screen flash
            if (damageFlashTimer > 0f && flashTexture != null)
            {
                float alpha = (damageFlashTimer / damageFlashDuration) * 0.35f;
                Color prev = GUI.color;
                GUI.color = new Color(0.9f, 0.1f, 0.1f, alpha);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), flashTexture);
                GUI.color = prev;
            }

            // Death Screen
            if (IsDead)
            {
                if (deathStyle == null)
                {
                    deathStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 48,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                    deathStyle.normal.textColor = new Color(0.85f, 0.15f, 0.15f, 1f);

                    restartStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 20,
                        alignment = TextAnchor.MiddleCenter
                    };
                    restartStyle.normal.textColor = Color.white;
                }

                float cx = Screen.width * 0.5f;
                float cy = Screen.height * 0.45f;

                GUI.Label(new Rect(cx - 300, cy - 60, 600, 80), "YOU DIED", deathStyle);
                GUI.Label(new Rect(cx - 300, cy + 20, 600, 40), "Press [R] to Restart", restartStyle);
            }
        }

        private void OnDestroy()
        {
            if (flashTexture != null)
            {
                Destroy(flashTexture);
            }
        }
    }
}
