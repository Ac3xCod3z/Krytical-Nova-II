# _ThirdParty — "We didn't make this."

Vendor packages (Asset Store, .unitypackage) live here, one folder per vendor/package, **unmodified**.

- Don't reorganize a vendor's internal folders and don't put our files inside them. It breaks updates.
- New packages land here automatically (`Krytical Nova/Project/Auto-Quarantine Imported Packages`).
- Need to change vendor behavior? Wrap or extend it from `_Game/<System>/`, or copy the asset into `_Game` and own the copy.
- Exceptions that must stay at the Assets root because they hardcode their paths: `Synty/`, `DownloadCache/`.
- **Our files inside a vendor folder (owner's exceptions).** A vendor update may remove them; put them back:
  - `GI/Scripts/GI.EditorOnly.asmdef` keeps GI out of player builds (6 Oct 2026).
  - `Emerald AI/Scripts/EmeraldAI.asmdef`, `Emerald AI/Scripts/Internal/Editor/EmeraldAI.Editor.asmdef` and an
    `EmeraldAI.Editor.asmref` in each of Emerald's other twelve `Editor` folders (9 Oct 2026). Emerald ships no
    assembly definition, and the game's assemblies cannot call code that has none. No Emerald script is edited.
    Only `KryticalNova.Hostiles.Emerald` and the editor tools reference them.

Rules: `Assets/_Game/_Docs/ProjectOrganization.md`
