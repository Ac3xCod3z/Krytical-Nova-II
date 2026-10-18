# Removed 2026-10-18: MegaBook

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

6 files, 16.6 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## MegaBook (6 files, 16.6 MB)

Get it back from: MegaBook.unitypackage

**Assets/_ThirdParty/MegaBook/Scenes/Objects/**

- bookspine.FBX · Model · 16 KB

**Assets/_ThirdParty/MegaBook/Scenes/Objects/desk1.fbm/**

- beech desktop.jpg · Texture · 453 KB
- desktop specular.bmp · Texture · 8.0 MB
- megafiers cup.jpg · Texture · 155 KB
- pencil.jpg · Texture · 37 KB

**Assets/_ThirdParty/MegaBook/Scenes/Textures/**

- desktop specular.bmp · Texture · 8.0 MB
