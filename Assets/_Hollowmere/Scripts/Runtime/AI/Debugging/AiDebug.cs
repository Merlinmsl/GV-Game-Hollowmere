using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollowmere.AI.Debugging
{
    /// <summary>
    /// Global debug controller for AI visualization overlays.
    /// Toggleable via F2 key at runtime.
    /// </summary>
    public static class AiDebug
    {
        /// <summary>Global toggle state for AI debug views and gizmos.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Polls keyboard input for F2 toggle across both new and legacy Input Systems.
        /// </summary>
        public static void CheckToggleInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                Enabled = !Enabled;
                return;
            }
#endif
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Enabled = !Enabled;
            }
        }
    }
}
