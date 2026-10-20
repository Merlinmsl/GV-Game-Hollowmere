# Hollowmere: AI Plan (SE3062 Intelligent Systems)

The plan for the four graded agents, the system they share, and the order to build them in.
Read sections 1 to 4 first; then only the section for your own agent.

For the story see [Docs/Story](../Story/README.md). For running the game see
[Docs/Setup](../Setup/README.md).

**Deadline: 21 October 2026.** Owners are not assigned yet (see section 10).

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

Two more characters exist in the story but are **not graded agents**. They reuse the brains above
with different settings:

- **The creature** (Zone 3 tunnel): the zombie brain with sight switched off and sharper hearing.
- **Dr Marrow** (Zone 4): the ghost's position scoring, on the NavMesh, with a gun.

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

| Noise | Loudness (m) |
|---|---|
| Gunshot | 45 |
| Biscuit's bark | 25 |
| Zombie groan (alert) | 20 |
| Ice gun | 15 |
| Running | 12 |
| Door, hatch | 10 |
| Wading through water | 9 |
| Walking | 5 |
| Crouch-walking | 2 |

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

The player is "seen" when the meter reaches 1. This makes the dark mansion and tunnel matter:
the flashlight helps the player see and helps the enemy see the player.

Sight checks run about 4 times a second per agent, not every frame.

### 3.3 Memory

Agents never read the player's true position. Each keeps a **belief**:

```csharp
struct Belief { Vector3 position; float confidence; float time; BeliefSource source; }
```

Confidence fades: `confidence = c0 x e^(-t / tau)`. Seen starts at 1.0, heard at 0.6, told by
another agent at 0.4. When confidence falls below 0.1 the agent gives up.

### 3.4 Player state

The player (Systems Engineer) exposes one interface. Until the real player exists, the debug
walker implements it with test keys.

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

- **Think slowly.** Brains tick 5 to 10 times a second through `AiScheduler`, spread across
  frames. Movement and animation still run every frame.
- **Path budget.** At most 3 path searches per frame; the rest wait in a queue.
- **No per-frame searches.** No `FindObjectOfType`, no `GetComponent`, no allocation inside a tick.
- **Only the current zone thinks.** Agents in an unloaded zone do not exist.
- **Awake limits:** 12 zombies, 3 ghosts, 1 creature at once. The Director enforces this.

### 4.2 Logic separate from visuals

Each agent has two parts:

- **Brain:** plain C# classes. No `MonoBehaviour`, no animator, no mesh. Takes senses in,
  gives a decision out. Can be unit-tested.
- **Body:** one thin `MonoBehaviour` that feeds the brain, moves the `NavMeshAgent`, and tells
  the Agent Controller's animation code what the brain decided through events
  (`OnStateChanged`, `OnAttack`).

### 4.3 Debug view

Every agent draws its thinking when debug is on (key **F2**): the active tree node, the utility
scores, the current plan, the stress graph. Build this early. It is how you find bugs, and it
is what you show in the viva and the video.

### 4.4 Folders and branches

```
Assets/_Hollowmere/Scripts/Runtime/AI/
├── Senses/        shared: noise, sight, beliefs, player state
├── Scheduling/    shared: AiScheduler, path budget
├── Zombie/
├── Ghost/
├── Biscuit/
└── Director/
```

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

**Edge cases to handle:** belief position is unreachable (go to the nearest reachable point);
two noises at once (keep the higher confidence); player leaves the zone (return to dormant).

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

---

## 9. Build order

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

## 10. Still to decide

- **Owners** of the four agents, and who builds the shared senses layer.
- **Who sets up the creature and Marrow** from the reused brains.
- **Evidence and pickup placement**, which Biscuit and the Director both read.
- **The numbers in this document** (loudness, weights, thresholds) are starting values. Each
  owner tunes their own and must be able to say why the final value was chosen.
