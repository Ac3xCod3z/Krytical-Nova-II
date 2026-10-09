# How Synty builds Sidekick characters and drones, and how to make our own

**Written:** 8 Oct 2026. **Measured on:** the Synty files in this project, opened in Blender 5.2: 1,120 Sidekick
part files, `SK_BaseModel.fbx`, `Side_Kick_Data.db`, Sidekick's runtime scripts, and ten drone models from POLYGON
Sci-Fi City and Sci-Fi Worlds. Synty publishes no guide to making custom parts, so everything here is read from the
files, not from Synty. Nothing in the game was changed to write this.

**The Blender tools that do this work:** the `synty-blender` skill in `.claude/skills/synty-blender/`
(section 7).

---

## 1. The short version

A Sidekick character is not one model. It is up to 38 small skinned meshes ("parts") that all carry **the same
skeleton, the same four body shape keys, and the same colour-swatch UV layout**. Because those three things are
identical on every part, any part can be swapped for another at run time and the result still animates, still
follows the body sliders, and still recolours.

| What you see in the game | What makes it work |
|---|---|
| Parts swap instantly | Each part is skinned to one shared 88-bone rig; the game matches bones **by name** and merges the meshes |
| Body sliders (feminine, heavy, muscular, skinny) reshape everything | Every part has four shape keys with the **same four names**; the merge joins them into one slider each |
| Shoulder pads, backpacks, holsters ride on the body as it changes shape | They are skinned to a single **attach bone**, and that bone is moved when a slider moves |
| Colours change instantly | Every face is UV-mapped onto one cell of a 32 × 32 pixel **swatch texture**; recolouring writes four pixels |
| The low-poly look | Few polygons, no painted detail, smooth shading with **hand-chosen hard edges** |

The drones are much simpler: a plain, unrigged mesh whose faces are UV-mapped onto flat colours in the pack's
shared texture, with moving pieces as separate child objects.

---

## 2. A Sidekick part file

Every part is one FBX holding the whole skeleton and exactly one skinned mesh with one material.

### Name

`SK_<PACK>_<SET>_<VV>_<NN><CODE>_<SP>01`, for example `SK_SCFI_SOLD_01_10TORS_HU01`.

- `PACK_SET`: the outfit family (`SCFI_SOLD`, `HUMN_BASE`). Letters only.
- `VV`: variant number within the family.
- `NN CODE`: the slot, a number and four letters (table below). The number is what code reads.
- `SP`: the species it fits. `HU` is used by every outfit part; `EV` elf, `RO` robot, `SN` skeleton.

Sidekick finds parts by this pattern, and so does our own catalog (`SidekickPartCatalogBaker.PartName`). A file
that does not match is ignored.

### The 38 slots

"Follows body shapes" means Synty's parts in that slot carry real movement in the body shape keys. Where it says
no, the keys exist but are empty and the part rides on its bone.

