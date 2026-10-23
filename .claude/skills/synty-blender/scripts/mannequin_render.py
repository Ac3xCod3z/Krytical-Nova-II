"""Pictures and MP4s of the Synty Mannequin Modeling scene (made by mannequin_build.py). Saves nothing to the .blend.

  run_blender.py --blend SyntyMannequinModeling.blend mannequin_render.py --lineup OUT.png [--size 4096]
        front row and back row of every mannequin at its defaults

  ... mannequin_render.py --tag CivilianF --columns "bare|;bust 100|kn_bust=1;both|kn_bust=1,kn_butt=1"
                          --views front,right,back --out OUT.png [--size 512]
        one mannequin, a column per setting (label|key=value,...), a row per view. Keys are shape keys
        (kn_bust, defaultHeavy, ...) or kn_height (the rig's property). Several tags: --tag A,B (columns repeat per tag).

  ... mannequin_render.py --tag CivilianF --sweep kn_bust,kn_butt --views front,right --video OUT.mp4 [--seconds 3]
        each slider in turn goes 0 -> max -> 0, views side by side, the slider's name and value burnt in

  ... mannequin_render.py --turntable OUT.mp4 [--seconds 6]
        every mannequin turns once on its own spot

  [--gear on|off|both]   draw the gear parts mannequin_gear.py added (default: as the scene has them)
"""
import argparse
import math
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

VIEWS = {"front": Vector((0, -1, 0.05)), "right": Vector((1, 0, 0.05)), "left": Vector((-1, 0, 0.05)),
         "back": Vector((0, 1, 0.05)), "threeq": Vector((0.7, -1, 0.3))}
HEIGHT_RANGE = (0.90, 1.10)


def manifest():
    import json
    raw = bpy.context.scene.get("kn_mannequins")
    if not raw:
        raise SystemExit("Not a mannequin scene (no kn_mannequins); build it with mannequin_build.py.")
    return json.loads(raw)


def meshes_of(entry):
    rig = bpy.data.objects[entry["rig"]]
    return rig, [o for o in rig.children if o.type == "MESH"]


def set_slider(rig, meshes, key, value):
    if key in ("kn_height", "kn_bust", "kn_butt", "kn_defined") and key in rig.keys():
        rig[key] = float(value)
        return True
    hit = False
    for o in meshes:
        ks = o.data.shape_keys
        if ks and key in ks.key_blocks:
            ks.key_blocks[key].value = float(value)
            hit = True
    return hit


def parse_columns(text):
    cols = []
    for chunk in text.split(";"):
        if not chunk.strip():
            continue
        label, _, settings = chunk.partition("|")
        values = {}
        for kv in settings.split(","):
            if "=" in kv:
                k, v = kv.split("=")
                values[k.strip()] = float(v)
        cols.append((label.strip(), values))
    return cols


