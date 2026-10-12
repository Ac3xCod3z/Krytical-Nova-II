# Crew Halls: guild housing, building first and server second

Written 11 Oct 2026. Row 62 of `../Reference/12Sky/01_Systems_Build_List.md`.

**As built, 11 Oct 2026: Stage 1, steps 1 to 3** (the Lot, furnishings and the Crate, Arrange on the lawns).
What it does, its numbers and how it was checked are in `../Reference/12Sky/05_Economy_And_Social.md`,
"Crew Halls". To try it: Play, press **L** and use GO TO MY HALL (or `gm.home`), fill the Crate with
`gm.furnish.kit`, then click a furnishing and click the lawn. Where the build differs from the plan below:

- The Lot's NavMesh is made when the Lot is set down, not baked into the prefab (a baked one comes with the
  Crew Quarter maps).
- The Hall on the pad is the vendor's own building for the faction, shrunk to fit; baked Facades of one
  footprint are step 4.
- A piece is one object per furnishing with no pooling yet; 73 pieces cost nothing measurable.
- The Furnishing Crate is saved with the character. Stage 2 needs it where an owner who is offline can be
  given pieces back.
- There is no screen fade on the way to the Lot, and the minimap is not hidden there.
- The faction rule is on the item itself (`ItemDefinition.boundFaction`, `ItemFactionRules`), so Trade,
  Kiosks, the Deep Vault and pickup ask it without knowing about housing.

Next: step 4 (the room inside, Finishes, Facades), then step 5 (the comfort set).

## The order (owner, 11 Oct 2026)

The finished system lives on the server. Before any of that is built, the building mechanics are made
playable on their own, so they can be tried and changed while they are cheap to change:

- **Stage 1, the Hall sandbox.** One Lot with a Crew Hall on it that a player can walk around, enter and
  decorate. It runs offline or as host with no new networking, and it saves between sessions. Five steps,
  each one playable.
- **Stage 2, shared Halls.** Crew Quarters as maps with Lines, Lots claimed by Crews, the Crew Fund, who may
  decorate, visitors, pieces going back to their owners, vendors and drops. It is planned against
  `online-roadmap.md` when the owner asks for it, not before.

What keeps Stage 1 from being thrown away: the decorating tool never changes a layout itself. It hands
every edit to a **layout authority** and draws what comes back. In Stage 1 the authority is local; in
Stage 2 it is the server. The layout, the rules, the catalog, the Crate, the tool and the windows stay.

## Decisions

Owner, 10 and 11 Oct 2026:

- Nova Store furnishings are tagged with their source now and sold when that store exists (row 32).
- A **Crew Fund** that members pay into buys the Lot, the Facades and the upgrades.
- A Crew that goes quiet has its Hall packed away and its Lot freed. Every furnishing goes back to the
  player who placed it, and setting the Hall down again costs the Lot again. No upkeep.
- A faction's Crew Quarter is for that faction only. Each Crew sets its own door. No fighting there.
- Build the playable mechanics first, the server part after (this document's order).
- **A faction's furnishings are for that faction only** (11 Oct 2026). A character has access only to the
  pieces of its own faction's theme. They are never traded to a character of another faction. In a
  Kiosk another faction can see them but cannot buy them, on neutral ground too. They are kept in the
  Vault, which is one character's, and never in the Deep Vault, which an account's characters of any
  faction share. Facade Deeds and Finishes follow the same rule.

From the request itself: neighbourhoods in channels with a set number of Lots each, bought pre-built
outside looks, front and back lawns decorated freely, walls, floor and ceiling each changed whole by one
item, the inside decorated freely, and decorating that is as comfortable as The Sims.

## Names (proposals, not reviewed)

| Term | Meaning |
| --- | --- |
| **Crew Hall** | a Crew's home (the word is already on the Vaultkeeper's sign) |
| **Crew Quarter** | a faction's housing map; the three Quarters' own names are the owner's |
| **Line** | one running copy of a Quarter, as for every other map with Lines |
| **Lot** | a plot in a Quarter |
| **Furnishing** | an item that can be placed |
| **Furnishing Crate** | a character's own store of furnishings, apart from the Pack |
| **Arrange** | the decorating mode |
| **Facade** | a whole outside look of a Hall |
| **Finish** | an item that changes every wall, the floor or the ceiling of a Hall at once |
| **Crew Fund** | the Crew's shared Creds |

## What other games do, and what is taken

| Taken | From | Changed |
| --- | --- | --- |
| Copies of one neighbourhood, a set number of plots each | FFXIV wards, LOTRO | A new Line opens when all are full: no lottery, no shortage |
| No upkeep, guild neighbourhoods, moving keeps the layout | WoW Midnight | A quiet Crew's Hall is packed, not kept and not demolished |
| Basic and Advanced placing, items that ride on furniture, a budget bar | WoW Midnight, WildStar | One tool for both; the budget shows from the first piece |
| Undo, linked pieces, fine nudging | ESO | Plus what ESO players need an add-on for: duplicate, group move, eyedropper |
| Grid steps, free placing on Alt, repeat on Shift, walls down | The Sims 4 | The same comfort set, later shared between Crew members |
| Storage that is not the bags | FFXIV, Path of Exile | A Crate per character, because a piece stays its placer's |
| Bigger on the inside | WoW Midnight | The inside is its own room, so a Facade changes without touching it |

