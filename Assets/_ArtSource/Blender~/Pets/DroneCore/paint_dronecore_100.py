"""Drone Core 100 %: the model came without a texture, so one is painted here, on its own UV map.

  run_blender.py --blend SM_Pet_DroneCore_100.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/DroneCore/paint_dronecore_100.py
  then rig_dronecore_stage.py

Painted the way a texture artist blocks a hard-surface model in, part by part, not by smearing pictures over it:

  1. The model is split into its parts: every flat or gently curved patch bounded by hard edges (a panel, a strip of
     the frame, the floor of a groove, one slat of a vent) is one part. This is the model's ID map.
  2. Each part is filled with ONE flat colour, edge to edge. Which colour is read from the artist's three pictures
     (Ref100/front.png, side.png, back.png): the colour most of the part shows in the pictures that see it. The back
     picture is taken at an angle; the angle is worked out first, by turning the model until its outline matches.
  3. Lights are placed by hand, not read off the pictures by the script: every blue line and yellow bar is a shape
     (STENCILS, below) traced on a straight-on view and projected onto the faces that view looks at. Their edges
     are exact and their widths constant, whatever the faces under them look like.
  4. Parts no picture sees take the colour of the parts round them.

  T_Pet_DroneCore_100.png            the colours (flat, in Synty's olive and greys)
  T_Pet_DroneCore_100_Emission.png   only what glows: the light blue lines and lens, and the yellow vents

Safe to run again: it starts from the untouched model kept in the file.
"""
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# ---- The lights, placed by hand. Each is a shape traced on one straight-on view and projected onto the faces that
#      view looks at: a line (its corner points and its width), a box, or an arc. Front and side shapes are traced on
#      the artist's own pictures, in the picture's pixels (x from the left, y from the top), so they land exactly
#      where the picture has them. The back has no straight-on picture: its shapes are set on the model itself, in
#      the model's own measures (x across, z up), on the slots and slats the back is modelled with.
BLUE_W, THIN_W = 5.5, 3.0
STENCILS = {
    "front": [
        ("line", [(155, 108), (155, 140), (182, 170), (280, 170), (303, 140), (303, 108)], BLUE_W, "blue"),      # the brow
        ("line", [(145, 82), (147, 118)], BLUE_W, "blue"), ("line", [(313, 82), (311, 118)], BLUE_W, "blue"),    # beside the top plate
        ("line", [(109, 106), (109, 300)], THIN_W, "blue"), ("line", [(347, 106), (347, 300)], THIN_W, "blue"),  # the frame's outer edges
        ("line", [(127, 162), (129, 142), (147, 138)], BLUE_W, "blue"), ("line", [(331, 162), (329, 142), (311, 138)], BLUE_W, "blue"),   # upper hooks
        ("line", [(128, 270), (128, 330), (134, 338), (155, 338)], BLUE_W, "blue"),                              # lower brackets
        ("line", [(330, 270), (330, 330), (324, 338), (303, 338)], BLUE_W, "blue"),
        ("line", [(168, 362), (170, 336), (186, 319), (272, 319), (288, 336), (291, 362)], BLUE_W, "blue"),      # round the chin plate
        ("line", [(181, 299), (279, 299)], 5.0, "yellow"),                                                       # the yellow bar
        ("arc", (229, 378), 17.0, 205.0, -25.0, 4.5, "blue"),                                                    # over the cannon
        ("arc", (229, 238), 51.0, 0.0, 360.0, 5.0, "blue"),                                                      # round the lens
    ],
    "side": [
        ("line", [(175, 357), (95, 357), (80, 345), (80, 152), (95, 140), (305, 140), (318, 152), (320, 345), (310, 357), (296, 357)], BLUE_W, "blue"),
        ("line", [(78, 122), (86, 112), (322, 112), (328, 120)], THIN_W, "blue"),                                # along the roof
        ("box", (82, 215, 88, 268), "blue"),                                                                     # the small light on the grey block
        ("box", (117, 184, 129, 300), "yellow"), ("box", (137, 184, 149, 300), "yellow"), ("box", (157, 184, 170, 300), "yellow"),
        ("box", (177, 184, 190, 300), "yellow"), ("box", (197, 184, 210, 300), "yellow"),                        # the five vent bars
    ],
    "back": [                        # model measures: x from, z from, x to, z to
        ("box", (-0.070, 0.690, 0.070, 0.700), "blue"), ("box", (-0.070, 0.667, 0.070, 0.677), "blue"), ("box", (-0.070, 0.643, 0.070, 0.653), "blue"),   # the vent's three slots
        ("box", (-0.135, 0.196, 0.135, 0.214), "blue"),                                                          # the long slot at the bottom
        ("box", (0.104, 0.272, 0.116, 0.330), "blue"), ("box", (-0.116, 0.272, -0.104, 0.330), "blue"),          # the two short dashes
    ],
}
NAME = "SM_Pet_DroneCore_100"
RAW = "DroneCore100%"
SIZE = 2048
HARD = 8.0                  # degrees: an edge sharper than this is the border between two parts
LIGHT_SIZE = 0.006          # the biggest a light may be, as a share of the whole surface
BACK_LIGHT_SIZE = 0.0       # the same for the sunk lights of the back
CHANNEL_SIZE = 0.012        # the biggest a channel, slot or slat may be, as a share of the whole surface
REACH_BOX = 6.0             # how far outside a traced box a fold may still be one of its lights (picture pixels)
SAME_RUN = 2.5              # folds this close to the nearest one belong to the same channel (picture pixels)
REACH_PICTURE = 11.0        # how far beside a traced shape a channel is still taken to be that shape's (picture pixels)
CREASE = 14.0               # degrees: an inward fold at least this sharp is a crease a light can lie in
CREASE_WIDE = 1.0           # a crease's light is this many times the width its shape is traced with
CHANNEL_FIT = 2.4           # a channel may be this many times as wide as the light traced for it, no wider
REACH_MODEL = 0.008         # the same for shapes set on the model
LIGHT_SHARE = 9.0           # a part is a light only if at least this much of what the pictures show of it is lit
#          name          colour      glows
COLOURS = {"olive": ("#5A6552", False), "dark olive": ("#3C4636", False), "grey": ("#76787B", False), "light grey": ("#9D9DA1", False),
           "dark grey": ("#353837", False), "black": ("#1E1F20", False), "blue": ("#4FE3F0", True), "yellow": ("#FFD21F", True)}


