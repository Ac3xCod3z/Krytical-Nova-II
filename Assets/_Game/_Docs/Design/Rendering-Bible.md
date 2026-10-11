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
| `_KN_WindPhase` | x = wind strength summed over time, y = the same one frame ago, w = 1 once published. What a foliage wave's speed-up with the wind is multiplied by (`KNWindPhase()`) |
| `_KN_WindDrift` | xy = how far the grass's gust cells have drifted downwind (`KNWindDrift()`), zw = one frame ago |
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
0.05 m, fade-out from 55 to 80 m, `halfResolution` on. PC renderer only (it needs the depth texture).

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
second ago: when a run takes off, and when the blow that kills throws the body. It is the smear package's own shader
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

**One take-off for a burst of orders (10 Oct 2026).** A new move order given while running, and a run that stops and
starts again at once, both ask for the take-off again. `GameFeelPresenter.RunStarted` plays it (dust, smear, and on the
player's own screen the 4 degree kick of the view) only when `runTakeOffQuietSeconds` (1 s) have passed with no order
and no restart for that character. Before, clicking about quickly kicked the view and smeared the body every 0.4 s:
measured over 12 s of rapid clicks the field of view was changing for a quarter to a third of all frames.

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
| Live minimap | ~8 renders/s, 512² | opaque-only Forward+ renderer, no shadows/post/features |
| Menus' page turn | nothing while a screen is at rest; during a turn one picture of the screen and one mesh drawn by one camera for 0.7 s | not measured against a budget yet |
| Comic marks and words | nothing created in a fight, 0 B GC a hit, < 1 ms a frame in a crowd | measured in the Editor at 30 hits a second by other players: +0.6 ms mean frame, 0 effects created, 0 B allocated by the layer's own calls; the pool is filled when a scene starts (about 86 MB of sleeping comic effects by the Editor's count) |
| Glow Halo | < 0.5 ms GPU, < 0.3 ms CPU with 40 glowing weapons | measured 0.22 ms GPU / 0.18 ms CPU with 42 halos (flame, lightning, sparks) at 925×1067 in the Editor, 0.07 / 0.06 ms with 2; the flood runs at half res inside the glowing things' screen rectangle only; nothing glowing = no pass |

Measure with the Frame Debugger and Profiler (GPU module) in a build, not the Editor. Look for each feature's
`ProfilingSampler` name ("Comic Style", "Sun Shafts", "Glow Halo", "Comic Lettering").

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

- **2026-10-10**:
  - Glow halo: embers (Perlin noise) for the 120 % Temper band; an Epic at 120 % is red (6e).
  - A Nova hides the characters, pets and effects behind it: `ActorDrawOrder`, `NovaMask.shader`, `NovaCull` (6e).
  - Performance readout (Settings ▸ Graphics): frame times, what is drawn, memory, connection. Frame Timing Stats switched on in Player Settings for it.

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
