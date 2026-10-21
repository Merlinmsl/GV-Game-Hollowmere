namespace Hollowmere.Interaction
{
    /// <summary>
    /// Standard interface for environmental objects, doors, and pickups
    /// that the player can interact with by looking at them and pressing E.
    /// Defined in SystemsEngineer.md (Section 5).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Text prompt displayed on screen to the player (e.g., "[E] Open", "Locked").</summary>
        string Prompt { get; }

        /// <summary>Returns true if the player meets all conditions (e.g. holds required key) to interact.</summary>
        bool CanInteract(PlayerInventory inventory);

        /// <summary>Executes interaction logic when the player triggers interaction.</summary>
        void Interact(PlayerInventory inventory);
    }
}
