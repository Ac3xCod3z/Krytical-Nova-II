# Krytical Nova — Gear System Spec

One spec for the whole gear layer of Krytical Nova, written to hand to Claude Code. Numbers come from the owner's gear notes first, then Twelve Sky 2 where the notes are silent, then a proposal marked *(proposed)* where neither says. Set bonuses are listed but excluded from the build. Nothing here is gospel: every tunable is meant to land in a ScriptableObject, not in code. Naming: Krytical Nova name first, TS2 name in parentheses; Claude Code uses the Krytical Nova name in code and UI.

## 1. Naming

This table is the gear slice of the Glossary in `01_Systems_Build_List.md`. Reference = Twelve Sky 2 term [12Sky term]; the Krytical Nova name is the one used everywhere below; every code identifier is a single rename if a name changes.

| Krytical Nova | Twelve Sky 2 reference [12Sky] | Owner's notes said | Code identifier |
| --- | --- | --- | --- |
| **Tempering** ("Temper %") | Reinforcement [Item Upgrade] | Reinforcement, enchant | `TemperPercent` |
| **Fusion** (FS1–FS12) | Combination / Combine CS1–12 [Item Combine] | Combine System (CS) | `FusionStage` |
| **Tuning** (Tune 1–25) | Refine | Refine System | `TuneStage` |
| **Slotting** | Item Penetration | Sockets | `Sockets[]`, `SocketCount` |
| **Core** | Diamond (gem) | Diamonds | `CoreId` |
| **Slot Cutter** / **Deep Cutter** | Unril Stone / Plus Penetrate Scroll | Unril Stones, Xcraft scrolls | items |
| **Core Extractor** / **Slot Sealer** | Gem Removal Scroll / Socket Removal Stone | — | items |
| **Alloy I–V** | Upgrade stones 3 / 6 / 9 / 12 / 15% | Reinforcement materials | `AlloyGrade` |
| **Prime Alloy** | Millennial Gold | Millenial Gold | item |
| **Failsafe** / **Advanced Failsafe** | Protection Scroll / APS, Comrade Scroll | APS, protection charm | items |
| **Fusion Catalyst** | Combine Scroll | Combine Scrolls | item |
| **Tuning Chip** / **Violet Chip** | Refining Stone / Purple Stone | Refined Stones | items |
| **Reset Chip** | Chaos Stone | — | item |
| **Mark Stone**; **Epic Mk I / Mk II** | WarLord Upgrade Stone; WarLord 1 / 2 | — | item; `MarkTier` |
| **Epic** | Elite / WarLord | Elite (Warlord / Epic) | `ItemRarity.Epic` |
| **Uncommon** | Unique | Unique | `ItemRarity.Uncommon` |
| **Legendary** (deferred) | Requiem | Requiem | `ItemRarity.Legendary` |
| **Amulet** | Necklace | Necklace | `EquipSlot.Amulet` |
| **Cape** | Cloak / Cape | Cape | `EquipSlot.Cape` |
| **Nova Rank** (the halo is a Nova) | CP Reinforcement bonus levels | cp spent halo progression | `NovaRank` |
| **Titles** (Vanguard, Bulwark, Ironblood, Mindwell) | CP stat Titles L1–L12 | cp spent title progression | `TitleId` |
| **Renown** (RN) | Contribution Points (CP) | CP | `Renown` |
| **Creds** | Silver [Silver Jeon] | Gold, silver | `Creds` |
| **Nova Store** | Item Mall (real money) | — | — |
| **Armorer** | Blacksmith [Forge] | — | `NpcRole.Blacksmith` |
| **Fixer** | Agent [Beggar] | town Agent NPC | `NpcRole.Agent` |
| **Herald** (new) | — (TS2: Elder, "CP RFC") | — | `NpcRole.Herald` |
| **Critical** | Deadly / Deathblow | Deadly Strike, Deathblow | `CriticalRate` |
| **Hit Rate** | Attack Rate | Attack Rate, Hit Rate | `HitRate` |
| **Evasion** | Evade / Dodge | Evade, Dodge | `EvasionRate` |
| **Elemental** | Attribute | Elemental | `ElementalAttack` / `ElementalDefense` |
| **Spirit** (stat); **Flux** (pool, display) | Spirit / Chi / Intelligence; Chi | Spirit; Chi | `StatType.Spirit`; `MaxChi` |
| **Knockback Resist** | Bounce | Bounce | `KnockbackResistancePercent` |
| **Core Resist** | socket attribute resistance | Socket Attribute Resistance | `CoreResistancePercent` |

## 2. Slots, rarities, level bands (TS2: Common / Unique / Rare / Elite / WarLord / Requiem)

**Slots.** Six core slots carry the progression systems: Weapon, Armor, Gloves, Boots, Ring, Amulet. Cape and Pet (`EquipSlot` 7 and 8) are auxiliary: Cape takes Tempering and one Core slot only; Pet takes none (pet growth is its own system, design 04).

