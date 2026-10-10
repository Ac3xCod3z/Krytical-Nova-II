# The interface: faction skins, the title flow as a comic book, Settings

As built, 8 Oct 2026 (polish pass, workstream G). The plan this follows is `Plans/ui-faction-skins-g1-report.md`; where
what was built differs from that plan it says so here. Section 9 is the parity checklist and section 10 is what is
**not** checked or not built yet. Read section 10 before relying on this page.

## 1. In one paragraph

Each faction has one **skin asset**. The game's interface (UI Toolkit) wears the skin of the character's faction; the
title flow (uGUI, built from Synty's menu packs) exists three times, once per skin, and wears one of them for a whole
run of the game. The menus are the pages of a comic book: leaving a screen turns its page, with MegaBook. One Settings
window, with one store of values, opens from character select and in the game. Everything a player does in the menus
and in Settings is answered with Feel.

## 2. Faction skins

| Faction | In-game pack | Menu pack | Skin asset |
| --- | --- | --- | --- |
| Hegemony | Sci-Fi Soldier HUD | Sci-Fi Menus | `UI/Skins/UiSkin_Hegemony.asset` |
| Null | Dark Fantasy HUD | Dark Fantasy Menus | `UI/Skins/UiSkin_Null.asset` |
| Covenant | Fantasy Warrior HUD | Fantasy Menus | `UI/Skins/UiSkin_Covenant.asset` |

A skin (`UI/Runtime/Skins/UiSkin`) holds, for the game: the body and title fonts and a font scale, whether the base
colours are recoloured and to which accent, panel tone and label colour, and the sprites of the parts (panel, card,
token, item slot, frames, bar, pips, close and arrow icons, soft backing, tech patterns). For the menus: the pack's
screens (title, choice, list, loading), its confirm popup, its option row and body label, and the menus' accent.

**The game's interface.** The seventeen base style sheets are the Hegemony layout. `FactionSkinBuilder` (menu
**Krytical Nova ▸ UI ▸ Build Faction Skins**) reads the three assets and writes `UI/Common/KryticalSkin_<Faction>.uss`:
overrides that only apply under the class `kn-faction-<faction>`, which `FactionSkin` puts on the HUD's root for the
character's faction. The sheets are generated: never edit them. A value changed in a skin asset is written into the
sheets at once (`UiSkinWatcher`), so every screen that uses it changes with it.

Base sheets: `KryticalTheme`, `ProgressionHUD`, `Settings`, `Items`, `Forge`, `Herald`, `GameMenu`, `Minimap`,
`WorldMap`, `ChatWindow`, `DuelPanels`, `PartyPanels`, `PetPanels`, `SocialPanels`, `EmoteWheel`, `EconomyWindows`,
`NpcInteraction`. That is every in-game document: a new sheet must be added to the builder's list.

**The pieces drawn with the packs' own prefabs (the faction HUD).** A recolour of one layout is not a faction's HUD.
What the packs draw themselves is taken from them: `FactionHudBuilder` (menu **Krytical Nova ▸ UI ▸ Build Faction
HUD**) composes one uGUI prefab per faction and piece from that faction's HUD pack into `UI/HUD/Faction/<Faction>/`,
and lists them in `UI/Skins/FactionHudKit_<Faction>.asset`. Every faction has every piece (`FactionHudTests`).

| Piece | What it is | Stands on | Hegemony | Null | Covenant |
| --- | --- | --- | --- | --- | --- |
| Hotbar | Slots 1 to 0 with the XP bar; Q W E R on a small bar beside it | `#hotbar` | `ARPGBar_01` + `XPBar_01` + `HotBar_03` | `ARPGBar_02` + `HotBar_01` | `ARPGBar_05` + `HotBar_01` |
| Player plate | Live portrait, level, HP and Flux with their numbers, the walk/run line, points left | `#summary` | `Comms_01` + two `HealthBar_02` | `HealthStats_05` + `PlayerLevel_02` | `CharacterPortrait_05` + `PlayerLevel_01` + two `HealthBar_01` |
| Minimap dial | The frame round the map | `#minimap`, by its glass | `Minimap_Dial_01` | `Minimap_Dial_02` | `Minimap_Dial_04` |

