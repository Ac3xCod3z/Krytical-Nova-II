# Start Here: a new developer's guide to Krytical Nova II

Welcome. This page is the way in for anyone joining the project. Read it top to bottom on your first day (about half an
hour) and you will know what the game is, how to run it, how the code is laid out, the handful of patterns every system
uses, how to check your work, and the house rules. Every other document is linked from here, in the order worth
reading it.

The Encyclopedia of Nova (the page you are probably reading this in) is the project's front door. It is built from
the documents in `Assets/_Game/_Docs`, so what you read here is what is in the repo.

---

## 1. The game in one page

**Krytical Nova II** is an online action RPG. Its systems are modelled on the Korean MMO **Twelve Sky 2** (levels and
stats, nine weapon classes, gear you temper and fuse, pets, faction wars), but the setting, story, names and art are
our own: a Western comic-book look, with hits lettered like comic panels.

- **The world:** the planet Nova, wounded long ago when an industrial civilisation learned to capture the souls of the
  dead. Read `Design/Lore-Bible.md` before writing any story or flavour text; it is canon and is never contradicted.
- **The three factions:** the **Hegemony**, the **Null** and the **Covenant**. A player picks one when making a
  character; it decides the look of their menus and HUD, their home city and who they fight. `Design/11_factions_lore.md`
  is how they look and play.
- **How a player plays:** click to move and click to attack (with WASD as well), skills on a hotbar, gear from Hostiles
  (our word for monsters) and shops, a pet at their side, Creds (the money) to trade, Crews (guilds) to join.
- **Online:** the server decides everything that matters (damage, loot, money, saves). The game runs as a host in the
  Editor and on a dedicated server for real play. The plan to take it fully online is ten phases in
  `Plans/online-roadmap.md`.

Our names are not Twelve Sky's. The **Glossary** chapter of this page gives every term three ways: the Twelve Sky
name, ours, and what the code calls it. Player-facing text always uses ours. When you meet a word you do not know
(Flux, Renown, Tempering, Kiosk, Callout, Wallstride...), look it up there first.

**What is built and what is not:** the score strip at the top of this page counts the systems by status. Each system
strip says what the game has today and what is left. Many systems in the design docs are deliberately not built yet:
build only what you are asked to.

---

## 2. Your first day: get it running

1. **Unity:** install **Unity 6000.6.3f1** (Unity 6.6) through Unity Hub, with the Windows build support module.
   The project uses URP, Netcode for GameObjects, the Input System, Cinemachine and the Dedicated Server package.
2. **Clone** `github.com/Ac3xCod3z/Krytical-Nova-II`. The vendor packs (Synty, KINEMATION, Emerald AI, Feel and the
   rest) are committed, so a clone has everything. Ask the owner which paid packs your seat is licensed for.
3. **Open the project** in Unity Hub. The first import takes a long while (the project is large). The owner's PC runs
   a Unity Accelerator that shares imports; on the same network, ask for its address and set it in
   **Edit ▸ Preferences ▸ Cache Server** to cut the import time.
4. **Play:** open `Assets/_Game/Scenes/MainMenu/Title.unity` and press Play. With **Krytical Nova ▸ Front End ▸ Play
   From Title Screen** on (the default), Play always starts at the title: make a character, pick a faction, and you
   land in the starter town, `Zone01_StarterTown`, as the host.
5. **Look around:** **H** opens Settings, whose GUIDE page explains every built system to a player. **F2** opens the
   GM console (`gm.help` lists the commands); **Krytical Nova ▸ GM Tools** is the same with buttons. `gm.level 60`,
   `gm.creds 100000`, `gm.kit gear 60` and `gm.hostile.spawn` are a quick way to see the later game.
6. **Run the tests:** **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**. About a thousand tests; all pass on
   `main`.

**Never point anything at the owner's real server.** It runs on UDP **7777 and 7778** with its control page on TCP
**8090**, and its saves live in a Postgres database. Your tests use other ports (section 7).

---

## 3. What to read, in order

