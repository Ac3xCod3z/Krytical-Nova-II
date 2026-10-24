"""The fit half of gear_second_pass.py: weights, push clear, Synty slots, body keys, swatch colours, gear swatch."""
import importlib
import json
import math
import sys
import tempfile
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kn_common as kn

importlib.reload(kn)
import mannequin_clip as mc

importlib.reload(mc)

ARMOR_SLOTS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR"]
ALL_KEYS = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny", "kn_bust", "kn_butt", "kn_defined")


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def islands_of(me):
    """Vertex islands (lists of vertex indices) and each vertex's neighbours."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        grp, stack = [v.index], [v]
        seen.add(v.index)
        while stack:
            x = stack.pop()
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    grp.append(o.index)
                    stack.append(o)
        islands.append(grp)
    neigh = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
    bm.free()
    return islands, neigh


def rest_weights(piece, body, evaluated_soup, weights_at):
    """Final skin weights, from the T-pose body (arms included now the sleeves lie along them)."""
    verts, tris, owner = evaluated_soup([o for s, o in body.items() if s != "01HEAD"])
    tree = BVHTree.FromPolygons(verts, tris)
    out = []
    for v in piece.data.vertices:
        loc, _n, idx, _d = tree.find_nearest(v.co)
        t3 = tris[idx]
        out.append(weights_at(owner, t3, poly_3d_calc([verts[i] for i in t3], loc)))
    return out


def conform(piece, body, evaluated_soup, margin=0.004, rigid=False):
    """Push whatever lies inside the T-pose body (or nearer it than `margin`, metres) out along the body's surface.
    The push is spread over the cloth so nothing kinks; small hard pieces (a stud, a buckle, a plate) move whole."""
    verts, tris, _owner = evaluated_soup([o for s, o in body.items() if s != "01HEAD"])
    tree = BVHTree.FromPolygons(verts, tris)
    me = piece.data
    n = len(me.vertices)
    push = [Vector((0, 0, 0)) for _ in range(n)]
    moved = 0
    for v in me.vertices:
        loc, nrm, _i, dist = tree.find_nearest(v.co)
        if loc is None or dist > 0.04:
            continue
        d = (v.co - loc).dot(nrm)
        if d < margin:
            push[v.index] = nrm * (margin - d)
            moved += 1
    islands, neigh = islands_of(me)
    for grp in islands:
        # a robot's plates move whole, never bent; its big shell does not (moved by its one largest push, the
        # whole torso slid off the head's centre)
        if len(grp) < (600 if rigid else 120):
            big = max((push[i] for i in grp), key=lambda d: d.length)
            for i in grp:
                push[i] = big.copy()
    for _ in range(10 if rigid else 4):              # a robot's shell: the push spread wide so it stays flat
        nxt = []
        for i in range(n):
            if neigh[i]:
                avg = sum((push[j] for j in neigh[i]), Vector((0, 0, 0))) / len(neigh[i])
                nxt.append(avg if avg.length > push[i].length else push[i])
            else:
                nxt.append(push[i])
        push = nxt
    for v, d in zip(me.vertices, push):
        v.co += d
    me.update()
    return moved


def tighten_sleeves(piece, body, rig, sleeve_verts, evaluated_soup, gap=0.012, start=0.55):
    """Sleeve openings sit close round the arm that comes through them (owner, 22 Oct 2026): over the last part of
    each sleeve (from `start` of its length to its end) the cloth is drawn in towards the arm's skin, gradually,
    until at the opening it is `gap` metres off the skin. Returns how many vertices moved."""
    if not sleeve_verts:
        return 0
    me = piece.data
    arms = [o for s, o in body.items() if s in ("11AUPL", "12AUPR", "13ALWL", "14ALWR")]
    verts, tris, _owner = evaluated_soup(arms)
    tree = BVHTree.FromPolygons(verts, tris)
    mw = rig.matrix_world
    moved = 0
    for side, s in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        u = (E - S).normalized()
        mine = [i for i in sleeve_verts if me.vertices[i].co.x * side > 0]
        if not mine:
            continue
        ts = {i: (me.vertices[i].co - S).dot(u) for i in mine}
        t_end = max(ts.values())
        t_from = start * t_end
        for i in mine:
            t = ts[i]
            if t <= t_from:
                continue
            k = (t - t_from) / max(t_end - t_from, 1e-6)
            k = k * k * (3 - 2 * k)
            co = me.vertices[i].co
            loc, nrm, _x, dist = tree.find_nearest(co)
            if loc is None:
                continue
            out = co - loc
            if out.dot(nrm) <= gap or out.length < 1e-6:
                continue
            target = loc + out.normalized() * gap
            me.vertices[i].co = co.lerp(target, k)
            moved += 1
    me.update()
    return moved


# Sleeves converted from arms-down models whose underside hangs in a curtain below the T-pose arm (owner, 23 Oct
# 2026: Dock Runner, Bastion Field, StonePledge). The Elf's and Skeleton's were right and are left alone.
LIFT_UNDERSIDE = ("Civilian", "Soldier", "Viking")


def lift_undersides(piece, body, rig, sleeve_verts, evaluated_soup, gap=0.025, passes=3):
    """The underside of each sleeve, below the arm, drawn up to `gap` metres off the arm's skin: full straight under
    the arm, fading round the sides and in from the armpit, the pull smoothed through the sleeve so it never kinks;
    small pieces (studs, buckles) ride whole. Returns how many vertices moved."""
    if not sleeve_verts:
        return 0
    me = piece.data
    arms = [o for s, o in body.items() if s in ("11AUPL", "12AUPR", "13ALWL", "14ALWR")]
    verts, tris, _owner = evaluated_soup(arms)
    tree = BVHTree.FromPolygons(verts, tris)
    mw = rig.matrix_world
    n = len(me.vertices)
    disp = [Vector() for _ in range(n)]
    for side, s in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        u = (mw @ rig.pose.bones[f"hand_{s}"].head - S).normalized()
        for i in sleeve_verts:
            co = me.vertices[i].co
            if co.x * side <= 0:
                continue
            t = (co - S).dot(u)
            off = co - S - u * t
            if t <= 0 or off.length < 1e-6:
                continue
            below = -off.z / off.length
            if below <= 0.2:
                continue
            k = min(1.0, (below - 0.2) / 0.6)
            r = min(1.0, t / 0.07)
            k = k * k * (3 - 2 * k) * r * r * (3 - 2 * r)
            loc, nrm, _x, _d = tree.find_nearest(co)
            if loc is None or (co - loc).dot(nrm) <= gap:
                continue
            disp[i] = (loc + (co - loc).normalized() * gap - co) * k
    islands, neigh = islands_of(me)
    for _ in range(passes):
        nxt = list(disp)
        for i in sleeve_verts:
            if neigh[i]:
                nxt[i] = (disp[i] + sum((disp[j] for j in neigh[i]), Vector())) / (1 + len(neigh[i]))
        disp = nxt
    for grp in islands:
        if len(grp) < 60:
            big = max((disp[i] for i in grp), key=lambda d: d.length)
            for i in grp:
                disp[i] = big.copy()
    moved = 0
    for v, d in zip(me.vertices, disp):
        if d.length > 1e-4:
            v.co += d
            moved += 1
    me.update()
    return moved


# The sleeve ends at its cuff (owner, 23 Oct 2026): the Viking's wrist flaps stuck out over the hand once the arm
# was in T-pose, so nothing of the sleeve runs past the wrist.
TRIM_PAST_CUFF = ("Viking",)


def trim_past_cuff(piece, rig, sleeve_verts, reach=0.15, flap=0.10):
    """The sleeve ends at the wrist: a loose piece starting within `flap` metres of the wrist and running past it
    (a flap over the hand) is removed, and a cuff that runs on past the wrist is squared off there. Only within
    `reach` metres of the arm. Returns how many vertices were removed or drawn back."""
    me = piece.data
    mw = rig.matrix_world
    islands, _neigh = islands_of(me)
    doomed, drawn = set(), 0
    for side, s in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        W = mw @ rig.pose.bones[f"hand_{s}"].head
        u = (W - S).normalized()
        tw = (W - S).dot(u) - 0.01
        t_of = lambda i: (me.vertices[i].co - S).dot(u)
        near = lambda i: (me.vertices[i].co - S - u * t_of(i)).length < reach
        for g in islands:
            if me.vertices[g[0]].co.x * side <= 0 or not all(near(i) for i in g):
                continue
            ts = [t_of(i) for i in g]
            if max(ts) <= tw:
                continue
            if min(ts) > tw - flap and len(g) < 200:
                doomed.update(g)
                continue
            for i, t in zip(g, ts):
                if t > tw:
                    me.vertices[i].co -= u * (t - tw)
                    drawn += 1
    if doomed:
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in doomed], context="VERTS")
        bm.to_mesh(me)
        bm.free()
    me.update()
    return {"removed": len(doomed), "drawn_back": drawn}


def cinch_waist(piece, body, rig, evaluated_soup, gap=0.015, sector=15, slice_=0.02):
    """The waist and hips drawn in to the body (owner, 23 Oct 2026: "waist need to conform better"; the arms-down
    models stood 10-15 cm off the waist all round). Around the pelvis, in 2 cm slices and 15 degree sectors, the
    cloth's innermost layer is found, and every vertex there moves in by the same amount until that layer is `gap`
    metres off the body: the belt, studs and folds keep their thickness. Full from the crotch to the lower ribs,
    eased out above and below; never pushed outward. Returns how many vertices moved."""
    mw = rig.matrix_world
    c = mw @ rig.pose.bones["pelvis"].head
    z_lo = (mw @ rig.pose.bones["thigh_l"].head).z - 0.04
    z_hi = (mw @ rig.pose.bones["spine_02"].head).z + 0.06
    ease = 0.05
    bv, bt, _o = evaluated_soup([o for s, o in body.items() if s in ("10TORS", "17HIPS")])
    btree = BVHTree.FromPolygons(bv, bt)
    me = piece.data
    atree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    nz = int((z_hi - z_lo + 2 * ease) / slice_) + 1
    ns = 360 // sector

    def cell(p):
        a = (math.degrees(math.atan2(p.y - c.y, p.x - c.x)) + 360) % 360
        return int((p.z - (z_lo - ease)) / slice_), int(a // sector)

    def rad(p):
        return math.hypot(p.x - c.x, p.y - c.y)
    # rays out from the body's centre line through each cell: the first body hit is the skin, the first armor
    # hit the cloth's inner face (a low-poly body leaves most cells without a vertex of its own)
    off = {}
    for zi in range(nz):
        z = z_lo - ease + (zi + 0.5) * slice_
        for si in range(ns):
            a = math.radians((si + 0.5) * sector)
            d = Vector((math.cos(a), math.sin(a), 0.0))
            o = Vector((c.x, c.y, z))
            hb = btree.ray_cast(o, d, 1.0)
            ha = atree.ray_cast(o, d, 1.0)
            if hb[0] is None or ha[0] is None:
                continue
            off[(zi, si)] = max(0.0, ha[3] - hb[3] - gap)
    # smoothed over neighbouring cells so no slice kinks; a cell's own need is never exceeded (no clipping)
    by_slice = {}
    for (zi, si), d in off.items():
        by_slice.setdefault(zi, []).append(d)
    pull = uniform_pull(by_slice)
    sm = {(zi, si): pull.get(zi, 0.0) for (zi, si) in off}
    cinch_waist.stats = {"cells": nz * ns, "with_body": len(off), "need_over_2cm": sum(1 for d in off.values() if d > 0.02),
                         "after_smoothing_over_2cm": sum(1 for d in sm.values() if d > 0.02),
                         "z_band": [round(z_lo, 3), round(z_hi, 3)], "body_points": len(bv)}
    moved = 0
    for v in me.vertices:
        p = v.co
        a_ = (math.degrees(math.atan2(p.y - c.y, p.x - c.x)) + 360) % 360
        d = blend_cells(sm, (p.z - (z_lo - ease)) / slice_, a_ / sector, ns)
        if not d:
            continue
        w = 1.0
        if p.z < z_lo:
            w = max(0.0, 1 - (z_lo - p.z) / ease)
        elif p.z > z_hi:
            w = max(0.0, 1 - (p.z - z_hi) / ease)
        w = w * w * (3 - 2 * w)
        r = rad(p)
        if r < 1e-4 or w <= 0:
            continue
        k_in = (r - d * w) / r
        v.co.x = c.x + (p.x - c.x) * k_in
        v.co.y = c.y + (p.y - c.y) * k_in
        moved += 1
    me.update()
    return moved


def seat_floaters(piece, gap=0.015, keep=0.003, small=60):
    """Small loose pieces (rivets, studs) left floating more than `gap` metres off the rest of the armor are moved
    straight back until they sit `keep` metres off it: the fit's pushes can carry the cloth away from what was sewn
    on it (four rivets hung 3 cm off the Skeleton's shoulder guards). Returns how many pieces moved."""
    me = piece.data
    islands, _neigh = islands_of(me)
    lab = [0] * len(me.vertices)
    for k, g in enumerate(islands):
        for i in g:
            lab[i] = k
    moved = 0
    for k, g in enumerate(islands):
        if len(g) >= small:
            continue
        others = [tuple(p.vertices) for p in me.polygons if lab[p.vertices[0]] != k]
        if not others:
            continue
        tree = BVHTree.FromPolygons([v.co for v in me.vertices], others)
        best = None
        for i in g:
            loc, _n, _x, d = tree.find_nearest(me.vertices[i].co)
            if loc is not None and (best is None or d < best[0]):
                best = (d, loc - me.vertices[i].co)
        if best is None or best[0] <= gap:
            continue
        step = best[1] * ((best[0] - keep) / best[0])
        for i in g:
            me.vertices[i].co += step
        moved += 1
    me.update()
    return moved


def uniform_pull(need_by_slice, share=0.3, sigma=2.0):
    """One pull per slice, smooth along the limb (owner, 23 Oct 2026: "make it uniform so that it still mimics the
    original model"; pulled cell by cell, a knee pad bent out of shape). A slice's pull is the `share` quantile of what
    its directions need (the tight side does not hold back the loose one, nor the loose side crush the tight one), then
    smoothed along the limb over `sigma` slices so no band stands out. Whatever pokes in after is pushed clear later."""
    raw = {}
    for ti, ds in need_by_slice.items():
        ds = sorted(ds)
        raw[ti] = ds[int(share * (len(ds) - 1))] if ds else 0.0
    if not raw:
        return {}
    out = {}
    lo, hi = min(raw), max(raw)
    for ti in range(lo, hi + 1):
        acc = w = 0.0
        for tj, d in raw.items():
            k = math.exp(-0.5 * ((ti - tj) / sigma) ** 2)
            acc += d * k
            w += k
        out[ti] = acc / w if w else 0.0
    return out


def blend_cells(sm, ft, fa, ns):
    """The pull at a point between cell centres: bilinear over slice (`ft`, in cells) and angle (`fa`, in cells,
    wrapping round `ns`). A pull constant per cell sheared every face that crossed a cell edge into a spike (the
    shards on Dock Runner's and Bastion Field's sleeves); cells with no reading take no part."""
    t0 = math.floor(ft - 0.5)
    a0 = math.floor(fa - 0.5)
    wt = ft - 0.5 - t0
    wa = fa - 0.5 - a0
    tot, acc = 0.0, 0.0
    for dt, w1 in ((0, 1 - wt), (1, wt)):
        for da, w2 in ((0, 1 - wa), (1, wa)):
            v = sm.get((t0 + dt, (a0 + da) % ns))
            if v is None:
                continue
            w = w1 * w2
            tot += w
            acc += v * w
    return acc / tot if tot > 1e-6 else 0.0


def _frame(u):
    """Two unit vectors at right angles to `u` (and each other)."""
    a = Vector((0, 0, 1)) if abs(u.z) < 0.9 else Vector((1, 0, 0))
    e1 = u.cross(a).normalized()
    return e1, u.cross(e1).normalized()


def cinch_limb(piece, skin, A, B, t0, t1, gap_at, pick, ease=0.04, rigid=False, sector=20, slice_=0.025,
               centre=False, wrap=0.45):
    """Around the line A->B (a limb: knee to ankle, shoulder to wrist), from `t0` to `t1` metres along it: rays out
    from the line through 2.5 cm by 20 degree cells, the first skin hit against the first armor hit. Only pieces that
    wrap the limb take part (a trouser leg, a shin guard, a sleeve: hit by at least `wrap` of the rays round it in
    some slice); flat cloth hanging past it (rags, a loincloth) is left alone, as cinching it crumpled it. `centre`
    first sets each slice of those pieces straight side to side over the limb, by the piece's own width there (the
    Skeleton's shin guards stood beside its leg bones). Then every vertex of a cell moves in by the same amount until
    the cloth is `gap_at(t)` metres off the skin, keeping its thickness, eased out at both ends; never pushed outward.
    With `rigid` each piece moves as one, by the least any of its cells needs (robot plates never bend). Returns
    (vertices moved, slices recentred, pieces that wrap)."""
    me = piece.data
    u = (B - A).normalized()
    e1, e2 = _frame(u)
    sv, st, _o = skin
    btree = BVHTree.FromPolygons(sv, st)
    ns = 360 // sector
    nt = int((t1 - t0 + 2 * ease) / slice_) + 1
    idx = [v.index for v in me.vertices if pick(v.co)]
    if not idx:
        return 0, 0, 0
    islands, _neigh = islands_of(me)
    isl_of = {}
    for n_, g in enumerate(islands):
        for v in g:
            isl_of[v] = n_

    def tt(p):
        return (p - A).dot(u)

    def cell(p):
        t = tt(p)
        r = p - A - u * t
        ang = (math.degrees(math.atan2(r.dot(e2), r.dot(e1))) + 360) % 360
        return int((t - (t0 - ease)) / slice_), int(ang // sector)

    def weight(t):
        w = 1.0
        if t < t0:
            w = max(0.0, 1 - (t0 - t) / ease)
        elif t > t1:
            w = max(0.0, 1 - (t - t1) / ease)
        return w * w * (3 - 2 * w)
    sel = set(idx)
    faces = [p for p in me.polygons if any(v in sel for v in p.vertices)]

    def cast():
        tree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in faces])
        out = {}
        for ti in range(nt):
            t = t0 - ease + (ti + 0.5) * slice_
            o = A + u * t
            for si in range(ns):
                a = math.radians((si + 0.5) * sector)
                d = e1 * math.cos(a) + e2 * math.sin(a)
                hb = btree.ray_cast(o, d, 0.5)
                ha = tree.ray_cast(o, d, 0.5)
                isl = isl_of[faces[ha[2]].vertices[0]] if ha[0] is not None else None
                out[(ti, si)] = (t, hb, ha, isl)
        return out
    recentred = 0
    if centre == "whole":
        # Every leg piece of some size is set straight over the limb first, side to side and front to back, by its
        # own extent in each slice: the Skeleton's guards, made for a human leg, stood 9 cm out and 6 cm behind its
        # leg bones, where rays from the bone barely met them.
        big = {}
        for i in idx:
            big.setdefault(isl_of[i], []).append(i)
        cand = [i for g in big.values() if len(g) >= 30 for i in g]
        by_slice = {}
        for i in cand:
            p = me.vertices[i].co
            r = p - A - u * tt(p)
            by_slice.setdefault(cell(p)[0], []).append((r.dot(e1), r.dot(e2)))
        # one shift for the whole lower leg, from the slices between the ankle and the knee, given to every piece
        # there (straps and buckles too): slice by slice the guard came apart and the small pieces were left behind
        span = t1 - t0
        mids = [d for ti, rs in by_slice.items() if len(rs) >= 6
                and 0.25 * span < t0 - ease + (ti + 0.5) * slice_ - t0 < 0.8 * span
                for d in [e1 * (min(x for x, _y in rs) + max(x for x, _y in rs)) / 2 +
                          e2 * (min(y for _x, y in rs) + max(y for _x, y in rs)) / 2]]
        if mids:
            d = sum(mids, Vector()) / len(mids)
            if d.length > 0.10:
                d = d.normalized() * 0.10
            for i in idx:
                p = me.vertices[i].co
                me.vertices[i].co = p - d * weight(tt(p))
            recentred = len(mids)
    hits = cast()
    seen = {}
    for (ti, si), (_t, _hb, ha, isl) in hits.items():
        if isl is not None:
            seen.setdefault((isl, ti), set()).add(si)
    wraps = {isl for (isl, _ti), ss in seen.items() if len(ss) >= wrap * ns}
    mine = [i for i in idx if isl_of[i] in wraps]
    if not mine:
        return 0, recentred, 0
    if centre == "rays":
        # The leg in the middle of its trouser or short leg (owner, 23 Oct 2026: Dock Runner's shorts flared, the
        # legs off-centre in the openings): each slice of a piece that wraps the limb is moved sideways until the
        # inner face it shows the rays sits evenly round the limb; the pockets and pads on it ride along.
        shift = {}
        for ti in range(nt):
            pts = [ha[0] for si in range(ns) for (_t, _hb, ha, isl) in [hits[(ti, si)]] if isl in wraps]
            if len(pts) < 0.75 * ns:
                continue
            o = A + u * (t0 - ease + (ti + 0.5) * slice_)
            off = sum(pts, Vector()) / len(pts) - o
            off -= u * off.dot(u)
            if off.length > 0.06:
                off = off.normalized() * 0.06
            shift[ti] = off
        if shift:
            keys = sorted(shift)
            sm_ = {}
            for kk in keys:
                near = [shift[j] for j in keys if abs(j - kk) <= 1]
                sm_[kk] = sum(near, Vector()) / len(near)
            sizes = {n_: len(g) for n_, g in enumerate(islands)}
            for i in idx:
                if isl_of[i] not in wraps and sizes[isl_of[i]] >= 200:
                    continue            # big hanging cloth (a skirt, a vest tail) stays where it hangs
                p = me.vertices[i].co
                kk = cell(p)[0]
                d = sm_.get(kk)
                if d is None:
                    d = sm_[min(keys, key=lambda j: abs(j - kk))]
                me.vertices[i].co = p - d * weight(tt(p))
            recentred = len(sm_)
            hits = cast()
    need = {}
    for (ti, si), (t, hb, ha, isl) in hits.items():
        if hb[0] is None or ha[0] is None or isl not in wraps:
            continue
        need.setdefault(ti, []).append(max(0.0, ha[3] - hb[3] - gap_at(t)))
    pull = uniform_pull(need)
    move = {}
    for i in mine:
        p = me.vertices[i].co
        t = tt(p)
        r = p - A - u * t
        if r.length < 1e-4:
            continue
        ft = (t - (t0 - ease)) / slice_ - 0.5
        k0 = math.floor(ft)
        a0, a1 = pull.get(k0), pull.get(k0 + 1)
        if a0 is None and a1 is None:
            continue
        a0 = a1 if a0 is None else a0
        a1 = a0 if a1 is None else a1
        d = a0 + (a1 - a0) * (ft - k0)
        if d > 1e-4:
            move[i] = -r.normalized() * d * weight(t)
    # small pieces on the pulled cloth (patches, pockets, flaps) move with it, by the nearest pulled vertex: left
    # where they were they stuck out of the sleeve as shards (Dock Runner, Bastion Field)
    if move and not rigid:
        kd = KDTree(len(move))
        for i in move:
            kd.insert(me.vertices[i].co, i)
        kd.balance()
        sizes = {n_: len(g) for n_, g in enumerate(islands)}
        for i in idx:
            if i in move or isl_of[i] in wraps or sizes[isl_of[i]] >= 200:
                continue
            _co, j, dist = kd.find(me.vertices[i].co)
            if j is not None and dist < 0.03:
                move[i] = move[j]
        for g in islands:
            if len(g) < 200 and any(i in move for i in g) and not any(isl_of[i] in wraps for i in g[:1]):
                ms = [move[i] for i in g if i in move]
                avg = sum(ms, Vector()) / len(ms)
                for i in g:
                    move[i] = avg
    if rigid and move:
        for g in islands:
            ms = [move[i] for i in g if i in move]
            if not ms:
                continue
            # the plate's middle need, not its least: every plate has some corner that needs nothing, and by the
            # least the robot's shoulders never came in (the push clear afterwards lifts any plate that sinks)
            ms.sort(key=lambda m: m.length)
            mid = ms[len(ms) // 2] if len(ms) >= len(g) * 0.2 else Vector()
            for i in g:
                move[i] = mid
    moved = 0
    for i, m in move.items():
        if m.length > 1e-5:
            me.vertices[i].co += m
            moved += 1
    me.update()
    return moved, recentred, len(wraps)


def fit_limbs(piece, body, rig, evaluated_soup, lineage):
    """Shoulders and upper arms drawn in to the arm, and lower legs tapered to the ankle so trousers and shin guards
    fit inside a boot (owner, 23 Oct 2026: the shoulders were too bulky on nearly every set, legs must taper towards
    the bottom so they fit inside a boot, and the Skeleton's shin guards missed its legs, so a skeleton's leg pieces
    are also set straight over its leg bones)."""
    mw = rig.matrix_world

    def J(b):
        return mw @ rig.pose.bones[b].head
    rigid = lineage == "Robot"
    out = {}
    arm_skin = evaluated_soup([o for s, o in body.items() if s in ("10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR")])
    # the feet too: below the ankle the hem had no skin to measure against and kept its flare
    leg_skin = evaluated_soup([o for s, o in body.items() if s in ("17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR")])
    for side, s in ((1, "l"), (-1, "r")):
        S, W = J(f"upperarm_{s}"), J(f"hand_{s}")
        L = (W - S).length

        u_ = (W - S).normalized()

        def pick_arm(p, S=S, side=side, L=L, u_=u_):
            # past the joint, the whole sleeve; at the shoulder, only what lies over the top of it: the chest and the
            # sides below the arm belong to the torso (pulled toward the arm they shredded Dock Runner's vest)
            if p.x * side <= abs(S.x) - 0.07 or (p - S).length >= L + 0.1:
                return False
            return (p - S).dot(u_) > 0.04 or p.z > S.z - 0.01
        out[f"arm_{s}"] = cinch_limb(piece, arm_skin, S, W, -0.05, L - 0.02, lambda t: 0.015, pick_arm,
                                     rigid=rigid, wrap=0.3)
        H, K, F = J(f"thigh_{s}"), J(f"calf_{s}"), J(f"foot_{s}")
        lc = "whole" if lineage == "Skeleton" else "rays"
        # thighs and knees formed round the leg, as real trousers and shorts are (they stood bloated)
        crotch = H.z - 0.07

        def pick_thigh(p, side=side, K=K, crotch=crotch):
            return p.x * side > 0.01 and K.z - 0.04 < p.z < crotch
        lt = (K - H).length
        # not a skeleton's: its thighs are bare bone, and seen from the thin bone its loincloth's front, side and
        # back panels together look like a tube wrapping the leg, so the pull crumpled them
        if lineage != "Skeleton":
            out[f"thigh_{s}"] = cinch_limb(piece, leg_skin, H, K, 0.10, lt + 0.02, lambda t: 0.015, pick_thigh,
                                           rigid=rigid, centre="rays")

        def pick_shin(p, side=side, K=K):
            return p.x * side > 0.01 and p.z <= K.z + 0.06
        ls = (F - K).length
        # 1.5 cm off at the knee, 0.8 at the ankle; round a skeleton's thin bones 2 cm more, or the guards were
        # squeezed into slats. The pull runs past the ankle to the bottom of the hem: short of it the hems flared.
        room = 0.02 if lineage == "Skeleton" else 0.0

        def taper(t, ls=ls, room=room):
            return room + 0.015 - 0.007 * max(0.0, min(1.0, t / ls))
        out[f"shin_{s}"] = cinch_limb(piece, leg_skin, K, F, 0.0, ls + 0.08, taper, pick_shin, rigid=rigid,
                                      centre=lc, ease=0.02)
    return {k: {"moved": v[0], "slices_recentred": v[1], "pieces_wrapping": v[2]} for k, v in out.items()}


def close_armholes(piece, body, rig, evaluated_soup, gap=0.008, reach=0.16, rings=4):
    """The open edges round each shoulder (the sleeve's top, the shirt's armhole: what was cut where the arms-down
    sleeve met the body) drawn onto the arm and shoulder, `gap` metres off the skin, the pull fading out over `rings`
    rows of faces so the cloth eases in rather than kinks (owner, 23 Oct 2026: big gaping holes under Dock Runner's
    and Bastion Field's arms, the body showing through the shirt). Returns how many edge vertices were closed."""
    me = piece.data
    mw = rig.matrix_world
    skin_v, skin_t, _o = evaluated_soup([o for s_, o in body.items() if s_ in ("10TORS", "11AUPL", "12AUPR")])
    tree = BVHTree.FromPolygons(skin_v, skin_t)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    edge = {v.index for v in bm.verts if any(e.is_boundary for e in v.link_edges)}
    adj = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
    bm.free()
    shoulders = [mw @ rig.pose.bones[f"upperarm_{s_}"].head for s_ in ("l", "r")]
    near = {i for i in edge if min((me.vertices[i].co - S).length for S in shoulders) < reach}
    if not near:
        return 0
    move = {}
    for i in near:
        co = me.vertices[i].co
        loc, nrm, _x, _d = tree.find_nearest(co)
        if loc is None:
            continue
        target = loc + nrm * gap
        if (co - loc).dot(nrm) > gap:          # only drawn in, never pushed out (that is the push clear's job)
            move[i] = target - co
    # the pull fades over the next rows of faces, each row taking the mean of its moved neighbours, less each row
    front = set(move)
    done = set(move)
    for r in range(1, rings + 1):
        k = 1.0 - r / (rings + 1)
        nxt = {}
        for i in front:
            for j in adj[i]:
                if j in done:
                    continue
                nxt.setdefault(j, []).append(move[i])
        for j, ms in nxt.items():
            move[j] = (sum(ms, Vector()) / len(ms)) * k
        done |= set(nxt)
        front = set(nxt)
    for i, m in move.items():
        me.vertices[i].co += m
    me.update()
    return len(near)


def underarm_gusset(piece, body, rig, evaluated_soup, fcol, reach=0.24, look=0.03, lift=0.006):
    """Cloth under the arm where the converted model has none (owner, 23 Oct 2026: big gaping holes under Dock
    Runner's and Bastion Field's arms). When the arms-down sleeves were cut free, the cloth under the arm stayed with
    the shirt, so the inside of the upper arm and the side of the chest were bare. Every body face near the armpit,
    below the shoulder, with no armor within `look` metres out along it, is copied `lift` metres off the skin and
    coloured like the nearest armor face: a gusset, as a real shirt has. It takes the body's weights and keys later,
    like the rest of the cloth. Returns how many faces were added."""
    me = piece.data
    mw = rig.matrix_world
    atree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    objs = [o for s_, o in body.items() if s_ in ("10TORS", "11AUPL", "12AUPR")]
    dg = bpy.context.evaluated_depsgraph_get()
    new_v, new_f, new_c = [], [], []
    for side, s_ in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s_}"].head
        pit = S + Vector((-0.03 * side, 0.0, -0.06))
        elbow_x = abs((mw @ rig.pose.bones[f"lowerarm_{s_}"].head).x)    # along the upper arm as far as the elbow
        for o in objs:
            ev = o.evaluated_get(dg)
            m = ev.to_mesh()
            m.calc_loop_triangles()
            ow = o.matrix_world
            nm = ow.to_3x3().inverted().transposed()
            vw = [ow @ v.co for v in m.vertices]
            vn = [(nm @ v.normal).normalized() for v in m.vertices]
            for t in m.loop_triangles:
                c = sum((vw[i] for i in t.vertices), Vector()) / 3
                # under the arm and over the top of the shoulder (skin showed between the Soldier's pads too)
                if (c - pit).length > reach or c.z > S.z + 0.12 or c.x * side < abs(S.x) * 0.55                         or c.x * side > elbow_x:
                    continue
                n = sum((vn[i] for i in t.vertices), Vector()).normalized()
                hit = atree.ray_cast(c + n * 0.001, n, look)
                if hit[0] is not None:
                    continue
                base = len(new_v)
                new_v += [vw[i] + vn[i] * lift for i in t.vertices]
                new_f.append((base, base + 1, base + 2))
                loc, _n, fi, _d = atree.find_nearest(c)
                new_c.append(fcol[fi] if fi is not None and fi < len(fcol) else fcol[0])
            ev.to_mesh_clear()
    if not new_f:
        return 0
    bm = bmesh.new()
    bm.from_mesh(me)
    vs = [bm.verts.new(co) for co in new_v]
    for f in new_f:
        bm.faces.new([vs[i] for i in f])
    # the gusset's triangles share corners: weld them so it is one sheet of cloth, not loose shards
    bmesh.ops.remove_doubles(bm, verts=vs, dist=0.0005)
    bm.to_mesh(me)
    bm.free()
    me.update()
    fcol.extend(new_c)
    return len(new_f)


