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

- **Title, Faction select, Character select, Loading:** each pack's own screens.
- **Character creator:** Fantasy Screens' layout in every skin (the other two menu packs have no equivalent), with the
  skin's option rows and its lettering (`MenuLettering`).
- **Loading:** the header is found under either name the packs give it. The sci-fi screen has no tip line, so the
  builder adds one over its bar; the other two have theirs under the bar.
- **Settings from character select:** the O key and a prompt at the foot of the screen (section 7).

Synty's TextMesh Pro font assets are dynamic and get written to when a new glyph is drawn. `VendorFontGuard` keeps
every vendor font asset out of every save, so building or playing never changes a vendor file.

## 5. The menus are a comic book

**What is built.** Every menu screen is a page. Leaving a screen takes its picture (the whole frame, interface
included); the next scene loads under that picture; then the picture lifts off as a leaf of a MegaBook and turns away,
and what it uncovers is the next screen, already live. Forward through the menus (Title, Character select, Faction
select, Creator, Loading, the world) the page leaves by the left edge; going back it leaves by the right. When there is
no page to turn (the game has just started, or the menus are come back to from the world) the screen starts under
solid ink and that ink is the book's cover: it turns away the same way. The page from the Loading screen turns into
the world.

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

## 8. Tests

EditMode, all passing at the last run (427 in the project):

- `UI/Tests/Editor/UiSkinTests` (10): the Menu theme and the pick of a run; every faction has a whole skin asset; the
  generated sheets are what the assets say; colours that mean something are untouched.
- `UI/Tests/Editor/SettingsTests` (7): the ledger behind APPLY and REVERT; every page has its tab; the camera maths.
- `UI/Tests/Editor/MenuBookTests` (4): the page order; the book prefab is whole and is MegaBook's; every menu scene
  turns its pages with it; the turn starts flat and ends over.
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
| Faction select | same, pictured | same, pictured | same, pictured | three crests, arrow keys, Enter, Esc |
| Character creator | same, pictured | same, pictured | same, pictured | Fantasy Screens' layout in every skin; the skin's rows and lettering |
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

**The book at rest.** The comic book is the way between screens and the way in; it is not drawn while a screen is at
rest (section 5 says why).
