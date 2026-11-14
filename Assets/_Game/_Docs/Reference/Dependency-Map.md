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
Generated 13 Nov 2026, 21:46: 1041 runtime scripts (192,828 lines) declaring 1958 types (216 MonoBehaviours, 65 NetworkBehaviours, 118 ScriptableObject classes); 340 test scripts; 4753 prefabs, 48 scenes and 2790 assets read. 231 system-to-system links, of which 0 run uphill, 0 pairs of systems use each other both ways, and 4 links would have to go for the systems to form a clean stack. The largest tangle, classes that all reach each other through some chain of uses, holds 10 classes from 1 systems (UI 10, ...).
<!-- /map:summary -->

**Checked against the compiler.**

<!-- map:compiler -->
Checked against the 236 references the assemblies declare between systems: every link the map found is one the compiler allows. 5 declared references name no type in code: GameFeel -> Gameplay/CharacterCreation, Gameplay/Crossfire -> Networking, Gameplay/Factions -> Core, Gameplay/Wallet -> Core, Tools -> Gameplay/NPC. Each is either unused and can go, or needed by the compiler for a type reached through another assembly's member (error CS0012 when removed), which reading the code as text cannot see.
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
| Core | Foundation | 78 | 6,754 | 0 | 449 | 0.0 | 10 | 62 of 74 |
| Networking | Infrastructure | 100 | 15,069 | 44 | 144 | 0.23 | 39 | 114 of 174 |
| Rendering | Infrastructure | 18 | 3,673 | 3 | 32 | 0.09 | 1 | 2 of 22 |
| Environment | Simulation | 6 | 834 | 0 | 2 | 0.0 | 0 | 0 of 6 |
| Gameplay/Crafting | Simulation | 3 | 426 | 25 | 9 | 0.74 | 1 | 4 of 6 |
| Gameplay/Crossfire | Simulation | 9 | 911 | 9 | 4 | 0.69 | 5 | 15 of 17 |
| Gameplay/Forge | Simulation | 1 | 461 | 46 | 4 | 0.92 | 0 | 0 of 2 |
| Gameplay/Housing | Simulation | 20 | 3,662 | 37 | 37 | 0.5 | 3 | 15 of 28 |
| Gameplay/Mounts | Simulation | 15 | 3,196 | 37 | 9 | 0.8 | 4 | 13 of 21 |
| Gameplay/Restyle | Simulation | 3 | 994 | 55 | 5 | 0.92 | 0 | 0 of 3 |
| Gameplay/Townsfolk | Simulation | 12 | 2,511 | 43 | 2 | 0.96 | 3 | 24 of 36 |
| Gameplay/Economy | Simulation | 23 | 4,637 | 84 | 46 | 0.65 | 10 | 24 of 37 |
| Gameplay/Autopilot | Simulation | 11 | 1,562 | 46 | 14 | 0.77 | 4 | 7 of 12 |
| Gameplay/Social | Simulation | 42 | 6,871 | 83 | 49 | 0.63 | 14 | 34 of 55 |
| Gameplay/Persistence | Simulation | 66 | 19,507 | 414 | 14 | 0.97 | 50 | 57 of 249 |
| Gameplay/Hostiles | Simulation | 52 | 6,009 | 80 | 35 | 0.7 | 23 | 58 of 76 |
| Gameplay/Pets | Simulation | 12 | 1,550 | 40 | 32 | 0.56 | 6 | 14 of 19 |
| Gameplay/Combat | Simulation | 34 | 5,900 | 92 | 42 | 0.69 | 15 | 34 of 59 |
| Gameplay/Player | Simulation | 83 | 22,559 | 78 | 230 | 0.25 | 48 | 161 of 209 |
| Gameplay/CharacterCreation | Simulation | 28 | 3,268 | 27 | 96 | 0.22 | 11 | 26 of 30 |
| Gameplay/Items | Simulation | 85 | 13,069 | 65 | 303 | 0.18 | 16 | 74 of 113 |
| World | Simulation | 40 | 7,240 | 21 | 49 | 0.3 | 4 | 14 of 49 |
| Gameplay/Progression | Simulation | 49 | 9,168 | 36 | 237 | 0.13 | 15 | 54 of 63 |
| Gameplay/Wallet | Simulation | 4 | 146 | 0 | 21 | 0.0 | 0 | 2 of 2 |
| Gameplay/NPC | Simulation | 3 | 293 | 5 | 18 | 0.22 | 1 | 3 of 4 |
| Gameplay/Factions | Simulation | 7 | 197 | 0 | 29 | 0.0 | 0 | 2 of 4 |
| Gameplay/Brains | Simulation | 10 | 1,512 | 1 | 49 | 0.02 | 4 | 34 of 39 |
| GameFeel | Presentation | 31 | 5,936 | 57 | 2 | 0.97 | 6 | 19 of 39 |
| UI | Presentation | 148 | 37,933 | 334 | 5 | 0.99 | 31 | 52 of 155 |
| Audio | Presentation | 15 | 1,299 | 10 | 16 | 0.38 | 2 | 5 of 15 |
| Tools | Tooling | 33 | 5,681 | 212 | 0 | 1.0 | 11 | 2 of 36 |
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
| Gameplay/Autopilot -> Gameplay/Economy | 2 | KioskRules | longer loop |
| Gameplay/Hostiles -> Gameplay/Social | 2 | PartyRules, PlayerChat, PlayerParty, PlayerSponsor, SponsorRules | longer loop |
| Gameplay/Persistence -> Gameplay/Autopilot | 1 | PlayerAutopilot | longer loop |
| Gameplay/Pets -> Gameplay/Social | 1 | PlayerChat | longer loop |
<!-- /map:cycles -->

