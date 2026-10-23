"""How much clipping the Krytical Nova sliders add to gear, over every outfit piece of the humanoid packs.

  run_blender.py --blend SyntyMannequinModeling.blend creator_gear_audit.py [--packs SCFI_CIVL:Civilian,...] [--out audit.json]

Each piece gets the new keys the way the game will give them at merge time (the bare body's motion at the nearest
point; Synty's own keys untouched), then is measured at each slider state against the same state with the new
sliders at 0. Only what the new sliders add counts: clipping Synty's own keys already have is not ours.
Nothing is saved.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import mannequin_clip as mc

importlib.reload(mc)
SLOTS = ("10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR")
STATES = {"F": [("bust", {"kn_bust": 1}), ("butt", {"kn_butt": 1}),
                ("heavy+bust+butt", {"defaultHeavy": 1, "kn_bust": 1, "kn_butt": 1}),
                ("muscular+bust", {"defaultBuff": 1, "kn_bust": 1})],
          "M": [("defined", {"kn_defined": 1}), ("muscular+defined", {"defaultBuff": 1, "kn_defined": 1}),
                ("heavy+defined", {"defaultHeavy": 1, "kn_defined": 1})]}
SKIN_BODY = ("10TORS", "38WRAP", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--packs", default="SCFI_CIVL:Civilian,SCFI_SOLD:Soldier,VIKG_WARR:Viking,ELVN_WARR:Elf")
    ap.add_argument("--out")
    a = ap.parse_args(kn.tool_args())
    entries = {e["tag"]: e for e in json.loads(bpy.context.scene["kn_mannequins"])}
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}
    rows = []
    for chunk in a.packs.split(","):
        pack, lineage = chunk.split(":")
        stems = sorted(s for s in index if s.startswith(f"SK_{pack}_") and mc.slot_of(s) in SLOTS)
        for sex in ("F", "M"):
            e = entries[lineage + sex]
            rig = bpy.data.objects[e["rig"]]
            skin = [o for o in rig.children if o.type == "MESH" and mc.slot_of(o.name) in SKIN_BODY
                    and not o.get("kn_gear") and not (mc.slot_of(o.name) == "38WRAP" and sex == "M")]
            rest = mc.rest_values(e)
            for stem in stems:
                new = kn.import_fbx(index[stem])
                ob = next(o for o in new if o.type == "MESH")
                for o in new:
                    if o is not ob:
                        bpy.data.objects.remove(o)
                ob.parent = None
                for m in list(ob.modifiers):
                    ob.modifiers.remove(m)
                mc.transfer(e, ob, skin)
                worst, added = 0.0, 0
                for label, change in STATES[sex]:
                    with_kn = dict(rest, **change)
                    without = {k: v for k, v in with_kn.items() if not k.startswith("kn_")}
                    clipped = []
                    for values in (without, with_kn):
                        sv, st = mc.skin_soup(skin, values)
                        bvh = BVHTree.FromPolygons(sv, st)
                        clipped.append({i: mc.inside(bvh, p)[0] for i, p in enumerate(mc.positions(ob, values))})
                    new_in = {i: d for i, d in clipped[1].items() if d > 0.05 and clipped[0][i] <= 0.05}
                    added += len(new_in)
                    worst = max([worst] + list(new_in.values()))
                rows.append({"part": stem, "body": e["tag"], "added_verts": added, "deepest_cm": round(worst, 2),
                             "verts": len(ob.data.vertices)})
                me = ob.data
                bpy.data.objects.remove(ob)
                bpy.data.meshes.remove(me)
    bad = [r for r in rows if r["deepest_cm"] > 1.0]
    summary = {"pieces_checked": len(rows), "clean": sum(1 for r in rows if r["added_verts"] == 0),
               "under_1cm": sum(1 for r in rows if 0 < r["deepest_cm"] <= 1.0), "over_1cm": len(bad),
               "worst": [f"{r['body']} {r['part']}: {r['added_verts']} verts, {r['deepest_cm']} cm"
                         for r in sorted(rows, key=lambda r: -r["deepest_cm"])[:12]]}
    if a.out:
        Path(a.out).resolve().write_text(json.dumps(rows, indent=1), encoding="utf-8")
    kn.report("Creator gear audit", summary)


main()
