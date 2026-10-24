"""First pass on one gear piece from the owner's Drive: open the holes a body passes through, size it, save it as Revised.

  run_blender.py gear_first_pass.py --fbx "<Drive>/Rares lv.45-75/Elf/Autumn Thread Armor/Autumn_Thread_Armor.fbx"
        [--lineage Elf]              default: the folder above the piece's folder (Civilian, Soldier, Viking, Elf, Robot, Skeleton)
        [--slot armor|gloves|boots|cape|helmet]   default: read from the file and folder name
        [--sheet review.png]         before / after pictures on the lineage's bare body
        [--dry-run]                  find and report, write nothing

What it does (owner's rules, 22 Oct 2026; second pass, the fit, happens once a whole set is in):
1. Imports the textured model and puts it upright, facing -Y like Synty's parts.
2. Finds every opening a body part passes through for that slot (armor: the neck, both sleeve ends, both leg
   ends; gloves: each cuff; boots: each boot top; helmet: where the head goes in; a cape has none). An opening that
   is already open is left as it is. One that the model closed with a cap has the cap's faces removed, then any
   vertex left without a face. Robots keep their caps: Synty's robot parts are the chassis and every one of them is
   closed, its ends hidden inside the next part. Skeleton pieces are opened like humanoid ones; the second pass adds
   the bones of that body area into the piece, as Synty's skeleton outfits carry them.
3. Sizes the piece uniformly against the lineage's bare body (masculine, the creator's neutral muscle) by the slot's
   own measure, and stands it where that body part is. The pose stays as modelled (often arms down): the T-pose fit
   is the second pass.
4. Writes <name>_Revised.fbx beside the original, texture embedded, the mesh named after the piece.
"""
import argparse
import math
import re
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

BARE = {"Civilian": ("HUMN_BASE_01", "HU01"), "Soldier": ("HUMN_BASE_01", "HU01"), "Viking": ("HUMN_BASE_01", "HU01"),
        "Elf": ("ELVN_BASE_01", "EV01"), "Skeleton": ("SKTN_BASE_01", "SN01"), "Robot": ("SCFI_ROBO_04", "RO01")}
HEADS = {"Civilian": "SK_HUMN_BASE_01_01HEAD_HU01", "Soldier": "SK_HUMN_BASE_01_01HEAD_HU01",
         "Viking": "SK_HUMN_BASE_01_01HEAD_HU01", "Elf": "SK_ELVN_BASE_01_01HEAD_EV01",
         "Skeleton": "SK_SKTN_BASE_01_01HEAD_SN01", "Robot": "SK_ROBO_BASE_15_01HEAD_RO01"}
SLOT_WORDS = [("helmet", r"helmet|helm|hood|cap\b"), ("gloves", r"glove|gauntlet"), ("boots", r"boot|greave"),
              ("cape", r"cape|mantle|cloak"), ("armor", r"armou?r|chest|legs|suit")]
BODY_SLOTS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR",
              "20FOTL", "21FOTR"]


def guess_slot(path):
    text = (path.stem + " " + path.parent.name).lower().replace("_", " ")
    for slot, pat in SLOT_WORDS:
        if re.search(pat, text):
            return slot
    raise SystemExit(f"Cannot tell the slot of '{path.name}'. Pass --slot armor|gloves|boots|cape|helmet.")


def import_body(lineage):
    """The lineage's bare body (and head) at the masculine neutral shape, as world-space bounding boxes per slot."""
    pack, sp = BARE[lineage]
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}
    boxes, objects = {}, []
    for stem in [f"SK_{pack}_{s}_{sp}" for s in BODY_SLOTS] + [HEADS[lineage]]:
        if stem not in index:
            continue
        new = kn.import_fbx(index[stem])
        for o in new:
            if o.type == "MESH":
                ks = o.data.shape_keys
                if ks:
                    for kb in ks.key_blocks[1:]:
                        kb.value = 0.5 if kb.name == "defaultBuff" else 0.0
                objects.append(o)
                o["kn_body"] = stem.split("_")[4]
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objects:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        pts = [o.matrix_world @ v.co for v in me.vertices]
        ev.to_mesh_clear()
        lo = Vector((min(p[i] for p in pts) for i in range(3)))
        hi = Vector((max(p[i] for p in pts) for i in range(3)))
        boxes[o["kn_body"]] = (lo, hi)
        boxes["pts_" + o["kn_body"]] = pts
        me2 = o.evaluated_get(dg).to_mesh()
        me2.calc_loop_triangles()
        boxes["tris_" + o["kn_body"]] = [tuple(lt.vertices) for lt in me2.loop_triangles]
        o.evaluated_get(dg).to_mesh_clear()
    return boxes, objects


def band_reach(points, z_lo, z_hi, cx, cy, half_x=None):
    """How far the points reach from (cx, cy) between two heights: left, right, front, back (metres).
    A high percentile, not the extreme, so one stray strap does not decide it."""
    sel = [p for p in points if z_lo <= p.z <= z_hi and (half_x is None or abs(p.x - cx) < half_x)]
    if len(sel) < 6:
        return None

    def reach(vals):
        vals = sorted(vals)
        return vals[int(0.97 * (len(vals) - 1))]
    return (reach([p.x - cx for p in sel]), reach([cx - p.x for p in sel]),
            reach([cy - p.y for p in sel]), reach([p.y - cy for p in sel]))


