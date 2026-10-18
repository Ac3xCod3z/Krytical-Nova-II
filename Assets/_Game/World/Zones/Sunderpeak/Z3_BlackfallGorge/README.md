# Blackfall Gorge

**Sunderpeak (Ronin, hidden until the expansion).** Map scene: `Sunderpeak_Z3_BlackfallGorge`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 18, FX 4, Ground 3, Lights 3, Trees 14). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z3: Blackfall Gorge

**Map: 2,000 m square, terrain height 600 m. Lv 45-67.**

**Description.** Black-water falls, rope bridges, lantern-cut cliffs. Roar and spray everywhere; the light comes from ten thousand lanterns set into the rock. Cliff paths run along both walls, rope bridges swing between them, and the Great Fall closes the far end.

**Life.** Hostile grounds 49-67. Suggested species (proposals): spray wraiths, cliff apes, lantern moths. Hawks (`FX_Hawk_*`). Sound: the falls, always.

| Sub-area | Assets |
| --- | --- |
| **Cliff paths** (both walls) | `SM_Env_Rock_Cliff_*`, `SM_Env_Wall_Rock_*` (Samurai Empire), ledges `SM_Bld_Platform_*` (Samurai), cliff pines `SM_Env_Tree_Pine_*`, spray moss `SM_Env_Moss_*`. |
| **Rope bridges** | `SM_Bld_RopeBridge_*` (Tropical Jungle), 60-150 m spans; Rope Bridge Nine holds the Bruiser. |
| **Lantern Cliffs** | the Run entrance: cliffs studded with `SM_Prop_Lantern_Wall_*`, `SM_Prop_Lantern_Feature_*` (Samurai Empire). |
| **The Great Fall** | 300 m: `SM_Env_Water_Plane_Waterfall_*` (Samurai Empire), `FX_Waterfall_Impact_*`, `FX_Water_Splash_*`. |
| **The Drowned Gate** | Wayfinder: a torii `SM_Prop_Torii_Gate_*` standing in the river. |

**Points of interest.** Atlas: The Great Fall, Rope Bridge Nine (Bruiser), Lantern Cliffs (Run entrance), The Drowned Gate (Wayfinder). New, my pick: *The Ten Thousand* (the lantern wall seen from the lower bridge, a vista).
