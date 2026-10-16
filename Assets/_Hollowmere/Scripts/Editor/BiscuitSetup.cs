using Hollowmere.AI.Biscuit;
using Hollowmere.AI.Biscuit.Debugging;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Hollowmere > Biscuit menu.
    /// Set Up Biscuit Prefab: gives PF_Biscuit a NavMeshAgent on the Dog NavMesh, BiscuitAgent and
    /// BiscuitDebugView. Safe to run again: it only adds what is missing and resets the agent's size.
    /// Create Test Dead Prefab: makes PF_TestDead, a capsule that hears and groans, for testing
    /// Biscuit before the real zombie is merged.
    /// </summary>
    public static class BiscuitSetup
    {
        const string AgentsFolder = "Assets/_Hollowmere/Prefabs/Agents";
        const string BiscuitPrefab = AgentsFolder + "/PF_Biscuit.prefab";
        const string DebugFolder = AgentsFolder + "/Debug";
        const string TestDeadPrefab = DebugFolder + "/PF_TestDead.prefab";

        [MenuItem("Hollowmere/Biscuit/Set Up Biscuit Prefab", priority = 210)]
        static void SetUpBiscuit()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BiscuitPrefab) == null)
            {
                Debug.LogError("No prefab at " + BiscuitPrefab + ". Run Hollowmere > Characters > Set Up Characters first.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BiscuitPrefab);
            try
            {
                var agent = GetOrAdd<NavMeshAgent>(root);
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

                GetOrAdd<BiscuitAgent>(root);
                GetOrAdd<BiscuitDebugView>(root);

                PrefabUtility.SaveAsPrefabAsset(root, BiscuitPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log("PF_Biscuit set up: NavMeshAgent (Dog), BiscuitAgent, BiscuitDebugView.");
        }

        [MenuItem("Hollowmere/Biscuit/Create Test Dead Prefab", priority = 211)]
        static void CreateTestDead()
        {
            if (!AssetDatabase.IsValidFolder(DebugFolder)) AssetDatabase.CreateFolder(AgentsFolder, "Debug");

            // A capsule's pivot is its centre, so lift the agent by half its height to stand on the floor.
            GameObject dead = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dead.name = "PF_TestDead";
            try
            {
                var agent = dead.AddComponent<NavMeshAgent>();
                agent.agentTypeID = 0;   // Humanoid
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.baseOffset = 1f;
                agent.speed = 1.4f;

                dead.AddComponent<ScentSource>();
                dead.AddComponent<TestDead>();

                PrefabUtility.SaveAsPrefabAsset(dead, TestDeadPrefab);
            }
            finally
            {
                Object.DestroyImmediate(dead);
            }

            Debug.Log("Created " + TestDeadPrefab + ". If Unity asks to save the open scene now, choose Don't Save.");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T component) ? component : go.AddComponent<T>();

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
