"""Second pass on a gear piece: T-pose fit, conform, Synty slots, weights, every body key, swatch colours, export.

  run_blender.py gear_second_pass.py --fbx "<Drive>/.../<Piece>/<Piece>_Revised.fbx" --set DOCK
        [--lineage Civilian]      default: the folder above the piece's folder
        [--palette "<Set> - Color Palette.png"]   default: the "* - Color Palette.png" in the lineage folder
        [--stage pose|fit]        pose: stop after the T-pose; fit (default): the whole second pass, saved as a working scene
        [--work <file.blend>]     keep the working scene (default <Drive art root>/Sidekick/Gear/<Piece>.blend)
        [--out-dir <folder>]      default Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/<Piece>
        [--sheet <png>]

Parts are named SK_KRYT_<SET>_01_<slot>_<SP>01 (SP: HU for humans and elves, RO robots, SN skeletons, as Synty names
its outfit parts). Owner's rules, 22 Oct 2026: one swatch, no per-part textures; the set's palette picture is its
colour theme (and a dye for gear and weapons); gear works on both sexes, every slider, emotes and combat.
"""
import argparse
import colorsys
import json
import math
import os
import re
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import kn_fbx
from sidekick_export_settings import EXPORT

BARE = {"Civilian": ("HUMN_BASE_01", "HU01", "HU"), "Soldier": ("HUMN_BASE_01", "HU01", "HU"),
        "Viking": ("HUMN_BASE_01", "HU01", "HU"), "Elf": ("ELVN_BASE_01", "EV01", "HU"),
        "Skeleton": ("SKTN_BASE_01", "SN01", "SN"), "Robot": ("SCFI_ROBO_04", "RO01", "RO")}
HEADS = {"Civilian": "SK_HUMN_BASE_01_01HEAD_HU01", "Soldier": "SK_HUMN_BASE_01_01HEAD_HU01",
         "Viking": "SK_HUMN_BASE_01_01HEAD_HU01", "Elf": "SK_ELVN_BASE_01_01HEAD_EV01",
         "Skeleton": "SK_SKTN_BASE_01_01HEAD_SN01", "Robot": "SK_ROBO_BASE_15_01HEAD_RO01"}
ARMOR_SLOTS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR"]
BODY_SLOTS = ARMOR_SLOTS + ["15HNDL", "16HNDR", "20FOTL", "21FOTR"]
SYNTY_KEYS = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny")
OUR_KEYS = ("kn_bust", "kn_butt", "kn_defined")
ALL_KEYS = SYNTY_KEYS + OUR_KEYS


def region_of_bone(name):
    """Which armor slot a bone's weight belongs to."""
    n = name
    if re.match(r"(upperarm)(_twist_\d+)?_l$", n):
        return "11AUPL"
    if re.match(r"(upperarm)(_twist_\d+)?_r$", n):
        return "12AUPR"
    if re.match(r"(lowerarm(_twist_\d+)?|hand|.*_0\d_l|thumb.*|index.*|middle.*|ring.*|pinky.*)_l$", n) or n.endswith("_l") and n.split("_")[0] in ("hand", "thumb", "index", "middle", "ring", "pinky", "lowerarm"):
        return "13ALWL"
    if n.endswith("_r") and n.split("_")[0] in ("hand", "thumb", "index", "middle", "ring", "pinky", "lowerarm"):
        return "14ALWR"
    if n.startswith(("calf", "foot", "ball")) and n.endswith("_l"):
        return "18LEGL"
    if n.startswith(("calf", "foot", "ball")) and n.endswith("_r"):
        return "19LEGR"
    if n.startswith(("pelvis", "thigh", "hipAttach")):
        return "17HIPS"
    return "10TORS"                                   # spine, clavicles, neck, head, attach bones


# ------------------------------------------------------------------------------------------------ scene

def import_body(lineage, index):
    """A clean 88-bone rig and the lineage's bare body under it, all keys at 0 (the basis: masculine, lean)."""
    pack, sp, _ = BARE[lineage]
    rig, parts = None, {}
    # Import every part first, then keep one rig: the one with the most bones (robot chassis parts carry a few
    # beyond Synty's 88). Parts imported before the rig was chosen were once left unparented in raw centimetre
    # space, a hundred times too big, which broke every robot measurement.
    loaded = []
    for slot in BODY_SLOTS + ["01HEAD"]:
        stem = HEADS[lineage] if slot == "01HEAD" else f"SK_{pack}_{slot}_{sp}"
        if stem not in index:
            continue
        new = kn.import_fbx(index[stem])
        arm = next(o for o in new if o.type == "ARMATURE")
        mesh = next(o for o in new if o.type == "MESH")
        loaded.append((slot, stem, arm, mesh))
    rig = max((x[2] for x in loaded), key=lambda a: len(a.data.bones))
    rig.name = "SK_Rig"
    for slot, stem, arm, mesh in loaded:
        if arm is not rig:
            mesh.parent = rig
            mesh.matrix_parent_inverse = Matrix.Identity(4)
            mesh.matrix_basis = Matrix.Identity(4)
            for mod in mesh.modifiers:
                if mod.type == "ARMATURE":
                    mod.object = rig
            bpy.data.objects.remove(arm)
        mesh.name = stem
        if mesh.data.shape_keys:
            for kb in mesh.data.shape_keys.key_blocks:
                kb.value = 0.0
        mesh.hide_set(False)
        mesh.hide_viewport = mesh.hide_render = False
        parts[slot] = mesh
    return rig, parts


def evaluated_soup(objects):
    """World-space triangles of the evaluated (posed, keyed) meshes, with the source object and vertex of each corner."""
    dg = bpy.context.evaluated_depsgraph_get()
    verts, tris, owner = [], [], []
    for ob in objects:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        base = len(verts)
        mw = ob.matrix_world
        for v in me.vertices:
            verts.append(mw @ v.co)
            owner.append((ob, v.index))
        me.calc_loop_triangles()
        tris.extend(tuple(base + i for i in t.vertices) for t in me.loop_triangles)
        ev.to_mesh_clear()
    return verts, tris, owner


def weights_at(owner, tri, bary):
    acc = {}
    for corner, w in zip(tri, bary):
        ob, vi = owner[corner]
        names = [g.name for g in ob.vertex_groups]
        for g in ob.data.vertices[vi].groups:
            if g.weight > 1e-5:
                acc[names[g.group]] = acc.get(names[g.group], 0.0) + w * g.weight
    top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
    s = sum(w for _b, w in top) or 1.0
    return {b: w / s for b, w in top}


# ------------------------------------------------------------------------------------------------ pose

def loops_of(bm):
    """Open edge loops: centre and radius of each."""
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
        out.append({"centre": c, "radius": max((v.co - c).length for v in vs), "verts": {v.index for v in vs}})
    return out


def island_of(bm, seeds):
    """Vertex indices connected to any of the seed vertices."""
    bm.verts.ensure_lookup_table()
    seen = set(seeds)
    stack = [bm.verts[i] for i in seeds]
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    return seen


