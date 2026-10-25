# Kagero

**Sunderpeak (Ronin, hidden until the expansion).** Map scene: `Sunderpeak_Kagero`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 55, FX 2, Foliage 2, Ground 16, Lights 23, Props 13). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Hidden Capital: Kagero

**Map: 1,600 m square, terrain height 500 m.** Atlas: terraces climb the valley wall from the hot springs to the Council. The mockup makes it a **gorge city**: terraces on both walls, a waterfall at the head, red arched bridges across.

**Description.** The gorge is 300 m deep. A waterfall closes its head in a white curtain; below it the river runs blue between black rocks. Both walls are terraced: lantern-lit houses, red-railed walkways, stairs everywhere, cherry trees in bloom, rice terraces glowing with the sky in their water. Two red arched bridges cross the gorge at different heights. Pagodas crown the top terraces against snow peaks. Machines, skeletons, humans and elves share one street. Named for heat-haze: seen and not seen.

**Life.** Ronin citizens of every origin (Sidekick Samurai Warriors, Fantasy Skeletons, Sci-Fi Robots, Elven Warriors together); cranes (`FX_Cranes_*`, Nature Biomes) over the gorge. Sound: the waterfall, wind chimes, a temple bell.

**Layout (gorge centre 0,0, running north-south, waterfall at the north end).**

```
        waterfall (0,+600)  Council of Three (pagoda, east top terrace +200,+450, 280 m up)
   West wall terraces (x -300..-120)  | river | East wall terraces (x +120..+300)
        Upper bridge (0,+250, 180 m up)     The Shrine of the Unerased (-220,+380, top)
        Mixed Market (east, +200,0, 120 m up)  rice terraces (west, -250,-150)
        Lower bridge (0,-100, 60 m up)      The Steam Baths (0,-400, gorge floor)
        hidden road out (south, 0,-780) -> The Ember Terraces
```

| District | Assets |
| --- | --- |
| **Terraces** (both walls) | retaining walls `SM_Bld_Platform_Wall_*`, `SM_Bld_Platform_Pillar_*` (Samurai Empire), houses `SM_Bld_House_Base_*` with `SM_Bld_House_Roof_Wood_*` (Samurai), walkways with `SM_Bld_Railing_*`, stairs `SM_Bld_Stairs_Wide_*`, `SM_Env_Stone_Stairs_*`. |
| **Bridges** | red arched bridges `SM_Bld_Bridge_*`, `SM_Bld_Bridge_Pillar_*` (Samurai Empire), `SM_Env_Bridge_Curved_*` (Samurai), stretched to 120-180 m spans on stone piers. |
| **The Council of Three** | a three-tier pagoda: `SM_Bld_Castle_Base_Large_Windows_*`, `SM_Bld_Castle_Roof_Large_*`, `SM_Bld_Castle_Roof_Top_*` (Samurai), 40 m. |
| **The Mixed Market** | stalls `SM_Bld_Awning_*`, `SM_Prop_Lantern_*`, `SM_Prop_Banner_*` (Samurai Empire); Broker and Kiosks. |
| **The Steam Baths** | `SM_Prop_Bath_*` (Samurai Empire), pools `SM_Env_PondEdge_*`, `SM_Env_PondGround_*` (Samurai), steam. |
| **The Shrine of the Unerased** | `SM_Bld_Shrine_*`, `SM_Prop_Shrine_*`, `SM_Prop_Torii_Gate_*` (Samurai Empire); walls of name tablets. |
| **Rice terraces** | `SM_Env_FarmTerrace_*`, `SM_Env_RicePlant_*` (Samurai) on the west wall, lit water. |
| **Waterfall** | `SM_Env_Water_Plane_Waterfall_*` (Samurai Empire) 250 m tall, `FX_Waterfall_Impact_*` (Nature Biomes). |

**Points of interest.** Atlas: The Council of Three, The Steam Baths, The Mixed Market, The Shrine of the Unerased. New, my picks: *The Two Bridges*; *The Lantern Stair* (a stair of 800 lanterns up the east wall).
