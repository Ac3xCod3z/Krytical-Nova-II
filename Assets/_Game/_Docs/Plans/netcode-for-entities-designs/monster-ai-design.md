# DOTS monster AI: design of the new Monster system (replaces Emerald AI)

Author: nested session "monster AI design", 29 Oct 2026. Read-only on code; nothing compiled, Unity not opened.
Target: clone `C:\Users\dlaco\Krytical Nova II DOTS`, branch `dots/netcode-entities`, new system `Assets/_Game/Gameplay/Monsters/`.
Verification tags: **[V]** checked in official docs this session (source in section 16), **[U]** unverified / from memory, must be checked before code.

---------------------------------------------------------------------------------------------------

## 0. Decisions in one screen

| Topic | Decision | Reason (short) |
|---|---|---|
| Brain | **Hierarchical finite state machine**, one byte state per monster, transitions in one Burst `IJobEntity`; a tiny weighted pick (utility-style) only for *which ability*, not for the state | 102 monsters; Emerald's model is already a state machine (Behaviors/Combat/Movement), so parity is cheap; trivially Burst-safe; debuggable by printing one enum; bosses can later add a utility/BT layer on top without touching normal monsters |
| Data | `MonsterArchetype` ScriptableObject (designer) -> baked **blob asset** per archetype; stat curve baked to a blob table by level | blobs are immutable value-type data readable from parallel jobs [V] |
| Aggro scan | Players copied once per tick into a **uniform grid hash** (NativeParallelMultiHashMap); each monster scans its 3x3 cells, staggered over 4 ticks (7.5 Hz per monster, 0.13 s worst-case reaction) | O(monsters x nearby players), no raycasts, no physics world needed |
| Navigation | **Baked 2.5D walkability grid** (0.5 m cells, height + walkable + slope) sampled from the existing NavMesh/terrain in the Editor; straight-line + grid A* in a budgeted Burst job only when blocked; no runtime NavMesh | `NavMeshQuery` is `Experimental.AI`, marked deprecated in favour of `NavWorld`, Burst not stated [V]; Hostile Zones are open 30 m circles, so most moves are straight lines |
| Movement | Kinematic: server integrates position; separation from neighbours via a monster hash; terrain height from the grid | no rigidbody/physics step per monster |
| Combat | Server-authoritative. Monster attack = windup timer then a distance/arc check at the hit tick, rolled by a **Burst port of `CombatCalculator`**; damage on players goes through the existing managed `CombatEntity.TakeDamage` via an event bridge until players are ECS | keeps every damage rule in one formula; parity test against the managed one |
| Rewards | ECS emits **one idempotent `MonsterKilledEvent`**; one managed bridge calls the existing kill pipeline (XP, `HostileLoot`, `PlayerLoot.ServerPlaceAt`, pet credit). ECS never touches wallet or ledger | CLAUDE.md ledger rules (CredsSource/CredsSink, EconomyLedger, SaveTogether) stay untouched |
| Replication | Monster = **interpolated ghost**, no prediction, quantized fields, state + attack-start tick (client derives the telegraph), health as a byte percent, distance-importance + relevancy radius | monsters are never predicted, so Windows/Linux float drift does not cause mispredictions |
| Client visuals | Phase 1: pooled **GameObject view** (existing model + Animator + dissolve) for the nearest ~32 monsters, driven by the ghost state. Phase 2 (only if profiling asks): Entities Graphics + baked/GPU animation for far crowds | Animancer/Animator and the imported look are kept ("keep the imported look"); the server has no visuals at all |
| Sleep / LOD | Per-zone `ZoneAwake` flag; monsters in a sleeping zone carry a disabled `MonsterActive` enableable component, so no job touches them; zones share a `ZoneId` shared component so whole chunks skip | Emerald/HostileZone sleep behaviour reproduced at zero cost |
| Respawn | Slot buffer on the zone entity (min-heap equivalent: sorted `RespawnSlot` buffer, only the head is inspected); a dead ghost is destroyed and a fresh one instantiated from the archetype prefab | no GameObject pool needed on the server; ghosts are cheap to create |

---------------------------------------------------------------------------------------------------

## 1. What exists today (the contract we must keep)

From `hostiles-decisions` memory, `hostiles-plan.md`, and the code (`HostileDefinition`, `HostileNet`, `HostileZone`, `HostileSpawner`, `HostileBrainLink`, `HostileKillRewards`, `HostileLoot`, `HostileSettings`, `HostileStatCurve`, `HostileZoneBuilder`):

**Roster (ids are stable, never reuse):** 2 Bonewalker L1 (FightsBack, skeleton model Skeleton_01), 3 Blightborn L4 (Undead_01), 4 Blight Reaver L7 (Undead_02), 5 Gargoyle L10 (AttacksOnSight), 6 Wraith L13, 7 Seraph Unit L16 (RobotFemale_01), 8 Carapace Raider L19 (AlienArmor_01), 9 War Frame L22 (BR_War_Robot_01, range 2.2, body 0.6x2.1), plus Bruisers 10 Hellion L12 (scale 1.3, range 2.4) and 11 Colossus L21 (scale 1.3, range 2.6, body 0.6x2.1). Levels 1-7 fight back only, 10+ attack on sight. Bosses are out of scope (owner builds them separately): reserve nothing boss-specific, only keep `HostileTier.BossReserved` refused by every zone.

**Zones (Zone01):** north-gate (-50,157) r25 Bonewalker x10; mob-nw (-150,150) Blightborn x12; mob-sw (-150,-150) Blight Reaver x12; mob-se (150,-150) Gargoyle x12 + Hellion Bruiser; mob-ne (150,150) Wraith x12; island-west (-120,310) Seraph x14; island-north (40,380) Carapace x14 + Colossus Bruiser; portal-approach (330,300) War Frame x14. 100 + 2 = 102 max alive, map cap `maxActiveHostiles` 300. One species and one level per zone, never mixed.

**Per-species data (HostileDefinition):** id, displayName, tier, level, behaviour (FightsBack/AttacksOnSight), spirit, healthMultiplier, attackRange, reachAbove (3 m default; Wallstride rule), lootTableId (empty), model, scale, bodySize, clips, `attacks[]` of {windupSeconds, damageMultiplier, heavy, weight}. Every current species has the same three attacks: 0.25 s x1.0 w45, 0.25 s x1.0 w40, 0.55 s x1.5 heavy w15.

**Numbers (HostileStatCurve, HostileSettings):** hitsToKill 5, hitsToDie 25, player hit 90 %, hostile hit 75 %, aggro radius 8 m +1 m per 28 levels capped 12, walk 1.8 m/s, run 4.2 + 0.006/level, respawn Normal 8 + 0.12/level clamped 8-22 s, Bruiser 480-900 s, tiers Bruiser x8 HP / x1.6 damage / x6 XP / x5 Creds; corpse 3 s + 1.4 s dissolve; leash 15 m past zone edge; spawn clearance 3 m; wander radius 8; spawnsPerTick 4; zoneTick 0.5 s; levelCap 112 + 33 Prime levels.

**Rules to keep:** damage = `max(1, Attack x skill - Defense)` + Elemental, hit chance `Hit/(Hit+Evasion)` clamped 10-95 %, level-gap hit modifier (`IHitChanceModifier`, a Hostile above a player is harder to hit), crit; kill credit = **first hit** holder (tag released after 10 s without damage, on holder death/leave, on leash reset); Squad shares by `HostileKillRewards`; drops held 20 s for the holder; Creds drop as item 9040; a Hostile drops its target when the player steps on neutral ground (town); a zone that falls asleep ends every fight in it and heals everything; GM tools (`gm.hostile.*`, `gm.zone.*`) and the GM window tab.

