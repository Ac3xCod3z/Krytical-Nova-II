# Body sliders: how to hook them into the character creator

Written 22 Oct 2026 for the owner, to hand to a game session. The sliders exist and are tested in Blender
(`SyntyMannequinModeling.blend`, built by `.claude/skills/synty-blender/scripts/mannequin_build.py`). Nothing in the
game knows about them yet. This is the exact brief for the session that wires them in.

## What exists in Blender

Four sliders on top of Synty's four (`masculineFeminine`, `defaultBuff`, `defaultHeavy`, `defaultSkinny`):

| Slider | Where it lives | Value | On which bodies | What it does at 100 |
|---|---|---|---|---|
| `kn_bust` | shape key on `10TORS` and `38WRAP` (the chest wrap) | 0..1 in Blender, 0..100 in the game | feminine human and elf (empty key on robot and skeleton) | chest pushed out up to 4.5 cm along the surface, centred on the two points Synty's feminine key already raises; the wrap rides the skin with a 12 % margin so no skin shows |
| `kn_butt` | shape key on `17HIPS` | same | feminine human and elf (empty on robot, skeleton) | glutes pushed out up to 4.2 cm, back-facing surface between 0.76 m and 1.02 m |
| `kn_defined` | shape key on `10TORS`, both upper arms, both lower arms, `17HIPS`, both lower legs | same | masculine human and elf | existing muscle creases sharpened (unsharp mask along the normal, at most 1.1 cm), zero on a part's open edge so no seams at the joints |
| `kn_height` | custom property on the rig, driver on the `root` bone's scale X, Y, Z | 0.90 .. 1.10 (times 1.75 m: 1.58 m to 1.93 m) | every lineage, both sexes | the whole character scales uniformly from the floor; every part, hair and gear follows because it is bone scale, not a key |

Bust and butt were cut by 40 % on the owner's word (22 Oct 2026: "a little more realistic proportions"); the numbers
above are the current ones.

In Blender each mannequin's rig (`Rig_CivilianF` and so on) carries the four sliders as custom properties
(Object properties > Custom Properties: `kn_bust`, `kn_butt`, `kn_defined`, `kn_height`). Each one drives its key on
every body part and on any gear the mannequin wears, so one number moves the whole figure.

Every body part of every mannequin carries all three keys, empty where the slider does nothing (the same rule Synty
uses for its four: a part without the key would ignore the slider silently). Blender shape key = game blendshape;
the key names above are the blendshape names after Synty's `<SLOT>Blends.` prefix.

Tested in Blender: each slider alone at 0, 50, 100; bust+butt; bust+butt+heavy; defined on the lean, muscular and
heavy body; height at 0.90 and 1.10; all of it bare and in the Civilian starter outfit (`SCFI_CIVL_01`).
Pictures: `Sheets/Mannequin/*.png`; videos: `Sheets/Mannequin/*.mp4`.

## What the game has to do

The game already bakes Synty's body keys into the merged mesh at equip time (`SkinnedMeshMerger.Merge`, weights from
`BodyShapeWeights.WeightFor`, which matches blendshape names with `Contains`). The new sliders use that same path.

1. **Recipe.** `CharacterRecipe` gets three bytes `bust`, `butt`, `defined` (0..100, default 0) and one `height`
   (0..100, default 50, where 0 = 0.90 and 100 = 1.10). Bump `CharacterRecipe.CurrentVersion`; old recipes read as
   zeros and 50 (the body they have today). They travel in `CharacterNetState` like `build` and `muscle` do.
2. **Weights.** `BodyShapeWeights` gets `bust`, `butt`, `defined` and `WeightFor` returns them for names containing
   `kn_bust`, `kn_butt`, `kn_defined`. `CharacterRecipe.ToShape()` copies them straight (no halving: 0 is the body as
   modelled, unlike `buff` whose creator middle is 50). Rule for the UI: bust and butt only when `bodyType` is
   Feminine, defined only when Masculine; the other sex sends 0.
3. **Height.** Not a blendshape. The character's `root` bone (or the armature root transform the merger binds to) is
   scaled uniformly by `0.90 + 0.20 * height / 100`. Do it on the server-authored visual root so observers see it,
   and check what reads the body's size: the capsule collider height and centre, the camera pivot height, the
   name plate anchor, the mount / pet follow points, and melee reach if it is measured from a bone. These were not
   tested in Blender; they are the game's own.
4. **Gear follows.** Synty's gear FBX files do not have the three keys and Assets/Synty stays untouched, so the
   merger must give gear the motion itself: when a worn part lacks `kn_bust` / `kn_butt` / `kn_defined`, sample the
   bare body's delta for that key at the nearest point of the bare body's surface under each gear vertex and bake
   that. This is exactly what `mannequin_gear.py` does in Blender (BVH nearest point on the bare torso/hips, the
   delta interpolated across the hit triangle), and the sheets show it holds up on the starter outfit at bust 100,
   butt 100, heavy 100. Cache per (part, lineage, sex). Parts that are rigid attachments (back, hips, shoulders)
   keep their bone-move behaviour and get no key.
