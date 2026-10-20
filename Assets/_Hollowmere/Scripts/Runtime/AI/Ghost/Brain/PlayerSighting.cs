using Hollowmere.AI.Senses;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// What a ghost noticed the last time it actually SAW the player: where they stood, which way they
    /// looked, what they held and whether the flashlight was on. The ghost only knows these from a
    /// sighting; between sightings it works from this memory, which goes stale.
    /// </summary>
    public readonly struct PlayerSighting
    {
        public readonly Vector3 Position;
        public readonly Vector3 Look;
        public readonly WeaponKind Weapon;
        public readonly bool Flashlight;
        public readonly float Time;
        public readonly bool Valid;

        public PlayerSighting(Vector3 position, Vector3 look, WeaponKind weapon, bool flashlight, float time)
        {
            Position = position;
            Look = look;
            Weapon = weapon;
            Flashlight = flashlight;
            Time = time;
            Valid = true;
        }

        public float Age(float now) => Valid ? now - Time : float.PositiveInfinity;
    }
}
