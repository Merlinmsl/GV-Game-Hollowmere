using System;
using UnityEngine;
using UnityEngine.Events;
using Hollowmere.AI.Senses;
using Hollowmere.Interaction;

namespace Hollowmere.Interactables
{
    /// <summary>
    /// Zone exit gate or passage barrier.
    /// Requires the zone's 3 evidence pages (and optional key) before opening.
    /// Named with prefix 'ZoneExit_' for discovery by GameDirector.
    /// Defined in SystemsEngineer.md (Sections 7 & 10).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ZoneExit : MonoBehaviour, IInteractable
    {
        [Header("Unlock Requirements")]
        [Tooltip("Number of evidence documents required to unseal this exit (default 3).")]
        [SerializeField] private int requiredEvidenceCount = 3;

        [Tooltip("Zone prefix filter for evidence pages (e.g. 'Z1', 'Z2', 'Z3'). If empty, counts all pages.")]
        [SerializeField] private string zonePrefix = "";

        [Tooltip("Optional key name required in addition to evidence (e.g. 'Key_TunnelExit').")]
        [SerializeField] private string requiredKey = "";

        [Header("Physical Gate / Barrier")]
        [Tooltip("Transform of the gate door/barrier that swings or slides open.")]
        [SerializeField] private Transform gateLeaf;

        [Tooltip("Target rotation when gate opens.")]
        [SerializeField] private Vector3 openRotationEuler = new Vector3(0f, 90f, 0f);

        [Tooltip("Optional barrier collider blocking passage until unlocked.")]
        [SerializeField] private Collider barrierCollider;

        [Header("Audio & Noise")]
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private float noiseLoudness = Loudness.Door;

        [Header("Events")]
        public UnityEvent onExitOpened;

        private bool isOpen = false;
        private Quaternion targetOpenRotation;

        public bool IsOpen => isOpen;
        public int RequiredEvidenceCount => requiredEvidenceCount;
        public string RequiredKey => requiredKey;

        public string Prompt
        {
            get
            {
                if (isOpen) return "Exit Unlocked";

                var playerObj = GameObject.FindWithTag("Player");
                PlayerInventory inv = playerObj != null ? playerObj.GetComponent<PlayerInventory>() : null;
                int currentEvidence = inv != null ? inv.GetEvidenceCountForZone(zonePrefix) : 0;

                if (currentEvidence < requiredEvidenceCount)
                {
                    return $"Sealed (Need {requiredEvidenceCount} Evidence Pages - [{currentEvidence}/{requiredEvidenceCount}])";
                }

                if (!string.IsNullOrEmpty(requiredKey) && (inv == null || !inv.HasKey(requiredKey)))
                {
                    return $"Locked ({requiredKey} required)";
                }

                return "Unlock Zone Exit";
            }
        }

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (gateLeaf != null)
            {
                targetOpenRotation = gateLeaf.localRotation * Quaternion.Euler(openRotationEuler);
            }
        }

        private void Update()
        {
            if (isOpen && gateLeaf != null)
            {
                gateLeaf.localRotation = Quaternion.RotateTowards(
                    gateLeaf.localRotation,
                    targetOpenRotation,
                    90f * Time.deltaTime
                );
            }
        }

        public bool CanInteract(PlayerInventory inventory)
        {
            if (isOpen) return false;
            if (inventory == null) return false;

            int count = inventory.GetEvidenceCountForZone(zonePrefix);
            if (count < requiredEvidenceCount) return false;

            if (!string.IsNullOrEmpty(requiredKey) && !inventory.HasKey(requiredKey))
            {
                return false;
            }

            return true;
        }

        public void Interact(PlayerInventory inventory)
        {
            if (CanInteract(inventory))
            {
                OpenExit();
            }
            else
            {
                Debug.Log($"<color=yellow><b>[ZoneExit]</b> Exit cannot be opened yet. Check evidence and keys.</color>");
            }
        }

        /// <summary>
        /// Unseals the zone exit, swings the gate open, and disables physical blockage.
        /// </summary>
        public void OpenExit()
        {
            if (isOpen) return;
            isOpen = true;

            if (barrierCollider != null)
            {
                barrierCollider.enabled = false;
            }

            if (audioSource != null && openClip != null)
            {
                audioSource.PlayOneShot(openClip);
            }

            NoiseSystem.Emit(transform.position, noiseLoudness, NoiseKind.Interaction, gameObject);
            Debug.Log($"<color=green><b>[ZoneExit]</b> Exit {gameObject.name} unsealed! Passage cleared.</color>");

            onExitOpened?.Invoke();
        }
    }
}
