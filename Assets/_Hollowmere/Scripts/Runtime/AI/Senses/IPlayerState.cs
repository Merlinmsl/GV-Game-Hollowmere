using UnityEngine;

namespace Hollowmere.AI.Senses
{
    public enum WeaponKind { None, Pistol, IceGun }

    /// <summary>
    /// What the AI may read about the player. The Director and Biscuit use it directly;
    /// enemies must use their own senses and beliefs, never this.
    /// </summary>
    public interface IPlayerState
    {
        Vector3 Position { get; }
        Vector3 LookDirection { get; }
        float Health01 { get; }      // 1 = full, 0 = dead
        float Ammo01 { get; }        // 1 = full, 0 = empty
        WeaponKind HeldWeapon { get; }
        bool FlashlightOn { get; }
        bool IsCrouching { get; }
    }
}





