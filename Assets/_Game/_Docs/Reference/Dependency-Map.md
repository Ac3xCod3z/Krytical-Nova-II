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
Generated 29 Oct 2026, 20:25: 722 runtime scripts (135,454 lines) declaring 1019 types (185 MonoBehaviours, 65 NetworkBehaviours, 101 ScriptableObject classes); 133 test scripts; 4722 prefabs, 46 scenes and 2748 assets read. 206 system-to-system links, of which 0 run uphill, 0 pairs of systems use each other both ways, and 0 links would have to go for the systems to form a clean stack. The largest tangle, classes that all reach each other through some chain of uses, holds 10 classes from 1 systems (UI 10, ...).
<!-- /map:summary -->

**Checked against the compiler.**

<!-- map:compiler -->
Checked against the 211 references the assemblies declare between systems: every link the map found is one the compiler allows. 5 declared references name no type in code: GameFeel -> Gameplay/CharacterCreation, Gameplay/Factions -> Core, Gameplay/Persistence -> Gameplay/Factions, Gameplay/Wallet -> Core, Tools -> Gameplay/NPC. Each is either unused and can go, or needed by the compiler for a type reached through another assembly's member (error CS0012 when removed), which reading the code as text cannot see.
<!-- /map:compiler -->

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
  layer so that as few links as possible point backwards. Since 19 Oct 2026 every system is its own assembly, so the
  compiler refuses a loop outright; the map finds none.
- **Ripple.** For a class: the files that use it directly, and the types that reach it through others. A large ripple
  means a change to it travels far.
- **Instability.** Uses ÷ (uses + used by). Near 0, many lean on it and it should change rarely. Near 1, it leans on
  others and is free to change.
- **What it cannot see.** It reads the code as text, not through the compiler. It misses:
  - reflection and string lookups (counted separately below);
  - UnityEvents wired in the Inspector;
  - Editor code, which is left out on purpose;
  - links that only go through an interface;
  - a type reached through another assembly's member without its name being written (the compiler still needs that
    assembly referenced: error CS0012).

  A name two namespaces both declare is skipped rather than guessed. Every link it shows names its file and line. To
  catch what it misreads, it checks itself against the references the assemblies declare (above).

## Systems

