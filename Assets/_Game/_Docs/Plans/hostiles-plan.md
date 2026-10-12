# Hostiles plan

The owner's plan for regular monsters (version 4.1, 8 October 2026) follows unchanged below. This first part records
what a review of it against the code found on 9 October 2026, and what is built. Where the two disagree, this part wins.

## As built (9 October 2026)

| Task | State |
|---|---|
| 0 Inventory | Done. Every character model in the Synty packs is on a Humanoid rig; there is no creature rig and nothing taller than 3.5 m. 31 candidates were sent to the owner as a numbered sheet. **The roster is not picked yet.** |
| 1 Make Emerald callable | Done. `EmeraldAI` and `EmeraldAI.Editor` assemblies (our files in the vendor folder, see `_ThirdParty/README.md`), `KryticalNova.Hostiles.Emerald`. Compiles for the Windows client and the dedicated server. |
| 2 Data | Done. `HostileDefinition`, `HostileZoneDefinition`, `LevelGapSettings` (data only), `HostileStatCurve` with edit-mode tests. |
| 3 Combat link | Done. `HostileBrainLink`, `KNEmeraldPlayerBridge` on the player prefab. |
| 4 Prefab builder | Done: **Krytical Nova ▸ Hostiles ▸ Build Hostiles**. |
| 5 GM base set | Done: `gm.hostile.list / spawn / clear / kill / info / hp / freeze / ignore / ttk / stats`, `gm.zone.list / fill / clear / respawn / sleep / bruiser`. |
| 6 Networking | Done and checked on a host with a second client: same position, health, level, combat state and death; brain and NavMeshAgent off on the client; a client that walks away stops receiving the Hostile. |

| 7 Hostile Zone runtime | Done: `HostileZone`, `HostilePool` (pooled on every peer through a Netcode prefab handler), `RespawnQueue`, sleep and wake, the map-wide cap. A kill and a respawn create nothing. The 300-Hostile soak has not been run. |
| 8 Zone01 zones | Done: eight zones at the plan's positions (none had to move), `HostileZoneBuilder` and its rules, the level on the map labels, a notice on walking in. |
| 9 Leash and aggro | Done: attack on sight and fight back only; a Hostile 15 m past its zone's edge gives up and is healed; one whose target steps onto neutral ground gives up too; a zone that falls asleep ends every fight in it. The plan's "out of reach for 6 s" rule is left to Emerald's own chase limit. |
| 10 Level-gap XP, Squad XP, death penalty | Done: `LevelGapSettings`, `LevelGapRules`, `HostileKillRewards`; the Squad boost is hooked up; a Hostile above a player is harder to hit (`IHitChanceModifier`). |
| 11 Nameplates | Done: `HostileNameplateController`. Checked: two clients at different levels see different colours on the same Hostile. |
| 12 Comic combat effects | Done through the existing game feel: a Hostile's blow is a comic hit with its number for the player struck; a heavy attack shows a "!" and a swing ribbon for its windup. No new effects were made. |
| 13 Bruisers | Done: Hellion (12) in the SE zone and Colossus (21) on Island North, at the zone's edge, with their own timer, six times the XP, five times the Creds and a 25 % gear drop of the best rarity their level has. Lucky Drops do not exist as an item yet. |
| 14 Drops and kill credit | Done: `HostileLoot`. Creds drop as a stack of cash (item 9040, `LootRules.CredsItemId`); picking it up pays the wallet and never uses a Pack slot. Drops are pooled (`WorldDropPool`). `gm.hostile.loot` simulates kills. |
| 15 Magnet | Done: `MagnetController`, `MagnetRules`, in the Autopilot window. |
| 16 Autopilot | Done: `PlayerAutopilot`, `AutopilotController`, `AutopilotWindowController`, the Autopilot Chip (item 9030), the AUTO tag. Checked for 45 s in a zone, not for the plan's 30 minutes. The hunt radius is not drawn on the ground. |
| 17 GM Tools window | Done: a Hostiles tab with every command. |
| 18 Roster | Done from the owner's picks of 9 October 2026: eight species and two Bruisers. Names are proposals. Every species shares the unarmed animation set. |

**Corpses (owner, 9 October 2026):** a dead Hostile lies for 3 s, burns away with the Fade Burn of the All In 1 Shader
Nodes pack (`HostileDissolve.shader`, `HostileCorpse`), and goes back to the pool. Numbers in `HostileSettings.asset`.

**Bosses** are out of this plan altogether: the owner builds them as a separate system.

**Where what was built differs from the plan below:** Magnet's filter is tick boxes only (one per gear rarity, Stims, materials and other items), with no typed name lists (owner, 9 Oct 2026); a
Squad's drop is held for the one player who holds the kill (a drop can be held for one player only); Autopilot's
attack skills and buffs are chosen as Hotbar slots; the Autopilot Chip's icon comes from the icon library, not from a
Synty drone picture; the numbers of section 10 are replaced by `HostileStatCurve` (correction 1).

## Corrections to the plan below

1. **Damage formula (section 10).** The game's formula is `max(1, Attack × skill − Defense)` plus Elemental, and the chance
   to hit is `Hit / (Hit + Evasion)` held between 10 % and 95 %. The plan assumed half of Defense. The stat table of
   section 10 is therefore not used. `HostileStatCurve` instead describes the player a Hostile of each level is made for
   (stat points, gear of the expected rarity) and how the fight should go (5 hits to kill, 25 to die, the player lands
   90 %, the Hostile 75 %); the Hostile's stats follow. The XP, Creds, aggro, speed and respawn formulas are the plan's.
   A tier multiplies the damage that gets through, not the raw Attack.
2. **Level-gap hit modifier.** `CombatCalculator` had no input for it: it takes one now, and `HostileNet` supplies it.
3. **Emerald behaviours** are Passive, Coward and Aggressive. Passive never fights. A species that only fights back is
   Aggressive and holds players as Neutral; one that attacks on sight holds them as Enemy.
4. **Aggro from damage** waits 0.6 s inside Emerald. The link sets the target itself, at once.
5. **Health.** `CombatEntity` raises `Killed` before `Damaged`, and `Damaged` on an evaded swing too. Emerald would also
   kill a Hostile on its own. The link gives Emerald each hit from a pool that leaves it exactly where the `CombatEntity`
   is, and tells it of a death only when the `CombatEntity` dies.
6. **Clients.** The Emerald parts are switched off in the prefab and switched on by the link where the game is decided.
   A client's Animator does not fire events (the attack clips carry the event that makes Emerald strike).
7. **Emerald on a client** still creates its pool object and its combat text canvas in `Awake`. Both are inert.
8. **Pooling on clients** needs an `INetworkPrefabInstanceHandler`: `HostilePool` registers one per Hostile prefab.
9. **Pet XP** listened to `CombatEntity.AnyKilled` and paid whoever struck the last blow. A Hostile kill now pays the pet
   of the holder of the first hit (`PlayerPet.ServerCreditHostileKill`).
10. **Assemblies.** The game is one assembly joined by `.asmref` files; only the Emerald side is an assembly of its own.
11. **Autopilot Chip icon.** There is no `ComicIconBaker`; icons are given through the Icon Library (`CLAUDE.md`).
12. **Models.** No pack ships attack, hit or death clips. The stand-in uses Synty Base Locomotion, Emerald's demo unarmed
    attacks and hit reactions, and the player's death clips, all copied so the vendor clips' own events never fire.
13. **Smaller facts.** 86 `gm.*` commands existed, not 83. Teal and grey are the accent and muted colours of the Hegemony
    skin only. Twelve Sky's early species sit at levels 1, 5, 9, 13, 16, 19 and 22.

---

# Krytical Nova — Hostiles Plan (regular monsters)

**Version 4.1 — 8 October 2026.** This is the complete plan and replaces every earlier version. Checked against the repo at commit `6ec5bdf`.

New in 4.1, from your answers: kill credit goes to the **first hit**; Autopilot runs on time from an **Autopilot Chip**, an item only GM tools can give for now, with its own cyberpunk icon (8.6); levels 1–7 fight back only and 10+ attack on sight is confirmed.

New in version 4:
- **Emerald AI on Netcode for GameObjects is no longer an open problem.** Section 3 is now a decided design: Emerald stays in its vendor folder, and the game gets its own Hostile prefabs built from only the Emerald parts it uses, with Netcode components on them.
- **Autopilot and Magnet (auto hunt with auto loot) are now fully in the plan** (section 8), with what Twelve Sky 2 does and two build tasks. Earlier versions only had a hook for them.
- The document was reorganised into one clean numbering. Nothing agreed earlier was dropped.

Words used here: **Hostile** = regular monster. **Bruiser** = field champion (not a boss). **Zone01** = the map. **Hostile Zone** = one hunting ground inside a map. **Autopilot** = auto hunt. **Magnet** = auto loot. Bosses are out of scope; you plan a separate system for them.