**Rarities** (`ItemRarity`, already in the repo):

| Rarity | Owner's notes / TS2 | Level band | Level step | Sets | Role | Rarity multiplier |
| --- | --- | --- | --- | --- | --- | --- |
| Common | Common | 1–30 | every 3 (1, 4, 7 … 28) | 10 | vendor gear, early leveling | 1.00 |
| Uncommon | Unique | 20–40 | every 5 (20, 25, 30, 35, 40) | 5 | mini-boss and dungeon drops | 1.20 |
| Rare | Rare | 45–105 | every 10 (45, 55 … 105) | 7 | endgame boss drops, high-tier crafting | 1.45 |
| Epic | Elite / WarLord | 100–112 | every 3 (100, 103, 106, 109, 112) | 5 | endgame sets (Mk I, Mk II) | 1.70 |
| Legendary | Requiem | 112+ | *(proposed)* one set | 1 | Reboot 12+, deferred | 2.00 *(proposed)* |

**Looks, groups and the level cap (owner, 4 Oct 2026; as built).** Gear of one rarity shares looks across its level tiers, so a new tier is new stats and a new level on the same piece:

| Band | Item levels | Tiers | Looks |
| --- | --- | --- | --- |
| Common | 1, 4, 7 … 28 | 10 | one per group; outfits use the group's variant 01, the basic outfit a new character already wears |
| Uncommon | 20, 25, 30, 35, 40 | 5 | one per group, a little more detailed |
| Rare I | 45, 55, 65, 75 | 4 (40 levels) | first Rare look |
| Rare II | 85, 95, 105 | 3 (30 levels) | second Rare look |
| Epic | 100, 103, 106, 109, 112 | 5 | hero look |

- **Groups.** Armor, Gloves, Boots, Cape, Ring and Amulet each have a line set per group: SOLD and CIVL (Hegemony), ROBO and SKTN (Null), VIKG and ELVN (Covenant), so skeletons, robots, soldiers… each have their own drops. Weapons have a line set per weapon class (nine; `04_Weapon_Classes.md`) and are locked to that class's faction.
- **Player level cap: 112**, so the top Epic tier is the cap gear. A piece can only be equipped at or above its item level.
- **Frame colours:** Common white, Uncommon blue, Rare yellow, Epic purple (Legendary, reserved, orange). They are the same in every faction skin.
- **Legendary** stays in the enum; nothing of that rarity is generated.
- **Rank up / down** (12Sky page 26, future): the next tier of a piece is the next item of the same line (`GearDictionary.TryGetNextTier`); crossing from Rare I to Rare II or into another rarity is a design decision for that feature.
**Which systems apply per rarity:**

| Rarity | Tempering | Fusion | Tuning | Slotting | Mark upgrade |
| --- | --- | --- | --- | --- | --- |
| Common | yes | no | no | no | no |
| Uncommon | yes | no | no | up to 1 slot | no |
| Rare | yes | yes | yes | up to 3 slots | no |
| Epic | yes | yes | yes | up to 5 slots | Mk I → Mk II |
| Legendary | yes | yes | own Tuning table | up to 5 slots | no |

**Set bonuses** (Epic 4-piece: +60% Damage / Defense / Evasion / Hit Rate, +5% Critical; Epic 6-piece: +100% of those, +100% Elemental, +5% Crit Rate / Def, +5% Luck) are **not in this build**; `ItemDefinition.setId` is reserved so they can be added later without a data migration.

## 3. Base stats and the per-slot ladder

