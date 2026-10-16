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
| Depth / opaque texture | on / on | on / on (in the asset; this table said off until 15 Oct 2026) |
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
It is also the right path for a phone later (Deferred costs tiled mobile GPUs too much bandwidth), so it stays.

### Graphics tiers: author for the top, step down (owner, 15 Oct 2026)

Make the game as pretty and atmospheric as it can be on a top GPU, then work backwards: each lower preset removes or
cheapens effects, and still has to look like a great game. A **PC build always runs the "PC" quality level**; the
"Mobile" level and asset are only for phones, so a check for "Mobile" never fires on PC. The presets are
`GraphicsPreferences` (rows) applied by `GraphicsSettingsApplier` onto the live `PC_RPAsset`.

| Preset | Render scale | Shadows (distance / map / cascades) | AA | Grass | Fog & mist | Sun shafts | Outlines |
|---|---|---|---|---|---|---|---|
| Low | 0.7 | 22 m / 1024 / 1 | off | 0.4 | off | off | on |
| Medium | 0.85 | 36 m / 2048 / 2 | FXAA | 0.7 | on | off | on |
| High (as authored) | 1.0 | 50 m / 4096 / 4 | SMAA | 1.0 | on | on | on |
| Ultra | 1.0 | 80 m / 4096 / 4 | SMAA | 1.0 | on | on | on |

