# _Plugins — integration layers for external SDKs/services

Our glue code for third-party services (platform SDKs, analytics, external backends, networking services):
`_Plugins/<Area>/` e.g. `_Plugins/Analytics/`, `_Plugins/Platform/`.

- The SDK itself stays untouched in `_ThirdParty/` (or `Packages/` via UPM).
- `Assets/Plugins/` (no underscore) is Unity's reserved folder for native libraries and vendor DLLs. Leave it alone.

Rules: `Assets/_Game/_Docs/ProjectOrganization.md`
