"""Blender (background): turn any mesh file into the clean GLB UltraTex wants.

blender -b --factory-startup --python prep_mesh.py -- <in.fbx|obj|glb|ply> <out.glb> [--yaw DEG] [--keep-uvs]

One mesh, one material, transforms applied, no animation. Faces over 1M get decimated to 1M.
--yaw turns the mesh about the up axis so the reference picture's view lines up with the front camera.
Prints PREP_INFO {...} (tris, uvs, dims) for run.py.
"""
import bpy, sys, os, json, math

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
yaw = float(argv[argv.index("--yaw") + 1]) if "--yaw" in argv else 0.0
keep_uvs = "--keep-uvs" in argv

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(src)[1].lower()
if ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=src)
elif ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
elif ext == ".obj":
    bpy.ops.wm.obj_import(filepath=src)
elif ext == ".ply":
    bpy.ops.wm.ply_import(filepath=src)
else:
    raise SystemExit(f"Unsupported mesh type: {ext}")

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("No mesh in " + src)
for o in bpy.data.objects:
    if o.type != "MESH":
        bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
ob.parent = None
ob.name = ob.data.name = "UltraTexMesh"
ob.rotation_euler.z += math.radians(yaw)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

me = ob.data
had_uvs = len(me.uv_layers) > 0
if not keep_uvs:
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
mat = bpy.data.materials.new("UltraTexMat")
me.materials.clear()
me.materials.append(mat)

tris = sum(len(p.vertices) - 2 for p in me.polygons)
if tris > 1_000_000:
    mod = ob.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = 1_000_000 / tris
    bpy.ops.object.modifier_apply(modifier=mod.name)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)

os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=True,
                          export_animations=False, export_skins=False, export_morph=False)
print("PREP_INFO " + json.dumps({"tris": tris, "had_uvs": had_uvs, "kept_uvs": keep_uvs and had_uvs,
                                  "dims": [round(d, 3) for d in ob.dimensions], "yaw": yaw}))
