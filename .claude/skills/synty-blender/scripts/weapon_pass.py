"""Weapon pass for Tripo weapons (owner 23 Oct 2026: weapons get passes of their own, not the armour's).

One run takes a Tripo weapon FBX to a game-ready model:
  1. sizes it to the game's length for its class (DockRunner_Weapon_Sizing.json, measured from the Rare weapons),
  2. finds its grip and puts the origin there: the butt-to-tip axis runs along +Y, the broad side (blade width, a
     pistol's handle) along Z (a pistol's handle hangs down, -Z),
  3. snaps every face to the set's palette (the same rules as the gear second pass) on a small palette texture,
     with flat (faceted) shading: the low-poly look, dyeable in game by WeaponPaint,
  4. writes the FBX, the palette PNG, a grip json (main hand at the origin; a two-handed weapon's off hand on the
     shaft) and a review sheet with the grips marked.

    python run_blender.py weapon_pass.py --fbx "<DockRunner_Glaive.fbx>" --kind polearm --length 2.776
        --grip-json "<DockRunner_Glaive_grip.json>" --set DOCK --out-dir "<Assets folder>" --name W_KRYT_DOCK_Glaive
        [--sheet "<png>"] [--blend "<working .blend to save>"]
  --kind sword | polearm | pistol
"""
import argparse
import importlib
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).parent))
import kn_common as kn  # noqa: E402

importlib.reload(kn)
import gear_fit as gf  # noqa: E402

importlib.reload(gf)

SWATCH = Path(__file__).resolve().parents[4] / "Assets/_ArtSource/Blender~/Swatch/KN_GearSwatch.json"
GRID = 8            # palette texture: 8 x 8 blocks
BLOCK = 32          # pixels per block (256 px texture)
SPECK_CM2 = 1.0     # a weapon is small: colour patches under 1 cm2 take their border's colour
SWORD_HAND = 0.07   # metres from the guard to the middle of the sword hand: the hand closes right under the guard


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", required=True)
    p.add_argument("--kind", choices=["sword", "polearm", "pistol"], required=True)
    p.add_argument("--length", type=float, required=True, help="game length in metres, butt to tip")
    p.add_argument("--grip-json", help="measured grips (polearm): axis_butt_to_tip, main_hand/off_hand along")
    p.add_argument("--set", default="DOCK")
    p.add_argument("--out-dir", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--sheet")
    p.add_argument("--blend")
    p.add_argument("--flip", action="store_true", help="the found butt is the tip: turn the weapon round")
    p.add_argument("--grip-drop", type=float, default=0.0,
                   help="metres down the pistol's handle from the slide to the middle of the hand")
    p.add_argument("--grip-along", type=float, help="the hand's place from the butt (0) to the tip (1), read off a sheet")
    return p.parse_args(argv)


# ------------------------------------------------------------------------------------------------ colour (as the gear pass)

def as_srgb(img, v):
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


def nearest(cols_lab, c_lab):
    best, bd = 0, 1e9
    for i, p in enumerate(cols_lab):
        d = sum((a - b) ** 2 for a, b in zip(p, c_lab))
        if d < bd:
            best, bd = i, d
    return best


def face_colours(ob, size=2048):
    img = None
    for slot in ob.material_slots:
        m = slot.material
        if m and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    img = n.image
    if img is None:
        raise SystemExit("The weapon has no texture to read colours from.")
    if img.size[0] > size:
        img.scale(size, size)
    w, h = img.size
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)[..., :3]
    uv = ob.data.uv_layers.active.data
    out = []
    for p in ob.data.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        c = sum((Vector(x) for x in pts), Vector((0, 0))) / len(pts)
        samples = [c] + [c.lerp(Vector(x), 0.6) for x in pts]
        cols = [px[min(h - 1, max(0, int((s.y % 1.0) * h))), min(w - 1, max(0, int((s.x % 1.0) * w)))] for s in samples]
        lin = np.median(np.array(cols), axis=0)
        out.append(tuple(as_srgb(img, float(v)) for v in lin))
    return out


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ------------------------------------------------------------------------------------------------ shape

