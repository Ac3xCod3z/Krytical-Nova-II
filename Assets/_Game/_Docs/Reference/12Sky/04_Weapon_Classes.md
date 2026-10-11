# Krytical Nova — Weapon Classes, Skill Names and Gear Looks

Twelve Sky 2 reference mapped onto the Synty packs in the project. Transcribed from the owner's `Krytical_Nova_Weapon_Classes.docx`
(4 Oct 2026) and extended with the decisions taken the same day. Naming: Krytical Nova name first, TS2 name in parentheses.

**Scope:** the nine weapon classes (three per faction), the skill structure every class copies from the Monoblade kit, all 90
skill names, the weapon meshes each class draws from, creator selection, starting gear, the look-count and grading rule, and the
GM gear generator. Grruzam assets are used for animation only; every weapon mesh is Synty.

## Decisions (owner, 4 Oct 2026)

- **The equipped weapon is the class.** A character may switch to any of its faction's three classes at any time, as long as it
  has a weapon of that class. Weapons are locked to the class's faction, never to a class.
- **Discipline** (picked in the creator) only decides the starting weapon, and the class while no weapon is equipped.
- **Skill ranks are shared by slot.** S1–S8, Stance and Guard have one rank each for all three classes of the faction; Skill Chip
  and Dossier unlocks carry over; the Hotbar swaps to the matching skills on a class change; the old class's Stance / Guard buff drops.
- Ranged classes and Servo Claws play stand-in animations until their own sets exist (see section 2). Warbow uses the Synty Bow
  Combat pack.

## 1. Classes matched to the packs

