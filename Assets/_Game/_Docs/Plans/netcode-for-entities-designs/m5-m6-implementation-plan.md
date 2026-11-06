# M5 / M6 implementation plan: build the Brain step by step

Owner order (6 Nov 2026, via Nova): "write the ACTUAL implementation plan now, so that when a DOTS session reaches M5/M6 the
planning is done and it just builds." Companion of `m5-m6-brain-plan.md` (the what and why; owner answers Q1-Q8 = A, confirmed
6 Nov) and `monster-ai-design.md` (29 Oct component reference). Where they differ, **this file wins**.
Planning only: nothing here is compiled. File:line refs are the clone `C:\Users\dlaco\Krytical Nova II DOTS` on
`dots/netcode-entities` as of 01d4e7320 unless marked NGO (= the same files on main, unchanged since the clone).

---------------------------------------------------------------------------------------------------

## 0. How to use this file (every M5/M6 session reads this first)

1. One step = one commit (local, never pushed), in order. A step is done when every **Done when** line is true.
2. **Before code in a step:** read the rules rows it lists (section 3) and the files it changes; `graphify explain` every type it
   touches; put "graphify first" in any subagent prompt.
3. **Ground rules (migration plan s0) apply unchanged:** test ports only (DOTS server 7797/8097, save `--test` 7802), never the
   real server, saves or ports 7777/7778/7801/8090; no Editor in the clone without the Master's OK; `dependency_map.py --check`
   (0 uphill, 0 two-way), `compile_check.py` and `--server` before each commit; `graphify update .` after; the milestone ends
   with the 2-client test on the dedicated test server with Game-view screenshots/GIFs; Encyclopedia after each landed step.
