# Netcode for GameObjects -> Netcode for Entities (DOTS) migration plan

Live checklist. Tick items as they land. Owner: Krytical Nova owner. Run by: Master > mini-boss `dots-migration`.
Started 29 Oct 2026. Work happens ONLY in this clone (`C:\Users\dlaco\Krytical Nova II DOTS`, branch
`dots/netcode-entities`, cut from `main` 3ad5ba303). The real project stays on NGO and live until cutover.

Source designs (full detail, verification tags [V]/[U], citations) live in the shared notes folder
`...\side-session-notes\local_2aa79775-83d6-4aae-a2f9-2e2b9fd6dda5\`:
`dots-ghosts-design.md`, `dots-monster-ai-design.md`, `dots-persistence-and-ui-bridge.md`,
`dots-linux-server-and-burst.md`, `dots-test-harness-plan.md`, `prior-survey-*.md`, and the owner's 20 Oct draft
`C:\Users\dlaco\.claude\plans\i-need-you-to-magical-parasol.md` (stages 0-10, hybrid). This doc supersedes the draft
where they differ: **full ECS direction** (Entities, Entities Graphics, Burst, Jobs), **Emerald AI dropped**, new ECS
monster brain, Burst brain pulled forward from "Stage 10, maybe" to a core milestone.

## 0. Ground rules (apply to every milestone)

- No push to main, no publish, never the real save DB (7801) or real server (7777/7778/8090). Test ports: DOTS test
  server 7797/8097, measure 7790/8091, save service `--test` 7802 / `krytical_nova_test`.
- No Editor open in the clone without Master's OK (31 GB RAM, other sessions). One Editor + one **built** client + built
  test server; no MPPM Player 2 unless asked.
- No LFS growth: no binaries committed in the clone except what the game needs; tell Master the size before any push.
- CLAUDE.md applies: layers, `dependency_map.py --check` (0 uphill, 0 two-way), `compile_check.py` and `--server`,
  Encyclopedia update after every landed task, `PlayerSystemsSetup` rows, icon library, economy/ledger/SaveTogether rules.
- **Every milestone ends with:** dependency check + compile checks, the system's tests, a 2-client test against the
  dedicated test server with Game-view screenshots, `graphify update .`, this checklist ticked, Encyclopedia updated.
  The next milestone does not start until a 2-client test passes.
- **Burst DO/DON'T** (full 20-point list in `dots-linux-server-and-burst.md`): predicted code is `FloatMode.Deterministic`
  + `FloatPrecision.High` and integer-based (mm, hundredths, ticks); no managed types/statics/exceptions in `[BurstCompile]`
  code; a lint forbids Burst files referencing wallet/ledger/save/inventory types; ECS code needs a normal compile (Fast
  Script Reload does not cover it); verify Burst really compiled in builds (silent skip on invalid target; fail the build if
  `lib_burst_generated` is missing); same Unity+Burst version on client and server; N4E is **not** deterministic: expect small
  corrections, quantise ghost fields, let the server correct.

## 1. Decisions already made (owner / Master)

1. Move to Netcode for Entities on Unity 6000.6 with Entities, Entities Graphics, Burst, Jobs; simulation state is ECS.
2. Presentation that cannot be ECS stays GameObjects: Animancer, KINEMATION, Synty Sidekick, Cinemachine, Feel, UI, VFX.
3. Emerald AI is replaced by an ECS-native monster brain (design in section 6).
4. Separate clone; real project untouched; merge back only the correct changes (section 9).
5. Server decides hits itself with lag compensation; clients stop reporting hits.
6. Linux dedicated server (Hetzner US tests, account opened, nothing created) must honour the host-layer contract in
   `online-linux-cloud-servers-plan.md` (flags, one UDP + one TCP port per map server, SIGTERM saves all, health, ticket).
7. Authority model unchanged: server decides progression; observers only present; GM tools stay.

## 2. Architecture in one picture

```
SERVER WORLD (ECS, headless, Linux)                  CLIENT WORLD (ECS)                       GameObjects (client only)
player ghost (owner-predicted)             --->      predicted(own)/interpolated      --->    presentation body = Player.prefab (Animancer,
 PlayerInput, ServerOrder, PathCorner buf,                                                    Sidekick, warp, Feel, camera) + UI view-models
 MoveState, MotionPlan, Vitals, TeleportSeq,                                                  (X.Local + Changed kept, fed by bridge systems)
 owner-only buffers (cooldowns, books), event rings
monster ghosts (interpolated, non-predicted)         interpolated                      --->    pooled GO views near camera / EG far
drop/kiosk ghosts (static)                                                                    Entities Graphics for drops
SERVER-ONLY managed bodies: "ServerBody" = Player.prefab with NetRole.ServerBody (rules, saves, CombatEntity, ISaveParticipants)
```
Key rules (details in ghosts + persistence docs):
- **One prefab, three roles** (`NetRole`: ServerBody / ClientView / Offline-gone). Server body keeps `PlayerSaveAgent` and
  all `ISaveParticipant`s; the ghost holds no save state; save/ledger/SaveTogether stay main-thread managed code, with
  Burst systems only writing request/event records that a managed system applies.
- **`PlayerKey`**: server-minted monotonic id in Core; never the raw `NetworkId` (N4E reuses it [V]). Every async
  continuation re-checks the registry. `ServerAccounts`, `RpcBudget` re-keyed to it.
- **Prediction state is integers** (path progress mm, HP/Chi hundredths, ticks); paths planned once per order outside rollback;
  `TeleportSeq` snaps; presentation keeps a decaying visual offset replacing `PlayerMoveAnticipation`.
- Jump/vault/wall-run/waterstride/death = `MotionPlan{Kind,StartTick,Duration,From,To}` evaluated by tick; owner does not
  predict vault/wall-run at first (recommended).
- Mounts and pets are fields/owner-only books on the player ghost; NPCs are server `NpcAnchor` entities (not ghosts);
  housing stays local behind `IHomeAuthority`; drops/kiosks are static ghosts.
- One-off events: tick-stamped ring buffers + non-rolled-back client cursor; owner RPC for gameplay-significant ones.
- Requests: one unmanaged `IRpcCommand` per call -> managed receive system -> `PlayerKey` -> server body -> existing `ServerX`.
  No event bus, no DI, no service container (CLAUDE.md).
- **Layers:** Core gets only plain C# (`PlayerKey`, `NetSession`, `IServerLeaving`, `IWorldTargets`) and drops Netcode refs.
  Networking (Infrastructure) is type-agnostic, reaches Gameplay only through `ServerHooks`. Each Simulation system keeps
  a `Net/` folder in its own asmdef. UI/GameFeel get no Entities refs. New: `KryticalNova.Monsters` (Simulation).

## 3. What ports straight vs is rewritten

| Ports straight (no netcode) | Rewritten (network edge) | New |
| --- | --- | --- |
| SaveService + Postgres, `ServerSave.SaveTogether`, `EconomyLedger`, kill test, 85 ScriptableObject types, `CombatCalculator` (plus Burst port with parity test), `JoinRules`, `UnityToken`, `ServerBans`, ServerApp, launcher, rule code of all gameplay systems | 59 NetworkBehaviours -> `X.cs` rules + `X.Net` edge (216 RPCs -> `IRpcCommand`, 49 NetworkVariables/Lists -> ghost components/buffers or paged view RPCs, 37 `INetworkSerializable`), `NetworkConnectionManager` -> approval RPC chunks + `PlayerKey`, `SpatialInterestManager` -> N4E relevancy/importance, `PlayerMoveAnticipation` + `NetworkTransform` -> predicted movement, `HostileNet`/Emerald -> Monsters, WorldDrop/Kiosk/WorldEnvironment/MusicConductor/ZonePortal, `RpcBudget`, MeasureBot -> thin clients, 44 scene NetworkManagers -> Bootstrap scene | Bootstrap, `KnBootstrap : ClientServerBootstrap`, ghost subscene, `MapAnchor`, `NetRole`, `PlayerKey`, `NetClock`, Monsters system, Linux/IL2CPP server build path, NetCodeTestWorld-style helpers |

## 4. Milestones (each gated by a 2-client test before the next)

Pass gate per milestone (thresholds in `dots-test-harness-plan.md`, proposals until M2 tuning): 2 clients (built client +
Editor client) against the Windows dedicated test server, 60/150/250 ms, no errors in logs, dependency check clean,
prediction error p95 <= 0.05 m at 60 ms, tick p95 < 15 ms with 102 monsters (from M6).

- [ ] **M0 Docs & seams in the clone.** This plan; Encyclopedia rows (Networking "moving to Netcode for Entities", glossary:
  Ghost, Server Body, Presentation Body, Map Anchor, Monster Brain, Nav Grid); `PlayerKey`, `NetRole`, `NetClock`,
  `NetSession`, `IServerLeaving`, `IWorldTargets` seams, `X.cs`/`X.Net.cs` split with a stub-base compile gate; Core and UI
  asmdefs lose `Unity.Netcode.Runtime`; ~96 string/array RPCs -> fixed/paged. All still compiling on NGO (so seam work can
  be offered to main as ordinary PRs, see section 9).
- [ ] **M1 Packages + Bootstrap + Linux spike + early spikes (review items 4, 6, 10, 20).**
  Also in M1 as EditMode/Layer-C tests (no Player, no scene): S1 replicated buffer GhostFields, S3 client plan survives rollback, S9 RPC size,
  H0 real test-world class names, **S11 subscene inside an Addressable map scene loads on client+server with matching prespawned-ghost hash**
  (owner decision "Maps are Addressable (B)"; unchecked until now). **Compile gate:** add the Entities/Netcode source-generator + analyzer DLLs to the
  `compile_check.py` csproj template and prove it on a deliberately broken `IJobEntity`; otherwise the gate becomes a Unity batch compile + Burst AOT log scan
  (Windows + Linux). Pin the exact Editor patch (6000.6.3f1) for main, clone, client and server builds with a build-id gate; read the patch release notes first.
  M1 gate = connect/approve in the netcode test world (layers A-C only); the first real 2-client test is M2 (fixes the earlier contradiction between this line and the per-milestone rule). Add entities, entities.graphics, netcode, physics, burst, collections,
  mathematics (versions from the 6.6 registry, verified on import; manifest currently has none). Bootstrap scene,
  `KnBootstrap` (client+server in player/host build, server-only on dedicated), tick 30/30. **Batch spike (S-Linux):** an empty
  N4E server builds for Linux x64 Mono and IL2CPP with Burst, subscene baking, Entities Graphics excluded on server; `lib_burst_generated`
  check. Needs Master's OK for the Editor/batch run.
- [ ] **M2 Connection & identity.** Approval via `IApprovalRpcCommand` chunks (Unity token > 1 MTU), async approval with raised
  timeout (5 s default), `JoinRefusedRpc`, roster path (decision Q-roster), encryption driver, Relay path, travel ticket
  (world-service token) in the connect RPC, SIGTERM -> save all -> exit, CLI flags/ports contract, ServerControlPage reads N4E
  connections. Tests: `NetworkSetupTests` replaced; NetCode test world; two clients join and leave.
  **M2a (written 29 Oct, NOT yet compiled or run; needs the first Editor batch):** `Assets/_Game/Networking/Entities/`, assembly `KryticalNova.Networking.Entities`.
  - [x] Packages in `Packages/manifest.json` (entities, netcode, transport, collections 6.6.0; mathematics 1.4.0; burst 2.0.0; test-framework was there). Lock file left for Unity.
  - [x] New assembly + test assembly (`Runtime/KryticalNova.Networking.Entities.asmdef`, `Tests/Editor/...Tests.Editor.asmdef`), part of the Networking system for `dependency_map.py` (folder rule).
  - [x] `KnBootstrap : ClientServerBootstrap` (`Runtime/KnBootstrap.cs`): front end = local world only; `UNITY_SERVER` = server world only (port/-port, -pageport, role, map from `KnServerSettings`); `StartHost/StartClient/Stop`; 30/30 ticks and a 15 s handshake limit (`KnNetDefaults`).
  - [x] `KnServerSettings` (`Runtime/KnServerSettings.cs`): reads `server-settings.json` keys and flags like the NGO server, no NGO type.
  - [x] `ConnectionKeySystem` + `ConnectionClosedSystem` (`Runtime/ConnectionKeySystem.cs`, `ConnectionClosedSystem.cs`): `PlayerKey` per connection (cleanup component, Core `PlayerKeys` registry), close hook with empty handler list and body lookup into `ServerLeaving.Notify`.
  - [x] Approval: `KnApprovalRequest` chunks, `KnApprovalAssembler` with pre-approval budget (chunks, bytes, seconds), `IKnJoinCheck` (polled, accept-all default), `JoinRefusedRpc` + delayed disconnect, client send system, `KnApprovalSetupSystem` (`Runtime/KnApproval*.cs`, `IKnJoinCheck.cs`, `KnRefusalSystems.cs`, `KnClientApprovalSendSystem.cs`).
  - [x] `KnTestWorld` brought over (`Tests/Editor/KnTestWorld.cs`); tests: `KnConnectionTests` (connect, 3-chunk approval, async check, too many chunks, too many bytes, timeout, refused by the check, PlayerKey minted once and never reused, close hook once), `RpcSizeLintTests`, `KnPureTests`.
  - [ ] Evidence: compile + all of the above passing in a batch run (record test names and counts here), then the M2a completion line to Master.
  **M2b-1 (written 29 Oct, NOT yet compiled or run; first Editor batch needed):** all under `Assets/_Game/Networking/Entities/` unless a path says otherwise.
  - [x] Seam: pure rules moved from Networking to Core, namespace `KryticalNova.Networking` kept (`Core/Runtime/Net/Join/`: `JoinRules`+`GameBuild`, `AccountProof` (`UnityToken`, `UnityKeys`, `AccountIds`, `ServerAccounts`), `ControlPageRules`, `StopEndpointRules`, `ServerStatusProtocol`). Commit 86ec4ad64. No behaviour change on NGO.
  - [x] Seam 2: `JoinRules.PayloadText/TryReadText/Judge/Words` (7th part = travel ticket; NGO `TryRead` still refuses it), Core `IBanLookup` (`BanLookup.Current`, filled by `Networking/Runtime/Safety/ServerBansLookup.cs`), Core `IServerShutdown` (`ServerShutdown.Handler`, filled by `ServerSaveHooks` with the control page's `FlushAll`). Commit e33750f75.
  - [x] `KnJoinCheck : IKnJoinCheck` (`Runtime/KnJoinCheck.cs`): parse, build/version, server full, Unity token (`UnityAccountProof`, polls `Task.IsCompleted`, never awaits), bans (Core slot), travel ticket (`IKnTravelTicketCheck`, `AcceptAllTickets` stub until the world service exists), account + old-account claims on the connection (`ConnectionAccount`, copied to Core `ServerAccounts` by `ConnectionKeySystem`, forgotten by `ConnectionClosedSystem`). New refusal reasons 7-11. `JoinRefusedRpc.Detail` is now 512 bytes (ban texts are long); `KnFixed` cuts text safely.
  - [x] Stop path: `KnStopMachine` (pure state machine: door, save, disconnect, quit 0 after 30 ticks; idempotent), `KnStopRequests` (thread-safe flag), `KnStopSystem` (server world), `KnSignals` (Linux SIGTERM via libc `signal`, Windows console handler; none in the Editor), `KnServerStopping` gate in `KnApprovalAssembler`.
  - [x] `KnServerEntry` (called by `KnBootstrap` on `UNITY_SERVER`): settings keys `saveService`, `saveServiceKeyFile`, `certificateFile`, `privateKeyFile`, `serverName`, `devBots`, `cloudProjectId`; flags `-role -map -port -pageport -devbots -savekeyfile -certificate -privatekey`. `KnServerPage` = TCP page on the page port (`GET /status`, `/health`, loopback `POST /stop` with `X-KN-Key`) + UDP "how are you?" answer on the same number; `KnServerStatus`/`KnStatusSystem` = snapshot written once a second, read by the threads.
  - [x] Thin-client bots (`KnDevBots`, whole file in `#if KN_DEV_BOTS || UNITY_EDITOR`; the three approval systems drop `ThinClientSimulation` outside that define; `Enabled` must also be switched on, server side `devBots`/`-devbots`).
  - [x] Tests (Tests/Editor): `KnJoinCheckTests` (refusals for ban, wrong version, server full, bad token, roster question; token pending then approved; ticket slot; Unity token proof with a signed test token; account reaches Core and is forgotten; thin-client guard and a source lint that bot code is compiled out), `KnStopTests` (machine with fakes, idempotent requests, loopback-only/key/202 rules, page over a real loopback socket, stop with 2 players connected: save first, then disconnect, quit once, join during stop refused, status snapshot), `KnServerPureTests` (settings, stop key, entry options, KnFixed, join text round trip).
  - [ ] Evidence: compile + the new tests passing in a batch run (record names and counts here); `dependency_map.py --check` was run after the code and was clean (206 links, 0 uphill, 0 two-way); `compile_check.py` NOT run (this assembly is outside it until the source-generator support lands).
  **M2b-2 (written 29 Oct, NOT yet compiled or run; first Editor batch needed):** all under `Assets/_Game/Networking/Entities/` unless a path says otherwise. Notes in section 28.
  - [x] Seam (commit af8161224): Core gets `RosterWire` (pack/unpack of the roster text), `IServerRoster` + `RosterLookup.Current` (the slot Persistence fills; `ServerHooks.Roster` now forwards to it) and `ServerCertificates` (authority certificate, server name, default folder, read cert + key); the NGO `ServerEncryption` and `RosterLink` forward to them. No behaviour change on NGO.
  - [x] Roster path: `KnRosterReply : IApprovalRpcCommand` pages (<= 480 chars, <= 16 pages), `KnRosterPages` (split + assembler, any order), `KnRosterAnswer` (rules, no ECS), the join check's `Question` verdict (same proof and ban check, no ticket, no place), the assembler answers it and closes; client `KnRosterClientSystem`, `KnRosterQuery` (owns the short-lived world, `Task` result, destroys the world), `KnRosterClient.Ask` (one at a time, hidden pump). Front-end use: see the class comment of `KnRosterClient`. UI not edited.
  - [x] Transport: `KnDriverConstructor : INetworkStreamDriverConstructor` (plain UDP / DTLS with our authority / Unity Relay, chosen per world through `KnTransport.Assign`), `KnTransportPlanner` (pure rules), `KnServerSettings.connectionMode` + `plainTransport` (`-connectionmode`, `-plaintransport`), `KnServerEntry.PrepareTransport`, `KnBootstrap.StartClient(.., encrypted)` and `StartClientRelay`.
  - [x] Server page: `GET /players` and `POST /kick?player=<PlayerKey>` (loopback + the save-service key, like `/stop`), `KnPlayerRow` rows in the status snapshot (key, id, name via `KnServerStatus.NameOf`, ping, map; no account, no address), `KnKickRequests` + `KnKickSystem` (main thread closes the connection). Bans, GMs, announce, save, log and the flags list of the NGO control page are NOT ported yet (they need the systems they read; see section 28).
  - [x] Travel ticket: `WorldServiceTicketCheck` (`POST world/redeem` of the world service in the save service, polled, fail closed, account must match, arrival kept in `KnArrivals`), `KnHttpTicketTransport`, `KnServerEntry.BuildTicketCheck`.
  - [x] Tests (Tests/Editor): `KnRosterTests`, `KnTransportTests`, `KnControlPageTests`, `KnWorldTicketTests`, plus the three roster tests in `KnJoinCheckTests`.
  - [x] `compile_check.py`: optional Entities / Netcode source-generator and package-DLL support (not run yet; usage in its docstring).
  - [ ] Evidence: compile + all of the above passing in a batch run (record names and counts here).
- [ ] **M3 Player ghost & prediction (split per review item 12: M3a walk/run/stop, teleport, save/identity/look with passthrough saves (playable);
  M3b jump/death `MotionPlan` + warp spike S7; M3c vault/wall-run/waterstride, follow, skill walk-in. Owner "feel" checkpoint after M3a and after M4.)**
  Movement must scale by tick fraction (or run on full ticks only and interpolate in presentation; decide in S3). `PlayerInput`, `ServerOrder`, `PathCorner`, `MoveState`, `MotionPlan`, `TeleportSeq`;
  predicted move system (integers, deterministic Burst); presentation body follows; walk/run/stop/jump/vault/wall-run/follow/
  skill walk-in/death; SaveAgent load/save, identity, equipment, look (`LookVersion` + cache) so the clone stays playable;
  unported systems run as local stubs behind a banner. Spikes S3 (path outside rollback), S4 (Windows vs Linux parity).
  **M3a-1 (written 29 Oct, NOT yet compiled in Unity or run; first Editor batch needed):** assembly `KryticalNova.Player.Entities` under `Assets/_Game/Gameplay/Player/Entities/` (Runtime + Tests/Editor), namespace `KryticalNova.Player.Entities`. Notes in section 30.
  - [x] Components, all integer prediction state: `PlayerInput` (IInputComponentData: click goal mm, `ClickSeq`, Run, `Click`/`Stop` InputEvents), `ServerOrder`, `MoveState`, `PathCorner` buffer (SendToOwner, mm ints, <= 32) + `PathPlan` header, `PlayerPose` (mm + centi-degrees), `TeleportSeq`, `MoveStats`, `PlayerGhostTag`; non-replicated `ClientPlanCorner`/`ClientPlanHeader`, `PlayerMoveDiag`, optional `PlayerMoveTrace` ring; `PlayerMoveSettings` (singleton or defaults).
  - [x] Ghost authoring (`PlayerGhostAuthoring`, `PlayerGhostPrefabAuthoring`) in the non-Editor asmdef; the test world gets the prefab through a SubScene (`PlayerTestAssets`).
  - [x] Systems: `PlayerInputGatherSystem` (client, `GhostInputSystemGroup`, plans once per click with `IPathPlanner`), `PlayerServerPlanSystem` (server, validates, fills `PathCorner`, writes `ServerOrder`, refuses with `RefusedSeq`), `PredictedMoveSystem` (Burst `IJobEntity`, `PredictedSimulationSystemGroup`, Deterministic/High, full ticks only), `PlayerTeleportSystem` (server). Planners: `StraightLinePlanner`, `NavMeshPathPlanner`.
  - [x] Tests: `PlayerMovementPureTests` (no world), `PlayerGhostWorldTests` (server + 2 clients, 0 and 100 ms: click/arrive/0 mismatches, rollback count, observer interpolation, stop, teleport, client plan survives rollback, refusal, server order override, walk/run, 4 random-sequence property seeds, 200-ghost budget, prefab shape).
  - [x] Evidence: compile in Unity + all of the above passing in a batch run (record names, counts and the budget number here); `compile_check.py` result in section 30. (Green 29 Oct: 1166 passed.)
  - [x] M3a-2a (presentation follow, registry, body adapters, click source): section 34; green 29 Oct (1194 passed).
  - [x] M3a-2b steps 1-4 (role adapter, assembler, passthrough save, go-in-game pages): section 38, evidence section 39.
  - [x] M3a-2b steps 5-9 in the Editor (server body lifecycle, leave, client view, observer look): section 40, evidence section 41.
  - [x] M3a-2b layer B: B1/B2 PlayMode smoke PASSED (section 44); R8 ghost subscene done (sections 42, 47). B3 the built 2-client gate is PARKED (section 48, `Reference/Unity-Issue-Burst-Linux-AOT.md`). Was: B3 the built 2-client gate (Behavioral Protection off or the Linux route), the `PlayerGhostPrefab` subscene in the Bootstrap scene (R8), the PlayerPrefab source on a DOTS dedicated server. Then the owner "feel" checkpoint.
  - [ ] Left for M3a-2 and later: presentation body following the ghost (`GhostViewFollowSystem`, snap on `TeleportSeq`), adapters for `IPlayerBody`/`LocalPlayer`, SaveAgent/identity/look passthrough, mouse + camera `IPlayerCommandSource`, installing `NavMeshPathPlanner`, `MovementSettings` -> `PlayerMoveSettings` baking, GM speed/mount/rank into `MoveStats`, Chi payment for Run on the server, `RelevancyFillSystem`, jump/death `MotionPlan` (M3b), vault/wall-run/waterstride/follow/skill walk-in (M3c).