# A part in a slot replaces the body part there, so a slot the armor hardly covers (a cuff ring, a few plates at the
# elbow, a sleeve ending mid-forearm) would leave the arm or shin bare and the hand or foot floating: unless the armor
# covers nearly all of the body part, its faces go to the slot above instead.
FOLD_INTO = {"13ALWL": "11AUPL", "14ALWR": "12AUPR", "18LEGL": "17HIPS", "19LEGR": "17HIPS"}


def split_slots(piece, weights, region_of_bone, body=None, evaluated_soup=None, cover=0.85, near=0.05):
    """One object per Synty armor slot, by the region the faces' weights belong to. A small loose piece goes whole
    to the slot most of it belongs to, so a buckle is never cut in two."""
    me = piece.data
    vreg = []
    for w in weights:
        acc = {}
        for b, x in w.items():
            r = region_of_bone(b)
            acc[r] = acc.get(r, 0.0) + x
        vreg.append(max(acc.items(), key=lambda kv: kv[1])[0] if acc else "10TORS")
    freg = []
    for f in me.polygons:
        cnt = {}
        for vi in f.vertices:
            cnt[vreg[vi]] = cnt.get(vreg[vi], 0) + 1
        freg.append(max(cnt.items(), key=lambda kv: kv[1])[0])
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen = set()
    for f in bm.faces:
        if f.index in seen:
            continue
        grp, stack = [f.index], [f]
        seen.add(f.index)
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        grp.append(h.index)
                        stack.append(h)
        if len(grp) < 250:
            cnt = {}
            for fi in grp:
                cnt[freg[fi]] = cnt.get(freg[fi], 0) + 1
            r = max(cnt.items(), key=lambda kv: kv[1])[0]
            for fi in grp:
                freg[fi] = r
    bm.free()
    folded = {}
    if body is not None:
        for slot, up in FOLD_INTO.items():
            fis = [i for i, r in enumerate(freg) if r == slot]
            if not fis or slot not in body:
                continue
            tree = BVHTree.FromPolygons([v.co for v in me.vertices], [tuple(me.polygons[i].vertices) for i in fis])
            bv, _bt, _o = evaluated_soup([body[slot]])
            hit = sum(1 for p in bv if (tree.find_nearest(p)[3] or 1.0) < near)
            share = hit / max(1, len(bv))
            need = cover if slot in ("13ALWL", "14ALWR") else 0.6     # baggy trousers sit well off the shin
            folded[slot] = {"covered": round(share, 2), "folded_up": share < need}
            if share < need:
                for i in fis:
                    freg[i] = up
    split_slots.folded = folded
    attr = me.attributes.get("kn_slot") or me.attributes.new("kn_slot", "INT", "FACE")
    attr.data.foreach_set("value", [ARMOR_SLOTS.index(r) for r in freg])
    parts = {}
    for si, slot in enumerate(ARMOR_SLOTS):
        if slot not in freg:
            continue
        ob = piece.copy()
        ob.data = me.copy()
        bpy.context.scene.collection.objects.link(ob)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        lay = bm.faces.layers.int.get("kn_slot")
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[lay] != si], context="FACES")
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.update()
        ob.hide_set(False)
        ob.hide_render = False
        parts[slot] = ob
    return parts


