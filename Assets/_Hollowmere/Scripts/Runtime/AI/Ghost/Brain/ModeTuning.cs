using System;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// How a ghost chooses between Stalk, Ambush, Strike and Flee, and how each mode behaves.
    /// Part of a GhostProfile: Mara and the servants share the brain and differ only here.
    /// </summary>
    [Serializable]
    public struct ModeTuning
    {
        [Header("Utility (how much each mode is worth at best)")]
        public float stalkBase;
        public float ambushWeight;
        public float strikeWeight;
        public float fleeWeight;
        [Tooltip("0 timid .. 1 bold: raises Ambush and Strike, lowers nervous fleeing.")]
        [Range(0f, 1f)] public float aggression;
        [Tooltip("The current mode keeps this bonus, so near-ties do not flicker.")]
        public float switchMargin;

        [Header("Ambush: wait close by, in the dark, where the player has not looked")]
        public ScoreWeights ambushWeights;
        public RangeBand ambushRange;

        [Header("Strike")]
        [Tooltip("Metres from the player at which the strike lands.")]
        public float strikeDistance;
        [Tooltip("Beyond this many metres Strike is not considered at all.")]
        public float strikeReach;
        public float strikeCooldown;
        [Tooltip("Seconds of stalking after which the ghost's patience is gone and Strike is worth its full weight.")]
        public float patienceSeconds;

        [Header("Flee")]
        [Tooltip("Once fleeing, keep fleeing at least this long (no flicker while the aim wavers).")]
        public float fleeMinSeconds;
        public float fleeAfterStrike;
        public float fleeAfterFreeze;
        [Tooltip("Furthest refuge searched for, in moves.")]
        public int fleeDepth;
        [Tooltip("How old a sighting of the ice gun aimed at the ghost may be and still count as aimed.")]
        public float aimFreshness;

        [Header("Weapon tactics: ice gun held = careful; pistol or nothing = bold")]
        public RangeBand carefulRange;
        public float carefulCoverScale;
        public float carefulUnseenScale;
        public float carefulStrikeScale;
        public float boldStrikeScale;
        [Tooltip("Seconds the ghost remembers the player has the ice gun.")]
        public float iceGunMemory;

        /// <summary>A servant: patient, wary of the ice gun, keeps its distance.</summary>
        public static ModeTuning Servant => new ModeTuning
        {
            stalkBase = 0.5f,
            ambushWeight = 0.8f,
            strikeWeight = 1f,
            fleeWeight = 1.2f,
            aggression = 0.4f,
            switchMargin = 0.1f,
            ambushWeights = new ScoreWeights
                { behind = 0f, dark = 1.4f, unseen = 1.5f, cover = 1.2f, range = 1.5f, home = 0.5f, threat = 3f, crowd = 1f },
            ambushRange = new RangeBand(1.5f, 3.5f, 2f),
            strikeDistance = 1.6f,
            strikeReach = 8f,
            strikeCooldown = 6f,
            patienceSeconds = 15f,
            fleeMinSeconds = 1.5f,
            fleeAfterStrike = 1.5f,
            fleeAfterFreeze = 2f,
            fleeDepth = 10,
            aimFreshness = 1f,
            carefulRange = new RangeBand(6f, 10f, 4f),
            carefulCoverScale = 1.5f,
            carefulUnseenScale = 1.3f,
            carefulStrikeScale = 0.4f,
            boldStrikeScale = 1.2f,
            iceGunMemory = 20f,
        };

        /// <summary>Mara: the same brain, bolder and less patient.</summary>
        public static ModeTuning Mara
        {
            get
            {
                ModeTuning t = Servant;
                t.aggression = 0.9f;
                t.strikeWeight = 1.3f;
                t.strikeCooldown = 4f;
                t.patienceSeconds = 6f;
                t.fleeWeight = 1.1f;
                t.carefulStrikeScale = 0.6f;
                t.carefulRange = new RangeBand(4f, 8f, 4f);
                return t;
            }
        }
    }
}
