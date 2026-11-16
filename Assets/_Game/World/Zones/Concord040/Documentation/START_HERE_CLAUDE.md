# Concord 040 — Unity import handoff

Import `Concord_040_Unity6_URP.unitypackage` into Krytical Nova II using Assets > Import Package > Custom Package. This is an environment art package from the approved checkpoint040, not a replacement for the game's working zone or gameplay systems. The actual game project was read for version/pipeline settings only and was not edited.

## Place the environment

- Target tested Editor: Unity6000.6.3f1; Universal Render Pipeline17.6.0; linear color space. URP is an existing game dependency and is not embedded into this package.
- Main prefab: `Assets/Concord/Prefabs/Concord_040.prefab`.
- Place at world origin, rotation(0,0,0), scale(1,1,1). Units are metres. Unity+Y is up; +Z is north/Accord Hall; -Z is south/arrival; -X is west/market; +X is east/workshops. The playable platform spans approximately180x180m, with hull details projecting beyond its edges. Typical deck surface is y0.2; Hall terrace is y1.1; east tiers are y7.25 and12.7.
- `Assets/Concord/Scenes/Concord_Preview.unity` is an isolated review scene with its own camera/light. Open it separately to inspect the package. The environment prefab itself contains no global lighting/camera or project-setting changes.
- `Assets/Concord/PreviewSettings` contains optional review URP assets. Keep the game's existing pipeline and sky. Importing the package does not automatically replace them.
- The package uses native Unity Mesh assets and URP materials. No Blender installation, GLB importer, runtime loader, or third-party package is required beyond URP.

## Current owner decisions

- Accord Hall has a larger shell and a50mwide outdoor entrance terrace, about6.2m deep, reached by a26mwide stair. Keep that platform between the stairs and glazing. Do not restore the old ramp.
- Small Hall doors, handles and low frames are removed. Two full-height glass leaves are under `Accord Hall Sliding Glass - unanimated`, named `DoorLeft` and `DoorRight`. Their transform origins are at their own bounds centres, so they can slide along local/worldX. Add your proximity, obstruction, networking and collision behavior separately; none is supplied here. Preserve the broad glass appearance.
- Freestanding west street canopies and the freestanding upper workshop-court shelter were removed at the owner's request. Keep awnings attached to buildings. Ten tree-planter/bench pockets line the two streets. Do not restore the rejected freestanding row.
- Preserve the continuous east middle floor and three east levels. Vendor locations and travel relationships are authoritative; do not invent new services or portals.
- North Accord Hall; northwest Vaultkeeper; west Broker/kiosks; southwest Apothecary; east Fabricator/Fixer/Armorer; northeast Herald; southeast Holoweaver/Store Clerk; south shared arrival/return portal and separate Wayfinder, Marshal, Deep Vault services.

## Integration work still required

- Keep current Zone01 and existing working scenes until the user explicitly approves replacement. Create a separate integration scene or branch of the game's scene workflow.
- Bind the game's existing vendor/portal components and authoritative spawn points. This package contains scenery, not vendor NPCs, inventories, travel logic or netcode.
- MeshColliders cover a conservative subset of authored architectural surfaces (floors, decks, stairs, walls and columns), not every prop, rail or glass surface. Validate player capsule traversal, stair handling, falling boundaries, roofs and all arrival positions; refine/simplify collision before shipping. No NavMesh is baked.
- The large sliding Hall leaves have no blocking collider or automation. Assign these deliberately with the eventual door system. Other glazing also needs gameplay-specific collision decisions.
- No NPC crowds, finished shop interiors, doors animation, baked lighting, occlusion bake, LODs or production performance certification are included.
- Opaque geometry is grouped by material/spatial chunk for practical import, with glass separated and the Hall leaves individually addressable. The detailed editable source remains the Blender master; arbitrary native props are not preserved as individually editable Unity prefabs.

## Visual translation

Blender geometry, world-aligned floor UVs, source textures, normal maps and approximate PBR/emission values are carried over. Roughness textures are converted into Unity metallic/smoothness maps. Blender physical transmission is approximated with URP transparent glass. Area-light washes are not recreated as hundreds of realtime lights; use the game's lighting and bake or author a lighting pass. The preview will differ from the Cycles images. `BlenderLightingReference.json` records the original lights for reference.

`VALIDATION.json` records actual Unity serialization, geometry counts, bounds and Hall terrace collider support checks. `Unity_Preview.png` is rendered by the isolated Unity staging project. `SourceObjectManifest.json` records source-object names and owned Synty provenance. Owned asset content is included for this owner's game integration, not as a standalone redistributable asset pack.

## Source and reproducibility

Master: `C:/Users/dlaco/Documents/ChatGPT/Krytical Nova/Worldbuilding/Concord/Concord_040_FullHeightHallGlass.blend`.

The delivery folder's `StagingProject` and `BuildData` are reproducibility/debug files, not files to copy over the game project. The staging manifest uses local cached package paths on this PC. Import the `.unitypackage`, not that manifest or the staging ProjectSettings. Build scripts are retained beside the master (`export_unity_040.py`, `prepare_unity_delivery.py`) and in `StagingProject/Assets/Editor/ConcordPackageBuilder.cs`.

Private visual history: https://ac3nsi0n.taile2bc25.ts.net/concord/index.html ; interactive Blender-derived review: https://ac3nsi0n.taile2bc25.ts.net/concord/review-3d.html .
