"""A Tripo T-pose model (gear modelled on our mannequin, mannequin still in it) onto the lineage's bare body, with the
mannequin taken out (step 3 of references/tripo-gear-set-playbook.md; done by hand on the Dock Runner).

  blender -b --python tpose_import.py -- --fbx <baked or exported model.fbx> --lineage "Robot Male" --name <Set>_<Piece>
        --out <Gear folder> [--grey 0.30,0.85] [--near 0.025]

1. Appends the lineage's bare body (the slots of its collection in SyntyMannequinModeling.blend).
2. Imports the model, joined, transforms applied. Tripo shrinks a model to about 1 m: it is scaled by height and arm
   span together to the bare body, feet on the floor, centred on the body.
3. The mannequin: faces whose colour is a neutral mid grey (value in --grey, saturation under 0.16) that lie within
   --near metres of the bare body's surface. They are deleted, then loose bits under 30 faces that were grey. A hidden
   copy keeps everything (<name>_WithMannequin).
4. Saves <out>/<name>.blend and a sheet <out>/<name>_import.png (front and back, the piece over the bare body), and
   prints what it did.
"""
import argparse
import colorsys
import json
import math
import re
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SCENE = Path(r"G:\My Drive\KryticalNova\Art\Blender\Sidekick\SyntyMannequinModeling.blend")
BODY = re.compile(r"_(01HEAD|1[0-9][A-Z]{4}|2[01][A-Z]{4})_")


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", required=True)
    p.add_argument("--lineage", required=True, help='a collection of the mannequin scene, e.g. "Robot Male"')
    p.add_argument("--name", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--grey", default="0.30,0.85")
    p.add_argument("--near", type=float, default=0.025)
    p.add_argument("--by-island", action="store_true",
                   help="a model generated in parts: drop whole islands that are mostly grey and lie on the body, never"
                        " single faces (per face, a helmet's light side panels went with the mannequin)")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def bare_body(collection):
    with bpy.data.libraries.load(str(SCENE), link=False) as (src, dst):
        dst.collections = [c for c in src.collections if c == collection]
    col = dst.collections[0]
    bpy.context.scene.collection.children.link(col)
    body = []
    for o in list(col.all_objects):
        if o.type == "MESH" and BODY.search(o.name) and "HAIR" not in o.name:
            body.append(o)
        elif o.type == "MESH":
            o.hide_set(True)
            o.hide_render = True
    grey = bpy.data.materials.new("kn_bare_grey")
    grey.diffuse_color = (0.55, 0.55, 0.57, 1.0)
    for o in body:
        o.data = o.data.copy()
        o.data.materials.clear()
        o.data.materials.append(grey)
        if o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks[1:]:
                kb.value = 0.0
    return body


def world_points(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        pts += [o.matrix_world @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    return pts


def body_tree(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        part = bmesh.new()
        part.from_mesh(me)
        part.transform(o.matrix_world)
        tmp = bpy.data.meshes.new("kn_tmp")
        part.to_mesh(tmp)
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        part.free()
        ev.to_mesh_clear()
    return BVHTree.FromBMesh(bm)


def load(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    others = [o.name for o in new if o.type != "MESH"]
    meshes = [o for o in new if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    for n in others:
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n])
    return ob


def face_colours(ob, size=2048):
    img = None
    for slot in ob.material_slots:
        m = slot.material
        if m and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    img = n.image
    if img is None:
        raise SystemExit("The model has no texture to tell the mannequin by.")
    if img.size[0] > size:
        img.scale(size, size)
    w, h = img.size
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)[..., :3]
    uv = ob.data.uv_layers.active.data
    out = []
    for p in ob.data.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        c = sum((Vector(x) for x in pts), Vector((0, 0))) / len(pts)
        out.append(px[min(h - 1, max(0, int((c.y % 1.0) * h))), min(w - 1, max(0, int((c.x % 1.0) * w)))])
    return out


def sheet(ob, body, path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    pts = world_points([ob] + body)
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    mid = (lo + hi) / 2
    cd = bpy.data.cameras.new("kn_cam")
    cd.type = "ORTHO"
    aspect = scene.render.resolution_x / scene.render.resolution_y
    cd.ortho_scale = max(hi.x - lo.x, (hi.z - lo.z) * aspect) * 1.06
    cam = bpy.data.objects.new("kn_cam", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = []
    for tag, d in (("front", Vector((0, -1, 0))), ("back", Vector((0, 1, 0)))):
        cam.location = mid + d * 10
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        f = str(Path(path).with_suffix(f".{tag}.png"))
        scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        shots.append(f)
    bpy.data.objects.remove(cam)
    return shots


def main():
    a = args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body = bare_body(a.lineage)
    ob = load(a.fbx)
    ob.name = ob.data.name = a.name
    for m in ob.data.materials:
        if m:
            m.name = a.name
    for slot in ob.material_slots:
        for n in (slot.material.node_tree.nodes if slot.material and slot.material.node_tree else []):
            if n.type == "TEX_IMAGE" and n.image:
                n.image.name = a.name + "_basecolor"

    # size by height and span together, feet down, centred on the body
    bp = world_points(body)
    mp = [v.co.copy() for v in ob.data.vertices]

    def ext(pts, i):
        return max(p[i] for p in pts) - min(p[i] for p in pts)
    s = (ext(bp, 2) / ext(mp, 2) + ext(bp, 0) / ext(mp, 0)) / 2
    bc = Vector(((max(p.x for p in bp) + min(p.x for p in bp)) / 2, (max(p.y for p in bp) + min(p.y for p in bp)) / 2,
                 min(p.z for p in bp)))
    mc = Vector(((max(p.x for p in mp) + min(p.x for p in mp)) / 2, (max(p.y for p in mp) + min(p.y for p in mp)) / 2,
                 min(p.z for p in mp)))
    for v in ob.data.vertices:
        v.co = bc + (v.co - mc) * s
    ob.data.update()

    copy = ob.copy()
    copy.data = ob.data.copy()
    copy.name = a.name + "_WithMannequin"
    bpy.context.scene.collection.objects.link(copy)

    # the mannequin: neutral grey faces lying on the bare body
    g0, g1 = (float(x) for x in a.grey.split(","))
    cols = face_colours(ob)
    tree = body_tree(body)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    gone = []
    if a.by_island:
        seen = set()
        for f in bm.faces:
            if f.index in seen:
                continue
            stack, isle = [f], []
            seen.add(f.index)
            while stack:
                x = stack.pop()
                isle.append(x)
                for e in x.edges:
                    for y in e.link_faces:
                        if y.index not in seen:
                            seen.add(y.index)
                            stack.append(y)
            area = sum(x.calc_area() for x in isle) or 1e-9
            grey_area = near_area = loose_near = mid_grey = 0.0
            for x in isle:
                hh, ss, vv = colorsys.rgb_to_hsv(*[float(t) for t in cols[x.index]])
                if ss < 0.16 and g0 <= vv <= g1:
                    grey_area += x.calc_area()
                if ss < 0.12 and 0.30 <= vv <= 0.72:       # the mannequin's own mid grey, not light bone trim
                    mid_grey += x.calc_area()
                hit = tree.find_nearest(x.calc_center_median())
                if hit[0] is not None and hit[3] <= a.near:
                    near_area += x.calc_area()
                if hit[0] is not None and hit[3] <= 3 * a.near:
                    loose_near += x.calc_area()
            # an island all but entirely grey is the generated mannequin even where it is bulkier than our body
            # (Scrap Sentinel: chest, arm and shin pieces 3-7 cm off the bare robot stayed in the cape)
            if (grey_area / area > 0.6 and near_area / area > 0.6) or (mid_grey / area > 0.85 and loose_near / area > 0.5):
                gone += isle
    for f, c in ([] if a.by_island else zip(bm.faces, cols)):
        hh, ss, vv = colorsys.rgb_to_hsv(*[float(x) for x in c])
        if ss < 0.16 and g0 <= vv <= g1:
            hit = tree.find_nearest(f.calc_center_median())
            if hit[0] is not None and hit[3] <= a.near:
                gone.append(f)
    grey_faces = len(gone)
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    # loose grey scraps left behind
    bm.faces.ensure_lookup_table()
    seen, scraps = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isle = [f], []
        seen.add(f.index)
        while stack:
            x = stack.pop()
            isle.append(x)
            for e in x.edges:
                for y in e.link_faces:
                    if y.index not in seen:
                        seen.add(y.index)
                        stack.append(y)
        if len(isle) < 30:
            scraps += isle
    bmesh.ops.delete(bm, geom=scraps, context="FACES")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    copy.hide_set(True)
    copy.hide_render = True

    shots = sheet(ob, body, out / f"{a.name}_import.png")
    bpy.ops.wm.save_as_mainfile(filepath=str(out / f"{a.name}.blend"))
    print("TPOSE " + json.dumps({"name": a.name, "scale": round(s, 4), "mannequin_faces_removed": grey_faces,
                                 "scraps_removed": len(scraps), "faces_left": len(ob.data.polygons), "sheet": shots}))


main()
