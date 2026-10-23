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
    if slot == "gloves":
        return [("left cuff", lambda c: c.x >= 0 and h(c) > 0.6, up), ("right cuff", lambda c: c.x < 0 and h(c) > 0.6, up)]
    if slot == "boots":
        return [("left boot top", lambda c: c.x >= 0 and h(c) > 0.6, up),
                ("right boot top", lambda c: c.x < 0 and h(c) > 0.6, up)]
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
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(kn.tool_args())
    src = Path(a.fbx)
    lineage = a.lineage or src.parent.parent.name
    if lineage not in BARE:
        raise SystemExit(f"Lineage '{lineage}' unknown; pass --lineage one of {sorted(BARE)}.")
    slot = a.slot or guess_slot(src)
    piece = re.sub(r"\s+", "_", src.parent.name.strip()) or src.stem   # the piece is named after its folder (Tripo names files generically)
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
    zs = [v.co.z for v in bm.verts]
    height = max(zs) - min(zs)
    before_loops = loops_of(bm)
    tri_before = sum(len(f.verts) - 2 for f in bm.faces)

    # 2. Openings: open loops count as open; sealed ones are cap patches.
    caps = cap_candidates(bm, height)
    used = set()
    for name, place, facing in openings(slot, bm, height):
        open_loops = [lp for lp in before_loops if 0.012 * height < lp["radius"] < 0.15 * height and place(lp["centre"])]
        if open_loops:
            big = max(open_loops, key=lambda lp: lp["radius"])
            done.append({"opening": name, "state": "already open", "radius_cm": round(100 * big["radius"], 1)})
            continue
        found = [c for c in caps if id(c) not in used and place(c["centre"]) and facing(c["normal"])]
        if not found:
            warnings.append(f"{name}: neither open nor a clear cap found; look at the sheet.")
            continue
        cap = max(found, key=lambda c: c["radius"])
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
        caps = [c for c in caps if c is cap or all(f.is_valid for f in c["faces"])]
        done.append({"opening": name, "state": "opened", "faces_removed": len(cap["faces"]),
                     "verts_removed": len(loose), "radius_cm": round(100 * cap["radius"], 1),
                     "where": [round(x, 3) for x in cap["centre"]]})
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
        top = T[1].z
        leg = boxes.get("18LEGL") or boxes.get("17HIPS")
        bottom = leg[0].z
        scale = (top - bottom) / size.z
        anchor_model = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        anchor_body = Vector(((T[0].x + T[1].x) / 2, (T[0].y + T[1].y) / 2, bottom))
        measure = f"collar to ankle = body neck to ankle ({100 * (top - bottom):.1f} cm)"
    elif slot == "boots":
        foot = boxes["20FOTL"]
        scale = (foot[1].y - foot[0].y) * 1.08 / size.y
        anchor_model = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        anchor_body = Vector((0.0, (foot[0].y + foot[1].y) / 2, 0.0))
        measure = f"sole length = foot length + 8 % ({100 * (foot[1].y - foot[0].y) * 1.08:.1f} cm)"
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
        "piece": piece, "lineage": lineage, "slot": slot, "openings": done,
        "triangles": f"{tri_before} -> {tri_after}",
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


main()
