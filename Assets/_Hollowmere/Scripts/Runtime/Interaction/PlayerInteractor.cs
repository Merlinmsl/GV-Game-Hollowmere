using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.Interaction
{
    /// <summary>
    /// Casts an interaction ray forward from the player's camera (2.5m).
    /// Detects any IInteractable targets, renders an on-screen HUD prompt,
    /// and invokes Interact() upon pressing the 'E' key.
    /// Defined in SystemsEngineer.md (Section 5).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Raycast Settings")]
        [Tooltip("Max reach for interacting with world objects.")]
        [SerializeField] private float reachDistance = 2.5f;

        [Tooltip("Layer mask for interactables. Defaults to all standard layers if unassigned.")]
        [SerializeField] private LayerMask interactMask = ~0;

        [Tooltip("Camera transform used as origin. If null, automatically resolves Camera.main or parent Head.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Inventory Link")]
        [SerializeField] private PlayerInventory inventory;

        private IInteractable currentTarget;
        private string currentPrompt = "";
        private bool canInteractWithTarget = false;

        // UI Notification toast
        private string toastMessage = "";
        private float toastExpiryTime = 0f;

        public IInteractable CurrentTarget => currentTarget;
        public string CurrentPrompt => currentPrompt;
        public bool CanInteract => canInteractWithTarget;

        private void Awake()
        {
            if (cameraTransform == null)
            {
                Camera cam = GetComponent<Camera>();
                if (cam == null) cam = GetComponentInChildren<Camera>();
                if (cam == null && Camera.main != null) cam = Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }

            if (inventory == null)
            {
                inventory = GetComponentInParent<PlayerInventory>();
                if (inventory == null) inventory = GetComponent<PlayerInventory>();
            }

            if (inventory != null)
            {
                inventory.OnKeyCollected += key => ShowToast($"Key Acquired: {key}");
                inventory.OnEvidenceCollected += doc => ShowToast($"Evidence Collected: {doc}");
                inventory.OnResourceCollected += (name, amt) => ShowToast($"+{amt} {name}");
            }
        }

        private void Update()
        {
            CheckForInteractable();
            HandleInput();
        }

        /// <summary>
        /// Casts ray forward to identify candidate IInteractable objects.
        /// </summary>
        private void CheckForInteractable()
        {
            if (cameraTransform == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            // Use QueryTriggerInteraction.Collide so trigger-based pickups & interactables are detected
            if (Physics.Raycast(ray, out RaycastHit hit, reachDistance, interactMask, QueryTriggerInteraction.Collide))
            {
                // Don't interact with ourselves
                if (hit.transform.IsChildOf(transform.root))
                {
                    ClearTarget();
                    return;
                }

                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    currentTarget = interactable;
                    canInteractWithTarget = interactable.CanInteract(inventory);
                    currentPrompt = interactable.Prompt;
                    return;
                }
            }

            ClearTarget();
        }

        private void ClearTarget()
        {
            currentTarget = null;
            currentPrompt = "";
            canInteractWithTarget = false;
        }

        /// <summary>
        /// Reads E key presses and triggers interaction.
        /// </summary>
        private void HandleInput()
        {
            if (currentTarget == null) return;

            bool ePressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                ePressed = true;
            }
#endif

            if (ePressed)
            {
                currentTarget.Interact(inventory);
                // Name the item in plain words rather than by its inventory id
                if (currentTarget is Hollowmere.Pickups.Pickup pickup && !pickup.gameObject.activeInHierarchy)
                {
                    ShowToast($"Picked up: {pickup.DisplayName}");
                }
                // Refresh current target status immediately
                CheckForInteractable();
            }
        }

        public void ShowToast(string message, float duration = 3f)
        {
            toastMessage = message;
            toastExpiryTime = Time.time + duration;
        }

        private void OnGUI()
        {
            // 1. Draw interaction prompt if pointing at something interactable
            if (currentTarget != null && !string.IsNullOrEmpty(currentPrompt))
            {
                float screenW = Screen.width;
                float screenH = Screen.height;

                GUIStyle promptStyle = new GUIStyle(GUI.skin.box);
                promptStyle.fontSize = 15;
                promptStyle.fontStyle = FontStyle.Bold;
                promptStyle.alignment = TextAnchor.MiddleCenter;

                Color oldColor = GUI.color;
                GUI.color = canInteractWithTarget ? new Color(0.1f, 0.8f, 1f, 0.9f) : new Color(1f, 0.4f, 0.4f, 0.9f);

                string displayText = canInteractWithTarget ? $"[E] {currentPrompt}" : currentPrompt;
                Vector2 size = promptStyle.CalcSize(new GUIContent(displayText));
                float boxW = Mathf.Max(size.x + 30f, 200f);
                float boxH = 36f;
                Rect rect = new Rect((screenW - boxW) * 0.5f, screenH * 0.58f, boxW, boxH);

                GUI.Box(rect, displayText, promptStyle);
                GUI.color = oldColor;
            }

            // 2. Draw recent toast pickup notification in top center
            if (Time.time < toastExpiryTime && !string.IsNullOrEmpty(toastMessage))
            {
                float screenW = Screen.width;
                GUIStyle toastStyle = new GUIStyle(GUI.skin.box);
                toastStyle.fontSize = 16;
                toastStyle.fontStyle = FontStyle.Bold;
                toastStyle.alignment = TextAnchor.MiddleCenter;

                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.9f, 0.3f, 0.95f);

                float boxW = 340f;
                float boxH = 40f;
                Rect rect = new Rect((screenW - boxW) * 0.5f, 50f, boxW, boxH);

                GUI.Box(rect, toastMessage, toastStyle);
                GUI.color = oldColor;
            }
        }
    }
}
