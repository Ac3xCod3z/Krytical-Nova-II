# M5 research: the NGO rules table for Hostiles (step M5.0)

Read-only research, 6 Nov 2026, in `C:\Users\dlaco\Krytical Nova II DOTS` (branch `dots/netcode-entities`). Paths are relative to `Assets/_Game/`.
Graph first (`graphify explain` HostileSpawner / HostileBrainLink, `graphify affected` HostileNet / IHostile, `graphify query`), then only the pointed lines were read.
Nothing was run, built or changed. Numbers worked out by hand say so.

**Signed off (Nova, 6 Nov 2026): option A.** The 6 MISMATCH rows follow the code (parity), except where an owner-confirmed Q1-Q8 change replaces it (each row says which). Also: `HostileStrikeRules.TryRoll` takes the attacker's `Transform` (the wall ray skips its own colliders); 10 species; `noDeathLossFromPrimeLevel` is unread today and stays unread.

**Summary.** (1) All 25 rows of plan section 3 were checked against the code; every `[line: M5.0]` is filled (H01, H14) and 5 line references are corrected (H05, H11, H15, H21, H24).
(2) **6 rows are MISMATCH** (H01 aggro is rounded up to whole metres, H03 a hit only turns a Hostile with no target, H14 the give-up is Emerald's
5 s out of detection range and does not heal, H17 ignore is sight only, H20 there is no GM aggro/calm, H22 Magnet and the target ring do not read HostileNet).
(3) Settings, curve and level-gap assets are listed field by field (section 2). (4) Seam check: `HostileSpawner.HasAuthority` **is true on a DOTS
client** (NGO not listening), so the 8 Hostile Zones of Zone01 spawn offline Emerald Hostiles around the local player on every DOTS client (section 5).

## 0. Shared facts

| Fact | Where | Numbers |
| --- | --- | --- |
| Roster: 10 species (ids 2-11), not 11 | `Gameplay/Hostiles/Editor/HostileAssetBuilder.cs:100-112`; assets `Hostiles/Data/Assets/Species/Hostile_*.asset` | Normal L1 Bonewalker, L4 Blightborn, L7 Blight Reaver, L10 Gargoyle, L13 Wraith, L16 Seraph Unit, L19 Carapace Raider, L22 War Frame; Bruisers L12 Hellion, L21 Colossus. Scavenger retired (`:115`). Plan M6.1 says "11 species". |
| Every prefab points at the same three assets | `Hostiles/Prefabs/Hostile_Bonewalker.prefab:4336-4338` | `HostileStatCurve.asset`, `HostileSettings.asset`, `LevelGapSettings.asset` (guids match their .meta) |
| Per-species numbers on the definitions | `Hostiles/Data/HostileDefinition.cs:43-48`; assets | `healthMultiplier` 1 for all; `attackRange` 1.8 (War Frame 2.2, Hellion 2.4, Colossus 2.6); `reachAbove` not serialized in any asset, so the code default **3 m** |
| Timers are frame time | everywhere below (`Time.time`) | DOTS must turn them into ticks (30 Hz) |

## 1. The rules (plan section 3, checked)

Hostiles code is under `Gameplay/Hostiles/` (`Runtime/`, `Emerald/Runtime/`, `Data/`, `Editor/`).

