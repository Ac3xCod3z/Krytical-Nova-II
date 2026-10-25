"""Hangs a cape's hem plates from the cloth (owner, 24 Oct 2026, Scrap Sentinel: "the plates at the bottom of the
cape are not connected to the cape in some places"; Tripo made the trim as loose plates 1-7 cm off the hem).

  blender -b <Gear/<Set>_Cape.blend> --python cape_hem_plates.py -- --set SCRP [--overlap 0.004]

The plates are the light-coloured islands of the cape part (their theme cell brighter than the cloth), or with
--by-place the small islands (up to --max-faces) whose top lies within 15 cm of the cape's bottom. Each one is
moved, whole, onto the hem so it overlaps it by --overlap, and then rides the hem point nearest its top: every vertex
of the plate takes that point's bone weights and body keys, so the plate stays fixed to the cloth when the dangle
bones swing it. The collar (a light island high on the shoulders) is left alone. Prints each plate's gap before and
saves.
"""
import argparse
import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kn_common as kn  # noqa: E402


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--set", required=True)
    p.add_argument("--overlap", type=float, default=0.004)
    p.add_argument("--by-place", action="store_true",
                   help="pick the plates as the small pieces along the bottom of the cape instead of by their light "
                        "colour (UltraTex painted the Scrap Sentinel trim tan, not cream)")
    p.add_argument("--max-faces", type=int, default=150)
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def main():
    a = args()
    theme = json.loads((kn.swatch_dir() / "KN_GearSwatch.json").read_text(encoding="utf-8"))["sets"][a.set]["theme"]
    names = kn.cell_names(kn.load_spec())
    ob = next(o for o in bpy.data.objects if o.name.startswith(f"SK_KRYT_{a.set}_01_24ABAC"))
    me = ob.data
    mw, inv = ob.matrix_world, ob.matrix_world.inverted()
    uv = me.uv_layers.active.data

    def light(p):
        u, v = uv[p.loop_indices[0]].uv
        h = theme.get(names.get((int(u * 16), int(v * 16))), "#000000")
        return sum(int(h[i:i + 2], 16) for i in (1, 3, 5)) > 400

    # islands by shared vertices
    adj = [[] for _ in me.vertices]
    for e in me.edges:
        x, y = e.vertices
        adj[x].append(y)
        adj[y].append(x)
    comp = [-1] * len(me.vertices)
    isles = []
    for s in range(len(me.vertices)):
        if comp[s] >= 0:
            continue
        st, isle = [s], []
        comp[s] = len(isles)
        while st:
            x = st.pop()
            isle.append(x)
            for y in adj[x]:
                if comp[y] < 0:
                    comp[y] = len(isles)
                    st.append(y)
        isles.append(isle)
    lit = [0] * len(isles)
    tot = [0] * len(isles)
    for p in me.polygons:
        k = comp[p.vertices[0]]
        tot[k] += 1
        lit[k] += light(p)
    world = [mw @ v.co for v in me.vertices]
    top = max(p.z for p in world)
    if a.by_place:
        bottom = min(p.z for p in world)
        plates = [k for k in range(len(isles)) if 0 < tot[k] <= a.max_faces
                  and max(world[i].z for i in isles[k]) < bottom + 0.15]
    else:
        plates = [k for k in range(len(isles)) if tot[k] and lit[k] > 0.6 * tot[k]
                  and max(world[i].z for i in isles[k]) < top - 0.25]      # not the collar
    cloth_v = [i for k in range(len(isles)) if k not in plates for i in isles[k]]
    cloth_set = set(cloth_v)
    tris = [tuple(p.vertices) for p in me.polygons if p.vertices[0] in cloth_set]
    tree = BVHTree.FromPolygons(world, tris, all_triangles=False)
    kd_pts = cloth_v

    groups = [g.name for g in ob.vertex_groups]
    blocks = me.shape_keys.key_blocks if me.shape_keys else []
    report = []
    for k in plates:
        vs = isles[k]
        # gap: the plate's nearest point to the cloth
        best = min(((tree.find_nearest(world[i]), i) for i in vs), key=lambda r: r[0][3])
        (hit, _n, _f, gap), vi = best
        move = Vector()
        if gap > 1e-5:
            d = hit - world[vi]
            move = d * ((gap + a.overlap) / gap)
        # the hem point it hangs from: the cloth vertex nearest the plate's top middle
        ptop = max(world[i].z for i in vs)
        tm = [world[i] for i in vs if world[i].z > ptop - 0.02]
        anchor_at = sum(tm, Vector()) / len(tm) + move
        anchor = min(kd_pts, key=lambda j: (world[j] - anchor_at).length)
        aw = {groups[g.group]: g.weight for g in me.vertices[anchor].groups}
        lm = inv.to_3x3() @ move
        for i in vs:
            me.vertices[i].co += lm
            for g in list(me.vertices[i].groups):
                ob.vertex_groups[g.group].remove([i])
            for name, w in aw.items():
                ob.vertex_groups[name].add([i], w, "REPLACE")
        if blocks:
            base = blocks[0]
            for kb in blocks:
                kb.data[0].co  # touch
            for kb in blocks[1:]:
                delta = kb.data[anchor].co - base.data[anchor].co
                for i in vs:
                    kb.data[i].co = me.vertices[i].co + delta
            for i in vs:
                base.data[i].co = me.vertices[i].co.copy()
        report.append({"faces": tot[k], "gap_cm": round(gap * 100, 2), "rides": int(anchor)})
    me.update()
    bpy.ops.wm.save_mainfile()
    print("HEM_PLATES " + json.dumps(report))


main()
