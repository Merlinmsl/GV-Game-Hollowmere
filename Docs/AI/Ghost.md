# Ghosts: utility scoring over an influence map

The ghosts of Marrow House (Zone 2): the servants and Mara. Ghosts pass through walls, so finding
a path is not the interesting part. The interesting part is **where to be**: somewhere the player
is not looking, in the dark, at the right distance, out of the ice gun's aim. Every ghost answers
that question several times a second from what its own senses tell it. Section 6 of the
[AI plan](README.md) is the original design. This page describes what was built and why each
number is what it is.

Code: `Assets/_Hollowmere/Scripts/Runtime/AI/Ghost/` (the paths below are relative to it).
Tests: `Assets/_Hollowmere/Scripts/Tests/EditMode/Ghost/` (99 EditMode tests).
Body hook: `Assets/_Hollowmere/Scripts/Runtime/Agents/GhostMovement.cs` (`MoveTo`, `Stop`, `SetSpeedScale`).

---

## 1. How it fits together

```
 senses (shared)            brain (plain C#, unit-tested)                       body (Unity)
 ---------------            ------------------------------------------          ------------
 SightSensor --See-->  AgentMemory belief + PlayerSighting + known weapon
 NoiseSystem --Hear->        |
 Director ---Told--->        v
 Ice gun --Freeze--->  ModeSelector (utility)  ->  Idle / Stalk / Ambush / Strike / Flee / Frozen
                             |
                             v
                       PositionScorer + HillClimber  (where to be, on the InfluenceMap)
                       BFS: walking field, flee and lurk walls
                             |
                             v
                       A* (time-sliced) -> PathSmoother -> waypoints  -->  GhostRouteFollower -> GhostMovement
```

| Part | File | What it does |
|---|---|---|
| Body | `Body/GhostBody.cs` | The only MonoBehaviour that matters. Senses (`SightSensor`, `INoiseListener`), thinks through the shared `AiScheduler` (`IAiThinker`, 5 Hz), can be sent closer by the Director (`IDirectable`) and frozen by the ice gun (`IIceable`). Runs route slices, passes claims between ghosts, looks at its **belief**, never at the player. Decides nothing. |
| Brain | `Brain/GhostBrain.cs` | Plain C#: perceptions in, mode and route out. No scene, no physics, so it is fully unit-tested. Allocates nothing after construction. |
| Modes | `Brain/ModeSelector.cs`, `Brain/ModeTuning.cs` | Utility AI over Stalk, Ambush, Strike and Flee, with hysteresis. All the numbers live in `ModeTuning`. |
| Who it is | `Brain/GhostProfile.cs`, `Profiles/SO_GhostProfile_*.asset` | ScriptableObjects. Mara and the servants share one brain and differ only in their profile. |
| Influence map | `Map/InfluenceMap.cs`, `Map/GhostGrid.cs` | A 1 m grid per storey: light (baked lamps plus flashlight) and watched (fading over 8 s). |
| Baking | `Map/GridBaker.cs`, `Map/GhostHouseVolume.cs`, `Map/GhostHouseMap.cs` | Bakes walls, floors, walkable cells and lamp light into an asset (context menu "Bake Ghost Map"). |
| Position | `Positioning/PositionScorer.cs`, `Positioning/HillClimber.cs`, `Positioning/ScoreWeights.cs` | Weighted-sum score of a cell, optimised by steepest-ascent hill climbing from ring seeds. |
| Search | `Search/BfsField.cs`, `Search/AStarSearch.cs`, `Search/PathSmoother.cs`, `Search/RouteCosts.cs`, `Search/SafeWallGoal.cs`, `Search/AimCone.cs` | BFS walking field and nearest-safe-wall search, A* through walls with exposure costs, string pulling. |
| Movement | `Movement/GhostRouteFollower.cs` | Pure pursuit: a carrot 2.5 m ahead on the waypoints is fed to `GhostMovement.MoveTo`. |
| Debug | `Debugging/GhostMapDebugView.cs` (F2/F3), `GhostProfilerHud.cs` (F4), `GhostCombatStub.cs`, `GhostLoadTest.cs`, `GhostRouteProbe.cs` | Heatmaps, the ghost's thinking, per-frame cost, a stand-in ice gun, a repeatable load. |

