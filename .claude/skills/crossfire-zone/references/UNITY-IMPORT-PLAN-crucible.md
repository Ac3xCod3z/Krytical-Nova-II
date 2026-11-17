# The Crucible - Unity import plan (checkpoint 022b)

Written by the look session. Nothing in the game repo, Unity or the servers was touched; this is the plan for the session that imports.
Source of truth: `Crucible_021_Scale.blend`, `CRUCIBLE_021_OBJECTS.json`, `IMPORT-REPORT_021.md`, `UNITY-FX-LIST.md`, `LAYOUT_021.json` (007 layout + the changes since),
`VALIDATION_021.json`, all in `Documents\ChatGPT\Krytical Nova\Worldbuilding\Crucible`.

## 1. What the map is
- Its own map zone (owner: Transit Row's map slot, its own server; players are ported there from Zone01). Origin = the Crucible heart.
- Blender axes: X east, Y north, Z up, metres. Rim and yards at height 0, arena floor at -7.5 (bowl halved in 019).
- 1,946 Synty placements, 208 custom placements, 64 kept layout meshes, 367 markers. 417k triangles at Synty LOD0. Exact lists: IMPORT-REPORT_021.md.

## 1b. Scale (owner rule, `Assets/_Game/_Docs/Design/16_world_scale.md`)
- One unit = one metre, a person is 1.8 m. The map was checked against a 1.84 m Sidekick figure (Soldier Male from `SyntyMannequinModeling.blend`).
- In range for their kind: crates <= 1.5 m longest side, barrels <= 1.25 m, brazier bowls <= 1.6 m (1.5 x on the monument pillars), cages, awnings, chains, skull piles at pack size.
- Grand on purpose (stated ratio, never scaled more than 1.5): big banners 6.0 m (3.3 persons), shrine crystals 5.5 to 7.4 m (4 persons), gates 12 x 8 m, yard walls 10 m, towers 15 to 16 m.
- Steps are 0.3 m (under the 0.35 m knee-high limit); tier blocks are 1.07 m (not walk-up, by design).
- `ScaleFigure_*` objects (collection `ScaleFigures_PREVIEW_ONLY`) are review aids only: do not import them.
- Bowl since 020: arena floor radius 82.3 m at z -7.5, seven rings of big blocks (`BowlNew_Tiers_n`), grand stairs with cheek walls (`BowlNew_GrandStair_n`), floor (`BowlNew_Floor_n`).

## 2. Synty pieces: place the project's own prefabs, do not import meshes
Each Synty object in the JSON has `synty_pack`, `synty_prefab`, `position`, `rotation_deg`, `scale`, `pivot` and (if any) `material_variant`.
- Prefab to place: `Assets/Synty/<synty_pack>/Prefabs/**/<synty_prefab>.prefab` (found by name; the look session's loader used the same files).
- Pivot: pieces placed by this session were re-pivoted in Blender to the centre of their footprint on the ground. `pivot` is the vector from the
  prefab's own origin to that point, in the piece's local metres (Blender axes). Prefab origin in world = object matrix x (-pivot).
  Pieces re-bound from the 007 build have pivot 0,0,0 (prefab origin unchanged).
- Axes: Blender (x, y, z) -> Unity (-x, z, -y) is the usual FBX convention, but Synty FBX roots differ per pack. NOT verified in Unity by this session:
  place one piece per pack first (a wall, a brazier, a crate) and compare with the preview before running the whole list.
- Many pieces are scaled non-uniformly (walls stretched to bay length, piers scaled to size). Keep the scale from the JSON.
- LOD: use the prefabs' own LOD groups; the 445k count is LOD0 everywhere. Per the project rule, Mesh LOD import setting, static batching off on Synty.

## 3. Material variants (stone colour per faction, glow)
Materials named `<Pack>/<Material>__<Tag>` are variants of the Synty material `<Material>`: same textures, a multiply tint (`tint` in the JSON) and
sometimes emission. Tags: Hegemony, Covenant, Null (yard stone), Arena, Tier, Stair, Rim (arena stone), `<Faction>Glow` (light strips: emission colour
= the faction glow), Glow (sci-fi columns: emissive on), NullRed (red awning). In Unity: material variants of the Synty materials with `_Color_Tint`
set to the tint; light-strip variants get emission colour Hegemony (0.36, 0.62, 1.0), Covenant (1.0, 0.62, 0.12), Null (1.0, 0.10, 0.06).
The untagged Dark Fortress brick is itself tinted (1.05, 0.95, 1.30) for the neutral arena / perimeter / sealed-north stone.

## 4. Custom pieces (export as FBX from the blend)
- `KN_Crucible_Banner_<Faction>`, `_BannerMid_`, `_BannerSmall_`: sigil banners with iron brackets (textures `T_Banner_<Faction>.png`). Rest pose; sway is an effect.
- `KN_Crucible_GateLeaf_<Faction>_L / _R` and `KN_Crucible_GatePortcullis_Null`: the braced plank gates. The objects keep the 007 names and pivots
  (`GateLeaf_<Faction>_L/R`, `GateLeaf_Null_Portcullis`, `GateLeaf_Sealed_North`), so the MusterGate logic and markers from LAYOUT_007 still apply.
- Centrepieces from the owner's close-up refs (checkpoint 021): `KN_Crucible_ShrineDais_<Covenant|Null>` and `KN_Crucible_ShrinePlinth_Hegemony` (these replace the
  mesh of the 007 `Yard_<Faction>_RespawnShrine` objects, same name and marker), `KN_Crucible_ShrineCrystal_<Faction>`, `KN_Crucible_ShrineBlock_<Faction>`,
  `KN_Crucible_DaisRingBlock` (16 kerb blocks round the heart dais), `KN_Crucible_Menhir1..6` (standing stones), `KN_Crucible_Finial_Covenant` (tower tops).
  Their stone faces use the same Synty brick / paving materials as the walls and floors; glow and gold faces use `M_Crucible_Glow_<Faction>` / `M_Crucible_Accent_<Faction>`.
- Kept layout meshes (floors, tiers, stairs, rim, ramparts, shrine plinths, dais, landing pads, corner towers, outer ground): export as they are; they are
  the collision the validation walked. Faces looking up carry the floor material, the other faces the wall brick (two material slots).
- Folder: by owning system per `ProjectOrganization.md` (the importing session decides; e.g. the World system's map folder), with an Editor builder that reads the JSON.

## 5. Textures to bring in (about 12 MB; tell the owner before the LFS push)
- `Textures\Crucible013\`: `T_Floor_Yard_<Faction>.jpg` x3 (2048 x 4096, mapped once per yard), `T_Floor_Yard_Null_Emit.jpg`, `T_Floor_Arena_Quarter.jpg` (+`_Emit`),
  `T_Floor_Rim_Quarter.jpg`, `T_Floor_Dais.jpg` (+`_Emit`), `T_Crucible_Paving.jpg` (16 m tile, tinted per area).
- `Textures\Crucible009\`: `T_Banner_<Faction>.png` x3, `T_Crucible_Planks.png` (gates), `T_Crucible_Basalt.png` + `_Emit` (dais), `T_Crucible_OuterRock.png` + `_Emit` (outer ground, 170 m tile).
- Not needed: the Ashlar/Flagstone tiles of the first pass, the 006 sky cubemap (the sky is a new skybox, see the FX list).

## 5b. Normal and height maps (checkpoint 022, owner: "our materials are using normal and height maps ... rocks walls pillars")
- `Textures\Crucible022\<stem>_Normal_8K.png` (OpenGL / Y+, import as Normal map) and `<stem>_Height_8K.png` (import as Height / parallax, URP Lit
  Height Map, start at 0.02) for 14 stone textures: the Dark Fortress Brick Large / Medium, SmoothStone (+Dark) and Rock Cliff (Synty's own normals at 8K),
  Fantasy Kingdom Wall Brick, the Crucible paving and every floor painting (yards, arena quarter, rim quarter, dais) and the outer rock. Heights come from the
  colour with the broad tint and painted light removed (no lighting baked in); the lava cracks of the _Emit maps are pressed down. The `_2K` copies are
  only for the Blender preview.
- Each Blender material records `normal_map_8k`, `height_map_8k` and `relief_mapping`: `uv0` = same UV as its colour texture; `triplanar_world` =
  the Rock Cliff maps box-projected in world metres (`relief_tile_m`, 3 to 4 m) on pieces that only have a colour atlas or a flat colour (tier blocks,
  stair cheeks, cover stones, shards, rocks): in Unity a world-space triplanar detail normal + height on those materials.
- Size: the 8K files are about 0.5 GB. Tell the owner before the LFS push (CLAUDE.md, 100 MB rule); Synty normals already in the repo can be reused
  at 4K if he prefers not to store the 8K upscales of them.
- `PreviewFlames_PREVIEW_ONLY` (emissive flame cones at every BrazierFlame marker) are preview stand-ins: never import them; Unity's live fire sits on
  the FX markers.

## 6b. Colliders, gates, Wallstride, blockers (checkpoint 022b; data in COLLIDERS_022.json, GATES_022.json, WALLSTRIDE_022.json)
- `Colliders_For_Unity`: 1,028 `UCX_*` objects that share one unit cube: make each a **BoxCollider** from its transform (no mesh). 17 `UCX_*` convex
  meshes (rampart stairs with a floor lip so they start as ramps, gate thresholds, respawn daises, sealed gatehouse, perimeter corner towers):
  **MeshCollider, convex**. The 16 objects flagged `mesh_collider_ok` (yard floors, rim plateau, arena floor quarters, heart dais, landing pads)
  keep a plain MeshCollider. **Every other art mesh gets no collider.** Walls are merged into solid runs; ramparts are two solid ends plus the
  deck over the gate; the seven tier rings are boxes per 15 degrees; the grand stairs are ramps. Proven: `VALIDATION_022b_COLLIDERS.json`
  (routes, spawns, landings, gates and ramparts walked on the colliders alone) PASS.
- Gates (`GATES_022.json`): GateLeaf_Covenant_L/R, GateLeaf_Hegemony_L/R slide 6.3 m sideways into the wall; GateLeaf_Null_Portcullis rises
  8.3 m; GateLeaf_Sealed_North never opens. Each leaf's origin is the bottom centre of its closed place, the slide axis runs through it; open
  position, slide vector and a parented kinematic `UCX_GateLeaf_*` box are given. They open together when Crossfire starts (2.5 s ease).
- **Stairs (`STAIRS_022.json`, owner: Concord's stairs could not be walked, never again):** 16 flights (4 grand stairs, 6 gate-threshold
  flights, 6 rampart stairs) in Concord040Setup's `Flights` form (x min, x max, z start, y start, z end, y end; flights along Unity x give the
  same form turned) with step height, count and tread. Each has an invisible Ground-layer BoxCollider 0.6 m thick whose top face runs through
  the step nosings, plus a 1 m flat lip at the head (`UCX_StairRamp_*`, `UCX_StairLanding_*`). Stairs and their side walls are cut out of the
  Wallstride faces (like SurfaceGraphBuilder's ConcordStairFootprints); `side_walls_not_wallstride` lists them. Round daises keep convex hulls.
- Wallstride (`WALLSTRIDE_022.json`): 36 flat wall faces, 4 m+ tall and long, playable side only, as four corners + normal in Unity axes.
- `Blockers_For_Unity`: 21 invisible boxes from the wall tops up to 40 m on every wall with the outside behind it, plus one over the sealed
  gate; put them on a player-only layer so arrows and cameras pass.

## 8b. Sky (022b)
- The skybox is replaced by an unlit aurora **sky dome, radius 500 m** (`SkyDome_R500`, inward hemisphere, `Crucible_Sky.png` as the stand-in
  picture), 18 far peaks (`Outer_Peak_*`, 330-410 m out, 95-190 m tall) and 28 near crags (`Outer_Crag_*`, 215-290 m) as low-poly dark rock
  with the rock relief maps, and a far outer floor ring (`Outer_Ground_Far`, 200-500 m, z -12.5, the lava-cracked rock). Camera far clip >= 600 m.

## 6. Collision
- Mesh colliders on the kept layout meshes. Synty prefabs keep their own colliders. Outer ground and `Outer_Rock_*` are outside the play bounds.
- No collider: banners, light strips, FX markers, the sky stand-in. Rubble under 0.5 m: no collider (do not snag players).
- Gate leaves are movers (closed at muster, open at war start); the sealed north gate never opens.

## 7. Markers
- From the 007 layout (unchanged): `MARK_*` (CrossfireArea boxes, Landing, MusterGate, RespawnShrine, CrucibleHeart), `Spawn_<Faction>_01..33`. Numbers in LAYOUT_007.json.
- `FX_<Type>_<Area>_<nn>` (231): see `UNITY-FX-LIST.md`. `MAP_Bounds_Crucible`: the map box.
- Not for import: collection `Lights_PreviewOnly`, `SkyboxStandIn_PREVIEW_ONLY` and its faces.

## 8. Lighting and sky (Unity's job)
- Sky: a skybox cubemap (row "Skybox" in the FX list says what it must show). No sky geometry.
- Night scene, fixed time. Each FX marker with a colour is also where a light belongs; the Blender preview lights show the intended falloff only.

## 9. Order of work suggested
1. One test piece per Synty pack to confirm axes and pivot. 2. Builder: Synty prefabs from the JSON. 3. Custom FBX + kept layout meshes + colliders.
4. Material variants. 5. Markers and gates. 6. Skybox, lights, effects from the FX list. 7. Walk test with a second client; compare with the five shots.
