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
- [ ] **M3 Player ghost & prediction (split per review item 12: M3a walk/run/stop, teleport, save/identity/look with passthrough saves (playable);
  M3b jump/death `MotionPlan` + warp spike S7; M3c vault/wall-run/waterstride, follow, skill walk-in. Owner "feel" checkpoint after M3a and after M4.)**
  Movement must scale by tick fraction (or run on full ticks only and interpolate in presentation; decide in S3). `PlayerInput`, `ServerOrder`, `PathCorner`, `MoveState`, `MotionPlan`, `TeleportSeq`;
  predicted move system (integers, deterministic Burst); presentation body follows; walk/run/stop/jump/vault/wall-run/follow/
  skill walk-in/death; SaveAgent load/save, identity, equipment, look (`LookVersion` + cache) so the clone stays playable;
  unported systems run as local stubs behind a banner. Spikes S3 (path outside rollback), S4 (Windows vs Linux parity).
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
