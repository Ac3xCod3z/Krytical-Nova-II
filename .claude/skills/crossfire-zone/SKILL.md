---
name: crossfire-zone
description: Build or refine a Crossfire / Siege Night war zone (Hollowgate Lv 10-29, Cinderford 30-49, Saltglass Redoubt 50-69, Stormbreak Bastion 70-89, The Crucible 90-112, or any new one) in Blender the way The Crucible was built - three faction Rally Point yards around a central battle area, a sealed gate, checkpoint after checkpoint against the owner's concept paintings until he approves the look, then the Unity data pass (exact lights, fire, lava, colliders, stair ramps, gates, Wallstride faces, blockers, sky dome) and the Unity hand-off brief. Use this whenever the user mentions a Crossfire zone, a Siege Night map, a war map, Rally Points, muster yards, a checkpoint of a zone look, the Crucible pipeline, a Refs_vs_Build sheet, FX_/LAVA_/STAIRS_ json for a zone, or asks to start, continue or refine any of those zones - even if they do not name the skill.
---

# Crossfire zones in Blender (the Crucible pipeline)

The Crucible (Lv 90-112) was built this way from checkpoint 007 to 022b and the owner approved it. Every lower Crossfire zone is
built the same way. This skill is that pipeline, with the scripts that ran it. **The look belongs to a Fable session**: it iterates
with the owner checkpoint by checkpoint; nothing goes into git or Unity until the owner approves the look.

## 0. Rules that never bend
- **Fable owns the look.** One Fable session per zone; report to the Master; the owner may talk to you directly.
- **Hand over at 50 % of `get_usage` (self)**, never by feel: write `<ZONE>-HANDOVER-<checkpoint>.md` in the shared notes folder with
  the exact plan list (step you are on marked), start a cold standby successor and send it "take over".