| Id | Rule as the code does it | NGO file:line | Numbers | DOTS step | Test |
| --- | --- | --- | --- | --- | --- |
| H01 | **MISMATCH -> PARITY (Nova, 6 Nov): build 9 m as the code does.** the curve gives a continuous radius, then Emerald gets it **rounded up to whole metres**, so every roster species (L1-22) scans **9 m**, not 8-8.8. Set for every species (FightsBack too; it just holds players Neutral). | `Data/HostileStatCurve.cs:86` (field), `:180` (formula); applied `Emerald/Runtime/HostileBrainLink.cs:109-111` | `min(z, x + L / y)` with x 8, y 28, z 12; then `CeilToInt`: 9 m L1-28, 10 m L29-56, 11 m L57-84, 12 m L85+ (by hand) | M5.3 | `HostileRulesTests.AggroRadius_MatchesCurve` (assert the ceil) |
| H02 | AttacksOnSight = Emerald relation Enemy; FightsBack = Neutral (never picked by sight, turns when hit). Roster: level >= 10 **or Bruiser** attacks on sight. | `HostileBrainLink.cs:112-118` (relation `:114`); `HostileAssetBuilder.cs:129` (roster rule) | FightsBack: L1, L4, L7. AttacksOnSight: L10-22 and both Bruisers (L12, L21) | M5.3 | `HostileBrainTests.FightsBack_IgnoresPlayerInRange` |
| H03 | **MISMATCH -> REPLACED BY Q5 (threat table, switch at 110 % in melee / 130 % at range; first hit keeps the kill) and Q8 (notice 0.1 s); PARITY for the rest (an evaded, reflected or 0-damage hit adds nothing).** a hit turns the Hostile on its attacker at once **only when it has no target** (`CombatTarget == null`); a Hostile already fighting someone does not switch. No turn on an evaded, reflected or 0-damage hit, an attacker without `IFaction`, or in the frame it woke (`!Started`). Emerald's own path (`EmeraldHealth.CheckForAttacker`, only with no target and no combat state) is the fallback. | `HostileBrainLink.cs:186-206` (guard `:189-191`, IFaction `:194`, turn `:196`, Started `:184`); `_ThirdParty/Emerald AI/Scripts/Components/Required/EmeraldHealth.cs:76,135-141` | 0 s (same frame) | M5.3 | `ReactionTests.HitTurnsSameTick` (+ "keeps current target") |
| H04 | Speeds come from the curve and overwrite the prefab's Emerald values in `Configure` (the prefab numbers are never used). | `HostileStatCurve.cs:87-90`, `:181-182`; applied `HostileBrainLink.cs:101-103` | walk 1.8; run 4.2 + 0.006 x L (L1 4.206, L22 4.332, L112 4.872) | M5.3 | `HostileRulesTests.Speeds_MatchCurve` |
| H05 | Three attacks per species, Emerald picks by `AttackOdds` = weight. Corrected from `:323-325` (that is the copy; the numbers are the rows). | rows `HostileAssetBuilder.cs:83-85`; copy `:317-328`; odds `:594`; struct `HostileDefinition.cs:17-28` | weights 45/40/15; multipliers 1.0/1.0/1.5; windup 0.25/0.25/0.55 s (capped at 0.9 x clip length); third `heavy` | M5.3, M6.1 | `HostileScoreTests.FlatCurves_EqualWeightedDice` |
| H06 | Roll = `CombatCalculator.CalculateAttack(mine, target.CombatStats, multiplier, reflects, reflectChance)`, then `target.TakeDamage(result, mine)`, then `net.ReportStrike`. Multiplier = attack's `damageMultiplier`, min 0.1; reflection from the target's `CharacterBuffManager.HasReflection`. | `HostileBrainLink.cs:245-259` (multiplier `:248-251`, reflect `:253-255`, roll `:257`, apply `:258`, report `:259`) | 1.0 / 1.0 / 1.5 | M5.6 | `StrikeRulesTests.TryRoll_EqualsNgoStrike` |
| H07 | No hit when target feet are higher than own feet + `reachAbove`. | `HostileBrainLink.cs:241` | 3 m (default, every species) | M5.3 | `HostileAttackTests.AboveReach_Misses` |
| H08 | Target "on a wall" = angle(body.up, world up) > 30 deg. Then lands only if eye-to-feet distance <= range + 0.5 + 1.2 and a ray from the eye (feet + 1.2 m up) to the target's position hits nothing but the two bodies (default layers, triggers ignored). | `HostileBrainLink.cs:244`, `:262-286` (`OnAWall` `:267`, reach `:271-276`, ray `:278-284`) | 30 deg; slack 0.5 m; eye 1.2 m; range fallback 2 m | M5.6 | `StrikeRulesTests.WallRule` (B73) |
| H09 | Damage through `TakeDamage` (DOTS: M4.9 queue, section 4). Hit player sees the number. Death to a Hostile (attacker has `HostileNet` with a `LevelGap`) costs XP once (on `CombatEntity.AnyKilled`): `round(ExperienceRequired x 5 / 100)`, clamped to the XP held, never a level. Off with `HostileRates.DeathPenalty`. NOTE: `noDeathLossFromPrimeLevel` (33) is in the asset but **no code reads it**. | `Combat/Entities/Runtime/HostileStrikeSystems.cs:86-94,140-160`; `Runtime/HostileKillRewards.cs:161-175`; `Runtime/LevelGapRules.cs:63-68`; `Runtime/HostileRates.cs:15` | 5 % | M5.6 | B54 (exists) + B74 |
| H10 | First player to deal damage > 0 (not evaded) holds the kill. Same player or a Squad mate refreshes it; anyone else cannot take it until it lapses. Lapses after 10 s without damage from the holder's side, or when the holder is dead or gone. Released on Hostile death, leash, zone sleep and pool revive. Synced as `netTag` (holder's NetworkObjectId). | `Runtime/HostileNet.cs:335-399` (TagHolder `:335-342`, `IsTaggedByAnother` `:345-353`, `TagLapsed` `:355-364`, `OnDamaged` `:366-375`, `SetTag` `:384-391`, `ReleaseTag` `:394-397`); releases `:225,257,273`, `HostileZone.cs:237` | `LevelGapSettings.tagTimeoutSeconds` 10 (fallback 10) | M5.6 | B75 |
| H11 | Credited = tag holder, else the blow's attacker (GM kill). Alone: paid by own gap. Squad: only members in the kill's scene; a member >= 6 levels better-gapped than the worst is paid at the worst gap; Squad boost `ServerExperienceBoost`. Sponsor with a Recruit in the Squad: holder Sponsor pays nobody XP (and drops nothing at the no-drop gap); a Sponsor member is paid its own gap, no boost. XP = `max(1, round(stats.Experience x band% / 100 x (1 + boost) x HostileRates.Experience))`, 0 if band 0 %. Pet and mount credit hang on `Rewarded`, corrected from "`:52-160`": they are not in KillRewards. | `HostileKillRewards.cs:52-123` (credit `:55-58`, Sponsor `:67-87`, Squad `:92-110`), `:126-132` (formula), `:134-157` (Pay: Sponsor bonus, boosters); pet `Runtime/HostileLoot.cs:43` -> `Pets/Runtime/PlayerPet.cs:520-526`; mount `Mounts/Runtime/PlayerMounts.cs:283-287`; gap `LevelGapRules.cs:11,30-38,57-60` | bands in section 2.3; `squadCarryLevels` 6 (not in asset, code default) | M5.6 | B76 (compare XP with NGO numbers) |
| H12 | On `Rewarded`: Creds as item 9040 (max 60 000 per pile), Stim, gear, then each material row rolled alone. All scaled by `dropPercent/100 x HostileRates.Drops`. Placed at the corpse, held for the credited player. | `HostileLoot.cs:38-71`, `Roll` `:95-118`, `RollMaterials` `:78-89`, `PickGear` `:124-140`; `Items/Runtime/LootRules.cs:57-58`; `Items/Runtime/PlayerLoot.cs:203-206`; start: `HostileNet.cs:265-274` (`AnyKilled` `:272`) | Creds chance 0.7, amount `round((3 + 1.1 x L x rarity) x tierCreds) x (1 +- 0.2)`; Stim 6 %; gear 2.5 % / Bruiser 25 % (best rarity), item level L-3..L; hold `dropReserveSeconds` 20 (`Player/Prefabs/Player.prefab:3649`) | M5.6 | B77 |
| H13 | On `Died` (every peer) the body lies, then burns away; the authority removes it to the pool after `CorpseLifetime`. | `Runtime/HostileCorpse.cs:53,63-88`; `HostileNet.cs:276-281`; `Data/HostileSettings.cs:28-30,84` | 3 s + 1.4 s = 4.4 s (fallback 4 s) | M5.5 | B72 |
| H14 | **MISMATCH -> PARITY: the 5 s give-up beyond the detection radius (no heal, no tag release) and the leash exactly as below.** leash = Hostile's flat distance from the zone centre > radius + 15 m, **or** its target on cross-faction neutral ground (`TradeZone.CrossFactionAt`): Calm + full heal + tag release, checked each zone tick. The "out of reach 6 s" give-up does not exist: Emerald gives up after **5 s with the target beyond the detection radius** (`ChaseSeconds` 5) and only cancels combat (no heal, no tag release). `StayNearStartingArea` is off. | `Runtime/HostileZone.cs:226-239` (filled), limit `:228`, test `:234`; `HostileSettings.cs:69`; Emerald `Components/Required/EmeraldBehaviors.cs:23,258-276`; prefab `Hostile_Gargoyle.prefab:2147,2156` | 15 m; tick 0.5 s; Emerald 5 s | M5.3 | `HostileBrainTests.Leash_*` (decide: keep 5 s/out-of-range or the plan's 6 s unreachable) |
| H15 | Zone check every 0.5 s. Awake while any player is within radius + `wakeMargin` (flat). Asleep: brain off, animator off, can still be hit; going to sleep calms, heals (living ones) and releases the tag. Respawn timers keep running while asleep. Corrected from `:100-160`. | `HostileZone.cs:100-116` (Update), `:131-154` (Tick, wake `:133-140`); `HostileNet.cs:247-261` (`SetAsleep`); `Data/HostileZoneDefinition.cs:17-22` | tick 0.5; wakeMargin 40 m (all 8 zones); radius 30 (north-gate 25) | M6.2 | `ZoneSleepTests` + B80 |
| H16 | Up to 4 spawns per tick; a failed fill retries in 1 s. Spot: 8 tries, NavMesh sample 6 m, inside the radius, >= 3 m from every player. Bruiser exactly on the 0.85 x radius ring. Respawn from death: Normal `clamp(8 + 0.12 x L, 8, 22)`, Bruiser `Random.Range(480, 900)`. Map cap 300 active. | `HostileZone.cs:21-22,143-150,172-184,196-201,204-220`; `HostileStatCurve.cs:91-94,167,183-184`; `Runtime/RespawnQueue.cs:9-30`; `HostileSpawner.cs:15,63` (NavMesh 4 m) | L1 8.12 s, L22 10.64 s (by hand); maxAlive 12/14/10 per zone | M6.2 | B81 |
| H17 | **MISMATCH -> PARITY: ignore is sight only; a hit from an ignored player still turns the Hostile.** `HostileIgnore` is copied into Emerald's static `IgnoredTargetsList`, which only `DetectTarget` (sight) reads. A hit from an ignored player still turns the Hostile (`SetDetectedTarget` at `HostileBrainLink.cs:196` skips the list), as `gm.hostile.ignore` says. | `HostileBrainLink.cs:51-60`; `Runtime/HostileIgnore.cs:11-33`; Emerald `EmeraldDetection.cs:297-300`; `Tools/Debug/HostileCommands.cs:211` | | M5.3 | `HostileBrainTests.IgnoredPlayer_NeverTargeted` (rename: never by sight) |
| H18 | Every 0.5 s while awake the authority tells the brain's current target `PlayerCombatState.ServerNoteThreat()`; the player stays in a fight 6 s after the last note. | `HostileNet.cs:36`, `:417-428`, called `:434`; `Combat/Runtime/PlayerCombatState.cs:187-192`, linger `:47,113` | 0.5 s; linger 6 s (`Player.prefab:3907`) | M5.6 | B74 |
| H19 | `IHitChanceModifier`: a player's hit chance on a Hostile gets the band's modifier by gap = Hostile level - player level. | `HostileNet.cs:404-409`; `LevelGapRules.cs:43` | Purple (5..9) -5, Red (10+) -15 points, else 0 | unchanged (body) | B76 |
| H20 | **MISMATCH -> PARITY: only the commands that exist (no aggro / calm).** commands are `gm.hostile.list/spawn/clear/kill/info/hp/freeze/ignore/ttk/loot/stats` and `gm.zone.list/fill/clear/respawn/sleep/bruiser`. There is **no aggro or calm command**; `IHostileBrain.Aggro` has no caller, `Calm` is called only by sleep and leash. Freeze stops `EmeraldSystem` and the agent; frozen Hostiles still take hits. | `HostileCommands.cs:73,88,130,145,163,180,195,211,223,324,383,403,412,422,434,447,458`; `Runtime/IHostileBrain.cs:10-36`; `HostileBrainLink.cs:121-153` | `gm.zone.clear` default 30 s | M6.3 | `GmHostileTests` + B82 |
| H21 | Attack start: authority `ReportAttack` -> `AttackClientRpc` (Attack Index + trigger on clients) -> `ShowAttack`: heavy shows "!" at body height and a Feel swing (step 3, windup). Flinch: `ReportHit` -> `HitClientRpc`. Lasting state (speed, combat, stunned, dead + death index) sent every 0.1 s; death clip on `Dead`. Note: Emerald only flinches on non-critical hits (`EmeraldHealth.cs:112`). Corrected from `:544-575`. | `HostileNet.cs:432-446` (state), `:448-471` (Play, death), `:476-484` (ReportAttack), `:543-553` (ShowAttack), `:556-559` (ReportHit), `:561-578` (RPCs); hooks `HostileBrainLink.cs:223-231` | 0.1 s; "!" at `bodySize.y x scale` | M5.5 | B71, B72 |
| H22 | **MISMATCH -> PARITY: consumers keep polling HostileNet.Active.** nameplate, minimap and Autopilot read `HostileNet.Active` (by polling: no outside code subscribes to `LevelChanged`, `TagChanged` or `AttackStarted`). The target ring reads `WorldTargetKind` from `HostilityCheck`, not HostileNet; `MagnetController` reads no Hostile type. Full list in section 3. | `graphify affected "HostileNet"`; `Player/Runtime/PointAndClickPlayer.cs:738,746,1232`; `UI/Runtime/WorldMarkerPresenter.cs:180` | | M6.4 | B83 |
| H23 | No NGO rule for Hostiles: a Hostile always lies and burns (H13), wherever it dies. The existing de-rez is the player's (DOTS). | owner rule 5 Nov; `Persistence/Entities/Runtime/DeathSystems.cs:35,55` | | M5.6 | B78 |
| H24 | An area skill hits every hostile body in the sphere: the collider buffer doubles until it holds them all (NGO stopped at 64). Corrected from `MeleeResolveServerSystem.cs:85-112`, which is the melee arc's bystander hits (M4.4g). | `Combat/Runtime/PlayerSkillAttacks.cs:44-45`, `CollectAreaTargets` `:276-293`, `HitsHere` `:299-300` | no cap | M6.5 | B84 |
| H25 | Not matched on purpose (brain plan L1-L11): 1 s sight scan (prefab `DetectionFrequency` 1), 1 s path check, animator-gated swings, hits block the next attack, attack announced a frame late, 0.6 s fight-back wait, 0.1 s obstruction rays. | `m5-m6-brain-plan.md:57-67`; `Hostile_Gargoyle.prefab:1974,1982` | 1 s, 1 s, 0.35 s, 0.6 s, 0.1 s | M5.3 | `ReactionTests` (the opposite) |

