"""Drone Core, the later forms (50 %, 75 %, 100 %): a clean lens, closed meshes, a rig with the cannon, and the clips.

One script for all three; which one it is building is read from the object in the file (SM_Pet_DroneCore_50 ..).
It runs on the low-poly model that pet_from_scan.py and pet_finish.py leave (50 and 75), or on the painted model
that paint_dronecore_100.py leaves (100). It cannot be run twice on the same model: make the model again first.

  lens     the scan's lens is taken out and built again from clean, closed parts, the same as on the 25 % form:
           barrel (bone shutter), glass (bone lens), a white dot (bone eye), a red dot under it (eye_red), a flash.
           The barrel's band, the dots and the flash have glowing materials of their own.
  cannon   the gun barrel under the lens is cut free of the body, closed, and given a bone: cannon
  pods     75 % only: the two side pods, cut free and closed: pod.L, pod.R
  closed   every open edge of the model is closed
  size     the cube body is made the stage's size (the 25 % form is 0.34 m): 0.38, 0.42, 0.46 m

Clips, 24 frames a second: BarrelTurn (taking a photo), EyeLook, AxisSpin, AxisSpinDiagonal, Mad, FrontRoll (loop),
as on the 25 % form, and CannonFire (two shots with recoil; on the 75 % the pods kick too).

The 50 % and 75 % forms have MinigunFire 80: the cannon spins up (frames 1 to 12), fires fast while it spins, and
winds down (61 to 80). A shot every 2 frames from 14 to 60, 24 shots at 12 a second, each from the tip of the cannon
bone; the recoil is slight (the barrel ticks back, the drone shivers). The shots and the sound are Unity's.
This one clip is made from the rig alone, so the script can add it to a model that is already rigged: run it again.

Colours of the 75 % form, set by the artist (11 Oct 2026) and applied on every run, rigged or not (STAGES "colours"):
its darkest green, nearly brown, is the same olive as the rest of the chassis; the two upright bars low on the front,
either side of the cannon, are the light grey of the metal frame.

The 100 % form has five more. Only the acting is in them: the speech bubble, the beam and the shots are effects for
Unity to add, on these CUES (frame, then seconds from the start of the clip):

  FakeoutBlast 130    bubble "Ka" 1 (0.00)   "Me" 13 (0.50)   "Ha!" 27 (1.08, the thrust lands on 31)
                      "...just kidding" 45 (1.83)   "Die!" 65 (2.67)
                      beam ON 72 (2.96) to OFF 110 (4.54): from the tip of the cannon bone, along the bone
  SpinShots 91        barrel spinning from frame 1; a shot on 26, 32, 38, 44, 50, 56, 62, 68
                      (1.04, 1.29, 1.54, 1.79, 2.04, 2.29, 2.54, 2.79 s), each from the tip of the cannon bone
  Disassemble 44      the body's 18 blocks, the lens and the cannon fly apart (burst from frame 10)
  SwarmHover 49       loop while apart: starts on Disassemble's last frame
  Assemble 46         they fly home and lock (the lock is on frame 39): starts on SwarmHover's first frame

The 100 % form's body is never cut. Its own mesh, whole, is the hull (bone hull). The 18 blocks of the swarm are a
second, simple body made for the purpose (a closed skin wrapped round the hull, sliced into blocks with flat ends),
each block shrunk to a speck at its own middle and so out of sight inside the hull. The three swarm clips swap them: the
hull's bone is scaled to nothing and the blocks' bones to full size, on one frame (Disassemble 10, Assemble 36).
"""
import math

import bmesh
import bpy
from mathutils import Euler, Quaternion, Vector
from mathutils.bvhtree import BVHTree

# All measures below are in the scan's own units, as it came; the script scales them with the model.
STAGES = {
    "50": {"size": 0.38, "width": 0.46, "centre": (0.0, 0.11, 0.325),
           "lens": {"x": 0.0, "z": 0.335, "outer": 0.088, "inner": 0.060, "face": -0.165, "back": -0.05},
           "glass": ((0.004, 0.005, 0.007), 0.0), "band": True, "cannon_cut": -0.185, "pods": {}},
    "75": {"size": 0.42, "width": 0.48, "centre": (-0.03, 0.035, 0.37),
           "lens": {"x": -0.025, "z": 0.38, "outer": 0.095, "inner": 0.055, "face": -0.245, "back": -0.19},
           "glass": ((0.004, 0.005, 0.007), 0.0), "band": True, "cannon_cut": -0.215,
           "pods": {"pod.L": (-0.29, 0.225, 0.095, -0.19, 0.18), "pod.R": (0.265, 0.225, 0.10, -0.19, 0.18)},    # axis x, z; radius; from y to y
           "colours": {"same": [("Colour 01", "Colour 02")],            # the first takes the second's colour
                       # faces of the first colour inside the box become the second: |x| from..to, y below, z from..to (finished model's measures)
                       "boxes": [("Colour 02", "Colour 04", (0.11, 0.205), -0.165, (-0.23, -0.04))]}},
    "100": {"size": 0.46, "width": 0.60, "centre": (0.0, 0.125, 0.38),
            "lens": {"x": 0.0, "z": 0.395, "outer": 0.108, "inner": 0.072, "face": -0.235, "back": -0.14},
            "glass": ((0.10, 0.85, 0.95), 2.5), "band": False, "cannon_cut": -0.215, "pods": {}, "skin": False,
            "shards": True},          # a second body in blocks is made for the swarm clips (see the top of this file)
}
MINIGUN = ("50", "75")       # the forms that have MinigunFire
AROUND = 24
VOXEL = 0.0055             # the skin's voxel, as a share of the cube's width
TRIS = 5200                # triangles the skin is thinned to
BAND = (0.74, 0.88)
GAPS = (0, 1, 15)
GLOW = (0.80, 0.86, 0.42)
PUSH = 0.04                 # how far the barrel comes out in BarrelTurn, as a share of the lens's radius ... times 10
FLASH_SMALL = 0.02
DOT = {"radius": 0.14, "high": 0.028, "at": (0.24, 0.20)}       # shares of the glass's radius
RED_SMALLER = 0.8
EYE_MARGIN = 0.92
SHARD_VOXEL = 0.009         # the swarm blocks' skin: voxel as a share of the cube's size, and the triangles it is thinned to
SHARD_TRIS = 2400
SHARD_SMALL = 0.02          # the blocks rest this small, each a speck deep inside the hull; the swarm clips scale them back
SINK = 0.30                 # 100 %: how far the barrel's back reaches into the body, as a share of the lens's radius


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


def set_colours(ob, rules):
    """The artist's colour corrections for a form (see the top of this file). Safe to run again.
    A face's colour is two things that must agree: its material (what Blender shows) and its place on the swatch
    page (its UVs: what the exported model shows, on the one shared swatch material)."""
    me = ob.data
    uv = me.uv_layers.active.data
    slot = {m.name.split(" ", 1)[1]: i for i, m in enumerate(me.materials) if m and " " in m.name}

    def inside(poly, box):
        (x0, x1), y_below, (z0, z1) = box
        c = poly.center
        return x0 < abs(c.x) < x1 and c.y < y_below and z0 < c.z < z1

    boxes = [(slot[name], slot[to], box) for name, to, *box in rules.get("boxes", [])]

    def spot_of(index):
        """Where this colour sits on the swatch page: the place most of its faces point at (boxed faces left out)."""
        tally = {}
        for poly in me.polygons:
            if poly.material_index == index and not any(inside(poly, box) for _f, _t, box in boxes):
                at = tuple(round(c, 5) for c in uv[poly.loop_start].uv)
                tally[at] = tally.get(at, 0) + 1
        return max(tally, key=tally.get)

    for name, like in rules.get("same", []):
        mat, src = me.materials[slot[name]], me.materials[slot[like]]
        mat.diffuse_color = src.diffuse_color
        if mat.use_nodes and src.use_nodes:
            mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = src.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
        at = spot_of(slot[like])
        for poly in me.polygons:
            if poly.material_index == slot[name]:
                for li in poly.loop_indices:
                    uv[li].uv = at
    changed = 0
    for from_, to, box in boxes:
        at = spot_of(to)
        for poly in me.polygons:
            if poly.material_index in (from_, to) and inside(poly, box):
                poly.material_index = to
                for li in poly.loop_indices:
                    uv[li].uv = at
                changed += 1
    me.update()
    return changed


