# blender -b <file.blend> --python render_shot.py -- <out.png> <shot> [w h]
# One camera per concept painting, at the painting's own angle.
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
out, shot = a[0], a[1]; W, H = (int(a[2]), int(a[3])) if len(a) > 3 else (1536, 1024)
SHOTS = {  # name: (camera position, look-at, lens mm)
    '02_hegemony': ((-134.6, 13.9, 1.7), (-118.0, -1.2, 5.6), 21.0),
    '02_wide': ((-158.0, 0.0, 14.0), (-118.0, 0.0, 3.0), 20.0),
    '03_covenant': ((134.0, 0.0, 2.2), (160.0, 0.0, 6.5), 17.0),
    '04_null': ((12.0, -158.6, 4.6), (-3.5, -118.0, 3.6), 17.0),
    '04_null_b': ((13.5, -146.0, 2.2), (-3.5, -118.0, 5.2), 20.0),
    '01_heart': ((0.0, -52.0, -4.6), (0.0, 40.0, -3.4), 22.0),
    '05_panorama': ((0.0, -335.0, 215.0), (0.0, 12.0, -12.0), 30.0),
    'own4_sealed_gate': ((0.0, 58.0, -1.0), (0.0, 116.0, 8.0), 26.0),
    'own1_cov_gate_out': ((86.0, -34.0, 32.0), (115.0, 4.0, 4.0), 32.0),
    'own2_heg_gate_out': ((-86.0, -34.0, 32.0), (-115.0, 4.0, 4.0), 32.0),
    'own3_arena_cover': ((22.0, -26.0, -4.6), (46.0, -52.0, -4.0), 24.0),
    'gate_null_out': ((24.0, -84.0, 26.0), (0.0, -115.0, 4.0), 32.0),
    'top': ((0.0, 0.0, 520.0), (0.0, 0.01, 0.0), 35.0),
    'fl_heg': ((-158.0, 0.0, 14.0), (-118.0, 0.0, 3.0), 20.0),
    'fl_cov': ((155.5, 15.0, 16.0), (124.0, -5.0, 0.0), 19.0),
    'fl_null': ((0.0, -158.0, 15.0), (0.0, -124.0, 0.0), 19.0),
    'fl_arena': ((0.0, -78.0, 38.0), (0.0, 5.0, -7.5), 24.0),
    'fl_rim': ((70.0, -150.0, 60.0), (60.0, -60.0, -8.0), 24.0),
    'ref_heart': ((0.0, -22.0, -3.7), (0.0, 4.0, -7.1), 21.0),
    'ref_cov_shrine': ((137.0, 0.0, 2.4), (156.0, 0.0, 3.6), 20.0),
    'ref_null_shrine': ((11.2, -157.4, 2.6), (-4.0, -125.0, 3.4), 18.0),
    'ref_heg_shrine': ((-139.0, -9.0, 7.0), (-152.0, 0.0, 1.5), 28.0),
    'ref_heg_top': ((-176.0, -62.0, 62.0), (-139.0, 0.0, 2.0), 30.0),
    'ref_cov_top': ((182.0, -58.0, 60.0), (138.0, 0.0, 2.0), 30.0),
    'ref_null_top': ((0.0, -205.0, 62.0), (0.0, -134.0, 0.0), 30.0),
    'ref_arena': ((0.0, -150.0, 102.0), (0.0, -6.0, -7.5), 30.0),
    'bowl_side': ((95.0, -95.0, 26.0), (0.0, 0.0, -6.0), 24.0),
    'stair_east': ((60.0, -16.0, -3.0), (104.0, 0.0, -2.0), 22.0),
    'size_heg_gate': ((-137.0, 7.0, 1.7), (-119.0, 0.0, 3.6), 24.0),
    'size_heg_crates': ((-137.0, -10.5, 1.6), (-130.5, -17.0, 1.0), 26.0),
    'size_heg_shrine': ((-138.5, -7.0, 1.7), (-152.0, 0.5, 2.6), 22.0),
    'size_cov_shrine': ((138.5, 7.0, 1.7), (152.0, -0.5, 2.8), 22.0),
    'size_null_shrine': ((7.0, -138.5, 1.7), (-0.5, -152.0, 2.8), 22.0),
    'size_tier': ((63.5, 31.0, -5.9), (72.0, 42.5, -6.2), 24.0),
    'size_dais': ((0.0, -24.0, -5.6), (0.0, -3.0, -6.2), 26.0),
    'size_stair': ((68.0, -11.0, -5.8), (86.0, 2.0, -4.6), 22.0),
    '02_top': ((-139.0, 0.0, 95.0), (-139.0, 0.01, 0.0), 30.0),
}
pos, look, lens = SHOTS[shot]
sc = bpy.context.scene
cam = bpy.data.objects.new('Cam_' + shot, bpy.data.cameras.new('Cam_' + shot)); sc.collection.objects.link(cam)
cam.location = pos; cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
cam.data.lens = lens; cam.data.clip_start = 0.1; cam.data.clip_end = 1500; sc.camera = cam
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = W; sc.render.resolution_y = H; sc.render.resolution_percentage = 100
sc.render.threads_mode = 'FIXED'; sc.render.threads = 2
try:
    sc.eevee.taa_render_samples = 24; sc.eevee.use_raytracing = True; sc.eevee.use_shadows = True
except Exception as e: print('eevee opt', e)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
sc.view_settings.exposure = 0.0
# comic ink line (Freestyle)
sc.render.use_freestyle = True; sc.render.line_thickness = 1.6
vl = bpy.context.view_layer; vl.use_freestyle = True
fs = vl.freestyle_settings
ls = fs.linesets[0] if fs.linesets else fs.linesets.new('ink')
ls.select_silhouette = True; ls.select_border = False; ls.select_crease = True; ls.select_material_boundary = True
fs.crease_angle = math.radians(150)
if ls.linestyle is None: ls.linestyle = bpy.data.linestyles.new('ink')
ls.linestyle.color = (0.02, 0.015, 0.03); ls.linestyle.thickness = 1.7
noink = bpy.data.collections.new('NoInk'); sc.collection.children.link(noink)
for o in bpy.data.objects:          # 022b: colliders and blockers never render (linking them into NoInk would make them visible)
    if o.name.startswith(('UCX_', 'Blocker_', 'TopOrtho')): o.hide_render = True
for o in bpy.data.objects:
    if o.type == 'MESH' and (o.name.startswith(('Sky', 'Outer', 'Backdrop', 'Bowl_Floor')) or 'Rubble' in o.name or o.name.endswith(('_Floor', '_LandingPad'))):
        noink.objects.link(o)
fwd = (Vector(look) - Vector(pos)).normalized()
for o in bpy.data.objects:          # Freestyle draws stray lines for meshes that cross behind the camera
    if o.type == 'MESH' and o.name not in noink.objects:
        if min((o.matrix_world @ Vector(c) - Vector(pos)).dot(fwd) for c in o.bound_box) < 0.3: noink.objects.link(o)
ls.select_by_collection = True; ls.collection = noink; ls.collection_negation = 'EXCLUSIVE'
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print('RENDERED', out)