| Slot | Part | Attach bone | Follows body shapes | Triangles (least / typical / most) |
|---|---|---|---|---|
| `01HEAD` | Head | | yes | 178 / 1477 / 3062 |
| `02HAIR` | Hair | | no | 272 / 1288 / 3172 |
| `03EBRL` `04EBRR` | Eyebrows | | no | 52 / 80 / 173 |
| `05EYEL` `06EYER` | Eyes | | no | 60 / 170 / 170 |
| `07EARL` `08EARR` | Ears | | yes | 72 / 121 / 252 |
| `09FCHR` | Facial hair | | yes | 210 / 1015 / 2138 |
| `10TORS` | Torso | | yes | 580 / 2370 / 3678 |
| `11AUPL` `12AUPR` | Upper arms | | yes | 94 / 360 / 932 |
| `13ALWL` `14ALWR` | Lower arms | | yes | 96 / 545 / 1670 |
| `15HNDL` `16HNDR` | Hands | | yes | 252 / 424 / 680 |
| `17HIPS` | Hips (to the knee) | | yes | 618 / 1786 / 3700 |
| `18LEGL` `19LEGR` | Lower legs | | yes | 100 / 498 / 1028 |
| `20FOTL` `21FOTR` | Feet | | yes | 138 / 320 / 562 |
| `22AHED` | Head attachment (helmet, hat) | `headAttach` | two in three do | 638 / 1240 / 2420 |
| `23AFAC` | Face attachment (glasses, mask) | `faceAttach` | two in three do | 246 / 614 / 1350 |
| `24ABAC` | Back attachment | `backAttach` | no | 548 / 1828 / 4748 |
| `25AHPF` `26AHPB` `27AHPL` `28AHPR` | Hip attachments front, back, left, right | `hipAttachFront` `hipAttachBack` `hipAttach_l` `hipAttach_r` | no | 132 / 500 / 1589 |
| `29ASHL` `30ASHR` | Shoulder attachments | `shoulderAttach_l` `_r` | no | 118 / 476 / 2340 |
| `31AEBL` `32AEBR` | Elbow attachments | `elbowAttach_l` `_r` | no | 34 / 152 / 600 |
| `33AKNL` `34AKNR` | Knee attachments | `kneeAttach_l` `_r` | no | 56 / 185 / 604 |
| `35NOSE` | Nose | | yes | 56 / 106 / 182 |
| `36TETH` | Teeth | | yes | 88 / 144 / 484 |
| `37TONG` | Tongue | | no | 86 |
| `38WRAP` | Chest wrap (shown on a feminine body under a bare torso) | | yes | 764 / 918 / 1072 |

A character of typical parts is roughly 13,000 triangles before attachments.

### The rig

- 88 bones, Unreal-mannequin names (`pelvis`, `spine_01..03`, `clavicle_l`, `upperarm_l`, `upperarm_twist_01_l`,
  `lowerarm_l`, `hand_l`, fingers `index_01_l`…, `thigh_l`, `calf_l`, `foot_l`, `ball_l`, `neck_01`, `head`, `jaw`,
  `eye_l`), plus the eleven attach bones above, `prop_l` / `prop_r` for held items and the `ik_` bones.
  The full list with parents is in `.claude/skills/synty-blender/references/sidekick_spec.json`.
- T-pose, arms straight out, about 1.75 m to the top of the head, facing −Y in Blender.
- Authored in Maya in **centimetres, Y up**. In Blender the rig arrives as an armature object scaled 0.01 and
  turned 90° on X, with the mesh under it and no transform of its own. That is correct; do not "apply" it.
- Up to four bones a vertex in Unity (`maxBonesPerVertex: 4`; some source files have more and Unity trims them).
- 307 of the 1,120 parts add a few bones of their own for loose pieces, named `<slot>_dyn_01_01_l` and parented
  to a rig bone (`tors_dyn_…` under `spine_03`, `abac_dyn_…` under `backAttach`). Sidekick's combiner adds bones
  it does not know under their named parent.

### How the game puts parts together

Read from `Combiner.cs` and `SidekickRuntime.cs` (Synty) and `SkinnedMeshMerger.cs` (ours):

1. The rig is taken from `SK_BaseModel`. Each part's bone list is matched to it **by bone name**.
2. The part meshes are merged into one skinned mesh (or kept separate on the one skeleton).
3. Shape keys are merged **by name**: everything after the last dot is the key's name, so `TORSBlends.defaultHeavy`
   on the torso and `LEGLBlends.defaultHeavy` on a leg become one `MESHBlends.defaultHeavy` on the character.
4. Swapping a part repeats this with a different list. Nothing is baked ahead of time.

So a new part works if, and only if: its bones have the rig's names and sit where the rig's bones sit, its shape
keys have the standard names, and it is modelled in the same space as the rest of the body.

---

## 3. Blend shapes

### The four body shapes (on every part)

