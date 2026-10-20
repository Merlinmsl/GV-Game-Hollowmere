using System;
using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>
    /// Sees the player only inside its view range and cone, with nothing in between.
    /// Recognising the player takes time: a detection meter fills at
    ///   rate = detectRate x lightFactor x distanceFactor x postureFactor
    /// and the player counts as seen when it reaches 1. Out of sight, the meter drains.
    /// </summary>
    [Serializable]
    public sealed class SightSensor
    {
        [Tooltip("How far the agent can see, in metres.")]
        [SerializeField] float viewRange = 20f;
        [Tooltip("Width of the view cone, in degrees.")]
        [Range(10f, 360f)] [SerializeField] float viewAngle = 110f;
        [Tooltip("Meter filled per second at best: point-blank, lit, standing.")]
        [SerializeField] float detectRate = 2f;
        [Tooltip("Meter lost per second while the player is out of sight.")]
        [SerializeField] float forgetRate = 0.5f;
        [Tooltip("Height of the eyes above the agent's feet.")]
        [SerializeField] float eyeHeight = 1.7f;
        [Tooltip("What blocks the view: walls, floors, terrain.")]
        [SerializeField] LayerMask blockers = ~0;

        // Aim at the player's chest, not the feet, so a low wall does not hide a standing player.
        const float PlayerChestHeight = 1.2f;
        // A ray that hits something this close to the player has hit the player itself.
        const float PlayerHitTolerance = 0.6f;

        public float Meter { get; private set; }
        public bool InView { get; private set; }
        public float ViewRange => viewRange;

        /// <summary>Updates the meter. Returns true when the player is in view and fully recognised.</summary>
        public bool Tick(Transform owner, IPlayerState player, float deltaTime)
        {
            InView = player != null && CanSee(owner, player, out float distance)
                     && Fill(player, distance, deltaTime);
            if (!InView) Meter = Mathf.Max(0f, Meter - forgetRate * deltaTime);
            return InView && Meter >= 1f;
        }

        bool Fill(IPlayerState player, float distance, float deltaTime)
        {
            float lightFactor = Illumination.LightFactor(player.Position, player.FlashlightOn);
            float closeness = distance / viewRange;
            float distanceFactor = 1f - closeness * closeness;     // close is easy, the far edge is hard
            float postureFactor = player.IsCrouching ? 0.5f : 1f;

            float rate = detectRate * lightFactor * distanceFactor * postureFactor;
            Meter = Mathf.Min(1f, Meter + rate * deltaTime);
            return true;
        }

        bool CanSee(Transform owner, IPlayerState player, out float distance)
        {
            Vector3 eye = owner.position + Vector3.up * eyeHeight;
            Vector3 target = player.Position + Vector3.up * PlayerChestHeight;
            Vector3 toPlayer = target - eye;
            distance = toPlayer.magnitude;

            // Cheapest checks first: range, then the cone, and only then a ray.
            if (distance > viewRange) return false;
            if (Vector3.Angle(owner.forward, toPlayer) > viewAngle * 0.5f) return false;

            if (Physics.Raycast(eye, toPlayer / distance, out RaycastHit hit, distance, blockers, QueryTriggerInteraction.Ignore)
                && hit.distance < distance - PlayerHitTolerance)
                return false;

            return true;
        }
    }
}
