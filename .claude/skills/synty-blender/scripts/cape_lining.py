"""Lines a cape's back so no opening in the cloth shows the body (owner, 24 Oct 2026, Scrap Sentinel: "it still shows
a whole bunch of holes inside the backside of the cape" - Tripo's back cloth stopped short under the mantle and its
two halves parted down the middle, so the armor showed through).

  blender -b <Gear/<Set>_Cape.blend> --python cape_lining.py -- --armor <Gear/<Set>_Armor.blend> --set SCRP
        [--bottom 0.95] [--top 1.5] [--sweep 85] [--cols 34] [--rows 22] [--inset 0.006] [--clear 0.01]

A grid of rays fans out level from the trunk's axis over the back (--sweep degrees either side of straight back,
from --bottom to --top metres). Each ray's lining point sits --inset inside the first cloth it meets; where a ray finds
no cloth (an opening) the point is filled in from its neighbours; never nearer the axis than the outermost armor
plus --clear. The grid becomes one sheet of faces in the cloth's own cell, added to the cape part; every lining point
takes the bone weights and body-key motion of the nearest cloth point, so it moves with the cloth. Saves.
"""
import argparse
import math
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--armor", required=True)
    p.add_argument("--set", required=True)
    p.add_argument("--bottom", type=float, default=0.95)
    p.add_argument("--top", type=float, default=1.5)
    p.add_argument("--sweep", type=float, default=70.0)
    p.add_argument("--cols", type=int, default=34)
    p.add_argument("--rows", type=int, default=22)
    p.add_argument("--inset", type=float, default=0.006)
    p.add_argument("--clear", type=float, default=0.01)
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def main():
    a = args()
    rig = bpy.data.objects["SK_Rig"]
    cape = next(o for o in bpy.data.objects if o.name.startswith(f"SK_KRYT_{a.set}_01_24ABAC"))
    with bpy.data.libraries.load(a.armor, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(f"SK_KRYT_{a.set}_01_")
                       and n.split("_")[4] in ("10TORS", "17HIPS")]
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
        mb = o.matrix_basis.copy()
        o.parent = rig
        o.matrix_parent_inverse.identity()
        o.matrix_basis = mb
    bpy.context.view_layer.update()
    av, at, torso = [], [], []
    for o in dst.objects:
        mw = o.matrix_world
        off = len(av)
        pts = [mw @ v.co for v in o.data.vertices]
        av += pts
        if "_10TORS_" in o.name:
            torso += pts
        o.data.calc_loop_triangles()
        at += [tuple(off + i for i in t.vertices) for t in o.data.loop_triangles]
    armor = BVHTree.FromPolygons(av, at)
    ax = Vector((sum(p.x for p in torso) / len(torso), sum(p.y for p in torso) / len(torso), 0))

    me = cape.data
    mw = cape.matrix_world
    inv = mw.inverted()
    cw = [mw @ v.co for v in me.vertices]
    me.calc_loop_triangles()
    cloth = BVHTree.FromPolygons(cw, [tuple(t.vertices) for t in me.loop_triangles])

    def farthest(tree, o, d, reach):
        far, start = None, o + d * 0.001
        while True:
            hit, _n, _f, _t = tree.ray_cast(start, d, reach - (start - o).length)
            if hit is None:
                return far
            far = (hit - o).length
            start = hit + d * 0.0005

    R, C = a.rows, a.cols
    grid = [[None] * C for _ in range(R)]
    floor = [[0.0] * C for _ in range(R)]
    dirs = []
    for c in range(C):
        ang = math.radians(-a.sweep + 2 * a.sweep * c / (C - 1))
        dirs.append(Vector((math.sin(ang), math.cos(ang), 0)))   # +y is the back
    for r in range(R):
        z = a.bottom + (a.top - a.bottom) * r / (R - 1)
        o = Vector((ax.x, ax.y, z))
        for c in range(C):
            d = dirs[c]
            fa = farthest(armor, o, d, 0.6)
            floor[r][c] = (fa or 0.0) + a.clear
            hit, _n, _f, t = cloth.ray_cast(o + d * 0.001, d, 0.8)
            if hit is not None:
                grid[r][c] = (hit - o).length - a.inset
    hit_at = [[grid[r][c] is not None for c in range(C)] for r in range(R)]

    def bounded(r, c):
        # an opening only counts when cloth surrounds it: left and right in its row, above and below in its column
        return (any(hit_at[r][:c]) and any(hit_at[r][c + 1:])
                and any(hit_at[rr][c] for rr in range(r)) and any(hit_at[rr][c] for rr in range(r + 1, R)))
    valid = [[hit_at[r][c] or bounded(r, c) for c in range(C)] for r in range(R)]
    missing = sum(1 for r in range(R) for c in range(C) if valid[r][c] and not hit_at[r][c])
    for _ in range(200):   # fill the openings from their neighbours
        todo = [(r, c) for r in range(R) for c in range(C) if grid[r][c] is None]
        if not todo:
            break
        for r, c in todo:
            nb = [grid[rr][cc] for rr, cc in ((r - 1, c), (r + 1, c), (r, c - 1), (r, c + 1))
                  if 0 <= rr < R and 0 <= cc < C and grid[rr][cc] is not None]
            if nb:
                grid[r][c] = sum(nb) / len(nb)
    pts = []
    for r in range(R):
        z = a.bottom + (a.top - a.bottom) * r / (R - 1)
        for c in range(C):
            rad = max(grid[r][c] if grid[r][c] is not None else floor[r][c], floor[r][c])
            pts.append(Vector((ax.x, ax.y, z)) + dirs[c] * rad)

    # the cloth's own dark cell, read from its largest face
    uvl = me.uv_layers.active.data
    big = max(me.polygons, key=lambda p: p.area)
    cell_uv = uvl[big.loop_indices[0]].uv.copy()

    kd = KDTree(len(cw))
    for i, p in enumerate(cw):
        kd.insert(p, i)
    kd.balance()
    near = [kd.find(p)[1] for p in pts]

    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.active
    deform = bm.verts.layers.deform.verify()
    skeys = [bm.verts.layers.shape.get(k.name) for k in me.shape_keys.key_blocks] if me.shape_keys else []
    bm.verts.ensure_lookup_table()
    sources = [bm.verts[j] for j in near]
    new, made = [], []
    for p, src in zip(pts, sources):
        lp = inv @ p
        v = bm.verts.new(lp)
        for g, w in src[deform].items():
            v[deform][g] = w
        for sk in skeys:
            v[sk] = lp + (src[sk] - src.co)
        new.append(v)
    for r in range(R - 1):
        for c in range(C - 1):
            if not (valid[r][c] and valid[r][c + 1] and valid[r + 1][c + 1] and valid[r + 1][c]):
                continue
            q = (new[r * C + c], new[r * C + c + 1], new[(r + 1) * C + c + 1], new[(r + 1) * C + c])
            f = bm.faces.new(q)
            made.append(f)
            for l in f.loops:
                l[uv].uv = cell_uv
    loose = [v for v in new if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=made)
    count = len(made)
    bm.to_mesh(me)
    bm.free()
    # outward from the trunk: flip the lining if its first face points in
    me.update()
    for p in me.polygons[len(me.polygons) - count:]:
        p.use_smooth = False
    first = me.polygons[len(me.polygons) - count]
    cen = mw @ first.center
    out = (cen - Vector((ax.x, ax.y, cen.z))).normalized()
    if (mw.to_3x3() @ first.normal).dot(out) < 0:
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        lining = bm.faces[len(bm.faces) - count:]
        bmesh.ops.reverse_faces(bm, faces=list(lining))
        bm.to_mesh(me)
        bm.free()
    me.update()
    for o in dst.objects:
        bpy.data.objects.remove(o)
    print(f"LINING faces={count} openings_filled={missing}")
    bpy.ops.wm.save_mainfile()


main()
