"""Fix every hair and facial hair the creator offers so it clears every body it can be worn on.

  run_blender.py --blend SyntyMannequinModeling.blend creator_hair_fix.py --parts creator_parts.json
        --save ".../SyntyHairFixes.blend" [--rounds 3] [--margin 0.4] [--out report.json]

For each part: the body's motion is transferred from the human bare body with head 01 (one reference, so the part
moves the same way on every lineage; Synty's human and elf bodies move alike), then, round by round, every body and
head that offers the part asks for its push-out and the part takes the largest asked of each vertex for each key.
Measured again on every combination afterwards. The fixed meshes (Synty's mesh, Synty's keys plus the filled ones)
are saved alone into --save, for mannequin_export-style export once the owner says so. The mannequin scene is
not changed.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import mannequin_clip as mc

importlib.reload(mc)
LINEAGES = ("Civilian", "Soldier", "Viking", "Elf")
KEYS = [k for k in mc.SYNTY + mc.OURS if k != "masculineFeminine"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--margin", type=float, default=0.4)
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
            ob.name = stem + ".src"
            cache[stem] = ob
        return cache[stem]

    def copy(ob, name=None):
        c = ob.copy()
        c.data = ob.data.copy()
        if name:
            c.name = c.data.name = name
        bpy.context.scene.collection.objects.link(c)
        return c

    def drop(ob):
        me = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.meshes.remove(me)

    def body_of(tag):
        e = entries[tag]
        rig = bpy.data.objects[e["rig"]]
        return e, [o for o in rig.children if o.type == "MESH" and mc.slot_of(o.name) in mc.SKIN
                   and mc.slot_of(o.name) != "01HEAD" and not o.get("kn_gear")
                   and not (mc.slot_of(o.name) == "38WRAP" and e["sex"] == "M")]

    # Every (body, head) that offers each part.
    wearers = {}
    for key, regions in offers.items():
        lineage, sexword = key.split("_")
        if lineage not in LINEAGES:
            continue
        tag = lineage + ("F" if sexword == "feminine" else "M")
        for stem in regions.get("2", []) + regions.get("9", []):
            for head in regions.get("1", []):
                if stem in index and head in index:
                    wearers.setdefault(stem, []).append((tag, head))

    ref_e, ref_body = body_of("CivilianM")
    ref_skin = ref_body + [mesh("SK_HUMN_BASE_01_01HEAD_HU01")]
    fixed, rows = [], []
    for stem, who in sorted(wearers.items()):
        master = copy(mesh(stem), stem)
        mc.transfer(ref_e, master, ref_skin)
        ks = master.data.shape_keys.key_blocks
        n = len(master.data.vertices)
        combos = []
        for tag, head in who:
            e, body = body_of(tag)
            combos.append((e, body + [mesh(head)], mc.STATES_F if e["sex"] == "F" else mc.STATES_M, tag, head))
        for _round in range(a.rounds):
            ask = {k: [Vector((0, 0, 0))] * n for k in KEYS if k in ks}
            for e, skin, states, _t, _h in combos:
                c = copy(master)
                mc.push_out(e, c, skin, a.margin)
                mc.push_combos(e, c, skin, a.margin, states)
                cks = c.data.shape_keys.key_blocks
                for k in ask:
                    for i in range(n):
                        d = cks[k].data[i].co - ks[k].data[i].co
                        if d.length > ask[k][i].length:
                            ask[k][i] = d
                drop(c)
            moved = 0
            for k, deltas in ask.items():
                for i, d in enumerate(deltas):
                    if d.length > 1e-6:
                        ks[k].data[i].co += d
                        moved += 1
            if not moved:
                break
        before = after = 0
        deep_before = deep_after = 0.0
        for e, skin, states, tag, head in combos:
            r0 = mc.measure(e, [mesh(stem)], skin, states)
            r1 = mc.measure(e, [master], skin, states)
            before += sum(r["verts"] for r in r0)
            after += sum(r["verts"] for r in r1)
            deep_before = max([deep_before] + [r["max_cm"] for r in r0])
            deep_after = max([deep_after] + [r["max_cm"] for r in r1])
        rows.append({"part": stem, "wearers": len(combos), "before": before, "after": after,
                     "deepest_before_cm": deep_before, "deepest_after_cm": deep_after})
        fixed.append(master)

    # Keep only the fixed meshes and save them as their own file.
    keep = set(fixed)
    for ob in list(bpy.data.objects):
        if ob not in keep:
            bpy.data.objects.remove(ob)
    for col in list(bpy.data.collections):
        bpy.data.collections.remove(col)
    for ob in fixed:
        if ob.name not in bpy.context.scene.collection.objects:
            bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene["kn_hair_fixes"] = json.dumps(rows)
    bpy.ops.outliner.orphans_purge(do_recursive=True) if bpy.app.background else None
    save = Path(a.save).resolve()
    bpy.ops.wm.save_as_mainfile(filepath=str(save))
    summary = {"parts": len(rows), "clipping_vertices_before": sum(r["before"] for r in rows),
               "clipping_vertices_after": sum(r["after"] for r in rows),
               "parts_over_1cm_after": [f"{r['part']} {r['deepest_after_cm']} cm ({r['after']} verts)"
                                        for r in rows if r["deepest_after_cm"] > 1.0],
               "saved": str(save)}
    if a.out:
        Path(a.out).resolve().write_text(json.dumps(rows, indent=1), encoding="utf-8")
    kn.report("Creator hair fix", summary)


main()
