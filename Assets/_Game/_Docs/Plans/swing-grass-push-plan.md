# Swing grass push plan

**Status: proposed 13 Nov 2026, waiting for the owner's review.** No code until the owner has read this and answered the
picks (owner, 13 Nov 2026: items 18-21 are research and design notes today; code after the review).

Owner's day list 13 Nov 2026, item 20: "Weapon swings move the grass (cheap, no physics)". Design: `../Design/Rendering-Bible.md`
6d (vegetation, wind and water) and 7 (performance budgets). Every number marked PROPOSED is a starting value for the
settings asset, not a decision.

## What it is for

When anyone swings a weapon in a meadow, the grass in front of them is thrown aside and springs back in about half a
second. A basic attack bends a small arc in front of the swing; an area skill sends a ring out across the grass; a big
hit (a critical, a finisher, a Shatterpoint) sends a stronger wave from the point it lands. Every player sees every
player's swings, Hostiles' swings included. It is a look only: nothing in the game rules changes, and nothing new is
sent over the network.

## Risk

**Low.** If it breaks, grass looks wrong or does not move. No item, Cred, save or other player's data is touched. The
one real risk is the frame rate (the 60 fps target, no frame under 50), so the budget below is part of the evals.

## 1. How it works today (research)

Line numbers checked 13 Nov 2026 on this branch. The WHERE lines of the day list are right: `FoliageInteractor.cs`
L34-37 are the four shader ids.

**Who bends grass today.** `World/Runtime/Presentation/FoliageInteractor.cs`
- `FoliageInteractor` (L11-20): a marker on players, NPCs, Hostiles and pets, with a `radius` (default 0.9 m). Its
  comment says it: presentation only, nothing networked.
- `FoliageInteractorSystem` (L27-91) runs in `LateUpdate` at execution order 1000 and fills two lists each frame, with
  fixed arrays and no allocation:
  - `_KN_Interactors[8]` + `_KN_InteractorCount` (L34-35, `MaxInteractors = 8`, L29): the 8 nearest the camera, sorted by
    insertion (L74-84). Read by bushes and town patches (`KNFoliageCommon.hlsl`), and by grass only when there is no
    trample map.
  - `_KN_TrampleSources[32]` + `_KN_TrampleSourceCount` (L36-37, `MaxTramplers = 32`, L31): everyone within 70 m
    (`MaxDistance`, L32), unsorted. They press the trample map.
- Each entry is one `float4`: xyz feet, w radius. No direction, no time, no strength.

**The trample map (the grass path).** `Environment/Vegetation/Grass/`
- `Runtime/GrassTrampleMap.cs`: a 256 x 256 `R16G16B16A16_SFloat` ping-pong texture pair (L88-99) over 64 m
  (`GrassRuleSet` L101-103: one texel per 25 cm), laid a quarter of its width ahead of the camera
  (`GrassRenderer.cs` L301-307). It moves in whole texels (L51-56), so marks are shifted, never resampled.
- It copies `_KN_TrampleSources` from the globals (L58-61) and dispatches `Compute/GrassTrample.compute` once a frame
  (8 x 8 groups, L72-73).
- Decay: every frame each mark is multiplied by `exp(-dt / trampleSpringSeconds)` (L63; `GrassRuleSet` L105, 1.5 s).
  That is a plain fade, with no overshoot.
- `GrassTrample.compute` L36-51: per texel, per source, `press = (1 - dist / radius)^2`; the hardest press wins (no
  sum). It stores rg = push direction x press (radial, away from the source), b = press, a = the presser's height.