## The plan in brief
- **Brain and networking:** Emerald AI is the brain and runs only where the server runs. Each species gets one prefab of our own with a NetworkObject, a NetworkTransform, the game's existing networked `CombatEntity` for health, and a small `HostileNet` script. Clients switch the Emerald parts off and only show the result.
- **Where Hostiles live:** in Hostile Zones, one species and one level per zone, never mixed, never inside the town. Zone01 has eight zones for levels 1 to 22, starting outside the north gate and ending at the portal.
- **Level gap:** names are white around your level, purple at 5+ above, red at 10+ above (XP up to 175%), then teal, blue and grey below (no XP at 10+ below). Every colour is one the game already uses.
- **Rewards:** XP and drops go to the player who lands the first hit, never to whoever was noticed first. No Sweep cap and no counter-attack. Death while hunting costs 5% XP. Squad XP uses the boost that is already built.
- **Autopilot and Magnet:** a client-side helper that hunts inside one Hostile Zone, uses Stims, keeps buffs up and picks up the drops your filter allows. It presses the same buttons a player would, so the server needs no new powers. It runs on time from an Autopilot Chip, which only GM tools give out until you decide how players get it.
- **GM tools:** about 30 new `gm.*` commands and a Hostiles tab in the GM Tools window, built early so every later task can be tested.
- **Still to do first:** classify your real monster models (task 0). No roster exists yet.

## What was checked

| Area | Finding | Status |
|---|---|---|
| Repo | Readable at commit `6ec5bdf`. Docs, combat, networking, UI colours, Zone01 terrain, GM tools, pickup and the Emerald package were read. Monster models were not classified | Partly verified |
| Emerald AI in the repo | `Assets/_ThirdParty/Emerald AI`, 157 scripts, namespace `EmeraldAI`, version 1.3.3 or later. Used today only as the source of one demo animation | Verified (repo) |
| Emerald and Netcode | Emerald has no networking code and no official Netcode for GameObjects support. Nothing in its AI components looks for a local player or camera, so it runs on a server as it is | Verified (repo, Asset Store) |
| Assemblies | Emerald has no assembly definition, so the game's own code assemblies cannot call it today. Section 3 fixes this first | Verified (repo) |
| Game combat | `CombatEntity` is already a networked health component with `Damaged`, `Killed` and `AnyKilled` events; `CombatHostility` already treats anything that is not a player as hostile | Verified (repo) |
| Interest culling | `SpatialInterestManager.RegisterEntity` already shows and hides networked objects per client | Verified (repo) |
| Zone01 | 1,000 × 1,000 m; town wall a 220 m square; four empty mob-area anchors at (±150, ±150); a river cuts off the North Island, where the portal is | Verified (repo) |
| UI colours | All six name colours exist in the game today (section 10.1) | Verified (repo) |
| GM tools | 83 `gm.*` commands (Quantum Console, host or server only) and an 11-tab GM Tools window. No Hostile commands yet | Verified (repo) |
| Pickup and Stims | `WorldDrop`, `PlayerLoot.RequestPickup`, the pickup key, and Stims usable from Pack and Hotbar are built. Nothing automatic exists | Verified (repo) |
| Twelve Sky 2 level gap | 10+ below = grey, no XP; within 10 = normal; 10+ above = red. Death costs 5% XP | Verified (your transcription) |
| Twelve Sky 2 auto hunt | A timed Auto Hunt Scroll with skill, buff, monster-count and pickup-list settings, plus a separate Auto Pill with HP and Chi thresholds | Verified (official guide, player guide); details partly **inferred** |
| Twelve Sky 2 placement | First monsters at the town gate, one level band per map, caves denser and aggressive. No source maps spawn positions | Partly verified; layout **inferred** |
| Stat formulas, respawn timers, XP percentages | Not published for Twelve Sky 2 | **Inferred**, to tune in play |

---

## 1. Repo status

### 1.1 What the plan builds on
- **Docs:** `Assets/_Game/_Docs/Reference/12Sky/01_Systems_Build_List.md` (row 6 Hostiles, row 9 Autopilot, row 30 level and death), `03_Full_Transcription.md`, `05_Economy_And_Social.md`, `Design/06_pve_grind.md`.
- **Combat:** `CombatEntity`, the game's `IDamageable`, `CombatCalculator`, `CombatHostility`, `KillingBlow`, `PlayerCharacterProgression.GrantExperience`, `PartyRules`.
- **Networking:** `SpatialInterestManager`, `GameplayPrefabsSetup.SpawnedPrefabs` (today: the world drop and the Kiosk stand).
- **World:** `StarterTownBuilder.Outskirts.cs` (mob-area anchors and their map labels), `ZTP_Zone01` (terrain profile), `Zone01Setup` (the town is neutral ground).
- **UI:** `PlayerNamecardController`, the `.nameplate` styles, `WorldMarkerPresenter` (hostile target ring), `CombatFeedbackManager`.
- **Items:** `WorldDrop`, `PlayerLoot`, `LootRules`, Stims on the Hotbar.
- **GM:** `Assets/_Game/Tools/Debug/*Commands.cs` and `GmToolsWindow`.
- **Project rules (`CLAUDE.md`):** code goes in `Assets/_Game/<System>/` under an assembly definition; vendor packages stay untouched in `_ThirdParty`; tunable numbers go in a settings asset, never in code; a prefab the server spawns must be listed in `GameplayPrefabsSetup.SpawnedPrefabs`; test with a second client.

### 1.2 Monster models: not classified yet
The Synty folders in the repo are PolygonDarkFantasy, PolygonDarkFortress, PolygonElvenRealm, PolygonFantasyHeroCharacters, PolygonFantasyKingdom, PolygonGeneric, PolygonKnights, PolygonSamurai, PolygonSamuraiEmpire, PolygonSciFiCity, PolygonSciFiSpace, PolygonSciFiWorlds, PolygonVikings and SidekickCharacters, with the animation packs Base Locomotion, Bow Combat, Emotes and Taunts, and Idles. No character in them has been sorted into Hostile, Bruiser or boss-scale. Task 0 does that with this rule:

1. **Rig:** read `Humanoid` or `Generic` from the model's import settings. Humanoid models can share animation clips; generic ones need their own.
2. **Boss-scale:** taller than 3.5 m at scale 1, or named Giant, God, Queen, Golem, Dragon, Titan, Lord or King, or on Synty's Big Rig with no sensible scale-down. These are tagged `BossCandidate` and kept out of every Hostile Zone.
3. **Animations:** list any clips that ship with the pack. Synty character packs usually ship none.

---

## 2. Emerald AI reference

**Version and compatibility.** Emerald AI 2025 was a full rewrite. The listing is now "Emerald AI 2026", v1.3.4, released Sep 14, 2026, compatible with Unity 6000.0.21f1 and URP. The scripting define is `EMERALD_AI_2024_PRESENT`. Unity 6.6 isn't listed explicitly. The copy in the repo has the Cover and NavMesh Link components, which arrived in 1.3.0 and 1.3.3, so it is 1.3.3 or later; no version number is stamped in its scripts.

**Component architecture.** Required: Behaviors, Combat, Detection, Movement, Health, Animation (via Animation Profile), Sounds. Optional: Cover, Debugger, Decal, Events, Footsteps, Inverse Kinematics, Items, Location Based Damage, Optimization, Sound Detector, Target Position Modifier, UI, Weapon Collision, NavMesh Link. Tools: Setup Manager (builds an AI from a model), AI Duplicator (copies a configured AI onto other models), Animation Viewer (scrubs clips and creates events), Faction Manager, Combat Text Manager.

**Behaviours and wander.** Behaviours: Aggressive, Cautious, Passive. Wander types: Dynamic, Waypoints, Stationary, Destination. Normal Hostiles use **Aggressive + Dynamic** (wander radius = spawn-area radius) or **Passive** for species that only fight back. Bruisers use Aggressive + Stationary or short Dynamic.

**Detection.** An AI sees targets on its configured Detection Layers and reacts according to its Faction Relation. The player needs the right tag/layer and a player faction. Since 1.2.0, field of view only applies during first detection; in combat the AI has 360° detection. Keep line-of-sight obstruction checks off for open-field mobs (cheaper and more predictable) and on in Runs.

**Combat.** Attacks are Ability Objects (melee, AoE, projectile, heal, summon) with Pick Types, Cooldown Modules and Condition Modules. Emerald's Knockback and Slowed modules overlap with your Knockback Resist stat. Turn them off on Hostiles and apply knockback through the game's own combat code, so resist rules stay in one place.

**Animation Profiles and events.** An Animation Profile holds all clips and auto-builds the Animator Controller, and can be shared between AIs on the same rig. Use **Copy Animation Profile**, not a manual duplicate. Each attack clip needs an attack event at the hit frame. Attack Transform names must match exactly between the event string and the AI's Attack Transform list.

