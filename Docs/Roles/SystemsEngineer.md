# Role Guide: Systems Engineer

**Graphics duty: Player Interaction Physics.** You program the physics and scripts that let the
human player move through the world and act on it: open doors, push barricades, pick up and
throw objects.

Read the [role index](README.md) first for the Git routine, naming and limits.

---

## 1. What your role is

The World Builder has built the world, but right now the player is a temporary stand-in that
can only walk and look. Nothing can be opened, pushed, picked up or thrown. Your job is to
make the world respond to the player, using Unity's physics engine.

The assignment marks you on: **"Player physics and environmental interactions such as doors
and barricades."**

---

## 2. What you must deliver

In this order. Items 1 to 4 are the core of your marks.

| # | Deliverable | Prefab to hand over |
|---|---|---|
| 1 | The real player: walk, run, crouch, jump, look, flashlight | `PF_Player` |
| 2 | An interaction system: look at something, press **E** | part of `PF_Player` |
| 3 | Doors that swing on hinges, some of them locked | `PF_Door_Wood`, `PF_Hatch_Study` |
| 4 | Barricades the player can push and drag | `PF_Barricade_Crate`, `PF_Barricade_Barrel` |
| 5 | Objects the player can pick up and throw | `PF_Throwable_Bottle` |
| 6 | Pickups: keys, ammo, ice canisters, batteries, evidence pages | one prefab each |
| 7 | Zone exits that open only when the zone's three evidence pages are collected | `PF_ZoneExit` |

Weapons (the pistol and the ice gun) and player health are not assigned to anyone yet. Raise
this with the team: they fit most naturally with your player.

---

## 3. What already exists that you will use

- **The temporary player.** `DebugPlayer (temporary)` in the `World` scene, script
  `Scripts/Runtime/Debugging/DebugWalker.cs`. Read it: your player replaces it.
- **Markers.** Empty objects under `_Gameplay` in each zone scene show where your prefabs go:

| Zone | Markers for you |
|---|---|
| Zone 1, village | `Key_MarrowHouse`, `Pickup_Revolver_GuardHouse`, `Pickup_RevolverAmmo_Car`, `Pickup_Bottle_01` to `03`, `ZoneExit_EstateGate` |
| Zone 2, mansion | `Door_Front_Locked_HouseKey`, `Hatch_Undercroft_Locked_TunnelKey`, `Key_Tunnel`, three `Evidence_...`, `Pickup_IceCanister_01` and `02`, `Pickup_Battery_First`, `Pickup_Magazine_01`, `ZoneExit_Undercroft` |
| Zone 3, undercroft | `Door_LabToTunnel`, `Door_TunnelExit_Locked_ExitKey`, `Key_TunnelExit`, three `Evidence_...`, `Pickup_Magazine_01` and `02`, `ZoneExit_Waterworks` |
| Zone 4, waterworks | `Pickup_Magazine_01` and `02`, `Trigger_Confrontation` |

- **The Input System package.** The project uses Unity's new Input System, not the old
  `Input.GetKey`. Read keys with `Keyboard.current.eKey.wasPressedThisFrame`.
- **The AI hooks.** The AI needs two things from your player (section 9).

---

## 4. Step 1: the player

Work in your sandbox scene. Add a large **Plane** as a floor first.

1. Create an empty object named `PF_Player`. Set its tag to **Player** (the zone loading and
   the fog look for this tag).
2. Add a **Character Controller** component. Height 1.8, radius 0.35, step offset 0.55.
3. Add a child object `Head` at height 1.6, with a **Camera** on it. Set the camera's far
   clip plane to 450.
4. Add a child of `Head` named `Flashlight` with a **Light** set to Spot.
5. Create `Scripts/Runtime/Player/PlayerMovement.cs` and attach it to `PF_Player`.

What the movement script must do, every frame:

- Read WASD and build a direction relative to where the player faces.
- Choose a speed: walk 3.5, run 7 (Shift), crouch 1.8 (Ctrl).
- Apply gravity: when grounded keep a small downward speed (-2); otherwise add
  `gravity x Time.deltaTime`.