**Consumers of `HostileNet` that must keep working (graphify `affected` to run before migrating):** `HostileNameplateController`, `LootHudController`, `MinimapController`, `PlayerAutopilot`/`MagnetController`, `PlayerPet.ServerCreditHostileKill`, `HostileIgnore`, `HostileCorpse`, GM commands, `IHostile` (Core interface), `WorldMarkerPresenter` hostile ring. They read a small surface: species id/name, level, tier, tag state, health percent, alive, position. The new system exposes exactly this surface client-side (section 12).

## 2. Emerald behaviour we reproduce (and what we drop)

| Emerald behaviour (as used by the game) | New implementation |
|---|---|
| Detection radius (`DetectionRadius`, from stat curve) | `MonsterStats.AggroRadius`; grid scan, staggered |
| Field of view only for first detection, 360 degrees in combat; LOS checks off in open field, on in Runs | `AggroFovDegrees` in archetype (default 360 = off); `RequireLineOfSight` flag (default false) using the nav grid height line test, not physics |
| Faction relation (Enemy/Neutral/Friendly to `PlayerFaction`) | `Hostility` byte: AttacksOnSight = scan finds players, FightsBack = no scan, only damage sets target; neutral ground (TradeZone) removes players from the player snapshot for a monster's scan |
| Behaviours Passive / Coward / Aggressive | `MonsterBehaviour` enum: FightsBack, AttacksOnSight (as in `HostileBehaviour`) + `Flees` flag (Coward: below `FleeHealthPct` or when outnumbered it walks away from the target for `FleeSeconds`, then returns). Passive-that-never-fights is a prop species, not needed |
| Wander types Dynamic / Stationary / Destination / Waypoints | `PatrolMode`: Wander (random point in `WanderRadius` of home, pause 2-6 s), Stationary (Bruiser), Route (dynamic buffer of waypoints, for Runs later). Destination = route of length 1 |
| Attack types melee / AoE / projectile / heal / summon, cooldown + condition modules | `AbilityDef` in blob: kind {Melee, Cone, Aoe, Projectile}, range, arc, windup, recovery, cooldown, damage multiplier, weight, condition flags (minHealthPct, maxHealthPct, minTargetDistance). Heal/Summon are phase 3 (not used by the roster). Emerald Knockback/Slowed stay off: knockback is a damage-result field resolved by the player's Knockback Resist |
| Aggro from damage (Emerald waited 0.6 s; the link set it at once) | A `DamageRequest` sets target at once (threat += damage) |
| Leash (15 m past the zone edge; target on neutral ground; "out of reach for 6 s" was left to Emerald) | `MonsterHome.LeashRadius` = zone radius + settings.leashMetres, plus `UnreachableSeconds` (default 6 s, nav says no path or target above `reachAbove` for that long) |
| Groups / assist | Not used by the game today. Optional `AssistRadius` + `AssistGroup` on the archetype: on first aggro, monsters of the same zone within the radius copy the target (one job reads a "pull events" list). Default 0 = off |
| Zone sleep (HostileZone: wake at radius + 40 m, 2 Hz, fight ended + healed on sleep) | `MonsterZoneSleepSystem` section 7 |
| Corpse lies 3 s then 1.4 s dissolve then pool | `Dead` state with `DeadUntilTick`; client view runs the dissolve from `DeadSinceTick`; entity destroyed after `CorpseLifetime` |
| Attack events from animation clips at the hit frame | Replaced: windup seconds in data, server decides the hit at `windup` tick; client clip is only cosmetic (animator `fireEvents` off, as today) |
| Optimization component, UI, items, footsteps, IK, cover, decals | Dropped |
| Hit reaction, stun (Lockup), slow | `Stunned` status component with end tick; hit reaction is a client-only flinch from a `HitCounter` ghost field |

## 3. Brain: FSM vs utility AI vs behaviour tree

Workload: 102 monsters per map (plan cap 300), 30 Hz server tick, Linux dedicated server, all in one world with players, drops, and the save/ledger bridge.

