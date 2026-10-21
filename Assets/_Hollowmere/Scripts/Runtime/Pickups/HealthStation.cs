using UnityEngine;
using Hollowmere.AI.Senses;
using Hollowmere.Interaction;
using Hollowmere.Player;

namespace Hollowmere.Pickups
{
    /// <summary>
    /// A first-aid box fixed to a wall. Press E to restore health; it holds a few doses and goes
    /// dark when it is empty. It cannot be used at full health, so a dose is never wasted.
    /// Placed on the Station_Health_ markers by the World Builder tool.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HealthStation : MonoBehaviour, IInteractable
    {
        [Tooltip("Health restored per use (the player has 100).")]
        [SerializeField] private float healAmount = 60f;

        [Tooltip("How many times the box can be used.")]
        [SerializeField] private int uses = 2;

        [Tooltip("Renderers that glow while doses remain (the red cross).")]
        [SerializeField] private Renderer[] glow;

        [SerializeField] private AudioClip useClip;

        private int usesLeft;

        public int UsesLeft => usesLeft;

        public string Prompt
        {
            get
            {
                if (usesLeft <= 0) return "First-aid box (empty)";
                var health = FindHealth(null);
                if (health != null && health.Health01 >= 1f) return "First-aid box (health is full)";
                return $"Use first-aid box ({usesLeft} left)";
            }
        }

        private void Awake() => usesLeft = uses;

        public bool CanInteract(PlayerInventory inventory)
        {
            if (usesLeft <= 0) return false;
            var health = FindHealth(inventory);
            return health != null && !health.IsDead && health.Health01 < 1f;
        }

        public void Interact(PlayerInventory inventory)
        {
            if (!CanInteract(inventory)) return;

            var health = FindHealth(inventory);
            float before = health.CurrentHealth;
            health.Heal(healAmount);
            usesLeft--;

            if (useClip != null) AudioSource.PlayClipAtPoint(useClip, transform.position);
            NoiseSystem.Emit(transform.position, Loudness.Walk, NoiseKind.Interaction, gameObject);   // a latch, quieter than a door

            var interactor = inventory != null ? inventory.GetComponentInChildren<PlayerInteractor>() : null;
            if (interactor != null) interactor.ShowToast($"+{Mathf.RoundToInt(health.CurrentHealth - before)} health");

            if (usesLeft <= 0 && glow != null)
                foreach (var r in glow)
                    if (r != null) r.material.DisableKeyword("_EMISSION");   // per-instance copy: only this box goes dark
        }

        private static PlayerHealth FindHealth(PlayerInventory inventory)
        {
            if (inventory != null) return inventory.GetComponent<PlayerHealth>();
            return PlayerRegistry.Current is Component c ? c.GetComponent<PlayerHealth>() : null;
        }
    }
}