## Classes with the widest ripple

<!-- map:ripple -->
| Type | System | Kind | Files using it | Systems | Types reached through others | Tests | Prefabs / scenes holding it |
|---|---|---|---|---|---|---|---|
| ClientPresentationRegistry | Core | plain | 91 | 20 | 376 | 3 | 0 |
| BodyRoleTag | Core | mono | 90 | 19 | 455 | 16 | 0 |
| FactionId | Gameplay/Factions | enum | 85 | 16 | 433 | 31 | 0 |
| PlayerInventory | Gameplay/Items | net | 76 | 15 | 289 | 2 | 1 |
| ItemDefinition | Gameplay/Items | so | 70 | 13 | 362 | 27 | 0 |
| CallKind | Networking | enum | 66 | 15 | 408 | 2 | 0 |
| NetRoleKind | Core | enum | 63 | 18 | 460 | 17 | 0 |
| ItemStack | Gameplay/Items | plain | 61 | 12 | 366 | 14 | 0 |
| GhostBodyRegistry | Gameplay/Player | plain | 57 | 4 | 123 | 15 | 0 |
| GhostBodyEntry | Gameplay/Player | plain | 54 | 4 | 125 | 5 | 0 |
| PlayerIdentity | Gameplay/Player | net | 49 | 12 | 275 | 3 | 1 |
| RpcBudget | Networking | plain | 48 | 15 | 387 | 4 | 0 |
| LocalPlayer | Core | plain | 45 | 18 | 367 | 8 | 0 |
| EquipSlot | Gameplay/Items | enum | 42 | 10 | 391 | 18 | 0 |
| PlayerResources | Gameplay/Progression | net | 41 | 14 | 330 | 3 | 41 |
| PointAndClickPlayer | Gameplay/Player | net | 40 | 9 | 239 | 5 | 41 |
| CharacterRecipe | Gameplay/CharacterCreation | plain | 39 | 8 | 320 | 13 | 0 |
| PlayerCharacterProgression | Gameplay/Progression | net | 36 | 12 | 362 | 5 | 41 |
| CharacterCombatStats | Gameplay/Progression | mono | 36 | 14 | 361 | 8 | 61 |
| CombatEntity | Gameplay/Progression | net | 36 | 12 | 288 | 3 | 23 |
| PlayerKey | Core | plain | 34 | 6 | 515 | 28 | 0 |
| IBodyRequests | Core | interface | 31 | 14 | 456 | 1 | 0 |
| WeaponClassId | Gameplay/Progression | enum | 29 | 10 | 470 | 11 | 0 |
| StatType | Gameplay/Progression | enum | 27 | 8 | 462 | 12 | 0 |
| PlayerPose | Gameplay/Player | plain | 27 | 6 | 36 | 13 | 0 |
<!-- /map:ripple -->

