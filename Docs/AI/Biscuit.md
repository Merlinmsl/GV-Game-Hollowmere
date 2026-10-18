# Biscuit: goal-oriented action planning

Biscuit is the player's dog. Every eighth of a second it decides for itself what would help most
right now, and works out the steps with an A* search over its own actions. Section 7 of the
[AI plan](README.md) is the design; this page is what was built and why each number is what it is.

Code: `Assets/_Hollowmere/Scripts/Runtime/AI/Biscuit/`. Tests: `Assets/_Hollowmere/Scripts/Tests/EditMode/Biscuit/`.

---

## 1. How it fits together

| Part | File | What it does |
|---|---|---|
| Body | `BiscuitAgent.cs` | The only MonoBehaviour. Hears (`INoiseListener`), smells (`ScentRegistry`, `PointRegistry`), drives the `NavMeshAgent` on the Dog NavMesh, barks through `NoiseSystem.Emit`. Decides nothing. |
| Brain | `Brain/BiscuitBrain.cs` | Plain C#. Senses in, facts out, goal chosen, plan made, plan run. |
| Planner | `Brain/GoapPlanner.cs` | A* over world states. |
| Facts | `Brain/Fact.cs` | 12 true/false facts packed into one `ushort`. |
| Memory | `Brain/BiscuitMemory.cs` | Beliefs about enemies (and their kind), fading as c0 x e^(-t / tau). |
| Goals | `Brain/BiscuitGoal.cs` | 7 goals, each with a priority from 0 to 1. |
| Actions | `Brain/Actions/*.cs` | Follow, Sniff, Growl, Whine, Bark, Hide, Lure, Fetch ammo, Bring ammo, Lead to evidence. |
| Enemies | `ScentSource.cs` | On each enemy prefab, with its kind: Dead, Ghost or Creature. |
| Pages, ammunition | `PointOfInterest.cs` | `PointRegistry` reads the zones' own `Evidence_*`, `Clue_*` and `Pickup_Magazine_*` markers when a zone loads. |
| Debug view | `BiscuitDebugView.cs`, `BiscuitWorldOverlay.cs` | F2: the panel, plus rings, routes and beliefs drawn into the Game view. |

The brain never touches a GameObject. It talks to the body through `IBiscuitBody`, so the tests
drive it with a fake body and no scene.

## 2. Each think (8 times a second)

1. **Sense.** The nose smells enemies, pages and ammunition within 5 m. Noises arrive from
   `NoiseSystem` as they happen. A sniff reaches 25 m for enemies and 35 m for pages and ammunition.
2. **Facts.** Beliefs, the player's state and known places become 12 facts.
3. **Goal.** Every goal is scored; the best one not yet met *that has a plan* wins.
4. **Plan.** A* finds the cheapest chain of actions to that goal.
5. **Act.** The first action runs. The brain plans again when a fact changes, an action ends or fails.

## 3. Facts

| Fact | True when |
|---|---|
| NearPlayer | within 3 m of the player; stays true until 6 m |
| EnemyNearby | a belief within 25 m of the player |
| GhostNearby | a ghost believed within 15 m of the player |
| PlayerWarned | Biscuit has warned since the last new threat, in the last 20 s |
| InDanger | a belief (confidence 0.3 or more) within 4 m of Biscuit; 6 m for a ghost |
| PlayerInTrouble | health under 0.35 and a belief within 8 m of the player |
| AreaScouted | sniffed within 8 m of here in the last 12 s |
| PlayerLowOnAmmo | the player's ammunition is under 0.3 |
| KnowsAmmo | has smelt ammunition |
| CarryingAmmo | has ammunition in its mouth |
| KnowsEvidence | has smelt a page the player has not seen |
| PlayerAtEvidence | the player is within 2.5 m of that page |

## 4. Goals

