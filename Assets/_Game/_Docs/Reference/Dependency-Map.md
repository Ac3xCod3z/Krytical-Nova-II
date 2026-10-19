# Dependency Map

How the game's scripts depend on each other: which system uses which, where a reference runs the wrong way, which
classes are expensive to change, how far the ScriptableObject design data reaches, and which parts no test guards.
It is the starting point for `Plans/architecture-decoupling-plan.md`.

**The page:** https://claude.ai/artifact/Uvq5NyTWtTvYWNVoo45gdm. It has the map of systems, a page for every system
and class, the ranked problems with the file and line of each, the hubs, the design data and the tests.

**Re-run it** from the project folder (a few seconds; Unity can stay closed):

```bash
python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py"
```

It rewrites the tables of this doc between the `map:` markers. It also builds `out/dependency-map.html` beside the
script, which is then published to the link above. `--baseline` saves today's uphill links and two-way pairs, and
`--check` fails when a new one appears.

## Today

<!-- map:summary -->
Generated 19 Oct 2026, 11:30: 634 runtime scripts (119,112 lines) declaring 899 types (165 MonoBehaviours, 61 NetworkBehaviours, 87 ScriptableObject classes); 108 test scripts; 4025 prefabs, 46 scenes and 2579 assets read. 195 system-to-system links, of which 0 run uphill, 10 pairs of systems use each other both ways, and 17 links would have to go for the systems to form a clean stack. The largest tangle, classes that all reach each other through some chain of uses, holds 26 classes from 7 systems (Gameplay/Player 9, Gameplay/Combat 4, World 4, Gameplay/CharacterCreation 3, Gameplay/Items 3, Gameplay/Persistence 2, ...).
<!-- /map:summary -->

## How to read it

- **Layers.** From the bottom:
  - Foundation: Core.
  - Infrastructure: Networking, Rendering.
  - Simulation: every Gameplay system, World, Environment.
  - Presentation: UI, GameFeel, Audio, Icons.
  - Tooling: Tools.

  A system may use its own layer and the ones below. This is section 4 of `ProjectOrganization.md` written as numbers.
- **Uphill.** A link from a lower layer into a higher one. The lower system then cannot be changed, tested or split out
  without dragging the higher one along.
- **Loop.** Two systems of the same layer that use each other, directly or around a longer circle. The map orders each
  layer so that as few links as possible point backwards. The ones still pointing backwards are the cuts. They are why
  `KryticalNova.Runtime` cannot be split into one assembly per system yet.
- **Ripple.** For a class: the files that use it directly, and the types that reach it through others. A large ripple
  means a change to it travels far.
- **Instability.** Uses ÷ (uses + used by). Near 0, many lean on it and it should change rarely. Near 1, it leans on
  others and is free to change.
- **What it cannot see.** It reads the code as text, not through the compiler. It misses:
  - reflection and string lookups (counted separately below);
  - UnityEvents wired in the Inspector;
  - Editor code, which is left out on purpose;
  - links that only go through an interface.

  A name two namespaces both declare is skipped rather than guessed. Every link it shows names its file and line.

## Systems

