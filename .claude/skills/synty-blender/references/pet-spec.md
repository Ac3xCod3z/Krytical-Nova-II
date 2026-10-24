# Pet and prop rules (POLYGON drone style)

Measured 8 Oct 2026 from Synty's drones in this project. Background:
`Assets/_Game/_Docs/Reference/SyntyArt/Synty_Art_Breakdown.md` section 6, and the pet design in
`Assets/_Game/_Docs/Design/12_pet_evolution_system.md`.

## What Synty's drones are

| Model | Size (m) | Triangles | Colours used | Pieces |
|---|---|---|---|---|
| SciFiCity `SM_Prop_Drone_01` | 1.6 x 1.3 x 2.5 | 650 | 10 | 1 |
| SciFiCity `SM_Prop_Drone_02` | 1.6 x 1.3 x 2.2 | 650 | 10 | 1 |
| SciFiCity `SM_Prop_Drone_Ball_01` | 0.7 x 0.6 x 0.6 | 1,174 | 5 | 1 |
| SciFiCity `SM_Prop_Drone_Insect_01` | 1.5 x 1.0 x 1.1 | 1,972 | 6 | 1 |
| SciFiCity `SM_Prop_Drone_Quad_01` | 1.2 x 1.2 x 0.5 | 2,154 | 6 | 1 |
| SciFiWorlds `SM_Prop_Drone_01` | 1.2 x 1.0 x 0.9 | 1,734 | 8 | body + `_Wing_l` + `_Wing_r` |

- Static meshes: no skeleton, no shape keys.
- Every face's UVs are one point on a flat colour. One material.
- All faces smooth; 70 to 85 % of edges hard (machines are cut harder than characters).
- Lights are a separate emission texture on the same UVs.
- Moving parts are child objects with their own pivots, animated in Unity on their transforms.

## What our pet needs

- Reads as a silhouette from 10 m: a few big shapes, thick limbs, no thin detail.
- 1,000 to 2,500 triangles. `pet_finish.py` warns above 2,500.
- 5 to 10 flat colours, one of them glowing if the creature has an eye, core or flame. The evolution glow is
  drawn by the game round the outline; do not model a glow shell or a ground ring.
- Modelled in metres, facing -Y in Blender (that becomes +Z, forward, in Unity), Z up.
- One material per colour while modelling. Name them for what they are (`Shell`, `Eye Glow`); the names are kept
  in `T_<name>_Palette.json` so a colour can be found again.
- A glowing colour: a Principled material with Emission Strength above 0. It is written to the emission texture.
- Pieces that move are separate objects with the origin on the hinge. The tool parents them under the body
  (the largest piece) and keeps each origin.
- Origin of the body: centre for a pet that hovers (`--pivot centre`), underside for one that stands (`base`).
  The game spaces a pet from its bounds, so no offsets are needed.
- Five stage models per pet, `SM_Pet_<Name>_S1` ... `_S5`, each its own file and FBX. Later stages may be bigger
  and more detailed; keep the same colours so they read as one creature.

## The Krytical Nova swatch (decided 8 Oct 2026)

Everything we model that is not a Sidekick part (pets, props, weapons) shares one small texture of named
flat colours, the way a Synty pack shares one texture. It replaces the per-pet palette.

- Source of truth: `scripts/make_swatch.py`. It writes `Assets/_ArtSource/Blender~/Swatch/KN_Swatch.json`,
  `T_KN_Swatch.png` (64 px, 16 x 16 cells of 4 px), `T_KN_Swatch_Emission.png`, `T_KN_Swatch_Metallic.png`.
- Layout, row 15 at the top: Colour Bible colours; Grey 01-16; Fur 01-16; Gold / Silver / Bronze / Iron x 4;
  then twelve hue rows (Red, Orange, Amber, Yellow, Lime, Green, Teal, Cyan, Blue, Indigo, Violet, Magenta),
  each `Bold 1-6`, `Soft 1-6`, `Pastel 1-2`, `Glow 1-2`. Glow cells emit; the metal row is metallic.
- `pet_finish.py` matches each material to the closest cell (a glowing material only to Glow cells, a metallic
  one only to the metal row), puts the faces' UVs on it and recolours the material to the cell's colour, so
  Blender shows what the game will. `--own-palette` gives one model a palette of its own instead.
