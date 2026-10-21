using System;
using UnityEngine;
using UnityEngine.Assertions;
using Hollowmere.AI.Senses;

namespace Hollowmere.AI.Zombie
{
    /// <summary>
    /// Play-mode verification and unit test suite for ZombieMemory and acoustic sensory handling.
    /// Provides concrete unit test evidence for the viva examination (SE3062 Intelligent Systems).
    /// Can be attached to an object in SB_Luhansa to run on Play, or executed from Inspector context menu.
    /// </summary>
    public class ZombieMemoryTests : MonoBehaviour
    {
        [Header("Test Runner Options")]
        [Tooltip("Automatically execute test suite on Start in Play mode.")]
        [SerializeField] private bool runOnStart = true;

        [Tooltip("Log detailed step-by-step outputs for each test.")]
        [SerializeField] private bool verboseLogs = true;

        private void Start()
        {
            if (runOnStart)
            {
                RunAllTests();
            }
        }

        /// <summary>
        /// Executes the entire ZombieMemory unit test suite.
        /// Right-click component in Inspector -> 'Run Zombie Memory Tests'.
        /// </summary>
        [ContextMenu("Run Zombie Memory Tests")]
        public void RunAllTests()
        {
            Debug.Log("<color=cyan><b>[ZombieMemoryTests]</b> Starting test suite execution...</color>");
            int passed = 0;
            int total = 7;

            try
            {
                Test_OnHeard_Gunshot_UpdatesBeliefWithHighConfidence();
                passed++;

                Test_OnHeard_Groan_SetsSourceToToldAndConfidenceTo40();
                passed++;

                Test_OnHeard_LowerConfidenceNoise_DoesNotOverwriteStrongerBelief();
                passed++;

                Test_OnHeard_HigherConfidenceNoise_OverwritesWeakerBelief();
                passed++;

                Test_ClearBelief_ResetsBeliefToEmpty();
                passed++;

                Test_ExponentialDecay_FadesConfidenceCorrectly();
                passed++;

                Test_ExponentialDecay_ClearsBeliefBelowMinConfidence();
                passed++;

                Debug.Log($"<color=green><b>[ZombieMemoryTests] ALL {passed}/{total} TESTS PASSED!</b></color>");
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red><b>[ZombieMemoryTests] FAILED:</b> {ex.Message}\n{ex.StackTrace}</color>");
            }
        }

        /// <summary>
        /// Test 1: Calling OnHeard with a Gunshot updates belief position, sets confidence to 0.60,
        /// and marks source as Heard.
        /// </summary>
        public void Test_OnHeard_Gunshot_UpdatesBeliefWithHighConfidence()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 gunshotPos = new Vector3(12.5f, 0f, 24.0f);
            var noise = new Noise(gunshotPos, Loudness.Gunshot, NoiseKind.Gunshot, null);

            // Act
            memory.OnHeard(in noise, pathDistance: 15.0f);

            // Assert
            ZombieBelief belief = memory.GetBelief();
            Assert.IsTrue(belief.IsValid, "Belief must be valid after hearing gunshot.");
            Assert.AreEqual(gunshotPos, belief.Position, "Belief position must match gunshot location.");
            Assert.AreApproximatelyEqual(0.60f, belief.Confidence, 0.001f, "Gunshot confidence must be 0.60.");
            Assert.AreEqual(BeliefSource.Heard, belief.Source, "Belief source must be Heard.");

            LogPass("Test_OnHeard_Gunshot_UpdatesBeliefWithHighConfidence");
        }

        /// <summary>
        /// Test 2: Calling OnHeard with a ZombieGroan (neighbour alert) sets BeliefSource to Told
        /// and assigns 0.40 confidence (emergent horde mechanic).
        /// </summary>
        public void Test_OnHeard_Groan_SetsSourceToToldAndConfidenceTo40()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 groanPos = new Vector3(-8.0f, 1.5f, 14.0f);
            var noise = new Noise(groanPos, Loudness.Groan, NoiseKind.Groan, null);