## 2. Settings assets as they are today

### 2.1 `Gameplay/Hostiles/Data/Assets/HostileSettings.asset` (class `Data/HostileSettings.cs`)

| Field | Value | Meaning (source comment) |
| --- | --- | --- |
| corpseSeconds | 3 | seconds a dead Hostile lies before it burns away |
| dissolveSeconds | 1.4 | seconds the burning takes; then back to the pool (`CorpseLifetime` = 4.4) |
| dissolveShader | `Hostiles/Shaders/HostileDissolve.shader` | kept here so a build includes it |
| dissolveNoise | texture guid 8ca60805... | noise the body burns along |
| burnColor | (0.2, 3, 3.4, 1) HDR | glowing edge |
| burnWidth | 0.07 | edge width as a share of the noise |
| noiseTiling | 0.8 | noise repeats per metre |
| catalog | `Items/Data/Assets/ItemCatalog.asset` | where gear drops are picked |
| stims | `Pill_Vitality`, `Pill_Chi` | Stims a Hostile may drop, one at random |
| stimChancePercent | 6 | chance a kill drops a Stim |
| gearChancePercent | 2.5 | Normal Hostile gear chance |
| bruiserGearChancePercent | 25 | Bruiser gear chance, best rarity of its level |
| gearLevelsBelow | 3 | gear item level from L-3 to L |
| announceDrops / announceDropsFrom | 1 / 3 (Epic) | ticker announcement at or above this rarity |
| materialDrops | 90 rows, each rolled alone (chance %, minLevel; maxLevel 0 for all) | 4 x 1.5 % any level; 1 x 0.5 %; 2 x 0.3 % L10+; 2 x 0.2 %; 29 x 0.1 % L60+; 1 x 0.1 % L70+; 12 x 0.25 % L70+; 3 x 0.02 % L70+; 36 x 0.02 % L113+ |
| maxActiveHostiles | 300 | Hostiles a map may hold at once |
| zoneTickSeconds | 0.5 | seconds between a zone's checks |
| leashMetres | 15 | metres past the zone edge before giving up |
| spawnClearance | 3 | never appears closer than this to a player |
| wanderRadius | 8 | metres it wanders from where it appeared |
| spawnsPerTick | 4 | spawns per zone check |
| minimapMetres / minimapMarkers | 60 / 24 | minimap marker range / most markers |

