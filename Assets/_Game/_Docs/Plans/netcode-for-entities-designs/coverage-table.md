# DOTS migration: SYSTEM COVERAGE TABLE (NGO -> Netcode for Entities)

Written 29 Oct 2026 by a read-only research worker. Nothing in either repo was edited. Method: asmdef/asmref walk of
`Assets/_Game` (59 asmdefs = 35 runtime/editor + 24 `*.Tests.Editor`, plus 41 asmrefs) in the real project, a per-file
regex count of `NetworkBehaviour` / `[Rpc]` / `NetworkVariable` / `NetworkList` / `INetworkSerializable`, the Build List
rows 1-67, `Dependency-Map.md`, `ProjectOrganization.md`, and every DOTS design note in this folder. Graphify was
consulted first (node check on `NetworkBehaviour`); counts below come from the scripted walk.

## 0. Read this first (three honesty flags)

1. **The plan file does not exist yet.** `Krytical Nova II DOTS/Assets/_Game/_Docs/Plans/netcode-for-entities-migration.md`
   is absent (checked twice, also by name search over the whole clone). No `designs` folder exists beside it either.
   So the "plan" used here is the owner's 20 Oct draft (`C:\Users\dlaco\.claude\plans\i-need-you-to-magical-parasol.md`,
   Stages 0-10, 28 owner questions) plus the 29 Oct design notes in this folder (ghosts, monster AI, persistence/UI
   bridge, Linux/Burst, test harness, owner questions). Where this table says "the plan does not mention", it means
   those sources. Re-run the "not mentioned" check (section 5) when the real plan lands.
2. **Milestone numbering is not settled across the sources.** The test-harness note uses M1 bootstrap, M2 movement,
   M3 combat, M4 hostiles+drops, M5 economy, M6 persistence+roster+world/travel, M7 UI bridges, M8 load, M9 Linux parity.
   The owner-questions note says "feel checkpoints after **M3a (movement)** and **M4 (combat)**". The 20 Oct draft uses
   Stages 0-10. This table uses the harness numbering, adds **M0** (docs, seams on NGO, spikes) and **M10** (cutover),
   and adds sub-milestones that no source has (marked **NEW**). A crosswalk is in 0.1. The plan's own numbering must
   win; only the "Milestone" column needs re-mapping.
3. **Counts are scripted and approximate.** Regex finds **63** `: NetworkBehaviour` classes in runtime code (plan says
   59, Dependency-Map 25 Oct says 65; 3 are newer than the surveys: `PlayerRename`, `PlayerGmSight`,
   `PlayerWaterstrideMotor`), **244** `[Rpc]/[ServerRpc]/[ClientRpc]` text hits (plan: 216; the regex also catches doc
   comments), and NetworkVariable/NetworkList hits double-count `new NetworkVariable<` initialisers, so values shown as
   "nv~N" are halved. Use the plan's 59 / 216 / 49 / 37 as the headline; use the per-row numbers only for relative size.

### 0.1 Milestone legend and crosswalk

| This table | Meaning | Test-harness plan | 20 Oct draft |
|---|---|---|---|
| M0 | Docs, seams on NGO (PlayerKey, NetRole, X.cs/X.Net.cs split, NetClock, unmanaged RPC args), spikes (S1-S12, Linux IL2CPP+Burst empty server) | (none) | Stage 0-1 |
| M1 | Packages, bootstrap, connect/approve, RpcBudget re-key, roster transport, test harness H1-H5 | M1 | Stage 2 |
| M2 | Player ghost: input, point-and-click movement, prediction, motions, server body / presentation body, SaveAgent+Identity+Equipment edge, Look | M2 | Stage 3 (owner "M3a" feel checkpoint) |
| M3 | Combat: Vitals, melee, skills, lag compensation, HitEvent ring, Combat Dummy | M3 | Stage 4 (owner "M4" feel checkpoint) |
| M4 | Hostiles ECS brain + zone spawner + WorldDrop ghosts | M4 | Stage 5 + part of 6 |
| **M4b NEW** | World singletons: clock/weather, music state, ZonePortal, Combat-adjacent world rules (ZoneRules/TradeZone blobs), NpcAnchor | (inside M6 / M4) | Stage 6 |
| M5 | Money and items edge: Wallet, Inventory, Equipment, Loadouts, Loot, Flask (group 1) + Storage, Shopping, Kiosk, Trade (group 2, ledger + kill test) | M5 | Stage 7 groups 1-2 |
| **M5b NEW** | Progression / crafting / appearance edge (group 3): GearWork, Crafting, Weaves, Restyle, Nova, Serums, XP boosts, Progression, CombatSkills, MovementSkills, Autobuff/Autopilot | (inside M5) | Stage 7 group 3 |
| **M5c NEW** | Social / companions / housing edge (group 4): Chat, Contacts, Crew, Party, Sponsor, Duel prompts, Emotes, Pet, Mount, Crate, Housing, Recall, Cadence | (inside M5) | Stage 7 group 4 |
| M6 | Persistence + roster + accounts + world directory/travel + GM + server tools (control page, status, lifetime) | M6 | Stage 2/6 leftovers |
| M7 | UI / presentation consolidation (bridges land per system; M7 is the sweep, the `Local` seam, 60 fps with bots in view) | M7 | Stage 3-7 spread |
| M8 | Scale: delete SpatialInterestManager, relevancy/importance, thin-client bots, soak | M8 | Stage 8 |
| M9 | Linux server parity + release candidate | M9 | (online-linux plan) |
| M10 | Compare, last merge, delete NGO, publish to Test, promote | (none) | Stage 9 (Stage 10 Burst brain is folded into M4) |

### 0.2 Owner (worker role) legend

These are proposed nested-worker roles; the mini-boss should assign sessions to them.
`W-Seams` (M0 NGO seams) / `W-Found` (bootstrap, connection, Networking) / `W-Player` (movement, motions, presentation
body) / `W-Combat` / `W-Monster` (Hostiles, Emerald replacement) / `W-World` (drops, singletons, travel, maps, anchors) /
`W-Persist` (saves, roster, identity, accounts) / `W-Menus` (Stage 7 edge ports, three sub-workers: Money, Progression,
Social) / `W-UI` (UI, GameFeel, Audio, Icons bridges) / `W-Test` (harness, bots, parity) / `W-Linux` (Burst, Linux,
deploy) / `W-Tools` (Editor builders, MapAnchor, GM, build pipeline) / `W-Docs` (plan, Encyclopedia, memory) /
`Mini-boss` (decisions, merges).

### 0.3 Proving-test legend (from `dots-test-harness-plan.md`)

A = pure rule tests (existing EditMode `*RulesTests`, unchanged); B = ECS system tests (EditMode, `World` per test);
C = netcode test world (in-process server+client); D = 2-client dedicated-server script with screenshots;
E = thin-client load bots; F = latency/loss simulation (60/150/250 ms); G = economy/save golden ledger + kill test on
`krytical_nova_test`; **LP** = Linux parity (Windows vs Linux replay hash, section 6 of the harness plan);
S = structure gates (`dependency_map.py --check`, `compile_check.py` and `--server`, `Validate Project Organization`).

### 0.4 Fate legend

