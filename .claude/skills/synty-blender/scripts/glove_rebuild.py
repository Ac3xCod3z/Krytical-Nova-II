"""glove_rebuild.py: a Tripo glove made to bend like a Synty glove.

  blender -b <Gloves_Faceted.blend> --python glove_rebuild.py -- --set DOCK --out <Gloves_Faceted_v2.blend>
        [--body SCFI_ROBO_04_{slot}_RO01]   the bare hand of another lineage (default the human one)

The Dock Runner glove's hand rebuilt on Sidekick's own hand (owner 24 Oct 2026: the Tripo fingers were loose plates
that splintered when the hand closed; Synty's gloves are clean mitts). The hand is the bare hand pushed out a few
millimetres, wearing the Tripo glove's colours where it lies under them; the Tripo cuff is kept above the wrist."""
import bpy, bmesh, sys
from collections import Counter
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import argparse
_a = argparse.ArgumentParser()
_a.add_argument("--set", required=True, help="set code in the part names, e.g. DOCK (SK_KRYT_<SET>_01_15HNDL_HU01)")
_a.add_argument("--out", required=True, help="the .blend to save the rebuilt gloves into")
_a.add_argument("--body", default="HUMN_BASE_01_{slot}_HU01",
                help="the bare part under the glove; robots: SCFI_ROBO_04_{slot}_RO01")
_a.add_argument("--no-cuff", action="store_true",
                help="drop Tripo's cuff too (Scrap Sentinel: it was shards, and the robot's wrist ring is its own)")
_a.add_argument("--plate-colours", action="store_true",
                help="one colour per plate of the hand (each loose piece of the bare hand), the commonest under it")
_args = _a.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
SAVE = _args.out
PUFF = 0.004        # metres the glove stands off the skin
CUFF_PAST = 0.012   # metres past the wrist joint the Tripo cuff is kept
rig = bpy.data.objects["SK_Rig"]
SP = _args.body.rsplit("_", 1)[1]                    # HU01, RO01: the parts' own suffix


def bare(slot):
    return "SK_" + _args.body.format(slot=slot)

for side, hand_name, bone in (("15HNDL", bare("15HNDL"), "l"), ("16HNDR", bare("16HNDR"), "r")):
    glove = bpy.data.objects[f"SK_KRYT_{_args.set}_01_{side}_{SP}"]
    base = bpy.data.objects[hand_name]
    gw = glove.matrix_world
    # the glove's surface (rest shape) and each face's colour cell, for the new hand's colours
    gbm = bmesh.new()
    gbm.from_mesh(glove.data)
    gbm.transform(gw)
    gtree = BVHTree.FromBMesh(gbm)
    guv = glove.data.uv_layers.active.data
    gcell = []
    for p in glove.data.polygons:
        u = sum(guv[i].uv.x for i in p.loop_indices) / p.loop_total
        v = sum(guv[i].uv.y for i in p.loop_indices) / p.loop_total
        gcell.append((u, v))

    # 1. the new hand: a copy of the bare hand, pushed out along its normals (every body shape too)
    hand = base.copy()
    hand.data = base.data.copy()
    bpy.context.scene.collection.objects.link(hand)
    me = hand.data
    scale = hand.matrix_world.to_scale().x
    push = PUFF / max(scale, 1e-6)
    normals = [v.normal.copy() for v in me.vertices]
    if me.shape_keys:
        for kb in me.shape_keys.key_blocks:
            for i, d in enumerate(kb.data):
                d.co += normals[i] * push
    for i, v in enumerate(me.vertices):
        v.co += normals[i] * push
    me.materials.clear()
    for m in glove.data.materials:
        me.materials.append(m)
    for p in me.polygons:
        p.material_index = 0
        p.use_smooth = False
    # colours: the glove's cell under each face, then lone faces take their neighbours'
    hw = hand.matrix_world
    pick = []
    for p in me.polygons:
        c = hw @ p.center
        _loc, _n, fi, _d = gtree.find_nearest(c)
        pick.append(gcell[fi] if fi is not None else gcell[0])
    nb = [set() for _ in me.polygons]
    by_edge = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            by_edge.setdefault(ek, []).append(p.index)
    for fs in by_edge.values():
        for a in fs:
            for b in fs:
                if a != b:
                    nb[a].add(b)
    for _ in range(2):
        out = list(pick)
        for i in range(len(pick)):
            cnt = Counter(pick[j] for j in nb[i])
            if cnt and cnt.get(pick[i], 0) == 0:
                out[i] = cnt.most_common(1)[0][0]
        pick = out
    if _args.plate_colours:
        # each loose piece of the bare hand (a robot's finger segment) takes the colour most of it lies under
        comp = list(range(len(me.polygons)))
        def root(i):
            while comp[i] != i:
                comp[i] = comp[comp[i]]
                i = comp[i]
            return i
        for fs in by_edge.values():
            for b in fs[1:]:
                comp[root(b)] = root(fs[0])
        votes = {}
        for p in me.polygons:
            votes.setdefault(root(p.index), Counter())[pick[p.index]] += p.area
        pick = [votes[root(i)].most_common(1)[0][0] for i in range(len(pick))]
    uv = me.uv_layers.active.data
    for p in me.polygons:
        for li in p.loop_indices:
            uv[li].uv = pick[p.index]

    # 2. the Tripo glove keeps only its cuff: what lies before the wrist (plus a little)
    lower = rig.pose.bones[f"lowerarm_{bone}"]
    wrist_b = rig.pose.bones[f"hand_{bone}"]
    wrist = rig.matrix_world @ wrist_b.head
    axis = ((rig.matrix_world @ wrist_b.head) - (rig.matrix_world @ lower.head)).normalized()
    bpy.ops.object.select_all(action="DESELECT")
    glove.select_set(True)
    bpy.context.view_layer.objects.active = glove
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(glove.data)
    gone = [f for f in bm.faces if _args.no_cuff or (gw @ f.calc_center_median() - wrist).dot(axis) > CUFF_PAST]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.update_edit_mesh(glove.data)
    bpy.ops.object.mode_set(mode="OBJECT")
    kept = len(glove.data.polygons)

    # 3. one piece: the cuff and the new hand
    hand.select_set(True)
    glove.select_set(True)
    bpy.context.view_layer.objects.active = glove
    bpy.ops.object.join()
    for p in glove.data.polygons:
        p.use_smooth = False
    print(f"Q {glove.name}: cuff {kept} faces + hand {len(me.polygons)} faces -> {len(glove.data.polygons)}")
    gbm.free()


