using Hollowmere.AI.Ghost;
using UnityEngine;

namespace Hollowmere.Tests.Ghost
{
    /// <summary>
    /// Builds small grids from ASCII floor plans, north row first:
    ///   '.' floor the player can walk on   '#' wall (ghosts only)   'o' inside, ghost-only space   ' ' outside
    /// Cells are 1 m, one storey with its floor at y = 0.
    /// </summary>
    internal static class TestGrids
    {
        public static GhostGrid FromRows(params string[] rows)
        {
            int sizeZ = rows.Length, sizeX = rows[0].Length;
            var grid = new GhostGrid(Vector3.zero, 1f, sizeX, sizeZ, new[] { 0f });
            for (int row = 0; row < sizeZ; row++)
            for (int x = 0; x < sizeX; x++)
            {
                int cell = Cell(grid, x, row);
                switch (rows[row][x])
                {
                    case '.': grid.Flags[cell] = CellFlags.Inside | CellFlags.Walkable; break;
                    case '#': grid.Flags[cell] = CellFlags.Inside | CellFlags.Solid; break;
                    case 'o': grid.Flags[cell] = CellFlags.Inside; break;
                    default: grid.Flags[cell] = CellFlags.None; break;
                }
            }
            return grid;
        }

        /// <summary>The cell at column x of the given ASCII row (row 0 is the top, north, line).</summary>
        public static int Cell(GhostGrid grid, int x, int row) => grid.Index(x, 0, grid.SizeZ - 1 - row);

        /// <summary>Moves between two cells when every cell is open: the larger of the axis gaps (8/26 neighbours).</summary>
        public static int Chebyshev(GhostGrid grid, int a, int b)
        {
            grid.Coords(a, out int ax, out int al, out int az);
            grid.Coords(b, out int bx, out int bl, out int bz);
            return Mathf.Max(Mathf.Abs(ax - bx), Mathf.Max(Mathf.Abs(al - bl), Mathf.Abs(az - bz)));
        }
    }
}
