using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Finds a high-scoring cell for a ghost without scoring the whole house: local search by
    /// steepest-ascent hill climbing from many starting points.
    ///  - Seeds: rings around the BELIEVED player position (3 rings x 8 = 24 by default, 3..12 m), where
    ///    good stalking cells are; plus the ghost's current target, so it can stay where it is.
    ///  - Random restarts: a few more seeds anywhere within reach, against the rings missing a pocket.
    ///  - Climb: from a seed, move to the best neighbouring cell until no neighbour is better (a local
    ///    maximum). Neighbours may lie beyond a thin wall, because a ghost passes through walls.
    ///  - Optional simulated annealing: random neighbours, accepting a worse one with probability
    ///    exp(delta / T) while T cools, to escape plateaus; the best cell seen is then polished by a climb.
    ///  - Hysteresis: the current target is kept unless the best is better by SwitchMargin.
    /// Each cell is scored at most once per search (stamp cache), and a seed already on an earlier climb's
    /// trail is skipped, since a greedy climb from it reaches the same peak. Allocation-free after construction.
    /// </summary>
    public sealed class HillClimber
    {
        [Serializable]
        public struct Settings
        {
            public int rings;
            public int seedsPerRing;
            public float innerRadius;
            public float outerRadius;
            public int randomRestarts;
            public float restartRadius;
            [Tooltip("Most moves one climb may make.")]
            public int maxSteps;
            [Tooltip("How many wall cells a neighbour step may pass through.")]
            public int wallSkip;
            [Tooltip("The current target is kept unless another cell scores this much higher.")]
            public float switchMargin;
            public bool useAnnealing;
            public int annealSteps;
            public float startTemperature;
            public float cooling;

            public static Settings Default => new Settings
            {
                rings = 3,
                seedsPerRing = 8,
                innerRadius = 3f,
                outerRadius = 12f,
                randomRestarts = 2,
                restartRadius = 12f,
                maxSteps = 24,
                wallSkip = 2,
                switchMargin = 0.15f,
                useAnnealing = false,
                annealSteps = 40,
                startTemperature = 0.6f,
                cooling = 0.9f,
            };
        }

        public struct Result
        {
            public int Cell;
            public float Score;
            public int Climbs;
            public int Evaluations;
            public bool KeptCurrent;
        }

        const float Improvement = 1e-4f;
        const int MaxNeighbours = 10;
        const int MaxSeeds = 64;
        const int MaxTrail = 2048;

        readonly PositionScorer scorer;
        readonly GhostGrid grid;
        readonly System.Random random;
        readonly float[] cachedScore;
        readonly int[] cacheStamp;
        readonly int[] climbedStamp;
        readonly int[] neighbours = new int[MaxNeighbours];
        int stamp;

        public Settings Config = Settings.Default;

        /// <summary>Record seeds, trails and peaks for the debug overlay.</summary>
        public bool RecordTrace;
        public readonly int[] TraceSeeds = new int[MaxSeeds];
        public readonly int[] TracePeaks = new int[MaxSeeds];
        public readonly int[] TraceTrail = new int[MaxTrail];
        public int TraceSeedCount, TracePeakCount, TraceTrailCount;

        /// <summary>Cells scored by the last search: its cost.</summary>
        public int Evaluations { get; private set; }

        public HillClimber(PositionScorer scorer, int randomSeed = 1234)
        {
            this.scorer = scorer;
            grid = scorer.Map.Grid;
            random = new System.Random(randomSeed);
            cachedScore = new float[grid.Count];
            cacheStamp = new int[grid.Count];
            climbedStamp = new int[grid.Count];
        }

        public PositionScorer Scorer => scorer;

        // ---------------------------------------------------------------- search

        /// <summary>
        /// The best cell found around the believed player position in scorer.Query. 'currentTarget' (or -1)
        /// is where this ghost is already heading: it is climbed from too and kept unless clearly beaten.
        /// </summary>
        public Result Search(int currentTarget)
        {
            BeginSearch();
            var result = new Result { Cell = -1, Score = float.NegativeInfinity };
            bool hasCurrent = scorer.IsCandidate(currentTarget);
            if (hasCurrent) Consider(currentTarget, ref result);

            Vector3 centre = scorer.Query.PlayerEye;
            int layer = grid.LayerAt(centre.y - PositionQuery.EyeHeight + 0.1f);
            Settings s = Config;

            for (int ring = 0; ring < s.rings; ring++)
            {
                float radius = s.rings == 1 ? s.innerRadius : Mathf.Lerp(s.innerRadius, s.outerRadius, ring / (float)(s.rings - 1));
                float turn = ring * Mathf.PI / Mathf.Max(1, s.seedsPerRing) / s.rings * 2f;   // stagger the rings
                for (int i = 0; i < s.seedsPerRing; i++)
                {
                    float angle = turn + i * Mathf.PI * 2f / s.seedsPerRing;
                    Consider(SeedAt(centre, layer, angle, radius), ref result);
                }
            }

            for (int i = 0; i < s.randomRestarts; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt((float)random.NextDouble()) * s.restartRadius;   // uniform over the disc
                Consider(SeedAt(centre, layer, angle, radius), ref result);
            }

            // Hysteresis: only switch away from the current target for a clearly better cell.
            if (hasCurrent && result.Cell != currentTarget)
            {
                float current = CachedScore(currentTarget);
                if (result.Score < current + s.switchMargin)
                {
                    result.Cell = currentTarget;
                    result.Score = current;
                    result.KeptCurrent = true;
                }
            }

            result.Evaluations = Evaluations;
            return result;
        }

        void Consider(int seed, ref Result result)
        {
            if (seed < 0) return;
            // A greedy climb from a cell already on a trail would reach the same peak: skip it.
            if (!Config.useAnnealing && climbedStamp[seed] == stamp) return;
            if (RecordTrace && TraceSeedCount < MaxSeeds) TraceSeeds[TraceSeedCount++] = seed;

            int peak = Config.useAnnealing ? Anneal(seed) : Climb(seed);
            result.Climbs++;
            if (RecordTrace && TracePeakCount < MaxSeeds) TracePeaks[TracePeakCount++] = peak;

            float score = CachedScore(peak);
            if (score > result.Score)
            {
                result.Cell = peak;
                result.Score = score;
            }
        }

        // ---------------------------------------------------------------- local search

        /// <summary>
        /// Steepest ascent from 'seed': repeatedly move to the best neighbour while it improves the score.
        /// Returns the local maximum reached (or where MaxSteps ran out). Uses the current search's cache;
        /// call BeginSearch first when using it on its own.
        /// </summary>
        public int Climb(int seed)
        {
            int cell = seed;
            float score = CachedScore(cell);
            MarkClimbed(cell);

            for (int step = 0; step < Config.maxSteps; step++)
            {
                int count = Neighbours(cell, neighbours);
                int best = -1;
                float bestScore = score + Improvement;
                for (int i = 0; i < count; i++)
                {
                    float s = CachedScore(neighbours[i]);
                    if (s > bestScore) { best = neighbours[i]; bestScore = s; }
                }
                if (best < 0) break;                  // a local maximum
                cell = best;
                score = bestScore;
                MarkClimbed(cell);
            }
            return cell;
        }

        // Simulated annealing: a random walk that accepts worse moves less and less often as T cools.
        int Anneal(int seed)
        {
            Settings s = Config;
            int cell = seed, bestCell = seed;
            float score = CachedScore(cell), bestScore = score;
            float temperature = Mathf.Max(s.startTemperature, 1e-3f);
            MarkClimbed(cell);

            for (int step = 0; step < s.annealSteps; step++)
            {
                int count = Neighbours(cell, neighbours);
                if (count == 0) break;
                int next = neighbours[random.Next(count)];
                float delta = CachedScore(next) - score;
                if (delta > 0f || random.NextDouble() < Math.Exp(delta / temperature))
                {
                    cell = next;
                    score += delta;
                    MarkClimbed(cell);
                    if (score > bestScore) { bestCell = cell; bestScore = score; }
                }
                temperature *= s.cooling;
            }
            return Climb(bestCell);                   // polish the best cell seen to its local maximum
        }

        /// <summary>
        /// A cell's neighbours for the climb: the 8 around it on its storey and the cells above and below.
        /// A neighbour inside a wall is looked past, up to WallSkip cells, to the open cell beyond: the climb
        /// moves the way a ghost does. Returns the count written into 'buffer'.
        /// </summary>
        public int Neighbours(int cell, int[] buffer)
        {
            grid.Coords(cell, out int cx, out int layer, out int cz);
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                for (int k = 1; k <= 1 + Config.wallSkip; k++)
                {
                    int x = cx + dx * k, z = cz + dz * k;
                    if (!grid.InBounds(x, layer, z)) break;
                    int n = grid.Index(x, layer, z);
                    if (!grid.Is(n, CellFlags.Inside)) break;           // the edge of the house
                    if (grid.Is(n, CellFlags.Solid)) continue;          // inside a wall: look beyond
                    buffer[count++] = n;
                    break;
                }
            }
            for (int dl = -1; dl <= 1; dl += 2)
            {
                if (!grid.InBounds(cx, layer + dl, cz)) continue;
                int n = grid.Index(cx, layer + dl, cz);
                if (scorer.IsCandidate(n)) buffer[count++] = n;
            }
            return count;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Starts a new search: clears the score cache (by stamp) and the trace.</summary>
        public void BeginSearch()
        {
            stamp++;
            Evaluations = 0;
            TraceSeedCount = TracePeakCount = TraceTrailCount = 0;
        }

        /// <summary>A cell's score, computed at most once per search.</summary>
        public float CachedScore(int cell)
        {
            if (cacheStamp[cell] != stamp)
            {
                cacheStamp[cell] = stamp;
                cachedScore[cell] = scorer.Score(cell);
                Evaluations++;
            }
            return cachedScore[cell];
        }

        void MarkClimbed(int cell)
        {
            climbedStamp[cell] = stamp;
            if (RecordTrace && TraceTrailCount < MaxTrail) TraceTrail[TraceTrailCount++] = cell;
        }

        // The candidate cell nearest to a point on a ring around the player, or -1.
        int SeedAt(Vector3 centre, int layer, float angle, float radius)
        {
            var point = new Vector3(centre.x + Mathf.Cos(angle) * radius, grid.FloorY[layer] + GhostGrid.BodyHeight,
                                    centre.z + Mathf.Sin(angle) * radius);
            int x = Mathf.FloorToInt((point.x - grid.Origin.x) / grid.CellSize);
            int z = Mathf.FloorToInt((point.z - grid.Origin.z) / grid.CellSize);
            if (!grid.InBounds(x, layer, z)) return -1;
            return NearestCandidate(grid.Index(x, layer, z), 2);
        }

        int NearestCandidate(int cell, int radius)
        {
            if (scorer.IsCandidate(cell)) return cell;
            grid.Coords(cell, out int cx, out int layer, out int cz);
            for (int r = 1; r <= radius; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int x = cx + dx, z = cz + dz;
                if (!grid.InBounds(x, layer, z)) continue;
                int candidate = grid.Index(x, layer, z);
                if (scorer.IsCandidate(candidate)) return candidate;
            }
            return -1;
        }
    }
}
