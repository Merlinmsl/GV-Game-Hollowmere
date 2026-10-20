# Hollowmere: Setup and Testing Guide

A step-by-step guide for team members: install Unity, get the project, run it, walk through the
world, and see what is already built.

For team rules (folders, naming, Git branches) see the [main README](../../README.md).
For the game's story see [Docs/Story](../Story/README.md).

---

## 1. Install what you need

You need three things: Git, Unity Hub, and the exact Unity version the project uses.

### 1.1 Git

1. Download Git from <https://git-scm.com/downloads> and install it with the default options.
2. Open a terminal and check it works:

   ```bash
   git --version
   ```

### 1.2 Unity Hub

1. Download Unity Hub from <https://unity.com/download>.
2. Install it and sign in with a Unity account (a free Personal licence is enough).

### 1.3 Unity Editor 6000.3.24f1

The project must be opened with **exactly this version**. A different version will re-import
everything and can break scenes for the whole team.

1. In Unity Hub, open **Installs > Install Editor > Archive**.
2. Follow the link to the download archive, find **6000.3.24f1** (Unity 6.3 LTS) and click
   **Install**. Unity Hub opens and starts the download.
3. When asked for modules, the defaults are enough. Keep **Windows Build Support** ticked.

The download is several gigabytes, so start it early.

---

## 2. Get the project

1. Make sure you have been added to the GitHub repository (it is private). Ask Minsara if not.
2. Choose a folder with at least **15 GB free**. Avoid OneDrive or other synced folders.
3. Clone the repository and switch to the integration branch:

   ```bash
   git clone https://github.com/Merlinmsl/GV-Game-Sample.git
   ```

   ```bash
   cd GV-Game-Sample
   ```

   ```bash
   git checkout dev
   ```

**Do not import any Asset Store packs.** Every pack file the game uses is already in the
repository. Importing a pack again breaks the materials (everything turns pink).

---

## 3. Open the project in Unity

1. In Unity Hub, click **Add > Add project from disk** and select the `GV-Game-Sample` folder.
2. Check the version shown beside the project is **6000.3.24f1**, then click the project.
3. **Wait.** The first open imports every asset and takes 10 to 30 minutes. Later opens take
   about a minute.
4. If Unity asks to enter Safe Mode, choose **Ignore** and tell the team: it means a script
   does not compile.

---

## 4. Run the game

1. In the menu bar at the very top of Unity, click
   **Hollowmere > World > Open World (press Play here)**.
2. Look at the Hierarchy panel on the left. It should list six scenes, with **World** in bold:
   `World`, `Z1_Outskirts`, `C1_EstateRoad`, `Z2_MarrowHouse`, `Z3_Undercroft`, `Z4_Waterworks`.
3. Press the **Play** button (the triangle at the top centre of the window).
4. Unity switches to the **Game** tab. **Click once inside it** so the mouse controls the view.
5. To stop: press **Esc** to free the mouse, then press **Play** again.

Always judge the look in Play mode. If the Scene tab looks flat and bright with no mist, its fog
or effects are switched off: turn them on in the effects drop-down of the Scene tab's toolbar.

Changes you make while Play is running are thrown away when you stop. Stop first, then edit.

---

## 5. Controls

The project currently uses a **temporary debug player** so the world can be walked before the
real player is ready. It can walk and look. It cannot jump, crouch, shoot or interact.

### Moving

| Key | Action |
|---|---|
| **W** | Walk forward |
| **S** | Walk backward |
| **A** | Step left |
| **D** | Step right |
| **Mouse** | Look around |
| **Left Shift** (hold) | Run |
| **Left mouse button** | Lock the mouse to the game again |
| **Esc** | Free the mouse |
| **M** | Open or close the map of the zone you are in (pauses the game) |
| **X** | Test only: pick up the nearest map item within 2.5 m, until the real pickups exist |

### Testing shortcuts

Number keys jump straight to a place, so you do not have to walk the whole map.

| Key | Jumps to |
|---|---|
| **1** | Zone 1: the start, on the road south of the flooded village |
| **2** | Zone 1: the estate gate, at the foot of the road |
| **3** | Half way up the estate road |
| **4** | Zone 2: the forecourt in front of Marrow House |
| **5** | Zone 2: inside the mansion, entrance hall |
| **6** | Zone 3: the Undercroft laboratory |
| **7** | Zone 4: the Waterworks, where the tunnel comes out |
| **F1** | Hide or show the help text |

After a jump the player hangs still for a second while the zone loads, then lands.

### The text in the top-left corner

- **Position**: where you are standing (x, y, z). Quote these numbers when you report a problem.
- **Loaded**: the zones currently in memory. Zones load and unload by themselves as you move.

---

## 6. A full walk-through to test the world

Walking it once is the best way to learn the map. Start with key **1**.

1. **The village.** Follow the boardwalk north through the flooded cabins to the estate gate.
2. **The estate road.** Go through the gate and follow the road up the hill through the pines.
3. **Marrow House.** Pass the second gate. The front steps are **not** in the middle: the
   stairs run up from the left and right sides of the porch. Go in through the front door.
