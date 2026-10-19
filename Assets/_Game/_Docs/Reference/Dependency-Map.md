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
Generated 19 Oct 2026, 10:20: 621 runtime scripts (118,656 lines) declaring 885 types (165 MonoBehaviours, 61 NetworkBehaviours, 87 ScriptableObject classes); 106 test scripts; 4025 prefabs, 46 scenes and 2579 assets read. 209 system-to-system links, of which 8 run uphill, 21 pairs of systems use each other both ways, and 31 links would have to go for the systems to form a clean stack. The largest tangle, classes that all reach each other through some chain of uses, holds 57 classes from 10 systems (Gameplay/Progression 14, Gameplay/Player 11, Networking 9, World 4, Gameplay/Combat 4, Gameplay/Social 4, ...).
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
| Core | Foundation | 32 | 2,856 | 1 | 125 | 0.01 | 3 | 10 of 23 |
| Networking | Infrastructure | 36 | 6,696 | 11 | 64 | 0.15 | 1 | 23 of 47 |
| Rendering | Infrastructure | 14 | 2,809 | 1 | 26 | 0.04 | 0 | 1 of 18 |
| Environment | Simulation | 6 | 834 | 0 | 2 | 0.0 | 0 | 0 of 6 |
| Gameplay/Autopilot | Simulation | 11 | 1,448 | 39 | 12 | 0.76 | 3 | 6 of 12 |
| Gameplay/Crafting | Simulation | 3 | 400 | 18 | 9 | 0.67 | 1 | 4 of 6 |
| Gameplay/Forge | Simulation | 1 | 411 | 38 | 4 | 0.9 | 0 | 0 of 2 |
| Gameplay/Restyle | Simulation | 3 | 822 | 39 | 5 | 0.89 | 0 | 0 of 3 |
| Gameplay/Economy | Simulation | 25 | 4,242 | 47 | 65 | 0.42 | 8 | 16 of 40 |
| Gameplay/Social | Simulation | 38 | 5,604 | 44 | 50 | 0.47 | 9 | 23 of 51 |
| Gameplay/Hostiles | Simulation | 23 | 2,869 | 40 | 28 | 0.59 | 4 | 10 of 23 |
| Gameplay/Pets | Simulation | 12 | 1,445 | 31 | 32 | 0.49 | 4 | 13 of 19 |
| Gameplay/CharacterCreation | Simulation | 24 | 2,513 | 26 | 55 | 0.32 | 8 | 20 of 24 |
| Gameplay/Combat | Simulation | 11 | 2,974 | 43 | 25 | 0.63 | 3 | 4 of 12 |
| Gameplay/Housing | Simulation | 17 | 3,364 | 18 | 38 | 0.32 | 2 | 14 of 25 |
| Gameplay/Items | Simulation | 59 | 10,756 | 44 | 191 | 0.19 | 12 | 38 of 81 |
| Gameplay/Player | Simulation | 23 | 9,805 | 62 | 45 | 0.58 | 3 | 10 of 25 |
| World | Simulation | 33 | 5,860 | 13 | 30 | 0.3 | 1 | 7 of 39 |
| Gameplay/Persistence | Simulation | 7 | 2,105 | 31 | 17 | 0.65 | 11 | 5 of 10 |
| Gameplay/Progression | Simulation | 45 | 7,877 | 23 | 201 | 0.1 | 6 | 27 of 58 |
| Gameplay/NPC | Simulation | 2 | 227 | 5 | 17 | 0.23 | 0 | 0 of 2 |
| Gameplay/Factions | Simulation | 6 | 169 | 0 | 26 | 0.0 | 0 | 1 of 3 |
| GameFeel | Presentation | 26 | 4,476 | 41 | 2 | 0.95 | 4 | 17 of 34 |
| UI | Presentation | 129 | 32,484 | 291 | 4 | 0.99 | 17 | 32 of 134 |
| Audio | Presentation | 6 | 631 | 2 | 12 | 0.14 | 1 | 1 of 7 |
| Tools | Tooling | 29 | 4,979 | 177 | 0 | 1.0 | 2 | 0 of 31 |
<!-- /map:systems -->

## Links running uphill