def rgb(h):
    return tuple(int(h[i:i + 2], 16) / 255 for i in (1, 3, 5))


def main():
    here = Path(bpy.data.filepath).parent
    source = bpy.data.objects.get(RAW + "_source") or bpy.data.objects[RAW]
    source.name = RAW + "_source"
    source.data.name = RAW + "_source"
    for o in list(bpy.data.objects):
        if o is not source:
            bpy.data.objects.remove(o, do_unlink=True)
    for mat in list(bpy.data.materials):
        if mat.name.startswith(NAME):
            bpy.data.materials.remove(mat)
    for img in list(bpy.data.images):
        if img.name.startswith(("T_Pet_DroneCore_100", "ref100_")):
            bpy.data.images.remove(img)
    source.hide_viewport = source.hide_render = True
    source.use_fake_user = True
    me = source.data.copy()
    me.name = NAME
    ob = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = source.matrix_world.copy()

    names = list(COLOURS)
    table = np.array([rgb(COLOURS[n][0]) for n in names], dtype=np.float32)
    glows = [COLOURS[n][1] for n in names]

    def picture(name):
        img = bpy.data.images.load(str(here / "Ref100" / name))
        w, h = img.size
        px = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(h, w, 4)
        bpy.data.images.remove(img)
        drone = px[..., :3].min(-1) < 0.90                   # the drone, on its white ground
        rows, cols = np.where(drone.any(1))[0], np.where(drone.any(0))[0]
        return px, drone, (cols[0], cols[-1], rows[0], rows[-1])     # left, right, bottom, top (rows count from the bottom)

    def kinds_of(c):
        """The flat colour (as a number) of each of many picture colours; -1 for the white ground."""
        mx, mn = c.max(-1), c.min(-1)
        span = mx - mn + 1e-6
        r, g, b = c[..., 0], c[..., 1], c[..., 2]
        hue = np.where(mx == r, ((g - b) / span) % 6, np.where(mx == g, (b - r) / span + 2, (r - g) / span + 4)) * 60
        sat = span / (mx + 1e-6)
        out = np.where(mx > 0.30, names.index("olive"), names.index("dark olive"))
        greyish = (sat < 0.13) | ((hue >= 190) & (hue <= 260))
        grey = np.where(mx > 0.62, names.index("light grey"), np.where(mx > 0.40, names.index("grey"),
                        np.where(mx > 0.20, names.index("dark grey"), names.index("black"))))
        out = np.where(greyish, grey, out)
        out = np.where((hue >= 40) & (hue <= 68) & (sat > 0.50) & (mx > 0.70), names.index("yellow"), out)
        out = np.where((hue >= 150) & (hue <= 205) & (sat > 0.30) & (mx > 0.55), names.index("blue"), out)
        return np.where((mx > 0.93) & (sat < 0.08), -1, out)

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(ob.matrix_world)
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    all_pts = np.array([v.co[:] for v in bm.verts])
    span_ = float((all_pts.max(0) - all_pts.min(0)).max())

    def camera(direction):
        """A straight-on camera standing in that direction from the model: its right and up, and the model's box in it."""
        d = Vector(direction).normalized()
        right = (-d).cross(Vector((0, 0, 1))).normalized()
        up = right.cross(-d).normalized()
        a, b = all_pts @ np.array(right[:]), all_pts @ np.array(up[:])
        return d, right, up, (a.min(), a.max(), b.min(), b.max())

    # ---- the back picture's angle: the model is turned until its outline fills the same shape as the picture's
    back_px, back_drone, back_box = picture("back.png")
    left, right_, bottom, top_ = back_box
    grid = 72
    ys, xs = np.linspace(bottom, top_, grid).astype(int), np.linspace(left, right_, grid).astype(int)
    want = back_drone[np.ix_(ys, xs)]
    best = (-1.0, None)
    for az in range(100, 261, 5):
        if 170 < az < 190:
            continue
        for el in range(5, 46, 5):
            d = (math.sin(math.radians(az)) * math.cos(math.radians(el)), -math.cos(math.radians(az)) * math.cos(math.radians(el)), math.sin(math.radians(el)))
            d, r_, u_, box = camera(d)
            a, b = all_pts @ np.array(r_[:]), all_pts @ np.array(u_[:])
            ia = np.clip(((a - box[0]) / (box[1] - box[0]) * (grid - 1)).round().astype(int), 0, grid - 1)
            ib = np.clip(((b - box[2]) / (box[3] - box[2]) * (grid - 1)).round().astype(int), 0, grid - 1)
            got = np.zeros((grid, grid), dtype=bool)
            got[ib, ia] = True
            for _ in range(2):                                 # the corners alone leave gaps: close them
                got = got | np.roll(got, 1, 0) | np.roll(got, -1, 0) | np.roll(got, 1, 1) | np.roll(got, -1, 1)
            # the picture's aspect has to match as well as the outline
            aspect = ((box[1] - box[0]) / (box[3] - box[2])) / ((right_ - left) / (top_ - bottom))
            score = (got & want).sum() / max((got | want).sum(), 1) - abs(math.log(aspect)) * 0.6
            if score > best[0]:
                best = (score, (az, el, d))
    back_az, back_el, back_dir = best[1]

    front_px, _fd, front_box = picture("front.png")
    side_px, _sd, side_box = picture("side.png")
    #        picture   its drone box   where the camera stands   mirrored
    views = [(front_px, front_box, (0, -1, 0), False), (side_px, side_box, (-1, 0, 0), False), (side_px, side_box, (1, 0, 0), True),
             (back_px, back_box, back_dir, False)]
    cams = [camera(v[2]) for v in views]

    # ---- the parts
    part_of, parts = {}, []
    hard = math.radians(HARD)
    for f in bm.faces:
        if f.index in part_of:
            continue
        stack, group = [f], []
        part_of[f.index] = len(parts)
        while stack:
            g = stack.pop()
            group.append(g)
            for e in g.edges:
                if len(e.link_faces) != 2:
                    continue
                h = e.link_faces[0] if e.link_faces[1] is g else e.link_faces[1]
                if h.index not in part_of and g.normal.angle(h.normal, 0.0) < hard:
                    part_of[h.index] = len(parts)
                    stack.append(h)
        parts.append(group)

    # ---- what each part shows in the pictures
    votes = np.zeros((len(parts), len(names)))
    eps = span_ * 0.002
    samples = ((1 / 3, 1 / 3), (0.6, 0.2), (0.2, 0.6), (0.2, 0.2))
    for f in bm.faces:
        tri = [l.vert.co for l in f.loops][:3]
        for (px, box, _d, mirrored), (d, r_, u_, mbox) in zip(views, cams):
            facing = f.normal.dot(d)
            if facing < 0.25:
                continue
            for wa, wb in samples:
                p = tri[0] * (1 - wa - wb) + tri[1] * wa + tri[2] * wb
                if tree.ray_cast(p + d * eps, d)[0] is not None:
                    continue                                   # something stands between it and the camera
                across = (p.dot(r_) - mbox[0]) / (mbox[1] - mbox[0])
                high = (p.dot(u_) - mbox[2]) / (mbox[3] - mbox[2])
                if mirrored:
                    across = 1 - across
                x = int(round(box[0] + across * (box[1] - box[0])))
                y = int(round(box[2] + high * (box[3] - box[2])))
                if 0 <= x < px.shape[1] and 0 <= y < px.shape[0]:
                    k = int(kinds_of(px[y, x, :3][None, :])[0])
                    if k >= 0:
                        votes[part_of[f.index], k] += f.calc_area() * facing / len(samples)
    part_area = [sum(f.calc_area() for f in group) for group in parts]
    whole_area = sum(part_area)
    colour_of = [-1] * len(parts)
    for i in range(len(parts)):
        total = votes[i].sum()
        if total <= 0:
            continue
        lights = [k for k in range(len(names)) if glows[k]]
        lit = max(lights, key=lambda k: votes[i, k])
        # a light is a small part (a strip, a slat, the floor of a groove): a whole panel or box never is one
        if votes[i, lit] / total >= LIGHT_SHARE and part_area[i] < whole_area * LIGHT_SIZE:
            colour_of[i] = lit
        else:
            plain = votes[i].copy()
            plain[lights] = 0
            colour_of[i] = int(plain.argmax()) if plain.sum() > 0 else lit
    # parts no picture sees take the colour most of their border has; what is still left is olive
    for _ in range(6):
        for i, group in enumerate(parts):
            if colour_of[i] >= 0:
                continue
            border = {}
            for f in group:
                for e in f.edges:
                    for h in e.link_faces:
                        j = part_of[h.index]
                        if j != i and colour_of[j] >= 0 and not glows[colour_of[j]]:
                            border[colour_of[j]] = border.get(colour_of[j], 0.0) + e.calc_length()
            if border:
                colour_of[i] = max(border, key=border.get)
    colour_of = [c if c >= 0 else names.index("olive") for c in colour_of]

    # ---- the back. Its picture is taken at an angle, too loosely to place its small lights from it; but the model
    #      itself says where they are: on the back, every small part that is sunk below the parts round it (the
    #      floor of a slot, the gaps of the vent) is one of the blue lights of the picture.
    blue = names.index("blue")
    for i, group in enumerate(parts):
        normal = sum((f.normal * f.calc_area() for f in group), Vector()).normalized()
        if normal.y < 0.7 or part_area[i] > whole_area * BACK_LIGHT_SIZE:
            continue
        xs_ = [v.co.x for f in group for v in f.verts]
        zs_ = [v.co.z for f in group for v in f.verts]
        long_, short_ = max(max(xs_) - min(xs_), max(zs_) - min(zs_)), min(max(xs_) - min(xs_), max(zs_) - min(zs_))
        if long_ < 2.5 * max(short_, 1e-6) or long_ < span_ * 0.03:        # a slot is long and thin; a bolt hole is not
            continue
        depth = sum(f.calc_center_median().y * f.calc_area() for f in group) / part_area[i]
        round_depth, round_len = 0.0, 0.0
        for f in group:
            for e in f.edges:
                for h in e.link_faces:
                    j = part_of[h.index]
                    if j != i:
                        n_j = sum((g.normal * g.calc_area() for g in parts[j]), Vector()).normalized()
                        if n_j.y > 0.7:                        # the back-facing parts beside it
                            round_depth += sum(g.calc_center_median().y * g.calc_area() for g in parts[j]) / part_area[j] * e.calc_length()
                            round_len += e.calc_length()
        # beside it there are only walls (it is the floor of a slot), or its back-facing neighbours stand further out
        walls_only = round_len == 0.0
        if walls_only or depth < round_depth / round_len - span_ * 0.004:
            deeper = [part_of[h.index] for f in group for e in f.edges for h in e.link_faces if part_of[h.index] != i]
            if walls_only and not deeper:
                continue
            colour_of[i] = blue

    # ---- the light lines. In the pictures a thin line of light runs along the border of many panels. Each border
    #      of each part is checked in the pictures a little way in from the edge; where the picture is lit there,
    #      the part gets a band of light along that border: always the same width, the same distance in, dead
    #      straight, because it is drawn from the model's own edge and not copied from the picture.
    def light_at(point, normal):
        """The light (a colour number) the pictures show at a point of the surface, or None."""
        found = {}
        for (px, box, _d, mirrored), (d, r_, u_, mbox) in zip(views, cams):
            if normal.dot(d) < 0.45 or tree.ray_cast(point + d * eps, d)[0] is not None:
                continue
            across = (point.dot(r_) - mbox[0]) / (mbox[1] - mbox[0])
            high = (point.dot(u_) - mbox[2]) / (mbox[3] - mbox[2])
            if mirrored:
                across = 1 - across
            x = int(round(box[0] + across * (box[1] - box[0])))
            y = int(round(box[2] + high * (box[3] - box[2])))
            if 0 <= x < px.shape[1] and 0 <= y < px.shape[0]:
                r, g, b = (float(v) for v in px[y, x, :3])
                mx, mn = max(r, g, b), min(r, g, b)
                sat = (mx - mn) / (mx + 1e-6)
                # only the bright heart of a light counts, not the glow round it
                if mx > 0.72 and sat > 0.40 and b > r and g > r:
                    found["blue"] = found.get("blue", 0) + 1
                elif mx > 0.80 and sat > 0.55 and r > b * 1.6 and g > b * 1.4:
                    found["yellow"] = found.get("yellow", 0) + 1
                else:
                    found[None] = found.get(None, 0) + 1
        if not found:
            return None
        best_ = max(found, key=found.get)
        return names.index(best_) if best_ else None

    bands = {}                      # part -> [(end, end, how far in, colour)]
    LINE_IN = ()
    for e in bm.edges:
        if len(e.link_faces) != 2 or part_of[e.link_faces[0].index] == part_of[e.link_faces[1].index]:
            continue
        if e.calc_length() < span_ * 0.02:
            continue
        a_, b_ = e.verts[0].co, e.verts[1].co
        along = (b_ - a_).normalized()
        for f in e.link_faces:
            i = part_of[f.index]
            if glows[colour_of[i]] or part_area[i] < whole_area * 0.002:
                continue
            inward = f.calc_center_median() - (a_ + b_) / 2
            inward = (inward - along * inward.dot(along))
            inward = (inward - f.normal * inward.dot(f.normal))
            if inward.length < 1e-9:
                continue
            inward.normalize()
            for step in LINE_IN:
                hits = [light_at(a_ + (b_ - a_) * t + inward * (span_ * step), f.normal) for t in (0.2, 0.35, 0.5, 0.65, 0.8)]
                lit = [h for h in hits if h is not None]
                if len(lit) >= 4 and lit.count(lit[0]) == len(lit):
                    bands.setdefault(i, []).append((a_.copy(), b_.copy(), span_ * step, lit[0]))
                    break
    LINE_WIDE = span_ * 0.011

    # ---- the lights go INTO the model's channels. The model is built with a channel, a slot or a slat wherever a
    #      light belongs: a small part of its own. A traced shape only says which of them is lit. Every small part
    #      that a shape covers (looked for a little wider than the shape is drawn, because a tracing is never quite
    #      on the channel) becomes that light whole, so the light fills the channel exactly, wall to wall. Only a
    #      shape that finds no channel at all (a light the model has no slot for) is drawn on the surface as traced.
    def view_of(f):
        for view_name, cam_i, facing in (("front", 0, -f.normal.y), ("side", 1, -f.normal.x), ("side", 2, f.normal.x), ("back", 3, f.normal.y)):
            if facing > 0.55:
                d_ = Vector((0, -1, 0)) if view_name == "front" else Vector((0, 1, 0)) if view_name == "back" else Vector((-1 if cam_i == 1 else 1, 0, 0))
                if tree.ray_cast(f.calc_center_median() + d_ * eps, d_)[0] is None:
                    return view_name, cam_i
                return None
        return None

    def on_view(view_name, cam_i, point):
        if view_name == "back":
            return point.x, point.z
        px_, box, _dir, mirrored = views[cam_i]
        _d, r_, u_, mbox = cams[cam_i]
        across = (point.dot(r_) - mbox[0]) / (mbox[1] - mbox[0])
        high = (point.dot(u_) - mbox[2]) / (mbox[3] - mbox[2])
        if mirrored:
            across = 1 - across
        return box[0] + across * (box[1] - box[0]), (px_.shape[0] - 1) - (box[2] + high * (box[3] - box[2]))

    def outline_of(shape):
        if shape[0] == "arc":
            (cx, cy), rad, a0, a1, wide = shape[1], shape[2], shape[3], shape[4], shape[5]
            return [(cx + rad * math.cos(math.radians(a0 + (a1 - a0) * i_ / 24)), cy - rad * math.sin(math.radians(a0 + (a1 - a0) * i_ / 24))) for i_ in range(25)], wide
        return shape[1], shape[2]

    def covers(shape, u_, v_, pad):
        if shape[0] == "box":
            x_a, y_a, x_b, y_b = shape[1]
            return x_a - pad <= u_ <= x_b + pad and y_a - pad <= v_ <= y_b + pad
        pts_, wide = outline_of(shape)
        for (xa, ya), (xb, yb) in zip(pts_[:-1], pts_[1:]):
            ex_, ey_ = xb - xa, yb - ya
            t_ = min(1.0, max(0.0, ((u_ - xa) * ex_ + (v_ - ya) * ey_) / max(ex_ * ex_ + ey_ * ey_, 1e-9)))
            if (u_ - xa - t_ * ex_) ** 2 + (v_ - ya - t_ * ey_) ** 2 <= (wide / 2 + pad) ** 2:
                return True
        return False

    # ---- front and sides: the lights lie IN THE CREASES of the model. A channel there is not a part of its own but a
    #      crease: the inward fold where a panel meets its frame, the valley between two ribs of the vent, the two
    #      folds of a slot's floor. Every such fold that runs along a traced shape, close beside it, gets a band of
    #      light centred on the fold itself: a thin line of one width, sitting exactly in the model's channel and
    #      mimicking the picture's line, never spread over a whole face.
    claimed, channels, crease_count = set(), 0, 0
    creases = {}                    # face -> [(end, end, width, colour)]

    def run_of(shape, u_, v_):
        """Which way the shape runs at that spot (a direction on the view)."""
        if shape[0] == "box":
            x_a, y_a, x_b, y_b = shape[1]
            return (1.0, 0.0) if (x_b - x_a) > (y_b - y_a) else (0.0, 1.0)
        pts_, _wide = outline_of(shape)
        best_d, best_dir = 1e18, (1.0, 0.0)
        for (xa, ya), (xb, yb) in zip(pts_[:-1], pts_[1:]):
            ex_, ey_ = xb - xa, yb - ya
            t_ = min(1.0, max(0.0, ((u_ - xa) * ex_ + (v_ - ya) * ey_) / max(ex_ * ex_ + ey_ * ey_, 1e-9)))
            d_ = (u_ - xa - t_ * ex_) ** 2 + (v_ - ya - t_ * ey_) ** 2
            if d_ < best_d:
                best_d, best_dir = d_, (ex_, ey_)
        return best_dir

    def nearest_piece(shape, u_, v_):
        """Which stretch of a traced line is nearest that spot, and how far off it is (an arc counts as one stretch)."""
        pts_, _wide = outline_of(shape)
        best_d, best_i = 1e18, 0
        for i_, ((xa, ya), (xb, yb)) in enumerate(zip(pts_[:-1], pts_[1:])):
            ex_, ey_ = xb - xa, yb - ya
            t_ = min(1.0, max(0.0, ((u_ - xa) * ex_ + (v_ - ya) * ey_) / max(ex_ * ex_ + ey_ * ey_, 1e-9)))
            d_ = math.hypot(u_ - xa - t_ * ex_, v_ - ya - t_ * ey_)
            if d_ < best_d:
                best_d, best_i = d_, i_
        return (0 if shape[0] == "arc" else best_i), best_d

    found_ = {}
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        f1, f2 = e.link_faces
        if f1.normal.angle(f2.normal, 0.0) < math.radians(CREASE):
            continue
        if (f2.calc_center_median() - f1.calc_center_median()).dot(f1.normal) <= 0:
            continue                                           # a ridge, not a fold inwards
        where_ = view_of(f1) or view_of(f2)
        if where_ is None or where_[0] == "back":
            continue
        a_, b_ = e.verts[0].co, e.verts[1].co
        ua, va = on_view(where_[0], where_[1], a_)
        ub, vb = on_view(where_[0], where_[1], b_)
        um, vm = (ua + ub) / 2, (va + vb) / 2
        if math.hypot(ub - ua, vb - va) < 1.5:
            continue                                           # seen end on
        cam_ = 0 if where_[0] == "front" else 1
        per_unit = (cams[cam_][3][1] - cams[cam_][3][0]) / (views[cam_][1][1] - views[cam_][1][0])
        best_fit = None
        for s_i, shape in enumerate(STENCILS[where_[0]]):
            if shape[0] == "box":
                # a box marks a field of lights (the vent): every fold inside it that runs its long way is one
                if not (covers(shape, um, vm, REACH_BOX) and covers(shape, ua, va, REACH_BOX) and covers(shape, ub, vb, REACH_BOX)):
                    continue
                piece, off = 0, 0.0
            else:
                piece, off = nearest_piece(shape, um, vm)
                if off > REACH_PICTURE or nearest_piece(shape, ua, va)[1] > REACH_PICTURE * 1.5 or nearest_piece(shape, ub, vb)[1] > REACH_PICTURE * 1.5:
                    continue
            rx, ry = run_of(shape, um, vm)
            along_ = abs((ub - ua) * rx + (vb - va) * ry) / max(math.hypot(ub - ua, vb - va) * math.hypot(rx, ry), 1e-9)
            if along_ < math.cos(math.radians(30)):
                continue                                       # it crosses the line: not its channel
            wide_ = (min(shape[1][2] - shape[1][0], shape[1][3] - shape[1][1]) if shape[0] == "box" else outline_of(shape)[1]) * per_unit * CREASE_WIDE
            if best_fit is None or off < best_fit[0]:
                best_fit = (off, (where_[0], s_i, piece), (a_.copy(), b_.copy(), wide_, names.index(shape[-1])))
        if best_fit is not None:
            found_.setdefault(best_fit[1], []).append((best_fit[0], best_fit[2], e))

    # of the folds found beside one stretch of a traced line, only the nearest run is its channel: the tracing was
    # made on a picture, the picture is not the model to the pixel, and a line must not light two channels at once
    for (view_name, s_i, piece), hits in found_.items():
        nearest = min(h[0] for h in hits)
        for off, entry, e in hits:
            if STENCILS[view_name][s_i][0] != "box" and off > nearest + SAME_RUN:
                continue
            for f in {g for v in e.verts for g in v.link_faces}:
                creases.setdefault(f.index, []).append(entry)
            claimed.add((view_name, s_i))
            crease_count += 1

    for i, group in enumerate(parts):
        if part_area[i] > whole_area * CHANNEL_SIZE:
            continue
        if not any((view_of(f) or ("", 0))[0] == "back" for f in group[:4]):
            continue
        tally, seen_area = {}, 0.0
        for f in group:
            where_ = view_of(f)
            if where_ is None:
                continue
            seen_area += f.calc_area()
            spots_ = [f.calc_center_median()] + [(v.co + f.calc_center_median()) / 2 for v in f.verts]
            for s_i, shape in enumerate(STENCILS[where_[0]]):
                pad = REACH_MODEL if where_[0] == "back" else REACH_PICTURE
                inside_ = sum(1 for q in spots_ if covers(shape, *on_view(where_[0], where_[1], q), pad))
                if inside_:
                    tally[(where_[0], s_i)] = tally.get((where_[0], s_i), 0.0) + f.calc_area() * inside_ / len(spots_)
        if tally:
            best_ = max(tally, key=tally.get)
            # a channel is about as wide as the light drawn for it: a broad strip of frame that a thin line merely
            # runs along is not that line's channel
            corners = [v.co for f in group for v in f.verts]
            long_ = max(max(c_[k] for c_ in corners) - min(c_[k] for c_ in corners) for k in range(3))
            part_wide = part_area[i] / max(long_, 1e-9)
            shape_ = STENCILS[best_[0]][best_[1]]
            if best_[0] == "back":
                per_unit = 1.0
            else:
                cam_ = 0 if best_[0] == "front" else 1
                per_unit = (cams[cam_][3][1] - cams[cam_][3][0]) / (views[cam_][1][1] - views[cam_][1][0])
            shape_wide = (min(shape_[1][2] - shape_[1][0], shape_[1][3] - shape_[1][1]) if shape_[0] == "box" else outline_of(shape_)[1]) * per_unit
            if part_wide > shape_wide * CHANNEL_FIT:
                continue
            if tally[best_] >= 0.6 * seen_area and seen_area >= 0.5 * part_area[i]:
                colour_of[i] = names.index(STENCILS[best_[0]][best_[1]][-1])
                claimed.add(best_)
                channels += 1

    # ---- every part filled, edge to edge, on the UV map
    uv = bm.loops.layers.uv.active
    index = np.full((SIZE, SIZE), names.index("olive"), dtype=np.int32)
    done = np.zeros((SIZE, SIZE), dtype=bool)
    area = np.zeros(len(names))
    for f in bm.faces:
        kind = colour_of[part_of[f.index]]
        area[kind] += f.calc_area()
        pts = [Vector((l[uv].uv.x * SIZE, l[uv].uv.y * SIZE)) for l in f.loops]
        cos = [l.vert.co for l in f.loops]
        # a face one of the straight-on pictures looks squarely at is painted point by point from that picture, so
        # the thin lines drawn on a panel land where the picture has them; its part's colour fills in the rest
        see = None
        # which hand-placed shapes can land on this face: those of the view that looks squarely at it, if nothing
        # stands in the way
        stencil = None
        for view_name, cam_i, facing in (("front", 0, -f.normal.y), ("side", 1, -f.normal.x), ("side", 2, f.normal.x), ("back", 3, f.normal.y)):
            if facing > 0.55:
                d_ = Vector((0, -1, 0)) if view_name == "front" else Vector((0, 1, 0)) if view_name == "back" else Vector((-1 if cam_i == 1 else 1, 0, 0))
                if tree.ray_cast(f.calc_center_median() + d_ * eps, d_)[0] is None:
                    stencil = (view_name, cam_i)
                break
        if see == 3:
            # the back picture is taken at an angle: only a face that picture really shows, and shows squarely, is
            # painted from it
            d_back = cams[3][0]
            c_ = f.calc_center_median()
            if f.normal.dot(d_back) < 0.55 or tree.ray_cast(c_ + d_back * eps, d_back)[0] is not None:
                see = None
        for i in range(1, len(pts) - 1):
            a, b, c = pts[0], pts[i], pts[i + 1]
            pa, pb_, pc = cos[0], cos[i], cos[i + 1]
            x0, x1 = int(max(min(a.x, b.x, c.x) - 1, 0)), int(min(max(a.x, b.x, c.x) + 2, SIZE))
            y0, y1 = int(max(min(a.y, b.y, c.y) - 1, 0)), int(min(max(a.y, b.y, c.y) + 2, SIZE))
            twice = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)
            if x1 <= x0 or y1 <= y0 or abs(twice) < 1e-9:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            w0 = ((b.x - gx) * (c.y - gy) - (c.x - gx) * (b.y - gy)) / twice
            w1 = ((c.x - gx) * (a.y - gy) - (a.x - gx) * (c.y - gy)) / twice
            w2 = 1 - w0 - w1
            inside = (w0 >= -0.01) & (w1 >= -0.01) & (w2 >= -0.01)
            got = np.full(gx.shape, kind, dtype=np.int32)
            if see is not None:
                px_, box, _dir, mirrored = views[see]
                _d, r_, u_, mbox = cams[see]
                X, Y, Z = (w0 * pa[k] + w1 * pb_[k] + w2 * pc[k] for k in range(3))
                across = (X * r_.x + Y * r_.y + Z * r_.z - mbox[0]) / (mbox[1] - mbox[0])
                high = (X * u_.x + Y * u_.y + Z * u_.z - mbox[2]) / (mbox[3] - mbox[2])
                if mirrored:
                    across = 1 - across
                xi = np.clip(np.rint(box[0] + across * (box[1] - box[0])).astype(int), 0, px_.shape[1] - 1)
                yi = np.clip(np.rint(box[2] + high * (box[3] - box[2])).astype(int), 0, px_.shape[0] - 1)
                seen_kind = kinds_of(px_[yi, xi, :3])
                got = np.where(seen_kind >= 0, seen_kind, got)
            for p0, p1, far_in, light in bands.get(part_of[f.index], ()):
                X, Y, Z = (w0 * pa[k] + w1 * pb_[k] + w2 * pc[k] for k in range(3))
                ex, ey, ez = p1.x - p0.x, p1.y - p0.y, p1.z - p0.z
                t_ = np.clip(((X - p0.x) * ex + (Y - p0.y) * ey + (Z - p0.z) * ez) / (ex * ex + ey * ey + ez * ez), 0.0, 1.0)
                gap = np.sqrt((X - p0.x - t_ * ex) ** 2 + (Y - p0.y - t_ * ey) ** 2 + (Z - p0.z - t_ * ez) ** 2)
                got = np.where(np.abs(gap - far_in) < LINE_WIDE / 2, light, got)
            if stencil is not None:
                view_name, cam_i = stencil
                X, Y, Z = (w0 * pa[k] + w1 * pb_[k] + w2 * pc[k] for k in range(3))
                if view_name == "back":
                    U, V = X, Z
                else:
                    px_, box, _dir, mirrored = views[cam_i]
                    _d, r_, u_, mbox = cams[cam_i]
                    across = (X * r_.x + Y * r_.y + Z * r_.z - mbox[0]) / (mbox[1] - mbox[0])
                    high = (X * u_.x + Y * u_.y + Z * u_.z - mbox[2]) / (mbox[3] - mbox[2])
                    if mirrored:
                        across = 1 - across
                    U = box[0] + across * (box[1] - box[0])
                    V = (px_.shape[0] - 1) - (box[2] + high * (box[3] - box[2]))       # counted from the top, as the shapes are
                for s_i, shape in enumerate(STENCILS[view_name]):
                    if (view_name, s_i) in claimed or view_name != "back":
                        continue                               # front and side lights live in the creases only; the back's two dashes have no slot
                    light = names.index(shape[-1])
                    if shape[0] == "box":
                        x_a, y_a, x_b, y_b = shape[1]
                        hit = (U >= x_a) & (U <= x_b) & (V >= y_a) & (V <= y_b)
                    else:
                        if shape[0] == "arc":
                            (cx, cy), rad, a0, a1, wide = shape[1], shape[2], shape[3], shape[4], shape[5]
                            pts_ = [(cx + rad * math.cos(math.radians(a0 + (a1 - a0) * i_ / 24)), cy - rad * math.sin(math.radians(a0 + (a1 - a0) * i_ / 24))) for i_ in range(25)]
                        else:
                            pts_, wide = shape[1], shape[2]
                        hit = np.zeros(U.shape, dtype=bool)
                        for (xa, ya), (xb, yb) in zip(pts_[:-1], pts_[1:]):
                            ex_, ey_ = xb - xa, yb - ya
                            t_ = np.clip(((U - xa) * ex_ + (V - ya) * ey_) / max(ex_ * ex_ + ey_ * ey_, 1e-9), 0.0, 1.0)
                            hit |= (U - xa - t_ * ex_) ** 2 + (V - ya - t_ * ey_) ** 2 <= (wide / 2) ** 2
                    got = np.where(hit, light, got)
            for p0, p1, wide_, light in creases.get(f.index, ()):
                X, Y, Z = (w0 * pa[k] + w1 * pb_[k] + w2 * pc[k] for k in range(3))
                ex, ey, ez = p1.x - p0.x, p1.y - p0.y, p1.z - p0.z
                raw_t = ((X - p0.x) * ex + (Y - p0.y) * ey + (Z - p0.z) * ez) / (ex * ex + ey * ey + ez * ez)
                t_ = np.clip(raw_t, 0.0, 1.0)
                gap = np.sqrt((X - p0.x - t_ * ex) ** 2 + (Y - p0.y - t_ * ey) ** 2 + (Z - p0.z - t_ * ez) ** 2)
                got = np.where((gap <= wide_ / 2) & (raw_t >= -0.02) & (raw_t <= 1.02), light, got)
            index[y0:y1, x0:x1][inside] = got[inside]
            done[y0:y1, x0:x1] |= inside
    # tidied: a texel takes the colour most of the texels round it have, twice over, so the soft edges and specks of
    # the pictures become clean shapes
    for _ in range(0):
        counts_ = np.zeros((len(names), SIZE, SIZE), dtype=np.int16)
        for dy in (-2, -1, 0, 1, 2):
            for dx in (-2, -1, 0, 1, 2):
                moved = np.roll(index, (dy, dx), (0, 1))
                ok = np.roll(done, (dy, dx), (0, 1))
                for k in range(len(names)):
                    counts_[k] += ((moved == k) & ok)
        index = np.where(done, counts_.argmax(0), index).astype(np.int32)
    for _ in range(8):                                         # the colours run a few pixels past each island's edge
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            take = np.roll(done, (dy, dx), (0, 1)) & ~done
            index[take] = np.roll(index, (dy, dx), (0, 1))[take]
            done |= take
    base = table[index]
    glow = np.where(np.array(glows)[index][..., None], base, 0.0).astype(np.float32)
    bm.free()

    def save(name, px):
        img = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
        full = np.ones((SIZE, SIZE, 4), dtype=np.float32)
        full[..., :3] = px
        img.pixels.foreach_set(full.ravel())
        img.filepath_raw = str(here / (name + ".png"))
        img.file_format = "PNG"
        img.save()
        img.pack()
        return img

    base_img = save("T_Pet_DroneCore_100", base)
    glow_img = save("T_Pet_DroneCore_100_Emission", glow)
    mat = bpy.data.materials.new(NAME + " Body")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes["Principled BSDF"]
    shader.inputs["Roughness"].default_value = 0.8
    for img, socket in ((base_img, "Base Color"), (glow_img, "Emission Color")):
        node = nodes.new("ShaderNodeTexImage")
        node.image = img
        links.new(node.outputs["Color"], shader.inputs[socket])
    shader.inputs["Emission Strength"].default_value = 2.5
    me.materials.clear()
    me.materials.append(mat)
    for poly in me.polygons:
        poly.material_index = 0
    total = area.sum()
    print("DRONECORE100 creases lit: %d" % crease_count)
    print("DRONECORE100 lights: %d channels lit; shapes that found a channel %d of %d; drawn on the surface instead: %s"
          % (channels, len(claimed), sum(len(v) for v in STENCILS.values()),
             [(k, i_) for k, v in STENCILS.items() for i_ in range(len(v)) if (k, i_) not in claimed]))
    print("DRONECORE100 painted: %d parts; back picture taken from %d deg round, %d deg up (match %.2f); share of the surface: %s"
          % (len(parts), back_az, back_el, best[0], {n: "%.0f%%" % (100 * a / total) for n, a in sorted(zip(names, area), key=lambda kv: -kv[1]) if a > 0}))


main()
