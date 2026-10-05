# Krytical Nova — Systems Build List (12Sky / Twelve Sky 2 cross-reference)

As of 4 Oct 2026 · first scan at repo commit 9623a17 · statuses last updated 4 Oct 2026 on branch `gear-ladder-and-naming` (naming pass, gear ladder, nine weapon classes) · companion doc: "12Sky Systems Transcription & Krytical Nova Build List" (Claude doc)

The 86-second recording pages through all 31 screens of the 12Sky (Twelve Sky) in-game help; the audio is game ambience only, no narration. Statuses below come from a scan of the Krytical-Nova-II repo at commit 9623a17 (4 Oct 2026, "Hold props during prop emotes"): 11 of the 37 systems are built, 8 are partial, 18 are still to build. All 31 pages are transcribed in `03_Full_Transcription.md` with the original screenshots in `pages~/`.

**Naming convention for every file in this folder:** each system is written as **Krytical Nova name (TS2: Twelve Sky 2 name)**. Claude Code builds and names code with the Krytical Nova name; the TS2 name is there so the owner can read the doc by the name he knows. Where the 12Sky (first game) help page used a third name, it follows in square brackets.

**For Claude Code:** (1) apply the Glossary below as the renaming scheme in code, assets and UI text; (2) keep the Status column of the Master build list current as systems land (`Complete` / `Partial` / `To build` / `Candidate`); (3) the gear layer is specced in `02_Gear_System_Spec.md`.

## Glossary: Twelve Sky reference → Krytical Nova term

Every system, item, place and stat in this doc has one Krytical Nova name, chosen to fit the setting (Hegemony corporate tech, Null machines, Covenant clans, comic-book register) rather than copied. The reference column holds the Twelve Sky 2 term and, in brackets, the 12Sky (first game) term from the help pages where it differs. "In code" names what the repo calls it today; a different display name costs one string, a different identifier one rename.