<!-- map:uphill -->
| From | Uses (higher layer) | Files | Types it uses | First place |
|---|---|---|---|---|
| Networking | Gameplay/Player | 5 | KeyBindings, PlayerIdentity | `Networking/Runtime/NetworkDebugHUD.cs:46` |
| Networking | Gameplay/Persistence | 4 | ServerBackups, ServerSave, ServiceSaveStore | `Networking/Runtime/NetworkConnectionManager.cs:673` |
| Core | Gameplay/Progression | 1 | DamageResult | `Core/Runtime/ICombatFeedbackSink.cs:9` |
| Gameplay/Combat | Audio | 1 | BeatMath, CombatBeat, MusicConductor, MusicTrack | `Gameplay/Combat/Runtime/PlayerCadence.cs:36` |
| Networking | Gameplay/CharacterCreation | 1 | CharacterSession | `Networking/Runtime/NetworkConnectionManager.cs:73` |
| Networking | Gameplay/Factions | 1 | FactionId | `Networking/Runtime/Server/ServerControlPage.cs:166` |
| Networking | Gameplay/Progression | 1 | PlayerCharacterProgression | `Networking/Runtime/Server/ServerControlPage.cs:167` |
| Networking | Gameplay/Social | 1 | PlayerChat | `Networking/Runtime/Server/ServerLifetime.cs:192` |
<!-- /map:uphill -->

## Loops to cut

Each row is the cheaper side of a loop: cut these references and the two systems stop needing each other.

<!-- map:cycles -->
| Cut this link | Files | Types it uses | The other way (files) |
|---|---|---|---|
| Gameplay/Player -> Gameplay/Combat | 4 | CombatHostility, PlayerCombatState, PlayerDuel, PlayerMeleeCombat | 4 |
| World -> Gameplay/Player | 4 | ClimbableKind, ClimbableSurface, PlayerJumpMotor, PlayerWallRunMotor, PointAndClickPlayer, WallstrideSettings | 2 |
| Gameplay/Player -> Gameplay/CharacterCreation | 3 | BodyType, CharacterCreationDatabase, CharacterLook, CharacterNameRules, CharacterNetState, CharacterProfile, CharacterRecipe, CharacterRecipeValidator, Characte | longer loop |
| Gameplay/Progression -> Gameplay/Economy | 3 | CredsSink, EconomySettings, ExperienceBooster, PlayerWallet | 4 |
| Gameplay/Progression -> Gameplay/Items | 3 | IItemUseHandler, ItemDefinition, ItemUseEffect, PlayerInventory | 14 |
| Gameplay/Social -> Gameplay/Economy | 3 | CredsSink, EconomyLedger, PlayerWallet, TradeZone | 4 |
| Gameplay/Hostiles -> Gameplay/Social | 2 | PartyRules, PlayerChat, PlayerParty, PlayerSponsor, SponsorRules | longer loop |
| Gameplay/Persistence -> Gameplay/Player | 2 | PlayerIdentity | longer loop |
| Gameplay/Persistence -> Gameplay/Items | 2 | EquipSlot, EquipSlots, PlayerEquipment, PlayerInventory | 3 |
| Gameplay/Player -> Gameplay/Items | 2 | BodyFit, IGearWearer, IItemUseHandler, ItemDefinition, ItemUseEffect, PlayerInventory, WeaponClassRules | longer loop |
| Gameplay/Progression -> Gameplay/Persistence | 2 | PlayerSaveAgent | 2 |
| Gameplay/Combat -> Gameplay/Economy | 1 | TradeZone | longer loop |
| Gameplay/Hostiles -> Gameplay/Economy | 1 | TradeZone | longer loop |
| Gameplay/Items -> Gameplay/Social | 1 | PlayerChat | 7 |
| Gameplay/Items -> Gameplay/Economy | 1 | CredsSink, PlayerWallet | 16 |
| Gameplay/Items -> Gameplay/CharacterCreation | 1 | CharacterLook | 12 |
| Gameplay/Items -> Gameplay/Combat | 1 | WeaponHitbox | 1 |
| Gameplay/Persistence -> Gameplay/CharacterCreation | 1 | CharacterProfile, CharacterSession, WornPiece | longer loop |
| Gameplay/Pets -> Gameplay/Social | 1 | PlayerChat | longer loop |
| Gameplay/Pets -> Gameplay/Hostiles | 1 | HostileNet | 1 |
| Gameplay/Player -> Gameplay/Housing | 1 | PlayerHousing | 2 |
| Gameplay/Progression -> Gameplay/Player | 1 | KeyBindings | 9 |
| World -> Gameplay/Items | 1 | PlayerInventory | longer loop |
<!-- /map:cycles -->

