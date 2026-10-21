# Role Guide: Agent Controller

**Graphics duty: Agent Animation and Movement.** You turn the path the AI calculates into
smooth 3D character movement, rotation and animation.

Read the [role index](README.md) first for the Git routine, naming and limits.

---

## 1. What your role is

The AI decides *where* a character should go and gives a path: a list of points. On its own,
that produces a model that slides across the floor in straight lines and snaps round corners.
Your job is everything between the path and what the player sees: the body turns smoothly,
the feet match the ground speed, and the right animation plays.

The assignment marks you on: **"Agent movement, rotations, and animations following the
calculated path."**

---

## 2. What you must deliver

| # | Deliverable | For |
|---|---|---|
| 1 | A locomotion script that follows a path with smooth turning and speed changes | Zombies, the creature, Biscuit, Marrow |
| 2 | An Animator Controller with idle, walk, run, attack, hit and death | Each walking character |
| 3 | A floating movement script (no legs, passes through walls) | Ghosts |
| 4 | A finished prefab per character, ready for an AI brain to drive | `PF_Zombie`, `PF_Ghost`, `PF_Creature` |

Start with the zombie. The creature and Marrow reuse the same script and controller layout.

---

## 3. What already exists that you will use

- **The NavMesh.** Every zone has one baked for two agent types:

| Agent type | Radius | Height | Step | Used by |
|---|---|---|---|---|
| Humanoid | 0.4 m | 1.9 m | 0.55 m | Zombies, creature, Marrow |
| Dog | 0.25 m | 0.7 m | 0.5 m | Biscuit |

- **The path.** Unity's `NavMeshAgent` calculates it. `agent.path.corners` is the array of
  points: this is the "mathematical path array" in the assignment.
- **The character models.** Zombies, the creature and the ghosts are made and will be imported.
- **The AI plan.** [Docs/AI](../AI/README.md), section 4.2: each agent has a brain (decides),
  a body (moves) and visuals (animates). You build the visuals and the movement.

---

## 4. Step 1: import a character

1. Put the model in `Assets/_Hollowmere/Art/Models/Characters/Zombie/` as `SK_Zombie.fbx`.
2. Click it. In the **Rig** tab choose **Animation Type: Humanoid** for a human-shaped
   character and press **Apply**. Open **Configure** and check every bone is green.
   - Use **Generic** for the creature and the dog, which are not human-shaped.
3. In the **Model** tab: **Mesh Compression: Medium**, **Read/Write** unticked.
4. Get the animations: idle, walk, run, attack, hit reaction, death. Mixamo (mixamo.com) has
   free ones; download as **FBX for Unity, without skin**.
5. Import each clip into `Assets/_Hollowmere/Art/Animations/Zombie/`. Set its Rig to
   **Humanoid** and **Avatar Definition: Copy From Other Avatar**, choosing the zombie's avatar.
6. In each clip's **Animation** tab:
   - Tick **Loop Time** for idle, walk and run.
   - Tick **Bake Into Pose** for Root Transform Rotation and Position (Y).
   - Set **Anim. Compression: Optimal**.
   - Rename the clip `A_Zombie_Walk` and so on.

**Why Humanoid:** all human-shaped characters then share one set of clips. One walk clip
works for every zombie and for Marrow, which saves memory and work.

---

## 5. Step 2: the Animator Controller

1. Right-click > **Create > Animation > Animator Controller**. Name it `AC_Zombie`.
2. Open it. Add these **parameters**:

| Name | Type | Set by |
|---|---|---|
| `Speed` | Float | Your locomotion script, every frame |
| `Attack` | Trigger | The agent's body, when the brain attacks |
| `Hit` | Trigger | When the agent is shot |
| `Dead` | Bool | When the agent dies |

3. Right-click > **Create State > From New Blend Tree**. Name it `Locomotion` and open it:
   - Parameter: `Speed`.
   - Add three motions: idle at 0, walk at 1.2, run at 4.5. Use the real speed of each clip
     in metres per second as its threshold.
4. Add states `Attack`, `Hit` and `Death` with their clips.
5. Add transitions:
   - **Any State > Attack** on the `Attack` trigger; **Attack > Locomotion** with Has Exit Time.
   - **Any State > Hit** on the `Hit` trigger; back to Locomotion with Has Exit Time.
   - **Any State > Death** when `Dead` is true. No transition out.
   - Set each transition's duration to about 0.15 s so poses blend and do not snap.

**Why a blend tree:** it mixes idle, walk and run by speed, so any speed in between has a
matching animation. Separate states would jump from one clip to the next.

---

## 6. Step 3: the locomotion script

Create `Assets/_Hollowmere/Scripts/Runtime/Agents/AgentLocomotion.cs`.

The `NavMeshAgent` moves the character along the path, but by default it also rotates it
instantly. You switch that off and do the rotation yourself.

```csharp
using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.Agents
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentLocomotion : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] float turnSpeed = 240f;      // degrees per second
        [SerializeField] float speedSmoothing = 0.15f;

        static readonly int Speed = Animator.StringToHash("Speed");
        NavMeshAgent agent;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;             // we turn the body ourselves
        }

        void Update()
        {
            // Where the agent wants to go this frame, flattened to the ground.
            Vector3 direction = agent.desiredVelocity;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            // Feed the real ground speed to the blend tree, smoothed so it does not flicker.
            animator.SetFloat(Speed, agent.velocity.magnitude, speedSmoothing, Time.deltaTime);
        }
    }
}
```