**Formula** (from the owner's notes), evaluated once when an `ItemDefinition` is baked, never at runtime:

```
BaseStat = ItemLevel × SlotSlope × RarityMultiplier
```

| Slot slope | Value per level | Applies to |
| --- | --- | --- |
| Weapon damage (Attack Power) | 5.2 | Weapon |
| Armor defense (Defense Power) | 2.1 | Armor |
| Secondary defense | 0.55 | Gloves, Boots, Ring, Amulet, Cape |
| Evasion | 0.35 | Armor, Boots |
| Hit Rate | 2.5 | Weapon; Gloves at 40% of weapon |

Check against the Epic table: level 100 × 5.2 × 1.70 = 884 damage, level 100 × 2.1 × 1.70 = 357 defense, level 100 × 0.55 × 1.70 = 94 secondary defense. All three match the notes, so the slopes are confirmed.

**Which stats each rarity introduces** ("Skill modifier" = +N levels to one named skill line):

| Stat group | Common | Uncommon | Rare | Epic |
| --- | --- | --- | --- | --- |
| Primary (Damage, Hit Rate, Defense) | from level 1 | scaled 1.2 | scaled 1.45 | scaled 1.70 |
| Evasion (Armor, Boots) | from level 5, +1 | yes | yes | yes |
| Elemental Attack / Defense | no | 1 flat elemental stat | higher flat | +120–150 atk / +50–60 def |
| Health bonus (Armor) | no | flat on Armor | much higher | +80–100 |
| Attributes on accessories (Agility, Spirit) | no | +5 | +11–14 | +24–28 |
| Skill modifiers | no | 1 line, +1 | 1–2 lines, +2–+4 | +4–+5 |
| Utility % (Crit Damage %, Kill-XP %) | no | +13% crit dmg, +40% kill-XP on accessories | higher | +18–20% crit dmg, +50–60% XP rate, +100–120% XP amount |

**Secondary-stat milestones by level:** Common L1 primaries only; Common L5+ Evasion +1 on Armor / Boots. Uncommon L15–20 adds one Elemental stat, one skill modifier (+1), Health on Armor, +5 Agility / Spirit and first utility % on accessories; L25–35 raises those and skill modifiers to +2 / +3. Rare L45 roughly +32 Health, +11 Agility, +67 Elemental Damage, accessories gain kill-XP rate and amount %; L55 skill modifiers +3 / +4, attributes +13–14, +100% kill-XP amount.

**Epic tables, levels 100–112** (owner's notes, verbatim; these are the acceptance values for the baker):

| Weapon | Damage | Hit Rate | Elemental Dmg | Skill mod |
| --- | --- | --- | --- | --- |
| 100 | 884 | 250 | +120 | +4 |
| 103 | 910 | 258 | +128 | +4 |
| 106 | 937 | 265 | +135 | +4 |
| 109 | 963 | 273 | +143 | +5 |
| 112 | 990 | 280 | +150 | +5 |

| Armor | Defense | Evasion | Health | Elemental Def |
| --- | --- | --- | --- | --- |
| 100 | 357 | 35 | +80 | +50 |
| 103 | 368 | 36 | +85 | +52 |
| 106 | 378 | 37 | +90 | +55 |
| 109 | 389 | 38 | +95 | +58 |
| 112 | 400 | 39 | +100 | +60 |

| Gloves | Defense | Hit Rate | Elemental Dmg | Skill mod |
| --- | --- | --- | --- | --- |
| 100 | 94 | 100 | +30 | +4 |
| 103 | 97 | 105 | +32 | +4 |
| 106 | 100 | 110 | +35 | +4 |
| 109 | 102 | 115 | +37 | +5 |
| 112 | 105 | 120 | +40 | +5 |

| Boots | Defense | Evasion | Skill mod |
| --- | --- | --- | --- |
| 100 | 94 | 35 | +4 |
| 103 | 97 | 36 | +4 |
| 106 | 100 | 37 | +4 |
| 109 | 102 | 38 | +5 |
| 112 | 105 | 39 | +5 |

| Ring | Agility | Elemental Dmg | Crit Damage % | Skill mod |
| --- | --- | --- | --- | --- |
| 100 | +24 | +100 | +18% | +4 |
| 103 | +25 | +106 | +18% | +4 |
| 106 | +26 | +112 | +19% | +4 |
| 109 | +27 | +118 | +19% | +5 |
| 112 | +28 | +125 | +20% | +5 |

| Amulet | Spirit | Elemental Def | Kill-XP rate % | Kill-XP amount % |
| --- | --- | --- | --- | --- |
| 100 | +24 | +75 | +50% | +100% |
| 103 | +25 | +78 | +52% | +105% |
| 106 | +26 | +82 | +54% | +110% |
| 109 | +27 | +86 | +56% | +115% |
| 112 | +28 | +90 | +60% | +120% |

Boots also carry +30–40 move / utility speed in the notes; keep that as `MoveSpeedPercent` on the Boots row of the ladder table rather than a separate stat.

## 4. Tempering (TS2: Reinforcement / Enchant %)

Tempering scales an item's **primary** base stat (Attack Power on Weapon, Defense Power on everything else) by `1 + TemperPercent / 100`, applied before Fusion and Tuning bonuses. Done at the **Armorer**.

**Materials.** Five Alloy grades (TS2: 3 / 6 / 9 / 12 / 15% upgrade stones), each worth a fixed step; higher grades are rarer and have a lower success chance *(proposed, TS2 lists the steps but not the odds)*:

| Alloy | Step | Success chance | Source |
| --- | --- | --- | --- |
| Alloy I | +3% | 90% | vendor, common drop |
| Alloy II | +6% | 75% | drop |
| Alloy III | +9% | 60% | drop, craft (4 × Alloy I) |
| Alloy IV | +12% | 45% | boss drop, craft |
| Alloy V | +15% | 30% | boss drop, craft, Mall |
| Prime Alloy | +3% | 50% | required for every attempt above 120% |

**Outcome rules** (12Sky page 23 + TS2 Classic + owner's notes):

| Temper band | On success | On fail | Break chance on fail | Glow |
| --- | --- | --- | --- | --- |
| 0–59% | + step | −3% (floor 0) | 0% | yellow (1–59) |
| 60–89% | + step | −3% | 5% → 15% (linear) | blue |
| 90–119% | + step | −3% | 15% → 30% | purple |
| 120% | cap without Prime Alloy | — | — | orange |
| 121–132% | + step (Prime Alloy only) | −3% | 35% | orange + ring |
| 133–150% | + step (Prime Alloy + Advanced Failsafe only) | −3% | 50% | red |

- **Failsafe** consumed on the attempt: a fail still costs −3% but the item never breaks.
- **Advanced Failsafe**: fail costs nothing and the item never breaks. Required above 132%.
- A **break** destroys the item (no recovery). Fused or tuned items break the same way, so the UI warns when an item above FS0 / Tune 0 is being tempered without a Failsafe.
- Creds cost per attempt *(proposed)*: `ItemLevel × 500 × RarityMultiplier`, doubled above 120%.
- Thresholds other systems read: **12%** unlocks Fusion as a material and Exchange listing (Broker NPC text already says this); **132%** unlocks Fusion on Epic (owner's notes); **150%** is the hard cap.
- Server rolls every outcome; the client only sends `TemperRequest(itemLocation, alloyLocation, failsafeLocation?)`.

## 5. Fusion FS1–FS12 (TS2: Combination / Combine CS1–CS12)

Fusion (TS2: Combine) melds a **material item** into a **target item** of the same slot, rarity and item level, raising the target's Fusion Stage by one. Twelve stages. Done at the Armorer. The current stage shows under the item name ("FS7").

**Requirements** (12Sky page 24, TS2 upgrading guide, owner's notes):

- Target: Rare or Epic, Temper ≥ 12% (Rare) or ≥ 132% (Epic).
- Material: same slot, rarity and level; **Temper 0%** and **FS0** (one source must be clean). The material is consumed.
- One **Fusion Catalyst** per attempt (drop, craft, Mall).
- Creds: 100,000 for FS1, +50,000 per stage (FS12 = 650,000).

**Outcome:** success chance *(proposed)* 100% for FS1–FS3, then −7% per stage (FS12 = 37%). A fail consumes the material, catalyst and Creds; **the target is never destroyed and never loses a stage**.

**Effect per stage.** Each stage adds a flat bonus scaled from the item's own base stat, so the same table works at every level:

| Slot | Per stage | At FS12 |
| --- | --- | --- |
| Weapon | +3% base Attack Power, +2% base Hit Rate | +36% AP, +24% Hit |
| Armor | +3% base Defense, +2% base Evasion, +1.5% Max HP | +36% DP, +24% Eva, +18% HP |
| Gloves | +3% base Defense, +2% base Hit Rate | +36% DP, +24% Hit |
| Boots | +3% base Defense, +2% base Evasion | +36% DP, +24% Eva |
| Cape | +3% base Defense | +36% DP |
| Ring | +8 Elemental Attack × RarityMultiplier | +96 × mult |
| Amulet | +8 Elemental Defense × RarityMultiplier | +96 × mult |

The note "+20,000 HP for a full purple set at CS12" is an Epic set bonus, which is excluded; the Armor HP line above is the per-item share of that idea and is a tunable.

**Interaction rules:** Fusion stage is kept when the item is tempered further or tuned. A fused item cannot be used as fusion material. Fused items cannot be listed on the Exchange *(proposed, to keep FS12 items earned)*.

## 6. Tuning, stages 1–25 (TS2: Refine)

Tuning (TS2: Refine) adds **secondary** stats by slot, one stage at a time, up to stage 25. Rare and Epic only. Done at the Armorer (TS2 Reborn uses a dedicated Refine NPC; one NPC fewer here). Costs **Tuning Chips** and **Renown**.

**Per-slot gains** (owner's notes, verbatim):

| Slot | Per stage | Stage 25 bonus |
| --- | --- | --- |
| Weapon | Critical Rate +1%, Spirit +5 | Spirit +5 |
| Armor | Max HP +400, Spirit +5 | Spirit +5 |
| Gloves | Attack Speed +0.5%, Agility +5 | Agility +5 |
| Boots | Move Speed +0.5%, Agility +5 | Agility +5 |
| Ring | Core Resistance +0.1%, Vitality +5 | Vitality +5 |
| Amulet | Bounce +1%, Strength +5 | Strength +5 |

Three of those stats are new to `StatType` (see section 11): **Attack Speed**, **Core Resistance** (the "socket attribute resistance": reduces the elemental damage an enemy's Cores add) and **Bounce** (TS2's term; treated as **Knockback Resistance %** unless the owner means something else: flag).

**Materials and cost per attempt** *(proposed; the notes give the materials, not the amounts)*:

| Stages | Chips | Renown | Creds |
| --- | --- | --- | --- |
| 1–12 | 1 Tuning Chip | 10 | 50,000 |
| 13–20 | 1 Violet Chip | 25 | 150,000 |
| 21–25 | 2 Violet Chips | 50 | 400,000 |

**Outcome** *(proposed)*: success 100% to stage 10, then −5% per stage (stage 25 = 25%). A fail consumes materials; stages 1–12 never drop, stages 13+ drop by one on a fail. A **Reset Chip** returns the item to stage 0 and refunds nothing (needed so Legendary's randomized tuning, deferred, has a reset path).

Tuning stacks additively with the item's own secondary stats and with Fusion.

## 7. Slotting and Cores (TS2: Item Penetration, sockets and Diamonds)

Slotting (TS2: Item Penetration) cuts up to five sockets into an item; each socket holds one **Core** that adds a flat stat. Done at the Fixer (Renown vendor; code role Agent), which matches that NPC's existing Zone01 text ("buy penetration materials with CP to add socket slots").

**Sockets** (TS2 Item Penetration + owner's notes):

| Action | Item | Cost | Result |
| --- | --- | --- | --- |
| First cut | Slot Cutter | 1 Cutter + Creds 1,000,000 × RarityMultiplier (TS2 charges 100M flat; too high for this economy) | random 1–3 sockets (Rare), 1–5 (Epic); never fewer than 1 |
| Extra socket | Deep Cutter | 1 Deep Cutter (Renown 200) | +1 socket, chance 60% for the 2nd and 3rd, 15% for the 4th, 5% for the 5th; existing Cores stay |
| Remove a Core | Core Extractor | Renown 50 | Core returned to bag |
| Seal a socket | Slot Sealer | Creds 500,000 | removes the last empty socket; a socket holding a Core cannot be sealed |

Rarity caps: Uncommon 1, Rare 3, Epic 5, Legendary 5, Cape 1.

**Cores** (TS2 Diamonds, "up to +400 attribute attack / defense or +400 HP per socket"):

| Core | Grade I | Grade II | Grade III | Grade IV (max) |
| --- | --- | --- | --- | --- |
| Ember Core (Elemental Attack) | +50 | +120 | +250 | +400 |
| Ward Core (Elemental Defense) | +50 | +120 | +250 | +400 |
| Vital Core (Max HP) | +50 | +120 | +250 | +400 |
| Edge Core (Critical Rate) | +0.5% | +1% | +2% | +3% |
| Pulse Core (Flux) | +50 | +120 | +250 | +400 |

Cores drop from PvP, bosses and boxes; three of a grade craft one of the next grade at the Fixer *(proposed, mirrors TS2 pets)*. A Core's value is fixed by grade; the "roll ideal attributes" from the notes becomes choosing which Core to seat.

**Code shape:** `ItemStack.sockets` is a fixed `byte[5]` of Core item ids (0 = empty), `ItemStack.socketCount` 0–5. Stats from Cores are summed into the item's modifier list by `GearStatResolver` like every other bonus.

## 8. Epic Mk I → Mk II (TS2: WarLord 1 → 2) and Legendary (TS2: Requiem)

**Epic Mk I → Mk II** (TS2 WarLord 1 → 2): at the Armorer, one **Mark Stone** plus Creds 5,000,000 *(proposed)* converts an Epic item to its Mk II version of the same slot, level and set. Mk II is a second `ItemDefinition` with base stats × 1.15 and one extra skill modifier; Temper %, Fusion stage, Tuning stage and Cores carry over. No fail state: the stone is the gate, not the roll.

**Legendary** (TS2 Requiem) is reserved, not built: Reboot 12+ (TS2 Rebirth), six-piece set, own Tuning table with up to five randomized results and Reset Chip, mixes with Epic. The only thing to do now is keep `ItemRarity.Legendary` in the enum and the multiplier slot in the stat table.

## 9. Nova Rank (TS2: CP Reinforcement halo) and Titles (TS2: CP stat Titles), at the Herald

Both are Renown-bought **character** progression, not gear, and both live on one new NPC: the **Herald** (`NpcRole.Herald`), who also hosts the Bounty Board later. The Nova Store stays real-money only (Store Clerk), and the Fixer stays Renown-for-materials.

**Nova Rank** (TS2 "CP Reinforcement", whose visual tiers are the halos):

| Rule | Value |
| --- | --- |
| Cost per attempt | 100 Renown + 1,000,000 Creds (TS2: 100 CP + 1M Silver); *(proposed)* scale Creds as `250,000 × (1 + rank / 10)` so rank 1 is cheap and rank 60 is ~1.75M |
| Outcomes | success: rank +1 · fail: rank +0 · slip: rank −1 (TS2 lists all three; *(proposed)* 55 / 35 / 10% to rank 24, 45 / 40 / 15% after) |
| Effect per rank | +1 Strength, +1 Agility, +1 Vitality, +1 Spirit (as a `StatModifier` with source `NovaRank`, so derived stats follow automatically) |
| Max rank | 96 *(proposed; TS2 shows tiers to 89+)* |
| Nova (halo) visual | one tier per 8 ranks: 1–8, 9–16, 17–24, 25–32, 33–40, 41–48, 49–56, 57–64, 65–72, 73–80, 81–88, 89+ → 12 Nova prefabs in `Assets/_Game/Gameplay/Progression/Data/Assets/Nova/`, spawned by `ActorVfx` from `ProgressionState.novaRank` |
| Persistence | `ProgressionState.novaRank` (byte), server authoritative |

**Titles** (TS2 Classic CP stat titles L1–L12; 800 CP at L1 to 10,000 at L12):

| Level | Renown (interpolated between TS2's two CP endpoints) | Bonus per track |
| --- | --- | --- |
| 1 | 800 | +1% |
| 2 | 1,200 | +2% |
| 3 | 1,600 | +3% |
| 4 | 2,200 | +4% |
| 5 | 2,900 | +5% |
| 6 | 3,700 | +6% |
| 7 | 4,600 | +7% |
| 8 | 5,600 | +8% |
| 9 | 6,800 | +9% |
| 10 | 8,000 | +10% |
| 11 | 9,000 | +11% |
| 12 | 10,000 | +12% |

Four tracks, matching TS2's STR / DEF / HP / CHI titles: **Vanguard** (Attack Power %), **Bulwark** (Defense Power %), **Ironblood** (Max HP %), **Mindwell** (Max Flux %). Levels are bought in order; the player equips one Title at a time (`ProgressionState.activeTitle`) and it shows in front of the name on nameplates and in chat. Levels never slip. The **bounty** half of the owner's note is a separate Renown source, not a Title mechanic: Bounty Board kills pay Renown that funds these levels; its design is still open.

## 10. Auxiliary slots: Cape (TS2: Cloak), Pet, Mount, Wings

These cost more Creds than armor in TS2 and are where min-maxing lives; only the Cape is in this gear build.

- **Cape** (`EquipSlot.Cape`): Tempering and one Core slot; base Defense via the secondary slope; ladder adds **Critical Defense %** at Rare (TS2 Tier 2 Eternal Capes carry ~28%). Critical Defense is a new `StatType` (reduces incoming crit damage). Cape bands: Common "Chi cape" L1–30, Rare L45–105, Epic L100–112.
- **Pet** (`EquipSlot.Pet`, cosmetic follower exists): TS2 pets add flat Damage / Defense / HP / Chi scaled by a growth % (100–200%), with +50 damage per 25% over 100%; 3 × level-1 pets combine into one M1, 3 × M1 into one M15. Deferred to design 04; the gear build only reserves `ItemDefinition.petGrowthPercent`.
- **Mount**: no slot yet. TS2 tiers 5% → 10% → 15% (each crafted from a maxed lower tier) adding % HP, Damage, Elemental, Chi, Defense, Evasion; the notes cite +17–20% HP and Damage at the top. Deferred to design 05; add `EquipSlot.Mount = 9` now so the enum does not shift later.
- **Wings** (TS2 Classic): crafted from feathers + 50–100 Renown at the Outfitter (TS2 Tailor), tempered to 120% with their own protection item, stat options re-rolled with Arch Wingstones. Not in the notes; listed only as a possible cosmetic-plus-stat slot for the Outfitter.

## 11. Data model and code changes (for Claude Code)

**As built (4 Oct 2026): steps 1 and 2 of section 12.**

- `GearDictionary` (`Gameplay/Items/Runtime`) holds two tables. **`lines`** is the hand-edited one: a line is one run of level tiers that share a look (`lineId`, `tag`, `slot`, `rarity`, `band`, `pack`, `factions`, `bodies`, `weaponClass`, `look`, `displayName`). **`entries`** is baked: one row per item, read by the equip rule, the GM commands and the starter kit.
- Item ID = `10000 + (lineId − 1) × 16 + tier index`. Line IDs are append-only, so item IDs never change. Legacy IDs below 10000 are retired (look templates, pets and Stims keep theirs).
- A **look template** is an `ItemDefinition` that is not in the catalog (the per-variant outfit pieces, one asset per weapon mesh, the ring / amulet icons). `GearStatBaker` copies its parts, icon and weapon model onto every tier of the lines that wear it. To change a look, change the line's `look`.
- `GearStatTable` (`Items/Data/Assets/GearStatTable.asset`) holds the bands, rarity multipliers, slopes, the secondary ladder and exact rows. The owner's Epic 100–112 tables are exact rows, word for word, because they do not follow one rounding rule; `GearLadderTests` asserts every cell. Uncommon and Rare secondaries are *(proposed)* values from the milestones in section 3.
- `ItemDefinition` gained `itemLevel`, `lineId`, `weaponClass`, `baseStats` (primary: what Tempering scales), `secondaryStats`, `skillModifiers`. Not added yet: `maxSockets`, `setId`, `markTier`, `mkIIDefinition`, `fusionEligible`, `tuneEligible` (they arrive with their systems).
- `ItemStack` and `EquipmentLoadout` are unchanged (item ID only on the wire). When Tempering lands, `ItemStack` grows its instance fields and the loadout carries stacks; `GearStatResolver.Resolve(stack, definition, …)` already takes the stack, so callers do not change.
- `StatType` gained `AttackSpeedPercent`, `CoreResistancePercent`, `KnockbackResistancePercent`, `CriticalDefensePercent`, `KillExpRatePercent`, `KillExpAmountPercent`. Skill modifiers are `ItemDefinition.skillModifiers` (class + levels), not a stat type. Critical Defense is applied in `CombatCalculator`; skill modifiers and the Kill-XP stats are stored and shown only.
- Menus: `Krytical Nova ▸ Items ▸ 1. Generate Gear Looks From Synty` (templates, seeds missing lines), `▸ 6. Bake Gear Ladder` (lines → items, entries, catalog), `▸ 2. Capture Item Icons` (per look), `▸ Rebuild Weapon Look Models`. GM: `gm.gear <slot> <level> <rarity> [class]`, `gm.loadout [class]`.
All paths follow `ProjectOrganization.md`: gear code in `Assets/_Game/Gameplay/Items/`, stat code in `Assets/_Game/Gameplay/Progression/`, NPC services in `Assets/_Game/Gameplay/NPC/`, UI in `Assets/_Game/UI/`. Namespaces `KryticalNova.Items`, `KryticalNova.Progression`, `KryticalNova.NPC`.

**`StatType` additions** (`Gameplay/Progression/Data/StatType.cs`): `AttackSpeedPercent`, `CoreResistancePercent`, `KnockbackResistancePercent` (the "Bounce"), `CriticalDefensePercent`, `KillExpRatePercent`, `KillExpAmountPercent`, `SkillLineModifier` (carries a skill-line id + level in its modifier). Each needs a `StatDefinitionData` asset and a row in the character sheet.

**`ItemDefinition`** (`Gameplay/Items/Runtime/ItemDefinition.cs`), new fields:

| Field | Type | Notes |
| --- | --- | --- |
| `itemLevel` | int | drives the base formula |
| `baseStats` | `StatModifier[]` | baked by `GearStatBaker` from slope × level × rarity (section 3) |
| `secondaryStats` | `StatModifier[]` | baked from the ladder table (section 3) |
| `skillModifiers` | `(skillLineId, levels)[]` | 0–2 entries |
| `maxSockets` | byte | 0 / 1 / 3 / 5 by rarity |
| `setId` | ushort | reserved, unused |
| `markTier` | byte | 1 or 2 (Epic Mk) |
| `mkIIDefinition` | `ItemDefinition` | Epic Mk I points to its Mk II |
| `fusionEligible`, `tuneEligible` | bool | from the rarity matrix |

**`ItemStack`** (`Gameplay/Items/Runtime/ItemNetworkTypes.cs`; it already says "room to grow: enhance level, refine rolls"): add `temperPercent` (byte, 0–150), `fusionStage` (byte, 0–12), `tuneStage` (byte, 0–25), `socketCount` (byte), `sockets` (fixed `byte[5]` Core ids). Keep it an unmanaged struct for NGO `NetworkList`.

**New ScriptableObjects** in `Gameplay/Items/Data/`: `GearStatTable` (slopes, rarity multipliers, ladder rows per rarity band), `TemperTuning` (alloy steps and odds, band table, costs, Failsafe rules), `FusionTuning` (per-slot per-stage effects, odds, costs), `TuneTable` (per-slot per-stage gains, chip costs, odds), `SocketTuning` (cutter odds, caps, Core grades), `NovaRankTuning`, `TitleCatalog`. One editor baker, `GearStatBaker` (`Gameplay/Items/Editor/`), generates every `ItemDefinition` for a rarity band from `GearStatTable` and asserts the Epic 100–112 tables in section 3 as a test (`Gameplay/Items/Tests/Editor/GearStatBakerTests.cs`).

**Runtime:** `GearStatResolver` (static) turns one `ItemStack` + its `ItemDefinition` into the final `StatModifier` list: base × (1 + temper / 100), + fusion, + tuning, + cores, + secondaries, + skill modifiers. `PlayerEquipment` already applies modifiers to `CharacterCombatStats`; it calls the resolver instead of reading the definition directly.

**Services** (server authoritative, one `ServerRpc` each, client gets a result event + updated `ItemStack`): `TemperRequest`, `FuseRequest`, `TuneRequest`, `CutSocketRequest`, `SeatCoreRequest`, `ExtractCoreRequest`, `SealSocketRequest`, `MarkUpgradeRequest`, `NovaRankRequest`, `BuyTitleRequest`, `EquipTitleRequest`. All live on a `GearServices` NetworkBehaviour on the player; the NPC only opens the matching panel when the player is within `interactRange`.

**NPC routing** (`NpcRole`): Armorer (role Blacksmith) → Tempering, Fusion, Tuning, Mark upgrade. Fixer (role Agent) → Slotting, Cores, Renown materials. **Herald (new)** → Nova Rank, Titles, Bounty Board. Store Clerk (role MallManager) → real-money Nova Store only. Add `NpcRole.Herald`, a seed in `Zone01Setup`, and a stall kind in `StarterTownBuilder.Plaza` (the unused `Collector` kind can become the Herald's booth).

**UI** (UI Toolkit, matching `GearUIController`): one `GearServicePanel.uxml` with a tab per service, a two-slot drop target (item + material), a cost line, a result toast. Item tooltips gain lines for Temper %, FS, Tune stage, sockets, and the glow colour from section 4 on the icon frame. Nameplates show the active Title; `ActorVfx` shows the Nova.

**Economy hooks:** `ItemDefinition` for every material (Alloys I–V, Prime Alloy, Failsafe, Advanced Failsafe, Fusion Catalyst, Tuning Chip, Violet Chip, Reset Chip, Slot Cutter, Deep Cutter, Core Extractor, Slot Sealer, Mark Stone, 5 Cores × 4 grades) go in `Gameplay/Items/Data/Assets/Materials/`, generated by a `GearMaterialGenerator` alongside the existing `PillGenerator`. `item.give` already lets you test any of them.

## 12. Build order

Each step compiles and plays on its own; the GM console (`item.give`, `gm.attr`) is the test harness until shops exist.

- [x] **1. Stats and definitions.** *(done 4 Oct 2026: all four rarities baked, not only Common and Epic)* Add the seven `StatType`s and their definition assets; extend `ItemDefinition` and `ItemStack`; write `GearStatTable` and `GearStatBaker`; bake Common 1–30 and Epic 100–112 and pass the Epic acceptance test.
- [x] **2. Resolver.** *(done 4 Oct 2026)* `GearStatResolver` and `PlayerEquipment` reading it; character sheet shows gear-derived values; equip a baked Epic weapon and see 884 AP at 0%.
- [ ] **3. Materials.** `GearMaterialGenerator` creates every material and Core item; `item.give` works for all.
- [ ] **4. Tempering.** `TemperTuning`, `TemperRequest`, Armorer panel tab, tooltip and glow. Test: 0 → 60% never breaks, 90–119% breaks sometimes, Failsafe never breaks, 120% cap without Prime Alloy.
- [ ] **5. Fusion.** `FusionTuning`, `FuseRequest`, material validation (clean item rule), FS label under the name.
- [ ] **6. Tuning.** `TuneTable`, `TuneRequest`, Renown spend path (add `ProgressionState.renown` if it is not there yet).
- [ ] **7. Slotting and Cores.** `SocketTuning`, four socket requests, Fixer panel, Core crafting 3 → 1.
- [ ] **8. Epic Mark.** Mk II definitions from the baker, `MarkUpgradeRequest`, carry-over test.
- [ ] **9. Herald.** `NpcRole.Herald`, Zone01 seed, Nova Rank and Titles with their panels, 12 Nova prefabs, title on nameplates.
- [ ] **10. Cape.** Cape ladder rows and the Critical Defense stat are in (4 Oct 2026); the one socket waits for Slotting.
- [ ] **11. Verify.** `GearStatBakerTests`, a resolver test per system, and a play-mode pass: temper, fuse, tune, slot, mark, nova, title on one Epic set end to end; then flip rows 2–5, 10, 11 of the build list to Complete.

Deferred and out of scope here: set bonuses, Legendary / Requiem, Pets (design 04), Mounts (design 05), Wings, the Bounty Board's Renown rewards, the Nova Store's real-money catalogue.

## Sources

- Owner's gear notes (4 Oct 2026) and `Assets/_Game/_Docs/Design/03_equipment_enhancement.md`, `09_economy_ranks_endgame.md`
- 12Sky help pages 7, 22–27 (`03_Full_Transcription.md`)
- Halos & CP Reinforcement, RedFox TS2 help: https://12sky-help.playredfox.com/en/support/solutions/articles/8000069522-halos-cp-reinforcement
- Item Penetration, RedFox TS2 help: https://12sky-help.playredfox.com/en/support/solutions/articles/8000069287-item-penetration
- Wings Crafting, Stat Options & Upgrading, RedFox TS2 help: https://12sky-help.playredfox.com/en/support/solutions/articles/8000031558-wings-crafting-stat-options-upgrading
- Starting Player Guide, Steam Community (grades, enchant glow bands, break threshold, pets, mounts, CP titles): https://steamcommunity.com/sharedfiles/filedetails/?id=852102476
- Twelve Sky 2 Upgrading Rares Guide, guidescroll: https://guidescroll.com/2011/10/twelve-sky-2-upgrading-rares-guide/
- God 12, Craft and Reinforcement Expansion, Steam announcement: https://steamcommunity.com/games/539650/announcements/detail/1664521145471880050
- TS2 Reborn wiki pages for Refine, WarLord Items, Requiem Items and Title Guide were unreachable (HTTP 402); their numbers come from the owner's notes and design 03 instead.
