# _ThirdParty — "We didn't make this."

Vendor packages (Asset Store, .unitypackage) live here, one folder per vendor/package, **unmodified**.

- Don't reorganize a vendor's internal folders and don't put our files inside them. It breaks updates.
- New packages land here automatically (`Krytical Nova/Project/Auto-Quarantine Imported Packages`).
- Need to change vendor behavior? Wrap or extend it from `_Game/<System>/`, or copy the asset into `_Game` and own the copy.
- Exceptions that must stay at the Assets root because they hardcode their paths: `Synty/`, `DownloadCache/`.

Rules: `Assets/_Game/_Docs/ProjectOrganization.md`
