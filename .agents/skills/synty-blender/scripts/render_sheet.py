"""Render a contact sheet of the work so it can be looked at (and sent to the artist to mark up).

  run_blender.py --blend FILE.blend render_sheet.py --out sheet.png
        [--objects A,B]     default: every mesh in the SK_Work collection, else every visible mesh
        [--shapes]          Sidekick: one front and one back picture per body shape, on the reference body
        [--no-reference]    hide the Sidekick reference body
        [--hide A,B]        leave these objects out (the picture is framed on --objects; everything else
                            that is visible is drawn too, which is how a whole outfit is shown together)
        [--size 512]

Without --shapes the sheet is one row: front, right, back, three-quarter.
With --shapes it is two rows (front, back) of: neutral, feminine, heavy, muscular, skinny.
"""
import argparse
import math
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers

VIEWS = {"front": Vector((0, -1, 0.12)), "right": Vector((1, 0, 0.12)), "back": Vector((0, 1, 0.12)),
         "threeq": Vector((0.75, -1, 0.45))}
# Key weights (0..1) per column. The creator's neutral body is feminine 50 %, muscle 50 %.
SHAPES = [("neutral", {"masculineFeminine": 0.5, "defaultBuff": 0.5}),
          ("feminine", {"masculineFeminine": 1.0, "defaultBuff": 0.5}),
          ("heavy", {"masculineFeminine": 0.5, "defaultBuff": 0.5, "defaultHeavy": 1.0}),
          ("muscular", {"masculineFeminine": 0.0, "defaultBuff": 1.0}),
          ("skinny", {"masculineFeminine": 0.5, "defaultBuff": 0.0, "defaultSkinny": 1.0})]


def bounds(objects):
    pts = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    return lo, hi