**Not yet tied to the preset** (the pre-server rendering pass, section 8): volumetric fog quality (raymarch quality,
step size, resolution), the lamp cones (they ignore the fog toggle and run at full resolution), cascade split ratios,
soft-shadow quality. Measured so far: 2048 / 2 cascades instead of 4096 / 4 saves 0.8–1.0 ms (section 7).

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
   - **Night haze**: on dry nights `VF_GroundMist` also runs at a small share of its density
     (`DayCycleProfile.nightHaze`, 0.12), stronger under a bright moon, so moonlight has air to scatter in and
     buildings cut soft shafts through it. It never counts toward `FogAmount`.
   - Volumes at ~0 are switched off entirely (no cost in clear or rainy daytime weather). Both sample APV, so baked
     lamp light tints the mist at night.
   **Volumetric Lights**: every lit street lamp has a cone (`StreetLampController`), on dusk→dawn, faint in clear air
   and stronger only in fog weather (`VolumetricAtmosphereDriver.FogAmount`; local water mist doesn't count), distance-toggled at 45–65 m.
6. **SunShafts** (BeforeRenderingPostProcessing + 1): sky / cloud god rays from the key light (sun by day, faint
   moon rays at night), added before post so Bloom catches them.
   Kept alongside the mist: the shafts own the sky, the mist owns the streets.
6b. **Glow Halo** (BeforeRenderingPostProcessing + 2): the light round tempered weapons and evolved pets (section 6e).
   Laid over the inked, toned frame, so the comic ink and dots stay under it, it is never outlined or dotted itself,
   and it does not lean on bloom to read. Adds no pass at all when nothing on screen glows.
6c. **Comic lettering** (BeforeRenderingPostProcessing + 3): the marks and words of a hit and every other comic effect
   (section 6h), which are on the `ComicLettering` layer. They are laid over the finished frame the way lettering is
   laid over a drawn page: not inked, dotted or lit over, still hidden by what really stands in front of them (they
   test the scene's depth), and under URP's own post like everything else. The renderer's transparent pass leaves the
   layer out (`PostProcessSetup.EnsureComicLettering`), so nothing is drawn twice. They are drawn after Beautify, so
   their colours are the Colour Bible's own and do not bloom.
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
- **Baked lights follow the day.** The APV bake has **Day** and **Night** scenarios, blended by the sun's height
  (`EnvironmentPresenter.BlendLightingScenarios`), so baked lamps are dark at noon and lit at night.
- **Shadows.** Only the sun or moon casts realtime shadows; the 72 street lamps are baked spots with no shadows; the
  player's night light and housing lights are realtime and shadowless. URP 17.6 has no per-light cached or on-demand
  shadow maps (that is an HDRP feature), so baking is our way of making a stationary light free.
- **Realtime spell lights (none exist yet)** must be: shadowless, short range (a few metres), capped per frame, and
  culled by distance with a `CullingGroup` rather than per-light `Update` checks. Write that cap before the first one.
- **SSAO** stays subtle (0.4). The comic shadow pass does the heavy lifting.
- **Reflections**: no realtime reflection probes. Environment reflection intensity stays low (0.3), since the look is
  matte and inked.
- **God rays from props**: hand-placed beams through windows and alleys use the Synty `FX_SunBeam_Large_01` /
  `FX_SunBeam_Small_01` (PolygonParticleFX) and `LightRay_*` prefabs. The **sun's** rays are the screen-space SunShafts pass.

## 3b. Volumetric light, fog and mist (as built, checked 15 Oct 2026)

Five sources, cheapest first:
1. **Unity distance fog**, exponential squared, density from the weather profile (Clear 0.0022, Fog 0.014,
   Blizzard 0.02) via `EnvironmentPresenter`.
2. **Horizon fog in the sky shader** (`_KN_SkyParams.w`).
3. **Volumetric Fog & Mist 2** (Kronnect, v31.4.4, `_ThirdParty/VolumetricFog2`): one `VolumetricFogManager` at
   half resolution with edge-aware blur; 20 volumes (one terrain-fitted ground mist `VF_GroundMist`: raymarch quality
   6, min step 0.6, 14 m high, noise and turbulence, max distance 300 m, receives sun shadows to 120 m; 19 water-mist
   boxes `VF_WaterMist`). Unused features we own: **fog voids** (keep mist out of interiors), fog point lights,
   sub-volumes, distant fog.
4. **Volumetric Lights** (Kronnect, v20.2.1, `_ThirdParty/VolumetricLights`): the cone on each of the 72 street lamps,
   "Faster" raymarch preset, auto-toggle 45–65 m, no occlusion shadows, **full resolution** (downscaling 1).
5. **SunShafts** (ours): screen-space god rays at quarter-resolution mask; the shafts own the sky, the mist the streets.

Rules: volumetrics render below full resolution with an edge-aware upscale; raymarch steps stay capped; mist is a
height-bounded volume with noise, never a full-screen 3D voxel field; a local effect gets a local box volume; interiors
get a fog void; every volumetric switches off with distance or at zero weight. Lower presets cheapen these first.

**Research checked 15 Oct 2026** (owner's research file in `Documents/Game Docs`):

| Advice | Here | Verdict |
|---|---|---|
| Forward+ rather than Deferred | Forward+ | Already done |
| Half-res volumetrics, bilateral upscale | Fog yes; lamp cones full res | Do for the cones |
| Raymarch step caps per tier | Capped, but not per preset | Do |
| Height fog with noise for mist; local fog volumes | Terrain-fit mist, 19 water boxes | Already done |
| 2 vs 4 cascades, distance per preset | Yes; split ratios fixed | Add splits per preset |
| Cached / on-demand shadows for stationary lights | Not in URP; lamps baked | Not applicable |
| Distance culling of local lights / CullingGroup | Cone auto-toggle; no realtime local lights | Needed when spell lights arrive |
| APV with Day/Night blending; time-of-day shader globals | Both built (about 28 `_KN_*` globals) | Already done |
| Screen-space shadows, shadowmask | Not used | Little gain today; shadowmask for interiors later |

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
| `_KN_WindPhase` | x = wind strength summed over time, y = the same one frame ago, w = 1 once published. What a foliage wave's speed-up with the wind is multiplied by (`KNWindPhase()`) |
| `_KN_WindDrift` | xy = how far the grass's gust cells have drifted downwind (`KNWindDrift()`), zw = one frame ago |
| `_KN_Interactors[8]`, `_KN_InteractorCount` | xyz feet, w radius of the 8 `FoliageInteractor`s nearest the camera (bushes and town grass patches part round them; GPU grass only without a trample map) |
| `_KN_TrampleSources[32]`, `_KN_TrampleSourceCount` | xyz feet, w radius of up to 32 `FoliageInteractor`s near the camera: who presses the grass's trample map |
| `_KN_GrassTrample`, `_KN_GrassTrampleRect` | the trample map (rg push, b press, a height of who pressed) and where it lies: xy corner in world XZ, z 1 / width in metres, w 1 while there is a map |

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
  0.23 × 0.209 uv). The dark side keeps only `_MoonEarthshine` (0.015) of the art and covers the sky behind it at
  `_MoonDarkAlpha` (0.95), so it reads as a shadow that hides the stars; the moon halo is masked off it.
- Phase = (world day count + time of day) / `lunarCycleDays` (8). It's derived from the replicated world clock, so all
  clients agree. Waxing lights the right side.
- Moonlight scales with the lit fraction through `DayCycleProfile.moonPhaseLight` (new moon 0.42 → full 1) on top
  of `moonIntensity` (0.75 at midnight). A bright, unclouded moon also lifts the night ambient
  (`moonAmbientBoost`, up to +60%), drives faint moon shafts (`moonShaftIntensity`) and thickens the night haze.
  The moon halo scales with the lit fraction too. Still one directional light: the moon is the key light at night.
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
- At night the same pass follows the moon: `SunShaftRuntime.SourceDirection` / `Tint` switch with the key light and
  the strength comes from `moonShaftIntensity` × lit fraction × clear sky (peak 0.3, so it stays under the bloom
  threshold). Fades when the source leaves the screen.
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
- **A wave's phase is never time × the wind's strength** (rule of 10 Oct 2026). Strength changes every frame with
  the gusts and the time is large, so `sin(t * (a + b * strength))` threw the whole field to another point of its
  wave on every change: 80 s into a session the large grass wave jumped by up to 0.6 rad in a frame, half an hour in
  by up to 16 rad on nearly every frame, which is the stutter the owner saw. A wave is written
  `sin(a * t + b * KNWindPhase())`: `PrecipitationController` sums the strength over time (and the gust cells' travel,
  `KNWindDrift()`), so a gust only changes how fast a wave goes on from where it is. The sums wrap at 20π, so `b`
  must be a multiple of 0.1. Grass, trees and bushes all follow it (`KNWind.hlsl` says so at the declaration).
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
  - **Only the ground the camera sees is walked** (10 Oct 2026). `GrassRenderer.ViewFootprint` takes the camera and
    the four corners of its view at the pass's range, and each pass walks that rectangle of its grid instead of the
    whole square round the camera: at the game's camera angle about 158,000 cells a frame instead of 500,000, with
    the same grass drawn to the triangle. Measured on an RTX 5070 Ti the frame's GPU time did not change by more
    than the noise (grass as a whole costs about 1 ms there in the thickest meadow, nearly all of it drawing), so
    this is headroom for weaker cards, not a gain proven on one. `grass.view` switches it off to compare;
    `grass.stats` prints the cells walked.
  - **`KryticalNova/Grass Blades`** draws the blades:
    - Layered wind: field waves, local gust cells and blade flutter, with roots planted.
    - Characters bend it through the **trample map** (10 Oct 2026, `GrassTrampleMap` + `GrassTrample.compute`): a
      256² picture of 64 m of ground, one texel per 25 cm, lying from 16 m behind the game camera to 48 m ahead of
      it. Every frame it moves with the camera in whole texels, what it holds fades (to about a third in `trampleSpringSeconds`, 1.5 s) and every
      `FoliageInteractor` near the camera, up to 32, presses its mark in again; the hardest press on a texel wins.
      A blade reads one texel: which way it is pushed and how far it is pressed down. So any number of players,
      NPCs and Hostiles bend grass at the cost of one texture read a vertex, and it stays down for a moment where
      someone walked. A mark carries the height of who made it, and a blade more than 1.5 m above or below is left
      alone (a bridge over a lawn). The numbers are on `GrassRuleSet` (Trample map). Outside Play, and with
      `trample` off, a blade asks the 8 nearest `_KN_Interactors` instead, as it used to. Fill tufts take no part
      (`playerBend` 0), and grass past the map's edge does not bend.
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
    `grass.toggle`, `grass.fill`, `grass.view`, `grass.stats`.
  - **Two kinds of grass, and where each goes** (rule of 10 Oct 2026). Both are the same system and the same
    shader; what differs is the biome (`GrassBiome`), its types (`GrassType`) and how the mask is baked. A new
    zone takes these two as they are, and a third kind is a new biome, never a change to one of these.

    | | **Overworld** (`GB_Meadow`) | **Town lawn** (`GB_Lawn`) |
    |---|---|---|
    | Where | Everything outside the walls: past `lawnRegionHalfSize` (112 m) from the zone's origin | Inside the walls, on ground painted as lawn |
    | Reads as | Wild: stands of different grasses, uneven height, gaps | Kept: one short even cut, edge to edge |
    | Types | Five, each in its own patches (patchiness 0.2-0.6, patches 8-14 m): tall grass A / B 0.5-1.3 m, medium 0.35-0.6 m, short 0.18-0.34 m, blade clumps 0.28-0.7 m | One: `GT_LawnBlades`, 0.13-0.15 m, wide clumps (5.5-7 x), no patches |
    | Edge | Fades out over 1.5 m from roads, buildings and props; thins on slopes and with altitude | Stops in a straight line: the kept lawn rectangles (`lawnRects`, written by the mask bake) are filled completely, roots stay 4 cm inside, blades are clamped to the rectangle, and only paving, floors and buildings block it |
    | Fill tufts | None (`fillDensity` 0): the gaps are part of the look | Half density (`fillDensity` 0.5) within 35 m, so no ground shows through |
    | Wind | Full (`windStrength` 1-1.15, flutter 0.35-0.45) | Light (`windStrength` 0.6, flutter 0.25): a lawn shivers, it does not roll |
    | Underfoot | Parts and lies down (`playerBend` 1.1-1.2 of blades up to 1.3 m): a walker leaves a path | Same setting on a 14 cm blade: a few centimetres, read as a footfall, not a path |
    | Far tufts | Yes, 42-170 m | Yes: without them a lawn would go bare past 60 m |

    A lawn is never made wilder by mixing meadow types into `GB_Lawn`, and the meadow is never tidied by raising
    its density until the gaps close: the difference between the two is how a player knows they have left town.
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

## 6d-2. Upscalers: DLSS and FSR 2 (10 Oct 2026)

Settings ▸ Graphics has an **Upscaler** (Off / DLSS / FSR 2) and an **Upscaler quality** (Native AA, Quality, Balanced,
Performance, Ultra Performance). An upscaler draws the game smaller and rebuilds the full picture from several
frames. DLSS is offered only on an NVIDIA RTX card; FSR 2 runs on any card and is what a saved DLSS choice becomes on
a PC that cannot run it. Neither is part of a quality preset. While one is on it sets the render scale and the
pipeline switches on the temporal anti-aliasing it needs, so the Render scale and Anti-aliasing options wait.

- **How it is switched on in the project** (**Krytical Nova ▸ Lighting ▸ Set Up Upscalers (DLSS, FSR 2)**, safe to run
  again): the render pipeline has both behind its upscaler framework, which is compiled in only with the scripting
  define `ENABLE_UPSCALER_FRAMEWORK` (Standalone) and the engine modules `com.unity.modules.nvidia` and
  `com.unity.modules.amd` in `Packages/manifest.json`. Unity does not document that define for URP: it is an
  opt-in of this version (6000.6, URP 17.6), so check it first after an engine upgrade. Each upscaler keeps an
  options asset inside `PC_RPAsset`; `GraphicsSettingsApplier` changes those at run time and puts them back when the
  session ends, like every other value it touches on that asset.
- **DLSS is the 4.5 generation**: this Editor ships `nvngx_dlss.dll` 310.5.3. Each mode runs on a model, NVIDIA's own
  pairing: its second transformer model on the two modes that gain from it (Preset M for Performance, Preset L for
  Ultra Performance) and the DLSS 4 model (Preset K) for Native AA, Quality and Balanced. On RTX 20 and 30 cards the
  4.5 models cost more than they give, so those run K throughout (`GraphicsSettingsApplier.OlderRtx`).
- **Anything that moves by its shader must say so.** An upscaler follows each pixel from frame to frame by its
  motion vector; a blade that sways in the vertex shader has none unless the shader writes one. `GrassBlades.shader`
  has a `MotionVectors` pass (the same vertex maths run for last frame's wind sums) and `GrassRenderer` draws with
  per-object motion: with it, grass in wind under DLSS Performance keeps its blades; without it they smear. Trees and
  bushes (`FoliageWind.shader`) do not have one yet and rely on the camera's motion alone.
- **Known cost to the look:** the comic dots and the ink outlines are drawn before the upscale, at the smaller size,
  so at Performance and below the dots grow and soften. Not tuned yet.
- **Not measured yet:** the gain. On the owner's PC (RTX 5070 Ti) at the Editor's Game view size the game is held
  back by the main thread, not the graphics card, so no mode changes the frame rate there.

## 6e. Glow halo (tempered weapons, evolved pets)

The glow is light **round** a thing, never a tint on it. It is drawn on the screen, not on the model
(`GlowHaloFeature`, shader `Hidden/KryticalNova/GlowHalo`, both in `Assets/_Game/Rendering`), so it follows any
outline exactly (thin blades, hollow shapes, hard-edged low-poly meshes) and falls away evenly. Before 7 Oct 2026 it
was a hull pushed out along the mesh normals in four stacked bands, which is why it had hard steps, gaps at hard
edges and a pulse that only changed brightness.

**How a frame is made.** Every glowing renderer still on screen is drawn into a mask (colour × brightness, halo width
in pixels, depth; only the parts the camera can see). The mask is jump-flooded at half resolution, so every pixel
near it learns which glowing pixel is nearest and how far away it is. One pass then lays the light over the frame
from that distance: `pow(1 - distance / width, softness)`, starting one pixel outside the model so its ink outline
stays black, and hidden by anything standing in front of the glowing thing. All passes are scissored to the glowing
things' screen rectangle. Render Graph only; nothing is added to the models (no shells, no extra materials).

**Who glows.** Anything that registers a `GlowHalo` (`Rendering/Runtime/GlowHalo.cs`): today tempered weapons
(`WeaponVisualMount` through `GlowRim`) and evolved pets (`PetFollower`). The look comes from data: a Temper band's
colour and its rarity's `TemperGlowLook` (`GearProgressionSettings.temper.glowLooks`), a pet's stage
(`PetSettings.rim…`).

**The breath.** Each halo breathes on its own beat (so a crowd does not pulse in step): a smooth swell that is
brightest and widest together. `pulse` is the share of brightness lost at the low point, `pulseSpeed` is breaths a
second, `pulseWidth` is how much wider it is at the top of a breath.

**Only part of a model can glow** (`GlowZone`: a box the glow is kept to, and a box kept dark). The dark parts go
into the mask too and are flooded alongside: air whose nearest piece of the model is a dark one gets no light, so the
glow of a blade stops at its hilt instead of spilling round it, and ends softly where the two meet (`zoneSoftness`).
Weapons carry their zone on their model prefab (`WeaponGlowZone`): blades glow past the guard, axes and scythes on the
head (the shaft beside a beard or between horns stays dark), guns everywhere but the grip, bows everywhere but the
string. Baked from the meshes by **Krytical Nova ▸ Items ▸ 8**; the ten guns, the compound bow, the cleaver and one
knuckle blade are set by hand and ticked "keep as set".

**What the strongest looks add**, in the same pass and always in the glow's own colour, never where there is no light:

| | What it is | Per look | Tuned on the feature |
|---|---|---|---|
| Sparks | a few small dots with a thin four-point glint that swell and fade, in the light and on the glowing part | `twinkle` (brightness) | `twinkleSize` (room per spark: larger = fewer), `twinkleShare`, `twinkleRate`, `twinkleDot`, `twinkleRay`, `twinkleWhite` |
| Lightning | thin jagged arcs that stand off the outline for an instant and flicker out | `lightning` | `boltLength`, `boltRate`, `boltChance`, `boltWidthPixels`, `boltBrightness`, `boltWander`, `boltWhite` |
| Flame | tongues that rise from the outline, lean towards the tip and stream to it | `flame` (share of the light) | `flameSize`, `flameSpeed`, `flameDepth`, `flameLean`, `flameHeat` |
| Embers (10 Oct 2026) | the light broken up like a flame seen close: a ragged edge, a patchy body, and shades from a deeper to a hotter, paler tone of the glow's own colour. Two layers of gradient (Perlin) noise pinned to the weapon, one streaming to the tip, one boiling across. Lies over the flame when there is one. Only the 120 % Temper band asks for it (`TemperGlowBand.ember`) | `ember` | `emberSize`, `emberSpeed`, `emberEdge`, `emberPatch`, `emberHeat`, `emberDeep` |

They are pinned to the glowing thing (its origin and the way its tip points), so they travel with it. Epic and
above wear all three; lower rarities glow plainly. At most 32 things in one view carry them (the rest only glow).

**Feature settings** (`PC_Renderer` ▸ Glow Halo): `softness` 1.8, `inkGap` 1 px, `solidity` 0.45 (how much of the
background the light covers, so it does not burn out on a bright sky), halo width clamped to 3–56 px, `depthFade`
0.02 m (0.05 until 11 Oct 2026), fade-out from 55 to 80 m, `halfResolution` on. PC renderer only (it needs the depth
texture).

### The light is never drawn through things (11 Oct 2026)

The owner saw a pet's rim as "a disco ball" behind an object, and the Epic 120 % glow of a Hegemony gun shining
through the player and through other objects. Two causes, both measured and both closed:

- **Props the camera is fading.** `OccluderFader` dithers whatever stands between the camera and the player, and
  the dither is also in the depth the camera keeps: the light of a pet or a weapon behind such a prop was laid down
  in every hole of the fade and nowhere else, a field of bright dots. The halo now draws the fading props whole
  into a small depth target of its own first (pass 4 "Block" of `GlowHalo.shader`, the props from
  `OccluderFader.Faded`, the nearest of them kept with a Min blend), and both of its depth tests take the nearer of
  the scene and that. Behind a fading prop the pet and the weapon are still seen through the fade; their light is
  not. With no prop fading the pass only clears.
- **Conservative rasterisation of the mask.** The mask was drawn conservatively so that a blade thinner than a pixel
  far away still lit the pixels it crossed. A pixel that only touches a triangle is given a depth worked out beyond
  the triangle's edge; on a face seen nearly edge-on (a gun's barrel from behind, a bow's limb) that depth is far
  nearer than the face, so hidden pixels passed the "can the camera see it" test and seeded a halo on whatever hid
  them. It is off. The price: a blade under a pixel wide (beyond about 40 m at 1080p, where the halo is already
  fading out) may flicker in and out of the mask.
- `depthFade` went from 5 cm to 2 cm: less light on a body that stands just in front of the weapon it carries.

**The glow leak check** (`Rendering/Editor/GlowLeakCheck.cs`, menu Krytical Nova ▸ Lighting ▸ Check Glow Leaks, in
Play on the local player): for each of the 45 weapon models, as Rare and as Epic at 120 %, carried and drawn, it
draws the character from a camera of its own with the glow and without (`GlowHaloFeature.Suspended`) and counts the
light: on a wall that hides the character altogether (must be 0), and on the body where the weapon is not in front
of it (for comparison between runs: a weapon held before the body lights it rightly). Table and marked sheets go to
`Temp/GlowLeaks`. Before: 12 of the 90 looks lit the wall (Warbow 01, 04, 05 and Arc Caster 01, 04, 05, as Rare and
as Epic; 40,832 pixels in all), and a drawn Carbine 01 put 2,448 pixels of light on its owner's back. After: 0 of
90, and 369.

Not done: `FX_PetWaterFlow` (the Tidecaller's ribbons) has no DepthNormals pass, so it is not in the camera's depth
and a halo is drawn over it. Giving it one would also ink its outline, which changes the pet's look: left for the
owner to ask for.

### A Nova hides what is behind it (10 Oct 2026)

Characters, pets, Hostiles and their effects are not seen through an Ascension Nova; buildings, ground and sky still are. The reverse of `OccluderFader`, and done with draw order instead of material swaps:

- `ActorDrawOrder` (on players, pets, Hostiles and NPCs) gives their solid renderers sorting order 2, so they are drawn after the scenery (order 0). It looks its models over once a second, because gear and growth stages swap them.
- `NovaHalo` draws a camera-facing disc behind its rings with `KryticalNova/NovaMask` (depth and stencil only), in stages: at order 1 it marks the disc's sky pixels and writes its depth, so the actors drawn next fail the depth test behind it; at order 3 it gives the marked sky pixels the far plane back, so the skybox fills them; first thing in the blended pass it writes its depth again, so sparkles and other blended light behind it stay hidden. Stencil bits 0x40 and 0x20 are its own.
- The disc has no depth-only pass, so it is not in the depth texture: fog, ambient shading and the ink do not see a disc. What reads the depth texture is told where the Novas are instead (`NovaCull` → `_KN_NovaDiscs`, `_KN_NovaDiscCount`, at most 8): `GlowHalo.shader` drops the light of what is behind one, `ComicPost.shader` drops its ink.
- **The disc must not be seen (10 Oct 2026, the same day).** As first built it showed, three ways, all fixed:
  - *A pale circle on a shadowed wall.* `ComicPost` took the ink and the comic shadow off everything deeper than the Nova inside the disc, on a 2 pixel edge. It has only depth to go on, so walls and ground lost theirs too. Now `NovaCull` finds the actors that stand behind a Nova as the camera sees it (`ActorDrawOrder.All`, each with a ball round its solid models) and hands them over as `_KN_NovaHidden` / `_KN_NovaHiddenCount` (at most 8): the ink is left out only inside one of those balls. Scenery keeps its lines.
  - *The comic shadow* is a separate rule (`ShadeBehindNovas`). It is laid over the finished picture, so it would dot and dim the Nova's own glow where the wall behind is in shadow; it thins toward the Nova's middle like the light of a lamp, whole at the disc's rim and gone at the star, with no line between.
  - *A pet sliced on a circle.* The disc's rim is broken into dots on the screen's own grid that shrink to nothing at the edge (`NovaMask.shader`, the same dots in all six stages), so what it hides comes back into view through them. The halo's light and the ink come back over the same band (`_KN_NovaFeather`).
  - Settings (`NovaLookSettings`): `maskSize` 1 (the share of the Nova's reach the disc covers) and `maskFeather` 0.45 (the share of the disc's radius that is rim). `backdrop` 0.4, the soft patch of night, is unchanged.
- A character seen through its own Nova (the camera looking down on it) is drawn with the scenery for as long as that lasts, so it is never cut by its own Nova.
- Not hidden: anything that is neither marked an actor nor blended (a new kind of creature needs `ActorDrawOrder.On(root)` where it is built), and the part of a model that sticks out past the disc.

## 6f. Motion smear, and the slow motion of a critical kill

**The smear.** For a moment a character's trailing side is pulled back, in streaks, to where it was a tenth of a
second ago: when a run takes off, and when the blow that kills throws the body. Since 15 Oct 2026 (owner) the run's
smear does not end with the take-off: whole for 0.45 s, it stays at a third of its strength (`SmearProfile.sustain`)
for as long as the run goes on and fades over 0.15 s when the run stops (`ActorSmear.Release`); a jump is smeared the
same way, whole as the feet leave the ground and lighter through the air until the landing (`SmearSettings.jump`, on
the Jump moment). The run's smear shows from 5.5 m/s, so a rank 1 run (7 m/s) has it. It is the smear package's own shader
(`WuHaipeng/smear_urp`, `Assets/_ThirdParty/smear effect/Demo_urp/shader`, untouched) drawn as a **second skin** over
the body: the same mesh and bones, the body's colour texture, switched on only while a smear plays (`ActorSmear`,
`GameFeel/Runtime`). The body's own comic materials are never swapped, so its ink, tone and hit flash carry on
underneath. The skin casts no shadow, and weapons and pets are not smeared.

The package's control scripts (`Smear_control_*`) are not used. They need the body's own materials to be the smear
shader, they smear every movement all the time (a walk, a teleport, a network correction alike), and they sample in
`FixedUpdate` over a fixed count of frames, so the trail changes with the frame rate. `ActorSmear` hands the same
shader the same properties (`position_current`, `position_previous`, `_smear_strength`, `_noise_scale`) under the
rules below.

**When.** A FEEL feedback (`MMF_Smear`) on the world half of the **Run Start**, **Death** and **Critical Death**
moments of `PFX_GameFeel`, so it plays on every client for every character and is timed, and switched on or off,
with the moment's dust and comic text. Run Start fires only when the run clip starts (the Run skill: 10 m/s at rank 1
up to 29 m/s at rank 20), never for a walk. `GameFeelBuilder` puts the feedbacks there: **Krytical Nova ▸ GameFeel ▸
Rebuild Smear Feel** does only that (and makes the settings asset when it is missing); **Build Game Feel** and
**Rebuild Death Feel** include it.

**A take-off for every start and every new destination (11 Oct 2026).** A run that starts, and a new move order given
while running, both ask for the take-off. `GameFeelPresenter.RunStarted` plays its dust and smear each time, at least
`runTakeOffRepeatSeconds` (0.4 s) apart (`RunTakeOffRules.TakesOff`): the owner wants the smear on every new
destination, also one chosen mid-run. The 4 degree kick of the view on the player's own screen is rarer
(`RunTakeOffRules.KicksView`): once for a run from a standstill (`ActorVfx.LastStillTime`, within
`runTakeOffRestSeconds`), and again only after `runTakeOffQuietSeconds` (1 s) with no order. Before 10 Oct clicking
about quickly kicked the view every 0.4 s (the field of view was changing for a quarter to a third of all frames); from
10 to 11 Oct one sliding 1 s window gated the smear as well, and since every order restarted it, a player who kept
clicking saw the smear on the first take-off only. Measured after: 12 new destinations 0.43 s apart, 11 of them
mid-run, gave 12 smears and one kick; five stop-and-go runs a second apart gave five of each.

**A run ends on its point at the body's own height (10 Oct 2026).** At the end of a fast run the body is placed on the
destination (`PointAndClickPlayer.PlaceOn`). `NavMeshAgent.Warp` alone puts the root on the NavMesh point, the agent's
base offset (1 m) below where the root belongs, until the agent's next update: for one drawn frame the body stood
waist-deep in the ground and the camera, which follows without damping, dropped with it. Measured: 15 to 33 such
frames in 12 s of rapid clicks, none after. `NetworkPlacement.Place` already placed the root itself.

**Props about to block the view fade early (10 Oct 2026).** `OccluderFader` checks every 0.1 s and a fade takes 0.2 s,
in which the top Run rank covers nine metres: a tree or a truck filled the screen before it thinned. Above 8 m/s it
checks every frame and also along the line the camera will be looking down 0.15 and 0.3 s from now.

**The rules.** Every number is in `GameFeel/Data/Assets/SmearSettings.asset`, one profile per moment:

| | Take-off | Death throw | What it does |
|---|---|---|---|
| `seconds` | 0.45 | 0.5 | how long the smear lasts from the start of the motion |
| `trailSeconds` | 0.1 | 0.1 | the body is stretched back to where it was this long ago |
| `shape` | whole to 45 % of its life, then fading out | whole to 40 %, then fading out | how much of the stretch shows over its life: strong at the start, gone by the end |
| `strength` | 1.25 | 1.25 | the package's smear strength |
| `longest` | 1.8 m | 1.6 m | never longer than this, however fast the body goes |
| `slowest` | 7 m/s | 4 m/s | nothing is smeared below this speed (a walk is 5 m/s, a knock-down about 2.5); the smear is whole from a third above it |

`placedSpeed` (200 m/s): a body that covers ground faster than this between two frames was put there (a spawn, a
teleport, a large network correction), and its smear is dropped at once. It sits well above the fastest real motion:
the first frame of a critical death's throw is about 80 m/s. `noiseScale` (100) is how finely the smear breaks into
streaks.

**Which deaths.** The thrown body of a critical death (5.5 m, backwards from a blow to the front, forwards from a
blow to the back) smears through the fast first half second of its flight. A normal death only knocks the body
over (it moves 0.4 to 0.7 m, at up to about 2.5 m/s): that is below the death smear's `slowest`, so it is not
smeared; lower `slowest` to smear it too. A throw that a wall cuts short moves too little and is not smeared either.

**Cost.** One more draw of the body's skinned meshes on the frames that have a stretch to see (half a second at
most). A skin with nothing to show is not drawn, and between smears the skins are switched off; they are made on a
character's first smear and kept.

**The slow motion.** When *your* critical blow kills another player, the thrown body hangs for a blink on *your*
screen only: it is shown at a quarter of real speed for 0.12 s, then catches up with where it really is over the
next 0.26 s (`ActorTimeSlow`; `critKillSlowSeconds` and `critKillSlowRate` on the game feel prefab's presenter, 0
seconds = off). At its furthest the picture is 0.09 s behind. It is a picture only: the body's drawn parts are shown
where its root was a little while ago, and its animation runs at the same slowed, then quickened, pace so pose and
place stay together. The game's clock, the body's real place and the network are never touched, so it is safe on the
host, nobody else sees it and the victim's own screen is never slowed. It rides with the kill's hit stop, chromatic
punch and comic text. Not played for a normal kill, for a kill by a GM command, or when the victim is not a player.

## 6g. Icons

Two families, made to sit side by side in the HUD:

- **Pictures of the real thing.** Gear and pets are rendered from their own models through the comic pass
  (`ItemIconCapture`): the icon is what you will wear. A tier of the gear ladder wears its look's icon.
- **Pack icons in the comic treatment.** Skills, and every item that has no model (forge materials, Stims, Kiosk
  Permits, the Pet Ration, jewellery), take a painted icon from the two CraftPix packs: `_ThirdParty/Fantasy RPG Icons
  Pack` for skills, `_ThirdParty/Cyberpunk RPG Icons Pack` for items. `ComicIconTreatment` (`Assets/_Game/Icons/Editor`)
  redraws each one. The packs' files are only read, never changed: the game shows treated copies, written to
  `Gameplay/Progression/Data/Assets/Icons` and `Gameplay/Items/Data/Assets/Icons`.

**The treatment.** Every number is in `Icons/Data/Assets/IconTreatmentSettings.asset`; change one, then **Krytical
Nova ▸ Icons ▸ Bake Every Assigned Icon**. Sizes are in pixels of the finished 256 px icon (drawn at twice the size
and reduced).

| Step | What it does | Settings |
|---|---|---|
| Fit | a skill painting becomes a rounded tile; an item keeps its own outline, scaled into the same box | `margin` 18, `cornerRadius` 30 |
| Flat colour | an edge-keeping blur gathers soft paint into even patches and leaves the edges between shapes sharp | `flattenRadius` 4, `flattenPasses` 2, `flattenTolerance` 0.14 |
| Tones | each pixel is drawn in one of five flat tones by how bright its colour is (its strongest channel, so a pure red stays red) | `tones` 5, `toneAdapt` 0.35, `toneLow` 0.24, `toneHigh` 0.95, `toneContrast` 0.55, `toneSettle` 1 |
| Ben-Day dots | the second darkest tone is drawn as dots, on the same 45° grid as the world's dots | `dotCell` 9, `dotRadius` 0.36, `dotContrast` 0.5 |
| Ink | a line where two patches meet with a clear step in colour; never over the lightest tone, so a bolt of lightning or a blade's edge keeps its light | `inkEdge` 0.24, `inkWidth` 5, `inkSpeck` 30, `inkSpareTones` 1 |
| Colour | stronger, and pulled toward the nearest colour of the Colour Bible or of a faction; the darkest tone leans to shadow indigo | `saturation` 1.4, `palettePull` 0.35, `palette`, `shadeTint` 0.45, `shadeColor` `#4C52BF` |
| Sticker | the ink outline, paper keyline and hard shadow every icon of this family wears | `outlineWidth` 7, `keylineWidth` 3.5, `shadowOffset` 5, `shadowAlpha` 0.55, `ink` `#14122B`, `paper` `#F4EEDF` |

An item icon has a clear ground, so the bag's rarity frame and glow show round it.

**One colour per class.** A class's ten skills come from one set of the pack, so its Hotbar has one colour:
Monoblade blue, Glaive magenta, Carbine cyan; Harvester red, Servo Claws teal, Arc Caster violet; Longaxe
red-orange, Claymore orange-gold, Warbow green. The eight shared skills, Run and Jump come from the many-coloured
Buff set. A set can have its own colour moved round the colour wheel when a class wears a set painted in another
faction's colour (`IconLibrary.Set.moveHue`): the Arc Caster's set is painted electric blue and is moved to Null's
violet, while skin and metal in the picture are left alone.

**The library** (`Icons/Data/Assets/IconLibrary.asset`, edited in **Krytical Nova ▸ Icons ▸ Icon Library**) lists the
packs' sets, tags icons by use (attack, buff, debuff, passive, movement, item), faction, class, lineage and
keywords, and records which icon every skill and item has. No icon is ever given twice. A new skill or a new item
without a model gets its icon there: choose it on the left, pick from the best free matches, press Give. **Check
Icons** lists anything left without one.

## 6h. Comic marks and words

The comic layer of a fight: `Design/13_comic_effects.md` has every effect, word and number. The rules of the look:

- **Shape, word and lettering say who hit; colour says what kind of hit it was.** A weapon class owns one shape of
  impact mark, one font and three words. A kind of hit (Normal, Elemental, Critical, Shattered, Reflected) owns one
  ramp of four colours (core, fill, back, ink), the same for every class. `HitLookSettings.asset` holds both.
- Every comic effect is flat art that carries its own ink outline and its own dots, in the Colour Bible's ink
  `#14122B`. It is lettering: it is drawn over the inked frame (section 2, 6c), never through the ComicStyle pass.
- A word is read or it is noise: a word of a hit is at most 1.7 m wide with capitals a third of a metre tall, sits
  above the hit and stays at least 0.55 s; a mark is at least 0.18 s. Of fights the local player is no part of, five
  words at most are on screen.
- The look of a critical hit by one player on another is the largest lettering in a fight and is used for nothing else.
- Effects come from the Cartoon FX pack through `GameFeelBuilder.Comic.cs`; the pack's files are only read.

**Comic speech bubbles** (pets today, `ComicBubble` in the HUD): paper `#F4EEDF`, a 3 px ink outline, a hard ink
shadow 4 px down and to the right, shadow-indigo Ben-Day dots on a 7 px 45° grid growing toward the lower right
corner, and a tail on the side the speaker is. Every number is a `--bubble-*` value of `.comic-bubble` in
`ProgressionHUD.uss`. The animal pets' emoji are drawn in the same ink, with one fill and one accent of the Colour Bible.

## 6i. The interface: faction skins and the menus' comic book

`Design/14_interface_skins_menus_settings.md` has all of it. The rules of the look:

- **One skin per faction, in an asset** (`UI/Skins/UiSkin_<Faction>.asset`). No screen names a faction's art or colour
  itself: the game's style sheets are generated from the assets, and the title flow is built from the menu pack the
  asset names. The comic layer (ink `#14122B`, paper `#F4EEDF`, Ben-Day dots on the shared screen grid) is ours and is
  the same in every skin.
- **Colours that mean something are never skinned:** rarity colours, the pets' comic bubbles, a namecard's faction
  colour and the red of a Callout opponent.
- **A menu screen is a page of a comic book.** The page that turns is the screen that was left, unlit: where it curls
  away from the reader it is shaded with Ben-Day dots on the same grid as the rest of the frame, never with a
  gradient; its back is paper with dots in the menus' accent and an ink line round it; the cover is solid ink. The
  flat page shows the screen in exactly its own colours (shader `KryticalNova/UI/Comic Page`).
- **Feedback moves the thing that was touched and nothing else:** a swell, a squash, a nudge or a shake of a few
  percent or pixels, over in a quarter of a second, added to the pack's own animation, never in place of it.

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
| Minimap | no camera | the zone's baked picture with markers on it (11 Oct 2026); until then a live top-down camera drew the world again 8 times a second at 512² |
| Portrait on the player plate | no camera at rest | a still picture, taken again only for a change of armor or of the character's look (11 Oct 2026); until then its camera drew 30 times a second |
| Menus' page turn | nothing while a screen is at rest; during a turn one picture of the screen and one mesh drawn by one camera for 0.7 s | not measured against a budget yet |
| Comic marks and words | nothing created in a fight, 0 B GC a hit, < 1 ms a frame in a crowd | measured in the Editor at 30 hits a second by other players: +0.6 ms mean frame, 0 effects created, 0 B allocated by the layer's own calls; the pool is filled when a scene starts (about 86 MB of sleeping comic effects by the Editor's count) |
| Glow Halo | < 0.5 ms GPU, < 0.3 ms CPU with 40 glowing weapons | measured 0.22 ms GPU / 0.18 ms CPU with 42 halos (flame, lightning, sparks) at 925×1067 in the Editor, 0.07 / 0.06 ms with 2; the flood runs at half res inside the glowing things' screen rectangle only; nothing glowing = no pass |

