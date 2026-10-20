namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// What a ghost is doing. Idle: no belief about the player, drifting home. Stalk: keeping a hidden
    /// position near where it believes the player is. Ambush, Strike and Flee are chosen by the utility
    /// modes (Phase 7); Frozen is the ice gun's effect.
    /// </summary>
    public enum GhostMode { Idle, Stalk, Ambush, Strike, Flee, Frozen }
}
