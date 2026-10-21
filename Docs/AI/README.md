# Hollowmere: AI Plan (SE3062 Intelligent Systems)

The four graded agents, the system they share, and what was built. Read sections 1 to 4 first;
then only the section for your own agent.

For the story see [Docs/Story](../Story/README.md). For running the game see
[Docs/Setup](../Setup/README.md).

**Deadline: 21 October 2026.** All four agents are built and placed in the game:

| Agent | Owner | Code | Prefab, where | Detailed notes |
|---|---|---|---|---|
| Zombies | Systems Engineer | `AI/Zombie/` | `PF_Zombie`, twelve in Zone 1 | Section 5 |
| Ghosts | Agent Controller | `AI/Ghost/` | `PF_Ghost`, four in Zone 2 (three servants, Mara) | [Ghost.md](Ghost.md) |
| Biscuit | Core Developer | `AI/Biscuit/` | `PF_Biscuit`, in the World scene | [Biscuit.md](Biscuit.md) |
| Director | World Builder | `AI/Director/` | `_Director`, in the World scene | Section 8 |

Code paths are under `Assets/_Hollowmere/Scripts/Runtime/`.

**How the built code fits together:** [Senses_and_Director.svg](Senses_and_Director.svg) (also as
[PNG](Senses_and_Director.png)) shows every file, method and connection in the senses layer, the
scheduler and the Director, and how each agent plugs in. Open the SVG in a browser to zoom.

---

## 1. What the assignment marks

| Category | Marks | What gets the top band |
|---|---|---|
| Group: integration and stability | 20 | All four agents run together, no frame drops, no null errors, AI logic separate from visuals |
| Individual: effort and code quality | 15 | Small, regular commits with clear messages, on branches; clean code |
| Individual: creative design | 15 | A unique hook, such as hunting by sound or changing tactics with the player's weapon |
| Individual: implementation | 25 | The architecture is real, handles edge cases, and is clearly more than IF/ELSE |
| Individual: viva | 25 | You can say why your architecture beats the alternatives and defend every number |

Three rules from the lecturer:

1. With all agents running, the frame rate must not drop.
2. No enemy may simply run at the player along a fixed path.
3. No enemy may be triggered just because the player walked into a circle. Enemies react to
   what they sense, for example a loud sound, and go to where the sound came from.

---

## 2. The four agents

Each agent uses a different architecture, so each viva has its own story.

| Agent | Where | Architecture | The hook |
|---|---|---|---|
| **Zombies** | Zone 1, the village | Behaviour tree with memory | Hunt by sound that travels along walkable routes; investigate; alert each other |
| **Ghosts** | Zone 2, the mansion | Utility scoring over an influence map | Come from where the player is not looking; change tactics when the ice gun is out |
| **Biscuit** | Follows the player | Goal-oriented planner (A* over actions) | Plans its own chains of actions: scout, warn, fetch, lead, lure |
| **Director** | Whole game | Utility AI with a stress model | Paces the game: decides which of the dead wake, where and when |

Two more characters exist in the story but are **not graded agents**:

- **The creature** (Zone 3 tunnel): built. `PF_Creature` runs the zombie brain (`ZombieBody`)
  with `SO_CreatureConfig`: hearing acuity 1.6 instead of 1.0, and a groan of 30 m instead of
  20 m. Its sight is the same as a zombie's. The shut laboratory door's NavMesh obstacle keeps it
  in the tunnel.
- **Dr Marrow** (Zone 4): not built. `PF_Marrow` is placed with its model only and no AI. The
  plan was the ghost's position scoring on the NavMesh, with a gun.

---

## 3. The shared senses layer

Every agent senses the world through this layer. It is what makes rule 3 true: nothing in the
game checks "is the player inside my radius".

### 3.1 Noise

Anything loud calls one function:

```csharp
NoiseSystem.Emit(position, loudness, kind, source);
```

`loudness` is the distance in metres at which a normal ear can still hear it.

