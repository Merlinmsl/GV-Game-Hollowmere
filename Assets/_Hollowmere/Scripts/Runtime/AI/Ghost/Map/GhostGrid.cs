using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        Inside = 1,     // part of the house: ghosts may be here
        Solid = 2,      // inside a wall: ghosts pass, the player and light do not
        Walkable = 4,   // the player can stand here (on the NavMesh)
    }

    /// <summary>
    /// A regular 3D grid of cells over one house. x and z run across a storey; each storey is one
    /// layer, so a two-floor house is two stacked sheets of cells. Plain C#: no Unity objects, so the
    /// brain, the searches and the tests can all use it.
    /// </summary>
    public sealed class GhostGrid
    {
        /// <summary>A cell's centre sits this far above its floor: about a ghost's chest.</summary>
        public const float BodyHeight = 1f;

        public readonly Vector3 Origin;      // minimum corner of the grid (y unused; floors give heights)
        public readonly float CellSize;
        public readonly int SizeX, SizeZ, Layers;
        public readonly float[] FloorY;      // height of each storey's floor
        public readonly CellFlags[] Flags;
        /// <summary>Height the player walks at in each walkable cell (a stair step can be above the floor).</summary>
        public readonly float[] WalkY;

        /// <summary>Highest step up or down between neighbouring walkable cells: a stair, not a ledge.</summary>
        public const float MaxStep = 0.75f;

        public int Count => Flags.Length;
        public int LayerSize => SizeX * SizeZ;

        public GhostGrid(Vector3 origin, float cellSize, int sizeX, int sizeZ, float[] floorY,
                         CellFlags[] flags = null, float[] walkY = null)
        {
            if (sizeX <= 0 || sizeZ <= 0 || floorY == null || floorY.Length == 0)
                throw new ArgumentException("A grid needs at least one cell and one storey.");

            Origin = origin;
            CellSize = cellSize;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Layers = floorY.Length;
            FloorY = floorY;
            Flags = flags ?? new CellFlags[sizeX * sizeZ * Layers];
            if (Flags.Length != sizeX * sizeZ * Layers)
                throw new ArgumentException("Flags do not match the grid size.");

            WalkY = walkY ?? new float[Flags.Length];
            if (walkY == null)
                for (int i = 0; i < WalkY.Length; i++) WalkY[i] = floorY[i / LayerSize];
            if (WalkY.Length != Flags.Length)
                throw new ArgumentException("Walk heights do not match the grid size.");
        }

        /// <summary>
        /// Can the player step from one walkable cell to a neighbouring one? Both must be walkable and
        /// within a stair step of each other in height, which is also how stairs link two storeys.
        /// </summary>
        public bool CanStep(int from, int to) =>
            Is(to, CellFlags.Walkable) && Math.Abs(WalkY[to] - WalkY[from]) <= MaxStep;

        // ---------------------------------------------------------------- indexing

        public int Index(int x, int layer, int z) => (layer * SizeZ + z) * SizeX + x;

        public void Coords(int index, out int x, out int layer, out int z)
        {
            x = index % SizeX;
            int rest = index / SizeX;
            z = rest % SizeZ;
            layer = rest / SizeZ;
        }

        public bool InBounds(int x, int layer, int z) =>
            x >= 0 && x < SizeX && z >= 0 && z < SizeZ && layer >= 0 && layer < Layers;

        public bool Is(int index, CellFlags flag) => (Flags[index] & flag) != 0;

        // ---------------------------------------------------------------- world <-> cell

        public Vector3 CellCentre(int index)
        {
            Coords(index, out int x, out int layer, out int z);
            return new Vector3(Origin.x + (x + 0.5f) * CellSize,
                               FloorY[layer] + BodyHeight,
                               Origin.z + (z + 0.5f) * CellSize);
        }

        /// <summary>The storey a height belongs to: the highest floor at or just below it.</summary>
        public int LayerAt(float y)
        {
            int layer = 0;
            for (int i = 1; i < Layers; i++)
                if (y >= FloorY[i] - 0.5f) layer = i;
            return layer;
        }

        /// <summary>The cell containing a world point, or -1 outside the grid.</summary>
        public int CellAt(Vector3 point)
        {
            int x = Mathf.FloorToInt((point.x - Origin.x) / CellSize);
            int z = Mathf.FloorToInt((point.z - Origin.z) / CellSize);
            int layer = LayerAt(point.y);
            return InBounds(x, layer, z) ? Index(x, layer, z) : -1;
        }

        /// <summary>
        /// The walkable cell nearest to a cell, searching outward ring by ring on the same storey up to
        /// 'radius' cells. -1 when there is none. Used when a belief lands somewhere the player cannot stand.
        /// </summary>
        public int NearestWalkable(int cell, int radius = 3) => NearestWith(cell, CellFlags.Walkable, radius);

        /// <summary>The nearest cell carrying 'flag', ring by ring on the same storey up to 'radius'; -1 when none.</summary>
        public int NearestWith(int cell, CellFlags flag, int radius)
        {
            if (cell < 0) return -1;
            if (Is(cell, flag)) return cell;
            Coords(cell, out int cx, out int layer, out int cz);
            for (int r = 1; r <= radius; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;   // only the ring itself
                int x = cx + dx, z = cz + dz;
                if (!InBounds(x, layer, z)) continue;
                int candidate = Index(x, layer, z);
                if (Is(candidate, flag)) return candidate;
            }
            return -1;
        }

        /// <summary>Walkable cells among the 8 around a cell on its storey: 8 in the open, 3 in a corner.</summary>
        public int OpenNeighbours(int cell)
        {
            Coords(cell, out int cx, out int layer, out int cz);
            int open = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dz != 0) && InBounds(cx + dx, layer, cz + dz) && Is(Index(cx + dx, layer, cz + dz), CellFlags.Walkable))
                    open++;
            }
            return open;
        }

        /// <summary>Is this a wall cell's surface: solid, with walkable floor on one of its four sides?</summary>
        public bool IsWallSkin(int cell)
        {
            if (!Is(cell, CellFlags.Solid)) return false;
            Coords(cell, out int cx, out int layer, out int cz);
            return (InBounds(cx + 1, layer, cz) && Is(Index(cx + 1, layer, cz), CellFlags.Walkable))
                || (InBounds(cx - 1, layer, cz) && Is(Index(cx - 1, layer, cz), CellFlags.Walkable))
                || (InBounds(cx, layer, cz + 1) && Is(Index(cx, layer, cz + 1), CellFlags.Walkable))
                || (InBounds(cx, layer, cz - 1) && Is(Index(cx, layer, cz - 1), CellFlags.Walkable));
        }

        /// <summary>Like CellAt, but a point outside the grid snaps to the nearest edge cell.</summary>
        public int ClampedCellAt(Vector3 point)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x - Origin.x) / CellSize), 0, SizeX - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((point.z - Origin.z) / CellSize), 0, SizeZ - 1);
            return Index(x, LayerAt(point.y), z);
        }

        // ---------------------------------------------------------------- line of sight

        /// <summary>
        /// Can light or a look travel between two cells of the same storey? Walks the cells on the line
        /// between them (Bresenham) and fails at the first solid one. The end cells themselves are not
        /// tested, so a viewer standing next to a wall still sees along it. Cost: one step per cell crossed.
        /// </summary>
        public bool LineOfSight(int fromX, int fromZ, int toX, int toZ, int layer)
        {
            int dx = Math.Abs(toX - fromX), dz = Math.Abs(toZ - fromZ);
            int stepX = fromX < toX ? 1 : -1, stepZ = fromZ < toZ ? 1 : -1;
            int error = dx - dz;
            int x = fromX, z = fromZ;

            while (true)
            {
                int doubled = 2 * error;
                if (doubled > -dz) { error -= dz; x += stepX; }
                if (doubled < dx) { error += dx; z += stepZ; }
                if (x == toX && z == toZ) return true;
                if (Is(Index(x, layer, z), CellFlags.Solid)) return false;
            }
        }
    }
}
