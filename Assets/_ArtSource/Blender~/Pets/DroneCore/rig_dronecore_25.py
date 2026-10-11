"""Drone Core 25 %: the second form, in the flat-colour low-poly look, with a clean lens, rigged and animated.

Three steps, always in this order (the first two make the low-poly model again from the kept scan, the third
builds on that):

  run_blender.py --blend SM_Pet_DroneCore_25.blend pet_from_scan.py --object "DroneCore25%" --name SM_Pet_DroneCore_25 --tris 3600 --colours 8
  run_blender.py --blend SM_Pet_DroneCore_25.blend pet_finish.py --name SM_Pet_DroneCore_25 --objects SM_Pet_DroneCore_25 --size 0.34 --pivot centre --max-tris 4200 --swatch synty --assign "Colour 01=Synty 172,Colour 04=Synty 166"
  run_blender.py --blend SM_Pet_DroneCore_25.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/DroneCore/rig_dronecore_25.py

To export (only when asked), from a copy of the file:
  run_blender.py --blend COPY.blend pet_export.py --out-dir OUT --root SM_Pet_DroneCore_25 --keep-materials "Ring Glow,Mark,Mark Red,Flash"
  make_unitypackage.py --fbx OUT/SM_Pet_DroneCore_25.fbx --out-dir OUT --package-name "..." --swatch-page synty
      --material "SM_Pet_DroneCore_25 Ring Glow=#E7EFAD:glow" --material "SM_Pet_DroneCore_25 Mark=#F9F9F9:glow"
      --material "SM_Pet_DroneCore_25 Mark Red=#F32727:glow" --material "SM_Pet_DroneCore_25 Flash=#FFFFFF:glow"
The body goes on the shared Synty page material; the four parts above keep materials of their own.

The scan's lens is one piece (ring and glass together) and has no dot, so it is taken out and the lens is built
again from clean parts in the same place and size, each a closed solid:

  barrel   the ring round the glass: a dark bezel with a band on its face that glows (a material of its own,
           "Ring Glow", so the glow can be set in Unity). The band has two gaps, so a turn of the barrel can be seen.
  glass    the black dome in the ring
  dot      the white mark on the glass: the drone's eye
  red dot  a red copy of the dot under the glass, brought out when it is angry

Bones, the same as the first Drone Core's
  root, body, lens (squash it to squint), shutter (the barrel: turns about the lens's axis and slides along it),
  eye (the dot; turns about the centre of the glass's curve, so the dot slides over the glass), eye_red,
  and one more: flash (a white disc, tiny inside the glass until a photo is taken)

Actions, 24 frames a second: the first form's, without ShutterTurn, plus the new barrel move
  BarrelTurn    taking a photo: the barrel pushes out and makes a quarter turn, a white flash, the drone kicks back
                and the lens blinks like a shutter, then the barrel turns back and seats itself again
  EyeLook, AxisSpin, AxisSpinDiagonal, Mad, FrontRoll (loop)     as on the first form
"""
import math

import bmesh
import bpy
from mathutils import Euler, Quaternion, Vector

NAME = "SM_Pet_DroneCore_25"
AROUND = 24                 # sides of the round parts
BAND = (0.68, 0.80)         # the glowing band on the barrel's face, as shares of the barrel's radius
GAPS = (0, 1, 15)           # which of the band's AROUND pieces are dark (0 is at the top, counting to the drone's left)
GLOW = (0.80, 0.86, 0.42)   # the band's colour
PUSH = 0.016                # how far the barrel comes out in BarrelTurn, metres
FLASH_SMALL = 0.02          # the flash disc's size while it is hidden inside the glass
DOT = {"radius": 0.0085, "high": 0.0017, "at": (0.014, 0.012)}     # the dot: size, how proud, where it rests from the lens's axis (x, z)
RED_SMALLER = 0.8
RED_SUNK = 0.0030
EYE_MARGIN = 0.92