| Noise | Loudness (m) | Made by |
|---|---|---|
| Gunshot | 45 | `WeaponSystem` (pistol); test key G |
| Creature groan | 30 | `ZombieBody` with `SO_CreatureConfig` |
| Biscuit's bark | 25 | `BiscuitAgent` |
| Zombie groan (alert) | 20 | `ZombieBody`, once each time it starts a chase |
| Staged noise | 20 | `GameDirector` |
| Bottle landing | 18 | `Throwable` |
| Ice gun | 15 | `WeaponSystem` |
| Running | 12 | `PlayerNoiseEmitter`, one per step |
| Barricade scraping | 12 | `Barricade` |
| Door, hatch, gate | 10 | `Door`, `ZoneExit`; test key H |
| Wading through water | 9 | `PlayerNoiseEmitter` |
| Walking | 5 | `PlayerNoiseEmitter` |
| Crouch-walking | 2 | `PlayerNoiseEmitter` (walking x 0.4) |

The values live in `Senses/Noise.cs` (`Loudness`) and in each emitter's Inspector.

**How far a noise carries.** A listener hears a noise when

```
pathDistance(noise, listener) <= loudness x listener.hearingAcuity
```

`pathDistance` is the length of the walking route between them on the NavMesh (Unity's A*),
not the straight line. A gunshot on the other side of a wall has to travel round through the
door, so walls and floors muffle sound without any audio physics.

**Cheap first.** The straight-line distance is never longer than the path. So if the straight
line is already too far, the listener is skipped without a path search. Most listeners are
rejected this way.

**Which NavMesh.** Sound always travels on the Humanoid NavMesh. If the noise or the listener is
off the NavMesh (a ghost inside a wall), there is no route, so the straight line x 1.5 is used
instead (`NoiseSystem.OffMeshMuffle`).

`NoiseSystem.Emitted` is raised after every noise. The Director uses it to learn how loud the
player is, and the debug probes use it.

Listeners implement:

```csharp
public interface INoiseListener
{
    Vector3 EarPosition { get; }
    float HearingAcuity { get; }   // 1 = normal, 1.6 = the creature, 0 = deaf
    void OnHeard(in Noise noise, float pathDistance);
}
```

Noise is **pushed** to listeners when it happens. Nobody polls for it.

### 3.2 Sight

An agent sees the player only if all of these hold:

- within its view range,
- within its view cone,
- a line-of-sight ray is not blocked.

Seeing is not instant. A detection meter fills at

```
rate = baseRate x lightFactor x distanceFactor x postureFactor
```

| Factor | Values |
|---|---|
| lightFactor | flashlight on 1.0, near a lit lamp 0.7, dark 0.3 |
| distanceFactor | 1 at point-blank, falling linearly to 0 at view range |
| postureFactor | standing 1.0, crouching 0.5 |

The player is "seen" when the meter reaches 1. Out of sight the meter drains. This makes the
dark mansion and tunnel matter: the flashlight helps the player see and helps the enemy see the
player.

Code: `Senses/SightSensor.cs`. Lamps count as "lit" only if they carry an `IlluminationSource`
(`Senses/Illumination.cs`). Sight runs at each agent's think rate, not every frame (zombies 4
times a second, ghosts 5).

### 3.3 Memory

Agents never read the player's true position. Each keeps a **belief**:

```csharp
struct Belief { Vector3 position; float confidence; float time; BeliefSource source; }
```

Confidence fades: `confidence = c0 x e^(-t / tau)`. Seen starts at 1.0, heard at 0.6, told by
another agent at 0.4. When confidence falls below 0.1 the agent gives up.

Code: `Senses/Belief.cs`, `Senses/AgentMemory.cs` (keeps the single most convincing belief; tau
8 s by default). The ghosts use `AgentMemory`; the zombies keep their own `ZombieMemory` with the
same rules and tau 12 s; Biscuit's `BiscuitMemory` remembers each enemy separately.

### 3.4 Player state

The player (Systems Engineer) exposes one interface. `PlayerController` on `PF_Player`
implements it and announces itself through `PlayerRegistry`, so no agent searches the scene for
the player. (`DebugPlayerState` implements it for the old debug walker, with test keys.)

