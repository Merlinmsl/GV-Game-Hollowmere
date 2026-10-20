# Core Developer models

Two props made from scratch in Blender for Hollowmere. Sources are in `SourceArt/<Model>/<Model>.blend`; the
`Screenshots` folder beside each shows the four stages: blockout, wireframe, UV layout, textured.

| Model | Triangles | Budget | Textures | Prefab |
|---|---|---|---|---|
| `SM_IceGun` | 2,460 | under 8,000 | 2048 px: BaseMap, Normal, MaskMap | `Prefabs/Interactables/PF_IceGun` |
| `SM_SpecimenJar` | 1,008 | under 3,000 | 1024 px: BaseMap, Normal, MaskMap | `Prefabs/Environment/PF_SpecimenJar` (static) |

**Ice gun.** Home-built from laboratory parts: a steel receiver, a taped wooden grip, a gas canister held on two
saddles with tape, a brass pressure gauge tilted towards the player, a valve with a red knob, a copper pipe into a
finned barrel, and a frosted nozzle. 47 cm long; the origin is on the grip and the barrel points along +Z.

**Specimen jar.** A murky glass jar with an aged paper label and a screw lid. 23 cm tall. The top of the glass is
left open because the lid hides it.

## How they were made
1. **Blockout:** primitives at real size (metric, unit scale 1).
2. **Model:** surfaces of revolution for round parts, bevelled boxes for hard parts. Loops only where the outline
   bends; faces that can never be seen are left out. Edges sharper than 35 degrees are marked sharp, the rest are smooth.
3. **UV:** Smart UV Project at 66 degrees (seams fall on hard edges), islands averaged for even texel density,
   packed with a 1% margin. Seams are marked in the file.
4. **Texture:** a procedural material (grime from ambient occlusion, worn edges from a bevel mask, rust in
   cavities, paint chips on painted parts) baked to the three maps. Mask map: metallic in red, ambient occlusion in
   green, smoothness in alpha.
5. **Export:** FBX, selected mesh only, FBX All scale, -Z forward, Y up, apply transform, face smoothing.

In Unity, **Hollowmere > Props > Set Up Core Models** applies the import settings, builds `M_IceGun` and
`M_SpecimenJar` (URP Lit) and the two prefabs.

## Optimisation
| Technique | What was done | Why |
|---|---|---|
| Polygon control | Detail only where the shape changes; hidden faces removed | Fewer triangles to draw |
| Triangle budgets | 2,460 and 1,008, well under 8,000 and 3,000 | Predictable cost on a 4 GB GPU |
| One material per model | Every part shares one texture set | One draw call each |
| Texture size | 2048 for the gun seen close up, 1024 for the jar | Video memory is limited |
| Packed mask map | Metallic, AO and smoothness in one image | One texture, not three |
| Texture compression | Compressed, Normal Quality | About a sixth of the memory |
| Mesh compression, Read/Write off | Set on import | Smaller build, less memory |
| Lightmap UVs | Only on the jar, which never moves | The gun is never lightmapped |
| Primitive colliders | One box collider each | Cheaper than a mesh collider |
