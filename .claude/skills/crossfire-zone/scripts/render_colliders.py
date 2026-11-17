# blender -b Crucible_022b_Data.blend --python render_colliders.py -- <out.png> : the Unity colliders alone (Workbench, random colours), blockers see-through red
import bpy, sys, math
from mathutils import Vector
out = sys.argv[sys.argv.index('--') + 1]; sc = bpy.context.scene; D = bpy.data
for o in list(sc.objects):
    keep = o.name.startswith(('UCX_', 'Blocker_')) or o.get('mesh_collider_ok')
    if o.type in ('MESH', 'CURVE') and not keep: o.hide_render = True
    elif keep: o.hide_render = False
for c in D.collections: c.hide_render = False
for o in sc.objects:
    if o.type in ('MESH',) and not (o.name.startswith(('UCX_', 'Blocker_')) or o.get('mesh_collider_ok')): o.hide_render = True
bm = D.materials.new('BlockerRed'); bm.diffuse_color = (1.0, 0.1, 0.1, 0.18)
for o in sc.objects:
    if o.name.startswith('Blocker_'): o.color = (1, 0.1, 0.1, 0.25)
    elif o.get('mesh_collider_ok'): o.color = (0.35, 0.35, 0.42, 1)
sc.render.engine = 'BLENDER_WORKBENCH'; sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'RANDOM'; sh.show_xray = False
sh.show_cavity = True; sh.show_object_outline = True
cam_d = D.cameras.new('C'); cam = D.objects.new('C', cam_d); sc.collection.objects.link(cam); sc.camera = cam
cam.location = (0.0, -300.0, 190.0); d = Vector((0, 10, -10)) - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); cam_d.lens = 30; cam_d.clip_end = 3000
sc.render.resolution_x, sc.render.resolution_y = 2400, 1600; sc.render.filepath = out; bpy.ops.render.render(write_still=True)
