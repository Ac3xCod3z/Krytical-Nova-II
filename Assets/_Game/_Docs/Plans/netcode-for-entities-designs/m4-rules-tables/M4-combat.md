# M4 research: the NGO rules table for Combat (health, damage, melee, skills, buffs, hostility, duel, death, dummy)

Read-only research, 4 Nov 2026. NGO = the game code as it stands in `C:\Users\dlaco\Krytical Nova II DOTS` (branch `dots/netcode-entities`;
the NGO classes are all still there, the DOTS work sits beside them). Paths below are relative to `Assets/_Game/`.
Graph used first (`graphify explain`/`affected` for `CombatEntity`, `PlayerSkillAttacks`, `CombatHostility`, `IHitChanceModifier`), then only the
pointed lines were read. Nothing was run, built or changed. Where a number is derived by hand it says so.

Sources: `_Docs/Plans/netcode-for-entities-migration.md` (M4 line 152-156; acceptance list section 54, lines 976-1019; decisions 260-268,
184-185, 228), `netcode-for-entities-designs/ghosts-design.md` (3.1 table 184-222, section 4 432-514, 5.2 Combat Dummy 543-544),
`coverage-table.md` (C1-C5 125-129, G1-G3 130-132, FAC 173, V6 225), `monster-ai-design.md` (18, 37-39, 133-135, 177-183),
`m7m8-rules-tables/M8-g4.md` (section 6 Duel 143-159, section 12 Cadence 247-263: not repeated here, only linked).

Scope note: the plan's M4 also holds "Wallet vertical slice + one Trade with a disconnect mid-trade, kill test, golden ledger" (migration.md:152-154,
1017-1019). That is not combat and is not in this table. DOTS already has `TradeOpRpc`, `ShopBuyRpc`, `StorageOpRpc` (`Persistence/Entities/Runtime/PlayerRequestSystems.cs:56-221`),
so it needs its own short table.

## 0. Shared facts for the whole milestone

| Fact | Where | Numbers | Notes |
| --- | --- | --- | --- |
| Server simulation and network rate | `Networking/Entities/Runtime/KnNetDefaults.cs:9`; `ghosts-design.md:59-60` | 30 Hz = 33.3 ms per tick | A 16-tick history ring is 533 ms. NGO code runs on frame time (`Time.time`, `Time.deltaTime`) everywhere in this table. Every timer below must become a tick count. |
| Request limits, `CallKind.Combat` | `Networking/Runtime/Safety/CallLimits.cs:51`, `CallKind.Combat` `:13`; spend `RpcBudget.cs:58-123` | burst 25, 15 per second; over the flood count (50 refused within 10 s, `CallLimits.cs:55-56`) the game is disconnected (`RpcBudget.cs:73-79`) | `Move` is 10/10 here (owner, 30 Oct), not the 30/20 written in M8-g4 section 0. `Social` 8/3, `Money` 10/5 for the duel and the rank upgrade. DOTS already spends by `PlayerKey` (`PlayerRequestSystems.cs:131-142`; `Cast()` uses `CallKind.Combat`, `:1071`). |
| Who applies a hit | server (or offline) only: `PlayerMeleeCombat.cs:749-772`, `CombatEntity.cs:249-343`, `PlayerSkillAttacks.cs:200-271` | | A client never applies damage. Offline (`!IsSpawned`) the same code runs locally. |
| The one damage formula | `Progression/Runtime/CombatCalculator.cs:45-134` | see section 3 | Called by melee `PlayerMeleeCombat.cs:800`, skills (through `ResolveHit`), Hostiles `HostileBrainLink.cs:257`, and tests. |
| Which numbers are real | `Player/Prefabs/Player.prefab:713-736` (melee), `:842-843` (skills), `:3521,3535` (death), `:3549-3552` (duel), `:3907-3908` (combat state), `:3922-3925` (queue) | | The prefab overrides several code defaults (section 5). `ghosts-design.md:460` quotes the code defaults, not the prefab. |
| What a DOTS server body keeps | `Player/Entities/Bodies/Runtime/PlayerRoleTable.cs:64-79` | | Server body keeps every combat rule class (`PlayerMeleeCombat`, `PlayerSkillAttacks`, `PlayerSkillQueue`, `PlayerCombatSkills`, `CombatEntity`, `PlayerCombatState`, `PlayerDuel`, `KNEmeraldPlayerBridge`, `TargetPositionModifier`, `WeaponHitbox`); strips animation (`:38-43`) and disables `PlayerDeathMotor` (`:47-48`, `ActionFor` `:113-120`). |
| What a DOTS remote body keeps | `PlayerRoleTable.cs:84` | `CombatEntity`, `FactionExtension` only (of the combat classes) | So another player's hostility, namecard and selection colour work, but no `PlayerDuel`, no `PlayerCombatState`, no buffs, no resources exist on a remote body. |
| What a DOTS own view body keeps | `PlayerRoleTable.cs:113-120` | everything in `Rules`, offline-style | Its `PlayerResources` is a mirror (`MirrorChi` `PlayerResources.cs:246`, `MirrorHealth` `:315`) fed by the owner-only `OwnerFlux`. |
| DOTS combat today (the gap) | see each section | | **Nothing in combat is networked yet.** Walk/attack clicks on a unit: first click selects (works), second click prints "Not networked yet on this server: only walking is." (`Player/Runtime/PointAndClickPlayer.cs:196,750`). Non-attack skills and item stims are cast on the server body (`PlayerRequestSystems.cs:1070-1088`); attack skills are refused there with "Attacks are not networked yet on this server." (`:1078`). |
| Existing contract tests | `Combat/Tests/Editor/` (CadenceRulesTests 8, CombatStateRulesTests 9, HostilityCheckTests 2, RedlineRulesTests 8), `Factions` `FactionRulesTests`, `Progression/Tests/Editor/SquadBuffRulesTests` | | **There is no `CombatCalculator` test at all** (only one call in `Hostiles/Tests/Editor/HostileStatCurveTests.cs:62`). The 10 000-tuple parity test of the plan (migration.md:991) needs a golden table written first from the managed code. |

## 1. CombatEntity (`Progression/Runtime/CombatEntity.cs`, 402 lines): health, Damaged / Died / Killed

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| NetworkBehaviour + `IDamageable`; works for players (wraps `PlayerResources`), dummies, Hostiles (standalone pool) | `:10`, `:180-193` | | A "standalone" entity (no `PlayerResources`) holds its own float health. |
| 3 NV, all `Server` write, `Everyone` read: `netStandaloneHealth`, `netMaxHealthOverride`, `netInfiniteHealth` | `:29-34` | floats / bool | Only used when there is no `PlayerResources` (dummy, Hostile); a player's HP is in `PlayerResources` (section 2). Set once at spawn (`:105-124`), then on every change. |
| Max health: player = resources; else GM override > combat-stats MaxHP > `overrideMaxHealth` | `:70-79`, `:18-19` | prefab dummy: `overrideMaxHealth` 600 but `useCombatStatsForMaxHealth` 1, so 5000 (section 12) | `Zone01_StarterTown.unity` Combat Dummy object `m_Name` at line 323351. |
| Damaged event (every hit incl. evaded), Died (bare notice on every peer), Killed (blow, authority only), static AnyKilled | `:36-47`, `:211-218` | | Subscribers: `PlayerCombatState` `PlayerCombatState.cs:113`, `PlayerChat.cs:94`, `PlayerDuel.cs:76`, `PlayerDeathMotor.cs:170`, `PlayerPet.cs:182,508`, `HostileKillRewards.cs:48,161`, `PlayerMounts.cs:113` (dismount on death). A client sees `Died` only when its NV copy goes from above 0 to 0 or below (`:150-159`). |
| TakeDamage order: reflected (attacker takes the reflected amount, bypasses shield, target takes nothing) -> evaded (only `Damaged`) -> shield-shatter proc -> physical hits Heavenly Shield first, elemental bypasses -> any damage to life interrupts a channel -> apply | `:249-343` (reflect `:253-272`, evade `:274-278`, shatter `:283-286`, absorb `:290-293`, elemental `:295-296`, interrupt `:299-302`) | | Always runs where combat is decided. |
| Infinite health (standalone only): a lethal hit refills the pool, no death, no `Killed` | `:323`, set `:391-400`, `:389` | A Strike Dummy (no player, no `IHostile`) is **always** infinite (owner, 24 Oct) | `:190-192`, `:395`. |
| `Kill(blow)`: authority only, ignores god mode | `:224-247` | | GM `gm.kill` (`Tools/Debug/GmCommands.cs:363`). |
| `ResetHealth`, `SetHealth` (clamped 1..max), `SetMaxHealthOverride`, `SetInfiniteHealth` | `:345-400` | | GM dummy tools and Hostile setup (`HostileNet.cs:300-301`, `Tools/Debug/BenchCommands.cs:341-432`, `HostileCommands.cs:191`). |
| `KillingBlow` {result, attacker stats, flat direction, crit} | `Progression/Runtime/KillingBlow.cs:7-33` | | The direction decides the death chain (section 11). A kill with no attacker is "from the front". |
| Save | none | | Current HP is not saved by `CombatEntity`. |

