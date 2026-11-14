# Living NPCs on Sky Concord plan (Townsfolk)

**Status: first pass built 13 Nov 2026 (PR into `items-18-24`, b18-coder-11), owner picks 1a, 2a (48 / 20), 3a, 4a.** Not yet
run in Unity: the asset builder, the EditMode tests and the Play proof are owed (see "As built").

Owner's day list 13 Nov 2026, item 18. Row: new, "Townsfolk", to add to `../Reference/12Sky/01_Systems_Build_List.md`
when step 1 lands. Sky Concord here means the Concord 040 preview that floats 300 m above Zone01's Concord
(`World/Editor/Concord040Setup.cs`).

## What it is for

Sky Concord feels like a lived-in free town. Townsfolk with their own personalities and day plans walk, work, jog, shop
and chat in small groups; the vendors notice the player, wave, beckon and call out. Every one of them is dressed from the
same Sidekick parts the players wear. It stays cheap: a few ms in total, 60 fps held with the crowd in view.

## Risk

**Low.** Townsfolk carry no items, Creds, saves or player data; the worst failure is something that looks wrong (a stuck
NPC, a pop-in, a lost frame). Medium only if a step lets the crowd cost frames or bandwidth that players feel, which is
why the budget below has its own evals.

## 1. What exists today (read 13 Nov 2026; line numbers checked)

| Thing | Where | What it does / numbers |
|---|---|---|
| The Brain core | `Gameplay/Brains/Runtime/BrainComponents.cs` L7-9 | "the shared core of every server-side mind (Hostiles first, townsfolk later)": `BrainTag`, `BrainActive`, `BrainClock` (think tier, stagger, stable id), `BrainDice`, `BrainMover` (Dest, Speed, Flags Stop/Run/Face), `BrainPath`, `BrainEvent`, `SenseWorld` (player grid). |
| Think tiers | `Brains/Data/BrainSettings.cs` L13-17 | thinks every 1 / 3 / 15 ticks within 12 / 40 / 120 m of the nearest player; tier 3 (farther) never thinks; 2 s hysteresis. |
| Paths | `BrainSettings.cs` L27-35 | 8 path searches per tick, 2,000 NavMesh nodes each, repath 0.3 s at least, 16 waypoints. Measured: PathService p99 < 0.2 ms with 8 searches. |
| Utility scores | `Brains/Runtime/Score.cs` L5-53 | response curves, product of considerations compensated for their count, early-out `CanStillBeat`. Ready for smart-spot scoring. |
| Hostile entity brain | `Hostiles/Entities/Runtime/EcsHostileBrain.cs` L38 (`Brain`), L92-101 (`SetHome`) | the managed bridge from a Hostile's server body to its brain entity. |
| Hostile mind, idle wander | `HostileDecideSystem.cs` L120-144, the wander pick L133-136 | idle for a random pause (`PatrolPauseMin/MaxTicks`), then walk to a random point within `WanderRadius` of home, idle on arrival. This is the part town life replaces with a smart-spot pick. |
| Threat and senses | `HostileMindSystems.cs` L19 (`HostileThreatSystem`), L168 (`HostileSenseScanSystem`) | combat only; town life does not use them. |
| Mind numbers | `HostileBrainConfig.cs` L7-61 | ticks made from `HostileBrainSettings` (the asset is the only place numbers are set). Same pattern for town life. |
| Diagnostics | `HostileBrainDiag.cs` L15-34 | `KN_BRAIN_DIAG=1` writes `[KnBrain]` lines; pure line makers, swappable sink. Same pattern: `KN_TOWN_DIAG`. |
| Brain cost | `m5-m6-implementation-plan.md` L521-523 | 2,000 Hostile brains, Editor with safety checks: mind tick p50 1.34 ms, p99 3.13 ms. 60 townsfolk is about 3 % of that. |
| Hostile ghost | `HostileGhost.cs` L16-36 | Species, Level, Tier, Mode, SpeedSteps, attack/death fields. Sent at 15 Hz, importance 60 (`Networking/Entities/Runtime/RelevancySettings.cs` L32). |
| Hostile client bodies | `HostileViewClientSystem.cs` L25-26 | the nearest 32 ghosts within 45 m get a pooled body. Constants in code (a later clean-up); town life puts its numbers in settings. |
| Tick | `Networking/Entities/Runtime/KnNetDefaults.cs` L9-10 | 30 simulation and network ticks a second. |
| Town NPCs | `Gameplay/NPC/Runtime/NpcDefinition.cs` L5 (`NpcRole`), L29-30 (`NpcOffer`), L65 | data of the service NPCs (Armorer, Apothecary, Broker...). |
| Clickable NPC | `NpcInteractable.cs` L12-14, `Interact` L140 | plain scene object, not networked, same on every peer; turns to the player on click (client only). |
| NPC lines | `NpcLines.cs` L12-20 | a line picked at random, never the same twice in a row. Reused for barks. |
| NPCs in the DOTS game | `ghosts-design.md` section 6.3 | NPCs stay GameObjects; "If NPCs later wander/patrol, they become interpolated ghosts exactly like Hostiles minus combat." This plan does that. |
| `VendorStall` | `World/Runtime/Surfaces/VendorStall.cs` L13-18 | **not a vendor actor**: the rule that stalls (6.5 m or lower, vendor within 3 m) are never climbed. Smart spots of kind Stall reuse its test to find stalls; nothing else. |
| Looks | `Items/Runtime/Visuals/SidekickPartCatalog.cs` L8-13; `CharacterAppearance.cs`; `SkinnedMeshMerger.cs` L65-71 | parts keyed by stable id; the merger bakes parts and body shapes into ONE skinned mesh on an existing skeleton. Townsfolk bake with it. |
| Animations | `Assets/Synty/AnimationBaseLocomotion`, `AnimationIdles`, `AnimationEmotesAndTaunts` (each `Animations/Sidekick/Masculine|Feminine`) | 641 Sidekick clips in the three packs. Vendors today use `Characters/NPC/Animations/AC_NPC_Idle.controller`. |
| Motions library | `CharacterCreation/Data/CharacterExpressionLibrary.cs`; `Social/Runtime/EmoteCatalog.cs` L27-33 | emote and stance keys ("E/Greet/Bow"). `PlayerEmotes.cs` L45 is an NGO `NetworkBehaviour` for players only: not extended. |
| Character LOD | `Rendering/Runtime/CharacterLod.cs` L8-18; `Rendering/Data/WorldLodSettings.cs` L85-93 | 25 m two-bone skinning, 60 m no shadow + animate only when seen, 150 m not drawn; looks again every 0.4 s. |
| Concord NavMesh | `World/Editor/Concord040Setup.cs` L116-124 | its own `NavMeshSurface`, volume 220 x 25 x 220 m round the deck, physics colliders. Vendors stood at district spots L93-105. |
| World clock and weather | `World/Runtime/WorldEnvironmentState.cs` L11-27; `World/Data/DayCycleProfile.cs` L18; `WeatherProfile.cs` L7 | time of day from epoch, a day is 3,600 s by default, `FromWeather/ToWeather` index the catalog, `PrecipitationType.Rain`. GM: `time.dawn/noon/dusk/midnight`, `SetTimeOfDay(hours)` (`Tools/Debug/GmCommands.cs` L925-937). |
| Head-look | none | not built for anyone yet. |

