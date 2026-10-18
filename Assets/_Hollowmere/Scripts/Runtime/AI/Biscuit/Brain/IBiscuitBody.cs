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

        /// <summary>The warning for ghosts: a whine, facing the ghost.</summary>
        void Whine(Vector3 ghost);

        /// <summary>A real noise in the senses layer: the dead within 25 m of walking distance hear it.</summary>
        void Bark();

        /// <summary>Starts a sniff for enemies within this walking distance (and for supplies and pages).</summary>
        void BeginSniff(float range);

        /// <summary>Follows a few more scent trails. True once every trail has been checked.</summary>
        bool SniffStep();

        /// <summary>Picks up the ammunition at this point. False if it is already gone.</summary>
        bool TryTakeAmmo(int pointId);

        /// <summary>Hands the carried ammunition to the player.</summary>
        void DeliverAmmo(float amount);

        /// <summary>The player has reached this page: mark it seen and say what it is.</summary>
        void ShowEvidence(int pointId);

        bool TryFindRetreatPoint(Vector3 threat, float distance, out Vector3 point);

        bool TryFindLurePoint(Vector3 player, Vector3 threat, float distance, out Vector3 point);
    }
}
