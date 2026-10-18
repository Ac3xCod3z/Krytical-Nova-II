# Barrowfell

**Elders-Reach (Covenant).** Map scene: `EldersReach_Z4_Barrowfell`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 8, Ground 52, Props 2). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z4: Barrowfell

**Map: 2,000 m square, terrain height 600 m. Lv 65-90 · Scar Gate.**

**Description.** Snowline moors of barrow mounds and runestone circles, rising to the fells. Wind, ice, a frozen lake with something under it. The mounds are the old kings', each with a stone door. At the top of the pass, a great carved arch holds the Scar Gate: from the arch you see the violet aurora over the Scar.

**Life.** Hostile grounds 70-88, Bruisers 78 and 89 (the Frozen Lake). Suggested species (proposals): barrow kings' guard, ice wolves, frost giants. Ravens, a lone elk. Sound: wind, ice cracking, a horn from the crypts.

**Layout.** The Atlas's high moor of mound fields around a frozen lake; the carved arch of the Scar Gate at the top of the pass.

```
              The Scar Gate arch (0,+880, 560 m up)
        The Howlstone (+400,+550)          pass road switchbacks
   Mound field W (-500,+150)   The Frozen Lake (0,+200, 600 m across)   Runestone Circle (+500,-100)
        S: arrival from the Sylwood (0,-900, 80 m up)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Snow moor** | most of the map | `SM_Env_Snow_Mound_*`, `SM_Env_GroundCover_*` (Alpine Mountain), `SM_Env_Snow_Hill_*` (Viking Realm), dead heather; pines `SM_Env_Pine_*` (Alpine Mountain) in sheltered hollows only. |
| **Mound fields** | 40 barrows | `SM_Env_Snow_Hill_*` as mounds, each with a stone door `SM_Env_Door_Large_Stone_*` (Dungeon) and a `SM_Prop_Cairn_*`. |
| **The Frozen Lake** | 600 m | Bruiser 89 on the ice: `SM_Env_Ice_Sheet_*`, `SM_Env_Lake_*` (Alpine Mountain), a hole with `SM_Prop_Ice_Fishing_Hut_*` long abandoned. |
| **Runestone Circle** | 80 m | `SM_Prop_RuneStone_*`, `SM_Env_Stone_Engraved_*` (Viking Realm) at Heroic 4-5 m, iced with `SM_Env_Icicle_*`. |
| **The Howlstone** | 30 m crag | the crypt gate: a crag `SM_Env_Rock_Cliff_*` with a wind-hole that howls, `SM_Env_Entrance_Crypt_*` (Dungeon) at its foot. |
| **The pass and the Scar Gate** | | icicle cliffs `SM_Env_Icicle_*`, `SM_Env_Rock_Cliff_Arch_*` (Alpine Mountain); the arch: `SM_Bld_Arch_*` (Viking Realm) built Colossal (25 m), carved `SM_Bld_Head_*` dragons, `SM_Prop_Brazier_*` either side. |

**Points of interest.** Atlas: The Runestone Circle, The Frozen Lake (Bruiser), The Howlstone (crypt gate), The Scar Gate. New, my picks: *The King's Road* (the old processional way of standing stones up the pass); *Ice-Fisher's Rest* (Wayfinder, a turf hut by the lake); *The Last Watch* (a vista over the Scar from the arch).
