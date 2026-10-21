using System.Collections.Generic;
using UnityEngine;

namespace Hollowmere.AI.Director
{
    /// <summary>
    /// An enemy the Director may wake. The Director never moves enemies or tells them where the
    /// player really is: it only wakes one and gives it a place to go. The enemy's own senses do the rest.
    /// Zombies and the creature implement this; the grey stand-ins do until they are ready.
    /// </summary>
    public interface IDirectable
    {
        Vector3 Position { get; }
        bool IsAwake { get; }

        /// <summary>Wake up and head toward this point, as if something was heard there.</summary>
        void WakeToward(Vector3 point);
    }

    /// <summary>Where enemies announce themselves to the Director while they exist.</summary>
    public static class DirectableRegistry
    {
        static readonly List<IDirectable> agents = new List<IDirectable>();

        public static int Count => agents.Count;
        public static IDirectable Get(int index) => agents[index];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => agents.Clear();

        public static void Register(IDirectable agent)
        {
            if (!agents.Contains(agent)) agents.Add(agent);
        }

        public static void Unregister(IDirectable agent) => agents.Remove(agent);
    }
}
