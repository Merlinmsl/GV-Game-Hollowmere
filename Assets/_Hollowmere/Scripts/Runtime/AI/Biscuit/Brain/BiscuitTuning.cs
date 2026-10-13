using System;
using UnityEngine;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Every number Biscuit's brain uses, in one place so it can be tuned in the Inspector and
    /// defended in the viva. Distances in metres, times in seconds.
    /// </summary>
    [Serializable]
    public sealed class BiscuitTuning
    {
        [Header("Thinking")]

        [Tooltip("Brain updates per second. 8 Hz reacts within an eighth of a second, inside the " +
                 "5-10 Hz the AI plan allows. Movement still runs every frame.")]
        public float thinkRate = 8f;

        [Tooltip("Bonus the goal being worked on gets when goals are compared, so two goals with " +
                 "similar scores do not take turns every think.")]
        public float commitmentBonus = 0.1f;

        [Tooltip("Longest one action may run before it counts as failed (stuck on a corner, etc.).")]
        public float actionTimeout = 10f;

        [Tooltip("After an action fails, the planner leaves it out for this long and finds another way.")]
        public float failedActionBan = 3f;

        [Header("Player")]

        [Tooltip("Biscuit counts as at the player's side once this close...")]
        public float nearPlayerDistance = 3f;

        [Tooltip("...and stays at their side until the player is this far away. The gap stops " +
                 "Biscuit starting and stopping with every step the player takes.")]
        public float leavePlayerDistance = 6f;

        [Tooltip("Follow stops this far short of the player's feet.")]
        public float followStopDistance = 2f;

        [Tooltip("Further than this from the player, Biscuit runs instead of trotting.")]
        public float runDistance = 8f;

        [Tooltip("Further than this (the player teleported or fell), Biscuit is placed behind the " +
                 "player instead of running the whole way.")]
        public float warpDistance = 40f;

        public float walkSpeed = 2.5f;
        public float runSpeed = 5.5f;

        [Header("Senses")]

        [Tooltip("1 = a normal ear. 1.5 because dogs hear better than people: a groan (20 m) " +
                 "reaches Biscuit from 30 m of walking distance.")]
        public float hearingAcuity = 1.5f;

        [Tooltip("Within this straight-line distance Biscuit smells the dead without stopping to sniff.")]
        public float noseRange = 5f;

        [Tooltip("A sniff finds the dead within this walking distance: the same 25 m as a bark, so " +
                 "Biscuit knows what a bark would wake.")]
        public float sniffRange = 25f;

        public float sniffDuration = 1.5f;

        [Tooltip("After a sniff, the area within this radius counts as scouted...")]
        public float scoutedRadius = 8f;

        [Tooltip("...for this long. Then Biscuit sniffs again.")]
        public float scoutedFor = 12f;

        [Tooltip("Confidence of a smell. Scent says exactly where something is.")]
        public float smeltConfidence = 1f;

        [Tooltip("Confidence of a sound. 0.6, as in the shared senses plan: a sound gives a rough " +
                 "place, not the thing itself.")]
        public float heardConfidence = 0.6f;

        [Tooltip("Confidence = c0 x e^(-t / tau). At tau 6 s a smell (1.0) is forgotten after " +
                 "6 x ln(10) = 14 s, a sound (0.6) after 11 s.")]
        public float beliefTau = 6f;

        [Tooltip("Below this confidence a belief is forgotten.")]
        public float forgetBelow = 0.1f;

        [Header("Facts")]

        [Tooltip("EnemyNearby: one of the dead believed within this distance of the player.")]
        public float enemyNearbyDistance = 25f;

        [Tooltip("InDanger: one of the dead believed this close to Biscuit (about one lunge).")]
        public float dangerDistance = 4f;

        [Tooltip("PlayerInTrouble: the player's health is below this...")]
        public float troubleHealth = 0.35f;

        [Tooltip("...and one of the dead is believed within this distance of them.")]
        public float troubleDistance = 8f;

        [Tooltip("Beliefs weaker than this do not count for danger or trouble: Biscuit only runs " +
                 "from what it is fairly sure of.")]
        public float sureConfidence = 0.3f;

        [Tooltip("A warning covers the threats met before it, but wears off after this long.")]
        public float rewarnAfter = 20f;

        [Header("Action costs")]

        [Tooltip("Follow costs 1 plus this much per metre to the player: about one point per " +
                 "second of running, so a cost reads as time.")]
        public float followCostPerMetre = 0.2f;

        [Tooltip("A bark is quick but loud. 3 = about the time it takes to run 10 m back and growl.")]
        public float barkCost = 3f;

        [Tooltip("Added to the bark's cost for every other one of the dead in earshot, because the " +
                 "bark will wake them too.")]
        public float barkCostPerExtraDead = 2f;

        [Tooltip("Luring is risky and leaves the player alone, so it is the dearest action.")]
        public float lureCost = 5f;

        [Header("Actions")]

        public float growlDuration = 1.2f;
        public float barkDuration = 0.8f;

        [Tooltip("How far Biscuit backs away from one of the dead that got too close.")]
        public float hideDistance = 6f;

        [Tooltip("How far from the player Biscuit runs before barking to draw the dead away.")]
        public float lureDistance = 14f;

        public int lureBarks = 2;
        public float lureBarkInterval = 1f;

        [Tooltip("Time the dead need to come to the bark before Biscuit lures again.")]
        public float lureCooldown = 15f;
    }
}
