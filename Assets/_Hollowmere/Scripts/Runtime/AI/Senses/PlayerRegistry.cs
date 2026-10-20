using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>Where the player announces itself, so the AI never has to search the scene for it.</summary>
    public static class PlayerRegistry
    {
        /// <summary>The player, or null when there is none (menus, loading).</summary>
        public static IPlayerState Current { get; private set; }

        /// <summary>Alias for Current matching standard registry access patterns.</summary>
        public static IPlayerState Instance => Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Current = null;

        public static void Set(IPlayerState player) => Current = player;

        public static void Clear(IPlayerState player)
        {
            if (Current == player) Current = null;
        }

        public static void Register(IPlayerState player) => Set(player);
        public static void Unregister(IPlayerState player) => Clear(player);
    }
}