- `pet_export.py` names the material `M_KN_Swatch` for every swatch model, so Unity shares one material.
- Never move or recolour a cell that is in use: every model on it changes. Three cells are free (row 15).
  When it is full, add a second swatch rather than reshuffling.
- The swatch textures go into the game with the first export the user approves (suggested
  `Assets/_Game/Characters/Shared/Swatch/`): Point filter, no mip maps, no compression.

Sidekick parts (armour, capes) do not use this swatch and cannot: they must sit on Sidekick's own 16 x 16
grid, because the character's one material and the game's colour system read those cells
(`sidekick-spec.md`). They are already swatch-mapped in the same way, on Synty's layout.

## A pet that starts as a Tripo (or other AI / scan) model

Decided with the user 8 Oct 2026 on the fox, and the route for every pet made this way:

1. The model arrives through the Tripo Bridge add-on into whatever file is open. The user saves it as its own
   file, `G:/My Drive/KryticalNova/Art/Blender/Pets/<Pet>/SM_Pet_<Pet>_S1.blend` (File > Save As). One pet, one file.
2. `pet_from_scan.py --object "<its name>_source" --name SM_Pet_<Pet>_S1 --tris 2000 --colours 6 --merge 0.13 --smooth 4 --bold 3.2`
   (the Kitsune Spirit's numbers; `--bold 1` keeps the texture's own colours)
   - keeps the original as `<name>_source` (hidden), so any step can be redone;
   - reduces a copy to the triangle target;
   - gives every face one flat colour taken from the original's texture, folding near-identical colours
     together so a soft gradient becomes one or two flat tones, not bands;
   - keeps small markings the coarse mesh cannot carry (eyes, nose) as their own small pieces of surface.
   Numbers that worked on the fox are the ones above. More `--colours` or a lower `--merge` keeps more tones.
