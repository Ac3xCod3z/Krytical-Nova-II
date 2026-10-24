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
   python .../run_blender.py sidekick_new_part.py --slot 10TORS --name SK_KRYT_HEGE_01_10TORS_HU01 --out "G:/My Drive/KryticalNova/Art/Blender/Sidekick/SK_KRYT_HEGE_01_10TORS_HU01.blend"
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
| .blend sources, review sheets, build scripts | Google Drive `G:/My Drive/KryticalNova/Art/Blender/` (`kn.art_root()`: Creatures, Pets, Sidekick). Not in git (CLAUDE.md, "Git LFS size and Google Drive"); only `Assets/_ArtSource/Blender~/Swatch/` stays in the project |
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

`G:/My Drive/KryticalNova/Art/Blender/Sidekick/SyntyMannequinModeling.blend` holds the twelve bare bodies the game shows with no
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
- **Mesh health first** (owner, 22 Oct 2026: no broken meshes that can mess up the model). It welds points split
  apart along texture seams, which would tear open when the piece bends; the UVs stay, because they live on the
  face corners. It removes faces with no area, doubled faces, loose points and edges, and floating crumbs of a few
  millimetres; studs and rivets are bigger and stay. Whole islands facing inward are turned round. Edges shared by
  three faces or more are reported, not changed.
- Openings that look open but are sealed deeper inside, like a cup (a collar running down to a floor in the
  chest) or a dome closing a leg, are found by **looking into the opening**. The first surface met, if it faces
  back out, is the seal, and everything connected to it that also faces out goes. An open loop must be
  opening-sized: a 2 cm strap loop near the collar is not the neck.
- **Sized by the torso, collar to crotch**, against the lineage's bare body. Every armor has both, whether it
  ends in shorts, trousers or a skirt: a collar-to-ankle rule stretched a pair of shorts down to the ankles (owner,
  22 Oct 2026). The crotch is found by looking up the centre line from below; the Skeleton's bones have none, so it
  falls back to collar to ankle. The piece stands with its crotch at the body's crotch, and its legs end where they
  were modelled. It is centred on its **trunk** (front and back of the chest), not its overall box, so a back pack
  or a front flap does not move it. Then, armor only, it widens at the chest and hips, never narrowing, until the
  body is covered with a few per cent to spare, measured front, back, left and right from the body's centre. The
  pose stays as modelled, usually arms down. Skin showing at the shoulders, or through gaps in a strapped or plated
  design, is for the second pass's body-hugging fit.
- Writes `<Piece_Name>_Revised.fbx` beside the original, named after the piece's **folder** (Tripo names files
  generically, e.g. `3d_model_robot_armor`). It holds one mesh of that name with the texture embedded. The owner
  asked for this file, so writing it needs no further go-ahead. Nothing goes into the Unity project.

