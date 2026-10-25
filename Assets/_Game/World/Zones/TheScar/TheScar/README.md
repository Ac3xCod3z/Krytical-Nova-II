# The Scar

**The Scar (contested).** Map scene: `TheScar`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 20, FX 2, Ground 24, Landmarks 5, Props 4). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

## The Scar (contested)

Atlas card: `World-Atlas.md`, "The Scar". No mockup yet; the region painting (`art/concept-scar.jpg`).

**Map: 3,000 × 1,500 m, terrain height 500 m. Lv 88-105 · always-on PvP.** A long valley running east-west.

**Description.** A vast rent of glassed earth. A severed extraction titan lies across the middle of it, its chest open, its arm reaching up the south wall. Black glass, ash drifts, cracked edges; the violet aurora overhead is brighter here than anywhere. Three faction gates open onto the valley, one at each end and one on the south wall; high on the north wall, a descent nobody uses yet. Beacon towers on the flanks. Everyone is pushed into the same valley.

**Life.** Hostile grounds 91-103, Bruisers 98 and 105 roaming the crossing (Hostiles plan). Players of every faction. No animals. Sound: wind over glass, the titan creaking, war horns from every side.

**Layout (valley centre 0,0, long axis east-west).**

```
  north wall (z +700, 400 m up) ........ Ronin descent (hidden) (+300,+680)
  Hegemony gate (W end, -1450,0)    Beacon West (-900,-300)   Beacon North (0,+450)
           The Glass Cliffs (z +250..+400, sniper ridges)
           THE SEVERED TITAN (lies -400..+500 along z 0); Ruptureworks portals under its chest (0,-60)
  Covenant gate (E end, +1450,0)   Beacon East (+900,-300)
  Null gate (south wall, 0,-700)
```

| Sub-area | Assets |
| --- | --- |
| **Valley floor** | black glass terrain layer, ash drifts `SM_Env_SandDunes_*` (Apocalypse Wasteland) greyed, `SM_Env_Cracked_Edge_*`, `SM_Env_Crater_Edge_*` (Sci-Fi Worlds), lava seams `SM_Env_Lava_River_*` (Arid Desert). |
| **The Severed Titan** | 900 m long, Colossal: the Dungeon Realms golem `SM_Env_GiantGolem_Chest_*`, `SM_Env_GiantGolem_Head_*`, `SM_Env_GiantGolem_Arm_R_Upper_*`, `SM_Env_GiantGolem_Leg_L_Lower_*` and the rest scaled about 20×, plated with Sci-Fi Worlds `SM_Env_Core_Shell_*`; you walk across it on the central crossing. |
| **Beacon sites** (3) | `SM_Bld_Corp_Comms_Tower_*` stripped to the frame, each on a mound of `SM_Env_Rock_Large_*`, ringed with barricades `SM_Prop_Barricade_*` (Maps Sci-Fi Outpost) and ruined walls `SM_Bld_Wall_L_Damaged_*` (Dark Fortress). |
| **The Glass Cliffs** | `SM_Env_Cliff_Curved_Large_*` (Sci-Fi Worlds), `SM_Env_Cliff_Basalt_*` (Dark Fantasy), landslides `SM_Env_Landslide_*`. |
| **The gates** | each faction's gate dressed in its own kit (Hegemony corp wall, Covenant carved arch, Null iron gate), the same pieces as its Z4's Scar Gate. |
| **Sky** | `SM_Env_CloudRing_*` (Sci-Fi Worlds), `FX_Aurora_Mesh_*` (Nature Biomes) in Rupture violet. |

**Points of interest.** Atlas: The Severed Titan, Beacon North / East / West, The Glass Cliffs, The Ruptureworks portals. New, my picks: *The Titan's Hand* (its hand on the south wall, a climbable vista); *Ash Flats* (the open killing ground between the Null gate and the titan). The Beacon site names stay open (Atlas).
