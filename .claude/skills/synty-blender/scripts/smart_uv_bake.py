"""Bakes a Tripo model's texture onto its Smart UV unwrap (owner, 24 Oct 2026: the Smart UV layout is the one to
paint on when a spot needs fixing, rather than Tripo's scattered islands).

  blender -b --python smart_uv_bake.py -- --textured <model.fbx> [--smart <SmartUV.fbx>] --out <dir> --name <Set>_<Piece>
        [--size 8192]

--textured is the textured export (in parts mode one texture per part); --smart is the same mesh unwrapped by Tripo's
Smart UV (Smart UV removes the texture and does not take a multi-part model, so it is run on the single-mesh version
of the same generation). Cycles bakes the textured model's base colour onto the Smart UV mesh, selected to active,
through a small cage. Writes <name>_basecolor.png (the Smart UV layout), <name>.fbx (Smart UV mesh with that texture,
ready for gear_first_pass.py) and <name>_SmartUV.blend (both models, to paint fixes and bake again).
"""
import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--textured", required=True)
    p.add_argument("--smart", help="Tripo's Smart UV export; without it the model is unwrapped here (multi-part models,"
                   " which Tripo's Smart UV refuses)")
    p.add_argument("--out", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--size", type=int, default=8192)
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


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
    for n in others:  # empties and armatures the export brought; joined meshes are already gone
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n])
    return ob


def main():
    a = args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src = load(a.textured)
    src.name = a.name + "_TripoTextured"
    if a.smart:
        dst = load(a.smart)
    else:
        # Blender's Smart UV Project on a copy: clean islands per part, packed with a margin
        dst = src.copy()
        dst.data = src.data.copy()
        bpy.context.scene.collection.objects.link(dst)
        for layer in list(dst.data.uv_layers):
            dst.data.uv_layers.remove(layer)
        dst.data.uv_layers.new(name="SmartUV")
        bpy.ops.object.select_all(action="DESELECT")
        dst.select_set(True)
        bpy.context.view_layer.objects.active = dst
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.0)
        bpy.ops.object.mode_set(mode="OBJECT")
    dst.name = a.name
    dst.data.name = a.name
    # the two exports should lie on top of each other; say how far apart their middles are
    def middle(o):
        pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
        return sum(pts, Vector()) / 8, max((p - q).length for p in pts for q in pts)
    ms, ss = middle(src)
    md, sd = middle(dst)
    print(f"SMARTUV middles {(ms - md).length:.4f} apart, sizes {ss:.3f} / {sd:.3f}")

    img = bpy.data.images.new(a.name + "_basecolor", a.size, a.size, alpha=False)
    mat = bpy.data.materials.new(a.name)
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes.active = tex
    dst.data.materials.clear()
    dst.data.materials.append(mat)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.device = "CPU"
    bake = scene.render.bake
    bake.use_selected_to_active = True
    bake.use_cage = False
    bake.cage_extrusion = sd * 0.01
    bake.max_ray_distance = sd * 0.03
    bake.margin = 8
    bpy.ops.object.select_all(action="DESELECT")
    src.select_set(True)
    dst.select_set(True)
    bpy.context.view_layer.objects.active = dst
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})

    png = out / f"{a.name}_basecolor.png"
    img.filepath_raw = str(png)
    img.file_format = "PNG"
    img.save()
    src.hide_set(True)
    src.hide_render = True
    bpy.ops.object.select_all(action="DESELECT")
    dst.select_set(True)
    bpy.context.view_layer.objects.active = dst
    bpy.ops.export_scene.fbx(filepath=str(out / f"{a.name}.fbx"), use_selection=True, object_types={"MESH"},
                             path_mode="COPY", embed_textures=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out / f"{a.name}_SmartUV.blend"))
    print(f"SMARTUV baked {png} ({a.size}px), {len(dst.data.polygons)} faces")


main()
