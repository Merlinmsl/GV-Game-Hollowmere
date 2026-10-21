using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Hollowmere.Interaction;
using Hollowmere.Interactables;
using Hollowmere.Pickups;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Editor utility to generate or update canonical prefabs for Systems Engineer deliverables:
    /// - Pickups: Keys, Ammo, Ice Canisters, Batteries, Evidence Pages (Assets/_Hollowmere/Prefabs/Pickups/)
    /// - Doors & Exits: PF_Door_Wood, PF_Hatch_Study, PF_ZoneExit (Assets/_Hollowmere/Prefabs/Interactables/)
    /// Defined in Docs/Roles/SystemsEngineer.md.
    /// </summary>
    public static class InteractableAndPickupSetup
    {
        private const string PickupsFolder = "Assets/_Hollowmere/Prefabs/Pickups";
        private const string InteractablesFolder = "Assets/_Hollowmere/Prefabs/Interactables";

        [MenuItem("Hollowmere/Systems/Generate Pickups and Doors Prefabs", priority = 250)]
        public static void GenerateAll()
        {
            EnsureFolder(PickupsFolder);
            EnsureFolder(InteractablesFolder);

            CreatePickupPrefab("PF_Pickup_Key", PickupType.Key, "Key_MarrowHouse", "House Key", 1f, new Vector3(0.1f, 0.05f, 0.25f), Color.yellow);
            CreatePickupPrefab("PF_Pickup_Ammo", PickupType.Ammo, "Ammo_Pistol", "Pistol Ammo", 12f, new Vector3(0.2f, 0.15f, 0.25f), new Color(1f, 0.5f, 0.1f));
            CreatePickupPrefab("PF_Pickup_IceCanister", PickupType.IceCanister, "Ice_Canister", "Ice Canister", 1f, new Vector3(0.15f, 0.35f, 0.15f), Color.cyan);
            CreatePickupPrefab("PF_Pickup_Battery", PickupType.Battery, "Battery_Flashlight", "Flashlight Battery", 1f, new Vector3(0.1f, 0.2f, 0.1f), new Color(0.2f, 0.9f, 0.3f));
            CreatePickupPrefab("PF_Pickup_EvidencePage", PickupType.EvidencePage, "Evidence_Marrow_01", "Evidence Document", 1f, new Vector3(0.25f, 0.02f, 0.35f), Color.white);

            CreateDoorWoodPrefab();
            CreateHatchStudyPrefab();
            CreateZoneExitPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green><b>[InteractableAndPickupSetup]</b> Successfully generated all pickup and door prefabs!</color>");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace("\\", "/");
                string leaf = Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        private static void CreatePickupPrefab(string prefabName, PickupType type, string id, string displayName, float amount, Vector3 size, Color gizmoColor)
        {
            string prefabPath = $"{PickupsFolder}/{prefabName}.prefab";
            GameObject go = new GameObject(prefabName);

            // Trigger collider
            SphereCollider col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = Mathf.Max(size.x, size.y, size.z) * 0.8f;

            // Visual mesh
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = size;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            // Audio source
            AudioSource audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            // Pickup script
            Pickup pickup = go.AddComponent<Pickup>();
            pickup.Configure(type, id, displayName, amount);

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"Generated prefab: {prefabPath}");
        }

        private static void CreateDoorWoodPrefab()
        {
            string prefabPath = $"{InteractablesFolder}/PF_Door_Wood.prefab";
            GameObject frame = new GameObject("PF_Door_Wood");

            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(frame.transform, false);
            leaf.transform.localPosition = new Vector3(0.45f, 1.1f, 0f);
            leaf.transform.localScale = new Vector3(0.9f, 2.2f, 0.1f);

            var rb = leaf.AddComponent<Rigidbody>();
            rb.mass = 25f;
            rb.linearDamping = 1f;
            rb.angularDamping = 2f;

            var hinge = leaf.AddComponent<HingeJoint>();
            hinge.anchor = new Vector3(-0.5f, 0f, 0f);
            hinge.axis = Vector3.up;
            hinge.useLimits = true;
            JointLimits limits = new JointLimits { min = 0f, max = 100f };
            hinge.limits = limits;
            hinge.useSpring = true;
            JointSpring spring = new JointSpring { spring = 40f, damper = 8f, targetPosition = 0f };
            hinge.spring = spring;

            var navObstacle = leaf.AddComponent<NavMeshObstacle>();
            navObstacle.carving = true;
            navObstacle.size = new Vector3(1f, 2.2f, 0.2f);

            leaf.AddComponent<AudioSource>();
            var door = leaf.AddComponent<Door>();

            PrefabUtility.SaveAsPrefabAsset(frame, prefabPath);
            Object.DestroyImmediate(frame);
            Debug.Log($"Generated prefab: {prefabPath}");
        }

        private static void CreateHatchStudyPrefab()
        {
            string prefabPath = $"{InteractablesFolder}/PF_Hatch_Study.prefab";
            GameObject frame = new GameObject("PF_Hatch_Study");

            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "HatchLeaf";
            leaf.transform.SetParent(frame.transform, false);
            leaf.transform.localPosition = new Vector3(0f, 0.05f, 1.5f);
            leaf.transform.localScale = new Vector3(3f, 0.1f, 8f);

            var rb = leaf.AddComponent<Rigidbody>();
            rb.mass = 60f;
            rb.isKinematic = true; // Locked until tunnel key found

            var hinge = leaf.AddComponent<HingeJoint>();
            hinge.anchor = new Vector3(0f, 0f, -0.5f);
            hinge.axis = Vector3.right;
            hinge.useLimits = true;
            JointLimits limits = new JointLimits { min = 0f, max = 85f };
            hinge.limits = limits;

            var navObstacle = leaf.AddComponent<NavMeshObstacle>();
            navObstacle.carving = true;

            leaf.AddComponent<AudioSource>();
            var door = leaf.AddComponent<Door>();

            // Set default locked state for study hatch
            var isLockedField = typeof(Door).GetField("isLocked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var reqKeyField = typeof(Door).GetField("requiredKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var rotAxisField = typeof(Door).GetField("rotationAxis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            isLockedField?.SetValue(door, true);
            reqKeyField?.SetValue(door, "Key_Tunnel");
            rotAxisField?.SetValue(door, Vector3.right);

            PrefabUtility.SaveAsPrefabAsset(frame, prefabPath);
            Object.DestroyImmediate(frame);
            Debug.Log($"Generated prefab: {prefabPath}");
        }

        private static void CreateZoneExitPrefab()
        {
            string prefabPath = $"{InteractablesFolder}/PF_ZoneExit.prefab";
            GameObject exit = new GameObject("PF_ZoneExit");

            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(4f, 3f, 2f);

            GameObject gateLeaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateLeaf.name = "GateLeaf";
            gateLeaf.transform.SetParent(exit.transform, false);
            gateLeaf.transform.localScale = new Vector3(3.8f, 2.8f, 0.2f);

            var barrierCol = gateLeaf.GetComponent<BoxCollider>();

            var zoneExit = exit.AddComponent<ZoneExit>();
            var gateField = typeof(ZoneExit).GetField("gateLeaf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var barrierField = typeof(ZoneExit).GetField("barrierCollider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            gateField?.SetValue(zoneExit, gateLeaf.transform);
            barrierField?.SetValue(zoneExit, barrierCol);

            exit.AddComponent<AudioSource>();

            PrefabUtility.SaveAsPrefabAsset(exit, prefabPath);
            Object.DestroyImmediate(exit);
            Debug.Log($"Generated prefab: {prefabPath}");
        }
    }
}
