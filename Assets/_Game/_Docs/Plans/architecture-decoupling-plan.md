# Architecture decoupling plan

**Status: agreed 19 Oct 2026 (owner's decisions at the end); the owner then handed the stages over to be done in
order without asking. Stage 0 is built; the rest follow, one PR each.**

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

## Stage 0: the safety net (built 19 Oct 2026)

- **Compile a checkout without the open Editor.** `Tools/Editor/Architecture/Scripts~/compile_check.py` builds all
  33 first-party assemblies of a worktree with `dotnet` in about a minute. `--server` builds the runtime ones with
  the dedicated server's defines. It borrows the project files Unity keeps in the main folder and uses the
  worktree's own scripts, so a refactor is checked before any Editor sees it.
- **Run the EditMode tests from the command line.** `run_editmode_tests.py` runs them in a batch Unity on a worktree
  (below normal priority, through the Accelerator) and reads out every failure. The open Editor is never touched.
- **The ratchet.** `dependency_map.py --check` fails when any of these happens:
  - a new uphill link or two-way pair appears;
  - a script that prefabs, scenes or data assets hold loses its `.meta` GUID, or its class stops matching the file
    name (274 such scripts are in the baseline);
  - a class named by `[SerializeReference]` entries changes namespace or assembly without `[MovedFrom]`.

  This last check replaces marking the 169 feel entries in advance. `[MovedFrom]` only means something once a class
  has moved, and the check now stops any move that forgets it.
- **Tests for the hubs nobody tested:**
  - `ClientPresentationRegistry`: a registered sink is the one gameplay reaches; an old sink going away leaves its
    replacement plugged in; pointer blocks.
  - `RpcBudget` / `CallLimits`: no limit without a server; a limit for every kind of request; the defaults come back.
    These live in the new `KryticalNova.Networking.Tests.Editor`.
  - `CombatCalculator` was already covered by the Hostile stat-curve tests.
  - `PlayerInventory` and `PlayerIdentity` get their tests with the stages that move them (3 and 5), where the
    behaviour being kept is clear.

## Stage 1: the uphill links (built 19 Oct 2026)

All 8 are cut: no system uses a system above it any more, and the two-way pairs fell from 21 to 13. What moved, and
the few hooks that replaced a reach upward:

| Link | What was done |
|---|---|
| Core → Progression (`ICombatFeedbackSink` used `DamageResult`) | `DamageResult` moved to `Core/Runtime/DamageResult.cs` (namespace `KryticalNova.Core`). |
| Networking → Player (`NetworkDebugHUD` used `KeyBindings`) and Progression → Player (the hotbar) | `KeyBindings` moved to Core with its `.meta`. What it needed from Networking, the machine profile's name, moved to Core too (`LocalProfile`); `NetworkConnectionManager.LocalProfile()` / `EditorCloneProfile()` still answer. |
| Networking → CharacterCreation (the signed-in account id) | The account id (proven by sign-in, else made up per profile) moved to Core as `LocalAccount`; `CharacterSession.AccountId` / `VerifiedAccountId` / `MadeUpAccountId` still answer. |
| Networking → Player / Progression / Factions / Social / Persistence (security flags, records, bans, the control page, the safe stop, the start) | One file of hooks Networking owns, `ServerHooks`: who a player is (`DescribePlayer`, filled by Player's `ServerPlayerFacts`), announcing (`Announce`, filled by Social's `ServerAnnouncer`) and the saves (`IServerSaves`, filled by Persistence's `ServerSaveHooks`, each call going straight to `ServerSave` / `ServerBackups` as before). They are filled at load (and in the Editor, for EditMode tests); `ServerHooksTests` checks that. Moving the server's administration up into a system of its own was looked at and dropped: the connection manager starts all of it, so every move only made a new uphill link. |
| Combat → Audio (`PlayerCadence` read `MusicConductor`, `CombatBeat`, `BeatMath`, `MusicTrack`) | Combat owns a small `IBeatClock` (`Gameplay/Combat/Runtime/IBeatClock.cs`); `MusicConductor` implements it and registers where it sets its own `Instance`. A Cadence press keeps its track as a plain asset reference. |

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
