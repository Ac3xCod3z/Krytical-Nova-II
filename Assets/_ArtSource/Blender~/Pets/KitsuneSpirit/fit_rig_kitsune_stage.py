"""Kitsune Spirit, the 50 %, 75 % and 100 % forms: write the rig's landmarks file, rig_kitsune_NN.json.

  run_blender.py --blend SM_Pet_KitsuneSpirit_50.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/fit_rig_kitsune_stage.py
  (after prep_kitsune_stage.py; then pet_rig.py --landmarks rig_kitsune_50.json, then bind_kitsune_tails.py)

The body's bones are the 25 % form's (rig_kitsune_25.json), moved to this model's own joints: the model is measured
(paws, nose, rump, back, ear tips) and every landmark of the 25 % form is carried over between the same
measures of that form. OVERRIDES holds what was then set by eye for a stage.
The tails are not carried over: each tail gets a chain of five bones of its own, laid along the middle of the tail
as prep_kitsune_stage.py found it (stored on the object as kn_tails). They are named tail_K_1 .. tail_K_5, K counted
from the fox's left to its right; none is mirrored, so no name ends in .L or .R.
Pieces that are not fur (collar, talisman, headband, paper tags, a loose eyeball) are made rigid with the nearest
of the neck and head bones.
"""
import json
import re
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
# the 25 % form measured with measure() below (SM_Pet_KitsuneSpirit_25.blend), metres
M25 = {"length": 0.3954, "front": [0.0326, -0.1601], "hind": [0.0305, 0.0363], "back": 0.2345, "nose": [-0.33, 0.2144],
       "rump": 0.0437, "ear": [0.0775, -0.232, 0.3762]}
OVERRIDES = {"50": {}, "75": {}, "100": {}}


def islands_of(bm):
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, group = [v], []
        seen.add(v.index)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        out.append(group)
    return sorted(out, key=lambda g: -len(g))


