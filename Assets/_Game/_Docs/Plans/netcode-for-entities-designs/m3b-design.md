# M3b design: jump and death as tick plans, and spike S7 (Motion Warping vs the ghost's root)

Written 30 Oct 2026, code-free. Builds on `ghosts-design.md` section 3.3 (the `MotionPlan` shape) and the M3a work (plan sections 32-50).
Vault, Wallstride (wall-run, catch), Waterstride, follow and skill walk-in are M3c. Nothing here is built yet.

## 1. What the game does today (Netcode for GameObjects)

**Jump** (`Player/Runtime/PlayerJumpMotor.cs`, 867 lines): a click with Jump selected asks the server (`RequestJumpServerRpc`).
The server plans (`TryPlanJump`): learned rank, Flux check, start on the NavMesh, a walkable or water landing within
`landingSampleRadius`, distance up to the rank's `jumpDistance`, rise up to the rank's `jumpHeight` (down has no limit; owner
22 Oct), apex = max(clearance over both ends, `RiseForDistance`). With the warp asset the whole jump is one clip: phases give
take-off, apex and touchdown times, travel is even over the air time, the height follows the clip's root Y curve, and the server
sweeps a capsule along the way (`ValidateWarpedTrajectory`). Flux is paid **at take-off** (`TryPayForCommand(Jump)` when the
clip reaches its take-off time); an abort before take-off costs nothing. Landing cooldown `landingCooldown`. Other machines get
`JumpStartedClientRpc(landing, peak)` and replay the clips on their own clock while NetworkTransform carries the body. The owner
starts its crouch on the press (`BeginOwnJump`) and holds it at most 0.25 s until the server's body leaves the ground.

**Death** (`PlayerDeathMotor.cs`, 678 lines): the killing blow picks a chain: front half = turn to the blow and fall backwards,
behind = fall forwards without turning (memory: back hits never turn the body), critical = the big knockdowns, thrown
`critDistance` (5.5 m) away with `critLift` extra height. Each chain has a fall clip, a lying loop, a `travel` curve baked from
the clip's root motion, a distance and an impact time. Killed in the air (more than 0.15 m above the ground found within 80 m
below), the body falls. A disintegrating kill de-rezzes in place. A late joiner sees the lying pose through `netPose`
(Alive / OnBack / FaceDown / Gone). After `respawnDelay` (8 s) the server puts the player on a spawn point with full health.

## 2. The entity version

### 2.1 Components (player ghost)

| Component | Fields (integers) | Who sees it |
|---|---|---|
| `MotionPlan` (predicted, replicated) | `Kind` (None, Jump, DeathKnock, Fall), `PlanSeq`, `StartTick`, `TakeoffTicks`, `ApexTicks`, `LandTicks`, `FromMm`, `ToMm`, `ApexYMm`, `YawEndCdeg`, `Chain` (death set), `Flags` (Crit, Water, Paid) | everyone (observers animate from it) |
| `ClientMotionPlan` (client only, not a ghost field) | the owner's own jump plan for its latest jump click | owner |
| `Dead` (enableable, replicated) | none | everyone; movement and input skip a dead ghost by query |
| `DeathPose` (replicated) | `Pose` (Alive, OnBack, FaceDown, Gone) | late joiners |
| `PlayerInput` (existing) | + `Jump` byte on the click (the click asks to jump), same `ClickSeq` | owner -> server |

Every number is integer (mm, ticks, centi-degrees, permille) like the walk, so server and owner compute the same position on the
same tick. Curves are baked tables, not `AnimationCurve`s (section 2.4).

### 2.2 Systems

