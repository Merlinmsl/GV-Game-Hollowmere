namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// The player implements this so Biscuit can hand over the ammunition it fetched. The real
    /// player (Systems Engineer) needs only this one method; until then BiscuitTestPlayer has it.
    /// </summary>
    public interface IAmmoReceiver
    {
        /// <param name="amount">Fraction of a full load, 0 to 1.</param>
        void ReceiveAmmo(float amount);
    }
}
