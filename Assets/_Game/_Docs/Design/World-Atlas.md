# Krytical Nova — World Atlas

The owner's world map, first copied from the Figma file **Krytical Nova — World Atlas** (https://www.figma.com/design/JhbgViYlKpJEsj8IKTLz5k) on 15 Oct 2026. **Rewritten 25 Oct 2026 at the owner's word** so that every place can be painted from its words alone: for each Home City, field zone and Run, the vibe, a ready concept prompt, a palette, the tone, the sky from dawn to night, the lighting, shadows and fog, the hills and verticality, how the map is crossed, its trees and foliage, ground, buildings and walls, signs and billboards, water, rivers, bridges and docks, how it joins its neighbours, its creatures and its sound, each with the Synty prefab families that build it. The Figma file stays the source for the names and the painted art; the Lore Bible (`Lore-Bible.md`) is canon; map sizes and layouts in metres are in `Zone-Build-Kits.md`.

**Nothing here is built** except the starter town, **Concord** (Zone01, the neutral town of all factions), and first-pass blank layouts of every map. Anything marked *(proposal)* is mine for the owner to keep, rename or drop. Creatures named "in the game now" are the Zone01 roster (`../Plans/hostiles-plan.md`); the rest are marked **needs a model**.

## How the world fits together

**Where to find more.** This chapter is the doc `World-Atlas.md` (open it whole in the reader). Open any place below for its full breakdown: the vibe, a concept prompt, palette, sky from dawn to night, lighting and fog, hills, foliage, buildings, signs, water and bridges, creatures and the Synty assets. The docs beside it:

- `World-Atlas.md`: this atlas as one document, every place in full.
- `Zone-Build-Kits.md`: each map's size, its layout in metres, its sub-areas and the exact Synty prefabs per sub-area.
- `Lore-Bible.md`: the story canon every place follows (Lore Bible section 14 names the places).
- `../Plans/hostiles-plan.md`: the Hostiles, their roster and the level bands.
- `../Plans/sky-weather-system-plan.md`: the one-hour day and the weather list every sky here uses.
- `Rendering-Bible.md`: the comic look every picture should be painted in.

![World spider map: every Home City, zone and Run](art/world-spider-map.jpg)

Each faction has a **Home City** and four field zones in a line; the fourth zone's gate opens onto **The Scar**, where all four lands meet. Every field zone has a **Run** (dungeon), growing with the band: 1 room, 2 rooms, 2 floors, 3 floors; the Ruptureworks has 4. Ronin maps ship with the expansion and are not visible at launch.

**The levels (owner, 25 Oct 2026).** A Home City is not neutral ground from wall to edge: the **city inside its walls** has no Hostiles and no fighting, but the **land outside its walls, on the same map**, is where every character of that faction starts levelling. So the levels start at the Home City and the last zone reaches 100:

| Map | Levels | Hostile species (one Hostile Zone each, 3 levels apart) | Bruisers | Run (levels) |
| --- | --- | --- | --- | --- |
| Home City outskirts | 1–20 | 1, 4, 7, 10, 13, 16, 19 | 12, 20 | none |
| Z1 | 18–40 | 22, 25, 28, 31, 34, 37, 40 | 30, 40 | 1 room (32–40) |
| Z2 | 38–60 | 42, 45, 48, 51, 54, 57, 60 | 50, 60 | 2 rooms (52–60) |
| Z3 | 58–80 | 62, 65, 68, 71, 74, 77, 80 | 70, 80 | 2 floors (72–80) |
| Z4 (Scar gate) | 78–100 | 82, 85, 88, 91, 94, 97, 100 | 90, 100 | 3 floors (92–100) |
| The Scar | 95–108 | 97, 100, 103, 106 | 102, 108 | — |
| The Ruptureworks | 105–112 | 106, 109, 112 | 112 | 4 floors |

Each band overlaps the next by two levels, so a player is never stuck between maps. The player cap stays 112. Concord, the neutral town of all factions (Zone01), keeps its own outskirts at the same starter levels.

**Who may fight whom.** Inside a Home City's walls: no Hostiles, no PvP, Callouts only. Outside its walls the outskirts follow the faction-war rule of every field zone (other factions are hostile while cross-faction PvP is on) *(proposal: the owner's "it's not a neutral zone")*. The Scar is always-on PvP.

| Faction | Region | Home City (outskirts 1–20) | Z1 18–40 | Z2 38–60 | Z3 58–80 | Z4 78–100 (Scar gate) |
| --- | --- | --- | --- | --- | --- | --- |
| Hegemony | Meridian Sprawl | New Meridian | Transit Row (Run: Substation 9) | Crestfall (Run: The Galleria) | Alder County (Run: Silo 12) | Gantry Ridge (Run: The Coreworks) |
| Covenant | Elders-Reach | Aldhavn | Birchlight Vale (Run: Hearthbarrow) | Skeldfjord (Run: Wrecktide Grotto) | The Sylwood (Run: The Rootvault) | Barrowfell (Run: Howlstone Crypts) |
| Null | Cessation | Terminus | The Palefields (Run: The Idle Works) | The Scrapline (Run: The Disassembly) | The Needlegarden (Run: Ossuary Sigma) | Noct Caldera (Run: The Stirring Well) |
| Ronin (hidden) | Sunderpeak | Kagero | The Ember Terraces (Run: Steamhollow) | The Cane Sea (Run: The Listening House) | Blackfall Gorge (Run: The Lantern Gallery) | The Veilpass (Run: The Quiet Archive) |

The Scar (Lv 95–108, always-on PvP) holds the endgame Run, **The Ruptureworks** (Lv 105–112, 4 floors). **Siege Night**, the hourly war, is fought on five small battlefields of its own, the Siege Grounds (last chapter of this atlas).

**How every place is painted.** Synty POLYGON low-poly shapes with flat colour, seen through the game's comic pass: black ink outlines, two or three tone steps of shade, halftone in the shadows, no outlines on the sky (`Rendering-Bible.md`). Concept art should be painted the same way: bold silhouettes, flat colour fields, ink lines, a little halftone; never photoreal or painterly mush. **One game day is one real hour** (about 35 minutes of day, 5 of dusk, 15 of night, 5 of dawn), so every place is seen at every hour. Weather comes from the game's list: Clear, Few Clouds, Partly Cloudy, Mostly Cloudy, Overcast, Fog, Light Rain, Rain, Thunderstorm, Snow, Blizzard. A person is 1.8 m, trees are their real height, grand things are grand on purpose; a field zone is 2 km square and takes about five minutes to run across.

**Each region keeps one feeling from city to Scar, growing harder outward:** Meridian Sprawl from gleaming to ruined, Elders-Reach from meadow to snow, Cessation from bone-white to glass-black, Sunderpeak from steam to ice. Every field zone carries the marks of the other factions' raids: that is what makes it more than scenery.

**Creatures in the game now** (the Zone01 roster, names are proposals): Bonewalker (a walking skeleton), Blightborn (a shambling undead), Blight Reaver (a heavier undead that only fights back), Gargoyle (a stone-winged ambusher), Wraith (a floating spirit), Seraph Unit (a sleek female combat robot), Carapace Raider (an alien-armoured raider), War Frame (a heavy battle robot), and the Bruisers Hellion (a demon) and Colossus (a giant brute). A species is not tied to one level: the same model can stand in a Hostile Zone of any level. **Storm Sovereign** (the 18 m storm dragon) reads as a boss, the owner's own system: seen in the sky, not fought in a field zone. The pets (Emberwing, Drone Core, Tidecaller, Kitsune Spirit) and the Brambleback mount are the player's, but their wild kin can be seen where they fit.

## Meridian Sprawl

![Concept art: Meridian Sprawl](art/concept-meridian-sprawl.jpg)

**Hegemony.** A New York that runs on souls, fraying outward into war-torn upstate. Gleaming inside the walls; outside, the commuter belt is abandoned, the suburbs are silent and the countryside is cratered, all the way up to the strip-mined front at the Scar.

| Look and feel | |
| --- | --- |
| Tone | clinical, polished, confident inside the walls; outside, the suburbs are silent and the countryside is cratered. |
| Palette | Neon Cyan, Steel White, Deep Steel, Rust, Ash, and hazard yellow (taxis, wall bands, Marines). Cyan + steel inside the walls; rust and ash take over block by block the further you walk from New Meridian. |
| Light | Warm, clean late-morning light inside the walls; the further out, the dustier and lower the sun: Gantry Ridge is late afternoon under smoke. |
| Synty packs | Sci-Fi City · Cyber City · Sci-Fi Worlds (corp walls, gantries) · Maps Sci-Fi Outpost (military walls, floodlights) · Generic · Apocalypse Wasteland · Mech · Sci-Fi Space (Runs); nature from Meadow Forest, Alpine Mountain, Swamp Marshland |
| Run flavour | sterile tech gone wrong: emergency red light, coolant fog, polite automated voices in empty halls, doors that open for nobody. |

### Home City: New Meridian (city safe · outskirts Lv 1–20)

![Mockup: New Meridian, isometric](art/mockups/mockup-new-meridian-isometric.jpg)

![Mockup: New Meridian, street](art/mockups/mockup-new-meridian-street.jpg)

**The vibe.** The last gleaming arcology: a round walled city a kilometre across on dry ochre ground by the sea, clean streets, yellow taxis, yellow-armoured Marines, a monorail sliding past on its viaduct, and in the middle a 180 m lattice spire with a golden crown and a ring of soul-light that hums. Nobody inside looks doubtful. Step out of a gate and it changes in a hundred metres: a shanty town in sight of the wall, a solar farm, a broken highway viaduct, a raid crater, and the first rogue machines in the dust. You are born in the cleanest city in the world and level up in its shadow.

**Concept prompt.** *Aerial three-quarter view at late morning of a round walled sci-fi city about 1 km across on dry ochre hills beside a blue sea. A 30 m grey city wall with amber bands and a glowing cyan light strip, four gate towers. Inside, concentric ring roads, a monorail on a viaduct, glass-and-steel towers to the north, red-roofed low homes to the west, colourful market streets to the south-west, hangars and a landing pad to the east, a Marine barracks with yellow armoured vehicles. In the centre a 180 m steel lattice spire with a golden crown and a floating ring of cyan soul-light. Outside the south gate a shanty town of shacks and scrap beside rows of solar panels; to the south-east a broken highway viaduct ending mid-air over a crater; pylon lines marching north-east over the hills. Clean warm light inside, dusty haze outside. Synty POLYGON low-poly, bold black ink outlines, flat colour, light halftone, comic-book finish.*

**Palette.** Steel white `#E8ECEF` · deep steel `#3B4450` · neon cyan `#2EE6E6` · hazard yellow `#F2C230` · Spire gold `#E8C25A` · terracotta roof `#C4643A` · ochre dust `#C9A46A` · rust (outside) `#A4512B`.

**Tone.** Inside: confident, orderly, prosperous, a little too clean, like a city in an advert. Outside: the cost of that order. A new player should feel proud of the city and uneasy about everything just beyond its gates. Never dystopian-grey inside: it must look like a place worth defending.

**Sky, day to night.**

- *Dawn:* peach and pale gold behind the Spire, the soul-ring the brightest thing in the sky, the sea silver, streetlights switching off ring by ring.
- *Day:* a clear, slightly hazy cornflower blue, a few flat-bottomed cumulus; warm sun high in the south. Outside the walls the horizon goes ochre with dust.
- *Dusk:* orange and rose over the sea in the north-west; the towers' windows light up floor by floor; holo-signs in the market blocks come into their own (cyan, magenta, yellow).
- *Night:* deep navy with few stars (the city glow washes them out); the city a bowl of light under a faint amber dome; the Spire's ring and the wall's cyan strip outline the city from far away; the shanty outside lit only by fires and a few bulbs; searchlights on the gate towers.
- *Weather:* mostly Clear, Few Clouds and Partly Cloudy; Light Rain makes the streets mirror the neon; sea Fog rolls over the north-west coast at dawn; a Thunderstorm now and then with lightning striking the Spire (it takes it). No snow.

**Lighting, shadows and fog.**

- Inside: crisp warm sunlight, clean hard shadows of towers across the plazas, white-blue fill from glass; at night, every street lit, little true dark.
- Outside: a thin ochre dust haze (soft fade from about 400 m), thicker downwind of the shanty's cook-fires; the crater and viaduct in harsh light.
- Fog: sea fog only, white, along the coast and into the north-west outskirts at dawn.

**Hills and verticality.** The city sits on a low plateau above the sea; the land outside rolls in dry ochre hills 20–60 m high with buttes (`SM_Env_Butte_*`) to the east and ochre cliffs down to the sea in the north-west. The verticality is built: the Spire 180 m, towers 40–120 m in the north arc, the wall 30 m, the monorail at 12 m, the broken viaduct 15 m up and ending in the air.

**Navigation.** Inside: a perfect plan of rings and eight radial avenues; the Spire is always the compass. Outside: you leave by any of the four gates into the outskirts ring; the north gate leads on to Transit Row. The outskirts are a loop round the wall: the shanty and solar farm (south), the broken viaduct and crater (south-east), the pylon hills (north-east), the sea cliffs (north-west). Levels rise with distance from the wall, so a new player hunts in sight of the gate and walks outward as they grow.

**Map.** The map grows from 1.6 km to about **2.4 km square** *(proposal)* so the outskirts make a ring about 700 m deep round the 1 km city.

**Trees and foliage.**

- Inside: manicured street trees in steel planters (`SM_Env_Tree_*`, Sci-Fi City), trimmed hedges (`SM_Env_Hedge_*`), holographic trees in the Pylon Garden (`SM_Env_Hologram_Tree_*`, `SM_Env_Hologram_Cherry_Tree_*`, Cyber City) beside real meadow trees (`SM_Env_Tree_Meadow_*`), flower beds.
- Outside: dry yellow grass, scrub and tumbleweed on the ochre hills, a few dead street trees along the old roads (`SM_Env_TreeDead_*`), weeds through the asphalt; greener only by the sea.

**Ground, hills and rocks.** Inside: road and sidewalk tiles (`SM_Env_Road_*`, `SM_Env_Sidewalk_*`), plaza paving (`SM_Env_Ground_Tile_*`). Outside: dry ochre earth, cracked asphalt of old roads, rocks `SM_Env_Rock_*` (Sci-Fi Worlds), buttes `SM_Env_Butte_*` (Apocalypse Wasteland), ochre cliffs `SM_Env_Cliff_Rough_*` to the sea, sea stacks `SM_Env_Rock_Large_*` in the water.

**Buildings and walls.**

- *The wall:* a ring 30 m high, grey with amber bands and a cyan light strip, gate towers 40 m with floodlights (`SM_Bld_City_Wall_*`, `SM_Bld_City_Wall_Tower_*`, gates `SM_Bld_Corp_Wall_Gate_*`, `SM_Prop_Floodlight_*`).
- *The Core Spire:* a 180 m lattice mast with a golden crown and soul-light ring (`SM_Bld_Corp_Comms_Tower_*` at Colossal scale on `SM_Bld_Corp_Platform_*`); Chancellor, Herald, Nova Rank.
- *Highrise district (north):* glass-and-steel towers 40–120 m (`SM_Bld_Large_*`, `SM_Bld_Advanced_*`), skyline filler (`SM_Bld_Background_*`), rooftop antennas and dishes.
- *Exchange Concourse (east of the plaza):* bank and shop fronts (`SM_Bld_Bank_*`, `SM_Bld_Shop_Open_*`), the Vault door, Kiosks of market tables under lit covers, a food trailer with a holo-dragon sign; Broker and Kiosks.
- *Red-roof homes (west):* low 2–4 storey pod homes with terracotta roofs and small hedged yards (`SM_Bld_Pod_Hab_*`).
- *Market blocks (south-west):* colourful shopfronts, awnings, neon, vending machines, food trucks (Cyber City).
- *Industrial yards (east):* hangars, a landing pad with a dropship, containers, a loading crane.
- *Marine Barracks (south-east):* a walled compound, a row of six-wheeled APCs, a training lane, yellow flags; Instructor and Marshal.
- *The Pylon Garden (west of the plaza):* a park on a live soul conduit under a glass canopy.
- *Outside:* the shanty (shacks `SM_Building_Shack_*`, crate buildings, scrap piles), the solar farm in rows, the broken highway viaduct on supports ending mid-air (`SM_Env_Highway_*`, `SM_Bld_Bridge_Supports_*`), a 60 m raid crater, pylon lines (`SM_Prop_Powerline_*`), a Marine outpost at each gate with barricades.

**Signs and billboards.** Inside: clean corporate signage, cyan wayfinding arrows, monorail station boards, holo-adverts on the towers (*SOUL-SAFE POWER FOR EVERY HOME*, *ENLIST: THE WALL NEEDS YOU*), the creed lettered over the south gate (*NECESSITY IS CIVILIZATION*, "the Gate of Necessity"), hazard-yellow bands, Hegemony crests on banners. Outside: hand-painted shanty signs, a faded *NO ENTRY WITHOUT PERMIT* plate at the gate, a dead billboard frame on the viaduct, spray-painted tallies on the scrap, a Covenant rune scrawled on the crater's edge where the raid came.

**Water, rivers, bridges and docks.** The sea along the north-west, blue-green, sea stacks in the surf, a concrete seawall and a small harbour of grey patrol boats and a fuel jetty *(proposal)*; a storm-drain outfall into the sea; no river inside the walls, only fountains in the plazas. The monorail viaduct and the broken highway are the only bridges.

**Connections.** The north gate opens onto the road to Transit Row (the commuter belt). The other three gates open only onto the outskirts. The pylon lines lead north-east toward Transit Row too.

