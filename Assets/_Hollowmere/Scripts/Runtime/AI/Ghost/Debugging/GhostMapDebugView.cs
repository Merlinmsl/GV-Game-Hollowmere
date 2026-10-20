using System.Diagnostics;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// F2 shows the ghosts' influence map as a heatmap on the floor of the storey the player is on;
    /// F3 cycles what it shows. One mesh with a quad per cell, recoloured only while visible, so it
    /// costs nothing when hidden.
    ///
    /// "Feed from player" is a SANDBOX stand-in until GhostBody feeds the map from its own senses
    /// (Phase 6): it marks the player's real view and flashlight so the map can be seen working.
    /// The Score view runs the position search as if a ghost believed the player were exactly where
    /// they are, and paints the score, the hill-climb seeds, trails and peaks, and the chosen cell.
    /// </summary>
    public sealed class GhostMapDebugView : MonoBehaviour
    {
        public enum View { Exposure, Light, Watched, Cells, WalkDistance, Score }

        const int ViewCount = 6;
        // Walking distance shown as a colour ramp up to this many metres.
        const float DistanceScale = 25f;

        [SerializeField] GhostHouseMap house;
        [Tooltip("Show the heatmap from the start. F2 toggles it.")]
        [SerializeField] bool visible;
        [SerializeField] View view = View.Exposure;
        [Tooltip("SANDBOX ONLY: mark the debug player's view and flashlight directly, until the ghosts feed the map from their senses.")]
        [SerializeField] bool feedFromPlayer = true;
        [SerializeField] float feedInterval = 0.2f;
        [SerializeField, Range(0.05f, 1f)] float opacity = 0.5f;

        [Header("Score view (sandbox tuning until the ghost profiles in Phase 7)")]
        [SerializeField] ScoreWeights weights = ScoreWeights.Default;
        [SerializeField] RangeBand range = RangeBand.Stalk;
        [SerializeField] HillClimber.Settings climb = HillClimber.Settings.Default;
        [SerializeField] float iceGunHalfAngle = 12f;
        [SerializeField] float iceGunRange = 15f;
        [SerializeField] float searchInterval = 0.25f;

        const float LiftAboveFloor = 0.06f;

        // Overlay marks for the Score view, per cell of the shown storey.
        const byte MarkTrail = 1, MarkSeed = 2, MarkPeak = 3, MarkBest = 4;

        readonly Stopwatch watch = new Stopwatch();
        InfluenceMap map;
        Mesh mesh;
        Material material;
        Color32[] colours;
        int meshLayer = -1;
        float nextFeed;
        float lastFeedMs;
        BfsField walkField;
        float lastFieldMs;
        PositionScorer scorer;
        HillClimber climber;
        HillClimber.Result lastSearch;
        ScoreBreakdown bestBreakdown;
        readonly int[] bestCell = new int[1];
        float lastSearchMs;
        float nextSearch;
        float[] layerScore;
        byte[] marks;
        float scoreMin, scoreMax;
        int candidates;

        public bool Visible => visible;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f2Key.wasPressedThisFrame) visible = !visible;
                if (visible && keyboard.f3Key.wasPressedThisFrame) view = (View)(((int)view + 1) % ViewCount);
            }

            if (house == null || !house.IsBaked) return;
            map = house.Runtime;

            if (feedFromPlayer) Feed();
            if (visible) Draw();
        }

        void Feed()
        {
            if (Time.time < nextFeed) return;
            nextFeed = Time.time + feedInterval;
            IPlayerState player = PlayerRegistry.Current;
            if (player == null) return;

            watch.Restart();
            Vector3 eye = player.Position + Vector3.up * 1.6f;
            map.MarkWatched(eye, player.LookDirection, Time.time);
            map.SetFlashlight(player.FlashlightOn, eye, player.LookDirection);
            watch.Stop();
            lastFeedMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        void Draw()
        {
            GhostGrid grid = map.Grid;
            int layer = ShownLayer(grid);
            if (mesh == null || layer != meshLayer) BuildMesh(grid, layer);
            if (view == View.WalkDistance || view == View.Score) UpdateWalkField(grid);
            if (view == View.Score) UpdateSearch(grid, layer);

            float now = Time.time;
            int offset = layer * grid.LayerSize;
            for (int c = 0; c < grid.LayerSize; c++)
            {
                Color32 colour = CellColour(offset + c, now);
                int v = c * 4;
                colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = colour;
            }
            mesh.SetColors(colours);
            Graphics.RenderMesh(new RenderParams(material), mesh, 0, Matrix4x4.identity);
        }

        Color32 CellColour(int cell, float now)
        {
            GhostGrid grid = map.Grid;
            if (!grid.Is(cell, CellFlags.Inside)) return new Color32(0, 0, 0, 0);
            byte alpha = (byte)(opacity * 255f);
            if (grid.Is(cell, CellFlags.Solid) && view != View.Cells) return new Color32(40, 40, 40, alpha);

            switch (view)
            {
                case View.Light:
                    return Color32.Lerp(new Color32(10, 10, 40, alpha), new Color32(255, 230, 80, alpha), map.Light(cell));
                case View.Watched:
                    return Color32.Lerp(new Color32(20, 40, 120, alpha), new Color32(255, 40, 40, alpha), map.Watched(cell, now));
                case View.Cells:
                    if (grid.Is(cell, CellFlags.Solid)) return new Color32(90, 90, 90, alpha);
                    if (grid.Is(cell, CellFlags.Walkable)) return new Color32(60, 200, 90, alpha);
                    return new Color32(70, 120, 255, alpha);          // ghost-only space
                case View.WalkDistance:
                    // BFS walking distance from the player: white nearby, through orange, to purple far away.
                    float metres = walkField != null ? walkField.WalkingDistance(cell) : float.PositiveInfinity;
                    if (float.IsPositiveInfinity(metres)) return new Color32(25, 25, 25, alpha);
                    float t = Mathf.Clamp01(metres / DistanceScale);
                    return t < 0.5f
                        ? Color32.Lerp(new Color32(255, 255, 255, alpha), new Color32(255, 140, 30, alpha), t * 2f)
                        : Color32.Lerp(new Color32(255, 140, 30, alpha), new Color32(90, 30, 160, alpha), (t - 0.5f) * 2f);
                case View.Score:
                    return ScoreColour(cell - meshLayer * grid.LayerSize, alpha);
                default:
                    // What a ghost avoids: lit or watched. Green is a good hiding place, red is exposed.
                    float exposure = Mathf.Max(map.Light(cell), map.Watched(cell, now));
                    return Color32.Lerp(new Color32(40, 200, 80, alpha), new Color32(255, 50, 40, alpha), exposure);
            }
        }

        // Rebuilds the walking-distance field from the player's cell, only when it moved more than one cell.
        void UpdateWalkField(GhostGrid grid)
        {
            IPlayerState player = PlayerRegistry.Current;
            if (player == null) return;
            if (walkField == null || walkField.Grid != grid) walkField = new BfsField(grid);

            int cell = grid.CellAt(player.Position + Vector3.up * 0.1f);
            if (!walkField.NeedsRebuild(grid.NearestWalkable(cell))) return;
            watch.Restart();
            walkField.BuildWalkingField(cell);
            watch.Stop();
            lastFieldMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        Color32 ScoreColour(int layerCell, byte alpha)
        {
            if (marks == null) return new Color32(25, 25, 25, alpha);
            switch (marks[layerCell])
            {
                case MarkBest: return new Color32(255, 0, 255, 255);
                case MarkPeak: return new Color32(255, 230, 0, 230);
                case MarkSeed: return new Color32(0, 230, 255, 230);
                case MarkTrail: return new Color32(255, 255, 255, 200);
            }
            if (float.IsNegativeInfinity(layerScore[layerCell])) return new Color32(25, 25, 25, alpha);
            float t = scoreMax > scoreMin ? (layerScore[layerCell] - scoreMin) / (scoreMax - scoreMin) : 0f;
            return Color32.Lerp(new Color32(30, 20, 90, alpha), new Color32(60, 230, 90, alpha), t);
        }

        // Runs the ghost position search from the player's real cell (sandbox stand-in for a belief),
        // then scores the shown storey for the colour ramp. Throttled: a few searches a second.
        void UpdateSearch(GhostGrid grid, int layer)
        {
            IPlayerState player = PlayerRegistry.Current;
            if (player == null || walkField == null || Time.time < nextSearch) return;
            nextSearch = Time.time + searchInterval;

            if (scorer == null || scorer.Map != map)
            {
                scorer = new PositionScorer(map);
                climber = new HillClimber(scorer) { RecordTrace = true };
                lastSearch = new HillClimber.Result { Cell = -1 };
                layerScore = new float[grid.LayerSize];
                marks = new byte[grid.LayerSize];
            }

            Vector3 eye = player.Position + Vector3.up * PositionQuery.EyeHeight;
            scorer.WalkField = walkField;
            scorer.Query = new PositionQuery
            {
                PlayerEye = eye,
                LookDirection = player.LookDirection,
                HasLook = true,
                Now = Time.time,
                Weights = weights,
                Range = range,
                ThreatActive = player.HeldWeapon == WeaponKind.IceGun,
                Aim = new AimCone(eye, player.LookDirection, iceGunHalfAngle, iceGunRange),
            };
            climber.Config = climb;

            watch.Restart();
            lastSearch = climber.Search(lastSearch.Cell);
            watch.Stop();
            lastSearchMs = (float)watch.Elapsed.TotalMilliseconds;
            if (lastSearch.Cell >= 0) bestBreakdown = scorer.Explain(lastSearch.Cell);

            // Colour ramp over the shown storey (debug only: the ghosts never score every cell).
            int offset = layer * grid.LayerSize;
            scoreMin = float.PositiveInfinity;
            scoreMax = float.NegativeInfinity;
            candidates = 0;
            for (int c = 0; c < grid.LayerSize; c++)
            {
                marks[c] = 0;
                if (!scorer.IsCandidate(offset + c)) { layerScore[c] = float.NegativeInfinity; continue; }
                float s = scorer.Score(offset + c);
                layerScore[c] = s;
                scoreMin = Mathf.Min(scoreMin, s);
                scoreMax = Mathf.Max(scoreMax, s);
                candidates++;
            }
            Mark(climber.TraceTrail, climber.TraceTrailCount, MarkTrail, offset);
            Mark(climber.TraceSeeds, climber.TraceSeedCount, MarkSeed, offset);
            Mark(climber.TracePeaks, climber.TracePeakCount, MarkPeak, offset);
            bestCell[0] = lastSearch.Cell;
            Mark(bestCell, 1, MarkBest, offset);
        }

        void Mark(int[] cells, int count, byte mark, int offset)
        {
            for (int i = 0; i < count; i++)
            {
                int c = cells[i] - offset;
                if (c >= 0 && c < marks.Length) marks[c] = mark;
            }
        }

        int ShownLayer(GhostGrid grid)
        {
            IPlayerState player = PlayerRegistry.Current;
            if (player != null) return grid.LayerAt(player.Position.y + 0.1f);
            Camera view = Camera.main;
            return view != null ? grid.LayerAt(view.transform.position.y - 1.5f) : 0;
        }

        void BuildMesh(GhostGrid grid, int layer)
        {
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            if (mesh == null)
                mesh = new Mesh { name = "Ghost map heat", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };

            int cells = grid.LayerSize;
            var vertices = new Vector3[cells * 4];
            var triangles = new int[cells * 6];
            colours = new Color32[cells * 4];
            float y = grid.FloorY[layer] + LiftAboveFloor;
            float inset = grid.CellSize * 0.04f;        // thin gaps so single cells can be told apart

            for (int z = 0; z < grid.SizeZ; z++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                int c = z * grid.SizeX + x;
                float x0 = grid.Origin.x + x * grid.CellSize + inset, x1 = x0 + grid.CellSize - 2f * inset;
                float z0 = grid.Origin.z + z * grid.CellSize + inset, z1 = z0 + grid.CellSize - 2f * inset;
                int v = c * 4;
                vertices[v] = new Vector3(x0, y, z0);
                vertices[v + 1] = new Vector3(x0, y, z1);
                vertices[v + 2] = new Vector3(x1, y, z1);
                vertices[v + 3] = new Vector3(x1, y, z0);
                int t = c * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors32 = colours;
            mesh.RecalculateBounds();
            meshLayer = layer;
        }

        void OnGUI()
        {
            if (!visible || map == null) return;
            GhostGrid grid = map.Grid;
            IPlayerState player = PlayerRegistry.Current;
            string under = "-";
            if (player != null)
            {
                int cell = grid.CellAt(player.Position + Vector3.up * 0.1f);
                if (cell >= 0)
                    under = "light " + map.Light(cell).ToString("F2") + "  watched " + map.Watched(cell, Time.time).ToString("F2")
                            + "  " + grid.Flags[cell];
            }
            string field = (view == View.WalkDistance || view == View.Score) && walkField != null
                ? "\nwalk field (BFS): " + walkField.CellsVisited + " cells in " + lastFieldMs.ToString("F3") + " ms, rebuilt when you move > 1 cell"
                : "";
            if (view == View.Score && climber != null)
                field += "\nhill climb: " + lastSearch.Climbs + " climbs, " + lastSearch.Evaluations + " cells scored (" + candidates +
                         " on this storey) in " + lastSearchMs.ToString("F3") + " ms" + (climb.useAnnealing ? "  [annealing]" : "") +
                         (lastSearch.KeptCurrent ? "  kept current" : "") +
                         "\ncyan seed, white trail, yellow peak, magenta best" +
                         (scorer.Query.ThreatActive ? "   ICE GUN AIMED" : "") +
                         "\nbest: " + (lastSearch.Cell >= 0 ? bestBreakdown.ToString() : "no candidate");

            string text = "GHOST MAP   F2 hide | F3 view: " + view + "   storey " + meshLayer + "\n" +
                          house.BakeInfo + "\n" +
                          "view update: " + map.LastViewCellsWritten + " cells in " + lastFeedMs.ToString("F3") + " ms" +
                          (feedFromPlayer ? "  (sandbox feed)" : "") + "\n" +
                          "under player: " + under + field;
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            float height = lines * 16f + 14f;
            GUI.Box(new Rect(Screen.width - 470f, 10f, 460f, height), GUIContent.none);
            GUI.Label(new Rect(Screen.width - 460f, 14f, 450f, height - 4f), text);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