## Classes with the widest ripple

<!-- map:ripple -->
| Type | System | Kind | Files using it | Systems | Types reached through others | Tests | Prefabs / scenes holding it |
|---|---|---|---|---|---|---|---|
| PlayerInventory | Gameplay/Items | net | 81 | 19 | 258 | 0 | 1 |
| ClientPresentationRegistry | Core | plain | 80 | 18 | 262 | 1 | 0 |
| FactionId | Gameplay/Factions | enum | 75 | 15 | 340 | 24 | 0 |
| ItemDefinition | Gameplay/Items | so | 72 | 14 | 298 | 20 | 0 |
| ItemStack | Gameplay/Items | plain | 48 | 10 | 288 | 12 | 0 |
| EquipSlot | Gameplay/Items | enum | 39 | 10 | 321 | 15 | 0 |
| CallKind | Networking | enum | 39 | 13 | 268 | 1 | 0 |
| RpcBudget | Networking | plain | 39 | 13 | 258 | 1 | 0 |
| PlayerIdentity | Gameplay/Player | net | 37 | 13 | 258 | 0 | 1 |
| PlayerResources | Gameplay/Progression | net | 35 | 13 | 258 | 0 | 41 |
| PointAndClickPlayer | Gameplay/Player | net | 31 | 9 | 258 | 0 | 41 |
| CharacterCombatStats | Gameplay/Progression | mono | 29 | 11 | 258 | 1 | 51 |
| PlayerCharacterProgression | Gameplay/Progression | net | 27 | 13 | 258 | 0 | 41 |
| WeaponClassId | Gameplay/Progression | enum | 26 | 8 | 329 | 8 | 0 |
| CombatEntity | Gameplay/Progression | net | 26 | 9 | 258 | 0 | 13 |
| StatType | Gameplay/Progression | enum | 24 | 7 | 331 | 7 | 0 |
| NpcOffer | Gameplay/NPC | enum | 24 | 9 | 265 | 1 | 0 |
| SkillDefinitionSO | Gameplay/Progression | so | 22 | 8 | 267 | 7 | 0 |
| SaveScope | Core | enum | 22 | 9 | 260 | 0 | 0 |
| PlayerSaveAgent | Gameplay/Persistence | net | 22 | 9 | 258 | 0 | 1 |
| CharacterRecipe | Gameplay/CharacterCreation | plain | 20 | 6 | 274 | 4 | 0 |
| PerformanceLog | Core | plain | 20 | 9 | 270 | 1 | 0 |
| ItemCatalog | Gameplay/Items | so | 20 | 8 | 260 | 7 | 0 |
| ISaveParticipant | Core | interface | 20 | 9 | 259 | 0 | 0 |
| KeyBindings | Gameplay/Player | plain | 20 | 4 | 258 | 2 | 0 |
<!-- /map:ripple -->

## Hubs (static members reached from other systems)

<!-- map:hubs -->
| Static member | Kind | Runtime files | Other systems |
|---|---|---|---|
| PlayerInventory.Local | locator | 63 | Audio, GameFeel, Gameplay/Autopilot, Gameplay/Combat, Gameplay/Crafting, Gameplay/Economy, Gameplay/Forge, Gameplay/Hostiles, Gameplay/Housing, Gameplay/Persistence, Gameplay/Pets, Gameplay/Progression, Gameplay/Restyle, Gameplay/Social, Tools, UI, World |
| HostileNet.Active | locator | 5 | Gameplay/Autopilot, Tools, UI |
| WorldDrop.Active | locator | 4 | GameFeel, Gameplay/Autopilot, UI |
| MusicConductor.Instance | locator | 3 | Gameplay/Combat, Tools, UI |
| PlayerInventory.LocalChanged | event | 20 | Gameplay/Persistence, UI |
| NpcInteractable.Active | locator | 6 | Gameplay/Economy, UI |
| NetworkCombatVFXPool.Instance | locator | 5 | GameFeel, Gameplay/Combat |
| PointAndClickPlayer.Active | locator | 5 | UI, World |
| ZonePortal.Active | locator | 4 | Tools, UI |
| PlayerSpawnPoint.Active | locator | 3 | Gameplay/Player, Tools |
| ZoneRules.Current | locator | 3 | Gameplay/Player, Tools |
| PlayerWallet.Local | locator | 3 | Gameplay/Progression, UI |
| PlayerDuel.Local | locator | 3 | Gameplay/Social, UI |
| PointOfInterest.Active | locator | 3 | Tools, UI |
| CombatEntity.AnyKilled | event | 2 | Gameplay/Hostiles, Gameplay/Pets |
| KeyBindings.Changed | event | 2 | Gameplay/Progression, UI |
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
| NetworkConnectionManager | Networking | 878 | Gameplay/CharacterCreation, Gameplay/Persistence | 13 |
| PlayerCombatSkills | Gameplay/Progression | 869 | Core, Gameplay/Items, Networking | 18 |
| PlayerJumpMotor | Gameplay/Player | 807 | Gameplay/Progression, Networking | 7 |
| PlayerInventory | Gameplay/Items | 780 | Core, Gameplay/Factions, Gameplay/Progression, Networking | 81 |
| CharacterCreatorController | UI | 767 | Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Items, Gameplay/Progression | 0 |
| ServerSave | Gameplay/Persistence | 735 | Core, Networking | 19 |
| PlayerVaultMotor | Gameplay/Player | 710 | Core, Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Progression | 2 |
<!-- /map:gods -->

