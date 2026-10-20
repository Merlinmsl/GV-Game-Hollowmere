using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// What the ghosts know about the house, cell by cell:
    ///  - light: how lit a cell is, 0..1. Lamps are baked once; the flashlight cone is added on top.
    ///  - watched: 1 where the player has just looked, fading to 0 over WatchFadeSeconds.
    /// Watched is stored as the TIME each cell was last looked at and turned into a value on demand,
    /// so fading costs nothing: no loop over the grid, only the cells in the view cone are written.
    /// Plain C#, allocation-free after construction.
    /// </summary>
    public sealed class InfluenceMap
    {
        public const float WatchFadeSeconds = 8f;

        const float NeverSeen = -1e6f;

        public readonly GhostGrid Grid;

        readonly float[] bakedLight;
        readonly float[] flashLight;
        readonly float[] watchedAt;
        readonly int[] flashCells;        // cells the flashlight lit last time, so they can be cleared
        int flashCount;

        /// <summary>Cells written by the last view update, for the profiler and the debug panel.</summary>
        public int LastViewCellsWritten { get; private set; }

        public InfluenceMap(GhostGrid grid, float[] bakedLight = null)
        {
            Grid = grid;
            this.bakedLight = bakedLight ?? new float[grid.Count];
            flashLight = new float[grid.Count];
            watchedAt = new float[grid.Count];
            flashCells = new int[grid.Count];
            ResetWatched();
        }

        // ---------------------------------------------------------------- reading

        /// <summary>Lamps plus flashlight, 0..1.</summary>
        public float Light(int cell) => Mathf.Min(1f, bakedLight[cell] + flashLight[cell]);

        public float BakedLight(int cell) => bakedLight[cell];

        /// <summary>1 where the player has just looked, falling linearly to 0 over WatchFadeSeconds.</summary>
        public float Watched(int cell, float now) =>
            Mathf.Clamp01(1f - (now - watchedAt[cell]) / WatchFadeSeconds);

        // ---------------------------------------------------------------- writing

        public void SetBakedLight(int cell, float value) => bakedLight[cell] = Mathf.Clamp01(value);

        public void ResetWatched()
        {
            for (int i = 0; i < watchedAt.Length; i++) watchedAt[i] = NeverSeen;
        }

        /// <summary>
        /// Marks the cells inside a view cone, on the viewer's storey, that the viewer can actually see.
        /// Only a square of radius 'range' around the viewer is visited, never the whole grid.
        /// </summary>
        public void MarkWatched(Vector3 eye, Vector3 look, float now, float range = 15f, float coneDegrees = 90f)
        {
            LastViewCellsWritten = VisitCone(eye, look, range, coneDegrees, now, 0f, false);
        }

        /// <summary>
        /// Replaces the flashlight's light with a new cone. Cells lit last time are cleared first, using
        /// the list kept from the previous call, so the cost is the cone's size, not the grid's.
        /// </summary>
        public void SetFlashlight(bool on, Vector3 origin, Vector3 direction, float range = 12f, float coneDegrees = 35f)
        {
            for (int i = 0; i < flashCount; i++) flashLight[flashCells[i]] = 0f;
            flashCount = 0;
            if (on) VisitCone(origin, direction, range, coneDegrees, 0f, 1f, true);
        }

        // Shared walk over a cone of cells: writes either 'watched at now' or flashlight light.
        int VisitCone(Vector3 origin, Vector3 direction, float range, float coneDegrees, float now, float strength, bool asLight)
        {
            int start = Grid.CellAt(origin);
            if (start < 0) return 0;

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return 0;
            direction.Normalize();
            float minDot = Mathf.Cos(coneDegrees * 0.5f * Mathf.Deg2Rad);

            Grid.Coords(start, out int ox, out int layer, out int oz);
            int radius = Mathf.CeilToInt(range / Grid.CellSize);
            float rangeSq = range * range;
            int written = 0;

            for (int z = Mathf.Max(0, oz - radius); z <= Mathf.Min(Grid.SizeZ - 1, oz + radius); z++)
            for (int x = Mathf.Max(0, ox - radius); x <= Mathf.Min(Grid.SizeX - 1, ox + radius); x++)
            {
                float dx = (x - ox) * Grid.CellSize, dz = (z - oz) * Grid.CellSize;
                float distSq = dx * dx + dz * dz;
                if (distSq > rangeSq) continue;

                int cell = Grid.Index(x, layer, z);
                if (!Grid.Is(cell, CellFlags.Inside)) continue;

                // The viewer's own cell and its neighbours count even when facing away: you notice
                // what is right beside you.
                if (distSq > 2.1f * Grid.CellSize * Grid.CellSize)
                {
                    float dist = Mathf.Sqrt(distSq);
                    if ((dx * direction.x + dz * direction.z) / dist < minDot) continue;
                    if (!Grid.LineOfSight(ox, oz, x, z, layer)) continue;
                }

                if (asLight)
                {
                    float falloff = 1f - Mathf.Sqrt(distSq) / range;
                    flashLight[cell] = Mathf.Max(flashLight[cell], strength * falloff);
                    flashCells[flashCount++] = cell;
                }
                else watchedAt[cell] = now;
                written++;
            }
            return written;
        }
    }
}