| When | Read | Why |
|---|---|---|
| Day 1 | This page | The map of everything |
| Day 1 | `ProjectOrganization.md`, sections 1 to 4 | Where every file goes, and the assemblies |
| Day 1 | `Reference/12Sky/README.md` | How we name things and use the Twelve Sky reference |
| Day 1 | The Glossary chapter and the system strips of this page | The vocabulary, and what is built |
| Day 2 | `Reference/Dependency-Map.md` | How the systems depend on each other, and why the stack is shaped as it is |
| Day 2 | `Plans/architecture-decoupling-plan.md` | How the code was split into one assembly per system, and the patterns that came out of it |
| Day 2 | `Guides/Server-Guide.md`, sections 1 to 3 | How the dedicated server runs and is looked after |
| Day 2 | `Design/Lore-Bible.md` | The story canon |
| When you work on a system | Its design doc in `Design/` and its plan in `Plans/` | How it is meant to work, and how it was built |
| When you work on gear | `Reference/12Sky/02_Gear_System_Spec.md` | The gear ladder, Tempering, Fusion, Tuning |
| When you work on combat | `Reference/12Sky/04_Weapon_Classes.md` | The nine weapon classes and their skills |
| When you touch money, trade or saves | `Reference/12Sky/05_Economy_And_Social.md` | Every economy number and rule, as built |
| When you touch the look | `Design/Rendering-Bible.md`, `Design/13_comic_effects.md`, `Design/14_interface_skins_menus_settings.md` | The visual rules |
| When you release | `Guides/Release-Guide.md` | Building, publishing and the launcher |

The **Library** chapter of this page holds every document; any underlined name opens it in the reader at the top.

---

## 4. The codebase in ten minutes

### Where things are

Everything we make lives in `Assets/_Game/`, in a folder **per system**, never per file type (no `Scripts/` or
`Prefabs/` buckets). Vendor packs live untouched in `Assets/_ThirdParty/`. The question to ask of any new file is
"what system owns this?" (`ProjectOrganization.md`, section 1).

```
Assets/_Game/
├── _Docs/        every document (Design, Plans, Reference, Guides) and the Encyclopedia's builder
├── Core/         the contracts every system may use; knows nothing about gameplay
├── Gameplay/     one folder per game system: Player, Combat, Progression, Items, Economy, Wallet, Social,
│                 Pets, Hostiles, Housing, Forge, Crafting, Restyle, Autopilot, Persistence, NPC, Factions,
│                 CharacterCreation
├── World/        maps, travel, portals, weather, day cycle, minimap
├── Networking/   connecting, the dedicated server, its control page and the server app
├── Rendering/    URP settings, quality presets, upscalers
├── Audio/        music (and Cadence, the beat of the fight music)
├── UI/           every window, HUD and menu (never on the server)
├── GameFeel/     hit effects, smears, comic lettering (never on the server)
├── Characters/   models, rigs and animations (representation only)
├── Environment/  props, vegetation, grass
├── Icons/        the icon library
├── Scenes/       MainMenu/, World/ (every map of the World Atlas), Development/ (test arenas)
└── Tools/        editor tools (Tools/Editor), GM commands (Tools/Debug)
```

Inside a system the folders are always the same: `Runtime/` (game code), `Data/` (ScriptableObject classes) with
`Data/Assets/` (their assets), `Editor/` (builders and inspectors), `Tests/Editor/` (EditMode tests), and `Prefabs/`
when it has any.

### Assemblies: the compiler keeps the systems apart

Each system's `Runtime/` is its own assembly, `KryticalNova.<System>`, and may reference only the systems **below** it.
Core (`KryticalNova.Runtime`) is at the bottom; UI, GameFeel and the tools are at the top. A reference that would close
a loop does not compile. The full table is `ProjectOrganization.md`, section 4; the order and the reasons are in
`Reference/Dependency-Map.md`.

```
 Tools, GM commands                  may use anything
 UI · GameFeel                       watch gameplay; gameplay never calls them
 Forge · Restyle · Housing · ...     systems built on top of others
 Economy · Social · Pets · Hostiles  ...
 Items · Combat · Progression · ...  the core of play
 Player · Wallet · Factions · ...    the lowest game systems
 Networking · World · Rendering
 Core                                contracts only
```

