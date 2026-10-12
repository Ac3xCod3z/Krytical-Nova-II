"""Kitsune Spirit, the 50 %, 75 % and 100 % forms: does any tail pass through another, or through the body, in any clip?

  blender -b SM_Pet_KitsuneSpirit_100.blend -P tail_clip_check.py [-- Clip Clip ...]        (reads only; saves nothing)

The owner, 12 Oct 2026: no tail may clip in any animation. For every clip, every second frame (every frame of a
short clip), the posed mesh is taken and each tail's faces are tested against every other tail's, and against the
body's. The part of a tail on its first two bones is left out: there the tails are joined to each other and sunk
in the rump as modelled. What touches at rest (frame "rest") is printed first: a clip is judged against that.
Printed per clip: the most face pairs touching in any frame, tail to tail and tail to body, the frame, and which tails.
"""
import sys

import bmesh
import bpy
from mathutils.bvhtree import BVHTree


def main():
    wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.name.endswith("_Rig") and o.name.startswith("SM_Pet_KitsuneSpirit_"))
    ob = bpy.data.objects[rig.name[:-4]]
    me = ob.data
    ids = [d.value for d in me.attributes["kn_tail_id"].data]
    names = {g.index: g.name for g in ob.vertex_groups}
    far = {}                                                  # vertex -> True when it rides on a tail's third bone or beyond
    for v in me.vertices:
        if not ids[v.index]:
            continue
        best = max(v.groups, key=lambda g: g.weight if names[g.group].startswith("DEF-tail_") else -1.0, default=None)
        far[v.index] = bool(best) and names[best.group].startswith("DEF-tail_") and int(names[best.group].rsplit("_", 1)[1]) >= 3
    lids = {g.index for g in ob.vertex_groups if g.name.startswith("eyelid.")}
    tails = sorted(set(ids) - {0})
    tail_faces = {k: [] for k in tails}
    body_faces = []
    for p in me.polygons:
        k = ids[p.vertices[0]]
        if k:
            if all(far.get(v, False) for v in p.vertices):
                tail_faces[k].append(tuple(p.vertices))
        elif not any(g.group in lids for v in p.vertices for g in me.vertices[v].groups):
            body_faces.append(tuple(p.vertices))
    scene = bpy.context.scene

    def touching():
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        m = ev.to_mesh()
        co = [v.co.copy() for v in m.vertices]
        ev.to_mesh_clear()

        def tree(faces):
            used = sorted({v for f in faces for v in f})
            at = {v: i for i, v in enumerate(used)}
            return BVHTree.FromPolygons([co[v] for v in used], [tuple(at[v] for v in f) for f in faces], epsilon=0.0)

        trees = {k: tree(tail_faces[k]) for k in tails if tail_faces[k]}
        body = tree(body_faces)
        pairs, worst = 0, {}
        keys = sorted(trees)
        for i, a in enumerate(keys):
            for b in keys[i + 1:]:
                n = len(trees[a].overlap(trees[b]))
                if n:
                    pairs += n
                    worst[(a, b)] = n
        on_body = {k: len(trees[k].overlap(body)) for k in keys}
        return pairs, worst, sum(on_body.values()), {k: n for k, n in on_body.items() if n}

    rig.animation_data_create()
    rig.animation_data.action = None
    for b in rig.pose.bones:
        if not b.name.startswith(("MCH-", "ORG-", "DEF-", "VIS_")):
            b.location, b.rotation_euler, b.rotation_quaternion, b.scale = (0, 0, 0), (0, 0, 0), (1, 0, 0, 0), (1, 1, 1)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    r_pairs, r_worst, r_body, r_which = touching()
    print("CLIPCHECK %s rest: tail to tail %d %s, tail to body %d %s" % (ob.name, r_pairs, r_worst, r_body, r_which))
    total = 0
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        if action.name.startswith("WGT") or not action.use_fake_user or (wanted and action.name not in wanted):
            continue
        rig.animation_data.action = action
        if getattr(rig.animation_data, "action_slot", None) is None and getattr(action, "slots", None):
            rig.animation_data.action_slot = action.slots[0]
        first, last = int(action.frame_range[0]), int(action.frame_range[1])
        step = 1 if last - first <= 60 else 2
        top = (0, None, {})
        top_body = (0, None, {})
        bad_frames = 0
        for frame in range(first, last + 1, step):
            scene.frame_set(frame)
            pairs, worst, on_body, which = touching()
            if pairs > r_pairs or on_body > r_body:
                bad_frames += 1
            if pairs > top[0]:
                top = (pairs, frame, worst)
            if on_body > top_body[0]:
                top_body = (on_body, frame, which)
        over = max(0, top[0] - r_pairs) + max(0, top_body[0] - r_body)
        total += over
        print("CLIPCHECK %-14s %s  tail to tail: most %d on frame %s %s | tail to body: most %d on frame %s %s | frames worse than rest: %d"
              % (action.name, "ok  " if over == 0 else "CLIP", top[0], top[1], dict(sorted(top[2].items(), key=lambda kv: -kv[1])[:4]),
                 top_body[0], top_body[1], top_body[2], bad_frames))
    rig.animation_data.action = None
    print("CLIPCHECK %s total over rest: %d" % (ob.name, total))


main()