<!-- map:systems -->
| System | Layer | Scripts | Lines | Uses (types) | Used by (types) | Instability | Tests | Types touched by a test |
|---|---|---|---|---|---|---|---|---|
| Core | Foundation | 54 | 4,405 | 0 | 237 | 0.0 | 7 | 24 of 43 |
| Networking | Infrastructure | 47 | 8,132 | 20 | 92 | 0.18 | 9 | 31 of 60 |
| Rendering | Infrastructure | 17 | 3,589 | 3 | 28 | 0.1 | 1 | 2 of 21 |
| Environment | Simulation | 6 | 834 | 0 | 2 | 0.0 | 0 | 0 of 6 |
| Gameplay/Autopilot | Simulation | 11 | 1,423 | 41 | 13 | 0.76 | 3 | 6 of 12 |
| Gameplay/Crafting | Simulation | 3 | 401 | 19 | 9 | 0.68 | 1 | 4 of 6 |
| Gameplay/Forge | Simulation | 1 | 412 | 39 | 4 | 0.91 | 0 | 0 of 2 |
| Gameplay/Housing | Simulation | 20 | 3,611 | 30 | 37 | 0.45 | 3 | 15 of 28 |
| Gameplay/Mounts | Simulation | 10 | 1,382 | 22 | 9 | 0.71 | 1 | 4 of 15 |
| Gameplay/Restyle | Simulation | 3 | 869 | 40 | 5 | 0.89 | 0 | 0 of 3 |
| Gameplay/Economy | Simulation | 23 | 4,098 | 57 | 46 | 0.55 | 9 | 14 of 37 |
| Gameplay/Hostiles | Simulation | 23 | 2,870 | 41 | 29 | 0.59 | 4 | 10 of 23 |
| Gameplay/Pets | Simulation | 12 | 1,445 | 33 | 32 | 0.51 | 4 | 13 of 19 |
| Gameplay/Social | Simulation | 39 | 5,805 | 52 | 49 | 0.51 | 9 | 23 of 52 |
| Gameplay/Combat | Simulation | 13 | 3,362 | 44 | 27 | 0.62 | 4 | 5 of 14 |
| Gameplay/Persistence | Simulation | 10 | 3,161 | 63 | 14 | 0.82 | 14 | 7 of 14 |
| Gameplay/Player | Simulation | 25 | 10,686 | 64 | 48 | 0.57 | 4 | 11 of 28 |
| Gameplay/CharacterCreation | Simulation | 26 | 3,173 | 27 | 70 | 0.28 | 10 | 25 of 29 |
| Gameplay/Items | Simulation | 66 | 11,859 | 48 | 182 | 0.21 | 13 | 41 of 88 |
| World | Simulation | 37 | 6,379 | 16 | 41 | 0.28 | 2 | 9 of 43 |
| Gameplay/Progression | Simulation | 46 | 8,125 | 25 | 207 | 0.11 | 6 | 27 of 58 |
| Gameplay/Wallet | Simulation | 3 | 132 | 0 | 18 | 0.0 | 0 | 2 of 2 |
| Gameplay/NPC | Simulation | 3 | 290 | 5 | 17 | 0.23 | 1 | 1 of 4 |
| Gameplay/Factions | Simulation | 6 | 169 | 0 | 25 | 0.0 | 0 | 1 of 3 |
| GameFeel | Presentation | 30 | 5,526 | 54 | 2 | 0.96 | 4 | 17 of 38 |
| UI | Presentation | 143 | 36,815 | 325 | 5 | 0.98 | 17 | 32 of 148 |
| Audio | Presentation | 14 | 1,223 | 6 | 16 | 0.27 | 1 | 1 of 15 |
| Tools | Tooling | 31 | 5,278 | 190 | 0 | 1.0 | 3 | 0 of 33 |
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
<!-- /map:cycles -->

## Classes with the widest ripple

<!-- map:ripple -->
| Type | System | Kind | Files using it | Systems | Types reached through others | Tests | Prefabs / scenes holding it |
|---|---|---|---|---|---|---|---|
| ClientPresentationRegistry | Core | plain | 83 | 19 | 247 | 1 | 0 |
| FactionId | Gameplay/Factions | enum | 80 | 14 | 317 | 27 | 0 |
| PlayerInventory | Gameplay/Items | net | 74 | 15 | 202 | 0 | 1 |
| ItemDefinition | Gameplay/Items | so | 68 | 13 | 267 | 21 | 0 |
| ItemStack | Gameplay/Items | plain | 51 | 12 | 244 | 12 | 0 |
| CallKind | Networking | enum | 42 | 15 | 274 | 2 | 0 |
| RpcBudget | Networking | plain | 42 | 15 | 259 | 2 | 0 |
| EquipSlot | Gameplay/Items | enum | 39 | 10 | 291 | 15 | 0 |
| PlayerIdentity | Gameplay/Player | net | 39 | 12 | 191 | 1 | 1 |
| PlayerResources | Gameplay/Progression | net | 36 | 14 | 218 | 0 | 41 |
| LocalPlayer | Core | plain | 36 | 16 | 211 | 1 | 0 |
| PointAndClickPlayer | Gameplay/Player | net | 32 | 8 | 165 | 0 | 41 |
| CharacterCombatStats | Gameplay/Progression | mono | 30 | 13 | 237 | 1 | 51 |
| PlayerCharacterProgression | Gameplay/Progression | net | 28 | 12 | 238 | 0 | 41 |
| StatType | Gameplay/Progression | enum | 27 | 8 | 328 | 7 | 0 |
| CombatEntity | Gameplay/Progression | net | 27 | 11 | 195 | 0 | 13 |
| WeaponClassId | Gameplay/Progression | enum | 26 | 8 | 307 | 8 | 0 |
| CharacterRecipe | Gameplay/CharacterCreation | plain | 24 | 7 | 220 | 7 | 0 |
| NpcOffer | Gameplay/NPC | enum | 24 | 9 | 210 | 1 | 0 |
| HudTyping | UI | plain | 24 | 1 | 32 | 0 | 0 |
| SaveScope | Core | enum | 23 | 10 | 242 | 0 | 0 |
| SkillDefinitionSO | Gameplay/Progression | so | 22 | 8 | 212 | 7 | 0 |
| KeyBindings | Core | plain | 22 | 4 | 175 | 2 | 0 |
| ISaveParticipant | Core | interface | 21 | 10 | 241 | 0 | 0 |
| ItemCatalog | Gameplay/Items | so | 21 | 8 | 216 | 7 | 0 |
<!-- /map:ripple -->

