using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hollowmere.Debugging
{
    /// <summary>
    /// TEMPORARY stand-in for the player, used to walk the world before the real player controller
    /// is delivered. WASD to move, mouse to look, Shift to run, Esc to release the mouse,
    /// 1-7 to jump between test points, F1 to hide the help overlay.
    /// Replace the object carrying this with the real player prefab (tagged "Player").
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class DebugWalker : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float walkSpeed = 3.5f;
        [SerializeField] float runSpeed = 7f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float gravity = -18f;
        [Tooltip("Number keys 1, 2, 3... jump to these world positions.")]
        [SerializeField] Vector3[] testPoints =
        {
            new Vector3(0f, 2.3f, -31f),   // 1: start, south of the village
            new Vector3(3f, 3f, 80f),      // 2: estate gate, foot of the road
            new Vector3(11f, 18f, 150f),   // 3: half way up the estate road
            new Vector3(0f, 24f, 215f),    // 4: Marrow House forecourt
            new Vector3(0f, 26.3f, 241f),  // 5: inside Marrow House, entrance hall
            new Vector3(-16.4f, 14.7f, 239.5f),  // 6: the Undercroft laboratory
            new Vector3(-106.5f, 8.9f, 245.3f),  // 7: the Waterworks, at the tunnel mouth
        };
        [SerializeField] bool showHelp = true;

        CharacterController controller;
        float pitch;
        float verticalVelocity;
        bool waitingForGround;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (keyboard.f1Key.wasPressedThisFrame) showHelp = !showHelp;

            for (int i = 0; i < testPoints.Length && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) Teleport(testPoints[i]);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
                if (head != null) head.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            // After a jump the zone underneath may still be loading: hang in the air until there is ground.
            if (waitingForGround)
            {
                if (!Physics.Raycast(transform.position + Vector3.up, Vector3.down, 200f)) return;
                waitingForGround = false;
            }

            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float right = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float speed = keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;
            Vector3 move = Vector3.ClampMagnitude(transform.forward * forward + transform.right * right, 1f) * speed;

            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);
        }

        /// <summary>Moves the walker instantly. A CharacterController ignores direct position changes while enabled.</summary>
        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            verticalVelocity = 0f;
            controller.enabled = true;
            waitingForGround = true;
        }

        void OnGUI()
        {
            if (!showHelp) return;

            string loaded = "";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                loaded += (i > 0 ? ", " : "") + SceneManager.GetSceneAt(i).name;

            Vector3 p = transform.position;
            GUI.Label(new Rect(12, 10, 900, 80),
                "DEBUG PLAYER   WASD move | Mouse look | Shift run | 1-7 jump to test points | Esc free mouse | F1 hide\n" +
                "Position  x " + p.x.ToString("F0") + "  y " + p.y.ToString("F0") + "  z " + p.z.ToString("F0") + "\n" +
                "Loaded: " + loaded);
        }
    }
}