**Logic is separate from visuals.** The brain sees a grid and numbers. The body turns them into
movement and fading. The tests drive the brain with no scene at all.

## 2. Each thought (5 times a second, staggered by the scheduler)

`Body/GhostBody.cs:225` then `Brain/GhostBrain.cs:326`:

1. **Sense.** The sight sensor ticks. If the player is recognised, `See` stores the position, look
   direction, weapon and flashlight, and marks what the player is looking at on the shared map
   (`Brain/GhostBrain.cs:231`). Noises arrive as they happen (`Hear`, `:284`). A shot gives away the
   weapon even unseen. Bark and Groan say nothing about the player.
2. **Not here?** If the ghost stands on its believed player position, can see it and sees nobody
   for 1 s, it drops the belief (`:718`, see section 7).
3. **Locate.** Where the searches centre: the believed cell, or, if the player is outside the
   house, the inside cell nearest them (`:675`). It also notes whether the player is cornered.
4. **Gather.** The mode inputs, from the ghost's own knowledge only (`:422`).
5. **Choose a mode** by utility (`Brain/ModeSelector.cs:69`). Flee dwell and hysteresis apply.
6. **Carry it out.** Each mode picks a target cell (section 5).
7. **Route.** `RouteTo` starts an A* search when the goal changes or every second (`:540`). The
   search runs in budgeted slices in `LateUpdate` (`Body/GhostBody.cs:284`). A flight route
   comes straight from the BFS with no A* wait.

The ghost never reads the player's true state. The only way the player's position, look and
weapon reach the brain is through `See`, `Hear` and `Told`.

## 3. The influence map

One 1 m cell per square metre per storey (`Map/GhostGrid.cs`). Flags: **Inside** (part of the
house), **Solid** (inside a wall: ghosts pass, the player and light do not) and **Walkable** (the
player can stand here, sampled from the NavMesh).

| Layer | How it is made | Why |
|---|---|---|
| `light` | Baked from the lamps with line-of-sight falloff (`Map/GridBaker.cs:126`), plus the flashlight cone when a ghost sees it on (`Map/InfluenceMap.cs:73`). | Ghosts prefer the dark. Re-bakeable when the generator restores the lights. |
| `watched` | Each cell stores the **time** it was last seen. Watched = 1 - age / 8 s (`Map/InfluenceMap.cs:48`). | Lazy decay: nothing is updated per frame. Only the cells inside a sighting's view cone are written (`:64`). |

The map is **shared per house**: what one ghost saw the player look at, all ghosts know. Baked
sizes: sandbox 24x20x2 (11 ms); Marrow House 46x42x2, 1980 walkable cells (20 ms).

## 4. Choosing the mode (utility AI)

`Brain/ModeSelector.cs:53`. Each mode's utility is a **product** of response curves, so any one
consideration at 0 vetoes the mode (an aimed ice gun forbids Strike however close the ghost is):

```
Stalk  = stalkBase x C
Ambush = ambushWeight x C x (1 - F) x (0.5 + 0.5 G)
Strike = strikeWeight x C x Ready x Inside x Near(D) x (0.25 + 0.75 B) x (1 - Aimed) x Tactic x Patience
Flee   = fleeWeight x max(Aimed, IceGun x E x 0.6 x (1 - G))

C confidence (seen 1.0, heard 0.6, told 0.4, fading e^(-t/8))   F sighting freshness
D distance to belief   B facing away (1 = back to the ghost)   E exposure where the ghost hides
G aggression   Near(D) = 1 - t^2, t from strikeDistance to strikeReach   Patience = 0.5 -> 1 over patienceSeconds
Tactic = carefulStrikeScale with the ice gun about, boldStrikeScale otherwise   Inside = 0 if the player is outside
```

The highest utility wins and the current mode gets `switchMargin` (0.1) on top (`:69`).