4. **The study.** From the entrance hall, go right (east) into the study. Along the front wall
   there is an open hatch in the floor with a handrail. Step onto the stairs at its far (east)
   end and go down.
5. **The Undercroft.** Two flights of stairs lead to the laboratory. Cross it, take the
   doorway at the far end and go down again into the tunnel. Go through the large pillared
   chamber and leave by the corridor in its north-west corner.
6. **The Waterworks.** The tunnel comes out in a walled cutting. The brick pump hall is
   straight ahead, with the dam behind it.

Things worth checking and reporting:

- Places where you get stuck or fall through the floor.
- Objects that float, sink into the ground or block a doorway.
- Zones that visibly pop into view instead of appearing out of the fog.

---

## 7. What is built so far

### The world (World Builder)

| Zone | Scene | What is there |
|---|---|---|
| 1. Flooded Village | `Z1_Outskirts` | Flooded cabins, boardwalks, guard house, estate gate |
| Estate Road | `C1_EstateRoad` | The road climbing through the forest |
| 2. Marrow House | `Z2_MarrowHouse` | The mansion, furnished on both floors, estate walls |
| 3. The Undercroft | `Z3_Undercroft` | Stairs from the study, laboratory with cages, tunnel, creature chamber |
| 4. The Waterworks | `Z4_Waterworks` | Yard, dam, pump hall, outflow pool, radio mast |

Also working:

- **One connected map.** The `World` scene holds the player, moon, fog and sky, and loads each
  zone as the player gets near it.
- **Baked lighting** in every zone.
- **NavMesh** in every zone, for two agent types: `Humanoid` and `Dog`.
- **Far views.** From the village you can see the mansion on its hill and the red light at the
  Waterworks.
- **Gameplay markers.** Empty objects under `_Gameplay` in each zone mark where things go:
  spawn points, keys, evidence pages, pickups, doors, the creature, the final fight.

### Not built yet

| Missing | Owner |
|---|---|
| The real player (movement, weapons, flashlight, health) | Systems Engineer |
| Doors, the study hatch, locks, keys, pickups, evidence collection | Systems Engineer |
| Zombies, ghosts, the creature, Dr Marrow | Each agent's owner |
| Biscuit the dog | Core Developer |
| The Director (decides when and where enemies appear) | World Builder |
| Mansion grounds (trees, graves), laboratory equipment | World Builder / Core Developer |
| Sound, menus, HUD, ending | To be assigned |

---

## 8. Notes for whoever builds on this

- **The player must have the tag `Player`.** Zone loading and the fog look for that tag. To
  swap in the real player, put it where `DebugPlayer (temporary)` is in the `World` scene and
  delete the debug one.
- **Enemies stay in their zone.** The `Humanoid` NavMesh is deliberately not joined between
  zones. Only the `Dog` NavMesh is linked, so Biscuit can follow the player everywhere.
- **Anything inside a zone is removed when that zone unloads.** Objects that must follow the
  player across zones belong in the `World` scene.
- **Use the markers.** Find them under `_Gameplay` in each zone scene. Their names say what
  they are for, for example `Key_Tunnel`, `Evidence_MaraMedicalFile`, `CreatureSpawn`,
  `Trigger_Confrontation`.
- **Work in your own sandbox scene** (`Assets/_Hollowmere/Scenes/Sandbox/<YourName>/`) and hand
  over prefabs. Do not edit the zone scenes: Git cannot merge two people's changes to a scene.
- **To open one zone alone** for a closer look: **Hollowmere > World > Open Zone**. There is no
  player in a zone on its own, so use the World scene to walk around.

---

## 9. Getting the latest version

Before you start work each day:

```bash
git checkout dev
```

```bash
git pull
```

Then make your own branch for the work, for example:

```bash
git checkout -b feature/player-controller
```

Close Unity before switching branches or pulling. It avoids half-imported assets.

---

## 10. If something goes wrong

| Problem | Fix |
|---|---|
| **Everything is pink** | A pack was imported on top of the repo's files. Close Unity, delete `Assets/ThirdParty`, run `git checkout -- Assets/ThirdParty`, reopen. |
| **The "Hollowmere" menu is missing** | A script has a compile error. Open **Window > General > Console** and read the red message. |
| **Game tab is black, or says "No cameras rendering"** | You are not in Play mode, or `World` is not the bold scene. Use **Open World (press Play here)** again. |
| **The mouse does not look around** | Click once inside the Game tab. |
| **The Scene tab shows nothing** | Select `_World` in the Hierarchy, move the mouse over the Scene tab and press **F**. |
| **The player falls forever after a jump key** | The zone did not load. Stop, press Play again, and report which key. |
| **Unity wants to upgrade the project** | You opened it with the wrong Unity version. Cancel and install 6000.3.24f1. |
| **Pulling fails with "local changes would be overwritten"** | Unity changed a file by itself. Run `git status` to see which, and `git checkout -- <file>` if you did not mean to change it. |
