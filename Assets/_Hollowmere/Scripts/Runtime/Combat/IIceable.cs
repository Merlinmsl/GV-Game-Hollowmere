namespace Hollowmere.Combat
{
    /// <summary>
    /// Something the ice gun can freeze.
    /// Implemented by GhostBody and ZombieBody.
    /// </summary>
    public interface IIceable
    {
        /// <summary>Frozen in place for 'seconds'.</summary>
        void ReceiveIce(float seconds);
    }
}
