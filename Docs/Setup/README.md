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

The game uses the real player, `PF_Player`. (The old `DebugPlayer (temporary)` walker is still
in the World scene but switched off.)

### Moving

| Key | Action |
|---|---|
| **W A S D** | Walk |
| **Mouse** | Look around |
| **Left Shift** (hold) | Run (forward only). Louder: enemies hear you further away |
| **C** or **Left Ctrl** (hold) | Crouch. Quieter, and harder to see |
| **Space** | Jump |
| **Esc** | Free the mouse. **Left mouse button** locks it again |

### Using things

| Key | Action |
|---|---|
| **Left mouse button** | Fire the weapon in hand |
| **Q** | Switch weapon: none, pistol, ice gun. The pistol is skipped until you find it in the Zone 1 guard house |
| **F** | Flashlight on or off. Also picks up a bottle in front of you, and throws it when held |
| **E** | Use what you are looking at: pick up, open a door or gate, use a first-aid box |
| **M** | Open or close the map of the zone you are in (pauses the game) |
| **R** | After dying: restart |

The weapons:

- **Pistol:** 35 damage, three times that for a head shot. A zombie has 100 health. Bullets pass
  through ghosts. A shot is a noise of 45 m, so it wakes the dead.
- **Ice gun:** a short cone (15 m, 10°) that freezes ghosts and zombies for 3.5 s. Walls block it.
  Each shot uses 20 of its 100 pressure; ice canisters refill it.

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

After a jump the player's controller is switched off for a moment while the zone loads, then it lands.

Other test keys (on the player, for testing only):

| Key | Action |
|---|---|
| **X** | Pick up the nearest map item within 2.5 m |
| **G** | Make a gunshot noise (45 m) without firing |
| **H** | Make a door noise (10 m) |
| **P** / **O** / **J** | Drop a noise probe / a senses probe / a grey stand-in enemy at your feet |
| **F2** | Show what the AI is thinking: the Director's stress graph, Biscuit's plan, labels over zombies. Ghosts show their label (mode, sight meter, belief) whenever **Show Label** is ticked on `GhostBody` |

### The HUD

- **Bottom right:** health bar and, with a weapon in hand, its name and ammunition.
- **Centre:** the crosshair. It spreads when you fire or move, and takes the weapon's colour.
- **Interaction prompt:** what **E** will do, for example "[E] Open" or "Locked".

---

## 6. A full walk-through to test the world

Walking it once is the best way to learn the map. Start with key **1**.

1. **The village.** Follow the boardwalk north through the flooded cabins. Twelve zombies lie
   dormant here: walk or crouch past them, or shoot. The pistol is in the guard house. Find the
   **two clues** (a newspaper clipping and a diary page) and the **Marrow House key**.
2. **The estate gate.** Its left leaf opens with **E** once both Zone 1 clues are found.
3. **The estate road.** Follow the road up the hill through the pines. No enemies here.
4. **Marrow House.** The front steps are **not** in the middle: the stairs run up from the left
   and right sides of the porch. The front door needs the house key. Four ghosts haunt the house;
   only the ice gun stops them. Find **three of the four pages** and the **tunnel key**.
5. **The study.** From the entrance hall, go right (east) into the study. The hatch in the floor
   by the front wall opens with the tunnel key and three Marrow House pages. Take the stairs down.
6. **The Undercroft.** Two flights of stairs lead to the laboratory. Cross it, open the door at
   the far end and go down into the tunnel, where the creature hunts by sound. Find the **three
   tunnel pages** and the **tunnel exit key**; the door out to the Waterworks needs them all.
7. **The Waterworks.** The tunnel comes out in a walled cutting. The brick pump hall is straight
   ahead, with the dam behind it. Its door opens with eight pages from the whole game.

Each zone has a first-aid box (not the tunnel). Press **E** at it to heal; it holds a few doses.

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
- **Baked lighting** in every zone (one shared Adaptive Probe Volume baking set; see
  **Hollowmere > World > Lighting**).
- **NavMesh** in every zone, for two agent types: `Humanoid` and `Dog`.
- **Far views.** From the village you can see the mansion on its hill and the red light at the
  Waterworks.
