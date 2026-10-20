using System.Collections.Generic;
using System.Diagnostics;
using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Marks out a house for the ghosts and bakes it into a GhostHouseMap (right-click the component,
    /// "Bake Ghost Map"). The box sits on this object's position; storey floors are heights above it.
    /// Ghosts never leave this box.
    /// </summary>
    public sealed class GhostHouseVolume : MonoBehaviour
    {
        [Tooltip("The asset the bake is written to.")]
        [SerializeField] GhostHouseMap target;
        [Tooltip("Size of the house box in metres. Its base is at this object's position.")]
        [SerializeField] Vector3 size = new Vector3(20f, 8f, 20f);
        [SerializeField] float cellSize = 1f;
        [Tooltip("Floor height of each storey, above this object's position.")]
        [SerializeField] float[] storeyFloors = { 0f, 4f };
        [Tooltip("What counts as walls and floors: ghosts pass through, the player and light do not.")]
        [SerializeField] LayerMask solidMask = 1;
        [Tooltip("What stops lamp light.")]
        [SerializeField] LayerMask lightBlockers = 1;

        static readonly List<GhostHouseVolume> active = new List<GhostHouseVolume>();

        public GhostHouseMap Target => target;

        public Bounds WorldBounds => new Bounds(transform.position + Vector3.up * size.y * 0.5f, size);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => active.Clear();

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);

        /// <summary>The baked map of the house containing a point, or null. How a ghost prefab finds its house.</summary>
        public static GhostHouseMap MapAt(Vector3 point)
        {
            for (int i = 0; i < active.Count; i++)
            {
                GhostHouseVolume house = active[i];
                if (house.target != null && house.target.IsBaked && house.WorldBounds.Contains(point)) return house.target;
            }
            return null;
        }

        [ContextMenu("Bake Ghost Map")]
        public void Bake()
        {
            if (target == null)
            {
                UnityEngine.Debug.LogError("[GhostHouseVolume] Assign a GhostHouseMap asset to bake into.", this);
                return;
            }

            var watch = Stopwatch.StartNew();
            var floors = new float[storeyFloors.Length];
            for (int i = 0; i < floors.Length; i++) floors[i] = transform.position.y + storeyFloors[i];

            var settings = new GridBaker.Settings
            {
                Bounds = WorldBounds,
                CellSize = cellSize,
                FloorY = floors,
                SolidMask = solidMask,
                LightBlockers = lightBlockers,
            };
            GhostGrid grid = GridBaker.BakeGrid(settings);
            var map = new InfluenceMap(grid);
            int lamps = GridBaker.BakeLight(map, FindLamps(), lightBlockers);
            watch.Stop();

            int inside = 0, solid = 0, walkable = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                if (grid.Is(i, CellFlags.Inside)) inside++;
                if (grid.Is(i, CellFlags.Solid)) solid++;
                if (grid.Is(i, CellFlags.Walkable)) walkable++;
            }

            string info = gameObject.scene.name + ": " + grid.SizeX + "x" + grid.SizeZ + "x" + grid.Layers + " cells ("
                          + inside + " inside, " + solid + " solid, " + walkable + " walkable), " + lamps + " lamps, baked in "
                          + watch.ElapsedMilliseconds + " ms";
            target.Store(map, info);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(target);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(target);
#endif
            UnityEngine.Debug.Log("[GhostHouseVolume] Baked " + info, this);
        }

        // Lamps marked as IlluminationSource count; if a scene has none marked, every point or spot light
        // inside the box does.
        List<Light> FindLamps()
        {
            Bounds box = WorldBounds;
            box.Expand(2f);
            var marked = new List<Light>();
            var all = new List<Light>();
            foreach (Light lamp in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!box.Contains(lamp.transform.position)) continue;
                all.Add(lamp);
                if (lamp.GetComponent<IlluminationSource>() != null) marked.Add(lamp);
            }
            return marked.Count > 0 ? marked : all;
        }

        void OnDrawGizmosSelected()
        {
            Bounds box = WorldBounds;
            Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.15f);
            foreach (float floor in storeyFloors)
                Gizmos.DrawCube(new Vector3(box.center.x, transform.position.y + floor, box.center.z),
                                new Vector3(box.size.x, 0.02f, box.size.z));
        }
    }
}