Measure with the Frame Debugger and Profiler (GPU module) in a build, not the Editor. Look for each feature's
`ProfilingSampler` name ("Comic Style", "Sun Shafts", "Glow Halo", "Comic Lettering").

### The frame rate the game must hold (owner, 10 Oct 2026)

**A locked 60 frames a second on mid to upper-mid tier PCs, and no frame under 50 (20 ms), even as a spike.** An
average does not count: performance is judged by the slowest frame. The test is the training dummy with every buff
up, the pet out, a Nova lit and every hotbar skill on rotation for a minute. The performance readout shows the two
numbers that decide it, **Slowest, last minute** and **Frames under 50**, and names the machine it is running on,
so a reading from any PC can be judged.

What mid tier means here is an estimate until a reading from such a PC says otherwise: an RTX 3060 / RX 6600 with
a Ryzen 5 5600 at 1080p on Medium, and an RTX 4060 Ti / 4070 at 1440p on High. On the development PC (RTX 5070 Ti,
Ryzen 7 9700X), about 2.5 to 3 times that GPU and 1.5 times that CPU, the same frame has to fit in: GPU 6 ms at
1080p Medium and 8 ms at 1440p High; main thread 9 ms steady and 13 ms on its worst frame.

**Measured in the Editor, 10 Oct 2026** (the game view's render scale raised to the pixel count of each screen;
medians of 200 frames, GPU / main thread in ms):

