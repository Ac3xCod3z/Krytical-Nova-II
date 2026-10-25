# New Meridian

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_NewMeridian`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 80, FX 1, Foliage 10, Ground 25, Landmarks 1, Lights 27, Props 146, Trees 27, Vehicles 8). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Home City: New Meridian

**Map: 1,600 m square, terrain height 200 m.** Atlas: concentric rings around the Core Spire. The mockup sets the shape: a **round walled city about 1,000 m across** on dry ochre ground, the sea to the north-west.

**Description.** You come out of the travel screen at the south gate under a 30 m wall, grey with amber bands and a cyan light strip running its whole length. Beyond the gate, a dead-straight avenue runs 500 m to the Core Spire, a lattice mast 180 m high with a golden crown and a ring of soul-light that hums. Every street is clean. Taxis are yellow, Marines are yellow, the planters are trimmed. Nobody here looks doubtful. Look up and the monorail slides past on its viaduct; look back over the wall and there is the shanty town nobody inside talks about.

**Life.** Marines in yellow armour on the gates and avenues (Sidekick Sci-Fi Soldiers), civilians in pairs (Sidekick Sci-Fi Civilians), hover traffic on the ring road, drones (`SM_Prop_Drone_*`) crossing overhead, gulls on the north-west shore. No Hostiles, no PvP (Atlas), though the owner's rule makes Home Cities faction-war maps: the wall and gates are built to be fought over. Sound: city hum, monorail whoosh, PA chimes, distant surf.

**Layout (metres, centre of the Spire at 0,0, north up).**

```
                  N gate (0,+500) -> Transit Row
         Highrise district (north arc, r 250-480)
   Sea   |                                   | Pylon lines (NE, outside)
  coast  |  Red-roof homes    CORE    Industrial yards
 (NW)  W gate  (west arc)    SPIRE    (east arc)      E gate
         |                  (0,0)     landing pad (+330,+40)
         |  Market blocks (SW)   Marine Barracks (SE)
                  S gate (0,-500)  <- arrival
         Shanty + solar farm (outside, -150..+150, -560..-720)
         Broken highway viaduct (SE, outside) . crater (E, outside)
