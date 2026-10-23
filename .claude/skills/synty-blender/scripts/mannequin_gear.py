"""Put an outfit on a mannequin of the Synty Mannequin Modeling scene and make it follow the Krytical Nova sliders.

  run_blender.py --blend SyntyMannequinModeling.blend mannequin_gear.py --tag CivilianF --pack SCFI_CIVL [--variant 1]
        [--slots 10TORS,17HIPS]   default: every body slot the pack has in that variant
        [--remove]                take the gear off that mannequin again

The gear parts come from Assets/Synty (read only), go under the mannequin's rig, keep their Synty keys and receive
kn_bust / kn_butt / kn_defined copied from the bare body under them by nearest surface point (the same transfer
sidekick_fit.py uses for Synty's four keys). The body part of the same slot is hidden while gear is on.
Each gear object is marked kn_gear = True and kn_replaces = <body part name>, which mannequin_render.py --gear reads.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

NEW_KEYS = ("kn_bust", "kn_butt", "kn_defined")
BODY = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR",
        "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR"]


def slot_of(name):
    return name.split("_")[4] if name.count("_") >= 5 else ""


def body_soup(body_parts):
    """Bare body of one mannequin in mesh-local (cm) space: triangles and, per vertex, the delta of each new key."""
    verts, tris, deltas = [], [], {k: [] for k in NEW_KEYS}
    for ob in body_parts:
        me = ob.data
        keys = me.shape_keys.key_blocks if me.shape_keys else None
        base = len(verts)
        # the body as shown: Synty keys at their current values (feminine 100 on a feminine body)
        mix = [keys[0].data[i].co.copy() for i in range(len(me.vertices))] if keys else [v.co.copy() for v in me.vertices]
        if keys:
            for kb in keys[1:]:
                if kb.value and kb.name not in NEW_KEYS:
                    for i in range(len(mix)):
                        mix[i] += (kb.data[i].co - keys[0].data[i].co) * kb.value
        verts.extend(mix)
        for k in NEW_KEYS:
            if keys and k in keys:
                deltas[k].extend(keys[k].data[i].co - keys[0].data[i].co for i in range(len(me.vertices)))
            else:
                deltas[k].extend(Vector((0, 0, 0)) for _ in me.vertices)
        me.calc_loop_triangles()
        tris.extend(tuple(base + i for i in t.vertices) for t in me.loop_triangles)
    return verts, tris, deltas


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tag", required=True)
    ap.add_argument("--pack")
    ap.add_argument("--variant", type=int, default=1)
    ap.add_argument("--slots")
    ap.add_argument("--remove", action="store_true")
    a = ap.parse_args(kn.tool_args())
    if kn.is_live():
        raise SystemExit("Run this headless: it saves the scene.")
    scene = bpy.context.scene
    entries = json.loads(scene["kn_mannequins"])
    entry = next((e for e in entries if e["tag"] == a.tag), None)
    if entry is None:
        raise SystemExit(f"No mannequin '{a.tag}'. Tags: {[e['tag'] for e in entries]}")
    rig = bpy.data.objects[entry["rig"]]
    meshes = [o for o in rig.children if o.type == "MESH"]

    if a.remove:
        gone = []
        for o in [m for m in meshes if m.get("kn_gear")]:
            gone.append(o.name)
            me = o.data
            bpy.data.objects.remove(o)
            bpy.data.meshes.remove(me)
        for o in rig.children:
            o.hide_render = o.hide_viewport = False
            o.hide_set(False)
        kn.save_blend()
        kn.report("Mannequin gear removed", {"tag": a.tag, "removed": gone})
        return

    if not a.pack:
        raise SystemExit("--pack SCFI_CIVL (and --variant) names the outfit.")
    slots = [s.strip() for s in a.slots.split(",")] if a.slots else BODY
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob(f"**/SK_{a.pack}_{a.variant:02d}_*.fbx")}
    body_parts = [m for m in meshes if slot_of(m.name) in BODY and not m.get("kn_gear")]
    verts, tris, deltas = body_soup(body_parts)
    bvh = BVHTree.FromPolygons(verts, tris)
    preview = bpy.data.materials.get(kn.PREVIEW_MATERIAL)
    col = bpy.data.collections[entry["collection"]]
    added, missing, far_total = [], [], 0
    for slot in slots:
        stem = next((s for s in index if slot_of(s) == slot), None)
        if stem is None:
            missing.append(slot)
            continue
        new = kn.import_fbx(index[stem])
        arm = next((o for o in new if o.type == "ARMATURE"), None)
        ob = next((o for o in new if o.type == "MESH"), None)
        if ob is None:
            missing.append(slot)
            continue
        ob.name = f"{stem}.{a.tag}.gear"
        ob.data.name = ob.name
        extra = [b for b in (arm.data.bones if arm else []) if b.name not in rig.data.bones
                 and b.name in {g.name for g in ob.vertex_groups}]
        if extra:
            bpy.context.view_layer.objects.active = arm
            bpy.ops.object.mode_set(mode="EDIT")
            info = [{"name": eb.name, "parent": eb.parent.name if eb.parent else None, "head": eb.head.copy(),
                     "tail": eb.tail.copy(), "roll": eb.roll} for eb in arm.data.edit_bones if eb.name in {b.name for b in extra}]
            bpy.ops.object.mode_set(mode="OBJECT")
            bpy.context.view_layer.objects.active = rig
            bpy.ops.object.mode_set(mode="EDIT")
            for b in info:
                eb = rig.data.edit_bones.new(b["name"])
                eb.head, eb.tail, eb.roll = b["head"], b["tail"], b["roll"]
                if b["parent"] and b["parent"] in rig.data.edit_bones:
                    eb.parent = rig.data.edit_bones[b["parent"]]
            bpy.ops.object.mode_set(mode="OBJECT")
        if arm:
            bpy.data.objects.remove(arm)
        for mod in list(ob.modifiers):
            ob.modifiers.remove(mod)
        ob.parent = rig
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = Matrix.Identity(4)
        ob.modifiers.new("Armature", "ARMATURE").object = rig
        ob.data.materials.clear()
        if preview:
            ob.data.materials.append(preview)
        kn.move_to_collection(ob, col)
        keys = ob.data.shape_keys
        if keys:
            if "masculineFeminine" in keys.key_blocks:
                keys.key_blocks["masculineFeminine"].value = 1.0 if entry["sex"] == "F" else 0.0
            if "defaultBuff" in keys.key_blocks:
                keys.key_blocks["defaultBuff"].value = 0.5
        # The new sliders: sampled from the bare body's surface under each gear vertex.
        if not keys:
            ob.shape_key_add(name="Basis", from_mix=False)
            keys = ob.data.shape_keys
        basis = keys.key_blocks[0]
        far = 0
        hits = []
        for v in ob.data.vertices:
            loc, _n, idx, dist = bvh.find_nearest(v.co)
            tri = tris[idx]
            hits.append((tri, poly_3d_calc([verts[i] for i in tri], loc)))
            far += dist > 15.0
        far_total += far
        for k in NEW_KEYS:
            kb = keys.key_blocks.get(k) or ob.shape_key_add(name=k, from_mix=False)
            kb.slider_min, kb.slider_max, kb.value = 0.0, 1.0, 0.0
            for vi, (tri, bw) in enumerate(hits):
                d = sum((deltas[k][c] * w for c, w in zip(tri, bw)), Vector((0, 0, 0)))
                kb.data[vi].co = basis.data[vi].co + d
            if k in rig.keys():
                fc = kb.driver_add("value")
                var = fc.driver.variables.new()
                fc.driver.type = "AVERAGE"
                var.name, var.type = "v", "SINGLE_PROP"
                var.targets[0].id_type, var.targets[0].id = "OBJECT", rig
                var.targets[0].data_path = f'["{k}"]'
        replaced = next((m.name for m in body_parts if slot_of(m.name) == slot), "")
        ob["kn_gear"] = True
        ob["kn_replaces"] = replaced
        if replaced:
            bpy.data.objects[replaced].hide_render = True
            bpy.data.objects[replaced].hide_set(True)          # the eye in the outliner; render ignores it
        added.append(ob.name)
    kn.save_blend()
    kn.report("Mannequin gear", {"tag": a.tag, "pack": f"{a.pack} {a.variant:02d}", "added": added,
                                 "slots_without_a_part": missing,
                                 "vertices_far_from_body": far_total},
              warnings=([f"{far_total} gear vertices sit more than 15 cm from the body; their slider motion is a guess."]
                        if far_total else []))


main()