## Hubs (static members reached from other systems)

<!-- map:hubs -->
| Static member | Kind | Runtime files | Other systems |
|---|---|---|---|
| PlayerInventory.Local | locator | 38 | GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles, Gameplay/Persistence, Tools, UI |
| WorldDrop.Active | locator | 6 | GameFeel, Gameplay/Autopilot, Gameplay/Persistence, UI |
| HostileNet.Active | locator | 6 | Gameplay/Autopilot, Tools, UI |
| PlayerCadence.LocalPress | event | 4 | GameFeel, Gameplay/Persistence, UI |
| KnWorldHooks.ServerWorldMade | state | 4 | Gameplay/Hostiles, Gameplay/Persistence, Gameplay/Townsfolk |
| PlayerGm.Remote | state | 4 | Gameplay/Persistence, Gameplay/Social, Tools |
| CombatEntity.AnyKilled | event | 3 | Gameplay/Combat, Gameplay/Hostiles, Gameplay/Pets |
| WorkshopOwnerLink.Current | locator | 3 | Gameplay/Crafting, Gameplay/Forge, Gameplay/Persistence |
| ServerChat.Announce | state | 3 | Gameplay/Crossfire, Gameplay/Items, Gameplay/Social |
| NetSession.EndReason | state | 3 | Gameplay/Persistence, Networking, UI |
| PlayerInventory.LocalChanged | event | 21 | Gameplay/Persistence, UI |
| WorldEnvironmentNetwork.Instance | locator | 11 | Gameplay/Townsfolk, Tools |
| ServerHooks.Saves | state | 9 | Gameplay/Persistence, World |
| RecipeEdit.Default | locator | 6 | Gameplay/Persistence, UI |
| NpcInteractable.Active | locator | 6 | Gameplay/Economy, UI |
| PlayerGm.CommandRunner | state | 6 | Gameplay/Persistence, Tools |
| NetworkConnectionManager.Instance | locator | 6 | UI, World |
| NetworkCombatVFXPool.Instance | locator | 5 | GameFeel, Gameplay/Combat |
| PlayerPet.Local | locator | 5 | GameFeel, UI |
| KnPageDesk.Desk | state | 4 | Gameplay/Persistence, Tools |
| ZoneRules.Current | locator | 4 | Gameplay/Player, Tools |
| PlayerDuel.Local | locator | 4 | Gameplay/Social, UI |
| ZonePortal.Active | locator | 4 | Tools, UI |
| PlayerCadence.LocalCombo | event | 3 | GameFeel, UI |
| PlayerSpawnPoint.Active | locator | 3 | Gameplay/Housing, Tools |
| GmRoster.Granted | event | 3 | Gameplay/Persistence, Tools |
| GmRoster.Revoked | event | 3 | Gameplay/Persistence, Tools |
| UnityKeys.Current | locator | 3 | Gameplay/Persistence, Networking |
| PointOfInterest.Active | locator | 3 | Tools, UI |
| MusicStateLink.RemoteAuthority | state | 2 | Audio, Networking |
<!-- /map:hubs -->

## God classes

Classes over 700 lines. Several of them also reach into three or more other systems.

