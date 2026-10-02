# Stylized Sky, Day/Night, Clouds & Weather — Plan

> Current rendering rules, sky globals, post stack and budgets live in **[Rendering-Bible.md](../Game%20Docs/Rendering-Bible.md)**. Keep that file up to date; this plan is the original design record.

## Context
Krytical Nova II (Unity 6000.6.3f1, URP 17.6, NGO 2.13, PC-first, comic-book art style) has no sky or weather system. `SampleScene` still uses the default procedural skybox, Skybox ambient, fog off, and a static white directional light. The goal is a **very cheap**, art-directable system with:
- a skydome with a stylized day/night cycle. The Synty Sci-Fi `SimpleSky_01.png` is a **color reference only**: its colors seed the default palette and the texture isn't sampled at runtime.
- **soft stylized clouds** whose coverage varies continuously from clear, through few and partly cloudy, to overcast.
- weather: clear → overcast, **rain + thunderstorm, fog/mist, snow/blizzard**, built from the Synty PolygonParticleFX / PolygonGeneric effects.
- server-authoritative and synced across clients with almost no bandwidth.
- a default day length of **24 min** for testing. It will be lengthened later and exposed to a future **GM mod** (see the GM note at the end).

## Core performance decisions (the "why")
1. **The sky is a skybox material, not a mesh dome.** URP draws the skybox once, after opaques, so hidden pixels are rejected by depth and there's no overdraw. `ComicPost.shader` skips far-plane pixels (`IsSky()`), so the sky gets no ink outlines. A depth-writing Synty dome mesh *would* get outlined.
2. **Clouds are drawn inside the sky shader.** They're a planar-projected, wind-scrolled noise texture with 2–3 samples per visible sky pixel. There are no cloud meshes, particles, sorting or overdraw. Coverage is a single float, so "anything in between" comes for free.
3. **Only one directional light ever.** It is the sun by day and the moon by night. Its intensity crosses zero at the horizon, so swapping its direction is invisible. There's never a second shadow-casting light.
4. **Ambient comes from a hand-built SH probe.** The script builds a `SphericalHarmonicsL2` from three palette colors and assigns `RenderSettings.ambientProbe` (about 10 Hz, only when it changed). It never calls `DynamicGI.UpdateEnvironment`. `ComicToonLit`'s `SampleSH()` and Synty/URP shaders pick it up automatically. There are no realtime reflection probes; environment reflection intensity is set low.
5. **Cloud shadows on the ground cost one texture sample inside the existing ComicPost pass.** That pass already rebuilds world position for every pixel. No light cookies and no extra pass.
6. **Lightning uses no lights.** The flash is a shader uniform plus a brief boost to the main light's intensity. The strike timing is deterministic from a shared seed and server time, so every client sees the same strike with zero network traffic.
7. **Precipitation is a camera-following rig.** It uses a small radius (15–20 m), **no particle collision**, capped particle counts, no shadows and no lights. When the weather is clear it is stopped and deactivated, so it costs nothing.
8. **The network sends one small struct, and only when the weather changes.** Every client computes the time of day from `NetworkManager.ServerTime`. Coverage drift and lightning come from a shared seed.
9. **No per-frame allocations.** Shader property IDs are cached, gradients are evaluated once per frame, and work is throttled where it's invisible.

**Budget targets (PC, 1080p):** sky shader under 0.15 ms GPU; cloud-shadow term under 0.05 ms; environment CPU under 0.05 ms per frame with 0 B GC; rain or snow rig under 0.2 ms CPU with at most about 3k live particles; network about 40 bytes per weather change.

---

## 1. Data (ScriptableObjects in `Assets/GameData/World/`)
Follow the existing pattern: `[CreateAssetMenu(menuName = "Krytical Nova/World/...")]`, as in `Assets/Scripts/Progression/Data/*.cs`.

