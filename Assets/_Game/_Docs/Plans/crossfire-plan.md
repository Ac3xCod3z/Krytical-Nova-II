# Crossfire: the hourly war (owner's DAY list item 17)

Owner, 13 Nov 2026: the hourly faction war is called **Crossfire** (it replaces "Siege Night" in player-facing text; identifiers
may stay). It runs by itself on the server: it calls players, opens on time, runs for a set length and ends. Every time is in a
settings asset; GM controls (start now, skip, end) in the GM window and on the control page.

This plan is a checklist: tick each step as it lands.

## 1. Rules table (from the design; nothing of it is built)

| Rule | Source | Value / meaning |
| --- | --- | --- |
| A war every hour, at nightfall | `Design/World-Atlas.md` L1437-1449 | one game day = one real hour (`WorldEnvironmentState.DayLengthSeconds`), so nightfall comes once an hour |
| Dusk call | Atlas L1445 | about 10 real minutes before nightfall a repeating notice to everyone in the bracket |
| Entry window | Atlas L1446 | the last 3 minutes of dusk; the queue is split into even wars of 33 a faction |
| Rally | Atlas L1447, L1474-1475 | at nightfall everyone stands in their faction's walled Rally Point; 1 minute with the gates shut |
| War | Atlas L1448 | the Rally (Siege) Gates open: 15 minutes of battle on the Siege Ground |
| End | Atlas L1449 | results, rewards, everyone back where they entered |
| Respawn | Atlas L1474 | the dead come back in their own yard |
| Cap and balance | build list row 28, Atlas L1439 | 33 a faction at most; opens with no more than 5 missing on a side; Mercs even the sides |
| Brackets | Atlas L1455-1468 | ten levels each from 10 (I 10-19 ... X 100-112); no war under level 10; each bracket's war is its own server copy |
| Battlefields | Atlas L1483-1595 | Hollowgate (I-II), Cinderford (III-IV), The Saltglass Redoubt (V-VI), Stormbreak Bastion (VII-VIII), The Crucible (IX-X) |
| Yard shape | Atlas L1472-1478 | four yards N/E/S/W, ~50 x 40 m, 10 m walls; a 12 m gate facing the middle, shut until the war starts; the fourth yard sealed (Ronin) |
| Fixed night light | Atlas L1451 | the battlefields' clock does not run inside them |
| Always-on PvP inside | `ZoneCatalog.ZonePvpRule.AlwaysOn`, `CombatHostility` | other factions hostile whatever gm.pvp says |
| Name | owner, 13 Nov 2026 | **Crossfire** in UI text (notice, HUD, GM window, control page, news) |

## 2. What exists to build on

- The world clock: `WorldEnvironmentState` (`World/Runtime`), `TimeOfDay01(now)`; sent to clients by `WorldStateServerSystem` /
  `WorldStateClientSystem` (`World/Entities`), a reliable RPC on change and on join. Crossfire's state goes the same way.
- `NetClock.Now` (Core): the shared clock, so a client works out every timer itself from a phase's start and end.
- Teleport on the server: `PlayerEntitiesSetup.Teleport(serverEm, ghost, mm, yaw)` (sending everyone back).
- GM commands: QC `[Command("gm.*")]` in `Tools/Debug/GmCommands.cs`; the GM window runs them (`GmToolsWindow.Run`); the control page runs
  any line through `/api/command` (`KnServerPage.CommandPath`), so the page needs only buttons in `ControlPage~/app.js`.
- Walking is planned on the NavMesh on the server and the owner's client (`NavMeshPathPlanner`), so a shut gate is a carved
  `NavMeshObstacle` on both.
- Nothing else: no Siege Ground scene, no Rally Point, no Marshal entry, no bracket server copies, no `ZoneKind` for a war map.

## 3. Phases (one clock, on the server)

`Gap -> Call -> Muster -> War -> End -> Gap`. The clock anchors on the world clock's nightfall (`nightfallHour`), so the war meets the
night the Atlas wants; with `anchorToNightfall` off it runs every `cycleMinutes` from the server's start (test servers).