How it works: `HudChrome` (on the HUD object, wired by `HudSetup`) puts the kit's pieces on one overlay canvas under
the UI Toolkit panel. A piece (`HudChromePiece`) is a view only. It stands on one of the HUD's own elements (it takes
that element's rectangle every frame, and the element takes the piece's size) and shows what the elements show: the
Hotbar reads each slot's icon, key, cooldown, count and states and the XP bar; the plate reads the HP and Flux bars, the
level, the mode line and the points line. So `ProgressionHUD` is unchanged, and everything the player does (click,
right-click, drag, drop, the layout editor, click blocking) still happens on the elements, which lose only their own
look while pieces are showing (class `hud-chrome` on the panel's root). A piece may stand by a part of itself
(`standOn`): the dial stands by its glass, and its frame reaches out round it. An ornament that lies on the glass is
drawn as an element over the map (`MinimapChromeView.overMap`: the Covenant dial's jewel).

- The packs' containers (orbs, flasks, canisters) and level badges on the action bars are switched off: HP, Flux and the
  level are on the plate only.
- Sizes are the builder's: every faction's main bar, plate and dial glass is as wide as the others' (`MainBarPanelWidth`,
  `VitalsPanelWidth`, `MinimapGlassPanelWidth` in `FactionHudBuilder`). The main bar is centred on the screen and clears
  the chat at the default sizes.
- The live portrait is `CharacterPortraitStage` (`Gameplay/Items/Runtime/Visuals/`): a stripped doll that shares the
  player's merged mesh, as the Equipment page's does, drawn by its own camera into a 256 px texture with a clear
  background. While an Equipment preview is lit it puts its own lights out (both are on the preview layer).
- Labels that follow things in the world (namecards, nameplates, loot labels, Kiosk plates, combat numbers) are UI
  Toolkit and would draw over the pieces: each hides while it is over one (`HudChrome.HideOver`).
- The plate over a Hostile is the exception since 9 Oct 2026: while the HUD wears a faction's art it is that
  faction's own (`HostilePlateView`, prefab `UI/HUD/Faction/<Faction>/HUD_<Faction>_HostilePlate.prefab`, listed in the
  kit as `hostilePlate`). **Krytical Nova ▸ UI ▸ Build Hostile Plates** (`HostilePlateBuilder`) composes it from the
  pack's `WorldSpace_EnemyInfo_01`: the pack's level badge and health bar, with three lines of ours in the pack's
  lettering (the name, 12 panel pixels tall and in the level-gap colour; BRUISER over it; TAKEN under the bar). The
  badge and bar are 118 panel pixels wide in every faction. `HostileNameplateController` decides what a plate says and
  when it shows, as before; it makes one per Hostile on `HudChrome.WorldLayer`, the first child of the pieces'
  canvas, so the pieces draw over the plates and nothing has to hide. With no faction art the plain UI Toolkit plate
  is drawn instead. Player namecards are still the plain ones.
- Not yet in the packs' art: chat, buffs, Squad frames, the Callout strip and prompts (they have the pack's small
  panel frame and buttons only).
- `FactionVariantBuilder` was a spike for the pieces a faction's own pack lacks (a recoloured prefab variant of the
  pack that has it). It was judged on 2026-10-08 and not taken: a recolour keeps the other race's outline. The rule
  is the same layout with the faction's own pack's frames and parts. Its one variant is not used by the game.

**The windows' pages (UI Toolkit, the pack's own art).** The windows stay UI Toolkit (they overlap, and a uGUI piece
is always under the panel). What a page is made of is the skin's, in the pack's own colours, never tinted:

| Part | Skin field | Hegemony | Null | Covenant |
| --- | --- | --- | --- | --- |
| Window frame, reaching out past the edge | `windowFrame` | Sci-Fi `Frame_TechyDark_01` | `Frame_Box_Large_08` | `Frame_Box_06` |
| Small panel frame (chat, prompts, tooltip) | `panelFrame` | the layout's tinted outline | `Frame_Box_Large_07` | `Frame_Box_12` |
| Ornament on the top edge (and one behind it) | `crown`, `crownBack` | `Greeble_BoxTop_02` | `Greeble_Skull_01` over `Greeble_Horns_05` | `Greeble_Curlicue_24` |
| Nearly solid backing of what is read | `windowBacking` | the dark tone, darker | the same | the same |
| Title band | `headerBand` | Sci-Fi Menus `Button_Frame_04` | Dark Fantasy Menus `Frame_Bar_Medium_06` | Fantasy Menus `Menu_Button_11` |
| Round close button on the corner | `closeButton` | `Menu_Box_Small_02` | `Ring_Small_09` | `Ring_Small_02` |
| Item cell and equipment slot | `cell` | Sci-Fi Menus `Frame_Box_Small_04` | `Frame_Box_Medium_17` | `Frame_Box_Small_03` |
| Button | `button` | Sci-Fi Menus `Frame_Box_Small_07` | `Frame_Box_Medium_01` | `Frame_Box_22` |
| Menu row button | `menuButton` | Sci-Fi Menus `Frame_Box_Small_06` | Dark Fantasy Menus `Frame_Bar_Medium_07` | Fantasy Menus `Menu_Button_05` |

**Equipment, Pack and the item card (9 Oct 2026)** are laid out after the packs' own inventory (the Dark Fantasy HUD's
`HUD_DarkFantasy_Inventory_Character_01` and `ItemPickupInfo_04`, Fantasy Screens' `Panel_FantasyScreens_Character_Inventory_01`),
the same arrangement in every faction, each in its own pack's parts (`FactionSkinBuilder.EnsureInventoryParts`):

| Part | Skin field | Hegemony | Null | Covenant |
| --- | --- | --- | --- | --- |
| What an empty equipment slot is for (a picture, not a word) | `slotGlyphs`, `slotGlyphTint` | Sci-Fi HUD `Icons_Inventory` (Weapon, Armor, Backpack, Science); gloves, boots, ring and amulet borrowed from Dark Fantasy, which the Sci-Fi pack lacks | Dark Fantasy `Icons_Inventory` `_Underlay` | Fantasy Screens `Icons_Inventory` `_Underlay` |
| Bars over and under the model | `dollBarTopLeft` / `TopRight` / `FootLeft` / `FootRight` | `Line_Horizontal_01` (one sprite, mirrored) | `Bar_03` Top / Bottom halves | Fantasy Menus `Frame_Bar_01` halves |
| A cell of the Pack's grid | `gridCell`, `gridCellTint` | the Sci-Fi HUD's glass slot `Box_Glass_04` (the packs have no inventory) | `Box_Small_Parchment_01`, dark, no frame | `Box_Medium_Parchment_01`, dark, no frame |
| The ornate frame of an equipment slot, reaching out past it | `slotFrame`, `slotFill` | Sci-Fi Menus `Frame_Box_Small_10` (the bolted, cut-corner housing) | `Frame_Box_Medium_17` on the pack's teal | Fantasy Warrior `Frame_Box_04` (steel, gold corners with gems) on blue |
| The item card's own frame | `cardFrame`, `cardBacking` | Sci-Fi Menus `Frame_Box_Large_09` | `Frame_Box_Large_02` | `Frame_Box_22_Variant_01` |
| The frame round the card's picture | `cardPictureFrame` | Sci-Fi Menus `Frame_Box_Small_05` | `Frame_Box_Medium_17` | `Frame_Box_18_Variant_01` |

