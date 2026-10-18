# The Cane Sea

**Sunderpeak (Ronin, hidden until the expansion).** Map scene: `Sunderpeak_Z2_TheCaneSea`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 20, FX 4, Foliage 8, Ground 6, Props 5, Trees 3). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z2: The Cane Sea

**Map: 2,000 m square, terrain height 300 m. Lv 20-45.**

**Description.** A bamboo sea that whispers. Canes 12 to 15 m tall, so close the light comes down green. Listening posts on stilts poke above the tops. Paths vanish when the wind moves the canes; the dry riverbed is the one straight line through.

**Life.** Hostile grounds 25-43. Suggested species (proposals): cane stalkers, fox spirits, lost scouts. Birds (`FX_Birds_*`). Sound: bamboo knocking, wind hiss, footsteps that are not yours.

| Sub-area | Assets |
| --- | --- |
| **The bamboo sea** | `SM_Env_Bamboo_*`, `SM_Env_Bamboo_Wall_*` (Samurai Empire), `SM_Env_Bamboo_Group_*` (Samurai), ferns `SM_Env_Fern_*`, stone lanterns `SM_Prop_Lantern_Stone_*`; clearings 30-60 m. |
| **Listening Post 3** | stilt tower: `SM_Bld_Platform_Large_Legs_*`, `SM_Bld_Tower_*` (Samurai), 25 m. |
| **The Dry Riverbed** | `SM_Env_Rock_Small_*`, `SM_Env_Gravel_Pile_*` (Samurai Empire), the safe road. |
| **The Hermit's Hut** | `SM_Bld_House_Base_*` with `SM_Bld_House_Roof_Thatch_*` (Samurai); a Null dissident who refused extinction. |
| **Fox Shrine** | Bruiser: `SM_Prop_Shrine_*`, `SM_Prop_Statue_*`, `SM_Prop_Torii_Gate_*` (Samurai Empire). |

**Points of interest.** Atlas: Listening Post 3 (vista), The Dry Riverbed, The Hermit's Hut, Fox Shrine (Bruiser). New, my pick: *The Listening House* (the Run entrance, half-sunk in the canes).