<!-- map:systems -->
| System | Layer | Scripts | Lines | Uses (types) | Used by (types) | Instability | Tests | Types touched by a test |
|---|---|---|---|---|---|---|---|---|
| Core | Foundation | 41 | 3,524 | 0 | 174 | 0.0 | 4 | 13 of 29 |
| Networking | Infrastructure | 37 | 6,713 | 5 | 67 | 0.07 | 2 | 24 of 48 |
| Rendering | Infrastructure | 14 | 2,809 | 1 | 26 | 0.04 | 0 | 1 of 18 |
| Environment | Simulation | 6 | 834 | 0 | 2 | 0.0 | 0 | 0 of 6 |
| Gameplay/Autopilot | Simulation | 11 | 1,448 | 41 | 12 | 0.77 | 3 | 6 of 12 |
| Gameplay/Crafting | Simulation | 3 | 400 | 19 | 9 | 0.68 | 1 | 4 of 6 |
| Gameplay/Forge | Simulation | 1 | 411 | 39 | 4 | 0.91 | 0 | 0 of 2 |
| Gameplay/Restyle | Simulation | 3 | 822 | 40 | 5 | 0.89 | 0 | 0 of 3 |
| Gameplay/Economy | Simulation | 25 | 4,234 | 50 | 63 | 0.44 | 8 | 15 of 39 |
| Gameplay/Social | Simulation | 39 | 5,624 | 45 | 49 | 0.48 | 9 | 23 of 52 |
| Gameplay/Housing | Simulation | 17 | 3,364 | 19 | 38 | 0.33 | 2 | 14 of 25 |
| Gameplay/CharacterCreation | Simulation | 24 | 2,487 | 26 | 54 | 0.33 | 8 | 20 of 24 |
| Gameplay/Pets | Simulation | 12 | 1,445 | 33 | 32 | 0.51 | 4 | 13 of 19 |
| Gameplay/Hostiles | Simulation | 23 | 2,870 | 40 | 28 | 0.59 | 4 | 10 of 23 |
| Gameplay/Persistence | Simulation | 8 | 2,155 | 34 | 13 | 0.72 | 11 | 5 of 11 |
| World | Simulation | 33 | 5,859 | 13 | 30 | 0.3 | 1 | 7 of 39 |
| Gameplay/Player | Simulation | 23 | 9,482 | 63 | 40 | 0.61 | 3 | 9 of 25 |
| Gameplay/Items | Simulation | 58 | 10,721 | 49 | 175 | 0.22 | 12 | 38 of 81 |
| Gameplay/Combat | Simulation | 12 | 3,016 | 39 | 27 | 0.59 | 3 | 4 of 13 |
| Gameplay/Progression | Simulation | 46 | 7,906 | 23 | 194 | 0.11 | 6 | 27 of 58 |
| Gameplay/NPC | Simulation | 2 | 227 | 5 | 17 | 0.23 | 0 | 0 of 2 |
| Gameplay/Factions | Simulation | 6 | 169 | 0 | 25 | 0.0 | 0 | 1 of 3 |
| GameFeel | Presentation | 26 | 4,476 | 41 | 2 | 0.95 | 4 | 17 of 34 |
| UI | Presentation | 129 | 32,492 | 292 | 4 | 0.99 | 17 | 32 of 134 |
| Audio | Presentation | 6 | 645 | 4 | 8 | 0.33 | 1 | 1 of 7 |
| Tools | Tooling | 29 | 4,979 | 177 | 0 | 1.0 | 2 | 0 of 31 |
<!-- /map:systems -->

## Links running uphill

<!-- map:uphill -->
| From | Uses (higher layer) | Files | Types it uses | First place |
|---|---|---|---|---|
<!-- /map:uphill -->

## Loops to cut

Each row is the cheaper side of a loop: cut these references and the two systems stop needing each other.

<!-- map:cycles -->
| Cut this link | Files | Types it uses | The other way (files) |
|---|---|---|---|
| Gameplay/Combat -> Gameplay/Player | 4 | CharacterAnimState, FootGroundingIK, PlayerAnimancerMotor, PlayerIdentity, PlayerWallRunMotor, PointAndClickPlayer, WallRunPhase | 4 |
| Gameplay/Items -> Gameplay/Persistence | 3 | PlayerSaveAgent, ServerSave | 2 |
| Gameplay/Player -> Gameplay/CharacterCreation | 3 | BodyType, CharacterCreationDatabase, CharacterLook, CharacterNameRules, CharacterNetState, CharacterProfile, CharacterRecipe, CharacterRecipeValidator, Characte | longer loop |
| Gameplay/Social -> Gameplay/Economy | 3 | CredsSink, EconomyLedger, PlayerWallet, TradeZone | 4 |
| Gameplay/Hostiles -> Gameplay/Social | 2 | PartyRules, PlayerChat, PlayerParty, PlayerSponsor, SponsorRules | longer loop |
| Gameplay/Player -> World | 2 | EnvironmentPresenter, SurfaceGraph, SurfaceRoute, ZoneRules | 4 |
| Gameplay/Combat -> Gameplay/Economy | 1 | TradeZone | longer loop |
| Gameplay/Combat -> World | 1 | ZoneRules | longer loop |
| Gameplay/Hostiles -> Gameplay/Pets | 1 | PlayerPet | 1 |
| Gameplay/Hostiles -> Gameplay/Economy | 1 | TradeZone | longer loop |
| Gameplay/Items -> Gameplay/Social | 1 | PlayerChat | 2 |
| Gameplay/Items -> Gameplay/Economy | 1 | CredsSink, PlayerWallet | 16 |
| Gameplay/Items -> Gameplay/CharacterCreation | 1 | CharacterLook | 12 |
| Gameplay/Persistence -> Gameplay/CharacterCreation | 1 | CharacterProfile, CharacterSession, WornPiece | longer loop |
| Gameplay/Pets -> Gameplay/Social | 1 | PlayerChat | longer loop |
| Gameplay/Player -> Gameplay/Housing | 1 | PlayerHousing | 2 |
| Gameplay/Progression -> Gameplay/Economy | 1 | CredsSink, PlayerWallet | 5 |
<!-- /map:cycles -->

## Classes with the widest ripple

