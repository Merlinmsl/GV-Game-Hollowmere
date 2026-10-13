using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Everything the brain asks the body to do. BiscuitAgent does it with the NavMeshAgent and the
    /// noise system; the tests use a fake that only records what was asked.
    /// </summary>
    public interface IBiscuitBody
    {
        /// <summary>Walks (or runs) towards a point, stopping this far short of it.</summary>
        void MoveTo(Vector3 destination, float stopDistance, bool run);

        void Stop();

        bool HasArrived { get; }

        /// <summary>True when there is no full route to the destination: a locked door, or off the NavMesh.</summary>
        bool PathFailed { get; }

        /// <summary>Puts Biscuit on the NavMesh near a point, for when the player is suddenly far away.</summary>
        void WarpNear(Vector3 point);

        /// <summary>A quiet warning, turned towards the threat. The dead do not hear it.</summary>
        void Growl(Vector3 towards);

        /// <summary>A real noise in the senses layer: the dead within 25 m of walking distance hear it.</summary>
        void Bark();

        /// <summary>Starts a sniff for the dead within this walking distance.</summary>
        void BeginSniff(float range);

        /// <summary>Follows a few more scent trails. True once every trail has been checked.</summary>
        bool SniffStep();

        bool TryFindRetreatPoint(Vector3 threat, float distance, out Vector3 point);

        bool TryFindLurePoint(Vector3 player, Vector3 threat, float distance, out Vector3 point);
    }
}