def attach_bone_motion(spec, arm, target):
    """For a part bound whole to an attach bone: a function giving, for a set of body shape weights, the
    move Sidekick makes to that bone (so the part), in rig space. None for any other part."""
    import kn_fbx
    from mathutils import Euler, Matrix, Quaternion
    groups = [g.name for g in target.vertex_groups]
    table = spec.get("attach_bone_movement", {})
    if len(groups) != 1 or groups[0] not in table or groups[0] not in arm.data.bones:
        return None
    bone = groups[0]
    parent = arm.data.bones[bone].parent.name
    base = kn_fbx.read(kn.base_model_path())["bind"]
    slot = next(code for code, s in spec["slots"].items() if s["attach_bone"] == bone)
    bone_bind = None
    for cand in sorted((kn.sidekick_root() / "Resources" / "Meshes").glob(f"**/SK_*_{slot}_*.fbx")):
        bone_bind = kn_fbx.read(cand)["bind"].get(bone)
        if bone_bind:
            break
    if not bone_bind or not base.get(parent):
        return None
    P, B = kn_fbx.bind_matrix(base[parent]), kn_fbx.bind_matrix(bone_bind)
    flip = Matrix.Diagonal((-1, 1, 1, 1))          # Unity mirrors X on import
    local_u = flip @ (P.inverted() @ B) @ flip
    rows = table[bone]

    def motion(weights):
        offset, turn = Vector((0, 0, 0)), Quaternion()
        for key, row in rows.items():
            w = weights.get(key, 0.0)
            offset += Vector(row["offset_m"]) * 100.0 * w
            full = Euler([math.radians(d) for d in row["rotation_deg"]], "ZXY").to_quaternion()
            turn = turn @ Quaternion().slerp(full, w)
        moved_u = Matrix.Translation(local_u.translation + offset) @ (local_u.to_quaternion() @ turn).to_matrix().to_4x4()
        return (P @ (flip @ moved_u @ flip)) @ B.inverted()

    return motion


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--objects")
    ap.add_argument("--shapes", action="store_true")
    ap.add_argument("--no-reference", action="store_true")
    ap.add_argument("--hide", help="objects to leave out of the picture, comma separated")
    ap.add_argument("--only", action="store_true", help="draw nothing but --objects")
    ap.add_argument("--size", type=int, default=512)
    a = ap.parse_args(kn.tool_args())

    scene = bpy.context.scene
    if a.objects:
        targets = [kn.get_object(n.strip()) for n in a.objects.split(",")]
    else:
        work = bpy.data.collections.get("SK_Work")
        targets = [o for o in (work.all_objects if work else []) if o.type == "MESH"]
        if not targets:
            ref = bpy.data.collections.get(kn.REF_COLLECTION)
            skip = set(ref.all_objects) if ref else set()
            targets = [o for o in scene.objects if o.type == "MESH" and o not in skip and not o.hide_render]
    if not targets:
        raise SystemExit("Nothing to render.")

    arm = kn.find_armature()
    ref = bpy.data.collections.get(kn.REF_COLLECTION)
    ref_meshes = [o for o in (ref.all_objects if ref else []) if o.type == "MESH"]
    hidden = [bpy.data.objects[n.strip()] for n in (a.hide or "").split(",") if n.strip() in bpy.data.objects]
    restore = [(o, o.hide_render) for o in ref_meshes + hidden]
    if a.only:
        extra = [o for o in scene.objects if o not in targets and o not in ref_meshes and o not in hidden]
        restore += [(o, o.hide_render) for o in extra]
        hidden += extra
    for o in hidden:
        o.hide_render = True
    shown = [(o, o.hide_render) for o in targets]
    for o in targets:
        o.hide_render = False
    # The part replaces the body piece of its own slot, so that piece is not drawn under it.
    replaced = {t.name.split("_")[4] for t in targets if t.name.count("_") >= 5}
    for o in ref_meshes:
        o.hide_render = a.no_reference or any(code in o.name for code in replaced)
    keyed = [o for o in scene.objects if o.type == "MESH" and o.data.shape_keys and (arm is None or o.parent == arm)]
    saved_keys = [(k, k.value) for o in keyed for k in o.data.shape_keys.key_blocks]

    # Fitted parts are drawn with the swatch texture, as the game draws them.
    spec = kn.load_spec()
    swapped = []
    if arm is not None:
        mat = kn.preview_material(kn.colour_map_path(), uv_layer=spec["uv"]["swatch_layer"])
        for o in targets:
            if o.parent == arm and o.data.uv_layers.get(spec["uv"]["swatch_layer"]):
                swapped.append((o, [s.material for s in o.material_slots], [p.material_index for p in o.data.polygons]))
                o.data.materials.clear()
                o.data.materials.append(mat)

    cam_data = bpy.data.cameras.new("KN_SheetCam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("KN_SheetCam", cam_data)
    scene.collection.objects.link(cam)
    sun_data = bpy.data.lights.new("KN_SheetSun", "SUN")
    sun_data.energy = 2.5
    sun = bpy.data.objects.new("KN_SheetSun", sun_data)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("KN_SheetWorld")
    if world.node_tree is None:
        world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    old = (scene.camera, scene.world, scene.render.engine, scene.render.resolution_x, scene.render.resolution_y,
           scene.render.resolution_percentage, scene.render.filepath, scene.view_settings.view_transform)
    scene.camera, scene.world = cam, world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = a.size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"

    frame_on = targets + ([o for o in ref_meshes if a.shapes and not a.no_reference and
                           any(code in o.name for code in ("10TORS", "17HIPS"))] if a.shapes else [])
    lo, hi = bounds(frame_on)
    centre = (lo + hi) / 2
    extent = max((hi - lo)[i] for i in range(3)) * (1.45 if a.shapes else 1.2)
    if ref_meshes and not a.no_reference:
        extent = max(extent, 0.8)  # a small part is shown with enough of the body to place it
    cam_data.ortho_scale = extent
    cam_data.clip_end = 1000

    def shot(direction, path):
        d = direction.normalized()
        cam.location = centre + d * 50
        cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)

    tmp = Path(tempfile.mkdtemp(prefix="kn_sheet_"))
    rows = []
    motions = [(o, attach_bone_motion(spec, arm, o), o.matrix_basis.copy()) for o in targets] if a.shapes and arm else []
    motions = [m for m in motions if m[1]]
    try:
        if a.shapes:
            for view in ("front", "back"):
                row = []
                for label, weights in SHAPES:
                    for o, motion, _rest in motions:
                        o.matrix_basis = motion(weights)
                    for o in keyed:
                        for k in o.data.shape_keys.key_blocks[1:]:
                            if k.name in kn.BODY_KEYS:
                                k.value = weights.get(k.name, 0.0)
                    path = tmp / f"{view}_{label}.png"
                    shot(VIEWS[view], path)
                    row.append(path)
                rows.append(row)
        else:
            row = []
            for view in ("front", "right", "back", "threeq"):
                path = tmp / f"{view}.png"
                shot(VIEWS[view], path)
                row.append(path)
            rows.append(row)

        gap = 6
        cols = len(rows[0])
        sheet = np.full((len(rows) * a.size + (len(rows) + 1) * gap, cols * a.size + (cols + 1) * gap, 4), 0.1, np.float32)
        sheet[..., 3] = 1.0
        for r, row in enumerate(rows):
            for c, path in enumerate(row):
                img = bpy.data.images.load(str(path))
                px = np.array(img.pixels[:], np.float32).reshape(a.size, a.size, 4)
                y = (len(rows) - 1 - r) * a.size + (len(rows) - r) * gap  # pixel rows run bottom to top
                x = c * a.size + (c + 1) * gap
                sheet[y:y + a.size, x:x + a.size] = px
                bpy.data.images.remove(img)
        out = bpy.data.images.new("KN_Sheet", sheet.shape[1], sheet.shape[0], alpha=True)
        out.pixels = sheet.ravel()
        out.filepath_raw = str(Path(a.out))
        out.file_format = "PNG"
        Path(a.out).parent.mkdir(parents=True, exist_ok=True)
        out.save()
        bpy.data.images.remove(out)
    finally:
        for o, was_hidden in shown:
            o.hide_render = was_hidden
        for o, _motion, rest in motions:
            o.matrix_basis = rest
        for k, v in saved_keys:
            k.value = v
        for o, hidden in restore:
            o.hide_render = hidden
        for o, mats, indices in swapped:
            o.data.materials.clear()
            for m in mats:
                o.data.materials.append(m)
            o.data.polygons.foreach_set("material_index", indices)
        (scene.camera, scene.world, scene.render.engine, scene.render.resolution_x, scene.render.resolution_y,
         scene.render.resolution_percentage, scene.render.filepath, scene.view_settings.view_transform) = old
        bpy.data.objects.remove(cam)
        bpy.data.objects.remove(sun)
        bpy.data.cameras.remove(cam_data)
        bpy.data.lights.remove(sun_data)
        bpy.data.worlds.remove(world)

    order = "front, right, back, three-quarter" if not a.shapes else \
        "columns: " + ", ".join(s[0] for s in SHAPES) + "; rows: front, back"
    kn.report("Contact sheet", {"file": a.out, "objects": [o.name for o in targets], "layout": order,
                                "moved_with_attach_bone": [o.name for o, _m, _r in motions]})


main()