def bind_part(ob, rig, weight_of):
    ob.vertex_groups.clear()
    groups = {}
    for v in ob.data.vertices:
        for b, x in weight_of(v.co).items():
            if b not in groups:
                groups[b] = ob.vertex_groups.new(name=b)
            groups[b].add([v.index], x, "REPLACE")
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.modifiers.new("Armature", "ARMATURE").object = rig


def to_rig_space(ob, rig):
    """Under the rig with no transform of its own, in centimetres, as Synty's parts are."""
    m = rig.matrix_world.inverted() @ ob.matrix_world
    ob.data.transform(m)
    if m.determinant() < 0:
        ob.data.flip_normals()
    ob.parent = rig
    ob.matrix_parent_inverse = Matrix.Identity(4)
    ob.matrix_basis = Matrix.Identity(4)


def body_keys(ob, skin):
    """All seven body keys from the bare body under each vertex (rig space); the part's own keys are empty."""
    if not ob.data.shape_keys:
        ob.shape_key_add(name="Basis", from_mix=False)
    for k in ALL_KEYS:
        if k not in ob.data.shape_keys.key_blocks:
            kb = ob.shape_key_add(name=k, from_mix=False)
            kb.slider_min, kb.slider_max = 0.0, 1.0
    sv, st = mc.skin_soup(skin, {})
    bvh = BVHTree.FromPolygons(sv, st)
    ks = ob.data.shape_keys.key_blocks
    basis = ks[0]
    hits = []
    for v in ob.data.vertices:
        loc, _n, idx, _d = bvh.find_nearest(basis.data[v.index].co)
        tri = st[idx]
        hits.append((tri, poly_3d_calc([sv[i] for i in tri], loc)))
    for k in ALL_KEYS:
        d = []
        for s in skin:
            sk = s.data.shape_keys
            if sk and k in sk.key_blocks:
                b0 = sk.key_blocks[0].data
                d.extend(kd.co - b0[i].co for i, kd in enumerate(sk.key_blocks[k].data))
            else:
                d.extend(Vector((0, 0, 0)) for _ in s.data.vertices)
        kb = ks[k]
        for vi, (tri, bw) in enumerate(hits):
            kb.data[vi].co = basis.data[vi].co + sum((d[c] * w for c, w in zip(tri, bw)), Vector((0, 0, 0)))