| Phase | Starts | Lasts | What players see / what the server does |
| --- | --- | --- | --- |
| Call | `callMinutes` (10) before nightfall | until nightfall | "Crossfire at nightfall" notice to eligible players (level >= `minLevel` 10), repeated every `callRepeatSeconds` (60); a countdown on the HUD; the last `joinWindowMinutes` (3) the yards take players in |
| Rally | nightfall | `musterSeconds` (60) | gates shut, "the gates open in N"; a player who steps into a yard is recorded (entry point) |
| War | rally end | `warMinutes` (15) | gates open; HUD shows time left |
| End | war end | `endSeconds` (20) | gates shut; after `endSeconds` every recorded player still in the war area is sent back to their entry point |
| Gap | end over | to the next Call | nothing shown |

All numbers above (and `minLevel`, `bracketSize` 10, `maxLevel` 112) live in `CrossfireSettings` (a ScriptableObject); none in code.

## 4. Pieces, by system and layer (no new machinery)

| Piece | Where | Layer |
| --- | --- | --- |
| `CrossfireSettings` (SO) + `CrossfireRules` (pure: phase at a time, next nightfall from the world clock, bracket of a level, eligible) + `CrossfirePhase` | `Gameplay/Crossfire/Runtime` (`KryticalNova.Crossfire`, refs Core, World) | Simulation |
| `CrossfireClockServerSystem` (the clock, GM overrides, entry records, send-back), `CrossfireStateRpc` (phase, start, end, next war, cycle number) sent on change and on join, `CrossfireClientSystem` (keeps `CrossfireView.Current` for presentation) | `Gameplay/Crossfire/Entities/Runtime` (`KryticalNova.Crossfire.Entities`) | Simulation |
| `MusterGate` (MonoBehaviour: a carved NavMeshObstacle + the door's open/shut look, reads `CrossfireView`/server state) and `MusterYard` (a box: who is inside, entry point, faction) | `Gameplay/Crossfire/Runtime` | Simulation |
| HUD: the call banner + countdown / time left | `UI/Runtime` (reads `CrossfireView`) | Presentation |
| `gm.crossfire start / skip / end / status` | `Tools/Debug/CrossfireCommands.cs` | Tooling |
| GM window box (phase, time left, three buttons) | `Tools/Editor/GM/GmToolsWindow.cs` | Tooling |
| Control page buttons (the same three lines through `/api/command`) | `Networking/ControlPage~` (`index.html`, `app.js`) | Infrastructure (files only) |

## 5. Steps (code first, Unity after)

- [ ] 1. This plan (PR).
- [ ] 2. `CrossfireSettings` + `CrossfireRules` + tests (pure: every phase boundary, nightfall wrap-round, GM skip / end, brackets).
- [ ] 3. Server clock + state RPC + client view + tests in a `KnTestWorld` (a client gets the phase on join and on every change).
- [ ] 4. `MusterGate` / `MusterYard` (entry record, send-back at End) + tests.
- [ ] 5. HUD call and timer; `gm.crossfire` commands; GM window box; control page buttons.
- [ ] 6. Unity (coder-4's slot): make the settings asset, a **test Rally Point pair** (two yards with gates facing a small field) at a
  spot planned beforehand in a test map, then the two-client proof: both get the call, wait at shut gates, gates open on time, the war
  ends on time and both go back; GM forces each phase. GIF for the owner.
- [ ] 7. Docs: Encyclopedia (glossary "Crossfire", row 28), Server Guide (GM commands), launcher news.

## 6. Not in this item (later, each its own plan)

The five battlefields as maps, the Marshal entry and the queue (33 a faction, even wars, Mercs), one server copy per bracket and the
travel there and back, rewards and results, respawn in the yard, the sealed Ronin yard. Until then Crossfire runs on whatever map holds a
Rally Point (the test pair), and "back where they entered" is the spot where the player stepped into the yard.
