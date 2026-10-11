"""Shared helpers for the synty-blender tools. Imported inside Blender (bpy available)."""
import json
import math
import os
import sys
from pathlib import Path

import bpy

SKILL_DIR = Path(__file__).resolve().parent.parent
SPEC_PATH = SKILL_DIR / "references" / "sidekick_spec.json"
REF_COLLECTION = "SK_Reference"
PREVIEW_MATERIAL = "SK_Preview"
BODY_KEYS = ("masculineFeminine", "defaultHeavy", "defaultBuff", "defaultSkinny")


def tool_args():
    """Arguments after `--` (headless) or the ones the live bridge handed over."""
    argv = sys.argv
    return argv[argv.index("--") + 1:] if "--" in argv else []


def project_root():
    env = os.environ.get("KN_PROJECT_ROOT")
    if env:
        return Path(env)
    for parent in SKILL_DIR.parents:
        if (parent / "Assets" / "Synty" / "SidekickCharacters").is_dir():
            return parent
    raise SystemExit("Project root not found. Set KN_PROJECT_ROOT to the folder that holds Assets/.")


def sidekick_root():
    return project_root() / "Assets" / "Synty" / "SidekickCharacters"


def base_model_path():
    return sidekick_root() / "Resources" / "Meshes" / "SK_BaseModel.fbx"


def colour_map_path():
    return sidekick_root() / "Resources" / "Textures" / "T_ColorMap.png"


def load_spec():
    with open(SPEC_PATH, encoding="utf-8") as fh:
        return json.load(fh)


def cell_key(name):
    """'Torso Outfit 01', 'torso_outfit_01' and 'Torso Outfit 01.002' are the same swatch."""
    name = name.lower()
    if len(name) > 4 and name[-4] == "." and name[-3:].isdigit():
        name = name[:-4]
    return "".join(ch for ch in name if ch.isalnum())


def cell_lookup(spec):
    """Colour cell key (see cell_key) -> (u, v)."""
    return {cell_key(c["name"]): (c["u"], c["v"]) for c in spec["colour_cells"]}


def cell_names(spec):
    return {(c["u"], c["v"]): c["name"] for c in spec["colour_cells"]}


def cell_uv(u, v, grid=16):
    """UV of the middle of a swatch cell."""
    return ((u + 0.5) / grid, (v + 0.5) / grid)


def is_live():
    return not bpy.app.background


def save_blend(path=None):
    """Save in headless runs. A live session is left for the artist to save."""
    if path:
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
    elif bpy.data.filepath and not is_live():
        bpy.ops.wm.save_mainfile()


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=str(path))
    return [o for o in bpy.data.objects if o not in before]


def find_armature():
    """The Sidekick rig in the scene: the armature that owns a `spine_03` bone."""
    for ob in bpy.data.objects:
        if ob.type == "ARMATURE" and "spine_03" in ob.data.bones and "root" in ob.data.bones:
            return ob
    return None


def get_object(name, kind="MESH"):
    ob = bpy.data.objects.get(name)
    if ob is None or ob.type != kind:
        have = sorted(o.name for o in bpy.data.objects if o.type == kind)
        raise SystemExit(f"No {kind.lower()} object named '{name}'. In the file: {have}")
    return ob


def ensure_collection(name, parent=None):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(col)
    return col


def move_to_collection(ob, col):
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    col.objects.link(ob)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def set_hard_edges(me, angle_deg, cell_of_poly=None):
    """Synty's shading: every face smooth, hard edges where the angle is steep or the colour changes."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    limit = math.radians(angle_deg)
    hard = 0
    for e in bm.edges:
        sharp = False
        if len(e.link_faces) == 2:
            if e.calc_face_angle(0.0) >= limit:
                sharp = True
            elif cell_of_poly is not None and cell_of_poly[e.link_faces[0].index] != cell_of_poly[e.link_faces[1].index]:
                sharp = True
        e.smooth = not sharp
        hard += sharp
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()
    me.update()
    return hard


def preview_material(image_path, name=PREVIEW_MATERIAL, uv_layer=None):
    """One material that samples a swatch texture without filtering, as the game shader does."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(image_path), check_existing=True)
    tex.interpolation = "Closest"
    if uv_layer:
        uv = nt.nodes.new("ShaderNodeUVMap")
        uv.uv_map = uv_layer
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    bsdf.inputs["Roughness"].default_value = 0.8
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def material_colour(mat):
    """Base colour of a material as linear RGBA, and its emission as linear RGB times strength."""
    col, emis = tuple(mat.diffuse_color), (0.0, 0.0, 0.0)
    if mat.use_nodes and mat.node_tree:
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                col = tuple(node.inputs["Base Color"].default_value)
                strength = node.inputs["Emission Strength"].default_value if "Emission Strength" in node.inputs else 0.0
                if strength > 0.0 and "Emission Color" in node.inputs:
                    e = node.inputs["Emission Color"].default_value
                    emis = (e[0] * min(strength, 1.0), e[1] * min(strength, 1.0), e[2] * min(strength, 1.0))
                break
    return col, emis


