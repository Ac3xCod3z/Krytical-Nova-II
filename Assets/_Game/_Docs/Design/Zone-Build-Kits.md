# Zone Build Kits: every place, what it holds, and what it is built from

Written 18 Oct 2026 on the owner's ask: "build upon, and add to the description and layouts and which assets should be used where in which zone to make sure we bring these zones to life", from the owner's Figma **World Atlas** (https://www.figma.com/design/JhbgViYlKpJEsj8IKTLz5k), its five finished-look mockups and the Synty packs in the project.

**How it relates to the other docs.** `World-Atlas.md` is the Figma copy and is not changed; each place here starts from its Atlas card and goes further. Names a player sees follow the Lore Bible (`Lore-Bible.md` section 14); anything marked *new, my pick* is a proposal for the owner to keep, rename or drop. Hostile species are the owner's to pick: the ones named here are suggestions only. Sizes follow the world scale (`16_world_scale.md`, "Open-world kinds" and "Map sizes").

**Where things are.** Each place has a folder `Assets/_Game/World/Zones/<Region>/<Place>/` with:

- `Kit/`: its prefabs, as variants of the Synty ones, already brought to world scale; the pieces a whole region shares are in `Zones/<Region>/_RegionKit/`;
- `README.md`: the brief for concept art;
- `Concept/`: the renders of the built map, `Concept/Reference/` (the owner's mockups), and `Concept/Owner/` for the owner's own art.

Its map is the scene `Assets/_Game/Scenes/World/...` of the same name.

**Prefab names.** A name ending `_*` means the family (`SM_Env_Tree_Birch_*` = every numbered birch). Every name in this doc was checked against the packs on disk when it was written.

## Status (18 Oct 2026)

**First pass, built.**
- Every place has its kit (variants at world scale), its brief and an `Owner/` folder.
- All 38 maps are laid out from their layout files (`Assets/_Game/World/Data/ZoneLayouts/`) and have concept shots in their `Concept/` folder. **New Meridian** is the pilot, checked against the owner's mockups.

**Not yet.**
- ~~Six packs not in the project yet~~: done 19 Oct 2026 with the **Zone Pack Importer**. **Krytical Nova ▸ World ▸ Zone Pack Importer** brings in only the prefabs the kits and layouts use from Apocalypse Wasteland, Fantasy Village, Dungeon, Dungeon Realms and Mech, with the meshes, materials and textures they need, one pack at a time: 1,594 files (about 200 MB) instead of the whole packs (1.75 GB). Nothing in Kaiju is used, so none of it came in. The kits were built again (684 more pieces, none missing) and every map was laid out again. One Mech shin-pad keeps an empty second material slot: Synty ships it that way.
- **Every map needs a hand pass.** The layouts give each map its ground, routes, districts and landmarks, not its finish.
- **The Runs** are rooms on open ground: no roofs, no darkness yet.
- **Gameplay** (Hostile grounds, Wayfinders, Run entrances, portals) is only named markers.
- Wallstride surface graphs and GPU grass rules per map.

## Packs by region

| Region | Built from | Nature Biomes used |
| --- | --- | --- |
| Meridian Sprawl (Hegemony) | Sci-Fi City, Cyber City, Sci-Fi Worlds, Maps Sci-Fi Outpost, Generic, Apocalypse Wasteland, Mech, Sci-Fi Space (Runs) | Meadow Forest (Crestfall, Alder County), Alpine Mountain (upper Gantry Ridge), Swamp Marshland (reeds) |
| Elders-Reach (Covenant) | Viking Realm, Vikings, Elven Realm, Fantasy Kingdom, Fantasy Village, Knights, Dungeon, Dungeon Realms (Runs) | Meadow Forest (Birchlight Vale), Tropical Jungle and Swamp Marshland (Skeldfjord's wrecks, kelp, jetties), Enchanted Forest (the Sylwood), Alpine Mountain (Barrowfell) |
| Cessation (Null) | Sci-Fi Worlds, Dark Fantasy, Dark Fortress, Sci-Fi Space, Cyber City, Dungeon, Dungeon Realms, Mech, Apocalypse Wasteland | Arid Desert (craters, sulphur blobs, spikes, bones), Enchanted Forest (crystals, recoloured), Alpine Mountain (ice); the aurora from the biomes' FX |
| The Scar | Sci-Fi Worlds, Dungeon Realms (the titan), Dark Fantasy, Dark Fortress, Maps Sci-Fi Outpost, Apocalypse Wasteland | Arid Desert (lava, cracked ground) |
| Sunderpeak (Ronin, hidden) | Samurai, Samurai Empire, Viking Realm, Vikings, Elven Realm, Dungeon | Tropical Jungle (rope bridges), Alpine Mountain (the Veilpass), Arid Desert (vents) |

## Scale, in short

One unit is one metre; a person is 1.8 m; made things are true size, up to 25 % over (`16_world_scale.md`). Out in the world:

- **Trees are their real height**: birch about 18 m, pines 25-35 m, oak and maple about 20 m.
- **Grand things are grand on purpose**, by tier: the Great Tree 90 m, the Core Spire 180 m, the Severed Titan 900 m long.
- **Maps**: a field zone is 2 km square, a Home City 1.6 km, the Scar 3 × 1.5 km, a Run 400 m. A field zone takes about five minutes to run across.

Every kit prefab carries its scale fix, so anything placed from a kit is already the right size.

## How each place reads

Every place has the same parts:

- **Map**: size and terrain height.
- **Description**: what you meet walking in.
- **Life**: Hostiles, people, animals, sound.
- **Layout**: a sketch in metres, with the centre at 0,0 and north up.
- **Sub-areas**: each with its size and the prefabs that go there.
- **Points of interest**: the Atlas's own first, then new ones.
- **Run**: rooms or floors, sizes and kit.
- **Gaps**: where no pack has the piece.

Hostile grounds follow the Hostiles plan: one species and one level each, levels rising away from the entry, and the Bruisers at the far edge.

## Meridian Sprawl (Hegemony)

Atlas card: `World-Atlas.md`, "Meridian Sprawl". Mockups: New Meridian isometric view and street view (Figma 125:69, 125:75).

### The region kit

| | |
| --- | --- |
| Light | Warm, clean daylight inside the walls (late morning sun, light haze); the further out, the dustier and lower the sun: Gantry Ridge is late afternoon under smoke. |
| Palette, mapped | Steel white and deep steel = Sci-Fi City `PolygonSciFiCity_Texture_01_A`; **hazard yellow** (the mockups' taxis, wall bands, Marines) = Sci-Fi Worlds corp yellow; neon cyan = the light strips; rust and ash take over outside the wall (Apocalypse Wasteland and Sci-Fi Worlds scav materials). |
| Ground | Inside: road and sidewalk tiles (Sci-Fi City `SM_Env_Road_*`, `SM_Env_Sidewalk_*`). Outside: dry ochre earth and rock (the isometric mockup), going green with meadow grass in Crestfall and Alder County, then bare rock and snow on Gantry Ridge. |
| Shared rocks | `SM_Env_Rock_*` (Sci-Fi Worlds), `SM_Env_Butte_*` (Apocalypse Wasteland) for the dry hills, `SM_Env_Rock_Cliff_*` (Meadow Forest) where it greens. |
| Shared trees | Street trees `SM_Env_Tree_*` (Sci-Fi City) in planters; outside, `SM_Env_Tree_Meadow_*`, `SM_Env_Tree_Birch_*`, `SM_Env_Tree_Fruit_*` (Meadow Forest); dead ones `SM_Env_TreeDead_*` (Sci-Fi City), `SM_Env_Tree_Dead_*` (Apocalypse Wasteland). All at their species' real height (16_world_scale.md, "Open-world kinds"). |
| War marks | Craters `SM_Env_Crater_*` (Apocalypse Wasteland), broken highway `SM_Env_Highway_Straight_Damaged_*`, `SM_Env_Highway_Rubble_*`, scorched trees. Every field zone shows where the Covenant raids landed. |
| Run flavour | Sterile tech gone wrong: emergency red light, coolant fog (`FX_Fog_Big_*`, Cyber City), polite automated voices, doors that open for nobody. Built from Sci-Fi Space interiors (`SM_Bld_Corridor_*`, `SM_Bld_Wall_*`) and Cyber City modular blocks. |

### Home City: New Meridian

**Map: 1,600 m square, terrain height 200 m.** Atlas: concentric rings around the Core Spire. The mockup sets the shape: a **round walled city about 1,000 m across** on dry ochre ground, the sea to the north-west.

**Description.** You come out of the travel screen at the south gate under a 30 m wall, grey with amber bands and a cyan light strip running its whole length. Beyond the gate, a dead-straight avenue runs 500 m to the Core Spire, a lattice mast 180 m high with a golden crown and a ring of soul-light that hums. Every street is clean. Taxis are yellow, Marines are yellow, the planters are trimmed. Nobody here looks doubtful. Look up and the monorail slides past on its viaduct; look back over the wall and there is the shanty town nobody inside talks about.

**Life.** Marines in yellow armour on the gates and avenues (Sidekick Sci-Fi Soldiers), civilians in pairs (Sidekick Sci-Fi Civilians), hover traffic on the ring road, drones (`SM_Prop_Drone_*`) crossing overhead, gulls on the north-west shore. No Hostiles, no PvP (Atlas), though the owner's rule makes Home Cities faction-war maps: the wall and gates are built to be fought over. Sound: city hum, monorail whoosh, PA chimes, distant surf.

**Layout (metres, centre of the Spire at 0,0, north up).**

```
                  N gate (0,+500) -> Transit Row
         Highrise district (north arc, r 250-480)
   Sea   |                                   | Pylon lines (NE, outside)
  coast  |  Red-roof homes    CORE    Industrial yards
 (NW)  W gate  (west arc)    SPIRE    (east arc)      E gate
         |                  (0,0)     landing pad (+330,+40)
         |  Market blocks (SW)   Marine Barracks (SE)
                  S gate (0,-500)  <- arrival
         Shanty + solar farm (outside, -150..+150, -560..-720)
         Broken highway viaduct (SE, outside) . crater (E, outside)
```

- The wall: a ring of radius 500 m, 30 m high, gate towers 40 m at the four compass points.
- Ring roads at r 120 (Spire plaza), 260 (Exchange ring) and 420 (residential ring); eight radial avenues, 30 m wide, the north-south one the main axis.
- The monorail runs on a viaduct 12 m up along the r 260 ring, with one station per quarter.

**Districts and what goes where**

| District | Size | Assets |
| --- | --- | --- |
| **Core Spire plaza** (r 0-120) | 240 m disc | Spire: `SM_Bld_Corp_Comms_Tower_*` (Sci-Fi Worlds) as the lattice mast, scaled Colossal to 180 m, its base wrapped in `SM_Bld_Corp_Platform_*`; plaza paving `SM_Env_Ground_Tile_*` (Sci-Fi City); planters `SM_Env_Hedge_*`, `SM_Env_Tree_*`; `SM_Prop_Bench_*`, `SM_Prop_Streetlight_*`; holo signs `SM_Prop_Holo_Sign_*` (Cyber City). |
| **Exchange Concourse** (east of the plaza, r 120-260) | 4 blocks | `SM_Bld_Bank_*`, `SM_Bld_Shop_Open_*`, `SM_Bld_Advanced_*` (Sci-Fi City); Kiosks are `SM_Prop_MarketTable_*` + `SM_Prop_MarketCover_*` + `SM_Prop_MarketLights_*`; the mockup's food Kiosk is `SM_Veh_Food_Trailer_*` with the `Hologram_Dragon_*` sign (Cyber City); the Vault entrance is `SM_Prop_Bank_Door_*`. |
| **Highrise district** (north arc, r 250-480) | about 60 towers | `SM_Bld_Large_*`, `SM_Bld_Advanced_*` (Sci-Fi City) stacked to 40-120 m; skyline filler `SM_Bld_Background_Lrg_*`, `SM_Bld_Background_Building_*` (Cyber City) in the far ring; rooftop `SM_Prop_Antenna_*`, `SM_Prop_SatDish_*`, `SM_Prop_AirConditioningUnit_*`. |
| **Red-roof homes** (west arc) | low blocks, 2-4 storeys | Sci-Fi Worlds pods `SM_Bld_Pod_Hab_Large_*`, `SM_Bld_Pod_Hab_Small_*` (the mockup's orange roofs, recoloured terracotta), small yards with `SM_Env_Hedge_*`. |
| **Market blocks** (south-west) | colourful 1-2 storey streets | Cyber City shopfronts `SM_Bld_Wall_Shop_*`, awnings `SM_Prop_Canopy_*`, `SM_Sign_Neon_*`, `SM_Prop_Vending_Machine_*`, street food `SM_Veh_Food_Truck_*`. |
| **Industrial yards** (east arc) | hangars, pad | `SM_Bld_Warehouse_*`, `SM_Bld_Industrial_*` (Sci-Fi City), landing pad `SM_Bld_Corp_LandingPad_*` with `SM_Veh_DropShip_*` (Sci-Fi Worlds), containers `SM_Prop_Container_*` (Maps Sci-Fi Outpost), crane-yard `SM_Bld_Corp_Loading_Bay_*`. |
| **Marine Barracks** (south-east) | walled compound | `SM_Bld_Corp_Barracks_*`, `SM_Bld_Corp_Wall_*`, `SM_Veh_Apc_*` parked in a row (the mockup's six-wheeler), `SM_Prop_Barricade_*` training lane, flag line `SM_Prop_Banner_*` (Sci-Fi Worlds) in Hegemony yellow. |
| **The Pylon Garden** (west of the plaza) | 120 m park | a live soul conduit: `SM_Bld_Power_*` (Sci-Fi City) under a glass canopy `SM_Bld_Overhang_*`, planted with `SM_Env_Hologram_Tree_*` and `SM_Env_Hologram_Cherry_Tree_*` (Cyber City) beside real `SM_Env_Tree_Meadow_*`. |
| **The wall and gates** | ring r 500 | `SM_Bld_City_Wall_*` and `SM_Bld_City_Wall_Tower_*` (Sci-Fi City), gates `SM_Bld_Corp_Wall_Gate_*` (Sci-Fi Worlds) between `SM_Bld_Corp_Wall_Tower_*`; floodlights `SM_Prop_Floodlight_*` (Maps Sci-Fi Outpost) on the towers. |
| **Monorail** | viaduct r 260 | `SM_Bld_Train_Rail_Straight_*`, `SM_Bld_Train_Rail_Curve_Long_*`, `SM_Bld_Train_Rail_Strut_*` (Cyber City), a train of `SM_Veh_Train_Cockpit_*` + `SM_Veh_Train_Middle_*`. |
| **Outside: the shanty** (south of the south gate) | 300 × 160 m | `SM_Building_Shack_*` (Sci-Fi City), `SM_Bld_Crate_Building_*` (Sci-Fi Worlds), `SM_Prop_SolarPanel_*` in rows (the solar farm), `SM_Prop_Scav_Scrap_Pile_*`. The city's poor, in sight of the gate they may not enter. |
| **Outside: the broken viaduct and crater** (south-east and east) | | `SM_Env_Highway_Straight_Long_*` on `SM_Bld_Bridge_Supports_*`, ending in `SM_Env_Highway_Straight_Damaged_*` (Apocalypse Wasteland); a 60 m crater `SM_Env_Crater_*` where a Covenant raid hit the old road. |
| **Outside: the coast** (north-west) | | sea stacks `SM_Env_Rock_Large_*` (Sci-Fi Worlds) in the water, the ochre cliff `SM_Env_Cliff_Rough_*`, sea plane from the region water. |
| **Outside: pylon lines** (north-east) | | `SM_Prop_Powerline_*` (Sci-Fi Worlds) marching away across the ochre hills toward Transit Row. |

**Points of interest.** Atlas: The Core Spire, Exchange Concourse, Marine Barracks, The Pylon Garden. New, my picks: *The Gate of Necessity* (the south gate, its arch lettered with the Hegemony's creed); *Monorail Station Meridian-Central*; *The Shanty* (outside the wall); *Landing Pad One* (east).

**Gaps.** The Spire's crown and soul-light ring (a custom emissive ring, or `FX_RingLight_*` from Sci-Fi City at Colossal scale); a proper Hegemony crest for banners.

### Z1: Transit Row

**Map: 2,000 m square, terrain height 300 m. Lv 1-22.**

**Description.** The commuter belt right outside the wall. The elevated rail toppled into the streets years ago and nobody came to lift it; the overpass is now a ramp you walk up. Traffic died in its lanes. Holo-ads still flicker on the billboards for products nobody buys. Weeds push through the asphalt, the street trees are dead, and the wind carries grit off the ochre hills. New Meridian's Spire is always behind you on the skyline.

**Life.** Hostile grounds 1-22 in eight steps outward from the city gate (Hostiles plan section 10): low levels near the wall, the Bruisers at the river and the depot. Suggested species (proposals; the owner picks): scavenger drones, feral maintenance bots, rad-hounds. Marines at Checkpoint Delta. Crows on the dead trees. Sound: wind, buzzing neon, a far alarm that never stops.

**Layout.** The Atlas's ring road hugging the wall, as Zone01 runs its route anticlockwise; a river cuts off the north island where the portal to Crestfall sits.

```
 N island: portal to Crestfall (+300,+780)  . Platform 9 (-420,+700)
 ~~~~~~~~~~~~~ river (z +560..+640) ~~~~~~~~~~ Fallen Overpass crosses it (0,+600)
   Burning Depot (+650,+300)        Billboard Row (-600,+250)
        ring road (r ~650)  Checkpoint Delta (+120,-150)
   Commuter blocks W            Dead lot E
        S: arrival from New Meridian's north gate (0,-900)
```

| Sub-area | Size | What happens there | Assets |
| --- | --- | --- | --- |
| **Gate Approach** (south) | 400 × 300 m | arrival; first Hostiles (Lv 1, 4) | Sci-Fi City road tiles running north, `SM_Prop_Barrier_*`, `SM_Prop_Sidewalk_Barrier_*`, dead street trees `SM_Env_TreeDead_*`, `SM_Veh_Future_Taxi_*` and `SM_Veh_Classic_*` stalled in the lanes. |
| **Commuter blocks** (west) | 600 × 500 m | Lv 7-10; climbable ruins | gutted `SM_Bld_Large_*` and `SM_Bld_Section_*` shells with `SM_Bld_Base_Wall_Destroyed_*` (Generic), rubble `SM_Prop_Sidewalk_Rubble_*`, `SM_Prop_RubbishPile_*`; Wallstride walls on the shells, roofs walkable. |
| **Billboard Row** (north-west) | 500 m strip | Lv 13; a vista down the road | `SM_Sign_Billboard_Large_*`, `SM_Prop_Billboard_Damaged_Sign_*` (Cyber City), `SM_Prop_Sign_Billboard_Broken_*` (Apocalypse Wasteland) flickering. |
| **The Fallen Overpass** (centre-north) | 400 m long | the only dry crossing to the island; chokepoint | `SM_Env_Highway_Straight_Long_*` on `SM_Bld_Bridge_Supports_*`, one span tipped 20° to the ground (deck at 7 m above the road), `SM_Env_Highway_Rubble_*` under it, Cyber City `SM_Bld_Train_Rail_*` fallen across, Meadow Forest `SM_Env_Grass_Tall_Clump_*` in the cracks. |
| **Checkpoint Delta** (centre) | 120 m compound | Marine outpost, the **Wayfinder** | `SM_Bld_Military_Wall_*`, `SM_Bld_Military_Pillar_*`, `SM_Prop_Barricade_*`, `SM_Prop_Floodlight_*`, `SM_Prop_Container_*` (Maps Sci-Fi Outpost), `SM_Veh_Apc_*`. |
| **The Burning Depot** (east) | 250 m yard | Bruiser lair (Lv 12) | bus depot `SM_Bld_Warehouse_*`, `SM_Veh_Garbage_*`, `SM_Veh_Armored_Truck_*` burned, `FX_Fire_*` and `FX_Smoke_Drift_*` (Sci-Fi City), `SM_Prop_Tyre_Pile_*` (Apocalypse Wasteland). |
| **The river** | 80 m wide | splits the island off | Generic water, `SM_Env_River_Edge_*` (Apocalypse Wasteland), reeds `SM_Env_Reeds_*`. |
| **North Island** | 700 × 400 m | Lv 19-22, Bruiser 21; the portal | Platform 9: a sunken metro entrance `SM_Bld_Underpass_*` (Sci-Fi City) with `SM_Prop_Security_Camera_*`, `SM_Prop_LightBar_*` in emergency red. The portal stands in a ring of `SM_Prop_Pillar_*`. |

**Points of interest.** Atlas: The Fallen Overpass, Platform 9 (Substation entrance), Checkpoint Delta (Wayfinder), The Burning Depot (Bruiser). New, my picks: *Billboard Row* (the ads still play: "Necessity is Civilization"); *Lane 4 Jam* (two hundred metres of abandoned cars, a crawl-over route); *The Last Commuter* (a skeleton still seated at a bus stop, `SM_Prop_Skeleton_*` on `SM_Prop_Bench_*`).

**Run: Substation 9** (1 room, 400 m map, the room about 60 × 40 m, 14 m high). A flooded metro power substation. Arc-light, dripping water, rogue maintenance drones still following their last order. Assets: Sci-Fi Space `SM_Bld_Wall_Mechanical_*`, `SM_Bld_Corridor_Double_Arch_*`, `SM_Prop_Detail_Pipe_*`; Cyber City `SM_Bld_Pipe_Large_*`; water plane at 0.6 m with `FX_Electricity_*` (Sci-Fi City) arcing off `SM_Bld_Power_*` transformers; drones `SM_Prop_Drone_*` on patrol rails. Layout: entry stairs from Platform 9 → catwalk ring 4 m up around a central transformer pit → the boss floor under the main breaker.

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

**Run: The Galleria** (2 rooms). Atrium (50 × 40 m, three galleries high, 18 m), then Food Court and Loading Dock (two linked halls, 40 × 30 m each). Mannequin-bots, escalators as ramps, muzak looping over the PA. Assets: Cyber City modular interior `SM_Bld_Base_*`, `SM_Bld_Stairs_*` as stopped escalators, `SM_Prop_Food_Shelf_*`, `SM_Prop_Vending_Machine_*`, tables `SM_Prop_Small_Table_*`, mannequins posed from `SM_Prop_Robot_Posed_*` (Cyber City); loading dock `SM_Bld_Door_Garage_*` (Sci-Fi Worlds), `SM_Veh_Truck_*`.

### Z3: Alder County

**Map: 2,000 m square, terrain height 300 m. Lv 45-67.**

**Description.** Upstate farm belt strung with soul-pylon lines. Automated harvesters still cut fields nobody eats from. Apple orchards in rows, cornfields to the horizon, red barns, hay bales, a river with a covered bridge. Then the craters and the burned farmsteads, where the Covenant raids landed. At night Pylon Junction 4 lights the sky soul-blue.

**Life.** Hostile grounds 49-67 (Hostiles plan), Bruisers 57 and 67. Suggested species (proposals): harvester-bots, field ghouls, raider bands. Crows, cattle bones, a lone horse. Sound: harvester engines far off, wind in the corn, the pylon hum rising toward the junction.

**Layout.** The Atlas's rolling patchwork split by a river; the pylon line is the leading line to Fort Alder and the Silo.

```
  Pylon Junction 4 (+600,+650)  ---- pylon line ---- Fort Alder (-300,+600) -> portal to Gantry Ridge
       Silo 12 (+250,+250)            Burned Farmstead (-550,+200)
  ~~~~~~~~~ river (z -100..-20), covered bridge (0,-60) ~~~~~~~~~
       The Orchard Rows (+450,-350)     Cornfields (-400,-450)
        S: arrival from Crestfall (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Fields** (everywhere) | 100-200 m patches | crop rows `SM_Env_CropField_Clump_*`, `SM_Env_Sunflower_*`, hay `SM_Prop_HandCart_*`, fences `SM_Prop_Meadow_Fence_*`, scarecrows `SM_Prop_ScareCrow_*`, windmill `SM_Bld_Windmill_*` (Meadow Forest); hedgerows of `SM_Env_Bush_*`. |
| **The Orchard Rows** (south-east) | 300 × 300 m | dense Hostile ground: `SM_Env_Tree_Fruit_*` (5 m, real apple-tree size) in rows 6 m apart. |
| **Farmsteads** (three) | 80 m each | barns from Fantasy Kingdom farm kit re-skinned (Atlas), `SM_Bld_Stone_Cabin_*` (Meadow Forest), silos `SM_Bld_Pod_Silo_*` (Sci-Fi Worlds), Outpost fences `SM_Bld_Fence_*`. One burned: the same pieces with scorched material and `SM_Env_Crater_*`. |
| **River and covered bridge** | 60 m wide | water, `SM_Env_Lillies_*`, the bridge `SM_Prop_Bridge_*` (Meadow Forest) roofed. |
| **The pylon line** | 1.6 km, a pylon every 120 m | `SM_Prop_Powerline_*`, `SM_Prop_Powerline_Antenna_*` (Sci-Fi Worlds) at 30 m; the junction is `SM_Bld_Corp_Generator_*` + `SM_Bld_Power_*` ringed by pylons, lit soul-blue. |
| **Silo 12** | 50 m tall | Run entrance: `SM_Bld_Scav_Refinery_Silo_*` (Sci-Fi Worlds) grouped in three, a harvester `SM_Veh_Scav_Lifter_*` parked at the door. |
| **Fort Alder** | 150 m fort | Hegemony forward base, Wayfinder: `SM_Bld_Corp_Wall_*`, `SM_Bld_Corp_Wall_Tower_*`, `SM_Bld_Corp_Barracks_*`, `SM_Prop_Floodlight_*`, `SM_Veh_HoverTank_*`. |

**Points of interest.** Atlas: Silo 12 (Run entrance), Pylon Junction 4, The Orchard Rows, Fort Alder (Wayfinder). New, my picks: *The Covered Bridge*; *Harrow Farm* (the burned farmstead, a Covenant rune painted on the barn door); *The Scarecrow Field* (Bruiser 57).

**Run: Silo 12** (2 floors). Grain Deck (circular, 40 m across, 20 m tall), then the Munitions Cache below (60 × 30 m vault). Harvester-bots turned sentries; the silo was never only for grain. Assets: Sci-Fi Worlds `SM_Bld_Pod_Silo_*` interior, `SM_Bld_Platform_*` decks, grain heaps from `SM_Env_Ground_Mound_*` (Meadow Forest) in gold; the cache: `SM_Prop_Crate_Military_*` (Sci-Fi City), `SM_Prop_Missile_*` (Sci-Fi Space), `SM_Prop_Turret_Small_Floor_*`.

### Z4: Gantry Ridge

**Map: 2,000 m square, terrain height 600 m. Lv 65-90 · Scar Gate.**

**Description.** Strip-mined highlands and the war front. The ridge climbs in terraces cut by the extraction gantries, a drill as tall as a tower biting into the summit. A trench line crosses the middle, wrecked APCs and a downed dropship in no-man's-land. Floodlit walls at the top guard the Scar Gate. Pines below, scorched stumps in the middle, snow on the upper ridge. The city's hunger laid bare.

**Life.** Hostile grounds 70-88 (Hostiles plan), Bruisers 78 and 89. Suggested species (proposals): war-frames, trench revenants, frost hounds. Searchlights sweep at night. Sound: the drill's grind, wind, distant artillery.

**Layout.** The Atlas's terraced ridge climbing by switchbacks to the fortified Scar Gate at the summit; the trench line across the middle.

```
                The Scar Gate (0,+850, 520 m up)
        Upper ridge: snow, Extraction Gantry Alpha (+400,+600)
  ===== The Trench Line (z +100) =====  Crash Site (-450,+250)
        Switchbacks, terraces 30 m apart in height
        Pine slopes (lower)   Mech graveyard (+500,-200)
        S: arrival from Alder County (0,-900, 40 m up)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Pine slopes** (lower) | the south third | `SM_Env_Pine_*` (Alpine Mountain) at real height (25-35 m), `SM_Env_Rock_Cliff_*`, `SM_Env_Bush_*`; switchback road `SM_Env_DirtRoad_*` (Sci-Fi Worlds). |
| **Terraces** | benches 30 m tall, 60 m deep | cut faces `SM_Env_Cliff_Flat_*` (Sci-Fi Worlds), mining kit `SM_Prop_Drill_*`, `SM_Bld_Bridge_Mining_Laser_*`, conveyor `SM_Bld_Scav_Refinery_Pipe_*`, scorched stumps `SM_Env_Pine_Stump_*`. |
| **The Trench Line** | 1.4 km across the map | trench cut 3 m deep, `SM_Bld_Military_Wall_Cover_*`, `SM_Prop_Barricade_*` (Maps Sci-Fi Outpost), `SM_Prop_Tank_Trap_*` (Apocalypse Wasteland), wrecked `SM_Veh_Apc_*`, craters `SM_Env_Crater_*`. |
| **Crash Site** | 120 m | a downed `SM_Veh_DropShip_*` (Sci-Fi Worlds) nose-down, debris `SM_Env_Debris_Structure_*` (Sci-Fi Space), `FX_Smoke_Large_*`. |
| **Mech graveyard** (new, my pick) | 150 m | two wrecked war-frames from the Mech pack (`SM_Veh_Mech_*` assembled and toppled), one kneeling as a climbable vista. |
| **Extraction Gantry Alpha** | 90 m tall | `SM_Bld_Planetary_Cannon_*` (Sci-Fi Worlds) as the colossal drill, Colossal tier, set over `SM_Env_Core_*` glowing soul-light; `SM_Bld_Bridge_*` catwalks. |
| **Upper ridge** | the north third | snow `SM_Env_Snow_Mound_*`, `SM_Env_Ice_Sheet_*` (Alpine Mountain), `FX_Snow_Soft_*` (Sci-Fi Worlds). |
| **The Scar Gate** | 200 m fort | `SM_Bld_Corp_Wall_*`, `SM_Bld_Corp_Wall_Gate_*`, `SM_Bld_Corp_Wall_Tower_Corner_*`, `SM_Prop_Searchlight_*`, `SM_Prop_Floodlight_*`; the portal glows Rupture violet in the gate. |

**Points of interest.** Atlas: Extraction Gantry Alpha, The Trench Line (Bruiser), Crash Site, The Scar Gate. New, my picks: *The Mech Graveyard*; *Switchback 7* (the Wayfinder, a mining office on the bend); *The Last Floodlight* (a vista over the Scar from the summit).

**Run: The Coreworks** (3 floors). Processing Deck (70 × 50 m), the Bore Shaft (a 30 m wide vertical drop circled by a ramp, 60 m deep), the Core Chamber (a 50 m dome). The extraction bore itself: soul-light leaking from every seam. Assets: Sci-Fi Worlds `SM_Env_Core_*`, `SM_Env_Core_Shell_*`, `SM_Env_Core_Strut_*`, `SM_Bld_Core_Vent_*`; Sci-Fi Space `SM_Bld_Floor_Large_Walkway_*`, `SM_Prop_Detail_Pipe_*`; `FX_Core_*` in the chamber.
## Elders-Reach (Covenant)

Atlas card: `World-Atlas.md`, "Elders-Reach". No mockup yet; the region painting (`art/concept-eldervales.jpg`) sets the look: a hall-city on a fjord, a standing-stone meadow with a burning village, a longship wreck under sea cliffs, the great tree-house tree, snow fells with runestones.

### The region kit

| | |
| --- | --- |
| Light | Low northern sun, long gold light, quick weather: sun and rain showers in the Vale, sea mist on Skeldfjord, green shafts in the Sylwood, white wind on Barrowfell. |
| Palette, mapped | Ember gold = torch and brazier light (`SM_Prop_Brazier_*`, `SM_Prop_Torch_*`, Viking Realm); forest green, oak and slate = Viking Realm and Elven Realm materials as shipped; snow = the `_Snow` variants (Knights, Vikings) and `SM_Env_Snow_*` (Viking Realm). |
| Ground | Grass and wildflowers (Meadow Forest), dark beach and shingle (`SM_Env_Beach_*`, Vikings), fern floor (Enchanted Forest), snow (Alpine Mountain). |
| Shared rocks | `SM_Env_Rock_Cliff_*` (Viking Realm), `SM_Env_Rock_*` (Meadow Forest), `SM_Env_Stone_*` (Viking Realm) as the grey Nordic boulders. |
| Shared trees | Birch `SM_Env_Tree_Birch_*` (Meadow Forest, about 18 m), pines `SM_Env_Tree_Pine_*` (Viking Realm, true size 9.7-13.8 m, brought to real 25-30 m), `SM_Env_Pine_*` (Alpine Mountain), elven trees `SM_Env_Tree_*` and `SM_Env_Tree_Glowing_*` (Elven Realm), giant trees `SM_Env_Tree_Giant_*` (Enchanted Forest, Colossal). |
| War marks | Burned villages: Fantasy Village houses with `SM_Bld_Castle_DestroyedWall_*` (Fantasy Kingdom), charred beams, `FX_Smoke_*` (Generic); extraction-crawler scars in the Sylwood (crashed `SM_Veh_Scav_Lifter_*`, Sci-Fi Worlds, the only tech in the region). |
| Run flavour | Torchlit earth, roots and runes, the restless dead: sorrowful, never gross. Built from Dungeon (`SM_Env_Cave_*`, `SM_Env_Entrance_Crypt_*`, `SM_Env_Rune_*`) and Dungeon Realms (`SM_Bld_Ruin_*`), lit with `SM_Prop_Torch_*` and `SM_Prop_Brazier_*`. |

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

**Run: Hearthbarrow** (1 room). A chieftain's barrow where the dead will not rest. One torchlit hall (40 × 20 m, 8 m high, timber-ribbed), one angry king. Assets: Dungeon `SM_Env_Entrance_Crypt_*`, `SM_Env_Ceiling_Arch_*`, `SM_Env_Stone_Throne_*`, `SM_Env_Rune_Pillar_*`; Viking Realm `SM_Prop_Chest_*`, `SM_Prop_Torch_Hanging_*`, a burial longship `SM_Prop_Boat_Structure_*` at the head of the hall.

### Z2: Skeldfjord

**Map: 2,000 m square, terrain height 400 m. Lv 20-45.**

**Description.** Sea cliffs, black-sand coves, longship wrecks. The wind never stops. A drowned village shows at low tide: roofs and a church bell in the shallows. A watchtower stands on the cliff path; out on the point, a sea-stack shrine you reach over a broken rope bridge. Gulls, surf and wind.

**Life.** Hostile grounds 25-43, Bruisers 33 and 44 (Hostiles plan). Suggested species (proposals): drowned huskarls, sea crabs, kelp horrors. Seals on the rocks, gulls. Sound: surf, gulls, the creak of wrecked hulls.

**Layout.** The Atlas's coastline strip: cliff path above, tidal flats and coves below, the Sea-Stack Shrine out on the point.

```
  sea (west) | coves and flats (x -900..-400) | cliff path (x -400..-200, 60-120 m up) | moor (east)
  Sea-Stack Shrine (-950,+700)  Wreck of the Sigrun (-700,+300)
  The Drowned Village (-650,-250)   Cliff Watchtower (-300,0)
  S: arrival from Elk Pass (+200,-900)    N: portal to the Sylwood (+300,+880)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Cliff path** | 1.8 km long, 15 m wide | `SM_Env_Rock_Cliff_*` (Viking Realm) at 60-120 m, `SM_Env_Path_Rocks_Side_*` (Vikings), windswept pines `SM_Env_Tree_Pine_*` leaning, heather `SM_Env_Flax_*` (Vikings). |
| **Coves and black-sand flats** | 500 m wide band | `SM_Env_Beach_*`, `SM_Env_Beach_Long_*` (Vikings), driftwood `SM_Env_DriftWood_*`, kelp `SM_Env_Seaweed_Beach_*` (Tropical Jungle), rocks `SM_Env_Rock_Round_*`. |
| **The Drowned Village** | 250 m | Fantasy Village houses half-sunk: walls `SM_Bld_Wall_Curved_*` to the eaves in water, roofs `SM_Bld_Roof_Thatch_*`, jetties `SM_Prop_Jetty_Broken_*` (Swamp Marshland); a tide plane that drops 3 m on a cycle (a later system; build it at low tide). |
| **Wreck of the Sigrun** | 40 m hull | Bruiser 33: `SM_Prop_ShipWreck_*` (Tropical Jungle) recoloured to a longship, `SM_Prop_Boat_Destroyed_*` (Viking Realm) beside it. |
| **Cliff Watchtower** | | Wayfinder: `SM_Bld_Guard_Tower_*` (Vikings) with `SM_Prop_Beacon_*` (Viking Realm) fire basket. |
| **Sea-Stack Shrine** | stack 70 m high | `SM_Env_Rock_Cliff_*` stacked as a pillar, `SM_Bld_RopeBridge_*` (Tropical Jungle) broken half-way, shrine `SM_Bld_Shrine_*` (Fantasy Village) on top with `SM_Prop_RuneStone_*`. |
| **The moor** (east) | | heather, `SM_Env_Grass_*` (Vikings), cairns, the road inland. |

**Points of interest.** Atlas: The Drowned Village, Wreck of the Sigrun (Bruiser), Cliff Watchtower (Wayfinder), Sea-Stack Shrine. New, my picks: *The Bell Under the Tide* (the drowned village's chapel bell rings at low tide); *Wrecktide Cave* (the Run entrance at the cliff foot, only reachable over the flats); *The Whale Ribs* (`SM_Prop_Whale_Skeleton_*`, Tropical Jungle, on the north beach).

**Run: Wrecktide Grotto** (2 rooms). Wreck Cavern (60 × 40 m sea cave, broken longships wedged in the rock), then the Tide Hall (a flooded hall of drowned warriors, 40 × 30 m). Assets: Dungeon `SM_Env_Cave_Large_*`, `SM_Env_Stalactite_*`, `SM_Env_Sewer_Water_*` as tide pools; Viking Realm `SM_Prop_Boat_Destroyed_*`, `SM_Prop_Barnacle_*`; `SM_Prop_ShipWreck_*`.

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

**Run: The Rootvault** (2 floors). Root Hall (60 m long, living roots for walls), then the Ossuary Hollow (a round chamber 40 m across, niches of elven dead). Assets: Enchanted Forest `SM_Env_Roots_*`, `SM_Env_Tree_Trunk_*`; Dungeon `SM_Env_Cave_Curved_*`, `SM_Env_BonePile_*`, `SM_Env_Rune_Rounded_*`; Elven Realm `SM_Prop_Candle_Stand_*`.

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

**Run: Howlstone Crypts** (3 floors). Barrow Halls (long galleries of burial niches), the Hall of Kings (a 60 × 30 m frozen hall of seated stone kings), the Deep Cairn (a round pit 40 m across, a spiral stair down to the last king). Assets: Dungeon `SM_Env_Ceiling_Stone_Curved_*`, `SM_Env_Statue_*`, `SM_Env_SpiralStairs_*`, `SM_Env_Stalagmite_*`; Dungeon Realms `SM_Bld_Ruin_Pillar_*`; Viking Realm `SM_Env_Icicle_*`, `SM_Prop_Throne_*`.
## Cessation (Null)

Atlas card: `World-Atlas.md`, "Cessation". Mockups: the Cessation overview and its closer view (Figma 140:2, 140:3).

### The region kit

| | |
| --- | --- |
| Light | Cold and exact. Night or deep dusk most of the time, under a **green and violet aurora** (`FX_Aurora_Mesh_*`, Nature Biomes) across the whole sky. No fire anywhere: braziers burn cold violet, windows glow amber-white, the pit glows crimson. |
| Palette, mapped | Blood red (banners `SM_Prop_Banner_*`, Dark Fortress, recoloured; the pit light), necrotic violet (crystals and aurora), bone (`SM_Env_Bones_*`, Dark Fortress), basalt (`SM_Env_Basalt_*`, `SM_Env_Cliff_Basalt_*`, Dark Fantasy), a sick green sky. |
| Ground | **Pale ash-snow** (the mockups): a white-grey ground layer with frost, cracked earth (`SM_Env_Ground_Fissure_*`, Apocalypse Wasteland), black basalt outcrops. Frost and frozen falls are new to the Atlas and come from the mockups. |
| Shared rocks | Basalt needles `SM_Env_Detail_Spire_*`, `SM_Env_Detail_Spire_Group_*` (Sci-Fi Worlds) and `SM_Env_Basalt_*` (Dark Fantasy); `SM_Env_Rocks_Spikes_*` (Arid Desert); floating shards `SM_Env_Floating_Rocks_*` (Sci-Fi Worlds), `SM_Env_Hell_Floating_Rock_Chain_*` (Dungeon Realms). |
| Shared "trees" | None alive. Dead trees `SM_Env_Tree_Dead_*` (Dark Fortress, Dark Fantasy), cable vines `SM_Prop_Cable_*` (Sci-Fi Worlds), crystal clusters `SM_Env_Crystals_Cluster_Large_*` (Dungeon Realms), `SM_Env_Gem_Spike_*` (Dungeon), `SM_Prop_Crystal_*` (Enchanted Forest) recoloured violet. |
| Bones of the old world | Giant fossils: `SM_Env_Skull_Huge_*` (Dungeon Realms), `SM_Env_Bone_Rigcage_*`, `SM_Env_Bone_Rib_*`, `SM_Env_Bone_Spine_*` (Dungeon) scaled Colossal (ribs 20-30 m). |
| Machines | Null outposts: `SM_Bld_Scav_*` (Sci-Fi Worlds), radar dish `SM_Bld_Scav_Dish_*`, lattice towers `SM_Bld_Corp_Comms_Tower_*`; wrecked giants from the Mech pack and the Dungeon Realms golem `SM_Env_GiantGolem_*`. |
| Run flavour | Precise, humming, funereal: machinery that never stopped and ossuaries arranged like circuit boards. Sci-Fi Space interiors in black and red, with Dungeon bone dressing laid out on a grid. |

### Home City: Terminus

**Map: 1,600 m square, terrain height 400 m.** Atlas: a crater city of gear and bone; a spiral road down the crater wall to the Assembly Core; the Ossuary Spire on the rim. The mockups add the gothic iron fortress on the ridge.

**Description.** The crater is 900 m across and 250 m deep. A spiral road winds down its wall in three turns, lined with conveyor streets where skeletal and machine citizens move in perfect order, never hurrying, never stopping. At the bottom the Assembly Core hums under a lattice dome. On the rim, the Ossuary Spire and the iron fortress stand against the aurora. Nobody raises their voice. The only colour is the red of the banners and the violet of the lamps.

**Life.** Null citizens (Sidekick Fantasy Skeletons, Sci-Fi Robots) in lines; drones; conveyor carts moving boxes of bone. No animals. Sound: a deep machine hum, conveyor clatter, a bell that rings once an hour exactly.

**Layout (crater centre 0,0).**

```
   Iron fortress on the north rim (0,+520)   Ossuary Spire (+380,+380, rim)
   spiral road: rim (r 450, 250 m up) -> r 300 (160 m) -> r 150 (70 m) -> floor (r 100, 0 m)
   Reclamation Yards (west terrace, r 300)   Conveyor Concourse (east terrace, r 300)
   Assembly Core (0,0 on the floor)
   south rim gate (0,-520) -> The Palefields
```

| District | Assets |
| --- | --- |
| **The crater walls** | `SM_Env_Cliff_Curve_Large_*` (Dark Fortress), `SM_Env_Cliff_Basalt_*` (Dark Fantasy), frozen falls `SM_Env_Water_Plane_Waterfall_*` (Dark Fortress) in ice-white. |
| **The spiral road** | 12 m wide, paved with `SM_Env_Path_Tiles_*` (Dark Fortress); railings `SM_Bld_Bridge_Railing_*` (Dark Fantasy); lamps `SM_Prop_Brazier_*` burning violet. |
| **Conveyor Concourse** (Broker, Kiosks) | conveyor belts from `SM_Bld_Scav_Refinery_Pipe_*` laid flat and `SM_Prop_Pod_Pipe_*` (Sci-Fi Worlds); stalls `SM_Bld_Vendor_*`; boxes `SM_Prop_Crate_*`. |
| **Reclamation Yards** (Armorer, Fixer) | `SM_Bld_Scav_Garage_*`, `SM_Bld_Scav_Refinery_*` (Sci-Fi Worlds), robot parts `SM_Prop_Robot_Part_*`, `SM_Prop_Robot_Posed_Pile_*` (Cyber City). |
| **Assembly Core** (Chancellor, Herald) | Sci-Fi Worlds `SM_Env_Core_*` at the crater floor, under a cage of `SM_Env_Core_Strut_*`; `FX_Core_*`. |
| **Ossuary Spire** | 120 m: Dark Fortress round walls `SM_Bld_Wall_L_Curve_*` stacked, `SM_Bld_Tower_Roof_*` cap; inside, niches of skulls `SM_Prop_Skull_Pile_*` in exact rows. |
| **The iron fortress** (north rim) | the mockups' castle: `SM_Bld_Wall_L_*`, `SM_Bld_Wall_L_Gate_*`, `SM_Bld_Wall_L_Curve_*`, `SM_Bld_Tower_Roof_*`, `SM_Bld_Drawbridge_01_Preset_*` (Dark Fortress); red banners `SM_Prop_Banner_*`. |

**Points of interest.** Atlas: The Assembly Core, The Reclamation Yards, The Ossuary Spire, Conveyor Concourse. New, my picks: *The Count* (an hourly bell in the Spire that every citizen stops for); *The Rim Gate* (south, toward the Palefields); *The Frozen Fall* (a waterfall frozen down the crater wall, a vista).

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

**Run: The Idle Works** (1 room). A stalled fabrication hall (70 × 30 m, 14 m high) where unawakened machines twitch on the line. Cold strip-light, one great press at the far end. Assets: Sci-Fi Space `SM_Bld_Wall_Mechanical_*`, `SM_Prop_Detail_Machine_*`; Cyber City `SM_Prop_Assembly_Machine_*`, `SM_Prop_Robotic_Needle_Arm_*`, `SM_Prop_Robot_Posed_*` on the line; the press `SM_Prop_Drill_*` (Sci-Fi Worlds) mounted vertically.

### Z2: The Scrapline

**Map: 2,000 m square, terrain height 400 m. Lv 20-45.**

**Description.** A trench grid of machine graveyards. Chassis stacked like rock strata form whole cliff walls. Scrap bridges cross the cuts. Magnetic storms roll over the plateau, and loose metal lifts and drifts in the air when they pass. In the central cut a crashed hauler, the Magnet, lies across the trench like a dam.

**Life.** Hostile grounds 25-43, Bruisers 33 and 44 (the Magnet). Suggested species (proposals): scrap golems, magnet-wraiths, salvage swarms. No animals. Sound: metal groaning, storm crackle, distant hammering from Reclamation Camp.

**Layout.** The Atlas's parallel trenches cut across a plateau; scrap bridges link the plateaus; the crashed hauler blocks the central cut.

```
   Plateau N ---- Scrap Bridge B ---- Plateau N2      portal to the Needlegarden (+400,+880)
   ====== trench 3 (z +400, 40 m deep) ======
   Plateau M      The Magnet (0,+50) blocks trench 2      Reclamation Camp (+600,+100)
   ====== trench 2 (z 0) ======   The Chassis Wall (-600, both sides)
   Plateau S ---- Scrap Bridge A (-200,-400)
   ====== trench 1 (z -400) ======
          S: arrival from the Palefields (0,-900)
```

| Sub-area | Size | Assets |
| --- | --- | --- |
| **Trenches** | 3 cuts, 40 m deep, 60-100 m wide | walls of `SM_Env_Cliff_Flat_*` (Sci-Fi Worlds) faced with stacked scrap `SM_Env_Scrap_Pile_*`, `SM_Env_Junk_Pile_*` (Cyber City), `SM_Prop_Scav_Scrap_Pile_*`; floors of rust lichen (Dark Fantasy `SM_Env_Grunge_*`, `SM_Env_Moss_*`). |
| **The Chassis Wall** | 400 m cliff | strata of robot bodies: `SM_Prop_Robot_Posed_Pile_*` (Cyber City), Mech pack frames `SM_Mech_*`, `SM_Veh_Truck_*` crushed between. |
| **Scrap bridges** | 2, 80-120 m | chokepoints: `SM_Bld_Bridge_L_*` (Dark Fortress) patched with `SM_Bld_Scav_Wreckage_*`, cables `SM_Prop_Tether_Wire_*` (Sci-Fi Worlds). |
| **The Magnet** | 160 m hull | Bruiser 44: a crashed hauler `SM_Ship_Massive_Transport_*` (Sci-Fi Space) at ground scale, broken in two, `FX_Electricity_*`, floating scrap `FX_Scrap_Floating_Mesh_Full_*` (Sci-Fi Worlds). |
| **Reclamation Camp** | 150 m | Wayfinder: `SM_Bld_Crate_Building_*`, `SM_Bld_Scav_Garage_*`, `SM_Bld_Scav_Dish_*` (Sci-Fi Worlds), red banners. |
| **Plateaus** | | cable vines `SM_Prop_Cable_*`, hull plates `SM_Env_Debris_Shell_*` (Sci-Fi Space), `SM_Env_Ground_Junk_*` (Sci-Fi Worlds). |

**Points of interest.** Atlas: The Chassis Wall, Scrap Bridge (chokepoint), The Magnet (Bruiser), Reclamation Camp (Wayfinder). New, my picks: *The Disassembly Gate* (the Run entrance cut into the Chassis Wall); *Storm Spire* (a lightning-struck mast where the storms start); *The Sorting Field* (parts laid out by size on a plateau).

**Run: The Disassembly** (2 rooms). The Line (a 90 m conveyor hall, machines being taken apart in sequence), then the Press (a 40 m square chamber under a moving crusher). Where the Hegemony once broke machines for parts; the machines remember the sequence. Assets: Sci-Fi Space `SM_Bld_Floor_Large_Walkway_*`, `SM_Prop_MedicalArms_*` as dismantling arms; Cyber City `SM_Prop_Robotic_Needle_Arm_*`, `SM_Prop_Robot_Posed_Hanging_*`; Dungeon `SM_Env_Trap_Grinder_*`.

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

**Run: Ossuary Sigma** (2 floors). The Reliquary (a 60 × 40 m hall of bone niches in a circuit-board grid), then the Bone Choir (a round chamber 50 m across, ribs as arches). A skeletal catacomb grown into the basalt, arranged with machine precision. Assets: Dungeon `SM_Env_BonePile_*`, `SM_Env_Bone_Ribs_*`, `SM_Env_Tiles_Ornate_*`; Dark Fortress `SM_Prop_Skull_Pile_*`, `SM_Env_Spirit_Pool_*`; Sci-Fi Space `SM_Prop_Light_Panel_*` in red lines along the floor.

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

**Run: The Stirring Well** (3 floors). Bore Rim (a ring gallery round a 40 m shaft), the Descent (a spiral ramp 80 m down the shaft wall), the Listening Chamber (a 60 m cavern where the floor breathes). Down toward whatever stirs beneath Nova; its nature stays open (Lore Bible section 10). Assets: Sci-Fi Worlds `SM_Env_Core_Shell_*`, `SM_Bld_Core_Vent_Tunnel_*`; Dungeon `SM_Env_Cave_Large_*`, `SM_Env_SpiralStairs_*`; Dungeon Realms `SM_Env_Hell_Ring_*`, `SM_Env_Lava_Flow_*` recoloured crimson.

## The Scar (contested)

Atlas card: `World-Atlas.md`, "The Scar". No mockup yet; the region painting (`art/concept-scar.jpg`).

**Map: 3,000 × 1,500 m, terrain height 500 m. Lv 88-105 · always-on PvP.** A long valley running east-west.

**Description.** A vast rent of glassed earth. A severed extraction titan lies across the middle of it, its chest open, its arm reaching up the south wall. Black glass, ash drifts, cracked edges; the violet aurora overhead is brighter here than anywhere. Three faction gates open onto the valley, one at each end and one on the south wall; high on the north wall, a descent nobody uses yet. Beacon towers on the flanks. Everyone is pushed into the same valley.

**Life.** Hostile grounds 91-103, Bruisers 98 and 105 roaming the crossing (Hostiles plan). Players of every faction. No animals. Sound: wind over glass, the titan creaking, war horns from every side.

**Layout (valley centre 0,0, long axis east-west).**

```
  north wall (z +700, 400 m up) ........ Ronin descent (hidden) (+300,+680)
  Hegemony gate (W end, -1450,0)    Beacon West (-900,-300)   Beacon North (0,+450)
           The Glass Cliffs (z +250..+400, sniper ridges)
           THE SEVERED TITAN (lies -400..+500 along z 0); Ruptureworks portals under its chest (0,-60)
  Covenant gate (E end, +1450,0)   Beacon East (+900,-300)
  Null gate (south wall, 0,-700)
```

| Sub-area | Assets |
| --- | --- |
| **Valley floor** | black glass terrain layer, ash drifts `SM_Env_SandDunes_*` (Apocalypse Wasteland) greyed, `SM_Env_Cracked_Edge_*`, `SM_Env_Crater_Edge_*` (Sci-Fi Worlds), lava seams `SM_Env_Lava_River_*` (Arid Desert). |
| **The Severed Titan** | 900 m long, Colossal: the Dungeon Realms golem `SM_Env_GiantGolem_Chest_*`, `SM_Env_GiantGolem_Head_*`, `SM_Env_GiantGolem_Arm_R_Upper_*`, `SM_Env_GiantGolem_Leg_L_Lower_*` and the rest scaled about 20×, plated with Sci-Fi Worlds `SM_Env_Core_Shell_*`; you walk across it on the central crossing. |
| **Beacon sites** (3) | `SM_Bld_Corp_Comms_Tower_*` stripped to the frame, each on a mound of `SM_Env_Rock_Large_*`, ringed with barricades `SM_Prop_Barricade_*` (Maps Sci-Fi Outpost) and ruined walls `SM_Bld_Wall_L_Damaged_*` (Dark Fortress). |
| **The Glass Cliffs** | `SM_Env_Cliff_Curved_Large_*` (Sci-Fi Worlds), `SM_Env_Cliff_Basalt_*` (Dark Fantasy), landslides `SM_Env_Landslide_*`. |
| **The gates** | each faction's gate dressed in its own kit (Hegemony corp wall, Covenant carved arch, Null iron gate), the same pieces as its Z4's Scar Gate. |
| **Sky** | `SM_Env_CloudRing_*` (Sci-Fi Worlds), `FX_Aurora_Mesh_*` (Nature Biomes) in Rupture violet. |

**Points of interest.** Atlas: The Severed Titan, Beacon North / East / West, The Glass Cliffs, The Ruptureworks portals. New, my picks: *The Titan's Hand* (its hand on the south wall, a climbable vista); *Ash Flats* (the open killing ground between the Null gate and the titan). The Beacon site names stay open (Atlas).

### Run: The Ruptureworks

**Lv 103-112, 4 floors.** Intake, Separation, the Cycle Chamber, the Rupture. The ruined extraction machinery itself, deeper than any faction Run.

| Floor | Size | Assets |
| --- | --- | --- |
| **Intake** | 80 × 60 m hall of soul conduits | Sci-Fi Space `SM_Bld_Wall_Tube_*`, `SM_Bld_Wall_Pods_*`; Dark Fortress `SM_Env_Spirit_Spread_*` leaking across the floor. |
| **Separation** | three 30 m tanks over a gantry | Sci-Fi Worlds `SM_Bld_Scav_Refinery_Silo_*` as tanks, `SM_Bld_Bridge_*`; Dark Fortress `SM_Env_Spirit_Pool_*` inside them. |
| **The Cycle Chamber** | a 70 m ring | Sci-Fi Worlds `SM_Env_Core_*` ringed by `SM_Env_Core_Strut_*`, Dungeon Realms `SM_Env_Hell_Ring_Chains_*`. |
| **The Rupture** | a cavern with no floor, 100 m across | floating platforms `SM_Env_Floating_Rocks_*`, `SM_Env_Spirit_Ocean_*` (Dark Fortress) below, violet light everywhere. |

The Rupture's tie to the original catastrophe stays undecided (Lore Bible section 8); this dressing shows extraction machinery, not the first wound itself.
## Sunderpeak (Ronin, expansion: hidden at launch)

**Expansion spoiler.** Nothing here is shown in game before the Ronin reveal (Lore Bible sections 6, 7, 14): the maps are built, kept out of the build list and the travel list, and no player-facing text names them. Atlas card: `World-Atlas.md`, "Sunderpeak". Mockup: Kagero (Figma 125:78).

### The region kit

| | |
| --- | --- |
| Light | Dusk and steam. Warm lantern light low in the valleys, cold blue snow light on the peaks; mist always moving. |
| Palette, mapped | Lacquer red and black (Samurai Empire buildings as shipped), moss, snow (`SM_Bld_Snow_*` variants, Samurai), moon silver and deep indigo for the Ronin colours (the Atlas proposal; the mockup's red banners with a white diamond and some blue ones are a colour reference only, the crest and colours stay open). |
| Ground | Moss and gravel (`SM_Env_Moss_*`, `SM_Env_Gravel_Pile_*`, Samurai Empire), rice terraces (`SM_Env_FarmTerrace_*`, `SM_Env_PaddyHills_*`, Samurai), snow (Alpine Mountain). |
| Shared rocks | `SM_Env_Rock_Cliff_*`, `SM_Env_Rock_Basalt_*`, `SM_Env_Wall_Rock_*` (Samurai Empire), `SM_Env_Cliff_*` (Samurai). |
| Shared trees | Cherry `SM_Env_Tree_Cherry_Blossom_*` (8 m), maple `SM_Env_Tree_Maple_*`, ginkgo `SM_Env_Tree_Ginko_*`, dark pines `SM_Env_Tree_Pine_*` (Samurai Empire, brought to 25 m), bamboo `SM_Env_Bamboo_*` (Samurai Empire, 12-15 m). |
| Steam | `FX_Smoke_Cloud_*`, `FX_Fog_*` (Nature Biomes) from vents and pools. |
| Run flavour | Hot-spring caves, lacquered shrine vaults, ice archives: what the Ronin hid, and what they did to keep it hidden. |

### Hidden Capital: Kagero

**Map: 1,600 m square, terrain height 500 m.** Atlas: terraces climb the valley wall from the hot springs to the Council. The mockup makes it a **gorge city**: terraces on both walls, a waterfall at the head, red arched bridges across.

**Description.** The gorge is 300 m deep. A waterfall closes its head in a white curtain; below it the river runs blue between black rocks. Both walls are terraced: lantern-lit houses, red-railed walkways, stairs everywhere, cherry trees in bloom, rice terraces glowing with the sky in their water. Two red arched bridges cross the gorge at different heights. Pagodas crown the top terraces against snow peaks. Machines, skeletons, humans and elves share one street. Named for heat-haze: seen and not seen.

**Life.** Ronin citizens of every origin (Sidekick Samurai Warriors, Fantasy Skeletons, Sci-Fi Robots, Elven Warriors together); cranes (`FX_Cranes_*`, Nature Biomes) over the gorge. Sound: the waterfall, wind chimes, a temple bell.

**Layout (gorge centre 0,0, running north-south, waterfall at the north end).**

```
        waterfall (0,+600)  Council of Three (pagoda, east top terrace +200,+450, 280 m up)
   West wall terraces (x -300..-120)  | river | East wall terraces (x +120..+300)
        Upper bridge (0,+250, 180 m up)     The Shrine of the Unerased (-220,+380, top)
        Mixed Market (east, +200,0, 120 m up)  rice terraces (west, -250,-150)
        Lower bridge (0,-100, 60 m up)      The Steam Baths (0,-400, gorge floor)
        hidden road out (south, 0,-780) -> The Ember Terraces
```

| District | Assets |
| --- | --- |
| **Terraces** (both walls) | retaining walls `SM_Bld_Platform_Wall_*`, `SM_Bld_Platform_Pillar_*` (Samurai Empire), houses `SM_Bld_House_Base_*` with `SM_Bld_House_Roof_Wood_*` (Samurai), walkways with `SM_Bld_Railing_*`, stairs `SM_Bld_Stairs_Wide_*`, `SM_Env_Stone_Stairs_*`. |
| **Bridges** | red arched bridges `SM_Bld_Bridge_*`, `SM_Bld_Bridge_Pillar_*` (Samurai Empire), `SM_Env_Bridge_Curved_*` (Samurai), stretched to 120-180 m spans on stone piers. |
| **The Council of Three** | a three-tier pagoda: `SM_Bld_Castle_Base_Large_Windows_*`, `SM_Bld_Castle_Roof_Large_*`, `SM_Bld_Castle_Roof_Top_*` (Samurai), 40 m. |
| **The Mixed Market** | stalls `SM_Bld_Awning_*`, `SM_Prop_Lantern_*`, `SM_Prop_Banner_*` (Samurai Empire); Broker and Kiosks. |
| **The Steam Baths** | `SM_Prop_Bath_*` (Samurai Empire), pools `SM_Env_PondEdge_*`, `SM_Env_PondGround_*` (Samurai), steam. |
| **The Shrine of the Unerased** | `SM_Bld_Shrine_*`, `SM_Prop_Shrine_*`, `SM_Prop_Torii_Gate_*` (Samurai Empire); walls of name tablets. |
| **Rice terraces** | `SM_Env_FarmTerrace_*`, `SM_Env_RicePlant_*` (Samurai) on the west wall, lit water. |
| **Waterfall** | `SM_Env_Water_Plane_Waterfall_*` (Samurai Empire) 250 m tall, `FX_Waterfall_Impact_*` (Nature Biomes). |

**Points of interest.** Atlas: The Council of Three, The Steam Baths, The Mixed Market, The Shrine of the Unerased. New, my picks: *The Two Bridges*; *The Lantern Stair* (a stair of 800 lanterns up the east wall).

### Z1: The Ember Terraces

**Map: 2,000 m square, terrain height 400 m. Lv 1-22.**

**Description.** Geothermal rice terraces and hot-spring pools under drifting mist. The valley's farms, warmed from below: green steps down the slopes, water steaming in the morning, geysers on a timer in the vent fields. The Watch Gate guards the only road out.

**Life.** Hostile grounds 1-22. Suggested species (proposals): steam imps, terrace boars, mist foxes. Herons and frogs (`FX_Frogs_*`). Sound: water, bubbling vents, a bamboo water-clapper.

| Sub-area | Assets |
| --- | --- |
| **Terraces** (stepped, following a stream) | `SM_Env_FarmTerrace_*`, `SM_Env_PaddyHills_*`, `SM_Env_CropRow_*`, `SM_Env_RicePlant_*` (Samurai); maples `SM_Env_Tree_Maple_*`. |
| **The Vent Fields** | geysers on a timer: `SM_Env_Sulphur_Pool_*`, `SM_Env_Sulphur_Ground_*` (Arid Desert) with `FX_Sulphur_Smoke`. |
| **Hot-spring Pools** | `SM_Env_PondEdge_*`, `SM_Env_PondGround_*` (Samurai), `SM_Env_Rock_Circle_*` (Samurai Empire); Hostiles bathe here too. |
| **Terrace Shrine** | `SM_Prop_Shrine_*`, `SM_Prop_Torii_Gate_*`, `SM_Prop_Lantern_Stone_*` (Samurai Empire). |
| **The Watch Gate** | Wayfinder: `SM_Bld_Gate_Dobei_*`, `SM_Bld_Fence_Dobei_*` (Samurai Empire), a watch tower `SM_Bld_Tower_*` (Samurai). |

**Points of interest.** Atlas: The Vent Fields, Terrace Shrine, Hot-spring Pools, The Watch Gate. New, my pick: *Steamhollow Mouth* (the Run entrance behind the hottest pool).

**Run: Steamhollow** (1 room). A scalding cave behind the springs (50 × 30 m): boiling pools, one narrow ledge round them, things that like the heat. Assets: Dungeon `SM_Env_Cave_*`, `SM_Env_Stalactite_*`; Dungeon Realms `SM_Env_Lava_Plane_*` recoloured to boiling water; steam.

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

**Run: The Listening House** (2 rooms). The Hall of Ears (40 × 20 m, its walls lined with bronze listening horns), then the Cellar (a low 30 m vault). A half-sunken shrine where the assassins learned to listen before they learned to kill. Assets: Samurai Empire `SM_Bld_Wall_*`, `SM_Bld_Sliding_Door_*`, `SM_Bld_Floor_*`; Dungeon `SM_Env_Basement_*`.

### Z3: Blackfall Gorge

**Map: 2,000 m square, terrain height 600 m. Lv 45-67.**

**Description.** Black-water falls, rope bridges, lantern-cut cliffs. Roar and spray everywhere; the light comes from ten thousand lanterns set into the rock. Cliff paths run along both walls, rope bridges swing between them, and the Great Fall closes the far end.

**Life.** Hostile grounds 49-67. Suggested species (proposals): spray wraiths, cliff apes, lantern moths. Hawks (`FX_Hawk_*`). Sound: the falls, always.

| Sub-area | Assets |
| --- | --- |
| **Cliff paths** (both walls) | `SM_Env_Rock_Cliff_*`, `SM_Env_Wall_Rock_*` (Samurai Empire), ledges `SM_Bld_Platform_*` (Samurai), cliff pines `SM_Env_Tree_Pine_*`, spray moss `SM_Env_Moss_*`. |
| **Rope bridges** | `SM_Bld_RopeBridge_*` (Tropical Jungle), 60-150 m spans; Rope Bridge Nine holds the Bruiser. |
| **Lantern Cliffs** | the Run entrance: cliffs studded with `SM_Prop_Lantern_Wall_*`, `SM_Prop_Lantern_Feature_*` (Samurai Empire). |
| **The Great Fall** | 300 m: `SM_Env_Water_Plane_Waterfall_*` (Samurai Empire), `FX_Waterfall_Impact_*`, `FX_Water_Splash_*`. |
| **The Drowned Gate** | Wayfinder: a torii `SM_Prop_Torii_Gate_*` standing in the river. |

**Points of interest.** Atlas: The Great Fall, Rope Bridge Nine (Bruiser), Lantern Cliffs (Run entrance), The Drowned Gate (Wayfinder). New, my pick: *The Ten Thousand* (the lantern wall seen from the lower bridge, a vista).

**Run: The Lantern Gallery** (2 floors). The Gallery of Ten Thousand Lanterns (a 100 m long cave lined with lanterns, one for each name the Ronin could not keep), then the Cold Shrine (a 30 m ice-cold chamber). Assets: Dungeon `SM_Env_Cave_Large_*`; Samurai Empire `SM_Prop_Lantern_*`, `SM_Prop_Shrine_Wall_*`; Viking Realm `SM_Env_Icicle_*` in the shrine.

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

**Run: The Quiet Archive** (3 floors). The Stacks (a 60 m hall of shelves), the Map Room (a round 30 m room, a carved relief of the homeland), the Vault of Erasure (a sealed 20 m vault). Every stolen map and record of the homeland is kept here. Assets: Samurai Empire `SM_Bld_Wall_*`, `SM_Bld_Alcove_*`; Elven Realm `SM_Prop_Cabinet_*`, `SM_Prop_BookHolder_*`; Alpine Mountain `SM_Env_Ice_Sheet_*`.

## The owner's mockups and the first-pass shots

The owner's five finished-look mockups from the Figma World Atlas, then a shot of every built map. The shots are taken in the maps themselves through the game's comic look: what can be built, because it is what was built. They are a first pass, before the hand dressing (taken again 19 Oct 2026 with the imported packs' pieces in place). Every place's folder holds more shots: one per point of interest, and a top-down Layout.

![Mockup: New Meridian, isometric](art/mockups/mockup-new-meridian-isometric.jpg)

![First pass: New Meridian, the same view](art/zones/MeridianSprawl_NewMeridian_Isometric.jpg)

![Mockup: New Meridian, street](art/mockups/mockup-new-meridian-street.jpg)

![First pass: New Meridian, street](art/zones/MeridianSprawl_NewMeridian_Street.jpg)

![Mockup: Cessation](art/mockups/mockup-cessation-overview.jpg)

![Mockup: Cessation, closer](art/mockups/mockup-cessation-close.jpg)

![Mockup: Kagero](art/mockups/mockup-kagero.jpg)

### Shots: Meridian Sprawl

![NewMeridian](art/zones/MeridianSprawl_NewMeridian_Vista.jpg)

![Run Silo12](art/zones/MeridianSprawl_Run_Silo12_Vista.jpg)

![Run Substation9](art/zones/MeridianSprawl_Run_Substation9_Vista.jpg)

![Run TheCoreworks](art/zones/MeridianSprawl_Run_TheCoreworks_Vista.jpg)

![Run TheGalleria](art/zones/MeridianSprawl_Run_TheGalleria_Vista.jpg)

![Z1 TransitRow](art/zones/MeridianSprawl_Z1_TransitRow_Vista.jpg)

![Z2 Crestfall](art/zones/MeridianSprawl_Z2_Crestfall_Vista.jpg)

![Z3 AlderCounty](art/zones/MeridianSprawl_Z3_AlderCounty_Vista.jpg)

![Z4 GantryRidge](art/zones/MeridianSprawl_Z4_GantryRidge_Vista.jpg)

### Shots: Elders-Reach

![Aldhavn](art/zones/EldersReach_Aldhavn_Vista.jpg)

![Run Hearthbarrow](art/zones/EldersReach_Run_Hearthbarrow_Vista.jpg)

![Run HowlstoneCrypts](art/zones/EldersReach_Run_HowlstoneCrypts_Vista.jpg)

![Run TheRootvault](art/zones/EldersReach_Run_TheRootvault_Vista.jpg)

![Run WrecktideGrotto](art/zones/EldersReach_Run_WrecktideGrotto_Vista.jpg)

![Z1 BirchlightVale](art/zones/EldersReach_Z1_BirchlightVale_Vista.jpg)

![Z2 Skeldfjord](art/zones/EldersReach_Z2_Skeldfjord_Vista.jpg)

![Z3 TheSylwood](art/zones/EldersReach_Z3_TheSylwood_Vista.jpg)

![Z4 Barrowfell](art/zones/EldersReach_Z4_Barrowfell_Vista.jpg)

### Shots: Cessation

![Run OssuarySigma](art/zones/Cessation_Run_OssuarySigma_Vista.jpg)

![Run TheDisassembly](art/zones/Cessation_Run_TheDisassembly_Vista.jpg)

![Run TheIdleWorks](art/zones/Cessation_Run_TheIdleWorks_Vista.jpg)

![Run TheStirringWell](art/zones/Cessation_Run_TheStirringWell_Vista.jpg)

![Terminus](art/zones/Cessation_Terminus_Vista.jpg)

![Z1 ThePalefields](art/zones/Cessation_Z1_ThePalefields_Vista.jpg)

![Z2 TheScrapline](art/zones/Cessation_Z2_TheScrapline_Vista.jpg)

![Z3 TheNeedlegarden](art/zones/Cessation_Z3_TheNeedlegarden_Vista.jpg)

![Z4 NoctCaldera](art/zones/Cessation_Z4_NoctCaldera_Vista.jpg)

### Shots: The Scar

![Run TheRuptureworks](art/zones/TheScar_Run_TheRuptureworks_Vista.jpg)

![The Scar](art/zones/TheScar_Vista.jpg)

### Shots: Sunderpeak (hidden)

![Kagero](art/zones/Sunderpeak_Kagero_Vista.jpg)

![Run Steamhollow](art/zones/Sunderpeak_Run_Steamhollow_Vista.jpg)

![Run TheLanternGallery](art/zones/Sunderpeak_Run_TheLanternGallery_Vista.jpg)

![Run TheListeningHouse](art/zones/Sunderpeak_Run_TheListeningHouse_Vista.jpg)

![Run TheQuietArchive](art/zones/Sunderpeak_Run_TheQuietArchive_Vista.jpg)

![Z1 TheEmberTerraces](art/zones/Sunderpeak_Z1_TheEmberTerraces_Vista.jpg)

![Z2 TheCaneSea](art/zones/Sunderpeak_Z2_TheCaneSea_Vista.jpg)

![Z3 BlackfallGorge](art/zones/Sunderpeak_Z3_BlackfallGorge_Vista.jpg)

![Z4 TheVeilpass](art/zones/Sunderpeak_Z4_TheVeilpass_Vista.jpg)