### 2.2 `Gameplay/Hostiles/Data/Assets/HostileStatCurve.asset` (class `Data/HostileStatCurve.cs`, formulas `:118-186`)

| Field | Value | Meaning (source comment) |
| --- | --- | --- |
| tuning | `Progression/Data/Assets/CombatTuning/CombatStatTuning.asset` | attribute ratios and hit limits (code fallbacks: Str->AP 1, Spi->AP 1, Str->Hit 2, Agi->DP 1, Agi->Eva 2, Vit->Eva 1, Vit->HP 20, HP/level 15) |
| progression | `Progression/Data/Assets/CharacterData/CharacterProgression.asset` | XP per level and stat points per level (fallbacks 5 points/level, `100 x L^1.5` XP) |
| startingAttribute | 10 | each attribute of a new character |
| pointShares | (0.5, 0.2, 0.2, 0.1) | share of stat points in Str, Vit, Agi, Spi |
| classAttackAndDefense | (15, 5) | class AP and Defense before gear |
| classHealth | 100 | class Max HP before level and Vit |
| gearAttackPerLevel | 5.2 | weapon AP per level before rarity |
| gearDefensePerLevel | 4.85 | full-set Defense per level before rarity |
| gearHitRatePerLevel | 3.5 | weapon + gloves Hit per level |
| gearEvasionPerLevel | 0.7 | armor + boots Evasion per level |
| expectedGearRarity | (1, 1.7, 2) | rarity multiplier at L1, at the cap, at the last Prime level |
| levelCap / primeLevels | 112 / 33 | player cap; Prime levels above it (MaxLevel 145) |
| hitsToKill | 5 | plain hits the reference player needs |
| defenseShare | 0.3 | Hostile Defense as a share of the player's AP (capped at 0.9 x AP) |
| hitsToDie | 25 | hits a Hostile needs to kill the player |
| playerHitChance / hostileHitChance | 0.9 / 0.75 | how often each side lands |
| criticalRate | 5 | Hostile crit % |
| killsPerLevel | (40, 6) | even-level kills to level: 40 + 6 x L (XP = XPtoNext / that x tier) |
| creds | (3, 1.1) | Creds = 3 + 1.1 x L x rarity (x tier) |
| credsSpread / credsDropChance | 0.2 / 0.7 | +-20 %; 70 % of kills drop Creds |
| aggroRadius | (8, 28, 12) | base, levels per metre, cap |
| walkSpeed | 1.8 | m/s |
| runSpeed | (4.2, 0.006) | base + per level |
| respawnSeconds | (8, 0.12, 8, 22) | base, per level, min, max (Normal) |
| bruiserRespawnSeconds | (480, 900) | random between |
| tiers | Normal all 1; Bruiser health 8, damage 1.6, defense 1.5, hitRate 1.2, evasion 1.2, experience 6, creds 5 | what a tier multiplies |