def sleeve_ends(piece_pts, piece_bm, shoulder_l, shoulder_r, closed=False):
    """Where each sleeve ends: the open loop furthest out on that side above the waist, else the furthest point."""
    out = {}
    loops = loops_of(piece_bm)
    zs = [p.z for p in piece_pts]
    z0, z1 = min(zs), max(zs)
    for side, sh in ((1, shoulder_l), (-1, shoulder_r)):
        # A sleeve opening lies wholly on one side of the body, above the crotch, and is arm-sized. A hem crosses the
        # middle (the Viking's tunic hem was taken for a sleeve); a pocket or a strap loop is smaller.
        piece_bm.verts.ensure_lookup_table()
        cand = [lp for lp in loops if lp["centre"].x * side > abs(sh.x) * 0.9 and lp["centre"].z > z0 + 0.45 * (z1 - z0)
                and 0.02 < lp["radius"] < 0.12
                and all(piece_bm.verts[i].co.x * side > 0.4 * abs(sh.x) for i in lp["verts"])
                # a sleeve ends well down the arm: a hole 3 cm from the shoulder is a plate's (the robot's), and a
                # robot's real cuff is closed, so then the arm's furthest reach is its end
                and (lp["centre"] - sh).length > 0.12]
        if closed:
            cand = []                    # robot plates keep their cuffs closed (Synty's do): no opening to find
        if cand:
            # the sleeve is the biggest opening out at that side; a pocket or a strap loop is smaller
            lp = max(cand, key=lambda lp: (round(lp["radius"], 2), lp["centre"].x * side))
            out[side] = {"end": lp["centre"], "radius": lp["radius"], "verts": lp["verts"]}
        else:
            arm = [p for p in piece_pts if p.x * side > abs(sh.x) and p.z > z0 + 0.45 * (z1 - z0)]
            if arm:
                arm.sort(key=lambda p: -p.x * side)
                tip = arm[:max(1, len(arm) // 50)]                  # the outermost few, not one stray vertex
                out[side] = {"end": sum(tip, Vector()) / len(tip), "radius": 0.07, "verts": None}
    # Tripo sometimes seals one cuff and not the other (the Viking's right): then the other side's sleeve end, the
    # one further from its shoulder, is the real one and this side takes its mirror image.
    if 1 in out and -1 in out:
        dl = (out[1]["end"] - shoulder_l).length
        dr = (out[-1]["end"] - shoulder_r).length
        if abs(dl - dr) > 0.15:
            good, bad = (1, -1) if dl > dr else (-1, 1)
            e = out[good]["end"]
            out[bad] = {"end": Vector((-e.x, e.y, e.z)), "radius": out[good]["radius"], "verts": None, "mirrored": True}
    return out


def open_mirrored_cuff(piece, rig, ends, report):
    """A cuff taken from the other side's mirror image may be sealed on the model: look into the sleeve from just
    outside that opening and remove the seal, as the first pass does."""
    import gear_first_pass as fp
    importlib.reload(fp)
    mw = rig.matrix_world
    for side, s in ((1, "l"), (-1, "r")):
        sl = ends.get(side)
        if not sl or not sl.get("mirrored"):
            continue
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        d = (sl["end"] - S).normalized()
        bm = bmesh.new()
        bm.from_mesh(piece.data)
        cap = fp.cap_by_looking_in(bm, sl["end"] + d * 0.05, -d, 0.15)
        if cap and cap["radius"] < 0.12:
            bmesh.ops.delete(bm, geom=cap["faces"], context="FACES_ONLY")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            bm.to_mesh(piece.data)
            piece.data.update()
            report.setdefault("cuffs_opened", []).append(s)
        bm.free()


def joint_child(rig, bone_name):
    """The next joint down the limb (upperarm -> lowerarm -> hand). Imported FBX bones have made-up tails, so a
    limb's real direction is joint to joint, never head to tail."""
    nxt = {"upperarm": "lowerarm", "lowerarm": "hand", "thigh": "calf", "calf": "foot"}
    base, side = bone_name.rsplit("_", 1)
    return rig.pose.bones[f"{nxt[base]}_{side}"]


def aim_bone(rig, bone_name, target_dir_world):
    """Turn a pose bone (about its joint) so the limb points along a world direction."""
    pb = rig.pose.bones[bone_name]
    mw = rig.matrix_world
    head_w = mw @ pb.head
    cur = ((mw @ joint_child(rig, bone_name).head) - head_w).normalized()
    q = cur.rotation_difference(target_dir_world.normalized())
    R_world = Matrix.Translation(head_w) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head_w)
    pb.matrix = mw.inverted() @ R_world @ mw @ pb.matrix
    bpy.context.view_layer.update()


def arm_share(rig, piece, sleeves, trunk_tree, iterations=30):
    """How much each vertex follows each arm (0..1), and which side. The sleeve's core, the cloth round the lowered
    arm out to the sleeve's opening, follows fully; nothing clearly lying on the chest counts as core. From there
    the share spreads smoothly through the cloth (each free vertex the mean of its neighbours) and is held at 0 well
    away from the arm, so the armpit blends as Synty's own torsos do: no tear, no wing. Works on any pattern, since
    it reads only shape, never colour."""
    mw = rig.matrix_world
    me = piece.data
    n = len(me.vertices)
    adj = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)
    share = [0.0] * n
    side_of = [None] * n
    core_all = set()
    for side, s in ((1, "l"), (-1, "r")):
        sl = sleeves.get(side)
        if not sl:
            continue
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        u = (E - S).normalized()
        reach = (sl["end"] - S).dot(u) + 0.03
        width = 1.5 * sl["radius"] + 0.02
        val = [None] * n
        for i, v in enumerate(me.vertices):
            p = v.co
            t = (p - S).dot(u)
            r = (p - S - u * t).length
            if t < -0.06 or t > reach + 0.05 or r > 1.3 * width + 0.05 or p.x * side < 0:
                val[i] = 0.0                               # held: outside a thin band round the sleeve and its armhole
                continue
            if 0.03 < t <= reach and r <= width:
                hit = trunk_tree.find_nearest(p)
                on_chest = hit[0] is not None and hit[3] < 0.5 * r
                if not on_chest:
                    val[i] = 1.0                           # the sleeve's core
                    core_all.add(i)
        free = [i for i in range(n) if val[i] is None]
        cur = [x if x is not None else 0.0 for x in val]
        for _ in range(iterations):
            nxt = cur[:]
            for i in free:
                if adj[i]:
                    nxt[i] = sum(cur[j] for j in adj[i]) / len(adj[i])
            cur = nxt
        for i in range(n):
            if cur[i] > share[i]:
                share[i] = cur[i]
                side_of[i] = s
    return share, side_of, core_all


