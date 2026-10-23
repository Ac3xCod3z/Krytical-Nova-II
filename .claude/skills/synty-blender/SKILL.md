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
python .claude/skills/synty-blender/scripts/run_blender.py --blend "<file.blend>" <tool>.py <options>
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

## Job: the mannequin scene (every lineage, bare, side by side)

`Assets/_ArtSource/Blender~/Sidekick/SyntyMannequinModeling.blend` holds the twelve bare bodies the game shows with no
gear on (six lineages, male and female, from `CharacterDataBaker.Specs` and the Lineage assets), each on its own copy
of the Sidekick rig in T-pose, every Synty shape key kept, plus the Krytical Nova sliders `kn_bust`, `kn_butt`,
`kn_defined` (shape keys) and `kn_height` (rig property driving the root bone's scale). New gear parts are fitted here.

- Rebuild from the Synty files (fast, deterministic; the numbers are `AMPLITUDES` in the tool):
  `run_blender.py mannequin_build.py --out ".../SyntyMannequinModeling.blend"`
- Dress one mannequin to test gear against the sliders (keys copied from the body by nearest surface point):
  `run_blender.py --blend FILE mannequin_gear.py --tag CivilianF --pack SCFI_CIVL` (`--remove` takes it off)
- Pictures and MP4s (nothing is saved to the .blend):
  `mannequin_render.py --lineup out.png`, `--tag CivilianF --columns "bare|;bust|kn_bust=1" --views front,right,back --out sheet.png`,
  `--tag CivilianF --sweep kn_bust,kn_height --video out.mp4`, `--turntable out.mp4`; `--gear on|off|both` for dressed figures.
- The hand-off for putting the sliders into the game is `BODY_SLIDERS_HANDOFF.md` beside the scene.
- **Clipping (brief section 3).** `mannequin_clip.py` counts hair, facial hair and gear vertices that go inside the
  body at every slider extreme and combination, against the rest shape (Synty's hairline already dips into the
  scalp on purpose). `--fix` gives hair and beards the body's motion (Synty ships their body keys empty: the cause)
  and pushes out what is left; it saves the .blend. Order after a rebuild: `mannequin_build.py`, then
  `mannequin_gear.py` for any dressed mannequin, then `mannequin_clip.py --fix`. Every new part is checked with it
  before it is shown, and the report (`--out`) goes in the part's Sheets folder.
- **Export the bare bodies** (only on the owner's go-ahead; given 22 Oct 2026): `mannequin_export.py` writes 26
  Synty-named copies into `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody/`, each from a
  fresh import of Synty's original plus the kn_* keys, checked bone for bone against that original. Some Synty
  originals differ from SK_BaseModel on a few bones (thighs, upper arms, props); compare a copy with its own original.
- **Every creator option** (`creator_clip_audit.py --parts creator_parts.json`): measures every head, hair and
  facial hair the Lineage assets offer, on every body, as Synty ships it, with the transfer alone (what the game
  does at merge time) and with the full fix.

## Job: gear from the owner's Drive (owner, 22 Oct 2026)

The owner makes gear in Tripo, textured, and drops **one piece at a time** into
`G:\My Drive\Krytical Nova Armors\<tier>\<Lineage>\<Piece Name>\` (an FBX plus its `.fbm` texture folder; the
lineage folders are Civilian, Soldier, Viking, Elf, Robot, Skeleton; concept pictures sit beside them). The game's
gear slots that become models are Armor (torso, arms, hips and legs in one piece), Gloves, Boots and Cape.
Helmets are vanity, shown with a toggle on the equipment page, but they still get this pass. Ring, Amulet and the
weapons are not Sidekick body parts and do not go through this job.

**First pass, every piece as it arrives:** `gear_first_pass.py --fbx "<piece>.fbx" --sheet <Sheets/Gear/...png>`
(`--dry-run` first).
- Opens the holes a body passes through: the neck, sleeve ends and leg ends of an armor piece, each glove's cuff,
  each boot's top, and the head opening of a helmet. Tripo often seals these with a flat cap. An opening counts
  as already open when an open loop sits there, judged by its position alone: which way a rim's faces run says
  nothing. A sealed one is a flat, roughly round patch whose whole edge folds sharply into the walls. A sash end
  or a plate never qualifies, because it is long and thin or its edge runs on smoothly.
- **Robots keep their caps.** Synty's robot parts are the chassis itself, every part closed, its ends hidden in
  the next part. **Skeleton pieces are opened like human ones**, because Synty's skeleton outfits carry the bones of
  their body area inside them (the torso piece holds the ribcage). The second pass adds those bones.
- Sizes the piece uniformly by height against the lineage's bare body (armor: collar to ankle = neck to ankle).
  Then, armor only, it widens at the chest and hips, never narrowing, until the body is covered with a few per
  cent to spare. Each direction is measured from the body's centre: front, back, left and right separately,
  because Tripo's figures carry their depth differently from Synty's chunkier bodies. The pose stays as modelled,
  usually arms down. Skin showing at the shoulders is that pose, not the size, and the second pass fixes it.
- Writes `<Piece_Name>_Revised.fbx` beside the original, named after the piece's **folder** (Tripo names files
  generically, e.g. `3d_model_robot_armor`). It holds one mesh of that name with the texture embedded. The owner
  asked for this file, so writing it needs no further go-ahead. Nothing goes into the Unity project.

**Second pass, once a whole set is in (not built yet; the plan):**
1. Pose each piece into the rig's T-pose: arms up to the rig's arm line, with a temporary armature fitted to
   the piece.
2. Conform to the lineage body as an offset shell (the body's surface plus a set thickness). Hard plates stay
   rigid and are moved whole. Make the female breastplate or the male counterpart as the character brief says.
3. Split into Synty's slots (torso, upper and lower arms, hips, legs, attachments) with correct part names.
   Weights from the body with `sidekick_fit.py`; Synty's four body keys plus kn_bust, kn_butt and kn_defined.
4. **Loose cloth (sashes, tabards, capes) moves the Synty way:** chains of dangle bones inside the part, named
   `<slot>_dyn_01_01_l` and parented to the rig bone it hangs from (`tors_dyn_…` under `spine_03`,
   `abac_dyn_…` under `backAttach`), weighted along the chain. The game creates missing dyn bones on demand
   (`SidekickPartRefs`) but nothing moves them yet. Making them swing in play needs a spring-bone or cloth system
   in the game; that is game work, so tell the owner, don't build it here. To preview the swing in Blender a free
   add-on may be installed (owner's leave, 22 Oct 2026): look on extensions.blender.org first (a spring-bone
   add-on such as Wiggle 2). Blender's own cloth sim only bakes motion, it does not export as game physics.
5. **Clip test at rest and in motion.** `mannequin_clip.py` covers every slider. Then play the game's real motion
   on the fitted piece: Synty's Sidekick clips load straight onto the rig (Emotes and Taunts 140, Idles 330, Base
   Locomotion 171, under `Assets/Synty/Animation*/Animations/Sidekick`). The melee packs (Grruzam and others)
   are humanoid in Unity and are retargeted to the rig first. Measure body-through-gear frame by frame and send
   MP4s. Synty's gear holds in emotes and combat because it is skin-tight with the body's own weights; ours must
   pass the same test before export.
6. Export the Unity files into the game's export folder
   (`Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/<Set>/`) on the owner's go-ahead. The parts
   session brings them into Unity. The textures are 8192 px from Tripo: size them down for the game (2048 is
   plenty for a part this size) unless the owner says otherwise.
- **Export the hair fixes** (go-ahead given 22 Oct 2026): `hair_export.py` on `SyntyHairFixes.blend` writes the
  67 copies to `.../Resources/Meshes/HairFixed/`. Two traps: a fixed mesh whose name was already taken in the
  source scene gets a `.001` ending (match by base name), and some Synty part files hide their mesh object, which
  Blender then leaves out of the export (unhide it first).