- **Nothing into git, Unity scenes, the repo or servers** during look work. Outputs go to Drive `G:\My Drive\KryticalNova\AI Outputs\<zone>\`
  and the zone's working folder `C:\Users\dlaco\Documents\ChatGPT\Krytical Nova\Worldbuilding\<Zone>\` (blends, Build scripts, Textures, Web).
- New numbered checkpoints (`<Zone>_0NN_<Name>.blend`), never overwrite one. Blender in the background (`-b -t 2`), GUI closed, close it
  when a job ends; other crews use Unity, keep RAM lean.
- Look at every picture yourself before you send it. Report every checkpoint with the links at the end (section 4).

## 1. Inputs (read and look at all of them first)
- Concept folder: `G:\My Drive\Krytical Nova Zones\Starter Zone Concepts\Siege Night\<Zone - Lv a-b>\` - every painting (yards, centre,
  panorama, layout map, top-down), `BATTLEFIELD_DESIGN.md`, `FINAL_MAP_README.md`, `SHOT_INDEX.md`, `Brackets\*\INSTANCE_BRIEF.md`.
- Owner refs and notes sent during the work (save them beside the handover, e.g. `<zone>-0NN-owner-images\`); webp: convert with PIL.
- Scale: `Assets/_Game/_Docs/Design/16_world_scale.md` - a person is 1.8 m; made things true size, at most 25 % over; grand things a
  stated ratio, never over 1.5x. Stand the Sidekick 1.8 m figures (`scale_figure.blend`, `ScaleFigure_*`, preview only) at landings,
  gates, crates, shrines, stairs and the centre in every checkpoint.
- Names and lore: `_Docs/Reference/12Sky/README.md`, `01_Systems_Build_List.md`, `Design/Lore-Bible.md` (Ronin stay hidden: sealed gate).
- Synty packs: `C:\Users\dlaco\Documents\SyntyPolygonForKN` and `Assets\Synty` (each pack on its OWN atlas - the first Crucible failure
  was pieces sampling another pack's atlas). The `synty-blender` skill knows the packs.

## 2. The layout rules (validated every checkpoint by `scripts/validate.py`)
- Three faction Rally Point yards (Hegemony, Covenant, Null; yaw per faction like `YAW` in `build_zone.py`) and one **sealed** gate
  (Ronin, expansion): a pile of big black shards in the gate mouth, rising above the wall, bound by heavy chains crossed tower to tower,
  purple fires on posts at the stair head; never opens; a blocker over it.
- **33 spawn points per faction**, none blocked; a landing point clear 7 m round (72 rays).
- **Landing-to-centre route lengths equal for all three factions** (spread 0.0 m), **max step 0.3 m** on every route and rampart.
- **Gates 11/11 lanes open**; every gate the same threshold (four 0.3 m steps each side of a 1.2 m platform).
- Respawn stand points on each shrine platform; rampart stairs walkable; Wallstride walls flat.
- `LAYOUT_0NN.json` records yards, spawns, landing, gates, battle area, and `changes_since_007` grows each checkpoint.

## 3. The look lessons the owner taught (do these from the first checkpoint)
- **No seat rows**: the bowl/terraces are **big rings of stone blocks** of one dark colour (1.07 m blocks, not walk-up), with **grand
  stairs** (14 m wide, 0.3 m risers, cheek walls) as the only routes.
- **No brick ruins / castle-wall ruins** anywhere in the battle area: cover is dark cut stone only - tall leaning slabs (menhirs, no brick
  coursing) and stacked rough blocks, low long blocks, fallen chunks (`cover_stone()` in `build_zone.py`).
- **No lava in the Null yard** (owner removed it); lava lives on the centre dais, the battle floor cracks and the outer ground.
- **Each faction its own palette and dressing**: Hegemony white/gold light, glowing pilaster light strips, light columns; Covenant warm gold,
  gilded finials, arched niches, gold canopies with stores, fire braziers on pedestals against the walls; Null charcoal stone, spiked
  towers with fire baskets, red canopies, chains, cages. **No free-standing pillars in the yard floor** ("random pillars look odd").
- **Gate exteriors dressed** (the arena side was "plain as can be"): banners on the outer wall, a string course round the gate towers,
  light strips or chains, fire/light at the gate mouth per faction.
- **Banners always in front of arches and wall pieces** (a Covenant banner hid behind an arch: check every banner against the piece behind it).
- **Normal + height maps on every stone material** (ground, walls, pillars, rocks, stairs, tier blocks, cover): `scripts/relief_maps.py`
  makes **8K** normal + height (Synty's own normals where the pack ships them, the rest from the colour with tint and painted light high-passed
  away: **no lighting baked in**); the Blender preview loads the 2K copies; `scripts/relief.py` wires them (UV-mapped or world box-projected).
- Fire reads in pictures: preview-only flame cones at every `BrazierFlame` marker (`PreviewFlames_PREVIEW_ONLY`, never imported).
- Pack pieces placed at pack size; the zone's own pieces modelled where no pack has one (gate leaves, canopies, shards, chains, dais, banners).

## 4. The checkpoint loop
1. Copy the last build script to `build_0NN.py` (header line: what changed and why, owner quote). Build:
   `blender -b <Zone>_007_KitLayout.blend -t 2 --python Build009/build_0NN.py -- <Zone>_0NN_<Name>.blend` (`build_zone.py` is the Crucible's
   022 build: keep its structure - yards, whole map, battle area, sealed gate, outside, preview flames, relief pass - and change the numbers
   from the zone's layout and concepts).
2. Validate: `blender -b <blend> --python validate.py -- 0NN` -> `VALIDATION_0NN.json` must PASS.
3. Render every ref angle (`render_shot.py`, one camera per painting and per owner image; add cameras to `SHOTS`), then
   `compare_refs.py` -> `Review0NN/Refs_vs_Build_0NN.jpg`: every owner ref beside the build at the same angle. **Look at it yourself**; fix
   what does not read as the same place before you send it.
4. Export the preview: `export_glb.py -- <Web dir> 0NN` (bumps `scene-review.json`); `export_objects.py -- 0NN` -> `<ZONE>_0NN_OBJECTS.json`
   + `IMPORT-REPORT_0NN.md`. Copy the sheet, layout, validation and reports to Drive.
5. Report to the Master: what changed, VALIDATION, triangles, the sheet, what is NOT verified, and end with
   `[phone + PC via Tailscale](https://ac3nsi0n.taile2bc25.ts.net/<zone>/review-3d.html) | [this PC](http://127.0.0.1:8769/<zone>/review-3d.html)`.
   Preview server: `scripts/preview/private_preview_server.py` (loopback 8769; add the zone to `ROOTS`); Tailscale Serve already proxies the
   tailnet name to the front router on 8768 - do not change the serve config.
6. The owner's notes come back -> next checkpoint. Keep a live plan list; at 50 % hand over.

## 5. The Unity data pass (after the owner approves the look; "precise locations for lighting, fire and its color, floor lava placement")
Run on the approved blend: `lava_paths.py` (system Python) then `unity_data_pass.py` (Blender) -> `<Zone>_0NNb_Data.blend` and:
- **FX_0NN.json** + `UNITY-FX-LIST.md` (`fx_list_md.py`): every light/fire/glow/shrine marker with Blender and Unity position
  (Unity = (-x, z, -y)), euler + quaternion, linear RGB, URP intensity, range, Realtime/Mixed/Baked, shadows, flicker, VFX. **Budget**:
  1 shadowed directional (moon, Mixed), the 3 shrine beams Mixed with soft low-res shadow, the centre lava Mixed without shadow, ~10 hero
  fires where players stand Realtime without shadow (range <= 10 m, flicker); every other fire, lava light and strip baked + emissive + APV.
- **LAVA_0NN.json**: crack paths skeletonised from the emission maps the floors use (exact to a few cm) with width, colour, HDR intensity;
  heat-haze boxes (16 m cells) and capsules; ember and smoke spawn points. Picture `LavaMap_0NN.jpg` (+ `_full`) with lights by mode (`overlays.py`).
- **Colliders** (`Colliders_For_Unity`): merged box colliders (`UCX_*`, unit cube x scale), convex hulls only for round daises and towers,
  plain mesh colliders only for simple ground (`mesh_collider_ok`), **no collider on detail**. Ramparts that bridge a gate are split so the gate
  stays open. Validate the routes on the colliders alone: `KN_VALIDATE_COLLIDERS=1 blender -b <blend> --python validate.py -- 0NNb` must PASS.
- **STAIRS_0NN.json (Concord rule: stairs must always be walkable):** every flight (grand stairs, gate thresholds, rampart stairs, any other
  steps) in Concord040Setup's `Flights` form `(x min, x max, z start, y start, z end, y end)` in Unity axes (flights along Unity x: the same form
  turned) with step height, count and tread; an invisible **Ground-layer** box 0.6 m thick whose top face runs through the step nosings, plus a
  1 m flat lip at the head; stairs and their side walls cut out of the Wallstride faces (like SurfaceGraphBuilder's ConcordStairFootprints).
- **GATES_0NN.json**: each faction leaf its own object, origin at the bottom centre of its closed place, slide axis/open position, a parented
  kinematic collider; they open when Crossfire starts.
- **WALLSTRIDE_0NN.json**: clean flat wall faces (4 m+), playable side only, stairs cut out. **Blockers** (`Blockers_For_Unity`, owner:
  nobody jumps out anywhere): invisible boxes from every outer wall top to 40 m, each run 2 m past both ends so corners overlap, one over
  every corner tower, one over the sealed gate. Check `Colliders_0NN.jpg` (blockers drawn on top): the red line must close round the whole
  outer square, the sealed yard and every yard back with no gap.
- Haze is intended only on the main cracks (16 m cells with 4 m+ of lava); the thin outer cracks just glow (performance). Say so in the FX list.
- **Sky**: no skybox - an unlit aurora **sky dome R 500 m**, far peaks (330-410 m, 95-190 m tall, no lava seams on them) and near crags as
  low-poly rock with the relief maps, a far outer floor ring in the concept's ground style. Camera far clip >= 600 m.
- **STATS / OPTIMISATION-REPORT**: tris per collection, objects, materials, draw estimate, texture memory (8K vs 2K), file sizes; decimation
  advice = Mesh LOD on the heavy Synty props, not a Blender decimate (the pieces are placed as the project's prefabs). Keep the 8K maps as the
  source and import at Max Size 2048 (4096 only for unique floors on High).
- Pictures you look at yourself: LavaMap, `Colliders_0NN.jpg` (stair ramps in white), `Colliders3D_0NN.png` (`render_colliders.py`), a panorama.

## 6. Hand-off to Unity
Write `<ZONE>-UNITY-BRIEF.md` from `references/UNITY-BRIEF-TEMPLATE.md` (the Crucible's brief) with the zone's numbers, plus the zone's
`UNITY-IMPORT-PLAN.md` (template: `references/UNITY-IMPORT-PLAN-crucible.md`) and `UNITY-FX-LIST.md`. The Unity crew follows CLAUDE.md
(project organisation, dependency map, registered prefabs, test with a second client). Tell the owner before an LFS push over 100 MB
(the 8K relief maps are about 0.5 GB per zone).

## Scripts (Crucible reference implementations: copy into the zone's Build folder and change the zone numbers)
| Script | Runs in | Does |
|---|---|---|
| `build_zone.py` (+ `parts.py`, `synty.py`, `arena_bowl.py`, `floors.py`) | Blender | the whole look build of one checkpoint |
| `relief_maps.py` / `relief.py` | Python / Blender | 8K + 2K normal and height maps; wiring them into stone materials |
| `validate.py` | Blender | routes, spawns, landing, gates, ramparts; `KN_VALIDATE_COLLIDERS=1` = on the colliders alone |
| `render_shot.py`, `compare_refs.py` | Blender / Python | one camera per ref; the Refs_vs_Build sheet |
| `export_glb.py`, `export_objects.py` | Blender | preview GLB (drops colliders); object list + import report |
| `lava_paths.py`, `unity_data_pass.py`, `overlays.py`, `render_colliders.py`, `fx_list_md.py` | Python / Blender | the Unity data pass and its pictures |
| `preview/` | Python + browser | the review-3d viewer and the loopback preview server |
