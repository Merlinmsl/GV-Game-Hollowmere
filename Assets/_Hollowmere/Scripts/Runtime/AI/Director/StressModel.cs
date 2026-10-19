using System;
using UnityEngine;

namespace Hollowmere.AI.Director
{
    /// <summary>
    /// How tense the player is right now, from 0 (calm) to 1 (overwhelmed). Updated every Director tick:
    ///
    ///   rise = damageWeight x healthLost
    ///        + (pressureWeight x enemiesNear + lowHealthWeight x (1 - health) + lowAmmoWeight x (1 - ammo)) x dt
    ///   fall = calmRate x dt, only while no enemy is near
    ///   stress = clamp01(stress + rise - fall)
    ///
    /// Damage is a one-off jump; the rest build up over time. With nothing near, a badly hurt player
    /// (health 0.2) still calms down, because 0.03 x 0.8 = 0.024 per second is less than calmRate 0.05.
    /// </summary>
    [Serializable]
    public sealed class StressModel
    {
        [Tooltip("Stress added per unit of health lost. Losing 20 % health adds 0.3.")]
        [SerializeField] float damageWeight = 1.5f;
        [Tooltip("Stress added per second for each awake enemy near the player.")]
        [SerializeField] float pressureWeight = 0.04f;
        [Tooltip("Stress added per second at zero health, scaled by how much health is missing.")]
        [SerializeField] float lowHealthWeight = 0.03f;
        [Tooltip("Stress added per second at zero ammo, scaled by how much ammo is missing.")]
        [SerializeField] float lowAmmoWeight = 0.015f;
        [Tooltip("Stress lost per second while no awake enemy is near. 0.05 = from 1 to 0 in 20 s.")]
        [SerializeField] float calmRate = 0.05f;
        [Tooltip("An awake enemy closer than this (metres) counts as pressure on the player.")]
        [SerializeField] float pressureRadius = 25f;

        float lastHealth;
        bool hasLastHealth;

        public float Value { get; private set; }
        public float PressureRadius => pressureRadius;

        public void Update(float deltaTime, float health01, float ammo01, int enemiesNear)
        {
            float healthLost = hasLastHealth ? Mathf.Max(0f, lastHealth - health01) : 0f;
            lastHealth = health01;
            hasLastHealth = true;

            float rise = damageWeight * healthLost
                         + (pressureWeight * enemiesNear
                            + lowHealthWeight * (1f - health01)
                            + lowAmmoWeight * (1f - ammo01)) * deltaTime;
            float fall = enemiesNear == 0 ? calmRate * deltaTime : 0f;

            Value = Mathf.Clamp01(Value + rise - fall);
        }
    }
}
