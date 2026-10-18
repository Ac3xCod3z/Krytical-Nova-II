# Project Cleanup & Restore

Why it exists (owner, 18 Oct 2026): the project carried about 23 GB of vendor files the game never uses, and the
Editor's memory and every refresh, import and search paid for them. The cleanup takes out only vendor files that
nothing uses, and anything taken out can be brought back one asset at a time.

## What is kept, always

A vendor file is removed only when **three independent checks** all say nothing uses it:

1. **Maintainer** (Code Stage Maintainer's Project Cleaner, `Assets/Plugins/CodeStage/Maintainer`), counting as used:
   every scene in the build list, all of `Assets/_Game` (every zone, scene, kit, prefab and data file, laid out or not),
   every `Resources` and `StreamingAssets` folder, the project settings and the last build.
2. **Unity's own dependencies** (`AssetDatabase.GetDependencies`) from every file outside the vendor folders, the build
   scenes, and what `ProjectSettings` and the embedded packages point at.
3. **Written references**: every GUID written in those files' YAML, JSON and `.meta`, followed file by file
   (a prefab to its material, the material to its texture), without asking Unity.

Also kept, whatever the checks say:

- Files our editor builders load **by path or by name** (`"Assets/Synty/…"` in code, an interpolated part matching any
  folder; any vendor file whose name appears in quotes in `_Game` code or JSON), with everything they depend on.
- `Assets/Synty/SidekickCharacters` whole (parts load from Resources by path), and the `Prefabs`, `Sprites` and `Fonts`
  of every Interface pack (the HUD, faction and plate builders read them by path).
- Code, shaders and assembly files, and anything in a vendor `Editor`, `Plugins`, `Resources` or `StreamingAssets`
  folder. These go only with a whole package, never one by one.
- Anything **no package on this PC holds**, so nothing is removed without a way back other than git.

The cleanup may only touch `Assets/Synty`, `Assets/_ThirdParty`, `Assets/QuickDestruction/Demo` and
`Assets/TextMesh Pro/Examples & Extras`. `Assets/_Recovery` is listed but has no package source, so it is kept.

After a removal, the **after-check** reads every file still in the project for references to removed GUIDs; anything it
finds is restored at once. Then the project is built and played (Title, Zone01, a Run) before the branch is merged.

## Bringing something back

Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**.

- **Catalog**: every Synty and `_ThirdParty` asset the project has had, with its status (*In project* or *Removed* and
  when). Search a name ("lamp", "SM_Prop_Barrel"), pick it, **Restore**.
- **Removed**: only what the cleanup took out.
- **Packages**: everything in the packages on this PC, imported or not.

A restore copies in **that asset and what it needs that the project lacks** (materials, textures, meshes, sub-prefabs,
their folders), each with its original `.meta`, so it has its old GUID and everything that pointed at it finds it again.
It never imports the whole pack and never overwrites a file already in the project (the project's upgraded shared
materials stay as they are). Tick several rows to restore them together.

The packages come from `Documents/SyntyPolygonForKN` (the Synty library) and Unity's Asset Store cache
(`%AppData%/Unity/Asset Store-5.x`). **Build Package Index** reads them once (a few minutes) and again only for packages
that changed. The index lives in `Library/KryticalNova.AssetRestore`.

Without the tool: each Removal Log batch names the last commit that still has its files, so
`git checkout <commit> -- "<path>" "<path>.meta"` brings one back too.

## The files

| File | What it is |
|---|---|
| `Removal-Log.md` | One section per batch: what went, why, how much, from which packages, the commit that still has it. |
| `Lists~/Removed-<date>-<pack>.md` | The batch's files one per line, by pack and folder, with where each comes back from. |
| `Lists~/Removed-Assets.tsv` | The same as a table (read by the Removed tab). |
| `Lists~/Asset-Catalog.tsv` | The Asset Catalog: one row per vendor asset, removed rows kept. |
| `Lists~/Asset-Catalog.html` | The catalog as a page to search outside Unity. |

`Lists~` ends in a tilde, so Unity does not import it (no cost to the Editor) while git keeps it.

## Menus

Krytical Nova ▸ Project ▸ Cleanup:

- **Build Package Index**: reads the packages on this PC.
- **Take Catalog Snapshot**: writes the Asset Catalog from what is on disk now.
- **Scan For Unused Vendor Assets (report only)**: the three checks; the report is `Library/KryticalNova.Cleanup/Scan-Summary.md`. Nothing is removed.
- **Remove Unused Vendor Assets…**: scans afresh and removes what passed, Removal Log first, files to the Recycle Bin, then the after-check.
- **Check Nothing Points At A Removed File**: the after-check on its own.

Code: `Tools/Editor/AssetRestore` (index, restore, catalog, log, window) and `Tools/Editor/Cleanup` (the scan and the two
extra checks, on top of Maintainer).

## Later

Unity Asset Manager (cloud) was looked at and not used for this: the packs are already on this PC for free, and its docs
do not say an asset comes back with its old GUID. Owner, 18 Oct 2026: "asset manager might be used later when we go
about adding our own assets, but for now we do it our own way".
