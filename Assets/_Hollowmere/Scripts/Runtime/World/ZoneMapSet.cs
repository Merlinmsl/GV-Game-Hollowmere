using System;
using UnityEngine;

namespace Hollowmere.World
{
    /// <summary>
    /// The map picture of every zone and where on the ground it sits.
    /// Made by Hollowmere > World > Bake Zone Maps; read by the MapScreen.
    /// </summary>
    [CreateAssetMenu(menuName = "Hollowmere/World/Zone Map Set", fileName = "SO_ZoneMaps")]
    public sealed class ZoneMapSet : ScriptableObject
    {
        [Serializable]
        public sealed class ZoneMap
        {
            [Tooltip("Scene name of the zone, e.g. Z1_Outskirts.")]
            public string sceneName;
            [Tooltip("Name shown at the top of the map.")]
            public string displayName;
            [Tooltip("The baked top-down picture.")]
            public Texture2D image;
            [Tooltip("The ground the picture covers: x and z of its lower-left corner, then width and depth in metres.")]
            public Rect worldRect;
            [Tooltip("The player counts as being in this zone while inside this box.")]
            public Bounds area;
            [Tooltip("Only counts while the player is below the terrain surface (the Undercroft lies under the hill).")]
            public bool underground;
        }

        public ZoneMap[] maps = Array.Empty<ZoneMap>();

        /// <summary>
        /// The zone the position is in. Where boxes overlap (the Undercroft lies under Marrow House),
        /// the smallest box wins, because it is the more specific place.
        /// </summary>
        public ZoneMap Find(Vector3 position)
        {
            ZoneMap best = null;
            float bestVolume = float.PositiveInfinity;
            foreach (ZoneMap map in maps)
            {
                if (map == null || !map.area.Contains(position)) continue;
                if (map.underground && !IsBelowTerrain(position)) continue;
                float volume = map.area.size.x * map.area.size.y * map.area.size.z;
                if (volume < bestVolume)
                {
                    best = map;
                    bestVolume = volume;
                }
            }
            return best;
        }

        // True when the ground surface above this point is more than a metre higher: the player is in a tunnel.
        static bool IsBelowTerrain(Vector3 position)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                if (position.x < origin.x || position.x > origin.x + size.x || position.z < origin.z || position.z > origin.z + size.z) continue;
                return terrain.SampleHeight(position) + origin.y > position.y + 1f;
            }
            return false;
        }
    }
}