**Health and death.** The Health component holds Starting Health, Heal Rate (out of combat) and Immortal. Death uses an animation or a ragdoll; use **death animations** (cheaper and deterministic on the server). API helpers include `KillAI`, `ResetAI` and `InstantlyRefillAIHealth`; check exact signatures in your imported version.

**Damage both ways.** To hurt an AI: Emerald's `IDamageable.Damage(int DamageAmount, Transform AttackerTransform, int RagdollForce, bool CriticalHit)` (the signature in the installed copy). To let Emerald hurt your player: put a class derived from `EmeraldPlayerBridge` on the player and override its damage/death methods to call your own combat code.

**UI and optimization.** Emerald's UI component assumes one local camera, so build Hostile nameplates in the game's own UI. The Optimization component turns AI off when the renderer isn't visible. **On a headless dedicated server nothing is ever visible, so remove or disable Optimization on server instances.**

**NavMesh.** Emerald uses NavMeshAgent. Bake with Unity's AI Navigation package (`NavMeshSurface`) per zone.

---

## 3. Hostile prefabs: Emerald AI on Netcode for GameObjects

**Decision:** Emerald AI stays untouched in its vendor folder. The game builds its **own Hostile prefabs** from the Emerald parts it actually uses and puts Netcode components on those prefabs. The server runs the Emerald brain; clients switch it off and show what the server sends. There is nothing left to find: every piece below either exists in the repo or is a small script in the task list.

### 3.1 Why this works
- Emerald is plain Unity scripts with no networking of its own. Read on the server, nothing in its AI components looks for a local player, a camera or a screen. The only parts that do are its combat text, its UI component and its health bar, and this plan uses none of them.
- Emerald runs all of its components from one `Update` in `EmeraldSystem`. Switching the Emerald components off on a client stops the whole brain there.
- The game already has what Netcode needs for a creature: `CombatEntity` replicates health and death, and `SpatialInterestManager` decides which clients see which objects.

### 3.2 First step: let the game's code see Emerald
Emerald has no assembly definition, so Unity compiles it into the default assembly. The game's code lives in its own assemblies (`KryticalNova.Runtime` and others), and those cannot call the default assembly. Until this is fixed, no game script can reference Emerald.

- Add one assembly definition for Emerald's runtime scripts and one for its editor scripts (assembly-reference files in its scattered `Editor` folders). These are new files beside the vendor code; no vendor script is edited. Record them in `Assets/_ThirdParty/README.md` so a future Emerald update does not lose them.
- Put every script that touches Emerald in one small game assembly, `KryticalNova.Hostiles.Emerald`. The rest of the game talks to Hostiles through `CombatEntity` and `HostileNet` and never sees an Emerald type. If Emerald is ever replaced, only that one assembly changes.
- That assembly must compile for the dedicated server (some game assemblies are excluded from server builds; this one must not be).

### 3.3 What goes on each Hostile prefab
One prefab per species, generated by an editor builder from the species' `HostileDefinition` into `Assets/_Game/Gameplay/Hostiles/Prefabs/`, and added to `GameplayPrefabsSetup.SpawnedPrefabs` by the same builder.

| Part | Comes from | Active on |
|---|---|---|
| Model and Animator, using the controller Emerald generates from the species' Animation Profile | Synty model, Emerald profile | Server and clients |
| `NetworkObject`, `NetworkTransform` (server-owned, position and facing only) | Netcode | Server and clients |
| `CombatEntity`: health, death, damage events. **This is the one source of truth for health** | Game, already built | Server and clients |
| `HostileNet`: species id, level, zone id, animation state, attack windup message | New, small | Server and clients |
| `EmeraldSystem` and its seven required components (Animation, Behaviors, Combat, Detection, Health, Movement, Sounds), plus the `NavMeshAgent`, collider and audio source Emerald demands | Emerald | **Server only.** Switched off on clients |
| `HostileBrainLink`: joins `CombatEntity` to Emerald (3.4) | New, small | Server only |

**Emerald parts the game does not use, so they are never added:** UI, Optimization, Items, Footsteps, Decals, Inverse Kinematics, Cover, Sound Detection, Location Based Damage, Weapon Collision, the demo content and the integrations. Emerald's combat text is set to Disabled in its manager; the game has its own damage numbers.

"Server" here means wherever the game logic runs: the dedicated server, the host in a hosted game, or the single player when offline. `HostileNet` turns the brain on when the object is not spawned or is on the server, and off otherwise, the same rule `CombatEntity` already uses.

### 3.4 How damage and health cross between the two systems
- **Player hits Hostile:** the player's skill resolves on the server as it does today and calls the Hostile's `CombatEntity`. `HostileBrainLink` listens to `CombatEntity.Damaged` and passes the same amount to Emerald's `Damage`, which plays the hit reaction and makes Emerald target the attacker. After each hit it writes the real health back with Emerald's `UpdateHealth`, so the two can never drift.
- **Hostile dies:** `CombatEntity.Killed` fires (this is also where XP and drops hang). The link calls Emerald's `KillAI` so the death animation plays.
- **Hostile hits player:** the player prefab gets `KNEmeraldPlayerBridge`, a subclass of Emerald's `EmeraldPlayerBridge`. Its `DamageCharacterController` override runs the hit through `CombatCalculator` with the Hostile's stats and applies it to the player's `CombatEntity`. Emerald's own damage number is ignored; only its timing is used.
- **Finding players:** Emerald looks for the tag `Player` on the layers in its detection mask. The builder sets the mask to the layer the player prefab is on, and the validator fails if they do not match.
- **Reuse:** Emerald resets itself when a dead AI is re-enabled, which is exactly what pooling does. The link also calls `CombatEntity.ResetHealth`.

### 3.5 What clients receive
- **Position:** `NetworkTransform`.
- **Health and death:** `CombatEntity`, already replicated.
- **Animation:** Emerald drives about 35 animator settings; clients need eight of them. On the server `HostileNet` copies those eight from the Animator into one small replicated value (move speed, in-combat flag, attack index and trigger, hit trigger, dead and death index, stunned), and on clients it writes them into the same Animator Controller. No second controller and no NetworkAnimator.
- **Attack warning:** when an attack starts the server sends one short message with the attack index and windup time, so clients can show the telegraph before the hit lands.
- **Who sees it:** the Hostile is registered with `SpatialInterestManager`, the same as other networked objects.

### 3.6 Things to prove early (tests, not unknowns)
- On a server with no screen, the Animator must be set to always animate, or Emerald's attack events never fire. Tested in task 4.
- Emerald creates one shared pool object the first time any AI wakes. Harmless on clients; confirmed in task 6.
- Emerald's Knockback and Slowed modules stay off so the game's Knockback Resist rules apply in one place.
- If a later profile shows the idle Emerald components cost clients memory at high counts, clients can be given a lighter prefab through Netcode's prefab handler. This is an optimisation, not needed to ship.

---

## 4. Attack animations

**Humanoid-rig monsters:**
- **Synty ANIMATION – Base Locomotion** (in the repo) for idle/walk/run/turn.
- **Synty ANIMATION – Bow Combat** (in the repo) for ranged Hostiles.
- Melee: the repo has the Grruzam sword animation pack under `_ThirdParty`. Synty's Sword Combat pack is not in the repo.
- Fill gaps (claws, unarmed slams, hit reacts) with Mixamo or Asset Store humanoid packs. Any humanoid clip retargets through the Mecanim avatar.

**Generic-rig creatures (animals, insects, golems):** they can't use humanoid clips. Options in order of cost: a creature-specific animation pack that matches the model; author in Blender; edit in Unity with UMotion Pro; use Cascadeur.

**Minimum clip set per Hostile (Emerald Animation Profile):** Idle, Idle-Combat, Walk, Run, Turn L/R, 2–3 attacks (1 fast, 1 heavy telegraphed, optional AoE), Hit React ×2, Death ×1–2, optional Stunned loop (for Lockup).

**Event setup:** in the Animation Viewer, scrub to the contact frame → create the attack event → enter the Attack Transform name → Apply Changes. Set Loop Time **off** on attack clips. Enable Read/Write on the FBX. Use NavMesh-driven movement (not root motion) for all Normals.

**Comic-style readability, kept fast:**
- Anticipation: 0.20–0.35 s windup for Normal light attacks, 0.5–0.8 s for heavy/Bruiser attacks, with a client-side flash triggered by `AttackWindupClientRpc`.
- Impact frames: on hit, the client freezes the victim pose for 2–3 frames (≈50–80 ms), shows an onomatopoeia burst and an impact flash. This is visual only; the server timeline never pauses.
- Use stepped playback on attacks only, so locomotion stays smooth.

---

## 5. HP, response time and respawn

