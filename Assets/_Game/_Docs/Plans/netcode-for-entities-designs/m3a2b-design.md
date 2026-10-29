# M3a-2b design: the real Player.prefab as ServerBody + ClientView, moved by the ghost

Status: design only (read-only research, 29 Oct 2026). Base: clone `C:\Users\dlaco\Krytical Nova II DOTS`, branch
`dots/netcode-entities` at 420a3ea18 (M3a-1 and M3a-2a green). Nothing compiled or run. Paths are relative to
`Assets/_Game/` of the clone unless they start with a drive letter. `[U]` = unverified assumption to prove in the named test.

## 0. What the code looks like today (facts that drive every decision)

1. **Only two classes use `NetRole`**: `PlayerInventory` (Items) and `PlayerSaveAgent` (Persistence). The other ~52
   NetworkBehaviours on Player.prefab (85 MonoBehaviours in all, counted from `Gameplay/Player/Prefabs/Player.prefab`) read
   the raw NGO properties `IsSpawned` / `IsServer` / `IsOwner` / `OwnerClientId`. 165 `OnNetworkSpawn/Despawn` overrides in 45 files.
2. A NetworkBehaviour that is **never spawned** (no NetworkManager) runs its "offline avatar" path: `IsSpawned == false`, so
   `!IsSpawned || IsServer` is true, `IsSpawned && !IsOwner` is false. The scene's offline Player works this way today, so
   every class already survives it. This is the stub mechanism: a body that is never NGO-spawned is a single-player avatar
   with full local authority. On the server that is exactly what a ServerBody wants (rules may mutate). On a client it is the
   "local stub behind a banner" the plan asks for.
3. `NetRole` is an adapter slot (`Core/Runtime/NetRole.cs`); the only adapter is `Networking/Runtime/NgoNetRoleAdapter.cs`
   (installed BeforeSceneLoad). With no NetworkManager every `Of(component)` answers Offline, so as things stand a DOTS
   client/server would run EVERY body as Offline. No `INetSessionSource`/`IWorldTargets` exists for DOTS either
   (`NgoNetSession.cs` only), so `NetSession.DecidesGame` (`!IsRunning || IsServer`) is true on a DOTS client.
4. `X.Local` for 30 of 33 systems is `LocalPlayer.Get<T>()` (reads `LocalPlayer.Object`, set by `GhostBodyRegistry.SetLocal`):
   it works on the client for free and is null on the server (nobody sets `LocalPlayer` there). Exceptions: `PlayerInventory.Local`
   (own static, set in `OnEnable` only if `NetRole.IsOffline` process-wide, `PlayerInventory.cs:112`), `PlayerRename.Local`
   and `PlayerGmSight.Local` (set in `OnNetworkSpawn` if `IsOwner`; stay null: acceptable stubs).
5. `PointAndClickPlayer` (2165 lines, `RequireComponent(NavMeshAgent, PlayerInput)`, `Gameplay/Player/Runtime/PointAndClickPlayer.cs:14`)
   does movement (agent), camera (Cinemachine orbit/zoom, occluder fader), click handling and the NGO RPCs. Its `Start`
   (L316) and `Update` (L420) take the offline branch when not spawned (`!IsSpawned || IsServer`), which would drive the
   agent. Other systems call its public API: `ServerMoveTo` (L1249), `ServerJumpTo` (L1241), `ServerStopMoving` (L1470),
   `TravelTo` (L821), `CommandAttack` (L963), `FacePoint` (L1792).
6. `PlayerSaveAgent` (`Gameplay/Persistence/Runtime/PlayerSaveAgent.cs`) is already NetRole-based; loading is triggered by
   `PlayerIdentity.Changed` (L63-L75, `OnIdentityChanged` L117 -> `TryLoad` L156). Spots that still name NGO-world things:
   L165 `NetPlayerKeys.Find(NetRole.OwnerId(this))` (works unchanged if `OwnerId` returns the connection serial, see 2.2), L297
   `NetworkConnectionManager.RequireProof` (static in the NGO class), and `ServerSave.TryBind` (`Persistence/Runtime/ServerSave.cs` ~L975)
   uses `holder.IsSpawned` to tell a networked holder (refuse the second login) from the offline avatar (hand over): a ServerBody
   is not NGO-spawned, so without a change the second login would silently take the first player's character over.
7. M3a-1/2a give us: ghost (`PlayerGhostTag`, `PlayerPose`, `MoveState`, `TeleportSeq`, `MoveStats`, `PathCorner`),
   `PlayerEntitiesSetup.SpawnServer/IssueOrder/Teleport` (`Gameplay/Player/Entities/Runtime/PlayerEntitiesSetup.cs`),
   client `PlayerBodySpawnSystem` + `IPlayerBodyFactory` (no implementation yet), `PlayerPresentationFollowSystem`,
   `GhostBodyRegistry` (client entries + `SetServerBody/TryGetServerBody(PlayerKey)`), `GhostPlayerBody : IPlayerBody`,
   `IClickSource`/`ClickCommandSource`. M2: `ConnectionKeySystem` (PlayerKey per connection, `ServerAccounts.Remember`),
   `ConnectionClosedSystem` (releases the key first, then `IKnConnectionClosedHandler`s, then `BodyOf` + `ServerLeaving.Notify`),
   `KnRefuseDisconnectSystem`/`JoinRefusedRpc`/`KnPendingDisconnect`, `KnTestWorld` (`GoInGame()` for tests only).
   **Not there yet**: any real GoInGame handshake, any server-side spawn of the ghost outside tests (`PlayerRig`), the
   `KnPreSimulation/rules` system group the plan names, a `PlayerGhostPrefab` subscene in a shipped scene.

