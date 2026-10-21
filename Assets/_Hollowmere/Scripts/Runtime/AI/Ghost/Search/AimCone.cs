using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// The space the ice gun threatens: a cone from where the player was last seen aiming, out to the
    /// gun's range. Shared by the flee goal and the position score so both agree on what "in the aim" means.
    /// </summary>
    public readonly struct AimCone
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly float CosHalfAngle;
        public readonly float Range;

        /// <param name="origin">Where the player was last seen aiming from (eye height).</param>
        /// <param name="direction">The direction they were aiming, as last seen.</param>
        /// <param name="halfAngleDegrees">Half the width of the cone the ghost treats as dangerous.</param>
        /// <param name="range">How far the ice gun reaches.</param>
        public AimCone(Vector3 origin, Vector3 direction, float halfAngleDegrees, float range)
        {
            Origin = origin;
            Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            CosHalfAngle = Mathf.Cos(halfAngleDegrees * Mathf.Deg2Rad);
            Range = range;
        }

        /// <summary>Is a point inside the cone and within range? (Walls are not considered here.)</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 toPoint = point - Origin;
            float distance = toPoint.magnitude;
            if (distance < 1e-4f) return true;
            if (distance > Range) return false;
            return Vector3.Dot(toPoint, Direction) / distance >= CosHalfAngle;
        }
    }
}