- DOTS today: the server body keeps `CombatEntity` and the remote body keeps it for targeting (`PlayerRoleTable.cs:84`), but a remote body's HP never changes
  (no NV, no ghost field). `OwnerFlux` carries only the own player's HP in thousandths (`Player/Entities/Runtime/PlayerComponents.cs:239-248`).
- Dependencies: `PlayerResources` (section 2), `Vitals` ghost component (design), `Dead` enableable bit, `GmFlags` (`Infinite`).
- Risk: **MEDIUM**. Two writers of HP must never exist (plan decision 1, migration.md:260): `Vitals` is truth, `CombatEntity`/`PlayerResources` become a facade, and `CaptureSave` reads through the facade.
- Size: M. 0 RPC, 3 NV (NGO) -> 1 `Vitals` component (HP, MaxHP, Chi, MaxChi, level, flags) + 2 enableable bits (`Dead`, `InCombat`) + a byte of GM flags.
- DOTS edge: `ghosts-design.md:186,514`: `Vitals` as `int` hundredths; `Died` = the `Dead` bit going on, `Killed`/`AnyKilled` fire on the server from `DamageApplySystem` (managed bridge), unchanged subscribers.

## 2. PlayerResources (`Progression/Runtime/PlayerResources.cs`, 349 lines): HP and Flux (code: Chi)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| 4 NV, `Server` write, `Everyone` read: `netHealth`, `netChi`, `netMaxHealth`, `netMaxChi` | `:28-35` | floats | Everyone can read another player's HP and Flux today. |
| Settings `CharacterResources.asset`: HP regen 0 per s (none), Flux regen 0 per s, regen delays 2 s, `fillGainedCapacity` on | `Progression/Data/CharacterResourceData.cs:15-24`; asset `Progression/Data/Assets/CharacterData/CharacterResources.asset` | | HP does **not** passively regenerate and Flux never does (code comment `:176`: Twelve Sky 2 has no Chi regen). Both are restored by items, Serenity, GM. The delays are therefore unused today. |
| Max HP / Max Flux come from stats | `:195-228` | via `CharacterCombatStats` (section 3) | When a maximum rises, current rises by the same amount (not for a dead player `:219`). |
| Spend Flux: alive, finite, affordable (or GM `InfiniteChi`); sets the regen delay | `:231-274` | `CanSpendChi` tolerance 0.0001 | `TrySpendChi` on a mirror body only answers (does not spend) `:266`. |
| `ApplyDamage`: needs alive, ignores god mode only for GM kill; sets HP regen delay; sends `DamagedRpc(fraction)` to the owner if more than 0.01 HP lost; raises `Depleted` at 0 | `:288-305` | | `Depleted` -> `CombatEntity.RaiseDied`. |
| `DamagedRpc(float)` `SendTo.Owner`, `InvokePermission.Server` | `:308-309` | | Hurt vignette on the owner (`Feel.LocalPlayerDamaged`). No `CallKind` (server->owner). |
| `Heal`, `RestoreChi`, `RestoreAll` | `:329-346` | | Not for a dead player (`Heal`). `RestoreAll` is the respawn refill. |
| God mode / infinite Flux / no cooldowns (GM) | `Progression/Runtime/PlayerGmState.cs:17-18,31-34` | NV `netToggles` byte, NV `netSpeedMultiplier` float, `Server` write | Read by `ApplyDamage` `:291`, `CanSpendChi` `:232`, `IsOnCooldown` `PlayerCombatSkills.cs:609`. |
| Save | HP/Flux not saved here | | Full refill at respawn; login starts full (`:66-67`). |

- DOTS today: the server body runs this class with the server's own tick (Update at frame rate: `:165-182`). `PlayerMoveStatsRefreshSystem` copies Flux and HP (rounded, thousandths) to `OwnerFlux` every time they change; the own view body mirrors them (`PlayerMoveFromSkills.cs:79-91,175-194,242-246`). The run cost and jump cost are paid on the server body and predicted on the owner from `OwnerFlux` (M3).
- Dependencies: `PlayerCharacterProgression` level and attributes (M5b), `PlayerGmState`, `OwnerFlux` (exists), `Vitals` (new).
- Risk: **MEDIUM**: precision. Design says hundredths (`ghosts-design.md:219`), the shipped `OwnerFlux` uses thousandths. Chi must stay integer inside prediction (run cost). A mismatch between the two units in `Vitals` and `OwnerFlux` would drift. HP never predicted, so no rollback risk for HP.
- Size: S-M. 1 server->owner RPC (`DamagedRpc`), 4 NV -> folded into `Vitals` (public HP/MaxHP, owner-only Flux already in `OwnerFlux`).
- DOTS edge: `Vitals` on every hittable ghost; `OwnerFlux` fields become part of it (migration.md:1000-1001); `DamagedRpc` becomes an `FxEvent`/`HitEvent` flag on the owner (`ghosts-design.md:514`).

## 3. CharacterCombatStats and the damage formula (`CharacterCombatStats.cs` 323 lines, `CombatCalculator.cs` 136 lines, `Data/CombatStatTuningData.cs`)

(Line numbers inside `GetBaseStatValue`, `CharacterCombatStats.cs:195-286`, and inside `PlayerDeathMotor.cs` are within a few lines; the files were read in ranges.)

Stat value = `(base + flat) x (1 + sum(percentAdd)/100) x product(percentMult)`, floor 0 (`CharacterCombatStats.cs:170-193`). Settings asset: `Progression/Data/Assets/CombatTuning/CombatStatTuning.asset`.

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Attack Power = Strength x 1 + Spirit x 1 + class weapon AP (default 15) | `CharacterCombatStats.cs:225-230` | `strengthToAttackPower` 1, `spiritToAttackPower` 1 | Class data: `Progression/Data/Assets/CharacterClasses/GuanyinBladeClass.asset` (base attributes 10 each, HP pool 100, Flux pool 50, AP 15, DP 5). |
| Defense Power = Agility x 1 + class weapon defense (5) | `:233-238` | `agilityToDefensePower` 1 | |
| Hit Rate = Strength x 2; Evasion = Agility x 2 + Vitality x 1 | `:241-251` | `strengthToHitRate` 2, `agilityToEvasion` 2, `vitalityToEvasion` 1 | |
| Max HP = pool + 15 x (level-1) + Vitality x 20; Max Flux = pool + 10 x (level-1) + Spirit x 20 | `:254-271` | `healthPerLevel` 15, `chiPerLevel` 10, `vitalityToHealth` 20, `spiritToChi` 20 | Level from `PlayerCharacterProgression` (1 when none). |
| Baselines: crit rate 15, move speed 100 %, elemental atk/def 0, stun resistance 0, luck 50 (not used in combat) | `:273-283` | asset values equal these | |
| Hit chance = Hit / max(1, Hit + Evasion) x 100 + level-gap modifier, clamped to 10..95 | `CombatCalculator.cs:63-73` | `minimumHitChance` 10, `maximumHitChance` 95 | Level-gap modifier: White band 0, Purple (gap 5-9) -5, Red (gap 10+) -15 percent points (`Hostiles/Data/Assets/LevelGapSettings.asset`, lines 24-78). Only a Hostile above the player is harder to hit (`HostileNet.cs:404-409`, `IHitChanceModifier`). |
| Reflection roll (defender buff) -> target takes 0, attacker takes `AttackPower x skillMultiplier` as bypass | `:83-89`; applied `CombatEntity.cs:253-272` | roll vs buff chance (1-10 %) | |
| Shield-shatter roll (attacker buff) | `:92-95` | chance 1-20 % per rank, dispel 50 % | Dispel is rolled again in `PlayerMeleeCombat.cs:819-825` with `UnityEngine.Random`. |
| Physical damage = max(1, AP x skillMultiplier - DP) | `:98-102` | | Minimum 1 before the crit. |
| Critical: roll <= crit rate; bonus = 1.5 + critDamage%/100; Critical Defense trims the bonus; stun resistance cuts 50 % of its value, never below 0.7x | `:106-124` | `baseCriticalMultiplier` 1.5 | `canCrit` false -> never; `forceCrit` -> always if it hits (Redline). |
| Elemental = max(0, eleAtk - eleDef), added after, bypasses DP and shield | `:127-132` | | |
| Four dice are rolled up front: hit, reflect, crit, shatter, all 0-100 | `:22-27` | `UnityEngine.Random` | `CalculateAttackInternal` takes the four rolls as arguments (the testable form). The live wrapper `CalculateAttack` uses `UnityEngine.Random`. |
| Cadence/Redline scale after the formula | `PlayerMeleeCombat.cs:811-815` | `damageScale` multiplies `DamageDealt` and `NetElementalDamage` | Cadence +6 % (M8-g4 section 12). |
| Combo step multiplier (basic attack) 1.0 / 1.15 / 1.35 | `PlayerMeleeCombat.cs:767` | | Hard-coded, not a setting (plan 5: numbers go into settings assets, not code). |
| Skill multiplier = rank-interpolated damage percent / 100, split evenly over `hitCount` | `Data/SkillDefinitionSO.cs:284-285`, `PlayerSkillAttacks.cs:202-203` | `hitCount` 1/2/3 (80/36/36 of 152 assets) | |
| Save | none | | |

