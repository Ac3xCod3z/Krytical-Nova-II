"""Tidecaller, the later stages (25 %, 50 %, 75 %, 100 %): the Tripo model cleaned, given new eyes, and rigged.

  run_blender.py --blend SM_Pet_Tidecaller_25.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/Tidecaller/build_tidecaller_stage.py

One script for all four; which stage it is building is read from the untouched model kept in the file
("Tidecaller25%_source" and so on). These stages keep the look they came with: the artist's mesh and texture.

  size      scaled to the stage's height, standing on the ground
  hidden    whole layers of the scan that cannot be seen from outside are removed
  eyes      the scan's own eyes are taken out (separate painted balls on three stages, faces of the shell on the
            100 % one) and a clean ball is set in each socket, the size and place of the old one. On the 25, 50 and
            75 % stages the ball is amber with a black slit pupil. The 100 % stage keeps the eyes it came with (they
            are faces of its shell): they only get an eyelid, and they are written to the emission map so they glow.
            A bone turns each ball. An eyelid, hidden up under the brow, comes down over the ball on its own bone.
  closed    every open edge is closed; the finished mesh has none
  texture   2048 x 2048. On the stages with light blue lines those lines are also written to a second picture, the
            emission map, so that their glow can be set on its own in Unity.
  rig       root, body, eyeball.L/R, lid.L/R, and a bone for each fin the script finds (fin.L/R, tail.L/R)
  clips     24 frames a second. Idle and Move loop; the others start and end on the first frame of Idle.
            Idle 144       a slow swim on the spot: a small bob, the fins paddling together as a turtle does, blinks,
                           and (not on the 100 % stage) the eyes looking about
            Move 48        swimming for real: nose down, faster and deeper strokes, a surge and a small roll
            TideCall 124   bows with eyes shut, rears up with fins wide and trembles, beats down twice, swelling
            SwimFlip 100   one hard stroke upward into a backward somersault, brakes, bobs back into place
            Ram 84         draws back, drives forward head down and butts, rebounds, shakes its head, swims back

Every run starts again from the untouched model.
"""
import colorsys
import math

import bmesh
import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

# Per stage: height in metres; whether the eyes have a pupil; whether the eyes glow; whether there is an emission map.
STAGES = {
    "25": {"height": 0.45, "pupil": True, "eye_glow": False, "emission": False},
    "50": {"height": 0.50, "pupil": True, "eye_glow": False, "emission": True},
    "75": {"height": 0.55, "pupil": True, "eye_glow": False, "emission": True},
    "100": {"height": 0.60, "pupil": False, "eye_glow": True, "emission": True, "keep_eyes": True},
}
TEXTURE = 2048
LID = {"gap": 0.002, "thick": 0.002, "margin": 7.0, "corners": 10.0, "pad": 6.0, "flat": 0.05}       # shares of the height; degrees
FIN = {"from": 1.10, "full": 0.28}          # how far outside the body's own roundness a fin starts, and where it is all fin


def is_amber(r, g, b):
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    return 0.07 < h < 0.18 and s > 0.5 and v > 0.45