**Moved or different from the brief:** all brief line numbers hold. `VendorStall` is the climb rule, not a vendor (see
above). `NpcOffer` is L30 (its `[Flags]` L29).

**Layer finding that shapes the design.** `KryticalNova.Items` already uses `KryticalNova.NPC`, and CharacterCreation uses
Items. So the NPC system can never use Items' Sidekick merger or the motions library without a loop. Town life is
therefore **its own Simulation system, `Gameplay/Townsfolk`**, above NPC, Items, CharacterCreation, World and Brains, and
used by nothing except Tools.

## 2. What the named sources teach (in our words, only what we use)

- **Assassin's Creed Unity crowds (GDC 2015).** Thousands in view only because few are "real": full AI and animation
  near the camera, cheaper puppets in the middle, the farthest drawn as bulk with almost no logic. Reactions are local and
  staggered so a crowd never turns as one. *We use:* detail levels by distance, staggered reactions, shared animation.
- **Kingdom Come NPC detail levels.** Every NPC keeps living on its schedule while nobody looks; far away it only
  "knows" where its plan puts it, and when a player comes near it appears there already doing the right thing. *We use:*
  the far plan step (no walking, no paths, a jump to the plan's spot and pose before anyone is near).
- **Hitman crowds.** Cheap agents with very few states (walk, idle, look, step aside, flee), reactions that spread a
  little and fade, and hard caps on how many react at once. *We use:* a few activities only, "never all at once" caps.
- **The Sims smart objects.** The object says what it offers and how many can use it; the person only scores the offers
  by its own wants. *We use:* smart spots carry their offer, slots and hours; personalities only weigh them.
- **Unreal Mass.** Data-oriented agents with LOD processors that tick at different rates, smart objects with claimable
  slots, and the visual LOD kept apart from the simulation LOD. *We use:* claims on spot slots, server tiers apart from
  client LOD, everything in components (Burst, no managed objects).
- **Skyrim Radiant AI.** Time-boxed packages (work, eat, sleep, "sandbox" near a place, picking idle markers at random).
  *We use:* day plans as time blocks per personality, and "pick from the best few at random" so nothing looks scripted.

## 3. The design

### 3.1 One brain, a town-life mode

Townsfolk are **brain entities of the existing Brain** (`Gameplay/Brains`): the same `BrainTag`, think clock and tiers,
dice, mover, path queue and scores the Hostiles use, in the same `BrainSimulationGroup` on the same tick. What is new is
the **town-life mind**, which takes the place of the Hostile mind's idle wander (`HostileDecideSystem` L120-144) for
townsfolk only: instead of "a random point within the wander radius" it asks "which smart spot, for my personality, at
this hour, in this weather". There is no second brain, clock, path service or tick. Hostiles are not touched; townsfolk
carry no Hostile components, so no Hostile system ever sees them. (Pick 1 offers the literal alternative.)

### 3.2 Where the code lives (layers)

| Assembly | Folder | Uses | Holds |
|---|---|---|---|
| `KryticalNova.Townsfolk.Entities` (new) | `Gameplay/Townsfolk/Entities/Runtime` | Core, Networking.Entities, Brains, NPC, Items, CharacterCreation, World, Factions, Rendering | components, ghost, server systems, client view and presenters |
| `KryticalNova.Townsfolk.Entities.Editor` | `.../Entities/Editor` | the above + Player.Entities.Editor (ghost importance helper, as Hostiles) | ghost assets builder, spot builder for Sky Concord, animator builder, personality drafts builder |
| Data (asmref into the runtime) | `Gameplay/Townsfolk/Data` | | `TownsfolkSettings`, `TownsfolkPersonality`, `TownsfolkCast`, `VendorManner` + their assets |
| Tests | `.../Entities/Tests` | | EditMode and `KnTestWorld` tests |

Nothing below uses Townsfolk; Tools does (GM commands, debug window). It never uses Social, UI or GameFeel. The one change
to a lower system is a single static event on `NpcInteractable` (`Interacted`), raised in `Interact`, so a vendor can
react to its shop opening. `dependency_map.py --check` must stay at 0 uphill, 0 two-way.

### 3.3 Server decides, clients only present

| | Server (decides) | Client (presents only) |
|---|---|---|
| Where an NPC is, where it goes, which spot and slot, its activity, joining or leaving a group, going home, stepping aside | yes (brain + town-life mind, NavMesh paths) | follows the interpolated ghost |
| Looks (outfit, colours, body), walk cycle offsets, gesture timing | sends one seed | builds them from seed + cast data (pure, same on every client) |
| Head turns, glances, greetings, nods, barks, vendor waves and beckons, "welcome" on shop open | no | yes, each player's own game picks its own |

**`TownsfolkGhost`** (interpolated only; no physics collider; one prefab, never owned):

| Field | Type | Sent | What for |
|---|---|---|---|
| `LocalTransform` position | quantised (as Hostiles) | while moving | where it is |
| `Yaw` | ushort, 1/100 degree | on change | facing |
| `Seed` | uint | once | looks, timing, which lines |
| `Cast` | byte | once | which cast (zone) list the seed picks from |
| `Personality` | byte | once | gait, idle set, how it reacts (client side) |
| `Activity` | byte | on change | Walk, Jog, Stand, Sit, Lean, Chat, Shop, Work, Perform, Watch, Guard, Shelter |
| `SpotSlot` | ushort | on change | the spot slot it uses (0 none): the client snaps the sit/lean pose to that slot's anchor |
| `GroupId` | ushort | on change | chat circle or walking pair: members face the circle's centre |
| `SpeedStep` | byte, 0.05 m/s | on change | blends walk and jog |

Server only (never on a client): `TownLife` (plan block, current spot, claim, stay-until tick, last spots), `TownGroup`,
the Brain components. No ring entries are needed: every one-off moment (a greeting, a bark) is client-only. If one ever
must be the same for everyone, it goes in a ring with a tick, never a flag.

Spawning follows the Hostile way: a server spawner reads a baked `TownLifeArea` (centre, radius, cast asset, caps) and
instantiates the ghost prefab; the prefab and its holder in the ghost subscene are made by
`TownsfolkEntitiesAssetsBuilder` (safe to rerun; prefab name and GUID never change), then re-baked with no diff.
Relevancy ships off (150 m when on); importance `importanceTownsfolk` sits beside `importanceHostile` in
`RelevancySettings`, proposed 30.

### 3.4 Personalities (DRAFTS for the owner to edit)

Every number below is **PROPOSED**. Speeds in m/s (Synty walk clip about 1.4, jog about 3.5; player run is far faster).
"Stops" = chance to stop at a spot on each leg of a walk. "Stay" = seconds at a spot. Hours are game hours (a game day is
one real hour today). Dawn 05-08, Day 08-18, Dusk 18-22, Night 22-05.

| # | Personality | Walk | Stops | Stay | Chats with | Liked spots (weight) | Reaction to players | Out at dawn / day / dusk / night | Rain |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Busy Worker | 1.6-1.8 | 10 % | 120-300 at work | Busy Worker, Guard (breaks only) | Work 1.0, Stall 0.4 | ignores; steps aside early (2.5 m) | shift by seed: 06-14 or 14-22; home off shift | works if spot sheltered, else Shelter |
| 2 | Stroller | 1.0-1.2 | 50 % | 20-60 | Stroller, Gossip, Shopper | Viewpoint 1.0, Bench 0.8, Stall 0.4 | glances 3 s; greets 10 % | 40 / 80 / 60 / 10 % | Shelter, then home after 5 min |
| 3 | Jogger | jog 3.2-3.8 | 15 % (stretch) | 10-20 | nobody (nods) | Jog loop 1.0, Viewpoint 0.5 | swerves round; nods 5 % | 90 / 20 / 60 / 5 % | goes home |
| 4 | Gossip | 1.1-1.3 | 70 % | 60-180 | anyone; best with Gossip, Nosy Local, Shopper | Chat circle 1.0, Bench 0.6, Stall 0.4 | turns to look 4 s; comments (bark) 15 % | 20 / 70 / 70 / 10 % | chat circle under shelter |
| 5 | Shopper | 1.2-1.4 | 70 % at stalls | 15-45 per stall | Shopper, Gossip | Stall 1.0, Vendor stall (window-shop) 0.9, Bench 0.3 | glances 2 s; steps aside | 0 / 80 / 50 / 0 % (stalls open 08-20) | under canopies, keeps shopping |
| 6 | Loner | 1.0-1.3 | 30 % | 120-300 | nobody (declines invites) | Lean wall 1.0, edge Bench 0.8, quiet Viewpoint 0.7; crowding counts double | looks away; keeps 3 m | 40 / 40 / 40 / 40 % | ignores it |
| 7 | Nosy Local | 1.0-1.2 | 60 % | 30-90 | Gossip | Lean wall facing the plaza 1.0, Chat circle edge 0.6; +0.5 for spots near players | watches longest (6 s); greets 25 %; drifts to a spot near a player (1 per player at a time) | 50 / 70 / 50 / 10 % | Shelter, still watching |
| 8 | Night Owl | 1.0-1.3 | 40 % | 60-180 | Night Owl, Loner, a performer's audience | lit Lean wall 1.0, lit Bench 0.8, Viewpoint 0.6 | nods | 0 / 10 / 50 / 90 % | Shelter |
| 9 | Street Performer | 1.0 | n/a | 300-600 at a perform spot | draws an audience (3-6 slots round the spot) | Perform 1.0 only | plays to a player who stops within 6 m for 4 s: bow or cheer, 30 s cooldown | 0 / 70 / 80 / 20 % | packs up, Shelter |
| 10 | Guard | 1.3 | at every post | 60-120 per post | Guard (short) | Guard post 1.0, Gate 0.8 | nods to all; watches anyone who runs past within 8 m | always; shifts 06-18 / 18-06 | stays; prefers sheltered posts |

**Proposed mix on Sky Concord** (day cap 48): Busy Worker 8, Stroller 8, Shopper 8, Gossip 6, Nosy Local 4, Guard 4,
Jogger 3, Loner 3, Night Owl 2, Street Performer 2. Night (cap 20): Guard 4, Night Owl 6, Loner 3, Busy Worker 3,
Stroller 2, Gossip 1, Street Performer 1. The "out" percentages above decide who of the cast is out; the caps are hard.

**Data asset shape** (one `TownsfolkPersonality` asset per personality, in `Gameplay/Townsfolk/Data/Assets/Personalities/`;
menu Krytical Nova ▸ Townsfolk ▸ Personality):

```
TownsfolkPersonality : ScriptableObject
  id (byte, stable), displayName                      // debug view and docs only
  Vector2 walkSpeed, bool jogs, Vector2 jogSpeed
  float stopChance; Vector2 staySeconds
  TownsfolkPersonality[] chatsWith; float chatChance; bool declinesInvites
  SpotLike[] likes { TownSpotKind kind; float weight; bool needsLight; bool nearPlayers }
  float crowdingDislike                               // 1 normal, 2 = Loner
  PeriodPlan[4] { float outChance; SpotLike[] extraLikes }   // Dawn, Day, Dusk, Night
  Vector2Int[] shifts                                 // hours, for Busy Worker and Guard
  RainBehaviour rain (Ignore, Shelter, ShelterThenHome, GoHome, PackUp)
  PlayerReaction reaction { glanceSeconds; greetChance; stepAsideMetres; watchRunners; drawsAudience; approachNear }
  string idleSet, gestureSet                          // motion keys in CharacterExpressionLibrary
  NpcLine[] barks                                     // reuse NpcLines (text first; see pick 4)
```

The short page for the owner is this section; the ten assets are made by a drafts builder from the table once the owner
has edited it (step 1).

### 3.5 Smart spots

A smart spot is a scene marker (`TownSpotAuthoring`) baked into a server entity:

- **Kind:** Bench, Stall, Vendor stall (window-shop beside a real vendor), Lean wall, Chat circle, Viewpoint, Work,
  Perform, Guard post, Gate, Shelter, Home door, Jog loop point.
- **Slots:** 1-6, each with an offset, a yaw and the pose it gives (sit, lean, stand, watch); a claim per slot.
- **Hours open, sheltered, lit** (lit from the street lamps' hours), optional personality list (a Work spot for workers).

Scoring uses the Brain's `Score`: liked-kind weight x hour fit x distance curve (prefer under 40 m) x free slots x
crowding x "not one of my last three spots" x shelter when raining. The static part (personality x spot x period) is
cached in a table when the period or weather changes; only distance and free slots are read per pick. The mind takes the
best three and picks one at random by score. A claim is released on leaving; a claim older than its stay plus 30 s is
freed by itself, so nothing locks a bench forever. Spots for Sky Concord are placed by
`TownSpotsConcordBuilder` (Townsfolk Editor) from the Concord 040 root by name, with stalls found by `VendorStall.Is`;
Zone01 itself is not changed.

### 3.6 Groups, vendors, passers-by

- **Chat circles:** the first to reach a Chat circle "hosts" it; it invites up to three idle townsfolk within 30 m whose
  personality chats with its own (each accepts by its `chatChance`). Members face the centre; gestures are client-side
  from the seed. Each member's stay is rolled apart, so they drift off one by one. Most 4 circles at once.
- **Pairs:** 15 % of Strollers and Shoppers walk as a pair: the follower's mover aims 1 m beside the leader's.
- **Vendors** (the service NPCs stay non-networked scene objects; all of this is client-only, on each player's game,
  through a `VendorPresenter` added to each vendor): head follows the player within 8 m; a wave or beckon when the player
  is within 10 m and looks their way, once per 30 s; a call-out bark at most once per 45 s per vendor and 10 s for all
  vendors together; a "show goods" gesture while the player stands in reach; a welcome motion when the shop opens
  (`NpcInteractable.Interacted`). Which motions and lines: `VendorManner` asset per vendor (`NpcDefinition` keyed).
- **Passers-by:** glance (head-look) within 8 m only, at most 6 NPCs looking at the local player at once, 20 s cooldown
  each; a greeting at most once per 6 s from anyone; stepping aside is the server's (players within 1.5 m count as a soft
  obstacle in the town-life mover). Variety from the seed: outfit, body, walk speed in its range, clip start offset.

### 3.7 Day plans and the clock

The mind reads the world's time of day and weather from `WorldEnvironmentState` (the GM time commands move them). Each
personality's four periods say whether it is out and what it likes extra; workers and guards follow shifts. Going home =
walking to the nearest Home door and despawning there; coming out = spawning at a Home door. Rain sends townsfolk by their
`RainBehaviour`; the night cap thins the crowd by those whose period says stay in. Each NPC's block edges move by up to
30 minutes from its seed, so the town never empties on the hour.

### 3.8 Detail levels (server) and looks (client)

| Server tier (Brain) | Nearest player | Thinks | What it does |
|---|---|---|---|
| 0 | 12 m | every tick | full: reactions to players, step aside, groups |
| 1 | 40 m | every 3 ticks | walks spot to spot, groups |
| 2 | 120 m | every 15 ticks | walks, no reactions |
| 3 | farther | never (Brain rule) | **plan step** every 2 s, staggered: no paths; if its plan says another spot, it is moved there, seated or leaning, before any player comes within 120 m (Kingdom Come) |

Client: a pool of bodies (proposed 48) given to the nearest ghosts within 80 m. Each body is baked once with
`SkinnedMeshMerger` into one skinned mesh and the shared swatch material; bakes are cached by outfit key so equal outfits
share one mesh (pick 3). `CharacterLod` does the rest: 25 m two bones, 60 m no shadow and animate only when seen, 150 m
hidden. One shared animator controller (`AC_Townsfolk`, made by a builder, an override for each body type) with Synty
Base Locomotion, Idles and Emotes & Taunts clips; Animator culling off screen. An impostor is added only if the fps log
asks for it. Presenters never run on a dedicated server and never switch themselves off in a batch publish (the black-sky
lesson); the server build has no townsfolk bodies at all.

**Looks by place.** `TownsfolkCast` per area: which factions (weights), a small outfit set per faction and lineage
(Sidekick part ids from `SidekickPartCatalog` plus colour sets), and the personality mix. A faction's Home City cast lists
only that faction; Sky Concord lists all three. `TownsfolkLooks.Pick(seed, cast)` is pure, so every client builds the
same person.

### 3.9 Debug view

- `gm.town.debug` turns on labels over each townsfolk: personality, activity, plan block, spot and slot, server tier,
  client LOD tier. Plan block and spot come from the ghost and the cast; server-only state goes through `KN_TOWN_DIAG`
  `[KnTown]` lines (the `HostileBrainDiag` way).
- A Tools window, Krytical Nova ▸ World ▸ Townsfolk Debug, and the same controls in the GM window (every new GM command
  gets them): a time-of-day slider (calls `SetTimeOfDay`), rain on/off, cap, spawn/clear, and a counter of who is out by
  personality. Commands: `gm.town.debug`, `gm.town.cap <n>`, `gm.town.clear`, `gm.town.spawn <personality> <n>`.

## 4. Performance budget (60 fps = 16.7 ms a frame; target mid to upper-mid PC)

| Item | Budget | How it is held |
|---|---|---|
| NPC cap on Sky Concord | 48 day / 20 night (settings; hard ceiling 60) | spawner caps |
| Server town-life mind | under 0.15 ms p99 at 60 | Burst systems, Brain tiers, scores cached per period; reference: 2,000 Hostile brains p99 3.13 ms (Editor) |
| Server paths | at most 2 of the Brain's 8 searches per tick | townsfolk path priority below Hostiles; far tier never paths |
| Server memory | under 1 MB | components only |
| Bandwidth | about 10 bytes per moving update; 10 Hz near, about 2 Hz far by importance; still NPCs send nothing; under 4 KB/s per client with 60 relevant | quantised fields, change-only |
| Client main thread | under 1.0 ms | view sync staggered, head-look only within 8 m and at most 6 at once, presenters tick at 10 Hz past 25 m |
| Client animation | under 0.8 ms | one shared controller, culling, CharacterLod tiers |
| Client GPU | under 1.0 ms | one skinned draw per NPC, one shared material, shadows only within 25 m |
| Client memory | under 40 MB of baked meshes | outfit cache (about 36 outfits), no new textures (swatch shared) |
| Garbage | 0 bytes a frame | pools, no LINQ, no allocations in presenters |
| Total | a few ms (about 2.5 ms client, 0.2 ms server) | perf log before and after, crowd in view |

## 5. Files

**New**

- `Gameplay/Townsfolk/Data/` `TownsfolkSettings.cs`, `TownsfolkPersonality.cs`, `TownsfolkCast.cs`, `VendorManner.cs`,
  `TownSpotKind.cs`; `Assets/TownsfolkSettings.asset`, `Assets/Personalities/TP_*.asset` (10), `Assets/Cast_SkyConcord.asset`,
  `Assets/VendorManner_*.asset`; asmref into the runtime.
- `Gameplay/Townsfolk/Entities/Runtime/` `KryticalNova.Townsfolk.Entities.asmdef`, `TownsfolkComponents.cs`,
  `TownsfolkConfig.cs` (ticks from settings), `TownsfolkGhost.cs` (+ write system), `TownLifeAreaAuthoring.cs`,
  `TownSpotAuthoring.cs`, `TownsfolkSpawnSystem.cs`, `TownLifeDecideSystem.cs`, `TownSpotSystems.cs` (claims, scoring),
  `TownGroupSystem.cs`, `TownPlanSystem.cs` (clock, periods, far plan step), `TownLifeRules.cs` (pure),
  `TownsfolkLooks.cs` (pure), `TownsfolkViewClientSystem.cs`, `TownsfolkPresenter.cs`, `VendorPresenter.cs`,
  `TownsfolkDiag.cs`.
- `Gameplay/Townsfolk/Entities/Editor/` `TownsfolkEntitiesAssetsBuilder.cs`, `TownSpotsConcordBuilder.cs`,
  `TownsfolkAnimatorBuilder.cs` (`AC_Townsfolk` in `Characters/NPC/Animations/`), `TownsfolkPersonalityDrafts.cs`.
- `Gameplay/Townsfolk/Entities/Tests/` `TownSpotScoreTests`, `TownLifeRulesTests`, `TownsfolkLooksTests`,
  `TownLifeWorldTests` (KnTestWorld), `TownsfolkGhostLintTests`.

**Touched**

- `Gameplay/NPC/Runtime/NpcInteractable.cs`: one static `Interacted` event raised in `Interact` (L140).
- `Networking/Entities/Runtime/RelevancySettings.cs`: `importanceTownsfolk`.
- `Tools/Debug/GmCommands.cs` (or a new `TownsfolkCommands.cs`) and `Tools/Editor/GM/GmToolsWindow.cs`: the debug controls.
- Vendor prefabs in `Characters/NPC/Prefabs/`: dressed with Sidekick presets (step 7), `VendorPresenter` added.
- Docs: `01_Systems_Build_List.md` (row, glossary: Townsfolk, Personality, Day Plan, Smart Spot, Chat Circle; change log),
  `ghosts-design.md` section 6.3 (townsfolk are now a ghost), `Dependency-Map.md` (regenerated), the Encyclopedia.

## Evals (written and approved before any code)

- [ ] Every number lives in `TownsfolkSettings` or a personality/cast asset; none in systems. Proved by: `kn_check` and review.
- [ ] Layers hold: Townsfolk uses only lower or same-layer systems, nobody but Tools uses it. Proved by: `dependency_map.py --check` (0 uphill, 0 two-way) and `compile_check.py` + `--server`.
- [ ] The scorer picks liked, open, free spots and never the last three; a full spot is never picked. Proved by: `TownSpotScoreTests`.
- [ ] Claims never double-book a slot and a stale claim frees itself. Proved by: `TownLifeWorldTests` (60 NPCs, 20 spots, 10,000 ticks).
- [ ] Day plans: at 03:00 at most the night cap is out and only night personalities and shifts; rain sends each by its rule. Proved by: `TownLifeRulesTests` + `TownLifeWorldTests` with the clock moved.
- [ ] Far NPCs keep their plan: a tier-3 NPC whose plan changed is at its new spot and pose before a player is within 120 m. Proved by: `TownLifeWorldTests`.
- [ ] Hostiles are unchanged: their decide results and the Hostile soak are the same with townsfolk present. Proved by: existing Hostile world tests + `HostileSoakTimingTests` within 5 %.
- [ ] Same person on every client: `TownsfolkLooks.Pick` gives the same outfit for a seed and cast. Proved by: `TownsfolkLooksTests`.
- [ ] Faction cities only wear their faction; Sky Concord mixes all three. Proved by: `TownsfolkLooksTests` over each cast asset.
- [ ] Ghost: one prefab, interpolated only, never owned (`PlayerGhost` stays the only owned ghost); the re-bake shows no diff. Proved by: `TownsfolkGhostLintTests` + the builder's no-diff check.
- [ ] Server cost under 0.15 ms p99 at 60 townsfolk; bandwidth under 4 KB/s per client. Proved by: `[KnTown]` timing line on the test-lane server + the netcode stats.
- [ ] No client presentation on the server: the server build has no townsfolk bodies or presenters. Proved by: server build check + `compile_check.py --server`.
- [ ] Two clients: both see the same NPCs in the same places and spots. Proved by: second-client test on the DOTS test server (screenshots of both).
- [ ] fps holds 60 with the crowd in view (perf log, before and after, same camera spot on Sky Concord). Proved by: perf log.
- [ ] Sky Concord feels alive. Proved by: owner in Play DOTS (GIF per step).

## Steps (one PR each)

- [ ] 0. Checks only: the server's path service finds paths on the Concord 040 NavMesh; perf log baseline on Sky Concord with no crowd. Evals: none (sets the baseline).
- [x] 1. Data: settings, personality, cast, manner types; the ten personality assets from the owner's edited table; the glossary rows. Evals: numbers in assets.
- [x] 2. The ghost, spawner and town-life mind on the Brain with Strollers only on hand-placed points; debug labels and `gm.town.*`. Evals: layers, ghost lint, two clients.
- [x] 3. Smart spots (authoring, Sky Concord builder, claims, scoring), all ten personalities, clock and rain. Evals: scorer, claims, day plans, Hostiles unchanged.
- [x] 4. Looks: casts, seed pick, merged-mesh bake and cache, `AC_Townsfolk`, CharacterLod. Evals: same person, faction casts.
- [ ] 5. Groups: chat circles and pairs. Evals: claims (circles), two clients.
- [ ] 6. Client life: head-look, glances, greetings, barks; step aside on the server. Evals: no presentation on server, fps.
- [ ] 7. Vendors: Sidekick looks, `VendorPresenter`, `VendorManner` assets, `Interacted`. Evals: fps, owner in Play.
- [ ] 8. Detail levels and performance: far plan step, update rates, budgets measured, perf log after. Evals: far plan, server cost, bandwidth, fps, owner approval.

## The owner's picks

1. **How town life sits on the monster brain:** a) townsfolk are brain entities of the shared Brain core (`Gameplay/Brains`) with their own town-life mind in a new `Townsfolk` system; Hostiles untouched (recommended: one brain, no combat code on townsfolk, no loop with Items); b) a `TownLife` mode inside `HostileDecideSystem`, townsfolk carry Hostile components (literal, but every Hostile change then risks the town and the reverse).
2. **Crowd size on Sky Concord:** a) 48 by day, 20 at night (recommended); b) 60 by day, 30 at night; c) 40 by day, 15 at night.
3. **Mesh baking:** a) bake per outfit and share the mesh between NPCs who wear it, colour sets on top (recommended: about 40 MB instead of about 90); b) bake each NPC at spawn as the brief says.
4. **Barks and call-outs in the first pass:** a) text bubbles from the line lists, voices later with the npc-voice skill (recommended); b) voiced from the start; c) none, motions only.