- DOTS today: the server body runs `CharacterCombatStats`. Nothing reaches the client, so the owner cannot show a hit chance.
- Dependencies: attributes and level (M5b, `OwnerProgress` already carries them to the owner, `PlayerComponents.cs:253-261`), gear modifiers, buffs (section 7), `IHitChanceModifier`.
- Risk: **MEDIUM**. The formula is plain, but (a) no test exists, (b) it is written on a `MonoBehaviour` (`CharacterCombatStats`) and a float `Random`; the plan wants a Burst version on blittable structs (migration.md:268,991) while `ghosts-design.md:219` and `coverage-table.md:131` still say "untouched, Burst only for monsters". The plan (later) wins; see owner choice 1.
- Size: M. 0 RPC, 0 NV.
- DOTS edge: `CombatMath` static on a `StatBlock` struct (AP, DP, Hit, Evasion, crit rate/damage/defense, elemental atk/def, stun resistance) with the four rolls as input; server draws rolls from a seeded `Unity.Mathematics.Random`; a golden table from the managed code first, then 10 000 random tuples at tolerance 1e-4 (monster-ai-design.md:177). Rolls are server-only: no client prediction of damage.

## 4. Targeting and the attack order (`Player/Runtime/PointAndClickPlayer.cs`, `Progression/Runtime/IHitChanceModifier.cs`, Emerald `TargetPositionModifier`)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Click on a unit: first click only selects (colour = hostile or friendly); second click on the selected unit: hostile -> attack, friendly -> walk to it; Shift+click on another player opens the player menu | `PointAndClickPlayer.cs:722-800` | | Selection is purely local (`selectedTarget` `:986`, `Select` `:1195`). Hostile colour comes from `HostilityCheck.IsHostile` -> `CombatHostility` (`:730`, `:1189`). |
| Owner starts the basic attack: local `StartAttackTarget` (draws weapon, shows strike 1 on the click when already in reach) and, on a client, `RequestAttackEntityServerRpc(targetNetworkObjectId)` | `:766-792`, `:1881-1895`; `PlayerMeleeCombat.cs:176-207` | | `CommandAttack` is the same for Autopilot (`:1146-1165`). |
| `RequestAttackEntityServerRpc` `SendTo.Server`, owner only, `CallKind.Combat`: target must exist, be alive and hostile, then the server's `StartAttackTarget` | `:1881-1895` | | Silently ignores a bad target (no refusal text, no `RpcBudget.Refuse`). |
| `RequestCancelAttackServerRpc` `SendTo.Server`, owner only, `Combat`: stop the combo, stop moving | `:1872-1879` | | Sent when the owner cancels an attack in progress (`:1865-1870`). |
| Selection told to the server only for the Strike Dummy rule: `PlayerCombatState.SelectionRpc(ulong)` | `PlayerCombatState.cs:233-245` | when it changes | No `RpcBudget` on it. |
| Skills use the same selection: `ResolveTarget` = selected target, else the melee target | `PlayerSkillAttacks.cs:105-112` | area skills have no target | |
| Emerald aim point: `TargetPositionModifier.TransformSource` set to the player root | `Tools/Editor/Setup/PlayerSystemsSetup.cs:201-209`; role table `PlayerRoleTable.cs:76` | | Vendor script on the Player prefab so Emerald hostiles aim at the body; goes away with Emerald (section 13). |
| `IHitChanceModifier.HitChanceModifierFor(attacker)` | `Progression/Runtime/IHitChanceModifier.cs:7-10`; `HostileNet.cs:404` | | The only implementer is `HostileNet`. |
| Facing: the server turns the body to the target before each strike and cast (`FaceTarget`, `FaceTowards`) | `PlayerMeleeCombat.cs:675-690`; `PlayerSkillAttacks.cs:147` | area skills never turn the body (owner, 23 Oct) | "Back hits never turn the body" is only the **death** rule (memory, section 11). NGO has **no** arc/facing test on the hit itself. |

- DOTS today: the first click selects a hostile or friendly and shows the highlight; the second click shows `NotYetNetworkedText` (`PointAndClickPlayer.cs:750`). No attack RPC exists. `PlayerInput` has a walk click only (`PlayerComponents.cs:51-76`).
- Dependencies: ghost id for the target (`GhostBodyRegistry` lookups exist), `PlayerInput`/command data for a target, M3c skill walk-in and `ServerOrder` for the approach (section 5), `RelevancyFillSystem` (target must be relevant, `ghosts-design.md:129`).
- Risk: **MEDIUM**. Selection is client-side presentation; the server must pick the target from the command and validate it. Danger: target ghost out of relevancy.
- Size: M. 3 owner->server RPCs (Attack, Cancel, Selection) -> fields of the player input command (`TargetGhostId` + `AttackSeq` + `Cancel` event).
- DOTS edge: `PlayerInput` gets `AttackTarget` (ghost id) and an `AttackSeq`/`StopAttack` event; Selection is a field for the dummy rule (or derived on the server from `AttackTarget`); the hit's hostility check runs on both sides from the same pure function (section 9).

## 5. PlayerMeleeCombat + WeaponHitbox (`Combat/Runtime/PlayerMeleeCombat.cs` 1006 lines, `WeaponHitbox.cs`)