class Studio:
    """Camera, light, world and a burnt-in caption; restores the scene when closed."""

    def __init__(self, size_x, size_y):
        scene = bpy.context.scene
        self.scene = scene
        self.old = (scene.camera, scene.world, scene.render.engine, scene.render.resolution_x, scene.render.resolution_y,
                    scene.render.resolution_percentage, scene.render.filepath, scene.view_settings.view_transform,
                    scene.render.image_settings.file_format, scene.render.film_transparent)
        self.cam_data = bpy.data.cameras.new("KN_Cam")
        self.cam_data.type = "ORTHO"
        self.cam_data.clip_end = 1000
        self.cam = bpy.data.objects.new("KN_Cam", self.cam_data)
        scene.collection.objects.link(self.cam)
        self.sun_data = bpy.data.lights.new("KN_Sun", "SUN")
        self.sun_data.energy = 2.5
        self.sun = bpy.data.objects.new("KN_Sun", self.sun_data)
        self.sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
        scene.collection.objects.link(self.sun)
        self.fill_data = bpy.data.lights.new("KN_Fill", "SUN")
        self.fill_data.energy = 1.0
        self.fill = bpy.data.objects.new("KN_Fill", self.fill_data)
        self.fill.rotation_euler = (math.radians(60), 0, math.radians(150))
        scene.collection.objects.link(self.fill)
        self.world = bpy.data.worlds.new("KN_World")
        self.world.use_nodes = True
        bg = self.world.node_tree.nodes["Background"]
        bg.inputs[0].default_value = (0.33, 0.34, 0.37, 1)
        bg.inputs[1].default_value = 1.0
        cu = bpy.data.curves.new("KN_Caption", type="FONT")
        cu.size = 1.0
        cu.align_x = "LEFT"
        self.caption = bpy.data.objects.new("KN_Caption", cu)
        self.caption.parent = self.cam
        scene.collection.objects.link(self.caption)
        mat = bpy.data.materials.new("KN_CaptionMat")
        mat.use_nodes = True
        mat.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (1, 1, 1, 1)
        mat.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 3.0
        cu.materials.append(mat)
        self.caption_mat = mat
        scene.camera, scene.world = self.cam, self.world
        try:
            scene.render.engine = "BLENDER_EEVEE"
        except TypeError:
            scene.render.engine = "BLENDER_EEVEE_NEXT"
        scene.render.resolution_x, scene.render.resolution_y = size_x, size_y
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.view_settings.view_transform = "Standard"
        self.aspect = size_x / size_y

    def frame(self, lo, hi, pad=1.15):
        """Ortho scale and centre so the box lo..hi fills the picture (ortho_scale is the picture's larger side)."""
        self.centre = (lo + hi) / 2
        span = hi - lo
        w_need, h_need = max(span.x, span.y) * pad, span.z * pad
        if self.aspect >= 1:
            self.cam_data.ortho_scale = max(w_need, h_need * self.aspect, 0.3)
        else:
            self.cam_data.ortho_scale = max(h_need, w_need / self.aspect, 0.3)

    def picture_size(self):
        s = self.cam_data.ortho_scale
        return (s, s / self.aspect) if self.aspect >= 1 else (s * self.aspect, s)

    def shot(self, direction, path, text=None):
        d = direction.normalized()
        self.cam.location = self.centre + d * 60
        self.cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        if text:
            self.caption.data.body = text
            w, h = self.picture_size()
            self.caption.data.size = h * 0.05
            self.caption.location = (-w / 2 + h * 0.03, h / 2 - h * 0.08, -1.0)
            self.caption.hide_render = False
        else:
            self.caption.hide_render = True
        self.scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)

    def close(self):
        s = self.scene
        (s.camera, s.world, s.render.engine, s.render.resolution_x, s.render.resolution_y, s.render.resolution_percentage,
         s.render.filepath, s.view_settings.view_transform, s.render.image_settings.file_format,
         s.render.film_transparent) = self.old
        cu = self.caption.data
        for o in (self.caption, self.cam, self.sun, self.fill):
            bpy.data.objects.remove(o)
        bpy.data.curves.remove(cu)
        bpy.data.cameras.remove(self.cam_data)
        bpy.data.lights.remove(self.sun_data)
        bpy.data.lights.remove(self.fill_data)
        bpy.data.worlds.remove(self.world)
        bpy.data.materials.remove(self.caption_mat)


