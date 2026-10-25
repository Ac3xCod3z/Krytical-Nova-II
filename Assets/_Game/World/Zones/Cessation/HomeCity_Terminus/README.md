# Terminus

**Cessation (Null).** Map scene: `Cessation_Terminus`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 130, FX 1, Ground 7, Lights 7, Props 81). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Home City: Terminus

**Map: 1,600 m square, terrain height 400 m.** Atlas: a crater city of gear and bone; a spiral road down the crater wall to the Assembly Core; the Ossuary Spire on the rim. The mockups add the gothic iron fortress on the ridge.

**Description.** The crater is 900 m across and 250 m deep. A spiral road winds down its wall in three turns, lined with conveyor streets where skeletal and machine citizens move in perfect order, never hurrying, never stopping. At the bottom the Assembly Core hums under a lattice dome. On the rim, the Ossuary Spire and the iron fortress stand against the aurora. Nobody raises their voice. The only colour is the red of the banners and the violet of the lamps.

**Life.** Null citizens (Sidekick Fantasy Skeletons, Sci-Fi Robots) in lines; drones; conveyor carts moving boxes of bone. No animals. Sound: a deep machine hum, conveyor clatter, a bell that rings once an hour exactly.

**Layout (crater centre 0,0).**

```
   Iron fortress on the north rim (0,+520)   Ossuary Spire (+380,+380, rim)
   spiral road: rim (r 450, 250 m up) -> r 300 (160 m) -> r 150 (70 m) -> floor (r 100, 0 m)
   Reclamation Yards (west terrace, r 300)   Conveyor Concourse (east terrace, r 300)
   Assembly Core (0,0 on the floor)
   south rim gate (0,-520) -> The Palefields
```

| District | Assets |
| --- | --- |
| **The crater walls** | `SM_Env_Cliff_Curve_Large_*` (Dark Fortress), `SM_Env_Cliff_Basalt_*` (Dark Fantasy), frozen falls `SM_Env_Water_Plane_Waterfall_*` (Dark Fortress) in ice-white. |
| **The spiral road** | 12 m wide, paved with `SM_Env_Path_Tiles_*` (Dark Fortress); railings `SM_Bld_Bridge_Railing_*` (Dark Fantasy); lamps `SM_Prop_Brazier_*` burning violet. |
| **Conveyor Concourse** (Broker, Kiosks) | conveyor belts from `SM_Bld_Scav_Refinery_Pipe_*` laid flat and `SM_Prop_Pod_Pipe_*` (Sci-Fi Worlds); stalls `SM_Bld_Vendor_*`; boxes `SM_Prop_Crate_*`. |
| **Reclamation Yards** (Armorer, Fixer) | `SM_Bld_Scav_Garage_*`, `SM_Bld_Scav_Refinery_*` (Sci-Fi Worlds), robot parts `SM_Prop_Robot_Part_*`, `SM_Prop_Robot_Posed_Pile_*` (Cyber City). |
| **Assembly Core** (Chancellor, Herald) | Sci-Fi Worlds `SM_Env_Core_*` at the crater floor, under a cage of `SM_Env_Core_Strut_*`; `FX_Core_*`. |
| **Ossuary Spire** | 120 m: Dark Fortress round walls `SM_Bld_Wall_L_Curve_*` stacked, `SM_Bld_Tower_Roof_*` cap; inside, niches of skulls `SM_Prop_Skull_Pile_*` in exact rows. |
| **The iron fortress** (north rim) | the mockups' castle: `SM_Bld_Wall_L_*`, `SM_Bld_Wall_L_Gate_*`, `SM_Bld_Wall_L_Curve_*`, `SM_Bld_Tower_Roof_*`, `SM_Bld_Drawbridge_01_Preset_*` (Dark Fortress); red banners `SM_Prop_Banner_*`. |

**Points of interest.** Atlas: The Assembly Core, The Reclamation Yards, The Ossuary Spire, Conveyor Concourse. New, my picks: *The Count* (an hourly bell in the Spire that every citizen stops for); *The Rim Gate* (south, toward the Palefields); *The Frozen Fall* (a waterfall frozen down the crater wall, a vista).
