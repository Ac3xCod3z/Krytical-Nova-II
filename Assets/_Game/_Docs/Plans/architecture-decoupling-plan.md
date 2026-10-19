# Architecture decoupling plan

**Status: agreed 19 Oct 2026 (owner's decisions at the end); the owner then handed the stages over to be done in
order without asking. Stages 0 to 7 are built, one PR each; stage 8 was found not to be needed (below).**

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

## Stage 2: Progression stops reaching into Items, Economy's data and Persistence (built 19 Oct 2026)

The map showed that almost all of Progression is the low-level combat model everyone needs. So instead of moving
that model out, the few features in Progression that reached upward were turned around:

| Link | What was done |
|---|---|
| Progression → Persistence (Nova and Serums asked the save agent to save soon) | A one-method `ISaveScheduler` in Core, which `PlayerSaveAgent` implements; they find it on their own object. |
| Progression → Items (Skill Chips, Serums and XP boosters are items) | The item-use contract moved to Core: `IItemUseHandler`, `ItemUseEffect` (its stored values unchanged) and `UsedItem` (an item's id and name, all a handler reads). The inventory hands each handler the item it uses. |
| Progression → Items (a booster's name) | A one-method `IItemNames` in Core, which `PlayerInventory` implements. The boosts' tooltip line takes the item's id. |
| Progression → Economy (the booster table lives in `EconomySettings`) | The `ExperienceBooster` struct moved to Progression with an `IExperienceBoosterTable`, which `EconomySettings` implements; the boosters stay in the same asset. The boosts' `settings` field (same name, same reference on the player prefab) takes the asset through that; dropping any other asset there is refused with a warning. Moving the boosts into Economy was tried first and dropped: Kiosks sell pets, so Economy sits above Pets, while Pets reads the boosts. |

Left for later stages: `PlayerSerums.Local` (stage 3) and Nova paying with the wallet (stage 4).

## Stage 3: one way to find the local player (built 19 Oct 2026)

`Core/Runtime/LocalPlayer.cs` holds the player this machine controls. `PlayerInventory` still decides who that is, as
it always has, and sets it there; `LocalPlayer.Changed` fires just before `PlayerInventory.LocalChanged`. All 28
`X.Local` accessors (wallet, trade, pet, chat, Crew, Serums, Nova...) now read `LocalPlayer.Get<X>()`, which finds a
component once and keeps it, in place of a component search through the inventory on every call. Their names did
not change, so none of their callers did. The fight music and the rain find the local player there too, without
knowing Items. `LocalPlayerTests` checks the keeping, a new player starting afresh and a player that is gone.

## Stage 4: the money types and the map markers (built 19 Oct 2026)

- **The Wallet system** (`Gameplay/Wallet`, owner's name) holds what everything that pays needs, and nothing else:
  - `CredsSource` and `CredsSink`, with their values unchanged, plus `WalletRules` (moved whole: it uses nothing);
  - `IWallet`: Creds, can afford, spend;
  - `LedgerNote`: a ledger line from a system below Economy.

  `PlayerWallet` stays in Economy, implements `IWallet`, and still tallies every Cred. Nova, the Flask and Crews pay
  through `IWallet`. A Crew founding writes its line through `LedgerNote`, which `EconomyLedger` fills at load and
  writes as every other line. With it empty, the line is reported as an error, never dropped silently.
  `LedgerNoteTests` checks it is filled. The save service and the database are untouched: the economy rework keeps
  `SaveTogether` and the ledger as the owner asked.
- **`TradeZone` moved to World** (with its `.meta`; the starter town's two markers keep working): it is a place on the
  map, and Combat, Hostiles and Social only ask whether a point is neutral ground.

Economy no longer has a loop with Items, Progression or Social; seven two-way pairs remain.

## Stage 5: the remaining loops (built 19 Oct 2026)

Every two-way pair is gone and the map finds **no link left to cut**: each system now uses only systems below it.

| Loop | What was done |
|---|---|
| Pets → Hostiles (a pet's kill XP) | `IHostile`, an empty marker in Core that `HostileNet` carries. |
| Items → Persistence (Flask, loadouts, loot) | `ISaveScheduler` gained `Save()` and `TallyItems()`; `PlayerSaveAgent` implements them as before. |
| Items → Social (drop announcements) | `ServerChat` in Core, filled by Social at load; `PlayerChat.ServerAnnounce` does the work as before. |
| Items → CharacterCreation | `CharacterPortraitStage` moved to CharacterCreation: it shows a character. |
| Player → Housing (Recall) | Recall (`PlayerRecall`, `RecallRules`, `RecallSettings` and its asset, its tests) moved to Housing: it takes you home. |
| Player → Combat (click to attack, death, the duel rule) | Player owns `IMeleeAttacker`, `IDuelState` and `HostilityCheck` (`CombatContracts.cs`). `PlayerMeleeCombat` and `PlayerDuel` implement them, and Combat fills `HostilityCheck` with its own rule (`HostilityCheckTests`). The fight sits above the body. |
| World → Player (portals, travel, rain, trees) | As the owner decided, travel stays in World. `IPlayerBody` / `PlayerBodies` in Core is a player's body as the world sees it; `PointAndClickPlayer` implements it and `PlayerBodies.All` reads its own player list. `TreeProxyField` moved to Player: it exists for Wallstride. |
| Items → Combat (the weapon's hitbox) | `IWeaponReach` (Items), which `WeaponHitbox` implements. The mount's `hitbox` field keeps its name and its reference on the prefab. |

The map also stopped taking a method named like a class (`ServerSave(index)` in the loadouts) for that class.

## Stage 6: one assembly per system (built 19 Oct 2026)

`KryticalNova.Runtime` is now Core alone (its name kept, so nothing above had to rename it). The 22 systems it held
are assemblies of their own, `KryticalNova.<System>`:
- Audio, Networking, Rendering and World;
- Autopilot, CharacterCreation, Combat, Crafting, Economy, Factions, Forge, Hostiles, Housing, Items, NPC, Persistence,
  Pets, Player, Progression, Restyle, Social and Wallet.

How it was done:
- Each system's `Runtime` asmref became its asmdef (history kept); its `Data` asmref joins it.
- Each one references Core and the systems below it that it uses, so **the compiler now refuses a loop**.
- UI, GameFeel, DevTools, the Editor assemblies and the tests reference all of them.

Changes the split itself needed:
- Leftover `using` lines for namespaces an assembly no longer sees were dropped, and six helpers of
  `CharacterPreviewStage` became public (the portrait stage, now in CharacterCreation, shares them).
- `PlayerSystemsSetup` finds a row's type through Unity's `TypeCache` instead of in `KryticalNova.Runtime` alone.
- `compile_check.py` now hands each assembly only the assemblies it names, as Unity does, so it catches what Unity
  would refuse.

What this buys:
- A change in one system recompiles that system and the ones above it, not 75k lines.
- A reference that would make a loop is a compile error the moment it is written.

## Stage 7: design data back in the Inspector (built 19 Oct 2026, in part)

Done:
- **Click-to-travel numbers into `MovementSettings`.** These are the arrival distance, stall time and speed, map-point
  search radius, friendly approach, pick-up stand-off and how long a walk to something is kept.
  `PointAndClickPlayer` reads them through the movement skills that hold the asset. Each starting value is written once,
  in `MovementSettings`, and the existing asset loads with today's values.
- **The vault's reach into `TraversalSettings`.** This covers the farthest run-up, how far the landing may move, how
  far to the side a vault may start and the tallest step down after it. The motor's probe steps, tolerances and
  network timings are engineering limits and stay in code, as do the jump's.
- **The camera numbers turned out to be Inspector fields already** on the player prefab: zoom range, orbit speed,
  zoom speed and ray distance. The `Default...` constants only seed them. So a separate `CameraSettings` asset (the
  owner's pick) would only move numbers a designer can already tune; it was not made. Say so if it is still wanted.
- **`WorldLodSettings` puts the pipeline's small-mesh cull back when Play stops** (Editor only). Before, each Play
  left it changed in `PC_RPAsset.asset`, which then showed as a changed file.
- **`PlayerSettingsWiringTests`** fails when any settings or tuning asset on the player prefab is not wired. That is
  the real risk behind the code's fallback numbers.

Left for the owner, because each changes how the game or the tools behave:
- **The 93 `settings != null ? settings.x : N` fallbacks.** Making them fail loudly would change what a misconfigured
  scene does. The wiring test now guards the player; scenes could get the same test.
- **Builders that rewrite numbers on every run** (`HostileAssetBuilder`, `BladeSkillBuilder`, `GameFeelBuilder`).
  Making them fill only what is missing would stop a table edit from reaching assets that already exist. That changes
  how content is tuned, so it is the owner's call.

## Stage 8: big classes (not needed for coupling)

Stage 8 was "only where it cuts coupling", and stages 1 to 6 already cut it:
- `PointAndClickPlayer` reaches Combat only through `IMeleeAttacker` and `HostilityCheck`;
- the motors no longer touch World;
- `GearUIController` is the UI's own window manager inside the UI assembly.

Splitting these files for size alone, without Play testing every path, risks more than it gains, so they are left as
they are. A split remains worthwhile when a change has to be made in one of them anyway.

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
