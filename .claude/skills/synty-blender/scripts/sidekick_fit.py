"""Turn a modelled mesh into a Sidekick part: rig space, skin weights, body shape keys, swatch UVs, shading.

  run_blender.py --blend FILE.blend sidekick_fit.py --object MyMesh
        [--name SK_..] [--slot 10TORS]      default: what sidekick_new_part.py stored in the scene
        [--bind auto|body|rigid]            body = follow the body like clothing; rigid = one attach bone
        [--default-cell "Metal 01"]         swatch for faces whose material is not a swatch name
        [--sharp-angle 40] [--keep-weights] [--keep-shapes] [--keep-uvs] [--max-influences 4]

A vertex group whose name starts with "hang" marks a piece that dangles (a tabard panel): all of it takes
the weights and body shapes of its highest point, so it hangs as one piece instead of stretching.

Safe to run again after the mesh changes: weights, shapes and UVs are rebuilt from the reference body.
"""
import argparse
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def donor_soup(arm):
    """The reference body as one triangle soup in rig space, with weights and shape deltas per vertex."""
    verts, tris, weights, deltas = [], [], [], {k: [] for k in kn.BODY_KEYS}
    for ob in bpy.data.objects:
        if ob.type != "MESH" or ob.parent != arm or not ob.name.startswith("SK_SPEC_"):
            continue
        if any(code in ob.name for code in ("02HAIR", "03EBR", "04EBR", "05EYE", "06EYE", "09FCHR")):
            continue
        me = ob.data
        base = len(verts)
        m = ob.matrix_local
        rot = m.to_3x3()
        names = [g.name for g in ob.vertex_groups]
        keys = me.shape_keys.key_blocks if me.shape_keys else None
        for v in me.vertices:
            verts.append(m @ v.co)
            weights.append({names[g.group]: g.weight for g in v.groups if g.weight > 1e-5})
        for k in kn.BODY_KEYS:
            if keys and k in keys:
                kb, basis = keys[k], keys[0]
                deltas[k].extend(rot @ (kb.data[i].co - basis.data[i].co) for i in range(len(me.vertices)))
            else:
                deltas[k].extend(Vector((0, 0, 0)) for _ in me.vertices)
        me.calc_loop_triangles()
        tris.extend(tuple(base + i for i in t.vertices) for t in me.loop_triangles)
    if not tris:
        raise SystemExit("No reference body found (meshes named SK_SPEC_* under the rig). "
                         "Start the file with sidekick_new_part.py.")
    return verts, tris, weights, deltas


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object", required=True)
    ap.add_argument("--name")
    ap.add_argument("--slot")
    ap.add_argument("--bind", choices=("auto", "body", "rigid"), default="auto")
    ap.add_argument("--default-cell")
    ap.add_argument("--sharp-angle", type=float)
    ap.add_argument("--max-influences", type=int)
    ap.add_argument("--keep-weights", action="store_true")
    ap.add_argument("--keep-shapes", action="store_true")
    ap.add_argument("--keep-uvs", action="store_true")
    a = ap.parse_args(kn.tool_args())

    spec = kn.load_spec()
    scene = bpy.context.scene
    name = a.name or scene.get("kn_part_name")
    # A name passed in carries its own slot; only fall back to the scene's slot for the scene's own part.
    from_name = name.split("_")[4] if name and name.count("_") >= 5 else None
    slot_code = a.slot or (from_name if a.name else None) or scene.get("kn_slot") or from_name
    if not name or slot_code not in spec["slots"]:
        raise SystemExit("Pass --name SK_<PACK>_<SET>_<VV>_<slot>_<SP>01 (and --slot if the name has none).")
    slot = spec["slots"][slot_code]
    problems, warnings = [], []
    if not re.match(spec["part_name_regex"], name):
        problems.append(f"'{name}' does not match the part name pattern; the game's catalog will skip it.")

    arm = kn.find_armature()
    if arm is None:
        raise SystemExit("No Sidekick rig in the file. Start it with sidekick_new_part.py.")
    ob = kn.get_object(a.object)
    me = ob.data
    if me.users > 1:
        me = ob.data = me.copy()
    if not me.polygons:
        raise SystemExit(f"'{ob.name}' has no faces.")

    # 1. Into rig space: Synty's meshes sit under the rig with no transform of their own, in centimetres.
    if ob.parent != arm or ob.matrix_local != Matrix.Identity(4):
        to_rig = arm.matrix_world.inverted() @ ob.matrix_world
        me.transform(to_rig, shape_keys=True)
        if to_rig.determinant() < 0:
            me.flip_normals()
        ob.parent = arm
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = Matrix.Identity(4)
    for mod in [m for m in ob.modifiers if m.type == "ARMATURE"]:
        ob.modifiers.remove(mod)
    if ob.modifiers:
        warnings.append("Modifiers are not exported; apply them first: " + ", ".join(m.name for m in ob.modifiers))
    ob.modifiers.new("Armature", "ARMATURE").object = arm

    bind = a.bind
    if bind == "auto":
        bind = "rigid" if slot["attach_bone"] else "body"
    if bind == "rigid" and not slot["attach_bone"]:
        raise SystemExit(f"Slot {slot_code} has no attach bone; use --bind body.")
    max_infl = a.max_influences or spec["max_bone_influences"]

    verts = [v.co.copy() for v in me.vertices]
    # Hanging pieces (a tabard panel, a tassel): every vertex group named "hang..." moves as one piece with
    # the point it hangs from, its highest vertex, so it keeps its shape on every body.
    hang = {}
    for g in ob.vertex_groups:
        if g.name.startswith("hang"):
            members = [v.index for v in me.vertices if any(e.group == g.index for e in v.groups)]
            if members:
                top = max(verts[i].y for i in members)
                high = [i for i in members if verts[i].y > top - 0.5]
                mid = sum((verts[i] for i in high), Vector()) / len(high)
                hang[g.name] = (members, min(high, key=lambda i: (verts[i] - mid).length))
    need_donor = bind == "body" and not (a.keep_weights and a.keep_shapes)
    hits, push = None, {}
    if need_donor:
        d_verts, d_tris, d_weights, d_deltas = donor_soup(arm)
        bvh = BVHTree.FromPolygons(d_verts, d_tris)
        hits, far = [], 0
        for co in verts:
            loc, _n, idx, dist = bvh.find_nearest(co)
            tri = d_tris[idx]
            hits.append((tri, poly_3d_calc([d_verts[i] for i in tri], loc)))
            far += dist > 15.0
        # A hanging piece is carried by its top point, but pushed out as far as the body pushes any of it:
        # a belly that grows under a tabard moves the whole panel clear instead of coming through it.
        hung, push = set(), {}

        def moved(hit, key):
            return sum((d_deltas[key][corner] * w for corner, w in zip(*hit)), Vector((0, 0, 0)))

        for members, anchor in hang.values():
            most = {k: max((moved(hits[i], k) for i in members), key=lambda d: d.length) for k in kn.BODY_KEYS}
            for i in members:
                push[i] = most
            for i in members:
                hits[i] = hits[anchor]
            hung.update(members)
        far = sum(1 for i, co in enumerate(verts) if i not in hung and (bvh.find_nearest(co)[3] or 0) > 15.0)
        if far:
            warnings.append(f"{far} vertices are more than 15 cm from the reference body; "
                            "their weights and body shapes are guesses. Check them, or bind those pieces rigid.")

    # 2. Skin weights.
    if not a.keep_weights:
        ob.vertex_groups.clear()
        groups = {}

        def group(bone):
            if bone not in groups:
                groups[bone] = ob.vertex_groups.new(name=bone)
            return groups[bone]

        if bind == "rigid":
            group(slot["attach_bone"]).add(range(len(verts)), 1.0, "REPLACE")
        else:
            for vi, (tri, bw) in enumerate(hits):
                acc = {}
                for corner, w in zip(tri, bw):
                    for bone, bone_w in d_weights[corner].items():
                        acc[bone] = acc.get(bone, 0.0) + w * bone_w
                top = sorted(acc.items(), key=lambda kv: -kv[1])[:max_infl]
                total = sum(w for _b, w in top) or 1.0
                for bone, w in top:
                    if w / total > 0.001:
                        group(bone).add([vi], w / total, "REPLACE")

    # 3. Body shape keys. Every part carries all four names so the character's sliders reach it.
    if not a.keep_shapes:
        if me.shape_keys:
            for k in kn.BODY_KEYS:
                kb = me.shape_keys.key_blocks.get(k)
                if kb:
                    ob.shape_key_remove(kb)
        if not me.shape_keys:
            ob.shape_key_add(name="Basis", from_mix=False)
        for k in kn.BODY_KEYS:
            kb = ob.shape_key_add(name=k, from_mix=False)
            kb.slider_min, kb.slider_max = 0.0, 1.0
            if bind == "body":
                for vi, (tri, bw) in enumerate(hits):
                    if vi in push:
                        kb.data[vi].co = verts[vi] + push[vi][k]
                        continue
                    delta = Vector((0, 0, 0))
                    for corner, w in zip(tri, bw):
                        delta += d_deltas[k][corner] * w
                    kb.data[vi].co = verts[vi] + delta
        ob.active_shape_key_index = 0
    for name_, (members, _anchor) in hang.items():
        (ob.vertex_groups.get(name_) or ob.vertex_groups.new(name=name_)).add(members, 1.0, "REPLACE")

    # 4. Swatch UVs: each face sits wholly inside the cell its material names.
    layer_name = spec["uv"]["swatch_layer"]
    cells = kn.cell_lookup(spec)
    names = kn.cell_names(spec)
    cell_of_poly = None
    if not a.keep_uvs:
        uv = me.uv_layers.get(layer_name)
        if uv is None:
            if me.uv_layers:
                me.uv_layers[0].name = layer_name
                uv = me.uv_layers[0]
            else:
                uv = me.uv_layers.new(name=layer_name)
        default = cells.get(kn.cell_key(a.default_cell)) if a.default_cell else None
        if a.default_cell and default is None:
            raise SystemExit(f"--default-cell '{a.default_cell}' is not a swatch name.")
        unknown, used = {}, {}
        cell_of_poly = [None] * len(me.polygons)
        for p in me.polygons:
            mat = me.materials[p.material_index] if p.material_index < len(me.materials) else None
            mname = mat.name if mat else "(no material)"
            cell = cells.get(kn.cell_key(mname), default)
            if cell is None:
                unknown[mname] = unknown.get(mname, 0) + 1
                continue
            cell_of_poly[p.index] = cell
            key = names[cell]
            used[key] = used.get(key, 0) + 1
            point = kn.cell_uv(*cell, grid=spec["uv"]["grid"])
            for li in p.loop_indices:
                uv.data[li].uv = point
        if unknown:
            problems.append(f"Faces with a material that is not a swatch name: {unknown}. Rename the materials to "
                            "swatch names (see references/sidekick-spec.md) or pass --default-cell.")
        me.uv_layers.active = uv
    else:
        uv = me.uv_layers.get(layer_name)
        if uv is None:
            problems.append(f"--keep-uvs but the mesh has no UV layer named '{layer_name}'.")
        else:
            cell_of_poly = [None] * len(me.polygons)
            for p in me.polygons:
                u, v = uv.data[p.loop_indices[0]].uv
                cell_of_poly[p.index] = (min(15, int(u * 16)), min(15, int(v * 16)))
            used = {}

    # 5. Shading: all faces smooth, hard edges at steep angles and colour changes.
    angle = a.sharp_angle if a.sharp_angle is not None else spec["shading"]["sharp_angle_deg"]
    hard = kn.set_hard_edges(me, angle, cell_of_poly)

    ob.name = name
    me.name = name
    kn.move_to_collection(ob, kn.ensure_collection("SK_Work"))

    # Checks.
    tris = kn.tri_count(me)
    budget = slot["tris"]
    if tris > budget["max"]:
        warnings.append(f"{tris} triangles; Synty's heaviest {slot_code} is {budget['max']} (typical {budget['median']}).")
    ngons = sum(1 for p in me.polygons if len(p.vertices) > 4)
    if ngons:
        warnings.append(f"{ngons} faces have more than four sides; triangulate them yourself so the result is yours.")
    bones = {b.name for b in arm.data.bones}
    stray = [g.name for g in ob.vertex_groups if g.name not in bones and not g.name.startswith("hang")]
    if stray:
        problems.append(f"Vertex groups with no bone in the rig: {stray}")
    bone_groups = {g.index for g in ob.vertex_groups if g.name in bones}
    unweighted = sum(1 for v in me.vertices if not any(g.weight > 1e-5 and g.group in bone_groups for g in v.groups))
    if unweighted:
        problems.append(f"{unweighted} vertices have no weight.")
    most = max((sum(1 for g in v.groups if g.weight > 1e-5 and g.group in bone_groups) for v in me.vertices), default=0)
    if most > max_infl:
        warnings.append(f"Up to {most} bones on one vertex; Unity keeps the strongest {max_infl}.")
    missing = [k for k in kn.BODY_KEYS if not me.shape_keys or k not in me.shape_keys.key_blocks]
    if missing:
        problems.append(f"Missing body shape keys: {missing}")
    extra = [k.name for k in me.shape_keys.key_blocks[1:] if k.name not in kn.BODY_KEYS
             and k.name not in spec["face_shape_keys"]] if me.shape_keys else []
    if extra:
        warnings.append(f"Shape keys the character will never drive: {extra}")

    kn.save_blend()
    kn.report("Sidekick fit", {
        "object": name, "slot": slot_code + " (" + slot["name"] + ")", "bind": bind,
        "vertices": len(me.vertices), "triangles": tris, "synty_triangles": budget,
        "bones_used": sorted(g.name for g in ob.vertex_groups if g.name in bones), "hanging_pieces": len(hang), "shape_keys": [k.name for k in me.shape_keys.key_blocks],
        "swatches_used": used, "hard_edges": f"{hard} of {len(me.edges)} (angle {angle})",
        "next": "render_sheet.py --shapes to look at it on every body shape, then sidekick_export.py."},
        problems, warnings)
    if problems:
        sys.exit(1)


main()
