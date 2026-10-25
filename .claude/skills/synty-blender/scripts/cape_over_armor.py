"""Keeps a fitted cape over the set's armor (owner, 24 Oct 2026, Scrap Sentinel: "cape is clipping in the back"; the
fit keeps a cape clear of the bare body only, and the robot's back plates came through it).

  blender -b <Gear/<Set>_Cape.blend> --python cape_over_armor.py -- --armor <Gear/<Set>_Armor.blend> --set SCRP
        [--margin 0.01] [--spread 0.06]

The armor parts (SK_KRYT_<SET>_01_*) are read from the armor scene, in the same rig space. Looking out level from
the trunk's own axis, a cape point that lies inside the armor's outer surface (or nearer it than --margin) is pushed
out to --margin over it; the push fades out over --spread metres of cloth around it, so no point spikes. It is
measured at rest and with each body key at full, and the largest push is used for the basis and every key alike
(the cape keeps its own key motion on top). Prints how many points moved and the largest push, and saves.
"""
import argparse
import math
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--armor", required=True)
    p.add_argument("--set", required=True)
    p.add_argument("--margin", type=float, default=0.01)
    p.add_argument("--spread", type=float, default=0.06)
    p.add_argument("--deepest", type=float, default=0.06,
                   help="a ray finding armor further out than this past the cape has met an arm plate along the "
                        "outstretched arm, not armor under the cloth (Scrap Sentinel: 29 cm pushes at the shoulders)")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def keyed_points(ob, key=None):
    """World positions of a mesh's vertices with one shape key at 1 (or the basis)."""
    me = ob.data
    kb = me.shape_keys.key_blocks if me.shape_keys else None
    base = kb["Basis"].data if kb else me.vertices
    pts = [v.co.copy() for v in base]
    if key and kb and key in kb:
        pts = [p + (kb[key].data[i].co - base[i].co) for i, p in enumerate(pts)]
    mw = ob.matrix_world
    return [mw @ p for p in pts]


def main():
    a = args()
    rig = bpy.data.objects["SK_Rig"]
    cape = next(o for o in bpy.data.objects if o.name.startswith(f"SK_KRYT_{a.set}_01_24ABAC"))
    with bpy.data.libraries.load(a.armor, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(f"SK_KRYT_{a.set}_01_")
                       and n.split("_")[4] in ("10TORS", "11AUPL", "12AUPR", "17HIPS")]
    armor = []
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
        mb = o.matrix_basis.copy()
        o.parent = rig
        o.matrix_parent_inverse.identity()
        o.matrix_basis = mb
        armor.append(o)
    bpy.context.view_layer.update()
    keys = [None] + [k.name for k in cape.data.shape_keys.key_blocks[1:]] if cape.data.shape_keys else [None]

    # the trunk's axis: the middle of the torso armor, level
    tp = [p for o in armor if "_10TORS_" in o.name for p in keyed_points(o)]
    ax = Vector((sum(p.x for p in tp) / len(tp), sum(p.y for p in tp) / len(tp), 0))

    n = len(cape.data.vertices)
    push = [Vector() for _ in range(n)]
    for key in keys:
        verts, tris = [], []
        for o in armor:
            pts = keyed_points(o, key)
            off = len(verts)
            verts += pts
            o.data.calc_loop_triangles()
            tris += [tuple(off + i for i in t.vertices) for t in o.data.loop_triangles]
        tree = BVHTree.FromPolygons(verts, tris)
        cp = keyed_points(cape, key)
        for i, p in enumerate(cp):
            c = Vector((ax.x, ax.y, p.z))
            d = p - c
            d.z = 0.0
            L = d.length
            if L < 1e-4:
                continue
            u = d / L
            # the armor's outermost surface along this level ray, out to well past the cape
            far = c + u * (L + 0.3)
            hit = tree.ray_cast(far, -u, L + 0.3)
            if hit[0] is None:
                continue
            outer = (hit[0] - c).dot(u)
            need = outer + a.margin - L
            if need > a.deepest:
                continue
            if need > push[i].length and need > 0:
                push[i] = u * need
    moved = [i for i in range(n) if push[i].length > 0]
    # fade the push into the cloth around it
    kd = KDTree(n)
    rest = keyed_points(cape)
    for i, p in enumerate(rest):
        kd.insert(p, i)
    kd.balance()
    final = [Vector() for _ in range(n)]
    for i in moved:
        for co, j, dist in kd.find_range(rest[i], a.spread):
            w = 0.5 * (1 + math.cos(math.pi * min(1.0, dist / a.spread)))
            cand = push[i] * w
            if cand.length > final[j].length:
                final[j] = cand
    inv = cape.matrix_world.inverted().to_3x3()
    me = cape.data
    blocks = me.shape_keys.key_blocks if me.shape_keys else []
    for j, f in enumerate(final):
        if f.length == 0:
            continue
        local = inv @ f
        me.vertices[j].co += local
        for kb in blocks:
            kb.data[j].co += local
    me.update()
    for o in armor:
        bpy.data.objects.remove(o)
    bpy.ops.wm.save_mainfile()
    print("CAPE_OVER_ARMOR", {"points_inside": len(moved), "points_moved": sum(1 for f in final if f.length > 0),
                              "largest_push_cm": round(100 * max((f.length for f in final), default=0), 2)})


main()