| Krytical Nova term | Twelve Sky 2 reference [12Sky] | In code today | Group |
| --- | --- | --- | --- |
| **Creds** | Silver [Silver Jeon] | name approved 4 Oct 2026; used in NPC text; no field yet | Currency |
| **Renown** (RN) | Contribution Points, CP | name approved 4 Oct 2026; used in NPC text; no field yet | Currency |
| **Valor** | Class Points | — | Currency |
| **XP** | EXP | `ProgressionState` | Currency |
| **Nova Store** | Item Mall / Item Shop (real money) | Mall Manager NPC | Economy |
| **The Exchange** | Consignment [Consigner] | Trader NPC text, Exchange square in plaza | Economy |
| **Kiosk** | Private Shop / stall (proxy selling) | stall props only | Economy |
| **Vault** / **Deep Vault** | Storage per character / account Warehouse | bank building only | Economy |
| **Trade** | Trade | — | Economy |
| **Reboot** | Rebirth | — | Progression |
| **Prime levels** | Master levels (Master 1+, Deity) | — | Progression |
| **Nova Rank** (the halo is a **Nova**) | CP Reinforcement / halo bonus levels | — | Progression |
| **Titles** (Vanguard, Bulwark, Ironblood, Mindwell) | CP stat Titles L1–L12 | — | Progression |
| **Spotlight** | Hero Rank (weekly) | — | Progression |
| **Valor Rank** | Class System | — | Progression |
| **Respec Chip** / **Memory Wipe** | Stats Reset item / Skill Reset Scroll | `gm.resetstats`, `gm.dropskills` | Progression |
| **Tempering** (Temper %) | Reinforcement / Upgrade / Enchant % | — | Gear |
| **Fusion** (FS1–12) | Combination, Combine (CS1–12) | — | Gear |
| **Tuning** (Tune 1–25) | Refine / Refinement | — | Gear |
| **Slotting** + **Cores** | Item Penetration + Diamonds / gems | Agent NPC text | Gear |
| **Epic Mk I / Mk II** | WarLord 1 / 2 | — | Gear |
| **Legendary** (deferred) | Requiem | `ItemRarity.Legendary` | Gear |
| Common / **Uncommon** / Rare / **Epic** / Legendary | Common / Unique / Rare / Elite (WarLord) / Requiem | `ItemRarity` | Gear |
| Weapon, Armor, Gloves, Boots, Ring, **Amulet**, **Cape**, Pet | Weapon, Costume, Gloves, Shoes, Ring, Necklace, Cloak, Pet | `EquipSlot` | Gear |
| **Alloy I–V**, **Prime Alloy** | Upgrade stones 3/6/9/12/15 %, Millennial Gold | — | Gear materials |
| **Failsafe** / **Advanced Failsafe** | Protection Scroll / APS, Comrade Scroll | — | Gear materials |
| **Fusion Catalyst** | Combine Scroll | — | Gear materials |
| **Tuning Chip** / **Violet Chip** / **Reset Chip** | Refining Stone / Purple Stone / Chaos Stone | — | Gear materials |
| **Slot Cutter** / **Deep Cutter** / **Core Extractor** / **Slot Sealer** | Unril Stone / Plus Penetrate Scroll / Gem Removal Scroll / Socket Removal Stone | — | Gear materials |
| **Mark Stone** | WarLord Upgrade Stone | — | Gear materials |
| **Armorer** | Blacksmith [Forge] | `NpcRole.Blacksmith`, shown as Armorer | NPC |
| **Apothecary** | Herb Master [Herbal Medicine Shop] | `NpcRole.HerbMaster`, shown as Apothecary | NPC |
| **Instructor** (Blade Instructor …) | Trainer [Home of the Falchion etc.] | `NpcRole.Trainer`, shown as Monoblade Instructor / Servo Claws Instructor | NPC |
| **Monoblade**, Glaive, Carbine · Harvester, Servo Claws, Arc Caster · Longaxe, Claymore, Warbow | Blade, Sword, Marble · Katana, Double Blade, Lute · Spear, Light Blade, Sceptre (offense / defense / ranged per faction) | `WeaponClassId`, `WeaponClassCatalog`; see `04_Weapon_Classes.md` | Classes |
| **Discipline** | starting weapon class (TS2: fixed by faction and weapon choice) | `CharacterRecipe.weaponClass`; the class in use is the equipped weapon's (`PlayerWeaponClass`) | Classes |
| **Broker** | Trader [Consigner] | `NpcRole.Trader`, shown as Broker | NPC |
| **Store Clerk** | Mall Manager [Merchant] | `NpcRole.MallManager`, shown as Store Clerk | NPC |
| **Fixer** | Agent [Beggar] | `NpcRole.Agent`, shown as Fixer | NPC |
| **Marshal** | Guard Captain [Chief Guard] | `NpcRole.GuardCaptain`, shown as Marshal | NPC |
| **Chancellor** | Elder / Prime Minister [Law Enforced HQ] | Elder hall prop | NPC |
| **Vaultkeeper** | Hermit (storage, guild) | — | NPC |
| **Wayfinder** | Gate Master [Yellow Gateway Express] | Gate stall prop | NPC |
| **Herald** (new) | — (TS2 puts CP Reinforcement on the Elder) | — | NPC |
| **Outfitter**, **Stylist**, **Reshaper**, **Coach** | Tailor, (none), (none), (none) | Tailor stall prop | NPC |
| **Crew** | Guild / Clan | chat channel `Guild`, shown as Crew; `/crew` (`/g` still works) | Social |
| **Squad** | Party | `PlayerParty`, `PartyRules`, shown as Squad; `/squad` (`/p` still works) | Social |
| **Contacts** | Friend list | — | Social |
| **Sponsor / Recruit** | Mentor / Disciple (teacher / student) | — | Social |
| **Accord** | Alliance | — | Social |
| **Broadcast** / **Hailer** | Shout [Grand Force chat] / Megaphone | chat has Faction channel | Social |
| **Callout** | PvP request / Duel | `PlayerDuel`, shown as Callout; `/callout` (`/duel` still works) | PvP |
| **Siege Night** | Regular Warfare (castle battles) | — | PvP |
| **Outpost Raid** | Outpost battle | — | PvP |
| **Beacon War** | Holy Stone Battle [Sacred Stone] | — | PvP |
| **The Pit** | Coliseum | — | PvP |
| **Lockdown** | Alcatraz | — | PvP |
| **The Gauntlet** | Circuit Valley | — | PvP |
| **The Maze** | Labyrinth | — | PvP |
| **Warden** / **Lieutenants** | Faction Leader / Vice Leaders | — | Faction |
| **War Chest** | Faction Vault | — | Faction |
| **Siege Rig** | Catapult / Guild Weapon | — | Faction |
| **Beacon** | Divine Stone / Alliance Stone / Sacred Stone | — | Faction |
| **Spire** | Tower / defensive structure | — | Faction |
| **Turf War** | Territory warfare | — | Faction |
| **Faction** (Hegemony, Null, Covenant) | Faction [Grand Force] | `FactionId` | Faction |
| **Hostiles** | Monsters | `CombatHostility` | PvE |
| **Bruiser** | Field Champion | — | PvE |
| **Run** | Dungeon | — | PvE |
| **Sweep** | Battue hunt | — | PvE |
| **Autopilot** / **Magnet** | (none) auto hunt / auto loot | — | PvE |
| **Lucky Drop** | Fortune Pouch | — | PvE |
| **Contract** | Quest / Mission | — | PvE |
| **Bounty Board** | (none) | — | PvE |
| **Stim** (HP Stim, Flux Stim, Dual Stim) | Pill / Tablet (Healing, Chi, Stamina) | `Pill_*` assets, shown as HP Stim / Flux Stim / Dual Stim | Items |
| **Flask** | Calabash bottle | NPC text | Items |
| **Recall Tag** | Scroll of Return [Return Scroll] | NPC text | Items |
| **Skill Chip** | Skill Book | `requiresSkillBook`, shown as Skill Chip | Items |
| **Dossier** (4 **Pages** per Dossier) | Archive / Craft Scroll (4 shards) | `requiredCraftingArchives`, shown as Dossiers | Items |
| **Pack** | Bag / Traveler's Bag | `PlayerInventory`, window titled Pack | Items |
| **Flux** (display) | Chi (resource pool) | `MaxChi`, `PlayerResources`, shown as Flux everywhere (approved 4 Oct 2026) | Stats |
| Strength, Agility, Vitality, **Spirit** | Str, Agi, Vit, Chi (stat) | `StatType` | Stats |
| **Critical** | Deadly / Deathblow | `CriticalRate`, shown as Critical | Stats |
| **Hit Rate** | Attack Rate / Attack Success | `HitRate` | Stats |
| **Evasion** | Evade / Dodge | `EvasionRate` | Stats |
| **Elemental** | Attribution / Attribute | `ElementalAttack` | Stats |
| **Lockup** | Acupressure stun [AccuPoint] | `AcupressureStunResistance`, shown as Lockup Resist | Stats |
| **Knockback Resist** | Bounce | — | Stats |
| **Core Resist** | Socket attribute resistance | — | Stats |
| **Hotbar** | Shortcut Window (F1–F10, 1–0) | `PlayerSkillHotbar` | UI |
| **Settings** (O) | Game Option Setup (ESC) | `HudSettingsController` | UI |
| **Character Sheet** (C) | Character Status Window | `ProgressionHUD` | UI |
| **Transit** | Teleport | `ZonePortal`, `gm.tp` | World |
| **Central Plaza** (Zone01) | Saigo Temple (neutral hub) | `StarterTownBuilder.Plaza` | World |
| **Home Cities** | Faction fortresses (Kailan, LingChi, Ronyan) | — | World |