- **Equipment:** the owner's name, level and lineage over the model; slots down either side, larger, with no captions.
- **Pack:** the page tabs stand on a well that holds the grid; count, SORT and Creds on one foot bar. An empty cell
  has no outline (class `item-empty`). **In the grid an item's rarity is the colour of the cell it lies on**, not a
  square outline (the packs' grids have none); the outline is kept for a pointer over a cell and for drops. On an
  equipment slot the rarity tints the slot's ornate frame, which is grey while the slot is empty.
- **Item card** (the tooltip, everywhere an item is hovered): the item's picture on the pack's slot lit in its rarity,
  the name in the title face, what it gives as one strip a line, an EQUIPPED flag on worn pieces, and the hint as
  mouse prompts. The texts are the ones the tooltip had.

**Every other window** takes the same parts through what the windows share, so nothing is laid out per window:

- A card (`.kn-card`: a section of Character, a skill's card, a panel of Trade or the Kiosk) wears the pack's ornate
  frame (`cardPictureFrame`) as a layer `SyntySkin.Decorate` adds (`kn-card-frame`); a skill's tile wears the slot
  frame (`kn-token-frame`).
- The heading of a section (`.eyebrow`) has the pack's bar under it (`kn-section-bar`, the bars of the paper doll),
  where the heading has a line to itself.
- Every grid of items (`.item-cell` in the Vault, Trade, the Kiosk, the shop, the forge's bench) is the pack's plain
  cell, with the rarity as the cell's colour.
- The game menu and the small dialogs over a page (an item's menu, a confirmation, an amount, a player's menu) wear
  the item card's frame.
- A number of the character (`.combat-stat-row`) is a strip a line.

A UI Toolkit background draws one image, so a part that is a frame over a fill is flattened into one picture
(`SpriteRecolourer.Flatten`, into `UI/HUD/Faction/<Faction>/Art/`) the first time the skins are built; **Krytical Nova ▸
UI ▸ Rebuild Faction Skin Art** makes them again. The builder writes the rules after the converted sheets (the "window
chrome" section of each `KryticalSkin_<Faction>.uss`); they name a part through its parent, so they win over the tinted
look. `SyntySkin.Decorate` adds the two ornament layers to windows. A cell's rarity outline is drawn over the pack's cell
and keeps its colours; a button's state only lightens or darkens its art. Nothing a window does, and no element name,
changed.

**Colours that mean something are not skinned.** Selectors with `.rarity-` (rarity colours) and `-bubble` (the pets'
comic bubbles) are left out of the recolour; `UiSkinTests` holds this. A namecard's name is coloured in code from the
faction's own accent, and red for a Callout opponent (`PlayerNamecardController`), outside the sheets. The names on
nameplates are not recoloured by any skin (only their font and size change). No name colour by level gap was found in
the project, so there was none to protect: if one is added, give it a `.rarity-`-style exemption in the builder.

## 3. Which skin the menus wear

`MenuSkin` picks one faction at random when the game starts and keeps it for that run. It changes at the next start,
or at once when the player changes **Settings ▸ Interface ▸ Menu theme** (Rotate, which is the default, or one of the
three factions). Menu theme changes the menus only: in the game the interface is always the character's faction.

## 4. The title flow, once per skin

`FrontEndBuilder` (**Krytical Nova ▸ Front End ▸ 1. Build Front-End Scenes**) builds each of the five scenes with its
screen, popup and controller three times, under `Skin_Hegemony`, `Skin_Null` and `Skin_Covenant`; `MenuSkinSwitch`
switches on the one the menus wear. Every screen is the pack's own prefab, with our comic layer (Ben-Day dots, the
glow in the skin's menu accent) over its background. Layout, text and behaviour are the same in the three: one
controller class per screen, which finds the parts it needs by name.

- **Title, Faction select, Character select, Loading:** each pack's own screens. On the Title the game's name is
  centred as a whole (`CentreTitle`): the packs place each part of their own title in a room made for their words.
- **Faction select** is the pack's screen of three cards, and each card is the frame its faction's **champion** stands
  in (see "Faction select's champions" below).
- **Character creator:** each skin's own. The Covenant wears Fantasy Screens' creator screen. Sci-Fi Menus and Dark
  Fantasy Menus have no creator screen, so the Hegemony's and the Null's are assembled from their own pack's parts
  (see "The creator in every skin" below).
- **Loading:** the header is found under either name the packs give it. The sci-fi screen has no tip line, so the
  builder adds one over its bar; the other two have theirs under the bar.
- **Settings from character select:** the O key and a prompt at the foot of the screen (section 7).
- **The doll on character select wears what the character was last played in** (9 Oct 2026): outfit, weapon in its
  carry place and the weapon's Temper glow, beside the look. The roster entry keeps a small record of it
  (`CharacterProfile.worn`: slot, item, Temper), which the player's own game brings up to date as gear changes and once
  more on leaving the world (`PlayerSaveAgent.RememberWorn` → `CharacterSession.RememberWorn`, the way the level is
  kept). It is the player's machine that writes it, because that machine is told its loadout whoever hosts, while the
  server's save cannot be read from a menu by a joined client. A copy for show only: the server's save decides what
  the character has. `CharacterShowroom.Show(recipe, worn)` dresses the doll with `DollOutfit.Wear`, the same call
  faction select's champions use; the creator's stage is not given the item catalog and its doll wears no gear. A
  character not played since this was added shows without gear until its next session.

Synty's TextMesh Pro font assets are dynamic and get written to when a new glyph is drawn. `VendorFontGuard` keeps
every vendor font asset out of every save, so building or playing never changes a vendor file.

### Faction select's champions

`FactionShowcase` (one in the scene, behind the canvas, shared by the three skins) stands a doll for each faction:
the Hegemony's Civilian, the Null's Skeleton and the Covenant's Elf (owner's choice), each in the best Epic piece of every slot at or
under level 112 (`gearLevel`) with the weapon of its faction's offense class, glowing at Temper 120 (`temper`). What
they wear is looked up in the gear dictionary as the game does (`BestPiece`), so it follows the gear ladder.

- While it is not the chosen one, a champion stands in a stance of Synty's idle pack that suits it (the expression
  library the creator's Preview tab uses): the Civilian with arms folded, the Skeleton in the aggressive posture, the
  Elf in the dramatic one (`Champions` in `FrontEndBuilder.Showcase`).
- A doll stands in its card: every frame the showcase reads where the card's window (`Doll_Window`) is on the screen
  and puts the doll's feet on its foot at the stage's depth, as tall as its share of the window. So it holds at any
  screen shape, on the book's page, and in any skin's card.
- The chosen one draws its weapon (the player's own draw and drawn stance), stands larger and is lit by its own lamp
  and a rim lamp in its faction's colour; the others carry their weapons and stand back in the dark. The wall's glow
  takes the chosen faction's colour.
- The cards keep the faction's crest (small, in the top corner) and name; a card's own text is not used. The words of
  the chosen faction (tagline, lore, lineages) are on one panel to the right of the cards, the pack's framed panel.
- Keys and flow are as before: Left / Right, Enter or a double click to choose, Esc back, a click to select.
- The packs' colour washes over the screen (blue gradients and hexagons, red gradients) are kept at about a third of
  their strength here (`ChampionWash`), and the wall's glow is soft (`backdropSaturation`, `backdropValue`): stronger,
  they tinted all three champions in one colour.
- `CharacterDoll.Create(..., keepWeapon: true)` keeps the model's weapon mount on a doll; the weapon glow
  (`GlowHaloFeature`) was added to `UIPreview_Renderer`, which the menus' cameras and the Equipment preview draw with.

### The creator in every skin

One controller (`CharacterCreatorController`) and one set of sections in all three: seven tabs with Q and E at their
ends, the Origin page (four tiles, the chosen one's name, description and three traits; the Discipline tab uses the same
tiles), a list of option rows and sliders shared by Body, Head and Preview, three swatch groups, the name page, and five
prompts along the foot. What differs is what each part is made of.

A skin either names its pack's creator screen (`UiSkin.creatorScreen`: the Covenant's, from Fantasy Screens) or has
none, and then `FrontEndBuilder.AssembleCreator` builds the screen from the parts the skin names. The three menu packs
are made to one template, so every skin has every part (`FactionSkinBuilder.EnsureCreatorParts` fills them in):

| Part | Skin field | Pack prefab (`<Pack>` = SciFiMenus, DarkFantasyMenus) | Used for |
| --- | --- | --- | --- |
| Tab strip | `tabStrip` | `Menu_<Pack>_Navigation_Icons_01` | the seven tabs (the strip ships five buttons: two more are copies) and the Q / E caps |
| Framed panel | `framedPanel` | `Panel_<Pack>_Popup_02` | the page the sections stand on; the sections go where the pack writes on its panel |
| Icon button | `iconButton` | `Menu_<Pack>_Button_Icon_01` | an origin tile (portrait, or the class weapon), a colour swatch |
| Option row | `optionRow` | `Settings_<Pack>_Item_Array_01` | the stepped rows, and "Suggest a Name" |
| Slider row | `sliderRow` | `Settings_<Pack>_Item_Slider_01` | Body Size, Musculature |
| Text field | `textField` | `Panel_<Pack>_Input_01` | the name |
| Section heading, headline, body text | `sectionHeader`, `headerLabel`, `bodyLabel` | `Settings_<Pack>_Section_Header_01`, `Label_<Pack>_Header`, `Label_<Pack>_Body` | every heading and line of text |
| Start button | `startButton` | `Button_<Pack>_Start_01` | Create Character |
| Footer | `footer` | `Input_<Pack>_Footer_01` | the five prompts |
| Tab pictures, trait pictures | `creatorTabIcons`, `traitIcons` | the pack's own icon sets (menus and HUD) | one picture a tab, in tab order; one beside each trait |

- The parts are unpacked in the scene (the vendor prefabs are only read): a tile's portrait and a swatch's colour go
  between the button's own layers, which a prefab instance does not allow.
- The sections are laid out at the packs' own sizes and shown 1.25 times larger (`CreatorPageScale`): a pack's rows are
  made for a list as wide as the screen. Reading text is also scaled by the skin's `fontScale`.
- **Changed on purpose from the pack's parts:** a slider row says what each end means (Slim / Heavy, Lean / Muscular)
  either side of the slider, as Fantasy Screens' does, and not a number on its handle. The chosen tile or swatch wears
  its frame a second time in the skin's accent (`MenuRow.selectedMark`): the packs light the button behind its icon,
  which a portrait or a colour covers.
- A label whose face cannot be drawn is set in the pack's reading face (`ReadableFaces`); `MenuLettering` is no longer
  used by anything.

## 5. The menus are a comic book

**At rest the screen is a page of the open book** (`MenuPage`, made by `MenuTransition` for its scene). The whole
screen, its canvases and what its cameras draw, is shown at `MenuBookSettings.pageScale` (0.88) of the view on the
book's right-hand page; round it the book is seen: the table, the edge of the page before with the fold's shadow, the
edges of the pages under this one, and an ink line round the page. Nothing of a screen is rearranged: every overlay
canvas's children go under one "Book Page" that is the canvas made smaller, each screen camera draws into the page's
rectangle, and clicks, keys and drags work as before. `pageScale` 1 gives the full-screen menus back. On the open
book a turn forward takes the leaf over onto the left-hand page; a turn back brings a leaf from the left that shows
the screen as it was last seen in this run (`MenuTransition` keeps one picture per menu screen), landing on the
screen that was left. The page from Loading does not turn: it breaks into panels (see below).
The book round the page is drawn, not modelled: MegaBook has no cover or spine of its own (a cover is an object the
user supplies), and seen straight on a model would show the same edges. Under the pages lies the cover board
(`MenuBookSettings.cover`, `coverEdge`, `coverMargin`), standing out past them along the top, the outer side and the
foot, with its shadow on the table. The menus' Settings window (UI Toolkit) is on the page too: `MenuSettings` makes
its whole document the page's size when it opens, so its shade darkens the page and not the book. Turning the model
with the mouse is measured against the page's height (`ShowroomDragSurface`); the press and hover movements are
shares of the thing they move, so they shrink with the page by themselves.

**What is built.** Every menu screen is a page. Leaving a screen takes its picture (the whole frame, interface
included); the next scene loads under that picture; then the picture lifts off as a leaf of a MegaBook and turns away,
and what it uncovers is the next screen, already live. Forward through the menus (Title, Character select, Faction
select, Creator, Loading, the world) the page leaves by the left edge; going back it leaves by the right. When there is
no page to turn (the game has just started, or the menus are come back to from the world) the screen starts under
solid ink and that ink is the book's cover: it turns away the same way.

**The way into the world (as built, 9 Oct 2026).** The Loading page is the last page, and it does not turn. When the
bar is full `MenuTransition` takes a picture of the whole view, book and all, and lays it over the view exactly where
it was, so nothing moves or changes size when the world starts under it. The picture waits there until the world runs
steadily (`settledFrames` frames in a row each faster than `settledFrameSeconds`, and nothing left in `LoadingGate`),
or `revealTimeout` seconds at most. Then ink lines draw in across it, cutting it into five to seven comic panels (rows
of different heights, one to three panels a row, a different cut every time), and the panels are thrown clear one
after another, each turning and taking the skin's accent in dots as it goes, with the game behind them. It is one
uGUI shader on the picture (`KryticalNova/UI/PanelShatter`, material `UI/Menus/Materials/M_UI_PanelShatter.mat`),
drawn over the HUD; every number is on `MenuBookSettings` ("The way into the world"), and with no material there the
page turns into the world as it used to. The Loading screen itself now waits for the page from the last screen to
finish turning before it starts loading the zone and its own clock: before, the zone's long frames made that turn
stutter while the screen's four seconds ran out behind it, so on a slow load the screen was hardly seen. Its clock
and bar count a long frame as a short one for the same reason.

**How it differs from the plan.** The G-1 report proposed a book lying open in the scene with the live screen drawn on
its right-hand page. That needs every screen moved onto a camera-space canvas fitted to the page, which changes every
layout, and the brief says to keep them. So the screens stay exactly as they are and the book is what moves between
them. The book is not on screen while a screen is at rest.

**Parts.**

| Part | Where | What it does |
| --- | --- | --- |
| `MenuTransition` | every menu scene, top-most canvas | takes the picture, carries it to the next scene, turns the page; falls back to the Ben-Day wipe when there is no book |
| `MenuBook` | `UI/Menus/Prefabs/MenuBook.prefab`, instanced while a page turns | the MegaBook of one leaf, its camera, and the picture the camera draws (nothing round the page) |
| `MenuBookSettings` | `UI/Menus/MenuBookSettings.asset` | seconds of a turn (0.7), the turn's curve, frames the picture lies flat first (2), the camera's field of view (18°), where the book stands, the back's tint |
| MegaBook Builder | on the prefab's `Book` object | **how the paper lifts and curls**: tuned with MegaBook's own inspector |
| `KryticalNova/UI/Comic Page` | `UI/Shaders/UI_ComicPage.shader` | the page: the picture on the face, paper on the back, Ben-Day dots on the shared screen grid where it curls away, an ink line round the back |
| `FrontEnd.PageOf` | | where each scene is in the book, which decides the way a page turns |

- **MegaBook is used as it ships.** Its scripts are in the project's default assembly, which our assemblies cannot
  refer to, so the scene builder adds its builder by name and `MenuBook` sets its two control values (`page`, `Flip`) by
  name. Nothing in `Assets/_ThirdParty/MegaBook` is edited by this work.
- **The book's camera** uses the renderer with no features (`Minimap_Renderer`) and looks straight down at the leaf
  from the height where the flat leaf fills the view, so the flat page is the screen it shows, pixel for pixel in
  colour (checked: equal values before and after the picture replaces the screen).
- **Input is never held back.** The picture lies flat for two frames while the next scene draws for the first time;
  from the moment the page lifts it takes no clicks and holds no keys (`MenuTransition.IsBusy` is false while a page
  turns). Leaving again during a turn simply ends that turn.
- **Cost.** Nothing of the book exists while a screen is at rest. During a turn: one picture of the screen, one mesh
  drawn by one camera, for 0.7 s.
- The back of a page is paper with dots in the menu accent of the skin the menus wear.

## 6. Feel in the menus and in Settings

The interface says what happened (`MenuMoments`, in the UI assembly); the game feel answers (`MenuFeelPresenter`, in
the game feel assembly). The interface knows nothing of how a moment looks or sounds and works the same when nobody
listens. A moment is raised after the thing was done, so nothing waits for its feedback.

| Moment | Raised when | As built (tune on `GameFeel/Prefabs/PFX_MenuFeel.prefab`) |
| --- | --- | --- |
| Hover | the pointer comes onto anything that can be clicked | swells 3.5 %, 0.14 s |
| Press | the mouse goes down on it, or its key is pressed (the prompt that shows the key answers) | gives 6 % and springs back, 0.13 s |
| Confirm | continue, pick a faction, create, enter the world, delete, APPLY, a key rebound, exit | swells 9 % and settles, 0.24 s |
| Back | a screen left backwards, a popup or Settings closed, REVERT, a rebind cancelled | a step to the left and back, 0.18 s |
| Error | FINALISE without a valid name; DELETE without the character's name typed | shakes side to side, dying away, 0.32 s |
| Toggle | a switch, an option stepped, a swatch, another row or faction selected | a squash and stretch, 0.14 s |
| Slide | a slider moved (at most every 0.07 s) | the handle swells 12 %, 0.09 s |
| Open | a popup or the Settings window opens | starts 3.5 % small and grows to rest, 0.2 s |
| Tab | another tab of the creator or of Settings; RESET TO DEFAULTS | the page starts 8 px low and rises, 0.16 s |
| Page forward / back | a page of the book lifts | the screen it uncovers starts 1.2 % large (small, going back) and settles, 0.4 s |

- Every moment has one Feel player with one **Menu Punch** feedback (`MMF_MenuPunch`: a swell, a squash, a nudge, a
  shake or a turn of the thing the moment happened on) and one **empty sound slot**. The project has no interface
  sounds yet: assign clips on the prefab's `MMF_Sound` slots and they play.
- The same feedback moves a menu element (uGUI) and an element of a UI Toolkit window, so Settings feels the same
  from character select and in the game.
- `MenuPunchDriver` does the moving in LateUpdate, after the packs' own animators, and adds its movement to whatever
  pose it finds. A pack's hover animation and ours add up, and the element ends where the pack (or a layout group, or
  the HUD layout editor) puts it. A window element is flagged as transform-animated first, which UI Toolkit requires;
  its own inline scale and place are handed back afterwards. Movements run on real time and a long frame cannot
  swallow one.
- Every menu scene holds `PFX_MenuFeel`; `PFX_GameFeel` carries it into the world.

Where the moments come from: `MenuUi.MakeClickable` puts a `MenuTouch` on everything it makes clickable; `MenuRow`,
`MenuOptionRow`, `MenuSliderRow`, `MenuSwatchGroup` and `MenuHotkeys` raise their own; the four screen controllers
raise Confirm, Back, Error, Open and Tab; `WindowMoments.Watch` covers every button, switch and slider of the
Settings window with one watch.

## 7. Settings

One window (`UI/Settings/SettingsWindow.uxml`), one code path (`SettingsPages`), one store (PlayerPrefs, per local
profile). In the game it is opened by `HudSettingsController` (O, or the game menu) and wears the character's faction;
from character select by `MenuSettings` (O, or the prompt) and wears the menus' skin. Both show and keep the same
values.

**APPLY, REVERT, RESET TO DEFAULTS.** A setting takes effect the moment its control is moved, so the player sees what
it does. APPLY keeps the changes; REVERT puts back everything as it was when the window opened or was last applied
(`SettingsLedger`); RESET TO DEFAULTS sets the page shown to the game's own values, and REVERT can undo that.

| Page | Options | Stored in |
| --- | --- | --- |
| Graphics | resolution, window mode, vertical sync, frame cap, quality preset, render scale, shadows, anti-aliasing, grass density, volumetric fog, sun shafts, ink outlines | `GraphicsPreferences` |
| Audio | Master, Music, Effects, Ambience, Interface volumes; mute in the background | `AudioPreferences` |
| Interface | HUD layout editor, reset, snap, grid size (game only); rings at the move point and under the target; names over NPCs and players; my own namecard; screen shake; hotbar hint; pet chatter; minimap rotation; interface size; **Menu theme**; **Chat text size**; **Time on chat lines** | `HudPreferences` |
| Gameplay (new) | **How fast the camera turns**, **Invert the camera's up and down**, **How fast the wheel zooms**, **How far the camera pulls back**; **Damage numbers**; **Decline** Squad invitations, Callouts, trade requests, contact requests, Crew invitations | `CameraPreferences`, `HudPreferences` |
| Controls | every window key and Hotbar key, rebindable; a key in use swaps; reset one or all | `KeyBindings` |

- From the menus the parts that need a character in the world are hidden (the HUD layout editor and its options, by
  the class `in-game-only`); the skill keys are not listed there, because there is no character to list them for.
- **The camera options go over the rig's own numbers and never past its limits** (`CameraPreferences.Turn`,
  `FurthestDistance`): 100 % is the camera as shipped, the furthest distance can only be brought in.
- **Declining requests is done on this machine**: a request of a kind the player declines is answered no on arrival,
  exactly as the Decline button would, and never shown. The server is not asked to do anything new, so the asker
  sees an ordinary refusal.
- **Chat text size** is a share of what the faction's skin gives a chat line, so it holds in every skin.

### The rows, as the packs' settings rows

A row is a bare line with the pack's highlight across it under the pointer, and its control is made of the pack's own
parts, read off each menu pack's `Settings_<Pack>_Item_Array / _Slider / _Toggle` (`FactionSkinBuilder.EnsureSettingsParts`,
flattened into `UI/HUD/Faction/<Faction>/Art/`):

