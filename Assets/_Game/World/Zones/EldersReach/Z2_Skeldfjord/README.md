# Skeldfjord

**Elders-Reach (Covenant).** Map scene: `EldersReach_Z2_Skeldfjord`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 64, Foliage 11, Ground 17, Props 18, Trees 14). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z2: Skeldfjord

**Map: 2,000 m square, terrain height 400 m. Lv 20-45.**

**Description.** Sea cliffs, black-sand coves, longship wrecks. The wind never stops. A drowned village shows at low tide: roofs and a church bell in the shallows. A watchtower stands on the cliff path; out on the point, a sea-stack shrine you reach over a broken rope bridge. Gulls, surf and wind.

**Life.** Hostile grounds 25-43, Bruisers 33 and 44 (Hostiles plan). Suggested species (proposals): drowned huskarls, sea crabs, kelp horrors. Seals on the rocks, gulls. Sound: surf, gulls, the creak of wrecked hulls.

**Layout.** The Atlas's coastline strip: cliff path above, tidal flats and coves below, the Sea-Stack Shrine out on the point.

```
  sea (west) | coves and flats (x -900..-400) | cliff path (x -400..-200, 60-120 m up) | moor (east)
  Sea-Stack Shrine (-950,+700)  Wreck of the Sigrun (-700,+300)
  The Drowned Village (-650,-250)   Cliff Watchtower (-300,0)
  S: arrival from Elk Pass (+200,-900)    N: portal to the Sylwood (+300,+880)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Cliff path** | 1.8 km long, 15 m wide | `SM_Env_Rock_Cliff_*` (Viking Realm) at 60-120 m, `SM_Env_Path_Rocks_Side_*` (Vikings), windswept pines `SM_Env_Tree_Pine_*` leaning, heather `SM_Env_Flax_*` (Vikings). |
| **Coves and black-sand flats** | 500 m wide band | `SM_Env_Beach_*`, `SM_Env_Beach_Long_*` (Vikings), driftwood `SM_Env_DriftWood_*`, kelp `SM_Env_Seaweed_Beach_*` (Tropical Jungle), rocks `SM_Env_Rock_Round_*`. |
| **The Drowned Village** | 250 m | Fantasy Village houses half-sunk: walls `SM_Bld_Wall_Curved_*` to the eaves in water, roofs `SM_Bld_Roof_Thatch_*`, jetties `SM_Prop_Jetty_Broken_*` (Swamp Marshland); a tide plane that drops 3 m on a cycle (a later system; build it at low tide). |
| **Wreck of the Sigrun** | 40 m hull | Bruiser 33: `SM_Prop_ShipWreck_*` (Tropical Jungle) recoloured to a longship, `SM_Prop_Boat_Destroyed_*` (Viking Realm) beside it. |
| **Cliff Watchtower** | | Wayfinder: `SM_Bld_Guard_Tower_*` (Vikings) with `SM_Prop_Beacon_*` (Viking Realm) fire basket. |
| **Sea-Stack Shrine** | stack 70 m high | `SM_Env_Rock_Cliff_*` stacked as a pillar, `SM_Bld_RopeBridge_*` (Tropical Jungle) broken half-way, shrine `SM_Bld_Shrine_*` (Fantasy Village) on top with `SM_Prop_RuneStone_*`. |
| **The moor** (east) | | heather, `SM_Env_Grass_*` (Vikings), cairns, the road inland. |

**Points of interest.** Atlas: The Drowned Village, Wreck of the Sigrun (Bruiser), Cliff Watchtower (Wayfinder), Sea-Stack Shrine. New, my picks: *The Bell Under the Tide* (the drowned village's chapel bell rings at low tide); *Wrecktide Cave* (the Run entrance at the cliff foot, only reachable over the flats); *The Whale Ribs* (`SM_Prop_Whale_Skeleton_*`, Tropical Jungle, on the north beach).