## 1. Answer to question 1: the minimal change set (one real player works, ~50 systems untouched)

Principle: **the real prefab is instantiated inactive, tagged with a Core `BodyRoleTag`, stripped per role, then activated.
No NGO spawn ever happens.** Everything not touched below keeps compiling and runs as an offline-style stub.

| # | Change | Where | Size |
|---|---|---|---|
| 1 | `BodyRoleTag` (MonoBehaviour: `Kind`, `Key`, `ConnectionSerial`, `IsLocal`, `Mover`), `IBodyMover`, `IRoleAwareBody` marker, `ServerPolicy.RequireProof`, `SavePassthrough` rules | Core | new, ~150 lines |
| 2 | `BodyRoleNetRoleAdapter : INetRoleAdapter`, `KnNetSession : INetSessionSource`, `KnPlayerRefusals` | Networking.Entities | new, ~200 |
| 3 | `GoInGameRequest : IRpcCommand` (paged), client send system, `KnAdmissionSystem` + `IKnAdmission` slot, `KnRulesGroup` | Networking.Entities | new, ~250 |
| 4 | `PlayerRoleTable`, `PlayerBodyAssembler`, `ClientBodyFactory`, `ServerBodyMover`, `ServerBodySyncSystem`, `PlayerMoveSettingsBaker`, `BodyWorldTargets` | new asm `KryticalNova.Player.Entities.Bodies` | new, ~600 |
| 5 | `PlayerAdmission : IKnAdmission`, `PlayerLeaveHandler : IKnConnectionClosedHandler`, `SaveParticipantSet` (pure) | new asm `KryticalNova.Persistence.Entities` | new, ~400 |
| 6 | Edits to NGO-world classes | see 5.1 (all small) | ~60 lines in total |

What does NOT change in 2b: the other ~50 NetworkBehaviours (no `.Net.cs` split yet), the 216 RPCs, `ServerSave`/ledger logic,
`ServerRoster`, `JoinRules`. Movement truth is the ghost; the server body never steers.

### 1.1 Stub policy per class (how the unported ones run)

| Group | Classes (from the prefab) | ServerBody | ClientView local | ClientView remote |
|---|---|---|---|---|
| Role-aware (ported in 2b) | `PlayerSaveAgent` (+ `IRoleAwareBody`), `PlayerIdentity` (new `ServerBegin`, `ApplyView`), `PointAndClickPlayer` (ViewMode, Mover) | real | view mode | removed |
| Rules, raw NGO, run as offline-style stub | `PlayerCharacterProgression`, `PlayerMovementSkills`, `PlayerSkillHotbar`, `PlayerResources`, `CharacterCombatStats`, `CombatEntity`, `CharacterBuffManager`, `PlayerCombatSkills`, `PlayerMeleeCombat`, `PlayerEquipment`, `PlayerInventory`, `PlayerSkillAttacks`, `PlayerGmState`, `PlayerChat`, `PlayerDuel`, `PlayerParty`, `PlayerWeaponClass`, `PlayerHotbarMemory`, `PlayerPet`, `PlayerWallet`, `PlayerLoot`, `PlayerVault`, `PlayerDeepVault`, `PlayerTrade`, `PlayerKiosk`, `PlayerContacts`, `PlayerCrew`, `PlayerGearWork`, `PlayerNova`, `PlayerEmotes`, `PlayerFollow`, `PlayerSerums`, `FactionExtension`, `PlayerCombatState`, `PlayerSkillQueue`, `PlayerShopping`, `PlayerItemSources`, `PlayerWeaveLibrary`, `PlayerWeaves`, `PlayerCrafting`, `PlayerRestyle`, `PlayerSponsor`, `PlayerExperienceBoosts`, `PlayerCrate`, `PlayerHousing`, `PlayerRecall`, `PlayerFlask`, `PlayerGm`, `PlayerGmSight`, `PlayerCadence`, `PlayerLoadouts`, `PlayerMounts`, `MountFeeding`, `MountRider`, `PlayerRename`, `KNEmeraldPlayerBridge`, `TargetPositionModifier`, `WeaponHitbox` | kept, run as server authority (see 0.2) but their save parts are passthrough (section 4) | kept as local stubs (HUD `X.Local` works; nothing reaches the server; banner "prototype: gear, skills, wallet are local") | stripped (no rule code on an observer) |
| Presentation | `PlayerAnimancerMotor`, `AnimancerComponent` x2, `Animator`, `MotionWarping`, `FootGroundingIK`-style children, `PlayerJumpPresentation`, `EquipmentVisualBinder`, `FoliageInteractor`, `PetFollower`, `CharacterLod`, mesh children + `CharacterLook` (Sidekick) | stripped | kept | kept |
| Movement/camera | `PointAndClickPlayer`, `PlayerInput`, `NavMeshAgent`, `PlayerMoveAnticipation`, `NetworkTransform`, `PlayerJumpMotor`, `PlayerVaultMotor`, `PlayerWallRunMotor`, `PlayerWaterstrideMotor`, `PlayerDeathMotor` | `PointAndClickPlayer` and `NavMeshAgent` disabled (kept: others call the API and RequireComponent), `PlayerInput`, `NetworkTransform`, `PlayerMoveAnticipation` removed; motors disabled (M3b/M3c own them) | `PointAndClickPlayer` ViewMode on, agent+motors disabled, `NetworkTransform`+`PlayerMoveAnticipation` removed | all removed |
| Client automation | `PlayerAutopilot`, `MagnetController`, `AutopilotController`, `PlayerAutobuff`, `AutobuffCaster` | removed | disabled (they steer through the movement API; back in M3c) | removed |

