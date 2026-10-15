using Hollowmere.AI.Biscuit;
using UnityEngine;

namespace Hollowmere.Tests.Biscuit
{
    /// <summary>A body that only records what the brain asked for. Tests set Arrived and the failure flags.</summary>
    sealed class FakeBiscuitBody : IBiscuitBody
    {
        public int Barks;
        public int Growls;
        public int Warps;
        public int Sniffs;
        public Vector3? Destination;
        public bool Arrived;
        public bool NoRoute;
        public bool RetreatFound = true;
        public bool LureFound = true;

        public void MoveTo(Vector3 destination, float stopDistance, bool run) => Destination = destination;
        public void Stop() => Destination = null;
        public bool HasArrived => Arrived;
        public bool PathFailed => NoRoute;
        public void WarpNear(Vector3 point) => Warps++;
        public void Growl(Vector3 towards) => Growls++;
        public void Bark() => Barks++;
        public void BeginSniff(float range) => Sniffs++;
        public bool SniffStep() => true;

        public bool TryFindRetreatPoint(Vector3 threat, float distance, out Vector3 point)
        {
            point = threat + Vector3.right * distance;
            return RetreatFound;
        }

        public bool TryFindLurePoint(Vector3 player, Vector3 threat, float distance, out Vector3 point)
        {
            point = player + Vector3.left * distance;
            return LureFound;
        }
    }
}