| Goal | Wants | Priority |
|---|---|---|
| Stay safe | InDanger = false | 1.0 when in danger |
| Protect player | PlayerInTrouble = false | 0.9 when the player is in trouble |
| Warn player | PlayerWarned = true | 0.5 + 0.3 x strongest belief (0.68 for a sound, 0.8 for a smell) |
| Resupply | PlayerLowOnAmmo = false | 0.6 when ammunition is low and there is some to fetch; not for 20 s after a delivery |
| Show evidence | PlayerAtEvidence = true | 0.45 when quiet and a page is left in the loaded zones |
| Scout | AreaScouted = true | 0.35 when quiet |
| Stay close | NearPlayer = true | 0.2 + 0.03 x metres to the player |

The goal being worked on gets +0.1 when goals are compared, so two close scores do not swap every think.

## 5. Actions

| Action | Needs | Gives | Cost |
|---|---|---|---|
| Follow | (player exists) | NearPlayer | 1 + 0.2 per metre |
| Sniff | not AreaScouted | AreaScouted, KnowsAmmo, KnowsEvidence (hoped for) | 1 |
| Growl | EnemyNearby, NearPlayer, not GhostNearby | PlayerWarned | 1 |
| Whine | GhostNearby, NearPlayer | PlayerWarned | 1 |
| Bark | EnemyNearby | PlayerWarned, **noise of 25 m** | 3 + 2 per extra listener, weighted by hearing |
| Hide | InDanger | not InDanger | 1 |
| Lure | EnemyNearby, PlayerInTrouble (and something that chases noise) | not PlayerInTrouble | 5, then 15 s cooldown |
| Fetch ammo | KnowsAmmo, not CarryingAmmo | CarryingAmmo | 1 + 0.2 per metre (10 m assumed if not smelt yet) |
| Bring ammo | CarryingAmmo | not CarryingAmmo, not PlayerLowOnAmmo | 1 + 0.2 per metre |
| Lead to evidence | KnowsEvidence, NearPlayer, not EnemyNearby | PlayerAtEvidence | 2 |

## 6. Plans it builds (all checked by the tests)

| Situation | Plan |
|---|---|
| A zombie near, Biscuit beside the player | Growl |
| A zombie near, Biscuit 15 m away | Bark (3 beats Follow 4 + Growl 1) |
| Three zombies in earshot, Biscuit 8 m away | Follow > Growl (3.6 beats a bark of 7) |
| A zombie near, and the creature 30 m behind Biscuit | Follow > Growl (a bark would cost 6.2) |
| A ghost near the player | Whine |
| Player badly hurt, a zombie close | Lure |
| Player badly hurt, only a ghost close | Whine (Lure is useless on ghosts) |
| Ammunition low, nothing smelt yet | Sniff > Fetch ammo > Bring ammo |
| Quiet, a page in the zone, Biscuit 5 m away | Follow, Sniff (either order) > Lead to evidence |

## 7. The hook: Biscuit knows who is listening

The bark is a real noise in the shared senses layer, and the planner knows who would hear it.
Each kind of enemy hears differently, so the same warning has a different price:

- **The dead** hear a bark from 25 m. Each extra one in earshot makes a bark dearer, so in a busy
  street Biscuit runs back and growls rather than wake them all.
- **The creature** hears 1.6 times better, from 40 m. Near it, Biscuit goes quiet.
- **Ghosts** do not chase noise. A bark does not wake them, luring them is pointless, and growling
  at something that walks through walls does nothing: Biscuit whines and backs off behind the
  player instead, which shows the player where the ghost is.

When the player is badly hurt, **Lure** sends Biscuit 14 m off to one side to bark twice. The
zombies' own hearing takes them to the bark. Nothing in the zombie code knows about Biscuit.

None of these outcomes is scripted. They come from the costs, the planner and the noise system.

## 8. The planner and its numbers

- **Search space.** 12 facts, so at most 2^12 = 4096 states. Every array is sized once; a search
  number marks which entries belong to the current search, so planning allocates nothing.
- **Heuristic.** h = (goal facts unmet) x (cheapest action cost) / (most goal facts one action sets).
  No action can close the gap faster, so h never overestimates (admissible) and never drops by more
  than one action's cost (consistent). A* therefore returns the cheapest plan. The test
  `AStarMatchesDijkstra_FromEveryStateForEveryGoal` checks this from all 4096 states for all 7 goals.