def reaim_sleeves(piece, rig, sleeves, trunk_tree, iterations=30):
    """Turn each sleeve along its own axis onto the T-pose arm, no body posing needed. The sleeve's core is the cloth
    round the line from the shoulder to the sleeve's opening (nothing clearly lying on the chest); its root is where
    the core starts. The core is turned so its root-to-opening axis lies along the T-pose arm, the root landing on
    the arm as far out from the shoulder as it sat. A thin band round the armhole blends from moved to not moved
    (each free vertex the mean of its neighbours), so nothing tears. Works on puffy, short, long and plated sleeves,
    since the sleeve's own direction is used, not Synty's shoulder joint."""
    from mathutils import Quaternion
    mw = rig.matrix_world
    me = piece.data
    n = len(me.vertices)
    adj = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)
    orig = [v.co.copy() for v in me.vertices]
    new = [c.copy() for c in orig]
    best_k = [0.0] * n
    core_all, info = set(), {}
    for side, s in ((1, "l"), (-1, "r")):
        sl = sleeves.get(side)
        if not sl:
            continue
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        u_T = ((mw @ rig.pose.bones[f"lowerarm_{s}"].head) - S).normalized()
        O = sl["end"]
        d0 = (O - S)
        L0 = d0.length
        d0 = d0 / L0
        width = 1.5 * sl["radius"] + 0.02
        core = []
        for i, p in enumerate(orig):
            if p.x * side < 0:
                continue
            t = (p - S).dot(d0)
            r = (p - S - d0 * t).length
            if 0.0 < t <= L0 + 0.03 and r <= width:
                hit = trunk_tree.find_nearest(p)
                if not (hit[0] is not None and hit[3] < 0.5 * r):
                    core.append(i)
        if len(core) < 8:
            continue
        ts = sorted((orig[i] - S).dot(d0) for i in core)
        t_root = ts[int(0.15 * (len(ts) - 1))]
        root_pts = [orig[i] for i in core if (orig[i] - S).dot(d0) <= t_root]
        R = sum(root_pts, Vector()) / len(root_pts)
        axis = (O - R)
        if axis.length < 1e-4:
            continue
        q = axis.normalized().rotation_difference(u_T)
        R_T = S + u_T * max(0.02, (R - S).length)
        M = Matrix.Translation(R_T) @ q.to_matrix().to_4x4() @ Matrix.Translation(-R)
        # share: core 1, held 0 outside a band round the sleeve and its armhole, smooth in between
        val = [None] * n
        cs = set(core)
        for i, p in enumerate(orig):
            if i in cs:
                val[i] = 1.0
                continue
            t = (p - S).dot(d0)
            r = (p - S - d0 * t).length
            if t < -0.06 or t > L0 + 0.08 or r > 1.3 * width + 0.05 or p.x * side < 0:
                val[i] = 0.0
        free = [i for i in range(n) if val[i] is None]
        cur = [x if x is not None else 0.0 for x in val]
        for _ in range(iterations):
            nxt = cur[:]
            for i in free:
                if adj[i]:
                    nxt[i] = sum(cur[j] for j in adj[i]) / len(adj[i])
            cur = nxt
        for i in range(n):
            k = cur[i]
            if k > best_k[i]:
                best_k[i] = k
                new[i] = orig[i].lerp(M @ orig[i], k)
        core_all |= cs
        info[s] = {"core_vertices": len(core), "turned_deg": round(math.degrees(axis.normalized().angle(u_T)), 1)}
    for v, c in zip(me.vertices, new):
        v.co = c
    me.update()
    return core_all, info


# lineages whose sleeves are cut free of the body piece instead of blended into it
HARD_BREAK = ("Robot", "Viking", "Civilian", "Soldier")


def share_from_posed(rig, piece, body, ends, iterations=12, rigid=False):
    """With the body's arms pointing through the sleeves (shoulder to opening), each vertex follows the arm whose
    skin is nearest it, else the body; the choice is then smoothed through the cloth (each vertex the mean of its
    neighbours, a few passes) so the boundary never tears. Reads only shape: any pattern, any colour."""
    me = piece.data
    n = len(me.vertices)
    arm_slots = {"11AUPL": "l", "13ALWL": "l", "15HNDL": "l", "12AUPR": "r", "14ALWR": "r", "16HNDR": "r"}
    objs = [o for s, o in body.items() if s != "01HEAD"]
    verts, tris, owner = evaluated_soup(objs)
    slot_of_obj = {o.name: s for s, o in body.items()}
    tree = BVHTree.FromPolygons(verts, tris)
    ind = {"l": [0.0] * n, "r": [0.0] * n}
    mw = rig.matrix_world
    axes = {}
    for side, s in ((1, "l"), (-1, "r")):
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        axes[s] = (S, (E - S).normalized(), ends.get(side, {}).get("radius", 0.07))
    for v in me.vertices:
        loc, _n, idx, _d = tree.find_nearest(v.co)
        ob, _vi = owner[tris[idx][0]]
        s = arm_slots.get(slot_of_obj.get(ob.name, ""))
        if rigid and not s:
            # Hard plates (robots): a ring or plate round the posed arm, past the shoulder joint, is arm whatever
            # skin is nearest it; at the elbow of a lowered arm the trunk is often closer than the arm itself.
            for side, s2 in ((1, "l"), (-1, "r")):
                if side not in ends:
                    continue
                S, u, rad = axes[s2]
                reach = (mw @ rig.pose.bones[f"hand_{s2}"].head - S).length + 0.05   # to the wrist: rings, bracers
                t = (v.co - S).dot(u)
                off = (v.co - S - u * t).length
                if 0.03 < t < reach and off < max(1.8 * rad, 0.12):
                    s = s2
        if not s:
            continue
        # Cloth on the body's side of a lowered arm (a tunic panel the arm hangs against) is not sleeve: only what
        # lies round the arm or out from it. The sleeve's own inner half comes along through the smoothing below.
        S, u, rad = axes[s]
        # only where the arm hangs against the body (more than 80 degrees down, like the Viking's): with a gap
        # between arm and body, as on the other pieces, the nearest skin alone tells them apart
        if math.degrees(math.asin(max(-1.0, min(1.0, -u.z)))) > 80:
            t = (v.co - S).dot(u)
            off = v.co - S - u * t
            sign = 1 if s == "l" else -1
            if off.x * sign < -0.4 * rad and off.length > 0.6 * rad:
                continue
        ind[s][v.index] = 1.0
    raw = {s: list(ind[s]) for s in ind}
    adj = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)
    for s in ("l", "r"):
        cur = ind[s]
        for _ in range(iterations):
            cur = [(cur[i] + sum(cur[j] for j in adj[i])) / (1 + len(adj[i])) if adj[i] else cur[i] for i in range(n)]
        ind[s] = cur
    share, side_of = [0.0] * n, [None] * n
    for i in range(n):
        for s in ("l", "r"):
            k = ind[s][i]
            k = max(0.0, min(1.0, (k - 0.15) / 0.55))          # firm inside the sleeve, fading out at its edge
            k = k * k * (3 - 2 * k)
            if k > share[i]:
                share[i], side_of[i] = k, s
    # A separate hard piece (a plate, a shoulder guard, a bracer: islands under 400 vertices) moves whole or not at
    # all, by where most of it lies; a share in between bent the robot's plates and shredded the skeleton's guards.
    seen = set()
    for start in range(n):
        if start in seen:
            continue
        grp, stack = [start], [start]
        seen.add(start)
        while stack:
            x = stack.pop()
            for j in adj[x]:
                if j not in seen:
                    seen.add(j)
                    grp.append(j)
                    stack.append(j)
        if len(grp) >= 400:
            continue
        mean = sum(share[i] for i in grp) / len(grp)
        sides = [side_of[i] for i in grp if side_of[i]]
        s = max(set(sides), key=sides.count) if sides else None
        for i in grp:
            share[i], side_of[i] = (1.0, s) if mean > 0.5 and s else (0.0, None)
    return share, side_of


def seat_sleeves(piece, rig, share, side_of):
    """A sleeve cut free of a hanging body piece (the Viking's) is set onto its T-pose arm by its own shape: its long
    axis turned onto the arm and its middle put on the arm's line. Aiming the arm shoulder-to-cuff left a sleeve that
    hung a little outward riding high above the arm once turned up. Bracers past the elbow stay where they came."""
    import numpy as np
    me = piece.data
    mw = rig.matrix_world
    adj = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)
    moved = {}
    for s in ("l", "r"):
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        u = (E - S).normalized()
        mine = {i for i, (k, sd) in enumerate(zip(share, side_of)) if k > 0.5 and sd == s}
        seen, sleeve = set(), []
        for st in mine:
            if st in seen:
                continue
            grp, stack = [st], [st]
            seen.add(st)
            while stack:
                x = stack.pop()
                for j in adj[x]:
                    if j in mine and j not in seen:
                        seen.add(j)
                        grp.append(j)
                        stack.append(j)
            c = sum((me.vertices[i].co for i in grp), Vector()) / len(grp)
            if (c - S).dot(u) < (E - S).length:
                sleeve += grp
        if len(sleeve) < 30:
            continue
        P = np.array([tuple(me.vertices[i].co) for i in sleeve])
        c = P.mean(0)
        _w, vec = np.linalg.eigh(np.cov((P - c).T))
        a = Vector(vec[:, 2])
        if a.dot(u) < 0:
            a = -a
        R = a.rotation_difference(u).to_matrix()
        cv = Vector(c)
        on_line = S + u * (cv - S).dot(u)
        for i in sleeve:
            me.vertices[i].co = on_line + R @ (me.vertices[i].co - cv)
        # slid in along the arm until it overlaps the body piece's armhole by 3 cm: a gap there showed bare shoulder
        # (Dock Runner), and in the game the torso under the armor is hidden
        ss = set(sleeve)
        moved[s] = {}
        # measured along the top of the shoulder, where the gap shows
        rest_t = [(v.co - S).dot(u) for v in me.vertices if v.index not in ss and side_of[v.index] is None
                  and v.co.z > S.z - 0.01 and v.co.x * (1 if s == "l" else -1) > 0.03]
        top_t = [(me.vertices[i].co - S).dot(u) for i in sleeve if me.vertices[i].co.z > S.z]
        if rest_t and top_t:
            slide = min(top_t) - (max(rest_t) - 0.03)
            moved[s]["top_gap_cm"] = round(100 * slide, 1)
            if slide > 0:
                for i in sleeve:
                    me.vertices[i].co -= u * slide
                on_line -= u * slide
        moved[s].update({"vertices": len(sleeve), "turned_deg": round(math.degrees(a.angle(u)), 1),
                         "shifted_cm": round(100 * (on_line - cv).length, 1)})
    me.update()
    return moved


