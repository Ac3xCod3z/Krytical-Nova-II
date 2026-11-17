# The Crucible in Unity: the owner's list (16 Nov 2026, ~22:20), for the import crew

The owner approved the 022 look on the condition that Fable delivers exact light, fire and lava data (checkpoint 022b: FX_022.json,
LAVA_022.json, colliders, gate pivots, blockers, outer floor + peaks, size report). Start the import only after the Master confirms 022b.
Read first: `G:\My Drive\KryticalNova\AI Outputs\crucible\UNITY-IMPORT-PLAN.md`, `UNITY-FX-LIST.md` (022b), `IMPORT-REPORT_022.md`,
`CROSSFIRE-WARBOTS-BRIEF.md` (OWNER sections), and the memories ngo-rules-table-before-dots-port, profile-before-perf-chunks,
performance-target, one-user-unity-and-test-lane, claim-slot-before-any-checkout, client-bundles-match-scenes,
executealways-batchmode-packs-disabled. Before designing any system, build a rules table from how Zone01 does it today (file:line, numbers).
Everything must be written the way the new perf pass works (Burst jobs / ScheduleParallel for per-entity code, nothing visual on the server,
#327), so it never has to be redone. Locked 60 fps on mid hardware; measure with the perf log before and after.

## 1. Performance colliders
- GOAL: every surface you can stand on or hit has a cheap collider; no mesh colliders on detail props.
- WHERE: Fable's `Colliders_For_Unity` collection -> DOTS physics shapes (box/convex) in the Crucible subscene.
- HOW: import the proxies as physics shapes and strip colliders from the visual meshes; static, one compound per piece group.
- DONE WHEN: a bot can walk every route (validation's 129.5 m landing-to-heart routes x3) and the physics step in the perf log stays in budget.

## 2. Gates that open when Crossfire starts
- GOAL: the three Rally Gates (Hegemony, Covenant, Null) stay shut during the join window and open together when the war starts; the sealed north gate never opens.
- WHERE: gate leaves from Fable (own pivots, open pose noted); the war phase comes from the Crossfire flow (crossfire-b / successor: `CrossfireJoinSystems`, settle -> war).
- HOW: the server owns a replicated gate state (shut/opening/open) per gate, the collider follows it, and the client animates the mesh (presentation only, never on the server).
- DONE WHEN: a GIF shows a test war with gates shut through the 5-min join and 1-min settle, then opening at the start; blocked until open.

## 3. Nobody jumps out; Wallstride works on the walls
- GOAL: players can never leave the zone over the perimeter or yard walls, but can wall-run on them like in Zone01.
- WHERE: Fable's blocker volumes and Wallstride faces; Zone01's wall data (the "Zone01 style graph" for walls: find how Zone01 marks Wallstride walls in the DOTS project and copy that exactly).
- HOW: tall invisible blockers (collide with players only, not the camera), plus the wall graph baked for the Crucible walls the same way as Zone01.
- DONE WHEN: GIF of a wall-run along a yard wall and a failed jump-out at the top of the highest wall (and from the top of the tiers).

## 3b. Stairs you can run up (owner, 16 Nov: "the issue with Concord and its stairs that we weren't able to walk for a while")
- GOAL: every stair (the four grand stairs down to the heart, the Null gate stairs, the shrine and dais steps, the gate thresholds) can be walked and run up and down smoothly, and a click on a flight walks up it.
- WHERE (how Concord was fixed, DOTS project `C:\Users\dlaco\Krytical Nova II DOTS`):
  - `Assets/_Game/World/Editor/Concord040Setup.cs` L155-187 `Flights` + `GiveStairRamps`: an invisible BoxCollider ramp on the **Ground** layer for each flight (x min/max, z + y start, z + y end), its top face through the step nosings, 0.6 m thick; plus invisible Ground "Deck floors" boxes under the walkable floors (L189+, for the camera stop and clicks).
  - `Assets/_Game/World/Editor/SurfaceGraphBuilder.cs` L26-45: stairs must NOT become Wallstride walls: the stair footprints (`ConcordStairFootprints`) are excluded and the "Stair ramps" / "Deck floors" helpers are hidden while the wall graph is baked (stair sides are tall and wide and were read as walls).
- HOW: Fable's 022b export lists every stair flight in the same form (STAIRS in FX_022.json / LAYOUT_022.json); the Crucible setup script makes the Ground ramps from that list, then the surface graph is baked with those footprints excluded; the NavMesh/nav grid is (re)baked on the final colliders.
- DONE WHEN: the capsule-walk check (as in Concord slim phase 2: 0 wall blocks, 0 too-high steps, 0 no-ground steps) passes on all 3 x 129.5 m routes, and a GIF shows a run up a grand stair and up the Null gate stairs, and a wall-run that does not start on a stair side.

## 4. A small sky dome: live aurora, permanent night, the moon
- GOAL: replace the sky box with a small sky dome that has a live (moving) green and violet aurora, a permanently night sky with stars, and the moon up there.
- WHERE: Rendering Bible section 5 "Sky & celestial" and "Moon phases" (`Assets/_Game/_Docs/Design/Rendering-Bible.md`); Zone01's sky and moon setup.
- HOW: one dome mesh with a cheap shader (scrolling aurora noise bands, star field); the time of day is locked to night for this map; the moon is the same object/light as in Zone01; the peaks ring from Fable sits inside the dome.
- DONE WHEN: an in-game picture and a short GIF of the moving aurora; the shader costs under 0.3 ms on the GPU at High (perf log).

## 5. Outer floor and peaks from the reference
- GOAL: the ground outside the walls looks like the concept (black rock, lava fissures) with jagged peaks and mountains around the map.
- WHERE: Fable's outer floor + peaks ring (022b); concepts 05 and 11.
- HOW: static, LOD'd, non-playable (blocked); the lava fissures use the same emissive and haze setup as section 6.
- DONE WHEN: the picture matches concept 05 from the gate tops.

## 6. Lava that feels hot: heat haze + particles at the cracks
- GOAL: lava cracks (heart dais, arena floor, outer ground) pulse and look hot: heat haze concentrated right above the cracks, embers and a little smoke close to them.
- WHERE: LAVA_022.json (crack paths, haze zones, spawn points); FX_022.json.
- HOW: emission pulse in the material (shader-side, no per-frame CPU); one screen-space heat-distortion pass masked to the haze zones (URP renderer feature, one pass, half res) instead of many distortion particles; ember/smoke VFX Graph systems placed along the cracks, culled by distance and scaled by the Graphics tier.
- DONE WHEN: a GIF close to the heart cracks; perf log at the dais with every effect on stays at 60 fps on the mid test settings.

## 7. Moonlight and GI like Zone01; fire realism
- GOAL: the moon lights the map the way it lights Zone01, with GI from light probes so light bounces, and fires light their surroundings realistically.
- WHERE: Zone01's lighting setup (moon directional light, light probes/APV settings): copy it; FX_022.json light list and Fable's light budget.
- HOW: the moon is the one realtime shadowed light; fires are mostly emissive + baked into the probes, with a tiny unshadowed flicker light only where the budget allows; place probes densely in the yards, at the gates, on the tiers and around the dais.
- DONE WHEN: side-by-side pictures (moonlit yard, fire-lit Covenant brazier row, Null fire pit, heart at night) and the perf log at each spot.

## 8. Size and export
- Fable reports the .blend / GLB / texture sizes and any decimation before export. Textures: 8K source in Drive; ship what the Graphics tiers need. Tell the owner if the LFS push is over 100 MB before pushing.
