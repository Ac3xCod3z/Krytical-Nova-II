# Online phase 3: responsive input (client anticipation)

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** Needs Phase 1 (a host never feels the
delay, so this can only be judged against a dedicated server).

## Goal

A player's own character starts moving, jumping and casting the moment they press, at any normal
connection delay. The server still decides everything; when it disagrees, the client is corrected
smoothly. Nothing here gives the client any authority.

## Why, and what Netcode for GameObjects offers

Today the owner's character moves only when the server's position arrives, and a cast animation starts
only when the server's reply arrives. With 100 ms of delay each way that is a fifth of a second of nothing
after every click.

Netcode for GameObjects has no full prediction with rollback. It has **client anticipation**: the client
shows what it expects and the package tells it when the server's answer arrives so it can correct
(`AnticipatedNetworkTransform`, `AnticipatedNetworkVariable`). For a click-to-move game with
server-decided hits, that is enough.

## Related design

`Design/15_movement_wasd_controller_navworld.md` (11 Oct 2026) reaches the same conclusion from the
movement side: the delay is the network model, owner-side prediction is "worth doing whether or not WASD
ever is", and WASD and a controller cannot work without it. This phase is that document's step 2. If the
Keys mode of its section 4 is built, the direction a client sends many times a second is a new
client-to-server call: it goes on the audit list of `online-security-model.md`, and the server replays it
at the server's own speed.

## What exists today

| Piece | Where | Behaviour |
| --- | --- | --- |
| Body position | `Gameplay/Player/Prefabs/Player.prefab`: `NetworkTransform`, server authority, interpolation on | The owner sees its own body through the same delayed stream as everyone else |
| Click to move | `Gameplay/Player/Runtime/PointAndClickPlayer.cs`: owner sends `RequestMoveDestinationServerRpc` (990); server plans in `ServerMoveTo` (1014–1055) | The owner shows only a move marker (667). The `NavMeshAgent` is switched off on every machine but the server (223–227) |
| Jump | `Gameplay/Player/Runtime/PlayerJumpMotor.cs`: `TryJump` already checks the jump locally (107–121), then waits for `JumpStartedClientRpc` | `RemoteStartDelay = 0.1f` on other machines |
| Vault, death | `PlayerVaultMotor.cs`, `PlayerDeathMotor.cs` | Server simulates, others replay |
| Skill cast | `Gameplay/Progression/Runtime/PlayerCombatSkills.cs`: owner runs `CheckCast`, sends `RequestCastSkillServerRpc`; animation, cooldown and buff start on `TriggerSkillCastClientRpc`; failure returns by `NotifyCastFailedClientRpc`; `CastRequestTimeout = 0.5f` | The caster waits |
| Charge pose | `Gameplay/Combat/Runtime/PlayerSkillAttacks.cs` (360–385) | The one thing that already plays at once on the owner |
| Basic melee | `Gameplay/Combat/Runtime/PlayerMeleeCombat.cs`: the server sequences the combo and sends `PlayComboStrikeClientRpc` | The owner follows |

## Work items

1. **Decide the movement method with a one-day test.** Two ways:
   - **A. `AnticipatedNetworkTransform`** replaces `NetworkTransform` on the player. The owner keeps a
     working `NavMeshAgent`, walks its own path on click and reports it with `AnticipateMove`; when the
     server's position arrives it is compared and, past a small distance, the owner is eased onto it.
   - **B. The body stays as it is and only the model leads.** The networked root keeps following the
     server; on the owner, the visible model walks ahead on a local path and is eased back to the root.
   **Prove first:** how other players' characters are smoothed under A (the anticipated component handles
   interpolation differently from `NetworkTransform`). If observers look worse, use B.
2. **Owner-side walking.** On a click the owner plans the same path the server will (same NavMesh, same
   speed, same Run rule) and starts at once. It must know its own speed exactly: walk, Run, Move Speed
   from gear and buffs, slows. Any of those that are not already sent to the owner are added.
3. **Correction.** Small error: keep going. Larger: ease over a short time. Very large (a teleport, a
   refusal, a knockback): snap. The three distances and the easing time are tunables.
4. **Things the server moves are never anticipated.** Autopilot, Follow, the walk-in of a queued skill
   (`PlayerSkillQueue`), knockback, pulls, death, a GM teleport. While one of those is running the owner
   follows the server as now.
5. **Jump and vault.** The owner starts its own jump when it sends the request (its local check has
   already passed) instead of waiting. `JumpRejectedRpc` blends it back out. Other players keep the
   existing delay.
6. **Casts.** On pressing a skill the owner starts the cast animation and shows the cooldown and Flux
   change at once, marked with a request number. `TriggerSkillCastClientRpc` confirms it (the owner does
   not replay it); `NotifyCastFailedClientRpc` cancels the animation and puts the Hotbar back. The same
   for the first swing of a basic attack.
7. **Never shown early:** damage numbers, HP, a kill, loot, Creds, XP, a buff's effect. Those appear when
   the server says so.
8. **Speed and teleport checks stay on the server.** Add a test that a client claiming to be somewhere
   else changes nothing: the server never reads a position from a client under either method.
9. **Measuring.** The Multiplayer Tools package (installed, 2.2.12) has a network simulator. Test at
   60, 150 and 250 ms with 2 % of packets lost, against the dedicated server, with a second client
   watching.

## Tunables

A new `NetworkFeelSettings` asset: the keep / ease / snap distances, the easing time, the most a model may
lead its server position, the cast-confirm timeout (today 0.5 s in code).

## Done when

- At 150 ms: a click moves the character on the next frame; a skill's animation starts on the press; a
  jump leaves the ground on the press.
- Walking a straight line at 150 ms shows no visible snapping; a refused move or cast corrects cleanly.
- A second client sees the same quality of movement as before this phase.
- Autopilot, Follow, queued skills, knockback and death look the same as before.
- The server's position for a player is never taken from that player's client.

## Risks

- The owner's guess and the server's path differ at corners and crowds: the tunables hide small
  differences, and a bad choice of them looks like rubber-banding.
- Swapping the transform component on `Player.prefab` touches every system that reads the player's
  position. Method B avoids that and is the fallback.
