# Role Guide: Core Developer

**Graphics duty: Custom 3D Modeling.** You design, model and import at least two custom 3D
models made from scratch in Blender or Maya.

Read the [role index](README.md) first for the Git routine, naming and limits.

---

## 1. What your role is

Almost everything in the world so far comes from bought asset packs. Your models are the
group's own work, and they must look as if they belong in the same game.

The assignment marks you on: **"Custom 3D models, topology, and UV mapping."**

- **Topology** is how the surface is built from polygons: clean, even, no wasted detail.
- **UV mapping** is how the 3D surface is unfolded flat so a texture can be painted on it.

"From scratch" means you start from a cube or a plane and build the shape yourself. A model
made by a character generator, or downloaded and edited, does not count.

---

## 2. What you must deliver

At least two models. Three is safer, in case one is judged too simple.

| Priority | Model | Where it is used | Triangle budget |
|---|---|---|---|
| 1 | **The ice gun** | Held in the player's hand; the only weapon that frees ghosts | under 8,000 |
| 2 | **Laboratory equipment set**: operating table, specimen jars, a pump with tubes | Zone 3 laboratory, which is still nearly empty | under 3,000 each |
| 3 | **The study hatch door** | Covers the stairs down from Marrow's study | under 1,500 |
| Extra | Evidence props: Mara's medical file, the diary, the sluice order | On tables in Zones 2 and 3 | under 500 each |

The ice gun is the most valuable: the player sees it close up for half the game, so good
topology and UVs are obvious, and it has a clear design story.

Agree the final list with the team before you start.

---

## 3. The look to match

Autumn 1994, a neglected estate, a scientist who stopped caring.

- Worn metal, chipped paint, rust, grime, water stains.
- Dull colours: grey-green, brown, off-white. No bright or clean surfaces.
- 1990s or older technology. The ice gun should look home-built from laboratory parts: a gas
  canister, copper pipe, a pressure gauge, tape.

Open the game and walk through the mansion and the laboratory before modelling, and collect
five or six reference photos of real objects.

---

## 4. Step 1: set up Blender

1. Install Blender (free, from blender.org).
2. In a new file, open **Scene Properties > Units**: Unit System **Metric**, Unit Scale **1**.
   One Blender unit is then one metre, the same as Unity.
3. Save the file **outside** the Unity `Assets` folder, in
   `SourceArt/<ModelName>/<ModelName>.blend` at the top of the repository.

Never save a `.blend` file inside `Assets`. Unity tries to import it using Blender, and it
then breaks on any machine without the same Blender installed.

---

## 5. Step 2: model

1. Start from a primitive (cube, cylinder). Block out the big shapes at real size first: an
   ice gun is about 45 cm long.
2. Add detail with loop cuts, extrude, inset and bevel.
3. Keep the topology clean:
   - Use four-sided faces (quads) while you work.
   - Put polygons only where the shape changes. A flat panel needs one face, not twenty.
   - Bevel the edges the player will see closely; leave hidden edges sharp.
   - Delete faces that can never be seen (the underside of a table foot, the inside of a tube).
4. Check the count: turn on **Overlays > Statistics** and read **Triangles**. Stay under the
   budget in section 2.
5. Check the surface:
   - **Overlays > Face Orientation**: everything must be blue. Red faces are inside out; fix
     with **Mesh > Normals > Recalculate Outside**.
   - Select all and **Mesh > Clean Up > Merge by Distance** to remove doubled points.
6. Set the shading: right-click > **Shade Auto Smooth**, so curved parts look smooth and hard
   edges stay crisp.

Take a screenshot of the model in wireframe now. You will show it in the viva.

---

## 6. Step 3: UV unwrap

1. Switch to the **UV Editing** workspace.
2. In Edit mode, select the edges where the surface should be cut open, and
   **Edge > Mark Seam**. Put seams where they are hidden: underneath, at the back, along
   natural joins.
3. Select all and press **U > Unwrap**.
4. In the UV editor, check:
   - **Nothing overlaps** (unless two parts are meant to share the same texture area).
   - **Even texel density:** apply a checker texture; the squares must be about the same
     size everywhere on the model. Scale UV islands until they are.
   - **Little wasted space:** pack the islands with **UV > Pack Islands**, margin 0.01.
5. Give the parts the player sees most a little more UV space (the top and sides of the gun,
   not the underside).

Take a screenshot of the UV layout. You will show it in the viva.

---

## 7. Step 4: texture

You need three images per model, all the same size (2048 px for the ice gun, 1024 px for
props).