def loops_of(bm):
    left = {e for e in bm.edges if e.is_boundary}
    out = []
    while left:
        e = left.pop()
        grp, stack = [e], [e]
        while stack:
            x = stack.pop()
            for v in x.verts:
                for y in v.link_edges:
                    if y in left:
                        left.discard(y)
                        grp.append(y)
                        stack.append(y)
        vs = {v for e in grp for v in e.verts}
        c = sum((v.co for v in vs), Vector()) / len(vs)
        inner = [f.calc_center_median() for e in grp for f in e.link_faces]
        away = (c - sum(inner, Vector()) / len(inner)) if inner else Vector((0, 0, 1))
        out.append({"centre": c, "radius": max((v.co - c).length for v in vs), "edges": len(grp),
                    "normal": away.normalized() if away.length > 1e-9 else Vector((0, 0, 1))})
    return out


def cap_candidates(bm, height):
    """Flat, roughly round patches whose whole edge folds sharply into walls: how a sealed opening looks.
    A sash end or a plate is long and thin, or its edge runs on smoothly, so it does not qualify."""
    seen, out = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        n0 = f.normal.copy()
        grp, stack = {f}, [f]
        seen.add(f)
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h not in grp and h.normal.angle(n0, 9) < math.radians(20):
                        grp.add(h)
                        stack.append(h)
                        seen.add(h)
        rim = [e for g in grp for e in g.edges
               if e.is_boundary or any(h not in grp for h in e.link_faces)]
        if not rim:
            continue
        sharp = sum(1 for e in rim if e.is_boundary or e.calc_face_angle(0) > math.radians(45)) / len(rim)
        area = sum(g.calc_area() for g in grp)
        perim = sum(e.calc_length() for e in rim)
        if perim <= 0:
            continue
        round_ = 4 * math.pi * area / (perim * perim)
        r_eq = math.sqrt(area / math.pi)
        if sharp >= 0.75 and round_ > 0.45 and 0.012 * height < r_eq < 0.12 * height:
            c = sum((g.calc_center_median() * g.calc_area() for g in grp), Vector()) / area
            nrm = sum((g.normal * g.calc_area() for g in grp), Vector()).normalized()
            out.append({"faces": list(grp), "centre": c, "normal": nrm, "radius": r_eq, "round": round_})
    return out


def trunk_centre_y(points, z_lo, z_hi, half_x):
    """Middle of the trunk's front and back (97th percentiles), so a back pack or a front flap does not move it."""
    sel = [p.y for p in points if z_lo <= p.z <= z_hi and abs(p.x) < half_x]
    if len(sel) < 6:
        return None
    sel.sort()
    return (sel[int(0.03 * (len(sel) - 1))] + sel[int(0.97 * (len(sel) - 1))]) / 2


def crotch_height(tris_bvh, x, y, z_from, z_to):
    """Lowest surface met going up the centre line from below: the underside of the crotch."""
    from mathutils import Vector as V
    hit = tris_bvh.ray_cast(V((x, y, z_from)), V((0, 0, 1)), z_to - z_from)
    return hit[0].z if hit and hit[0] is not None else None


def cap_by_looking_in(bm, start, direction, reach, out_dot=0.5):
    """Look into an opening from outside along `direction` (pointing into the piece). The first surface met, if it
    faces back out of the opening, seals it: grow from there over faces that also face out, within `reach` of where
    the look started. Flat, domed and recessed caps all end up here."""
    from mathutils.bvhtree import BVHTree
    faces = list(bm.faces)
    tree = BVHTree.FromBMesh(bm)
    loc, nrm, idx, dist = tree.ray_cast(start, direction, reach)
    if loc is None:
        return None
    bm.faces.ensure_lookup_table()
    seed = bm.faces[idx]
    out = -direction
    if seed.normal.dot(out) < out_dot:
        return None
    grown, stack = {seed}, [seed]
    while stack:
        f = stack.pop()
        for e in f.edges:
            for g in e.link_faces:
                if g in grown or g.normal.dot(out) < out_dot:
                    continue
                c = g.calc_center_median()
                if (c - start).dot(direction) > reach:
                    continue
                grown.add(g)
                stack.append(g)
    c = sum((f.calc_center_median() for f in grown), Vector()) / len(grown)
    area = sum(f.calc_area() for f in grown)
    return {"faces": list(grown), "centre": c, "normal": out, "radius": math.sqrt(area / math.pi), "round": 0}


def leg_core(bm, height, side, depth=0.07, inner=0.8):
    """The inside of a leg tube's end: faces in the bottom `depth` of the piece on that side whose centre lies within
    `inner` of the tube's radius from its axis (the wall is outside that, pockets further out still)."""
    # A face is part of the seal when the leg's tube surrounds it: rays fired level from it meet the piece in nearly
    # every direction. A wall face's outward rays meet nothing. (An axis guessed from the bottom points drifted out
    # towards the pockets and left half the dish.)
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromBMesh(bm)
    zs = [v.co.z for v in bm.verts]
    z0 = min(zs)
    dirs = [Vector((math.cos(a), math.sin(a), 0.0)) for a in [i * math.pi / 6 for i in range(12)]]
    faces, cs = [], []
    for f in bm.faces:
        c = f.calc_center_median()
        if c.z > z0 + depth * height or c.x * side <= 0:
            continue
        hits = sum(1 for d in dirs if tree.ray_cast(c + d * 0.002 * height, d, 0.25 * height)[0] is not None)
        if hits >= 11:
            faces.append(f)
            cs.append(c)
    if not faces:
        return None
    centre = sum(cs, Vector()) / len(cs)
    R = max(math.hypot(c.x - centre.x, c.y - centre.y) for c in cs)
    return {"faces": faces, "centre": centre, "normal": Vector((0, 0, -1)), "radius": R, "round": 0}