def clear_by_keys(ob, skin, push=True):
    """Every key, and the combinations players reach, pushed clear of the body for both sexes. Returns what is left.
    push=False only measures: a robot's hard plates pushed vertex by vertex grew spikes at the elbows."""
    for sex in ("M", "F") if push else ():
        e = {"sex": sex}
        mc.push_out(e, ob, skin, 0.4)
        for _r in range(2):
            mc.push_combos(e, ob, skin, 0.4, mc.STATES_F if sex == "F" else mc.STATES_M)
    left = 0
    for sex in ("M", "F"):
        e = {"sex": sex}
        left += sum(r["verts"] for r in mc.measure(e, [ob], skin, mc.STATES_F if sex == "F" else mc.STATES_M))
    return left


FACE_MARGIN = 0.4   # cm off the skin, as the vertex push
RIGID_PIECE = 300   # vertices, and
DETAIL_CM = 25.0    # across: a loose piece smaller than both is a detail on the cloth (Dock Runner: pouches 15-23, cloth 33+)
FACE_DEEP = 3.0     # cm: a deeper dip is cloth tucked in on purpose (the inner collar, the shirt under the belt)


def clear_faces(ob, skin, rounds=8):
    """Cloth faces that dip into the body between their corners: the Dock Runner's front shoulders and the female
    chest showed skin while nearly every vertex sat outside (owner, 23 Oct 2026). For each sex and body state, an
    outward-facing face whose centre or an edge midpoint is under the skin is pushed out with its corners by the depth
    plus the margin, on the keys of that state (a male rest dip on the basis and every key, a female one on
    masculineFeminine). Returns how many vertex moves were made."""
    from mathutils import geometry
    me = ob.data
    ks = me.shape_keys.key_blocks
    # small compact loose pieces (pouches, tabs, buckles) sit on the cloth, not on the skin: pushed corner by corner the
    # Dock Runner's thigh pouch tore into spikes, and moved whole, torn-off cloth flew off the shoulders. Left alone.
    island = list(range(len(me.vertices)))
    def root(i):
        while island[i] != i:
            island[i] = island[island[i]]
            i = island[i]
        return i
    for e in me.edges:
        island[root(e.vertices[0])] = root(e.vertices[1])
    members = {}
    for i in range(len(me.vertices)):
        members.setdefault(root(i), []).append(i)
    def compact(m):
        xs = [me.vertices[i].co for i in m]
        lo = Vector((min(v.x for v in xs), min(v.y for v in xs), min(v.z for v in xs)))
        hi = Vector((max(v.x for v in xs), max(v.y for v in xs), max(v.z for v in xs)))
        return (hi - lo).length < DETAIL_CM          # mesh-local is rig-local centimetres here, as the push
    detail = {i for m in members.values() if len(m) < RIGID_PIECE and compact(m) for i in m}
    total = 0
    for _ in range(rounds):
        pushed = 0
        for sex in ("M", "F"):
            rest = mc.rest_values({"sex": sex})
            for _label, change in [("rest", {})] + (mc.STATES_F if sex == "F" else mc.STATES_M):
                vals = dict(rest, **change)
                sv, st = mc.skin_soup(skin, vals)
                bvh = BVHTree.FromPolygons(sv, st)
                co = mc.positions(ob, vals)
                move = {}
                for p in me.polygons:
                    vs = [co[i] for i in p.vertices]
                    c = sum(vs, Vector()) / len(vs)
                    worst, wn = 0.0, None
                    for q in [c] + [(vs[i] + vs[(i + 1) % len(vs)]) / 2 for i in range(len(vs))]:
                        d, nn = mc.inside(bvh, q, FACE_DEEP)
                        if d > worst:
                            worst, wn = d, nn
                    if worst <= 0.05 or geometry.normal(vs).dot(wn) < 0.3:
                        continue                       # outside, or an inner surface facing the body
                    m = wn * (worst + FACE_MARGIN)
                    for i in p.vertices:
                        if i not in move or m.length > move[i].length:
                            move[i] = m
                if not move:
                    continue
                for i in detail & move.keys():
                    del move[i]
                if not move:
                    continue
                pushed += len(move)
                keys = [(k, v - rest.get(k, 0.0)) for k, v in change.items() if k in ks and v != rest.get(k, 0.0)]
                if not keys and sex == "F":
                    keys = [("masculineFeminine", 1.0)]
                if not keys:
                    for i, m in move.items():
                        for kb in ks:
                            kb.data[i].co += m
                else:
                    for k, w in keys:
                        for i, m in move.items():
                            ks[k].data[i].co += m / (len(keys) * w)
        total += pushed
        if not pushed:
            break
    # the mesh's own vertex positions follow the Basis: a later bmesh round trip (set_hard_edges) rebuilds the Basis
    # from them, and put every rest push back where it was
    for v, d in zip(me.vertices, ks[0].data):
        v.co = d.co
    return total


def count_dips(ob, skin):
    """Outward-facing cloth faces under the skin (as clear_faces sees them) at rest, both sexes; faces lying on the
    skin (the bare body joined into the part) are not cloth and are skipped."""
    from mathutils import geometry
    me = ob.data
    n = 0
    for sex in ("M", "F"):
        vals = mc.rest_values({"sex": sex})
        sv, st = mc.skin_soup(skin, vals)
        bvh = BVHTree.FromPolygons(sv, st)
        co = mc.positions(ob, vals)
        for p in me.polygons:
            vs = [co[i] for i in p.vertices]
            if all(bvh.find_nearest(v)[3] < 0.05 for v in vs):
                continue
            c = sum(vs, Vector()) / len(vs)
            w = max((mc.inside(bvh, q, FACE_DEEP) for q in [c] + [(vs[i] + vs[(i + 1) % len(vs)]) / 2
                                                                   for i in range(len(vs))]), key=lambda r: r[0])
            if w[0] > 0.05 and geometry.normal(vs).dot(w[1]) >= 0.3:
                n += 1
    return n


def snapshot_keys(ob):
    ks = ob.data.shape_keys.key_blocks
    return {k.name: [d.co.copy() for d in k.data] for k in ks[1:]}


def calm_lone_pushes(ob, before, lone=1.0, near=0.5, company=3):
    """The clip check's push, kept where it is cloth-wide and calmed where it is one or two vertices on their own:
    a vertex the body under it does not move on that key, whose key still strays more than `lone` cm, while fewer than
    `company` of its
    neighbours stray more than `near` cm takes its neighbours' average (owner's parts session, 23 Oct 2026: two
    knee vertices on the Elf's right leg moved 2.6 cm on the bust and butt keys). Returns how many were calmed."""
    me = ob.data
    ks = me.shape_keys.key_blocks
    sc = ob.matrix_world.to_scale()[0] * 100
    neigh = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        neigh[a].append(b)
        neigh[b].append(a)
    calmed = 0
    rest = ks[0].data
    for name, base in before.items():
        kd = ks[name].data
        corr = [kd[i].co - base[i] for i in range(len(base))]
        for i, c in enumerate(corr):
            if c.length * sc <= lone or not neigh[i]:
                continue
            if (base[i] - rest[i].co).length * sc > 0.2:
                continue        # the body moves here on this key: the push is the cloth keeping clear, keep it
            if sum(1 for j in neigh[i] if corr[j].length * sc > near) >= company:
                continue
            avg = sum((corr[j] for j in neigh[i]), Vector()) / len(neigh[i])
            kd[i].co = base[i] + avg
            calmed += 1
    return calmed


def face_neighbours(me):
    by_edge = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            by_edge.setdefault(ek, []).append(p.index)
    nb = [set() for _ in me.polygons]
    for fs in by_edge.values():
        for a in fs:
            for b in fs:
                if a != b:
                    nb[a].add(b)
    return nb


TEXTURE_CHROMA_BOOST = 1.5   # Tripo's Remove Lighting pales colours: the Dock Runner's sand shirt matched the sage
EXACT_DE = 4.0               # a texture colour this close (Lab) to a palette colour is that colour
GREY_CHROMA = 6.0            # a face this grey only takes the palette's greys (buckles went teal)