def islands_of(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, group = [f], []
        seen.add(f)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for e in cur.edges:
                for n in e.link_faces:
                    if n not in seen:
                        seen.add(n)
                        stack.append(n)
        out.append(group)
    return out


def main():
    scene = bpy.context.scene
    scene.render.fps = 24
    ob = bpy.data.objects[NAME]
    if ob.vertex_groups.get("shutter") is not None:
        raise SystemExit("This model is already rigged. Run pet_from_scan.py and pet_finish.py first: see the top of this file.")
    for o in list(bpy.data.objects):
        if o.type == "ARMATURE":
            bpy.data.objects.remove(o, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    me = ob.data

    def flat(name, colour, glow=0.0):
        mat = bpy.data.materials.get(f"{NAME} {name}")
        if mat is not None:
            bpy.data.materials.remove(mat)
        mat = bpy.data.materials.new(f"{NAME} {name}")
        mat.diffuse_color = (*colour, 1.0)
        mat.use_nodes = True
        shader = mat.node_tree.nodes["Principled BSDF"]
        shader.inputs["Base Color"].default_value = (*colour, 1.0)
        shader.inputs["Emission Color"].default_value = (*colour, 1.0)
        shader.inputs["Emission Strength"].default_value = glow
        me.materials.append(mat)
        return len(me.materials) - 1

    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()

    # ---- the scan's lens: the round piece in the middle of the front. Measured, then removed.
    front = min(v.co.y for v in bm.verts)
    size = max(v.co.x for v in bm.verts) - min(v.co.x for v in bm.verts)
    lens_old = None
    for group in islands_of(bm):
        verts = {v for f in group for v in f.verts}
        lo = Vector([min(v.co[i] for v in verts) for i in range(3)])
        hi = Vector([max(v.co[i] for v in verts) for i in range(3)])
        wide, tall = hi.x - lo.x, hi.z - lo.z
        if lo.y < front + 0.06 * size and 0.3 * size < wide < 0.6 * size and abs(wide - tall) < 0.05 * size and len(group) > 60:
            if lens_old is None or len(group) > len(lens_old[0]):
                lens_old = (group, lo, hi)
    group, lo, hi = lens_old
    axis_x, axis_z = (lo.x + hi.x) / 2, (lo.z + hi.z) / 2
    outer = (hi.x - lo.x + hi.z - lo.z) / 4               # the barrel's radius
    face_y, back_y = lo.y, hi.y                           # its face and how far back it reaches

    def colour_of(face):
        return ob.material_slots[face.material_index].material.diffuse_color[:3]

    def brightness(face):
        return sum(colour_of(face))

    # the glass is the dark dome in the middle: it ends where the ring's flat face begins
    def radius_of(co):
        return math.hypot(co.x - axis_x, co.z - axis_z)

    inner = min((radius_of(v.co) for v in {v for f in group for v in f.verts} if v.co.y < face_y + 0.0015 and radius_of(v.co) > 0.3 * outer),
                default=0.6 * outer)
    middle = [v.co.y for v in {v for f in group for v in f.verts} if radius_of(v.co) < 0.25 * inner]
    rim = [v.co.y for v in {v for f in group for v in f.verts} if 0.8 * inner < radius_of(v.co) < 0.99 * inner]
    apex_y = min(middle) if middle else face_y + 0.004
    rim_y = max(sum(rim) / len(rim) if rim else apex_y + 0.009, apex_y + 0.004)
    bulge = rim_y - apex_y
    curve = (inner ** 2 + bulge ** 2) / (2 * bulge)       # the radius of the glass's dome
    centre = Vector((axis_x, apex_y + curve, axis_z))

    # the palette colours the new parts reuse, each with the spot of the palette texture that holds it
    spots = {}
    for f in bm.faces:
        spots.setdefault(f.material_index, f.loops[0][uv].uv.copy())
    by_dark = sorted(spots, key=lambda i: sum(ob.material_slots[i].material.diffuse_color[:3]))
    glass_slot = by_dark[0]
    ring_faces = [f for f in group if radius_of(f.calc_center_median()) > inner and brightness(f) < 0.9]
    counts = {}
    for f in ring_faces:
        counts[f.material_index] = counts.get(f.material_index, 0) + f.calc_area()
    bezel_slot = max(counts, key=counts.get) if counts else by_dark[1]
    bmesh.ops.delete(bm, geom=group, context="FACES")
    # bits of the scan that stood inside the lens (in front of where the glass goes, inside the ring) go too
    stray = [f for f in bm.faces if radius_of(f.calc_center_median()) < outer * 0.97 and f.calc_center_median().y < rim_y + 0.006]
    bmesh.ops.delete(bm, geom=stray, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    glow_slot = flat("Ring Glow", GLOW, glow=2.0)
    mark_slot = flat("Mark", (0.95, 0.95, 0.95), glow=0.6)
    red_slot = flat("Mark Red", (0.9, 0.02, 0.02), glow=2.0)

    def paint(faces, slot):
        for f in faces:
            f.material_index = slot
            f.smooth = False
            if slot in spots:
                for l in f.loops:
                    l[uv].uv = spots[slot]

    def spoke(k):
        ang = 2 * math.pi * k / AROUND
        return Vector((math.sin(ang), 0.0, math.cos(ang)))              # 0 is straight up, going round to the drone's left

    # ---- the barrel: a ring turned from this outline (radius, depth), closed all round
    r1, r2 = outer * BAND[0], outer * BAND[1]
    lip = inner + 0.0012
    outline = [(lip, back_y), (lip, face_y), (r1, face_y), (r2, face_y), (outer * 0.92, face_y), (outer, face_y + (back_y - face_y) * 0.3), (outer, back_y)]
    rings = [[bm.verts.new(Vector((axis_x, y, axis_z)) + spoke(k) * r) for k in range(AROUND)] for r, y in outline]
    barrel_faces, band_faces = [], []
    for i in range(len(outline)):
        a_, b_ = rings[i], rings[(i + 1) % len(outline)]
        for k in range(AROUND):
            k2 = (k + 1) % AROUND
            f = bm.faces.new((a_[k], a_[k2], b_[k2], b_[k]))
            barrel_faces.append(f)
            if i == 2 and k not in GAPS:
                band_faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=barrel_faces)
    paint(barrel_faces, bezel_slot)
    paint(band_faces, glow_slot)
    barrel_verts = [v for ring in rings for v in ring]

    # ---- the glass: a dome on the same curve the scan's had, closed behind
    def on_dome(px, pz, lift=0.0):
        room = max((curve + lift) ** 2 - px * px - pz * pz, 1e-9)
        return Vector((axis_x + px, centre.y - math.sqrt(room), axis_z + pz))

    steps = 4
    dome = [[bm.verts.new(on_dome(*(spoke(k).xz * (inner * j / steps)))) for k in range(AROUND)] for j in range(1, steps + 1)]
    top = bm.verts.new(on_dome(0.0, 0.0))
    behind = bm.verts.new(Vector((axis_x, rim_y + 0.004, axis_z)))
    glass_faces = []
    for k in range(AROUND):
        k2 = (k + 1) % AROUND
        glass_faces.append(bm.faces.new((top, dome[0][k], dome[0][k2])))
        for a_, b_ in zip(dome[:-1], dome[1:]):
            glass_faces.append(bm.faces.new((a_[k], b_[k], b_[k2], a_[k2])))
        glass_faces.append(bm.faces.new((behind, dome[-1][k2], dome[-1][k])))
    bmesh.ops.recalc_face_normals(bm, faces=glass_faces)
    paint(glass_faces, glass_slot)
    for f in glass_faces:
        f.smooth = True
    glass_verts = [top, behind] + [v for ring in dome for v in ring]

    # ---- the dot, and its red copy under the glass
    dot_at = Vector(DOT["at"])
    dot_mid = on_dome(dot_at.x, dot_at.y)
    out_dir = (dot_mid - centre).normalized()
    side_a = out_dir.cross(Vector((0, 0, 1))).normalized()
    side_b = out_dir.cross(side_a).normalized()

    def make_dot(slot, smaller=1.0, sunk=0.0):
        made, verts = [], []
        tip = bm.verts.new(dot_mid + out_dir * (DOT["high"] * smaller - sunk))
        under = bm.verts.new(dot_mid - out_dir * (0.0015 + sunk))
        edge = []
        for k in range(12):
            ang = 2 * math.pi * k / 12
            flat_at = dot_mid + (side_a * math.cos(ang) + side_b * math.sin(ang)) * DOT["radius"] * smaller
            on = centre + (flat_at - centre).normalized() * (curve + 0.0002)        # on the glass, a hair proud of it
            edge.append(bm.verts.new(on - out_dir * sunk))
        for k in range(12):
            k2 = (k + 1) % 12
            made.append(bm.faces.new((tip, edge[k], edge[k2])))
            made.append(bm.faces.new((under, edge[k2], edge[k])))
        bmesh.ops.recalc_face_normals(bm, faces=made)
        paint(made, slot)
        return [tip, under] + edge

    flash_slot = flat("Flash", (1.0, 1.0, 1.0), glow=4.0)
    flash_home = Vector((axis_x, rim_y + 0.003, axis_z))                # inside the glass
    flash_rings = [[bm.verts.new(Vector((axis_x, y, axis_z)) + spoke(k) * (inner * 0.99)) for k in range(AROUND)]
                   for y in (apex_y - 0.0022, apex_y - 0.0010)]
    flash_mid = [bm.verts.new(Vector((axis_x, y, axis_z))) for y in (apex_y - 0.0022, apex_y - 0.0010)]
    flash_faces = []
    for k in range(AROUND):
        k2 = (k + 1) % AROUND
        flash_faces.append(bm.faces.new((flash_mid[0], flash_rings[0][k], flash_rings[0][k2])))
        flash_faces.append(bm.faces.new((flash_mid[1], flash_rings[1][k2], flash_rings[1][k])))
        flash_faces.append(bm.faces.new((flash_rings[0][k], flash_rings[1][k], flash_rings[1][k2], flash_rings[0][k2])))
    bmesh.ops.recalc_face_normals(bm, faces=flash_faces)
    paint(flash_faces, flash_slot)
    flash_verts = flash_mid + [v for ring in flash_rings for v in ring]
    for v in flash_verts:
        v.co = flash_home + (v.co - flash_home) * FLASH_SMALL

    dot_verts = make_dot(mark_slot)
    red_verts = make_dot(red_slot, RED_SMALLER, RED_SUNK)
    reach = (inner - DOT["radius"]) * EYE_MARGIN

    bm.verts.index_update()
    owner = {v.index: "body" for v in bm.verts}
    for verts, bone_name in ((barrel_verts, "shutter"), (glass_verts, "lens"), (dot_verts, "eye"), (red_verts, "eye_red"), (flash_verts, "flash")):
        for v in verts:
            owner[v.index] = bone_name
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    new_parts_open = sum(1 for v in barrel_verts + glass_verts + dot_verts + red_verts + flash_verts for e in v.link_edges if len(e.link_faces) != 2)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # ---- the rig
    arm = bpy.data.armatures.new(NAME + "_Rig")
    rig = bpy.data.objects.new(NAME + "_Rig", arm)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects:
        o.select_set(o is rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    root = eb.new("root")
    root.head, root.tail = (0, 0, 0), (0, 0.08, 0)
    body = eb.new("body")
    body.head, body.tail = (0, 0, 0), (0, 0, 0.08)
    body.parent = root
    lens = eb.new("lens")
    lens.head = (axis_x, back_y, axis_z)
    lens.tail = (axis_x, back_y, axis_z + 0.05)           # upright, so squashing it along its length is a squint
    lens.parent = body
    shutter = eb.new("shutter")
    shutter.head = (axis_x, back_y, axis_z)
    shutter.tail = (axis_x, face_y, axis_z)               # along the lens's axis: it turns about its length and slides along it
    shutter.parent = lens
    eye = eb.new("eye")
    eye.head = centre
    eye.tail = dot_mid
    eye.parent = lens
    eye_red = eb.new("eye_red")
    eye_red.head = dot_mid - out_dir * 0.004
    eye_red.tail = dot_mid + out_dir * 0.004
    eye_red.parent = eye
    flash = eb.new("flash")
    flash.head = flash_home
    flash.tail = flash_home + Vector((0, -0.01, 0))
    flash.parent = lens
    for b in eb:
        b.roll = 0.0
    bpy.ops.object.mode_set(mode="OBJECT")
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    for bone_name in ("body", "lens", "shutter", "eye", "eye_red", "flash"):
        ob.vertex_groups.new(name=bone_name).add([i for i, b in owner.items() if b == bone_name], 1.0, "REPLACE")
    ob.parent = rig
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- the animations
    pb = rig.pose.bones
    for b in pb:
        b.rotation_mode = "QUATERNION" if b.name == "eye" else "XYZ"

    def curves_of(action):
        return [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]

    def start(name):
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        return action

    def finish(action, last_frame, straight=False):
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, last_frame
        if straight:
            for fc in curves_of(action):
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
        rig.animation_data.action = None
        for b in pb:
            b.rotation_euler = (0, 0, 0)
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def turn(bone_name, frame, x=0.0, y=0.0, z=0.0):
        pb[bone_name].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
        pb[bone_name].keyframe_insert("rotation_euler", frame=frame)

    # EyeLook: positions on the glass, as shares of how far the dot may go (x to the drone's left, z up)
    rest_bone = arm.bones["eye"]
    to_bone = rest_bone.matrix_local.to_3x3().inverted()
    rest_at = Vector((dot_at.x / reach, dot_at.y / reach))
    aim_at_rest = (dot_mid - centre).normalized()
    last = [None]

    def look(frame, x, z):
        length = math.hypot(x, z)
        if length > 1.0:
            x, z = x / length, z / length
        px, pz = x * reach, z * reach
        aim = Vector((px, -math.sqrt(max(curve * curve - px * px - pz * pz, 1e-9)), pz)).normalized()
        q = aim_at_rest.rotation_difference(aim)
        local = Quaternion(to_bone @ Vector(q.axis), q.angle) if q.angle > 1e-6 else Quaternion()
        if last[0] is not None and last[0].dot(local) < 0:
            local.negate()
        last[0] = local.copy()
        pb["eye"].rotation_quaternion = local
        pb["eye"].keyframe_insert("rotation_quaternion", frame=frame)

    a = start("EyeLook")
    for frame, x, z in ((1, rest_at.x, rest_at.y), (10, 0, 0), (16, 0, 0), (24, -1, 0), (30, -1, 0), (42, 1, 0), (48, 1, 0),
                        (56, 0, 1), (60, 0, 1), (68, 0, -1), (72, 0, -1)):
        look(frame, x, z)
    for step in range(1, 25):                              # once round the edge of the glass
        ang = -math.pi / 2 + 2 * math.pi * step / 24
        look(72 + step * 2, math.cos(ang), math.sin(ang))
    look(132, rest_at.x, rest_at.y)
    look(138, rest_at.x, rest_at.y)
    finish(a, 138)

    # BarrelTurn: taking a photo. The barrel pushes out (the zoom) and the dot comes to the middle; the barrel makes
    # a quarter turn (the focus); then the shot: a white flash over the glass, the drone kicks back a little, and the
    # lens blinks shut and open like a shutter; the barrel turns back and seats itself, the dot goes back to rest.
    # The shutter bone lies along the lens's axis: its length is "out", a turn about its length is the barrel's turn.
    a = start("BarrelTurn")
    last[0] = None
    #            frame  out    degrees
    for frame, out, degrees in ((1, 0.0, 0), (4, -0.08, 0), (10, 1.12, 0), (13, 1.0, 0), (16, 1.0, -5), (26, 1.0, 94), (29, 1.0, 90),
                                (34, 1.0, 90), (36, 0.72, 90), (41, 1.0, 90), (46, 1.0, 90), (49, 1.0, 95), (59, 1.0, -4), (62, 1.0, 0),
                                (66, 1.0, 0), (72, -0.06, 0), (76, 0.0, 0), (80, 0.0, 0)):
        pb["shutter"].location = (0.0, PUSH * out, 0.0)
        pb["shutter"].keyframe_insert("location", frame=frame)
        turn("shutter", frame, y=degrees)
    for frame, x, z in ((1, rest_at.x, rest_at.y), (6, rest_at.x, rest_at.y), (14, 0, 0), (62, 0, 0), (72, rest_at.x, rest_at.y), (80, rest_at.x, rest_at.y)):
        look(frame, x, z)
    for frame, big in ((1, False), (33, False), (34, True), (35, True), (36, False), (80, False)):       # the flash: two frames
        pb["flash"].scale = ((1.0 / FLASH_SMALL) if big else 1.0,) * 3
        pb["flash"].keyframe_insert("scale", frame=frame)
    for frame, tall in ((1, 1.0), (35, 1.0), (37, 0.08), (38, 0.08), (41, 1.05), (44, 1.0), (80, 1.0)):   # the shutter: the lens blinks
        pb["lens"].scale = (1.0, tall, 1.0)
        pb["lens"].keyframe_insert("scale", frame=frame)
    for frame, back, tip in ((1, 0.0, 0.0), (33, 0.0, 0.0), (36, 0.010, -3.0), (42, -0.002, 0.6), (48, 0.0, 0.0), (80, 0.0, 0.0)):   # the kick
        pb["root"].location = (0.0, back, 0.0)
        pb["root"].keyframe_insert("location", frame=frame)
        turn("root", frame, x=tip)
    finish(a, 80)
    for fc in curves_of(a):                                # the flash is there or not: never half grown
        if "flash" in fc.data_path:
            for kp in fc.keyframe_points:
                kp.interpolation = "CONSTANT"

    # AxisSpin: one full turn about each axis in turn, each easing in and out
    a = start("AxisSpin")
    turn("root", 1)
    turn("root", 30, z=360)
    turn("root", 31)
    turn("root", 60, x=360)
    turn("root", 61)
    turn("root", 90, y=360)
    turn("root", 96, y=360)
    finish(a, 96)
    for fc in curves_of(a):                                # the jump from 360 back to 0 between turns must not blend
        for kp in fc.keyframe_points:
            if int(round(kp.co.x)) in (30, 60):
                kp.interpolation = "CONSTANT"

    # AxisSpinDiagonal: one full turn about each of three corner-to-corner lines of the cube
    a = start("AxisSpinDiagonal")
    frame = 1
    jumps = []
    for line in ((1, 1, 1), (-1, 1, 1), (1, -1, 1)):
        axis = Vector(line).normalized()
        before = Euler((0, 0, 0), "XYZ")
        for step in range(0, 31):
            t = step / 30.0
            eased = t * t * (3 - 2 * t)
            e = Quaternion(axis, 2 * math.pi * eased).to_euler("XYZ", before)
            before = e
            pb["root"].rotation_euler = e
            pb["root"].keyframe_insert("rotation_euler", frame=frame + step)
        jumps.append(frame + 30)
        frame += 31
    last_spin = frame + 4
    pb["root"].rotation_euler = (0, 0, 0)
    pb["root"].keyframe_insert("rotation_euler", frame=frame)
    pb["root"].keyframe_insert("rotation_euler", frame=last_spin)
    finish(a, last_spin)
    for fc in curves_of(a):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT" if int(round(kp.co.x)) in jumps else "LINEAR"

    # Mad: the dot snaps to the middle and goes red, the lens squints, the drone trembles, then it lets go
    a = start("Mad")
    red_out = RED_SUNK + DOT["high"] + 0.0012               # far enough forward to cover the white dot
    red_big = 1.25 / RED_SMALLER
    last[0] = None
    for frame, anger, squint in ((1, 0.0, 1.00), (5, 0.0, 1.06), (10, 1.0, 0.42), (14, 1.0, 0.56), (52, 1.0, 0.56),
                                 (58, 1.0, 0.50), (68, 0.0, 1.04), (76, 0.0, 1.00)):
        pb["lens"].scale = (1.0, squint, 1.0)               # the lens bone stands upright: its length is the lens's height
        pb["lens"].keyframe_insert("scale", frame=frame)
        pb["eye_red"].location = (0.0, red_out * anger, 0.0)
        pb["eye_red"].scale = (1 + (red_big - 1) * anger,) * 3
        pb["eye_red"].keyframe_insert("location", frame=frame)
        pb["eye_red"].keyframe_insert("scale", frame=frame)
        look(frame, rest_at.x * (1 - anger), rest_at.y * (1 - anger))
    turn("root", 1)
    turn("root", 12)
    for i, frame in enumerate(range(14, 53, 2)):            # trembling with it
        shake = (1.0 if i % 2 else -1.0) * (2.2 if 2 < i < 17 else 1.0)
        turn("root", frame, z=shake, y=shake * 0.6)
    turn("root", 56)
    turn("root", 76)
    finish(a, 76)

    # FrontRoll: the movement loop. The drone rolls forward, top first, one full turn a second.
    a = start("FrontRoll")
    turn("root", 1)
    turn("root", 25, x=360)
    finish(a, 25, straight=True)

    scene.frame_set(1)
    print("DRONECORE25 size %s; %d triangles; lens: barrel radius %.1f mm, glass radius %.1f mm (dome radius %.1f mm), dot radius %.1f mm, "
          "may travel %.1f mm from the axis; open edges on the new lens parts: %d (whole model, as scanned: %d); actions %s"
          % (tuple(round(d, 3) for d in ob.dimensions), tris, outer * 1000, inner * 1000, curve * 1000, DOT["radius"] * 1000, reach * 1000,
             new_parts_open, open_edges, sorted(x.name for x in bpy.data.actions)))


main()
