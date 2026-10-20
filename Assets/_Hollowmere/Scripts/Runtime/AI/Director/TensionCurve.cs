using System;
using UnityEngine;

namespace Hollowmere.AI.Director
{
    public enum TensionPhase { BuildUp, Peak, Relax }

    /// <summary>
    /// The tension the Director is aiming for. It cycles like the pacing of a horror film:
    ///   BuildUp: the target climbs from buildFrom to buildTo over buildSeconds.
    ///   Peak:    entered when stress reaches peakThreshold; the target stays high for peakSeconds.
    ///   Relax:   the target drops to relaxTarget, so the Director holds back and the player recovers.
    ///            It ends once relaxSeconds have passed and the player has calmed down
    ///            (or after three times relaxSeconds whatever happens), and BuildUp starts again.
    /// The Director acts to close the gap between this target and the measured stress:
    /// a feedback loop, with the utility scores as the controller.
    /// </summary>
    [Serializable]
    public sealed class TensionCurve
    {
        [SerializeField] float buildFrom = 0.2f;
        [SerializeField] float buildTo = 0.8f;
        [SerializeField] float buildSeconds = 90f;
        [SerializeField] float peakThreshold = 0.75f;
        [SerializeField] float peakSeconds = 12f;
        [SerializeField] float relaxTarget = 0.15f;
        [SerializeField] float relaxSeconds = 35f;

        public TensionPhase Phase { get; private set; } = TensionPhase.BuildUp;
        public float PhaseTime { get; private set; }

        public float Target
        {
            get
            {
                switch (Phase)
                {
                    case TensionPhase.BuildUp: return Mathf.Lerp(buildFrom, buildTo, PhaseTime / buildSeconds);
                    case TensionPhase.Peak: return buildTo;
                    default: return relaxTarget;
                }
            }
        }

        public void Update(float deltaTime, float stress)
        {
            PhaseTime += deltaTime;
            switch (Phase)
            {
                case TensionPhase.BuildUp:
                    if (stress >= peakThreshold) Enter(TensionPhase.Peak);
                    break;
                case TensionPhase.Peak:
                    if (PhaseTime >= peakSeconds) Enter(TensionPhase.Relax);
                    break;
                case TensionPhase.Relax:
                    bool calmedDown = PhaseTime >= relaxSeconds && stress <= relaxTarget + 0.15f;
                    if (calmedDown || PhaseTime >= relaxSeconds * 3f) Enter(TensionPhase.BuildUp);
                    break;
            }
        }

        void Enter(TensionPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }
    }
}