<!-- map:ripple -->
| Type | System | Kind | Files using it | Systems | Types reached through others | Tests | Prefabs / scenes holding it |
|---|---|---|---|---|---|---|---|
| ClientPresentationRegistry | Core | plain | 80 | 18 | 242 | 1 | 0 |
| FactionId | Gameplay/Factions | enum | 75 | 14 | 296 | 24 | 0 |
| ItemDefinition | Gameplay/Items | so | 65 | 12 | 254 | 20 | 0 |
| PlayerInventory | Gameplay/Items | net | 65 | 14 | 210 | 0 | 1 |
| ItemStack | Gameplay/Items | plain | 48 | 10 | 243 | 12 | 0 |
| EquipSlot | Gameplay/Items | enum | 39 | 10 | 277 | 15 | 0 |
| CallKind | Networking | enum | 39 | 13 | 265 | 1 | 0 |
| RpcBudget | Networking | plain | 39 | 13 | 252 | 1 | 0 |
| PlayerResources | Gameplay/Progression | net | 35 | 13 | 221 | 0 | 41 |
| PlayerIdentity | Gameplay/Player | net | 34 | 12 | 210 | 0 | 1 |
| LocalPlayer | Core | plain | 33 | 14 | 213 | 1 | 0 |
| PointAndClickPlayer | Gameplay/Player | net | 31 | 9 | 210 | 0 | 41 |
| CharacterCombatStats | Gameplay/Progression | mono | 29 | 11 | 235 | 1 | 51 |
| PlayerCharacterProgression | Gameplay/Progression | net | 27 | 12 | 236 | 0 | 41 |
| WeaponClassId | Gameplay/Progression | enum | 26 | 8 | 290 | 8 | 0 |
| CombatEntity | Gameplay/Progression | net | 26 | 9 | 212 | 0 | 13 |
| StatType | Gameplay/Progression | enum | 24 | 7 | 308 | 7 | 0 |
| NpcOffer | Gameplay/NPC | enum | 24 | 9 | 217 | 1 | 0 |
| SaveScope | Core | enum | 22 | 9 | 240 | 0 | 0 |
| SkillDefinitionSO | Gameplay/Progression | so | 22 | 8 | 227 | 7 | 0 |
| PerformanceLog | Core | plain | 20 | 9 | 252 | 1 | 0 |
| ISaveParticipant | Core | interface | 20 | 9 | 239 | 0 | 0 |
| CharacterRecipe | Gameplay/CharacterCreation | plain | 20 | 6 | 226 | 4 | 0 |
| KeyBindings | Core | plain | 20 | 4 | 215 | 2 | 0 |
| ItemCatalog | Gameplay/Items | so | 20 | 8 | 215 | 7 | 0 |
<!-- /map:ripple -->

## Hubs (static members reached from other systems)

<!-- map:hubs -->
| Static member | Kind | Runtime files | Other systems |
|---|---|---|---|
| PlayerInventory.Local | locator | 34 | GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles, Gameplay/Persistence, Tools, UI |
| HostileNet.Active | locator | 5 | Gameplay/Autopilot, Tools, UI |
| WorldDrop.Active | locator | 4 | GameFeel, Gameplay/Autopilot, UI |
| PlayerInventory.LocalChanged | event | 20 | Gameplay/Persistence, UI |
| NpcInteractable.Active | locator | 6 | Gameplay/Economy, UI |
| NetworkCombatVFXPool.Instance | locator | 5 | GameFeel, Gameplay/Combat |
| PointAndClickPlayer.Active | locator | 5 | UI, World |
| ZonePortal.Active | locator | 4 | Tools, UI |
| PlayerSpawnPoint.Active | locator | 3 | Gameplay/Player, Tools |
| ZoneRules.Current | locator | 3 | Gameplay/Player, Tools |
| PlayerDuel.Local | locator | 3 | Gameplay/Social, UI |
| PointOfInterest.Active | locator | 3 | Tools, UI |
| CombatEntity.AnyKilled | event | 2 | Gameplay/Hostiles, Gameplay/Pets |
| KeyBindings.Changed | event | 2 | Gameplay/Progression, UI |
| MusicConductor.Instance | locator | 2 | Tools, UI |
| JoinFlow.Failed | event | 2 | Tools, UI |
| WorldEnvironmentNetwork.Instance | locator | 10 | Tools |
| GearUIController.Instance | locator | 20 |  |
<!-- /map:hubs -->

## God classes

Classes over 700 lines. Several of them also reach into three or more other systems.

