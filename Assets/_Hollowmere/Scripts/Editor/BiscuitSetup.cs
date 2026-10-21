using Hollowmere.AI.Biscuit;
using Hollowmere.AI.Biscuit.Debugging;
using Hollowmere.AI.Senses;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Hollowmere > Biscuit menu.
    /// Set Up Biscuit Prefab: rebuilds PF_Biscuit as a parent with a NavMeshAgent on the Dog NavMesh,
    /// BiscuitAgent, BiscuitDebugView and BiscuitWorldOverlay, and the model inside it. Safe to run again.
    /// Create Test Prefabs: stand-ins for testing before the real enemies and pickups exist: a test
    /// zombie that hears and groans, a test ghost, a box of ammunition and an evidence page.
    /// Spawn ...: in Play mode, puts Biscuit and the test props around the player.
    /// </summary>
    public static class BiscuitSetup
    {
        const string AgentsFolder = "Assets/_Hollowmere/Prefabs/Agents";
        const string BiscuitPrefab = AgentsFolder + "/PF_Biscuit.prefab";
        const string BiscuitModel = "Assets/_Hollowmere/Art/Models/Characters/Biscuit/SM_Biscuit.fbx";
        const string DebugFolder = AgentsFolder + "/Debug";
        const string TestDeadPrefab = DebugFolder + "/PF_TestDead.prefab";
        const string TestGhostPrefab = DebugFolder + "/PF_TestGhost.prefab";
        const string TestAmmoPrefab = DebugFolder + "/PF_TestAmmo.prefab";
        const string TestPagePrefab = DebugFolder + "/PF_TestPage.prefab";

        // Rebuilds PF_Biscuit as an empty parent (NavMeshAgent and scripts) with the model inside.
        // The model was made in Blender, where Z is up, so it needs its import turn of -90 degrees on X
        // to stand. The NavMeshAgent turns the object it sits on around Y only and would wipe that out,
        // tipping the dog nose-down, so the two must be on different objects. With the turn kept, the
        // dog's head (Blender -Y) points along the parent's forward (+Z): no extra yaw is needed.
        [MenuItem("Hollowmere/Biscuit/Set Up Biscuit Prefab", priority = 210)]
        static void SetUpBiscuit()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Stop Play first, then run Hollowmere > Biscuit > Set Up Biscuit Prefab.");
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BiscuitModel);
            if (model == null)
            {
                Debug.LogError("No model at " + BiscuitModel + ".");
                return;
            }

            var root = new GameObject("PF_Biscuit");
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = "Model";
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = model.transform.localRotation;   // the -90 on X from the import
                body.transform.localScale = Vector3.one;

                var agent = root.AddComponent<NavMeshAgent>();
                int dog = AgentType("Dog");
                if (dog != int.MinValue) agent.agentTypeID = dog;
                else Debug.LogWarning("No 'Dog' NavMesh agent type found; PF_Biscuit keeps its current agent type.");

                // The Dog agent type: 0.25 m radius, 0.7 m tall (see the Agent Controller guide).
                agent.radius = 0.25f;
                agent.height = 0.7f;
                agent.baseOffset = 0f;
                agent.speed = 2.5f;
                agent.acceleration = 12f;
                agent.angularSpeed = 540f;
                agent.stoppingDistance = 2f;
                agent.autoBraking = true;

                root.AddComponent<BiscuitAgent>();
                root.AddComponent<BiscuitDebugView>();
                root.AddComponent<BiscuitWorldOverlay>();

                PrefabUtility.SaveAsPrefabAsset(root, BiscuitPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log("PF_Biscuit rebuilt: parent with NavMeshAgent (Dog), BiscuitAgent, BiscuitDebugView and " +
                      "BiscuitWorldOverlay; the model stands upright inside it. If Unity asks to save the open " +
                      "scene, choose Don't Save.");
        }

        [MenuItem("Hollowmere/Biscuit/Create Test Prefabs", priority = 211)]
        static void CreateTestPrefabs()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Stop Play first, then run Hollowmere > Biscuit > Create Test Prefabs.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(DebugFolder)) AssetDatabase.CreateFolder(AgentsFolder, "Debug");

            // A capsule's pivot is its centre, so the zombie's agent is lifted by half its height.
            SaveTestPrefab(TestDeadPrefab, PrimitiveType.Capsule, dead =>
            {
                var agent = dead.AddComponent<NavMeshAgent>();
                agent.agentTypeID = 0;   // Humanoid
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.baseOffset = 1f;
                agent.speed = 1.4f;
                SetKind(dead.AddComponent<ScentSource>(), EnemyKind.Dead);
                dead.AddComponent<TestDead>();
            });

            // Ghosts pass through walls, so no collider and no NavMeshAgent: it just hangs there.
            SaveTestPrefab(TestGhostPrefab, PrimitiveType.Capsule, ghost =>
            {
                Object.DestroyImmediate(ghost.GetComponent<Collider>());
                ghost.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
                SetKind(ghost.AddComponent<ScentSource>(), EnemyKind.Ghost);
            });

            SaveTestPrefab(TestAmmoPrefab, PrimitiveType.Cube, ammo =>
            {
                ammo.transform.localScale = new Vector3(0.3f, 0.15f, 0.2f);
                SetPoint(ammo.AddComponent<PointOfInterest>(), PointKind.Ammo, "a box of magazines");
            });

            SaveTestPrefab(TestPagePrefab, PrimitiveType.Cube, page =>
            {
                page.transform.localScale = new Vector3(0.25f, 0.02f, 0.35f);
                SetPoint(page.AddComponent<PointOfInterest>(), PointKind.Evidence, "a torn diary page");
            });

            Debug.Log("Created PF_TestDead, PF_TestGhost, PF_TestAmmo and PF_TestPage in " + DebugFolder +
                      ". If Unity asks to save the open scene, choose Don't Save.");
        }

        [MenuItem("Hollowmere/Biscuit/Spawn Test Near Player (in Play mode)", priority = 230)]
        static void SpawnTestNearPlayer()
        {
            if (!TryGetPlayer(out GameObject player)) return;

            var biscuitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BiscuitPrefab);
            if (biscuitPrefab == null || biscuitPrefab.GetComponent<BiscuitAgent>() == null)
            {
                Debug.LogError("Stop Play and run Hollowmere > Biscuit > Set Up Biscuit Prefab first.");
                return;
            }
            if (!TryLoad(TestDeadPrefab, out GameObject deadPrefab)) return;

            // The debug walker has no health or ammunition of its own; this gives it test keys and lets
            // it take the ammunition Biscuit brings.
            if (player.GetComponent<IPlayerState>() == null) player.AddComponent<BiscuitTestPlayer>();

            Axes(player, out Vector3 forward, out Vector3 right);
            Vector3 at = player.transform.position;
            int dog = AgentType("Dog");
            GameObject biscuit = Spawn(biscuitPrefab, at - forward * 2f + right, dog != int.MinValue ? dog : 0, 0f);
            Spawn(deadPrefab, at + forward * 15f + right * 4f, 0, 0f);
            Spawn(deadPrefab, at + forward * 18f - right * 5f, 0, 0f);

            Selection.activeGameObject = biscuit;
            Debug.Log("Biscuit is behind you and two test dead are 15-18 m ahead. Click in the Game view and " +
                      "press F2. K/L change health, N/M ammunition.");
        }

        [MenuItem("Hollowmere/Biscuit/Spawn Test Ghost Near Player (in Play mode)", priority = 231)]
        static void SpawnTestGhost()
        {
            if (!TryGetPlayer(out GameObject player) || !TryLoad(TestGhostPrefab, out GameObject ghostPrefab)) return;

            Axes(player, out Vector3 forward, out Vector3 right);
            Spawn(ghostPrefab, player.transform.position + forward * 9f + right * 2f, 0, 1.2f);
            Debug.Log("A test ghost hangs 9 m ahead. Biscuit should whine and back away instead of growling.");
        }

        [MenuItem("Hollowmere/Biscuit/Spawn Test Ammo And Page Near Player (in Play mode)", priority = 232)]
        static void SpawnTestSupplies()
        {
            if (!TryGetPlayer(out GameObject player) ||
                !TryLoad(TestAmmoPrefab, out GameObject ammoPrefab) ||
                !TryLoad(TestPagePrefab, out GameObject pagePrefab))
                return;

            Axes(player, out Vector3 forward, out Vector3 right);
            Vector3 at = player.transform.position;
            Spawn(ammoPrefab, at + right * 12f, 0, 0.08f);
            Spawn(pagePrefab, at - right * 14f + forward * 4f, 0, 0.02f);
            Debug.Log("A box of ammunition is 12 m to your right and a page 14 m to your left. Press N until " +
                      "ammunition is below 0.3 and Biscuit fetches it; when it is quiet it leads you to the page.");
        }

        // ---------------------------------------------------------------- helpers

        static void SaveTestPrefab(string path, PrimitiveType shape, System.Action<GameObject> build)
        {
            GameObject prop = GameObject.CreatePrimitive(shape);
            prop.name = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                build(prop);
                PrefabUtility.SaveAsPrefabAsset(prop, path);
            }
            finally
            {
                Object.DestroyImmediate(prop);
            }
        }

        static void SetKind(ScentSource scent, EnemyKind kind)
        {
            var serialized = new SerializedObject(scent);
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetPoint(PointOfInterest point, PointKind kind, string displayName)
        {
            var serialized = new SerializedObject(point);
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool TryGetPlayer(out GameObject player)
        {
            player = null;
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Press Play first, then use the Hollowmere > Biscuit > Spawn menu.");
                return false;
            }
            player = GameObject.FindWithTag("Player");
            if (player == null) Debug.LogError("Nothing in the scene is tagged Player.");
            return player != null;
        }

        static bool TryLoad(string path, out GameObject prefab)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogError("Stop Play and run Hollowmere > Biscuit > Create Test Prefabs first.");
            return prefab != null;
        }

        static void Axes(GameObject player, out Vector3 forward, out Vector3 right)
        {
            forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            right = Vector3.Cross(Vector3.up, forward);
        }

        // Places a copy on the NavMesh near a point, lifted by half its height if its pivot is its centre.
        static GameObject Spawn(GameObject prefab, Vector3 near, int agentType, float lift)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            Vector3 at = NavMesh.SamplePosition(near, out NavMeshHit hit, 8f, filter) ? hit.position : near;
            return Object.Instantiate(prefab, at + Vector3.up * lift, Quaternion.identity);
        }

        static int AgentType(string name)
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == name) return id;
            }
            return int.MinValue;
        }
    }
}
