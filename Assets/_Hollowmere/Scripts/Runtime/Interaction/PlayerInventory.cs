using System;
using System.Collections.Generic;
using UnityEngine;
using Hollowmere.Player;

namespace Hollowmere.Interaction
{
    /// <summary>
    /// Tracks all keys, evidence documents, and consumable supplies collected by the player.
    /// Attached to PF_Player and queried by IInteractable objects (doors, zone exits).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Starting Resources")]
        [SerializeField] private float ammoReserve = 12f;
        [SerializeField] private int iceCanisters = 2;
        [SerializeField] private int batteries = 1;

        [Tooltip("Ice gun charge one canister gives (the gun holds 100 and a shot costs 20).")]
        [SerializeField] private float iceChargePerCanister = 40f;

        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> evidencePages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cached player references
        private WeaponSystem weaponSystem;

        public event Action<string> OnKeyCollected;
        public event Action<string> OnEvidenceCollected;
        public event Action<string, float> OnResourceCollected;

        public IReadOnlyCollection<string> Keys => keys;
        public IReadOnlyCollection<string> EvidencePages => evidencePages;
        public int TotalEvidenceCount => evidencePages.Count;
        public float AmmoReserve => ammoReserve;
        public int IceCanisters => iceCanisters;
        public int Batteries => batteries;

        private void Awake()
        {
            weaponSystem = GetComponent<WeaponSystem>();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Keys Management
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Checks whether the player holds the given key by identifier.</summary>
        public bool HasKey(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return true;
            return keys.Contains(keyId);
        }

        /// <summary>Adds a key to the player's keychain.</summary>
        public void AddKey(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return;
            if (keys.Add(keyId))
            {
                Debug.Log($"<color=yellow><b>[PlayerInventory]</b> Acquired key: {keyId}</color>");
                OnKeyCollected?.Invoke(keyId);
            }
        }

        /// <summary>Removes a key from the player's keychain.</summary>
        public bool RemoveKey(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return false;
            return keys.Remove(keyId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Evidence Pages Management
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Checks whether a specific evidence page has been collected.</summary>
        public bool HasEvidence(string evidenceId)
        {
            if (string.IsNullOrEmpty(evidenceId)) return false;
            return evidencePages.Contains(evidenceId);
        }

        /// <summary>Records an evidence document in the player's case file.</summary>
        public void AddEvidence(string evidenceId)
        {
            if (string.IsNullOrEmpty(evidenceId)) return;
            if (evidencePages.Add(evidenceId))
            {
                Debug.Log($"<color=cyan><b>[PlayerInventory]</b> Collected Evidence: {evidenceId} (Total: {evidencePages.Count})</color>");
                OnEvidenceCollected?.Invoke(evidenceId);
            }
        }

        /// <summary>
        /// Returns the count of evidence pages collected for a given zone or prefix.
        /// If prefix is empty/null, returns the total evidence count.
        /// </summary>
        public int GetEvidenceCountForZone(string zonePrefix)
        {
            if (string.IsNullOrEmpty(zonePrefix))
            {
                return evidencePages.Count;
            }

            int count = 0;
            foreach (var evidence in evidencePages)
            {
                if (evidence.IndexOf(zonePrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                }
            }
            return count;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Supplies / Consumables Management
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Adds pistol rounds to weapon reserves and player weapon system.</summary>
        public void AddAmmo(float amount)
        {
            ammoReserve += amount;
            if (weaponSystem != null)
            {
                weaponSystem.AddPistolRounds(Mathf.RoundToInt(amount));   // amount is in rounds
            }
            Debug.Log($"<color=orange><b>[PlayerInventory]</b> Added {amount} ammo. Total reserve: {ammoReserve}</color>");
            OnResourceCollected?.Invoke("Ammo", amount);
        }

        /// <summary>Adds cryogenic ice canisters for the Ice Gun.</summary>
        public void AddIceCanisters(int count)
        {
            iceCanisters += count;
            if (weaponSystem != null)
            {
                weaponSystem.AddIceCharge(count * iceChargePerCanister);
            }
            Debug.Log($"<color=cyan><b>[PlayerInventory]</b> Added {count} Ice Canister(s). Total: {iceCanisters}</color>");
            OnResourceCollected?.Invoke("Ice Canister", count);
        }

        /// <summary>Adds flashlight batteries to the inventory.</summary>
        public void AddBatteries(int count)
        {
            batteries += count;
            Debug.Log($"<color=yellow><b>[PlayerInventory]</b> Added {count} Battery(ies). Total: {batteries}</color>");
            OnResourceCollected?.Invoke("Battery", count);
        }
    }
}
