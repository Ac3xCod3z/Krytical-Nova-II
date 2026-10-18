# The Sylwood

**Elders-Reach (Covenant).** Map scene: `EldersReach_Z3_TheSylwood`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 15, Foliage 71, Ground 26, Lights 4, Props 3, Trees 16). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z3: The Sylwood

**Map: 2,000 m square, terrain height 300 m. Lv 45-67.**

**Description.** Elven old-growth. Trees so big their roots are hills, lantern-lit tree-houses in the crowns, shafts of green light, glow-flowers on the fern floor, moss over everything. Then the scorched clearings, where Hegemony extraction crawlers cut straight through: stumps in rows, a crashed crawler, mud. Up in the canopy, the elven walkways go on as if nothing happened.

**Life.** Hostile grounds 49-67, Bruisers 57 and 67. Suggested species (proposals): rootbound guardians, moth swarms, fallen wardens. Owls, fireflies (the Enchanted Forest pack's FX motes). Sound: deep wind in leaves, creaking boughs, a distant harp from Moon Lantern Court.

**Layout.** Two levels: the Canopy Road above (walkways 25-40 m up), the fern floor below. The Great Tree marks the Rootvault.

```
              The Great Tree (0,+300) — Rootvault entrance at its roots
   Moon Lantern Court (-450,+500, in the canopy)    Scorched Clearing (+550,+150)
        Canopy Road loop (r ~400 around the Great Tree, 30 m up)
   Fern floor everywhere below; stream (W)
        S: arrival from Skeldfjord (0,-900)    N: portal to Barrowfell (+200,+880)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Fern floor** | the whole map | `SM_Env_Fern_*`, `SM_Env_Fern_Tree_*`, `SM_Env_Undergrowth_Fern_*`, `SM_Env_Moss_Lumps_*`, `SM_Env_Mushroom_*`, `SM_Env_Roots_*` (Enchanted Forest); old-growth trees `SM_Env_Tree_*` (Enchanted Forest, Elven Realm) at 30-45 m, every one a landmark. |
| **The Canopy Road** | 2.5 km loop, 30 m up | `SM_Bld_Bridge_*`, `SM_Bld_Balcony_Convex_Round_*`, `SM_Bld_Railing_*` (Elven Realm) from tree to tree; ramps and spiral stairs `SM_Bld_Stairs_Curved_*` up the trunks; lanterns `SM_Prop_Lantern_*` (Enchanted Forest). |
| **Tree-houses** | in the crowns | `SM_Env_Tree_House_*` (Enchanted Forest), `SM_Bld_Gazebo_*` (Elven Realm). |
| **The Great Tree** | 90 m tall, trunk 25 m wide | Colossal: `SM_Env_Tree_Giant_*` (Enchanted Forest) at Colossal scale; its roots a doorway `SM_Env_Tree_Portal_*`; `SM_Env_Tree_Glowing_Canopy_*` (Elven Realm). |
| **The Scorched Clearing** | 300 m | Bruiser 57: stumps `SM_Env_Pine_Stump_*`, `SM_Tree_Stump_*`, burned logs, mud; the crashed crawler `SM_Veh_Scav_Lifter_*` (Sci-Fi Worlds) on its side, `SM_Bld_Scav_Wreckage_*`. |
| **Moon Lantern Court** | 60 m platform, 35 m up | Wayfinder: `SM_Bld_Planetarium_*` (Elven Realm), `SM_Prop_Constellation_Rings_*`, `SM_Prop_Lantern_Stand_*`. |

**Points of interest.** Atlas: The Canopy Road, The Great Tree (Rootvault entrance), The Scorched Clearing (Bruiser), Moon Lantern Court (Wayfinder). New, my picks: *The Crawler Furrow* (the straight scar of felled trees leading from the clearing to the map edge); *The Weeping Pool* (a glow-crystal spring, `SM_Prop_Crystal_*`, `SM_Prop_Geode_*`); *Warden's Fall* (an elven warden's grave under a fallen giant).
