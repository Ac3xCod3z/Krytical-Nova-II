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
| **Alumite**, **Iridion**, **Orichalc**, **Eversteel**, **Trinity Platinum** (the Alloys) | Upgrade stones 3 / 6 / 9 / 12 / 15%: Aluminium, Iridium, Oracite, Steel of Eternity, Holy Trinity Platinum | Reinforcement materials (owner's list, 5 Oct 2026) | `AlloyGrade`, `GearMaterialIds` |
| **Prime Alloy** | Millennial Gold | Millenial Gold | item |
| **Failsafe** / **Advanced Failsafe** / **Lucky Failsafe** | Protection Charm / APS, Comrade Scroll / Lucky enchant | APS, protection charm | items |
| **Reforge** | Item Exchange [12Sky: Item Exchange] | gear exchange | `ReforgeSettings` |
| **The forge** | the Blacksmith's upgrade windows | — | `PlayerGearWork`, `NpcOffer.GearWork` |
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

**Slots.** Six core slots carry the progression systems: Weapon, Armor, Gloves, Boots, Ring, Amulet. Cape and Pet (`EquipSlot` 7 and 8) are auxiliary; **a Cape exists from Rare up only** (owner, 6 Oct 2026: basic-tier gear has no cape, so Common and Uncommon have no Cape lines and a new character starts without one): Cape takes Tempering and one Core slot only; Pet takes none (pet growth is its own system, design 04).

**Rarities** (`ItemRarity`, already in the repo):

| Rarity | Owner's notes / TS2 | Level band | Level step | Sets | Role | Rarity multiplier |
| --- | --- | --- | --- | --- | --- | --- |
| Common | Common | 1–30 | every 3 (1, 4, 7 … 28) | 10 | vendor gear, early leveling | 1.00 |
| Uncommon | Unique | 20–40 | every 5 (20, 25, 30, 35, 40) | 5 | mini-boss and dungeon drops | 1.20 |
| Rare | Rare | 45–110 | every 10 (45, 55 … 105), then 110 | 8 | endgame boss drops, high-tier crafting | 1.45 |
| Epic | Elite / WarLord | 100–112 | every 3 (100, 103, 106, 109, 112) | 5 | endgame sets (Mk I, Mk II) | 1.70 |
| Legendary | Requiem | 112+ | *(proposed)* one set | 1 | Reboot 12+, deferred | 2.00 *(proposed)* |

**Looks, groups and the level cap (owner, 4 Oct 2026; as built).** Gear of one rarity shares looks across its level tiers, so a new tier is new stats and a new level on the same piece:

| Band | Item levels | Tiers | Looks |
| --- | --- | --- | --- |
| Common | 1, 4, 7 … 28 | 10 | one per group; outfits use the group's variant 01, the basic outfit a new character starts in (its level-1 starter kit) |
| Uncommon | 20, 25, 30, 35, 40 | 5 | one per group, a little more detailed |
| Rare I | 45, 55, 65, 75 | 4 (40 levels) | first Rare look |
| Rare II | 85, 95, 105, 110 | 4 | second Rare look. The level-110 tier was added on 6 Oct 2026 (owner: "rares last lvl is 110") so the top Rare sits just under the level-112 Epic; it is what an Epic 112 is fused with |
| Epic | 100, 103, 106, 109, 112 | 5 | hero look |

- **Groups.** Armor, Gloves, Boots, Cape, Ring and Amulet each have a line set per group: SOLD and CIVL (Hegemony), ROBO and SKTN (Null), VIKG and ELVN (Covenant), so skeletons, robots, soldiers… each have their own drops. Weapons have a line set per weapon class (nine; `04_Weapon_Classes.md`) and are locked to that class's faction.
- **Player level cap: 112**, so the top Epic tier is the cap gear. A piece can only be equipped at or above its item level.
- **Frame colours:** Common white, Uncommon blue, Rare yellow, Epic purple (Legendary, reserved, orange). They are the same in every faction skin.
- **Legendary** stays in the enum; nothing of that rarity is generated.
- **Set names (as built, 6 Oct 2026).** Every piece of a set carries the set's name: the Epic Elven set is Sunlord Armor, Sunlord Gloves, Sunlord Boots, Sunlord Cloak, Sunlord Leaf Ring, Sunlord Talisman. A set is one group at one band (`GearDictionary.Line.set`, hand-editable like the rest of a line). Weapons keep their own names: a weapon class belongs to a faction, not to one outfit group.

| Group | Common | Uncommon | Rare I | Rare II | Epic |
| --- | --- | --- | --- | --- | --- |
| SOLD | Recruit | Sentinel | Striker | Bastion | Commander |
| CIVL | Commuter | Courier | Analyst | Director | Magnate |
| ROBO | Servitor | Welder | Ravager | Overseer | Prime |
| SKTN | Graveworn | Cryptwarden | Deathsworn | Lichbound | Revenant |
| VIKG | Thrall | Huscarl | Berserker | Jarl | Warlord |
| ELVN | Scout | Wildrunner | Bladesinger | Highguard | Sunlord |

- **Bare body (as built, 9 Oct 2026).** The basic outfit is gear: the level-1 Common starter kit. Under it every lineage has a bare body (`LineageDefinition.bareBody`, twelve parts, torso to feet), and an empty Armor, Gloves or Boots slot shows that body's parts for the slot: small clothes for humans (HUMN_BASE 01) and elves (ELVN_BASE 01), bones for skeletons (SKTN_BASE 01), a bare chassis for robots (SCFI_ROBO 04, since the robot species pack has heads only). It is per lineage, not per faction (the Null has both robots and skeletons), needs no save or network field (it follows the lineage id), and is baked by `CharacterDataBaker` (**Krytical Nova ▸ Characters ▸ Bake Bare Bodies Only** rewrites nothing else). A feminine body keeps its chest wrap. Menu dolls that are shown without gear (the creator, a kiosk's keeper, a character at select that has nothing on record) stand in the basic outfit instead (`CharacterDoll.Apply(..., dressed: true)`).
- **Colour themes (as built, 6 Oct 2026).** Common gear shows the colours the player picked in the creator (it is the basic set). Every set above Common has its own theme (`GearThemeLibrary`, `Items/Data/Assets/GearThemes.asset`: 24 themes named `<GROUP>_<Set>`, each a handful of role colours: primary, secondary, under-layer, accent, four metals, trim, leather, glow). A worn piece paints its own part of the character's colour map with its theme (`CharacterColorMap.OverlayGear`), so a full set is coherent by construction and a mixed outfit shows each piece in its own set's colours. Only item ids travel the network; every machine works the colours out. Icons and the try-on doll use the same overlay. A theme never recolours skin or bone (`GearLadderTests`).
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

