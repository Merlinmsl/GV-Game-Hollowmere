using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// What a metre of ghost travel costs in each cell. A ghost would rather not be seen on the way, so
    /// a lit or watched cell costs more than its length, and a cell inside a wall (where it fades out)
    /// costs less:
    ///
    ///   cost(step into cell) = distance * (1 + kWatched*watched + kLight*light) * (solid ? solidFactor : 1)
    /// </summary>
    [Serializable]
    public struct RouteCosts
    {
        [Tooltip("Extra cost per metre in a cell the player is watching (x watched, 0..1).")]
        public float kWatched;
        [Tooltip("Extra cost per metre in a lit cell (x light, 0..1).")]
        public float kLight;
        [Tooltip("Cost multiplier inside a wall, below 1: walls are the ghost's hidden corridors.")]
        [Range(0.1f, 1f)] public float solidFactor;

        public static RouteCosts Default => new RouteCosts { kWatched = 3f, kLight = 2f, solidFactor = 0.7f };

        /// <summary>Plain length only: every cell costs its distance (for tests and comparisons).</summary>
        public static RouteCosts Shortest => new RouteCosts { kWatched = 0f, kLight = 0f, solidFactor = 1f };

        /// <summary>
        /// The least any metre can cost. The A* heuristic is straight-line distance times this, so it
        /// never overestimates the real cost (admissible) and A* still returns the cheapest route.
        /// </summary>
        public float CheapestPerMetre => Mathf.Min(1f, solidFactor);

        /// <summary>Exposure multiplier of a cell: 1 + kWatched*watched + kLight*light, times the wall discount.</summary>
        public float PerMetre(InfluenceMap map, int cell, float now)
        {
            float exposure = 1f + kWatched * map.Watched(cell, now) + kLight * map.Light(cell);
            return map.Grid.Is(cell, CellFlags.Solid) ? exposure * solidFactor : exposure;
        }
    }
}