## Hubs (static members reached from other systems)

<!-- map:hubs -->
| Static member | Kind | Runtime files | Other systems |
|---|---|---|---|
| PlayerInventory.Local | locator | 38 | GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles, Gameplay/Persistence, Tools, UI |
| HostileNet.Active | locator | 5 | Gameplay/Autopilot, Tools, UI |
| WorldDrop.Active | locator | 4 | GameFeel, Gameplay/Autopilot, UI |
| PlayerInventory.LocalChanged | event | 21 | Gameplay/Persistence, UI |
| NpcInteractable.Active | locator | 6 | Gameplay/Economy, UI |
| ServerHooks.Saves | state | 6 | Gameplay/Persistence, World |
| NetworkConnectionManager.Instance | locator | 6 | UI, World |
| NetworkCombatVFXPool.Instance | locator | 5 | GameFeel, Gameplay/Combat |
| PlayerPet.Local | locator | 5 | GameFeel, UI |
| ZoneRules.Current | locator | 4 | Gameplay/Player, Tools |
| PlayerDuel.Local | locator | 4 | Gameplay/Social, UI |
| ZonePortal.Active | locator | 4 | Tools, UI |
| PlayerSpawnPoint.Active | locator | 3 | Gameplay/Housing, Tools |
| PointOfInterest.Active | locator | 3 | Tools, UI |
| PlayerCadence.LocalPress | event | 2 | GameFeel, UI |
| PlayerCadence.LocalCombo | event | 2 | GameFeel, UI |
| CombatEntity.AnyKilled | event | 2 | Gameplay/Hostiles, Gameplay/Pets |
| ServerChat.Announce | state | 2 | Gameplay/Items, Gameplay/Social |
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
| ProgressionHUD | UI | 2212 | Audio, Core, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 5 |
| PointAndClickPlayer | Gameplay/Player | 2146 | Core, Gameplay/Progression, Networking, Rendering | 32 |
| PlayerWallRunMotor | Gameplay/Player | 1760 | Core, Gameplay/Progression, Networking, World | 8 |
| ArrangeController | UI | 1663 | Core, Gameplay/Housing, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 0 |
| GearUIController | UI | 1628 | Core, Gameplay/CharacterCreation, Gameplay/Economy, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression, Gameplay/Restyle, Gameplay/Wallet | 21 |
| PlayerAnimancerMotor | Gameplay/Player | 1513 | Core, Gameplay/Progression, Networking | 15 |
| GameFeelPresenter | GameFeel | 1239 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression, Networking, UI | 1 |
| PetFollower | Gameplay/Items | 1056 | Rendering | 5 |
| ServerSave | Gameplay/Persistence | 991 | Core, Networking | 17 |
| PlayerMeleeCombat | Gameplay/Combat | 983 | Core, Gameplay/Player, Gameplay/Progression, Networking | 9 |
| PlayerCombatSkills | Gameplay/Progression | 965 | Core, Networking | 18 |
| HudLayoutEditor | UI | 964 | - | 12 |
| GearWorkWindowController | UI | 947 | Core, Gameplay/Economy, Gameplay/Forge, Gameplay/Items, Gameplay/NPC, Gameplay/Progression | 0 |
| NetworkConnectionManager | Networking | 937 | Core | 15 |
| CharacterCreatorController | UI | 935 | Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Items, Gameplay/Progression | 0 |
| GmCommands | Tools | 910 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Player, Gameplay/Progression, Gameplay/Social, Networking, World | 0 |
| PlayerJumpMotor | Gameplay/Player | 844 | Gameplay/Progression, Networking | 7 |
| CharacterSelectController | UI | 840 | Gameplay/CharacterCreation, Networking | 0 |
| PlayerInventory | Gameplay/Items | 798 | Core, Gameplay/Factions, Gameplay/Progression, Networking | 74 |
| PlayerVaultMotor | Gameplay/Player | 731 | Core, Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Progression, Networking | 3 |
<!-- /map:gods -->

