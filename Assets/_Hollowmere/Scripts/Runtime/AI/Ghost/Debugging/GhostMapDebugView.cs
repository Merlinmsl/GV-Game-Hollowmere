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
    /// </summary>
    public sealed class GhostMapDebugView : MonoBehaviour
    {
        public enum View { Exposure, Light, Watched, Cells }

        [SerializeField] GhostHouseMap house;
        [Tooltip("Show the heatmap from the start. F2 toggles it.")]
        [SerializeField] bool visible;
        [SerializeField] View view = View.Exposure;
        [Tooltip("SANDBOX ONLY: mark the debug player's view and flashlight directly, until the ghosts feed the map from their senses.")]
        [SerializeField] bool feedFromPlayer = true;
        [SerializeField] float feedInterval = 0.2f;
        [SerializeField, Range(0.05f, 1f)] float opacity = 0.5f;

        const float LiftAboveFloor = 0.06f;

        readonly Stopwatch watch = new Stopwatch();
        InfluenceMap map;
        Mesh mesh;
        Material material;
        Color32[] colours;
        int meshLayer = -1;
        float nextFeed;
        float lastFeedMs;

        public bool Visible => visible;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f2Key.wasPressedThisFrame) visible = !visible;
                if (visible && keyboard.f3Key.wasPressedThisFrame) view = (View)(((int)view + 1) % 4);
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
                default:
                    // What a ghost avoids: lit or watched. Green is a good hiding place, red is exposed.
                    float exposure = Mathf.Max(map.Light(cell), map.Watched(cell, now));
                    return Color32.Lerp(new Color32(40, 200, 80, alpha), new Color32(255, 50, 40, alpha), exposure);
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

            GUI.Box(new Rect(Screen.width - 430f, 10f, 420f, 92f), GUIContent.none);
            GUI.Label(new Rect(Screen.width - 420f, 14f, 410f, 88f),
                "GHOST MAP   F2 hide | F3 view: " + view + "   storey " + meshLayer + "\n" +
                house.BakeInfo + "\n" +
                "view update: " + map.LastViewCellsWritten + " cells in " + lastFeedMs.ToString("F3") + " ms" +
                (feedFromPlayer ? "  (sandbox feed)" : "") + "\n" +
                "under player: " + under);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
