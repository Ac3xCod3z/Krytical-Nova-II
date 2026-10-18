# Noct Caldera

**Cessation (Null).** Map scene: `Cessation_Z4_NoctCaldera`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 30, Ground 9, Props 2, Vehicles 6). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z4: Noct Caldera

**Map: 2,000 m square, terrain height 600 m. Lv 65-90 · Scar Gate.**

**Description.** A glassed crater rim under an aurora of trapped souls. In the middle, the pit: basalt needles rising out of crimson light, giant ribcage fossils arching over it, plank bridges and mine-rail scaffolds across it. Tremors from something stirring beneath. On the near rim, a ruined Hegemony observation station with glass domes on icy cliffs and frozen falls; on the far rim, the Scar Gate.

**Life.** Hostile grounds 70-88, Bruisers 78 (Observation Core) and 89. Suggested species (proposals): rim sentinels, caldera wraiths, the Well's spawn. No animals. Sound: a low tremor every 40 seconds, glass ticking as it cools, the aurora's faint chorus.

**Layout.** The Atlas's ring road around a central pit; the Observation Core on the near rim, the Scar Gate on the far rim, the Well dropping into the middle. The mockups give the pit its crimson light, bone arches and mine-rail scaffolds.

```
                 The Scar Gate (0,+850, far rim)
   ring road (r 650) .  bone arches over the pit . plank bridges
   Null outpost + wrecked mech (-600,+200)   THE PIT (r 300, 200 m deep, crimson) — The Stirring Well (0,0)
   The Glass Rim (-400,-500)   Observation Core (+550,-350, icy cliffs, frozen falls)
                 S: arrival from the Needlegarden (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **The Glass Rim** | ring 300 m wide | fused glass ground (a glossy black terrain layer), `SM_Env_Cracked_Edge_*` (Sci-Fi Worlds), `SM_Env_Ground_Fissure_*` (Apocalypse Wasteland). |
| **The pit** | 600 m across, 200 m deep | basalt needles `SM_Env_Detail_Spire_Group_*` rising from `SM_Env_Lava_Plane_*` (Dungeon Realms) recoloured crimson; ribcage fossils `SM_Env_Bone_Rigcage_*` (Dungeon) at Colossal scale (30 m ribs) arching over; `SM_Env_Skull_Huge_*` (Dungeon Realms) on the north edge (the mockups' giant skull). |
| **Bridges and scaffolds** | | `SM_Env_Minetrack_Bridge_*`, `SM_Env_Minetrack_Straight_*` (Dungeon), `SM_Env_Dwarf_Bridge_*` (Dungeon Realms), wooden scaffolds `SM_Env_Basement_Support_Pole_*` (Dungeon) down the pit walls. |
| **Null outpost and wrecked mech** | 200 m | the mockups' outpost: `SM_Bld_Scav_Dish_*`, `SM_Bld_Corp_Comms_Tower_*`, `SM_Bld_Crate_Building_*` (Sci-Fi Worlds); a toppled war-frame from the Mech pack (`SM_Veh_Mech_*`) 18 m long. |
| **Observation Core** | 250 m | Bruiser 78: glass domes `SM_Bld_Pod_Research_*` (Sci-Fi Worlds) and a tower `SM_Bld_Corp_Control_Tower_*` on icy cliffs `SM_Env_Ice_Sheet_*` (Alpine Mountain) with frozen falls; the ruined corp station. |
| **Floating shards** | over the pit | `SM_Env_Floating_Rocks_*` (Sci-Fi Worlds), `SM_Env_Hell_Floating_Rock_Chain_*` (Dungeon Realms). |
| **The Scar Gate** | | Dark Fortress `SM_Bld_Wall_L_Gate_*` set into the far rim, red banners, violet portal. |

**Points of interest.** Atlas: The Glass Rim (vista over the Scar), The Stirring Well (Run entrance), Observation Core (Bruiser), The Scar Gate. New, my picks: *The Great Skull* (the fossil skull on the pit edge); *Outpost Null-7* (the Wayfinder, with the wrecked mech); *The Frozen Falls* (under the Observation Core).
