using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Hollowmere.Agents
{
    /// <summary>
    /// TEMPORARY stand-in for an AI brain, used to test locomotion in a sandbox scene. Left-click the
    /// ground to send the agent there; hold Shift to run. Add it to a scene instance only, never to
    /// an agent prefab: in the game the brain sets the destination and speed.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ClickToMove : MonoBehaviour
    {
        [Tooltip("Left empty, Camera.main is used at the moment of the click.")]
        [SerializeField] Camera viewCamera;
        [SerializeField] float walkSpeed = 1.2f;
        [SerializeField] float runSpeed = 4.5f;
        [Tooltip("Layers the click ray can hit.")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far from the clicked point to look for walkable NavMesh, in metres.")]
        [SerializeField] float snapDistance = 2f;

        NavMeshAgent agent;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            agent.speed = keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;

            if (!mouse.leftButton.wasPressedThisFrame) return;

            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, groundMask)) return;

            // The clicked point may be off the NavMesh (a cube top, an edge): snap to the nearest walkable point.
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, snapDistance, agent.areaMask))
                agent.SetDestination(navHit.position);
        }
    }
}