| Part | Skin field | What it is |
| --- | --- | --- |
| Arrows either side of a value or a slider | `stepArrowLeft`, `stepArrowRight` | the pack's arrow buttons (a pack with one arrow: the other is its mirror) |
| The box a value is written in, and the slider's handle | `stepBox` | the pack's frame over its fill |
| The slider's track | `sliderTrack` | the pack's bar |
| ON / OFF | `toggleLeft` / `Lit`, `toggleRight` / `Lit` | the two halves of the pack's switch, dim and lit |
| The highlight under the pointer | `rowHighlight`, `rowHighlightTint` | the pack's row gradient |

A slider's value is written on its handle and an arrow at either end moves it a twentieth of its range; a switch
shows ON and OFF side by side with the one that holds lit (a click anywhere on its line still flips it). The game
menu wears the item card's frame. **A window's backing is 65 % solid** (owner, 9 Oct 2026: first 40 %, which was too see-through; one value per skin asset) (`windowBacking`), so the world reads faintly
through every window as through the packs' own; the item card stays solid.

### Controls: keys as key caps

`ControlsSettingsTab` lays the page out as the packs' own controls pages do: the keyboard picture on top
(`ICON_Input_PC_Medium`), then bare lines, the key first as a cap (`KeyCaps`) ending at the page's middle and what it
does after it. A one-letter key gets a square cap, a word a wider one; Enter, Space, Tab, Backspace and the arrows get
Synty's pictures (`InterfaceCore/Sprites/Icons_Input/MouseKeyboard`), and the fixed mouse keys the mouse with the
meant button coloured. A cap is the skin's `keycap` in the pack's own three colours (`keycapTint`, `keycapLetter`,
`keycapHot`): the Hegemony's blue cut-corner cap with white letters, the Null's rough dark cap that fills red, the
Covenant's rough slate cap with gold letters. Under the pointer a cap fills; while the page waits it says PRESS A KEY.
RESET shows on the line the pointer is on.