## Design data

Each ScriptableObject class, how many assets of it exist, and where they are wired from.

<!-- map:data -->
| ScriptableObject class | System | Assets | Read by (runtime files) | Wired from prefabs / scenes / other assets |
|---|---|---|---|---|
| ItemDefinition | Gameplay/Items | 1813 | 68 | 0 / 0 / 338 |
| FurnishingDefinition | Gameplay/Housing | 223 | 8 | 0 / 0 / 1 |
| SkillDefinitionSO | Gameplay/Progression | 152 | 22 | 1 / 0 / 1 |
| HallLookDefinition | Gameplay/Housing | 50 | 6 | 0 / 0 / 1 |
| ZoneMinimapData | World | 39 | 3 | 0 / 40 / 0 |
| MusicTrack | Audio | 33 | 14 | 0 / 0 / 5 |
| StatDefinitionData | Gameplay/Progression | 19 | 0 | 0 / 0 / 0 |
| NpcDefinition | Gameplay/NPC | 17 | 3 | 17 / 0 / 3 |
| WeatherProfile | World | 11 | 4 | 0 / 40 / 12 |
| HostileDefinition | Gameplay/Hostiles | 10 | 8 | 10 / 0 / 8 |
| GearPaintLook | Gameplay/Items | 10 | 3 | 0 / 0 / 0 |
| HostileZoneDefinition | Gameplay/Hostiles | 8 | 2 | 0 / 2 / 0 |
| GrassType | Environment | 6 | 2 | 0 / 0 / 2 |
| LineageDefinition | Gameplay/CharacterCreation | 6 | 10 | 0 / 0 / 1 |
| MountDefinition | Gameplay/Mounts | 4 | 5 | 0 / 0 / 1 |
| PetChatterSet | Gameplay/Pets | 4 | 2 | 0 / 0 / 4 |
| PetDefinition | Gameplay/Pets | 4 | 12 | 0 / 0 / 1 |
| VaultAnimationSet | Gameplay/Player | 4 | 1 | 0 / 0 / 1 |
| VendorStock | Gameplay/Economy | 3 | 4 | 0 / 0 / 1 |
| FactionDefinition | Gameplay/Factions | 3 | 6 | 0 / 0 / 1 |
| FactionHudKit | UI | 3 | 2 | 0 / 40 / 0 |
| UiSkin | UI | 3 | 1 | 1 / 0 / 0 |
| DeckPack | Audio | 2 | 2 | 0 / 0 / 1 |
| ZoneAmbience | Audio | 2 | 1 | 0 / 2 / 0 |
| ZoneMusic | Audio | 2 | 3 | 0 / 2 / 0 |
| GrassBiome | Environment | 2 | 2 | 0 / 0 / 1 |
| CharacterClassData | Gameplay/Progression | 2 | 2 | 1 / 2 / 0 |
| MovementSkillData | Gameplay/Progression | 2 | 3 | 1 / 0 / 0 |
| DeckLibrary | Audio | 1 | 2 | 0 / 40 / 0 |
| MenuMusic | Audio | 1 | 1 | 0 / 4 / 0 |
| GrassRuleSet | Environment | 1 | 1 | 0 / 40 / 0 |
| BeatGlowSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| BeatRingSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| HitLookSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| SmearSettings | GameFeel | 1 | 2 | 1 / 0 / 0 |
| SpiritSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| AutopilotSettings | Gameplay/Autopilot | 1 | 5 | 1 / 0 / 1 |
| CharacterCreationDatabase | Gameplay/CharacterCreation | 1 | 19 | 2 / 4 / 0 |
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
| RecallSettings | Gameplay/Housing | 1 | 1 | 1 / 0 / 0 |
| FlaskSettings | Gameplay/Items | 1 | 1 | 1 / 0 / 0 |
| GearProgressionSettings | Gameplay/Items | 1 | 11 | 1 / 2 / 1 |
| GearThemeLibrary | Gameplay/Items | 1 | 12 | 0 / 0 / 403 |
| ItemGlowIcons | Gameplay/Items | 1 | 1 | 0 / 0 / 1 |
| GearDictionary | Gameplay/Items | 1 | 7 | 1 / 2 / 0 |
| GearStatTable | Gameplay/Items | 1 | 2 | 0 / 0 / 1 |
| ItemCatalog | Gameplay/Items | 1 | 21 | 2 / 3 / 1 |
| WeaponClassCatalog | Gameplay/Items | 1 | 2 | 1 / 1 / 0 |
| SidekickPartCatalog | Gameplay/Items | 1 | 7 | 0 / 0 / 747 |
| MountCatalog | Gameplay/Mounts | 1 | 1 | 1 / 0 / 0 |
| MountSettings | Gameplay/Mounts | 1 | 4 | 0 / 0 / 1 |
| PetCatalog | Gameplay/Pets | 1 | 3 | 1 / 0 / 0 |
| PetChatterSettings | Gameplay/Pets | 1 | 2 | 1 / 0 / 0 |
| PetEmojiLibrary | Gameplay/Pets | 1 | 0 | 0 / 0 / 0 |
| PetSettings | Gameplay/Pets | 1 | 2 | 0 / 0 / 1 |
| TraversalSettings | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| VaultAnimationMap | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| WallstrideSettings | Gameplay/Player | 1 | 4 | 1 / 0 / 0 |
| WaterstrideSettings | Gameplay/Player | 1 | 2 | 1 / 0 / 0 |
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
| WaterMap | World | 1 | 3 | 0 / 0 / 1 |
| WeatherCatalog | World | 1 | 1 | 0 / 40 / 0 |
| WorldScaleSettings | World | 1 | 0 | 0 / 0 / 0 |
| ZoneCatalog | World | 1 | 4 | 0 / 39 / 0 |
| ZoneTerrainProfile | World | 1 | 0 | 0 / 0 / 0 |
| GearClothLook | Gameplay/Items | 0 | 2 | 0 / 0 / 0 |
| ColourBlindFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
| ComicLetteringFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
| ComicStyleFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
<!-- /map:data -->