<!-- map:gods -->
| Class | System | Lines | Other systems it uses | Files using it |
|---|---|---|---|---|
| ProgressionHUD | UI | 2090 | Core, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 4 |
| PointAndClickPlayer | Gameplay/Player | 1989 | Core, Gameplay/Combat, Gameplay/Progression, Networking, Rendering | 31 |
| PlayerWallRunMotor | Gameplay/Player | 1715 | Core, Gameplay/Progression, Networking, World | 6 |
| ArrangeController | UI | 1663 | Core, Gameplay/Housing, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 0 |
| GearUIController | UI | 1464 | Core, Gameplay/Economy, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression | 20 |
| PlayerAnimancerMotor | Gameplay/Player | 1420 | Core, Gameplay/Progression, Networking | 13 |
| GameFeelPresenter | GameFeel | 1181 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression, Networking, UI | 1 |
| PetFollower | Gameplay/Items | 1048 | Rendering | 4 |
| PlayerMeleeCombat | Gameplay/Combat | 977 | Core, Gameplay/Player, Gameplay/Progression, Networking | 11 |
| HudLayoutEditor | UI | 964 | - | 12 |
| GearWorkWindowController | UI | 947 | Core, Gameplay/Economy, Gameplay/Forge, Gameplay/Items, Gameplay/NPC, Gameplay/Progression | 0 |
| GmCommands | Tools | 904 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Player, Gameplay/Progression, Gameplay/Social, Networking, World | 0 |
| PlayerCombatSkills | Gameplay/Progression | 869 | Core, Networking | 18 |
| NetworkConnectionManager | Networking | 826 | Core | 11 |
| PlayerJumpMotor | Gameplay/Player | 807 | Gameplay/Progression, Networking | 7 |
| PlayerInventory | Gameplay/Items | 788 | Core, Gameplay/Factions, Gameplay/Progression, Networking | 65 |
| CharacterCreatorController | UI | 767 | Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Items, Gameplay/Progression | 0 |
| ServerSave | Gameplay/Persistence | 735 | Core, Networking | 16 |
| PlayerVaultMotor | Gameplay/Player | 710 | Core, Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Progression | 2 |
<!-- /map:gods -->

## Design data

Each ScriptableObject class, how many assets of it exist, and where they are wired from.