**HP bars and nameplates:** a Hostile nameplate in the game's own UI Toolkit HUD (client only), built on the existing `.nameplate` / `.nameplate-name` styles in `UI/NPC/NpcInteraction.uss` so it matches NPC plates (dark plate, 1 px black text outline). It reads health from the Hostile's `CombatEntity` and level and species from `HostileNet`. The **name and level take the level-gap colour from section 10.1**; the HP bar keeps one colour so the name is the only thing that changes with the gap. Bruisers add a badge. Show the plate only while the Hostile is targeted, damaged within the last 5 s, or within 15 m.

**Fast response:**
- Aggro from damage is immediate (the Damage call sets the attacker as target). Proximity aggro should land within 0.15–0.30 s.
- Area sleep/wake instead of per-monster culling: each Hostile Zone checks "any player within wakeRadius (≈ areaRadius + 40 m)" at 2 Hz. When none are found, all its Hostiles have EmeraldSystem/NavMeshAgent/Animator disabled.
- Leash: a server component checks every 0.5 s. If the Hostile is more than 15 m past its Hostile Zone's edge (section 6), or the target has been out of reach for 6 s: clear the target, return to spawn, refill health and ignore damage until it's back.
- No Sweep cap: a player may pull as many Hostiles as they can survive. Twelve Sky's 5-monster limit is not used.

**Respawn (event-driven, pooled):**
1. On death, the server fires the kill event (XP/loot hooks), keeps the corpse visible for 3 s, then calls `NetworkObject.Despawn(false)` and returns the object to the pool.
2. The Hostile Zone pushes `(respawnAtTime, slotIndex)` into a **min-heap**. One scheduler update on the server only looks at the top entry of each active area's heap, so the cost per frame doesn't depend on how many monsters are dead.
3. When a slot is due: take a pooled instance → pick a random point inside the Hostile Zone at least 3 m from any player (never a fixed spot, so nobody can camp a spawn) → `NavMeshAgent.Warp` → apply `HostileDefinition` stats → `ResetAI` → `Spawn()` → register with `SpatialInterestManager` so only nearby clients receive it.
4. Density caps: each Hostile Zone has `maxAlive`. The map session has a hard `maxActiveHostiles` (start at 300). Pools are prewarmed to `maxAlive` per prefab.

---

## 6. Hostile Zones (placement)

**The rule:** every Hostile belongs to a Hostile Zone. A Hostile Zone holds one species at one level. Nothing spawns outside a zone, and no zone is inside or against the town.

**Rules the validator enforces (menu: Krytical Nova ▸ World ▸ Validate Hostile Zones, also run by the Zone01 build):**

| Rule | Value (tunable) |
|---|---|
| Species and level per zone | Exactly one `HostileDefinition`. A Bruiser may share a zone if it is at most 2 levels above the zone |
| Distance from the town wall | Zone edge at least 20 m from the wall; no zone inside the neutral-ground box |
| Distance from roads, bridges, the portal, player spawn points | Zone edge at least 12 m from a gate road and 25 m from the portal pad |
| Gap between zones | At least 35 m between zone edges, so a chasing Hostile turns back before it reaches another zone |
| Ground | Every spawn point on the NavMesh, out of water |
| Level | A zone's level lies inside its map's band |
| Bosses | No `BossCandidate` definition in any zone |
| Networking | Every Hostile prefab is listed in `GameplayPrefabsSetup.SpawnedPrefabs` |

**How Hostiles stay in their zone:**
- Wander radius is the zone radius. A Hostile that is pulled more than 15 m past its zone's edge, or whose target is out of reach for 6 s, drops the target, walks back and refills its health.
- A Hostile also drops its target the moment that player steps onto neutral ground (the town). Hostiles never path through a gate.
- Respawns appear at a random point inside the same zone.

**Zone01 layout (levels 1–22).** Positions are map coordinates in metres with the town centre at (0, 0) and north up. They come from the terrain profile `ZTP_Zone01`: the wall is at ±110, the north river runs at about y = 206 to 250, the east river at about x = 228 to 262, and the portal is at (262, 362) on the North Island.

| Hostile Zone | Position | Level | Behaviour | Max alive | Bruiser |
|---|---|---|---|---|---|
| North Gate (new) | (−50, 157), west of the gate road, radius 25 | 1 | Passive: fights back only | 10 | — |
| NW (existing `mob-nw`) | (−150, 150) | 4 | Passive | 12 | — |
| SW (existing `mob-sw`) | (−150, −150) | 7 | Passive | 12 | — |
| SE (existing `mob-se`) | (150, −150) | 10 | Aggressive | 12 | Level 12, at the outer edge |
| NE (existing `mob-ne`) | (150, 150) | 13 | Aggressive | 12 | — |
| Island West (new) | about (−120, 310) | 16 | Aggressive | 14 | — |
| Island North (new) | about (40, 380) | 19 | Aggressive | 14 | Level 21, at the outer edge |
| Portal Approach (new) | about (330, 300), past the portal | 22 | Aggressive | 14 | — |

- **The route:** step out of the north gate into level 1, circle the town anticlockwise through levels 4, 7, 10 and 13, arrive back near the north road, cross the bridge to the North Island for 16 and 19, and finish at level 22 beside the portal to the next map.
- The strip between the north wall and the north river is only about 96 m deep, so the level 1 zone is smaller (radius 25) and the wall rule is 20 m. Every other zone has a 30 m radius.
- The North Gate and corner positions are exact and clear every rule on paper. The three island positions are approximate: the builder settles them with the same clearance check it uses for ruins, and the validator has the final say.
- The map is 1,000 m across, so eight zones use well under a tenth of the ground outside the walls. There is room to add second zones for busy levels later.
- Levels 1 to 7 only fight back and levels 10 and up attack on sight (confirmed). This follows Twelve Sky, where low field monsters wait to be pulled and at least one level-9 species is tagged aggressive.

**What players see:**
- Map and minimap labels read "Hostile Zone NW · Lv 1" (the labels exist; the level is new).
- The existing zone toast shows the zone name, level and species when a player walks in.
- Every Hostile in a zone has the same name colour for a given player, so the colour of one tells you the colour of all.

**Other maps** follow the same rule: one Hostile Zone per species in the band table (section 10), placed so levels rise with distance from that map's entry point, with the band's Bruisers at the outer edge of the zone nearest their level.

**Runs (caves).** Twelve Sky maps also have cave portals where monsters are packed closer and all attack first. That is your Run system and is not built here. A Run can reuse `HostileZoneDefinition` with aggressive behaviour and a higher maxAlive.

---

## 7. Combat and reward rules

| Rule | Where it comes from | In this plan |
|---|---|---|
| **No Sweep cap, no counter-attack** | Your decision. The only Twelve Sky mention is one sentence on help page 15 ("will be counterattacked if the # lured monsters is more than 5"), with no detail anywhere | A player may pull as many Hostiles as they can survive. Pulling a crowd and killing it together stays as a way to play |
| **Kill credit: first hit** | Your decision | The first player to damage a Hostile tags it. The tag holder gets the XP and owns the drops, however much damage anyone else adds. Being noticed or chased by a Hostile is not a hit and tags nothing. A Squad counts as one player: any member's first hit tags it for the Squad, which then shares XP by the Squad rule in 10.1. The tag ends when the Hostile dies or resets at its leash. So that nobody can tag a whole zone and walk away, the tag is also released if its holder deals no damage for 10 s, dies or leaves the zone (my addition, tunable). Other players see a small mark on the nameplate of a Hostile that is tagged by someone else |
| **Hostile drops** | Your transcription; `05_Economy_And_Social.md` | Gear, Stims and Creds drop through the existing `PlayerLoot.ServerPlace` and `WorldDrop`. Creds per kill are tuned against the income target in `EconomySettings` |
| **Drop ownership** | `WorldDrop` already holds an item for its dropper for 20 s | A Hostile's drop is held for the player or Squad with kill credit for the same 20 s |
| **Pet XP** | `12_pet_evolution_system.md`: 0.04 per Hostile kill | The kill event calls the existing pet hook |
| **Contracts** | Build list | The kill event carries the species id so kill counts work later |
| **XP and drop multipliers** | Sponsor and Recruit (help page 19), faction castles (`06_pve_grind.md`), events | One server-side XP multiplier and one drop multiplier that those systems and GM commands can set. The systems themselves are not built here |
| **PvP while hunting** | Only the town is neutral ground | Outside the walls another faction can attack you. A death to a player does not cost the 5% hunting penalty |
| **One mini boss per map** | Steam player guide | Covered by Bruisers: Zone01 has two |

---

## 8. Autopilot and Magnet (auto hunt with auto loot)

**Status in the game today:** not built. The build list names them (row 9, "To build") and says Twelve Sky has no auto hunt; that is true of the in-game help pages you transcribed, but the official Twelve Sky 2 Classic guide and player guides describe one in detail. The pieces Autopilot needs already exist: click-to-move, skills and Stims on the Hotbar, and item pickup.

### 8.1 How Twelve Sky 2 and its relatives do it

