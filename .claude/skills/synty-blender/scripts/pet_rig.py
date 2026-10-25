"""Rig a four-legged pet with Rigify: fit the quadruped metarig to the model, generate the rig, bind the mesh.

  run_blender.py --blend FILE.blend pet_rig.py --object SM_Pet_X_S1 --landmarks rig_x.json

The landmarks file says where each metarig bone goes on this pet, in metres:
  {
    "bones":  {"spine.004": [[x,y,z],[x,y,z]], "front_thigh.L": [...], ...},   head and tail of each bone
    "extra":  {"ear.L": {"parent": "spine.011", "head": [..], "tail": [..]}},  more bones, each with a control
    "eyelids": true,                           add eyelid.L / eyelid.R from what pet_eyes.py stored
    "rigid":  {"eye.L": "spine.011", "kn_details": {"spine.009": "z<0.19", ...}}   vertex group -> bone;
    "give_to": {"spine.003": "spine.004"}      a bone's whole share of the skin goes to another bone
    "plain": ["spine.004"]                     deforming bones that are made plain bones, not bendy ones
    "hold_below": {"thigh": {"fade": 0.04, "to": "spine.004"}}   bones named so hold no skin above their own top
    "reach": {"shin": {"r": 0.04, "to": "spine.004"}}            bones named so hold only the skin this near them
              in place of a bone, "@skin" makes each vertex follow the body's skin under it (a collar),
              and "@skin_as_one" makes the set follow the skin under its highest point (a hanging tag)
  }
Only the left (.L) side is given; the right is mirrored. A bone that continues from its parent takes its
head from the parent's tail. Bone names are Rigify's basic quadruped metarig's (inspect a metarig to see
them): spine.004-.008 run from the hips to the chest, spine.009-.011 are neck, neck, head, and
spine.003, .002, .001, spine run down the tail.

Binding: each vertex follows the bones nearest to it (Blender's automatic weights fail on these meshes),
smoothed along the surface; "pieces" keeps a separate piece of mesh on the bones it names
({"near": [x,y,z], "bones": ["spine.003", ...]}, e.g. the tail; add "only": true and no other piece follows those bones); the groups under "rigid" are then pinned. Safe to run again: the old
rig is removed first. The mesh must already be the right size (pet_finish.py --size).
"""
import argparse
import json
import sys
from pathlib import Path

import addon_utils
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def mirror_name(name):
    return name[:-2] + ".R" if name.endswith(".L") else None


def flip(p):
    return [-p[0], p[1], p[2]]