            // Act
            memory.OnHeard(in noise, pathDistance: 18.0f);

            // Assert
            ZombieBelief belief = memory.GetBelief();
            Assert.IsTrue(belief.IsValid, "Belief must be valid after hearing groan.");
            Assert.AreEqual(groanPos, belief.Position, "Belief position must match groan location.");
            Assert.AreApproximatelyEqual(0.40f, belief.Confidence, 0.001f, "Groan confidence must be 0.40.");
            Assert.AreEqual(BeliefSource.Told, belief.Source, "Groan belief source must be Told (emergent horde).");

            LogPass("Test_OnHeard_Groan_SetsSourceToToldAndConfidenceTo40");
        }

        /// <summary>
        /// Test 3: If zombie already holds a high-confidence belief (e.g. Gunshot = 0.60),
        /// hearing a lower-confidence noise (e.g. Groan = 0.40) does NOT overwrite the stronger belief.
        /// </summary>
        public void Test_OnHeard_LowerConfidenceNoise_DoesNotOverwriteStrongerBelief()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 gunshotPos = new Vector3(10f, 0f, 10f);
            var gunshotNoise = new Noise(gunshotPos, Loudness.Gunshot, NoiseKind.Gunshot, null);
            memory.OnHeard(in gunshotNoise, pathDistance: 5.0f);

            Vector3 groanPos = new Vector3(50f, 0f, 50f);
            var groanNoise = new Noise(groanPos, Loudness.Groan, NoiseKind.Groan, null);

            // Act: Hear weaker groan
            memory.OnHeard(in groanNoise, pathDistance: 10.0f);

            // Assert: Gunshot belief retained
            ZombieBelief belief = memory.GetBelief();
            Assert.AreEqual(gunshotPos, belief.Position, "Stronger gunshot position must NOT be overwritten by weaker groan.");
            Assert.AreApproximatelyEqual(0.60f, belief.Confidence, 0.001f, "Confidence must remain 0.60.");
            Assert.AreEqual(BeliefSource.Heard, belief.Source, "Source must remain Heard.");

            LogPass("Test_OnHeard_LowerConfidenceNoise_DoesNotOverwriteStrongerBelief");
        }

        /// <summary>
        /// Test 4: If zombie holds a moderate belief (e.g. Footstep = 0.55),
        /// hearing a higher-confidence noise (e.g. Gunshot = 0.60) DOES overwrite it.
        /// </summary>
        public void Test_OnHeard_HigherConfidenceNoise_OverwritesWeakerBelief()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 footstepPos = new Vector3(2f, 0f, 2f);
            var footstepNoise = new Noise(footstepPos, Loudness.Run, NoiseKind.Footstep, null);
            memory.OnHeard(in footstepNoise, pathDistance: 4.0f);

            Vector3 gunshotPos = new Vector3(30f, 0f, 30f);
            var gunshotNoise = new Noise(gunshotPos, Loudness.Gunshot, NoiseKind.Gunshot, null);

            // Act: Hear louder gunshot
            memory.OnHeard(in gunshotNoise, pathDistance: 15.0f);

            // Assert: Overwritten by gunshot
            ZombieBelief belief = memory.GetBelief();
            Assert.AreEqual(gunshotPos, belief.Position, "Higher confidence gunshot must overwrite footstep position.");
            Assert.AreApproximatelyEqual(0.60f, belief.Confidence, 0.001f, "Confidence must upgrade to 0.60.");

            LogPass("Test_OnHeard_HigherConfidenceNoise_OverwritesWeakerBelief");
        }

        /// <summary>
        /// Test 5: ClearBelief resets the belief state to Empty with 0 confidence.
        /// </summary>
        public void Test_ClearBelief_ResetsBeliefToEmpty()
        {
            // Arrange
            var memory = new ZombieMemory();
            var noise = new Noise(new Vector3(5f, 0f, 5f), Loudness.Gunshot, NoiseKind.Gunshot, null);
            memory.OnHeard(in noise, 10f);
            Assert.IsTrue(memory.HasBelief, "Precondition: memory must have belief.");

            // Act
            memory.ClearBelief();

            // Assert
            Assert.IsFalse(memory.HasBelief, "Memory must report HasBelief == false after ClearBelief.");
            Assert.AreEqual(0f, memory.GetBelief().Confidence, "Confidence must be 0 after clear.");
            Assert.AreEqual(BeliefSource.None, memory.GetBelief().Source, "Source must be None.");

            LogPass("Test_ClearBelief_ResetsBeliefToEmpty");
        }

        /// <summary>
        /// Test 6: Confidence decays exponentially according to c = c0 * exp(-elapsed / tau).
        /// At tau = 12s, after 12s confidence should be ~36.8% of c0.
        /// </summary>
        public void Test_ExponentialDecay_FadesConfidenceCorrectly()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 pos = new Vector3(10f, 0f, 10f);
            float c0 = 1.0f;
            memory.UpdateBelief(pos, c0, BeliefSource.Seen, timestamp: 0f);

            // Act: Tick at t = 12s (elapsed = 1 tau = 12s)
            memory.Tick(currentTime: 12f);

            // Assert: c = 1.0 * exp(-1) = 0.367879...
            ZombieBelief belief1Tau = memory.GetBelief();
            float expected1Tau = c0 * Mathf.Exp(-12f / 12f); // ~0.368f
            Assert.IsTrue(belief1Tau.IsValid, "Belief must still be valid after 1 tau.");
            Assert.AreApproximatelyEqual(expected1Tau, belief1Tau.Confidence, 0.01f, "Confidence after 1 tau must match c0 * exp(-1).");

            // Act: Tick at t = 24s (elapsed = 2 tau = 24s)
            memory.Tick(currentTime: 24f);

            // Assert: c = 1.0 * exp(-2) = 0.1353...
            ZombieBelief belief2Tau = memory.GetBelief();
            float expected2Tau = c0 * Mathf.Exp(-24f / 12f); // ~0.135f
            Assert.IsTrue(belief2Tau.IsValid, "Belief must still be valid after 2 tau.");
            Assert.AreApproximatelyEqual(expected2Tau, belief2Tau.Confidence, 0.01f, "Confidence after 2 tau must match c0 * exp(-2).");

            LogPass("Test_ExponentialDecay_FadesConfidenceCorrectly");
        }

        /// <summary>
        /// Test 7: When confidence decays below minConfidence (0.1), the belief is forgotten
        /// and memory clears itself to Empty.
        /// </summary>
        public void Test_ExponentialDecay_ClearsBeliefBelowMinConfidence()
        {
            // Arrange
            var memory = new ZombieMemory();
            Vector3 pos = new Vector3(15f, 0f, 25f);
            memory.UpdateBelief(pos, 0.60f, BeliefSource.Heard, timestamp: 0f);

            // At tau = 12s, c0 = 0.60:
            // c(28s) = 0.60 * exp(-28/12) = 0.60 * 0.0969 = 0.058 < 0.10 (minConfidence)
            // Act: Tick at t = 28s
            memory.Tick(currentTime: 28f);

            // Assert: Belief has fallen below minConfidence and must be forgotten/cleared
            ZombieBelief belief = memory.GetBelief();
            Assert.IsFalse(memory.HasBelief, "Memory must clear belief when confidence falls below minConfidence (0.1).");
            Assert.AreEqual(0f, belief.Confidence, "Confidence must be 0 after memory fadeout.");

            LogPass("Test_ExponentialDecay_ClearsBeliefBelowMinConfidence");
        }

        private void LogPass(string testName)
        {
            if (verboseLogs)
            {
                Debug.Log($"<color=lime>  [PASS]</color> {testName}");
            }
        }
    }
}
