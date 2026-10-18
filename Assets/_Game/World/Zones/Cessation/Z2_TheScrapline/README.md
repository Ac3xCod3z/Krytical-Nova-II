# The Scrapline

**Cessation (Null).** Map scene: `Cessation_Z2_TheScrapline`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 20, FX 3, Foliage 5, Ground 10, Props 146, Vehicles 7). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z2: The Scrapline

**Map: 2,000 m square, terrain height 400 m. Lv 20-45.**

**Description.** A trench grid of machine graveyards. Chassis stacked like rock strata form whole cliff walls. Scrap bridges cross the cuts. Magnetic storms roll over the plateau, and loose metal lifts and drifts in the air when they pass. In the central cut a crashed hauler, the Magnet, lies across the trench like a dam.

**Life.** Hostile grounds 25-43, Bruisers 33 and 44 (the Magnet). Suggested species (proposals): scrap golems, magnet-wraiths, salvage swarms. No animals. Sound: metal groaning, storm crackle, distant hammering from Reclamation Camp.

**Layout.** The Atlas's parallel trenches cut across a plateau; scrap bridges link the plateaus; the crashed hauler blocks the central cut.

```
   Plateau N ---- Scrap Bridge B ---- Plateau N2      portal to the Needlegarden (+400,+880)
   ====== trench 3 (z +400, 40 m deep) ======
   Plateau M      The Magnet (0,+50) blocks trench 2      Reclamation Camp (+600,+100)
   ====== trench 2 (z 0) ======   The Chassis Wall (-600, both sides)
   Plateau S ---- Scrap Bridge A (-200,-400)
   ====== trench 1 (z -400) ======
          S: arrival from the Palefields (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Trenches** | 3 cuts, 40 m deep, 60-100 m wide | walls of `SM_Env_Cliff_Flat_*` (Sci-Fi Worlds) faced with stacked scrap `SM_Env_Scrap_Pile_*`, `SM_Env_Junk_Pile_*` (Cyber City), `SM_Prop_Scav_Scrap_Pile_*`; floors of rust lichen (Dark Fantasy `SM_Env_Grunge_*`, `SM_Env_Moss_*`). |
| **The Chassis Wall** | 400 m cliff | strata of robot bodies: `SM_Prop_Robot_Posed_Pile_*` (Cyber City), Mech pack frames `SM_Mech_*`, `SM_Veh_Truck_*` crushed between. |
| **Scrap bridges** | 2, 80-120 m | chokepoints: `SM_Bld_Bridge_L_*` (Dark Fortress) patched with `SM_Bld_Scav_Wreckage_*`, cables `SM_Prop_Tether_Wire_*` (Sci-Fi Worlds). |
| **The Magnet** | 160 m hull | Bruiser 44: a crashed hauler `SM_Ship_Massive_Transport_*` (Sci-Fi Space) at ground scale, broken in two, `FX_Electricity_*`, floating scrap `FX_Scrap_Floating_Mesh_Full_*` (Sci-Fi Worlds). |
| **Reclamation Camp** | 150 m | Wayfinder: `SM_Bld_Crate_Building_*`, `SM_Bld_Scav_Garage_*`, `SM_Bld_Scav_Dish_*` (Sci-Fi Worlds), red banners. |
| **Plateaus** | | cable vines `SM_Prop_Cable_*`, hull plates `SM_Env_Debris_Shell_*` (Sci-Fi Space), `SM_Env_Ground_Junk_*` (Sci-Fi Worlds). |

**Points of interest.** Atlas: The Chassis Wall, Scrap Bridge (chokepoint), The Magnet (Bruiser), Reclamation Camp (Wayfinder). New, my picks: *The Disassembly Gate* (the Run entrance cut into the Chassis Wall); *Storm Spire* (a lightning-struck mast where the storms start); *The Sorting Field* (parts laid out by size on a plateau).