| Feature | What the sources say | Source |
|---|---|---|
| Access | Needs an Auto Hunt Scroll. Durations stack. Sold by the Medic NPC in each main castle; versions of 5 hours, 7, 15 and 30 days; new players get a free 7-day one | Official Classic guide; Steam player guide |
| Attack setup | You register a multi-strike skill and an area skill, and set a monster count (the guide sets it to 1) | Steam player guide |
| Buffs | Two buffs can be registered; up to 8 with a separate Auto Buff scroll | Steam player guide; official guide |
| Pickup | Lists of item names marked "Acquired Item / Item Not Acquired". Twelve Sky M has "Item grade to pick up". Players turn off common items so the bag does not fill | Steam player guide; TwelveSky M help; 12Sky M guide |
| Auto Pill | A separate switch with HP and Chi thresholds, usually 60%. Uses pills on the skill bar first, then the bag | Steam player guide; 12Sky M guide |
| Selling while hunting | A Proxy Shop keeps your shop open in town while you grind | Steam player guide |
| Hunt radius | A range in metres shown as a circle on the ground | Metin2 Auto-Hunt (comparable game) |
| Level filter | Only targets monsters within a level difference, default ±15 | Metin2 Auto-Hunt |
| Courtesy mode | Never targets a monster another player is already fighting | Lineage II Essence (comparable game) |
| What stops it | Entering a peaceful zone and teleporting switch it off | Lineage II Essence |

Not found for Twelve Sky 2: its hunt radius, what happens on death or a full bag, and whether it runs in dungeons. The Twelve Sky 2 Reborn wiki has an Auto Hunt Guide that could not be opened. Where Twelve Sky is silent, the design below borrows from the two comparable games in the table.

### 8.2 How it works in Krytical Nova
- **It runs on the player's own game, not on the server.** Autopilot presses the same buttons a player would: move here, use this skill, use a Stim, pick that up. Every one of those already goes through the server's normal checks, so Autopilot can do nothing a player cannot.
- **The server keeps three things** in a small `PlayerAutopilot` component (a new row in `PlayerSystemsSetup`): whether it is on, which Hostile Zone it was switched on in, and how much Autopilot time is left. It replicates the on/off flag so other players see an "AUTO" tag on the namecard.
- **It only runs while you are connected.** Closing the game stops it.
- **It never acts faster than a person:** a short delay of 0.3 to 0.5 s sits between actions.

**What it does each cycle, in order:**
1. Use a Stim if HP or Flux is under its threshold.
2. Recast any registered buff that has run out.
3. If a drop the Magnet filter wants is within the hunt area, walk to it and pick it up.
4. Pick the nearest allowed Hostile, walk into range and fight with the registered skills.

### 8.3 Settings (saved per character)

| Setting | Default | Notes |
|---|---|---|
| Hunt area | The whole Hostile Zone | Or a circle of 10–40 m around where you switched it on, drawn on the ground |
| Skip Hostiles someone else has tagged | On | They would give you no XP or drops (section 7) |
| Skip grey names | On | They give no XP |
| Skip red names | On | Too dangerous to fight unattended |
| Include Bruisers | Off | |
| Attack skills | Two slots | One single-target or multi-hit skill, one area skill; the basic attack fills the gaps |
| Gather count | 1 (range 1–8) | How many Hostiles to pull before using the area skill. This is a player choice, not a rule; there is no Sweep cap |
| Buffs | Two slots | Recast when they expire |
| Auto Stim | HP 60%, Flux 60% | Uses Stims on the Hotbar first, then the Pack |
| Magnet: Creds | Always | |
| Magnet: gear | Uncommon, Rare and Epic ticked; Common not | One tick box per rarity (as built) |
| Magnet: Stims and Lucky Drops | On | |
| Magnet: materials and other items | On | One tick box. The typed name lists were taken out (owner, 9 Oct 2026) |
| When the Pack is full | Keep hunting, take Creds only | Or stop |
| When Stims run out | Stop | Or keep hunting |
| If a player attacks you | Switch off and sound an alert | Or fight back |

### 8.4 What switches it off
Dying (you respawn in town and it stays off), leaving the Hostile Zone, entering the town, using a portal or Recall Tag, clicking to move, running out of Autopilot time, or pressing its key again.

### 8.5 Magnet on its own
Magnet also works with Autopilot off. While you play by hand it picks up anything your filter wants as soon as you are within normal pickup reach, without walking. The server's pickup rules do not change: reach, the 20 s ownership hold and the faction rule all still apply.

### 8.6 Access: the Autopilot Chip
How players will get Autopilot is undecided. Until you decide, it works like this:

- **The item:** an **Autopilot Chip**, a consumable in the same family as your Skill Chip and Kiosk Permit, and the counterpart of Twelve Sky's Auto Hunt Scroll. Using one adds 5 hours of Autopilot time to the character. Time from several chips adds up, as Twelve Sky's scroll durations do.
- **Only GM tools give it.** It is in the item catalog, so the existing `item.give` command hands it out and `gm.drop` puts one on the ground. No shop sells it, no Hostile drops it and no Contract rewards it. Adding one of those later is a data change, not new code.
- **How it is built:** a new row in `UtilityItemGenerator` (id 9030, which is free in its 9000 range), stack of 99, Uncommon. A new item-use effect, added to the end of `ItemUseEffect`, is handled by `PlayerAutopilot`, the same way a Pet Ration is handled by the pet. The time left is saved with the character.
- **Time only runs while Autopilot is on** (my assumption; the other choice is real time from the moment the chip is used). With no time left, Autopilot will not start and tells the player it needs an Autopilot Chip.
- **Magnet on its own stays free** and needs no chip (my assumption).

**The icon (cyberpunk).** Your Stims and Kiosk Permits show a text label instead of an icon today; the Autopilot Chip gets a real one:
- **Glyph:** the camera-drone icon from Synty's Sci-Fi Cyber City set, already in the repo (`ICON_SM_Prop_Camera_Drone_01_SciFiCyberCity`), or `ICON_SM_Prop_Drone_01_SciFiCyberCity` if you prefer its shape. I could not view either image (the repo stores them as large-file pointers), so pick by eye.
- **Style:** baked through your existing `ComicIconBaker`, the tool that makes the skill icons, so it gets the same ink outline, cel highlight and Ben-Day dots. Fill colour is the Colour Bible's Neon Cyan `#4FE3F0`, the game's tech accent, on the Uncommon item frame.
- **Reuse:** the same glyph marks the Autopilot button on the HUD and the AUTO tag on namecards, so players learn one symbol.

### 8.7 Limits
- Autopilot works in Hostile Zones only. Runs and contested PvP areas are off at first; this is a per-map setting.

---

## 9. Twelve Sky 2 findings

**From your own transcription in the repo:**
- Help page 18, level gap: character 10+ above the monster → dark grey name, no XP. Within 10 levels → pink, normal. Monster 10+ above → red, dangerous. Death while hunting costs 5% XP; no penalty from Master 33 up.
- Help page 17, Squad: max 5, within 5 levels to join, XP bonus only on the same map (+20/30/40/50% for 1–4 extra members). The game as built uses within 10 levels and a boost that tops out at +20% (`PartyRules`).
- Help page 15, hunting: lure several monsters and kill them together; "each player can gather up to 5 monsters and will be counterattacked" above that. Not used here (section 7).

**From the official Twelve Sky 2 Classic leveling guide:**
- Levels 1–20 hunt on the outskirts of the faction castle, with a new species roughly every 3–4 levels.
- Field Champion monsters are much stronger than normal ones and give uncommon items or Fortune Pouches. This is the Bruiser.
- In dungeons all monsters attack first.
- Later bands are separate maps with monsters exactly 3 levels apart: 49 to 67, 70 to 88, 91 to 103, 106 to 112.
- Auto Hunt needs an Auto Hunt Scroll (section 8).

**From the Steam "Starting Player Guide"** (a 2017 player guide, since removed from the Steam community, so lower reliability):
- You kill your first one or two monsters at the town gate, then move to the next species.
- Every map has mini bosses and three portals; cave monsters are denser and aggressive "so you don't need to pull them".
- Hunting spots are named by species and map corner, which suggests each species keeps to its own part of a map.
- Auto Hunt Scroll and Auto Pill settings (section 8).

**Not found, so inferred here:** a map of spawn positions, respawn timers, spawn counts, aggro radii, HP and damage formulas, and XP percentages inside the level bands. A community monster list (guidescroll.com) and the Twelve Sky 2 Reborn wiki's Auto Hunt Guide could not be opened.

---

## 10. Level scaling

**Expected-gear rarity curve:** `r(L) = 1.0 + 0.7 × (L / 112)` → 1.00 at L1, 1.35 at L56, 1.70 at L112. Prime levels P (1–33): `L_eff = 112 + P`, `r = 1.70 + 0.30 × (P/33)`.

