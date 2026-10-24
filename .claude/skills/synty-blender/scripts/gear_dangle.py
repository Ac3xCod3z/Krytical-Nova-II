"""Dangle bones for a fitted gear set's loose cloth (a sash's tails), the way Synty builds them.

  run_blender.py --blend "<Gear/<Piece>.blend>" gear_dangle.py --cells "10TORS:0,13" "17HIPS:0,8"
        [--bones 3] [--parent pelvis] [--prefix hips] [--sheet out.png]

The cloth is picked by its swatch cells (slot:u,v of the 16x16 grid; the set's theme names which cell is the sash).
Below the belt (the height where that cloth wraps most of the way round the body) each hanging tail gets a chain
of `--bones` bones from its knot to its end, `<prefix>_dyn_NN_MM_<side>` (NN from 11: Synty's own parts use 01-04
and bones merge by name) under `--parent`, as Synty's tors_dyn_
bones sit under spine_03; the game's DangleBones swings every `_dyn_` chain (a chain's first bone stays pinned).
The tail is weighted along its chain, eased in from the knot so the belt never swings. Both slots' parts get the
bones' weights; the rig gets the bones. Saves the scene.
"""
import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)


def cell_faces(ob, cells):
    me = ob.data
    uv = me.uv_layers.active.data
    out = []
    for p in me.polygons:
        u = sum(uv[l].uv.x for l in p.loop_indices) / p.loop_total
        v = sum(uv[l].uv.y for l in p.loop_indices) / p.loop_total
        if (int(u * 16), int(v * 16)) in cells:
            out.append(p)
    return out