**Second pass** (built 23 Oct 2026; the owner models all new gear in T-pose, so the arms-down conversion below is
for the first six Rare armors): `gear_second_pass.py --fbx "<piece>_Revised.fbx" --set ABCD --sheet <png>` writes a
working scene `G:/My Drive/KryticalNova/Art/Blender/Sidekick/Gear/<Piece>.blend`, a shapes sheet and an upper-body close-up
(`_closeup.png`: armpits and sleeve openings, where arms-down models go wrong). `--stage pose` stops after the T-pose.
1. **Arms-down to T-pose.** Each sleeve's opening is the biggest open loop wholly on one side, above the crotch,
   2-12 cm across (a hem crosses the middle; a pocket is smaller). Where Tripo sealed one cuff, the other side's
   (further from its shoulder) is mirrored and the cuff opened. The body's arms are pointed through the sleeves
   (shoulder to opening; imported FBX bones have made-up tails, so limbs are aimed joint to joint). Each vertex
   follows the arm whose skin is nearest it, smoothed through the cloth so nothing tears; where an arm hangs
   against the body (over 80 degrees down) cloth on the body's side of it is not sleeve. Everything else keeps the
   body's trunk weights with no arm in them (Synty's torso carries some upper-arm weight round the armpit). The
   piece is then unposed (p_rest = (sum w M)^-1 p) and the arms put back.
   A separate hard piece (an island under 400 vertices: a plate, a guard, a bracer) moves whole or not at all. No
   sleeve ends within 12 cm of the shoulder (that is a plate's hole). **Hard break** (`HARD_BREAK`: Robot, Viking):
   robots keep their cuffs closed, so the arm is aimed at its furthest reach, and any plate round the posed arm out
   to the wrist is arm even where the trunk is nearer. For both lineages each face moves wholly or stays, and the mesh
   is cut where moving and staying faces meet, at edges and at single corners; any blend between them stretched into
   claws (robot) or webs (the Viking's sleeves hang fused to the tunic). The Viking's cut-free sleeves are then set
   onto the arm by their own long axis (`seat_sleeves`). Robots get no cuff tightening and every plate moves whole
   when pushed clear.
   Owner's review, 23 Oct 2026 (Elf and Skeleton were right; the rest are fixed by these):
   - Dock Runner and the Soldier joined the hard break too (sleeves webbed or sagged under the arm); Dock Runner's shoulder yoke (inside 3 cm
     past the joint) stays with the body piece, or the shoulders showed bare (on the Viking that rule bent the
     sleeve's axis and on the Soldier it split the shoulder guards, so it is Dock Runner's only).
   - Sleeve undersides that hang in a curtain under the T-pose arm are drawn up to 2.5 cm off the skin
     (`LIFT_UNDERSIDE`: Civilian, Soldier, Viking).
   - The Viking's sleeve ends at the wrist (`TRIM_PAST_CUFF`): flaps over the hand removed, cuffs squared off.
   - Robots: hoses hanging in the open under the T-pose arm are removed (`drop_underarm_hoses`); no vertex-by-vertex
     push for the body keys (it pulled spikes out of the plates); small plates move whole when pushed clear, the big
     shell does not (moved by its one largest push, the torso slid off the head's centre).
   - A part in a slot replaces the body part there, so a forearm slot needs 85 % of the forearm covered and a leg
     slot 60 % (baggy trousers sit well off the shin); otherwise its faces go to the slot above
     (`FOLD_INTO`), or the hands and feet floated with bare gaps.
   - Waist cinch (`cinch_waist`, all but robots): rays out from the pelvis through 2 cm by 15 degree cells, first
     body hit against first armor hit; every vertex of a cell moves in by the same amount until the cloth is 1.5 cm
     off the skin (thickness kept), crotch to lower ribs, eased out. Nearest-vertex measures failed on a low-poly
     body (most cells hold no body vertex).
   - The fit loads our bare bodies (`Meshes/BareBody`, Synty's plus kn_bust/kn_butt/kn_defined) where they exist;
     on Synty's own the armor's three new keys came out empty.
   - The working scene is saved with every body key at 0 (the clip check leaves them at 1, which puffed the armor
     out in the scene and in any FBX made from it).
   - `gear_keys_vs_synty.py --synty SK_ELVN_WARR_01` (run Blender directly, nothing saved): per key, the armor's
     motion against the skin under it (gain, spread) and vertices inside, ours beside a Synty outfit on the same
     body. 23 Oct 2026: ours gain 1.00, spread 0-4 % (Synty 5-24 %); inside about Synty's or fewer, Dock Runner
     a little more (1 %).
   - After the clip check, a lone push on a key where the body under the vertex does not move on that key is
     evened out with its neighbours (`calm_lone_pushes`): the heavy+bust+butt combination pushed two Elf knee
     vertices 2.6 cm through the bust and butt keys, on one leg only. Calming every lone push let clipping back in;
     only the ones where the body stays put are wrong.
   - UVs sit on the centre of the cell's first texel, (2c + 0.5)/32 (`kn.cell_uv(..., inset=kn.SIDEKICK_INSET)`),
     never the cell's middle: the game's colour map and dark/skin masks are 32x32, so a cell's middle is a texel
     border and the cloth showed grain that crawled as the character moved (Autumn Thread in Play, 23 Oct 2026).
   - Owner's review of the Unity videos, 23 Oct 2026: see-through armpits and robot thighs, bulky shoulders, no
     taper into boots, skeleton shin guards beside the legs. So:
     - every part carries the bare body of its own slot inside it (step 6 for all lineages, not only skeletons):
       the game hides the body part a slot replaces, so any gap had nothing behind it;
     - `fit_limbs` / `cinch_limb`: rays out from the limb line, the pull blended between cells (a pull constant per
       cell sheared faces into spikes); only pieces that wrap the limb are pulled (cinching hanging cloth crumpled
       it), small pieces near them follow; at the shoulder only cloth over the top of it (the chest and the sides
       belong to the torso; pulled toward the arm they shredded Dock Runner's vest); lower legs 1.5 cm off at the
       knee to 0.8 at the ankle, plus 2 cm round a skeleton's thin bones; a skeleton's lower leg moved as one piece
       onto the bone first (sliced, the guard came apart); robot plates move by their middle need.
   - Owner, 23 Oct 2026, second review: "make it uniform so that it still mimics the original model ... professionally
     done". Every pull (limbs and waist) is one amount per height round the limb, the 30 % quantile of what the
     directions need, smoothed along the limb (`uniform_pull`); per-cell pulls bent knee pads out of shape. Thighs are
     pulled too (tubes only; never a skeleton's, whose loincloth reads as a tube from the thin bone); trouser and
     short legs are centred on the leg by their inner face (`centre="rays"`; the legs sat off-centre in Dock Runner's
     shorts); the shin pull runs past the ankle with the feet as skin, or the hems kept their flare.
   - Armpit holes (owner, 23 Oct 2026, Dock Runner and Bastion Field): the sleeve cut left the inside of the upper
     arm and the chest side bare. `close_armholes` draws the open edges round each shoulder onto the skin, 0.8 cm
     off, easing over 4 rows; `underarm_gusset` copies every bare body face under the arm and over the shoulder
     (no armor within 3 cm out along it) 6 mm off the skin, coloured like the nearest armor face, welded into one
     sheet: a gusset as real shirts have. Not robots or skeletons. Check with a low front and low back view into
     the armpit, not only front, side and back.
   - Each piece is centred side to side under the head's neck ring (`centre_on_neck`); front to back a collar ring
     is no guide (a V-neck's centre sits forward).
   Dead ends, for the record: sleeve by colour (fails on camouflage and multi-coloured jackets), sleeve as a fixed
   tube (misses puffy sleeves), turning sleeves about Synty's shoulder with no body pose (wrong pivot), giving the
   sleeve the arm's full turn wherever its nearest skin is the arm (wings).
2. **Armpit.** Cloth the T-pose stretched past 1.4x, and cloth below the shoulder out towards the arm, is laid onto
   the trunk 6 mm off the skin and evened out on the skin (smoothing off the skin made a batwing); studs and buckles
   (islands under 60 vertices) ride whole on the cloth under them. Robots skip this: plates are not cloth.
3. **Fit.** Sleeve ends drawn in to 1.2 cm off the arm (owner); weights from the T-pose body; anything inside the body
   pushed out (smoothed; small pieces move whole); split into Synty's slots by weight region (small loose pieces go
   whole to one slot); all seven body keys from the body, pushed clear for both sexes; skeleton parts take in the
   bare bones of their area. Robot chassis parts carry extra bones, so the body import keeps the rig with the most
   bones (once left robot parts a hundred times too big).
4. **Colour.** Faces sampled from the Tripo texture, snapped to the set's 24-colour palette picture, ranked per slot
   onto its own Outfit 01-05 cells; the set's theme joins `Swatch/KN_GearSwatch.json` and `T_KN_GearSwatch.png`
   (owner: one swatch of our own for gear and weapons, dyes designed from it). Parts are named
   `SK_KRYT_<SET>_01_<slot>_<SP>01` (HU humans and elves, RO robots, SN skeletons).
5. **Loose cloth (sashes, tabards, capes) moves the Synty way** (`gear_dangle.py --cells "10TORS:0,13" "17HIPS:0,8"` on
   the working scene picks the cloth by its swatch cells and gives each tail hanging below the belt a chain, tails
   grouped by the mesh's own edges since nearness split low-poly tails into crumbs; built 23 Oct 2026 on the Elf's
   sash; our chains are numbered from 11, `hips_dyn_11_01_l`, because Synty's own parts use 01-04 and the game's
   merge matches bones by name, so a shared name bound our sash to Synty's bones; cloth that never wraps the body,
   a loincloth's panels, hangs from 6 cm below its own top; studs and rivets on swinging cloth copy the weights of
   the cloth under them, or they floated off; scraps under 20 vertices get no chain of their own and ride the
   cloth too, each vertex blending the weights of the cloth face under it, from up to 6 cm; the fit seats small
   pieces left floating over 1.5 cm off the armor back onto it, `seat_floaters`): chains of dangle bones inside the part, named
   `<slot>_dyn_01_01_l` and parented to the rig bone it hangs from (`tors_dyn_…` under `spine_03`,
   `abac_dyn_…` under `backAttach`), weighted along the chain. A spring-bone session moves `_dyn_` chains in the
   game (PR 140). A free Blender add-on may be used to preview the swing (owner's leave): look on
   extensions.blender.org first.
6. **Motion test:** `gear_motion_test.py` on the working scene plays Synty's own Sidekick clips (Emotes and Taunts,
   Idles, Base Locomotion under `Assets/Synty/Animation*/Animations/Sidekick`) and counts armor vertices sinking
   into the body they cover, frame by frame, with an MP4 (`--female` for the feminine clips). Melee packs are
   humanoid in Unity and must be retargeted first.
7. **Export only when the owner's whole set is in** (owner, 23 Oct 2026: a set's full collection goes to Unity in one go) into the game's export folder
   (`Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/<Set>/`) on the owner's go-ahead. The parts
   session brings them into Unity. The textures are 8192 px from Tripo: size them down for the game (2048 is
   plenty for a part this size) unless the owner says otherwise.

**Tripo T-pose gear, the way it works now (Dock Runner, 23 Oct 2026).** Recipe for the pictures is in memory
(`tripo-front-image-recipe`): Nano Banana 2 dresses our mannequin render (`MannequinViews/`), the mannequin stays in.
- **Getting it into Blender:** Export (FBX, Blender, 8K) → zip in Downloads → unzip into the lineage folder on G:
  (the DCC Bridge's Send stopped sending). Tripo shrinks a model to 1 m: scale back by height and arm span together,
  feet on the floor, centred on the bare `_BASE_` body (the rig's `.gear` outfit is not the body). The mannequin goes
  by its mid-grey texture (value 0.38-0.82; dark soles and straps stay) and by lying on the skin; keep a hidden
  `_WithMannequin` copy. Name object, mesh and material `<Set>_<Piece>`.
- **Several pieces at once:** Tripo's Image tab takes three pictures, so put the pieces on one labelled sheet. Split
  them in Blender by where each loose piece sits. Tripo builds a piece's hidden back from the mannequin (a helmet came
  with no back of its own): regenerate such a piece alone from a close-up render (`CivilianMale_HeadFront.png`).
  Next time try HD Model's "Generate in Parts".
- **Colour:** a cape or anything far off the palette is recoloured to its nearest palette colour (never Tripo's
  bright yellow). Blender returns 8-bit pixels already sRGB: never convert them again (every set colour came out
  washed). Tripo's Remove Lighting pales colours, so the match boosts saturation 1.5x; greys match only greys; an
  exact palette match is kept; patches under 5 cm² that are not a loose piece take their border's colour.
- **Clip checks:** `clear_faces` pushes faces that dip into the body between their corners (skin showed through
  low-poly cloth with every vertex outside), on every body state including bust and butt (males get those sliders
  too); small loose details (pouches) are left alone; afterwards the Basis is written back to the vertex positions
  or the next bmesh round trip undoes the push. The sheet has male and female bust+butt columns;
  `gear_motion_test.py --shape "kn_bust=1,kn_butt=1"`. The motion test's **show-through** measure (inner surface
  seen through the outer piece) is the one that matters for gloves and boots: palm and sole points hidden inside
  them made the old sinking measure report 3.6 and 4.6 cm that never show.
- **Gloves, boots, cape, helmet:** each has its own path in both passes (`--slot`). A piece that already covers its
  body part as modelled keeps its size and place. Gloves and boots are kept clear of the forearm and lower leg
  only; cuff and shaft weights blend toward the limb. The cape takes trunk weights plus up to 60 % of the upper arm
  over the shoulders, five chains under `backAttach` (`gear_dangle.py --sectors`), `--resize` to scale it about the
  collar; its arms still pass through the side panels in some clips, which the game's spring bones handle with arm
  colliders (owner's choice). A helmet rides `headAttach`, follows the head's keys, and is eased out where the head
  comes through.
- **Painting over Tripo is not a fix** (owner: "Can't just paint and expect that to be a complete fix"); a raised
  strap built before the fit was shredded by it.
- **Under-helmet hair:** `hair_under_helmet.py --rim <brow z,y,nape z,y> --shell <helmet shell json>` bakes every
  `HairFixed` hair into what hangs below that helmet, cut 5 cm up inside the shell, hair pulled inside it on every
  key, stumps and pieces hanging from nothing removed; hairs left with nothing are not drawn under the helmet.
- **Weapons are not body gear:** they get their own passes (owner, 23 Oct 2026), sized to the game's weapon of the
  same class and tier (Dock Runner, Rare: Monoblade 1.27 m, Glaive 2.78 m, the Carbine is a 0.38 m pistol;
  `DockRunner_Weapon_Sizing.json`), grip points for the hands (`_grip.json`; the Glaive has a second hand), tested
  with the game's strike animations only.
- **Export the hair fixes** (go-ahead given 22 Oct 2026): `hair_export.py` on `SyntyHairFixes.blend` writes the
  67 copies to `.../Resources/Meshes/HairFixed/`. Two traps: a fixed mesh whose name was already taken in the
  source scene gets a `.001` ending (match by base name), and some Synty part files hide their mesh object, which
  Blender then leaves out of the export (unhide it first).
