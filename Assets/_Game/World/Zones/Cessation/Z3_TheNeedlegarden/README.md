# The Needlegarden

**Cessation (Null).** Map scene: `Cessation_Z3_TheNeedlegarden`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 18, FX 1, Foliage 1, Ground 7, Landmarks 11, Props 31). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z3: The Needlegarden

**Map: 2,000 m square, terrain height 500 m. Lv 45-67.**

**Description.** A forest of basalt needles 40 to 120 m tall, hung with ossuaries: bone reliquaries swaying on chains between the spires. Violet crystal light from the grottos, absolute silence. Paths are carved stairs spiralling up the needles; chain bridges link them. In the middle stands the Cathedral Spire, and somewhere in the east an Alien Ruin older than any faction.

**Life.** Hostile grounds 49-67, Bruisers 57 (Crystal Grotto) and 67. Suggested species (proposals): reliquary keepers, crystal stalkers, chain hangers. Bats (`FX_Bats_*`, Nature Biomes). Sound: silence, a chain creaking, wind whistling through the needles.

**Layout.** Vertical: stair-carved paths spiral up the spires, chain bridges link them; the Cathedral Spire holds the Run.

```
     Alien Ruin (+600,+500)       portal to Noct Caldera (0,+880, up on the north spires)
   The Hanging Ossuaries (-300,+300, between 6 spires)    The Cathedral Spire (0,+100, 160 m)
     Crystal Grotto (-550,-200, under the needles)        Chain Walk (+300,-150)
          S: arrival from the Scrapline (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Needle forest** | the whole map, 200+ needles | `SM_Env_Detail_Spire_*`, `SM_Env_Detail_Spire_Group_*` (Sci-Fi Worlds), `SM_Env_Basalt_*` (Dark Fantasy), `SM_Env_Rock_Peak_*` (Dungeon Realms) at 40-120 m; carved stairs `SM_Env_Rock_Stairs_*` (Dark Fantasy) spiralling up them. |
| **Chain bridges** | 30-80 m spans | `SM_Env_Rock_Bridge_*` (Dark Fortress) and plank bridges `SM_Env_Dwarf_Bridge_*` (Dungeon Realms) hung on `SM_Prop_Chain_*`. |
| **The Hanging Ossuaries** | 6 spires | cages `SM_Prop_Cage_Hanging_*` (Dark Fortress) full of skulls `SM_Prop_Skull_Pile_*`, hung on `SM_Env_Hell_Chains_*` (Dungeon Realms) 20-40 m up. |
| **Crystal Grotto** | 150 m cave | Bruiser 57: `SM_Env_Crystals_Cluster_Large_*` (Dungeon Realms), `SM_Env_Gem_Large_*` (Dungeon), `SM_Prop_Geode_*` (Enchanted Forest), violet light. |
| **The Cathedral Spire** | 160 m | the Run entrance: a needle carved into a gothic front, `SM_Bld_Wall_Window_Huge_*`, `SM_Bld_Buttress_*`, `SM_Bld_Wall_Archway_*` (Dark Fantasy). |
| **Alien Ruin** | 120 m | `SM_Env_Artifact_AlienRuin_*` (Sci-Fi Worlds), `SM_Env_Obelisk_Glow_*` (Dungeon Realms); something older than the factions. |

**Points of interest.** Atlas: The Hanging Ossuaries, Crystal Grotto (Bruiser), The Cathedral Spire (Run entrance), Alien Ruin. New, my picks: *The Chain Walk* (a run of bridges 60 m up; the Wayfinder sits at its middle platform); *The Counting Stair* (a stair of exactly 1,000 steps); *The Silent Choir* (a ring of statues `SM_Prop_Statue_People_*`, Dark Fantasy, with skulls for faces).