| Shape key | What 0 and 100 mean | How the slider drives it |
|---|---|---|
| `masculineFeminine` | 0 = the mesh as modelled (masculine), 100 = feminine | slider −100…100 becomes (slider + 100) / 2, so the creator's middle is **50** |
| `defaultBuff` | 0 = lean as modelled, 100 = muscular | same mapping, middle is **50** |
| `defaultHeavy` | 0…100 | direct |
| `defaultSkinny` | 0…100 | direct |

Two things follow that are easy to miss:

- **The mesh you model is not the body most players will see.** The neutral character sits at feminine 50 and
  muscular 50. Model on the basis, then check at 50 / 50 and at each extreme.
- The movements are large. On the base torso the heaviest shape moves vertices up to 16 cm, muscular 8 cm,
  feminine 4 cm, skinny 3 cm. Clothing that only fits the basis will show skin through it or float.

Shape key *normals* are not imported (`blendShapeNormalImportMode: None` in the parts' import settings), so only
positions matter.

### Attachments do not deform, their bone moves

Back, hip, shoulder, elbow and knee attachments carry the four keys with nothing in them. They are skinned whole
to their attach bone, and when a slider moves, Sidekick moves that bone (`BlendshapeJointAdjustment.cs`, and the
`sk_blend_shape_rig_movement` table). A backpack therefore keeps its shape on a heavy body and simply sits
further back. Helmets and masks are the mixed case: a third ride rigidly on `headAttach` / `faceAttach`, the rest
also follow the body and jaw.

### Faces

The head carries 68 facial shape keys in the Apple ARKit style (`jawOpen`, `eyeBlinkUpperLeft`, `mouthSmileLeft`,
`browInnerUpLeft`…). Anything that must move with the face carries the matching keys with the same names:
teeth and tongue have the `jaw…` and `mouth…` keys, eyebrows the `brow…` keys, facial hair and full helmets the
jaw keys. A new beard needs at least `jawOpen` or the jaw will open through it.

---

## 4. UVs, colour and texture

### One tiny texture, no painting

The colour map is **32 × 32 pixels**: a 16 × 16 grid of swatches, each swatch 2 × 2 pixels. On the main UV layer
(`map1`) every face of every part has all its corners inside one swatch. There are no UV islands to unwrap and
nothing is painted. A face is one flat colour, and all the form comes from the model and the light.

Each swatch has a meaning, the same on every part. There are 209 named ones, in five groups:

| Group | Examples (column, row from the bottom) |
|---|---|
| Species | `Skin 01` (0,5), `Lips` (0,1), `Hair 01` (6,4), `Eye Color Left` (4,2), `Teeth 01` (3,5) |
| Outfit | `Torso Outfit 01…05` (0,15 down to 0,11), `Arm Left Outfit 01…05` (1,…), `Hips Outfit 01…05` (0,10 down to 0,6), `Foot Left Outfit 01…05` (3,…) |
| Attachment | `Head Attachment 01…05` (5,10…), `Back Attachment 01…05` (7,10…), `Shoulder Left Outfit 01…05` (5,15…) |
| Material | `Metal 01…04` (8,10…8,7), `Glass 01`, `Plastic 01`, `Rubber 01`, `Strap 01`, `Glow 01` (8,2), `Wood 01`, `Fur 01` |
| Element | `Fire 01`, `Lava 01`, `Water 01`, `Ice 01`, `Vegetation 01` |

The whole table is in `sidekick_spec.json` (`colour_cells`). A torso uses its own five `Torso Outfit` swatches for
cloth and armour colours, the shared material swatches for metal, straps and glow, and `Skin 01` where skin shows.
Because a soldier's chest plate and a viking's chest plate both sit on `Torso Outfit 01`, one colour choice dresses
any outfit.

### Why recolouring is instant

`SidekickRuntime.UpdateTexture` writes the new colour into the swatch's four pixels and re-uploads a 32 × 32
texture. Every face on that swatch, on every part, changes at once. No mesh is touched.

### A swatch is a whole material

Five more 32 × 32 textures share the same layout: metallic, smoothness, reflection, emission and opacity
(`T_MetallicMap.png` and so on, and the shader inputs `_MetallicMap`, `_SmoothnessMap`…). So `Metal 01` is shiny
and `Glow 01` emits because of what is in *their* cell of those maps. One material draws the whole character.

### The second UV layer

90 parts (body pieces and the outfits that show skin) have a second, properly unwrapped layer called `decals`,
and 131 have one called `pc`. The shader has skin, dirt, cuts, dark and eye-edge mask inputs. I did not trace
which layer each mask reads, so treat this layer as optional: our parts work without it and simply take no
decals.

---

## 5. Where the low-poly look comes from

Measured over all 1,120 parts:

- **Quads and triangles only.** Four faces in the whole set have more than four sides.
- **No flat-shaded faces at all.** Every face is smooth. The faceted look is made with **hard edges** and stored
  split normals.
- **Which edges are hard:** where the colour changes, almost always (95 to 99 % once the faces meet at more
  than 20°). Otherwise by angle:

  | Angle between the two faces | Share of edges that are hard |
  |---|---|
  | under 20° | 8 % |
  | 20 to 30° | 25 % |
  | 30 to 45° | 48 % |
  | 45 to 60° | 68 % |
  | over 60° | 87 % |

  That is an artist's hand, not a rule: gentle curves (a thigh, a cheek) stay soft, creases and plate edges are
  cut hard. A threshold of about 40° plus every colour boundary reproduces it closely, and is what the tools use
  as a starting point.
- **Bevels are modelled.** Plate edges have a real narrow face rather than a normal map. There are no normal maps.
- **Chunky proportions.** Hands, feet and gear are thick. Small detail (buckles, pouches) is a separate block
  sitting on the surface, not cut into it.
- **Body parts are closed.** 41 of 49 torsos, 39 of 43 hips and every hand have no open edges; neighbours
  overlap rather than share a seam, which is why a gap never opens when the body changes shape. Attachments are
  more often open underneath.

---

## 6. The drones

Five from POLYGON Sci-Fi City (which stand in for our four pets today) and one family from Sci-Fi Worlds.

| | What Synty did |
|---|---|
| Kind | A static mesh: no skeleton, no shape keys |
| Size | 0.7 m (`Drone_Ball`) to 2.5 m (`Drone_01`) across |
| Triangles | 650 (`Drone_01`, `_02`), 1,174 (`Ball`), 1,972 (`Insect`), 2,154 (`Quad`) |
| Shading | All faces smooth, 70 to 85 % of edges hard: harder than the characters, because they are machines |
| Colour | Every face's UVs are a **single point** on a flat colour in the pack's shared 2048 px texture (`PolygonScifi_01_A`). A drone uses 5 to 10 colours |
| Material | One, shared with the whole pack, with a separate emission texture for lights |
| Moving parts | Sci-Fi City's are one piece. Sci-Fi Worlds' `SM_Prop_Drone_01` has `_Wing_l` and `_Wing_r` as **child objects with their own pivots**, moved by a Unity animation clip (`Drone_Preset_01.anim`), not by bones |
| Units | Centimetres, like the characters |

So a pet in this style is: a blocky silhouette that reads from 10 m, 1,000 to 2,500 triangles, a handful of flat
colours, one glowing element, and wings or rotors as separate pieces pivoted where they hinge.

For our pets specifically (`Design/12_pet_evolution_system.md`): there are five growth stages per pet, the game
spaces and frames a pet from its model's own bounds, and the evolution glow is drawn round the outline by the Glow
Halo pass, so a stage model needs no glow of its own and no ground ring.

---

## 7. Making our own, in Blender

The `synty-blender` skill runs these steps. Ask Claude for any of them in plain words ("fit my chest plate to the
Sidekick body", "finish the fox pet and show me a sheet").

### A Sidekick part

1. **Start the file.** `sidekick_new_part.py` makes a .blend holding Synty's base body and rig as a locked
   reference and one material per swatch the slot uses.
2. **Model** the part round the reference body, in metres, facing −Y. Keep to the slot's triangle range. Close the
   mesh, and let it overlap its neighbours rather than stopping at their edge.
3. **Colour by material.** Give each face one of the swatch materials (`Torso Outfit 01`, `Metal 01`…). No UV work.
4. **Fit.** `sidekick_fit.py` moves the mesh into rig space, copies skin weights and the four body shape keys
   from the reference body (or binds it whole to its attach bone), writes the swatch UVs, sets the hard edges and
   names it. Tested on a Synty torso stripped bare: the copied weights came within 15 % of Synty's on average and
   the body shapes within about a fifth of the movement. That is a good start, not a finish.
5. **Look.** `render_sheet.py --shapes` draws it on the neutral, feminine, heavy, muscular and skinny bodies, front
   and back. Fix what pokes through by editing the shape keys or weights, then fit again with `--keep-shapes` /
   `--keep-weights` so your fixes stay.
6. **Export.** `sidekick_export.py` writes the FBX and checks it against `SK_BaseModel.fbx`: same unit, same bind
   pose on every bone, the four keys present, `map1` first.

Export settings matter and were proven by sending a Synty part through Blender and comparing the raw files:
scale option **FBX Scale None**, forward −Z, up Y, no leaf bones, no baked animation. The other scale options
write a 0.01 scale onto the nodes and the part no longer matches the rig.

### A pet

1. Model it in metres facing −Y, one material per colour, moving pieces as separate objects.
2. `pet_finish.py` cleans the transforms, sizes it, sets the origin, bakes the materials into a small palette
   texture (plus an emission texture if anything glows), puts each face on its colour, and sets the hard edges
   (30°).
3. `render_sheet.py` for a turnaround, `pet_export.py` for the FBX (metres, no rotation or scale on any node).

We bake our own palette per pet rather than use Synty's shared texture, so a pet does not depend on a vendor
file (`ProjectOrganization.md`, section 7).

### Where files go

| File | Folder |
|---|---|
| `.blend` sources | `Assets/_ArtSource/Blender~/` (the `~` keeps Unity from importing them) |
| Sidekick part FBX | Not in `Assets/Synty/`. Suggested: `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/<Set>/` |
| Pet FBX, palette, material, prefab | Suggested: `Assets/_Game/Characters/Pets/<Pet>/` |

### In Unity, once per new part

Copy a Synty part's import settings (select one, copy the importer preset): Read/Write on, import blend shapes,
blend shape normals **None**, normals **Import**, weld vertices off, keep quads, four bones a vertex, rig
Humanoid copied from `SK_BaseModelAvatar`, materials not imported.

### What the game needs before our parts appear (not done; code changes)

- `SidekickPartCatalogBaker` scans only `Assets/Synty/SidekickCharacters/Resources/Meshes` and needs the file
  under a `Resources` folder. It must also scan our folder.
- Synty's own Character Creator window lists parts from its database (`sk_part`), so a custom part will not show
  there. Our creator and gear use our catalog, which is what matters.
- Our `SkinnedMeshMerger` falls back to the root for a bone it does not know. Avoid extra `_dyn_` bones in our
  parts until that is handled.
- A pet's model is chosen by `PetAssetBuilder` (`StandIn`). Real models need that table pointed at them.

---

## 8. Not yet known

- Which UV layer the skin / dirt / cuts masks read (section 4).
- How a custom part behaves in Unity itself. The exported file matches Synty's bone for bone, but no custom part
  has been imported and worn in the game yet. Do that with the first real part before making a set.
- Synty's licence terms for sharing a .blend that contains their base body. Keep those files inside the team.