| Criterion | FSM | Utility AI | Behaviour tree |
|---|---|---|---|
| Per-entity cost in Burst | One byte + a few floats; a `switch` per monster. ~100-300 ns | Score N considerations x M actions each tick; 5-20x FSM unless throttled | Needs a per-entity node cursor / running-node stack; a tree walk with blob node arrays; 3-10x FSM; awkward because ECS jobs cannot "yield" in the middle of a tree |
| Branchy divergence | Chunk can be sorted by state (optional) | Uniform | Divergent |
| Parity with Emerald (it is a state machine of Wander/Chase/Attack/Return/Dead) | Direct | Needs re-expressing as scores | Re-expressing |
| Debuggability | Print state + enter tick | Needs score dumps | Needs tree trace |
| Authoring | Enum + data; designers tune numbers, not graphs | Curves | Graph editor (we would have to build or buy one; no Unity DOTS BT package is official) [U] |
| Fits future bosses (owner's separate system) | Bosses add phases as states or add a layer | Good for ability choice | Good for scripted phases |

**Pick: hierarchical FSM.** Top states (byte): `Idle`, `Patrol`, `Alert` (optional 0.15-0.3 s react delay so aggro "lands within 0.15-0.30 s" per plan section 5), `Chase`, `Attack` (sub-phases Windup / Strike / Recover as a second byte), `Flee`, `Return` (leash/healing walk home, invulnerable like Emerald's old "ignore damage until back"), `Dead`, `Despawn`. Ability choice inside `Attack` is a weighted random pick filtered by condition flags (this is the only "utility" element and is plain weighted sampling, same as today's `attacks[].weight`). Stated reason, concretely: with a tick budget of 33 ms and a target of < 1 ms for all monsters, anything beyond O(1) per monster per tick is wasted; an FSM keeps the whole brain in one `IJobEntity` over contiguous component arrays with no managed access, no dynamic memory, and it is the only option that maps 1:1 onto the Emerald behaviour the owner already tuned. Revisit only if a boss needs scripted phases.

Per-state work cadence (all measured in ticks of the 30 Hz simulation group, no `Time.time`):
- scan for targets: every 4th tick (staggered by `entityIndexInQuery % 4`) only in Idle/Patrol/Return-no-target (AttacksOnSight only).
- target validity (alive, same map, not on neutral ground, within leash, reach height): every 2nd tick in Chase/Attack; hard checks (leash, unreachable) every 15th tick (0.5 s, same as zone tick).
- move: every tick (needed for smooth interpolated ghosts) but cheap.

## 4. Data model

### 4.1 Designer data (ScriptableObjects, kept as assets, never edited at runtime)
- `MonsterArchetype` (new, `Monsters/Data`): mirrors `HostileDefinition` plus ECS-only fields. Fields: `ushort id`, `displayName`, `tier`, `baseLevel`, `behaviour`, `spirit`, `isBossCandidate`, `healthMultiplier`, `attackRange`, `reachAbove`, `bodyRadius/Height`, `scale`, `lootTableId`, `viewPrefab` (the existing model prefab used on clients), `AbilityDef[] abilities` (windup, recovery, cooldown, range, arc, damageMultiplier, heavy, weight, kind), patrol (`mode`, `wanderRadius`, `pauseMin/Max`), `aggroFovDegrees`, `requireLineOfSight`, `flees`+`fleeHealthPct`+`fleeSeconds`, `assistRadius`, `reactSeconds`, `unreachableSeconds`. An editor tool `Create From HostileDefinition` copies ids and numbers so the roster is not re-typed; `source` keeps the link for validation (ids equal).
- `MonsterSettings` (new, replaces the AI part of `HostileSettings`; corpse/dissolve/drops stay on `HostileSettings` until Hostiles retires): `simTickHz` (30), `scanEveryTicks` (4), `gridCell` (16 m), `separationRadius`, `leashMetres`, `spawnClearance`, `spawnsPerTick`, `zoneTickSeconds`, `corpseSeconds`, `maxActiveMonsters`, `relevancyRadius`, `viewBudget` (32), `viewRadius`, debug toggles. Numbers stay out of code (CLAUDE.md).
- `HostileStatCurve` stays as is (it knows the game formulas). `HostileZoneDefinition` is reused as the zone authoring source (id, centre, radius, species, maxAlive, wakeMargin, bruiser).

### 4.2 Baked blobs (built by bakers in a Subscene; immutable)
```
struct MonsterArchetypeBlob { ushort Id; byte Tier, Behaviour, PatrolMode; float AttackRange, ReachAbove, BodyRadius, BodyHeight, Scale,
  AggroFovCos, ReactSeconds, UnreachableSeconds, FleeHealthPct, FleeSeconds, AssistRadius, WanderRadius, PauseMin, PauseMax;
  BlobArray<AbilityDef> Abilities; }
struct AbilityDef { byte Kind; float Range, ArcCos, WindupSeconds, RecoverSeconds, CooldownSeconds, DamageMultiplier; byte Heavy; byte Weight; byte ConditionFlags; float MinHp, MaxHp; }
struct StatTableBlob { BlobArray<StatRow> Rows; }   // index = (tier * (MaxLevel+1)) + level, precomputed from HostileStatCurve.Evaluate in the baker
struct StatRow { float MaxHealth, Attack, Defense, HitRate, Evasion, CritRate, AggroRadius, WalkSpeed, RunSpeed, RespawnMin, RespawnMax; int Xp, Creds; }
```
Tables are tiny (about (145 levels) x 2 tiers x ~60 bytes = 17 KB) and let a GM spawn at any level with no managed call. `healthMultiplier` is applied at spawn. Loot is not baked into ECS: the event carries species id, level, tier; `HostileLoot`/`HostileSettings` roll it (managed, main thread) so item catalogs and the economy code stay where they are. Blob rules: value types only, access by `ref`/`BlobAssetReference` [V].

Blobs are referenced from components as `BlobAssetReference<MonsterArchetypeBlob>` (one per archetype prefab) and a singleton `MonsterConfig { BlobAssetReference<StatTableBlob> Stats; settings floats }`.

### 4.3 Components (all unmanaged, all `IComponentData`/`IBufferElementData`)
Per monster entity (a ghost; fields marked G are `[GhostField]`):
- `MonsterId { ushort Species (G, static); short Level (G, static); byte Tier (G, static); }`
- `MonsterBrain { byte State; byte Sub; uint StateEnterTick; uint NextScanTick; uint NextDecisionTick; uint Rng; }`
- `MonsterStats { StatRow copy for this life }` (value copy: no blob lookup in hot loop)
- `MonsterVitals { float Health; float MaxHealth; }`; ghost field `HealthPct (G byte)` is a separate tiny component written once per tick when changed.
- `MonsterTarget { Entity Target; int TargetNetworkId; float3 LastKnownPos; float DistSq; uint LostSinceTick; }`
- `MonsterHome { float3 Home; float ZoneRadius; float LeashRadius; Entity Zone; int Slot; }`
- `MonsterPatrol { float3 Dest; uint PauseUntilTick; byte RouteIndex; }` + `DynamicBuffer<PatrolPoint>` only on route monsters.
- `MonsterAttack { byte Ability; byte Phase; uint PhaseEndTick; uint CooldownUntilTick[N via fixed buffer] ; uint AttackStartTick (G) ; byte AttackIndex (G) }`
- `DynamicBuffer<ThreatEntry { int PlayerNetworkId; Entity Player; float Threat; uint LastHitTick; }>` capacity 6 (InternalBufferCapacity 6; prune oldest). Highest threat is the target; first-hit tag = `TagHolder { int PlayerNetworkId; uint LastDamageTick; }` (separate, per the "first hit holds the kill" rule, not threat-based).
- `DynamicBuffer<DamageRequest { int AttackerNetworkId; Entity Attacker; float Amount (already rolled by the managed player skill) or raw CombatStatsRef; byte Crit; byte Element; uint Tick; ulong RequestId; }>` (the inbox, section 9).
- `MonsterNetState { byte Anim (G); byte Speed8 (G); byte Flags (G: InCombat, Stunned, Dead); byte HitCounter (G); uint DeadSinceTick (G) }`
- Tags/enableables: `MonsterTag`, `MonsterActive : IEnableableComponent` (off when its zone sleeps), `MonsterDead`(enableable), `Stunned { uint EndTick }`.
- `ZoneId : ISharedComponentData` (int) for chunk-level sleep and zone queries.

Zone entity: `MonsterZone { float2 Centre; float Radius; float WakeRadius; bool Awake; Entity MonsterPrefab; Entity BruiserPrefab; int MaxAlive; uint NextTickAt; }` + `DynamicBuffer<RespawnSlot { int Slot; uint DueTick; }>` (kept sorted) + `DynamicBuffer<SlotOccupant { Entity Monster; }>`. Tools can force awake/asleep (`ForcedAwake` byte) like `HostileZone.ForceAwake`.

Singletons: `MonsterConfig`, `NavGridRef`, `PlayerSnapshot` (buffer of {NetworkId, Entity, float3 Pos, float Radius, byte flags(Alive, NeutralGround, Ignored)}), `PlayerGrid` (hash), `MonsterGrid` (hash), `MonsterKillQueue` / `PlayerDamageQueue` (buffers on one entity read by the managed bridge systems).

## 5. Systems, ordering, Burst layout

All simulation systems are `ISystem` with `[BurstCompile]` [V: ISystem is unmanaged, OnCreate/OnUpdate/OnDestroy burst-compilable, jobs scheduled from OnUpdate, IJobEntity for iteration], in **server world only** except the client presentation. Group: a new `MonsterSimulationGroup` inside `Unity.NetCode.PredictedSimulationSystemGroup`? **No**: monsters are not predicted. Put it in `SimulationSystemGroup` of the server world, `[UpdateInGroup(typeof(SimulationSystemGroup))]`, running before `GhostSendSystem` (netcode orders send at end of the frame) [U: exact group name in the installed Netcode version, check]. The N4E fixed simulation tick must be 30 Hz (`ClientServerTickRate.SimulationTickRate = 30`) [U], and monster time is `NetworkTime.ServerTick`, so tick counts (not floats) drive timers: deterministic, no drift.

Order (each a separate ISystem; data dependencies through `state.Dependency`):
1. `PlayerSnapshotSystem` (main-thread-light, Burst job): reads player ghost entities (or, in hybrid phase, a managed copy from `NetworkManager.ConnectedClients` into a NativeList via a managed `SystemBase` bridge) into `PlayerSnapshot`; builds `PlayerGrid` (cell = 16 m, hash on x/z). Sets `NeutralGround` flag from `TradeZone` data baked into the nav grid layer (rect list in a blob) instead of managed `TradeZone.CrossFactionAt`.
2. `MonsterZoneSleepSystem`: every `zoneTickSeconds` (15 ticks) per zone: any player within `Radius+Wake` via the grid; on change set `MonsterActive` enabled/disabled for the zone's chunks using `EntityCommandBuffer` or `SetComponentEnabledForAll` on a query filtered by shared `ZoneId`; on sleep: for each monster: clear target/threat, heal to full, release tag, state -> Idle (the existing sleep rule). Few zones (8), so this is not a hot path.
3. `MonsterDamageApplySystem`: drains `DamageRequest`, updates `Health`, threat, first-hit tag, sets target (aggro from damage at once); runs the Burst combat math if the request is raw; flags death (`MonsterDead` enabled + state Dead + `DeadSinceTick`) and appends one `MonsterKilledEvent` (guarded by "not already dead", plus `RequestId` dedupe in the queue).
4. `MonsterAggroScanSystem` (`IJobEntity`, parallel): query `MonsterTag + MonsterActive + MonsterBrain + MonsterTarget` where state in {Idle, Patrol, Return}; only entities whose `(index + tick) % scanEveryTicks == 0`; scan 3x3 grid cells, pick the nearest valid player within `AggroRadius`, FOV and LOS checks only if the archetype asks; AttacksOnSight only. Writes target + enters `Alert`.
5. `MonsterBrainSystem` (`IJobEntity`, parallel): the FSM transition function; reads target position from `PlayerSnapshot` via an `Entity -> index` lookup built in step 1 (a `NativeParallelHashMap<int,int>`), never `ComponentLookup` on managed or on other-world data. Leash, unreachable timer, neutral ground check, flee, return, patrol destination choice (random from `Rng`), ability selection and cooldowns.
6. `MonsterMoveSystem` (`IJobEntity`, parallel): moves toward `Dest` (chase target, patrol point, home) at walk/run speed per state with separation vector from `MonsterGrid` (cell 4 m; steering = sum of push-away within `separationRadius` of neighbours, clamped); samples terrain height and walkable from the nav grid; rejects a step into an unwalkable cell (slides along). Updates `LocalTransform` (yaw smoothed at fixed turn rate) and `MonsterNetState.Anim/Speed8`.
7. `MonsterAttackSystem` (`IJobEntity`): when `Phase == Windup` and `now >= PhaseEndTick`, performs the strike: target still valid and within range (+ small slack), height within `reachAbove`, wall rule (see 9.4), arc test for cones, AoE collects players from `PlayerGrid`; rolls `CombatMath.Attack` for each victim and appends `PlayerDamage` records to the `PlayerDamageQueue` (an `NativeQueue.ParallelWriter` or a per-thread stream merged after).
8. `MonsterDeathSystem` + `MonsterRespawnSystem`: dead monsters wait `corpseSeconds + dissolveSeconds` (ticks), then are destroyed via ECB; the zone's slot buffer gets `DueTick = now + respawnTicks(species, tier)`; respawn picks a random point in the zone (up to 8 attempts like `TryFindSpot`: walkable cell, inside radius, >= `spawnClearance` from every player, bruiser at 0.85 x radius edge), budget `spawnsPerTick` per zone tick, global cap `maxActiveMonsters`; instantiates the archetype prefab (a ghost) with ECB, sets Level/Stats/Home. Only runs while the zone is awake (as `HostileZone` does) but slot timers keep running (`DueTick`).
9. `MonsterBridgeSystem` (managed `SystemBase`, server, main thread, runs last): drains `PlayerDamageQueue` into `CombatEntity.TakeDamage` on the player's GameObject, drains `MonsterKilledEvent` into the existing `HostileKillRewards`-equivalent pipeline (section 10). Managed work is isolated here so every other system stays Burst.

Burst constraints we follow [U unless noted]: no managed types, no `string` except `FixedString`, no static mutable fields (use singleton components), `Unity.Mathematics.Random` stored in component (seeded from `entity.Index ^ tick ^ zoneSeed`), `ref` access to blobs [V]. Burst errors can be invisible until a player/server build (shared-context note): the CI step is a Linux server build plus the compile check, not just Editor play.

## 6. Spatial queries

- **Aggro:** uniform grid hash of players (cell 16 m > max aggro 12 m; one cell ring is enough). Players per map are tens, monsters ~100: the scan is ~100/4 monsters x ~9 cells per tick = ~225 cell lookups/tick. Negligible. No raycasts and no Physics overlaps.
- **Why not Unity Physics:** the package page only states it has "a deterministic rigid body dynamics system and spatial query system" [V]; it does not document overlap APIs on that page, and a physics world of 100 kinematic bodies plus `CollisionWorld` rebuilds per tick would cost more than the whole brain. Physics stays optional for later (cover/line-of-sight in Runs, projectiles) [U].
- **Player-to-monster queries** (skills hitting monsters, nameplate target picks, Autopilot hunt radius, gm commands): the `MonsterGrid` (cell 4 m) is a public read-only singleton; hybrid-phase managed callers use a `MonsterQuery.Overlap(float3, radius, NativeList<MonsterHit>)` helper that takes the grid with `CompleteDependency`. Targeted hits from a player skill already know the monster ghost id; hits are applied by writing a `DamageRequest` (section 9).
- **Separation / crowd:** same monster grid, radius 1.2 m, applied as a velocity bias, never as a hard collision.
- **LOS (Runs only):** heightfield ray march over the nav grid (2D DDA), O(distance / cell), no physics.

## 7. Navigation: evaluation and pick

| Option | Burst/jobs | Linux server | Dynamic obstacles | Multi-level | Verdict |
|---|---|---|---|---|---|
| A. `NavMeshQuery` (Experimental.AI) jobs + `NavMeshWorld` | `NavMeshQuery` is usable inside jobs (IJob, IJobParallelFor), Burst not mentioned [V] | works | NavMesh carving yes | yes | **Deprecated in the docs in favour of `NavWorld`, feature set "not yet fully complete" [V]**. Building on a deprecated experimental API is the wrong risk for "mistakes not affordable"; the NavWorld successor was not verifiable (404) [U] |
| B. Baked nav grid (2.5D) + budgeted Burst A* + straight-line fast path | yes, plain arrays | works, no engine dependency | none at runtime (rebake) | one layer (extend to 2 layers for bridges/Runs) | **Pick** |
| C. Waypoint graph | yes | works | none | yes | Good for caves/Runs corridors later; tedious to author for open zones |
| D. Flow field per zone toward home/target | yes | works | none | one layer | Good when 30+ monsters chase one target; add later if profiling shows A* load |

**Pick B.** Reasons: (1) Hostile Zones are 25-30 m circles on open terrain, ground between a monster and its prey is nearly always straight; (2) it removes the dependency on a deprecated experimental API and on `NavMeshAgent`/GameObject at all, which is required for a headless ECS world; (3) all inputs are arrays, so unit tests need no scene; (4) height comes from the same data, which the old stack got from the NavMesh.

Design: `NavGrid` blob per map: `{ float2 Origin; float CellSize (0.5 m); int Width, Height; BlobArray<ushort> HeightQ (centimetres, 0 = unwalkable flag via separate bit); BlobArray<byte> Flags }`. Zone01 is 1000 x 1000 m: a full-map 0.5 m grid is 4M cells x 3 bytes = 12 MB, too much to ship per map, so the baker only stores **tiles around Hostile Zones** (zone radius + leash + 25 m margin, 0.5 m cells): 8 zones x (about (30+15+25)x2 = 140 m)^2 / 0.25 = ~78k cells x 3 B = 235 KB each, ~1.9 MB total. Positions outside any tile are unwalkable for monsters, which also enforces "never leave your area". Baker samples walkability with `NavMesh.SamplePosition` (Editor only) and `Terrain.SampleHeight`, marks water and slope > 40 degrees, bakes `TradeZone` neutral rectangles as a flag bit.

Pathing: (1) straight-line test by DDA over the grid; if clear, steer directly. (2) If blocked, a Burst A* on the 8-neighbour grid with a node budget (e.g. 2,000 nodes, 8 searches per tick across all monsters, queued by priority); result path stored as up to 16 smoothed waypoints in a `DynamicBuffer<PathPoint>`; re-path at most every 1 s or when the target moved > 3 m. (3) No path found or budget exceeded for `unreachableSeconds` -> monster gives up (existing "out of reach" rule). Limits: static world only (ruins destroyed by Quick Destruction do not change the grid; rebake or accept), one walking layer (Wallstride walls are handled as targets-above rule, not paths), no steep-slope climbing, single agent radius (0.45 m; War Frame/Colossus 0.6 m use a one-cell dilation). Upgrade path: if Unity's `NavWorld` is verified and Burst-friendly, replace stage (2) only.

## 8. Movement, steering, sleep, LOD

- Speeds from `StatRow` (walk 1.8 m/s patrol/return? Emerald returned running; keep: Return uses run speed). Yaw turn rate 360 deg/s. Position integrates at 30 Hz; the client interpolates (ghost interpolation, 2-3 ticks delay).
- Patrol: Wander picks a random grid point within `WanderRadius` of home that is walkable, pauses 2-6 s (ticks), repeat. Stationary (Bruisers): no destination. Route: next `PatrolPoint`.
- **Sleep by player proximity (reproduces HostileZone):** zone tick (0.5 s) tests `AnyPlayerWithin(Radius + wakeMargin)` via the grid. Asleep: `MonsterActive` disabled -> no scan/brain/move/attack jobs match the query (enableable components are skipped without structural change; [U] confirm in the installed Entities version), and the zone does not spawn. Waking resumes; respawn timers kept counting during sleep (as today).
- **Server LOD inside awake zones:** monsters with no player within `activeRadius` (default 60 m) but in an awake zone run `Idle`/`Patrol` at 1/4 rate (brain tick only every 4th tick; position stays). Not needed for the first pass.
- **Client view LOD:** see section 12.

## 9. Combat resolution (server-authoritative)

### 9.1 Monster hits player
At `Strike` time the server evaluates in this order: target alive; target not on neutral ground; `abs(heightDiff) <= reachAbove`; distance <= `ability.Range + slack` (slack = lag-compensation hook 9.3); arc; wall rule (player standing on a wall: only a straight clear line within range + 0.5 m + eye height counts: replicate `HostileBrainLink.ReachesOnWall` with the nav grid line test; *needs the player's up vector* from the snapshot flag `OnWall`); then roll: `CombatMath.CalculateAttack(monsterStats, targetStatsSnapshot, abilityMultiplier, reflect, levelGapHitModifier)`. The player's defensive stats come from the player snapshot (`PlayerSnapshot` carries Defense, Evasion, Reflect chance) filled by the managed bridge from `CharacterCombatStats` each tick the player changed; if stale by one tick, accepted.
The `DamageResult` is queued; the managed bridge calls `CombatEntity.TakeDamage(result, monsterStatsAdapter)` so `Damaged/Killed` events, GameFeel comic hits (monster's blow is a comic hit with its number), pet reactions, death penalty (5 % XP), buffs/reflection all run in the existing code. `monsterStatsAdapter` is a pooled `CharacterCombatStats` stand-in or an `IAttackerInfo` (the `CombatEntity.Damaged` signature takes `CharacterCombatStats attacker`; hybrid phase uses one shared hidden "MonsterAttacker" object per tier, with the stats from the event) [needs checking in `CombatEntity.TakeDamage`].

### 9.2 Player hits monster
Player skills resolve today as managed code that finds a `CombatEntity` via `IDamageable`. During hybrid: add `IDamageable` adapter `MonsterDamageable` (a tiny non-network component on the **client-visible proxy is not authoritative**; the server resolution looks up the monster entity by ghost id) so `PlayerSkillAttacks` calls `MonsterQuery`/`ServerDamageInbox.Enqueue(ghostId, DamageResult, attackerNetworkId)`; the inbox appends a `DamageRequest` (already rolled by the game's `CombatCalculator`, so the formula has one source of truth, including the hit-chance level-gap modifier). The Burst port `CombatMath` is only for the monster-as-attacker direction in the first phase; when players become ECS the player side uses it too. **Parity test** compares 10,000 random tuples between managed `CombatCalculator` and `CombatMath` (tolerance 1e-4, fixed seeds): must be bit-for-bit equal in integer results (damage is rounded) [required before any release].

### 9.3 Lag compensation hooks
Monsters are interpolated on clients, so a client sees monsters ~100 ms in the past. Hooks, none active in the prototype: (a) `PositionHistory` ring buffer (last 8 ticks of position per monster, 8 x 12 B) written by `MonsterMoveSystem`; (b) the client attack message carries the `ServerTick` it saw (`NetworkTime.InterpolationTick`) [U]; (c) the server's hit test for player-to-monster targeted skills uses the monster position at that tick (clamped to 200 ms); (d) monster-to-player attacks get `slack = 0.5 m` plus telegraph: clients show windup from `AttackStartTick`/`windup` so players dodge in what they *see* and the server accepts the position the player had at hit tick (`players are not rewound`; players are predicted so their server position is authoritative). Decide rewind only after a playtest with 150 ms latency.

### 9.4 Rules preserved
First hit holds the kill; aggro from damage immediate; "Hostile above player is harder to hit" is the level-gap modifier already inside the managed `CombatCalculator` (player-to-monster) and inside `CombatMath` (monster-to-player uses `IHitChanceModifier` equivalent `LevelGap` value read from `LevelGapSettings` baked into `MonsterConfig`).

## 10. Death, respawn, XP, loot, economy hand-off

1. `MonsterDamageApplySystem` marks death exactly once (`MonsterDead` enabled, `DeadSinceTick = now`), appends `MonsterKilledEvent { ulong EventId; ushort Species; short Level; byte Tier; float3 Pos; int CreditedNetworkId; }` (credit = `TagHolder` else last attacker).
2. `MonsterBridgeSystem` (managed, main thread) turns each event into the existing flow: `HostileKillRewards`-equivalent (XP by level gap, Squad split, Sponsor rules, `PlayerPet.ServerCreditHostileKill`), then `HostileLoot.Roll` and `PlayerLoot.ServerPlaceAt` (Creds as item 9040 pile, Stims, gear, materials; held for the credited player 20 s). **The ECS code does not roll loot, does not touch `PlayerWallet`, `EconomyLedger`, `ServerSave`**: Creds are paid when a drop is picked up through `PlayerWallet` with its `CredsSource`, as today, so the all-or-nothing and ledger rules in CLAUDE.md are untouched and need no new source/sink. If the economy later pays Creds directly from a kill, that is a new `CredsSource` and an `EconomyLedger` line, added in the bridge, never in a job.
3. The event id plus the one-time `MonsterDead` transition make the hand-off idempotent; the bridge keeps a short ring of processed `EventId`s (not persisted).
4. Corpse: stays `corpseSeconds` (3 s) + `dissolveSeconds` (1.4 s) then destroyed by ECB (ghost despawn tells clients; the client view runs the dissolve using `DeadSinceTick`, so no per-frame ghost updates during the burn).
5. Respawn: zone's `RespawnSlot` head compared once per zone tick (O(1) per zone, as the old min-heap): Normal `respawnSeconds`, Bruiser random in `bruiserRespawnSeconds`; GM override `respawnOverride`.
6. Level scaling: `Level` chosen at spawn (archetype level or GM level), `StatRow = Stats[tier][level]`, `MaxHealth *= healthMultiplier`; level sent as a static ghost field so nameplate colours (level gap, per viewing player) stay client-side exactly as `HostileNameplateController` does today.
7. Saving: monsters are never saved (world state, not player state). Nothing to migrate in `IMigratingSave`. Ghost ids are not stable across restarts and must never be stored in a save; persisted references use `species id` only.

## 11. Ghost replication

- Prefab: ghost, `Default Ghost Mode = Interpolated`, `Supported Ghost Mode = Interpolated` (no prediction) [V: modes exist; interpolation smoothing options Clamp/Interpolate/InterpolateAndExtrapolate apply only to interpolated ghosts].
- Fields (all quantized, `[GhostField(Quantization=...)]` [V quantization = float*factor to int]): `LocalTransform.Position` (x/z factor 100 -> 1 cm? use 20 -> 5 cm, y 20), yaw as byte-ish quantized rotation (factor 100 on a float angle), `MonsterNetState` (4 bytes), `HealthPct` (1 byte), `AttackStartTick` (uint), `Species/Level/Tier` set once (Static optimization mode; Netcode "Optimization Mode" Dynamic/Static controls resend frequency [V]). Do not give jobs write access to a GhostField they do not change: Netcode serializes anything written to, even if unchanged [V]. So `HealthPct` is written only on a change, `NetState` only when different.
- **Send rate:** target 10 Hz for monsters. The exact per-ghost knob (`MaxSendRate`/`SendInterval`) is **[U]: the pages checked did not cover it**; verify in the installed Netcode version; fallback is importance scaling.
- **Relevancy:** use `GhostRelevancy` mode `SetIsRelevant` (everything non-replicated unless added) or `SetIsIrrelevant` [V]; per connection, set relevant the monsters within `relevancyRadius` (default 150 m, covers a zone wake margin) of that player, from the MonsterGrid. This replaces `SpatialInterestManager` for monsters. Sleeping-zone monsters far from everyone are never relevant, so they cost no bandwidth.
- **Importance:** `GhostDistanceData` + `GhostImportance` singletons (distance-based) [V; importance is per chunk, which is why a `ZoneId` shared component keeps chunks spatially coherent]. Packet limits: `GhostSendSystemData.MaxSendEntities/MaxSendChunks`, `NetworkStreamSnapshotTargetSize` [V].
- **Bandwidth budget (estimate, [U] measure):** per relevant monster per update ~14 bytes delta; 30 relevant x 10 Hz x 14 B = 4.2 KB/s per client (about 34 kbit/s), worst case all 102 relevant = 14 KB/s. Compare Netcode snapshot compression; no per-monster RPCs exist.
- Attack telegraph: the ghost carries `AttackStartTick` + `AttackIndex`; the client reads the archetype blob (also present on clients; the client world bakes it) for windup/heavy and starts the warning/ribbon, replacing today's `AttackWindupClientRpc`. No RPCs per attack.
- `NetworkTransform` / `HostileNet` NetworkVariables are retired.

## 12. Client visuals and animation

- Server world: monsters have no renderers/animators. Dedicated Linux server builds exclude `KryticalNova.Monsters.Presentation`.
- Client phase 1 (`MonsterViewSystem`, managed `SystemBase` in the client world): each frame for monsters within `viewRadius` (45 m) choose the nearest `viewBudget` (32) and bind a pooled **MonsterView GameObject** (the existing species model + the Animator Controller the builder makes today + `HostileCorpse` dissolve + nameplate anchor). The view follows the interpolated `LocalTransform`, sets Animator params from `MonsterNetState` (Speed, Combat State Active, Attack Index trigger from `AttackStartTick` change, Hit from `HitCounter`, Dead from the flag; the same eight parameters `HostileNet` uses), `animator.fireEvents = false`. Monsters beyond the budget show nothing (fog/LOD) or a cheap capsule impostor; this is a visual decision, not a gameplay one. This keeps the imported look, the dissolve shader, `ActorDrawOrder` and `CharacterLod`.
- A `MonsterViewRegistry` (managed static, client) exposes the data HostileNet exposes today so nameplates, minimap markers, Autopilot, Magnet, `IHostile` consumers and the target ring keep working with a one-class swap (`HostileNet.Active` -> `MonsterViewRegistry.Active`).
- Client phase 2 (only if profiling shows view cost): Entities Graphics for static LOD meshes (`Unity.Entities.Graphics` renders ECS entities with `MaterialMeshInfo`) and GPU/baked animation for far crowds; Unity has no first-party ECS skeletal animation (Animator is not available on entities) [U: re-check for Unity 6.6, the third-party route would be an owner decision]. The state/tick data above is already what a GPU animation system needs.

## 13. Budget (targets; measure in the Phase-0 soak)

Server per tick, 102 awake monsters: scan 0.01 ms, brain 0.03 ms, move 0.03 ms, attack 0.01 ms, damage apply 0.01 ms, bridge (managed) 0.05 ms typical, ghost serialization is Netcode's (budget 1 ms for 8-16 clients, separate). **Whole monster simulation target < 0.3 ms per tick (1 % of 33 ms)**, hard limit 1 ms with 300 monsters. A* 8 searches/tick x ~2,000 nodes ~ 0.2 ms worst case, queued. Memory per monster ~ 350 B simulation + 6 threat entries (6 x 24 B) + history 96 B ~ 600 B -> 60 KB for 100. All numbers are design estimates, [U] until profiled on the Linux build.

## 14. Determinism and Burst caveats

- Monsters are server-only simulation + interpolated ghosts, never predicted, so Windows (client) vs Linux (server) float differences cannot cause mispredictions (the Burst warning in the shared context applies to predicted code only). The only cross-platform sensitive output is positions, which are replicated rather than recomputed.
- Server restarts do not need bit-identical replays, but tests (and a record/replay debug tool) want the same results on the same machine: use `Unity.Mathematics.Random` per entity, tick-based timers, no `Time.time`, no `System.Random`, job scheduling order must not change results (writes only to own entity; shared outputs only through per-thread streams merged in a stable order).
- If prediction is ever added (e.g. a predicted monster for a duel), set Burst `FloatMode.Deterministic`/`FloatPrecision.Standard` on that code per the docs cited in the shared context [V per shared context], and do not rely on N4E being deterministic.
- Fast Script Reload does not work on ISystem/Burst code: monster code is iterated by a normal compile, so the monster work needs its own EditMode test loop (no Play-mode iteration). Burst is validated by a Linux dedicated-server build; managed fallbacks are forbidden (a Burst-compile error must fail CI, `[BurstCompile(CompileSynchronously = true)]` in tests).
- Stale ghost `Entity` references inside components are not replicated; always store `NetworkId`/ghost id for cross-references that travel, and resolve to `Entity` per tick.

## 15. Debug tooling

- Gizmos (Editor and a runtime `MonsterDebugDraw` system behind a settings toggle, server editor/host only): aggro radius, leash ring, home, current path points, target line, state colour, FOV cone, attack arc, nav grid tile overlay.
- `MonsterDebugWindow` (Editor): table of server-world monsters (id, level, state, target, hp, zone, ticks in state) from an `EntityQuery` with live filters; click to select the view GameObject; buttons to force state (Freeze, Aggro, Kill, Calm).
- GM commands ported: every `gm.hostile.*` / `gm.zone.*` command keeps its name and calls a `MonsterGm` static (managed, server) that writes ECS: spawn at level, clear, kill, info, hp, freeze (`Frozen` tag), ignore (`PlayerSnapshot` flag), ttk, stats, zone fill/clear/respawn/sleep/bruiser. Per memory "GM tools in the window too": each also appears in the GM Tools Hostiles tab.
- Counters into the existing `PerformanceLog` (alive, awake zones, scans/tick, path searches/tick, queue depths, tick ms per system via `ProfilerMarker`/`PerformanceLog.Time`).
- Determinism self-check: `MonsterReplayTest` records inputs (player snapshots, damage requests, rng seed) for N ticks and replays them in a fresh world to compare end state.

## 16. Migration from existing HostileAsset prefabs

1. Keep `HostileDefinition`, `HostileStatCurve`, `HostileZoneDefinition`, `HostileSettings`, `LevelGapSettings`, `HostileKillRewards`, `HostileLoot`, `HostileCorpse`, `HostileDissolve.shader`, ids and the species/zone assets. They are designer data and reward code, not Emerald.
2. New editor builder `MonsterAssetBuilder` (paths in `Monsters/Editor` constants, writes into `Monsters/`): for each `HostileDefinition` create `MonsterArchetype` asset (id parity validated), the **ghost prefab** (Authoring component + `GhostAuthoringComponent`, no NetworkObject, no Emerald, no NavMeshAgent), and the **view prefab** (the model + Animator controller currently built by `HostileAssetBuilder`, with `NetworkObject`, `NetworkTransform`, `HostileNet`, `CombatEntity`, `EmeraldSystem` removed). It reuses `HostileAssetBuilder`'s controller/clip logic (extract to a shared method rather than copy).
3. Zones: `MonsterZoneAuthoring` replaces the `HostileZone` MonoBehaviour in the Zone01 subscene (same `HostileZoneDefinition` reference, same positions).
4. Registration: ghost prefabs need no `GameplayPrefabsSetup.SpawnedPrefabs` entries (that list is for NGO); N4E needs the ghosts baked into a subscene/ghost collection. The rule in CLAUDE.md must be updated for the DOTS branch (owner's mini-boss decision).
5. Staged retirement: stage A new system runs beside Hostiles in a test scene; stage B Zone01 uses Monsters, `HostileNet` kept as a *view-side shim* with the same `IHostile` surface (so UI keeps compiling); stage C remove `HostileNet`, `HostileBrainLink`, `KNEmeraldPlayerBridge`, `HostilePool`, `HostileSpawner`, `HostileZone`, `EmeraldAI` asmdef/asmref files (the vendor exception in `_ThirdParty/README.md` is then deleted), `IHostileBrain`. Run `graphify affected "HostileNet"` and `"HostileBrainLink"` before stage B/C.
6. Layer rules: `KryticalNova.Monsters` (Simulation) may reference Core, Networking, Rendering(no), Progression, Items, Social, Pets, World(only if the nav baker needs it, Editor-only). It must not be referenced by those. `KryticalNova.Monsters.Presentation` is client-only. Run `python Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py --check`, `compile_check.py` and `--server` before the first commit. Anything Autopilot/UI/GameFeel needs from Monsters flows through a small interface in Core (`IHostile` exists) or the view registry, as the CLAUDE.md pattern says.
7. Encyclopedia: a "Monsters (ECS brain)" row in `01_Systems_Build_List.md`, glossary rows for each new term (Archetype, Threat, Leash, Tether tick, Nav Grid, Monster View), a Change-log line, run `build_encyclopedia.py` and publish; `05_Economy_And_Social.md` unchanged (no new numbers there). New tunables go into `MonsterSettings`.

## 17. Tests

EditMode (no scene, create `World` + systems with a `SimulationSystemGroup`, step with fake `NetworkTime`/own tick):
- `MonsterRulesTests` (pure static): state transition table, leash math, aggro radius formula, respawn time, spawn spot rule (clearance, bruiser edge).
- `MonsterStatTableTests`: blob rows equal `HostileStatCurve.Evaluate` for every level/tier.
- `CombatParityTests`: managed `CombatCalculator` vs Burst `CombatMath` over 10k tuples.
- `MonsterBrainTests`: scripted scenarios: idle->patrol->aggro (player enters radius, within 4 ticks) ->chase->attack (hit at windup tick)->player leaves ->leash->return->heal; FightsBack never aggros on proximity; sleep ends fight and heals; unreachable 6 s gives up; neutral ground drops target; first-hit tag and 10 s release; one `MonsterKilledEvent` per death even with simultaneous hits; respawn after N ticks, never inside 3 m of a player; GM level spawn row.
- `NavGridTests`: DDA clear/blocked, A* optimal length on synthetic grids, budget/failure, tile edges, height sampling.
- `DeterminismTests`: two worlds, same seeds, same results after 1,000 ticks.
- `ThreatBufferTests`: capacity pruning, ordering.
PlayMode/integration (later, with a second client per `CLAUDE.md`): ghost spawn/despawn, relevancy radius, health percent, telegraph timing, hit applies to player `CombatEntity`, kill rewards equal the old pipeline (compare XP and drop counts via `gm.hostile.loot` simulation), soak of 300 monsters, 60 min, tick time p99.
Build checks: Linux dedicated server build with Burst on, zero Burst errors; `dependency_map.py --check`.

## 18. Prototype slice (smallest vertical slice)

Goal: **one wolf-like melee monster** doing idle, patrol, aggro, chase, attack, leash, death, respawn, in a headless-testable ECS world first, then as a ghost. No Hostile roster species is a wolf; the stand-in uses the Bonewalker clips and model (or a capsule) and an archetype named `Prowler` (placeholder, owner may rename). Phases: **P0** simulation only in EditMode tests (no Netcode package needed at runtime); **P1** ghost + client view in a test scene on test ports 7797/8097 only; **P2** reward bridge dry run (logs, no loot).

Prerequisite (not in this slice's folder): `Packages/manifest.json` of the DOTS clone currently has NGO and `com.unity.ai.navigation` only; Entities, Burst, Collections, Mathematics, Entities Graphics, Netcode (and optionally Physics) versions for Unity 6.6 must be chosen by the migration-core session [U]. The slice must not add packages itself.

All paths relative to `C:\Users\dlaco\Krytical Nova II DOTS\Assets\_Game\Gameplay\Monsters\` (each file with its `.meta`):

```
Runtime/KryticalNova.Monsters.asmdef            name KryticalNova.Monsters, refs: KryticalNova.Runtime(Core), Unity.Entities, Unity.Entities.Hybrid, Unity.Collections, Unity.Burst, Unity.Mathematics, Unity.Transforms, Unity.NetCode (P1); noEngineReferences false; excluded: none
Runtime/Components/MonsterComponents.cs         MonsterTag, MonsterBrain, MonsterStats, MonsterVitals, MonsterTarget, MonsterHome, MonsterPatrol, MonsterAttack, ZoneId, MonsterActive, MonsterDead
Runtime/Components/MonsterBuffers.cs            ThreatEntry, DamageRequest, PatrolPoint(P1), RespawnSlot, SlotOccupant
Runtime/Components/MonsterNetState.cs           ghost fields (P1)
Runtime/Components/MonsterSingletons.cs          MonsterConfig, PlayerSnapshot buffer, GridRefs, event queues
Runtime/Blobs/MonsterArchetypeBlob.cs           MonsterArchetypeBlob, AbilityDef, StatTableBlob, StatRow
Runtime/Authoring/MonsterAuthoring.cs           MonoBehaviour + Baker (archetype -> blob + components)
Runtime/Authoring/MonsterZoneAuthoring.cs       Baker for zone entity from HostileZoneDefinition-like data
Runtime/Authoring/MonsterConfigAuthoring.cs     singleton + stat table blob
Runtime/Rules/MonsterRules.cs                   pure static helpers (transitions, leash, aggro radius, respawn time, spawn-spot test)
Runtime/Rules/MonsterCombatMath.cs              Burst port of damage formula (hit chance, damage, crit)
Runtime/Nav/MonsterNavGrid.cs                   blob layout + DDA + height sample + A* (budgeted)
Runtime/Systems/MonsterSimulationGroup.cs       group + ordering attributes
Runtime/Systems/PlayerSnapshotSystem.cs         fills PlayerSnapshot/PlayerGrid (P0: test feeder component; P1: ghost players)
Runtime/Systems/MonsterZoneSleepSystem.cs
Runtime/Systems/MonsterDamageApplySystem.cs
Runtime/Systems/MonsterAggroScanSystem.cs
Runtime/Systems/MonsterBrainSystem.cs
Runtime/Systems/MonsterMoveSystem.cs
Runtime/Systems/MonsterAttackSystem.cs
Runtime/Systems/MonsterDeathRespawnSystem.cs
Runtime/Bridge/MonsterBridgeSystem.cs           managed, server; P2: logs kills and routes player damage to CombatEntity
Presentation/KryticalNova.Monsters.Presentation.asmdef   client-only (excluded for Linux server), refs KryticalNova.Monsters, Unity.Entities.Graphics not yet
Presentation/MonsterViewSystem.cs               pooled view binding (P1)
Presentation/MonsterView.cs                     applies MonsterNetState to an Animator
Data/MonsterArchetype.cs                        ScriptableObject
Data/MonsterSettings.cs                         ScriptableObject
Data/KryticalNova.Monsters.Data.asmref          -> KryticalNova.Monsters
Data/Assets/MonsterSettings.asset               created by the builder
Data/Assets/Archetypes/Monster_Prowler.asset
Editor/KryticalNova.Editor.Monsters.asmref      -> the project's editor assembly pattern used by Hostiles (copy KryticalNova.Editor.Hostiles.asmref's target)
Editor/MonsterArchetypeFromHostile.cs           menu: Krytical Nova > Monsters > Create Archetype From Hostile
Editor/MonsterNavGridBaker.cs                   menu: Krytical Nova > Monsters > Bake Nav Grid (writes Data/Assets/NavGrid_<map>.asset; path constants)
Editor/MonsterPrototypeSceneBuilder.cs          builds Prefabs/ + a test scene folder under Monsters/ (never in Zone01)
Editor/MonsterDebugWindow.cs
Prefabs/Monster_Prowler_Ghost.prefab            (P1)
Prefabs/Monster_Prowler_View.prefab             (P1) model + Animator, no NGO components
Scenes/MonsterPrototype.unity + Scenes/MonsterPrototype_Sub.unity   test map with one zone, flat terrain tile, a few blockers
Tests/Editor/KryticalNova.Monsters.Tests.Editor.asmdef
Tests/Editor/MonsterRulesTests.cs
Tests/Editor/MonsterBrainTests.cs
Tests/Editor/MonsterNavGridTests.cs
Tests/Editor/CombatParityTests.cs
Tests/Editor/MonsterDeterminismTests.cs
Tests/Editor/MonsterTestWorld.cs                helper: create World, feed players, step ticks
```
Prototype behaviour spec: `Prowler` Lv 4, AttacksOnSight, aggro 8 m, range 1.8 m, abilities from current species (0.25 s bite x1.0 w45, 0.25 s x1.0 w40, 0.55 s lunge x1.5 heavy w15), patrol Wander r8, leash 15 m past zone edge, zone r30 x 6 monsters, corpse 3 s + 1.4 s, respawn 8 + 0.12 x level s. Acceptance for P0: all brain tests green in EditMode; zero GC allocations in the brain loop (`GC.GetAllocatedBytesForCurrentThread` over 1,000 ticks); a Burst compile check of every job. P1: two clients see the same positions; wolf aggro lands within 0.3 s; leash reset heals; 102-monster synthetic load < 0.3 ms/tick (editor Burst on).

Rough effort: P0 ~ 12 files / 1,800 lines + tests ~ 900; P1 +6 files; P2 +2 files.

## 19. Risks and open items
- Package versions for Unity 6.6 (Entities, Netcode, Graphics): unknown to this session; docs read were 1.3-line pages [V pages but version relation U].
- Per-ghost send rate API, exact system group names, enableable-component skipping in the installed Entities: verify before coding [U].
- `NavWorld`: referenced by the NavMeshQuery page as the successor; its page was not reachable (404) [U]. If it turns out Burst/ECS-ready, section 7 option A becomes the best nav in Runs/caves; the grid remains the fallback for open zones.
- The player side is the larger problem: monster-to-player and player-to-monster damage cross the NGO/ECS boundary until players are ECS. The bridge keeps rules in one place but adds one tick of latency (~33 ms, acceptable).
- `IHostile` surface and Autopilot/Magnet hunt radius queries need the `MonsterQuery` API from stage B; check with `graphify affected`.
- Wallstride (players on walls) and "no monster follows into town" depend on player flags in `PlayerSnapshot`; the hybrid bridge must fill them every tick.

## 20. Sources (WebFetch this session)
1. Entities ISystem: https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/systems-isystem.html [V: ISystem unmanaged, OnCreate/OnUpdate/OnDestroy Burst-compilable, schedule IJobEntity/IJobChunk from OnUpdate]
2. Entities blob assets: https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/blob-assets-concept.html [V: immutable, value types only, BlobBuilder, pass by ref]
3. Netcode ghosts: https://docs.unity3d.com/Packages/com.unity.netcode@1.3/manual/ghost-snapshots.html [V: Interpolated/Predicted/Owner predicted modes, quantization, importance] and .../optimizations.html [V: GhostRelevancy modes, GhostDistanceData/GhostImportance, GhostSendSystemData limits, write-access serialization cost, Optimization Mode Dynamic/Static] and .../ghost-types-templates.html [V: GhostField quantization/smoothing]
4. NavMeshQuery (Experimental.AI): https://docs.unity.com/en-us/engine/current/script-reference/unityengine/experimental/ai/navmeshquery [V: usable in IJob/IJobParallelFor, deprecated -> NavWorld, Burst not mentioned, Begin/Update/EndFindPath, MapLocation, Raycast]
5. AI Navigation package intro https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/index.html (no Burst/ECS statement) ; Unity Physics intro https://docs.unity3d.com/Packages/com.unity.physics@1.3/manual/index.html (says deterministic rigid body dynamics + spatial query, no API detail)
6. Burst float determinism (from shared context, not re-fetched): https://docs.unity3d.com/Manual/burst/float-precision-determinism.html
Not reachable / not verified: NavWorld page (404), per-ghost send interval, Entities 1.4+/6.x differences, Entities Graphics skinned mesh support, Unity animation-in-ECS status.
