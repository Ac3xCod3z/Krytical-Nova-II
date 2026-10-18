# Removed 2026-10-18: smear effect

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

54 files, 17.6 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## smear effect (54 files, 17.6 MB)

Get it back from: Smear Effect Tool.unitypackage

**Assets/_ThirdParty/smear effect/animator/**

- Chomper.controller · Animator · 4 KB
- Ellen_0.controller · Animator · 1 KB
- Ellen_1.controller · Animator · 25 KB
- Grenadier.controller · Animator · 5 KB
- Spitter.controller · Animator · 3 KB

**Assets/_ThirdParty/smear effect/audio/**

- btn.wav · Audio · 35 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/**

- Demo_built-in.unity · Scene · 192 KB
- ppv.asset · Asset · 2 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/**

- Ground_built-in.mat · Material · 3 KB
- wall_built-in.mat · Material · 3 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/Chomper/**

- Chomper_built-in.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/Ellen/**

- body_built-in.mat · Material · 5 KB
- eyes_built-in.mat · Material · 5 KB
- hair_built-in.mat · Material · 5 KB
- head_built-in.mat · Material · 5 KB
- tear_built-in.mat · Material · 4 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/Grenadier/**

- core_built-in.mat · Material · 5 KB
- Eyes_built-in.mat · Material · 5 KB
- Grenadier_built-in.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/Sphere/**

- ball.physicMaterial · Physics material · 324 B
- Sphere_built-in.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/Demo_built-in/material_built-in/Spitter/**

- Spitter_built-in.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/**

- Demo_hdrp.unity · Scene · 205 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/material_HDRP/Chomper/**

- Chomper_hdrp.mat · Material · 7 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/material_HDRP/Ellen/**

- body_hdrp.mat · Material · 7 KB
- eyes_hdrp.mat · Material · 6 KB
- hair_hdrp.mat · Material · 6 KB
- head_hdrp.mat · Material · 6 KB
- tear_hdrp.mat · Material · 6 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/material_HDRP/Grenadier/**

- core_hdrp.mat · Material · 6 KB
- Eyes_hdrp.mat · Material · 6 KB
- Grenadier_hdrp.mat · Material · 6 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/material_HDRP/Sphere/**

- ball.physicMaterial · Physics material · 324 B
- Sphere_hdrp.mat · Material · 6 KB

**Assets/_ThirdParty/smear effect/Demo_hdrp/material_HDRP/Spitter/**

- Spitter_hdrp.mat · Material · 6 KB

**Assets/_ThirdParty/smear effect/Demo_urp/**

- Demo_URP.unity · Scene · 195 KB
- ppv.asset · Asset · 2 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/**

- Ground_urp.mat · Material · 4 KB
- wall_urp.mat · Material · 4 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/Chomper/**

- Chomper_urp.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/Ellen/**

- body_urp.mat · Material · 5 KB
- eyes_urp.mat · Material · 4 KB
- hair_urp.mat · Material · 4 KB
- head_urp.mat · Material · 4 KB
- tear_urp.mat · Material · 4 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/Grenadier/**

- core_urp.mat · Material · 4 KB
- Eyes_urp.mat · Material · 4 KB
- Grenadier_urp.mat · Material · 4 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/Sphere/**

- ball.physicMaterial · Physics material · 324 B
- Sphere_urp.mat · Material · 4 KB

**Assets/_ThirdParty/smear effect/Demo_urp/material_urp/Spitter/**

- Spitter_urp.mat · Material · 5 KB

**Assets/_ThirdParty/smear effect/textures/**

- Chomper_Albedo.png · Texture · 3.1 MB
- Grenadier_Albedo.png · Texture · 11.9 MB
- Spitter_Albedo.png · Texture · 1.8 MB