<!-- map:gods -->
| Class | System | Lines | Other systems it uses | Files using it |
|---|---|---|---|---|
| KnPlayHere | Gameplay/Persistence | 4032 | Core, Gameplay/CharacterCreation, Gameplay/Items, Gameplay/Player, Gameplay/Progression, Networking | 2 |
| PlayerWallRunMotor | Gameplay/Player | 2845 | Core, Gameplay/CharacterCreation, Gameplay/Progression, Networking, World | 11 |
| PointAndClickPlayer | Gameplay/Player | 2449 | Core, Gameplay/Progression, Networking, Rendering | 40 |
| ProgressionHUD | UI | 2236 | Audio, Core, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 6 |
| PlayerAnimancerMotor | Gameplay/Player | 1745 | Core, Gameplay/Progression, Networking | 20 |
| ArrangeController | UI | 1663 | Core, Gameplay/Housing, Gameplay/Items, Gameplay/Player, Gameplay/Progression | 0 |
| GearUIController | UI | 1650 | Core, Gameplay/CharacterCreation, Gameplay/Economy, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression, Gameplay/Restyle, Gameplay/Wallet | 21 |
| GameFeelPresenter | GameFeel | 1431 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Pets, Gameplay/Player, Gameplay/Progression, Networking, UI | 1 |
| ServerSave | Gameplay/Persistence | 1217 | Core, Networking | 22 |
| PlayerCombatSkills | Gameplay/Progression | 1208 | Core, Networking | 25 |
| PlayerMeleeCombat | Gameplay/Combat | 1166 | Core, Gameplay/Player, Gameplay/Progression, Networking | 17 |
| PetFollower | Gameplay/Items | 1056 | Rendering | 5 |
| HudLayoutEditor | UI | 1042 | - | 14 |
| NetworkConnectionManager | Networking | 977 | Core | 15 |
| GearWorkWindowController | UI | 947 | Core, Gameplay/Economy, Gameplay/Forge, Gameplay/Items, Gameplay/NPC, Gameplay/Progression | 0 |
| MountRider | Gameplay/Mounts | 937 | Core, Gameplay/Player, Gameplay/Progression | 0 |
| CharacterCreatorController | UI | 935 | Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Items, Gameplay/Progression | 0 |
| GmCommands | Tools | 924 | Core, Gameplay/Combat, Gameplay/Factions, Gameplay/Items, Gameplay/Player, Gameplay/Progression, Gameplay/Social, Networking, World | 0 |
| PlayerJumpMotor | Gameplay/Player | 913 | Gameplay/Progression, Networking | 12 |
| EnvironmentPresenter | World | 894 | Rendering | 4 |
| PlayerInventory | Gameplay/Items | 887 | Core, Gameplay/Factions, Gameplay/Progression, Networking | 76 |
| CharacterSelectController | UI | 864 | Gameplay/CharacterCreation, Networking | 0 |
| PlayerVaultMotor | Gameplay/Player | 863 | Core, Gameplay/CharacterCreation, Gameplay/Factions, Gameplay/Progression, Networking | 6 |
| PlayerTrade | Gameplay/Economy | 856 | Core, Gameplay/Factions, Gameplay/Items, Gameplay/Persistence, Gameplay/Player, Gameplay/Progression, Gameplay/Social, Gameplay/Wallet, Networking, World | 4 |
| PlayerDeathMotor | Gameplay/Player | 828 | Core, Gameplay/Progression, Networking | 5 |
| KnCadenceProbe | Gameplay/Persistence | 791 | Core, Gameplay/Brains, Gameplay/Combat, Gameplay/Hostiles, Gameplay/Player, Gameplay/Progression, Networking | 1 |
<!-- /map:gods -->

## Design data

Each ScriptableObject class, how many assets of it exist, and where they are wired from.

