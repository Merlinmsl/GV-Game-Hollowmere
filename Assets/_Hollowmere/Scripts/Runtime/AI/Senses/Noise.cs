using UnityEngine;

namespace Hollowmere.AI.Senses
{
    public enum NoiseKind { Footstep, Wading, Gunshot, IceGun, Door, Bark, Groan, Staged }

    /// <summary>How far each kind of noise carries, in metres of walking distance.</summary>
    public static class Loudness
    {
        public const float CrouchStep = 2f;
        public const float Walk = 5f;  /// f means float literal, 5f is a float value of 5
        public const float Wade = 9f;
        public const float Door = 10f;
        public const float Run = 12f;
        public const float IceGun = 15f;
        public const float Groan = 20f;
        public const float Bark = 25f;
        public const float Gunshot = 45f;
    }

    /// <summary>One noise that happened in the world. Read-only once made.</summary>
    public readonly struct Noise
    {
        public readonly Vector3 Position;
        public readonly float Loudness;
        public readonly NoiseKind Kind;
        public readonly GameObject Source;

        public Noise(Vector3 position, float loudness, NoiseKind kind, GameObject source)
        {
            Position = position;
            Loudness = loudness;
            Kind = kind;
            Source = source;
        }
    }
}