def snap_to_palette(pal_lab, c, nearest):
    """The palette colour of one face, with the texture's saturation brought back up before matching."""
    # a face already painted in a palette colour keeps it: the owner recoloured Dock Runner's cape to #BE7D3C and the
    # boost turned it into the brighter #DB8040
    exact = nearest(pal_lab, c)
    if sum((x - y) ** 2 for x, y in zip(pal_lab[exact], c)) ** 0.5 < EXACT_DE:
        return exact
    chroma = (c[1] ** 2 + c[2] ** 2) ** 0.5
    if chroma < GREY_CHROMA:
        greys = [i for i, p in enumerate(pal_lab) if (p[1] ** 2 + p[2] ** 2) ** 0.5 < 2 * GREY_CHROMA]
        if greys:
            return greys[nearest([pal_lab[i] for i in greys], c)]
    return nearest(pal_lab, (c[0], c[1] * TEXTURE_CHROMA_BOOST, c[2] * TEXTURE_CHROMA_BOOST))


def clean_colours(me, cols, pal_lab, to_lab, nearest, areas, min_patch=0.0):
    """Flat colours without speckle (the Tripo texture bakes in shading, so neighbouring faces of one material snap
    to different palette shades): (1) each face's colour is averaged with neighbours of a similar colour (never
    across a real colour edge), (2) palette shades too close to tell apart are merged into the one used most, (3)
    after snapping, a face that differs from nearly all its neighbours takes theirs."""
    nb = face_neighbours(me)
    lab = [to_lab(c) for c in cols]

    def dist(a, b):
        return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5
    for _ in range(3):
        new = []
        for i, c in enumerate(lab):
            acc, w = [c[0] * areas[i], c[1] * areas[i], c[2] * areas[i]], areas[i]
            for j in nb[i]:
                if dist(lab[j], c) < 14:
                    acc = [acc[k] + lab[j][k] * areas[j] for k in range(3)]
                    w += areas[j]
            new.append(tuple(x / max(w, 1e-12) for x in acc))
        lab = new
    lab_pal = [snap_to_palette(pal_lab, c, nearest) for c in lab]
    used = {}
    for i, p in enumerate(lab_pal):
        used[p] = used.get(p, 0.0) + areas[i]
    order = sorted(used, key=lambda p: -used[p])
    merge = {}
    for p in order:
        tgt = next((q for q in order if q in merge.values() and dist(pal_lab[p], pal_lab[q]) < 12), None)
        merge[p] = tgt if tgt is not None else p
    lab_pal = [merge[p] for p in lab_pal]
    for _ in range(2):
        out = list(lab_pal)
        for i, p in enumerate(lab_pal):
            if not nb[i]:
                continue
            cnt = {}
            for j in nb[i]:
                cnt[lab_pal[j]] = cnt.get(lab_pal[j], 0) + 1
            q, c = max(cnt.items(), key=lambda kv: kv[1])
            if q != p and c >= max(2, int(0.66 * len(nb[i]) + 0.5)) and cnt.get(p, 0) == 0:
                out[i] = q
        lab_pal = out
    if min_patch:
        lab_pal = drop_specks(nb, lab_pal, pal_lab, areas, min_patch)
    return lab_pal


SPECK_CM2 = 5.0   # owner 23 Oct: the Dock Runner's shoulders were peppered with orange and brown specks; the art has a
                  # few clean tabs. A colour patch smaller than this that is not a piece of its own takes its border's colour.


def drop_specks(nb, lab_pal, pal_lab, areas, min_patch):
    """(4) Patches of one colour smaller than min_patch take the colour they share the most edges with, unless the
    patch is a whole loose piece (a real tab or strap) or a grey (snaps and buckles are small on purpose)."""
    island = [-1] * len(lab_pal)
    for s in range(len(lab_pal)):
        if island[s] < 0:
            island[s] = s
            st = [s]
            while st:
                f = st.pop()
                for g in nb[f]:
                    if island[g] < 0:
                        island[g] = s
                        st.append(g)
    island_area = {}
    for i, a in enumerate(areas):
        island_area[island[i]] = island_area.get(island[i], 0.0) + a
    grey = {i for i, p in enumerate(pal_lab) if (p[1] ** 2 + p[2] ** 2) ** 0.5 < 2 * GREY_CHROMA}
    for _ in range(3):
        seen = [False] * len(lab_pal)
        changed = 0
        for s in range(len(lab_pal)):
            if seen[s]:
                continue
            seen[s] = True
            patch, st = [s], [s]
            while st:
                f = st.pop()
                for g in nb[f]:
                    if not seen[g] and lab_pal[g] == lab_pal[s]:
                        seen[g] = True
                        st.append(g)
                        patch.append(g)
            area = sum(areas[f] for f in patch)
            if area >= min_patch or lab_pal[s] in grey or area >= island_area[island[s]] * (1 - 1e-6):
                continue
            inside, border = set(patch), {}
            for f in patch:
                for g in nb[f]:
                    if g not in inside:
                        border[lab_pal[g]] = border.get(lab_pal[g], 0) + 1
            if border:
                q = max(border.items(), key=lambda kv: kv[1])[0]
                for f in patch:
                    lab_pal[f] = q
                changed += 1
        if not changed:
            break
    return lab_pal


def mirror_colours(src, src_pal, dst):
    """The colour of each face of `dst` taken from the face of `src` mirrored across the centre line nearest it."""
    kd = KDTree(len(src.data.polygons))
    for p in src.data.polygons:
        c = p.center
        kd.insert(Vector((-c.x, c.y, c.z)), p.index)
    kd.balance()
    return [src_pal[kd.find(p.center)[1]] for p in dst.data.polygons]


def palette_cells(parts, fcol_by_slot, palette, spec, to_lab, nearest, assign_cells, mirror=()):
    """Each part's faces onto its slot's own Outfit cells, in the set's palette colours. Returns the theme. `mirror`:
    (left, right) slot pairs whose right part takes the left's colours face by face (owner, 23 Oct 2026: a tab on Dock
    Runner's gloves snapped to #DBB07D on one hand and #B8935C on the other)."""
    pal_lab = [to_lab(c) for c in palette]
    theme = {}
    face_pals = {}
    bpy.context.view_layer.update()                # the parts were just scaled to Synty's centimetres
    for slot, ob in parts.items():
        areas = [p.area for p in ob.data.polygons]
        s2 = ob.matrix_world.to_scale()[0] ** 2
        face_pals[slot] = clean_colours(ob.data, fcol_by_slot[slot], pal_lab, to_lab, nearest, areas,
                                        SPECK_CM2 * 1e-4 / s2)
    for a_, b_ in mirror:
        if a_ in parts and b_ in parts:
            face_pals[b_] = mirror_colours(parts[a_], face_pals[a_], parts[b_])
    for slot, ob in parts.items():
        me = ob.data
        areas = [p.area for p in me.polygons]
        face_pal = face_pals[slot]
        fc, used = assign_cells(spec, {slot: list(range(len(me.polygons)))}, face_pal, palette, areas)
        for name, info in used[slot].items():
            theme[name] = info["colour"]
        for uvl in list(me.uv_layers):
            me.uv_layers.remove(uvl)
        uvl = me.uv_layers.new(name="map1")
        for p in me.polygons:
            c = kn.cell_uv(*fc[p.index], inset=kn.SIDEKICK_INSET)
            for li in p.loop_indices:
                uvl.data[li].uv = c
        kn.set_hard_edges(me, 40, [fc[p.index] for p in me.polygons])
    return theme


