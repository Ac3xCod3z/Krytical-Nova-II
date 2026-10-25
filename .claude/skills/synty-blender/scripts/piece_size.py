"""Sizes a split gear piece to the bare body part it covers, before the fit (owner, 24 Oct 2026, Scrap Sentinel:
"they will need to be resized to proper proportions"; the robot's head and shins are bulkier than the generated
mannequin's, the boots sank 6 cm into the shins and the fit pass shoved them into spikes).

  blender -b <Gear blend> --python piece_size.py -- --object <Set>_Helmet --part helmet [--clear 0.004] [--max 1.4]
  blender -b <Gear blend> --python piece_size.py -- --object <Set>_Boots --part boots

helmet: one uniform scale about the middle of the head, the smallest that leaves no head point outside the helmet
deeper than --clear (the head is measured inside the helmet's own reach: the neck below the helmet's rim does not
count). boots: each boot on its own (split by side), scaled about the middle of its sole on the floor, so the sole
stays on the ground, the smallest that leaves no point of the lower leg and foot poking out. Prints the scales and
saves the blend.
"""
import argparse
import json
import re
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

BODY = re.compile(r"_(01HEAD|18LEGL|19LEGR|20FOTL|21FOTR)_")


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--object", required=True)
    p.add_argument("--part", choices=("helmet", "boots"), required=True)
    p.add_argument("--clear", type=float, default=0.004, help="metres the piece must stand off the body")
    p.add_argument("--max", type=float, default=1.4, help="never scale beyond this")
    p.add_argument("--min", type=float, default=0.6, help="never shrink below this")
    p.add_argument("--margin", type=float, default=0.06,
                   help="added to the smallest clear scale, so the piece is not skin-tight")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def body_points(tags):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in bpy.data.objects:
        m = BODY.search(o.name) if o.type == "MESH" else None
        if m and m.group(1) in tags and not o.name.startswith(("ScrapSentinel", "SK_KRYT")):
            ev = o.evaluated_get(dg)
            me = ev.to_mesh()
            pts += [o.matrix_world @ v.co for v in me.vertices]
            ev.to_mesh_clear()
    return pts


def poking(piece_verts, faces, pts, clear, zlo, zhi, centre):
    """Body points lying beyond the piece: a ray from the body part's middle (`centre(p)`) out through the point hits
    the piece's inner surface nearer than the point itself (plus `clear`). Rays that find no piece (a face opening, the
    helmet's open bottom) do not count. Works on Tripo's plates whatever way their faces point."""
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in piece_verts]
    for f in faces:
        try:
            bm.faces.new([vs[i] for i in f])
        except ValueError:
            pass
    tree = BVHTree.FromBMesh(bm)
    out, worst = 0, 0.0
    for p in pts:
        if not (zlo <= p.z <= zhi):
            continue
        c = centre(p)
        d = p - c
        L = d.length
        if L < 1e-4:
            continue
        hit, _n, _i, t = tree.ray_cast(c, d / L, L + 0.10)
        if hit is not None and t < L + clear:
            out += 1
            worst = max(worst, L + clear - t)
    bm.free()
    return out, worst


def grow(me, mw, idx, pivot, pts, a, zlo, zhi, centre):
    base = [mw @ me.vertices[i].co for i in idx]
    remap = {i: k for k, i in enumerate(idx)}
    faces = [[remap[v] for v in p.vertices] for p in me.polygons if all(v in remap for v in p.vertices)]
    ok_n = max(2, len(pts) // 400)

    def test(s):
        moved = [pivot + (p - pivot) * s for p in base]
        return poking(moved, faces, pts, a.clear, zlo(s), zhi(s), centre)

    best = test(1.0)
    n = best[0]
    s = 1.0
    if n <= ok_n:
        # already clear: as small as it can be and still clear, plus --margin (a generated piece is often oversized:
        # the Scrap Sentinel boots were 28 cm wide round a 13 cm leg)
        while s - 0.02 >= a.min - 1e-6:
            n2, _w = test(s - 0.02)
            if n2 > ok_n:
                break
            s -= 0.02
        s = min(1.0, s + a.margin)
        n = test(s)[0]
    else:
        while n > ok_n and s + 0.02 <= a.max + 1e-6:
            s += 0.02
            n = test(s)[0]
    inv = mw.inverted()
    for i, p in zip(idx, base):
        me.vertices[i].co = inv @ (pivot + (p - pivot) * s)
    return round(s, 3), {"poking_before": best[0], "worst_cm_before": round(100 * best[1], 1), "poking_after": n}


def main():
    a = args()
    ob = bpy.data.objects[a.object]
    me, mw = ob.data, ob.matrix_world
    out = {}
    if a.part == "helmet":
        head = body_points({"01HEAD"})
        idx = list(range(len(me.vertices)))
        P = [mw @ v.co for v in me.vertices]
        rim = min(p.z for p in P)
        hz = [p for p in head if p.z >= rim]
        c = Vector([(min(p[k] for p in hz) + max(p[k] for p in hz)) / 2 for k in range(3)])
        # first onto the head's middle across and front to back (the Scrap Sentinel helmet sat 5 cm forward of the
        # robot head: out at the visor, short at the back)
        hc = Vector([(min(p[k] for p in P) + max(p[k] for p in P)) / 2 for k in range(3)])
        shift = Vector((c.x - hc.x, c.y - hc.y, 0))
        inv = mw.inverted()
        for v, p in zip(me.vertices, P):
            v.co = inv @ (p + shift)
        out["moved_cm"] = [round(100 * x, 1) for x in shift]
        out["Helmet"] = grow(me, mw, idx, c, head, a, lambda s: c.z + (rim - c.z) * s + 0.01, lambda s: 9.0,
                             lambda p: c)
    else:
        legs = {side: body_points(tags) for side, tags in ((1, {"18LEGL", "20FOTL"}), (-1, {"19LEGR", "21FOTR"}))}
        mids = {side: sum(p.x for p in pts) / len(pts) for side, pts in legs.items()}
        for side, leg in legs.items():
            # this boot: the vertices nearer this leg than the other (the body may stand anywhere in the scene)
            idx = [i for i, v in enumerate(me.vertices)
                   if abs((mw @ v.co).x - mids[side]) < abs((mw @ v.co).x - mids[-side])]
            P = [mw @ me.vertices[i].co for i in idx]
            floor = min(p.z for p in P)
            top = max(p.z for p in P)
            pivot = Vector(((min(p.x for p in P) + max(p.x for p in P)) / 2,
                            (min(p.y for p in P) + max(p.y for p in P)) / 2, floor))
            ax = Vector((sum(p.x for p in leg) / len(leg), sum(p.y for p in leg) / len(leg), 0))
            out["Boot " + ("L" if side > 0 else "R")] = grow(
                me, mw, idx, pivot, leg, a, lambda s: floor + 0.03, lambda s, t=top: floor + (t - floor) * s - 0.02,
                lambda p, ax=ax: Vector((ax.x, ax.y, p.z)))       # out from the leg's own axis, level
    me.update()
    bpy.ops.wm.save_mainfile()
    print("SIZE " + json.dumps(out))


main()
