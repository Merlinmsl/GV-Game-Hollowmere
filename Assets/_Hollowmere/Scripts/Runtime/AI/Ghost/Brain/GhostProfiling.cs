using Unity.Profiling;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Named profiler markers round each stage of a ghost's work. They show as rows in the Unity Profiler
    /// and are read at runtime by GhostProfilerHud (ProfilerRecorder), so the cost of every stage can be
    /// measured in play without a Stopwatch in the code. Free when nothing is recording; never allocate.
    /// </summary>
    public static class GhostProfiling
    {
        /// <summary>One whole thought: senses, mode choice and whatever the mode needs. Contains the stages below.</summary>
        public static readonly ProfilerMarker Think = new ProfilerMarker("Ghost.Think");
        /// <summary>Sight test plus marking what the player looks at on the influence map.</summary>
        public static readonly ProfilerMarker Sense = new ProfilerMarker("Ghost.Sense");
        /// <summary>BFS walking-distance field from the believed player cell.</summary>
        public static readonly ProfilerMarker WalkField = new ProfilerMarker("Ghost.WalkField");
        /// <summary>Utility score optimised by hill climbing.</summary>
        public static readonly ProfilerMarker Position = new ProfilerMarker("Ghost.Position");
        /// <summary>BFS to the nearest safe wall cell (flee and lurk).</summary>
        public static readonly ProfilerMarker WallSearch = new ProfilerMarker("Ghost.WallSearch");
        /// <summary>A* slices and path smoothing, run every frame under the shared node budget.</summary>
        public static readonly ProfilerMarker Route = new ProfilerMarker("Ghost.Route");

        public static readonly string[] Names =
            { "Ghost.Think", "Ghost.Sense", "Ghost.WalkField", "Ghost.Position", "Ghost.WallSearch", "Ghost.Route" };
    }
}