What each part does:

- `agent.updateRotation = false`: stops Unity snapping the body to face the next corner.
- `Quaternion.RotateTowards`: turns at a fixed number of degrees per second, so a corner
  becomes a visible turn.
- `agent.velocity.magnitude`: the actual speed over the ground. Driving the blend tree with
  the real speed keeps the feet from sliding.
- `Animator.StringToHash`: looks the parameter up once, not by text every frame.

Then improve it, one commit each:

1. **Slow down for sharp corners.** Measure the angle between the current direction and the
   direction to the next path corner; lower `agent.speed` when it is large.
2. **Match the feet.** Set `animator.speed` so the clip's stride matches the ground speed
   when the agent is slowed by a slope or water.
3. **Draw the path.** In `OnDrawGizmosSelected`, draw lines between `agent.path.corners`.
   This shows the path array on screen: use it in the video and the viva.

---

## 7. Step 4: set up the NavMeshAgent

On the prefab's `NavMeshAgent` component:

| Setting | Zombie | Why |
|---|---|---|
| Agent Type | Humanoid | Must match the baked NavMesh |
| Speed | 1.2 walking, 4.5 chasing (set by the brain) | Match the animation clips |
| Angular Speed | 0 | You rotate it yourself |
| Acceleration | 6 | Lower values make starts and stops gentler |
| Stopping Distance | 1.2 | Stop at arm's reach, not inside the player |
| Auto Braking | on | Slows before the target |
| Obstacle Avoidance Quality | Low | High quality is costly with many agents |

---

## 8. Step 5: ghosts

Ghosts pass through walls, so they have no `NavMeshAgent` and no legs to animate. The AI
gives a target point and you make the movement look alive.

Create `GhostMovement.cs`:

- Move toward the target with `Vector3.SmoothDamp`, which eases in and out.
- Add a slow up-and-down bob: `sin(Time.time x bobSpeed) x bobHeight`.
- Lean into the direction of travel by a few degrees.
- Turn to face the player with `Quaternion.Slerp`.
- Fade the material in and out when the ghost enters or leaves a wall.

---

## 9. Step 6: build the prefab and test it

1. In your sandbox scene, add a large **Plane**. Add a **NavMesh Surface** component to it,
   set Agent Type to **Humanoid**, and press **Bake**.
2. Add a few cubes as obstacles and bake again.
3. Drag `SK_Zombie` into the scene. On its root, add a **NavMesh Agent**, a
   **Capsule Collider**, and your `AgentLocomotion`. Assign `AC_Zombie` to its Animator.
4. Make a small test script that sets `agent.destination` to wherever you click, so you can
   send the zombie round corners.
5. Watch for: sliding feet, snapping turns, jitter when it arrives, walking on the spot.
6. Drag the root into `Assets/_Hollowmere/Prefabs/Agents/` to make `PF_Zombie`.

Then test in a real zone: open the `World` scene, press Play, drag `PF_Zombie` into the
village, and send it across the boardwalk and through the water **without saving the scene**.

---

## 10. Working with the AI owners

The AI brain must never touch the Animator. It works through the agent's body, which raises
events. Your animation code listens:

| Event from the body | What you do |
|---|---|
| State changed (dormant, investigating, chasing) | Set walk or run speed; play a wake-up animation |
| Attack | Fire the `Attack` trigger |
| Took a hit | Fire the `Hit` trigger |
| Died | Set `Dead` to true, disable the `NavMeshAgent` and the collider |

Agree the exact event names with each agent's owner before either of you writes code.

---

## 11. Handing over

1. Commit the model, clips, controller, scripts and prefab with
   `git add -A Assets/_Hollowmere/Art Assets/_Hollowmere/Prefabs/Agents Assets/_Hollowmere/Scripts/Runtime/Agents`.
2. Tell each agent's owner the prefab is ready and which events it listens to.

---

## 12. Optimisation you must be able to explain

| Technique | What you did | Why |
|---|---|---|
| Humanoid retargeting | One set of clips for all human-shaped characters | Less memory, less work |
| Animation compression | Optimal compression on every clip | Smaller clips in memory |
| Animator culling | **Culling Mode: Cull Update Transforms** | Off-screen characters skip bone updates |
| Optimize Game Objects | Ticked in the Rig tab | Removes the bone hierarchy from the scene |
| Parameter hashes | `Animator.StringToHash` once | No text lookups each frame |
| Low-quality avoidance | Obstacle Avoidance Quality: Low | Cheaper with a dozen agents |
| LOD on characters | A simpler mesh at distance | Fewer skinned vertices |
| Bone limit | 2 bones per vertex in Quality settings | Cheaper skinning |

---

## 13. Viva questions to prepare

- What is the path array, where does it come from, and how does your code use it?
- Why switch off `updateRotation`, and how do you rotate the character?
- What is the difference between `RotateTowards`, `Slerp` and `LookAt`?
- What is a blend tree and why use one for locomotion?
- What causes foot sliding and how did you reduce it?
- What is root motion, and did you use it? Why or why not?
- What is the difference between Humanoid and Generic rigs?
- How does the ghost move with no NavMesh?
- What happens to animation cost when a character is off screen?