def plates_whole(me, fcol, fshare, fside, agree=0.6, sliver=25):
    """Tripo fuses a robot into one mesh, so its plates are not islands: a plate is a run of faces of one colour.
    Each colour run moves with the arm or stays as a whole when most of it agrees (a split plate stuck out in shards);
    then any run of moving or staying faces smaller than `sliver` faces takes the side of the faces round it."""
    n = len(me.polygons)
    by_edge = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            by_edge.setdefault(ek, []).append(p.index)
    adj = [set() for _ in range(n)]
    for fs in by_edge.values():
        for a in fs:
            adj[a].update(b for b in fs if b != a)
    col = [Vector(c) for c in fcol]
    lab = [(fside[i] if fshare[i] else None) for i in range(n)]

    def runs(same):
        seen, out = [False] * n, []
        for st in range(n):
            if seen[st]:
                continue
            grp, stack = [st], [st]
            seen[st] = True
            while stack:
                x = stack.pop()
                for y in adj[x]:
                    if not seen[y] and same(x, y):
                        seen[y] = True
                        grp.append(y)
                        stack.append(y)
            out.append(grp)
        return out

    for grp in runs(lambda a, b: (col[a] - col[b]).length < 0.1):
        moving = [lab[i] for i in grp if lab[i]]
        frac = len(moving) / len(grp)
        if frac >= agree:
            side = max(set(moving), key=moving.count)
            for i in grp:
                lab[i] = side
        elif frac <= 1 - agree:
            for i in grp:
                lab[i] = None
    for _ in range(2):
        for grp in runs(lambda a, b: lab[a] == lab[b]):
            if len(grp) >= sliver:
                continue
            gs = set(grp)
            round_ = [lab[y] for x in grp for y in adj[x] if y not in gs]
            if round_:
                new = max(set(round_), key=round_.count)
                for i in grp:
                    lab[i] = new
    return [l is not None for l in lab], lab


def drop_underarm_hoses(piece, rig, body, fcol, clear=0.03):
    """Robots (owner, 23 Oct 2026): hoses Tripo ran from the chest to the lowered arm hang in the open under the
    T-pose arm. Faces out past the shoulder, below the arm and more than `clear` metres from both the arm and the
    trunk go; returns the face colours of what is left and how many faces went."""
    arms_hands = ("11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR")
    av, at, _o = evaluated_soup([o for s, o in body.items() if s in arms_hands])
    tv, tt, _o = evaluated_soup([o for s, o in body.items() if s not in arms_hands and s != "01HEAD"])
    arm, trunk = BVHTree.FromPolygons(av, at), BVHTree.FromPolygons(tv, tt)
    sx = abs((rig.matrix_world @ rig.pose.bones["upperarm_l"].head).x)
    me = piece.data
    gone = set()
    for p in me.polygons:
        c = p.center
        if abs(c.x) < sx + 0.03:
            continue
        loc, _n, _i, da = arm.find_nearest(c)
        _l, _n2, _i2, dt = trunk.find_nearest(c)
        if loc is not None and c.z < loc.z and da > clear and (dt is None or dt > clear):
            gone.add(p.index)
    if gone:
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in gone], context="FACES_ONLY")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(me)
        bm.free()
        me.update()
    return [c for i, c in enumerate(fcol) if i not in gone], len(gone)


def centre_on_neck(piece, rig, body):
    """The piece set straight under the head (owner, 23 Oct 2026: the head looked off-centre): the middle of the
    piece across the shoulders goes under the middle of the head's neck ring, side to side only (a collar ring is no
    guide front to back: a V-neck's centre sits forward). Returns the shift in cm."""
    hv, _t, _o = evaluated_soup([body["01HEAD"]])
    z0 = min(p.z for p in hv)
    ring = [p for p in hv if p.z < z0 + 0.03]
    neck_x = sum(p.x for p in ring) / len(ring)
    me = piece.data
    sh = rig.matrix_world @ rig.pose.bones["upperarm_l"].head
    band = [v.co.x for v in me.vertices if sh.z - 0.12 < v.co.z < sh.z]
    if not band:
        return None
    dx = neck_x - (min(band) + max(band)) / 2
    dx = dx if 0.003 <= abs(dx) <= 0.05 else 0.0
    if not dx:
        return {"moved": False}
    me.transform(Matrix.Translation((dx, 0, 0)))
    me.update()
    return {"x_cm": round(100 * dx, 1), "moved": True}


def blend_arm(rig, piece, weights, share, side_of, hard=False):
    """Weights: (1 - share) of the body's trunk weights and `share` of the arm (the upper arm, or the lower arm past
    the elbow), so every vertex turns with the arm exactly as much as its share."""
    mw = rig.matrix_world
    me = piece.data
    out = list(weights)
    for i, k in enumerate(share):
        if k <= 1e-4 or side_of[i] is None:
            continue
        s = side_of[i]
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        L = (E - S).length
        t = (me.vertices[i].co - S).dot((E - S) / L)
        ramp = max(0.0, min(1.0, (t + 0.03) / 0.08))
        k = k * ramp * ramp * (3 - 2 * ramp) if t < 0.05 and not hard else k   # a cut-free piece moves whole
        bone = f"lowerarm_{s}" if t > L else f"upperarm_{s}"
        w = weights[i]
        tot = sum(w.values()) or 1.0
        mixed = {b: x / tot * (1 - k) for b, x in w.items()}
        mixed[bone] = mixed.get(bone, 0.0) + k
        out[i] = mixed
    return out


def sleeve_faces(piece, rig, sleeves, fcol):
    """The faces of each sleeve: grown from the faces at the sleeve's opening, across neighbours of the opening's
    own colour, out along the arm (from just inside the shoulder to the sleeve's end, within a sleeve's width of the
    arm's line). A vest, a sash or a back panel lying beside a lowered arm is a different colour and stays out."""
    mw = rig.matrix_world
    me = piece.data
    lab = [to_lab(c) for c in fcol]
    edge_faces = {}
    for f in me.polygons:
        for ek in f.edge_keys:
            edge_faces.setdefault(ek, []).append(f.index)
    out = {}
    for side, s in ((1, "l"), (-1, "r")):
        sl = sleeves.get(side)
        if not sl or not sl.get("verts"):
            continue
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        u = (E - S).normalized()
        reach = (sl["end"] - S).dot(u) + 0.03
        width = 1.5 * sl["radius"] + 0.02

        def in_tube(fi):
            c = me.polygons[fi].center
            t = (c - S).dot(u)
            return -0.04 <= t <= reach and (c - S - u * t).length <= width
        seed = [f.index for f in me.polygons if any(v in sl["verts"] for v in f.vertices)]
        if not seed:
            continue
        ref = tuple(float(np.median([lab[i][k] for i in seed])) for k in range(3))
        grown, stack = set(seed), list(seed)
        while stack:
            fi = stack.pop()
            for ek in me.polygons[fi].edge_keys:
                for g in edge_faces[ek]:
                    if g in grown or not in_tube(g):
                        continue
                    if sum((a - b) ** 2 for a, b in zip(lab[g], ref)) ** 0.5 > 22:
                        continue
                    grown.add(g)
                    stack.append(g)
        out[side] = grown
    return out


