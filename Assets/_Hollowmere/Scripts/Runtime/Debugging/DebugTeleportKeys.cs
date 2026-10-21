using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.Debugging
{
    /// <summary>
    /// TEMPORARY test keys for the real player: 1-7 jump to the same test points the debug walker had.
    /// The zone at the destination may still be loading, so the player's own controller is switched off
    /// until there is ground under the new position; otherwise gravity would drop it through the world.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class DebugTeleportKeys : MonoBehaviour
    {
        [Tooltip("The player's movement script, switched off while the destination loads.")]
        [SerializeField] Behaviour movement;
        [SerializeField] Vector3[] testPoints =
        {
            new Vector3(0f, 2.3f, -31f),         // 1: start, south of the village
            new Vector3(3f, 3f, 80f),            // 2: estate gate
            new Vector3(11f, 18f, 150f),         // 3: half way up the estate road
            new Vector3(0f, 24f, 215f),          // 4: Marrow House forecourt
            new Vector3(0f, 26.3f, 241f),        // 5: inside Marrow House, entrance hall
            new Vector3(-16.4f, 14.7f, 239.5f),  // 6: the Undercroft laboratory
            new Vector3(-106.5f, 8.9f, 245.3f),  // 7: the Waterworks, at the tunnel mouth
        };

        CharacterController controller;
        bool waitingForGround;

        void Awake() => controller = GetComponent<CharacterController>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
                for (int i = 0; i < testPoints.Length && i < 9; i++)
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame) Teleport(testPoints[i]);

            if (waitingForGround && Physics.Raycast(transform.position + Vector3.up, Vector3.down, 200f))
            {
                waitingForGround = false;
                if (movement != null) movement.enabled = true;
            }
        }

        public void Teleport(Vector3 position)
        {
            if (movement != null) movement.enabled = false;
            controller.enabled = false;              // a CharacterController ignores direct moves while enabled
            transform.position = position;
            controller.enabled = true;
            waitingForGround = true;
        }
    }
}
