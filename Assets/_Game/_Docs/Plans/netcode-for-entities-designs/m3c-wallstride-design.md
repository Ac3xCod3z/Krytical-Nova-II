# M3c Wallstride on the entity netcode: NGO rules and design (1 Nov 2026)

Plan section 55 item 5 is the acceptance list. Owner's standing rule: the NGO rules table first (section 1), then the design. Section 1
was drafted by a read-only research pass and its citations spot-checked (WallRunRules 113-131 refusal texts, WallWalk 130-136 Flux
check, motor 239-243 click order, motor 124-128 replicated pose: all as written). Paths are under `Assets/_Game/`. M =
`Gameplay/Player/Runtime/PlayerWallRunMotor.cs`, WW = `PlayerWallRunMotor.WallWalk.cs`, R = `WallRunRules.cs`, S =
`Gameplay/Player/Data/WallstrideSettings.cs`, PCP = `PointAndClickPlayer.cs`, JM = `PlayerJumpMotor.cs`, DM = `PlayerDeathMotor.cs`.
Every number in `WallstrideSettings.asset` equals the class default in S.

## 1. NGO Wallstride rules

NGO has TWO climb paths. **Straight climb** (Plant -> Climb -> Mantle / ToPerch / KickOff, planned at click time from PhysX rays and
NavMesh samples, no surface graph): trees, zones without a graph, runs that arm by themselves. **Wall walk** ("walls as a second
ground"): when the zone has a `surfaceGraph` and the click is near a node, routed by `SurfaceRoute` anywhere on the wall, over the
edge, down, or leaping off. A click goes to the wall walk first when `WantsWallWalk` is true (M:241).

| # | Rule (NGO) | Source | Numbers | Scene data |
|---|---|---|---|---|
| 1 | opens at level 60, Run 20, Jump 20 | R:63-64, M:179-180 | 60 / 20 / 20 | - |
| 2 | a click climbs only off view mode, on a spot not walkable from here, on a non-Ground layer or a steep slope the graph covers, with Run or Jump selected; it cancels attack, selection, interact, travel | PCP:840-853 | - | `groundLayers`; roof NavMesh |
| 3 | a steep-slope click needs angle >= 45 and a graph node near | WW:74-75 | graphMinSurfaceAngle 45 | graph |
| 4 | node lookup: a top click (< 36 deg) never snaps below its edge; else nearest node to point + n x 0.38 within 1.2 m | WW:77-84 | wallStandOff 0.38, wallClickReach 1.2 | graph |
| 5 | click order: vendor stall "Vendor stalls can't be climbed."; a graph node -> wall walk; not climbable -> a walk; client runs `Decide` then the server RPC | M:237-253 | - | `VendorStall.Is` (<= 6.5 m, an IInteractable within 3 m) |
| 6 | server re-finds the collider from the point (ray 0.5 out, 1 long), own layers, RPC budget; no surface "That cannot be climbed." | M:255-272, 1107-1112 | - | wall colliders |
| 7 | Jump selected: Decide(fromJump) -> catch point -> `JumpMotor.TryJumpToCatch`; Run: Decide -> `ArmOrBegin`, else "There is no way to the foot of that wall." | M:274-291 | - | NavMesh to the foot |
| 8 | climbable: not trigger, not own, not a stall; `ClimbableSurface` decides (Never = no), else wallLayers, no dynamic Rigidbody, no IDamageable / IInteractable | M:499-511 | wallLayers = Default | `ClimbableSurface` (Wall/Tree/Never, trunkRadius 0.35, height 8) |
| 9 | a ledge or sill click (< 70 deg) uses the face ahead at 1.2 m chest height on the same root; open front: 16 probes round the bounds | M:408-415, 445-496 | 70; 1.2, 16, gap 2 | building colliders |
| 10 | refusals in order: "Wallstride opens at level 60 with Run and Jump at rank 20." / "Not now." / "Select Run to run up walls, or jump at them." / "That cannot be climbed." / "Too gentle a slope to run up: walk it." / "It leans out too far to run up." / "Come at it head-on to run up it." / "Low enough to vault or walk." / "Too high: Wallstride reaches 14 m." / "There is nowhere to stand at the top." / "There is no room at the top." / "Not enough Flux (12 to climb)." | R:84-131 | overhang 20, min 2.2 m, max 14 m, Flux 12 | - |
| 11 | Busy = dead, in a phase, or (not from a jump) jumping; Stride = from a jump, Running, or Jump selected | M:418-435 | - | - |
| 12 | slant: a run within 45 deg of head-on, a jump within 65 | R:81-82, 96 | 45 / 65 | - |
| 13 | wall shape: ground 1.2 m out (60 m ray); face scan up every 0.25 m (ray 1.8), gaps < 2 m count as face; foot = wall foot + n(0.38 + ledge) on open NavMesh within 1.2; top 1.1 m past the edge within 0.7 + 0.6, within 0.8 m of the edge height, open ground, capsule room; climb stops 1.1 m below the edge | M:546-594 | consts + mantleReach 1.1, topSampleRadius 0.7, edgeBelowHands 1.1 | roof NavMesh and colliders |
| 14 | tree: climbed on the side come from; foot = trunk + side(r + 0.38); perch `PerchFor(side, 0.6, r)`; climb ends 0.6 m below it | M:523-544 | perchHeightShare 0.6 | trees only as proxy colliders (`TreeProxyField`: 35 m round a player, Default layer, max 512, >= 3 m, every 0.25 s) |
| 15 | never inside a building: `OnOpenGround` false with a ceiling within 6 m and walls within 6 m on 3 of 4 sides; used for foot, top, drops, landings | M:1152-1170 | 6 m | roofed building colliders |
| 16 | auto-arm while running to a place >= 1.1 m higher with no walk or a walk >= 2x the line: a ray at 1 m ahead up to 8 m must hit a face whose top is within 2 m of the place | M:298-324; PCP:1639-1643 | autoLookAhead 8, autoDetourFactor 2 | NavMesh |
| 17 | arming: at once within 0.45 + r of the foot, else walk there; dropped if another order moved the destination > 0.6 m after 0.15 s | M:605-625, 964-971 | 0.45, 0.15 | NavMesh |
| 18 | a jump catches a wall when its arc is blocked: sphere-cast r x 0.6 at 1 m along the jump; catch at feet + 1.4 clamped to the climb; refusal "Too high to jump at: Jump rank reaches {h} m."; failed hold "Could not hold on. Returned to takeoff." | M:331-367, 597-601; JM:303, 321-338, 384-393 | catchHeight 1.4 | colliders |
| 19 | jump-at-wall refusals (`TryJumpToCatch`), NGO's jump order and words | JM:345-373 | Jump rank | - |
| 20 | Flux: 12 when the straight climb begins; a jump catch also pays the jump | M:630, 643, 361; JM:357 | 12 | - |
| 21 | straight route legs (agent off): Plant 0.18 s (from a jump 0.12-0.30 by slant/65, turn eased in), lean 12 deg; Climb linear at clamp(ground x 0.55, 4, 8) m/s; Over: lift 0.25 + 0.35 inward, then in over the top 0.2-0.6 s; no top: KickOff out 2.5, lift 0.8, gravity 14 | M:667-752; R:109-110 | asset numbers; MaxLegs 6. `mantleLift` 0.35 is in the asset but unread (the 0.35 at M:707 is a literal) | - |
| 22 | Motion Warping: one runtime asset against a 30 s silent clip, one phase per leg, eased root curves (8 samples per leg), warp points feet + up x baseOffset; a manual fallback moves the body along the same legs | M:198-204, 760-891 | 30 s, 8 | `MotionWarping` component |
| 23 | ends: Perched snaps upright; else Land (agent on within 1.5 m), then the walk on to the click | M:905-953 | - | NavMesh |
| 24 | perch: only a ground click ("Click the ground to drop down to it."); server "Choose a walkable place to drop down to." / "Too far: drop within 25 m of the tree."; DropDown arc lift 0.4; a vanished tree proxy drops the player | PCP:704-710; M:370-400, 926-979 | 25 m | tree proxies |
| 25 | wall walk from the ground: no graph "That cannot be climbed."; Locked; dead "Not now."; no node "That cannot be climbed."; busy / off NavMesh "Not now."; Jump + Flux >= 1: JumpOnto; route "There is no way onto that wall from here."; Flux < 1 the NoFlux text (it says 12, checks 1); no client pre-check | WW:86-137 | - | graph, NavMesh |
| 26 | plant on a wall: walk to the join's navPoint (0.6 + r), plant 0.32 s with up = the face normal; from a roof two legs out over the edge (+0.25 up, 0.5 out) and down head-first | WW:172-236 | wallPlantSeconds 0.32 | graph joins |
| 27 | on the wall: Grip 15 Flux/s in whole points (fail -> PushOff); speed ground x 1.0 along nodes; feet stick to the face (probe 0.6 out, 1.2 long, >= 36 deg, within 60 of the node normal), lift 0.03; normal and rotation slerp 1 - e^(-10 dt); facing = travel on the plane, up = normal; no height cap | WW:387-444, 593-600 | grip 15, share 1, turn 10, lift 0.03 | wall colliders |
| 28 | clicks on a wall: a unit still selects; else `TryWallClick`, marker on the wall; Jump: a node > 2 m -> JumpOnto, else JumpOffTowards; a node -> `SurfaceRoute.Along` ("That part of the wall is not joined to this one."); else NavMesh within 1.5 ("Choose a place to go to.") and the shorter of Off(Top) / Off(Ground), else jump off | PCP:695-701; WW:98-170 | snap 2, JumpOnMinimum 2 | graph |
| 29 | wall leap: a target > 3.5 m above caught lower (node near feet + 2.1 within 2.5, else "Too high to jump to."); "Too far: jump within 25 m."; "Something is in the way."; two legs apex +0.8, gravity 14; round a corner the apex swings 1.5 m out; no Flux beyond the >= 1 check | WW:247-315 | 3.5, 0.8, 25, 14 | graph, colliders |
| 30 | jump off a wall: "Choose a place on the ground or a roof to jump to." / "Too far: jump off within 25 m." / "Too high to jump up to." / "Something is in the way."; arc lift 1.2 raised by 1.5 up to 3 times; PushOff (front flip) | WW:486-540 | pushOffLift 1.2 | colliders, NavMesh |
| 31 | coming down: route end with no exit -> hold; Top -> TopOut (mantle 0.4 s); Ground -> StepDown (lift 0.25); then the walk on; a roof with no walk down, or a run whose walk is >= 2x, arms `SurfaceRoute.Through` (Flux >= 1) | WW:447-483, 325-343; PCP:1632-1636 | 0.4, 2 | graph Top/Ground joins |
| 32 | out of Grip: PushOff lands 3 m out (ray 80 m, NavMesh within 3, else the foot, open ground), lift 1.2. The only forced exit, and it is the owner's Grip rule; no knockback code exists for players | WW:546-569, 640-661 | 3, 80 | NavMesh |
| 33 | wall fights: melee chase on a wall via `ServerChase` at most every 0.5 s when the target moved > 1 m; no strike during a hand-off | PlayerMeleeCombat 263-275, 290, 633-648, 680; WW:632-661 | 0.5, 1 | - |
| 34 | other orders while busy: clicks ignored mid-climb unless perched or on a wall; `TravelTo` "Come down first."; move anticipation off | PCP:685, 960, 1296-1343 | - | - |
| 35 | death off the ground (motor busy, jump/vault busy, or >= 2.5 m over Ground with no path down): disintegrate in place, pet hidden too, clip at 0.2x | DM:204-311, 577-580; M:989-1013 | 2.5, 0.2; deathPushOff 0.6 | Ground layer, NavMesh |
| 36 | GM teleport: `Cancel` lands on NavMesh within 16 m (else the foot) | GmCommands 842-882; M:1016-1026 | 16 | NavMesh |
| 37 | replicated: `NetworkVariable<WallRunPose>` {Phase (12), Yaw, Duration}; the body by server-authority NetworkTransform with all 3 rotation axes (the wall tilt); RPCs climb / drop / wall click (budgeted), refusals to the owner; legs, arm and grip are not sent | M:126-127, 169, 222-272, 378-384, 956-1034; WW:106-112 | - | Player.prefab:582-602 |
| 38 | others see per phase: plant clip fitted, climb loop 1.3x, mantle fitted, perch idle, wall move = locomotion, push-off flip, jump-on, drop; fades 0.12 remote, 0.1 / 0.15 own | M:1036-1103 | 1.3, fades | Wallstride_Plant / _Climb_Loop / _Mantle anims, Synty crouch idle and fall |
| 39 | feet VFX on a wall (`PFX_Wall_Loop` under each sole, no run dust), Landed on the face; sounds only for the own player | M:1043-1045; GameFeelPresenter 728-748; ActorVfx 209-240, 346 | - | `PFX_Wall_Loop` |
| 40 | the ground marker lies on the wall when the normal is > 50 deg from up | WorldMarkerPresenter 83-160 | 50 | - |
| 41 | surface graph: `ZoneCatalog.surfaceGraph`, Zone01 only (502,111 nodes, 1.75 MB bytes, baked 22 Oct): nodes, normals, edges, joins {node, navPoint, Ground/Top}, 2 m grid; preloaded in the background; routes A* with NavMesh-costed ways in and out | World/Data/SurfaceGraph.cs 18-165; ZoneRules 38; SurfaceRoute 13-212 | bake spacing 0.5, stand-off 0.38, walls 45-110 deg | baked in the Editor |

Pure (liftable): `WallRunRules` (already static), leg layout and ease maths, arcs, Grip debt, wall-walk speed along nodes, `FeetAt`,
`PerchFor`. PhysX: climbability, face scans, catches, open-ground, stick probe, jump clears, tree proxies. NavMesh: foot/top samples,
landings, all of `SurfaceRoute`. Agent: filter, radius, base offset, arming walks. Static entry points that already take a position:
`WallRunRules.*`, `SurfaceRoute.Plan/Through/ToNode/Along/Off`, `SurfaceGraph.Nearest/JoinsNear`, `ClimbableSurface.PerchFor`,
`VendorStall.Is`. To lift into a `PlanForGhost` (private today): `Decide` (M:405, already takes the feet), `Shape` (M:517),
`CatchPointOn` (M:597), `StartRoute` (M:667), `NodeAt`, `JumpOnto`, `JumpClear`, `ArcClear`, `FeetAt`. `PlayerJumpMotor.PlanForGhost`
does not plan a catch yet (JM:147).

NGO oddities kept as NGO does them (owner's bar is exact NGO): the wall walk's NoFlux text says 12 while it checks 1; a wall leap
charges no Flux; `mantleLift` is unread.

## 2. Decisions before the design (to the Master, multiple choice)

A. **Order of the parts.** Wallstride is five systems in one motor. Proposed: (1) the straight climb with Run (wall, plant -> climb ->
mantle, Flux 12) as a stateless `MotionPlan` of up to 6 eased legs, like the jump; (2) trees: perch and drop; (3) the jump that catches
a wall; (4) the wall walk on the surface graph (plant, move with Grip, top-out, step-down, leap, jump off, push-off); (5) the run
that arms by itself. Each finished and proven in the GUI before the next.

B. **The body's tilt on a wall.** NGO sends all three rotation axes (the body stands on the face). The DOTS pose has yaw only. Options:
(a, recommended) a replicated `PlayerPose` up-vector (the face normal in permille, 3 x short) used only while on a wall; (b) the tilt
drawn from the surface graph on each client (no ghost field, but observers could disagree with the server near corners).

Picks (owner, 1 Nov, through the Master): **A = (b)** the wall walk on the surface graph first (plant, move with Grip, top-out, step
down, leap, jump off, push-off), then the straight climb, trees / perch / drop, the jump that catches a wall, and the self-starting run.
**B = (a)** a replicated up-vector, used only on a wall. The owner's standing Wallstride rules hold for every part: no glitching or
clipping, never knocked off, the player leaves only by choice (or out of Grip), a way down from roofs, the marker on the wall, feet VFX.

## 3. Design: part 1, the wall walk

### 3.1 One idea: the wall is legs of the walk's own path

The DOTS walk is already a 3D polyline in mm with a progress (`PlayerPath`), and the vault and Waterstride are ranges of that progress
with their own speed and shape. The wall walk is the same: a click on a wall gives ONE walk whose corners run on the NavMesh to the
join's `navPoint`, then the plant point, the wall's node feet, and the way off (top-out or step-down points, then the walk on). A
**`WallLeg` per corner** (the segment that ends at that corner) says how that segment is moved:

| field | meaning |
|---|---|
| `Micros` | 0 = moved at the walk's speed (ground segments: the walk's own rules; wall segments: `WallPlan.SpeedMmps`, NGO ground speed x `wallSpeedShare`, no acceleration or arrival slowing). > 0 = a **timed leg** (NGO `Leg.Seconds`): progress runs at length / time, so the leg takes exactly that long |
| `EaseUp`, `EaseAlong` | NGO's `Ease` (Linear / In / Out / InOut) for the height and the ground part of a timed leg: the point is `lerp(a, b, ease(f))` per axis, `f` the share of the leg covered. Even progress + eased position = NGO's warp curve (`AddLeg` keys) |
| `UpX/Y/Z`, `FwdX/Y/Z` | the body's facing at the corner in permille (NGO `Leg.Facing`, or on the wall `WallFacing(node normal, travel)`); zero = upright, facing the way |
| `Phase` | `WallRunPhase` of the segment (Plant, WallMove, Mantle, DropDown, PushOff, JumpOn) for the presentation |

Header `WallPlan` {Seq, SpeedMmps, OnWallFrom (index of the first corner on the wall), OffWallFrom, Flags: EndsOnWall, Truncated} on the
server (ghost, owner only, like `PathPlan`) and `ClientWallPlan` + a `ClientWallLeg` buffer for the owner's own click (not ghost fields:
a rollback leaves them, like water). `PlayerMovement.Step` gains the wall overload: on a wall or timed segment the speed is the leg's,
the pose is the eased point, a Stop does nothing while `MoveState.Wall != 0` (NGO: `StopMoving` returns early, the agent is off on a
wall), and no click starts during a timed segment (NGO ignores clicks while `IsBusy` and not on the wall, PCP:685).