Where tuning still lives in code, and other links the compiler cannot see:

<!-- map:smells -->
- **Gameplay numbers fixed in code:** 347 in 108 files. Most: `Gameplay/Crafting/Data/CraftingSettings.cs` (28), `Gameplay/Player/Runtime/PlayerWallRunMotor.cs` (17), `Gameplay/Player/Runtime/PlayerVaultMotor.cs` (15), `Gameplay/Player/Runtime/PointAndClickPlayer.cs` (15), `Gameplay/Items/Data/GearProgressionSettings.cs` (13), `Gameplay/Items/Runtime/WeaponPaintRules.cs` (11), `Gameplay/Player/Runtime/PlayerJumpMotor.cs` (10), `Gameplay/Player/Runtime/PlayerWallRunMotor.WallWalk.cs` (10).
- **Settings fallbacks** (`settings != null ? settings.x : 8`): 96.
- **Runtime writes to shared assets** (ScriptableObject or pipeline fields): 25 — `Rendering/Runtime/PipelineRebuilds.cs:60`, `Rendering/Runtime/PipelineRebuilds.cs:68`, `Rendering/Runtime/PipelineRebuilds.cs:104`, `Rendering/Runtime/PipelineRebuilds.cs:105`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:142`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:143`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:144`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:145`.
- **Lookups by name** (Type.GetType, Find("..."), Resources.Load, SendMessage): 20.
- **Asset entries that store a class by name and assembly** ([SerializeReference]): 174 in 2 assets.
- **Static accessors that call GetComponent on every use:** 0.
- **FindObject calls in runtime code:** 33.
<!-- /map:smells -->

