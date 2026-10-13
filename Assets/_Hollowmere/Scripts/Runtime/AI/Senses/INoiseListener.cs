using UnityEngine;

namespace Hollowmere.AI.Senses
{
    /// <summary>Anything that can hear. Register with NoiseSystem while active.</summary>
    public interface INoiseListener
    {
        /// <summary>Where the listener stands. Use the position at its feet, on the floor.</summary>
        Vector3 EarPosition { get; }

        /// <summary>1 = normal hearing, above 1 = sharper, 0 = deaf.</summary>
        float HearingAcuity { get; }

        /// <summary>Called once for each noise this listener can hear.</summary>
        /// <param name="pathDistance">How far the sound travelled to reach it, in metres.</param>
        void OnHeard(in Noise noise, float pathDistance);
    }
}