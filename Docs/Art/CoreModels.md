# Core Developer models

Models made from scratch in Blender for Hollowmere. Sources are in `SourceArt/<Model>/<Model>.blend`; the
`Screenshots` folder beside each shows the four stages: blockout, wireframe, UV layout, textured. The arms folder
also has the view from the player's eye, rendered in Blender (`05_view_*`) and in Unity (`06_unity_*`,
`07_unity_*_firing`).

| Model | Triangles | Budget | Textures | Used by |
|---|---|---|---|---|
| `SM_IceGun` | 2,460 | under 8,000 | 2048 px: BaseMap, Normal, MaskMap | `PF_IceGun` (prop) and `PF_Viewmodel_IceGun` |
| `SM_Pistol` | 2,612 (frame 1,196, slide 1,124, magazine 292) | under 5,000 | 2048 px: BaseMap, Normal, MaskMap | `PF_Viewmodel_Pistol` |
| `SM_FPArms_Pistol` | 5,572 (right 2,758, left 2,814) | under 8,000 | 2048 px, shared by both pairs of arms | `PF_Viewmodel_Pistol` |
| `SM_FPArms_IceGun` | 5,928 (right 2,966, left 2,962) | under 8,000 | the same set | `PF_Viewmodel_IceGun` |
| `SM_SpecimenJar` | 1,008 | under 3,000 | 1024 px: BaseMap, Normal, MaskMap | `PF_SpecimenJar` (static) |

**Ice gun.** Home-built from laboratory parts: a steel receiver, a taped wooden grip, a gas canister held on two
saddles with tape, a brass pressure gauge tilted towards the player, a valve with a red knob, a copper pipe into a
finned barrel, and a frosted nozzle. 47 cm long; the origin is on the grip and the barrel points along +Z.

**Pistol.** An old service pistol: a blued steel slide with cocking grooves and an ejection port that shows the
barrel, a chamfered muzzle, sights with white dots, checkered walnut grips held by two screws, and the slide stop,
safety and magazine release on the left side. 20 cm long. The frame, slide and magazine are separate objects (one
texture set) so the slide can blow back and the magazine drop out; a round sits on top of the magazine for the
reload. The grip copies the ice gun's size, angle and origin, so one hand pose fits both guns.

**Arms.** Leather gloves and jacket sleeves, posed on each gun: the shooting hand round the grip with the trigger
finger in the guard; the support hand wrapped round it on the pistol, and under the body on the ice gun. Each left
arm is its own object so it can leave the gun during a reload.

**Specimen jar.** A murky glass jar with an aged paper label and a screw lid. 23 cm tall. The top of the glass is
left open because the lid hides it.

## How they were made
1. **Blockout:** primitives at real size (metric, unit scale 1). The arms start as one tube per bone.
2. **Model:** surfaces of revolution for round parts, bevelled boxes and swept profiles for hard parts, with
   recesses inset and pushed in (the pistol's grooves and ejection port). Loops only where the outline bends; faces
   that can never be seen are left out. Edges sharper than 35 degrees are marked sharp, the rest are smooth.
   The gloves are built from metaball bones (one per finger bone), merged into one surface by a voxel remesh and
   rebuilt as even quads with QuadriFlow, so each glove is one seamless all-quad mesh; the sleeves are tubes with
   soft folds and a turned-in cuff.
3. **UV:** Smart UV Project at 66 degrees (seams fall on hard edges), islands averaged for even texel density,
   packed with a 1% margin. Seams are marked in the file.
4. **Texture:** a procedural material (grime from ambient occlusion, worn edges from a bevel mask, rust in
   cavities, paint chips on painted parts, checkering and grain on the wood, pebbled leather, woven canvas) baked
   to the three maps. Mask map: metallic in red, ambient occlusion in green, smoothness in alpha.
5. **Export:** FBX, selected objects only, FBX All scale, -Z forward, Y up, apply transform, face smoothing.

In Unity, **Hollowmere > Props > Set Up Core Models** sets up the ice gun and jar props, and **Hollowmere > Weapons
> Set Up Viewmodels** sets up the pistol and arms and builds the first-person weapons below.

## First-person weapons
`PF_Player > Head > Viewmodels` holds `PF_Viewmodel_Pistol` and `PF_Viewmodel_IceGun`, which fill WeaponSystem's
Pistol Model and Ice Gun Model slots. Q cycles unarmed, pistol, ice gun, and WeaponSystem shows the one in hand.

- **Motion (`WeaponViewmodel`):** the gun is drawn up from below the screen, lags slightly behind the mouse, bobs
  while walking, breathes when still, and pulls in and points up when a wall is close so it never pokes through.
  Recoil is a damped spring. All of it is procedural, so there is no Animator.
- **Firing:** the pistol's slide blows back on every shot and locks open on the last round; a muzzle flash (three
  crossed additive quads), a short orange light and a puff of smoke go off at the muzzle. The ice gun sprays a
  cone of mist with ice crystals and a cold blue light; the cone sits just inside WeaponSystem's freeze cone.
- **Reload:** plays whenever ammo comes in (a pickup or Biscuit's delivery). On the pistol the gun tilts, the
  magazine drops out as the left hand leaves, the hand brings it back up, and the slide is racked. On the ice gun the
  left hand leaves to open the valve while the canister refills.
- **Lighting:** the gun and arms draw on their own rendering layer (bit 1), which the head torch does not
  reach, because a light a few centimetres away would burn them white. A soft fill light that only reaches that
  layer stands in for the torch's spill and switches on and off with it; the shot light reaches both layers.
- **Crosshair (`Crosshair`):** four ticks round a dot that spread after a shot and while moving, tinted per
  weapon; only the dot shows when unarmed.
- **Sound:** fire, empty click, reload and equip for each gun, made from filtered noise and sine waves (no
  recordings): a crack, a body and a low thump with a dark room echo for the pistol; a valve thunk, a gas hiss and
  ice crackle for the ice gun.

**Hollowmere > Weapons > Save Viewmodel Screenshots** renders the player's view of both guns, at rest and firing.
To change where a gun is held, change `PistolOffset` / `IceGunOffset` in `ViewmodelSetup` and run the setup again.

## Optimisation
| Technique | What was done | Why |
|---|---|---|
| Polygon control | Detail only where the shape changes; hidden faces removed | Fewer triangles to draw |
| Triangle budgets | Every model well under its budget (table above) | Predictable cost on a 4 GB GPU |
| One material per model | Every part shares one texture set; all four arms share one | One draw call each |
| Texture size | 2048 for what is seen close up, 1024 for the jar | Video memory is limited |
| Packed mask map | Metallic, AO and smoothness in one image | One texture, not three |
| Texture compression | Compressed, Normal Quality | About a sixth of the memory |
| Mesh compression, Read/Write off | Set on import (Low for the viewmodels, seen from a few centimetres) | Smaller build, less memory |
| Lightmap UVs | Only on the jar, which never moves | The guns are never lightmapped |
| Primitive colliders | One box collider on each prop; none on the viewmodels | Cheap, and the player's own shots pass |
| No shadows from viewmodels | The arms and guns cast no shadows | Saves a shadow pass, and keeps the torch beam clear |
| Procedural animation | Springs and curves in code, no Animator or clips | Nothing to evaluate when idle |
| Small particle budgets | 250 mist, 80 crystals, 30 smoke, world space | The spray stays cheap |
| Audio import | Mono, Vorbis, decompressed on load | Small and plays instantly |
