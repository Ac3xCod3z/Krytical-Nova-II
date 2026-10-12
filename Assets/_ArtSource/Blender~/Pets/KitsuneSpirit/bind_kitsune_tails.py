"""Kitsune Spirit, the 50 %, 75 % and 100 % forms: give each tail's skin to its own chain of bones.

  run_blender.py --blend SM_Pet_KitsuneSpirit_50.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/bind_kitsune_tails.py
  (after pet_rig.py; safe to run again)

pet_rig.py binds each vertex to the bones nearest it. Where three, six or nine tails fan out side by side that hands
parts of one tail to the bones of the next. Here every vertex of a tail (prep_kitsune_stage.py marked which tail it
belongs to, the attribute kn_tail_id) follows only its own tail's chain: the bone it lies along, shared with the
bone before or after it across each joint so the tail bends in a curve and not in a kink.
On the 100 % form it also puts each flame tuft whole on one bone.
"""
import json
import re

import bpy
from mathutils import Vector

BLEND = 0.5        # how far either side of a joint the two bones share the skin, as a share of a bone's length


def main():
    stage = re.search(r"_(\d+)\.blend$", bpy.data.filepath).group(1)
    ob = bpy.data.objects["SM_Pet_KitsuneSpirit_" + stage]
    rig = bpy.data.objects["SM_Pet_KitsuneSpirit_%s_Rig" % stage]
    ids = [d.value for d in ob.data.attributes["kn_tail_id"].data]
    to_rig = rig.matrix_world.inverted() @ ob.matrix_world
    chains = {}
    for k in sorted(set(ids) - {0}):
        names = []
        j = 1
        while ("DEF-tail_%d_%d" % (k, j)) in rig.data.bones:
            names.append("DEF-tail_%d_%d" % (k, j))
            j += 1
        chains[k] = [(n, rig.data.bones[n].head_local.copy(), rig.data.bones[n].tail_local.copy()) for n in names]
    groups = {g.name: g for g in ob.vertex_groups}
    for names in chains.values():
        for n, _h, _t in names:
            if n not in groups:
                groups[n] = ob.vertex_groups.new(name=n)
    deform = [g for g in ob.vertex_groups if g.name.startswith("DEF-")]
    count = 0
    for v in ob.data.vertices:
        k = ids[v.index]
        if not k:
            continue
        p = to_rig @ v.co
        chain = chains[k]
        best = None
        for i, (_n, h, t) in enumerate(chain):
            ab = t - h
            u = (p - h).dot(ab) / ab.length_squared
            if i == 0:
                u = min(u, 1.0)                               # before the first bone still counts as on it
            elif i == len(chain) - 1:
                u = max(u, 0.0)                               # and past the last
            else:
                u = max(0.0, min(1.0, u))
            d = (p - (h + ab * max(0.0, min(1.0, u)))).length
            if best is None or d < best[0]:
                best = (d, i, u)
        _d, i, u = best
        w = {i: 1.0}
        if u < BLEND and i > 0:
            share = 0.5 * (1.0 - u / BLEND)
            w = {i: 1.0 - share, i - 1: share}
        elif u > 1.0 - BLEND and i < len(chain) - 1:
            share = 0.5 * (1.0 - (1.0 - u) / BLEND)
            w = {i: 1.0 - share, i + 1: share}
        for g in deform:
            g.remove([v.index])
        for b, x in w.items():
            groups[chain[b][0]].add([v.index], x, "REPLACE")
        count += 1
    # the 100 % form's flame tufts: each rides whole on one bone, the one a fire effect is hung on in Unity
    # (left to the nearest bones, the hock tuft followed the hips and the shoulder tuft the shoulder blades)
    fire = json.loads(ob.get("kn_fire", "{}"))
    vg = ob.vertex_groups.get("kn_fire")
    choices = {"shoulder": ["spine.006", "spine.007", "spine.008"],
               "foreleg": ["front_thigh.%s", "front_thigh.%s.001", "front_shin.%s", "front_shin.%s.001"],
               "hindleg": ["thigh.%s", "thigh.%s.001", "shin.%s", "shin.%s.001", "foot.%s"]}
    for tag, (y0, y1, z0, z1) in fire.items():
        for side, sign in (("L", 1.0), ("R", -1.0)):
            vs = [v for v in ob.data.vertices if any(g.group == vg.index for g in v.groups)
                  and y0 <= v.co.y <= y1 and z0 <= v.co.z <= z1 and v.co.x * sign > 0.0]
            if not vs:
                continue
            c = to_rig @ (sum((v.co for v in vs), Vector()) / len(vs))

            def far(name):
                b = rig.data.bones["DEF-" + name]
                ab = b.tail_local - b.head_local
                u = max(0.0, min(1.0, (c - b.head_local).dot(ab) / ab.length_squared))
                return (c - (b.head_local + ab * u)).length
            bone = min((n % side if "%s" in n else n for n in choices[tag]), key=far)
            for v in vs:
                for g in deform:
                    g.remove([v.index])
                groups["DEF-" + bone].add([v.index], 1.0, "REPLACE")
            local = rig.data.bones["DEF-" + bone].matrix_local.inverted() @ c
            print("FIRE %s.%s: %d vertices on %s; middle (%.3f, %.3f, %.3f), in the bone's own space (%.3f, %.3f, %.3f)"
                  % (tag, side, len(vs), bone, *c, *local))
    print("TAILS %s: %d vertices on %d tails of %d bones" % (stage, count, len(chains), len(next(iter(chains.values())))))


main()