The exact lists live in `PlayerRoleTable` as type-name strings (Player cannot reference Combat/Economy/Social types), and a
test (step 2) fails when a component type on Player.prefab is not classified, so a new `PlayerSystemsSetup` row forces a decision.

## 2. Question 2: role switching

### 2.1 Instantiate inactive, tag, strip, activate

`PlayerBodyAssembler.Create(GameObject prefab, NetRoleKind role, PlayerKey key, ulong serial, bool isLocal, Vector3 pos, float yaw)`:

1. Make an inactive holder (`new GameObject("BodyHolder"); holder.SetActive(false)`), `Instantiate(prefab, holder.transform)`. The
   instance is `activeInHierarchy == false`, so **no Awake/OnEnable has run**.
2. `AddComponent<BodyRoleTag>()` and fill it (`Kind`, `Key`, `ConnectionSerial`, `IsLocal`). Done before Awake, so any component that asks `NetRole`
   in Awake/OnEnable/Start already gets the answer.
3. Apply `PlayerRoleTable` for the role (strip lists below). All `DestroyImmediate`, in dependency order, legal because nothing is awake.
4. Set pose (`transform.SetPositionAndRotation`), `Rigidbody.isKinematic = true; useGravity = false` (it has one Rigidbody + 2 capsule colliders; a
   dynamic body would be moved by physics against the ghost) [U: check the prefab's current settings in the step-2 test].
5. `body.transform.SetParent(null)` (activates the hierarchy: Awake/OnEnable/Start run with role in place), `Destroy(holder)`.

Alternative rejected: reading the role in Awake from a global "next role" static (race with other spawns) and `DestroyImmediate` inside another script's Awake (undefined order).

### 2.2 The adapter (what `NetRole` answers on DOTS)

`BodyRoleNetRoleAdapter : INetRoleAdapter` in `Networking/Entities/Runtime/`, installed by `KnBootstrap` (after Core's reset, replaces the NGO adapter; the NGO
adapter stays for NGO tests):

- `Process`: server world exists -> `ServerBody`; else client world -> `ClientView`; else Offline (`World.All` flags). Host (both worlds) is out of scope for 2b (decision D5).
- `Of(component)`: **`Offline` unless the component implements `IRoleAwareBody`**; then the tag's `Kind` (looked up through a `Dictionary<GameObject, BodyRoleTag>` filled lazily
  by `GetComponentInParent`; `Of` is on hot paths). This is the stub rule: `PlayerInventory.Start()` (`IsUnspawned` -> `GrantStarterLoadout`, L118) keeps taking the offline path, which is what a stub needs; `PlayerSaveAgent` gets real roles.
- `IsOwner`: `Kind == ClientView && tag.IsLocal`. `IsOwnedByServer`: false. `IsRemoteOwned`: `Kind != Offline`.
- `OwnerId`: `tag.ConnectionSerial` (the registry id `ConnectionKeySystem` minted with, `ConnectionPlayerKey.Serial`), so `SaveAgent.cs:165`
  `NetPlayerKeys.Find(NetRole.OwnerId(this))` works unmodified.
- `DisconnectOwner(c, reason)`: `KnPlayerRefusals.Enqueue(key, reason)`; a server system turns it into `JoinRefusedRpc` + `KnPendingDisconnect` (the code of `KnApprovalAssembler.Refuse`, L214) and re-checks `PlayerKeys.IsAlive(key)`.

### 2.3 Parts switched per role (the stripping lists; final text comes from the step-2 test)

- **ServerBody keeps**: all rule behaviours of the table in 1.1, `CapsuleCollider` x2 (range checks, melee/area queries), `Rigidbody` (kinematic), `NetworkObject` (inert; NetworkBehaviours do not require it but removing it makes `NetworkBehaviour.NetworkObject` log [U]; keep).
  **Strips**: Animancer, Animator, MotionWarping, Sidekick `CharacterLook` + model children (identity handles `look == null`, `PlayerIdentity.cs:Apply`, and `FitBodyToHeight` uses only the capsule), `EquipmentVisualBinder`, renderers, `CharacterLod`, `PetFollower`, `FoliageInteractor`, `PlayerInput`, `NetworkTransform`, `PlayerMoveAnticipation`, `PlayerAnimancerMotor`, `PlayerJumpPresentation`, autopilot set. **Disables**: `PointAndClickPlayer`, `NavMeshAgent`, jump/vault/wall/water/death motors.
- **ClientView local keeps**: everything except autopilot set, `NetworkTransform`, `PlayerMoveAnticipation`, `NavMeshAgent`-driven motors (disabled); `PointAndClickPlayer` enabled in ViewMode; `PlayerInput` enabled when `IsLocal`.
- **ClientView remote keeps**: Animancer/Animator, `PlayerAnimancerMotor`, `CharacterLook`, `EquipmentVisualBinder`, `PlayerIdentity` (name, faction, look), `CombatEntity`/`FactionExtension` (nameplates, targeting), `PlayerPresentationState`, colliders (click targeting). Strips every other rule stub (cost: 100 players x 54 components) [U: confirm which UI reads components of a remote body: `PlayerMenuController`, `PlayerNamecardController`].

### 2.4 RequireComponent and the agent

`PointAndClickPlayer` requires `NavMeshAgent` + `PlayerInput`: Unity refuses to destroy a required component while a dependant exists. Rules:
- Never destroy `NavMeshAgent` while `PointAndClickPlayer` is on the body. ServerBody and ClientView-local **keep the agent, disabled**. `PointAndClickPlayer.Awake` (L208) already does `agent.enabled = false`, and `PlaceAgentOnNavigation` (the only place that enables it, L371-L397) is never reached because `Start` does not run on a disabled component (ServerBody) or is guarded (ViewMode).
- Remote ClientView removes `PointAndClickPlayer` first, then `PlayerInput`, then `NavMeshAgent` (inactive object, `DestroyImmediate`).
- `PlayerRoleTable` has a computed order: reflect `[RequireComponent]` of every type on the prefab; the test asserts no removed type is required by a kept one and the removal order is dependants first.
- `agent.updatePosition=false` is not needed: a disabled agent writes nothing. A lint (step 5) bans `agent.Warp`/`transform.position =` writes in classes that stay enabled on a ServerBody.

