# Role Guide: World Builder

**Graphics duty: Level Design, Lighting and Texturing.** You construct the 3D environment,
apply cohesive textures, and bake the lighting and the NavMesh.

Read the [role index](README.md) first for the Git routine, naming and limits.

---

## 1. What your role is

You build the place the game happens in, and you own the zone scenes. Everyone else's work
ends up placed in your scenes, so you are also the one who keeps the whole game looking like
one world.

The assignment marks you on: **"Level layout, NavMesh baking, lighting, and texturing."**

---

## 2. What is already built

| Zone | Scene | Contents |
|---|---|---|
| 1. Flooded Village | `Z1_Outskirts` | Flooded cabins, boardwalks, guard house, estate gate |
| Estate Road | `C1_EstateRoad` | The road climbing through the forest |
| 2. Marrow House | `Z2_MarrowHouse` | The mansion, furnished on both floors |
| 3. The Undercroft | `Z3_Undercroft` | Stairs from the study, laboratory, tunnel, creature chamber |
| 4. The Waterworks | `Z4_Waterworks` | Yard, dam, pump hall, outflow pool, radio mast |

Also done:

- **One connected map.** The `World` scene loads each zone as the player approaches.
- **Terrain** generated from one height function, so the tiles meet exactly.
- **Baked lighting** for all zones in one shared set.
- **NavMesh** in every zone for the Humanoid and Dog agent types.
- **Far views:** the mansion silhouette and the red mast light seen from the village.
- **Gameplay markers** in every zone for the other roles.
- **A Windows build** that runs.

---

## 3. What is left to do

| # | Task | Section |
|---|---|---|
| 1 | Place teammates' prefabs on the markers as they arrive | 4 |
| 2 | Rebake the NavMesh after anything solid is added | 5 |
| 3 | Rebake the lighting after lamps or large objects change | 6 |
| 4 | Dress the mansion grounds: trees, overgrowth, the servants' graves | 7 |
| 5 | Place the laboratory and pump-room props from the Core Developer | 4 |
| 6 | Measure performance and add occlusion culling | 8 |
| 7 | Rebuild and test the Windows game before the video | 9 |

---

## 4. Placing a delivered prefab

1. Pull `dev` and open **Hollowmere > World > Open World**.
2. In the Hierarchy, open the zone scene, then `_Gameplay`, and find the marker (for example
   `Door_Front_Locked_HouseKey`).
3. Drag the prefab from the Project window onto the marker so it becomes its child, then set
   the prefab's position and rotation to zero. It now sits exactly on the marker.
4. Press Play and check it works and that it looks right in that zone's light.
5. Save, then commit only that zone's scene.

If a prefab looks too clean or too bright next to the world, send it back to its owner with a
screenshot. Visual cohesion is fifteen group marks.

---

## 5. Rebaking the NavMesh

Do this whenever something solid is added, moved or removed.

1. Open the zone scene. In the Hierarchy open `_Navigation`.
2. Select the `Humanoid` surface and press **Bake** in the Inspector. Do the same for `Dog`.
3. Turn on the NavMesh display (the **AI Navigation** overlay in the Scene view) and check:
   - blue covers every place a character should walk;
   - there are no blue islands on roofs or behind walls;
   - doorways are wide enough to be blue all the way through.
4. Save and commit the scene with the two `NavMesh-...asset` files next to it.

Reminders:

- The bake reads **colliders**, not what you see. Something with no collider does not block.
- Flooded ground is the `Water` area with cost 3: agents prefer the boardwalk.
- The Humanoid NavMeshes are deliberately **not** joined between zones, so enemies stay in
  their zone. Only the Dog NavMesh has links across the joins.
- Doors and barricades block with a NavMesh Obstacle. Never rebake while the game runs.

---

## 6. Rebaking the lighting

All zones share one baked lighting set. Bake the whole world in one pass, with Unity closed.

1. Save everything and **close Unity**.
2. In a terminal in the project folder, run (one line):