<!-- map:data -->
| ScriptableObject class | System | Assets | Read by (runtime files) | Wired from prefabs / scenes / other assets |
|---|---|---|---|---|
| ItemDefinition | Gameplay/Items | 1826 | 70 | 0 / 0 / 338 |
| FurnishingDefinition | Gameplay/Housing | 223 | 8 | 0 / 0 / 1 |
| SkillDefinitionSO | Gameplay/Progression | 152 | 25 | 1 / 0 / 1 |
| HallLookDefinition | Gameplay/Housing | 50 | 6 | 0 / 0 / 1 |
| ZoneMinimapData | World | 39 | 3 | 0 / 40 / 0 |
| MusicTrack | Audio | 33 | 15 | 0 / 0 / 5 |
| NpcDefinition | Gameplay/NPC | 24 | 3 | 24 / 0 / 3 |
| StatDefinitionData | Gameplay/Progression | 19 | 0 | 0 / 0 / 0 |
| WeatherProfile | World | 11 | 5 | 0 / 40 / 12 |
| HostileDefinition | Gameplay/Hostiles | 10 | 13 | 20 / 0 / 8 |
| GearPaintLook | Gameplay/Items | 10 | 3 | 0 / 0 / 0 |
| AutoAttackSet | Gameplay/Combat | 9 | 1 | 1 / 0 / 0 |
| HostileZoneDefinition | Gameplay/Hostiles | 8 | 2 | 0 / 2 / 0 |
| GrassType | Environment | 6 | 2 | 0 / 0 / 2 |
| LineageDefinition | Gameplay/CharacterCreation | 6 | 11 | 0 / 0 / 1 |
| MountDefinition | Gameplay/Mounts | 4 | 6 | 0 / 0 / 1 |
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
| MountMaterialiseSettings | Gameplay/Mounts | 2 | 3 | 0 / 0 / 2 |
| CharacterClassData | Gameplay/Progression | 2 | 2 | 1 / 2 / 0 |
| MovementSkillData | Gameplay/Progression | 2 | 4 | 1 / 0 / 0 |
| DeckLibrary | Audio | 1 | 2 | 0 / 40 / 0 |
| MenuMusic | Audio | 1 | 1 | 0 / 4 / 0 |
| GrassRuleSet | Environment | 1 | 1 | 0 / 40 / 0 |
| BeatGlowSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| BeatRingSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| HitLookSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| SmearSettings | GameFeel | 1 | 2 | 1 / 0 / 0 |
| SpiritSettings | GameFeel | 1 | 1 | 1 / 0 / 0 |
| AutopilotSettings | Gameplay/Autopilot | 1 | 5 | 1 / 0 / 1 |
| BrainSettings | Gameplay/Brains | 1 | 3 | 0 / 0 / 1 |
| CharacterCreationDatabase | Gameplay/CharacterCreation | 1 | 24 | 2 / 4 / 0 |
| CharacterExpressionLibrary | Gameplay/CharacterCreation | 1 | 7 | 0 / 3 / 1 |
| CharacterPaletteLibrary | Gameplay/CharacterCreation | 1 | 2 | 0 / 0 / 1 |
| CadenceSettings | Gameplay/Combat | 1 | 4 | 1 / 0 / 0 |
| CraftingSettings | Gameplay/Crafting | 1 | 1 | 1 / 0 / 0 |
| CrossfireSettings | Gameplay/Crossfire | 1 | 2 | 0 / 0 / 0 |
| EconomySettings | Gameplay/Economy | 1 | 8 | 1 / 0 / 1 |
| VendorCatalog | Gameplay/Economy | 1 | 2 | 1 / 0 / 0 |
| FactionCatalog | Gameplay/Factions | 1 | 1 | 0 / 0 / 1 |
| HostileBodyCatalog | Gameplay/Hostiles | 1 | 2 | 0 / 0 / 1 |
| HostileBrainSettings | Gameplay/Hostiles | 1 | 3 | 0 / 0 / 1 |
| HostileSettings | Gameplay/Hostiles | 1 | 5 | 20 / 2 / 1 |
| HostileStatCurve | Gameplay/Hostiles | 1 | 4 | 20 / 0 / 0 |
| LevelGapSettings | Gameplay/Hostiles | 1 | 7 | 20 / 0 / 0 |
| HostileWorldAssets | Gameplay/Hostiles | 1 | 2 | 0 / 0 / 0 |
| FurnishingCatalog | Gameplay/Housing | 1 | 6 | 1 / 0 / 0 |
| HousingSettings | Gameplay/Housing | 1 | 9 | 1 / 0 / 0 |
| RecallSettings | Gameplay/Housing | 1 | 1 | 1 / 0 / 0 |
| FlaskSettings | Gameplay/Items | 1 | 1 | 1 / 0 / 0 |
| GearProgressionSettings | Gameplay/Items | 1 | 12 | 1 / 2 / 1 |
| GearThemeLibrary | Gameplay/Items | 1 | 12 | 0 / 0 / 403 |
| ItemGlowIcons | Gameplay/Items | 1 | 1 | 0 / 0 / 1 |
| GearDictionary | Gameplay/Items | 1 | 7 | 1 / 2 / 0 |
| GearStatTable | Gameplay/Items | 1 | 2 | 0 / 0 / 1 |
| ItemCatalog | Gameplay/Items | 1 | 22 | 2 / 3 / 1 |
| WeaponClassCatalog | Gameplay/Items | 1 | 2 | 1 / 1 / 0 |
| SidekickPartCatalog | Gameplay/Items | 1 | 7 | 0 / 0 / 747 |
| MountCatalog | Gameplay/Mounts | 1 | 1 | 1 / 0 / 0 |
| MountSettings | Gameplay/Mounts | 1 | 4 | 0 / 0 / 1 |
| PetCatalog | Gameplay/Pets | 1 | 3 | 1 / 0 / 0 |
| PetChatterSettings | Gameplay/Pets | 1 | 2 | 1 / 0 / 0 |
| PetEmojiLibrary | Gameplay/Pets | 1 | 0 | 0 / 0 / 0 |
| PetSettings | Gameplay/Pets | 1 | 2 | 0 / 0 / 1 |
| LedgeJumpClips | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| TraversalSettings | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| VaultAnimationMap | Gameplay/Player | 1 | 1 | 1 / 0 / 0 |
| WallstrideSettings | Gameplay/Player | 1 | 6 | 1 / 0 / 0 |
| WaterstrideSettings | Gameplay/Player | 1 | 3 | 1 / 0 / 0 |
| PlayerWorldAssets | Gameplay/Player | 1 | 5 | 0 / 0 / 0 |
| CharacterProgressionData | Gameplay/Progression | 1 | 3 | 1 / 0 / 1 |
| CharacterResourceData | Gameplay/Progression | 1 | 1 | 1 / 0 / 0 |
| CharacterStatData | Gameplay/Progression | 1 | 1 | 1 / 0 / 0 |
| CombatStatTuningData | Gameplay/Progression | 1 | 7 | 21 / 2 / 1 |
| JumpMotionData | Gameplay/Progression | 1 | 3 | 1 / 0 / 0 |
| MovementSettings | Gameplay/Progression | 1 | 3 | 1 / 0 / 0 |
| NovaSettings | Gameplay/Progression | 1 | 4 | 1 / 0 / 1 |
| SerumSettings | Gameplay/Progression | 1 | 5 | 1 / 0 / 1 |
| CrewSettings | Gameplay/Social | 1 | 5 | 1 / 0 / 1 |
| CrewSymbolLibrary | Gameplay/Social | 1 | 3 | 0 / 0 / 1 |
| EmoteWheelSettings | Gameplay/Social | 1 | 3 | 1 / 0 / 0 |
| SponsorSettings | Gameplay/Social | 1 | 3 | 1 / 0 / 1 |
| SquadSettings | Gameplay/Social | 1 | 2 | 1 / 0 / 0 |
| RelevancySettings | Networking | 1 | 2 | 0 / 0 / 0 |
| WorldLodSettings | Rendering | 1 | 2 | 1 / 0 / 0 |
| GlowHaloFeature | Rendering | 1 | 0 | 0 / 0 / 2 |
| SunShaftsFeature | Rendering | 1 | 0 | 0 / 0 / 1 |
| GuideLibrary | UI | 1 | 5 | 0 / 41 / 0 |
| ToastSettings | UI | 1 | 1 | 0 / 40 / 0 |
| KeyGlyphSet | UI | 1 | 2 | 0 / 4 / 0 |
| MenuBookSettings | UI | 1 | 3 | 1 / 0 / 0 |
| DayCycleProfile | World | 1 | 2 | 0 / 40 / 0 |
| SurfaceGraph | World | 1 | 7 | 0 / 0 / 1 |
| WaterMap | World | 1 | 4 | 0 / 0 / 1 |
| WeatherCatalog | World | 1 | 1 | 0 / 40 / 0 |
| WorldScaleSettings | World | 1 | 0 | 0 / 0 / 0 |
| ZoneCatalog | World | 1 | 4 | 0 / 39 / 0 |
| ZoneTerrainProfile | World | 1 | 0 | 0 / 0 / 0 |
| SoakSpots | Gameplay/Hostiles | 0 | 0 | 0 / 0 / 0 |
| GearClothLook | Gameplay/Items | 0 | 2 | 0 / 0 / 0 |
| TownLifeArea | Gameplay/Townsfolk | 0 | 2 | 0 / 0 / 0 |
| TownsfolkCast | Gameplay/Townsfolk | 0 | 2 | 0 / 0 / 0 |
| TownsfolkPersonality | Gameplay/Townsfolk | 0 | 4 | 0 / 0 / 0 |
| TownsfolkSettings | Gameplay/Townsfolk | 0 | 4 | 0 / 0 / 0 |
| TownsfolkWorldAssets | Gameplay/Townsfolk | 0 | 1 | 0 / 0 / 0 |
| TownsfolkViewAssets | Gameplay/Townsfolk | 0 | 1 | 0 / 0 / 0 |
| ColourBlindFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
| ComicLetteringFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
| ComicStyleFeature | Rendering | 0 | 0 | 0 / 0 / 0 |
<!-- /map:data -->

