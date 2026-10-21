using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Hollowmere.AI.Senses;
using Hollowmere.Player;

namespace Hollowmere.Tests.Player
{
    /// <summary>
    /// The first-person weapons: the player prefab's slots are filled, the viewmodels are cheap and wired,
    /// and the events and curves they run on behave.
    /// </summary>
    public class ViewmodelTests
    {
        const string PlayerPrefab = "Assets/_Hollowmere/Prefabs/PF_Player.prefab";
        const string PistolPrefab = "Assets/_Hollowmere/Prefabs/Player/PF_Viewmodel_Pistol.prefab";
        const string IceGunPrefab = "Assets/_Hollowmere/Prefabs/Player/PF_Viewmodel_IceGun.prefab";

        static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Missing {path}. Run Hollowmere > Weapons > Set Up Viewmodels.");
            return prefab;
        }

        static Object Field(Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;

        [Test]
        public void PlayerPrefab_FillsBothWeaponSlotsUnderTheHead()
        {
            var player = Load(PlayerPrefab);
            var weapons = player.GetComponent<WeaponSystem>();
            var head = player.transform.Find("Head");

            var pistol = Field(weapons, "pistolModel") as GameObject;
            var iceGun = Field(weapons, "iceGunModel") as GameObject;

            Assert.IsNotNull(pistol, "Pistol Model slot is empty.");
            Assert.IsNotNull(iceGun, "Ice Gun Model slot is empty.");
            Assert.IsTrue(pistol.transform.IsChildOf(head), "The pistol should hang under the Head camera.");
            Assert.IsTrue(iceGun.transform.IsChildOf(head), "The ice gun should hang under the Head camera.");
            Assert.AreEqual(WeaponKind.Pistol, pistol.GetComponent<WeaponViewmodel>().Kind);
            Assert.AreEqual(WeaponKind.IceGun, iceGun.GetComponent<WeaponViewmodel>().Kind);
        }

        [Test]
        public void PlayerPrefab_HasACrosshair()
        {
            Assert.IsNotNull(Load(PlayerPrefab).GetComponent<Crosshair>());
        }

        [TestCase(PistolPrefab)]
        [TestCase(IceGunPrefab)]
        public void Viewmodel_DrawsOnItsOwnLayerWithoutShadowsOrColliders(string path)
        {
            var viewmodel = Load(path);
            foreach (var renderer in viewmodel.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode,
                                $"{renderer.name} would cast a shadow across the torch beam.");
                if (renderer is MeshRenderer)
                    Assert.AreEqual(WeaponViewmodel.RenderingLayer, renderer.renderingLayerMask,
                                    $"{renderer.name} would be burnt white by the torch a few centimetres away.");
            }
            Assert.IsEmpty(viewmodel.GetComponentsInChildren<Collider>(true),
                           "A collider on the viewmodel would block the player's own shots and the AI's sight lines.");
        }

        [TestCase(PistolPrefab, 12000)]
        [TestCase(IceGunPrefab, 12000)]
        public void Viewmodel_StaysWithinItsTriangleBudget(string path, int budget)
        {
            int triangles = 0;
            foreach (var filter in Load(path).GetComponentsInChildren<MeshFilter>(true))
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                    triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
            Assert.Less(triangles, budget, $"{path}: {triangles} triangles for the gun and both arms.");
        }

        [Test]
        public void Pistol_HasItsMovingPartsSoundsAndFlash()
        {
            var viewmodel = Load(PistolPrefab).GetComponent<WeaponViewmodel>();
            foreach (var field in new[] { "pose", "slide", "magazine", "leftArm", "fillLight", "muzzleFlash", "shotLight",
                                          "audioSource", "fireClip", "emptyClip", "reloadClip", "equipClip" })
                Assert.IsNotNull(Field(viewmodel, field), $"Pistol viewmodel: {field} is not set.");
        }

        [Test]
        public void IceGun_HasItsSprayAndSounds()
        {
            var viewmodel = Load(IceGunPrefab).GetComponent<WeaponViewmodel>();
            var effects = new SerializedObject(viewmodel).FindProperty("fireEffects");
            Assert.GreaterOrEqual(effects.arraySize, 1, "The ice gun needs its spray.");
            for (int i = 0; i < effects.arraySize; i++)
                Assert.IsNotNull(effects.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (var field in new[] { "pose", "leftArm", "fillLight", "audioSource", "fireClip", "emptyClip", "reloadClip" })
                Assert.IsNotNull(Field(viewmodel, field), $"Ice gun viewmodel: {field} is not set.");
        }

        [Test]
        public void Envelope_RisesHoldsAndFallsBackToRest()
        {
            Assert.AreEqual(0f, WeaponViewmodel.Envelope(0f, 0.1f, 0.3f, 0.6f, 0.8f), 1e-5f);
            Assert.AreEqual(1f, WeaponViewmodel.Envelope(0.45f, 0.1f, 0.3f, 0.6f, 0.8f), 1e-5f);
            Assert.AreEqual(0f, WeaponViewmodel.Envelope(1f, 0.1f, 0.3f, 0.6f, 0.8f), 1e-5f);
            float rising = WeaponViewmodel.Envelope(0.2f, 0.1f, 0.3f, 0.6f, 0.8f);
            Assert.That(rising, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void AddAmmo_RaisesAmmoAdded_OnlyWhenSomethingCameIn()
        {
            var go = new GameObject("TestPlayer");
            try
            {
                go.AddComponent<CharacterController>();
                go.AddComponent<PlayerNoiseEmitter>();
                go.AddComponent<PlayerController>();
                var weapons = go.AddComponent<WeaponSystem>();
                int raised = 0;
                weapons.OnAmmoAdded += () => raised++;

                weapons.AddAmmo(0.25f);     // the pistol starts half full, so this adds rounds
                Assert.AreEqual(1, raised);
                weapons.AddAmmo(1f);        // fills it
                weapons.AddAmmo(1f);        // nothing left to add
                Assert.AreEqual(2, raised);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