## Design data

Each ScriptableObject class, how many assets of it exist, and where they are wired from.

<!-- map:data -->
| ScriptableObject class | System | Assets | Read by (runtime files) | Wired from prefabs / scenes / other assets |
|---|---|---|---|---|
| ItemDefinition | Gameplay/Items | 1792 | 72 | 0 / 0 / 338 |
| FurnishingDefinition | Gameplay/Housing | 223 | 8 | 0 / 0 / 1 |
| SkillDefinitionSO | Gameplay/Progression | 152 | 22 | 1 / 0 / 1 |
| HallLookDefinition | Gameplay/Housing | 50 | 6 | 0 / 0 / 1 |
| ZoneMinimapData | World | 39 | 3 | 0 / 40 / 0 |
| StatDefinitionData | Gameplay/Progression | 19 | 0 | 0 / 0 / 0 |
| NpcDefinition | Gameplay/NPC | 17 | 3 | 17 / 0 / 3 |
| MusicTrack | Audio | 13 | 7 | 0 / 0 / 1 |
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
| EconomySettings | Gameplay/Economy | 1 | 9 | 1 / 0 / 1 |
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
- **Static accessors that call GetComponent on every use:** 27.
- **FindObject calls in runtime code:** 27.
<!-- /map:smells -->

## The problems, ranked

Ranked by impact (1 to 5: how much it gets in the way of changing the game later) and, among equals, the cheaper fix
first. The page lists the file and line behind each.

