using NUnit.Framework;
using UnityEngine;
using Hollowmere.Player;
using Hollowmere.Combat;
using Hollowmere.AI.Zombie;
using Hollowmere.AI.Senses;

namespace Hollowmere.Tests.Combat
{
    [TestFixture]
    public class CombatTests
    {
        private GameObject playerGo;
        private PlayerController playerController;
        private PlayerHealth playerHealth;
        private WeaponSystem weaponSystem;

        [SetUp]
        public void SetUp()
        {
            playerGo = new GameObject("TestPlayer");
            playerGo.AddComponent<CharacterController>();
            playerGo.AddComponent<PlayerNoiseEmitter>();
            playerController = playerGo.AddComponent<PlayerController>();
            playerHealth = playerGo.AddComponent<PlayerHealth>();
            weaponSystem = playerGo.AddComponent<WeaponSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (playerGo != null)
            {
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void PlayerHealth_StartsAtFullCapacity()
        {
            Assert.AreEqual(1.0f, playerHealth.Health01, 0.001f);
            Assert.AreEqual(100f, playerHealth.CurrentHealth, 0.001f);
            Assert.IsFalse(playerHealth.IsDead);
            Assert.AreEqual(1.0f, playerController.Health01, 0.001f);
        }

        [Test]
        public void PlayerHealth_TakesDamageAndSyncsWithController()
        {
            playerHealth.TakeDamage(30f, Vector3.zero, Vector3.forward);

            Assert.AreEqual(70f, playerHealth.CurrentHealth, 0.001f);
            Assert.AreEqual(0.7f, playerHealth.Health01, 0.001f);
            Assert.AreEqual(0.7f, playerController.Health01, 0.001f);
            Assert.IsFalse(playerHealth.IsDead);
        }

        [Test]
        public void PlayerHealth_LethalDamageTriggersDeath()
        {
            bool died = false;
            playerHealth.OnDeath += () => died = true;

            playerHealth.TakeDamage(120f, Vector3.zero, Vector3.forward);

            Assert.AreEqual(0f, playerHealth.CurrentHealth, 0.001f);
            Assert.AreEqual(0f, playerHealth.Health01, 0.001f);
            Assert.IsTrue(playerHealth.IsDead);
            Assert.IsTrue(died);
            Assert.IsFalse(playerController.enabled, "PlayerController locomotion should be disabled upon death");
        }

        [Test]
        public void PlayerHealth_HealsUpToMaximum()
        {
            playerHealth.TakeDamage(50f, Vector3.zero, Vector3.forward);
            Assert.AreEqual(50f, playerHealth.CurrentHealth, 0.001f);

            playerHealth.Heal(25f);
            Assert.AreEqual(75f, playerHealth.CurrentHealth, 0.001f);

            playerHealth.Heal(100f);
            Assert.AreEqual(100f, playerHealth.CurrentHealth, 0.001f);
        }

        [Test]
        public void PlayerController_ReceivesAmmoThroughInterface()
        {
            // Initial ammo
            float initialAmmo = playerController.Ammo01;
            playerController.ReceiveAmmo(0.25f);

            Assert.GreaterOrEqual(playerController.Ammo01, initialAmmo);
        }

        [Test]
        public void ZombieBody_ImplementsDamageableAndIceable()
        {
            var zombieGo = new GameObject("TestZombie");
            zombieGo.AddComponent<UnityEngine.AI.NavMeshAgent>();
            var zombie = zombieGo.AddComponent<ZombieBody>();

            Assert.IsInstanceOf<IDamageable>(zombie);
            Assert.IsInstanceOf<IIceable>(zombie);
            Assert.AreEqual(1.0f, zombie.Health01, 0.001f);
            Assert.IsFalse(zombie.IsDead);

            // Damage zombie
            zombie.TakeDamage(40f, Vector3.zero, Vector3.forward);
            Assert.AreEqual(0.6f, zombie.Health01, 0.001f);
            Assert.IsFalse(zombie.IsDead);

            // Lethal damage kills zombie
            zombie.TakeDamage(80f, Vector3.zero, Vector3.forward);
            Assert.AreEqual(0f, zombie.Health01, 0.001f);
            Assert.IsTrue(zombie.IsDead);

            Object.DestroyImmediate(zombieGo);
        }
    }
}