def minigun_fire(rig, size):
    """MinigunFire, from the rig alone (see the top of this file)."""
    old = bpy.data.actions.get("MinigunFire")
    if old is not None:
        bpy.data.actions.remove(old)
    pb = rig.pose.bones
    gun_len = rig.data.bones["cannon"].length
    pods = [b.name for b in pb if b.name.startswith("pod.")]
    for name in ["root", "cannon"] + pods:
        pb[name].rotation_mode = "XYZ"
    action = bpy.data.actions.new("MinigunFire")
    action.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = action
    last, first_shot, last_shot = 80, 14, 60
    angle, spin = 0.0, []
    for frame in range(1, last + 1):
        speed = min(1.0, (frame - 1) / 12.0) if frame <= last_shot else max(0.0, (last - frame) / (last - last_shot))
        spin.append(angle)
        angle += 50.0 * speed * (2 - speed) if frame <= last_shot else 50.0 * speed * speed
    whole = round(spin[-1] / 360.0) * 360.0                       # it stops where it started
    shots = set(range(first_shot, last_shot + 1, 2))
    for frame in range(1, last + 1):
        fired = frame in shots
        brace = min(1.0, max(0.0, (frame - 6) / 6.0)) * min(1.0, max(0.0, (last_shot + 8 - frame) / 6.0))   # leaning into it while it fires
        shiver = (1.0 if (frame // 2) % 2 else -1.0) if first_shot <= frame <= last_shot else 0.0
        pb["cannon"].rotation_euler = (0.0, math.radians(spin[frame - 1] * whole / max(spin[-1], 1e-6)), 0.0)
        pb["cannon"].location = (0.0, -0.035 * gun_len if fired else 0.0, 0.0)
        pb["root"].location = (0.0, 0.004 * size if fired else 0.0, 0.0)
        pb["root"].rotation_euler = (math.radians(1.5 * brace - (0.5 if fired else 0.0)), 0.0, math.radians(0.3 * shiver))
        for name in ("cannon", "root"):
            pb[name].keyframe_insert("rotation_euler", frame=frame)
            pb[name].keyframe_insert("location", frame=frame)
        for name in pods:
            pb[name].rotation_euler = (math.radians(1.2 if fired else 0.0), 0.0, 0.0)
            pb[name].keyframe_insert("rotation_euler", frame=frame)
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, last
    for fc in [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    rig.animation_data.action = None
    for b in pb:
        b.rotation_euler = (0, 0, 0)
        b.location = (0, 0, 0)


def main():
    scene = bpy.context.scene
    scene.render.fps = 24
    ob = next(o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SM_Pet_DroneCore_"))
    NAME = ob.name
    stage = NAME.rsplit("_", 1)[1]
    cfg = STAGES[stage]
    if ob.vertex_groups.get("shutter") is not None:
        if stage in MINIGUN and ob.parent is not None and ob.parent.type == "ARMATURE":
            minigun_fire(ob.parent, cfg["size"])
            if cfg.get("colours"):
                print("DRONECORE %s%%: colours set, %d faces moved to another colour" % (stage, set_colours(ob, cfg["colours"])))
            print("DRONECORE %s%%: already rigged; MinigunFire made again from the rig. Clips %s" % (stage, sorted(x.name for x in bpy.data.actions)))
            return
        raise SystemExit("This model is already rigged. Make the low-poly model again first: see the top of this file.")
    for o in list(bpy.data.objects):
        if o.type == "ARMATURE":
            bpy.data.objects.remove(o, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    me = ob.data
    k = cfg["size"] / cfg["width"]
    C = Vector(cfg["centre"])

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

    # ---- one watertight skin. The scan is many open, overlapping shells; filling their holes one by one leaves
    #      shards. So the whole model is rebuilt as a single closed surface that wraps what can be seen (a fine
    #      voxel skin), thinned again to low-poly, and every face takes back the colour of the old face nearest it.
    if cfg.get("skin", True):
        old = bmesh.new()
        old.from_mesh(me)
        old.transform(ob.matrix_world)
        old.faces.ensure_lookup_table()
        old_tree = BVHTree.FromBMesh(old)
        old_uv = old.loops.layers.uv.active
        old_look = [(f.material_index, sum((l[old_uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops) if old_uv else Vector((0.5, 0.5))) for f in old.faces]
        old.to_mesh(me)
        old.free()
        ob.matrix_world.identity()
        bpy.context.view_layer.objects.active = ob
        for o in bpy.context.view_layer.objects:
            o.select_set(o is ob)
        for m in list(ob.modifiers):
            ob.modifiers.remove(m)
        wall = ob.modifiers.new("Wall", "SOLIDIFY")           # an open sheet has no inside for the skin to wrap: give it a wall
        wall.thickness = cfg["width"] * VOXEL * 2.4
        wall.offset = 0.0
        bpy.ops.object.modifier_apply(modifier=wall.name)
        skin = ob.modifiers.new("Skin", "REMESH")
        skin.mode = "VOXEL"
        skin.voxel_size = cfg["width"] * VOXEL
        skin.adaptivity = 0.0
        bpy.ops.object.modifier_apply(modifier=skin.name)
        thin = ob.modifiers.new("Thin", "DECIMATE")
        thin.decimate_type = "COLLAPSE"
        thin.ratio = min(1.0, cfg.get("tris", TRIS) / max(sum(len(p.vertices) - 2 for p in ob.data.polygons), 1))
        bpy.ops.object.modifier_apply(modifier=thin.name)
        me = ob.data
        if not me.uv_layers:
            me.uv_layers.new(name="map1")
        bm = bmesh.new()
        bm.from_mesh(me)
        uv_new = bm.loops.layers.uv.verify()
        # the skin also wraps the hollows inside the model: those pieces cannot be seen and go
        bm.faces.ensure_lookup_table()
        skin_tree = BVHTree.FromBMesh(bm)
        ways = [Vector(d).normalized() for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1), (1, 1, 1), (-1, 1, 1), (1, -1, 1),
                                               (-1, -1, 1), (1, 1, -1), (-1, 1, -1), (1, -1, -1), (-1, -1, -1))]
        hidden = []
        for group in islands_of(bm):
            seen_ = False
            for f in group[::max(1, len(group) // 60)]:
                c_ = f.calc_center_median() + f.normal * (cfg["width"] * 0.002)
                if any(skin_tree.ray_cast(c_, d)[0] is None for d in ways if d.dot(f.normal) > 0):
                    seen_ = True
                    break
            if not seen_:
                hidden += group
        bmesh.ops.delete(bm, geom=hidden, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        for f in bm.faces:
            index = old_tree.find_nearest(f.calc_center_median())[2]
            slot, spot = old_look[index] if index is not None else (0, Vector((0.5, 0.5)))
            f.material_index = slot
            f.smooth = False
            for l in f.loops:
                l[uv_new].uv = spot
    else:
        # this form keeps the mesh and UV map it came with (its texture is painted on that map): only its holes are closed
        turn_ = ob.matrix_world.to_quaternion()
        own_corner = [turn_ @ Vector(n.vector) for n in me.corner_normals]      # the shading it came with: hard edges and all
        own_loops = [list(poly.loop_indices) for poly in me.polygons]
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(ob.matrix_world)
        ob.matrix_world.identity()
    for v in bm.verts:                                    # the stage's size, the cube's middle at the origin
        v.co = (v.co - C) * k
    uv = bm.loops.layers.uv.verify()

    keeps_own_mesh = not cfg.get("skin", True)
    before = bm.copy()
    before.normal_update()
    before.faces.ensure_lookup_table()
    before_tree = BVHTree.FromBMesh(before)
    uv = bm.loops.layers.uv.verify()
    made_layer = bm.faces.layers.int.new("kn_made")       # 0 the model's own surface, 1 a lid that closes a cut, 2 a new lens part

    L = cfg["lens"]
    axis_x, axis_z = (L["x"] - C.x) * k, (L["z"] - C.z) * k
    outer, inner = L["outer"] * k, L["inner"] * k
    face_y, back_y = (L["face"] - C.y) * k, (L["back"] - C.y) * k

    def radius_of(co):
        return math.hypot(co.x - axis_x, co.z - axis_z)

    # ---- the scan's lens goes: everything inside the barrel's round, forward of its back
    if keeps_own_mesh:
        # the hole is cut, not picked face by face: a 24-sided cut just inside the barrel's own outline, so the edge
        # of the hole is a clean round hidden in the barrel's wall and nothing can be seen between barrel and body
        hole_r = outer * 0.985 * math.cos(math.pi / AROUND)
        sides_ = [Vector((math.sin(2 * math.pi * (i + 0.5) / AROUND), 0.0, math.cos(2 * math.pi * (i + 0.5) / AROUND))) for i in range(AROUND)]
        axis_at = Vector((axis_x, 0.0, axis_z))
        for n_ in sides_:
            near = [f for f in bm.faces if any(radius_of(v.co) < outer * 1.6 for v in f.verts) and min(v.co.y for v in f.verts) < back_y + 0.004]
            geom = list({v for f in near for v in f.verts}) + list({e for f in near for e in f.edges}) + near
            bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=axis_at + n_ * hole_r, plane_no=n_)

        def in_hole(co):
            return all((co - axis_at).dot(n_) < hole_r for n_ in sides_)

        gone = [f for f in bm.faces if in_hole(f.calc_center_median()) and f.calc_center_median().y < back_y + 0.004]
    else:
        gone = [f for f in bm.faces if radius_of(f.calc_center_median()) < outer * 0.98 and f.calc_center_median().y < back_y + 0.004]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    if keeps_own_mesh:
        # the hole is closed by a cone to a point deep on the lens's axis: behind the barrel and the glass, out of sight
        deep = bm.verts.new(Vector((axis_x, back_y + 0.55 * outer, axis_z)))
        rim = [e for e in bm.edges if len(e.link_faces) == 1 and all(radius_of(v.co) < outer * 0.992 and v.co.y < back_y + 0.004 for v in e.verts)]
        for e in rim:
            f = bm.faces.new((e.verts[0], e.verts[1], deep))
            f[made_layer] = 1
            near = e.link_faces[0]
            f.material_index = near.material_index
            f.smooth = False
            spot_ = sum((m[uv].uv for m in near.loops), Vector((0, 0))) / len(near.loops)
            for l in f.loops:
                l[uv].uv = spot_
        if not deep.link_faces:
            bm.verts.remove(deep)

    # ---- the cannon and the pods are cut free of the body along the edge of their own space
    front = min(v.co.y for v in bm.verts)
    tip = [v.co for v in bm.verts if v.co.y < front + 0.03 * k]
    gun_x, gun_z = sum(p.x for p in tip) / len(tip), sum(p.z for p in tip) / len(tip)
    gun_r = max(math.hypot(p.x - gun_x, p.z - gun_z) for p in tip) * 1.45
    cut_y = (cfg["cannon_cut"] - C.y) * k

    # a clean cut: the mesh is sliced along the plane where the barrel leaves its housing, so the barrel's end and the
    # housing's mouth are two neat rings, not a ragged line of whole faces
    near_gun = [f for f in bm.faces if any(math.hypot(v.co.x - gun_x, v.co.z - gun_z) < gun_r * 1.6 and abs(v.co.y - cut_y) < 0.2 * cfg["size"] for v in f.verts)]
    slice_geom = list({v for f in near_gun for v in f.verts}) + list({e for f in near_gun for e in f.edges}) + near_gun
    bmesh.ops.bisect_plane(bm, geom=slice_geom, dist=1e-6, plane_co=Vector((0, cut_y, 0)), plane_no=Vector((0, 1, 0)))

    def in_cannon(co):
        return co.y < cut_y and math.hypot(co.x - gun_x, co.z - gun_z) < gun_r

    spaces = {"cannon": in_cannon}
    pod_axes, pod_round = {}, {}
    for name, (px_, pz_, pr_, y0, y1) in cfg["pods"].items():
        ax, az, ar = (px_ - C.x) * k, (pz_ - C.z) * k, pr_ * k
        ya, yb = (y0 - C.y) * k, (y1 - C.y) * k
        pod_axes[name] = (ax, az, ya, yb)
        pod_round[name] = ar        # the pods are not cut free: their brackets join them to the body, so they are weighted, fading out at the bracket

    # a cut leaves faces of five corners and more; they are made triangles here, as part of the model's own surface,
    # keeping their place on the texture (left for later they would be taken for lids and lose it)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])

    def space_of(face):
        c = face.calc_center_median()
        return next((name for name, test in spaces.items() if test(c)), "body")

    tag = {f: space_of(f) for f in bm.faces}
    border = [e for e in bm.edges if len({tag[f] for f in e.link_faces}) > 1]
    if border:
        bmesh.ops.split_edges(bm, edges=border)

    # ---- closed: edges with more than two faces are parted, and every open ring is filled
    for turn_ in range(12):
        crowded = [e for e in bm.edges if len(e.link_faces) > 2]
        if crowded:
            bmesh.ops.split_edges(bm, edges=crowded)
        gaps = [e for e in bm.edges if len(e.link_faces) == 1]
        if not gaps and not crowded:
            break
        had = set(bm.faces)
        if turn_ < 2 and not keeps_own_mesh:
            bmesh.ops.holes_fill(bm, edges=gaps, sides=0)
        else:
            left = set(gaps)
            while left:
                e = left.pop()
                ring, v = [e], e.verts[1]
                while True:
                    onward = [x for x in v.link_edges if x in left]
                    if not onward:
                        break
                    e = onward[0]
                    left.discard(e)
                    ring.append(e)
                    v = e.other_vert(v)
                points = {v for e in ring for v in e.verts}
                hub_at = sum((v.co for v in points), Vector()) / len(points)
                if keeps_own_mesh:
                    # the lid is a shallow pyramid behind the surface it closes, never flush with it: a loose plate
                    # closed flat would lie in its own plane and flicker. An end cut straight across (a tube) stays flat.
                    facing = sum((f.normal * f.calc_area() for e in ring for f in e.link_faces), Vector())
                    spread = sum((v.co - hub_at).length for v in points) / len(points)
                    flatness = facing.length / max(sum(f.calc_area() for e in ring for f in e.link_faces), 1e-12)
                    if flatness > 0.5:
                        hub_at = hub_at - facing.normalized() * min(max(0.15 * spread, 0.002), 0.012)
                hub = bm.verts.new(hub_at)
                for e in ring:
                    try:
                        bm.faces.new((e.verts[0], e.verts[1], hub))
                    except ValueError:
                        pass
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        for f in [f for f in bm.faces if f not in had]:
            f[made_layer] = 1
            near = next((g for e in f.edges for g in e.link_faces if g in had), None)
            if near is not None:
                f.material_index = near.material_index
                f.smooth = False if keeps_own_mesh else near.smooth
                spot_ = sum((m[uv].uv for m in near.loops), Vector((0, 0))) / len(near.loops)
                for l in f.loops:                             # a lid is one flat colour: its neighbour's
                    l[uv].uv = spot_
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for group in islands_of(bm):                           # a piece that came out inside out is turned
        if sum(f.calc_center_median().dot(f.normal) * f.calc_area() for f in group) < 0:
            bmesh.ops.reverse_faces(bm, faces=group)

    # which piece follows which bone: a whole piece goes with the space most of it lies in
    owner_of = {}
    for group in islands_of(bm):
        votes = {}
        for f in group:
            s_ = space_of(f)
            votes[s_] = votes.get(s_, 0.0) + f.calc_area()
        best = max(votes, key=votes.get)
        for f in group:
            for v in f.verts:
                owner_of[v] = best

    # ---- 100 % only: the swarm's blocks. A closed skin is wrapped round the finished hull, thinned, and sliced into
    #      blocks (three across, two deep, three high) with flat ends; each block is one flat colour a face, taken
    #      from the hull's texture, sits a little smaller inside the hull, and has a bone of its own.
    if cfg.get("shards"):
        for v in list(owner_of):
            if owner_of[v] == "body":
                owner_of[v] = "hull"
        hull_faces = [f for f in bm.faces if owner_of.get(f.verts[0]) == "hull"]
        wrap = bmesh.new()
        twin = {}
        for f in hull_faces:
            for v in f.verts:
                if v not in twin:
                    twin[v] = wrap.verts.new(v.co)
            try:
                wrap.faces.new([twin[v] for v in f.verts])
            except ValueError:
                pass
        wrap_me = bpy.data.meshes.new("kn_wrap")
        wrap.to_mesh(wrap_me)
        wrap.free()
        wrap_ob = bpy.data.objects.new("kn_wrap", wrap_me)
        scene.collection.objects.link(wrap_ob)
        bpy.context.view_layer.objects.active = wrap_ob
        for o in bpy.context.view_layer.objects:
            o.select_set(o is wrap_ob)
        wall = wrap_ob.modifiers.new("Wall", "SOLIDIFY")
        wall.thickness = cfg["size"] * SHARD_VOXEL * 2.4
        wall.offset = 0.0
        bpy.ops.object.modifier_apply(modifier=wall.name)
        skin = wrap_ob.modifiers.new("Skin", "REMESH")
        skin.mode = "VOXEL"
        skin.voxel_size = cfg["size"] * SHARD_VOXEL
        skin.adaptivity = 0.0
        bpy.ops.object.modifier_apply(modifier=skin.name)
        thin = wrap_ob.modifiers.new("Thin", "DECIMATE")
        thin.decimate_type = "COLLAPSE"
        thin.ratio = min(1.0, SHARD_TRIS / max(sum(len(p.vertices) - 2 for p in wrap_ob.data.polygons), 1))
        bpy.ops.object.modifier_apply(modifier=thin.name)
        sk = bmesh.new()
        sk.from_mesh(wrap_ob.data)
        bpy.data.objects.remove(wrap_ob, do_unlink=True)
        sk.faces.ensure_lookup_table()
        sk_tree = BVHTree.FromBMesh(sk)
        ways = [Vector(d).normalized() for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1), (1, 1, 1), (-1, 1, 1), (1, -1, 1),
                                               (-1, -1, 1), (1, 1, -1), (-1, 1, -1), (1, -1, -1), (-1, -1, -1))]
        hidden = []
        for group in islands_of(sk):                        # the skin also wraps hollows inside the hull: those go
            seen_ = False
            for f in group[::max(1, len(group) // 60)]:
                c_ = f.calc_center_median() + f.normal * (cfg["size"] * 0.002)
                if any(sk_tree.ray_cast(c_, d)[0] is None for d in ways if d.dot(f.normal) > 0):
                    seen_ = True
                    break
            if not seen_:
                hidden += group
        bmesh.ops.delete(sk, geom=hidden, context="FACES")
        bmesh.ops.delete(sk, geom=[v for v in sk.verts if not v.link_faces], context="VERTS")

        # the colour of a point of the hull: where its texture is read for the face nearest that point
        from mathutils.interpolate import poly_3d_calc as weights_in
        before_uv = before.loops.layers.uv.active

        def spot_of(co):
            at, _n, index, _d = before_tree.find_nearest(co)
            face = before.faces[index]
            w = weights_in([v.co for v in face.verts], at)
            return sum((l[before_uv].uv * w_ for l, w_ in zip(face.loops, w)), Vector((0, 0)))

        # a block's cut ends are one dark colour of the hull's own
        dark_spot = spot_of(Vector((0, 0, 0)))
        picture = next((slot.material.node_tree.nodes[n.name].image for slot in ob.material_slots if slot.material and slot.material.use_nodes
                        for n in slot.material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image and "Emission" not in n.image.name), None)
        if picture is not None:
            import numpy as np
            w_, h_ = picture.size
            px = np.empty(w_ * h_ * 4, dtype=np.float32)
            picture.pixels.foreach_get(px)
            px = px.reshape(h_, w_, 4)
            tried = []
            for f in before.faces:
                if f.calc_area() < 1e-5:
                    continue
                c_uv = sum((l[before_uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
                r_, g_, b_ = px[int(c_uv.y % 1.0 * (h_ - 1)), int(c_uv.x % 1.0 * (w_ - 1)), :3]
                tried.append((float(r_ + g_ + b_), max(r_, g_, b_) - min(r_, g_, b_), c_uv))
            tried.sort(key=lambda t: t[0])
            few = tried[len(tried) // 20: len(tried) // 5]
            if few:
                dark_spot = min(few, key=lambda t: t[1])[2]

        third = cfg["size"] / 6
        shard_at = {}
        cap_layer = sk.faces.layers.int.new("cap")
        made_blocks = 0
        for ix in range(3):
            for iy in range(2):
                for iz in range(3):
                    blk = sk.copy()
                    cap_l = blk.faces.layers.int.get("cap")
                    cuts = []
                    if ix > 0:
                        cuts.append((Vector(((-third, third)[ix - 1], 0, 0)), Vector((1, 0, 0)), False))
                    if ix < 2:
                        cuts.append((Vector(((-third, third)[ix], 0, 0)), Vector((1, 0, 0)), True))
                    cuts.append((Vector((0, 0, 0)), Vector((0, 1, 0)), iy == 0))
                    if iz > 0:
                        cuts.append((Vector((0, 0, (-third, third)[iz - 1])), Vector((0, 0, 1)), False))
                    if iz < 2:
                        cuts.append((Vector((0, 0, (-third, third)[iz])), Vector((0, 0, 1)), True))
                    for co_, no_, keep_below in cuts:
                        if not blk.faces:
                            break
                        bmesh.ops.bisect_plane(blk, geom=blk.verts[:] + blk.edges[:] + blk.faces[:], dist=1e-6, plane_co=co_, plane_no=no_,
                                               clear_outer=keep_below, clear_inner=not keep_below)
                        rim_ = [e for e in blk.edges if len(e.link_faces) == 1]
                        if rim_:
                            had_ = set(blk.faces)
                            bmesh.ops.triangle_fill(blk, use_beauty=True, use_dissolve=False, edges=rim_, normal=no_)
                            for f in blk.faces:
                                if f not in had_:
                                    f[cap_l] = 1
                    left = {e for e in blk.edges if len(e.link_faces) == 1}       # what the flat fill could not close
                    while left:
                        e = left.pop()
                        ring, v = [e], e.verts[1]
                        while True:
                            onward = [x for x in v.link_edges if x in left]
                            if not onward:
                                break
                            e = onward[0]
                            left.discard(e)
                            ring.append(e)
                            v = e.other_vert(v)
                        points = {v for e in ring for v in e.verts}
                        hub = blk.verts.new(sum((v.co for v in points), Vector()) / len(points))
                        for e in ring:
                            try:
                                blk.faces.new((e.verts[0], e.verts[1], hub))[cap_l] = 1
                            except ValueError:
                                pass
                    for group in islands_of(blk):               # crumbs go
                        if len(group) < 8:
                            bmesh.ops.delete(blk, geom=group, context="FACES")
                    bmesh.ops.delete(blk, geom=[v for v in blk.verts if not v.link_faces], context="VERTS")
                    if not blk.faces:
                        blk.free()
                        continue
                    bmesh.ops.recalc_face_normals(blk, faces=blk.faces)
                    for group in islands_of(blk):
                        mid_ = sum((f.calc_center_median() for f in group), Vector()) / len(group)
                        if sum((f.calc_center_median() - mid_).dot(f.normal) * f.calc_area() for f in group) < 0:
                            bmesh.ops.reverse_faces(blk, faces=group)
                    name = f"shard.{ix}{iy}{iz}"
                    mid_ = sum((v.co for v in blk.verts), Vector()) / len(blk.verts) * 0.6     # its bone: towards the body's middle, well inside the hull
                    shard_at[name] = mid_.copy()
                    copy_ = {}
                    for v in blk.verts:
                        nv = bm.verts.new(mid_ + (v.co - mid_) * SHARD_SMALL)
                        copy_[v] = nv
                        owner_of[nv] = name
                    for f in blk.faces:
                        try:
                            nf = bm.faces.new([copy_[v] for v in f.verts])
                        except ValueError:
                            continue
                        nf[made_layer] = 3
                        nf.material_index = 0
                        nf.smooth = False
                        spot_ = dark_spot if f[cap_l] else spot_of(f.calc_center_median())
                        for l in nf.loops:
                            l[uv].uv = spot_
                    made_blocks += 1
                    blk.free()
        sk.free()
        print("SWARM BLOCKS", made_blocks)

    # ---- the new lens
    glass_slot = flat("Glass", cfg["glass"][0], glow=cfg["glass"][1])
    bezel_slot = flat("Bezel", (0.045, 0.048, 0.052))
    glow_slot = flat("Ring Glow", GLOW, glow=2.0)
    mark_slot = flat("Mark", (0.95, 0.95, 0.95), glow=0.6)
    red_slot = flat("Mark Red", (0.9, 0.02, 0.02), glow=2.0)
    flash_slot = flat("Flash", (1.0, 1.0, 1.0), glow=4.0)

    def paint(faces, slot):
        for f in faces:
            f[made_layer] = 2
            f.material_index = slot
            f.smooth = False

    def spoke(i):
        ang = 2 * math.pi * i / AROUND
        return Vector((math.sin(ang), 0.0, math.cos(ang)))

    r1, r2 = outer * BAND[0], outer * BAND[1]
    lip = inner + 0.0012
    sunk_y = back_y + (SINK * outer if keeps_own_mesh else 0.0)      # its back is inside the body, so pushed out (BarrelTurn) it still shows no gap
    outline = [(lip, sunk_y), (lip, face_y), (r1, face_y), (r2, face_y), (outer * 0.95, face_y), (outer, face_y + (back_y - face_y) * 0.25), (outer, sunk_y)]
    rings = [[bm.verts.new(Vector((axis_x, y, axis_z)) + spoke(i) * r) for i in range(AROUND)] for r, y in outline]
    barrel_faces, band_faces = [], []
    for i in range(len(outline)):
        a_, b_ = rings[i], rings[(i + 1) % len(outline)]
        for j in range(AROUND):
            j2 = (j + 1) % AROUND
            f = bm.faces.new((a_[j], a_[j2], b_[j2], b_[j]))
            barrel_faces.append(f)
            if i == 2 and j not in GAPS:
                band_faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=barrel_faces)
    paint(barrel_faces, bezel_slot)
    if cfg["band"]:
        paint(band_faces, glow_slot)
    barrel_verts = [v for ring in rings for v in ring]

    rim_y = face_y + 0.30 * inner                        # the glass sits back in the barrel, domed towards the front
    bulge = 0.20 * inner
    apex_y = rim_y - bulge
    curve = (inner ** 2 + bulge ** 2) / (2 * bulge)
    centre = Vector((axis_x, apex_y + curve, axis_z))

    def on_dome(px, pz, lift=0.0):
        room = max((curve + lift) ** 2 - px * px - pz * pz, 1e-9)
        return Vector((axis_x + px, centre.y - math.sqrt(room), axis_z + pz))

    steps = 4
    dome = [[bm.verts.new(on_dome(*(spoke(i).xz * (inner * j / steps)))) for i in range(AROUND)] for j in range(1, steps + 1)]
    top = bm.verts.new(on_dome(0.0, 0.0))
    behind = bm.verts.new(Vector((axis_x, rim_y + 0.10 * inner, axis_z)))
    glass_faces = []
    for i in range(AROUND):
        i2 = (i + 1) % AROUND
        glass_faces.append(bm.faces.new((top, dome[0][i], dome[0][i2])))
        for a_, b_ in zip(dome[:-1], dome[1:]):
            glass_faces.append(bm.faces.new((a_[i], b_[i], b_[i2], a_[i2])))
        glass_faces.append(bm.faces.new((behind, dome[-1][i2], dome[-1][i])))
    bmesh.ops.recalc_face_normals(bm, faces=glass_faces)
    paint(glass_faces, glass_slot)
    for f in glass_faces:
        f.smooth = True
    glass_verts = [top, behind] + [v for ring in dome for v in ring]

    flash_home = Vector((axis_x, rim_y + 0.06 * inner, axis_z))
    flash_rings = [[bm.verts.new(Vector((axis_x, y, axis_z)) + spoke(i) * (inner * 0.99)) for i in range(AROUND)]
                   for y in (apex_y - 0.05 * inner, apex_y - 0.02 * inner)]
    flash_mid = [bm.verts.new(Vector((axis_x, y, axis_z))) for y in (apex_y - 0.05 * inner, apex_y - 0.02 * inner)]
    flash_faces = []
    for i in range(AROUND):
        i2 = (i + 1) % AROUND
        flash_faces.append(bm.faces.new((flash_mid[0], flash_rings[0][i], flash_rings[0][i2])))
        flash_faces.append(bm.faces.new((flash_mid[1], flash_rings[1][i2], flash_rings[1][i])))
        flash_faces.append(bm.faces.new((flash_rings[0][i], flash_rings[1][i], flash_rings[1][i2], flash_rings[0][i2])))
    bmesh.ops.recalc_face_normals(bm, faces=flash_faces)
    paint(flash_faces, flash_slot)
    flash_verts = flash_mid + [v for ring in flash_rings for v in ring]
    for v in flash_verts:
        v.co = flash_home + (v.co - flash_home) * FLASH_SMALL

    dot_r, dot_high = DOT["radius"] * inner, DOT["high"] * inner
    dot_at = Vector(DOT["at"]) * inner
    dot_mid = on_dome(dot_at.x, dot_at.y)
    out_dir = (dot_mid - centre).normalized()
    side_a = out_dir.cross(Vector((0, 0, 1))).normalized()
    side_b = out_dir.cross(side_a).normalized()
    red_sunk = 0.06 * inner

    def make_dot(slot, smaller=1.0, sunk=0.0):
        made = []
        tip_ = bm.verts.new(dot_mid + out_dir * (dot_high * smaller - sunk))
        under = bm.verts.new(dot_mid - out_dir * (0.03 * inner + sunk))
        edge = []
        for i in range(12):
            ang = 2 * math.pi * i / 12
            flat_at = dot_mid + (side_a * math.cos(ang) + side_b * math.sin(ang)) * dot_r * smaller
            edge.append(bm.verts.new(centre + (flat_at - centre).normalized() * (curve + 0.004 * inner) - out_dir * sunk))
        for i in range(12):
            i2 = (i + 1) % 12
            made.append(bm.faces.new((tip_, edge[i], edge[i2])))
            made.append(bm.faces.new((under, edge[i2], edge[i])))
        bmesh.ops.recalc_face_normals(bm, faces=made)
        paint(made, slot)
        return [tip_, under] + edge

    dot_verts = make_dot(mark_slot)
    red_verts = make_dot(red_slot, RED_SMALLER, red_sunk)
    reach = (inner - dot_r) * EYE_MARGIN

    for verts, bone_name in ((barrel_verts, "shutter"), (glass_verts, "lens"), (dot_verts, "eye"), (red_verts, "eye_red"), (flash_verts, "flash")):
        for v in verts:
            owner_of[v] = bone_name
    bm.verts.index_update()
    owner = {v.index: owner_of.get(v, "body") for v in bm.verts}
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    counts = {}
    for b in owner.values():
        counts[b] = counts.get(b, 0) + 1
    shard_mid = {}
    for name in [n for n in counts if n.startswith("shard.")]:
        pts_ = [v.co for v in bm.verts if owner[v.index] == name]
        shard_mid[name] = shard_at[name]
    gun_verts = [v.co for v in bm.verts if owner[v.index] == "cannon"]
    gun_back = cut_y                                      # where the barrel leaves its housing (its lid reaches further in)
    gun_front = min(p.y for p in gun_verts) if gun_verts else front
    bm.faces.index_update()
    made = [f[made_layer] for f in bm.faces]
    lens_smooth = [f.smooth for f in bm.faces]
    bm.to_mesh(me)
    bm.free()
    me.update()

    if keeps_own_mesh:
        # ---- the surface shades as it did before it was cut. Cutting splits the corners along every cut, and each
        #      piece would then be shaded on its own: dents and seams all over the body. So every corner of the
        #      model's own surface is given the direction the uncut surface had at that spot (read from the
        #      model as it came, so its hard edges stay hard); lids, blocks and the new lens parts keep their own.
        from mathutils.interpolate import poly_3d_calc
        normals = []
        for poly in me.polygons:
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                if made[poly.index] == 0:
                    spot, _n, index, _d = before_tree.find_nearest(co)
                    face = before.faces[index]
                    weights = poly_3d_calc([v.co for v in face.verts], spot)
                    n_ = sum((own_corner[li_] * w for li_, w in zip(own_loops[index], weights)), Vector())
                    normals.append(n_.normalized() if n_.length > 1e-9 else poly.normal.copy())
                elif made[poly.index] == 2 and lens_smooth[poly.index]:
                    normals.append(me.vertices[me.loops[li].vertex_index].normal.copy())
                else:
                    normals.append(poly.normal.copy())
        for poly in me.polygons:
            poly.use_smooth = True
        me.normals_split_custom_set(normals)
    before.free()

    # ---- the rig
    arm = bpy.data.armatures.new(NAME + "_Rig")
    rig = bpy.data.objects.new(NAME + "_Rig", arm)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects:
        o.select_set(o is rig)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = eb[parent]

    bone("root", (0, 0, 0), (0, 0.08, 0))
    bone("body", (0, 0, 0), (0, 0, 0.08), "root")
    bone("lens", (axis_x, back_y, axis_z), (axis_x, back_y, axis_z + 0.05), "body")       # upright: squash its length to squint
    bone("shutter", (axis_x, back_y, axis_z), (axis_x, face_y, axis_z), "lens")           # along the lens's axis
    bone("eye", centre, dot_mid, "lens")
    bone("eye_red", dot_mid - out_dir * 0.004, dot_mid + out_dir * 0.004, "eye")
    bone("flash", flash_home, flash_home + Vector((0, -0.01, 0)), "lens")
    bone("cannon", (gun_x, gun_back, gun_z), (gun_x, gun_front, gun_z), "body")           # along the barrel: its length is "forward"
    for name, (ax, az, ya, yb) in pod_axes.items():
        bone(name, (ax, yb, az), (ax, ya, az), "body")
    if shard_mid:
        bone("hull", (0, 0, 0), (0, 0, 0.06), "body")
    for name, mid_ in shard_mid.items():
        bone(name, mid_, mid_ + Vector((0, 0, 0.03)), "body")
    for b in eb:
        b.roll = 0.0
    bpy.ops.object.mode_set(mode="OBJECT")
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    for bone_name in [b.name for b in arm.bones if b.name != "root"]:
        ob.vertex_groups.new(name=bone_name).add([i for i, b in owner.items() if b == bone_name], 1.0, "REPLACE")
    for name, (ax, az, ya, yb) in pod_axes.items():
        ar = pod_round[name]
        group, body_group = ob.vertex_groups[name], ob.vertex_groups["body"]
        for v in me.vertices:
            if owner[v.index] != "body":
                continue
            r_ = math.hypot(v.co.x - ax, v.co.z - az)
            w = 1.0 - min(1.0, max(0.0, (r_ - 0.85 * ar) / (0.45 * ar)))
            outside = max(ya - v.co.y, v.co.y - yb, 0.0)
            w *= 1.0 - min(1.0, outside / (0.25 * ar))
            w = w * w * (3 - 2 * w)
            if w > 0.001:
                group.add([v.index], w, "REPLACE")
                body_group.add([v.index], 1.0 - w, "REPLACE")
    ob.parent = rig
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- the clips
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

    def slide(bone_name, frame, along):
        pb[bone_name].location = (0.0, along, 0.0)
        pb[bone_name].keyframe_insert("location", frame=frame)

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
    for step in range(1, 25):
        ang = -math.pi / 2 + 2 * math.pi * step / 24
        look(72 + step * 2, math.cos(ang), math.sin(ang))
    look(132, rest_at.x, rest_at.y)
    look(138, rest_at.x, rest_at.y)
    finish(a, 138)

    # BarrelTurn: taking a photo (see the 25 % form's script)
    a = start("BarrelTurn")
    last[0] = None
    push = 0.22 * outer
    for frame, out, degrees in ((1, 0.0, 0), (4, -0.08, 0), (10, 1.12, 0), (13, 1.0, 0), (16, 1.0, -5), (26, 1.0, 94), (29, 1.0, 90),
                                (34, 1.0, 90), (36, 0.72, 90), (41, 1.0, 90), (46, 1.0, 90), (49, 1.0, 95), (59, 1.0, -4), (62, 1.0, 0),
                                (66, 1.0, 0), (72, -0.06, 0), (76, 0.0, 0), (80, 0.0, 0)):
        slide("shutter", frame, push * out)
        turn("shutter", frame, y=degrees)
    for frame, x, z in ((1, rest_at.x, rest_at.y), (6, rest_at.x, rest_at.y), (14, 0, 0), (62, 0, 0), (72, rest_at.x, rest_at.y), (80, rest_at.x, rest_at.y)):
        look(frame, x, z)
    for frame, big in ((1, False), (33, False), (34, True), (35, True), (36, False), (80, False)):
        pb["flash"].scale = ((1.0 / FLASH_SMALL) if big else 1.0,) * 3
        pb["flash"].keyframe_insert("scale", frame=frame)
    for frame, tall in ((1, 1.0), (35, 1.0), (37, 0.08), (38, 0.08), (41, 1.05), (44, 1.0), (80, 1.0)):
        pb["lens"].scale = (1.0, tall, 1.0)
        pb["lens"].keyframe_insert("scale", frame=frame)
    for frame, back, tip_ in ((1, 0.0, 0.0), (33, 0.0, 0.0), (36, 0.010, -3.0), (42, -0.002, 0.6), (48, 0.0, 0.0), (80, 0.0, 0.0)):
        slide("root", frame, back)
        turn("root", frame, x=tip_)
    finish(a, 80)
    for fc in curves_of(a):
        if "flash" in fc.data_path:
            for kp in fc.keyframe_points:
                kp.interpolation = "CONSTANT"

    a = start("AxisSpin")
    turn("root", 1)
    turn("root", 30, z=360)
    turn("root", 31)
    turn("root", 60, x=360)
    turn("root", 61)
    turn("root", 90, y=360)
    turn("root", 96, y=360)
    finish(a, 96)
    for fc in curves_of(a):
        for kp in fc.keyframe_points:
            if int(round(kp.co.x)) in (30, 60):
                kp.interpolation = "CONSTANT"

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

    a = start("Mad")
    red_out = red_sunk + dot_high + 0.03 * inner
    red_big = 1.25 / RED_SMALLER
    last[0] = None
    for frame, anger, squint in ((1, 0.0, 1.00), (5, 0.0, 1.06), (10, 1.0, 0.42), (14, 1.0, 0.56), (52, 1.0, 0.56),
                                 (58, 1.0, 0.50), (68, 0.0, 1.04), (76, 0.0, 1.00)):
        pb["lens"].scale = (1.0, squint, 1.0)
        pb["lens"].keyframe_insert("scale", frame=frame)
        pb["eye_red"].location = (0.0, red_out * anger, 0.0)
        pb["eye_red"].scale = (1 + (red_big - 1) * anger,) * 3
        pb["eye_red"].keyframe_insert("location", frame=frame)
        pb["eye_red"].keyframe_insert("scale", frame=frame)
        look(frame, rest_at.x * (1 - anger), rest_at.y * (1 - anger))
    turn("root", 1)
    turn("root", 12)
    for i, frame in enumerate(range(14, 53, 2)):
        shake = (1.0 if i % 2 else -1.0) * (2.2 if 2 < i < 17 else 1.0)
        turn("root", frame, z=shake, y=shake * 0.6)
    turn("root", 56)
    turn("root", 76)
    finish(a, 76)

    a = start("FrontRoll")
    turn("root", 1)
    turn("root", 25, x=360)
    finish(a, 25, straight=True)

    # CannonFire: it braces, fires twice (the barrel snaps back into its housing and eases out again, the drone
    # kicks back and its nose lifts), and settles. On the 75 % form the pods kick with each shot.
    a = start("CannonFire")
    gun_len = abs(gun_back - gun_front)
    for frame in (1, 8, 60):
        slide("cannon", frame, 0.0)
        slide("root", frame, 0.0)
        turn("root", frame)
    turn("root", 6, x=2.0)                                  # the brace: nose down a touch
    for shot in (12, 30):
        slide("cannon", shot - 1, 0.0)
        slide("cannon", shot + 1, -0.26 * gun_len)
        slide("cannon", shot + 12, 0.03 * gun_len)
        slide("cannon", shot + 16, 0.0)
        slide("root", shot - 1, 0.0)
        slide("root", shot + 2, 0.045 * cfg["size"])
        slide("root", shot + 12, -0.004 * cfg["size"])
        slide("root", shot + 16, 0.0)
        turn("root", shot - 1, x=2.0)
        turn("root", shot + 2, x=-7.0)
        turn("root", shot + 12, x=1.0)
        turn("root", shot + 16, x=0.0 if shot == 30 else 2.0)
        for name in pod_axes:
            turn(name, shot - 1)
            turn(name, shot + 2, x=7.0)
            turn(name, shot + 12, x=-2.0)
            turn(name, shot + 16)
    finish(a, 60)

    if stage in MINIGUN:
        minigun_fire(rig, cfg["size"])
    if cfg.get("colours"):
        set_colours(ob, cfg["colours"])

    if stage == "100":
        size = cfg["size"]

        def move(bone_name, frame, world_offset):
            """Moved by an offset given in the drone's own directions (x to its left, -y forward, z up)."""
            pb[bone_name].location = arm.bones[bone_name].matrix_local.to_3x3().inverted() @ Vector(world_offset)
            pb[bone_name].keyframe_insert("location", frame=frame)

        def squint(frame, tall):
            pb["lens"].scale = (1.0, tall, 1.0)
            pb["lens"].keyframe_insert("scale", frame=frame)

        def red(frame, anger):
            pb["eye_red"].location = (0.0, (red_sunk + dot_high + 0.03 * inner) * anger, 0.0)
            pb["eye_red"].scale = (1 + (1.25 / RED_SMALLER - 1) * anger,) * 3
            pb["eye_red"].keyframe_insert("location", frame=frame)
            pb["eye_red"].keyframe_insert("scale", frame=frame)

        # ---- FakeoutBlast: "Ka... me... ha..." wound up as if for a great blast, then nothing: "just kidding". It
        # relaxes, glances aside; then "Die!": the eye goes red and it fires for real, a long beam, braced against it.
        # The speech bubble and the beam are Unity's (see CUES at the top of this file): only the acting is here.
        a = start("FakeoutBlast")
        last[0] = None
        #        frame  back    nose   cannon  squint
        wind = [(1, 0.0, 0.0, 0.0, 1.0), (4, -0.010, 2.0, 0.02, 1.0), (12, 0.030, -6.0, -0.10, 0.90),        # Ka
                (15, 0.024, -4.0, -0.06, 0.92), (24, 0.060, -11.0, -0.18, 0.80),                            # Me
                (27, 0.066, -12.0, -0.20, 0.78), (31, -0.080, 9.0, 0.16, 1.10), (36, -0.070, 7.0, 0.12, 1.05),   # Ha! (thrust, and nothing)
                (44, -0.070, 7.0, 0.12, 1.05), (52, -0.020, 1.0, 0.03, 1.0), (58, 0.0, 0.0, 0.0, 1.0),      # ... just kidding
                (64, 0.006, -1.0, 0.0, 1.0), (67, -0.030, 6.0, 0.05, 0.50), (70, -0.020, 5.0, 0.0, 0.52),   # Die!
                (72, 0.050, -3.0, -0.26, 0.50), (110, 0.085, -2.0, -0.26, 0.50),                            # the beam, braced
                (116, 0.040, 2.0, 0.04, 0.9), (124, 0.0, 0.0, 0.0, 1.0), (130, 0.0, 0.0, 0.0, 1.0)]
        for frame, back, nose, gun, tall in wind:
            slide("root", frame, back * size)
            turn("root", frame, x=nose)
            slide("cannon", frame, gun * gun_len)
            squint(frame, tall)
        for i, frame in enumerate(range(74, 110, 2)):               # shaking against the beam
            turn("root", frame, x=-2.5 + (0.8 if i % 2 else -0.8), z=1.3 if i % 2 else -1.3)
            slide("cannon", frame, (-0.26 + (0.02 if i % 2 else -0.02)) * gun_len)
        for frame, wag in ((46, 0.0), (50, 5.0), (54, -5.0), (58, 0.0)):   # the shrug of "just kidding"
            turn("body", frame, y=wag)
        turn("body", 1)
        turn("body", 130)
        for frame, x, z in ((1, rest_at.x, rest_at.y), (10, 0, 0), (40, 0, 0), (47, -0.9, 0.2), (55, 0.9, 0.2), (62, rest_at.x, rest_at.y),
                            (66, 0, 0), (112, 0, 0), (124, rest_at.x, rest_at.y), (130, rest_at.x, rest_at.y)):
            look(frame, x, z)
        for frame, anger in ((1, 0.0), (64, 0.0), (67, 1.0), (112, 1.0), (120, 0.0), (130, 0.0)):
            red(frame, anger)
        finish(a, 130)

        # ---- SpinShots: the barrel spins up, fires eight quick shots as it spins, and winds down.
        a = start("SpinShots")
        shots = (26, 32, 38, 44, 50, 56, 62, 68)
        angle, spin_keys = 0.0, []
        for frame in range(1, 91):
            speed = min(1.0, (frame - 1) / 18.0) if frame < 70 else max(0.0, (90 - frame) / 20.0)
            spin_keys.append((frame, angle))
            angle += 52.0 * speed * (2 - speed) if frame < 70 else 52.0 * speed * speed
        whole = round(spin_keys[-1][1] / 360.0) * 360.0
        for frame, ang in spin_keys:
            turn("cannon", frame, y=ang * whole / max(spin_keys[-1][1], 1e-6))
        turn("cannon", 91, y=0.0)                                # the same pose as a whole number of turns
        slide("cannon", 1, 0.0)
        slide("root", 1, 0.0)
        turn("root", 1)
        for shot in shots:
            slide("cannon", shot - 1, 0.0)
            slide("cannon", shot + 1, -0.13 * gun_len)
            slide("cannon", shot + 4, 0.0)
            slide("root", shot - 1, 0.0)
            slide("root", shot + 1, 0.012 * size)
            slide("root", shot + 4, 0.0)
            turn("root", shot - 1)
            turn("root", shot + 1, x=-2.0)
            turn("root", shot + 4)
        slide("cannon", 91, 0.0)
        slide("root", 91, 0.0)
        turn("root", 91)
        finish(a, 91, straight=True)
        for fc in curves_of(a):
            if "cannon" in fc.data_path and "rotation" in fc.data_path:
                for kp in fc.keyframe_points:
                    if int(round(kp.co.x)) == 90:
                        kp.interpolation = "CONSTANT"

        # ---- the swarm: Disassemble (it shudders and bursts into its blocks, each flying out to hover as a small
        # drone of its own), SwarmHover (the loop while apart) and Assemble (they fly home block by block and lock).
        pieces = sorted(shard_mid) + ["lens", "cannon"]
        middle = Vector((0, 0, 0))
        away, tumble = {}, {}
        for i, name in enumerate(pieces):
            at = shard_mid.get(name, arm.bones[name].head_local)
            out_ = (Vector(at) - middle)
            out_ = out_.normalized() if out_.length > 1e-6 else Vector((0, -1, 0))
            if name == "lens":
                out_ = Vector((0, -1, 0.15)).normalized()
            if name == "cannon":
                out_ = Vector((0, -0.8, -0.6)).normalized()
            away[name] = out_ * size * (0.62 + 0.30 * ((i * 0.618) % 1.0))
            tumble[name] = (38.0 * math.sin(i * 1.7), 46.0 * math.cos(i * 2.3), 30.0 * math.sin(i * 0.9 + 1.0))

        def piece(name, frame, share, spin=1.0, bob=0.0):
            off = away[name] * share + Vector((0, 0, bob))
            move(name, frame, off)
            if name in ("lens", "cannon"):
                return                                         # they keep their own facing: their other clips turn them
            tx, ty, tz = tumble[name]
            turn(name, frame, x=tx * spin, y=ty * spin, z=tz * spin)

        def swap(frame, apart):
            """Hull out and blocks in (apart), or the other way: the same frame for all of them."""
            pb["hull"].scale = (0.0001,) * 3 if apart else (1.0,) * 3
            pb["hull"].keyframe_insert("scale", frame=frame)
            for name_ in shard_mid:
                pb[name_].scale = (1.0 / SHARD_SMALL,) * 3 if apart else (1.0,) * 3
                pb[name_].keyframe_insert("scale", frame=frame)

        def swaps_jump(action):
            for fc in curves_of(action):
                if "scale" in fc.data_path and ("hull" in fc.data_path or "shard." in fc.data_path):
                    for kp in fc.keyframe_points:
                        kp.interpolation = "CONSTANT"

        a = start("Disassemble")
        for frame, apart in ((1, False), (9, False), (10, True), (44, True)):
            swap(frame, apart)
        turn("root", 1)
        for i, frame in enumerate(range(2, 10)):                    # the shudder before it lets go
            turn("root", frame, z=1.6 if i % 2 else -1.6, x=0.8 if i % 2 else -0.8)
        turn("root", 11)
        turn("root", 44)
        for i, name in enumerate(pieces):
            go = 10 + (i * 5) % 12
            piece(name, 1, 0.0, 0.0)
            piece(name, go, 0.0, 0.0)
            piece(name, go + 9, 1.12, 0.8)
            piece(name, go + 15, 1.0, 1.0)
            piece(name, 44, 1.0, 1.0)
        finish(a, 44)
        swaps_jump(a)

        a = start("SwarmHover")
        swap(1, True)
        swap(49, True)
        for i, name in enumerate(pieces):
            for frame in range(1, 50, 4):
                t = 2 * math.pi * (frame - 1) / 48
                lift_ = size * 0.035 * math.sin(t) * (1 if i % 2 else -1)
                piece(name, frame, 1.0 + 0.03 * math.sin(2 * t) * (1 if i % 3 else -1), 1.0 + 0.12 * math.sin(t), lift_)
        finish(a, 49)
        swaps_jump(a)

        a = start("Assemble")
        for frame, apart in ((1, True), (35, True), (36, False), (46, False)):
            swap(frame, apart)
        for i, name in enumerate(reversed(pieces)):
            home = 6 + (i * 5) % 16
            piece(name, 1, 1.0, 1.0)
            piece(name, home, 1.0, 1.0)
            piece(name, home + 9, -0.035, 0.0)
            piece(name, home + 12, 0.0, 0.0)
            piece(name, 46, 0.0, 0.0)
        for frame, big in ((1, 1.0), (36, 1.0), (39, 1.05), (43, 0.99), (46, 1.0)):       # the lock: one small swell
            pb["root"].scale = (big, big, big)
            pb["root"].keyframe_insert("scale", frame=frame)
        finish(a, 46)
        swaps_jump(a)

    scene.frame_set(1)
    print("DRONECORE %s%%: cube %.2f m, whole model %s; %d triangles; lens radius %.1f mm, glass %.1f mm; cannon %.0f mm long at x %.3f z %.3f; "
          "vertices per bone %s; edges not shared by exactly two faces: %d; clips %s"
          % (stage, cfg["size"], tuple(round(d, 3) for d in ob.dimensions), tris, outer * 1000, inner * 1000, gun_len * 1000, gun_x, gun_z,
             counts, open_edges, sorted(x.name for x in bpy.data.actions)))


main()
