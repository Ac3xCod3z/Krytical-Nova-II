"""T-pose views of one bare mannequin for the Tripo front picture (step 1 of references/tripo-gear-set-playbook.md).

  blender -b SyntyMannequinModeling.blend --python mannequin_views.py -- "<Lineage Sex collection>" <out folder>

Bare _BASE_ parts only (no hair, no labels), Workbench grey on white, orthographic, 1024 px: <Lineage><Sex>_Front/Left/Back/Right.png.
"""
import bpy, sys, os
from mathutils import Vector
lin, out = sys.argv[sys.argv.index("--")+1:][:2]
col = bpy.data.collections[lin]
import re
# the bare body by its slots (head, trunk, arms, hands, hips, legs, feet): a robot's chassis is SK_SCFI_ROBO_04_*,
# not a _BASE_ part
BODY = re.compile(r"_(01HEAD|1[0-9][A-Z]{4}|2[01][A-Z]{4})_")
keep = [o for o in col.all_objects if o.type=="MESH" and BODY.search(o.name) and "HAIR" not in o.name]
print("KEEP", len(keep), [o.name for o in keep][:40])
for o in bpy.data.objects:
    if o.type in ("MESH","FONT","CURVE"): o.hide_render = o not in keep
for o in keep:
    if o.data.shape_keys:
        for kb in o.data.shape_keys.key_blocks[1:]: kb.value = 0.0
sc=bpy.context.scene; sc.render.engine="BLENDER_WORKBENCH"
sh=sc.display.shading; sh.light="STUDIO"; sh.color_type="SINGLE"; sh.single_color=(0.6,0.6,0.6); sh.show_shadows=False; sh.show_cavity=False
sc.render.film_transparent=False
w=bpy.data.worlds.get("kn_white") or bpy.data.worlds.new("kn_white"); sc.world=w; w.color=(1,1,1)
sc.display.shading.background_type="WORLD" if hasattr(sc.display.shading,"background_type") else None
sc.view_settings.view_transform="Standard"
sc.render.resolution_x=sc.render.resolution_y=1024
pts=[o.matrix_world@Vector(c) for o in keep for c in o.bound_box]
lo=Vector((min(p.x for p in pts),min(p.y for p in pts),min(p.z for p in pts))); hi=Vector((max(p.x for p in pts),max(p.y for p in pts),max(p.z for p in pts)))
mid=(lo+hi)/2; size=max(hi.x-lo.x, hi.z-lo.z)*1.1
cd=bpy.data.cameras.new("kn_c"); cd.type="ORTHO"; cd.ortho_scale=size; cam=bpy.data.objects.new("kn_c",cd); sc.collection.objects.link(cam); sc.camera=cam
for nm,d in (("Front",Vector((0,-1,0))),("Left",Vector((1,0,0))),("Back",Vector((0,1,0))),("Right",Vector((-1,0,0)))):
    cam.location=mid+d*10; cam.rotation_euler=(-d).to_track_quat("-Z","Y").to_euler()
    sc.render.filepath=os.path.join(out, f"{lin.replace(' ','')}_{nm}.png"); bpy.ops.render.render(write_still=True)
print("DONE")