**ports as-is** (no code change beyond a recompile) / **rewritten (network edge only)** (rules stay managed, `X.Net.cs`
swapped for an N4E edge: unmanaged `IRpcCommand`s, owner-only ghost buffers or view-RPC pages) /
**rewritten (new ECS)** (new components, Burst/jobs systems) / **bridged (managed presentation bridge)** (stays
GameObject/MonoBehaviour, fed by ghost data through a view system) / **replaced** (a different mechanism does the job) /
**removed**.

---

## 1. The table

Footprint format: `NB` NetworkBehaviours, `RPC` rpc text hits, `nv~` NetworkVariables (halved), `nl~` NetworkLists
(halved), `INS` INetworkSerializable mentions. "none" = no Unity.Netcode type in the code.

### 1.1 Foundation and Infrastructure

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| F1 | Core - seams (new: `PlayerKey`, `NetRole`, `NetSession`, `NetClock`, `IWorldTargets`, `IServerLeaving`) | Things every system will call instead of NGO | none today (all new) | rewritten (new, plain C# in Core) | M0 | W-Seams | A, S (stub-base compile gate) | Draft Stage 1. Open Q3: land as ordinary NGO PRs to `main` early. `IPlayerBody.OwnerClientId` becomes `PlayerKey`. |
| F2 | Core - `PerformanceLogRecorder` / Diagnostics | The perf log a built game writes | 1 file uses `NetworkManager.Singleton` (2 hits); Core asmdef still references `Unity.Netcode.Runtime` | ports as-is after moving its netcode half to Networking; feed via a Core interface | M0 (seam), M1 (feed), M8 (new counters) | W-Seams, W-Test | A, S | Needed so Core/UI asmdefs can drop the Netcode reference. Telemetry list in harness section 5. |
| F3 | Core - sinks and registries (`ClientPresentationRegistry`, `I*Sink`, `LocalPlayer`, `PlayerBodies`, `IHostile`, `IInteractable`, `IWorldMarkerSink`) | How rules reach the screen without knowing UI | none (83 files / 19 systems use the registry) | ports as-is; `LocalPlayer.Set` and `PlayerBodies` get a new source (the ghost view systems) | M2 (LocalPlayer), M7 (rest) | W-UI | A, D | The 99 `X.Local` call sites survive only if the managed `X` classes stay as view models (open Q10). |
| F4 | Core - save interfaces (`ISaveStore`, `ISaveParticipant`, `IBatchSaveStore`, `ISaveScheduler`, file store) | The contract saves are written through | none | ports as-is | - (no work; guard with M6 tests) | W-Persist | A, G | Do not touch: CLAUDE.md save rules. |
| F5 | Core - misc (`KeyBindings`, `LocalProfile`, `LocalAccount`, `GroundSnap`, `Accessibility`, `DamageResult`, `ItemUseEffect`, WarmUp) | Small shared helpers | none | ports as-is | - | - | S | `GroundSnap` is used by client placement; check against prediction in M2. |
| N1 | Networking - bootstrap and connection (`NetworkConnectionManager` 950 lines, `JoinFlow`, `JoinRules`, `AccountProof`, `DedicatedServerBoot`, `DedicatedServerOptimizer`, `EditorTestSession`) | Starts the server/client, approval with Unity token, bans, refusals, travel ticket | NMS 57 hits in asm; 0 RPC; approval callback; payload up to 5000 B | rewritten (new ECS/N4E): `KnBootstrap : ClientServerBootstrap`, approval RPC in chunks, refusal RPC, no worlds at front end | M1 | W-Found | C, D, F | Biggest single rewrite. Token vs RPC size limit R8. `JoinRules`/`AccountProof`/`UnityToken` logic ports. |
| N2 | Networking - transport security (`ServerEncryption`, Relay start/join, Multiplayer-sessions path) | DTLS/own-CA encryption, Relay, code-based join | in `NetworkConnectionManager` | rewritten (network edge only): custom `INetworkStreamDriverConstructor`, `RelayServerData` | M1 (Direct), M9 (Relay re-check) | W-Found | C, D, LP | See Q about player-hosting and mediated sessions (section 4). |
| N3 | Networking - Safety (`RpcBudget` 171 call sites / 40 files, `CallLimits`, `NetworkHealth`, `SecurityFlags`, `ServerBans`, `ServerRecords`, `GmRoster`) | Per-client flood limits, bans, who-is-GM | keyed by NGO `ulong clientId` | rewritten (network edge only): re-key to `PlayerKey`, same buckets | M0 (re-key on NGO), M1 | W-Seams, W-Found | A (`RpcBudgetTests`, `ServerBansTests`), C | Never key by raw `NetworkId` (reused). |
| N4 | Networking - spawn and placement (`PlayerSpawnService`, `PlayerSpawnPoint`, `NetworkPlacement`) | Where a player appears; one teleport entry point | uses NGO spawn + `agent.Warp` | rewritten (new ECS): `GoInGame`, `TeleportSeq` | M2 | W-Player | C, D, F | Draft Stage 1 also forces raw `agent.Warp` (`ZoneTravel.cs:162`, `PointAndClickPlayer.cs:1469`) through it in M0. |
| N5 | Networking - scene guards (`SceneNetworkGuard`, `SceneObjectKeeper`, `OfflinePlayerHandoff`, NetworkManager.prefab in 44 scenes) | Keeps one NetworkManager, lets maps be played alone | prefab copies in 44 scenes, 40 override fields | removed (offline = local server + client) | M1 build, M10 delete | W-Found, W-Tools | S, C | `NetworkSetupTests` retire; replacement test = one bootstrap config (harness section 4). |
| N6 | Networking - `SpatialInterestManager`, `AlwaysRelevantNetworkObject` | Radius culling O(clients x objects) | `CheckObjectVisibility`, `NetworkShow/Hide` | replaced by ghost relevancy + `GhostDistanceImportance` (48 m tiles, job-filled) | M8 (stub relevancy at M2) | W-Monster / W-World | E, D | Draft Stage 8; fill relevancy in a job (review item 23). |
| N7 | Networking - roster transport (`RosterLink`, `RosterWire`, roster answers in refusal text) | Character list before joining | answer rides in `response.Reason` | rewritten (network edge only): paged `RosterReplyRpc` on a throwaway client world (Q7 a) | M1 (transport), M6 (full) | W-Persist | C, A (`CharacterRosterTests` format) | Alternative: HTTPS endpoint (Q7 b). |
| N8 | Networking - `PlayerGm` (+ `GmRoster`) | GM commands over RPC | 1 NB, 2 RPC, 1 nv | rewritten (network edge only) | M6 | W-Tools | C, D | New GM commands also need GM-window controls (memory rule). |
| N9 | Networking - server runtime (`ServerControlPage`, `ControlPageRules`, `ServerLaunchOptions`, `ServerLifetime`, `ServerPerformanceLog`, `ServerReach`, `ServerStatusResponder`, `ServerSingleCopy`, `WorldLink`, `ServerList`, `ServerDirectory`, `UnityAccount`) | HTTP control page, status UDP, lifetime/restart, server list publish (UGS CCD), world directory link | uses NGO client lists (35 `ConnectedClients*` hits repo-wide) | ports as-is, with small reads rewritten to N4E connections/`PlayerKey` | M6 | W-Found | A (`ControlPageTests`, `ServerSettingsTests`), D | `ServerReach`/UGS CCD publishing and `ServerList` are not in the draft plan (section 5). |
| N10 | Networking - `ServerHooks` | Callback seam Gameplay fills | none | ports as-is, extended (`GoInGame` hook) | M2 | W-Found | A (`ServerHooksTests`) | Draft keeps Networking type-agnostic. |
| N11 | Networking - `NetworkCombatVFXPool`, `NetworkDebugHUD` | Pooled networked VFX, debug HUD | NetworkObject pool | replaced: VFX via `HitEvent/FxEvent` ring; HUD from N4E telemetry | M3 (VFX), M8 (HUD) | W-Combat, W-Test | D | |
| N12 | Networking - `ZoneSessionManager` (feature-gated, default zone "Central_Plains_01") | Old zone-sharding sketch on Unity Multiplayer Services | none NGO; uses `Unity.Services.Multiplayer` | removed (likely dead; `WorldLink`/`MapScenes` do the job) | M10 (or M0 cleanup) | W-Found | S | Question U1. |
| N13 | Networking - `ServerApp~` (WinForms app), `Tools~/certificates.sh`, `Deploy~` (Linux box scripts, untracked in main) | Owner's server window, certs, cloud deploy scripts | none | ports as-is (ServerApp launches processes with CLI flags); Deploy~ owned by Linux plan | ServerApp: M6 smoke; Deploy~: M9 | W-Linux | D (beside measure server), LP | Flags (`-role map -map -port -pageport`) must keep working. |
| N14 | Networking.Tests + Tools.Tests | `NetworkSetupTests` (YAML scan of 44 scenes), `CharacterRosterTests`, `RpcBudgetTests`, `JoinRulesTests` etc. | NGO FastBuffer in roster test | rewritten/retired per harness section 4 | M1 | W-Test | A, C | |
| R1 | Rendering (`KryticalNova.Rendering`) | Comic style, shadows, LOD, occluder fader, culling | NMS 3 + NO 2 (`CharacterLod`) | ports as-is (fix `CharacterLod` netcode use through `LocalPlayer`) | M0 | W-Seams | S | Entities Graphics is NOT adopted for rendering in the draft; open Q17/Q21 for monsters/drops. |

### 1.2 Simulation - Player (split by networked sub-feature)

`KryticalNova.Player` total: 10 NB, ~30 RPC, nv~7, INS 2, NMS 3, NO 11, 20 files, 10,685 lines (the largest
networked assembly). Build list rows 2, 23, 30, 44.

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| P1 | Player - click movement (`PointAndClickPlayer`, `PlayerMoveAnticipation`, NavMeshAgent steering, `ClimbableSurface`) | Server-moved NavMeshAgent, owner anticipation, stock NetworkTransform | NB 2, RPC ~6, 1539-line file; `NetworkTransform` on player | rewritten (new ECS): `PlayerInput`, `ServerOrder`, `MoveState`, `PathCorner` buffer, `PredictedMoveSystem`; path planning stays managed per click | M2 | W-Player | B, C, D, F | Owner "feel" checkpoint after this. Player-vs-player avoidance disappears (Q11). Planners (`TryPlanRoute`, `TryArm`) port. |
| P2 | Player - jump, vault, wall-run, waterstride, death motors (`PlayerJumpMotor`, `PlayerVaultMotor`, `PlayerWallRunMotor` + WallWalk, `PlayerWaterstrideMotor`, `PlayerDeathMotor`, `WallRunRules`) | Server-scripted motions with Motion Warping | NB 5, RPC ~17, nv~3 | planners port; execution rewritten (new ECS): `MotionPlan` + `PlayerMotionSystem`, tick-based curves; warp calls move to presentation | M2 | W-Player | B, C, D, F | `PlayerWaterstrideMotor` and wallstride are not named in the draft plan but are in the ghosts design 3.3. Do not predict vault on owner (Q12). Restart-guard by `startTick`. |
| P3 | Player - animation and presentation (`PlayerAnimancerMotor`, `PlayerJumpPresentation`, `FootGroundingIK`, `PlayerNightLight`, `CameraPreferences`, `TreeProxyField`) | Animancer/KINEMATION layer, IK, light, camera | `PlayerAnimancerMotor`: NB 1, RPC 4, nv~1 | bridged (managed presentation bridge): `PlayerViewBinding` / `PlayerPresentationSystem` follows the ghost | M2 (local), M7 (remote players) | W-Player, W-UI | D, F (visual) | One `Player.prefab`, three roles (`NetRole`: ServerBody, ClientView, LocalStub). Animancer, Cinemachine, KINEMATION, Sidekick stay GameObjects. |
| P4 | Player - identity and look (`PlayerIdentity`, `CharacterNetState` in CharacterCreation) | Name, faction, level, equipped look shown to others | NB 1, RPC 1, nv~2; `CharacterNetState` uses FixedLists (not GhostFields) | rewritten (network edge only): `LookVersion` on ghost + versioned look RPC cache | M2 | W-Player, W-Persist | C, D | Look cache miss = default mannequin in crowds (R16). |
| P5 | Player - GM sight (`PlayerGmSight`) | GM "see all" toggle | NB 1, RPC 1, nv~1 | rewritten (network edge only) | M6 | W-Tools | D | Not named in any source. |
| P6 | Player - server facts and contracts (`ServerPlayerFacts`, `CombatContracts`) | Server-side lookups/interfaces | none | ports as-is | M2 | W-Player | A | |
| P7 | Player - role prefab and setup (`Player.prefab`, `PlayerSystemsSetup`, `Characters/Player`) | Prefab with 54+ NBs on one NetworkObject | 1 NetworkObject | replaced: ghost prefab (baked in Bootstrap subscene) + server-body GameObject + presentation body; `PlayerSystemsSetup` rows become edge components | M2 (prefab roles), each later milestone adds its row | W-Player, W-Tools | S, D | CLAUDE.md "new player-side system adds a row" rule changes to ghost-component rule. |

### 1.3 Simulation - Combat, Progression, Hostiles

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| C1 | Combat - melee and weapon hits (`PlayerMeleeCombat`, `WeaponHitbox`, `CombatHostility`, `RedlineRules`) | Combo ordering, cadence, hit validation | NB 1, RPC 5 (`RequestMeleeStrikeDamageServerRpc`) | rewritten (new ECS): swing as input, server hit test at `CommandDataInterpolationDelay` against position history; damage resolution ports | M3 | W-Combat | B, C, D, F | Lag-comp mechanism undecided: draft says Unity Physics, owner Q14 says own `PositionHistory` ring (section 5 gap). Hit moments from data (Q13). Owner feel checkpoint. |
| C2 | Combat - skill execution (`PlayerSkillAttacks`, `PlayerSkillQueue`, `ISkillAttackExecutor`) | Cast, queue, walk-in to range | NB 2, RPC 6 | rewritten (new ECS) transport/timing; rules port | M3 | W-Combat | B, C, D | Skill walk-in uses `ServerOrder`. |
| C3 | Combat - state and duel (`PlayerCombatState`, `PlayerDuel`) | In-combat flag, Callout (duel) rules | NB 2, RPC 9, nv~2 | rewritten (network edge only) / `InCombat` into `Vitals` | M3 (state), M5c (duel prompts) | W-Combat, W-Menus | C, D | Duel/PvP hostility rules unchanged. |
| C4 | Combat - Cadence (`PlayerCadence`, `CadenceRules`, `CadenceCombo`, `IBeatClock`) | Skill bonus for on-beat presses; server judges beats | NB 1, RPC 5; uses NGO `ServerTime` (Audio `MusicConductor`) | rewritten (network edge only); clock via `NetClock` seam; music-state ghost lives in World | M5c (needs M4b) | W-Combat | A (`CadenceRulesTests`), D | Build list row 63. Audio asmdef is Presentation, the music writer moves down to World (review item 26). |
| C5 | Combat Dummy (Hostile prefab, no own script) | Static test target | via `HostileNet` | rewritten (new ECS): anchored static ghost with `Vitals`, hit-proxy | M3 | W-Combat | D | First target for the two-client test. |
| G1 | Progression - health and resources (`CombatEntity`, `PlayerResources`, `PlayerCombatState` link, `IDamageable`) | HP/Chi/level truth for players and Hostiles | NB 2, nv~7 | rewritten (new ECS storage): `Vitals` ghost component; regen rules port | M3 | W-Combat | A, B, C | Open Q2 (who is truth): `Vitals` (rec) vs server body. |
| G2 | Progression - rules and maths (`CombatCalculator`, `CharacterBuffManager`, `CharacterCombatStats`, `LevelGapRules`, `NovaRules`, `SerumRules`, `SquadBuffRules`, `ExperienceBoostRules`, `CastCheck`, data SOs) | Damage, buffs, stats, XP | none | ports as-is (called from `DamageApplySystem`, hundredths conversion); Burst port of `CombatCalculator` only for monsters (parity-tested) | M3 / M4 | W-Combat, W-Monster | A (existing ~70 rule tests), B (parity) | Rules tests are "the contract". |
| G3 | Progression - skills (`PlayerCombatSkills`, `PlayerMovementSkills`, `PlayerSkillHotbar`, `MovementSkillState`) | Skill ranks/points, hotbar, movement skills | NB 2, RPC 13, nv~2, nl~1 | rewritten (network edge only); movement skills ride the motion system | M5b (movement skills with M2 dependency) | W-Menus (Progression) | C, D | `PlayerCombatSkills` uses `OnClientConnectedCallback` (re-key). |
| G4 | Progression - character progression (`PlayerCharacterProgression`, `PlayerNova`, `PlayerSerums`, `PlayerExperienceBoosts`, `PlayerGmState`, `ProgressionState`, `NovaHalo`) | Level/XP/points, Nova standings, Serums, boosts | NB 5, RPC ~11, nv~5, nl~1 | rewritten (network edge only) | M5b | W-Menus (Progression) | A, C, D | |
| H1 | Hostiles - ghost and AI (`HostileNet`, `IHostileBrain`, `HostileAnimState`, `HostileCorpse`, `HostileIgnore`, `Hostiles.Emerald` asmdef + Emerald AI vendor) | Up to 102 Hostiles per map, AI, HP bars | NB 1, RPC 3, nv~3; NO 15 | rewritten (new ECS): interpolated ghost + Burst FSM brain (Emerald dropped); draft's "companion first" is superseded by `dots-monster-ai-design.md` | M4 | W-Monster | B, C, D, E, LP (monsters unpredicted so float-safe) | Q16 navigation, Q17 monster visuals (pooled GameObject views near, impostors far), Q18 wolf prototype. Tick p95 < 15 ms at 102. |
| H2 | Hostiles - spawn and rewards (`HostileZone`, `HostileSpawner`, `HostilePool`, `RespawnQueue`, `HostileKillRewards`, `HostileLoot`, `HostileRates`, `LevelGapRules`) | Zones, respawn, XP/loot on kill | `Spawn(true)` in `HostileSpawner:70` | rewritten (new ECS) zone spawner system + one managed kill bridge; loot/XP/level-gap rules port | M4 | W-Monster | A (`HostileLootTests`, `StatCurveTests`), B, D | ECS never touches wallet or ledger (monster-ai design 0). |
| H3 | Hostiles - content (`HostileDefinition`, curves, `HostileAssetBuilder`, `HostileZoneBuilder`, `Characters/Hostiles`) | Data and Editor builders | Builder makes prefabs registered with NGO | ports as-is (data); builders rewritten to emit ghost prefab + client view + `MapAnchor` | M4 | W-Monster, W-Tools | S, B | `GameplayPrefabsSetup.SpawnedPrefabs` guard becomes "ghost collection" check. |
| W1 | Items - `WorldDrop` + `WorldDropPool` + `TakenDropVisual` | Loot on the ground (200 per zone) | NB 1, RPC 1, nv~2; `INetworkPrefabInstanceHandler` | rewritten (new ECS): static interpolated ghost, `DropInfo`; Entities Graphics for rendering deferred (Q21) | M4 | W-World | B, C, D | `ServerPickup` logic ports; autopilot/magnet reads drops through a `DropView` registry. |

### 1.4 Simulation - Items, Economy, Wallet

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| I1 | Items - inventory and equipment (`PlayerInventory`, `PlayerEquipment`, `PlayerLoadouts`, `PlayerLoot`, `PlayerFlask`, `PlayerWeaponClass`) | Pack, worn gear, loadouts, pickup, Flask | NB 5, RPC 19, nv~4, nl~2 | rewritten (network edge only): owner-only buffers + view-RPC pages | M2 (Equipment, for the body), M5 (rest) | W-Menus (Money) | A, C, D, G | `PlayerInventory` is the 74-file ripple hub; slice first: Wallet vertical slice, then Inventory. |
| I2 | Items - rules and data (`InventoryRules`, `LootRules`, `FlaskRules`, `GearStatResolver`, `GearProgressionRules`, `ItemCatalog`, `ItemDefinition`, `ItemNetworkTypes`, `LoadoutRules`, `ItemFactionRules`) | Gear ladder, stats, item data | `ItemNetworkTypes` (8 INS structs: `ItemStack`, `EquipmentData`...) | ports as-is (rules/SOs); INS structs rewritten as unmanaged ghost/buffer/RPC structs | M0 (unmanaged args), M5 | W-Seams, W-Menus | A | Must end up Burst-friendly where used in ECS (`ItemId` ushort). |
| I3 | Items - visuals (`Items/Runtime/Visuals/*`: `CharacterAppearance`, `EquipmentVisualBinder`, `GearCloth`, `GearPaint`, `GlowRim`, `EyeGlow`, `BodyKeyTransfer`, `IPetPresenter`...) | Sidekick-based gear look on bodies | none | bridged (managed presentation bridge) | M2 (own), M7 (others via look cache) | W-UI / W-Player | D (screenshots) | Presentation stays GameObject. |
| E1 | Economy - Wallet edge (`PlayerWallet`) and `Wallet` asmdef (`IWallet`, `WalletRules`, `LedgerNote`) | Creds balance and tally; ledger note type | `PlayerWallet`: NB 1, nv~1; Wallet asmdef: none | edge rewritten (Q15: view in RPC pages or owner-only field); asmdef ports as-is | M5 (vertical slice first) | W-Menus (Money) | A, G (`TransactionLedgerTests`) | Every Cred tallied by `PlayerWallet`; keep `CredsSource/CredsSink`. |
| E2 | Economy - Vault / Deep Vault (`PlayerStorage`, `PlayerVault`, `PlayerDeepVault`, `StorageRules`) | Per-character and per-account storage | NB 1, RPC 7, nv~1, nl~1 | rewritten (network edge only) | M5 | W-Menus (Money) | A, C, G | 96 cells each: paged views. |
| E3 | Economy - Shopping and vendors (`PlayerShopping`, `VendorRules`, `VendorBought`, `ItemCostRules`, `NpcAnchor` (new)) | Buying/selling at NPCs | NB 1, RPC 3, nv~1, nl~1 | rewritten (network edge only); range check via `NpcAnchor` entities | M5 (anchors in M4b) | W-Menus (Money) | A, C, G | |
| E4 | Economy - Trade (`PlayerTrade`, `TradeRules`, `TradeChatCommands`) | Player-to-player trade, both lock, all-or-nothing | NB 1, RPC 13 | rewritten (network edge only) | M5 | W-Menus (Money) | A, G (golden trade), kill test | Must stay `ServerSave.SaveTogether` + `EconomyLedger`; owner rule 18 Oct. |
| E5 | Economy - Kiosks (`PlayerKiosk`, `KioskStand`, `KioskService`, `KioskRules`, `KioskData`) | Proxy shops standing in the world | PlayerKiosk RPC 6; KioskStand NB 1, nv~2, nl~1 (own NetworkObject) | stand: rewritten (new ECS) static ghost + listing buffer; service/rules port | M5 | W-Menus (Money), W-World | A, C, G | Listing buffer ghost field may need page RPC (spike S1). |
| E6 | Economy - ledger (`EconomyLedger`, `CredsLedger`, `PlayerItemSources`) | Kept ledger lines, item sources | none | ports as-is | - (guarded in M5, M6) | W-Persist | G, kill test | `LedgerKinds` unchanged; SaveService must not learn economy rules. |
| E7 | Forge (`PlayerGearWork`) | Tempering, Fusion, Tuning at the Armorer | NB 1, RPC 8 | rewritten (network edge only) | M5b | W-Menus (Progression) | A (gear tests), C, G | Build list rows 3-5. |
| E8 | Crafting (`PlayerCrafting`, `CraftRules`) | Fabricator bench, Dossier crafting | NB 1, RPC 3 | rewritten (network edge only) | M5b | W-Menus (Progression) | A, C | Rows 34, 53. |
| E9 | Restyle (`PlayerRestyle`, `PlayerWeaves`, `PlayerWeaveLibrary`) | Outfitter, Stylist, Holoweave library | NB 3, RPC 12, nl~2 | rewritten (network edge only) | M5b | W-Menus (Progression) | A, C | Weave Library is account-wide: paged. Rows 19, 52. |

### 1.5 Simulation - Social, Companions, Housing, Autopilot, NPC, Persistence, Factions, CharacterCreation

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| S1 | Social - chat (`PlayerChat`, `ChatRouting`, `ChatCommands`, chat command classes, `ServerAnnouncer`) | Channels, slash commands, system lines | NB 1, RPC 6, nv~1 | rewritten (network edge only); text via `FixedString` or paged | M5c | W-Menus (Social) | A, C, D | Text length caps (`TextFits`). |
| S2 | Social - Contacts (`PlayerContacts`, `ContactsService`, `ContactRules`) | Friend list, requests, online state | NB 1, RPC 16 | rewritten (network edge only) | M5c | W-Menus (Social) | A, C | Offline-target requests rely on save service. |
| S3 | Social - Crew (`PlayerCrew`, `CrewService`, `CrewRules`, `CrewData`) | Guild: founding, ranks, tag | NB 1, RPC 16, nv~1 | rewritten (network edge only) | M5c | W-Menus (Social) | A, G | Founding uses `SaveTogether`. |
| S4 | Social - Party/Squad (`PlayerParty`, `PartyRules`, `PartyMember`, `PartyCheck`) | Squad of 5, XP share | NB 1, RPC 9, nv~1; `SpawnedObjects` lookup (`PlayerParty:472`) | rewritten (network edge only); cross-player lookup via registry | M5c | W-Menus (Social) | A, C, D | Frame data via `IWorldTargets`. |
| S5 | Social - Sponsor (`PlayerSponsor`, `SponsorService`, `SponsorRules`) | Mentor/recruit | NB 1, RPC 8, nv~1 | rewritten (network edge only) | M5c | W-Menus (Social) | A, C | |
| S6 | Social - emotes and follow (`PlayerEmotes`, `EmoteCatalog`, `PlayerFollow`, `FollowRules`) | Emote wheel; Follow walks | NB 2, RPC 5, nv~1 | emotes: rewritten (network edge only) via `FxEvent`; Follow: rewritten (new ECS) using `ServerOrder` | M5c (Follow wants M2) | W-Menus (Social), W-Player | C, D | Follow is a server-issued walk (`PlayerFollow.cs:213,242`). |
| A1 | Autopilot (`PlayerAutopilot`, `PlayerAutobuff`, `AutopilotController`, `MagnetController`, `PassClock`, rules) | Hunt/auto-buff/magnet inside a Hostile Zone | NB 2, RPC 7, nv~4 | server flags: rewritten (network edge only); controllers (client) port, read hostiles/drops via registries | M5b (after M4) | W-Menus (Progression) | A, D | Uses server-issued walks. Row 9. |
| PET | Pets (`PlayerPet`, `PetRules`, `MorphRules`, `PetItemState`, `PetFollower`, `PetChatter*`) | Companion with growth; local follower, no networked object | NB 1, RPC 5, nv~1 | edge rewritten (replicated on player ghost: `PetPublic` + `PetTrick` ring); rules and `PetFollower` port | M5c | W-Menus (Social) | A, D | No separate pet ghost. Row 43. |
| MNT | Mounts (`PlayerMounts`, `MountRider`, `StirrupIK`, `MountNet`, `MountRules`, `MountFeeding`, `Characters/Mounts`) | Rideable Brambleback, 3 stages, ride speed | NB 1, RPC 4, nv~2 | edge rewritten (`MountPublic` + owner `MountBook`); rules port; `MountRider`/`StirrupIK` bridged | M5c (ride speed hooks into M2 prediction) | W-Menus (Social), W-Player | A, F (34 m/s rollback), D | Risk R1. Mount art pipeline untouched. |
| HOU | Housing and Recall (`PlayerHousing`, `PlayerCrate`, `PlayerRecall`, `HomeView`, `HallRoom`, `HousingRules`, `LocalHomeAuthority`) | Crew Hall sandbox, crate, recall tags | NB 3, RPC 7; host/offline only | edge rewritten (network edge only) for Crate/Recall; Housing ports unchanged (still local, `IHomeAuthority` seam) | M5c | W-Menus (Social) | A, D | Housing walks use `ServerOrder`. Shared Halls later (`crew-halls-plan.md`). |
| NPC | NPC (`NpcInteractable`, `NpcDefinition`, `NpcLines`, `Characters/NPC`) | Scene-placed shopkeepers and trainers | none | ports as-is + new server-only `NpcAnchor` baked from `MapAnchor` | M4b | W-World | A (`NpcTests`), D | Not networked today; draft Stage 6/7 do not name NPC range checks. |
| PER | Persistence (`PlayerSaveAgent`, `ServerSave`, `ServerSaveHooks`, `ServerRoster`, `AccountSave`, `ServiceSaveStore`, `ServerBackups`, `PlayerRename`, `PlayerHotbarMemory`, `LegacyDataMove`) | Saves keyed by account+character; save agent per player | NB 2 (`PlayerSaveAgent`, `PlayerRename`), RPC 4, NMS 5 | rules/store port as-is; `PlayerSaveAgent` edge rewritten (`.Net.cs` split, load on Entered, save on Leaving, disconnect order) | M2 (agent), M6 (full) | W-Persist | A (14 tests), G, kill test, D | Open Q2/Q9. Save on server body before ghost destroyed. |
| SVC | SaveService (.NET), Postgres, `WorldDirectory`, KillTest, WorldTest | The only program that talks to the DB; world directory/tickets | none | ports as-is | - (M6 regression; test DB only) | W-Persist | G, kill test, `world_test.py` | Real DB (7801) never touched. |
| FAC | Factions (`FactionId`, `FactionMask`, `FactionRules`, `IFactionMember`) | Three factions, hostility masks | none | ports as-is (becomes ghost byte component); `FactionRules` pure-data rewrite for Burst use | M3 | W-Combat | A | 80-file ripple. Ronin (row 56) stays hidden. |
| CHC | CharacterCreation (`CharacterLook/Recipe/Roster/Session/Doll`, `CharacterNetState`, `RosterMessages`, name rules) | Creator, roster, lineage/body | INS 1 (`CharacterNetState`); no NB | ports as-is; `CharacterNetState` and `RosterMessages` wire rewritten (unmanaged); `CharacterDoll` bridged | M2 (look), M1/M6 (roster wire) | W-Persist, W-UI | A (10 tests), D | Front-end scenes (Title, FactionSelect, Creator, Select, Loading) run with no netcode world. |

### 1.6 World, Environment, Characters, Scenes

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| WLD1 | World - clock and weather (`WorldClock`, `WeatherBlend`, `WorldEnvironmentNetwork`, `WorldEnvironmentState`) | Day cycle and weather every peer derives | NB 1, nv~1; placed in ~40 scenes | edge rewritten (static always-relevant singleton ghost); derivations port | M4b | W-World | A, D | Via `NetClock`. |
| WLD2 | World - portals and travel (`ZonePortal`, `IZoneTransferHandler`, `MapScenes`, `ZoneTravel`, `ZoneRules`, `TradeZone`) | Zone transfer, map scene loading, zone rules | NB 1 (ZonePortal), RPC 1 | portal: rewritten (new ECS) server trigger + RPC; travel: rewritten (network edge only) = client world rebuild + ticket; `ZoneRules`/`TradeZone` become baked blobs | M4b (portal), M6 (travel tickets) | W-World | C, D, `world_test.py` | Row 37, 54. Addressable map scenes + anchors (section 5 gap on subscene build). |
| WLD3 | World - presentation (`EnvironmentPresenter`, precipitation, lightning, wind, street lamps, volumetrics, `WeatherAmbience`) | Renders sky/weather | none | ports as-is (unchanged presenters) | M4b | W-World | D (screenshots) | |
| WLD4 | World - navigation (`RuntimeNavMeshBuilder`, `Surfaces/*`: `SurfaceGraphBaker`, `SurfaceRoute`, `WaterRoute`, `VendorStall`) | Runtime NavMesh, surface/water routes used by click paths | none | ports as-is (planners stay managed; called once per order) | M2 | W-Player | A, D | Draft plan does not name surface-graph or water routes (section 5). |
| WLD5 | World - minimap and POI (`Minimap/*`, `PointOfInterest`, `FlyingTrafficLane`) | Minimap data, points of interest, ambience traffic | none | ports as-is | - | W-World | S | |
| WLD6 | World data and zones (`World/Data`, `Zones`, `Sky`, `Terrain`, `Travel` art; terrain data) | SOs, terrain, art | none | ports as-is | - | W-World | S | |
| ENV | Environment (Buildings, Props, Ruins, Vegetation/Grass, Water) | Palette, props, ruins, GPU-instanced grass, water shader | none | ports as-is | - | - | S | Entities Graphics NOT used here (grass direction memory). Ruins are baked static prefabs. |
| SCN | Scenes (`Scenes/MainMenu` 5, `Scenes/World` ~40 maps, `Scenes/Development` 2) | Front-end and map scenes, Addressable | 44 carry the NetworkManager prefab; ~4-8 `GlobalObjectIdHash` placed NetworkObjects each | replaced: NetworkManager removed, placed objects become `MapAnchor`s; ghost prefabs baked in Bootstrap subscene | M0 (anchors), M1 (bootstrap), M10 (cleanup) | W-Tools, W-World | S, C | Builders to update listed in section 5. |
| CHR | Characters (`Player_01`, `Hostiles`, `Mounts`, `NPC`, `Pets`, `Shared` Sidekick/Swatch) | Models, rigs, clips | none | ports as-is (art); only ghost/client-view prefabs are new | M2-M5c per kind | W-Tools | S | Q18 wolf prototype uses stand-in Bonewalker clips. |
| VFX | VFX (Environment, GameFeel, Gameplay, Materials) | Particle prefabs and comic mark/word prefabs | NetworkCombatVFXPool only (N11) | ports as-is | M3 (events), M7 | W-UI | D | |
| INP | Input (`InputSystem_Actions`) | Input actions | none | ports as-is; new `PlayerInputGatherSystem` reads it | M2 | W-Player | B | `ProjectInputActionsGuard` unchanged. |

### 1.7 Presentation layer

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| UI1 | UI - runtime (143 files, 36,823 lines: HUD, Inventory, Menus, Settings, Guide, Social, Economy, Crafting, Restyle, Housing, NPC, Travel, Servers, Skins, Diagnostics) | The whole interface (UI Toolkit) | NMS 8, NO 14; 52 `.Changed +=` and 19 `.LocalChanged +=` on `X.Local`; 8 files use netcode directly (`NetworkSessionPanel`, `PartyHudController`, `HostileNameplateController`, `LootHudController`, `GameMenuController`, `PerformanceReadout`...) | bridged (managed presentation bridge): managed `X` classes stay as view models fed by bridge systems (Q10 a) | M7 (+ per system) | W-UI | `UI.Tests` (17), D | Server build excludes UI (`!UNITY_SERVER`). Menu screens for Title/Select need a throwaway client world (N7). |
| UI2 | UI - `NetworkSessionPanel` + Servers pages | Host/join, server list | direct `NetworkManager` use | rewritten (network edge only) | M1 / M6 | W-UI, W-Found | D | Player-hosting decision U2. |
| GF | GameFeel (HitReactor, ActorSmear/TimeSlow/Vfx, Feel/MMF, SkillFeelCatalog, pet chatter, BeatGlow...) | Hit reactions, smears, comic FX | NO 6 (via `GameFeelPresenter.IsLocalPlayer` etc.) | bridged (managed presentation bridge); `HitEvent/FxEvent` ring -> `ClientPresentationRegistry.Feel` | M3 (events), M7 | W-UI | `GameFeel.Tests`, D | Feel is a vendor (stays GameObject). |
| AUD1 | Audio - playback (`CombatMusicPlayer`, `ExploreMusicPlayer`, `DeckPlayer`, `ZoneAmbiencePlayer`, `BeatMath`, `CombatBeat`) | Local music and ambience | none | ports as-is | M7 | W-UI | A (`BeatMath` tests) | |
| AUD2 | Audio - `MusicConductor` (`IBeatClock`) | Networked music state the server judges beats on | NB 1, nv~1 | edge rewritten: music-state ghost + writer moves to World | M4b | W-World | A, D | Layer fix: Presentation owns a type the server needs (review item 26). |
| ICN | Icons (Art, Data, Editor library, Tests) | Icon library and baker | none | ports as-is | - | - | `Icons.Tests` | |

### 1.8 Tooling, build, release

| # | System | What it is | Netcode footprint today | Fate | Milestone | Owner | Proving test | Notes |
|---|---|---|---|---|---|---|---|---|
| T1 | Tools/Debug (`KryticalNova.DevTools`: GM console, `*Commands`, `MeasureBot`, `ServerConsoleInput`, `GmCommandRelay`) | In-game debug commands and the bot | NMS 11 | ports as-is (commands call managed server code); `MeasureBot` becomes thin-client bot launcher | M6 (GM), M8 (bots) | W-Tools, W-Test | E, D | `BenchCommands` uses `ServerMoveTo` (draft item 20). |
| T2 | Tools/Editor builders (`ServerBuildBuilder`, `ServerSceneStripper`, `BuildSceneOrder`, `GameplayPrefabsSetup`, `PlayerSystemsSetup`, `TestBuildBuilder`, `MeasureTools`) | Build pipeline and prefab/scene setup | NO 3 | rewritten (network edge only): subscene bake, ghost collection, MapAnchor emit | M1 (build), per milestone (setup rows) | W-Tools | S, build smoke | Server build must bake subscenes; Linux target in M9. |
| T3 | World Editor builders (`WorldScenesBuilder`, `WorldEnvironmentSetup`, `StarterTownBuilder.*`, `ZoneKitBuilder`, `HostileZoneBuilder`, `EnvironmentLodBuilder`, `ZonePackImporter`) | Place NetworkObjects in scenes | place NGO objects | rewritten (network edge only): emit `MapAnchor`s | M0 (anchors beside NGO), M4b | W-Tools | S | Draft Stage 1 item. |
| T4 | Editor.GM (`GmToolsWindow`, `GmConsoleSetup`) | GM window with server controls | uses `NetworkManager` | rewritten (network edge only) | M6 | W-Tools | D | Must mirror new commands. |
| T5 | Tools/Editor - Architecture, Organization, Cleanup, AssetRestore, Skin, Hierarchy | Dependency map, organization validator, cleanup | none | ports as-is; dependency map must recognise new ECS assemblies/layers | M1 | W-Test | S | Gate: 0 uphill, 0 two-way. |
| T6 | Per-system Editor folders (41 asmrefs to `KryticalNova.Editor`, `Editor.Presentation`) | Per-system builders (Items, Hostiles, Pets, Mounts, Persistence...) | NO 4 in shared Editor asm | ports as-is unless they create NGO prefabs | per system | W-Tools | S | |
| T7 | Release (`ReleasePublisher`, `BackgroundPublisher`, `Tools~/Launcher`, `ReleaseTool`, `UpdateManifest`) | Publish via Accelerator, launcher, download site | none | ports as-is | M10 (publish) | Mini-boss | publish smoke on Test channel | `JoinRules` build check must reject mixed NGO/N4E builds. Launcher news file untouched. Not in the draft plan (section 5). |
| T8 | Tests (24 `*.Tests.Editor` asmdefs; ~70 `*RulesTests`) | EditMode tests | none for NGO except Networking.Tests | ports as-is (they are the contract); add layers B-G | all | W-Test | A | Harness section 4. |

### 1.9 Vendor packages and engine packages

| # | Package | Role | Fate | Milestone | Owner | Notes |
|---|---|---|---|---|---|---|
| V1 | Netcode for GameObjects 2.13.3, multiplayer.tools, Dedicated Server 3.0.0, Services.Multiplayer 2.3.3, multiplayer.playmode 3.0.0 | Current netcode stack | replaced by `com.unity.netcode` + `entities` (6.6); dedicated-server package and playmode tools reviewed | M1 add, M10 remove | W-Found | Q22: second client = built client, not MPPM. |
| V2 | Burst 2.0.0, Collections 6.6, Mathematics 1.4, Transport 6.6 (transitive) | ECS dependencies | become direct | M1 | W-Linux | Burst float-drift rules (linux/burst note). |
| V3 | Emerald AI (`_ThirdParty`), `KryticalNova.Hostiles.Emerald` | Current monster brain | removed (does not work with ECS) | M4 / M10 | W-Monster | |
| V4 | Animancer, KINEMATION (Motion Warping), Cinemachine, Feel, Synty Sidekick, QuickDestruction (ruins baked), FastScriptReload | Presentation vendors | stay as-is (GameObject side). FastScriptReload does not work on ISystem/Burst | - | - | Dev-loop note (draft item 24). |
| V5 | Entities Graphics | Rendering ECS entities | not in the draft plan; owner asked for it 29 Oct. Decide scope: drops (Q21 a = defer), far monsters (Q17), nothing else | M4 decision | Mini-boss | Gap: plan must state. |
| V6 | Unity Physics | Lag-comp history (draft) | draft adds it; Q14 recommends none (own ring) | M3 decision | W-Combat | Contradiction between sources. |

---

## 2. Per-assembly totals (for cross-check)

From the scripted walk of runtime asmdefs (hits; `nv`/`nl` halved):

| Assembly | NB | RPC | nv~ | nl~ | INS | Fate summary |
|---|---|---|---|---|---|---|
| KryticalNova.Player | 10 | 30 | 7 | 0 | 2 | P1-P7 |
| KryticalNova.Social | 7 | 60 | 5 | 0 | 7 | S1-S6 |
| KryticalNova.Combat | 6 | 25 | 2 | 0 | 0 | C1-C5 |
| KryticalNova.Economy | 6 | 29 | 5 | 3 | 7 | E1-E6 |
| KryticalNova.Items | 6 | 20 | 6 | 2 | 8 | I1-I3, W1 |
| KryticalNova.Progression | 9 | 24 | 14 | 2 | 5 | G1-G4 |
| KryticalNova.Restyle | 3 | 12 | 0 | 2 | 0 | E9 |
| KryticalNova.Housing | 3 | 7 | 0 | 0 | 3 | HOU |
| KryticalNova.Autopilot | 2 | 7 | 4 | 0 | 0 | A1 |
| KryticalNova.Persistence | 2 | 4 | 0 | 0 | 0 | PER |
| KryticalNova.Forge | 1 | 8 | 0 | 0 | 1 | E7 |
| KryticalNova.Pets / Mounts / Crafting / Hostiles / World / Audio / Networking | 1 / 1 / 1 / 1 / 2 / 1 / 1 | 5 / 4 / 3 / 3 / 1 / 0 / 2 | 1 / 2 / 0 / 3 / 1 / 1 / 1 | 0 | 1 / 2 / 0 / 1 / 1 / 1 / 0 | PET, MNT, E8, H1, WLD1-2, AUD2, N8 |
| KryticalNova.CharacterCreation | 0 | 0 | 0 | 0 | 1 | CHC |
| KryticalNova.UI / GameFeel / Rendering / DevTools / Runtime (Core) | 0 | 0 | 0 | 0 | 0 | UI1, GF, R1, T1, F1-F5 (NGO API calls only: NMS 8/0/3/11/2; NetworkObject 14/6/2/0/0) |
| Not touched by netcode at all | - | - | - | - | - | Wallet, Factions, NPC, Hostiles.Emerald, Vegetation.Grass, Icons, Environment, VFX, Input |

---

## 3. Coverage count

| Measure | Count |
|---|---|
| Runtime assemblies (asmdef, non-test, incl. Editor ones) enumerated | 35 |
| Test assemblies | 24 (covered as one row T8 and per-system proving tests) |
| asmrefs (Data/Editor folders) | 41 (covered by their owning assembly row) |
| Rows in this table (one per system / assembly / networked sub-feature) | **98** |
| Systems found on the filesystem and in the code (same unit) | 98 |
| Classified with fate, milestone, owner and proving test | **95** |
| Provisionally classified, pending an owner question (N12, N2+UI2, V5) | 3 (section 4) |
| Rows that also carry an open design decision but are classified (G1, E1, H1, C1, V6, A1) | 6 |
| Build-list rows 1-67 mapped to a system row | 67 of 67 (Complete/Partial: all mapped; Candidate/To-build rows 28, 32, 35, 36, 55, 56, 60, 64, 65, 66 map to "not built, nothing to port"; row 60 Steam and row 56 Ronin need a netcode note when built) |
| Networked NetworkBehaviour classes found / classified | 63 / 63 |

## 4. Systems I could NOT classify (with the question)

1. **U1 `ZoneSessionManager` (Networking).** A feature-gated MonoBehaviour for "horizontal zone sharding" on
   `Unity.Services.Multiplayer` (default zone `Central_Plains_01`, transitions disabled). Question: is it dead code to
   delete now (so M0 removes it) or a kept seam for the multi-server travel design (`WorldLink`/`online-phase-05`)?
   Marked "removed (likely)" above.
2. **U2 Player-hosted sessions (`NetworkSessionPanel`, `StartMediatedHostAsync`, `JoinMediatedSessionByCodeAsync`,
   Relay host path, `ZoneTravel` "host trip").** The 20 Oct draft keeps host support ("player build targets client AND
   server"); the new full-ECS direction and the Linux/dedicated-server plan never say whether players can still host.
   Question: keep listen-server hosting (then the server world runs in the player build with Burst on Windows, and
   offline = local host) or drop it and make offline play a local dedicated-server process?
3. **U3 Entities Graphics scope (V5).** Owner asked for Entities Graphics on 29 Oct; the draft rejected it and Q17/Q21
   recommend deferring (GameObject views). Question: is the owner's intent "ECS-native simulation, GameObject
   presentation until profiling asks" (current recommendation) or "monsters and drops rendered by Entities Graphics at
   M4"? This decides whether skinned-mesh GPU animation work enters the plan.

## 5. Systems and facts the plan does NOT mention (or contradicts)

"Not mentioned" is measured against the 20 Oct draft plus the 29 Oct design notes (the real plan file is missing).

**Systems/code not named anywhere:**
- `PlayerRename`, `PlayerGmSight`, `PlayerHotbarMemory` (Persistence/Player edge classes added after the surveys).
- `ServerReach`/UGS Cloud Content Delivery server-list publishing, `ServerList`, `ServerDirectory`, `UnityAccount`.
- `ZoneSessionManager` and the Multiplayer-Services mediated-session path (U1/U2).
- `ServerApp~` (WinForms) process flags, `Deploy~` Linux scripts (covered only by the Linux plan), the control page's
  player list/kick (draft item 20 names it but no stage owns it) and `ServerStatusResponder`.
- Release/Launcher/ReleaseTool and the **build-number compatibility check in `JoinRules`**: N4E and NGO builds cannot
  talk to each other, so the publish/launcher update path and the "wrong build" refusal must be part of the cutover (M10).
- `SurfaceGraph`/`WaterRoute` pathing and `RuntimeNavMeshBuilder` as inputs to server path planning.
- `GroundSnap` (client placement) versus prediction.
- Sidekick look/doll pipeline for *other* players' gear (covered by the look cache but no milestone owner).
- NPC range validation (`NpcAnchor`) is only in the ghosts design, not in the draft stages.
- `TestBuildBuilder`, the publish-to-Test pipeline and the Accelerator workflow: must bake subscenes in batch mode
  (memory: ExecuteAlways + batch-mode packs disabled; check built bundles).

**Gaps and contradictions between sources:**
1. **No real plan file** and **milestone numbering mismatch** (section 0). Add M0, M4b, M5b, M5c, M10 or fold them.
2. **Lag compensation mechanism**: draft = Unity Physics `PhysicsWorldHistorySingleton`; owner Q14 recommends an own
   `PositionHistory` ring without Unity Physics. Pick before M3 (spike S5).
3. **Hostile AI**: draft = Emerald companion first, Burst brain at Stage 10; 29 Oct decision = new ECS brain now, Emerald
   dropped. The draft's Stage 5 and Stage 10 are obsolete; `HostileAssetBuilder` two-prefab design must be redone.
4. **Entities Graphics** is absent in the draft ("nothing is drawn by ECS") but wanted by the owner (U3).
5. **Addressable map scenes + Entities subscenes**: maps are Addressable (memory: maps-addressable-decision); ghosts
   bake in one Bootstrap subscene; per-map content (anchors, NPC anchors, zone blobs, hostile zones) has no defined
   build path for the headless server and the Addressable client. Needs a spike in M1.
6. **Player-hosting / offline play** (U2) is undecided.
7. **Test count**: "no PlayMode tests start a NetworkManager" today; the plan needs C-layer netcode test worlds
   (harness H2) before M2, not after.
8. **Cutover sequencing**: Stage 9 is a single merge; the dependency on Linux parity (M9) before release candidate and
   the "NGO and N4E builds cannot coexist" rule (see Release above) are not in the draft.
9. **Documents to update at each landing** (CLAUDE.md): Encyclopedia row 40 "Networking" says Complete/NGO; each landed
   milestone needs its row, glossary rows (Ghost, Server Body, Presentation Body, Map Anchor, PlayerKey, NetRole...) and
   change-log line; `05_Economy_And_Social.md`, `Server-Guide.md`, `Dependency-Map.md`, CLAUDE.md `SpawnedPrefabs`
   rule must change at M10.
10. **Counts drift**: regex finds 63 NetworkBehaviours (plan 59; map 65 on 25 Oct). The inventory of Stage 7 groups lists
    ~34 low-frequency classes; make the plan carry an explicit class-by-class checklist (the 63 above) so none is lost.
