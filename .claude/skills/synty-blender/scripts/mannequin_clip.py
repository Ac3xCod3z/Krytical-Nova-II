"""Clipping test for the Synty Mannequin Modeling scene: how far hair, facial hair and gear go into the body.

  run_blender.py --blend SyntyMannequinModeling.blend mannequin_clip.py [--tags CivilianF,VikingM] [--parts hair,beard,gear]
        [--fix] [--margin 0.4] [--out report.json]

For every mannequin and every body state (each slider at 0 and full, and the combinations in STATES) it counts the
vertices of each covering part (02HAIR, 09FCHR, gear) that sit inside the body (head, torso, wrap, upper arms,
hips) and how deep. Only clipping the state adds is counted: Synty's hair already dips into the scalp at the
hairline by design, so what is inside at the mannequin's own rest shape is the baseline.

--fix (saves the .blend): gives each covering part the body's motion for every body key it lacks (the same nearest-
surface transfer mannequin_gear.py uses), then, key by key, pushes out what still ends inside at that key's full
value by its depth + margin (cm) along the body's normal. The report is run again after the fix.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

SYNTY = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny")
OURS = ("kn_bust", "kn_butt", "kn_defined")
SKIN = ("01HEAD", "10TORS", "38WRAP", "11AUPL", "12AUPR", "17HIPS")
# States on top of each mannequin's own rest (feminine 0 or 1, buff 0.5). Values are absolute.
STATES_F = [("heavy", {"defaultHeavy": 1}), ("muscular", {"defaultBuff": 1}), ("lean", {"defaultBuff": 0}),
            ("skinny", {"defaultSkinny": 1}), ("bust", {"kn_bust": 1}), ("butt", {"kn_butt": 1}),
            ("heavy+bust+butt", {"defaultHeavy": 1, "kn_bust": 1, "kn_butt": 1}),
            ("muscular+bust", {"defaultBuff": 1, "kn_bust": 1})]
STATES_M = [("heavy", {"defaultHeavy": 1}), ("muscular", {"defaultBuff": 1}), ("lean", {"defaultBuff": 0}),
            ("skinny", {"defaultSkinny": 1}), ("defined", {"kn_defined": 1}),
            ("muscular+defined", {"defaultBuff": 1, "kn_defined": 1}),
            ("heavy+defined", {"defaultHeavy": 1, "kn_defined": 1}),
            # the creator shows Bust and Butt on any body that has the keys, male ones too (owner, 23 Oct 2026)
            ("bust", {"kn_bust": 1}), ("butt", {"kn_butt": 1}),
            ("heavy+bust+butt", {"defaultHeavy": 1, "kn_bust": 1, "kn_butt": 1})]
NEAR = 4.0      # cm: a vertex further than this inside the surface is on the far side of a thin part, not inside
REST_NEAR = 15.0  # cm: at rest, ends Synty tucks deep inside the body (long hair into the back) are hidden by design


def slot_of(name):
    return name.split("_")[4] if name.count("_") >= 5 else ""


def kind_of(ob):
    if ob.get("kn_gear"):
        return "gear"
    s = slot_of(ob.name)
    return {"02HAIR": "hair", "09FCHR": "beard"}.get(s)


def rest_values(entry):
    return {"masculineFeminine": 1.0 if entry["sex"] == "F" else 0.0, "defaultBuff": 0.5}


def positions(ob, values):
    """Mesh-local (= rig-local cm) positions with the keys mixed at absolute values; missing keys count as 0."""
    me = ob.data
    ks = me.shape_keys
    if not ks:
        return [v.co.copy() for v in me.vertices]
    basis = ks.key_blocks[0]
    co = [d.co.copy() for d in basis.data]
    for kb in ks.key_blocks[1:]:
        w = values.get(kb.name, 0.0)
        if w:
            for i, d in enumerate(kb.data):
                co[i] += (d.co - basis.data[i].co) * w
    return co


def skin_soup(skin, values):
    verts, tris, owner = [], [], []
    for ob in skin:
        base = len(verts)
        verts.extend(positions(ob, values))
        ob.data.calc_loop_triangles()
        for t in ob.data.loop_triangles:
            tris.append(tuple(base + i for i in t.vertices))
            owner.append(ob)
    return verts, tris


def inside(bvh, p, near=NEAR):
    """Depth (cm) of p under the body surface, or 0."""
    loc, n, _i, dist = bvh.find_nearest(p)
    if loc is None:
        return 0.0, None
    d = (p - loc).dot(n)
    if d < 0 and dist < near:
        return -d, n
    return 0.0, n


def measure(entry, covers, skin, states):
    rest = rest_values(entry)
    base_v, base_t = skin_soup(skin, rest)
    base_bvh = BVHTree.FromPolygons(base_v, base_t)
    baseline = {}
    for ob in covers:
        baseline[ob.name] = {i for i, p in enumerate(positions(ob, rest)) if inside(base_bvh, p, REST_NEAR)[0] > 0.05}
    rows = []
    for label, change in states:
        values = dict(rest, **change)
        sv, st = skin_soup(skin, values)
        bvh = BVHTree.FromPolygons(sv, st)
        for ob in covers:
            co = positions(ob, values)
            depths = [inside(bvh, p)[0] for p in co]
            new = [(i, d) for i, d in enumerate(depths) if d > 0.05 and i not in baseline[ob.name]]
            if new:
                rows.append({"state": label, "part": ob.name.split(".")[0], "kind": kind_of(ob),
                             "verts": len(new), "of": len(co), "max_cm": round(max(d for _i, d in new), 2)})
    return rows


def transfer(entry, ob, skin):
    """Give `ob` the body's motion for each body key it lacks or has empty (nearest point of the body at rest)."""
    rest = rest_values(entry)
    sv, st = skin_soup(skin, rest)
    bvh = BVHTree.FromPolygons(sv, st)
    me = ob.data
    if not me.shape_keys:
        ob.shape_key_add(name="Basis", from_mix=False)
    ks = me.shape_keys
    basis = ks.key_blocks[0]
    co = positions(ob, rest)
    hits = []
    for p in co:
        loc, _n, idx, _d = bvh.find_nearest(p)
        tri = st[idx]
        hits.append((tri, poly_3d_calc([sv[i] for i in tri], loc)))
    # per skin vertex delta of each key
    deltas = {}
    for k in SYNTY + OURS:
        d = []
        for s in skin:
            sk = s.data.shape_keys
            if sk and k in sk.key_blocks:
                b0 = sk.key_blocks[0].data
                d.extend(kd.co - b0[i].co for i, kd in enumerate(sk.key_blocks[k].data))
            else:
                d.extend(Vector((0, 0, 0)) for _ in s.data.vertices)
        deltas[k] = d
    filled = []
    for k in SYNTY + OURS:
        if k == "masculineFeminine":
            continue        # the sex is fixed per mannequin; hair is modelled for the head either way
        kb = ks.key_blocks.get(k)
        if kb is not None:
            moved = max(((kb.data[i].co - basis.data[i].co).length for i in range(len(co))), default=0.0)
            if moved > 0.01:
                continue    # Synty or an earlier pass already moves it: keep that
        else:
            kb = ob.shape_key_add(name=k, from_mix=False)
            kb.slider_min, kb.slider_max, kb.value = 0.0, 1.0, 0.0
        for vi, (tri, bw) in enumerate(hits):
            kb.data[vi].co = basis.data[vi].co + sum((deltas[k][c] * w for c, w in zip(tri, bw)), Vector((0, 0, 0)))
        filled.append(k)
    return filled


