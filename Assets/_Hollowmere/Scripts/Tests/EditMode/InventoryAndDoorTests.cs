using NUnit.Framework;
using UnityEngine;
using Hollowmere.Interaction;
using Hollowmere.Interactables;
using Hollowmere.Pickups;

namespace Hollowmere.Tests.InventoryAndDoors
{
    [TestFixture]
    public class InventoryAndDoorTests
    {
        private GameObject playerGo;
        private PlayerInventory inventory;

        [SetUp]
        public void SetUp()
        {
            playerGo = new GameObject("TestPlayer");
            playerGo.tag = "Player";
            inventory = playerGo.AddComponent<PlayerInventory>();
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
        public void PlayerInventory_StartsEmptyOfKeysAndEvidence()
        {
            Assert.AreEqual(0, inventory.Keys.Count);
            Assert.AreEqual(0, inventory.TotalEvidenceCount);
            Assert.IsFalse(inventory.HasKey("Key_MarrowHouse"));
            Assert.IsFalse(inventory.HasEvidence("Evidence_01"));
        }

        [Test]
        public void PlayerInventory_CanAddAndRemoveKeys()
        {
            inventory.AddKey("Key_MarrowHouse");
            Assert.IsTrue(inventory.HasKey("Key_MarrowHouse"));
            Assert.IsTrue(inventory.HasKey("key_marrowhouse")); // Case-insensitive
            Assert.AreEqual(1, inventory.Keys.Count);

            // Adding duplicate should not duplicate
            inventory.AddKey("Key_MarrowHouse");
            Assert.AreEqual(1, inventory.Keys.Count);

            // Removing key
            bool removed = inventory.RemoveKey("Key_MarrowHouse");
            Assert.IsTrue(removed);
            Assert.IsFalse(inventory.HasKey("Key_MarrowHouse"));
            Assert.AreEqual(0, inventory.Keys.Count);
        }

        [Test]
        public void PlayerInventory_TracksEvidenceByZone()
        {
            inventory.AddEvidence("Z1_Evidence_01");
            inventory.AddEvidence("Z1_Evidence_02");
            inventory.AddEvidence("Z1_Evidence_03");
            inventory.AddEvidence("Z2_Evidence_01");

            Assert.AreEqual(4, inventory.TotalEvidenceCount);
            Assert.AreEqual(3, inventory.GetEvidenceCountForZone("Z1"));
            Assert.AreEqual(1, inventory.GetEvidenceCountForZone("Z2"));
            Assert.AreEqual(0, inventory.GetEvidenceCountForZone("Z3"));
        }

        [Test]
        public void PlayerInventory_ConsumablesTracking()
        {
            float initialAmmo = inventory.AmmoReserve;
            int initialIce = inventory.IceCanisters;
            int initialBatteries = inventory.Batteries;

            inventory.AddAmmo(12f);
            Assert.AreEqual(initialAmmo + 12f, inventory.AmmoReserve);

            inventory.AddIceCanisters(2);
            Assert.AreEqual(initialIce + 2, inventory.IceCanisters);

            inventory.AddBatteries(1);
            Assert.AreEqual(initialBatteries + 1, inventory.Batteries);
        }

        [Test]
        public void Door_Unlocked_OpensAndClosesOnInteract()
        {
            var doorGo = new GameObject("TestDoor");
            doorGo.AddComponent<BoxCollider>();
            var door = doorGo.AddComponent<Door>();

            Assert.IsFalse(door.IsOpen);
            Assert.IsFalse(door.IsLocked);
            Assert.IsTrue(door.CanInteract(inventory));
            Assert.AreEqual("Open Door", door.Prompt);

            // Open door
            door.Interact(inventory);
            Assert.IsTrue(door.IsOpen);
            Assert.AreEqual("Close Door", door.Prompt);

            // Close door
            door.Interact(inventory);
            Assert.IsFalse(door.IsOpen);

            Object.DestroyImmediate(doorGo);
        }

        [Test]
        public void Door_Locked_RequiresKeyToUnlockAndOpen()
        {
            var doorGo = new GameObject("TestLockedDoor");
            doorGo.AddComponent<BoxCollider>();
            var rb = doorGo.AddComponent<Rigidbody>();
            var door = doorGo.AddComponent<Door>();

            // Configure locked door via reflection or fields
            var isLockedField = typeof(Door).GetField("isLocked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var reqKeyField = typeof(Door).GetField("requiredKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var rbField = typeof(Door).GetField("rb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            isLockedField.SetValue(door, true);
            reqKeyField.SetValue(door, "Key_MarrowHouse");
            rbField.SetValue(door, rb);
            rb.isKinematic = true;

            Assert.IsTrue(door.IsLocked);
            Assert.IsFalse(door.IsOpen);

            // Without key: cannot interact, kinematic remains true
            Assert.IsFalse(door.CanInteract(inventory));
            door.Interact(inventory);
            Assert.IsTrue(door.IsLocked);
            Assert.IsFalse(door.IsOpen);
            Assert.IsTrue(rb.isKinematic);

            // Acquire key
            inventory.AddKey("Key_MarrowHouse");
            Assert.IsTrue(door.CanInteract(inventory));

            // Interact unlocks and opens door, restoring non-kinematic physics
            door.Interact(inventory);
            Assert.IsFalse(door.IsLocked);
            Assert.IsTrue(door.IsOpen);
            Assert.IsFalse(rb.isKinematic);

            Object.DestroyImmediate(doorGo);
        }

        [Test]
        public void ZoneExit_RequiresThreeEvidencePagesToOpen()
        {
            var exitGo = new GameObject("ZoneExit_EstateGate");
            exitGo.AddComponent<BoxCollider>();
            var exit = exitGo.AddComponent<ZoneExit>();

            var reqCountField = typeof(ZoneExit).GetField("requiredEvidenceCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var prefixField = typeof(ZoneExit).GetField("zonePrefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            reqCountField.SetValue(exit, 3);
            prefixField.SetValue(exit, "Z1");

            bool eventFired = false;
            exit.onExitOpened.AddListener(() => eventFired = true);

            // 0 pages: sealed
            Assert.IsFalse(exit.CanInteract(inventory));
            StringAssert.Contains("Sealed", exit.Prompt);

            // 2 pages: still sealed
            inventory.AddEvidence("Z1_Evidence_01");
            inventory.AddEvidence("Z1_Evidence_02");
            Assert.IsFalse(exit.CanInteract(inventory));

            // 3 pages: unsealed!
            inventory.AddEvidence("Z1_Evidence_03");
            Assert.IsTrue(exit.CanInteract(inventory));
            Assert.AreEqual("Unlock Zone Exit", exit.Prompt);

            exit.Interact(inventory);
            Assert.IsTrue(exit.IsOpen);
            Assert.IsTrue(eventFired);

            Object.DestroyImmediate(exitGo);
        }

        [Test]
        public void ZoneExit_CanRequireKeyInAdditionToEvidence()
        {
            var exitGo = new GameObject("ZoneExit_Undercroft");
            exitGo.AddComponent<BoxCollider>();
            var exit = exitGo.AddComponent<ZoneExit>();

            var reqCountField = typeof(ZoneExit).GetField("requiredEvidenceCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var prefixField = typeof(ZoneExit).GetField("zonePrefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var keyField = typeof(ZoneExit).GetField("requiredKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            reqCountField.SetValue(exit, 3);
            prefixField.SetValue(exit, "Z2");
            keyField.SetValue(exit, "Key_TunnelExit");

            // Add 3 evidence pages but no key
            inventory.AddEvidence("Z2_Evidence_01");
            inventory.AddEvidence("Z2_Evidence_02");
            inventory.AddEvidence("Z2_Evidence_03");

            Assert.IsFalse(exit.CanInteract(inventory));
            StringAssert.Contains("Locked", exit.Prompt);

            // Add required key
            inventory.AddKey("Key_TunnelExit");
            Assert.IsTrue(exit.CanInteract(inventory));

            exit.Interact(inventory);
            Assert.IsTrue(exit.IsOpen);

            Object.DestroyImmediate(exitGo);
        }

        [Test]
        public void Pickup_Key_CollectsIntoInventoryAndDisables()
        {
            var pickupGo = new GameObject("PF_Pickup_Key");
            pickupGo.AddComponent<SphereCollider>();
            var pickup = pickupGo.AddComponent<Pickup>();

            pickup.Configure(PickupType.Key, "Key_MarrowHouse", "Marrow House Key");

            Assert.IsTrue(pickup.CanInteract(inventory));
            Assert.AreEqual("Pick up Marrow House Key", pickup.Prompt);
            Assert.IsFalse(inventory.HasKey("Key_MarrowHouse"));

            pickup.Interact(inventory);

            Assert.IsTrue(inventory.HasKey("Key_MarrowHouse"));
            Assert.IsFalse(pickupGo.activeSelf);

            Object.DestroyImmediate(pickupGo);
        }

        [Test]
        public void Pickup_AmmoAndEvidence_CollectsIntoInventory()
        {
            // Ammo pickup
            var ammoGo = new GameObject("PF_Pickup_Ammo");
            ammoGo.AddComponent<SphereCollider>();
            var ammoPickup = ammoGo.AddComponent<Pickup>();
            ammoPickup.Configure(PickupType.Ammo, "Ammo_Pistol", "Pistol Ammo", 12f);

            float prevAmmo = inventory.AmmoReserve;
            ammoPickup.Interact(inventory);
            Assert.AreEqual(prevAmmo + 12f, inventory.AmmoReserve);
            Assert.IsFalse(ammoGo.activeSelf);

            // Evidence pickup
            var evidenceGo = new GameObject("PF_Pickup_Evidence");
            evidenceGo.AddComponent<SphereCollider>();
            var evidencePickup = evidenceGo.AddComponent<Pickup>();
            evidencePickup.Configure(PickupType.EvidencePage, "Z1_Evidence_01", "Marrow's Journal Page 1");

            evidencePickup.Interact(inventory);
            Assert.IsTrue(inventory.HasEvidence("Z1_Evidence_01"));
            Assert.IsFalse(evidenceGo.activeSelf);

            Object.DestroyImmediate(ammoGo);
            Object.DestroyImmediate(evidenceGo);
        }
    }
}