## 8. Tests

EditMode, all passing at the last run (427 in the project):

- `UI/Tests/Editor/UiSkinTests` (10): the Menu theme and the pick of a run; every faction has a whole skin asset; the
  generated sheets are what the assets say; colours that mean something are untouched.
- `UI/Tests/Editor/SettingsTests` (7): the ledger behind APPLY and REVERT; every page has its tab; the camera maths.
- `UI/Tests/Editor/MenuBookTests` (4): the page order; the book prefab is whole and is MegaBook's; every menu scene
  turns its pages with it; the turn starts flat and ends over.
- `UI/Tests/Editor/FactionShowcaseTests` (3): every champion has its lineage and a whole Epic set with a weapon model;
  the level asked for is honoured; the scene has one showcase and hands it and the panel of words to every skin.
- `UI/Tests/Editor/CreatorScreenTests` (3): every skin has a creator of its own pack (its screen, or every part one is
  assembled from, with a picture for every tab); no two factions share one; the creator scene hands each skin's
  controller every part it works with, in the right numbers.
- `GameFeel/Tests/Editor/MenuFeelTests` (8): every moment has a player that moves something and can sound; the world
  and every menu scene hold the menu feel; a movement leaves the thing where an animator put it.

In Play, on the isolated test session (port 7788, scratch save; port 7777 never used): the title flow in three skins;
pages forward and back; the cover opening; the Loading screen in three skins; the page into the world (before the
cover was added, see section 10); Feel on a prompt, a row, an error, the Settings window, a switch, a button, a slider,
in the menus and in the world; every Gameplay option; both chat options in two skins; no new console errors.