- **What if it were not admissible?** If an action cost 0.5 but h still said 1, A* could stop on a
  dearer plan, because it would think the cheap branch was further from the goal than it is.
- **Optimistic sniff.** The planner is told a sniff may turn up ammunition and a page, which is how
  it plans three steps ahead before anything is found. If the sniff finds nothing, the next step's
  needs fail, the brain plans again, and AreaScouted stops another sniff there for 12 s.
- **Cost of planning.** About 1.5 microseconds for a three-step plan (7 states searched), about
  1.4 microseconds for a whole think, no memory allocated. The AI budget is 2 ms per frame.

## 9. Edge cases

| Case | What happens |
|---|---|
| No route to the player (a locked door) | Follow fails, is left out for 3 s, Biscuit sniffs instead, then tries again |
| Nowhere to hide | Hide fails, Stay safe has no plan, the next goal (warn) runs |
| Ammunition taken before Biscuit gets there | KnowsAmmo turns false, Fetch is interrupted, the brain plans again |
| Ammunition Biscuit cannot reach | Fetch fails and that spot is ignored from then on |
| Enemy turns up while leading to a page | Lead's needs break, the brain warns instead |
| Player wanders off while being led | Biscuit waits at 5 m; past 6 m it plans Follow > Lead and comes back |
| Page up on a table | Distances are measured across the ground, and Biscuit waits as close as it can get |
| Delivered, but the player has not picked it up | No more fetching for 20 s |
| Player teleports or falls (over 40 m) | Follow places Biscuit just behind the player |
| An enemy is destroyed or its zone unloads | Its `ScentSource` unregisters and Biscuit forgets it |
| A zone unloads | Its pages and ammunition leave the registry; a page once shown stays shown |
| No player (menus, loading) | Every goal scores 0; Biscuit stands still |
| Stuck on a corner | An action running over 10 s (60 s for leading) fails and is left out |

## 10. Numbers worth defending

| Number | Value | Why |
|---|---|---|
| Think rate | 8 Hz | Reacts within 0.125 s; inside the 5-10 Hz the plan allows |
| Hearing acuity | 1.5 | Dogs hear better; a groan (20 m) reaches Biscuit from 30 m |
| Sniff range | 25 m enemies, 35 m pages | 25 m is the bark's reach, so Biscuit knows what a bark would wake |
| Belief tau | 6 s | A smell is forgotten after 6 ln 10 = 14 s, a sound after 6 ln 6 = 11 s |
| Creature hearing | 1.6 | From the AI plan; a bark reaches it from 40 m |
| Ghost danger distance | 6 m | Ghosts come through walls, so Biscuit keeps further away |
| Follow cost | 1 + 0.2/m | About one point per second of running, so costs read as time |
| Bark cost | 3 + 2 per extra listener | Quick but loud; each extra listener is an enemy it will wake |
| Near player | 3 m in, 6 m out | The gap stops stop-start following |

## 11. Testing it in the game

1. **Hollowmere > Biscuit > Set Up Biscuit Prefab** and **Create Test Prefabs** (once, not in Play).
2. Open the World scene, press **Play**, then **Hollowmere > Biscuit > Spawn Test Near Player**.
   Click in the Game view and press **F2**.
3. Walk around: Biscuit follows and sniffs (a blue ring spreads out). Press **G** (a test gunshot):
   the test dead wake and groan, Biscuit warns.
4. **Spawn Test Ghost Near Player**: Biscuit whines and backs off behind you.
5. **Spawn Test Ammo And Page Near Player**, then press **N** until ammunition is under 0.3:
   Biscuit sniffs, fetches the box and drops it at your feet. When it is quiet, it leads you to the page.
6. Press **K** four times (health 0.2) with a test zombie close: Biscuit lures it away.
7. **Window > General > Test Runner > EditMode > Run All** runs the 32 tests.
