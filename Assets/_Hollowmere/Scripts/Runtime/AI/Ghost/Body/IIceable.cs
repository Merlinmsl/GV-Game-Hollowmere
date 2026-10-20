namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Something the ice gun can freeze. GhostBody implements it.
    /// PROPOSAL for the team: there is no ice gun yet. When the weapon is built, its raycast (on a "Ghost"
    /// layer) should call ReceiveIce on what it hits; this interface should then move to the shared
    /// combat code so the weapon does not depend on the ghost assembly. Until then GhostCombatStub stands in.
    /// </summary>
    public interface IIceable
    {
        /// <summary>Frozen in place for 'seconds'.</summary>
        void ReceiveIce(float seconds);
    }
}
