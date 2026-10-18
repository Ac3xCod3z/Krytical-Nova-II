# The Ember Terraces

**Sunderpeak (Ronin, hidden until the expansion).** Map scene: `Sunderpeak_Z1_TheEmberTerraces`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 23, FX 2, Foliage 5, Ground 17, Props 1). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z1: The Ember Terraces

**Map: 2,000 m square, terrain height 400 m. Lv 1-22.**

**Description.** Geothermal rice terraces and hot-spring pools under drifting mist. The valley's farms, warmed from below: green steps down the slopes, water steaming in the morning, geysers on a timer in the vent fields. The Watch Gate guards the only road out.

**Life.** Hostile grounds 1-22. Suggested species (proposals): steam imps, terrace boars, mist foxes. Herons and frogs (`FX_Frogs_*`). Sound: water, bubbling vents, a bamboo water-clapper.

| Sub-area | Assets |
| --- | --- |
| **Terraces** (stepped, following a stream) | `SM_Env_FarmTerrace_*`, `SM_Env_PaddyHills_*`, `SM_Env_CropRow_*`, `SM_Env_RicePlant_*` (Samurai); maples `SM_Env_Tree_Maple_*`. |
| **The Vent Fields** | geysers on a timer: `SM_Env_Sulphur_Pool_*`, `SM_Env_Sulphur_Ground_*` (Arid Desert) with `FX_Sulphur_Smoke`. |
| **Hot-spring Pools** | `SM_Env_PondEdge_*`, `SM_Env_PondGround_*` (Samurai), `SM_Env_Rock_Circle_*` (Samurai Empire); Hostiles bathe here too. |
| **Terrace Shrine** | `SM_Prop_Shrine_*`, `SM_Prop_Torii_Gate_*`, `SM_Prop_Lantern_Stone_*` (Samurai Empire). |
| **The Watch Gate** | Wayfinder: `SM_Bld_Gate_Dobei_*`, `SM_Bld_Fence_Dobei_*` (Samurai Empire), a watch tower `SM_Bld_Tower_*` (Samurai). |

**Points of interest.** Atlas: The Vent Fields, Terrace Shrine, Hot-spring Pools, The Watch Gate. New, my pick: *Steamhollow Mouth* (the Run entrance behind the hottest pool).