**As built, 6 Oct 2026.** Done at the **Armorer's forge** (`NpcOffer.GearWork`, window `GearWorkWindowController`, service `PlayerGearWork`) on gear that is in the Pack. Every number below is in `Items/Data/Assets/GearProgressionSettings.asset` (section `temper`); the rules are `GearProgressionRules`. Where Twelve Sky 2's own behaviour is known it is followed (owner, 5 Oct 2026); the rest is marked *(proposed)*.

**The forge bench (rebuilt 7 Oct 2026).** Tempering, Fusion, Tuning and Reforge share one narrow bench that opens docked at the right edge with the Pack open beside it (`UI/Inventory/ForgeWindow.uxml`, `Forge.uss`). The original game's Upgrade window (12Sky pages 23 and 24) is the same idea, a small panel beside the open bag with the character in view; the layout here is our own. How a player uses it:

1. Talk to the Armorer and choose the forge. The bench and the Pack open on the right, and the camera slides so the character stands in the middle of the part of the screen left free (`ServicePanelPlacement`, `PointAndClickPlayer.SetScreenFraming`). A panel the player has placed by hand in the layout editor stays where it was put. The Herald's window is kept off the character the same way.
2. Pick the work on the tabs: TEMPER, FUSION, TUNING, REFORGE.
3. Put the piece on the anvil: right-click it in the Pack (its tooltip says "place on the forge") or drag it onto the socket. A ring round the socket shows its Temper against the cap, with the stretch where a fail can break the piece marked red. On the TEMPER tab the socket's rim, the ring and the number all take the colour of the glow band the piece's Temper is in (`GearProgressionRules.TryGetGlow`; below 1 % the plain rim), and change with it as a strike lands, after the short flash of the result (9 Oct 2026; on the other tabs the socket keeps the rarity look). The socket and its ring are the largest thing on the bench (104 and 224 units of the 336-wide bench); the materials are one small row each, so the whole bench, button included, fits a 720-unit-high screen. A click on the socket takes the piece off again.
4. Load what the work needs from the picks under the anvil, which show what the Pack holds, with counts: an Alloy and, if wanted, a Failsafe (Tempering), the Rare to fuse in (Fusion), chips (Tuning). The best Alloy that can be used is loaded already.
5. Read the numbers: the change ("+45 % to +54 %"), the chance, and chips for what a fail costs and whether it can break.
6. Press the one button, which carries the cost. The bench strikes for 0.45 s and then shows the answer: the ring sweeps up on a success, drops on a fail, empties on a break. The piece and the Alloy stay loaded, so the next attempt is one press.

The bench is an `IPackTarget`, as the Vault, Trade and Kiosk windows are, and a piece on it is locked in the Pack. Closing the bench closes the Pack again if the bench opened it, and puts the camera back. The rules and the server side (`PlayerGearWork`) are what they were; where a panel docks and how far the camera slides is pinned by `ServicePlacementRulesTests`.

**What Tempering raises.** One thing per slot, as in 12Sky page 23. This replaces the earlier draft ("Defense on everything but the weapon") and is what makes a luck Amulet:

| Slot | 1% of Temper gives |
| --- | --- |
| Weapon | +1% of its Attack Power |
| Armor, Cape | +1% of its Defense |
| Gloves | +1% of its Hit Rate |
| Boots | +1% of its Evasion |
| Ring | +0.05% Critical *(proposed)*: +6% at 120% |
| Amulet | +4 Luck × rarity multiplier *(proposed)*: +576 on an Uncommon at 120% |

**The cap is 120%** (owner, 6 Oct 2026). Going on to 150% is a system for later in the game; see "Later: beyond 120%" below. It is one number, `temper.hardCap`.

**Materials.** Five Alloys (items 9100–9104; nothing drops or sells them yet). Prime Alloy (9105) exists as an item but has no use until the cap is raised: the forge does not offer it and refuses it.

| Alloy | Twelve Sky 2 | Step |
| --- | --- | --- |
| Alumite | Aluminium | +3% |
| Iridion | Iridium | +6% |
| Orichalc | Oracite | +9% |
| Eversteel | Steel of Eternity | +12% |
| Trinity Platinum | Holy Trinity Platinum | +15% |
| Prime Alloy | Millennial Gold | later system: +3%, the only Alloy that works from 120% up |