Stateless like the vault: the pose, the tilt and the phase are functions of the progress, so a rollback never leaves them wrong.
`MoveState.Wall` (ghost byte): 1 while the progress is on the wall (from `OnWallFrom` to `OffWallFrom`), or the walk ended on the wall
(`EndsOnWall`, NGO WallHold). Set and cleared by the walk only; a teleport clears it.

### 3.2 The tilt (pick B a)

`PoseTilt` (ghost to everyone): {UpX, UpY, UpZ, FwdX, FwdY, FwdZ} short permille + `Phase` byte. Zero up = upright (the yaw in
`PlayerPose` rules). Written by the step on server and owner from the segment: on a timed leg the facing goes from the previous corner's
to this corner's by the leg's share (NGO warps the rotation over the leg); on wall segments the up blends between the two nodes' normals
by the share and the forward is the travel projected on that plane (NGO `WallFacing`). `PlayerPose.YawCdeg` keeps the forward's ground
heading, so everything that reads yaw today (targeting, the minimap) still works. The presentation eases the drawn rotation toward the
tilt at NGO's `wallTurnRate` (10, `1 - e^(-10 dt)`), NGO's own easing, on every machine.

### 3.3 Where each NGO rule goes (DOTS equivalent | built in DOTS now?)

| NGO rule (section 1) | DOTS | now |
|---|---|---|
| 1 gate (60 / 20 / 20) | owner: `MoveRanks` (Level, Run, Jump); server: the body's skills | ranks yes, Wallstride gate no |
| 2 a wall click | `PointAndClickPlayer` view branch: a ray meeting a non-Ground layer, or a steep slope the graph covers, with Run or Jump selected -> a wall click; `PlayerInput` gains `Wall` byte + the normal (3 short permille) | no |
| 3-4 node lookup (45 deg, 0.38, 1.2, a top click < 36 deg never snaps below) | the motor's own `NodeAt`, made usable for a ghost | no |
| 5-6 click order, vendor stall, the server re-finds it | server plan system: the wall branch before the walk; the planner finds the node from the point and normal itself (the graph, never the client's word) | no |
| 25 from the ground: refusals in NGO's words and order; Jump + Flux >= 1 -> JumpOnto; `SurfaceRoute.ToNode`; Flux >= 1 (the "12" text) | `IWallPlanner.Plan` (a Persistence.Entities adapter over the body's motor, like `BodyWaterPlanner`); the owner plans the same with its own body (graph and NavMesh are on the client) | no |
| 26 plant (navPoint at 0.6 + r, 0.32 s, Ease In / Out; from a roof two legs out over the edge and down) | timed legs in the walk: the NavMesh walk ends at the `navPoint`, then the plant leg(s) | no |
| 27 on the wall: speed ground x 1.0, stuck to the face, facing, lift 0.03 | wall segments at `WallPlan.SpeedMmps`; corners = node feet **snapped to the real face at plan time** (NGO's stick probe: 0.6 out, 1.2 long, >= 36 deg, < 60 deg off the node), so no PhysX in the loop; the polyline is thinned where it runs straight on the face (a node is kept where the way or the normal turns > 2 deg or the face moves > 2 cm off the line) | no |
| 27 Grip 15/s in whole points; none -> PushOff | `WallGripSystem` (server, after the move) like `WaterstrideFluxSystem`: millionths owed while `MoveState.Wall == 1`, paid whole; can't pay -> a server order (`ServerOrder`, server seq) whose corners are the push-off arc | no |
| 28 clicks on a wall: Along / Off(Top) / Off(Ground) the shorter / jump off; Jump: a node > 2 m away -> JumpOnto, else JumpOff; refusals | `IWallPlanner.Plan` with the body on the wall (NGO `CurrentNode`: nearest node to feet + up x 0.38 within 2 m) | no |
| 29 leap (a rise > 3.5 m caught lower, 25 m, a clear way, apex +0.8, gravity 14, round a corner 1.5 m out) | two timed legs (NGO `JumpOnto`: Out / Linear, then In / Linear), then the wall corners; the planner's PhysX `JumpClear` | no |
| 30 jump off (lift 1.2, raised 1.5 up to 3 times, 25 m, not > 1 m up) | timed arc legs (NGO `AddArc`, `ArcClear` in the planner), phase PushOff, then the ground | no |
| 31 top-out (lift + mantle 0.4 s) / step-down (arc lift 0.25) / hold; then the walk on | timed legs at the route's end, then the NavMesh corners of the walk on (`route.Departure`) | no |
| 32 out of Grip: PushOff 3 m out (NavMesh within 3, else where the wall walk started), open ground | the Grip system asks `IWallPlanner.PushOff` (body PhysX + NavMesh) | no |
| 33 wall fights | M4 (combat) | later |
| 34 clicks ignored mid-climb; "Come down first." for travel | the step (no start during a timed leg); the travel refusal where travel is asked | partly |
| 35 death off the ground | the death part (not on DOTS yet) | later |
| 36 GM teleport lands on NavMesh | `TeleportRequest` clears `Wall` and the tilt | teleport yes, the clear no |
| 37 replication | `PlayerPose` + `PoseTilt` (with the phase) to all; corners and legs owner only; refusals to the owner (`ShowViewRefusal`) | no |
| 38 clips per phase, fades | the body's view (`IGhostMotionView`, `GhostJumpView`): plant fitted, mantle fitted, push-off flip, jump-on, wall move = the game's own locomotion turned on the face | no |
| 39 feet VFX `PFX_Wall_Loop`, Landed on the face | `Feel` calls from the same view, sounds for the own player only | no |
| 40 marker on the wall (> 50 deg) | `WorldMarkerPresenter` with the click's normal (the view branch passes it) | to check |
| 41 the graph (Zone01, preloaded) | read through `ZoneRules.Current.Entry.surfaceGraph` by the motor, on server and client bodies | yes (NGO side) |
| 2-24 straight climb, trees, catches, auto-arm | parts 2-5 | later |

Limits: `PathCorner` holds 32 corners today. A wall walk may use up to **64** (`PlayerLimits.MaxWallCorners`); a longer thinned route is
cut at the last node that fits and flagged `Truncated`: on arrival the server issues the rest as a server order from there (the next
order starts on the tick the last one ends, so the body never stops). The owner's own plan is cut at the same node.

### 3.4 Layers

`Player.Entities`: `WallPlan`, `WallLeg`, `PoseTilt`, the step, `IWallPlanner` (Plan, PushOff), `WallGripSystem`, wiring in the server
plan and the gather. `Player` (`PlayerWallRunMotor`, which already reads World's graph): public `PlanForGhost` / `PushOffForGhost`
returning plain lists (corners, legs) and NGO's refusal words, built from the motor's own `NodeAt`, `SurfaceRoute`, `JumpClear`,
`ArcClear`, `FeetAt`, `WallFacing` and the leg layout (`AddLeg` / `AddArc` numbers). `Persistence.Entities`: `BodyWallPlanner` (mm,
`BodyOf(world, ghost)`), installed in `PlayerWorldsSetup`. Presentation: the view and the follow system (no ground snap while
`Wall != 0` or a timed leg is under way). No new assembly references; `dependency_map.py --check` before the commit.

### 3.5 Steps (each with EditMode tests; the GUI proof with GIFs at the end of the part, announced to the Master)

- [x] 1. `WallLeg` maths (pure): timed legs with NGO's eases, wall segments at their speed, the tilt blend; the `Step` wall overload;
  world tests at 0 and 100 ms like `WaterWorldTests` (a stub planner: a straight 10 m wall, a roof).
- [x] 2. The planner on the motor (`PlanForGhost`: from the ground, along, off by top / ground, jump onto, jump off) + thinning + the
  face snap; tests on a handmade graph (no zone).
- [x] 3. Wiring: input (Wall + normal), the view-mode click, the server plan branch with NGO's refusal words, the owner's gather, the
  Grip system + push-off; world tests (Grip paid whole, out of Grip pushed off, a Stop on the wall does nothing, a click mid-plant
  starts nothing).
- [x] 4. Presentation: the tilt eased at 10, phases to clips, feet VFX, the marker on the wall, no ground snap.
- [ ] 5. GUI run on Zone01 (KnPlayHere `KN_PLAYHERE_WALL=1`): onto a wall from the ground, along it, over its edge onto a roof, down
  from the roof onto the wall (the way down), off it to the ground, a leap to another wall, a jump off, out of Grip (Flux set low)
  pushed off, clicks past the wall; pass / fail per try; GIFs to Drive `DOTS-proof`.
  Commits 5ff89281b, 6bce24e57, d92a4297d, 1a48d9672 (EditMode 235/235), script da617e6de + gate fix a9b05836f (gui-wall-1 never reached
  the script: the exit after the join's door knew only walk, jumps and vaults; one list of scripted parts now). GUI run gui-wall-2 (1 Nov,
  a wall at -10,-45 from the graph): planner check level 1 refused in NGO's words, level 60 planned; 9 of 9 tries PASS (onto with Run,
  along, over the edge onto the roof, from the roof down onto the face, off to the ground, Jump onto, Jump off, onto again, out of Grip
  pushed off), owner 0.0 cm from the server, no glide, tilt on the face; the plain click past the foot from the wall NOT TESTED (the RAM
  watchdog stopped Unity at 0.9 GB free with the recorder on). GIFs `DOTS-proof/dots-wall-1..4-*.gif`. To check in the GIF: the body
  passes an air-conditioner box on the face during "along" (possible clipping).
