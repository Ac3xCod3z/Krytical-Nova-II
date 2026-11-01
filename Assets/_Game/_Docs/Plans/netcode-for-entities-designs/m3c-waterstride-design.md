# M3c Waterstride on the entity netcode: design (1 Nov 2026)

Plan section 55 item 4 is the acceptance list. Owner's standing rule: the NGO rules table comes first (section 1), then the
design (sections 2-4), then the code. The bar is NGO's behaviour exactly, with the server still deciding.

## 1. NGO Waterstride rules (what DOTS must do the same)

Source `Gameplay/Player/Runtime/PlayerWaterstrideMotor.cs` (line numbers on 1 Nov) unless named. Numbers come from
`Gameplay/Player/Data/Assets/WaterstrideSettings.asset`. The two jump-onto-water numbers are not in the asset, so the class
defaults apply: fall-in 0.15 of Max HP, shore within 80 m. Scene data: `ZoneRules` (a scene object) -> `ZoneCatalog` entry ->
`waterMap`. Zone01's map is `World/Data/Assets/WaterMaps/Zone01_StarterTown_WaterMap.asset`: water at -1.20 m, 1 m cells,
1000 x 1000 from (-500, -500), 52,869 water cells, at least 0.35 m deep, room for a body of 0.3 m. There is no water collider, no
layer and no marker: the water is the map's bits plus its height. The NavMesh is only used for the shore.

