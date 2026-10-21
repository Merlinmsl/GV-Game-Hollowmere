using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// Who a ghost is: Mara or a servant. Every ghost runs the same brain; the profile sets how it scores
    /// places, how it chooses modes and how far it strays from its haunt. Swap the profile on a GhostBody
    /// to turn a servant into Mara.
    /// </summary>
    [CreateAssetMenu(menuName = "Hollowmere/Ghost/Profile", fileName = "SO_GhostProfile")]
    public sealed class GhostProfile : ScriptableObject
    {
        [TextArea(2, 4)] [SerializeField] string notes;
        [SerializeField] GhostTuning tuning = GhostTuning.Default;
        [SerializeField] ModeTuning modes = ModeTuning.Servant;
        [Tooltip("Metres from its haunt anchor the ghost is happy to stray.")]
        [SerializeField] float leash = 6f;

        public GhostTuning Tuning => tuning;
        public ModeTuning Modes => modes;
        public float Leash => leash;

        /// <summary>Fills the profile with the built-in values (used to create the two shipped profiles).</summary>
        public void Set(GhostTuning tuning, ModeTuning modes, float leash, string notes)
        {
            this.tuning = tuning;
            this.modes = modes;
            this.leash = leash;
            this.notes = notes;
        }
    }
}
