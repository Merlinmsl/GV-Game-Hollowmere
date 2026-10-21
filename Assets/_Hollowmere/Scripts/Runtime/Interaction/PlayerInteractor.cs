using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.Interaction
{
    /// <summary>
    /// Casts an interaction ray forward from the player's camera (2.5m), falling back to the
    /// nearest item in front of the player's feet. Renders an on-screen HUD prompt,
    /// and invokes Interact() upon pressing the 'E' key.
    /// Defined in SystemsEngineer.md (Section 5).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Raycast Settings")]
        [Tooltip("Max reach for interacting with world objects.")]
        [SerializeField] private float reachDistance = 2.5f;

        [Tooltip("Radius of the look ray, so small items do not need pixel-perfect aim.")]
        [SerializeField] private float lookRadius = 0.12f;

        [Tooltip("Items this close in front of the player can be taken without looking straight at them.")]
        [SerializeField] private float nearbyRadius = 1.6f;

        [Tooltip("Layer mask for interactables. Defaults to all standard layers if unassigned.")]
        [SerializeField] private LayerMask interactMask = ~0;

        [Tooltip("Camera transform used as origin. If null, automatically resolves Camera.main or parent Head.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Inventory Link")]
        [SerializeField] private PlayerInventory inventory;

        private readonly RaycastHit[] rayHits = new RaycastHit[16];
        private readonly Collider[] nearby = new Collider[32];
        private Transform playerRoot;
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
        /// Picks what E acts on: whatever the player looks at, or else the nearest item at their feet.
        /// </summary>
        private void CheckForInteractable()
        {
            if (cameraTransform == null) return;

            IInteractable found = LookTarget();
            if (found == null) found = NearbyItem();
            if (found == null)
            {
                ClearTarget();
                return;
            }

            currentTarget = found;
            canInteractWithTarget = found.CanInteract(inventory);
            currentPrompt = found.Prompt;
        }

        // A thick ray from the eye. Every hit along it is checked, so a fog or post-processing
        // volume (a trigger with nothing to use) no longer hides the item behind it; a solid wall does.
        private IInteractable LookTarget()
        {
            var ray = new Ray(cameraTransform.position, cameraTransform.forward);
            int count = Physics.SphereCastNonAlloc(ray, lookRadius, rayHits, reachDistance, interactMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDistance = float.MaxValue, wallDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (hit.collider.transform.IsChildOf(PlayerRoot)) continue;

                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    if (hit.distance < bestDistance) { best = interactable; bestDistance = hit.distance; }
                }
                else if (!hit.collider.isTrigger && hit.distance > 0f && hit.distance < wallDistance)
                {
                    wallDistance = hit.distance;
                }
            }
            return bestDistance <= wallDistance ? best : null;
        }

        // Items on the floor sit below a forward-looking ray, so standing next to one was not enough.
        // The nearest pickup or health station in front of the player, with nothing solid between, is
        // offered instead. Doors and gates still have to be looked at.
        private IInteractable NearbyItem()
        {
            Transform root = PlayerRoot;
            Vector3 eye = cameraTransform.position;
            Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            int count = Physics.OverlapSphereNonAlloc(root.position + Vector3.up * 0.8f, nearbyRadius, nearby, interactMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var col = nearby[i];
                var interactable = col.GetComponentInParent<IInteractable>();
                if (!(interactable is Hollowmere.Pickups.Pickup) && !(interactable is Hollowmere.Pickups.HealthStation)) continue;

                Vector3 centre = col.bounds.center;
                Vector3 flat = Vector3.ProjectOnPlane(centre - root.position, Vector3.up);
                if (flat.sqrMagnitude > 0.09f && Vector3.Dot(flat.normalized, facing) < 0.3f) continue;   // behind or beside

                var owner = ((Component)interactable).transform;
                if (Physics.Linecast(eye, centre, out RaycastHit block, interactMask, QueryTriggerInteraction.Ignore)
                    && !block.collider.transform.IsChildOf(owner)) continue;                                  // a wall between

                float distance = (centre - eye).sqrMagnitude;
                if (distance < bestDistance) { best = interactable; bestDistance = distance; }
            }
            return best;
        }

        private Transform PlayerRoot
        {
            get
            {
                if (playerRoot == null)
                {
                    var body = GetComponentInParent<CharacterController>();
                    playerRoot = body != null ? body.transform : transform.root;
                }
                return playerRoot;
            }
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
