"""Do our armor's body keys react the way Synty's armor does? One number per key, ours beside Synty's.

  run_blender.py --blend "<Gear/<Piece>.blend>" gear_keys_vs_synty.py --synty SK_ELVN_WARR_01 [--slots 10TORS,17HIPS]

On the working scene's bare body, for every body key at full: how far each armor vertex moves against the skin
under it (gain 1.0 = moves exactly with the body; Synty's outfits sit a little above 1 so cloth stays outside), how
much it strays from the body's motion (spread, as a share of the body's motion), and how many vertices end up
inside the body deeper than 5 mm. The same is measured for the Synty outfit's parts of the same slots on the same
body, so the table says whether ours behaves like theirs. Nothing is saved.
"""
import argparse
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

KEYS = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny", "kn_bust", "kn_butt", "kn_defined")


def world_key(ob, key):
    """Basis and keyed positions in world space (rest pose), or None when the mesh has no such key."""
    ks = ob.data.shape_keys
    if not ks or key not in ks.key_blocks:
        return None
    mw = ob.matrix_world
    b = ks.key_blocks[0].data
    k = ks.key_blocks[key].data
    return [mw @ b[i].co for i in range(len(b))], [mw @ k[i].co for i in range(len(k))]


def body_soup(body, key):
    V0, V1, T = [], [], []
    for o in body:
        wk = world_key(o, key)
        mw = o.matrix_world
        base = [mw @ v.co for v in o.data.vertices]
        keyed = wk[1] if wk else base
        n = len(V0)
        V0 += base
        V1 += keyed
        o.data.calc_loop_triangles()
        T += [tuple(n + i for i in t.vertices) for t in o.data.loop_triangles]
    return V0, V1, T


def measure(armor, body):
    out = {}
    for key in KEYS:
        V0, V1, T = body_soup(body, key)
        if all(a == b for a, b in zip(V0, V1)):
            continue
        t0, t1 = BVHTree.FromPolygons(V0, T), BVHTree.FromPolygons(V1, T)
        gains, spread, moved_body, inside, n = [], 0.0, 0.0, 0, 0
        has = False
        for ob in armor:
            wk = world_key(ob, key)
            if wk is None:
                continue
            has = True
            for p0, p1 in zip(*wk):
                loc, _nrm, idx, _d = t0.find_nearest(p0)
                if loc is None:
                    continue
                tri = T[idx]
                w = poly_3d_calc([V0[i] for i in tri], loc)
                db = sum(((V1[i] - V0[i]) * x for i, x in zip(tri, w)), Vector())
                da = p1 - p0
                n += 1
                if db.length > 0.002:
                    gains.append(da.dot(db) / db.length_squared)
                    spread += (da - db).length
                    moved_body += db.length
                l1, n1, _i, _d = t1.find_nearest(p1)
                if l1 is not None and (p1 - l1).dot(n1) < -0.005:
                    inside += 1
        if not has or not gains:
            out[key] = "no key" if not has else "body does not move here"
            continue
        gains.sort()
        out[key] = {"gain_median": round(gains[len(gains) // 2], 2),
                    "gain_10_90": [round(gains[len(gains) // 10], 2), round(gains[9 * len(gains) // 10], 2)],
                    "spread_pct": round(100 * spread / max(moved_body, 1e-9)),
                    "inside_5mm": inside, "vertices": n}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--synty", required=True, help="Synty outfit stem without the slot, e.g. SK_ELVN_WARR_01")
    ap.add_argument("--slots", default="10TORS,17HIPS")
    a = ap.parse_args(kn.tool_args())
    slots = a.slots.split(",")
    rig = bpy.data.objects["SK_Rig"]
    ours = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SK_KRYT_")
            and o.name.split("_")[4] in slots]
    body = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig and not o.name.startswith("SK_KRYT_")
            and len(o.name.split("_")) > 4 and o.name.split("_")[4] in slots]
    for o in bpy.data.objects:
        if o.type == "MESH" and o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks[1:]:
                kb.value = 0.0
    index = {p.stem: p for p in kn.project_root().joinpath("Assets/Synty/SidekickCharacters/Resources/Meshes").glob("**/SK_*.fbx")}
    theirs = []
    for s in slots:
        stem = next((k for k in index if k.startswith(f"{a.synty}_{s}_")), None)
        if stem is None:
            continue
        new = kn.import_fbx(index[stem])
        theirs += [o for o in new if o.type == "MESH"]
    bpy.context.view_layer.update()
    kn.report("Armor keys against Synty's", {
        "body": [o.name for o in body],
        "ours": {"parts": [o.name for o in ours], "keys": measure(ours, body)},
        "synty": {"parts": [o.name for o in theirs], "keys": measure(theirs, body)}})


main()
