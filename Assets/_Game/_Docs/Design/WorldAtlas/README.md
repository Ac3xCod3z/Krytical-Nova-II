# World Atlas (Figma) — board scripts

The owner's concept-art boards live in the Figma file **Krytical Nova — World Atlas**
(`figma.com/design/JhbgViYlKpJEsj8IKTLz5k`). The scripts here change that file through
Figma's Plugin API; they are kept in the repo so the board layout is reproducible.

## `gear-pets-mounts-board.js` (15 Oct 2026)

Run it once with the page **Gear, pet, mount Concept art** open. It is safe to run again.

1. **Gear.** Every `Rare • …` / `Elite • …` look box (42 of them) is rebuilt around a
   **1920 × 1080 image area**; a concept sheet already in the box is moved in and
   scaled to fit. The six piece slots (Armor, Gloves, Boots, Cape, Ring, Amulet) are
   replaced by **three weapon slots** under the sheet, one per weapon of the set. Lineage
   columns, faction panels, the title row and the board frame grow to fit, and the loose
   sheets parked under the board move down with it.
2. **Pets & Mounts.** A second board, to the right of the gear board and decorated the
   same way (title, dark guide panel, coloured identity band, faceted accent, Ben-Day
   dots, rimmed boxes, dashed art targets):
   - **PETS**: 8 rows × 5 evolution slots (0 / 25 / 50 / 75 / 100 %). Emberwing, Drone
     Core, Tidecaller and Kitsune Spirit carry their stat, item id and the stage looks
     that exist; rows 5–8 are reserved for the next roster. Image areas are 960 × 540,
     so a 1920 × 1080 sheet drops in at 50 %.
   - **MOUNTS**: one roster box with 4 slots (1200 × 675 image areas) and the mount
     rules from `05_mounts.md` as context.

### How to run it

Figma, desktop app or browser, with edit rights on the file:

1. Open the file and select the page **Gear, pet, mount Concept art**.
2. Menu **Plugins ▸ Development ▸ New plugin…**, pick *Figma design*, choose
   *Empty*, name it `World Atlas board`, save it anywhere.
3. Open the folder it created and replace the whole content of `code.js` with
   `gear-pets-mounts-board.js`. (Keep `manifest.json` as generated.)
4. Menu **Plugins ▸ Development ▸ World Atlas board**. It finishes in a few seconds
   and shows a toast; if it fails the toast says why, and nothing partial needs undoing.
5. Undo (Ctrl/Cmd-Z) reverts the whole run if the result is not wanted.

The community plugin **Scripter** also runs it: paste the file in and press Run.

Fonts: the board uses **Barlow Condensed** and **Inter**, both already in the file.
