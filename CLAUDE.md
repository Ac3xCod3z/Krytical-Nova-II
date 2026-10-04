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
