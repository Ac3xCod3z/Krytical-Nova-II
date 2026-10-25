"""Puts every point of a cape's back outside the armor, measured by rays from the trunk (owner, 24 Oct 2026, Scrap
Sentinel: "it still shows a whole bunch of holes inside the backside of the cape" - armor came through the folds where
the mantle overlaps the back cloth; the nearest-point test of cape_shoulder_clear misreads points deep inside a plate).

  blender -b <Gear/<Set>_Cape.blend> --python cape_back_clear.py -- --armor <Gear/<Set>_Armor.blend> --set SCRP
        [--margin 0.015] [--deepest 0.15] [--spread 0.05] [--width 0.3]

For each cape point behind the trunk (y past the torso's middle, |x| under --width), a ray goes from the trunk's
axis (same height) through the point and on; the farthest armor it crosses within --deepest past the point marks
where the armor ends, and the point moves out along the ray to --margin past it. Moves fade over --spread metres of
cloth (largest wins). Basis and body keys take the same move. Prints counts and saves.
"""
import argparse
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--armor", required=True)
    p.add_argument("--set", required=True)
    p.add_argument("--margin", type=float, default=0.015)
    p.add_argument("--deepest", type=float, default=0.15)
    p.add_argument("--spread", type=float, default=0.05)
    p.add_argument("--width", type=float, default=0.3)
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def main():
    a = args()
    rig = bpy.data.objects["SK_Rig"]
    cape = next(o for o in bpy.data.objects if o.name.startswith(f"SK_KRYT_{a.set}_01_24ABAC"))
    with bpy.data.libraries.load(a.armor, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(f"SK_KRYT_{a.set}_01_")
                       and n.split("_")[4] in ("10TORS", "11AUPL", "12AUPR", "17HIPS")]
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
        mb = o.matrix_basis.copy()
        o.parent = rig
        o.matrix_parent_inverse.identity()
        o.matrix_basis = mb
    bpy.context.view_layer.update()
    verts, tris, torso = [], [], []
    for o in dst.objects:
        mw = o.matrix_world
        off = len(verts)
        pts = [mw @ v.co for v in o.data.vertices]
        verts += pts
        if "_10TORS_" in o.name:
            torso += pts
        o.data.calc_loop_triangles()
        tris += [tuple(off + i for i in t.vertices) for t in o.data.loop_triangles]
    tree = BVHTree.FromPolygons(verts, tris)
    ax = Vector((sum(p.x for p in torso) / len(torso), sum(p.y for p in torso) / len(torso), 0))

    me = cape.data
    mw, inv = cape.matrix_world, cape.matrix_world.inverted()
    world = [mw @ v.co for v in me.vertices]
    move = {}
    for i, p in enumerate(world):
        if p.y < ax.y or abs(p.x - ax.x) > a.width:
            continue
        o = Vector((ax.x, ax.y, p.z))
        d = p - o
        r = d.length
        if r < 1e-4:
            continue
        d.normalize()
        far, start = None, o + d * 0.001
        reach = r + a.deepest
        while True:
            hit, _n, _f, t = tree.ray_cast(start, d, reach - (start - o).length)
            if hit is None:
                break
            far = (hit - o).length
            start = hit + d * 0.0005
        if far is not None and far + a.margin > r:
            move[i] = d * (far + a.margin - r)

    kd = KDTree(len(world))
    for i, p in enumerate(world):
        kd.insert(p, i)
    kd.balance()
    total = {}
    for i, m in move.items():
        for _co, j, dd in kd.find_range(world[i], a.spread):
            mj = m * (1.0 - dd / a.spread)
            if j not in total or mj.length > total[j].length:
                total[j] = mj
    rot = inv.to_3x3()
    blocks = me.shape_keys.key_blocks if me.shape_keys else []
    for j, m in total.items():
        lm = rot @ m
        me.vertices[j].co += lm
        for kb in blocks:
            kb.data[j].co += lm
    me.update()
    for o in dst.objects:
        bpy.data.objects.remove(o)
    biggest = max((m.length for m in move.values()), default=0.0)
    print(f"BACK_CLEAR under_armor={len(move)} moved={len(total)} largest_cm={biggest * 100:.1f}")
    bpy.ops.wm.save_mainfile()


main()