```bash
"D:/Projects/Unity/Editor/Unity.exe" -batchmode -quit -projectPath . -executeMethod Hollowmere.EditorTools.WorldLighting.BakeWholeWorldFromCommandLine -spacing 3 -logFile Logs/bake-world.log
```

3. Wait about a minute. Open `Logs/bake-world.log` and search for **Probe cells per zone**.
   Every zone must have more than zero.
4. Open Unity, press Play, and check each zone with the number keys 1 to 7.
5. Commit `Assets/_Hollowmere/Settings/Lighting` and `Assets/_Hollowmere/Scenes/World`.

The lighting rules for this game:

- The mansion and the tunnel are **dark**, with a short view distance.
- Only a few dim or flickering lamps. Every lit lamp has a visible fitting.
- Everything looks neglected: dead bulbs, burnt-out torches, grime.
- The Waterworks may be a little lighter, with a few bulbs.

---

## 7. Dressing a zone

For the mansion grounds, and any later dressing.

1. Open the zone and work under `_Environment`. Make a group for what you are adding
   (for example `Grounds/Graves`).
2. Use prefabs already in `Assets/ThirdParty/`. If you need a piece that is not in the
   repository yet, add it the way `Assets/ThirdParty/README.md` describes: the pack's files
   are large, so textures are re-encoded at 2048 px before they are committed.
3. Avoid straight lines and even spacing. Rotate and scale each tree and stone a little.
4. Mark anything that never moves as **Static**.
5. Look at it from the player's height, in Play mode, before judging it.
6. Rebake the NavMesh (section 5) if you added anything solid.

Do not sculpt the terrain by hand. Its shape comes from `Scripts/Editor/WorldTerrain.cs`;
edit that and run **Hollowmere > World > Rebuild Terrain Tiles**.

---

## 8. Performance and occlusion culling

1. Build a development version: **Hollowmere > Build > Windows Game (development)**.
2. Run it and open **Window > Analysis > Profiler** in Unity; it connects to the running game.
3. In each zone (keys 1 to 7), note: frames per second, draw calls (batches), triangles,
   and video memory. Keep the numbers in a table: they are your evidence in the viva.
4. Occlusion culling stops Unity drawing what is hidden behind walls:
   - mark the mansion and tunnel walls as **Occluder Static** and furniture as
     **Occludee Static**;
   - open **Window > Rendering > Occlusion Culling** and press **Bake**;
   - measure again and record the difference.

Ask before starting any long bake or build on the shared laptop.

---

## 9. Building the game

1. Close Unity and run the build from a terminal, or use **Hollowmere > Build > Windows Game**.
2. The result is `Builds/Windows/Hollowmere.exe`. It is not committed.
3. Play it from start to finish once before recording the video.

---

## 10. Optimisation you must be able to explain

| Technique | What you did | Why |
|---|---|---|
| Zone streaming | Only the zones near the player are loaded | Memory and draw cost stay flat across the map |
| Fog as a limit | Fog hides everything beyond about 110 m | Zones can load and unload unseen |
| Texture re-encoding | Pack textures stored at 2048 px, not 4096 px | Zone 1 went from 1.15 GB to about 180 MB |
| Texture compression | Compressed on import, streaming mipmaps | Fits a 4 GB GPU |
| Baked lighting | Light probes in one shared set, mixed lights | Little lighting cost while playing |
| Few real-time lights | A handful of dim lamps per zone | Each shadowed light is costly |
| Vistas | A flat silhouette stands in for the far mansion | One quad, not a whole zone |
| NavMesh volumes | Baked only around the playable area | Smaller data, no stray islands |
| Static batching | Non-moving objects marked Static | Fewer draw calls |
| Occlusion culling | Hidden rooms are not drawn | Fewer draw calls indoors |

---

## 11. Viva questions to prepare

- Why one connected map with streamed zones, and not separate levels?
- How does the game decide when to load and unload a zone?
- Why bake lighting, and what are light probes?
- Why one shared lighting set for all zones?
- What does the NavMesh bake read, and what are areas and costs?
- Why are the enemy NavMeshes not joined between zones?
- How did you keep the pack assets looking like one world?
- What did you measure, and what did each optimisation change?