When a lower system needs something from a higher one, the lower one owns a small **interface** and the higher one
implements it. You will meet these: `IWallet` (Wallet, implemented by Economy's `PlayerWallet`), `IMeleeAttacker`
(Player, by Combat), `IPlayerBody` (Core, by Player), `IItemUseHandler` (Core, by every system an item can trigger),
`IWeaponReach` (Items, by Combat). This is the one pattern to reach for before anything else.

---

## 5. The patterns you will meet in every system

| Pattern | What it looks like | Where to see it |
|---|---|---|
| **The player is one prefab with a component per system** | `Gameplay/Player/Prefabs/Player.prefab` carries about forty networked components: `PlayerInventory`, `PlayerWallet`, `PlayerPet`, `PlayerCrew`... A new player-side system adds a row to `PlayerSystemsSetup` and runs **Krytical Nova ▸ Setup ▸ Player Prefab** | `Tools/Editor/Setup/PlayerSystemsSetup.cs` |
| **The server decides** | The owner asks with `RequestX()`, which sends an RPC (`XRpc`, rate-limited by `RpcBudget`) to the server, which runs `ServerX()`. Only `ServerX` changes state; clients are told the result | `PlayerNova.RequestAscend` → `AscendRpc` → `ServerAscend` |
| **Design numbers live in ScriptableObjects** | Every tunable is a field on a settings asset in the system's `Data/Assets/` (`EconomySettings`, `MovementSettings`, `HostileSettings`...), edited in the Inspector. Code keeps no magic numbers | any `Data/` folder |
| **Content is made by builders** | Items, skills, Hostiles, vendors and icons are generated by editor builders from tables (menus under **Krytical Nova**). Re-run the builder after changing its table; some builders rewrite their assets each run | `Gameplay/Items/Editor`, `Gameplay/Hostiles/Editor` |
| **Finding the local player** | `LocalPlayer.Get<T>()` (Core), or a system's own `PlayerX.Local` | `Core/Runtime/LocalPlayer.cs` |
| **Gameplay never calls the UI** | Gameplay talks to the screen through sinks in Core: `ClientPresentationRegistry.Messages` (a notice), `.Feel` (an effect), `.Markers` (a target ring). UI and GameFeel register themselves | `Core/Runtime/ClientPresentationRegistry.cs` |
| **Hooks for the server** | Networking calls up into gameplay through `ServerHooks` that the higher systems fill when they load (`[RuntimeInitializeOnLoadMethod]`) | `Networking/Runtime/ServerHooks.cs`, `Gameplay/Social/Runtime/ServerAnnouncer.cs` |
| **Saving** | Each player component that keeps anything implements `ISaveParticipant` and saves itself; `PlayerSaveAgent` writes them all. Anything that changes **two** players or a player and the world saves through `ServerSave.SaveTogether` in one go | `Core/Runtime/SaveSystem`, `Gameplay/Persistence` |
| **Money** | Every Cred goes through `PlayerWallet` with a named `CredsSource` or `CredsSink`; every item moved by an economy action writes a line with `EconomyLedger` | `Gameplay/Wallet`, `Gameplay/Economy` |
| **Spawned network prefabs are registered** | A prefab the server spawns must be listed in `GameplayPrefabsSetup.SpawnedPrefabs`, or clients never see it | `Tools/Editor/Setup/GameplayPrefabsSetup.cs` |
| **GM commands** | Quantum Console commands named `gm.*` in `Tools/Debug` (one file per area: `ItemCommands`, `EconomyCommands`, `CombatCommands`...); each also gets controls in the GM Tools window | `Tools/Debug/GmCommands.cs` |

### One feature end to end: drinking from a Flux Flask

Follow this once in the code and the rest of the game will read the same way.

1. The player presses the hotbar key. The UI calls `PlayerInventory.UseItem(itemId)` on the player's own machine.
2. `UseItem` finds the item in the Pack and sends `UseRpc` to the server (rate-limited by `RpcBudget`).
3. On the server, `PlayerInventory` checks the item can be used (alive, not on the shared item cooldown, the effect's
   own reason via `UseBlockedReason`), then takes it out of the Pack.
4. The item's `useEffect` is `FluxFlask`, so the inventory hands it to the player component that handles that effect,
   through `IItemUseHandler`: here `PlayerFlask.ServerUse`. The inventory never knows what a Flask is.
5. `PlayerFlask` restores Flux over a few seconds and spends one charge, which is stored in the item's own data.
6. The changed Pack is replicated to the owner; the UI redraws from it. The next save of the player writes it.
7. Refilling a Flask at an Apothecary is `PlayerFlask.ServerRefill`: it pays through `IWallet` with the sink
   `CredsSink.Vendor`, so the money is tallied and written to the ledger like every other Cred.

The tunables (charges, the refill price) are on the `FlaskSettings` asset, not in the code.

---

## 6. House rules

These are not style preferences: each one exists because breaking it broke something.

- **Names.** Use the Krytical Nova term in anything a player sees and in new code; look it up in the Glossary. Add a
  glossary row, with a plain "What it is for", for every new term (`Reference/12Sky/README.md`).
- **Files.** Every file goes in its owning system's folder and is covered by an assembly; namespace
  `KryticalNova.<System>`. Move assets through Unity (or with their `.meta`), never by hand in Explorer. Check with
  **Krytical Nova ▸ Project ▸ Validate Project Organization**.
- **Vendor packs are untouched.** Never edit a file under `_ThirdParty`, `Synty` or the other vendor folders; read the
  whole diff and ask before reverting one.
- **Design numbers go in settings assets**, never in code. Economy numbers go in `EconomySettings` or `CrewSettings`.
- **Every player-side system** is a row in `PlayerSystemsSetup`; **every spawned prefab** is in
  `GameplayPrefabsSetup.SpawnedPrefabs`.
- **Icons** come from the icon library (**Krytical Nova ▸ Icons ▸ Icon Library**): every skill has its own, none is
  used twice; **Check Icons** must come back clean.
- **Story** follows the Lore Bible. Rumour is kept apart from history, and one faction stays hidden until its
  expansion: read the Lore Bible before you write a line.
- **Money and saves.** Anything that changes two documents saves through `ServerSave.SaveTogether`; every Cred has a
  `CredsSource` or `CredsSink`; every item moved by an economy action writes a ledger line. The save service never
  learns the economy's rules.
- **The owner's real server and saves are off limits to tests.** Real server: UDP 7777 / 7778, page 8090, database
  `krytical_nova`. Tests: the test server on 7796 / page 8096, the database `krytical_nova_test`, the save service's
  `--test` mode on port 7802. Secrets live outside the repo; never copy them in.
- **UI prompts show keyboard keys**, never gamepad buttons.
- **Build only what is asked.** The design docs describe many systems on purpose not built yet.
- **After every change that lands, update the Encyclopedia**: in `Reference/12Sky/01_Systems_Build_List.md` the
  system's status, "What is left", the glossary and a Change log line; then rebuild the page
  (`_Docs/Encyclopedia~/README.md`).

---

## 7. Checking your work

| Check | How | When |
|---|---|---|
| EditMode tests | Test Runner in the Editor, or `python "Assets/_Game/Tools/Editor/Architecture/Scripts~/run_editmode_tests.py"` (a batch Unity, results in `Logs/`) | Every change |
| The stack | `python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py" --check`: fails on a new reference up the stack, a new two-way pair, or a move that would break what an asset holds | Every change that crosses systems |
| Compiles without the Editor | `compile_check.py` beside it (`--server` compiles as the dedicated server does) | Big refactors, or with the Editor busy |
| Organisation | **Krytical Nova ▸ Project ▸ Validate Project Organization** | New folders or moved assets |
| Icons | **Krytical Nova ▸ Icons ▸ Check Icons** | New skills or items |
| Play | Play from the title in the Editor; use GM commands to reach what you changed | Every gameplay change |
| Two players | A Multiplayer Play Mode virtual player (**Window ▸ Multiplayer ▸ Multiplayer Play Mode**) joins your Editor as a client | Anything networked: host-only testing hides client bugs |
| A dedicated server | `Guides/Server-Guide.md`, "A second server for tests": build into the test folder, run on 7796 / 8096, join with `Library/KryticalNova.TestSession` (`port=7796`, `join=1`) | Server-side changes |

Remove `Library/KryticalNova.TestSession` when you are done: while it is there, the Editor's Play joins the test
server instead of hosting.

---

## 8. Making your first change

1. Branch from `main`, named by area and subject as the others are (`world/zone-build-kits`, `online/phase4-...`,
   `pets/...`); one subject per branch.
2. Find the owning system and its doc (`Design/`, `Plans/`, the system's strip on this page).
3. Make the change in that system's folder. Numbers go on its settings asset. If you need a higher system, add an
   interface in the lower one instead of a reference.
4. Add or update EditMode tests in the system's `Tests/Editor/`.
5. Run the checks of section 7 that apply, and play it.
6. Update `Reference/12Sky/01_Systems_Build_List.md` (status, glossary, Change log) and any doc whose "as built"
   section you changed.
7. Open a pull request into `main` that says what changed, how you checked it, and what you could not check.

---

## 9. Working with Claude

The owner builds much of the game with Claude (Anthropic's coding agent). `CLAUDE.md` at the repo root holds the rules
Claude follows in this project; they are the same house rules as section 6, so it is worth reading once. Plans and
docs written in those sessions are in `Plans/`, and every task updates this page.