# Body shapes from the bare hand and forearm under the glove: Tripo's transferred keys moved fingers up to 9 cm
# (the bare hand moves 4) and bust / butt moved glove points, which splintered the fingers in game.
from mathutils.bvhtree import BVHTree as _BVH
from mathutils.interpolate import poly_3d_calc
for side, hand, arm in (("15HNDL", bare("15HNDL"), bare("13ALWL")), ("16HNDR", bare("16HNDR"), bare("14ALWR"))):
    glove = bpy.data.objects[f"SK_KRYT_{_args.set}_01_{side}_{SP}"]
    # the source surface: hand and forearm, in world space, with each key's offsets per vertex
    verts, faces, keys = [], [], {}
    names = [k.name for k in glove.data.shape_keys.key_blocks[1:]]
    for n in (hand, arm):
        o = bpy.data.objects[n]
        mw = o.matrix_world
        kb = o.data.shape_keys.key_blocks if o.data.shape_keys else None
        base = [mw @ v.co for v in (kb["Basis"].data if kb else o.data.vertices)]
        off = len(verts)
        verts += base
        faces += [[off + i for i in p.vertices] for p in o.data.polygons]
        for name in names:
            if kb and name in kb:
                d = [(mw @ kb[name].data[i].co) - base[i] for i in range(len(base))]
            else:
                d = [Vector() for _ in base]
            keys.setdefault(name, []).extend(d)
    tree = _BVH.FromPolygons(verts, faces)
    gmw = glove.matrix_world
    ginv = gmw.inverted()
    gkb = glove.data.shape_keys.key_blocks
    gbase = [gmw @ v.co for v in gkb["Basis"].data]
    worst = {}
    for i, p in enumerate(gbase):
        hit, _n, fi, _d = tree.find_nearest(p)
        f = faces[fi]
        w = poly_3d_calc([verts[j] for j in f], hit)
        for name in names:
            delta = sum((keys[name][j] * wj for j, wj in zip(f, w)), Vector())
            gkb[name].data[i].co = ginv @ (p + delta)
            worst[name] = max(worst.get(name, 0.0), delta.length)
    print("Q", glove.name, {k: round(v, 4) for k, v in worst.items()})

bpy.ops.wm.save_as_mainfile(filepath=SAVE)
