"""Drone Core: the Tripo model as it came (its own texture, no low-poly conversion), sized, rigged and animated.

  run_blender.py --blend SM_Pet_DroneCore_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/DroneCore/rig_dronecore.py

The model arrives in separate pieces, which is what makes this rig possible:
  the cube frame, the camera body, the lens barrel, the black lens glass and the white dot on the glass.

Bones (all rigid: every piece follows exactly one bone)
  root      the whole drone; turn it to tumble the drone
  body      everything that is not named below
  lens      the barrel, the glass and the dot together; squash it to make the lens squint
  shutter   the lens barrel; it turns about the lens's own axis
  eye       the white dot; the bone starts at the centre of the lens's curve, so turning it slides the
            dot over the glass without lifting off it
  eye_red   a red copy of the dot, kept just under the glass behind the white one; pushed forward and
            grown, it covers the white dot, which is how the mark "turns red" in a way a game engine can play

Actions, 24 frames a second
  ShutterTurn   the barrel makes a quarter turn one way, then three quarters of a turn the other way
  EyeLook       the dot goes to the middle, looks right, left, up and down, runs once round the edge
                of the glass and returns to where it rests
  AxisSpin      the whole drone turns once about each axis in turn: upright, side to side, front to back
  AxisSpinDiagonal   the same, about the cube's corner-to-corner lines instead
  FrontRoll     a loop for moving: the drone rolls forward, top first, one turn a second
  Mad           the dot snaps to the middle and turns red, the lens squints, the drone trembles, then it calms down

The only change to the model's look: the lens glass is one flat black, because the scan had the white dot
painted onto the glass as well and that copy would stay behind when the real dot moves.

Safe to run again: the rig and the actions are rebuilt; the mesh is only sized once.
"""
import math

import bmesh
import bpy
from mathutils import Quaternion, Vector

NAME = "SM_Pet_DroneCore_S1"
SOURCE = "camera 3d model"
TARGET_TRIS = 6500          # asked of the thinning step; with the lens kept whole it lands near 5,900. The full Tripo model has about 12,500
SIZE = 0.30                 # longest side, metres: three quarters of the 0.4 m pets
RED_SMALLER = 0.8           # the red dot's size at rest, as a share of the white dot's
RED_SUNK = 0.0030           # how far under the white dot it waits, metres
EYE_MARGIN = 0.92           # how close to the edge of the glass the dot may go (1 = touching it)


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


def box(verts):
    lo = Vector([min(v.co[i] for v in verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in verts) for i in range(3)])
    return lo, hi


