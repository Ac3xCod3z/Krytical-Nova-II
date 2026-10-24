# Sidekick part rules

Measured 8 Oct 2026 from the project's Synty files. Machine-readable copy: `sidekick_spec.json` (slots with
triangle ranges and usual bones, all 209 swatches with their cell, the 88 bones with parents, the 68 face keys).
The why behind each rule: `Assets/_Game/_Docs/Reference/SyntyArt/Synty_Art_Breakdown.md`.

## A valid part

- One FBX, named like the mesh inside it: `SK_<PACK>_<SET>_<VV>_<NN><CODE>_<SP>01`.
  The game's regex: `^SK_[A-Z]+_[A-Z]+_\d{2}_\d{2}[A-Z]{4}_[A-Z]{2}\d{2}$`. Species `HU` for outfit parts.
- The whole 88-bone rig plus exactly one skinned mesh, one material.
- Mesh under the rig with no transform of its own; rig object scale 0.01, rotated 90 degrees on X; vertices in
  centimetres. Never apply the rig's transform, never edit a rest bone.
- Bones by the rig's names only. At most 4 bones a vertex. No vertex without a weight.
- Shape keys `masculineFeminine`, `defaultHeavy`, `defaultBuff`, `defaultSkinny`, exactly those spellings, on
  every part, even when empty.
- First UV layer `map1`, every face wholly inside one 1/16 cell.
- Every face smooth; hard edges at colour changes and at steep angles (about 40 degrees).
- Quads and triangles. Triangle count within the slot's range.

## Slots

| Slot | Part | Binds to | Triangles least / typical / most |
|---|---|---|---|
| 01HEAD | Head | body | 178 / 1477 / 3062 |
| 02HAIR | Hair | body (`head`), empty body keys | 272 / 1288 / 3172 |
| 03EBRL 04EBRR | Eyebrows | `head`, brow face keys | 52 / 80 / 173 |
| 05EYEL 06EYER | Eyes | `eye_l` / `eye_r` | 60 / 170 / 170 |
| 07EARL 08EARR | Ears | body | 72 / 121 / 252 |
| 09FCHR | Facial hair | body, jaw face keys | 210 / 1015 / 2138 |
| 10TORS | Torso | body | 580 / 2370 / 3678 |
| 11AUPL 12AUPR | Upper arms | body | 94 / 360 / 932 |
| 13ALWL 14ALWR | Lower arms | body | 96 / 545 / 1670 |
| 15HNDL 16HNDR | Hands | body | 252 / 424 / 680 |
| 17HIPS | Hips, down to the knee | body | 618 / 1786 / 3700 |
| 18LEGL 19LEGR | Lower legs | body | 100 / 498 / 1028 |
| 20FOTL 21FOTR | Feet | body | 138 / 320 / 562 |
| 22AHED | Head attachment | rigid `headAttach` (or `--bind body`) | 638 / 1240 / 2420 |
| 23AFAC | Face attachment | rigid `faceAttach` (or `--bind body`) | 246 / 614 / 1350 |
| 24ABAC | Back attachment | rigid `backAttach` | 548 / 1828 / 4748 |
| 25AHPF 26AHPB 27AHPL 28AHPR | Hip attachments front, back, left, right | rigid `hipAttachFront`, `hipAttachBack`, `hipAttach_l`, `hipAttach_r` | 132 / 500 / 1589 |
| 29ASHL 30ASHR | Shoulder attachments | rigid `shoulderAttach_l` / `_r` | 118 / 476 / 2340 |
| 31AEBL 32AEBR | Elbow attachments | rigid `elbowAttach_l` / `_r` | 34 / 152 / 600 |
| 33AKNL 34AKNR | Knee attachments | rigid `kneeAttach_l` / `_r` | 56 / 185 / 604 |
| 35NOSE | Nose | `head` | 56 / 106 / 182 |
| 36TETH | Teeth | `head` + `jaw`, jaw face keys | 88 / 144 / 484 |
| 37TONG | Tongue | `head` + `jaw` | 86 |
| 38WRAP | Chest wrap | body | 764 / 918 / 1072 |

"body" = weights and body keys copied from the reference body. "rigid" = every vertex on the one bone, body keys
empty; the game moves that bone when a body slider moves.