## The problems, ranked

Ranked by impact (1 to 5: how much it gets in the way of changing the game later) and, among equals, the cheaper fix
first. The page lists the file and line behind each.

<!-- map:problems -->
| # | Kind | Problem | Systems | Impact (1-5) | Files a fix touches | Effort |
|---|---|---|---|---|---|---|
| 1 | Hidden link | 174 asset entries store a class by name and assembly | GameFeel | 4 | 6 | small |
| 2 | God class | PlayerInventory (798 lines) | Gameplay/Items | 4 | 75 | large |
| 3 | Hub | PlayerInventory.Local (locator) | Gameplay/Items, GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles ... | 4 | 38 | large |
| 4 | Hub | PlayerSpawnPoint.Active (locator) | Networking, Gameplay/Housing, Tools | 3 | 3 | small |
| 5 | Hub | PointOfInterest.Active (locator) | World, Tools, UI | 3 | 3 | small |
| 6 | Hub | CombatEntity.AnyKilled (event) | Gameplay/Progression, Gameplay/Hostiles, Gameplay/Pets | 3 | 2 | small |
| 7 | Hub | JoinFlow.Failed (event) | Networking, Tools, UI | 3 | 2 | small |
| 8 | Hub | KeyBindings.Changed (event) | Core, Gameplay/Progression, UI | 3 | 2 | small |
| 9 | Hub | MusicConductor.Instance (locator) | Audio, Tools, UI | 3 | 2 | small |
| 10 | Hub | PlayerCadence.LocalCombo (event) | Gameplay/Combat, GameFeel, UI | 3 | 2 | small |
| 11 | Hub | PlayerCadence.LocalPress (event) | Gameplay/Combat, GameFeel, UI | 3 | 2 | small |
| 12 | Hub | ServerChat.Announce (state) | Core, Gameplay/Items, Gameplay/Social | 3 | 2 | small |
| 13 | Design data | 347 gameplay numbers fixed in code | Gameplay/Autopilot, Gameplay/CharacterCreation, Gameplay/Combat, Gameplay/Crafting, Gameplay/Economy ... | 3 | 108 | large |
| 14 | Hub | WorldEnvironmentNetwork.Instance (locator) | World, Tools | 3 | 10 | medium |
| 15 | God class | PointAndClickPlayer (2146 lines) | Gameplay/Player | 3 | 33 | large |
| 16 | Hub | NetworkConnectionManager.Instance (locator) | Networking, UI, World | 3 | 6 | medium |
| 17 | Hub | NpcInteractable.Active (locator) | Gameplay/NPC, Gameplay/Economy, UI | 3 | 6 | medium |
| 18 | Hub | ServerHooks.Saves (state) | Networking, Gameplay/Persistence, World | 3 | 6 | medium |
| 19 | Hub | HostileNet.Active (locator) | Gameplay/Hostiles, Gameplay/Autopilot, Tools, UI | 3 | 5 | medium |
| 20 | Hub | NetworkCombatVFXPool.Instance (locator) | Networking, GameFeel, Gameplay/Combat | 3 | 5 | medium |
| 21 | Hub | PlayerPet.Local (locator) | Gameplay/Pets, GameFeel, UI | 3 | 5 | medium |
| 22 | Hub | PlayerDuel.Local (locator) | Gameplay/Combat, Gameplay/Social, UI | 3 | 4 | medium |
| 23 | Hub | WorldDrop.Active (locator) | Gameplay/Items, GameFeel, Gameplay/Autopilot, UI | 3 | 4 | medium |
| 24 | Hub | ZonePortal.Active (locator) | World, Tools, UI | 3 | 4 | medium |
| 25 | Hub | ZoneRules.Current (locator) | World, Gameplay/Player, Tools | 3 | 4 | medium |
| 26 | Untested | CombatEntity has no test | Gameplay/Progression | 3 | 1 | medium |
| 27 | Untested | PlayerCharacterProgression has no test | Gameplay/Progression | 3 | 1 | medium |
| 28 | Untested | PlayerInventory has no test | Gameplay/Items | 3 | 1 | medium |
| 29 | Untested | PlayerResources has no test | Gameplay/Progression | 3 | 1 | medium |
| 30 | Untested | PointAndClickPlayer has no test | Gameplay/Player | 3 | 1 | medium |
| 31 | God class | GearUIController (1628 lines) | UI | 3 | 22 | large |
| 32 | Hub | PlayerInventory.LocalChanged (event) | Gameplay/Items, Gameplay/Persistence, UI | 3 | 21 | large |
| 33 | Hub | GearUIController.Instance (locator) | UI | 3 | 20 | large |
| 34 | Duplicated state | 19 classes keep their own static player / object lists | Environment, Gameplay/Economy, Gameplay/Items, Gameplay/NPC, Gameplay/Player ... | 3 | 19 | large |
| 35 | God class | PlayerAnimancerMotor (1513 lines) | Gameplay/Player | 3 | 16 | large |
| 36 | God class | PlayerMeleeCombat (983 lines) | Gameplay/Combat | 3 | 10 | large |
| 37 | God class | PlayerWallRunMotor (1760 lines) | Gameplay/Player | 3 | 10 | large |
| 38 | God class | ProgressionHUD (2212 lines) | UI | 3 | 6 | large |
| 39 | God class | PlayerVaultMotor (731 lines) | Gameplay/Player | 3 | 4 | large |
| 40 | God class | ArrangeController (1663 lines) | UI | 3 | 3 | large |
<!-- /map:problems -->

## After the plan (19 Oct 2026, stages 0 to 7)

| | Before | After |
|---|---|---|
| Links running uphill | 8 | 0 |
| Pairs of systems that use each other | 21 | 0 |
| Links to cut for a clean stack | 31 | 0 |
| The largest tangle | 57 classes across 10 systems | 7 classes inside one system (Player's motors and `PointAndClickPlayer`, which call each other; harmless across systems) |
| Assemblies the runtime compiles into | 1 (`KryticalNova.Runtime`) | 23 (Core + one per system) |
| Classes no test touches but 25+ files use | 8 | 6 (`ClientPresentationRegistry` and `RpcBudget` now tested) |
| EditMode tests | 1014 | 1023 |

Every system now uses only the systems below it, and the compiler refuses a reference that would close a loop. How
each loop was cut, stage by stage, is `Plans/architecture-decoupling-plan.md`, with the Play check of every feature
the stages touched. Of the design-data findings below, the click-travel and vault numbers moved into their settings
assets and the pipeline asset is put back after Play; the fallback numbers and the rewriting builders were kept on
purpose (the plan says why). The findings below are kept as they were on the morning of 19 Oct 2026; the tables above
them are regenerated on every run.

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
