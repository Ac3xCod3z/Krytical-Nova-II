# World Atlas (Figma) — board scripts

The owner's concept-art boards live in the Figma file **Krytical Nova — World Atlas**
(`figma.com/design/JhbgViYlKpJEsj8IKTLz5k`). The script here changes that file through
Figma's Plugin API. It is kept in the repo so the board layout is reproducible.

## What `gear-pets-mounts-board.js` does (15 Oct 2026)

Page **Gear, pet, mount Concept art**. Safe to run twice.

**Gear board** — every Rare / Elite look box in all six lineage columns:

- the concept-sheet frame gets a **1920 × 1080 image area**; a sheet already in the box is
  moved in and scaled to fit;
- the six piece slots (Armor, Gloves, Boots, Cape, Ring, Amulet) **stay**; **three weapon
  slots** are added beside them, as tall as the piece grid;
- a **FACTION CAPES** strip with three image slots goes **between Rare and Elite**, rimmed in
  the faction's colour;
- a **GOD** section goes after Elite: three sets, **G1–G4, G5–G8, G9–G12**, each a full look
  box (sheet, pieces, weapons) with its own level chips;
- lineage columns, faction panels, title row, rarity key and board frame grow to fit; the
  loose sheets parked under the board move down with it.

**Pets & Mounts board** — new, to the right of the gear board, decorated the same way:

- **PET REFERENCE**: the three Twelve Sky 2 pet sheets transcribed as tables (Rare ×6 at
  Adept 1, Epic ×4 at Adept 15, Epic ×1 at Adept 30) with Health / Flux (Force) / Attack /
  Defense at 25–100 % and Additional Attack at 125–200 %;
- **NEW PETS**: eight boxes, each with a **1920 × 1080 reference image** slot, a stat strip to
  fill from the tables, and **five evolution slots** (baseline, 25, 50, 75, 100 %) at 960 × 540;
- **MOUNTS**: a roster of four 1920 × 1080 slots with the mount rules as context.

## How to run it (Figma desktop app, about two minutes)

**A. Get the two files onto your PC**

1. On GitHub open the folder `Assets/_Game/_Docs/Design/WorldAtlas` on the branch.
2. Click `manifest.json`, click **Raw**, press **Ctrl+S**, save it as `manifest.json` into a
   new folder, for example `Desktop\WorldAtlasPlugin`.
3. Go back, click `gear-pets-mounts-board.js`, click **Raw**, press **Ctrl+S**, save it as
   `gear-pets-mounts-board.js` into the **same** folder. (If the browser adds `.txt`, rename
   the file so the names match exactly.)

**B. Load it into Figma once**

4. Open the World Atlas file in the **Figma desktop app** (the Development menu below is not
   in the browser version).
5. Click the **Figma logo** at the top-left of the window to open the main menu.
6. Go to **Plugins ▸ Development ▸ Import plugin from manifest…**
7. Pick the `manifest.json` you saved. Figma now lists **World Atlas board** under
   Plugins ▸ Development.

**C. Run it**

8. In the left panel under **Pages**, click **Gear, pet, mount Concept art**.
9. Figma logo ▸ **Plugins ▸ Development ▸ World Atlas board**.
10. Wait a few seconds. A toast at the bottom reads "World Atlas: gear boxes rebuilt, capes +
    God tier added, pets & mounts board added". If it reads "script failed: …", send that
    text back.
11. Press **Shift+1** to zoom to fit. **Ctrl+Z** once undoes the whole run.

Fonts: the board uses **Barlow Condensed** and **Inter**, both already in the file.
