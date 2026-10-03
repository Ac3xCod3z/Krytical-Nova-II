# Krytical Nova II — Rendering Bible

The single source of truth for how the game is lit, post-processed, and kept fast. **Update this file in the same
commit as any rendering change** (pipeline assets, renderer features, sky/post shaders, lighting setup, volume
profiles). Add a line to the [Changelog](#changelog) every time.

Look target: **comic-book stylized realism**. Ink outlines and halftone shadows on a believable, time-of-day lit world.
The sky is painterly but physically motivated (real rainbow geometry, real sunset colours, real moon phases).

---

## 1. Pipeline per platform

| | PC (`Assets/_Game/Rendering/Settings/PC_RPAsset.asset`) | Mobile (`Mobile_RPAsset.asset`) |
|---|---|---|
| Rendering path | **Forward+** (`PC_Renderer`) | Forward (`Mobile_Renderer`) |
| Depth / opaque texture | on / on | off / off |
| HDR | on | on |
| MSAA / render scale | off / 1.0 | off / 0.8 |
| Light probes | **Adaptive Probe Volumes** | legacy light probes |
| Renderer features | SSAO, ComicStyle, SunShafts, Volumetric Fog 2, Volumetric Lights, Beautify | none |
| Main light shadows | 4096, 4 cascades, 50 m | 1024, 1 cascade |

**PC-only effects** (they need the depth texture): comic outlines and shadows, SSAO, sun shafts, volumetric mist,
Beautify. Every new effect must
degrade cleanly on Mobile, either by skipping its pass or with a cheaper fallback. The sky shader has a `_KN_SKY_LOW`
keyword for Mobile.

Why Forward+: per-tile light culling, so dozens of hand-placed realtime lights don't hit the 8-lights-per-object limit.

## 2. Frame order (PC)

1. Depth/normals prepass → opaques (ComicToonLit, Synty)
2. **SSAO** (DepthNormals, intensity 0.4, radius 0.3)
3. **Skybox**: `Skybox/KryticalNova/Stylized Sky`. Sky gradient, stars, sun/moon art, clouds, rainbow.
4. Transparents (weather particles, VFX)
5. **ComicStyle** (BeforeRenderingPostProcessing): cast-shadow restyle, cloud shadows, Sobel outlines. Skips sky pixels.
4b. **Volumetric Fog & Mist 2** (half-res raymarch + 1 blur pass), driven by `VolumetricAtmosphereDriver`:
   - `VF_GroundMist` — terrain-fitted ground fog that **only appears in fog weather** (`WeatherProfile.volumetricFog`:
     Fog 1, Blizzard 0.5, everything else 0; any Rain profile always reads 0 — air rains *or* fogs).
     It samples the sun's shadow map, so buildings cut shafts through it.
   - `VF_WaterMist` — low "Water Mist" volumes over each lake and along the rivers, building at dusk from 17:45 and thinning slowly until 22:00
     on dry evenings.
   - Volumes at ~0 are switched off entirely (no cost in clear or rainy weather). Both sample APV, so baked lamp
     light tints the mist at night.
   **Volumetric Lights**: every lit street lamp has a cone (`StreetLampController`), on dusk→dawn, faint in clear air
   and stronger only in fog weather (`VolumetricAtmosphereDriver.FogAmount`; local water mist doesn't count), distance-toggled at 45–65 m.
6. **SunShafts** (BeforeRenderingPostProcessing + 1): sky / cloud god rays, added before post so Bloom catches them.
   Kept alongside the mist: the shafts own the sky, the mist owns the streets.
7. **Beautify 3** (AfterRenderingTransparents — i.e. *before* URP post, on the HDR frame) owns **tonemapping
   (PBR Neutral)**, **bloom** (threshold 1, intensity 0.4, spread 0.58, warm `#FFF1E0` tint) and the **ink vignette**
   (rings 0.1 → 0.5, ink `#14122B` at 60 %, **fade 0** — Beautify's "fade" fades the whole frame to the
   vignette colour, it is not edge softness), plus depth-aware sharpen (1.6), vibrance (+0.35), contrast 1.03, a faint
   warm anamorphic streak and dither 0.005. Its grain, outline, sun flare, eye adaptation and depth of field stay off.
8. URP post: Tonemapping **None**, Bloom/Vignette **off** (Beautify has them) → **Split Toning / Lift Gamma Gain** →
   **Color Adjustments** (runtime) → **Chromatic Aberration** (0 at rest; GameFeel pulses it on heavy hits) →
   **Film Grain** → **SMAA** → **Lens Flare** (data-driven, on the sun light). The GameFeel hurt pulse drives Beautify's
   vignette (`BeautifyVignettePulse` on the Global Volume, runtime profile only).

## 3. Lighting rules

- **One realtime directional light.** It is the sun by day and the moon by night (`EnvironmentPresenter` swaps it, and
  intensity crosses 0 at the swap). Never add a second shadowed directional light.
- **Hand-placed lights bake.** Point/spot/area lights for lamps, windows and signage go under the scene's `Lighting` root
  and are set to **Baked**. Their direct and bounce light is stored in **Adaptive Probe Volumes** (no lightmaps to
  manage). Static environment has *Contribute GI* and *Receive GI = Light Probes*.
  - Setup for a new zone: **Krytical Nova → Lighting → Setup Baked GI (APV) For Open Scene**, then
    **Krytical Nova → Lighting → Bake Lighting**.
  - Per-zone assets live next to the scene: `LS_<Scene>.lighting`, `APV_<Scene>.asset`.
- **Ambient follows the day cycle.** The presenter builds the ambient SH probe from the day-cycle palette ~10×/s. APV
  **Sky Occlusion** bakes only *how much sky each probe sees*, and sky light is re-evaluated at runtime from that ambient
  probe. So baking never freezes the time of day.
- **Baked lights are always on.** If lamps read badly at noon, add APV lighting scenarios (Day / Night) and blend them
  with the clock (see Roadmap).
- **SSAO** stays subtle (0.4). The comic shadow pass does the heavy lifting.
- **Reflections**: no realtime reflection probes. Environment reflection intensity stays low (0.3), since the look is
  matte and inked.
- **God rays from props**: hand-placed beams through windows and alleys use the Synty `FX_SunBeam_Large_01` /
  `FX_SunBeam_Small_01` (PolygonParticleFX) and `LightRay_*` prefabs. The **sun's** rays are the screen-space SunShafts pass.

## 4. HDR & Bloom budget

Bloom threshold is **1.0** (intensity 0.25, scatter 0.5). Only these may exceed 1.0:
- sun glare core (`_SunGlareHDR`) and the sun's cloud rims (`_BodyRimGlow`)
- sun shafts at dawn and dusk
- lightning flashes
- emissive VFX that should glow

Everything else (UI-like colours, moon art, rainbow, sky gradient) stays ≤ 1 so Bloom doesn't smear the comic look.

## 5. Sky & celestial

Driven by `EnvironmentPresenter` (`Assets/_Game/World/Runtime/Presentation/`). Tuning lives on `DayCycleProfile`
(`DC_Default`), `WeatherProfile` (`WP_*`), and the sky material `M_StylizedSky`.

### Shader globals (`_KN_` prefix, set every frame)
| Global | Meaning |
|---|---|
| `_KN_SunDir`, `_KN_MoonDir` | directions toward the bodies |
| `_KN_SunColor`, `_KN_MoonColor` | colour × visibility |
| `_KN_SunTex`, `_KN_MoonTex` | stylized art (`T_Sun`, `T_Moon`, 839×925, no power-of-two rescale) |
| `_KN_BodyTexParams` | x/y tan(art radius), z/w art on |
| `_KN_BodyTexBrightness` | x sun, y moon brightness; **z weather diffusion** |
| `_KN_BodyTexAspect` | art width/height (keeps non-square art undistorted) |
| `_KN_MoonPhase` | x phase (0 new, 0.5 full), y lit fraction |
| `_KN_Rainbow` | x strength |
| `_KN_Halo` | x sun 22° halo strength, y moon halo strength |
| `_KN_SunShimmer` | x heat shimmer multiplier (1 = clear midday) |
| `_KN_Cloud*` | cloud field, shared with ComicPost and SunShafts (`SkyCommon.hlsl`) |
| `_KN_WindVector` | xy wind (m/s, world XZ, gust included), z speed. Set by `PrecipitationController` |
| `_KN_WindParams` | x gust now, y gustiness, z strength 0-1 (speed / 20 m/s). Drives foliage and water |
| `_KN_WindScroll` | integrated wind drift (texture tiles) for water ripples; continuous as the wind turns |
| `_KN_Interactors[8]`, `_KN_InteractorCount` | xyz feet, w radius of the 8 `FoliageInteractor`s nearest the camera (grass parting) |

### Sun
- **Art treatment**: the centre is washed out by its own light (`_SunGlare*`), light bleeds over ink lines
  (`_BodyInkLift`), colour and contrast are softened, and the art hazes into the sky near the horizon.
- **Spikes**: barely visible (`_SunRayVisibility` 0.3), with a heat shimmer from the cloud noise
  (`_SunShimmerAmount/Speed/Scale`, base 0.03) and a slow flicker. The glow wobbles too, by `_SunShimmerGlow` (30%)
  of the spike amount. The disc core never wobbles.
- **Shimmer strength**: `_KN_SunShimmer` from the presenter (`shimmerStrength`): full on a clear midday sky, about
  half at a low sun, close to 0 under cloud.
- **No flare on the sun**: `LF_ComicSun` only has ghosts along the flare line. A flare element at position 0 reads
  as a star stamped over the sun art, so don't add one.
- **Sunset**: art graded to deep red → orange → warm (`_SunsetGrade`), flattened vertically up to 15% like real
  refraction (`_SunsetFlatten`), below `_SunsetHeight`.
- **Size**: `sunTextureSize` 0.065 rad on `DC_Default`. Lower `sunTilt` to raise the noon sun; this also shortens shadows.

### Weather diffusion (`WeatherProfile.celestialDiffusion`)
0 = crisp art, 1 = only a pale glow through the cloud deck. Clouds then also carry a soft deck glow.

| Clear | FewClouds | PartlyCloudy | MostlyCloudy | Overcast | Fog | LightRain | Rain | Thunderstorm | Snow | Blizzard |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 0 | 0.15 | 0.5 | 0.9 | 0.85 | 0.8 | 0.95 | 1 | 0.85 | 1 |

### Moon phases
- A shader mask over the unchanged `T_Moon` art. The moon disc in the art is `_MoonDisc` (centre 0.495, 0.5; radius
  0.23 × 0.209 uv). The dark side keeps `_MoonEarthshine`.
- Phase = (world day count + time of day) / `lunarCycleDays` (8). It's derived from the replicated world clock, so all
  clients agree. Waxing lights the right side.
- Moonlight intensity and the moon halo scale with the lit fraction.
- `time.set` resets the day count, so the phase jumps. That's acceptable.

### Rainbow (physically motivated)
Appears only when **all** hold:
1. **Recent rain**: wet air lingers `rainbowLingerSeconds` (150 s) after rain stops.
2. **Clearing sky**: no rain falling, and diffusion < ~0.4.
3. **Low sun**: above the horizon and below **42°**.

The bow is drawn around the **antisolar point**:
- Primary at 40.6–42.3°, red outside.
- Secondary at 50–53°, reversed colours, 35% strength.
- **Alexander's dark band** between them, and a slightly brighter sky inside the primary.

Peak strength is `rainbowPeak` 0.25, so it stays subtle. Preview it with `previewRainbow` on the presenter.

### 22° halo (rare)
- A soft ring 22° from the sun (or moon): a faint red inner edge washing out to white, and slightly darker sky inside.
  Added with a soft blend after the clouds, so it shows through cirrus, fades under thick cloud, and never clips.
- **When**: each world day rolls `haloChancePerDay` (6%) for a `haloWindowHours` (2.5 h) window between 08:00 and
  16:00. Each night rolls `moonHaloChancePerNight` (4%) between 20:00 and 04:00 and needs a moon over half lit.
  The roll is a hash of the day count, so all clients agree without network traffic.
- **Weather**: needs thin high cloud (cirrus ≥ ~0.15: Clear, FewClouds, PartlyCloudy), no rain or snow, and low diffusion.
- Preview with `previewHalo` on the presenter. Shader tuning: `_HaloIntensity`, `_HaloWidth`, `_HaloMoon`.

### GM sky commands
`sky.rainbow`, `sky.moonphase`, `sky.moon.next`, `sky.halo`, `sky.shimmer`, `sky.flare`, `sky.lightning`, `sky.status`,
`sky.reset` (`Assets/_Game/Tools/Debug/SkyCommands.cs`, and the World tab of the GM Tools window). These are local
presentation overrides only.

### Cloud-edge glow
Clouds crossing the sun or moon get a bright rim (`_BodyRimGlow`), HDR for the sun.

### Lens flare
- `LF_ComicSun` (built by **Krytical Nova → World → Build Comic Lens Flare**): starburst, halftone discs, inked ring and
  flat hexes, all additive and low intensity.
- Sun only, with depth occlusion (buildings hide it). The presenter fades it with `sunFlareIntensity` (day curve),
  diffusion, and a CPU sample of the cloud field in front of the sun (needs Read/Write on `T_CloudNoise`).

### Sun shafts
- `SunShaftsFeature` + `Hidden/KryticalNova/SunShafts`:
  1. Quarter-res sky mask (buildings and clouds block it).
  2. Long radial blur, then a short one.
  3. Additive composite tinted by the sun colour.
- Strength comes from `SunShaftRuntime.Intensity`, which the presenter sets from `sunShaftIntensity` (peaks at dawn and
  dusk), sun visibility, and diffusion.
- Off at night, and fades when the sun leaves the screen.
- `comicBands` posterizes the shafts (off by default, since it draws hard rings). `debugShaftsOnly` shows only the shaft
  buffer.
- **Render Graph gotcha**: material values set *inside* a pass's render function apply when the command buffer runs, so
  per-pass values must be baked into separate shader passes or set before recording.

## 6. Post volume rules

- Scene volume: **Global Volume** → `SampleSceneProfile`, written by **Krytical Nova → Lighting → Apply Post Processing
  Look** (`Assets/_Game/Rendering/Editor/PostProcessSetup.cs`; also runs in Build Zone01):

  | Override | Value | Why |
  |---|---|---|
  | Tonemapping | Neutral | keeps the palette's hues (ACES shifts them) |
  | Bloom | threshold 1, intensity 0.36, scatter 0.58, tint `#FFF1E0` | only the HDR budget glows, warm |
  | Split Toning | shadows `#6E74A4`, highlights `#B8A68A`, balance 0 | cool shadows, warm paper highlights |
  | Lift Gamma Gain | lift slightly blue, gain slightly warm | darks lean to ink-indigo, not dead black |
  | Vignette | 0.28, smoothness 0.45, colour Ink `#14122B` | comic-panel framing |
  | Film Grain | Thin1, 0.2 | printed-page feel under the halftone shadows |
  | Motion Blur | off | |

- Main camera anti-aliasing: **SMAA High**. FXAA smears the ink outlines; TAA ghosts the halftone dots.
- No depth of field: a far blur would also blur the sky (sun art, clouds). This includes Beautify's DoF.
- Beautify lives in the same profile (`Beautify` override, written by `PostProcessSetup.ApplyBeautify`).
- **Never write the profile asset at runtime.** `EnvironmentPresenter` writes Color Adjustments (saturation, contrast,
  colour filter) on `postVolume.profile`, the runtime instance. FEEL's vignette / chromatic-aberration shakers on the
  Global Volume also work on that runtime instance.
- Volumetric mist density and tint follow the weather through `VolumetricAtmosphereDriver` (on Environment Presenter),
  which edits the fog's instanced profile (`VolumetricFog.settings`), never `VF_GroundMist.asset`.
- Renderer features re-publish their serialized settings every frame. Modulate them only through their runtime classes:
  `ComicStyleRuntime` (shadow tint, opacity, dots) and `SunShaftRuntime` (shaft strength).
- New screen-space features: Render Graph only (`RecordRenderGraph`). Skip Preview and Reflection cameras. Read
  `cameraDepthTexture` via `ConfigureInput`.

## 6b. Colour bible

Code: `Assets/_Game/Rendering/Editor/ColorBible.cs` (**Krytical Nova → World → Apply Colour Bible**, also run by Build Zone01).

| Role | Colour | Used for |
|---|---|---|
| Ink | `#14122B` | outlines, vignette, deepest night fog |
| Shadow indigo | `#4C52BF` | comic shadow tint |
| Sky blue / horizon | `#1F6ECC` / `#ADE0F2` | day sky |
| Neon cyan | `#4FE3F0` | tech accents, exhaust glow, portal |
| Neon magenta | `#E04FB8` | signage accent |
| Signal yellow | `#FFC93C` | road paint, warnings |
| Sunset coral | `#FF9E66` | dawn / dusk light |
| Concrete / ruin concrete | `#8C8A94` / `#4A4752` | city neutral / demolished cut faces |

**Fog ramp** (`DC_Default.fogColor`): night `#141A3D` → pre-dawn violet `#2E2A5C` → dawn coral `#E8A08A` → day comic blue
`#8FC3E8` → dusk rose `#E07A8C` → twilight violet `#5B3F7A`. Distance fades toward the sky's hue, never grey. Weather fog
**Weather looks** mute the palette but never grey it out. Fog tints are chromatic blue-lavenders with a low pull
(overcast `#9EA8C8` at 35%, fog `#B4BEDC` at 40%, rain `#7C88B0` at 50%, storm `#5C6490` at 55%), so dawn and dusk colours
still show through. Saturation drops stay small (fog −12, rain −15, storm −18; it was −30 to −45). Clear skies run fog
density 0.0022 so distance reads as air (about 35% fog at 300 m).

**Building palette**: no two buildings within 18 m share a material (`StarterTownBuilder.ApplyPalette`).
- Shops and large buildings use 12 of Synty's 24 `PolygonScifi_0X_Y` atlas variants: warm and cool neutrals, teal,
  indigo slate, dusty rose, ochre, sage, plum, brick, sandstone, and a rare orange. Lime, near-black and murky olive
  are excluded.
- Background towers use the 3 Synty window sets × 6 facade tints: steel, teal, lavender, sand, rose and clean
  (`Assets/_Game/Environment/Buildings/Materials/BuildingPalette`).

**Sky bodies** (`DC_Default`): sun art 0.05 rad, moon art 0.075 rad, procedural discs 0.027 / 0.023. They read
farther away than the original 0.065 / 0.1.

## 6c. City layout and dressing (Zone01, `StarterTownBuilder.*`)

- **Streets** follow the Synty demo: 10 m two-lane roads with 5 m kerbed sidewalks on the lines 0, ±35, ±65 and ±100. A
  curated segment table varies the grid:
  - The ±65 ring survives only as short road and alley pieces, so the outer bands form big lots.
  - Some segments become pedestrian market alleys.
  - Four blocks have a special use: park, parking, raised deck and construction.
  - A flood fill checks that the plaza and all gates stay connected.
- **Street types:**
  - Main (gate avenues and the plaza ring): lattice gantries (`Pillar_02` and `Beams_01`) with hanging signs, plus
    cables strung facade to facade.
  - Side: power poles with cables running pole to pole.
  - Wall road: barriers and crates.
- **Buildings:**
  - Modular shopfronts are `Section_*` modules: 1-3 bays, 1-2 deep and 1-6 floors, with awnings, wall signs, blade
    neon, posters, AC units and roof clutter.
  - Tower stacks are built from `Large_Part_*`.
  - Whole prefabs get a 0.85-1.25 Y stretch on the outer bands.
  - **Variety rule:** the same prefab or stack can't repeat within 20 m, and side-by-side neighbours on a street row
    must differ in height by at least 25%.
- **Signage:**
  - Billboards are found by raycasting the building's own colliders and mounted 0.15 m off the wall. About 75% face the
    plaza, and they're dropped if no wall is found. Nothing floats.
  - Art orientation: `Billboard_Large` shows its art on -Z, `Sign_0x` on +Z, and Neon and `Sign_Ad` read from both
    sides.
- **Trash corners:** bins and dumpsters with rubbish in Synty's triplanar dirt material (`M_Grime_Light/Medium/Heavy`),
  in back lots, alleys and along sidewalks.
- **Stalls:** a shared grounded frame (back wall, a canopy stretched to 5 m, counter, crates, lanterns hung from the
  measured canopy). Each stall has its own sign style: neon panel, wooden board, hologram plate, fabric banner, blade
  sign or lightbox. Fonts are Orbitron, Exo 2 and Lato.
  - **Wall-only props** (`Overhang_0x`, `Wall_Table`, `Flags`) are never used on open ground.
  - **Prop placement:** props are snapped by their measured bounds, so each one sits on the ground, on the counter, or
    hangs from the canopy.

## 6d. Vegetation, wind and water

- **Wind is part of every weather.** Each `WeatherProfile` has a `windSpeedMin`/`windSpeedMax` range and a
  `calmChance`. The server rolls a speed per weather cycle (biased low, so strong days are rarer) and replicates it
  next to the heading. A slow shared lull/surge noise (±25%) and the gust noise ride on top. GM: `weather.windset
  <m/s> [heading]`, `weather.windclear`, `weather.wind`.
- **Grass is GPU grass** (`GrassRenderer`, `Assets/_Game/Environment/Vegetation/Grass/Runtime`, `Assets/_Game/Environment/Vegetation/Grass/Rendering`). Data lives in
  `Assets/_Game/Environment/Vegetation/Grass/Data/Assets`:
  - `GrassType` sets the mesh, height and width ranges, root/mid/tip colours, fake AO, wind and player bend.
  - `GrassBiome` is a weighted mix of types, with patchy stands.
  - `GrassRuleSet` holds the placement curves and the LOD settings.
  - **Placement is baked into `T_GrassMask_Zone01`:** R = density, G = biome, B = moisture. Density is
    ground splat × slope curve × altitude curve × a 1.5 m fade from roads, sidewalks, buildings and props × an
    optional artist mask. `GrassExclusionVolume` adds runtime no-grass zones with soft edges.
  - **Each frame, per camera,** `GrassGenerate.compute` walks a world-snapped jittered grid (0.25 m) within 60 m.
    It picks a type per biome, applies distance LOD (full to 15 m, 45% to 35 m, 15% to 60 m; kept clumps widen,
    and clumps shrink out before the edge) and frustum-culls. One `RenderMeshIndirect` is issued per type. No
    instance data is stored.
  - **`KryticalNova/Grass Blades`** draws the blades:
    - Layered wind: field waves, local gust cells and blade flutter, with roots planted.
    - `_KN_Interactors` push the grass aside.
    - Colour is a root→tip gradient with per-clump, patch and wetness variation and fake AO.
    - Lighting is the comic cel band and dots on upward-biased normals.
    - It casts no shadows but receives them.
  - **Far grass (smoke and mirrors):** a second pass of the same compute shader, with the same mask, biomes,
    types, colours and wind, places 6-triangle tufts on a 0.9 m grid out to 170 m. Real clumps thin out and the
    tufts thin in across the 42-60 m hand-off band, so grass gets more detailed as you approach and never ends in
    a line. All draws read regions of one shared instance buffer. The compute shader counts into a raw buffer,
    which is GPU-copied to the indirect args (D3D11 can't make args buffers raw).
  - **Meadow types:** Synty tall grass A/B (0.5-1.3 m), the same meshes as Medium (0.35-0.6 m) and Short
    (0.18-0.34 m), plus procedural blade clumps, each growing in its own patchy stands.
  - **Lawn mask:** inside the walls only paving, floors and buildings block grass; benches, planters, lamps and
    bins don't. The lawn edge falloff is 0.2 m and the mask is 2048² (0.49 m). The town lawn biome is a short,
    wide-footprint mowed cut.
  - The terrain's grass layer is tinted (`TL_Grass` remap) so the ground past 60 m reads as the same grass.
  - **Rebuild with Krytical Nova > Zone > Grass > Build GPU Grass**, or *Bake Grass Mask* after town or terrain
    edits. The same menu has debug views. In game: `grass.debug 0-3`, `grass.density`, `grass.distance`,
    `grass.toggle`, `grass.stats`.
  - Terrain details now carry only the flowers.
- **`KryticalNova/Foliage Wind`** uses the same cel lighting and dots as Comic Toon Lit.
  - All motion happens in the vertex stage with no texture reads. Every pass (lit, shadow, depth, depth-normals)
    bends the same way, so outlines stay attached.
  - Grass mode bends from the base and parts around interactors. Tree mode keeps the trunk still while the canopy
    sways and branch ends flutter.
  - Motion fades out past `_WindFadeDistance`. Grass casts no shadows.
  - Trees, bushes, hedges and town grass patches use it through `FoliageAssetBuilder`. The shared Synty atlas
    materials are untouched.
- **Leaves:** `WindLeavesController` runs 2 pooled `FX_Leaves_Green_01` emitters, only above 6 m/s, capped at 4/s and
  40 particles each.
- **Water** is `KryticalNova/Anime Water`, a hand-edited copy of the KumaBeer URP 6.1 shader.
  - It covers one generated mesh (`Zone01_WaterMesh`, 4 m quads) only where the terrain dips under the waterline.
  - A baked flow map (`T_WaterFlow_Zone01`: RG direction, B speed) makes rivers run from their first point to their
    last. The current dies out inside lakes, which keep the calm ripples.
  - Wind adds a ripple layer.
  - The old flat `Water` object keeps the collider and NavMesh modifiers with its renderer off.
  - Rebuild with **Krytical Nova > Zone > Rebuild Water**.
  - The water needs the depth and opaque textures, which are now on for the PC and Mobile URP assets.

## 7. Performance budgets (PC, 1080p)

| Item | Budget | Notes |
|---|---|---|
| Sky shader | < 0.15 ms GPU | 4 texture reads in clouds, + 3 for the bodies |
| ComicStyle pass | < 0.6 ms | full-res Sobel + shadow-map reads |
| SunShafts | < 0.25 ms | quarter res, 1 + 24 + 24 samples; skipped when strength is 0 or the sun is off-screen |
| SSAO | < 0.5 ms | |
| Lens flare | ~0.05 ms | 6 elements, depth occlusion |
| Environment CPU | < 0.1 ms, 0 B GC | presenter, flare cloud sample |
| Volumetric mist + Beautify | < 2 ms together | mist at half res, 0.6 m min step, 300 m max distance; mist volumes off when unused |
| Lamp cones | < 0.5 ms at night in fog | Faster raymarch preset, no shadows, off beyond 65 m, off by day |
| Live minimap | ~8 renders/s, 512² | opaque-only Forward+ renderer, no shadows/post/features |

Measure with the Frame Debugger and Profiler (GPU module) in a build, not the Editor. Look for each feature's
`ProfilingSampler` name ("Comic Style", "Sun Shafts").

## 8. Roadmap

- **APV lighting scenarios** (Day/Night): turn baked lamps off at noon and on at night, blended by the clock.
- **Dynamic GI**: evaluated 2026-10-02 (the "Fast Dynamic GI" asset, `_ThirdParty/GI`) against APV and **rejected**.
  An A/B in Zone01 roughly doubled frame cost (+2–5 ms at street and vista views) and added no visible bounce, because
  its SDF tracing needs Read/Write enabled on every Synty mesh (memory cost, importer churn). APV + sky occlusion stays.
  Revisit only as an optional Ultra tier.
- **Interior lights** can reuse the street-lamp pattern: Baked light (APV Night scenario) + optional VolumetricLight.
- **Screen-space reflections**: URP has none built in, so it would be a custom Render Graph pass. Wet streets after rain
  would pair with the rainbow and moisture state.
- **Auto-exposure / eye adaptation** for interiors.
- **Shadowmask mixed lighting** for interiors once indoor zones exist: crisp baked shadows far away, realtime up close.

## Changelog

- **2026-10-02 (night fix)**:
  - Night "haze" was Beautify's vignette fade (0.45) mixing 45 % ink into every pixel; fade is now 0.
  - Ground fog only in Fog / Blizzard weather (Overcast 0); dusk water mist lifts slowly until 22:00.
  - Local player gets a soft shadowless night fill light (`PlayerNightLight`, dusk → dawn).
  - Paving tiles have mesh colliders: NavMesh, clicks, foot IK and ground rings sit on the visible surface
    (`GroundSnap` places markers and dust on it).

- **2026-10-02 (pass 2)**:
  - Beautify now owns tonemapping, bloom and vignette (URP's off); corrected the frame order (Beautify runs before URP post).
  - Fog follows the weather: ground fog only in fog weather, never with rain; dusk water mist over lakes and rivers.
  - Lit street-lamp prefab variant (Baked spot + VolumetricLight cone) everywhere an intact street light stands.
  - **APV lighting scenarios "Day" / "Night"**: baked with the lamps off / on (Krytical Nova ▸ Lighting ▸ Bake Day +
    Night Scenarios); EnvironmentPresenter blends Day→Night with the sun, so lamp light fades in at dusk.
  - Gatehouses (POLYGON demo layout) with direction nameplates on all four gates; paved approaches.
  - Live minimap (Minimap_Renderer, Forward+ so GPU Resident Drawer stays on; fog off for its render only).

- **2026-10-02 (atmosphere, destruction, game feel)**:
  - Volumetric Fog & Mist 2 ground mist (terrain-fitted, shadowed sun scattering, weather-driven density/tint).
  - Volumetric Lights feature registered for future lamp volumetrics.
  - Beautify 3 after post (sharpen, vibrance, anamorphic streak); duplicate effects left off.
  - Dynamic GI A/B: rejected, APV stays (see Roadmap).
  - Ruins re-baked with Quick Destruction: Damaged / Collapsed / Rubble tiers settled by an edit-mode physics pass,
    breached wall spans and broken mid-wall towers, a siege band of ruins just outside the wall.
  - GameFeel: FEEL camera impulses, FOV kick, hurt vignette, chromatic punch, target squash/flash, POLYGON FX dust and
    sparks (URP copies).

- **2026-10-02 (GPU grass)**:
  - Compute-generated, indirect-drawn stylized grass with a baked placement mask, biomes, distance LOD, debug views
    and GM commands.
  - Lawns use a short mowed cut.
  - Terrain grass layer tinted to match.
- **2026-10-02 (nature)**:
  - Per-cycle wind speed ranges for all weather.
  - Wind-reactive grass, flowers, bushes and trees, with grass parting around players.
  - Dense small grass as terrain details.
  - Sparse windy-day leaves.
  - KumaBeer anime water with river flow.

- **2026-10-02 (city)**:
  - Streets rebuilt to the demo standard (10 m roads, gantries, power lines, alleys, special blocks).
  - Modular shopfronts and tower stacks; variety rule for repeats and heights.
  - Colour families with strong tower tints.
  - Raycast-mounted facade billboards.
  - Finished stalls with distinct name signs; triplanar trash corners.
- **2026-10-02**:
  - Post look: Split Toning, Lift Gamma Gain, Film Grain, an ink vignette and a warm bloom tint; SMAA on the main camera.
  - Colour bible documented; fog ramp and weather fog tints retuned to it.
  - Sun and moon read farther away (smaller).
  - Building palette: neighbouring buildings never share a colour.
  - URP exhaust trails and booster glows on the flying traffic (Synty legacy particle shaders converted).
  - Ruin library: fractured towers in the outer ring (`Assets/_Game/Environment/Ruins/Prefabs`); replaced on
    2026-10-02 (atmosphere) by the Quick Destruction library below.

- **2026-10-01**:
  - Stylized sun/moon art with glare, ink lift and haze. Heat-shimmering, mostly hidden sun spikes.
  - Weather diffusion, sunset grading and flattening, moon phases, cloud rim glow.
  - Physically placed rainbow after rain.
  - SunShafts feature, comic lens flare.
  - APV baked GI for hand-placed lights with sky occlusion.
  - Cirrus streaks turned off (`_CirrusStrength` 0).
- **2026-10-01 (GI)**:
  - Zone01 APV baked at 3 m minimum probe spacing (1 m produced ~650 MB; 3 m ≈ 65 MB, 22 MB of it editor-only support data).
  - PC APV memory budget raised to 2048.
  - APV bake data is tracked with Git LFS (`APV_*.bytes`).
  - Verified that ambient still follows the day cycle with APV on (sky occlusion).
