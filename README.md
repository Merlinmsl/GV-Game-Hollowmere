# Hollowmere

A first-person horror shooter built in Unity for the SLIIT joint project
**SE3032 Graphics & Visualization** and **SE3062 Intelligent Systems**.

- **Engine:** Unity 6000.3.24f1 (Unity 6 LTS), Universal Render Pipeline
- **Target hardware:** 4 GB VRAM laptop GPUs

## Team

| Role | Graphics duty | Intelligent Systems agent |
|---|---|---|
| World Builder | Level design, lighting, texturing, NavMesh | Director (spawn pacing) |
| Systems Engineer | Player interaction physics | Runner zombie |
| Core Developer | Custom 3D models | Biscuit (dog companion) |
| Agent Controller | Agent movement and animation | Ghost |

## Getting started

**New to the project? Follow the [Setup and Testing Guide](Docs/Setup/README.md):** installing
Unity, cloning, running the game, controls and testing shortcuts, and what is built so far.

In short:

1. Clone the repository and open it with Unity **6000.3.24f1**.
2. Wait for the first import to finish. The pack assets the game uses are already in the repo;
   do not import the Asset Store packs yourself (see *Third-party assets*).
3. Open your sandbox scene from `Assets/_Hollowmere/Scenes/Sandbox/<YourName>/`.

## Folder structure

```
Assets/
├── _Hollowmere/              Everything the team creates
│   ├── Art/
│   │   ├── Materials/
│   │   ├── Models/
│   │   ├── Shaders/
│   │   ├── Textures/
│   │   └── VFX/
│   ├── Audio/
│   ├── Data/                 ScriptableObject configs (tuning values, settings)
│   ├── Prefabs/
│   │   ├── Agents/
│   │   ├── Environment/
│   │   ├── Interactables/
│   │   └── Pickups/
│   ├── Scenes/
│   │   ├── Sandbox/<Name>/   Personal test scenes, never in the build
│   │   └── Zones/            The four playable zone scenes
│   ├── Scripts/
│   │   ├── Editor/
│   │   └── Runtime/
│   └── Settings/
│       ├── Input/
│       ├── Lighting/
│       └── Rendering/        URP assets, renderers, volume profiles
└── ThirdParty/               Asset Store packs, unmodified
```

Rules:

- Never edit files inside `ThirdParty/`. To change a pack asset, duplicate it into
  `_Hollowmere/` and edit the copy.
- Move and rename assets **inside the Unity Editor** only, so `.meta` files and references
  stay intact.
- Empty folders contain a `.gitkeep` file so Git keeps them. Delete it once the folder has content.

## Scenes and the world map

The game is **one connected map**. Each zone is its own scene, placed at its real position in the
world, and the game loads the next zone as the player approaches. The fog hides everything
beyond about 110 m, so that is the distance at which zones are loaded and unloaded.

| Scene | What it is | World position |
|---|---|---|
| `Z1_Outskirts` | The flooded village, in the basin. The player starts on the dry road south of it | z -45 to 100, water level |
| `C1_EstateRoad` | The road climbing through the woods | z 100 to 180, rising about 17 m |
| `Z2_MarrowHouse` | Marrow House and its grounds, on the hilltop | z 195 to 291, 22 m up |
| `Z3_Undercroft` | Marrow's laboratory and the tunnel, under the house and the hill | x 20 to -102, three levels from 20 m down to 9 m |
| `Z4_Waterworks` | The pumping station below the dam: yard, pump hall, outflow pool, radio mast | x -103 to -240, yard at 8.6 m |

- Zone scenes are owned by the World Builder. Other members deliver prefabs that get placed in them.
- Each member experiments in their own `Scenes/Sandbox/<Name>/` scene. Do not edit another
  member's scene. That avoids merge conflicts in `.unity` files, which Git cannot merge reliably.
