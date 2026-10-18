# Gantry Ridge

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_Z4_GantryRidge`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 49, FX 5, Foliage 23, Ground 36, Landmarks 1, Props 18, Trees 8, Vehicles 9). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z4: Gantry Ridge

**Map: 2,000 m square, terrain height 600 m. Lv 65-90 · Scar Gate.**

**Description.** Strip-mined highlands and the war front. The ridge climbs in terraces cut by the extraction gantries, a drill as tall as a tower biting into the summit. A trench line crosses the middle, wrecked APCs and a downed dropship in no-man's-land. Floodlit walls at the top guard the Scar Gate. Pines below, scorched stumps in the middle, snow on the upper ridge. The city's hunger laid bare.

**Life.** Hostile grounds 70-88 (Hostiles plan), Bruisers 78 and 89. Suggested species (proposals): war-frames, trench revenants, frost hounds. Searchlights sweep at night. Sound: the drill's grind, wind, distant artillery.

**Layout.** The Atlas's terraced ridge climbing by switchbacks to the fortified Scar Gate at the summit; the trench line across the middle.

```
                The Scar Gate (0,+850, 520 m up)
        Upper ridge: snow, Extraction Gantry Alpha (+400,+600)
  ===== The Trench Line (z +100) =====  Crash Site (-450,+250)
        Switchbacks, terraces 30 m apart in height
        Pine slopes (lower)   Mech graveyard (+500,-200)
        S: arrival from Alder County (0,-900, 40 m up)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Pine slopes** (lower) | the south third | `SM_Env_Pine_*` (Alpine Mountain) at real height (25-35 m), `SM_Env_Rock_Cliff_*`, `SM_Env_Bush_*`; switchback road `SM_Env_DirtRoad_*` (Sci-Fi Worlds). |
| **Terraces** | benches 30 m tall, 60 m deep | cut faces `SM_Env_Cliff_Flat_*` (Sci-Fi Worlds), mining kit `SM_Prop_Drill_*`, `SM_Bld_Bridge_Mining_Laser_*`, conveyor `SM_Bld_Scav_Refinery_Pipe_*`, scorched stumps `SM_Env_Pine_Stump_*`. |
| **The Trench Line** | 1.4 km across the map | trench cut 3 m deep, `SM_Bld_Military_Wall_Cover_*`, `SM_Prop_Barricade_*` (Maps Sci-Fi Outpost), `SM_Prop_Tank_Trap_*` (Apocalypse Wasteland), wrecked `SM_Veh_Apc_*`, craters `SM_Env_Crater_*`. |
| **Crash Site** | 120 m | a downed `SM_Veh_DropShip_*` (Sci-Fi Worlds) nose-down, debris `SM_Env_Debris_Structure_*` (Sci-Fi Space), `FX_Smoke_Large_*`. |
| **Mech graveyard** (new, my pick) | 150 m | two wrecked war-frames from the Mech pack (`SM_Veh_Mech_*` assembled and toppled), one kneeling as a climbable vista. |
| **Extraction Gantry Alpha** | 90 m tall | `SM_Bld_Planetary_Cannon_*` (Sci-Fi Worlds) as the colossal drill, Colossal tier, set over `SM_Env_Core_*` glowing soul-light; `SM_Bld_Bridge_*` catwalks. |
| **Upper ridge** | the north third | snow `SM_Env_Snow_Mound_*`, `SM_Env_Ice_Sheet_*` (Alpine Mountain), `FX_Snow_Soft_*` (Sci-Fi Worlds). |
| **The Scar Gate** | 200 m fort | `SM_Bld_Corp_Wall_*`, `SM_Bld_Corp_Wall_Gate_*`, `SM_Bld_Corp_Wall_Tower_Corner_*`, `SM_Prop_Searchlight_*`, `SM_Prop_Floodlight_*`; the portal glows Rupture violet in the gate. |

**Points of interest.** Atlas: Extraction Gantry Alpha, The Trench Line (Bruiser), Crash Site, The Scar Gate. New, my picks: *The Mech Graveyard*; *Switchback 7* (the Wayfinder, a mining office on the bend); *The Last Floodlight* (a vista over the Scar from the summit).