### 2.5 Hot spots a disabled/removed part exposes

`PointAndClickPlayer.OnEnable` (L2146) calls `PlayerBodies.Use(ActivePlayers)`: it would replace `GhostBodyRegistry`'s view list on the local ClientView. Guard: skip when ViewMode. Everything that asks `GetComponent<PointAndClickPlayer>()` (PlayerFollow, PlayerHousing, PlayerMeleeCombat walk-in...) still gets the component; its methods route through `BodyRoleTag.Mover` (5.1).

## 3. Question 3: spawn flow, disconnect order

```
client: connect -> KnApprovalRequest chunks (account proof, build, ticket)          [M2, exists]
server: KnJoinCheck ok -> NetworkId appears -> ConnectionKeySystem mints PlayerKey, ServerAccounts.Remember   [M2, exists]
client: sees NetworkId -> KnClientGoInGameSystem sends GoInGameRequest once (characterId, name, recipe pages) [NEW]
server: KnAdmissionSystem (KnRulesGroup) -> IKnAdmission.Admit(PlayerAdmission) [NEW]:
   1  PlayerBodyAssembler.Create(prefab, ServerBody, key, serial)  -> inactive, tagged, stripped, ACTIVATED
   2  identity.ServerBegin(state, ServerAccounts[key].Account, characterId)   -> Apply -> Changed -> SaveAgent.TryLoad()
        (account lock, roster check, migration, RestoreSave per participant; may refuse -> KnPlayerRefusals)
   3  agent.IsLoaded?  no -> Destroy(body); (refusal already queued); nothing else was created. yes -> continue
   4  PlayerSpawnService.PickPoint(occupied = server bodies' positions) -> NavMesh-sampled spawn
   5  ghost = PlayerEntitiesSetup.SpawnServer(em, PlayerGhostPrefab, networkId, mm); add PlayerGhostKey{Key} (cleanup, server only);
        set MoveStats from the body's movement skills (PlayerMoveSettingsBaker)
   6  body.transform to the same spot; BodyRoleTag.Mover = new ServerBodyMover(em, ghost)
   7  GhostBodyRegistry.SetServerBody(key, body); connection gets NetworkStreamInGame (snapshots flow only from here)
   8  Persistence hook SaveAgent already loaded; first Save() already ran inside TryLoad (L242)
server tick: ServerBodySyncSystem copies ghost pose -> body.transform before every RPC receive system
```

Deviation from the order given in the brief, and why (decision D1): the brief says load after ghost spawn. The load can refuse (account held by another server,
second login, not on roster, save written by a newer build) and is synchronous (blocking HTTP, phase A of `persistence-and-ui-bridge.md` 1.6). Loading first means a
refused player never has a ghost that other clients would see for a few ticks, and there is nothing to unwind. The ghost is the LAST thing created; snapshots only start at `NetworkStreamInGame`.

### 3.1 Disconnect (`PlayerLeaveHandler`, registered in `ConnectionClosedSystem.Handlers`)

`ConnectionClosedSystem` already releases the PlayerKey first (`PlayerKeys.Release`) and then runs handlers. The handler does the whole sequence itself (`BodyOf` stays null; the system's own `ServerLeaving.Notify` is
then skipped, and `IServerLeaving` is idempotent by contract anyway):