def arm_weights(rig, piece, weights, sleeves, faces_by_side):
    """Sleeve vertices follow the arm by where they lie along it, blending in over a few centimetres at the
    shoulder joint (upper arm, or the lower arm past the elbow). Everything else keeps the body's weights."""
    mw = rig.matrix_world
    me = piece.data
    out = list(weights)
    sleeve = set()
    for side, s in ((1, "l"), (-1, "r")):
        faces = faces_by_side.get(side)
        if not faces:
            continue
        S = mw @ rig.pose.bones[f"upperarm_{s}"].head
        E = mw @ rig.pose.bones[f"lowerarm_{s}"].head
        L = (E - S).length
        u = (E - S) / L
        verts = {v for fi in faces for v in me.polygons[fi].vertices}
        for i in verts:
            p = me.vertices[i].co
            t = (p - S).dot(u)
            k = max(0.0, min(1.0, (t + 0.03) / 0.08))
            k = k * k * (3 - 2 * k)
            bone = f"lowerarm_{s}" if t > L else f"upperarm_{s}"
            w = weights[i]
            tot = sum(w.values())
            rest = {b: x / tot * (1 - k) for b, x in w.items()} if tot > 0 else {}
            rest[bone] = rest.get(bone, 0.0) + (k if tot > 0 else 1.0)
            norm = sum(rest.values()) or 1.0
            out[i] = {b: x / norm for b, x in rest.items()}
            sleeve.add(i)
        # The cloth round the armhole follows the arm a little, fading out over FADE metres along the cloth from the
        # sleeve, as Synty's own torsos carry some upper-arm weight. Without it the sleeve swings up and the vest's
        # edge stays behind: the faces between stretch into wings (owner, 22 Oct 2026: "looks like wings").
        FADE = 0.06
        start = {i for i in verts}
        dist = {i: 0.0 for i in start}
        frontier = list(start)
        adj = {}
        for e in me.edges:
            a, b = e.vertices
            adj.setdefault(a, []).append(b)
            adj.setdefault(b, []).append(a)
        while frontier:
            nxt = []
            for i in frontier:
                for j in adj.get(i, ()):
                    d = dist[i] + (me.vertices[i].co - me.vertices[j].co).length
                    if d < FADE and d < dist.get(j, 1e9):
                        dist[j] = d
                        nxt.append(j)
            frontier = nxt
        for j, d in dist.items():
            if j in sleeve:
                continue
            k = 0.85 * (1 - d / FADE) ** 2
            w = out[j]
            tot = sum(w.values()) or 1.0
            mixed = {b: x / tot * (1 - k) for b, x in w.items()}
            mixed[f"upperarm_{s}"] = mixed.get(f"upperarm_{s}", 0.0) + k
            out[j] = mixed
    return out, sleeve


def tuck_stretched(piece, body, before, sleeve, offset=0.006, limit=1.4):
    """Cloth the T-pose stretched (edges past `limit` times their modelled length, outside the sleeves) is laid onto
    the T-pose body just above the skin and smoothed, as cloth lies into the armpit, instead of spanning the gap
    between the chest and the raised arm like a wing. Returns how many vertices were laid down."""
    me = piece.data
    bad = set()
    for e in me.edges:
        a, b = e.vertices
        l0 = (before[a] - before[b]).length
        if l0 > 1e-5 and (me.vertices[a].co - me.vertices[b].co).length / l0 > limit:
            for x in (a, b):
                if x not in sleeve:
                    bad.add(x)
    if not bad:
        return 0
    adj = {}
    for e in me.edges:
        a, b = e.vertices
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    ring = set(bad)
    for _ in range(2):                                  # and the cloth just round it, so the change blends in
        ring |= {j for i in ring for j in adj.get(i, ()) if j not in sleeve}
    verts, tris, _owner = evaluated_soup([o for s, o in body.items() if s != "01HEAD"])
    tree = BVHTree.FromPolygons(verts, tris)

    def lay(i):
        co = me.vertices[i].co
        loc, nrm, _x, _d = tree.find_nearest(co)
        if loc is not None:
            me.vertices[i].co = loc + nrm * offset
    # The armpit itself: the vest's side ran up to the bottom of the armhole on the model, and the armhole rose with
    # the arm, so the side now runs diagonally out under the arm (a batwing). On a T-posed body that cloth lies on the
    # side of the chest, up into the armpit: below the shoulder joint and out towards the arm, outside the sleeves,
    # it is laid onto the trunk.
    trunk_parts = [o for s, o in body.items() if s in ("10TORS", "17HIPS")]
    tv, tt, _o2 = evaluated_soup(trunk_parts)
    trunk_tree = BVHTree.FromPolygons(tv, tt)
    rig = next(o.parent for o in body.values() if o.parent)
    mw = rig.matrix_world
    # Small hard pieces (studs, buckles) are not laid down point by point: they ride whole on the cloth under them.
    islands = []
    seen = set()
    for v in me.vertices:
        if v.index in seen:
            continue
        grp, stack = [v.index], [v.index]
        seen.add(v.index)
        while stack:
            x = stack.pop()
            for j in adj.get(x, ()):
                if j not in seen:
                    seen.add(j)
                    grp.append(j)
                    stack.append(j)
        islands.append(grp)
    small = {i for grp in islands if len(grp) < 60 for i in grp}
    bad -= small
    armpit = set()
    S_l = mw @ rig.pose.bones["upperarm_l"].head
    for v in me.vertices:
        i = v.index
        if i in sleeve or i in small:
            continue
        c = v.co
        if not (S_l.z - 0.30 < c.z < S_l.z + 0.03 and abs(c.x) > 0.55 * abs(S_l.x)):
            continue
        loc, nrm, _x, dist = trunk_tree.find_nearest(c)
        if loc is not None and (c - loc).dot(nrm) > offset + 0.008:
            armpit.add(i)
    bad |= armpit
    start = {i: me.vertices[i].co.copy() for i in range(len(me.vertices))}

    def lay(i):
        co = me.vertices[i].co
        loc, nrm, _x, _d = (trunk_tree if i in armpit else tree).find_nearest(co)
        if loc is not None:
            me.vertices[i].co = loc + nrm * offset
    for i in bad:
        lay(i)
    # Even out the laid-down cloth, putting it back on the skin after every pass, so it follows the hollow of the
    # armpit instead of bridging straight across it (smoothing everything round it made a batwing).
    for _ in range(6):
        new = {}
        for i in bad:
            nb = adj.get(i, ())
            if nb:
                new[i] = sum((me.vertices[j].co for j in nb), Vector()) / len(nb) * 0.5 + me.vertices[i].co * 0.5
        for i, c in new.items():
            me.vertices[i].co = c
        for i in bad:
            lay(i)
    # studs and buckles follow the cloth point nearest them, whole
    from mathutils.kdtree import KDTree
    big = [i for i in range(len(me.vertices)) if i not in small]
    kd = KDTree(len(big))
    for i in big:
        kd.insert(start[i], i)
    kd.balance()
    for grp in islands:
        if len(grp) >= 60:
            continue
        c = sum((start[i] for i in grp), Vector()) / len(grp)
        _co, j, _d = kd.find(c)
        shift = me.vertices[j].co - start[j]
        if shift.length > 1e-6:
            for i in grp:
                me.vertices[i].co = start[i] + shift
    me.update()
    return len(bad)


