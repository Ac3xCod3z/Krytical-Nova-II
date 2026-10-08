# Krytical Nova II — Project Organization Rules

**Status:** adopted 2026-10-02. Applies to everything created from now on, by people or by Claude.
**Enforced by:** `Assets/_Game/Tools/Editor/Organization/` (see [Tooling](#8-tooling)). If you change a rule here, update
`ProjectOrganizationRules.cs` too.

---

## 1. The golden rule

> **Ask: "What system owns this?"** Put it in that system's folder, not in a folder named after its file type.

| If it is…                                   | It goes in…                                         |
|---------------------------------------------|-----------------------------------------------------|
| Grass code, shader, compute, data, material | `_Game/Environment/Vegetation/Grass/…`              |
| Player progression / skills / stats         | `_Game/Gameplay/Progression/…`                      |
| Items, inventory, equipment                 | `_Game/Gameplay/Items/…`                            |
| Weather, day cycle, sky, zones, portals     | `_Game/World/…`                                     |
| A forest biome definition (world config)    | `_Game/World/Biomes/…`                              |
| A tree / rock / building asset              | `_Game/Environment/<Category>/…`                    |
| Player / NPC / enemy model, rig, anims      | `_Game/Characters/<Who>/…` (representation)         |
| Player / NPC / enemy behavior code          | `_Game/Gameplay/<System>/Runtime/` (behavior)       |
| Global service (save, pooling, events)      | `_Game/Core/Runtime/…` (keep Core tiny)             |
| System-specific editor tool                 | `_Game/<System>/Editor/`                            |
| Reusable / cross-system editor tool         | `_Game/Tools/Editor/`                               |
| Dev console commands, debug overlays        | `_Game/Tools/Debug/`                                |
| Asset Store / vendor package                | `_ThirdParty/<Vendor>/` (unmodified)                |
| Our glue code for an external SDK           | `_Plugins/<Area>/`                                  |
| PSD / Blender / Substance / concept art     | `_ArtSource/…`                                      |

If the honest answer is "I don't know, put it in Core", **stop**. Find or create the owning system instead.
Core is for things *every* system needs and that know nothing about gameplay.

---

## 2. Top level of `Assets/`

```
Assets/
├── _Game/          Everything we make (only place for first-party content)
├── _ThirdParty/    Vendor packages, untouched ("we didn't make this")
├── _Plugins/       Our integration layers for external SDKs/services
├── _ArtSource/     Source/working art files (PSD, .blend, concept, reference)
│
│   Allowed exceptions (tools require these exact paths; never move them):
├── Plugins/              Unity-reserved: native libs & vendor DLLs (Odin, DOTween, QC, Maintainer)
├── Resources/            DOTweenSettings.asset (DOTween looks for it here)
├── TextMesh Pro/         TMP essentials (TMP writes into this path)
├── Synty/                Sidekick hard-codes Assets/Synty/SidekickCharacters/... paths
├── DownloadCache/        Synty's ToolDownloader cache
├── QuickDestruction/     RipVertices Quick Destruction hard-codes Assets/QuickDestruction/... (bake output, icons)
├── FastScriptReload/     Fast Script Reload (hot reload in Play mode). Cannot be moved out of the root
├── _Recovery/            Unity crash-recovery scenes (Unity-generated)
└── DefaultNetworkPrefabs.asset   Netcode for GameObjects regenerates it at this path
```

Nothing else goes at the root. No `Scripts/`, `Prefabs/`, `Materials/`, `Textures/`, `GameData/` buckets. The validator
reports them as errors.

---

## 3. `_Game/` layout (current + where future things go)

Folders marked *(future)* don't exist yet. Create them when the first asset needs them, never empty in advance.

```
_Game/
├── _Docs/                 Design docs (Design/), plans (Plans/), reference packs (Reference/: naming glossary, build list), this file
├── Core/Runtime/          Cross-cutting interfaces & registries. Defines KryticalNova.Runtime.asmdef
│                          SaveSystem/ (ISaveStore, ISaveParticipant, file and memory stores)
│                          (future: Architecture/ Events/ Services/ Pooling/ Utilities/ Extensions/)
├── Gameplay/
│   ├── Combat/            Runtime/
│   ├── Factions/          Runtime/ (ids, rules)  Data/ + Data/Assets/ (the three factions, catalog)  Editor/
│   ├── CharacterCreation/ Runtime/ (recipe, look, roster, save, names)  Data/ + Data/Assets/ (lineages, palettes,
│   │                      database)  Editor/ (bakers)  Tests/Editor/
│   ├── Economy/           Runtime/ (Creds, Vault and Deep Vault, Trade, Kiosks, trade zones)  Data/ + Data/Assets/
│   │                      (EconomySettings)  Editor/  Prefabs/ (KioskStand)  Tests/Editor/
│   ├── Forge/             Runtime/ (PlayerGearWork: Tempering, Fusion, Tuning and Reforge at the Armorer. Sits above Items,
│   │                      Economy, Progression and NPC, which is why it is its own system)
│   ├── Items/             Runtime/ (+Visuals/; world drops, pickup; gear state and progression rules)  Data/ (GearProgressionSettings,
│   │                      GearThemeLibrary)  Data/Assets/ (items, part catalog, gear dictionary, GearThemes = one colour theme per
│   │                      gear set, GearMaterials/ = forge materials, Prefabs/Weapons/ = weapon models, each with its glow zone and reach)
│   │                      Editor/ (also GearThemeBuilder, WeaponGlowZoneBaker)  Prefabs/ (WorldDrop)  Tests/Editor/
│   ├── NPC/               Runtime/  Data/Assets/
│   ├── Persistence/       Runtime/ (ServerSave, account and character documents, PlayerSaveAgent)  Tests/Editor/
│   ├── Pets/              Runtime/ (growth, hunger, PlayerPet)  Data/ + Data/Assets/ (settings, catalog, definitions)
│   │                      Editor/ (PetAssetBuilder)  Tests/Editor/
│   ├── Player/            Runtime/  Prefabs/ (Player.prefab = the networked gameplay player)
│   ├── Progression/       Runtime/ (also PlayerNova, NovaRules, NovaHalo: Ascension and Nova Standings)  Data/ (SO classes,
│   │                      NovaSettings)  Data/Assets/ (SO instances)  Shaders/ (the Nova's four shaders and their materials)  Editor/  Tests/Editor/
│   ├── Social/            Runtime/ (chat channels and rules, PlayerChat, the combat log, Squads, Contacts, Crews, NamecardRules,
│   │                      PlayerEmotes, EmoteCatalog)  Data/ + Data/Assets/ (CrewSettings, EmoteWheelSettings)  Tests/Editor/
│   └── (future) Skills/ Quests/ Interaction/ StatusEffects/ Inventory/
├── Characters/            Representation only: models, rigs, materials, animations, visual prefabs
│   ├── Player/            Player_01/ (Sidekick character)  Animations/
│   ├── NPC/               Prefabs/  Animations/
│   ├── Shared/            Props/ (SM_EmoteProp_*: what a character holds during prop emotes; baked from Synty's samples)
│   └── (future) Enemies/
├── World/
│   ├── Runtime/           Clock, weather, environment net state, Presentation/, Portals/, Minimap/, Navigation/, Ambience/
│   ├── Data/              SO classes;  Data/Assets/ (day cycle, weather catalog, Weather/ profiles)
│   ├── Sky/               Shaders/ Materials/ Textures/ LensFlare/
│   ├── Terrain/           Materials/
│   ├── Zones/<ZoneId>/    Per-zone terrain data, layers, masks, minimap data, water
│   ├── Editor/            Zone/town builders, sky/weather setup, VFX builders
│   └── (future) Biomes/ WorldGeneration/ WorldStreaming/ WorldEvents/
├── Environment/
│   ├── Vegetation/        Shaders/ (shared wind/foliage)  Materials/  Prefabs/  Editor/
│   │   └── Grass/         Runtime/ Data/ Data/Assets/ Rendering/ Compute/ Materials/ Editor/  (own assembly)
│   ├── Buildings/         Materials/BuildingPalette/ (Signs/ = gate nameplate materials)
│   ├── Props/             StreetLights/ (lit SM_Prop_Streetlight_01 variant)  Editor/ (StreetLightBuilder)
│   ├── Ruins/             Meshes/  Prefabs/  Profiles/ (Quick Destruction profile)  Editor/ (RuinBaker)
│   ├── Water/             Shaders/
│   └── (future) Rocks/ Props/ Architecture/
├── GameFeel/              Runtime/ (KryticalNova.GameFeel, client-only)  Data/ (SmearSettings; Data/Assets/ = its asset)
│                          Editor/ (GameFeelBuilder)  Prefabs/ (PFX_GameFeel)  Tests/Editor/
├── Icons/                 Editor/ (the icon library, editor-only: IconLibrary, IconTreatmentSettings, ComicIconTreatment, IconBaker,
│                          IconPacks, IconLibraryWindow)  Data/Assets/ (IconLibrary.asset, IconTreatmentSettings.asset)  Tests/Editor/
│                          The icons it bakes go to the owning systems: Gameplay/Progression/Data/Assets/Icons, Gameplay/Items/Data/Assets/Icons
├── Networking/            Runtime/  Prefabs/ (NetworkManager)
├── Rendering/             Runtime/ (renderer features)  Shaders/  Settings/ (URP assets, volume profiles)  Editor/
├── UI/                    Runtime/ (KryticalNova.UI; Menus/ = front-end controllers and widgets)  Editor/ (Menus/ = front-end
│                          scene builder, own assembly)  Common/  HUD/ (Minimap/ Map/ Chat/ Duel/ Party/ Pet/ Herald/ GameMenu/)  Inventory/
│                          (also the forge bench)  Economy/ (Vault, Trade and Kiosk windows)  Social/ (prompt, player menu,
│                          Contacts, Crew, the emote wheel)  Settings/ (the Settings window; its code is Runtime/Settings/)  NPC/  Shaders/
│                          Menus/ (Materials/, KeyGlyphs.asset: assets the front-end scenes use)
│                          (future: Skills/ Character/ World/)
├── VFX/                   Gameplay/  GameFeel/  Environment/{City,Portals,Vehicles,Weather}/  Materials/ (shared FX mats)
├── Input/Actions/         InputSystem_Actions.inputactions (project-wide actions)
├── Scenes/                MainMenu/ (Title, FactionSelect, CharacterCreator, CharacterSelect, Loading: generated, rebuild
│                          with Krytical Nova ▸ Front End ▸ 1. Build Front-End Scenes; never edit by hand)
│                          World/Towns/ (Zone01_StarterTown + its lighting/APV/navmesh data)  Development/
│                          (future: Bootstrap/ World/Overworld/ Dungeons/ Cinematics/)
├── Settings/              Project-level config assets for packages (Animancer Settings)
├── Tools/                 Editor/ (KryticalNova.Editor.asmdef, Organization/, GM/, Build/ = build scene order,
│                          Setup/ = the two wiring steps: player prefab and zone scene, Hierarchy/ = vHierarchy data)
│                          Debug/ (QC commands)
│                          (future: Profiling/ Importers/)
├── Audio/                 Mixers/ (KN_Main.mixer: Master with Music, Sfx, UI and Ambience; the Settings sliders drive it)
│                          (future: Music/ SFX/ Ambience/ Voice/)
└── (future) Art/ (shared-only art)  Tests/ (cross-system tests)
```

### Anatomy of a system

```
<System>/
├── Runtime/      MonoBehaviours, NetworkBehaviours, plain C# (player-build code)
├── Data/         ScriptableObject *classes*
│   └── Assets/   ScriptableObject *instances* (.asset), optionally grouped in subfolders
├── Editor/       Inspectors, builders, windows for this system only
├── Tests/
│   ├── Editor/   EditMode tests
│   └── Runtime/  PlayMode tests
└── Prefabs/ Materials/ Shaders/ (or Rendering/) Compute/ Meshes/ Animations/ VFX/ Audio/  (as needed)
```

- Never mix runtime, editor, and test code in one folder. C# scripts in `_Game` live under a folder named
  `Runtime`, `Editor`, `Data`, `Tests`, `Debug`, `Profiling` or `Importers`.
- System-specific art lives **with the system** (`Grass/Rendering/GrassBlades.shader`), not in a global `Art/` bucket.
  `Art/` is only for art genuinely shared by many systems.
- Scenes keep their generated data (lighting, APV, navmesh) in a folder named after the scene, next to it.
- Test/dev scenes go in `Scenes/Development/` as `Test_<Feature>.unity` (e.g. `Test_Grass.unity`).

---

## 4. Assemblies

Every script in `_Game` must belong to an assembly (`.asmdef` or `.asmref`). Nothing compiles into `Assembly-CSharp`.

### Current assemblies

| Assembly                          | Defined in                                     | Covers (via .asmref)                                                     |
|-----------------------------------|------------------------------------------------|---------------------------------------------------------------------------|
| `KryticalNova.Runtime`            | `Core/Runtime/`                                | Gameplay/{Combat,Items,NPC,Player,Progression,Factions,CharacterCreation,Social,Economy,Persistence,Pets,Forge}/Runtime, {Progression,Factions,CharacterCreation,Social,Economy,Pets,Items}/Data, Networking/Runtime, Rendering/Runtime, World/Runtime, World/Data |
| `KryticalNova.Vegetation.Grass`   | `Environment/Vegetation/Grass/Runtime/`        | Grass/Data. **No dependencies.** The model for future system assemblies |
| `KryticalNova.UI`                 | `UI/Runtime/`                                  | Depends on Runtime and Vegetation.Grass (the graphics settings set grass density). Excluded from dedicated server (`!UNITY_SERVER`) |
| `KryticalNova.GameFeel`           | `GameFeel/Runtime/`                            | GameFeel/Data. FEEL-driven juice behind `Core/IGameFeelSink`. Depends on Runtime, UI, FEEL. `!UNITY_SERVER` |
| `KryticalNova.DevTools`           | `Tools/Debug/`                                 | Quantum Console commands                                                  |
| `KryticalNova.Editor`             | `Tools/Editor/`                                | Every `<System>/Editor/` folder (Items, Progression, Factions, CharacterCreation, Player, Economy, Pets, UI, World, Rendering, Vegetation, Grass, Ruins, GameFeel, Icons) |
| `KryticalNova.Editor.GM`          | `Tools/Editor/GM/`                             | GM tools window                                                           |
| `KryticalNova.Editor.Menus`       | `UI/Editor/Menus/`                             | Front-end scene builder. Depends on Editor, UI, Runtime, uGUI, TextMesh Pro. `!UNITY_SERVER` |
| `KryticalNova.Items.Tests.Editor` | `Gameplay/Items/Tests/Editor/`                 | EditMode tests                                                            |
| `KryticalNova.CharacterCreation.Tests.Editor` | `Gameplay/CharacterCreation/Tests/Editor/` | EditMode tests (names, roster, recipes, creator edits, gear rules) |
| `KryticalNova.Social.Tests.Editor` | `Gameplay/Social/Tests/Editor/`               | EditMode tests (chat channel rules, typed chat lines, party rules, Contacts, Crews, namecards, the emote wheel) |
| `KryticalNova.Progression.Tests.Editor` | `Gameplay/Progression/Tests/Editor/`     | EditMode tests (Ascension and Nova Standings rules)                                    |
| `KryticalNova.Economy.Tests.Editor` | `Gameplay/Economy/Tests/Editor/`             | EditMode tests (wallet, storage, trade and Kiosk rules)                                |
| `KryticalNova.Persistence.Tests.Editor` | `Gameplay/Persistence/Tests/Editor/`     | EditMode tests (save stores, account documents)                                        |
| `KryticalNova.Pets.Tests.Editor`  | `Gameplay/Pets/Tests/Editor/`                  | EditMode tests (pet growth, stages, hunger)                                            |
| `KryticalNova.UI.Tests.Editor`    | `UI/Tests/Editor/`                             | EditMode tests (where a service window docks and how far the camera slides)            |
| `KryticalNova.Icons.Tests.Editor` | `Icons/Tests/Editor/`                          | EditMode tests (the comic icon treatment, the icon library's rules, and that every skill and item shows an icon of its own). Depends on Runtime and Editor |
| `KryticalNova.GameFeel.Tests.Editor` | `GameFeel/Tests/Editor/`                    | EditMode tests (the motion smear's rules against the walk and run speeds, the slow motion of a critical kill, the smear feedbacks on the game feel prefab). `!UNITY_SERVER` |

**Adding code to an existing system:** drop the file in the right folder; the folder's asmref already covers it.
**New system folder with code:** add an `.asmref` to `KryticalNova.Runtime` (runtime) or `KryticalNova.Editor`
(editor), or, if it has no dependencies on the monolith, give it its own `.asmdef` like Grass.

### Dependency direction (arrows point at what you may depend on)

```
              Core  ◄──────────────────────────────┐
               ▲                                   │
   ┌───────────┼──────────────┬──────────────┐     │
Gameplay     World       Environment     Characters│
(Player→Combat→Skills→Progression)  (Vegetation→Grass)
   ▲           ▲              ▲              ▲
   └───────────┴──────┬───────┴──────────────┘
              UI · Audio · VFX   (observe gameplay state/events; gameplay never references them)
              Tools / Editor     (may reference anything)
```

- Core knows nothing about gameplay.
- UI depends on gameplay, never the reverse. Gameplay exposes state and events; UI observes. (Existing pattern:
  `Core/I*Sink` interfaces + `ClientPresentationRegistry`.)
- Audio and VFX react to gameplay events. No `FindObjectOfType<AudioManager>()` in gameplay code.
- No circular references. Assembly boundaries make cycles a compile error, which is the point.

### Assembly roadmap: splitting `KryticalNova.Runtime`

The monolith can't be split yet because of two cycles. Break them first, then replace each system's `.asmref`
with its own `.asmdef`:

1. **Core → Progression**: `Core/ICombatFeedbackSink` uses `Progression.DamageResult`. Move `DamageResult`
   (or a slim struct) into Core, or move the sink interface into Progression.
2. **Player ↔ Combat**: `PointAndClickPlayer` references `PlayerMeleeCombat` / `CombatHostility`, and Combat
   references Player. Introduce an interface in the lower layer (e.g. `IMeleeAttacker` in Combat or Core).
3. Then split: `KryticalNova.Core` → `.Networking` → `.Progression` → `.Combat` → `.Player` → `.Items`
   → `.World` → `.Rendering`, verifying compile after each step.

---

## 5. Namespaces

`KryticalNova.<System>[.<Subsystem>]`. The category layer (`Gameplay`, `Environment`) is **not** part of the namespace.

| Folder                               | Namespace                          |
|--------------------------------------|------------------------------------|
| `Gameplay/Combat/Runtime`            | `KryticalNova.Combat`              |
| `Gameplay/Items/Runtime/Visuals`     | `KryticalNova.Items.Visuals`       |
| `World/Runtime/Portals`              | `KryticalNova.World.Portals`       |
| `Environment/Vegetation/Grass/*`     | `KryticalNova.Vegetation.Grass`    |
| Any `Editor/` folder                 | `KryticalNova.EditorTools[.<Area>]` |

(Avoid a `KryticalNova.Environment` namespace: it would shadow `System.Environment` inside `KryticalNova.*` code.)

---

## 6. Naming

- **C# types/files:** PascalCase, one public type per file, file name = type name. Interfaces `IName`
  (`IDamageable`, `IInteractable`). ScriptableObject classes describe the thing (`GrassType`, `WeatherProfile`,
  `ItemDefinition`). Don't suffix with `SO` in new code.
- **Assets:** descriptive and searchable: `Type_Group_Variant` or `Group_Variant`. Never `grass1`, `newgrass`,
  `grassFINAL_REAL`, `Copy of…`, `New Material`, `Untitled`, `… (1)`. The validator flags these.
- **Prefixes already established in this project** (use them for these asset types, don't invent new ones casually):

| Prefix  | Asset                      | Prefix | Asset                              |
|---------|----------------------------|--------|------------------------------------|
| `M_`    | Material                   | `GT_`  | GrassType                          |
| `T_`    | Texture                    | `GB_`  | GrassBiome                         |
| `SM_`   | Static mesh / mesh prefab  | `GRS_` | GrassRuleSet                       |
| `PFX_`  | Particle-effect prefab     | `WP_`  | WeatherProfile                     |
| `FX_`   | Effect prefab / FX material| `DC_`  | DayCycleProfile                    |
| `AC_`   | Animator controller        | `ZTP_` | ZoneTerrainProfile                 |
| `NPC_`  | NPC definition / prefab    | `LF_`  | Lens flare data                    |
| `LS_` / `APV_` | Lighting settings / probe volume data (scene-generated) | `TL_`  | Terrain layer |
| `UI_`   | UI element/screen asset (e.g. `UI_HUD_Target`) | `VFX_` | Future VFX Graph assets (`VFX_Combat_Hit`) |
| `RUIN_` | Baked ruin prefab/mesh (`RUIN_<src>_<Tier>_v<n>`, Tier = Damaged/Collapsed/Rubble/Sliced) | `DP_` | Quick Destruction profile |
| `VF_`   | Volumetric Fog & Mist 2 profile | | |

---

## 7. Third-party policy

- Import → it lands in `_ThirdParty/<Vendor>/` automatically. Never rearrange a vendor's internal folders and never
  save our own assets inside them.
- Vendor updates re-import into the original path; the enforcer merges new files into the existing
  `_ThirdParty/<Vendor>/` folder (same GUIDs → existing files update in place).
- If a vendor hardcodes its install path (check with a search for `"Assets/<Vendor>`), move it back to the root
  and add it to `PinnedVendorRoots` in `ProjectOrganizationRules.cs` and to the exceptions list in section 2.
- To customise vendor content: copy the asset into `_Game/<System>/` and own the copy (e.g. our `M_FoliageWind_*`
  materials built from Synty atlases), or wrap the vendor API from our code.
- UPM packages (`Packages/manifest.json`) are preferred over Asset Store `.unitypackage`s when available.
- Our editor builders reference vendor source assets by path constants (e.g. `"Assets/Synty/PolygonSciFiCity/…"`,
  `"Assets/_ThirdParty/KumaBeer/…"`). If a vendor moves, update those constants.

---

## 8. Tooling

Menu **Krytical Nova ▸ Project**:

| Item                                         | What it does                                                                           |
|----------------------------------------------|----------------------------------------------------------------------------------------|
| Auto-Quarantine Imported Packages (toggle)   | On by default. After any package import, new root folders move to `_ThirdParty/` (merging) |
| Quarantine Root Folders Into _ThirdParty...  | Same, on demand, for folders dropped in through Explorer. Asks first                   |
| Validate Project Organization                | Audits root entries, loose files, asset names, script folders, assembly coverage, `UnityEditor` usage in runtime code |

| Organize Scene Hierarchy                     | Files a zone scene's loose root objects under `--- X ---` headers and colours/icons every header with vHierarchy 2 |

Menu **Krytical Nova ▸ Setup** (the two wiring steps; both safe to run again at any time):

| Item                                         | What it does                                                                           |
|----------------------------------------------|----------------------------------------------------------------------------------------|
| Player Prefab                                | Wires every player component onto `Player.prefab` and hands each its settings asset (created with defaults when missing); builds the prefabs gameplay spawns (item drop, Kiosk stand) and registers them as network prefabs. **A new player system adds one row to `PlayerSystemsSetup`; a new spawned prefab adds its path to `GameplayPrefabsSetup.SpawnedPrefabs`** (a prefab that is not registered never appears on clients) |
| Zone Scene (Open Scene)                      | Prepares the open scene as a zone: HUD and its controllers (`HudSetup.MoreControllers`), zone UI, game feel, the audio mixer and who plays into it (`AudioSetup`), matte terrain, post look, atmosphere, minimap renderer, lighting, hierarchy headers, the NetworkManager's prefab list; in the starter town also the stalls whose NPC has arrived and the trade zones |

**Setup** also has **GM Console (Open Scene)** (`GmConsoleSetup`): builds `Tools/Debug/Prefabs/GmConsole.prefab` (the vendor Quantum Console prefab nested untouched, with `GmConsoleGate`) and puts it in the open zone scene.

Menu **Krytical Nova ▸ Build ▸ Windows Test Build** (`TestBuildBuilder`): the Windows build handed to testers. It takes only the scenes of `BuildSceneOrder`, is not a development build, and writes to `Builds/Windows/KryticalNovaII` (ignored by git) with a size report, largest first, in `Builds/Windows/BuildReport.txt`. A first build compiles every shader variant and takes a long time; later ones reuse the cache.

A vendor package whose runtime scripts cannot compile into a player build is kept out of builds with an Editor-only assembly definition in its own folder rather than by editing its files (owner, 6 Oct 2026): `_ThirdParty/GI/Scripts/GI.EditorOnly.asmdef`. The game must not use such a package at run time.

Menu **Krytical Nova ▸ Icons** (the icons of skills and of items that have no model; see `Design/Rendering-Bible.md` 6g): **Icon Library** is the browser (`IconLibraryWindow`): every skill or item on the left, the icon packs' pictures in the middle (by set, or the best free matches for the chosen skill by its tags), and on the right the chosen icon as painted and as the game will show it, its tags, and the button that gives it to the skill; giving bakes the treated copy and hands it over at once. **Bake Every Assigned Icon** redraws them all after a change to `IconTreatmentSettings`. **Check Icons** lists every skill or item without an icon of its own. **A new skill, or a new item without a model, gets its icon here, never by hand.** The packs (`_ThirdParty/Fantasy RPG Icons Pack`, `_ThirdParty/Cyberpunk RPG Icons Pack`) are only read, and only by these tools: the game shows the baked copies, so a copy of the project without the packs (4.2 GB of pictures) plays and checks clean; it just cannot bake or hand out icons.

Menu **Krytical Nova ▸ Items** also has **5. Generate Utility Items** (Stims, Kiosk Permits, Pet Ration, forge materials: one table in `UtilityItemGenerator`) and **7. Build Pets** (`PetAssetBuilder`). **8. Bake Weapon Models (glow zones, reach)** (`WeaponGlowZoneBaker`) reads every weapon model's mesh and writes onto its prefab which part glows when tempered (`WeaponGlowZone`) and its outermost points (`WeaponReach`, which keeps long weapons out of the ground); run it after adding or changing a weapon model. A zone ticked "keep as set" on a prefab is left alone.

Menu **Krytical Nova ▸ Characters** (run in order, or use *Bake Everything*):

| Item                                         | What it does                                                                           |
|----------------------------------------------|----------------------------------------------------------------------------------------|
| 1. Bake Sidekick Part Catalog                | Indexes every Sidekick part on disk into `SidekickPartCatalog.asset` (stable ids, loaded on demand) |
| 2. Build Faction Assets                      | The three faction assets and the catalog. Keeps text and colours tuned in the Inspector |
| 3. Bake Character Data                       | Lineages, palettes, ready-made faces and colour channels into `CharacterCreationDatabase.asset` |
| 4. Set Up Player Identity Components         | Adds `PlayerIdentity` / `CharacterLook` to the player prefabs                          |
| 5. Bake Expressions (Faces, Idles, Emotes)   | Fills `CharacterExpressionLibrary.asset` for the creator's Preview tab: Sidekick face poses, idles, emotes. Add clips in `CharacterExpressionBaker` |
| Clear Saved Characters                       | Deletes the three saved character slots (PlayerPrefs)                                  |

Menu **Krytical Nova ▸ Front End**:

| Item                                         | What it does                                                                           |
|----------------------------------------------|----------------------------------------------------------------------------------------|
| 1. Build Front-End Scenes                    | Regenerates the five `Scenes/MainMenu/` scenes from the Synty Fantasy Menus / Fantasy Screens prefabs and puts them first in the build order |
| Play From Title Screen (toggle)              | On: Play starts at the Title like a build. Off (default): Play runs the open scene, so the zone still starts directly |

**Editor test session** (`Networking/Runtime/EditorTestSession`, 7 Oct 2026): while the file `Library/KryticalNova.TestSession`
exists (two lines, `port=7788` and `save=<folder>`), every Editor of the project, the main one and its Multiplayer Play
Mode clones, hosts and joins on that port and keeps the server save in that folder, and says so in the console on entering
Play. It keeps an Editor test off a build running on the same PC, which hosts on 7777 and writes the default save. Delete
the file to go back to the defaults. A build never reads it, and nothing committed changes.

Menu **Krytical Nova ▸ UI**:

| Item                                         | What it does                                                                           |
|----------------------------------------------|----------------------------------------------------------------------------------------|
| Build Faction Skins                          | Regenerates `UI/Common/KryticalSkin_Null.uss` and `KryticalSkin_Covenant.uss` from the Hegemony sheets (the base layout). Run after editing a base sheet. Never edit the base sheets for one faction |

### Scene hierarchy

Zone scenes are grouped under `--- X ---` headers, in this order: Systems, Cameras, Lighting & FX, UI, Gameplay, Spawn,
NPCs, Portals, Environment, Town, Outskirts, Traffic. Builders own their headers; `SceneHierarchyOrganizer` files
everything hand-placed and applies the vHierarchy colours (Systems grey, Cameras blue, Lighting yellow, UI violet,
Gameplay red, Spawn/Portals magenta, NPCs cyan, Environment green, Town/Outskirts orange). `NetworkManager` and the
offline `Player` stay at the root (DontDestroyOnLoad / handoff), directly under their header. Headers are never
`EditorOnly`: they parent runtime objects. vHierarchy's data asset lives in `Tools/Editor/Hierarchy/`.

Code: `_Game/Tools/Editor/Organization/` (`ProjectOrganizationRules`, `ThirdPartyImportEnforcer`,
`ProjectOrganizationValidator`, `SceneHierarchyOrganizer`). Generic type-bucket folders created by a package (`Scripts/`, `Materials/`…) are
**flagged, not moved**: they might be ours, so a human decides.

---

## 9. Moving or renaming assets

- Move inside Unity (Project window, or `AssetDatabase.MoveAsset` from scripts), or move the file **and its `.meta`**
  together. GUIDs are what keep references alive.
- After moving, search code for the old path: editor builders use path constants
  (`grep -rn '"Assets/' Assets/_Game --include=*.cs`).
- Shaders `#include` across systems with absolute paths (`"Assets/_Game/World/Sky/Shaders/SkyCommon.hlsl"`).
  Same-folder includes may stay relative.

---

## 10. Addressables (when adopted)

The folder structure answers "who owns this?"; Addressables groups answer "what loads together?". They are different
on purpose. Planned groups: `Bootstrap`, `MainMenu`, `World_Common`, `World_<Zone>`, `Characters_Common`,
`Characters_Enemies`, `VFX_Common`, `Audio_Common`. Don't make every folder a group.

---

## 11. Checklist for anything new

1. Which system owns it? → folder from section 1/3. Create the system's sub-folders only as needed.
2. Code → `Runtime/`, `Editor/`, `Data/` or `Tests/` of that system; make sure an asmdef/asmref covers it.
3. ScriptableObject class in `Data/`, instances in `Data/Assets/`.
4. Name it per section 6.
5. Editor builders that generate assets write into the owning system's folders, never a root bucket.
   A new skill, or a new item without a model, gets its icon in **Krytical Nova ▸ Icons ▸ Icon Library**.
6. Run **Krytical Nova ▸ Project ▸ Validate Project Organization** before committing big changes.