- **`DayCycleProfile`** — `dayLengthSeconds = 1440` with a `// TODO(GM)` tooltip, `startTimeOfDay01`, sun path (`sunAzimuthOffset`, `sunTilt`), and moon offset.
  - Gradients over 0–1 of the day: `skyZenith`, `skyHorizon`, `skyGround`, `sunColor`, `moonColor`, `ambientSky/Equator/Ground`, `fogColor`, `comicShadowTint`.
  - Curves: `sunIntensity`, `moonIntensity`, `shadowStrength`, `starVisibility`, `comicShadowOpacityMul`, `comicDotStartOffset`.
  - Sun and moon disc size and glow.
  - The default day colors are picked from `Assets/Synty/PolygonSciFiCity/Textures/Misc/SimpleSky_01.png`: blue zenith, pale-cyan horizon, warm/pink tones for dawn and dusk.
- **`WeatherProfile`** — fields:
  - `coverageMin/Max`, cloud density, softness, darkness and tint, cirrus amount.
  - Sky desaturation and darkening.
  - Multipliers for `sunIntensityMul`, `ambientMul` and `shadowStrengthMul`, plus `cloudShadowStrength` (set high when partly cloudy and about 0 when overcast, so an overcast sky doesn't paint the whole screen in shadow).
  - `fogDensity` and fog tint; wind direction and speed.
  - `precipitation` (None/Rain/Snow) with intensity; lightning interval range (0 = none).
  - Post saturation and contrast; `transitionSeconds`; `durationMin/Max`.
  - `next: List<{WeatherProfile, weight}>` (a Markov chain); optional ambience `AudioClip`.
- **`WeatherCatalog`** — an ordered list of profiles, so the network sends a `byte` index.
- **Assets:** `DC_Default`, `WeatherCatalog`, and the profiles `WP_Clear`, `WP_FewClouds`, `WP_PartlyCloudy`, `WP_MostlyCloudy`, `WP_Overcast`, `WP_Fog`, `WP_LightRain`, `WP_Rain`, `WP_Thunderstorm`, `WP_Snow`, `WP_Blizzard`.

## 2. Simulation and network (Runtime asmdef, namespace `KryticalNova.World`, `Assets/Scripts/World/`)
- **`WorldEnvironmentState`** (`INetworkSerializable` struct) — `double epochServerTime`, `float dayLengthSeconds`, `float timeOffset01`, `byte fromWeather`, `byte toWeather`, `double transitionStartTime`, `float transitionSeconds`, `int seed`.
- **`WorldEnvironmentNetwork`** (`NetworkBehaviour`, sealed, singleton pattern like `SpatialInterestManager`):
  - Holds a server-write `NetworkVariable<WorldEnvironmentState> netState`.
  - **Server:** runs the weather scheduler. When the current duration ends, it picks the next weather from the weighted `next` list with `System.Random(seed)` and writes the state. That happens rarely: minutes apart.
  - **Server-only API, ready for GM tools:** `SetTimeOfDay(float hours)`, `SetDayLength(float seconds)`, `ForceWeather(byte id, float transitionSeconds)`.
  - **Offline fallback:** when no NetworkManager is listening, it runs the same logic locally on `Time.timeAsDouble`. This matches the existing `OfflinePlayerHandoff` flow.
- **`WorldClock`** (static read API) — `TimeOfDay01`, `Hours`, `IsNight`, `SunDirection`, `MoonDirection`, and `event Action<int> HourChanged` for gameplay. Time of day = `frac((ServerTime.Time - epoch) / dayLength + offset)`. Re-sync is off (`EnableTimeResync: 0`), and drift over a session is well under a second, which is invisible here.
- **`WeatherBlend`** (struct plus a static evaluate function) — lerps every numeric field from the "from" profile to the "to" profile, smoothstepped by transition progress. Coverage = `lerp(min, max, slowValueNoise(serverTime / 300, seed))`, so clouds drift continuously and identically on every client.
- **`AlwaysRelevantNetworkObject`** (marker component). **Edit `Assets/Scripts/Networking/SpatialInterestManager.cs` `IsCullable()`** to skip objects that have it. Without this, the 75 m interest culling would hide world state from distant players.
- **Scene:** add a scene-placed `World Environment` GameObject with a `NetworkObject`, `WorldEnvironmentNetwork` and `AlwaysRelevantNetworkObject`. This is the same approach as the existing scene-placed "Combat Dummy".

## 3. Presentation (client only; skipped when `Application.isBatchMode` / `UNITY_SERVER`; a Host still runs it)
`Assets/Scripts/World/Presentation/`
- **`EnvironmentPresenter`** (one MonoBehaviour that drives everything). It holds references to the Directional Light, the sky material, the Global Volume and the profiles. Each frame it does the following:
  - **Light:**
    - Rotation from the sun or moon direction. Rotation updates only once the change exceeds about 0.05°, which reduces shadow swimming.
    - Color and intensity from the profile curves × the weather multiplier × the lightning envelope.
    - `shadowStrength` is set the same way.
    - The light swaps between sun and moon below -2° elevation.
  - **`RenderSettings`:**
    - `ambientProbe`, rebuilt from three colors at about 10 Hz when it changed.
    - `fogColor` and `fogDensity`, using Exp2 fog. `ComicToonLit` already uses `MixFog`.
    - `sun` is set to the Directional Light.
  - **Sky globals** (cached IDs, prefixed `_KN_`): `_KN_SunDir`, `_KN_MoonDir`, `_KN_SkyZenith/Horizon/Ground`, `_KN_SunColor`, `_KN_CloudParams` (coverage, density, softness, darkness), `_KN_CloudTint`, `_KN_Wind` (xy direction × speed, plus accumulated offset), `_KN_StarVisibility`, `_KN_LightningFlash`, `_KN_CloudShadow` (strength, plane height, scale).
  - **Post:** writes ColorAdjustments saturation, contrast and color filter on the Volume's *runtime instance* profile. It never edits the asset, and it doesn't blend extra volumes.
  - **Comic style:** sets `ComicStyleRuntime` overrides (night gives weaker shadow opacity and a deeper blue tint; overcast fades cast shadows).
  - Sets the global keyword `_KN_SKY_LOW` when the Mobile quality level is active.
- **`PrecipitationController`**:
  - Keeps the rain, snow and mist rigs at the camera's position, snapped and XZ only.
  - Sets `emission.rateOverTimeMultiplier` from intensity and the wind through `velocityOverLifetime`.
  - Stops a rig at zero intensity and deactivates it once its particles have died.
  - Casts one raycast up every 0.25 s; if the player is under a roof, emission fades out.
- **`LightningController`**:
  - Strike times come from `hash(seed, floor(serverTime / slot))`. Positions are fixed in world space, 120–400 m from the zone origin and biased toward the camera. Players near each other see the same strike.
  - Pools 2 strike VFX instances. The flash envelope feeds the presenter.
  - Thunder audio is delayed by distance.
- **Ambience audio:** two AudioSources crossfaded by the presenter, using the profile clips. Optional; skipped if no clip is assigned.

## 4. Sky shader — `Assets/Shaders/Sky/StylizedSky.shader` + `SkyCommon.hlsl`
Hand-written HLSL in the style of `Assets/Shaders/Comic/ComicToonLit.shader`. It is a `Skybox/KryticalNova/Stylized Sky` shader with one material, `Assets/Materials/Sky/M_StylizedSky.mat`.
- **Gradient:** ground → horizon → zenith, using view-direction y with power falloff. The horizon band blends toward `unity_FogColor` so fog meets the sky without a seam.
- **Sun:** a soft-edged disc plus a glow lobe, both HDR so the existing Bloom gives a gentle halo.
- **Moon:** a disc with a crescent mask from a sun-relative offset.
- **Stars:** procedural hash on a cube-face cell grid with twinkle. Cost is ALU only, multiplied by star visibility × (1 − coverage), and skipped entirely when visibility is 0.
- **Soft clouds:**
  - Projection: `uv = (camPos.xz + dir.xz / max(dir.y, ε) · H) · scale + windOffset`. Base sample from R, detail from G at a different scroll speed.
  - Density: `smoothstep(1 − coverage − softness, 1 − coverage + softness, n)`.
  - Soft painted shading: one extra sample offset toward the sun gives a density difference, which lerps between the lit tint and the shadow tint, plus a silver lining near the sun.
  - Clouds fade near the horizon, darken with weather darkness, flash with lightning, and hide the sun or moon by their alpha.
  - The cirrus layer (B channel, stretched) is omitted under `_KN_SKY_LOW`, which also drops the lighting sample. That leaves 2 variants in total.
- `SkyCommon.hlsl` holds the shared `KN_CloudDensity(float2 worldXZ)` used by `ComicPost`, so ground shadows line up with the clouds in the sky.
- **Noise texture** `Assets/Textures/Sky/T_CloudNoise.png`: 256², RGBA, tileable, linear, Repeat, mips, BC7, about 90 KB. It is baked once by an editor tool, `Assets/Editor/World/CloudNoiseBaker.cs` (Perlin-Worley FBM: R = shapes, G = detail, B = cirrus streaks), using the existing `KryticalNova.Editor` asmdef. There's no runtime noise generation.

## 5. Comic pipeline edits
- **`Assets/Scripts/Rendering/ComicStyleFeature.cs`:** add a static `ComicStyleRuntime` class with `shadowOpacityMul`, `shadowTint` + `shadowTintWeight`, `shadowBrightnessMul` and `dotStartOffset`, all defaulting to identity. `PushGlobals()` and `ComicPass.Setup()` apply them. This is needed because the feature re-pushes its serialized settings every frame and would overwrite direct global writes.
- **`Assets/Shaders/Comic/ComicPost.shader` `ApplyComicShadow()`:** multiply in a **soft cloud-shadow term**, sampled once at the center pixel and projected along the sun direction to the cloud plane. It is not added to `ShadowAt()`, so it isn't sampled 5× for the ink rim, and soft cloud shadows don't get ink rims. The term is gated by a uniform branch: `_KN_CloudShadow.x > 0`.

## 6. Weather VFX (URP-safe copies; Synty pack folders are never edited)
PolygonParticleFX materials use **legacy Built-in particle shaders**, so they render magenta in URP. Make tuned prefab variants in `Assets/Prefabs/Environment/Weather/`, using the PolygonGeneric `Generic_ParticlesUnlit` materials (already URP) or new materials on `Universal Render Pipeline/Particles/Unlit`:

| Variant | Source | Changes |
|---|---|---|
| `PFX_Rain` | PolygonParticleFX `FX_Rain_01` | max 3000, radius 18 m, collision off, stretched billboard, no shadows |
| `PFX_RainSplash` | collision splash sub-emitter → standalone | ≤150 particles on a disc at player foot height, no collision (faked) |
| `PFX_Snow` | PolygonParticleFX `FX_Snow_01` | max 2000, collision **off**, noise kept at low quality |
| `PFX_Blizzard` | PolygonParticleFX `FX_Blizzard_Snow_01` | max 3000; drop the huge mesh "cloud" emitter (heavy overdraw); fog does the haze |
| `PFX_Mist` | PolygonGeneric `FX_Fog_01` | ≤30 soft particles, soft particles + camera fade, shadows **off** |
| `PFX_LightningStrike` | PolygonParticleFX `FX_LightningStrike_01` | remove the Lights module (up to 20 lights) and the Point Light |

The big mesh fog prefabs, `FX_Fog_Big_01` and `FX_Fog_Small_01`, are deliberately not used; distance fog plus the sky horizon blend covers them.

## 7. Scene setup (`Assets/Scenes/SampleScene.unity`, via the unity-cli skill in the live Editor rather than YAML edits)
- Skybox = `M_StylizedSky`; Sun Source = Directional Light; fog on (Exp2); ambient handled by the presenter; environment reflection intensity about 0.3.
- Add the `World Environment` (networked) and `Environment Presenter` objects plus the weather rigs.

## 8. Dev testing commands (also the seed of the GM mod)
QFSW Quantum Console is already in `Assets/Plugins`. Add `[Command]`s in `Assets/Scripts/World/WorldEnvironmentCommands.cs`: `time.set <hours>`, `time.daylength <sec>`, `time.scale`, `weather.set <name> [transition]`, `weather.list`. They call the server API and work only on the host or server. This is a dev stand-in that the GM mod will later wrap with permission checks.

## Implementation order
1. Data SOs, the sim/network layer and the `SpatialInterestManager` opt-out, plus console commands.
2. Sky shader, noise baker and the presenter for light, ambient and fog: the day/night cycle running.
3. Clouds in the sky, weather blending, ComicPost cloud shadows and the `ComicStyleRuntime` hooks.
4. Weather VFX variants, precipitation and lightning controllers, audio.
5. Profile tuning pass, Mobile tier check, profiling.

## Verification
- **Visual:** in Play Mode, scrub `time.set 0…24` and step through every `weather.set`. Check that:
  - there are no outlines on the sky;
  - fog meets the horizon without a seam;
  - the sun/moon swap pops neither shadows nor light;
  - night stays readable;
  - an overcast sky doesn't flood the screen with comic shadow;
  - cloud shadows line up with the clouds in the sky.
- **Performance:**
  - Profiler and Frame Debugger: the skybox is drawn once; no extra lights or passes; presenter + controllers under 0.05 ms; **0 B GC.Alloc per frame**.
  - GPU profiler: the sky stays within budget at 1080p in the Thunderstorm and Blizzard worst cases.
  - Switch to the Mobile quality level and confirm the `_KN_SKY_LOW` path.
- **Network:**
  - Run a Host plus a Client (build + Editor). Time of day matches within a frame or two, the same weather blends on both, and lightning strikes at the same moment.
  - Late-join a client mid-transition; walk more than 75 m away and confirm the environment state stays synced (the interest opt-out works).
  - A dedicated server or batchmode run doesn't touch rendering.
- **Offline:** play with no network session and confirm the cycle and weather still run.

## Notes to record after approval
- **GM mod (near future):** the 24-minute day is a *testing* value; lengthen it later. Expose `SetTimeOfDay`, `SetDayLength` and `ForceWeather` (plus pausing time and locking weather) as GM commands with permission checks, wrapping the dev console commands above. Put `// TODO(GM)` comments at the API and in `DC_Default`, save it as a project memory, and copy this plan to `Assets/Plans/sky-weather-system-plan.md` to follow the existing plans convention.

---

## Implementation status (2026-09-30)

Implemented and verified in the Editor (offline and as local Host). Everything is regenerable from the menus under **Krytical Nova → World**:
- **Setup Sky + Weather (All Steps)** bakes the cloud noise, creates any missing profiles and the sky material, builds the weather VFX and wires up the open scene. Re-running it never overwrites profiles you've tuned.
- The individual steps are also available on their own: **Bake Cloud Noise Texture**, **Create Default Assets**, **Build Weather VFX Prefabs**, **Setup Scene Objects**.

Paths that differ from the plan above:
- The sky material is `Assets/GameData/World/Materials/M_StylizedSky.mat`.
- The noise texture is `Assets/GameData/World/Textures/T_CloudNoise.png`.
- URP particle materials are in `Assets/GameData/World/Materials/VFX/`.

Measured: all five components together cost about 0.003 ms of CPU per frame with 0 B of GC. The shadow pass turns off automatically in overcast, rain and storm weather.

Where to tune:
- Time-of-day look: `DC_Default` (gradients and curves).
- Each weather's look and its Markov chain: `WP_*`.
- Cloud scale, height, detail and shadow softness: **Environment Presenter** component.
- Rig heights, look-ahead, wind coupling and shelter raycasts: **Precipitation Controller**.
- Strike placement and flash: **Lightning Controller**.
- Edit-mode preview: set **Preview Hours** and **Preview Weather** on the Environment Presenter.

Testing in Play Mode:
- Quantum Console commands `time.set`, `time.get`, `time.daylength`, `time.scale`, `weather.set`, `weather.lock`, `weather.list`. They need a QC prefab in the scene.
- Without the console, use the **Debug** context menu on World Environment.

Not yet verified:
- A remote client on a second machine or instance.
- A dedicated server build.
- The Mobile quality tier on device.

The ambience and thunder `AudioClip` fields are empty until sounds are added.

**TODO(GM):** the 24-minute day is a testing value. The GM mod should wrap `WorldEnvironmentNetwork.SetTimeOfDay / SetDayLength / SetTimeScale / ForceWeather / SetWeatherLocked` with permission checks.
