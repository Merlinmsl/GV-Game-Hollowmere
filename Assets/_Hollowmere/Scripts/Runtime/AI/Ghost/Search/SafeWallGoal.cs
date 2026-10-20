using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Flee goal: a cell inside a wall (where the player cannot follow and the ghost fades out) that is
    /// outside the ice gun's aim cone. Used with BfsField.FindNearest, which returns the nearest such cell.
    /// </summary>
    public struct SafeWallGoal : ICellGoal
    {
        readonly GhostGrid grid;
        readonly Vector3 aimOrigin;
        readonly Vector3 aimDirection;
        readonly float cosHalfAngle;
        readonly float range;

        /// <param name="aimOrigin">Where the player was last seen aiming from (eye height).</param>
        /// <param name="aimDirection">The direction they were aiming, as last seen.</param>
        /// <param name="halfAngleDegrees">Half the width of the cone the ghost treats as dangerous.</param>
        /// <param name="range">How far the ice gun reaches.</param>
        public SafeWallGoal(GhostGrid grid, Vector3 aimOrigin, Vector3 aimDirection, float halfAngleDegrees, float range)
        {
            this.grid = grid;
            this.aimOrigin = aimOrigin;
            this.aimDirection = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector3.forward;
            cosHalfAngle = Mathf.Cos(halfAngleDegrees * Mathf.Deg2Rad);
            this.range = range;
        }

        public bool IsGoal(int cell) =>
            grid.Is(cell, CellFlags.Solid) && grid.Is(cell, CellFlags.Inside) && !InAim(grid.CellCentre(cell));

        /// <summary>Is a point inside the aim cone and within range?</summary>
        public bool InAim(Vector3 point)
        {
            Vector3 toPoint = point - aimOrigin;
            float distance = toPoint.magnitude;
            if (distance < 1e-4f) return true;
            if (distance > range) return false;
            return Vector3.Dot(toPoint, aimDirection) / distance >= cosHalfAngle;
        }
    }
}