Settings sit on the component in `Player.prefab:713-736` (the numbers below are the **prefab's**; the code defaults in brackets differ).

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Three-hit combo: clips = Grruzam Katana `Attack_3Combo_1/2/3_Inplace` | `:29-31`; prefab `:713-715` | clip lengths are inside the FBX (not read here) | One clip set on the prefab; nothing in `PlayerMeleeCombat` swaps clips per weapon class. The design's per-class `StrikeTimingBlob` (`ghosts-design.md:460`) has no NGO source for classes other than this. |
| Strike speeds (clip speed) | `:53-55`; prefab `:728-730` | **1.411 / 1.30 / 1.126** (code defaults 1.30 / 1.30 / 1.00) | Strike duration = clip length / speed (`:436-441`). |
| Swing window (fraction of the clip) | `:38-44`; prefab `:719-724` | step 1: 0.22-0.65; step 2: 0.10-0.30; step 3: **0.33-0.70** (code default 0.20-0.70) | The hit sweep is enabled in the window (`:380-396`). |
| Post-combo delay | `:35`; prefab `:718` | **0.25 s** (code default 0.45) | Wait after step 3 before the loop restarts (`:321-324`). |
| Cross-fade between strikes | `:32` | 0.12 s | Presentation. |
| Striking range / approach inset | `:59-61`; prefab `:731-732` | 1.6 m; 0.15 m | `IsWithinStrikingRange` uses reach offset with height slack 1.2 m (`:608-630`). |
| Server validation range (centre to centre when a hit is applied) | `:62`; checked `:759-764` and for client reports `:739-744` | 2.7 m | |
| Combo reach slack (target may drift this far past the range before the combo breaks off to chase) | `:97`, `HasLeftReach` `:615-619` | 0.5 m | |
| Min cadence tolerance (a step may not start before 70 % of the last step's duration) | `:67`; check `:462-468`, set `:473` | 0.7 | Guards fast re-swings; reject text `:466`. |
| Combo order (1 then 2 then 3, or restart at 1) | `:455-460` | | |
| Hit latency grace (a client-reported hit may arrive this long after the strike's end) | `:69`; check `:490-497` | 0.35 s | Disappears with the report RPC (below). |
| One damage application per target per swing | `:118`, `:500` | | `serverSwingHitTargets`. |
| **Approach**: the server walks the player into range with the NavMeshAgent, re-planning every 0.1 s; wall fights use `ServerChase` every 0.5 s (moved more than 1 m) | `:95`, `:249-286`, `:633-634` | ring = range - inset - stopping distance, min 0.1 | DOTS server body has no agent (`ActionFor` disables movement): needs `ServerOrder` walks (M3c skill walk-in). |
| **Who lands the hit** (important): the server's animated blade sweeps (`WeaponHitbox`) for the whole window and reports every hostile it touches (once per swing); at the **end** of the window `EnsureTargetStruck` applies the hit to the current target if the sweep missed it | `WeaponHitbox.cs:99-190` (box overlap, 0.15 m steps, up to 12 samples, `sweepLayers = ~0`); `PlayerMeleeCombat.cs:388-392,413-418` | `sweepStepDistance` 0.15, `maxSweepSamples` 12 | So today a swing can hit **other hostiles under the blade**, and the target is hit for certain by the window's end. Evasion is the only way to miss. A DOTS server body has no animated bones: only the end-of-window fallback survives. See owner choices 2 and 3. |
| Owner's client also sweeps and reports: `RequestMeleeStrikeDamageServerRpc(targetId, step)` `SendTo.Server`, owner only, `CallKind.Combat`; refuses step outside 1-3, a target that is not hostile ("Not an enemy"), reach over 2.7 m; a vanished target is only counted (`LagEvent.GoneTarget`) | `:709-747` | | The server's own sweep lands the hit anyway ("these mismatches are expected", `:481-486`). The report adds nothing the server does not already decide, and it is the one RPC the design deletes (`ghosts-design.md:286,498`). |
| Lag counters | `Networking/Runtime/Safety/NetworkHealth.cs:9-15`; `PlayerMeleeCombat.cs:486,495,722` | `LateHit`, `GoneTarget` | |
| `ExecuteAuthoritativeStrike`: hostility check, swing validation, 2.7 m check, step multiplier 1.0/1.15/1.35, `ResolveHit`, then `ConsumeNextHitBuffs` if it did not evade | `:749-772` | | |
| `ResolveHit` (shared with skills): rolls, `TakeDamage`, shatter dispel, damage number to the attacker, `HitResolved` event, impact VFX RPC, combat-log line | `:779-853` | | `HitResolved` feeds `PlayerCombatState` (`:114`) and the chat Combat tab (`PlayerChat.cs:93,533`). |
| Skill hold: a skill cast freezes the combo for the cast's hold; the combo resumes on its next strike | `:926-935`; `PlayerSkillAttacks.cs:172` | | |
| RPCs: `PlayComboStrikeClientRpc(int step)` Everyone (non-server plays the strike; owner skips step 1 if it already showed it on the click, 0.6 s) ; `SpawnImpactVfxClientRpc(pos, towardAttacker, step, kind, targetId)` Everyone; `DamageFeedbackRpc(pos, damage, reflected, flags)` Owner; `CombatMessageRpc(string)` Owner; plus the report RPC above | `:509-522,960-968,897-920`, `:709` | `kind` byte bits crit 1, reflected 4, shattered 8, elemental 16 (`:860-878`); `flags` evaded 2, skill 32 | The damage number belongs to the **attacker only** (`AttackerIsHere` `:882`). Other players see the comic hit (`Feel.Hit`) and not the number. |
| Slash VFX timing | `:38-50` | 0.22 / 0.10 / 0.38 of the clip | Presentation. |

- DOTS today: nothing runs. The server body keeps `PlayerMeleeCombat` and `WeaponHitbox` (`PlayerRoleTable.cs:75-76`) but no animation, so the sweep never fires and no one starts it. `AttackerIsHere` is documented as the NGO routing that the combat port replaces (`PlayerMeleeCombat.cs:880-882`).
- Dependencies: `ServerOrder` walks (M3c walk-in), `CombatState` and `StrikeTimingBlob` baked from the prefab and the three clips, `PositionHistory` (section 16), hostility (9), formula (3), `Vitals` (1-2), `HitEvent` ring (15).
- Risk: **HIGH**. The feel checkpoint of M4 sits here: hit moment, cadence, approach, collateral hits and "swing starts on the press". Numbers in the design doc are the code defaults, not the shipped prefab.
- Size: L. 1 owner->server RPC deleted, 4 server->client RPCs -> `CombatState` (predicted, 7 fields), `Strike` ring, `HitEvent` ring; ~1 blob.
- DOTS edge: `ghosts-design.md:438,449-460,486-496`: swing is an input (owner predicts step and animation), server `MeleeResolveServerSystem` resolves at the data tick against the history world; `SwingHit` bitset replaces `serverSwingHitTargets`.

## 6. Skills: PlayerCombatSkills (cast part), PlayerSkillAttacks, PlayerSkillQueue, hotbar charge

Files: `Progression/Runtime/PlayerCombatSkills.cs` (1086; ranks, books and upgrades are M5b and are not repeated), `Combat/Runtime/PlayerSkillAttacks.cs` (about 520), `Combat/Runtime/PlayerSkillQueue.cs` (303), `Progression/Runtime/PlayerSkillHotbar.cs`, data `Progression/Data/SkillDefinitionSO.cs` (152 skill assets).

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Cast check, in this order: learned (chip/point), right weapon class in hand, alive, a cast request pending (0.5 s), a cast still playing (unless the new one is an attack the current cast allows cutting), cooldown, Flux, then for attacks: target exists, alive, hostile, in reach, in height | `PlayerCombatSkills.cs:645-702`; `PlayerSkillAttacks.cs:114-140` | pending 0.5 s (`:19`); reach = skill range at rank + `rangeTolerance` 0.6 m (flat distance); height limit = range + `HeightSlack` 1.5 m | Results: Ready / Busy / OutOfReach / Refused (`CastCheck.cs`). Busy and OutOfReach go to the queue. All refusal words are fixed strings ("Not enough Flux...", "Out of range...", "Too high above..."). |
| Cooldown per skill, by `RankKey`, in seconds; GM `NoCooldowns` skips | `PlayerCombatSkills.cs:609-614`, set `:992`; `SkillDefinitionSO.cs:146` | values over 152 skills: 3 s (36), 2.25 s (36), 1.5 s (36), 1 s (25), 2.5 s (9), 0.2 s (9), 2 s (1) | Not saved. A client's mirror is set on the press and taken back if refused (`:868-892`). |
| Flux cost by rank, paid once by the server in `ExecuteCastInternal` (before the cooldown starts); refusal text `"Not enough Flux (n Flux required, current: m)."` | `:975-997`; `SkillDefinitionSO.cs:143-144,324` | default 15 | |
| Cast moment: the server animates nothing; it starts `ExecuteRoutine` which waits `GetHitDelay(i)` per hit and resolves | `PlayerSkillAttacks.cs:200-255`; `SkillDefinitionSO.cs:291-300` | hit i delay = `castClipDelay` + (normalized hit time x clip length - trim) / clip speed; default hit time = (i+1)/(hits+1) | `hitTimesNormalized` array on the asset. The hit moment is wall-clock `elapsed += deltaTime` (frame). |
| Cast lock (the body is busy), attack cancel point | `SkillDefinitionSO.cs:306-314` | `CastLockDuration` = `castLockSeconds` or clip duration x `castLockNormalized`; default cast 0.6 s without a clip; `castLockSeconds` values seen: 2.06, 1.81, 1.67, 1.58, 1.5, 1.39, 1.35, 1.28 s | `castBusyUntil` `PlayerSkillAttacks.cs:59,168`; `castCancelFrom` `:61,171`. |
| Single-target hit still lands if the target drifted: reach x 1.5 | `PlayerSkillAttacks.cs:240-245`; prefab `:843` | `impactRangeLeniency` 1.5 on (range + 0.6) | Already a lag-leniency rule: DOTS history test replaces/overlaps it. |
| Area skills: `Physics.OverlapSphereNonAlloc` around the **caster**, radius = skill range, max 64 colliders, all hostile `IDamageable`s once each | `:273-285`, `:25` | `areaTargetLayers = ~0` | `target == null`. 18 area assets (`targetType: 2`). |
| Hostility check per victim | `:123`, `:283` | | Section 9. |
| Cadence/Redline: `cadence.ServerTake`, `ServerCombo`, forced crit for three-hit chains | `:207-218,244-253` | see M8-g4 section 12 | |
| Skill kinds: 26 self (buffs), 108 single-target, 18 area; 151 instant, 1 channeled (Serenity); 1 charged (1 s) | asset scan (this research) | | `SkillTargetType` Self/Single/Area/Party (`SkillDefinitionSO.cs:8-14`). |
| Cast request: `RequestCastSkillServerRpc(skillId, NetworkObjectReference target)` `SendTo.Server`, owner only, `CallKind.Combat` + `TextFits`; failure -> `NotifyCastFailedClientRpc(reason)` | `PlayerCombatSkills.cs:787-797`, `:821-833` | | Client shows the cast and sets the cooldown at once ("online phase 3"), takes it back on refusal (`:868-892`). |
| Autoward volley: `CastWardsServerRpc(string "a|b|c")` Combat, max 16 ids | `:849-860` | | One shown, all take hold (`WardCastClientRpc` `:878`). Non-attacks only. |
| Cast confirmation: `TriggerSkillCastClientRpc(skillId, rank, target)` Everyone: observers play the cast, non-server mirrors the cooldown and the buff | `:916-946` | | |
| Squad buff share: `SquadBuffClientRpc` | `:1013-1040` (`ShareWithSquad`, `ServerReceiveSquadBuff`), `SquadBuffRules` | share rank 20, radius 20 m (defaults `:51-53`) | Squad is M8 group 4. |
| One-hit buff spent: `ConsumeNextHitBuffsClientRpc` ; buff removed: `BuffRemovedClientRpc(skillId)` ; late joiner: `SyncBuffClientRpc(skill, rank, remaining, squad, giver)` | `:1048-1062,1062-1086`, `OnClientConnectedToServer` `:1070` | | Buff state is mirrored by messages because every peer ticks its own copy. |
| Hold-to-charge (1 skill, 1 s): the hotbar fills locally, on completion calls `TryCastSkill`; others are told by `ChargeStartedRpc(skillId, aimedAt, aimed)` / `ChargeEndedRpc(skillId, completed)` `SendTo.NotOwner`, owner-invoked, **no `RpcBudget` call** | `PlayerSkillHotbar.cs:200-255`; `PlayerSkillAttacks.cs:394,419-452` | `chargeTime` 1 s; `holdToRepeat` re-casts quietly | Release early: "No Flux spent". Charge hand-off 0.75 s (`:26`). |
| Skill queue (one waiting attack): owner offers when Busy or OutOfReach; `QueueServerRpc(skillId, targetRef)` Combat, `ClearServerRpc()` Combat; server walks in (`ServerMoveTo`, Run Flux paid on the first step only) and casts the moment `CheckCast` is Ready; `EndedRpc(said)` Owner | `PlayerSkillQueue.cs:98-124,152-168,197-265,294-301` | `approachRange` 20 m (refused beyond), `ringInset` 0.3, `timeoutSeconds` 10 s, `giveUpFactor` 1.5 (gives up when target is 30 m away), repath 0.15 s, `LeastRing` 0.5, `NoWaySeconds` 1.5, ring moved 0.5 m; prefab `Player.prefab:3922-3925` | Releases on: player's own move order, another skill, either side dies, target escapes. Charged skills are never queued (`:100`). Skill walk-in is "moved in" from M3c (migration.md:1012-1015). |
| Presentation on every peer: cast clip, foot IK, body lift, channel pose, VFX at caster | `PlayerSkillAttacks.cs:152-182,301-392` | | Pure presentation, stays as is. `SkillHitVfxClientRpc(skillId, pos)` Everyone `:394`. |

- DOTS today: **buff skills already cast.** `TryCastSkill` on the DOTS view body sends `SkillCastRpc` for non-attacks (`PlayerCombatSkills.cs:723-728`, `PlayerRequestSystems.cs:723-731`), the server body runs `ServerTryCast` and replies `RequestReplyRpc.SkillCast`/`SkillRefused` (`:1071-1088`), the view applies the cooldown/buff with `ShowServerCast`/`ShowCastRefused` (`PlayerCombatSkills.cs:949-967`). Attack skills: the server refuses (`:1078`). **Probable hole, read not run:** on the view body the interception is for non-attacks only (`:723`); an attack skill falls through to the offline-style `ServerCast` (`:705-745`) and would play and run `ExecuteRoutine` against the local stub bodies, with local fake damage. It needs a one-line guard or the combat port.
- Dependencies: M3c skill walk-in, `Vitals` Flux, `CombatState`, `SkillCooldown` buffer, `SkillRanks` (owner-only, M5b, exists as `MoveRanks` for movement only), buffs (section 7), history world (16), formula (3).
- Risk: **HIGH**. Many moving parts (152 skills, three target types, multi-hit timings, charge, queue, Cadence/Redline coupling, Squad share). Cast timing must be a tick.
- Size: L. Owner->server: Cast, Wards, Queue, ClearQueue, ChargeStarted, ChargeEnded = 6; server->client: NotifyCastFailed, WardCast, TriggerSkillCast, SquadBuff, ConsumeNextHit, BuffRemoved, SyncBuff, SkillHitVfx, QueueEnded = 9; NV 0 here (ranks and books are M5b: 1 `NetworkList<byte>`, 1 ulong, owner only, `PlayerCombatSkills.cs:36-37,93`).
- DOTS edge: `ghosts-design.md:440-441,449-462`: cast = input event (`CastSkill`, tick-stamped) checked by one shared system on both sides (cooldown buffer, Flux in `Vitals`, `CastPhase`); server resolves hits at `CastStartTick + HitTick` from `SkillBlob`; the queue is an owner-only buffer of 4 consumed by a predicted system; refusals ride `RequestReplyRpc.SkillRefused` (exists).

## 7. CharacterBuffManager (`Progression/Runtime/CharacterBuffManager.cs`, 408 lines)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Plain `MonoBehaviour`, **no NV, no RPC**: every peer ticks its own copy, kept equal by `PlayerCombatSkills` messages (section 6) | `:44`, `:76-118` | | `Update` uses `Time.deltaTime`. Server only decides early ends. |
| `ApplyBuff(skill, rank, remaining, fromSquad, giver)`: refreshes an existing buff (remove then add); rank-scaled stat modifiers added to `CharacterCombatStats`; shield capacity = shield% x Max HP | `:122-178` | duration by rank (`SkillDefinitionSO.minDuration/maxDuration` default 180 s) | Squad buffs never stack, restart at the higher rank (`SquadBuffRules.RankAfterRecast`). |
| Channeled skill (Serenity): each `channelTickInterval` heals a % of Max HP and Flux, on the authority only | `:84-103`; `SkillDefinitionSO.cs:152-161` | tick 1.0 s; Flux 5 % default, HP 0 % default | Any damage that reaches life interrupts the channel (`:365-381`). |
| Heavenly Shield: physical damage absorbed first; broken by a shatter proc; elemental bypasses | `:200-265`; `CombatEntity.cs:283-293` | shield 111-150 % of Max HP | |
| Reflection (defender) / Shield-shatter + dispel (attacker) queries | `:267-299` | | Read by `ResolveHit`. |
| Dispel stance buffs (`WeaponStance`, `GeneralUtility`) | `:301-319` | chance `dispelBuffChance` 50 % | |
| One-hit buffs (`consumedOnNextHit`): checked by `HasNextHitBuff`, spent after a hit that did not evade | `:321-352`; `PlayerMeleeCombat.cs:770-771` | | |
| Death clears all buffs | `:354-363`; `PlayerDeathMotor.cs` (`ClearAllBuffs` call at kill) | | |
| `BuffStarted`/`BuffEnded` to `Feel` | `:177,404` | | Presentation. |
| Save | none (buffs are not saved) | | |

- DOTS today: buff skills cast on the server body and the view body shows them (section 6); the server body's `CharacterBuffManager` runs. No buff state reaches other players (the remote body strips it).
- Dependencies: `PlayerCombatSkills`, `CharacterCombatStats`, `Vitals`, `PlayerResources` (heal), `SquadBuffRules`.
- Risk: **MEDIUM**. Not hard, but the multi-peer mirroring by message must become one rule: server owns buffs; the owner gets its own list, others a bitfield (design: `BuffState` 8-byte bitfield `ghosts-design.md:443`).
- Size: M. 0 RPC / 0 NV (buffs travel in the skill RPCs above) -> an owner-only buff buffer + a replicated bitfield.
- DOTS edge: stays server-managed C# (`ghosts-design.md:443`); stat modifiers still on the server body; buff stack changes re-sent as a page when they change; clocks as end ticks, not `RemainingDuration`.

## 8. PlayerCombatState (`Combat/Runtime/PlayerCombatState.cs`, 259 lines): InCombat

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| NV `netInCombat` bool, `Everyone` read, `Server` write | `:51-52` | | The fight music, Cadence, pets' idle and `InFightFor` read it on every machine (`:72,78`). |
| Reasons (flags): Blow, Attack, Callout, Hostile, Held, Dummy | `:12-31` | | `Why(...)` is a pure static (`:95-100`), covered by `CombatStateRulesTests` (9 tests). |
| Linger after the last blow given or taken, last skill cast, or last Hostile threat | `:46-47`, prefab `Player.prefab:3907` | **6 s** (min 0.5) | |
| Server looks again every | `:44` | 0.25 s | |
| Triggers: `CombatEntity.Damaged`, `PlayerMeleeCombat.HitResolved`, attack-skill `SkillCast` | `:113-115`, `:153-159` | | |
| Hostile threat: a Hostile that targets the player calls `ServerNoteThreat` every 0.5 s | `:173-178`; `HostileNet.cs:36,417-428` | `ThreatTellInterval` 0.5 | The fight starts when a Hostile turns on the player, not at the first hit. |
| Held: always-a-fight ground (war, The Scar) or GM hold | `:184-188` | | |
| Strike Dummy rule: from the first blow on a dummy until it is no longer selected and the player is 30 m away | `:48-49`, `:206-231` | `dummyLeaveMetres` 30 | Needs the owner's selection (`SelectionRpc`, section 4). |
| Save | none | | |

- DOTS today: nothing. `PlayerCombatState` is kept on the server and own view (`PlayerRoleTable.cs:75`), stripped on remote bodies.
- Dependencies: melee, skills, hostile threat (M5), duel (`IsDuelling`), `Vitals.InCombat` bit, `PlayerCombatState.Why` ports as a pure function.
- Risk: **LOW**. A server flag with a timer; `Why` and `DummyHolds` are already pure and tested.
- Size: S. 1 owner->server RPC (`SelectionRpc`), 1 NV -> `InCombat` enableable bit.
- DOTS edge: `InCombat` bit in `Vitals`/ghost (`coverage-table.md:127`, `ghosts-design.md:188`); timers as last-act ticks.

## 9. CombatHostility and the PvP rule (`Combat/Runtime/CombatHostility.cs` 42 lines, `Factions/Runtime/FactionRules.cs`, `World/Runtime/TradeZone.cs`, `World/Runtime/Travel/ZoneRules.cs`)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| `IsHostile(attacker, target)`: false for null or self; **anything that is not a player and can take damage is always hostile** (dummies, monsters, also any NPC with an `IDamageable`) | `CombatHostility.cs:29-32` | | "Player" = has a `PlayerResources` (`:20`). |
| Two players: neutral ground first (either end of the pair standing in a `TradeZone` marked `crossFaction`), then The Scar (`ZoneRules.AlwaysOnPvp`: different known factions are hostile whatever the switch) | `:33-38`; `TradeZone.cs:47-50,62-67`; `ZoneRules.cs:23` | zone = rotated rectangle, `size` default 60 x 60 m | Neutral ground wins over The Scar. |
| Otherwise `FactionRules.AreHostile(a, b, neutralGround)`: in a duel (either side says it duels the other) -> hostile, also on neutral ground; neutral ground with no duel -> peace; else `CrossFactionPvp && both factions known && different` | `FactionRules.cs:42-65` | | Same faction is never hostile (unless duelling); `FactionId.None` is never hostile. |
| `CrossFactionPvp`: a static bool; default from `FactionCatalog.crossFactionPvp` (on), taken once per session; a GM switches it | `FactionRules.cs:20-38`; `Factions/Data/FactionCatalog.cs:13-14`; `PlayerIdentity.cs:119`; `Tools/Debug/GmCommands.cs:399-410` (`gm.pvp [true|false]`) | default **on** | The memory note "other factions hostile by default, gm.pvp switches it" matches. GM window toggle `Tools/Editor/GM/GmToolsWindow.cs:588-591`. |
| Replication of the switch: NV `netCrossFactionPvp` on every `PlayerIdentity`, `Server` write; `SetCrossFactionPvp` writes it on all identities; clients write the static on change | `PlayerIdentity.cs:37,137-139,162-173` | 1 bool | |
| Hook: `HostilityCheck.Rule` (Player assembly, filled by Combat at load) | `Player/Runtime/CombatContracts.cs:26-35`; `CombatHostility.cs:23-27` | | Layering shim. |
| Callers | `PlayerMeleeCombat.cs:734,753`; `PlayerSkillAttacks.cs:123,283`; `WeaponHitbox.cs:182`; `PlayerDuel.cs:135`; `PointAndClickPlayer.cs:730,1149,1189,1890`; `UI/Runtime/DuelHudController.cs:171`; `UI/Runtime/PlayerNamecardController.cs:204` | | Server uses it on every hit; the client uses it for cursor colour, namecard colour and cast pre-check. |
| Tests | `Combat/Tests/Editor/HostilityCheckTests.cs`, `Factions/.../FactionRulesTests.cs` | | These tests are the table of cases the DOTS function must match. |

- DOTS today: the function runs on the client and the server body as managed code from the scene. The PvP switch does **not** replicate to a DOTS client (the NV lives on `PlayerIdentity`, which is a role-aware stub there; `FactionRules.CrossFactionPvp` is a static that never changes on the client). A remote body keeps only `CombatEntity`/`FactionExtension`, so a client could never know a duel (no `PlayerDuel`).
- Dependencies: `FactionMember` (faction byte, exists as a ghost field per `ghosts-design.md:187`), `DuelState{OpponentPlayerKey}`, `TradeZone` AABB blob (M4b), `MapRules` singleton with `CrossFactionPvp` and `AlwaysOnPvp` (M4b).
- Risk: **LOW**. Pure and small. The only catch is the three inputs the client lacks today (switch, zone rules, duel).
- Size: S. 0 RPC, 1 NV (existing) -> 1 `MapRules` static singleton ghost (2 bools) + `DuelState` field + `TradeZoneBlob`.
- DOTS edge: `HostilityRules.IsHostile(in FactionMember a, in FactionMember b, in DuelState da, bool bothPlayers, bool neutralGround, bool alwaysOnPvp, bool crossFactionPvp)` (`ghosts-design.md:469`); keep `HostilityCheck.Rule` as the shim (`:470`); test table = the existing two test files plus the neutral/Scar/duel cases.

## 10. PlayerDuel (Callout): the combat side

The prompts, requests, results and numbers are in `M8-g4.md` section 6 (not repeated). What M4 needs from it:

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| NV `netOpponent` ulong (`ulong.MaxValue` = none), `Everyone` read, `Server` write; `FactionExtension.IsDuelling` reads it through `IDuelState` | `PlayerDuel.cs:23,34-35,57-58`; `Player/Runtime/CombatContracts.cs:26` | 1 NV | Both duelists' NVs are set when the countdown ends (`:218-232`). |
| Request range / answer time / countdown / leash | `:26-32`; prefab `Player.prefab:3549-3552` | 20 m / 30 s / 3 s / 60 m | The leash is checked by one of the two every frame (`:243-250`). |
| Win and loss: `Killed` while duelling the killer = win for the killer, nobody else; killed by anyone else = cancelled | `:261-271` | | Duel kills do not cost XP (`HostileKillRewards.cs:161-176`: only a Hostile's kill does). |
| Forfeit, leaving the game, GM start/end | `:181-186`, `:98-103`, `:239-258` | `gm.duel`, `gm.duel.end` (`GmCommands.cs:133,411-431`) | |
| Hostility is only consulted through `IsDuelling` | `FactionRules.cs:49-53,60-64` | | |

- DOTS today: stub (stripped on remote bodies).
- Dependencies: M4 hostility (section 9) and kill; `DuelState` on both ghosts; chat notices (M8-g4 section 1).
- Risk: **MEDIUM** (as in M8-g4). Size: M; 3 in (Social), 5 out, 1 NV (`ghosts-design.md:471`: `DuelState{OpponentPlayerKey}`).
- DOTS edge: `DuelOpponent` as a ghost field; countdown, leash and answer window as server ticks; the combat part of the duel (hostility, win on kill) is in M4, the prompt UI stays with M8 group 4.

## 11. Death and respawn (`Player/Runtime/PlayerDeathMotor.cs` 678 lines)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| Starts on `CombatEntity.Killed` on the authority; needs its death clips and warp asset | `:170-175`, `:200-215` | | |
| Off the ground (jump, vault, wall, tree, roof, 2.5 m above ground) -> the body de-rezzes in place (`Disintegrate`); else it falls | `:218-225`, `IsOffTheGround` `:250-268`, `Disintegrate` `:274-304` | `roofHeight` 2.5 m, `FallProbe` 80 m, `derezClipSpeed` 0.2 | Pet goes with the body (migration.md:1147). |
| Front half of the blow: face the blow and fall back; back half: stay as you stand ("back hits never turn the body"); crits go further | `:228-240`; chains `:67-72` | `critDistance` 5.5 m, `critLift` 0.55 (prefab `:3521`) | Direction comes from `KillingBlow.Direction`. |
| Motors let go of the body (jump, vault, wall, water), buffs cleared, agent switched off | `:231-246` | | |
| Pose NV `netPose` (Alive, OnBack, FaceDown, Gone), `Server` write, so a late joiner sees the body | `:107`, spawn `:176-185` | 1 byte | |
| RPCs, all server->clients: `DeathClientRpc(fromFront, crit, direction, landing, facing, killer)`, `DisintegrateClientRpc(direction, killer)`, `RevivedClientRpc()` | `:306,344,643` | | |
| Respawn after | `:105`, prefab `:3535` | **8 s** | Countdown announced at the start and at 3, 2, 1 (`:596-610`) to chat (`PlayerChat.cs:96-97,575`: "You are back on your feet."). |
| Respawn: agent off, `PlayerSpawnService.Respawn(transform)` (each map's spawn points), `RestoreAll` (full HP and Flux), pose Alive, get up | `:617-640` | | Also revived by a GM `gm.heal` / reset (`:532-535`). |
| XP penalty only for a death to a Hostile: `deathExperienceLossPercent` of the level's XP (max what is held); not for a Callout, another faction or GM | `Hostiles/Runtime/HostileKillRewards.cs:161-176`; `LevelGapSettings.asset:79`; `LevelGapRules.cs:63-68` | **5 %** | Server only; chat line "You lost n XP..." . M6 owns the kill rewards, M4 only raises the kill. |
| Mount: dismount on death | `PlayerMounts.cs:113` | | |
| Save | none | | |

- DOTS today: nothing built (no `DeathKnock` or `Dead` code in the clone). `PlayerDeathMotor` is on the server body but **disabled** (`PlayerRoleTable.cs:47-48,113-120`).
- Dependencies: `MotionPlan` (M3b `DeathKnock`/`Fall`), `Dead` bit, `DeathPose`, a spawn service on DOTS, `KillingBlow` direction, `Vitals`.
- Risk: **MEDIUM**. Designed in `m3b-design.md:18-23,56-58,104-112` (server-only, not predicted) but not built; M4 must bring it because combat can now kill.
- Size: M. 0 in, 3 out RPCs -> `MotionPlan` + `Dead` + `DeathPose` (1 NV -> 1 byte field).
- DOTS edge: `PlayerDeathServerSystem` per M3b design; the killer reference becomes the ghost id in the plan; respawn timer = 8 s x 30 = 240 ticks; countdown lines through the chat/owner-text RPC.

## 12. The Combat Dummy

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| An in-scene object "Combat Dummy" in `Zone01_StarterTown.unity` (also `Test_Cadence.unity`): capsule collider (r 0.5, h 2, y-axis), `CombatEntity` (`entityName` "Combat Dummy", `overrideMaxHealth` 600, `useCombatStatsForMaxHealth` on), `CharacterCombatStats` (fallback attributes 5 / 20 / 5 / 5), `NetworkObject` in-scene (synchronise transform on), a mesh | scene object at `Scenes/World/Towns/Zone01_StarterTown.unity:323351` and its components (`784644383-784644390`) | | Moved into the Training Yard by `World/Editor/StarterTownBuilder.Outskirts.cs:560-566`; kept across scene loads by `Networking/Runtime/SceneObjectKeeper.cs`. |
| Stats from `DummyTargetClass.asset`: base STR 5, VIT 20, AGI 5, SPR 5, HP pool 4600, Flux pool 50, weapon AP 10, defense 5 | `Progression/Data/Assets/CharacterClasses/DummyTargetClass.asset` | **derived by hand:** Max HP 4600 + 20 x 20 = **5000**, Flux 150, AP 20, DP 10, Hit 10, Evasion 30 | Against an attacker with Hit 20 the chance to hit is 20/(20+30) = 40 %; against AP 35 the hit is 25. Use as the golden example. |
| Always infinite health: a lethal hit refills it, no death event | `CombatEntity.cs:191-192,323,389-395` | | So it cannot test a kill or a respawn. |
| 3 NV of `CombatEntity` replicate its HP to clients | `CombatEntity.cs:29-34` | | |
| GM tools: `gm.dummy.hit` (a normal attack roll on the nearest dummy), `gm.dummy.reset` | `Tools/Debug/GmCommands.cs:596,617`; `BenchCommands.cs:341-368,432` | | |
| Strike Dummy fight rule | `PlayerCombatState.cs:206-231` | 30 m | |
| `IHostile`? No: `IsStrikeDummy` = standalone and not `IHostile` | `CombatEntity.cs:389` | | |

- DOTS today: the dummy is a scene `NetworkObject`; DOTS has no NGO host, so it does not exist on a DOTS server or client. `coverage-table.md:129`: "anchored static ghost with `Vitals`, hit proxy".
- Dependencies: a `MapAnchor` for the dummy (M4b/M0 anchors), `Vitals`, `GmFlags.Infinite`, hit proxy (capsule 0.5 / 2 m), registration of its ghost prefab.
- Risk: **LOW**. First target of the two-client test.
- Size: S. 0 RPC, 3 NV -> `Vitals` + `Infinite`.
- DOTS edge: `ghosts-design.md:543-544` (Static + Interpolated, `Vitals`, `Infinite`, spawned by the server from its anchor). **Owner-visible catch:** an infinite dummy never dies, so the first test of death/respawn needs a second, finite dummy or a player.

## 13. How Hostiles hit players today (`KNEmeraldPlayerBridge`, `HostileBrainLink`, `HostileNet`)

| Rule | NGO file:line | Numbers | Notes |
| --- | --- | --- | --- |
| `KNEmeraldPlayerBridge` makes a player visible to Emerald AI: faction "Player" (2); copies the player's `CombatEntity` health into Emerald's `Health`/`StartHealth` each time it changes; ignores Emerald's own damage number | `Hostiles/Emerald/Runtime/KNEmeraldPlayerBridge.cs:16-47`; set up `Tools/Editor/Setup/PlayerSystemsSetup.cs:201-209` | | `DamageCharacterController` -> `HostileBrainLink.Strike(entity)`. |
| `HostileBrainLink.Strike(target)`: needs both alive, awake; target too high above (`definition.reachAbove`) or on a wall out of reach (ray test, `WallReachSlack` 0.5, eye 1.2) -> the swing meets nothing | `HostileBrainLink.cs:237-244,262-286` | | |
| Damage = `CombatCalculator.CalculateAttack(hostileStats, playerStats, attackMultiplier, reflects, reflectChance)` with the player's reflection buff; **no** shield-shatter, crit allowed (default `canCrit`), no hit-chance modifier | `:248-258` | `definition.attacks[index].damageMultiplier` (min 0.1) | Applied by `target.TakeDamage` -> the player's `CombatEntity`. Hit chance from stats (hostile hit 75 % default, `monster-ai-design.md:37`). |
| `HostileNet.ReportStrike` -> `StrikeClientRpc(targetId, flags, damage)` to all clients; shows the comic hit; the number shows only for the hit player (`targetIsLocal`) | `HostileNet.cs:489-545` | flags crit 1, evaded 2, reflected 4, heavy 8 | |
| `ReportAttack` (`AttackClientRpc` with wind-up seconds, heavy flag), `ReportHit` (`HitClientRpc` flinch) | `:464-475`, `:561,572` | | M5/M6 hostile presentation. |
| Threat: hostile calls `ServerNoteThreat` every 0.5 s on its target | `:417-428` | | Section 8. |

- DOTS today: nothing; Emerald is dropped (`coverage-table.md:222`, `V3`). Players take no damage on DOTS.
- Dependencies: the M5 monster brain (new), `PlayerDamageQueue` bridge (`monster-ai-design.md:133-135`) -> `CombatEntity.TakeDamage`, `Vitals`, `HitEvent` ring, hostility (non-player always hostile **to** a player).
- Risk: **MEDIUM**. The player side is M4 (`TakeDamage` bridge and `HitEvent`); the monster side is M5. Design (`ghosts-design.md:520`) gives monsters no lag compensation (the victim's current position is used).
- Size: S for M4's share (1 server->client RPC replaced by a `HitEvent` with attacker = ghost id).
- DOTS edge: a `HostileAttack{TargetGhost, DamageProfile}` event consumed by `DamageApplySystem`, rolling `CombatMath` with the player's `StatBlock`; the evade window by `MotionPlan` is a design idea (`ghosts-design.md:520`) with **no NGO counterpart** (NGO has no i-frames during a jump): see owner choice 7.

## 14. RpcBudget `CallKind.Combat` (`Networking/Runtime/Safety/CallLimits.cs`, `RpcBudget.cs`)

Every NGO combat request below spends `Combat` (burst 25, 15 per second per player; flooding disconnects). Count of the calls:

| Call | File:line | Budget? |
| --- | --- | --- |
| `RequestAttackEntityServerRpc` | `PointAndClickPlayer.cs:1884` | yes |
| `RequestCancelAttackServerRpc` | `:1875` | yes |
| `RequestMeleeStrikeDamageServerRpc` | `PlayerMeleeCombat.cs:713` | yes (deleted in DOTS) |
| `RequestCastSkillServerRpc` | `PlayerCombatSkills.cs:789` | yes + `TextFits` |
| `CastWardsServerRpc` | `:851` | yes + `TextFits` |
| `QueueServerRpc` / `ClearServerRpc` | `PlayerSkillQueue.cs:155,166` | yes (queue also `TextFits`) |
| `PressRpc` (Cadence) | `PlayerCadence.cs:221` | yes (M8-g4 section 12) |
| `SelectionRpc` | `PlayerCombatState.cs:244` | **no** |
| `ChargeStartedRpc` / `ChargeEndedRpc` | `PlayerSkillAttacks.cs:438,447` | **no** (they are relayed to all other clients: a flood amplifier) |
| Duel Request/Respond/Forfeit | `PlayerDuel.cs:125,150,181` | yes, `Social` |

- DOTS today: `RpcBudget`'s numbers are kept; `BodyOf(connection, CallKind.Combat, ...)` is used by `SkillCastRpc` only (`PlayerRequestSystems.cs:1071`). Attack input would ride the owner's input command (not an RPC), so it needs its own counter (e.g. attack events per second) to keep the NGO limit for the same abuse case.
- Risk: **LOW**. Size: S.
- DOTS edge: keep the numbers; one `CombatInputLimit` beside `BudgetClickLimit` (`PlayerMoveFromSkills.cs:100+`) for the attack/cast/charge input events; the three unbudgeted calls get a budget (cheap, an unauthenticated amplifier today).

## 15. What the presentation layer listens to (the reason for the HitEvent ring)

All of it is `ClientPresentationRegistry` sinks in `Core/Runtime`, called from the rules classes above; none of it needs to change except the source.

| Sink | Methods used by combat | Called from (NGO) |
| --- | --- | --- |
| `IGameFeelSink` (`Core/Runtime/IGameFeelSink.cs:64+`; `HitFeel`, `DeathFeel`) | `Swing`, `Hit(HitFeel)`, `Missed`, `LocalPlayerDamaged`, `SkillCast`, `SkillHit`, `ChargeStarted/Ended`, `BuffStarted/Ended`, `Consumed`, `Death`, `BodyLanded`, `Respawned`, `Disintegrate`, `SetDisintegrated` | `PlayerMeleeCombat.cs:605,889,910,987`; `PlayerSkillAttacks.cs:155-162,405,458,475`; `CharacterBuffManager.cs:177,404`; `PlayerResources.cs:302,309`; `PlayerDeathMotor.cs:655`; `HostileNet.cs:520-545` |
| `ICombatFeedbackSink` (`Core/Runtime/ICombatFeedbackSink.cs`) | `ShowCombatFeedback(pos, DamageResult)`: the damage number; `ShowFloatingText` (cast name) | `PlayerMeleeCombat.cs:888,900`; `PlayerCombatSkills.cs` `PresentCast`; `HostileNet.cs:531` |
| `IDuelSink` | `ShowRequest`, `HideRequest`, `ShowResult` | `PlayerDuel.cs:309-315` |
| C# events read by UI and others | `PlayerResources.Changed/Depleted` (HUD bars), `CombatEntity.HealthChanged/Damaged/Died` (nameplates, HostileNameplateController), `PlayerCombatState.Changed` (music, Cadence, pets), `CharacterBuffManager.BuffsChanged`, `PlayerCombatSkills.Changed/SkillCast/SkillMessage` (hotbar, messages), `PlayerChat` combat tab (`HitResolved`, `Damaged`) | as listed |

- A damage number is shown to the **attacker** (and the target for a hostile's blow), the comic hit to everyone near; evade sounds only for the attacker. The `HitEvent` ring must carry: attacker ghost, target ghost, damage, flags (crit, evade, reflect, shatter, elemental, skill, finisher step), position, tick. Each event once (a rollback never repeats it), gated by `BodyRoleTag.IsMine` (`PlayerMeleeCombat.cs:1004`).

## 16. Lag compensation and the PositionHistory ring (no NGO counterpart; design only)

| Rule | Source | Numbers | Notes |
| --- | --- | --- | --- |
| NGO has no lag compensation: the owner's blade sweep is local, the server's own blade decides; the only leniency is `hitLatencyGrace` and `impactRangeLeniency` | `PlayerMeleeCombat.cs:69,490-497`; `PlayerSkillAttacks.cs:41,240-245` | 0.35 s; reach x 1.5 | |
| Decision: **no Unity Physics package**; server-only `PositionHistory` ring on every hittable ghost, position + capsule, hit tested at the attacker's interpolation tick; spike S5 is removed | migration.md:152-153,267; `coverage-table.md:225,306`; `ghosts-design.md:476-496` | 16 ticks (533 ms at 30 Hz); test radius: melee 2.7 m (server validation range) | Design 4.4 still describes `PhysicsWorldHistory`/Unity Physics: **stale** against the later decision (the plan line wins). |
| Attacker's view time: `CommandDataInterpolationDelay` per connection (a Netcode for Entities component, names unverified on 6.6) | `ghosts-design.md:487,478` | | |
| A claim older than the ring is refused (the section 53 clamp pattern); a target that did not exist at the rewound tick cannot be hit | migration.md:1003; `ghosts-design.md:499` | | |
| PvP reach: larger of history reach and current reach + epsilon | `ghosts-design.md:473` | | |
| Map line of sight stays a PhysX raycast in managed server code | `ghosts-design.md:505` | | |

- Dependencies: `CommandDataInterpolationDelay`, hit proxy capsule per ghost (players, Hostiles, dummy), a tick clock.
- Risk: **HIGH**: the only part of M4 that is new rules, not a port. Server-only ring, one system.
- Size: M. 0 RPC, 0 NV (server-only buffer 16 x (float3 + capsule)).
- DOTS edge: `PositionHistoryWriteSystem` (end of each server tick); `HitTestAtTick(ghost, tick)` shared by melee and area skills; unit-tested at 0 and 100 ms (acceptance list, migration.md:1003).

## 17. Counts

Owner->server RPCs: attack 1 + cancel 1 + melee report 1 (deleted) + cast 1 + wards 1 + queue 2 + charge 2 + selection 1 = **10** (9 kept, 1 deleted); plus Duel 3 and Cadence 1 which belong to M8 group 4 (see M8-g4). Server->client RPCs: resources 1 + melee 4 + cast/buff 7 + skill vfx 1 + queue 1 + death 3 = **17**; plus Duel 5, Cadence 4, chat combat line 1, hostile strike 1. NetworkVariables in the M4 core: CombatEntity 3 + PlayerResources 4 + PlayerCombatState 1 + death pose 1 = **9**; shared-state NV that stay: GM toggles 2 (`PlayerGmState.cs:31-34`), PvP switch 1 (`PlayerIdentity.cs:37`); Duel 1 (M8-g4); ranks list and books ulong (M5b, `PlayerCombatSkills.cs:36-37`). Save keys: none (no combat state is saved). World docs: none. Ledger: none.

## 18. Suggested order inside M4

1. **Golden tables first, no netcode:** write the missing `CombatCalculator` golden test from the managed code (explicit-roll form), the hostility case table (`HostilityCheckTests`, `FactionRulesTests` plus neutral ground, The Scar, duel), then `CombatMath` + parity test. Nothing else moves until this is green (migration.md:990-992).
2. `Vitals` (public HP, MaxHP, Chi, level, `Dead`/`InCombat` bits) with `OwnerFlux` folded in; the facade rule (one writer); `Combat Dummy` ghost as the first target (finite + infinite variants).
3. `PositionHistory` ring, hit proxy capsule, `HitTestAtTick`, and its 0 ms / 100 ms tests (spike S5 removed).
4. Melee: `PlayerInput` target + attack events, `CombatState`, `StrikeTimingBlob` baked from `Player.prefab` and the three clips, `MeleeResolveServerSystem`, server walk-in through `ServerOrder` (pull in the M3c walk-in).
5. `HitEvent` ring -> `ClientPresentationRegistry` (damage number, `HitReactor`, VFX), `BodyRoleTag.IsMine` gate; remove `AttackerIsHere`.
6. Death/respawn on `MotionPlan` + `Dead`; kill raises `Killed`/`AnyKilled`; respawn 8 s.
7. Skills as input (single target first, then multi-hit, area, charge, queue, wards), Cadence press field; buff replication (bitfield + owner list).
8. PvP: `MapRules` singleton + `DuelState`; players as targets; duel combat part (prompt UI stays with M8).
9. Hostile -> player damage bridge (event consumed by `DamageApplySystem`) so the monster brain (M5) has something to hit.
10. Owner feel checkpoint (press to swing, press to number at 0 and 100 ms vs NGO).

## 19. Real owner choices (only where the design does not already decide)

1. **One formula or two?** The plan (migration.md:268,991) wants `CombatCalculator` rewritten once as a Burst static used by NGO and DOTS; `ghosts-design.md:219` and `coverage-table.md:131` say "untouched, called from `DamageApplySystem`, Burst only for monsters". Pick: (a) the plan: one Burst `CombatMath`, parity test, or (b) keep the managed formula for player hits and have only monsters use the Burst copy (two formulas, drift risk).
2. **Collateral hits.** Today the blade hits every hostile under it (once per swing), and the target for certain at the swing's end. The DOTS server has no animated blade. Pick: (a) target only (simplest), (b) target plus other hostiles in an arc/cone of the weapon's reach on the history world (feels like today), (c) target only for single-target, arc for a heavy third step.
3. **When in the swing does the hit land?** NGO: at the first sweep overlap inside the window, otherwise at the window's end (fractions 0.65 / 0.30 / 0.70 of the clip ÷ speed). Data ticks need one moment per step. Pick: window end (matches the fallback, slower), window start (snappier, a change), or midpoint.
4. **Which swing numbers are the truth?** The prefab values (1.411 / 1.30 / 1.126; step 3 window 0.33-0.70; post-combo 0.25 s) differ from the code defaults the design quotes. Recommendation: bake the prefab values; only confirm.
5. **HP/Flux precision:** hundredths (design) or thousandths (shipped `OwnerFlux`). Master can pick; no feel difference, but it has to be one.
6. **Attacks while walking in:** the server walks the player in and swings at 1.6 m; DOTS has no agent. Pick the walk (use the `ServerOrder` walk, same ring) with or without the owner predicting the approach (NGO's owner only draws the first strike on the click).
7. **Evade windows.** The design leaves open "a jump grants i-frames if designed" (`ghosts-design.md:520`). NGO has none. Recommend: no i-frames (rule parity), unless the owner wants one.
8. **Infinite dummy.** The Combat Dummy never dies (owner, 24 Oct). Add a second dummy that can die for the death/respawn test, or test death with a second player?
9. **Where the PvP switch lives:** a `MapRules` singleton ghost (design) or the existing `PlayerIdentity` field carried to clients; the first is cleaner and is what `ghosts-design.md:469` says. Only a confirmation.

### Decided (4 Nov 2026)
Owner's picks (multiple choice, all the recommended options):
1. **One shared formula:** `CombatCalculator` rewritten once as a Burst static used by NGO and DOTS, with a parity (golden table) test.
2. **Target + nearby arc:** the target, plus other hostiles in front within the weapon's reach on the history world (feels like today).
3. **The hit lands at the end of the window** (today's guaranteed hit point; no feel change).
8. **A second Combat Dummy that can die** (limited HP), for death / respawn tests and practice.

Master's calls (Nova, by the recommendations; the owner may overrule):
4. The shipped prefab swing numbers are the truth (1.411 / 1.30 / 1.126; step 3 window 0.33-0.70; post-combo 0.25 s); the blob is baked from the prefab.
5. HP / Flux in thousandths, as the shipped `OwnerFlux`.
6. The server walks the attacker in with the `ServerOrder` walk; the owner's view draws only the first strike on the click (NGO's rule); no client-predicted approach for now.
7. No i-frames on a jump (rule parity with NGO).
9. The PvP switch is a `MapRules` singleton ghost (as `ghosts-design.md:469`).

## 20. What could not be found or is unclear

- **Clip lengths** of the three Katana combo clips (inside the Grruzam FBX): not read, so strike durations in seconds and ticks are unknown here. The bake must read them.
- **`CommandDataInterpolationDelay` / lag compensation API names** on Unity 6.6: unverified in the design (`ghosts-design.md:478`), and obsolete after the "no Unity Physics" decision.
- **Per-weapon-class combos:** the code has one clip set; the design's per-class blob has no NGO source. `04_Weapon_Classes.md` was not read for this table.
- **`TargetPositionModifier`** is a vendor (Emerald) component; its behaviour is only known from the way it is set up (`PlayerSystemsSetup.cs:207`). It is not game code.
- **Scene data the hit tests need:** the only scene data are the Combat Dummy's capsule (r 0.5, h 2) and `TradeZone` rectangles; no layer filter is used (`sweepLayers`, `areaTargetLayers` are `~0`). Players' and Hostiles' hit-capsule size was not measured (the Player prefab has a `CapsuleCollider`; not read).
- **The attack-skill hole on the DOTS view body** (section 6) is from reading, not from a run.
- **Level and attributes** feed Max HP/damage; the numbers in section 3 for a real player depend on `CharacterProgression.asset` and gear (M5b), not read here.