<!-- map:data -->
| ScriptableObject class | System | Assets | Read by (runtime files) | Wired from prefabs / scenes / other assets |
|---|---|---|---|---|
| ItemDefinition | Gameplay/Items | 1792 | 65 | 0 / 0 / 338 |
| FurnishingDefinition | Gameplay/Housing | 223 | 8 | 0 / 0 / 1 |
| SkillDefinitionSO | Gameplay/Progression | 152 | 22 | 1 / 0 / 1 |
| HallLookDefinition | Gameplay/Housing | 50 | 6 | 0 / 0 / 1 |
| ZoneMinimapData | World | 39 | 3 | 0 / 40 / 0 |
| StatDefinitionData | Gameplay/Progression | 19 | 0 | 0 / 0 / 0 |
| NpcDefinition | Gameplay/NPC | 17 | 3 | 17 / 0 / 3 |
| MusicTrack | Audio | 13 | 6 | 0 / 0 / 1 |
| WeatherProfile | World | 11 | 4 | 0 / 40 / 12 |
| HostileDefinition | Gameplay/Hostiles | 10 | 8 | 10 / 0 / 8 |
| HostileZoneDefinition | Gameplay/Hostiles | 8 | 2 | 0 / 2 / 0 |
| GrassType | Environment | 6 | 2 | 0 / 0 / 2 |
| LineageDefinition | Gameplay/CharacterCreation | 6 | 8 | 0 / 0 / 1 |
| PetChatterSet | Gameplay/Pets | 4 | 2 | 0 / 0 / 4 |
| PetDefinition | Gameplay/Pets | 4 | 12 | 0 / 0 / 1 |
| VaultAnimationSet | Gameplay/Player | 4 | 1 | 0 / 0 / 1 |
| VendorStock | Gameplay/Economy | 3 | 4 | 0 / 0 / 1 |
| FactionDefinition | Gameplay/Factions | 3 | 6 | 0 / 0 / 1 |
| FactionHudKit | UI | 3 | 2 | 0 / 40 / 0 |
| UiSkin | UI | 3 | 1 | 1 / 0 / 0 |
| GrassBiome | Environment | 2 | 2 | 0 / 0 / 1 |
| CharacterClassData | Gameplay/Progression | 2 | 2 | 1 / 2 / 0 |
| MovementSkillData | Gameplay/Progression | 2 | 2 | 1 / 0 / 0 |
| ZoneMusic | Audio | 1 | 2 | 0 / 1 / 0 |
| GrassRuleSet | Environment | 1 | 1 | 0 / 40 / 0 |
| HitLookSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| SmearSettings | GameFeel | 1 | 2 | 1 / 0 / 0 |
| SpiritSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| AutopilotSettings | Gameplay/Autopilot | 1 | 5 | 1 / 0 / 1 |
| CharacterCreationDatabase | Gameplay/CharacterCreation | 1 | 17 | 2 / 4 / 0 |
| CharacterExpressionLibrary | Gameplay/CharacterCreation | 1 | 7 | 0 / 3 / 1 |
| CharacterPaletteLibrary | Gameplay/CharacterCreation | 1 | 2 | 0 / 0 / 1 |
| CadenceSettings | Gameplay/Combat | 1 | 4 | 1 / 0 / 0 |
| CraftingSettings | Gameplay/Crafting | 1 | 1 | 1 / 0 / 0 |
| EconomySettings | Gameplay/Economy | 1 | 8 | 1 / 0 / 1 |
| VendorCatalog | Gameplay/Economy | 1 | 2 | 1 / 0 / 0 |
| FactionCatalog | Gameplay/Factions | 1 | 1 | 0 / 0 / 1 |
| HostileSettings | Gameplay/Hostiles | 1 | 5 | 10 / 2 / 1 |
| HostileStatCurve | Gameplay/Hostiles | 1 | 4 | 10 / 0 / 0 |
| LevelGapSettings | Gameplay/Hostiles | 1 | 6 | 10 / 0 / 0 |
| FurnishingCatalog | Gameplay/Housing | 1 | 6 | 1 / 0 / 0 |
| HousingSettings | Gameplay/Housing | 1 | 9 | 1 / 0 / 0 |
| FlaskSettings | Gameplay/Items | 1 | 1 | 1 / 0 / 0 |
| GearProgressionSettings | Gameplay/Items | 1 | 11 | 1 / 2 / 1 |
| GearThemeLibrary | Gameplay/Items | 1 | 11 | 0 / 0 / 403 |
| ItemGlowIcons | Gameplay/Items | 1 | 1 | 0 / 0 / 1 |
| GearDictionary | Gameplay/Items | 1 | 6 | 1 / 2 / 0 |
| GearStatTable | Gameplay/Items | 1 | 2 | 0 / 0 / 1 |
| ItemCatalog | Gameplay/Items | 1 | 20 | 2 / 3 / 1 |
| WeaponClassCatalog | Gameplay/Items | 1 | 2 | 1 / 1 / 0 |
| SidekickPartCatalog | Gameplay/Items | 1 | 7 | 0 / 0 / 735 |
| PetCatalog | Gameplay/Pets | 1 | 3 | 1 / 0 / 0 |
| PetChatterSettings | Gameplay/Pets | 1 | 2 | 1 / 0 / 0 |
| PetEmojiLibrary | Gameplay/Pets | 1 | 0 | 0 / 0 / 0 |
| PetSettings | Gameplay/Pets | 1 | 2 | 0 / 0 / 1 |
| RecallSettings | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| TraversalSettings | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| VaultAnimationMap | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| WallstrideSettings | Gameplay/Player | 1 | 4 | 1 / 0 / 0 |
| CharacterProgressionData | Gameplay/Progression | 1 | 2 | 1 / 0 / 1 |
| CharacterResourceData | Gameplay/Progression | 1 | 1 | 1 / 0 / 0 |
| CharacterStatData | Gameplay/Progression | 1 | 1 | 1 / 0 / 0 |
| CombatStatTuningData | Gameplay/Progression | 1 | 6 | 11 / 2 / 1 |
| JumpMotionData | Gameplay/Progression | 1 | 2 | 1 / 0 / 0 |
| MovementSettings | Gameplay/Progression | 1 | 2 | 1 / 0 / 0 |
| NovaSettings | Gameplay/Progression | 1 | 4 | 1 / 0 / 1 |
| SerumSettings | Gameplay/Progression | 1 | 5 | 1 / 0 / 1 |
| CrewSettings | Gameplay/Social | 1 | 5 | 1 / 0 / 1 |
| CrewSymbolLibrary | Gameplay/Social | 1 | 3 | 0 / 0 / 1 |
| EmoteWheelSettings | Gameplay/Social | 1 | 3 | 1 / 0 / 0 |
| SponsorSettings | Gameplay/Social | 1 | 3 | 1 / 0 / 1 |
| SquadSettings | Gameplay/Social | 1 | 2 | 1 / 0 / 0 |
| WorldLodSettings | Rendering | 1 | 2 | 1 / 0 / 0 |
| GlowHaloFeature | Rendering | 1 | 0 | 0 / 0 / 2 |
| SunShaftsFeature | Rendering | 1 | 0 | 0 / 0 / 1 |
| GuideLibrary | UI | 1 | 5 | 0 / 41 / 0 |
| ToastSettings | UI | 1 | 1 | 0 / 40 / 0 |
| KeyGlyphSet | UI | 1 | 2 | 0 / 4 / 0 |
| MenuBookSettings | UI | 1 | 3 | 1 / 0 / 0 |
| DayCycleProfile | World | 1 | 2 | 0 / 40 / 0 |
| SurfaceGraph | World | 1 | 5 | 0 / 0 / 1 |
| WeatherCatalog | World | 1 | 1 | 0 / 40 / 0 |
| WorldScaleSettings | World | 1 | 0 | 0 / 0 / 0 |
| ZoneCatalog | World | 1 | 4 | 0 / 39 / 0 |
| ZoneTerrainProfile | World | 1 | 0 | 0 / 0 / 0 |
| ComicLetteringFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
| ComicStyleFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
<!-- /map:data -->

