using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// A baked house: the grid's cells and their lamp light, stored as an asset so nothing is computed
    /// at load. One asset per house (the sandbox and Marrow House). All ghosts in a house share one
    /// runtime InfluenceMap made from it, so what one ghost learns about the player's view, all use.
    /// </summary>
    [CreateAssetMenu(menuName = "Hollowmere/Ghost/House Map", fileName = "GhostHouseMap")]
    public sealed class GhostHouseMap : ScriptableObject
    {
        [SerializeField] Vector3 origin;
        [SerializeField] float cellSize = 1f;
        [SerializeField] int sizeX;
        [SerializeField] int sizeZ;
        [SerializeField] float[] floorY = Array.Empty<float>();
        [SerializeField, HideInInspector] byte[] flags = Array.Empty<byte>();
        [SerializeField, HideInInspector] float[] light = Array.Empty<float>();
        [SerializeField, HideInInspector] float[] walkY = Array.Empty<float>();
        [Tooltip("Where the bake came from and when.")]
        [SerializeField] string bakeInfo;

        // Rebuilt once per Play session, so watched data never leaks from one session to the next.
        static int session;
        [NonSerialized] InfluenceMap runtime;
        [NonSerialized] int runtimeSession = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void NewSession() => session++;

        public bool IsBaked => flags != null && flags.Length > 0 && flags.Length == sizeX * sizeZ * floorY.Length;
        public string BakeInfo => bakeInfo;

        /// <summary>The map every ghost in this house shares during Play.</summary>
        public InfluenceMap Runtime
        {
            get
            {
                if (runtime == null || runtimeSession != session)
                {
                    runtime = Build();
                    runtimeSession = session;
                }
                return runtime;
            }
        }

        /// <summary>A fresh grid and map from the stored bake. Null when not baked.</summary>
        public InfluenceMap Build()
        {
            if (!IsBaked) return null;
            var cellFlags = new CellFlags[flags.Length];
            for (int i = 0; i < flags.Length; i++) cellFlags[i] = (CellFlags)flags[i];
            float[] heights = walkY != null && walkY.Length == flags.Length ? (float[])walkY.Clone() : null;
            var grid = new GhostGrid(origin, cellSize, sizeX, sizeZ, (float[])floorY.Clone(), cellFlags, heights);
            return new InfluenceMap(grid, (float[])light.Clone());
        }

        public void Store(InfluenceMap map, string info)
        {
            GhostGrid grid = map.Grid;
            origin = grid.Origin;
            cellSize = grid.CellSize;
            sizeX = grid.SizeX;
            sizeZ = grid.SizeZ;
            floorY = (float[])grid.FloorY.Clone();
            flags = new byte[grid.Count];
            light = new float[grid.Count];
            walkY = (float[])grid.WalkY.Clone();
            for (int i = 0; i < grid.Count; i++)
            {
                flags[i] = (byte)grid.Flags[i];
                light[i] = map.BakedLight(i);
            }
            bakeInfo = info;
            runtime = null;
        }
    }
}