- Every zone scene has the same root objects: `_Environment`, `_Gameplay`, `_Lighting`, `_Navigation`.
  In Zone 2 these roots carry the zone's world offset, so its contents keep zone-local coordinates.
- To open zones, use the menu **Hollowmere > World**: *Open All Zones (whole map)* loads every
  zone together, *Open Zone* loads one on its own.

### Playing the game: the World scene

`Scenes/World.unity` is the scene to press Play in (**Hollowmere > World > Open World**). It is
always loaded and holds what must survive from zone to zone: the player, the moon, the fog and
sky, the global post-processing, and later the Director and the HUD.

- `ZoneStreamer` (on `_World`) loads a zone when the player comes near it and unloads it once the
  player is far enough away. It finds the player by the **`Player` tag**, so the player prefab
  must carry that tag.
- `DebugPlayer (temporary)` is a stand-in walker (WASD, mouse, Shift to run) until the real player
  is ready. Replace it with the player prefab in the same place.
- Each zone keeps its own camera, moon and post-processing volume so it can be opened and viewed
  alone. They carry `StandalonePreviewOnly` and switch themselves off when the zone is loaded by
  the World scene. Do not put gameplay on those objects.
- **The way into the Undercroft** is a hatch in the floor of Marrow's study, with stairs down.
  The hatch door and its lock are not built yet (Systems Engineer); until then the opening is
  simply open. The stairwell passes through a hole in the terrain, defined in `WorldTerrain.cs`.
- **The tunnel comes out at the Waterworks** through a walled cutting in the hillside. The red
  light seen from the village is on the radio mast here; `WorldVistas.WaterworksBeacon` must
  match the top of that mast.
- `FogZone` changes the fog while the player is inside its box. The Undercroft uses one: the
  pale outdoor mist would make distant walls glow underground.
- `_Vistas` holds what the player sees of zones that are not loaded yet: the silhouette of
  Marrow House above the forest, its lit window, and the red light on the Waterworks mast.
  They fade out as the player gets close. After changing the shape of Marrow House, run
  **Hollowmere > World > Rebuild Vistas** to photograph the silhouette again.
- **Judge the look in Play mode.** While the World scene is open, the editor switches the zones'
  preview moons and cameras off (`PreviewObjectsSync`), so the Scene tab is close to the game.
  If it looks flat and bright, the Scene tab's fog or effects are off: turn them on in the
  effects drop-down of the Scene tab's toolbar. Flickering lights and fog zones only run in Play.
- Anything left in a zone is unloaded with it. Objects that must follow the player across zones
  (Biscuit, the Director) belong in the World scene.

### Terrain

The terrain is split into one tile per scene. All tiles are generated from a single height
function in `Scripts/Editor/WorldTerrain.cs`, so neighbouring tiles meet exactly.
To change the shape of the land, edit that function and run **Hollowmere > World > Rebuild
Terrain Tiles**. Do not sculpt the tiles by hand: the next rebuild would overwrite it.

## Navigation (NavMesh)

Pathfinding uses Unity's built-in NavMesh (AI Navigation package). Each zone scene has a
`_Navigation` object holding one **NavMesh Surface per agent type**. The baked data is stored
next to the scene in `Scenes/Zones/<SceneName>/NavMesh-<Agent>.asset`.

| Agent type | Radius | Height | Step height | Max slope | Used by |
|---|---|---|---|---|---|
| Humanoid | 0.4 m | 1.9 m | 0.55 m | 40° | Runner zombie |
| Dog | 0.25 m | 0.7 m | 0.5 m | 45° | Biscuit |

Ghosts fly through walls and do not use the NavMesh.

| Area | Cost | Meaning |
|---|---|---|
| Walkable | 1 | Dry ground: boardwalks, floors |
| Water | 3 | Flooded ground. Agents wade through it only when the dry route is more than 3x longer |
| Not Walkable | - | Blocked |

Rules:

