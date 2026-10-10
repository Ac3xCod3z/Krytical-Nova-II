"""Tidecaller: the Tripo egg, with a new face that can act, a rig, and its animations.

  run_blender.py --blend SM_Pet_Tidecaller_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/Tidecaller/rig_tidecaller.py

The shell is made again as a clean, regular mesh of the artist's egg: same shape (measured on the scan's clean back
half), same colours (each face takes the scan's texture colour at that spot). The eyes it came with, two pieces
that were its painted eyes, could not be made to act cleanly, so they are taken out and the eyes are built again
from clean parts, in the same sockets:

  eyeballs  a ball in each socket: amber, with a dark slit pupil. A bone turns it, so the eyes look anywhere.
  eyelids   an upper and a lower lid for each eye: shells that fit round the ball, in the colour the old rim had.
            A bone turns each one over the ball, the way a lid slides. At rest the upper lid sits low and slanted,
            which is the egg's glare. Shut: a blink. Lower lids up: the arched, smiling eye. Opened right up: shock.
  sockets   the shell round each eye is cut out and rebuilt clean: an almond opening on the eyeball, with a raised
            rim that is deep above the eye, which is the brow.
  eyebrows  the upper part of each new rim follows a bone, so the brows tip (angrier, softer) and rise or drop.
  anger     the red cross-shaped vein mark, top right of the forehead; tiny and sunk under the shell until its
            bone blows it up.

Every part is a closed surface; the finished mesh has no open edges.

Bones: root; top (the upper end of the shell: moved down or up it squashes or stretches the egg, with no scaling);
face (carries the eyes and brows with the shell as it squashes); eyeball.L/R; lid_up.L/R; lid_low.L/R; brow.L/R; anger.

Actions, 24 frames a second. Idle and Move loop; MoveStart starts on Idle's first frame and ends on Move's first
frame, MoveStop the other way round; everything else starts and ends on the rest pose, which is Idle's first frame.
  Idle 192 (loop; the blinks and the looking about are in it)   HopSpin 36
  MoveStart 18   Move 32 (loop)   MoveStop 24
  WobbleNap 150   Tantrum 88   TideCall 96   VictoryBounce 76   CrackCry 140
There are no plain face clips: the faces (shut, wide, glare with the red mark, smiling arches, crying) are acted inside
the clips above.

Every run starts again from the untouched model, kept in the file.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

NAME = "SM_Pet_Tidecaller_S1"
RAW = "angry egg creature 3d model"
# The socket frame, in each eye's own plane (metres): half the opening's length, how far it opens above and below its
# middle line, the slant of that line, how far the frame stands clear of the eyeball, how high its crest is above
# the eye and below it, how much deeper the frame is above the eye (the brow), and the size of the piece of shell cut out.
RIM = {"long": 0.040, "up": 0.011, "down": 0.022, "slant": 22.0, "gap": 0.0026, "high": (0.009, 0.0006), "brow": 0.022, "low_grow": 0.4, "soften": 3,
       "cut": 1.95, "skirt": 1.62, "sunk": 0.012}
LUMP = {"from": 0.004, "most": 0.008,       # the shell may stand this far off the egg untouched, and at the very most this far
        "above": -0.06, "reach": 0.095, "smooth": 3}    # the ridge over each eye that is smoothed, and how many times
SOCKET = {"reach": 0.066, "warm": 0.05, "dark": 0.8, "smooth": 6}     # how far round each eye the shell's lip is cleaned, and how little amber counts
EYE = {"radius": 0.046, "proud": 0.005,        # the ball's size, and how far its front stands in front of where the old eye's did
       "pixels": 256, "pupil": (0.60, -0.30, 0.15, -0.30),   # the wedge, in shares of the ball's radius: how high its broad
                                                # end is, how low its point comes, half its width at the top, how far outward (inward if below 0: the eyes face
                                                # out to the sides, so a pupil in the middle of the ball looks wall-eyed from the front)
       "amber_edge": 0.72}                      # how much darker the amber is at the edge of the ball than in the middle
LID = {"gap": 0.0008, "thick": 0.0008,
       "up_reach": 86.0, "up_open": 15.0, "up_slant": 22.0,     # upper lid: how far round it reaches; where its edge rests,
       "low_reach": 60.0, "low_open": 52.0, "low_slant": -4.0}   # degrees above (below) the middle of the eye; the edge's slant
# The brow ridge on the shell: how far above the eye its middle is, its slant, and how far along it, across it and
# into the shell the brow bone's hold reaches (metres, to where the hold has faded to nothing).
BROW = {"up": 0.024, "slant": 22.0, "long": 0.058, "wide": 0.020, "deep": 0.030}
# The smiling arch: how wide, how high its middle stands above its ends, how thick the stroke is, how far below the
# middle of the eye its ends sit, and how small it is kept while hidden.
SMILE = {"wide": 0.062, "high": 0.017, "thick": 0.0105, "drop": -0.015, "flat": 0.03}
TEAR = {"under": 0.034, "size": 0.0065, "flat": 0.03}      # how far under the eye's middle a tear starts, and its size
# The crack: its lines as seen from the front (x across, z up, metres), the black line's width, the colour of its
# chipped edge, and how it is hidden.
CRACK = {"lines": (((-0.030, 0.386), (-0.052, 0.368), (-0.040, 0.352), (-0.066, 0.334), (-0.054, 0.318), (-0.080, 0.298)),
                   ((-0.040, 0.352), (-0.022, 0.340), (-0.028, 0.322))),
         "wide": 0.0095, "pale": (0.50, 0.62, 0.68), "flat": 0.02, "sunk": 0.004}
MARK = {"x": 0.082, "z": 0.325, "size": 0.030, "flat": 0.02, "sunk": 0.004}
ROLL_RADIUS = 0.163         # how high the egg's middle is when it lies on its side
ROCK_RADIUS = 0.14          # how round the end it stands on is: the radius it rocks on


def components(faces):
    left, out = set(faces), []
    while left:
        stack = [left.pop()]
        group = [stack[0]]
        while stack:
            cur = stack.pop()
            for e in cur.edges:
                for n in e.link_faces:
                    if n in left:
                        left.discard(n)
                        stack.append(n)
                        group.append(n)
        out.append(group)
    return out


def main():
    scene = bpy.context.scene
    scene.render.fps = 24

    # ---- start again from the untouched model
    ob = bpy.data.objects.get(NAME)
    keep = bpy.data.objects.get(NAME + "_source")
    if keep is None:
        keep = bpy.data.objects[RAW]
        keep.name = NAME + "_source"
        keep.data.name = NAME + "_source"
    for o in list(bpy.data.objects):
        if o.type == "ARMATURE" and o.name == NAME + "_Rig":
            bpy.data.objects.remove(o, do_unlink=True)
    if ob is not None:
        old = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        if old.users == 0:
            bpy.data.meshes.remove(old)
    ob = keep.copy()
    ob.data = keep.data.copy()
    ob.name = NAME
    ob.data.name = NAME
    scene.collection.objects.link(ob)
    keep.hide_set(True)
    keep.hide_render = True
    ob.hide_set(False)
    ob.hide_render = False
    me = ob.data
    world = ob.matrix_world.copy()
    for v in me.vertices:
        v.co = world @ v.co
    ob.matrix_world.identity()

    def picture_of(mat):
        return next((n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None) if mat and mat.node_tree else None

    # the model's own skin and picture (an earlier version of this script kept an untouched copy of the picture)
    skin = next(m for m in keep.data.materials if picture_of(m) is not None)
    image = bpy.data.images.get(NAME + "_texture_source") or picture_of(skin)
    next(n for n in skin.node_tree.nodes if n.type == "TEX_IMAGE" and n.image).image = image
    me.materials.clear()
    me.materials.append(skin)
    w, h = image.size
    px = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)

    def material(name, colour=None, glow=0.0, picture=None, rough=0.8):
        mat = bpy.data.materials.get(NAME + " " + name)
        if mat is not None:
            bpy.data.materials.remove(mat)
        mat = bpy.data.materials.new(NAME + " " + name)
        mat.use_nodes = True
        shader = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        shader.inputs["Roughness"].default_value = rough
        if picture is not None:
            node = mat.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = picture
            mat.node_tree.links.new(node.outputs["Color"], shader.inputs["Base Color"])
        else:
            mat.diffuse_color = (*colour, 1)
            shader.inputs["Base Color"].default_value = (*colour, 1)
            shader.inputs["Emission Color"].default_value = (*colour, 1)
            shader.inputs["Emission Strength"].default_value = glow
            mat.roughness = 1.0
            mat.specular_intensity = 0.0
        me.materials.append(mat)
        return len(me.materials) - 1

    group_names = ["root", "top", "anger", "crack"] + [f"{b}.{s}" for s in "LR" for b in ("eyeball", "lid_up", "lid_low", "brow", "tear", "smile")]
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    gi = {n: ob.vertex_groups.new(name=n).index for n in group_names}

    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.active
    dl = bm.verts.layers.deform.verify()
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    parts = sorted(components(bm.faces), key=len, reverse=True)

    def sample(face, a=1 / 3, b=1 / 3):
        uvs = [l[uv].uv for l in face.loops]
        p = uvs[0] * a + uvs[1] * b + uvs[2] * (1 - a - b)
        return px[int(p.y % 1 * (h - 1)), int(p.x % 1 * (w - 1)), :3]

    # ---- what the old face tells us before it goes: where each eye was, which way it faced, and its colours
    old_piece = next(p for p in parts[1:3] if sum(f.calc_center_median().x for f in p) > 0)       # the egg's left eye
    amber = [f for f in old_piece if all(sample(f, a, b)[0] - sample(f, a, b)[2] > 0.25 for a, b in ((1 / 3, 1 / 3), (0.6, 0.2), (0.2, 0.6), (0.2, 0.2)))]
    area = sum(f.calc_area() for f in amber)
    old_n = sum((f.normal * f.calc_area() for f in amber), Vector()).normalized()
    old_c = sum((f.calc_center_median() * f.calc_area() for f in amber), Vector()) / area
    front = max((v.co - old_c).dot(old_n) for f in amber for v in f.verts)
    amber_colour = np.median(np.array([sample(f) for f in amber]), axis=0)
    rim = [f for f in old_piece if sample(f)[0] - sample(f)[2] < 0.05]
    rim.sort(key=lambda f: float(sample(f).mean()))
    rim_face = rim[int(len(rim) * 0.7)]                                  # a lighter stretch of the old rim: the lids' and brows' colour
    rim_uv = sum((l[uv].uv for l in rim_face.loops), Vector((0, 0))) / 3

    for part in parts[1:]:                                               # the old face comes off
        bmesh.ops.delete(bm, geom=[f for f in part if f.is_valid], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    # ---- the shell: its small holes closed, and weighted so that its top can be pushed down or pulled up
    # (the scan also has a few places where three or four faces meet along one edge; the faces there are taken
    # out and the gap closed like any other hole, until every edge has exactly two faces)
    for _ in range(4):
        tangled = {f for e in bm.edges if len(e.link_faces) > 2 for f in e.link_faces}
        if tangled:
            bmesh.ops.delete(bm, geom=list(tangled), context="FACES")
        holes = [e for e in bm.edges if len(e.link_faces) == 1]
        if not holes and not tangled:
            break
        before = set(bm.faces)
        bmesh.ops.holes_fill(bm, edges=holes, sides=0)
        left = [e for e in bm.edges if len(e.link_faces) == 1]
        if left:
            bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=left)
        for f in [f for f in bm.faces if f not in before]:
            near = next((x for e in f.edges for x in e.link_faces if x in before), None)
            for l in f.loops:
                l[uv].uv = near.loops[0][uv].uv if near is not None else rim_uv
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # ---- the inside edge of each socket. The shell's own lip round each eye was painted with the old eye's amber
    #      and its shadow, in ragged patches; every face of the shell near an eye that carries any of that takes the
    #      plain colour of the rim instead, so each socket has one clean edge.
    cleaned = 0
    for sign in (1.0, -1.0):
        eye_at = Vector((sign * old_c.x, old_c.y, old_c.z))
        near_eye = [f for f in bm.faces if (f.calc_center_median() - eye_at).length < SOCKET["reach"] * 1.4]

        def warmth(f):
            return max(float(sample(f, a_, b_)[0] - sample(f, a_, b_)[2])
                       for a_, b_ in ((1 / 3, 1 / 3), (0.7, 0.15), (0.15, 0.7), (0.15, 0.15), (0.45, 0.45), (0.45, 0.1), (0.1, 0.45)))

        cool = [f for f in near_eye if warmth(f) <= SOCKET["warm"]]
        usual = sorted(float(sample(f).mean()) for f in cool)[len(cool) // 2]
        # plain shell: no amber on it, and not the dark of the old eye's shadow either (the faces that close the hole
        # behind the eye picked that dark up, and showed as dark spikes inside the socket)
        plain = [f for f in cool if float(sample(f).mean()) > usual * SOCKET["dark"]]
        lip = set()
        spots = [(f.calc_center_median(), sum((l[uv].uv for l in f.loops), Vector((0, 0))) / 3) for f in plain]
        for f in near_eye:
            if f in plain or (f.calc_center_median() - eye_at).length > SOCKET["reach"]:
                continue
            here = f.calc_center_median()
            take = min(spots, key=lambda sp: (sp[0] - here).length)[1]      # the shell's own colour, from right beside it
            for l in f.loops:
                l[uv].uv = take
            cleaned += 1
            lip.add(f)
        # the inside of the socket is ragged where the old eye was cut out: its vertices are evened out
        inner = [v for v in {v for f in lip if f.is_valid for v in f.verts} if all(x in lip for x in v.link_faces)]
        for _ in range(SOCKET["smooth"]):
            new = {v: sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges) for v in inner}
            for v, co in new.items():
                v.co = v.co * 0.4 + co * 0.6
        if sign > 0:                                         # the lids take the shell's colour round the eye too
            plain.sort(key=lambda f: float(sample(f).mean()))
            mid_face = plain[len(plain) // 2]
            rim_uv = sum((l[uv].uv for l in mid_face.loops), Vector((0, 0))) / 3

    # ---- lumps. The scan left a horn sticking out at the outer end of each brow and bumps on the ridge over each eye.
    #      The egg is round about its upright axis, so its back half says how wide it should be at each height: what
    #      stands further off that than a brow should is pressed back, and the ridge over each eye is smoothed.
    height_guess = max(v.co.z for v in bm.verts) + 1e-6
    back = sorted((v.co.z, math.hypot(v.co.x, v.co.y)) for v in bm.verts if v.co.y > 0.02)
    table = []
    for k in range(40):
        zs = [rr for z_, rr in back[len(back) * k // 40: len(back) * (k + 1) // 40]]
        zz = [z_ for z_, rr in back[len(back) * k // 40: len(back) * (k + 1) // 40]]
        table.append((sum(zz) / len(zz), sum(zs) / len(zs)))

    def egg_radius(z):
        for (z0, r0), (z1, r1) in zip(table[:-1], table[1:]):
            if z0 <= z <= z1:
                return r0 + (r1 - r0) * (z - z0) / max(z1 - z0, 1e-9)
        return table[0][1] if z < table[0][0] else table[-1][1]

    # the egg is not quite round seen from above (it is deeper front to back than side to side), so the width it
    # should have is taken from the back half at the same height AND the same angle off the front-to-back line
    grid = {}
    for v in bm.verts:
        if v.co.y > 0.0:
            key_ = (int(v.co.z / height_guess * 24), int(math.degrees(math.atan2(abs(v.co.x), abs(v.co.y))) / 11.25))
            grid.setdefault(key_, []).append(math.hypot(v.co.x, v.co.y))
    grid = {k_: sum(vals) / len(vals) for k_, vals in grid.items()}

    def egg_r(co):
        zi, ai = int(co.z / height_guess * 24), min(7, int(math.degrees(math.atan2(abs(co.x), abs(co.y))) / 11.25))
        for dz, da in ((0, 0), (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            found = grid.get((zi + dz, ai + da))
            if found is not None:
                return found
        return egg_radius(co.z)

    pressed = 0
    for v in bm.verts:
        if v.co.y > -0.02 or not (old_c.z - 0.03 < v.co.z < old_c.z + 0.10):
            continue
        r_now = math.hypot(v.co.x, v.co.y)
        off = r_now - egg_r(v.co)
        if off > LUMP["from"]:
            room = LUMP["most"] - LUMP["from"]
            want = egg_r(v.co) + LUMP["from"] + room * math.tanh((off - LUMP["from"]) / room)
            v.co.x *= want / r_now
            v.co.y *= want / r_now
            pressed += 1
    for sign in (1.0, -1.0):
        eye_at = Vector((sign * old_c.x, old_c.y, old_c.z))
        ridge = [v for v in bm.verts if v.co.z > eye_at.z + LUMP["above"] and (v.co - eye_at).length < LUMP["reach"] and v.co.y < 0]
        for _ in range(LUMP["smooth"]):
            new = {v: sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges) for v in ridge if v.link_edges}
            for v, co in new.items():
                v.co = v.co * 0.5 + co * 0.5
    # ---- a clean shell. Patching the scan's shell round the eyes left it ragged every time, so the shell is made
    #      again as a regular mesh of the same egg: its shape is measured on the scan's back half, which is clean
    #      (ring by ring, direction by direction), and used for the front as well; each face takes the colour the
    #      scan's texture has at that spot.
    bm.faces.ensure_lookup_table()
    scan = BVHTree.FromBMesh(bm)
    scan_faces = list(bm.faces)
    scan_uv = [sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops) for f in scan_faces]
    z_top = max(v.co.z for v in bm.verts)
    z_low = min(v.co.z for v in bm.verts)
    RINGS, AROUND = 30, 44
    layout = []                                             # per ring: (z, [(radius, uv) per direction])
    for k in range(1, RINGS):
        z = z_low + (z_top - z_low) * (1 - math.cos(math.pi * k / RINGS)) / 2
        row = []
        for j in range(AROUND):
            ang = 2 * math.pi * j / AROUND
            hit, _n, index, dist = scan.ray_cast(Vector((0, 0, z)), Vector((math.sin(ang), abs(math.cos(ang)), 0)))
            row.append((dist if hit is not None else 0.05, scan_uv[index] if index is not None else rim_uv))
        layout.append((z, row))
    for _ in range(2):                                      # the measured widths evened out a little along each ring
        for _z, row in layout:
            radii = [x for x, _uv in row]
            for j in range(AROUND):
                row[j] = ((radii[j - 1] + 2 * radii[j] + radii[(j + 1) % AROUND]) / 4, row[j][1])
    bmesh.ops.delete(bm, geom=list(bm.verts), context="VERTS")
    grid_v = [[bm.verts.new((rad * math.sin(2 * math.pi * j / AROUND), -rad * math.cos(2 * math.pi * j / AROUND), z))
               for j, (rad, _uv) in enumerate(row)] for z, row in layout]
    south, north = bm.verts.new((0, 0, z_low)), bm.verts.new((0, 0, z_top))
    for j in range(AROUND):
        j2 = (j + 1) % AROUND
        for f, tint in ((bm.faces.new((south, grid_v[0][j2], grid_v[0][j])), layout[0][1][j][1]),
                        (bm.faces.new((north, grid_v[-1][j], grid_v[-1][j2])), layout[-1][1][j][1])):
            for l in f.loops:
                l[uv].uv = tint
        for k in range(len(grid_v) - 1):
            f = bm.faces.new((grid_v[k][j], grid_v[k][j2], grid_v[k + 1][j2], grid_v[k + 1][j]))
            for l in f.loops:
                l[uv].uv = layout[k][1][j][1]
    # one even colour and smooth shading: a face-by-face copy of the scan's texture made the egg look like blocks
    for f in bm.faces:
        f.smooth = True
        for l in f.loops:
            l[uv].uv = rim_uv
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # ---- new sockets, as on the reference picture. Round each eye a piece of the clean shell is cut out and a frame
    #      is built into the gap: an almond-shaped opening that sits on the eyeball, a raised rim round it that is deep
    #      above the eye (the brow) and thin below, and a foot joined vertex to vertex to the cut edge of the shell.
    eye_centre = {}
    eye_ball = {}
    frames = 0
    egg = BVHTree.FromBMesh(bm)
    for sign in (1.0, -1.0):
        n = Vector((sign * old_n.x, old_n.y, old_n.z))
        c = Vector((sign * old_c.x, old_c.y, old_c.z))
        u = (Vector((0, 0, 1)) - n * n.z).normalized()
        r = u.cross(n).normalized()
        if r.x * sign < 0:
            r = -r
        ball = EYE["radius"]
        lean = math.radians(RIM["slant"])
        e1 = (r * math.cos(lean) + u * math.sin(lean)).normalized()      # along the eye, towards its outer corner and up
        e2 = n.cross(e1).normalized()
        if e2.dot(u) < 0:
            e2 = -e2
        hit = egg.ray_cast(c + n * 0.3, -n)[0]
        # the eyeball and its lids must stay inside the egg everywhere but in the socket: the ball is made no bigger
        # than the room there is for it
        for _ in range(6):
            centre = (hit if hit is not None else c) + n * (EYE["proud"] - ball)
            room = min((v.co - centre).length for v in bm.verts
                       if math.hypot((v.co - c).dot(e1), (v.co - c).dot(e2)) > RIM["long"] * 1.25)
            if room >= ball + 0.004:
                break
            ball = room - 0.0045
        eye_centre[sign] = centre.copy()
        eye_ball[sign] = ball
        depth0 = (centre - c).dot(n)

        def coords(co):
            d = co - c
            return d.dot(e1), d.dot(e2), d.dot(n)

        def on_ball(x, y):
            room = (ball + RIM["gap"]) ** 2 - x * x - y * y
            return depth0 + math.sqrt(room) if room > 0 else -1.0

        def on_shell(x, y):
            found = egg.ray_cast(c + e1 * x + e2 * y + n * 0.3, -n)[0]
            if found is None:                                 # past the edge of the egg as seen from the eye: the nearest
                found = egg.find_nearest(c + e1 * x + e2 * y)[0]   # point of the shell, or the rim would stick out into the air
            return (found - c).dot(n) if found is not None else 0.0

        def outline(t, grow, more_up):
            """A point of the eye's outline, made bigger by `grow`. Above the eye the extra height `more_up` comes in
            quickly and then stays level, so the brow is a plate with a flat top and steep ends; below the eye the
            outline grows less, so the lower rim stays close in."""
            up_side = math.sin(t) > 0
            rise = max(0.0, min(1.0, math.sin(t) * 2.2))
            rise = rise * rise * (3 - 2 * rise)               # eased, so nothing steps at the corners of the eye
            narrow = 1.0 + (grow - 1.0) * RIM["low_grow"]
            widen = narrow + (grow - narrow) * rise
            x = RIM["long"] * math.cos(t) * widen
            y = (RIM["up"] if up_side else RIM["down"]) * math.sin(t) * widen + more_up * rise
            return x, y

        count = 32
        edge_of_cut = [outline(2 * math.pi * k / count, RIM["cut"], RIM["brow"] * 1.6) for k in range(count)]

        def in_cut(co):
            x, y, z = coords(co)
            if z < -0.10:
                return False
            inside = False
            for (x0, y0), (x1, y1) in zip(edge_of_cut, edge_of_cut[1:] + edge_of_cut[:1]):
                if (y0 > y) != (y1 > y) and x < x0 + (x1 - x0) * (y - y0) / (y1 - y0):
                    inside = not inside
            return inside

        # every face that reaches into the cut at all goes, or a corner of one is left lying over the eye
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if in_cut(f.calc_center_median()) or any(in_cut(v.co) for v in f.verts)],
                         context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        cut_verts = list({v for e in bm.edges if len(e.link_faces) == 1 for v in e.verts
                          if coords(v.co)[2] > -0.11 and (v.co - c).length < 0.13})

        lip, crest, foot = [], [], []
        for k in range(count):
            t = 2 * math.pi * k / count
            x, y = outline(t, 1.0, 0.0)
            lip.append(bm.verts.new(c + e1 * x + e2 * y + n * max(on_ball(x, y), on_shell(x, y) - RIM["sunk"])))
            x, y = outline(t, 1.2, RIM["brow"] * 0.92)
            lift_ = max(0.0, min(1.0, math.sin(t) * 1.6))
            high = RIM["high"][1] + (RIM["high"][0] - RIM["high"][1]) * lift_ * lift_ * (3 - 2 * lift_)
            crest.append(bm.verts.new(c + e1 * x + e2 * y + n * (max(on_shell(x, y), on_ball(x, y)) + high)))
            x, y = outline(t, 1.34, RIM["brow"] * 1.12)
            where_ = c + e1 * x + e2 * y + n * max(on_shell(x, y), on_ball(x, y) + 0.0005)
            snug = egg.find_nearest(where_)[0]                # set down exactly on the shell, so the join has no crease
            if snug is not None and (snug - where_).length < 0.02 and on_ball(x, y) + 0.0005 < on_shell(x, y):
                where_ = snug
            foot.append(bm.verts.new(where_))
        made = []
        for k in range(count):
            j = (k + 1) % count
            made.append(bm.faces.new((lip[k], lip[j], crest[j], crest[k])))
            made.append(bm.faces.new((crest[k], crest[j], foot[j], foot[k])))

        def angle(v):
            x, y, _z = coords(v.co)
            return math.atan2(y, x) % (2 * math.pi)

        # a skirt: one more ring, a little further out and lying on the shell like the foot, so that the uneven join
        # to the cut edge of the shell is flat on the egg and away from the rim, where it left a crease
        skirt = []
        for k in range(count):
            x, y = outline(2 * math.pi * k / count, RIM["skirt"], RIM["brow"] * 1.12 + 0.005)
            where_ = c + e1 * x + e2 * y + n * on_shell(x, y)
            snug = egg.find_nearest(where_)[0]
            skirt.append(bm.verts.new(snug if snug is not None else where_))
        for k in range(count):
            j2 = (k + 1) % count
            made.append(bm.faces.new((foot[k], foot[j2], skirt[j2], skirt[k])))
        ring_a, ring_b = sorted(skirt, key=angle), sorted(cut_verts, key=angle)
        ang_a = [angle(v) for v in ring_a] + [angle(ring_a[0]) + 2 * math.pi]
        ang_b = [angle(v) for v in ring_b] + [angle(ring_b[0]) + 2 * math.pi]
        i = j = 0
        while i < len(ring_a) or j < len(ring_b):
            a0, b0 = ring_a[i % len(ring_a)], ring_b[j % len(ring_b)]
            next_a = ang_a[i + 1] if i < len(ring_a) else 99.0
            next_b = ang_b[j + 1] if j < len(ring_b) else 99.0
            if next_a <= next_b:
                tri = (a0, ring_a[(i + 1) % len(ring_a)], b0)
                i += 1
            else:
                tri = (a0, ring_b[(j + 1) % len(ring_b)], b0)
                j += 1
            if len(set(tri)) == 3 and bm.faces.get(tri) is None:
                made.append(bm.faces.new(tri))
        # the brow is rounded off, as with a smoothing brush: each point of the rim's crest and foot is drawn towards
        # the points either side of it, a few times over
        for ring in (crest, foot):
            for _ in range(RIM["soften"]):
                was = [v.co.copy() for v in ring]
                for k, v in enumerate(ring):
                    v.co = was[k] * 0.5 + (was[k - 1] + was[(k + 1) % count]) * 0.25
        # behind the lip the socket has a wall straight down onto the eyeball, and is closed off inside the ball
        def straight_down(v):
            """The point of the eyeball's surface straight behind a point of the lip, seen from in front of the eye."""
            x, y, _z = coords(v.co)
            room = (ball - 0.003) ** 2 - x * x - y * y
            if room > 0:
                return c + e1 * x + e2 * y + n * (depth0 + math.sqrt(room))
            return centre + (v.co - centre).normalized() * (ball - 0.003)

        floor = [bm.verts.new(straight_down(v)) for v in lip]
        inside_ball = bm.verts.new(centre)
        for k in range(count):
            j = (k + 1) % count
            made.append(bm.faces.new((lip[j], lip[k], floor[k], floor[j])))
            made.append(bm.faces.new((inside_ball, floor[j], floor[k])))
        for f in made:
            f.smooth = True
            for l in f.loops:
                l[uv].uv = rim_uv
        frames += 1
    # anything the joins left open is closed
    for _ in range(3):
        tangled = {f for e in bm.edges if len(e.link_faces) > 2 for f in e.link_faces}
        if tangled:
            bmesh.ops.delete(bm, geom=list(tangled), context="FACES")
        gaps = [e for e in bm.edges if len(e.link_faces) == 1]
        if not gaps:
            break
        had = set(bm.faces)
        bmesh.ops.holes_fill(bm, edges=gaps, sides=0)
        gaps = [e for e in bm.edges if len(e.link_faces) == 1]
        if gaps:
            bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=gaps)
        for f in [f for f in bm.faces if f not in had]:
            f.smooth = True
            for l in f.loops:
                l[uv].uv = rim_uv
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    height = max(v.co.z for v in bm.verts)
    for v in bm.verts:
        up = max(0.0, min(1.0, v.co.z / height))
        v[dl][gi["top"]] = up
        v[dl][gi["root"]] = 1.0 - up
    shell_tree = BVHTree.FromBMesh(bm)
    shell_verts = list(bm.verts)
    brow_count = {}

    # ---- the eyeballs' picture: amber, darker towards the edge, with the slit pupil; one picture for both eyes
    size = EYE["pixels"]
    grid = (np.arange(size) + 0.5 - size / 2) / (size / 2)
    gx, gy = np.meshgrid(grid, grid)
    away = np.sqrt(np.clip(gx * gx + gy * gy, 0, 1))
    tile = np.ones((size, size, 4), dtype=np.float32)
    tile[:, :, :3] = amber_colour * (1.0 - (1.0 - EYE["amber_edge"]) * away[:, :, None] ** 2.2)
    # the pupil, as the egg's own eyes had it: a wedge, broad where it goes up under the lid and coming down to a
    # point that stops well short of the bottom of the eye, set a little towards the outer corner
    top, point, broad, shift = EYE["pupil"]
    t = np.clip((gy - point) / (top - point), 0.0, 1.0)
    inside = (gy > point) & (gy < 0.95)
    slit = inside & (np.abs(gx - shift) < broad * t ** 0.85)
    soft = inside & (np.abs(gx - shift) < broad * t ** 0.85 + 0.02)
    tile[soft, :3] = tile[soft, :3] * 0.5
    tile[slit, :3] = (0.035, 0.035, 0.045)
    eye_image = bpy.data.images.get(NAME + "_eye")
    if eye_image is not None:
        bpy.data.images.remove(eye_image)
    eye_image = bpy.data.images.new(NAME + "_eye", size, size, alpha=False)
    eye_image.pixels.foreach_set(tile.ravel())
    eye_image.pack()
    eye_slot = material("Eye", picture=eye_image, rough=0.3)

    def paint(faces, at_uv):
        for f in faces:
            f.smooth = True
            for l in f.loops:
                l[uv].uv = at_uv

    def cap(centre, pole, e1, e2, radius, reach, rings, around=18):
        """Vertices of a part of a ball round `pole`, out to `reach` degrees: the pole first, then ring by ring."""
        verts = [bm.verts.new(centre + pole * radius)]
        for a_ in range(1, rings + 1):
            polar = math.radians(reach) * a_ / rings
            for k in range(around):
                turn = 2 * math.pi * k / around
                verts.append(bm.verts.new(centre + (pole * math.cos(polar) + (e1 * math.cos(turn) + e2 * math.sin(turn)) * math.sin(polar)) * radius))
        return verts

    def skin_cap(verts, rings, around=18):
        faces = []
        ring = lambda a_: verts[1 + (a_ - 1) * around: 1 + a_ * around]
        first = ring(1)
        for k in range(around):
            faces.append(bm.faces.new((verts[0], first[k], first[(k + 1) % around])))
            for a_ in range(1, rings):
                lo, hi = ring(a_), ring(a_ + 1)
                faces.append(bm.faces.new((lo[k], hi[k], hi[(k + 1) % around], lo[(k + 1) % around])))
        return faces

    eyes = {}
    tear_slot = None
    smile_slot = None
    for side, sign in (("L", 1.0), ("R", -1.0)):
        n = Vector((sign * old_n.x, old_n.y, old_n.z))                    # the right eye is the left one mirrored
        c = Vector((sign * old_c.x, old_c.y, old_c.z))
        u = (Vector((0, 0, 1)) - n * n.z).normalized()
        r = u.cross(n).normalized()
        if r.x * sign < 0:
            r = -r                                                       # towards the outer corner of the eye
        radius = eye_ball[sign]
        centre = eye_centre[sign]

        # the eyeball
        around = 18
        ball = cap(centre, n, r, u, radius, 162.0, 9, around)
        south = bm.verts.new(centre - n * radius)
        faces = skin_cap(ball, 9, around)
        last = ball[1 + 8 * around: 1 + 9 * around]
        for k in range(around):
            faces.append(bm.faces.new((south, last[(k + 1) % around], last[k])))
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        for f in faces:
            f.material_index = eye_slot
            f.smooth = True
            for l in f.loops:
                d = (l.vert.co - centre).normalized()
                x, y = d.dot(r), d.dot(u)                      # the picture's right is the eye's outer corner, on both eyes
                if d.dot(n) < 0.1:
                    length = math.hypot(x, y) or 1.0
                    x, y = x / length * 0.995, y / length * 0.995
                l[uv].uv = (x * 0.5 + 0.5, y * 0.5 + 0.5)
        for v in ball + [south]:
            v[dl][gi["eyeball." + side]] = 1.0

        # the lids: each a thin shell round part of the ball, turning about the ball's middle
        lid_axes = {}
        for which, reach, opened, slant, upper in (("lid_up", LID["up_reach"], LID["up_open"], LID["up_slant"], True),
                                                    ("lid_low", LID["low_reach"], LID["low_open"], LID["low_slant"], False)):
            edge = (r * math.cos(math.radians(slant)) + u * math.sin(math.radians(slant))).normalized()    # along the lid's edge, rising outwards
            lift = n.cross(edge).normalized()
            if lift.dot(u) < 0:
                lift = -lift
            if not upper:
                lift = -lift                                              # the lower lid's own side is down
            tip = math.radians(reach + opened)
            pole = (n * math.cos(tip) + lift * math.sin(tip)).normalized()
            e1 = edge
            e2 = pole.cross(e1).normalized()
            outer = cap(centre, pole, e1, e2, radius + LID["gap"] + LID["thick"], reach, 7, around)
            inner = cap(centre, pole, e1, e2, radius + LID["gap"], reach, 7, around)
            made = skin_cap(outer, 7, around) + skin_cap(inner, 7, around)
            rim_o, rim_i = outer[1 + 6 * around:], inner[1 + 6 * around:]
            for k in range(around):
                made.append(bm.faces.new((rim_o[k], rim_i[k], rim_i[(k + 1) % around], rim_o[(k + 1) % around])))
            bmesh.ops.recalc_face_normals(bm, faces=made)
            paint(made, rim_uv)
            for v in outer + inner:
                v[dl][gi[f"{which}.{side}"]] = 1.0
            probe = Quaternion(edge, 0.1) @ pole
            lid_axes[which] = edge * (1.0 if probe.dot(n) > pole.dot(n) else -1.0)       # turning about this shuts the lid

        # the brow: a tapered bar lying on the shell above the eye, inner end low
        slant = math.radians(BROW["slant"])
        along = (r * math.cos(slant) + u * math.sin(slant)).normalized()
        middle = centre + n * radius + u * BROW["up"]
        hit, nrm, _i, _d = shell_tree.ray_cast(middle + n * 0.2, -n)
        if hit is None:
            hit, nrm = middle, n
        if nrm.dot(n) < 0:
            nrm = -nrm
        across = nrm.cross(along).normalized()
        # the egg's own brow ridge is part of its shell: the shell round it is handed over to the brow bone, fully
        # at the ridge and less and less further off, so the bone moves the real brow
        moved = 0
        for v in shell_verts:
            off = v.co - hit
            far = math.sqrt((off.dot(along) / BROW["long"]) ** 2 + (off.dot(across) / BROW["wide"]) ** 2 + (off.dot(nrm) / BROW["deep"]) ** 2)
            if far >= 1.0:
                continue
            share = (1.0 - far) ** 2 * (3.0 - 2.0 * (1.0 - far))
            for g in (gi["top"], gi["root"]):
                if g in v[dl]:
                    v[dl][g] *= 1.0 - share
            v[dl][gi["brow." + side]] = share
            moved += 1
        brow_count[side] = moved
        eyes[side] = {"centre": centre.copy(), "n": n.copy(), "u": u.copy(), "lids": lid_axes,
                      "brow": hit.copy(), "brow_n": nrm.copy(), "brow_up": (u - nrm * u.dot(nrm)).normalized(), "sign": sign}

        # a tear: a drop under the eye, tiny and sunk under the shell until its bone brings it out and runs it down
        if tear_slot is None:
            tear_slot = material("Tear", (0.35, 0.75, 1.0), glow=0.6)
        t_hit, t_n, _i, _d = shell_tree.ray_cast(centre + n * 0.25 - u * TEAR["under"], -n)
        if t_hit is None:
            t_hit, t_n = centre + n * radius - u * TEAR["under"], n
        if t_n.dot(n) < 0:
            t_n = -t_n
        t_up = (Vector((0, 0, 1)) - t_n * t_n.z).normalized()
        t_side = t_n.cross(t_up).normalized()
        big, tall_ = TEAR["size"], TEAR["size"] * 2.3
        drop_mid = t_hit + t_n * big * 0.55
        rows = [(tall_, 0.0), (tall_ * 0.45, big * 0.55), (0.0, big), (-big * 0.62, big * 0.72), (-big, 0.0)]
        rings_ = []
        for along_, wide_ in rows:
            if wide_ == 0.0:
                rings_.append([bm.verts.new(drop_mid + t_up * along_)])
            else:
                rings_.append([bm.verts.new(drop_mid + t_up * along_ + (t_side * math.cos(2 * math.pi * k / 8)
                                                                      + t_n * math.sin(2 * math.pi * k / 8) * 0.7) * wide_)
                               for k in range(8)])
        drop_faces = []
        for a_, b_ in zip(rings_[:-1], rings_[1:]):
            for k in range(8):
                k2 = (k + 1) % 8
                if len(a_) == 1:
                    drop_faces.append(bm.faces.new((a_[0], b_[k], b_[k2])))
                elif len(b_) == 1:
                    drop_faces.append(bm.faces.new((b_[0], a_[k2], a_[k])))
                else:
                    drop_faces.append(bm.faces.new((a_[k], b_[k], b_[k2], a_[k2])))
        bmesh.ops.recalc_face_normals(bm, faces=drop_faces)
        t_anchor = t_hit - t_n * 0.004
        for f in drop_faces:
            f.material_index = tear_slot
            f.smooth = True
        for v in [v for ring in rings_ for v in ring]:
            v.co = t_anchor + (v.co - t_anchor) * TEAR["flat"]
            v[dl][gi["tear." + side]] = 1.0
        eyes[side]["tear"] = (t_anchor.copy(), t_n.copy(), (-t_up).copy())

        # the smiling eye: a dark arch, high in the middle, that lies on the closed lid. Tiny and inside the eyeball
        # until its bone brings it out. With the lids shut it is the closed, happy eye of a cartoon face.
        if smile_slot is None:
            smile_slot = material("Smile", (0.004, 0.004, 0.006))      # black, as asked: the drawn arch of a cartoon smile
        s_anchor = centre + n * (radius - 0.008)
        shell_r = radius + LID["gap"] + LID["thick"] + 0.0022

        def on_lid(x, y, out=0.0):
            d = (n * math.sqrt(max(1.0 - (x * x + y * y) / (shell_r * shell_r), 0.05)) + r * (x / shell_r) + u * (y / shell_r)).normalized()
            return centre + d * (shell_r + out)

        half, rise, thick = SMILE["wide"] / 2, SMILE["high"], SMILE["thick"]
        steps_ = 10
        upper_edge, lower_edge = [], []
        for k in range(steps_ + 1):
            x = -half + SMILE["wide"] * k / steps_
            taper = 1.0 - 0.55 * abs(x / half) ** 2                       # the stroke thins towards its ends
            y = SMILE["drop"] + rise * (1.0 - (x / half) ** 2)
            upper_edge.append((x, y + thick * taper / 2))
            lower_edge.append((x, y - thick * taper / 2))
        ring2 = upper_edge + lower_edge[::-1]
        front = [bm.verts.new(on_lid(x, y, 0.0016)) for x, y in ring2]
        back_ = [bm.verts.new(on_lid(x, y, 0.0)) for x, y in ring2]
        smile_faces = []
        count2 = len(ring2)
        for k in range(steps_):                                           # front and back, strip by strip
            a_, b_, c_, d_ = k, k + 1, count2 - 2 - k, count2 - 1 - k
            smile_faces.append(bm.faces.new((front[a_], front[b_], front[c_], front[d_])))
            smile_faces.append(bm.faces.new((back_[d_], back_[c_], back_[b_], back_[a_])))
        for k in range(count2):                                           # the edge all round
            k2 = (k + 1) % count2
            smile_faces.append(bm.faces.new((front[k], front[k2], back_[k2], back_[k])))
        bmesh.ops.recalc_face_normals(bm, faces=smile_faces)
        for f in smile_faces:
            f.material_index = smile_slot
            f.smooth = False
        for v in front + back_:
            v.co = s_anchor + (v.co - s_anchor) * SMILE["flat"]
            v[dl][gi["smile." + side]] = 1.0
        eyes[side]["smile"] = s_anchor.copy()

    # ---- the anger mark: four red strokes, tiny and under the shell until wanted
    red = material("Anger", (0.9, 0.03, 0.03), glow=1.5)
    hit, hit_n, _i, _d = shell_tree.ray_cast(Vector((MARK["x"], -1.0, MARK["z"])), Vector((0, 1, 0)))
    if hit_n.y > 0:
        hit_n = -hit_n
    a1 = (Vector((0, 0, 1)) - hit_n * hit_n.z).normalized()
    a2 = hit_n.cross(a1).normalized()
    anchor = hit - hit_n * MARK["sunk"]
    size = MARK["size"]
    mark_verts = []
    for quarter in range(4):
        diag = math.radians(45 + 90 * quarter)
        centre2 = Vector((math.cos(diag), math.sin(diag))) * size * 1.12
        outline = []
        for radius in (size * 0.74, size * 0.52):
            arc = []
            for k in range(6):
                ang = diag + math.pi + math.radians(-52 + 104 * k / 5)
                arc.append(centre2 + Vector((math.cos(ang), math.sin(ang))) * radius)
            outline.append(arc)
        loop2 = outline[0] + outline[1][::-1]
        top = [bm.verts.new(hit + a2 * p.x + a1 * p.y + hit_n * 0.0016) for p in loop2]
        bottom = [bm.verts.new(hit + a2 * p.x + a1 * p.y - hit_n * 0.0012) for p in loop2]
        count = len(loop2)
        made = [bm.faces.new(top), bm.faces.new(bottom[::-1])]
        for i in range(count):
            made.append(bm.faces.new((top[i], top[(i + 1) % count], bottom[(i + 1) % count], bottom[i])))
        bmesh.ops.recalc_face_normals(bm, faces=made)
        for f in made:
            f.material_index = red
        mark_verts += top + bottom
    for v in mark_verts:
        v.co = anchor + (v.co - anchor) * MARK["flat"]
        v[dl][gi["anger"]] = 1.0

    # ---- the crack: a jagged black line with a pale chipped edge, down the forehead on the side away from the anger
    # mark; like the mark it is tiny and under the shell until its bone brings it out
    crack_slots = (material("Crack Edge", CRACK["pale"]), material("Crack", (0.004, 0.004, 0.006)))
    crack_verts = []
    crack_hit, crack_n = None, None
    for line in CRACK["lines"]:
        on_shell_, normals_ = [], []
        for x, z in line:
            at, at_n, _i, _d = shell_tree.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
            if at_n.y > 0:
                at_n = -at_n
            on_shell_.append(at)
            normals_.append(at_n)
        if crack_hit is None:
            crack_hit, crack_n = on_shell_[len(on_shell_) // 2].copy(), normals_[len(on_shell_) // 2].copy()
        last_ = len(on_shell_) - 1
        for layer, (wide_, high_) in enumerate(((CRACK["wide"] * 2.1, 0.0010), (CRACK["wide"], 0.0019))):
            rows = []
            for i, (at, at_n) in enumerate(zip(on_shell_, normals_)):
                along_ = (on_shell_[min(i + 1, last_)] - on_shell_[max(i - 1, 0)]).normalized()
                across_ = at_n.cross(along_).normalized()
                half = wide_ / 2 * (1.0 - 0.8 * i / last_)      # broad where it starts, a point at its end
                rows.append([bm.verts.new(at + across_ * (half * side_) + at_n * up_)
                             for side_, up_ in ((-1, high_), (1, high_), (1, -0.0012), (-1, -0.0012))])
            made = [bm.faces.new(rows[0]), bm.faces.new(rows[-1][::-1])]
            for a_, b_ in zip(rows[:-1], rows[1:]):
                for k in range(4):
                    k2 = (k + 1) % 4
                    made.append(bm.faces.new((a_[k], b_[k], b_[k2], a_[k2])))
            bmesh.ops.recalc_face_normals(bm, faces=made)
            for f in made:
                f.material_index = crack_slots[layer]
                f.smooth = False
            crack_verts += [v for row in rows for v in row]
    crack_anchor = crack_hit - crack_n * CRACK["sunk"]
    for v in crack_verts:
        v.co = crack_anchor + (v.co - crack_anchor) * CRACK["flat"]
        v[dl][gi["crack"]] = 1.0

    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    for e in bm.edges:
        if len(e.link_faces) != 2:
            mid_ = (e.verts[0].co + e.verts[1].co) / 2
            print("TIDECALLER odd edge: %d faces at (%.3f, %.3f, %.3f)" % (len(e.link_faces), *mid_))
    open_left = sum(1 for e in bm.edges if len(e.link_faces) != 2)
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
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]

    eye_z = eyes["L"]["centre"].z
    bone("root", (0, 0, 0), (0, 0.06, 0))
    bone("top", (0, 0, height * 0.6), (0, 0, height), parent="root")
    bone("face", (0, -0.10, eye_z), (0, -0.10, eye_z + 0.04), parent="root")
    bone("anger", anchor, anchor + hit_n * 0.02, parent="top")
    bone("crack", crack_anchor, crack_anchor + crack_n * 0.02, parent="top")
    for s in "LR":
        e = eyes[s]
        bone(f"eyeball.{s}", e["centre"], e["centre"] + e["n"] * EYE["radius"], parent="face")
        bone(f"lid_up.{s}", e["centre"], e["centre"] + e["n"] * EYE["radius"] * 0.7, parent="face")
        bone(f"lid_low.{s}", e["centre"], e["centre"] + e["n"] * EYE["radius"] * 0.5, parent="face")
        bone(f"brow.{s}", e["brow"], e["brow"] + e["brow_n"] * 0.02, parent="face")
        bone(f"tear.{s}", e["tear"][0], e["tear"][0] + e["tear"][1] * 0.015, parent="face")
        bone(f"smile.{s}", e["smile"], e["smile"] + e["n"] * 0.012, parent="face")
    bpy.ops.object.mode_set(mode="OBJECT")
    ob.parent = rig
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- animation helpers. Turns and moves are given in the egg's own directions (it faces -Y, z is up).
    pb = rig.pose.bones
    for b in pb:
        b.rotation_mode = "QUATERNION"
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    MARK_FULL = 1.0 / MARK["flat"]
    SHUT = LID["up_open"] + LID["low_open"] + 3.0          # degrees that take the upper lid's edge down to the lower lid's
    # the lower lid, as shares of SHUT: raised until its edge is a little above the middle of the eye (the happy arch),
    # just showing over the lower rim (the mad glare), and somewhat more than that (sad, heavy eyes)
    HAPPY_LID = (LID["low_open"] + 7.0) / SHUT
    MAD_LID = (LID["low_open"] - 16.0) / SHUT
    SAD_LID = (LID["low_open"] - 10.0) / SHUT
    last = {}

    def about(axis, degrees):
        return Quaternion(axis, math.radians(degrees))

    def key(name, frame, q=None, move=None, scale=None):
        b = pb[name]
        rest = arm.bones[name].matrix_local.to_3x3()
        if q is not None:
            local = Quaternion(rest.inverted() @ Vector(q.axis), q.angle) if abs(q.angle) > 1e-7 else Quaternion()
            if name in last and last[name].dot(local) < 0:
                local.negate()
            last[name] = local.copy()
            b.rotation_quaternion = local
            b.keyframe_insert("rotation_quaternion", frame=frame)
        if move is not None:
            b.location = rest.inverted() @ Vector(move)
            b.keyframe_insert("location", frame=frame)
        if scale is not None:
            b.scale = scale
            b.keyframe_insert("scale", frame=frame)

    def start(name):
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        last.clear()
        return action

    def finish(action, end):
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, end
        rig.animation_data.action = None
        for b in pb:
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def squash(frame, dz):
        """The top of the egg moved down (-) or up (+) by dz metres; the face goes with the shell."""
        key("top", frame, move=(0, 0, dz))
        key("face", frame, move=(0, 0, dz * eye_z / height))

    def look(frame, right=0.0, up=0.0):
        """Both eyes: degrees towards the egg's own right (+) or left (-), up (+) or down (-)."""
        for s in "LR":
            key(f"eyeball.{s}", frame, about(Z, right) @ about(X, up))

    def lids(frame, upper=0.0, lower=0.0):
        """upper: 0 as it rests, 1 shut, below 0 opened wider. lower: 0 as it rests, 1 raised right up, below 0 dropped."""
        for s in "LR":
            key(f"lid_up.{s}", frame, about(eyes[s]["lids"]["lid_up"], SHUT * upper))
            key(f"lid_low.{s}", frame, about(eyes[s]["lids"]["lid_low"], SHUT * lower))

    def brows(frame, frown=0.0, lift=0.0):
        """frown: degrees each brow is tipped, inner end down (+, angrier) or up (-, softer). lift: metres raised (+) or dropped."""
        for s in "LR":
            e = eyes[s]
            key(f"brow.{s}", frame, about(e["brow_n"], -e["sign"] * frown), move=e["brow_up"] * lift)

    def anger(frame, amount):
        k = 1.0 + (MARK_FULL - 1.0) * amount
        key("anger", frame, scale=(k, k, k))

    def blink(at, hold=1):
        for frame, shut in ((at, 0.0), (at + 2, 1.0), (at + 2 + hold, 1.0), (at + 5 + hold, 0.0)):
            lids(frame, upper=shut)

    def tears(frame, size=0.0, run=0.0, only=None):
        """size: 0 hidden .. 1 a full drop. run: metres it has run down the shell."""
        k = 1.0 + (1.0 / TEAR["flat"] - 1.0) * size
        for s in (only or "LR"):
            _anchor, out_, down_ = eyes[s]["tear"]
            key(f"tear.{s}", frame, move=down_ * run + out_ * (0.004 * min(1.0, size * 3) + run * 0.10), scale=(k, k, k))

    def smile(frame, amount=0.0):
        k = 1.0 + (1.0 / SMILE["flat"] - 1.0) * amount
        for s in "LR":
            key(f"smile.{s}", frame, scale=(k, k, k))

    def crack(frame, amount=0.0):
        k = 1.0 + (1.0 / CRACK["flat"] - 1.0) * amount
        key("crack", frame, scale=(k, k, k))

    def face_rest(*frames):
        for frame in frames:
            crack(frame)
            lids(frame)
            look(frame)
            brows(frame)
            anger(frame, 0.0)
            tears(frame)
            smile(frame)

    def body_rest(*frames):
        for frame in frames:
            key("root", frame, Quaternion(), move=(0, 0, 0))
            squash(frame, 0.0)

    # the plain face clips are no longer kept: their faces are acted inside the other clips
    for gone in ("Blink", "LookAround", "Happy", "Shock", "Mad", "Sad"):
        old = bpy.data.actions.get(gone)
        if old:
            bpy.data.actions.remove(old)

    # ---- Idle: it breathes and sways, glances right and left, looks up, down and once right round, and blinks
    a = start("Idle")
    LOOP = 192
    for frame in range(1, LOOP + 2, 4):
        squash(frame, 0.004 * math.sin(2 * math.pi * (frame - 1) / 64))
        key("root", frame, about(Y, 1.5 * math.sin(2 * math.pi * (frame - 1) / 96)), move=(0, 0, 0))
    face_rest(1, LOOP + 1)
    for frame, right, up in ((1, 0, 0), (30, 0, 0), (36, 18, 1), (52, 18, 1), (60, -18, 0), (76, -18, 0), (84, 0, 9), (92, 0, 9), (100, 0, -11), (106, 0, -11)):
        look(frame, right, up)
    for k in range(1, 13):
        ang = -math.pi / 2 + 2 * math.pi * k / 12
        look(106 + k * 1.5, 18 * math.cos(ang), 11 * math.sin(ang))
    look(131, 0, 0)
    look(LOOP + 1, 0, 0)
    for frame, wide in ((78, 0.0), (84, -0.25), (92, -0.25), (98, 0.0)):       # looking up, the upper lids rise with it
        lids(frame, upper=wide)
    blink(16)
    blink(56, hold=2)
    blink(136)
    blink(168)
    blink(174)
    finish(a, LOOP + 1)

    # ---- HopSpin: crouch, hop, one quick turn in the air, land where it stood
    a = start("HopSpin")
    face_rest(1, 36)
    #        frame  height  squash   turn
    hop = [(1, 0.0, 0.0, 0), (6, 0.0, -0.045, 0), (9, 0.02, 0.030, 20), (13, 0.15, 0.015, 150), (16, 0.19, 0.0, 215),
           (19, 0.15, 0.010, 285), (23, 0.02, 0.025, 350), (25, 0.0, -0.050, 360), (29, 0.0, 0.014, 360), (33, 0.0, -0.004, 360), (36, 0.0, 0.0, 360)]
    for frame, lift, dz, turn in hop:
        key("root", frame, about(Z, turn), move=(0, 0, lift))
        squash(frame, dz)
    for frame, s in ((1, 0.0), (9, 1.0), (22, 1.0), (26, 0.0)):
        brows(frame, frown=-12 * s, lift=0.008 * s)
        lids(frame, upper=-0.4 * s)
    blink(27)
    finish(a, 36)

    # ---- The movement set. Moving, the egg lies on its side and rolls. MoveStart tips it over (from Idle's first
    # frame to Move's first frame); MoveStop is the leap back up (from Move's first frame to Idle's first frame).
    middle = Vector((0, 0, height * 0.42))                          # the point of the egg that stays over its spot

    def lie(frame, tipped, rolled=0.0, lift=0.0, yaw=0.0):
        """tipped: 0 upright .. 1 lying on its side. rolled: degrees it has rolled forward."""
        q = about(Z, yaw) @ about(X, rolled) @ about(Y, 90.0 * tipped)
        target = Vector((0, 0, middle.z + (ROLL_RADIUS - middle.z) * tipped + lift))
        key("root", frame, q, move=target - q @ middle)

    ROLL = 32
    a = start("Move")
    for frame in range(1, ROLL + 2):
        turn = 360.0 * (frame - 1) / ROLL
        wobble = math.sin(math.radians(turn))
        lie(frame, 1.0, rolled=turn, lift=0.004 * abs(math.sin(math.radians(turn * 2))), yaw=5.0 * wobble)
        squash(frame, 0.0)
    face_rest(1, ROLL + 1)
    blink(12)
    finish(a, ROLL + 1)

    a = start("MoveStart")
    face_rest(1, 18)
    for frame, tipped, lift, dz in ((1, 0.0, 0.0, 0.0), (4, 0.0, 0.0, -0.030), (7, 0.12, 0.035, 0.020), (10, 0.55, 0.060, 0.008),
                                    (13, 0.95, 0.020, 0.0), (15, 1.0, 0.0, -0.010), (18, 1.0, 0.0, 0.0)):
        lie(frame, tipped, lift=lift)
        squash(frame, dz)
    for frame, s in ((1, 0.0), (6, 1.0), (13, 1.0), (18, 0.0)):
        lids(frame, upper=-0.4 * s)
        brows(frame, lift=0.006 * s)
    finish(a, 18)

    a = start("MoveStop")
    face_rest(1, 24)
    for frame, tipped, lift, dz in ((1, 1.0, 0.0, 0.0), (4, 1.0, 0.0, -0.012), (8, 0.7, 0.090, 0.015), (12, 0.25, 0.130, 0.010),
                                    (15, 0.03, 0.050, 0.020), (17, 0.0, 0.0, -0.045), (20, 0.0, 0.0, 0.012), (24, 0.0, 0.0, 0.0)):
        lie(frame, tipped, lift=lift)
        squash(frame, dz)
    blink(16)
    finish(a, 24)

    # ---- Four acted clips. Standing, the egg rocks on its rounded end the way a tumbler toy does: the middle of
    # that roundness stays at one height and moves sideways as the egg tips, so the shell neither sinks into the
    # ground nor slides on it.
    round_end = Vector((0, 0, ROCK_RADIUS))

    def rock(frame, side=0.0, forward=0.0, lift=0.0, turn=0.0, across=0.0, size=None):
        """side: degrees tipped to the egg's left (+x) or right (-). forward: degrees tipped forward (+) or back (-).
        lift: metres off the ground. turn: degrees turned about its own height. across: metres moved sideways."""
        q = about(Z, turn) @ about(Y, side) @ about(X, forward)
        rolled = Vector((ROCK_RADIUS * math.radians(side) + across, -ROCK_RADIUS * math.radians(forward), lift))
        key("root", frame, q, move=round_end + rolled - q @ round_end, scale=None if size is None else (size, size, size))

    # ---- WobbleNap: it nods off, sleeps, and rocks further and further over until it nearly topples; it starts awake
    # with a jump, wide-eyed, rocks itself still and blinks. For long idles.
    a = start("WobbleNap")
    END = 150
    face_rest(1, END)
    body_rest(1, END)
    for frame, shut in ((1, 0.0), (8, 0.55), (13, 0.25), (22, 0.75), (26, 0.5), (34, 1.0), (104, 1.0)):      # nodding off
        lids(frame, upper=shut)
    for frame, soft in ((1, 0.0), (34, 1.0), (104, 1.0)):
        brows(frame, frown=-14 * soft, lift=-0.002 * soft)
    for frame, right, up in ((1, 0, 0), (22, 0, -8), (34, 0, -10), (104, 0, -10), (108, 0, 4)):
        look(frame, right, up)
    #           frame  side  forward
    for frame, side, forward in ((1, 0, 0), (10, 0, 5), (14, 0, 1), (24, 0, 7), (34, 3, 5), (46, 9, 4), (58, -11, 4), (70, 15, 3),
                                 (82, -19, 3), (96, 26, 2), (104, 33, 1)):
        rock(frame, side, forward)
    for frame in range(34, 105, 10):                                       # breathing, asleep
        squash(frame, -0.006 + 0.006 * math.sin(2 * math.pi * (frame - 34) / 40))
    # the start
    for frame, wide in ((106, -0.9), (124, -0.9), (132, 0.0)):
        lids(frame, upper=wide, lower=-0.3 * (wide / -0.9))
        brows(frame, frown=-26 * (wide / -0.9), lift=0.022 * (wide / -0.9))
    for frame, side, lift, dz in ((106, 30, 0.0, -0.020), (109, 14, 0.050, 0.022), (112, -4, 0.062, 0.010), (115, -9, 0.020, 0.012),
                                  (117, -10, 0.0, -0.040), (122, 7, 0.0, 0.012), (128, -4, 0.0, -0.004), (134, 2, 0.0, 0.0), (140, 0, 0.0, 0.0)):
        rock(frame, side, 0.0, lift)
        squash(frame, dz)
    look(124, 0, 4)
    look(132, 0, 0)
    blink(136)
    blink(142)
    finish(a, END)

    # ---- Tantrum: the glare and the red mark, then three hard stamps on the spot, each one turned a little the other
    # way; a huff (it swells, shuts its eyes, turns its face away) and it settles
    a = start("Tantrum")
    END = 88
    face_rest(1, END)
    body_rest(1, END)
    for frame, m in ((1, 0.0), (6, 1.1), (9, 1.0), (56, 1.0), (60, 0.7), (72, 0.7), (80, 0.0), (END, 0.0)):
        brows(frame, frown=10 * m, lift=-0.005 * m)
        look(frame, 0, -3 * m)
    for frame, m in ((1, 0.0), (6, 1.1), (9, 1.0), (56, 1.0), (61, 0.0)):
        lids(frame, upper=0.12 * m, lower=MAD_LID * m)
    for frame, amount in ((1, 0.0), (5, 0.0), (9, 1.3), (13, 0.95), (22, 1.3), (27, 0.95), (36, 1.3), (41, 0.95), (50, 1.35), (56, 1.0),
                          (70, 0.9), (78, 0.0), (END, 0.0)):
        anger(frame, amount)                                                # the mark jumps with each stamp
    stamp, was = 10, 0.0
    for i in range(3):
        f0 = stamp + i * 14
        now = 14.0 if i % 2 == 0 else -14.0
        for frame, lift, dz, forward, turn in ((f0, 0.0, -0.034, 4, 0.0), (f0 + 3, 0.035, 0.022, 10, 0.4), (f0 + 6, 0.085, 0.010, 13, 0.8),
                                               (f0 + 9, 0.050, 0.004, 10, 1.0), (f0 + 11, 0.0, -0.058, 5, 1.0), (f0 + 13, 0.0, 0.010, 3, 1.0)):
            rock(frame, 0.0, forward, lift, turn=was + (now - was) * turn)
            squash(frame, dz)
        was = now
    # the huff
    for frame, dz, forward, turn in ((54, -0.010, 3, 8.0), (60, 0.026, -6, -22.0), (64, 0.030, -7, -32.0), (70, -0.012, 2, -32.0),
                                     (76, 0.0, 0, -18.0), (82, 0.0, 0, 0.0), (END, 0.0, 0, 0.0)):
        rock(frame, 0.0, forward, 0.0, turn=turn)
        squash(frame, dz)
    for frame, shut in ((61, 0.0), (64, 1.0), (72, 1.0), (77, 0.0), (END, 0.0)):
        lids(frame, upper=shut)
    finish(a, END)

    # ---- TideCall: its namesake. It leans back with its eyes shut and swells, swaying; then it throws itself forward,
    # eyes wide, and beats outward twice, like a wave going out. For a cast or a buff.
    a = start("TideCall")
    END = 96
    face_rest(1, END)
    body_rest(1, END)
    for frame, shut in ((1, 0.0), (6, 1.0), (38, 1.0), (41, -0.9), (50, -0.6), (57, -0.9), (70, -0.6), (78, 0.0), (END, 0.0)):
        lids(frame, upper=shut, lower=-0.3 if shut < 0 else 0.0)
    for frame, frown, lift in ((1, 0, 0.0), (8, 6, -0.003), (38, 6, -0.003), (41, -20, 0.018), (70, -16, 0.014), (80, 0, 0.0), (END, 0, 0.0)):
        brows(frame, frown=frown, lift=lift)
    #           frame  side  forward  lift    stretch   size
    call = [(1, 0, 0, 0.0, 0.0, 1.0), (6, 0, 3, 0.0, -0.016, 1.0), (14, 4, -6, 0.0, 0.006, 1.02), (22, -4, -10, 0.0, 0.016, 1.05),
            (30, 4, -13, 0.0, 0.024, 1.08), (37, 0, -15, 0.0, 0.030, 1.10),
            (40, 0, 6, 0.030, 0.034, 1.20), (44, 0, 4, 0.0, -0.030, 1.04), (50, 0, -6, 0.0, 0.018, 1.08),       # the first beat
            (56, 0, 6, 0.036, 0.036, 1.24), (60, 0, 4, 0.0, -0.034, 1.04), (66, 0, 0, 0.0, 0.014, 1.03),        # the second
            (72, 0, 0, 0.0, -0.006, 1.0), (80, 0, 0, 0.0, 0.0, 1.0), (END, 0, 0, 0.0, 0.0, 1.0)]
    for frame, side, forward, lift, dz, size in call:
        rock(frame, side, forward, lift, size=size)
        squash(frame, dz)
    for frame, up in ((1, 0), (38, 0), (41, 6), (70, 4), (80, 0)):
        look(frame, 0, up)
    blink(84)
    finish(a, END)

    # ---- VictoryBounce: the smiling eyes, a bounce to one side and a bounce to the other, then a high one with a full
    # turn, landing where it stood. For a pickup, a level gained, a duel won.
    a = start("VictoryBounce")
    END = 76
    face_rest(1, END)
    body_rest(1, END)
    for frame, joy in ((1, 0.0), (4, 0.0), (8, 1.0), (58, 1.0), (63, 0.0), (END, 0.0)):
        lids(frame, upper=1.0 * joy)
        brows(frame, frown=-16 * joy, lift=0.005 * joy)
    for frame, amount in ((1, 0.0), (6, 0.0), (9, 1.15), (12, 1.0), (58, 1.0), (61, 0.0), (END, 0.0)):
        smile(frame, amount)
    #             frame  across  lift    side  turn  stretch
    bounce = [(1, 0.0, 0.0, 0, 0, 0.0), (5, 0.0, 0.0, 0, 0, -0.034),
              (8, 0.012, 0.045, 7, 0, 0.022), (12, 0.035, 0.105, 12, 0, 0.006), (16, 0.055, 0.045, 8, 0, 0.012), (18, 0.060, 0.0, 4, 0, -0.044),
              (21, 0.050, 0.035, -3, 0, 0.022), (26, 0.0, 0.115, -12, 0, 0.006), (31, -0.050, 0.040, -9, 0, 0.012), (33, -0.060, 0.0, -4, 0, -0.046),
              (36, -0.050, 0.050, 0, 30, 0.026), (40, -0.030, 0.150, 0, 140, 0.010), (43, -0.015, 0.185, 0, 215, 0.0), (46, -0.005, 0.150, 0, 290, 0.008),
              (50, 0.0, 0.030, 0, 352, 0.022), (52, 0.0, 0.0, 0, 360, -0.052), (56, 0.0, 0.0, 0, 360, 0.016), (60, 0.0, 0.0, 0, 360, -0.005),
              (64, 0.0, 0.0, 0, 360, 0.0), (END, 0.0, 0.0, 0, 360, 0.0)]
    for frame, across, lift, side, turn, dz in bounce:
        rock(frame, side, 0.0, lift, turn=turn, across=across)
        squash(frame, dz)
    blink(66)
    finish(a, END)

    # ---- CrackCry: it loses its balance and falls on its side; the shell cracks, and it lies there crying. Then it
    # gathers itself, leaps back upright, and the crack closes.
    a = start("CrackCry")
    END = 140
    face_rest(1, END)
    #            frame  tipped  lift   stretch
    for frame, tipped, lift, dz in ((1, 0.0, 0.0, 0.0), (5, -0.05, 0.0, 0.0), (10, 0.10, 0.0, 0.004), (14, 0.45, 0.0, 0.006), (18, 1.0, 0.0, -0.012),
                                    (21, 0.94, 0.022, 0.006), (24, 1.0, 0.0, -0.006), (28, 1.0, 0.0, 0.0),
                                    (88, 1.0, 0.0, 0.0), (92, 1.0, 0.0, -0.014), (96, 0.7, 0.090, 0.015), (100, 0.25, 0.130, 0.010),
                                    (103, 0.03, 0.050, 0.020), (105, 0.0, 0.0, -0.045), (109, 0.0, 0.0, 0.014), (116, 0.0, 0.0, 0.020),
                                    (126, 0.0, 0.0, -0.004), (132, 0.0, 0.0, 0.0), (END, 0.0, 0.0, 0.0)):
        lie(frame, tipped, lift=lift)
        squash(frame, dz)
    for i, frame in enumerate(range(32, 85, 4)):                             # sobbing where it lies
        lie(frame, 1.0, rolled=3.0 if i % 2 else -3.0)
        squash(frame, -0.006 if i % 2 else 0.003)
    # the face: alarm as it goes over, then the crying face, then itself again
    for frame, wide in ((1, 0.0), (7, 1.0), (20, 1.0), (27, 0.0)):
        lids(frame, upper=-0.9 * wide, lower=-0.3 * wide)
        brows(frame, frown=-26 * wide, lift=0.020 * wide)
    for frame, sad in ((27, 0.0), (34, 1.0), (92, 1.0), (104, 0.0), (END, 0.0)):
        brows(frame, frown=-26 * sad, lift=0.003 * sad)
        lids(frame, upper=0.20 * sad, lower=SAD_LID * sad)
        look(frame, 0, -9 * sad)
    falls = Vector((1, 0, 0))                                                # lying on its side, down is the egg's own left
    for begin in (36, 62):
        for s_ in "LR":
            _anchor, out_, _down = eyes[s_]["tear"]
            for frame, size, run in ((begin, 0.0, 0.0), (begin + 6, 1.0, 0.0), (begin + 10, 1.0, 0.004), (begin + 20, 0.9, 0.040),
                                     (begin + 24, 0.0, 0.050), (begin + 25, 0.0, 0.0)):
                k = 1.0 + (1.0 / TEAR["flat"] - 1.0) * size
                key(f"tear.{s_}", frame, move=falls * run + out_ * (0.004 * min(1.0, size * 3) + run * 0.35), scale=(k, k, k))
    # the crack: there with the bump, gone a little after it is back on its feet
    for frame, amount in ((1, 0.0), (17, 0.0), (19, 1.15), (22, 1.0), (110, 1.0), (128, 0.0), (END, 0.0)):
        crack(frame, amount)
    blink(130)
    finish(a, END)

    scene.frame_set(1)
    print("TIDECALLER rigged: %d bones; triangles %d; shell vertices on the brow bones %s; socket faces cleaned %d; edges that are not shared by exactly two faces: %d"
          % (len(arm.bones), len(me.polygons), brow_count, cleaned, open_left))


main()
