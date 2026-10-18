# Birchlight Vale

**Elders-Reach (Covenant).** Map scene: `EldersReach_Z1_BirchlightVale`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 36, FX 1, Foliage 10, Ground 4, Props 19, Trees 8). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/`: renders of the built map, taken through the game's own comic look (made by **Krytical Nova ▸ World ▸ Render Concept Shots Of Open Map**): `_Vista` (the wide view), `_Layout` (top down) and one per point of interest.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z1: Birchlight Vale

**Map: 2,000 m square, terrain height 300 m. Lv 1-22.**

**Description.** Sunlit birch meadows and standing stones. White trunks, gold leaves, wildflowers to the knee, a slow river down the middle. In the middle of it all, Lofthollow, the first village, still smoking from a Hegemony extraction raid: thatch roofs fallen in, a well, a broken cart, the extraction rig the raiders left behind. Beyond the Stone Ring the vale narrows to Elk Pass.

**Life.** Hostile grounds 1-22 (Hostiles plan), Bruisers 12 and 21 (Elk Pass). Suggested species (proposals): blight-boars, restless barrow-wights, raid scavengers. Elk and deer, songbirds, butterflies. Sound: river, birdsong, the crackle of the last fires in Lofthollow.

**Layout.**

```
              Elk Pass (0,+850) -> portal to Skeldfjord
      Birch woods W            The Stone Ring (+150,+350)
  ~~~~ river (x -50..+30, south to north) ~~~~
      The Old Mill (-300,+50) on the river    Lofthollow ruins (+350,-150)
              S: arrival from Aldhavn's gate (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Meadows** | most of the map | grass `SM_Env_Grass_Med_Clump_*`, `SM_Env_Wildflowers_Patch_*`, `SM_Env_Flowers_Flat_*`; stone walls `SM_Prop_Stonewall_*` (Meadow Forest); berry bushes `SM_Env_Bush_Berries_*` (Viking Realm). |
| **Birch woods** (west and east edges) | 300-500 m stands | `SM_Env_Tree_Birch_*` (Meadow Forest, 18 m, in groves of 20-40), `SM_Tree_Birch_*` (POLYGON Nature) for variety, fallen `SM_Tree_Log_*`. |
| **Lofthollow ruins** | 200 m village | Fantasy Village houses: `SM_Bld_Wall_Curved_*`, thatch `SM_Bld_Roof_Thatch_Preset_*`, some roofs collapsed with `SM_Bld_Castle_DestroyedWall_RubblePile_*` (Fantasy Kingdom); a well `SM_Bld_Well_*` (Elven Realm); the Hegemony's left-behind rig `SM_Prop_Drill_*` (Sci-Fi Worlds) in the square; `FX_Smoke_*`, `FX_Fire_Card_Small_*` (Generic). First Contracts. |
| **The Stone Ring** | 50 m circle | `SM_Prop_StoneRunes_*`, `SM_Prop_StoneArch_*` (Meadow Forest), `SM_Prop_RuneStone_*` (Viking Realm) 4-5 m tall (Heroic). |
| **The Old Mill** | | Wayfinder: `SM_Bld_Windmill_*` (Fantasy Village) on the riverbank with a water wheel, `SM_Prop_Bridge_*` (Meadow Forest). |
| **Elk Pass** | 120 m gap | Bruiser 21: the vale closes between `SM_Env_Rock_Cliff_*` walls, a cairn `SM_Prop_Cairn_*` (Viking Realm) and elk antlers on a post. |

**Points of interest.** Atlas: Lofthollow ruins (first Contracts), The Stone Ring, The Old Mill (Wayfinder), Elk Pass (Bruiser). New, my picks: *The Raid Rig* (the Hegemony drill left in Lofthollow's square, still humming); *Hearthbarrow Mound* (the Run entrance, a grass barrow by the Stone Ring); *Ferry Stones* (stepping stones over the river).
