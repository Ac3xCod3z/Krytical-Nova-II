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
           "pods": {"pod.L": (-0.29, 0.225, 0.095, -0.19, 0.18), "pod.R": (0.265, 0.225, 0.10, -0.19, 0.18)}},   # axis x, z; radius; from y to y
    "100": {"size": 0.46, "width": 0.60, "centre": (0.0, 0.125, 0.38),
            "lens": {"x": 0.0, "z": 0.395, "outer": 0.108, "inner": 0.072, "face": -0.235, "back": -0.14},
            "glass": ((0.10, 0.85, 0.95), 2.5), "band": False, "cannon_cut": -0.215, "pods": {}, "skin": False},
}
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
    ob = next(o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SM_Pet_DroneCore_"))
    NAME = ob.name
    stage = NAME.rsplit("_", 1)[1]
    cfg = STAGES[stage]
    if ob.vertex_groups.get("shutter") is not None:
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
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(ob.matrix_world)
        ob.matrix_world.identity()
    for v in bm.verts:                                    # the stage's size, the cube's middle at the origin
        v.co = (v.co - C) * k
    uv = bm.loops.layers.uv.verify()

    L = cfg["lens"]
    axis_x, axis_z = (L["x"] - C.x) * k, (L["z"] - C.z) * k
    outer, inner = L["outer"] * k, L["inner"] * k
    face_y, back_y = (L["face"] - C.y) * k, (L["back"] - C.y) * k

    def radius_of(co):
        return math.hypot(co.x - axis_x, co.z - axis_z)

    # ---- the scan's lens goes: everything inside the barrel's round, forward of its back
    gone = [f for f in bm.faces if radius_of(f.calc_center_median()) < outer * 0.98 and f.calc_center_median().y < back_y + 0.004]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

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
        if turn_ < 2:
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
                hub = bm.verts.new(sum((v.co for v in points), Vector()) / len(points))
                for e in ring:
                    try:
                        bm.faces.new((e.verts[0], e.verts[1], hub))
                    except ValueError:
                        pass
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        for f in [f for f in bm.faces if f not in had]:
            near = next((g for e in f.edges for g in e.link_faces if g in had), None)
            if near is not None:
                f.material_index = near.material_index
                f.smooth = near.smooth
                for l in f.loops:                             # a new face takes its neighbour's colour
                    other = next((m for m in l.vert.link_loops if m.face in had), None)
                    l[uv].uv = other[uv].uv if other is not None else near.loops[0][uv].uv
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

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

    # ---- the new lens
    glass_slot = flat("Glass", cfg["glass"][0], glow=cfg["glass"][1])
    bezel_slot = flat("Bezel", (0.045, 0.048, 0.052))
    glow_slot = flat("Ring Glow", GLOW, glow=2.0)
    mark_slot = flat("Mark", (0.95, 0.95, 0.95), glow=0.6)
    red_slot = flat("Mark Red", (0.9, 0.02, 0.02), glow=2.0)
    flash_slot = flat("Flash", (1.0, 1.0, 1.0), glow=4.0)

    def paint(faces, slot):
        for f in faces:
            f.material_index = slot
            f.smooth = False

    def spoke(i):
        ang = 2 * math.pi * i / AROUND
        return Vector((math.sin(ang), 0.0, math.cos(ang)))

    r1, r2 = outer * BAND[0], outer * BAND[1]
    lip = inner + 0.0012
    outline = [(lip, back_y), (lip, face_y), (r1, face_y), (r2, face_y), (outer * 0.95, face_y), (outer, face_y + (back_y - face_y) * 0.25), (outer, back_y)]
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
    gun_verts = [v.co for v in bm.verts if owner[v.index] == "cannon"]
    gun_back = max(p.y for p in gun_verts) if gun_verts else cut_y
    gun_front = min(p.y for p in gun_verts) if gun_verts else front
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

    scene.frame_set(1)
    print("DRONECORE %s%%: cube %.2f m, whole model %s; %d triangles; lens radius %.1f mm, glass %.1f mm; cannon %.0f mm long at x %.3f z %.3f; "
          "vertices per bone %s; edges not shared by exactly two faces: %d; clips %s"
          % (stage, cfg["size"], tuple(round(d, 3) for d in ob.dimensions), tris, outer * 1000, inner * 1000, gun_len * 1000, gun_x, gun_z,
             counts, open_edges, sorted(x.name for x in bpy.data.actions)))


main()
