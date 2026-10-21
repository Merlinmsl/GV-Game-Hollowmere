using UnityEngine;
using Hollowmere.Interaction;

namespace Hollowmere.Pickups
{
    public enum PickupType
    {
        Key,
        Ammo,
        IceCanister,
        Battery,
        EvidencePage
    }

    /// <summary>
    /// Interactive pickup object placed in the world.
    /// Implements IInteractable to support pickup via PlayerInteractor (E key).
    /// Covers Keys, Ammo, Ice Canisters, Batteries, and Evidence Pages per SystemsEngineer.md.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Pickup : MonoBehaviour, IInteractable
    {
        [Header("Pickup Configuration")]
        [SerializeField] private PickupType pickupType = PickupType.Ammo;
        [SerializeField] private string itemId = "Ammo_Pistol";
        [SerializeField] private string displayName = "Ammunition";
        [SerializeField] private float amount = 6f;

        [Header("Visual Effects")]
        [SerializeField] private bool bobAndSpin = true;
        [SerializeField] private float bobSpeed = 2f;
        [SerializeField] private float bobHeight = 0.08f;
        [SerializeField] private float spinSpeed = 45f;

        [Header("Audio")]
        [SerializeField] private AudioClip pickupSound;
        [SerializeField] private AudioSource audioSource;

        private Vector3 startPosition;

        public PickupType Type => pickupType;
        public string ItemId => itemId;
        public string DisplayName => displayName;
        public float Amount => amount;

        public string Prompt => $"Pick up {displayName}";

        public void Configure(PickupType type, string id, string name, float amt = 1f)
        {
            pickupType = type;
            itemId = id;
            displayName = name;
            amount = amt;
        }

        private void Start()
        {
            startPosition = transform.position;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            // Ensure collider is marked as a trigger or interactable
            var col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void Update()
        {
            if (bobAndSpin)
            {
                float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
                transform.position = new Vector3(transform.position.x, newY, transform.position.z);
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            }
        }

        public bool CanInteract(PlayerInventory inventory)
        {
            return true;
        }

        public void Interact(PlayerInventory inventory)
        {
            if (inventory != null)
            {
                switch (pickupType)
                {
                    case PickupType.Key:
                        inventory.AddKey(itemId);
                        break;
                    case PickupType.Ammo:
                        inventory.AddAmmo(amount);
                        break;
                    case PickupType.IceCanister:
                        inventory.AddIceCanisters(Mathf.Max(1, (int)amount));
                        break;
                    case PickupType.Battery:
                        inventory.AddBatteries(Mathf.Max(1, (int)amount));
                        break;
                    case PickupType.EvidencePage:
                        inventory.AddEvidence(itemId);
                        break;
                }
            }

            if (pickupSound != null)
            {
                AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            }

            Debug.Log($"<color=green><b>[Pickup]</b> Collected {displayName} ({itemId})</color>");

            // Notify any MapMarker attached so the item is marked collected on the map
            SendMessage("Collect", SendMessageOptions.DontRequireReceiver);
            if (transform.parent != null)
            {
                transform.parent.SendMessage("Collect", SendMessageOptions.DontRequireReceiver);
            }

            // Disable or destroy pickup
            gameObject.SetActive(false);
        }
    }
}
