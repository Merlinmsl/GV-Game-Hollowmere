namespace Hollowmere.AI.Biscuit
{
    /// <summary>The kind of enemy Biscuit has sensed. Biscuit treats each kind differently.</summary>
    public enum EnemyKind
    {
        Dead,       // the drowned villagers: they hunt by sound, so a bark wakes them
        Ghost,      // Mara and the servants: they drift through walls and do not chase noise
        Creature,   // the thing in the tunnel: blind, and it hears better than the dead
    }

    /// <summary>Which enemies a memory query counts.</summary>
    public enum KindFilter { Any, Ghosts, NotGhosts }

    /// <summary>Something useful in the level that Biscuit can find by smell.</summary>
    public enum PointKind { Ammo, Evidence }

    public static class KindFilters
    {
        public static bool Matches(this KindFilter filter, EnemyKind kind) =>
            filter == KindFilter.Any || (filter == KindFilter.Ghosts) == (kind == EnemyKind.Ghost);
    }
}
