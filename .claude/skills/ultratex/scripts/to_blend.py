"""Blender (background): textured UltraTex GLB -> a .blend with a clean PBR material + a 6-view review sheet.

blender -b --factory-startup --python to_blend.py -- <textured.glb> <out.blend> <sheet.png>
        [--albedo A.png] [--roughness R.png] [--metallic M.png]

The textures sit next to the .blend and are linked relatively (not packed), so painting them
in another program shows up in Blender. Nothing is exported.
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
glb, out_blend, sheet = argv[0], argv[1], argv[2]
opt = {argv[i][2:]: argv[i + 1] for i in range(3, len(argv) - 1) if argv[i].startswith("--")}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
name = os.path.splitext(os.path.basename(out_blend))[0]
for i, o in enumerate(meshes):
    o.name = name if i == 0 else f"{name}_{i}"
# prep_mesh --yaw turned the mesh to match the picture; turn it back so the result keeps its own facing.
if "yaw" in opt and float(opt["yaw"]):
    bpy.ops.object.select_all(action="DESELECT")
    turn = Matrix.Rotation(-math.radians(float(opt["yaw"])), 4, "Z")
    for o in meshes:
        o.matrix_world = turn @ o.matrix_world  # glTF imports use quaternion rotation: go through the matrix
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

# Rebuild the material from the separate maps when we have them (the GLB has them packed too).
if "albedo" in opt:
    mat = bpy.data.materials.new(name + "_Mat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def tex(path, label, color, x, y):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(path)
        n.image.colorspace_settings.name = "sRGB" if color else "Non-Color"
        n.label, n.location = label, (x, y)
        return n

    a = tex(opt["albedo"], "Albedo", True, -500, 300)
    nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    if "roughness" in opt:
        r = tex(opt["roughness"], "Roughness", False, -500, 0)
        nt.links.new(r.outputs["Color"], bsdf.inputs["Roughness"])
    if "metallic" in opt:
        m = tex(opt["metallic"], "Metallic", False, -500, -300)
        nt.links.new(m.outputs["Color"], bsdf.inputs["Metallic"])
    for o in meshes:
        o.data.materials.clear()
        o.data.materials.append(mat)

# Frame: ground the model at z=0, centred.
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
shift = Vector((-(mn.x + mx.x) / 2, -(mn.y + mx.y) / 2, -mn.z))
for o in meshes:
    o.location += shift
size = max(mx - mn)

sc = bpy.context.scene
light = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
light.data.energy = 3.0
light.rotation_euler = (math.radians(50), 0, math.radians(30))
sc.collection.objects.link(light)
cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
cam.data.type, cam.data.ortho_scale = "ORTHO", size * 1.25
sc.collection.objects.link(cam)
sc.camera = cam

# Save the working file first (camera at the 3/4 view).
target = Vector((0, 0, (mx.z - mn.z) / 2))


def aim(az, el):
    d = size * 3
    cam.location = target + Vector((math.sin(az) * math.cos(el) * d, -math.cos(az) * math.cos(el) * d, math.sin(el) * d))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()


aim(math.radians(35), math.radians(20))
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile()

# Review sheet: workbench, texture colour, 3x2 views.
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.render.film_transparent = False
sc.render.resolution_x = sc.render.resolution_y = 640
views = [("front", 0, 5), ("3/4", 35, 20), ("side", 90, 5), ("back", 180, 5), ("other side", 270, 5), ("top", 0, 85)]
tiles = []
tmp = os.path.join(os.path.dirname(os.path.abspath(sheet)), "_tile.png")
for label, az, el in views:
    aim(math.radians(az), math.radians(el))
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(tmp)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    tiles.append(px)
    bpy.data.images.remove(img)
os.remove(tmp)
rows = [np.concatenate(tiles[i:i + 3], axis=1) for i in (3, 0)]  # bottom row first (Blender images start at the bottom)
grid = np.concatenate(rows, axis=0)
out = bpy.data.images.new("sheet", grid.shape[1], grid.shape[0])
out.pixels = grid.ravel()
out.filepath_raw, out.file_format = sheet, "PNG"
out.save()
print("BLEND_SAVED", out_blend)
print("SHEET_SAVED", sheet)