def report(title, data, problems=(), warnings=()):
    """Print a result block the calling agent reads back."""
    print("\n=== " + title + " ===")
    print(json.dumps(data, indent=1, default=str))
    for w in warnings:
        print("WARNING: " + w)
    for p in problems:
        print("PROBLEM: " + p)
    print("RESULT: " + ("FAILED" if problems else "OK"))


def bound_rig(ob):
    """The armature a mesh is skinned to (through its Armature modifier), or None."""
    for mod in ob.modifiers:
        if mod.type == "ARMATURE" and mod.object is not None:
            return mod.object
    return None


def swatch_dir():
    return project_root() / "Assets" / "_ArtSource" / "Blender~" / "Swatch"


def load_swatch(page="kn"):
    """The Krytical Nova swatch table (see make_swatch.py), or None if it has not been made.
    page "synty" is the second page: Synty's own sci-fi colours."""
    tag = "_Synty" if page == "synty" else ""
    path = swatch_dir() / f"KN_Swatch{tag}.json"
    if not path.is_file():
        return None
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    for c in data["cells"]:
        c["rgb"] = tuple(int(c["colour"][i:i + 2], 16) / 255 for i in (1, 3, 5))
    data["image"] = str(swatch_dir() / f"T_KN_Swatch{tag}.png")
    data["page"] = page
    return data


def linear_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def nearest_swatch(swatch, linear_rgb, glow=False, metal=False):
    """The swatch cell closest to a colour as the eye sees it. Glowing and metal materials only match
    glow and metal cells, and plain ones never do, because those cells also carry emission / metalness."""
    r, g, b = (linear_to_srgb(x) for x in linear_rgb[:3])
    best, best_d = None, 1e9
    plain_page = swatch.get("page") == "synty"            # that page has no glow or metal cells: everything matches as a plain colour
    for c in swatch["cells"]:
        if not plain_page and (c["glow"] != glow or c["metal"] != metal):
            continue
        cr, cg, cb = c["rgb"]
        mean = (r + cr) / 2
        d = (2 + mean) * (r - cr) ** 2 + 4 * (g - cg) ** 2 + (3 - mean) * (b - cb) ** 2
        if d < best_d:
            best, best_d = c, d
    return best, best_d ** 0.5


def zero_controls(rig):
    """Put a rig's controls back to neutral. Only the controls: Rigify keeps set-up rotations on some of
    its helper bones (the front paws' MCH bones), and clearing those folds the limb."""
    for pb in rig.pose.bones:
        if pb.name.startswith(("ORG-", "MCH-", "DEF-", "VIS")):
            continue
        pb.location = (0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_axis_angle = (0, 0, 1, 0)
        pb.scale = (1, 1, 1)


def keep_to_own_length(ob, rig, bone, parent, start=-0.05, full=0.2):
    """Stop an appendage bone (an ear, a horn, a fin) from pulling on the body it sits on.

    Nearest-bone weights give the bone every vertex that is closer to it than to the body's own bone,
    which on a wide head takes in the cheek below an ear. Here the bone keeps a vertex only where the
    vertex lies along the bone's own length: none of it behind `start`, all of it past `full` (both as a
    share of the bone's length from its base), blended in between. What it gives up goes to `parent`.
    Returns how many vertices changed."""
    name = bone if bone.startswith("DEF-") else "DEF-" + bone
    parent_name = parent if parent.startswith("DEF-") else "DEF-" + parent
    group = ob.vertex_groups.get(name)
    if group is None or name not in rig.data.bones:
        return 0
    parent_group = ob.vertex_groups.get(parent_name) or ob.vertex_groups.new(name=parent_name)
    b = rig.data.bones[name]
    to_rig = rig.matrix_world.inverted() @ ob.matrix_world
    base, along = b.head_local, b.tail_local - b.head_local
    changed = 0
    for v in ob.data.vertices:
        mine = next((e.weight for e in v.groups if e.group == group.index), 0.0)
        if mine <= 0.0:
            continue
        t = ((to_rig @ v.co) - base).dot(along) / max(along.length_squared, 1e-12)
        k = max(0.0, min(1.0, (t - start) / max(full - start, 1e-6)))
        keep = mine * k * k * (3 - 2 * k)
        if mine - keep < 1e-4:
            continue
        had = next((e.weight for e in v.groups if e.group == parent_group.index), 0.0)
        if keep > 0.005:
            group.add([v.index], keep, "REPLACE")
        else:
            group.remove([v.index])
            keep = 0.0
        parent_group.add([v.index], had + mine - keep, "REPLACE")
        changed += 1
    return changed