| Map | What it holds | File name |
|---|---|---|
| Base map | The colour | `T_IceGun_BaseMap.png` |
| Normal map | Small surface bumps and scratches | `T_IceGun_Normal.png` |
| Mask map | Metallic in red, ambient occlusion in green, smoothness in alpha | `T_IceGun_MaskMap.png` |

You can paint in Blender's **Texture Paint** workspace, or bake detail from a high-detail
copy of the model onto the low-detail one (**Render Properties > Bake**, with Cycles). Add
dirt, edge wear and rust so the model matches the world.

Use one material per model where you can. Every extra material is an extra draw call.

---

## 8. Step 5: export from Blender

1. Select the model. Set its **origin** where it should pivot: the bottom centre for a table,
   the grip for the gun, the hinge edge for the hatch door.
   (**Object > Set Origin > Origin to 3D Cursor**.)
2. Apply the transforms: **Ctrl+A > All Transforms**. Scale must read 1, 1, 1.
3. **File > Export > FBX** with these settings:
   - **Limit to: Selected Objects** ticked
   - **Object Types:** Mesh only
   - **Apply Scalings: FBX All**
   - **Forward: -Z Forward**, **Up: Y Up**
   - **Apply Transform** ticked
   - **Geometry > Smoothing: Face**
4. Name the file `SM_IceGun.fbx`.

---

## 9. Step 6: import into Unity

1. In Unity's Project window, create `Assets/_Hollowmere/Art/Models/IceGun/`.
2. Drag in `SM_IceGun.fbx` and the three textures.
3. Click the FBX. In the Inspector's **Model** tab:
   - **Scale Factor** 1; check the model is the right size next to the player.
   - **Mesh Compression: Medium**
   - **Read/Write** unticked
   - **Generate Lightmap UVs** ticked for furniture that never moves; unticked for the gun
4. Click each texture:
   - Normal map: **Texture Type: Normal map**
   - Mask map: **sRGB** unticked
   - **Max Size** 2048 or 1024, **Compression: Normal Quality**
5. Create a material: right-click > **Create > Material**, name it `M_IceGun`, shader
   **Universal Render Pipeline/Lit**. Assign the three maps.
6. Drag the model into your sandbox scene, assign the material, and add a collider:
   a **Box Collider** or two, never a mesh collider for a small prop.
7. Drag it from the Hierarchy into `Assets/_Hollowmere/Prefabs/Environment/` (or
   `Prefabs/Interactables/` for the gun and the hatch) to make `PF_IceGun`.

For any model seen from far away, add a simpler second version and an **LOD Group**
component, so distant copies draw fewer triangles.

---

## 10. Step 7: check it in the game

1. Open the `World` scene (**Hollowmere > World > Open World**) and press Play.
2. Drag your prefab into the scene near the player **without saving the scene**.
3. Look at it in the dark mansion and in the laboratory. Does it look as worn and as dark as
   what is around it? If it looks cleaner or brighter, darken the base map and lower the
   smoothness.

---

## 11. Handing over

1. Commit the `.blend` source (in `SourceArt/`), the FBX, textures, material and prefab.
2. Use `git add -A Assets/_Hollowmere/Art Assets/_Hollowmere/Prefabs SourceArt` so every
   `.meta` file is included.
3. Tell the World Builder where each prop should go, and the Systems Engineer that the hatch
   door and the gun are ready for scripting.

Commit at each stage (blockout, final model, UVs, textures, import), not once at the end.
The stages in the history are your evidence that the model was made from scratch.

---

## 12. Optimisation you must be able to explain

| Technique | What you did | Why |
|---|---|---|
| Polygon control | Detail only where the shape changes; hidden faces deleted | Fewer triangles to draw |
| Triangle budgets | Each model under its limit | Predictable cost on a 4 GB GPU |
| LOD | A simpler mesh at distance | Far objects cover few pixels |
| One material per model | All parts share one texture set | One draw call, not several |
| Texture size | 2048 for the hero gun, 1024 or less for props | Video memory is limited |
| Texture compression | Compressed in Unity's import settings | About a sixth of the memory |
| Packed mask map | Three greyscale maps in one image | One texture, not three |
| Mesh compression, Read/Write off | Set on import | Smaller build, less memory |
| Primitive colliders | Boxes, not mesh colliders | Cheaper physics |

---

## 13. Viva questions to prepare

- Show the wireframe. Why are the polygons where they are?
- How many triangles does each model have, and how did you choose that number?
- Show the UV layout. Where did you put the seams, and why there?
- What is texel density and how did you keep it even?
- What is a normal map, and what detail did you put in it instead of in the mesh?
- Why does the mask map have sRGB switched off?
- What FBX export settings did you use, and what goes wrong without Apply Transform?
- How does your model fit the look of the rest of the game?