3. `pet_finish.py --objects SM_Pet_<Pet>_S1 --name SM_Pet_<Pet>_S1 --size 0.6 --pivot base` sizes it, maps
   those colours onto the KN swatch and sets hard edges. The Tripo texture (8192 px) is not used in the game.
   Then, if the eyes came out blotchy: `pet_eyes.py --object SM_Pet_<Pet>_S1 --at <x,y,z of one eye>` removes
   the model's own eye lumps and lays a clean almond eye in one swatch colour on each side, then
   `pet_finish.py` again. Kitsune Spirit: `--at 0.031,-0.237,0.2555 --width 0.036 --height 0.019 --tilt 14
   --radius 0.03 --lift 0.0025`, colour Black. Each eye is a smooth lens worked out once and mirrored, so both match;
   it follows the bend of the head but not its lumps. A first version that hugged the surface vertex by vertex
   came out crooked and the user rejected it. Pet-specific fixes (the Kitsune's collar ring, tag and nose) live
   in a script beside the pet's .blend (`details_kitsune.py`) and run after the eyes. The eyes carry vertex groups `eye.L/R` and `eyelid.L/R`
   (the upper edge) for a blink: the rig hangs the upper edge on an eyelid bone.
4. Look at a sheet (`render_sheet.py --objects SM_Pet_<Pet>_S1 --only`), send it, adjust.
5. Size it before rigging. Then rig, animate, and only export when the user says so.

What this cannot do: invent bold colour. A pale, soft-shaded source gives a pale result. Colours are easy to
change afterwards: each one is a material named `<object> Colour NN`; change its Base Color and run
`pet_finish.py` again.

## Rigging a four-legged pet: `pet_rig.py` (built for the Kitsune Spirit, 8 Oct 2026)

`pet_rig.py --object SM_Pet_<Pet>_S1 --landmarks <pet folder>/rig_<pet>.json` does the whole rig step:

1. adds Rigify's basic quadruped metarig and moves every bone to the landmark the JSON gives it
   (left side only, the right is mirrored), plus extra bones the JSON lists (ears) and an eyelid bone
   per eye from what `pet_eyes.py` stored;
2. generates the Rigify rig (`<object>_Rig`; the metarig stays hidden as `<object>_Metarig`);
3. binds the mesh. Blender's automatic weights fail on these meshes, so each vertex follows its nearest
   bones, smoothed along the surface; `pieces` keeps a separate piece of mesh on named bones (the tail);
   `rigid` pins vertex groups to one bone (eyes and nose to the head, collar and tag to the neck).

Landmarks come from measuring the model (feet = lowest clusters, spine = slice centres along the body,
ear tips = highest points); `rig_kitsune.json` beside the Kitsune's .blend is the worked example.
Rigify names: spine.004-.008 hips to chest, spine.009-.011 neck, neck, head, spine.003/.002/.001/spine
down the tail. In the generated rig the tail controls keep those names; `head`, `neck`, `chest`, `hips`,
`front_foot_ik.L`, `foot_ik.L`, `ear.L`, `eyelid.L` are the ones an animator uses most.

**Blink.** `pet_eyes.py` gives each eye a fur-coloured eyelid strip folded along its upper edge. The
eyelid bone lies along the lid's pull: slide the `eyelid.L` / `eyelid.R` control one bone-length along its
own Y axis and the lid covers the eye. Zero is open.

Weights that hold: a vertex never follows a limb on the other side of the body (`side_margin`); under
`rigid`, `"@skin"` makes a collar bend with the neck and `"@skin_as_one"` makes a tag hang from where it is
tied. The Kitsune was stress-tested in a stride, a 55 degree head turn, head down, a spine twist, a crouch
and a tail curl with no tearing.

Animations are keyed on the controls by a script beside the pet (`anim_kitsune.py`: Idle, Blink, LookAround,
PawLift, TailCurl, SpinChase), one action each with a fake user. An action named `Idle` is the pet's resting
loop (tail sway, breathing, blinks, ear flicks at uneven moments): its last frame must equal its first, and
it need not sit on the rest pose. `Walk` and `Run` are loops too, played on the spot: each paw slides back
along the ground at one steady speed while it carries weight, and the script prints the speed the loop
matches (fox: walk 0.15 m/s, run 0.81 m/s; a paw must leave and meet the ground without a jump in speed, or the legs snap; Rigify's paw legs have almost no slack, so drop the body a few
millimetres before lengthening a step, and check leg stretch). Every other action starts and ends on the
rest pose. `test_actions.py`
checks both rules (`--loops` names the looping ones). Check one by rendering a strip of its frames from a
scratch copy.

Bones under "extra" (ears, horns) keep only the vertices along their own length (`kn.keep_to_own_length`,
done by `pet_rig.py`; `pet_tighten.py` applies it to a pet that is already rigged, live or headless, without
touching rig or animations). Without it an ear bone takes the cheek below it: nearest-bone weights on a wide
head. With `--live`, give file arguments as full paths.

Getting a model out of the artist's open, unsaved Blender session without writing from it: Blender autosaves
on its own every two minutes to `%TEMP%/<file>_<pid>_autosave.blend`. Copy that file, open the copy headless,
remove everything but the one object, and save it as the pet's own .blend. Creatures that are not pets (the
18 m Storm Sovereign) go under `G:/My Drive/KryticalNova/Art/Blender/Creatures/<Name>/` as `SM_Creature_<Name>`, with a higher triangle
budget (`pet_finish.py --max-tris 6000`). A scan's own eyes rarely survive flattening. Show the artist the result before rebuilding a face: a rebuilt
Tidecaller face was rejected outright.

A pet that keeps the look it came with (the Drone Core: the artist had the low-poly conversion undone) skips
`pet_from_scan` / `pet_finish`. Size it and rig it with a script of its own (`Pets/DroneCore/rig_dronecore.py`:
plain bones, one per rigid piece), export with `pet_export.py --own-materials`, save its texture, and package with
`make_unitypackage.py --own-texture <png> --material "<name in FBX>=texture" --material "<name>=#RRGGBB:glow"
--package-name "<Name>"`. A colour change that must play in Unity is done with bones (a second, coloured copy
of the piece that slides out over the first), not with a material animation.

A scan with limbs fused to the body (folded wings) has to be cut before it can be rigged:
`Pets/Emberwing/build_emberwing.py` is the worked example (upright cut along the flank, both sides capped, the
wing turned out about its shoulder and flipped front to back so the feathers trail; the perch removed and new
feet built). New faces have no swatch UVs until `pet_finish.py --pivot keep` is run again, so the order is
build, finish, rig. A folded wing is not a spread wing rotated: turned as one piece its feathers point forward.

