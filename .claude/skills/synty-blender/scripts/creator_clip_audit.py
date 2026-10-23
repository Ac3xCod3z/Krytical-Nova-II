"""Clipping audit over everything the game's character creator offers, not only the mannequins' own picks.

  run_blender.py --blend SyntyMannequinModeling.blend creator_clip_audit.py --parts creator_parts.json [--out audit.json]

creator_parts.json: {"Civilian_masculine": {"1": [heads], "2": [hairs], "9": [facial hairs]}, ...}, read from the
Lineage assets. For each lineage and sex the bare body is the mannequin's (with the kn_* keys); every head on offer
replaces the mannequin's head in turn, and every hair and facial hair on offer is measured three ways: as Synty ships
it, with only the follow-the-body transfer (what the game does at merge time), and with transfer + push-out.
Nothing is saved.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import mannequin_clip as mc

importlib.reload(mc)
TAGS = {"Civilian": "Civilian", "Soldier": "Soldier", "Viking": "Viking", "Elf": "Elf"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts", required=True)
    ap.add_argument("--out")
    a = ap.parse_args(kn.tool_args())
    offers = json.loads(Path(a.parts).read_text(encoding="utf-8"))
    entries = {e["tag"]: e for e in json.loads(bpy.context.scene["kn_mannequins"])}
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}
    cache = {}

    def mesh(stem):
        if stem not in cache:
            new = kn.import_fbx(index[stem])
            ob = next(o for o in new if o.type == "MESH")
            for o in new:
                if o is not ob:
                    bpy.data.objects.remove(o)
            ob.parent = None
            for m in list(ob.modifiers):
                ob.modifiers.remove(m)
            ob.name = stem + ".audit"
            cache[stem] = ob
        return cache[stem]

    def copy(ob):
        c = ob.copy()
        c.data = ob.data.copy()
        bpy.context.scene.collection.objects.link(c)
        return c

    rows, totals = [], {"synty": 0, "transfer": 0, "fixed": 0}
    for key, regions in offers.items():
        lineage, sexword = key.split("_")
        if lineage not in TAGS:
            continue                       # Robot and Skeleton offer no hair or facial hair
        tag = TAGS[lineage] + ("F" if sexword == "feminine" else "M")
        e = entries[tag]
        rig = bpy.data.objects[e["rig"]]
        body = [o for o in rig.children if o.type == "MESH" and mc.slot_of(o.name) in mc.SKIN
                and mc.slot_of(o.name) != "01HEAD" and not o.get("kn_gear")
                and not (mc.slot_of(o.name) == "38WRAP" and e["sex"] == "M")]
        states = mc.STATES_F if e["sex"] == "F" else mc.STATES_M
        covers = [s for s in regions.get("2", []) + regions.get("9", []) if s in index]
        for head in [h for h in regions.get("1", []) if h in index]:
            skin = body + [mesh(head)]
            for stem in covers:
                src = mesh(stem)
                result = {}
                for variant in ("synty", "transfer", "fixed"):
                    ob = copy(src)
                    if variant != "synty":
                        mc.transfer(e, ob, skin)
                    if variant == "fixed":
                        mc.push_out(e, ob, skin, 0.4)
                        mc.push_combos(e, ob, skin, 0.4, states)
                    found = mc.measure(e, [ob], skin, states)
                    n = sum(r["verts"] for r in found)
                    deep = max((r["max_cm"] for r in found), default=0.0)
                    result[variant] = (n, deep)
                    totals[variant] += n
                    me = ob.data
                    bpy.data.objects.remove(ob)
                    bpy.data.meshes.remove(me)
                rows.append({"body": tag, "head": head, "part": stem, **{k: v for k, v in result.items()}})
    worst = {v: sorted(rows, key=lambda r: -r[v][0])[:8] for v in ("transfer", "fixed")}
    summary = {"combinations": len(rows), "clipping_vertices": totals,
               "parts_clean_after_transfer": sum(1 for r in rows if r["transfer"][0] == 0),
               "parts_over_1cm_after_transfer": sum(1 for r in rows if r["transfer"][1] > 1.0),
               "parts_over_1cm_after_fix": sum(1 for r in rows if r["fixed"][1] > 1.0),
               "worst_after_transfer": [f"{r['body']} {r['part']}: {r['transfer'][0]} verts, {r['transfer'][1]} cm" for r in worst["transfer"]],
               "worst_after_fix": [f"{r['body']} {r['part']}: {r['fixed'][0]} verts, {r['fixed'][1]} cm" for r in worst["fixed"]]}
    if a.out:
        Path(a.out).resolve().write_text(json.dumps(rows, indent=1), encoding="utf-8")
    kn.report("Creator clipping audit", summary)


main()