1. **Invalidate the ghost first, before anything that can be async**: find the ghost by `PlayerGhostKey == key`; set `GhostOwner.NetworkId = 0`, remove it from relevancy (`GhostRelevancy` / a `PlayerLeaving` tag excluded from `PredictedMoveSystem` queries). This is the advisor-review item 2 protection: netcode may hand the same NetworkId to the next connection, and a lingering ghost with that owner id would become that player's `GhostOwnerIsLocal`/command target.
2. `ServerLeaving.Notify(body)` -> `PlayerSaveAgent.OnServerLeaving` -> `SaveAndRelease` (`Save` enqueues the batch, then `ServerSave.Release` + `Unlock`), trade/Kiosk cancel through their own `IServerLeaving`.
3. `GhostBodyRegistry.SetServerBody(key, null)`; `Destroy(body)`.
4. `em.DestroyEntity(ghost)` (immediate, not through a command buffer, and NOT tied to the connection's `LinkedEntityGroup`).
5. `RpcBudget.Forget`, `ServerRecords.Left`, `NetworkHealth.Forget` (the NGO-side equivalents of `OnClientDisconnectCallback`); `ServerAccounts.Forget(key)` is done by the system last.
Phase A saves are synchronous enqueue, so steps 1-4 happen in one call and the ghost never survives a tick; step 1 is cheap insurance for phase B (async save) and has its own test.
Quit/SIGTERM: `KnStopMachine` "save" step must call the same handler for every body in `GhostBodyRegistry` server map before `ServerSave.FlushAll` (`ServerShutdown.Handler`).

### 3.2 Things GoInGame must carry

`GoInGameRequest : IRpcCommand` (Networking.Entities, no Gameplay type): `FixedString64Bytes CharacterId`, `FixedString32Bytes Name`, paged recipe text
(`FixedString512Bytes Chunk`, `byte Page, PageCount`; precedent `KnRosterPages`). `CharacterNetState` itself implements NGO's `INetworkSerializable` and is not RPC-able under N4E; the server
rebuilds it with `CharacterNetState.From(name, recipe)` after `JsonUtility`/packed-bytes parsing. The maximum legal recipe (63 parts, 15 colours) must fit in <= 3 pages (test). The account is NEVER taken from the request: `ServerAccounts.TryGet(key)`.
Refuse when `RequireProof` and no proven account, as `PlayerIdentity.ProvenAccount` (L180-187) does today.

## 4. Question 4: the passthrough-save rule

**Rule:** a participant on the `SavePassthrough` list is never restored and never captured: its stored JSON stays in the `AccountSave`/`CharacterSave` parts exactly as loaded,
so the next write contains the identical bytes and the part is not dirty (`SaveParts.Set` returns false when equal).

**Where:** nowhere inside 19+ participants. In `PlayerSaveAgent`, through one pure class `SaveParticipantSet` (Persistence, plain C#, ~120 lines) that replaces the two loops of `TryLoad` (L201-L237) and `CaptureForSave` (L334-L348):

```
SaveParticipantSet.Load(participants, account, character, passthrough, log)   // migrates + restores the non-passthrough ones, returns per-key result
SaveParticipantSet.Capture(participants, account, character, passthrough, log) // returns changed; passthrough keys are not even asked
```
- Passthrough parts are NOT migrated either (`SaveVersions.Upgrade` would rewrite the bytes); they stay at the stored version until their system is ported. A part saved by a newer build still blocks nothing for passthrough, but also is never read.
- Header fields written in `CaptureForSave` L350-L358 (`character.level` from `progression`): if `progression`'s key is passthrough the three fields are left as loaded; `character.map/x/y/z/savedUtc` keep their existing "place" rule (they are the only intended change).
- Policy source: Core `SavePassthrough` (static set of SaveKeys + `Is(key)`; reset on `SubsystemRegistration`; default for 2b: everything EXCEPT `look` (PlayerIdentity.LookSaveKey) and the progression key). A system leaves the list in its own port PR together with its test. No ScriptableObject (pure code list, like `LedgerKinds`).
- `ISaveParticipant` implementations stay untouched (no adapter class per participant); the policy is looked up by `SaveKey` in the set.
- `PlayerSaveAgent` implements `IRoleAwareBody`; on a ClientView `HasAuthority` is false so `Load/Capture` never run.

**Test spec (byte-identical load -> save):**
- EditMode `SaveParticipantSetTests` with fake participants (stored text with odd whitespace, an old version stamp, unknown fields, numbers in non-canonical form): for P keys `Capture` returns changed == false and `account.Get(key)` equals the original bytes; for R keys the fake's own formatting is used; mixed set; a throwing `RestoreSave` of one R key does not touch others; migration failure of an R key aborts the load and leaves everything as stored (existing behaviour L210-L221); a P key with a text the build cannot parse still loads.
- EditMode `SavePassthroughLintTests`: load `Player.prefab` with `AssetDatabase`, `GetComponentsInChildren<ISaveParticipant>(true)`, assert every `SaveKey` is classified R or P (a new participant forces a choice) and no two share a key. Reading `SaveKey` needs no Awake.
- **Cannot run in EditMode** (real participants need Awake): built-player/PlayMode test `B2 RealParticipantsRoundTrip`: test save service (`SaveService --test`, 7802, `krytical_nova_test`) with a copy of a real character; admit, wait for `Loaded`, `Save()`, read all parts back; compare every part byte for byte for the P keys, and `ServerSave` batch count == 0 for an untouched character (place excluded). Never the real database.

## 5. Question 5: classes, assemblies, order of work

### 5.1 NGO-world edits (file:line in the clone)

1. `Core/Runtime/NetRole.cs`: add nothing to `INetRoleAdapter`; new files `Core/Runtime/Bodies/BodyRoleTag.cs`, `IBodyMover.cs`, `IRoleAwareBody.cs`, `Core/Runtime/Net/ServerPolicy.cs` (`RequireProof`, reset on SubsystemRegistration), `Core/Runtime/SaveSystem/SavePassthrough.cs`.
2. `Gameplay/Persistence/Runtime/PlayerSaveAgent.cs`: class gets `IRoleAwareBody` (L21); L201-L237 and L334-L348 -> `SaveParticipantSet`; L297 `NetworkConnectionManager.RequireProof` -> `ServerPolicy.RequireProof`; header guard at L350-L358; new `public bool IsLoaded` already exists (L45). No other change: `OnNetworkSpawn/Despawn` simply never fire.
3. `Gameplay/Persistence/Runtime/ServerSave.cs` ~L980 `holder.IsSpawned` -> `NetRole.IsNetworked(holder)` (identical under NGO: `NgoNetRoleAdapter.Of` is `IsSpawned`-based).
4. `Gameplay/Player/Runtime/PlayerIdentity.cs`: L186 `NetworkConnectionManager.RequireProof` -> `ServerPolicy.RequireProof`; new `public string ServerBegin(in CharacterNetState state, string account, string characterId)` wrapping private `ServerAssign` (L205) and returning a refusal text or null; new `public void ApplyView(in CharacterNetState)` (client observers: `Apply` without the starter kit / save work).
5. `Gameplay/Player/Runtime/PointAndClickPlayer.cs`: `bool ViewMode => NetRole.IsClientViewBody(this)` is false for it (not role-aware), so use `BodyRoleTag.Kind`: new property `ViewMode`. Guards: `Start` (L316: skip the agent/NavMesh part and `PlaceAgentOnNavigation` when ViewMode), `Update` (L420-L436: skip the movement block and `UpdatePendingInteract/UpdateTravel` when ViewMode), ground click (L769: `if (ViewMode) { ClickQueue.Enqueue(point, run); marker; return; }`), attack/interact/jump branches (L646-L690, L760): `CommandMessage("ported in M4")` in ViewMode, `OnEnable` L2146-L2149 skip `PlayerBodies.Use`, `OnLocalChanged()` (what `OnNetworkSpawn` L240-L265 does for the owner: `OfflinePlayerHandoff.ReleaseOfflineAvatar()`, enable `PlayerInput`, `SetupLocalCamera`). Mover routing: `ServerMoveTo` (L1249), `ServerJumpTo` (L1241, "not yet"), `ServerStopMoving` (L1470) call `tag.Mover` first when present. ~50 lines.
6. `Gameplay/Items/Runtime/PlayerInventory.cs:112`: `NetRole.IsOffline` -> `(NetRole.IsOffline || BodyRoleTag.IsLocalClientView(this))` so `PlayerInventory.Local` exists on the client stub (38 UI/gameplay readers).
7. `Networking/Runtime/NetworkPlacement.cs` `Place`: if the root has a `BodyRoleTag` with a `Mover`, call `Mover.Teleport(position, rotation)` and return (NGO path unchanged). `Networking/Runtime/PlayerSpawnService.cs`: extract `PickPoint(IEnumerable<Vector3> occupied, out Vector3 pos, out Quaternion rot)` from `PlaceSpawnedPlayer`/`NextFreePoint` (it reads `NetworkManager.ConnectedClientsList`, L66-L77).
8. `Networking/Entities/Runtime/KnBootstrap.cs`/`KnServerEntry.cs`: install the new adapter, `KnNetSession`, `ServerPolicy.RequireProof = options.RequireProof`; server start calls `OfflinePlayerHandoff.ReleaseOfflineAvatar()` (the scene avatar must be inactive on a server or it claims the account: `ServerSave.TryBind` would hand over).
9. `Gameplay/Player/Entities/Presentation/Runtime/GhostBodyRegistry.cs` `SetLocal` (L109-L115): also set `BodyRoleTag.IsLocal` and raise its change event.

### 5.2 New classes and assemblies (layers; `dependency_map.py --check` stays 0 uphill / 0 two-way)

| Assembly | Folder (system, layer) | References | Contains |
|---|---|---|---|
| `KryticalNova.Runtime` (Core, Foundation) | `Core/Runtime/` | - | tag, interfaces, `ServerPolicy`, `SavePassthrough` |
| `KryticalNova.Networking.Entities` (Infrastructure, exists) | `Networking/Entities/Runtime/` | Core, Entities, NetCode | adapter, `KnNetSession`, `GoInGameRequest` + client/server systems, `IKnAdmission`, `KnRulesGroup`, `KnPlayerRefusals`. **No Gameplay reference.** |
| `KryticalNova.Player.Entities.Bodies` (new; Player, Simulation) | `Gameplay/Player/Entities/Bodies/Runtime/` | Core, Player, Player.Entities, Player.Entities.Presentation, Networking.Entities, Entities, NetCode, Transforms | role table, assembler, `ClientBodyFactory`, `ServerBodyMover`, `ServerBodySyncSystem`, `PlayerMoveSettingsBaker`, `BodyWorldTargets`, `GoInGameClientSystem` (needs `CharacterSession`) |
| `KryticalNova.Persistence.Entities` (new; Persistence, Simulation) | `Gameplay/Persistence/Entities/Runtime/` | Core, Persistence, Player, Player.Entities(.Bodies), CharacterCreation, Networking.Entities | `PlayerAdmission`, `PlayerLeaveHandler`, `SaveParticipantSet` stays in `Persistence` proper (the agent calls it) |
Why `Persistence.Entities` holds the glue: Persistence already uses Player; Player cannot reference Persistence without a two-way link, and the admission code needs both `PlayerSaveAgent` and the assembler. Networking stays blind to Gameplay (hook interface `IKnAdmission`, like `ServerHooks`). Each new folder needs its asmdef + tests asmdef (Project organization rule 2); wiring goes in `PlayerAdmissionSetup.Install(serverWorld)` called from the game's server entry, no statics beyond the Core slots.

### 5.3 Steps (each is a small PR with its own tests; a failing step stops the line)

Run column: **E** = EditMode batch (the clone's existing way, ~70 s), **W** = `KnTestWorld` server + clients inside EditMode, **B** = needs real NGO components' Awake chain or a map NavMesh: a built player / PlayMode run (needs Master's OK for the Editor and the Norton exclusions of M2c). `compile_check.py` (+`--server`) and `dependency_map.py --check` after each.

1. **Core seams + adapter + session (E, W).** Tag, interfaces, `ServerPolicy`, `BodyRoleNetRoleAdapter`, `KnNetSession`, `BodyWorldTargets` (players only: `TryFind(Transform)` climbs to the tag root; `TargetId` = `PlayerKey.Value` on the server, `NetworkId` on a client).
   Tests: adapter answers per role/tag/no tag (plain GameObject + `AddComponent<BodyRoleTag>`, no Awake needed); `Of` returns Offline for a non-`IRoleAwareBody` component and the tag role for a fake aware one; `OwnerId` -> serial -> `NetPlayerKeys.Find` -> the key; `DisconnectOwner` queues once and the server system sends `JoinRefusedRpc` + disconnect (W); queue ignores a dead key; statics reset test (domain-reload-free Play).
2. **Role table + assembler (E).** `PlayerRoleTable`, `PlayerBodyAssembler.Create`.
   Tests on the real asset (`AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab")`, `Instantiate` + `DestroyImmediate`, no Awake needed): every component type is classified; after `Create(ServerBody)` the instance is inactive, has the tag, has no Animancer/Animator/PlayerInput/NetworkTransform, still has `PointAndClickPlayer` (disabled) and `NavMeshAgent`; `ClientView remote` has no rule stub; `RequireComponent` order (no `DestroyImmediate` error, no removed type required by a kept one); the Rigidbody ends kinematic. **Awake/Start NREs of a stripped body: B1.**
3. **SaveParticipantSet + passthrough + agent/ServerSave edits (E).** Tests as in section 4, plus: `TryBind` refuses a second agent when the holder's `NetRole.IsNetworked` (fake adapter), hands over when the holder is the offline avatar; `Load` refusal paths do not call the flush. B2 for the real participants.
4. **GoInGame wire + admission slot (W).** `GoInGameRequest` pages, `KnClientGoInGameSystem`, `KnAdmissionSystem`, `KnRulesGroup`.
   Tests: three-page recipe round trip with the largest legal recipe (pure); request accepted once per connection, second one ignored; request from a connection whose key died is dropped; page budget/size caps like `KnApprovalAssembler`'s; fake `IKnAdmission` accepted -> `NetworkStreamInGame` on that connection only; refused -> `JoinRefusedRpc` + disconnect; no `IKnAdmission` installed -> refusal with a reason, not a silent hang. `KnRulesGroup` ordering test: a probe system records `[UpdateAfter(GhostSimulationSystemGroup)]` order relative to `ServerBodySyncSystem` and to a fake RPC receive system [U on group placement].
5. **Server body lifecycle (W, with a stand-in body prefab).** `ServerBodySyncSystem`, `ServerBodyMover`, `PlayerAdmission` (body-first order), `PlayerGhostKey`, `PlayerSpawnService.PickPoint`, `PlayerMoveSettingsBaker`.
   The stand-in prefab is a GameObject with `BodyRoleTag`, a fake `ISaveParticipant`-style set and a fake `PlayerSaveAgent`-like loader behind an `IServerBodyRules` test double in `Persistence.Entities.Tests`; the real prefab is B1/B3.
   Tests: ghost created only after load success (refused load -> zero ghosts, zero bodies, one refusal); event order log: body created -> loaded -> ghost spawned -> InGame; body transform equals ghost every tick, written before a probe "RPC receive" system of the same frame; `Physics.SyncTransforms` called once per frame (counter); `Mover.MoveTo` -> `IssueOrder` -> ghost walks, body follows; `Teleport` bumps `TeleportSeq` and moves both; two players do not share a spawn point; 200 bodies sync under a time budget (measure, no assert on ms in CI).
6. **Disconnect (W).** `PlayerLeaveHandler`.
   Tests (fake `IServerLeaving` that records): order = ghost invalidated -> `OnServerLeaving` -> body destroyed -> ghost destroyed; second close of the same key is a no-op; save runs even when the ghost is already gone; **NetworkId reuse**: client A leaves, client B joins and receives A's NetworkId (assert reuse happened, else Inconclusive), B's ghost is the only one with `GhostOwnerIsLocal`, A's ghost has NetworkId 0 or is gone; deferred-destroy variant (handler splits step 4 over two ticks) keeps the ghost invalid in between; server-stop path with 2 players: both saved before `NetworkStreamRequestDisconnect`; Leaving during an open Trade: `IServerLeaving` of trade runs before the agent (B for the real `PlayerTrade`).
7. **Client view (W with stand-in, B with real prefab).** `ClientBodyFactory : IPlayerBodyFactory`, `PlayerPresentationSetup.Install` call, `BodyRoleTag.IsLocal` flip when ownership settles, `PointAndClickPlayer` ViewMode edits + click queue (`IClickSource` over `Camera.main` ground ray -> `WorldClick`, run flag from the selected movement skill), `PlayerInventory:112`, `NetworkPlacement`, `NavMeshPathPlanner` installed in both worlds (`PlayerEntitiesSetup.Install`).
   Tests: factory makes a local body with `PlayerInput` enabled and a remote body without; `LocalPlayer.Object` is the local body only; `PlayerBodies.All` is the registry list (not clobbered by `PointAndClickPlayer.OnEnable`, checked with a fake `IPlayerBody`); a queued click walks the owner (existing `ClickCommandSource` path) in the W rig; no `X.Local` static stays set on a server (reflection over `TypeCache` types named `Local`, run after step 5's admission with the stand-in; real classes in B1). **B**: camera attaches to "Camera Target", occluder fade, no exception log over 60 s, the offline avatar stays hidden.
8. **Look for observers (W).** `PlayerLook {uint Version}` on the ghost, `LookRequestRpc/LookReplyRpc` (paged), client `LookCache` per version, `PlayerIdentity.ApplyView`. Tests: reply round trip; late joiner gets the look of an existing player; cache hit does not re-request; unknown version shows the default look for at most N ticks; look change bumps the version once. If this slips, ship local-only look and default mannequins for others (flagged R16).
9. **Real-prefab smoke (B, PlayMode in a built player or the Editor with Master's OK).** B1: instantiate Player.prefab as ServerBody, ClientView local, ClientView remote in one scene with a NavMesh plane: no exception/warning from the Awake/OnEnable/Start chain beyond a recorded allow-list (NetworkVariable/NetworkList "not spawned" warnings are expected from offline-style stubs; list them once, assert the set does not grow), `PlayerInventory.Local` set only on the local client body, `LocalPlayer.Object == null` on the server, `agent.enabled == false` everywhere, no physics movement of the Rigidbody. B2: real participants round trip (section 4). B3: the 2-client run below.
10. **2-client gate (B, owner/Master OK, Norton exclusions done).** Windows dedicated test server (7797/8097, test save service 7802): two built clients; both log in, walk, see each other's body and look; kill one client mid-walk; second login of the same account refused with the existing text; disconnect -> server log shows the save before ghost destroy, test DB parts unchanged (passthrough) except `look` and place; Game-view screenshots per step. Then plan section 4 checklist, Encyclopedia (Networking, Player, Persistence status, glossary rows), `graphify update .`.

## 6. Risks, unknowns, owner decisions

### 6.1 Risks / unknowns (with the test that settles them)
- **R1 Awake chain on stripped bodies**: 54 components were never run without NGO spawn and a NetworkManager (`NetworkManager.Singleton == null`). The offline avatar proves the unspawned path exists, but not with a missing singleton or stripped siblings (`GetComponent<X>() != null` checks). Only B1 proves it. EditMode cannot run Awake (it does not for non-ExecuteAlways scripts).
- **R2 Raw-NGO stubs on the ClientView mutate local state** (starter kit granted, wallet earns, items used). That is the intended stub; the banner and the fact that nothing is saved (SaveAgent inert on ClientView, passthrough on the server) keep it safe. The server's inventory and the client's stub inventory will differ until each system is ported. Never show server-wide effects from the stub.
- **R3 `X.Local` pollution on the server** (`LocalPlayer.Object` set by anything): `PlayerInventory.SetLocal` sets `LocalPlayer`. Mitigated because its `OnEnable` is gated on process Offline/local ClientView; B1 asserts null on the server. `PlayerRename`/`PlayerGmSight` `Local` stay null (stubs).
- **R4 Physics colliders are stale after a transform write** (`Physics.autoSyncTransforms` is off): range/hit checks right after `ServerBodySyncSystem` would read last tick's colliders. `Physics.SyncTransforms()` once after the loop (test counts calls; B1 checks an `OverlapSphere` hit after a move).
- **R5 Root offset**: ghost position is the body ROOT (navmesh point + `agent.baseOffset`, as `PlayerSpawnService.PlaceSpawnedPlayer` adds it, L50-L53). `NavMeshPathPlanner` and the follow system must agree on this offset (constant from the prefab agent). Tested in the planner test (flat plane) and B1.
- **R6 Blocking load on the tick (phase A)**: `ServiceSaveStore` HTTP is synchronous (5 s timeout); one login can stall the server tick. Accepted for 2b (same as NGO `OnNetworkSpawn` today); `ServiceReachable()` precheck refuses admissions when the service is down. Phase B (poll a `Task`) later.
- **R7 Group ordering [U]**: `KnRulesGroup` after `GhostSimulationSystemGroup` inside `SimulationSystemGroup`. Test in step 4; if wrong, move the sync to `[UpdateInGroup(PredictedSimulationSystemGroup)] [UpdateAfter(PredictedMoveSystem)]` server-only.
- **R8 `PlayerGhostPrefab` singleton must exist in a shipped scene** (today only a test SubScene): put the ghost-prefab subscene in the Bootstrap scene (always loaded), not in map scenes (S11 only covers map subscenes).
- **R9 `HasComponent<PredictedGhost>` (M3a-2a doubt 1)** decides owner vs observer follow; the 2-client gate is its first real proof.
- **R10 Emerald bridge** (`KNEmeraldPlayerBridge`, `TargetPositionModifier`) find players through NGO collections; hostiles do not run in 2b (no Hostile ghosts), so they are kept inert on the server body. Revisit at M5/M6.
- **R11 Host mode**: Process answers ServerBody when both worlds exist, while the host's own ClientView would still be per-body; unsupported in 2b.
- **R12 Mode drift**: `OfflinePlayerHandoff` (NGO events) never fires on DOTS; the factory and server entry call `ReleaseOfflineAvatar()` explicitly; if a map scene load re-creates the avatar (`SceneNetworkGuard`) it must be hidden again [U].

### 6.2 Decisions for the owner (recommendation first)
- **D1 Spawn order.** (A, recommended) body -> load -> ghost last (refused players never get a ghost). (B) ghost first as in the brief, then body and load; needs an unwind for refusals and a visible ghost for a few ticks.
- **D2 How unported systems run on the client.** (A, recommended) offline-style local stubs behind a banner ("prototype: gear, skills and wallet are local, not saved"); HUD works. (B) strip all rule stubs on the client (HUD `X.Local` null, UI errors). (C) hide the HUD until each system is ported.
- **D3 Save scope in 2b.** (A, recommended) only `look` and the progression level/header are real; every other part is passthrough, flipped one by one with its port. (B) all parts real from day one (risk: a half-working participant overwrites a real save with an empty part). (C) everything passthrough including `look` (a new character could not be saved; not recommended).
- **D4 Click and camera.** (A, recommended) keep `PointAndClickPlayer` for camera/orbit/zoom/selection with a ViewMode (about 50 lines) and send ground clicks to the ghost. (B) extract a new `PlayerClientInput` and retire the class (cleaner, 1,000+ lines moved, conflicts with main's edits to the file). (C) a throw-away click+camera script just for the milestone (two implementations to retire).
- **D5 Host mode in 2b.** (A, recommended) dedicated server + client builds only (Editor client = second client). (B) also a host, which needs two bodies for one player and a `Process` rule.

### 6.3 Checklist for the end of the milestone (CLAUDE.md)
`dependency_map.py --check` (0 uphill, 0 two-way), `compile_check.py` and `--server` (the two new assemblies must be in the list), `PlayerSystemsSetup` unchanged (no new player system),
`GameplayPrefabsSetup.SpawnedPrefabs` untouched (the ghost comes from the subscene), icon check n/a, Encyclopedia rows (Server Body, Presentation Body, Passthrough save as glossary terms), Server Guide note (how a player enters and leaves), 2-client screenshots, `graphify update .`.