def belt_bottom(points, centre):
    """The lowest 1 cm slice where the cloth goes at least 70 % of the way round the body."""
    zs = sorted(p.z for p in points)
    best = None
    z = zs[0]
    while z < zs[-1]:
        ang = {int((math.degrees(math.atan2(p.y - centre.y, p.x - centre.x)) + 180) // 15)
               for p in points if z <= p.z < z + 0.01}
        if len(ang) >= 17:
            best = z if best is None else min(best, z)
        z += 0.01
    return best


def clusters(points, gap=0.03):
    kd = KDTree(len(points))
    for i, p in enumerate(points):
        kd.insert(p, i)
    kd.balance()
    seen, out = set(), []
    for s in range(len(points)):
        if s in seen:
            continue
        grp, stack = [s], [s]
        seen.add(s)
        while stack:
            x = stack.pop()
            for _co, j, _d in kd.find_range(points[x], gap):
                if j not in seen:
                    seen.add(j)
                    grp.append(j)
                    stack.append(j)
        out.append(grp)
    return out


def tails(idx, pts, owner, touch=0.015):
    """Hanging pieces: tail vertices joined by the mesh's own edges, and across the slot parts where they touch
    (a low-poly tail's edges are longer than any distance that would group it by nearness). Lists of positions in
    `idx`."""
    pos = {i: k for k, i in enumerate(idx)}
    par = list(range(len(idx)))

    def f(x):
        while par[x] != x:
            par[x] = par[par[x]]
            x = par[x]
        return x
    by_part = {}
    for k, i in enumerate(idx):
        ob, vi = owner[i]
        by_part.setdefault(ob.name, {})[vi] = k
    for ob in {owner[i][0] for i in idx}:
        m = by_part[ob.name]
        for e in ob.data.edges:
            a, b = e.vertices
            if a in m and b in m:
                par[f(m[a])] = f(m[b])
    for g in clusters([pts[i] for i in idx], touch):
        for k in g[1:]:
            par[f(k)] = f(g[0])
    out = {}
    for k in range(len(idx)):
        out.setdefault(f(k), []).append(k)
    return list(out.values())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cells", nargs="+", required=True, help="slot:u,v, e.g. 10TORS:0,13")
    ap.add_argument("--bones", type=int, default=3)
    ap.add_argument("--parent", default="pelvis")
    ap.add_argument("--prefix", default="hips")
    ap.add_argument("--min-length", type=float, default=0.10, help="metres; shorter tails stay stiff")
    ap.add_argument("--min-vertices", type=int, default=20, help="smaller hanging scraps ride the cloth instead")
    ap.add_argument("--sectors", type=int, default=1,
                    help="a wide hanging sheet (a cape) split into this many strips round the parent bone, a chain "
                         "each, every vertex blending its two nearest chains; one chain swung the whole cape as a board")
    ap.add_argument("--first", type=int, default=11,
                    help="first chain number; Synty's parts own hips_dyn_01..04 and the game's merge matches bones "
                         "by name, so ours start at 11 (owner's parts session, 23 Oct 2026)")
    a = ap.parse_args(kn.tool_args())
    rig = bpy.data.objects["SK_Rig"]
    want = {}
    for c in a.cells:
        slot, uv = c.split(":")
        u, v = (int(x) for x in uv.split(","))
        want.setdefault(slot, set()).add((u, v))
    parts = {o.name.split("_")[4]: o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SK_KRYT_")}

    # every sash vertex in world space, remembering its part
    pts, owner = [], []
    for slot, cells in want.items():
        ob = parts[slot]
        mw = ob.matrix_world
        vs = {vi for p in cell_faces(ob, cells) for vi in p.vertices}
        for vi in vs:
            pts.append(mw @ ob.data.vertices[vi].co)
            owner.append((ob, vi))
    if not pts:
        raise SystemExit("No faces in those cells.")
    pel = rig.matrix_world @ rig.pose.bones[a.parent].head
    z_belt = belt_bottom(pts, pel)
    if z_belt is None:
        # panels (a skeleton's loincloth, a tabard) never wrap the body: they hang from their own top, the first
        # few centimetres staying put as stitched on
        z_belt = max(p.z for p in pts) - 0.06
    tail_idx = [i for i, p in enumerate(pts) if p.z < z_belt - 0.01]
    # a scrap of a few vertices gets no chain of its own: swung on its own it drifted off the cloth (the Skeleton's
    # rag strips); it rides the cloth under it with the studs below
    groups = [g for g in tails(tail_idx, pts, owner) if len(g) >= a.min_vertices]
    chains, report = [], []

    def angle(p):
        return math.atan2(p.x - pel.x, p.y - pel.y)     # round the parent bone: 0 straight behind, the seam in front
    pieces = []
    for gi, g in enumerate(groups):
        ids = [tail_idx[k] for k in g]
        if a.sectors <= 1:
            pieces.append((gi, ids))
            continue
        angs = sorted(angle(pts[i]) for i in ids)
        lo_a, hi_a = angs[0], angs[-1]
        width = (hi_a - lo_a) / a.sectors or 1.0
        by = {}
        for i in ids:
            by.setdefault(min(a.sectors - 1, int((angle(pts[i]) - lo_a) / width)), []).append(i)
        for k in sorted(by):
            if len(by[k]) >= a.min_vertices:
                pieces.append((gi, by[k]))
    chain_group = []
    for gi, ids in pieces:
        zs = [pts[i].z for i in ids]
        top, bot = z_belt, min(zs)
        if top - bot < a.min_length:
            continue
        # the tail's centre line, slice by slice
        line = []
        for j in range(a.bones + 1):
            z = top - (top - bot) * j / a.bones
            near = [pts[i] for i in ids if abs(pts[i].z - z) < (top - bot) / (2 * a.bones) + 0.01] or \
                   [min((pts[i] for i in ids), key=lambda p: abs(p.z - z))]
            c = sum(near, Vector()) / len(near)
            line.append(Vector((c.x, c.y, z)))
        side = "l" if line[0].x > 0.02 else ("r" if line[0].x < -0.02 else "c")
        chains.append((ids, line, side))
        chain_group.append(gi)
    if not chains:
        raise SystemExit("No hanging tail long enough.")

    # bones on the rig (edit mode works in the rig's own space)
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.selected_objects:
        o.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = rig.data.edit_bones
    inv = rig.matrix_world.inverted()
    names = []
    count = {}
    for ids, line, side in chains:
        count[side] = count.get(side, a.first - 1) + 1
        k = count[side]
        prev = eb[a.parent]
        chain_names = []
        for j in range(a.bones):
            nm = f"{a.prefix}_dyn_{k:02d}_{j + 1:02d}_{side}"
            if nm in eb:
                eb.remove(eb[nm])
            b = eb.new(nm)
            b.head = inv @ line[j]
            b.tail = inv @ line[j + 1]
            b.roll = 0.0
            b.parent = prev
            b.use_connect = j > 0
            b.use_deform = True
            prev = b
            chain_names.append(nm)
        names.append(chain_names)
    bpy.ops.object.mode_set(mode="OBJECT")

    def along(ci, p):
        """Weights of chain ci's bones at p, and the ease in from the knot."""
        line, chain_names = chains[ci][1], names[ci]
        top, bot = line[0].z, line[-1].z
        s = max(0.0, min(1.0, (top - p.z) / max(1e-6, top - bot)))   # 0 at the knot, 1 at the end
        ease = max(0.0, min(1.0, (s - 0.05) / 0.25))
        seg = min(a.bones - 1, int(s * a.bones))
        t = s * a.bones - seg
        w = {chain_names[seg]: 1.0 - 0.5 * t if seg + 1 < a.bones else 1.0}
        if seg + 1 < a.bones:
            w[chain_names[seg + 1]] = 0.5 * t
        return w, ease * ease * (3 - 2 * ease)

    def line_at(ci, z):
        line = chains[ci][1]
        for j in range(len(line) - 1):
            if line[j + 1].z <= z <= line[j].z or j == len(line) - 2:
                k = (line[j].z - z) / max(1e-6, line[j].z - line[j + 1].z)
                return line[j].lerp(line[j + 1], max(0.0, min(1.0, k)))
    # weights along each chain, eased in below the knot; with sectors, between the two nearest chains of the sheet
    for ci, ((ids, line, side), chain_names) in enumerate(zip(chains, names)):
        mates = [cj for cj, gj in enumerate(chain_group) if gj == chain_group[ci]]
        for i in ids:
            ob, vi = owner[i]
            p = pts[i]
            if len(mates) > 1:
                d = sorted((max(1e-4, (Vector((p.x, p.y)) - line_at(cj, p.z).to_2d()).length), cj) for cj in mates)[:2]
                inv = [(1 / x ** 2, cj) for x, cj in d]
                tot_inv = sum(x for x, _ in inv)
                chain_w, ease = {}, 0.0
                for x, cj in inv:
                    w_, e_ = along(cj, p)
                    for n_, v_ in w_.items():
                        chain_w[n_] = chain_w.get(n_, 0.0) + v_ * x / tot_inv
                    ease += e_ * x / tot_inv
            else:
                chain_w, ease = along(ci, p)
            if ease <= 0:
                continue
            v = ob.data.vertices[vi]
            old = {ob.vertex_groups[g.group].name: g.weight for g in v.groups if g.weight > 0}
            tot = sum(old.values()) or 1.0
            new = {n: w / tot * (1 - ease) for n, w in old.items()}
            for n, w in chain_w.items():
                new[n] = new.get(n, 0.0) + w * ease
            for g in list(v.groups):
                ob.vertex_groups[g.group].remove([vi])
            for n, w in new.items():
                if w <= 1e-4:
                    continue
                vg = ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)
                vg.add([vi], w, "REPLACE")
        report.append({"bones": chain_names, "side": side, "length_cm": round(100 * (line[0].z - line[-1].z), 1),
                       "vertices": len(ids)})
    # studs, rivets, buckles and scraps on the swinging cloth (small islands) ride with the cloth under
    # them: they copy the nearest swinging vertex's weights (they floated off the Skeleton's loincloth)
    report_riders = 0
    swing = {}
    for (ids, _line, _side) in chains:
        for i in ids:
            swing.setdefault(owner[i][0].name, set()).add(owner[i][1])
    same_cloth = {}
    for i in tail_idx:
        same_cloth.setdefault(owner[i][0].name, set()).add(owner[i][1])
    for oname, svs in swing.items():
        ob = bpy.data.objects[oname]
        me = ob.data
        # the swinging cloth's faces, not its vertices: on a low-poly panel a stud can sit mid-face, far from every
        # corner (studs floated off the Skeleton's hip cloth while it swung)
        sf = [p for p in me.polygons if any(v in svs for v in p.vertices)]
        ftree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in sf])
        adj = [[] for _ in me.vertices]
        for e in me.edges:
            a_, b_ = e.vertices
            adj[a_].append(b_)
            adj[b_].append(a_)
        seen = set(svs)
        for st in range(len(me.vertices)):
            if st in seen:
                continue
            grp, stack = [st], [st]
            seen.add(st)
            while stack:
                x = stack.pop()
                for y in adj[x]:
                    if y not in seen:
                        seen.add(y)
                        grp.append(y)
                        stack.append(y)
            if len(grp) >= 60:
                continue
            # scraps of the same cloth ride it from further off than a stud of another colour does
            reach = 0.06 if any(i in same_cloth.get(oname, ()) for i in grp) else 0.02
            gap = reach / ob.matrix_world.to_scale()[0]
            hits = [ftree.find_nearest(me.vertices[i].co) for i in grp]
            hits = [h for h in hits if h[0] is not None]
            if not hits:
                continue
            loc, _n, fi, dist = min(hits, key=lambda h: h[3])
            if dist > gap:
                continue
            def wts(vi):
                return {ob.vertex_groups[g.group].name: g.weight for g in me.vertices[vi].groups}

            def put(i, src):
                for g in list(me.vertices[i].groups):
                    ob.vertex_groups[g.group].remove([i])
                for n_, w in src.items():
                    if w > 1e-4:
                        ob.vertex_groups[n_].add([i], w, "REPLACE")
            if reach > 0.02:
                # a scrap of the cloth bends with the cloth: each of its vertices blends the weights of the cloth
                # face under it (one weight set for the whole scrap left it drifting off as the panel curved)
                for i in grp:
                    l2, _n2, f2, _d2 = ftree.find_nearest(me.vertices[i].co)
                    face = sf[f2]
                    bw = poly_3d_calc([me.vertices[v].co for v in face.vertices], l2)
                    src = {}
                    for v, x in zip(face.vertices, bw):
                        for n_, w in wts(v).items():
                            src[n_] = src.get(n_, 0.0) + w * x
                    tot = sum(src.values()) or 1.0
                    put(i, {n_: w / tot for n_, w in src.items()})
            else:
                face = sf[fi]
                near_v = min((v for v in face.vertices if v in svs), key=lambda v: (me.vertices[v].co - loc).length)
                src = wts(near_v)
                for i in grp:
                    put(i, src)
            report_riders += len(grp)
    bpy.ops.wm.save_mainfile()
    kn.report("Gear dangle bones", {"riders_vertices": report_riders, "belt_bottom_z": round(z_belt, 3), "chains": report})


main()
