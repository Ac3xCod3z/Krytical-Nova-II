# Krytical Nova II

Unity 6 (6000.6) URP multiplayer (Netcode for GameObjects) project.

## Project organization (mandatory)

Read `Assets/_Game/_Docs/ProjectOrganization.md` before creating, moving, or generating any asset or script.

- All first-party content goes in `Assets/_Game/<System>/` by **owning system**, never in type buckets
  (`Scripts/`, `Prefabs/`, `Materials/`…) and never at the Assets root.
- Code goes in the system's `Runtime/`, `Editor/`, `Data/` or `Tests/` folder and must be covered by an
  asmdef/asmref. Namespace: `KryticalNova.<System>[.<Sub>]`.
- Vendor packages live untouched in `Assets/_ThirdParty/<Vendor>/`. Exceptions pinned at the root:
  `Synty/`, `DownloadCache/`, `QuickDestruction/`, `FastScriptReload/`, `Plugins/`, `Resources/`, `TextMesh Pro/`, `DefaultNetworkPrefabs.asset`.
- Editor builders that create assets must write into the owning system's folder (path constants).
- Move assets through Unity (`AssetDatabase.MoveAsset`) or together with their `.meta`; then grep for old path literals.
- Validate with menu **Krytical Nova ▸ Project ▸ Validate Project Organization**.

## Names and systems (mandatory)

Read `Assets/_Game/_Docs/Reference/12Sky/README.md` before naming anything a player sees or building a system.

- `01_Systems_Build_List.md` is the glossary (Twelve Sky 2 term → Krytical Nova term → what the code calls it → what it is for) and the build list. Use the Krytical Nova term in UI text and in new code; update a system's Status there when it lands.
- **After every task that lands, update the Encyclopedia of Nova** (owner, 15 Oct 2026): in `01_Systems_Build_List.md` the system's Status and columns, "What is left", a glossary row with a plain "What it is for" for every new term (a system says what it does, an item what using it does; nothing vague), and a line in its Change log; then run `Assets/_Game/_Docs/Encyclopedia~/build_encyclopedia.py` and publish the result to the Encyclopedia artifact (`_Docs/Encyclopedia~/README.md` has the link and the steps). A new doc under `_Docs` joins its Library by itself.
- `02_Gear_System_Spec.md` is the gear layer (ladder, stats, Tempering, Fusion, Tuning, Slotting); `04_Weapon_Classes.md` is the nine weapon classes and their skills. Keep their "as built" sections current.
- `05_Economy_And_Social.md` is saving, Creds, pickup, Vault, Trade, Kiosks, Contacts and Crews as built, with every tunable number and who may deal with whom. Keep it current; new numbers go in `EconomySettings` / `CrewSettings`, never in code.
- A new player-side system adds a row to `PlayerSystemsSetup`; a prefab the server spawns adds its path to `GameplayPrefabsSetup.SpawnedPrefabs` (unregistered prefabs never appear on clients). Test networked features with a second client, not only as host.
- A new skill, or a new item that has no model, gets its icon through the icon library (**Krytical Nova ▸ Icons ▸ Icon Library**), never by hand: every skill has an icon of its own and no icon is used twice. The one exception is the Skill Chips (owner, 11 Oct 2026): all chips of attack skills show one picture and all chips of buffs another (`ItemDefinition.sharesIconOf`; the library holds the two). **Krytical Nova ▸ Icons ▸ Check Icons** must come back clean. The look of those icons is `IconTreatmentSettings` (`Design/Rendering-Bible.md` 6g).
- `Assets/_Game/_Docs/Design/Lore-Bible.md` is the story canon (the owner's Core Lore Bible). Read it before writing any story, quest, NPC or flavour text. Its Fixed Canon list is never contradicted, rumor is kept apart from history, and the Ronin stay hidden until their expansion; `11_factions_lore.md` is how the factions look and work in the game.
- Build only what is asked for: these docs describe many systems that are deliberately not built yet.

## Saves, the save database and tests (mandatory, online phase 4)

- Postgres 18 runs on the owner's PC as a Windows service (starts with Windows). Two databases: **`krytical_nova`** holds
  the **real saves**; **`krytical_nova_test`** is for tests only. Never point a test, a measure server or an Editor
  experiment at the real one.
- The **save service** (`Assets/_Game/Gameplay/Persistence/Service~/SaveService`) is the only program that talks to the
  database. Real: port **7801**. Tests: `SaveService --test`, port **7802**, database `krytical_nova_test`. A dedicated
  server uses it only when its `server-settings.json` names it (`saveService`); otherwise it saves to files as before.
  The Editor's own Play sessions still save to files.
- Secrets live in `Documents\KryticalNova-Secrets` (`postgres-admin.txt`, `save-service.json`, `save-service-key.txt`):
  never read them aloud, print them, copy them into the repo or chat. `Setup Database.bat` (beside the service) makes the
  databases and the service's login; the owner runs it.
- **All-or-nothing changes and the kept ledger (milestone 3; owner, 18 Oct 2026: the economy will be reworked, and every
  rework must keep these):** anything that changes more than one player or world document (a trade, a Kiosk sale, a Crew
  founding, and whatever the economy becomes) saves through `ServerSave.SaveTogether` in one change, never with separate
  saves. Every Cred is tallied by `PlayerWallet` itself (a new source or sink is a new `CredsSource` / `CredsSink`, never
  a side door); every item movement of an economy action writes its line through `EconomyLedger` (kinds in
  `LedgerKinds`). The save service stores ledger lines as text with only account, kind and time: it must never learn the
  economy's rules, so a rework needs no database change. Test with `TransactionLedgerTests` and the kill test
  (`Persistence/Service~/KillTest/kill_test.py`, test database only).
- The owner runs the server with the **Krytical Nova Server app** (`Assets/_Game/Networking/ServerApp~`, built beside the
  server by `ServerBuildBuilder`): it starts the save service and the server hidden inside one window. Stopping the
  server never stops the save service or the database; closing the app stops the save service it started. Test the app
  only beside the measure server (`Builds/Bots`, `--selftest <folder>` pictures its tabs), never on the real server. Details: `_Docs/Guides/Server-Guide.md`,
  "The save database and save service"; the plan is `_Docs/Plans/online-phase-04-accounts-and-shared-save.md`.