| | 1080p Low | Medium | High | Ultra | 1440p Low | Medium | High | Ultra |
|---|---|---|---|---|---|---|---|---|
| Plaza, noon | 4.1 / 8.5 | 4.2 / 8.3 | 4.4 / 8.5 | 4.9 / 8.7 | 4.3 / 8.4 | 4.6 / 8.7 | 5.2 / 9.0 | 5.8 / 9.0 |
| Thick meadow, noon | 4.2 / 9.2 | 4.7 / 9.2 | 4.8 / 9.4 | 5.2 / 9.4 | 4.8 / 9.2 | 5.2 / 9.3 | 6.1 / 9.2 | 6.0 / 9.3 |

- The steady budgets hold on this PC even inside the Editor. About 4 ms of the GPU figure does not move with the
  screen or the preset, so it is not the picture's cost: in the Editor it cannot be told apart from the Editor's own
  drawing, and no single effect could be ranked there (each came out under about 0.7 ms, within the noise of the
  measurement, about 0.3 ms). Main shadows at 2048 with two cascades in place of 4096 with four saved 0.8 to 1.0 ms
  three times out of three; the grass costs about 1 ms in the thickest meadow.
- **The slowest frame cannot be judged in the Editor.** Standing still there, with nothing happening, about 7 % of
  frames take longer than 20 ms: the Editor's own loop (57 ms at worst in the capture) and a second Editor sharing
  the PC. The game's own part of an idle frame is small and even: scripts 1.5 ms, culling 1.3 ms, physics 0.2 ms,
  garbage 2.5 KB. **The check that counts is the dummy test in a build, read off the readout.**