## 9. Parity checklist

"Same" means the same layout, text strings, hotkeys and behaviour as before workstream G, in the faction's own art.
"Pictured" means looked at in a capture in that skin.

| Screen | Hegemony | Null | Covenant | Notes |
| --- | --- | --- | --- | --- |
| Title | same, pictured | same, pictured | same, pictured | name in the pack's own title lettering; "press to continue"; Esc opens the exit popup |
| Faction select | pictured | pictured | pictured | **changed on purpose:** a champion in each card, the words on one panel; arrow keys, Enter, Esc, click as before |
| Character creator | same sections, own pack's parts, pictured (7 tabs) | same sections, own pack's parts, pictured (7 tabs) | same, pictured (7 tabs) | Hegemony and Null assembled from their menu pack; Covenant is Fantasy Screens' screen. Checked by script in all three (section 10 says what that covers) |
| Character select | same, pictured | same, pictured | same, pictured | roster, showroom, Enter, Delete (type the name), Esc; **new:** Settings (O) |
| Loading | same, pictured | same, pictured | same, pictured | header and tip the same in all three (the sci-fi tip line is added by us) |
| Settings, from the menus | pictured | pictured | pictured | the game's window; game-only parts hidden |
| Settings, in the game | pictured | pictured | pictured | **changed on purpose:** a Gameplay page, three buttons at the foot, new options |
| HUD frame: summary, menu row, Hotbar, minimap, chat | pictured | pictured | pictured | one capture per skin (the G-2 sheet); not each window |
| Every other in-game window (Pack, Equipment, forge, Vault, Trade, Kiosk, Herald, Contacts, Crew, Squad, Callout, pet, emote wheel, game menu, world map, NPC dialogue, tooltips, layout editor) | skinned by rule | skinned by rule | skinned by rule | **not pictured one by one**, see section 10 |