def stretch_report(me, before):
    """How far the T-pose stretched the cloth: edges more than 1.5 times their modelled length."""
    over, worst, where = 0, 1.0, []
    for e in me.edges:
        a, b = e.vertices
        l0 = (before[a] - before[b]).length
        if l0 < 1e-5:
            continue
        r = (me.vertices[a].co - me.vertices[b].co).length / l0
        worst = max(worst, r)
        over += r > 1.5
        if r > 1.5:
            where.append((r, [round(x, 2) for x in (me.vertices[a].co + me.vertices[b].co) / 2]))
    where.sort(key=lambda w: -w[0])
    return {"edges_stretched_over_1_5x": over, "worst_stretch": round(worst, 2),
            "worst_at": [w[1] for w in where[:6]]}


def unpose(piece, rig, weights):
    """Vertices modelled in the posed space go back to the rest pose: p_rest = (sum w_i M_i)^-1 p_posed."""
    mw = rig.matrix_world
    M = {}
    for pb in rig.pose.bones:
        M[pb.name] = (mw @ pb.matrix) @ (mw @ pb.bone.matrix_local).inverted()
    for v, w in zip(piece.data.vertices, weights):
        blend = Matrix(((0, 0, 0, 0),) * 4)
        for b, x in w.items():
            if b in M:
                blend = blend + M[b] * x
        try:
            v.co = blend.inverted() @ v.co
        except ValueError:
            pass
    piece.data.update()


# ------------------------------------------------------------------------------------------------ colour

def read_palette(path):
    """The 24 colours of an owner palette picture (6 x 4 swatches), as sRGB 0..1 tuples."""
    img = bpy.data.images.load(str(path))
    w, h = img.size
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)
    cols = []
    for row in range(4):
        for col in range(6):
            x = int((40 + 230 * col + 105) * w / 1440)
            y_top = int((175 + 200 * row + 67) * h / 1020)
            y = h - 1 - y_top                              # pixels run bottom to top
            c = px[max(0, y - 6):y + 6, max(0, x - 6):x + 6, :3].reshape(-1, 3).mean(0)
            cols.append(tuple(float(min(1, max(0, as_srgb(img, v)))) for v in c))
    bpy.data.images.remove(img)
    return cols


def as_srgb(img, v):
    """Blender gives an 8-bit PNG or JPEG's pixels as stored, already sRGB; only float images are linear. Converting
    the stored values again washed every set colour out (the Dock Runner's sage #A8A78F came out #D4D4C5)."""
    return kn.linear_to_srgb(v) if img.is_float else v


def to_lab(rgb):
    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(c) for c in rgb)
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883

    def f(t):
        return t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return (116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z)))


def face_colours(piece, size=2048):
    """sRGB colour of each face, sampled from the piece's own texture at its corners and centre (median)."""
    img = None
    for slot in piece.material_slots:
        m = slot.material
        if m and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    img = n.image
    if img is None:
        raise SystemExit("The piece has no texture to read colours from.")
    if img.size[0] > size:
        img.scale(size, size)
    w, h = img.size
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)[..., :3]
    uv = piece.data.uv_layers.active.data
    out = []
    for p in piece.data.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        c = sum((Vector(x) for x in pts), Vector((0, 0))) / len(pts)
        samples = [c] + [c.lerp(Vector(x), 0.6) for x in pts]
        cols = [px[min(h - 1, max(0, int((s.y % 1.0) * h))), min(w - 1, max(0, int((s.x % 1.0) * w)))] for s in samples]
        lin = np.median(np.array(cols), axis=0)
        out.append(tuple(as_srgb(img, float(v)) for v in lin))
    return out


def nearest(cols_lab, c_lab):
    best, bd = 0, 1e9
    for i, p in enumerate(cols_lab):
        d = sum((a - b) ** 2 for a, b in zip(p, c_lab))
        if d < bd:
            best, bd = i, d
    return best


def assign_cells(spec, regions, face_pal, palette, areas):
    """For each slot: rank the palette colours its faces use by area, keep five (merging the rest into the nearest
    kept one), and put them on the slot's own Outfit 01..05 cells. Returns face -> (cell u, v) and slot -> cells."""
    pal_lab = [to_lab(c) for c in palette]
    cells = kn.cell_lookup(spec)
    face_cell, used = {}, {}
    for slot, faces in regions.items():
        own = spec["slots"][slot]["own_cells"]
        area = {}
        for f in faces:
            area[face_pal[f]] = area.get(face_pal[f], 0.0) + areas[f]
        keep = [i for i, _a in sorted(area.items(), key=lambda kv: -kv[1])[:len(own)]]
        keep_lab = [pal_lab[i] for i in keep]
        slot_cells = {}
        for rank, pi in enumerate(keep):
            u, v = cells[kn.cell_key(own[rank])]
            slot_cells[own[rank]] = {"uv": (u, v), "colour": "#%02X%02X%02X" % tuple(int(round(x * 255)) for x in palette[pi])}
        for f in faces:
            pi = face_pal[f]
            k = keep.index(pi) if pi in keep else nearest(keep_lab, pal_lab[pi])
            face_cell[f] = cells[kn.cell_key(own[k])]
        used[slot] = slot_cells
    return face_cell, used


