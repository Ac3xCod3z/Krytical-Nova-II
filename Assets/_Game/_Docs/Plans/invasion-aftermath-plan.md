# Invasion Aftermath (Destroyed City Look) — Plan

> Rendering rules, the colour bible and budgets live in **[Rendering-Bible.md](../Design/Rendering-Bible.md)**. Folder and
> naming rules live in **[ProjectOrganization.md](../ProjectOrganization.md)**. This plan is the design record for the
> destroyed-city look; update the Rendering Bible when a step lands.

**Status:** planned 2026-10-04. Nothing in this plan is built yet. The inventory below was read from the repository
files only; nothing was opened in Unity and the textures (Git LFS) were not viewed.

## Context

After a **successful invasion** the city should show it: burning buildings, collapsed buildings, destroyed cars, and
burn marks on grass, concrete, cars and huts.

The invasion itself (trigger, rules, mechanics, rewards) is **deliberately undefined**. No invasion code or design doc
exists yet. This plan therefore builds the destroyed look as a standalone system with one small input, and leaves the
trigger as an empty socket.

## Decisions (owner, 2026-10-04)

1. **Visual only first.** Damage does not change collision or pathing. Collision for the collapsed tiers is added
   later.
2. **Invasion logic stays out of scope** until its mechanics are designed. The city flips between *intact* and
   *aftermath*; there are no mid-invasion states yet.
3. **No vendor edits.** Synty materials and prefabs are not modified (art-style brief, `_ThirdParty/README.md`).
   Anything we need to change is copied into `_Game/` and owned there.

## The boundary

The destruction system knows nothing about invasions. It takes three values:

| Input | Meaning |
|---|---|
| `Seed` (int) | Which buildings, cars and patches of ground get hit. Same seed, same city. |
| `Intensity` (0–1) | How much of the city is damaged. 0 = intact. |
| `Origin` (optional point) | Damage clusters around it (for example a breached gate). Without it, damage is even. |

Until the invasion exists, GM console commands set those values. Later the invasion code calls the same function on
success. Assumption to confirm when the invasion is designed: damage is the same for everyone in the zone, which is
why it runs from a seed.

## Inventory: what exists and what is missing

| Goal | Already in the project | Gap |
|---|---|---|
| Collapsed buildings | `RuinBaker` + Quick Destruction: 99 baked static ruin prefabs (Damaged / Collapsed / Rubble / Sliced, 2 variants) for 11 towers, the city wall and the wall tower, in `Environment/Ruins/Prefabs` | Shopfront modules, the two shacks and market stalls are not covered |
| Burning buildings | Synty `PolygonParticleFX` (`FX_Fire_Small/Big/Huge`, `FX_Smoke_Black*`, `FX_Embers_01`, `FX_Sparks_01`), Sci-Fi City `FX_Fire_01/02`, `FX_Smoke_Drift_01`, 55 Cartoon FX explosions, `NetworkCombatVFXPool` | Fire sockets on buildings; a cap on simultaneous fires |
| Destroyed cars | 19 Synty vehicles; `StarterTownBuilder` already places parked cars | No wrecked variants |
| Burn marks | Synty `Generic_Decal_Crack_01/02` and `Grunge_01–04`; `ComicHalftone.hlsl` | No scorch textures; no Decal feature on `PC_Renderer`; `Mobile_Renderer` has no features and no depth texture |
| Burnt grass | GPU grass with `T_GrassMask_Zone01` and runtime `GrassExclusionVolume` | No scorched terrain layer (`TL_Dirt`, `TL_TownStone`, `TL_Grass`, `TL_Rock` only) |
| Procedural placement | `StarterTownBuilder.*` builds the town from rules and a seed; ruins are already placed in the outskirts and wall band | Rules run in the editor only, not in response to gameplay |

### Why materials alone cannot do it

- The towers (`M_Tower_*`) and 40 of the Sci-Fi City materials use Synty's `Generic_Basic` shader graph. It has ten
  properties and **no overlay, dirt or damage input**.
- Only `Polygon_Triplanar` (the `M_Grime_Light/Medium/Heavy` materials) has `Overlay Map` and `Dirt Amount`.
- `KryticalNova/Comic Toon Lit` is used by two materials only. The comic look comes from the `ComicStyle` post pass.

So burn marks are **projected decals**, which land on any opaque surface without touching its material, and fully
burnt objects use a small set of **project-owned charred materials** on their wreck or burnt-out variant.