**Normal Hostile base stats (level L):**
- HP = `40 + 21 × L × r(L)`
- ATK = `8 + 2.6 × L × r(L)`
- DEF = `1.2 × L × r(L)`
- Hit Rate = `2.0 × L × r(L)`
- Evasion = `0.30 × L × r(L)`
- XP = `XPToNext(L) / (40 + 6 × L)` → ~46 even-level kills per level at L1, ~340 at L50, ~712 at L112 (solo)
- Creds = `round(3 + 1.1 × L × r(L))`, ±20% roll, 70% drop chance
- Aggro radius (aggressive species) = `min(12, 8 + L/28)` m; Passive = 0
- Move: walk 1.8 m/s, run `4.2 + 0.006 × L` m/s
- Respawn = `clamp(8 + 0.12 × L, 8, 22)` s

**Sanity check (assumed damage model):** player hit ≈ weapon ATK − 0.5 × Hostile DEF. At L50: ≈ 302 per hit vs 1,418 HP ≈ 4.7 hits. At L100: ≈ 748 vs 3,453 HP ≈ 4.6 hits. Re-run this against the real formula in `CombatCalculator` in Task 2.

| L | r(L) | HP | ATK | DEF | Hit | Eva | Creds | Aggro m | Respawn s |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 1.006 | 61 | 11 | 1 | 2 | 0.3 | 4 | 8.0 | 8 |
| 10 | 1.063 | 263 | 36 | 13 | 21 | 3 | 15 | 8.4 | 9 |
| 20 | 1.125 | 513 | 67 | 27 | 45 | 7 | 28 | 8.7 | 10 |
| 30 | 1.188 | 788 | 101 | 43 | 71 | 11 | 42 | 9.1 | 12 |
| 40 | 1.250 | 1,090 | 138 | 60 | 100 | 15 | 58 | 9.4 | 13 |
| 50 | 1.313 | 1,418 | 179 | 79 | 131 | 20 | 75 | 9.8 | 14 |
| 60 | 1.375 | 1,773 | 223 | 99 | 165 | 25 | 94 | 10.1 | 15 |
| 70 | 1.438 | 2,153 | 270 | 121 | 201 | 30 | 114 | 10.5 | 16 |
| 80 | 1.500 | 2,560 | 320 | 144 | 240 | 36 | 135 | 10.9 | 18 |
| 90 | 1.563 | 2,993 | 374 | 169 | 281 | 42 | 158 | 11.2 | 19 |
| 100 | 1.625 | 3,453 | 431 | 195 | 325 | 49 | 182 | 11.6 | 20 |
| 112 | 1.700 | 4,038 | 503 | 228 | 381 | 57 | 212 | 12.0 | 21 |
| P10 (122) | 1.791 | 4,629 | 576 | 262 | 437 | 66 | 243 | 12.0 | 22 |
| P33 (145) | 2.000 | 6,130 | 762 | 348 | 580 | 87 | 322 | 12.0 | 22 |

**Tier multipliers (applied to the Normal values):**

| Tier | HP | ATK | DEF | Hit | Eva | XP | Creds | Drops | Respawn | Per area |
|---|---|---|---|---|---|---|---|---|---|---|
| Normal | ×1 | ×1 | ×1 | ×1 | ×1 | ×1 | ×1 | standard table | formula above | 6–14 |
| Bruiser | ×8 | ×1.6 | ×1.5 | ×1.2 | ×1.2 | ×6 | ×5 | Lucky Drop 15%, Uncommon+ gear 25% | 8–15 min (random) | 0–1 |

Bosses are not designed here. The tier list keeps a reserved Boss value that every Hostile Zone rejects.

### 10.1 Level-gap rules

Gap = Hostile level − player level. The player sees the colour for their own level, so two players can see the same Hostile in different colours.

| Gap | Name colour | Hex | XP | Creds and gear drops | Player hit-rate modifier |
|---|---|---|---|---|---|
| +10 or more | **Red** | `#FF5C52` | 175% (cap) | 100% | −15% |
| +5 to +9 | **Purple** | `#C880FF` | 125%, 135%, 145%, 155%, 165% | 100% | −5% |
| −2 to +4 | **White** | `#E6EDF5` | 100% | 100% | 0 |
| −3 to −5 | Teal | `#72DEC4` | 97%, 80%, 61% | 75% | 0 |
| −6 to −9 | Blue | `#6EAAFF` | 37%, 22%, 13%, 8% | 25%, no gear | 0 |
| −10 or lower | Grey | `#92A2B2` | 0% | none (Contract items still drop) | 0 |

**Where each colour comes from in the game today:**

| Colour | Hex | Already used for | Defined in |
|---|---|---|---|
| White | `#E6EDF5` | Name of a player with no faction | `PlayerNamecardController.NoFactionColor` |
| Purple | `#C880FF` | Epic item names | `.rarity-text-3` in `UI/Inventory/Items.uss` |
| Red | `#FF5C52` | Name of a Callout opponent | `PlayerNamecardController.DuelColor` |
| Teal | `#72DEC4` | The UI accent (frames, selected tabs, Squad HP) | `.kn-accent` in `UI/Common/KryticalTheme.uss` |
| Blue | `#6EAAFF` | Uncommon item names | `.rarity-text-1` in `UI/Inventory/Items.uss` |
| Grey | `#92A2B2` | Muted text | `.kn-muted` in `UI/Common/KryticalTheme.uss` |

**Notes on the colours:**
- White, purple and red are the three you specified. Teal, blue and grey for weaker Hostiles are my addition so players can see XP falling off; replace them with white or grey if you'd rather have only four colours.
- Purple and blue are also the Epic and Uncommon item colours. They appear on item names and frames, never on a name over a head, so the two uses don't meet on screen.
- The Null faction's accent is a deeper red (`#D8343C`) and its players' names use it. Hostile red is the lighter Callout red, and a Hostile plate always has an HP bar, which a player namecard does not.
- The world Colour Bible (`ColorBible.cs`) holds world and sky colours only; it has no UI text colours, so none of these come from it.
- Grey and red start at exactly 10 levels, as on Twelve Sky help page 18.

**Squad XP (uses what is built):** each member in the same map as the kill gets `baseXP × gapMod(member) × (1 + PartyRules.ExperienceBoost(squadSize, membersInSameMap))`. That boost is already written and tested and reaches +20% for a full Squad of 5 together; it only needs hooking up. XP is not split. Anti-carry rule: the gap is computed per member, and if the highest member's gap is 6 or more levels worse than a member's own, that member uses the highest member's gap. Squads already require members within 10 levels at join.

**Death penalty (closes build-list row 30):** lose 5% of current-level XP on death while hunting, never dropping a level; no penalty from Prime 33 up, in Callouts or in PvP.

**Zone level bands (3-level species steps):**

| Zone | Band | Species levels (one Hostile type each) | Bruiser level |
|---|---|---|---|
| Zone01 (exists) | 1–22 | 1, 4, 7, 10, 13, 16, 19, 22 | 12, 21 |
| Zone02 | 20–45 | 25, 28, 31, 34, 37, 40, 43 | 33, 44 |
| Zone03 | 45–67 | 49, 52, 55, 58, 61, 64, 67 | 57, 67 |
| Zone04 | 65–90 | 70, 73, 76, 79, 82, 85, 88 | 78, 89 |
| Zone05 (The Scar, PvP contested; TS2: Yanggok Valley) | 88–105 | 91, 94, 97, 100, 103 | 98, 105 |
| Zone06 | 103–112 | 106, 109, 112 | 112 |
| Prime grounds | P1–P33 | every 3 Prime levels | per ground |

Each species level in this table is one Hostile Zone (section 6). With 3-level steps, a player at the bottom of a band sees the next two species in white, the one after in purple, and the top of the band in red.

---

## 11. Implementation plan

### 11.1 Data (settings assets)
- `HostileDefinition`: id, display name, tier (Normal, Bruiser, BossReserved), level, stat multipliers, model, Emerald Animation Profile, behaviour, attack set with windup times, loot table id, and `isBossCandidate` (must be false).
- `HostileStatCurve`: the formulas in section 10 as editable numbers.
- `HostileZoneDefinition`: id, display name, centre and radius, exactly **one** `HostileDefinition`, max alive, behaviour (passive or aggressive), wake radius, and an optional Bruiser with its own timer. There is no weighted spawn table, so a zone cannot mix levels.
- `LevelGapSettings`: the rows of table 10.1 as data (gap range, name colour, XP percentage per level, drop modifier, hit-rate modifier), plus the death-penalty percentage and the Prime level it stops at. No colour or percentage is written in code.
- `AutopilotSettings`: the limits behind section 8 (action delay, radius range, gather-count range, default thresholds, where it is allowed, hours per Autopilot Chip, whether time runs only while Autopilot is on).
- `LevelGapSettings` also holds the kill-credit tag timeout (10 s).