Formulas (`:156-185`): `defense = min(AP x 0.3 x tierDef, AP x 0.9)`; `health = 5 x AP x 0.7 x tierHealth` (x species `healthMultiplier`, `HostileDefinition.cs:78`);
`attack = playerDef + playerMaxHP / 25 x tierDamage`; `hitRate = playerEva x 0.75 / 0.25 x tierHit`; `evasion = playerHit x 0.1 / 0.9 x tierEva`; all rounded.

### 2.3 `Gameplay/Hostiles/Data/Assets/LevelGapSettings.asset` (class `Data/LevelGapSettings.cs`; gap = Hostile level - player level)

| Band | Gap | XP % (one per gap from the low end) | Drop % | Gear | Hit modifier |
| --- | --- | --- | --- | --- | --- |
| Grey | -200..-10 | 0 | 0 | no | 0 |
| Blue | -9..-6 | 8, 13, 22, 37 | 25 | no | 0 |
| Teal | -5..-3 | 61, 80, 97 | 75 | yes | 0 |
| White | -2..4 | 100 | 100 | yes | 0 |
| Purple | 5..9 | 125, 135, 145, 155, 165 | 100 | yes | -5 |
| Red | 10..200 | 175 | 100 | yes | -15 |

Other fields: `deathExperienceLossPercent` 5; `noDeathLossFromPrimeLevel` 33 (unread, H09); `tagTimeoutSeconds` 10; `squadCarryLevels` absent from the YAML, so the code default 6 (`:47`).