| Mode | When | What it does (`Brain/GhostBrain.cs`) |
|---|---|---|
| Idle | No belief | Home to its haunt anchor, then short, slow, random wanders (`Wander :766`) |
| Stalk | Seen | The best hidden cell 4 to 8 m away on foot (sections 5 and 6) |
| Ambush | Heard, not seen | The same search with ambush weights: close (1.5 to 3.5 m), dark, unwatched, beside a wall |
| Strike | Close, behind, cooldown over, no other ghost has just struck | Closes on the belief. **Lands only if its eyes saw the player within 1.6 m in the last 0.5 s** (`TryStrike :480`). Then a cooldown, and it fades into a wall. One striker at a time: after any strike, no ghost in the house may strike for 1.5 s (`Body/GhostBody.cs:257`). |
| Flee | Aimed at, or nervous in the light with the ice gun known | BFS to the nearest wall cell out of the aim. The route is taken at once (`Flee :498`). |
| Frozen | Hit by the ice gun | Stops dead (`GhostMovement.Stop`). When it thaws, it flees (`Freeze :302`). |

**Weapon tactics** (`Tactics :462`): with the ice gun known (seen or heard, remembered for 20 s)
the ghost keeps 6 to 10 m off, cover x1.5, unseen x1.3, strike x0.4. With a pistol or nothing it is
bold (strike x1.2).

**Mara vs servants** (`Brain/ModeTuning.cs:82`, `:123`):

| | Servant | Mara |
|---|---|---|
| Aggression | 0.4 | 0.9 |
| Strike weight / cooldown | 1.0 / 6 s | 1.3 / 4 s |
| Patience | 15 s | 6 s |
| Careful range / strike scale | 6-10 m / 0.4 | 4-8 m / 0.6 |
| Stalk range (profile) | 4-8 m | 3-6 m |
| Leash | 6 m | 40 m (the whole house) |
| Wander radius / speed | 4 m / 0.35x | 8 m / 0.5x |

## 5. Choosing a position: the score

`Positioning/PositionScorer.cs:116`. A weighted sum of 0..1 terms (weights `Positioning/ScoreWeights.cs:31`):

```
score = 1.0 behind + 1.0 dark + 1.2 unseen + 0.4 cover + 1.5 rangeFit(walking distance)
      + 0.5 home - 3.0 inIceGunAim - 1.0 nearOtherGhost
```

| Term | Meaning | Line |
|---|---|---|
| behind | 1 behind the last seen look direction, 0.5 to the side, 0 in front. 0.5 if the look is unknown. | `:142` |
| dark | 1 - light | `:123` |
| unseen | 1 - watched | `:123` |
| cover | Solid cells among the 8 around, 3 or more = 1 | `:155` |
| range | Trapezoid on **walking** distance from the BFS field (4-8 m full, 4 m soft edges) | `:171`, `ScoreWeights.cs:65` |
| home | 1 within the leash of the haunt anchor, 0 at twice it | `:189` |
| threat | In the seen ice-gun cone (12 degrees, 15 m) with no wall between | `:198` |
| crowd | Near a cell another ghost has claimed (3 m radius) | `:210` |

**Why a weighted sum and not a product here:** positions trade off. A slightly lit cell perfectly
behind the player can beat a dark cell in front of them. Every term is visible in the F3 overlay,
and each weight reads directly as "how many points this is worth at most".

**Worked example** (player looking east, ghost choosing between two cells 5 m away):

| | behind | dark | unseen | cover | range | home | threat | total |
|---|---|---|---|---|---|---|---|---|
| A: west, beside a wall, light 0.1 | 1 x 1.0 | 0.9 x 1.0 | 1 x 1.2 | 0.67 x 0.4 | 1 x 1.5 | 1 x 0.5 | 0 | **5.37** |
| B: east, under a lamp (0.6), just looked at (0.9) | 0 | 0.4 x 1.0 | 0.1 x 1.2 | 0 | 1 x 1.5 | 1 x 0.5 | 0 | **2.52** |

A wins by 2.85. If the player then aims the ice gun at A, A loses 3.0 and drops to 2.37, below B.

## 6. The search core

