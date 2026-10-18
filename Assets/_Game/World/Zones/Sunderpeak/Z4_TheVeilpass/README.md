# The Veilpass

**Sunderpeak (Ronin, hidden until the expansion).** Map scene: `Sunderpeak_Z4_TheVeilpass`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Ground 68, Props 2, Trees 8). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z4: The Veilpass

**Map: 2,000 m square, terrain height 700 m. Lv 65-90 · Scar Descent.**

**Description.** The frozen outer peaks. Wind-carved ice, hidden gates, the walls that let a nation disappear. A switchback pass through ice caves. At the top, the Hidden Gate; at the bottom, the descent to the Scar's north wall. A cairn of names stands for the outsiders who got too close.

**Life.** Hostile grounds 70-88. Suggested species (proposals): ice sentries, snow leopards, the forgotten. No birds above the snowline. Sound: wind, ice creak, silence.

| Sub-area | Assets |
| --- | --- |
| **Ice slopes** | `SM_Env_Snow_Mound_*`, `SM_Env_Ice_Sheet_*`, `SM_Env_Rock_Cliff_*` (Alpine Mountain), `SM_Env_Glacier_*`, `SM_Env_Glacier_Arch_*` (Vikings), icicles `SM_Env_Icicle_*` (Viking Realm); snow pines `SM_Env_Pine_*`. |
| **Ice caves** | `SM_Env_Cave_*` (Dungeon) in ice white, `SM_Env_Stalactite_*` (Alpine Mountain). |
| **The Hidden Gate** | sealed until the reveal: `SM_Bld_Snow_Castle_Gates_*`, `SM_Bld_Snow_Wall_*` (Samurai) set into the rock. |
| **The Frozen Watch** | Bruiser: `SM_Bld_Snow_Tower_*` (Samurai). |
| **The Cairn of Names** | `SM_Prop_Cairn_*`, `SM_Prop_RuneStone_*` (Viking Realm). |
| **The Scar Descent** | portal: a stair `SM_Env_Snow_Archway_*` (Samurai) dropping toward the Scar. |

**Points of interest.** Atlas: The Hidden Gate, The Frozen Watch (Bruiser), The Cairn of Names, The Scar Descent. New, my pick: *The Last Lantern* (the final lit lantern before the outside world).
