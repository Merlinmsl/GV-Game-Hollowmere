using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Everything about how one ghost decides and moves that a designer may tune. Kept together so the
    /// Mara and servant profiles (Phase 7) can swap whole sets.
    /// </summary>
    [Serializable]
    public struct GhostTuning
    {
        public ScoreWeights weights;
        public RangeBand range;
        public HillClimber.Settings climb;
        public RouteCosts costs;

        [Tooltip("Seconds a sighting's look direction and weapon are still trusted.")]
        public float lookMemory;
        [Tooltip("Half-width of the ice gun's aim cone the ghost avoids, in degrees.")]
        public float iceGunHalfAngle;
        public float iceGunRange;
        [Tooltip("Other ghosts' cells closer than this count as crowding.")]
        public float crowdRadius;
        [Tooltip("Re-plan the route this often even when the target is the same, as the map changes.")]
        public float replanInterval;
        [Tooltip("Cells one route search may expand before settling for a partial route.")]
        public int maxRouteNodes;
        [Tooltip("A full position search runs when what the ghost knows changes, and at least this often; " +
                 "in between it only re-scores its current target.")]
        public float fullSearchInterval;

        public static GhostTuning Default => new GhostTuning
        {
            weights = ScoreWeights.Default,
            range = RangeBand.Stalk,
            climb = HillClimber.Settings.Default,
            costs = RouteCosts.Default,
            lookMemory = 4f,
            iceGunHalfAngle = 12f,
            iceGunRange = 15f,
            crowdRadius = 3f,
            replanInterval = 1f,
            maxRouteNodes = 4000,
            fullSearchInterval = 1f,
        };
    }
}
