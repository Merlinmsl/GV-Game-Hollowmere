using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Hollowmere.Interactables;
using Hollowmere.Pickups;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// World Builder pass that turns the zone markers into things the player can use.
    ///
    /// 1. Gives the five pickup prefabs a visible model (simple primitives with a faint glow, so
    ///    they can be found in the dark interiors).
    /// 2. Puts a pickup (or a throwable bottle) under every item marker, standing on the surface
    ///    below it, with an id the doors and zone exits can check.
    /// 3. Builds the doors, the study hatch and the zone gates in the openings measured in each zone,
    ///    with the keys and evidence they need.
    ///
    /// Everything it creates is named "Placed_..." and is replaced when it runs again.
    /// Open World with every zone loaded first (Hollowmere > World > Open World + All Zones).
    /// </summary>
    public static class WorldInteractables
    {
        const string PickupFolder = "Assets/_Hollowmere/Prefabs/Pickups";
        const string MatFolder = "Assets/_Hollowmere/Art/Materials/Interactables";
        const string BottlePrefab = "Assets/_Hollowmere/Prefabs/Interactables/PF_Bottle.prefab";
        const string DungeonWood = "Assets/ThirdParty/Decrepit Dungeon LITE/Materials/Wood_A.mat";
        const string Placed = "Placed_";
        const float VillageWater = 0.16f;   // the flood plane in Z1; items below it would be hidden

        // Plain names for the prompt ("Pick up ...") and the pickup message.
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "Clue_DiaryPage1_Car", "Diary page" },
            { "Clue_Clipping_GuardHouse", "Newspaper clipping" },
            { "Key_MarrowHouse", "Marrow House key" },
            { "Pickup_RevolverAmmo_Car", "Revolver rounds" },
            { "Clue_TunnelMap", "Map of the tunnels" },
            { "Evidence_MaraMedicalFile", "Mara's medical file" },
            { "Evidence_DiaryNightMaraDied", "Diary: the night Mara died" },
            { "Evidence_ServantsTrialNotes", "Notes on the servants' trial" },
            { "Pickup_Battery_First", "Flashlight battery" },
            { "Key_Tunnel", "Tunnel key" },
            { "Key_TunnelExit", "Tunnel exit key" },
            { "Evidence_AnimalTrialLog", "Animal trial log" },
            { "Evidence_SluiceOrder", "Sluice order" },
            { "Evidence_NotesOnRaisingTheDead", "Notes on raising the dead" },
        };

        [MenuItem("Hollowmere/World/Place Pickups, Doors and Gates", priority = 80)]
        public static void PlaceAll()
        {
            var zones = new Dictionary<string, Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name.Length > 2 && s.name[0] == 'Z' && char.IsDigit(s.name[1])) zones[s.name.Substring(0, 2)] = s;
            }
            foreach (var z in new[] { "Z1", "Z2", "Z3", "Z4" })
                if (!zones.ContainsKey(z)) { Debug.LogError($"[WorldInteractables] Zone {z} is not open. Open World with all zones first."); return; }

            var mats = new Materials();
            UpgradePickupPrefabs(mats);
            BuildPistolPickup();

            int items = 0;
            foreach (var pair in zones) items += PlaceItems(pair.Key, pair.Value);

            BuildDoorsAndGates(zones, mats);

            var station = BuildHealthStationPrefab(mats);
            int stations = 0;
            foreach (var pair in zones) stations += PlaceHealthStations(pair.Value, station);

            foreach (var pair in zones)
            {
                EditorSceneManager.MarkSceneDirty(pair.Value);
                EditorSceneManager.SaveScene(pair.Value);
            }
            AssetDatabase.SaveAssets();   // write the edited materials to disk, not only to memory
            Debug.Log($"[WorldInteractables] Placed {items} items, {stations} first-aid boxes, 4 doors, the study hatch and 4 zone gates.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Materials
        // ─────────────────────────────────────────────────────────────────────

        sealed class Materials
        {
            public readonly Material Brass, Card, Ice, Steel, Battery, Copper, Paper, Wood, Iron, Enamel, Cross;

            public Materials()
            {
                if (!AssetDatabase.IsValidFolder(MatFolder))
                    AssetDatabase.CreateFolder("Assets/_Hollowmere/Art/Materials", "Interactables");

                Brass = Lit("M_Pickup_Brass", new Color(0.55f, 0.42f, 0.16f), 0.8f, 0.55f, new Color(0.22f, 0.15f, 0.03f));
                Card = Lit("M_Pickup_AmmoBox", new Color(0.28f, 0.27f, 0.17f), 0f, 0.2f, new Color(0.07f, 0.06f, 0.02f));
                Ice = Lit("M_Pickup_IceGlass", new Color(0.45f, 0.78f, 0.95f), 0.1f, 0.85f, new Color(0.08f, 0.38f, 0.55f));
                Steel = Lit("M_Pickup_Steel", new Color(0.22f, 0.23f, 0.25f), 0.9f, 0.45f, Color.black);
                Battery = Lit("M_Pickup_Battery", new Color(0.12f, 0.3f, 0.14f), 0.2f, 0.4f, new Color(0.02f, 0.12f, 0.04f));
                Copper = Lit("M_Pickup_Copper", new Color(0.62f, 0.36f, 0.2f), 0.9f, 0.6f, Color.black);
                Paper = Lit("M_Pickup_Paper", new Color(0.82f, 0.78f, 0.66f), 0f, 0.1f, new Color(0.12f, 0.115f, 0.1f));
                Iron = Lit("M_Gate_Iron", new Color(0.1f, 0.09f, 0.085f), 0.85f, 0.3f, Color.black);
                Enamel = Lit("M_Health_Enamel", new Color(0.72f, 0.71f, 0.66f), 0.3f, 0.45f, Color.black);
                Cross = Lit("M_Health_Cross", new Color(0.55f, 0.03f, 0.03f), 0f, 0.4f, new Color(0.45f, 0.02f, 0.02f));

                // Door wood: the dungeon kit's wood (already in the repo), tiled for a tall leaf.
                string woodPath = MatFolder + "/M_Door_Wood.mat";
                Wood = AssetDatabase.LoadAssetAtPath<Material>(woodPath);
                if (Wood == null)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Material>(DungeonWood);
                    Wood = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (source == null) Wood.SetColor("_BaseColor", new Color(0.25f, 0.17f, 0.1f));
                    Wood.mainTextureScale = new Vector2(1f, 2.5f);
                    AssetDatabase.CreateAsset(Wood, woodPath);
                }
            }

            static Material Lit(string name, Color baseColor, float metallic, float smoothness, Color emission)
            {
                string path = MatFolder + "/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = m == null;
                if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", baseColor);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smoothness);
                if (emission.maxColorComponent > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // a glow, not a light source
                }
                if (create) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                return m;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. Pickup prefabs get a model
        // ─────────────────────────────────────────────────────────────────────

        static void UpgradePickupPrefabs(Materials m)
        {
            Upgrade("PF_Pickup_Key", true, 0.22f, v =>
            {
                Part(v, PrimitiveType.Cube, new Vector3(0f, 0f, 0.03f), new Vector3(0.025f, 0.015f, 0.16f), m.Brass);   // shaft
                Part(v, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.08f), new Vector3(0.08f, 0.008f, 0.08f), m.Brass, new Vector3(0f, 0f, 0f)); // bow
                Part(v, PrimitiveType.Cube, new Vector3(0.022f, 0f, 0.09f), new Vector3(0.035f, 0.015f, 0.025f), m.Brass); // bit
                Part(v, PrimitiveType.Cube, new Vector3(0.022f, 0f, 0.055f), new Vector3(0.025f, 0.015f, 0.018f), m.Brass);
            });
            Upgrade("PF_Pickup_Ammo", true, 0.25f, v =>
            {
                Part(v, PrimitiveType.Cube, Vector3.zero, new Vector3(0.16f, 0.09f, 0.11f), m.Card);
                for (int i = 0; i < 4; i++)   // rounds standing in the open box
                    Part(v, PrimitiveType.Cylinder, new Vector3(-0.05f + i * 0.033f, 0.06f, 0f), new Vector3(0.018f, 0.025f, 0.018f), m.Brass);
            });
            Upgrade("PF_Pickup_IceCanister", true, 0.28f, v =>
            {
                Part(v, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.1f, 0.13f, 0.1f), m.Ice);
                Part(v, PrimitiveType.Cylinder, new Vector3(0f, 0.135f, 0f), new Vector3(0.11f, 0.015f, 0.11f), m.Steel);
                Part(v, PrimitiveType.Cylinder, new Vector3(0f, -0.135f, 0f), new Vector3(0.11f, 0.015f, 0.11f), m.Steel);
                Part(v, PrimitiveType.Cylinder, new Vector3(0f, 0.165f, 0f), new Vector3(0.035f, 0.02f, 0.035f), m.Steel);
            });
            Upgrade("PF_Pickup_Battery", true, 0.22f, v =>
            {
                // A big lantern cell rather than a torch battery, so it can be seen on the floor
                Part(v, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.07f, 0.08f, 0.07f), m.Battery);
                Part(v, PrimitiveType.Cylinder, new Vector3(0f, 0.085f, 0f), new Vector3(0.03f, 0.01f, 0.03f), m.Copper);
                Part(v, PrimitiveType.Cube, Vector3.zero, new Vector3(0.072f, 0.05f, 0.072f), m.Paper).name = "Label";
            });
            Upgrade("PF_Pickup_EvidencePage", false, 0.3f, v =>
            {
                Part(v, PrimitiveType.Cube, Vector3.zero, new Vector3(0.21f, 0.004f, 0.297f), m.Paper);
                Part(v, PrimitiveType.Cube, new Vector3(0.02f, 0.004f, -0.01f), new Vector3(0.21f, 0.004f, 0.297f), m.Paper, new Vector3(0f, 12f, 0f));
            });
        }

        static void Upgrade(string prefabName, bool bob, float radius, System.Action<Transform> build)
        {
            string path = PickupFolder + "/" + prefabName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var old = root.transform.Find("Visual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            build(visual);

            var sphere = root.GetComponent<SphereCollider>();
            if (sphere != null) { sphere.radius = radius; sphere.isTrigger = true; }

            var so = new SerializedObject(root.GetComponent<Pickup>());
            so.FindProperty("bobAndSpin").boolValue = bob;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // small props, not worth a shadow
            return go.transform;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 2. Items on the markers
        // ─────────────────────────────────────────────────────────────────────

        static int PlaceItems(string zone, Scene scene)
        {
            int count = 0;
            // Collect the marker groups first: clearing old placements below destroys transforms
            // that a single pass over GetComponentsInChildren would still visit.
            var groups = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "ItemMarkers") groups.Add(t);
            foreach (var markers in groups)
                for (int i = 0; i < markers.childCount; i++)
                {
                    var marker = markers.GetChild(i);
                    ClearPlaced(marker);
                    if (PlaceItem(zone, marker, scene)) count++;
                }
            return count;
        }

        static bool PlaceItem(string zone, Transform marker, Scene scene)
        {
            string n = marker.name;
            string prefab; PickupType type = PickupType.Ammo; float amount = 1f; float lift;
            if (n.StartsWith("Key_")) { prefab = "PF_Pickup_Key"; type = PickupType.Key; lift = 0.3f; }
            else if (n.StartsWith("Evidence_") || n.StartsWith("Clue_")) { prefab = "PF_Pickup_EvidencePage"; type = PickupType.EvidencePage; lift = 0.01f; }
            else if (n.StartsWith("Pickup_Magazine")) { prefab = "PF_Pickup_Ammo"; amount = 8f; lift = 0.3f; }
            else if (n.StartsWith("Pickup_RevolverAmmo")) { prefab = "PF_Pickup_Ammo"; amount = 12f; lift = 0.3f; }
            else if (n.StartsWith("Pickup_IceCanister")) { prefab = "PF_Pickup_IceCanister"; type = PickupType.IceCanister; lift = 0.35f; }
            else if (n.StartsWith("Pickup_Battery")) { prefab = "PF_Pickup_Battery"; type = PickupType.Battery; lift = 0.3f; }
            else if (n.StartsWith("Pickup_Revolver_")) { prefab = "PF_Pickup_Pistol"; type = PickupType.Weapon; amount = 6f; lift = 0.3f; }
            else if (n.StartsWith("Pickup_Bottle")) { PlaceBottle(zone, marker, scene); return true; }
            else return false;   // health stations are placed separately

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PickupFolder + "/" + prefab + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            go.name = Placed + prefab.Substring(3);
            go.transform.SetParent(marker, false);
            go.transform.position = Surface(zone, marker.position) + Vector3.up * lift;
            go.transform.rotation = Quaternion.Euler(0f, Hash(n) % 360, 0f);

            var so = new SerializedObject(go.GetComponent<Pickup>());
            so.FindProperty("pickupType").enumValueIndex = (int)type;
            // Keys keep the marker's name, which the doors ask for. Evidence gets the zone in front
            // ("Z2_Evidence_...") so each zone exit counts only its own pages.
            so.FindProperty("itemId").stringValue = type == PickupType.EvidencePage ? zone + "_" + n : n;
            so.FindProperty("displayName").stringValue = Names.TryGetValue(n, out var nice) ? nice
                : n.StartsWith("Pickup_Magazine") ? "Pistol magazine"
                : type == PickupType.Weapon ? "Pistol" : Hollowmere.World.MapMarker.MakeLabel(n);
            so.FindProperty("amount").floatValue = amount;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static void PlaceBottle(string zone, Transform marker, Scene scene)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePrefab);
            if (asset == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            go.name = Placed + "Bottle";
            go.transform.SetParent(marker, false);
            // PF_Bottle is a 0.9 m capsule; a bottle is about 28 cm tall and 8 cm wide.
            go.transform.localScale = new Vector3(0.08f, 0.14f, 0.08f);
            // The bottle's pivot is at its middle: stand it on the surface by its real height.
            go.transform.position = Surface(zone, marker.position);
            float bottom = go.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
            go.transform.position += Vector3.up * (go.transform.position.y - bottom + 0.01f);
        }

        // The pistol lying in the guard house, using the team's own pistol model.
        const string PistolModel = "Assets/_Hollowmere/Art/Models/Pistol/SM_Pistol.fbx";
        const string PistolMaterial = "Assets/_Hollowmere/Art/Materials/Props/M_Pistol.mat";

        static void BuildPistolPickup()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PistolModel);
            if (model == null) { Debug.LogWarning("[WorldInteractables] SM_Pistol.fbx not found; no pistol pickup."); return; }

            var root = new GameObject("PF_Pickup_Pistol");
            var visual = (GameObject)Object.Instantiate(model);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PistolMaterial);
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                if (mat != null) r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Fit it to a real pistol's length (about 22 cm) and centre it on the pickup.
            var b = RendererBounds(visual);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest > 0f) visual.transform.localScale *= 0.22f / longest;
            b = RendererBounds(visual);
            visual.transform.localPosition -= b.center;

            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.25f;
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            root.AddComponent<Pickup>().Configure(PickupType.Weapon, "Weapon_Pistol", "Pistol", 6f);

            PrefabUtility.SaveAsPrefabAsset(root, PickupFolder + "/PF_Pickup_Pistol.prefab");
            Object.DestroyImmediate(root);
        }

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // The highest surface within reach of the marker (floor, table, water). Both the colliders and
        // the visible meshes are checked: a marker on the ground under the guard house's raised floor
        // must end up on that floor, and the flood water and floor decals have no collider at all.
        static Vector3 Surface(string zone, Vector3 p)
        {
            var start = p + Vector3.up * 1.2f;   // above a floor or table, below any ceiling
            var hits = Physics.RaycastAll(start, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
                if (!IsPlaced(h.collider.transform) && h.point.y > best) best = h.point.y;
            best = Mathf.Max(best, VisibleSurface(start, 3f));
            if (float.IsNegativeInfinity(best)) best = p.y;
            if (zone == "Z1") best = Mathf.Max(best, VillageWater);
            return new Vector3(p.x, best, p.z);
        }

        static bool IsPlaced(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith(Placed)) return true;
            return false;
        }

        // Highest point of the drawn meshes (full-detail only) straight below 'start'.
        static float VisibleSurface(Vector3 start, float length)
        {
            float best = float.NegativeInfinity;
            var column = new Bounds(start + Vector3.down * length * 0.5f, new Vector3(0.1f, length, 0.1f));
            var ray = new Ray(start, Vector3.down);
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var r = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || r == null || !r.enabled || !r.bounds.Intersects(column)) continue;
                if (IsPlaced(filter.transform) || filter.GetComponentInParent<HealthStation>() != null) continue;
                if (filter.name.Contains("_LOD") && !filter.name.Contains("_LOD0")) continue;
                var temp = new GameObject("TMP_VisibleMesh") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    temp.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    temp.transform.localScale = filter.transform.lossyScale;
                    var mc = temp.AddComponent<MeshCollider>();
                    mc.sharedMesh = filter.sharedMesh;
                    if (mc.Raycast(ray, out var hit, length) && hit.point.y > best) best = hit.point.y;
                }
                finally { Object.DestroyImmediate(temp); }
            }
            return best;
        }

        static int Hash(string s) { int h = 17; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; }

        static void ClearPlaced(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                if (parent.GetChild(i).name.StartsWith(Placed)) Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 3. Doors, hatch and zone gates (openings measured in each zone)
        // ─────────────────────────────────────────────────────────────────────

        static void BuildDoorsAndGates(Dictionary<string, Scene> zones, Materials m)
        {
            // Zone 2, front door of Marrow House: a double opening x -1.85..1.9 in the wall at z 237,
            // floor 26.01, 4.9 m high (the pack house is oversized). Both leaves need the house key
            // and swing into the hall.
            var front = Find(zones["Z2"], "Door_Front_Locked_HouseKey");
            ClearPlaced(front);
            Door(front, "Placed_DoorLeft", new Vector3(-1.85f, 26.01f, 237f), Vector3.right, 1.75f, 4.85f, Vector3.forward, m.Wood, true, "Key_MarrowHouse");
            Door(front, "Placed_DoorRight", new Vector3(1.9f, 26.01f, 237f), Vector3.left, 1.8f, 4.85f, Vector3.forward, m.Wood, true, "Key_MarrowHouse");

            // Zone 3, laboratory to tunnel stairs: opening z 238.4..240.6 in the wall at x -38,
            // floor 14.4, 4.2 m high. Not locked; swings away from the lab. Its NavMesh obstacle keeps
            // the creature in the tunnel while the door is shut.
            var lab = Find(zones["Z3"], "Door_LabToTunnel");
            ClearPlaced(lab);
            Door(lab, "Placed_Door", new Vector3(-38f, 14.4f, 238.35f), Vector3.forward, 2.35f, 4.15f, Vector3.left, m.Wood, false, "");

            // Zone 1 exit, the estate arch: opening x -2.7..2.8 at z 62.5. Two iron leaves; the left
            // one opens once both Zone 1 clues are found (Zone 1 only has two), the right one stays shut.
            var estate = Find(zones["Z1"], "ZoneExit_EstateGate");
            ClearPlaced(estate);
            var estateLeaf = IronLeaf(estate, "Placed_GateLeft", new Vector3(-2.7f, 0.05f, 62.5f), Vector3.right, 2.72f, 3.2f, m.Iron);
            IronLeaf(estate, "Placed_GateRight", new Vector3(2.8f, 0.05f, 62.5f), Vector3.left, 2.72f, 3.2f, m.Iron);
            Exit(estate.gameObject, estateLeaf, Vector3.forward, 2, "Z1_", "");

            // Zone 2 exit, the study hatch over the Undercroft stairs: floor hole x 12.25..19.25,
            // z 238.25..240.75, floor 26.01. Needs the tunnel key and three Zone 2 pages; the lid
            // lifts up on its north edge.
            var hatchExit = Find(zones["Z2"], "ZoneExit_Undercroft");
            ClearPlaced(hatchExit);
            var lid = Hatch(hatchExit, new Vector3(15.75f, 26.03f, 240.95f), 7.3f, 2.9f, m.Wood);
            Exit(hatchExit.gameObject, lid, Vector3.zero, 3, "Z2_", "Key_Tunnel", new Vector3(100f, 0f, 0f));

            // Zone 3 exit, the tunnel door to the Waterworks: opening z 244.2..246.4 in the wall at
            // x -101.8, floor 8.6. Needs the tunnel exit key and the three Zone 3 pages.
            var tunnelExit = Find(zones["Z3"], "ZoneExit_Waterworks");
            ClearPlaced(tunnelExit);
            Trigger(tunnelExit.gameObject, new Vector3(1f, 3f, 2.2f));
            var tunnelLeaf = SolidLeaf(tunnelExit, "Placed_Door", new Vector3(-101.8f, 8.6f, 244.15f), Vector3.forward, 2.35f, 4.15f, m.Wood);
            Exit(tunnelExit.gameObject, tunnelLeaf, Vector3.left, 3, "Z3_", "Key_TunnelExit");

            // Zone 4, the pump hall door before the final confrontation: opening z 244.35..246.25 in
            // the wall at x -126.1, floor 8.72, 3.1 m high. Needs eight pages from the whole game
            // (2 + 3 + 3; the tunnel map counts too, so one may be missed).
            var hall = Find(zones["Z4"], "Gate_AllEvidenceRequired");
            ClearPlaced(hall);
            Trigger(hall.gameObject, new Vector3(1f, 3f, 2f));
            var hallLeaf = SolidLeaf(hall, "Placed_Door", new Vector3(-126.15f, 8.72f, 244.3f), Vector3.forward, 2.0f, 3.05f, m.Iron);
            Exit(hall.gameObject, hallLeaf, Vector3.left, 8, "", "");
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            throw new System.Exception($"[WorldInteractables] Marker {name} not found in {scene.name}.");
        }

        // Leaves are a few centimetres wider than the measured gap so no light shows at the edges.
        // A hinge object at the edge of the opening whose local +X runs along the leaf; it carries
        // the collider, so the whole leaf turns with it.
        static Transform Hinge(Transform parent, string name, Vector3 hinge, Vector3 along, float width, float height, float thickness)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.position = hinge;
            pivot.rotation = Quaternion.FromToRotation(Vector3.right, along);
            var box = pivot.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(width * 0.5f, height * 0.5f, 0f);
            box.size = new Vector3(width, height, thickness);
            return pivot;
        }

        // Degrees about the hinge (up) that swing the leaf toward 'openInto'.
        static float SwingAngle(Transform pivot, Vector3 openInto) => Vector3.Dot(pivot.forward, openInto) > 0f ? -90f : 90f;

        static Transform SolidLeaf(Transform parent, string name, Vector3 hinge, Vector3 along, float width, float height, Material mat)
        {
            var pivot = Hinge(parent, name, hinge, along, width, height, 0.1f);
            Part(pivot, PrimitiveType.Cube, new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(width, height, 0.08f), mat).name = "Leaf";
            // Iron straps and a handle on the free edge
            var strap = mat.name.Contains("Iron") ? mat : AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/M_Gate_Iron.mat");
            foreach (float y in new[] { 0.18f, 0.5f, 0.82f })
                Part(pivot, PrimitiveType.Cube, new Vector3(width * 0.5f, height * y, 0f), new Vector3(width * 0.96f, 0.08f, 0.1f), strap).name = "Strap";
            Part(pivot, PrimitiveType.Cube, new Vector3(width - 0.15f, 1.05f, 0f), new Vector3(0.05f, 0.18f, 0.16f), strap).name = "Handle";
            foreach (var r in pivot.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return pivot;
        }

        static Transform IronLeaf(Transform parent, string name, Vector3 hinge, Vector3 along, float width, float height, Material iron)
        {
            var pivot = Hinge(parent, name, hinge, along, width, height, 0.15f);
            foreach (float y in new[] { 0.15f, height * 0.5f, height - 0.1f })
                Part(pivot, PrimitiveType.Cube, new Vector3(width * 0.5f, y, 0f), new Vector3(width, 0.07f, 0.06f), iron).name = "Rail";
            int bars = Mathf.RoundToInt(width / 0.14f);
            for (int i = 0; i <= bars; i++)
                Part(pivot, PrimitiveType.Cube, new Vector3(i * width / bars, height * 0.5f + 0.05f, 0f), new Vector3(0.03f, height + 0.1f, 0.03f), iron).name = "Bar";
            foreach (var r in pivot.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return pivot;
        }

        static void Door(Transform parent, string name, Vector3 hinge, Vector3 along, float width, float height, Vector3 openInto, Material mat, bool locked, string key)
        {
            var pivot = SolidLeaf(parent, name, hinge, along, width, height, mat);
            var obstacle = pivot.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(width * 0.5f, height * 0.5f, 0f);
            obstacle.size = new Vector3(width, height, 0.2f);
            obstacle.carving = true;
            var audio = pivot.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            var door = pivot.gameObject.AddComponent<Door>();
            var so = new SerializedObject(door);
            so.FindProperty("openAngle").floatValue = SwingAngle(pivot, openInto);
            so.FindProperty("rotationAxis").vector3Value = Vector3.up;
            so.FindProperty("isLocked").boolValue = locked;
            so.FindProperty("requiredKey").stringValue = key;
            so.FindProperty("navObstacle").objectReferenceValue = obstacle;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Hatch(Transform parent, Vector3 hinge, float width, float depth, Material wood)
        {
            // Hinge along the north edge (+Z); the lid lies to the south of it.
            var pivot = new GameObject("Placed_HatchLid").transform;
            pivot.SetParent(parent, false);
            pivot.position = hinge;
            pivot.rotation = Quaternion.identity;
            var box = pivot.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.06f, -depth * 0.5f);
            box.size = new Vector3(width, 0.12f, depth);
            Part(pivot, PrimitiveType.Cube, box.center, box.size, wood).name = "Lid";
            var iron = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/M_Gate_Iron.mat");
            foreach (float x in new[] { -0.4f, -0.15f, 0.15f, 0.4f })
                Part(pivot, PrimitiveType.Cube, new Vector3(width * x, 0.005f, -depth * 0.5f), new Vector3(0.08f, 0.02f, depth * 0.97f), iron).name = "Strap";
            Part(pivot, PrimitiveType.Cube, new Vector3(0f, 0.03f, -depth + 0.2f), new Vector3(0.25f, 0.05f, 0.06f), iron).name = "Ring";
            return pivot;
        }

        static void Trigger(GameObject marker, Vector3 size)
        {
            var box = marker.GetComponent<BoxCollider>();
            if (box == null) box = marker.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
        }

        static void Exit(GameObject marker, Transform leaf, Vector3 openInto, int pages, string zonePrefix, string key, Vector3? openEuler = null)
        {
            var exit = marker.GetComponent<ZoneExit>();
            if (exit == null) exit = marker.AddComponent<ZoneExit>();
            var audio = marker.GetComponent<AudioSource>();
            if (audio == null) { audio = marker.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 1f; }

            var so = new SerializedObject(exit);
            so.FindProperty("requiredEvidenceCount").intValue = pages;
            so.FindProperty("zonePrefix").stringValue = zonePrefix;
            so.FindProperty("requiredKey").stringValue = key;
            so.FindProperty("gateLeaf").objectReferenceValue = leaf;
            so.FindProperty("barrierCollider").objectReferenceValue = leaf.GetComponent<Collider>();
            so.FindProperty("openRotationEuler").vector3Value = openEuler ?? new Vector3(0f, SwingAngle(leaf, openInto), 0f);
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 4. First-aid boxes on the Station_Health_ markers
        // ─────────────────────────────────────────────────────────────────────

        const string HealthPrefab = "Assets/_Hollowmere/Prefabs/Interactables/PF_HealthStation.prefab";

        // None of the packs has a first-aid kit, so this one is built here: a wall-mounted enamel box
        // with a red cross that glows faintly while doses remain. Its back is at z = 0, facing +Z.
        static GameObject BuildHealthStationPrefab(Materials m)
        {
            var root = new GameObject("PF_HealthStation");
            var case_ = Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.07f), new Vector3(0.42f, 0.34f, 0.14f), m.Enamel);
            case_.name = "Case";
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.071f), new Vector3(0.425f, 0.008f, 0.142f), m.Iron).name = "LidSeam";
            var crossH = Part(root.transform, PrimitiveType.Cube, new Vector3(0f, -0.03f, 0.143f), new Vector3(0.16f, 0.05f, 0.01f), m.Cross);
            var crossV = Part(root.transform, PrimitiveType.Cube, new Vector3(0f, -0.03f, 0.143f), new Vector3(0.05f, 0.16f, 0.01f), m.Cross);
            crossH.name = crossV.name = "Cross";
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.19f, 0.07f), new Vector3(0.13f, 0.02f, 0.025f), m.Steel).name = "Handle";
            foreach (float x in new[] { -0.055f, 0.055f })
                Part(root.transform, PrimitiveType.Cube, new Vector3(x, 0.178f, 0.07f), new Vector3(0.015f, 0.03f, 0.02f), m.Steel).name = "HandlePost";
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.19f, 0.06f, 0.145f), new Vector3(0.03f, 0.05f, 0.012f), m.Steel).name = "Latch";
            case_.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, 0.07f);
            box.size = new Vector3(0.42f, 0.34f, 0.14f);
            var station = root.AddComponent<HealthStation>();
            var so = new SerializedObject(station);
            var glow = so.FindProperty("glow");
            glow.arraySize = 2;
            glow.GetArrayElementAtIndex(0).objectReferenceValue = crossH.GetComponent<Renderer>();
            glow.GetArrayElementAtIndex(1).objectReferenceValue = crossV.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, HealthPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static int PlaceHealthStations(Scene scene, GameObject prefab)
        {
            var markers = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Station_Health")) markers.Add(t);

            foreach (var marker in markers)
            {
                ClearPlaced(marker);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = Placed + "HealthStation";
                go.transform.SetParent(marker, false);

                // Hang it on the nearest wall at chest height, facing the room; with no wall in
                // reach it stands on the floor instead.
                Vector3 floor = Surface(scene.name.Substring(0, 2), marker.position);
                Vector3 chest = floor + Vector3.up * 1.3f;
                RaycastHit best = default;
                float bestDistance = float.MaxValue;
                for (int a = 0; a < 16; a++)
                {
                    var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                    if (Physics.Raycast(chest, dir, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(hit.normal.y) < 0.2f && hit.distance < bestDistance
                        && VisiblySolid(hit.point, Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized))
                    {
                        best = hit;
                        bestDistance = hit.distance;
                    }
                }
                if (bestDistance < float.MaxValue)
                {
                    var normal = Vector3.ProjectOnPlane(best.normal, Vector3.up).normalized;
                    go.transform.SetPositionAndRotation(best.point + normal * 0.005f, Quaternion.LookRotation(normal, Vector3.up));
                }
                else
                {
                    go.transform.SetPositionAndRotation(floor + Vector3.up * 0.17f, Quaternion.identity);
                }
            }
            return markers.Count;
        }

        // Wall colliders in the house pack are simplified and run straight across the windows, so a
        // physics ray cannot tell a window from a wall. The visible mesh has the hole, so the box's
        // four corners and centre are tested against temporary colliders built from the meshes drawn there.
        static bool VisiblySolid(Vector3 point, Vector3 normal)
        {
            var right = Vector3.Cross(Vector3.up, normal);
            var probes = new[] { Vector3.zero, right * 0.22f + Vector3.up * 0.18f, -right * 0.22f + Vector3.up * 0.18f,
                                 right * 0.22f - Vector3.up * 0.18f, -right * 0.22f - Vector3.up * 0.18f };
            var area = new Bounds(point, Vector3.one * 0.8f);
            var temps = new List<GameObject>();
            try
            {
                foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    var r = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || r == null || !r.enabled || !r.bounds.Intersects(area)) continue;
                    if (filter.name.StartsWith(Placed) || filter.GetComponentInParent<HealthStation>() != null) continue;
                    if (filter.name.Contains("_LOD") && !filter.name.Contains("_LOD0")) continue;   // simpler LODs may fill the window
                    var temp = new GameObject("TMP_VisibleMesh") { hideFlags = HideFlags.HideAndDontSave };
                    temp.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    temp.transform.localScale = filter.transform.lossyScale;
                    temp.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    temps.Add(temp);
                }
                foreach (var offset in probes)
                {
                    var ray = new Ray(point + offset + normal * 0.3f, -normal);
                    bool solid = false;
                    foreach (var t in temps)
                        if (t.GetComponent<MeshCollider>().Raycast(ray, out _, 0.6f)) { solid = true; break; }
                    if (!solid) return false;
                }
                return true;
            }
            finally
            {
                foreach (var t in temps) Object.DestroyImmediate(t);
            }
        }
    }
}
