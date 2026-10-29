# DOTS ghost and prediction design (Netcode for Entities 6.6, Unity 6000.6)

Author: nested session "ghosts" for the dots-migration mini-boss, 29 Oct 2026. Read-only on code; no Unity opened.
Inputs read: `dots-00-shared-context.md`, the 20 Oct plan (`i-need-you-to-magical-parasol.md`), the four `prior-survey-*.md`,
and the real code through `graphify explain` + targeted line reads (paths below are under
`C:\Users\dlaco\Krytical Nova II\Assets\_Game\`). The 20 Oct plan was HYBRID; the owner now wants a FULL ECS direction, so
this doc makes simulation, monsters, drops and projectiles ECS-native and keeps only the presentation seams as GameObjects.

## 0. How to read the claims

- **[V]** = verified on the official 6.6 manual/API page named in section 12 (fetched 29 Oct 2026).
- **[V1x]** = found only on 1.x docs or release notes of the same package line (the 6.x line continues 1.x); likely still true, re-check at first compile.
- **[U]** = unverified (my reading of older material, forum, or inference). Every [U] is listed again in section 12.1 as a spike item.
- Names `PlayerKey`, `NetRole`, `MapAnchor`, `ServerOrder` come from the 20 Oct plan and are reused.

Two corrections to the earlier surveys, found while verifying:

1. `prior-survey-ae874621` item 10 says GhostFields cannot hold FixedLists. The 6.6 "ghost types" page lists
   `FixedString32..4096Bytes` and `FixedList32..4096Bytes<T>` as supported replicated types [V, S2]. So `CharacterNetState`
   *can* be a ghost component. It is still the wrong thing to put in every snapshot (about 300 bytes of look data), so the
   `LookVersion` + cached look decision (section 3.1) stands, for bandwidth, not for capability.
2. The 20 Oct plan said "No Entities Graphics". Superseded by the owner's 29 Oct direction (section 7.3).

---

## 1. Design principles (the ten rules everything below follows)

1. **One simulation, two worlds.** The same Burst/ISystem code runs in `PredictedSimulationSystemGroup` on server and client
   [V, S10]. Anything that must not repeat during rollback (NavMesh API, PhysX queries, spawning, VFX, save, RPC send) lives
   *outside* the predicted group or behind `NetworkTime.IsFirstTimeFullyPredictingTick` [V, S10].
2. **State is integers, positions are derived.** Prediction code integrates integers (path progress in millimetres, HP and
   Chi in hundredths, ticks as `NetworkTick`) and derives floats from them. Float drift between a Windows client and a Linux
   server then cannot accumulate or flip a branch (section 3.2.6).
3. **The server decides, the owner predicts only what it can undo.** The owner predicts movement, motion start, a cast
   start and its Chi/cooldown, a swing start. It never predicts a hit, damage, loot, XP, a trade or a kill.
4. **Observers only present.** Everything not owned is an interpolated ghost. No game rule runs on an interpolated ghost.
5. **One-off moments are ring entries with a tick and a sequence**, never a bool that flips (section 2.6).
6. **Private state rides owner-only components** (`OwnerSendType = SendToOwner` [V, S4]); large private state rides
   versioned page RPCs (menus), as in the 20 Oct plan.
7. **No NavMesh, PhysX or managed object inside the client's resimulated loop.** Paths are planned once per order, outside it.
8. **Data stays ScriptableObjects at the edit side, blobs/singletons at runtime.** Skill timings, strike windows, movement
   settings, mount speeds bake into `BlobAssetReference` singletons in the ghost subscene. Numbers stay in the settings assets
   (CLAUDE.md), never in systems.
9. **Layering holds** (CLAUDE.md): Core < Networking/Rendering < Simulation systems < Presentation < Tools. Each Simulation
   system owns its components and ghost prefab singleton in its own assembly; Networking only owns bootstrap, `PlayerKey`,
   `NetRole`, the type-agnostic `PlayerGhostPrefab` and the ghost-to-GameObject registry. Presentation bridges (section 7)
   live in Presentation assemblies and read ghost components; they never write them.
10. **Build only what is asked.** Housing, NPC wandering and shared Halls stay as they are today (section 6).

---

## 2. Worlds, ticks and system-group order

### 2.1 Worlds

- `KnBootstrap : ClientServerBootstrap` (20 Oct plan, section Stage 2): player build = client + server (a player can host);
  dedicated build = server world only; worlds are created on Connect/Host, destroyed on travel. Roster/menu use a
  short-lived client world.
- `ClientServerTickRate`: `SimulationTickRate = 30`, `NetworkTickRate = 30` (today's NGO tick 30, `NetworkManager.prefab:58`;
  server frame cap 30 in `DedicatedServerOptimizer.cs:14`). `NetworkTickRate < SimulationTickRate` would round-robin connections [V, S1], so keep them equal.
  `MaxPredictAheadTimeMS` default is kept (it caps the accepted ping; higher costs CPU) [V, S9]; test at 250 ms.
- Connection flow [V, S13]: `RequireConnectionApproval = true` both sides; `IApprovalRpcCommand` carries token chunks;
  server adds `ConnectionApproved`; `NetworkId` is assigned only after approval; `HandshakeApprovalTimeoutMS` default 5000
  must be raised because Unity-token verification is async (`UnityKeys.EnsureAsync`, `NetworkConnectionManager.cs:600-610`).
  `NetworkStreamInGame` is added by our `GoInGameServerSystem` (never automatic) [V, S13].
- `PlayerKey` (monotonic uint, stored in a `ConnectionPlayerKey` component on the connection entity). Never the raw
  `NetworkId` (reuse risk, review item 9; I could not confirm id reuse in the 6.6 text -> [U], design is safe either way).

### 2.2 System groups and order (concrete)

Server world and client world share the groups; `[WorldSystemFilter]` splits the members.

```
InitializationSystemGroup
  (client) NetworkReceiveSystemGroup -> GhostReceiveSystem / GhostUpdateSystem apply snapshots (package)   [V, S10]
SimulationSystemGroup
  GhostInputSystemGroup                      (client only; package group for input gather)                [V1x]
    PlayerInputGatherSystem   (managed)      Input System + camera raycast -> PlayerInput (+ ClientPlan, once per click)
  KnPreSimulationGroup  (ours, [UpdateBefore PredictedSimulationSystemGroup])
    ServerRpcReceiveSystems   (server, managed, one per gameplay system; resolve PlayerKey, RpcBudget)
    ServerBodySyncSystem      (server; only needed while GameObject rule code still reads transforms, see 7.5)
    RelevancyFillSystem       (server, Burst job; section 2.4)
  PredictedSimulationSystemGroup             (package; runs on server once/tick, on client N times after rollback) [V, S10]
    PlayerMoveOrderServerPlanSystem (server-only, managed)   NavMesh/PhysX planning for an order that starts THIS tick
    PlayerOrderStartSystem          (Burst)  consumes input events + ServerOrder -> MoveState / MotionPlan
    PlayerMoveSystem                (Burst)  advances path progress, writes LocalTransform
    PlayerMotionSystem              (Burst)  jump / vault / wall-run / waterstride / death plans as f(tick)
    PlayerSkillCastSystem           (Burst)  cooldown, Chi, cast state
    PlayerMeleeStateSystem          (Burst)  combo step and swing window by ticks
    VitalsRegenSystem               (Burst)  integer Chi/HP regen
    MountPhaseSystem                (Burst)
    -- server only, still inside the group (server never re-simulates):
    SkillAreaResolveServerSystem / MeleeResolveServerSystem  (lag-compensated physics queries, section 4.4)
    DamageApplySystem               (server, writes Vitals, appends HitEvent)
  PredictedFixedStepSimulationSystemGroup   (Unity Physics moves in here)                                [V, S6]
  GhostSimulationSystemGroup                (server: GhostSendSystem; client: spawn/classify)             [V, S12]
  KnPostSimulationGroup
    HostileBrainSystem / DropSystems (server)
    ServerBodySyncSystem (reverse, if kept)
PresentationSystemGroup  (client; managed)
  GhostViewFollowSystem        ghost LocalTransform -> presentation body Transform
  FxEventPresentSystem         HitEvent / FxEvent rings -> ClientPresentationRegistry
  AnimatorFeedSystem           MoveState / MotionPlan / CombatState -> Animancer, MotionWarping
```

Rules enforced by this order:
- Anything planned for a tick (corners, plans) exists before `PredictedSimulationSystemGroup` runs that tick.
- The server resolves hits *after* the predicted movement of the same tick, against the history world [V, S6 for history; ordering is my design].
- Physics: `NetCodePhysicsConfig.EnableLagCompensation = true` and `PhysicGroupRunMode = LagCompensationEnabledOrKinematicGhosts` [V, S6].
  Netcode moves `PhysicsSystemGroup` into `PredictedFixedStepSimulationSystemGroup` [V, S6]; the client runs no
  rigid-body simulation for players (they move by our systems), so only the hit colliders matter.

### 2.3 Ghost authoring rules used throughout

- `GhostAuthoringComponent` per ghost prefab: *Supported Ghost Mode* + *Default Ghost Mode* (Interpolated / Predicted / Owner predicted) [V, S1]. Owner predicted needs `GhostOwner` with `NetworkId` set [V, S1].
- `[GhostField(Quantization = N)]` multiplies and rounds floats; N=100 means 0.01 precision; 0 = full precision; default for floats is unquantised [V, S3, S2]. Ints are never quantised. `Composite = true` gives one change bit to a whole struct [V, S3].
- `[GhostComponent(PrefabType, OwnerSendType, SendTypeOptimization)]` selects which prefab flavour has the component and who receives it [V, S4]. `SendDataForChildEntity` defaults off because it is costly [V, S4], so **no replicated data on child entities**: the player ghost is a flat entity.
- `GhostEnabledBit` replicates `IEnableableComponent` state [V, S1]: used for `Dead`, `InCombat`, `Stunned`.
- Dynamic vs Static optimisation per ghost [V, S1, S5]. Static ghosts are not sent when unchanged but force `UseSingleBaseline`, cannot be ghost-group members and cannot extrapolate [V, S5]. Static: drops, kiosks, world singletons, the dummy.
- `MaxSendRate` is a pre-pass keeping a chunk out of the priority queue that tick; it does not change importance; spawns and despawns bypass it [V, S5]. It is a ceiling, not a guarantee [V, S1].
- A ghost that becomes irrelevant is **destroyed on the client** [V, S5]. Death must therefore be a state (`GhostEnabledBit Dead`), and presentation must tolerate destruction while visible (pool return).

### 2.4 Relevancy and importance (replaces `SpatialInterestManager`)

`SpatialInterestManager` (`Networking/Runtime/SpatialInterestManager.cs`) does clients x objects every 0.5 s. Replacement:

- **Importance**: `GhostImportance` singleton with `GhostDistanceImportance.Scale` (or the batch variant), `GhostDistanceData{TileSize=48 m, TileCenter, TileBorderWidth}`, `GhostDistancePartitioningSystem` places chunks into tiles, and each connection entity carries `GhostConnectionPosition` updated every tick from the player's `LocalTransform` [V, S5]. Per-chunk, not per-ghost [V, S5]. Base importance per ghost type from `GhostAuthoringComponent.Importance`: player 100, hostile 60, drop 10, kiosk 5.
- **Relevancy**: `GhostRelevancy` singleton, mode `SetIsRelevant` (nothing replicates unless in the set); `DefaultRelevancyQuery` marks always-relevant chunks (world clock, weather, music, zone rules); `GhostRelevancySet` entries take precedence [V, S5]. 6.x adds `PrioChunk.isRelevant` so relevancy folds into the distance batch function (`BatchScaleWithRelevancy`) [V, S5]. That is the fast path to use: relevance = "tile within radius R (default 96 m, war-map preset 64 m) of the connection", computed once per tile per tick, not per pair. This is the Stage 8 design of the 20 Oct plan made concrete; Stage 8 stays the *measurement* stage, but the tile data must exist from the first ghost so it is never retrofitted.
- Rules that need to bypass distance: the player's own ghost (owner), the player's party members' *vitals* (not their bodies, see 3.1: carried in an owner-only `PartyView` buffer so a party member on another map/line is covered too), duel opponent (always relevant while a duel runs), the target a player is attacking (relevant while targeted; set in `RelevancyFillSystem`).
- Hysteresis: irrelevant ghosts are destroyed on the client [V, S5], so use an enter radius of R and a leave radius of R + 16 m to avoid pop-in/out at the edge.

### 2.5 Send rates (first numbers, to be set by Phase 7 measurement, in `NetworkSettings`/`GhostSendSettings` ScriptableObject)

| Ghost | Mode | MaxSendRate | Notes |
|---|---|---|---|
| Player (own) | Owner predicted | 30 | Importance max; must hit every tick |
| Player (others) | Interpolated flavour of the same prefab | 30 moving, relevance by tile | Same prefab; N4E delivers the interpolated view automatically for non-owners [V, S1] |
| Hostile | Interpolated | 15 | Idle Hostiles: Static-like by sending `LocalTransform` only on change; 15 Hz is enough at 10 m/s with 150 ms interpolation |
| Combat dummy | Interpolated, Static | n/a | Vitals changes only |
| WorldDrop | Interpolated, Static | n/a | Spawn once, change on pickup hold |
| KioskStand | Interpolated, Static | n/a | Listings change on sale |
| Zone singletons (clock, weather, music) | Interpolated, Static | n/a | Changes on weather change / beat grid change |
| Skill projectiles (if any) | Predicted spawn on owner, interpolated for others | 30 | None exist today; area skills are instantaneous (section 4) |

### 2.6 One-off events as rings

Today these are ClientRpcs (`HostileNet.StrikeClientRpc` L505, `AttackClientRpc` L562, `HitClientRpc` L573,
`PlayerMeleeCombat.PlayComboStrikeClientRpc` L509, `SkillHitVfxClientRpc`, `PlayerPet` `SendTo.Everyone` L530/L567).
Replacement:

```csharp
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct FxEvent : IBufferElementData {           // InternalBufferCapacity(8), one ring per ghost
    [GhostField] public NetworkTick Tick;               // server tick the thing happened
    [GhostField] public byte Seq;                       // wraps at 256; the ring slot is Seq % 8
    [GhostField] public byte Kind;                      // HitSmall, HitCrit, Evade, Strike1..3, SkillCast, Death, PetTrick...
    [GhostField] public ushort Arg;                     // skill id hash low bits, damage tier, pet trick id
    [GhostField] public int Other;                      // ghost id of the other party (target / attacker), 0 = none
}
public struct FxEventCursor : IComponentData { public byte LastSeq; public NetworkTick LastTick; } // client-only, NOT a ghost field
```

- Server `AppendFx(ref DynamicBuffer<FxEvent>, kind, arg, other)` bumps a per-ghost `FxSeq` byte (a ghost field on `FxState`).
- Client `FxEventPresentSystem` (presentation group, managed): for each ghost, play every ring entry with `Seq` newer than `FxEventCursor.LastSeq` (wrap-aware), but **for interpolated ghosts only once `InterpolationTick >= entry.Tick`** (the effect then lines up with the interpolated animation); for the owner's own ghost, play on arrival.
- Dedup under rollback: the ring is plain replicated data, the cursor is not rolled back, so a resimulation cannot replay an effect [by construction]. Predicted-only cosmetics (our own swing start) are guarded by `IsFirstTimeFullyPredictingTick` [V, S10].
- Capacity: 8 entries at 30 Hz snapshots covers ~270 ms of loss at one event per tick. A boss tick with more than 8 hits in a tick aggregates into one `HitFlurry` entry (`Arg` = count). If an event is *gameplay-significant to one player* (loot received, XP, level-up, quest) it is an owner RPC (reliable [V, S8]) or an owner-only component change, never a ring.
- Because a ghost that is irrelevant is destroyed, rings never reach a player who could not see the thing. That is the relevancy-gated behaviour the 20 Oct plan wanted ("relevancy decides who receives it").

---

## 3. Movement under prediction and rollback

### 3.1 The player ghost (owner predicted)

Source today: `Player.prefab`, 54 NetworkBehaviours on one NetworkObject (`prior-survey-a9a9a0fb`), `NetworkTransform` stock
(server authority, interpolate, full float, scale synced). One flat entity `PlayerGhost` baked from a **ghost authoring
prefab with no GameObject children replicated**.

**Replicated to everyone (interpolated for observers, predicted for the owner)**

| Component | Fields (GhostField, quantisation) | Replaces |
|---|---|---|
| `LocalTransform` (package component, Position + Rotation only, **no scale**) | Position Q=100 (1 cm); Rotation as `Yaw` below instead of quaternion | NetworkTransform |
| `PlayerYaw` | `ushort Yaw` (0..65535 = 360 deg), Smoothing=Interpolate | rotation |
| `MoveState` | `byte Flags` (Moving, Run, OnMotion, Sitting...), `ushort SpeedMmps`, `ushort OrderSeq`, `byte TeleportSeq` | `agent.velocity`, `MoveOrderShownClientRpc` |
| `Vitals` | `int Hp, MaxHp, Chi, MaxChi` (hundredths), `byte Level`, `byte Flags` | `PlayerResources.netHealth/netChi/netMaxHealth/netMaxChi` (L28-34), level from `PlayerCharacterProgression` |
| `FactionMember` | `byte Faction` | `IFactionMember` |
| `GhostEnabledBit` tags | `Dead`, `InCombat`, `Stunned`, `OnWater` | `PlayerCombatState.netInCombat` (L51), `PlayerWaterstrideMotor.netOnWater` (L40), death pose |
| `MotionPlan` | see 3.3 | jump/vault/wall-run/death `ClientRpc`s and `WallRunPose` (PlayerWallRunMotor L126), `PlayerDeathMotor.netPose` (L107) |
| `CombatState` | `byte Step (0..3)`, `NetworkTick StepStartTick`, `byte SkillSlotAnim`, `NetworkTick CastStartTick` | `PlayComboStrikeClientRpc`, `TriggerSkillCastClientRpc` |
| `DuelState` | `uint OpponentPlayerKey` (0 none) | `PlayerDuel.netOpponent` (L34) |
| `LookVersion` | `uint Version` | `PlayerIdentity` NetworkVariable<CharacterNetState> (`PlayerIdentity.cs:32`) |
| `EquipVisual` | worn gear ids (ushort x ~12) packed; `Weapon class` | `PlayerEquipment` public part |
| `MountPublic` | `byte Selected, Phase, Growth, bool Fed`, `NetworkTick PhaseEndTick`, `ushort WalkMmps, RunMmps` | `MountNetState` (`MountNet.cs:18`) |
| `PetPublic` | `byte Growth, bool Fed, ushort MorphId` | `PetNetState` (`PlayerPet.cs:17`) |
| `FxState` + `FxEvent` ring | section 2.6 | all one-off ClientRpcs |
| `GhostOwner` | `NetworkId` | NGO ownership |

Quantised numbers: positions Q=100 are 1 cm. At the top mount speed of 34 m/s the body moves 1.13 m per tick, so
interpolation between snapshots is long strides; this is fine for rendering (the animation is driven by `SpeedMmps`), but
it dictates the corner-follower design in 3.2.

**Owner-only (`OwnerSendType = SendToOwner` [V, S4])** (private and small, per the plan's state placement rule)
- `PathCorner` buffer (3.2), `ClientOrderEcho`.
- `SkillCooldown` buffer: `{ uint SkillKeyHash, NetworkTick EndTick }`, capacity 16 (today `cooldownEndTimes` in `PlayerCombatSkills` L43, keyed by `RankKey`).
- `SkillRanks`: today `NetworkList<byte> netRanks` (L93, owner) + `netUnlockedBooks` ulong (L37). Becomes a `FixedList128Bytes<byte>`/bitfield component, SendToOwner (FixedList is a supported ghost type [V, S2]).
- `MovementSkillOwn`: selected movement skill, ranks (`PlayerMovementSkills.netState` L31).
- `MountBook` (`MountNet.cs:42`, 9 uint), `PetBook` equivalent.
- `PartyView` buffer `{PlayerKey, Hp%, Level, MapId, Online}` x5 (party max 5, memory `party-system-decisions`): carries party frames across maps and servers; **party members' bodies are not made relevant**.
- Small economy numbers shown in the HUD (Creds) *do not* ride a ghost: `PlayerWallet` is saved and ledgered; its value is sent by a rate-limited owner RPC page (`WalletViewRpc`) on change. Reason: `CLAUDE.md` says every Cred goes through `PlayerWallet`/`EconomyLedger`; keeping it out of the snapshot also keeps the economy independent of the netcode (and of its rollback).

**Not ghosts, not components of the player ghost (RPC "view" pages, menu systems, section 6.4 of the 20 Oct plan)**: inventory,
vault, loadouts, trade, kiosk, crafting, social lists, chat, contacts, crew. Their server logic stays plain managed C# on the
server body / services; their transport becomes `IRpcCommand`s keyed by `PlayerKey`.

**Look**: observers need parts and colours of every visible player. Decision: `LookVersion` on the ghost; the look
itself (`CharacterNetState`, ~300 B incl. `FixedList128Bytes<ushort> parts` + colours) is sent by a **LookRequestRpc/LookReplyRpc** pair and cached per `LookVersion` in a client-side `LookCache` (managed). A late joiner sees a default mannequin for ~1 frame, then the real look. Alternative (rejected): ghost field, because a crowd of 100 would add 30 KB to the initial burst and every re-baseline.

**Vital precision**: HP/Chi as `int` hundredths. Reason: Chi is spent and regenerated inside prediction (run cost, skill cost); integer math has no drift (rule 2). `CombatCalculator` is float-based today; the server converts its float result to hundredths once when applying damage (`DamageApplySystem`), so the calculator stays untouched ("Untouched" in the 20 Oct scope table).

### 3.2 Point-and-click movement

**Facts from the code (read, not guessed)**

- The click is `RequestMoveDestinationServerRpc(Vector3)` (`PointAndClickPlayer.cs:1131`), the server calls `ServerMoveTo`
  (L1249): `NavMesh.SamplePosition` (destination), `TryPlanRoute` (L1480: `agent.CalculatePath`, wall-run entry `TryArmSurfaceRoute`,
  `TryArmTowards`, drop `TryPlanDrop`, vault `TryArm` which re-plans to a take-off point), pays Run Chi through
  `movementSkills.TryPayForCommand` (refund with `resources.RestoreChi` if `SetPath` fails), then `agent.SetPath`.
- The agent's speed is `movementSkills.CurrentSpeed` (`PlayerMovementSkills.cs:45`) times an arrival factor
  `settings.ArrivalSpeedFactor(remaining, speed)` (`MovementSettings.cs:90`: eases to `arrivalMinSpeedFactor` over an arrival
  radius), with `agent.acceleration = max(settings.acceleration, speed * accelerationPerSpeed)`, `angularSpeed = turnSpeed`,
  `stoppingDistance` (`PointAndClickPlayer.cs:1841-1853`). Run ranks reach 29 m/s; mounts 34 m/s (`MountSettings.runSpeed`).
- The owner today walks the same route locally (`LeadOwnWalk` L1157) and `PlayerMoveAnticipation` (L20, execution order -200)
  eases onto the server's NetworkTransform with `snapDistance` 3 m + speed x (RTT + 0.35 s), `easeSeconds` 0.2.
- Server-issued walks: `PlayerSkillQueue.cs:242`, `PlayerFollow.cs:213,242`, `PlayerHousing.cs:270,555`, `BenchCommands.cs:390`, `GuideClipStaging.cs:286`.
- Teleports: `NetworkPlacement`, raw `agent.Warp` at `ZoneTravel.cs:162`, `PointAndClickPlayer.cs:1469,1615`.
- Avoidance: `Player.prefab:302 avoidancePriority: 50` (NavMeshAgent crowd avoidance exists between agents on the server). [U] whether it visibly changes
  player-player paths; N4E prediction cannot include other players' positions without making those predicted. Decision: **no player-player avoidance** (players pass through each other's capsule), same as most MMOs; verify on the server today by test, flag to owner if a behaviour change is seen.

#### 3.2.1 What replaces the NavMeshAgent

The agent does three jobs: (a) path query, (b) following corners with accel/arrival/turn, (c) staying on the NavMesh. N4E
replaces (b) and (c) with `PlayerMoveSystem` on a stored polyline; (a) stays on the NavMesh API (managed, main thread) but is
moved *out of rollback*.

```csharp
public struct MoveState : IComponentData {
  [GhostField] public byte Flags;            // Moving, Run (paid), Arriving
  [GhostField] public ushort SpeedMmps;      // current speed (integer)
  [GhostField] public ushort OrderSeq;       // wraps; identifies the active order
  [GhostField] public NetworkTick OrderStartTick;
  [GhostField] public uint ProgressMm;       // arc length travelled along the polyline, integer millimetres
  [GhostField] public byte CornerCount;      // active corners in PathCorner
  [GhostField] public byte TeleportSeq;      // bumps on every placement
}
[GhostComponent(OwnerSendType = SendToOwner)]
public struct PathCorner : IBufferElementData { [GhostField(Quantization = 100)] public float3 P; }  // InternalBufferCapacity(0..24), cap 32
public struct ClientPlanCorner : IBufferElementData { public float3 P; }   // client-only, NOT replicated, NOT rolled back
public struct ClientPlanHeader : IComponentData { public ushort Seq; public byte Count; public float3 Goal; } // client-only
```

Buffer ghost fields: the 6.6 types page documents fixed-list fields inside buffer elements (capped at 64) [V, S2] and says
prediction-error reporting does not support `DynamicBuffer` [V, S2]; replicated buffers themselves are a long-standing
feature but the 6.6 page text I fetched does not show a buffer-with-GhostField sample -> **[U] spike item S1** (add a
`PathCorner` buffer to a test ghost, confirm it replicates and rolls back).

Path length cap: NavMesh corners for a click are usually < 12; the cap is 32. A longer path is truncated server-side to the
first 32 corners and the order "continues" automatically: when `ProgressMm` reaches the end and `OrderFlags.Continue` is set,
`PlayerMoveOrderServerPlanSystem` re-plans from the end to the stored goal (this mirrors `ReplanOrder` L1319).

#### 3.2.2 Inputs

```csharp
public struct PlayerInput : IInputComponentData {          // payload limit 1024 bytes [V, S7]
  public float3 ClickGoal; public ushort ClickSeq; public InputEvent Click;   // InputEvent: delivered exactly once [V, S7]
  public InputEvent Stop, Jump, Ride, Cancel, WallClick;
  public byte SkillSlot; public InputEvent CastSkill; public int TargetGhostId; public InputEvent BasicAttack;
  public float3 JumpGoal; public float3 WallGoal; public byte MovementSkillSelect;
}
```

- Redundancy: the client resends the last `n` (default 4) commands every tick, delta-compressed, packets sent once per tick on the first partial tick [V, S7].
- `ClickGoal` is the raw ground point from the camera ray; the server re-snaps it with `NavMesh.SamplePosition` (the existing `destinationSampleRadius`).
- `TargetGhostId` is the target's `GhostInstance` id; the server resolves it through `SpawnedGhostEntityMap` [U; name from 1.x: verify spike S2].
- `RpcBudget` for input: input commands bypass `RpcBudget`; the equivalent guard is a server system that counts `InputEvent`s per second per connection and disconnects on flood (`SecurityFlags` stays).
- Replaced RPCs: `RequestMoveDestinationServerRpc` (L1131), `PlayerJumpMotor.cs:145` jump request, wall-run `PlayerWallRunMotor.cs:255/378` and `WallWalk.cs:106`, stop `PointAndClickPlayer` L898 and L1672/1681, `PlayerSkillQueue.QueueServerRpc` L152, `PlayerCombatSkills.RequestCastSkillServerRpc` L770, `PlayerMeleeCombat.RequestMeleeStrikeDamageServerRpc` L709 (deleted outright, section 4), `PlayerMounts.RideRpc` L166, `PlayerMovementSkills` L259/L302 (selection stays an RPC: it is a setting, not a per-tick action).

#### 3.2.3 Where a path is planned without the NavMesh API inside rollback

The rule: **the predicted loop never calls the NavMesh API; it consumes corners already in a buffer.**

*Client (the owner):* in `PlayerInputGatherSystem` (managed, `GhostInputSystemGroup`), when the player clicks:

1. Raycast the ground (as `PointAndClickPlayer` does now with `raycastLayers`).
2. Run the existing planners once: `NavMesh.SamplePosition` + `NavMesh.CalculatePath` with the player's `NavMeshQueryFilter{agentTypeID, areaMask}` (static API; works without an agent [U: the vault motor already uses the static `NavMesh.CalculatePath(landing, destination, areaMask, path)` at `PlayerVaultMotor.cs:246`, so the pattern exists in this code base]). Include `WouldVault`/`TryArm` take-off logic by calling the same pure planning helpers (extracted from `TryPlanRoute`).
3. Write `ClientPlanCorner` + `ClientPlanHeader{Seq=n}`, set `PlayerInput.Click` and `ClickSeq = n`.
4. Pre-check what the client can: `CanPayForCommand(Run)`, alive, not on a motion. If the check fails, do not send the click (matches `LeadOwnWalk` L1165).

*Both worlds, inside the predicted loop:* `PlayerOrderStartSystem` (Burst) sees `Click.IsSet` at tick T in the input
buffer for `ClickSeq = n`:
- It sets `MoveState.OrderSeq = n`, `OrderStartTick = T`, `ProgressMm = 0`, `Flags |= Moving`, pays Run Chi (integer) in `Vitals`.
- Corner source: **if `PathCorner` (server-replicated) carries `OrderSeq == n`, use it; else use `ClientPlanCorner` with `Seq == n`.**
  The server always has its own (it planned it in `PlayerMoveOrderServerPlanSystem` earlier in the same tick), so on the server
  `PathCorner` is always the source.

*Server only, inside the group (never resimulated):* `PlayerMoveOrderServerPlanSystem` (managed, `[WorldSystemFilter(ServerSimulation)]`,
`[UpdateBefore(typeof(PlayerOrderStartSystem))]`) reads the same `Click` event for the current tick, runs the same planner
helpers (with the authoritative `vault/wall-run/drop` choices), validates (alive, no motion, `PointFits`-equivalent range
check, Chi), writes `PathCorner`, and if it refuses it clears the `Click` consumption so the start system does not start the
order (the existing `MoveRefusedRpc` becomes a `MoveRefused` `FxEvent` + toast text kind). A refusal needs no rollback code: the
next snapshot shows `OrderSeq != n`, the client resimulates without the order, the character stops. That is today's
`StopLeading()` for free.

*Why the plan is not lost on rollback:* the order *start* is derived from the **input buffer**, which persists on the client
and is re-read on each resimulated tick; the corner data lives in the client-only `ClientPlanCorner` buffer which rollback
does not touch (it only restores ghost-field data from snapshots [V, S10: "only ghosts that received updates are rolled back"; the restore
applies ghost fields], to be proved in spike S3). A snapshot older than the click restores `MoveState` to "not moving, OrderSeq old";
the resimulated tick T then re-applies the click from the input buffer. This is the key correctness argument; it is
spike S3's pass criterion ("click, then drop 3 snapshots, character keeps walking").

*Path divergence:* when the server's `PathCorner` arrives with different corners (different NavMesh state, vault take-off), the owner
gets a rollback with the server polyline; positions snap by the difference (Section 3.2.7 smooths it).

#### 3.2.4 Following the polyline (the part that replaces the agent)

`PlayerMoveSystem` (Burst, `[BurstCompile(FloatMode = Deterministic, FloatPrecision = High)]`):

```
speedTarget  = MoveState.Flags.Run ? stats.RunMmps : stats.WalkMmps           // ushort, from MountPublic/MovementStats, server-written
remainingMm  = totalLengthMm(corners) - ProgressMm
arrival      = arrivalFactorTable[ clamp(remainingMm / arrivalRadiusMm(speedTarget)) ]   // baked lookup in the MovementSettings blob (replaces Mathf.Sqrt/Lerp)
speedWanted  = speedTarget * arrival
speedMmps   += clamp(speedWanted - speedMmps, -decel*dt, +accel*dt)             // all integers; accel = max(settings.acceleration, speed*accelerationPerSpeed) in mm/s^2
ProgressMm  += speedMmps * tickMicros / 1_000_000                                // integer divide, deterministic
position     = PointAtArcLength(corners, ProgressMm)                              // pure function of (corners, ProgressMm)
yaw          = TurnToward(yawNow, tangent(corners, ProgressMm), turnSpeed*dt)     // yaw in 1/100 degree integers
arrived      = remainingMm <= stoppingMm  -> Moving=false, SpeedMmps decays to 0
```

Properties that matter:
- `PointAtArcLength` walks the polyline: at 34 m/s the progress per tick is 1133 mm, so several short segments can be crossed in one tick; the function is a loop over corners from a cached segment index in `MoveState`, not an Euler step, so it cannot overshoot corners.
- `arrivesThisFrame` (`MovementSettings.cs:104`) disappears: it was a fix for a float step overshooting the stopping point; with arc-length progress the arrival test is exact.
- Corner Y comes from the NavMesh corner Y (agents add `baseOffset`; `PlayerOrderStartSystem` adds the base offset to each corner once), so no ground query exists in the loop. Slopes: linear interpolation between corners; the NavMesh polyline is already on the surface at its vertices, edges are straight lines on the navmesh triangles. Visual foot placement stays `FootGroundingIK` in the presentation body (it already raycasts PhysX itself).
- Same system runs on the server (`Simulate` tag filter, one tick), on the owner client (resim), and on the observer clients **not at all** (interpolated ghosts have no prediction; the interpolated `LocalTransform` is shown).
- Speed stats: `WalkMmps/RunMmps` are written by the server from `MovementSettings` + ranks + mount (`MountRules.Speed`, `PlayerMovementSkills.SetRideSpeeds`) + GM speed multiplier (`PlayerGmState.SpeedMultiplier`), and replicated. The client never recomputes them from item data in the loop. Stat changes from gear take effect at the next snapshot (an equip is itself a server action).
- Stall watching (`WatchForStall`, L1344): server-only managed system compares `ProgressMm` growth; with exact kinematic motion a stall can only come from an external blocker, which we do not simulate. Kept as a cheap re-plan trigger only for `Continue` orders. Likely deleted.

#### 3.2.5 Server-issued walks (skill walk-in, Follow, housing, bench, guide staging)

`ServerOrder` replaces the five call sites by one writer API (the 20 Oct Stage 1 seam: "Server-issued walks through one call"):

```csharp
public struct ServerOrder : IComponentData {                // predicted, replicated
  [GhostField] public ushort Seq;                           // separate sequence space from ClickSeq (high bit set)
  [GhostField] public NetworkTick StartTick;
  [GhostField(Quantization = 100)] public float3 Goal;
  [GhostField] public byte Flags;                           // RunPaid, Continue, KeepFacing
}
```

- Server system (managed) plans like a click and fills `PathCorner`, then writes `ServerOrder{Seq, StartTick = next tick}`. `PlayerOrderStartSystem` treats `ServerOrder.Seq` change exactly like a click event of that seq. The Run Chi is paid by the server at issue time and flagged `RunPaid` (the 20 Oct plan: "run/walk decision stored in the order, not predicted").
- The owner would otherwise predict "standing still" for RTT and get corrected: for the **skill walk-in** (the only latency-sensitive one) the client also runs `PlayerSkillQueueClientPlan` on the `CastSkill` event, writes a `ClientPlan` for the same skill/target/position (deterministic function of positions and skill range, `PlayerSkillQueue.cs:242`), tagged with the next `ServerOrder` seq it expects (`LastServerOrderSeq + 1`). On mismatch the server's order wins (rollback). Follow, Housing, Bench, Guide: not anticipated (they are not latency-sensitive; they appear after RTT).
- Follow (`PlayerFollow.cs:213,242`) re-issues a `ServerOrder` each time the followed player moves > 2 m; with 5-player parties at most 4 orders per second per follower, bounded and cheap. To avoid snapshot spam the follow target position goes in `FollowState{TargetPlayerKey}` and the **follower's own `PlayerMoveSystem`** steers (a `FollowSteerServerSystem` re-plans through the NavMesh every 0.5 s server-side, not per tick).

#### 3.2.6 Windows client vs Linux server: how float drift is neutralised

Findings [V, S14]: Burst floats default to `FloatMode.Strict`; `FloatMode.Deterministic` makes results consistent
**across all supported platforms**, only on 64-bit, flushes subnormals to zero, uses deterministic math functions, disables
rearrangements, and **does not make non-Burst inputs deterministic**; NaN occurrence but not bit patterns are guaranteed; it may cost performance; platform
intrinsics break it. The page does not list per-function bit-identity between Windows x64 and Linux x64 [V, S14 gap] and N4E is
not a deterministic package (shared context). Therefore the design does not *depend* on bit identity:

1. **Integer state** (rule 2): `ProgressMm`, `SpeedMmps`, HP/Chi hundredths, yaw 1/100 deg, ticks. All branching conditions
   (corner reached, arrived, cooldown ready, Chi enough) are integer comparisons. A float is only a *derived output* (`position = f(corners, ProgressMm)`).
2. **`[BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]`** on every predicted system and job, with
   `Unity.Mathematics` only (no `System.Math`, no platform intrinsics, no `Mathf`). Corners are the only float inputs to the polyline function; they are quantised (Q=100 -> centimetre) *on both sides* because the client's own
   plan corners are rounded to 1 cm (`math.round(p*100)/100`) before use, matching what the server's replicated copy will be.
3. **Corner data are replicated** (owner `PathCorner`), so when the two planners differ by one float ulp the client adopts the
   server's data; the only thing both sides compute from the same bytes is a handful of adds and multiplies of at most ~10 corners.
4. **Same Burst + Unity + Mathematics version** on client and server builds (build pipeline; the Linux server build is made
   by the same Unity version, shared context). Add a CI check that `Packages/packages-lock.json` is identical for both targets (trivially true, one project).
5. **Server correction as backstop**: N4E restores the server state on every snapshot of a predicted ghost and resimulates [V, S10]; a residual float error of 1e-6 m is
   invisible and never accumulates because the next snapshot resets the integer state. Large divergence (> 1.5 m) is a bug:
   `MoveDivergenceLogger` (dev builds) writes the error and a hash of `MoveState` to the performance log; the Phase 7
   bots count it per second. The "prediction error reporting" feature exists for non-buffer ghost fields [V, S2 notes buffers unsupported], not verified for 6.6 -> [U], not relied upon.
6. **Test**: a Burst-compiled EditMode/PlayMode test runs `PlayerMoveSystem` on a fixed corner set for 10 000 ticks on Windows and on the Linux server build (`--selftest`) and compares `ProgressMm`, `SpeedMmps`, and the position bits; this is spike S4 and a permanent regression test. Expected: integers identical; positions identical to <= 1 ulp.
7. **What I do not do**: no fixed-point library for positions (overkill), no physics-engine movement (Unity Physics character controller is not needed: movement is kinematic on a path).

#### 3.2.7 Correction, smoothing and teleports

- Predicted `LocalTransform` is the *simulation* position. The presentation body does not equal it; it eases to it
  (rule below) so rollback pops never show. N4E has `GhostPredictionSmoothingSystem` with registered smoothing actions per
  component via the `GhostPredictionSmoothing` singleton (`RegisterSmoothingAction<T>`), default for translation [V, S11]; actions are
  stateless function-pointer callbacks and structural changes can disable smoothing [V, S11]. Decision: **use the package smoothing for small
  errors only (default translation action) and own the big-error case in the presentation follower**:
  - `GhostViewFollowSystem` keeps a visual offset `V = bodyPos - ghostPos`; each frame it decays `V` toward 0 (ease 0.2 s, today's `easeSeconds`); when `TeleportSeq` changed or
    `|V| > snapDistance + speed*(RTT+0.35 s)` it snaps (today's `PlayerMoveAnticipation` numbers move into a `PresentationSettings` asset).
  - This keeps the exact behaviour the owner tuned on 22 Oct ("I seem to slip out of the spot I stop at" fix: when the local walk has arrived and the server is only late, wait; here the late snapshot only shifts `V`, the stop pose is the predicted one).
- **Teleports** (`NetworkPlacement.Place` rewritten): server sets `LocalTransform.Position`, resets `MoveState` (progress 0, corners cleared, speed 0, `OrderSeq++` to cancel any client plan), bumps `TeleportSeq`, flags `Placement` in `MoveState.Flags`. The client's `PlayerMoveSystem` has no history to protect; rollback to the new snapshot lands at the new position and the follower snaps because `TeleportSeq` changed. Raw `agent.Warp` sites (`ZoneTravel.cs:162`, `PointAndClickPlayer.cs:1469,1615`) must call `NetworkPlacement` first (Stage 1 seam). Cross-map travel destroys the client world and reconnects (Phase 5), so no cross-map teleport exists in-ghost.
- **Death**: `Dead` enableable bit + `MotionPlan{Kind=Death, ...}`; `PlayerMoveSystem` is skipped for dead entities by query exclusion. Respawn = placement.

### 3.3 Jump, vault, wall-run, waterstride, death as tick-based plans

Today (all server-scripted GameObject code that writes `transform.position`, then ClientRpcs replay the visuals): `PlayerJumpMotor` (Rpc L145, ClientRpc L192/L213, planner with PhysX validate `ValidateWarpedTrajectory`, KINEMATION Motion Warping asset from clip phases), `PlayerVaultMotor` (PhysX raycasts/capsule checks, planner `TryArm`, ClientRpc L555/L586, warp), `PlayerWallRunMotor` (NetworkVariable `netPose` L126, route legs, warp), `PlayerWaterstrideMotor` (moves `transform.position` L339/L389/L466, `netOnWater` L40), `PlayerDeathMotor` (`netPose`, warp, ground probes).

New shape (one component, one system, plans decided by server managed systems that keep their PhysX/NavMesh code):

```csharp
public struct MotionPlan : IComponentData {                     // predicted + replicated
  [GhostField] public byte Kind;                                // None, Jump, Vault, WallClimb, WallDrop, WaterRun, DeathKnock, Fall
  [GhostField] public ushort PlanSeq;
  [GhostField] public NetworkTick StartTick;
  [GhostField] public ushort DurationTicks;
  [GhostField(Quantization = 100)] public float3 From, To;
  [GhostField(Quantization = 100)] public float Apex;           // jump / vault arc height
  [GhostField] public ushort YawEnd;
}
[GhostComponent(OwnerSendType = SendToOwner)] public struct MotionLeg : IBufferElementData { [GhostField(Quantization=100)] public float3 P; public ushort Ticks; public byte Style; }  // wall-run legs, vault stages
```

- `PlayerMotionSystem` computes `position = Curve(Kind, (tick - StartTick) / DurationTicks)` (integer tick index, float ease via a baked curve table in the settings blob; no integration). It runs the same code on server and owner client and **never reads PhysX**. A plan with `Kind != None` suppresses `PlayerMoveSystem`; at the last tick it writes `MoveState` (progress 0) and the landing yaw, and clears the plan.
- The existing planners (`TryJump`, `TryArm`, `TryArmSurfaceRoute`, `ValidateWarpedTrajectory`, ground probes) stay as **server-only managed** code in a `MotionPlanServerSystem`; they output `MotionPlan`. This is the "server checks moved out of the motors" line of the 20 Oct plan.
- KINEMATION Motion Warping asset shaping (`ShapeWarp`, warp phases from clips) becomes presentation: the `AnimatorFeedSystem` sees `PlanSeq` change and plays Animancer + the warp with `startOffset = (currentTick - StartTick)`. The presentation re-triggers only when `PlanSeq` changes, so rollback never restarts a jump (20 Oct plan, preserved). The *body position* during a motion comes from the ghost (`Curve`), not from the warp; the warp is driven to match it (or run with root position taken from the ghost: warp points are `From/To`). Risk R5.
- **Owner prediction of motions**: jump yes (client already shows `BeginOwnJump`): the client plans with the same helper on its PhysX/NavMesh at the `Jump` event, writes a `ClientPlanMotion` (client-only), start system adopts server's `MotionPlan` when it arrives, same rule as corners. Vault, wall-run, drop, waterstride: **not predicted initially** (the server plan arrives after RTT; the owner's walk ends at the take-off point and waits, exactly as `LeadOwnWalk` + `WouldVault` does now, L1175-1180). Phase 2 option: predict vault too because the client already has `WouldVault`.
- Because ticks align (the server simulates tick T when the input stamped T arrives; the client at T is "ahead" of the snapshot clock) `StartTick` of a server plan is the same tick the client predicted for the same input; the animation fast-forward offset on arrival is about RTT (typ. 50-150 ms) and is hidden by the follower smoothing.
- Death knock sequences use physics probes (`FallProbe`) on the server only. Respawn: placement. `PlayerDeathMotor.netPose` -> `MotionPlan{Kind=Death}` + `Dead` bit.
- Waterstride (`OnWater` flag): polyline-over-water is a server `MotionLeg` plan; leaves the NavMesh, so `PlayerMoveSystem` is off while on water (the water is not NavMesh).
- Mounted: any plan except `Fall` calls `MountPhaseSystem` first: Riding + motion start -> `Phase = OnFoot` (code today `PlayerMounts.cs:242 ServerDismountNow`). Same rule in client prediction (so jump key on a mount shows a dismount).

---

## 4. Skills and combat

### 4.1 Authority split

| Action | Owner predicts | Server decides | Observers see |
|---|---|---|---|
| Basic attack start / combo step | Step, StepStartTick, anim (immediately) | Valid step, cadence, target hostile, reach | `CombatState` interpolated + `Strike` ring |
| Hit and damage | nothing (cosmetic spark optional, 4.5) | everything | `HitEvent` ring + `Vitals` change |
| Skill cast start | Cast anim, cooldown set, Chi paid | Cooldown, Chi, range, rank, target, hostility | `CombatState` + `SkillCast` ring |
| Area skill damage | nothing | overlap on history world | `SkillHit` ring |
| Buffs / debuffs | nothing | all (`CharacterBuffManager` stays server managed) | `BuffState` bitfield (8 bytes) on ghost |
| Movement skill select | via RPC | validated | `MoveState` flags |
| Duel challenge / accept | RPC | `PlayerDuel` rules unchanged | `DuelState` |

### 4.2 Data on the ghost

```csharp
public struct CombatState : IComponentData {                    // predicted
  [GhostField] public byte Step;                               // 0 none, 1..3 combo
  [GhostField] public NetworkTick StepStartTick;
  [GhostField] public NetworkTick NextStepEarliest;            // minCadenceTolerance * duration, in ticks
  [GhostField] public byte WeaponClass;                        // nine weapon classes (04_Weapon_Classes.md)
  [GhostField] public int TargetGhostId;                       // 0 none
  [GhostField] public NetworkTick CastStartTick; [GhostField] public uint CastSkillHash; [GhostField] public byte CastPhase;
}
public struct SkillCooldown : IBufferElementData { public uint SkillHash; public NetworkTick EndTick; }   // owner-only, predicted
```

- Timing data (swing windows 0.22-0.65 etc., strike speeds 1.30/1.30/1.00, `postComboDelay`, `hitLatencyGrace` 0.35 s, `serverMaxValidationRange` 2.7 m, combo multipliers 1.0/1.15/1.35: `PlayerMeleeCombat.cs`) are baked from each weapon class's clips + settings into a `StrikeTimingBlob`: per class, per step: `HitStartTick`, `HitEndTick`, `DurationTicks`, `MinCadenceTicks`, `Reach`, `ArcCos`. This is the line "the server body has no animated bones, so per-step hit times come from data" in the 20 Oct plan, made concrete. The hit moment is a *tick*, not a physics sweep of an animated weapon collider.
- Chi/Cooldown: `PlayerSkillCastSystem` (Burst, predicted): on `CastSkill` event, checks `SkillCooldown` (no entry or `EndTick <= now`), `Vitals.Chi >= cost`, `SkillRanks`, `CastPhase == 0`, GM `NoCooldowns` flag, then writes cooldown/Chi/`CastStartTick`. Same code both sides; a server refusal shows up as a rolled-back Chi and cleared cooldown (today's manual "taken back, cooldown and all", `PlayerCombatSkills.cs:805`).
- The skill queue (`PlayerSkillQueue.cs`, `ServerRun` L197, `QueueServerRpc` L152, `EndedRpc` L294): queue = `SkillQueue` buffer on the ghost (owner-only, capacity 4), consumed by `PlayerSkillQueueSystem` (predicted): walk in is a `ServerOrder`-style order (3.2.5) and when range is satisfied it issues the cast. `EndedRpc` is the ring `QueueEnded`.
- Cadence (on-beat bonus, `PlayerCadence.cs`): judged on `NetClock`; `MusicConductor` is a World-layer ghost (20 Oct plan); the press is an `InputEvent` tick-stamped; the server judges against the beat grid at that *tick*, which is better than the NGO `ServerTime` judging (no clock estimation on the client).

### 4.3 Hostility, PvP, duels

- `CombatHostility.IsHostile(Transform attacker, IDamageable target)` (`Combat/Runtime/CombatHostility.cs`): players of different factions hostile while cross-faction PvP is on, same faction friendly, duel opponents hostile, `TradeZone` neutral ground (either end), `ZoneRules.AlwaysOnPvp` (The Scar), non-players always hostile.
- Port: `HostilityRules.IsHostile(in FactionMember a, in FactionMember b, in DuelState da, bool bothPlayers, bool neutralGround, bool alwaysOnPvp, bool crossFactionPvp)` as a pure static Burst-compatible function. Inputs: faction bytes and `DuelState.OpponentPlayerKey` (components), `NeutralGround` = point-in-AABB test against a baked `TradeZoneBlob` singleton (from `TradeZone` MapAnchors), `AlwaysOnPvp`/`CrossFactionPvp` in a `MapRules` singleton (baked from `ZoneRules`; `gm.pvp` switches it on the server and it replicates as a Static singleton ghost).
- Used (a) by the server on every hit/skill/cast target check, (b) by the client for cursor colour and cast pre-check, (c) by Hostile threat selection.
- `HostilityCheck.Rule` hook (Player assembly, filled by Combat) stays as the managed shim until all callers are ECS.
- Duels: `PlayerDuel` is pairwise and to-the-death rules ("back hits never turn the body; duels pairwise" memory `death-duel-chat-decisions`); keep its prompt/accept RPCs, replace `netOpponent` (ulong) with `DuelState{OpponentPlayerKey}` on both ghosts. The duel arena rules stay server C#.
- Authority: PvP damage uses the same lag-compensated path as PvE (section 4.4), with the attacker's and *target's* views both at the attacker's interpolation delay (the victim moved, the attacker saw an older position: standard). Reach uses the larger of (history reach, current reach + epsilon) to avoid "hit around corners" complaints: R7.

### 4.4 Server-validated hits with lag compensation

Facts [V, S6]: `NetCodePhysicsConfig.EnableLagCompensation`; `PhysicsWorldHistorySingleton` returns the collision world at a past
tick; Netcode requires kinematic entities (those with `PhysicsVelocity`) or lag compensation for the physics group to run;
`PhysicGroupRunMode` options. [V1x/U: names `PhysicsWorldHistorySingleton.GetCollisionWorldFromTick(tick, interpolationDelay, out CollisionWorld)` and the per-connection
`CommandDataInterpolationDelay` component; the 6.6 API page returned 404 and the physics page does not describe them (history
size limit reported as 16 worlds in a forum thread -> 16 ticks = 533 ms at 30 Hz, enough for 250 ms RTT + interpolation).]
URLs for the 1.x pages: https://docs.unity3d.com/Packages/com.unity.netcode@1.13/api/Unity.NetCode.PhysicsWorldHistory.html

Setup:
- Every hittable ghost (players, hostiles, dummy) has a `PhysicsCollider` baked from a **capsule/box hit proxy** (not the render mesh, not the PhysX collider), `PhysicsVelocity` (kinematic) so it is a dynamic body in the history rather than a rebuilt static one (review item 11; matches the Unity requirement [V, S6]), and a `PhysicsMass` of infinite mass.
- `LocalTransform` drives the body: a `KinematicHitProxySyncSystem` copies position to the physics body every tick before `PhysicsInitializeGroup`.
- History depth: `NetCodePhysicsConfig` default; request 16-worlds-worth; spike S5 confirms the maximum rewind and clamping behaviour (a query further back is clamped to the oldest entry per the 1.x API remarks, i.e. a laggier client gets the oldest history rather than an error).

Melee (`MeleeResolveServerSystem`, server only, after `PlayerMeleeStateSystem`):

1. For each attacker with `CombatState.Step > 0` and `now == StepStartTick + HitStartTick .. HitEndTick`, take `TargetGhostId`.
2. Resolve to entity (hostile, dummy, player). Skip if dead or not `HostilityRules.IsHostile`.
3. Get the attacker's `CommandDataInterpolationDelay` [V1x/U] and ask history for the collision world at that tick.
4. Reach/arc test between the attacker's *current* position (the server's predicted-authoritative, owner is authoritative for self in the sense that the server simulated their input) and the target proxy in the past world: distance <= `Reach` (blob, 2.7 m validation range today) and, for steps 2/3, an arc test.
5. One damage application per target per swing: a `SwingHit` bitset on `CombatState` (reset on a new step), replacing `serverSwingHitTargets` (`PlayerMeleeCombat.cs`).
6. Call the existing damage pipeline (managed, server): `ResolveHit` content (reflection / shield-shatter procs, crits, evade, `ConsumeNextHitBuffs`) stays managed C# (the 20 Oct "Untouched: CombatCalculator").
7. Append `HitEvent` to the target and attacker rings, write `Vitals` on target.

Deleted: `RequestMeleeStrikeDamageServerRpc` (L709) and the client `WeaponHitbox` reports (`WeaponHitbox.cs` keeps only the cosmetic local spark, 4.5). The `LateHit/GoneTarget` `NetworkHealth.Record` events are replaced by `NetworkHealth` counters on "target not in history" (the target did not exist at the rewound tick, e.g. spawned in the last 100 ms: the client could not have seen it).

Area skills (`PlayerSkillAttacks.cs:277 Physics.OverlapSphereNonAlloc`): `SkillAreaResolveServerSystem` does `CollisionWorld.OverlapSphere` on the **history** world for the cast tick (CastStartTick + data `HitTick`), filtering with `HostilityRules`. Same two lines: radius from `SkillBlob`.

Map line of sight stays a PhysX raycast in the server's managed code (Unity Physics has no map geometry; keeping two physics worlds in sync is not worth it) [rule from the 20 Oct plan].

Hostile attacks on players (Emerald today strikes via PhysX faction targets and animation events, `KNEmeraldPlayerBridge.cs`): the new monster brain (task 2) emits `HostileAttack{TargetGhost, DamageProfile}` on the server in the tick of its attack data; server applies damage vs **current** player position (no rewind: the victim is predicted on its own screen; monsters get no lag compensation). Evade window = the player's `MotionPlan` (a jump grants i-frames if designed). Not changed here.

### 4.5 Cosmetic prediction at the owner

- Swing and cast animations start at the click (predicted `CombatState`); the existing code shows `ShowStrike`/`ShowCastOnPress`. Keep.
- Owner-side hit sparks: when the predicted swing reaches `HitStartTick` and the locally interpolated target is within reach, the presentation spawns a *non-damaging spark* (no number). The authoritative `HitEvent` plays the number and the hit reaction; if the server disagreed no number appears, only the spark was shown (acceptable, and today's behaviour with `WeaponHitbox`). Guarded by `IsFirstTimeFullyPredictingTick`.
- Damage numbers, sounds of getting hit, hit reaction (`HitReactor`), time slow: server `HitEvent` only.

### 4.6 HP / Chi / cooldown replication summary

`Vitals` on player/hostile/dummy ghosts (`int` hundredths, `GhostField`), `Dead/InCombat` enableable bits. `CombatEntity` (`netStandaloneHealth`, `netMaxHealthOverride`, `netInfiniteHealth` L29-33) collapses into `Vitals` + `GmFlags` byte (`Infinite`, GM). `PlayerResources.RpcL284` owner reports become `FxEvent` kinds. Regen: `VitalsRegenSystem` integer per tick, predicted for owner Chi only (HP regen server-only because HP never predicted: both live in `Vitals`, restored from the snapshot, the client skips HP regen in its predicted copy via `if (isOwnerPredicted) skip hp`).

---

## 5. Hostiles, drops, kiosks, singletons

### 5.1 Hostile ghost (interpolated, Entities Graphics presentation, new ECS brain)

Today: `HostileNet` (L18) NetworkVariables `netLevel`, `netAnim` (`HostileAnimState`: speed 0.05 steps, flags, death index; sent every 0.1 s on change, L432-446), `netTag`; three ClientRpcs; pooled prefab instances via `HostilePool`; `HostileZone` server MonoBehaviour manages slots/respawn; <=102 per map (Zone01: 8 zones, 100+2). Emerald AI is dropped (does not work with ECS) -> a new ECS brain (task 2, separate doc).

Ghost `HostileGhost` (Supported Ghost Mode = Interpolated only; Static optimisation off, because it moves):

| Component | GhostFields | Notes |
|---|---|---|
| `LocalTransform` | Position Q=100, Y rotation only via `HostileYaw ushort` | no scale |
| `HostileInfo` | `ushort SpeciesId`, `byte Level`, `uint SpawnZoneId` | species -> `HostileDefinition` lookup on client for model, name, nameplate |
| `HostileAnim` | `ushort SpeedStep` (0.05 m/s), `byte Flags` (Combat, Stunned, Dead), `byte DeathIndex` | exactly `HostileAnimState` re-expressed; reuse the struct fields |
| `Vitals` | Hp, MaxHp | nameplate HP bar |
| `TagHolder` | `uint PlayerKey` | kill tag mark on nameplate (`netTag`) |
| `FxEvent` ring (capacity 4) | Strike / Attack / Hit kinds | replaces `StrikeClientRpc` etc. |
| (server-only, `GhostPrefabType.Server`) `HostileBrain`, `ThreatList` buffer, `NavAgentState`, `LeashInfo`, `RespawnSlot` | none | never on client |
| (server-only) `PhysicsCollider` + `PhysicsVelocity` | none | hit proxy for lag compensation |

- Send rate 15 Hz with importance 60; distant Hostiles are deprioritised by tile scaling; idle Hostiles send nothing (no changes -> no delta).
- Level gap math (`LevelGapRules`), rewards/loot (`HostileKillRewards`, `HostileLoot`) are server managed code reading `Vitals` death; unchanged.
- Zones: `HostileZone` becomes a **zone-spawner system on the server** reading `ZoneSpawner` entities baked from `MapAnchor` zone markers (`SpeciesTable` blob, `MaxAlive`, respawn queue); sleep when no player in `ZoneTrigger`s (existing rule). Spawn = `EntityCommandBuffer.Instantiate(HostileGhostPrefab)` on the server [ghost prefabs spawn on the server and replicate; client-side prefab collection must match [V, S12]]. Death: `Dead` bit + corpse timer, then destroy (corpse dissolve is client presentation: uses `Dead` plus ghost destroy event; since destroy removes the entity, the dissolve model is taken over by a pooled presenter that keeps fading after the ghost is gone, 7.3).
- Per-hostile pathing on the server: the brain uses NavMesh (the managed API or `NavMeshQuery` jobified [U: `UnityEngine.Experimental.AI.NavMeshQuery` is Burst/job-compatible in older Unity; verify for 6.6 -> spike S6]) at low rate (path request on target change, repath every 0.5 s while chasing); it follows the polyline with the same `PointAtArcLength` code as the player (shared `PathFollow` library). 102 per map is well inside a managed main-thread budget; Burst brain is Stage 10.
- Nameplates: `HostileNameplateController` today reads `NetworkObject` (L101, L231) -> reads the presenter's ghost reference (7.4).

### 5.2 Combat Dummy
Anchored ghost (Static, Interpolated), `Vitals`, `Infinite`; hit proxy collider; first target of the Stage 4 two-client test (20 Oct plan). Spawned by the server from its `MapAnchor`.

### 5.3 WorldDrop (`WorldDrop.cs`: `netStack` ItemStack, `netHold` Hold, one `Rpc(Everyone)` L324, `WorldDropPool` with handler, 200/zone cap)

- Ghost, Interpolated, **Static optimisation** (rarely changes). Components: `DropInfo{ushort ItemId, ushort Count (stack), byte Rarity, byte Flags (IsCreds), uint HoldPlayerKey (reservation), uint HoldUntilTick}`, `LocalTransform` (Position Q=100, no rotation), `FactionMember` if faction-locked (`WorldDrop` has `FactionId`).
- No pooling on the server: ECS instantiation is cheap; the cap of 200 per zone is a server system that destroys the oldest (`DropCap` per `ZoneId`).
- Pickup: owner `PickupRequestRpc{GhostId}` (not an input event: it is a discrete command with a reply). Server system `PlayerLootSystem` checks reach (1.5 m + the tick-position of the player), reservation, inventory, calls the unchanged `PlayerLoot.ServerPickup` and `EconomyLedger`. Rate limited. The "item flies to the hand" animation is presentation triggered by the despawn + `DropPickedFx` event to the picker (owner RPC, small).
- Rendering: **Entities Graphics** (7.3): a mesh/material per rarity tier, rarity tint via `MaterialProperty` override (per-instance colour), floating/spin by a shader or tiny presentation system. The magnet/autopilot (`MagnetController`, `AutopilotController`) reads drops through a `DropView` registry (position, item id), unchanged logic.

### 5.4 KioskStand (`KioskStand.cs`: `Sign`, `netLook` CharacterNetState, `NetworkList<KioskListing>`, spawned by `KioskService.cs:107`)
Ghost, Static, Interpolated. `KioskInfo{ownerPlayerKey, sign id}` + `LookVersion` (look through the same cache as players) + `KioskListing` buffer (`ItemId, Count, Price`; cap 32, GhostField on the buffer elements [U spike S1], else page RPC). `KioskService.ServerBuy` stays managed with `SaveTogether`. The doll (`CharacterDoll`) is a presentation GameObject (Sidekick), pooled.

### 5.5 Singletons (clock, weather, music, zone rules, portals)
- `WorldEnvironmentNetwork` (`NetworkVariable<WorldEnvironmentState>` L44; all peers derive time/weather from server time + seed): ghost `WorldEnvironment` (Static, always relevant) with `WorldEnvState{ seed, weather, nextChangeTick, dayLengthTicks, timeAtTick0}`. Every peer derives sky from `NetworkTime.ServerTick` through the `NetClock` seam.
- `MusicConductor` (`MusicState` ~14 bytes): World-layer ghost `MusicState{...}` + `BeatGrid`; Audio plays locally.
- `ZonePortal`: server trigger system over baked portal AABBs; "portal message" -> owner RPC. Travel itself is not a ghost thing (client world rebuild).
- Map content (`ZoneRules`, `TradeZone`): baked blobs from `MapAnchor`.

### 5.6 Projectiles
None exist for players today (all instant melee/area). If skills with projectiles arrive: owner-predicted spawn using `PredictedGhostSpawn` classification (5-tick window, one spawn per type per window [V, S12]), so rapid-fire of the same projectile type must be spaced > 5 ticks or classified by a custom classification system [V, S12]. Not built, per "build only what is asked".

---

## 6. Pets, mounts, NPCs, housing

### 6.1 Pets (`PlayerPet.cs`, `PetFollower`, `PetDefinition`)
- Not their own ghost. Reasons: today there is **no networked pet object** (`prior-survey-acddd94c` section 2), `PetFollower` is a local follower, 4 pets, 1 per player, pet position is cosmetic.
- Replicated on the player ghost: `PetPublic{Growth, Fed, MorphId}` (observer look + stage), `PetTrick` ring entries (tricks, chatter emoji) via `FxEvent`. Server-only: XP, hunger, evolution, `PetItemState` in the item itself.
- Client: `PetFollowerPresenter` (GameObject, already exists) follows the player's presentation body; pooled; stage changes with `Growth` thresholds as today (pets table in `12_pet_evolution_system.md`).
- Pet combat/stat bonus: computed on the server and included in `Vitals`/stat components; the client never recomputes pet bonuses in prediction. (The equipment/pet bonus changes `MaxHp`, speed stats: written by the server.)
- Chatter director (`PetChatterDirector`): presentation, driven by `HitEvent`/`FxEvent` of the owner.

### 6.2 Mounts (`Gameplay/Mounts`: `PlayerMounts.cs` 516 lines, `MountRider.cs` 292, `MountNet.cs`, `MountRules`, `MountSettings`)
- Facts: the mount is **a state of the player**, not an entity: `MountNetState` (selected, phase, growth, fed) everyone-readable `NetworkVariable`; `MountBook` (4 boxes: owned bits, xp, hunger) owner-readable; `MountRider` (GameObject) instantiates the model under the player and lifts the rider to a seat socket; speeds from `MountSettings` (walk 6-9 m/s, run 20-34 m/s, clips 4/14 m/s, stride count 3); `ServerToggleRide` with 0.4 s debounce; getting on/off timed by `phaseEnds` = `Time.time`; a jump/vault/wall-run takes the rider off (L242); hunger drains 0.05 %/min while ridden, written every 10 s; EXP for kills.
- Ghost data: `MountPublic` (3.1) + `MountBook` owner-only. `PhaseEndTick` replaces `Time.time`. Debounce becomes a tick (12 ticks).
- Ride speed & prediction: `WalkMmps/RunMmps` are in `MountPublic` (server writes `MountRules.Speed(...)`). At 34 m/s the owner covers 1.13 m per tick; the corner follower copes (3.2.4). **Risk**: a 150 ms rollback at 34 m/s shifts the owner by up to 5 m before smoothing; correct by design (the follower snaps/eases using `speed*(RTT+0.35)`, 3.2.7, scaling the tolerance with speed as today).
- Mounted fight (owner decision "fight mounted"): `CombatState` unchanged; reach measured from the rider's position; the mount's stance is presentational. Gating: jump/vault/wall-run dismount first (3.3); skills that need a motion (movement skills) dismount.
- Hit proxy while mounted: the same capsule, taller (`MountedHitProxy` swap on `MountPublic.Phase`), server-side only.
- Presentation: `MountRider` + `StirrupIK` stay GameObjects (Animancer on the mount model, seat socket lift, lean/bob procedural from speed). It reads `MountPublic` and `MoveState.SpeedMmps` from the ghost through `PlayerViewBinding` (7.4). Stage models swap on `Growth` stage (`MountDefinition.Stage`). On a dedicated server it makes no model (it already skips when `Application.isBatchMode`).
- Mount Feed, selection, mount list: RPC pages (Apothecary/menu), logic untouched.
- Brambleback (the first mount, `MountAssetBuilder.cs`): rig + clips are art pipeline outputs; nothing ghost-specific. 3 stages x idle/walk/run clips.

### 6.3 NPCs and shop actors
- `NpcInteractable`, `NpcDefinition` (SO), `VendorCatalog`, `VendorStock`: scene-placed GameObjects, **not networked** (`prior-survey-acddd94c`). They stay GameObjects on the client, loaded with the Addressable map scene.
- Server needs "player is within range of NPC X" for shopping/dialogue validation. Today the server body has the NPC GameObject loaded from the scene (the dedicated server loads map scenes). New: `NpcAnchor` entities (server-only) baked from `MapAnchor` data (NpcId, position, interact range, role) in the map subscene; `ShopRpcSystem` verifies `distance(PlayerPosition, NpcAnchor) <= range` and `NpcId` exists. No ghost, no replication.
- Shop/Apothecary/Armorer transactions: unchanged managed server logic (`SaveTogether`, ledger), new transport = `ShopBuyRpc{NpcId, ItemId, Count}`. Stock lists are ScriptableObjects shipped with the client; server stock is authoritative (`ShopStockViewRpc` pages only if stock is dynamic).
- If NPCs later wander/patrol, they become interpolated ghosts exactly like Hostiles minus combat. Not built.

### 6.4 Housing (`PlayerHousing`, `PlayerCrate`, `PlayerRecall`, `HallRoom`, `HomeView`, `IHomeAuthority`)
- Today: host/offline only (`LocalHomeAuthority`); interiors are local `Instantiate`s per visit ("an instance made for this visit and nobody else", `PlayerHousing.cs:19,328`); only the player's own session sees them.
- Design: keep it that way for the migration. Furnishings are **not ghosts**. The `IHomeAuthority` seam (`Home`, `Submit`, `Changed`, `Refused`) gets a later `RemoteHomeAuthority` that sends `HomeSubmitRpc{ops}` and receives `HomeChangedRpc`. When Halls are shared later, a `HallInstance` ghost per hall carries `HomeVersion`; visitors fetch pages, relevancy by `InstanceId`. Players entering a Hall are placed at the instance offset like any placement (`TeleportSeq`).
- The walks `PlayerHousing.cs:270,555` use `ServerOrder` (3.2.5).
- Recall tags/teleports use `NetworkPlacement`.

---

## 7. Presentation GameObjects following ghosts

### 7.1 Principle
Ghost entity = truth; GameObject = skin. Presentation reads ghost components, never writes them, never decides rules. This
keeps UI (X.Local 99 uses, C# `Changed` events) alive because the `X.Local` types stay (as plain MonoBehaviour **view models** filled from ghost data).

### 7.2 `PlayerViewBinding` (client)
- Per player ghost: a pooled `PlayerPresentationBody` GameObject = `Player.prefab` in `NetRole.ClientView` (20 Oct plan "one prefab, three roles"): Animancer, `FootGroundingIK`, MotionWarping child, Sidekick doll (`CharacterDoll`), `EquipmentVisualBinder`, Feel components, UI-facing view models.
- `GhostViewFollowSystem` (managed, `PresentationSystemGroup`): for each ghost with `PlayerGhost`, finds the body in a `GhostToBodyRegistry` (client-side `Dictionary<Entity, PlayerPresentationBody>`; created on ghost spawn via `GhostSpawnQueue`-driven `PlayerBodySpawnSystem`, returned to pool on ghost destroy) and sets
  `body.transform.SetPositionAndRotation(ghostPos + visualOffset, yaw)`.
- The camera (Cinemachine) targets the body's "Camera Target" child (`PointAndClickPlayer.cs:268-284`), unchanged, only for `GhostOwnerIsLocal` [V, S7]. Input System `PlayerInput` stays disabled on bodies except none: input is gathered by `PlayerInputGatherSystem`, which reads the Input System actions asset directly (the device-pairing bug of `OfflinePlayerHandoff.cs:138-143` goes away because no `PlayerInput` MonoBehaviour is spawned per body).
- `Animancer`: `AnimatorFeedSystem` writes one `LocomotionInput{speed01, moving, run, mounted, onWater, dead, motionKind, motionSeq}` struct per body; `PlayerAnimancerMotor` (1,450 lines) reads it instead of reading `NavMeshAgent.velocity`/NetworkVariables. Event-driven clips (strike, cast, jump, vault) start when the corresponding `StepStartTick/CastStartTick/PlanSeq` changes, with `startOffset = max(0, currentTick - StartTick)` in seconds (so a rollback that only shifts the position does not restart the clip, 20 Oct plan).
- `KINEMATION MotionWarping` (jump/vault/wall/death): driven with the plan's `From/To/YawEnd` and the clip phase; body position during a plan comes from the ghost curve; warp output is used only for the visual pose offset if the clip root motion contradicts the curve. Spike S7 ("does the warp fight the ghost position?"): the safest start is to disable warp's root writing and let `MotionPlan.Curve` own the root, using the warp only to shape the pose (Risk R5).
- `Sidekick` (look): `LookCache` + `LookVersion`; `PlayerIdentity.Apply` unchanged.
- Feel / `GameFeelPresenter`: `FxEventPresentSystem` calls `ClientPresentationRegistry.Feedback/.Feel`; `GameFeelPresenter.IsLocalPlayer(Transform)` (L847-851, uses `NetworkObject.IsPlayerObject && IsOwner`) becomes `LocalPlayer.Is(Transform)` backed by `GhostOwnerIsLocal` (20 Oct Stage 1 seam). `HitReactor.cs:183` (`NetworkTransform` check) -> `PlayerBodies.Is(Transform)`.
- UI: `X.Local` view-model types stay (plain MonoBehaviours `PlayerInventoryView`...) with the same events; each is filled by an RPC page handler or by `Vitals`/`MountPublic`/`PartyView` ghost data through `PlayerViewBinding`. 8 UI files use netcode directly (`GameMenuController`, `NetworkSessionPanel`, `MenuRoster`, `PerformanceReadout`, `CadenceReadout`, `HostileNameplateController`, `LootHudController`, `MinimapController`) and are rewritten against `PlayerBodies` / a `NetSession` seam in Networking.

### 7.3 Where Entities Graphics is used (and not)

Decision table:

| Thing | Renders as | Why |
|---|---|---|
| Players | GameObject (Sidekick skinned mesh, Animancer, IK, warp) | Sidekick/Animancer/KINEMATION/IK are all GameObject tech; 60 fps target needs <= ~100 visible; LOD via `CharacterLod` stays |
| Hostile bodies (<= 102/map) | **Decision needed (Q1)** | They are skinned, animated (Animator, species clips, death dissolve). Entities Graphics renders meshes; ECS playback of arbitrary AnimationClips on a skinned mesh is not something I could verify for 6.6 [U]. Recommended: GameObject presenters pooled for Hostiles within ~60 m (about 20-40 at once) + **Entities Graphics for far/LOD silhouettes and corpses** as a later optimisation. |
| WorldDrops | **Entities Graphics** (static mesh per rarity, per-instance colour override, no animation) | Up to 200/zone, no skeleton; best fit. |
| Kiosk stands | GameObject (doll uses Sidekick) | Few, need Sidekick |
| Corpse dissolve | GameObject presenter or Entities Graphics with a `FadeAmount` property | Existing `HostileCorpse` sets `FadeAmount` (`HostileCorpse.cs:143-146`): the Entities Graphics `MaterialProperty` override can do the same on a shader that exposes it [U: needs the property declared as `[MaterialProperty]` in the shader graph, spike S8] |
| Skill VFX (Feel/VFX Graph) | GameObject (VFX Graph / Feel) | not ECS |
| Zone singletons, sky | GameObject (`EnvironmentPresenter`) | unchanged |

Entities Graphics requires URP + SRP Batcher + shaders compatible with DOTS instancing; our comic URP shaders (Rendering system) need
`#pragma multi_compile _ DOTS_INSTANCING_ON` support for the drop material only (ShaderGraph supports it). [U] verify with the Rendering owner (spike S8).

### 7.4 Ghost-to-GameObject registries
- **Client** `GhostToBodyRegistry` (players, hostiles, kiosks): entity -> presenter; presenters implement `IDamageable`/`IHostile`-like **view interfaces** in Core so `HostileNameplateController`, minimap, autopilot (`AutopilotController` reads `HostileNet.Active` and `WorldDrop.ActiveDrops`) read `IHostileView.Active` instead of NetworkBehaviours. This is the main seam the 20 Oct plan asked for (UI binds to interfaces, not NetworkBehaviours).
- **Server** `GhostToServerObjectRegistry` (20 Oct item 17): needed while some managed server code (`CombatEntity`, `PlayerSaveAgent`, `ISaveParticipant` x19, economy services) still hangs on a **server body GameObject**. Long-term direction: server managed code takes `PlayerKey` + component lookups through `PlayerFacts` (an `EntityManager` wrapper) and the server body exists only as long as it is useful. The server body follows the ghost in `KnPreSimulationGroup` (before RPC receive systems), because range checks read `transform.position` (`PlayerMeleeCombat.cs:752`, pickup, vendor, Kiosk distance).

### 7.5 What a `NetRole.ServerBody` GameObject is in the full-ECS direction
It is a compatibility shell (rules, saves, Emerald-free): collider and faction for the legacy managed rule code, **no** NavMeshAgent steering (the agent is disabled; `agent.Warp` equivalents write the ghost). Retire it system by system as the rule code moves to `PlayerKey` + ECS lookups. Do not put new rule code on it.

---

## 8. Authority model (maps to memory `multiplayer-authority-model`)

- **Server decides progression**: XP, level, skills ranks, gear, Creds, drops, kills, crafting, tempering. Nothing of that is in a predicted component except `Vitals.Chi`.
- **Observers only present**: interpolated ghosts have no simulation; presentation bodies of others are pure skins. GM tools and `gm.*` commands are server RPCs (`PlayerGm`), controls mirrored in the GM window.
- **Client may predict**: movement along a server-validated order, motion start, cast/swing start, Chi spend. All reversible by snapshot.
- **Saves**: `ServerSave.SaveTogether`, `EconomyLedger`, `PlayerWallet` unchanged (CLAUDE.md). Disconnect: server runs `SaveAndRelease` on the server body *before* the ghost is destroyed, not through `LinkedEntityGroup` (review item 22); the connection entity's destroy hook triggers `PlayerSaveFlushSystem` which holds the ghost until the async save completes (`PendingDespawn` tag + timeout 5 s, then force).
- **Anti-cheat**: input flood guard, `PointFits` equivalent (goal within map bounds / max range from current position), cast range/cooldown/Chi checks server-side, hit reach with history, `SecurityFlags` unchanged.
- **Hit authority**: the client never reports hits. The target id in input is an *intent* (like today's auto-approach target).
- **Teleports and placements** only by the server.
- **Duels/PvP**: hostility computed identically server and client; server rule wins.

---

## 9. What ports straight and what is rewritten

| Area | Existing code | Fate | Notes |
|---|---|---|---|
| Damage maths | `CombatCalculator`, `ResolveHit` content, buff manager, `LevelGapRules` | **Ports straight** (managed, server) | Called from `DamageApplySystem`; results converted to hundredths |
| Hostility | `CombatHostility`, `FactionRules` | **Port with pure-data rewrite** | Components in, bool out |
| Movement settings | `MovementSettings.ArrivalSpeedFactor`, acceleration | **Port to blob + integer tables** | Values unchanged |
| Path planning | `TryPlanRoute`, `TryArm`, wall/drop planners | **Ports as managed helpers**, moved out of motors | Same NavMesh API, called once per order |
| Click raycast | `PointAndClickPlayer` input code | Ports into `PlayerInputGatherSystem` | |
| NavMeshAgent steering | `agent.SetPath`, speed/acceleration, `PlayerMoveAnticipation` | **Rewritten** | `PlayerMoveSystem` + presentation follower |
| Jump/vault/wall/water/death | the five motors | **Planners port; execution rewritten** as `MotionPlan` + `PlayerMotionSystem`; Animancer/Warp calls move to presentation | |
| Melee | `PlayerMeleeCombat` (combo ordering, cadence, validation) | **Rewritten** (ticks + blob + history); damage resolution ports | RPC deleted |
| Skills | `PlayerCombatSkills`, `PlayerSkillAttacks`, `PlayerSkillQueue` | **Rewritten transport/timing; rules port** | Cooldown by ticks |
| Resources | `PlayerResources`, `CombatEntity` | **Rewritten storage** (Vitals), regen rules port | |
| Hostile AI | Emerald + `HostileNet`, `HostileBrainLink` | **Rewritten** (task 2) | `HostileDefinition`, curves, rewards port |
| Hostile spawn | `HostileZone`, `HostileSpawner`, `HostilePool` | **Rewritten** as zone spawner system | Level gap, loot tables port |
| Drops | `WorldDrop`, `WorldDropPool` | **Rewritten** (ghost + Entities Graphics) | `ServerPickup` logic ports |
| Kiosk | `KioskStand` | **Network edge rewritten**; `KioskService` ports | |
| Singletons | `WorldEnvironmentNetwork`, `MusicConductor`, `ZonePortal` | **Edge rewritten**; derivations port | Presenters unchanged |
| Mounts | `MountRules`, `MountSettings`, `MountFeeding`, `MountBook` math | **Ports** (rules, settings, save) | `PlayerMounts` network edge + `MountRider` bindings rewritten; `MountRider`/`StirrupIK` visuals port |
| Pets | `PetRules`, `MorphRules`, `PetItemState`, `PetFollower` | **Ports** | `PlayerPet` network edge rewritten |
| NPCs / shops | `NpcInteractable`, `VendorCatalog`, shop services | **Ports**; add `NpcAnchor` | transport rewritten |
| Housing | `HousingRules`, `HomeView`, `LocalHomeAuthority` | **Ports unchanged** | seam for later |
| Menu systems (34) | inventory...crew | **Rules port; edge rewritten** (20 Oct Stage 7) | |
| Saves / economy / ledger | `ServerSave`, `EconomyLedger`, `PlayerWallet` | **Ports unchanged** | Test with `TransactionLedgerTests` and the kill test |
| Presentation | Animancer motors, Sidekick, Cinemachine, Feel, UI | **Ports with a new data source** | `LocomotionInput`, `FxEvent`, registries |
| Interest management | `SpatialInterestManager` | **Deleted**, replaced by tiles/relevancy | |
| RPC safety | `RpcBudget` (171 sites) | **Rewritten key** (`PlayerKey`), same buckets | |

---

## 10. Risk list

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| R1 | Rollback during a 34 m/s mounted run produces several-metre corrections | Med / High (feel) | Integer progress; speed-scaled snap tolerance; presentation visual offset; limit `MaxPredictAheadTimeMS`; test at 250 ms |
| R2 | Client-planned vs server-planned path disagree often (NavMesh differences, vault take-off) | Med / Med | Server polyline replicated and wins; log divergence; share planner code; Phase 7 bot metric |
| R3 | `DynamicBuffer` ghost fields (PathCorner, listings) have limits / no error report | Med / Med | Spike S1 first; fallback: fixed `FixedList128Bytes<float3>`-style ushort offsets or page RPC |
| R4 | Lag-comp API/limits differ in 6.6 (history depth 16?, names unverified) | Med / High | Spike S5 before writing melee; fallback: server-side "last 150 ms position ring" per ghost in a plain buffer and a manual cylinder test (no Unity Physics) |
| R5 | MotionWarping and Animancer fight the ghost position for jump/vault/wall/death | High / Med | Ghost owns root; warp shapes pose only; spike S7 |
| R6 | Entities Graphics cannot play our skinned clips (Hostiles) | High / Med | Hostiles stay GameObject presenters near, EG only for drops/far silhouettes; Q1 |
| R7 | Lag compensation feels unfair in PvP (hit around corners, dodging dead) | Med / Med | Clamp rewind (<= 250 ms), require current-reach + rewound-reach, log; per-map PvP toggle already exists |
| R8 | Approval payload / RPC size (token 1.2-1.4 KB), reliable window overflow with paged views | Med / Med | Chunk approval; page RPCs rate-limited; N4E RPC max reported as 8192 bytes in 1.3.0-exp.1 release notes [U for 6.6]; re-confirm |
| R9 | Burst only fails in the player build; managed types in Burst systems | High / Low | Compile-check Burst for Win and Linux in CI; managed work stays in non-Burst systems |
| R10 | Fast Script Reload does not work on ISystem/Burst code | Certain / Low | Normal recompiles; keep tuning numbers in assets |
| R11 | Relevancy destroys ghosts at the edge (flicker) | Med / Low | Hysteresis band, 16 m |
| R12 | Many systems calling managed APIs in the server prediction group risk the 30 Hz budget | Low / Med | Profile; planners are per-click, not per-tick |
| R13 | `ServerBody` GameObject lingers and diverges from ghost | Med / Med | One-way sync in `KnPreSimulationGroup`, test `distance(body, ghost) < 1 cm` assert in dev |
| R14 | N4E version change (7.0 renames `NetCode`->`Netcode`) | Low / Low | Stay on 6.6 per 20 Oct decision 1 |
| R15 | Player-player avoidance removed (behaviour change) | Low / Low | Owner confirmation, Q4 |
| R16 | Look cache miss shows default mannequins in crowds | Low / Low | Prefetch on ghost spawn; limit parallel requests |

---

## 11. Open owner questions (with options and a recommendation)

- **Q1 Hostile bodies in Entities Graphics?** (a) GameObject presenters pooled near the camera, Entities Graphics only for drops, far silhouettes and corpses; (b) fully Entities Graphics with baked vertex-animation textures (art pipeline cost, no Animator, no animation events); (c) all GameObject. **Recommend (a)**, then spike (b) for far LOD.
- **Q2 Player-player avoidance (NavMesh crowd avoidance) goes away** (players pass through each other). (a) accept; (b) soft push applied only on the server and shown as a position correction. **Recommend (a).**
- **Q3 Predict vault/wall-run on the owner too (instead of waiting at the take-off point for the server plan)?** (a) not at first, as today; (b) predict vault as well in phase 2; (c) predict both. **Recommend (a).**
- **Q4 Hits: pure server-side reach/arc test by ticks (no animated colliders).** (a) accept, hit moments from animation data; (b) keep animated weapon colliders on the server body (costs skeleton updates). **Recommend (a).**
- **Q5 Creds and economy numbers stay out of snapshots (RPC pages).** (a) yes; (b) owner-only ghost field. **Recommend (a)** (independence of the economy from rollback).

## 12. Verification record

### 12.1 Spikes before real code (each is small; no Unity open now)
- **S1** `DynamicBuffer<PathCorner>` with `[GhostField]` + `SendToOwner` replicates and survives rollback. (R3)
- **S2** `SpawnedGhostEntityMap` / ghost id to entity lookup name in 6.6; `PlayerInput` with `InputEvent` fields round-trips.
- **S3** Rollback keeps client-only `ClientPlanCorner` buffers and re-applies a click from the input buffer when a snapshot older than the click arrives.
- **S4** Burst `FloatMode.Deterministic` regression test of `PlayerMoveSystem` on Windows and Linux server build.
- **S5** `PhysicsWorldHistorySingleton` API in 6.6: `GetCollisionWorldFromTick`, `CommandDataInterpolationDelay`, history depth, kinematic hit proxies at 102 + 200 bodies.
- **S6** `NavMeshQuery` in jobs on 6.6 for the hostile brain.
- **S7** MotionWarping vs ghost-owned root.
- **S8** Entities Graphics: drop material with DOTS instancing; `MaterialProperty` for `FadeAmount`.
- **S9** RPC size limit in 6.6 (approval chunks, page RPCs).
- **S10** `GhostPredictionSmoothing` default translation action interplay with the follower offset.

### 12.2 Official pages used (all fetched 29 Oct 2026)
- S1 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/ghost-snapshots.html (ghost modes, optimisation, MaxSendRate, snapshots)
- S2 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/ghost-types-templates.html (supported GhostField types incl. FixedString/FixedList, buffer note)
- S3 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/api/Unity.NetCode.GhostFieldAttribute.html (Quantization, Smoothing, SendData, Composite)
- S4 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/api/Unity.NetCode.GhostComponentAttribute.html (PrefabType, OwnerSendType, SendTypeOptimization, SendDataForChildEntity)
- S5 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/optimization/optimize-ghosts.html (relevancy, importance, tiles, GhostConnectionPosition, static optimisation, MaxSendRate)
- S6 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/physics.html (NetCodePhysicsConfig, lag compensation, group placement, kinematic requirement, PhysicsWorldHistorySingleton named)
- S7 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/command-stream.html (IInputComponentData, InputEvent, 1024 B limit, resend n=4, GhostOwnerIsLocal)
- S8 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/rpcs.html (IRpcCommand, reliable; size not documented there)
- S9 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/time-synchronization.html (prediction/interpolation ticks, ClientTickRate fields)
- S10 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/intro-to-prediction.html (PredictedSimulationSystemGroup, rollback, partial ticks, NetworkTime flags)
- S11 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/prediction-smoothing.html (GhostPredictionSmoothing)
- S12 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/ghost-spawning.html (spawn classification, prefab collection, pre-spawned ghosts)
- S13 https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/network-connection.html (NetworkStreamInGame, approval, timeouts)
- S14 https://docs.unity.com/en-us/engine/current/manual/programming-environment/script-compilation/burst/compilation/float-precision-determinism (FloatMode.Deterministic, FloatPrecision; the old docs.unity3d.com/Manual/burst URL 302-redirects here)
- (index) https://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/prediction.html

### 12.3 Not verified or only on older docs (marked [U]/[V1x] above)
- `PhysicsWorldHistorySingleton` method names / `CommandDataInterpolationDelay` / history depth (6.6 API page 404; seen in 1.x docs and a forum thread). https://docs.unity3d.com/Packages/com.unity.netcode@1.13/api/Unity.NetCode.PhysicsWorldHistory.html
- RPC maximum size (8192 B stated in 1.3.0-exp.1 release notes only), fragmentation, IApprovalRpcCommand payload limit. https://github.com/needle-mirror/com.unity.netcode/releases/tag/1.3.0-exp.1
- `GhostInputSystemGroup`, `SpawnedGhostEntityMap`, replicated `DynamicBuffer` samples, `PredictedGhost` component name (not found as a type), prediction error reporting scope, `NetworkId` reuse.
- Entities Graphics skinned-animation support, `[MaterialProperty]` overrides, DOTS-instancing shader support, `NavMeshQuery` in jobs on 6.6, Unity Physics specifics beyond the netcode page.
- Static `NavMesh.CalculatePath` without an agent is already used by the vault motor in this repo (`PlayerVaultMotor.cs:246`), so it is a project fact, not an API claim.
