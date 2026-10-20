using System.Collections.Generic;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// TEMPORARY stand-in for the parts of combat nobody has built yet (no ice gun, no player damage API):
    ///  - Ice gun: with the ice gun held (Q), left click or I fires. The shot is a noise (so ghosts hear the
    ///    weapon), and the first ghost within range, inside a narrow cone from the camera and not behind a
    ///    wall, is frozen through IIceable.
    ///  - Strike: every ghost's Struck event is logged and shown on screen. The real game should drain
    ///    the player's health there.
    /// Delete this when the team's weapon and health code exist.
    /// </summary>
    public sealed class GhostCombatStub : MonoBehaviour
    {
        [SerializeField] float freezeSeconds = 3f;
        [SerializeField] float iceRange = 15f;
        [Tooltip("Half-angle of the cone the shot hits in, in degrees.")]
        [SerializeField] float hitAngle = 6f;
        [Tooltip("What stops the ice: walls and floors.")]
        [SerializeField] LayerMask walls = 1;

        readonly List<GhostBody> subscribed = new List<GhostBody>();
        string message = "";
        float messageUntil;
        GUIStyle style;

        void Update()
        {
            for (int i = 0; i < GhostBody.Active.Count; i++)
            {
                GhostBody ghost = GhostBody.Active[i];
                if (subscribed.Contains(ghost)) continue;
                subscribed.Add(ghost);
                ghost.Struck += at => Show(ghost.name + " STRUCK the player");
                ghost.Frozen += () => Show(ghost.name + " frozen");
                ghost.Freed += () => Show(ghost.name + " thawed and flees");
            }

            IPlayerState player = PlayerRegistry.Current;
            if (player == null || player.HeldWeapon != WeaponKind.IceGun) return;
            bool fire = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                        (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame);
            if (fire) FireIce(player);
        }

        void FireIce(IPlayerState player)
        {
            NoiseSystem.Emit(player.Position, Loudness.IceGun, NoiseKind.IceGun, gameObject);

            Camera view = Camera.main;
            if (view == null) return;
            Vector3 eye = view.transform.position, aim = view.transform.forward;
            float cos = Mathf.Cos(hitAngle * Mathf.Deg2Rad);

            for (int i = 0; i < GhostBody.Active.Count; i++)
            {
                GhostBody ghost = GhostBody.Active[i];
                Vector3 to = ghost.transform.position + Vector3.up * 1.2f - eye;
                float distance = to.magnitude;
                if (distance > iceRange || Vector3.Dot(to / distance, aim) < cos) continue;
                if (Physics.Raycast(eye, to / distance, distance - 0.3f, walls, QueryTriggerInteraction.Ignore)) continue;

                ghost.ReceiveIce(freezeSeconds);
                return;
            }
            Show("ice shot missed");
        }

        void Show(string text)
        {
            message = text;
            messageUntil = Time.time + 2.5f;
            Debug.Log("[GhostCombatStub] " + text);
        }

        void OnGUI()
        {
            if (Time.time > messageUntil) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { fontSize = 22, fontStyle = FontStyle.Bold };
            GUI.Box(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.3f, 400f, 40f), message, style);
        }
    }
}