def record_theme(set_code, piece, lineage, pal_path, palette, theme):
    """The growing Krytical Nova gear swatch (owner, 22 Oct 2026: one swatch of our own for gear and weapons; dyes
    are designed from it): every set's 24 palette colours and the cells its theme fills, plus a picture."""
    sw = kn.swatch_dir()
    path = sw / "KN_GearSwatch.json"
    data = json.loads(path.read_text(encoding="utf-8")) if path.is_file() else {
        "_about": "The Krytical Nova gear swatch: one row per set, its 24 palette colours (from the owner's palette "
                  "picture) and the Sidekick cells its theme fills. Grows with every set; dyes are designed from it. "
                  "Written by gear_second_pass.py.", "sets": {}}
    hexes = ["#%02X%02X%02X" % tuple(int(round(x * 255)) for x in c) for c in palette]
    e = data["sets"].setdefault(set_code, {"lineage": lineage, "palette_picture": pal_path.name, "pieces": {}, "theme": {}})
    e["palette"] = hexes
    e["pieces"][piece] = sorted(theme)
    e["theme"].update(theme)
    path.write_text(json.dumps(data, indent=1), encoding="utf-8")
    rows = list(data["sets"].values())
    cw = 24
    img = np.ones((len(rows) * cw, 24 * cw, 4), np.float32)
    for r, s in enumerate(rows):
        for c, hx in enumerate(s["palette"][:24]):
            y = (len(rows) - 1 - r) * cw
            img[y:y + cw, c * cw:(c + 1) * cw, :3] = [int(hx[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    out = bpy.data.images.new("T_KN_GearSwatch", img.shape[1], img.shape[0], alpha=True)   # 8-bit: pixels are stored as sRGB
    out.pixels = img.ravel()
    out.filepath_raw = str(sw / "T_KN_GearSwatch.png")
    out.file_format = "PNG"
    out.save()


def preview_with_theme(parts, theme, spec):
    """A preview material: Synty's colour map with the set's theme in its cells, as the game paints it."""
    base = bpy.data.images.load(str(kn.colour_map_path()), check_existing=False)
    w, h = base.size
    px = np.array(base.pixels[:], np.float32).reshape(h, w, 4)
    cells = kn.cell_lookup(spec)
    cs = w // 16
    for name, hx in theme.items():
        u, v = cells[kn.cell_key(name)]
        rgb = [int(hx[i:i + 2], 16) / 255 for i in (1, 3, 5)]
        px[v * cs:(v + 1) * cs, u * cs:(u + 1) * cs, :3] = [srgb_to_lin(x) for x in rgb] if base.is_float else rgb
    base.pixels = px.ravel()
    base.pack()
    mat = bpy.data.materials.new("KN_GearPreview")
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = base
    tex.interpolation = "Closest"
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "map1"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
    for ob in parts.values():
        ob.data.materials.clear()
        ob.data.materials.append(mat)


def shapes_sheet(body, parts, path, size=420):
    """Columns: male, male heavy, male muscular + defined, male bust + butt, female, female bust + butt, female heavy;
    rows: front, side, back. The body parts each armor part replaces are not drawn."""
    cols = [("male", {"defaultBuff": 0.5}), ("male heavy", {"defaultBuff": 0.5, "defaultHeavy": 1}),
            ("male muscular+defined", {"defaultBuff": 1, "kn_defined": 1}),
            ("male bust+butt", {"defaultBuff": 0.5, "kn_bust": 1, "kn_butt": 1}),
            ("female", {"masculineFeminine": 1, "defaultBuff": 0.5}),
            ("female bust+butt", {"masculineFeminine": 1, "defaultBuff": 0.5, "kn_bust": 1, "kn_butt": 1}),
            ("female heavy", {"masculineFeminine": 1, "defaultBuff": 0.5, "defaultHeavy": 1})]
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.view_settings.view_transform = "Standard"
    if sc.world is None:
        w = bpy.data.worlds.new("kn_w")
        w.use_nodes = True
        w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1)
        sc.world = w
    if not any(o.type == "LIGHT" for o in sc.objects):
        for rot in ((50, 0, -30), (60, 0, 150)):
            l = bpy.data.objects.new("kn_sun", bpy.data.lights.new("kn_sun", "SUN"))
            l.data.energy = 2.5
            l.rotation_euler = [math.radians(r) for r in rot]
            sc.collection.objects.link(l)
    skin = bpy.data.materials.get("kn_body_preview") or kn.preview_material(kn.colour_map_path(), name="kn_body_preview", uv_layer="map1")
    covered = {p.name.split("_")[4] for p in parts}
    for o in body:
        if not o.data.materials or o.data.materials[0] != skin:
            o.data.materials.clear()
            o.data.materials.append(skin)
        o.hide_render = o.name.split("_")[4] in covered
    cam = sc.camera
    if cam is None:
        cd = bpy.data.cameras.new("kn_c")
        cd.type = "ORTHO"
        cam = bpy.data.objects.new("kn_c", cd)
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    ctr = Vector((0, 0, 0.95))
    tmp = Path(tempfile.mkdtemp(prefix="kn_ss_"))
    meshes = body + parts
    rows = [[], [], []]
    for label, vals in cols:
        for o in meshes:
            ks = o.data.shape_keys
            if ks:
                for kb in ks.key_blocks[1:]:
                    kb.value = vals.get(kb.name, 0.0)
        bpy.context.view_layer.update()
        for r, d in enumerate(((0, -1, 0.05), (1, 0, 0.05), (0, 1, 0.05))):
            d = Vector(d).normalized()
            cam.location = ctr + d * 20
            cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
            p = tmp / ("%d_%d.png" % (len(rows[r]), r))
            sc.render.filepath = str(p)
            bpy.ops.render.render(write_still=True)
            rows[r].append(p)
    gap = 6
    img = np.full((3 * size + 4 * gap, len(cols) * size + (len(cols) + 1) * gap, 4), 0.1, np.float32)
    img[..., 3] = 1
    for r, row in enumerate(rows):
        for c, p in enumerate(row):
            im = bpy.data.images.load(str(p))
            y = (2 - r) * size + (3 - r) * gap
            x = c * size + (c + 1) * gap
            img[y:y + size, x:x + size] = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
            bpy.data.images.remove(im)
    out = bpy.data.images.new("kn_sheet", img.shape[1], img.shape[0], alpha=True)
    out.pixels = img.ravel()
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save()
    for o in meshes:
        if o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks:
                kb.value = 0.0
        o.hide_render = False


# Close-ups of the other gear slots: where each goes wrong (a cuff on the wrist, a boot's top round the calf, a cape
# at the shoulders and behind the legs, a helmet round the face and the back of the neck).
CLOSEUP = {
    "helmet": {"ctr": (0.0, 0.0, 1.6), "ortho": 0.5,
               "dirs": ((0, -1, 0.05), (1, 0, 0.05), (0, 1, 0.05), (0.6, -0.8, -0.5))},
    "gloves": {"ctr": (0.8, 0.0, 1.35), "ortho": 0.42,
               "dirs": ((0, -1, 0.05), (0.2, -0.3, 1), (0, 1, 0.05), (-0.6, -0.4, -0.7))},
    "boots": {"ctr": (0.12, 0.0, 0.16), "ortho": 0.42,
              "dirs": ((0, -1, 0.05), (1, 0, 0.05), (0, 1, 0.05), (0.6, -0.8, 0.6))},
    "cape": {"ctr": (0.0, 0.05, 1.15), "ortho": 1.3,
             "dirs": ((0.6, -1, 0.15), (1, 0, 0.05), (0, 1, 0.05), (0.7, 0.6, -0.5))},
}


def closeup_sheet(body, parts, path, size=520, ctr=(0.12, 0, 1.3), ortho=0.75,
                  dirs=((0, -1, 0.05), (0.6, -1, 0.15), (0, 1, 0.05), (0.35, -0.2, -1))):
    """Upper body close up (front, three-quarter, back, from below the arm), male then female: the armpits and the
    sleeve openings, where an A-pose model goes wrong. Gloves, boots and capes pass their own (CLOSEUP)."""
    sc = bpy.context.scene
    cam = sc.camera
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho
    covered = {p.name.split("_")[4] for p in parts}
    for o in body:
        o.hide_render = o.name.split("_")[4] in covered
    ctr = Vector(ctr)
    tmp = Path(tempfile.mkdtemp(prefix="kn_cu_"))
    old = (sc.render.resolution_x, sc.render.resolution_y)
    sc.render.resolution_x = sc.render.resolution_y = size
    rows = []
    for vals in ({"defaultBuff": 0.5}, {"masculineFeminine": 1, "defaultBuff": 0.5}):
        for o in body + parts:
            ks = o.data.shape_keys
            if ks:
                for kb in ks.key_blocks[1:]:
                    kb.value = vals.get(kb.name, 0.0)
        bpy.context.view_layer.update()
        row = []
        for i, d in enumerate(dirs):
            d = Vector(d).normalized()
            cam.location = ctr + d * 10
            cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
            p = tmp / f"{len(rows)}_{i}.png"
            sc.render.filepath = str(p)
            bpy.ops.render.render(write_still=True)
            row.append(p)
        rows.append(row)
    gap = 6
    img = np.full((2 * size + 3 * gap, 4 * size + 5 * gap, 4), 0.1, np.float32)
    img[..., 3] = 1
    for r, row in enumerate(rows):
        for c, p in enumerate(row):
            im = bpy.data.images.load(str(p))
            y = (1 - r) * size + (2 - r) * gap
            x = c * size + (c + 1) * gap
            img[y:y + size, x:x + size] = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
            bpy.data.images.remove(im)
    out = bpy.data.images.new("kn_cu", img.shape[1], img.shape[0], alpha=True)
    out.pixels = img.ravel()
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    out.save()
    sc.render.resolution_x, sc.render.resolution_y = old
    for o in body + parts:
        if o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks:
                kb.value = 0.0
        o.hide_render = False


def run_fit(a, src, piece, piece_name, lineage, rig, body, fcol, spec, report, problems, warnings, g):
    """g: the second-pass module's helpers (evaluated_soup, weights_at, region_of_bone, colour functions, BARE)."""
    if getattr(a, "slot", "armor") != "armor":
        return run_fit_piece(a, src, piece, piece_name, lineage, rig, body, fcol, spec, report, problems, warnings, g)
    # 3. Weights from the T-pose body, pushed clear of it, split into Synty's slots.
    import os
    if not os.environ.get("KN_NO_LIMBS"):
        report["limbs"] = fit_limbs(piece, body, rig, g["evaluated_soup"], lineage)
    if lineage != "Robot":
        report["armhole_edge_vertices_closed"] = close_armholes(piece, body, rig, g["evaluated_soup"])
    if lineage not in ("Robot", "Skeleton"):
        report["underarm_gusset_faces"] = underarm_gusset(piece, body, rig, g["evaluated_soup"], fcol)
    if lineage != "Robot":
        report["waist_cinched_vertices"] = cinch_waist(piece, body, rig, g["evaluated_soup"])
        report["waist_cells"] = cinch_waist.stats
    # robot plates are hard: no cuff drawn in round the arm (Synty's robot arms are rigid chassis too)
    report["sleeve_ends_drawn_in"] = 0 if lineage == "Robot" else tighten_sleeves(
        piece, body, rig, g.get("sleeve_verts"), g["evaluated_soup"])
    if lineage in LIFT_UNDERSIDE:
        report["sleeve_undersides_lifted"] = lift_undersides(piece, body, rig, g.get("sleeve_verts"),
                                                             g["evaluated_soup"])
    weights = rest_weights(piece, body, g["evaluated_soup"], g["weights_at"])
    report["pushed_clear_vertices"] = conform(piece, body, g["evaluated_soup"], rigid=lineage == "Robot")
    report["floaters_seated"] = seat_floaters(piece)
    weights = rest_weights(piece, body, g["evaluated_soup"], g["weights_at"])
    me = piece.data
    col_attr = me.attributes.new("kn_col", "FLOAT_COLOR", "FACE")
    for i, c in enumerate(fcol):
        col_attr.data[i].color = (c[0], c[1], c[2], 1.0)
    if lineage in TRIM_PAST_CUFF:
        report["trimmed_past_cuff"] = trim_past_cuff(piece, rig, g.get("sleeve_verts") or set())
        weights = rest_weights(piece, body, g["evaluated_soup"], g["weights_at"])
    kd = KDTree(len(me.vertices))
    for v in me.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    parts = split_slots(piece, weights, g["region_of_bone"], body, g["evaluated_soup"])
    report["slots_folded_up"] = split_slots.folded       # slot: how much of its body part the armor covered
    _pack, _sp, species = g["BARE"][lineage]
    report["slots"] = {}
    fcol_by_slot = {}
    for slot, ob in parts.items():
        name = f"SK_KRYT_{a.set}_01_{slot}_{species}01"
        ob.name = ob.data.name = name
        bind_part(ob, rig, lambda co: weights[kd.find(co)[1]])
        fcol_by_slot[slot] = [tuple(ob.data.attributes["kn_col"].data[i].color[:3]) for i in range(len(ob.data.polygons))]
    piece.hide_set(True)
    piece.hide_render = True

    # 4. Rig space, every body key, pushed clear for both sexes.
    skin = [o for s, o in body.items()]
    for slot, ob in parts.items():
        to_rig_space(ob, rig)
        body_keys(ob, skin)
        transferred = snapshot_keys(ob)
        left = clear_by_keys(ob, skin, push=lineage != "Robot")
        calmed = calm_lone_pushes(ob, transferred)
        faces_pushed = clear_faces(ob, skin) if lineage != "Robot" else 0
        if calmed or faces_pushed:
            left = clear_by_keys(ob, skin, push=False)
        report["slots"][slot] = {"part": ob.name, "triangles": sum(len(p.vertices) - 2 for p in ob.data.polygons),
                                 "clipping_vertices_left": left, "lone_pushes_calmed": calmed,
                                 "face_dips_pushed": faces_pushed, "face_dips_left": count_dips(ob, skin)}

    # 5. The set's palette on each slot's own cells; the theme joins the gear swatch.
    pal_path = Path(a.palette) if a.palette else next(src.parent.parent.glob("* - Color Palette.png"), None)
    if pal_path is None:
        problems.append("No '<Set> - Color Palette.png' beside the piece's folder.")
    else:
        palette = g["read_palette"](pal_path)
        theme = palette_cells(parts, fcol_by_slot, palette, spec, g["to_lab"], g["nearest"], g["assign_cells"])
        report["theme_cells"] = theme
        if not getattr(a, "no_record", False):
            record_theme(a.set, piece_name, lineage, pal_path, palette, theme)
        preview_with_theme(parts, theme, spec)

    # 6. Every part carries the bare body of its own slot inside it, as Synty's skeleton outfits carry their bones:
    # the game hides the body part a slot replaces, so through any gap (an armpit seam, between robot plates)
    # nothing was behind the armor (owner, 23 Oct 2026: you could see through the character).
    if True:
        for slot, ob in parts.items():
            bone_part = body.get(slot)
            if bone_part is None:
                continue
            extra = bone_part.copy()
            extra.data = bone_part.data.copy()
            bpy.context.scene.collection.objects.link(extra)
            for k in ALL_KEYS:
                if not extra.data.shape_keys:
                    extra.shape_key_add(name="Basis", from_mix=False)
                if k not in extra.data.shape_keys.key_blocks:
                    extra.shape_key_add(name=k, from_mix=False)
            if ob.data.materials:
                extra.data.materials.clear()
                extra.data.materials.append(ob.data.materials[0])
            bpy.ops.object.select_all(action="DESELECT")
            extra.select_set(True)
            ob.select_set(True)
            bpy.context.view_layer.objects.active = ob
            bpy.ops.object.join()
            report["slots"][slot]["body_inside"] = bone_part.name
    return parts


# ------------------------------------------------------------------------------------------------ gloves, boots, cape

# The game wears these in Synty's slots (SidekickGearGenerator: HandLeft/HandRight, FootLeft/FootRight, AttachmentBack).
PIECE_SLOTS = {"gloves": ("15HNDL", "16HNDR"), "boots": ("20FOTL", "21FOTR"), "cape": ("24ABAC",),
               "helmet": ("22AHED",)}
LIMB_BONES = ("upperarm", "lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky", "thigh", "calf", "foot", "ball")
# A cape takes the trunk's weights and keys only: from the legs it kicked with every step, from the arms the cloth
# hanging below them swung into the chest as they came down. What hangs free gets dangle chains (gear_dangle.py).
CAPE_TRUNK = ("10TORS", "17HIPS")
CAPE_SKIN = ("10TORS", "17HIPS", "11AUPL", "12AUPR")
# What a piece is kept clear of. A glove replaces the hand (the game hides it) and Tripo's gloves are half-closed round
# a flat bare hand: pushed clear of that hand, Dock Runner's gloves broke into shards. Only the forearm under the cuff.
# A boot replaces the foot the same way: pushed clear of a foot whose sole lies level with the boot's, the tread tore
# into spikes. Only the lower leg above the boot's top.
# A helmet (owner, 23 Oct 2026: a vanity toggle on the head attachment slot) stays clear of the bare head only.
PUSH_CLEAR = {"gloves": ("13ALWL", "14ALWR"), "boots": ("18LEGL", "19LEGR"), "cape": CAPE_SKIN, "helmet": ("01HEAD",)}
BODY_OUT_SHARE = 0.05   # a bare hand or foot standing this much outside its part is left out of it, not joined in


def split_sides(piece, slots):
    """One object per slot: everything for a single slot, else each loose piece whole to the side (x >= 0 is the
    left) most of it lies on."""
    me = piece.data
    side = [0] * len(me.vertices)
    if len(slots) > 1:
        islands, _neigh = islands_of(me)
        for g in islands:
            k = 0 if sum(me.vertices[i].co.x for i in g) >= 0 else 1
            for i in g:
                side[i] = k
    parts = {}
    for k, slot in enumerate(slots):
        ob = piece.copy()
        ob.data = me.copy()
        bpy.context.scene.collection.objects.link(ob)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.verts.ensure_lookup_table()
        gone = [f for f in bm.faces if side[f.verts[0].index] != k]
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.update()
        ob.hide_set(False)
        ob.hide_render = False
        parts[slot] = ob
    return parts


def body_poking_out(ob, body_part, near=0.002):
    """Vertices of the bare body joined into a part that stand outside it (fingertips past a glove's), measured
    before the join: nearest point of the part, on its outer side by more than `near` metres."""
    me = ob.data
    mw = ob.matrix_world
    tree = BVHTree.FromPolygons([mw @ v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    bmw = body_part.matrix_world
    bv = [bmw @ v.co for v in body_part.data.vertices]      # the rest shape, every key at 0, as the part was bound
    out = 0
    for p in bv:
        loc, nrm, _i, d = tree.find_nearest(p)
        if loc is not None and (p - loc).dot(nrm) > near:
            out += 1
    return out, len(bv)


CAPE_SHOULDER_SHARE = 0.6   # of the upper arm, at most, for cloth lying over the top of it


def cape_shoulders(piece, rig, weights, reach=0.14):
    """A cape modelled over T-pose arms (Dock Runner's poncho) lies on top of the upper arms as caps: on trunk weights
    alone they stood out level when the arms came down, a box round the shoulders. Cloth over the top of an upper arm,
    out past the shoulder joint, follows that arm in part, more the further out it lies; cloth below the arm stays."""
    mw = rig.matrix_world
    me = piece.data
    n = 0
    for side, s_ in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s_}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s_}"].head
        u = (E - S).normalized()
        for v in me.vertices:
            p = v.co
            t = (p - S).dot(u)
            off = p - S - u * t
            if p.x * side <= 0 or t <= 0 or off.z < 0.0:
                continue
            k = min(1.0, t / reach)
            k = CAPE_SHOULDER_SHARE * k * k * (3 - 2 * k)
            w = weights[v.index]
            mixed = {b_: x * (1 - k) for b_, x in w.items()}
            mixed[f"upperarm_{s_}"] = mixed.get(f"upperarm_{s_}", 0.0) + k
            weights[v.index] = mixed
            n += 1
    return n


def clear_skin_points(ob, skin, margin=0.4, near=3.0, rounds=4):
    """The skin's own corners standing out through a part's faces (owner, 23 Oct 2026: light slivers down the back of
    Dock Runner's boots were the calf poking through the shaft's crease while every boot vertex sat clear of it): each
    skin vertex within `near` cm whose outward side lies beyond the part's surface pushes the part's face there out by
    the depth plus `margin` cm (corners, face middles and edge midpoints of the skin), for the male rest (every key) and the female rest (masculineFeminine). Mesh-local is
    rig-local centimetres here. Returns how many part vertices moved."""
    me = ob.data
    ks = me.shape_keys.key_blocks
    polys = [tuple(p.vertices) for p in me.polygons]
    moved = set()
    for _ in range(rounds):
        any_push = False
        for sex in ("M", "F"):
            vals = mc.rest_values({"sex": sex})
            pts, nrm = [], []
            for o in skin:
                co = mc.positions(o, vals)
                acc = [Vector() for _v in co]
                o.data.calc_loop_triangles()
                for t in o.data.loop_triangles:
                    a_, b_, c_ = (co[i] for i in t.vertices)
                    fn = (b_ - a_).cross(c_ - a_)
                    for i in t.vertices:
                        acc[i] += fn
                    # the skin's flat faces cross a concave crease between its corners: their middles and edge
                    # midpoints are tested too (the calf through the boot's back crease, no calf corner outside)
                    if fn.length > 1e-9:
                        fu = fn.normalized()
                        for q in ((a_ + b_ + c_) / 3, (a_ + b_) / 2, (b_ + c_) / 2, (c_ + a_) / 2):
                            pts.append(q)
                            nrm.append(fu)
                pts += co
                nrm += [n.normalized() if n.length > 1e-9 else None for n in acc]
            pco = mc.positions(ob, vals)
            tree = BVHTree.FromPolygons(pco, polys)
            push = {}
            for p_, n_ in zip(pts, nrm):
                if n_ is None:
                    continue
                # the outermost surface over the point: looked for from outside along the skin's normal (the boot's
                # back crease is two layers; the nearest face was the inner one and the calf stayed through the outer)
                loc, _fn, fi, d = tree.ray_cast(p_ + n_ * near, -n_, 2 * near)
                if loc is None:
                    continue
                depth = (p_ - loc).dot(n_)
                if depth <= -margin:
                    continue
                m = n_ * (depth + margin)
                for i in polys[fi]:
                    if i not in push or m.length > push[i].length:
                        push[i] = m
            if not push:
                continue
            any_push = True
            moved.update(push)
            if sex == "M":
                for kb in ks:
                    for i, m in push.items():
                        kb.data[i].co += m
            elif "masculineFeminine" in ks:
                for i, m in push.items():
                    ks["masculineFeminine"].data[i].co += m
        if not any_push:
            break
    for v, d in zip(me.vertices, ks[0].data):
        v.co = d.co
    return len(moved)


def head_clearance(ob, skin, margin=0.4, reach=3.0):
    """A helmet kept clear of the bare head on every body key without tearing its liner: per sex and body state, the
    head's face middles standing out through the shell's outer surface are found by looking in from outside, and the
    shell round each is eased out from the head's middle to `margin` cm beyond it, falling off smoothly over `reach` cm
    (the whole shell scaled for one spot under Dock Runner's neck flap came out 7 % too big), on the keys of that state.
    Returns per state how many points came through and the most it moved (cm)."""
    import math as _m
    me = ob.data
    ks = me.shape_keys.key_blocks
    polys = [tuple(p.vertices) for p in me.polygons]
    out = {}
    for sex in ("M", "F"):
        rest = mc.rest_values({"sex": sex})
        for label, change in [("rest", {})] + (mc.STATES_F if sex == "F" else mc.STATES_M):
            vals = dict(rest, **change)
            hv = mc.positions(skin[0], vals)
            centre = sum(hv, Vector()) / len(hv)
            co = mc.positions(ob, vals)
            tree = BVHTree.FromPolygons(co, polys)
            hits = []
            skin[0].data.calc_loop_triangles()
            for t in skin[0].data.loop_triangles:
                a_, b_, c_ = (hv[i] for i in t.vertices)
                fn = (b_ - a_).cross(c_ - a_)
                if fn.length < 1e-9:
                    continue
                p_, n_ = (a_ + b_ + c_) / 3, fn.normalized()
                # from well outside, so the shell is met and not the liner padded inside it
                loc, _fn, _fi, _d = tree.ray_cast(p_ + n_ * 15.0, -n_, 30.0)
                # only skin through the shell right there (under the open front the ray went on to the back of the cap)
                if loc is None or not -3.0 < (loc - p_).dot(n_) < 0:
                    continue
                gap = (p_ - centre).length + margin - (loc - centre).length
                if gap > 0:
                    hits.append((loc, gap))
            if not hits:
                continue
            move = []
            for c in co:
                best = 0.0
                for loc, gap in hits:
                    w = _m.exp(-((c - loc).length / reach) ** 2)
                    best = max(best, gap * w)
                move.append((c - centre).normalized() * best if best > 1e-4 else None)
            keys = [k for k, v in change.items() if k in ks and v != rest.get(k, 0.0)]
            if not keys and sex == "F":
                keys = ["masculineFeminine"]
            targets = [ks[k] for k in keys] if keys else list(ks)
            for kb in targets:
                w = (abs(change.get(kb.name, 1.0) - rest.get(kb.name, 0.0)) or 1.0) if keys else 1.0
                for i, m in enumerate(move):
                    if m is not None:
                        kb.data[i].co += m / (len(targets) if keys else 1) / w
            out[f"{sex} {label}"] = {"through": len(hits), "most_cm": round(max(g for _l, g in hits), 2)}
    for v, d in zip(me.vertices, ks[0].data):
        v.co = d.co
    return out or "clear on every state"


def follow_limb(piece, rig, body, weights, g, slot, span=0.03):
    """Owner, 23 Oct 2026: glove cuffs sank 3.6 cm into the forearm as the wrist turned, boot tops 4.6 cm into the calf
    as the knee bent. A cuff behind the wrist takes the forearm skin's weights (its twist bones), blended in over `span`
    metres either side of the joint; a boot's shaft takes the lower leg skin's, from nothing at the ankle to all of it at
    the boot's top. Returns how many vertices changed."""
    mw = rig.matrix_world
    me = piece.data
    n = 0
    for side, s_ in ((1, "l"), (-1, "r")):
        if slot == "gloves":
            limb = body.get("13ALWL" if side == 1 else "14ALWR")
        else:
            limb = body.get("18LEGL" if side == 1 else "19LEGR")
        if limb is None:
            continue
        lv, lt, lo = g["evaluated_soup"]([limb])
        tree = BVHTree.FromPolygons(lv, lt)
        mine = [v for v in me.vertices if v.co.x * side > 0]
        if slot == "gloves":
            W = mw @ rig.pose.bones[f"hand_{s_}"].head
            u = (W - mw @ rig.pose.bones[f"lowerarm_{s_}"].head).normalized()

            def share(p, W=W, u=u):
                return 1.0 - max(0.0, min(1.0, ((p - W).dot(u) + span) / (2 * span)))
        else:
            ankle = (mw @ rig.pose.bones[f"foot_{s_}"].head).z
            top = max((v.co.z for v in mine), default=ankle + 0.1)

            def share(p, ankle=ankle, top=top):
                return max(0.0, min(1.0, (p.z - ankle) / max(0.02, top - ankle)))
        for v in mine:
            k = share(v.co)
            k = k * k * (3 - 2 * k)
            if k <= 1e-3:
                continue
            loc, _nn, idx, _d = tree.find_nearest(v.co)
            tri = lt[idx]
            lw = g["weights_at"](lo, tri, poly_3d_calc([lv[i] for i in tri], loc))
            w = weights[v.index]
            mixed = {b: x * (1 - k) for b, x in w.items()}
            for b, x in lw.items():
                mixed[b] = mixed.get(b, 0.0) + x * k
            tot = sum(mixed.values()) or 1.0
            weights[v.index] = {b: x / tot for b, x in mixed.items()}
            n += 1
    return n


ARM_RADIUS = {"upperarm": 0.055, "lowerarm": 0.045, "hand": 0.055}   # metres round each segment, gloves included
ARM_CLEAR = 0.015


def clear_arm_swing(ob, rig, g, cap=0.08):
    """Owner, 23 Oct 2026: the arms passed through Dock Runner's poncho's front and side panels as they hung or swung
    (guitar, dust). The arms are posed down at 60-88 degrees, swung forward and back, elbows straight and bent; any cape
    vertex below the shoulders that comes within an arm's thickness plus ARM_CLEAR of a segment is moved out, away from
    the arm's line, by the most any pose needs, mapped back through its own skinning to the rest shape, spread over its
    neighbours and capped at `cap` metres. The panels keep their cut; only where an arm hangs do they stand off."""
    mw = rig.matrix_world
    me = ob.data
    names = [vg.name for vg in ob.vertex_groups]
    vw = [{names[x.group]: x.weight for x in v.groups if x.weight > 1e-5} for v in me.vertices]
    sh_z = (mw @ rig.pose.bones["upperarm_l"].head).z
    need = {}
    dg = bpy.context.evaluated_depsgraph_get()
    for down in (60, 75, 88):
        for fwd in (-0.45, 0.0, 0.3):
            for bend in (0, 50):
                for side, s_ in ((1, "l"), (-1, "r")):
                    d_up = Vector((side * math.cos(math.radians(down)), fwd, -math.sin(math.radians(down)))).normalized()
                    g["aim_bone"](rig, f"upperarm_{s_}", d_up)
                    b = math.radians(bend)
                    g["aim_bone"](rig, f"lowerarm_{s_}",
                                  (d_up * math.cos(b) + Vector((0, -1, 0)) * math.sin(b)).normalized())
                bpy.context.view_layer.update()
                segs = []
                for s_ in ("l", "r"):
                    P = [mw @ rig.pose.bones[f"{b_}_{s_}"].head for b_ in ("upperarm", "lowerarm", "hand")]
                    tip = P[2] + (P[2] - P[1]).normalized() * 0.18
                    segs += [(P[0], P[1], ARM_RADIUS["upperarm"]), (P[1], P[2], ARM_RADIUS["lowerarm"]),
                             (P[2], tip, ARM_RADIUS["hand"])]
                M = {pb.name: (mw @ pb.matrix) @ (mw @ pb.bone.matrix_local).inverted() for pb in rig.pose.bones}
                dg = bpy.context.evaluated_depsgraph_get()
                ev = ob.evaluated_get(dg)
                em = ev.to_mesh()
                pos = [ob.matrix_world @ v.co for v in em.vertices]
                ev.to_mesh_clear()
                for i, p in enumerate(pos):
                    if me.vertices[i].co.z > sh_z - 0.04:
                        continue
                    for A, B, r in segs:
                        ab = B - A
                        t = max(0.0, min(1.0, (p - A).dot(ab) / ab.length_squared))
                        off = p - (A + ab * t)
                        dist = off.length
                        want = r + ARM_CLEAR
                        if dist >= want or dist < 1e-6:
                            continue
                        # out from the body's centre line, level: pushed radially from each arm line the panels
                        # crumpled, every vertex going its own way
                        out = Vector((p.x, p.y - 0.02, 0.0))
                        out = out.normalized() if out.length > 1e-4 else off.normalized()
                        push = out * ((want - dist) / max(0.35, out.dot(off.normalized())))
                        blend = Matrix(((0, 0, 0), (0, 0, 0), (0, 0, 0)))
                        for bn, x in vw[i].items():
                            if bn in M:
                                blend = blend + M[bn].to_3x3() * x
                        try:
                            push = blend.inverted() @ push
                        except ValueError:
                            pass
                        if i not in need or push.length > need[i].length:
                            need[i] = push
                for pb in rig.pose.bones:
                    pb.matrix_basis = Matrix.Identity(4)
                bpy.context.view_layer.update()
    if not need:
        return {"vertices": 0}
    adj = [[] for _ in me.vertices]
    for e in me.edges:
        a_, b_ = e.vertices
        adj[a_].append(b_)
        adj[b_].append(a_)
    # a smooth field: averaged over the cloth, never below what a vertex needs
    push = [need.get(i, Vector()) for i in range(len(me.vertices))]
    for _ in range(12):
        nxt = []
        for i in range(len(me.vertices)):
            avg = (push[i] + sum((push[j] for j in adj[i]), Vector())) / (1 + len(adj[i]))
            nxt.append(need[i] if i in need and need[i].length > avg.length else avg)
        push = nxt
    push = {i: m for i, m in enumerate(push) if m.length > 1e-4}
    worst = 0.0
    for i, m in push.items():
        if m.length > cap:
            m = m.normalized() * cap
        me.vertices[i].co += m
        worst = max(worst, m.length)
    me.update()
    return {"vertices": len(push), "needed_by": len(need), "most_cm": round(100 * worst, 1)}


def run_fit_piece(a, src, piece, piece_name, lineage, rig, body, fcol, spec, report, problems, warnings, g):
    """The fit for gloves, boots and a cape (see gear_second_pass.other_piece)."""
    slot = a.slot
    ev = g["evaluated_soup"]
    clear_of = {s_: o for s_, o in body.items() if s_ in PUSH_CLEAR.get(slot, body.keys())}
    report["pushed_clear_vertices"] = conform(piece, clear_of, ev, rigid=lineage == "Robot")
    report["floaters_seated"] = seat_floaters(piece)
    if slot == "cape":
        trunk = {s: o for s, o in body.items() if s in CAPE_TRUNK}
        weights = rest_weights(piece, trunk, ev, g["weights_at"])
        for i, w in enumerate(weights):
            keep = {b: x for b, x in w.items() if not b.startswith(LIMB_BONES) and x > 1e-6}
            if not keep:
                keep = {"spine_03": 1.0}
            tot = sum(keep.values())
            weights[i] = {b: x / tot for b, x in keep.items()}
        skin = [o for s, o in body.items() if s in CAPE_SKIN]
        report["cape_shoulder_vertices"] = cape_shoulders(piece, rig, weights)
    elif slot == "helmet":
        # rigid on the head attachment bone, as Synty's helmets are (headAttach rides the head)
        bone = "headAttach" if "headAttach" in rig.data.bones else "head"
        weights = [{bone: 1.0} for _v in piece.data.vertices]
        skin = [body["01HEAD"]]
    else:
        weights = rest_weights(piece, body, ev, g["weights_at"])
        skin = list(body.values())
        if slot == "gloves":
            report["cuff_vertices_on_forearm"] = follow_limb(piece, rig, body, weights, g, "gloves")
        elif slot == "boots":
            report["shaft_vertices_on_shin"] = follow_limb(piece, rig, body, weights, g, "boots")
    push_skin = list(clear_of.values())
    me = piece.data
    col_attr = me.attributes.new("kn_col", "FLOAT_COLOR", "FACE")
    for i, c in enumerate(fcol):
        col_attr.data[i].color = (c[0], c[1], c[2], 1.0)
    kd = KDTree(len(me.vertices))
    for v in me.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    parts = split_sides(piece, PIECE_SLOTS[slot])
    _pack, _sp, species = g["BARE"][lineage]
    report["slots"] = {}
    fcol_by_slot = {}
    for s, ob in parts.items():
        name = f"SK_KRYT_{a.set}_01_{s}_{species}01"
        ob.name = ob.data.name = name
        bind_part(ob, rig, lambda co: weights[kd.find(co)[1]])
        fcol_by_slot[s] = [tuple(ob.data.attributes["kn_col"].data[i].color[:3]) for i in range(len(ob.data.polygons))]
    piece.hide_set(True)
    piece.hide_render = True
    if slot == "cape":
        report["arm_swing_cleared"] = clear_arm_swing(parts["24ABAC"], rig, g)
    for s, ob in parts.items():
        to_rig_space(ob, rig)
        body_keys(ob, skin)
        transferred = snapshot_keys(ob)
        # a helmet is a padded shell with a liner inside: pushed vertex by vertex its liner came out through the shell
        # in shards over the brow. It keeps the head's own key motion and moves whole (head_clearance)
        hard = lineage == "Robot" or slot == "helmet"
        left = clear_by_keys(ob, push_skin, push=not hard)
        calmed = calm_lone_pushes(ob, transferred) if not hard else 0
        faces_pushed = clear_faces(ob, push_skin) if not hard else 0
        skin_out = clear_skin_points(ob, push_skin) if slot in ("gloves", "boots") else 0
        if slot == "helmet":
            report["helmet_clearance"] = head_clearance(ob, push_skin)
            left = clear_by_keys(ob, push_skin, push=False)
        if calmed or faces_pushed or skin_out:
            left = clear_by_keys(ob, push_skin, push=False)
        report["slots"][s] = {"part": ob.name, "triangles": sum(len(p.vertices) - 2 for p in ob.data.polygons),
                              "kept_clear_of": [o.name.split("_")[4] for o in push_skin],
                              "pushed_over_skin_corners": skin_out,
                              "clipping_vertices_left": left, "lone_pushes_calmed": calmed,
                              "face_dips_pushed": faces_pushed, "face_dips_left": count_dips(ob, push_skin)}
    pal_path = Path(a.palette) if a.palette else next(src.parent.parent.glob("* - Color Palette.png"), None)
    if pal_path is None:
        problems.append("No '<Set> - Color Palette.png' beside the piece's folder.")
    else:
        palette = g["read_palette"](pal_path)
        pairs = [PIECE_SLOTS[slot]] if len(PIECE_SLOTS[slot]) == 2 else []
        theme = palette_cells(parts, fcol_by_slot, palette, spec, g["to_lab"], g["nearest"], g["assign_cells"], pairs)
        report["theme_cells"] = theme
        if not getattr(a, "no_record", False):
            record_theme(a.set, piece_name, lineage, pal_path, palette, theme)
        preview_with_theme(parts, theme, spec)
    # the bare hand or foot inside, as every part carries its slot's body (the game hides the one it replaces)
    for s, ob in parts.items():
        bone_part = body.get(s)
        if bone_part is None:
            continue
        out, of = body_poking_out(ob, bone_part)
        report["slots"][s]["body_vertices_outside_the_part"] = f"{out} of {of}"
        if out > BODY_OUT_SHARE * of:
            report["slots"][s]["body_inside"] = None
            warnings.append(f"{s}: the bare body part is not joined inside: {out} of {of} of its vertices stand outside "
                            "the part (a half-closed glove round a flat hand, a sole level with the foot's). The game "
                            "hides it; only the piece's own opening looks inside.")
            continue
        extra = bone_part.copy()
        extra.data = bone_part.data.copy()
        bpy.context.scene.collection.objects.link(extra)
        for k in ALL_KEYS:
            if not extra.data.shape_keys:
                extra.shape_key_add(name="Basis", from_mix=False)
            if k not in extra.data.shape_keys.key_blocks:
                extra.shape_key_add(name=k, from_mix=False)
        if ob.data.materials:
            extra.data.materials.clear()
            extra.data.materials.append(ob.data.materials[0])
        bpy.ops.object.select_all(action="DESELECT")
        extra.select_set(True)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.join()
        report["slots"][s]["body_inside"] = bone_part.name
    return parts