Where tuning still lives in code, and other links the compiler cannot see:

<!-- map:smells -->
- **Gameplay numbers fixed in code:** 326 in 98 files. Most: `Gameplay/Crafting/Data/CraftingSettings.cs` (28), `Gameplay/Player/Runtime/PlayerVaultMotor.cs` (19), `Gameplay/Player/Runtime/PointAndClickPlayer.cs` (17), `Gameplay/Player/Runtime/PlayerWallRunMotor.cs` (16), `Gameplay/Items/Data/GearProgressionSettings.cs` (13), `Gameplay/Items/Runtime/WeaponPaintRules.cs` (11), `Gameplay/Player/Runtime/PlayerJumpMotor.cs` (10), `Gameplay/Player/Runtime/PlayerWallRunMotor.WallWalk.cs` (10).
- **Settings fallbacks** (`settings != null ? settings.x : 8`): 93.
- **Runtime writes to shared assets** (ScriptableObject or pipeline fields): 24 — `Rendering/Data/WorldLodSettings.cs:125`, `Rendering/Runtime/ResidentDrawerOcclusion.cs:24`, `Rendering/Runtime/ResidentDrawerOcclusion.cs:32`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:142`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:143`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:144`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:145`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:146`.
- **Lookups by name** (Type.GetType, Find("..."), Resources.Load, SendMessage): 9.
- **Asset entries that store a class by name and assembly** ([SerializeReference]): 169 in 2 assets.
- **Static accessors that call GetComponent on every use:** 0.
- **FindObject calls in runtime code:** 27.
<!-- /map:smells -->

## The problems, ranked

Ranked by impact (1 to 5: how much it gets in the way of changing the game later) and, among equals, the cheaper fix
first. The page lists the file and line behind each.

<!-- map:problems -->
| # | Kind | Problem | Systems | Impact (1-5) | Files a fix touches | Effort |
|---|---|---|---|---|---|---|
| 1 | Hidden link | 169 asset entries store a class by name and assembly | GameFeel | 4 | 6 | small |
| 2 | Cycle | Gameplay/Items and Gameplay/Persistence need each other | Gameplay/Items, Gameplay/Persistence | 4 | 3 | small |
| 3 | Cycle | Gameplay/Social and Gameplay/Economy need each other | Gameplay/Social, Gameplay/Economy | 4 | 3 | small |
| 4 | Cycle | Gameplay/Player and World need each other | Gameplay/Player, World | 4 | 2 | small |
| 5 | Cycle | Gameplay/Hostiles and Gameplay/Pets need each other | Gameplay/Hostiles, Gameplay/Pets | 4 | 1 | small |
| 6 | Cycle | Gameplay/Items and Gameplay/CharacterCreation need each other | Gameplay/Items, Gameplay/CharacterCreation | 4 | 1 | small |
| 7 | Cycle | Gameplay/Items and Gameplay/Economy need each other | Gameplay/Items, Gameplay/Economy | 4 | 1 | small |
| 8 | Cycle | Gameplay/Items and Gameplay/Social need each other | Gameplay/Items, Gameplay/Social | 4 | 1 | small |
| 9 | Cycle | Gameplay/Player and Gameplay/Housing need each other | Gameplay/Player, Gameplay/Housing | 4 | 1 | small |
| 10 | Cycle | Gameplay/Progression and Gameplay/Economy need each other | Gameplay/Progression, Gameplay/Economy | 4 | 1 | small |
| 11 | God class | PlayerInventory (788 lines) | Gameplay/Items | 4 | 66 | large |
| 12 | Hub | PlayerInventory.Local (locator) | Gameplay/Items, GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles ... | 4 | 34 | large |
| 13 | Cycle | Gameplay/Combat and Gameplay/Player need each other | Gameplay/Combat, Gameplay/Player | 4 | 4 | medium |
| 14 | Cycle | Gameplay/Player uses Gameplay/CharacterCreation against the order | Gameplay/Player, Gameplay/CharacterCreation | 3 | 3 | small |
| 15 | Hub | PlayerDuel.Local (locator) | Gameplay/Combat, Gameplay/Social, UI | 3 | 3 | small |
| 16 | Hub | PlayerSpawnPoint.Active (locator) | Networking, Gameplay/Player, Tools | 3 | 3 | small |
| 17 | Hub | PointOfInterest.Active (locator) | World, Tools, UI | 3 | 3 | small |
| 18 | Hub | ZoneRules.Current (locator) | World, Gameplay/Player, Tools | 3 | 3 | small |
| 19 | Hub | CombatEntity.AnyKilled (event) | Gameplay/Progression, Gameplay/Hostiles, Gameplay/Pets | 3 | 2 | small |
| 20 | Cycle | Gameplay/Hostiles uses Gameplay/Social against the order | Gameplay/Hostiles, Gameplay/Social | 3 | 2 | small |
| 21 | Hub | JoinFlow.Failed (event) | Networking, Tools, UI | 3 | 2 | small |
| 22 | Hub | KeyBindings.Changed (event) | Core, Gameplay/Progression, UI | 3 | 2 | small |
| 23 | Hub | MusicConductor.Instance (locator) | Audio, Tools, UI | 3 | 2 | small |
| 24 | Cycle | Gameplay/Combat uses Gameplay/Economy against the order | Gameplay/Combat, Gameplay/Economy | 3 | 1 | small |
| 25 | Cycle | Gameplay/Combat uses World against the order | Gameplay/Combat, World | 3 | 1 | small |
| 26 | Cycle | Gameplay/Hostiles uses Gameplay/Economy against the order | Gameplay/Hostiles, Gameplay/Economy | 3 | 1 | small |
| 27 | Cycle | Gameplay/Persistence uses Gameplay/CharacterCreation against the order | Gameplay/Persistence, Gameplay/CharacterCreation | 3 | 1 | small |
| 28 | Cycle | Gameplay/Pets uses Gameplay/Social against the order | Gameplay/Pets, Gameplay/Social | 3 | 1 | small |
| 29 | Design data | 326 gameplay numbers fixed in code | Gameplay/Autopilot, Gameplay/CharacterCreation, Gameplay/Combat, Gameplay/Crafting, Gameplay/Economy ... | 3 | 98 | large |
| 30 | Hub | WorldEnvironmentNetwork.Instance (locator) | World, Tools | 3 | 10 | medium |
| 31 | God class | PointAndClickPlayer (1989 lines) | Gameplay/Player | 3 | 32 | large |
| 32 | Hub | NpcInteractable.Active (locator) | Gameplay/NPC, Gameplay/Economy, UI | 3 | 6 | medium |
| 33 | Hub | HostileNet.Active (locator) | Gameplay/Hostiles, Gameplay/Autopilot, Tools, UI | 3 | 5 | medium |
| 34 | Hub | NetworkCombatVFXPool.Instance (locator) | Networking, GameFeel, Gameplay/Combat | 3 | 5 | medium |
| 35 | Hub | PointAndClickPlayer.Active (locator) | Gameplay/Player, UI, World | 3 | 5 | medium |
| 36 | Hub | WorldDrop.Active (locator) | Gameplay/Items, GameFeel, Gameplay/Autopilot, UI | 3 | 4 | medium |
| 37 | Hub | ZonePortal.Active (locator) | World, Tools, UI | 3 | 4 | medium |
| 38 | Untested | CombatEntity has no test | Gameplay/Progression | 3 | 1 | medium |
| 39 | Untested | PlayerCharacterProgression has no test | Gameplay/Progression | 3 | 1 | medium |
| 40 | Untested | PlayerIdentity has no test | Gameplay/Player | 3 | 1 | medium |
<!-- /map:problems -->

## What it found (19 Oct 2026)

The short version: the walls that exist are good. The trouble sits inside the one big runtime assembly, where the
gameplay systems lean on each other in loops. Most of those loops hang on one to five files each.

**What is healthy**

- **Presentation is fenced off.** UI and GameFeel are assemblies of their own, so no gameplay code can call them.
  Gameplay reaches the screen only through `ClientPresentationRegistry` and the `Core/I*Sink` interfaces.
- **Saving is decentralised.** Systems save themselves through `ISaveParticipant` (21 implementers). The
  all-or-nothing rules (`ServerSave.SaveTogether`, the ledger) sit in one place.
- **Content is ScriptableObject data.**
  - 87 ScriptableObject classes and 2,438 assets of them, including 1,792 items.
  - No gameplay asset is written during Play.
  - Designers already tune items, skills, pets, enemies, vendors and zones in the Inspector.

**The tangle**

57 classes from 10 systems all reach each other through some chain of uses. The biggest shares are Progression 14,
Player 11, Networking 9, then World, Combat, Social, Items, CharacterCreation, Persistence and Economy. That is why
`KryticalNova.Runtime` cannot be split per system yet, and why a change near the player can travel so far.

**Six places hold most of it together**

1. **Networking reaches up into gameplay.** 6 of the 8 uphill links start in Networking:
   - the server's control page, bans, records, security flags and shutdown use `PlayerIdentity`, `ServerSave`,
     `PlayerChat` and `PlayerCharacterProgression`;
   - `NetworkConnectionManager` uses `ServerSave` and `CharacterSession`.

   This is server administration code living in the transport layer.
2. **Progression is two things.** Its lower half is the combat model that Items, Hostiles and Combat all need:
   `CombatEntity`, `IDamageable`, `CombatCalculator`, the buffs and the stats. Its upper half is player features
   (Serums, Experience Boosts, Nova, the skill items) that use Items, Economy and Persistence. One folder holding both
   makes the Items ↔ Progression, Progression ↔ Economy and Progression ↔ Persistence loops.
3. **`PlayerInventory.Local` is how the game finds the local player.** 63 runtime files in 17 systems use it, and 27
   static `X.Local` accessors run a `GetComponent` through it on every call. `PlayerInventory` therefore has the widest
   ripple in the game: 81 files use it directly, and no test touches it.
4. **Player ↔ Combat.**
   - Player uses Combat: the click-to-attack route in `PointAndClickPlayer`, death stopping the combo, recall checking
     the fight state.
   - Combat uses Player: the movement motors.
5. **The money types sit high up.** `PlayerWallet`, `CredsSink` and `EconomyLedger` are what Progression (Nova),
   Items (Flask) and Social (Crews) use from Economy. `TradeZone`, a place on the map, is used by Combat, Hostiles and
   Social.
6. **World ↔ Player.** Zone travel and portals move the player through `PointAndClickPlayer`. Trees and rain read the
   wall-run and jump motors.

**Smaller loops, one file each**

| File | Uses |
|---|---|
| `LootAnnouncements` | `PlayerChat` |
| `PlayerFlask` | the wallet |
| `CharacterPortraitStage` | `CharacterLook` |
| `WeaponVisualMount` | `WeaponHitbox` |
| `PlayerPet` | `HostileNet` and `PlayerChat` |
| `PlayerRecall` | `PlayerHousing` |
| `PlayerSkillHotbar` | `KeyBindings` |
| `PlayerCadence` | the music conductor (Combat using Audio) |
| `ICombatFeedbackSink` | `DamageResult` (Core using Progression) |

**Design data**

- **Tuning that still lives in code:**
  - 326 named gameplay numbers in code, most in the traversal motors and `PointAndClickPlayer`. Some are engineering
    limits that belong there.
  - Item IDs fixed as constants in `CraftingSettings` and `GearProgressionSettings`.
  - 93 `settings != null ? settings.x : N` fallbacks, each keeping a second copy of an asset's number.
- **Builders that rewrite data on every run** (Editor code; not traced by the map, found by reading it):
  - `HostileAssetBuilder` rebuilds the enemy roster, and says its output is not edited by hand.
  - `BladeSkillBuilder` rewrites the Monoblade skill numbers from its table.
  - `GameFeelBuilder*` holds the feel tables.

  A designer's Inspector edit to those is lost on the next build.

**Hidden links the compiler cannot see**

- 169 entries in `PFX_GameFeel` and `PFX_MenuFeel` store their class by name and assembly (`[SerializeReference]`
  feel entries). Renaming those classes, or moving them to another assembly, empties the entries unless the classes
  carry `[MovedFrom]`. This matters before any assembly split.
- 9 string lookups (`Shader.Find`, `GameObject.Find`, `Resources.Load`).
- 24 runtime writes to the render pipeline asset. Most are the graphics settings, which put the values back.
  `WorldLodSettings` does not.

**Tests are thinnest where the ripple is widest.** These classes have no test touching them: `PlayerInventory`,
`ClientPresentationRegistry`, `PlayerIdentity`, `PlayerResources`, `CombatEntity`, `PlayerCharacterProgression`,
`PointAndClickPlayer` and `RpcBudget`. Each is used by 25 to 81 files.

**Scenes hold the player.** The player prefab sits in 40 scenes, and those scenes are wired to its components:
`PointAndClickPlayer`, `PlayerResources` and `PlayerCharacterProgression` show 41 holders each. Moving those scripts
keeps working only if the `.meta` file moves with them, and renaming a serialized field needs `[FormerlySerializedAs]`.

What to do about it is `Plans/architecture-decoupling-plan.md`.