| Search | Where | What and why |
|---|---|---|
| **BFS walking field** | `Search/BfsField.cs:99` | From the believed cell over walkable cells (8 neighbours, stairs by `WalkY` within 0.75 m). It gives the **range** term walking distance, so "close" means close on foot, not through a wall. A diagonal step is longer than a straight one, so plain BFS (every move = 1) would undercount by up to 29%. Instead it is **Dial's algorithm**: BFS with two move costs (straight 5, diagonal 7, so a diagonal is 1.4 m, within 1% of the true 1.41). The FIFO queue becomes a ring of 8 buckets, one per total cost; cells leave cheapest first, so each distance is exact, still O(cells) and with no heap. Limited to the reach the range term can still score (Marrow House: about 385 cells in 0.1 ms, nothing allocated). Rebuilt only when the belief moves more than one cell (`:83`). |
| **BFS to a safe wall** | `Search/BfsField.cs:161`, `Search/SafeWallGoal.cs:27` | The nearest wall cell (26 neighbours, through floors) outside the aim cone. Used by Flee and Lurk. BFS because the nearest refuge is what matters, and it is exact and cheap (121 cells in 0.014 ms). |
| **Hill climbing** | `Positioning/HillClimber.cs:113` | 3 rings x 8 seeds at 3 to 12 m around the belief, plus 2 random restarts, plus the current target. Steepest ascent to a local maximum. Neighbours look past up to 2 wall cells, as a ghost passes walls. Scores are cached per search. Seeds already on a climbed trail are skipped. Optional simulated annealing. It scores 250-370 cells, against about 1,560 candidate cells (inside, not in a wall) in Marrow House by brute force. It matched brute force in 24/24 sandbox probes and in `HillClimberTests.Search_FromRingSeeds_FindsTheGlobalBest`. |
| **A\*** | `Search/AStarSearch.cs:79` | Over the 3D grid **through walls and floors** (26 neighbours). It never leaves the house, and there is no diagonal squeeze past an outside corner (`:185`). Edge cost = length x the average of the two cells' cost per metre, `1 + 3 watched + 2 light`, x0.7 inside walls (`Search/RouteCosts.cs:23`). Admissible heuristic: straight-line distance x the cheapest cost per metre (`:214`). Binary heap with decrease-key. **Time-sliced** (`Step(budget)`). A node cap gives a partial route to the nearest expanded cell. |
| **Smoothing** | `Search/PathSmoother.cs:31` | String pulling with an exact grid walk (Amanatides-Woo). A leg may not cut through a cell more exposed than the worst cell on the stretch it replaces, so the smoothed route never crosses the lit patch the A* went round. A 30-cell route becomes 9 to 13 waypoints. |

## 7. Edge cases

Each one is a fallback inside the same loop, not a scripted state.

| Case | Behaviour | Code | Test (`GhostEdgeCaseTests` unless stated) |
|---|---|---|---|
| No dark cell nearby | If the best cell's exposure is 0.7 or more, it lurks in the nearest wall cell (BFS) until somewhere drops below 0.6 (hysteresis) | `HideOrLurk :747` | `NoDarkCellNear_LurksInTheNearestWall_UntilTheLightsGo` |
| Player cornered | 3 or fewer open cells around the belief: wall-surface cells become candidates and count as fully dark, unseen and covered, so it waits *in the wall behind* them | `Locate :675`, `PositionScorer.cs:98`, `:123` | `PlayerCornered_WaitsInTheWallBehindThem`, `NotCornered_NeverPicksAWallCell` |
| Player leaves the house | It focuses on the inside cell nearest them (the door). Strike is vetoed, and it never follows outside. | `Locate :675`, `ModeSelector.cs:61` | `PlayerOutside_WaitsAtTheThreshold_AndNeverStrikes` |
| Belief lost | It goes home, then wanders to random floor cells around the anchor every 6 s ±40%, at 0.35x speed | `Wander :766`, `WanderCell :788` | `BeliefLost_GoesHome_ThenWandersSlowlyAroundIt` |
| Player gone from where it thought | **Negative evidence:** standing on the spot, not in a wall, with a clear line to it, nobody in view (not even half-recognised) for 1 s: it drops the belief | `NotHere :718` | `ReachedTheBelievedSpot_AndNobodyIsThere_GivesUp`, `SomeoneStillInView_IsNotRuledOut`, `FromInsideAWall_ItCannotRuleThePlayerOut` |
| A* node cap reached | It walks the partial route and re-plans after 0.4 s. With no route at all it drifts straight (it passes walls). | `StepRoute :387`, `DirectRoute :406` | `RouteNodeCapReached_TakesThePartialRoute_AndReplansSooner` |
| Spawned in a wall / outside | It plans from the nearest inside cell. Home snaps to the nearest floor cell. | `GhostCell :665`, `HomeCell :651` | `SpawnedInAWall_ComesOutToTheFloor`, `SpawnedOutsideTheHouse_PlansFromTheNearestInsideCell` |
| Aim flicking | Every sight of the aim renews the 1.5 s flee dwell. A refuge is kept while it is still out of the aim. | `Think :353`, `Flee :517` | `AimFlickingOnAndOff_DoesNotMakeItFlicker` |
| Heard (not seen) ice gun | The danger cone is fixed when the flight starts. Exposure in a wall is judged from the floor cell it would step onto. | `Flee :509`, `OpenExposure :708` | `HeardTheIceGun_HidesInOneWall_WithoutFlickering` |
| Several ghosts | Each ghost is given the others' target cells. A claimed cell is never picked, and nearby cells are penalised (crowd), so they spread and flank. | `Body/GhostBody.cs:269`, `PositionScorer.cs:106` | `TwoGhosts_NeverClaimTheSameCell` |
| Two ghosts in reach at once | A house-wide strike token: the first strike takes it for 1.5 s, before the next ghost thinks, even in the same frame. The others keep stalking. | `Body/GhostBody.cs:175`, `:257`, `TryStrike :480` | `GhostModeTests.OneStrikerAtATime_WaitsWhileAnotherGhostHasStruck` |
| Stale sighting | Strike closes in but cannot land without a fresh sighting | `TryStrike :480` | `GhostModeTests.Strike_ClosesInButDoesNotLand_OnAStaleSighting` |
| No house / not baked | It logs once and does nothing. No null errors. | `Body/GhostBody.cs:159` | - |