## 3. Consumers of `HostileNet` and `IHostile` (`graphify affected`, code nodes only)

| System | File:line | What it reads |
| --- | --- | --- |
| Core | `Core/Runtime/IHostile.cs:7` | the empty marker itself |
| Progression | `Progression/Runtime/CombatEntity.cs:391` | `IsStrikeDummy` = no PlayerResources and no `IHostile` |
| Networking | `Networking/Runtime/NgoNetSession.cs:74,84,93,103` | `IHostile` in children -> `TargetKind.Hostile` |
| Pets | `Pets/Runtime/PlayerPet.cs:515` | skips `IHostile` victims in `OnAnyKilled` (credit via `ServerCreditHostileKill`, from HostileLoot `:43`) |
| Mounts | `Mounts/Runtime/PlayerMounts.cs:112,119,283-287` | `HostileKillRewards.Rewarded` -> `kill.Credited` |
| Hostiles (own) | `HostileBrainLink.cs:23,37`; `HostileKillRewards.cs:12,46-47,52,134,167`; `HostilePool.cs:14-123`; `HostileSpawner.cs:28-80`; `HostileZone.cs:27-317` (`Active.Count` `:174`); `HostileLoot.cs:38-71` | Report*, `AnyKilled`, TagHolder, Stats, Level, LevelGap, Settings, Curve, Definition, Killed, Zone, Brain, Entity |
| Player Entities (DOTS) | `Combat/Entities/Runtime/HostileStrikeSystems.cs:47`; `Persistence/Entities/Tests/PlayMode/RealPrefabSmokeTests.cs:4099,4685` | stand-in attacker must carry HostileNet with LevelGap (XP loss); tests find the type by name |
| Autopilot | `Autopilot/Runtime/AutopilotController.cs:48-49,224,272-298` | `Active`, `Entity.IsAlive`, position, `Definition`, `IsTaggedByAnother(networkObject)`, `LevelGap`, `Level` |
| UI | `UI/Runtime/HostileNameplateController.cs:47,54-55,114-118,147,170,182,237-273` | `Active`, `Entity` (health, name), `Definition` (name, tier), `Level`, `LevelGap` (name colour), `IsTaggedByAnother(viewer)` |
| UI | `UI/Runtime/Minimap/MinimapController.cs:58,368-372,388-399` | `Active`, `isActiveAndEnabled`, `Entity.IsAlive`, `Definition.tier`, position |
| GameFeel | `GameFeel/Runtime/SpiritReleaser.cs:54-55` | `GetComponentInParent<HostileNet>().Definition.spirit` |
| Tools | `Tools/Debug/HostileCommands.cs:40-468`; `Tools/Debug/BenchCommands.cs:463`; `Tools/Editor/GM/GmToolsWindow.cs:1848` | `Active`, `HostileSpawner.Species/Find/Spawn/Remove`, Brain, Stats, Zone, GmSpawned; Bench skips Hostiles; GM window lists species |
| Editor tools | `UI/Editor/Guide/GuideClipStaging.cs:305,500,1149-1193`; `Hostiles/Editor/HostileAssetBuilder.cs:155-158,536`; `HostileZoneBuilder.cs:80-82` | `FindObjectsByType<HostileNet>`; build-time only |

Events: `AnyKilled` (static) is used by KillRewards only; `Killed` by HostileZone only; `LevelChanged`, `TagChanged`, `AttackStarted` have **no outside subscriber**.

## 4. M4.9 queue contract (`Combat/Entities/Runtime/HostileStrikeSystems.cs`)