def main():
    scene = bpy.context.scene
    scene.render.fps = 24

    # ---- the model: the raw one, under the pet's name, sized, origin at its middle
    ob = bpy.data.objects.get(NAME)
    raw = bpy.data.objects.get(SOURCE + "_source") or bpy.data.objects.get(SOURCE)
    if raw is not None:                                   # first run: drop the low-poly copy, keep the original
        if ob is not None and ob is not raw:
            old = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if old.users == 0:
                bpy.data.meshes.remove(old)
        ob = raw
        ob.name = NAME
        ob.data.name = NAME
    for o in bpy.data.objects:
        if o.type == "ARMATURE" and o.name == NAME + "_Rig":
            bpy.data.objects.remove(o, do_unlink=True)
    ob.hide_set(False)
    ob.hide_viewport = False
    ob.hide_render = False
    ob.parent = None
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    me = ob.data
    earlier = ob.vertex_groups.get("eye_red")
    if earlier is not None:                                 # the red dot from an earlier run: it is made again below
        gone = [v.index for v in me.vertices if any(e.group == earlier.index for e in v.groups)]
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in gone], context="VERTS")
        bm.to_mesh(me)
        bm.free()
        for g in list(ob.vertex_groups):
            ob.vertex_groups.remove(g)
    world = ob.matrix_world.copy()
    pts = [world @ v.co for v in me.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    k = SIZE / max(hi - lo)
    mid = (lo + hi) / 2
    for v, p in zip(me.vertices, pts):
        v.co = (p - mid) * k
    ob.matrix_world.identity()
    me.update()

    # ---- fewer triangles, same shapes. The full model is kept in the file as "<name>_source" (hidden) and
    #      every run starts from it, so TARGET_TRIS can be changed and the script run again. The lens (barrel,
    #      glass, dot) is left exactly as it is: it is small, round and looked at closely. The frame and the
    #      camera body, which hold nearly all the triangles, are thinned; texture seams are kept.
    full = bpy.data.objects.get(NAME + "_source")
    if full is None:
        full = ob.copy()
        full.data = me.copy()
        full.name = NAME + "_source"
        full.data.name = NAME + "_source"
        scene.collection.objects.link(full)
        for g in list(full.vertex_groups):
            full.vertex_groups.remove(g)
    else:
        old = ob.data
        ob.data = full.data.copy()
        ob.data.name = NAME
        if old.users == 0:
            bpy.data.meshes.remove(old)
        me = ob.data
        for g in list(ob.vertex_groups):
            ob.vertex_groups.remove(g)
    full.hide_set(True)
    full.hide_render = True
    full.parent = None
    tris_before = sum(len(poly.vertices) - 2 for poly in me.polygons)
    if tris_before > TARGET_TRIS * 1.05:
        front_y = min(v.co.y for v in me.vertices)
        lens_zone = [v.index for v in me.vertices
                     if v.co.y < front_y + 0.24 * SIZE and math.hypot(v.co.x, v.co.z) < 0.2 * SIZE]
        zone_tris = sum(len(poly.vertices) - 2 for poly in me.polygons if all(i in set(lens_zone) for i in poly.vertices))
        rest_group = ob.vertex_groups.new(name="kn_thin")
        zone = set(lens_zone)
        rest_group.add([v.index for v in me.vertices if v.index not in zone], 1.0, "REPLACE")
        mod = ob.modifiers.new("Thin", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = max(0.05, (TARGET_TRIS - zone_tris) / max(tris_before - zone_tris, 1))
        mod.vertex_group = "kn_thin"                         # only what is in this group is thinned
        mod.vertex_group_factor = 1000.0
        mod.invert_vertex_group = False
        mod.delimit = {"UV"}
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = ob
        for o in bpy.context.view_layer.objects:
            o.select_set(o is ob)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        ob.vertex_groups.remove(ob.vertex_groups["kn_thin"])
        me = ob.data
    tris_after = sum(len(poly.vertices) - 2 for poly in me.polygons)

    # ---- which piece is which
    bm = bmesh.new()
    bm.from_mesh(me)
    pieces = []
    for group in islands_of(bm):
        verts = list({v for f in group for v in f.verts})
        plo, phi = box(verts)
        pieces.append({"verts": [v.index for v in verts], "lo": plo, "hi": phi, "mid": (plo + phi) / 2, "size": phi - plo})
    bm.free()
    front = min(p["lo"].y for p in pieces)

    def near_front(p, depth):
        return p["lo"].y < front + depth * SIZE

    # the barrel: a round piece at the front, a third of the drone wide, sticking well out of the body
    barrel = max((p for p in pieces if near_front(p, 0.08) and 0.25 * SIZE < p["size"].x < 0.45 * SIZE
                  and abs(p["size"].x - p["size"].z) < 0.06 * SIZE and p["size"].y > 0.1 * SIZE),
                 key=lambda p: len(p["verts"]))
    axis_x, axis_z = barrel["mid"].x, barrel["mid"].z

    def on_axis(p, slack):
        return math.hypot(p["mid"].x - axis_x, p["mid"].z - axis_z) < slack * SIZE

    # the glass: the round, shallow piece in the barrel's mouth; the dot: the small piece sitting on the glass
    glass = max((p for p in pieces if p is not barrel and near_front(p, 0.1) and on_axis(p, 0.03)
                 and p["size"].y < 0.08 * SIZE and p["size"].x < barrel["size"].x),
                key=lambda p: p["size"].x)
    dot = min((p for p in pieces if p is not barrel and p is not glass and near_front(p, 0.1)
               and glass["lo"].x <= p["mid"].x <= glass["hi"].x and glass["lo"].z <= p["mid"].z <= glass["hi"].z
               and p["size"].x < 0.5 * glass["size"].x),
              key=lambda p: p["lo"].y)

    # the glass is a shallow dome: find the centre of its curve, so the dot can ride on it
    glass_radius = (glass["size"].x + glass["size"].z) / 4
    bulge = max(glass["size"].y, 1e-4)
    curve = (glass_radius ** 2 + bulge ** 2) / (2 * bulge)
    centre = Vector((axis_x, glass["lo"].y + curve, axis_z))
    dot_radius = (dot["size"].x + dot["size"].z) / 4
    reach = (glass_radius - dot_radius) * EYE_MARGIN          # how far from the lens's axis the dot's middle may go

    # ---- the glass has the dot painted on it as well, which would stay behind when the real dot moves:
    #      the whole glass takes the colour of its own darkest spot (its UVs all go to that one place)
    uv_layer = me.uv_layers.active
    image = next((n.image for m in me.materials if m and m.node_tree for n in m.node_tree.nodes
                  if n.type == "TEX_IMAGE" and n.image), None)
    if uv_layer is not None and image is not None and image.size[0] > 0:
        import numpy as np
        w, h = image.size
        px = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
        glass_verts = set(glass["verts"])
        loops = [l for poly in me.polygons if all(v in glass_verts for v in poly.vertices) for l in poly.loop_indices]

        def dark(index):
            u, v = uv_layer.data[index].uv
            return float(px[int(v % 1.0 * (h - 1)), int(u % 1.0 * (w - 1)), :3].sum())

        if loops:
            spot = uv_layer.data[min(loops, key=dark)].uv.copy()
            for index in loops:
                uv_layer.data[index].uv = spot

    # ---- a red copy of the dot, a little smaller and sunk under the glass, so it is out of sight at rest
    red = bpy.data.materials.get(NAME + " Mark Red") or bpy.data.materials.new(NAME + " Mark Red")
    red.diffuse_color = (0.9, 0.02, 0.02, 1.0)
    red.use_nodes = True
    shader = next((n for n in red.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if shader:
        shader.inputs["Base Color"].default_value = (0.9, 0.02, 0.02, 1.0)
        shader.inputs["Emission Color"].default_value = (1.0, 0.05, 0.03, 1.0)
        shader.inputs["Emission Strength"].default_value = 2.0
    if red.name not in [m.name for m in me.materials if m]:
        me.materials.append(red)
    red_slot = [m.name if m else None for m in me.materials].index(red.name)
    dot_mid = Vector((dot["mid"].x, dot["mid"].y, dot["mid"].z))
    inward = (centre - dot_mid).normalized()
    dot_verts = set(dot["verts"])
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    copy = bmesh.ops.duplicate(bm, geom=[f for f in bm.faces if all(v.index in dot_verts for v in f.verts)])
    red_new = [e for e in copy["geom"] if isinstance(e, bmesh.types.BMVert)]
    for f in (e for e in copy["geom"] if isinstance(e, bmesh.types.BMFace)):
        f.material_index = red_slot
    for v in red_new:
        v.co = dot_mid + (v.co - dot_mid) * RED_SMALLER + inward * RED_SUNK
    bm.verts.index_update()
    red_indices = [v.index for v in red_new]
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
    lens.head = (axis_x, barrel["hi"].y, axis_z)
    lens.tail = (axis_x, barrel["hi"].y, axis_z + 0.05)      # upright, so squashing it along its length is a squint
    lens.parent = body
    shutter = eb.new("shutter")
    shutter.head = (axis_x, barrel["hi"].y, axis_z)
    shutter.tail = (axis_x, barrel["lo"].y, axis_z)          # along the lens's axis: turning about its length turns the barrel
    shutter.parent = lens
    eye = eb.new("eye")
    eye.head = centre
    eye.tail = Vector((dot["mid"].x, dot["lo"].y, dot["mid"].z))
    eye.parent = lens
    eye_red = eb.new("eye_red")
    eye_line = (eye.tail - eye.head).normalized()
    eye_red.head = eye.tail - eye_line * 0.004
    eye_red.tail = eye.tail + eye_line * 0.004            # along the eye's line: sliding it brings the red dot out
    eye_red.parent = eye
    for b in eb:
        b.roll = 0.0
    bpy.ops.object.mode_set(mode="OBJECT")

    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    owner = {}
    for p in pieces:
        bone = "shutter" if p is barrel else "eye" if p is dot else "lens" if p is glass else "body"
        for i in p["verts"]:
            owner[i] = bone
    for i in red_indices:
        owner[i] = "eye_red"
    for bone in ("body", "lens", "shutter", "eye", "eye_red"):
        ob.vertex_groups.new(name=bone).add([i for i, b in owner.items() if b == bone], 1.0, "REPLACE")
    ob.parent = rig
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- the animations
    pb = rig.pose.bones
    for b in pb:
        b.rotation_mode = "QUATERNION" if b.name == "eye" else "XYZ"

    def start(name):
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        return action

    def finish(action, last, straight=False):
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, last
        curves = getattr(action, "fcurves", None) or [fc for layer in action.layers for strip in layer.strips
                                                      for bag in strip.channelbags for fc in bag.fcurves]
        if straight:
            for fc in curves:
                for key in fc.keyframe_points:
                    key.interpolation = "LINEAR"
        rig.animation_data.action = None
        for b in pb:
            b.rotation_euler = (0, 0, 0)
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def turn(bone, frame, x=0.0, y=0.0, z=0.0):
        pb[bone].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
        pb[bone].keyframe_insert("rotation_euler", frame=frame)

    # ShutterTurn
    a = start("ShutterTurn")
    for frame, degrees in ((1, 0), (4, -6), (14, 90), (20, 90), (23, 96), (50, -180), (56, -180)):
        turn("shutter", frame, y=degrees)                  # a quarter turn, a beat, then three quarters back the other way
    finish(a, 56)

    # EyeLook: positions on the glass, as shares of how far the dot may go (x to the drone's left, z up)
    rest_bone = arm.bones["eye"]
    rest_dir = (rest_bone.tail_local - rest_bone.head_local).normalized()
    to_bone = rest_bone.matrix_local.to_3x3().inverted()
    rest_at = Vector(((rest_bone.tail_local.x - axis_x) / reach, (rest_bone.tail_local.z - axis_z) / reach))

    rest_px, rest_pz = rest_at.x * reach, rest_at.y * reach
    aim_at_rest = Vector((rest_px, -math.sqrt(max(curve * curve - rest_px ** 2 - rest_pz ** 2, 1e-9)), rest_pz)).normalized()
    last = [None]

    def look(frame, x, z):
        length = math.hypot(x, z)
        if length > 1.0:
            x, z = x / length, z / length
        px, pz = x * reach, z * reach
        aim = Vector((px, -math.sqrt(max(curve * curve - px * px - pz * pz, 1e-9)), pz)).normalized()
        q = aim_at_rest.rotation_difference(aim)           # from where it rests; in the rig's space ...
        local = Quaternion(to_bone @ Vector(q.axis), q.angle) if q.angle > 1e-6 else Quaternion()   # ... in the bone's
        if last[0] is not None and last[0].dot(local) < 0:
            local.negate()
        last[0] = local.copy()
        pb["eye"].rotation_quaternion = local
        pb["eye"].keyframe_insert("rotation_quaternion", frame=frame)

    a = start("EyeLook")
    beats = [(1, rest_at.x, rest_at.y), (10, 0, 0), (16, 0, 0), (24, -1, 0), (30, -1, 0), (42, 1, 0), (48, 1, 0),
             (56, 0, 1), (60, 0, 1), (68, 0, -1), (72, 0, -1)]
    for frame, x, z in beats:
        look(frame, x, z)
    for step in range(1, 25):                              # once round the edge of the glass
        ang = -math.pi / 2 + 2 * math.pi * step / 24
        look(72 + step * 2, math.cos(ang), math.sin(ang))
    look(132, rest_at.x, rest_at.y)
    look(138, rest_at.x, rest_at.y)
    finish(a, 138)

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
    curves = getattr(a, "fcurves", None) or [fc for layer in a.layers for strip in layer.strips
                                             for bag in strip.channelbags for fc in bag.fcurves]
    for fc in curves:                                      # the jump from 360 back to 0 between turns must not blend
        for key in fc.keyframe_points:
            if int(round(key.co.x)) in (30, 60):
                key.interpolation = "CONSTANT"

    # AxisSpinDiagonal: one full turn about each of three corner-to-corner lines of the cube
    from mathutils import Euler
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
    curves = getattr(a, "fcurves", None) or [fc for layer in a.layers for strip in layer.strips
                                             for bag in strip.channelbags for fc in bag.fcurves]
    for fc in curves:                                      # every frame is keyed: straight lines, and no blending
        for key in fc.keyframe_points:                     # across the jump back to zero between turns
            key.interpolation = "CONSTANT" if int(round(key.co.x)) in jumps else "LINEAR"

    # Mad: the dot snaps to the middle and goes red, the lens squints, the drone trembles, then it lets go
    a = start("Mad")
    red_out = RED_SUNK + 0.0045                             # far enough forward to cover the white dot, which is a deep bump
    red_big = 1.25 / RED_SMALLER
    #         frame  anger  squint (lens height)
    mood = [(1, 0.0, 1.00), (5, 0.0, 1.06), (10, 1.0, 0.42), (14, 1.0, 0.56), (52, 1.0, 0.56),
            (58, 1.0, 0.50), (68, 0.0, 1.04), (76, 0.0, 1.00)]
    last[0] = None
    for frame, anger, squint in mood:
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

    # FrontRoll: the movement loop. The drone rolls forward, top first, one full turn a second, at a steady
    # speed so the loop has no seam (frame 25 is frame 1 a whole turn later).
    a = start("FrontRoll")
    turn("root", 1)
    turn("root", 25, x=360)                                # forward is -Y: turning this way tips the top forward
    finish(a, 25, straight=True)

    scene.frame_set(1)
    print("DRONECORE triangles: %d before thinning, %d now (kept in the file: %s)" % (tris_before, sum(len(poly.vertices) - 2 for poly in me.polygons), full.name))
    print("DRONECORE rigged: size %s, pieces %d | barrel %d verts, glass radius %.1f mm, dot %d verts radius %.1f mm, "
          "dot may travel %.1f mm from the lens axis, rests %.1f mm from it | actions ShutterTurn 56, EyeLook 138, AxisSpin 96, AxisSpinDiagonal %d, Mad 76, FrontRoll 24 (loop)"
          % (tuple(round(d, 3) for d in ob.dimensions), len(pieces), len(barrel["verts"]), glass_radius * 1000,
             len(dot["verts"]), dot_radius * 1000, reach * 1000, math.hypot(rest_at.x, rest_at.y) * reach * 1000, last_spin))


main()