## 8. Performance

Target: **all ghosts together under 0.6 ms per frame.** Measured with named profiler markers
(`Brain/GhostProfiling.cs`), read every frame by `GhostProfilerHud` (F4) through `ProfilerRecorder`,
under a repeatable load (`GhostLoadTest`: the player walks a loop through Marrow House with the
ice gun and flashlight, facing the nearest ghost now and then).

| What | How |
|---|---|
| Thinking spread out | 5 Hz each, random phase, at most 6 thoughts a frame (shared `AiScheduler`) |
| Re-search only on change | A full hill climb only when the believed cell, the look (more than 20 degrees), the threat, the tactic or the cornered status changed, or 1 s has passed. Otherwise it re-scores its current target. About 75% of thoughts skip the search (`Brain/GhostBrain.cs:611`). |
| One heavy step per frame | A walk-field rebuild or a full search: at most one per frame across the house. A ghost with a target waits its turn (`:566`, `Body/GhostBody.cs:256`). |
| Shared route budget | 240 A* cells per frame for all ghosts together, at most 120 each. None in a frame that already did a heavy step (`Body/GhostBody.cs:290`). |
| Warm-up at load | The real thinking paths run once at load, with events off and nothing written to the shared map. The sighting paths run on a throwaway brain on an 11x11 scratch map (`:171`, `:254`), and the sight sensor on a dummy target (`Body/GhostBody.cs:187`). |
| No allocation | Stamp arrays, preallocated buffers, a zero-GC heap and bucket lists threaded through the cell arrays. Checked by `Search_AllocatesNothingAfterWarmUp` (A* and hill climbing); the walking field allocated 0 bytes over 50 builds on a Marrow House-sized grid. |

**Results** (Marrow House, 5 ghosts, 90 s, 25,600 frames):

| | Before Phase 9 | After |
|---|---|---|
| Average per frame | 0.040 ms | 0.005 ms |
| 99th percentile | 1.04 ms | 0.28 ms |
| Frames over 0.6 ms | 3.94% | 0.14% |
| First thought | 2.8-3.9 ms in play | at load |
| First sighting of the session | 2.45 ms | 0.67 ms (once) |

One full search costs 0.21-0.27 ms (250-370 cells at 0.7-0.9 µs each). The remaining rare spikes
did not reproduce in a 2000-frame recording (worst 0.32 ms). They are most likely editor
main-thread noise (garbage collection from editor tooling). The ghost code itself allocates nothing.