5. **The bare bodies themselves: where the Blender session exports them.** The keys exist only in the Blender scene
   today. When the owner gives the export go-ahead, the Blender session writes copies of the bare-body parts, with
   Synty's four keys and the three new ones, as FBX into exactly this folder:

   `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody/`

   The files keep Synty's names, so the game finds them as the same parts (26 files):
   `SK_HUMN_BASE_01_<slot>_HU01.fbx` and `SK_ELVN_BASE_01_<slot>_EV01.fbx` for every slot from `10TORS` to `21FOTR`,
   plus `38WRAP`. Robot and skeleton bodies get no copies (their keys are empty, and a missing key reads as 0).
   Nothing goes into Assets/Synty. The Blender session checks each export against Synty's base model (bones, bind
   pose, unit, key names) and copies the import settings from the Synty original (Read/Write on, blend shapes on,
   blend shape normals None, Humanoid with `SK_BaseModelAvatar`, no materials).

   What the game session does for that folder: `SidekickPartCatalogBaker.Bake` scans only `MeshRoot`
   (Assets/Synty/...). Make it scan this folder as well, **after** Synty's. `SidekickPartCatalog.Upsert` matches by
   name and keeps the part's id, so the copy replaces the original's `resourcePath` and every saved character and
   item stays valid. The Resources path differs (`Meshes/BareBody/...` against `Meshes/Species/Humans/...`), so the
   two never collide in `Resources.Load`. Re-bake the catalog after the files land. Until then the code can be built
   and tested: the new sliders simply read 0 on the Synty originals.
6. **Hair and facial hair must follow the body too (section 3 of the brief, found 22 Oct 2026).** Synty ships
   every `02HAIR` and `09FCHR` part with its four body keys empty or nearly empty, while the head swells up to 6 cm
   and the chest and shoulders up to 16 cm on the heavy body. So long hair sinks into the back and beards into the
   chest. Measured on the twelve mannequins at every slider extreme and combination: 1,681 vertices of hair, beard
   and starter gear inside the body, up to 4 cm deep. The fix in Blender (`mannequin_clip.py --fix`) gives each
   hair and beard the motion of the body under it for every body key, Synty's four and the three new ones, by
   the same nearest-surface transfer as gear. It then pushes out what is still inside, key by key and for
   combinations, by its depth plus 0.4 cm. After it: 23 vertices, none deeper than about 1 cm. The game must do the
   transfer at merge time for every hair and facial-hair part whose body keys are empty (point 4 covers gear the
   same way). The push-out is baked only into parts the Blender session exports.
   **Every creator option, measured (22 Oct 2026).** Every head, hair and facial hair the Lineage assets offer, on
   every human and elf body and head that offers it, 465 combinations, at every slider extreme and combination:

   | Hair and facial hair | Vertices inside the body | Combinations deeper than 1 cm |
   |---|---|---|
   | As Synty ships them | 46,367 | most |
   | With the merge-time transfer only | 11,355 | 116 |
   | With the Blender fix baked in | under 600, all inner hair layers | none visible |

   So the merge-time transfer is the fallback, not the answer. The 67 hairs and beards on offer are fixed in
   `Assets/_ArtSource/Blender~/Sidekick/SyntyHairFixes.blend` (each made to clear every body that can wear it). Exported
   22 Oct 2026 on the owner's go-ahead as 67 Synty-named copies (35 hairs, 32 facial hairs, each identical to its
   Synty original bone for bone, its face keys kept, all seven body keys present) into
   `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/HairFixed/`, picked up by the catalog the same way as
   `BareBody/`. A part that already carries non-empty body keys must be left as it is by the transfer.

   **Gear, measured the same way:** 394 outfit pieces of the four human and elf packs (torso, arms, hips, legs). With
   the merge-time transfer alone, 344 are clean or under 1 cm; 50 dip a few vertices (at most 23) by up to 4 cm, only
   at extreme slider combinations. Baking fixes into those is possible the same way; it is the owner's call.
7. **Creator UI.** Four sliders on the BODY page next to Build and Muscle: Bust and Butt (feminine only), Definition
   (masculine only), Height (everyone). Same widget as Build.

## What to test in the game

- Second client, not only host: the sliders arrive over the network and the other player sees them.
- Every lineage × both sexes at every slider's 0 and 100, bare and in the starter kit, then in Rare gear with a
  back attachment: no skin through clothing, no gap at the waist, attachments still on their bone.
- Height 0.90 and 1.10: collider, camera, name plate, pet follow, mount, vault and wallstride (Motion Warping
  distances assume a 1.75 m body: check both ends), doors and ladders in Crew housing.
- A saved character from before the change loads with the body it had.
- Hair and beards at every slider (section 3 of the brief: they must not clip).

## Paste this into a new session

> Read `Assets/_ArtSource/Blender~/Sidekick/BODY_SLIDERS_HANDOFF.md` in full, then CLAUDE.md's mandatory docs (project
> organization, the dependency rules, the 12Sky README and Build List). Add the four body sliders it describes
> (Bust, Butt, Definition, Height) to the game's character creator, following its "What the game has to do"
> section: recipe fields with a new recipe version, `BodyShapeWeights` and `ToShape`, Height as root scale with
> the collider, camera and name plate following, the runtime key transfer so gear, hair and facial hair follow the
> bare body, and the four sliders on the creator's BODY page. Do not export any FBX and do not touch Assets/Synty:
> the bare-body copies with the new keys come from the Blender session when the owner gives the export go-ahead, into
> `Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody/` (point 5 says how the catalog picks them up).
> Until then, test with the sliders driving Synty's own parts (bust and butt will show on gear and hair through the
> transfer only once the copies exist). Test with a second client, run the dependency check and compile check
> before committing, update the Build List row 39 and the Encyclopedia, and send the owner Game-view screenshots.
