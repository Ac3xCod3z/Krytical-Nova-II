# Aldhavn

**Elders-Reach (Covenant).** Map scene: `EldersReach_Aldhavn`. This folder is the place's own: its kit, this brief, and its concept art.

The full text, with the region kit and how the doc is read, is `Assets/_Game/_Docs/Design/Zone-Build-Kits.md` (in the Encyclopedia's Library); the Atlas card it builds on is `Design/World-Atlas.md`. Sizes follow `Design/16_world_scale.md`: a person is 1.8 m and trees are their real height.

## What is in this folder

- `Kit/`: the prefabs this place is built from, as variants of the Synty ones, already at world scale (Buildings 88, Foliage 30, Ground 18, Lights 12, Props 83, Vehicles 2). What the whole region shares is in `../_RegionKit/`. Place from here, not from `Assets/Synty`.
- `Concept/Reference/` (where this place has one): the owner's finished-look mockup from the Figma World Atlas.
- `Concept/Owner/`: **your concept art goes here.** Name it `<what it shows>_<number>.png` (for example `Gate_01.png`); anything in this folder is yours and no tool writes to it.

## The brief

### Home City: Aldhavn

**Map: 1,600 m square, terrain height 300 m.** Atlas: a hall-city at the head of a fjord, tiered.

**Description.** The fjord narrows to a harbour of black water and longships. Above it, the hall ring: Viking longhalls with grass roofs and carved dragon heads, smoke from every roof hole. Higher, on the cliff terraces, elven spires with curved roofs and climbing vines, and at the very top the Ancestor Grove, where the souls once came home. One pact, one wall: a stone wall with timber hoardings runs round the landward side. Waterfalls fall off the terraces into the fjord.

**Life.** Shieldmaidens and elven wardens (Sidekick Viking Warriors, Elven Warriors) on the wall and gates; fishers on the docks, children on the hall steps, ravens on the roof ridges. Sound: surf, gulls, a smithy hammer, a horn at dusk.

**Layout (metres, harbour centre at 0,0, the fjord opens to the west).**

```
  Ancestor Grove (+150,+520, 160 m up)  elven terraces (+0..+300, +350..+500, 110-140 m up)
     Pact Hall (0,+280, 60 m up)   hall ring (r 200-300 around it)
  Rune Forge (+260,+180)               landward wall (east, x +450) -> gate to Birchlight Vale (+450,+50)
  ~~~ fjord ~~~ Harbor Market (0,0) docks (-150..+150, -80..0)
```

| District | Assets |
| --- | --- |
| **Harbor Market** (water level) | docks `SM_Bld_Dock_Wood_Straight_*`, `SM_Bld_Dock_Pillar_*`, `SM_Bld_Dock_Wood_Stairs_*` (Viking Realm), `SM_Bld_Wharf_*` (Vikings); longships `SM_Veh_Boat_Large_*`, `SM_Veh_Boat_Medium_*` (Viking Realm, about 25 m); stalls `SM_Bld_Stall_Cover_*` (Vikings), `SM_Prop_Fish_Rack_*`, `SM_Prop_Barrel_*`; the Broker and Kiosks under `SM_Prop_Awning_*`. |
| **Hall ring** (middle tier) | longhalls built from `SM_Bld_Hall_Roof_*`, `SM_Bld_Wall_Logs_*`, `SM_Bld_Head_*` dragon heads (Viking Realm), 30-45 m long; the **Pact Hall** the biggest (60 m long, Monumental) with `SM_Prop_Throne_*` and `SM_Prop_Fire_Pit_*` inside. |
| **Rune Forge** | an open smithy: `SM_Prop_Furnace_*`, `SM_Prop_Anvil_*`, `SM_Prop_Grindstone_*`, `SM_Prop_Weapon_Rack_*` (Viking Realm); Armorer and Fixer. |
| **Elven terraces** (upper tier) | Elven Realm houses: `SM_Bld_Wall_Curved_*`, `SM_Bld_Roof_Concave_*`, `SM_Bld_Roof_Convex_Tower_*`, `SM_Bld_Balcony_*`, `SM_Env_Vines_Preset_*`; footpaths `SM_Env_Footpath_*`, flower lines `SM_Env_Flowers_Line_*`, `SM_Prop_Fountain_*`; waterfalls `SM_Env_Water_Plane_Waterfall_*`. |
| **Ancestor Grove** (top) | a ring of `SM_Env_Tree_Glowing_*` round a stone circle of `SM_Prop_RuneStone_*`; `SM_Env_Tree_Giant_*` (Enchanted Forest) at its heart, 60 m. |
| **Landward wall** | `SM_Bld_Castle_Wall_*`, `SM_Bld_Castle_Tower_Round_*`, `SM_Bld_Castle_Wall_Gate_*` (Knights) with timber `SM_Bld_Castle_Hoarding_*` (Fantasy Kingdom). |
| **Stairs and lifts between tiers** | `SM_Env_Stone_Stairs_*` (Viking Realm), `SM_Bld_Stairs_Curved_*` (Elven Realm), a rope crane `SM_Prop_Crane_*` (Viking Realm) hauling goods up the cliff. |

**Points of interest.** Atlas: The Pact Hall, Harbor Market, The Ancestor Grove, The Rune Forge. New, my picks: *The Oathstones* (the stair of carved stones between harbour and hall ring, every oath ever sworn here); *Skald's Rest* (a mead hall on the harbour, the social hub); *The Falls of Aldhavn* (vista from the elven terraces).

**Gaps.** None; a Covenant crest for banners (`SM_Prop_Flag_*` takes a texture).
