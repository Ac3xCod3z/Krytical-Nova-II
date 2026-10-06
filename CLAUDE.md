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

- `01_Systems_Build_List.md` is the glossary (Twelve Sky 2 term → Krytical Nova term → what the code calls it) and the build list. Use the Krytical Nova term in UI text and in new code; update a system's Status there when it lands.
- `02_Gear_System_Spec.md` is the gear layer (ladder, stats, Tempering, Fusion, Tuning, Slotting); `04_Weapon_Classes.md` is the nine weapon classes and their skills. Keep their "as built" sections current.
- `05_Economy_And_Social.md` is saving, Creds, pickup, Vault, Trade, Kiosks, Contacts and Crews as built, with every tunable number and who may deal with whom. Keep it current; new numbers go in `EconomySettings` / `CrewSettings`, never in code.
- A new player-side system adds a row to `PlayerSystemsSetup`; a prefab the server spawns adds its path to `GameplayPrefabsSetup.SpawnedPrefabs` (unregistered prefabs never appear on clients). Test networked features with a second client, not only as host.
- Build only what is asked for: these docs describe many systems that are deliberately not built yet.