## Done when (the owner's words)

Sky Concord feels alive in Play DOTS (owner approves), faction cities only wear their faction while Concord mixes, the fps
log holds 60 with the crowd in view, and a second client sees the same NPCs in the same places.

## As built

First pass, 13 Nov 2026 (b18-coder-11). Compiles (90/90 Editor, 40/40 server), `dependency_map.py --check` clean.
**Not yet run in Unity**: builder, EditMode tests and the Play GIF / perf log are owed before the owner sees it.

- [x] Data: `TownsfolkSettings`, `TownsfolkPersonality` (10 drafts, made by the builder only when missing: page
  `Design/Townsfolk-Personalities.md`), `TownLifeArea` (Sky Concord: 67 spots in Concord 040's frame + 8 jog points; mix
  48 / 20), `TownsfolkCast` (Sky Concord: all three factions). Resources holders `TownsfolkWorldAssets` (server: no bodies)
  and `TownsfolkViewAssets` (client).
- [x] Server: `TownsfolkServerSystem` (installs the config, makes the Brain's config only when the Hostiles did not, builds
  the spots from the area snapped onto the NavMesh plus 60 sampled walk points, clock and rain once a second from the world
  clock, keeps every personality at the hour's number: out of the home doors 4 a second, sent home when over);
  `TownLifeDecideSystem` (Burst, on the Brain: pick from the best 3 by score, claims, stay, stroll, shifts, rain, home; far
  tier plan step every 2 s); `TownsfolkGhostWriteSystem`; `TownPlayerFeedSystem` (only when no Hostiles). Switch:
  `KN_TOWNSFOLK` (on in a built server and the Editor's Play, off in EditMode tests).
- [x] Ghost `TownsfolkGhost` (seed, cast, personality, activity, speed, chat circle), interpolated, never owned, importance
  30 (`RelevancySettings.importanceTownsfolk`), prefab + holder by `TownsfolkEntitiesAssetsBuilder`.
- [x] Client: `TownsfolkViewClientSystem` (first pass: nearest 48 within 80 m, now by Crowd Detail below; a hidden template per look, baked once, bodies share its
  mesh through `CharacterAppearance.ShareFrom`; 16 looks per cast from `RecipeEdit.Randomize`, the lineage's basic outfit;
  `CharacterLod` on every body), one shared animator per body type (`AC_Townsfolk_Masc/Femn`, Speed blend + 4 variants per
  activity picked by the seed), `gm.town.debug` labels.
- [x] GM: `gm.town`, `gm.town.debug`, `gm.town.cap`, `gm.town.clear`, `gm.town.spawn`; GM window World tab; control page
  Townsfolk panel. Time of day: the existing `time.*` controls.
- [x] Tests written: `TownLifeRulesTests`, `TownLifeWorldTests` (60 townsfolk, 20 spots, 10,000 ticks; stale claims; shifts
  and rain; far plan step; ghost), `TownsfolkLooksTests` (same look per seed, home city vs Concord, ghost lint, built assets).
- [x] Jobs from the start (owner, 13 Nov 2026 ~22:45): `TownLifeDecideSystem` is five Burst jobs: the score cache
  (`TownScoreCache`, the fixed part of each spot's score per personality, rebuilt only when the game hour, the period, the
  rain or the spots change), the stale-claim sweep, players near per spot (parallel), the think job (parallel
  `IJobEntity`: states, arrival, plan step and scoring into `TownPick`, wishes into lists, slots read only) and the claim
  job (releases and stays first, then slots handed out in StableId order: never two in one slot, the same town every run).
  The ghost writer is a parallel `IJobEntity`. Test: `SameSeedMakesTheSameTown`.
- [x] Crowd Detail (owner: performance first, through the existing Graphics settings): a column of
  `GraphicsPreferences.Detail` (Low / Medium / High / Ultra with the presets) and one choice on the Graphics page, put in
  force by `GraphicsSettingsApplier` into `CrowdDetailRuntime` (Rendering). The tier's numbers are rows in
  `TownsfolkSettings` (`*ByTier`): full bodies 16 / 32 / 48 / 60 within 25 / 35 / 45 / 60 m; cards out to 60 / 80 / 100 /
  140 m (24 / 48 / 64 / 96); every-frame animation within 10 / 15 / 20 / 30 m, farther every 4 / 3 / 2 / 2 frames;
  shadows within 8 / 12 / 18 / 25 m; full skinning within 10 / 15 / 20 / 30 m (`CharacterLod.Drive`, the view sets each
  body's level). The server's crowd never changes with it.
- [x] Far cards (impostors): `TownsfolkCards` photographs each look once (hidden camera, idle pose, layer 31) and draws
  every far townsperson of that look as a camera-facing quad in one instanced draw, no shadow (`M_TownsfolkCard`, URP
  Unlit, alpha clip, instancing); tinted by the hour's light, retaken when the light halves or doubles; off by itself
  (one warning) if a picture cannot be taken.
- [x] Vendors (owner 18.1 A; step 7 in part): `VendorLooksClientSystem` dresses every `NpcInteractable` in one fixed
  Sidekick look (its definition's name, the scene's cast: `TownsfolkViewAssets.vendorCasts` for a home city, else Sky
  Concord's mixed cast; the old model hidden, never removed); `VendorPresenter`: head follows within 8 m, wave or beckon
  when the player looks its way (30 s each, 10 s between any two), show goods on stepping into reach, a bow on
  `NpcInteractable.Interacted` (new). Gestures: an override layer `Gestures` on the shared controllers, IK pass on.
  `VendorManner` (Townsfolk Data, `Data/Assets/VendorManners/VendorManner_*.asset`, one per NpcDefinition, drafts made only
  when missing): its greeting gesture, call-out lines (12 m, 45 s each, 10 s between any two vendors) and welcome lines, shown
  in the HUD's comic speech bubble through a new Core sink `ISpeechBubbleSink` (`ClientPresentationRegistry.Speech`,
  implemented by `PetBubbleController`; vendor lines are not hidden by the Pet chatter option).
- [x] Owner notes, 14 Nov 2026 (after the Fable fix; Opus coder b18-standby-18b):
  - [x] Far cards much farther: bodies near and mid-distance, cards only far out. Five bands per townsperson, edges per Crowd
    Detail tier in `TownsfolkSettings` (metres, Low / Medium / High / Ultra):

    | Band | What is drawn | Ends at |
    |---|---|---|
    | CLOSE | the whole mesh (LOD0), every frame animated, head-look (nearest 6 within 8 m), shadows | 12 / 18 / 25 / 35 |
    | MEDIUM | LOD1 (about half the triangles), animated every 2nd frame, shadows, no head-look | 28 / 40 / 55 / 75 |
    | FAR | LOD2 (about a quarter), animated every 4th frame, no shadows | 45 / 65 / 85 / 115 |
    | REALLY FAR | the picture card (one instanced draw per look) | 120 / 160 / 220 / 300 |
    | TOO FAR | not drawn (the server still keeps it where it is) | past that |

    Cards began at 25 / 35 / 45 / 60 m before; Low now starts them at 45 m (never so near a card reads as a flat figure).
    Bodies drawn at most 20 / 32 / 48 / 60, cards 32 / 48 / 64 / 96. A band changes back only 2 m past its edge.
  - [x] Fewer polygons with distance: LOD1 and LOD2 are baked once per look when the look is baked (`TownsfolkMeshLod`:
    vertex clustering inside one submesh and one swatch colour cell, the cell size searched to the triangle share in
    `lod1Share` / `lod2Share`), shared by every body wearing it, never made per NPC. Unity's own Mesh LOD generator is
    Editor-only and a look is merged at run time, so the project's own baker makes them. Each body draws them on its own
    bones through a `LODGroup` (cross-fade, animated dither) whose switch heights are worked out from the band edges in
    metres (`TownsfolkBands.ScreenHeight`, again when the tier, the camera's view angle or the LOD bias change); past FAR
    the group fades the body out over the card.
  - [x] Life across ALL of Sky Concord, not only by the Sky Lift: the area now reaches the whole platform (radius 160 m:
    its NavMesh is 220 m square). `TownSpread` spreads points every 9 m over every walkable floor of its NavMesh (plazas,
    streets, stairs, upper tiers, edges; at most 240) and sorts them: rim points look out (Viewpoint, quiet: strollers),
    back streets 18 m or more from every hand-placed spot get quiet lean spots (40 %, loners), elsewhere 15 % are places to
    stand and look round, the rest walk points. 70 % of new townsfolk step out somewhere on the district instead of from
    a home door; never by the arrival while 15 % of the crowd is already within 28 m of it, and while it is, its spots
    score a quarter (`TownSpotFlags.NearArrival`, `TownArea.ArrivalCrowded`). Numbers in `TownsfolkSettings`.
  - [x] No weapons on townsfolk or vendors: the Sidekick weapon mounts (back, hips back / left / right:
    `TownsfolkSettings.noWeaponRegions`) are left out of the merged mesh (`CharacterAppearance.ExcludeRegions`), and every
    renderer of the player model but the look's own is removed when a body is made (held weapons, holsters). Guards too,
    until the Master says otherwise.
  - [x] Vendors out of step (the Fable fix): kept as it was.
  - Tests: `TownsfolkBandsTests` (bands and hysteresis, cards far on every tier, switch heights in metres, LOD triangle
    shares, the spread over a whole deck and the arrival's share, no weapon regions).
- [ ] Owed: run the builder and the tests in Unity; Play proof GIF + perf log Low and High against the budget (48 in view:
  <= 1.5 ms main thread, <= 1.0 ms GPU, 0 GC a frame); second client.
- [ ] Later steps: pairs walking together, head-look / glances / greetings / text barks (step 6), vendors' manner assets and
  barks (step 7).

Findings: the Concord 040 prefab (`World/Zones/Concord040/`) is not in git (only in the hand-test clone), so a fresh
checkout has no Sky Concord; its meshes carry tile names only, so spots are data in the area asset, not found by name. The
Synty packs in the project have no sitting clip: benches show a standing rest. Feminine locomotion clips are missing from
the locomotion pack; feminine bodies use the masculine walk and run.
