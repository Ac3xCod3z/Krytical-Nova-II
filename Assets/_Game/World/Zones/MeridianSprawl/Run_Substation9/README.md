# Substation 9

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_Run_Substation9`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 23, FX 1, Props 11). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

**Run: Substation 9** (1 room, 400 m map, the room about 60 × 40 m, 14 m high). A flooded metro power substation. Arc-light, dripping water, rogue maintenance drones still following their last order. Assets: Sci-Fi Space `SM_Bld_Wall_Mechanical_*`, `SM_Bld_Corridor_Double_Arch_*`, `SM_Prop_Detail_Pipe_*`; Cyber City `SM_Bld_Pipe_Large_*`; water plane at 0.6 m with `FX_Electricity_*` (Sci-Fi City) arcing off `SM_Bld_Power_*` transformers; drones `SM_Prop_Drone_*` on patrol rails. Layout: entry stairs from Platform 9 → catwalk ring 4 m up around a central transformer pit → the boss floor under the main breaker.