def measure(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    isl = islands_of(bm)
    body = [v.co.copy() for v in isl[0]]
    front_half = [v.co.copy() for g in isl for v in g if max(x.co.y for x in g) < 0.0 or g is isl[0]]
    length = max(p.y for p in body) - min(p.y for p in body)
    low = [p for p in body if p.z < 0.05 * length and p.x > 0]
    mid_y = (min(p.y for p in low) + max(p.y for p in low)) / 2
    front = [p for p in low if p.y < mid_y]
    hind = [p for p in low if p.y >= mid_y]
    fy, fx = sum(p.y for p in front) / len(front), sum(p.x for p in front) / len(front)
    hy, hx = sum(p.y for p in hind) / len(hind), sum(p.x for p in hind) / len(hind)
    cy = hy + 0.2 * (fy - hy)                                # the loins: clear of any mane or shoulder tuft
    trunk = [p for p in body if abs(p.y - cy) < 0.05 * length and abs(p.x) < 0.1 * length]
    back, belly = max(p.z for p in trunk), min(p.z for p in trunk)
    nose = min(body, key=lambda p: p.y)
    rump = max(p.y for p in body if p.z > 0.6 * back)
    ear = max((p for p in front_half if p.y < fy + 0.1 * length and p.x > 0), key=lambda p: p.z)
    out = {"length": length, "front": [fx, fy], "hind": [hx, hy], "back": back, "belly": belly, "nose": [nose.y, nose.z],
           "rump": rump, "ear": list(ear)}
    bm.free()
    return {k: ([round(c, 4) for c in v] if isinstance(v, list) else round(v, 4)) for k, v in out.items()}


def through(x, xs, ys):
    """Straight lines through the points (xs, ys), carried on past both ends."""
    i = 0
    while i < len(xs) - 2 and x > xs[i + 1]:
        i += 1
    t = (x - xs[i]) / (xs[i + 1] - xs[i])
    return ys[i] + (ys[i + 1] - ys[i]) * t


def main():
    stage = re.search(r"_(\d+)\.blend$", bpy.data.filepath).group(1)
    ob = bpy.data.objects["SM_Pet_KitsuneSpirit_" + stage]
    m = measure(ob)
    print("MEASURES %s: %s" % (stage, json.dumps(m)))
    if stage == "25":
        return
    a = M25
    old = json.loads((HERE / "rig_kitsune_25.json").read_text(encoding="utf-8"))
    # behind the hind paws the spacing between the paws is carried on (the rump is no measure: a fuller haunch
    # threw the hind feet's bones out behind the legs)
    ys = ([a["nose"][0], a["front"][1], a["hind"][1]], [m["nose"][0], m["front"][1], m["hind"][1]])
    zs = ([0.0, a["back"], a["ear"][2]], [0.0, m["back"], m["ear"][2]])

    def carry(p):
        x, y, z = p
        wide = through(y, [a["front"][1], a["hind"][1]], [m["front"][0] / a["front"][0], m["hind"][0] / a["hind"][0]])
        wide = min(max(wide, min(m["front"][0] / a["front"][0], m["hind"][0] / a["hind"][0])),
                   max(m["front"][0] / a["front"][0], m["hind"][0] / a["hind"][0]))
        if z > a["back"] * 1.25:                             # up in the head: as wide as the ears stand
            wide = m["ear"][0] / a["ear"][0]
        return [round(x * wide, 4), round(through(y, *ys), 4), round(through(z, *zs), 4)]

    bones = {}
    for name, (head, tail) in old["bones"].items():
        bones[name] = [carry(head) if head else None, carry(tail)]
    extra = {"ear.L": {"parent": "spine.011", "head": carry(old["extra"]["ear.L"]["head"]), "tail": [round(c, 4) for c in m["ear"]]}}
    for name, value in OVERRIDES[stage].items():
        if name in bones:
            bones[name] = value
        else:
            extra[name].update(value)
    chains = json.loads(ob["kn_tails"])
    tail_bones = []
    for k, chain in enumerate(chains, 1):
        for j in range(len(chain) - 1):
            name = "tail_%d_%d" % (k, j + 1)
            extra[name] = {"parent": "tail_%d_%d" % (k, j) if j else "spine.004", "head": chain[j], "tail": chain[j + 1]}
            tail_bones.append(name)
    size = m["back"] / a["back"]

    # what is not fur goes rigid with the nearest neck or head bone
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    isl = islands_of(bm)
    tail_id = ob.data.attributes["kn_tail_id"].data
    holders = {n: (Vector(bones[n][0] or bones["spine.%03d" % (int(n[-3:]) - 1)][1]), Vector(bones[n][1])) for n in ("spine.009", "spine.010", "spine.011")}
    rigid = {}
    lid_groups = {g.index for g in ob.vertex_groups if g.name.startswith("eyelid.")}
    lids = {v.index for v in ob.data.vertices if any(e.group in lid_groups for e in v.groups)}
    for g in ob.vertex_groups:
        if g.name.startswith("kn_rigid_"):
            ob.vertex_groups.remove(g)
    for g in isl[1:]:
        if tail_id[g[0].index].value or g[0].index in lids:
            continue
        top = max(v.co.z for v in g)
        if top > 0.9 * m["ear"][2] and len(g) > 40 and max(v.co.z for v in g) - min(v.co.z for v in g) > 0.25 * (m["ear"][2] - m["back"]):
            continue                                         # an ear that is a piece of its own: it follows the ear bone
        c = sum((v.co for v in g), Vector()) / len(g)

        def far(seg):
            p, q = seg
            t = max(0.0, min(1.0, (c - p).dot(q - p) / (q - p).length_squared))
            return (c - (p + (q - p) * t)).length
        bone = min(holders, key=lambda n: far(holders[n]))
        vg = ob.vertex_groups.get("kn_rigid_" + bone) or ob.vertex_groups.new(name="kn_rigid_" + bone)
        vg.add([v.index for v in g], 1.0, "REPLACE")
        rigid["kn_rigid_" + bone] = bone
    bm.free()

    scaled = lambda rule, key: {**rule, key: round(rule[key] * size, 4)}
    spec = {
        "_about": "Where Rigify's basic quadruped metarig sits on the Kitsune Spirit's %s %% form. Metres; the fox faces -Y and its left is +X. "
                  "WRITTEN BY fit_rig_kitsune_stage.py: change that script's OVERRIDES, not this file. Read by pet_rig.py." % stage,
        "_tails": "%d tails, each a chain of five bones with a control on every bone: tail_K_1 (at the rump) .. tail_K_5 (the tip), "
                  "K from the fox's left to its right. Rigify's own single tail (spine.003 .. spine) is a stub in the rump; nothing is animated on it. "
                  "bind_kitsune_tails.py gives each tail's skin to its own chain after pet_rig.py." % len(chains),
        "head_bone": "spine.011",
        "bones": bones,
        "extra": extra,
        "reach": {k: scaled(v, "r") for k, v in old["reach"].items()},
        "hold_below": {k: scaled(v, "fade") for k, v in old["hold_below"].items()},
        "plain": old["plain"],
        "give_to": old["give_to"],
        "eyelids": True,
        "rigid": rigid,
        "pieces": [{"near": chains[0][2], "only": True, "bones": tail_bones}],
    }
    (HERE / ("rig_kitsune_%s.json" % stage)).write_text(json.dumps(spec, indent=1), encoding="utf-8")
    print("FIT %s: %d bones, %d tail bones, rigid %s, size x%.3f of the 25 %% form" % (stage, len(bones), len(tail_bones), rigid, size))


main()