- **Gameplay markers.** Empty objects under `_Gameplay` in each zone mark where things go:
  spawn points, keys, evidence pages, pickups, doors, the creature, the final fight.
- **The map screen** (**M**), with the items still to find and the zone exits.

### The player and what it can touch (Systems Engineer)

| Part | Where | What it does |
|---|---|---|
| Player | `PF_Player` (`Player/PlayerController.cs`) | Walk, run, crouch, jump, flashlight; makes footstep noises for the AI |
| Health | `Player/PlayerHealth.cs` | 100 health, a short invulnerability after each hit, red flash, death screen, **R** to restart |
| Weapons | `Player/WeaponSystem.cs`, `WeaponViewmodel.cs` | Pistol and ice gun with first-person models, muzzle flash, ice spray and sounds |
| HUD | `Player/PlayerHud.cs`, `Crosshair.cs` | Health, weapon, ammunition, crosshair |
| Interaction | `Interaction/PlayerInteractor.cs`, `PlayerInventory.cs` | **E** on what you look at; keys, pages, ammunition, canisters, batteries |
| Doors and gates | `Physics/Door.cs`, `ZoneExit.cs` | Locked doors need a key; zone exits need a key and the zone's pages |
| Physics props | `Physics/Throwable.cs`, `Barricade.cs` | Bottles to throw as a distraction (a noise where they land), barricades to push |
| Pickups | `Pickups/Pickup.cs`, `HealthStation.cs` | Pickups and first-aid boxes, placed in every zone |

### The AI (Intelligent Systems)

| Agent | Where | Prefab | Details |
|---|---|---|---|
| Zombies (behaviour tree) | Zone 1, twelve | `PF_Zombie` | [AI plan section 5](../AI/README.md) |
| Ghosts (utility over an influence map) | Zone 2, four | `PF_Ghost` | [Ghost.md](../AI/Ghost.md) |
| The creature (zombie brain, sharper hearing) | Zone 3 | `PF_Creature` | [AI plan section 2](../AI/README.md) |
| Biscuit (goal-oriented planner) | World scene, follows the player | `PF_Biscuit` | [Biscuit.md](../AI/Biscuit.md) |
| The Director (utility AI, stress model) | World scene, `_Director` | none | [AI plan section 8](../AI/README.md) |

### Not built yet

| Missing | Owner |
|---|---|
| Dr Marrow's behaviour (`PF_Marrow` is placed in Zone 4 with no AI) | Not assigned |
| Sound beyond the weapons (footsteps, doors, enemies, ambience, music) | Not assigned |
| Menus and the ending | Not assigned |
| Mansion grounds (trees, graves), laboratory equipment | World Builder / Core Developer |

---

## 8. Notes for whoever builds on this

- **The player must have the tag `Player`.** Zone loading and the fog look for that tag.
  `PF_Player` has it. Keep the old `DebugPlayer (temporary)` switched off: two active players
  would confuse the zone streamer and the AI.
- **The AI never searches the scene for the player.** The player announces itself through
  `PlayerRegistry`; enemies only know what their senses tell them.
- **Anything loud calls `NoiseSystem.Emit`.** That is how the AI hears. Doors, gates, bottles,
  barricades, shots and footsteps already do.
- **Placed objects are rebuilt.** Doors, gates and pickups named `Placed_...` come from
  **Hollowmere > World > Place Pickups, Doors and Gates**. Change that script, not the objects.
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
| **A gate or hatch will not open** | Look at the prompt: it says what is missing (a key or how many pages). Press **M** to see the items left in the zone. |
| **The ghosts do not move** | The Console says a ghost "is not inside a baked GhostHouseVolume". The Zone 2 ghost map needs baking again (see [Ghost.md](../AI/Ghost.md)). |
| **Unity wants to upgrade the project** | You opened it with the wrong Unity version. Cancel and install 6000.3.24f1. |
| **Pulling fails with "local changes would be overwritten"** | Unity changed a file by itself. Run `git status` to see which, and `git checkout -- <file>` if you did not mean to change it. |
