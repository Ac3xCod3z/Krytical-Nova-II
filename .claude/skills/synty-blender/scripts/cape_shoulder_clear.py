"""Lifts a cape's shoulders and upper back clear of the armor under it (owner, 24 Oct 2026, Scrap Sentinel: "i can
still see through it" - the yellow shoulder plates came through the cape mantle; cape_over_armor looks level out from
the trunk only, so it misses armor that sits on top of the shoulders).

  blender -b <Gear/<Set>_Cape.blend> --python cape_shoulder_clear.py -- --armor <Gear/<Set>_Armor.blend> --set SCRP
        [--above 1.2] [--margin 0.015] [--spread 0.05]

Every cape point higher than --above (metres) is compared with the nearest point of the armor (torso, upper arms,
head piece): when it lies inside the armor or nearer than --margin over it, it moves out along the armor's normal to
--margin. The move fades over --spread metres of cloth around it, so the mantle stays smooth. The basis and every
body key take the same move. Prints how many points moved and the largest move, and saves.
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
    p.add_argument("--above", type=float, default=1.2)
    p.add_argument("--margin", type=float, default=0.015)
    p.add_argument("--spread", type=float, default=0.05)
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def main():
    a = args()
    rig = bpy.data.objects["SK_Rig"]
    cape = next(o for o in bpy.data.objects if o.name.startswith(f"SK_KRYT_{a.set}_01_24ABAC"))
    with bpy.data.libraries.load(a.armor, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(f"SK_KRYT_{a.set}_01_")
                       and n.split("_")[4] in ("10TORS", "11AUPL", "12AUPR")]
    verts, tris = [], []
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
        mb = o.matrix_basis.copy()
        o.parent = rig
        o.matrix_parent_inverse.identity()
        o.matrix_basis = mb
    bpy.context.view_layer.update()
    for o in dst.objects:
        mw = o.matrix_world
        off = len(verts)
        verts += [mw @ v.co for v in o.data.vertices]
        o.data.calc_loop_triangles()
        tris += [tuple(off + i for i in t.vertices) for t in o.data.loop_triangles]
    tree = BVHTree.FromPolygons(verts, tris)

    me = cape.data
    mw, inv = cape.matrix_world, cape.matrix_world.inverted()
    world = [mw @ v.co for v in me.vertices]
    move = {}
    for i, p in enumerate(world):
        if p.z < a.above:
            continue
        hit, normal, _f, dist = tree.find_nearest(p)
        if hit is None:
            continue
        depth = (p - hit).dot(normal)
        if depth < a.margin:
            move[i] = normal * (a.margin - depth)

    # fade each move into the cloth around it (largest wins), so no point spikes out alone
    kd = KDTree(len(world))
    for i, p in enumerate(world):
        kd.insert(p, i)
    kd.balance()
    total = {}
    for i, m in move.items():
        for _co, j, d in kd.find_range(world[i], a.spread):
            w = 1.0 - d / a.spread
            mj = m * w
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
    print(f"SHOULDER_CLEAR inside={len(move)} moved={len(total)} largest_cm={biggest * 100:.1f}")
    bpy.ops.wm.save_mainfile()


main()