def activate(ob, mode="OBJECT"):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.select_set(o is ob)
    bpy.context.view_layer.objects.active = ob
    if mode != "OBJECT":
        bpy.ops.object.mode_set(mode=mode)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object", required=True)
    ap.add_argument("--landmarks", required=True)
    a = ap.parse_args(kn.tool_args())

    ob = kn.get_object(a.object)
    path = Path(a.landmarks)
    if not path.is_absolute():
        path = kn.project_root() / path
    with open(path, encoding="utf-8") as fh:
        spec = json.load(fh)
    addon_utils.enable("rigify", default_set=True)
    problems, warnings = [], []
    if any(abs(s - 1) > 1e-4 for s in ob.scale) or ob.rotation_euler.to_quaternion().angle > 1e-4:
        raise SystemExit("Apply the object's rotation and scale first (pet_finish.py does).")

    # ---- clear an earlier rig
    meta_name, rig_name = ob.name + "_Metarig", ob.name + "_Rig"
    for mod in [m for m in ob.modifiers if m.type == "ARMATURE"]:
        ob.modifiers.remove(mod)
    ob.parent = None
    for g in [g for g in ob.vertex_groups if g.name.startswith(("DEF-", "ORG-", "MCH-"))]:
        ob.vertex_groups.remove(g)
    for o in [o for o in bpy.data.objects if o.name in (meta_name, rig_name) or o.name.startswith("WGT-" + rig_name)]:
        data = o.data
        bpy.data.objects.remove(o)
        if data and data.users == 0:
            (bpy.data.armatures if isinstance(data, bpy.types.Armature) else bpy.data.meshes).remove(data)

    # ---- metarig, fitted
    activate(ob)
    bpy.ops.object.armature_basic_quadruped_metarig_add()
    meta = bpy.context.object
    meta.name = meta.data.name = meta_name
    meta.location = ob.matrix_world.translation
    origin = ob.matrix_world.translation
    places = dict(spec["bones"])
    for name, value in list(places.items()):
        other = mirror_name(name)
        if other and other not in places:
            places[other] = [flip(value[0]), flip(value[1])] if value[0] is not None else [None, flip(value[1])]
    activate(meta, "EDIT")
    eb = meta.data.edit_bones
    missing = [b.name for b in eb if b.name not in places]
    unknown = [n for n in places if n not in eb]
    if missing or unknown:
        raise SystemExit(f"Landmarks do not match the metarig. Not placed: {missing}. Not in the metarig: {unknown}.")

    def order(bone):
        depth = 0
        while bone.parent:
            bone, depth = bone.parent, depth + 1
        return depth

    for bone in sorted(eb, key=order):
        head, tail = places[bone.name]
        if bone.use_connect and bone.parent is not None:
            bone.head = bone.parent.tail
        elif head is not None:
            bone.head = Vector(head) - origin
        bone.tail = Vector(tail) - origin
        if bone.length < 1e-4:
            problems.append(f"{bone.name} has no length; check its landmarks.")

    extra = dict(spec.get("extra", {}))
    if spec.get("eyelids"):
        for side in ("L", "R"):
            stored = ob.get("kn_eyelid_" + side)
            if stored is None:
                problems.append("No eyelid data on the object; run pet_eyes.py first.")
                continue
            # The bone lies along the lid's pull, so sliding its control one bone-length along itself shuts the eye.
            extra["eyelid." + side] = {"parent": spec.get("head_bone", "spine.011"),      # stored in the mesh's own space
                                       "head": list(Vector(stored[0:3]) + origin), "tail": list(Vector(stored[3:6]) + origin)}
    for name, info in list(extra.items()):
        other = mirror_name(name)
        if other and other not in extra:
            extra[other] = {"parent": mirror_name(info["parent"]) or info["parent"],
                            "head": flip(info["head"]), "tail": flip(info["tail"])}
    for name, info in extra.items():
        bone = eb.new(name)
        bone.head, bone.tail = Vector(info["head"]) - origin, Vector(info["tail"]) - origin
        bone.parent = eb[info["parent"]]
        bone.use_connect = False
    bpy.ops.object.mode_set(mode="POSE")
    home = meta.data.collections_all.get("Spine") or next(iter(meta.data.collections_all), None)
    for name in extra:
        meta.pose.bones[name].rigify_type = "basic.super_copy"
        if home is not None:
            home.assign(meta.data.bones[name])
    bpy.ops.object.mode_set(mode="OBJECT")
    if problems:
        kn.report("Pet rig", {"stage": "metarig"}, problems)
        sys.exit(1)

    # ---- generate
    before = set(bpy.data.objects)
    activate(meta)
    bpy.ops.pose.rigify_generate()
    rig = next((o for o in bpy.data.objects if o not in before and o.type == "ARMATURE"), None) or meta.data.rigify_target_rig
    if rig is None:
        raise SystemExit("Rigify did not make a rig.")
    rig.name = rig.data.name = rig_name
    # Rigify puts the new rig at the world origin, but the bones were laid out from the metarig's place (the
    # model's origin): a model that is not at the origin would be bound to bones standing somewhere else
    rig.matrix_world = meta.matrix_world.copy()
    bpy.context.view_layer.update()
    meta.hide_set(True)
    meta.hide_render = True
    deform = {b.name for b in rig.data.bones if b.use_deform}
    # "plain": deforming bones that must not be bendy bones. Rigify makes the spine's bones bendy, and steers the
    # hips' bend by the first tail bone. Where the metarig's tail is only a stub inside the rump (a pet whose tails
    # have chains of their own) that bends the hips out of shape even at rest: by 57 mm on the Kitsune's 25 % form.
    for name in spec.get("plain", []):
        name = name if name.startswith("DEF-") else "DEF-" + name
        if name in rig.data.bones:
            rig.data.bones[name].bbone_segments = 1

    # ---- bind. Blender's automatic weights give up on these meshes (many separate pieces, faces passing
    #      through each other), so weights are worked out here: each vertex follows the bones it is nearest
    #      to, the result is smoothed along the surface, then the rigid pieces are pinned.
    activate(ob)
    ob.parent = rig
    ob.matrix_parent_inverse = rig.matrix_world.inverted()
    ob.modifiers.new("Armature", "ARMATURE").object = rig
    me = ob.data
    groups = {g.name: g for g in ob.vertex_groups}
    skip = tuple(spec.get("not_for_skin", ["eyelid", "breast", "pelvis"]))
    to_rig = rig.matrix_world.inverted() @ ob.matrix_world
    segments = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones
                if b.use_deform and not any(k in b.name for k in skip)}

    def distance(p, seg):
        a0, b0 = seg
        ab = b0 - a0
        t = max(0.0, min(1.0, (p - a0).dot(ab) / max(ab.length_squared, 1e-12)))
        return (p - (a0 + ab * t)).length

    # separate pieces of mesh that must only follow certain bones (a tail must not follow a hind leg)
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    piece_of, pieces = {}, []
    for v in bm.verts:
        if v.index in piece_of:
            continue
        stack, group = [v], []
        piece_of[v.index] = len(pieces)
        while stack:
            cur = stack.pop()
            group.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if o2.index not in piece_of:
                    piece_of[o2.index] = len(pieces)
                    stack.append(o2)
        pieces.append(group)
    neighbours = {v.index: [e.other_vert(v).index for e in v.link_edges] for v in bm.verts}
    bm.free()
    allowed, reserved = {}, set()
    for rule in spec.get("pieces", []):
        near = Vector(rule["near"])
        best = min(range(len(pieces)), key=lambda i: (sum((ob.matrix_world @ me.vertices[j].co for j in pieces[i]), Vector())
                                                      / len(pieces[i]) - near).length)
        names = ["DEF-" + n if not n.startswith("DEF-") else n for n in rule["bones"]]
        allowed[best] = {n: segments[n] for n in names if n in segments}
        if rule.get("only"):          # ... and no other piece may follow these bones (the rump must not follow the tails)
            reserved.update(names)
    everything_else = {n: sg for n, sg in segments.items() if n not in reserved}

    raw = {}
    side_margin = float(spec.get("side_margin", 0.006))
    for v in me.vertices:
        p = to_rig @ v.co
        pool = allowed.get(piece_of[v.index], everything_else)
        if p.x > side_margin:
            pool = {n: sg for n, sg in pool.items() if not (n.endswith(".R") or ".R." in n)}
        elif p.x < -side_margin:
            pool = {n: sg for n, sg in pool.items() if not (n.endswith(".L") or ".L." in n)}
        near = sorted(((distance(p, seg), name) for name, seg in pool.items()))[:3]
        closest = max(near[0][0], 1e-5)
        w = {name: (closest / max(d, 1e-5)) ** 4 for d, name in near}
        total = sum(w.values())
        raw[v.index] = {name: x / total for name, x in w.items()}
    for _ in range(2):      # soften the joins
        smooth = {}
        for index, w in raw.items():
            acc = {k: x * 2.0 for k, x in w.items()}
            px = (to_rig @ me.vertices[index].co).x
            for n in neighbours[index]:
                for k, x in raw[n].items():
                    wrong_side = (px > side_margin and (k.endswith(".R") or ".R." in k)) or \
                                 (px < -side_margin and (k.endswith(".L") or ".L." in k))
                    if not wrong_side:
                        acc[k] = acc.get(k, 0.0) + x
            top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
            total = sum(x for _k, x in top)
            smooth[index] = {k: x / total for k, x in top}
        raw = smooth
    for index, w in raw.items():
        for name, x in w.items():
            if x > 0.01:
                if name not in groups:
                    groups[name] = ob.vertex_groups.new(name=name)
                groups[name].add([index], x, "REPLACE")

    # an ear or a horn keeps only what lies along its own length, or it drags the cheek under it about
    for name, extra in spec.get("extra", {}).items():
        if "parent" in extra:
            for n in [name] + ([mirror_name(name)] if mirror_name(name) else []):
                kn.keep_to_own_length(ob, rig, n, extra["parent"], float(spec.get("extra_start", -0.05)),
                                      float(spec.get("extra_full", 0.2)))
    groups = {g.name: g for g in ob.vertex_groups}

    groups_by_index = {g.index: n for n, g in groups.items()}
    # "reach": a bone holds only the skin within this distance of itself. A belly or a flank is far from every bone,
    # so nearest-bone weights hand it to whichever leg bones happen to be nearest, and then every step drags dents
    # across the body. Past the reach a bone's hold fades out (gone at 1.4 times the reach); what it gives up goes
    # to the vertex's other bones, or to "to".
    for key, rule in spec.get("reach", {}).items():
        reach = float(rule["r"])
        to = rule["to"] if rule["to"].startswith("DEF-") else "DEF-" + rule["to"]
        if to not in groups:
            groups[to] = ob.vertex_groups.new(name=to)
        mine = {g.index: segments[n] for n, g in groups.items() if n.startswith("DEF-" + key) and n in segments}
        for v in me.vertices:
            p = to_rig @ v.co
            given, others = 0.0, []
            for e in v.groups:
                if e.group in mine:
                    far = (distance(p, mine[e.group]) - reach) / (0.4 * reach)
                    if far > 0.0:
                        keep = e.weight * max(0.0, 1.0 - far)
                        given += e.weight - keep
                        e.weight = keep
                elif e.weight > 0.0 and groups_by_index.get(e.group, "").startswith("DEF-spine"):
                    others.append(e)
            if given <= 0.0:
                continue
            total = sum(e.weight for e in others)
            if total > 1e-6:
                for e in others:
                    e.weight += given * e.weight / total
            else:
                groups[to].add([v.index], given, "ADD")

    # "hold_below": a leg's upper bone holds the skin only up to its own top. Nearest-bone weights also give it the
    # flank and the back above the joint, and then every step drags creases across the back. Above the bone's head
    # its hold fades out over "fade" metres; what it gives up goes to the vertex's other bones, or to "to".
    for key, rule in spec.get("hold_below", {}).items():
        fade = float(rule.get("fade", 0.04))
        to = rule["to"] if rule["to"].startswith("DEF-") else "DEF-" + rule["to"]
        if to not in groups:
            groups[to] = ob.vertex_groups.new(name=to)
        mine = {g.index: rig.data.bones[n].head_local.z for n, g in groups.items()
                if n.startswith("DEF-" + key) and n in rig.data.bones}
        tops = {}
        for n, g in groups.items():      # every segment of one leg fades from the top of that leg's first bone
            if g.index in mine:
                side = "R" if (n.endswith(".R") or ".R." in n) else "L"
                tops[side] = max(tops.get(side, -1e9), mine[g.index])
        side_of = {g.index: ("R" if (n.endswith(".R") or ".R." in n) else "L") for n, g in groups.items() if g.index in mine}
        for v in me.vertices:
            z = (to_rig @ v.co).z
            given, others = 0.0, []
            for e in v.groups:
                if e.group in mine:
                    over = (z - tops[side_of[e.group]]) / max(fade, 1e-6)
                    if over > 0.0:
                        keep = e.weight * max(0.0, 1.0 - over)
                        given += e.weight - keep
                        e.weight = keep
                elif e.weight > 0.0:
                    others.append(e)
            if given <= 0.0:
                continue
            total = sum(e.weight for e in others)
            if total > 1e-6:
                for e in others:
                    e.weight += given * e.weight / total
            else:
                groups[to].add([v.index], given, "ADD")

    # "give_to": a bone hands its whole share of the skin to another (a metarig bone the pet has no use for, such
    # as the single tail on a pet whose tails have chains of their own, must not carry any of the body)
    for name, to in spec.get("give_to", {}).items():
        name = name if name.startswith("DEF-") else "DEF-" + name
        to = to if to.startswith("DEF-") else "DEF-" + to
        if name not in groups:
            continue
        if to not in groups:
            groups[to] = ob.vertex_groups.new(name=to)
        gi, ti = groups[name].index, groups[to].index
        for v in me.vertices:
            mine = next((e.weight for e in v.groups if e.group == gi), 0.0)
            if mine > 0.0:
                had = next((e.weight for e in v.groups if e.group == ti), 0.0)
                groups[to].add([v.index], had + mine, "REPLACE")
        ob.vertex_groups.remove(groups[name])
        groups = {g.name: g for g in ob.vertex_groups}

    def members(group_name):
        g = groups.get(group_name)
        if g is None:
            return {}
        return {v.index: e.weight for v in me.vertices for e in v.groups if e.group == g.index}

    def bone_group(bone):
        name = bone if bone.startswith("DEF-") else "DEF-" + bone
        if name not in deform:
            raise SystemExit(f"The rig has no deforming bone '{name}'. Deforming bones: {sorted(deform)[:40]}")
        if name not in groups:
            groups[name] = ob.vertex_groups.new(name=name)
        return groups[name]

    def clear_bones(indices):
        for g in ob.vertex_groups:
            if g.name in deform:
                g.remove(indices)

    def test(expr, co):
        return eval(expr, {"__builtins__": {}}, {"x": co.x, "y": co.y, "z": co.z, "abs": abs})

    pinned = {}
    skin_each, skin_as_one = [], {}
    world = [ob.matrix_world @ v.co for v in me.vertices]
    for group_name, target in spec.get("rigid", {}).items():
        inside = members(group_name)
        if not inside:
            warnings.append(f"Vertex group '{group_name}' is empty or missing; nothing pinned for it.")
            continue
        rules = {target: "True"} if isinstance(target, str) else target
        for index in inside:
            for bone, expr in rules.items():
                if test(expr, world[index]):
                    if bone == "@skin":
                        skin_each.append(index)
                    elif bone.startswith("@skin_as_one"):
                        skin_as_one.setdefault(group_name + bone, []).append(index)
                    else:
                        pinned[index] = bone
                    break
    by_bone = {}
    for index, bone in pinned.items():
        by_bone.setdefault(bone, []).append(index)
    for bone, indices in by_bone.items():
        clear_bones(indices)
        bone_group(bone).add(indices, 1.0, "REPLACE")

    # "@skin": each vertex takes the weights of the nearest vertex of the body's skin, so a collar bends
    # with the neck. "@skin_as_one": the whole set takes the weights under its highest point, so a tag
    # hangs from where it is tied and keeps its shape.
    special = set(skin_each) | {i for group in skin_as_one.values() for i in group}
    if special:
        special_all = special | set(pinned)
        for group_name in spec.get("rigid", {}):
            special_all |= set(members(group_name))
        body = [v for v in me.vertices if v.index not in special_all]
        tree = KDTree(len(body))
        for i, v in enumerate(body):
            tree.insert(v.co, i)
        tree.balance()

        def weights_at(co):
            _c, i, _d = tree.find(co)
            return {ob.vertex_groups[e.group].name: e.weight for e in body[i].groups
                    if ob.vertex_groups[e.group].name in deform and e.weight > 1e-5}

        def give(indices, weights):
            clear_bones(indices)
            for name, x in weights.items():
                groups[name].add(indices, x, "REPLACE")

        for index in skin_each:
            give([index], weights_at(me.vertices[index].co))
        for indices in skin_as_one.values():
            top = max(indices, key=lambda i: world[i].z)
            give(indices, weights_at(me.vertices[top].co))

    head = spec.get("head_bone", "spine.011")
    lids = {}
    if spec.get("eyelids"):
        for side in ("L", "R"):
            for index, weight in members("eyelid." + side).items():
                clear_bones([index])
                if weight > 1e-4:
                    bone_group("eyelid." + side).add([index], weight, "REPLACE")
                if weight < 1 - 1e-4:
                    bone_group(head).add([index], 1.0 - weight, "REPLACE")
                lids[index] = weight

    def weighted(v):
        return any(e.weight > 1e-5 and ob.vertex_groups[e.group].name in deform for e in v.groups)

    lost = [v.index for v in me.vertices if not weighted(v)]
    if lost:
        have = [v for v in me.vertices if weighted(v)]
        tree = KDTree(len(have))
        for i, v in enumerate(have):
            tree.insert(v.co, i)
        tree.balance()
        for index in lost:
            _co, i, _d = tree.find(me.vertices[index].co)
            for e in have[i].groups:
                g = ob.vertex_groups[e.group]
                if g.name in deform and e.weight > 1e-5:
                    g.add([index], e.weight, "REPLACE")
    still = sum(1 for v in me.vertices if not weighted(v))
    if still:
        problems.append(f"{still} vertices have no bone weight.")

    # The neutral rig must hold the mesh as modelled. Rigify's limbs solve their own bend; if a leg was laid
    # out too straight they can settle on the wrong one and fold a paw at rest.
    bpy.context.view_layer.update()
    folded = []
    for bone in rig.data.bones:
        if bone.use_deform:
            pose, rest_m = rig.pose.bones[bone.name].matrix, bone.matrix_local
            angle = pose.to_quaternion().rotation_difference(rest_m.to_quaternion()).angle
            angle = min(angle, 6.283185307 - angle)
            if (pose.translation - rest_m.translation).length > 0.0005 or angle > 0.01:
                folded.append((bone.name, round(angle * 57.2958, 1)))
    if folded:
        problems.append(f"The rig bends the model at rest ({len(folded)} bones, e.g. {folded[:4]}). Give that limb a "
                        "clearer zigzag in the landmarks: upper bone leaning back, middle upright, foot forward.")

    used = {ob.vertex_groups[e.group].name for v in me.vertices for e in v.groups
            if e.weight > 1e-5 and ob.vertex_groups[e.group].name in deform}
    controls = sorted(b.name for b in rig.pose.bones if not b.name.startswith(("ORG-", "MCH-", "DEF-", "VIS")))
    kn.save_blend()
    kn.report("Pet rig", {
        "object": ob.name, "rig": rig.name, "metarig_kept_hidden": meta.name,
        "bones": {"all": len(rig.data.bones), "deforming": len(deform), "deforming_used_by_the_mesh": len(used)},
        "pinned_vertices": {b: len(i) for b, i in by_bone.items()}, "eyelid_vertices": len(lids),
        "following_the_skin": len(skin_each), "hanging_as_one": {k: len(v) for k, v in skin_as_one.items()},
        "vertices_mended_from_neighbours": len(lost), "controls": controls,
        "next": "Pose a control and look (render_sheet.py); pet_finish.py to check weights; animate; export on go-ahead."},
        problems, warnings)
    if problems:
        sys.exit(1)


main()
