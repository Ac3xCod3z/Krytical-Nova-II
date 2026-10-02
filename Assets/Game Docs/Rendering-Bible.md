# Krytical Nova II — Rendering Bible

The single source of truth for how the game is lit, post-processed, and kept fast. **Update this file in the same
commit as any rendering change** (pipeline assets, renderer features, sky/post shaders, lighting setup, volume
profiles). Add a line to the [Changelog](#changelog) every time.

Look target: **comic-book stylized realism**. Ink outlines and halftone shadows on a believable, time-of-day lit world.
The sky is painterly but physically motivated (real rainbow geometry, real sunset colours, real moon phases).

---

## 1. Pipeline per platform

| | PC (`Assets/Settings/PC_RPAsset.asset`) | Mobile (`Mobile_RPAsset.asset`) |
|---|---|---|
| Rendering path | **Forward+** (`PC_Renderer`) | Forward (`Mobile_Renderer`) |
| Depth / opaque texture | on / on | off / off |
| HDR | on | on |
| MSAA / render scale | off / 1.0 | off / 0.8 |
| Light probes | **Adaptive Probe Volumes** | legacy light probes |
| Renderer features | SSAO, ComicStyle, SunShafts | none |
| Main light shadows | 4096, 4 cascades, 50 m | 1024, 1 cascade |

**PC-only effects** (they need the depth texture): comic outlines and shadows, SSAO, sun shafts. Every new effect must
degrade cleanly on Mobile, either by skipping its pass or with a cheaper fallback. The sky shader has a `_KN_SKY_LOW`
keyword for Mobile.

Why Forward+: per-tile light culling, so dozens of hand-placed realtime lights don't hit the 8-lights-per-object limit.

## 2. Frame order (PC)

1. Depth/normals prepass → opaques (ComicToonLit, Synty)
2. **SSAO** (DepthNormals, intensity 0.4, radius 0.3)
3. **Skybox**: `Skybox/KryticalNova/Stylized Sky`. Sky gradient, stars, sun/moon art, clouds, rainbow.
4. Transparents (weather particles, VFX)
5. **ComicStyle** (BeforeRenderingPostProcessing): cast-shadow restyle, cloud shadows, Sobel outlines. Skips sky pixels.
6. **SunShafts** (BeforeRenderingPostProcessing + 1): god rays, added before post so Bloom catches them.
7. URP post: **Bloom** → **Tonemapping (Neutral)** → **Color Adjustments** (runtime) → **Vignette** → **Lens Flare** (data-driven, on the sun light).

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

Driven by `EnvironmentPresenter` (`Assets/Scripts/World/Presentation/`). Tuning lives on `DayCycleProfile`
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
| `_KN_Cloud*` | cloud field, shared with ComicPost and SunShafts (`SkyCommon.hlsl`) |

### Sun
- **Art treatment**: the centre is washed out by its own light (`_SunGlare*`), light bleeds over ink lines
  (`_BodyInkLift`), colour and contrast are softened, and the art hazes into the sky near the horizon.
- **Spikes**: barely visible (`_SunRayVisibility` 0.3), with a heat shimmer from the cloud noise
  (`_SunShimmerAmount/Speed/Scale`) and a slow flicker. The disc never wobbles, only the rays.
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

- Scene volume: **Global Volume** → `SampleSceneProfile` (Bloom, Neutral tonemapping, Vignette 0.2).
- **Never write the profile asset at runtime.** `EnvironmentPresenter` writes Color Adjustments (saturation, contrast,
  colour filter) on `postVolume.profile`, the runtime instance.
- Renderer features re-publish their serialized settings every frame. Modulate them only through their runtime classes:
  `ComicStyleRuntime` (shadow tint, opacity, dots) and `SunShaftRuntime` (shaft strength).
- New screen-space features: Render Graph only (`RecordRenderGraph`). Skip Preview and Reflection cameras. Read
  `cameraDepthTexture` via `ConfigureInput`.

## 7. Performance budgets (PC, 1080p)

| Item | Budget | Notes |
|---|---|---|
| Sky shader | < 0.15 ms GPU | 4 texture reads in clouds, + 3 for the bodies |
| ComicStyle pass | < 0.6 ms | full-res Sobel + shadow-map reads |
| SunShafts | < 0.25 ms | quarter res, 1 + 24 + 24 samples; skipped when strength is 0 or the sun is off-screen |
| SSAO | < 0.5 ms | |
| Lens flare | ~0.05 ms | 6 elements, depth occlusion |
| Environment CPU | < 0.1 ms, 0 B GC | presenter, flare cloud sample |

Measure with the Frame Debugger and Profiler (GPU module) in a build, not the Editor. Look for each feature's
`ProfilingSampler` name ("Comic Style", "Sun Shafts").

## 8. Roadmap

- **APV lighting scenarios** (Day/Night): turn baked lamps off at noon and on at night, blended by the clock.
- **Surface Cache GI**: fully dynamic bounce lighting for high-end PCs. Experimental and expensive. Evaluate only as an
  optional Ultra setting.
- **Volumetric fog / local fog volumes**: give the sun shafts something to scatter in at ground level.
- **Screen-space reflections**: URP has none built in, so it would be a custom Render Graph pass. Wet streets after rain
  would pair with the rainbow and moisture state.
- **Auto-exposure / eye adaptation** for interiors.
- **Shadowmask mixed lighting** for interiors once indoor zones exist: crisp baked shadows far away, realtime up close.

## Changelog

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
