# Removed 2026-10-18: PolygonParticleFX

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

16 files, 754 KB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## PolygonParticleFX (16 files, 754 KB)

Get it back from: POLYGON_Particle_FX.unitypackage

**Assets/Synty/PolygonParticleFX/Materials/**

- FX_01.mat · Material · 3 KB
- FX_Cloud_01.mat · Material · 1 KB
- FX_Emissive_Fire_01.mat · Material · 3 KB
- FX_Rock_02.mat · Material · 3 KB
- FX_Smoke_01.mat · Material · 4 KB
- PolygonParticleFX_Rock_02.mat · Material · 3 KB

**Assets/Synty/PolygonParticleFX/Materials/Alts/**

- PolygonParticles_Material_01_B.mat · Material · 3 KB
- PolygonParticles_Material_01_C.mat · Material · 3 KB

**Assets/Synty/PolygonParticleFX/Models/**

- FX_Bullet_Trail_01.fbx · Model · 21 KB
- FX_Cloud_Placement_01.fbx · Model · 24 KB
- FX_Sphere_Spikes_02.fbx · Model · 30 KB

**Assets/Synty/PolygonParticleFX/Scenes/**

- Demo.unity · Scene · 583 KB
- DemoSettings.lighting · Other · 1 KB
- Particle_Demo_Volume.asset · Asset · 3 KB
- Pivot_Spin.anim · Animation · 53 KB

**Assets/Synty/PolygonParticleFX/Textures/**

- PolygonParticles_Circle_02.png · Texture · 9 KB