```csharp
public interface IPlayerState
{
    Vector3 Position { get; }
    Vector3 LookDirection { get; }
    float Health01 { get; }
    float Ammo01 { get; }
    WeaponKind HeldWeapon { get; }     // None, Pistol, IceGun
    bool FlashlightOn { get; }
    bool IsCrouching { get; }
}
```

Only the Director and Biscuit read this directly (the Director is meant to know everything;
Biscuit is at the player's side). Enemies use senses and beliefs only.

---

## 4. Rules for every agent

### 4.1 Performance

Budget: **all AI together under 2 ms per frame** on the RTX 2050 laptop.

- **Think slowly.** Brains tick through `AiScheduler` (`Scheduling/AiScheduler.cs`): zombies 8
  times a second, Biscuit 8, ghosts 5, the Director 2. Each thinker starts at a random point in
  its interval, so the thoughts spread across frames, and at most 6 run in one frame
  (`MaxThinksPerFrame`). The scheduler measures its own time per frame. Movement and animation
  still run every frame.
- **Search budgets.** There is no shared path queue. Each agent limits its own searches: the
  ghosts allow one heavy search per frame for the whole house and share 240 A* nodes a frame;
  the Director predicts the player's route every 3 s.
- **No per-frame searches.** No `FindObjectOfType`, no `GetComponent`, no allocation inside a tick.
- **Only the loaded zones think.** Agents in an unloaded zone do not exist.
- **Awake limit:** the Director wakes no more than 12 enemies at once (`GameDirector.maxAwake`).

### 4.2 Logic separate from visuals

Each agent has two parts:

- **Brain:** plain C# classes. No `MonoBehaviour`, no animator, no mesh. Takes senses in,
  gives a decision out. Can be unit-tested.
- **Body:** one thin `MonoBehaviour` that feeds the brain, moves the agent, and tells the
  animation code what the brain decided through events.

| Agent | Brain | Body | Events for animation and effects |
|---|---|---|---|
| Zombie | `ZombieBrain` | `ZombieBody` | `OnStateChanged`, `OnAttack` |
| Ghost | `GhostBrain` | `GhostBody` | `ModeChanged`, `Spotted`, `LostSight`, `Struck`, `Frozen`, `Freed` |
| Biscuit | `BiscuitBrain` | `BiscuitAgent` | the action it just started, for the Animator |
| Director | `DirectorBrain` | `GameDirector` | none (it has no body to animate) |

Walking agents move with `Agents/AgentLocomotion.cs` (turning, speed and foot matching along the
NavMesh path); ghosts float with `Agents/GhostMovement.cs`.

The brains of the ghosts, Biscuit and the Director are unit-tested in EditMode (Window > General >
Test Runner): 99 ghost tests, 32 Biscuit tests.

### 4.3 Debug view

Press **F2** in Play mode (`Debugging/AiDebug.cs`): the Director's panel and stress graph,
Biscuit's facts, goals and plan (with rings and routes drawn in the world), and a label over
each zombie with its active tree node (its belief is drawn as a gizmo in the Scene view). Each ghost shows a label with its mode, sight
meter, belief and utilities while **Show Label** is ticked on `GhostBody`; the ghost sandbox also
has F2 to F4 views of the influence map and frame times (see [Ghost.md](Ghost.md)).

### 4.4 Folders and branches

```
Assets/_Hollowmere/Scripts/Runtime/AI/
├── Senses/        shared: noise, sight, beliefs, illumination, player state
├── Scheduling/    shared: AiScheduler
├── Debugging/     shared: F2 toggle, labels, test probes and stand-ins (temporary)
├── Zombie/        Brain/ (behaviour tree, memory), ZombieBody, ZombieConfig
├── Ghost/         Map/, Search/, Positioning/, Brain/, Body/, Movement/, Profiles/, Debugging/
├── Biscuit/       Brain/ (planner, facts, goals, Actions/), BiscuitAgent, ScentSource
└── Director/      GameDirector, DirectorBrain, StressModel, TensionCurve, RoutePredictor
```

Shared combat interfaces live outside the AI folder, in `Scripts/Runtime/Combat/`:
`IDamageable` (the player and zombies take damage) and `IIceable` (the ice gun freezes ghosts and
zombies).

- Work only in your agent's folder and your sandbox scene. Deliver a prefab to `Prefabs/Agents/`.
- Branches: `feature/ai-zombie-hearing`, `feature/ai-ghost-scoring`, and so on. One small step
  per branch, merged into `dev`.
- **Commit every working day.** The marks for effort look at the commit history.

---

## 5. Zombies: behaviour tree with memory

**Problem it solves.** About 40 drowned villagers stand or lie dormant in the flooded village.
The player chooses between speed (running and wading are loud) and silence.

**Tree** (checked top to bottom; the first branch that can run wins):

```
Selector
├── Attack          target within reach and seen
├── Chase           belief confidence >= 0.8 (seen recently)
├── Investigate     any belief: walk to it, then search around it
│   ├── MoveTo(belief.position)
│   └── Search: visit 3 points on a widening ring, look around at each
├── RespondToGroan  another zombie called: go toward its belief
└── Dormant         stand still, listen
```

**What makes it more than chase-the-player:**

- **Sound first.** A dormant zombie has a narrow view cone and wakes mainly through noise.
- **Last known position.** It goes where the sound *was*. A player who fires and moves away
  quietly leaves the zombies searching the wrong place.
- **Groans.** On first seeing the player a zombie groans: a noise of loudness 20 that carries
  its belief. Neighbours come with lower confidence. A horde forms by itself; no group code.
- **Water.** The NavMesh already costs flooded ground 3x, so zombies take the boardwalk unless
  the dry route is three times longer.

**Edge cases handled:** belief position is unreachable (go to the nearest NavMesh point within
5 m, then 15 m); two noises at once (keep the higher confidence); player leaves the zone (beliefs
cleared, back to dormant); stuck for more than 3 s (`ZombieBody` gives up the move).

**As built.** `ZombieBrain` (plain C#) owns the tree: a `BtSelector` over `AttackNode`,
`ChaseNode`, `InvestigateNode`, `RespondToGroanNode` and `DormantNode`, ticked 8 times a second
through the scheduler. `ZombieMemory` hears through `NoiseSystem` and keeps the belief and a
separate groan belief. `ZombieBody` drives the `NavMeshAgent` (with `AgentLocomotion`), groans
once each time a chase starts, deals 25 damage per hit to the player (1.8 m reach, 1.5 s
cooldown), takes pistol damage (100 health), freezes when the ice gun hits it, and lets the
Director wake it (`IDirectable`). Every number is in `SO_ZombieConfig`: view 14 m and 70°,
detection rate 0.35, tau 12 s, chase at confidence 0.8, ring search of 3 points from 3 m in
steps of 2 m.

**Viva points.**

- Why a behaviour tree, not a state machine: adding "respond to groan" was one new branch, not
  new transitions from every state.
- Why path distance for sound: straight-line hearing lets zombies hear through the hillside.
- Why confidence decays exponentially, and what `tau` does to how long a search lasts.

**Fallback if time runs out:** drop the ring search; investigate goes to the point and waits.

---

## 6. Ghosts: utility scoring over an influence map

**Problem it solves.** Ghosts pass through walls, so pathfinding cannot make them interesting.
The question is *where to appear*. Bullets do nothing; only the ice gun frees them.

**Influence map.** A grid of 1 m cells over each mansion floor holds two values:

- `light`: how lit the cell is (from the lamps; fixed, computed once).
- `watched`: set to 1 for cells in the player's view, fading to 0 over about 8 seconds.

**Choosing a position.** Every half second the ghost scores about 24 candidate points on rings
3 to 12 m from its belief of the player:

```
score = wBehind  x behind(player look direction)
      + wDark    x (1 - light)
      + wUnseen  x (1 - watched)
      + wCover   x hiddenByWall
      + wRange   x rangeFit(distance)
      - wThreat  x inIceGunAim
```

It drifts to the best point, straight through walls.

**Tactics change with the player's weapon.** The weights come from a table:

| Player holds | Ghost behaviour | Weight change |
|---|---|---|
| Pistol or nothing | Bold: drifts in from the front, drains the player | `wBehind` low, `wThreat` 0 |
| Ice gun | Careful: waits inside a wall, strikes from behind | `wBehind` high, `wCover` high, `wThreat` high |
| Ice gun, aimed at it | Flees through the nearest wall, comes back elsewhere | `wThreat` dominates |

**Modes** (also chosen by utility, not fixed transitions): Stalk, Ambush (wait inside a wall),
Strike, Flee.

**Mara** is the same brain with higher aggression. She attacks everyone except her husband.

**Edge cases:** no dark cell nearby (take the least watched); player backs into a corner (no
"behind" exists, so range and cover decide); two ghosts choose the same point (the second
takes the next best).

**Viva points.**

- Why utility, not a behaviour tree: the choice is a blend of several continuous things, not a
  yes/no ladder.
- Why these weights: show the score of two candidate points by hand.
- What the influence map costs: one small grid per floor, updated only in the player's view.

**Fallback:** drop the grid; score candidates from `behind`, `cover` and `threat` only.

**As built** (not the fallback; full write-up, numbers and viva cheat sheet in
[Ghost.md](Ghost.md)):

- **Influence map:** a 3D grid of 1 m cells per storey, baked from the Zone 2
  `GhostHouseVolume` (46 x 42 m, two storeys). Each cell holds light (from the lamps, with line of
  sight), watched (the player's view cone, fading over about 8 s), the flashlight cone, and
  whether it is inside a wall.
- **Search core:** a walking-distance field from the believed player cell (Dial's algorithm,
  diagonals cost 1.4), a breadth-first search for the nearest safe cell inside a wall, hill
  climbing over the position score (24 ring seeds, 2 random restarts), and time-sliced A*
  through walls with exposure costs, then path smoothing.
- **Modes by utility:** Stalk, Ambush, Strike, Flee, each a product of response curves, with
  hysteresis. Frozen while the ice gun holds it, and Idle/Wander when it has lost the player.
- **The strike** lands only if the ghost saw the player in the last 0.5 s and is within reach:
  35 damage. One ghost in the house may strike at a time (1.5 s gap).
- **Ice gun:** freezes the ghost; it flees into a wall when it thaws. Bullets pass through.
- **Profiles:** `SO_GhostProfile_Servant` (stays near its haunt, leash 6 m) and
  `SO_GhostProfile_Mara` (bolder, ranges the house, leash 40 m).
- **Many ghosts:** each claims its target cell so two never choose the same place.
- **Cost:** in Zone 2 with five ghosts, 0.005 ms per frame on average and 0.275 ms at the 99th
  percentile.

---

## 7. Biscuit: goal-oriented action planning

**Problem it solves.** A companion that only follows is decoration. Biscuit should decide by
itself what would help most right now, and work out the steps.

**World state** (facts Biscuit believes, true or false):

`NearPlayer`, `EnemyNearby`, `PlayerWarned`, `PlayerLowOnAmmo`, `KnowsAmmoSpot`, `CarryingAmmo`,
`EvidenceNearby`, `PlayerAtEvidence`, `InDanger`.

**Goals**, with a priority that changes with the situation:

| Goal | Wants | Priority rises when |
|---|---|---|
| Stay safe | `InDanger = false` | an enemy is very close to Biscuit |
| Warn the player | `PlayerWarned = true` | an enemy is near and the player has not been warned |
| Resupply | `PlayerLowOnAmmo = false` | the player's ammo is low |
| Show evidence | `PlayerAtEvidence = true` | a page is near and it is quiet |
| Stay close | `NearPlayer = true` | always, low priority |

**Actions**, each with what it needs, what it changes, and a cost:

| Action | Needs | Changes | Cost |
|---|---|---|---|
| Follow | - | `NearPlayer` | 1 |
| Sniff | `NearPlayer` | finds enemies and pickups within 25 m of walking distance | 1 |
| Growl (quiet warning) | `EnemyNearby`, `NearPlayer` | `PlayerWarned` | 1 |
| Bark (loud warning) | `EnemyNearby` | `PlayerWarned`, **emits noise 25** | 3 |
| GoToAmmo | `KnowsAmmoSpot` | at the pickup | 2 |
| CarryToPlayer | at the pickup | `CarryingAmmo`, then `PlayerLowOnAmmo = false` | 2 |
| LeadToEvidence | `EvidenceNearby`, not `EnemyNearby` | `PlayerAtEvidence` | 2 |
| Hide | `InDanger` | `InDanger = false` | 1 |
| Lure | `EnemyNearby`, player in trouble | runs off and barks; zombies follow the bark | 5 |

**The planner.** A* search where a node is a world state, an edge is an action, and the cost is
the sum of action costs. The heuristic is the number of goal facts not yet true. It replans
when a fact changes or an action fails.

**Why this is interesting to play.** The bark is a real noise in the senses layer. Warning the
player loudly can wake the dead; luring uses the same rule on purpose. Nobody scripts these
outcomes: they come from the planner and the noise system together.

**Edge cases:** the pickup is taken before Biscuit arrives (replan); the path is blocked by a
locked door (action fails, replan without it); the player runs far ahead (Stay close wins).

**Viva points.**

- Why a planner, not a behaviour tree: new actions combine with old ones without writing the
  combinations.
- The heuristic: when it is admissible, and what happens to the search if it is not.
- Plan depth and cost: at most 9 actions and 9 facts, so the search is tiny.

**Fallback:** five actions only: Follow, Sniff, Growl, Bark, Hide.

**As built** (full write-up in [Biscuit.md](Biscuit.md)). The plan above grew:

- **12 facts** packed into one `ushort` (`Brain/Fact.cs`), adding `PlayerInTrouble`,
  `AreaScouted`, `GhostNearby`, `KnowsAmmo` and `KnowsEvidence`.
- **7 goals:** Stay safe, Protect the player, Warn the player, Resupply, Show evidence, Scout,
  Stay close.
- **10 actions:** Follow, Sniff, Growl, Whine (the warning for ghosts: Biscuit backs to the far
  side of the player), Bark, Hide, Lure, Fetch ammo, Bring ammo, Lead to evidence.
- **Bark cost counts who would hear it:** the bark is a real 25 m noise, so its cost rises with
  the number of dead in earshot.
- **Smell:** enemies carry a `ScentSource` (Dead, Ghost or Creature); pages and ammunition are
  read from the zones' own markers (`PointRegistry`).
- `BiscuitAgent` is the only MonoBehaviour. It thinks 8 times a second and runs on the Dog
  NavMesh, which is linked across the zones so Biscuit can follow the player everywhere.

---

## 8. The Director: utility AI with a stress model

**Problem it solves.** The dead are finite: about 40 in the village, the servants in the
house, one creature. Waking them all at once is a wall; waking none is a walk. The Director
decides who wakes, where and when, so the tension rises and falls.

**Stress.** One number from 0 to 1, updated twice a second:

```
stress += damageTaken x kDamage
        + enemiesChasing x kChase x dt
        + (1 - health) x kHealth x dt
        + (1 - ammo) x kAmmo x dt
stress -= kCalm x dt            (only while nothing is chasing)
```

**Actions**, each scored from 0 to 1 by multiplying response curves:

| Action | Scores high when |
|---|---|
| Wake the dead (how many, where) | stress is low, it has been quiet for a while, the zone still has dormant dead |
| Hold back | stress is high or the player just escaped a fight |
| Stage a noise (a shutter bangs, a boat knocks) | it is quiet and dormant dead are near but unaware |
| Leave supplies ahead | stress is high and health or ammo is low |
| Send a ghost closer | the player has stood still in the mansion too long |

The highest score wins, with a short cooldown per action.

**Choosing who wakes.** Each dormant agent's spot is scored:

- out of the player's sight,
- 15 to 35 m away by walking distance,
- ahead of the player, toward the zone's exit.

The Director does not move enemies or tell them where the player is. It wakes one, or makes a
noise near it. The enemy's own senses do the rest.

**Budget.** Each zone has a fixed roster placed on the spawn markers. The Director can never
exceed it, and never exceeds the awake limits in section 4.1.

**Viva points.**

- Why utility, not a timer or a wave table: the same code gives a careful player a slow build
  and a reckless player an early peak.
- Why multiply the curves: any single "no" (no dead left, player in a fight) vetoes the action.
- Show the stress graph live with the F2 view.

**Fallback:** two actions only: Wake the dead, Hold back.

**As built.** `GameDirector` (in the World scene, `_Director`) thinks twice a second. Each tick it:

1. measures stress (`StressModel`) and learns the player's style from their noise
   (`PlayerProfile`: a moving average of loudness per second, giving a quietness from 0 to 1);
2. moves the target tension along its cycle (`TensionCurve`);
3. predicts the player's route to the zone exit (`RoutePredictor`, A* on the NavMesh, every 3 s);
4. scores every dormant enemy within 50 m: hidden from the player, 15 to 35 m away by walking
   distance, ahead on the route;
5. lets `DirectorBrain` pick an action, and carries it out.

The stress formula as coded:

```
rise   = 1.5 x healthLost
       + (0.04 x enemiesNear + 0.03 x (1 - health) + 0.015 x (1 - ammo)) x dt
fall   = 0.05 x dt, only while no awake enemy is within 25 m
stress = clamp01(stress + rise - fall)
```

The tension target cycles: **BuildUp** (0.2 rising to 0.8 over 90 s), **Peak** (when stress
reaches 0.75; held for 12 s), **Relax** (0.15 for at least 35 s, until the player calms down).

Three actions are built, each a product of terms so any single "no" vetoes it:

```
need      = clamp01((target - stress) / 0.4)
rested    = 0 for 5 s after the last event, rising to 1 at 15 s
WakeDead   = need x rested x available x room x best x (1 - 0.5 x quietness)
StageNoise = need x rested x available x best x (0.3 + 0.7 x quietness)
HoldBack   = 0.1 + 0.9 x clamp01((stress - target) / 0.3)
```

It wakes 1 to 3 at once (more when stress is far below the target) and sends them toward a
point 10 m ahead of the player on the route. A staged noise is a 20 m noise near a dormant enemy.
A quiet player therefore gets staged noises; a loud one gets the dead woken directly.

**Not built:** *Leave supplies ahead* and *Send a ghost closer*. Ghosts implement `IDirectable`,
so the Director can wake them like any other enemy.

---

## 9. Build order (as planned)

| Dates | Shared work | Each agent |
|---|---|---|
| 12 to 13 Oct | Senses layer, scheduler, debug walker as `IPlayerState`, F2 debug view | Brain skeleton and a body prefab in your sandbox scene |
| 14 to 16 Oct | Noise from the real player and weapons, as they arrive | Core behaviour working against the debug walker |
| 17 Oct | Place agents in the zones on the existing markers | Fix what breaks in the real level |
| 18 Oct | All agents together: measure frame time, fix edge cases | Tuning; decide whether to use the fallback |
| 19 Oct | Record the 3-minute video | Record your agent's F2 view for the viva |
| 20 Oct | Buffer | Viva practice: explain every number in your section |
| 21 Oct | Submit | |

The senses layer comes first because all four agents depend on it and none can be tested
without it.

---

## 10. Decided, and still open

Decided:

- **Owners:** zombies, Systems Engineer; ghosts, Agent Controller; Biscuit, Core Developer; the
  Director, World Builder (see the table at the top).
- **The creature** reuses the zombie brain with `SO_CreatureConfig` and is placed in Zone 3.
- **Evidence and pickups** are placed on the zone markers by **Hollowmere > World > Place
  Pickups, Doors and Gates**. Biscuit and the Director read the same markers.

Still open:

- **Dr Marrow** has a model in Zone 4 but no behaviour.
- **The numbers in this document** are the values in the code now. Each owner must be able to
  say why the final value was chosen.
