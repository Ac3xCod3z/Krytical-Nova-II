# Transit Row

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_Z1_TransitRow`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 57, FX 3, Foliage 16, Ground 8, Lights 1, Props 55, Vehicles 13). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z1: Transit Row

**Map: 2,000 m square, terrain height 300 m. Lv 1-22.**

**Description.** The commuter belt right outside the wall. The elevated rail toppled into the streets years ago and nobody came to lift it; the overpass is now a ramp you walk up. Traffic died in its lanes. Holo-ads still flicker on the billboards for products nobody buys. Weeds push through the asphalt, the street trees are dead, and the wind carries grit off the ochre hills. New Meridian's Spire is always behind you on the skyline.

**Life.** Hostile grounds 1-22 in eight steps outward from the city gate (Hostiles plan section 10): low levels near the wall, the Bruisers at the river and the depot. Suggested species (proposals; the owner picks): scavenger drones, feral maintenance bots, rad-hounds. Marines at Checkpoint Delta. Crows on the dead trees. Sound: wind, buzzing neon, a far alarm that never stops.

**Layout.** The Atlas's ring road hugging the wall, as Zone01 runs its route anticlockwise; a river cuts off the north island where the portal to Crestfall sits.

```
 N island: portal to Crestfall (+300,+780)  . Platform 9 (-420,+700)
 ~~~~~~~~~~~~~ river (z +560..+640) ~~~~~~~~~~ Fallen Overpass crosses it (0,+600)
   Burning Depot (+650,+300)        Billboard Row (-600,+250)
        ring road (r ~650)  Checkpoint Delta (+120,-150)
   Commuter blocks W            Dead lot E
        S: arrival from New Meridian's north gate (0,-900)
```

| Sub-area | Size | What happens there | Assets |
| --- | --- | --- | --- |
| **Gate Approach** (south) | 400 × 300 m | arrival; first Hostiles (Lv 1, 4) | Sci-Fi City road tiles running north, `SM_Prop_Barrier_*`, `SM_Prop_Sidewalk_Barrier_*`, dead street trees `SM_Env_TreeDead_*`, `SM_Veh_Future_Taxi_*` and `SM_Veh_Classic_*` stalled in the lanes. |
| **Commuter blocks** (west) | 600 × 500 m | Lv 7-10; climbable ruins | gutted `SM_Bld_Large_*` and `SM_Bld_Section_*` shells with `SM_Bld_Base_Wall_Destroyed_*` (Generic), rubble `SM_Prop_Sidewalk_Rubble_*`, `SM_Prop_RubbishPile_*`; Wallstride walls on the shells, roofs walkable. |
| **Billboard Row** (north-west) | 500 m strip | Lv 13; a vista down the road | `SM_Sign_Billboard_Large_*`, `SM_Prop_Billboard_Damaged_Sign_*` (Cyber City), `SM_Prop_Sign_Billboard_Broken_*` (Apocalypse Wasteland) flickering. |
| **The Fallen Overpass** (centre-north) | 400 m long | the only dry crossing to the island; chokepoint | `SM_Env_Highway_Straight_Long_*` on `SM_Bld_Bridge_Supports_*`, one span tipped 20° to the ground (deck at 7 m above the road), `SM_Env_Highway_Rubble_*` under it, Cyber City `SM_Bld_Train_Rail_*` fallen across, Meadow Forest `SM_Env_Grass_Tall_Clump_*` in the cracks. |
| **Checkpoint Delta** (centre) | 120 m compound | Marine outpost, the **Wayfinder** | `SM_Bld_Military_Wall_*`, `SM_Bld_Military_Pillar_*`, `SM_Prop_Barricade_*`, `SM_Prop_Floodlight_*`, `SM_Prop_Container_*` (Maps Sci-Fi Outpost), `SM_Veh_Apc_*`. |
| **The Burning Depot** (east) | 250 m yard | Bruiser lair (Lv 12) | bus depot `SM_Bld_Warehouse_*`, `SM_Veh_Garbage_*`, `SM_Veh_Armored_Truck_*` burned, `FX_Fire_*` and `FX_Smoke_Drift_*` (Sci-Fi City), `SM_Prop_Tyre_Pile_*` (Apocalypse Wasteland). |
| **The river** | 80 m wide | splits the island off | Generic water, `SM_Env_River_Edge_*` (Apocalypse Wasteland), reeds `SM_Env_Reeds_*`. |
| **North Island** | 700 × 400 m | Lv 19-22, Bruiser 21; the portal | Platform 9: a sunken metro entrance `SM_Bld_Underpass_*` (Sci-Fi City) with `SM_Prop_Security_Camera_*`, `SM_Prop_LightBar_*` in emergency red. The portal stands in a ring of `SM_Prop_Pillar_*`. |

**Points of interest.** Atlas: The Fallen Overpass, Platform 9 (Substation entrance), Checkpoint Delta (Wayfinder), The Burning Depot (Bruiser). New, my picks: *Billboard Row* (the ads still play: "Necessity is Civilization"); *Lane 4 Jam* (two hundred metres of abandoned cars, a crawl-over route); *The Last Commuter* (a skeleton still seated at a bus stop, `SM_Prop_Skeleton_*` on `SM_Prop_Bench_*`).