- Set your `NavMeshAgent`'s **Agent Type** to match the table. An agent only sees the surface baked for its type.
- The bake reads **colliders**, not render meshes. Anything that should block agents needs a collider.
- Moving doors and barricades use a `NavMeshObstacle` with **Carve** enabled. Do not rebake at runtime.
- After changing level geometry, the World Builder rebakes: select each surface under `_Navigation` and press **Bake**.
- Gameplay markers and triggers live under `_Gameplay`, which is excluded from the bake.
- Each surface bakes a **volume** around the playable area at walking height, so roofs and the
  hills outside the level get no NavMesh.
- Step heights are set so agents wading in the flood (bed about 0.5 m below the boardwalk) can climb onto it.

### Crossing between zones

Each zone has its own NavMesh, and they are **not** joined for the Humanoid agent: enemies
belong to their zone and cannot follow the player into the next one. Only the Dog agent has
`NavMeshLink`s across the zone joins (`NavLink_Dog_*` under `_Navigation`), so Biscuit can
follow the player along the whole road.

## Naming conventions

Asset names use `PascalCase` with a type prefix:

| Type | Prefix | Example |
|---|---|---|
| Scene (zone) | `Z<n>_` | `Z1_Outskirts` |
| Scene (sandbox) | `SB_` | `SB_Minsara` |
| Prefab | `PF_` | `PF_RitualLantern` |
| Static mesh / model | `SM_` | `SM_Wardrobe` |
| Skeletal mesh | `SK_` | `SK_Biscuit` |
| Material | `M_` | `M_WetStone` |
| Texture | `T_` + suffix | `T_WetStone_BaseMap`, `T_WetStone_Normal` |
| Animation clip | `A_` | `A_Runner_Sprint` |
| Animator controller | `AC_` | `AC_Runner` |
| Volume profile | `VP_` | `VP_Z3_Undercroft` |
| ScriptableObject data | `SO_` | `SO_DirectorConfig` |
| Audio clip | `SFX_` / `MUS_` / `VO_` | `SFX_IceShatter` |

C# scripts use `PascalCase` file names matching the class name, inside the `Hollowmere` namespace.

## Third-party assets

Asset Store packs live in `Assets/ThirdParty/`. The folder is git-ignored by default, and only the
pack files **a zone scene actually uses** are committed (force-added). Everything a zone needs is
therefore in the repository: **you do not import any packs to open the game**.

The packs ship very large source textures (4096 px TIFs of 48-64 MB each; Zone 1 alone used
1.15 GB). Unity imports them at 2048 px anyway, so the used textures are stored re-encoded at
2048 px: JPG for opaque colour, lossless PNG for normal maps and anything with an alpha channel.
Each file keeps its `.meta`, so asset IDs and references are unchanged. Zone 1 went from
1.15 GB to about 180 MB with no visible difference.

Rules:

- **Do not import a pack that is already in the repo.** A fresh import brings back the original
  `.tif` files next to the converted ones with the same asset IDs, and Unity breaks the references.
  If you need another piece from a pack, ask the World Builder to add it.
- If you already have a local `Assets/ThirdParty/` folder from before, delete it before pulling.
- Never edit files inside `ThirdParty/`. Duplicate into `_Hollowmere/` and edit the copy.
- The repository is private. Asset Store content must not be pushed to a public repository.

Adding assets from a pack (World Builder): see `Assets/ThirdParty/README.md`.

## Git workflow

- `main`: stable, submission-ready. Merged from `dev` only.
- `dev`: integration branch. Merge feature branches into it.
- Feature branches: `<type>/<short-description>`, created from `dev`.
  Types: `feature/`, `fix/`, `chore/`, `docs/`, `art/`.
- Commit messages: `<type>: <what changed>` in the imperative mood, for example
  `feature: add stress model to the Director`.
- Commit small, working steps often. Do not commit broken scenes or scripts with compile errors.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` and IDE files are never committed.