**Success chance** (Twelve Sky 2's formula; it replaces the per-Alloy odds of the earlier draft):

```
chance = 103 − (current Temper % + Alloy step) + Luck bonus      clamped to 5% .. 100%
Luck bonus = Luck / 100, at most 25% (proposed cap)
```

So a bigger Alloy is a bigger step at worse odds, and the higher the piece the harder every step: Alumite on an untouched piece is certain, Trinity Platinum at 60% is 28%, anything past 98% is the 5% floor plus Luck.

**Luck.** `StatType.Luck` existed and was read by nothing; the forge reads it (Tempering, Fusion, Reforge). It comes from:

- **Common and Uncommon gear** *(proposed numbers)*: every piece carries flat Luck (Common 5 at level 1 to 30 at level 28; Uncommon 40 at level 20 to 70 at level 40). Rare and Epic carry none, so cheap gear stays worth keeping as a forge set, as in Twelve Sky 2.
- **A tempered Amulet** (the table above): the higher its Temper, the more Luck.
- **Hot Streak** (TS2: Garuda's Prayer): +13% Luck at rank 1 to +100% at rank 30.
- The base 50 every character has.

**A fail** takes 3% back (never below 0). From 60% it may also break the piece:

| Temper before the attempt | Break chance on a fail | Glow |
| --- | --- | --- |
| 0 | none | none |
| 1–29% | none | green |
| 30–59% | none | yellow |
| 60–89% | 5% rising to 15% | blue |
| 90–119% | 15% rising to 30% | purple |
| 120% (the cap: nothing more can be attempted) | none | orange, with embers; **red** on an Epic and above |

- **Failsafe** (TS2: Protection Charm): stops one break, and is only used up when it does. A fail it saves still costs the 3%.
- **Lucky Failsafe** (TS2: Lucky enchant): +5% to one attempt and no break on it. Used up on the attempt.
- **Advanced Failsafe**: a fail costs nothing and never breaks. Used up on a fail.
- A **break** destroys the piece. At 120% the forge answers "It is at 120%: the highest Temper there is." and takes nothing.
- Cost per attempt *(proposed)*: `ItemLevel × 500 × RarityMultiplier` Creds, plus the Alloy.

**Later: beyond 120%.** Kept in the settings and the rules, switched off by `hardCap = 120`, and still covered by a test that raises the cap. Raising `hardCap` above `softCap` (120) turns it back on with no new code:

| Temper before the attempt | Needs | Break chance on a fail |
| --- | --- | --- |
| 120–132% | Prime Alloy | 35% |
| 133–150% | Prime Alloy and an Advanced Failsafe | 50% |

The Creds cost doubles from 120%.

**Effects** (6 Oct 2026, built by `GameFeelBuilder`, menu **Krytical Nova ▸ GameFeel ▸ Rebuild Forge Feel**). Every player nearby sees the burst on the character; only the one who did it gets the screen effects and the result text (`PlayerGearWork.ShownRpc` to everyone, `FinishedRpc` to the owner).

| Outcome | On the character | For the player who did it |
| --- | --- | --- |
| Success | rising gold burst, rays, sparkles, a comic "FORGED!" over the head | chromatic shimmer, a small upward camera nudge |
| Fail | a dull spark and smoke puff | a small camera drop |
| Failsafe held | the fail puff, a cyan shield flash, a comic "SAVED!" | a smaller camera drop |
| Break | red shatter burst, sparks, a comic "CRACK!" | camera jolt, chromatic punch, red vignette |

The three words are the forge's own (8 Oct 2026, `Design/13_comic_effects.md`): nothing else in the game shows them.

Sound slots are on the prefab and empty.

**Announced to the server** (9 Oct 2026). The success that takes a piece to `temper.announceFrom` (120; 0 switches it off) is told to every player on the server, wherever they are: a System chat line "<player> tempered <item> to 120%." and the same text as a toast (`PlayerGearWork.ServerTemper` → `PlayerChat.ServerAnnounce`, one message carrying both). It is said once, on the step that reaches it (`GearProgressionRules.TemperIsAnnounced`), not for attempts above it.

**Glow at the cap (10 Oct 2026).** At 120 % the light is no longer smooth: it is broken up into drifting embers (a ragged edge, a patchy body, shades from a deeper to a hotter tone of the one colour), after the "120 % Flame" glow of Twelve Sky 2. A Rare keeps the band's orange and gets only that. An Epic (and anything above) turns **red** at 120 % and keeps everything it had: tongues of flame to the tip, lightning and sparks, now with the embers in them. Below 120 % nothing differs by rarity but the look, as before. The data: `TemperGlowBand.recolorFrom` / `recolor` (which rarity upward swaps the band's colour, and to what) and `TemperGlowBand.ember` (1 on the 120 % band, 0 elsewhere), times the rarity's own `TemperGlowLook.ember`; one call hands both out, `GearProgressionRules.TryGetGlow(settings, temper, rarity, out color, out look)`, and the icon rim, the "+120" and the forge's ring use `TemperGlowBand.ColorFor(rarity)`, so an Epic's are red too. How the embers look is tuned on the renderer's Glow Halo feature (`emberSize`, `emberSpeed`, `emberEdge`, `emberPatch`, `emberHeat`, `emberDeep`; Rendering-Bible 6e). A web search found no footage or description of the TS2 glow beyond colour tables that disagree with each other; the look follows the owner's two screenshots.

**Glow.** The bands in the table (green and yellow are the owner's memory of Twelve Sky 2 for the range below 60%: at launch Rares glowed green up to 57%, the Classic guide lists yellow for 1–59%). The colour is the same for every rarity; what differs is how it is shown (`temper.glowLooks`): Common, Uncommon and Rare a soft halo that breathes, Epic the same colour several times brighter and wider, with flame, lightning and a few sparks in that colour. Shown on the weapon (`WeaponVisualMount.SetGlow`, drawn by the Glow Halo renderer feature) and as the "+36" on item icons; outfits do not glow yet. **Since 9 Oct 2026 a tempered weapon's icon has a glow rim too**, in the Pack, on the Equipment page and on the forge's socket: the outline of the part of the weapon that glows in the world (its `WeaponGlowZone`: the blade and not the hilt), behind the icon, tinted with the band's colour, so one baked rim an icon serves every band (`ItemGlowIcons`, `GearProgressionSettings.iconGlows`, `GearUIController.SetTemperGlow`; baked by Krytical Nova ▸ Items ▸ 6. Bake Weapon Glow Rims, to be run again after weapon icons or glow zones are re-baked). The Equipment page's doll shows the worn weapon's glow as well (`CharacterPreviewStage.SyncGlow`), as the select screen's doll does. Other players see it: the Temper % of each worn slot is replicated to everyone (`PlayerEquipment.GetTemper`).

**The state** of a piece (Temper, Fusion stage, Tuning stage) is `GearItemState`, packed into `ItemStack.data`, so it travels with the item through Pack, slot, Vault, Trade, Kiosk and the ground and is saved with it.

## 5. Fusion FS1–FS12 (TS2: Combination / Combine CS1–CS12)

**As built, 6 Oct 2026** (settings section `fusion`). Fusion melts a **material** into a **target**, raising the target's Fusion stage by one. Twelve stages. The stage shows after the item name ("FS7").

**What is fed to what** (12Sky page 24: "Epic items can only be combined with lower rank 'Rare rank' item"; owner, 6 Oct 2026):

| Target | Material |
| --- | --- |
| Rare | an untouched Rare of the same kind and the same item level |
| Epic | an untouched Rare of the same kind: only the closest Rare tier at or below the Epic's own level |

| Epic level | Rare it takes |
| --- | --- |
| 100, 103 | 95 |
| 106, 109 | 105 |
| 112 | 110 |

"The same kind" is the same slot, and for weapons the same weapon class. "Untouched" is no Temper, no Fusion, no Tuning. An Epic is never fed an Epic. The tier is worked out from the stat table's bands (`GearProgressionRules.FusionMaterialLevel`), not hard-coded.

- **Target:** Temper of 12% or more, Rare or Epic alike (owner, 6 Oct 2026; the earlier 132% for an Epic cannot be met under a 120% cap). `minTemperRare`, `minTemperEpic`.
- **Cost:** one Fusion Catalyst, the material, and 100,000 Creds for FS1, +50,000 a stage (FS12 = 650,000).
- **Success chance** *(proposed)*: 100% for FS1–FS3, then −7% a stage (FS12 = 37%), plus the Luck bonus (as for Tempering).
- **A success takes 12% Temper off the target** (Twelve Sky 2: "successfully combining always takes away 12% enchant"). Tunable: `temperCostOnSuccess`.
- **A fail destroys only the material** (and uses up the Catalyst and the Creds). The target is never harmed and loses nothing. This is the owner's recollection; the 12Sky help page says nothing is destroyed on a fail, which is one switch away (`failConsumesMaterial`).

**Effect per stage** *(proposed; no source gives Twelve Sky 2's numbers as text)*, scaled from the item's own baked stat so the same table works at every level:

| Slot | Per stage | At FS12 |
| --- | --- | --- |
| Weapon | +3% of its Attack Power, +2% of its Hit Rate | +36% AP, +24% Hit |
| Armor | +3% of its Defense, +2% of its Evasion, +1.5% Max HP | +36% DP, +24% Eva, +18% HP |
| Gloves | +3% of its Defense, +2% of its Hit Rate | +36% DP, +24% Hit |
| Boots | +3% of its Defense, +2% of its Evasion | +36% DP, +24% Eva |
| Cape | +3% of its Defense | +36% DP |
| Ring | +8 Elemental Attack × RarityMultiplier | +96 × mult |
| Amulet | +8 Elemental Defense × RarityMultiplier | +96 × mult |

The note "+20,000 HP for a full purple set at CS12" is an Epic set bonus, which is excluded; the Armor HP line above is the per-item share of that idea.

Fusion stage is kept when the piece is tempered further or tuned. A fused piece cannot be a material.

## 6. Tuning, stages 1–25 (TS2: Refine)

**As built, 6 Oct 2026** (settings section `tuning`). Tuning adds a second set of stats by slot, one stage at a time, up to stage 25. Rare and Epic only; the Cape is not tuned. At the Armorer's forge. Costs chips, **Renown** and Creds.

**Per-slot gains** (owner's notes, verbatim):

| Slot | Per stage | Stage 25 bonus |
| --- | --- | --- |
| Weapon | Critical Rate +1%, Spirit +5 | Spirit +5 |
| Armor | Max HP +400, Spirit +5 | Spirit +5 |
| Gloves | Attack Speed +0.5%, Agility +5 | Agility +5 |
| Boots | Move Speed +0.5%, Agility +5 | Agility +5 |
| Ring | Core Resistance +0.1%, Vitality +5 | Vitality +5 |
| Amulet | Knockback Resist +1%, Strength +5 | Strength +5 |

Attack Speed, Core Resistance and Knockback Resist are granted and shown but nothing consumes them yet. **To tune:** +400 Max HP a stage is +10,000 at stage 25, against about 2,000 Max HP on a level-112 character today: the number is from a game with far larger HP pools.

**Cost per attempt** *(proposed)*:

| Stages | Chips | Renown | Creds |
| --- | --- | --- | --- |
| 1–12 | 1 Tuning Chip | 10 | 50,000 |
| 13–20 | 1 Violet Chip | 25 | 150,000 |
| 21–25 | 2 Violet Chips | 50 | 400,000 |

**Outcome** *(proposed)*: 100% to stage 10, then −5% a stage (stage 25 = 25%). A fail uses up the materials; stages 1–12 never drop, a piece at stage 13 or higher drops one stage. Luck does not help here. A **Reset Chip** returns the piece to stage 0 and refunds nothing.

**Renown** is `ProgressionState.renown`: held by the server, saved with the character, shown in the forge window. Nothing earns it yet (`gm.renown`).

## 6b. Reforge (12Sky: Item Exchange, page 25)

**As built, 6 Oct 2026** (settings section `reforge`). Named Reforge because The Exchange is the Broker's consignment board. An **untouched Rare** (Weapon, Armor, Gloves, Boots, Ring or Amulet) becomes another Rare of the **same item level**, picked at random from every such piece the character's faction and body can wear. Twelve Sky 2: "you pay for a chance at a random rare; you never destroy your rare, only lose silver".

- Cost: 1,000,000 Creds at level 45, +200,000 per ten levels (2,200,000 at 105, 2,300,000 at 110).
- Success *(proposed)*: 50%, plus the Luck bonus. A fail costs only the Creds; the piece stays as it is.
- Epic, tempered, fused or tuned pieces cannot be reforged.

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

## 9. Ascension (TS2: CP Reinforcement halo) and Nova Standings (TS2: CP stat Titles), at the Herald

**As built (7 Oct 2026).** Both are Renown-bought **character** progression, not gear, and both are worked at one NPC: the **Herald** (`NpcRole.Herald`, offer `NpcOffer.Herald`), who stands at the plaza stall first kept for a Collector and will host the Bounty Board later. The Nova Store stays real-money only (Store Clerk), and the Fixer stays Renown-for-materials. The owner named them on 6 Oct 2026: the halo ladder is **Ascension** (the halo itself is still a **Nova**), the title ladder is **Nova Standings**. Every number below is in `NovaSettings.asset` (`Gameplay/Progression/Data/Assets/`).

**Ascension** (TS2 "CP Reinforcement", whose visual tiers are the halos):

| Rule | Value | Twelve Sky 2 |
| --- | --- | --- |
| Cost per attempt | 100 Renown + 500,000 Creds | 100 CP + 1,000,000 Silver. Ours is half the Creds (owner, 6 Oct 2026) |
| Outcomes | rise +1 · hold +0 · slip −1, at 55 / 35 / 10 % while the level is +24 or lower, 45 / 40 / 15 % above | +1, +0 or −1 "by luck"; TS2 publishes no odds, so these are ours |
| Floor | never below +0, and **a slip never takes a Nova tier once it is reached**: at +1, +9, +17 … +89 a slip changes nothing (owner, 6 Oct 2026). `slipKeepsTier` switches the rule | TS2 lets a slip cross a halo tier |
| Effect per level | +1 Strength, +1 Agility, +1 Vitality, +1 Spirit, as flat stat modifiers, so every derived stat follows | the same |
| Cap | +96 | TS2 shows tiers to 89+ |
| Nova (halo) | one tier per 8 levels: +1 to +8 is Nova I … +89 to +96 is Nova XII | a new halo per 8 levels |

**The Nova** (rebuilt 7 Oct 2026; it was one flat disc before). A small star floating above the head with rings of light turning round it (`NovaHalo`): the star is a sphere of slow-moving plasma, and each ring is a real ring on its own tilted axis. Some turn against the others, and each axis swings slowly so a ring is never seen from one side only. Round it are a glow, rays for the upper tiers and sparks in orbit. It follows the head's place but never its turning, and the namecard rises to clear it. `NovaSettings.look` holds the gap above the head, the list of rings (radius, width, tilt, spin, pattern and the first tier that shows it) and each tier's look (two colours, size, star, glow, rays, sparks). A higher tier is the lower one with more turning in it, so there are no per-tier prefabs:

| Tier | Looks like | Colour |
| --- | --- | --- |
| I | a star with one flat ring | silver |
| II – III | a second, then a third ring, each leaning its own way: a gyroscope | silver-blue, cyan |
| IV | a stronger glow and the first sparks in orbit | teal |
| V – VI | dashed orbits, one wide and one inside | green, yellow-green |
| VII | an upright hoop with a row of glyphs round it | gold |
| VIII | a small fast ring close to the star | ember |
| IX – XI | rays from the star (4, 6, then 8) and lights running round the rings | ember-red, crimson, violet |
| XII | a last wide orbit, ten rays, every colour | prismatic |

Four shaders draw it, all in `Gameplay/Progression/Shaders/` with one material each: `KryticalNova/NovaCore` (the star: solid, so rings pass behind it and in front of it), `NovaRing` (every ring; a far ring is kept a couple of pixels wide, so the rings still read at play distance), `Nova` (the glow and the rays, over a dark patch with a few faint stars so the added light still reads against a noon sky) and `NovaMotes` (the sparks; their motion is a function of time, so every machine shows the same thing). The materials are linked from `NovaSettings` by **Setup ▸ Player Prefab** (`PlayerSystemsSetup.LinkNovaMaterials`), so they ship in a build.

**The moment** (7 Oct 2026; built by `GameFeelBuilder`, menu **Krytical Nova ▸ GameFeel ▸ Rebuild Nova Feel**). Everyone nearby sees what happens at the Nova, in the tier's colour (one white set of effects, tinted as they spawn). Only the player whose moment it is gets the screen effects, the answer in the window and the banner (`PlayerNova.ShownRpc` to everyone, `FinishedRpc` to the owner).

| Outcome | The Nova itself | In the world | For the player |
| --- | --- | --- | --- |
| A level gained | flares and spins up | a ring of light off the Nova, sparks | camera nudge, shimmer, a light wash of the tier's colour; the level jumps and settles on the Herald's card |
| A new Nova tier | flashes, and the new set of rings unfolds one after another | the star bursts, light rises through the character, a ring runs out over the ground | view punch, lens bulge, a strong wash, a short hold of the pose; banner "NOVA VI" with its pips counting up |
| No change | dims for a moment | a dull spit of sparks | a small camera drop |
| A level lost | sputters and all but stops | red shards | camera jolt, chromatic punch, red wash |
| A slip the Nova held | a hard, bright hold | a cyan flash | a light cyan wash |
| A Standing rank | (none) | a seal at the feet, rising light, sparks | camera nudge, shimmer, gold wash; banner "VANGUARD THANE" with the rank's pips |

Pressing ASCEND charges for 0.45 s before the attempt goes, so the answer lands as a beat. The banner keeps clear of the Herald's window, and a plain level with the window open is told by the window alone. The GM tools play all of it: raising Ascension or a Standing sends the same result an attempt would, so every tier can be looked at. A reaction that reaches a machine before the Nova's new tier does waits for it. Sound slots are on the prefab and empty.

**Nova Standings** (TS2 Classic CP stat titles L1–L12):

| Rank | Renown (TS2's own list) | Bonus held on that track |
| --- | --- | --- |
| 1 | 800 | +1 % |
| 2 | 1,700 | +2 % |
| 3 | 2,500 | +3 % |
| 4 | 3,400 | +4 % |
| 5 | 4,200 | +5 % |
| 6 | 5,100 | +6 % |
| 7 | 5,900 | +7 % |
| 8 | 6,800 | +8 % |
| 9 | 7,600 | +9 % |
| 10 | 8,500 | +10 % |
| 11 | 9,300 | +11 % |
| 12 | 10,000 | +12 % |

A whole track costs 65,800 Renown; all four cost 263,200. Four tracks, matching TS2's STR / DEF / HP / CHI titles: **Vanguard** (Attack Power), **Bulwark** (Defense), **Ironblood** (Max HP), **Mindwell** (Max Flux). Ranks are bought in order, always succeed and are never lost. The +1 % per rank is ours (no source gives TS2's bonus); it is one number, `percentPerLevel`.

- **Every track a character holds counts**, worn or not (owner, 6 Oct 2026). TS2 applies only the title equipped.
- One track's name is **worn** as the title: the first rank bought is worn at once, and the Herald's window changes it for free. It shows on the namecard, on the Character Sheet and in the Herald's window.
- A rank is named by the track and the **faction's own rank word** (owner, 6 Oct 2026), so a Covenant character's sixth Vanguard rank is "Vanguard Thane" and a Null character's is "Vanguard Cipher":

| Rank | Hegemony | Null | Covenant |
| --- | --- | --- | --- |
| 1 | Associate | Unit | Kinsworn |
| 2 | Operative | Routine | Oathsworn |
| 3 | Specialist | Process | Hearthguard |
| 4 | Agent | Daemon | Shieldbearer |
| 5 | Auditor | Kernel | Runebearer |
| 6 | Captain | Cipher | Thane |
| 7 | Controller | Override | Bannerlord |
| 8 | Prefect | Vector | Stormborn |
| 9 | Executor | Architect | High Thane |
| 10 | Consul | Apex | Elderblood |
| 11 | Regent | Zenith | Dawnbringer |
| 12 | Hegemon | Singularity | Mythborn |

**Where it lives.** `PlayerNova` (`Gameplay/Progression/Runtime`) holds both ladders in one replicated `NovaState` (Ascension level, four track levels, the track worn): the server decides and saves it (save key `nova`; a character saved before it existed loads as +0), and every machine works out the stat modifiers and the Nova from it, the way worn gear does. `NovaRules` is the rules, pure, with the roll passed in; the Herald's window (`HeraldWindowController`, `UI/HUD/Herald/`) calls the same rules for odds, costs and reasons, and `NovaRulesTests` pins them. An attempt is refused away from a Herald, while defeated, or without the Renown and the Creds (`CredsSink.Herald`); nothing is spent when it is refused. GM: `gm.renown [amount]`, `gm.renown.set <amount>`, `gm.ascension [0-96]`, `gm.standing <track|all> <0-12>`, `gm.standing.wear <track|none>`, `gm.herald` (open it anywhere; `gm.herald off`); all of it is on the **Renown** tab of the GM Tools window (Renown give / take / set, Ascension by level or by Nova tier, a rank per track, the track worn, max and clear both ladders).

**Not built:** any way to earn Renown (it comes from `gm.renown` only). The **bounty** half of the owner's note is a separate Renown source, not a Standings mechanic: Bounty Board kills would pay the Renown that funds both ladders; its design is still open.

## 10. Auxiliary slots: Cape (TS2: Cloak), Pet, Mount, Wings

These cost more Creds than armor in TS2 and are where min-maxing lives; only the Cape is in this gear build.

- **Cape** (`EquipSlot.Cape`): Tempering and one Core slot; base Defense via the secondary slope; ladder adds **Critical Defense %** at Rare (TS2 Tier 2 Eternal Capes carry ~28%). Critical Defense is a new `StatType` (reduces incoming crit damage). Cape bands as built: Rare I 45–75, Rare II 85–110, Epic 100–112 (no Common or Uncommon Capes: owner, 6 Oct 2026).
- **Pet** (`EquipSlot.Pet`, cosmetic follower exists): TS2 pets add flat Damage / Defense / HP / Chi scaled by a growth % (100–200%), with +50 damage per 25% over 100%; 3 × level-1 pets combine into one M1, 3 × M1 into one M15. Deferred to design 04; the gear build only reserves `ItemDefinition.petGrowthPercent`.
- **Mount**: no slot yet. TS2 tiers 5% → 10% → 15% (each crafted from a maxed lower tier) adding % HP, Damage, Elemental, Chi, Defense, Evasion; the notes cite +17–20% HP and Damage at the top. Deferred to design 05; add `EquipSlot.Mount = 9` now so the enum does not shift later.
- **Wings** (TS2 Classic): crafted from feathers + 50–100 Renown at the Outfitter (TS2 Tailor), tempered to 120% with their own protection item, stat options re-rolled with Arch Wingstones. Not in the notes; listed only as a possible cosmetic-plus-stat slot for the Outfitter.

## 11. Data model and code changes (for Claude Code)

**As built (7 Oct 2026): set names, colour themes, paired weapons, the glow, Ascension and Nova Standings.**

- `GearDictionary.Line` gained `set` (the set a piece is named after), `theme` (a `GearThemeLibrary` theme by name) and `icon` (an icon of its own; a themed line has one, since a look is shared by lines of different colours). `SidekickGearGenerator` names lines after their set and seeds Capes from Rare up only; `GearThemeBuilder` seeds the themes and bakes which colour-map cells each theme paints for each look.
- `ItemDefinition` gained `colorTheme`, `colorThemes`, `colorCells` (the theme and the cells this piece paints), and for weapons `paired`, `offHandLocalPosition / Euler`, `leftHanded`, `carry` (`WeaponCarry.Back` or `Hips`) and `carryLocalPosition / Euler`. `GearStatBaker` copies them from the look template to every tier.
- `CharacterAppearance` remembers what is worn per slot and raises `WornChanged`; `CharacterLook` refills the colour map as recipe, skin, the player's picks, then the gear overlay. `CharacterPreviewStage` and `ItemIconCapture` do the same for the try-on doll and the icons (`CaptureLineIcons`).
- `WeaponVisualMount` shows a paired weapon in both hands (the off hand is the main hand's mirror), wears the carry pose when sheathed (back, or a holster on each hip: sockets from `WeaponSocketsSetup`) and moves a back-carried weapon out a little when no Cape is worn. See `04_Weapon_Classes.md`.
- The Temper glow (rebuilt 7 Oct 2026) is a halo of light round the weapon, not a tint on it. It is drawn on the screen (`GlowHaloFeature`, Rendering Bible section 6e): the glowing meshes go into a mask, the mask is flooded into a distance from the outline, and the light is laid over the frame from that distance, so it hugs any shape and falls away evenly without relying on bloom. Evolved pets wear the same glow (`PetFollower`).
  - **Each rarity's look** is a row of `GearProgressionSettings.temper.glowLooks`: `intensity`, `width` (metres), the breath (`pulse`, `pulseSpeed`, `pulseWidth`), and for the strong looks `twinkle` (sparks), `lightning` and `flame`, all in the Temper band's own colour. As set: Common 0.5 / 4.5 cm, Uncommon 0.6 / 5 cm, Rare 0.7 / 6 cm, Epic 1.6 / 10 cm with sparks 1.3, lightning 1, flame 1, Legendary 1.9 / 12 cm with 1.5, 1.2, 1.
  - **Only the business end glows.** Every weapon model carries a `WeaponGlowZone` (which part glows, which is kept dark) written by **Krytical Nova ▸ Items ▸ 8. Bake Weapon Models**: blades past the guard, axes and scythes on the head, polearms on the blade, guns everywhere but the grip (the two long rifles from the lock or receiver forward), bows everywhere but the string. The dark parts also keep the light off the air beside them. A model the rules read wrongly is set on its prefab and ticked "keep as set" (the ten guns, the compound bow, the cleaver, one knuckle blade).
  - **Long weapons stay out of the ground.** After the animation has placed the hand, `WeaponVisualMount` tips a weapon up about its grip when its lowest point would sink in (`groundClearance`, 3 cm, on the character prefab). It follows the weapon's real outline (`WeaponReach`, baked by the same menu item), so it lifts no further than needed. Looks only: the hitbox does not move.
- Ascension and Nova Standings: section 9.

**As built (6 Oct 2026): steps 3 to 6 of section 12, and Reforge.** Sections 4, 5, 6 and 6b say what the rules are; this is where they live.

- `GearItemState` (`Items/Runtime`): Temper 0–150 (what an item can store; the rule stops at `temper.hardCap`, 120), Fusion 0–12, Tune 0–25 in the low 17 bits of `ItemStack.data`. `ItemStack` did not grow new fields: the `data` word the pets already use carries it, and the bits above are left for sockets.
- `GearProgressionSettings` (`Items/Data`, asset in `Items/Data/Assets/`): one asset with four sections (`temper`, `fusion`, `tuning`, `reforge`) instead of the four assets first planned, because the resolver, the forge and the window all need all of it. It points at `GearStatTable` for rarity multipliers and level tiers.
- `GearProgressionRules` (`Items/Runtime`): the rules, pure and with every roll passed in. The server calls them with real rolls, the forge window calls them for odds and reasons, `GearProgressionTests` pins them.
- `GearStatResolver.Resolve(stack, definition, source, results, progression)` adds what the state is worth. `PlayerEquipment` hands it the real stack on the server and the owner (the inventory passes the worn items' data on), and replicates one Temper byte per slot to everyone (`EquipmentTemper`) for the glow.
- `PlayerGearWork` (`Gameplay/Forge/Runtime`, namespace `KryticalNova.Forge`): the networked service on the player. One owner-to-server request per action (`RequestTemper`, `RequestFuse`, `RequestTune`, `RequestResetTuning`, `RequestReforge`), a `GearWorkResult` back to the owner. It is its own system folder because it sits above Items, Economy (Creds through `CredsSink.GearWork`), Progression (Renown) and NPC (the Armorer must be in reach).
- Materials: rows in `UtilityItemGenerator` (ids in `GearMaterialIds`, assets in `Items/Data/Assets/GearMaterials/`).
- `GearStatBaker.UpgradeTable` adds the level-110 Rare tier and the Luck rows to an existing stat table without touching what was tuned in it; the bake runs it.
- UI: `GearWorkWindowController` with `UI/Inventory/ForgeWindow.uxml` and `Forge.uss`; tooltips and item icons show the state (`GearUIController`).
- GM: `gm.temper <percent> [slot]`, `gm.fuse <stage> [slot]`, `gm.tune <stage> [slot]`, `gm.renown [amount]`, `gm.forgekit [count]`, `gm.forge` (open it anywhere; `gm.forge off`), `gm.kit temper [count]` (every Temper item that has a use). `gm.temper` stops at the cap. `gm.temper.all <percent> [worn|pack|all]` sets the Temper of every piece the targeted player wears, carries, or both; the GM Tools window's Items tab has it as a Temper % slider with three buttons.
- Not built: Slotting and Cores, Epic Mk II, rank up / down, Alloy crafting (12Sky page 22), any drop or shop for the materials, any Renown income.

**As built (4 Oct 2026): steps 1 and 2 of section 12.**

- `GearDictionary` (`Gameplay/Items/Runtime`) holds two tables. **`lines`** is the hand-edited one: a line is one run of level tiers that share a look (`lineId`, `tag`, `slot`, `rarity`, `band`, `pack`, `factions`, `bodies`, `weaponClass`, `look`, `displayName`). **`entries`** is baked: one row per item, read by the equip rule, the GM commands and the starter kit.
- Item ID = `10000 + (lineId − 1) × 16 + tier index`. Line IDs are append-only, so item IDs never change. Legacy IDs below 10000 are retired (look templates, pets and Stims keep theirs).
- A **look template** is an `ItemDefinition` that is not in the catalog (the per-variant outfit pieces, one asset per weapon mesh, the ring / amulet icons). `GearStatBaker` copies its parts, icon and weapon model onto every tier of the lines that wear it. To change a look, change the line's `look`.
- `GearStatTable` (`Items/Data/Assets/GearStatTable.asset`) holds the bands, rarity multipliers, slopes, the secondary ladder and exact rows. The owner's Epic 100–112 tables are exact rows, word for word, because they do not follow one rounding rule; `GearLadderTests` asserts every cell. Uncommon and Rare secondaries are *(proposed)* values from the milestones in section 3.
- `ItemDefinition` gained `itemLevel`, `lineId`, `weaponClass`, `baseStats` (primary: what Tempering scales), `secondaryStats`, `skillModifiers`. Not added yet: `maxSockets`, `setId`, `markTier`, `mkIIDefinition`, `fusionEligible`, `tuneEligible` (they arrive with their systems).
- `ItemStack` and `EquipmentLoadout` are unchanged (item ID only on the wire). When Tempering lands, `ItemStack` grows its instance fields and the loadout carries stacks; `GearStatResolver.Resolve(stack, definition, …)` already takes the stack, so callers do not change.
- `StatType` gained `AttackSpeedPercent`, `CoreResistancePercent`, `KnockbackResistancePercent`, `CriticalDefensePercent`, `KillExpRatePercent`, `KillExpAmountPercent`. Skill modifiers are `ItemDefinition.skillModifiers` (class + levels), not a stat type. Critical Defense is applied in `CombatCalculator`; skill modifiers and the Kill-XP stats are stored and shown only.
- Menus: `Krytical Nova ▸ Items ▸ 1. Generate Gear Looks From Synty` (templates, seeds missing lines), `▸ 6. Bake Gear Ladder` (lines → items, entries, catalog), `▸ 2. Capture Item Icons` (per look; jewellery, forge materials and the other items without a model are not rendered: they take a pack icon from **Krytical Nova ▸ Icons ▸ Icon Library**, and every ring and amulet of the ladder wears its look's), `▸ Rebuild Weapon Look Models`. GM: `gm.gear <slot> <level> <rarity> [class]`, `gm.loadout [class]`, and the kits: `gm.kit gear [level] [rarity]` (every piece the targeted character can wear at that tier, 112 Epic unless told, with the weapon of every class of its faction), `gm.kit temper [count]`, `gm.kit pets`, `gm.kit all`. A kit says how many pieces did not fit a full Pack. **One whole set:** `gm.gearset [level] [rarity] [temper] [wear]` gives the set the targeted character wears at that tier (Armor, Gloves, Boots, Ring, Amulet, the Cape when the tier has one, and the weapon of the class in use), every piece at that Temper %; `wear` puts it on and moves what was worn to the Pack, and a character below the set's level gets it in the Pack with the reason. The GM Tools window's Items tab lists the character's sets by name ("Sunlord (Epic 100–112)") with a level, a Temper % and a "put it on" tick.
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

**NPC routing** (`NpcRole`): Armorer (role Blacksmith) → Tempering, Fusion, Tuning, Mark upgrade. Fixer (role Agent) → Slotting, Cores, Renown materials. **Herald (built)** → Ascension, Nova Standings, later the Bounty Board. Store Clerk (role MallManager) → real-money Nova Store only. Add `NpcRole.Herald`, a seed in `Zone01Setup`, and a stall kind in `StarterTownBuilder.Plaza` (the unused `Collector` kind can become the Herald's booth).

**UI** (UI Toolkit, matching `GearUIController`): one `GearServicePanel.uxml` with a tab per service, a two-slot drop target (item + material), a cost line, a result toast. Item tooltips gain lines for Temper %, FS, Tune stage, sockets, and the glow colour from section 4 on the icon frame. Nameplates show the active Title; `ActorVfx` shows the Nova.

**Economy hooks:** `ItemDefinition` for every material (Alloys I–V, Prime Alloy, Failsafe, Advanced Failsafe, Fusion Catalyst, Tuning Chip, Violet Chip, Reset Chip, Slot Cutter, Deep Cutter, Core Extractor, Slot Sealer, Mark Stone, 5 Cores × 4 grades) go in `Gameplay/Items/Data/Assets/Materials/`, generated by a `GearMaterialGenerator` alongside the existing `PillGenerator`. `item.give` already lets you test any of them.

## 12. Build order

Each step compiles and plays on its own; the GM console (`item.give`, `gm.attr`) is the test harness until shops exist.

- [x] **1. Stats and definitions.** *(done 4 Oct 2026: all four rarities baked, not only Common and Epic)* Add the seven `StatType`s and their definition assets; extend `ItemDefinition` and `ItemStack`; write `GearStatTable` and `GearStatBaker`; bake Common 1–30 and Epic 100–112 and pass the Epic acceptance test.
- [x] **2. Resolver.** *(done 4 Oct 2026)* `GearStatResolver` and `PlayerEquipment` reading it; character sheet shows gear-derived values; equip a baked Epic weapon and see 884 AP at 0%.
- [x] **3. Materials.** *(done 6 Oct 2026 for the forge: Alloys, Failsafes, Fusion Catalyst, chips, as rows of `UtilityItemGenerator`; Cores, cutters and the Mark Stone come with their systems)* `item.give` works for all.
- [x] **4. Tempering.** *(done 6 Oct 2026)* Settings section `temper`, `PlayerGearWork.RequestTemper`, forge window tab, tooltip and glow. Test: 0 → 60% never breaks, 90–119% breaks sometimes, Failsafe never breaks, nothing works at the 120% cap.
- [x] **5. Fusion.** *(done 6 Oct 2026)* Settings section `fusion`, `RequestFuse`, material validation (an untouched Rare, the Epic tier rule), FS label after the name.
- [x] **6. Tuning.** *(done 6 Oct 2026, with Reforge)* Settings section `tuning`, `RequestTune`, `ProgressionState.renown` and its spend path.
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
- Starting Player Guide, Steam Community (grades, enchant glow bands, break threshold, luck gear, pets, mounts, CP titles): https://steamcommunity.com/sharedfiles/filedetails/?id=852102476
- 12Sky wiki, Enchanting (materials, the −3% fail, breaking from 60%, Protection Charm and Lucky enchant, 100 Luck = 1%) and Upgrading: https://12sky.fandom.com/wiki/Enchanting · https://12sky.fandom.com/wiki/Upgrading
- 12 Sky 2 blog, Upgrading, Combining, Downgrading and Exchanging (a combine takes 12% enchant; an exchange costs only Silver on a fail): http://12sky2.blogspot.com/2009/03/guide-upgrading-combining-downgrading.html
- Twelve Sky 2 Reborn wiki, WarLord Items (combine rules): https://twelvesky2reborn.fandom.com/wiki/WarLord_Items
- Twelve Sky 2 skill sheet (Garuda's Song: 30 points, Luck 13% to 100%, 66 s to 240 s): https://12skyjp.proboards.com/thread/72/skill-formation
- Twelve Sky 2 compared with the first game (Rares glow green to 57%, blue from 60%, purple in the 90s; safe to 60%): https://www.freemmorpglist.com/Twelve_Sky_2-mmorpg.html
- The enchant success formula `103 − (current % + material %) + Luck / 100`, floor 5%, is quoted from a community wiki (vietkings, "Enchanting and Upgrading") through a search result; the page itself could not be opened.
- Twelve Sky 2 Upgrading Rares Guide, guidescroll: https://guidescroll.com/2011/10/twelve-sky-2-upgrading-rares-guide/
- God 12, Craft and Reinforcement Expansion, Steam announcement: https://steamcommunity.com/games/539650/announcements/detail/1664521145471880050
- TS2 Reborn wiki pages for Refine, WarLord Items, Requiem Items and Title Guide were unreachable (HTTP 402); their numbers come from the owner's notes and design 03 instead.
