"""Seats a generated piece onto a body without crushing it: straps, harness, bands, plates (owner, 24 Oct 2026,
Brambleback saddle: "its not on the face and parts arnt on the body"; the same method is for armor pieces).

  blender -b <scene.blend> --python conform_to_body.py -- --object <piece> --body <obj>[,<obj>...]
          [--islands all|3,7,12] [--mode conform|lift] [--offset 0.012] [--iters 40] [--rigid-under 40] [--dry-run]

Why not a Shrinkwrap modifier: it puts every vertex on the surface, so a 1 cm strap becomes a flat film and its
buckles fold into it. Here each loose piece (island) of --object is handled on its own:
  conform  every vertex gets the move along the body normal that would put it --offset metres outside the body;
           the moves are then smoothed --iters times over the piece's own edges, so the inner and outer faces of a
           strap move almost alike (the strap keeps its thickness, a plate keeps its curve) while a long piece still
           follows the body along its length. Anything left inside is pushed out to 4 mm.
  lift     for straps that run under a chest or belly: each point is raised straight up (+Z) to the underside
           above it, then smoothed the same way (nearest-surface would snap such a strap onto the legs).
Islands under --rigid-under vertices (buckles, rivets, rings) are not conformed: they move whole with the
nearest conformed vertex, so hardware never bends.

Pick the islands first with island_map.py (a numbered picture of every piece). Rigid placement (scale, offset,
pitch) comes before this; this tool only closes the last gap to the body. --body takes several objects (a Sidekick
body is many part meshes) and uses their evaluated shape, so a posed or shape-keyed body works. Saves the blend.
"""
import argparse
import importlib
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, __import__("os").path.dirname(__file__))
import kn_common as kn
importlib.reload(kn)


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--object", required=True)
    p.add_argument("--body", required=True, help="comma-separated body object names")
    p.add_argument("--islands", default="all", help="'all' or comma-separated island ids (island_map.py)")
    p.add_argument("--mode", choices=("conform", "lift"), default="conform")
    p.add_argument("--offset", type=float, default=0.012, help="metres between the piece and the body")
    p.add_argument("--iters", type=int, default=40, help="smoothing passes; more keeps the shape stiffer")
    p.add_argument("--rigid-under", type=int, default=40, help="islands smaller than this ride whole")
    p.add_argument("--dry-run", action="store_true")
    return p.parse_args(kn.tool_args())


def body_tree(names):
    dg = bpy.context.evaluated_depsgraph_get()
    verts, polys = [], []
    for n in names:
        ob = bpy.data.objects[n].evaluated_get(dg)
        me = ob.to_mesh()
        base = len(verts)
        verts += [tuple(ob.matrix_world @ v.co) for v in me.vertices]
        polys += [tuple(base + i for i in p.vertices) for p in me.polygons]
        ob.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys)


def islands(me):
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    out, seen = [], set()
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, ids = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            ids.append(x.index)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        out.append(np.array(ids))
    bm.free()
    return out


def main():
    a = args()
    ob = bpy.data.objects[a.object]
    bvh = body_tree([n.strip() for n in a.body.split(",") if n.strip()])
    me = ob.data
    mw, inv = ob.matrix_world, ob.matrix_world.inverted()
    co = np.array([tuple(mw @ v.co) for v in me.vertices])
    new = co.copy()
    isl = islands(me)
    chosen = range(len(isl)) if a.islands == "all" else [int(i) for i in a.islands.split(",")]
    edges = np.array([tuple(e.vertices) for e in me.edges])

    def signed(q):
        loc, n, _, d = bvh.find_nearest(Vector(q))
        return (d if (Vector(q) - loc).dot(n) >= 0 else -d), n

    moved, small, worst = [], [], 0.0
    for i in chosen:
        vids = isl[i]
        if len(vids) < a.rigid_under:
            small.append(i)
            continue
        disp = np.zeros((len(vids), 3))
        for k, q in enumerate(co[vids]):
            hit = bvh.ray_cast(Vector(q) - Vector((0, 0, 0.3)), Vector((0, 0, 1))) if a.mode == "lift" else (None,)
            if hit[0] is not None and hit[0].z > q[2] - 0.3:
                disp[k] = (0, 0, hit[0].z - a.offset - q[2])
            else:
                d, n = signed(q)
                disp[k] = np.array(n) * (a.offset - d)
        index = {v: k for k, v in enumerate(vids)}
        E = np.array([(index[x], index[y]) for x, y in edges if x in index and y in index])
        for _ in range(a.iters):
            acc, cnt = np.zeros_like(disp), np.zeros(len(disp))
            np.add.at(acc, E[:, 0], disp[E[:, 1]]); np.add.at(acc, E[:, 1], disp[E[:, 0]])
            np.add.at(cnt, E[:, 0], 1); np.add.at(cnt, E[:, 1], 1)
            disp = 0.5 * disp + 0.5 * acc / np.maximum(cnt, 1)[:, None]
        new[vids] = co[vids] + disp
        for v in vids:
            d, n = signed(new[v])
            if d < 0.004:
                new[v] += np.array(n) * (0.004 - d)
        worst = max(worst, float(np.abs(disp).max()))
        moved.append(i)

    # hardware rides whole with the nearest conformed vertex
    if moved:
        mv = np.concatenate([isl[i] for i in moved])
        for i in small:
            c = co[isl[i]].mean(0)
            j = mv[np.argmin(((co[mv] - c) ** 2).sum(1))]
            new[isl[i]] += new[j] - co[j]

    if not a.dry_run:
        for v in me.vertices:
            v.co = inv @ Vector(new[v.index])
        me.update()
        kn.save_blend()
    kn.report("conform_to_body", {"object": a.object, "mode": a.mode, "offset": a.offset,
                                  "islands_conformed": moved, "islands_rigid": small,
                                  "largest_move_m": round(worst, 3), "saved": not a.dry_run})


if __name__ == "__main__":
    main()