**Tried and rejected:** `RuntimeHelpers.PrepareMethod` to pre-compile methods. It "prepared" 382
methods in 2.4 ms, yet the first thought still took 7.8 ms (12.4 ms cold). It barely helps in
the editor's Mono, so running the real code paths at load replaced it.

## 9. Why this and not something else

| Alternative | Why not |
|---|---|
| Finite state machine | Transitions would be hand-written IF/ELSE ladders. Utility blends continuous evidence (confidence, distance, facing, exposure) and stays readable as the inputs grow. Hysteresis gives the stability an FSM gets from explicit states. |
| Behaviour tree | Good for ordered sequences (the zombies). The ghost's choice is a trade-off between several continuous things, not a priority ladder. |
| NavMesh pathfinding | Ghosts pass through walls, and what matters is **exposure**, not just distance. A* over our own 3D grid prices light and watched cells and can travel inside walls. |
| Brute-force scoring of every cell | About 1,560 cells x 0.8 µs (1.2 ms) every thought for every ghost, twice the whole budget. Ring-seeded hill climbing finds the same best cell (24/24 probes) for a quarter of the work or less, and the change-driven re-search removes most of the rest. |
| Fixed hiding spots / waypoints | That would be a fixed path (breaking a lecturer's rule) and predictable. The score is computed from where the player is looking now. |
| GOAP | Biscuit already uses it. The ghost has no multi-step plans, only a continuous "best place and stance". |

## 10. Numbers worth defending

| Number | Value | Why |
|---|---|---|
| Cell size | 1 m | About a body width. Marrow House is 46x42x2 = 3864 cells, small enough to search fully. |
| Watched fade | 8 s | About how long a glance stays "fresh". It matches the belief tau (8 s). |
| Stalk range | 4-8 m (Mara 3-6) | Out of easy reach, close enough to strike within a second at 1.4x speed. |
| Behind / dark / unseen | 1.0 / 1.0 / 1.2 | Unseen counts slightly more, because being looked at is what gives a ghost away. |
| Threat | 3.0 | The biggest single term: a cell in the aim loses 3 points, as much as being in front, lit and watched all at once (1.0 + 1.0 + 1.2 = 3.2 lost). An aimed-at cell only wins if every alternative is far worse, and then Flee takes over anyway. |
| Range | 1.5 | The strongest positive term, so the ghost keeps the stalking distance. |
| Strike lands | within 1.6 m, sighting at most 0.5 s old | The eyes must confirm: never a strike on memory alone. |
| Flee dwell | 1.5 s, renewed by every aim | Stops Flee/Stalk flicker while the aim wavers. |
| Switch margins | modes 0.1, positions 0.15 | Small enough to react, big enough to stop near-ties flickering. |
| Lurk | in at exposure 0.7, out at 0.6 | Hysteresis on the "no dark cell" fallback. |
| Not here | 1.5 m, 1 s | Longer than a point-blank recognition in the light; short enough not to look dumb. |
| Re-search | 20 degrees / 1 s | Below 20 degrees the best cell barely moves; 1 s catches the watched layer fading. |
| A* costs | watched x3, light x2, walls x0.7 | Being seen is worse than being lit. Walls are a ghost's corridor. |
| Route budget | 240 cells per frame (120 per ghost) | A long Marrow House route is 180-440 cells, so it finishes in 1-4 frames. |
| Walking-field move costs | straight 5, diagonal 7 | 7/5 = 1.4, within 1% of the true 1.41, with small whole numbers, so only 8 buckets are needed. |
| Strike gap | 1.5 s house-wide | One hit at a time reads as a haunting, not a pile-on; it is shorter than any cooldown (4-6 s), so it never blocks a ghost for long. |

## 11. Limitations and proposals

- **A servant 6.4 m behind a player with their back turned does not strike** (Strike 0.52
  against Stalk 0.50 + 0.10). A patient servant needs the player within about 6 m. Mara would
  strike. Left as tuning; a possible fix is to creep closer once patience runs out.
- **Fixed after the team review:** two ghosts could strike in the same tick (now a house-wide
  strike token, section 7), and the walking field counted a diagonal as 1 m, up to 29% short (now
  Dial's bucket queue with diagonal 1.4, section 6).
- **For the team (shared code, not edited):** a player damage API (for example
  `IDamageable.Drain`) for `Struck`; an ice-gun raycast calling `IIceable` (move `IIceable` into
  shared combat code). PF_Ghost has a trigger capsule and a kinematic Rigidbody, so the ice-gun ray
  must use `QueryTriggerInteraction.Collide`; pistol rays should ignore triggers (bullets pass
  through ghosts).
- A ghost inside a wall still hears: for a listener off the NavMesh, `NoiseSystem.PathDistance`
  falls back to the straight-line distance x 1.5 (`AI/Senses/NoiseSystem.cs:85`).
- Combat is stubbed in `Debugging/GhostCombatStub.cs` until the weapons exist.

## 12. Testing it in the game

Open `Scenes/Sandbox_GhostAI.unity` (24x20 m, two storeys, four lamps, a servant and Mara) and
press Play.

| Key | What |
|---|---|
| WASD, mouse | Walk (the debug player) |
| Q | Cycle weapon: none, pistol, ice gun |
| F | Flashlight |
| Click / I | With the ice gun: fire (stub) |
| F2 | Heatmap on the floor plus the panel |
| F3 | Cycle view: exposure, light, watched, cells, walk distance, score (seeds, trails, peaks, best, breakdown) |
| C | Crouch (halves how fast the sight meter fills) |
| F4 | Profiler HUD |

Things to try: let a ghost see you, then turn your back (it stalks, then strikes); face it (it
moves behind you within about 0.1 s); aim the ice gun at it (it flees into a wall); shoot it
(frozen for 3 s, then it flees); walk away out of its view (it gives up at your old spot); leave
by the door (it waits inside).

---

# Viva cheat sheet

Jump targets are relative to `Assets/_Hollowmere/Scripts/Runtime/AI/Ghost/`.

| Question | Answer in one breath | Show |
|---|---|---|
| What is the architecture? | Utility AI picks the mode; a weighted-sum score over an influence map picks the place; hill climbing optimises it; A* routes there through walls. Brain in plain C#, body in Unity. | `Brain/GhostBrain.cs:326` |
| Where does it sense? | Only through `See`, `Hear`, `Told` and `Freeze`. The body calls the shared SightSensor at 5 Hz. Noises come from the shared NoiseSystem. | `Brain/GhostBrain.cs:231`, `:284`, `Body/GhostBody.cs:225` |
| Does it ever read the player's true position? | No. The look point is placed on the belief (`Body/GhostBody.cs:318`). A strike needs a sighting under 0.5 s old. | `Brain/GhostBrain.cs:480` |
| No "entered a radius" trigger? | Nothing tests distance to the real player. Modes come from beliefs; the sight meter fills with light, distance and posture. | `Brain/ModeSelector.cs:53` |
| No fixed path? | Targets come from the live score. Idle wander picks random cells. A* re-plans every second. | `Brain/GhostBrain.cs:788`, `:540` |
| Why utility over an FSM or BT? | It blends continuous evidence; a product of curves gives vetoes for free; hysteresis gives stability. | `Brain/ModeSelector.cs:53-69` |
| Explain the Strike formula | Product: confidence x ready x inside x Near x facing-away x not-aimed x tactic x patience. Any zero vetoes it. | `Brain/ModeSelector.cs:61` |
| Why product for modes but sum for positions? | Modes have hard vetoes (aimed means no strike). Positions trade off (a little light is fine if perfectly behind). | `PositionScorer.cs:134` |
| Score two cells by hand | Use the worked example in section 5: 5.37 against 2.52; the aim flips it. | `Positioning/PositionScorer.cs:116` |
| How is "watched" stored? | As the last-seen time per cell; decay is computed when read, so nothing updates per frame. | `Map/InfluenceMap.cs:48` |
| Why BFS for range? | Walking distance, not straight line: through a wall is not "close". | `Search/BfsField.cs:99` |
| Plain BFS assumes equal move costs. Diagonals? | Right, so it is Dial's algorithm: costs 5 and 7 (diagonal 1.4 m), a ring of 8 buckets instead of a FIFO queue. Exact like Dijkstra, but O(cells) with no heap, because there are only two move costs. Before the fix, distances were up to 29% short. | `Search/BfsField.cs:99`, `:234`, `BfsWalkingFieldTests.ADiagonalMove_CountsItsRealLength` |
| Can two ghosts hit at once? | No. A house-wide strike token is taken for 1.5 s by the first strike, before the next ghost thinks, even in the same frame; the same pattern as the one-heavy-search-per-frame token. | `Body/GhostBody.cs:175`, `:257`, `GhostModeTests.OneStrikerAtATime_*` |
| Why hill climbing and not brute force? | A quarter of the cells or less (brute force in Marrow House would be about 1.2 ms per thought), same answer in 24/24 probes; random restarts and ring seeds avoid local maxima; annealing is available. | `Positioning/HillClimber.cs:113` |
| How do you avoid local maxima? | 24 ring seeds + 2 random restarts + the current target; skip seeds on climbed trails; optional annealing. | `HillClimber.cs:113`, `:209` |
| Is the A* heuristic admissible? | Straight-line distance x the cheapest possible cost per metre (0.7 in walls), never above the true cost. Tested against Dijkstra. | `Search/AStarSearch.cs:214`, `AStarTests.Cost_MatchesDijkstra_*` |
| How does A* not drop frames? | `Step(budget)` time slicing; 240 cells per frame shared; a node cap gives a partial route. | `Search/AStarSearch.cs:110`, `Body/GhostBody.cs:284` |
| What does smoothing guarantee? | It never crosses a cell more exposed than the stretch it replaces, and never leaves the house. | `Search/PathSmoother.cs:31` |
| Weapon tactics? | Ice gun known (seen or heard): 6-10 m, more cover and unseen, strike x0.4. Otherwise bold. | `Brain/GhostBrain.cs:462` |
| Mara vs servants? | Same brain; the profile ScriptableObject changes aggression, patience, cooldown, range and leash. | `Brain/ModeTuning.cs:82`, `:123` |
| Several ghosts? | Claims: other ghosts' target cells are excluded, and the crowd term pushes them apart, so they flank. | `Body/GhostBody.cs:269`, `PositionScorer.cs:106` |
| What if no dark cell exists? | It lurks in the nearest wall cell (BFS), hysteresis 0.7/0.6. | `Brain/GhostBrain.cs:747` |
| Player cornered? | Wall-surface cells become candidates; it waits inside the wall behind them. | `Brain/GhostBrain.cs:675`, `PositionScorer.cs:98` |
| Player leaves the house? | It waits at the inside cell nearest them; Strike x0. | `Brain/GhostBrain.cs:675`, `ModeSelector.cs:61` |
| Player slips away? | Negative evidence: standing on the spot with a clear view and nobody there for 1 s, so it forgets. | `Brain/GhostBrain.cs:718` |
| Ice gun aimed, then wavering? | Flee dwell renewed by every aim; the refuge is kept while safe. | `Brain/GhostBrain.cs:353`, `:517` |
| Frame budget? | 0.005 ms average, p99 0.28 ms, 0.14% over 0.6 ms with 5 ghosts in Marrow House. F4 shows it live. | `Debugging/GhostProfilerHud.cs` |
| What did profiling change? | Change-driven re-search (-75% searches), one heavy step per frame, shared route budget, warm-up at load. | `Brain/GhostBrain.cs:611`, `:566`, `:171` |
| What did not work? | `PrepareMethod`: 382 methods in 2.4 ms, first thought still 7.8 ms, so it was replaced by running the real paths at load. | section 8 |
| What would you improve? | Servant creeping in when its patience runs out; real damage and ice-gun APIs from the team. | section 11 |
| How is it tested? | 99 EditMode tests on the plain-C# brain and searches, plus Play-mode runs in the sandbox and in Marrow House with a stand-in player. | `Scripts/Tests/EditMode/Ghost/` |

**Test files:** `AStarTests` (11), `BfsFleeTests` (6), `BfsWalkingFieldTests` (9), `GhostBrainTests` (15),
`GhostEdgeCaseTests` (14), `GhostModeTests` (9), `HillClimberTests` (9), `ModeSelectorTests` (12),
`PathSmootherTests` (4), `PositionScorerTests` (10).
