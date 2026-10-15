# World Atlas (Figma) — board scripts

The owner's concept-art boards live in the Figma file **Krytical Nova — World Atlas**
(`figma.com/design/JhbgViYlKpJEsj8IKTLz5k`). The script here changes that file through
Figma's Plugin API. It is kept in the repo so the board layout is reproducible.

## What `gear-pets-mounts-board.js` does

Page **Gear, pet, mount Concept art**. Safe to run again, also on a board an earlier
version already changed: it finishes what is missing and fixes what changed.

**Gear board**, all three factions (Hegemony, Null, Covenant), every lineage column:

- Rare and Epic look boxes: a **1920 × 1080** concept-sheet area (a sheet already placed is
  moved in and scaled to fit), the six piece slots kept, **three weapon slots** added.
- Order down each column: **Rare → Epic faction cape → Epic → Legendary → Legendary
  faction cape**.
- The tier once called Elite is **Epic**. The God-level tier is **Legendary**: three sets,
  **G1–G4, G5–G8, G9–G12**, each a full look box.
- Each **faction cape band** spans both lineages of its faction and holds **one**
  1920 × 1080 image: one Epic cape and one Legendary cape per faction.
- The rarity key, guide text, look counts and board frame follow.

**Pets & Mounts board**, rebuilt each run (art already dropped in is carried over):

- **PET REFERENCE**: the three Twelve Sky 2 pet sheets (Rare ×6 Adept 1, Epic ×4 Adept 15,
  Epic ×1 Adept 30).
- **NEW PETS**: eight boxes. Each has its **stat layout filled in** (rarity, level
  requirement, Health / Flux / Attack / Defense at 25–100 %, Additional Attack 125–200 %),
  a **1920 × 1080 reference image**, and **five evolution boxes** (baseline, 25, 50, 75,
  100 %). Names stay TBD. The layouts are the `NEW_PET_STATS` list near the top of the
  script: four Rare, three Epic Adept 15, one Epic Adept 30.
- **MOUNTS**: four 1920 × 1080 slots.

## The two commands

- **Build / update board** lays out the boards as above, then centres and fits every image
  already placed. The pets & mounts board is only rebuilt when it predates this version, so
  names and notes typed into it stay.
- **Image snap (stays on)** opens a small window. While it is open, an image dropped into any
  dashed box (or straight into its image area) is moved into the image area, scaled to fit
  and centred; nudging it snaps it back. Other shapes are left alone. Close the window to stop.

The commands show under Plugins ▸ Development ▸ World Atlas board once the newest
`manifest.json` is imported (Import plugin from manifest… again, same folder). With an older
import and no submenu, running the plugin builds and then stays on in image snap.

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

**C. Run it** (to run a newer version later, save the new `gear-pets-mounts-board.js` over
the old one in the same folder and do C again; no need to import again)

8. In the left panel under **Pages**, click **Gear, pet, mount Concept art**.
9. Figma logo ▸ **Plugins ▸ Development ▸ World Atlas board**.
10. Wait a few seconds. A toast at the bottom reads "World Atlas: gear boxes rebuilt, capes +
    God tier added, pets & mounts board added". If it reads "script failed: …", send that
    text back.
11. Press **Shift+1** to zoom to fit. **Ctrl+Z** once undoes the whole run.

Fonts: the board uses **Barlow Condensed** and **Inter**, both already in the file.