## Master build list

Status is from the repo scan: Complete = working in code, Partial = some of it exists (the "In the repo" column says what), To build = nothing beyond an NPC description or a prop, Candidate = a 12Sky system not on the owner's original list. Page numbers refer to `03_Full_Transcription.md`. **Claude Code: update the Status column as work lands.**

| # | Krytical Nova system (TS2 name) | Status | 12Sky pages | In the repo | Left to build |
| --- | --- | --- | --- | --- | --- |
| 1 | Save system (TS2: server-side, no client save) | Partial | none | Character roster and HUD prefs in PlayerPrefs (`kn.*` keys, `PlayerPrefsCharacterStore`); `ICharacterStore` is the seam for a server store; `ProgressionState` is server-held and resent on change. | Server-side persistence of progression, Pack, equipment; decide Cloud Save vs own backend. |
| 2 | Player and gear stat sheet (TS2: Character Stats Distribution) | Complete | 2, 23, 24 | STR / VIT / AGI / Spirit, AP, DP, Hit Rate, Evasion, Max HP, Max Flux, Crit Rate, Crit Damage, Elemental Atk / Def, Lockup Resist, Luck, Move Speed, plus the gear stats Critical Defense, Kill-XP Rate / Amount (Attack Speed, Core Resist, Knockback Resist reserved for Tuning). Every ladder item carries baked base and secondary stats (`GearStatTable` → `GearStatBaker`); `PlayerEquipment` applies them through `GearStatResolver`; Character Sheet and gear tooltips show them. | Skill modifiers on gear and the Kill-XP stats are stored and shown but not consumed yet. |
| 3 | Tempering (TS2: Reinforcement / Enchant %) [12Sky: Item Upgrade] | To build | 23 | Only NPC text (Broker lists items at "12% or more"). | Gear spec section 4. |
| 4 | Fusion FS1–12 (TS2: Combination / Combine CS1–12) [12Sky: Item Combine] | To build | 24 | Nothing. | Gear spec section 5. |
| 5 | Tuning 1–25 (TS2: Refine) + Slotting and Cores (TS2: Item Penetration + Diamonds) + Epic Mk (TS2: WarLord 1→2) | To build | 25, 26 nearest | `ItemStack` has room for the stage. | Gear spec sections 6, 7, 8. |
| 6 | Hostiles with AI and HP bars (TS2: monsters, level-gap rules) | To build | 15, 18 | Emerald AI is in `_ThirdParty` (only a demo animation is used); `CombatEntity` / `IDamageable` / `CombatHostility` already treat Hostiles as hostile; combat dummy; target ring; spatial interest culling counts them. | Emerald AI agents wired to `CombatEntity`, Hostile HP bar, name tint by level gap, XP / drop gating, 5-Hostile aggro cap. |
| 7 | Blade combat skills (TS2: Guanyin Blade martial arts) | Complete | 8, 10 | `BladeSkillBuilder`: Stroke, Haeseo Stroke, Sunray's Strike, Fury Strike, Devil's Strike, Phantom Thunder, Flash of Rage, Gentleman's Pen, Thunderous Dragon (war god), Lightning Flash stance, Azure Wall; plus Tier 1 self-buffs. | Done. |
| 8 | Combat skills for the other 8 weapons (TS2: weapon skill lines) | Partial | 8, 9, 10 | All nine classes exist (`04_Weapon_Classes.md`): 90 named skills in ten shared slots (`ClassSkillBuilder`, `WeaponClassCatalog`), a weapon line per class in the gear ladder, Discipline step in the creator. The class in use is the equipped weapon's; ranks, unlocks and cooldowns are shared by slot; the Hotbar and skills window follow a weapon swap. | Real animation sets for Carbine, Arc Caster and Servo Claws (they play Monoblade stand-ins); measured hit timings for the great-sword and bow clips; projectiles and cone / called-in shapes for ranged S1 and S8; per-class basic attacks; weapon grip poses. |
| 9 | Autopilot with Magnet (TS2: none; nearest Battue hunt + pickup) | To build | 15, 5, 7 | Nothing; no ground loot exists yet (`GearUIController` TODO). | Ground drops first, then pickup, then Autopilot / Magnet with the 5-Hostile cap as a tunable. |
| 10 | Nova Rank, Renown-spent (TS2: CP Reinforcement / halo) | To build | 27 | Nothing (the only "halo" in code is the sun halo in `EnvironmentPresenter`). | At the Herald: Gear spec section 9. |
| 11 | Titles + Bounty Board, Renown-spent (TS2: CP stat Titles L1–L12; bounty is ours) | To build | 28 | Nothing. | At the Herald: Gear spec section 9. Bounty Renown source still open. |
| 12 | Trade + The Exchange (TS2: Trade + Consignment) | To build | 13 | Broker described as the Exchange (Commission Receipt, 12%+ items); no code. | Player-to-player trade window (page 13 flow) and the Exchange board. |
| 13 | Item pickup (TS2: pickup key / click) | To build | 5, 7 | Pack, equip, quick-equip, sort, use, destroy all work (`PlayerInventory`); no world drops. | World drop object, pickup key and click, stack merge. |
| 14 | Vault and Deep Vault (TS2: Storage and Warehouse) | To build | 20, 21 | Bank building, booth and ATMs placed in the Exchange square; no NPC or storage. | Per-character Vault (Vaultkeeper) and account-wide Deep Vault (Wayfinder), Creds deposit. |
| 15 | Kiosks, proxy selling (TS2: Private Shop) | To build | 14 | Vendor stall props built around the plaza. | Kiosk system: name, items, prices, Start, sale messages, zone restriction, offline proxy mode. |
| 16 | Squad (TS2: Party) | Complete | 17 | `PlayerParty` / `PartyRules`: same faction, max 5, members within 10 levels (TS2 uses 5: our variance), invite / join / refuse / leave / kick / promote, Squad chat (`/squad`, `/p`), movable Squad frames. `PartyRules.ExperienceBoost` (same-map rule) is written and tested but not hooked up to any XP award, by the owner's decision. | Hook the XP boost up when Hostile kill XP lands; formation auras (Phase 4). |
| 17 | Sponsor / Recruit (TS2: Mentor / Disciple) | To build | 19 | Nothing. | Request, level gates, 1.5x XP in same area, no stacking with Squad, Sponsor points. |
| 18 | Callouts (TS2: Duel / PvP request) | Complete | 12 | `PlayerDuel`: request / accept, countdown, hostile pair only, to the death, forfeit / distance / leave rules, server authoritative; `DuelHudController`; `/duel` chat commands; `gm.duel`. | Optional: 180 s timer and Stim toggle from page 12. |
| 19 | Outfitter, Stylist, Reshaper, Coach (TS2: Tailor only) | Partial | 1 | Character creator covers lineage, body type, palettes, expressions, name rules, roster; Outfitter stall prop exists. | In-world NPCs that re-open those creator panels for Creds; gear dye. |
| 20 | Tool cleanup (no TS2 equivalent) | To build | none | GM console (`gm.*` 42 commands, `item.*`, `time.*`, `weather.*`), `GmToolsWindow`, ~30 editor builders. | Decide which builders are one-shot and which stay as tools. |
| 21 | Settings (TS2: Game Option Setup, ESC) | Partial | 4, 5 | Settings window on O: HUD toggles and a full HUD layout editor (move, resize, scale, persisted). | Graphics, audio mixer sliders, key rebinding (Input System rebind UI). |
| 22 | NPC directory and town map (TS2: Main NPC Details) | Complete | 1 | Zone01: Apothecary, Armorer, Monoblade Instructor, Servo Claws Instructor, Broker, Store Clerk, Marshal, Fixer (code roles: HerbMaster, Blacksmith, Trainer, Trader, MallManager, GuardCaptain, Agent), each with nameplate, dialogue and services text in Krytical Nova terms; plaza signs and map labels renamed (Chancellor's Hall, Hostile Zones); world map with POIs, NPCs, portals, players, waypoint and travel; minimap. | NPC services behind the dialogue (shops, Instructors). |
| 23 | Mouse and camera control (TS2: Mouse Control) | Complete | 3 | `PointAndClickPlayer`, occluder fader, jump and vault motors, target ring. | Done. |
| 24 | Hotbar (TS2: Shortcut Window) | Partial | 4, 9 | 1–0 + Q W E R slots, skills and usable items, cooldown badges, hold-to-repeat, charged skills, drag from skills window. | Rebinding, extra pages (Z / X), emote slots. |
| 25 | Chat channels (TS2: Chat) | Complete | 6 | General, Faction, Squad, Crew, Whisper, GM, System, Combat tabs; `/s /f /p /g /w /r /gm` plus `/squad`, `/crew`, `/callout`; command registry used by Callouts and Squads. | Broadcast channel; Hailer item. |
| 26 | Gear rarities and slots (TS2: Common / Unique / Rare / Elite, item slots) | Complete | 7 | `ItemRarity` Common / Uncommon / Rare / Epic (Legendary reserved, nothing generated); `EquipSlot` Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape, Pet. The gear ladder: `GearDictionary` lines (one look per run of level tiers) baked into one item per tier; Common 1–28, Uncommon 20–40, Rare 45–75 and 85–105 (two looks), Epic 100–112; six outfit / ring / amulet groups and nine weapon classes; level-gated equipping; frames white / blue / yellow / purple. Gear spec sections 2, 3 and 11. | Done. Drops and shops come with rows 6, 13 and the NPC services. |
| 27 | Skill leveling, Respec Chip, Memory Wipe (TS2: skill levels, Stats Reset, Skill Reset Scroll) | Partial | 9 | Ranks with skill points, +1 / +5 / +10, rank-scaled cost and effect; `gm.dropskills`, `gm.resetstats`. | Respec Chip and Memory Wipe as drops or Nova Store goods. |
| 28 | Siege Nights, Beacon Wars, Outpost Raids, The Pit (TS2: Regular Warfare, Holy Stone Battles, Outpost battles, Coliseum) | Candidate | 11 | Factions and cross-faction hostility exist; no battlegrounds. | Design 07 / 08 with the new names. |
| 29 | Contacts (TS2: Friend List) | Candidate | 16 | Nothing (whisper works by name). | Contacts window, online status, click-to-whisper. |
| 30 | Level, XP and death penalty (TS2: Level & Exp) | Partial | 18 | Levels to the cap of **112**, XP, stat and skill points; gear asks for its item level; death animation by blow direction, 8 s respawn, countdown in Combat tab. | Level-gap name tint, XP and drop gating, 5% XP loss on death. |
| 31 | Stims, Flasks, Recall Tags (TS2: Pills / Tablets, Calabash, Scroll of Return) | Partial | 15, 7 | HP Stim, Flux Stim, Dual Stim (3:1), usable from Pack and Hotbar (assets still named `Pill_*`); a new character starts with ten Stims; Apothecary text lists the rest. | Apothecary shop, S / M / L sizes, Recall Tag, shift-click quantity buy. |
| 32 | Nova Store, real money (TS2: Item Mall) | Candidate | 27 | Store Clerk NPC description only. | Unity IAP storefront; Renown is not spent here. |
| 33 | Crew (TS2: Guild / Clan) | Candidate | 28 | Guild chat channel; `gm.guild` stub; Guild stall prop. | Create, invite, ranks, Crew titles, expel, disband. |
| 34 | Dossier crafting (TS2: Archives) [12Sky: Craft Scrolls] | Partial | 29 | HR / HD / RF assets flagged craftable with Archive names; dev toggle unlocks them. | Dossier Pages as drops and the craft action at the Chancellor. |
| 35 | Warden, War Chest, Siege Rigs (TS2: Faction Leader, Faction Vault, Catapults) | Candidate | 30 | Nothing. | Design 08 with the new names. |
| 36 | Accord (TS2: Alliance) | Candidate | 31 | Nothing. | Design 08. |
| 37 | Transit (TS2: Teleport / Yellow Gateway) | Partial | 1, 21, 31 | `ZonePortal` zone transfers, `gm.tp` / `gm.poi`, Wayfinder stall placeholder. | Wayfinder NPC, Recall Tag item, Transit fees. |
| 38 | Factions and lore (TS2: Grand Forces) | Complete | not in 12Sky help | Hegemony / Null / Covenant, lineages, faction select, faction skins, cross-faction PvP rules, faction chat. | Faction infrastructure (design 08). |
| 39 | Character creation and roster (no TS2 equivalent) | Complete | not in 12Sky help | Lineage, body type, **Discipline** (the faction's three weapon classes), palette, expression, name rules, roster, select screen, showroom, portrait studio. | Done. |
| 40 | Networking (no TS2 equivalent) | Complete | not in 12Sky help | Netcode for GameObjects, dedicated server boot, zone sessions, spatial interest management, spawn / respawn, offline handoff. | Scaling per architecture plan Phase 4. |
| 41 | World: day cycle, weather, zones (no TS2 equivalent) | Complete | not in 12Sky help | World clock, weather catalog and blends, precipitation, lightning, wind, street lamps, grass, Zone01, portals, minimap bake. | More zones. |
| 42 | Comic rendering and game feel (no TS2 equivalent) | Complete | not in 12Sky help | `ComicStyleFeature`, Color Bible, sun shafts, hit reactions, pooled VFX, skill feel catalog. | Per the art brief. |
| 43 | Pets and mounts (TS2: Pets, Mounts) | Partial | not in 12Sky help | Cosmetic pet follower from the loadout, Pet equip slot. | Pet stats and mounts (design 04, 05; pet growth, hunger and the first four pets are specified in `Design/12_pet_evolution_system.md`). |

### Checklist view (tick as systems land)

- [x] 2 Stat sheet (gear side) · [x] 7 Monoblade combat skills · [x] 16 Squad · [x] 18 Callouts · [x] 22 NPC directory and town map · [x] 23 Mouse and camera · [x] 25 Chat channels · [x] 26 Gear rarities and slots · [x] 38 Factions and lore · [x] 39 Character creation · [x] 40 Networking · [x] 41 World · [x] 42 Comic rendering
- [ ] 1 Save system · [ ] 19 Outfitter / Stylist / Reshaper / Coach · [ ] 21 Settings (graphics, audio, rebind) · [ ] 24 Hotbar (rebind, pages) · [ ] 27 Respec Chip / Memory Wipe · [ ] 30 Level-gap rules and death penalty · [ ] 31 Stims shop and Recall Tag · [ ] 34 Dossier crafting · [ ] 37 Transit NPC · [ ] 43 Pets and mounts
- [ ] 3 Tempering · [ ] 4 Fusion · [ ] 5 Tuning / Slotting / Epic Mk · [ ] 6 Hostiles with AI · [ ] 8 Other 8 weapon lines · [ ] 9 Autopilot / Magnet · [ ] 10 Nova Rank · [ ] 11 Titles + Bounty Board · [ ] 12 Trade + Exchange · [ ] 13 Item pickup · [ ] 14 Vault / Deep Vault · [ ] 15 Kiosks · [ ] 17 Sponsor / Recruit · [ ] 20 Tool cleanup
- [ ] 28 Siege Nights / Beacon Wars / Outpost Raids / The Pit · [ ] 29 Contacts · [ ] 32 Nova Store · [ ] 33 Crew · [ ] 35 Warden / War Chest / Siege Rigs · [ ] 36 Accord

## Variance notes

For each item on the owner's list, how the Krytical Nova version differs from its Twelve Sky reference and what is worth taking or leaving.

**Saving.** Twelve Sky never saves on the client. The repo is already shaped the same way: `ProgressionState` is server-held and resent on change, and `ICharacterStore` abstracts where characters live (PlayerPrefs today, a server store later). What is missing is the server side of that seam: persist progression, Pack, equipment and position per character. Keep one serializable state object per character so a save is one write.

**Stats (TS2: Character Stats Distribution).** The Phase 1 sheet already has everything Twelve Sky shows and more (Crit Damage, Lockup Resist, Move Speed, Luck). What is left is on the gear side: the per-slot base stats and the secondary ladder in Gear spec section 3, plus seven new stats (Attack Speed, Core Resist, Knockback Resist, Critical Defense, Kill-XP rate and amount, skill-line modifiers).

**Tempering** (TS2 Reinforcement, 12Sky Upgrade). The risky path in both games: percent success that climbs, −3% on a fail, a break chance that starts at 60%, a cap of 120% without Prime Alloy. The Broker's NPC text already uses "12% or more" as the Exchange threshold, so the percent model is what the repo expects. Full rules in Gear spec section 4.

**Fusion** (TS2 Combination, 12Sky Combine). Identical in both references: 12 stages, Rare+ only, same level only, one clean material consumed. Two rules worth copying: a fail never destroys the target, and a fused item cannot be fusion material. Gear spec section 5.

**Tuning** (TS2 Refine). 12Sky has nothing like it; its closest pages are Item Exchange and Rank Up / Down. The owner's notes give the per-slot gains per stage to 25, which is the whole system; Gear spec section 6 fills in materials and odds. Slotting + Cores (section 7) and Epic Mk (section 8) round out the gear layer.

**Hostiles and AI (TS2: monsters, Field Champions).** Twelve Sky's rules are about level gap, not AI: grey name = no XP, pink = normal, red = dangerous, and drop chance falls off when you out-level a Hostile by 10+. Emerald AI handles aggro, wander and combat; you still need the level-gap tint on the HP bar and the XP / drop gate, which are game rules, not AI rules. Bruisers (field champions) are the mid-tier target with better drops.

**Autopilot with Magnet (TS2: none; nearest Battue hunt).** Twelve Sky has no auto hunt; it has the Sweep (pull up to 5 Hostiles, AoE them, more than 5 and they counter). Keep that 5-Hostile cap as a tunable so Autopilot does not pull the whole zone. Magnet has no reference either; pickup is a key or a click.

**Nova Rank and Titles (TS2: CP Reinforcement halo, CP stat Titles).** Both have a direct Twelve Sky reference: CP Reinforcement (100 CP + Silver per attempt, +1 to all four attributes per level, a new halo every 8 levels) and the CP stat Titles L1–L12 (800 to 10,000 CP). Both move to the new Herald NPC; the Nova Store stays real-money. Gear spec section 9. The Bounty Board is our own; it is a Renown source, not a title mechanic.

**Combat skills for 8 weapons (TS2: weapon martial-art lines).** The Blade line already follows the Twelve Sky slot pattern (single, multi, AoE, war-god AoE, stance buff), so the other 8 lines are a `BladeSkillBuilder` clone per weapon with new names, animations and `SkillFeelCatalog` entries. The unlock cadence (Instructor for 1st–3rd, Skill Chip at 30 and 60, Dossier for the 8th) matches the `requiresSkillBook` flag and Archive gating; Phase 5 of design 10 is the plan for it.

**Trade and the Exchange (TS2: Trade, Consignment).** Same shape as the page 13 flow: Shift+click target, request, both parties add items and Creds, both Accept, items land in the Pack. The Exchange (consignment board at the Broker) is the asynchronous half: list a Rare at 12%+ Temper with a Commission Receipt, browse, buy.

**Picking up items (TS2: pickup key / click).** Twelve Sky binds pickup to a key and to left-click on the drop; that is the manual half of Magnet.

**Vault and Deep Vault (TS2: Storage, Warehouse).** Twelve Sky has two tiers and the original list named one. Per-character Vault at the Vaultkeeper in the Home City, and account-wide Deep Vault at the Wayfinder in every town. The two-tier split is the thing to copy: the account-wide tier is what makes alt characters useful.

**Kiosks (TS2: Private Shop).** Twelve Sky's private shops are player-manned: the seller is frozen while the shop is open. Proxy selling implies the Kiosk runs while the player is away or offline, which the reference does not do; the rest (shop name, price entry, Start, sale message, no take-back once sold, zone-restricted to the Central Plaza and Home Cities) transfers as-is.

**Squad (TS2: Party).** Twelve Sky caps at 5 and requires members within 5 levels of each other, XP bonus +20 / 30 / 40 / 50% per extra member, same map only. The level-gap cap is the design choice to decide on; it stops power-leveling but kills mixed-level friend groups.

**Sponsor / Recruit (TS2: Mentor / Disciple).** Twelve Sky's Mentor: Sponsor must be Prime 1+, Recruit level 122 or below, Recruit gets 1.5x XP only when the Sponsor is in the same area, does not stack with Squad XP, Sponsor earns 250 points per minute while nearby. The non-stacking rule and the proximity requirement are the two levers that stop it being a free XP buff.

**Callouts (TS2: Duel).** Built (as `PlayerDuel`). Ours are to the death with forfeit, distance and disconnect rules; Twelve Sky's are 180 s with a knock-out win, a pre-fight Stim toggle, and arena-only. The Stim toggle and the hard timer are cheap additions if a non-lethal Callout mode is wanted.

**Outfitter, Stylist, Reshaper, Coach (TS2: Tailor; the rest are new).** Not in Twelve Sky (its Tailor only sells armor and crafts wings). The character creator already has the panels (body type, palette, expression, lineage), so these NPCs are mostly a way to reopen those panels in-world for Creds and write the result back through `CharacterRecipe`. Gear dye at the Outfitter is the only new piece.

**Settings (TS2: Game Option Setup).** Further than Twelve Sky already: the O-key Settings window has HUD toggles and a full drag / resize / scale layout editor that persists. The reference's ESC menu only picks the Hotbar mode. What is left is the three standard tabs: graphics (resolution, quality, vsync, the comic-style toggles from the Color Bible), audio (mixer sliders), and key rebinding through the Input System rebind flow so Hotbar keys, camera keys and the window keys all live in one list.

## Candidates not on the original list

Of the sixteen reference systems not on the owner's list, six are already built (NPCs and map, mouse and camera, chat, gear rarities, skill leveling, Transit via portals) and four are partial. The ten still open, ranked by how much they unblock:

1. **Ground loot and world drops** (TS2: monster drops): blocks item pickup, Magnet, Hostile drops and the whole gear-progression loop; `GearUIController` already has the TODO.
2. **Level-gap rules and death penalty** (TS2: Level & Exp; p. 18): name tint, XP and drop gating, 5% XP loss; needed before Hostiles and Autopilot make sense.
3. **Shops behind the NPC dialogue** (TS2: Herbalist, Blacksmith shops): Apothecary sells Stims, Flasks and Recall Tags, Armorer buys / sells; the dialogue panel exists, the transaction does not.
4. **Squad and Squad chat** (TS2: Party; p. 17): max 5, within 5 levels, +20 / 30 / 40 / 50% XP; Phase 4 formation auras depend on it.
5. **Contacts** (TS2: Friend List; p. 16): one-sided accept, online status, click-to-whisper; whisper already works by name.
6. **Respec Chip and Memory Wipe** (TS2: Stats Reset item, Skill Reset Scroll; p. 9): as drops or Nova Store goods; today only GM commands.
7. **Crew** (TS2: Guild; p. 28): the chat channel and `gm.guild` stub are waiting on it; Crew titles can sit beside Herald Titles.
8. **Nova Store** (TS2: Item Mall; p. 27): real-money storefront (Unity IAP) at the Store Clerk only; Nova Rank and Titles go to the Herald.
9. **Siege Nights, Beacon Wars, Outpost Raids, The Pit** (TS2: Regular Warfare, Holy Stone Battles, Outpost battles, Coliseum; p. 11): from design 07 / 08; need Squad and Crew first.
10. **Warden, War Chest, Siege Rigs, Accord** (TS2: Faction Leader, Faction Vault, Catapults, Alliance; p. 30, 31): late game, design 08.

## Assumptions and open questions

The repo answered most of the earlier questions (three factions, Spirit is the stat and the pool is Chi in code, three gear layers). Naming decisions the owner still owns, then the design unknowns:

- [x] Currency names: **Creds** (Silver) and **Renown** (CP) approved by the owner on 4 Oct 2026; the long-form design docs still say Silver and CP.
- [x] Resource pool: **Flux** approved on 4 Oct 2026 as the display name. Code keeps `MaxChi` / `PlayerResources`.
- [x] NPC display names approved on 4 Oct 2026: the roles keep their enum names (HerbMaster, Blacksmith, Trainer, Trader, MallManager, GuardCaptain, Agent) and show as Apothecary / Armorer / Instructor / Broker / Store Clerk / Marshal / Fixer. Rename depth for everything built: **player-facing text only**; identifiers, asset names and saved keys stay.
- [ ] Bounty Board: a Renown source, design still open.
- [ ] Proxy selling: should a Kiosk keep selling while the owner is offline (server-side Kiosk state), or only while the owner stands there?
- [ ] Save backend: Unity Cloud Save, own database behind the dedicated server, or flat files on the server host?
- [x] The nine weapon lines are fixed: see `04_Weapon_Classes.md`.
- [ ] "Bounce" on the Amulet: treated as Knockback Resist; confirm.