- What was found and fixed on the way: the HUD rebuilt itself two or three times on every cast (105 ms each: the
  30 fps drops), now once a frame and only what is open; the portrait's camera drew every frame (0.7 ms of the main
  thread for a 256 px picture), now 30 times a second.

### The performance log (11 Oct 2026)

The readout says that a slow frame happened; the log says when, and what the game was doing. **A built game always
writes one**: a text file for each run, `PerformanceLogs/perf_<date>_<time>.txt` beside the exe (the newest 20 are
kept, about half a megabyte an hour, nothing is sent anywhere; a tester sends the file by hand). Play in the Editor
writes one only while **Krytical Nova ▸ Build ▸ Record Performance Log In Play** is ticked (into
`Library/PerformanceLogs`), because the Editor's own slow frames are in it. **Show Performance Logs** in the same
menu opens the folder, and a rebuild of the test build keeps the folder.

Code: `Core/Runtime/Diagnostics/` (`PerformanceLog`, `PerformanceLogRecorder`, `FramePhaseProbes`, `MarkerRing`,
`PerformanceLogText`). Lines are tab-separated and start with their kind:

| Line | When | What it holds |
|---|---|---|
| `#` | once | when, version, the machine, the screen, the names of the columns |
| `S` | every second | scene, frames, mean and slowest frame, frames over 20 ms, main thread / render thread / GPU ms, set-pass calls, triangles, memory, script memory, memory collections so far, players, networked objects, where the camera is, what the log itself cost |
| `F` | every slow frame (over 20 ms, or over three times the usual frame of the second before) | its length, whether a memory collection ran in it, loading or play, its main thread / render thread / GPU split, **where in the frame the time went**, and **what the game did in the two seconds before** |
| `E` | when the game does something that can cost a frame | its name, how long it took if it is a piece of work, a detail, a number |
| `C` | on a change | graphics options applied, a scene arriving, a session starting or ending: numbers before and after are not to be compared |

