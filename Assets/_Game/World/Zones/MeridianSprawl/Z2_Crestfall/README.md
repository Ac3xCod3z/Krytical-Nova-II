# Crestfall

**Meridian Sprawl (Hegemony).** Map scene: `MeridianSprawl_Z2_Crestfall`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 97, Foliage 7, Ground 34, Props 39, Vehicles 2). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Z2: Crestfall

**Map: 2,000 m square, terrain height 300 m. Lv 20-45.**

**Description.** The dead suburbs. Cul-de-sac grids, a husk of a mall, dry fountains, lawns gone to meadow. It is the silence that gets you. Swing sets move in the wind. A water tower stands over everything, its Hegemony logo half peeled. Birch and maple have taken the back yards and are now as tall as the houses; the grass is waist-high. Covenant raid craters pock the grid where an extraction line ran under the streets.

**Life.** Hostile grounds 25-43 in seven steps (Hostiles plan), Bruisers 33 and 44. Suggested species (proposals): feral house-bots, mutated hounds, looter gangs. Deer in the meadow lawns, birds. Sound: wind in tall grass, a looping PA from the mall, dogs far off.

**Layout.** The Atlas's block grid around a central mall and a reservoir; the highway interchange marks the far edge.

```
  Interchange (Bruiser 44) NE corner (+700,+700) -> portal to Alder County
     Oakridge Cul-de-sac (-500,+400)     Reservoir (+450,+150)
              Crestfall Galleria (0,+100)  Water Tower (+150,-100)
     Elmwood grid (W)          Schoolyard (E)
        S: arrival from Transit Row (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Elmwood grid** (west) | 700 × 700 m, 30 m blocks | houses from Generic `SM_Bld_Base_*` modules (2 storeys, 7-9 m) with Viking-free pitched roofs `SM_Bld_Base_Roof_*`; fences `SM_Prop_Meadow_Fence_*` (Meadow Forest); yards overgrown with `SM_Env_Grass_Tall_Clump_*`, `SM_Env_Wildflowers_*`; birch `SM_Env_Tree_Birch_*` and maple `SM_Env_Tree_Meadow_*` at real height (18-20 m), over the roofs. |
| **Oakridge Cul-de-sac** (north-west) | ring of 12 houses | the Atlas's Contract loot: open doors, `SM_Gen_Prop_Cardboard_Box_*`, `SM_Prop_Junk_Paper_*`; one house burned to `SM_Bld_Base_Wall_Destroyed_*`. |
| **Crestfall Galleria** (centre) | 180 × 120 m mall, 18 m high | the Run entrance; a husk built from Cyber City `SM_Bld_Base_*` modules with glass front `SM_Bld_Base_Wall_Window_Double_*`, parking lot `SM_Env_Road_ParkingLines_*` full of `SM_Veh_SUV_*` and `SM_Veh_Luxury_Car_*`, a dead sign `SM_Prop_Billboard_Frame_*`. |
| **The Water Tower** | 45 m tall | the vista: `SM_Bld_Watertank_*` (Sci-Fi Worlds) on a ladder `SM_Gen_Bld_Ladder_*`, visible from the whole map. |
| **Reservoir** (east) | 300 m lake | water plane, dry concrete rim (Sci-Fi City `SM_Env_Ground_Tile_*`), reeds `SM_Env_Reeds_*` (Swamp Marshland), a pump house `SM_Bld_Power_*`. |
| **Schoolyard** (south-east) | 150 m | new, my pick: a school `SM_Bld_Base_*` with a crater through the gym (`SM_Env_Crater_*`), swings `SM_Prop_Meadow_Fence_Railing_*` as frame; a story spot. |
| **The Interchange** (north-east) | 500 m cloverleaf | Bruiser on the overpass: `SM_Env_Highway_Corner_*`, `SM_Env_Highway_Straight_*`, `SM_Env_Highway_Wedge_*` (Apocalypse Wasteland) on `SM_Bld_Bridge_Supports_*`, decks at 8 and 16 m; the portal under the top loop. |

**Points of interest.** Atlas: Crestfall Galleria (Run entrance), The Water Tower, Oakridge Cul-de-sac, The Interchange (Bruiser). New, my picks: *The Schoolyard*; *Mile Marker 9* (a family car still packed for a trip that never left); *The Reservoir Pumphouse* (Wayfinder).