### 11.2 Code
Where it goes: `Assets/_Game/Gameplay/Hostiles/` (`Runtime/`, `Data/`, `Editor/`, `Tests/`, namespace `KryticalNova.Hostiles`), the Emerald-facing scripts in their own assembly `KryticalNova.Hostiles.Emerald`, zone assets with the map in `Assets/_Game/World/Zones/Zone01/`, Autopilot in `Assets/_Game/Gameplay/Autopilot/`, GM commands in `Assets/_Game/Tools/Debug/`.

| Script | Runs on | Job |
|---|---|---|
| `HostileNet` | Server and clients | Species, level, zone, animation state, windup message; switches the brain on or off |
| `HostileBrainLink` | Server | Joins `CombatEntity` and Emerald (section 3.4) |
| `KNEmeraldPlayerBridge` | Server | Lets Emerald hit players through the game's combat rules |
| `HostilePrefabBuilder` | Editor | Builds each species' prefab and registers it for network spawning |
| `HostileZone` | Server | One hunting ground: its Hostiles, respawn queue, sleep state, leash edge |
| `HostileSpawnManager` | Server | Owns the zones, the pools and the map-wide cap |
| `LevelGapRules` | Shared, testable | Colour, XP, drop and hit-rate modifiers for a gap |
| `HostileKillRewards` | Server | Kill credit, XP, Squad share, drops, pet XP, the kill event |
| `HostileRates` | Server | The XP and drop multipliers |
| `HostileNameplateController` | Client | Name and level in the gap colour, HP bar, Bruiser badge |
| `PlayerAutopilot`, `AutopilotController`, `MagnetController` | Server flag; client logic | Section 8 |
| `HostileZoneValidator` | Editor | The placement rules in section 6 |

### 11.3 Hooks for systems not built here
- A kill event with species id, level, tier, position, the credited player or Squad, each member's gap and XP, and the drop modifier. Contracts, pets and faction bonuses subscribe to it.
- `IHostileQuery`: nearest Hostile by filter (zone, tier, level gap, not already fought by someone else). Autopilot uses it; the boss system can too.
- `IBossSpawnProvider`: a placeholder so the boss system can add its own areas later. Nothing boss-specific is built.

### 11.4 Performance targets and tests
- Starting targets: 300 active Hostiles per map session, 2.5 ms or less of AI and pathfinding per server frame at 30 ticks a second, 25 kbit/s or less per client with 60 Hostiles in view.
- Tests: a 300-Hostile soak for 30 minutes; 20 clients in one zone; a kill-and-respawn cycle with no new allocations; a screenless server firing attack events; leash and zone edges; level-gap colours and XP against table 10.1; a Bruiser timer surviving zone sleep.

### 11.5 Ordered task list (for Claude Code)
Every task from 5 on is tested on a host with a second client, using the GM commands in 11.6.

0. **Inventory** — Classify every character model in the Synty folders listed in 1.2. *Accept:* every model classified; boss-scale list reviewed by you; a first roster with one species per Hostile Zone.
1. **Make Emerald callable** — Add the assembly definitions in 3.2; create `KryticalNova.Hostiles.Emerald`; confirm Emerald runs on Unity 6.6 and URP. *Accept:* the project compiles for client and dedicated server; a script in the new assembly can reference `EmeraldSystem`; no vendor script was edited; Emerald's demo scene still runs.
2. **Stat curves and data assets** — Create the assets in 11.1 and an editor preview of the section 10 table using `CombatCalculator`'s real formula. *Accept:* an even-level kill takes 4 to 6 hits for levels 1–112.
3. **Combat link** — `HostileBrainLink` and `KNEmeraldPlayerBridge`. *Accept:* offline, the player damages and kills a Hostile through `CombatEntity`, the Hostile damages the player through Hit Rate, Evasion and Critical, and the two health values never differ after any hit.
4. **Prefab builder and first Hostile** — `HostilePrefabBuilder`; one humanoid species with an Animation Profile and attack events. *Accept:* the builder produces the prefab in 3.3 from a definition with no hand edits; attacks land on the event frame, including on a screenless build; the death animation plays; pooled reuse works 50 times in a row.
5. **GM tools, base set** — The commands marked task 5 in 11.6. *Accept:* each works offline and as host, is refused on a joined client, and appears in `gm.help`.
6. **Networking the prefab** — `HostileNet`: brain off on clients, animation state, windup message, interest registration. *Accept:* host and second client see the same position, health, animation and death for a GM-spawned Hostile; on the client every Emerald component and the NavMeshAgent are disabled; the prefab is in `GameplayPrefabsSetup.SpawnedPrefabs`; a client that walks away stops receiving it.
7. **Hostile Zone runtime** — `HostileZone`, pools, respawn queue, density caps, sleep and wake. *Accept:* the 300-Hostile soak passes; respawn timing is within 0.1 s; nothing is instantiated after warm-up; a zone with no player nearby sleeps and wakes with its timers intact.
8. **Zone01 Hostile Zones** — The eight zones of section 6 in the Zone01 builder; the validator; levels on map labels; the zone toast. *Accept:* the validator passes; breaking each rule in turn (mixed species, a zone 10 m from the wall, two zones overlapping, a spawn point in water) fails it with a clear message; walking the whole map finds no Hostile outside a zone and none inside the walls.
9. **Leash and aggro** — *Accept:* proximity aggro within 0.3 s in aggressive zones; passive zones only fight back; a Hostile pulled 15 m past its zone edge returns and heals; a Hostile chasing a player into town drops the target at the gate; a Hostile from one zone never reaches another; one player can pull a whole zone with no cap.
10. **Level-gap XP, Squad XP, death penalty** — `LevelGapSettings`, `LevelGapRules`, `PartyRules.ExperienceBoost` hooked up, 5% death loss. Update rows 6 and 30 of the build list and take the 5-Hostile cap out of rows 6 and 9. *Accept:* edit-mode tests pass for every gap from −12 to +12 against table 10.1, including both sides of each boundary (−10/−9, −6/−5, −3/−2, +4/+5, +9/+10); XP never exceeds 175% and is 0 at −10 and below; Squad sizes 1–5 with members on and off the map; death takes 5%, never a level, and nothing from Prime 33.
11. **Hostile nameplates** — *Accept:* at gaps −10, −6, −3, 0, +5 and +10 the name shows grey, blue, teal, white, purple and red with the exact colours in table 10.1; two clients at different levels see different colours on the same Hostile; the colour updates when the player levels up; a Hostile tagged by another player shows the tagged mark; changing a colour in `LevelGapSettings` changes it in game with no code edit; 60 visible plates cost under 0.5 ms.
12. **Comic combat effects** — Windup telegraph, hit-stop, onomatopoeia through the existing GameFeel system. *Accept:* a telegraph appears before every heavy attack; no server timing changes.
13. **Bruisers** — *Accept:* a Bruiser spawns at the outer edge of its zone, survives zone sleep, and gives Bruiser rewards.
14. **Drops and kill credit** — `HostileKillRewards`, `HostileRates`. *Accept:* a player who pulls a Hostile but deals no damage gets no XP and no drop; of two solo players hitting one Hostile, the one who hit first gets all the XP and owns the drop for 20 s, even if the other dealt more damage; a first hitter who stops attacking for 10 s loses the tag to the next player who hits; a Squad shares; `gm.hostile.loot` over 1,000 simulated kills lands within 5% of the expected Creds per kill and reports Creds per hour against the `EconomySettings` income target.
15. **Magnet** — `MagnetController` and its filter panel. *Accept:* with Magnet on, walking over a wanted drop picks it up and an unwanted one stays; another player's held drop is never taken; a full Pack follows the chosen setting; filters persist after relog.
16. **Autopilot** — `PlayerAutopilot`, `AutopilotController`, the settings panel, the namecard tag, the Autopilot Chip item, its use effect and its icon. Correct build-list row 9, which says Twelve Sky has no auto hunt. *Accept:* left alone for 30 minutes in a level-appropriate zone the character keeps hunting, uses Stims at the thresholds, keeps both buffs up, loots by the filter and never leaves the zone; it never targets a Hostile another player is fighting; each stop condition in 8.4 switches it off; a second client sees the AUTO tag; `item.give` hands out an Autopilot Chip, using it adds 5 hours, two chips add 10, and the time survives a relog; with no time left Autopilot refuses to start and says why; the chip shows its baked drone icon (not a text label) in the Pack, on the Hotbar and on the ground; with it on, the server log shows no request a manual player could not have sent.
17. **GM Tools window** — A Hostiles tab and an Autopilot group in `GmToolsWindow`. *Accept:* every command in 11.6 is reachable from the window.
18. **Roster build-out** — *Accept:* all eight Zone01 zones populated from the task 0 roster; no boss-scale model in any zone.

### 11.6 GM tools for testing
Same pattern as the existing tools: Quantum Console commands, host, server or offline only, mirrored as buttons in the GM Tools window. "Target" means the Hostile under the target ring. None of these exists yet; the names are proposals.