## 10. Not checked, not built

**Not checked.**

- **The in-game windows one by one in each skin.** Every in-game sheet is in the skin builder's list and the HUD frame
  and Settings were looked at in all three skins, but the other windows were not opened and pictured per faction.
  This is the open half of G-5.
- **The cover opening, through to the world.** The cover and the forward turn were captured after it was added. The
  run that was to re-check the page into the world afterwards did not finish: the Editor ran out of memory while the
  world loaded and stopped answering. The code of that path changed by three trivial lines; it should be run once.
- **A second client.** Nothing in workstream G is networked, but the Settings options that decline requests were
  checked on a host by raising the request locally, not by a second player asking.
- **Frame cost of the interface** was not measured against the state before workstream G. At rest nothing was added
  to a menu screen except the menu feel's listener; in the game one listener and one watch on the Settings window.

**Not built (left out of Settings rather than added as controls that do nothing).**

| Asked for | Why it is not there |
| --- | --- |
| Auto-target | changes targeting: gameplay logic, needs your OK |
| Requests refused by the server | would need networking changes; what is built declines them on the asker's behalf on arrival |
| View distance, effects density, how many other players are drawn | each needs the world's systems to honour it (far plane and fog together, every pooled effect, the namecard and model split) |
| Nameplate options beyond the two toggles | not built |
| Damage number size | not built (on and off is) |
| Colourblind options | need a second cue besides colour for rarity and name colours |
| Screen shake and flash as a slider | the toggle is there; a strength would need every camera and flash feedback checked for it |
| Text size (apart from chat) | every panel would have to grow with its text; interface size scales everything |
| Language | there is no localisation |
| The skill keys, from the menus | no character there to list them for |