def push_combos(entry, ob, skin, margin, states):
    """What a combination of keys still pushes inside goes out, shared over the keys of that state."""
    rest = rest_values(entry)
    base_v, base_t = skin_soup(skin, rest)
    base_bvh = BVHTree.FromPolygons(base_v, base_t)
    baseline = {i for i, p in enumerate(positions(ob, rest)) if inside(base_bvh, p, REST_NEAR)[0] > 0.05}
    ks = ob.data.shape_keys
    pushed = {}
    for label, change in states:
        if len(change) < 2:
            continue
        keys = [k for k in change if ks.key_blocks.get(k) is not None]
        if not keys:
            continue
        values = dict(rest, **change)
        sv, st = skin_soup(skin, values)
        bvh = BVHTree.FromPolygons(sv, st)
        co = positions(ob, values)
        n = 0
        for i, pt in enumerate(co):
            if i in baseline:
                continue
            depth, nrm = inside(bvh, pt)
            if depth > 0.05:
                share = (depth + margin) / len(keys)
                for k in keys:
                    step = abs(change[k] - rest.get(k, 0.0)) or 1.0
                    ks.key_blocks[k].data[i].co += nrm * (share / step)
                n += 1
        if n:
            pushed[label] = n
    return pushed


def push_out(entry, ob, skin, margin):
    """Key by key at full value: what is still inside goes out along the body normal by depth + margin."""
    rest = rest_values(entry)
    base_v, base_t = skin_soup(skin, rest)
    base_bvh = BVHTree.FromPolygons(base_v, base_t)
    baseline = {i for i, p in enumerate(positions(ob, rest)) if inside(base_bvh, p, REST_NEAR)[0] > 0.05}
    ks = ob.data.shape_keys
    pushed = {}
    for k in (k for k in SYNTY + OURS if k != "masculineFeminine"):
        kb = ks.key_blocks.get(k)
        if kb is None:
            continue
        for value in ((0.0,) if k == "defaultBuff" else ()) + (1.0,):
            values = dict(rest, **{k: value})
            sv, st = skin_soup(skin, values)
            bvh = BVHTree.FromPolygons(sv, st)
            co = positions(ob, values)
            n_pushed = 0
            for i, p in enumerate(co):
                if i in baseline:
                    continue
                depth, n = inside(bvh, p)
                if depth > 0.05:
                    # the key is linear: moving the key's own delta by the push brings this state out
                    scale = 1.0 / max(abs(value - rest.get(k, 0.0)), 1e-6)
                    kb.data[i].co += n * ((depth + margin) * scale)
                    n_pushed += 1
            if n_pushed:
                pushed[f"{k}={value:g}"] = n_pushed
    return pushed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tags")
    ap.add_argument("--parts", default="hair,beard,gear")
    ap.add_argument("--fix", action="store_true")
    ap.add_argument("--margin", type=float, default=0.4)
    ap.add_argument("--out")
    a = ap.parse_args(kn.tool_args())
    entries = json.loads(bpy.context.scene["kn_mannequins"])
    if a.tags:
        want = {t.strip() for t in a.tags.split(",")}
        entries = [e for e in entries if e["tag"] in want]
    kinds = {k.strip() for k in a.parts.split(",")}
    report = {"before": {}, "fixed": {}, "after": {}}
    for e in entries:
        rig = bpy.data.objects[e["rig"]]
        meshes = [o for o in rig.children if o.type == "MESH"]
        skin = [o for o in meshes if slot_of(o.name) in SKIN and not o.get("kn_gear")
                and not (slot_of(o.name) == "38WRAP" and e["sex"] == "M")]
        covers = [o for o in meshes if kind_of(o) in kinds]
        if not covers:
            continue
        states = STATES_F if e["sex"] == "F" else STATES_M
        report["before"][e["tag"]] = measure(e, covers, skin, states)
        if a.fix:
            fixed = {}
            for ob in covers:
                # gear already has Synty's keys and got the new ones from mannequin_gear.py: only pushed out
                f = transfer(e, ob, skin) if kind_of(ob) != "gear" else []
                p = push_out(e, ob, skin, a.margin)
                for _round in range(2):
                    p.update({"combo " + k: v for k, v in push_combos(e, ob, skin, a.margin, states).items()})
                fixed[ob.name.split(".")[0]] = {"keys_given": f, "pushed": p}
            report["fixed"][e["tag"]] = fixed
            report["after"][e["tag"]] = measure(e, covers, skin, states)
    if a.fix:
        kn.save_blend()

    def total(section):
        return sum(r["verts"] for rows in report[section].values() for r in rows)

    summary = {"mannequins": len(entries), "clipping_vertices_before": total("before")}
    if a.fix:
        summary["clipping_vertices_after"] = total("after")
    worst = sorted((dict(r, tag=t) for t, rows in report["before"].items() for r in rows), key=lambda r: -r["verts"])[:12]
    summary["worst_before"] = [f"{r['tag']} {r['state']}: {r['part']} {r['verts']}/{r['of']} up to {r['max_cm']} cm"
                               for r in worst]
    if a.fix:
        worst = sorted((dict(r, tag=t) for t, rows in report["after"].items() for r in rows), key=lambda r: -r["verts"])[:12]
        summary["worst_after"] = [f"{r['tag']} {r['state']}: {r['part']} {r['verts']}/{r['of']} up to {r['max_cm']} cm"
                                  for r in worst]
    if a.out:
        out = Path(a.out).resolve()
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(report, indent=1), encoding="utf-8")
        summary["report"] = str(out)
    kn.report("Mannequin clipping", summary)


if __name__ == "__main__":     # importable by creator_clip_audit.py
    main()
