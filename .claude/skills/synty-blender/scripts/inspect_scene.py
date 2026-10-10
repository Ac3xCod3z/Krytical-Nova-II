"""Describe what is in a .blend or an .fbx: objects, sizes, triangles, rig, weights, shape keys, UV swatches.

  run_blender.py --blend FILE.blend inspect_scene.py [--objects A,B] [--json out.json]
  run_blender.py inspect_scene.py --fbx FILE.fbx

Use it first on anything the artist hands over, and on Synty's own files to see how a part was made.
"""
import argparse
import json
import math
import sys
from collections import Counter
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def describe(ob, cell_names):
    me = ob.data
    pts = [ob.matrix_world @ v.co for v in me.vertices] or [Vector((0, 0, 0))]
    lo = [round(min(p[i] for p in pts), 3) for i in range(3)]
    hi = [round(max(p[i] for p in pts), 3) for i in range(3)]
    d = {"name": ob.name, "parent": ob.parent.name if ob.parent else None,
         "location": [round(x, 3) for x in ob.location],
         "rotation_deg": [round(math.degrees(x), 1) for x in ob.rotation_euler],
         "scale": [round(x, 3) for x in ob.scale],
         "world_size_m": [round(hi[i] - lo[i], 3) for i in range(3)], "world_min": lo, "world_max": hi,
         "vertices": len(me.vertices), "triangles": kn.tri_count(me),
         "ngons": sum(1 for p in me.polygons if len(p.vertices) > 4),
         "flat_faces": sum(1 for p in me.polygons if not p.use_smooth),
         "hard_edges": f"{sum(1 for e in me.edges if e.use_edge_sharp)} of {len(me.edges)}",
         "materials": [m.name if m else None for m in me.materials],
         "modifiers": [m.type for m in ob.modifiers], "uv_layers": [l.name for l in me.uv_layers]}
    names = [g.name for g in ob.vertex_groups]
    if names:
        used, most = Counter(), 0
        for v in me.vertices:
            n = 0
            for g in v.groups:
                if g.weight > 1e-5:
                    used[names[g.group]] += 1
                    n += 1
            most = max(most, n)
        d["bones_used"] = dict(used.most_common(12))
        d["max_bones_per_vertex"] = most
    if me.shape_keys:
        kb = me.shape_keys.key_blocks
        keys = {}
        for k in kb[1:]:
            keys[k.name] = round(max(((k.data[i].co - kb[0].data[i].co).length
                                      for i in range(len(me.vertices))), default=0), 3)
        body = {k: v for k, v in keys.items() if k in kn.BODY_KEYS}
        d["body_shape_keys_max_move"] = body
        other = [k for k in keys if k not in body]
        if other:
            d["other_shape_keys"] = other if len(other) <= 12 else other[:12] + [f"... {len(other)} in all"]
    if me.uv_layers:
        uv = me.uv_layers[0]
        cells, spread = Counter(), 0
        for p in me.polygons:
            us = [uv.data[li].uv[0] for li in p.loop_indices]
            vs = [uv.data[li].uv[1] for li in p.loop_indices]
            cells[(min(15, int(us[0] * 16)), min(15, int(vs[0] * 16)))] += 1
            spread += (max(us) - min(us) > 1 / 16) or (max(vs) - min(vs) > 1 / 16)
        d["swatch_cells"] = {f"{cell_names.get(c, '?')} {c}": n for c, n in cells.most_common(10)}
        d["faces_wider_than_one_cell"] = spread
    return d


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fbx")
    ap.add_argument("--objects")
    ap.add_argument("--json")
    a = ap.parse_args(kn.tool_args())
    if a.fbx:
        if not kn.is_live():
            bpy.ops.wm.read_factory_settings(use_empty=True)
        kn.import_fbx(a.fbx)
    spec = kn.load_spec()
    cell_names = kn.cell_names(spec)
    ref = bpy.data.collections.get(kn.REF_COLLECTION)
    skip = set(ref.all_objects) if ref else set()
    if a.objects:
        meshes = [kn.get_object(n.strip()) for n in a.objects.split(",")]
    else:
        meshes = [o for o in bpy.data.objects if o.type == "MESH" and o not in skip]
    rig_bones = {b["name"] for b in spec["bones"]}
    data = {"file": a.fbx or bpy.data.filepath, "blender": bpy.app.version_string,
            "sidekick_reference_present": bool(skip),
            "armatures": [{"name": o.name, "scale": [round(x, 4) for x in o.scale], "bones": len(o.data.bones),
                           "bones_not_in_sidekick_rig": [b.name for b in o.data.bones if b.name not in rig_bones][:20]}
                          for o in bpy.data.objects if o.type == "ARMATURE"],
            "meshes": [describe(o, cell_names) for o in meshes],
            "other_objects": [(o.name, o.type) for o in bpy.data.objects
                              if o.type not in ("MESH", "ARMATURE") and o not in skip]}
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(data, fh, indent=1)
    kn.report("Scene", data)


main()