Left and right are separate parts with separate swatches. Model one, mirror it, fit each under its own name.

## Body shape keys

| Key | 0 | 100 | Neutral character |
|---|---|---|---|
| masculineFeminine | as modelled (masculine) | feminine | 50 |
| defaultBuff | as modelled (lean) | muscular | 50 |
| defaultHeavy | none | heavy | 0 |
| defaultSkinny | none | skinny | 0 |

Largest movement on the base torso: heavy 16 cm, muscular 8 cm, feminine 4 cm, skinny 3 cm. Check every part at
neutral (50 / 50) and at each extreme; `render_sheet.py --shapes` does exactly that.

Face keys (ARKit style, 68 of them, listed in the JSON) are only needed on parts that must move with the face:
at least `jawOpen` on anything covering the jaw. `sidekick_fit.py` does not create them; the artist adds them and
the tool leaves them alone.

## Swatches (material names)

A face's material name picks its swatch. Spaces, underscores, case and Blender's `.001` suffix are ignored, so
`torso_outfit_01.002` is `Torso Outfit 01`.

- **The slot's own five**: `Torso Outfit 01`-`05`, `Arm Left Outfit`, `Arm Right Outfit`, `Hand Left Outfit`,
  `Hand Right Outfit`, `Hips Outfit`, `Leg Left Outfit`, `Leg Right Outfit`, `Foot Left Outfit`,
  `Foot Right Outfit`, `Head Attachment`, `Face Attachment`, `Back Attachment`, `Shoulder Left Outfit`,
  `Shoulder Right Outfit`, `Elbow Left Outfit`, `Elbow Right Outfit`, `Knee Left Outfit`, `Knee Right Outfit`,
  `Hip Front Outfit`, `Hip Left Outfit`, `Hip Right Outfit`, `Hip Back Outfit`, each `01`-`05`.
  Use `01` for the main colour, `02` for the second, and so on: that is what lets one colour choice dress any
  outfit. (Upper and lower arm share the arm swatches.)
- **Shared materials**: `Metal 01`-`04`, `Jewellery Metal 01`-`02`, `Jewellery Gem 01`-`02`, `Glass 01`-`02`,
  `Plastic 01`-`02`, `Rubber 01`-`02`, `Strap 01`-`02`, `Stitch 01`-`02`, `Rope 01`-`02`, `Fur 01`-`02`,
  `Feathers 01`-`02`, `Paper 01`-`02`, `Wood 01`-`02`, `Concrete 01`-`02`, `Bone 01`-`02`, `Bandage 01`-`02`,
  `Glow 01`-`02`, `Digital Screen 01`-`02`, `Transparency FX 01`-`02`.
- **Body**: `Skin 01`-`03`, `Scar`, `Lips`, `Hair 01`-`02`, `Facial Hair 01`-`02`, `Fingernails Left/Right`,
  `Teeth 01`-`02`, `Horns 01`-`02`, `Flesh 01`-`02` and the eye, ear and brow swatches.
- **Elements**: `Fire`, `Lava`, `Vegetation`, `Water`, `Ice`, each `01`-`02`.

Metal looks like metal and glow emits because the matching cell of the metallic, smoothness and emission maps
says so. Picking the right swatch is picking the material, not only the colour.

Most used across Synty's parts: `Metal 02`, `Metal 03`, `Metal 01`, `Metal 04`, `Strap 01`, then each slot's
`Outfit 01` and `02`.

## Export settings (do not vary)

FBX, selected rig + mesh only, scale option **FBX_SCALE_NONE**, apply unit on, forward -Z, up Y, no leaf bones,
primary bone axis Y, secondary X, all bones (not deform-only), no baked animation, smoothing "normals only", no
tangents. Proven by round-tripping `SK_SCFI_SOLD_01_10TORS_HU01`: unit, every bone's bind matrix and every vertex
came back equal to Synty's. `FBX_SCALE_UNITS` and `FBX_SCALE_ALL` both write a 0.01 scale on the nodes and break
the match.

## Unity import settings to copy from a Synty part

Read/Write on; import blend shapes; blend shape normals None; normals Import; weld vertices off; keep quads;
max 4 bones a vertex; rig Humanoid, avatar copied from `SK_BaseModelAvatar`; do not import materials.
