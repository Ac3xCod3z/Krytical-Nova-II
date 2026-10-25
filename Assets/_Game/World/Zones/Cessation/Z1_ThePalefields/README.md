# The Palefields

**Cessation (Null).** Map scene: `Cessation_Z1_ThePalefields`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 113, FX 5, Ground 55, Landmarks 4, Props 17). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z1: The Palefields

**Map: 2,000 m square, terrain height 300 m. Lv 1-22.**

**Description.** Bone-white dust flats under the aurora, frosted at the edges. Blob growths pulse in the hollows, black and violet. Half-buried in the plain lies a colossus: an arm the length of a street reaching out of the ground, fingers curled, its head further off with one eye-socket full of crystal. On the far edge a dead Hegemony extraction pylon leans, the first thing the Null took back.

**Life.** Hostile grounds 1-22 (Hostiles plan), Bruisers 12 and 21 (Dust Crater). Suggested species (proposals): bone crawlers, blob spawn, stray drones. Vultures (`FX_Vulture_*`, Nature Biomes) circling. Sound: wind over dust, a low pulse from the blobs.

**Layout.** The Atlas's flat open plain pocked with craters; the colossus arm is the landmark you steer by; the dead pylon marks the far edge.

```
          The First Pylon (+500,+750) -> portal to the Scrapline (+350,+880)
    Dust Crater (-450,+350)      The Colossus Arm (0,+150) and Head (+250,+400)
    The Blob Fields (+450,-150)    Bone Rows (-400,-300)
          S: arrival from Terminus' rim gate (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **The plain** | the whole map | pale ground layer; `SM_Env_Detail_Crater_*`, `SM_Env_Detail_Crater_Spiral_*`, `SM_Env_Crater_Edge_*` (Sci-Fi Worlds); `SM_Env_Rocks_Spikes_*`, `SM_Env_Rock_Pebbles_*` (Arid Desert); tumbleweed `SM_Prop_Tumbleweed_*`. |
| **The Colossus Arm** | 90 m long, 25 m high at the elbow | climbable vista: `SM_Env_Artifact_Arm_*` (Sci-Fi Worlds) at Colossal scale, the head `SM_Env_Artifact_Head_*` half-sunk 250 m away, crystals `SM_Env_Crystals_Cluster_Large_*` in its eye. |
| **The Blob Fields** | 400 m | dense Hostile ground: `SM_Env_Blob_*`, `SM_Env_Ground_Blob_*` (Sci-Fi Worlds), `SM_Env_Sulphur_Blob_*`, `SM_Env_Sulphur_Pool_*` (Arid Desert) recoloured violet. |
| **Dust Crater** | 200 m | Bruiser 21: a deep crater `SM_Env_Crater_*` (Arid Desert) with `SM_Env_Rock_Cliff_*` walls, dust `FX_Dust_Blowing_*`. |
| **Bone Rows** (new, my pick) | 300 m | old fossils laid out in rows by the Null: `SM_Env_Bone_Rib_*`, `SM_Env_BonePile_*` (Dungeon), `SM_Prop_Bones_*` (Arid Desert) in a perfect grid. |
| **The First Pylon** | 35 m | Wayfinder: `SM_Prop_Powerline_*` (Sci-Fi Worlds) toppled at 30°, its base walled with `SM_Bld_Wall_L_*` (Dark Fortress) and hung with a red Null banner. |

**Points of interest.** Atlas: The Colossus Arm (vista), The Blob Fields, Dust Crater (Bruiser), The First Pylon (Wayfinder). New, my picks: *Bone Rows*; *The Idle Door* (the Run entrance, a factory hatch set in the plain); *The Eye* (the colossus head's crystal eye, glowing at night).
