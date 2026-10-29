# DOTS migration: persistence/save bridge and UI bridge (design, read-only research, 29 Oct 2026)

Inputs read: shared context, owner's 20 Oct plan, the four prior surveys, CLAUDE.md (saves + dependency sections),
Dependency-Map "How to read it", architecture-decoupling-plan rules. Code checked with graphify + reads in
`C:\Users\dlaco\Krytical Nova II` (nothing changed, Unity not opened).
Verification tags: **[V]** confirmed on Unity docs (URL in section 5), **[U]** unverified / from memory / inference, check in the 6.6 docs
before coding. Doc version fetched was netcode 1.4 / 1.8 manuals (the 6.6 built-in package version was not reachable; the DOTS clone's
`manifest.json` has no netcode/entities entries yet).

---------------------------------------------------------------------------------------------------------------------------

## Part 1. Persistence / save bridge

### 1.1 What the code does today (facts)

- `PlayerSaveAgent` (`Gameplay/Persistence/Runtime/PlayerSaveAgent.cs`, 436 lines) is a `NetworkBehaviour, ISaveScheduler` on `Player.prefab`.
  It collects the 19+ `ISaveParticipant`s with `GetComponents` (L60). NGO touches: `HasAuthority => !IsSpawned || IsServer` (L51), `IsOwner` (L52),
  `OnNetworkSpawn`/`OnNetworkDespawn` (L91-102: load / `SaveAndRelease`), `OwnerClientId` -> `ServerAccounts.TryGet` for the one-time carry-over (L165),
  `NetworkManager.DisconnectClient(OwnerClientId, text)` on refusals (L175, L178, L213), one owner->server RPC (`AgreementRpc`, L238).
  Everything else is plain C#: load (`TryLoad` L141-235), migrations (`SaveVersions.Upgrade` / `IMigratingSave`), `Save()` / `CaptureForSave()`, tallies, `SaveSoon`.
- Periodic save is the agent's own `Update()` every 60 s (L113), place written at most every 300 s, and on leave. Quit: `Application.quitting += FlushAll` (ServerSave.cs L44-45).
- `ServerSave` (static, 1007 lines): `LoadAccount` is **synchronous** (reads header and parts; `ServiceSaveStore.TryRead`/`TryReadMany`/`TryLock` block with
  `Task.Run(...).GetResult()`, 5 s HTTP timeout, `ServiceSaveStore.cs` "Send"/"Text"). **Writes are not synchronous**: `WriteBatch` only enqueues; a dedicated
  thread ("Save Service Writer") sends in order, retries, every batch has a GUID so a resend is not applied twice; the in-memory `waiting` dictionary is the truth until sent;
  `Flush()` waits. So the heavy HTTP is already off the game thread for writes.
- `ServerSave.SaveTogether(players, worlds)` (L422-465): on the main thread, calls `agent.CaptureForSave()` for each player, builds ONE `SaveBatch` (account parts that
  changed + world docs + waiting ledger lines) and hands it to the store; the service applies it as one transaction. All-or-nothing is a property of the **batch + service
  transaction**, not of threading. Used by: `PlayerTrade.Commit` (L538), `PlayerKiosk.ServerSaveWith` (L223), `PlayerCrew.ServerSaveWith` (L318), `PlayerRename` (L125), `ServerRoster` (4x).
- Ledger: `PlayerWallet` tallies every Cred via `PlayerSaveAgent.TallyCreds` -> `ServerSave.Tally`; item lines via `EconomyLedger.Line/Item/Keep` -> `ServerSave.Ledger`
  into `PendingLedger[account]`, flushed inside the next `Collect()` batch of that account ("saved with the change they describe").
- Identity: the server keeps `ServerAccounts: Dictionary<ulong clientId, {Account, OldAccount}>` (`Networking/Runtime/AccountProof.cs` L227-258) filled in approval after the Unity token is
  verified and cleared in `OnClientDisconnectCallback`. Readers: `PlayerSaveAgent` L165, `PlayerIdentity` L185, `GmRoster`, `SecurityFlags`, `ServerBans`, `ServerRecords`, `ServerControlPage`, `WorldLink`.
  Saves themselves are keyed by **account id + character id**, never client id; `ServerSave.BoundAccounts` + the service `/lock` stop a second character of one account (and a second server).
- Roster: `ServerRoster.Handle(account, oldAccount, RosterRequest)` is static pure C# (List/Create/Delete/Rename; uses `TryHoldOffline`, `WriteTogether(…, NamesDocument)`).
  Today it is reached from `NetworkConnectionManager.AnswerRoster` inside connection approval, and the JSON answer rides back as the **disconnect reason text**
  (`RosterLink`). N4E has no reason text, so this transport must change (not the roster logic).

### 1.2 Decision: who owns a player's save state when the player is a ghost

