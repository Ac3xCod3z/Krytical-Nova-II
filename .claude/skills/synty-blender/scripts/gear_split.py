"""Splits a T-pose gear model (several pieces generated together on the mannequin, already through tpose_import.py)
into one object per piece, by which part of the bare body each island lies nearest to.

  blender -b <Gear blend> --python gear_split.py -- --object <Set>_Gear --set <Set> [--pieces helmet,gloves,boots,cape]

head -> <Set>_Helmet; hands and forearms -> <Set>_Gloves; feet and lower legs -> <Set>_Boots; the rest (neck, trunk,
hips, upper arms: the cape and its collar) -> <Set>_Cape. Each new object keeps the material and texture. Prints the
island counts and saves the blend; the original stays as <object>_Unsplit (hidden).
"""
import argparse
import json
import re
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

BODY = re.compile(r"_(01HEAD|1[0-9][A-Z]{4}|2[01][A-Z]{4})_")
SLOT_OF = {"01HEAD": "Helmet", "13ALWL": "Gloves", "14ALWR": "Gloves", "15HNDL": "Gloves", "16HNDR": "Gloves",
           "18LEGL": "Boots", "19LEGR": "Boots", "20FOTL": "Boots", "21FOTR": "Boots"}


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--object", required=True)
    p.add_argument("--set", required=True)
    p.add_argument("--pieces", default="helmet,gloves,boots,cape",
                   help="the pieces this model holds; without a cape, what the cape would take below the trunk is "
                        "the boots' (Scrap Sentinel helmet and boots, 24 Oct 2026: the tall shafts went to a cape)")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def main():
    a = args()
    pieces = set(a.pieces.split(","))
    ob = bpy.data.objects[a.object]
    body = [o for o in bpy.data.objects if o.type == "MESH" and BODY.search(o.name) and o != ob
            and not o.name.startswith(a.set)]
    dg = bpy.context.evaluated_depsgraph_get()
    trees = []
    for o in body:
        ev = o.evaluated_get(dg)
        bm = bmesh.new()
        bm.from_mesh(ev.to_mesh())
        bm.transform(o.matrix_world)
        trees.append((BODY.search(o.name).group(1), BVHTree.FromBMesh(bm)))
        ev.to_mesh_clear()
    # the collar ring round the neck is the cape's, not the helmet's: the head part ends at the neck
    head_lo = None
    for o in body:
        if BODY.search(o.name).group(1) == "01HEAD":
            zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
            head_lo = min(zs) + 0.25 * (max(zs) - min(zs))
    # lower legs: only below the knee count as boot ground (a thigh plate is not a boot)
    leg_z = {}
    for o in body:
        tag = BODY.search(o.name).group(1)
        if tag in ("18LEGL", "19LEGR"):
            zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
            leg_z[tag] = (min(zs) + max(zs)) / 2

    hip_z = None
    for o in body:
        if BODY.search(o.name).group(1) == "17HIPS":
            hip_z = min((o.matrix_world @ v.co).z for v in o.data.vertices)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    mw = ob.matrix_world
    seen, label = set(), {}
    counts = {}
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isle = [f], []
        seen.add(f.index)
        while stack:
            x = stack.pop()
            isle.append(x)
            for e in x.edges:
                for y in e.link_faces:
                    if y.index not in seen:
                        seen.add(y.index)
                        stack.append(y)
        votes = {}
        for x in isle:
            c = mw @ x.calc_center_median()
            best, bd = None, 1e9
            for tag, tree in trees:
                hit = tree.find_nearest(c)
                if hit[0] is not None and hit[3] < bd:
                    best, bd = tag, hit[3]
            slot = SLOT_OF.get(best, "Cape")
            if best == "01HEAD" and head_lo is not None and c.z < head_lo:
                slot = "Cape"
            if best in leg_z:
                slot = "Boots" if c.z < leg_z[best] else "Cape"   # the hem by the thighs is the cape's
            votes[slot] = votes.get(slot, 0.0) + x.calc_area()
        slot = max(votes, key=votes.get)
        if slot == "Cape" and "cape" not in pieces:
            zc = sum((mw @ x.calc_center_median()).z for x in isle) / len(isle)
            slot = "Boots" if hip_z is not None and zc < hip_z else "Helmet" if head_lo and zc > head_lo else "Boots"
        for x in isle:
            label[x.index] = slot
        counts[slot] = counts.get(slot, 0) + 1

    # leftovers: a cape island lying wholly below the cloth's hem sits on the shins, so it belongs to the boots
    # (Scrap Sentinel: three small shin plates went to the cape because the robot's leg middle is low)
    def isles(slot):
        seen2, out = set(), []
        for i, sl in label.items():
            if sl != slot or i in seen2:
                continue
            st, isle = [bm.faces[i]], []
            seen2.add(i)
            while st:
                x = st.pop()
                isle.append(x.index)
                for e in x.edges:
                    for y in e.link_faces:
                        if y.index not in seen2 and label.get(y.index) == slot:
                            seen2.add(y.index)
                            st.append(y)
            out.append(isle)
        return out
    cape = isles("Cape")
    if cape and "cape" in pieces:
        main = max(cape, key=len)
        hem = min((mw @ v.co).z for i in main for v in bm.faces[i].verts)
        moved = 0
        for isle in cape:
            if max((mw @ v.co).z for i in isle for v in bm.faces[i].verts) < hem - 0.15:
                for i in isle:
                    label[i] = "Boots"
                moved += 1
        counts["cape_islands_to_boots"] = moved
    made = {}
    for slot in sorted(set(label.values())):
        piece = ob.copy()
        piece.data = ob.data.copy()
        piece.name = piece.data.name = f"{a.set}_{slot}"
        bpy.context.scene.collection.objects.link(piece)
        pb = bmesh.new()
        pb.from_mesh(piece.data)
        pb.faces.ensure_lookup_table()
        bmesh.ops.delete(pb, geom=[pf for pf in pb.faces if label.get(pf.index) != slot], context="FACES")
        pb.to_mesh(piece.data)
        pb.free()
        made[slot] = len(piece.data.polygons)
    ob.name = a.object + "_Unsplit"
    ob.hide_set(True)
    ob.hide_render = True
    bpy.ops.wm.save_mainfile()
    print("SPLIT " + json.dumps({"islands": counts, "faces": made}))


main()
