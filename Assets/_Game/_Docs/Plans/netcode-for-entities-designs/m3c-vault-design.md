# M3c vault on the entity netcode: design (31 Oct 2026; Master's pick: the vault as a segment of the walk)

Plan section 55 item 3 is the acceptance list. This is how it is built.

## 1. What NGO does (the reference, `PlayerVaultMotor`)
- **Decide** (`TryArm`, now `TryPlan`, shared): the straight line to the destination leaves the NavMesh at an obstacle 0.18-2.2 m tall
  and at most 1.8 m thick, met at most 80 degrees off square. There must be room to take off (the style's run-up, or the NavMesh edge)
  and to land (the style's landing distance, standing room clear, within 0.5 m of the ground's height), a clear crossing above the top,
  a walkable way on from the landing, and going over must be no longer than walking round (`minimumDistanceSaved`, 0 today).
- **Run**: the walk goes to the take-off and the vault starts a little before it, without stopping (speed carried). The warp moves
  the root evenly along the ground from take-off to landing, over `airTime = clamp(distance / speed, style window, clip length - 0.02)`.
  The height follows a 4-key curve with flat tangents: up to `raise = max(0.05, top - style.poseClearance + clearance)` by
  `riseEnds`, level, and down from `fallStarts` (`VaultTiming`, shared). The clip is the faction and body's style for the height
  (`VaultAnimationMap`), played in place over the style's window.
- **Land**: the walk carries on from the landing to the destination (`ResumeMoveAfterVault`).
- **A click during the vault is ignored**: `ServerMoveTo` and the client both return false while the vault is busy. The walk after
  the landing goes to the original destination. (Master asked "finish the vault, then the new path". NGO drops the new click, and
  the owner's bar is exact NGO feel, so this design follows NGO. To confirm with the Master.)

## 2. The DOTS shape: a vault leg in the walk's path
- The planner (server `PlayerServerPlanSystem`, owner gather) asks an `IVaultPlanner` (Persistence.Entities adapter over
  `PlayerVaultMotor.PlanForGhost`) about each click's straight line. With a vault, the corners are: the path to the take-off, then
  take-off -> landing (the vault leg), then the path from the landing to the destination.
- `VaultPlan` (ghost component, replicated to all; and `ClientVaultPlan` for the owner's own, not a ghost field):
  `Seq` (the order number it belongs to), `LegStartMm` (walk progress at the take-off), `LegLengthMm`, `AirMicros`, `RaiseMm`,
  `RiseEndsPermille`, `FallStartsPermille`, `Style` (an index into the body's resolved styles; presentation only). Integers only.
- `PlayerMovement.Step`: while the progress is on the leg, the speed is fixed at `LegLengthMm / AirMicros` (no acceleration, no
  arrival slowdown: the take-off is mid-path), and the pose's height is the leg's straight line plus `VaultHeightMm(share)`. Share
  = progress along the leg; it equals the time share because the leg's speed is constant. The height is the flat-tangent curve, in
  integers: smoothstep up to `RaiseMm` by `RiseEnds`, level, smoothstep down from `FallStarts`. Stateless: the pose at a tick is a
  function of the progress, so a rollback or a skipped tick can never leave it wrong, and nothing is triggered at run time.
- Walk clicks are refused while the progress is on a vault leg (NGO's rule above): the server refuses with `RefusedSeq` as for
  an airborne jump, and the owner does not start them.

## 3. Presentation
- The follow draws the pose at the frame's sub-tick time (`PoseAhead`, along the leg's curve too). There is no ground snap on the leg (as in a jump).
- The clip: when the drawn progress enters the leg, `PlayerAnimancerMotor.PlayWarpedAction(style.clip, window.x, window / air, fadeIn)` starts
  at the right offset (the clip time from the share), once per `VaultPlan.Seq` (a rollback never restarts it); at the leg's end
  `EndWarpedAction(fadeOut, speed)`. Feel: `JumpTakeoff` / `Airborne` / `Landed` with `BodyRoleTag.IsMine`.
- Observers: the replicated pose carries the height; `VaultPlan` gives the clip and timing at their interpolation tick.
- GUI log (the Master's must-have): per vault, the owner's and an observer's clip-start tick and share, side by side.

## 4. Steps (each with its proof, at most 2 runs)
1. Rules lift: `TryPlan` / `PlanForGhost` / `VaultTiming` in `PlayerVaultMotor` (NGO unchanged): `VaultSourceTests`. DONE with this design.
2. `VaultPlan` + `VaultHeightMm` + `Step` on the leg: pure tests (curve end points, the level stretch, the leg's speed, a skipped
   tick) and world tests at 0 and 100 ms (the owner and server poses equal on the leg, a click during the leg refused).
3. `IVaultPlanner` adapter + the planners' path splice, server and owner: world tests with a stub planner, then the real one in the GUI.
4. Presentation (clip, feel, no ground snap) and the KnPlayHere scripted vault (a known obstacle near the Zone01 spawn, with
   frames, compared against NGO's vault over the same obstacle).

## 5. NGO vault rules (owner, 31 Oct: DOTS must vault exactly what NGO vaults)

Source: `Gameplay/Player/Runtime/PlayerVaultMotor.cs` (line numbers on 31 Oct), numbers from `Player.prefab` and `TraversalSettings`.
DOTS runs the same code (`PlanForGhost` -> `TryPlan`) on the server body and on the owner's view body. There is no vaultable marker,
tag or component: an obstacle is vaultable by its shape alone.

| # | Rule (NGO) | Source | Number | DOTS: same code? | Does the DOTS body have what it needs? |
|---|---|---|---|---|---|
| 1 | the motor is set up (data) | 213, `PlanForGhost` | animations + traversal + warp template | yes (data-only check for a server body) | yes: serialized on the motor, kept (disabled) on server/view bodies |
| 2 | the straight line to the destination leaves the NavMesh (a NavMesh edge on the way) | 242 `NavMesh.Raycast` | agent's area mask | yes | agent kept (disabled): area mask readable. Zone01 NavMesh identical to main (no diff) |
| 3 | moves under 1 m flat are never vaults | after 242 | 1 m | yes | yes |
| 4 | the ground height in front of the edge (PhysX down-ray) | 254 | 1.5 m ray, player raycast layers | yes | layers = `PointAndClickPlayer.raycastLayers` (bits 73: layers 0, 3, 6); kept on bodies |
| 5 | an obstacle face at 0.85 x min height, straight out from the NavMesh edge (or along the travel), within 3.2 m | 502-506 | min height 0.18 m | yes | needs the planter's COLLIDER on a raycast layer; the hedge prefab itself has none, the planter's collider is on its stone mesh (layer to confirm in the run) |
| 6 | never a character (an agent or something damageable) | 508 | - | yes | DOTS has TWO bodies in one process (server + view): their colliders are characters, skipped |
| 7 | met at most 80 degrees off square | 510-515 | 80 deg | yes | yes |
| 8 | top 0.18-2.2 m, thickness up to 1.8 m, ground again behind it (no drop) | 520-541 | 0.18 / 2.2 / 1.8 m, ground tolerance 0.12 m | yes | colliders + layers as rule 5 |
| 9 | room to take off: standing in front within the style's run-up, or the run-up spot on the NavMesh, or the NavMesh edge within 3 m | 260, 430-452 | maximumRunUp 3 m, aside 0.5 m | yes | yes |
| 10 | room to land: the style's landing distance (+2.4 m search) on the NavMesh, within 0.5 m of the ground's height, standing room clear, nothing taller than 0.3 m in the way down | 262, 460-478 | landingSearch 2.4, step 0.3, landing step 0.5 m | yes | capsule check uses the agent's radius/height (kept) |
| 11 | the crossing above the top is clear (a box 1.1 m tall) | 264, 553 | clearance 0.12 m | yes | yes |
| 12 | a walkable way on from the landing | 268 | NavMesh path | yes | yes |
| 13 | going over is not longer than walking round | 272-273 | minimumDistanceSaved 0 | yes; DOTS passes its own walk's length round | DOTS walk length = the walk planner's corners (should match the agent's path) |
| 14 | the style (clip) for the height: low < 0.75 m <= middle < 1.35 m <= tall | `TraversalSettings.HeightOf` | 0.75 / 1.35 m | yes (class sent, each body resolves its clip) | the view body resolves with its own faction (as NGO) |
| 15 | a click during a vault is ignored; the walk goes on to its destination | `PointAndClickPlayer` 940, 1378; `ResumeMoveAfterVault` | - | yes (refused on the leg, server and owner) | - |

Which rule fails on the spawn planters (GUI run gui-vault-2: "none found" 5 times): not known from that run. It logged no reasons.
The next run logs the planner's own reason per candidate, and straight across the three planters nearest the spawn
(`HedgeGoals`). That names the failing rule by NGO's own words before any fix.