- **Where the time went** is measured by the game itself, because a built game has no profiler markers: a clock read
  at the start and end of each top-level phase of Unity's player loop (`TimeUpdate`, `Initialization`,
  `EarlyUpdate`, `FixedUpdate`, `PreUpdate`, `Update`, `PreLateUpdate`, `PostLateUpdate`, in which the frame is
  drawn), and `other` for what falls between them. It names the part of the frame, not the function.
- **Which steps** (11 Oct 2026, the last column of an `F` line): a phase is many steps, and "PostLateUpdate took
  37 ms" named nobody (48 of the 83 slow frames of the first real log). The clock is also read between the steps
  of every phase (some 130 of them: `PreLateUpdate.ScriptRunBehaviourLateUpdate`, `PreLateUpdate.DirectorUpdateAnimationEnd`,
  `PostLateUpdate.PlayerUpdateCanvases`, `PostLateUpdate.FinishFrameRendering`...), and a slow frame names the six
  that took longest. Inside the step that draws, `FrameCameraProbes` says which camera took the main thread's time
  (`camera:Main_Camera=12.3`). A step is still not a function: inside the scripts' own Update and LateUpdate it is
  the markers below that name the work.
- **Sections** (`H` lines, 11 Oct 2026): the log counts every frame of play by its length, not only the slow ones,
  in bins a quarter of a millisecond wide (5 ms wide past 50 ms), so the 1 % and 0.1 % lows can be worked out
  afterwards. `PerformanceLog.Section(name)` starts a stretch of its own (the benchmark's three); everything else
  is the section `session`. A section's count is written when it ends, at the end of the run, and once a minute as
  it stands; the last `H` line of a section number is the whole of it. Frames marked `loading` are left out.
- **Markers** are put where a hitch can plausibly come from, with `PerformanceLog.Mark(name, detail, number)` or
  `using var timed = PerformanceLog.Time(name, detail, number);`. While nothing records a call is one test and
  returns. Never build a string for a marker without asking `PerformanceLog.IsRecording` first. The ones in place:
  `save.player`, `save.file`, `save.all`, `hud.redraw`, `window.service`, `skill.cast`, `level.gained`,
  `pet.evolved`, `nova.work`, `zone.prewarm`, `zone.wake`, `zone.sleep`, `zone.spawn`, `drop.appeared`,
  `warmup.scripts`, `world.collect`, `bench.begin`, `bench.end`, and `save.disk` (the file being written on the
  save store's own thread: timed, but not the frame's). A new system that does a burst of work (builds a window,
  spawns a batch, writes a file) gets one.
- **Reading one:** `python Assets/_Game/Tools/Editor/Build/ReadPerformanceLog.py` reads the newest log of the
  test build (or a file or folder given to it) and prints each section with its average, 1 % low, 0.1 % low,
  slowest frame and whether it passes the mark (60 locked, none under 50), the slow frames grouped by likeliest
  cause, where their time went by step and by camera, the worst fifteen with their split and markers, every timed
  piece of work, what grew over the session, and how much memory the scripts were handed a minute and a frame.
  It reads older logs too (they have no steps and no sections).
- **The benchmark:** `gm.bench` in the GM console (host, standing in town, hands off for two and a half minutes)
  plays the same three stretches every time, a section each: `bench.town` (two turns of the camera in 20 s),
  `bench.fight` (45 s at the nearest training dummy, auto-attack and every learned skill on rotation: the owner's
  test) and `bench.run` (60 s running towards the Hostile Zones, nearest first). The character is not changed: it
  takes no damage and spends no Flux while it runs, and is put back where it stood. `gm.bench stop` ends it.
  `Tools/Debug/BenchCommands.cs`. One build is compared with another, and one PC with another, by these three.
- **Limits:** garbage made per frame cannot be read in a built game, only that a collection ran. The split of a
  slow frame is matched to it by length a few frames later; `~` before a number marks a match that is not certain.
  Frames in the first five seconds, and three seconds after a scene or a session arrives, are marked `loading`.
- **Cost:** 10 to 40 microseconds a frame measured in the Editor (the last column of every `S` line says what it
  was), against a budget of 50.
- **First things it showed** (in the Editor, so the sizes are not a built game's): preparing the eight Hostile
  Zones' pools takes 100 to 200 ms each when the world loads, and a player's save takes 3 to 16 ms on the frame it
  happens, every 60 seconds for each player on the host. Neither is changed yet.

### What the first real log led to (11 Oct 2026, branch `perf/top-tier`)

The owner's 23 minutes in the 09:53 build (RTX 5070 Ti, Ryzen 7 9700X, 1440p, no cap): 4.0 ms a frame as a rule,
98 frames over 20 ms, a collection of memory every six seconds with one of 258 ms, the scripts handed 55 MB a
minute (3.7 KB a frame), `hud.redraw` 4.2 ms about every 11 seconds, a save 3.9 ms of which 3.5 ms the disk, and
slow frames thinning out over the session (35, 23, 12, 10, 3 in each five minutes) though the character stood
still: firsts being paid for one by one. Eight things were done about it, in this order.

1. **An IL2CPP build beside the Mono one.** `TestBuildBuilder.Build(ScriptingImplementation.IL2CPP)`, menu
   **Krytical Nova ▸ Build ▸ Windows Test Build (IL2CPP)**: scripts compiled to machine code before the game
   ships, so nothing is compiled while it runs. It needs Unity's "Windows Build Support (IL2CPP)" module and the
   Visual Studio C++ build tools with a Windows SDK; `TestBuildBuilder.Il2CppMissing()` says which is not there.
   The first such build was made on 11 Oct 2026 at 22:47 (11 minutes, as long as the Mono one); it had not been
   run when this was written. Unity's own Visual Studio module for 6000.6 (`unity install-modules --module
   visualstudio`, Visual Studio Community 2026) installs the C++ tools but no Windows SDK, because the one it asks
   for is not offered by that Visual Studio: `Microsoft.VisualStudio.Component.Windows11SDK.26100` was added
   through the Visual Studio Installer. Stripping is Minimal for
   it (the GM console and Netcode find things by reflection). The Mono build touches no setting; the project's own are put back after the IL2CPP
   build. The log's header says `scripts Mono` or `scripts IL2CPP`.
2. **Garbage.** `Krytical Nova ▸ Build ▸ Allocations ▸ Start Recording / Write Report` (`AllocationFinder`) records
   Play with the Profiler and the call stack of every allocation, and writes who allocated how much a frame, by
   our own method and line, to `Library/KryticalNova.Allocations.txt`. What it found, standing still, in bytes a
   frame: the performance readout itself 4,600 (IMGUI drawing the same text again every frame: more than the rest
   of the game together), the Hotbar's cooldown lines 620, the grass's shader calls 330 (`SetInts(id, a, b)` makes
   an array each call), the map marker 116 (a scene's name is a new string every time), namecards 210 (names
   turned from their replicated form to text every frame), the Settings window's five-times-a-second check of every
   setting while shut 150, the minimap 170. All of them are gone: 7.7 KB a frame became 2.7 KB in the Editor, and
   of what is left 0.4 KB is the Editor's alone (Cinemachine's IMGUI), 0.3 KB the log's own lines for the
   Editor's many slow frames. The readout is now a canvas of its own and off costs nothing at all.
3. **Warming up.** `ScriptWarmUp` (`Core/Runtime/WarmUp/`) compiles every method of the game's assemblies and of
   the engine's largest that run in play (the interface, the render pipeline, Netcode, Cinemachine, Animancer, about
   40,000 methods) ahead of its first use: a few milliseconds a frame in the menus, more behind the Loading page,
   which stays up until it is done (`LoadingGate`, "Preparing"). Tried in the Editor: 39,838 methods, 2.4 seconds of the main thread in all, none refused. Under Mono `RuntimeHelpers.PrepareMethod` does
   nothing; asking for the method's function pointer compiles it (measured: 2,500 methods 211 ms the first time,
   0.6 ms the second). Compiled on the main thread, since compiling can run a class's static set-up. An IL2CPP
   build needs none of it. And the memory left behind by the menus and the world's start-up is collected once,
   under the picture that covers the world's first frames (`LoadingGate.WhenTheDoorOpens`, marker `world.collect`),
   so play starts with none of it owed. **Not done:** shaders are still compiled by the graphics driver the first
   time each is drawn, effects are not rehearsed, and windows other than the Character Sheet and Skills are still
   built when first opened; the steps column will say whether those are what is left.
4. **The HUD in pieces.** `ProgressionHUD` finds its parts once, and each piece (level, the walk/run line, points,
   the XP bar, HP and Flux, each Hotbar slot, each cooldown line, each buff's clock) remembers what it last
   showed: a redraw that changes nothing writes nothing. Cooldown and buff clocks are written when the number on
   them changes, not every frame (a buff with more than ten seconds left counts in whole seconds). `hud.redraw`
   went from about 4 ms to 0.02 ms in the Editor.
5. **Saving off the main thread.** `FileSaveStore` made with `background: true` (what `ServerSave` uses) hands a
   write to a thread of its own; a read gives back what is still waiting, the newest write of a key wins, and
   `Flush()` (on quitting, on `gm.save`) waits for the disk. What is left on the frame is turning the character
   into text.
6. **A frame cap by default.** The Graphics page's frame cap (used with vertical sync off) has a new first choice,
   **AS THE SCREEN**: the highest refresh rate the screen is made for (the fastest mode it offers). The game ships
   with vertical sync off and that cap (owner, 11 Oct 2026); NO CAP is still there. The cap is stored under a new
   key, so an older "no cap" is not taken for a choice. A PC that is not flat out has room
   for a frame that takes longer, and frames the screen cannot show are work for nothing.
7. **Naming the unnamed**: the steps and the cameras above.
8. **Measuring**: the sections and the benchmark above.

Two more came from the owner while this was being done: **the minimap no longer draws the world** (the baked
picture, with markers for the player, the Squad's members, NPCs, portals, the nearest Hostiles within
`HostileSettings.minimapMetres`, a Bruiser with a marker of its own, and one kept for a boss), and **the portrait
is a still picture**. Both took a camera out of the frame.

**Measured in the first two builds** (owner's PC, 11 Oct 2026, `gm.bench`; IL2CPP 22:47 / Mono 20:59). Both hold
the 144 cap. In play the scripts are handed 0.2 to 0.6 KB a frame (3.7 before) and memory is collected 0 to 1
times a minute (10 before); `hud.redraw` 0.01 to 0.03 ms; a save 0.4 / 1.1 ms on its frame. Slowest frame:
`bench.town` 8.7 / 47.7 ms, `bench.fight` 13.0 / 19.2 ms, and after the benchmark, standing, 10.5 / 101.5 ms. The
IL2CPP build passes the turn and the fight with room for a PC one and a half times slower. `bench.run` failed in
both, for the one reason below.

### A fade may not cost a frame (the North Gate, 11 Oct 2026)

Running through the North Gate took the graphics card from 2.4 ms to 22-35 ms for three seconds, and set-pass calls
from 268 to 1,115. Three wall pieces were being faded (`OccluderFader`), no more. The cost was the glow halo's pass
that draws "the faded props whole", so a glow does not shine through the fade's holes: it drew each prop's parts
by the number its mesh reported, and a piece of the town's combined static mesh reports that whole mesh, 208
parts. Three pieces, 624 draws of a town-sized mesh. With the fix the same view costs what it costs with the wall
solid (4.6 ms, 235 set-pass calls, in the Editor).

What is in place so that nothing can do this again:

- **A faded piece answers for its own parts** (`OccluderFader.OwnSubmeshes`: as many as it has materials, never
  the combined mesh's count). Anything that draws a renderer part by part must ask this, not the mesh.
- **Only what is in the line of sight fades.** A prop of up to eight pieces fades whole; of a prop of many pieces
  under one collider, only the pieces the line from camera to character passes through.
- **A hard limit**: at most `OccluderFader.MostFadedRenderers` (48) pieces are faded at once; the rest stay solid.
  The glow's pass has its own (96 draws, 8 parts a prop) whatever the list holds.
- **A click goes through whatever is drawn faded** (`OccluderFader.IsFaded(hit)`): asked of the pieces drawn, at
  the point clicked, not of the collider the line of sight happened to meet, so a destination behind a faded
  wall can always be chosen.
- **`gm.sweep [metres]`** (`BenchCommands`) is how the next such place is found before a player finds it: it
  stands the character on a grid over every terrain (a spot every 50 m unless said), looks four ways from each,
  and writes `sweep_<date>_<time>.txt` beside the performance logs: the costliest views for the graphics card
  and by set-pass calls, looked at a second time for longer (a view can be caught while something loads; a place
  that costs is costly both times), then every view. After the fix, in the Editor: 333 spots, 1,332 views, 185
  of them with something faded; usual view 4.2 ms and 208 set-pass calls, most set-pass calls anywhere 249.
  Run it after anything is added to the world or to what is drawn, and in a build on the slowest PC to hand.
- The log reader's sections show a place like this as a section that fails with the time in
  `TimeUpdate.WaitForLastPresentationAndUpdateTime` (the main thread waiting for the picture) and the `S` lines'
  set-pass column jumping: the camera's x and z are on the same lines.

### A player appearing (12 Oct 2026)

The frame hosting starts on was 175 to 258 ms in the first two builds, and it is the frame a character is made on,
so a part of it is paid by everyone near whenever any player appears. Taken apart in the Editor (ending the session
and hosting again with the Profiler on): 229 ms and 31 MB handed out.

- **Done.** Every character's `SkinnedMeshMerger` had six megabytes of scratch lists of its own, and
  `CharacterAppearance.Awake` made the merger twice (once to look the bones up by name): 43 ms and 12 MB for each
  character that woke, a player or a doll. The scratch is now one set for the whole game (every merge clears it
  first; merges run one at a time on the main thread) and the merger is made once (`SetReference`). After: the
  appearance's waking is gone from the frame and the frame hands out 7.8 MB. And `ProgressionHUD` draws its two
  windows for the first time on the two frames after the player arrives, not on that one.
- **Left**, in the Editor's numbers: making the player prefab's copy 50 to 100 ms (1,373 managed objects are read
  back for it: the prefab is large), 65 ms inside the session's start that the Profiler does not name, the whole
  interface restyled when the local player's faction is known 26 ms, the old avatar letting go of the interface
  17 ms, the two mesh merges 12 ms. The frame is still about 180 to 200 ms there. The next step is the prefab's
  size, and it has not been taken.

### The world, and what draws it (10 Oct 2026)

Zone 01 is one scene: a 1 km terrain, about 8,500 mesh renderers (3.3 million triangles of static geometry),
6,500 colliders, 270 animators, 220 particle systems, 77 lights. The **GPU Resident Drawer is on** (Instanced
Drawing, Forward+, SRP Batcher, BatchRendererGroup variants kept) and holds about 8,400 of those renderers, the
static-batched ones included; nearly everything in the world is drawn with Synty's Shader Graph shaders, which it
accepts. Its GPU occlusion culling is off: the camera looks down, so little hides behind anything. A frame draws
0.4 to 1.8 million triangles in 240 to 300 set-pass calls. Nothing in the world has an LOD group.

## 8. Roadmap

- ~~APV lighting scenarios (Day/Night)~~: built (section 3).
- **Pre-server rendering pass** (owner, 15 Oct 2026; before online Phase 1, starts on the owner's go; each change
  measured with `gm.bench` per preset): fog and lamp-cone quality follow the preset, cones obey the fog toggle and are
  off on Low; cones at half resolution with edge-preserving blur; cascade splits and soft-shadow quality per preset;
  additional-light shadows off on `PC_RPAsset` if nothing uses them; fog voids in Crew Halls and homes; fix
  `VolumetricAtmosphereDriver`'s never-true "Mobile" check and its stale Overcast comment.
- **Ultra-only extras to try**, kept only within budget: lamp-cone occlusion near the camera, fog glow round lamps
  (`FogPointLight`), a finer raymarch.
- **Dynamic GI**: evaluated 2026-10-02 (the "Fast Dynamic GI" asset, `_ThirdParty/GI`) against APV and **rejected**.
  An A/B in Zone01 roughly doubled frame cost (+2–5 ms at street and vista views) and added no visible bounce, because
  its SDF tracing needs Read/Write enabled on every Synty mesh (memory cost, importer churn). APV + sky occlusion stays.
  Revisit only as an optional Ultra tier.
- **Interior lights** can reuse the street-lamp pattern: Baked light (APV Night scenario) + optional VolumetricLight.
- **Screen-space reflections**: URP has none built in, so it would be a custom Render Graph pass. Wet streets after rain
  would pair with the rainbow and moisture state.
- **Auto-exposure / eye adaptation** for interiors.
- **Shadowmask mixed lighting** for interiors once indoor zones exist: crisp baked shadows far away, realtime up close.

### Showroom scenes (character creator, character select)

The front-end scenes that show a live character are studio sets, not world scenes. They have no sky, no day cycle and
nothing baked, and they are generated (`FrontEndBuilder`), so tune the numbers there.

- Camera on **`UIPreview_Renderer`** (the equipment page's renderer): comic outline and Ben-Day dots, without the
  world's sky-driven passes. SMAA, like the world camera.
- **One shadowed directional key light** (warm white, from the front left), as everywhere else. Up to three unshadowed
  realtime spots shape the figure: fill (front right), rim (behind left) and kicker (behind right, low).
- **The faction lights the set.** Rim takes the faction's accent colour and fill its second colour (lifted towards
  white); with no character shown they fall back to a neutral cool pair. The key never changes, so skin and gear
  colours read the same for every faction.
- Fixed trilight ambient (cool sky, dark ground). No fog, no skybox, no reflection probe.
- Backdrop: `KryticalNova/UI/Showroom Backdrop`, an unlit quad that prints a faction-coloured burst with the same
  screen-aligned dots as the comic pass. The pedestal is `Comic Toon Lit` and very dark, because three lights meet on it.
- Menus without a 3D scene (Title, Faction Select, Loading) draw the same dots in uGUI (`KryticalNova/UI/BenDay`), and
  every scene change is a Ben-Day wipe in Ink (`KryticalNova/UI/BenDayWipe`), never a fade to black. The one way into
  the world is its own: the Loading page breaks into comic panels (`KryticalNova/UI/PanelShatter`, one full-screen
  uGUI draw with a loop of at most eight panels a pixel, for about 1.25 s; not measured against a budget yet).

## Changelog

- **2026-10-15 (docs only)**: the owner's research on volumetric light, fog, mist and shadows checked against the
  game: graphics tiers rule and preset table (section 1), shadow and spell-light rules (3), volumetrics as built and
  the research verdicts (3b), the pre-server rendering pass and Ultra extras (8). Fixed the stale Mobile depth/opaque
  line and the Day/Night APV roadmap item (it is built).

- **2026-10-11 (performance, branch `perf/top-tier`)**: what the first real performance log led to, section 7:
  an IL2CPP build option, the allocation finder and the per-frame garbage it found removed, scripts compiled ahead
  behind the Loading page, the HUD redrawn in pieces, saves written on a thread of their own, a frame cap that
  follows the screen, the log's steps, cameras and sections, the `gm.bench` benchmark; and no camera for the
  minimap or the portrait.

- **2026-10-10 (grass)**:
  - The wind no longer makes the grass jump (wave phase rule, 6d).
  - Grass is generated only for the ground the camera sees; characters bend it through a trample map and it stays
    pressed for a moment behind them; the rule for Overworld and Town lawn grass is written down (6d).
- **2026-10-11**:
  - Glow halo: no light through props the camera fades or through bodies and walls (the Block pass, conservative
    rasterisation off, `depthFade` 2 cm), and the glow leak check that measures it (6e).
  - A run's dust and smear on every start and every new destination; the kick of the view stays one per burst.
- **2026-10-10**:
  - Glow halo: embers (Perlin noise) for the 120 % Temper band; an Epic at 120 % is red (6e).
  - A Nova hides the characters, pets and effects behind it: `ActorDrawOrder`, `NovaMask.shader`, `NovaCull` (6e).
  - Performance readout (Settings ▸ Graphics): frame times, what is drawn, memory, connection. Frame Timing Stats switched on in Player Settings for it.
  - The frame rate target is written down with what was measured against it; the readout shows the slowest frame of
    the last minute, how many frames fell under 50 a second, and the machine (section 7).
- **2026-10-11**: a built game writes a performance log beside its exe: every slow frame with where its time went
  and what the game had just done (section 7).

- **2026-10-08 (polish pass: interface)**:
  - One interface skin per faction, in an asset; the game's style sheets are generated from it (section 6i,
    `Design/14_interface_skins_menus_settings.md`).
  - The title flow is built once per skin from each faction's own Synty menu pack and wears one skin a run.
  - The menus are the pages of a comic book: a MegaBook leaf carries the screen that is left and turns away, shaded
    with Ben-Day dots (shader `KryticalNova/UI/Comic Page`); the book's cover opens the menus.
  - Menus and Settings answer what is done in them with Feel (`PFX_MenuFeel`, the Menu Punch feedback).

- **2026-10-08 (polish pass: comic effects)**:
  - Hits look like the weapon that dealt them: a mark, a font and three words for each of the nine weapon classes,
    one colour ramp for each kind of hit, words of their own for a death, the forge, a level gained, a find and a
    pet's evolution, and a look kept for a critical hit on another player (section 6h, `Design/13_comic_effects.md`).
  - Comic lettering has a layer and a pass of its own, after the glow (section 2, 6c).
  - The effect pool is filled before a fight and takes an effect back when its particles are done; effects of
    fights the local player is no part of are never created during one.

- **2026-10-08 (polish pass: pets)**:
  - Pets react to what their owner does in a comic speech bubble (end of section 6g): words for the Drone Core,
    emoji for the animal pets.

- **2026-10-08 (polish pass: icons)**:
  - Skill icons, and the icons of items that have no model, now come from the CraftPix icon packs, redrawn in the
    comic style by `ComicIconTreatment` and handed out by the icon library (section 6g). Every skill has an icon of
    its own (before, the nine classes shared ten). The Synty-glyph icons and their bakers (`SkillIconBuilder`,
    `ComicIconBaker`) are gone, and so is the procedural jewellery `ItemIconCapture` used to render.

- **2026-10-08 (polish pass: the smear)**:
  - Motion smear on a run's take-off and on the throw of a death (section 6f): the smear package's shader as a second
    skin over the body, played by a FEEL feedback, tuned in `SmearSettings`. A walk, standing still and being put
    somewhere never smear.
  - A critical kill of another player hangs the thrown body in slow motion for a blink, on the killer's screen only
    (section 6f).

- **2026-10-07 (polish pass: the glow)**:
  - The Temper and pet glow is rebuilt as a screen-space halo (section 6e, frame order 6b): the stacked hull
    (`TemperGlow.shader`, its material and the sparkle shell) is gone.
  - Only part of a weapon glows now (`GlowZone`, `WeaponGlowZone`), and the dark parts keep the light off the air
    beside them.
  - Epic and above add sparks, lightning and flame in the Temper band's own colour, drawn in the halo pass.
  - Budget row added: Glow Halo.

- **2026-10-04 (front end and factions)**:
  - Showroom lighting rules added (section 3): one shadowed key, faction-coloured rim and fill, dotted burst backdrop.
  - Two uGUI shaders share the comic dot grid with the world: `UI/BenDay` (dot-shaded fills) and `UI/BenDayWipe`
    (scene transition).
  - Gear icons are rendered in their own pack's palette (`ItemIconCapture`), so a Null chest piece is not drawn in
    Hegemony colours.

- **2026-10-03 (pass 4)**:
  - Moon phase mask darkened: earthshine 0.12 → 0.015, dark side opacity 0.55 → 0.95, halo no longer washes over the
    unlit part of the disc.
  - Cartoon dust puffs (run, jump, landing) are **opaque** flat-coloured meshes (`M_ComicPuff`), not translucent
    sprites: they write depth, so the outline pass inks them like any other comic shape. Their colour comes from the
    ground they are kicked off (`GroundSurfaces`: paving, road, grass, dirt, rock), never from the grass tint itself.
  - Area attacks draw their reach on the ground (`PFX_Area_Ring`, coral, alpha-blended, no emission).

- **2026-10-03 (moonlit nights)**:
  - Moon key light raised (0.4 → 0.75 at midnight) with a phase curve (`moonPhaseLight`) instead of a fixed 25–100 % lerp.
  - Night ambient lifts with the lit fraction; thin volumetric night haze on dry nights; faint moon shafts through the
    SunShafts pass (source and tint now follow the key light).
  - GameFeel VFX: every moment is a FEEL player (camera half for the local player, world half with pooled particles
    for everyone). Buff auras, run / airborne loops and the blade trail ride on the character (`ActorVfx`); hits use
    Cartoon FX impacts and comic text; the blade trail uses an All In 1 Shader Nodes slash material. Emissive VFX stay
    inside the bloom budget in §4.

- **2026-10-02 (pass 4)**:
  - Volumetric Fog 2 now renders **after transparents** (feature `renderPassEventOrder` 500): the water surface is
    transparent and used to paint over any mist above it.
  - Water mist: box volumes (river strips are real strips), visible to 650 m, lit by the sky ambient with a brightness
    floor (the key light is ~0 through dusk) and never tinted by the weather fog colour.
  - Edit mode has no fog: `EnvironmentPresenter` only enables distance fog in Play Mode (`previewFogInEditMode` to
    opt in) and the fog volumes are saved inactive.
  - Grass mask ignores transparent renderers (lamp light cones were erasing the plaza lawns).
  - A rainbow needs a low sun above the horizon even when forced from GM tools.

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