**The server body GameObject owns it. The ECS ghost entity owns none of it.**

Why: the 19+ `ISaveParticipant`s are managed rule classes (JsonUtility, strings, dictionaries, ScriptableObject catalogs); their `CaptureSave/RestoreSave`, `IMigratingSave` steps,
`PlayerSaveAgent`, `ServerSave`, `EconomyLedger` and `LedgerNote` are all main-thread managed code, and CLAUDE.md demands they keep their guarantees unchanged. Making them ECS-native would
be a rewrite of the economy that the owner explicitly said must keep `SaveTogether` + ledger. So:

- The 20 Oct plan's "ServerBody" (same `Player.prefab`, `NetRole.ServerBody`) stays the single holder of `PlayerSaveAgent` + all participants. One body per connected player, referenced from a
  managed registry `PlayerKey -> ServerBody` (Networking, type-agnostic; holds `GameObject`).
- The ghost carries only what the world must see/predict (position, vitals, anim, look version…). Direction of sync per field must be single-writer; table (extends the plan's "state placement"):

  | State | Truth lives in | Writer | Direction |
  |---|---|---|---|
  | Position, Motion, TeleportSeq (predicted) | ghost | `PredictedMoveSystem` (Burst) | ghost -> server body `transform` each tick, before RPC receive systems (range checks read it) |
  | HP/Chi/Level/InCombat for display | **server body rule classes** (`PlayerResources`, `CombatEntity`) | body | body -> ghost `Vitals` each tick; Burst combat produces `DamageRequest`/`HitEvent` records, a main-thread managed system applies them to the body (see 1.5) |
  | Inventory, wallet, vault, contacts, crew, trade, kiosk… | server body | body | body -> owner-only ghost buffers or view RPC pages (UI part) |
  | Account/character ids | connection entity + body | approval system | set once |

  Open question 1 (below) is whether Vitals should instead be ECS-truth later; the save would then read it through the body's `PlayerResources.CaptureSave` unchanged either way, because the body
  adapter reads/writes the ghost component.

- Pure ECS ownership was rejected (it would need every participant re-expressed as blittable data and JSON bridging per system; no benefit, since save frequency is ~1/min and the cost is serialisation, not iteration).

### 1.3 Lifecycle in a server world

Everything below runs in **managed `SystemBase` systems on the main thread** (no Burst, no jobs). [V] "All system events run on the main thread" (Entities systems page) — and a `SystemBase` is a managed class; managed-API
use in it is allowed [U: exact attribute/ISystem rules to confirm].

1. **Connect.** Approval stays what it is (token verify via `UnityToken`/`UnityKeys`, bans, build check) but its transport changes (plan Stage 2: chunked `IApprovalRpcCommand` [U name/limits], RPC size limit [V: "sending RPC larger than 1MTU is not currently supported"]).
   On success a managed `ConnectionAdmittedSystem` (Networking):
   - mints `PlayerKey` (monotonic `long`, server counter), puts `PlayerKey` + proven `account` + `oldAccount` on the connection entity (managed `PlayerKeyRegistry` dictionary `PlayerKey -> {Entity connection, account, oldAccount, characterId}`; **no component data is read by Burst**);
   - waits for the client's `GoInGameRequest` RPC (carries the chosen `characterId`, name, a `FixedString` or paged recipe; roster check as `NotOnRoster` today);
   - spawns the ghost, adds `NetworkStreamInGame` [V: never added automatically; no snapshots/commands until added], sets `GhostOwner`;
   - calls `ServerHooks.Players.Entered(playerKey, account, characterId, ghostEntity)` (Networking-owned interface, filled by Gameplay/Player) which instantiates the Player.prefab **inactive**, sets `NetRole.ServerBody` and identity (`PlayerIdentity.Begin(account, characterId, name)` replacing the NGO-time `AccountId` lookup), registers the body and activates it.
2. **Load.** Identity set triggers the existing `PlayerSaveAgent.TryLoad()` (it already reacts to `identity.Changed`). Replace only: `OnNetworkSpawn` trigger -> `Begin()` call from the Entered hook, `OwnerClientId`/`ServerAccounts.TryGet` -> `PlayerKey`, `NetworkManager.DisconnectClient(OwnerClientId, text)` -> `ServerHooks.Players.Refuse(playerKey, text)` (sends a `JoinRefusedRpc` then a `NetworkStreamRequestDisconnect` [V] a few ticks later, as plan Stage 2). All of `TryBind` / account lock / `LoadAccount` / migration / `RestoreSave` stay byte-for-byte.
   The load blocks the main thread for the HTTP round trips (lock + header + one batched `TryReadMany`); this is what NGO does today in `OnNetworkSpawn`. Acceptable in phase A; see 1.6 for the non-blocking variant.
3. **Periodic.** Keep `PlayerSaveAgent.Update()` autosave (60 s) on the server body: the body is an active GameObject on the server (dedicated server already runs the full prefab headless), so nothing is lost. Do **not** move autosave into an ECS system (it would just call the same managed methods, and Fast Script Reload/Burst concerns add nothing). `SaveSoon` unchanged.
4. **Disconnect (order matters; plan item 22).** A managed `ConnectionClosedSystem` in `SimulationSystemGroup` reads `NetworkStreamDriver.ConnectionEventsForTick` `Disconnected` [V: server raises it once per accepted client, with `DisconnectReason`; events live one tick, read only from a system in that group; connection entity is destroyed]. Because the connection entity is gone, the `PlayerKey` is resolved from a stable record: either our managed `NetworkId -> PlayerKey` map written at admit time and removed here, or a `ConnectionState` component, which [V] keeps NetworkId and reason until removed. Then, in this order:
   1. `body.GetComponents<IServerLeaving>()` -> `OnServerLeaving()` (new tiny interface in **Core**, like `ISaveScheduler`; `PlayerSaveAgent` implements it and calls `SaveAndRelease()`; same for `PlayerTrade`/`PlayerKiosk` cancelling open sessions as their `OnNetworkDespawn` does now);
   2. `SaveAndRelease` -> `Save()` -> `WriteAccount` (enqueue) -> `ServerSave.Release` -> `Unlock` (account lock released only after the batch is queued; the writer thread keeps order);
   3. destroy body, then ghost; remove registry entries; `RpcBudget.Forget(playerKey)`, `NetworkHealth.Forget`, `ServerRecords.Left`.
   The ghost must NOT be tied to the connection through `LinkedEntityGroup` (it would be destroyed before the save runs).
   A kill/crash of the server process is covered as today: nothing new is written, the last queued batch is either on the service or retried; locks expire after 180 s.
5. **Quit/restart.** `Application.quitting` hooks (`FlushAll`, `WaitForDisk`) and `ServerLifetime.StopNow` remain; before they run the same Leaving path is executed for every registered body (a loop over the registry, from a system's `OnDestroy` or the existing shutdown code).

### 1.4 Account / PlayerKey identity vs NetworkId reuse

- **PlayerKey = server-minted monotonic number, never the raw `NetworkId`.** [V, partial] Unity's API docs: "When a client disconnects, its network id can be reused by the server, and assigned to a new, incoming connection" (NetworkId API page).
  So any async continuation (UnityKeys fetch, a roster call, a save callback, a delayed `JoinRefusedRpc` disconnect) must capture `PlayerKey` and re-check `PlayerKeyRegistry.TryGet(playerKey)` and that the connection `Entity` still matches before acting. Never capture `NetworkId`.
- Re-key `ServerAccounts` from `ulong` client id to `PlayerKey` (plan Stage 1). Its 13 reader sites change their lookup argument only. `Entry {Account, OldAccount}` unchanged.
- The persistent identity remains **proven Unity account id + character id** (`AccountIds.FromUnityPlayer`, `SaveKeys`); `ServerSave.BoundAccounts` + service `/lock` are the real double-login guard, independent of any connection id. `OwnerClientId` in `IPlayerBody` (Core) becomes `PlayerKey Owner`.
- Reconnect race (same account reconnects before the old connection timed out): today refused by `TryBind` ("already has a character in the world"); unchanged. See Open question 3.
- `ServerBans`, `SecurityFlags`, `ServerRecords`, `WorldLink` use account for identity already; only their connection lookup changes.

### 1.5 All-or-nothing multi-player changes (trade, Kiosk, Crew) in a Burst/job world

Rule set (to be copied into the DOTS plan):

1. **Economy code never runs in a job or Burst.** Trade/Kiosk/Crew/Rename/Roster are managed rule code on server bodies, called from **main-thread managed RPC-receive systems** (one `SystemBase` per gameplay system, as in the plan: `XRpcReceiveSystem`). Those systems run in `SimulationSystemGroup` before/after the tick groups but always on the main thread; a `SaveTogether` call is therefore a plain synchronous call from one system `OnUpdate`. No locks needed: the main thread is the only writer of `PendingLedger`, `LastWritten`, `BoundAccounts`.
2. **Receive pattern** (per system): `foreach (cmd, ReceiveRpcCommandRequest) in query` -> `RpcBudget.Allow(playerKey, CallKind.X, …)` / `TextFits` / `PointFits` -> `PlayerKeyRegistry.TryGetBody(conn, out body)` -> `body.GetComponent<PlayerTrade>().ServerXxx(args)` (the existing method) -> destroy the request entity (or `EntityCommandBuffer`). [V] receive entity "can be used to identify which connection the request was received from"; [U] field name `SourceConnection`. [V] RPCs are reliable, processed on the frame received, not tied to a tick; docs give no cross-RPC ordering guarantee [V: not stated] — so a rule: **an RPC must never depend on a previous RPC having been applied first**; sequence numbers where the order matters (trade lock/confirm already has `session` state checks).
3. **Burst/job systems may only produce requests, never economy effects.** Examples: a Burst pickup-range system writes a `PickupRequest` buffer element; a Burst combat/lag-compensation system writes `DamageRequest`/`KillEvent`. A main-thread managed `ServerRulesSystem` drains them and calls the existing methods (`CombatEntity.ServerApplyDamage`, `PlayerInventory.ServerAdd`, `PlayerWallet.ServerEarn(CredsSource.…)`, `EconomyLedger.Keep`). So every Cred still passes `PlayerWallet` (new source = new `CredsSource`) and every item movement still writes its `LedgerKinds` line. This keeps CLAUDE.md's ledger rule enforceable by grep: no file with `[BurstCompile]` may reference `PlayerWallet`, `EconomyLedger`, `ServerSave`, `PlayerInventory` (add as a `dependency_map.py` or compile_check lint).
4. **Return path (results to the player).** The method changes body state; a `BodyToGhostSyncSystem` (managed, main thread, after the receive systems, per system) copies to owner-only ghost buffers / sends view RPC pages; notices go as `NotifyRpc`/`RequestFailedRpc` (`FixedString`). The caller gets its answer on its next snapshot or RPC; nothing ever blocks waiting.
5. **Cross-player lookups** (trade partner, Kiosk owner, Crew members) go through `PlayerKeyRegistry`/`ghost -> GameObject` registry instead of `NetworkManager.ConnectedClients` (35 sites) — the second player is resolved on the same main thread in the same system update, so both agents are in one `SaveTogether` call exactly as now.
6. **Ordering guarantee kept:** state mutation, ledger lines, `SaveTogether` happen in the same method on the same frame; the batch (with GUID) is enqueued once; a crash between enqueue and send loses all of it, after send the service applies one transaction. Nothing in ECS changes this.
7. **Kiosk/Crew world docs** (`ServerSave.World<T>` / `SaveWorld`) are main-thread managed statics; keep. `KioskService` spawns `KioskStand` ghosts from the world doc: spawning is an ECB/EntityManager call from the same managed system.

### 1.6 Async SaveService HTTP from a SystemBase

Phase A (recommended, zero new async): keep the store exactly as is. Writes: `WriteBatch` enqueue (non-blocking, own thread). Reads: blocking `GetResult()` only on connect/roster/offline-edit paths. Risk: the 5 s HTTP timeout stalls the server tick if the local service hangs (same as NGO today: `OnNetworkSpawn`). Mitigation: `ServiceReachable()` precheck already exists in `ServerHooks.Saves`; refuse admission when the service is down (reads fail anyway).

Phase B (only if measurement shows hitches, e.g. hundreds of logins at once): non-blocking load.
- Connection stays without `NetworkStreamInGame` while a `Task<AccountSave>` (started with `Task.Run(() => ServerSave.LoadAccount(...))`) runs; this needs `ServerSave` statics (`PartKeysOf`, `LastWritten`) to become thread-safe or the read to be split into "fetch strings (thread)" + "parse/apply (main)" — a real change to `ServerSave`, which is why it is not phase A.
- A managed `LoadingPlayersSystem` polls `task.IsCompleted` in `OnUpdate`, **never `await`s and never touches `EntityManager` from another thread** [U: managed class `IComponentData` / dictionary of `PlayerKey -> Task` both OK; sync points not allowed from jobs], re-checks `PlayerKeyRegistry` for the captured `PlayerKey` (the connection may have died meanwhile; then `ServerSave.Release` + unlock), then runs the Entered hook.
- A `Task` continuation must not use `TaskScheduler.FromCurrentSynchronizationContext()` assumptions from NGO (`NetworkConnectionManager.Approve` uses it); in a world update there is no Unity sync context guarantee [U]: poll instead.

### 1.7 Roster / RosterLink / character select

- `ServerRoster.Handle` and its tests (`ServerRosterTests`, `SplitSaveTests`) stay. Replace only the transport:
  - Option R1 (plan): short-lived client world (`RosterLink` today opens a throwaway NGO client) sends `RosterRequestRpc` (paged `FixedString`), server managed system proves the token as for a join, calls `ServerRoster.Handle`, returns paged `RosterReplyRpc`, closes. RPC 1-MTU limit [V] means ~1.2 KB per page; current answers up to ~2.6 KB and requests up to 1.8 KB (`RosterLink.cs` per survey) -> 2-3 pages each way; pages carry `PlayerKey`-free `requestId` + index/count.
  - Option R2 (see Open question 4): answer the roster over HTTP on a port the server already runs (control page style) with the Unity token, no N4E world at the front end at all.
- `PlayerRename`, `PlayerHotbarMemory` unchanged except NGO edge.

### 1.8 Kill test and tests

- `Service~/KillTest/kill_test.py` talks only to the save service (`--test`, 7802, `krytical_nova_test`) with simulated trades; it is **independent of N4E and stays valid unchanged**. It proves the service half only.
- New coverage needed for the ECS edge (all on test DB/ports 7797): (a) disconnect during an open trade and mid-`SaveTogether`; (b) kill the server process between `Commit` and the writer thread sending (existing `ServiceSaveStore` crash-drop test hook from the kill test) with a real second client; (c) NetworkId reuse test: client A disconnects, client B gets A's NetworkId while A's save is still flushing — assert B's body never receives A's callbacks; (d) disconnect while a roster call/Unity key fetch is pending; (e) `PlayerKey` registry leak test after 1000 connects/disconnects (thin-client bot).
- `TransactionLedgerTests`, `SaveMigrationTests`, `ServiceSaveStoreTests`, `SplitSaveTests` are Editor tests of pure classes: unchanged.

### 1.9 What stays unchanged (persistence)
`SaveService` (.NET), Postgres, `ServiceSaveStore`, `FileSaveStore`, `ServerSave` (except `HasAuthority` reading `NetworkManager.Singleton` -> `NetRole`/world check), `AccountSave`, `ServerBackups`, `LegacyDataMove`, `ServerRoster` logic, `EconomyLedger`, `LedgerNote`, `LedgerKinds`, `PlayerWallet` tally, all `ISaveParticipant`/`IMigratingSave` classes and their JSON, `Core/I*Store` interfaces, nightly copies, lock semantics. Save format and keys do not change: no save migration.

### 1.10 What changes (persistence/identity files)
`PlayerSaveAgent` (split `.Net.cs`: AgreementRpc + spawn trigger -> `Begin`; `OwnerClientId`/`DisconnectClient` -> `PlayerKey`/hook), `ServerSave` (HasAuthority), `AccountProof.ServerAccounts` (key), `PlayerIdentity` (L185 lookup), `NetworkConnectionManager` (approval/disconnect callbacks -> N4E systems), `RosterLink` + `NetworkConnectionManager.AnswerRoster`, `ServerSaveHooks` (stays; add `ServerHooks.Players`), `GmRoster`, `SecurityFlags`, `ServerBans`, `ServerRecords`, `ServerControlPage`, `WorldLink`, `RpcBudget` (key), `PlayerTrade/PlayerKiosk/PlayerCrew` (network edge only), Core `IPlayerBody` (`OwnerClientId` -> `PlayerKey`), new Core `IServerLeaving`, `PlayerKey`.

---------------------------------------------------------------------------------------------------------------------------

## Part 2. UI bridge

### 2.1 Today (facts)
- UI = UGUI/UITK, 143 scripts, asmdef `!UNITY_SERVER`. It reads state through `X.Local` -> `LocalPlayer.Get<X>()` (`Core/Runtime/LocalPlayer.cs`: GameObject-keyed cache; 110 `.Local` hits in UI by grep today, survey said 99) and ~93 `.Changed +=` subscriptions (C# events on the player components). UI does **not** subscribe to NetworkVariables; the components forward `OnValueChanged`/`OnListChanged` into their own events (e.g. `PlayerInventory.OnNetCellsChanged`, `netEquippedData`). `PlayerInventory.OnNetworkSpawn` decides who is Local (`SetLocal`).
- One-shot events (hit feel, toasts, chat, prompts…) go gameplay -> `ClientPresentationRegistry` slots (Core, 11 `I*Sink`s + pointer blocks) -> UI implementers; null on a server.
- Requests: `X.Method()` does `if (IsSpawned && IsOwner) FooServerRpc(args) else ServerFoo(args)`; 116 owner->server `[Rpc]`s; replies via NetworkVariable/NetworkList (some `ReadPermission.Owner`), targeted RPCs (`ViewRpc(ContactView[])`), `NotifyRpc`.
- 8 UI files use netcode types directly (`GameMenuController`, `NetworkSessionPanel`, `MenuRoster`, `PerformanceReadout`, `CadenceReadout`, `HostileNameplateController`, `LootHudController`, `MinimapController`) + 4 GameFeel files use `NetworkObject`/`IsLocalPlayer`.

### 2.2 Decision: keep the managed `X` component as the view model; bridge ghosts INTO it

The UI keeps binding to `X.Local` + `Changed` events. What changes is only who feeds those components:

```
SERVER WORLD ghost components / owner-only buffers / event rings / RPC pages
   │ (snapshots, N4E)                                   ▲ IRpcCommand (reliable)
CLIENT WORLD  ──►  per-system managed presentation systems ("XViewSystem", main thread, client only)
   │  diff against last applied copy, then call  X.ApplyView(...)  on the presentation body's component
   ▼
GameObject presentation body (Player.prefab, NetRole.ClientView): X MonoBehaviour fires the SAME C# events ──► UI (unchanged)
   one-shot records ──► ClientPresentationRegistry.Feedback / .Feel / .Chat … (unchanged sinks) ──► UI sink implementers (unchanged)
```

This is exactly the plan's "UI untouched if those classes stay", made concrete. No event bus, no DI, no new wrapper layer (CLAUDE.md): the "bridge" is one managed client system per gameplay system, living in that system's own assembly, calling the existing component and the existing Core sinks. Alternative of letting UI read ECS directly (`EntityManager`/`SystemAPI` from UI scripts) was rejected: 110+ call sites rewritten, UI would need Unity.Entities and a world reference, tests (`UI.Tests`) break, and nothing gained for a once-per-change menu.

### 2.3 Per-category bridge rules

1. **Owner-only menu state** (Pack, Vault, wallet, loadouts, skills, Nova…): server body -> `[GhostComponent(OwnerSendType = SendToOwner)]` buffer/component on the ghost [V: attribute exists; "controls whether the component is sent to the prediction owner" — exact enum semantics U], or paged view RPC for large lists (> a few hundred bytes; keep the plan's per-tick page limit). Client `XViewSystem` runs in `PresentationSystemGroup` [U name] with a change filter (`SystemAPI.Query<…>().WithChangeFilter<…>()` [U]) for entities that have `GhostOwnerIsLocal` [U: tag name] so it only runs for the local ghost, copies to the body component by **diff-then-fire**: compare with the last copy, call the same setter paths that `OnNetCellsChanged`/`OnNetEquippedDataChanged` call today, so `SlotChanged(slot, prev, next)` etc. fire with identical arguments. FixedList note: docs [V] say FixedString/FixedList (32-4096 B) are supported GhostField types, lists in components/buffers capped at 64 elements (needs `GhostFixedListCapacity` beyond that). This contradicts the earlier survey claim ("GhostFields don't support FixedLists"); `CharacterNetState` may therefore be replicable directly (prediction-error reporting n/a). Prefer `LookVersion` + RPC anyway for crowd bandwidth; revisit with measurement.
2. **Other players' visible state** (look, anim, vitals, level, name tag): interpolated ghost components -> remote presentation body (also `Player.prefab` ClientView) in the same pattern; bodies created/destroyed by `PlayerPresentationSystem` (Gameplay/Player) when ghosts appear/leave relevancy. Position/pose follow the interpolated `LocalTransform` each frame. Nameplates/minimap/party frames read through Core interfaces (below).
3. **Local player identity.** `PlayerPresentationSystem` calls `LocalPlayer.Set(bodyGO)` when the ghost is owned by this client [U: via `GhostOwnerIsLocal`] — replacing `PlayerInventory.OnNetworkSpawn`'s `SetLocal`. `LocalPlayer.Changed` fires as today; all 28 `X.Local` accessors keep working. Before the body exists UI sees `Local == null` as at the title.
4. **One-shot moments** (strike, hit, skill VFX, chat line, toasts, duel/party events): server appends to a small ring buffer on the ghost `HitEvent/FxEvent {eventId, tick, kind, args}` (relevancy then limits who gets it) or sends a targeted RPC for owner-only notices. A client `…EventsSystem` keeps a per-ghost `lastEventId` and forwards each new record exactly once to `ClientPresentationRegistry.Feedback/.Feel/.Chat/.Messages…`. De-duplication is by our own `eventId`, not by tick-flags, so prediction rollback/re-simulation can never replay a toast [U: `NetworkTime.IsFirstTimeFullyPredictingTick` also exists but is not needed].
5. **World objects** (Hostile, WorldDrop, KioskStand, ZonePortal, weather, music): ghost -> client model/pool owned by the system (`HostilePool`, `WorldDropPool`, existing MonoBehaviours in their own Simulation assemblies). `HostileNameplateController`, `LootHudController`, `MinimapController`, `PartyHudController` stop using `NetworkObject` and ask a Core read-only interface (e.g. `IWorldTargets`: ids, positions, names, HP ratio, faction) that the owning system fills, same pattern as `IWorldMarkerSink`.
6. **Session info** (`NetworkManager.Singleton` in 8 UI files + `PerformanceLogRecorder`): a Core static `NetSession` (IsConnected, IsHost, RttMs, Players, ServerTick) filled by Networking each frame; UI reads it. Same slot pattern as `ClientPresentationRegistry`; asmdefs of Core/UI drop `Unity.Netcode.Runtime`.
7. **Stub for unported systems** (plan Stage 3+): `NetRole.LocalStub` presentation component works UI-only, never saves, banner shown. Keep.

### 2.4 Request flow UI -> server

1. UI calls the same public method as today (`PlayerTrade.Local.SetOffer(...)`).
2. `X.Edge.cs` (replaces the `: NetworkBehaviour` half) builds an unmanaged `struct SetOfferRpc : IRpcCommand { … FixedString/ids/fixed lists }` [V: RPC is a struct implementing `IRpcCommand`; codegen serialises; managed fields not allowed U] and calls Networking's one typed helper `NetClient.Send(in cmd)`, which does `EntityManager.CreateEntity(typeof(T), typeof(SendRpcCommandRequest))` in the client world [V: "Create an entity with your command and a SendRpcCommandRequest"; `TargetConnection` Entity.Null on a client goes to the server]. That helper is a 10-line function, not a bus.
3. Server `XRpcReceiveSystem` (managed, main thread) as in 1.5.2. Validation, `RpcBudget`, rule check, state change, reply as in 1.5.4.
4. UI sees the result when the diff-then-fire system applies the next snapshot / RPC page (latency = one snapshot ≈ 33 ms at tick 30 + RTT). Optimistic UI is allowed only where it exists today (`ShowCastOnPress`, move anticipation → replaced by prediction).
5. The ~96 RPCs with `string`/arrays become `FixedString`, item/skill ids (`ushort`) and paged fixed lists (Stage 1 "unmanaged RPC arguments"; 1-MTU limit [V]).
6. Predicted input (move, jump, skill cast, swing) does **not** go via RPC: it is `IInputComponentData`/command data [U names] filled by a client system reading the existing `PointAndClickPlayer` input; out of scope here (player-movement session).

### 2.5 Layer placement (must satisfy `dependency_map.py --check`: 0 uphill, 0 two-way)

| New/changed assembly or code | Layer | Notes |
|---|---|---|
| `KryticalNova.Runtime` (Core) | Foundation | Add only plain C#: `PlayerKey`, `NetSession`, `IServerLeaving`, `IWorldTargets`, `TargetId`; **remove** Unity.Netcode refs, **no** Unity.Entities refs. |
| `KryticalNova.Networking` | Infrastructure | Bootstrap, approval/connection systems, `PlayerKeyRegistry`, `NetRole`, `RpcBudget`, ghost->GameObject registry, `NetClient.Send`, `ServerHooks.Players` (interface only), `NetSession` filler. References Unity.Entities/NetCode/Transport/Burst/Collections/Mathematics + Core. Never names a gameplay type (plan). |
| each Simulation system X (Items, Economy, Social, Progression, Combat, Hostiles, Player, Persistence, …) | Simulation | Keeps rules + view-model component; gains `Net/` folder in the same asmdef: ghost components, RPC structs, `XRpcReceiveSystem` (server), `XViewSystem` (client), `BodyToGhostSyncSystem` (server). Entities/NetCode are third-party refs, not systems, so they add no map links. Cross-system component use obeys the existing stack (e.g. `Vitals` lives in Progression, which Combat/Hostiles may use). |
| Gameplay/Player | Simulation | `PlayerPresentationSystem`, `PlayerServerBodySystem` (implements `ServerHooks.Players`), sets `LocalPlayer`. |
| Gameplay/Persistence | Simulation | Stays above Player/Items/Progression; reached from below only via Core interfaces (`ISaveScheduler`, `IServerLeaving`) and `ServerHooks.Saves` — same precedent as today. |
| World / Environment | Simulation | Weather/clock/music ghost + presentation (music moves from Audio per plan). |
| UI, GameFeel, Audio, Icons | Presentation | No Entities reference. UI keeps `!UNITY_SERVER`; consumes only Core sinks/interfaces + `X.Local`. The 12 netcode-touching UI/GameFeel files move to Core interfaces first. |
| Tools (builders, bots, `ServerBuildBuilder`, `MeasureBot` thin clients) | Tooling | May reference anything. |

Risks for the check: (1) a Networking system that "just calls" Player/Persistence = uphill — always via `ServerHooks`/Core interface; (2) World spawning player ghosts or Combat touching `Audio` — move first, per decoupling rule 1; (3) the map reads code text: new `Net/` folders count to their system; do **not** put ghost components of X into Networking. (4) `compile_check.py` and `--server` must include the new Entities/NetCode package references (shared note for the build-tools session). Also add the lint "no `[BurstCompile]` file references wallet/ledger/save/inventory types" (1.5.3).

### 2.6 What stays unchanged (UI)
All 143 UI scripts' logic and their `X.Local` / `Changed` bindings; `ClientPresentationRegistry` and the 11 sinks + implementers; `LocalPlayer`; `KeyBindings`/keyboard glyph rules; `UI.Tests`; ScriptableObjects; GameFeel sink usage. Only the 12 files with direct netcode use change (to `NetSession`/`IWorldTargets`/`LocalPlayer`).

---------------------------------------------------------------------------------------------------------------------------

## 3. Order of work (extends plan Stages 1-3, 7)

Stage 1 (NGO, no behaviour change; can run in parallel files; each ends with `dependency_map.py --check`, `compile_check.py` + `--server`, graphify update):
1. Core: `PlayerKey`, `NetSession`, `IServerLeaving`, `TargetId`/`IWorldTargets`; `IPlayerBody.OwnerClientId -> PlayerKey` (mapped from client id for now).
2. Re-key `ServerAccounts`, `RpcBudget`, `NetworkHealth`, `ServerRecords`, `ServerControlPage`, `GmRoster`, `WorldLink` to `PlayerKey`.
3. `PlayerSaveAgent` split (`.Net.cs` = AgreementRpc + spawn trigger); `Begin()` entry; `ServerSave.HasAuthority` via `NetRole`.
4. Move the 12 UI/GameFeel netcode uses behind `LocalPlayer`/`NetSession`/`IWorldTargets`; drop Netcode refs from Core and UI asmdefs.
5. `X.cs`/`X.Net.cs` splits with the stub-base compile gate (plan).
Stage 2 (copy): Networking bootstrap, approval chunks, `PlayerKeyRegistry`, `ConnectionAdmittedSystem`, `ConnectionClosedSystem` with the disconnect order of 1.3.4, `ServerHooks.Players`, roster transport (decision Q4).
Stage 3 (must stay playable): Player ghost + `PlayerServerBodySystem` + `PlayerPresentationSystem` + SaveAgent/Identity/Equipment edge (load on Entered, save on Leaving, autosave).
Vertical slice before the rest: **Wallet** end-to-end (small): ghost owner-only buffer -> `WalletViewSystem` -> `PlayerWallet.ApplyView` -> toast sink; `PlayerWallet` tally + trade-free spend request via RPC; run ledger tests. Then Inventory (largest UI user), then Stage 7 groups in the plan's order.
Stage 4+: event rings -> sinks; world-object views.
Stage 7 group 2 (Storage, Shopping, Kiosk, Trade): `SaveTogether` + ledger tests + kill test + the new tests of 1.8, test DB only.

## 4. Open owner questions (details in the final message)
1. Truth for HP/Chi/Level: server body (recommended) vs ECS component truth vs migrate later by measurement.
2. Save load on connect: keep blocking (recommended) vs non-blocking loading state.
3. Same account reconnects while old connection lingers: refuse (today) vs kick the old one (recommended).
4. Roster/character-select transport: throwaway client world RPC vs HTTP endpoint vs both.
5. UI: keep `X.Local` view-model bridge (recommended) vs rewrite UI to read ECS.

## 5. Sources (what each verified)
- RPCs (IRpcCommand struct, SendRpcCommandRequest/ReceiveRpcCommandRequest, reliable, processed on receive frame, no tick): https://docs.unity3d.com/Packages/com.unity.netcode@1.8/manual/rpcs.html [V]; field-type rules / size limit not on this page.
- 1-MTU RPC limit and GhostField types (FixedString/FixedList 32-4096 B, lists capped at 64 elements, dynamic buffers listed): https://docs.unity3d.com/Packages/com.unity.netcode@1.8/manual/ghost-types-templates.html [V]
- NetworkStreamInGame required, ConnectionEventsForTick, Disconnected + DisconnectReason, NetworkStreamRequestDisconnect, connection entity destroyed: https://docs.unity3d.com/Packages/com.unity.netcode@1.8/manual/network-connection.html [V]
- NetworkId reuse and ConnectionState (search result summarising the 1.1/1.4 API pages): https://docs.unity3d.com/Packages/com.unity.netcode@1.4/api/Unity.NetCode.NetworkId.html [V, via search summary; open the page once to quote exactly]; https://docs.unity3d.com/Packages/com.unity.netcode@1.1/api/Unity.NetCode.ConnectionState.html
- GhostComponent attribute (OwnerSendType, PrefabType, SendTypeOptimization, SendDataForChildEntity): https://docs.unity3d.com/Packages/com.unity.netcode@1.4/api/Unity.NetCode.GhostComponentAttribute.html [V]
- GhostOwner / owner-predicted needs GhostOwner.NetworkId: https://docs.unity3d.com/Packages/com.unity.netcode@1.8/manual/ghost-snapshots.html [V]
- System events on main thread: https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/systems-systembase.html [V for "main thread"; managed-API/ISystem rules NOT confirmed]
- NOT verified (404 or absent): `GhostOwnerIsLocal`, `PresentationSystemGroup` names/ordering, change-filter API, `IApprovalRpcCommand` details, `ReceiveRpcCommandRequest.SourceConnection` field name, managed class `IComponentData`, built-in 6.6 package versions (docs fetched are 1.4/1.8 manuals). Confirm in the 6.6 package docs or local `Library/PackageCache` once the DOTS clone imports packages.
