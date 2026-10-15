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

### How to run it, click by click

**1. Copy the script.** Open
`https://github.com/Ac3xCod3z/Krytical-Nova-II/blob/<branch>/Assets/_Game/_Docs/Design/WorldAtlas/gear-pets-mounts-board.js`
in a browser signed in to GitHub, click **Raw** (top right of the file), then Ctrl+A, Ctrl+C.

**2. Open the page.** In Figma open World Atlas and, in the left panel under Pages, click
**Gear, pet, mount Concept art**.

**3. Run it with Scripter** (free community plugin that runs a pasted script):
Shift+I → Plugins tab → search "Scripter" → Run. In the Scripter window click **+** (new
script), click in the editor, Ctrl+V, then **▶ Run** (Ctrl+Enter). After a few seconds a
toast reads "World Atlas: gear boxes rebuilt, pets & mounts board added" and Scripter closes.

**4. Check.** Shift+1 zooms to fit: the gear board is wider and the PETS & MOUNTS board sits
to its right. Ctrl+Z once undoes the whole run.

**No Scripter?** Main menu ▸ Plugins ▸ Development ▸ New plugin… ▸ Figma design ▸ Empty ▸
name it, Save as. Open the folder it made, open `code.js` in Notepad, replace everything with
the script, save. Then main menu ▸ Plugins ▸ Development ▸ your plugin.

Fonts: the board uses **Barlow Condensed** and **Inter**, both already in the file.