- Jump: when grounded and Space is pressed, set the vertical speed to `sqrt(2 x height x gravity)`.
- Call `controller.Move(velocity * Time.deltaTime)` once.
- Crouching: lower the controller height to 1.1 and the head to 0.9. Before standing up, cast
  a ray upward to check nothing is above.

Mouse look goes in a second script, `PlayerLook.cs`: turn the body left and right, tilt the
`Head` up and down, clamped to 85 degrees.

**Why a Character Controller and not a Rigidbody:** a first-person player needs exact,
predictable movement. A Rigidbody player slides, bounces and tips over. The Character
Controller handles steps and slopes for you. You must be able to say this in the viva.

A Character Controller does not push Rigidbodies by itself. You add that in step 4.

---

## 5. Step 2: the interaction system

One system that everything else plugs into.

1. Create `Scripts/Runtime/Interaction/IInteractable.cs`:

```csharp
namespace Hollowmere.Interaction
{
    public interface IInteractable
    {
        string Prompt { get; }              // text shown to the player, e.g. "Open"
        bool CanInteract(PlayerInventory inventory);
        void Interact(PlayerInventory inventory);
    }
}
```

2. Create `PlayerInteractor.cs` on the `Head`. Every frame it casts one ray forward from the
   camera, 2.5 m long, on an `Interactable` layer only. If it hits something with an
   `IInteractable`, show its prompt; when **E** is pressed, call `Interact`.
3. Create `PlayerInventory.cs` on the player: a list of keys held and a set of evidence pages
   collected.

Adding a layer changes a shared settings file (`ProjectSettings/TagManager.asset`). Tell the
team before you add the `Interactable` layer, and commit that file by itself.

**Why one ray on one layer:** a ray that tests every collider in the scene every frame is
wasted work. A short ray with a layer mask touches only the few things that can be used.

---

## 6. Step 3: doors

A door must be a real physics object, not an animation, because that is what your role is
marked on.

1. Make an empty object `PF_Door_Wood`. This is the frame; it does not move.
2. Add a child `Leaf` using a door mesh. Add a **Box Collider** and a **Rigidbody** (mass 25).
3. Add a **Hinge Joint** to `Leaf`. Leave Connected Body empty (it hinges to the world).
   Set the Anchor to the hinge edge of the door and the Axis to (0, 1, 0).
4. Tick **Use Limits**: min 0, max 100 degrees. Tick **Use Spring** with a small damper so
   the door slows and settles.
5. Create `Door.cs` implementing `IInteractable`:
   - Closed and unlocked: `Interact` adds a push (`AddTorque`) so the door swings open.
   - Locked: `CanInteract` checks the inventory for the key name. Without it the prompt says
     "Locked". While locked, set the Rigidbody to kinematic so the door cannot be barged open.
6. Add a **NavMesh Obstacle** to `Leaf` with **Carve** ticked. A closed door then blocks
   enemies from walking through it; when it swings open the gap reopens.

Because the door is a real Rigidbody, the player can also push it open by walking into it,
and a thrown object knocks it. That is "physics interaction" you can show in the video.

The study hatch (`PF_Hatch_Study`) is the same door lying flat: hinge axis along the floor.
The opening it covers is in Marrow's study, about 8 m long and 3 m wide.

---

## 7. Step 4: barricades

Things the player can shove to block a doorway or clear a path.

1. Make `PF_Barricade_Crate`: a crate mesh, a **Box Collider**, a **Rigidbody** (mass 40,
   drag 2 so it stops instead of sliding forever).
2. Add a **NavMesh Obstacle** with **Carve** ticked and **Carve Only Stationary** ticked, so
   enemies path around it once it stops moving.
3. Add pushing to the player. In `PlayerMovement.cs`:

```csharp
void OnControllerColliderHit(ControllerColliderHit hit)
{
    Rigidbody body = hit.collider.attachedRigidbody;
    if (body == null || body.isKinematic) return;
    if (hit.moveDirection.y < -0.3f) return;          // do not push what we stand on

    Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
    body.AddForce(push * pushStrength, ForceMode.Force);
}
```

