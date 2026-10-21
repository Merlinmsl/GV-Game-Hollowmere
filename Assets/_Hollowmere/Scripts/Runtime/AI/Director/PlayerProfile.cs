using System;
using UnityEngine;

namespace Hollowmere.AI.Director
{
    /// <summary>
    /// Learns how this player plays, from the noise they make.
    ///   noiseRate: metres of loudness per second, as an exponential moving average over 'window' seconds.
    ///              Walking is about 10 (5 m, twice a second), running about 36, crouching about 4.
    ///   quietness: 1 for a player at or below quietRate, 0 at or above loudRate, linear between.
    ///              With 3 and 25: crouching (about 4) is Stealthy, walking (about 9) Balanced, running Loud.
    /// The Director uses it to choose its tactic: a quiet player gets staged noises that lure the dead
    /// into their path; a loud player wakes the dead by themselves, so the Director wakes fewer directly.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        [Tooltip("Seconds of play the averages mostly reflect.")]
        [SerializeField] float window = 30f;
        [SerializeField] float quietRate = 3f;
        [SerializeField] float loudRate = 25f;

        float pendingLoudness;
        int pendingShots;

        public float NoiseRate { get; private set; }
        public float ShotsPerMinute { get; private set; }

        public float Quietness => 1f - Mathf.Clamp01((NoiseRate - quietRate) / (loudRate - quietRate));

        public string Style => Quietness > 0.8f ? "Stealthy" : Quietness < 0.35f ? "Loud" : "Balanced";

        public void RecordNoise(float loudness, bool gunshot)
        {
            pendingLoudness += loudness;
            if (gunshot) pendingShots++;
        }

        public void Update(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            // Exponential moving average with a time constant, so the result does not depend on the tick rate.
            float blend = 1f - Mathf.Exp(-deltaTime / window);
            NoiseRate += blend * (pendingLoudness / deltaTime - NoiseRate);
            ShotsPerMinute += blend * (pendingShots * 60f / deltaTime - ShotsPerMinute);

            pendingLoudness = 0f;
            pendingShots = 0;
        }
    }
}
