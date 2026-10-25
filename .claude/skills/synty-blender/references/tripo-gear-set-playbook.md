# A Tripo gear set, from the owner's art to playable in the game

The Dock Runner (Civilian Rare 45-75, 23-24 Oct 2026) was the first set made this way, and it took dozens of rounds
with the owner to get it right. This is everything it taught, in the order a set is made. Each step has a **check**:
a set is not done until every check has passed and been shown to the owner as pictures from the game.

The owner's standing rules for a set: the pieces look like the art and nothing is redesigned; the set is shown in its
own colours, never a dye; painting over a fault is not a fix; every fix is checked in the game on the right lineage
with real animations (runs, jumps, strikes, a dead stop), never emotes for weapons; decisions go to the owner as
multiple choice.

## 0. Before anything

- The Drive folder `G:\My Drive\Krytical Nova Armors\Rares lv.45-75\<Lineage>\` has the art: four views per piece
  (`<Set> - <Piece> - Front/Back/Left Side/Right Side.png`), `<Set> - Color Palette.png` and `<Set> 45-75.png`.
- Find what the set is for: lineage (bare body), faction, the three weapon classes of the faction, and the ladder lines
  it takes over (armor, gloves, boots, cape of the lineage's band; the Rare weapon line of each class; the band's ring
  and amulet). Read them from `GearDictionary.asset` (an eval that lists `lineId`, `displayName`, `look`).
- Measure the game's Rare weapon of each class (the `W_<Class>_03` prefab: length, grip position along it, which side
  the blade or handle is on). Write `<Set>_Weapon_Sizing.json` beside the art. A pistol, a pair, a two-hander: ask.

## 1. The front picture (Nano Banana 2 in Tripo's Image tab)

- Recipe in memory `tripo-front-image-recipe`: our mannequin of **that lineage** in T-pose (`MannequinViews/`) plus
  the piece art; the mannequin stays in the picture; all body pieces at once (outfit, helmet, gloves, boots, cape),
  split later in Blender.
- Tripo's Image tab takes one file per upload call and three in all; each upload goes through the file input again.
- Generate is free within the daily allowance (shown on the button). Check no toast refused the images: an upload of
  the Cyberpunk icon pack sheet was refused by the content filter; our own sheets passed.
- **Check:** T-pose, nothing added, the back view not a "paper plane" (fix with a back render plus the back art).
- **Capes (Scrap Sentinel, 24 Oct 2026; owner).** A cape is never generated through the arms: no sleeves, no armholes,
  nothing lying along the outstretched arms. It sits on the shoulders and hangs behind the arms. Nano Banana ignores
  lengths: every regeneration lengthened it, and asking for the trim lengthened it again. What worked was the **Edit
  tool** (the pencil on a picture): edit the picture whose length the owner approved, ask for one change only ("only
  add a thin trim ... do not change the shape, length or outline"), with the cape art as reference. Do not make the
  back view separately: Tripo's multi-views built a back that matched the approved front. Edits can sit for minutes
  and return nothing; "Done" keeps an edit, closing asks whether to apply it.

## 2. The model (Tripo Model tab)

- Multi-views (Left, Right, Back; check they add nothing), then **HD Model with Generate in Parts (Detailed)** for
  body pieces (owner, 24 Oct 2026): the model comes in parts, so a bad top can be regenerated and the bottom kept.
  8K texture, Private. (The Dock Runner used Smart Mesh P2 without parts.)
- In parts mode the texture is a second step: Tripo switches parts off when the texture is made during generation,
  so generate in parts without texture, save the parts, then Texture module at 8K with Remove Lighting on.
- **Smart UV (owner, 24 Oct 2026):** after the mesh, run Tripo's Smart UV module and export that model too. In
  Blender the Tripo texture is baked onto the Smart UV layout (`smart_uv_bake.py`), so a spot that needs fixing is
  painted on clean, readable UV islands rather than Tripo's scattered ones.
- Export FBX (Blender, 8K) → zip in Downloads → unzip into the lineage folder (the DCC Bridge stopped sending).
- **Check:** a piece whose hidden side Tripo built from the mannequin (the helmet had no back): regenerate it alone
  from a close-up render.

## 3. First pass in Blender

- Scale by height and arm span together, feet on the floor, centred on the bare `_BASE_` body (never the rig's
  `.gear` outfit). Remove the mannequin by its mid-grey texture and by lying on the skin; keep a `_WithMannequin` copy.
- Name object, mesh and material `<Set>_<Piece>`; delete earlier attempts once the owner agrees.
- **Check:** contact sheet front/back/sides in the owner's chat.

## 4. Colour

- The palette picture feeds `KN_GearSwatch.json` (`sets.<CODE>`). Blender's 8-bit pixels are already sRGB: never
  convert again. Saturation x1.5 before matching (Tripo's Remove Lighting pales colours); greys match greys; an exact
  palette match is kept; specks under 5 cm² take their border's colour.
- Anything off the palette (a bright yellow cape) takes its nearest palette colour.
- **Check:** a close-up of every small detail. The AI puts stray squares and marks on pieces (the helmet's teal square
  between the lenses): find them on a front close-up render and recolour them as the owner says.

## 5. Fit (second pass)

- `clear_faces` against every body state, **bust and butt on males too**; small loose details left alone; write the
  Basis back after any bmesh round trip.
- Gloves, boots, cape, helmet each take their own path (`--slot`).
- **Gloves: do not keep Tripo's fingers.** Its finger plates splinter into shards when the hand closes, in game more
  than in Blender. Rebuild the hand on Sidekick's own hand and keep only the Tripo cuff: `glove_rebuild.py --set CODE`.
  It also rebuilds the glove's body keys from the bare hand.
- **Shape keys:** run `keys_report.py --set CODE` on every piece scene. A piece's key should move about as far as the
  body under it, and not at all where the body part has the key at zero. The Dock Runner gloves moved 9 cm where the
  hand moves 4 and had bust and butt keys (fixed). The boots still have up to 2.8 cm of `kn_defined` and `defaultBuff`
  where the bare foot has none, and the torso's `defaultBuff` moves 15.8 cm where the body moves 7.8. Fix such keys
  from the body under the piece, as `glove_rebuild.py` does for the hand. A cape or a helmet has no body part of
  its own, so its `!!` lines are expected.
- **Cape:** trunk weights plus up to 60 % of the upper arm, five chains under `backAttach` (`gear_dangle.py --sectors`),
  `--resize` about the collar if the owner finds it big. In the game, the dangle bones keep cloth on its own side of
  the trunk and legs (`DangleBones`, one-sided since 24 Oct 2026: it swung through the body on a dead stop) and out
  of the arms' way.
- **Helmet:** rides `headAttach`, is a vanity toggle (not a slot), and needs the under-helmet hair bake
  (`hair_under_helmet.py --rim ... --shell ...`) with this helmet's own rim and shell.
- **Check:** the motion test's show-through measure on every piece; fist and wrist-bend renders of the gloves
  (stretched edges under 1.6x); `keys_report.py` clean.

## 5b. Into Blender (before the fit)

- **T-pose import:** `tpose_import.py --fbx <baked fbx> --lineage "<Lineage Sex>" --name <Set>_<Piece> --out <Gear>`
  scales by height and span (x1.88 for these), centres on the bare body and removes the mannequin. For a model made
  in parts, add `--by-island`: whole islands that are mostly grey and lie on the body go, never single faces (per
  face, the Scrap Sentinel helmet lost its light side panels). The robot's bare body is `SK_SCFI_ROBO_04_*`, not a
  `_BASE_` part.
- **UVs:** `smart_uv_bake.py` bakes the Tripo texture onto Tripo's Smart UV export (`--smart`); a parts model has
  none (Tripo refuses it), so without `--smart` the script unwraps a copy in Blender and bakes onto that.

## 6. The low-poly look and export

- Faceted (flat shading) before export: the owner asked for it after the first export.
- `sidekick_export.py` per piece (`--object`); the bind check compares with the best same-slot Synty part.
- Exports go to `Characters/Shared/SidekickParts/Resources/Meshes/Gear/<Set>/`.

## 7. Weapons

- `weapon_pass.py --kind sword|polearm|pistol --length <game length>`: sizes it, grip at the origin, palette and
  facets, a review sheet with ticks every tenth of the length and the grip marks. **Check the sheet before going on:**
  the Carbine's grip landed on its drum and its muzzle was the wrong end (fixed with `--flip --grip-along
  0.2 --grip-drop 0.08`).
- Two-handers carry the off-hand stretch of shaft (`_grip.json`, `WeaponGrip`); `OffHandGripIK` closes the left hand
  when a strike brings it within 35 cm. The game's Glaive strikes are one-handed, so do not promise a two-handed hold.
- Holds: the class look's hold, except pistols, which use `SidekickGearGenerator.PistolHoldEuler`. The old forward
  hold laid a pistol's top along the forearm.
- **Check in Play with numbers, not by eye:** barrel against forearm and the gun's up against world up through the
  strikes (the hand camera missed the gun for three runs); the hand camera aims at the hand **and** at what it
  holds.

## 8. Into the game (`TripoGearSetBuilder`)

- Add a `Set` entry: code, folder, name, roles, pieces with line ids and parts, weapons (line, like, hold, icon),
  renamed jewellery lines with icons. Menu **Krytical Nova ▸ Items ▸ Blender Sets ▸ Add <Set>** imports, bakes the
  part catalog, writes the theme `KRYT_<Set>` from the swatch cells, makes the looks, bakes the ladder, the weapons'
  glow zones and paints, and puts `OffHandGripIK` on the player.
- **Check:** every item of the set at its level shows the set's pieces, icon and name (an eval over the catalog).

## 9. Icons

- Capture each piece from the game (`ItemIconCapture` draws our own parts in their set's colours since 24 Oct 2026;
  before that they came out green or grey). Never a dyed look.
- Put every capture on one 4x2 sheet (white ground), one Nano Banana 2 generation for the whole set (16:9, one image;
  it may add a third row of repeats: cut the first two). Describe the Cyberpunk pack's style in words rather than
  uploading the pack's icons. Downloading the sheet from Tripo is allowed for these sets (owner, 24 Oct 2026).
- `icon_sheet_cut.py <sheet> Assets/_Game/Icons/Art/Generated/<Set> <Set> <Names> [first number]`, sources at 512 px.
  Jewellery has no model: paint it from the set's own art in a second small sheet.
- The builder bakes them with the comic pass (`IconBaker.BakePicture`) onto the lines. **Check Icons** must be clean.

## 10. The test in Play (host, the right lineage)

- Set up: `Library/KryticalNova.TestSession` (port 7788, a scratch save), Front End play mode off with Zone01 open,
  the PC's roster backed up to `Library/` and a test character of the lineage put in slot 0. Undo all three after.
- `gm.level <band>`, `gm.gearset <level> rare 0 wear`, `gm.skills.max`, `gm.dummy.inf`, `gm.tpxyz` beside the dummy.
- A scripted run with its own camera, frames to `Library/KN_<Set>Test/`: turnaround, run, a run then a dead stop
  (side and front), jumps, each weapon striking the dummy with side and hand frames, the fingers aimed **at the
  fingers** (middle finger bones, from the back, the palm and the side) beside two other characters' fingers, the
  hood from behind and the sides, the helmet off and on, four dyes on every piece, and the Outfitter preview of the
  cape alone (`GearUIController.PreviewPiece`).
- Pick the humanoid Animator (the first one found has no avatar). Measure what can be measured (palm to shaft, barrel
  direction, colour cells) and say the numbers.
- Send the owner pictures, not a video recorder (it kills the Editor's frame rate).

## 11. Landing it

- `dependency_map.py --check` (0 uphill, 0 two-way), `compile_check.py` and `--server`, the DangleBones tests,
  Check Icons.
- Commit by exact path; other sessions share the working tree. Stage only your own lines of a shared doc (write the
  file from `HEAD` plus your lines with `git hash-object` and `git update-index`). Tell the owner the LFS size. Push
  only when asked.
- Encyclopedia: glossary rows (the set, any new term), a change-log line, `build_encyclopedia.py`, publish.

## Working notes

- Unity evals: Editor-assembly types are not reachable by name; find them with reflection over the loaded assemblies.
  The 5 s timeout does not stop the code: write results to a file and poll the Play state.
- Fast Script Reload cannot add a Unity message (a new `Update`): stop Play and compile.
- `run_blender.py` only runs the skill's own tools; run Blender itself (`blender -b <file> --python <script> -- ...`)
  for a one-off script. The shell's Python has PIL but no numpy or scipy.