4. Optional and worth showing: hold **E** on a barricade to drag it. Attach it to the player
   with a **Configurable Joint** while the key is held, and break the joint on release.

---

## 8. Step 5: picking up and throwing

1. Make `PF_Throwable_Bottle`: bottle mesh, **Capsule Collider**, **Rigidbody** (mass 0.5).
2. Pick up (**E**): set the Rigidbody to kinematic, parent it to a `HoldPoint` under the `Head`.
3. Throw (left mouse): unparent, turn kinematic off, set **Collision Detection** to
   **Continuous Dynamic**, and call
   `body.AddForce(head.forward * throwForce, ForceMode.Impulse)`.
4. On the first hard collision, make a noise so enemies come to where it landed:

```csharp
void OnCollisionEnter(Collision collision)
{
    if (collision.relativeVelocity.magnitude < 3f) return;
    NoiseSystem.Emit(transform.position, Loudness.Door, NoiseKind.Staged, gameObject);
}
```

This is the link between your physics and the AI: a thrown bottle draws the zombies away.

**Why Continuous Dynamic only while thrown:** a fast small object can pass straight through a
thin wall between two physics steps. Continuous detection prevents that but costs more, so
switch it on only for the throw and back to Discrete when the object comes to rest.

---

## 9. Step 6: what the AI needs from the player

Two small things. The interfaces are in `Scripts/Runtime/AI/Senses/`.

1. **Player state.** Your player implements `IPlayerState` (position, look direction, health,
   ammo, held weapon, flashlight on, crouching) and announces itself:
   `PlayerRegistry.Set(this)` in `OnEnable`, `PlayerRegistry.Clear(this)` in `OnDisable`.
2. **Noise.** Add the existing `PlayerNoiseEmitter` component to your player (it makes the
   footsteps). Call `NoiseSystem.Emit(...)` yourself for gunshots and doors.

---

## 10. Pickups and zone exits

- A pickup is a small prefab with a trigger collider and a script implementing
  `IInteractable` that adds itself to the `PlayerInventory` and then disables itself.
- Evidence pages work the same way and record their name in the inventory.
- `PF_ZoneExit` is a trigger that checks the inventory holds that zone's three pages (and the
  key, where one is needed) before letting the player through.

---

## 11. Handing over

1. Put finished prefabs in `Assets/_Hollowmere/Prefabs/Interactables/` or `Prefabs/Pickups/`,
   and the player in `Prefabs/`.
2. Test each one in your sandbox scene, then in the `World` scene **without saving the scene**.
3. Push your branch and tell the World Builder which marker each prefab goes on.
4. To swap the player: the World Builder puts `PF_Player` where `DebugPlayer (temporary)` is
   and deletes the debug one.

---

## 12. Optimisation you must be able to explain

| Technique | What you did | Why |
|---|---|---|
| Primitive colliders | Boxes and capsules on doors, crates, bottles | Far cheaper than mesh colliders |
| Layers and the collision matrix | Pickups do not collide with each other | Fewer pairs for the engine to test |
| Sleeping rigidbodies | Drag on barricades so they come to rest | A sleeping body costs nothing |
| Kinematic when locked or held | Locked doors and held objects are kinematic | No simulation for things that cannot move |
| Continuous detection only when thrown | Switched on for the throw, off at rest | Accuracy only where needed |
| One short interaction ray with a layer mask | 2.5 m, `Interactable` layer only | Avoids testing the whole scene each frame |
| Physics in `FixedUpdate` | Forces are applied there, input is read in `Update` | Physics runs on a fixed step |
| NavMesh Obstacle carving | Doors and barricades carve, no runtime rebake | A rebake would stall the game |

---

## 13. Viva questions to prepare

- Why a Character Controller for the player and not a Rigidbody?
- How does a Hinge Joint work, and what do the limits and the spring do?
- Why is a locked door kinematic?
- What is the difference between Discrete and Continuous collision detection?
- What is the difference between `Update` and `FixedUpdate`, and what goes in each?
- How do enemies know a door is closed?
- What would happen to performance with 200 loose physics objects, and what did you do about it?