def load_px(path, w, h):
    img = bpy.data.images.load(str(path))
    px = np.array(img.pixels[:], np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return px


def save_grid(rows, out, w, h, gap=6):
    """rows: list of lists of PNG paths, all w x h. Pixel rows run bottom to top in Blender."""
    cols = max(len(r) for r in rows)
    sheet = np.full((len(rows) * h + (len(rows) + 1) * gap, cols * w + (cols + 1) * gap, 4), 0.1, np.float32)
    sheet[..., 3] = 1.0
    for r, row in enumerate(rows):
        for c, path in enumerate(row):
            y = (len(rows) - 1 - r) * h + (len(rows) - r) * gap
            x = c * w + (c + 1) * gap
            sheet[y:y + h, x:x + w] = load_px(path, w, h)
    img = bpy.data.images.new("KN_Grid", sheet.shape[1], sheet.shape[0], alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = str(out)
    img.file_format = "PNG"
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    img.save()
    bpy.data.images.remove(img)


def bounds(objects):
    objects = [o for o in objects if not o.hide_render] or list(objects)
    pts = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    return (Vector((min(p[i] for p in pts) for i in range(3))), Vector((max(p[i] for p in pts) for i in range(3))))


def ffmpeg_mp4(frames_dir, pattern, fps, out):
    exe = shutil.which("ffmpeg")
    if not exe:
        raise SystemExit("ffmpeg not found on PATH; the frames are in " + str(frames_dir))
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    cmd = [exe, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", str(frames_dir / pattern),
           "-c:v", "libx264", "-pix_fmt", "yuv420p", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
           "-movflags", "+faststart", str(out)]
    subprocess.check_call(cmd)


def solo(entries, tags):
    """Draw only the mannequins named (their rigs' children); everything else, labels included, stays out."""
    keep = set()
    for e in entries:
        if e["tag"] in tags:
            keep.update(o.name for o in bpy.data.objects[e["rig"]].children)
    for o in bpy.data.objects:
        if o.type in ("MESH", "FONT") and o.name not in keep:
            o.hide_render = True


def apply_gear(entries, mode):
    """Show or hide gear objects (mannequin_gear.py marks them kn_gear) and the body parts they replace."""
    if mode is None:
        return
    for e in entries:
        rig, meshes = meshes_of(e)
        gear = [o for o in meshes if o.get("kn_gear")]
        replaced = {o["kn_replaces"] for o in gear if o.get("kn_replaces")}
        for o in gear:
            o.hide_render = mode == "off"
        for o in meshes:
            if o.name in replaced:
                o.hide_render = mode == "on"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lineup")
    ap.add_argument("--tag")
    ap.add_argument("--columns")
    ap.add_argument("--views", default="front,right,back")
    ap.add_argument("--out")
    ap.add_argument("--sweep")
    ap.add_argument("--video")
    ap.add_argument("--turntable")
    ap.add_argument("--seconds", type=float, default=3.0)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--size", type=int)
    ap.add_argument("--gear", choices=("on", "off", "both"))
    a = ap.parse_args(kn.tool_args())
    for name in ("lineup", "out", "video", "turntable"):      # Blender resolves bare relative paths elsewhere
        if getattr(a, name):
            setattr(a, name, str(Path(getattr(a, name)).resolve()))
    scene = bpy.context.scene
    entries = manifest()
    by_tag = {e["tag"]: e for e in entries}
    saved = []
    for e in entries:
        rig, meshes = meshes_of(e)
        saved.append((rig, {k: rig[k] for k in ("kn_height", "kn_bust", "kn_butt", "kn_defined") if k in rig.keys()}, [(k, k.value) for o in meshes if o.data.shape_keys
                                                      for k in o.data.shape_keys.key_blocks], rig.rotation_euler.copy()))
    hidden_state = [(o, o.hide_render) for o in bpy.data.objects]
    tmp = Path(tempfile.mkdtemp(prefix="kn_mann_"))
    gear_modes = ["off", "on"] if a.gear == "both" else [a.gear]
    result = {}
    try:
        if a.lineup:
            size = a.size or 4096
            apply_gear(entries, gear_modes[0])
            lo, hi = bounds([o for e in entries for o in meshes_of(e)[1]])
            lo.z -= 0.3
            studio = Studio(size, max(256, int(size * (hi.z - lo.z) * 1.1 / max(hi.x - lo.x, 0.1))))
            studio.frame(lo, hi, 1.05)
            rows = []
            for view in ("front", "back"):
                p = tmp / f"lineup_{view}.png"
                studio.shot(VIEWS[view], p, f"Synty Mannequin Modeling  |  {view}")
                rows.append([p])
            w, h = scene.render.resolution_x, scene.render.resolution_y
            studio.close()
            save_grid(rows, a.lineup, w, h)
            result["lineup"] = a.lineup

        if a.tag and a.columns and a.out:
            size = a.size or 512
            tags = [t.strip() for t in a.tag.split(",")]
            views = [v.strip() for v in a.views.split(",")]
            cols = parse_columns(a.columns)
            solo(entries, tags)
            studio = Studio(size, size)
            rows = [[] for _ in views]
            for tag in tags:
                e = by_tag[tag]
                rig, meshes = meshes_of(e)
                for o, was in hidden_state:                         # draw this figure alone
                    o.hide_render = was
                solo(entries, {tag})
                set_slider(rig, meshes, "kn_height", HEIGHT_RANGE[1])
                scene.frame_set(scene.frame_current)
                lo, hi = bounds(meshes)
                lo.z -= 0.05
                studio.frame(lo, hi, 1.06)                      # one camera per figure: height differences stay visible
                set_slider(rig, meshes, "kn_height", 1.0)
                for gm in gear_modes:
                    apply_gear([e], gm)
                    for label, values in cols:
                        for k in set(k for _l, v in cols for k in v) | {"kn_height"}:
                            set_slider(rig, meshes, k, 1.0 if k == "kn_height" else 0.0)
                        for k, v in values.items():
                            if not set_slider(rig, meshes, k, v):
                                result.setdefault("missing_keys", []).append(f"{tag}:{k}")
                        scene.frame_set(scene.frame_current)
                        for r, view in enumerate(views):
                            p = tmp / f"{tag}_{gm}_{label}_{view}.png".replace(" ", "_").replace("/", "")
                            cap = f"{tag}  {label}" + (f"  gear {gm}" if a.gear == "both" else "")
                            studio.shot(VIEWS[view], p, cap)
                            rows[r].append(p)
                for k in set(k for _l, v in cols for k in v):
                    set_slider(rig, meshes, k, 1.0 if k == "kn_height" else 0.0)
            studio.close()
            if len(views) == 1 and len(tags) > 1:                 # one figure per row reads better on a phone
                per = len(rows[0]) // len(tags)
                rows = [rows[0][i * per:(i + 1) * per] for i in range(len(tags))]
            save_grid(rows, a.out, size, size)
            result["sheet"] = a.out
            result["layout"] = f"columns: {[c[0] for c in cols]} per tag {tags}; rows: {views}"

        if a.tag and a.sweep and a.video:
            size = a.size or 480
            views = [v.strip() for v in a.views.split(",")]
            keys = [k.strip() for k in a.sweep.split(",")]
            tags = [t.strip() for t in a.tag.split(",")]
            n = max(2, int(a.seconds * a.fps))
            frames_dir = tmp / "sweep"
            frames_dir.mkdir()
            solo(entries, tags)
            studio = Studio(size, size)
            frame = 0
            for tag in tags:
                e = by_tag[tag]
                rig, meshes = meshes_of(e)
                for gm in gear_modes:
                    apply_gear([e], gm)
                    for key in keys:
                        lo_v, hi_v = (HEIGHT_RANGE if key == "kn_height" else (0.0, 1.0))
                        for i in range(n):
                            t = i / (n - 1)
                            v = lo_v + (hi_v - lo_v) * (0.5 - 0.5 * math.cos(2 * math.pi * t))
                            set_slider(rig, meshes, key, v)
                            scene.frame_set(scene.frame_current)
                            lo, hi = bounds(meshes)
                            lo.z -= 0.05
                            if i == 0:
                                box = (lo.copy(), hi.copy())
                                box[1].z *= HEIGHT_RANGE[1] if key == "kn_height" else 1.0
                            studio.frame(box[0], box[1], 1.08)
                            shown = f"{v * 100:.0f}" if key != "kn_height" else f"x{v:.2f}"
                            cap = f"{tag}  {key} {shown}" + (f"  gear {gm}" if a.gear == "both" else "")
                            paths = []
                            for view in views:
                                p = tmp / f"sw_{view}.png"
                                studio.shot(VIEWS[view], p, cap if view == views[0] else None)
                                paths.append(p)
                            save_grid([paths], frames_dir / f"f_{frame:05d}.png", size, size, gap=4)
                            frame += 1
                        set_slider(rig, meshes, key, 1.0 if key == "kn_height" else 0.0)
            studio.close()
            ffmpeg_mp4(frames_dir, "f_%05d.png", a.fps, a.video)
            result["video"] = a.video
            result["frames"] = frame

        if a.turntable:
            size = a.size or 360
            apply_gear(entries, gear_modes[0])
            n = max(2, int(a.seconds * a.fps))
            frames_dir = tmp / "turn"
            frames_dir.mkdir()
            studio = Studio(size, size * 2)
            boxes = {}
            for e in entries:
                rig, meshes = meshes_of(e)
                lo, hi = bounds(meshes)
                lo.z -= 0.05
                r = max(hi.x - lo.x, hi.y - lo.y) / 2 * 1.05     # the figure turns: frame its turning circle
                c = (lo + hi) / 2
                boxes[e["tag"]] = (Vector((c.x - r, c.y - r, lo.z)), Vector((c.x + r, c.y + r, hi.z)))
            lineages = []
            for e in entries:
                if e["lineage"] not in lineages:
                    lineages.append(e["lineage"])
            for i in range(n):
                ang = 2 * math.pi * i / n
                for e in entries:
                    bpy.data.objects[e["rig"]].rotation_euler.z = ang
                scene.frame_set(scene.frame_current)
                rows = [[], []]
                for lin in lineages:
                    for r, sex in enumerate(("M", "F")):
                        e = by_tag.get(f"{lin}{sex}")
                        solo(entries, {e["tag"]})
                        studio.frame(*boxes[e["tag"]], 1.0)
                        p = tmp / f"t_{e['tag']}.png"
                        studio.shot(VIEWS["front"], p, f"{lin} {sex}")
                        rows[r].append(p)
                        for o in bpy.data.objects:                 # solo() hides; show all again for the next
                            if o.type in ("MESH", "FONT"):
                                o.hide_render = False
                        apply_gear(entries, gear_modes[0])
                save_grid(rows, frames_dir / f"f_{i:05d}.png", size, size * 2, gap=4)
            studio.close()
            ffmpeg_mp4(frames_dir, "f_%05d.png", a.fps, a.turntable)
            result["turntable"] = a.turntable
    finally:
        for rig, h, keys, rot in saved:
            for k, v in h.items():
                rig[k] = v
            rig.rotation_euler = rot
            for k, v in keys:
                k.value = v
        for o, was in hidden_state:
            o.hide_render = was
        shutil.rmtree(tmp, ignore_errors=True)
    kn.report("Mannequin render", result)


main()
