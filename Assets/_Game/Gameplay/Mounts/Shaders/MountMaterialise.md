# MountMaterialise shader graph (spec for the Editor step)

The one shared appear/vanish shader for every mount (owner, 6 Nov 2026: Torrent's timing, our own look; per mount
"the same but a bit different"). It is built in Shader Graph from AllIn1ShaderNodes sub-graphs
(`Assets/Plugins/AllIn1ShaderNodes/Editor/Nodes/SG/`). The graph and its material live in this folder, never in Plugins.
`MountMaterialiser` (Runtime) swaps a copy of the material onto every mount material while the mount forms or
vanishes and drives the properties below. Each mount's `MountMaterialiseSettings` asset supplies the values.

Files to make here: `MountMaterialise.shadergraph` and `MountMaterialise.mat` (the material every
`MountMaterialiseSettings.material` points at).

## Graph settings
URP Lit, Opaque, **Alpha Clipping on** (threshold 0.5). No Transparent surface, so the mount never sorts against its
rider. Cast Shadows on, so the shadow dissolves with the body.

## Properties (exact reference names; the component sets them by name)
| Reference | Type | Set from |
|---|---|---|
| `_BaseMap`, `_BaseColor`, `_BumpMap`, `_BumpScale`, `_EmissionMap`, `_EmissionColor`, `_MetallicGlossMap`, `_OcclusionMap`, `_Smoothness`, `_Metallic` | as URP Lit | copied from the mount's own material, so the forming body looks like the mount |
| `_Spawn` | Float 0-1 | the tween: 0 = gone, 1 = solid |
| `_EdgeColorA` | Color HDR | burning edge colour |
| `_EdgeColorB` | Color HDR | rim colour while see-through |
| `_BurnWidth` | Float | edge width (about 0.08) |
| `_RimStrength` | Float | rim multiplier |
| `_FadeTex` | Texture2D | per-mount noise or pattern (circuit traces, leaf veins) |
| `_FadeTiling` | Vector2 | its tiling |
| `_PatternMode` | Float | 0 none, 1 scanlines (Hologram), 2 glitch (Hologram + Glitch vertex) |
| `_PatternUntil` | Float | pattern shows only while `_Spawn` < this |
| `_DirectionWeight` | Float 0-1 | how much the sweep leads the noise |
| `_Inflate` | Float (m) | surface puff while forming |
| `_SweepOrigin` | Vector3 (world) | where the sweep starts (the model's bottom, or front), set every frame |
| `_SweepAxis` | Vector3 (world, unit) | the direction the body forms along |
| `_SweepLength` | Float (m) | the model's length along that axis |

## Wiring
1. **Sweep:** `sweep = saturate(dot(WorldPosition - _SweepOrigin, _SweepAxis) / _SweepLength)` (0 at the feet, 1 at
   the top). This replaces Height Gradient, which cannot follow a moving, scaled mount.
2. **Mask:** `mask = lerp(noise, sweep, _DirectionWeight)`, where `noise` = `_FadeTex` sampled at UV × `_FadeTiling`.
3. **Fade - Burn** (Alpha Effects): Fade Amount = `1 - _Spawn` remapped so the mask runs from fully gone to fully
   solid, Fade Tex = the mask, Burn Width = `_BurnWidth`, Burn Color = `_EdgeColorA`. Its alpha goes to Alpha (clip
   0.5). Its burn colour goes to Emission.
4. **Rim** (Color Effects): Rim Color = `_EdgeColorB`. Into Emission × `(1 - _Spawn)` × `_RimStrength`.
5. **Glow** (Color Effects): the burn edge plus the rim into Emission (Bloom is on in the URP volume).
6. **Pattern:** when `_PatternMode` ≥ 1 and `_Spawn` < `_PatternUntil`, add **Hologram** scanlines (about 20 %) into
   Emission. When `_PatternMode` = 2, also add the **Glitch** vertex offset scaled by `(1 - _Spawn)`.
7. **Swell:** **Vertex Inflate** with Inflate Blend = `_Inflate` × `saturate((0.2 - _Spawn) / 0.2)` (a puff only as
   it starts forming).
8. Base Color, Normal, Metallic, Smoothness and Occlusion come from the copied URP Lit properties. Emission = the
   copied emission plus the effect terms above.

Reference demos: `Demo/Graphs/GRPH_Demo_FadeBurn.shadergraph` (Fade - Burn + Rim) and
`GRPH_Demo_ChomperHologram.shadergraph` (Hologram + Glitch + Rim).

## Timing (Mount Settings, owner picks 6 Nov 2026)
Get on 1.0 s, control at 0.8 s. The mount forms from 0.12 s over 0.65 s (solid as the rider lands at 0.77 s). Get off
in 0.65 s; the vanish starts at 0.1 s and lasts 0.5 s. A mount's own asset may override the two durations.