- [ ] **M4 Combat with lag compensation (decision: NO Unity Physics package; server-only `PositionHistory` ring of 16 ticks of position+capsule on every
  hittable ghost, tested at the attacker's interpolation tick; removes spike S5). Includes the Wallet vertical slice + one Trade with disconnect mid-trade,
  kill test and golden ledger on the test DB (review item 13).** `Vitals` ghost; melee = input + server window + Unity Physics history test (spike S5:
  confirm 6.6 API names/history depth); skills as inputs; hit moments from data ticks; HitEvent ring -> `ClientPresentationRegistry`;
  duels/PvP/`CombatHostility` as pure-data function; Combat Dummy as first ghost target.
- [ ] **M5 Monster system prototype (new brain).** Section 6. P0 EditMode sim tests (no netcode), P1 ghost + view, P2 reward bridge.
- [ ] **M6 Hostiles in full.** All roster species via `MonsterArchetype` from `HostileDefinition` (ids kept), level scaling, zones,
  sleep, respawn, loot/XP through managed reward bridge with ledger rules; nav grids for the zones; bot soak with 102 monsters.
- [ ] **M7 World objects.** Drops (EG), Kiosks (`KioskService`), world clock/weather singleton, music-state ghost (World layer), ZonePortal,
  GM tools (commands + GM window/control page controls both).
- [ ] **M8 The remaining ~34 menu systems**, in four tested groups (edge swap only; rules/saves/UI untouched):
  (1) Wallet, Inventory, Loadouts, Loot, Flask; (2) Storage, Shopping, Kiosk, **Trade** (SaveTogether/ledger; `TransactionLedgerTests`
  + kill test on test DB only; new tests: disconnect mid-trade, crash after Commit, NetworkId reuse, pending roster call at
  disconnect); (3) GearWork, Crafting, Weaves, Restyle/Holoweave, Nova, Serums, XP boosts, Progression, Skills, Autobuff;
  (4) Chat, Contacts, Crew, Party, Sponsor, Duel prompts, Emotes, Pet tricks, Crate, Housing, Recall, Cadence, Mounts.
- [ ] **M9 Scale & Linux parity.** Delete `SpatialInterestManager`; distance importance (48 m tiles), relevancy, per-type send
  rates, war-map crowd preset; thin-client bots (many per process) replace MeasureBot; Linux IL2CPP server on WSL2/cloud test
  VM passes the parity checklist (section 8); soak with balanced ledger.
- [ ] **M10 Compare, merge, cut over** (section 9) on the owner's go-ahead.

## 5. Spikes that must be proven early (from the ghosts design)
S1-S10 listed in `dots-ghosts-design.md`; the blocking ones: S3 path planning outside rollback; S4 Windows-vs-Linux
prediction parity (replay + state hashes); S5 lag-compensation API and history depth in 6.6; rollback pop at 34 m/s mounted run;
Entities Graphics skinned playback (only needed if visuals option (b) is chosen); FixedString/FixedList as GhostField (docs [V]).

## 6. New monster system (Emerald replacement) - summary
Full design: `dots-monster-ai-design.md` (20 sections).
- **Brain:** hierarchical FSM (Idle/Patrol/Alert/Chase/Attack/Flee/Return/Dead) in one Burst `IJobEntity`; weighted ability pick; reason:
  1:1 with Emerald's state model, O(1)/monster, debuggable. Not behaviour tree/utility.
- **Data:** `MonsterArchetype` ScriptableObject (copied from `HostileDefinition` by an editor tool, ids kept) baked to blob assets;
  `HostileStatCurve` baked to a level x tier `StatRow` table (level scaling).
- **Aggro:** uniform grid hash of players, staggered scan every 4th tick; no raycasts. **Nav:** baked 2.5D walkability grid tiles
  (0.5 m) + straight-line fast path + budgeted Burst A* (NavMeshQuery is Experimental.AI/deprecated). **Combat:** server-authoritative,
  tick timers, Burst port of `CombatCalculator` with a 10k-tuple parity test; player damage through existing `CombatEntity.TakeDamage`
  via a managed bridge (+1 tick) until players are ECS; one idempotent `MonsterKilledEvent`; ECS never touches wallet/ledger/SaveTogether.
- **Net:** interpolated, non-predicted ghosts (float drift harmless), quantised, relevancy radius + distance importance, telegraph from
  `AttackStartTick`. **Client:** pooled GameObject views (existing model/Animator/dissolve) for the nearest ~32, Entities Graphics later if profiling demands.
- **Sleep:** per-zone flag toggling enableable `MonsterActive`. Budget < 0.3 ms/tick for 102 monsters (estimate, to measure).
- **Prototype slice "Prowler"** (wolf-like melee; no wolf in roster, stand-in clips): `Assets/_Game/Gameplay/Monsters/{Runtime,Data,Editor,Tests}`,
  asmdef `KryticalNova.Monsters` (Simulation) + client-only `KryticalNova.Monsters.Presentation`. P0 sim tests, P1 ghost+view, P2 reward dry run.
- Assist/group-pull and Flee: data fields present, off at first.

## 7. Persistence / save bridge - summary
Details: `dots-persistence-and-ui-bridge.md`. Server body owns saves; disconnect order `IServerLeaving` -> `SaveAndRelease` -> unlock ->
destroy body -> ghost (no `LinkedEntityGroup`); disconnect event from `ConnectionEventsForTick`; blocking connect-time load kept (5 s
timeout) in phase A; `SaveBatch` GUID per all-or-nothing change; SaveService/Postgres/ledger/kill test unchanged. UI keeps
`X.Local` + `Changed` on managed view-models fed by one bridge system per gameplay system; one-shot events to `ClientPresentationRegistry`
sinks, de-duplicated by our own eventId; 12 UI/GameFeel files move to `NetSession`/`IWorldTargets`/`LocalPlayer`.

## 8. Current-setup parity on Linux (checklist; links `online-linux-cloud-servers-plan.md`)
Proven on the Linux IL2CPP+Burst Entities server (WSL2 first, Hetzner US test VM later; test DB only), by the same host-layer scripts as the NGO build:
- [ ] Host contract (from server-plan, via Master 29 Oct): flags `-batchmode -nographics -role map -map <name> -port <udp> -pageport <port+1 status>`;
  `server-settings.json` keys `saveService`, `saveServiceKeyFile`, `certificateFile`, `privateKeyFile` (`publicAddress` later; `serverList` empty;
  `mapServers` [] until travel exists); game UDP on the settings port, status page on port+1; SaveService only on 127.0.0.1:7802 test / 7801 real, never public.
  Owned by M2: `KnBootstrap` (server-only world) parses these flags/keys before creating the world and issues `NetworkStreamRequestListen`;
  the page is managed code reading a snapshot. **Unverified for Entities** until the M1 spike runs on Linux (Linux build has never run on Linux).
- [ ] systemd `KillMode=mixed`, 90 s stop: SIGTERM (or `wantsToQuit`) must save everyone and exit < 90 s (budget it with 100+ players: batched `SaveTogether`, timeout test).
- [ ] Deploy scripts (`Assets/_Game/Networking/Deploy~/`, branch `owner/savesvc-linux`) run unchanged against the Entities build; a differing flag/port is a build bug, not a script change.
- [ ] SIGTERM saves everyone, releases locks, exits within the set time (save in `wantsToQuit`/shutdown system; verify).
- [ ] Health: control page answers; world service sees the 10 s presence report (managed snapshot, no ECS access from the HTTP thread).
- [ ] Save service (.NET linux-x64 publish) + Postgres over local socket: load on join, periodic save, save on leave; account/character keyed saves load unchanged.
- [ ] `ServerSave.SaveTogether` + ledger: Trade, Kiosk sale, Crew founding; `TransactionLedgerTests` and `kill_test.py` on the test DB.
- [ ] Roster on the server (character select/create/delete) via the chosen roster path.
- [ ] Map servers: one per map, travel ticket (use-once, world-service signed) carried in the connect RPC; ZoneTravel between two map servers.
- [ ] GM tools: commands + GM window + control page controls; GM roles by password.
- [ ] Launcher / server list: list entry, Enter joins last server, version/hash check (netcode setup hash equivalent).
- [ ] Relay mode (Unity Relay) via `RelayServerData` path and Direct mode.
- [ ] Bans + encryption + `RpcBudget` limits keyed by `PlayerKey`; refusal reasons via `JoinRefusedRpc`.
- [ ] Nightly backups (Postgres copy) and restore drill on the test DB (`krytical_nova_drill`), unchanged by the netcode.
- [ ] Windows server vs Linux server: replay with state hashes and prediction-error comparison (harness H-series).

## 9. Merge-back plan (exact changes to bring to main; nothing merged until owner's go-ahead)
Order so that main is never broken and the final merge stays small:
1. **Netcode-agnostic seams, offered to main early as ordinary NGO PRs (no behaviour change):** `PlayerKey`, `NetRole`, `NetClock`,
   `NetSession`, `IServerLeaving`, `IWorldTargets`; `X.cs`/`X.Net.cs` splits with the stub-compile gate; Core/UI asmdefs without
   `Unity.Netcode.Runtime`; fixed/paged RPC arguments; `MapAnchor` written by editor builders; one placement entry point and one
   server-walk call; Burst port of `CombatCalculator` with parity test (pure, tested on NGO). (Owner to choose: seams in main early
   [recommended] or all at the end.)
2. **At cutover (PR from `dots/netcode-entities`):** new packages + manifest/lock; Bootstrap scene + `KnBootstrap`; ghost subscenes;
   all `X.Net` edges; Monsters system + data + nav grids; test assets; server build changes (`ServerBuildBuilder` IL2CPP switch,
   baking-exclusion precheck, Burst-present check, linux-x64 SaveService publish); thin-client bots; docs/Encyclopedia/Server Guide/Dependency Map.
3. **Removed at cutover:** NGO package, `NetworkManager.prefab`, `DefaultNetworkPrefabs.asset`, `SceneObjectKeeper`, `SceneNetworkGuard`,
   `OfflinePlayerHandoff`, NGO prefab handlers, `GameplayPrefabsSetup.SpawnedPrefabs` rule (-> ghost subscene), NGO YAML tests,
   Emerald integration (`HostileBrainLink` etc.) once all species run on Monsters.
4. Weekly `git merge main` into the clone; `git diff main...dots/netcode-entities` summary page for the owner before cutover.
5. CLAUDE.md updates at cutover (header, SpawnedPrefabs rule, Burst/Entities rules); memories updated; publish via the normal Test -> Live path.
6. LFS: report the size of any LFS delta before the PR is pushed.

## 10. Owner decisions needed (recommendation first)
See the report to Master; answers get recorded here. Q1 prediction strictness; Q2 Linux backend (IL2CPP cloud/Mono dev); Q3 x64 only; Q4 batch spike now;
Q5 roster path; Q6 HP/Chi truth on server body; Q7 reconnect = kick old; Q8 blocking connect load; Q9 UI keeps `X.Local`; Q10 hostile rendering
(pooled GO near camera, EG far); Q11 nav grid; Q12 prototype wolf stand-in; Q13 monster damage bridge; Q14 assist/flee off; Q15 no player avoidance;
Q16 no vault/wall-run prediction; Q17 hit moments from data; Q18 economy values over RPC pages; Q19 second client = built client; Q20 Linux parity on WSL2;
Q21 thresholds accepted; Q22 bots skip sign-in on test server; Q23 seams to main early.

## 11. Risks
Rollback pop at 34 m/s mounted run; unverified 6.6 lag-compensation API; Burst silently skipped on invalid target; Entities/Netcode version drift
on 6.6 vs 6.7 (renames in N4E 7.0); Editor memory (31 GB); branch drift (weekly merges); approval RPC 5 s timeout vs async Unity token check;
monster visuals cost if GameObject views dominate (fallback: Entities Graphics); one large cutover PR (mitigated by early seams).

## 12. Advisor review (Opus, 29 Oct) and how the plan answers it
Verdict: direction sound, six blockers fixable in docs. Full text kept by the mini-boss; resolutions:
1. **HP/Chi single writer:** ECS `Vitals` is the truth; `PlayerResources`/`CombatEntity` become main-thread facades over `Vitals`; `CaptureSave` reads through the facade
   (save format unchanged); dev assert that nothing else writes `Vitals`. (Overrides the persistence worker's recommendation; Q6 for the owner.)
2. **NetworkId reuse + lingering ghost:** on connection close, before anything async: invalidate `GhostOwner`, remove command target/input buffer, make the ghost irrelevant; then save, then destroy. Test added.
3. **Monsters use `PlayerKey` everywhere** (threat table, tag holder, kill credit, target); bridge drops the event if the key is gone.
4. **Compile gate** replaced/proven in M1 (above).
5. **Save passthrough mode:** an unported `ISaveParticipant` keeps its loaded JSON part byte-for-byte and is marked not-dirty; test: load->save of an untouched character is byte-identical for every part.
6. **Spikes moved into M1** (above) incl. new S11.
7. Lag compensation via own `PositionHistory`, not Unity Physics (M4). 8. Partial-tick scaling (S3). 9. All statics reset in `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (domain reload is off); lint/test.
10. Editor patch pinned. 11. **One combat formula:** rewrite `CombatCalculator` once as a Burst-compatible static on blittable structs, called from managed and Burst; parity test guards the refactor.
12. M3 split. 13. Economy vertical slice early. 14. Pre-approval budget per connection entity (chunk count, bytes, time); raise handshake timeout; poll async token verify, never await.
15. Connect-time save load: HTTP fetch off-thread (apply on main) before M9, or refuse joins while the service is slow.
16. Monster view budget always includes local target, anything targeting/striking the local player, and anything within aggro range + 10 m; rest get impostors.
17. Monster repaths via static `NavMesh.CalculatePath` (not deprecated; only `NavMeshQuery` is) from a budgeted managed queue at 1 Hz; the baked grid is kept for height/separation/LOS or as fallback; check Zone01 for multi-level ground first. (Changes the monster design's nav pick: Q11 revisited.)
18. Settle contradictions between the design docs (send rate 10 vs 15 Hz, int vs float health, relevancy radii; one `RelevancyFillSystem` is the only writer of `GhostRelevancySet`; monster systems run before ghost send; monsters are Dynamic-optimised ghosts) - owner of the fix: the integrator, before M5.
19. Event rings: on ghost spawn set the cursor to the current Seq without playing. 20. Linux IL2CPP cross-build toolchain verified in M1; hard-fail if `lib_burst_generated` missing; measure only in builds.
21. Player-ghost snapshot byte budget; slow-changing owner data goes to RPC pages.
22. Honest scope: rules, saves and economy stay on a `Player.prefab` server body (hybrid on the server); ServerBody strips Animancer/IK/Sidekick/Feel by `NetRole`; lint bans transform/`agent.Warp` writes on ServerBody.
23. Entities Graphics for drops deferred (YAGNI) unless the owner insists; drops are ghosts with pooled GameObject views at first.
24. Early seams to main (Q23 = yes recommended) + a script listing `[Rpc]`/NetworkVariable added on main since 3ad5ba303; after that, new edges on main follow the `X.cs`/`X.Net.cs` split.
25. Dev-only bot sign-in bypass needs a release-build test proving it is compiled out; thin clients cannot test client relevancy/prediction cost (use real clients for those).
Missing items added: rollback plan (if a milestone fails: stop, keep NGO on main, nothing merged); owner checkpoints after M3a and M4; encryption (DTLS) and Relay driver spikes in M2;
`DedicatedServerOptimizer` 30 fps cap vs N4E `TargetFrameRateMode`; Editor PlayMode Tools = Client for the Editor client; RAM cost of subscene baking in the Editor.

## 13. Owner answers (29 Oct 2026) and working defaults
Answered by the owner: Q1 = (a) deterministic Burst + quantised/integer state on predicted systems only; Q2 = (a) ECS `Vitals` is truth; Q3 = (a) seams go to main early as
ordinary NGO PRs; Q26 = (a) run the 30-minute Linux IL2CPP+Burst spike.
**Defaults (owner may change)** = the recommended option of every other question in `dots-owner-questions.md` (Q4-Q25, Q27, Q28): IL2CPP cloud / Mono dev; x64 only;
hybrid server body accepted; roster via throwaway client world + paged RPCs; kick old connection on reconnect; blocking connect load first, HTTP fetch off-thread before M9;
UI keeps `X.Local` on managed view-models; no player avoidance; no owner prediction of vault/wall-run; hit moments from data ticks; own `PositionHistory` (no Unity Physics);
Creds over RPC pages; monster nav = static `NavMesh.CalculatePath` queue + grid for height/LOS; pooled GameObject monster views; stand-in wolf clips; managed damage bridge;
assist/flee off; Entities Graphics for drops deferred; built client as 2nd client; WSL2 Linux parity first; thresholds 0.05 m / 15 ms; bot sign-in bypass on test server only;
owner feel checkpoints after M3a and M4; rollback rule (fail = keep NGO on main).
**Editor/RAM gate (Master):** start the clone Editor only when free RAM >= 12 GB (check `Get-CimInstance Win32_OperatingSystem` before and during), as a batch run
(`-batchmode -nographics`), one Editor at a time, kill if free RAM < 3 GB, never touch the main project's Editor. If RAM is short, tell Master (do not wait silently).
**Seams PR rules:** branch from `main` (branches `seams/<topic>` created in the clone checkout, then merged into `dots/netcode-entities`), NGO only, no behaviour change, one concern per PR,
no push without telling Master the LFS size (none expected).

## 14. Scripting backend (owner, 29 Oct 2026): IL2CPP everywhere (Q4)
The Windows client and the Linux dedicated server both ship as **IL2CPP**; Mono is only for fast Editor iteration.
How the real project sets it today (read 29 Oct): `ProjectSettings` shows Standalone = Mono; `TestBuildBuilder.Build()` builds Mono by default and its menu
`MenuIl2Cpp` / `Build(ScriptingImplementation.IL2CPP)` switches the backend + `Il2CppCompilerConfiguration.Release` for the build and restores the
settings afterwards (needs the Visual Studio C++ workload: `Il2CppMissing()` says what is absent); `ServerBuildBuilder` builds Mono only.
Plan consequences: (1) `ServerBuildBuilder` gets an IL2CPP switch and builds the Linux server IL2CPP by default (Server named build target, Release C++ config);
(2) client release builds go through the IL2CPP path; (3) parity tests (Windows vs Linux prediction) run IL2CPP+Burst on both; (4) Linux IL2CPP cross-build from Windows
needs the Linux toolchain package (verified in the M1 spike, result below).

### 14.1 M1 Linux spike result (29 Oct 2026, throwaway project `C:\Users\dlaco\KN-worktrees\dots-spike`, Unity 6000.6.3f1)
- Linux x64 **IL2CPP + Burst** dedicated-server build of an empty Netcode-for-Entities server (custom `ClientServerBootstrap`, server world only, one Burst `ISystem`):
  builds from Windows in 287 s cold / 87 s incremental; Unity peak 1.2 GB; toolchain `com.unity.toolchain.win-x86_64-linux 1.1.0` + sysroot resolved automatically;
  `lib_burst_generated.so` present. Entities/Netcode source generators and IL post-processors run in `-batchmode -nographics`. Packages are built into the Editor (6.6.0; burst 2.0.0; mathematics 1.4.0).
- Runs in WSL Ubuntu-24.04: listens UDP 7797, about 200 MB RSS, Burst system alive.
- **SIGTERM finding:** the stock Unity Linux player exits in 0.2 s on SIGTERM **without calling `Application.wantsToQuit`** (no save). Proven fix: libc `signal(15)` handler
  (`[MonoPInvokeCallback]`, sets a flag only) + a main-thread `SystemBase` that runs the save-all and calls `Application.Quit(0)`: stopped 1.23 s after SIGTERM (1 s simulated save).
  Rule for the real server: never rely on `wantsToQuit` for saving; use this pattern, with a status-page admin stop endpoint as the systemd `ExecStop` fallback. Exit code to be confirmed with the real server.
- Not yet proven: Mono run to completion, subscene baking in a build, Entities Graphics exclusion, Windows-vs-Linux state hashes, Addressable-map subscene (S11).

## 15. M0 progress
- [x] Seam 1: `PlayerKey` + `PlayerKeys` (Core), `NetPlayerKeys` (Networking adapter), `IPlayerBody.Owner`, `ServerAccounts` keyed by `PlayerKey` (branch `seams/player-key`, commit 773438fd6, 25 files; `dependency_map --check` 0 uphill/0 two-way;
  `compile_check` 59/59 and `--server` 27/27; EditMode tests written, NOT yet run (needs an Editor)). Merged into `dots/netcode-entities`; to be offered to main as its own PR (with the LFS size note: none).
- [ ] Seam 1b: re-key `RpcBudget`, `NetworkHealth`, `ServerRecords` client-id dictionaries, `ServerControlPage` player list/kick.
- [ ] Seams: `NetRole`, `NetClock`, `NetSession`, `IServerLeaving`, `IWorldTargets`, `X.cs`/`X.Net.cs` splits + stub compile gate, fixed/paged RPC args, `MapAnchor` in builders, placement + server-walk entry points, Core/UI asmdefs without `Unity.Netcode.Runtime`.

## 16. Definition of done per system (owner rule, 29 Oct 2026)
Test each system as soon as it is completed; never batch up work untested. A system is done only when (1) its tests pass (EditMode / netcode test world, plus a 2-client check where it touches netcode),
(2) the result is recorded on this checklist with evidence (test names, screenshots) BEFORE the next system or milestone starts, and (3) a one-line completion report went to Master. A failing system stops the line;
the rollback rule stays: NGO stays on main, nothing merges. Seams stay local branches (no push, no PR, no merge) until the owner reviews a per-seam review card (branch, commit, files/lines, systems touched,
dependency_map + compile_check, tests run, risk, rollback).

## 17. Coverage and tests (details in the designs folder)
- `netcode-for-entities-designs/coverage-table.md`: 98 rows (35 runtime asmdefs, 24 test asmdefs, 41 asmrefs; all 63 NetworkBehaviours; all 67 build-list rows) with fate, milestone, owner and proving test.
  The table adds milestone labels M4b (world singletons), M5b (progression/crafting), M5c (social/companions) and a crosswalk to the numbering above.
- Missed by the first scope: `PlayerRename`, `PlayerGmSight`, `PlayerWaterstrideMotor` (NetworkBehaviours total 63, not 59); also `PlayerHotbarMemory`, `ServerReach`/`ServerList`, `ServerApp` flags,
  surface-graph and water-route pathing. Cutover items added: the `JoinRules` build check, launcher/Release/publish path, NGO and N4E builds cannot coexist, Addressable map + Entities subscene build path (spike S11).
- `netcode-for-entities-designs/editor-test-matrix.md`: layers A (rules), B (ECS systems), C (server+client worlds in one process, loopback) run as EditMode tests in the clone's Editor/batch
  (`run_editmode_tests.py`); D (built test server + built second client + one Editor client in Play) from M2; E bots, F latency, Burst/IL2CPP/Linux parity and performance need builds.
  Before the first Editor open with ECS: add packages entities/netcode (6.6.0; burst 2.0.0, collections 6.6.0, mathematics 1.4.0, transport 6.6.0 come built-in), no Unity Physics, Entities Graphics deferred; domain reload is off so statics reset.
- Owner answers (29 Oct): `ZoneSessionManager` kept until M2 decides, deleted at cutover if unused; hosting = offline local server + client only, no player-hosted public games (Relay/mediated sessions keep working);
  Entities Graphics deferred, GameObject views first.
- Early seam added: **server `POST /stop`** on the status port (port+1): loopback 127.0.0.1 only, header `X-KN-Key` = save service key, same save-all as the control page Stop button, answer 200/202 then `Application.Quit(0)`;
  systemd `ExecStop` (kn-stop.sh, waits up to 85 s, TimeoutStopSec=90) falls back to SIGTERM; the spike's libc SIGTERM handler is the second line. One concern, small PR, NGO-side, needed by today's Linux server too.

## 18. Evidence log
- 29 Oct, seams 1 + 1b merged on `dots/netcode-entities`: full batch EditMode run in the clone (first project import, Unity 6000.6.3f1): **1124 passed, 5 failed, 10 skipped of 1139**.
  All 19 tests of the seams passed: PlayerKeysTests (6), PerPlayerKeyTests (6), NetPlayerKeysTests (2), GmRosterTests (5).
  The 5 failures are content tests unrelated to netcode (`CharacterDataTests.OnlySkeletonArmorCarriesAHelm`, `EyeOptionTests.GlowingEyesLightOnlyTheIrises`, `IconAssetsTests.EveryRingAndAmuletOfTheLadder_WearsItsLooksLibraryIcon`,
  `IconAssetsTests.NoItem_IsBlank` (Armor_HOLW_01 no icon), `GearLadderTests.EverySetAboveCommonWearsItsOwnTheme` (Armor_SKTN_R45)). The branch differs from `main` only in netcode `.cs` and `.meta` files (+ docs), so these come from `main`'s
  in-progress gear content; not yet confirmed by running main (no second Editor import).
- First import needed >12 GB: the first run was killed by the 3 GB watchdog at 520 s (Unity 8 GB peak + workers); the second finished with min free 4.3 GB after Blender was closed.

## 19. M1 spikes result (29 Oct 2026, EditMode in the throwaway project; full text `netcode-for-entities-designs/m1-spikes-result.md`)
**32 of 32 spike tests passed** (21 s, one Unity, min free RAM 14.9 GB).
- **H0:** Unity's `NetCodeTestWorld`, `NetcodeWorld`, `SubSceneHelper` are all internal. We built our own `KnTestWorld` (public API only: server + N clients in one process over IPC loopback, hand-ticked, optional latency simulation and connection approval;
  copy at `netcode-for-entities-designs/spike-code/KnTestWorld.cs.txt` -> becomes the game's test helper in M2). Ghost prefabs can only be made through a SubScene (`BakingUtility` internal); authoring MonoBehaviours must be in a non-Editor asmdef.
- **S1 pass:** ghost components and buffers replicate intact incl. `FixedString64/512/4096` and FixedList fields; `SendToOwner` buffers reach only the owner. A FixedList above 64 needs `[GhostFixedListCapacity(Capacity=64)]` (else compile error); 100 elements deliver 64 and log an error.
- **S3 pass:** a client-only planned buffer survives rollback; integer progress and derived position float bits match server vs client with 0 mismatches over about 330 ticks; server correction converges. At 100 ms latency the client re-simulated up to 13 times per tick.
  **Open:** same process/CPU only — Windows client vs Linux server float equality still needs the Linux build run (M3 gate).
- **S9:** one RPC delivers up to about 1350 chars (1400 fails: "UTP only offered a packet buffer of 1378B"; `maxMessageSize` capped at 1472 -> 1450 B buffer). Oversize RPCs are LOST silently (no retry, no fragmentation): all large payloads (approval token, roster, look) MUST be chunked; the 3-chunk `IApprovalRpcCommand` flow completes. Add a size assert/lint on every `IRpcCommand`.
- **S11 (Editor):** a subscene with 3 prespawned ghosts loads on both worlds with identical ghost ids/positions; ghosts appear one tick after `IsSceneLoaded`. **Open:** Entities docs say Addressables "either don't work with Entities or are suboptimal" and builds only detect `SubScene` MonoBehaviours / `EntitySceneReference`. Needs one player build + Addressables to confirm; fallback = map scenes stay Addressable for GameObjects while the ghost subscene is a regular build-time subscene that spawns by `MapAnchor` id (as the plan already says).
- **Compile gate:** source generators live in `Library/PackageCache/com.unity.entities@<hash>/Unity.Entities/SourceGenerators/` and `com.unity.netcode@<hash>/Runtime/SourceGenerators/` (+ 42 `*.NetCodeSourceGenerator.additionalfile` templates). With `<Analyzer Include>` lines `dotnet build` fails a broken `IJobEntity` (SGJE0008); WITHOUT them the same code compiles clean (false green). `Library/Bee/artifacts/<dag>/<Asm>.rsp` holds Unity's own list; running Unity's `csc.dll` on it works in 2 s. => `compile_check.py` must be extended (M2) or replaced by a Unity batch compile for Entities assemblies; until then ECS assemblies are NOT covered by it.

## 20. M0 status (29 Oct 2026, all LOCAL branches, merged into `dots/netcode-entities`; none pushed)
Seams: `player-key` (773438fd6), `player-key-2` (9d5c85dfa), `server-stop-endpoint` (f7911ed7c), `net-clock` (e264c7944), `net-role` slice 1 (03e1d55ec, 4a8fca55f), `net-session` (76337636f, 2e2b5d9c4).
Merged tree: dependency_map 206 links 0 uphill/0 two-way; compile 59/59; --server 27/27; tests for seams 1 and 1b run and passed (19), others written, not run.
Decision (proposed to Master): X.cs/X.Net.cs splits happen per system at port time (M3-M8), not as a pre-split. Remaining M0 seams: `MapAnchor` in editor builders, fixed/paged RPC args, `IServerLeaving` wiring (Stage 2).

## 21. Decision: split per system at port time (approved by Master, 29 Oct 2026)
The `X.cs` / `X.Net.cs` split of each NetworkBehaviour happens at the moment that system is ported, inside the DOTS branch, together with its test (definition-of-done rule, section 16) — not as a pre-split on main.
Reason: refactoring ~59 classes on main first means refactoring each one twice (NGO edge now, N4E edge later), multiplies merge conflicts with sessions that keep editing those files, and tests nothing new.
Remaining small M0 seams: `MapAnchor` in editor builders, fixed/paged RPC arguments, `IServerLeaving` wiring into the disconnect path (Stage 2 `ConnectionClosedSystem`).

## 22. Main drift log (keep current: files `main` changed since our base that our branch also touches)
Base: `3ad5ba303` (main on 29 Oct 2026). Our branch touches 100 files so far (docs, Core/Networking/Persistence/Player/GameFeel/UI/World netcode edges, tools).
- 29 Oct 2026: `main` is still at `3ad5ba303`: 0 new commits, 0 overlapping files.
Check before every merge and weekly: `git diff --name-only 3ad5ba303 main` intersected with `git diff --name-only 3ad5ba303 dots/netcode-entities`; also list new `[Rpc]` / `NetworkVariable` lines added on main (those are new edges the DOTS branch must port).

## 23. M2a notes (29 Oct 2026, written without an Editor; nothing compiled or run yet)
What was written (all under `Assets/_Game/Networking/Entities/`): bootstrap, settings reader, 30/30 defaults, approval (setup, client send, server assembler with budget, join check interface, refusal RPC and delayed disconnect), PlayerKey minting and close hook, the test world and 3 test files (see the M2a lines in section 4).
Design choices worth knowing:
- Approval is on in every client/server world of this assembly (`KnApprovalSetupSystem`), so every join, also in tests, sends `KnApprovalRequest` chunks. A client with no `KnJoinPayload` sends nothing and times out.
- `JoinRefusedRpc` is an `IApprovalRpcCommand` on purpose: the package refuses to send any other RPC to a connection that is still in Handshake/Approval (RpcCommandRequest.cs), and the receive side lets approval RPCs through even when the transport already says Disconnected.
- `ConnectionPlayerKey` is a cleanup component, so the close of a connection is seen exactly once even though netcode destroys the entity in the same step. `PlayerKeys.Mint` is fed a process-wide serial, not the NetworkId (netcode reuses ids: `freeNetworkIds` queue in NetworkStreamReceiveSystem).
- The pre-approval budget runs from the first chunk, not from the connect; a client that never sends is ended by netcode's own handshake/approval timeout (15 s here).
- Chunks are ASCII only, 480 bytes each (a FixedString512Bytes holds 509; the brief's "payload up to 1000 bytes" does not fit that type, so a payload is several chunks). Defaults: 8 chunks, 4000 bytes, 10 s.
- `IKnJoinCheck` is polled each tick until it answers (no await on the simulation); the default accepts everyone and is for tests only.
API names that differed from the briefing or the docs: none found missing; `HandshakeApprovalTimeoutMS` is a field of the `ClientServerTickRate` singleton (the server value is pushed to clients in the handshake), `RequireConnectionApproval` is on `NetworkStreamDriver` and must be set before listen/connect (`NetworkStreamDriver` exists from world creation, created in `NetworkStreamReceiveSystem.OnCreate`).
Open doubts to check at the first compile/run: see the list in the M2a report (FixedString ctor from string, `RemoveComponent` on a query of cleanup-only entities, source-generator handling of `partial class : SystemBase` without `SystemAPI`, `World.Dispose` clearing the bootstrap lists, approval flag on thin clients).
What remains for M2b: roster path (throwaway client world + paged RPCs), Relay path, encryption driver (DTLS, `certificateFile`/`privateKeyFile`), travel ticket carried in the join payload and checked by a real `IKnJoinCheck` (Unity token, bans, `ServerFull`, build/hash check), `ServerControlPage` reading N4E connections (managed snapshot), SIGTERM handler + `POST /stop` on port+1 (hooks marked TODO in `KnBootstrap`), a Core-side stop/save-all interface, Linux server flags beyond `-port/-pageport/-role/-map` (`-batchmode -nographics`, `saveService`, `statusPort`), `TargetFrameRateMode`/30 fps cap vs `DedicatedServerOptimizer`, thin-client approval for bots, `compile_check.py` support for the Entities source generators (until then this assembly is outside the compile gate), Encyclopedia rows.

## 24. Evidence: first ECS batch in the clone (29 Oct 2026, Unity 6000.6.3f1, merged seams + M2a)
- Finding: **NGO and Netcode for Entities cannot share a project** (Editor assembly names `Unity.Netcode.Editor` vs `Unity.NetCode.Editor`, and `Unity.Netcode.Editor.Tests`, clash case-insensitively; Unity stops compiling). Fix on this branch: an embedded, trimmed copy of NGO 2.13.3 in `Packages/com.unity.netcode.gameobjects` with its Editor asmdef renamed (`Unity.Netcode.GameObjects.Editor`), tests/samples/docs removed (note file inside). Both packages compile side by side. Removed together at cutover.
- Run 1 compile error: test asmdef lacked `Unity.Entities.Hybrid` (UpdateWorldTimeSystem); fixed (+ Transforms, NetCode.Hybrid, NetCode.Authoring.Hybrid).
- Run 3 import was killed by the 3 GB watchdog (Unity + workers peaked 12 GB importing the new package set); run 4 finished (139 s, peak Unity 6.7 GB, min free 7.6 GB).
- Result: **1170 passed, 5 failed (the same 5 gear/icon content failures as on the pre-seam run), 10 skipped, of 1185.** New tests all passed: KnConnectionTests 8 (connect gets NetworkId, 3-chunk approval, async check, too many chunks / bytes / timeout refused, refusal reason, PlayerKey once and never reused), KnPureTests 6, RpcSizeLintTests 3,
  NetClockTests 6, NetcodeNetClockTests 2, NetRoleTests 5, NetSessionTests 9, NgoNetSessionTests 3, StopEndpointRulesTests 9, PlayerKeysTests 8, PerPlayerKeyTests 6, NetPlayerKeysTests 2.
- API doubts from the M2a author that the first compile and run settled: approval flow, refusal before disconnect, PlayerKey mint/close hook, server clock advance in KnTestWorld (all green).
- M2a (packages, assembly, KnBootstrap, chunked approval, refusal, ConnectionKeySystem, KnTestWorld in the game, EditMode tests): **DONE (EditMode layer C)**. Not yet covered: a real socket 2-client test on a built server (M2b/D).

## 25. M2b-1 notes (29 Oct 2026, written without an Editor; nothing compiled or run yet)
Dependency choices for `Networking/Entities` (`KryticalNova.Networking.Entities`): references Core (`KryticalNova.Runtime`) and the Unity packages only; it does **not** reference `KryticalNova.Networking` (the NGO assembly): that one holds NGO types and ServerHooks, and the NGO package is only embedded for the coexistence period (section 24). Instead the plain C# rules both servers share moved down to Core (`Core/Runtime/Net/Join/`, namespace unchanged so no `using` changed anywhere), and the two game-owned things a join needs are Core slots filled by their owners: `BanLookup` (Networking fills from `ServerBans`) and `ServerShutdown` (Persistence fills from `ServerSaveHooks`). Same pattern as `ServerLeaving` / `ClientPresentationRegistry`; no bus, no container.
Design choices worth knowing:
- Netcode has no public "stop listening" (`StopListening` is internal and obsolete). "Close the door" is therefore the `KnServerStopping` singleton: every join not yet decided is refused as `ServerStopping`, and every open connection is disconnected after the save. The listen socket itself closes when the process exits.
- `ConnectionApproved` is added directly on the `EntityManager` (not through the buffer) so the next join check in the same tick already counts the player (server full).
- The server's page and the UDP answer share one number (the page port). The NGO server used game port + 1 for both; the contract passes `-pageport`. `POST /stop` needs the save service key, so without `saveService` it answers 403 forever.
- Stop on a server with no `ServerShutdown.Handler` logs a warning and still quits (a hung server is worse); per-player saves still happen through the connection-closed hooks once gameplay handlers plug in there (M3).
- A character question (roster) in the join text is refused with `Refused` until the roster path lands.
- `compile_check.py` does not cover this assembly (section 19: source generators). The sources use `partial class : SystemBase` without `SystemAPI`, like M2a.
API doubts to check at the first compile/run:
1. `Entity { Index, Version }` object initialiser in tests (public fields in Entities 6.6, checked in source).
2. `new FixedString64Bytes(string)` / `FixedString512Bytes(string)` when the text is long: avoided by `KnFixed.CutUtf8` (500 bytes max); confirm no exception at the 509-byte edge.
3. Thin clients: `ClientServerBootstrap.CreateThinClientWorld()` inside `KnTestWorld` (player loop removal, `ThinClientSimulation` systems incl. `KnApprovalSetupSystem`). If the thin client never gets a NetworkId, look at `NetworkStreamDriver` creation for `WorldFlags.GameThinClient`.
4. `EntityManager.AddComponent<T>` inside `KnApprovalAssembler.OnUpdate` while an `EntityCommandBuffer` is open (no query array is alive then, expected fine).
5. `NetworkTime.ServerTick.IsValid` on the server before the first connection (the status tick stays 0 until valid).
6. `KnStatusSystem` takes its first snapshot at world time 0; `StatusSystem_CopiesPlayersAndTickIntoTheSnapshot` assumes the second one (+1 s) sees both players (40 ticks after `Connect`).
7. Linux: `Marshal.GetFunctionPointerForDelegate` + `[AOT.MonoPInvokeCallback]` under IL2CPP (same shape as the spike, which ran); Windows: `SetConsoleCtrlHandler` with a delegate parameter (same as `ServerLifetime`, Mono) now also marked for IL2CPP.
8. `Application.cloudProjectId` in a dedicated-server build (the NGO server uses it too; `cloudProjectId` in the settings overrides).
9. The lint `TheBotCode_IsCompiledOutOfReleaseBuilds` reads the .cs files under `Application.dataPath`; it fails if a future bot line is added outside `#if KN_DEV_BOTS`.
What remains for M2b-2: roster path (throwaway client world, paged RPCs, then the join check's character question), Relay, encryption/DTLS (`certificateFile`/`privateKeyFile` are read and only logged as an error today: the server runs unencrypted), `ServerControlPage` on N4E connections (managed snapshot of players/bans/GMs; `KnStatusSnapshot` is the seed), the world service ticket (replace `AcceptAllTickets`), `gm.shutdown`/countdown announcements on the stop path (players only get a disconnect), backups at start/stop for the main role (`ServerLifetime` does them for NGO), single-copy guard (`ServerSingleCopy`), `compile_check.py` source-generator support, Encyclopedia rows (Networking status, glossary: Join Check, Travel Ticket, Stop Gate, Dev Bot) and the build-list change log line.

## 26. Cutover item and merge-back risk: the embedded NGO copy (NGO vs NetCode Editor asmdef clash)
Why it exists: Netcode for GameObjects (com.unity.netcode.gameobjects 2.13.3) and Netcode for Entities (com.unity.netcode 6.6.0) cannot sit in one project: `Unity.Netcode.Editor` vs `Unity.NetCode.Editor` and `Unity.Netcode.Editor.Tests` clash case-insensitively and Unity stops compiling. While unported systems still need NGO, the DOTS branch carries `Packages/com.unity.netcode.gameobjects/` (embedded, 4 MB: Runtime, Editor with CodeGen, CHANGELOG, LICENSE; Tests, Samples~, Documentation~ removed; Editor asmdef renamed `Unity.Netcode.GameObjects.Editor`; `Runtime/AssemblyInfo.cs` InternalsVisibleTo extra line; `DOTS-MIGRATION-NOTE.md`).
Must be deleted at cutover (exact list):
1. The whole folder `Packages/com.unity.netcode.gameobjects/` (and its .meta files).
2. The `"com.unity.netcode.gameobjects"` line in `Packages/manifest.json` and its entry (and NGO-only dependents) in `Packages/packages-lock.json`.
3. Every asmdef/asmref reference to `Unity.Netcode.Runtime`, `Unity.Netcode.Components` in first-party assemblies (they should be gone by then: the NetRole/NetSession seams and per-system ports remove them).
4. NGO-only assets: `NetworkManager.prefab`, `DefaultNetworkPrefabs.asset`, NGO scene copies of the NetworkManager (44 scenes), `GameplayPrefabsSetup.SpawnedPrefabs` rule.
Risks on the main-merge list: (a) the main project must never receive the embedded copy or the N4E packages before the whole cutover merges (the two cannot coexist there without it); (b) a registry NGO update on main (package version bump) while the branch carries an embedded older copy: the embedded folder wins on the branch, the merge must take main's manifest line out; (c) after deletion, Library must be rebuilt for the clone-to-main hand-over (first import ~12 GB peak); (d) check that no code uses the renamed assembly name `Unity.Netcode.GameObjects.Editor`.
Checklist at cutover: [ ] folder deleted [ ] manifest + lock clean [ ] `grep -r "Unity.Netcode" Assets Packages` returns nothing [ ] project compiles with only N4E [ ] batch EditMode suite green.

## 27. Evidence: batch run after M2b-1 (29 Oct 2026, 11:06-11:10, 223 s, Unity peak 6.7 GB, min free 3.3 GB)
**1213 passed, 5 failed (same gear/icon content failures), 10 skipped of 1228** — compiled first time. New: KnJoinCheckTests 24, KnStopTests 12 (stop machine order, throwing save, quit once, idempotent, loopback-only page over a real socket, stop with 2 players saves then disconnects, join refused during stop, status snapshot), KnServerPureTests 7;
KnConnectionTests 8 and KnPureTests 6 still pass. M2b-1 (real join check, safe stop SIGTERM/console/`POST /stop`, dedicated server entry + status page, thin-client dev bots) is DONE at the EditMode layer; real-socket/IL2CPP/Linux behaviour of the stop path still needs the built-server test.
Note: min free RAM touched 3.3 GB (watchdog floor 3.0) — the first import after new package/code sets is the heavy moment; later runs peak lower.

## 28. M2b-2 notes (29 Oct 2026, written without an Editor; nothing compiled or run yet)
**Decisions**
- **Roster = Q7 default.** A short-lived client world per question (`KnRosterClient.Ask`), the question rides in the join text (sixth part, `RosterWire.Pack`), the answer comes back as `KnRosterReply` approval RPCs of <= 480 characters, up to 16 pages (7680 characters; the NGO limit was 2600), assembled in any order. A bigger answer is refused in words ("That answer is too long to send.") and logged without its content. The asking connection never gets `ConnectionApproved` or a NetworkId, so it takes no place and mints no PlayerKey. The server closes it 150 ticks after the last page as a safety net; the client hangs up as soon as the answer is whole. Errors travel as `JoinRefusedRpc` (the same path as a refused join), so one client system serves both.
- **The roster is asked synchronously on the main thread** (like the NGO approval callback did). `ServerRoster.Answer` may call the save service over HTTP; if that ever shows in a tick time, make the roster provider async (the assembler has the Pending shape ready). Flag for M3.
- **Seams instead of references.** `Networking/Entities` still does not reference the NGO assembly: `IServerRoster`/`RosterWire`/`ServerCertificates` moved to Core (namespace `KryticalNova.Networking` kept, like the other Join files).
- **Transport per world.** `KnDriverConstructor` is installed once by `KnBootstrap.Initialize` (`NetworkStreamReceiveSystem.DriverConstructor` is a process-wide static) and reads a per-world `KnWorldTransport` (a `ConditionalWeakTable`), so a roster world and a play world can differ. Netcode builds the driver at the world's first update; assign before that.
  - Client: `Plain` = Netcode's default choice (IPC to a server in the same process, else UDP), `Secure` = UDP with `WithSecureClientParameters(AuthorityCertificate, ServerName)` (the game accepts only a server our authority signed, as on NGO), `Relay` = `RegisterClientDriver(.., ref RelayServerData)`.
  - Server: `Secure` registers a DTLS UDP driver (`WithSecureServerParameters(cert, key)`) and, in the Editor only, a plain IPC driver for the in-process client; `Relay` uses `RegisterServerDriver(.., ref RelayServerData)`. The Relay allocation itself (Unity Services call) is NOT made in this assembly: the host layer sets `KnTransport.PendingServerRelay` before the server world's first update; Relay mode without it does not start.
  - Certificate rules (`KnTransportPlanner.PlanServer`): files found and both named or both defaulted -> Secure. Missing: release server (`UNITY_SERVER`, not a development build) **refuses to start** (exit 1, message names the folder, never the key); development build or Editor **warns and runs plain**. `plainTransport` (`-plaintransport`) is the explicit opt-out for a release server. Only one of `certificateFile`/`privateKeyFile` named is an error, never half encryption. Unreadable / non-PEM files take the same two branches. Logs hold paths and reasons only; `KnWorldTransport.ToString()` hides the secrets.
- **Server page.** `/players` and `/kick` are owner-only (loopback + `X-KN-Key`, same as `/stop`); `/status` and `/health` stay open. The kick names a PlayerKey (as text, it is a 64-bit number) and the HTTP thread checks it against the last snapshot (404 if the player is not there); the main thread closes the connection through the normal path so `ConnectionClosedSystem` saves and cleans up. Names come from the `KnServerStatus.NameOf` hook (empty until the Player system fills it at M3). Not ported yet because the data does not exist on Entities yet: login/cookie sessions and the GM password list, bans table + ban/unban, announce, log tail, flags, backups, mode switch, server-list edit. The Server app (`ServerApp~`) still drives the NGO page; moving it is an M10 item.
- **Travel ticket.** The brief named `/ticket/use`; the world service built in online phase 5 exposes `POST /world/redeem {ticket, server}` -> `{granted, refused, account, character, from, map, arrival}` (use once, 30 s), which is what is implemented. Rules: no ticket + role main = accepted; no ticket + role map = refused (`BadTicket`); a ticket is redeemed (polled, never awaited), must belong to the proven account, and the service not answering refuses the join. The arrival (character, place) is parked in `KnArrivals` for the player-body spawn (M3). The server id is `MachineName:port`, as `WorldLink.ServerId`.

**API differences and first-compile doubts**
1. `WithSecureClientParameters(string, string)` / `WithSecureServerParameters(string, string)` string overloads of `SecureParameterExtensions` (Transport 2.x): they take PEM text; check the server does not need `FixedString4096Bytes` for a certificate chain longer than 4 KB (our chain is one certificate).
2. `DefaultDriverBuilder.RegisterServerUdpDriver/RegisterServerIpcDriver/RegisterClientDriver(.., NetworkSettings)` are public in 6.6 (checked in source). `ENABLE_MANAGED_UNITYTLS` only guards Netcode's own convenience wrappers, not the transport's TLS layer; we call the settings overloads, so no define is needed. Whether UnityTLS actually works in the Editor on Windows is untested.
3. `world.IsHost()` (extension in `Unity.NetCode`) used in the Secure server branch: public.
4. `NetworkSnapshotAck.EstimatedRTT` (public float, milliseconds) read for the ping; it may be 0 until the first snapshots, then the row says -1.
5. `RelayServerData?` (nullable of an unsafe struct) as a static: should compile with `allowUnsafeCode`.
6. `ClientServerBootstrap.CreateClientWorld("KnRoster")` appends the world to the player loop by itself; `KnRosterQuery` disposes it from a MonoBehaviour `Update` (outside any world update). If Netcode complains about disposing a world that is in `ClientWorlds`, add `ClientServerBootstrap.ClientWorlds.Remove(world)` before `Dispose`.
7. `KnRosterTests` hand-tick the roster world with `KnTestWorld`, so they exercise the real systems but not the player loop / `KnRosterPump`.
8. `JsonUtility` field named `from` (the world service answer) is a C# contextual keyword; used only as a member name.
9. `RosterWire` moved files: if something outside the Networking tree (`grep RosterWire`) used a private overload, the compiler will say.
10. Every `.meta` was written by script with a random GUID (`MonoImporter` default); Unity will not change them.

**Manual tests for a built player (cannot be proven in EditMode; part of M2c)**
1. *DTLS direct:* build the Windows dedicated server and a Windows player. Put `server.crt` + `server.key` (made by `Networking/Tools~/certificates.sh`) in `certificates/` beside the server folder; start the server: log says "Direct connections are encrypted (DTLS)". Join from the player (`StartClient(.., encrypted: true)`): NetworkId arrives. Join with `encrypted: false`: must NOT join (no NetworkId, handshake timeout). Join to a server with the wrong-name certificate: refused by the client.
2. *Missing certificate:* start the release server without the files: it exits non-zero and says why; with `-plaintransport` it starts and logs "NOT ENCRYPTED"; a development build starts and warns.
3. *Relay:* with Unity Services signed in, allocate a Relay place on the server side, set `KnTransport.PendingServerRelay` before the world's first update, `connectionMode: relay`; join code to a client, `StartClientRelay(JoinAllocation -> RelayServerData "dtls")`; two clients join, play 5 minutes, one leaves cleanly. Also run on Linux (UnityTLS there).
4. *Roster:* `KnRosterClient.Ask` against the built server for an account with 0, 1 and 5 characters (the real `ServerRoster` on a test database), then with the server full (answer still comes), then banned (ban text comes).
5. *Page:* `curl -H "X-KN-Key: <key>" http://127.0.0.1:<pagePort>/players` lists the two clients; `curl -X POST -H "X-KN-Key: <key>" "http://127.0.0.1:<pagePort>/kick?player=<key>"` closes one; a request from another machine gets 403.
6. *Ticket:* with the save service in test mode (port 7802): issue a ticket on server A, join server B (role map) with it: accepted once, the same ticket again refused, an expired one refused, no ticket refused.

**What remains**
- M2c: the built-server two-client test (Windows, then Linux) with the manual list above, 60/150/250 ms; the first batch compile and test run of this section; run `compile_check.py` (and `--server`) once with the new generator support and fix what it finds.
- Move the front-end screens (character select, server list) to `KnRosterClient` and `KnBootstrap.StartClient` (UI not edited here).
- Port the rest of the control page and the Server app to the Entities page (M10), after the systems they read exist on Entities.
- `ServerRoster.Answer` async if it ever blocks a tick.

## 29. Evidence: batch run after M2b-2 and the compile gate (29 Oct 2026, 11:32-11:34, 124 s, Unity peak 6.6 GB, min free 6.5 GB)
- First run after M2b-2: 1253 passed / 10 failed: 5 known content failures + 5 new: 4 `KnRosterTests` (NullReference: roster query disposed after its world was destroyed -> fixed: queries are disposed before `Finish` destroys the world) and `ServerHooksTests.EverySlotIsFilledWhenTheGameLoads` (my tests nulled the process-wide `RosterLookup`/`BanLookup` slots the game fills at load -> every test file now keeps and restores them).
- After the fix: **1258 passed, 5 failed (the known gear/icon content failures), 10 skipped of 1273.** M2b-2 DONE at the EditMode layer: roster path (paged, short-lived client world), transport rules (direct/DTLS/Relay choice, certificate rules, no secrets in logs), control page players/kick, travel-ticket check against `world/redeem`, Encyclopedia rows for Networking.
- **Compile gate (M1 finding closed):** `compile_check.py` now compiles the Entities assemblies with the source generators: `61 of 61 assemblies compiled`; `--server`: `28 of 28`. Proven with a deliberate probe (non-partial `IJobEntity`): the check FAILED with `EA0007 Missing partial on IJobEntity` and passed again after the probe was removed. ECS code is now covered by the cheap gate; Burst AOT and baking errors still need a Unity build.
- `dependency_map.py --check`: 0 uphill, 0 two-way.
- Remaining before M2 closes: M2c, the built-server 2-client test (direct UDP; manual list for DTLS / Relay / missing certificate in section 28), plus the first roster/join front-end wiring (UI untouched so far).

## 30. M2c material (29 Oct 2026, written without an Editor; nothing built or run yet)
What was written (all under `Assets/_Game/Networking/Entities/Probe/`, own assemblies `KryticalNova.Networking.Entities.Probe`, `.Probe.Editor`, `.Probe.Tests.Editor`; the code is behind `#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD`, so a release build carries an empty assembly):
- **Probe players, no scene of their own.** `KnProbeEntry` (`-knprobe server|client`) starts the probe from the command line, so any empty scene works; the build script generates one (`KnProbeEmpty`, deleted again after the build). Server: the Server-subtarget player already starts its world in `KnBootstrap`; the probe adds a fake save (`IServerShutdown`: logs `SAVE ALL (probe)` begin/end, sleeps 1 s), a fake roster (`RosterLookup`), a join check without the Unity sign-in (an unsigned game plays as the made-up account it sends; build match, capacity and bans stay real), a watch system that logs each PlayerKey, and a result file `kn-probe-server.json`. Client: `KnProbeClient` (`-knaction connect|roster|idle`, `-knquit-after`, `-kntimeout`, `-knname`, `-knout`) drives `KnRosterClient.Ask` and `KnBootstrap.StartClient(..., encrypted: false)` and writes `kn-probe-client-<name>.json`. A drop after connecting counts as a kick when the server page's `/health` still says 200, as a server stop when it says 503 or does not answer.
- **Exit codes:** 0 ok (quit-after, server closed, server stopped), 1 error, 2 refused, 3 timeout, 4 kicked.
- **Safety:** the probe refuses any port that is not 7797, 7798 or 8097 (client: exit 1; server: `Environment.Exit(1)` before the world listens). A probe server build started without `-knprobe server` exits at once (otherwise the bootstrap would listen on 7777). The driver checks that 7797/7798/8097 are free, never names another port, and its settings point the save service at `http://127.0.0.1:9` (nothing there); only the made-up key file matters (the page's `/stop` and `/players` need a key). No save service, database or secrets folder is read.
- **Build:** `KnProbeBuild` (menu *Krytical Nova > Online > DOTS Probe*, or batch `-executeMethod KryticalNova.Networking.Entities.Probe.KnProbeBuild.BuildServer` / `.BuildClient`): Windows x64 Mono, development build, define `KN_PROBE` (`BuildPlayerOptions.extraScriptingDefines`), one generated empty scene, output `Builds/KnProbeServer/KnProbeServer.exe` (Server subtarget) and `Builds/KnProbeClient/KnProbeClient.exe` (Builds/ is git-ignored). It restores the scripting backend (only touched if not Mono), the Addressables "build with player" option, the build target and the subtarget; stripping is left as the project has it (Standalone: Disabled) plus `Probe/link.xml` preserving the two assemblies. Baking: there is no Entities Graphics package and no SubScene in the probe scene, so nothing is baked; if Entities Graphics is ever added, list `Unity.Rendering.Hybrid` under Project Settings > Entities > Baking > Excluded Baking System Assemblies for these builds.
- **Driver:** `Probe/Scripts~/run_probe_2client.ps1`. The NGO builders (`ServerBuildBuilder`, `TestBuildBuilder`) are untouched.
- **Tests (EditMode, pure):** `KnProbeTests` (arguments incl. the port guard, exit-code table, result file, the scripted client machine: kicked / server closed / refused / timeout / roster / quit-after, drop classification).
- **Gate:** `compile_check.py` 64 of 64 (was 61; the 3 new assemblies); `--server` with `KN_PROBE` added by a temporary copy of the script: 29 of 29; `dependency_map.py --check` 0 uphill / 0 two-way.

How to run (owner; one Editor at a time, RAM-gated; no Unity running during the driver):
1. Close other heavy programs. Free RAM >= 11 GB (Unity batch build peak about 7-12 GB; the driver itself asks for 11 GB free and kills everything if free RAM falls below 3 GB).
2. Build (two Unity batch runs, about 3-8 min each; the first import of a cold Library is the heavy one): `& "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "C:\Users\dlaco\Krytical Nova II DOTS" -executeMethod KryticalNova.Networking.Entities.Probe.KnProbeBuild.BuildServer -logFile "C:\Users\dlaco\Krytical Nova II DOTS\Builds\probe-build-server.log"`, then the same with `BuildClient` and `probe-build-client.log`. Look for `[KnProbeBuild] Probe server OK` in the log; the Unity exit code is 0 on success.
3. Close Unity. Run `powershell -ExecutionPolicy Bypass -File "C:\Users\dlaco\Krytical Nova II DOTS\Assets\_Game\Networking\Entities\Probe\Scripts~\run_probe_2client.ps1"` (1-2 minutes). Output: `Builds\KnProbeRuns\<time>\probe-report.md` plus server.log, client-A.log, client-B.log and the three result files.

Expected RAM: server player 1-2 GB, each headless client player 1-2 GB, so about 4-6 GB while the script runs. Time: server page up in 10-30 s, whole script 60-120 s.

Pass criteria (each is a row of `probe-report.md`): RAM and ports preflight; server page answers `/status`; page is owner-only (wrong key -> 403); client A gets the roster answer (`roster-ok`) and a NetworkId; client B gets a NetworkId; two different PlayerKeys on `/players`; `POST /kick` -> 202; B exits 4 as `kicked`; A still connected; `POST /stop` -> 200; server exits 0; A exits 0 as `server-closed` (not kicked); server log has `SAVE ALL (probe)` in the order stopping, save, "Saved. Waiting for the players", and A's drop time is not before the save end; `kn-probe-server.json` says 2 joins, 1 roster answer, `stopped` / 0; no process left and ports 7797/7798 free afterwards.

Doubts for the first build and run (look here first when something fails):
1. `extraScriptingDefines` with the Server subtarget: if `KN_PROBE` does not arrive, the guard that stops an unflagged probe server is off, and the whole `Probe` assembly is empty unless the build is a development build (it is), so the probe still runs; the log line `[KnProbe] server ready` shows it started.
2. The NGO-era runtime hooks of the game still run in these players (`ServerSingleCopy`, `ServerLifetime` backing up an empty save folder under the run folder, `ServerControlPage` failing to bind the page port because `KnServerPage` has it, `DedicatedServerBoot` opening the map `KnProbeEmpty`, which is the active scene). The settings file carries `callLimits`, `ledgerDays` and `mapServers` only so the NGO option reader does not rewrite it (and drop `plainTransport`; `-plaintransport` is also on the command line).
3. Windows: a server started hidden by `Start-Process` and `ServerConsoleInput` reading `Console.In`; if the server exits at once, read `server.log`.
4. DTLS is not exercised (plain UDP on purpose: `-plaintransport` and `encrypted: false`); DTLS, Relay and the missing-certificate cases stay on the manual list in section 28.
5. The client reads its NetworkId and `KnJoinRefusal` straight from its client world; if `NetworkId` stays after the connection ends, B never exits 4 and the driver times out after 30 s (row "Client B exits 4").
6. `LegacyDataMove` (client player) may copy old-name data into the player's AppData folder once; already done on this PC.

What remains of M2: run it and fix what it finds; the DTLS / Relay / missing-certificate manual items (section 28); the same two clients against the Linux server (WSL, after the Windows run passes); 60/150/250 ms with the simulator once the Editor client exists; the front-end wiring.

## 32. M3a-1 notes (29 Oct 2026, written without an Editor; nothing compiled in Unity or run yet)
Files (all under `Assets/_Game/Gameplay/Player/Entities/`): `Runtime/` `PlayerComponents.cs`, `PlayerPath.cs` (exact integer polyline maths), `PlayerMovement.cs` (the whole walk as one pure `Step`, shared by the system and the pure tests), `PredictedMoveSystem.cs` (+ `PlayerTeleportSystem`), `PlayerInputGatherSystem.cs` (+ `IPlayerCommandSource`, `ScriptedCommandSource`), `PlayerServerPlanSystem.cs`, `PathPlanners.cs`, `PlayerGhostAuthoring.cs`, `PlayerEntitiesSetup.cs` (install, spawn, `IssueOrder`, `Teleport`); `Tests/Editor/` the tests, `PlayerRig`, `PlayerTestAssets` (generates `Tests/Editor/Generated/`, git-ignored, deleted at the end).
**Decisions**
- **Everything the loop decides is an integer.** Corners are `int3` millimetres (`PathCorner` is 12 bytes, so the buffer is read as `NativeArray<int3>` with `Reinterpret`), lengths use an exact integer square root, `ProgressMm` advances by `SpeedMmps x tickMicros / 1e6` with the sub-millimetre rest kept in `RemMicro`, so the sum is exact. The only floats are the `LocalTransform` outputs and the facing angle (`atan2`, a derived output). Burst `FloatMode.Deterministic`/`FloatPrecision.High` is set on the job and the system as asked, but the result does not depend on it.
- **The walk starts at the player's pose, not at the planner's first corner** (`MoveState.Start*`, 3 ints). A client whose click is stamped a few ticks later than it planned, or a server whose pose differs by a millimetre, still agree on the shape of the walk. `corners[0]` is ignored.
- **Full ticks only.** `PredictedMoveSystem` returns on a partial tick (`NetworkTime.IsPartialTick`); the presentation (M3a-2) interpolates between the last two full poses. This is the answer to advisor item 8 / spike S3's open question; it keeps rollback exact.
- **Corner source rule** (the key rollback argument): the walk uses `PathCorner` when `PathPlan.Seq` equals the walk's order number, else the owner's `ClientPlanCorner`. The click START comes from the input buffer (re-read on every re-simulation); the plan is client-only data that a snapshot rollback never touches (spike S3).
- **A refusal must be state.** The design said "the next snapshot shows `OrderSeq != n`", but a re-simulation re-reads the click from the input buffer and would start it again. So the server writes `MoveState.RefusedSeq` (replicated) and the start step skips that number. Server orders have the same problem the other way (a later click must not restart an old `ServerOrder`): `MoveState.ServerSeq` remembers the last one taken up.
- **`PlayerPose` replicates the integer truth** next to `LocalTransform` (which is also replicated by its default variant and drives interpolated observers). That is about 16 bytes of double sending; if the snapshot budget needs it, strip LocalTransform serialisation on this prefab with a ghost variant (M3a-2 / M9). Observers use the interpolated `LocalTransform`; the presentation reads `PlayerPose` + `TeleportSeq` to snap.
- **No silent fallbacks.** A world without an installed `IPathPlanner` logs one error and drops clicks/orders. `PlayerEntitiesSetup.Install(world, planner, source)` is the wiring point; tests install `StraightLinePlanner`; the real game installs `NavMeshPathPlanner` (written, uses only the static NavMesh API) in M3a-2.
- Managed pieces hold their objects as instance properties of the systems (no statics, so domain reload being off is not an issue; nothing needed a `SubsystemRegistration` reset).
- Run is granted to every click for now (`PlayerInput.Run`); paying the Chi stays server-side and arrives with the Vitals/wallet work. A `ServerOrder` runs when the server sets `OrderFlags.RunPaid`.
- Paths longer than 32 corners are cut and flagged `OrderFlags.Truncated`; the continue-and-replan loop of the design is not built yet.
- Speed, acceleration, arrival easing and the stopping distance are `PlayerMoveSettings` (defaults = today's `MovementSettings`: accel 25 m/s^2, 8 x speed, 300 ms slowdown, 1.5-8 m radius, 55 % at the end; stop distance 0 so tests arrive exactly; the later baking sets 0.2 m).
**APIs and first-compile doubts (check at the first Unity run)**
1. `IJobEntity` with `in DynamicBuffer<T>` (docs: `in` = read-only) and `EnabledRefRW<TeleportRequest>`; nested private `partial struct` jobs inside a partial `ISystem`.
2. `DynamicBuffer<PathCorner>.Reinterpret<int3>()` needs equal sizes (12 bytes each; both buffers are three ints).
3. `[GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]` on a plain `IComponentData` (`PathPlan`) as well as on the buffer.
4. `NetworkTick` as a `GhostField` inside `ServerOrder` (listed in the generator's default types).
5. `InputEvent` in a hand-written `IInputComponentData`; the gather system resets the struct to `default` every frame and the command system adds the event counts.
6. `GhostOwnerIsLocal` in a `WithAll` of a managed system's query (enableable; matches when enabled).
7. In the test world the owner client needs about 60 ticks after the spawn for its clock to settle before inputs arrive on time; `PlayerRig.Create` waits that long. If the first click of a test is late, the trace compare shows the first mismatch tick.
8. Tests that need 0 mismatches compare the last simulated value of each tick; the newest ticks (about RTT) are predictions, so the compare windows stop one tick before the server's.
9. `PlayerTestAssets` needs `GhostModeMask.All` and the internal `prefabId` property (copied from the spike, which proved them).
10. Writing `MoveState`, `PlayerPose` and `LocalTransform` every tick marks the ghost chunk changed even for idle players, so the send system compares them every tick. Cheap, but measure in the 200-ghost budget test; the fix (skip idle) is an `IJobChunk` change check.
**What remains** (M3a-2 and later): see the unchecked line under M3a-1 in section 4.

## 31. M2c finding: security software freezes freshly built player exes (29 Oct 2026)
**Symptom:** in the real-socket 2-client test (`Probe/Scripts~/run_probe_2client.ps1`) the built Mono server and client players start, work for 3-7 s, then stop answering: the server page accepts TCP connections and never replies, a second client freezes at boot, the PowerShell driver hangs.
**Evidence (live, run 5, 14:55):** client 54584: all 66 threads `Wait / Suspended`, 1.6 s CPU in 13 minutes; server 34996: 77 of 78 threads `Suspended`, 1.7 s CPU; all `Responding=True`, no log lines. Windows Defender real-time protection was OFF (`Get-MpComputerStatus`); Norton Security (NortonSvc, NortonUI) and the Avast engine (`aswEngSrv`) were running. A security product suspends unsigned, freshly built player processes that open network ports (the earlier Norton `IDP.Generic` note in the dedicated-server build gotchas fits). This is a diagnosis from thread states: the product that suspends them is not confirmed. The earlier fixes (page request threads, file traces, driver cap) were NOT the cause; kept as harmless.
**What passed before each freeze (real sockets):** server up; page `/status` 200 and `/players` without key 403; client A roster answered ("roster-ok"); client A joined (`player joined key=1 networkId=1`, client `connected networkId=1`, `/players` showed PlayerKey 1). Not reached: client B, kick -> exit 4, `POST /stop` -> save -> exit 0, save-before-disconnect.
**Decision (owner, 29 Oct): option A.** The owner adds Norton exclusions themselves (no session touches security settings): `C:\Users\dlaco\Krytical Nova II DOTS\Builds\` and `C:\Users\dlaco\KN-worktrees\` (the spike and light worktrees). No built-player runs until the owner says "exclusions done". Then re-run driver steps 3-4 with the existing builds (check the exes still exist; rebuild only if quarantined).
**Alternatives:** (B) pause real-time protection for one ~3 min run with the owner present; (C) run the same script against a Linux IL2CPP server in WSL (Norton does not scan inside WSL; needs an IL2CPP Linux build, ~18 GB peak).
**Lesson for the test harness:** before blaming code for a hung built player, check the thread states (`Suspended`); the driver should log process thread states when a step times out (TODO).

## 33. Evidence: M3a-1 green (29 Oct 2026, batch EditMode in the clone, 63 s)
**1166 passed, 5 failed (the known gear/icon content failures), 9 skipped of 1180.** All 38 Player.Entities tests pass (16 world-free movement tests + 22 world tests: click -> move -> arrive with identical integer state on server and owning client at 0 and 100 ms with rollback, stop, teleport snap, walk -> run, client plan survives rollback, observer interpolation, server corners owner-only, refused click, server-order override, random command sequences (4 seeds), 200-ghost budget, ghost shape, server-order-mid-walk regression over 150 ticks).
Bugs this run found and fixed on the way: (1) the ghost authoring class lived in a file with another name, so Unity dropped the script and the prefab singleton never baked; (2) a teleport job asked for the same component read-only and as an enable switch (job-safety exception every tick); (3) a test counter that measured back-to-back runs, not re-simulations; (4) seed 11: a one-tick difference at the tick a client click starts (owner takes the order one tick after the server, converges at once). The comparison skips only the tick where the server's order changes; every other tick and the final state are compared exactly. Cause of the one-tick start difference is a hypothesis (command-stream tick attribution), not proven: watch it in the real 2-client run.
M3a-1 DONE at the EditMode layer; real-socket/2-client confirmation waits for the Norton exclusions (section 31).

## 34. M3a-2a notes (29 Oct 2026, written without an Editor; compile-checked with `compile_check.py` only, no Unity run yet)
Assembly `KryticalNova.Player.Entities.Presentation` in `Assets/_Game/Gameplay/Player/Entities/Presentation/` (Runtime + Tests/Editor), namespace `KryticalNova.Player.Entities.Presentation`. It references Core (`KryticalNova.Runtime`), `Player.Entities` and the Unity packages only (no Networking.Entities: nothing here needs a connection type). It is client-side and managed, so it is not Burst. Not part of M3a "done": M3a stays open until M3a-2b.
- **What:** `GhostBodyRegistry` (static, ghost entity/NetworkId/PlayerKey -> body, plus the server-body map; lookups drop disposed worlds and destroyed bodies; `Clear()` is the `SubsystemRegistration` reset), `PlayerBodySpawnSystem` (asks the injected `IPlayerBodyFactory` for a body per ghost, `PlayerBodyLink` cleanup component marks "has a body", destroys on despawn and in `OnDestroy`), `PlayerPresentationFollowSystem` (`PresentationSystemGroup`, every rendered frame), `FollowMath` (pure follow arithmetic), `PlayerPresentationState` (MonoBehaviour with `PlayerAnimState {Speed, Moving, Running}`), `GhostPlayerBody : IPlayerBody` (read-only view; `Owner` is `PlayerKey.None` on a client), `IClickSource`/`WorldClick`/`FakeClickSource`/`ClickCommandSource` (metres -> the mm `PlayerCommand` the existing gather takes; no camera or UI code), `PlayerPresentationSetup.Install(clientWorld, factory, clicks, registerLocalPlayer)`.
- **How the body follows:** rendered = lerp(previous sample, latest sample, T) + Offset. T runs 0..1 over one tick of frame time (predicted ghosts only change on full ticks; an interpolated ghost is already smooth, T = 1). A jump of the ghost bigger than "run speed x tick x 1.5 + 5 cm" (only when it is walking or just stopped; an idle ghost may not step at all) is a correction: the excess is moved from the sample to Offset, which halves every 0.07 s (about the old 0.2 s ease), so a rollback slides. A jump over 6 m, or a changed `TeleportSeq`, snaps in the same frame. Numbers are `FollowSettings` (a singleton in the client world overrides `Default`); they stand in for the `PlayerMoveAnticipation` numbers and move to a settings asset with the HUD/presentation settings later.
- **Local player:** the body of the ghost with `GhostOwnerIsLocal` enabled goes into Core `LocalPlayer`; every body goes into `PlayerBodies` (the registry owns the one list it serves, so several client worlds in tests do not overwrite each other). `RegisterLocalPlayer = false` keeps a second client world in tests out of `LocalPlayer`.
- **Why a registry and not a component on the body:** the ghost lives in an entity world, the body in the scene; the registry is the only link, which keeps Netcode types out of Core and lets the server body (M3a-2b) hang on the same key.
- **First-compile doubts (check on the first Unity run):** (1) `HasComponent<PredictedGhost>` as "this ghost is predicted": the prefab creation code removes/disables it on interpolated ghosts, which reads as removed on a client, not proven; (2) `World.Time.DeltaTime` in `PresentationSystemGroup` of a client world should be the frame time, but Netcode touches world time during prediction; (3) `RequireAnyForUpdate`, `TryGetSingleton` and `RemoveComponent<T>(EntityQuery)` on a managed `SystemBase` were written against 1.x signatures; (4) cleanup component `PlayerBodyLink` on a ghost entity: assumed the ghost despawn destroys everything but cleanup data so `PlayerGhostTag` is gone and the query sees it; (5) an observer's `LocalTransform` is interpolated by the default variant (M3a-1 test shows that), and across a teleport it lerps between the old and new snapshot, so an observer body may glide for the one interpolation step before `TeleportSeq` changes (the owner path is tested; the observer snap is not asserted); (6) `Assert.Inconclusive` in the correction test if the injected error does not survive to a rollback.
- **Tests (written, not run):** `FollowMathTests` (10, world-free), `GhostBodyRegistryTests` (10, plain `World`), `PresentationWorldTests` (8, `KnTestWorld` + `PlayerRig`: body per ghost and removal on despawn, start position, owner in `LocalPlayer` / remote only in `PlayerBodies`, follow over a 20 m walk at 100 ms (owner body within 0.5 m of its ghost, remote within 3 m of the server, equal at the end, `Moving` flag), rollback correction slides (largest frame step < 0.7 m against a >= 1.5 m ghost pop), teleport snaps within one frame, world dispose leaves no entry or body, a click from `FakeClickSource` walks the owner).
- **Left for M3a-2b:** the server body (`NetRole.ServerBody` Player.prefab on the server, `GhostBodyRegistry.SetServerBody` by `PlayerKey` from `ConnectionPlayerKey`, SaveAgent load/save and identity passthrough), the `Player.prefab` roles and the real `IPlayerBodyFactory` (instantiates Player.prefab as `NetRole.ClientView`, Animancer reading `PlayerPresentationState`), the mouse + camera `IClickSource` (ground ray -> `WorldClick`), installing `NavMeshPathPlanner` on the server and client, baking `MovementSettings` into `PlayerMoveSettings` (and the `FollowSettings` numbers into the presentation settings asset), GM speed/mount/rank into `MoveStats`, the Chi payment for Run, `RelevancyFillSystem`, then the owner "feel" checkpoint.

## 35. Evidence: M3a-2a green (29 Oct 2026, batch EditMode in the clone, 68 s)
**1194 passed, 5 failed (the known gear/icon content failures), 9 skipped of 1208.** All 28 new presentation tests pass on the first run (FollowMathTests 10, GhostBodyRegistryTests 10, PresentationWorldTests 8: body per ghost and removal on despawn, local owner only in LocalPlayer, follow over a 20 m walk at 100 ms, correction slides, teleport snap within a frame, dispose leaves nothing, click source drives the owner). No test returned Inconclusive (skipped count unchanged at 9). Compile gate 68/68, dependency 0 uphill/0 two-way.

### 31.1 Root cause confirmed (Master, 29 Oct 2026, from Norton's own history)
Norton **Behavioral Protection** flagged the freshly built probe exes as `IDP.Generic` ("very few users, very new, high risk") at 4:42-4:44 PM, suspended their threads and moved `KnProbeServer.exe` (the copy in `C:\KN-test\probe`) to Quarantine. The folder exclusions the owner added (Antivirus > Exclusions) did NOT help: **Behavioral Protection is a separate layer** (Norton > Antivirus > Real-Time Protection > Behavioral Protection toggle, plus its own exclusion list). Control experiment (my side, no settings touched): a server alone ran 60 s inside one open tool call, was suspended and later killed when started by a call that returned, and server + client were both suspended after the join even from a folder outside every exclusion; two Python UDP processes were never touched. The exes in the clone's `Builds\` were still present afterwards (667136 bytes each); I did not touch Norton's quarantine.
**Harness workaround options (owner's choice; no session touches security software):**
1. Server in WSL/Linux (Norton does not see inside WSL) with the Windows client only (the client is still a fresh unsigned exe: it may be flagged too; a Linux headless client avoids it entirely).
2. A signed build later (code-signing certificate; reputation builds with time and users).
3. The owner switches Behavioral Protection off for the test window (a few minutes), or adds the exes to the Behavioral Protection exclusion list.
**Harness rules added:** (a) before blaming code for a hung built player, check `Threads[].WaitReason == Suspended` and Norton's history; (b) the driver should log thread states when a step times out; (c) the Linux parity test (plan section 8, M9) runs the server in WSL anyway.

## 36. Evidence: M2c PASSED on real sockets (29 Oct 2026, 16:49-16:50, Norton Behavioral Protection switched off by the owner)
Built Mono dedicated-server player + two built Mono client players, plain UDP, probe ports 7797/7798 only, whole run 49 s, lowest free RAM 14.6 GB, no leftovers. **All 19 checks PASS** (report `Builds\KnProbeRuns\20261029-164931\probe-report.md`): RAM/ports preflight; server up (page after 1.8 s); page owner-only (403); client A roster answered ("roster-ok") and connected (NetworkId 1, PlayerKey 1); client B connected (NetworkId 2, PlayerKey 2, two different keys); `POST /kick?player=2` -> 202 and client B exits 4 "kicked" 1.1 s later while A stays; `POST /stop` -> 200, server exits 0, 'SAVE ALL (probe)' printed, order stopping -> save -> "Saved. Waiting for the players..." and client A dropped 187 ms AFTER the save ended (save-before-disconnect), A ends "server-closed" exit 0; server result file joins=2 rosterAnswers=1 saves=1 stopped/0. Thread states at +10 s: 0/78 and 0/66 suspended (nothing frozen).
**Open points from the run:** (1) server took 32.8 s from `/stop` to exit although everyone was gone within 0.2 s of the save: the stop machine waits for the connections to disappear (the transport's disconnect timeout) before `Application.Quit(0)`: **FIXED, pending verification (written without an Editor, not compiled or run):** after the disconnects the stop machine waits at most `GraceTicks` (3 s, `KnNetDefaults.StopGraceSeconds`, `stopGraceSeconds` in `server-settings.json`), quits at once when no connection is left, and otherwise force-closes the rest (`NetworkStreamDriver.Disconnect`, the legal call; the receive system destroys the entities) before `Application.Quit(0)`; save-before-disconnect order unchanged; to confirm in the next real-socket run that `/stop` to exit is now about 1-4 s; (2) the one-tick click-start difference of section 33 is not exercised by this probe (it sends no clicks): it needs the real input path (M3a-2b); (3) layer D under Windows needs Behavioral Protection off or a signed build (section 31.1).
M2 is DONE at layers A-D for the Windows Mono path (direct UDP, plain). Still manual (section 28): DTLS, Relay, missing certificate; Linux/IL2CPP server (M9).

## 37. M3a-2b owner decisions (29 Oct 2026) - design in `netcode-for-entities-designs/m3a2b-design.md`
D1 = body first, ghost last (a refused player never gets a ghost). D2 = unported systems run as offline-style local stubs behind a "not yet networked" banner and never save. D3 = only `look` and the progression header are real; everything else is PASSTHROUGH (loaded JSON kept byte-for-byte, never dirty). D4 = a ViewMode inside the existing `PointAndClickPlayer` (about 5 guards) feeding `IClickSource`. D5 = host mode OUT: dedicated server + clients only.
Included in the M3a-2b tests: the one-tick click-start difference (section 33) through the real input path, in a 2-client built run later (needs Behavioral Protection off for the test window or the Linux route).
Rule: after each milestone the mini-boss handover note (`dots-HANDOVER-mini-boss.md` in the notes folder) is updated.

## 38. M3a-2b steps 1-4 notes (29 Oct 2026, written without an Editor; NOT compiled, NOT run: Unity was busy and free RAM was under 8 GB)

Design: `netcode-for-entities-designs/m3a2b-design.md` (steps 1-4 of 10). One commit per step on `dots/m3a`. Owner decisions D1-D5 as in the design (body first, local stubs behind a banner, only `look` and the progression header real, `PointAndClickPlayer` ViewMode later, no host mode).

**Step 1: seams, adapter, session.** Core: `Bodies/BodyRoleTag` (kind, key, serial, local flag, `Mover`), `IBodyMover`, `IRoleAwareBody`, `Net/ServerPolicy`. Networking.Entities: `BodyRoleNetRoleAdapter`, `KnNetSession`, `KnPlayerRefusals` + `KnPlayerRefusalSystem`. Choices that differ from the design text:
- The adapter and the session WRAP the adapter/source installed before them (NGO). While no Kn server or client world exists every question goes to the old one, so the NGO path is unchanged. They are installed when the first Kn world is created (`KnBootstrap.StartHost`, `StartClientWorld`, dedicated server), never at startup (the design said "after Core's reset", which races the NGO adapter's BeforeSceneLoad install). Needs one Core getter, `NetSession.Source`.
- `ServerPolicy` is `RequireProofOverride` (nullable) + `RequireProof(fallback)`: NGO keeps asking `NetworkConnectionManager.RequireProof` through the fallback; `KnServerEntry.BuildJoinCheck` sets the override.
- `BodyWorldTargets` (players only) is NOT done: it belongs to the Bodies assembly and needs the registry; moved to step 5/7.
- Tests (`BodyRoleAdapterTests`, EditMode, plain GameObjects; `KnPlayerRefusalSystemTests`, W): process/fallback, Of() Offline for non-aware components, tag kind for an aware one, child finds root tag, owner only for local ClientView, `OwnerId` -> serial -> `PlayerKeys.Find`, `DisconnectOwner` queues once and ignores a dead key, queue drops entries whose key died, Install idempotent with fallback, `ServerPolicy`, queued refusal reaches only the right client and closes it.

**Step 2: role table + assembler.** New assembly `KryticalNova.Player.Entities.Bodies` (folder `Gameplay/Player/Entities/Bodies/`, references only Core: classification is by type NAME, so Player/Combat/Economy types are not needed). `PlayerRoleTable` (groups Presentation, Movement, Automation, RoleAware, Rules, Infrastructure; shapes Server / ViewLocal / ViewRemote; actions Keep / Strip / Disable), `PlayerBodyAssembler` (`Build` = inactive holder + tag + strip + pose + kinematic, `Activate`, `Create`, `Apply`). Choices: (a) the design says `PlayerInput` is stripped on the server AND `PointAndClickPlayer` is kept disabled, but `PointAndClickPlayer` has `[RequireComponent(PlayerInput)]`: `Apply` destroys dependants first and DOWNGRADES a part that a kept part requires to "disabled" (reported in `BodyBuildReport.Downgraded`), instead of precomputing an order; (b) a type the table does not know is kept; the lint test fails on the real prefab so a new player system forces a decision; (c) `Build` returns the body still asleep so structure tests need no Awake.
Tests (`PlayerBodyAssemblerTests`, EditMode, real `Player.prefab` through `AssetDatabase`, no Awake): lint (every component classified), table spot checks, dependency order on fakes, server/local/remote structure, kinematic Rigidbody, no stray holder. First-compile/first-run doubts: the lint will probably name a few built-in or nested-prefab types the table lacks (add them); the remote-view test asserts every surviving rule stub is a downgraded one and logs the list.

**Step 3: save passthrough.** Core `SavePassthrough` (real = `look`, `progression`; every other part passes through; explicit list of the 19 known passthrough keys for the lint), Persistence `SaveParticipantSet` (pure `Load`/`Capture` replacing the two loops of `PlayerSaveAgent`). Choice: the agent uses `SavePassthrough.Is` ONLY when its body carries a `BodyRoleTag` (resolved in `Awake`); every other body uses `SavePassthrough.Never`, so the NGO/offline save behaviour is identical. Agent edits: `IRoleAwareBody`, the loops, the level header (left as loaded while `progression` passes through), `ServerPolicy.RequireProof`. `ServerSave.TryBind`: `holder.IsSpawned` -> `NetRole.IsNetworked(holder)`. `PlayerIdentity`: `ServerPolicy.RequireProof`, new `ServerBegin(state, account, characterId)` (null = in, else refusal text; `ApplyView` is left for step 7/8).
Tests (`SaveParticipantSetTests`, `SavePassthroughTests`): byte-identical passthrough with odd JSON, real parts use their own formatting, mixed set, throwing restore/capture, migration failure of a real part aborts before any restore, a passthrough part the build cannot read still loads, null policy = old behaviour, account scope, default policy, lint that every `ISaveParticipant` on `Player.prefab` is classified and keys are unique.

**Step 4: GoInGame wire + admission slot.** `GoInGameRequest : IRpcCommand` (id, name, one page of the look text, at most 3 pages of 480 characters), `KnClientGoInGameSystem` (sends once per connection from `IKnGoInGameSource`, then puts its own side in game; waits while the game has nothing), `KnAdmissionSystem` in the new `KnRulesGroup` (after `GhostSimulationSystemGroup`; hands one `KnAdmissionRequest` to `IKnAdmission`; admitted -> `NetworkStreamInGame`; refused, no admission installed or an admission that throws -> reason through `KnPlayerRefusals`), `GoInGamePages` (reuses `KnRosterPages.Assembler`). Dropped silently: request from a dead key, repeat after the decision. Malformed (page count above 3, ids change between pages) is refused.
Tests (`GoInGamePagesTests` pure; `KnGoInGameTests` W): paging, 3-page request arrives whole, only the asking client goes in game, repeat ignored, refusal text reaches the player, no admission -> reason, throwing admission -> reason, dead key dropped, malformed refused, nothing-to-send waits, group order probe (R7). Not tested here: the largest LEGAL recipe (63 parts, 15 colours) fitting 1440 characters needs `CharacterRecipe` (Gameplay); add it next to the first real `IKnAdmission` in `Persistence.Entities.Tests` (step 5).

**Needs a built-player test (B):** Awake/OnEnable/Start of the stripped real prefab (design B1); real participants round trip against the test save service (B2); the 2-client run (B3). **Not written because it needs internals:** `TryBind` refuses a second networked holder / hands over from the offline avatar (`ServerSave.TryBind` and `SaveAndRelease` are internal; add `InternalsVisibleTo` or a public test seam in step 5). New files have hand-written `.meta` files (Unity keeps the GUIDs).
**What remains (steps 5-10):** server body lifecycle + `PlayerAdmission` + `ServerBodySyncSystem`/`ServerBodyMover` + `PickPoint`, `PlayerLeaveHandler`, client view (`ClientBodyFactory`, ViewMode edits in `PointAndClickPlayer`, `PlayerInventory.Local`, `NetworkPlacement`), look for observers, real-prefab smoke (B), 2-client gate (B). First thing to do when the Editor is free: `compile_check.py` (+ `--server`), then the batch EditMode run; expect small first-compile fixes in the new files.

## 39. Evidence: M3a-2b steps 1-4 green (29 Oct 2026, batch EditMode in the clone, 68 s)
**1251 passed, 5 failed (the known gear/icon content failures), 9 skipped of 1265.** Compile gate 70/70, `--server` 32/32, dependency 0 uphill/0 two-way. First compile found 2 bugs, fixed: `GetInstanceID()` is obsolete in 6000.6 (role adapter now caches by GameObject); `KnTrace` existed only in development/probe builds, so a RELEASE dedicated-server build would not have compiled (it is now always compiled and a no-op without `-knout`). First test run: 3 assembler tests failed because Unity does not auto-add a RequireComponent for fake MonoBehaviours declared in a file of another name (tests now add the fake `PlayerInput` explicitly; the real prefab carries it). Passed: role adapter, refusals, save passthrough + participant lint over the real Player.prefab, go-in-game pages and world tests, assembler rules. Still built-player (B): the Awake/Start chain of the stripped real prefab, real participant round trips, the 2-client run.

## 40. M3a-2b steps 5-10 notes (29 Oct 2026, written without an Editor; NOT compiled, NOT run: a 43-minute Unity build had the machine)

Design: `netcode-for-entities-designs/m3a2b-design.md` steps 5-10 (the copy in the notes folder). One commit per step on `dots/m3a` (steps 5-9 code + tests, this note = step 10), each marked "unchecked (no compile)". Owner decisions D1-D5 kept. The Netcode for GameObjects path is unchanged: every new branch is behind a `BodyRoleTag` (only bodies the entity netcode builds carry one) or a Kn world.

**Step 5: server body lifecycle.** `Player.Entities.Bodies`: `ServerBodyAdmission : IKnAdmission` (spot -> real body as ServerBody -> `IServerBodyRules.Begin` (character + save, may refuse: body destroyed, no ghost) -> ghost on the body's spot + `PlayerGhostKey` -> `ServerBodyMover` on the tag -> registry -> `Admitted`); `ServerBodySyncSystem` (`KnRulesGroup`, OrderFirst: ghost pose -> body transform, then ONE `Physics.SyncTransforms`; on world teardown it tells and destroys the bodies left); `ServerBodyMover : IBodyMover` (`MoveTo` -> `IssueOrder`, `Teleport` -> ghost teleport + body at once, `Stop` = a placement on the ghost's own pose because the ghost has no server stop order yet); `PoseMath`. Networking: `PlayerSpawnService.PickPoint` + pure `NextFreeIndex` (NGO `PlaceSpawnedPlayer` untouched), `KnWorldHooks` (Server/ClientWorldMade, raised by `KnBootstrap` for the dedicated server and `StartClient`; NOT for `StartHost`, D5). New assembly `KryticalNova.Persistence.Entities` (`Gameplay/Persistence/Entities/`): `PlayerAdmission : IServerBodyRules` (decode the look, refuse without proof where proof is required, `PlayerIdentity.ServerBegin` -> the agent loads inside that call, `IsLoaded` checked; the agent's own refusal text wins because `KnPlayerRefusals` keeps the first per key), `RecipeText` (compact ASCII look: JSON of a full recipe can pass the 1,440 characters of the request, this form is about 550), `PlayerWorldsSetup` (registers at AfterAssembliesLoaded, skipped with `-knprobe`; prefab = override or the scene NetworkManager's PlayerPrefab, asked at each admission).
Departures from the design: (a) the lifecycle is split into the generic `ServerBodyAdmission` (Bodies, testable with a stand-in body) and `PlayerAdmission` (Persistence.Entities, the real rules) instead of one class; (b) the spot is picked BEFORE the body is built (Awake sees its real place; same result); (c) `PlayerMoveSettingsBaker` is not written: the ghost keeps its prefab's walk/run speed (the `MoveStatsOf` hook is there, null) until M3b; (d) the planner's agent type and offset come from the first body's `NavMeshAgent` (the prefab is not loaded yet when the dedicated server's world is made); (e) `BodyWorldTargets` is still not written (nothing in 2b needs it).
Tests: `ServerBodyLifecycleTests` (W, stand-in): refused load -> 0 ghosts, body destroyed, 0 in game; exact order body -> begin -> loaded -> ghost -> admitted with 0 in game and 1 ghost at that moment; body equals ghost pose for every sample of a probe rules system of the same group; physics synced exactly once per server tick; teleport (body at once, ghost next tick, TeleportSeq bumped); stop drops the walk; two players on two spots, the second pick saw the first body; same key twice refused; world teardown destroys bodies; 200-body sync measured (no assert). `PlayerSpawnPickTests` (E), `RecipeTextTests` (E: largest recipe round trip and fits 3 pages = the step-4 leftover, field lint, malformed refused, name escaped to ASCII).

**Step 6: disconnect.** `PlayerLeaveHandler` (in `ConnectionClosedSystem.Handlers`, after the key release): 1 ghost `GhostOwner.NetworkId = 0` + `PlayerLeaving`; 2 `ServerLeaving.Notify` (agent SaveAndRelease, trade cancel); 3 registry cleared, mover dropped, body destroyed; 4 ghost destroyed at once (never via the connection's LinkedEntityGroup); 5 `RpcBudget.Forget` + `NetworkHealth.Forget` (wired by `PlayerWorldsSetup.InstallLeave`). `DeferGhostDestroy` variant + `PlayerLeavingCleanupSystem` (KnRulesGroup, OrderLast). `ServerRecords.Left` is not called (keyed by the NGO client id and inert without a NetworkManager; M4). Server stop: `ServerSave.FlushAll` (the stop machine's SaveAll) already saves every bound agent; the disconnects then run the handler for each body.
Tests: `PlayerLeaveTests` (W): order invalidated -> leaving(owner=0) -> left -> body destroyed -> ghost destroyed -> forget; a second close = only forget; rules run when the ghost is already gone; deferred variant stays ownerless until destroyed; NetworkId reuse (Inconclusive if netcode did not reuse): new key, old ghost gone, exactly one ghost with `GhostOwnerIsLocal` on B; server stop: "save all (connected=2)" before both leavings, no bodies or ghosts left.

**Step 7: client view.** Core `BodyClicks` (ground/stop queue, newest 4 kept); Bodies `BodyClickSource : IClickSource`, `ClientBodyFactory : IPlayerBodyFactory` (real prefab as ClientView, the offline avatar put away before the local body wakes, banner `NotYetNetworkedBanner` on the local body = D2), `PlayerWorldsSetup.InstallClient` (factory, clicks, client NavMesh planner configured from the local body's agent, look applier, `KnGoInGame.Source` = `CharacterSessionGoInGame`). `PointAndClickPlayer` (D4): `ViewMode` (tag kind ClientView); Start never stands the agent on the NavMesh; Update skips the agent's walk and pending interact/travel; a ground click -> `BodyClicks.Ground(point, run)` + marker; TravelTo -> a click at the goal; attack, walk-to-unit, interact, jump, wall and water -> "Not networked yet" message (selection stays); OnEnable never replaces `PlayerBodies`; `OnBodyLocalChanged` (input + camera when the local flag settles). ServerBody: `ServerMoveTo`/`ServerStopMoving` go to the tag's mover (running is NOT charged yet: M3b), `ServerJumpTo` says not networked. `PlayerBodySpawnSystem` also decides "local" by comparing the ghost's owner with the world's own NetworkId (the body is built in the right shape at once instead of a frame later); `GhostBodyRegistry.SetLocal` keeps the tag's flag in step. `PlayerInventory.OnEnable`: Local also for the local ClientView. `NetworkPlacement.Place`: a tagged body with a mover is placed through it. `PlayerIdentity.Start`: the offline auto-assign runs only for untagged bodies and the local ClientView (a remote view would otherwise wear this player's own character).
Tests: `ClientViewTests` (E + W, stand-in with fakes named PlayerInput/PointAndClickPlayer): local body keeps input, click code and banner, remote has none, avatar released once; missing prefab -> null; registry keeps the tag flag and LocalPlayer in step; click queue cap; 2 clients: one local body, the tag agrees with the registry, LocalPlayer is the client's local body, a `BodyClicks.Ground` walks the server body. `NetworkPlacementBodyTests` (E).

**Step 8: look for observers.** `PlayerLook { [GhostField] uint Version }` baked on the ghost; `LookRequestRpc` / `LookReplyRpc` (paged with `GoInGamePages`); `PlayerLookServerSystem` (`SetLook` bumps a process-wide version once per different text, answers by owner NetworkId, at most 64 replies per update); `PlayerLookClientSystem` (cache by version, asks once, again after 30 frames, applies through `ILookApplier`); `IdentityLookApplier` -> `PlayerIdentity.ApplyView` (Apply without the starter kit). `PlayerAdmission.Admitted` sends the look the body wears after the load (the saved look may win) and every later `Changed`. Versions are unique per process, so a cached text never belongs to a reused NetworkId's new owner.
Tests: `PlayerLookTests` (W): a 3-page look reaches the owner's own body and a late joiner; a cached version is applied again without a request; the version goes up once per different look only; version 0 asks nothing; the cache is bounded.

**Step 9: real-prefab smoke (B, written, not run).** `Persistence/Entities/Tests/PlayMode/RealPrefabSmokeTests` ([Explicit], category B, PlayMode, MemorySaveStore only): B1 server body: no new errors over 120 frames (the allow-list is printed as `[M3A2B-B1] log:` lines on the first run, then tightened), `LocalPlayer.Object` and `PlayerInventory.Local` null, no `X.Local` static points at it (reflection), agents off, body not moved by physics, `PointAndClickPlayer` kept and off. B1 client views: `PlayerInventory.Local` and `LocalPlayer` = the local stub, ViewMode on, `PlayerBodies` not replaced, remote has no click code and no inventory, keeps `PlayerIdentity`. B2: every passthrough part (account and character) byte-identical through admit -> load -> save; the look is saved.

**Step 10: the 2-client gate (B3; to run with the owner's/Master's OK, Behavioral Protection off for the test window or the Linux route, section 31).** Windows dedicated test server on the probe ports (7797/7798) with the TEST save service (`SaveService --test`, 7802, `krytical_nova_test`), two built clients: both sign in and enter; each sees the other's body and look (late joiner too); walk both; **the one-tick click-start difference of section 33 through the real input path** (mouse click -> `PointAndClickPlayer` ViewMode -> `BodyClicks` -> gather -> owner prediction vs the server's start tick, logged per click; expect 0 or 1 tick, record the distribution); kill one client mid-walk; a second login of the same account is refused with the existing text; the server log shows the save before the ghost destroy; test DB parts unchanged byte for byte except `look`, the header and the place; Game-view screenshots per step; no exception lines over the run.

**First-compile doubts (check first when the Editor is free: `compile_check.py` + `--server`, `dependency_map.py --check`, then the batch EditMode run):**
- The new assembly `KryticalNova.Persistence.Entities` (+ Tests/Editor, Tests/PlayMode) must appear in compile_check's list; `Player.Entities.Bodies` now references Entities, NetCode, Player.Entities(.Presentation) and Networking.Entities. Expected 0 uphill / 0 two-way (Persistence -> Player, Player -> Networking: downward or same layer).
- `CharacterNetState` implements NGO's `INetworkSerializable`: Persistence.Entities and the PlayMode tests reference `Unity.Netcode.Runtime` for that.
- `[UpdateInGroup(typeof(KnRulesGroup), OrderFirst/OrderLast = true)]` on systems of another assembly than the group.
- The new `PlayerLook` ghost component changes the player ghost's serializer: M3a-1/2a ghost tests rebuild their test prefab per session; any snapshot-size expectation is re-measured.
- RPC codegen for `LookRequestRpc` / `LookReplyRpc` in the Bodies assembly; `RpcSizeLintTests` covers them (about 520 B < 1300).
- Lambdas with `out` parameters (`Picker = (IReadOnlyList<Vector3> occupied, out Vector3 position, out Quaternion rotation) => ...`) and `prefab ?? (Func<GameObject>)PlayerPrefab`.
- The ProjectOrganization validator may not know a `Tests/PlayMode` folder (move to `Tests/Editor` with `[UnityTest]` + `EnterPlayMode` if it objects).
- Tests that may need tuning on the first run: exact trace sequences (the refusal system or the leave handler might add a line), the physics-sync count vs server ticks (rate-manager catch-up on the first frame), the deferred-cleanup count, the reuse test (Inconclusive if ids are not reused).

**What remains after this:** compile + EditMode run of steps 5-9; the B runs (B1 and B2 in the Editor's PlayMode with OK; B3 the 2-client built run above); the `PlayerGhostPrefab` subscene in the Bootstrap scene (R8: without it every admission is refused with "not ready" and an error line); the PlayerPrefab source on a DOTS dedicated server (if the Bootstrap scene keeps no NGO NetworkManager, set `PlayerWorldsSetup.PrefabOverride` from a scene component); `PlayerMoveSettingsBaker` and charging Flux for server-ordered runs (M3b); a server stop order for the ghost (M3b); `BodyWorldTargets`; `ServerRecords` access lines for the entity netcode (M4); host mode stays out (D5). Then the Encyclopedia of Nova rows (Server Body, Client View, Passthrough save, Look version), the Server Guide note on how a player enters and leaves, `graphify update .`, and the mini-boss handover note.

## 41. Evidence: M3a-2b steps 5-9 green (29 Oct 2026, batch EditMode in the clone)
**1297 passed, 5 failed (the known gear/icon content failures from main), 9 skipped of 1311 (130 s).** Compile gate on the FIRST compile of steps 5-9: `compile_check.py` 73/73, `--server` 33/33, `dependency_map.py --check` 0 uphill / 0 two-way (207 links). All 67 tests of the new areas pass (`ServerBodyLifecycleTests`, `PlayerLeaveTests`, `ClientViewTests`, `PlayerLookTests`, `PlayerSpawnPickTests`, `NetworkPlacementBodyTests`, `RecipeTextTests`); `AReusedNetworkId_NeverInheritsTheOldGhost` ran to the end (netcode did hand the NetworkId out again, not Inconclusive). Measured: 200 bodies synced in 0.19 ms in one server update (Editor, physics sync included). Peak batch Unity 9.1 GB, lowest free RAM 3.3 GB.
The first run had 6 new failures, all in the tests, none in the runtime code: (1) `LeaveRecorder` sat in its own file in a `Tests/Editor` folder, and Unity refuses to add a MonoBehaviour whose script file is in an Editor folder ("it is an editor script"), so the stand-in body had no recorder (5 failures); it is now declared in `BodyRig.cs`, as the other fakes are. (2) The reused-NetworkId test added client B after the subscene load, so B had no ghost prefab and netcode dropped it; `KnTestWorld.LoadSubScene` takes an optional single world and B loads it before going in game. Second run: a destroyed server ghost keeps only netcode's cleanup component until the ghost send system releases it, so `EntityManager.Exists` stays true for a few ticks; the leave tests now call a ghost gone when it has no `PlayerGhostTag` (the runtime already checked real components: `ServerBodyMover.IsLive`, the look store, the cleanup query).
Not covered here (layer B): B1/B2 PlayMode smoke tests ([Explicit]) and the B3 built 2-client gate; the R8 subscene and the dedicated server's PlayerPrefab source (section 40, "What remains").

## 42. R8 ghost subscene + the server's PlayerPrefab source (29 Oct 2026, written; builder NOT run, compile pending)
No Bootstrap scene exists yet (the plan makes it at cutover), so the ghost subscene is loaded per world instead of by a scene (option A put to Master; B = make the Bootstrap scene now, C = map scenes). `PlayerWorldAssets` (ScriptableObject, Player.Entities, at `Player/Entities/Resources/PlayerWorldAssets.asset`) holds an `EntitySceneReference` to `Player/Entities/Scenes/PlayerGhostPrefabs.unity` (a `PlayerGhostPrefabAuthoring` holder pointing at `Player/Entities/Prefabs/PlayerGhost.prefab`: Owner Predicted, Has Owner, Dynamic, no test trace ring) and Player.prefab. `PlayerWorldsSetup.InstallServer/InstallClient` load the subscene into the world the moment `KnBootstrap` makes it (`BlockOnStreamIn`; `loadGhostScene: false` for test worlds that bring their own); `PlayerPrefab()` = override, else the asset's Player.prefab, else the scene NetworkManager's. The three assets are made by the Editor builder `PlayerGhostAssetsBuilder` (new assembly `KryticalNova.Player.Entities.Editor`; menu Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets; batch `-executeMethod KryticalNova.Player.Entities.Editor.PlayerGhostAssetsBuilder.Build`), which updates in place so the GUIDs (ghost type, scene reference) never change.
Tests (`PlayerGhostAssetsTests`, E + W): prefab shape; the subscene names the prefab's GUID; the asset loads from Resources and names the shipped subscene and Player.prefab; loading the shipped subscene into a server + client test world gives each a `PlayerGhostPrefab` whose prefab has `PlayerGhostTag` and `PlayerLook`. They fail until the builder has run (next Unity slot, before B1/B2).
Open: that a player build includes an entity scene referenced only from a Resources asset is unproven (B3 proves it; fallback = option B).

## 43. B3 plan: the 2-client gate by the Linux route (owner pick 29 Oct 2026: server in WSL, Norton Behavioral Protection stays ON; test lane only)
Norton's Behavioral Protection flags every freshly built unsigned Windows exe (section 31.1), so with it on, **no built Windows player runs**: not the server and not the clients. The Editor (`Unity.exe`, signed) is not touched (the owner's Linux save test, 29 Oct, played from the Editor).
**Layout (proposed; the second client is the owner's choice, see the card):**
- **Server:** the clone's game as a Linux x64 **Mono** dedicated server (`UNITY_SERVER`), development build, map `Zone01_StarterTown` (NavMesh + spawn points), run in WSL2 Ubuntu by hand-written script, `-port 7797 -pageport 7798`, files of its own under a scratch folder; `-port` is mandatory (`KnServerSettings.DefaultPort` is 7777). Save service: the TEST lane only (`SaveService --test`, 7802, `krytical_nova_test`), the same way the Linux save test reached it (server-plan's WSL box: plan `online-linux-cloud-servers-plan.md` "Player save-loss test"); never 7777/7778/7801/8090.
- **Client A:** the clone's Editor in Play mode (real mouse clicks through `PointAndClickPlayer` view mode, Game-view screenshots), connecting to the WSL IP (read fresh; NAT forwards only TCP to 127.0.0.1) or through server-plan's UDP relay.
- **Client B, late joiner and the second login:** Linux headless DOTS clients in WSL (recommended; Norton never sees them): one built Linux client (`-batchmode -nographics`, dev-bot token through `KnDevBots`, `KN_DEV_BOTS` build) driven by a small script mode: join, walk a fixed list of ground points through `BodyClicks`, log every click (client tick, server start tick), exit; a second copy joins late; a third copy uses B's account and must be refused with the existing text.
**Code before the run (code-only, next):**
1. `KN_ENTITIES_SERVER` define, set only by the DOTS server build: in the clone `UNITY_SERVER` today also starts the Netcode for GameObjects server parts (`NetworkConnectionManager` dedicated start, `ServerLifetime` start backup + ledger trim, `ServerControlPage`, `ServerReach`), which would fight the entity server for the port and touch the save store. Under the define those parts stay off; `DedicatedServerBoot` still loads the map. Without the define nothing changes.
2. `KnGameServerBuild` (Networking/Entities/Editor): Linux64 Mono Server subtarget + `KN_ENTITIES_SERVER` + `KN_DEV_BOTS`, scenes = Zone01 only (and the Bootstrap-less path of R8), output `Builds/KnGameServerLinux`; and the Linux headless client build (`Builds/KnGameClientLinux`). Restores every setting it changes (as `KnProbeBuild`). Expected cost: a full-game Mono build, up to 40 min and about 17 GB: one heavy job, with Master's word.
3. Click-start log: client `[KnClick] seq tick` at gather, server `[KnClick] seq startTick` at plan; a script reads both and writes the distribution (expect 0 or 1 tick, section 33).
4. `run_b3_linux.sh` (WSL side: start server, three headless clients, kill -9 B mid-walk, POST /stop, collect logs) + a Windows checklist for client A (screenshots per step).
**Pass:** both enter; each sees the other's body and look (late joiner too); both walk; click-start differences recorded; B killed mid-walk -> its save is written before its ghost goes (server log order); the second login is refused; test DB parts byte-identical except `look`, header and place; no exception lines; the build contains the R8 subscene (admission not refused "not ready"; else fall back to option B, a Bootstrap scene).
**Open, to verify in code first:** a dev-bot account is named but not proven (`KnJoinCheck`); `PlayerAdmission` refuses an unproven account when the server requires proof, so the test server runs with `requireProof` off for bots or the bots get test-lane accounts.
**43.1 Found while planning the headless clients:** a dev-bot account is a real account id (`AccountIds.FromUnityPlayer("devbot|n")`, named, not proven), so `PlayerAdmission` treats a bot as a saved player and the save agent looks its character up in the server's roster. A bot's character is in no roster ("That character is not on this server", the same finding as the Linux save test), and the entity netcode's roster is read-only so far (`KnRosterReply`; creating a character is not ported). Without a fix every bot is refused at admission. Options put to the owner: (d, recommended) a test-lane server (`KN_TEST_LANE` + `devBots`) creates a missing bot character through the game's own roster/create path on its first admission, test database only; (a) seed bot characters into the test database with a one-off script through the test save service; (b) port roster create to the entity netcode first (needed at cutover anyway, larger); (c) bots play unsaved and the kill-mid-walk save check moves to client A (an Editor cannot be killed like a process: weaker).

## 44. Evidence: R8 assets built, EditMode green, B1/B2 PASSED (29 Oct 2026, batch Unity in the clone, below-normal priority, RAM watchdog)
- **Builder** (`-executeMethod KryticalNova.Player.Entities.Editor.PlayerGhostAssetsBuilder.Build`, 51 s, peak 5.8 GB): made `Prefabs/PlayerGhost.prefab` (Owner Predicted, Has Owner, `prefabId` = its GUID), `Scenes/PlayerGhostPrefabs.unity` (scene GUID 2a03233205580be4b9aa522d6b526638, the same on every rerun), `Resources/PlayerWorldAssets.asset` (that subscene + Player.prefab). Two builder bugs found by running it: a batch run cannot open a scene additively beside an untitled one (the subscene is now the only scene in batch); netcode's `OnValidate` fills `prefabId` in memory, so "already right" skipped the save and the file kept it empty, which the baker rejects (now always written and saved).
- **EditMode: 1304 passed, 5 failed (the known gear/icon content failures), 9 skipped of 1318.** New and green: the 4 `PlayerGhostAssetsTests` (the shipped subscene, loaded into a server and a client test world, gives each a `PlayerGhostPrefab` with `PlayerGhostTag` and `PlayerLook`), `KnServerPureTests.TestLane_RefusesEveryPortOfTheRealServer`, `TestLaneBotCharactersTests` (off without `KN_TEST_LANE`), `PlayerClickLogTests` (each click logged once per side under one seq; **server tick minus owner tick = 0, 0, 0** in the test world). One test bug on the way: `UnityEngine.Hash128.Parse(guid)` reads a GUID in another byte order than Entities, so it named a scene that does not exist; the test now uses `Unity.Entities.Hash128(guid)`.
- **B1/B2 PlayMode (`-testFilter ...RealPrefabSmokeTests`, 114 s, peak 7.2 GB): 3 of 3 PASSED** on the first run: B1 server body (no new errors over 120 frames, no `X.Local` static points at it, agents off, not moved by physics), B1 client views (local stub owns `PlayerInventory.Local`/`LocalPlayer`, view mode on, remote bare), B2 passthrough parts byte-identical through admit -> load -> save on a MemorySaveStore. The only log lines B1 recorded were netcode "Server Tick Batching" warnings (the batch Editor falling behind), none from the game's stubs.
- 43.1 built (owner pick option 1): `TestLaneBotCharacters.Ensure` (finds a bot's character by name in its account or makes it through `ServerRoster.Handle` create; `KN_TEST_LANE` only), `KnDevBots.RememberBot/IsBotAccount` (filled by the join check), `PlayerAdmission` loads the id it returns; `[KnClick]` logs via `PlayerClickLog.Sink`.
- Left for B3: the headless test-client script mode and `run_b3_linux.sh` (code), then the two Linux builds (>= 19 GB free, Master clears room) and the WSL run.

## 45. B3 code written (29 Oct 2026; compiled with the test-lane defines, not run)
- **Headless bot** `Persistence/Entities/Runtime/KnTestClient.cs` (`#if KN_TEST_LANE && KN_DEV_BOTS`, so only in the Linux test client build): `-kntestclient <n> -connect <host> -port 7797 -walk "x,z;..." -seconds S -step s -out <folder>`; loads the map, joins with dev-bot token n over plain UDP, sends character "Kntest<n>" in the database's first lineage's default look (`RecipeEdit.Default`; the test-lane server makes the character, 43.1), waits for its own body (`LocalPlayer`), clicks the walk points through `BodyClicks` (the view-mode mouse path), logs through `KnTrace`; exit 0 ok, 2 bad arguments/setup, 3 never admitted (the expected result of a second login), 4 dropped; refuses a real-lane port.
- **Server side:** `InstallServer` sets `ServerRoster.Database` from Player.prefab's `PlayerIdentity` (the roster found its look rules only through a NetworkManager) and, under `KN_TEST_LANE`, `PlayerClickLog.Sink = KnTrace.Write`. Click lines now carry the owner's NetworkId (`[KnClick] client|server owner=N seq=S tick=T`): every client counts seq from 1.
- **`Networking/Entities/Scripts~/run_b3_linux.sh`** (WSL): checks the test lane (7797/7798, save service on 7802, never a real port), copies both builds to `~/kn-b3`, writes the server's settings (test save service + key file path, devBots, plainTransport), starts the server, waits for client A (Editor) for `KN_WAIT_FOR_A` s, then bot 1 (walks, `kill -9` mid-walk), bot 3 (bot 1's account again: want exit 3), bot 2 (late joiner, want exit 0), `POST /stop` (loopback, X-KN-Key from the key file, never echoed), and writes `b3-report.md` (exits, results, click pairs by owner+seq, the server's leave/save lines, exception count). `KN_WALK` must be set to ground points near the spawn of the map used.
- **`compile_check.py --defines "A;B"`** (new, additive): compiles with a build's extra defines, so the test-lane branches are checked without a 40-minute build: plain 74/74, `KN_TEST_LANE;KN_DEV_BOTS` 74/74, `--server` + `KN_ENTITIES_SERVER;KN_TEST_LANE;KN_DEV_BOTS` 33/33.
- **Not yet:** a run. Next: the two Linux builds (>= 19 GB free; Master clears room), then the WSL run with client A in the Editor and Master's OK. Unknowns to watch: the bot's default look passing the server's recipe validator; `NetworkManager` in Zone01 on a headless client (NGO stays idle without a join target); the test save service reachable from WSL (NAT: Windows host IP, not 127.0.0.1).

## 46. The Linux test builds and RAM (29-30 Oct 2026)
- Server build, all maps: stopped twice by the RAM watchdog (tree 21.1 GB compiling shaders, then 23.1 GB in the final stage with 0.3 GB free). Fixes: `KnLinuxTestShaderStrip` (every shader stripped, only while `KnLinuxTestBuild.IsBuilding` and the target is Linux) and one map in the build (`KnLinuxTestBuild.MapScenePath`, Zone01_StarterTown); the watchdog also kills at once below 1 GB free.
- Results (30 Oct): server 3.6 min, tree peak 19.7 GB; client IL2CPP (`BuildClientIl2cpp`, `Builds/KnGameClientLinuxIl2cpp`) 11.6 min, 18.0 GB; client Mono (after the owner added the Linux Mono player module) 6.1 min, 22.0 GB. Both clients kept; `run_b3_linux.sh` picks one with `KN_CLIENT_BUILD`.
- **At the next main sync, drop `KnLinuxTestShaderStrip`** in favour of main's `ServerShaderStrip` (PR 166, merged 29 Oct; this clone has not taken main since). Never two strip classes at cutover: check main's strip also covers the headless Linux client build, or give it that case.

## 47. B3 run 1 (30 Oct 2026, WSL as root, test lane, the WSL box's kn-save on krytical_nova_test): FAILED, nobody entered
- Checks first (read only): kn-save runs `SaveService --test --urls=http://127.0.0.1:7802`; `--test` appends `_test` to the configured database (Program.cs:43); WSL Postgres holds only `postgres` and `krytical_nova_test`. The key file is `root:kn 640`, so the run used root (owner pick; no change to WSL users). kn-save started for the run only (not enabled) and stopped after; `/root/kn-b3` deleted; logs kept in the clone's `Logs/b3-run-20261030-074229` (git-ignored).
- Server up (UDP 7797, page 7798, saving through the save service, SIGTERM handler), `POST /stop` 200, exit 0. Every bot: "This server is not ready for players yet" because `PlayerGhostPrefab` never appeared: **the ghost subscene was not in the build** (`StreamingAssets/EntityScenes` held only `scene_info.bin`). A player build bakes only the subscenes a listed scene points to with a SubScene component; ours was referenced only from `PlayerWorldAssets`. The Editor bakes on demand, so B1/B2 passed there. Every built player would have failed the same way at cutover.
- Also: one real server exception, UI Toolkit making a font material with a null shader (the strip had removed the text shaders); one `ThreadAbortException` from the status page as the server quit; the other "exceptions" counted were stack frames named `...AndLogException`. Bot 3 shared bot 1's number, so it wrote over bot 1's result and log. Process exit codes read 1 while the result files said 3.
- **Fixes (owner pick, option 1):** `PlayerGhostAssetsBuilder` also writes `Scenes/PlayerGhostBake.unity` (a SubScene pointing at the ghost subscene, auto load off); `KnLinuxTestBuild` lists it second (never loaded) and fails the build when `EntityScenes/<guid>.entityheader` is missing; the strip keeps TextCore / TextMeshPro / UIElements shaders; `KnTestClient -label` names its files; the script reads results from the files and counts only real exception headers; every 2 s a bot logs `[KnSight] others=N owner=I look=V body=yes|no pos=x,z` for every other player's ghost (the by-log proof of "sees the other's body and look"). Test: `PlayerGhostAssetsTests.TheBakeScene_PointsAtTheGhostSubScene_WithoutAutoLoad`. Client A (the Editor, by eye) moves to the owner's feel checkpoint with an Editor-only join menu built then.
- Next (Unity slot): compile check (plain, test-lane defines, server), Build Player Ghost Assets (makes the bake scene), EditMode asset tests, server rebuild, Mono client rebuild, B3 run 2 the same way.
- **Build memory, 30 Oct:** with the bake scene in, the server build was stopped twice at Bee's last "Player" step (22.2 GB peak even on 8 of 16 cores). The heavy job was Burst AOT for Linux (its output is 185 MB). Owner pick: `KnLinuxTestBuild` writes `ProjectSettings/BurstAotSettings_StandaloneLinux64.json` with Burst off for the build only and removes it afterwards, even on failure; the build log says whether `lib_burst_generated.so` is absent. Result: server 1.7 min, peak 15.4 GB, min free 4.3 GB; Mono client 4.3 min, 16.1 GB, 5.6 GB; both carry `EntityScenes/2a032332....entityheader`. The test players run Burst jobs as plain C# (fine for three bots; never for a release build). Build wrapper: a Windows job object that kills the whole build when the wrapper ends, half the cores, gate 19 GB.

## 48. B3 run 2 (30 Oct 2026): Burst off crashes the client; finding, and the IL2CPP + Burst route
- Run 20261030-091112 (logs in the clone's `Logs/`), Mono players with Burst AOT off: the server came up and stopped cleanly, the ghost subscene was found (no "not ready"). **All three bots crashed (SIGSEGV) about 2 s after their client world was made, before connecting:** `__libc_free` <- `Memory.Unmanaged.Free` <- `BufferHeader.SetCapacity` <- `DynamicBuffer<SnapshotDynamicDataBuffer>.ResizeUninitialized` <- Netcode `GhostSpawnSystem.OnUpdate`, called through its non-Burst fallback (`BurstDirectCall.Invoke`).
- **Finding:** Netcode for Entities' client path without Burst crashes in a player build here. The Editor always runs Burst, so it never showed. Never ship or test a player with Burst AOT off; `KN_LINUX_TEST_BURST_OFF=1` stays as an explicit option only. Not yet proven in the Editor with `-burst-disable-compilation` (would tell a Unity bug from ours).
- The UI Toolkit font exception on the server stayed with the text shaders kept, so it does not come from the strip.
- **Owner pick:** Burst on, IL2CPP for both test players (`BuildServerIl2cpp` -> `Builds/KnGameServerLinuxIl2cpp`, `BuildClientIl2cpp`); the IL2CPP client with Burst fitted at 18 GB on 29 Oct. `run_b3_linux.sh` takes `KN_SERVER_BUILD` / `KN_CLIENT_BUILD`.
- **Open (30 Oct, about 11:30): Burst AOT is missing from every Linux test build since 09:00.** Every Linux build up to 07:21 ran Burst's compiler (`bcl.dll`, 93 s on the IL2CPP client; library in `Library/BurstCache/Linux-Intel`). Since then the step is registered ("Running Burst Compiler") but returns silently: no bcl run, no library, no error, with Burst on. Not the cause (each tried with one IL2CPP client build): the Windows job object, half cores / `-job-worker-count`, stale Bee player plans and the Linux player-script cache (moved to a scratchpad backup), Burst ON written outright in `BurstAotSettings_StandaloneLinux64.json` (kept: the build writes it and takes it away), `UNITY_BURST_DEBUG=1` (shows only the Editor side), and the bake scene left out. No Burst entries in the Unity registry keys, no `-burst` flag. The IL2CPP server never had Burst (Unity may leave it out of Dedicated Server builds). B3 run 3 waits on this: without Burst the bots crash as in run 2.
- **PARKED (owner, 30 Oct 2026 afternoon): the built-player 2-client gate (B3).** Burst AOT still does not run for Linux players after every cause tried, including a build from the Editor window and the Editor subtarget set to Player (Master found it left on Server by the killed server builds; restored). Write-up for Unity: `Reference/Unity-Issue-Burst-Linux-AOT.md`. The code for B3 stays (bake scene, test-lane bots, `[KnSight]`, run script, IL2CPP entries, job-object build wrapper in the session scratchpad); work moves on to the next M3 steps, tested in the Editor.

## 49. M3a leftovers, step 1 (30 Oct 2026): the walk's numbers from the game's data, and Run's Flux
- **Found:** `ServerBodyAdmission.MoveStatsOf` was never set, so every ghost walked at the ghost prefab's built-in speeds, not the character's.
- **Settings:** `PlayerMoveFromSkills.Bake(MovementSettings)` -> `PlayerMoveSettings` (acceleration, arrival, stopping distance), installed in both worlds from the player prefab and again from the first body (the scene may load after the world). No numbers in code; the click range has no setting yet (0 = no limit).
- **Speeds:** `PlayerMovementSkills.SpeedFor(running)` (Run rank, mount, Move Speed stat, GM speed; `CurrentSpeed` uses it) fills `MoveStats` at admission; `PlayerMoveStatsRefreshSystem` (server) rewrites it when the body's speeds change, so a GM speed, a mount or a new rank reaches the owner's prediction.
- **Flux:** `IRunCost` (Player.Entities) on `PlayerServerPlanSystem`: a running click is checked before it is planned and paid once when it starts; `BodyRunCost` charges the body's Run rank (`CanPayForCommand` / `TryPayForCommand`), the Netcode for GameObjects rule. No Flux = refused, the owner is taken back; the message to the player waits for the chat port (M8).
- **Planner:** set from the first body's NavMeshAgent on the server (`PlayerAdmission.ConfigurePlanner`) and the local body on the client; checked on a real map with step 2.
- **Evidence:** compile 74/74, test-lane 74/74, server 33/33; dependency map 0 uphill / 0 two-way; EditMode 1486 passed, 5 failed (the known gear/icon content failures), 10 skipped; new: `ARunningClick_ThePlayerCannotPayFor_IsRefused_AndAPaidOneIsChargedOnce`, `ASpeedChangeMidWalk_ReachesTheOwner_AndBothWorldsEndEqual`, `PlayerMoveFromSkillsTests` (5). Not covered by a test: `SpeedFor` itself (needs a full player body).

## 50. M3a step 2: "Play DOTS here" for the owner's feel checkpoint (30 Oct 2026; EditMode green, by-eye run pending)
- Menu **Krytical Nova ▸ Online ▸ Entities ▸ Play DOTS here** (`Persistence/Entities/Runtime/KnPlayHere.cs`, Editor only): enters Play; before the scene loads `ServerSave.Store` becomes a `MemorySaveStore` for the whole session (nothing to disk: no profile, no save service, no database); after it loads `KnBootstrap.StartPlayHere(7797, ...)` makes a server world that listens on **loopback only** and a client world that joins it in plain UDP, both with the game's hooks (`StartHost` stays the bare session of decision D5). A real-server port is refused.
- The player enters unsaved as "Tester" in the first lineage's default look (no account, no character id: `PlayerAdmission` keeps such a player unsaved). This also closes B3's client A gap for a by-eye check in the Editor.
- Tests: `KnPlayHereTests` (every real port refused and nothing started; the port is the test lane; the tester's look decodes).
- To check by eye: open the clone's Editor, open Zone01_StarterTown, run the menu, click the ground (walk) and with Run selected (run); look for the `[KnPlayHere]` lines and no errors.
- Evidence: compile 74/74, test-lane 74/74, server 33/33; dependency map 0 uphill / 0 two-way; EditMode 1489 passed, 5 failed (the known gear/icon content failures), 10 skipped; the 3 `KnPlayHereTests` pass. The by-eye run in the Editor window waits for the owner's word.
- **Scripted run (30 Oct, after Master's Editor froze on a hidden dialog after opening the scene by script):** the menu opens `Zone01_StarterTown` itself (`EditorSceneManager.OpenScene`, no dialog; with unsaved changes in an open scene it stops with an error). `KN_PLAYHERE_WALK="x,z;x,z"` makes the same ground clicks (each point walking, then each running), logs every arrival, the server and body positions and the Flux as `[KnPlayHere]` lines, and stops Play; `KnPlayHere.Batch` (batch Editor, `-executeMethod`, no `-quit`) quits with 0 when all arrived without an error line, else 1, and after 300 s at most. The tester gets Run and Jump at their top ranks (`GmSetMaxRanks`) so running can be felt. Tests: `TheScriptedWalk_ReadsMetres_AndRefusesAnythingElse`, `TheMapToPlay_Exists`.
- **Evidence, scripted run 2 (30 Oct 2026, batch Editor, `KnPlayHere.Batch`, `KN_PLAYHERE_WALK="0,-12;3,-10;0,-7;-3,-10"`): PASSED.** 3 min, job peak 16.4 GB, min free 3.5 GB, exit 0. Saves in memory; server + client on loopback 7797; the tester entered after 9.7 s (Run rank 20, Flux 250). Walks: 4 of 4 arrived, the server 0.10-0.17 m short of the point (the 0.2 m stopping distance now baked from `MovementSettings`), the body on the server's spot (0.4 m behind on the first walk while it caught up). Runs: 4 of 4 arrived exactly, 0.6-0.7 s each, Flux 250 -> 230 (5 per destination, the Run rank's price). 0 error lines.
- Run 1 had stopped writing its log after the tester entered: its wrapper ran inside a tool call that timed out. The wrapper now runs as its own hidden process with a transcript (session scratchpad `run_detached.ps1`).
- **Owner's feel test 30 Oct (GUI Editor): three bugs, fixed in code (not yet compiled or rerun).** (1) Safety: the scene's `NetworkConnectionManager` auto-started a Netcode for GameObjects local host in Editor Play (`autoStartInEditor`), bound UDP 7777 and spawned the owner's Editor profile character "[GM] Tolan"; the batch run never showed it (that auto-start skips batch mode). Fix: `Networking/Runtime/EntityNetcodeSession.Owns`, set by `KnPlayHere` before the scene loads; `NetworkConnectionManager.Start` then neither hosts nor joins, and `OfflinePlayerHandoff` keeps the scene's offline avatar hidden (its `Update` would otherwise bring it back, as no NGO session runs). (2) The clicks and the camera went to that NGO player; with NGO off they go to the entity body's view mode (`BodyClicks`) and its camera binding. (3) The Tester stood half in the ground: the server's first spawn spot is picked before any body exists, while the server planner's base offset was still 0. Fix: `PlayerWorldsSetup.ConfigurePlanner` from the prefab's NavMeshAgent at install. The scripted run now logs whether Netcode for GameObjects runs (error if it does) and which body is the local player.
- Profile check after the feel test: no file under the local profile (`AppData/LocalLow/Ascension Studios/Krytical Nova`) changed after noon except Unity's analytics and the test runner's `TestResults.xml`; no save file was written (saves were in memory). PlayerPrefs (registry) not checked.

## 51. Movement must feel exactly like the NGO game: side by side (30 Oct 2026, owner's bar)
Owner, 30 Oct: the DOTS walk must feel exactly like the original game's (first-click response, turn-to-face, speed, acceleration,
stopping, re-click, smoothness, camera), built differently underneath, server still authoritative. GUI retry 1 found clicks lost:
fixed in 89af050fc (a click followed by another frame of its tick lost its seq/goal; tests at 3 frames a tick and through
`BodyClicks.Ground`). NGO numbers below are the runtime ones (`Movement.asset`, `Run.asset`, Player.prefab).

| Behaviour | NGO game | DOTS now | Test |
|---|---|---|---|
| [x] Click input | press frame only; not over UI or while orbiting | same reader (`PointAndClickPlayer` view mode -> `BodyClicks.Ground`) | `BodyClickPathTests` |
| [x] Every click is taken | one RPC per press | fixed 89af050fc (was ~1 in 3 at 84 fps) | `AtThreeFramesATick_...`, `OneGroundClick_...` |
| [x] First visible move | press frame (`LeadOwnWalk`, local agent) | predicted on the click's own tick; drawn ahead to the frame (`PoseAhead`) | `ANewClickMidWalk_IsTakenOnTheNextTick_...` |
| [x] Start speed | full speed at once (`SnapVelocityToPath`) | full speed on the first tick (was a 0.2 s run-up) | `ANewWalk_StartsAtFullSpeed_OnItsFirstTick` |
| [x] Acceleration | max(25 m/s2, top speed x 8): 40 walking | max(AccelMmps2, top speed x AccelPerSpeed) (was current speed x 8) | `Acceleration_IsTheTopSpeedTimesAccelerationPerSpeed` |
| [x] Top speeds | walk 5 m/s; Run ranks 10..28 m/s; x Move Speed 0.5-1.5 | `MoveStats` from `PlayerMovementSkills.SpeedFor` (same numbers) | `PlayerMoveFromSkillsTests` |
| [x] Turn | RotateTowards velocity at turnSpeed 1080 deg/s (asset; code default 540), turns while moving, no turn in place | `TurnCdegPerSecond` baked from `turnSpeed` (1080 at runtime), turns while walking | `WithATurnSpeed_...` |
| [x] Arrival slowdown | radius clamp(speed x 0.3, 1.5, 8) m; factor lerp(0.55, 1, sqrt(rest/radius)) | same formula, integers | `SpeedRisesToWalkSpeed_ThenEasesInTheArrivalZone` |
| [x] Stop | within stoppingDistance 0.2 m + one step: `PlaceOn(destination)`, velocity 0 | stops ON the point (was 0.2 m short) | `WithAStoppingDistance_TheWalkStopsOnThePoint` |
| [x] Click on the spot (<= 0.2 m) | nothing starts | nothing starts, no hop, no turn | `AClickInsideTheStoppingDistance_StartsNothing` |
| [x] Re-click mid-walk | replans from the shown position, full speed in the new direction, no skid | replans from the predicted pose, full speed at once | `ANewClickMidWalk_StartsFromTheCurrentPose_AndKeepsTheSpeed` |
| [x] Rate limit | server: burst 10, 10 a second (Move) | same budget, keyed by PlayerKey; refusals logged `[KnClick] refused why=budget` | `AClickOverTheRateLimit_...` |
| [x] Marker | at the raw hit point, ground-snapped, Feel MoveCommand | same code (view mode) | (presentation, by eye) |
| [x] Animation speed | own walk: `agent.velocity.magnitude`; run clip at >= 7 m/s, hysteresis 2, 0.15 s; playback speed/clip speed 0.8-1.6 | own body: the predicted ghost's speed (`PlayerAnimancerMotor.ExternalSpeed`, set in `PlayerWorldsSetup.DriveAnimationFromGhost`), same clip rules | `TheOwnBodysAnimation_ReadsTheGhostsSpeed_NotAMeasurement` |
| [x] Root motion | off | off (same prefab) | - |
| [ ] Camera | OrbitalFollow on "Camera Target", world-space binding, damping 0, no recentering, radius 9 | same scene camera on the view body | by eye (owner) |
| [ ] Server disagreement | rare: 0.2 s smoothstep ease onto the server after a hand-over (apart > 3 m + speed x (RTT+0.3), or settled) | prediction rollback; equal on both sides, so no correction in normal play | trace tests (0 mismatches); by eye with lag |
| [ ] Stall / arriving short | server: 0.75 s with < 0.1 m -> replan once, then stop | not built (fixed-path walk cannot stall) | later (M3c, with obstacles) |

Open after this: the camera and
correction rows wait for the owner's GUI retry 2.
Speeds checked from the game's own assets (`RunSpeedSourceTests`, 30 Oct): Run rank 1 = 10.00 m/s, rank 10 = 18.53, rank 20 (top)
= 28.00; walk 5.00; mounted top 34.00 (walk 9.00, `MountSettings.asset`); a 20 % speed Stance on top of Run 28 = 33.60 (cap x1.5).
The ghost's `MoveStats` are `PlayerMovementSkills.SpeedFor` itself, so whatever the Stance, Boots, a mount or a GM add reaches it.

GUI retry 2 (30 Oct): first click, turn, stop and re-click all right; a long walk seen side-on juddered. Cause: the bodies were placed
in the PresentationSystemGroup, which Entities runs after every LateUpdate, so the Cinemachine brain (LateUpdate) aimed at last
frame's body. Fixed: `PlayerBodiesBeforeCameraGroup` (end of the simulation, Update phase) holds the body spawn and follow, as the NGO
game placed its walk before the camera (`PlayerMoveAnticipation`, order -200). Test `TheBodies_ArePlacedAtTheEndOfTheSimulation_...`.
GUI retry 3 (30 Oct): no judder side-on, but the body slid without turning: something between the end of the simulation and
LateUpdate (likely the animation pass) put the old root rotation back. `PlayerPresentationState` (order -200, after the animation,
before the camera) now puts the follow's pose back in LateUpdate and counts it; the Play DOTS here fps line reports the count.
Tests `TheOwnersBody_FacesTheWayItWalks_AndTurnsForANewClick`, `APoseChangedAfterTheFollow_IsPutBackBeforeTheCamera`.

GUI runs 1-8 (30-31 Oct, real Editor window, `run_gui.ps1`, frames + `[KnYaw]`): the body slid without turning because
`HitReactor` (GameFeel) took a ghost body's ROOT as its visual: `WorldTargets.SyncsTransform` only knew NGO's NetworkTransform,
so on an entity-netcode body it answered "nothing else moves this", and the reactor wrote its first rotation and scale back every
LateUpdate (order 0). Hidden until the follow moved before the camera (6b07f9e1f). Fixed: `SyncsTransform` is also true for a
body tagged ServerBody/ClientView (Core). Proof run 8: 8 of 8 walks and runs face the way (body = model = server yaw at every
LateUpdate order); `PlayerPresentationState` now only counts a second writer (order 290), never hides it. Nameplates, damage
numbers, pet bubbles, loot labels and NPC prompts are placed after the Cinemachine brain (order 200 > 100).

Walk acceptance list (owner, 31 Oct; each ticked only with its proof):
- [x] first click walks at once (89af050fc, `OneGroundClick_...`, GUI retry 2)
- [x] turns while walking, 1080 deg/s, left/right/behind (GUI run 8: 8 of 8)
- [x] stops on the marker (0a7b80312; GUI run 8: the server 0.00 m off every point)
- [x] speeds from the game's assets (walk 5, Run 10..28, mount 34; `RunSpeedSourceTests`)
- [x] walking side-on smooth: head anchor 0-2.2 px per frame at 64 fps (GUI run 8, capture off)
- [x] running side-on smooth (31 Oct, GUI run 3k): at render time the camera's offset from the body changed 0 mm over 266 moving
  frames (walk and run 0 px). The judder numbers of runs 2-3j below were measuring artifacts: the probes were nested classes, and
  Unity keeps the execution order per script file, so their order did not hold. Pass/fail is now the render-time check
  (`RenderWatch`, limit 10 mm). Earlier reading, kept: 3.8-6.2 px per frame at 28 m/s on 2 of 4 runs (limit 3). Cause named (GUI run cam-3, 31 Oct): the
  steps come only on frames where the field of view changes 40 -> 44 deg, the GameFeel "Run Start" kick (+4 deg over 0.4 s, played
  by `PlayerAnimancerMotor` -> `GameFeelPresenter.RunStarted` for the own body, from a standstill), the same code and asset as the
  NGO game: designed, not judder. With the view steady the head step is 0.0 px at full run speed; the camera's position and turn
  follow exactly, resolution scale 1, the anchor is body + 2 m (no bone). The scripted check now holds the zoom out of the step
  (last frame's projection, this frame's view); tick after its GUI run.
- [x] no camera jump on the first click after spawn (was 344 px once). Two causes: the probe measured the camera's first move onto the
  body that had just entered (now it waits 2 s and logs that apart), and the camera really took the body only after the first drawn
  frame (the DOTS body is made already local, so no event came). `PointAndClickPlayer.TakeCameraNow` from `PlayerWorldsSetup`'s
  BodyMade: GUI run 4 (31 Oct), the first drawn frame with the body is already 8.23 m from the head (its settled distance); with the
  fix off (runs 2, 3) it was 10.99 m, then a 3.91 m snap. NGO's own spawn cut is the same.
- [x] nameplate/pet follow the body: pet <= 1.8 px; cards placed after the camera (`NamecardOrderTests`)
- [ ] scripted arrival check counts short moves (the server is on the point; the script misses them). Cause (cam-3): a judder
  flag also set "not arrived"; both misses had the server 0.00 m off. Arrival, facing and smoothness are now counted apart.
- [ ] owner's by-hand retest: PENDING (owner, 31 Oct: "I can't test anytime soon", work goes on in plan order)

## 52. Known issues and required steps found in the walk work (31 Oct 2026)

Known issue: **the GUI Editor can freeze at start on NVIDIA's DLSS updater.** The Editor's GPU set-up (the `com.unity.modules.nvidia`
upscaler module) starts twelve `nvngx_update.exe -api update -feature dlss|dlssg|dlssd -cmsid 0..3 -bootstrap`. On 31 Oct three GUI
runs froze right after the project loaded: the Editor's main thread waited at 0 CPU (blank window, no more log lines) while the
updaters hung at 0 CPU from the second the freeze began (02:37:28; they had written NVIDIA's config at 02:37:31). Reproduced
without Unity: `-cmsid 2 -feature dlss` hangs even alone, cmsid 0 and 1 finish in about 1 s, and the hung one holds no network
connection (a local NVIDIA lock, not the download). The hang also happens outside any job object (the cmsid 2 test above), so the
wrapper's job is not the cause; nor is our code. Batch-mode runs (tests, builds) are not affected. Workaround in the DOTS GUI run wrapper: an
`nvngx_update` started by that run's own Editor that is still running after 20 s is stopped and logged; the Editor then goes on
and DLSS is only not refreshed for that run. A by-hand Editor that hangs at start the same way can be freed by ending its
`nvngx_update.exe` children in Task Manager.

- [x] **The join's door waits for the world (Master, 31 Oct).** The DOTS client holds `JoinFlow` (`HoldUntilWorldReady`; `Joining`
  stays true, so the menus' "Joining <server>" loading screen holds with no UI change) until `WorldEntryDoor` says the world may be
  shown: the local body is made, the orbit camera tracks it (`PointAndClickPlayer.IsCameraOnThisBody`), and 1 s of frames are each
  under 50 ms. On a slow PC the door opens anyway 10 s after the body and the camera are ready, logged `[KnJoin] ... the cap`.
  Proof: EditMode `WorldEntryDoorTests` (steady second, a 779 ms hitch starts it again, the slow-PC cap, JoinFlow unchanged without a
  hold), and GUI run door-1 (31 Oct): "steady frames after 2.5 s with the body and the camera ready (worst frame meanwhile 647 ms)".
  The first jump after it started on the owner's tick although its click came 7 ticks late (116 = 116, lift 0.203 s).
  Still to come: the menus' Enter must start the DOTS join (the plan's front-end move); the door is ready for it.

Required step **before the first 2-client test** of the DOTS game (owner, via Master, 31 Oct): **one answer to "is this body
mine" for entity-netcode bodies.** 124 places decide "mine" with `!IsSpawned || IsOwner` (run take-off view kick, messages,
death, pets, mounts, skills, combat presentation, recall, identity...). An entity-netcode body is never spawned by NGO, so the
test is true for EVERY body in the process: another player's run would zoom my camera, their messages would show on my screen,
their death would play my death view. The answer belongs in Core (the body's `BodyRoleTag` and `LocalPlayer`, which the Player
system fills) so every system can ask it without knowing the netcode.
- [ ] Acceptance: with two clients, a second player's run take-off, messages, death and pet never act on my client (no view
  kick, no message, no death view, no pet panel change); my own still do. Proven in a 2-client run with logs, plus an EditMode
  test per kind.

Prep for this step (mini-boss 6, 31 Oct; no code yet). **The answer exists in Core:** `NetRole.IsLocalOwner(component)`. It is
true offline, `IsOwner` for an NGO body, and `BodyRoleTag.IsLocal` for an entity-netcode body (through `BodyRoleNetRoleAdapter`).
`NetRole.IsNetworked` and `NetRole.IsOwner` answer the same way. Every system may use Core, so no new type and no new reference.
Acceptance list (each ticked with its proof):
- [ ] one rule for "mine", written in `NetRole`'s summary: offline = mine; NGO = `IsOwner`; entity netcode = the local ClientView
  body; a ServerBody is nobody's on this machine (no view, no message, no camera)
- [ ] the 33 exact `!IsSpawned || IsOwner` checks become `NetRole.IsLocalOwner(this)` (list below). The 50 `IsSpawned && IsOwner` and
  the 18 `IsSpawned && !IsOwner` are read one by one: "a networked body I own" -> `NetRole.IsNetworked(this) && NetRole.IsOwner(this)`,
  "a remote body" -> `NetRole.IsNetworked(this) && !NetRole.IsOwner(this)`. A check that guards an NGO RPC or a NetworkVariable stays
  on NGO's own flags (it only runs on NGO bodies); each one left is listed with why.
- [ ] my run take-off view kick plays; another player's never does (`PlayerAnimancerMotor` -> `GameFeelPresenter.RunStarted`)
- [ ] my command messages and refusals show; another player's never do (`PointAndClickPlayer.CommandMessage` and the systems' messages)
- [ ] my death plays the death view; another player's death only plays on their body (`PlayerDeathMotor`)
- [ ] my pet's panel follows my pet; another player's pet never changes it (`PlayerPet`), and the same for mounts (`PlayerMounts`)
- [ ] the NGO game is unchanged: `!IsSpawned || IsOwner` and `NetRole.IsLocalOwner` agree on every NGO and offline body (an
  EditMode test per kind, both answers)
- [ ] a 2-client run in the Editor test world (two ClientView bodies, one local), logged per kind; at most 2 runs
- [ ] `netrole_audit.py` (Tools/Editor/Architecture) passes, and its baseline only shrinks

The 33 exact sites, by system (line numbers on 31 Oct):
- Player (13): PlayerAnimancerMotor 353, 376 (run kick); PlayerDeathMotor 326, 389, 655; PlayerIdentity 303; PlayerVaultMotor 646, 694;
  PlayerWallRunMotor 1045, 1053, 1057, 1080, 1084; PlayerWaterstrideMotor 497
- Combat (6): PlayerCadence 160; PlayerCombatState 224; PlayerMeleeCombat 880, 1002; PlayerSkillAttacks 67; PlayerSkillQueue 62
- Restyle (3): PlayerRestyle 141; PlayerWeaveLibrary 204; PlayerWeaves 389
- Progression (2): PlayerCharacterProgression 122; PlayerExperienceBoosts 151
- Autopilot (2): PlayerAutobuff 112; PlayerAutopilot 210
- one each: Crafting PlayerCrafting 146; Economy PlayerStorage 354; Housing PlayerRecall 43; Items PlayerLoot 205; Mounts PlayerMounts 325;
  Pets PlayerPet 537
Order: Core rule and the agreement tests first; then the four acceptance kinds (Player, Pets/Mounts, messages); then the rest by
system, one commit per system group; then the 2-client run.

Progress (31 Oct). `NetRole.IsLocalOwner` is not a drop-in: the adapter's `Of()` answers Offline for components that are not
`IRoleAwareBody`. The one answer is `BodyRoleTag.IsMine(part, ngoMine)` in Core: the tag for an entity-netcode body, NGO's own answer
for every other body.
- [x] part 1 (aaf0af23d): the helper, `BodyIsMineTests`, and the Player group's 13 sites (run kick, death view and respawn,
  vault / Wallstride / Waterstride feel, identity)
- [x] part 2: the 13 presentation sites outside Player. Messages: Autobuff, Autopilot, Crafting, Storage, Loot, Experience Boosts,
  Restyle, Weave Library, Weaves. Feel: level-up, Recall charge, pet and mount evolve.
- [x] part 3, Combat: 5 of the 6 ask `IsMine`. They are local presentation or input: `PlayerCadence.IsMine` (beat key, combo UI),
  `PlayerSkillQueue.IsMine` (own input), both `IsLocalPlayerPresentation` (swing and hit VFX, feel), and `PlayerCombatState`'s
  dummy selection (read off the own body, else NGO's object path). `PlayerMeleeCombat.AttackerIsHere` stays: it is NGO routing,
  and its "no" sends `DamageFeedbackRpc`, which must never run on a body NGO never spawned. The entity-netcode damage feedback comes
  with the combat milestone.
- [x] the 50 `IsSpawned && IsOwner` and 18 `IsSpawned && !IsOwner`, read one by one (31 Oct): none needs a change.
  - 44 of the 50 are the owner's NGO RPC sends, named on the same line. The other 6 are NGO routing too: `PlayerCombatState`
    192 `SendSelection`, `PlayerKiosk` 154 (the stand's NGO object), `PlayerMoveAnticipation` 147 (NGO-only, stripped on DOTS bodies),
    `PlayerCombatSkills` 293 (its NetworkVariable), `PlayerCrew` 133 `FoundRpc`, and `PointAndClickPlayer`'s `IPlayerBody.IsLocal`
    (an NGO body's own answer; a DOTS body's `IPlayerBody` is `GhostPlayerBody`).
  - 7 of the 18 are `TellRpc` to a remote NGO owner. On a DOTS body they are skipped, and the next line's `IsMine` shows nothing on a
    server body; the DOTS message route comes with M8's chat port.
  - The other 11 are "only the owner acts" guards in Autobuff, Autopilot, `PlayerLoadouts` and `PointAndClickPlayer`. `PlayerRoleTable`
    covers them: a remote view strips Movement, Automation and the Rules, a server body strips Automation and disables Movement.
- [x] the 2-client run in the Editor test world (31 Oct, `BodyIsMineWorldTests`, 0 and 100 ms). A server and two clients, a body per
  ghost tagged as `ClientBodyFactory` tags it. On each client only its own player's body is mine; NGO's old answer said yes for all
  four. Each kind asks this one answer (`BodyRoleTag.IsMine(this, ...)` at every site above). Of the kinds, only the run kick's
  component (`PlayerAnimancerMotor`, Presentation) is on another player's view at all: their death motor, messages and pet are
  stripped from a remote view by `PlayerRoleTable`, and on a server body `IsMine` is false.
  Open: a by-eye 2-player check needs a second real client. In the Editor that is a thin client with a dev-bot character, which needs
  the test lane's bot characters (plan 43.1, `KN_TEST_LANE`), not defined in the Editor. Also, with the entity netcode my own
  messages and level-up come from my server body, which `IsMine` now keeps silent. They need the client route (M8 chat port); in
  Play DOTS here they no longer show.

## 53. M3b jump: acceptance list (owner's bar via Master, 31 Oct 2026; design `netcode-for-entities-designs/m3b-design.md`)

The DOTS jump must feel exactly like the NGO jump, server still deciding. NGO numbers are the runtime ones: `JumpMotion.asset`
(gravity 14, apex clearance 1.4 m, anticipation 0.12 s, landing cooldown 0.25 s, height-by-distance exponent 1.5, minimum rise
0.8 m, landing sample radius 0.3 m), `Jump.asset` (rank 1: 20 m distance and 20 m rise, top rank 67.5 m; 5 Flux every rank),
`Warp_Player_DoubleJump.asset` (take-off 0.183 s, apex 0.767 s, touchdown 1.407 s of the clip; at 30 Hz the first ticks at or after them are 6, 24 and 43: the ghost lands up to one tick, 33 ms, after the clip), and
`PlayerJumpMotor` (owner crouch on the press, held at most 0.25 s for the server). Each item is ticked only with its proof.

- [x] S7 spike measured (31 Oct, runs 5-9, fixed frame times; `S7WarpVsGhostSpikeTests`, PlayMode category S7). KINEMATION's warp
  is not a function of time: in flight it is 16.2 / 8.1 / 4.0 cm off the exact arc at 30 / 60 / 120 frames a second (it changes phase
  only on the first frame after the phase time), it lands 4.4 / 4.0 / 3.9 cm off its own target (NGO then put the agent back on the
  NavMesh), and before take-off it lowers the root by the clip's crouch (19.5-20.8 cm). The old bar ("within 2 cm of the warp")
  cannot be met at any frame rate.
- **Owner's bar (31 Oct, via Master), replacing it.** Their words: frame-rate dependence "is probably a bad design choice since some
  players would have an advantage", and the crouch "was based on the server and timing before so we had to add that crouch
  because there was delay between player jumping and the server". So: the DOTS jump flies the exact arc, the same at every frame
  rate, and is not judged against the warp in flight; the crouch stays only as a look of the drawn body (presentation, server
  unchanged), not a timing rule, and the owner checks it by hand.
  - [ ] the crouch look (S7 run 9 before the fade: 1.29 cm; to prove after it, and by the owner): the drawn body follows the clip's dip before take-off, then fades it out over the rise, no step at take-off
    (`JumpFlight.CrouchMm`, `PlayerMotionCurves.Crouch` baked from the clip); S7 run 9: within 1.29 cm of the warp's crouch
    (`TheCrouch_LowersTheDrawnBody_BeforeTakeoff`, `TheCrouchesEnd_FadesIntoTheRise_WithoutAStep`)
  - [ ] the arc is the same at 30 / 60 / 120 fps: the drawn body within 1 cm of the exact arc on every jump frame (scripted GUI
    run, `JumpArcErrorM`); EditMode: the body on the owner's arc every tick (`InAJump_TheOwnersBody_IsOnTheArc_EveryTick_...`)
- [ ] press to lift: the crouch shows on the press frame (predicted, owner Q1 a), take-off 0.183 s after it (tick 6), with no
  wait for the server; NGO held up to 0.25 s for its server, the DOTS jump never waits longer than that
  - [x] the owner predicts from the press (31 Oct, GUI runs predict-1/2): its own plan exists on the press frame (0 frames, 4 of 4
    jumps). Before, the owner's planner said "Learn Jump first.": the ranks were never sent to the client. Now the player ghost
    carries the learned ranks (`MoveRanks`) and the Flux (`OwnerFlux`), owner only, written by `PlayerMoveStatsRefreshSystem` and
    put on the own body by `PlayerMoveRanksMirrorSystem` (`PlayerMovementSkills.MirrorRanks`, `PlayerResources.MirrorChi`).
  - [x] the jump clip starts on the press frame (GUI run predict-2: 0 frames, 2 of 2). The owner's plan starts on the tick being
    predicted, and in the rest of the press's tick the follow's clock read -1 (run predict-1: 1-2 frames late). It reads 0 now, so
    the clip is held at its start for less than a tick.
  - [x] lift within 0.30 s on every jump. GUI run clicktick-2 (31 Oct) waited for settled frames after entering (1 s under 50 ms,
    logged). The first jump after entering then started on the same tick on both sides (server 168 = owner 168), lift 0.203 s; the
    second 258 = 258, 0.217 s. Clip and prediction on the press frame, landing 0.0 cm, refusals 3 of 3 again. Before: 0.213 / 0.210 /
    0.219 s, but 0.325 s on the first jump after entering (run predict-2). Cause
    proven ([KnClick]): the client gathered the click on tick 113, and the server planned it on tick 117 (the click arrived late), so
    the server's plan replaced the owner's and the arc started over. Step (A): the click carries its own tick (`PlayerInput.ClickTick`),
    and the server starts the jump on it when it is at most NGO's 0.25 s back (`JumpFlight.ClickStartTick`, `PlayerMotionCurves.
    ClickWindowMicros` from `PlayerJumpMotor.OwnTakeoffHoldMax`). It is clamped: never in the future, never before the previous jump's
    end or a server order's start, and a claim older than the window (a 1 s-old one in the tests) is not trusted.
    Documented fallback (Master, 31 Oct): a click that arrives more than 0.25 s late starts on the server's tick, the same as NGO (its
    owner held the crouch 0.25 s, then went on). The owner's arc then restarts once. GUI run clicktick-1 showed it only for a press in
    the Editor's warm-up (779 ms frames), which a player behind the loading screen never gets.
- [x] a jump the rank or the Flux does not allow is refused by the owner and the server with the same words, and nothing starts
  (GUI run predict-2, 3 of 3: no owner plan, the drawn body rose 0.0 cm): rank 0 "Learn Jump first.", rank 1 at 30 m "Too far: Jump
  rank reaches 20.0 m.", 4 of 5 Flux "Not enough Flux. Jump needs 5 Flux per destination." Both sides check in NGO's order (rank,
  Flux, then landing, limits and arc): `PointAndClickPlayer.ViewJumpRefusal`, `BodyJumpCost.CanPay`, and the owner's
  `BodyJumpPlanner(checkCost: true)`. The ranks and the Flux reach the owner in about 0.8 s.
- [ ] height: apex = max(1.4 m over the higher end, rise for the distance by the rank, exponent 1.5, at least 0.8 m); rise up to
  the rank's limit, down unlimited (owner 22 Oct); apex at the clip's apex time (tick 24)
- [x] (GUI runs 2-4, predict-1/2, 31 Oct: every jump landed 0.0 cm from the plan and 0.00 m from the click, after the stateless landing
  `JumpFlight.Places`) distance and landing: up to the rank's distance; lands within 2 cm of the server's planned point (a walkable or water spot
  within 0.3 m of the click; owner's bar 31 Oct); world tests land exactly (`AJump_LandsWhereTheServerPlanned_...`)
- [ ] air control as NGO: none. The path is fixed at take-off; a walk or jump click in the air is refused with NGO's message
  "Finish this jump before choosing another destination." (`PointAndClickPlayer` line 633; the design's "waits for the landing"
  was wrong); a second jump is refused until the 0.25 s landing cooldown is over
- [x] (GUI runs predict-1/2: "paid 1x", 5 Flux each; the refusals paid nothing) Flux: checked on the click, 5 paid once at take-off
  (owner Q2 a); a refusal or an abort before take-off is free
- [ ] the ghost owns the root: Motion Warping never moves the body; the clip plays in place with the ghost's curves; no second
  writer after the follow (the `[KnYaw]`-style per-order probe shows the root = the ghost pose)
- [ ] a forced rollback mid-air (100 ms latency) neither restarts the clip nor moves the body more than the follow's offset
- [ ] lands facing the way it jumped (landing yaw), the walk resumes from the landing point on the next click
- [ ] side-on and behind: the head anchor steps evenly through the arc (same zoom-held check as the walk), GUI run with frames
- [ ] (2-client day) an observer sees the same take-off tick, arc and landing
- [ ] owner's by-hand retest: PENDING (owner, 31 Oct: "I can't test anytime soon", work goes on in plan order)

Check note (31 Oct): the clone is a git worktree of the main folder, and `compile_check.py` takes Unity's project files from the
main worktree by default. A test assembly that exists only in the clone (the S7 PlayMode test) then gets a generic template, which
gave a false KINEMATION error. Run it for the clone with `--projects "<clone folder>"` (74/74 that way).

Tests it needs (design section 3):
- Pure: planner limits with the real `JumpMotionData` and ranks (distance, rise, down unlimited, too short, out of range);
  curve tables (end points exact, apex at tick 24, travel monotonic); tick conversion of the clip phases (6 / 24 / 43).
- Worlds (server + client, 0 and 100 ms, `PlayerRig`): the jump lands exactly where the server planned and owner and server
  agree on every tick of the trace ring; a refused jump is taken back; Flux paid once at take-off, not on a refusal or an abort
  before take-off; the cooldown refuses a second jump; a walk click in the air is refused (no queued walk); a late-arriving or lost
  jump click is still taken (the click-loss rule of 5d6fc04d6).
- Presentation: the clip starts at `now - StartTick` (a late snapshot fast-forwards, rollback never restarts it); no writer
  moves the root after the follow.
- Scripted run: `KN_PLAYHERE_JUMP="x,z;x,z"` jumps logged like the walk (press to crouch, take-off and landing ticks, landing
  error, Flux before and after, worst head step), quits 0 only when every jump passes; captured frames into a contact sheet.

Order (design section 4): S7 first; then the curve baker + `PlayerMotionCurves` and the planner lifted out of `PlayerJumpMotor`
into a shared class (the NGO motor keeps using it); then `MotionPlan`, `PlayerMotionSystem`, `PlayerJumpServerSystem`, owner
prediction and presentation; then the scripted run.

## 54. M4 combat: acceptance list (written before code, 31 Oct 2026; for the Master and the owner to confirm)

The DOTS combat must play exactly like the NGO combat, with the server deciding every hit, and no player advantage from frame rate
or ping beyond the lag compensation's window. NGO's own code is the reference: `PlayerMeleeCombat` (combo steps, hit windows),
`PlayerSkillAttacks` and `PlayerSkillQueue` (casts, charges, queued skills), `CombatCalculator` (the one formula), `CombatEntity` and
`CharacterBuffManager` (HP, Flux, buffs), `CombatHostility` (factions, duels, `gm.pvp`), and the presentation (`HitReactor`, damage
numbers, Cadence). Each item is ticked only with its proof. GUI runs prove feel; EditMode world tests prove rules. At most 2 runs per step.

Order note: in plan section 4, M3c (vault, Wallstride, Waterstride, follow, skill walk-in) comes before M4. "Skill walk-in" (walk to range,
then cast) is used by combat. Pick before code: M3c first, or M4 with the walk-in pulled in.

Rules (server, EditMode world tests at 0 and 100 ms):
- [ ] one combat formula: `CombatCalculator` as a Burst-compatible static on blittable structs, called by NGO and DOTS alike. A parity test
  runs the same inputs through the old and the new and gets identical results (damage, crit, evade, reflect, shield) before anything
  else moves.
- [ ] `Vitals` on the player ghost (HP, Flux, alive) written by the server only. The owner's HUD reads it. The Flux of section 53 (`OwnerFlux`)
  becomes part of it, with no second source.
- [ ] melee as input: the attack click goes in `PlayerInput` (target ghost + click seq, carried like a walk click); the server validates
  range, facing rule, cooldown and hostility; the hit moment comes from the clip's data tick (no frame timing). The combo steps, their
  timing and the cancel rules are the same as NGO's `PlayerMeleeCombat`.
- [ ] skills as input: cast, charge (held, released), queue (one queued attack, the NGO rules), Flux paid once by the server, a
  refusal in NGO's words on both sides (the section 53 pattern: the owner checks first, the server again).
- [ ] lag compensation: a server-only `PositionHistory` ring (16 ticks: position + capsule) on every hittable ghost. The hit is tested at
  the attacker's interpolation tick (what they saw), never further back than the ring. Tests: a target that moved away after the
  attacker saw it is still hit within the window; a claim older than the window is refused, not trusted (the section 53 clamp pattern).
- [ ] hostility as a pure function (`CombatHostility` on data): other factions hostile by default, `gm.pvp`, duels pairwise; the same
  answers as NGO for a table of cases.
- [ ] the Combat Dummy is the first ghost target (server spawned, registered in `GameplayPrefabsSetup.SpawnedPrefabs`), then players.
- [ ] death from combat uses the section 53 `MotionPlan` death knock; respawn as NGO.

Presentation (client):
- [ ] a HitEvent ring on the ghost goes to `ClientPresentationRegistry` (damage numbers, `HitReactor`, VFX). Each hit is shown once, a
  rollback never repeats it, and the attacker's and target's own feel only for the body that `BodyRoleTag.IsMine` says is mine (plan 52).
  `PlayerMeleeCombat.AttackerIsHere` (NGO routing) is replaced by this route.
- [ ] the owner's swing starts on the press frame (predicted animation only, like the jump clip); the hit number appears when the server's
  hit arrives. Measure press to swing and press to number at 0 and 100 ms and compare them with NGO's.
- [ ] Cadence (on-beat bonus) stays a local look plus a server-checked bonus window (no frame-rate advantage).

Moved in from M3c (owner pick 31 Oct): skill walk-in and Squad follow (section 55 items 1 and 2), with their server gaps:
- [ ] Run Flux charged once for a server-ordered run, as NGO's `ServerMoveTo` (refused without the Flux; later steering steps free)
- [ ] the Run selection known on the server (a server order runs when Run is selected, at every step, not only the first)

The rest of M4 (plan section 4):
- [ ] the Wallet vertical slice, one Trade with a disconnect mid-trade, the kill test, and the golden ledger on the TEST database only
  (`ServerSave.SaveTogether`, `EconomyLedger`; CLAUDE.md "All-or-nothing changes").
- [ ] owner feel checkpoint after M4 (by hand; pending is allowed, as for M3).

## 55. M3c traversal and server walks: acceptance list (owner pick 31 Oct 2026: M3c before M4; written before code)

The DOTS vault, Wallstride, Waterstride, follow and skill walk-in must feel exactly like NGO's, server still deciding. They are built
the M3b way: the server plans a `MotionPlan` (or a `ServerOrder` for walks), the owner predicts it from the press when its own rules
allow (the section 53 pattern: same rules, same order, same words on both sides), and the body is drawn on the plan's exact curve. The
clip plays in place on the plan's clock; KINEMATION Motion Warping is the source of the curve (baked, like the jump's), never a second
writer of the root (owner's rules: [[traversal-uses-motion-warping]]; only copies of clips are adjusted). NGO's code is the reference:
`PlayerVaultMotor` (TryArm / Watch / Landed, `TraversalSettings`, `VaultAnimationMap` per faction), `PlayerWallRunMotor` (+ `.WallWalk`,
the zone surface graph), `PlayerWaterstrideMotor` (`WaterMap`, Flux per second), `PlayerFollow` (`FollowRules`, `SquadSettings`, jump copy).
Each item is ticked only with its proof: EditMode world tests (0 and 100 ms) for rules, GUI runs with Game-view frames against NGO's
behaviour for feel. At most 2 runs per step.

Order (owner pick 31 Oct, each finished before the next): **vault, then Waterstride, then Wallstride**. These are driven by clicks the DOTS
client already sends, so each is proven in the GUI against NGO now. Skill walk-in and follow (items 1 and 2 below) MOVED to M4 (section 54)
and the Squad port: both are started by the owner through NGO RPCs (skill hotbar -> `PlayerSkillQueue`; Squad follow -> `PlayerFollow`),
which do not run on a DOTS client. They take two server gaps with them: Run Flux is not charged for server-ordered runs
(`PointAndClickPlayer.ServerMoveTo` mover branch), and the server body does not know the Run selection (DOTS sends Run per click).

1. Skill walk-in (the `ServerOrder` walk exists: `PlayerEntitiesSetup` "walk the player to a point")
- [ ] a skill clicked out of range walks the player into range by the server's order, at the Run or walk speed the click would use (Flux for
  Run as a click), and the skill starts on arrival; a new click or another order cancels it; the owner sees it start from the order's tick
- [ ] the range and the stop point are NGO's (the skill's range minus NGO's margin); a target that moves updates the walk as NGO did
2. Follow (Squad)
- [ ] the follower walks after the member by the server's orders, stops `SquadSettings`' distance off, re-plans as NGO did
- [ ] the member's jumps are copied: walk to the take-off, jump to the landing with the follower's own Jump (rank, Flux, arc; section 53
  rules), so two can cross a river together; ends on the NGO conditions (own order, fight, defeat, Squad change, map change)
3. Vault
- [ ] going over is the rule: a walk whose straight line is blocked by a vaultable obstacle, no longer than walking round, vaults (NGO's
  `TryArm`, `Watch`); the plan carries the take-off, the obstacle, the landing and the faction's clip (`VaultAnimationMap`)
- [ ] the take-off happens without stopping (speed carried through), the landing hands the rest of the walk back; the same vaults as NGO for a
  table of obstacles (heights up to 2.2 m, the Wallstride minimum)
- [ ] the drawn body on the plan's curve within 1 cm at 30/60/120 fps (the section 53 bar); no clipping into the obstacle (bones vs face,
  side-view film strip)
4. Waterstride
- [ ] the NGO gate: level 60, Run and Jump rank 20; a click on water with Run selected runs to the nearest shore point, across by the `WaterMap`
  route, stands on the water at the end; a land click from the water runs to the nearest shore and walks on
- [ ] Flux every second on the water, paid by the server; out of Flux, carried back to the nearest shore; the owner's Flux (section 53
  `OwnerFlux`) refuses at once in NGO's words
5. Wallstride (wall-run, tree perch, wall leaps, wall walk)
- [ ] the owner's rules (memory [[wallstride-decisions]]): no glitching, never inside a building, a jump catches a wall at a slant
  (65 degrees, run 45 degrees) with the turn eased in the plant, a player is NEVER knocked or forced off a wall, can always leave by choice
- [ ] the climb as one warp route of eased legs, planned by the server from the zone's surface graph, predicted by the owner; the ground
  marker lies on the wall; a way down from a roof
- [ ] a death on a wall, in a tree, in the air or on a roof disintegrates in place (no fall to the ground), pet with them
- [ ] feet VFX, Wall fights unchanged (combat itself comes in M4)

For all five
- [ ] other players see the same start tick, curve and landing (2-client test world, `BodyIsMineWorldTests` style)
- [ ] a forced rollback mid-move neither restarts the clip nor moves the body more than the follow's offset
- [ ] owner feel checkpoint after M3c (by hand; pending is allowed)
