# Biscuit: goal-oriented action planning

Biscuit is the player's dog. Every eighth of a second it decides for itself what would help most
right now, and works out the steps with an A* search over its own actions. Section 7 of the
[AI plan](README.md) is the design; this page is what was built and why each number is what it is.

Code: `Assets/_Hollowmere/Scripts/Runtime/AI/Biscuit/`. Tests: `Assets/_Hollowmere/Scripts/Tests/EditMode/Biscuit/`.

---

## 1. How it fits together

| Part | File | What it does |
|---|---|---|
| Body | `BiscuitAgent.cs` | The only MonoBehaviour. Hears (`INoiseListener`), smells (`ScentRegistry`), drives the `NavMeshAgent` on the Dog NavMesh, barks through `NoiseSystem.Emit`. Decides nothing. |
| Brain | `Brain/BiscuitBrain.cs` | Plain C#. Senses in, facts out, goal chosen, plan made, plan run. |
| Planner | `Brain/GoapPlanner.cs` | A* over world states. |
| Facts | `Brain/Fact.cs` | 6 true/false facts packed into one `ushort`. |
| Memory | `Brain/BiscuitMemory.cs` | Beliefs about the dead, fading as c0 x e^(-t / tau). |
| Goals | `Brain/BiscuitGoal.cs` | 5 goals, each with a priority from 0 to 1. |
| Actions | `Brain/Actions/*.cs` | Follow, Sniff, Growl, Bark, Hide, Lure. |
| Debug view | `BiscuitDebugView.cs` | F2: facts, goal priorities, the plan, planning time. |
| Scent | `ScentSource.cs` | Put on any enemy prefab so Biscuit can smell it. |

The brain never touches a GameObject. It talks to the body through `IBiscuitBody`, so the tests
drive it with a fake body and no scene.

## 2. Each think (8 times a second)

1. **Sense.** The nose smells anything within 5 m. Noises arrive from `NoiseSystem` as they happen.
2. **Facts.** Beliefs and the player's state become six facts.
3. **Goal.** Every goal is scored; the best one not yet met *that has a plan* wins.
4. **Plan.** A* finds the cheapest chain of actions to that goal.
5. **Act.** The first action runs. The brain plans again when a fact changes, the action ends or it fails.

## 3. Facts

| Fact | True when |
|---|---|
| NearPlayer | within 3 m of the player; stays true until 6 m |
| EnemyNearby | a belief within 25 m of the player |
| PlayerWarned | Biscuit has growled or barked since the last new threat, in the last 20 s |
| InDanger | a belief (confidence 0.3 or more) within 4 m of Biscuit |
| PlayerInTrouble | health under 0.35 and a belief within 8 m of the player |
| AreaScouted | sniffed within 8 m of here in the last 12 s |

## 4. Goals

| Goal | Wants | Priority |
|---|---|---|
| Stay safe | InDanger = false | 1.0 when in danger |
| Protect player | PlayerInTrouble = false | 0.9 when the player is in trouble |
| Warn player | PlayerWarned = true | 0.5 + 0.3 x strongest belief, when the dead are near and the player is not warned |
| Scout | AreaScouted = true | 0.35 when calm |
| Stay close | NearPlayer = true | 0.2 + 0.03 x metres to the player |

The goal being worked on gets +0.1 when goals are compared, so two close scores do not swap every think.

## 5. Actions

| Action | Needs | Gives | Cost |
|---|---|---|---|
| Follow | (player exists) | NearPlayer | 1 + 0.2 per metre |
| Sniff | not AreaScouted | AreaScouted | 1 |
| Growl | EnemyNearby, NearPlayer | PlayerWarned | 1 |
| Bark | EnemyNearby | PlayerWarned, **noise of 25 m** | 3 + 2 per extra one of the dead in earshot |
| Hide | InDanger | not InDanger | 1 |
| Lure | EnemyNearby, PlayerInTrouble | not PlayerInTrouble | 5 (then 15 s cooldown) |

## 6. The hook: the warning can wake the dead

The bark is a real noise in the shared senses layer. The planner knows that, through the cost:

