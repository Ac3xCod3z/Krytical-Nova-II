# Removed 2026-10-18: KumaBeer

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

7 files, 2.3 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## KumaBeer (7 files, 2.3 MB)

Get it back from: Stylized anime water.unitypackage

**Assets/_ThirdParty/KumaBeer/Stylized anime water/Demo/**

- Demo.unity · Scene · 45 KB
- DemoSettings.lighting · Other · 1 KB
- Demo_night.unity · Scene · 40 KB
- Postprocess.asset · Asset · 25 KB
- Skybox_night.mat · Material · 2 KB

**Assets/_ThirdParty/KumaBeer/Stylized anime water/Materials/**

- anime water_night.mat · Material · 5 KB

**Assets/_ThirdParty/KumaBeer/Stylized anime water/Models/**

- waterplane.fbx · Model · 2.1 MB
