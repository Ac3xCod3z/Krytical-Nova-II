"""A robot set's head piece onto the robot (owner, 24 Oct 2026, Scrap Sentinel: "for robots these technically are not
helmets but the [robot's] actual head, so fix it so it matches the art"). The piece is modelled from the head art
alone, not over a mannequin, so it is placed rather than cut out: as wide as the bare robot head times --width, centred
on it across and front to back, its top --above metres over the head's top. The game hides the bare head while this
piece is shown (CharacterAppearance, robot head pieces SK_KRYT_*_RO01).

  blender -b <robot scene.blend> --python head_place.py -- --fbx <head.fbx> --name <Set>_Helmet [--width 1.0]
        [--above 0.01] [--out <blend>]

The scene must hold the lineage's bare body (a tpose_import.py scene). An object of the same name is replaced.
Prints the scale and the head's and piece's sizes and saves (to --out, or over the scene).
"""
import argparse
import json
import re
import sys

import bpy
from mathutils import Vector

BODY_HEAD = re.compile(r"_01HEAD_")


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--width", type=float, default=1.0, help="piece width over the bare head's width")
    p.add_argument("--above", type=float, default=0.01, help="metres the piece's top stands over the head's top")
    p.add_argument("--out")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def bounds(pts):
    return (Vector([min(p[k] for p in pts) for k in range(3)]), Vector([max(p[k] for p in pts) for k in range(3)]))


def main():
    a = args()
    head = next(o for o in bpy.data.objects if o.type == "MESH" and BODY_HEAD.search(o.name)
                and not o.name.startswith(("SK_KRYT", a.name)))
    dg = bpy.context.evaluated_depsgraph_get()
    ev = head.evaluated_get(dg)
    hp = [head.matrix_world @ v.co for v in ev.to_mesh().vertices]
    ev.to_mesh_clear()
    hlo, hhi = bounds(hp)

    old = bpy.data.objects.get(a.name)
    if old:
        bpy.data.objects.remove(old)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=a.fbx)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    for o in new:
        if o != ob and o.name in bpy.data.objects:
            bpy.data.objects.remove(o)
    ob.name = ob.data.name = a.name

    me = ob.data
    plo, phi = bounds([v.co for v in me.vertices])
    s = a.width * (hhi.x - hlo.x) / (phi.x - plo.x)
    pc = Vector(((plo.x + phi.x) / 2, (plo.y + phi.y) / 2, phi.z))
    target = Vector(((hlo.x + hhi.x) / 2, (hlo.y + hhi.y) / 2, hhi.z + a.above))
    for v in me.vertices:
        v.co = target + (v.co - pc) * s
    me.update()
    nlo, nhi = bounds([v.co for v in me.vertices])
    bpy.ops.wm.save_as_mainfile(filepath=a.out or bpy.data.filepath)
    print("HEAD " + json.dumps({"scale": round(s, 4), "bare_head_cm": [round(100 * x, 1) for x in (hhi - hlo)],
                                "piece_cm": [round(100 * x, 1) for x in (nhi - nlo)],
                                "piece_bottom_z": round(nlo.z, 3), "bare_head_bottom_z": round(hlo.z, 3)}))


main()
