# Architecture decoupling plan

**Status: agreed 19 Oct 2026 (owner's decisions at the end). Stage 0 comes first, as a PR of its own. Nothing here
has been done yet.**

The aim: changing one system should not force changes in a dozen others. The game must keep working after every step,
and ScriptableObjects must stay the way designers tune the game. The evidence for every step is in
`Reference/Dependency-Map.md` and its page (https://claude.ai/artifact/Uvq5NyTWtTvYWNVoo45gdm), which name each file
and line.

## The rules for every stage

1. **Move before you add.** Most loops exist because a type sits in the wrong system. Moving it to the layer it belongs
   to fixes the loop with no new code. A new interface is added only where a loop crosses and nothing can move, and it
   is one small interface in the lower system, not a framework.
2. **No new machinery.** No event-bus assets, service containers, dependency-injection packages or wrapper layers.
   The project already has the patterns it needs:
   - `ClientPresentationRegistry` and the `Core/I*Sink` interfaces for gameplay to presentation;
   - `ISaveParticipant` for saving;
   - static C# events for "something happened".
3. **ScriptableObjects stay the design data.** Numbers move into the settings assets that already exist, never into new
   code. No stage changes how a designer edits an asset.
4. **Nothing an asset holds may break.**
   - A script moves together with its `.meta`, through Unity or `git mv` of both.
   - A renamed serialized field keeps `[FormerlySerializedAs]`.
   - A class that `[SerializeReference]` entries name (the 169 feel entries in `PFX_GameFeel` / `PFX_MenuFeel`)
     keeps `[MovedFrom]` when its namespace or assembly changes.
   - After a move, grep for the old path and class name in strings (`Type.GetType`, `PlayerSystemsSetup`'s type
     names, builders).
5. **One stage, one PR.** Each PR must:
   - compile;
   - pass the EditMode tests;
   - be played once as host plus a second client (CLAUDE.md);
   - be run through the dependency map with `--check`.

   The game is the same before and after: these are moves, not features.
6. **Ratchet.** After each stage the map's baseline is saved again (`--baseline`), so a cut link cannot quietly come
   back.

## Stage 0: the safety net (before any code moves)

- **Run the EditMode tests from the command line.** Unity's `-runTests` in batch mode, against a copy that is not the
  open Editor, with the results written to a file. Today no script does this.
- **The ratchet.** Save the map's baseline now. `dependency_map.py --check` then fails when a new uphill link or
  two-way pair appears.
- **Tests for the widest-ripple classes nobody tests,** written only around what the coming stages move:
  - `PlayerInventory`: add, remove, the hotbar source;
  - `CombatEntity` / `CombatCalculator`: damage in, death out;
  - `PlayerIdentity`'s pure rules;
  - `ClientPresentationRegistry`: register, replace, clear;
  - `RpcBudget`.

  Small EditMode tests in the systems' existing test assemblies.
- **Mark the 169 feel entries' classes with `[MovedFrom]` now.** It costs nothing while nothing has moved, and it
  protects `PFX_GameFeel` and `PFX_MenuFeel` from every later stage.

## Stage 1: the uphill links (8 links, about 14 files)

| Link | Fix |
|---|---|
| Core → Progression (`ICombatFeedbackSink` uses `DamageResult`) | Move `DamageResult` into Core. It is a plain result struct. |
| Networking → Player / Progression / Factions / Social / Persistence (`ServerControlPage`, `ServerBans`, `ServerRecords`, `SecurityFlags`, `ServerLifetime`) | These are server administration, not networking. Move `Networking/Runtime/Server/` and `Networking/Runtime/Safety/` into a **Server** system that sits above gameplay (it reads players, saves and chat by design). |
| Networking → Persistence / CharacterCreation (`NetworkConnectionManager` uses `ServerSave`, `CharacterSession`) | Networking raises events it owns (a client joined, the server is stopping). Persistence and CharacterCreation subscribe at start-up. |
| Networking → Player (`NetworkDebugHUD` uses `KeyBindings`) | `KeyBindings` is input settings: move it into Core (Progression's `PlayerSkillHotbar` uses it too). |
| Combat → Audio (`PlayerCadence` uses `MusicConductor`, `BeatMath`, `CombatBeat`, `MusicTrack`) | The beat arithmetic is game rules the server also checks: move `BeatMath`, `CombatBeat` and the `MusicTrack` data down into Combat. `MusicConductor` stays in Audio and plugs in through one small `IBeatClock` interface that Combat owns. |

Result: Core knows no gameplay, and Networking is transport, sessions and `RpcBudget` only.

## Stage 2: split Progression into its two halves (breaks 3 loops)

- **Lower half: the combat model.** `CombatEntity`, `IDamageable`, `CombatCalculator`, `CharacterBuffManager`,
  `CharacterCombatStats`, `StatType`, `WeaponClassId`, `KillingBlow`, `IHitChanceModifier`, and the stat and movement
  data. Move it into a system that sits at the bottom of the gameplay layer, `Gameplay/Stats`.
  Items, Hostiles, Combat and Player then use Stats, not Progression.
- **Upper half: player features.** `PlayerNova`, `PlayerSerums`, `PlayerExperienceBoosts`, `PlayerCombatSkills`,
  the skill hotbar, Ascension and Standings. These stay in Progression, which then sits above Items, Economy and
  Persistence, where it already reaches.
- `PlayerNova` and `PlayerSerums` call `PlayerSaveAgent.SaveSoon()`. They get the same call through a one-method
  `ISaveScheduler` in Core that `PlayerSaveAgent` implements. This cuts Progression → Persistence.

## Stage 3: one way to find the local player

Replace the 27 `static X Local => PlayerInventory.Local?.GetComponent<X>()` accessors with **one** `LocalPlayer`
class in the Player system:
- it is set when the local player spawns and cleared when it despawns;
- it caches each component once.

The accessors keep their names, so the 63 calling files do not change in this stage. Only their source moves off
`PlayerInventory`. This is a runtime saving too: a search per call becomes a field read.

## Stage 4: the money types and the map markers

- **Wallet and ledger down.** `PlayerWallet`, `CredsSource`, `CredsSink` and `EconomyLedger` (`LedgerKinds` is already in
  Core) are the rules every economy rework must keep (CLAUDE.md, milestone 3). Move them into a small **Wallet** sub-folder of
  Economy that sits below Items, Progression and Social; Kiosks, trade and vendors stay above. This also prepares the
  economy rework: the wallet and ledger stay fixed while the rules on top change.
- **`TradeZone` into World.** It is a place on the map, and Combat, Hostiles and Social only ask "am I in one".

## Stage 5: the remaining loops (one or two files each)

| Loop | Fix |
|---|---|
| Player → Combat | `PointAndClickPlayer`'s click-to-attack calls an `IAttackCommand` that Player owns and `PlayerMeleeCombat` implements. Death and recall raise events, or read one `IsInFight` query, instead of reaching into `PlayerMeleeCombat`, `PlayerDuel` and `PlayerCombatState`. |
| World → Player | Travel stays in World (owner, 19 Oct 2026). Travel, portals, trees and rain use a small `ILocalBody` in Core (position, airborne, move to a point) that Player registers for the local player. |
| Persistence → Items / Player / CharacterCreation | `PlayerSaveAgent` stops building the select-screen loadout itself. That becomes an `ISaveParticipant` owned by CharacterCreation. `PlayerHotbarMemory` moves to the hotbar's own system. |
| Social chat used by Items, Pets, Hostiles, Server | One `IChatBroadcast` in Core that `PlayerChat` registers, for "say this line to these players". |
| Hostiles → Social (party share, Sponsor bonus in `HostileKillRewards`) | Social listens to the kill event that already exists and adds the party and Sponsor part. |
| One-file loops | `CharacterPortraitStage` moves to CharacterCreation. Combat finds `WeaponHitbox` itself, so `WeaponVisualMount` no longer needs it. `PlayerPet` targets through `IDamageable` instead of `HostileNet`. `PlayerRecall` asks Housing for its destination through the recall item's own handler. |

## Stage 6: one assembly per system

Once the map shows no loop left between two systems, each system's `.asmref` becomes its own `.asmdef`. The order
follows the map's layers, bottom first: Core, Networking, Stats, Wallet, Items, Player, Combat, World, Progression, the
rest. After each one: compile, run the tests, run `--check`.

From then on the compiler, not this document, refuses a loop. Edit-time recompiles also get faster, because a change
in UI or Economy no longer recompiles the whole 75k-line runtime assembly.

## Stage 7: design data back in the Inspector

- **Tunables into existing settings.** Move the gameplay numbers fixed in code into the settings asset of their
  system, where a designer would look for them:
  - traversal geometry and timing into `TraversalSettings` / `WallstrideSettings`;
  - `PointAndClickPlayer`'s camera numbers into a new `CameraSettings` asset, and its click-travel numbers into
    `MovementSettings` (owner, 19 Oct 2026);
  - party limits into `SquadSettings`;
  - storage pages into `EconomySettings`.

  Engineering limits (probe steps, epsilons) stay in code.
- **Missing settings fail loudly.** Replace the 93 `settings != null ? settings.x : N` fallbacks with one check at
  start-up that names the missing asset. Add a rule to Validate Project Organization that flags a new fallback.
- **Builders seed once.** `HostileAssetBuilder`, `BladeSkillBuilder` and the `GameFeelBuilder` tables fill only
  what is missing, as the pet and vendor builders already do. A designer's Inspector edit then survives a rebuild.
  Item IDs stay where they are; they are identities, not tunables.
- **`WorldLodSettings`** puts the pipeline value it changes back when Play stops.

## Stage 8: big classes, only where it cuts coupling

`PointAndClickPlayer` (1,989 lines, five systems) and `GearUIController` (1,464 lines, the UI's window manager used
by 20 windows) are split only along lines the earlier stages expose:
- `PointAndClickPlayer`: camera, click travel, targeting, and the auto-attack maths that belongs to Combat;
- `GearUIController`: tooltips, the drag ghost and world-input blocking.

`ProgressionHUD` and the motors are big but mostly self-contained; they are left alone unless a change needs them.

## Order and size

Stages 0 and 1 are small and safe and come first. Stage 2 is the biggest win: it takes most of Progression out of the
tangle. Stages 3 to 5 are each a few files. Stage 6 is mechanical once the map is clean. Stages 7 and 8 can run any
time, beside the economy rework, because they move no system borders.

## Decisions (owner, 19 Oct 2026)

1. **Names:** the new systems are **Server** (administration), **Stats** (the combat model) and **Wallet** (money and
   the kept ledger).
2. **Travel stays in World.** Zone travel and portals move the player through a small `ILocalBody` handle in Core
   (where the local player stands, whether it is in the air, and moving it to a point). Player registers it when the
   local player spawns. World then uses Core, not Player. Trees and rain read the same handle.
3. **Camera numbers get a new `CameraSettings` asset.** It holds the click-to-move camera's zoom range, orbit speed
   and the like. The click-travel numbers (arrival distance, stall limits) go into `MovementSettings`.
4. **The first PR is stage 0, the safety net, alone.** No class moves in it.