## Core decisions (the "why")

1. **Everything derives from a seed.** A site's damage tier is `hash(Seed, siteId)` against `Intensity`, weighted by
   distance to `Origin`. No per-building state is stored or sent.
2. **Damage sites are tagged at build time.** `StarterTownBuilder` adds a component to each building, car, stall and
   shack with a stable ID, its damaged variants and its fire sockets. Runtime never searches the scene.
3. **Ruins stay static meshes.** Variants are baked by `RuinBaker` and swapped in; no Quick Destruction runtime
   component, rigidbody or fragment simulation ships.
4. **Shopfronts burn out; they do not collapse.** They are stacks of `Section_*` modules, not one prefab, so they get
   a charred material, dead windows and fire in the openings instead of a fracture bake.
5. **Burn marks follow the colour bible.** Ink `#14122B` with a halftone edge from `ComicHalftone.hlsl`; no soft
   photographic soot.
6. **Fires are pooled, capped and distance-culled.** Smoke haze goes through the existing fog driver, not extra
   particle volume.
7. **PC first, Mobile degrades.** Mobile skips projected decals (no depth texture) and uses ground quads or nothing.

## Steps

### 1. Test scene and GM commands
- New scene in `Scenes/Development/` with one tower, one shopfront stack, one shack, one car, a stall and a grass
  patch.
- `gm.city.destroy <intensity> [seed]` and `gm.city.restore` in `Tools/Debug/GmCommands.cs`.

### 2. Damage sites
- `DamageSite` component (stable ID, intact root, variants per tier, fire sockets) and a per-zone registry.
- `StarterTownBuilder` tags sites while it places them.
- A resolver turns `(Seed, Intensity, Origin)` into a tier per site and toggles the variants.

### 3. More ruin variants
- Add `SM_Building_Shack_01/02` to the `RuinBaker` sources.
- Add a vehicle **Wreck** tier (charred material, glass removed, settled pose) for the parked-car set.
- Burnt-out state for shopfront stacks and stalls.
- Charred material set in `Environment/Buildings/Materials/`, modelled on `M_Grime_Heavy`.

### 4. Scorch layer
- Add the URP Decal renderer feature to `PC_Renderer`.
- Project decal shader (ink + halftone edge) and four to six generated blot textures, baked by an editor tool in the
  way `CloudNoiseBaker` bakes cloud noise.
- Under each ground scorch: a `GrassExclusionVolume` and a new `TL_Scorched` terrain layer.

### 5. Fire layer
- Fire, smoke and ember effects at the site sockets through the VFX pool.
- Hard cap on live fires and a distance toggle, in the manner of the street-lamp cones (45–65 m).
- Smoke haze through `VolumetricAtmosphereDriver`.

### 6. Budgets and docs
- Measure decals and fires in a build and add both to the Rendering Bible's budget table.
- Record the new systems and folders in the Rendering Bible changelog.

## Deferred until the invasion is designed

- Network replication of the state (one small struct, in the pattern of `WorldEnvironmentState`).
- Collision and pathing changes for collapsed tiers, including the server-side swap and a navmesh rebuild
  (`RuntimeNavMeshBuilder`).
- Recovery and rebuilding over time (fires die to smoulder, scorch fades, tiers step back toward intact).
- Mid-invasion states such as buildings catching fire during the fight.

## Risks

- **Budgets:** decals and fires are not in the Rendering Bible's performance table yet.
- **Mobile:** needs its own fallback for burn marks.
- **Terrain painting:** changing terrain layers at runtime in the editor writes to the terrain asset; the scorched
  layer needs a path that does not dirty `Zone01_TerrainData`.
- **Unverified:** how Synty's `Generic_Decals` shader behaves under URP, and how the charred tint reads under the
  comic post pass. Both are checked in the test scene.

## Where things go

| Content | Folder |
|---|---|
| Damage sites, resolver, state | `_Game/World/Runtime/` (new `Aftermath/`) |
| Ruin and wreck variants, bake tools | `_Game/Environment/Ruins/` |
| Charred materials | `_Game/Environment/Buildings/Materials/` |
| Decal shader, renderer setup | `_Game/Rendering/` |
| Fire and smoke prefabs | `_Game/VFX/Environment/City/` |
| GM commands | `_Game/Tools/Debug/` |