- `Rendering/GrassBlades.shader` L88-120: with a map, every blade reads **one texel** at its root (L96-105). It ignores
  the map's rim and anything more than 1.5 m above or below the presser. Then `pos.xz += push * _GrassMotion.z * bend
  * heightWS * 1.2` and `pos.y -= flatten * 0.45 * ...` (L119-120). L122-125 keep the blade's length (a leaning tip
  drops). Fill tufts have `_GrassMotion.z = 0` and take no part.
- Without a map (outside Play, or `trample` off) blades loop over the 8 `_KN_Interactors` instead (L108-118).

**Bushes and trees.** `Environment/Vegetation/Shaders/KNFoliageCommon.hlsl` L74-89 loop over the 8 `_KN_Interactors`
per vertex with the same squared falloff, scaled by `_InteractStrength`. `KNWind.hlsl` L15-17 declares the array
(`KN_MAX_INTERACTORS 8`). L8-11 hold the wind phase rule: never write a wave as time x a changing value.

**Where swings are already shown on every client.** All three paths end in `IGameFeelSink`
(`Core/Runtime/IGameFeelSink.cs` L124), which `GameFeel/Runtime/GameFeelPresenter.cs` implements:
- Basic attack: the replicated `CombatState.Step` / `StepStartTick` (`Player/Entities/Runtime/PlayerComponents.cs`
  L282-296) -> `Combat/Entities/Runtime/StrikePresentSystem.cs` L52 -> `PlayerMeleeCombat.ShowStrikeFromGhost` (L608)
  -> `SpawnSlashVfxLocal` -> `Feel.Swing(transform, step, ...)` (L756) -> `GameFeelPresenter.Swing` (L438). The
  owner's own strike comes from its prediction, everyone else's from the server.
- Skill cast: the `CastEvent` ring (`HitEvents.cs` L93-100) -> `CastEventPresentSystem.cs` L44 -> `ShowCastSeen` ->
  `PresentCast` -> `PlayerSkillAttacks.cs` L159-162 -> `Feel.SkillCast(..., areaRadius, ...)` (area skills pass their
  range) and, for attack skills, `Feel.Swing(..., step 3, ...)`. `GameFeelPresenter.SkillCast` L968.
- Hit: the `HitEvent` ring -> `HitEventPresentSystem.cs` L76 `PreviewImpact` -> `PlayerMeleeCombat.cs` L1168
  `Feel.Hit(HitFeel)` (`ComboStep`, `IsCritical`, `ShieldShattered`, `Point`, `TowardAttacker`) -> `GameFeelPresenter.Hit`
  L454.
- Hostiles: `HostileViewClientSystem.cs` L124 `ShowAttackFromGhost` and the NGO `HostileNet.cs` L689 `Feel.Swing(...,
  3, false, windup)`. Step 1 checks that the DOTS path reaches L689.

So a listener in the presenter already hears every swing, cast and hit on every client, from replicated data, with
no new network field.

**How other games do it (short).** Most games bend grass from a short list of "benders" passed to the shader, or from
a top-down texture that a compute pass or a camera paints, as our trample map does. Single-player action games often
add a brief radial "shock" sphere for explosions and big hits: a position, a radius and a strength that rises and
falls over a fraction of a second, with the fall done as a damped spring so the grass overshoots once and settles.
Expanding rings for area attacks are the same sphere with a moving front: a blade only moves when the front reaches
it. Nobody uses physics for this. The cost is kept down by a hard cap (4-16 pushes), a distance cut-off, and by
skipping the work when no push is alive.

## 2. Design

**Layers.** Grass (Environment) and World are Simulation; GameFeel is Presentation. Grass already knows no gameplay
and reads globals (`GrassTrampleMap` L10-12); that stays.
- `World` (Simulation) gets the push pool: `World/Runtime/Presentation/FoliagePushes.cs`, a static fixed-size pool
  with `Add(kind, position, direction, radius, strength)`. `FoliageInteractorSystem.LateUpdate` publishes it next to
  the trample sources.
- `GameFeel` (Presentation) writes the pushes from `Swing`, `SkillCast` and `Hit`. It gains an asmdef reference to
  `KryticalNova.World`. That is downhill (Presentation -> Simulation); World never refers to GameFeel, so there is no
  two-way pair.
- `Vegetation.Grass` reads only the new globals; no new reference.
- **The server never writes a push.** The presenter does not run on a dedicated server (`ClientPresentationRegistry.Feel`
  is null there), and the server has no grass. No ghost field, ring or RPC is added.

**The push (one pool slot).**
| Field | Meaning |
|---|---|
| position | world xyz: the attacker's feet (arc, ring) or the hit point (wave) |
| direction | flat world XZ unit: the attacker's facing (arc) or attacker -> target (wave); unused by a ring |
| radius | how far it reaches (metres) |
| strength | 0-1.2, multiplies the grass's own `_GrassMotion.z` |
| start time | `Time.time` when written; age = now - start |
| kind | 0 arc, 1 ring, 2 wave |

Packed as three `float4` per slot: `(x, y, z, radius)`, `(dirX, dirZ, cos half-angle, strength)`,
`(age, kind, front speed, spring seconds)`. Globals `_KN_SwingPushes[24]` and `_KN_SwingPushCount`.

**Pool: 8 slots (PROPOSED).** A new push takes a free slot, else the slot with the least left (oldest, weakest). A push
farther than 60 m from the camera is not added (the map reaches 48 m ahead). A slot whose age passes its spring time
is freed. The local player's pushes are never pushed out by others'.

**Where it is drawn: into the trample pass, not per blade (recommended).** `GrassTrample.compute` gets a second
output, a 256 x 256 `R16G16_SFloat` "swing" texture on the same grid (256 KB). Each frame the kernel works out every
live push for its texel and writes the sum. Nothing is stored between frames: the spring is worked out from the age, so
there is no decay to tune and nothing left behind. Blades read one more texel (same UV as the trample map) and add it
to `push` before L119. If no push is alive, the CPU skips that part of the dispatch and sets a flag so blades skip the
fetch.

**Shader maths (per texel, per live push).**
- `away = texel - position.xz`, `d = length(away)`, `n = away / d`.
- Arc: `area = (1 - d / radius)^2 * smoothstep(cosHalf - 0.15, cosHalf + 0.05, dot(n, direction))`, local age = age.
- Ring and wave: the front is at `r = frontSpeed * age`; `area = (1 - abs(d - r) / band)^2` while `d <= radius`; local
  age = `age - d / frontSpeed` (a blade starts moving when the front reaches it, then springs on its own).
- Spring (damped, one overshoot): `e(t) = smoothstep(0, 0.05, t) * exp(-z * w * t) * cos(w * t)`, with `w` = 14 rad/s
  and `z` = 0.45 (PROPOSED). That is about 4 % left at 0.5 s, and it swings back past upright once. The rise to full
  takes 0.05 s. The constants come from the settings, so the curve is the same everywhere.
- Push: `swing.rg += n * area * e(localAge) * strength`. Arcs add a small sideways sweep, `0.3 * side * sign(step)`, so
  combo steps 1 and 2 lean opposite ways and 3 drives straight out (PROPOSED).
- Times are ages from the CPU (small numbers), never `_Time.y` times a changing value (`KNWind.hlsl` L8-11).

**How it combines with trample.** In `GrassBlades.shader`: `push = trample.rg * keep + swing.rg * keepS`, where `keepS`
uses the same rim fade and 1.5 m height check (the push's own y). The total push is clamped to length 1.3, so a
swing over trampled grass cannot fold a blade flat into the ground. A swing barely flattens: `flatten = max(trample.b,
0.25 * length(swing.rg))`. The length keep at L122-125 already makes a bent tip drop. Lawns keep their straight
edge (L127-130 runs after this).

**Bushes (owner pick 2).** Bushes and town patches use `KNFoliageCommon.hlsl`, which has no map. The cheap way is to
make the 8 `_KN_Interactors` slots carry pushes too. The recommended way is to leave bushes for later: they are few
in meadows and the item names grass.

## 3. Numbers per kind (PROPOSED, in a settings asset)

All in a new `FoliagePushSettings` asset (`World/Data/FoliagePushSettings.cs`, asset in `World/Data/Assets/`), which
GameFeel's presenter holds as a field. No number lives in code.

| Kind | From | Radius | Shape | Front speed | Strength | Spring |
|---|---|---|---|---|---|---|
| Arc, combo steps 1-2 | `Swing`, step 1-2 | 2.2 m | 120 deg arc in front | none | 0.55 | 0.45 s |
| Arc, finisher (step 3) and attack skills | `Swing`, step 3 | 2.8 m | 150 deg arc | none | 0.8 | 0.5 s |
| Ring, area skill | `SkillCast` with `areaRadius > 0` | the skill's range, at most 10 m | band 1.2 m wide | 14 m/s | 0.8 | 0.5 s |
| Wave, big hit | `Hit` when critical, step 3 or Shatterpoint | 4 m | band 1.5 m wide | 10 m/s | 1.1 | 0.6 s |
| Hostile swing | `Swing` from a Hostile | 2.5 m x body scale (pick 3) | 140 deg arc | none | 0.7 | 0.5 s |

Also in the asset: pool size (8, also the shader's constant, pinned by a test), cut-off distance (60 m), spring `w` and
`z`, the sweep share (0.3), the total-push clamp (1.3), and a master on/off switch. An area attack skill also fires
`Swing` at step 3 (`PlayerSkillAttacks.cs` L162): the ring replaces that arc so one cast takes one slot, not two.

## 4. Performance budget (inside 60 fps, no frame under 50)

- **CPU per swing:** one slot write and a scan of 8 slots. No allocation (fixed arrays, structs, no LINQ, no closures).
- **CPU per frame:** build 24 `float4` in a fixed array and one `SetGlobalVectorArray` plus one `SetGlobalInt`. Always
  send all 24 (Unity locks a global array's size at its first set). About 390 bytes of uniforms a frame.
- **GPU compute:** 65,536 texels x up to 8 pushes. Each push first does a bounding-circle test, so only texels inside a
  push do the maths (an exp, a cos, a smoothstep: about 25 ALU). Estimate under 0.02 ms on the owner's RTX 5070 Ti and
  under 0.06 ms on the mid tier (RTX 3060 class). It is skipped when no push is alive.
- **GPU per blade:** one more bilinear fetch from a 256 KB texture already in cache next to the trample fetch, and
  about 6 ALU (add, clamp, max). Fill tufts and far tufts skip it, as they skip the trample map.
- **Memory:** one 256 KB render texture.
- **Garbage:** zero per frame and per swing; pinned by an eval.
- **Culling:** pushes past 60 m are not added; the map ends 48 m ahead and 16 m behind anyway; blades past the wind
  fade distance are not touched.
- **Proof:** the dummy stress run (`performance-target`: full buffs, pet out, every hotbar skill on rotation) in a
  meadow, perf log on, against the same run with the switch off. Single effects under about 0.7 ms cannot be ranked
  in the Editor (Rendering-Bible 7), so the owner reads a build's "Slowest, last minute" and "Frames under 50".

## 5. Second client

Each client draws pushes from what it was already shown: the replicated `CombatState` step, the `CastEvent` ring
and the `HitEvent` ring. The owner's own swing pushes on the press (it is predicted, and the strike is shown then);
other players' push when their strike arrives, about one interpolation delay later, which is what the slash effect
already does. A client that first meets a ghost takes its rings as seen (`CastEventPresentSystem` L33-37), so it
replays no old pushes. Two clients can differ by a few frames in timing. That is fine for a look, and nothing depends
on it. A player out of relevancy range (when relevancy is on, 150 m) is past the 60 m cut-off anyway.

## 6. Files

New:
- `Assets/_Game/World/Runtime/Presentation/FoliagePushes.cs`: the pool, `Add`, the packing (pure functions for tests).
- `Assets/_Game/World/Data/FoliagePushSettings.cs` + `World/Data/Assets/FoliagePush_Default.asset`.
- `Assets/_Game/World/Tests/Editor/FoliagePushTests.cs`.

Changed:
- `World/Runtime/Presentation/FoliageInteractor.cs`: `LateUpdate` publishes `_KN_SwingPushes` / `_KN_SwingPushCount`.
- `Environment/Vegetation/Grass/Compute/GrassTrample.compute`: the swing output and its loop.
- `Environment/Vegetation/Grass/Runtime/GrassTrampleMap.cs`: second texture, copy pushes, skip when none,
  `_KN_GrassSwing` global.
- `Environment/Vegetation/Grass/Rendering/GrassBlades.shader` (L88-120): read and combine.
- `GameFeel/Runtime/GameFeelPresenter.cs` (`Swing` L438, `SkillCast` L968, `Hit` L454) and
  `GameFeel/Runtime/KryticalNova.GameFeel.asmdef` (+ `KryticalNova.World`).
- `_Docs/Design/Rendering-Bible.md` 6d (the globals table L208-210 and the trample text L424-432), 7 (the budget).
- `_Docs/Reference/12Sky/01_Systems_Build_List.md` (status, glossary row "Swing push", change log), then the
  Encyclopedia rebuild and publish.
- Only with pick 2 b: `KNWind.hlsl` and `KNFoliageCommon.hlsl`.

## Evals (written and approved before any code)

- [ ] The pool never holds more than 8. A ninth push replaces the one with the least left, and the local player's
  pushes outlive others'. Proved by: `FoliagePushTests.PoolCapAndReplace`.
- [ ] A push past 60 m from the camera is not added, and a push older than its spring time is freed. Proved by:
  `FoliagePushTests.CullAndExpire`.
- [ ] The spring curve starts at 0, peaks within 0.06 s, overshoots once and is under 5 % at 0.5 s. Proved by:
  `FoliagePushTests.SpringCurve` (the C# copy of the curve, kept equal to the shader's by a text check of the constants).
- [ ] The pool size in C# equals the shader's constant. Proved by: `FoliagePushTests.ShaderConstantMatches` (reads the
  .compute text).
- [ ] Adding pushes and publishing them makes no garbage. Proved by: `FoliagePushTests.NoAllocation` (GC.Alloc
  recorder over 1,000 swings and 600 frames).
- [ ] An arc bends only in front, a ring moves outwards over time, a wave starts at the hit point. Proved by: the
  owner in Play, from a GIF of each kind side by side (testing-feedback rule), at the Training Yard meadow.
- [ ] No server code writes a push, and the layers hold. Proved by: `dependency_map.py --check` (0 uphill, 0 two-way)
  and `compile_check.py --server`; a grep lint that `FoliagePushes.Add` is called only from `GameFeel`.
- [ ] No fps cost. Proved by: the owner's perf log from a build, dummy stress run in a meadow, switch on vs off:
  the same "Frames under 50" and "Slowest, last minute" within noise.
- [ ] Two clients: client B sees client A's basic attacks, an area skill and a critical move B's grass, and A sees
  B's. Proved by: second-client test (MPPM clone on the isolated test server, never the real one).

A new test is shown failing once (break the code, see it red) before it counts.

## Steps (one PR each)

- [ ] 1. Pool, settings asset and tests; publish the globals from `FoliageInteractorSystem`; check that the DOTS hostile
  path reaches `Feel.Swing`. Nothing visible yet. Evals: pool cap, cull, spring curve, no allocation, layers.
- [ ] 2. Compute output, `GrassTrampleMap` second texture, blade shader read and combine; an Editor-only menu item that
  drops each kind of push in front of the camera, for looking at it before any swing feeds it. Evals: shader constant, owner in Play.
- [ ] 3. GameFeel writes the three kinds from `Swing`, `SkillCast` and `Hit`, with the ring replacing the skill's arc.
  Evals: owner in Play (GIFs), two clients.
- [ ] 4. Measure in a build with the owner, tune the asset, update Rendering-Bible 6d and 7, the build list and the
  Encyclopedia. Evals: no fps cost.

## The owner's picks

Questions as multiple choice, the recommended option first. Record the answers here with the date.

1. **How the grass reads the pushes:** a) worked out once per texel in the trample pass, blades read one more texel
   (cheapest per blade, matches the trample map); b) each blade loops over the 8 pushes itself (simpler, about 8 times
   the per-blade cost, but works outside Play too).
2. **Bushes and town grass patches:** a) grass only for now, bushes later; b) bushes too, through their 8-slot
   interactor list (a small change to `KNFoliageCommon.hlsl`, costs bushes a longer loop).
3. **Hostile swings:** a) they push grass too, sized by the creature's body (a Storm Sovereign's swing flattens a
   field); b) the same small arc for every Hostile; c) players only.

## Done when (the owner's words)

Swings visibly push grass, no fps cost in the perf log, works on a second client.

## As built

(Empty until steps land.)
