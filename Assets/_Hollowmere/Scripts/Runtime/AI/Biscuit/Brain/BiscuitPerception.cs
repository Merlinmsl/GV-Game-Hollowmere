using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// What the body measured for one think. Plain numbers only, so the brain never touches a
    /// GameObject and a test can make one up.
    /// </summary>
    public struct BiscuitPerception
    {
        public float Now;
        public Vector3 Position;
        public bool HasPlayer;
        public Vector3 PlayerPosition;
        public Vector3 PlayerLook;
        public float PlayerHealth01;
        public float PlayerAmmo01;

        /// <summary>Unused ammunition spots in the loaded zones. Biscuit has to sniff to find one.</summary>
        public int AmmoSpotsLeft;

        /// <summary>Pages in the loaded zones the player has not been shown yet.</summary>
        public int PagesLeft;

        /// <summary>Straight-line distance to the player, or infinity when there is none.</summary>
        public float DistanceToPlayer =>
            HasPlayer ? Vector3.Distance(Position, PlayerPosition) : float.PositiveInfinity;
    }
}