- Beside the player, **Growl** (1) beats **Bark** (3).
- 15 m away with one of the dead nearby: Follow (4) + Growl (1) = 5, so Biscuit **barks** (3).
- 8 m away with three of the dead in earshot: Bark = 3 + 2 x 2 = 7, Follow (2.6) + Growl (1) = 3.6,
  so Biscuit **runs back and growls** rather than wake the street.
- When the player is badly hurt, **Lure** sends Biscuit 14 m off to one side to bark twice. The
  zombies' own hearing takes them to the bark. Nothing in the zombie code knows about Biscuit.

None of these outcomes is scripted. They come from the costs, the planner and the noise system.

## 7. The planner and its numbers

- **Search space.** 6 facts, so at most 2^6 = 64 states. Every array is sized once; a search
  number marks which entries belong to the current search, so planning allocates nothing.
- **Heuristic.** h = (goal facts unmet) x (cheapest action cost) / (most goal facts one action sets).
  No action can close the gap faster, so h never overestimates (admissible) and never drops by more
  than one action's cost (consistent). A* therefore returns the cheapest plan. The test
  `AStarMatchesDijkstra_FromEveryStateForEveryGoal` checks this from all 64 states for all 5 goals.
- **What if it were not admissible?** If Bark cost 0.5 but h still said 1, A* could stop on a
  dearer plan, because it would think the cheap branch was further from the goal than it is.
- **Cost of planning.** About 0.75 microseconds per plan, 4 states searched for a warning;
  about 1 microsecond for a whole think. The AI budget is 2 ms per frame.

## 8. Edge cases

| Case | What happens |
|---|---|
| No route to the player (a locked door) | Follow fails, is left out for 3 s, Biscuit sniffs instead, then tries again |
| Nowhere to hide | Hide fails, Stay safe has no plan, the next goal (warn) runs |
| Player teleports or falls (over 40 m) | Follow places Biscuit just behind the player |
| A sniff finds nothing | The area counts as scouted for 12 s, so it does not sniff again at once |
| A new one of the dead after a warning | PlayerWarned turns false, Biscuit warns again |
| An enemy is destroyed or its zone unloads | Its `ScentSource` unregisters and Biscuit forgets it |
| No player (menus, loading) | Every goal scores 0; Biscuit stands still |
| Stuck on a corner | An action running over 10 s fails and is left out |

## 9. Numbers worth defending

| Number | Value | Why |
|---|---|---|
| Think rate | 8 Hz | Reacts within 0.125 s; inside the 5-10 Hz the plan allows |
| Hearing acuity | 1.5 | Dogs hear better; a groan (20 m) reaches Biscuit from 30 m |
| Sniff range | 25 m | Same as the bark, so Biscuit knows what a bark would wake |
| Belief tau | 6 s | A smell is forgotten after 6 ln 10 = 14 s, a sound after 6 ln 6 = 11 s |
| Heard confidence | 0.6 | From the shared senses plan: a sound is a rough place, not the thing |
| Follow cost | 1 + 0.2/m | About one point per second of running, so costs read as time |
| Bark cost | 3 + 2 per extra | Quick but loud; each extra listener is a zombie it will wake |
| Near player | 3 m in, 6 m out | The gap stops stop-start following |

## 10. Testing it in the game

1. **Hollowmere > Biscuit > Set Up Biscuit Prefab** (adds the NavMeshAgent on the Dog NavMesh,
   `BiscuitAgent` and `BiscuitDebugView` to `PF_Biscuit`).
2. **Hollowmere > Biscuit > Create Test Dead Prefab** (a stand-in zombie until the real one is merged).
3. Open the World scene, press Play, drag `PF_Biscuit` next to the player and one or two
   `PF_TestDead` 10-20 m away. Press **F2**.
4. Walk around: Biscuit follows and sniffs. Press **G** (a test gunshot): the test dead wake and
   groan, Biscuit hears and warns.
5. To test Lure, the player needs health. In Play mode select `DebugPlayer (temporary)` and
   **Add Component > Debug Player State** (do not save the scene). Then press **K** four times
   (health 1.0 down to 0.2) with one of the dead close: Biscuit lures it away. Without it, Biscuit
   follows the object tagged `Player` and treats it as unhurt.
6. **Window > General > Test Runner > EditMode > Run All** runs the 21 tests.
