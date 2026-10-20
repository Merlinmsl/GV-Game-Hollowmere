using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Turns a house's geometry into a GhostGrid and its baked light. Uses physics and the NavMesh, so it
    /// runs once when baking (or when the generator brings the lights back), never during a brain tick.
    /// </summary>
    public static class GridBaker
    {
        public struct Settings
        {
            public Bounds Bounds;          // the house, in world space
            public float CellSize;
            public float[] FloorY;         // world height of each storey's floor
            public LayerMask SolidMask;    // walls and floors
            public LayerMask LightBlockers;
        }

        // A wall counts when it overlaps most of a cell; a box the full cell size would also catch walls
        // that only graze it and close one-cell doorways.
        const float SolidBoxShare = 0.45f;
        // How close to a cell's floor the NavMesh must be for the player to stand there.
        const float WalkableTolerance = 0.6f;
        // Lamp shades and bulbs have colliders of their own: the light ray starts outside them.
        const float LampSkin = 0.4f;

        static readonly Collider[] overlaps = new Collider[32];
        static readonly RaycastHit[] hits = new RaycastHit[16];

        // Only the building counts: the player, agents and physics props (doors, crates) are not walls.
        static bool IsStructure(Collider collider) =>
            !(collider is CharacterController) &&
            (collider.attachedRigidbody == null || collider.attachedRigidbody.isKinematic);

        static bool OverlapsStructure(Vector3 centre, Vector3 halfExtents, LayerMask mask)
        {
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlaps, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (IsStructure(overlaps[i])) return true;
            return false;
        }

        static bool StructureAlong(Vector3 origin, Vector3 direction, float distance, LayerMask mask)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (IsStructure(hits[i].collider)) return true;
            return false;
        }

        public static GhostGrid BakeGrid(in Settings s)
        {
            // Objects moved or created since the last physics step are not in the physics scene yet.
            Physics.SyncTransforms();

            int sizeX = Mathf.Max(1, Mathf.CeilToInt(s.Bounds.size.x / s.CellSize));
            int sizeZ = Mathf.Max(1, Mathf.CeilToInt(s.Bounds.size.z / s.CellSize));
            var origin = new Vector3(s.Bounds.min.x, 0f, s.Bounds.min.z);
            var grid = new GhostGrid(origin, s.CellSize, sizeX, sizeZ, (float[])s.FloorY.Clone());

            var humanoid = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            var halfBox = new Vector3(SolidBoxShare * s.CellSize, SolidBoxShare, SolidBoxShare * s.CellSize);
            float half = 0.5f * s.CellSize;

            for (int i = 0; i < grid.Count; i++)
            {
                grid.Coords(i, out _, out int layer, out _);
                Vector3 centre = grid.CellCentre(i);
                var flags = CellFlags.None;

                if (OverlapsStructure(centre, halfBox, s.SolidMask))
                    flags |= CellFlags.Solid;

                var floorPoint = new Vector3(centre.x, grid.FloorY[layer], centre.z);
                if (NavMesh.SamplePosition(floorPoint, out NavMeshHit hit, WalkableTolerance, humanoid)
                    && Mathf.Abs(hit.position.x - centre.x) <= half && Mathf.Abs(hit.position.z - centre.z) <= half)
                    flags |= CellFlags.Walkable;

                // Part of the house when there is a wall here, the player can stand here, or a floor is
                // just below: open air outside the walls or over a stairwell is not.
                bool floorBelow = StructureAlong(centre, Vector3.down, GhostGrid.BodyHeight + 0.3f, s.SolidMask);
                if (flags != CellFlags.None || floorBelow) flags |= CellFlags.Inside;

                grid.Flags[i] = flags;
            }
            return grid;
        }

        /// <summary>
        /// Light from the lamps: each lamp adds (1 - d/range)^2, scaled by its intensity relative to the
        /// brightest lamp, only where a ray from the lamp reaches the cell. Returns how many lamps were used.
        /// </summary>
        public static int BakeLight(InfluenceMap map, IReadOnlyList<Light> lamps, LayerMask blockers)
        {
            GhostGrid grid = map.Grid;
            float brightest = 0f;
            for (int l = 0; l < lamps.Count; l++)
                if (IsUsable(lamps[l])) brightest = Mathf.Max(brightest, lamps[l].intensity);

            int used = 0;
            for (int l = 0; l < lamps.Count; l++) if (IsUsable(lamps[l])) used++;

            for (int i = 0; i < grid.Count; i++)
            {
                if (!grid.Is(i, CellFlags.Inside) || grid.Is(i, CellFlags.Solid)) { map.SetBakedLight(i, 0f); continue; }

                Vector3 centre = grid.CellCentre(i);
                float sum = 0f;
                for (int l = 0; l < lamps.Count; l++)
                {
                    Light lamp = lamps[l];
                    if (!IsUsable(lamp)) continue;
                    Vector3 from = lamp.transform.position;
                    float distance = Vector3.Distance(from, centre);
                    if (distance >= lamp.range || !Reaches(from, centre, distance, blockers)) continue;

                    float falloff = 1f - distance / lamp.range;
                    sum += falloff * falloff * (brightest > 0f ? lamp.intensity / brightest : 1f);
                }
                map.SetBakedLight(i, sum);
            }
            return used;
        }

        static bool IsUsable(Light lamp) =>
            lamp != null && lamp.isActiveAndEnabled && (lamp.type == LightType.Point || lamp.type == LightType.Spot);

        static bool Reaches(Vector3 from, Vector3 to, float distance, LayerMask blockers)
        {
            if (distance <= LampSkin) return true;
            Vector3 direction = (to - from) / distance;
            return !StructureAlong(from + direction * LampSkin, direction, distance - LampSkin - 0.3f, blockers);
        }
    }
}