| Command | What it does | Lands with task |
|---|---|---|
| `gm.hostile.list` | Lists every Hostile definition: id, name, level, tier | 5 |
| `gm.hostile.spawn <id> [count] [level] [health\|inf]` | Spawns Hostiles at the cursor, outside any zone, optionally at another level (0 = the species' own) and with that much health instead of the species', or `inf` for one that cannot die (12 Oct 2026: `HostileNet.SetGmHealth`, for that life only) | 5 |
| `gm.hostile.clear [radius]` | Removes GM-spawned Hostiles (all, or within a radius) | 5 |
| `gm.hostile.kill [radius]` | Kills the target, or every Hostile within a radius, with normal XP and drops | 5 |
| `gm.hostile.info` | Target's definition, level, HP, AI state, current target, zone, distance to zone edge | 5 |
| `gm.hostile.hp <value or %>` | Sets the target's health | 5 |
| `gm.hostile.freeze [on/off]` | Stops all Hostile AI (they stand still and take hits) | 5 |
| `gm.hostile.ignore [on/off]` | Hostiles never notice you, for walking through zones | 5 |
| `gm.hostile.ttk <id>` | Works out hits to kill and hits to die against that Hostile with your current stats | 5 |
| `gm.zone.list` | Every Hostile Zone: id, level, species, alive / max, awake or asleep, seconds to next respawn | 7 |
| `gm.zone.fill <id>` / `gm.zone.clear <id>` | Respawns every slot now, or empties the zone | 7 |
| `gm.zone.respawn <seconds or default>` | Overrides respawn time everywhere, for fast testing | 7 |
| `gm.zone.sleep <id> [on/off/auto]` | Forces a zone asleep or awake | 7 |
| `gm.tp <zone name>` | Already exists: the zones are teleport points | 8 |
| `gm.zone.show [on/off]` | Draws each zone's edge and leash line on the ground in game | 8 |
| `gm.zone.validate` | Runs the placement validator and prints every failure | 8 |
| `gm.hostile.aggro [radius]` | Makes every Hostile in a radius attack you, to test big pulls and the leash | 9 |
| `gm.gap <hostile level>` | Prints the name colour, XP %, drop modifier and hit-rate modifier you would get | 10 |
| `gm.gap.table` | Prints the whole level-gap table as the game has it loaded | 10 |
| `gm.xprate <x>` / `gm.droprate <x>` | Sets the server XP and drop multipliers | 10, 14 |
| `gm.deathpenalty [on/off]` | Turns the 5% loss off while testing | 10 |
| `gm.zone.bruiser <id>` | Spawns that zone's Bruiser now, or prints its timer | 13 |
| `gm.hostile.loot <id> <kills>` | Simulates that many kills at your level and prints XP, Creds and drops per kill and per hour | 14 |
| `gm.hostile.stress <count>` | Fills zones and spawns extras up to a total count, for the soak test | 7 |
| `gm.hostile.stats` | Live counts: active, asleep and pooled Hostiles, AI time per frame, bytes per second per client | 7 |
| `gm.magnet.test` | Lists every drop in reach and whether your filter would take it | 15 |
| `item.give "Autopilot Chip" [count]` | Already exists as a command: gives Autopilot Chips, the only way to get them for now | 16 |
| `gm.autopilot [on/off]` | Switches Autopilot for your character without needing a chip | 16 |
| `gm.autopilot.time <minutes>` | Sets the Autopilot time left | 16 |
| `gm.autopilot.log [on/off]` | Prints what Autopilot is doing each cycle and why it stopped | 16 |

Already in the game and used by these tests: `gm.level` to move your own level across a gap boundary, `gm.god`, `gm.oneshot`, `gm.speed`, `gm.slowmo`, `gm.pvp`, `gm.xp`, `gm.kill` and `gm.tp` (the Hostile Zones are teleport points).

**In the Editor:** each Hostile Zone draws its radius, leash line and a label (level, species, max alive) in the Scene view, tinted with the level-gap colour for a preview level you type in, so a whole map's level spread can be checked at a glance.

---

## 12. Open questions for you
1. **How players get Autopilot Chips:** still to be decided by you (free, earned or sold). Nothing but GM tools gives them until then.
2. **Autopilot limits:** Hostile Zones only at first (assumed), or also Runs?
3. **Below-level colours:** keep teal, blue and grey, or show everything below white as white until it turns grey?
4. **Red Hostiles:** is 175% the right XP ceiling, and should drops also improve above your level (they stay at 100% here)?
5. **Hostile red** is the Callout red. The Null faction's name colour is a deeper red; do you want a red further from it?
6. **Bruisers:** may large models be scaled down as Bruisers, or are they all kept for the boss system?
7. **Scale:** how many players should one map session hold, and on what server hardware? This sets the 300-Hostile cap.

## 13. Caveats
- No monster model has been classified, so there is no roster yet. Task 0 comes first.
- `CombatCalculator`'s damage formula was not read. The 4-to-5-hit check in section 10 uses an assumed formula and is redone in task 2.
- The Emerald design in section 3 comes from reading its scripts, not from running it. Tasks 1, 3, 4 and 6 are where it is proven; the three checks in 3.6 are the likeliest places for surprises.
- The installed Emerald version is 1.3.3 or later by its features; the exact number is not stamped in its scripts.
- Twelve Sky 2's help pages give three colour bands and no XP percentages. The purple band, the XP bonus above your level and the falloff below are design choices to tune in play.
- No source shows where Twelve Sky's monsters stand. One level per Hostile Zone, the spacing numbers and the Zone01 route are design choices that follow your instructions.
- The Zone01 layout was worked out from the numbers in the terrain profile, not from the built scene. The three North Island zones may move.
- Three small rules are my additions and easy to change: the kill-credit tag is released after 10 s without damage from its holder, Autopilot time only runs while Autopilot is on, and Magnet on its own needs no chip.
- The two drone icons named for the Autopilot Chip were chosen by file name; the images could not be viewed.
- For auto hunt, Twelve Sky sources cover the scroll, skills, buffs, pickup lists and Auto Pill. The hunt radius, the courtesy mode and the stop conditions are borrowed from Metin2 and Lineage II.
- Respawn timers, aggro radii and stat formulas are inferred from comparable games, and the performance targets are starting assumptions.
- Colours were read from the game's stylesheets and scripts at commit `6ec5bdf`. If those change, update `LevelGapSettings` to match.

## Sources
- Repo `Ac3xCod3z/Krytical-Nova-II` at commit `6ec5bdf`: the files named in section 1.1, plus `Assets/_ThirdParty/Emerald AI/Scripts` (`EmeraldSystem`, `EmeraldHealth`, `EmeraldDetection`, `EmeraldPlayerBridge`, `EmeraldAPI`, `IDamageable`)
- [Emerald AI 2026 — Unity Asset Store](https://assetstore.unity.com/packages/tools/behavior-ai/emerald-ai-2025-268519)
- [Emerald AI wiki](https://black-horizon-studios.gitbook.io/emerald-ai-wiki)
- [Emerald AI — Damaging an AI](https://black-horizon-studios.gitbook.io/emerald-ai-wiki/api/damaging-an-ai)
- [Emerald AI — Damaging a custom character controller](https://black-horizon-studios.gitbook.io/emerald-ai-wiki/api/damaging-a-custom-character-controller)
- [Emerald AI — Creating attack animation events](https://black-horizon-studios.gitbook.io/emerald-ai-wiki/emerald-managers/animation-viewer-manager/creating-attack-animation-events)
- [Emerald AI — Release notes](https://black-horizon-studios.gitbook.io/emerald-ai-wiki/help/release-notes)
- [Netcode for GameObjects — Object pooling](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/advanced-topics/object-pooling.html)
- [Twelve Sky 2 Classic — Beginner's Leveling Guide (official)](https://12sky-help.playredfox.com/en/support/solutions/articles/8000031367-beginner-s-leveling-guide)
- [Twelve Sky 2 Classic — Starting Player Guide (Steam community)](https://steamcommunity.com/sharedfiles/filedetails/?id=852102476)
- [TwelveSky M — Game Setting (official help)](https://n-tori.oqupie.com/portals/778/articles/12602)
- [12Sky M Global AFK efficiency guide (LDCloud, a vendor blog)](https://www.ldcloud.net/blog/12sky-m-global-afk-guide)
- [Twelve Sky 2 Reborn wiki — Auto Hunt Guide (could not be opened)](https://twelvesky2reborn.fandom.com/wiki/Auto_Hunt_Guide)
- [Metin2 wiki — Auto-Hunt](https://en-wiki.metin2.gameforge.com/index.php/Auto-Hunt)
- [Lineage II Essence — patch notes on auto-use and Respectful Hunting](https://eu.4game.com/patchnotes/lineage2essence/188/)
- [Twelve Sky 2 — Wikipedia](https://en.wikipedia.org/wiki/Twelve_Sky_2)