```

- The wall: a ring of radius 500 m, 30 m high, gate towers 40 m at the four compass points.
- Ring roads at r 120 (Spire plaza), 260 (Exchange ring) and 420 (residential ring); eight radial avenues, 30 m wide, the north-south one the main axis.
- The monorail runs on a viaduct 12 m up along the r 260 ring, with one station per quarter.

**Districts and what goes where**

| District | Size | Assets |
| --- | --- | --- |
| **Core Spire plaza** (r 0-120) | 240 m disc | Spire: `SM_Bld_Corp_Comms_Tower_*` (Sci-Fi Worlds) as the lattice mast, scaled Colossal to 180 m, its base wrapped in `SM_Bld_Corp_Platform_*`; plaza paving `SM_Env_Ground_Tile_*` (Sci-Fi City); planters `SM_Env_Hedge_*`, `SM_Env_Tree_*`; `SM_Prop_Bench_*`, `SM_Prop_Streetlight_*`; holo signs `SM_Prop_Holo_Sign_*` (Cyber City). |
| **Exchange Concourse** (east of the plaza, r 120-260) | 4 blocks | `SM_Bld_Bank_*`, `SM_Bld_Shop_Open_*`, `SM_Bld_Advanced_*` (Sci-Fi City); Kiosks are `SM_Prop_MarketTable_*` + `SM_Prop_MarketCover_*` + `SM_Prop_MarketLights_*`; the mockup's food Kiosk is `SM_Veh_Food_Trailer_*` with the `Hologram_Dragon_*` sign (Cyber City); the Vault entrance is `SM_Prop_Bank_Door_*`. |
| **Highrise district** (north arc, r 250-480) | about 60 towers | `SM_Bld_Large_*`, `SM_Bld_Advanced_*` (Sci-Fi City) stacked to 40-120 m; skyline filler `SM_Bld_Background_Lrg_*`, `SM_Bld_Background_Building_*` (Cyber City) in the far ring; rooftop `SM_Prop_Antenna_*`, `SM_Prop_SatDish_*`, `SM_Prop_AirConditioningUnit_*`. |
| **Red-roof homes** (west arc) | low blocks, 2-4 storeys | Sci-Fi Worlds pods `SM_Bld_Pod_Hab_Large_*`, `SM_Bld_Pod_Hab_Small_*` (the mockup's orange roofs, recoloured terracotta), small yards with `SM_Env_Hedge_*`. |
| **Market blocks** (south-west) | colourful 1-2 storey streets | Cyber City shopfronts `SM_Bld_Wall_Shop_*`, awnings `SM_Prop_Canopy_*`, `SM_Sign_Neon_*`, `SM_Prop_Vending_Machine_*`, street food `SM_Veh_Food_Truck_*`. |
| **Industrial yards** (east arc) | hangars, pad | `SM_Bld_Warehouse_*`, `SM_Bld_Industrial_*` (Sci-Fi City), landing pad `SM_Bld_Corp_LandingPad_*` with `SM_Veh_DropShip_*` (Sci-Fi Worlds), containers `SM_Prop_Container_*` (Maps Sci-Fi Outpost), crane-yard `SM_Bld_Corp_Loading_Bay_*`. |
| **Marine Barracks** (south-east) | walled compound | `SM_Bld_Corp_Barracks_*`, `SM_Bld_Corp_Wall_*`, `SM_Veh_Apc_*` parked in a row (the mockup's six-wheeler), `SM_Prop_Barricade_*` training lane, flag line `SM_Prop_Banner_*` (Sci-Fi Worlds) in Hegemony yellow. |
| **The Pylon Garden** (west of the plaza) | 120 m park | a live soul conduit: `SM_Bld_Power_*` (Sci-Fi City) under a glass canopy `SM_Bld_Overhang_*`, planted with `SM_Env_Hologram_Tree_*` and `SM_Env_Hologram_Cherry_Tree_*` (Cyber City) beside real `SM_Env_Tree_Meadow_*`. |
| **The wall and gates** | ring r 500 | `SM_Bld_City_Wall_*` and `SM_Bld_City_Wall_Tower_*` (Sci-Fi City), gates `SM_Bld_Corp_Wall_Gate_*` (Sci-Fi Worlds) between `SM_Bld_Corp_Wall_Tower_*`; floodlights `SM_Prop_Floodlight_*` (Maps Sci-Fi Outpost) on the towers. |
| **Monorail** | viaduct r 260 | `SM_Bld_Train_Rail_Straight_*`, `SM_Bld_Train_Rail_Curve_Long_*`, `SM_Bld_Train_Rail_Strut_*` (Cyber City), a train of `SM_Veh_Train_Cockpit_*` + `SM_Veh_Train_Middle_*`. |
| **Outside: the shanty** (south of the south gate) | 300 × 160 m | `SM_Building_Shack_*` (Sci-Fi City), `SM_Bld_Crate_Building_*` (Sci-Fi Worlds), `SM_Prop_SolarPanel_*` in rows (the solar farm), `SM_Prop_Scav_Scrap_Pile_*`. The city's poor, in sight of the gate they may not enter. |
| **Outside: the broken viaduct and crater** (south-east and east) | | `SM_Env_Highway_Straight_Long_*` on `SM_Bld_Bridge_Supports_*`, ending in `SM_Env_Highway_Straight_Damaged_*` (Apocalypse Wasteland); a 60 m crater `SM_Env_Crater_*` where a Covenant raid hit the old road. |
| **Outside: the coast** (north-west) | | sea stacks `SM_Env_Rock_Large_*` (Sci-Fi Worlds) in the water, the ochre cliff `SM_Env_Cliff_Rough_*`, sea plane from the region water. |
| **Outside: pylon lines** (north-east) | | `SM_Prop_Powerline_*` (Sci-Fi Worlds) marching away across the ochre hills toward Transit Row. |

**Points of interest.** Atlas: The Core Spire, Exchange Concourse, Marine Barracks, The Pylon Garden. New, my picks: *The Gate of Necessity* (the south gate, its arch lettered with the Hegemony's creed); *Monorail Station Meridian-Central*; *The Shanty* (outside the wall); *Landing Pad One* (east).

**Gaps.** The Spire's crown and soul-light ring (a custom emissive ring, or `FX_RingLight_*` from Sci-Fi City at Colossal scale); a proper Hegemony crest for banners.