- **`PlayerJumpServerSystem`** (server, managed, inside `PredictedSimulationSystemGroup` before the move systems, like
  `PlayerServerPlanSystem`): for a click with `Jump` set, runs the existing planning rules on the server body (rank, distance,
  rise, landing sample, water landing, capsule sweep: PhysX and NavMesh stay server-only, as the 20 Oct "server checks moved out of
  the motors" line says) and writes `MotionPlan` or refuses (the existing `RefusedSeq`). The planner is lifted out of
  `PlayerJumpMotor` into a plain class so the motor and the system share it (no copy of the rules).
- **Flux:** checked before the plan (`CanPayForCommand(Jump)`), paid when the plan reaches its take-off tick, as today; an abort
  before take-off costs nothing. Through the `IRunCost` seam of M3a step 1, widened to an `IMoveSkillCost` with the skill named.
- **`PlayerMotionSystem`** (predicted, Burst, server + owner, never reads PhysX): for `Kind != None`, the pose at tick t is
  `Curve(Kind, t - StartTick)`; the walk (`PredictedMoveSystem`) is skipped for that ghost while a plan runs. On the landing tick it
  writes the pose, sets the landing yaw, clears the plan and starts the cooldown (`MoveState.CooldownUntilTick`).
- **Owner prediction of the jump (recommended, Q1):** the click source marks a jump click; `PlayerInputGatherSystem` plans it with
  the same planner on the client's PhysX/NavMesh into `ClientMotionPlan` (outside rollback, as the client path is), and
  `PlayerMotionSystem` on the owner uses it until the server's `MotionPlan` with the same sequence arrives: the same adoption rule as
  the walk's corners. A refused jump is taken back like a refused click.
- **`PlayerDeathServerSystem`** (server, managed): on the server body's kill (the existing `KillingBlow`), picks the chain as today,
  probes the ground and the air on the server, writes `MotionPlan{DeathKnock or Fall}`, sets `Dead` and `DeathPose`, and starts the
  respawn timer; on respawn: `Teleport` to a spawn point (the existing placement), clear `Dead`, full health on the body. Death is
  **not predicted** (the owner cannot know the blow before the server).
- **Presentation** (`PlayerPresentationFollowSystem` and the body's adapters): when `PlanSeq` changes, play the clip once with
  `startOffset = now - StartTick` (so a late snapshot fast-forwards and rollback never restarts a jump); the follow offset decays as for
  the walk. The body's NavMeshAgent stays off on client views (it already is).

### 2.3 Spike S7: does Motion Warping fight the ghost's root?

Today the warp moves the root (it "owns the root from the crouch onward"). With ghosts the root must come from the ghost.
Options, measured in the spike:
- **(a, recommended)** the ghost owns the root; the warp is not given the root (root writing off, or the warp not used for the
  jump at all) and Animancer plays the clip in place; the jump's height and travel are the same curves the warp would have made,
  baked from the same clip (2.4), so the feet still meet the ground.
- **(b)** the warp keeps driving the root through warp points `From`, apex, `To` on its own clock, and the body's follow offset is
  pulled back to the ghost each frame.
Pass for (a): over a whole jump the body's root and the ghost's pose differ by at most 2 cm on the owner and on an observer, the feet
are within 3 cm of the ground at take-off and touchdown, and a forced rollback mid-air (100 ms latency, server correction) neither
restarts the clip nor jumps the body more than the follow offset allows. The spike runs in the Editor (PlayMode, the Play DOTS here
path with a scripted jump), same guards as the walk run.

### 2.4 Numbers and curves (no numbers in code)

- Jump: `JumpMotionData` (gravity, clearance, hold, anticipation, cooldown, height-by-distance exponent, minimum rise, landing
  sample radius, skin, samples) and the rank (`jumpDistance`, `jumpHeight`, `chiCost`) stay where they are.
- Curves baked at build time (an Editor baker beside `Build Jump Warp Asset`): the warp clip's phases in ticks (take-off, apex,
  touchdown) and its root Y curve normalised to 0..1 in 32 samples; each death chain's `travel` curve, distance and impact time, and
  the fall height curve (`HeightCurveSteps` 24). Stored in one `PlayerMotionCurves` blob singleton in both worlds (made the way
  `PlayerMoveSettings` is made from `MovementSettings`).
- Death: `respawnDelay`, `critDistance`, `critLift` are on the `PlayerDeathMotor` component of `Player.prefab`; baked the same way.

## 3. Tests

- Pure: the jump planner's limits (distance, rise, down-unlimited, too short) with the real `JumpMotionData`; the curve tables (end
  points exact, apex at the clip's apex tick, monotonic travel); death chain choice by blow side (back never turns).
- Worlds (server + 2 clients, 0 and 100 ms): a jump lands exactly where the server planned, owner and server equal on every tick of
  the trace ring; a refused jump is taken back; Flux paid once at take-off, not on a refusal or an abort before take-off; the
  cooldown blocks a second jump; an observer sees the same landing; a walk click during a jump is refused with "Finish this jump before choosing another destination." (as today, `PointAndClickPlayer`; corrected 31 Oct).
- Death: the ghost is `Dead`, walking and jumping are refused, the knock lands where the server said, a late joiner sees `DeathPose`,
  the respawn teleports and clears `Dead`; disintegration leaves `Gone`.
- Scripted run (Play DOTS here): `KN_PLAYHERE_JUMP` points, logged like the walk run.

## 4. Order of work

1. S7 spike first (decides 2.3), with a throwaway jump plan straight from the clip curve.
2. Curve baker + `PlayerMotionCurves`, jump planner lifted into a shared class (motor keeps working on the old game).
3. `MotionPlan`, `PlayerMotionSystem`, `PlayerJumpServerSystem`, owner prediction, presentation; tests.
4. Death: `Dead`, `DeathPose`, `PlayerDeathServerSystem`, respawn; tests.
5. Scripted run, then the owner's feel checkpoint for jump and death.

## 5. Owner answers (30 Oct 2026, via Master)

- **Q1 (a):** the jump is predicted on the press, as `BeginOwnJump` feels today; the server corrects.
- **Q2 (a):** Flux is paid at take-off; an abort before take-off is free.
- **Q3 (a):** the ghost owns the root; Motion Warping does not move it (the clip plays in place, the curves come from the same clip).
- **Q4:** death is decided on the server only, not predicted.

The questions as they were asked:

- **Q1 Owner prediction of the jump:** (a) predicted on the press as today's `BeginOwnJump` feels, server corrects (recommended);
  (b) wait for the server (about one round trip before the crouch turns into flight).
- **Q2 When Flux is paid:** (a) at take-off, as today (an abort before take-off is free; recommended); (b) at the click.
- **Q3 S7:** (a) the ghost owns the root, the warp does not move it (recommended); (b) the warp drives the root and the follow pulls it
  back.
- **Q4 Death:** server only, not predicted (recommended; the only option that is safe).

## 6. Out of scope here (later steps)

Wallstride catch at the end of a blocked jump and Waterstride landings (M3c; the planner keeps the hooks but the entity version
refuses those plans until M3c), vault, a mounted jump's dismount (mounts are not ported yet), the Squad follower copying a jump,
the jump's i-frames for combat (M4).