# ------------------------------------------------------------------------------------------------ main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fbx", required=True)
    ap.add_argument("--set", required=True, help="four capital letters naming the set, e.g. DOCK")
    ap.add_argument("--lineage")
    ap.add_argument("--palette")
    ap.add_argument("--stage", choices=("pose", "fit"), default="fit")
    ap.add_argument("--work")
    ap.add_argument("--out-dir")
    ap.add_argument("--sheet")
    ap.add_argument("--posed-sheet", help="picture the body posed into the piece, before the piece goes to T-pose")
    ap.add_argument("--resize", type=float, default=1.0,
                    help="gloves, boots, cape, helmet: scale the piece about the middle of its top first (owner, 23 Oct "
                         "2026: Dock Runner's cape a tad big, scaled about its collar so it still sits on the shoulders)")
    ap.add_argument("--slot", choices=("armor", "gloves", "boots", "cape", "helmet"),
                    help="default: read from the file name, as the first pass does")
    ap.add_argument("--no-record", action="store_true",
                    help="leave the project's gear swatch (KN_GearSwatch.json) as it is; the theme is only reported")
    a = ap.parse_args(kn.tool_args())
    src = Path(a.fbx)
    lineage = a.lineage or src.parent.parent.name
    if lineage not in BARE or not re.fullmatch(r"[A-Z]{4}", a.set):
        raise SystemExit("Pass --set ABCD (four capitals) and a known --lineage.")
    piece_name = src.stem.replace("_Revised", "")
    spec = kn.load_spec()
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}
    # our bare bodies (Synty's plus kn_bust, kn_butt, kn_defined) where we have them: fitted on Synty's own, the
    # armor's three new keys came out empty and the armor would not follow those sliders in the creator
    bare = kn.project_root() / "Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody"
    index.update({p.stem: p for p in bare.glob("SK_*.fbx")})
    report, problems, warnings = {"piece": piece_name, "lineage": lineage, "set": a.set}, [], []

    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig, body = import_body(lineage, index)
    new = kn.import_fbx(src)
    piece = next(o for o in new if o.type == "MESH")
    piece.name = piece.data.name = piece_name
    piece.data.transform(piece.matrix_world)
    piece.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    if not a.slot:
        import gear_first_pass as fp
        importlib.reload(fp)
        a.slot = fp.guess_slot(src)
    report["slot"] = a.slot
    if a.slot != "armor":
        return other_piece(a, src, piece, piece_name, lineage, rig, body, spec, report, problems, warnings)

    report["centred_on_neck"] = centre_on_neck(piece, rig, body)
    # 1. Pose the body's arms to the piece's sleeves.
    bm = bmesh.new()
    bm.from_mesh(piece.data)
    pts = [v.co.copy() for v in bm.verts]
    mw = rig.matrix_world
    sh_l = mw @ rig.pose.bones["upperarm_l"].head
    sh_r = mw @ rig.pose.bones["upperarm_r"].head
    ends = sleeve_ends(pts, bm, sh_l, sh_r, closed=lineage == "Robot")
    bm.free()
    open_mirrored_cuff(piece, rig, ends, report)
    # 2. Each sleeve turned along its own axis onto the T-pose arm (the body is never posed).
    arms_hands = ("11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR")
    trunk = [o for s, o in body.items() if s not in arms_hands]
    verts, tris, owner = evaluated_soup(trunk)
    tree = BVHTree.FromPolygons(verts, tris)
    fcol = face_colours(piece)
    before = [v.co.copy() for v in piece.data.vertices]
    # body weights for everything (no arm), from the T-pose trunk
    weights = []
    for v in piece.data.vertices:
        loc, _n, idx, _d = tree.find_nearest(v.co)
        t3 = tris[idx]
        weights.append(weights_at(owner, t3, poly_3d_calc([verts[i] for i in t3], loc)))
    armish = ("upperarm", "lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky")
    for i, w in enumerate(weights):
        keep = {b: x for b, x in w.items() if not b.startswith(armish)}
        if not keep:
            keep = {("clavicle_l" if piece.data.vertices[i].co.x > 0 else "clavicle_r"): 1.0}
        s_ = sum(keep.values())
        weights[i] = {b: x / s_ for b, x in keep.items()}
    # arms pointed through the sleeves, the cloth's share of each arm read from the posed body, then back to T
    posed = {}
    for side, bone, sh in ((1, "upperarm_l", sh_l), (-1, "upperarm_r", sh_r)):
        if side in ends:
            d = ends[side]["end"] - sh
            aim_bone(rig, bone, d)
            posed[bone] = round(math.degrees(math.atan2(-d.z, abs(d.x))), 1)
    report["arms_lowered_deg"] = posed
    # Modelled in T-pose already (owner, 23 Oct 2026: all new gear is): the sleeves are on the arms, so none of the
    # arms-down conversion below runs (cutting sleeves free, turning them up, tucking armpits made the old six fail).
    tpose = bool(posed) and max(posed.values()) < 20
    report["modelled_in_tpose"] = tpose
    if tpose:
        for pb in rig.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        reach = abs(sh_l.x) + 0.03
        # out past the shoulder AND at arm height: thigh pockets stick out as far and were pulled up to the arm
        sleeve_verts = {v.index for v in piece.data.vertices if abs(v.co.x) > reach and v.co.z > sh_l.z - 0.14}
        report["sleeve_vertices"] = len(sleeve_verts)
    else:
        if a.posed_sheet:
            sheet(piece, list(body.values()), Path(a.posed_sheet).resolve())
        share, side_of = share_from_posed(rig, piece, body, ends, rigid=lineage == "Robot")
        if os.environ.get("KN_DUMP"):
            import json
            sh = {s: [list(mw @ rig.pose.bones[b].head) for b in (f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}")] for s in "lr"}
            json.dump({"co": [list(v.co) for v in piece.data.vertices], "share": share,
                       "edges": [list(e.vertices) for e in piece.data.edges], "bones": sh,
                       "ends": {str(k): [list(v["end"]), v["radius"]] for k, v in ends.items()}},
                      open(os.environ["KN_DUMP"], "w"))
        if lineage in HARD_BREAK:
            # Robots are hard plates with clean breaks, as Synty's chassis parts are: all or nothing, never bent. The
            # mesh is cut where a moving face meets a staying one, so neither stretches into a spike between them.
            # The Viking takes the same break: its sleeves hang fused to the tunic, and any blend between them webbed.
            me = piece.data
            if lineage == "Civilian":
                # cloth over the top of the shoulder (the yoke, inside 3 cm past the joint) stays with the body piece;
                # turned up with the sleeve it left the shoulder bare (Dock Runner; on the Viking it bent the sleeve's axis)
                for s_ in ("l", "r"):
                    S_ = mw @ rig.pose.bones[f"upperarm_{s_}"].head
                    u_ = (mw @ rig.pose.bones[f"lowerarm_{s_}"].head - S_).normalized()
                    for v in me.vertices:
                        if side_of[v.index] == s_ and (v.co - S_).dot(u_) < 0.03:
                            share[v.index], side_of[v.index] = 0.0, None
            fshare = [sum(share[v] for v in p.vertices) / len(p.vertices) > 0.5 for p in me.polygons]
            fside = []
            for p in me.polygons:
                ss = [side_of[v] for v in p.vertices if side_of[v]]
                fside.append(max(set(ss), key=ss.count) if ss else None)
            bm = bmesh.new()
            bm.from_mesh(me)
            bm.faces.ensure_lookup_table()
            lay = bm.faces.layers.int.new("kn_move")
            for f in bm.faces:
                f[lay] = (1 if fside[f.index] == "l" else 2) if fshare[f.index] and fside[f.index] else 0
            cut = [e for e in bm.edges if len(e.link_faces) == 2 and e.link_faces[0][lay] != e.link_faces[1][lay]]
            bmesh.ops.split_edges(bm, edges=cut)
            # faces that touch only at a corner are still joined there: rip the moving faces' corner free too
            for v in [v for v in bm.verts if len({f[lay] for f in v.link_faces}) > 1]:
                for f in [f for f in v.link_faces if f[lay]]:
                    if len({g[lay] for g in v.link_faces}) > 1:
                        bmesh.utils.face_vert_separate(f, v)
            bm.to_mesh(me)
            bm.free()
            me.update()
            mv = me.attributes["kn_move"].data
            share = [0.0] * len(me.vertices)
            side_of = [None] * len(me.vertices)
            for p in me.polygons:
                m = mv[p.index].value
                if m:
                    for v in p.vertices:
                        share[v], side_of[v] = 1.0, ("l" if m == 1 else "r")
            # the cut added vertices: body weights again, for every vertex, from the trunk (arms are not posed in it)
            weights = []
            for v in me.vertices:
                loc, _n, idx, _d = tree.find_nearest(v.co)
                t3 = tris[idx]
                weights.append(weights_at(owner, t3, poly_3d_calc([verts[i] for i in t3], loc)))
            for i, w in enumerate(weights):
                keep = {b: x for b, x in w.items() if not b.startswith(armish)}
                if not keep:
                    keep = {("clavicle_l" if me.vertices[i].co.x > 0 else "clavicle_r"): 1.0}
                s_ = sum(keep.values())
                weights[i] = {b: x / s_ for b, x in keep.items()}
            before = [v.co.copy() for v in me.vertices]
            fcol = face_colours(piece)
        sleeve_verts = {i for i, k in enumerate(share) if k > 0.5}
        report["sleeve_vertices"] = len(sleeve_verts)
        weights = blend_arm(rig, piece, weights, share, side_of, hard=lineage in HARD_BREAK)
        unpose(piece, rig, weights)
        for pb in rig.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        if lineage in HARD_BREAK and lineage != "Robot":
            report["sleeves_seated"] = seat_sleeves(piece, rig, share, side_of)
        report["stretch_before_tuck"] = stretch_report(piece.data, before)
        # Robots are hard plates, not cloth: nothing is laid onto the body (Synty's robot parts are rigid chassis).
        report["tucked_into_armpit"] = 0 if lineage in HARD_BREAK else tuck_stretched(piece, body, before, sleeve_verts)
        report["stretch"] = stretch_report(piece.data, before)
        if lineage == "Robot":
            fcol, report["underarm_hose_faces_removed"] = drop_underarm_hoses(piece, rig, body, fcol)
            sleeve_verts = set()          # vertex numbers moved; robots draw no cuffs in and lift no undersides anyway
        for pb in rig.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()

    if a.stage == "pose":
        save_work(a, piece_name)
        if a.sheet:
            sheet(piece, list(body.values()), Path(a.sheet).resolve())
        kn.report("Gear second pass (pose)", report, problems, warnings)
        return
    import gear_fit
    importlib.reload(gear_fit)
    helpers = dict(globals())
    helpers["sleeve_verts"] = sleeve_verts
    parts = gear_fit.run_fit(a, src, piece, piece_name, lineage, rig, body, fcol, spec, report, problems, warnings, helpers)
    for o in body.values():
        o.hide_set(False)
    work = save_work(a, piece_name)
    report["working_scene"] = str(work)
    if a.sheet:
        gear_fit.shapes_sheet(list(body.values()), list(parts.values()), Path(a.sheet).resolve())
        gear_fit.closeup_sheet(list(body.values()), list(parts.values()),
                               Path(a.sheet).resolve().with_name(Path(a.sheet).stem + "_closeup.png"))
    kn.report("Gear second pass", report, problems, warnings)


def piece_tpose(slot, piece, rig, body):
    """Whether gloves, boots or a cape were modelled for the T-posed body (owner, 23 Oct 2026: all new gear is). Gloves:
    each glove's long axis within 20 degrees of its T-pose forearm. Boots: the feet stand the same in every pose. A
    cape: no cloth inside the T-pose arms (a cape made over lowered arms has the arms through its sides)."""
    mw = rig.matrix_world
    me = piece.data
    out = {}
    if slot == "gloves":
        angles = {}
        for side, s in ((1, "l"), (-1, "r")):
            P = np.array([tuple(v.co) for v in me.vertices if v.co.x * side > 0])
            if len(P) < 10:
                continue
            _w, vec = np.linalg.eigh(np.cov((P - P.mean(0)).T))
            axis = Vector(vec[:, 2])
            arm = (mw @ rig.pose.bones[f"hand_{s}"].head) - (mw @ rig.pose.bones[f"lowerarm_{s}"].head)
            angles[s] = round(math.degrees(min(axis.angle(arm), axis.angle(-arm))), 1)
        out["glove_axis_vs_tpose_forearm_deg"] = angles
        out["modelled_in_tpose"] = bool(angles) and max(angles.values()) < 20
    elif slot in ("boots", "helmet"):
        out["modelled_in_tpose"] = True
        out["tpose_note"] = ("feet" if slot == "boots" else "the head") + ": the same in every pose"
    else:
        arms = [o for s, o in body.items() if s in ("11AUPL", "12AUPR", "13ALWL", "14ALWR")]
        av, at, _o = evaluated_soup(arms)
        tree = BVHTree.FromPolygons(av, at)
        inside = 0
        for v in me.vertices:
            loc, nrm, _i, d = tree.find_nearest(v.co)
            if loc is not None and d < 0.04 and (v.co - loc).dot(nrm) < -0.005:
                inside += 1
        out["vertices_inside_tpose_arms"] = inside
        out["modelled_in_tpose"] = inside < 0.01 * len(me.vertices)
    return out


def other_piece(a, src, piece, piece_name, lineage, rig, body, spec, report, problems, warnings):
    """Gloves, boots and a cape: no sleeves to turn, no waist or limbs to cinch. They sit on the T-posed body as
    modelled; the fit binds them, splits them into their Synty slots (15HNDL/16HNDR, 20FOTL/21FOTR, 24ABAC as the game
    wears them: SidekickGearGenerator), gives them the body keys and the set's colours."""
    if a.resize != 1.0:
        me = piece.data
        zs = [v.co.z for v in me.vertices]
        top = [v.co for v in me.vertices if v.co.z > max(zs) - 0.12 * (max(zs) - min(zs))]
        pivot = Vector(((min(p.x for p in top) + max(p.x for p in top)) / 2,
                        (min(p.y for p in top) + max(p.y for p in top)) / 2, max(zs)))
        me.transform(Matrix.Translation(pivot) @ Matrix.Scale(a.resize, 4) @ Matrix.Translation(-pivot))
        me.update()
        report["resized"] = f"x{a.resize} about the top's middle {[round(x, 3) for x in pivot]}"
    report.update(piece_tpose(a.slot, piece, rig, body))
    if not report["modelled_in_tpose"]:
        warnings.append("The piece does not lie on the T-posed body; this pass does not turn it. Look at the sheet.")
    fcol = face_colours(piece)
    if a.stage == "pose":
        save_work(a, piece_name)
        kn.report("Gear second pass (pose)", report, problems, warnings)
        return
    import gear_fit
    importlib.reload(gear_fit)
    helpers = dict(globals())
    helpers["sleeve_verts"] = set()
    parts = gear_fit.run_fit(a, src, piece, piece_name, lineage, rig, body, fcol, spec, report, problems, warnings, helpers)
    for o in body.values():
        o.hide_set(False)
    work = save_work(a, piece_name)
    report["working_scene"] = str(work)
    if a.sheet:
        gear_fit.shapes_sheet(list(body.values()), list(parts.values()), Path(a.sheet).resolve())
        gear_fit.closeup_sheet(list(body.values()), list(parts.values()),
                               Path(a.sheet).resolve().with_name(Path(a.sheet).stem + "_closeup.png"),
                               **gear_fit.CLOSEUP.get(a.slot, {}))
    kn.report("Gear second pass", report, problems, warnings)


def save_work(a, piece_name):
    work = Path(a.work) if a.work else kn.art_root() / "Sidekick/Gear" / f"{piece_name}.blend"
    work.parent.mkdir(parents=True, exist_ok=True)
    # every body key back to 0: the clip check leaves them all at 1, and the scene (and any FBX made from it) then
    # showed the armor puffed out by every key at once over a neutral body
    for o in bpy.data.objects:
        if o.type == "MESH" and o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks[1:]:
                kb.value = 0.0
    bpy.ops.wm.save_as_mainfile(filepath=str(work.resolve()))
    return work


def sheet(piece, body, path, size=480):
    import tempfile
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
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
    cd.ortho_scale = 2.0
    cam = bpy.data.objects.new("kn_c", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    ctr = Vector((0, 0, 0.95))
    tmp = Path(tempfile.mkdtemp(prefix="kn_sp_"))
    shots = []
    for name, d in (("front", (0, -1, 0.05)), ("side", (1, 0, 0.05)), ("back", (0, 1, 0.05)), ("threeq", (0.7, -1, 0.3))):
        d = Vector(d).normalized()
        cam.location = ctr + d * 20
        cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        p = tmp / f"{name}.png"
        sc.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        shots.append(p)
    img = np.full((size + 12, 4 * size + 30, 4), 0.1, np.float32)
    img[..., 3] = 1
    for c, p in enumerate(shots):
        im = bpy.data.images.load(str(p))
        img[6:6 + size, 6 + c * (size + 6):6 + c * (size + 6) + size] = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
        bpy.data.images.remove(im)
    out = bpy.data.images.new("kn_sheet", img.shape[1], img.shape[0], alpha=True)
    out.pixels = img.ravel()
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save()


main()