**Sounds.** Every menu moment has a sound slot and none has a clip: the project has no interface sounds.

**Faction select's champions (9 Oct 2026).** Pictured in three skins with each faction chosen. After the weapon glow was
added to their renderer, the HUD portrait and the Equipment page's preview were looked at in the world as host (one
Null character, no weapon tempered): both draw as before and the console stayed clean. Not checked: a tempered
weapon's glow in the Equipment preview; Enter, Esc and the arrow keys by a real keyboard; other screen shapes than
16:9.

**The window pages (9 Oct 2026).** Pictured in the world in three skins after the shared parts went in: Pack,
Equipment, the item card (on a worn piece), Character, Skills, the world map, Settings, the game menu, Trade, the
Kiosk, Contacts and Crew (the last four opened empty, by showing the window, not through play). Not pictured: the
Vault, the shop, the forge, the Herald, the NPC dialogue, the emote wheel and the small dialogs, which need an NPC or
an action to open. Nothing was clicked, dragged, sorted or equipped in any of them after the change.

**The book (9 Oct 2026).** Seen in captures with the turn slowed to 5 s: the leaf of a turn back in the air, showing
the screen it lands on, and the leaf of a turn forward lifting; the Settings window on the page; the creator on the
page in three skins. Not checked: dragging the model with a real mouse after the change to its measure.

**The creator in the three skins (9 Oct 2026).** Pictured on every tab in Play, and run through by script in each
skin, 21 checks a skin, all passing: a tile chooses an origin and a weapon class, the portraits are drawn, the tabs,
the Q / E caps and the Next and Back prompts change tab, a slider and its arrow shape the body, an arrow steps a row,
a swatch changes the look and is marked, Randomise and Undo, the five prompts, a suggested name, a good name accepted
and a bad one refused. The script clicks through the event system and sets values: no real pointer or key was used
(the R, Z, Q, E, Enter and Esc keys were not pressed), nothing was dragged, and no character was finished from the new
screens.