| Piece | Lines | What the Hostile attacker side must provide / what happens |
| --- | --- | --- |
| `HostileStrike : IBufferElementData` | `:18-29` | `Entity Target` (player ghost in the **server** world), `DamageResult Result` (already rolled: H06-H08 done by the attacker), `float3 From` (attacker position, gives the blow's direction), `int Attacker` (`HostileAttackers` key, 0 = none, then no XP loss), `bool Heavy` |
| Queue entity | `:32`, created `:134-138` | one `HostileStrikeQueue` entity with the buffer, made by `HostileStrikeServerSystem.OnCreate`; client worlds have none |
| `HostileStrikes.Queue(World server, in HostileStrike)` | `:72-80` | appends; false if no queue (client world, test) |
| `HostileAttackers.Set(int key, CharacterCombatStats)` | `:49-65` | stand-in object per key carrying the Hostile's `CharacterCombatStats` **and a `HostileNet` with `LevelGap`** (needed by `HostileKillRewards.OnAnyKilled :167-168` for the 5 % loss). `null` removes. Static, reset on SubsystemRegistration |
| Drain: `HostileStrikeServerSystem` | `:123-160` | ServerSimulation, SimulationSystemGroup (no explicit order). Each update: copies and clears the buffer; finds the `GhostBodyEntry` of `Target` in this world; `HostileStrikes.Apply` |
| `Apply` | `:86-94` | refuses dead targets and `ClientView` bodies; moves the stand-in to `From`; calls `target.TakeDamage(Result, standIn)` (shield, channel break, killing blow, death as NGO) |
| What lands on the player | `:158`, `:96-101`, `:169-192` | `HostileStrikeRpc { DamageMilli = round(dmg x 1000), Flags crit 1 / evaded 2 / reflected 4 / heavy 8 }` sent reliably to that player only (`HitNumbers.SendTo`); `HostileStrikeClientSystem` shows it on the own body (`ShowCombatFeedback`). The comic hit at the Hostile is left to the Hostile ghost (M5) |
| Counters | `:130-132`, `:174` | `Applied`, `Dropped`, `Sent`, `Received` (tests: `Combat/Entities/Tests/Editor/HostileStrikeTests.cs:25,59,72`; smoke B54 `RealPrefabSmokeTests.cs:4685-4754`, key 54) |

## 5. Seam check: is `HostileSpawner.HasAuthority` true on a DOTS client?

**Yes, by code.** `HostileSpawner.cs:18-25`: `manager == null || !manager.IsListening || manager.IsServer`. On DOTS, NGO must be off (`Persistence/Entities/Runtime/KnPlayHere.cs:1258-1263` logs an error if it listens), so the middle term is true on every DOTS process (client, server, Editor with both worlds).
`HostileNet` has its own copies that are also true for these bodies: `HasAuthority => !IsSpawned || IsServer` (`HostileNet.cs:108`) and `Offline` (`:138-145`).

Where offline NGO Hostiles appear and run on a DOTS client because of it:

| # | What runs | File:line |
| --- | --- | --- |
| 1 | Zone01 holds 8 `HostileZone`s (also 1 in `Scenes/Development/Test_Cadence.unity`) | `Scenes/World/Towns/Zone01_StarterTown.unity` (8 script refs) |
| 2 | `HostileZone.Update` passes the authority gate and calls `Begin` | `HostileZone.cs:104,112` |
| 3 | `Begin` prewarms the pool: `maxAlive` (+1 Bruiser) hidden bodies per zone (Awake/OnEnable run, then deactivated) | `HostileZone.cs:118-129`; `HostilePool.cs:87-100` |
| 4 | `Tick` every 0.5 s; "players" = `PlayerInventory.Local` because NGO is not listening; on a DOTS client that is the own ClientView body (D2) | `HostileZone.cs:131-154,245-257`; `Items/Runtime/PlayerInventory.cs:134-135` |
| 5 | Within 70 m of a zone it wakes and fills: `HostileSpawner.Spawn` -> `HostilePool.Take` (no `NetworkObject.Spawn`, NGO off) | `HostileZone.cs:145-150,172-184`; `HostileSpawner.cs:60-71` |
| 6 | `HostileNet.Start` sees `!IsSpawned && Offline` and wakes with authority: numbers, health, `brain.SetAwake(true)` -> Emerald on | `HostileNet.cs:147-151,205-216,283-296`; `HostileBrainLink.cs:64-80,90-119` |
| 7 | They join `HostileNet.Active`, so nameplates, minimap markers, Autopilot targets and GM tools show local-only Hostiles | section 3 |
| 8 | Emerald chases the own view (it keeps `KNEmeraldPlayerBridge`, `PlayerRoleTable.cs:76`, ViewLocal keeps rules `:135-143`) and strikes: `Strike` -> `CombatEntity.TakeDamage` on the view (no ClientView guard there, `CombatEntity.cs:251-253`), local only | `KNEmeraldPlayerBridge.cs:41-46`; `HostileBrainLink.cs:237-260` |
| 9 | Threat notes, leash, respawn timers, corpse removal, `AnyKilled` -> rewards/loot all run locally | `HostileNet.cs:432-434,265-281`; `HostileZone.cs:153,186-201`; `HostileKillRewards.cs:52`; `HostileLoot.cs:38` |
| 10 | A GM `gm.hostile.spawn` run in that process spawns locally too | `HostileCommands.cs:119` |

On a DOTS **dedicated server** the gate is also open, `Begin` prewarms, but `Players()` most likely finds no `PlayerInventory.Local` (only an offline avatar or the local ClientView claims it), so zones stay asleep and spawn nothing (by reading, not run). `HostileSpawner.Species()` is empty there when the scene has no NetworkManager (`:31-32`).

## 6. The three NGO seams (plan 2.3): exact code they wrap

### 6.1 `HostileSpawner.AuthorityOverride`
- Wraps `HostileSpawner.HasAuthority` getter, `Hostiles/Runtime/HostileSpawner.cs:17-25`.
- Every caller (graphify + grep): `HostileSpawner.Spawn :62`, `HostileSpawner.Remove :77`; `HostileZone.Update :104` (no authority -> `Forget()`), `Clear :275`, `FillNow :293`, `Empty :308`, `SpawnBruiserNow :350`. Indirect: `HostileNet.LeaveAfterCorpse :280` and `HostileCommands :119,141` through Spawn/Remove.
- Not covered by it: `HostileNet.HasAuthority :108`, `HostileNet.Offline :138-145`, `Start :150`, `OnEnable :126` (they gate on NGO alone; harmless once no zone spawns on clients, but they matter for 6.3).

### 6.2 `HostileStrikeRules.TryRoll`
- Wraps `HostileBrainLink.Strike(CombatEntity target)`, `Hostiles/Emerald/Runtime/HostileBrainLink.cs:237-260`, plus `WallReachSlack`, `EyeHeight`, `SightHits` `:262-264`, `OnAWall` `:267`, `ReachesOnWall` `:269-286`.
- Members read: `awake`, `entity` (`IsAlive`, `CombatStats`), `definition` (`reachAbove`, `attackRange`, `attacks[i].damageMultiplier`), `system.CombatComponent.CurrentAnimationIndex` (Emerald: becomes the `attackIndex` argument), `transform` (position, and `IsChildOf` to skip the attacker's own colliders in the wall ray: a `Vector3 attackerPos` alone cannot do that, so pass the attacker `Transform` or a root), `target.transform` (position, up), `target.GetComponent<CharacterBuffManager>().HasReflection`, `CombatCalculator.CalculateAttack`.
- Stays in Strike after the roll: `target.TakeDamage(result, mine)` `:258`, `net.ReportStrike(target, result, index)` `:259`.
- Only caller: `KNEmeraldPlayerBridge.DamageCharacterController` `Hostiles/Emerald/Runtime/KNEmeraldPlayerBridge.cs:41-46` (Emerald's hit callback on the player).

### 6.3 `HostileNet` view mode (`BindView`, `PlayFromGhost`)
- Authority gates a view must close: `HasAuthority :108`, `Offline :138-145`, `OnEnable` revive `:126`, `Start` wake `:147-151`; `SetLevel :195-203`, `SetAsleep :247`, `SetGmHealth :308`, `ReleaseTag :394`, `OnDamaged :370`, `ReportAttack :478`, `ReportStrike :492`, `ReportHit :558`, `Update` threat/state send `:432-446`.
- NGO client branch to copy for a view: `OnNetworkSpawn` else-part `:168-179` (`animator.fireEvents = false`, `Stand`, level, `ApplyIdentity`, `Play`).
- Playback the ghost drives: `Play(HostileAnimState, previous, joining)` `:453-471` (Speed, Idle, Combat, Stunned, Dead/DeathIndex, rebind on revive); `AttackClientRpc` body `:564-569` (Attack Index + 1, trigger, `ShowAttack`); `ShowAttack :544-553`; `HitClientRpc` body `:575-577`; `ShowStrike :512-541` (comic hit at the Hostile). State struct `Runtime/HostileAnimState.cs`.
- Surfaces consumers read and the view must fill: `Active :69-70` (`OnEnable`/`OnDisable` `:119-136`), `Level :96` (view sets `level` like `OnNetLevelChanged :321-326`), `Definition`, `Entity`, `LevelGap`, `IsTaggedByAnother(NetworkObject) :345-353` (reads `netTag` + NGO `SpawnManager` + `PlayerParty.PartyId`: needs a ghost-side holder id), `HitChanceModifierFor :404-409`, events `LevelChanged :89`, `TagChanged :85`, `AttackStarted :81`.
- Death on a view: `HostileCorpse` listens to `CombatEntity.Died` (`HostileCorpse.cs:53`), so the view's CombatEntity must raise `Died` when the ghost says dead.
