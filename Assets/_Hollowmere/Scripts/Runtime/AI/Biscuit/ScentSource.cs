using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Marks something Biscuit can smell. Put it on the root of each enemy prefab (the same object
    /// that makes its noises) and set its kind: Dead on PF_Zombie, Ghost on PF_Ghost, Creature on
    /// PF_CreepCreature. It registers itself while enabled, so Biscuit never has to search the scene.
    /// Disable it when the enemy is dead or freed.
    /// </summary>
    public sealed class ScentSource : MonoBehaviour
    {
        [Tooltip("What Biscuit smells. Dead: the zombies. Ghost: Mara and the servants. " +
                 "Creature: the thing in the tunnel.")]
        [SerializeField] EnemyKind kind = EnemyKind.Dead;

        public int Id { get; private set; }
        public EnemyKind Kind => kind;
        public Vector3 Position => transform.position;

        void Awake() => Id = GetInstanceID();
        void OnEnable() => ScentRegistry.Add(this);
        void OnDisable() => ScentRegistry.Remove(this);
    }

    /// <summary>Every enabled ScentSource. Static, like NoiseSystem, and cleared at the start of every Play.</summary>
    public static class ScentRegistry
    {
        static readonly List<ScentSource> sources = new List<ScentSource>();
        static readonly Dictionary<GameObject, ScentSource> byOwner = new Dictionary<GameObject, ScentSource>();

        /// <summary>Raised with a source's id when it goes away, so beliefs about it can be dropped.</summary>
        public static event Action<int> Removed;

        public static IReadOnlyList<ScentSource> All => sources;

        // Static data survives between Play sessions when domain reload is off, so clear it each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            sources.Clear();
            byOwner.Clear();
            Removed = null;
        }

        public static void Add(ScentSource source)
        {
            if (byOwner.ContainsKey(source.gameObject)) return;
            sources.Add(source);
            byOwner.Add(source.gameObject, source);
        }

        public static void Remove(ScentSource source)
        {
            if (!byOwner.Remove(source.gameObject)) return;
            sources.Remove(source);
            Removed?.Invoke(source.Id);
        }

        /// <summary>The ScentSource on this object, if it has one. Used to tell whose noise Biscuit heard.</summary>
        public static bool TryGet(GameObject owner, out ScentSource source) => byOwner.TryGetValue(owner, out source);
    }
}
