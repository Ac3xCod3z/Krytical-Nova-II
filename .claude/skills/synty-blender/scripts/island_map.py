"""Numbered picture of every loose piece (island) of a mesh, to choose islands for conform_to_body.py or to find
extra pieces to delete (Brambleback, 24 Oct 2026: Tripo made two stirrup pairs where the art has one).

  blender -b <scene.blend> --python island_map.py -- --object <piece> --out <folder/prefix> [--min 30]

Writes <prefix>_side/_below/_front.png with each island in its own colour and its id at its middle, plus
<prefix>_islands.txt (id, vertex count, world bounds). Nothing is saved to the blend. Island ids follow vertex
connectivity order, so they stay valid until the mesh itself changes.
"""
import argparse
import colorsys
import importlib
import os
import sys

import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import kn_common as kn
importlib.reload(kn)
from conform_to_body import islands  # noqa: E402  (same island order as the conform tool)


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--object", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--min", type=int, default=30, help="label islands with at least this many vertices")
    return p.parse_args(kn.tool_args())


def main():
    a = args()
    ob = bpy.data.objects[a.object]
    sc = bpy.context.scene
    for o in sc.objects:
        if o.type in ('MESH', 'FONT', 'CURVE') and o is not ob:
            o.hide_render = True
    me = ob.data
    isl = islands(me)
    mw = ob.matrix_world
    co = np.array([tuple(mw @ v.co) for v in me.vertices])
    attr = me.color_attributes.new("island_map", 'BYTE_COLOR', 'POINT')
    for i, ids in enumerate(isl):
        rgb = colorsys.hsv_to_rgb((i * 0.618) % 1, 0.8, 0.95)
        for v in ids:
            attr.data[v].color = (*rgb, 1)
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    with open(a.out + "_islands.txt", "w") as f:
        for i, ids in enumerate(isl):
            p = co[ids]
            f.write(f"{i} n={len(ids)} lo={p.min(0).round(3).tolist()} hi={p.max(0).round(3).tolist()}\n")
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'VERTEX'
    sc.display.shading.light = 'STUDIO'
    if not sc.world:
        sc.world = bpy.data.worlds.new("w")
    lo, hi = co.min(0), co.max(0)
    label_later = []
    c, ext = Vector((lo + hi) / 2), Vector(hi - lo)
    try:
        from PIL import Image, ImageDraw
    except ImportError:  # Blender's own Python has no Pillow: labels go to <prefix>_<view>_labels.txt instead
        Image = None
    for view, d in (("side", (1, 0, 0)), ("below", (0.3, 0.2, -1)), ("front", (0.25, -1, 0.1))):
        d = Vector(d).normalized()
        cam = bpy.data.objects.new("im_" + view, bpy.data.cameras.new("im_" + view))
        sc.collection.objects.link(cam)
        sc.camera = cam
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = ext.length * 0.9
        cam.location = c + d * ext.length * 3
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x = sc.render.resolution_y = 1100
        path = f"{a.out}_{view}.png"
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        bpy.context.view_layer.update()
        marks = []
        for i, ids in enumerate(isl):
            if len(ids) >= a.min:
                q = world_to_camera_view(sc, cam, Vector(co[ids].mean(0)))
                marks.append((i, q.x, 1 - q.y))
        if Image:
            im = Image.open(path).convert("RGB")
            dr = ImageDraw.Draw(im)
            for i, x, y in marks:
                dr.text((x * im.width, y * im.height), str(i), fill=(0, 0, 0), stroke_width=2, stroke_fill=(255, 255, 255))
            im.save(path)
        else:
            with open(f"{a.out}_{view}_labels.txt", "w") as f:
                f.writelines(f"{i} {x:.4f} {y:.4f}\n" for i, x, y in marks)
            label_later.append(view)
    me.color_attributes.remove(attr)
    kn.report("island_map", {"object": a.object, "islands": len(isl), "pictures": a.out + "_*.png"},
              warnings=[f"no Pillow in Blender: label {v} with label_islands.py" for v in label_later])


main()