Where tuning still lives in code, and other links the compiler cannot see:

<!-- map:smells -->
- **Gameplay numbers fixed in code:** 459 in 155 files. Most: `Gameplay/Crafting/Data/CraftingSettings.cs` (28), `Gameplay/Player/Entities/Runtime/WallWalk.cs` (20), `Gameplay/Player/Runtime/PlayerWallRunMotor.cs` (18), `Gameplay/Player/Runtime/PlayerVaultMotor.cs` (15), `Gameplay/Player/Runtime/PointAndClickPlayer.cs` (15), `Gameplay/Items/Data/GearProgressionSettings.cs` (13), `Gameplay/Items/Runtime/WeaponPaintRules.cs` (11), `Gameplay/Player/Runtime/PlayerWallRunMotor.WallWalk.cs` (10).
- **Settings fallbacks** (`settings != null ? settings.x : 8`): 109.
- **Runtime writes to shared assets** (ScriptableObject or pipeline fields): 25 — `Rendering/Runtime/PipelineRebuilds.cs:60`, `Rendering/Runtime/PipelineRebuilds.cs:68`, `Rendering/Runtime/PipelineRebuilds.cs:104`, `Rendering/Runtime/PipelineRebuilds.cs:105`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:142`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:143`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:144`, `UI/Runtime/Settings/GraphicsSettingsApplier.cs:145`.
- **Lookups by name** (Type.GetType, Find("..."), Resources.Load, SendMessage): 42.
- **Asset entries that store a class by name and assembly** ([SerializeReference]): 196 in 2 assets.
- **Static accessors that call GetComponent on every use:** 0.
- **FindObject calls in runtime code:** 45.
<!-- /map:smells -->

## The problems, ranked

Ranked by impact (1 to 5: how much it gets in the way of changing the game later) and, among equals, the cheaper fix
first. The page lists the file and line behind each.

<!-- map:problems -->
| # | Kind | Problem | Systems | Impact (1-5) | Files a fix touches | Effort |
|---|---|---|---|---|---|---|
| 1 | Hidden link | 196 asset entries store a class by name and assembly | GameFeel | 4 | 6 | small |
| 2 | God class | PlayerInventory (887 lines) | Gameplay/Items | 4 | 77 | large |
| 3 | God class | PointAndClickPlayer (2449 lines) | Gameplay/Player | 4 | 41 | large |
| 4 | Hub | PlayerInventory.Local (locator) | Gameplay/Items, GameFeel, Gameplay/Autopilot, Gameplay/Economy, Gameplay/Hostiles ... | 4 | 38 | large |
| 5 | Hub | CombatEntity.AnyKilled (event) | Gameplay/Progression, Gameplay/Combat, Gameplay/Hostiles, Gameplay/Pets | 3 | 3 | small |
| 6 | Hub | GmRoster.Granted (event) | Networking, Gameplay/Persistence, Tools | 3 | 3 | small |
| 7 | Hub | GmRoster.Revoked (event) | Networking, Gameplay/Persistence, Tools | 3 | 3 | small |
| 8 | Hub | NetSession.EndReason (state) | Core, Gameplay/Persistence, Networking, UI | 3 | 3 | small |
| 9 | Hub | PlayerCadence.LocalCombo (event) | Gameplay/Combat, GameFeel, UI | 3 | 3 | small |
| 10 | Hub | PlayerSpawnPoint.Active (locator) | Networking, Gameplay/Housing, Tools | 3 | 3 | small |
| 11 | Hub | PointOfInterest.Active (locator) | World, Tools, UI | 3 | 3 | small |
| 12 | Hub | ServerChat.Announce (state) | Core, Gameplay/Crossfire, Gameplay/Items, Gameplay/Social | 3 | 3 | small |
| 13 | Hub | UnityKeys.Current (locator) | Core, Gameplay/Persistence, Networking | 3 | 3 | small |
| 14 | Hub | WorkshopOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Crafting, Gameplay/Forge, Gameplay/Persistence | 3 | 3 | small |
| 15 | Hub | ChatOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 16 | Hub | ContactsOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 17 | Hub | CrewOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 18 | Hub | DuelOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Combat, Gameplay/Persistence | 3 | 2 | small |
| 19 | Hub | FollowOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 20 | Cycle | Gameplay/Autopilot uses Gameplay/Economy against the order | Gameplay/Autopilot, Gameplay/Economy | 3 | 2 | small |
| 21 | Cycle | Gameplay/Hostiles uses Gameplay/Social against the order | Gameplay/Hostiles, Gameplay/Social | 3 | 2 | small |
| 22 | Hub | JoinFlow.Failed (event) | Networking, Tools, UI | 3 | 2 | small |
| 23 | Hub | KeyBindings.Changed (event) | Core, Gameplay/Progression, UI | 3 | 2 | small |
| 24 | Hub | KioskOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Economy, Gameplay/Persistence | 3 | 2 | small |
| 25 | Hub | MountOwnerLink.Current (locator) | Core, Gameplay/Mounts, Gameplay/Persistence | 3 | 2 | small |
| 26 | Hub | MusicConductor.Instance (locator) | Audio, Tools, UI | 3 | 2 | small |
| 27 | Hub | MusicStateLink.Current (locator) | Core, Audio, Networking | 3 | 2 | small |
| 28 | Hub | MusicStateLink.RemoteAuthority (state) | Core, Audio, Networking | 3 | 2 | small |
| 29 | Hub | PartyOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 30 | Hub | PetShowLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Pets | 3 | 2 | small |
| 31 | Hub | RecallShowLink.Current (locator) | Gameplay/Items, Gameplay/Housing, Gameplay/Persistence | 3 | 2 | small |
| 32 | Hub | RestyleOwnerLink.Current (locator) | Gameplay/CharacterCreation, Gameplay/Persistence, Gameplay/Restyle | 3 | 2 | small |
| 33 | Hub | RosterLink.NetworkPrefab (state) | Networking, Tools, UI | 3 | 2 | small |
| 34 | Hub | ServerHooks.RunCommand (state) | Networking, Gameplay/Persistence, Tools | 3 | 2 | small |
| 35 | Hub | ServerShutdown.Handler (state) | Core, Gameplay/Persistence, Networking | 3 | 2 | small |
| 36 | Hub | SkyLiftLink.Requester (state) | Core, Gameplay/Persistence, UI | 3 | 2 | small |
| 37 | Hub | SponsorOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Persistence, Gameplay/Social | 3 | 2 | small |
| 38 | Hub | TradeOwnerLink.Current (locator) | Gameplay/Items, Gameplay/Economy, Gameplay/Persistence | 3 | 2 | small |
| 39 | Cycle | Gameplay/Persistence uses Gameplay/Autopilot against the order | Gameplay/Persistence, Gameplay/Autopilot | 3 | 1 | small |
| 40 | Cycle | Gameplay/Pets uses Gameplay/Social against the order | Gameplay/Pets, Gameplay/Social | 3 | 1 | small |
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