def to_linear(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def components(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, part = [f], []
        seen.add(f)
        while stack:
            g = stack.pop()
            part.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen.add(h)
                        stack.append(h)
        out.append(part)
    return sorted(out, key=len, reverse=True)


def sphere_through(points):
    a = np.array([[2 * p.x, 2 * p.y, 2 * p.z, 1.0] for p in points])
    b = np.array([p.length_squared for p in points])
    x, _res, _rank, _sv = np.linalg.lstsq(a, b, rcond=None)
    centre = Vector(x[:3])
    return centre, math.sqrt(max(x[3] + centre.length_squared, 1e-12))


def main():
    source = next(o for o in bpy.data.objects if o.name.endswith("%_source"))
    stage = source.name.replace("Tidecaller", "").replace("%_source", "")
    cfg = STAGES[stage]
    NAME = f"SM_Pet_Tidecaller_{stage}"
    scene = bpy.context.scene

    # ---- start again from the untouched model
    for o in list(bpy.data.objects):
        if o is not source:
            bpy.data.objects.remove(o)
    for action in list(bpy.data.actions):                  # the scan came out of a scene that held other pets' clips
        bpy.data.actions.remove(action)
    for block in (bpy.data.meshes, bpy.data.armatures):
        for item in list(block):
            if item.users == 0:
                block.remove(item)
    for mat in list(bpy.data.materials):
        if mat.name.startswith(NAME):
            bpy.data.materials.remove(mat)
    for img in list(bpy.data.images):
        if img.name.startswith(NAME):
            bpy.data.images.remove(img)
    source.hide_viewport = source.hide_render = True
    source.use_fake_user = True

    me = bpy.data.meshes.new(NAME)
    ob = bpy.data.objects.new(NAME, me)
    scene.collection.objects.link(ob)
    bm = bmesh.new()
    bm.from_mesh(source.data)
    uv = bm.loops.layers.uv.verify()

    # ---- the picture, at working size
    old_image = next(n.image for s in source.material_slots for n in s.material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    work = old_image.copy()
    work.scale(TEXTURE, TEXTURE)
    px = np.empty(TEXTURE * TEXTURE * 4, dtype=np.float32)
    work.pixels.foreach_get(px)
    px = px.reshape(TEXTURE, TEXTURE, 4)
    bpy.data.images.remove(work)

    def colour_at(u):
        return px[min(max(int(u.y * TEXTURE), 0), TEXTURE - 1), min(max(int(u.x * TEXTURE), 0), TEXTURE - 1), :3]

    def face_uv(f):
        return sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)

    # ---- size: the stage's height, on the ground
    low = min(v.co.z for v in bm.verts)
    grow = cfg["height"] / (max(v.co.z for v in bm.verts) - low)
    for v in bm.verts:
        v.co = Vector((v.co.x, v.co.y, v.co.z - low)) * grow
    H = cfg["height"]

    # ---- what can be seen from outside at all
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    seen = set()
    middle = Vector((0, 0, H * 0.5))
    step = H / 45
    for i in range(48):
        z = 1 - 2 * (i + 0.5) / 48
        ring = math.sqrt(1 - z * z)
        d = Vector((ring * math.cos(i * 2.39996), ring * math.sin(i * 2.39996), z))
        a1 = d.orthogonal().normalized()
        a2 = d.cross(a1)
        for i1 in range(-34, 35):
            for i2 in range(-34, 35):
                index = tree.ray_cast(middle + d * 3 + a1 * (i1 * step) + a2 * (i2 * step), -d)[2]
                if index is not None:
                    seen.add(index)

    eyes_old = {}          # side -> (centre, radius, towards the iris, pupil direction or None, pupil's long axis or None)
    removed_layers = 0
    for part in components(bm):
        area = sum(f.calc_area() for f in part)
        shown = sum(f.calc_area() for f in part if f.index in seen) / max(area, 1e-12)
        amber = [f for f in part if is_amber(*colour_at(face_uv(f)))]
        if len(part) > 60 and len(amber) >= 8 and len(part) < 0.3 * len(bm.faces):
            # a painted eyeball of the scan's own
            centre, radius = sphere_through([v.co for v in {v for f in part for v in f.verts}])
            towards = sum(((f.calc_center_median() - centre).normalized() * f.calc_area() for f in amber), Vector()).normalized()
            spread = sorted(towards.angle(f.calc_center_median() - centre) for f in amber)[int(len(amber) * 0.9)]
            dark = [f for f in part if f not in amber and max(colour_at(face_uv(f))) < 0.30
                    and towards.angle(f.calc_center_median() - centre) < spread * 0.75]
            pupil, axis = None, None
            if len(dark) >= 3:
                dirs = [(f.calc_center_median() - centre).normalized() for f in dark]
                pupil = sum(dirs, Vector()).normalized()
                spreads = np.array([list(d - pupil) for d in dirs])
                _u, _s, vt = np.linalg.svd(spreads - spreads.mean(0))
                axis = Vector(vt[0])
                axis = (axis - pupil * axis.dot(pupil)).normalized()
                if axis.z < 0:
                    axis = -axis
            eyes_old["L" if centre.x > 0 else "R"] = (centre, radius, towards, pupil, axis, spread)
            bmesh.ops.delete(bm, geom=part, context="FACES")
        elif shown < 0.03:
            removed_layers += len(part)
            bmesh.ops.delete(bm, geom=part, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    keep_eyes = cfg.get("keep_eyes", False)       # the eyes stay as they are: they only get an eyelid, and they glow
    kept = {}
    if not eyes_old:
        # the eyes are faces of the shell itself: every face with any amber on it goes, and the ball that takes their
        # place is the one their own surface lies on
        for side, sign in (("L", 1.0), ("R", -1.0)):
            mine = []
            for f in bm.faces:
                if f.calc_center_median().x * sign <= 0:
                    continue
                spots = [l[uv].uv for l in f.loops] + [face_uv(f)]
                if any(is_amber(*colour_at(u)) for u in spots):
                    mine.append(f)
            centre, radius = sphere_through([v.co for v in {v for f in mine for v in f.verts}])
            towards = sum(((f.calc_center_median() - centre).normalized() * f.calc_area() for f in mine), Vector()).normalized()
            spread = max(towards.angle(v.co - centre) for f in mine for v in f.verts)
            eyes_old[side] = (centre, radius, towards, None, None, spread)
            kept[side] = [(v.co - centre).normalized() for v in {v for f in mine for v in f.verts}]
            if not keep_eyes:
                bmesh.ops.delete(bm, geom=mine, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    # ---- closed: edges that carry more than two faces are parted, and every open edge is then filled
    for turn in range(10):
        crowded = [e for e in bm.edges if len(e.link_faces) > 2]
        if crowded:
            bmesh.ops.split_edges(bm, edges=crowded)
        gaps = [e for e in bm.edges if len(e.link_faces) == 1]
        if not gaps and not crowded:
            break
        had = set(bm.faces)
        if turn < 3:
            bmesh.ops.holes_fill(bm, edges=gaps, sides=0)
            gaps = [e for e in bm.edges if len(e.link_faces) == 1]
            if gaps:
                bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=gaps)
        else:
            # what the fill tools leave: each open ring is closed by hand, with faces to a point in its middle
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
            for l in f.loops:                                 # a new face takes the colour its neighbours have at each corner
                other = next((m for m in l.vert.link_loops if m.face in had), None)
                if other is None:
                    other = next((m for e in l.vert.link_edges for m in e.other_vert(l.vert).link_loops if m.face in had), None)
                if other is not None:
                    l[uv].uv = other[uv].uv
    print("TIDECALLER after closing: open %d, crowded %d" % (sum(1 for e in bm.edges if len(e.link_faces) == 1), sum(1 for e in bm.edges if len(e.link_faces) > 2)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    shell_faces = len(bm.faces)

    # ---- groups
    names = ["body", "eyeball.L", "eyeball.R", "lid.L", "lid.R", "fin.L", "fin.R", "tail.L", "tail.R"]
    gi = {n: ob.vertex_groups.new(name=n).index for n in names}
    dl = bm.verts.layers.deform.verify()
    for v in bm.verts:
        v[dl][gi["body"]] = 1.0

    # ---- fins: what stands out from the body's own roundness, low on the sides and low at the back
    bm.faces.ensure_lookup_table()
    shell = BVHTree.FromBMesh(bm)
    heart = Vector((0, sum(v.co.y for v in bm.verts) / len(bm.verts), H * 0.42))
    reach = {"x": [], "y": [], "up": [], "down": []}
    for i in range(600):
        z = 1 - 2 * (i + 0.5) / 600
        ring = math.sqrt(1 - z * z)
        d = Vector((ring * math.cos(i * 2.39996), ring * math.sin(i * 2.39996), z))
        dist = shell.ray_cast(heart, d)[3]
        if dist is None:
            continue
        for key_, amount in (("x", abs(d.x)), ("y", abs(d.y)), ("up", d.z), ("down", -d.z)):
            if amount > 0.8:
                reach[key_].append(dist)
    A, B = float(np.median(reach["x"])), float(np.median(reach["y"]))
    C_up, C_down = float(np.median(reach["up"])), float(np.median(reach["down"]))

    def outside(co):
        d = co - heart
        return math.sqrt((d.x / A) ** 2 + (d.y / B) ** 2 + (d.z / (C_up if d.z > 0 else C_down)) ** 2)

    fins = {}
    for v in bm.verts:
        out = outside(v.co)
        if out < FIN["from"] or v.co.z > heart.z + 0.05 * H:
            continue
        d = v.co - heart
        side = "L" if d.x > 0 else "R"
        if abs(d.x) / A > 0.62:
            fins.setdefault("fin." + side, []).append((v, out))
        elif d.y / B > 0.62 and abs(d.x) / A < 0.55:
            fins.setdefault("tail." + side, []).append((v, out))
    hinges = {}
    for name, found in list(fins.items()):
        if len(found) < 8:
            del fins[name]
            continue
        tip = max(found, key=lambda item: item[1])[0].co
        d = tip - heart
        scaled = Vector((d.x / A, d.y / B, d.z / (C_up if d.z > 0 else C_down)))
        root_ = heart + d / scaled.length                    # where that direction leaves the body's roundness
        hinges[name] = (root_, (tip - root_).normalized())
        for v, out in found:
            w = min(1.0, max(0.0, (out - FIN["from"]) / FIN["full"]))
            w = w * w * (3 - 2 * w)
            v[dl][gi[name]] = w
            v[dl][gi["body"]] = 1.0 - w

    # ---- materials
    def material(name, colour=None, glow=0.0, base=None, emission=None):
        mat = bpy.data.materials.new(f"{NAME} {name}")
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.8
        if colour is not None:
            mat.diffuse_color = (*colour, 1.0)
            bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
            bsdf.inputs["Emission Color"].default_value = (*colour, 1.0)
            bsdf.inputs["Emission Strength"].default_value = glow
        if base is not None:
            node = nodes.new("ShaderNodeTexImage")
            node.image = base
            links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        if emission is not None:
            node = nodes.new("ShaderNodeTexImage")
            node.image = emission
            links.new(node.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 2.0
        me.materials.append(mat)
        return len(me.materials) - 1

    # the pictures: the amber of the old eyes painted out; the light blue also written out as the emission map
    r, g, b = px[..., 0], px[..., 1], px[..., 2]
    mx, mn = px[..., :3].max(-1), px[..., :3].min(-1)
    span = mx - mn + 1e-6
    hue = np.where(mx == r, ((g - b) / span) % 6, np.where(mx == g, (b - r) / span + 2, (r - g) / span + 4)) * 60
    sat = span / (mx + 1e-6)
    amber_px = (hue > 25) & (hue < 65) & (sat > 0.5) & (mx > 0.45)
    amber_colour = px[amber_px][:, :3].mean(0) if amber_px.any() else np.array([0.95, 0.70, 0.08])
    plain = (sat < 0.55) & (mx < 0.40) & (mx > 0.08)
    body_colour = np.median(px[plain][:, :3], axis=0) * 0.7
    wide = amber_px.copy()
    for _ in range(5):
        wide = wide | np.roll(wide, 1, 0) | np.roll(wide, -1, 0) | np.roll(wide, 1, 1) | np.roll(wide, -1, 1)
    base_px = px.copy()
    if not keep_eyes:
        base_px[wide, :3] = body_colour
    base_px[..., 3] = 1.0
    base = bpy.data.images.new(NAME + "_base", TEXTURE, TEXTURE, alpha=False)
    base.pixels.foreach_set(base_px.ravel())
    base.pack()
    emission, lit = None, 0.0
    if cfg["emission"]:
        blue = np.clip((1 - np.abs(hue - 178) / 26) * 3, 0, 1) * np.clip((sat - 0.38) / 0.2, 0, 1) * np.clip((mx - 0.42) / 0.2, 0, 1)
        for _ in range(2):
            blue = (blue + np.roll(blue, 1, 0) + np.roll(blue, -1, 0) + np.roll(blue, 1, 1) + np.roll(blue, -1, 1)) / 5
        if keep_eyes:                                         # the eyes glow too
            soft = amber_px.astype(np.float32)
            for _ in range(2):
                soft = (soft + np.roll(soft, 1, 0) + np.roll(soft, -1, 0) + np.roll(soft, 1, 1) + np.roll(soft, -1, 1)) / 5
            blue = np.maximum(blue, soft)
        em_px = px.copy()
        em_px[..., :3] *= blue[..., None]
        em_px[..., 3] = 1.0
        emission = bpy.data.images.new(NAME + "_emission", TEXTURE, TEXTURE, alpha=False)
        emission.pixels.foreach_set(em_px.ravel())
        emission.pack()
        lit = float((blue > 0.5).mean()) * 100
    body_slot = material("Body", base=base, emission=emission)
    eye_colour = to_linear(tuple(float(x) for x in amber_colour))
    eye_slot = None if keep_eyes else material("Eye", eye_colour, glow=2.0 if cfg["eye_glow"] else 0.0)
    pupil_slot = material("Pupil", (0.004, 0.004, 0.006)) if cfg["pupil"] else None
    for f in bm.faces:
        f.material_index = body_slot

    # ---- new eyes
    def escapes(point, normal):
        """Can this point be seen from outside? Tried along its own direction and a few round it."""
        front = Vector((0, -1, 0))
        for d in (normal, front, (normal + front), (normal + Vector((0, 0, 0.6))), (normal - Vector((0, 0, 0.6))),
                  (normal + Vector((0.6, 0, 0))), (normal - Vector((0.6, 0, 0)))):
            d = d.normalized()
            if shell.ray_cast(point + d * (H * 0.004), d)[0] is None:
                return True
        return False

    eyes = {}
    report = []
    lid_tint = None
    for side in "LR":
        centre, radius, towards, pupil, axis, spread = eyes_old[side]
        # the right eye is the left one mirrored, so the two match
        if side == "R" and "L" in eyes and not keep_eyes:
            c0, r0, t0, p0, a0, s0 = eyes_old["L"]
            centre, radius, towards, spread = Vector((-c0.x, c0.y, c0.z)), r0, Vector((-t0.x, t0.y, t0.z)), s0
            pupil = Vector((-p0.x, p0.y, p0.z)) if p0 is not None else None
            axis = Vector((-a0.x, a0.y, a0.z)) if a0 is not None else None
        rings, around = 10, 16
        ball_verts = []
        if not keep_eyes:
            pole = towards
            e_a = pole.orthogonal().normalized()
            e_b = pole.cross(e_a)
            made, rows = [], []
            top = bm.verts.new(centre + pole * radius)
            bottom = bm.verts.new(centre - pole * radius)
            for i in range(1, rings):
                lat = math.pi * i / rings
                rows.append([bm.verts.new(centre + (pole * math.cos(lat) + (e_a * math.cos(2 * math.pi * k / around)
                                                                             + e_b * math.sin(2 * math.pi * k / around)) * math.sin(lat)) * radius)
                             for k in range(around)])
            for k in range(around):
                k2 = (k + 1) % around
                made.append(bm.faces.new((top, rows[0][k], rows[0][k2])))
                made.append(bm.faces.new((bottom, rows[-1][k2], rows[-1][k])))
                for a_, b_ in zip(rows[:-1], rows[1:]):
                    made.append(bm.faces.new((a_[k], b_[k], b_[k2], a_[k2])))
            ball_verts = [top, bottom] + [v for row in rows for v in row]
            for f in made:
                f.material_index = eye_slot
                f.smooth = True

        # which part of the ball shows: the eyelid has to cover it
        # (only where the old eye was painted as an eye: a ball can also come through the shell behind a brow)
        showing = kept[side] if keep_eyes else [(v.co - centre).normalized() for v in ball_verts
                                                if towards.angle(v.co - centre) < spread * 1.15 and escapes(v.co, (v.co - centre).normalized())]
        if not showing:
            showing = [towards]
        n = sum(showing, Vector()).normalized()
        u = (Vector((0, 0, 1)) - n * n.z).normalized()
        e1 = u.cross(n).normalized()

        if lid_tint is None:
            bm.faces.ensure_lookup_table()
            found = []
            wide_ = max(n.angle(d) for d in showing) + math.radians(16)
            for k in range(16):
                round_ = (n * math.cos(wide_) + (u * math.cos(2 * math.pi * k / 16) + e1 * math.sin(2 * math.pi * k / 16)) * math.sin(wide_)).normalized()
                index = shell.ray_cast(centre + round_ * H, -round_)[2]
                if index is not None and index < shell_faces:
                    spot = face_uv(bm.faces[index])
                    found.append((float(sum(colour_at(spot))), spot))
            found.sort(key=lambda item: item[0])
            lid_tint = found[len(found) // 2][1] if found else Vector((0.5, 0.5))
        if cfg["pupil"]:
            # the slit: a thin black leaf lying on the ball where the old pupil was, long the way the old one was
            near = pupil is not None and pupil.angle(n) < math.radians(25)      # the old pupil, if it was found where the eye shows
            p_dir = pupil if near else n
            p_axis = axis if near and axis is not None else u
            p_axis = (p_axis - p_dir * p_axis.dot(p_dir)).normalized()
            p_side = p_dir.cross(p_axis).normalized()
            long_ = min(spread, math.radians(40)) * 0.62
            fat = long_ * 0.40
            leaf = []
            for i in range(9):
                t = -1 + 2 * i / 8
                half = fat * max(1 - t * t, 0.06)
                row = []
                for sgn, lift in ((-1, 1.012), (1, 1.012), (1, 0.97), (-1, 0.97)):
                    d = (p_dir + p_axis * math.tan(long_ * t) + p_side * math.tan(half) * sgn).normalized()
                    row.append(bm.verts.new(centre + d * radius * lift))
                leaf.append(row)
            leaf_faces = [bm.faces.new(leaf[0]), bm.faces.new(leaf[-1][::-1])]
            for a_, b_ in zip(leaf[:-1], leaf[1:]):
                for k in range(4):
                    k2 = (k + 1) % 4
                    leaf_faces.append(bm.faces.new((a_[k], b_[k], b_[k2], a_[k2])))
            bmesh.ops.recalc_face_normals(bm, faces=leaf_faces)
            for f in leaf_faces:
                f.material_index = pupil_slot
            ball_verts += [v for row in leaf for v in row]
        if not keep_eyes:
            bmesh.ops.recalc_face_normals(bm, faces=made)
            # amber where the eye shows and some way round, so that it can turn; the rest of the ball is the shell's
            # colour, as the old one was, because a ball can come through the shell behind a brow
            dark_uv = lid_tint
            for f in made:
                way = (f.calc_center_median() - centre).normalized()
                off = towards.angle(way)
                if off > spread * 1.15 + math.radians(12) or (off > spread * 0.9 and min(way.angle(d) for d in showing) > math.radians(16)
                                                               and escapes(centre + way * radius, way)):
                    f.material_index = body_slot
                    for l in f.loops:
                        l[uv].uv = dark_uv
        for v in ball_verts:
            v[dl].clear()
            v[dl][gi["eyeball." + side]] = 1.0

        # the eyelid: a piece of a slightly larger ball. Shut, it covers the part that shows; at rest it is turned up
        # out of sight under the brow.
        def angles(d):
            return math.asin(max(-1.0, min(1.0, d.dot(e1)))), math.atan2(d.dot(u), d.dot(n))

        # the lid covers every bit of amber that can be seen, also what only just shows at the corners
        cover = showing if keep_eyes else showing + [(v.co - centre).normalized() for v in ball_verts[:2 + (rings - 1) * around]
                                                     if spread * 1.15 <= towards.angle(v.co - centre) < spread * 1.15 + math.radians(12)
                                                     and escapes(v.co, (v.co - centre).normalized())]
        spans = [angles(d) for d in cover]
        margin = math.radians(LID["margin"])
        side_margin = margin + math.radians(LID["corners"])      # wider at the corners, where a speck of eye showed past the shut lid
        lam0, lam1 = min(a for a, _ in spans) - side_margin, max(a for a, _ in spans) + side_margin
        phi0, phi1 = min(p for _, p in spans) - margin, max(p for _, p in spans) + margin
        lam0, lam1 = max(lam0, math.radians(-80)), min(lam1, math.radians(80))
        lift = (phi1 - phi0) + math.radians(LID["pad"])
        lift = min(lift, math.radians(165) - phi1)
        inner_r = radius + LID["gap"] * H
        outer_r = inner_r + LID["thick"] * H
        n_lam, n_phi = 8, 6

        def on_lid(i, j, rad):
            lam = lam0 + (lam1 - lam0) * i / n_lam
            phi = phi0 + (phi1 - phi0) * j / n_phi
            return centre + (e1 * math.sin(lam) + (n * math.cos(phi) + u * math.sin(phi)) * math.cos(lam)) * rad

        outer = [[bm.verts.new(on_lid(i, j, outer_r)) for j in range(n_phi + 1)] for i in range(n_lam + 1)]
        inner = [[bm.verts.new(on_lid(i, j, inner_r)) for j in range(n_phi + 1)] for i in range(n_lam + 1)]
        lid_faces = []
        for i in range(n_lam):
            for j in range(n_phi):
                lid_faces.append(bm.faces.new((outer[i][j], outer[i + 1][j], outer[i + 1][j + 1], outer[i][j + 1])))
                lid_faces.append(bm.faces.new((inner[i][j + 1], inner[i + 1][j + 1], inner[i + 1][j], inner[i][j])))
        for i in range(n_lam):
            for j in (0, n_phi):
                lid_faces.append(bm.faces.new((outer[i][j], inner[i][j], inner[i + 1][j], outer[i + 1][j])))
        for j in range(n_phi):
            for i in (0, n_lam):
                lid_faces.append(bm.faces.new((outer[i][j], outer[i][j + 1], inner[i][j + 1], inner[i][j])))
        bmesh.ops.recalc_face_normals(bm, faces=lid_faces)
        # its colour: the shell's, just above the eye
        tint = lid_tint
        lid_verts = [v for grid in (outer, inner) for row in grid for v in row]
        for f in lid_faces:
            f.material_index = body_slot
            f.smooth = True
            for l in f.loops:
                l[uv].uv = tint
        peeking = sum(1 for row in outer for v in row if escapes(v.co, (v.co - centre).normalized()))
        for v in lid_verts:
            v.co = centre + (v.co - centre) * LID["flat"]     # kept tiny inside the eye until a blink
            v[dl].clear()
            v[dl][gi["lid." + side]] = 1.0
        eyes[side] = {"centre": centre.copy(), "n": n.copy(), "u": u.copy(), "e1": e1.copy(), "radius": radius, "lift": lift}
        report.append("%s: ball %.3f m, shows over %d of %d points, lid turned up %.0f deg, %d of %d lid points peek out at rest"
                      % (side, radius, len(showing), len(ball_verts), math.degrees(lift), peeking, (n_lam + 1) * (n_phi + 1)))

    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    open_left = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    # every face that can be seen faces outwards. A game draws one side of a face only, and the scans are wound
    # every which way (the 50 % stage's whole body came out inside out): each face is looked at from both its sides,
    # and turned if its back is the side that is open to the outside.
    bm.faces.ensure_lookup_table()
    whole = BVHTree.FromBMesh(bm)
    turned = []
    for f in bm.faces:
        if f.calc_area() < 1e-10:
            continue
        c_, n_ = f.calc_center_median(), f.normal
        t1 = n_.orthogonal().normalized()
        t2 = n_.cross(t1)
        ways = [n_] + [(n_ + (t1 * a_ + t2 * b_) * 0.9).normalized() for a_, b_ in ((1, 0), (-1, 0), (0, 1), (0, -1), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7), (-0.7, -0.7))]
        front_open = sum(1 for d in ways if whole.ray_cast(c_ + n_ * 2e-4, d)[0] is None)
        back_open = sum(1 for d in ways if whole.ray_cast(c_ - n_ * 2e-4, -d)[0] is None)
        if back_open > front_open:
            turned.append(f)
    bmesh.ops.reverse_faces(bm, faces=turned)
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

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head, b.tail = head, tail
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]

    bone("root", (0, 0, 0), (0, H * 0.15, 0))
    bone("body", heart, heart + Vector((0, 0, H * 0.25)), parent="root")
    for s in "LR":
        e = eyes[s]
        if not keep_eyes:
            bone(f"eyeball.{s}", e["centre"], e["centre"] + e["n"] * e["radius"], parent="body")
        bone(f"lid.{s}", e["centre"], e["centre"] + e["n"] * e["radius"] * 0.6, parent="body")
    for name, (at, along) in hinges.items():
        bone(name, at, at + along * H * 0.18, parent="body")
    bpy.ops.object.mode_set(mode="OBJECT")
    for name in names:
        if name not in arm.bones:
            ob.vertex_groups.remove(ob.vertex_groups[name])
    ob.parent = rig
    ob.modifiers.new("Armature", "ARMATURE").object = rig

    # ---- Idle, to show the rig: it bobs, paddles, looks about and blinks
    pb = rig.pose.bones
    for b in pb:
        b.rotation_mode = "QUATERNION"
    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
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
            b.scale = (scale, scale, scale)
            b.keyframe_insert("scale", frame=frame)

    def lids(frame, shut=None):
        """None: hidden, tiny inside the eye. Otherwise full size: 0 turned right up under the brow .. 1 shut."""
        for s in "LR":
            e = eyes[s]
            key(f"lid.{s}", frame, Quaternion(e["e1"], -e["lift"] * (1.0 - (0.5 if shut is None else shut))),
                scale=1.0 if shut is None else 1.0 / LID["flat"])

    def look(frame, right=0.0, up=0.0):
        for s in "" if keep_eyes else "LR":
            key(f"eyeball.{s}", frame, about(Z, right) @ about(X, up))

    def blink(at):
        # the lid is brought out half way down, shuts, opens half way and is put away again
        for frame, shut in ((at, None), (at + 1, 0.5), (at + 2, 1.0), (at + 4, 1.0), (at + 6, 0.5), (at + 7, None)):
            lids(frame, shut)

    # ---- the clips. Every one but Move starts and ends on the first frame of Idle, so any of them follows Idle cleanly.
    def start(name):
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action
        last.clear()
        return action

    def finish(action, end):
        for layer in action.layers:                           # a lid appears and goes in one step, never half grown
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        if "lid." in fc.data_path and fc.data_path.endswith("scale"):
                            for kp in fc.keyframe_points:
                                kp.interpolation = "CONSTANT"
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, end
        rig.animation_data.action = None
        for b in pb:
            b.rotation_quaternion = (1, 0, 0, 0)
            b.location = (0, 0, 0)
            b.scale = (1, 1, 1)

    def paddle(frame, up=0.0, back=0.0, tail=0.0, splay=0.0):
        """Front fins, both alike: degrees raised (+) or lowered, and swept back (+) or forward. Back fins: degrees
        lowered (+) or raised, and spread (+)."""
        for name in hinges:
            sign = 1.0 if name.endswith(".L") else -1.0
            if name.startswith("fin"):
                key(name, frame, about(Z, sign * back) @ about(Y, -sign * up))
            else:
                key(name, frame, about(X, tail) @ about(Z, sign * splay))

    def body(frame, pitch=0.0, roll=0.0, turn=0.0, lift=0.0, size=None, ahead=0.0):
        """pitch: degrees nose down (+) or up. roll: tipped to its left (+). turn: about its height. lift and ahead:
        shares of its height, up and forward. size: the whole body larger or smaller."""
        key("root", frame, Quaternion(), move=(0, 0, 0))
        key("body", frame, about(Z, turn) @ about(Y, roll) @ about(X, pitch), move=(0, -H * ahead, H * lift), scale=size)

    def swim(t, reach=1.0):
        """The paddling pose at the angle t of a stroke: what Idle is made of."""
        return ({"up": 13.0 * reach * math.sin(t), "back": -9.0 * reach * math.cos(t), "tail": 8.0 * reach * math.sin(t + math.pi), "splay": 4.0 * math.cos(t)},
                {"pitch": 1.5 * math.sin(t - 0.6), "lift": 0.014 * math.sin(t - 2.2)})

    FINS0, BODY0 = swim(0.0)                                  # the first frame of Idle

    def home(*frames, size=None):
        for frame in frames:
            paddle(frame, **FINS0)
            body(frame, size=size, **BODY0)

    def face_home(*frames):
        for frame in frames:
            lids(frame)
            look(frame)

    def shut_eyes(begin, end):
        for frame, shut in ((begin, None), (begin + 1, 0.5), (begin + 2, 1.0), (end, 1.0), (end + 1, 0.5), (end + 2, None)):
            lids(frame, shut)

    # ---- Idle: a slow swim on the spot, the way a turtle paddles. Both front fins stroke together, down and back,
    # then come up and forward; the back fins paddle the other way round, smaller; the body rises a little on each
    # stroke. It blinks, and the stages whose eyes turn look about.
    a = start("Idle")
    LOOP, STROKES = 144, 2
    for frame in range(1, LOOP + 2, 3):
        f_, b_ = swim(2 * math.pi * STROKES * (frame - 1) / LOOP)
        paddle(frame, **f_)
        body(frame, **b_)
    face_home(1, LOOP + 1)
    for frame, right, up in ((1, 0, 0), (34, 0, 0), (41, 13, 0), (60, 13, 0), (69, -13, 1), (88, -13, 1), (96, 0, 4), (108, 0, 0), (LOOP + 1, 0, 0)):
        look(frame, right, up)
    blink(22)
    blink(112)
    blink(120)
    finish(a, LOOP + 1)

    # ---- Move: swimming for real. Nose down a little, the strokes twice as fast and twice as deep, the body
    # surging on each one and rolling a little from side to side. Loops.
    a = start("Move")
    LOOP = 48
    for frame in range(1, LOOP + 2, 2):
        t = 2 * math.pi * 2 * (frame - 1) / LOOP
        paddle(frame, up=24.0 * math.sin(t), back=6.0 - 18.0 * math.cos(t), tail=13.0 * math.sin(t + math.pi), splay=6.0 * math.cos(t))
        body(frame, pitch=9.0 + 2.5 * math.sin(t - 0.6), roll=2.5 * math.sin(t / 2), lift=0.03 + 0.022 * math.sin(t - 2.2))
    face_home(1, LOOP + 1)
    blink(20)
    finish(a, LOOP + 1)

    # ---- TideCall: it bows and gathers its fins in, eyes shut; rears up tall with the fins thrown wide and holds
    # there, trembling; then its eyes open and it beats the fins down twice, the body swelling with each beat as
    # the call goes out; and it sinks back into place.
    a = start("TideCall")
    END = 124
    home(1, END, size=1.0)
    face_home(1, END)
    #        frame  up    back  tail  splay  pitch  lift    size
    call = [(12, -12, -20, 6, 2, 8, -0.035, 1.0), (24, -20, -28, 10, 0, 11, -0.050, 1.0),
            (34, 20, 0, -6, 8, -6, 0.080, 1.0), (44, 42, 12, -12, 12, -13, 0.170, 1.03), (58, 46, 14, -12, 12, -14, 0.185, 1.05),
            (63, -30, 6, 10, 4, 7, 0.040, 1.16), (70, -16, 0, 4, 4, 2, 0.070, 1.02),
            (77, 34, 8, -8, 10, -8, 0.130, 1.03), (82, -28, 4, 9, 4, 6, 0.030, 1.13), (90, -10, -4, 2, 4, 1, 0.020, 1.0),
            (104, 6, -9, 0, 4, -1, -0.022, 1.0)]
    for frame, up, back, tail, splay, pitch, lift, size in call:
        paddle(frame, up, back, tail, splay)
        body(frame, pitch=pitch, lift=lift, size=size)
    for i, frame in enumerate(range(46, 58, 2)):              # the tremble at the top
        body(frame, pitch=-13.5, turn=1.6 if i % 2 else -1.6, lift=0.178, size=1.04)
    shut_eyes(10, 58)
    for frame, up in ((60, 0), (63, 6), (84, 5), (94, 0)):
        look(frame, 0, up)
    blink(108)
    finish(a, END)

    # ---- SwimFlip: it dips, drives up with one hard stroke, tucks its fins and goes over backwards, right round,
    # then spreads the fins to brake as it comes down, and bobs back into place.
    a = start("SwimFlip")
    END = 100
    home(1, END)
    face_home(1, END)
    #        frame  up    back  tail  splay  pitch   lift
    flip = [(8, 18, -14, -4, 6, 7, -0.030), (14, 30, -16, -8, 8, 10, -0.050),
            (20, -10, 10, 10, 4, -4, 0.060), (26, -32, 24, 14, 0, -22, 0.220),
            (32, -30, 30, 12, 0, -75, 0.360), (38, -28, 32, 10, 0, -140, 0.450), (44, -28, 32, 10, 0, -205, 0.470),
            (50, -26, 30, 10, 0, -270, 0.420), (56, -18, 20, 6, 2, -325, 0.330), (62, 10, 0, -4, 8, -358, 0.220),
            (70, 34, -10, -10, 12, -364, 0.050), (77, 18, -10, -4, 8, -357, -0.040), (86, 4, -9, 0, 4, -360, 0.012)]
    for frame, up, back, tail, splay, pitch, lift in flip:
        paddle(frame, up, back, tail, splay)
        body(frame, pitch=pitch, lift=lift)
    paddle(END, **FINS0)
    body(END, pitch=-360.0 + BODY0["pitch"], lift=BODY0["lift"])
    blink(4)
    blink(80)
    finish(a, END)

    # ---- Ram: it draws back and rears, fins raised; drives forward head down with the fins swept flat back and
    # butts, eyes shut for the blow; rebounds, shakes its head, and paddles back to its place.
    a = start("Ram")
    END = 84
    home(1, END)
    face_home(1, END)
    #       frame  up    back  tail  splay  pitch  turn  lift    ahead
    ram = [(8, 14, -14, -4, 6, -5, 0, -0.020, -0.050), (16, 28, -22, -8, 8, -12, 0, -0.035, -0.120),
           (19, -6, 14, 8, 2, 8, 0, 0.000, 0.060), (22, -22, 38, 12, 0, 24, 0, 0.030, 0.400),
           (24, -18, 30, 10, 0, 15, 0, 0.020, 0.430), (27, 22, -24, -8, 10, -8, 0, 0.040, 0.330),
           (31, 10, -12, -2, 6, -2, 8, 0.020, 0.290), (34, 8, -10, 0, 6, 0, -8, 0.010, 0.270), (37, 8, -10, 0, 6, 0, 6, 0.005, 0.250),
           (40, 8, -10, 0, 6, 0, -5, 0.000, 0.235), (44, 6, -9, 0, 4, 0, 0, -0.005, 0.220),
           (52, -12, 4, 6, 4, -3, 0, 0.010, 0.130), (60, 10, -12, -4, 4, -2, 0, -0.005, 0.050), (70, -4, -6, 2, 4, 0, 0, -0.012, 0.008)]
    for frame, up, back, tail, splay, pitch, turn, lift, ahead in ram:
        paddle(frame, up, back, tail, splay)
        body(frame, pitch=pitch, turn=turn, lift=lift, ahead=ahead)
    shut_eyes(19, 27)
    blink(48)
    finish(a, END)

    scene.frame_set(1)

    print("TIDECALLER %s%%: %.2f m tall; %d triangles; %d hidden faces removed; bones %s; fins %s; light blue is %.1f%% of the picture; edges not shared by exactly two faces: %d"
          % (stage, H, tris, removed_layers, [b.name for b in arm.bones], {k: len(v) for k, v in fins.items()}, lit, open_left))
    for line in report:
        print("TIDECALLER eye " + line)


main()