`append_asset.py --from <pet>.blend` (live) copies a finished pet, its rig and its actions into the scene
the user has open. It only adds to the scene. It was tested headless; the first live run is a test.

Check a rig by posing it on a scratch copy and rendering (blink, head turn, a paw lifted, tail), never by
saving a pose into the working file. Weights are a starting point; expect to touch up shoulders and hips.

## A rigged pet (Rigify or any armature)

Synty's drones are not rigged, but a creature that bends (a fox, a bird) can be. Order of work:

1. Model, then apply rotation and scale on the mesh (Ctrl+A) and size it. Do the same on the metarig.
2. Add a Rigify metarig (Add > Armature), fit it, Generate Rig, bind the mesh to the generated rig
   (parent with automatic weights), fix the weights. `enable_addon.py --module rigify` turns Rigify on.
3. Animate with the rig's controls. One action per animation (`Idle`, `Flap`), each with a fake user so it
   is kept.
4. `pet_finish.py` gives a rigged pet its palette, shading and weight checks but does not move, resize or
   join it (that would break the binding).
5. `pet_export.py` writes a clean skeleton instead of the rig: only the deforming bones the mesh is weighted
   to, plus the bones between them and one `root` bone. On the Rigify cat that is 48 bones out of 760. Each
   action is baked onto that skeleton and written as its own take; `--actions Idle,Flap` picks some,
   `--actions none` writes the skinned model only.

In Unity: Rig tab Generic with root node `root`; tick Bake Axis Conversion on the Model tab (a rigged file
keeps Blender's -90 degree turn on the skeleton node, a static one does not). Up to four bones a vertex.
Face bones only reach the file if the mesh is weighted to them.

Headless runs of the pet tools allow the small scripts a Rigify rig stores in its .blend, because its
bones are driven by them; without them a baked animation comes out wrong.

## What the tools write

- `pet_finish.py` (`<name>` here is the model name without `SM_`): `T_<name>_Palette.png` (64 px, 16 x 16 cells of 4 px, filled from the top left in the order
  colours are met), `T_<name>_Emission.png` when something glows, `T_<name>_Palette.json`, UV layer `map1`.
- `pet_export.py`: `<name>.fbx` in metres, no rotation or scale on any node, one material `M_<name>`.

## Getting a pet into Unity: `make_unitypackage.py` (8 Oct 2026)

After the user says to export: `pet_export.py --out-dir <folder>` then
`python scripts/make_unitypackage.py --fbx <folder>/SM_Pet_<Pet>_S1.fbx --out-dir <folder>`.

It writes one `.unitypackage`: the FBX, the swatch textures, `M_KN_Swatch.mat` (URP Lit), an Animator
Controller `AC_Pet_<Pet>` (loops `Idle` by default, or rests if there is none; one trigger per other clip, each blending back) and a prefab
`Pet_<Pet>_S1`, landing in `Assets/_Game/Characters/Pets/<Pet>/` and `Assets/_Game/Characters/Shared/Swatch/`.
The project's own Unity version does the last step in batch mode on a throwaway project in the temp folder
(it takes a few minutes and never touches the game project), and its report is printed: read it.

Facts measured in Unity 6000.6.3f1, not assumed:
- **Bake Axis Conversion must be off.** With it on, a pet facing -Y in Blender faces -Z in Unity
  (backwards). Off, it faces +Z with its left on -X. The mesh and skeleton nodes carry a 90 degree turn;
  the model's root is clean.
- Rig Generic, avatar created from the model, material mode "via material description" with the FBX's
  `M_KN_Swatch` remapped to the shared material.
- Clips import at 24 fps under the action names and do not loop.
- Asset GUIDs are kept in `Assets/_ArtSource/Blender~/Swatch/unity_guids.json`; do not delete it, or the
  next pet's package will bring a second copy of the swatch material.

Not checked by the tool: how the material looks under the game's URP and comic passes (the throwaway
project has no URP), and anything about the game using the pet (`PetAssetBuilder`).

## In Unity

Texture import for the palette: Filter Mode Point, no mip maps, compression None, sRGB on (emission too).
Material: the project's lit shader with the palette as base map and the emission map if there is one.
The pet system picks a pet's model in `PetAssetBuilder`; pointing it at a new model is a code change to ask for.
