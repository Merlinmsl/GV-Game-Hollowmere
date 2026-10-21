using Hollowmere.Combat;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Something the ice gun can freeze. GhostBody implements it.
    /// Inherits from shared Hollowmere.Combat.IIceable so weapons can freeze ghosts without depending on Ghost assembly.
    /// </summary>
    public interface IIceable : Combat.IIceable
    {
    }
}
