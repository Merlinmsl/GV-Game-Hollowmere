# Hollowmere: Role Guides (SE3032 Graphics and Visualization)

One guide per role. Each one says what the role is, what you must deliver, and how to do it step
by step, assuming you have never done it before.

| Role | Graphics duty | Guide |
|---|---|---|
| World Builder | Level design, lighting, texturing, NavMesh | [WorldBuilder.md](WorldBuilder.md) |
| Systems Engineer | Player interaction physics: doors, barricades, throwing | [SystemsEngineer.md](SystemsEngineer.md) |
| Core Developer | At least two custom 3D models made from scratch | [CoreDeveloper.md](CoreDeveloper.md) |
| Agent Controller | Agent movement, rotation and animation along the path | [AgentController.md](AgentController.md) |

Each guide also exists as a Word file with the same name in this folder.

These guides cover the **Graphics** module only. The AI agents for the Intelligent Systems
module are described in [Docs/AI](../AI/README.md).

### What each role has delivered so far

| Role | Delivered |
|---|---|
| World Builder | Five streamed zone scenes and the World scene; terrain tiles, vistas, fog zones, flickering lights; baked lighting in one shared probe-volume set; Humanoid and Dog NavMesh with dog links between zones; zone maps; pickups, doors, hatch and gates placed from the markers |
| Systems Engineer | `PF_Player`: movement, crouch, jump, flashlight, health and death, pistol and ice gun, HUD and crosshair; E-key interaction and inventory; hinged doors, study hatch, zone exits, pickups, first-aid boxes, throwable bottles, pushable barricades |
| Core Developer | Custom models made in Blender: the ice gun, the pistol, two pairs of first-person arms and a specimen jar (see [CoreModels.md](../Art/CoreModels.md)); the weapon viewmodels with muzzle flash, ice spray and weapon sounds; character import and set-up (Hollowmere > Characters) |
| Agent Controller | `AgentLocomotion` (constant-rate turning, braking, and animation speed matched to ground speed so the feet do not slide) on `PF_Zombie` and `PF_Creature`, with their prefabs and clips; `GhostMovement` (floating through walls, fading inside them) on `PF_Ghost` |

**Deadline: 21 October 2026.** Source code and a 3-minute demo video, then an individual viva.

---

## 1. How you are marked

| Category | Marks | Who |
|---|---|---|
| Visual cohesion: everything looks like one game | 15 | Group |
| Build stability: the game runs with no errors | 15 | Group |
| Quality of your role's component | 20 | You |
| Git commits and clean files | 10 | You |
| Workflow and tools: you can explain how you built it | 20 | You |
| Design and optimisation: you can justify your choices | 20 | You |

Seventy of the hundred marks are yours alone, and forty of those come from the viva. So for
everything you build, write down **why** you did it that way. Each guide ends with the
questions you should be able to answer.

---

## 2. How the work fits together

1. The **World Builder** builds the zones and leaves named markers where things go: doors,
   keys, pickups, spawn points.
2. The **Core Developer** makes models in Blender and delivers them as prefabs.
3. The **Systems Engineer** builds the player and everything the player can touch, and
   delivers them as prefabs.
4. The **Agent Controller** makes the characters move and animate, and delivers them as prefabs.
5. The World Builder places the delivered prefabs on the markers in the zone scenes.

Everyone delivers **prefabs**. Nobody except the World Builder edits the zone scenes, because
Git cannot merge two people's changes to the same scene.

---

## 3. Before you start (everyone)

1. Follow the [Setup and Testing Guide](../Setup/README.md) until you can press Play and walk
   around.
2. Make your own test scene. In Unity: **File > New Scene > Basic (URP)**, then
   **File > Save As** and save it as
   `Assets/_Hollowmere/Scenes/Sandbox/<YourName>/SB_<YourName>.unity`.
3. Do all your building and testing in that scene.

---

## 4. The daily Git routine (everyone)

Close Unity before switching branches or pulling.

1. Get the latest work: `git checkout dev`, then `git pull`.
2. Make a branch for one small piece of work, for example
   `git checkout -b feature/door-hinge`.
3. Work in Unity. Save with **Ctrl+S**.
4. See what changed: `git status`.
5. Add your folder, not single files, so every `.meta` file comes with its asset:
   `git add -A Assets/_Hollowmere/<your folder>`.
6. Commit with a clear message: `git commit -m "feature: add a hinged door that swings open"`.
7. Push: `git push -u origin feature/door-hinge`.
8. Open a pull request into `dev` on GitHub and merge it.

Rules:

- **Commit every day you work.** Ten marks are for the commit history, and the Intelligent
  Systems module marks it too.
- **Always commit the `.meta` file with its asset.** A missing `.meta` breaks references on
  everyone else's machine.
- **Never commit a scene or script that does not work.**
- **Do not import Asset Store packs.** Everything the game uses is already in the repository.
- Branch types: `feature/`, `fix/`, `art/`, `docs/`, `chore/`.

---

## 5. Naming and folders (everyone)

| Asset | Prefix | Example | Folder under `Assets/_Hollowmere/` |
|---|---|---|---|
| Prefab | `PF_` | `PF_Door_Wood` | `Prefabs/` |
| Static model | `SM_` | `SM_IceGun` | `Art/Models/` |
| Skinned (animated) model | `SK_` | `SK_Zombie` | `Art/Models/` |
| Material | `M_` | `M_IceGun` | `Art/Materials/` |
| Texture | `T_` | `T_IceGun_BaseMap` | `Art/Textures/` |
| Animation clip | `A_` | `A_Zombie_Walk` | `Art/Animations/` |
| Animator controller | `AC_` | `AC_Zombie` | `Art/Animations/` |
| Script | none | `DoorHinge.cs` | `Scripts/Runtime/` |

Move and rename assets **inside Unity only**, so references stay intact.

---

## 6. Limits that apply to everyone

The game must run on a laptop GPU with 4 GB of video memory.

- **Textures:** 2048 px at most; 1024 px for small props.
- **Triangles:** a small prop under 3,000; a weapon held in the hand under 8,000; a character
  under 15,000.
- **Colliders:** boxes, spheres and capsules. A mesh collider only when nothing else fits.
- **Look:** dark, wet, neglected, autumn 1994. Nothing clean, bright or modern.
