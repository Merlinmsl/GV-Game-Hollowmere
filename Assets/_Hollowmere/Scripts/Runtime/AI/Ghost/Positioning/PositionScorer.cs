using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Everything one position decision needs to know, all of it from the ghost's senses and memory:
    /// where it believes the player is, which way it last saw them look, the weapon it last saw, its own
    /// anchor and the cells other ghosts have already claimed. Never the player's true position.
    /// </summary>
    public struct PositionQuery
    {
        /// <summary>Height of the player's eye above their feet.</summary>
        public const float EyeHeight = 1.6f;

        /// <summary>Believed player position at eye height.</summary>
        public Vector3 PlayerEye;
        /// <summary>Last seen look direction; only used when HasLook.</summary>
        public Vector3 LookDirection;
        public bool HasLook;
        public float Now;

        public ScoreWeights Weights;
        public RangeBand Range;

        /// <summary>Is the ice gun a threat right now (last seen held and aimed)?</summary>
        public bool ThreatActive;
        public AimCone Aim;

        public bool HasAnchor;
        public Vector3 Anchor;
        /// <summary>Full marks within Leash metres of the anchor, nothing beyond twice that.</summary>
        public float Leash;

        /// <summary>Cells other ghosts are heading for; the first ReservedCount entries are used.</summary>
        public int[] Reserved;
        public int ReservedCount;
        public float CrowdRadius;

        /// <summary>
        /// The player is cornered: the only cells behind them are inside the walls, so a wall's surface cell
        /// (solid, floor beside it) may be chosen too. A ghost can wait there; the player cannot get round it.
        /// </summary>
        public bool AllowWallSkin;
    }

    /// <summary>Each term of a cell's score (all 0..1) and the weighted total, for the overlay and the viva.</summary>
    public struct ScoreBreakdown
    {
        public float Behind, Dark, Unseen, Cover, Range, Home, Threat, Crowd;
        public float WalkingDistance;
        public float Total;

        public override string ToString() =>
            "behind " + Behind.ToString("F2") + "  dark " + Dark.ToString("F2") + "  unseen " + Unseen.ToString("F2") +
            "  cover " + Cover.ToString("F2") + "\nrange " + Range.ToString("F2") + " (" + WalkingDistance.ToString("F0") + " m)" +
            "  home " + Home.ToString("F2") + "  threat " + Threat.ToString("F2") + "  crowd " + Crowd.ToString("F2") +
            "\nscore " + Total.ToString("F2");
    }

    /// <summary>
    /// The utility score of a cell as a place for a ghost to be:
    ///
    ///   score = wBehind*behind + wDark*(1-light) + wUnseen*(1-watched) + wCover*besideWall
    ///         + wRange*rangeFit(walkingDistance) + wHome*nearAnchor - wThreat*inIceGunAim - wCrowd*nearOtherGhost
    ///
    /// A weighted sum of 0..1 considerations: each factor is visible, separately tunable and trades off
    /// smoothly against the others. Walking distance comes from the BFS field built from the believed cell,
    /// so "close" means close on foot, not through a wall. Plain C#, allocation-free.
    /// </summary>
    public sealed class PositionScorer
    {
        readonly InfluenceMap map;
        readonly GhostGrid grid;
        BfsField walkField;

        /// <summary>The decision being scored. Set it, then call Score.</summary>
        public PositionQuery Query;

        public PositionScorer(InfluenceMap map)
        {
            this.map = map;
            grid = map.Grid;
        }

        public InfluenceMap Map => map;

        /// <summary>The player's walking-distance field, built by the caller from the believed cell.</summary>
        public BfsField WalkField
        {
            get => walkField;
            set => walkField = value;
        }

        /// <summary>
        /// A cell a ghost may pick as a position: inside the house, not inside a wall (except a wall's surface
        /// when the player is cornered), and not the cell another ghost is already heading for.
        /// </summary>
        public bool IsCandidate(int cell)
        {
            if (cell < 0 || !grid.Is(cell, CellFlags.Inside)) return false;
            if (grid.Is(cell, CellFlags.Solid) && !(Query.AllowWallSkin && grid.IsWallSkin(cell))) return false;
            return !IsReserved(cell);
        }

        /// <summary>Is another ghost already heading for exactly this cell? (A hard claim; nearby cells only crowd.)</summary>
        public bool IsReserved(int cell)
        {
            if (Query.Reserved == null) return false;
            for (int i = 0; i < Query.ReservedCount; i++)
                if (Query.Reserved[i] == cell) return true;
            return false;
        }

        public float Score(int cell) => Explain(cell).Total;

        public ScoreBreakdown Explain(int cell)
        {
            var b = new ScoreBreakdown();
            ScoreWeights w = Query.Weights;
            Vector3 centre = grid.CellCentre(cell);

            // Inside a wall (a candidate only when the player is cornered) the ghost is hidden outright.
            bool inWall = grid.Is(cell, CellFlags.Solid);
            b.Behind = Behind(centre);
            b.Dark = inWall ? 1f : 1f - map.Light(cell);
            b.Unseen = inWall ? 1f : 1f - map.Watched(cell, Query.Now);
            b.Cover = inWall ? 1f : BesideWall(cell);
            b.WalkingDistance = WalkingDistance(cell);
            b.Range = Query.Range.Fit(b.WalkingDistance);
            b.Home = NearAnchor(centre);
            b.Threat = InAim(cell, centre);
            b.Crowd = NearOtherGhost(centre);

            b.Total = w.behind * b.Behind + w.dark * b.Dark + w.unseen * b.Unseen + w.cover * b.Cover
                    + w.range * b.Range + w.home * b.Home - w.threat * b.Threat - w.crowd * b.Crowd;
            return b;
        }

        // ---------------------------------------------------------------- terms

        // 1 directly behind the last seen look direction, 0.5 to the side, 0 straight ahead.
        float Behind(Vector3 centre)
        {
            if (!Query.HasLook) return 0.5f;
            float dx = centre.x - Query.PlayerEye.x, dz = centre.z - Query.PlayerEye.z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            Vector3 look = Query.LookDirection;
            float lookLength = Mathf.Sqrt(look.x * look.x + look.z * look.z);
            if (length < 1e-3f || lookLength < 1e-3f) return 0.5f;
            float facing = (dx * look.x + dz * look.z) / (length * lookLength);
            return (1f - facing) * 0.5f;
        }

        // Solid cells among the 8 around: a wall to slip into. Three or more counts as full cover.
        float BesideWall(int cell)
        {
            grid.Coords(cell, out int cx, out int layer, out int cz);
            int solid = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int x = cx + dx, z = cz + dz;
                if (grid.InBounds(x, layer, z) && grid.Is(grid.Index(x, layer, z), CellFlags.Solid)) solid++;
            }
            return Mathf.Min(1f, solid / 3f);
        }

        // From the BFS field. A cell the player cannot stand on (ghost-only space) takes its nearest
        // walkable neighbour's distance plus one cell.
        float WalkingDistance(int cell)
        {
            if (walkField == null) return float.PositiveInfinity;
            float metres = walkField.WalkingDistance(cell);
            if (!float.IsPositiveInfinity(metres)) return metres;

            grid.Coords(cell, out int cx, out int layer, out int cz);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (!grid.InBounds(x, layer, z)) continue;
                float near = walkField.WalkingDistance(grid.Index(x, layer, z));
                if (near < metres) metres = near;
            }
            return float.IsPositiveInfinity(metres) ? metres : metres + grid.CellSize;
        }

        float NearAnchor(Vector3 centre)
        {
            if (!Query.HasAnchor) return 0f;
            float leash = Mathf.Max(Query.Leash, 0.1f);
            float distance = Vector3.Distance(centre, Query.Anchor);
            return Mathf.Clamp01(2f - distance / leash);    // 1 within the leash, 0 at twice it
        }

        // In the cone, in range, and with no wall between the player and the cell on that storey.
        float InAim(int cell, Vector3 centre)
        {
            if (!Query.ThreatActive || !Query.Aim.Contains(centre)) return 0f;
            int from = grid.CellAt(Query.Aim.Origin);
            if (from < 0) return 1f;
            grid.Coords(from, out int fx, out int fl, out int fz);
            grid.Coords(cell, out int x, out int layer, out int z);
            if (layer != fl) return 0f;                       // a floor in between
            if (fx == x && fz == z) return 1f;
            return grid.LineOfSight(fx, fz, x, z, layer) ? 1f : 0f;
        }

        float NearOtherGhost(Vector3 centre)
        {
            if (Query.Reserved == null || Query.CrowdRadius <= 0f) return 0f;
            float worst = 0f;
            for (int i = 0; i < Query.ReservedCount; i++)
            {
                int other = Query.Reserved[i];
                if (other < 0) continue;
                float distance = Vector3.Distance(centre, grid.CellCentre(other));
                worst = Mathf.Max(worst, 1f - distance / Query.CrowdRadius);
            }
            return worst;
        }
    }
}