def load(fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in bpy.context.scene.objects:
        o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != ob:
            bpy.data.objects.remove(o)
    return ob


def verts_np(ob):
    return np.array([v.co[:] for v in ob.data.vertices], np.float64)


def principal_axes(pts):
    c = pts.mean(0)
    _w, v = np.linalg.eigh(np.cov((pts - c).T))
    return [Vector(v[:, i]).normalized() for i in (2, 1, 0)]  # longest first


def slices(pts, axis, n=60):
    t = pts @ np.array(axis)
    lo, hi = t.min(), t.max()
    edges = np.linspace(lo, hi, n + 1)
    return t, lo, hi, edges


def find_frame(ob, a, grips):
    """Origin (grip), long axis (butt -> tip), side axis (broad side or the pistol's handle direction)."""
    pts = verts_np(ob)
    ax, side, _thin = principal_axes(pts)
    if grips:
        ax = Vector(grips["axis_butt_to_tip"]).normalized()
    t, lo, hi, edges = slices(pts, ax)
    span = hi - lo
    perp = pts - np.outer(t, np.array(ax))
    width = []
    for i in range(len(edges) - 1):
        m = (t >= edges[i]) & (t < edges[i + 1])
        if m.sum() < 3:
            width.append(0.0)
            continue
        q = perp[m]
        width.append(float(np.linalg.norm(q.max(0) - q.min(0))))
    width = np.array(width)
    info = {}
    if a.kind == "sword":
        # The guard is the widest slice; the pommel is the end nearer to it.
        g = int(width.argmax())
        guard_t = (edges[g] + edges[g + 1]) / 2
        butt_low = (guard_t - lo) < (hi - guard_t)
        if a.flip:
            butt_low = not butt_low
        if not butt_low:
            ax = -ax
            t, lo, hi = -t, -hi, -lo
            guard_t = -guard_t
        grip_t = max(lo + 0.3 * (guard_t - lo), guard_t - SWORD_HAND * span / a.length)
        if a.grip_along is not None:
            # a one-hander whose widest part is its head, not a guard (Scrap Sentinel's sickle-pick Harvester): the
            # hand goes where the sheet says, measured from the butt
            grip_t = lo + a.grip_along * span
        info["guard_along"] = (guard_t - lo) / span
        info["grip_along"] = (grip_t - lo) / span
    elif a.kind == "polearm":
        if a.flip:
            ax = -ax
            t, lo, hi = -t, -hi, -lo
        grip_t = lo + grips["main_hand"]["along"] * span
        info["grip_along"] = grips["main_hand"]["along"]
        info["off_hand_along"] = grips["off_hand"]["along"]
        info["blade_starts_at"] = grips.get("blade_starts_at")
    else:
        # A pistol: the barrel is the long axis, the handle hangs off it along the second axis. The handle is the
        # stretch of the barrel where the shape reaches furthest along that axis; the hand holds it at its top.
        s = perp @ np.array(side)
        mid = np.median(s)
        if abs(s.max() - mid) < abs(s.min() - mid):
            side = -side
            s = -s
            mid = -mid          # the middle flips with the side (Bastion Field's pistol found no handle without it)
        deep = s > mid + 0.55 * (s.max() - mid)
        handle_t = float(np.median(t[deep]))
        # muzzle is the end further from the handle
        if (handle_t - lo) > (hi - handle_t):
            ax = -ax
            t, lo, hi = -t, -hi, -lo
            handle_t = -handle_t
        if a.flip:
            ax = -ax
            t, lo, hi = -t, -hi, -lo
            handle_t = -handle_t
        grip_t = handle_t if a.grip_along is None else lo + a.grip_along * span
        info["grip_along"] = (grip_t - lo) / span
        info["handle_side"] = "down"
    centre = Vector(pts.mean(0))
    centre_t = centre.dot(ax)
    origin = centre + ax * (grip_t - centre_t)
    if a.kind == "pistol":
        # the hand closes round the top of the handle, just under the slide
        s_all = (pts - np.array(origin)) @ np.array(side)
        top = float(np.percentile(s_all[(np.abs(t - grip_t) < 0.08 * span)], 35))
        origin = origin + side * top
    else:
        # on the shaft's own centre line at that point
        near = np.abs(t - grip_t) < 0.02 * span
        if near.sum() >= 3:
            c = pts[near].mean(0)
            origin = Vector(c) + ax * (grip_t - Vector(c).dot(ax))
    # the side axis must be square to the long one
    side = (side - ax * side.dot(ax)).normalized()
    return origin, ax, side, span, info


def to_frame(ob, origin, ax, side, scale, pistol):
    """Grip at the origin, butt -> tip along +Y, broad side along +Z (a pistol's handle down, -Z)."""
    z = -side if pistol else side
    x = ax.cross(z).normalized()
    z = x.cross(ax).normalized()
    rot = Matrix((x, ax, z)).to_4x4()  # rows: world -> frame
    m = Matrix.Scale(scale, 4) @ rot @ Matrix.Translation(-origin)
    ob.data.transform(m)
    ob.data.update()


# ------------------------------------------------------------------------------------------------ palette

def paint(ob, palette_hex, png_path):
    palette = [hex_rgb(h) for h in palette_hex]
    pal_lab = [to_lab(c) for c in palette]
    cols = face_colours(ob)
    areas = [p.area for p in ob.data.polygons]
    face_pal = gf.clean_colours(ob.data, cols, pal_lab, to_lab, nearest, areas, min_patch=SPECK_CM2 * 1e-4)
    # the palette texture
    size = GRID * BLOCK
    img = bpy.data.images.new(Path(png_path).stem, size, size, alpha=False)
    px = np.zeros((size, size, 4), np.float32)
    px[..., 3] = 1
    for i in range(GRID * GRID):
        c = palette[i] if i < len(palette) else palette[0]
        u, v = i % GRID, i // GRID
        px[v * BLOCK:(v + 1) * BLOCK, u * BLOCK:(u + 1) * BLOCK, :3] = c
    img.pixels[:] = px.ravel()
    img.filepath_raw = str(png_path)
    img.file_format = "PNG"
    img.save()
    # every face's corners on the centre of its colour's block
    uv = ob.data.uv_layers.active.data
    for p in ob.data.polygons:
        i = face_pal[p.index]
        cu, cv = (i % GRID + 0.5) / GRID, (i // GRID + 0.5) / GRID
        for li in p.loop_indices:
            uv[li].uv = (cu, cv)
    # one material showing the palette
    mat = bpy.data.materials.new(Path(png_path).stem.replace("T_", "M_"))
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    used = {}
    for i, a_ in zip(face_pal, areas):
        used[palette_hex[i]] = used.get(palette_hex[i], 0.0) + a_
    return {k: round(v / sum(areas), 3) for k, v in sorted(used.items(), key=lambda kv: -kv[1])}


def facet(ob):
    for p in ob.data.polygons:
        p.use_smooth = False
    ob.data.update()


# ------------------------------------------------------------------------------------------------ review sheet

def sheet(ob, path, marks, length, butt, tip):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 520
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    scene.world = world
    for name, at in marks.items():
        bpy.ops.mesh.primitive_cylinder_add(radius=0.006, depth=0.3, location=at, rotation=(0, math.pi / 4, 0))
        s = bpy.context.active_object
        s.name = "mark_" + name
        m = bpy.data.materials.new(name)
        m.diffuse_color = (1, 0.1, 0.6, 1) if name == "main" else (0.1, 0.9, 1, 1)
        s.data.materials.append(m)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = length * 1.15
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = []
    mid = Vector((0, (butt + tip) / 2, 0))
    for k in range(11):  # a tick every tenth of the length, butt to tip
        bpy.ops.mesh.primitive_cylinder_add(radius=0.002, depth=length * 0.12, location=(0, butt + k * length / 10, 0),
                                            rotation=(0, math.pi / 4, 0))
        tick = bpy.context.active_object
        tick.name = f"mark_tick{k}"
    # side (looking along -X) and face (looking down -Z), the weapon running left to right in both
    for i, (loc, rot) in enumerate([((length * 2, 0, 0), (math.pi / 2, 0, math.pi / 2)),
                                     ((0, 0, length * 2), (0, 0, math.pi / 2))]):
        cam.location = mid + Vector(loc)
        cam.rotation_euler = rot
        out = str(Path(path).with_suffix(f".{i}.png"))
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        shots.append(out)
    for o in [o for o in scene.objects if o.name.startswith("mark_")]:
        bpy.data.objects.remove(o)
    bpy.data.objects.remove(cam)
    return shots


def main():
    a = args()
    out = Path(a.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    grips = json.loads(Path(a.grip_json).read_text()) if a.grip_json else None
    swatch = json.loads(SWATCH.read_text())
    palette_hex = swatch["sets"][a.set]["palette"]

    ob = load(a.fbx)
    ob.name = a.name
    origin, ax, side, span, info = find_frame(ob, a, grips)
    scale = a.length / span
    to_frame(ob, origin, ax, side, scale, a.kind == "pistol")
    if a.grip_drop:
        ob.data.transform(Matrix.Translation((0, 0, a.grip_drop)))
        ob.data.update()
    used = paint(ob, palette_hex, out / f"T_{a.name}.png")
    facet(ob)

    pts = verts_np(ob)
    report = {
        "weapon": a.name, "kind": a.kind, "source": a.fbx, "scale": round(scale, 4),
        "frame": "Blender: grip at the origin, butt -> tip +Y, broad side +Z (a pistol's handle -Z)",
        "length_m": round(float(pts[:, 1].max() - pts[:, 1].min()), 4),
        "butt_y": round(float(pts[:, 1].min()), 4), "tip_y": round(float(pts[:, 1].max()), 4),
        "faces": len(ob.data.polygons), "palette_use": used,
        "main_hand": {"hand": "right", "point": [0, 0, 0]},
    }
    report.update({k: round(v, 4) if isinstance(v, float) else v for k, v in info.items()})
    marks = {"main": Vector((0, 0, 0))}
    if "off_hand_along" in info:
        y = float(pts[:, 1].min()) + info["off_hand_along"] * report["length_m"]
        report["off_hand"] = {"hand": "left", "point": [0, round(y, 4), 0]}
        marks["off"] = Vector((0, y, 0))

    fbx_path = out / f"{a.name}.fbx"
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={"MESH"},
                             mesh_smooth_type="FACE", path_mode="STRIP", bake_space_transform=False,
                             apply_scale_options="FBX_SCALE_ALL", add_leaf_bones=False)
    (out / f"{a.name}_grip.json").write_text(json.dumps(report, indent=1))
    if a.sheet:
        report["sheet"] = sheet(ob, a.sheet, marks, report["length_m"], report["butt_y"], report["tip_y"])
    if a.blend:
        bpy.ops.wm.save_as_mainfile(filepath=a.blend, copy=True)
    print("WEAPON_PASS " + json.dumps(report))


main()