Left out on purpose: a fixed supply of plots, lotteries, demolition, a weekly fee, hidden limits, fixed
hooks, small outdoor budgets.

## Stage 1: the Hall sandbox

It needs no server because nothing in it is shared: one player, one Lot, the layout saved on the same
machine through `ServerSave` (which already answers when hosting or offline). A client of someone else's
game is told the sandbox is for the host until Stage 2.

The Lot is a prefab set down far from the starter town in the same scene (beyond x = 6500, outside the
camera's reach of the town), with its own baked NavMesh, reached and left behind a screen fade. The Lot,
the Hall and the room are prefabs that do not know which map they are on, so they move to a Crew Quarter
map unchanged.

| Step | What can be played at the end | Main pieces |
| --- | --- | --- |
| **1. The Lot** | `gm.home` takes the player to a fenced Lot with a Hall on its pad, a front and a back lawn; `gm.home.leave` returns to town | `HousingSettings`, `HousingLot`, `HousingSandbox` (sets the Lot down, travel), `HousingLotBuilder`, `ScreenFadeController`, `NetworkPlacement.TryPlace`, layers `Furnishing` and `HousingShell` |
| **2. Furnishings and the Crate** | A starter set from each faction's packs exists as real items with their own icons; `gm.furnish.kit` fills the Crate with the character's own faction's pieces; the Crate window lists them with search and filters; Pack and Crate exchange pieces; another faction's piece is refused by the Crate and by Trade, a Kiosk shows it but will not sell it, and the Deep Vault takes no furnishing at all, each with its reason | `FurnishingDefinition`, `FurnishingCatalog`, `FurnishingCatalogBuilder` + tables, `FurnishingIconCapture`, `PlayerCrate`, `CrateWindowController`, `FurnishingRules` (the faction lock), its calls in Trade, Kiosk and the two vaults |
| **3. Arrange on the lawns** | Pick from the Crate, a ghost follows the cursor, green or red with the reason; grid and finer steps, 15° turns, items ride on tables, rugs lie flat; pick up, move, put away; budget bar; undo and redo; the layout is there next session | `HousingLayout`, `HousingRules`, `HousingPlacement`, `ArrangeHistory`, `IHomeAuthority` + `LocalHomeAuthority`, `HomeView`, `FurnishingPool`, `ArrangeController`, `ArrangePaletteController`, two park flags in `PointAndClickPlayer`, key in `KeyBindings` |
| **4. Inside, Finishes and Facades** | The door leads into the Hall's room; walls drop out of the camera's way; lamps and candles light it; rain and sky stay outside; a Finish changes all walls, the floor or the ceiling; a Facade changes the outside | `HousingInteriorTemplate`, `HousingDoor`, `InteriorCutaway`, `HousingLights`, `InteriorPresentation` + `InteriorLook` (World), `SurfaceKitBaker`, `FacadeBaker`, Hall panel |
| **5. The comfort set** | Advanced mode (move, turn and scale on every axis, no collision), eyedropper, duplicate, repeat on Shift, box select with group move, an overhead camera for the lawns, favourites and recents, put away all | `ArrangeGizmo`, additions to the tool and palette |

Checks along the way, each a short measurement and not a feature: 600 and 2,400 pieces on screen in a
build (step 3, `gm.home.fill`); the inside look at noon, at night and in a storm (step 4); the automatic
sorting of props into floor, wall, ceiling, table-top and lawn pieces, read through as a table before
icons are made (step 2). Facades and Finishes are chosen by the owner from contact sheets before they are
baked (step 4).

### How the pieces are made

- **Furnishings are items.** A `FurnishingDefinition` belongs to an `ItemDefinition` id, so a piece is
  carried in the Pack and dropped, picked up, traded and sold like any other item. Id blocks: 20000 to
  22999 Hegemony, 23000 to 25999 Null, 26000 to 28999 Covenant, 29000 to 29499 Finishes, 29500 to 29999
  Facade Deeds.
- **Each one belongs to a faction, and the lock is one rule.** `ItemDefinition` has no faction today, so
  the faction is the furnishing's own, and `FurnishingRules` answers one question for every system: may
  this character have this piece. It is asked when a piece is placed or put in the Crate, when it is added
  to a trade (the other side's faction), when a Kiosk sale is made (the buyer's faction, on neutral ground
  too; the listing stays visible to everyone and says who may buy it), and when it is stored (the Vault
  takes it, the Deep Vault does not, so a piece never reaches an alt of another faction). The lock comes
  in with step 2, because from then on the pieces can change hands.
- **The catalog is built, not hand-made.** The builder reads each Synty prefab without changing it and
  works out its size, where it mounts (floor, wall, ceiling, on a surface, rug, lawn only), whether it
  lights, its budget cost and its parts. A table of hand corrections covers what the names get wrong.
- **Icons are pictures of the model**, taken with the rig the gear icons use, kept in the housing folder
  so the Icon Library's checks stay as they are. A test holds every furnishing to an icon of its own.
- **A layout is data.** A placed piece is a short record: its own number, its parent's, the item, a
  position to 1/64 m from the Lot's or room's corner, three angles, a scale, a colour variant, flags and
  its owner. Pieces on a table are stored relative to the table, so moving the table moves them.
- **Drawing** uses pooled plain renderers on their own layer, with colour variants taken from the packs'
  ready-made palette materials (no per-piece material changes), so the batching already in the project
  keeps working.
- **Pieces are walked through.** Only the Hall's shell, its pad, the fence and the door stop a player,
  with a clear space kept at each door and a "back to the door" action.
- **The server-side rules already exist as plain functions** (`HousingRules`: inside the bounds, within
  the budget, the piece is owned, the parent is valid). Stage 1 calls them locally; Stage 2 calls the same
  ones on the server. Piece against piece is never a server rule, so decorating stays forgiving.

### Art at hand

| Faction | Facades | Room kit | Furnishings | Thin spots |
| --- | --- | --- | --- | --- |
| Hegemony | Sci-Fi Worlds hab pods and corporate buildings; Samurai Empire houses (90 to 170 objects each, baked to one) | Sci-Fi Space walls, floors, ceilings | about 225 sci-fi and 158 samurai | soft furnishings; nothing in the city pack reads as a house |
| Null | none ready: put together from the Dark Fortress and Dark Fantasy kits and baked | Dark Fortress, Dark Fantasy | about 166 | kitchens, bathrooms, sofas; the garden is a graveyard |
| Covenant | 26 Fantasy Kingdom buildings (two renderers each), Viking longhouses | Fantasy Kingdom house kit, Elven Realm | about 400 | best covered |

No pack has LODs and no lighting prop holds a light; the builder gives lamps their light and switches
shadows off for small pieces. Lawns are plain ground in Stage 1 (the grass system draws only on the
starter town's terrain); grass tufts, hedges and flowers are lawn furnishings.

### Starting budgets (settings, to be measured in step 3)

| | |
| --- | --- |
| Lawn of one Lot | 200 pieces |
| Inside, Hall tier 1 / 2 / 3 | 300 / 450 / 600 pieces |
| Lights inside | 12 lit nearest the camera, one with shadows |
| Lights on a lawn | 4, at night |
| Target | 60 frames a second and never under 50 with a full Lot and a full room |

## Stage 2: shared Halls (planned later, with the online plan)

Not built in Stage 1 and not designed further until asked. What is already settled is in "Decisions".

- **Crew Quarters** are maps with Lines (online phases 5 and 6): one per faction, a set number of Lots per
  Line, a new Line when all are full. Which Line a Crew's Hall is on is world-service data, like Crews.
- **A server layout authority** replaces the local one: an edit is checked on the server and sent to
  everyone there, a piece someone is moving is held under their name, undo skips what another member has
  since changed. The record and the rules are Stage 1's.
- **Crew side**: the Crew Fund, claiming and moving a Lot, who may arrange (Leader, Officers, Members if
  the Leader allows), the door setting, events from `CrewService` for leaving, expelling and disbanding.
- **Ownership**: a piece returns to its placer's Crate when it is put away, when its owner leaves, on
  disband and when a quiet Crew's Hall is packed.
- **Sources**: a Furnisher who sells the buyer's faction's pieces, furnishings on the Hostile drop table,
  the Nova Store tag.
- **Also**: a directory of Lines and Halls, logging in at the Hall's door, the no-fighting rule, the
  Guide page.

An earlier draft put several Quarters far apart inside the one scene with its own visibility rules. The
online plan's map servers and Lines do that job, so that draft is dropped.

## Open (readings and picks of mine, not confirmed)

- Every name in the table above.
- "Cost them again" is read as the Lot only: Facades, Finishes and the Hall tier a Crew has stay its own.
- A member who leaves or is expelled takes their pieces with them.
- When a Crew disbands, what is left of the Crew Fund goes to its last Leader.
- A furnishing dropped on the ground: read as picked up only by its own faction.
- Every number here is a placeholder in a settings asset.

## How it is checked

- EditMode tests for everything that is a plain rule: layout edits and their undo, numbering and
  parenting, budgets, snapping, wall and table placing, the catalog (ids in their block, an icon each),
  and the save round trip.
- Each step is played in the Editor, offline and as host, and handed to the owner to try before the next.
- Step 3's measurement is taken in a Windows build.
- Stage 2 is checked with a second client throughout.