**Creatures (outskirts, Lv 1–20).** *In the game now:* Seraph Unit and War Frame (rogue maintenance and security machines, near the viaduct and pad), Bonewalker and Blightborn (Null scouts' raised dead creeping in from the east, the first sign of the war), Gargoyle on the viaduct, Colossus as the far Bruiser at the crater. *Needs a model:* scavenger drones, rad-hounds (dogs with cyan-glowing implants), shanty scavengers. Gulls on the coast, crows on the pylons. Inside the walls: Marines, civilians, hover traffic, drones overhead.

**Sound.** City hum, monorail whoosh, PA chimes, distant surf; outside, wind, buzzing pylons and a far dog.

**Points of interest.** The Core Spire · Exchange Concourse · Marine Barracks · The Pylon Garden · *The Gate of Necessity* · *Monorail Station Meridian-Central* · *The Shanty* · *Landing Pad One* · *The Broken Viaduct* (outskirts vista) *(proposals in italics)*.

### Z1: Transit Row (Lv 18–40)

**The vibe.** The commuter belt right outside the city, on the morning after a world ended: an elevated rail line toppled across six-lane streets, two hundred stalled cars, holo-billboards still selling soap to nobody, weeds splitting the asphalt, and behind it all the gold-and-cyan Spire of the city, humming. Abandoned, not destroyed: the city left this place.

**Concept prompt.** *Wide establishing shot, low camera at street level looking north along a dead six-lane boulevard at late morning. A concrete monorail viaduct has collapsed diagonally across the street, one span tipped to the ground as a ramp. Stalled yellow taxis and buses fill the lanes, dust on their roofs. Gutted 8–12 storey glass-and-steel commuter blocks on both sides, windows blown out, one with a flickering cyan holo-billboard reading NECESSITY IS CIVILIZATION. Dead street trees in steel planters, waist-high weeds in the cracks. Far behind, over the rooftops, a gleaming lattice spire with a golden crown and a ring of soft cyan light. Ochre dust haze, warm sun from the right, long blue shadows. Synty POLYGON low-poly, bold black ink outlines, flat colour, light halftone, comic-book finish.*

**Palette.** Concrete grey `#8A8F95` · ochre dust `#C9A46A` · hazard yellow `#F2C230` · neon cyan `#2EE6E6` · rust `#A4512B` · asphalt `#3A3D42` · Spire gold `#E8C25A`.

**Tone.** Melancholy with civic pride still flickering on the billboards. Never a wasteland of mutants and spikes: the Hegemony is alive and well a few kilometres away, and that is the point. Safe-ish near the south road, less safe with every block north.

**Sky, day to night.**

- *Dawn:* pale peach low in the east, the Spire's ring still the brightest thing in the sky to the south; streetlights switching off along the ring road.
- *Day:* hazy cornflower blue, washed out at the horizon by ochre dust; flat-bottomed cumulus; warm, slightly yellow sun.
- *Dusk:* orange and rose behind the toppled rail; the billboards glow cyan, magenta and yellow on the asphalt; the blocks go flat black against the sky.
- *Night:* deep navy, very few stars; the city's amber glow on the southern horizon; billboards and a few working streetlights the only light, long pools of dark between.
- *Weather:* Clear, Few Clouds, Partly Cloudy; Light Rain turns the street into a mirror of neon; Fog off the river at dawn. No snow.

**Lighting, shadows and fog.** High warm sun, hard shadows under the viaduct and between the blocks: the streets are striped light and dark. A thin ochre dust haze over the map (about 300 m soft fade), grey smoke over the Burning Depot. White river fog knee- to head-high along the river and the island at dawn. At night all light is artificial: cyan, magenta, amber signs and the white floodlights of Checkpoint Delta.

**Hills and verticality.** Flat city ground on the south two-thirds, rising to low ochre hills (20–40 m) on the east and west edges. Height comes from things: the toppled overpass is a 7 m ramp, the commuter-block shells are 8–12 storeys with walkable roofs and Wallstride walls, the north island sits 3–4 m higher on a concrete embankment.

**Navigation.** A ring road you follow anticlockwise, the Spire behind you always marking south. Open lanes but cluttered: cars are low cover and crawl-over routes (*Lane 4 Jam*). One real chokepoint, the Fallen Overpass, the only dry way to the island. Rooftop shortcuts for players who learn Wallstride.

**Trees and foliage.** Almost all dead or dying: grey bare street trees in steel planters every 15 m (`SM_Env_TreeDead_*`), weeds and tall grass in every crack and kerb (`SM_Env_Grass_Tall_Clump_*`), dandelion-yellow flower clumps, ivy up the lower floors of the shells. On the island and riverbank: reeds (`SM_Env_Reeds_*`), scrubby living trees, green coming back. The further from the city, the greener.

**Ground, hills and rocks.** Asphalt and sidewalk tiles (`SM_Env_Road_*`, `SM_Env_Sidewalk_*`) cracked and patched with weeds, rubble (`SM_Prop_Sidewalk_Rubble_*`, `SM_Prop_RubbishPile_*`), highway rubble under the overpass (`SM_Env_Highway_Rubble_*`), ochre earth and rocks on the edge hills.

**Buildings and walls.**

- *Commuter blocks (west):* glass-and-steel shells, curtain walls broken, floor slabs and rebar showing, one corner sheared off (`SM_Bld_Large_*`, `SM_Bld_Section_*`, `SM_Bld_Base_Wall_Destroyed_*`).
- *The Fallen Overpass:* concrete highway on round pillars, one span tipped 20° to the ground, fallen monorail track across it (`SM_Env_Highway_Straight_Long_*`, `SM_Bld_Bridge_Supports_*`, `SM_Bld_Train_Rail_*`).
- *Checkpoint Delta (centre):* a prefab military compound: grey blast walls, chain-link, a gate boom, floodlight masts, containers, an APC (`SM_Bld_Military_Wall_*`, `SM_Prop_Barricade_*`, `SM_Prop_Floodlight_*`, `SM_Prop_Container_*`, `SM_Veh_Apc_*`); the Wayfinder.
- *The Burning Depot (east):* a corrugated bus depot with its roof fallen in, burned buses in rows, an armoured truck burned out, a tyre pile on fire (`SM_Bld_Warehouse_*`, `FX_Fire_*`, `SM_Prop_Tyre_Pile_*`).
- *Platform 9 (north island):* a sunken metro entrance under a curved canopy, emergency red light bars in the stairwell, security cameras (`SM_Bld_Underpass_*`).
- *Vehicles everywhere:* stalled taxis and classic cars (`SM_Veh_Future_Taxi_*`, `SM_Veh_Classic_*`), buses, a garbage truck.

**Signs and billboards.** Billboard Row (north-west): six big holo and printed billboards, three still flickering (`SM_Sign_Billboard_Large_*`, `SM_Prop_Billboard_Damaged_Sign_*`, `SM_Prop_Sign_Billboard_Broken_*`): *NECESSITY IS CIVILIZATION*, *SOUL-SAFE POWER FOR EVERY HOME*, *ENLIST: THE WALL NEEDS YOU*, an ad for a breakfast drink. Lane markings, bus-stop timetables, a lit *9* over Platform 9, yellow-black chevrons on barriers, Marine stencil numbers on Checkpoint Delta, and one Covenant rune painted on the Burning Depot's door: the war reached here.

**Water, rivers, bridges and docks.** A river 80 m wide crosses the north of the map west to east, slow and grey-green, concrete embankments on the south bank, reeds and mud on the island, a sunken car roof in the shallows, storm drains spilling into it; at night it mirrors the billboards. The Fallen Overpass is the crossing; an old concrete boat ramp near it; no docks.

**Connections.** South: the road from New Meridian's north gate. North: across the Fallen Overpass to the island, where a ring of pillars holds the portal to Crestfall. Platform 9 drops under the island into Substation 9.

**Creatures.** *In the game now:* Seraph Unit and War Frame (rogue Hegemony machines), Gargoyle on the shells' roofs, Hellion as the Burning Depot's Bruiser, Colossus on the island. *Needs a model:* scavenger drones, feral maintenance bots, rad-hounds. Crows on the dead trees and billboard frames, pigeons off the overpass. Marines in hazard yellow at Checkpoint Delta.

**Sound.** Wind through empty windows; the buzz and click of a failing billboard; a far alarm that never stops; the city's hum to the south.

**Points of interest.** The Fallen Overpass · Platform 9 (Substation entrance) · Checkpoint Delta (Wayfinder) · The Burning Depot (Bruiser) · *Billboard Row* · *Lane 4 Jam* · *The Last Commuter* (a skeleton still seated at a bus stop).

### Run: Substation 9 (Lv 32–40 · 1 room)

**The vibe.** A flooded metro power substation under the river: shin-deep black water, transformer towers arcing blue-white light across the ceiling, and maintenance drones still walking the routes they were given before everyone left. A polite PA voice keeps apologising for the delay.

**Concept prompt.** *Interior, wide shot from a catwalk 4 m up across a 60 × 40 m underground concrete hall with a 14 m ceiling. Shin-deep still black water covers the floor, reflecting arcs of electricity jumping between three transformer towers in a central pit. Emergency red strip lights along the walls, cyan status lights on the machines, pipes and cable trays across the ceiling dripping water. Small boxy maintenance drones on rails along the walls with single cyan eyes. At the far end a huge main breaker panel with a lever under a sign MAIN BREAKER – AUTHORISED ONLY. Synty POLYGON low-poly, bold ink outlines, comic shading, dark with hard red and blue light.*

**Palette.** Wet concrete `#4B5057` · emergency red `#D8343A` · arc blue-white `#BFE9FF` · black water `#111820` · hazard yellow `#F2C230`.

**Layout.** One room: entry stairs down from Platform 9 → a catwalk ring 4 m up round the transformer pit → the boss floor under the main breaker.

**Lighting, shadows and fog.** No daylight. Red emergency strips and the arcs: each arc lights the hall white for an instant and throws hard shadows. Fine spray and a low cold mist at water level.

**Structures and assets.** Sci-Fi Space mechanical walls and double-arch corridors (`SM_Bld_Wall_Mechanical_*`, `SM_Bld_Corridor_Double_Arch_*`), detail pipes (`SM_Prop_Detail_Pipe_*`), Cyber City big pipes (`SM_Bld_Pipe_Large_*`), transformers (`SM_Bld_Power_*`) with `FX_Electricity_*`, a water plane at 0.6 m, drones (`SM_Prop_Drone_*`) on patrol rails.

**Signs.** *MAIN BREAKER – AUTHORISED ONLY*, *HIGH VOLTAGE*, numbered transformer plates, a PA voice: "Maintenance is in progress. Thank you for your patience."

**Creatures.** *In the game now:* Seraph Unit (substation security). *Needs a model:* maintenance drones, a cable-crawler (a spider of conduits); the boss, the Breaker Warden *(proposal)*: a big repair robot fused to the main breaker.

### Z2: Crestfall (Lv 38–60)

**The vibe.** The dead suburbs: cul-de-sacs of identical two-storey houses swallowed by waist-high meadow, birches taller than the roofs, a husk of a shopping mall with its sign still lit, swings moving in the wind, a water tower over everything. It is the silence that gets you.

**Concept prompt.** *Wide shot from a hilltop at late afternoon over a suburban grid of identical two-storey houses with pale siding and grey pitched roofs, front lawns gone to waist-high golden meadow grass and wildflowers, birch and maple trees grown taller than the roofs. A dry fountain at a cul-de-sac centre, a child's swing set, a family car still packed for a trip. In the middle distance a huge flat-roofed shopping mall with a glass atrium and a half-lit sign reading CRESTFALL GALLERIA over an empty parking lot of dusty SUVs. A tall steel water tower with a peeled Hegemony logo. Beyond, a highway interchange loops over everything. A raid crater has punched through one street. Warm low sun, long shadows, soft haze, crows. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Meadow gold `#CDB25C` · siding cream `#E8E1CF` · roof grey `#6E7177` · wild lawn green `#8AA84B` · birch white `#EDEAE0` · mall-sign magenta `#D9418C` · faded Hegemony cyan `#7CC9CC`.

**Tone.** Eerie and nostalgic, very quiet: the comfort of the Hegemony's promise left out in the rain. The player is a trespasser in someone's life. Never horror: no blood, no bodies in the street; the people simply left.

**Sky, day to night.**

- *Dawn:* soft grey-pink, mist in the reservoir hollow, the water tower a silhouette.
- *Day:* a big pale-blue suburban sky with lazy cumulus and their shadows sliding over the roofs: the most ordinary sky in the game, which is the point.
- *Dusk:* long amber; every window glints; the Galleria's sign flickers on, magenta.
- *Night:* dark and quiet, many stars (no city glow out here), one or two porch lights still on solar, the Galleria sign and the water tower's red aviation light.
- *Weather:* Partly and Mostly Cloudy; Light Rain and Rain (wet roads, gutters running); a Thunderstorm with lightning on the water tower; Fog on the reservoir.

**Lighting, shadows and fog.** Warm, softer and lower sun than Transit Row; dappled shadows under the yard trees; blue-grey shade; a light warm haze; fog only round the reservoir and in the hollows at dawn.

**Hills and verticality.** A gently rolling suburb on low hills (30–60 m), the grid bending over them; the reservoir in a hollow; the interchange on pillars with decks at 8 and 16 m. Climbable: the water tower (45 m), the mall roof (18 m), house roofs.

**Navigation.** A grid that all looks alike, so the landmarks matter: the water tower, the Galleria sign, the interchange. Streets are open; back yards are a maze of fences and hedges, good for ambushes. The interchange is the chokepoint onward.

**Trees and foliage.** Birch (`SM_Env_Tree_Birch_*`, 18 m) and maple (`SM_Env_Tree_Meadow_*`, 20 m) over the roofs, overgrown hedges, waist-high grass and wildflowers on every lawn (`SM_Env_Grass_Tall_Clump_*`, `SM_Env_Wildflowers_*`), ivy on the walls, moss on the roofs, reeds round the reservoir (`SM_Env_Reeds_*`).

**Ground, hills and rocks.** Cracked residential roads and driveways, parking lines (`SM_Env_Road_ParkingLines_*`), meadow lawns, a raid crater (`SM_Env_Crater_*`) through one street and the school gym, concrete reservoir rim.

**Buildings and walls.**

- *Elmwood grid (west):* two-storey houses from Generic modules (`SM_Bld_Base_*`, pitched roofs `SM_Bld_Base_Roof_*`), porches, picket fences (`SM_Prop_Meadow_Fence_*`), garden sheds.
- *Oakridge Cul-de-sac:* twelve houses with doors open, boxes spilled (`SM_Gen_Prop_Cardboard_Box_*`), one burned to the frame.
- *Crestfall Galleria (centre):* a 180 × 120 m mall husk with a glass atrium front (`SM_Bld_Base_Wall_Window_Double_*`), a car park full of SUVs and luxury cars, a dead pylon sign.
- *The Water Tower:* 45 m, steel, a ladder up (`SM_Bld_Watertank_*`).
- *The Schoolyard:* a school with a crater through the gym, swings.
- *The Interchange (north-east):* a 500 m concrete cloverleaf (`SM_Env_Highway_Corner_*`, `SM_Env_Highway_Wedge_*`).
- *Reservoir pump house:* the Wayfinder *(proposal)*.

**Signs and billboards.** Street names (*Elmwood Ave*, *Oakridge Ct*), stop and speed signs, a school sign with letters missing, *FOR SALE* signs in yards, a marquee board with stray letters, the Galleria's big sign and store signs, a peeled Hegemony logo on the water tower, looters' spray-painted X's on searched doors, a raid-siren pole on a corner.

**Water, rivers, bridges and docks.** The reservoir: a 300 m round lake in a concrete rim, still and dark blue, reeds in the shallow end, a pump house on a short pier, a broken rowing boat. A dry fountain in a cul-de-sac. Storm drains. No river. The interchange's decks are the only bridges.

**Connections.** South: the road from Transit Row's island portal. North-east: the interchange, the portal under its top loop to Alder County. The Galleria's doors lead into its Run.

**Creatures.** *In the game now:* War Frame (Hegemony security still on patrol), Seraph Unit in the car park, Gargoyle on the school roof. *Needs a model:* feral house-bots (domestic robots in aprons and caps, gone wrong), mutated hounds, looter gangs in scrap armour. Deer on the meadow lawns, crows, a stray dog that runs away.

**Sound.** Wind in tall grass; a swing set creaking; a looping PA from the mall; dogs far off.

**Points of interest.** Crestfall Galleria (Run entrance) · The Water Tower (vista) · Oakridge Cul-de-sac (Contract loot) · The Interchange (Bruiser) · *The Schoolyard* · *Mile Marker 9* · *The Reservoir Pumphouse* (Wayfinder).

### Run: The Galleria (Lv 52–60 · 2 rooms)

**The vibe.** A shopping mall where the muzak never stopped: a three-storey atrium of dead escalators under a cracked skylight, mannequins that turn their heads, a food court with trays still on the tables, and a loading dock out back where something big is unloading.

**Concept prompt.** *Interior of a three-storey shopping mall atrium, 50 × 40 m, 18 m high, a cracked glass skylight letting in shafts of dusty light. Stopped escalators crossing between galleries, store fronts with dark windows and neon signs, potted plants grown wild and dead. Mannequin-robots posed along the balconies, one turning its head. Pastel colours gone dingy. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Mall teal `#7FC3B8` · pastel pink `#E8A3B5` · floor-tile cream `#E4DCC8` · neon magenta `#D9418C` · shadow `#2A2C33`.

**Layout.** Two rooms: the Atrium (three galleries, 50 × 40 m) → the Food Court and Loading Dock (two linked halls, 40 × 30 m each).

**Lighting, shadows and fog.** Dusty shafts from the skylight, flickering store neon; at the dock, grey daylight through a half-open roller door and turning orange hazard beacons. Dust in the air.

**Structures and assets.** Cyber City modular interior (`SM_Bld_Base_*`), stopped escalators from `SM_Bld_Stairs_*`, food shelves and vending machines, small tables, posed robot mannequins (`SM_Prop_Robot_Posed_*`), the dock's garage door (`SM_Bld_Door_Garage_*`) and trucks.

**Signs.** Store names in neon, a *FOOD COURT* sign with a dead letter, a mall directory map *YOU ARE HERE*, *STAFF ONLY* at the dock.

**Creatures.** *In the game now:* Seraph Unit (mall security). *Needs a model:* mannequin-bots; the boss, the Floor Manager *(proposal)*: a big service robot with a name badge, or a loading-dock lifter mech.

### Z3: Alder County (Lv 58–80)

**The vibe.** Upstate farm country in a patchwork of gold and green, red barns and a covered bridge, apple orchards in rows, and marching across it all a line of soul pylons that hum and glow blue at night. Automated harvesters still cut fields nobody eats from. Then the craters, and a burned farmstead with a Covenant rune on its barn door.

**Concept prompt.** *Wide pastoral shot from a hill at golden hour over rolling farmland: a patchwork of golden wheat, green corn and dark apple orchards in neat rows, hedgerows between, red barns with white trim, round hay bales, a windmill. A slow river with a red covered bridge. Marching diagonally across the scene, a line of tall steel lattice soul-pylons 30 m high joined by cables, glowing faintly cyan, leading to a fortified grey military fort on a ridge with floodlights. A big autonomous harvester working a field alone. Two smoking craters and a burned farmstead in a corner. Warm low sun, long shadows, crows. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Wheat gold `#E1B84B` · corn green `#7AA64A` · barn red `#A8322C` · orchard green `#3F6B36` · sky blue `#8CC3E8` · soul cyan `#3FD9E6` · scorch black `#2A2422`.

**Tone.** Bountiful and hollow: the most beautiful farm country in the world, and nobody eats from it. Americana gone quiet with the pylons humming under everything. A front line: the craters and the fort say the war is close.

**Sky, day to night.**

- *Dawn:* gold mist in the low fields, the pylons standing out of it in a line.
- *Day:* huge clear blue with tall summer cumulus; rich warm light.
- *Dusk:* orange and pink over the fields, the harvester's work lights coming on, the pylon line starting to glow.
- *Night:* deep blue with stars, Pylon Junction 4 lighting the sky soul-blue like a dome of cold light over the north-east; the pylons a string of cyan beads across the dark.
- *Weather:* Clear and Partly Cloudy; summer Thunderstorms with lightning striking the pylons; Rain; Fog in the river valley at dawn.

**Lighting, shadows and fog.** Warm, low, golden; long shadows of hedgerows and pylons across the fields; soft valley fog at dawn; grey smoke drifting from the craters.

**Hills and verticality.** Rolling hills 40–80 m, the river in a shallow valley, Fort Alder on the north ridge. Height from the pylons (30 m), the windmill, Silo 12 (50 m) and the barns.

**Navigation.** Open and easy, the pylon line the leading line: follow it to Fort Alder and the Silo. Hedgerows and orchards break the sight lines; the covered bridge is the river crossing and a chokepoint, a ford downstream.

**Trees and foliage.** Apple orchards in rows 6 m apart (`SM_Env_Tree_Fruit_*`, 5 m), alder and maple along the river and lanes, hedgerows (`SM_Env_Bush_*`), wheat and corn (`SM_Env_CropField_Clump_*`), a sunflower field (`SM_Env_Sunflower_*`), verge wildflowers; burned stumps and black ground round the craters.

**Ground, hills and rocks.** Tilled fields in strips, dirt farm tracks, grassy hills, a few limestone outcrops (`SM_Env_Rock_Cliff_*`, Meadow Forest), craters (`SM_Env_Crater_*`).

**Buildings and walls.**

- *Farmsteads (three):* red barns, white farmhouses, stone cabins (`SM_Bld_Stone_Cabin_*`), steel silos (`SM_Bld_Pod_Silo_*`), Outpost fences (`SM_Bld_Fence_*`), scarecrows (`SM_Prop_ScareCrow_*`), a windmill (`SM_Bld_Windmill_*`). One burned (Harrow Farm).
- *The pylon line:* a pylon every 120 m for 1.6 km (`SM_Prop_Powerline_*`, `SM_Prop_Powerline_Antenna_*`); Pylon Junction 4, a generator yard ringed by pylons (`SM_Bld_Corp_Generator_*`, `SM_Bld_Power_*`), lit soul-blue.
- *Silo 12:* three 50 m refinery silos with a harvester parked at the door (`SM_Bld_Scav_Refinery_Silo_*`, `SM_Veh_Scav_Lifter_*`).
- *Fort Alder:* a 150 m Hegemony forward base: corp walls, towers, barracks, floodlights, a hover tank (`SM_Bld_Corp_Wall_*`, `SM_Bld_Corp_Barracks_*`, `SM_Veh_HoverTank_*`); the Wayfinder.

**Signs and billboards.** *ALDER COUNTY* road sign, farm names (*Harrow Farm*, *Greaves & Sons*), a hand-painted *FRESH APPLES* board, a Hegemony billboard *THE COUNTY FEEDS THE CITY*, pylon plates *SOUL CONDUIT – DO NOT CLIMB*, Fort Alder's stencils, and a Covenant rune painted red on Harrow Farm's barn door.

**Water, rivers, bridges and docks.** A river 60 m wide, slow and green-brown, with lilies (`SM_Env_Lillies_*`); the red covered bridge (`SM_Prop_Bridge_*`, roofed); a mill pond; irrigation ditches; a cattle pond; a small timber landing with a rowing boat.

**Connections.** South: from Crestfall's interchange portal (the highway ends and becomes a county road). North: the portal by Fort Alder to Gantry Ridge. Silo 12 into its Run.

**Creatures.** *In the game now:* War Frame (Fort Alder's lost patrols), Seraph Unit (harvester escorts), Blightborn as field ghouls *(stand-in)*. *Needs a model:* harvester-bots turned guard, field ghouls (scarecrow-dead), Covenant raider bands. Crows, a lone horse, cattle bones in a field, wild Bramblebacks in the orchards.

**Sound.** Harvester engines far off; wind in the corn; the pylon hum rising toward the junction.

**Points of interest.** Silo 12 (Run entrance) · Pylon Junction 4 · The Orchard Rows (dense Hostile ground) · Fort Alder (Wayfinder) · *The Covered Bridge* · *Harrow Farm* · *The Scarecrow Field* (Bruiser).

### Run: Silo 12 (Lv 72–80 · 2 floors)

**The vibe.** A grain silo that was never only for grain: a round gold-dusted grain deck forty metres across under catwalks, and below it a hidden munitions cache of crates and missiles, guarded by harvester-bots turned sentries.

**Concept prompt.** *Interior of a giant steel grain silo, round, 40 m across, 20 m tall, heaps of golden grain on the floor, dust in shafts of light from the top hatch, steel catwalks round the walls, a big auger. Through a floor hatch a lower grey military vault lit red with stacked crates and missiles. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Grain gold `#E2B85A` · steel `#7C8288` · dust light `#F6E7B8` · military grey `#4E5650` · alarm red `#D23A35`.

**Layout.** Two floors: the Grain Deck (round, 40 m) → the Munitions Cache (a 60 × 30 m vault).

**Lighting, shadows and fog.** Gold shafts from the top hatch and floating dust above; red alarm light and turning beacons below.

**Structures and assets.** Silo interior (`SM_Bld_Pod_Silo_*`), decks (`SM_Bld_Platform_*`), grain heaps (`SM_Env_Ground_Mound_*` in gold); military crates (`SM_Prop_Crate_Military_*`), missiles (`SM_Prop_Missile_*`), floor turrets (`SM_Prop_Turret_Small_Floor_*`).

**Signs.** *GRAIN – SILO 12*, then under it *RESTRICTED ORDNANCE*, stencilled crate codes.

**Creatures.** *In the game now:* War Frame. *Needs a model:* harvester-bot sentries; the boss, the Quartermaster unit *(proposal)*.

### Z4: Gantry Ridge (Lv 78–100 · Scar gate)

**The vibe.** Strip-mined highlands and the war front: a ridge cut into terraces by colossal extraction gantries, a drill as tall as a tower biting into the summit, a trench line across the middle with wrecked APCs and a downed dropship in no-man's-land, and at the top a floodlit fortress guarding the Scar Gate, snow on the peaks and violet light beyond. The city's hunger laid bare.

**Concept prompt.** *Wide shot from the lower switchback road looking up a strip-mined mountain ridge cut into giant terraces. A colossal lattice drill-gantry, 90 m tall, bites into the upper slope, cyan soul-light leaking from the bore. Across the middle a trench line with sandbags, tank traps and burning wrecked APCs, a crashed dropship nose-down in the mud. Scorched pine stumps on the slopes, live pines lower down, snow on the summit. At the top a fortified grey military wall with searchlights sweeping and a violet glow rising behind it. Smoke, late-afternoon sun low and orange through smoke. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Rock grey `#6F6E6A` · cut-earth ochre `#B8834A` · smoke grey `#8A8580` · searchlight white `#F5F3E6` · soul cyan `#3FD9E6` · Rupture violet `#9B5BE0` · snow `#EEF1F4` · fire orange `#E86A2C`.

**Tone.** Industrial war: heavy, loud and grim, but heroic, a front line. The violet glow at the top is a promise and a threat.

**Sky, day to night.**

- *Day:* always late-afternoon light, low and orange through drifting smoke; above the smoke, clean cold air on the upper ridge.
- *Dusk:* a red sun behind smoke; the drill's cyan glow and the first searchlights.
- *Night:* searchlights sweeping the sky and slopes, the drill glowing, artillery flashes far off, and over the summit the violet aurora of the Scar.
- *Weather:* Overcast with smoke, Rain below and Snow on the upper ridge (the line between them visible), Thunderstorm, Fog in the trenches, a Blizzard at the summit sometimes.

**Lighting, shadows and fog.** Low, orange, smoky light; hard shadows of the gantries; dark trenches; smoke banks across the middle; clean cold light above the snowline.

**Hills and verticality.** A big climb from 40 m at the entry to 520 m at the Scar Gate; terraces 30 m apart in height and 60 m deep; the drill 90 m. Long vistas downhill.

**Navigation.** Switchbacks: a road zig-zags up the terraces; shortcuts climb terrace faces by ladders and conveyor ramps. The trench line is a band to cross under fire, with chokepoints at its gaps; the summit fortress is the funnel to the gate.

**Trees and foliage.** Pines on the lower slopes (`SM_Env_Pine_*`, 25–35 m), scorched pine stumps in the middle (`SM_Env_Pine_Stump_*`), bare rock and snow above, tough grass and alpine flowers in the untouched corners.

**Ground, hills and rocks.** Cut terrace faces (`SM_Env_Cliff_Flat_*`), dirt switchback road (`SM_Env_DirtRoad_*`), cliffs (`SM_Env_Rock_Cliff_*`), mud and craters at the trench, snow mounds and ice sheets on the upper ridge (`SM_Env_Snow_Mound_*`, `SM_Env_Ice_Sheet_*`).

**Buildings and walls.**

- *Extraction gantries:* mining drills, laser bridges and conveyors (`SM_Prop_Drill_*`, `SM_Bld_Bridge_Mining_Laser_*`, `SM_Bld_Scav_Refinery_Pipe_*`).
- *Extraction Gantry Alpha:* a 90 m drill over glowing soul-core (`SM_Bld_Planetary_Cannon_*`, `SM_Env_Core_*`), catwalks.
- *The Trench Line:* 1.4 km of 3 m trench, cover walls, barricades, tank traps, wrecked APCs, craters (`SM_Bld_Military_Wall_Cover_*`, `SM_Prop_Tank_Trap_*`).
- *Crash Site:* a dropship nose-down with debris and smoke (`SM_Veh_DropShip_*`).
- *Mech Graveyard:* two fallen war-frames, one kneeling as a climbable vista (`SM_Veh_Mech_*`).
- *Switchback 7:* a mining office on the bend, the Wayfinder.
- *The Scar Gate:* a 200 m fortress of corp walls, corner towers, searchlights and floodlights, the portal glowing violet in the gate (`SM_Bld_Corp_Wall_Gate_*`, `SM_Prop_Searchlight_*`).

**Signs and billboards.** Mining signage (*BLAST ZONE*, *LEVEL 4 BENCH*, terrace numbers), Hegemony military stencils, unit insignia on the wrecks, a big painted *HOLD THE RIDGE* on a wall, kill tallies, red warning lights.

**Water, rivers, bridges and docks.** Meltwater streams from the snowline running down the terrace faces in thin falls, a muddy settling pond, frozen pools near the top. The gantry catwalks and conveyor bridges are the bridges.

**Connections.** South: from Alder County's portal by Fort Alder. Top: the Scar Gate, into the west end of The Scar. The Coreworks inside the bore.

**Creatures.** *In the game now:* War Frame (the front's machines), Colossus, Seraph Unit. *Needs a model:* heavier war-frames, trench revenants (dead soldiers in gas masks), frost hounds. Ravens, mountain goats on the untouched slopes.

**Sound.** The drill's grind; wind; distant artillery; searchlight motors.

**Points of interest.** Extraction Gantry Alpha · The Trench Line (Bruiser) · Crash Site · The Scar Gate · *The Mech Graveyard* · *Switchback 7* (Wayfinder) · *The Last Floodlight* (vista over the Scar).

### Run: The Coreworks (Lv 92–100 · 3 floors)

**The vibe.** The extraction bore itself: a processing deck of pipes and walkways, a vertical shaft circled by a ramp sixty metres down, and a domed core chamber where soul-light leaks from every seam.

**Concept prompt.** *Interior of a vast industrial bore, a 30 m wide vertical shaft with a spiral ramp down its wall, pipes and conduits glowing cyan soul-light from their seams, steam, gantries; at the bottom a domed chamber with a pulsing core. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Dark steel `#2F3338` · soul cyan `#3FD9E6` · warning amber `#F2A33A` · steam white `#E8EEF0`.

**Layout.** Three floors: Processing Deck (70 × 50 m) → the Bore Shaft (a 30 m drop, 60 m deep, round a ramp) → the Core Chamber (a 50 m dome).

**Lighting, shadows and fog.** Cyan soul-light from the seams, amber work lights, steam everywhere, brighter with depth.

**Structures and assets.** Core shells and struts (`SM_Env_Core_*`, `SM_Env_Core_Shell_*`, `SM_Env_Core_Strut_*`, `SM_Bld_Core_Vent_*`), walkways (`SM_Bld_Floor_Large_Walkway_*`), pipes, `FX_Core_*`.

**Creatures.** *In the game now:* War Frame, Seraph Unit. *Needs a model:* bore engineers turned husks; the boss, the Core Overseer *(proposal)*.

## Elders-Reach

![Concept art: Elders-Reach](art/concept-eldervales.jpg)

**Covenant.** Ethereal highlord vikings and elven houses in a northern European landscape scarred by raids: a hall-city on a fjord, golden birch meadows, a raw sea coast, elven old-growth, and snow moors of barrows climbing to the Scar.

| Look and feel | |
| --- | --- |
| Tone | defiant, communal, weathered. Oaths, kin, the land spoken of as a living thing. Beautiful, with burned villages in it. |
| Palette | Ember Gold, Forest Green, Snow, Slate, Oak. Gold firelight against green and slate. Snow creeps in by Barrowfell. |
| Light | Low northern sun, long gold light, quick weather: sun and showers in the Vale, sea mist on Skeldfjord, green shafts in the Sylwood, white wind on Barrowfell. |
| Synty packs | Viking Realm · Vikings · Elven Realm · Fantasy Kingdom · Fantasy Village · Knights (ruined keeps) · Dungeon and Dungeon Realms (Runs); nature from Meadow Forest, Enchanted Forest, Alpine Mountain, Tropical Jungle (wrecks, kelp), Swamp Marshland (jetties) |
| Run flavour | torchlit earth, roots and runes, the restless dead: sorrowful, never gross. |

### Home City: Aldhavn (city safe · outskirts Lv 1–20)

**The vibe.** A hall-city at the head of a fjord: black water and longships in the harbour, grass-roofed longhalls with carved dragon heads and smoke from every roof hole, elven spires with curved roofs on the cliff terraces above, waterfalls falling off the terraces into the fjord, and at the very top the Ancestor Grove, where souls once came home. One pact, one wall. Outside the landward wall, the first meadows and woods where young Covenant warriors learn to fight: pretty country, with the barrows already restless and Hegemony scouts in the trees.

**Concept prompt.** *Wide shot from the water at golden late afternoon looking up a Norwegian-style fjord: steep dark cliffs with thin white waterfalls, black water with Viking longships moored at timber docks. A tiered town climbs the cliff: at the water a harbour market of stalls and fish racks; in the middle long grass-roofed timber halls with carved dragon-head gables and smoke rising; above, on stone terraces, elegant elven houses with curved silver-blue roofs and climbing vines; at the very top a ring of softly glowing trees round standing stones. A stone wall with timber hoardings runs round the landward side. Outside the wall, golden meadows, birch woods and a burial mound. Synty POLYGON low-poly, bold ink outlines, flat colour, comic-book finish.*

**Palette.** Fjord black-blue `#1E2E3A` · cliff slate `#596570` · grass-roof green `#5E8A3A` · oak brown `#6B4A2E` · elven silver-blue `#A9C3D6` · ember gold `#E6A93A` · waterfall white `#EEF4F6`.

**Tone.** Proud, warm, communal: firelight, song, kin. Old and lived-in, never shiny. Inside the wall, the safest and most beautiful home in the north; outside, the reminder that it was raided once and can be again.

**Sky, day to night.**

- *Dawn:* mist lying on the fjord, the cliffs coming out of it gold at the top, horns from the hall ring.
- *Day:* a deep clear northern blue with big fast cumulus and cloud shadows racing across the cliffs; the sun low and gold even at noon.
- *Dusk:* long and slow, orange and rose on the water, every longhall's roof hole glowing, torches lit along the stairs.
- *Night:* clear and starry, a green aurora over the fells to the north, the harbour lit by braziers, the Ancestor Grove's trees glowing pale blue-white at the top of the town.
- *Weather:* Few Clouds and Partly Cloudy; quick Light Rain showers; Fog on the fjord at dawn; Snow in the coldest hour on the terraces only.

**Lighting, shadows and fog.** Low gold sun, long shadows of the cliffs across the water by afternoon (the harbour falls into shade first); warm torchlight and firelight in every doorway; cool lilac shade. Fjord mist at dawn, white and low, lifting by mid-morning; woodsmoke drifting from the hall ring.

**Hills and verticality.** Strongly tiered: harbour at sea level, the hall ring 60 m up, the elven terraces 110–140 m, the Ancestor Grove 160 m; stairs, a cargo crane and waterfalls between the tiers. Outside the landward wall the land rolls in meadows and low birch hills (20–80 m) toward the vale.

**Navigation.** Inside: up and down the tiers by the Oathstones stair, the curved elven stairs and the crane; the Pact Hall in the middle tier is the heart. Outside: the landward gate opens east onto the outskirts; a road between field walls leads on to Birchlight Vale.

**Map.** The map grows to about **2.4 km square** *(proposal)* so the outskirts east of the wall are a real hunting ground.

**Trees and foliage.** Inside: pines and birch on the terraces (`SM_Env_Tree_Pine_*`, `SM_Env_Tree_Birch_*`), flower lines and footpaths (`SM_Env_Flowers_Line_*`, `SM_Env_Footpath_*`), climbing vines on the elven houses (`SM_Env_Vines_Preset_*`), glowing trees and a giant tree in the Ancestor Grove (`SM_Env_Tree_Glowing_*`, `SM_Env_Tree_Giant_*`). Outside: meadows of soft grass and wildflowers, birch groves, berry bushes along the field walls, a few dark pines on the ridges.

**Ground, hills and rocks.** Grey Nordic cliffs and boulders (`SM_Env_Rock_Cliff_*`, `SM_Env_Stone_*`, Viking Realm), stone stairs (`SM_Env_Stone_Stairs_*`), dark shingle at the water, grass and wildflowers outside, dry-stone field walls (`SM_Prop_Stonewall_*`).

**Buildings and walls.**

- *Harbor Market (water level):* timber docks, pillars and stairs (`SM_Bld_Dock_Wood_*`), a wharf (`SM_Bld_Wharf_*`), stalls, fish racks, barrels; Broker and Kiosks.
- *Hall ring:* grass-roofed longhalls 30–45 m long with dragon-head gables (`SM_Bld_Hall_Roof_*`, `SM_Bld_Wall_Logs_*`, `SM_Bld_Head_*`); the **Pact Hall** 60 m long with a throne and fire pit; Chancellor and Herald.
- *The Rune Forge:* an open smithy with furnace, anvil, grindstone and weapon racks; Armorer and Fixer.
- *Elven terraces:* curved walls, concave roofs, round towers and balconies (`SM_Bld_Wall_Curved_*`, `SM_Bld_Roof_Concave_*`, `SM_Bld_Roof_Convex_Tower_*`), fountains.
- *The Ancestor Grove:* a ring of glowing trees round runestones (`SM_Prop_RuneStone_*`), a 60 m giant tree at its heart.
- *Landward wall:* stone castle walls, round towers and a gatehouse with timber hoardings (`SM_Bld_Castle_Wall_*`, `SM_Bld_Castle_Tower_Round_*`, `SM_Bld_Castle_Hoarding_*`).
- *Outside:* a burned farmstead from the old raid, a shepherd's hut, cairns, a burial mound or two, a watch post with a beacon at the gate road.

**Signs and billboards.** No billboards: a land of carved things. Runes carved on door posts and the Oathstones (every oath ever sworn here), painted shields on the hall walls, Covenant banners on the wall towers (the crest is open), carved wooden waymarkers at the forks outside the wall, a brass horn at the gate.

**Water, rivers, bridges and docks.** The fjord: deep black-blue, glassy at dawn, choppy in the afternoon wind; waterfalls off the terraces (`SM_Env_Water_Plane_Waterfall_*`); long timber docks with moored longships (`SM_Veh_Boat_Large_*`, about 25 m) and fishing boats; a stream runs out of the outskirts into the fjord under a small stone bridge.

**Connections.** The landward gate opens east onto the outskirts and the road to Birchlight Vale; the fjord opens west to the sea (no sea travel yet).

**Creatures (outskirts, Lv 1–20).** *In the game now:* Bonewalker and Blightborn (the restless dead from the old barrows outside the wall), Blight Reaver near the mounds, Wraith at night, Colossus as the far Bruiser. *Needs a model:* young blight-boars, Hegemony scout drones. Deer and hares, ravens on the roof ridges, gulls on the docks, wild Bramblebacks grazing outside the wall. Inside: shieldmaidens and elven wardens on the wall, fishers, children on the hall steps.

**Sound.** Surf and gulls; a smithy hammer; a horn at dusk; outside, wind in birch and a far raven.

**Points of interest.** The Pact Hall · Harbor Market · The Ancestor Grove · The Rune Forge · *The Oathstones* · *Skald's Rest* (a mead hall on the harbour) · *The Falls of Aldhavn* (vista) · *The Old Raid Farm* (outskirts).

### Z1: Birchlight Vale (Lv 18–40)

**The vibe.** A golden birch valley that should be the most peaceful place in the world: white trunks, knee-deep wildflowers, a slow river and a ring of standing stones in the sun. In the middle of it, Lofthollow still smokes: a village burned by a Hegemony extraction raid, the drill they left behind still humming in its square.

**Concept prompt.** *Wide establishing shot from a gentle hillside at golden late afternoon looking down a broad green valley with a slow blue river winding through it. Groves of tall white birch with gold and green leaves, meadows knee-deep in white, violet and yellow wildflowers. In the middle distance a small Nordic village of thatch-roofed timber houses, three burned black with roofs fallen in, thin grey smoke rising. In the village square an out-of-place grey steel drilling rig with a blinking cyan light. On a rise to the right, a circle of 5 m runestones. The far end of the valley narrows between grey cliffs into a pass. Long warm light, soft lilac shadows, cumulus clouds. Synty POLYGON low-poly, bold ink outlines, flat colour, comic finish.*

**Palette.** Meadow green `#7DB24A` · birch white `#EDEAE0` · leaf gold `#E3B23C` · river blue `#4F8FBF` · wildflower violet `#9B7BD1` · soot black `#2B2522` · ember orange `#E0702E`.

**Tone.** Warm, open and wounded. The land is loved: it should feel like a place people sang about. The raid is a wrong note in a beautiful song, and it should hurt to look at. Never grim or muddy: the sun shines here.

**Sky, day to night.**

- *Dawn:* mist in the valley floor, the sky pale gold to lilac, birdsong.
- *Day:* deep clear northern blue, big white cumulus moving fast, cloud shadows sliding across the meadows; the sun always a little gold.
- *Dusk:* long and slow, golden orange; the birches lit like candles; Lofthollow's last fires glow.
- *Night:* clear and full of stars, a pale green aurora low over the far fells, a big moon, fireflies by the river.
- *Weather:* Clear, Few Clouds, Partly Cloudy; quick Light Rain showers that come and go in a minute (sun and rain together, a rainbow over the Stone Ring if it can be afforded); Fog at dawn only.

**Lighting, shadows and fog.** Low warm sun, long soft shadows, gold rim light on the birch trunks; shadows cool lilac, never black. Thin white dawn fog in the river bottom, burned off by mid-morning. Woodsmoke from Lofthollow drifting downwind across the meadow in a long grey veil.

**Hills and verticality.** A broad U-shaped valley: the floor nearly flat along the river, rolling grassy shoulders rising to wooded ridges 80–120 m up, grey cliffs at the north end closing to Elk Pass. Little real verticality; the Stone Ring stands on a low rise seen from everywhere.

**Navigation.** Open and forgiving: walk anywhere. The river is the spine (south to north), the Stone Ring the landmark, worn dirt tracks between stone field walls the paths. Ferry Stones and the Old Mill's bridge are the crossings; the valley funnels to Elk Pass, the one narrow place.

**Trees and foliage.** Birch in groves of 20–40 (`SM_Env_Tree_Birch_*`, 18 m, white trunks, gold-green crowns; `SM_Tree_Birch_*` for variety), dark pines on the ridges, berry bushes red with berries along the walls (`SM_Env_Bush_Berries_*`), wildflower carpets (`SM_Env_Wildflowers_Patch_*`, `SM_Env_Flowers_Flat_*`), soft grass (`SM_Env_Grass_Med_Clump_*`), ferns at the grove edges, mossy fallen logs (`SM_Tree_Log_*`). Round Lofthollow: scorched grass, black stumps, ash.

**Ground, hills and rocks.** Soft green turf, pebbly river banks, grey lichen-spotted boulders (`SM_Env_Rock_*`), dry-stone field walls with wooden stiles (`SM_Prop_Stonewall_*`), grey cliffs at the pass (`SM_Env_Rock_Cliff_*`).

**Buildings and walls.**

- *Lofthollow (200 m):* about fifteen timber-and-wattle houses with deep thatch, carved door posts, drying racks, a well (`SM_Bld_Well_*`), a broken cart; four burned to black frames (Fantasy Village `SM_Bld_Wall_Curved_*`, `SM_Bld_Roof_Thatch_Preset_*`, `SM_Bld_Castle_DestroyedWall_RubblePile_*`, `FX_Smoke_*`). In the square the Raid Rig, a grey Hegemony drill on a tripod (`SM_Prop_Drill_*`), its cyan light blinking, cables into a hole.
- *The Stone Ring (50 m):* 4–5 m grey runestones with knotwork, an arch on the north side (`SM_Prop_StoneRunes_*`, `SM_Prop_StoneArch_*`, `SM_Prop_RuneStone_*`).
- *The Old Mill:* a stone-and-timber windmill on the riverbank with a water wheel (`SM_Bld_Windmill_*`); the Wayfinder.
- *Hearthbarrow Mound:* a grass barrow by the Stone Ring with a stone door.
- *Elk Pass:* cliffs closing to 120 m, a cairn and elk antlers on a post (`SM_Prop_Cairn_*`).

**Signs and billboards.** Runes on the standing stones and door posts, a painted shield on the Old Mill's door, carved waymarker posts with Covenant knotwork at the forks, an oath-ribbon tied to the well; on the Raid Rig, stencilled Hegemony letters, a serial number and a hazard sticker: the only print in the valley.

**Water, rivers, bridges and docks.** One slow clear river 20–30 m wide, blue with gold light on it, pebbly banks, a mill race, lily pads in a backwater, reeds; the Ferry Stones cross it as a line of flat grey boulders; a timber bridge and a small landing at the mill. No docks.

**Connections.** South: the road from Aldhavn's outskirts between field walls. North: Elk Pass, the portal to Skeldfjord in the throat of the pass. Hearthbarrow under its mound.

**Creatures.** *In the game now:* Bonewalker and Blightborn (the dead crawling out of the barrows since the raid broke the cycle), Blight Reaver near the Stone Ring, Wraith at night round the stones, Colossus holding Elk Pass. *Needs a model:* blight-boars, raid scavengers picking through Lofthollow. Elk and deer in the birch, songbirds, butterflies, wild Bramblebacks grazing by the river.

**Sound.** The river; birdsong; fire crackle and a timber settling in Lofthollow; wind in birch leaves.

**Points of interest.** Lofthollow ruins (first Contracts) · The Stone Ring · The Old Mill (Wayfinder) · Elk Pass (Bruiser) · *The Raid Rig* · *Hearthbarrow Mound* (Run entrance) · *Ferry Stones*.

### Run: Hearthbarrow (Lv 32–40 · 1 room)

**The vibe.** A chieftain's barrow under the grassy mound by the Stone Ring: one long timber-ribbed hall, torches in iron brackets, grave-goods heaped round a burial longship, and a dead king who will not lie down.

**Concept prompt.** *Interior of a Viking burial barrow, a long low hall 40 × 20 m with an 8 m ceiling of dark timber ribs and packed earth, torches in iron brackets, warm orange light, roots poking through the ceiling. At the far end a wooden burial longship on a stone plinth, shields along its side, a stone throne behind it with a crowned skeleton king in rusted mail rising from it. Chests, spears and a drinking horn on the floor. Dust in the light. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Torch orange `#E58A2E` · earth brown `#5A4030` · bone `#E2D6BE` · rust iron `#7A4A33` · shadow `#1E1714`.

**Layout.** One room: the stone door in the mound → a short passage → the hall → the king's ship at its head.

**Lighting, shadows and fog.** Torchlight only, flickering; warm pools with near-black between; dust motes in the beams.

**Structures and assets.** Crypt entrance, stone arches, rune pillars, a stone throne (`SM_Env_Entrance_Crypt_*`, `SM_Env_Ceiling_Arch_*`, `SM_Env_Rune_Pillar_*`, `SM_Env_Stone_Throne_*`); chests and hanging torches (`SM_Prop_Chest_*`, `SM_Prop_Torch_Hanging_*`); a burial longship (`SM_Prop_Boat_Structure_*`).

**Signs.** Runes on the throne and the ship's prow; the king's name carved over the door.

**Creatures.** *In the game now:* Bonewalker (the king's housecarls). *Needs a model:* the boss, the Hearthbarrow King *(proposal)*: a tall crowned skeleton with a broken sword and a round shield.

### Z2: Skeldfjord (Lv 38–60)

**The vibe.** A raw northern coast of black-sand coves under sea cliffs a hundred metres high, longship wrecks in the surf, a village drowned to its roofs that you can walk through at low tide, and out on the point a shrine on a sea stack reached by a broken rope bridge. The wind never stops.

**Concept prompt.** *Dramatic wide shot along a northern coastline: tall grey-green sea cliffs topped with heather and leaning windswept pines, a narrow cliff path with a timber watchtower and a fire basket. Below, coves of black sand and shingle, white surf, kelp and driftwood. A Viking longship wreck broken on the rocks, dragon prow still raised. In the shallows the thatched roofs of a drowned village showing above the water. Out on the point a 70 m sea stack with a tiny shrine on top and a broken rope bridge hanging from it. Low grey-gold sun breaking through heavy clouds, spray, gulls. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Sea slate `#3E5A66` · surf white `#F2F4F2` · black sand `#2A2A2C` · heather purple `#8E6A9E` · pine green `#2F5A3E` · wet-rock grey `#6F7A7C` · beacon gold `#E6A93A`.

**Tone.** Wild, lonely and brave: weather you lean into. The Covenant's grief for its sailors and its stubbornness to keep living here. Never cosy: this coast takes people.

**Sky, day to night.**

- *Dawn:* grey-lavender, sea mist rolling under the cliffs, the watchtower's fire still burning.
- *Day:* fast heavy clouds with blue gaps, shafts of sun striking the sea in moving spots, the sun low and silver-gold.
- *Dusk:* a red-gold strip under a lid of cloud, the sea going bronze, the drowned roofs black against it.
- *Night:* stormy dark with the moon breaking through; the watchtower beacon and the shrine's lamp the only lights; faint glow in the surf *(proposal)*.
- *Weather:* the wettest map of its ring: Mostly Cloudy, Overcast, Rain, Thunderstorm out at sea, thick Fog as sea mist hiding the stack; Clear only now and then, which makes it special.

**Lighting, shadows and fog.** Low, silver, broken light that moves with the clouds; soft shadows under overcast, long hard ones when the sun breaks through. Sea mist is the signature: white-grey, rolling in from the west, filling the coves first and the cliff path last.

**Hills and verticality.** Real verticality: cliffs 60–120 m, the path along their top, steep switchback paths down to the coves, the sea stack 70 m; inland (east) the land relaxes into a heather moor. A strong rhythm of cliff top and beach.

**Navigation.** A long coastal strip: the cliff path is the road (fast, windy, exposed), the coves below are slower and cut off by the tide; the drowned village is walkable only at low tide (a later system; build it at low tide); the rope bridge to the stack is the dramatic chokepoint.

**Trees and foliage.** Windswept pines leaning inland (`SM_Env_Tree_Pine_*`), heather and flax on the cliff tops (`SM_Env_Flax_*`), sea grass on the dunes, kelp and seaweed on the beaches (`SM_Env_Seaweed_Beach_*`), lichen, driftwood (`SM_Env_DriftWood_*`). Few trees: wind and stone win.

**Ground, hills and rocks.** Black sand and shingle beaches (`SM_Env_Beach_*`, `SM_Env_Beach_Long_*`), round sea-worn rocks (`SM_Env_Rock_Round_*`), grey-green cliffs (`SM_Env_Rock_Cliff_*`), rocky path edges (`SM_Env_Path_Rocks_Side_*`), heather moor.

**Buildings and walls.**

- *The Drowned Village (250 m):* thatch roofs and timber walls up to the eaves in water, a chapel with its bell, broken jetties (`SM_Prop_Jetty_Broken_*`).
- *Wreck of the Sigrun:* a 40 m longship broken on the rocks, dragon prow raised (`SM_Prop_ShipWreck_*`, `SM_Prop_Boat_Destroyed_*`); the Bruiser.
- *Cliff Watchtower:* timber on a stone base with a fire basket (`SM_Bld_Guard_Tower_*`, `SM_Prop_Beacon_*`); the Wayfinder.
- *Sea-Stack Shrine:* a small shrine with runestones on a 70 m stack (`SM_Bld_Shrine_*`), a broken rope bridge (`SM_Bld_RopeBridge_*`).
- *Fishing huts and fish racks* on the moor's edge; cairns along the cliff path; the whale ribs on the north beach (`SM_Prop_Whale_Skeleton_*`).

**Signs and billboards.** Carved waymarkers, rune-cut memorial stones for sailors lost at sea along the cliff path, painted shields on the wrecks, a carved dragon prow, fishing floats and nets hung as charms; a Hegemony survey buoy washed up, stencilled, the odd thing out.

**Water, rivers, bridges and docks.** The sea is half the map: cold slate-green, white surf, long swells, darker out deep; tide pools, some with Tidecaller-like eggs; a stream falling off the cliff in a thin waterfall onto the beach. Docks: a broken timber jetty at the drowned village, a longship landing on the north beach. The broken rope bridge to the stack.

**Connections.** South: up from Elk Pass, the moor road coming over the rise to the first view of the sea. North: the coast road turns inland into the forest edge, where the portal to the Sylwood stands. Wrecktide Cave at the foot of the cliff, reached over the flats.

**Creatures.** *In the game now:* Wraith (the drowned, in the village at low tide), Blight Reaver (drowned huskarls), Bonewalker on the wrecks. *Needs a model:* sea crabs (big, shield-backed), kelp horrors, drowned huskarls in barnacled mail. Seals on the rocks, gulls, a whale spout far out *(proposal)*.

**Sound.** Surf; gulls; the creak of wrecked hulls; the drowned bell at low tide.

**Points of interest.** The Drowned Village · Wreck of the Sigrun (Bruiser) · Cliff Watchtower (Wayfinder) · Sea-Stack Shrine · *The Bell Under the Tide* · *Wrecktide Cave* (Run entrance) · *The Whale Ribs*.

### Run: Wrecktide Grotto (Lv 52–60 · 2 rooms)

**The vibe.** A sea cave full of broken longships wedged in the rock where the tide threw them, and beyond it a flooded hall where drowned warriors still stand in rows, waiting for a captain.

**Concept prompt.** *Interior of a huge sea cave, 60 × 40 m, dark wet rock and stalactites, broken Viking longships wedged at angles high in the rock, tide pools glowing faintly blue-green, barnacles everywhere, shafts of grey light from a sea opening; beyond, a flooded stone hall where rows of drowned warriors stand knee-deep. Synty POLYGON low-poly, ink outlines, comic shading, cold green light.*

**Palette.** Sea-cave green `#2F5B55` · glowing teal `#5FD6C4` · wet rock `#3A3F42` · old wood `#6B4C32`.

**Layout.** Two rooms: Wreck Cavern (60 × 40 m) → the Tide Hall (40 × 30 m, flooded knee-deep).

**Lighting, shadows and fog.** Grey sea light from the mouth, glowing tide pools, a few lanterns on the wrecks; spray mist; the hall lit only by the water's glow.

**Structures and assets.** Large caves and stalactites (`SM_Env_Cave_Large_*`, `SM_Env_Stalactite_*`), tide pools (`SM_Env_Sewer_Water_*`), wrecked boats and barnacles (`SM_Prop_Boat_Destroyed_*`, `SM_Prop_Barnacle_*`, `SM_Prop_ShipWreck_*`).

**Creatures.** *In the game now:* Wraith, Bonewalker. *Needs a model:* drowned huskarls, sea crabs; the boss, a drowned jarl in a barnacle crown *(proposal)*.

### Z3: The Sylwood (Lv 58–80)

**The vibe.** Elven old-growth: trees so big their roots are hills, lantern-lit walkways and tree-houses in the crowns thirty metres up, green shafts of light, glow-flowers on the fern floor. Then a straight furrow of felled giants where a Hegemony crawler cut through, and the crawler itself lying crashed in the mud.

**Concept prompt.** *Low-angle shot on a mossy forest floor of ferns and glowing blue flowers, looking up at colossal ancient trees with trunks 10–25 m wide, shafts of green-gold light falling through the canopy. Thirty metres up, elegant elven wooden walkways with curved railings and hanging lanterns link the trees, with round tree-houses with leaf-shaped roofs in the crowns. In the background a 90 m Great Tree with a softly glowing doorway between its roots. To one side a raw muddy clearing of stumps and a crashed grey industrial crawler on its side. Synty POLYGON low-poly, ink outlines, flat colour, comic finish, magical but grounded.*

**Palette.** Moss green `#4E7D3A` · deep forest `#1F3A2A` · light-shaft gold `#F2D27A` · glow-flower blue `#6FC8F0` · bark brown `#5B4331` · elven silver `#D7DCE0` · crawler mud `#6A5236`.

**Tone.** Sacred, ancient and quiet, like a cathedral; the clearing should feel like a wound in a body. The elves up in the canopy carry on as if nothing happened: pride and denial.

**Sky, day to night.** Seen in pieces through the canopy.

- *Dawn:* mist between the trunks lit pale gold, birdsong high up.
- *Day:* green light everywhere, small patches of blue, god rays striking the ferns.
- *Dusk:* the canopy goes amber, the Canopy Road's lanterns come on one by one.
- *Night:* the forest's own light takes over: glow-flowers, glowing trees, fireflies, lanterns; Moon Lantern Court open to a starry sky and a big moon.
- *Weather:* Few Clouds, Fog (mist between the trunks: the best mood), Light Rain dripping through the leaves.

**Lighting, shadows and fog.** Deep shade on the floor with bright shafts; soft green ambient; mist between the trunks at dawn and dusk. The clearing is the one place of hard open daylight.

**Hills and verticality.** Two levels: the fern floor (rolling, with root-hills 5–15 m high) and the Canopy Road 25–40 m up; the Great Tree rises 90 m. Spiral stairs round the trunks join them.

**Navigation.** Two worlds: the floor is slow and tangled (roots, ferns, streams); the Canopy Road is a 2.5 km loop, fast and safe-feeling. The Great Tree is always in sight. The Crawler Furrow is a straight scar you can follow to the clearing.

**Trees and foliage.** Giant old-growth trees (`SM_Env_Tree_Giant_*`, `SM_Env_Tree_*`, 30–45 m, each one a landmark), glowing elven trees (`SM_Env_Tree_Glowing_*`), ferns and fern trees (`SM_Env_Fern_*`, `SM_Env_Fern_Tree_*`), moss lumps, mushrooms, roots (`SM_Env_Roots_*`), hanging moss and vines, glow-flowers. In the clearing: stumps in rows (`SM_Env_Pine_Stump_*`, `SM_Tree_Stump_*`), mud, burned logs.

**Ground, hills and rocks.** Moss and fern floor, root-hills, mossy boulders, clear stony streams, the clearing's churned mud and tracks.

**Buildings and walls.**

- *The Canopy Road:* bridges, round balconies and railings from tree to tree (`SM_Bld_Bridge_*`, `SM_Bld_Balcony_Convex_Round_*`, `SM_Bld_Railing_*`), spiral stairs up the trunks (`SM_Bld_Stairs_Curved_*`), lanterns.
- *Tree-houses* in the crowns (`SM_Env_Tree_House_*`, `SM_Bld_Gazebo_*`).
- *The Great Tree:* 90 m, trunk 25 m wide, a doorway between its roots (`SM_Env_Tree_Portal_*`).
- *Moon Lantern Court:* a 60 m platform 35 m up with a planetarium and constellation rings (`SM_Bld_Planetarium_*`, `SM_Prop_Constellation_Rings_*`); the Wayfinder.
- *The Scorched Clearing:* a crashed crawler on its side and its wreckage (`SM_Veh_Scav_Lifter_*`, `SM_Bld_Scav_Wreckage_*`): the only tech in the forest.
- *Warden's Fall:* an elven warden's grave under a fallen giant.

**Signs and billboards.** Elven script carved into railings and lantern posts, silver leaf-sigils on doorways, carved warden staves along the paths; Hegemony stencils on the crashed crawler and a survey flag stuck in a stump.

**Water, rivers, bridges and docks.** Clear streams over mossy stones, small falls, the Weeping Pool (a glow-crystal spring, blue-white, `SM_Prop_Crystal_*`), still black pools between roots. The Canopy Road's bridges and a root-bridge over the stream.

**Connections.** South: from Skeldfjord's coast road into the forest edge. North: where the trees thin and the first snow begins, the portal to Barrowfell. The Rootvault under the Great Tree.

**Creatures.** *In the game now:* Wraith (the restless elven dead), Gargoyle (warden statues come alive). *Needs a model:* rootbound guardians (walking trees of root and bark), glowing moth swarms, fallen wardens (elven undead in silver armour). Owls, fireflies, deer, a white stag *(proposal)*.

**Sound.** Deep wind in leaves; creaking boughs; a distant harp from Moon Lantern Court.

**Points of interest.** The Canopy Road · The Great Tree (Rootvault entrance) · The Scorched Clearing (Bruiser) · Moon Lantern Court (Wayfinder) · *The Crawler Furrow* · *The Weeping Pool* · *Warden's Fall*.

### Run: The Rootvault (Lv 72–80 · 2 floors)

**The vibe.** The elven dead sealed beneath the Great Tree, no longer sealed: a long hall whose walls are living roots, and below it a round ossuary of niches where the old ones lie in silver masks.

**Concept prompt.** *Interior beneath a giant tree: a 60 m hall whose walls and ceiling are thick living roots, soft blue-green glow from fungi and crystals, carved stone niches with elven skeletons wearing silver masks, candles on tall stands; below, a round chamber 40 m across ringed with niches. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Root brown `#4E3826` · fungus glow `#7FE0C2` · silver `#D7DCE0` · candle gold `#F0C46A`.

**Layout.** Two floors: the Root Hall (60 m long) → the Ossuary Hollow (round, 40 m).

**Lighting, shadows and fog.** Fungus and crystal glow, candle pools, a faint mist on the floor of the Hollow.

**Structures and assets.** Roots and trunks (`SM_Env_Roots_*`, `SM_Env_Tree_Trunk_*`), curved caves (`SM_Env_Cave_Curved_*`), bone piles and rounded runes (`SM_Env_BonePile_*`, `SM_Env_Rune_Rounded_*`), candle stands (`SM_Prop_Candle_Stand_*`).

**Creatures.** *In the game now:* Wraith, Bonewalker. *Needs a model:* fallen wardens, root horrors; the boss, the First Warden *(proposal)*.

### Z4: Barrowfell (Lv 78–100 · Scar gate)

**The vibe.** High snow moors of burial mounds and runestone circles under a low white sky, a frozen lake with something dark beneath the ice, and at the top of the pass a great carved arch with dragon heads where the old kings' road ends and the Scar begins.

**Concept prompt.** *Wide shot across high snowy moorland under a heavy pale sky: dozens of grass-and-snow burial mounds each with a stone door and a cairn, a frozen lake 600 m across with an abandoned ice-fishing hut, a circle of tall iced runestones hung with icicles. A processional road of standing stones climbs the pass to a colossal carved stone arch with dragon heads and braziers burning on each side; through the arch, a violet glow. Wind blowing snow. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Snow white `#EEF2F5` · slate `#596570` · ice blue `#A9D3E6` · dead-heather brown `#7A5E4A` · brazier gold `#F0A43A` · Rupture violet `#9B5BE0`.

**Tone.** The Covenant's last stand among its kings: solemn, cold, proud. Ancestors in every mound. The wind is grief.

**Sky, day to night.**

- *Dawn:* a brief pink line under the cloud lid, the mounds casting long blue shadows.
- *Day:* a low white-grey sky, a pale sun disc, snow drifting.
- *Dusk:* pink-gold for a few minutes, the braziers on the arch lit.
- *Night:* clear and frozen, many stars, a green aurora overhead and the violet one beyond the arch.
- *Weather:* Snow and Overcast most of the time; Blizzard (whiteout) sometimes; Clear rarely and gloriously.

**Lighting, shadows and fog.** Flat soft light with little shadow; blue shade; blowing snow as moving fog near the ground; gold firelight at the arch and the turf hut.

**Hills and verticality.** High moor rolling between 80 and 300 m, rising to the pass at 560 m; mounds 5–10 m; icicle cliffs and a rock arch near the top.

**Navigation.** Open but hard: snow slows, blizzards hide. The King's Road of standing stones is the safe line to the arch; the frozen lake is a shortcut and a danger.

**Trees and foliage.** Snow pines only in sheltered hollows (`SM_Env_Pine_*`), dead heather, frozen grass, icicles (`SM_Env_Icicle_*`).

**Ground, hills and rocks.** Snow mounds and ground cover (`SM_Env_Snow_Mound_*`, `SM_Env_GroundCover_*`), snow hills as barrows (`SM_Env_Snow_Hill_*`), iced cliffs and rock arches (`SM_Env_Rock_Cliff_*`, `SM_Env_Rock_Cliff_Arch_*`).

**Buildings and walls.**

- *Mound fields:* forty barrows, each with a stone door and a cairn (`SM_Env_Door_Large_Stone_*`, `SM_Prop_Cairn_*`).
- *The Runestone Circle (80 m):* engraved stones 4–5 m, iced (`SM_Prop_RuneStone_*`, `SM_Env_Stone_Engraved_*`).
- *The Howlstone:* a 30 m crag with a wind-hole that howls and a crypt gate at its foot (`SM_Env_Entrance_Crypt_*`).
- *Ice-Fisher's Rest:* a turf hut by the lake, the Wayfinder.
- *The Scar Gate arch:* 25 m of carved stone with dragon heads and braziers either side (`SM_Bld_Arch_*`, `SM_Bld_Head_*`, `SM_Prop_Brazier_*`).

**Signs and billboards.** Runes on every stone, kings' names on the mound doors, carved dragons on the arch, a Covenant war banner snapping at the arch.

**Water, rivers, bridges and docks.** The Frozen Lake (blue-white ice with cracks and dark shapes beneath, a fishing hole and an abandoned ice-fishing hut, `SM_Env_Ice_Sheet_*`, `SM_Env_Lake_*`, `SM_Prop_Ice_Fishing_Hut_*`), frozen streams, icicle falls on the cliffs. A stone causeway over a frozen stream on the King's Road.

**Connections.** South: from the Sylwood's upper edge. North: the arch of the Scar Gate, into the east end of The Scar. Howlstone Crypts under the crag.

**Creatures.** *In the game now:* Blight Reaver and Bonewalker (the kings' guard), Wraith, Colossus on the lake. *Needs a model:* ice wolves, frost giants, barrow kings' guard. Ravens, a lone elk.

**Sound.** Wind; ice cracking; a horn from the crypts.

**Points of interest.** The Runestone Circle · The Frozen Lake (Bruiser) · The Howlstone (crypt gate) · The Scar Gate · *The King's Road* · *Ice-Fisher's Rest* (Wayfinder) · *The Last Watch* (vista over the Scar from the arch).

### Run: Howlstone Crypts (Lv 92–100 · 3 floors)

**The vibe.** A frozen crypt city under the fells: long galleries of burial niches, a hall of seated stone kings under ice, and a round pit with a spiral stair down to the last king.

**Concept prompt.** *Interior of an icy Viking crypt hall, 60 × 30 m, rows of seated stone king statues on thrones, frost and icicles on everything, torches burning blue-white, a spiral stair descending into a dark round pit. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Ice `#BFE3F2` · stone grey `#6E747A` · torch blue `#8CC8FF` · gold `#E6B04A`.

**Layout.** Three floors: Barrow Halls (long galleries of niches) → the Hall of Kings (60 × 30 m) → the Deep Cairn (a round pit 40 m across with a spiral stair).

**Lighting, shadows and fog.** Cold blue-white torches, ice glowing faintly, breath-mist in the air, the pit dark.

**Structures and assets.** Curved stone ceilings, statues, spiral stairs, stalagmites (`SM_Env_Ceiling_Stone_Curved_*`, `SM_Env_Statue_*`, `SM_Env_SpiralStairs_*`, `SM_Env_Stalagmite_*`), ruin pillars (`SM_Bld_Ruin_Pillar_*`), icicles, thrones.

**Creatures.** *In the game now:* Bonewalker, Blight Reaver, Wraith. *Needs a model:* frost-dead; the boss, the Last King *(proposal)*.

## Cessation

![Concept art: Cessation](art/concept-cessation.jpg)

**Null.** A no-man's-land of alien geography where the awakened machines and dead keep perfect order: a crater city of gear and bone, bone-white plains under a wrong-coloured sky, trenches of machine graveyards, a forest of basalt needles hung with ossuaries, and a glassed caldera that trembles.

| Look and feel | |
| --- | --- |
| Tone | absolute, unemotional, exact. No fire anywhere — only cold light. Geometry of bone and chassis. Uncanny and calm, never gory: bone is clean and dry, like a museum. |
| Palette | Blood Red, Necrotic Violet, Bone, Basalt, Sick Green sky. Red and violet light on bone and basalt. The sky is wrong-coloured on purpose. |
| Light | Cold and exact; night or deep dusk much of the time under a green and violet aurora across the whole sky. Braziers burn cold violet, windows glow amber-white, pits glow crimson. |
| Synty packs | Sci-Fi Worlds (craters, blobs, alien ruins, corp ruins) · Dark Fantasy (basalt, grunge, moss) · Dark Fortress (iron walls) · Sci-Fi Space · Cyber City · Dungeon · Dungeon Realms · Mech · Apocalypse Wasteland; nature from Arid Desert (craters, spikes, bones), Enchanted Forest (crystals, recoloured), Alpine Mountain (ice); the aurora from the Nature Biomes FX |
| Run flavour | precise, humming, funereal: machinery that never stopped and ossuaries arranged like circuit boards. |

### Home City: Terminus (city safe · outskirts Lv 1–20)

![Mockup: Cessation](art/mockups/mockup-cessation-overview.jpg)

![Mockup: Cessation, closer](art/mockups/mockup-cessation-close.jpg)

**The vibe.** A crater city of gear and bone, 900 m across and 250 m deep: a spiral road winds down the crater wall in three turns, lined with conveyor streets where skeletal and machine citizens move in perfect order, never hurrying, never stopping. At the bottom the Assembly Core hums under a lattice dome; on the rim the Ossuary Spire and a gothic iron fortress stand against the aurora. Nobody raises their voice. Outside the rim gate the pale plain begins, and that is where a new Null learns to fight: among the first fossils and the blobs.

**Concept prompt.** *Aerial view at deep dusk under a green and violet aurora sky of a huge circular crater 900 m across. A spiral road with railings and cold violet lamps winds down the crater wall in three turns; along it, conveyor-belt streets with lines of skeletons and robots walking in perfect file. At the bottom a glowing machine core under a lattice dome. On the rim a tall round bone tower with niches of skulls, and a gothic black iron fortress with spiked towers, a drawbridge and red banners. Frozen white waterfalls down the crater walls. Outside the rim, a pale bone-white frosted plain with fossils and dark violet blob growths. Synty POLYGON low-poly, bold ink outlines, flat colour, halftone shadows, comic-book finish.*

**Palette.** Basalt black `#232129` · iron grey `#3E3E46` · bone `#E6E1D3` · blood-red banner `#A8232B` · lamp violet `#8F4FD1` · aurora green `#3FAE8C` · window amber-white `#F3E3C0` · frost `#C9D2D8`.

**Tone.** Order as a form of grief. Awe at the precision, unease at the silence. A player should feel this city is utterly safe and utterly indifferent. Never evil-looking for its own sake: no gore, no spikes for show (the fortress's spikes are architecture, not threat).

**Sky, day to night.**

- *Dawn:* the green lightens to pale mint at the rim, the aurora fades but never disappears, no warm colour.
- *Day:* a pale sea-green sky with a small white sun that gives light but no warmth; thin streaky high cloud; the aurora faint, like a scar in the sky.
- *Dusk:* teal sinking to violet, the aurora brightening in curtains; the spiral road's violet lamps come on, turn by turn, from the rim down.
- *Night:* violet-black, the aurora at full strength over the crater; the Core glows at the bottom like a coal; amber-white windows in exact rows.
- *Weather:* Clear and Few Clouds; frost Snow in the coldest hour; dust Fog drifting in over the rim from the plain. Never rain.

**Lighting, shadows and fog.** Cold white daylight from a high small sun, sharp shadows with violet in them; inside the crater the lower turns are in shadow most of the day, lit by violet lamps and the Core's red-amber glow. Thin frost haze in the crater bottom.

**Hills and verticality.** The most vertical Home City: rim at 250 m, the spiral road dropping through terraces at 160 m and 70 m to the floor; the Ossuary Spire 120 m on the rim; frozen falls down the walls. Outside the rim the plain is flat and open, cratered.

**Navigation.** Inside: the spiral road is the one way down (with stairs cutting between turns); every district is on a terrace off it. Outside: the south rim gate opens onto the outskirts, a pale plain round the crater; the road leads on to the Palefields.

**Map.** The map grows to about **2.4 km square** *(proposal)* so the plain round the crater makes a real hunting ground.

**Trees and foliage.** No plants inside. Cable vines hang on the crater walls (`SM_Prop_Cable_*`), crystal clusters glow violet in niches (`SM_Env_Crystals_Cluster_Large_*`). Outside: dead smooth white trees standing in rows, black and violet blob growths in the hollows, alien spikes.

**Ground, hills and rocks.** Basalt crater walls and curved cliffs (`SM_Env_Cliff_Curve_Large_*`, `SM_Env_Cliff_Basalt_*`), dark paving (`SM_Env_Path_Tiles_*`), frozen falls (`SM_Env_Water_Plane_Waterfall_*` in ice-white); outside, pale ash-snow ground, cracked earth (`SM_Env_Ground_Fissure_*`), crater rims (`SM_Env_Crater_Edge_*`), spikes (`SM_Env_Rocks_Spikes_*`).

**Buildings and walls.**

- *The spiral road:* 12 m wide, railings (`SM_Bld_Bridge_Railing_*`), braziers burning violet (`SM_Prop_Brazier_*`).
- *Conveyor Concourse (east terrace):* conveyor belts laid flat (`SM_Bld_Scav_Refinery_Pipe_*`, `SM_Prop_Pod_Pipe_*`), stalls (`SM_Bld_Vendor_*`), boxes of bone; Broker and Kiosks.
- *Reclamation Yards (west terrace):* scav garages and refineries, piles of robot parts (`SM_Bld_Scav_Garage_*`, `SM_Prop_Robot_Part_*`, `SM_Prop_Robot_Posed_Pile_*`); Armorer and Fixer.
- *Assembly Core (floor):* a glowing core in a strut cage (`SM_Env_Core_*`, `SM_Env_Core_Strut_*`, `FX_Core_*`); Chancellor and Herald.
- *The Ossuary Spire (rim, 120 m):* stacked round iron walls with a pointed roof (`SM_Bld_Wall_L_Curve_*`, `SM_Bld_Tower_Roof_*`); inside, skull niches in exact rows.
- *The iron fortress (north rim):* gothic black walls, gate, curved towers, drawbridge, red banners (`SM_Bld_Wall_L_*`, `SM_Bld_Wall_L_Gate_*`, `SM_Bld_Drawbridge_01_Preset_*`).
- *Outside:* boundary posts every 50 m, a Null survey camp of crate buildings and a radar dish, the first fossils dug up and laid in rows.

**Signs and billboards.** No writing for the living. The Null mark with geometry: red banners with a single white circle (the crest is open), numbered terraces in white numerals, painted red guide-lines on the road, the hourly bell called *The Count* *(proposal)*; outside, numbered bones and red survey lines.

**Water, rivers, bridges and docks.** No open water: frozen waterfalls on the crater walls, a frozen pool on the floor around the Core reflecting the aurora; footbridges of iron between terraces; no docks.

**Connections.** The south rim gate opens onto the outskirts and the road to the Palefields.

**Creatures (outskirts, Lv 1–20).** *In the game now:* Bonewalker and Blightborn (unawakened dead wandering out of the fossil beds), Seraph Unit (stray units), Carapace Raider in the blob hollows, Colossus as the far Bruiser. *Needs a model:* bone crawlers (skull-and-spine centipedes), blob spawn, stray drones. Vultures circling. Inside: Null citizens (skeletons and robots) in lines, drones, conveyor carts.

**Sound.** A deep machine hum, conveyor clatter, a bell that rings once an hour exactly; outside, wind over dust.

**Points of interest.** The Assembly Core · The Reclamation Yards · The Ossuary Spire · Conveyor Concourse · *The Count* · *The Rim Gate* · *The Frozen Fall* (vista).

### Z1: The Palefields (Lv 18–40)

**The vibe.** A bone-white dust plain under a green-and-violet aurora that never quite goes away: nothing grows, black and violet growths pulse in the hollows, and out of the ground reaches the arm of a buried colossus as long as a street, fingers curled, as if it fell grasping.

**Concept prompt.** *Vast flat pale plain of bone-white dust and frost under a wrong-coloured sky: deep teal-green overhead fading to violet at the horizon, with ribbons of green and violet aurora. A gigantic stone-and-metal arm, 90 m long, rises out of the ground at an angle, fingers curled; far behind it a half-buried giant head with one eye socket full of glowing violet crystal. Clusters of glossy black and violet blob growths in shallow craters. A tilted dead steel power pylon on the horizon hung with a red banner. Long cold shadows, no warm light. Small skeletal figures walking in a straight line in the distance. Synty POLYGON low-poly, bold ink outlines, flat colour, halftone shadows, comic-book finish.*

**Palette.** Bone white `#E6E1D3` · frost grey `#B9BFC4` · sick green sky `#3E7A6B` · necrotic violet `#7B3FA0` · basalt `#2A2730` · blood red `#A8232B`.

**Tone.** Uncanny and calm: wrong in a quiet way. Things are laid in rows, nothing is wasted, no fire burns. The player feels very small and very watched.

**Sky, day to night.**

- *Dawn:* green lightening to pale mint, the aurora fading but never gone.
- *Day:* a pale flat sea-green sky, a small white sun with no warmth, thin streaky cloud, the aurora a faint scar.
- *Dusk:* teal to violet, the aurora brightening in curtains.
- *Night:* violet-black, the aurora rippling across the whole sky, a cold violet glow on the dust; the colossus's crystal eye glowing on the horizon. The brightest night in the game.
- *Weather:* Clear and Few Clouds; Fog as pale dust; dust storms (Overcast and wind) that turn the sky ochre-green; frost-flake Snow in the coldest hour. Never rain.

**Lighting, shadows and fog.** Cold white light, very sharp shadows with violet in them; the aurora lights everything faintly at night, so night is never black. A low pale dust haze moving in slow streams a few metres high, thickest round Dust Crater.

**Hills and verticality.** Very flat on purpose: a plain pocked with craters 10–200 m across and spiral crater patterns; height only from the colossus (the arm rises to 25 m at the elbow and can be climbed) and the crater walls; low basalt outcrops on the edges.

**Navigation.** Wide open: you see everything and everything sees you. Steer by the colossus arm in the middle and the dead pylon on the far north-east edge. Crossing feels exposed and long; crater rims are the only cover.

**Trees and foliage.** None alive: glossy black blob growths with violet veins that pulse slowly (`SM_Env_Blob_*`, `SM_Env_Ground_Blob_*`, sulphur blobs `SM_Env_Sulphur_Blob_*` recoloured violet), alien spikes, tumbleweed of dry cable (`SM_Prop_Tumbleweed_*`), dead white trees like driftwood standing in rows.

**Ground, hills and rocks.** Pale ground layer with frost; craters and spiral craters (`SM_Env_Detail_Crater_*`, `SM_Env_Detail_Crater_Spiral_*`, `SM_Env_Crater_Edge_*`), spikes and pebbles (`SM_Env_Rocks_Spikes_*`, `SM_Env_Rock_Pebbles_*`), Dust Crater's cliffs (`SM_Env_Crater_*`, `SM_Env_Rock_Cliff_*`).

**Buildings and walls.**

- *The Colossus:* an arm of plated stone and dark metal, half fossil, half machine (`SM_Env_Artifact_Arm_*` at Colossal scale); the head 250 m off, sunk to the cheekbones, crystal in its eye (`SM_Env_Artifact_Head_*`).
- *The First Pylon:* a 35 m Hegemony power pylon leaning 30°, its base walled in black iron and hung with a red banner (`SM_Prop_Powerline_*`, `SM_Bld_Wall_L_*`); the Wayfinder.
- *Bone Rows:* ancient fossils laid out by the Null in a perfect grid (`SM_Env_Bone_Rib_*`, `SM_Env_BonePile_*`, `SM_Prop_Bones_*`).
- *The Idle Door:* a square black-steel factory hatch flush in the plain, red light round its rim.

**Signs and billboards.** Geometry, not words: red banners with a white circle, red survey lines in straight rows, white numerals stencilled on the fossils, boundary posts every 50 m; a faded Hegemony plate on the pylon, *DANGER: HIGH SOUL LOAD*.

**Water, rivers, bridges and docks.** None liquid. A frozen pond in one crater, violet-white and perfectly flat, with something dark beneath; frost rims in the shadows.

**Connections.** South: from Terminus's outskirts. North-east: past the First Pylon to the portal to the Scrapline, set in a frame of stacked chassis. The Idle Door drops into the Idle Works.

**Creatures.** *In the game now:* Bonewalker and Blightborn (unawakened dead), Carapace Raider in the Blob Fields, Colossus as the Dust Crater Bruiser, Seraph Unit near the Idle Door. *Needs a model:* bone crawlers, blob spawn, stray drones. Vultures circling; lines of Null skeletons and robots walking in perfect file far off (ambient, not Hostile).

**Sound.** Wind over dust; a low slow pulse from the blobs; the aurora's faint chorus at night; silence.

**Points of interest.** The Colossus Arm (climbable vista) · The Blob Fields · Dust Crater (Bruiser) · The First Pylon (Wayfinder) · *Bone Rows* · *The Idle Door* (Run entrance) · *The Eye*.

### Run: The Idle Works (Lv 32–40 · 1 room)

**The vibe.** A stalled fabrication hall where the unawakened machines still twitch on the line: cold strip lights, rows of half-built robot bodies on hooks, and at the end one great press that has not come down in a hundred years. Until now.

**Concept prompt.** *Interior of a long industrial assembly hall, 70 × 30 m, 14 m high, cold white strip lights in lines along the ceiling, a few flickering. A conveyor line down the middle with half-assembled humanoid robot bodies hanging from overhead hooks, some twitching. Robotic needle-arms frozen mid-weld. At the far end a massive vertical hydraulic press with a red warning light. Black and dark-grey metal, red floor markings in a strict grid. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Strip-light white `#E8F1F5` · machine black `#1C1D22` · warning red `#C62A2F` · steel `#6E737A` · violet spark `#9A5BD0`.

**Layout.** One room: the hatch stairs → the line → the press at the far end.

**Lighting, shadows and fog.** Rows of cold white strip-light, slow-turning red beacons, violet sparks from the machines that move; hard shadows of the hanging bodies across the floor.

**Structures and assets.** Mechanical walls and machines (`SM_Bld_Wall_Mechanical_*`, `SM_Prop_Detail_Machine_*`), assembly machines and needle arms (`SM_Prop_Assembly_Machine_*`, `SM_Prop_Robotic_Needle_Arm_*`), posed robots on the line (`SM_Prop_Robot_Posed_*`), the press (`SM_Prop_Drill_*` set vertical).

**Creatures.** *In the game now:* Seraph Unit and War Frame (unawakened units). *Needs a model:* line-hands (half-built robots with missing limbs); the boss, the Great Press *(proposal)*: the press itself walking on hydraulic legs.

### Z2: The Scrapline (Lv 38–60)

**The vibe.** A plateau cut by deep straight trenches whose walls are stacked machine bodies, layer on layer like rock strata; scrap bridges cross the cuts, and when the magnetic storms roll over, loose metal lifts off the ground and hangs in the air.

**Concept prompt.** *Wide shot from a scrap bridge over a 40 m deep straight trench whose walls are strata of crushed robot bodies, trucks and mech frames stacked in layers. Rust-red lichen on the trench floor, cables hanging like vines. A colossal crashed cargo hauler lies broken across the trench in the middle distance, arcing violet electricity, scrap metal floating in the air round it. A storm front of violet-green cloud with lightning rolling over the plateau. Red Null banners on a camp of crate buildings on the far plateau. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Rust `#9C4A2B` · gunmetal `#4D5259` · storm violet `#6C3F9E` · sick green sky `#3E7A6B` · lichen orange `#C7682E` · blood-red banner `#A8232B`.

**Tone.** Industrial, heavy and charged: a graveyard that hums. The Null's grief for their kind in the order of it; the Hegemony's crime in the scale. The air feels like the moment before lightning.

**Sky, day to night.**

- *Dawn:* olive haze, the stacks black against a pale green line.
- *Day:* the green sky dulled by metal dust to an olive haze, the sun a white coin.
- *Storm:* the signature: a violet-green front with sheets of dry lightning and a magnetic shimmer, scrap rising into the air as it passes.
- *Dusk and night:* violet aurora half hidden by storm cloud, lightning lighting the trench walls, the Magnet's arcs.
- *Weather:* Mostly Cloudy and Overcast as metal haze; dry Thunderstorm (magnetic storms) often; dust Fog. No rain, no snow.

**Lighting, shadows and fog.** Flat dull light by day; the trench floors deep in cool shadow; hard white lightning flashes; a low rust-coloured dust haze in the trenches.

**Hills and verticality.** A strong vertical rhythm: flat plateaus and three parallel trenches 40 m deep and 60–100 m wide; up on the plateau (exposed, long views) or down in the cut (enclosed, walls of scrap).

**Navigation.** Lines and crossings: the trenches run east-west, so going north means crossing them on two scrap bridges (chokepoints) or climbing down and up; the Magnet blocks the middle trench like a dam; the Chassis Wall is the landmark cliff.

**Trees and foliage.** None living: cable vines down the trench walls (`SM_Prop_Cable_*`), rust lichen in orange patches (`SM_Env_Grunge_*`, `SM_Env_Moss_*`, Dark Fantasy), wiry dead brush, dead trees wound with cable.

**Ground, hills and rocks.** Cut trench faces (`SM_Env_Cliff_Flat_*`) faced with stacked scrap (`SM_Env_Scrap_Pile_*`, `SM_Env_Junk_Pile_*`, `SM_Prop_Scav_Scrap_Pile_*`), hull-plate debris on the plateaus (`SM_Env_Debris_Shell_*`, `SM_Env_Ground_Junk_*`).

**Buildings and walls.**

- *The Chassis Wall:* a 400 m cliff of stacked robot bodies, mech frames and crushed trucks (`SM_Prop_Robot_Posed_Pile_*`, `SM_Mech_*`).
- *Scrap bridges:* two 80–120 m spans patched with hull plates and cables (`SM_Bld_Bridge_L_*`, `SM_Bld_Scav_Wreckage_*`, `SM_Prop_Tether_Wire_*`).
- *The Magnet:* a 160 m hauler broken in two, arcing, with floating scrap (`SM_Ship_Massive_Transport_*`, `FX_Electricity_*`, `FX_Scrap_Floating_Mesh_Full_*`); the Bruiser.
- *Reclamation Camp:* crate buildings, a scav garage, a radar dish, red banners; the Wayfinder.
- *Storm Spire:* a lightning-struck mast where the storms start.
- *The Disassembly Gate:* cut into the Chassis Wall.

**Signs and billboards.** Null sorting marks: painted numbers and sizes on the stacks, white grid lines on the Sorting Field, red banners. Old Hegemony markings on the scrap itself: serial plates, hazard stripes, company logos half legible.

**Water, rivers, bridges and docks.** No open water: oily rainbow puddles on the trench floors, a pale green coolant pool leaking under the Magnet. The scrap bridges.

**Connections.** South: from the Palefields' portal. North: the far plateau, its portal to the Needlegarden between two stacked spires. The Disassembly Gate in the Chassis Wall.

**Creatures.** *In the game now:* War Frame, Carapace Raider, Seraph Unit. *Needs a model:* scrap golems (hulks of welded parts), magnet-wraiths (spirits made of floating scrap), salvage swarms (small drones in clouds). No animals.

**Sound.** Metal groaning; storm crackle; distant hammering from the camp.

**Points of interest.** The Chassis Wall · Scrap Bridge (chokepoint) · The Magnet (Bruiser) · Reclamation Camp (Wayfinder) · *The Disassembly Gate* · *Storm Spire* · *The Sorting Field*.

### Run: The Disassembly (Lv 52–60 · 2 rooms)

**The vibe.** The plant where the Hegemony once broke machines for parts: a long conveyor hall of dismantling arms, then the Press, a square chamber under a moving crusher. The machines remember the sequence.

**Concept prompt.** *Interior of a long dismantling hall, a 90 m conveyor line, robotic arms with saws and claws frozen over robot bodies being taken apart, robot torsos hanging on chains, red warning light, sparks; beyond, a square chamber under a huge descending crusher. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Black steel `#1E1F24` · warning red `#C62A2F` · spark yellow `#F4C64C` · violet arc `#8E55D6`.

**Layout.** Two rooms: the Line (a 90 m conveyor hall) → the Press (a 40 m square chamber).

**Lighting, shadows and fog.** Red beacons, shower sparks, the crusher's warning lamps; smoke from cutting.

**Structures and assets.** Large walkways (`SM_Bld_Floor_Large_Walkway_*`), medical arms as dismantling arms (`SM_Prop_MedicalArms_*`), needle arms, hanging robots (`SM_Prop_Robot_Posed_Hanging_*`), a grinder trap (`SM_Env_Trap_Grinder_*`).

**Creatures.** *In the game now:* War Frame, Seraph Unit. *Needs a model:* dismantler arms that walk; the boss, the Sequence *(proposal)*: the crusher's controller.

### Z3: The Needlegarden (Lv 58–80)

**The vibe.** A forest of black basalt needles forty to a hundred and twenty metres tall, hung between with bone reliquaries swaying on chains; stairs carved spiralling up the spires, chain bridges between them, violet crystal light from the grottos and absolute silence. A cathedral carved out of the tallest needle.

**Concept prompt.** *Vertical shot looking up and across a forest of tall black basalt spires 40–120 m high, each with narrow stairs carved spiralling round it, linked by chain-hung stone and plank bridges. Iron cages full of skulls hang on long chains between the spires, swaying. Violet crystal clusters glow from cave mouths at the bases. In the centre the tallest spire carved into a gothic cathedral front with a huge pointed window. Teal-green sky with violet aurora; violet mist at the spires' feet. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Basalt black `#232129` · crystal violet `#8F4FD1` · bone `#E6E1D3` · aurora green `#3FAE8C` · iron chain `#56565E` · blood red `#A8232B`.

**Tone.** Holy, silent and vertiginous: a cathedral the size of a map. The Null's reverence for their dead arranged like a machine. Awe first, fear second.

**Sky, day to night.** The Cessation sky framed in narrow slots between needles.

- *Dawn and day:* pale teal through the spires, a white sun glinting off the crystals.
- *Dusk:* violet, the crystals starting to glow below.
- *Night:* the aurora at its most beautiful, curtains seen between the needles; the crystal grottos glowing violet underneath.
- *Weather:* Clear; Fog (violet mist pooling between the needles' feet: the best mood); frost Snow.

**Lighting, shadows and fog.** Slots of light between deep shadows; violet up-light from the grottos; mist pooled at the bottom so the needles seem to rise out of a cloud.

**Hills and verticality.** The most vertical map of its ring: paths climb from the floor to 60 m up the needles; the Cathedral Spire stands 160 m. Falling is the danger.

**Navigation.** Up and across: carved stairs spiral up, chain bridges link needles, the Chain Walk runs 60 m up; many dead ends and vistas; the Cathedral Spire is the compass.

**Trees and foliage.** None living: crystal clusters (`SM_Env_Crystals_Cluster_Large_*`, `SM_Env_Gem_Large_*`, `SM_Prop_Geode_*`), fungal shelves on the needles, dead white trees.

**Ground, hills and rocks.** Needles and spire groups (`SM_Env_Detail_Spire_*`, `SM_Env_Detail_Spire_Group_*`, `SM_Env_Basalt_*`, `SM_Env_Rock_Peak_*`) at 40–120 m, carved stairs (`SM_Env_Rock_Stairs_*`), a black basalt floor.

**Buildings and walls.**

- *Chain bridges:* stone and plank spans of 30–80 m hung on chains (`SM_Env_Rock_Bridge_*`, `SM_Env_Dwarf_Bridge_*`, `SM_Prop_Chain_*`).
- *The Hanging Ossuaries:* iron cages full of skulls on chains 20–40 m up between six spires (`SM_Prop_Cage_Hanging_*`, `SM_Prop_Skull_Pile_*`, `SM_Env_Hell_Chains_*`).
- *The Cathedral Spire:* a needle carved into a gothic front with huge windows, buttresses and an arch (`SM_Bld_Wall_Window_Huge_*`, `SM_Bld_Buttress_*`, `SM_Bld_Wall_Archway_*`).
- *The Silent Choir:* a ring of statues with skulls for faces (`SM_Prop_Statue_People_*`).
- *Alien Ruin:* strange geometry and glowing obelisks older than the factions (`SM_Env_Artifact_AlienRuin_*`, `SM_Env_Obelisk_Glow_*`).
- *The Chain Walk platform:* the Wayfinder.

**Signs and billboards.** Engraved numbers on every skull and cage, a Counting Stair with its step numbers cut in, geometric Null sigils on the needles, red banners on the Cathedral.

**Water, rivers, bridges and docks.** None but a still black pool in the Crystal Grotto mirroring the crystals. The chain bridges.

**Connections.** South: from the Scrapline's portal. North: up on the north spires, the portal to Noct Caldera. Ossuary Sigma inside the Cathedral Spire.

**Creatures.** *In the game now:* Gargoyle (on the needles), Wraith, Bonewalker. *Needs a model:* reliquary keepers, crystal stalkers, chain hangers. Bats.

**Sound.** Silence; a chain creaking; wind whistling through the needles.

**Points of interest.** The Hanging Ossuaries · Crystal Grotto (Bruiser) · The Cathedral Spire (Run entrance) · Alien Ruin · *The Chain Walk* (Wayfinder) · *The Counting Stair* · *The Silent Choir*.

### Run: Ossuary Sigma (Lv 72–80 · 2 floors)

**The vibe.** A catacomb grown into the basalt and laid out with machine precision: a hall of bone niches in a circuit-board grid lit by red floor lines, then a round chamber where ribs make the arches and the dead sing.

**Concept prompt.** *Interior of a black basalt catacomb, a 60 × 40 m hall whose walls are a perfect grid of skull niches, thin red light lines running along the floor like a circuit board, ghostly spirit pools; below, a round chamber 50 m across with giant ribs as arches. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Basalt `#1F1D24` · bone `#E6E1D3` · circuit red `#D4343C` · spirit cyan `#8FE6E0`.

**Layout.** Two floors: the Reliquary (60 × 40 m) → the Bone Choir (round, 50 m).

**Lighting, shadows and fog.** Red floor lines, spirit-pool glow, a faint cyan mist round the pools.

**Structures and assets.** Bone piles and ribs (`SM_Env_BonePile_*`, `SM_Env_Bone_Ribs_*`), ornate tiles (`SM_Env_Tiles_Ornate_*`), skull piles and spirit pools (`SM_Prop_Skull_Pile_*`, `SM_Env_Spirit_Pool_*`), light panels in red lines (`SM_Prop_Light_Panel_*`).

**Creatures.** *In the game now:* Bonewalker, Wraith. *Needs a model:* choir dead; the boss, the Cantor *(proposal)*.

### Z4: Noct Caldera (Lv 78–100 · Scar gate)

**The vibe.** A glassed crater rim under an aurora of trapped souls, and in the middle a pit two hundred metres deep glowing crimson, with basalt needles rising from it, giant ribcage fossils arching over it and plank bridges on mine rails strung across. The ground shakes every forty seconds. Something is stirring beneath.

**Concept prompt.** *Wide shot from the glassy black rim of a huge crater at night, looking across a 600 m pit glowing crimson from below, basalt needles rising out of the red light, colossal ribcage fossils arching over the pit, a giant fossil skull on the far edge, plank bridges and mine-rail scaffolds strung across. To the right a ruined research station with glass domes on icy cliffs and frozen waterfalls. On the far rim a black iron gate with a violet portal. Overhead a violet and green aurora with faint faces in it. Synty POLYGON low-poly, ink outlines, comic finish.*

**Palette.** Glass black `#121117` · crimson pit `#C21F32` · aurora violet `#9B5BE0` · aurora green `#3FAE8C` · bone `#E6E1D3` · ice `#BFE3F2`.

**Tone.** Dread and awe: the edge of something huge. Nobody understands what is under the Caldera (the Lore Bible keeps it open), and the art must not explain it.

**Sky, day to night.** Night or deep dusk almost always.

- *Day:* a dim violet twilight, the sun a pale disc behind aurora haze.
- *Dusk and night:* the aurora at its heaviest, with suggestions of shapes (trapped souls) in it; the pit's crimson glow on the underside of the clouds.
- *Weather:* Clear; Fog lit red in the pit; Snow of ash and frost.

**Lighting, shadows and fog.** Crimson up-light from the pit and violet from above: two-colour lighting on everything; fog in the pit glowing red.

**Hills and verticality.** A rim ring 300 m wide round a 600 m pit 200 m deep; scaffolds down the pit walls; icy cliffs at the Observation Core.

**Navigation.** A ring road round the pit, or the bridges across it (faster, exposed); the Scar Gate is opposite the entry.

**Trees and foliage.** Nothing grows: fused glass, cracked edges, frost.

**Ground, hills and rocks.** Fused glossy black glass ground, cracked edges and fissures (`SM_Env_Cracked_Edge_*`, `SM_Env_Ground_Fissure_*`), needles in the pit (`SM_Env_Detail_Spire_Group_*`) rising from crimson light (`SM_Env_Lava_Plane_*` recoloured), floating shards over the pit (`SM_Env_Floating_Rocks_*`), ice sheets at the station (`SM_Env_Ice_Sheet_*`).

**Buildings and walls.**

- *The pit's bones:* ribcage fossils with 30 m ribs and a giant skull (`SM_Env_Bone_Rigcage_*`, `SM_Env_Skull_Huge_*`).
- *Bridges and scaffolds:* mine-track bridges, plank bridges, wooden scaffolds down the walls (`SM_Env_Minetrack_Bridge_*`, `SM_Env_Dwarf_Bridge_*`, `SM_Env_Basement_Support_Pole_*`).
- *Observation Core:* a ruined Hegemony research station of glass domes and a control tower on icy cliffs (`SM_Bld_Pod_Research_*`, `SM_Bld_Corp_Control_Tower_*`); the Bruiser.
- *Outpost Null-7:* dishes, a comms tower, crate buildings and a toppled 18 m war-frame (`SM_Bld_Scav_Dish_*`, `SM_Veh_Mech_*`); the Wayfinder.
- *The Scar Gate:* a black iron gate set into the far rim, red banners, a violet portal (`SM_Bld_Wall_L_Gate_*`).

**Signs and billboards.** Hegemony research signage on the station (*OBSERVATION CORE – AUTHORISED*), Null sigils on the outpost, red banners on the gate, numbered survey stakes round the rim.

**Water, rivers, bridges and docks.** Frozen falls under the Observation Core; no open water. The pit bridges.

**Connections.** South: from the Needlegarden. North: the far rim's Scar Gate onto the south wall of The Scar. The Stirring Well into the pit.

**Creatures.** *In the game now:* Hellion (from the pit), Gargoyle, War Frame. *Needs a model:* rim sentinels, caldera wraiths, the Well's spawn. No animals.

**Sound.** A low tremor every 40 seconds; glass ticking as it cools; the aurora's faint chorus.

**Points of interest.** The Glass Rim (vista over the Scar) · The Stirring Well (Run entrance) · Observation Core (Bruiser) · The Scar Gate · *The Great Skull* · *Outpost Null-7* (Wayfinder) · *The Frozen Falls*.

### Run: The Stirring Well (Lv 92–100 · 3 floors)

**The vibe.** Down toward whatever stirs beneath Nova: a ring gallery round a forty-metre shaft, a spiral ramp eighty metres down its wall, and a cavern where the floor breathes. What it is stays open.

**Concept prompt.** *Interior of a deep vertical shaft lit crimson from below, a spiral ramp down the rock wall, machine vents and old core shells set into the rock; at the bottom a vast dark cavern whose floor seems to rise and fall. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Crimson `#C21F32` · black rock `#141218` · violet `#9B5BE0`.

**Layout.** Three floors: Bore Rim (a ring gallery round a 40 m shaft) → the Descent (a spiral ramp 80 m down) → the Listening Chamber (a 60 m cavern).

**Lighting, shadows and fog.** Crimson from below, darker with every turn of the ramp; the Chamber lit only by pulses.

**Structures and assets.** Core shells and vent tunnels (`SM_Env_Core_Shell_*`, `SM_Bld_Core_Vent_Tunnel_*`), large caves and spiral stairs (`SM_Env_Cave_Large_*`, `SM_Env_SpiralStairs_*`), hell rings and lava flows recoloured crimson (`SM_Env_Hell_Ring_*`, `SM_Env_Lava_Flow_*`).

**Creatures.** *In the game now:* Hellion. *Needs a model:* the Well's spawn; the boss is the owner's own system, its nature open.

## Sunderpeak

![Concept art: Sunderpeak](art/concept-veilspine.jpg)

**Ronin (expansion: hidden at launch).** EXPANSION CONTENT. Frozen outer peaks hiding steam-warmed valleys: the erased nation (Lore Bible §5–7). Written here so its art can be made; nothing of it may be shown to players before the reveal.

| Look and feel | |
| --- | --- |
| Tone | steam and snow, lacquer and paper, silence. A home built by former enemies; a history of assassins in every room. |
| Palette | Moon Silver, Deep Indigo, Lacquer, Snow, Moss. PROPOSAL — Ronin crest, colours and rank words are not canon yet (factions doc). Silver/indigo keeps them apart from all three founders. |
| Light | Dusk and steam: warm lantern light low in the valleys, cold blue snow light on the peaks, mist always moving. |
| Synty packs | Samurai · Samurai Empire · Viking Realm (snow, icicles, mountains) · Vikings (glaciers) · Elven Realm (flowers, footpaths) · Dungeon (Runs); nature from Tropical Jungle (rope bridges), Alpine Mountain (the Veilpass), Arid Desert (vents) |
| Run flavour | hot-spring caves, lacquered shrine vaults, ice archives: what the Ronin hid, and what they did to keep it hidden. |

### Hidden Capital: Kagero (city safe · outskirts Lv 1–20)

![Mockup: Kagero](art/mockups/mockup-kagero.jpg)

**The vibe.** A gorge city named for heat-haze, seen and not seen: a 300 m deep gorge closed at its head by a white waterfall, both walls stepped into lantern-lit terraces, red arched bridges crossing at two heights, pagodas against snow peaks, rice terraces mirroring the sky, and machines, skeletons, humans and elves sharing one street. Outside the lower gate, the steaming valley floor where young Ronin first learn their blades.

**Concept prompt.** *Wide shot up a deep gorge city at dusk: a tall white waterfall at the head, a blue river between black rocks below, both gorge walls stepped into terraces of dark-timber houses with tiled roofs, red-railed walkways and stairs, paper lanterns glowing amber, cherry trees in pink bloom, rice terraces reflecting the sky. Two vermilion arched bridges cross the gorge at different heights. Three-tier pagodas on the top terraces against snow-capped peaks. Steam rising from hot springs on the gorge floor. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Lacquer red `#B6352E` · dark timber `#3A2A22` · lantern amber `#F0A94A` · cherry pink `#F2A9C0` · moon silver `#C7CDD6` · deep indigo `#2D3561` · snow `#F2F5F8` · river blue `#4F8FBF`.

**Tone.** Secret, warm and layered: a refuge that should never have been possible, everyone living with what was done to keep it. Peaceful, with a held breath.

**Sky, day to night.**

- *Dawn:* pink and silver over the peaks, steam catching the light, the waterfall gold at its top.
- *Day:* soft pale blue, never fully clear because steam rises all day; the peaks white.
- *Dusk:* indigo and rose; lanterns lit terrace by terrace up both walls (the signature hour).
- *Night:* deep indigo, a big silver moon in every rice paddy, the gorge a river of lantern light; cranes silhouetted.
- *Weather:* Fog and Few Clouds; warm Light Rain; Snow that melts on the terraces and stays on the peaks.

**Lighting, shadows and fog.** Soft diffuse light through steam; the gorge floor in shade most of the day; warm lantern pools at night; mist moving in bands, thickest over the baths and the waterfall's foot.

**Hills and verticality.** A 300 m gorge: the baths on the floor, the market 120 m up, the upper bridge 180 m up, the Council 280 m up; stairs everywhere. Outside the lower gate the valley opens into steaming flats and low wooded slopes.

**Navigation.** Up and across: terrace walkways along each wall, two bridges across, the Lantern Stair up the east wall. The hidden road leaves south through the lower gate onto the outskirts and on to the Ember Terraces.

**Map.** Grows to about **2.4 km** *(proposal)*: the gorge city in the north, the steaming valley outskirts to the south.

**Trees and foliage.** Cherry in bloom (`SM_Env_Tree_Cherry_Blossom_*`), maples (`SM_Env_Tree_Maple_*`), ginkgo (`SM_Env_Tree_Ginko_*`), dark pines brought to 25 m, moss on every stone, ferns by the water; outside, bamboo groves and maple woods.

**Ground, hills and rocks.** Black gorge rock and retaining walls (`SM_Env_Rock_Cliff_*`, `SM_Env_Wall_Rock_*`), moss and gravel (`SM_Env_Moss_*`, `SM_Env_Gravel_Pile_*`), stone stairs, rice terraces (`SM_Env_FarmTerrace_*`).

**Buildings and walls.**

- *Terraces:* retaining walls and pillars (`SM_Bld_Platform_Wall_*`), houses (`SM_Bld_House_Base_*`, `SM_Bld_House_Roof_Wood_*`), railings and wide stairs.
- *The two bridges:* vermilion arched bridges stretched to 120–180 m on stone piers (`SM_Bld_Bridge_*`, `SM_Env_Bridge_Curved_*`).
- *The Council of Three:* a 40 m three-tier pagoda (`SM_Bld_Castle_Roof_Large_*`, `SM_Bld_Castle_Roof_Top_*`); Chancellor and Herald.
- *The Mixed Market:* stalls with awnings, lanterns and banners; Broker and Kiosks.
- *The Steam Baths:* stone baths and pools on the gorge floor (`SM_Prop_Bath_*`, `SM_Env_PondEdge_*`).
- *The Shrine of the Unerased:* a shrine with a torii and walls of name tablets.
- *Outside:* the lower gate (a tiled gatehouse with a watchtower), farm huts, a woodcutters' camp.

**Signs and billboards.** Painted wooden shop boards, paper lanterns with characters, prayer slips on shrine ropes, name tablets by the thousand at the Shrine of the Unerased; the Ronin crest left blank on banners (open).

**Water, rivers, bridges and docks.** The 250 m waterfall (`SM_Env_Water_Plane_Waterfall_*`, `FX_Waterfall_Impact_*`), a blue river between black rocks, hot-spring pools, rice paddies like mirrors, a small boat landing on the river for ferrying goods; the two great bridges and many small ones.

**Connections.** The lower gate and the hidden road south onto the outskirts and on to the Ember Terraces.

**Creatures (outskirts, Lv 1–20).** *Needs a model:* mist foxes, steam imps, young terrace boars. Cranes over the gorge, herons, frogs. Inside: Ronin of every origin together (Samurai, skeletons, robots, elves).

**Sound.** The waterfall; wind chimes; a temple bell.

**Points of interest.** The Council of Three · The Steam Baths · The Mixed Market · The Shrine of the Unerased · *The Two Bridges* · *The Lantern Stair*.

### Z1: The Ember Terraces (Lv 18–40)

**The vibe.** Geothermal rice terraces stepping down a steaming valley at dawn: green paddies mirroring the sky, hot-spring pools in black rock, geysers on a timer, maples turning red, and mist drifting through it all so that the valley is always half seen.

**Concept prompt.** *Wide shot from the top of a valley at dawn, looking down a staircase of curving green rice terraces filled with water that mirrors a pink-and-silver sky. White steam rises from hot-spring pools ringed with black volcanic rock and from geysers in a field of yellow sulphur ground. Red and orange maples between the terraces, moss on every stone, a small wooden shrine with a vermilion gate on a terrace edge, stone lanterns. Snowy peaks frame the valley. Mist in soft bands across everything. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Paddy green `#6FA35A` · steam white `#F1F2F4` · moon silver `#C7CDD6` · lacquer red `#B6352E` · maple orange `#E07A2F` · basalt black `#25252B` · deep indigo `#2D3561`.

**Tone.** Hidden, gentle and alive: a secret place that feeds people. Calm with a held breath.

**Sky, day to night.**

- *Dawn:* the signature hour: pink and silver, steam catching the first light, the paddies mirroring it.
- *Day:* soft pale blue, never fully clear for the steam; the peaks white.
- *Dusk:* indigo and rose, lanterns lit along the terrace paths.
- *Night:* deep indigo with many stars and a silver moon in every paddy; steam lit from below by lanterns.
- *Weather:* Fog and Few Clouds; warm drifting Light Rain; Snow that melts before it lands.

**Lighting, shadows and fog.** Soft diffuse light through steam; soft blue shadows; low bands of moving white mist across the terraces, thickest over the vent fields and pools.

**Hills and verticality.** A steep valley wall stepped into terraces 2–4 m high, dropping about 250 m along a stream to the valley floor; black rock outcrops between.

**Navigation.** Down and across: the stream and terrace stairs lead down the valley; raised narrow paths along the paddy edges; the Watch Gate at the bottom the only road out; steam hides what is ahead.

**Trees and foliage.** Rice (`SM_Env_RicePlant_*`, `SM_Env_CropRow_*`), maples red and orange, a few cherries by the shrine, dark pines on the upper slopes, moss, ferns by the pools.

**Ground, hills and rocks.** Terraces and paddy hills (`SM_Env_FarmTerrace_*`, `SM_Env_PaddyHills_*`), yellow sulphur ground and pools (`SM_Env_Sulphur_Ground_*`, `SM_Env_Sulphur_Pool_*`), black rock circles round the springs (`SM_Env_Rock_Circle_*`).

**Buildings and walls.** Farmhouses of dark timber with thatch and tile on the terrace edges, water wheels and bamboo pipes, the Terrace Shrine with a torii and stone lanterns (`SM_Prop_Shrine_*`, `SM_Prop_Torii_Gate_*`, `SM_Prop_Lantern_Stone_*`), the Watch Gate (`SM_Bld_Gate_Dobei_*`, `SM_Bld_Fence_Dobei_*`, a watchtower), the Wayfinder.

**Signs and billboards.** Paper prayer slips, painted plaques at the shrine, carved stone lanterns, a painted board at the Watch Gate.

**Water, rivers, bridges and docks.** Paddies like mirrors, a clear stream with small falls between terraces, milky turquoise hot-spring pools, yellow-green sulphur pools, geysers on a timer; small plank bridges over the stream.

**Connections.** Up: from Kagero's outskirts. Down: the Watch Gate and the road to the Cane Sea. Steamhollow behind the hottest pool.

**Creatures.** *Needs a model:* steam imps, terrace boars, mist foxes. Herons, frogs, Emberwing-like birds over the valley at dawn.

**Sound.** Running water; bubbling vents and a geyser's hiss; a bamboo water-clapper; a far temple bell.

**Points of interest.** The Vent Fields · Terrace Shrine · Hot-spring Pools · The Watch Gate (Wayfinder) · *Steamhollow Mouth* (Run entrance).

### Run: Steamhollow (Lv 32–40 · 1 room)

**The vibe.** A scalding cave behind the springs: boiling milky pools, one narrow ledge round them, steam so thick that shapes come and go in it.

**Concept prompt.** *Interior of a volcanic cave, 50 × 30 m, black rock slick with mineral crusts, boiling turquoise-white pools filling the floor, a narrow rock ledge winding round them, dense white steam lit orange from glowing vents below, dripping stalactites. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Boiling turquoise `#7FD3C9` · steam white `#F3F3F1` · vent orange `#E8783A` · basalt `#1F1E24`.

**Layout.** One room: a ring ledge round the pools to a deepest pool at the back.

**Lighting, shadows and fog.** Orange from below through steam, turquoise glow from the water; steam everywhere.

**Structures and assets.** Caves and stalactites (`SM_Env_Cave_*`, `SM_Env_Stalactite_*`), a lava plane recoloured to boiling water (`SM_Env_Lava_Plane_*`).

**Creatures.** *Needs a model:* steam imps; the boss, a heat-loving thing in the deepest pool.

### Z2: The Cane Sea (Lv 38–60)

**The vibe.** A bamboo sea that whispers: canes fifteen metres tall so close the light comes down green, paths that close when the wind moves, stilt towers poking above the tops, and one dry riverbed running straight through, the only road you can trust.

**Concept prompt.** *Inside a dense bamboo forest, canes 12–15 m tall, green light filtering down in shafts, a narrow path of stone lanterns disappearing into the canes; a clearing with a stilt watchtower rising above the bamboo tops; a dry rocky riverbed cutting straight through. Mist low on the ground. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Cane green `#7CA84A` · shadow green `#2E4A2B` · stone grey `#8C8F8A` · lantern amber `#E8A54A` · deep indigo `#2D3561`.

**Tone.** Watchful and tense: someone is listening. Beautiful and close.

**Sky, day to night.**

- *Day:* a strip of pale blue above the canes, green light below.
- *Dusk:* gold through the canes, then lanterns along the paths.
- *Night:* indigo with stars over the tops, the canes black, fireflies in the clearings.
- *Weather:* Few Clouds, Fog, Light Rain (the canes drip and knock); wind is the weather here.

**Lighting, shadows and fog.** Green, dappled, moving light; deep shade; low ground mist in the clearings.

**Hills and verticality.** Mostly flat with low rises; height only from the stilt towers (25 m) above the canopy.

**Navigation.** A maze: paths branch and vanish, the dry riverbed is the one straight line, clearings and towers are the landmarks.

**Trees and foliage.** Bamboo at full density (`SM_Env_Bamboo_*`, `SM_Env_Bamboo_Wall_*`, `SM_Env_Bamboo_Group_*`), ferns, moss.

**Ground, hills and rocks.** Leaf litter, gravel paths, the riverbed of grey stones (`SM_Env_Rock_Small_*`, `SM_Env_Gravel_Pile_*`).

**Buildings and walls.** Stilt listening posts (`SM_Bld_Platform_Large_Legs_*`, `SM_Bld_Tower_*`), the Hermit's Hut (thatch), the Fox Shrine (shrine, statues, torii), the half-sunk Listening House.

**Signs and billboards.** Cut marks on canes as trail signs (a code only the Ronin read), prayer slips at the Fox Shrine, stone lanterns at path forks.

**Water, rivers, bridges and docks.** The dry riverbed; a spring by the Hermit's Hut; a plank bridge over a gully.

**Connections.** South: the Watch Gate road from the Ember Terraces. North: down into Blackfall Gorge.

**Creatures.** *Needs a model:* cane stalkers, fox spirits (the Kitsune's wild kin), lost scouts. Birds.

**Sound.** Bamboo knocking; wind hiss; footsteps that are not yours.

**Points of interest.** Listening Post 3 (vista) · The Dry Riverbed · The Hermit's Hut · Fox Shrine (Bruiser) · *The Listening House* (Run entrance).

### Run: The Listening House (Lv 52–60 · 2 rooms)

**The vibe.** A half-sunken shrine where the assassins learned to listen before they learned to kill: a hall lined with bronze listening horns, and below it a low cellar where nothing should be heard.

**Concept prompt.** *Interior of a half-sunken lacquered shrine hall, 40 × 20 m, walls lined with bronze listening horns, paper sliding doors with slits of light, paper lanterns; a trapdoor to a low dark stone cellar. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Dark lacquer `#3A1E1C` · bronze `#A87A3E` · paper white `#EEE8DA` · lantern amber `#E8A54A`.

**Layout.** Two rooms: the Hall of Ears → the Cellar (a low 30 m vault).

**Lighting, shadows and fog.** Paper lanterns, slits of daylight; the cellar nearly dark.

**Structures and assets.** Walls, sliding doors and floors (`SM_Bld_Wall_*`, `SM_Bld_Sliding_Door_*`, `SM_Bld_Floor_*`), a basement (`SM_Env_Basement_*`).

**Creatures.** *Needs a model:* listeners (masked figures); the boss, the one who taught them.

### Z3: Blackfall Gorge (Lv 58–80)

**The vibe.** A gorge of black water and roaring falls lit by ten thousand lanterns set into the cliffs; rope bridges swing between the walls, spray hangs in the air, and the Great Fall closes the far end in thunder.

**Concept prompt.** *Wide shot inside a deep gorge at night, black rock walls 300 m high with thousands of small warm lanterns in niches, cliff paths and wooden ledges along both walls, sagging rope bridges between them, a black river far below and a huge white waterfall at the end. Spray mist, cliff pines, moss. Synty POLYGON low-poly, ink outlines, comic finish.*

**Palette.** Black rock `#1E1E22` · lantern amber `#F0A94A` · spray white `#EEF2F4` · moss green `#4E6B3A` · deep indigo `#2D3561`.

**Tone.** Overwhelming and mournful: each lantern a name the Ronin could not keep.

**Sky, day to night.**

- *Day:* a narrow pale-grey strip seen through spray.
- *Dusk:* the lanterns waking, one cliff at a time.
- *Night:* the best hour: indigo with stars in the strip of sky, the walls a galaxy of amber.
- *Weather:* Fog (spray), Rain, Light Rain.

**Lighting, shadows and fog.** Amber lantern light on wet black rock; constant spray mist; the falls white and glowing.

**Hills and verticality.** A gorge 300 m deep: the most vertical Ronin map; paths at several heights on both walls.

**Navigation.** Along the walls and across the bridges; Rope Bridge Nine is the chokepoint; the Great Fall closes the end.

**Trees and foliage.** Cliff pines (`SM_Env_Tree_Pine_*`), spray moss (`SM_Env_Moss_*`), ferns.

**Ground, hills and rocks.** Black cliffs (`SM_Env_Rock_Cliff_*`, `SM_Env_Wall_Rock_*`), ledges (`SM_Bld_Platform_*`), wet stone paths.

**Buildings and walls.** Ledges and cliff walkways, lantern niches (`SM_Prop_Lantern_Wall_*`, `SM_Prop_Lantern_Feature_*`), rope bridges of 60–150 m (`SM_Bld_RopeBridge_*`), the Drowned Gate (a torii standing in the river; the Wayfinder).

**Signs and billboards.** A name on every lantern; carved warnings at bridge heads; prayer strips.

**Water, rivers, bridges and docks.** The black river, falls everywhere, the 300 m Great Fall (`SM_Env_Water_Plane_Waterfall_*`, `FX_Waterfall_Impact_*`, `FX_Water_Splash_*`); a ferry raft on ropes at the lower end.

**Connections.** South: from the Cane Sea. North: up to the Veilpass.

**Creatures.** *Needs a model:* spray wraiths, cliff apes, lantern moths. Hawks.

**Sound.** The falls, always.

**Points of interest.** The Great Fall · Rope Bridge Nine (Bruiser) · Lantern Cliffs (Run entrance) · The Drowned Gate (Wayfinder) · *The Ten Thousand* (vista).

### Run: The Lantern Gallery (Lv 72–80 · 2 floors)

**The vibe.** A hundred-metre cave lined with ten thousand lanterns, one for each name the Ronin could not keep, and beyond it a cold shrine of ice.

**Concept prompt.** *Interior of a long cave 100 m deep, every wall covered in small glowing lanterns, a narrow stone path between them; beyond, a small ice-cold shrine chamber with icicles and a single lantern. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Lantern amber `#F0A94A` · black rock `#1E1E22` · ice blue `#BFE3F2`.

**Layout.** Two floors: the Gallery of Ten Thousand Lanterns → the Cold Shrine (30 m).

**Lighting, shadows and fog.** Warm amber everywhere in the gallery; cold blue in the shrine; breath-mist.

**Structures and assets.** Large caves (`SM_Env_Cave_Large_*`), lanterns and shrine walls (`SM_Prop_Lantern_*`, `SM_Prop_Shrine_Wall_*`), icicles.

**Creatures.** *Needs a model:* lantern keepers; the boss.

### Z4: The Veilpass (Lv 78–100 · Scar descent)

**The vibe.** The frozen outer peaks that let a nation disappear: a switchback pass through blue ice caves and wind-carved ridges, a sealed gate at the top, a cairn of names for the outsiders who got too close, and at the bottom a snow stair dropping toward the Scar.

**Concept prompt.** *Wide shot of a high frozen mountain pass under a clear cold indigo sky: wind-carved ice ridges, blue ice caves, a sealed lacquered gate set into the rock at the top, a cairn of stones with names, a last lantern glowing, blowing snow, and far below a violet glow where the road descends. Synty POLYGON low-poly, ink outlines, comic finish.*

**Palette.** Ice blue `#BFE3F2` · snow `#F2F5F8` · deep indigo `#2D3561` · lacquer red `#B6352E` · lantern amber `#F0A94A` · Rupture violet `#9B5BE0`.

**Tone.** Secrecy and cold: a wall built of weather.

**Sky, day to night.**

- *Day:* white in blizzards, brilliant blue on clear days with snow plumes off the ridges.
- *Dusk:* pink on the peaks, the last lantern lit.
- *Night:* clear indigo with brilliant stars; the violet aurora of the Scar below the pass; the Storm Sovereign seen far off in a storm *(proposal)*.
- *Weather:* Snow, Blizzard, Clear.

**Lighting, shadows and fog.** Hard white light on snow, deep blue shadows, ice caves glowing blue from within; blowing snow as moving fog.

**Hills and verticality.** The highest map: 700 m of climb; switchbacks and ice caves.

**Navigation.** Up the switchbacks and through the ice caves to the Hidden Gate; down the Scar Descent's snow stair to the Scar's north wall.

**Trees and foliage.** Snow pines low on the pass (`SM_Env_Pine_*`), icicles (`SM_Env_Icicle_*`), nothing above.

**Ground, hills and rocks.** Snow mounds, ice sheets, glaciers and glacier arches (`SM_Env_Snow_Mound_*`, `SM_Env_Ice_Sheet_*`, `SM_Env_Glacier_*`, `SM_Env_Glacier_Arch_*`), ice caves (`SM_Env_Cave_*` in ice white).

**Buildings and walls.** The Hidden Gate (sealed: `SM_Bld_Snow_Castle_Gates_*`, `SM_Bld_Snow_Wall_*`), the Frozen Watch tower (`SM_Bld_Snow_Tower_*`, the Bruiser), the Cairn of Names, the Scar Descent's snow archway (`SM_Env_Snow_Archway_*`).

**Signs and billboards.** Names on the cairn stones; a single carved warning at the Hidden Gate; the last lantern.

**Water, rivers, bridges and docks.** Frozen falls and ice; a snow bridge over a crevasse.

**Connections.** Up: the Hidden Gate (sealed until the reveal) toward Blackfall Gorge's top. Down: the Scar Descent onto the Scar's north wall.

**Creatures.** *Needs a model:* ice sentries, snow leopards, the forgotten. No birds above the snowline.

**Sound.** Wind; ice creak; silence.

**Points of interest.** The Hidden Gate · The Frozen Watch (Bruiser) · The Cairn of Names · The Scar Descent · *The Last Lantern*.

### Run: The Quiet Archive (Lv 92–100 · 3 floors)

**The vibe.** Every stolen map and record of the homeland kept in one place: a hall of shelves, a round room with a carved relief of the homeland, and a sealed vault of erasure.

**Concept prompt.** *Interior of a hidden archive carved into ice and rock: a 60 m hall of tall wooden shelves of scrolls and maps, paper lanterns, frost on the floor; a round room with a carved stone relief map of a valley homeland; a sealed vault door. Synty POLYGON low-poly, ink outlines, comic shading.*

**Palette.** Paper `#EEE8DA` · dark wood `#3A2A22` · ice `#BFE3F2` · lantern amber `#F0A94A`.

**Layout.** Three floors: the Stacks (60 m) → the Map Room (round, 30 m) → the Vault of Erasure (20 m).

**Lighting, shadows and fog.** Lanterns on paper, cold blue from the ice walls; the vault dark.

**Structures and assets.** Walls and alcoves (`SM_Bld_Wall_*`, `SM_Bld_Alcove_*`), cabinets and book holders (`SM_Prop_Cabinet_*`, `SM_Prop_BookHolder_*`), ice sheets.

**Creatures.** *Needs a model:* archive keepers; the boss.

## The Scar

![Concept art: The Scar](art/concept-scar.jpg)

**Contested: every faction.** ALWAYS-ON PVP · the wound between all four lands — Yanggok Valley's seat in Krytical Nova (Lore Bible §8). Lv 95–108.

| Look and feel | |
| --- | --- |
| Tone | ash, glass and aurora. Every gate meets here; nobody owns it for long. Siege Nights and Beacon Wars are fought on this ground. |
| Palette | Rupture Violet, Soul Aurora, Ash, Glass, Blood. Violet aurora over black glass. Its link to the original catastrophe is deliberately undecided — do not canonize it here. |
| Synty packs | Sci-Fi Worlds (core, cracked edges, cloud rings) · Dark Fantasy (basalt, landslides) · Maps Sci-Fi Outpost (barricades) · Dark Fortress (ruined walls) · Dungeon Realms (the titan) · Apocalypse Wasteland; Arid Desert lava and cracked ground |
| Run flavour | the Ruptureworks — the ruined extraction machinery itself, deeper than any faction Run. |

### How The Scar is set up today

**The painting** (above, the owner's Figma) is the target: a wide valley between teal-grey cliffs under a lilac-violet sky full of aurora curtains; in the middle a colossal ruined extraction machine, a lattice digger like a bucket-wheel excavator, toppled with its booms pointing at the sky and smoke rising from its base; at the left end a gate in the cliff under fire and a spiked black-and-red Null tower with orange windows; at the right a Hegemony fortress with green-lit windows and searchlights and its own gate under fire; behind the machine a tall cyan-violet crystal (a Beacon); on the floor tanks with red banners, fighter craft streaking blue, Marines in ranks, skeletons and robots advancing.

**The built map** (first pass, 19 Oct 2026) has the shape and none of the look:

- A long valley 3 km by 1.5 km with smooth walls in one dark maroon-black, no rock detail, no ledges.
- The default **daytime blue sky** with small white clouds: the violet aurora sky is not set up.
- A dark red-brown floor with **flat tan discs** everywhere (the ash drifts came out ochre, not grey) and **hot-pink glass shards** on walls and floor (the glass, far too saturated).
- A thin **crimson seam** (the lava river) down the middle.
- Where the Severed Titan should lie, a **small cluster of brown boulders**: its pieces at far too small a scale.
- One small lattice tower (a Beacon site); no gates dressed, no Glass Cliffs, no Ruptureworks portals, no aurora.

**To decide before the art:** the painting's central wreck is an **excavator** (machinery), the kit built a **golem titan**. My suggestion: the painting's machine, with an operator cab that reads like a head so the name "Titan" still fits; it says "the Hegemony dug here" without deciding whether this is the original wound.

### Zone: The Scar (Lv 95–108 · always-on PvP)

**The vibe.** The wound where all the lands meet: a valley of black glass and grey ash between shattered cliffs, an extraction machine the size of a town lying broken across the middle, violet aurora so bright it lights the ground, and at every end a faction's gate breathing soldiers into the same killing ground.

**Concept prompt.** *Epic wide shot from a high ridge down a 3 km valley of fused black glass and drifts of grey ash between shattered slate-teal cliffs. Across the centre lies a colossal broken extraction machine, a lattice excavator 900 m long, booms snapped and pointing at the sky, its open chest glowing violet where portals shimmer underneath. Three fortified gates at the valley ends and the south wall, each in its faction's style: a grey Hegemony blast wall with searchlights, a carved Covenant stone arch with dragon heads and braziers, a black Null iron gate with red banners. Three ruined lattice towers on mounds, each crowned with a glowing crystal Beacon. Cracks in the glass glow faint crimson. The sky is a dome of violet and green aurora over deep indigo night. Small figures of three armies clashing on the ash flats. Synty POLYGON low-poly, ink outlines, flat colour, halftone shadows, comic-book finish, epic scale.*

**Palette.** Glass black `#141218` · ash grey `#8E8C8A` · cliff slate-teal `#4E6466` · Rupture violet `#9B5BE0` · aurora green `#3FAE8C` · seam crimson `#B32234` · gate accents: Hegemony yellow `#F2C230`, Covenant gold `#E6A93A`, Null red `#A8232B`.

**Tone.** Epic, hostile and beautiful. The end of the map and the centre of the world at once: the most dramatic sky in the game over the bleakest ground.

**Sky, day to night.** The Scar keeps its own sky: the one-hour day still runs, but even at noon the light is a dim violet twilight.

- *Day:* a pale violet dome with the aurora faint, like sheets of light behind thin cloud; the sun a small white disc through haze.
- *Dawn and dusk:* the aurora strengthens into curtains of violet and green; the glass floor reflects them.
- *Night:* the brightest night sky in the game, the aurora filling the sky and lighting the valley violet; the Storm Sovereign may be seen far off crossing it *(proposal: seen, never fought here)*.
- *Weather:* Clear (aurora), Overcast (grey-violet ash cloud), ash storms (Fog and wind with grey ash falling like snow), dry Thunderstorm with violet lightning. No rain, no green life.

**Lighting, shadows and fog.** Violet from above, faint crimson from the glass seams below: two-colour lighting. Deep blue-violet shadows. Low grey ash haze drifting along the floor; ash storms blot out the far ends. Each gate lights its end in its faction's colour (white searchlights, gold braziers, red lamps), so a player always knows which end is which.

**Hills and verticality.** A valley 400 m deep: shattered cliffs on both walls, the Glass Cliffs (sniper ridges 250–400 m up the north wall); the floor broken into glass plates, ash drifts and low ridges; the titan lying across the middle as a crossing 30–60 m above the floor; Beacon mounds 20 m high.

**Navigation.** Every gate opens onto the same ground. The titan is the central crossing and the high ground; the Ash Flats between the Null gate and the titan are the open killing ground; the Glass Cliffs give height and long sight lines; the Beacons pull the fight to the flanks. The Ronin descent sits high on the north wall, sealed: a dark notch in the cliff, nothing more, until the reveal.

**Trees and foliage.** Nothing living: black glass, ash, cracked edges, crystal growths round the Beacons.

**Ground, hills and rocks.** A black glass ground layer, ash drifts (`SM_Env_SandDunes_*` greyed), cracked edges and crater edges (`SM_Env_Cracked_Edge_*`, `SM_Env_Crater_Edge_*`), crimson seams (`SM_Env_Lava_River_*`), cliffs and landslides (`SM_Env_Cliff_Curved_Large_*`, `SM_Env_Cliff_Basalt_*`, `SM_Env_Landslide_*`).

**Buildings and walls.**

- *The Severed Titan:* 900 m long, Colossal; the kit builds it from the giant golem (`SM_Env_GiantGolem_*` at about 20×) plated with core shells (`SM_Env_Core_Shell_*`); the painting asks for a lattice excavator instead (to decide).
- *Beacon sites (3):* comms towers stripped to the frame on rock mounds (`SM_Bld_Corp_Comms_Tower_*`, `SM_Env_Rock_Large_*`), each crowned with a crystal Beacon, ringed with barricades and ruined walls (`SM_Prop_Barricade_*`, `SM_Bld_Wall_L_Damaged_*`).
- *The gates:* each faction's gate dressed like its Z4 Scar Gate: a grey corp wall with searchlights (west), a carved dragon arch with braziers (east), a black iron gate with red banners (south).
- *The Ruptureworks portals:* violet under the titan's chest.
- *War litter:* old barricades, craters, wrecked tanks and walkers from earlier battles.

**Signs and billboards.** War marks, not words: each faction's banners at its own gate, scorched kill marks, painted claims on the titan's plates (*HEGEMONY HOLDS* sprayed, crossed out by a Covenant rune, a Null circle painted over both), Hegemony serial plates on the machine. Nobody official rules here.

**Water, rivers, bridges and docks.** None: one frozen black pool in a crater. The titan is the bridge.

**Connections.** West: Gantry Ridge's Scar Gate (Hegemony). East: Barrowfell's arch (Covenant). South wall: Noct Caldera's iron gate (Null). North wall, high: the Ronin descent from the Veilpass, sealed until the reveal. Under the titan: the Ruptureworks.

**Creatures.** *In the game now:* War Frame, Hellion, Colossus (at The Scar's levels). *Needs a model:* the species of levels 97–106 and the Bruisers at 102 and 108: rupture-born things, half machine, half soul-light. Players of every faction. No animals.

**Sound.** Wind over glass; the titan creaking; war horns from every side; the aurora's chorus.

**Points of interest.** The Severed Titan · Beacon North / East / West · The Glass Cliffs · The Ruptureworks portals · *The Titan's Hand* (a climbable vista) · *Ash Flats*.

**The battle** (owner, 15 Oct 2026): 30 minutes, a pad in the middle held 2 minutes unbroken, winner takes all or nobody does, a repeating warning beforehand, a world countdown only for the last 10 s, respawn by the side's entrance portal. Schedules and objectives of Beacon Wars stay open.

### Run: The Ruptureworks (Lv 105–112 · 4 floors)

**The vibe.** The ruined extraction machinery itself, deeper than any faction's Run: a hall of soul conduits, three separation tanks over a gantry, a ring chamber where the cycle was broken, and a cavern with no floor where platforms float over a sea of soul-light.

**Concept prompt.** *Interior of colossal ruined soul-extraction machinery: a ring-shaped chamber 70 m across round a cracked core held in struts and chains, violet light pouring from every seam, glass tubes and conduits; beyond, a floorless cavern with floating rock platforms over a glowing violet sea of light. Synty POLYGON low-poly, ink outlines, comic shading, epic scale.*

**Palette.** Violet `#9B5BE0` · machine black `#141218` · soul cyan `#8FE6E0` · crimson `#B32234`.

**Layout.** Four floors: Intake (an 80 × 60 m hall of soul conduits) → Separation (three 30 m tanks over a gantry) → the Cycle Chamber (a 70 m ring) → the Rupture (a cavern 100 m across with no floor).

**Lighting, shadows and fog.** Violet everywhere, brighter with every floor; the last floor lit only from below; spirit mist spreading across the floors.

**Structures and assets.** Tube and pod walls (`SM_Bld_Wall_Tube_*`, `SM_Bld_Wall_Pods_*`), spirit spread, pools and ocean (`SM_Env_Spirit_Spread_*`, `SM_Env_Spirit_Pool_*`, `SM_Env_Spirit_Ocean_*`), refinery silos as tanks, bridges, the core and struts, hell-ring chains (`SM_Env_Hell_Ring_Chains_*`), floating rocks.

**Creatures.** *Needs a model:* the species of levels 106–112; the bosses are the owner's own system. The Rupture's tie to the original catastrophe stays undecided (Lore Bible §8): this dressing shows extraction machinery, not the first wound itself.

## Siege Night: the hourly war

**Siege Night** is the Lore Bible's name for the scheduled war (Twelve Sky 2's Regular Warfare). The owner's brief (25 Oct 2026): a war **every hour**, split by **level brackets** the way the gear breaks down, so a war is between players of nearly the same level; fought on **small maps** with **four walled areas** that keep everyone shut in until it starts; when it starts **the gates open** and the fight is **in the middle**; the dead **respawn where they came in**. None of it is built. Names, brackets and timings below are *(proposals)*; what wins is open.

The decided war numbers still hold (build list row 28): **33 a faction** at most, open with **no more than 5 missing** on a side, always open while one player can fight, **Mercs** to even the sides.

**Where: the Siege Grounds.** Five small battlefields, old border forts where the factions have fought at nightfall for centuries. They are not on the world map and are not walked to: a player enters from the **Marshal** (the NPC meant to send players to battles: in Concord's plaza today, in each Home City's barracks in the atlas) and comes back where they left.

**When: every nightfall.** One game day is one real hour, so night falls once an hour, and Siege Night starts at nightfall:

| When (game clock) | What happens |
| --- | --- |
| Dusk begins (about 10 real minutes before) | a repeating notice to everyone in the bracket: "Siege Night at nightfall" |
| Last 3 minutes of dusk | entry open at the Marshal; the queue is split into even wars of 33 a faction |
| Nightfall | everyone in their faction's walled Muster Yard; 1 minute of muster with the gates shut |
| The gates open | 15 minutes of battle on the Siege Ground |
| The end | results, rewards, everyone back where they entered |

The battlefields are **night maps** with their own fixed light (the clock does not run inside them): that is what makes each one recognisable.

**The brackets: ten levels, the way the gear breaks down.** Ten is the step of Rare gear (45, 55 … 105), Twelve Sky 2's Siege Night bracket, and the span a Squad allows (±10): within one war the gap is at most nine levels, at most one Rare gear tier. Under level 10 there is no Siege Night. Ten brackets share five battlefields, one per band of the gear ladder, so the battlefield grows up with a player's armour; each bracket's war is its own server copy.

| Bracket | Levels | Gear in play | Battlefield |
| --- | --- | --- | --- |
| I | 10–19 | Common | Hollowgate |
| II | 20–29 | Common, first Uncommon | Hollowgate |
| III | 30–39 | Uncommon | Cinderford |
| IV | 40–49 | Uncommon, first Rare (45) | Cinderford |
| V | 50–59 | Rare I | The Saltglass Redoubt |
| VI | 60–69 | Rare I | The Saltglass Redoubt |
| VII | 70–79 | Rare I top (75) | Stormbreak Bastion |
| VIII | 80–89 | Rare II (85) | Stormbreak Bastion |
| IX | 90–99 | Rare II (95) | The Crucible |
| X | 100–112 | Rare II top, Epic | The Crucible |

**The shape every battlefield shares.**

- **Size:** about 360 × 360 m inside an outer wall, a little less than a Run map; a run from a gate to the middle takes about 15 seconds.
- **Four Muster Yards,** one on each side (north, east, south, west), each about 50 × 40 m with walls 10 m high. A faction spawns in its own yard, waits there during the muster minute, and comes back there when it dies.
- **The Siege Gate** of each yard: 12 m wide, facing the middle, shut until the war starts (a blast door for the Hegemony, a timber-and-iron gate for the Covenant, a portcullis for the Null), with a wide stair down to the field.
- **A rampart walk** on each yard's inner wall to look over the field and the other gates during the muster minute.
- **A respawn shrine** at the back of each yard, a faction landmark that says home: a cyan soul-pylon, a rune-stone with a brazier, a red-lit bone obelisk.
- **The fourth yard** stands sealed at launch: rubble-choked, chained, no crest, no banner, dark. It is the Ronin's, opened only by the expansion; until then it says nothing of who it was built for (Lore Bible §7). Which side it stands on changes by battlefield.
- **The Siege Ground,** the middle, about 200 m across: open enough to see the whole fight, broken by waist- to head-high cover, with one **heart** (the battlefield's central landmark and high ground) that fights pull toward. It can hold a capture pad, and every yard's shrine stands where it could be attacked, so any win rule fits.
- **Lighting rule:** night, a strong key light from one side (moon, fire, floodlights or lightning) so silhouettes read; each yard lit in its faction's colour (Hegemony white-cyan floodlights, Covenant gold braziers, Null red lamps, the sealed yard dark), so a player tells the yards apart from the middle in one look.

### Hollowgate (brackets I and II, Lv 10–29)

**The vibe.** A ruined toll fortress at an old crossroads in moonlit grassland: four broken gatehouses round a grassy square, a fallen toll-tower in the middle, bonfires at every gate and a full moon so bright it throws shadows. A first war should feel like an adventure, not a massacre.

**Concept prompt.** *Night, three-quarter top-down view of a small ruined stone fortress at a crossroads in rolling moonlit grassland. Four walled courtyards on the four sides, each with a gate: one with grey steel blast walls, white floodlights and yellow banners; one with timber palisades and stone, gold braziers and gold banners; one with black iron, red lamps and red banners; one sealed with rubble and chains, dark. In the centre a grassy square round a fallen stone toll-tower lying in pieces, an old well, broken carts, low walls. A huge full moon, deep blue night with stars, silver light and long blue shadows, orange bonfires at the gates, fireflies. Synty POLYGON low-poly, ink outlines, flat colour, comic finish.*

**Palette.** Moon silver `#C9D6E8` · night blue `#1F2E4A` · grass `#4F7A3E` · stone `#8A8478` · bonfire orange `#E8862E` · faction accents yellow `#F2C230`, gold `#E6A93A`, red `#A8232B`.

**Tone.** Clear, readable and exciting: a schoolyard of war. Bright for a night map.

**Sky.** A huge full moon high in the south, many stars, a few silver-edged clouds. Weather fixed: Clear.

**Lighting, shadows and fog.** Strong silver moonlight with crisp blue shadows; warm bonfire pools at each gate; a thin mist in the grass at the edges only.

**Hills and verticality.** Flat grassland; the toll-tower's fallen pieces make low ridges; the well-house a 3 m mound.

**Navigation.** Straight lines from each gate to the heart with low walls and wrecked carts as cover; the fallen tower splits the middle into two lanes.

**Trees and foliage.** Long grass and wildflowers, lone oaks outside the walls (seen over them), ivy on the ruins.

**Buildings and walls.** Old grey stone walls, each yard rebuilt in its faction's style; the fallen toll-tower; the well-house; a broken toll arch over the old road.

**Signs.** A weathered toll board with the old prices; faction banners in each yard; carved road marks at the crossroads.

**Water.** The old well in the middle; a stream outside the walls.

**The heart.** The **Toll Stone**: the fallen tower's base, a raised stone platform with the old toll bell lying on it.

### Cinderford (brackets III and IV, Lv 30–49)

**The vibe.** A burned river fort on a ford: a shallow river runs straight through the middle of the Siege Ground, crossed by a stone bridge, a timber bridge and the ford itself; the fort's wooden towers still smoulder, embers float in a red night, and it is raining.

**Concept prompt.** *Night in the rain, three-quarter view of a small burned river fortress. A shallow fast river cuts straight across the middle of the square battlefield, crossed by an arched stone bridge, a narrow timber bridge and a wide rocky ford with water foaming over stones. Charred timber towers glowing with embers, red-orange light on wet stone and water. Four walled courtyards round the field in faction styles (grey blast walls with floodlights, timber and stone with braziers, black iron with red lamps, one sealed and dark). Smoke and floating embers under dark red-brown cloud lit from below. Synty POLYGON low-poly, ink outlines, comic finish.*

**Palette.** Ember red `#D2452E` · charcoal `#2A2522` · wet stone `#5E615F` · river teal `#2D4A4E` · smoke grey `#6B6560` · rain silver `#BFC6CC`.

**Tone.** Gritty and intense: the war gets serious. Wet, smoky and loud.

**Sky.** Low cloud lit red-orange from the fires, no stars, rain. Weather fixed: Rain and smoke.

**Lighting, shadows and fog.** Warm red-orange firelight from the burning towers, cold wet reflections, smoke banks across the river, soft shadows.

**Hills and verticality.** The river 2 m below the field in a shallow cut; the stone bridge arches 4 m over it; muddy banks.

**Navigation.** The river is the line: every fight is about the crossings. The stone bridge high and narrow, the ford wide and slow, the timber bridge quick.

**Trees and foliage.** Burned trees and reeds on the banks, wet grass, mud.

**Buildings and walls.** Charred timber palisades and towers round a stone keep; the three crossings; a burned mill with its wheel still turning.

**Signs.** A scorched ferry sign, a carved ford-depth post, faction banners hanging wet.

**Water.** The river, the centre of the map: knee-deep at the ford, fast and foaming, dark teal mirroring the fire.

**The heart.** The **Ford Stones**: a broad rock island in mid-river where all three crossings meet.

### The Saltglass Redoubt (brackets V and VI, Lv 50–69)

**The vibe.** A pale star-shaped fort on a dead salt flat under a vast moon: white crust cracked in hexagons, thin pools of brine like mirrors, black silhouettes of fighters against glowing white ground. The cleanest, most graphic night in the game.

**Concept prompt.** *Night on a vast white salt flat cracked into hexagon plates, shallow mirror-still brine pools reflecting a huge moon and stars. A low star-shaped fort of pale stone with four walled courtyards on its sides in faction styles (grey blast walls with white floodlights, timber and stone with gold braziers, black iron with red lamps, one sealed and dark). In the centre a white obelisk on a stepped platform. Figures as black silhouettes against the glowing ground. Synty POLYGON low-poly, ink outlines, high-contrast comic finish.*

**Palette.** Salt white `#F1EEE6` · moon blue `#BFD3EA` · night indigo `#1C2340` · brine mirror `#9FB7C9` · pale stone `#D2CBBE` · silhouette black `#101014`.

**Tone.** Stark and elegant: a duel on a white table. Everyone is seen.

**Sky.** A vast clear sky, a huge low moon, dense stars, the Milky Way. Weather fixed: Clear.

**Lighting, shadows and fog.** The ground itself seems to glow; figures read black; long hard shadows; a faint white haze at the horizon.

**Hills and verticality.** Dead flat on purpose; only the fort's angled bastion walls (6 m) and the stepped platform (5 m).

**Navigation.** Wide open, cover rare (salt-crusted wagons, low angled walls): the bastions and the platform are everything.

**Trees and foliage.** Dry salt-bush tufts at the edges, a dead tree crusted white.

**Buildings and walls.** A pale stone star fort with angled bastions, a collapsed salt works, salt-crusted wagons, the obelisk.

**Signs.** Faded salt-works signs; faction banners stiff with salt.

**Water.** Ankle-deep brine pools, perfect mirrors.

**The heart.** The **Saltglass Obelisk**: a white obelisk on a stepped platform that catches the moon.

### Stormbreak Bastion (brackets VII and VIII, Lv 70–89)

**The vibe.** A cliff-top fortress in a storm that never ends: rain driven sideways, lightning every few seconds lighting everything white, a broken keep on a rock outcrop in the middle, the sea crashing two hundred metres below. The first battlefield with real height.

**Concept prompt.** *Night in a violent thunderstorm, three-quarter view of a fortress on a sea-cliff headland, rain blowing sideways, a lightning bolt lighting the scene white. Four walled courtyards in faction styles round a rocky battlefield that rises to a broken stone keep on an outcrop in the centre, with ramps, stairs and ledges at several heights. Waves exploding white against the cliffs far below. Synty POLYGON low-poly, ink outlines, comic finish, dramatic.*

**Palette.** Storm slate `#2E3640` · lightning white `#F4F7FF` · sea foam `#DCE6EA` · wet stone `#4F565E` · torch gold `#E6A93A`.

**Tone.** Dramatic and veteran: the war at its most heroic.

**Sky.** Black-blue storm cloud, constant lightning, no moon. Weather fixed: Thunderstorm.

**Lighting, shadows and fog.** Darkness broken by lightning flashes with hard shadows; the yards' lights the only steady light; rain streaks; spray mist at the cliff edge.

**Hills and verticality.** The field rises in tiers from the yards to the keep (12 m up); ledges, stairs and ramps at three heights; a cliff edge on one side (a fall is death).

**Navigation.** Climb to win: lower routes round the outcrop are slow with cover, the stairs fast and exposed.

**Trees and foliage.** Wind-bent pines, wet heather in crevices.

**Buildings and walls.** A broken stone keep, crumbling walls, a collapsed lighthouse, wooden scaffolds.

**Signs.** A lighthouse keeper's warning bell; faction banners whipping in the wind.

**Water.** The sea below, heard more than seen; rain pooling on the stones.

**The heart.** **The Broken Keep**: the keep's open top floor on the outcrop.

### The Crucible (brackets IX and X, Lv 90–112)

**The vibe.** A sunken amphitheatre cut into the black glass at the rim of The Scar under the violet aurora: tiers of fused glass stepping down to an arena floor, four gates in the tiers, braziers burning violet, and on the horizon the broken extraction machine against the sky. The last and grandest Siege Ground.

**Concept prompt.** *Night under a violet and green aurora, a large sunken amphitheatre carved into black volcanic glass at the rim of a vast valley. Concentric tiers of glossy black glass step down to a round arena floor cracked with faint crimson seams. Four gated courtyards set into the tiers in faction styles (grey blast walls with white floodlights, a carved stone arch with gold braziers, black iron with red lamps, one sealed and dark). Tall braziers burning violet. In the distance over the rim, a colossal broken extraction machine silhouetted against the aurora. Synty POLYGON low-poly, ink outlines, epic comic finish.*

**Palette.** Glass black `#141218` · aurora violet `#9B5BE0` · aurora green `#3FAE8C` · seam crimson `#B32234` · ash grey `#8E8C8A`.

**Tone.** Grand and final: the best of every faction in one arena.

**Sky.** The Scar's aurora at full strength. Weather fixed: Clear.

**Lighting, shadows and fog.** Violet from the sky and braziers, crimson from the seams; deep violet shadows; thin ash haze.

**Hills and verticality.** A bowl: the yards on the upper tiers, the arena floor 15 m below; the tiers are terraces to fight down.

**Navigation.** Down and in: every gate leads down the tiers to the floor; the tiers give height to whoever holds them.

**Trees and foliage.** None.

**Buildings and walls.** Glass tiers, violet braziers, broken barricades on the floor, a ring of shattered columns.

**Signs.** Kill tallies scratched into the glass by generations of fighters; faction banners.

**Water.** None.

**The heart.** **The Crucible Floor**: a raised round dais in the arena's centre with a crack of crimson light across it.

## Rules the layout follows

How the places line up with what is built, so the atlas does not fight the Hostiles plan or the Lore Bible.

**Levels.** A Home City's walls hold no Hostiles and no PvP; its outskirts on the same map are Lv 1–20; field zones Z1–Z4 are 18–40, 38–60, 58–80 and 78–100 (owner, 25 Oct 2026); The Scar 95–108; the Ruptureworks 105–112. One species per Hostile Zone, levels rising with distance from the entry, Bruisers at the far edge. Z4's gate is the only way into the Scar.

- Runs grow with the band: 1 room → 2 rooms → 2 floors → 3 floors; the Ruptureworks is 4.
- Each region's Runs share one flavour so a Null cave never feels like a Covenant one.
- Every zone names a Wayfinder (Transit) and one Bruiser lair, matching the NPC glossary.
- Naming follows the factions doc: corporate-cold / norse-elven / designations-and-endings / veil-and-ember.
- Foliage escalates by region: manicured → wild (Hegemony); meadow → forest → snow (Covenant); nothing but growths and crystal (Null); steam → bamboo → spray → ice (Ronin).

**Open for the owner.** Ronin colours and crest; whether the Scar's rent is the original extraction site; the names of the Beacon sites; the Scar's wreck (excavator or golem titan); Siege Night's names, brackets, timings and what wins; the creatures marked **needs a model**; growing the Home City maps to about 2.4 km for their outskirts.
