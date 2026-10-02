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

1. Clone the repository and open it with Unity **6000.3.24f1**.
2. Import the Asset Store packs listed in [`Assets/ThirdParty/README.md`](Assets/ThirdParty/README.md)
   and move each one into `Assets/ThirdParty/` from inside the Editor.
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

## Scenes

- Each zone is its own scene in `Scenes/Zones/`, loaded in story order.
- Each member experiments in their own `Scenes/Sandbox/<Name>/` scene. Do not edit another
  member's scene. That avoids merge conflicts in `.unity` files, which Git cannot merge reliably.
- Zone scenes are owned by the World Builder. Other members deliver prefabs that get placed in them.

## Naming conventions

Asset names use `PascalCase` with a type prefix:

| Type | Prefix | Example |
|---|---|---|
| Scene (zone) | `Z<n>_` | `Z1_Outskirts` |
| Scene (sandbox) | `SB_` | `SB_Sakitha` |
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

The packs are too large to store in Git (two of them are about 1 GB each), so:

1. `Assets/ThirdParty/*` is git-ignored. Every member imports the packs locally.
2. An asset that the game **actually uses** must be committed so the project opens for the
   assessors. Force-add the asset **and its `.meta` file** (and any textures or materials it
   depends on):

   ```bash
   git add -f "Assets/ThirdParty/Decrepit Dungeon LITE/Prefabs/Floors/Floor_A.prefab" "Assets/ThirdParty/Decrepit Dungeon LITE/Prefabs/Floors/Floor_A.prefab.meta"
   ```

   Once a file is tracked, Git follows later changes to it normally.

Asset GUIDs come from the pack itself, so references work on every machine as long as each
pack sits at the same path under `ThirdParty/`.

## Git workflow

- `main`: stable, submission-ready. Merged from `dev` only.
- `dev`: integration branch. Merge feature branches into it.
- Feature branches: `<type>/<short-description>`, created from `dev`.
  Types: `feature/`, `fix/`, `chore/`, `docs/`, `art/`.
- Commit messages: `<type>: <what changed>` in the imperative mood, for example
  `feature: add stress model to the Director`.
- Commit small, working steps often. Do not commit broken scenes or scripts with compile errors.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` and IDE files are never committed.
