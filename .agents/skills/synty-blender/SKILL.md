---
name: synty-blender
description: Drive Blender to turn the artist's models into game-ready Synty-style assets for Krytical Nova II - new Sidekick character parts (armour, clothing, helmets, attachments that fit Synty's Sidekick rig, follow the body blend shapes and recolour through the swatch texture) and new low-poly pets or props in the POLYGON drone style. Use this whenever the user mentions Blender, a .blend or a model they made, Sidekick parts, body or blend shapes, shape keys, skin weights, swatch or palette UVs, the low-poly look, a pet or drone model, exporting an FBX for Unity, or asks to check, fit, fix, preview or export a mesh - even if they do not name the skill. Also use it to inspect how any Synty FBX was built.
---

# Synty-style assets in Blender

The artist models in Blender. This skill does the technical work around the model: it sets up the scene,
binds the mesh to Synty's rig, gives it the body shapes, colours it the way Synty does, checks it against
measured limits, shows pictures of it, and exports a file the game can use.

Everything here was measured from the Synty files in this project (1,120 Sidekick parts, the base model,
Sidekick's database, ten drones). The full story is in
`Assets/_Game/_Docs/Reference/SyntyArt/Synty_Art_Breakdown.md`; read it once before a first job. The rules a
job needs are in `references/sidekick-spec.md` and `references/pet-spec.md`.

## How the tools run

All tools are in `scripts/` and start through one launcher (plain Python, finds Blender itself):

```bash
python .Codex/skills/synty-blender/scripts/run_blender.py --blend "<file.blend>" <tool>.py <options>
```

- **Headless (default).** Blender opens the saved .blend in the background, runs the tool and saves the
  file. Blender keeps the previous version beside it as `.blend1`. The artist must **save before** you run
  a tool, and afterwards use **File > Revert** to see the result. Say both, every time; a tool run on a
  stale file silently discards their unsaved work when they revert.
- **Live.** If the artist wants to watch it happen: they open `scripts/live_bridge.py` in Blender's
  Scripting tab and press Run Script once per session. Then add `--live` (and drop `--blend`):
  `run_blender.py --live sidekick_fit.py --object Chest`. The tool runs in their window on the open file.
  Nothing is saved for them and Ctrl+Z does not undo a tool, so ask them to save first. If the launcher
  says no live Blender is listening, fall back to headless.

Every tool prints a block ending in `RESULT: OK` or `RESULT: FAILED`, with `PROBLEM:` lines (must fix) and
`WARNING:` lines (judgement). Read the block; do not report success from the exit code alone. Blender prints
a lot of noise around it, so filter the output for the block.

Tools write nothing into the Unity project except the FBX and textures you point `--out-dir` at.

**Export only when the user says to.** While a model is being worked on, build, fit and render sheets, but do
not run an export tool until they give the go-ahead for that asset: intermediate exports clutter the folders.
Keep one review sheet per asset and overwrite it.

## First, look

Before changing anything the artist hands over, find out what it is:

```bash
python .../run_blender.py --blend "<file.blend>" inspect_scene.py
python .../run_blender.py inspect_scene.py --fbx "<any.fbx>"
```

It lists each mesh's size, triangles, transforms, materials, bones, shape keys and which swatches its UVs
sit on. The same tool on a Synty file shows how Synty solved the same slot, which is the best answer to
"how should this be built".

## Job: a Sidekick part

Read `references/sidekick-spec.md` for slot codes, swatch names and the limits.

1. **Name and slot.** `SK_<PACK>_<SET>_<VV>_<slot>_HU01`, letters only in PACK and SET, for example
   `SK_KRYT_HEGE_01_10TORS_HU01`. Agree the pack and set codes with the user once; the game's catalog reads
   the name, so a wrong one is an invisible part.
2. **Scene.** If the artist has not started, make the working file (add `--import-mesh model.fbx|.obj|.glb`
   if they already have a model):
   ```bash
   python .../run_blender.py sidekick_new_part.py --slot 10TORS --name SK_KRYT_HEGE_01_10TORS_HU01 --out "Assets/_ArtSource/Blender~/Sidekick/SK_KRYT_HEGE_01_10TORS_HU01.blend"
   ```
   It holds Synty's base body and rig as a locked reference (`SK_Reference`) and one material per swatch
   the slot normally uses. In a live session the same command adds these to the open file.
3. **The artist models**, in metres, facing -Y, round the reference body, and gives every face one of the
   swatch materials. That is all the colouring there is: no unwrapping.
4. **Fit.**
   ```bash
   python .../run_blender.py --blend "<file>" sidekick_fit.py --object <mesh name>
   ```
   This moves the mesh into rig space, copies skin weights and the four body shape keys from the reference
   body, writes the swatch UVs, sets hard edges and renames the object to the part name. Attachment slots
   are bound whole to their attach bone instead, with empty body keys, as Synty does. Useful options:
   `--bind body` for a hood or mask that should follow the body, `--bind rigid` to force one bone,
   `--default-cell "Metal 01"` for faces whose material is not a swatch, `--sharp-angle 40`.
5. **Look, then show.**
   ```bash
   python .../run_blender.py --blend "<file>" render_sheet.py --shapes --out "<folder>/shapes.png"
   ```
   Columns: neutral, feminine, heavy, muscular, skinny; rows: front, back. Open the PNG yourself and look
   for skin showing through, floating gaps and stretched areas before saying anything about quality, then
   send the sheet to the user. They review by marking up sheets.
6. **Fix and refit.** The copied weights and shapes are a starting point (about 15 % off Synty's own
   weights and a fifth off the shape movement in testing). When the artist has corrected weights or shape
   keys by hand, refit with `--keep-weights` and/or `--keep-shapes` so their work is kept. Without those
   flags a refit rebuilds both from the body, which is right after the mesh itself changed.
7. **Export.**
   ```bash
   python .../run_blender.py --blend "<file>" sidekick_export.py --out-dir "<folder>"
   ```
   The tool reads the FBX back and compares it with Synty's base model: unit, bind pose of every bone, shape
   keys, UV layer. A bind pose problem means the rig was moved or edited; start a fresh scene rather than
   trying to repair it.

Why these exact steps matter: the game matches bones by name and merges shape keys by name. A part whose
bones sit a millimetre off, or whose key is called `Heavy` instead of `defaultHeavy`, imports without an
error and then tears or ignores the slider in play. The checks exist because the failure is silent.

## Job: a pet or prop (POLYGON drone style)

Read `references/pet-spec.md`.

1. The artist models in metres, facing -Y, **one material per colour**, moving pieces (wings, rotors, jaws)
   as separate objects. Stage models are separate files: `SM_Pet_<Name>_S1` to `_S5`.
2. **Finish.**
   ```bash
   python .../run_blender.py --blend "<file>" pet_finish.py --name SM_Pet_Emberwing_S1 --size 0.6
   ```
   Applies rotation and scale, sizes the model, sets the origin (`--pivot centre` for a pet that hovers,
   `base` for one that stands), parents the pieces under the body, matches every material to the closest
   colour of the shared **Krytical Nova swatch** and puts each face on it, and sets hard edges at 30
   degrees. `--assign "Colour 01=Fur 02,Colour 02=Orange Bold 5"` picks swatch colours by hand; the names
   are in `Assets/_ArtSource/Blender~/Swatch/KN_Swatch.json`. `--join` makes one mesh when nothing moves.
   The swatch itself comes from `make_swatch.py`; `references/pet-spec.md` has its layout and rules.
3. **Look** with `render_sheet.py --out sheet.png` (front, right, back, three-quarter), then send it.
4. **Export.**
   ```bash
   python .../run_blender.py --blend "<file>" pet_export.py --out-dir "<folder>"
   ```
   The swatch textures go into the game once, with the first approved export.

A pet that arrives as a dense textured model (the user generates them in Tripo and sends them over with
the Tripo Bridge add-on) goes through `pet_from_scan.py` first: it reduces the mesh and turns the texture
into a few flat colours. The agreed route and numbers are in `references/pet-spec.md`.

**A model with a UV map and no texture, or lights to be placed exactly** (painting on its own UV map from
reference pictures): read `references/texture-painting.md` first. In short: flat colour per part, lights
traced by hand as shapes that *select* the mesh's own creases and slots (they never paint by themselves),
and close-up checks against the reference before anything is shown.

Live sessions are for the tools that edit the open scene. Do not write files or create and remove scenes
from a live tool: an attempt to copy objects out to a new .blend from the live bridge crashed the user's
Blender. To give an asset its own file, ask the user to Save As, or work headless on a saved file.
`session_check.py` (live) reports the open file and mends a missing temp folder or add-ons path.

A pet can also be **rigged** (the user rigs with Rigify): `pet_finish.py` then leaves its transforms alone,
and `pet_export.py` writes a clean deform-only skeleton with each action baked as a take. The order of work
and the Unity settings are in `references/pet-spec.md`. `enable_addon.py --module rigify` turns on a bundled
add-on in the live window and keeps it on.

## Where things go

| What | Where |
|---|---|
| .blend sources, review sheets | `Assets/_ArtSource/Blender~/` (Unity ignores folders ending in `~`) |
| Sidekick part FBX | never inside `Assets/Synty/`. Suggested `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/<Set>/` |
| Pet FBX and textures | suggested `Assets/_Game/Characters/Pets/<Pet>/` |

Confirm the export folder with the user the first time; these two are suggestions, not yet decided.
The project's layout rules are in `Assets/_Game/_Docs/ProjectOrganization.md`.

## What an export does not do

Be straight with the user about this, because a clean export can look like "done":

- **A new Sidekick part will not appear in the game yet.** The catalog baker scans only Synty's folder, and
  the merger sends unknown bones to the root. Those are code changes the user has not asked for; mention
  them, do not make them unasked. The breakdown lists them.
- **A new pet is not wired in.** `PetAssetBuilder` still points each pet at a Synty stand-in.
- No custom part has been worn in Unity yet. The first one should be imported and tried on in the game
  before a whole set is modelled.
- The tools do not model, sculpt or retopologise. They do not triangulate n-gons or apply modifiers for the
  artist; they report them.

## When something is off

- *"No Sidekick rig in the file"*: the scene was not made by `sidekick_new_part.py`. Run it live, or make
  a new file with `--import-mesh`.
- *Faces with a material that is not a swatch name*: list the valid names from the spec for that slot and
  let the user choose; `--default-cell` only when they agree one colour is fine.
- *Vertices far from the reference body*: capes, horns, big pauldrons. Copied weights there are guesses.
  Split the piece off as an attachment slot, or have the artist paint those weights and refit with
  `--keep-weights`.
- *Sheet shows skin through clothing on one body shape*: the artist pushes that shape key out in Blender
  (select the key, sculpt or move), then refit with `--keep-shapes`.
- *Blender not found*: set `BLENDER_EXE`. *Project not found*: set `KN_PROJECT_ROOT`.
- New Synty packs change what "typical" means: `references/sidekick_spec.json` records the date it was
  measured.
- When writing or changing a tool: do not add and remove temporary bmesh layers on a mesh that already has
  vertex groups to remember which vertices are new. It scrambled the groups (an eye lost most of its
  vertices, a collar doubled). Keep the new BMVerts in a Python set and read their indices after
  `bm.verts.index_update()`. And check that running a tool twice gives the same triangle count.
- Never reset a Rigify rig by zeroing every pose bone. Its helper bones carry set-up rotations (the front
  paws' `MCH-front_foot_parent`), and clearing them folded the fox's paws backwards in the user's scene.
  Use `kn.zero_controls(rig)`, which touches controls only, and run `rig_rest_check.py` after any tool
  that poses a rig: with controls at zero, every deforming bone must sit where the mesh was bound.
- A live Blender keeps Python modules loaded. Every tool reloads `kn_common` for that reason; keep that
  line when adding a tool, or a live run will use the helpers as they were when the session started.
