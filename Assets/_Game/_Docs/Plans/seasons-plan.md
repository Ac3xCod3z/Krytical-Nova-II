# Seasons plan

**Status: proposed 13 Nov 2026, waiting for the owner's review.** Research and design notes only. No code is written
until the owner has read this and answered the picks at the end (owner's DAY list, 13 Nov: "18-21: research + design
notes for the owner's review only; code comes after he reviews").

Row 41 of `../Reference/12Sky/01_Systems_Build_List.md` (World: day cycle, weather, zones). Owner's DAY list item 21.
Related: `sky-weather-system-plan.md`, `../Design/Rendering-Bible.md` sections 5, 6d and 7.

## What it is for

The world turns through four seasons, two real weeks each, so a year is eight real weeks. Each season has its own
weather mix (spring showers, clear summers with storms, windy and foggy autumns, snowy winters), its own daylight
(short winter days) and its own look: fresh spring grass with flowers, golden summer fields, orange and bare autumn
trees, frosted winter. Snow piles up on the ground and on roofs while it snows and melts after; footprints and trails
stay in it for a while. Some zones are always snowy, but still get the other effects of the season. The server decides
the season and every client sees the same one. A GM can switch it from the GM window, the console or the server page.

## Risk

**Low** by `../Guides/Review-Rules.md`: a broken season makes the world look wrong; no player can lose items, Creds or
a save, and nothing is saved per player. Two things raise care to **medium** for review:

- It touches the sky and environment presentation, which once shipped black in every client (build 12, 25 Oct 2026).
  The safety rule below is part of the plan, not an afterthought.
- It changes the shaders of the ground, the buildings, the grass and the trees, which are on every screen. A slow or
  broken shader is felt everywhere, so each shader step is measured in the performance log.

## 1. What exists today (read 13 Nov 2026; line numbers checked)

**Clock.** `World/Data/DayCycleProfile.cs`: `dayLengthSeconds = 3600` (L18; one real hour a day, owner 10 Oct),
`startTimeOfDay01 = 0.3` (L20), sun path `sunAzimuth -30` / `sunTilt 35` (L24-26). Every gradient and curve is keyed
on a fixed day: 0.25 sunrise, 0.5 noon, 0.75 sunset (L6-7). The moon's cycle is 8 in-game days (L76). The calendar
day comes from real UTC time since 1 Jan 2026 (`WorldEnvironmentState.CalendarDayNow`, L73-78), so it already goes on
from one session to the next.

**Weather.** `WeatherProfile.cs`: `PrecipitationType` None / Rain / Snow (L7), `SnowIntensity` (L105), each profile
lasts `durationMin`-`durationMax` real seconds (L99-100) and names its weighted `next` choices (L102). Eleven profiles
in the catalog: Clear, FewClouds, PartlyCloudy, MostlyCloudy, Overcast, Fog, LightRain (0.35), Rain (0.8),
Thunderstorm (1.0), Snow (0.6) and Blizzard (1.0); they last 3 to 12 minutes. `WeatherCatalog.cs` is the ordered list,
and the index (one byte) is what goes over the network (L6). The server picks the next weather by the current
profile's `next` weights in `WorldEnvironmentNetwork.AdvanceWeather` (L319-346, the weighted pick L328-337). There is
no season; snow can follow any weather the `next` lists allow.

**How it reaches clients (the DOTS game).** `WorldEnvironmentNetwork` (the scene's clock, class at L17; the
`WHERE` note said L15, the attributes sit there) holds one `WorldEnvironmentState` (`World/Runtime/WorldEnvironmentState.cs`:
2 doubles, 10 floats, 2 ints, 2 bytes, 66 bytes). In the DOTS game `World/Entities/Runtime/WorldStateSync.cs` sends it:
`WorldStateServerSystem` looks four times a second (L51) and sends a `WorldStateRpc` (L13-35) to a player only when it
changed since that player last got it, and to each player as they come in. `WorldStateClientSystem` gives it to
`WorldEnvironmentNetwork.ApplyRemoteState` and sets `RemoteAuthority`, so a client never rolls weather. Every machine
works out the time of day, the weather blend, cloud drift and lightning from that state and the shared `NetClock`.
`CrossfireClockSystems.cs` is the same pattern for the hourly war, with its own RPC and a settings asset in Resources.

**GM.** `Tools/Debug/WorldEnvironmentCommands.cs`: `time.set`, `time.daylength`, `time.scale`, `weather.set`,
`weather.lock`, `weather.wind*`. `Tools/Debug/SkyCommands.cs` (L13): `sky.*` overrides that change only this machine's
look. A GM's typed line goes to the server as a `GmLineRpc` (`Gameplay/Persistence/Entities/Runtime/GmLineSystems.cs`)
and runs there. The GM window's World tab (`Tools/Editor/GM/GmToolsWindow.cs` ~L860-900) has weather and wind controls.
The server page (`Networking/ControlPage~/index.html`) has a GM command box (L105-109) and a Crossfire panel of
buttons (L111-120) that send `gm.crossfire ...` lines through the command route (`app.js` L557-566). No time or
weather buttons there yet.

**Presentation.** `World/Runtime/Presentation/EnvironmentPresenter.cs` is `[ExecuteAlways]` (L16). Its `OnEnable`
headless guard (L217-226) only switches off in a playing headless server, the build 12 fix. `Apply` is at L354 (was
L350) and `UpdateAtmosphere` at L572 (was L568). It keeps `skyMoisture`, a lingering wetness after rain that drives the
rainbow (L486-487): the only "builds up and fades" value today. `PrecipitationController.cs` has the snow rig (L61),
a blizzard layer above 0.7 intensity (L63, L65) and a roof shelter raycast (L135-140).

**Foliage.** GPU grass (`Environment/Vegetation/Grass/Runtime/GrassRenderer.cs`, `[ExecuteAlways]` L17, class L20)
colours each blade from its `GrassType` root/mid/tip colours (`GrassType.cs` L24-26), set per type in a property block
(`GrassRenderer.cs` L240-248). Trees, bushes and town grass patches use `KryticalNova/Foliage Wind`;
`KNFoliageCommon.hlsl` `KNFoliageTint` (L102-113) tints the whole albedo (leaves and trunk share one Synty atlas).
Flowers are terrain details (Rendering Bible 6d). The ground is `Universal Render Pipeline/Terrain/Lit` with a project
material (`World/Editor/TerrainMatteFixer.cs` L18); most buildings and props are `KryticalNova/Comic Toon Lit`
(`Rendering/Shaders/Comic/ComicToonLit.shader`).

**Trample map.** `GrassTrampleMap.cs` (L14): a 256² RGBA16F map of 64 m of ground round the camera (25 cm a texel),
filled by a compute shader from up to 32 `_KN_TrampleSources` that `FoliageInteractorSystem` publishes
(`FoliageInteractor.cs` L31-37). Marks fade to about a third in 1.5 s (`GRS_Zone01.asset`: `trampleMapMetres 64`,
`trampleResolution 256`, `trampleSpringSeconds 1.5`). All four channels are in use (rg push, b press, a height).

**Zones.** `World/Data/ZoneCatalog.cs` `ZoneEntry` (L30-48) names each map's scene, kind, level band and PvP rule;
each scene's `ZoneRules` points at its entry. There is one server per map, so a server knows its own zone.

## 2. Research (short, own words)

**Seasons in other games.**
- *Forza Horizon 4* is the closest match: the season changes once a real week, at a fixed hour, for every player of
  the shared world at once. Players planned around it, and the change itself became an event. Lesson: a fixed real
  schedule that everyone can know, and a clear moment when it turns.
- *Stardew Valley* and *Animal Crossing* tie what you can do and how the world looks to the season; Animal Crossing
  follows the real calendar. Lesson: the look changes most on grass, trees and flowers; the sky changes little.
- *World of Warcraft* (Winterspring, Dun Morogh) and *Valheim* (Mountains) keep zones snowy all year. Lesson: an
  always-winter zone is a climate flag on the zone, not a season of its own.
- Most MMOs that have seasons blend them over days, not seconds, so no one sees a forest turn orange in one frame.

**Snow cover in shaders.**
- *Up-facing mask:* snow amount = coverage × how much the surface faces up (normal.y), broken up by world-space noise
  so the edge is ragged, not a straight line. Cheap (a few ALU), used almost everywhere.
- *Fill low places first:* compare the coverage to a height or noise value, so snow gathers in cracks and on flat
  parts first and the bumps poke through until it is deep.
- *Tracks:* a render texture round the camera that characters press into and that slowly fills back in (Rise of the
  Tomb Raider, Batman Arkham Origins, Horizon). Ours already exists for grass: the trample map and its sources.
- *Depth:* real displacement or tessellation gives deep snow but costs a lot; the owner already ruled tessellation out
  for grass (10 Oct). For the comic look a colour change plus a slight darker rim on tracks reads well.
- *Shelter:* serious engines keep snow off under roofs with a top-down depth map. Optional here; the up-facing mask
  already keeps it off walls and ceilings.

## 3. Design

### Layers (no uphill, no two-way)

| Piece | System, layer | Why there |
|---|---|---|
| `KNSeason.hlsl` (season and snow globals, the snow and tint functions) | Rendering (Infrastructure) | The lowest layer whose shaders read it; ground, building, grass and tree shaders include it |
| `SnowTrackMap` (compute, a second long-memory map from the same `_KN_TrampleSources`) | Rendering (Infrastructure) | Knows no gameplay, like `GrassTrampleMap` |
| `SeasonSettings` asset, `SeasonProfile` assets, `ZoneEntry.climate` | World/Data (Simulation) | Next to `DayCycleProfile`, `WeatherProfile`, `ZoneCatalog` |
| `SeasonRules` (pure math: season from UTC, blend, daylight warp, snow integration) | World/Runtime (Simulation) | Pure, tested in EditMode; used by server and client |
| `SeasonServerSystem` / `SeasonClientSystem`, `SeasonStateRpc` | World/Entities (Simulation) | The DOTS game only; same pattern as `WorldStateSync` and Crossfire |
| `SeasonPresenter` (client: sets the globals, terrain flower density, track map) | World/Runtime/Presentation (Simulation) | Next to `EnvironmentPresenter`; World already references Rendering |
| `gm.season`, `gm.snow`, GM window controls, page panel | Tools, Networking/ControlPage~ | Tooling at the top, as today |

Grass (`KryticalNova.Vegetation.Grass`, no references) never learns about seasons: it reads shader globals, as it reads
the wind today. Nothing in a lower layer names a season type. The `WorldEnvironmentState` NGO struct is **not**
extended (owner rule 12 Nov: the NGO code is the rollback copy); seasons get their own RPC.

### The season clock (server)

- **Real UTC time decides.** `season = floor((utcNow − yearStartUtc) / seasonLength) mod 4`, order Spring, Summer,
  Autumn, Winter. `seasonLength = 14 days` (1,209,600 s), a year = 56 days. Both are in `SeasonSettings`.
- **Proposed start:** `yearStartUtc = 2026-01-05 00:00 UTC` (a Monday). Then today, 13 Nov 2026, is day 5 of
  Autumn (Autumn began Mon 9 Nov); Winter starts Mon 23 Nov, Spring Mon 7 Dec, Summer Mon 21 Dec. Seasons always turn
  on a Monday at 00:00 UTC. `turnHourOffset` and `seasonOffsetDays` in the settings move that without code (pick 3).
- A server restart picks up the same season: nothing is saved, the clock is the calendar.
- The server turns the UTC boundary into its own `NetClock` time once (`SeasonStart = NetClock.Now − (utcNow −
  boundaryUtc)`), so clients never read their own PC clock (it may be wrong).
- **GM hold:** `gm.season winter` holds a season (it blends in over a short GM blend, 20 s proposed) until
  `gm.season auto`, which blends back to the calendar. The hold lives only in the server's memory: a restart goes
  back to the calendar (safe default).

### The state sent to clients

`SeasonStateRpc`, sent like `WorldStateRpc`: to each player as they come in and only when it changed. Fields:

| Field | Type | Bytes | Meaning |
|---|---|---|---|
| `FromSeason`, `ToSeason` | byte ×2 | 2 | blending from, to (equal when settled) |
| `BlendStart` | double | 8 | NetClock time the blend began |
| `BlendSeconds` | float | 4 | how long it lasts |
| `SeasonStart`, `SeasonSeconds` | double, float | 12 | for "day 5 of 14" on screens and the GM readout |
| `SnowCover`, `SnowAnchor`, `SnowRate` | float, double, float | 16 | cover at the anchor time and its rate per second (signed) |
| `Flags` | byte | 1 | bit 0 GM hold, bit 1 always-winter zone |

**43 bytes.** Each client works out `cover(now) = clamp01(SnowCover + SnowRate × (now − SnowAnchor))` and the blend
from `NetClock`, so nothing is sent per frame.

### Season profiles (one asset per season, `SP_Spring` … `SP_Winter`)

All numbers PROPOSED, tuned in the assets.

| | Spring | Summer | Autumn | Winter |
|---|---|---|---|---|
| Daylight share (sunrise-sunset) | 0.50 (06:00-18:00) | 0.625 (04:30-19:30) | 0.50 | 0.375 (07:30-16:30) |
| Weather weight multipliers | LightRain ×3, Rain ×2, Clear ×1, Snow ×0 | Clear ×2, FewClouds ×2, Thunderstorm ×1.5, Snow ×0 | Fog ×3, Overcast ×2, MostlyCloudy ×1.5, Snow ×0.2 | Snow ×3, Blizzard ×1, Overcast ×1.5, Rain ×0.2, Thunderstorm ×0 |
| Wind speed × | 1.0 | 0.9 | 1.4 | 1.1 |
| Grass tint (root/mid/tip multiply) | fresh green, +10 % saturation | warm yellow-green | straw / olive | frost: −40 % saturation, pale tips |
| Leaves (green part of the atlas) | bright green | as today | orange-red mix per tree | bare or frosted (pick 2) |
| Flowers (terrain detail density) | 1.0 | 0.5 | 0.1 | 0 |
| Leaf VFX (`WindLeavesController`) | green | green | orange, starts at 4 m/s | off |
| Frost on the ground at night | 0 | 0 | 0.15 after midnight | 0.4 |

- **Weather:** the season does not replace the `next` lists; it multiplies their weights. `AdvanceWeather` gets one
  null-safe seam (`WeatherWeightScale`, a static `Func<WeatherProfile, float>`, null = as today) that the season
  server system fills. The pick stays one weighted roll; a weight of 0 means never.
- **Short winter days, daylight warp (pick 1, option a):** the clock still runs one real hour a day (the hourly
  Crossfire and "one a day" sales depend on it). Only the sun's path is warped: `SeasonRules.SunTime(t, share)` maps
  the clock's sunrise-sunset span onto 0.25-0.75 of the art curves and the night onto the rest, piecewise and
  continuous. `EnvironmentPresenter`, `StreetLampController` and `PlayerNightLight` read the warped time, so lamps
  come on at a winter dusk. Crossfire's `nightfallHour` stays a clock hour (it is a schedule, not the light).

### Blending between seasons

- **6 in-game days = 6 real hours** (PROPOSED, `transitionDays` in the settings), at the start of the new season.
  Everything blends by one number, `seasonBlend` 0-1, with a smooth step: colours, flower density, daylight share,
  weather weights (the server uses the blended weights).
- Trees do not all turn at once: each tree adds its own hash offset (±25 % of the blend) so a forest turns patch by
  patch.
- A GM switch uses `gmBlendSeconds` (20 s PROPOSED) so a test is quick.

### Snow cover (built and melted over time)

One world value, `snowCover` 0-1, owned by the server. It is integrated by `SeasonRules.SnowRate(...)` from the current
weather, the time of day and the season, and the server re-anchors it (a new RPC) when the weather changes, at sunrise
and sunset, and on a GM change. That is about 4-15 sends a real hour. Rates PROPOSED, all in `SeasonSettings`:

| Case | Rate | What it means |
|---|---|---|
| Snowing | + intensity / 600 per s | Snow (0.6): full cover in about 17 min; Blizzard (1.0): 10 min |
| Dry, by day, Spring/Summer/Autumn | − 1 / 1,800 per s | full cover gone in 30 min |
| Dry, at night (not winter) | day rate × 0.25 | it lingers overnight |
| Raining | day rate × 3 | rain washes it in 10 min |
| Dry, Winter | − 1 / 14,400 per s by day, 0 at night | lingers about 4 real hours |
| Always-winter zone | never below `alwaysWinterFloor` 0.6 | stays white, snows up to 1 |

**How it is drawn** (one function in `KNSeason.hlsl`, used by each shader):
- `snow = smoothstep(edge − 0.08, edge + 0.08, cover × up-facing − noise)` with up-facing = saturate((normal.y − 0.45)
  / 0.4) and a world-space value noise (no texture, ALU only) for a ragged edge and to fill low spots first.
- **Ground** (a project copy of URP TerrainLit, `KN/Terrain Lit`, pick 4): the colour blends to `snowColor`
  (a flat comic white-blue from `SeasonSettings`, no texture) and gets a little brighter in the cel band.
- **Roofs** (`Comic Toon Lit`): the same mask; roofs show snow from a slightly lower cover (`roofBias` 0.1), as real
  roofs whiten first. Walls (normal.y < 0.45) never get any.
- **Grass:** blades are pressed down and paled under deep cover (`cover > 0.5` → blade height × (1.3 − cover), tips to
  `snowColor`), so a snowfield reads as a snowfield and does not have green grass standing out of it.
- **Trees and bushes:** the up-facing part of the canopy whitens (frosted), cheaply.

### Footprints and trails

- `SnowTrackMap` (Rendering): a second small map beside the grass trample map, fed from the same `_KN_TrampleSources`
  (so players, NPCs, Hostiles and mounts all leave tracks, nothing new is networked: remote characters already move on
  every client). **256² R16G16 (press, age), 64 m, ping-pong pair: 512 KB.** Marks fill back in over
  `trackFillSeconds` 120 s (PROPOSED); fresh snowfall fills them faster (× (1 + 4 × snow intensity)).
- The ground and grass read one texel: snow is pushed off the track (cover × (1 − press)), with a thin darker rim, so
  a trail shows as a comic line in the white.
- The map moves with the camera in whole texels like the trample map, so tracks more than 32-48 m away are let go.
  That is "stays a while" without memory per player.
- It runs only while `snowCover > 0.02` on screen; otherwise it is released (0 cost in summer).

### Always-snowy zones

`ZoneEntry.climate` (new enum `Temperate` / `AlwaysWinter`, Temperate by default). On an `AlwaysWinter` map the server
uses the Winter weather weights and colours for foliage and ground whatever the season, keeps `snowCover` at least at
the floor, but takes the **daylight share and the season's name from the calendar**, so it still has long summer days
and short winter ones (the owner's "get the other season effects"). Which maps get the flag: pick 4 lists the Sunderpeak
and mountain maps as the proposal; the owner decides.

### GM switch

- **Console (server, through `GmLineRpc`):** `gm.season <spring|summer|autumn|winter|auto|status> [blend s]`,
  `gm.snow <0-1|auto>` (sets the cover now, then it builds or melts from there), `gm.season status` prints season,
  day x of 14, blend, snow cover, daylight hours, next turn (UTC and server-local time).
- **GM window:** a "Season" group on the World tab beside Weather (same `Run(...)` strings): a live "Now" line
  (Autumn, day 5 of 14, blend 100 %, snow 0.00, day 06:00-18:00), a season dropdown with Apply, "Back to calendar",
  a snow cover slider with Set and Auto. (Owner rule 12 Oct: every GM command also in the window.)
- **Server page:** a "Season" panel like the Crossfire one: Spring, Summer, Autumn, Winter, Calendar, Status buttons
  and a Snow 0 / 0.5 / 1 / Auto row, all sending `gm.season ...` and `gm.snow ...` through the command route, with
  the answer under them.

## 4. Performance budget (inside a locked 60 fps on mid hardware, Rendering Bible 7)

| Cost | Budget | Why it fits |
|---|---|---|
| Ground snow + tracks (terrain) | < 0.15 ms GPU at 1080p | one extra texture read (track map) and about 15 ALU a pixel; no keywords (a uniform branch on `cover > 0`) so no new variants |
| Roof snow (Comic Toon Lit) | < 0.05 ms | ALU only, no texture |
| Grass and foliage tint / frost | < 0.05 ms | a few ALU a vertex; no extra reads |
| Track map compute | < 0.05 ms, only while there is snow | 256² dispatch, the same as the trample map |
| Texture memory | 512 KB (track maps) | no new textures: snow colour is flat, noise is ALU |
| CPU (SeasonPresenter + server system) | < 0.02 ms, 0 B GC a frame | a handful of `Shader.SetGlobal*` calls; the server looks four times a second |
| Flower density change | 0 spikes over 20 ms | `terrain.detailObjectDensity` rebuilds detail patches, so it changes only in 0.1 steps during the 6 h blend (≤ 10 times a season); measured in the perf log, and if it spikes it moves to a shader fade (pick 1 of the evals) |
| Bandwidth | about 43 B × 4-15 a real hour a player | under 1 KB an hour; nothing a frame |

## 5. Client-side and server-build safety (owner: "make sure we don't lose things like our sky again")

- **The server never runs or switches off client presentation.** `SeasonPresenter` is not placed in scenes: it is
  made at runtime by a `RuntimeInitializeOnLoadMethod` bootstrap (as `FoliageInteractorSystem` is), so there is no
  serialized `enabled` flag a batch build could pack switched off. In a `UNITY_SERVER` player it is simply not made.
- No `[ExecuteAlways]`, `OnValidate` or edit-mode code of this plan changes serialized state under `isBatchMode`.
  `EnvironmentPresenter`'s headless guard (L217-226) is not touched.
- **Safe default in every shader:** while no presenter has published (`_KN_SeasonParams.w == 0`), every shader
  draws exactly what it draws today (summer, no snow). A broken presenter makes "no season", never a black or
  white world.
- The server system only decides numbers (season, blend, snow cover) and sends them; no server-only `#if` sits in a
  client file.
- **Built client bundle check:** the publish's existing gate (client bundles match scenes) also checks that the map
  bundles carry the `KN/Terrain Lit` ground material and that `EnvironmentPresenter` is enabled in them; after a
  publish, `gm.season status` and `sky.status` on a client are the quick live check.

## 6. Files to touch and new files

New:
- `Assets/_Game/Rendering/Shaders/Include/KNSeason.hlsl`
- `Assets/_Game/Rendering/Shaders/Terrain/KNTerrainLit.shader` (project copy of URP TerrainLit plus `KNSeason.hlsl`)
- `Assets/_Game/Rendering/Runtime/Seasons/SnowTrackMap.cs`, `Assets/_Game/Rendering/Shaders/Seasons/SnowTrack.compute`
- `Assets/_Game/World/Data/SeasonSettings.cs`, `SeasonProfile.cs`; assets `World/Resources/SeasonSettings.asset` (new folder, loaded like `Crossfire/Resources/CrossfireSettings.asset`),
  `World/Data/Assets/Seasons/SP_Spring.asset` … `SP_Winter.asset`
- `Assets/_Game/World/Runtime/SeasonRules.cs` (pure), `World/Runtime/Presentation/SeasonPresenter.cs`
- `Assets/_Game/World/Entities/Runtime/SeasonSync.cs` (`SeasonStateRpc`, server and client systems)
- `Assets/_Game/World/Entities/Tests/Editor/SeasonRulesTests.cs`, `SeasonSyncTests.cs`
- `Assets/_Game/Tools/Debug/SeasonCommands.cs`

Touched:
- `World/Runtime/WorldEnvironmentNetwork.cs` (the one weight seam in `AdvanceWeather`, L328-337)
- `World/Data/ZoneCatalog.cs` (`ZoneEntry.climate`), `World/Runtime/Presentation/EnvironmentPresenter.cs` (reads the
  warped sun time in `Apply`, L354), `StreetLampController.cs`, `Gameplay/Player/Runtime/PlayerNightLight.cs`,
  `WindLeavesController.cs` (autumn leaves)
- `Rendering/Shaders/Comic/ComicToonLit.shader`, `Environment/Vegetation/Shaders/KNFoliageCommon.hlsl` (tint, frost),
  `Environment/Vegetation/Grass/Rendering/GrassBlades.shader` (tint, press under snow, track read)
- `World/Editor/TerrainMatteFixer.cs` (the project terrain material uses `KN/Terrain Lit`)
- `Tools/Editor/GM/GmToolsWindow.cs` (World tab), `Networking/ControlPage~/index.html` + `app.js` (Season panel)
- `_Docs/Design/Rendering-Bible.md` (5: new globals; 6d: seasons and snow; 7: budgets), `_Docs/Guides/Server-Guide.md`
  (page panel), `01_Systems_Build_List.md` row 41 and glossary (Season, Snow cover, Always-winter zone), Encyclopedia

## Evals (written and approved before any code)

- [ ] The season from UTC: 13 Nov 2026 is Autumn day 5; 23 Nov 00:00 UTC is Winter day 1; a year is 56 days; the
  offsets move the turn. Proved by: `SeasonRulesTests.SeasonFromUtc`.
- [ ] The blend runs 0 → 1 over `transitionDays` in-game days and never jumps. Proved by: `SeasonRulesTests.Blend`.
- [ ] The daylight warp is continuous, keeps noon at 0.5, and gives 07:30-16:30 at share 0.375. Proved by:
  `SeasonRulesTests.SunTime`.
- [ ] Snow builds and melts at the settings' rates, never leaves 0-1, and never drops below the floor in an
  always-winter zone. Proved by: `SeasonRulesTests.SnowCover`.
- [ ] Weather weights: over 10,000 rolls Winter never picks Thunderstorm and Summer never picks Snow; with no season
  the picks match today's. Proved by: `SeasonRulesTests.WeatherWeights`.
- [ ] Two clients: both get the same season, blend and snow cover after `gm.season winter`, and a late joiner gets it
  on arrival. Proved by: `SeasonSyncTests` in a `KnTestWorld` (server + two clients), and the owner on a second client.
- [ ] The RPC stays 43 bytes and under the 1,300 B limit. Proved by: `RpcSizeLintTests`.
- [ ] A server build has no `SeasonPresenter` and the map bundles keep `EnvironmentPresenter` enabled. Proved by: the
  publish bundle check.
- [ ] Shaders without a presenter draw exactly as today. Proved by: owner in Play with the presenter off, compared
  side by side with a screenshot from before.
- [ ] Footprints show in snow and fill in after about 2 minutes. Proved by: owner in Play (GIF), second client sees
  the other player's trail.
- [ ] 60 fps held: the training-dummy perf test with a blizzard and full cover, no frame over 20 ms. Proved by: the
  performance log.

## Steps (one PR each)

- [ ] 1. `SeasonSettings`, `SeasonProfile`, `SeasonRules` and their tests (no visuals). Evals 1-5.
- [ ] 2. `SeasonSync` (RPC, server and client systems), the weather weight seam, `gm.season` / `gm.snow`, GM window
  group, page panel. Evals 2, 5, 6, 7.
- [ ] 3. `SeasonPresenter` + `KNSeason.hlsl`; daylight warp in the presenter, lamps and night light; grass, leaf and
  flower changes. Evals 3, 8, 9.
- [ ] 4. Snow cover on ground (`KN/Terrain Lit`), roofs, grass and trees. Evals 4, 9, 11.
- [ ] 5. `SnowTrackMap` and tracks in ground and grass. Evals 10, 11.
- [ ] 6. `ZoneEntry.climate` and the always-winter maps; docs, Encyclopedia. Evals 4, 6.

## The owner's picks

Questions as multiple choice, the recommended option first. Answers are recorded here with the date.

1. **"Short winter days" means:** a) less daylight inside the same one-hour day (sun rises later, sets earlier; the
   hourly Crossfire and daily sales stay as they are); b) the whole in-game day gets shorter in real time in winter
   (simple, but Crossfire's hour and the "one a day" calendar would drift); c) both.
2. **Winter trees:** a) frosted, leaves kept (cheapest, no holes in the canopy); b) leaves thin out to bare branches
   (dithered, costs an alpha test on canopies in winter only); c) bare in late autumn, frosted in winter.
3. **When seasons turn:** a) Mondays 00:00 UTC, every two weeks (today is Autumn, Winter from 23 Nov); b) the same
   but at an hour the owner names (for example 18:00 his time, so players are on); c) start Winter now so it is
   tested first.
4. **The ground shader and the always-winter maps:** a) a project copy of URP Terrain Lit with the snow added, and
   the Sunderpeak maps plus any mountain field zones as always winter (owner names the list); b) snow only on roofs,
   grass and props first, the ground later; c) a) for the shader, no always-winter maps until they are dressed.

## Done when (the owner's words)

GM season switch changes weather, foliage and day length on two clients, snow piles up and melts, footprints show, an
always-snowy zone keeps its snow, fps log holds the target.

## As built

(Empty: nothing is built yet.)