def boot_floor(bm, cap, deep=0.06, near=0.015):
    """The whole seal of a boot's shaft, from the part of it that looking in found: every face that looks up, lies
    inside the shaft (within 97 % of how far the shaft's top reaches from the seal's centre, direction by direction)
    and less than `deep` metres below the seal's top, and touches the seal found so far (a shared corner, or a centre
    within `near` metres: Tripo's lids are not joined edge to edge, and the look found 4 of their faces)."""
    c0 = cap["centre"]
    side = 1 if c0.x >= 0 else -1
    ztop = max(f.calc_center_median().z for f in cap["faces"]) + 0.01
    n = 24
    reach = [0.0] * n

    def sector(p):
        return int(math.floor((math.atan2(p.y - c0.y, p.x - c0.x) + math.pi) / (2 * math.pi / n))) % n
    for v in bm.verts:
        if v.co.z > ztop - 0.05 and v.co.x * side > 0:
            k = sector(v.co)
            reach[k] = max(reach[k], math.hypot(v.co.x - c0.x, v.co.y - c0.y))
    seen = [r for r in reach if r > 0]
    fill = sorted(seen)[len(seen) // 2] if seen else 0.0
    reach = [r if r > 0 else fill for r in reach]

    def inside(c):
        return math.hypot(c.x - c0.x, c.y - c0.y) < 0.97 * reach[sector(c)]
    grown = set(cap["faces"])
    verts = {v for f in grown for v in f.verts}
    cand = [f for f in bm.faces if f not in grown and f.normal.z > 0.3
            and ztop - deep < f.calc_center_median().z < ztop and inside(f.calc_center_median())]
    changed = True
    while changed:
        changed = False
        cs = [f.calc_center_median() for f in grown]
        for f in cand:
            if f in grown:
                continue
            c = f.calc_center_median()
            if any(v in verts for v in f.verts) or any((c - g).length < near for g in cs):
                grown.add(f)
                verts.update(f.verts)
                changed = True
    # slivers standing between the lid's own corners (they crossed the opened top like threads) go with it
    grown |= {f for f in bm.faces if all(v in verts for v in f.verts)}
    return dict(cap, faces=list(grown))


def look_into(name, bm, height):
    """(start, direction into the piece, reach) for openings that run up or down: the neck (looking down the
    collar, deep enough to reach a floor inside the chest), leg ends, boot tops and cuffs (looking along the column)."""
    from mathutils import Vector as V
    zs = [v.co.z for v in bm.verts]
    z0, z1 = min(zs), max(zs)
    if name == "neck":
        top = [v.co for v in bm.verts if v.co.z > z1 - 0.12 * height and abs(v.co.x) < 0.15 * height]
        if not top:
            return None
        cy = sum(p.y for p in top) / len(top)
        return V((0.0, cy, z1 + 0.05)), V((0, 0, -1)), 0.45 * height
    side = 1 if name.startswith("left") else -1
    if "leg end" in name:
        col = [v.co for v in bm.verts if v.co.z < z0 + 0.1 * height and v.co.x * side > 0]
        if not col:
            return None
        cx, cy = sum(p.x for p in col) / len(col), sum(p.y for p in col) / len(col)
        return V((cx, cy, z0 - 0.05)), V((0, 0, 1)), 0.15 * height
    if "cuff" in name or "boot top" in name:
        col = [v.co for v in bm.verts if v.co.z > z1 - 0.1 * height and (v.co.x * side > 0 or side == 0)]
        if not col:
            return None
        cx, cy = sum(p.x for p in col) / len(col), sum(p.y for p in col) / len(col)
        # the look starts 5 cm above the top: on a 27 cm boot 20 % of the height alone never reached its seal
        return V((cx, cy, z1 + 0.05)), V((0, 0, -1)), 0.05 + 0.2 * height
    return None


def tpose_gloves(bm):
    """A pair of gloves modelled on the T-posed body lies along the arms. Each glove is turned about the body's
    centre to hang fingers down (the left one by +90 degrees about Y, the right by -90), so the openings and the
    health checks see a glove as they always have: cuff at the top. True when the pair lay in T-pose."""
    xs = [v.co.x for v in bm.verts]
    zs = [v.co.z for v in bm.verts]
    if max(xs) - min(xs) < 2.5 * (max(zs) - min(zs)):
        return False
    turn_gloves(bm, back=False)
    return True


def turn_gloves(bm, back):
    for v in bm.verts:
        x, z = v.co.x, v.co.z
        if not back:
            v.co.x, v.co.z = (z, -x) if x >= 0 else (-z, x)
        else:
            v.co.x, v.co.z = (-z, x) if x >= 0 else (z, -x)
    bm.normal_update()


def fill_peepholes(ob, body, max_r=0.03, reach=0.06):
    """Small holes the body shows through (owner's review, 23 Oct 2026: light slivers on the back of Dock Runner's boot
    shafts): an open loop under `max_r` metres across whose nearest skin lies within `reach` straight behind it, with
    nothing of the piece in between, is filled. A strap's open end or a buckle's hole has the piece behind it and
    stays. Returns how many holes were filled."""
    from mathutils.bvhtree import BVHTree
    bv, bt = [], []
    for o in body:
        base = len(bv)
        bv.extend(o.matrix_world @ v.co for v in o.data.vertices)
        o.data.calc_loop_triangles()
        bt.extend(tuple(base + i for i in t.vertices) for t in o.data.loop_triangles)
    btree = BVHTree.FromPolygons(bv, bt)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    ptree = BVHTree.FromBMesh(bm)
    left = {e for e in bm.edges if e.is_boundary}
    holes = []
    while left:
        e = left.pop()
        grp, stack = [e], [e]
        while stack:
            x = stack.pop()
            for v in x.verts:
                for y in v.link_edges:
                    if y in left:
                        left.discard(y)
                        grp.append(y)
                        stack.append(y)
        vs = {v for e in grp for v in e.verts}
        c = sum((v.co for v in vs), Vector()) / len(vs)
        if max((v.co - c).length for v in vs) >= max_r:
            continue
        loc, _n, _i, d = btree.find_nearest(c)
        if loc is None or d > reach or d < 1e-5:
            continue
        ray = (loc - c).normalized()
        hit = ptree.ray_cast(c + ray * 0.001, ray, d)
        if hit[0] is None:
            holes.append(grp)
    filled = 0
    for grp in holes:
        # a hole with a corner shared by more than two open edges is not a clean ring and holes_fill leaves it open
        # (the 1.7 cm hole down the back of Dock Runner's boot shaft): fill the edge net with triangles instead
        made = bmesh.ops.holes_fill(bm, edges=grp, sides=0)["faces"]
        if not made:
            made = bmesh.ops.triangle_fill(bm, edges=grp, use_beauty=True)["geom"]
        filled += bool(made)
    bm.normal_update()
    bm.to_mesh(ob.data)
    ob.data.update()
    bm.free()
    return filled


def zip_cracks(ob, gap=0.006, apart=5):
    """Seams Tripo left open (owner's review, 23 Oct 2026: light slivers down the back of Dock Runner's boot shafts,
    the leg showing through): two points on open edges within `gap` metres of each other but more than `apart` edges
    apart along those edges are welded at their middle, closing the crack. Returns how many points were welded."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    rim = [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
    from mathutils.kdtree import KDTree
    kd = KDTree(len(rim))
    for i, v in enumerate(rim):
        kd.insert(v.co, i)
    kd.balance()

    def hops(a, b):
        """Steps from a to b along open edges, up to `apart`."""
        seen, front = {a}, [a]
        for k in range(apart):
            nxt = []
            for v in front:
                for e in v.link_edges:
                    if e.is_boundary:
                        o = e.other_vert(v)
                        if o is b:
                            return k + 1
                        if o not in seen:
                            seen.add(o)
                            nxt.append(o)
            front = nxt
        return apart + 1
    target, used = {}, set()
    for i, v in enumerate(rim):
        if v in used:
            continue
        for _co, j, d in sorted(kd.find_range(v.co, gap), key=lambda h: h[2]):
            w = rim[j]
            if w is v or w in used or hops(v, w) <= apart:
                continue
            mid = (v.co + w.co) / 2
            v.co = mid
            target[w] = v
            used.update((v, w))
            break
    if target:
        bmesh.ops.weld_verts(bm, targetmap=target)
    bm.to_mesh(ob.data)
    ob.data.update()
    bm.free()
    return len(target)


def coverage(piece_ob, pts, near=0.03):
    """Share of the body points with the piece's surface within `near` metres: how much of that body part the
    piece as modelled already covers."""
    from mathutils.bvhtree import BVHTree
    me = piece_ob.data
    tree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    if not pts:
        return 0.0
    return sum(1 for p in pts if (tree.find_nearest(p)[3] or 1.0) < near) / len(pts)


def mesh_health(bm, height):
    """Find and mend breakage that wrecks a piece once it is skinned and moves (owner, 22 Oct 2026). Returns
    (what was fixed, what needs a look). Texture is kept: UVs live on the face corners, so welding keeps them."""
    from mathutils.bvhtree import BVHTree
    fixed, look = {}, []
    # Points split apart along texture seams tear open when the piece bends: weld what sits on the same spot.
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0001 * height)
    if before - len(bm.verts):
        fixed["welded_split_points"] = before - len(bm.verts)
    # Faces with no area, and the same face twice.
    flat = [f for f in bm.faces if f.calc_area() < 1e-10 * height * height]
    if flat:
        bmesh.ops.delete(bm, geom=flat, context="FACES_ONLY")
        fixed["zero_area_faces"] = len(flat)
    seen, twins = {}, []
    for f in bm.faces:
        key = tuple(sorted(v.index for v in f.verts))
        if key in seen:
            twins.append(f)
        else:
            seen[key] = f
    if twins:
        bmesh.ops.delete(bm, geom=twins, context="FACES_ONLY")
        fixed["duplicate_faces"] = len(twins)
    # Points and edges that belong to no face.
    loose_e = [e for e in bm.edges if not e.link_faces]
    if loose_e:
        bmesh.ops.delete(bm, geom=loose_e, context="EDGES")
    loose_v = [v for v in bm.verts if not v.link_faces]
    if loose_v:
        bmesh.ops.delete(bm, geom=loose_v, context="VERTS")
    if loose_e or loose_v:
        fixed["loose_points_and_edges"] = len(loose_e) + len(loose_v)
    # Islands: tiny floating bits go; a whole island facing inward is turned the right way.
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    islands, seen_f = [], set()
    for f in bm.faces:
        if f in seen_f:
            continue
        grp, stack = [f], [f]
        seen_f.add(f)
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen_f:
                        seen_f.add(h)
                        grp.append(h)
                        stack.append(h)
        islands.append(grp)
    crumbs = []
    for grp in islands:
        pts = [v.co for f in grp for v in f.verts]
        span = max((max(p[i] for p in pts) - min(p[i] for p in pts)) for i in range(3))
        if len(grp) < 6 and span < 0.003 * height:        # a few millimetres: studs and rivets are bigger and stay
            crumbs.extend(grp)
    if crumbs:
        bmesh.ops.delete(bm, geom=crumbs, context="FACES")
        fixed["floating_crumbs_faces"] = len(crumbs)
    # Inward-facing islands: most of an island's faces see more of the piece in front of them than behind.
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    flipped = 0
    seen_f = set()
    for f in bm.faces:
        if f in seen_f:
            continue
        grp, stack = [f], [f]
        seen_f.add(f)
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen_f:
                        seen_f.add(h)
                        grp.append(h)
                        stack.append(h)
        sample = grp[:: max(1, len(grp) // 40)]
        inward = 0
        for g in sample:
            c = g.calc_center_median() + g.normal * 0.0005 * height
            hit = tree.ray_cast(c, g.normal, 2 * height)
            back = tree.ray_cast(g.calc_center_median() - g.normal * 0.0005 * height, -g.normal, 2 * height)
            if hit[0] is not None and back[0] is None:
                inward += 1
        if len(sample) >= 6 and inward > 0.8 * len(sample):
            bmesh.ops.reverse_faces(bm, faces=grp)
            flipped += len(grp)
    if flipped:
        fixed["inward_faces_turned_round"] = flipped
    # Edges shared by three faces or more: report only.
    many = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    if many:
        look.append(f"{many} edges are shared by three faces or more (fins or doubled walls); check them on the sheet.")
    return fixed, look


def openings(slot, bm, height):
    """For each opening the slot has: (name, place(centre) -> bool, facing(normal) -> bool).
    An open loop counts by place alone (which way a rim's faces run says nothing about the opening);
    a sealed cap must also face out of the opening."""
    zs = [v.co.z for v in bm.verts]
    z0 = min(zs)
    half = max(abs(v.co.x) for v in bm.verts)
    h = lambda c: (c.z - z0) / height
    up = lambda n: n.z > 0.6
    down = lambda n: n.z < -0.6
    if slot == "armor":
        return [("neck", lambda c: h(c) > 0.82 and abs(c.x) < 0.35 * half, up),
                ("left sleeve", lambda c: c.x > 0.45 * half and 0.3 < h(c) < 0.95, lambda n: n.x > 0.25 and n.z < 0.6),
                ("right sleeve", lambda c: c.x < -0.45 * half and 0.3 < h(c) < 0.95, lambda n: n.x < -0.25 and n.z < 0.6),
                ("left leg end", lambda c: c.x > 0 and h(c) < 0.12, down),
                ("right leg end", lambda c: c.x < 0 and h(c) < 0.12, down)]
    # a cuff or a boot top is the piece's top end: a strap's ring lower down the shaft (Dock Runner's boots, 3 cm
    # across at 80 % of the height) is not it, and once taken for it the real, sealed top was left closed
    if slot == "gloves":
        return [("left cuff", lambda c: c.x >= 0 and h(c) > 0.85, up), ("right cuff", lambda c: c.x < 0 and h(c) > 0.85, up)]
    if slot == "boots":
        return [("left boot top", lambda c: c.x >= 0 and h(c) > 0.85, up),
                ("right boot top", lambda c: c.x < 0 and h(c) > 0.85, up)]
    if slot == "helmet":
        return [("head opening", lambda c: h(c) < 0.35, down)]
    return []


def loop_direction(bm, lp):
    """Which way an open loop faces: away from the faces next to it."""
    return lp["normal"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fbx", required=True)
    ap.add_argument("--lineage")
    ap.add_argument("--slot")
    ap.add_argument("--sheet")
    ap.add_argument("--name", help="the piece's name, e.g. DockRunner_Armor (default: its folder's name)")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(kn.tool_args())
    src = Path(a.fbx)
    lineage = a.lineage or src.parent.parent.name
    if lineage not in BARE:
        raise SystemExit(f"Lineage '{lineage}' unknown; pass --lineage one of {sorted(BARE)}.")
    slot = a.slot or guess_slot(src)
    piece = a.name or re.sub(r"\s+", "_", src.parent.name.strip()) or src.stem   # the piece is named after its folder (Tripo names files generically)
    problems, warnings, done = [], [], []

    bpy.ops.wm.read_factory_settings(use_empty=True)
    new = kn.import_fbx(src)
    meshes = [o for o in new if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No mesh in the file.")
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o)
    if len(meshes) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for o in meshes:
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
    ob = meshes[0]
    ob.name = ob.data.name = piece
    ob.data.transform(ob.matrix_world)          # upright, in metres, transforms applied
    ob.matrix_world = Matrix.Identity(4)

    bm = bmesh.new()
    bm.from_mesh(ob.data)
    hung = slot == "gloves" and tpose_gloves(bm)       # T-posed gloves hang fingers down while their cuffs are found
    zs = [v.co.z for v in bm.verts]
    height = max(zs) - min(zs)
    health, health_look = mesh_health(bm, height)
    warnings.extend(health_look)
    before_loops = loops_of(bm)
    tri_before = sum(len(f.verts) - 2 for f in bm.faces)

    # 2. Openings: open loops count as open; sealed ones are cap patches.
    caps = cap_candidates(bm, height)
    used = set()
    for name, place, facing in openings(slot, bm, height):
        smallest = (0.04 if name in ("neck", "head opening") else 0.025) * height   # a strap loop is not the neck
        open_loops = [lp for lp in before_loops if smallest < lp["radius"] < 0.15 * height and place(lp["centre"])]
        if open_loops:
            big = max(open_loops, key=lambda lp: lp["radius"])
            done.append({"opening": name, "state": "already open", "radius_cm": round(100 * big["radius"], 1)})
            continue
        # a cuff or a boot top seal is limb-sized: a buckle's flat top facing up (1 cm, on Dock Runner's boots) is not
        limb = 0.02 if ("cuff" in name or "boot top" in name) else 0.0
        found = [c for c in caps if id(c) not in used and place(c["centre"]) and facing(c["normal"]) and c["radius"] >= limb]
        cap = max(found, key=lambda c: c["radius"]) if found else None
        if cap is None:
            look = look_into(name, bm, height)
            if look:
                cap = cap_by_looking_in(bm, *look)
                if cap and not (max(limb, 0.012 * height) < cap["radius"] < 0.15 * height):
                    cap = None
        if "leg end" in name and (cap is None or cap["radius"] < 0.04 * height):
            # A recessed dish with a knob in it (Tripo's T-pose shorts, 23 Oct 2026): looking in only finds the knob.
            # Everything inside the leg tube's wall, in the bottom of the leg, is the seal.
            core = leg_core(bm, height, 1 if name.startswith("left") else -1)
            if core:
                cap = core
        if "boot top" in name and cap is not None:
            # Dock Runner's boots are sealed by a funnel sunk into the shaft: looking in finds only its bottom, and the
            # rest stood round the calf as a floor. The whole floor goes.
            cap = boot_floor(bm, cap)
        if cap is None:
            warnings.append(f"{name}: neither open nor a sealing surface found; look at the sheet.")
            continue
        used.add(id(cap))
        if lineage == "Robot":
            done.append({"opening": name, "state": "kept closed (robot: Synty keeps robot parts closed)",
                         "radius_cm": round(100 * cap["radius"], 1)})
            continue
        bmesh.ops.delete(bm, geom=cap["faces"], context="FACES_ONLY")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        loose_e = [e for e in bm.edges if not e.link_faces]
        bmesh.ops.delete(bm, geom=loose_e, context="EDGES")
        if "leg end" in name:
            # the knob that stood in the dish is left floating: any small loose piece inside the opening goes too
            bm.faces.ensure_lookup_table()
            seen_f, knobs = set(), []
            for f in bm.faces:
                if f in seen_f:
                    continue
                grp, st = [f], [f]
                seen_f.add(f)
                while st:
                    x = st.pop()
                    for e in x.edges:
                        for g2 in e.link_faces:
                            if g2 not in seen_f:
                                seen_f.add(g2)
                                grp.append(g2)
                                st.append(g2)
                if len(grp) > 120:
                    continue
                c = sum((g2.calc_center_median() for g2 in grp), Vector()) / len(grp)
                if math.hypot(c.x - cap["centre"].x, c.y - cap["centre"].y) < 0.8 * cap["radius"] \
                        and c.z < cap["centre"].z + 0.05 * height:
                    knobs.extend(grp)
            if knobs:
                bmesh.ops.delete(bm, geom=knobs, context="FACES")
        caps = [c for c in caps if c is cap or all(f.is_valid for f in c["faces"])]
        done.append({"opening": name, "state": "opened", "faces_removed": len(cap["faces"]),
                     "verts_removed": len(loose), "radius_cm": round(100 * cap["radius"], 1),
                     "where": [round(x, 3) for x in cap["centre"]]})
    if hung:
        turn_gloves(bm, back=True)
    bm.to_mesh(ob.data)
    ob.data.update()
    bm.free()

    # 3. Size and place against the lineage's bare body.
    boxes, body = import_body(lineage)
    pts = [v.co for v in ob.data.vertices]
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    size = hi - lo
    T = boxes.get("10TORS")
    if slot == "armor":
        # Torso length, collar to crotch: every armor has both, whether it ends in shorts, trousers or a skirt.
        from mathutils.bvhtree import BVHTree
        hips = boxes["17HIPS"]
        body_tree = BVHTree.FromPolygons(boxes["pts_17HIPS"], boxes["tris_17HIPS"])
        bcy = (hips[0].y + hips[1].y) / 2
        b_crotch = crotch_height(body_tree, 0.0, bcy, hips[0].z - 0.05, hips[1].z)
        top = T[1].z
        mtree = BVHTree.FromPolygons([v.co for v in ob.data.vertices], [tuple(p.vertices) for p in ob.data.polygons])
        mcy = trunk_centre_y(pts, lo.z + 0.55 * size.z, lo.z + 0.8 * size.z, 0.2 * size.x)
        m_crotch = crotch_height(mtree, (lo.x + hi.x) / 2, mcy if mcy is not None else (lo.y + hi.y) / 2,
                                 lo.z - 0.05, hi.z)
        if b_crotch is None or m_crotch is None or hi.z - m_crotch < 0.2 * size.z:
            warnings.append("No crotch found on the piece or the body; sized collar to ankle instead.")
            leg = boxes.get("18LEGL") or hips
            scale = (top - leg[0].z) / size.z
            anchor_model = Vector(((lo.x + hi.x) / 2, mcy if mcy is not None else (lo.y + hi.y) / 2, lo.z))
            anchor_body = Vector(((T[0].x + T[1].x) / 2, bcy, leg[0].z))
            measure = "collar to ankle (no crotch found)"
        else:
            scale = (top - b_crotch) / (hi.z - m_crotch)
            anchor_model = Vector(((lo.x + hi.x) / 2, mcy if mcy is not None else (lo.y + hi.y) / 2, m_crotch))
            tcy = trunk_centre_y(boxes["pts_10TORS"], T[0].z + 0.45 * (T[1].z - T[0].z), T[0].z + 0.85 * (T[1].z - T[0].z),
                                 0.5 * (T[1].x - T[0].x) * 0.75)
            anchor_body = Vector(((T[0].x + T[1].x) / 2, tcy if tcy is not None else bcy, b_crotch))
            measure = f"collar to crotch = body neck to crotch ({100 * (top - b_crotch):.1f} cm); legs end where modelled"
    elif slot == "boots":
        foot = boxes["20FOTL"]
        scale = (foot[1].y - foot[0].y) * 1.08 / size.y
        anchor_model = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        anchor_body = Vector((0.0, (foot[0].y + foot[1].y) / 2, 0.0))
        measure = f"sole length = foot length + 8 % ({100 * (foot[1].y - foot[0].y) * 1.08:.1f} cm)"
    elif slot == "gloves" and hung:
        # T-pose (owner, 23 Oct 2026: all new gear is): the pair lies along the arms, so its fingertips go to the
        # body's, one scale about the centre line, level with the hands; a glove's height is its hand's thickness
        hand = boxes["15HNDL"]
        reach = max(hi.x, -lo.x)
        scale = hand[1].x / reach
        anchor_model = Vector((0.0, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2))
        anchor_body = Vector((0.0, (hand[0].y + hand[1].y) / 2, (hand[0].z + hand[1].z) / 2))
        measure = f"fingertip reach = the hands' ({100 * hand[1].x:.1f} cm from the centre line), T-pose"
    elif slot == "gloves":
        hand = boxes["15HNDL"]
        hand_len = hand[1].x - hand[0].x
        scale = hand_len * 1.5 / size.z
        anchor_model = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2))
        anchor_body = Vector((0.0, (hand[0].y + hand[1].y) / 2, (hand[0].z + hand[1].z) / 2))
        measure = f"glove length = 1.5 x hand length ({100 * hand_len * 1.5:.1f} cm); placed between the hands"
    elif slot == "helmet":
        head = boxes[HEADS[lineage].split("_")[4]]
        scale = (head[1].x - head[0].x) * 1.18 / size.x
        anchor_model = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2))
        anchor_body = (head[0] + head[1]) / 2
        measure = f"width = head width + 18 % ({100 * (head[1].x - head[0].x) * 1.18:.1f} cm)"
    else:                                   # cape: shoulder to knee down the back
        hips = boxes["17HIPS"]
        scale = (T[1].z - (hips[0].z + 0.1 * (hips[1].z - hips[0].z))) / size.z
        anchor_model = Vector(((lo.x + hi.x) / 2, lo.y, hi.z))
        anchor_body = Vector(((T[0].x + T[1].x) / 2, T[1].y, T[1].z))
        measure = "shoulder to knee down the back"
        # A cape whose collar or hood wraps the neck (Dock Runner's rain cape) has its front at the collar: put with
        # its front on the torso's back it hung 30 cm behind the body. Its top goes round the torso's top instead.
        top = [p for p in pts if p.z > hi.z - 0.12 * size.z]
        neck = [p.y for p in boxes["pts_10TORS"] if p.z > T[1].z - 0.06]
        if top and neck:
            mid = (min(neck) + max(neck)) / 2
            if min(p.y for p in top) < mid - 0.02 and max(p.y for p in top) > mid + 0.02:
                anchor_model = Vector(((lo.x + hi.x) / 2, (min(p.y for p in top) + max(p.y for p in top)) / 2, hi.z))
                anchor_body = Vector(((T[0].x + T[1].x) / 2, mid, T[1].z))
                measure += "; its collar wraps the neck, so its top goes round the torso's top"
    # Modelled on the lineage's bare body already (the owner places the T-pose pieces on the mannequin): when the piece
    # covers that body part as it stands, it is kept as modelled. Dock Runner's boots, x0.89 by the sole rule, left the
    # toes through the sole.
    kept = covers = None
    if slot in ("gloves", "boots", "cape", "helmet"):
        if slot == "cape":
            mid_y = (T[0].y + T[1].y) / 2
            want, near = [p for p in boxes["pts_10TORS"] if p.y > mid_y and p.z > (T[0].z + T[1].z) / 2], 0.06
        elif slot == "helmet":
            tag = HEADS[lineage].split("_")[4]
            hb = boxes[tag]
            # the crown; a padded cap stands well off it (Dock Runner's pilot cap: 68 % within 4 cm), and the width
            # rule, ear cups and all, shrank that one x0.71 onto the skull
            want, near = [p for p in boxes["pts_" + tag] if p.z > (hb[0].z + hb[1].z) / 2], 0.06
        else:
            pair = ("15HNDL", "16HNDR") if slot == "gloves" else ("20FOTL", "21FOTR")
            want, near = boxes["pts_" + pair[0]] + boxes["pts_" + pair[1]], 0.03
        cov = coverage(ob, want, near)
        covers = round(cov, 2)
        # a helmet's width rule counts ear cups and visors, so a helmet sitting on the head is trusted over it
        if cov >= 0.8 and (0.8 < scale < 1.25 or (slot == "helmet" and cov >= 0.9)):
            kept = f"kept as modelled on the bare body (it covers {100 * cov:.0f} % of it; the rule gives x{scale:.3f}: {measure})"
            scale, measure = 1.0, kept
            anchor_model = anchor_body = Vector()
    M = Matrix.Translation(anchor_body) @ Matrix.Scale(scale, 4) @ Matrix.Translation(-anchor_model)
    ob.data.transform(M)
    ob.data.update()
    girth = None
    if slot == "armor":
        # Tripo's figures are slimmer than Synty's bodies: widen (never narrow) at the chest and hips so the
        # body is covered with a few per cent to spare. Height and the look stay; the real fit is the second pass.
        tz0, tz1 = T[0].z, T[1].z
        hips = boxes.get("17HIPS")
        bands = [(tz0 + 0.45 * (tz1 - tz0), tz0 + 0.85 * (tz1 - tz0), boxes["pts_10TORS"])]
        if hips:
            bands.append((hips[1].z - 0.35 * (hips[1].z - hips[0].z), hips[1].z, boxes["pts_17HIPS"]))
        core = 0.5 * (T[1].x - T[0].x) * 0.75          # the trunk, not the shoulders or a hanging arm
        cx, cy = (T[0].x + T[1].x) / 2, (T[0].y + T[1].y) / 2
        need_x = need_y = 1.0
        mpts = [v.co for v in ob.data.vertices]
        for z_lo, z_hi, bpts in bands:
            b = band_reach(bpts, z_lo, z_hi, cx, cy, core)
            m = band_reach(mpts, z_lo, z_hi, cx, cy, core * 1.25)
            if b and m:
                need_x = max(need_x, 1.04 * b[0] / max(m[0], 1e-4), 1.04 * b[1] / max(m[1], 1e-4))
                need_y = max(need_y, 1.06 * b[2] / max(m[2], 1e-4), 1.06 * b[3] / max(m[3], 1e-4))
        need = max(need_x, need_y)
        if need > 1.0:
            W = Matrix.Translation((cx, cy, 0)) @ Matrix.Diagonal((need_x, need_y, 1.0, 1.0)) @ Matrix.Translation((-cx, -cy, 0))
            ob.data.transform(W)
            ob.data.update()
            girth = f"{need_x:.2f} wide, {need_y:.2f} deep"
            if need > 1.35:
                warnings.append(f"Widened x{need:.2f} to cover the body: a big change; check the look on the sheet.")
    zipped = zip_cracks(ob) if slot in ("gloves", "boots", "helmet") else None
    peepholes = fill_peepholes(ob, body) if slot in ("gloves", "boots", "helmet") else None
    tri_after = sum(len(p.vertices) - 2 for p in ob.data.polygons)

    # Review pictures: the piece on the bare body, before and after, and the openings.
    if a.sheet:
        sheet(ob, body, Path(a.sheet).resolve(), slot)

    out = src.with_name(f"{piece}_Revised.fbx")
    if not a.dry_run:
        for o in body:
            o.hide_set(True)
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=str(out), use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE", axis_forward="-Z",
                                 axis_up="Y", mesh_smooth_type="OFF", use_mesh_modifiers=False,
                                 path_mode="COPY", embed_textures=True, bake_anim=False)
    kn.report("Gear first pass", {
        "piece": piece, "lineage": lineage, "slot": slot, "mesh_health": health or "nothing broken", "openings": done,
        "triangles": f"{tri_before} -> {tri_after}", "covers_its_body_part": covers, "peepholes_filled": peepholes, "crack_points_zipped": zipped,
        "sized": f"x{scale:.3f}: {measure}" + (f"; widened x{girth} at chest and hips to cover the body" if girth else ""), "now_m": [round(x * scale, 3) for x in size],
        "written": None if a.dry_run else str(out),
        "next": "second pass once the whole set is in: T-pose fit, skin weights, body shapes, clip test, Unity export"},
        problems, warnings)


def sheet(ob, body, path, slot):
    """Front, side and back of the piece on the bare body, plus a look along each opening."""
    import tempfile
    import numpy as np
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    size = 480
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("kn_w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1)
    sc.world = w
    for rot in ((50, 0, -30), (60, 0, 150)):
        l = bpy.data.objects.new("kn_sun", bpy.data.lights.new("kn_sun", "SUN"))
        l.data.energy = 2.5
        l.rotation_euler = [math.radians(r) for r in rot]
        sc.collection.objects.link(l)
    skin = kn.preview_material(kn.colour_map_path(), name="kn_body_preview", uv_layer="map1")
    for o in body:
        o.data.materials.clear()
        o.data.materials.append(skin)
    cd = bpy.data.cameras.new("kn_c")
    cd.type = "ORTHO"
    cam = bpy.data.objects.new("kn_c", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    ctr = (lo + hi) / 2
    tmp = Path(tempfile.mkdtemp(prefix="kn_gear_"))
    shots = []
    views = [("front", (0, -1, 0.05), True), ("side", (1, 0, 0.05), True), ("back", (0, 1, 0.05), True),
             ("piece from above", (0, -0.001, 1), False), ("piece from below", (0, -0.001, -1), False),
             ("piece three-quarter", (0.7, -1, -0.35), False)]
    for name, d, with_body in views:
        for o in body:
            o.hide_render = not with_body
        d = Vector(d).normalized()
        span = max(hi - lo) * (1.25 if with_body else 1.1)
        cd.ortho_scale = max(span, 0.3) if not with_body else max(2.0 if slot == "armor" else span, span)
        c = ctr if not with_body else Vector((ctr.x, ctr.y, ctr.z))
        cam.location = c + d * 20
        cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p = tmp / (name.replace(" ", "_") + ".png")
        sc.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        shots.append(p)
    cols, gap = 3, 6
    rows = [shots[:3], shots[3:]]
    img = np.full((2 * size + 3 * gap, cols * size + (cols + 1) * gap, 4), 0.1, np.float32)
    img[..., 3] = 1
    for r, row in enumerate(rows):
        for c, p in enumerate(row):
            im = bpy.data.images.load(str(p))
            px = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
            y = (1 - r) * size + (2 - r) * gap
            x = c * size + (c + 1) * gap
            img[y:y + size, x:x + size] = px
            bpy.data.images.remove(im)
    out = bpy.data.images.new("kn_sheet", img.shape[1], img.shape[0], alpha=True)
    out.pixels = img.ravel()
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save()


if __name__ == "__main__":     # importable by gear_second_pass.py
    main()