Twelve Sky 2's three factions each carry an offense / defense / ranged trio, and the stance buff follows the role: offense +15%
damage, defense +20% defence, ranged +20% dodge and move speed (design 10's Lightning Flash / Divine Protection / Soaring Phoenix).

| TS2 faction | Offense | Defense | Ranged |
| --- | --- | --- | --- |
| Guanyin (Dragon Clan) | Blade | Sword | Marble |
| Fujin (Snake Clan) | Katana | Double Blade | Lute |
| Jinong (Tiger Clan) | Spear | Light Blade | Sceptre |

Each Krytical Nova faction gets one cohesive pack family so a Hegemony sword never looks like a Covenant one.

| Faction | Offense | Defense | Ranged |
| --- | --- | --- | --- |
| **Hegemony** (Samurai + Samurai Empire melee; SciFi City / Worlds / Space guns) | **Monoblade** (TS2: Blade). Samurai Sword, Odachi, Ninjato, Tanto; Samurai Empire Sword ×2, Sword_Training. Anims: Grruzam katana set. | **Glaive**. Samurai Naginata ×2, JuMonjiYari, BoStaff, Tonfa; Samurai Empire Spear ×4, Staff ×2, Prong ×2. Anims: Grruzam great-sword. | **Carbine**. Samurai Empire Rifle ×2; SciFi City pistols, SMGs, rifles, snipers, shotguns; SciFi Worlds Assault ×3, Pistol ×6, SMG ×3, Shotgun ×3, Sniper ×3; SciFi Space Pistol / Rifle / Shotgun. |
| **Null** (SciFi Worlds alien line + Dark Fantasy / Dark Fortress) | **Harvester**. Dark Fantasy Scythe, Sickle, Cleaver; Dark Fortress Scythe, Saw; Vikings Saw; Samurai Empire Sickle. Anims: Grruzam great-sword. | **Servo Claws**. Samurai Empire Claw, Spike, Spikes; Fantasy Kingdom KnuckleDagger; SciFi Worlds Dagger ×7. Anims: needs an unarmed set. | **Arc Caster**. SciFi Worlds Alien ×4, Heavy ×3, Launcher ×3; SciFi City Rifle_Plasma ×3, Shotgun_Plasma, Sniper_Plasma, Rifle_Laser. |
| **Covenant** (Vikings + Elven Realm + Knights + Fantasy Kingdom) | **Longaxe**. Vikings Axe ×2, LargeAxe; Elven Axe ×7; Fantasy Kingdom Axe ×11. Anims: Grruzam great-sword. | **Claymore**. Knights Zweihander, Broadsword; Elven Sword ×7; Vikings Sword; Fantasy Kingdom Sword ×18 + modular blade / hilt kit. Anims: Grruzam great-sword. | **Warbow**. Synty Bow Combat pack: Recurve ×8, Longbow ×3, Compound ×3 (+ Elven Realm Bow ×2, Dark Fantasy Crossbow). Anims: Synty Bow Combat. |

**Animation gaps:** a gun aim / fire / reload set for Carbine and Arc Caster; an unarmed set for Servo Claws. Everything melee
runs on the two Grruzam sets; Warbow runs on `Assets/Synty/AnimationBowCombat`.

**Monoblade rename** (TS2 Blade): display names only. Skill ids, asset names and `BladeSkillBuilder`-era identifiers stay.

## 2. The structure every class copies

8 attacks + 2 buffs, identical per class; only the Stance stat follows the role.

| Slot | Role | Level / source |
| --- | --- | --- |
| S1 | AoE sweep, hold-to-repeat, no crit, radius 3–5 | L1 Instructor |
| S2 | single, 1.5 s cooldown | L1 Instructor |
| S3 | 3-hit, 3 s cooldown | L1 Instructor |
| S4 | heavy single | L30 Instructor |
| S5 | 3-hit flurry | L30 Instructor |
| S6 | single, +101–110% bonus | L60 Skill Chip |
| S7 | 3-hit, +101–110% bonus | L60 Skill Chip |
| S8 | Zenith technique (TS2: War God): AoE leap, hold-to-repeat, radius 5–7 | L80, its class's own Zenith Dossier (a drop of the Prime grounds; `gm.zenith`) |
| Stance | +Attack Power % (offense) / +Defense Power % (defense) / +Evasion and Move Speed % (ranged), 28 ranks | L1 |
| Guard | +Elemental Defense %, 20 ranks | L1 |

Ranged classes keep the same slots: S1 becomes a cone or volley, S8 a called-in strike, reach 8–12 instead of 2.

## 3. All 90 skill names

Monoblade's former names (TS2 Guanyin Blade) in parentheses.

### Hegemony

| Slot | Monoblade | Glaive | Carbine |
| --- | --- | --- | --- |
| S1 | Perimeter Cut (Fury Strike) | Riot Sweep | Spray Pattern |
| S2 | Clean Cut (Haeseo Stroke) | Suppression Thrust | Clean Shot |
| S3 | Signature Series (Gentleman's Pen) | Compliance Combo | Triple Tap |
| S4 | Severance (Devil's Strike) | Curfew | Headhunter |
| S5 | Hostile Takeover (Flash of Rage) | Cease and Desist | Burst Contract |
| S6 | Zero Hour (Sunray's Strike) | Lockstep | Longshot |
| S7 | Liquidation (Phantom Thunder) | Full Enforcement | Terminal Burst |
| S8 | Market Crash (Thunderous Dragon) | Martial Law | Orbital Call |
| Stance | Overclock (Lightning Flash) | Bulwark Protocol | Target Lock |
| Guard | Firewall (Azure Wall) | Shield Field | Reactive Plating |

### Null

| Slot | Harvester | Servo Claws | Arc Caster |
| --- | --- | --- | --- |
| S1 | Reaping Cycle | Shockwave Stomp | Arc Flare |
| S2 | Culling Stroke | Piston Jab | Lance Shot |
| S3 | Threshing Combo | Servo Combo | Tri-Bolt |
| S4 | Scrap Sentence | Compactor | Ion Spike |
| S5 | Hydraulic Flurry | Overdrive Cycle | Cascade Fire |
| S6 | Extinction Blow | Core Breach | Sunspot |
| S7 | Kill Loop | Three-Phase Rend | Chain Storm |
| S8 | Terminal Harvest | Zero Point | Extinction Beam |
| Stance | Kill Protocol | Lockdown Plating | Capacitor |
| Guard | Null Shell | Faraday Skin | Static Veil |

### Covenant

| Slot | Longaxe | Claymore | Warbow |
| --- | --- | --- | --- |
| S1 | Reaping Arc | Shieldwall Sweep | Arrow Rain |
| S2 | Hewing Blow | Steadfast Cut | Hunter's Mark |
| S3 | Clan Rhythm | Three Oaths | Three Feathers |
| S4 | Oathbreaker | Bulwark Strike | Heartseeker |
| S5 | Frostbite Flurry | Hearthguard Flurry | Volley |
| S6 | Skysplitter | Dawnguard | Moonshot |
| S7 | Wolfpack | Highguard Trinity | Stormflight |
| S8 | Northfall | Kingsfall | Skyfall |
| Stance | Blood Oath | Shieldwall | Windwalk |
| Guard | Ancestor's Ward | Stoneheart | Barkskin |

### Shared skills (every class, every faction)

Eight skills are not tied to a weapon: every character has the same eight under the same names. They are single
assets, so a name cannot differ by faction. Renamed 6 Oct 2026 (display text only: ids, assets and effect ids keep the
Twelve Sky names). The table is `ClassSkillBuilder.SharedSkillText`; **Krytical Nova ▸ Progression ▸ Build Class Skills** writes it.

| Krytical Nova | Twelve Sky 2 | Id | What it does | From |
| --- | --- | --- | --- | --- |
| **Redline** | Spiritual Rage | `SpiritualRage` | hold to charge; the next hit lands far harder with a Critical bonus | Instructor, level 1 |
| **Second Wind** | Heavenly Serenity | `HeavenlySerenity` | kneel and recover HP and Flux for 20 s | Instructor, level 1 |
| **Aegis** | Heavenly Shield (HS) | `HeavenlyShield` | a barrier worth 111–150 % of HP; Elemental damage passes it | Skill Chip, level 60 |
| **Killer Instinct** | Heavenly Blow (HB) | `HeavenlyBlow` | +12–75 % Critical chance | Skill Chip, level 60 |
| **Hot Streak** | Garuda's Prayer (GP) | `GarudasPrayer` | +13 % Luck at rank 1 to +100 % at rank 30, 66 s to 240 s (the TS2 slope); Luck is what improves the odds at the forge | Skill Chip, level 70 |
| **Backlash** | Heavenly Repulsion (HR) | `HeavenlyRepulsion` | 1–10 % chance to throw a physical hit back in full | Skill Chip or Dossier, level 80 |
| **Shatterpoint** | Heavenly Destruction (HD) | `HeavenlyDestruction` | 1–20 % chance per hit to shatter an Aegis and strip a buff | Skill Chip or Dossier, level 80 |
| **Iron Nerve** | Reverse Flow (RF) | `ReverseFlow` | +1–20 % Lockup resistance | Skill Chip or Dossier, level 80 |

## 4. Discipline in the creator

A Discipline step after Origin in the character creator: three cards (offense / defense / ranged for that faction) showing the
stance buff and the level-1 weapon. Stored in `CharacterRecipe.weaponClass` (`WeaponClassId`, nine values grouped by faction),
validated in `CharacterRecipeValidator`, replicated by `CharacterNetState`. `PlayerCombatSkills` offers the kits of the character's
faction instead of the hard-wired blade set; `SkillDefinitionSO.weaponClass` lets the skills window and Instructors filter.

## 5. Starting gear

Level-1 Common set of the character's own outfit pack in Armor / Gloves / Boots (no Cape: Capes exist from Rare up, owner
6 Oct 2026), the level-1 Common weapon of the chosen Discipline in the Weapon slot, 10 Stims, nothing else. Weapon lines carry the class's faction mask so a Carbine never reaches a
Null or Covenant character; Null's two body packs stay separated by `BodyFit`.

## 6. How many looks, and the grading rule

From the gear spec bands: Common 10 steps, Uncommon 5, Rare 8, Epic 5 = 28 level / rarity steps per slot (Rare gained its level-110 tier on 6 Oct 2026). Owner's rule
(4 Oct 2026): every Common tier shares one look, every Uncommon tier one look, Rare has two looks (levels 45–75 and 85–110),
Epic one. Outfit Commons are the pack's variant 01 (the basic outfit).

| Rarity | Weapon | Outfit |
| --- | --- | --- |
| Common (10) | the pack's plainest mesh (Sword_Training, Tanto, small axe, dagger, pistol) | the basic outfit (variant 01) |
| Uncommon (5) | standard mesh (Sword, Spear, Axe, Rifle) | a mid outfit per group |
| Rare (8) | ornate mesh (Odachi, Zweihander, Elven Sword, Sniper / Alien), two looks | two top outfits |
| Epic (5) | hero mesh (Odachi_Covered, Fantasy Kingdom blade, Heavy / Launcher); Temper glow band + trail VFX later | hero outfit (+ helmet attachment for skeletons) |

That is 5 look slots per class, 45 weapon lines in total. Outfits: Hegemony Soldier + Civilian, Null Robot + Skeleton, Covenant
Viking + Elven.

## 7. GM gear generator

- `gm.gear <slot> <level> <rarity> [class]` (for example `gm.gear weapon 45 rare carbine`, `gm.gear armor 100 epic`) looks up the
  baked `ItemDefinition` for that slot / level / rarity, filtered by the player's faction, body and class unless given, and puts it
  in the Pack.
- `gm.loadout <class>` wipes the Pack and re-equips the level-1 starter kit of that class.
- Both mirrored in `GmToolsWindow` as dropdowns (Slot, Rarity, Level snapped to band steps, Class) with a Give button.
- `gm.gearset [level] [rarity] [temper] [wear]` (for example `gm.gearset 112 epic 60 wear`) gives one whole set with the weapon of
  the class in use, tempered and optionally worn; `gm.temper.all <percent> [worn|pack|all]` sets the Temper of gear the player
  already has. In `GmToolsWindow` they are the "Whole Gear Set" and "Temper" sections of the Items tab.

## 8. As built (4 Oct 2026)

| Piece | Where | Notes |
| --- | --- | --- |
| Class ids | `Gameplay/Progression/Data/WeaponClassId.cs` | `WeaponClassId` (None + 9, three per faction in role order), `WeaponClassRole`, `ClassSkillSlot`. Never renumber. |
| Faction rules | `Gameplay/Items/Runtime/WeaponClassRules.cs` | which class belongs to which faction, the default (offense) class, Discipline validation. |
| Class catalog | `Items/Data/Assets/WeaponClassCatalog.asset` | per class: name, faction, role, card text, ten skills by slot; plus the shared skills. Built by `ClassSkillBuilder`. |
| Skills | `Progression/Data/Assets/Skills/<Class>Skills/` | Monoblade keeps its ten assets and ids (`BladeSkills/`). The other eight classes are copies per slot (`<Class>_<Slot>` ids) with their own names, clip set, reach and stance stat. Menu: Krytical Nova ▸ Progression ▸ Build Class Skills. |
| Active class | `Items/Runtime/PlayerWeaponClass.cs` | the equipped weapon's class, else the Discipline. Derived on every peer from the replicated loadout and identity: no network state of its own. |
| Ranks | `PlayerCombatSkills` | ranks, Skill Chip / Dossier unlocks and cooldowns are stored under the slot (`SkillDefinitionSO.RankKey`); a class skill only casts with its class's weapon in hand; a class change drops the old Stance / Guard. |
| Hotbar and skills window | `PlayerSkillHotbar`, `ProgressionHUD` | slots re-point to the same slot of the new class; the window lists shared skills plus the class in hand. |
| Discipline | `CharacterRecipe.weaponClass`, `CharacterNetState`, creator tab | chosen on the creator's Discipline tab, second in the bar: the Origin tiles become three class cards showing each class's starter weapon, with the role, the stance and the starting weapon below. Characters saved earlier read as their faction's offense class. |
| Weapons | `GearDictionary` lines, `Items/Data/Assets/Weapons/Weapon_<Class>_NN.asset` | five look templates per class (Common, Uncommon, Rare I, Rare II, Epic), 28 tiers each. |

**Weapon models.** Each look template's model is built from its Synty prefab with one convention, the one the katana grip was tuned for: grip at the pivot, long axis along +Z, broad side (blade width, a rifle's height, a bow's belly) along Y. Icons are captured looking at that broad side. The Bow Combat pack ships a Built-in pipeline material, so the bows use a URP copy in `Items/Data/Assets/Materials/`. A grip that needs a different hold is tuned on the template (`weaponLocalPosition` / `weaponLocalEuler`) and survives re-bakes; `Krytical Nova ▸ Items ▸ Rebuild Weapon Look Models` rebuilds every model from its mesh (and resets those poses).

**Paired weapons, holds and carry (as built, 6 Oct 2026).** Each weapon look says how it is held and where it goes when it is put away (`ItemDefinition`: `paired`, `offHandLocal…`, `leftHanded`, `carry`, `carryLocal…`; set per look in `SidekickGearGenerator`'s weapon table and baked onto every tier).

| Weapons | In the hands | Put away |
| --- | --- | --- |
| The Carbine's Common look (the **Sidearm**, a pistol) | **one in each hand**; the off hand is the main hand's mirror | a holster on each hip (`WeaponCarry.Hips`) |
| Servo Claws, all five looks | **one on each hand** | on the hips |
| The Carbine's rifles, Arc Caster | one weapon, held pointing forward (`Hold.Forward`), not blade-up like a sword | across the back, moved down so the muzzle does not stand over the head |
| Warbow | in the **left** hand, as Synty's bow clips draw it (`leftHanded`); a right-hand bow would need mirrored clips, which is possible per weapon but not needed | across the back |
| Monoblade, Glaive, Harvester, Longaxe, Claymore | main hand, blade up | across the back; long ones are shifted so they neither stand over the head nor reach the ground |

`WeaponVisualMount` does the work: it spawns the second copy on `OffHandSocket` (left hand), follows the draw and sheathe the animation motor already replicates, and when the weapon is on the back with no Cape worn it sits 8 cm closer in. The sockets are made by `WeaponSocketsSetup` (part of **Setup ▸ Player Prefab**). Combat is unchanged: the hit box stays on the main hand. Every look was checked in Play on a body of its own faction, sheathed, drawn and mid-attack, and every Cape on both body types with a weapon on the back.

**Clip sets.** Monoblade: Grruzam katana (measured hit timings). Glaive, Harvester, Longaxe, Claymore: Grruzam great-sword, same clip per slot letter, default hit timings. Warbow: Synty Bow Combat (`Stand_Shoot_Reload`, `Stand_Shoot_Leaping` for S8), default hit timings. Carbine, Arc Caster, Servo Claws: the Monoblade's clips as stand-ins.

**Stand-ins still to replace:** gun and unarmed animation sets (so a Sidearm or Servo Claws draw still reaches over the shoulder before the weapons appear in the hands); measured hit moments for the great-sword and bow clips; projectiles, and the cone / volley (S1) and called-in strike (S8) shapes for ranged classes (today ranged skills hit at reach 8–12 with no projectile, and their area skills are centred on the caster); one basic attack combo for every class (the katana combo); weapon grip poses per mesh (tuned on the look template).
## A skill asked for too early walks in and casts (as built, 10 Oct 2026)

An attack skill pressed when it cannot be cast yet is kept, one at a time, and cast the moment it can be (owner, 10 Oct 2026). Single-hit and multi-hit skills alike; the basic attack already walked in by itself.

- **Out of reach:** with the target no further than the approach range, the character walks to the skill's ring round the target and casts on arrival. Further than that the press is refused as out of range, as before.
- **Mid-swing or mid-cast:** the skill waits for the swing under way to play out and is cast straight after it.
- **Let go only when** the player gives another order (a click on the ground or on something else, Escape: these also stop the walk the queue started), asks for another skill (which takes its place), when either side dies, or when the target gets away or there is no way in. A click on the target itself changes nothing. A skill on cooldown or without the Flux for it is refused as before, never kept; a charged skill is held, not kept.
- **Who decides:** the owner asks (`PlayerCombatSkills.TryCastSkill` hands a press refused as `CastCheck.OutOfReach` or `CastCheck.Busy` to `ISkillQueue`); the server walks the player in with the same move order a click gives (the run is paid for once) and casts through the usual cast (`PlayerSkillQueue`, Gameplay/Combat). While the basic attack is already walking in on the same target the queue leaves the walking to it.
- **On the Hotbar** the waiting skill's slot is lit like a chosen movement skill (class `queued`).
- **Numbers** (on `PlayerSkillQueue`, the player prefab): approach range 20 m, the walk ends 0.3 m inside the skill's range, given up after 10 s, or at once when the target is 1.5 times the approach range away, or after 1.5 s with no way in.

## Hit looks (as built, 8 Oct 2026)

A class is also told by how its hits look: the mark it leaves, the font of its words and the words themselves. The
colour is the kind of hit (Normal, Elemental, Critical, Shattered, Reflected), the same for every class. Full detail,
sizes and rules: `Design/13_comic_effects.md`.

| Class | Mark | Lettering | Heavy hit | Critical hit | Signature skill (S4, S6, S8) | Blade trail |
| --- | --- | --- | --- | --- | --- | --- |
| Monoblade | a crossed cut | Shojumaru | SHING! | SLASH! | SLICE! | blue |
| Glaive | a sweep | Black Ops One | SWISH! | THWACK! | WHOOSH! | magenta |
| Carbine | a muzzle star | Bungee | BANG! | BLAM! | RATATAT! | cyan |
| Harvester | streaks of a scythe | Creepster | SHRED! | REAP! | CULL! | red |
| Servo Claws | a claw's rake | Rubik Glitch | RAKE! | REND! | RIP! | teal |
| Arc Caster | rays | Rubik Wet Paint | ZAP! | KRAKL! | BZZZT! | violet |
| Longaxe | a star | Caesar Dressing | CHOP! | KRAK! | THOOM! | orange |
| Claymore | a cloud | Berkshire Swash | KLANG! | BASH! | KRASH! | gold |
| Warbow | a ring | Jolly Lodger | THWIP! | THUNK! | TWANG! | green |

The class is the weapon in hand, so a character that changes weapon changes its marks and words with it. A new class
needs a row in `GameFeelBuilder.Comic.cs` (`ClassComics`) and **Krytical Nova ▸ GameFeel ▸ Build Comic Effects**;
`HitLookTests` fails while a class has no look of its own.

## Skill icons (as built, 8 Oct 2026)

Every one of the hundred skills has an icon of its own, from the fantasy icon pack and redrawn in the comic
treatment (`Design/Rendering-Bible.md` 6g). A class draws its ten from one set, so each class has one colour:

| Class | Set of the pack | Colour |
| --- | --- | --- |
| Monoblade | Swordman | blue |
| Glaive | Spearman | magenta |
| Carbine | Crossbowman | cyan |
| Harvester | Blood Mage | red |
| Servo Claws | Undead | teal |
| Arc Caster | Lightning Mage, its colour moved to Null's violet | violet |
| Longaxe | Berserker | red-orange |
| Claymore | Warrior | orange-gold |
| Warbow | Archer | green |
| the eight shared skills, Run, Jump | Buff (Shatterpoint: Debuff) | many |

Which icon each skill has is recorded in the icon library (`Icons/Data/Assets/IconLibrary.asset`); change one in
**Krytical Nova ▸ Icons ▸ Icon Library**. **Build Class Skills** no longer hands a new class skill another skill's
icon: it stays without one until the library gives it its own, and **Check Icons** says so.

## Sources

- 12SKY M Global class guide (LDCloud): https://www.ldcloud.net/blog/12sky-m-global-class-guide
- Twelve Sky 2 starter guide (guidescroll): https://guidescroll.com/2011/10/twelve-sky-2-starter-guide/
- Factions (12sky.fandom.com): https://12sky.fandom.com/wiki/Factions
- Twelve Sky (Wikipedia): https://en.wikipedia.org/wiki/Twelve_Sky
- Project scan of `Assets/Synty` (weapon prefab counts per pack) and `BladeSkillBuilder.cs`.