4. **Runs and RAM (DOTS 14/15 practice):** batch only (`-batchmode -nographics`), below-normal priority, one Unity at a time.
   EditMode wide filter needs ~13 GB free (peak ~13.4 GB commit), PlayMode ~12 GB; kill if free < 3 GB. **Max 2 real runs per
   step** (watchdog kills don't count); a third failure = stop and send Nova options. Runner: DOTS 11's `run_step.ps1`
   (scratchpad `54ab4afd...`), filter for this work:
   `-EditFilter "KryticalNova\.(Brains|Hostiles\.Entities|Combat\.Entities|Player\.Entities)"`.
5. **The owner may be in the DOTS Editor.** Fast Script Reload hot-reloads any `.cs` under Assets mid-Play, and ISystem/Burst code
   cannot hot-reload. While the owner plays: write to scratch copies only, touch nothing under Assets (DOTS 14 rule).
6. **Never block jobification** (owner, 5 Nov): all per-brain state in components; managed code only in the three named bridge
   systems; mark every managed hot path `// job later (perf phase)` in code and in the step note.
7. **B-test numbers:** M4/M8 used up to B62. **B70-B89 are reserved for M5/M6**, B90-B99 for N1-N4. PlayMode tests go into
   `Persistence/Entities/Tests/PlayMode/HostileSmokeTests.cs` (new file, same fixture style as `RealPrefabSmokeTests.cs`).
8. **Risk tags:** **HIGH** = touches XP, loot, Creds, saves or the death penalty: the step's diff summary goes to Nova before the
   commit (DOTS 14 practice, M4.9 review) and its B-test compares numbers with NGO exactly. MED = gameplay feel. LOW = tools/docs.

---------------------------------------------------------------------------------------------------

## 1. Architecture as it will be built

### 1.1 The key choice for the port: a hidden server body per Hostile (port first, performance later)

Each live Hostile on the server is **two things**:
- an ECS **brain entity that is also the ghost** (`HostileGhost` + `CombatTarget` + `PositionHistory` + brain components): it
  thinks, moves, attacks, and is what clients see;
- a pooled **server body**: a hidden GameObject with the game's own `CombatEntity` + `HostileNet` + a new `EcsHostileBrain`
  (the existing `IHostileBrain` seam, `Hostiles/Runtime/IHostileBrain.cs`), **no Emerald, no Animator, no renderer, no
  NavMeshAgent**. It holds health, the first-hit tag, death, XP, loot, the death penalty, GM tools, exactly as NGO does.

Why: every HIGH rule (XP by level gap, Squad split, pet credit, drops held 20 s, 5 % XP loss once per death, first-hit tag) keeps
running in unchanged NGO code (`HostileNet`, `HostileKillRewards`, `HostileLoot`, `CombatEntity`); player hits already reach
attackable ghosts through a body (`MeleeResolveServerSystem.cs:77-92,123-133` resolves `CombatTargetRegistry` entries by
`IDamageable` on `Body`), the same way M4.2b's Strike Dummy works (`StrikeDummySystems.cs`); M4.9's `HostileStrike` queue already
expects a stand-in attacker with `HostileNet` (`HostileStrikeSystems.cs:14-30`). Zones, respawn, sleep and the GM commands keep
working on `HostileNet.Active`. The **brain** (the owner's ask: faster, smarter) is all new and all Burst.
Performance phase (after the port; owner direction 5 Nov): bodies are replaced by ECS vitals for The Scar's 2,000 Hostiles. The
body is touched only by the three bridge systems, so that swap is local.

### 1.2 Assemblies, folders, namespaces, layers

All under `Assets/_Game/Gameplay/` = **Simulation** layer (`dependency_map.py:60`). Arrows = asmdef references (one way only).

| Folder | Assembly / namespace | References (beyond Unity.Entities/Burst/Collections/Mathematics/Transforms/NetCode) | Notes |
|---|---|---|---|
| `Brains/Runtime/` | `KryticalNova.Brains` | `KryticalNova.Runtime` (Core) only | **new system**; knows no mind |
| `Brains/Data/` | asmref -> `KryticalNova.Brains` | | `BrainSettings` SO + asset |
| `Brains/Tests/Editor/` | `KryticalNova.Brains.Tests.Editor` | Brains, `KryticalNova.Networking.Entities.Tests.Editor` (KnTestWorld) | |
| `Hostiles/Entities/Runtime/` | `KryticalNova.Hostiles.Entities` | Brains, `KryticalNova.Hostiles`, `.Combat`, `.Combat.Entities`, `.Progression`, `.Player.Entities`, `.Player.Entities.Presentation`, `.Networking` | the Monster mind + bridges |
| `Hostiles/Entities/Presentation/Runtime/` | `KryticalNova.Hostiles.Entities.Presentation` | Hostiles.Entities, `.Hostiles`, `.Rendering`, `.Player.Entities.Presentation` | client views; same platform settings as `Player.Entities.Presentation` |
| `Hostiles/Entities/Editor/` | `KryticalNova.Hostiles.Entities.Editor` | Hostiles.Entities, `.Hostiles`, `.Player.Entities.Editor` | ghost prefab + body prefab builder |
| `Hostiles/Entities/Tests/Editor/` | `KryticalNova.Hostiles.Entities.Tests.Editor` | as Combat.Entities.Tests.Editor + Brains + Hostiles.Entities | |
| `NPC/Entities/...` (N1+) | `KryticalNova.NPC.Entities` | Brains, `KryticalNova.NPC`, `.World` | Townsfolk mind |

Who installs: `Persistence.Entities` (`PlayerWorldsSetup.InstallServer/InstallClient`, `PlayerWorldsSetup.cs:72,159`) gains a
reference to `Hostiles.Entities` (+ `.Presentation` on the client side) and calls `HostileEntitiesSetup.InstallServer(world)` /
`InstallClient(world)`. Check before the first commit: no assembly under `Combat/`, `Player/`, `Progression/` may reference
`Hostiles.Entities` or `Brains` (none does today; test asmdefs excluded as now).

### 1.3 One server tick, in order (group `BrainSimulationGroup`, server world, in `SimulationSystemGroup`, after the player's
server movement/melee systems and `PositionHistoryWriteSystem`, before `GhostSendSystem`)

```
[managed] HostileBodyLinkServerSystem   bodies appeared/left -> create/destroy ghost+brain, CombatTargetRegistry      (job later)
[managed] HostileBodySyncServerSystem   body -> ECS: HP, alive, tag, asleep, frozen, BrainEvents; ECS -> body: pose   (job later)
[Burst]   SenseBuildSystem              players + brains -> grids (Brains)
[Burst]   ThinkClockSystem              tiers, NextThinkTick, events bump to tier 0 (Brains)
[Burst]   HostileThreatSystem           BrainEvents -> ThreatEntry, target switch 10/30 %
[Burst]   HostileSenseScanSystem        AttacksOnSight scan on think ticks -> Alert/notice
[Burst]   HostileDecideSystem           mode machine + target/ability scores
[Burst]   HostileAttackSlotSystem       stage manager: grid/attack capacity per player
[Burst]   PathServiceSystem             queued path jobs within budget (Brains)
[Burst]   BrainMoveSystem               steering, separation, ground height (Brains)
[Burst]   HostileAttackSystem           windup -> commit -> resolve -> PendingStrike
[managed] HostileStrikeBridgeServerSystem  PendingStrike -> HostileStrikeRules.TryRoll -> HostileStrikes.Queue        (job later)
[Burst]   HostileGhostWriteSystem       ghost fields only when changed; CombatTarget FeetMm/HP
```
`HostileStrikeServerSystem` (M4.9, Combat.Entities) then applies the queued blows on player server bodies as today.

### 1.4 Data (designer data stays ScriptableObjects; numbers in settings assets, never in code)

- **`BrainSettings`** (`Brains/Data/BrainSettings.cs`, asset `Brains/Data/Assets/BrainSettings.asset`): `tierIntervalTicks`
  [1,3,15], `tierRadius` [12,40,120] m, `tierDownHysteresisSeconds` 2, `engagedLingerSeconds` 5, `playerCell` 16 m,
  `brainCell` 4 m, `separationRadius` 1.2 m, `pathSearchesPerTick` 8, `pathNodeBudget` 2000, `repathMinSeconds` 0.3,
  `repathTargetMoved` 1.5 m, `maxWaypoints` 16, `engagedTurnDegPerSec` 540, `idleTurnDegPerSec` 270, `worldSeed`.
- **`HostileBrainSettings`** (`Hostiles/Data/HostileBrainSettings.cs`, asset in `Hostiles/Data/Assets/`): `noticeSecondsDefault`
  0.1, `reachSlack` 0.3 m, `commitFraction` 0.6, `facingDegrees` 60, `minVisibleWarningSeconds` 0.35, `threatSwitchMelee` 1.10,
  `threatSwitchRanged` 1.30, `threatDecayAfterSeconds` 5, `threatDecayPerSecond` 0.05, `healThreatFactor` 0.5, `tauntBonus` 0.10,
  `slotRingRadius` 2.0 m, `waitRingRadius` [3.5,5] m, `gridCapacity` 12, `attackCapacity` 10, `rangedAttackCapacity` 6,
  `gridWeightNormal` 4, `gridWeightBruiser` 8, `attackCostLight` 3, `attackCostHeavy` 5, `maxSlotWaitSeconds` 2,
  `assistRadius` 6 m, `unreachableSeconds` 6, `relevancyRadius` 150 m, `viewRadius` 45 m, `viewBudget` 32.
- **`HostileDefinition`** (exists) gains per species: `noticeSeconds` (-1 = default), `poise` (0-100), `assist` (bool, default =
  behaviour == AttacksOnSight), `flees` (false), `scoreProfile` (enum: Brute/Skirmisher/Finisher, default Brute). `attacks[]`
  windups follow Q3: light 0.25 -> **0.40 s**, heavy 0.55 stays (M6.1 data change, by `HostileAssetBuilder`'s table).
- **Blobs** baked at server start from the SOs (`HostileArchetypeBlob` per species: ids, ranges, abilities {windup ticks, commit
  tick, recover ticks, cooldown ticks, range mm, arc cos, multiplier, heavy, weight, attackCost}, score curves; `BrainConfigBlob`).
  Built by a managed `HostileArchetypeBakeSystem` once (OnCreate) from the roster asset, not by subscene bakers, so no rebake is
  needed when designers change numbers.

---------------------------------------------------------------------------------------------------

## 2. Components and ghost fields (reference; each step says which it adds)

### 2.1 Brains (`KryticalNova.Brains`)
```csharp
struct BrainTag : IComponentData {}
struct BrainActive : IComponentData, IEnableableComponent {}            // off = asleep (zone asleep, frozen-by-sleep, dead)
struct BrainClock : IComponentData { uint NextThinkTick; byte Tier; uint TierSinceTick; uint EngagedUntilTick; byte ThinkNow; }
struct BrainDice : IComponentData { Unity.Mathematics.Random Rng; }      // Random.CreateFromIndex(stableId ^ worldSeed)
struct BrainMover : IComponentData { float3 Dest; float Speed; float TurnRateRad; float Yaw; byte Flags; } // Flags: Stop 1, Run 2, Face 4
struct BrainWaypoint : IBufferElementData { float3 P; }                  // InternalBufferCapacity 16
struct BrainPath : IComponentData { float3 To; uint RequestTick; uint ReadyTick; byte Status; byte Priority; } // None/Queued/Ready/Failed
struct BrainEvent : IBufferElementData { byte Kind; int GhostId; float Amount; uint Tick; } // Damaged 1, Aggro 2, Calm 3, Freeze 4, Unfreeze 5, Killed 6, Assist 7, Healed 8, Taunt 9
struct SensedPlayer { Entity Ghost; int GhostId; float3 Feet; byte Flags; byte Faction; }    // Alive 1, Neutral 2, OnWall 4, Ignored 8, Dead 16
struct SenseWorld : IComponentData { NativeList<SensedPlayer> Players; NativeParallelMultiHashMap<int2,int> PlayerGrid;
                                     NativeParallelMultiHashMap<int2,Entity> BrainGrid; NativeParallelHashMap<int,int> ByGhostId; } // singleton, owned by SenseBuildSystem
struct ScoreCurve { byte Shape; float M, K, B, C; }                       // Linear/Quadratic/Logistic/Logit (IAUS), x and y clamped 0..1
static class Score { float Evaluate(in ScoreCurve c, float x); float Combine(...); } // pure, Burst
static class BrainRules { byte TierFor(float nearestSq, bool engaged, in BrainConfig c); uint NextThink(...); } // pure
```

### 2.2 Hostiles mind (`KryticalNova.Hostiles.Entities`)
```csharp
struct HostileBrain : IComponentData { byte Mode; byte Phase; uint ModeSinceTick; uint PhaseEndTick; uint CommitTick;
                                       byte Ability; uint NoticeUntilTick; uint UnreachableSinceTick; uint NextScanTick; }
   // Mode: Idle 0, Patrol 1, Alert 2, Engage 3, Return 4, Flee 5, Dead 6. Phase (Engage): Approach 0, Windup 1, Recover 2, Wait 3
struct HostileLife : IComponentData { int BodyKey; ushort Species; short Level; byte Tier; float AggroRadius, Walk, Run,
                                      AttackRange, ReachAbove; int RadiusMm, HeightMm; byte Behaviour; byte Poise; uint NoticeTicks; }
struct HostileArchetypeRef : IComponentData { BlobAssetReference<HostileArchetypeBlob> Blob; }
struct HostileHome : IComponentData { float3 Home; float WanderRadius; float2 ZoneCentre; float ZoneRadius; float LeashRadius; }
struct HostileTarget : IComponentData { Entity Ghost; int GhostId; float3 LastFeet; float DistSq; byte Flags; } // InReach 1, Facing 2, Above 4, Unreachable 8
struct ThreatEntry : IBufferElementData { int GhostId; Entity Ghost; float Threat; uint LastTick; }   // capacity 6, prune lowest
struct HostileCooldowns : IComponentData { FixedList32Bytes<uint> ReadyTick; }
struct AttackSlotClaim : IComponentData { Entity Player; sbyte Slot; byte GridWeight; byte Paying; uint ClaimTick; }
struct AttackSlotTable : IComponentData { NativeParallelHashMap<Entity, SlotState> ByPlayer; }  // singleton; SlotState { byte GridUsed, AttackUsed, RangedUsed; byte SlotMask; }
struct PendingStrike : IBufferElementData { Entity Hostile; Entity Target; byte Ability; float3 From; byte Heavy; } // singleton queue
struct HostileGhost : IComponentData {                                  // ghost, Interpolated only
  [GhostField] ushort Species; [GhostField] short Level; [GhostField] byte Tier;            // static optimisation
  [GhostField] byte Mode; [GhostField] byte Speed8;                                         // m/s * 20
  [GhostField] uint AttackStartTick; [GhostField] byte AttackIndex; [GhostField] int AttackTargetGhostId;
  [GhostField] byte HitCounter; [GhostField] byte Flags;   // InCombat 1, Stunned 2, Dead 4, OnGround 8, Asleep 16, Heavy 32
  [GhostField] uint DeadTick; [GhostField] int TagHolderGhostId; }
// plus LocalTransform (position quantisation 20 = 5 cm, yaw via rotation quantisation), CombatTarget (Kind = CombatTargetKinds.Hostile = 2;
// existing ghost fields FeetMm/HealthMilli/MaxHealthMilli/RadiusMm/HeightMm), PositionHistory (server only, existing ring 16 ticks).
```
`CombatTargetKinds.Hostile = 2` is added in `Player/Entities/Runtime/CombatTargets.cs:28-32` (one constant; the doc comment
there already says "M5: Hostiles").

### 2.3 Managed seams added to NGO code (small, behaviour-neutral on main; land as `seams/` PRs per migration plan s13)
- **[done, DOTS 15, 6 Nov]** `HostileSpawner.AuthorityOverride` (`Func<bool>`, null on main): when set, `HasAuthority` (`HostileSpawner.cs:18-25`) returns it.
  As built: unset, `HasAuthority` = no live entity-netcode worlds (Core `EntityNetcodeWorlds.Live`, filled by `KnBootstrap` at runtime) and
  the NGO rule; `HostileNet.HasAuthority`/`Offline` follow it (`HostileSpawner.EntityNetcodeAllows`), so no zone spawns and no body wakes
  while DOTS worlds run, until M5.5 sets the override for its server bodies. `HostileAuthorityTests`.
  DOTS sets it to "this process has a server world", so **clients stop running offline NGO Hostiles** (today they do: NGO is
  not listening on DOTS, so `HasAuthority` is true everywhere).
- **[done, DOTS 15, 6 Nov: `Hostiles/Runtime/HostileStrikeRules.cs`, HostileBrainLink.Strike calls it; `HostileStrikeRulesTests`]** `HostileStrikeRules.TryRoll(CombatEntity attacker, HostileDefinition def, int attackIndex, CombatEntity target, Transform attackerBody, out DamageResult)`
  (M5.0: a Transform, not a Vector3: the wall ray skips the attacker's own colliders with `IsChildOf`):
  the guard + roll part of `HostileBrainLink.Strike` (NGO `HostileBrainLink.cs:237-286`: reach-above, `OnAWall`/`ReachesOnWall`,
  multiplier, reflection, `CombatCalculator.CalculateAttack`) moved into `KryticalNova.Hostiles` (no Emerald). `HostileBrainLink`
  calls it, so NGO behaviour is identical (its own tests prove it).
- **[done, DOTS 15, 6 Nov; B70]** As built: `BindView(level)`, `ShowLevelFromGhost`, `ShowStateFromGhost(state, joining)`,
  `ShowAttackFromGhost(index)`, `ShowHitFromGhost`, `ShowStrikeFromGhost(target, result, index, targetIsLocal)` (separate calls, the
  view system dedupes by its ghost counters); `IsView` closes HasAuthority, the OnEnable revive and the Start wake. Left for M5.5 (they
  need ghost fields): the kill-tag holder for `IsTaggedByAnother`, and `CombatEntity.Died` on the view for `HostileCorpse`.
- `HostileNet` view mode: `HostileNet.BindView()` marks a client view (never authority, brain never woken, `Offline` false) and
  `HostileNet.PlayFromGhost(HostileAnimState, attackIndex, startedSecondsAgo, hitCounter)`; its `Active` list, `IHostile`
  surface, level/tag events keep feeding nameplates, minimap, Autopilot/Magnet, target ring unchanged.
- `HostileBodyCatalog` (asset, `Hostiles/Data/Assets/HostileBodyCatalog.asset`): species prefab -> server body prefab. `HostileZone`
  and `HostileSpawner.Spawn` take the body prefab when `HostileSpawner.UseServerBodies` is true (set by DOTS server install).

---------------------------------------------------------------------------------------------------

## 3. NGO rules the port must match (M5.0 turns this into `m5-rules-tables/M5-hostiles.md` with every line checked)

Rows marked `[line: M5.0]` have the file but the line is filled in M5.0. "Test" names the test that proves the DOTS side.

| Id | Rule (NGO) | NGO source | DOTS step | Test |
|---|---|---|---|---|
| H01 | Aggro radius = 8 m + 1 m per 28 levels, cap 12 | `HostileStatCurve` [line: M5.0] | M5.3 | `HostileRulesTests.AggroRadius_MatchesCurve` |
| H02 | FightsBack never aggroes by sight; AttacksOnSight does; levels 1-7 FightsBack, 10+ AttacksOnSight | `HostileBrainLink.cs:112-118`, roster | M5.3 | `HostileBrainTests.FightsBack_IgnoresPlayerInRange` |
| H03 | A hit turns a Hostile on its attacker at once | `HostileBrainLink.cs:195-196` | M5.3 | `ReactionTests.HitTurnsSameTick` |
| H04 | Walk 1.8 m/s, run 4.2 + 0.006/level | `HostileStatCurve`; prefab `WalkSpeed/RunSpeed` | M5.3 | `HostileRulesTests.Speeds_MatchCurve` |
| H05 | Three attacks per species, weights 45/40/15, multipliers 1.0/1.0/1.5, third heavy | `HostileAssetBuilder.cs:323-325`, `HostileDefinition.attacks` | M5.3, M6.1 | `HostileScoreTests.FlatCurves_EqualWeightedDice` |
| H06 | Hit roll = `CombatCalculator.CalculateAttack(mine, target, multiplier, reflects, reflectChance)` | `HostileBrainLink.cs:248-258` | M5.6 | `StrikeRulesTests.TryRoll_EqualsNgoStrike` |
| H07 | No hit on a player higher than `reachAbove` (3 m default) | `HostileBrainLink.cs:241` | M5.3 | `HostileAttackTests.AboveReach_Misses` |
| H08 | Player on a wall (up > 30 deg): only a straight clear line within range + 0.5 m + eye 1.2 m lands | `HostileBrainLink.cs:244,262-286` | M5.6 | `StrikeRulesTests.WallRule` (B73) |
| H09 | Damage on the player through `TakeDamage`; hit player sees the number; 5 % XP loss once per death to a Hostile | M4.9 `HostileStrikeSystems.cs`; NGO `HostileKillRewards.cs:161-175` | M5.6 | B54 (exists) + B74 |
| H10 | First hit holds the kill; tag released after 10 s without damage, on holder death/leave, on leash | `HostileNet.cs:335-399` | M5.6 | B75 |
| H11 | Kill XP by level gap, Squad split, Sponsor, pet credit | `HostileKillRewards.cs:52-160` | M5.6 | B76 (compare XP with NGO numbers) |
| H12 | Drops: `HostileLoot`, Creds as item 9040, held 20 s for the holder | `HostileLoot.cs`, `HostileNet.OnKilled` `:265` | M5.6 | B77 |
| H13 | Corpse 3 s + 1.4 s dissolve, then pool | `HostileCorpse.cs`, `HostileSettings` | M5.5 | B72 |
| H14 | Leash: 15 m past the zone edge; target on neutral ground dropped; out of reach 6 s gives up; return heals, clears | `HostileSettings`, `HostileZone.cs` [line: M5.0] | M5.3 | `HostileBrainTests.Leash_*` |
| H15 | Zone tick 0.5 s; wake at radius + 40 m; sleep ends fights and heals everything | `HostileZone.cs:100-160` | M6.2 | `ZoneSleepTests` + B80 |
| H16 | Respawn Normal 8 + 0.12/level s (8-22), Bruiser 480-900 s; spawn clearance 3 m; Bruiser at 0.85 x radius | `HostileZone`, `RespawnQueue.cs` | M6.2 | (NGO code reused) B81 |
| H17 | Hostile ignore list (`HostileIgnore`) excludes a player from targeting | `HostileBrainLink.cs:50-60`, `HostileIgnore.cs` | M5.3 | `HostileBrainTests.IgnoredPlayer_NeverTargeted` |
| H18 | Threat note to the targeted player every 0.5 s (keeps PlayerCombatState in combat) | `HostileNet.cs:36,410-428` | M5.6 | B74 |
| H19 | Level gap: a Hostile above a player is harder to hit (`IHitChanceModifier`) | `HostileNet.cs:404` | unchanged (body) | B76 |
| H20 | GM: `gm.hostile.*` / `gm.zone.*`, freeze (stands still, takes hits), aggro, calm, describe | `HostileCommands.cs`, `IHostileBrain` | M6.3 | `GmHostileTests` + B82 |
| H21 | Client sees attack start with windup and heavy "!" + Feel swing; flinch; death clip | `HostileNet.cs:544-575` | M5.5 | B71, B72 |
| H22 | Nameplate, minimap, Autopilot/Magnet, target ring read `HostileNet.Active` / `IHostile` | `graphify affected "HostileNet"` | M6.4 | B83 |
| H23 | Airborne deaths de-rez (owner rule, 5 Nov) | memory airborne-death-derez | M5.6 | B78 |
| H24 | Player area skills hit every Hostile in range, no cap (owner rule, 5 Nov) | memory area-skills-no-target-cap; `MeleeResolveServerSystem.cs:85-112` | M6.5 | B84 |
| H25 | Emerald behaviours the owner rejected as laggy are **not** matched: 1 s scan, animator-gated swings, flinch cancels swing | brain plan s1.2 L1-L11 | M5.3 | `ReactionTests` (the opposite is asserted) |

---------------------------------------------------------------------------------------------------

## 4. M5 steps

### M5.0 NGO rules table (S, LOW, no code)
- **Make:** `_Docs/Plans/netcode-for-entities-designs/m5-rules-tables/M5-hostiles.md`: section 3 rows with every `[line: M5.0]`
  filled and checked, plus every number of `HostileSettings`/`HostileStatCurve` assets as they are today (values from the `.asset`
  files), the `HostileNet` consumer list from `graphify affected "HostileNet"` and `"IHostile"`, and the M4.9 queue contract.
- **Seam check:** confirm `HostileSpawner.HasAuthority` is true on DOTS clients today (it is by code; the owner's GUI note of
  5 Nov) and list where offline NGO Hostiles appear on a client.
- **Owner hand test:** none. **Done when:** Nova signs the table (owner rule: table before code).

### M5.1 Brains core (M, LOW)
- **[done, DOTS 15, 6 Nov]** As built: `Gameplay/Brains` (Core-only asmdef; Data asmref; `BrainSettings.asset`), `BrainConfig.From(settings,
  tickRate)`, `BrainSimulationGroup` (server, after `PredictedSimulationSystemGroup`) with `BrainTickSystem` (NetworkTime -> `BrainTick`),
  `SenseBuildSystem`, `ThinkClockSystem` (Burst ISystems); `BrainRules`, `Score`, `SenseQueries`, `BrainDiceRules` (pure). Deviations:
  (1) Brains reads only `SenseWorld.Players`: the feeder from player ghosts (`PlayerPose`, vitals, wall, neutral ground) is M5.3's, in
  Hostiles.Entities, since Brains references Core only; (2) the test world is a plain `World` ticked by hand through `BrainTick`, not
  KnTestWorld; (3) a brain never scheduled takes its first tier at once (the hysteresis is for changes). Tests: 9 (clock, grid vs brute
  force, curves, early-out, determinism, no allocations). Linux IL2CPP + Burst server build: not run (no build in this step; compile
  checks with server defines). Job later: the clock's nearest-player query and the grid fill.
- **Create:** `Brains/Runtime/` `KryticalNova.Brains.asmdef`, `BrainComponents.cs` (2.1), `BrainSimulationGroup.cs`,
  `SenseBuildSystem.cs`, `ThinkClockSystem.cs`, `BrainRules.cs`, `Score.cs`, `BrainDiceRules.cs`;
  `Brains/Data/BrainSettings.cs` + `KryticalNova.Brains.Data.asmref` + asset (via a menu builder in Hostiles.Entities.Editor or a
  `[CreateAssetMenu]`; asset path constant `BrainPaths.SettingsAsset`); `Brains/Tests/Editor/` asmdef +
  `BrainTestWorld.cs` (wraps `KnTestWorld`: fake `SensedPlayer` feeder, step N ticks).
- **SenseBuildSystem** reads player ghosts in the server world (`PlayerPose.Mm` feet, alive from `PlayerVitals`, neutral ground
  from the `TradeZone` rule the player side uses, on-wall from the wall state component; exact component names from
  `graphify explain "PlayerPose"` / `"WallWalk"` in the step) into `SenseWorld`. In tests a feeder writes `SensedPlayer` directly.
- **Tests (EditMode):**
  - `ThinkClockTests.Stagger_SpreadsThinksEvenly`: 300 brains, tier 1 (every 3 ticks) -> each tick 100 +- 1 think.
  - `ThinkClockTests.TierFromDistance`: nearest player 10 m -> 0, 30 m -> 1, 100 m -> 2, 200 m -> 3 (no think).
  - `ThinkClockTests.TierDown_WaitsHysteresis`: from tier 0 to 1 only after 2 s (60 ticks) out of range; up is immediate.
  - `ThinkClockTests.EventBumpsToTier0_SameTick`: a `BrainEvent` at tick T sets `ThinkNow` on tick T.
  - `SenseGridTests.Query_FindsAllWithinRadius`: 500 random players, 1,000 random queries vs brute force, equal sets.
  - `ScoreTests.Curves_MatchFormulas` (each shape at 11 points, 1e-5), `ScoreTests.EarlyOut_SameWinner`.
  - `BrainDeterminismTests.SameSeed_SameState_1000Ticks`; `BrainGcTests.NoAllocs_1000Ticks` (`GC.GetAllocatedBytesForCurrentThread` delta 0 after warm-up).
- **Owner hand test:** none (no visible change). **Done when:** tests green, dependency check 0/0, Linux server compile with Burst.

### M5.2 NavWorld spike (S, LOW, throwaway branch code; keep only the report)
- **[done, DOTS 15, 6 Nov: report `m5-rules-tables/M5.2-navworld-spike.md`] Nova's pick: A** (NavWorld for monsters). **Gate before
  M5.4:** the Linux proof (a tiny throwaway project, one Burst NavWorld job, built for Linux, run under WSL; a full-game IL2CPP Linux
  build is for a bigger machine); if it fails, the baked grid (B). Links and heights: snap corners/heights to the detail mesh, or
  fall back to `CalculatePath` across NavMeshLinks. The players' later option C stays blocked until link corners and heights match.
  Real API names: `NavNode`, `NavQueryStatus`, `ContinueFindPath`; map monsters with a box >= 2 m tall.
- In `C:\Users\dlaco\KN-worktrees\dots-spike` (the M1 throwaway project style) or a scratch folder in the clone not committed:
  one Burst `IJob` that maps two points with `NavWorld.MapLocation`, runs `BeginFindPath/ContinueFindPath/EndFindPath`, reads the
  corridor, turns it into corners with a simple funnel (string-pull over `GetPortalPoints`); 200 queries on Zone01's NavMesh
  timed against `NavMesh.CalculatePath`; then the same in a **Linux IL2CPP + Burst dedicated server build** (WSL, test lane).
- **Report** `m5-rules-tables/M5.2-navworld-spike.md`: works in Burst? in the Linux build? cost per query; funnel corners equal
  `CalculatePath` corners within 0.1 m on 20 sample pairs. **Pick A (NavWorld) if all yes, else B (baked grid, old design s7).**
- **Done when:** report + Nova's pick. No commit of spike code.

### M5.3 Monster mind P0 on one stand-in (L, MED)
- **Split (DOTS 15):** M5.3a **[done, 6 Nov]** = `Hostiles/Entities` assembly (refs Core, Brains, Hostiles only for now),
  `HostileComponents.cs` (brain, life, home, target, threat, cooldowns, `PendingStrike`), pure `HostileRules.cs` (H01 ceil'd aggro
  radius, H04 speeds, Q8 notice ticks, reach/H07, facing, commit tick, Q5 switch, H14 leash, `NextMode` with the leash return healing
  and Emerald's 5 s give-up not), `HostileBrainSettings` (+ asset; `giveUpSeconds` 5 = H14 parity instead of `unreachableSeconds` 6)
  and `HostileBrainConfig`; `HostileRulesTests` (7). M5.3b = the blob, the player feeder for `SenseWorld` (Brains reads only
  `SenseWorld.Players`), the scan/threat/decide/attack systems and the reaction tests below.
  M5.3b1 **[done, 6 Nov]**: `HostileThreatSystem` (events -> threat: hit = damage, heal x0.5, taunt above the top; decay after 5 s at
  5 %/s; 6 kept, the lowest replaced; target = top threat when none, switch at > 110 % in reach / > 130 % out) and
  `HostileSenseScanSystem` (think ticks; AttacksOnSight in Idle/Patrol only; nearest living player in the ceil'd aggro radius, not on
  neutral ground nor ignored -> `HostileBrain.Saw` + target). `HostileTestWorld` (plain World) + ThreatTests, SightScanTests (4).
  M5.3b2a **[done, 6 Nov]**: `HostilePlayerFeedSystem` (player ghosts -> `SenseWorld.Players`: ghost id, feet from the pose, Alive/Dead
  as the melee rules, OnWall; runs only where `HostileBrainConfig` exists); Neutral and Ignored flags still to add (managed rules).
  M5.3b2b **[done, 6 Nov]**: `HostileDecideSystem` (NextMode on live state: notice ticks, Emerald's 5 s give-up beyond the
  detection radius without a heal, the leash return that heals and clears threat at home, the next valid threat, `BrainMover` dest
  for Engage/Return); Brains: a brain moving to a nearer tier thinks on that tick (sight reaction worst 4, mean 4 ticks; plan <= 6 / 4.5).
  Tests: ReactionTests (SightAggro, HitTurnsSameTick), HostileBrainTests (Leash, GiveUp, NeutralGround).
  M5.3b2c **[done, 6 Nov]**: `HostileAttackSystem` (Engage + in reach + facing + ready -> windup that tick, picked by weight with the
  brain's dice; turns with the target; cancel before the commit when the target is beyond range + 0.3 + 1 m; resolve at the windup's end
  -> `PendingStrike` queue, else a miss; recover, then `attackCooldownSeconds` 0.35 (new setting); hits never cancel). Attack rows sit in
  a per-Hostile `HostileAbilityRow` buffer until the species bake fills them. SwingTests (4).
  Neutral/Ignored **[done 6 Nov]**: `HostileSenseFlagsServerSystem` (managed bridge: `TradeZone.CrossFactionAt`, `HostileIgnore` by the
  server body; no test of its own: it needs a scene's zones); the decide step treats a target on neutral ground as the leash (H14 parity:
  home, heal); a dead target gives way to the next threat. M5.3b2d (next): the archetype blob + bake (fills the ability rows from
  `HostileDefinition.attacks`),
  patrol **[done 6 Nov: in HostileDecideSystem, pause `patrolPauseSeconds` (3, 6) by the brain's dice, a walk to a point within the
  wander radius, idle on arrival; `PatrolTests` 10,000 ticks]**. **[done 6 Nov]** the dice test (100,000 picks within 1 % of 45/40/15; `HostileRules.PickAbility`),
  determinism (50 Hostiles, 5 players, hits, 1,000 ticks) and no GC over the whole mind (`HostileScoreTests`, `HostileMindDeterminismTests`).
  (NextMode, notice, give-up clock, leash, patrol), `HostileAttackSystem` (windup/commit/resolve -> `PendingStrike`), the reaction,
  HostileBrain, score, determinism and GC tests.
- **Create (Hostiles/Entities/Runtime):** `HostileEntities.asmdef`, `HostileComponents.cs` (2.2 minus ghost), `HostileRules.cs`
  (pure: aggro radius, speeds, notice ticks, leash, reach, facing, commit tick, threat switch), `HostileScore.cs` (target and
  ability scoring over blob curves), `HostileArchetypeBlob.cs`, `HostileArchetypeBakeSystem.cs`, `HostileSenseScanSystem.cs`,
  `HostileThreatSystem.cs`, `HostileDecideSystem.cs`, `HostileAttackSystem.cs` (writes `PendingStrike`; no bridge yet),
  `HostileEntitiesSetup.cs` (`InstallServer` adds the group's systems); `Hostiles/Data/HostileBrainSettings.cs` + asset.
  **Tests** asmdef + `HostileTestWorld.cs` (BrainTestWorld + spawn a brain from a `HostileDefinition` with a fake body key).
- **Behaviour (state table, pure `HostileRules.NextMode`):** Idle/Patrol -(AttacksOnSight scan finds a valid player within aggro
  radius)-> Alert for `NoticeTicks` (0.1 s = 3 ticks) -> Engage; any mode -(Damaged event)-> Engage same tick (no Alert);
  Engage -(target invalid: dead, neutral ground, ignored)-> next threat or Return; -(beyond leash or Unreachable >= 6 s)-> Return;
  Return -(home within 0.5 m)-> Idle, healed, threat cleared (event to body); Patrol: wander point within 8 m of home, pause 3-6 s
  (Emerald `MinimumWaitTime 3` / `MaximumWaitTime 6`, prefab values).
- **Attack:** in Engage when `InReach` (distance <= range + 0.3 m slack, height <= reachAbove) and `Facing` (<= 60 deg), slot granted
  (M5.3 uses an always-grant stub; M6.5 adds the stage manager) and cooldown ready -> Windup at tick T: `AttackStartTick = T`,
  `CommitTick = T + round(0.6 x windupTicks)`; before CommitTick it turns to track the target and may cancel if the target leaves
  range + 1 m; at `T + windupTicks` resolve: target still alive, in range + slack, height, arc -> `PendingStrike`; else miss.
  Recover ticks then cooldown 0.35 s (Emerald `Type1AttackCooldown`, NGO feel kept).
- **Flinch never cancels:** Damaged events add threat and `HitCounter++` only; a Windup is interrupted only when
  `poise < incoming heavy/stun` (data; M5.3: poise 100 = never).
- **Tests (EditMode)** (all ticks at 30 Hz):
  - `ReactionTests.SightAggro_WithinFourTicks`: player steps from 13 m to 7 m of an AttacksOnSight Hostile in tier 1 -> Engage
    within **<= 3 + 3 = 6 ticks worst case, mean <= 4.5** over 100 seeds (scan every 3 ticks + notice 3 ticks). Emerald: 0-30 frames.
  - `ReactionTests.HitTurnsSameTick`: idle Hostile, Damaged event at T -> Mode Engage and target = attacker at T.
  - `ReactionTests.SwingStartsWhenInReach`: target walks into reach at T facing -> `AttackStartTick == T`.
  - `ReactionTests.DodgeBeforeCommit_Cancels`, `ReactionTests.DodgeAfterCommit_Misses` (target leaves at commit-1 / commit+1).
  - `ReactionTests.FlinchNeverCancelsWindup`: 10 Damaged events during a windup -> strike still resolves at the windup end.
  - `HostileBrainTests`: FightsBack_IgnoresPlayerInRange; Leash_ReturnsAndHeals (target 16 m past edge); Unreachable_GivesUpAfter6s
    (target 4 m above, 180 ticks); NeutralGround_DropsTarget; IgnoredPlayer_NeverTargeted; Patrol_StaysWithinWanderRadius (10,000 ticks).
  - `ThreatTests`: switch only at > 110 % in reach, > 130 % out of reach; decay after 5 s; capacity 6 prunes lowest; heal threat x0.5.
  - `HostileScoreTests.FlatCurves_EqualWeightedDice` (100,000 picks within 1 % of 45/40/15).
  - `HostileRulesTests`: AggroRadius_MatchesCurve (every level 1-145), Speeds_MatchCurve.
  - Determinism (1,000 ticks, 50 brains, 5 players) and zero GC, as M5.1.
- **Owner hand test:** none yet (no ghost). **Done when:** tests green; Burst compile of every job in the server build.

### M5.4 PathService (M, MED)
- **Create (Brains):** `PathServiceSystem.cs` (queue by priority: engaged first; budget `pathSearchesPerTick` x `pathNodeBudget`;
  results into `BrainWaypoint`), `IPathBackend` as a blob-free struct switch: `NavWorldBackend` (if M5.2 = A) or `NavGridBackend`
  (+ `Brains/Editor/NavGridBaker.cs` writing `Brains/Data/Assets/NavGrid_<map>.asset`, old design s7) ; `StraightLineBackend` for
  tests. `BrainMoveSystem.cs`: seek/arrive, separation from `BrainGrid` within 1.2 m, ground height from the backend
  (`MapLocation` height or grid height), yaw toward motion at the tier's turn rate; straight line first (backend raycast), path
  only when blocked.
- **Repath rules:** engaged: target moved > 1.5 m since the request and >= 0.3 s since it; patrol: once per destination.
- **Tests:** `PathServiceTests.Budget_NeverExceeded` (100 requests in one tick -> 8 served, rest queued, all served by tick 13);
  `PathServiceTests.EngagedFirst`; `PathServiceTests.Failure_SetsUnreachable`; `MoveTests.Separation_NoOverlapBelow0_9m` (20 brains
  to one point, 300 ticks); `MoveTests.ArrivesWithin0_2m`; `MoveTests.TurnRate_540dps`.
  NavWorld backend: one EditMode test on a tiny generated NavMesh (`NavMeshBuilder` in the test) if M5.2 proved it works in EditMode.
- **Owner hand test:** none. **Done when:** tests green, p99 PathService time < 0.2 ms with 8 searches (EditMode timing, Burst on).

### M5.5 Ghost, server body and client view (M, MED)
- **Create:** `Hostiles/Entities/Runtime/HostileGhostAuthoring.cs` (+ baker: `HostileGhost`, `CombatTarget{Kind=2}`, interpolated,
  quantised), `HostileGhostPrefab` singleton; `HostileBodyLinkServerSystem.cs`, `HostileBodySyncServerSystem.cs`,
  `EcsHostileBrain.cs` (MonoBehaviour, implements `IHostileBrain`: SetAwake -> `BrainActive`, Configure/SetHome -> `HostileLife`/
  `HostileHome`, Aggro/Calm/Frozen -> `BrainEvent`, Target -> body of `HostileTarget.Ghost`, Describe -> mode/phase/target text),
  `HostileGhostWriteSystem.cs`. **Presentation:** `HostileViewClientSystem.cs` (pool per species prefab = today's NGO prefab
  `Hostiles/Prefabs/Hostile_<Species>.prefab`; bind nearest `viewBudget` 32 within 45 m; follow interpolated `LocalTransform`;
  `HostileNet.BindView`; `PlayFromGhost`: Speed/InCombat/Stunned/Dead; on `AttackStartTick` change start the attack clip at
  `(interpolationTick - AttackStartTick)/30 s`; flinch on `HitCounter` change; registers the view in `CombatTargetRegistry` for
  the client copy, as the dummy's client link). **Editor:** `HostileEntitiesAssetsBuilder.cs` menu
  *Krytical Nova ▸ Hostiles ▸ Build DOTS Assets*: `Hostiles/Entities/Prefabs/HostileGhost.prefab`, adds `HostileGhostPrefabAuthoring`
  to the R8 ghost subscene (path from `PlayerGhostAssetsBuilder` constants), and **server bodies**
  `Hostiles/Prefabs/ServerBody/HostileBody_<Species>.prefab` (root, `CombatEntity`, `HostileNet` with its settings refs,
  `EcsHostileBrain`, `CapsuleCollider` from `bodySize x scale`, `NetworkObject` present but never spawned; no Animator, renderer,
  Emerald, NavMeshAgent) + `HostileBodyCatalog.asset`.
- **Change:** `CombatTargets.cs` (+ `CombatTargetKinds.Hostile = 2`); `Hostiles/Runtime/HostileSpawner.cs` (`AuthorityOverride`,
  `UseServerBodies`), `HostileZone.cs` (body prefab when `UseServerBodies`), `HostileNet.cs` (`BindView`, `PlayFromGhost`),
  `HostileBrainLink.cs` (calls `HostileStrikeRules.TryRoll`), new `Hostiles/Runtime/HostileStrikeRules.cs`;
  `PlayerWorldsSetup.cs` (install calls; server sets `AuthorityOverride` and `UseServerBodies`, clients set `AuthorityOverride = false`).
  **Prowler stand-in:** a GM-only spawn `gm.hostile.spawn prowler` of the Bonewalker body at level 4 AttacksOnSight (no new species asset).
- **Ghost send:** relevancy radius 150 m via `GhostRelevancy` SetIsRelevant per connection from `SenseWorld` (job later);
  importance via `GhostDistanceImportance` tiles (check the clone's existing setting first: `graphify query "GhostDistanceData"`).
- **Tests:** EditMode `HostileGhostTests.FieldsWrittenOnlyOnChange` (no write when unchanged: compare change versions over 30
  idle ticks), `HostileBodyLinkTests.BodyAppears_GhostCreated_Registered`, `...BodyPooled_GhostDestroyed_Unregistered`,
  `EcsHostileBrainTests.IHostileBrain_RoundTrip` (Aggro -> Engage; Calm -> Return; Frozen -> stands, takes hits).
  PlayMode (real prefabs, KnTestWorld server + client): **B70** a body spawned on the server yields one ghost on the client with
  species/level/HP; **B71** attack start: client's `AttackStartTick` equals the server's, view clip starts with offset within 1
  tick; heavy shows "!"; **B72** death: Dead flag -> death clip; body pooled after 3 s + 1.4 s; ghost gone; client view returned.
- **Owner hand test (first visible one):** "Play DOTS here", `gm.hostile.spawn prowler` 8 m away: it notices you at once, runs in,
  swings; side-step during a swing early (it turns) and late (it misses); keep hitting it: it still swings. GIF to the owner.
- **Run notes:** the builder run (`run_build.ps1 -executeMethod ...HostileEntitiesAssetsBuilder.Build`) rebakes the ghost
  subscene: one run, then `run_step`. **Done when:** B70-B72 green, the hand test approved.

### M5.6 Blows, death, rewards (M, **HIGH**: XP, loot, death penalty)
- **Create:** `HostileStrikeBridgeServerSystem.cs` (drain `PendingStrike`: body of the Hostile, target's server body from
  `GhostBodyRegistry`, move body to `From`, `HostileStrikeRules.TryRoll`; on success `HostileStrikes.Queue(world, new HostileStrike
  { Target, Result, From, Attacker = HostileAttackers.Register(body.CombatStats) key, Heavy })` - the real Hostile now, so
  reflection hits it (M4.9 note); then `HostileNet.ReportStrike`-equivalent for the threat note (H18) stays on the body).
  Body death: `CombatEntity.Killed` -> `HostileNet.OnKilled` (unchanged: AnyKilled -> `HostileKillRewards`, loot, tag) ->
  `HostileBodySyncServerSystem` sets `Mode Dead`, `DeadTick`, `Flags.Dead`, `OnGround` from the brain's last ground contact
  (airborne = knocked up or falling: H23) and disables `BrainActive`.
  **Client:** `HostileViewClientSystem` plays the de-rez instead of the death clip when `OnGround` is false (the player's de-rez
  effect, `graphify explain "PlayerDeathMotor"` for the shared dissolve).
- **Tests:** EditMode `StrikeRulesTests.TryRoll_EqualsNgoStrike` (10,000 seeded tuples: same `DamageResult` as the old
  `HostileBrainLink.Strike` path; reach-above and wall cases), `StrikeBridgeTests.OneStrikePerResolve`.
  PlayMode **B73** wall rule (player on a wall 2 m up beside a ledge: melee misses; clear line within range hits); **B74** blow on a
  player: HP drops by `Result.DamageDealt`, hit player gets `HostileStrikeRpc`, threat note keeps the player in combat 0.5 s
  cadence, death by Hostile costs exactly 5 % XP once (B54's numbers); **B75** first-hit tag: player A hits first, B does more
  damage, A gets the kill; tag released 10 s after A stops; **B76** XP for a kill equals `HostileKillRewards.ExperienceFor` for
  the level gap, Squad of 2 splits as NGO; **B77** drops spawn at the corpse, held 20 s for the holder, Creds item 9040;
  **B78** killed while knocked into the air -> de-rez, on ground -> death clip.
- **Before commit:** diff summary to Nova (HIGH). **Owner hand test:** kill five Prowlers with a second client in a Squad: XP and
  drops match NGO on the same level; die to one: 5 % XP once. **Done when:** B73-B78 green + Nova approves.

### M5.7 Feel checkpoint and soak (M, MED)
- **Create:** `[KnBrain]` timing lines (off by default; env `KN_BRAIN_DIAG` like `KN_PLAYHERE_DIAG`): notice tick, swing start,
  resolve, per Hostile; `PerformanceLog` counters: brains awake by tier, thinks/tick, paths/tick, ms per system (`ProfilerMarker`).
  `Hostiles/Entities/Editor/BrainSoakScene` builder (test scene folder under `Hostiles/Entities/Tests/`): 2,000 synthetic brains in
  a flat map with 200 fake players (bots), test server only.
- **Measure and record in the migration plan:** reaction table of brain plan s3.3 (server-side numbers from `[KnBrain]`, client
  warning time from a GIF at 100 ms simulated RTT); brain ms per tick p50/p99 at 102 and 2,000 on the Linux test server (WSL).
- **Targets (fail = stop and report):** notice <= 0.2 s p99; swing start = the reach tick; visible warning >= 0.35 s on light
  attacks after M6.1's windups (M5.7 uses Prowler windups 0.40 s); brains < 0.3 ms p99 at 102, < 2 ms p99 at 2,000 (worker
  threads), main-thread bridges < 0.3 ms at 102.
- **Owner hand test (the feel checkpoint):** side by side with the NGO game on the same species and level: owner says "faster".
  **Done when:** owner approves + numbers recorded + 2-client test on the DOTS test server with screenshots. **M5 closes.**

---------------------------------------------------------------------------------------------------

## 5. M6 steps

### M6.1 All 10 species (M5.0 count) (M, MED)
- **Change:** `HostileDefinition` (new fields 1.4), `HostileAssetBuilder` table: light windups 0.25 -> 0.40 s (Q3), poise per
  species (normal 30, Bruisers 70), assist per behaviour, score profile (Bonewalker/Blightborn Brute, Wraith/Seraph Skirmisher,
  Gargoyle/Carapace/War Frame Brute, Hellion/Colossus Finisher) - **the profiles are a proposal for the owner at the hand test**.
  Rebuild server bodies for every species (builder from M5.5).
- **Tests:** `RosterTests.EveryDefinitionHasBodyAndBlob` (ids 2-11 present, ids equal); `RosterTests.BlobRowsEqualCurve` (every
  level 1-145 x tier); `RosterTests.WarningAtLeast035` (every ability: windup - 0.15 s >= 0.35 s, the s3.8 formula with 50 ms half
  RTT + 2 ticks interpolation + 1 tick).
- **Owner hand test:** fight one of each species with `gm.hostile.spawn <id>`; note any that feel off. **Risk:** MED (balance: TTK
  changes; check with `gm.hostile.ttk` against NGO numbers in the step note).

### M6.2 Zones on the server only (M, MED)
- **Change:** nothing new in zones: `HostileZone` runs on the server world's process only (`AuthorityOverride`), spawns server
  bodies (`UseServerBodies`); `HostileZone.SetAsleep` -> body `SetAsleep` -> `EcsHostileBrain.SetAwake(false)` -> `BrainActive`
  off (H15). Zone home/leash into `HostileHome` at link time (zone centre, radius, `leashMetres`).
- **Tests:** EditMode `ZoneSleepTests.Sleep_DisablesBrains_HealsAndEndsFights`; PlayMode **B80** Zone01: a client sees no offline
  NGO Hostiles, only ghosts; walking 40 m inside the wake margin wakes the zone (bodies + ghosts appear); **B81** respawn after the
  NGO time at level 4 (8.48 s +- 1 tick), never within 3 m of a player.
- **Owner hand test:** walk Zone01's eight zones; each wakes as you arrive. **Run notes:** Zone01 subscene load in PlayMode needs
  the 12 GB rule.

### M6.3 GM commands and GM window tab (S, LOW)
- **Change:** `HostileCommands` keep their names (they act on `HostileNet.Active` bodies, which still exist on the server);
  add `gm.hostile.brain <on|off>` diag and `gm.hostile.slots` (shows the stage manager per player) to commands **and** the GM
  window's Hostiles tab (owner rule: GM tools in the window too).
- **Tests:** `GmHostileTests` (each command on a test world: spawn level, kill, freeze, aggro, calm, zone fill/clear/sleep);
  **B82** a GM client runs `gm.hostile.freeze` and the ghost stands still while taking hits.
- **Owner hand test:** run each command from the window once.

### M6.4 Consumers (M, MED)
- **Change:** none expected in UI: nameplates, minimap, Autopilot/Magnet, target ring, `IHostile` consumers read view-mode
  `HostileNet` (M5.5). Fix only what `graphify affected "HostileNet"` shows breaks (e.g. code that assumes `HasAuthority` on a
  view, or reads `Brain` on a client).
- **Tests:** **B83** on a client: nameplate shows species/level/tag mark; minimap marker moves with the ghost; Autopilot hunt finds
  the nearest Hostile and attacks it; clicking a Hostile sets the target ring (double-click rule unchanged).
- **Owner hand test:** play 10 minutes in Zone01 with Autopilot on and off.

### M6.5 Smarter packs (M, MED)
- **Create:** `HostileAttackSlotSystem.cs` (stage manager, Burst, single-threaded per tick: for each engaged player, sort
  claimants by distance, grant ring slots by grid weight within `gridCapacity`, grant attack cost within `attackCapacity`; release
  the cost right after Recover; a waiting Hostile is moved to the wait ring 3.5-5 m in front of a free slot; nobody waits > 2 s),
  assist in `HostileThreatSystem` (Damaged/aggro -> same zone, same species, within 6 m, `assist` flag -> small threat), flee for
  species with `flees` (none by default).
- **Tests:** `SlotTests.NeverOverCapacity` (12 Hostiles on one player, 3,000 ticks: grid <= 12, attack <= 10 at every tick),
  `SlotTests.Rotation_NoWaitOver2s`, `SlotTests.BruiserTakesTwoSlots`, `AssistTests.SameSpeciesWithin6m_Joins`,
  `AssistTests.FightsBackSpecies_DoNotAssist`. PlayMode **B84** area skill on 12 Hostiles in a ring hits all 12 (no cap, H24) while
  at most 3 swing.
- **Owner hand test:** pull a whole Gargoyle zone (12): it should feel dangerous and readable; tune capacities in the settings
  asset only. **Risk:** MED (difficulty).

### M6.6 Soak and switch-off (M, MED)
- 102 (Zone01 full), 300 (`maxActiveHostiles`), 2,000 synthetic (M5.7 scene) on the Linux test server, bot soak 60 min with two
  real clients; p99 whole-server tick < 15 ms (migration plan threshold), brains < 0.3 ms at 102; memory flat over 60 min.
- Emerald parts: never enabled on DOTS (server bodies have none; views keep them disabled); removing Emerald files is a merge-back
  item (migration plan s9), not done here. **Encyclopedia:** "Brain" system row, glossary rows (Brain, Think tier, Attack slots,
  Threat list, Server body, Notice time, Poise), Change log line, `build_encyclopedia.py`, publish.
- **Done when:** the 2-client test passes on the DOTS test server with screenshots/GIFs. **M6 closes.**

---------------------------------------------------------------------------------------------------

## 6. NPC use (only when the owner asks; sizes as above)

### N1 Townsfolk mind (L, MED)
- **Create:** `NPC/Entities/Runtime/` `KryticalNova.NPC.Entities` (refs Brains, NPC, World): `ScheduleRow` blob
  {startMinute, endMinute, placeId, activity, conditions}, `TownsfolkBrain` {row, activity, markerEntity, sinceTick},
  `SmartMarker` {activity, slots, used, faction, flags} authored by `SmartMarkerAuthoring` (bench, stall, forge, guard post) in map
  subscenes; `TownsfolkDecideSystem` (first passing row wins, then the Brains scorer picks a reachable free marker of that
  activity within 30 m), reuses `BrainMoveSystem`/`PathService`. Talk-to NPCs are ghosts linked to today's `NpcInteractable`
  views (vendors path ported in DOTS 14: `IEntityViewInteractable`).
- **Tests:** `ScheduleTests.FirstPassingRowWins`, `ScheduleTests.DayRollover`, `MarkerTests.NoDoubleBooking`; **B90** a vendor walks
  to its stall at 08:00 game time and is talkable there; **B91** two clients see the same NPC at the same marker.
- **Owner hand test:** watch a Home City block for one game day at speed x60 (`gm.time`).

### N2 Ambient crowds (L, LOW)
- Client-only entities (`KryticalNova.NPC.Entities.Presentation`), seeded per city block + game day + weather; LOD: full within
  12 m (cap 40), puppet 12-40 m, instanced far; never relevant to gameplay or saves. **Tests:** `CrowdSeedTests.SameSeed_SameCrowd`
  on two client worlds; perf: 200 agents < 1 ms on a mid PC client. **B92** two clients see the same crowd layout (same seed).

### N3 Guards (M, MED)
- Guards run the Hostiles mind (same modes/scores/slots) with the faction filter (`HostilityRules.IsHostile`, M4.1b) instead of
  "all players". **Tests:** `GuardTests.OnlyHostileFactions`; **B93** a guard engages a hostile-faction player, ignores own faction.

### N4 Off-screen life (S, LOW)
- Tier 3 townsfolk advance their schedule on paper (snap to the row's marker when a player comes within 120 m). **Tests:**
  `OffscreenTests.SnapToRowPlace`.

---------------------------------------------------------------------------------------------------

## 7. Summary table

| Step | Size | Risk | New B-tests | Owner hand test |
|---|---|---|---|---|
| M5.0 rules table | S | LOW | - | sign-off |
| M5.1 Brains core | M | LOW | - | - |
| M5.2 NavWorld spike | S | LOW | - | - |
| M5.3 Monster mind | L | MED | - | - |
| M5.4 PathService | M | MED | - | - |
| M5.5 Ghost, body, view | M | MED | B70-B72 | first fight with the Prowler |
| M5.6 Blows, death, rewards | M | **HIGH** | B73-B78 | XP/drops/death penalty vs NGO |
| M5.7 Feel + soak | M | MED | - | side-by-side feel checkpoint |
| M6.1 11 species | M | MED | - | one of each |
| M6.2 Zones | M | MED | B80-B81 | walk all zones |
| M6.3 GM | S | LOW | B82 | each command in the window |
| M6.4 Consumers | M | MED | B83 | 10 min with Autopilot |
| M6.5 Packs | M | MED | B84 | pull a full zone |
| M6.6 Soak + Encyclopedia | M | MED | - | 2-client test |
| N1-N4 | L/L/M/S | MED/LOW | B90-B93 | a city day |

About 26 session-days for M5 + M6 (S = 1, M = 2.5, L = 4.5), not counting run waits.