| # | Rule (NGO) | Source | Numbers | DOTS: has it? |
|---|---|---|---|---|
| 1 | open from level 60 with Run and Jump at rank 20 | 66-68 | 60 / 20 / 20 | server body: yes (progression, skills). Owner: ranks yes (`MoveRanks`), **level no**: add it, owner only |
| 2 | a click is a Waterstride click only in a zone with a water map, and not with Jump selected (that is a jump onto or off the water) | 139-142 | - | map: yes (`ZoneRules.Current` is a scene object, loaded in the server and client worlds' process and in a server build). Jump: `PlayerInput.Jump` |
| 3 | from land, only with Run selected and Waterstride open; on the water, every click is Waterstride's (over the water, or ashore) | 143-161, pointer 806 | - | Run per click (`PlayerInput.Run`). **The view body skips this branch** (`!ViewMode`): a water click goes on as a ground click on the lake bed, and the NavMesh refuses it. Needs the view branch |
| 4 | the click point: the camera ray meets the water's plane before its first collider hit (+0.05 m), and the cell under it is water; the move marker shows there | 145-155, pointer 812 | water level | client: same code in the view body's branch, sending the surface point |
| 5 | refusals, in this order, in these words: "Waterstride opens at level 60 with Run and Jump at rank 20." / "No HP remaining." / "Finish this move first." (jump, vault, wall busy) / "Select Run to run on water." / "Not enough Flux to run on water." (under one second's 3 Flux) / "Out of Flux: back to the shore first." (stranded) / "That water cannot be run on." | `CanStride` 248-258, 192-196 | 3 Flux | server body: all yes. Owner: ranks, level (rule 1), Flux (`OwnerFlux`), its own jump/vault state |
| 6 | from land: on the NavMesh ("Waterstride must start on walkable ground."); the water's edge along the straight line feet -> click, sampled every 0.5 m up to 80 m ("There is no shore on the way to that water."); the shore = NavMesh within 4 m of the last land sample ("There is no shore to run out from there."); the entry = the water cell nearest the first water sample within 4 m, and a route from it to the click ("No way over the water there."); a complete NavMesh path to the shore ("There is no way to that shore.") | 206-215, `EdgeAlong` 264-285 | 0.5 / 80 / 4 m | static NavMesh API (as `NavMeshPathPlanner`), water map: yes |
| 7 | the walk to the shore is the agent's walk at the current speed: Run speed, with acceleration; **no Run Flux is charged** (Waterstride sets the path itself); another order calls it off | 216-221, `WatchToShore` 347-354 | - | the DOTS walk (corners) with the click's Run speed; the server must not charge `RunCost` for a water click |
| 8 | the water run starts 0.7 m before the shore point (or at once if the feet are already within 0.7 m) | 216, 352 | 0.7 m | a leg start at (path length to the shore - 0.7 m) |
| 9 | on the water: the agent is off; constant speed max(1, ground speed x 1.0) (Run speed), no acceleration and no slowing to stop; the body turns at most 720 deg/s towards the way; feet at the water's height | `Update` 307-344, `Face` 510-516 | share 1.0, at least 1 m/s, 720 deg/s | a fixed-speed leg (like the vault's) at the water's height; DOTS turn step to check (`TurnStepCdeg`) |
| 10 | the route: A* over the cells, 8 ways, never across a corner of land, at most 60,000 cells searched, then pulled straight wherever a line stays over water (sampled every 0.25 m) | `World/Runtime/Water/WaterRoute.cs` 26-108 | 60,000 cells | `WaterRoute` is plain C#: the same code on both sides |
| 11 | at the route's end the player stands on the water (no move), still paying Flux | 343, 313 | - | needs a stored "standing on water" (a ghost field) |
| 12 | a water click while on the water: from the nearest water cell (within 4 m of the feet) straight to the new place; no shore | 198-204 | 4 m | same |
| 13 | a land click from the water: NavMesh within 2 m of it ("Choose a walkable ground or platform surface."); the edge on the line from that place back to the feet -> the shore within 4 m, else the shore nearest the place within 4 m ("There is no shore near there."); run there over the water, step ashore, then walk on to the place (more than 0.5 m away) with **no Run Flux** (`ServerMoveTo(payRun: false)`) | 224-246, `Land` 387-407 | 2 / 4 / 0.5 m | same planners, one spliced path: water leg, then the NavMesh walk |
| 14 | Flux: 3 a second while on the water (running or standing), counted from the first moment on it and paid a whole point at a time | `PayForTime` 356-366, `RunRoute` 299 | 3 Flux/s | the server body's `PlayerResources` (as `BodyJumpCost`), on the server only |
| 15 | out of Flux: carried back to the NavMesh nearest the feet within 80 m, over the water at 6 m/s, told "Out of Flux: back to the shore."; clicks are refused on the way; ashore it stops (no walk on) | `Strand` 368-385, 194, 229 | 80 m, 6 m/s | a server order (`ServerOrderRequest`) with a stranded flag and speed; the message waits for M8's route (plan 52) |
| 16 | ashore: the agent is on again, snapped to the NavMesh within 2 m | `Land` 387-404 | 2 m | the walk's next corner is on the NavMesh already |
| 17 | killed on the water: the run ends where it is; the death takes the body | `ReleaseForDeath` 418-425, `PlayerDeathMotor` 220, 280 | - | DOTS death comes with M4: the leg ends at death then |
| 18 | a GM teleport (or a map change) ends it on the nearest shore | `Cancel` 410-416, `GmCommands` 883 | 80 m | DOTS `TeleportRequest`: it must clear the water state |
| 19 | a jump from the water: the stand ends at take-off, with a big splash at the take-off; a jump called off before take-off puts the body back on the water | `PlayerJumpMotor` 228, 600, 679, 829-834 | - | the DOTS jump: end the water state at take-off |
| 20 | a jump onto water (the click within 0.3 m of the water's height and over a water cell) lands on its surface; then, with Waterstride open and Flux for a second, the player stands on it; otherwise they fall in: lose 0.15 of Max HP (never the last point), come out on the NavMesh nearest within 80 m, told "You fell in the water and climbed out on the shore (-N HP)." Splash either way | `TryWaterLandingAt` 436-443, `ServerLandedOnWater` 460-474, `FallIn` 477-491 | 0.3 m, 0.15, 80 m | **DOTS today lands on the water and just stands, for free, open or not** (`JumpPlanRules.Landing`): wrong; fixed in this step |
| 21 | while on the water the pointer does not walk, recover the agent, count a stall, or change mode; anticipation (owner lead) holds | pointer 496, 942, 1063, 1278, 1323, 1380, 1475, 1958 | - | the DOTS walk has no agent; the gather must not plan a NavMesh walk from the water (rule 3) |
| 22 | everyone sees the wake at the feet while on the water (`netOnWater`, a NetworkVariable: late joiners see it too); each foot strike on the water splashes for all, with sound for the own player only; a big splash on jumps on or off and falls in, sound for the own player only; run and idle play from the travel as on land | 40, 107-127, 494-503; `GameFeelPresenter` 750-770; `ActorVfx` 260-285 | - | the replicated water state on the ghost -> the view body calls `Feel.WaterStriding`; `IsMine` for the sounds (plan 52) |
| 23 | the click budget (Move kind) and the point check apply to the water RPCs | 164-178 | - | the DOTS click budget (`IClickBudget`) and range check already apply to every click |
| 24 | the Arsenal's Waterstride card is locked until open | `ProgressionHUD` 1153-1171 | - | owner: level + ranks (rule 1) |

## 2. The DOTS shape: the water as a leg of the walk (as the vault)

- The planners (server `PlayerServerPlanSystem`, owner gather) ask an `IWaterPlanner` first, for a click whose goal is a water surface
  point or that comes while the body is on the water. Persistence.Entities fills it over the server body or the owner's view body,
  with the motor's own rules lifted into a shared static (`PlayerWaterstrideMotor.PlanForGhost`; NGO unchanged). It answers the
  corners and the leg: way to the shore (NavMesh), then the water route (corners at the water's height + the base offset), then,
  for a land click, the way on (NavMesh).
- `WaterPlan` (ghost component, replicated to all; the owner's own in `ClientWaterPlan`): `Seq`, `LegStartMm` (progress where the run
  onto the water starts, rule 8), `LegEndMm` (progress where it steps ashore; int.MaxValue = ends standing on the water),
  `SpeedMmps` (fixed, rule 9), `Flags` (Stranded). Integers only, stateless: on the water at a tick = the walk under way is that
  order and its progress is in [start, end), or the walk has ended with the leg ending on the water (standing).
- `PlayerMovement.Step`: on the water leg the speed is fixed (no acceleration, no slowing at the end), the turn as rule 9. The vault
  leg's overload is widened to take the water leg too.
- Flux (server only, a new `WaterstrideFluxSystem` after the move): each tick on the water adds 3 x tick seconds to the owed
  thousandths and pays whole points through the server body's resources. When it cannot pay, it requests the stranded order
  (rule 15). The owed amount sits on a server-only component, so a rollback cannot pay twice (the server never re-simulates).
- A water click is not charged `RunCost` (rule 7). The walk-on after stepping ashore is part of the same order, so it is free too (rule 13).
- Jump: at take-off the water state ends (rule 19). The server's jump landing on water (rule 20) asks the planner whether to stand
  (`WaterPlan` standing, from the landing tick) or fall in (HP loss on the server body, then the same as a teleport to the shore).
- Owner: `MoveRanks` gets the level (owner only), so the owner refuses with NGO's words before sending (rules 1, 5, 24).

## 3. Presentation
- The view body follows `WaterPlan` (the owner's own first, as for the vault): on the water -> `Feel.WaterStriding(body, true)` once,
  off -> false. Splashes on jumps on and off the water and on falling in, with sounds only for `BodyRoleTag.IsMine` (plan 52).
- No ground snap while on the water (the pose's height is the water's).
- The view body's click branch (rule 3): in view mode a Waterstride click sends the surface point (`BodyClicks.Ground(surface, run)`),
  and the marker shows there; the owner's planner then sees a water goal.

## 4. Steps (each with its proof, at most 2 real runs)
1. Rules lift: `PlanForGhost` (land -> water, water -> water, water -> land, the stranded way) and `LandedOnWater` decision as statics
   in the motor, NGO unchanged: EditMode `WaterstrideSourceTests` on a small handmade `WaterMap` plus a flat NavMesh-free stub for the
   shore (the static NavMesh calls are behind one delegate).
2. `WaterPlan` + the leg in `Step` + `WaterstrideFluxSystem` + the stranded order: pure tests (fixed speed, standing, owed Flux paid
   whole, never twice) and world tests at 0 and 100 ms (owner and server poses equal on the water, a click while stranded refused,
   Flux run out -> back to the shore at 6 m/s).
3. Planners' splice on both sides (`IWaterPlanner`, the gather and the server plan), the view click branch, the level on `MoveRanks`,
   the jump hooks (rules 19, 20).
4. Presentation (wake, splashes) and the KnPlayHere scripted run in Zone01: a run out from the shore to a water point, a water ->
   water click, a land click from the water (walk on), clicks past the water's edge, Flux run out, a jump onto water open and not
   open (fall in). Each against NGO's behaviour over the same points, GIFs from the real GUI Editor.
