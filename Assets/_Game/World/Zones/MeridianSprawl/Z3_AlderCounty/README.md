# Alder County

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_Z3_AlderCounty`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 51, Foliage 29, Ground 1, Props 18, Trees 2, Vehicles 7). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z3: Alder County

**Map: 2,000 m square, terrain height 300 m. Lv 45-67.**

**Description.** Upstate farm belt strung with soul-pylon lines. Automated harvesters still cut fields nobody eats from. Apple orchards in rows, cornfields to the horizon, red barns, hay bales, a river with a covered bridge. Then the craters and the burned farmsteads, where the Covenant raids landed. At night Pylon Junction 4 lights the sky soul-blue.

**Life.** Hostile grounds 49-67 (Hostiles plan), Bruisers 57 and 67. Suggested species (proposals): harvester-bots, field ghouls, raider bands. Crows, cattle bones, a lone horse. Sound: harvester engines far off, wind in the corn, the pylon hum rising toward the junction.

**Layout.** The Atlas's rolling patchwork split by a river; the pylon line is the leading line to Fort Alder and the Silo.

```
  Pylon Junction 4 (+600,+650)  ---- pylon line ---- Fort Alder (-300,+600) -> portal to Gantry Ridge
       Silo 12 (+250,+250)            Burned Farmstead (-550,+200)
  ~~~~~~~~~ river (z -100..-20), covered bridge (0,-60) ~~~~~~~~~
       The Orchard Rows (+450,-350)     Cornfields (-400,-450)
        S: arrival from Crestfall (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Fields** (everywhere) | 100-200 m patches | crop rows `SM_Env_CropField_Clump_*`, `SM_Env_Sunflower_*`, hay `SM_Prop_HandCart_*`, fences `SM_Prop_Meadow_Fence_*`, scarecrows `SM_Prop_ScareCrow_*`, windmill `SM_Bld_Windmill_*` (Meadow Forest); hedgerows of `SM_Env_Bush_*`. |
| **The Orchard Rows** (south-east) | 300 × 300 m | dense Hostile ground: `SM_Env_Tree_Fruit_*` (5 m, real apple-tree size) in rows 6 m apart. |
| **Farmsteads** (three) | 80 m each | barns from Fantasy Kingdom farm kit re-skinned (Atlas), `SM_Bld_Stone_Cabin_*` (Meadow Forest), silos `SM_Bld_Pod_Silo_*` (Sci-Fi Worlds), Outpost fences `SM_Bld_Fence_*`. One burned: the same pieces with scorched material and `SM_Env_Crater_*`. |
| **River and covered bridge** | 60 m wide | water, `SM_Env_Lillies_*`, the bridge `SM_Prop_Bridge_*` (Meadow Forest) roofed. |
| **The pylon line** | 1.6 km, a pylon every 120 m | `SM_Prop_Powerline_*`, `SM_Prop_Powerline_Antenna_*` (Sci-Fi Worlds) at 30 m; the junction is `SM_Bld_Corp_Generator_*` + `SM_Bld_Power_*` ringed by pylons, lit soul-blue. |
| **Silo 12** | 50 m tall | Run entrance: `SM_Bld_Scav_Refinery_Silo_*` (Sci-Fi Worlds) grouped in three, a harvester `SM_Veh_Scav_Lifter_*` parked at the door. |
| **Fort Alder** | 150 m fort | Hegemony forward base, Wayfinder: `SM_Bld_Corp_Wall_*`, `SM_Bld_Corp_Wall_Tower_*`, `SM_Bld_Corp_Barracks_*`, `SM_Prop_Floodlight_*`, `SM_Veh_HoverTank_*`. |

**Points of interest.** Atlas: Silo 12 (Run entrance), Pylon Junction 4, The Orchard Rows, Fort Alder (Wayfinder). New, my picks: *The Covered Bridge*; *Harrow Farm* (the burned farmstead, a Covenant rune painted on the barn door); *The Scarecrow Field* (Bruiser 57).
