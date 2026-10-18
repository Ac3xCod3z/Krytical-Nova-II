# Removed 2026-10-18: PNB_Core

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

24 files, 7.4 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## PNB_Core (24 files, 7.4 MB)

Get it back from: POLYGON_Fantasy_Village.unitypackage

**Assets/Synty/PNB_Core/**

- PPP_Base_01.asset · Asset · 23 KB

**Assets/Synty/PNB_Core/Materials/**

- Birds_02.mat · Material · 4 KB
- SoftParticle.mat · Material · 3 KB
- Synty_Clouds_Tropical.mat · Material · 2 KB

**Assets/Synty/PNB_Core/Models/**

- Env_CloudRing_Larger_02.fbx · Model · 818 KB
- SM_Env_Water_Plane_02.fbx · Model · 245 KB

**Assets/Synty/PNB_Core/Shaders/**

- Shader Graphs_New Shader Graph.mat · Material · 1 KB
- Shader Graphs_noiseTest.mat · Material · 1 KB
- Synty_Fabric.mat · Material · 4 KB
- Synty_Refractive_Transparent.mat · Material · 4 KB

**Assets/Synty/PNB_Core/Textures/**

- BaseGrass_normals.png · Texture · 261 KB
- Bird_Test_01.png · Texture · 97 KB
- Bird_Test_03.png · Texture · 168 KB
- Circle_Soft_Tex_01.png · Texture · 35 KB
- Gradient 3.png · Texture · 148 KB
- Gradient 4.png · Texture · 922 KB
- Gradient_Biolumin_01.png · Texture · 335 KB
- Noise_Big.tga · Texture · 1.0 MB
- Noise_Big_02.tga · Texture · 1.0 MB
- Noise_Mid.tga · Texture · 1.0 MB
- Noise_Wave.tga · Texture · 1.0 MB
- WaterNormals_02.png · Texture · 225 KB
- Water_Texture_02 1.png · Texture · 128 KB
- White_01.png · Texture · 17 KB