<!-- map:problems -->
| # | Kind | Problem | Systems | Impact (1-5) | Files a fix touches | Effort |
|---|---|---|---|---|---|---|
| 1 | Uphill | Core uses Gameplay/Progression | Core, Gameplay/Progression | 5 | 1 | small |
| 2 | Uphill | Gameplay/Combat uses Audio | Gameplay/Combat, Audio | 5 | 1 | small |
| 3 | Uphill | Networking uses Gameplay/CharacterCreation | Networking, Gameplay/CharacterCreation | 5 | 1 | small |
| 4 | Uphill | Networking uses Gameplay/Factions | Networking, Gameplay/Factions | 5 | 1 | small |
| 5 | Uphill | Networking uses Gameplay/Progression | Networking, Gameplay/Progression | 5 | 1 | small |
| 6 | Uphill | Networking uses Gameplay/Social | Networking, Gameplay/Social | 5 | 1 | small |
| 7 | Uphill | Networking uses Gameplay/Player | Networking, Gameplay/Player | 5 | 5 | medium |
| 8 | Uphill | Networking uses Gameplay/Persistence | Networking, Gameplay/Persistence | 5 | 4 | medium |
| 9 | Hidden link | 169 asset entries store a class by name and assembly | GameFeel | 4 | 6 | small |
| 10 | Cycle | Gameplay/Progression and Gameplay/Economy need each other | Gameplay/Progression, Gameplay/Economy | 4 | 3 | small |
| 11 | Cycle | Gameplay/Progression and Gameplay/Items need each other | Gameplay/Progression, Gameplay/Items | 4 | 3 | small |
| 12 | Cycle | Gameplay/Social and Gameplay/Economy need each other | Gameplay/Social, Gameplay/Economy | 4 | 3 | small |
| 13 | Cycle | Gameplay/Persistence and Gameplay/Items need each other | Gameplay/Persistence, Gameplay/Items | 4 | 2 | small |
| 14 | Cycle | Gameplay/Progression and Gameplay/Persistence need each other | Gameplay/Progression, Gameplay/Persistence | 4 | 2 | small |
| 15 | Cycle | Gameplay/Items and Gameplay/CharacterCreation need each other | Gameplay/Items, Gameplay/CharacterCreation | 4 | 1 | small |
| 16 | Cycle | Gameplay/Items and Gameplay/Combat need each other | Gameplay/Items, Gameplay/Combat | 4 | 1 | small |
| 17 | Cycle | Gameplay/Items and Gameplay/Economy need each other | Gameplay/Items, Gameplay/Economy | 4 | 1 | small |
| 18 | Cycle | Gameplay/Items and Gameplay/Social need each other | Gameplay/Items, Gameplay/Social | 4 | 1 | small |
| 19 | Cycle | Gameplay/Pets and Gameplay/Hostiles need each other | Gameplay/Pets, Gameplay/Hostiles | 4 | 1 | small |
| 20 | Cycle | Gameplay/Player and Gameplay/Housing need each other | Gameplay/Player, Gameplay/Housing | 4 | 1 | small |
| 21 | Cycle | Gameplay/Progression and Gameplay/Player need each other | Gameplay/Progression, Gameplay/Player | 4 | 1 | small |
| 22 | God class | PlayerInventory (780 lines) | Gameplay/Items | 4 | 82 | large |
| 23 | Hub | PlayerInventory.Local (locator) | Gameplay/Items, Audio, GameFeel, Gameplay/Autopilot, Gameplay/Combat ... | 4 | 63 | large |
| 24 | Cycle | Gameplay/Player and Gameplay/Combat need each other | Gameplay/Player, Gameplay/Combat | 4 | 4 | medium |
| 25 | Cycle | World and Gameplay/Player need each other | World, Gameplay/Player | 4 | 4 | medium |
| 26 | Cycle | Gameplay/Player uses Gameplay/CharacterCreation against the order | Gameplay/Player, Gameplay/CharacterCreation | 3 | 3 | small |
| 27 | Hub | MusicConductor.Instance (locator) | Audio, Gameplay/Combat, Tools, UI | 3 | 3 | small |
| 28 | Hub | PlayerDuel.Local (locator) | Gameplay/Combat, Gameplay/Social, UI | 3 | 3 | small |
| 29 | Hub | PlayerSpawnPoint.Active (locator) | Networking, Gameplay/Player, Tools | 3 | 3 | small |
| 30 | Hub | PlayerWallet.Local (locator) | Gameplay/Economy, Gameplay/Progression, UI | 3 | 3 | small |
| 31 | Hub | PointOfInterest.Active (locator) | World, Tools, UI | 3 | 3 | small |
| 32 | Hub | ZoneRules.Current (locator) | World, Gameplay/Player, Tools | 3 | 3 | small |
| 33 | Hub | CombatEntity.AnyKilled (event) | Gameplay/Progression, Gameplay/Hostiles, Gameplay/Pets | 3 | 2 | small |
| 34 | Cycle | Gameplay/Hostiles uses Gameplay/Social against the order | Gameplay/Hostiles, Gameplay/Social | 3 | 2 | small |
| 35 | Cycle | Gameplay/Persistence uses Gameplay/Player against the order | Gameplay/Persistence, Gameplay/Player | 3 | 2 | small |
| 36 | Cycle | Gameplay/Player uses Gameplay/Items against the order | Gameplay/Player, Gameplay/Items | 3 | 2 | small |
| 37 | Hub | JoinFlow.Failed (event) | Networking, Tools, UI | 3 | 2 | small |
| 38 | Hub | KeyBindings.Changed (event) | Gameplay/Player, Gameplay/Progression, UI | 3 | 2 | small |
| 39 | Cycle | Gameplay/Combat uses Gameplay/Economy against the order | Gameplay/Combat, Gameplay/Economy | 3 | 1 | small |
| 40 | Cycle | Gameplay/Hostiles uses Gameplay/Economy against the order | Gameplay/Hostiles, Gameplay/Economy | 3 | 1 | small |
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
