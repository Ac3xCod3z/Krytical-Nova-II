"""Drone Core 100 %: the artist's own textured model, made ready for the rig.

  run_blender.py --blend SM_Pet_DroneCore_100.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/DroneCore/prep_dronecore_100.py
  then rig_dronecore_stage.py

The texture is the artist's and is kept as painted. Only two things are done to it:

  vent stripes   on the two side vents the yellow stripes came out in uneven shades with soft edges. The texels
                 that are clearly stripe (bright yellow) are set to one yellow, the vents' own, and their outline
                 is tidied, so the stripes are even and their edges clean. The dull olive between the stripes,
                 and everything else on the model, is left as painted.
  emission map   the light blue lines and the yellow lights are written to a second picture, on black, so their
                 glow can be set in Unity.

  T_Pet_DroneCore_100.png, T_Pet_DroneCore_100_Emission.png   2048 x 2048, beside this file

(paint_dronecore_100.py is the earlier attempt at painting this model from pictures. It is not used any more.)
Safe to run again: it starts from the untouched model kept in the file.
"""
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

NAME = "SM_Pet_DroneCore_100"
RAW = "DroneCore100%"
SIZE = 2048
FACE_STRIPE = 0.5              # a vent face with this share of stripe texels is a stripe from edge to edge
VENT = {"out": 0.33, "low": 0.26, "high": 0.60, "front": -0.14, "back": 0.38}     # where the side vents are, in the model's own measures


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
        if img.name.startswith("T_Pet_DroneCore_100"):
            bpy.data.images.remove(img)
    source.hide_viewport = source.hide_render = True
    source.use_fake_user = True
    me = source.data.copy()
    me.name = NAME
    ob = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = source.matrix_world.copy()

    painted = next(n.image for s in source.material_slots for n in s.material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    work = painted.copy()
    work.scale(SIZE, SIZE)
    px = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    work.pixels.foreach_get(px)
    px = px.reshape(SIZE, SIZE, 4)
    bpy.data.images.remove(work)
    rgb = px[..., :3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    span = mx - mn + 1e-6
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.where(mx == r, ((g - b) / span) % 6, np.where(mx == g, (b - r) / span + 2, (r - g) / span + 4)) * 60
    sat = span / (mx + 1e-6)

    # ---- which texels belong to the side vents
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(ob.matrix_world)
    uv = bm.loops.layers.uv.active
    vent = np.zeros((SIZE, SIZE), dtype=bool)
    vent_faces = []                                       # each vent face's own texels: (y0, y1, x0, x1, mask)
    for f in bm.faces:
        c = f.calc_center_median()
        if not (abs(c.x) > VENT["out"] and VENT["low"] < c.z < VENT["high"] and VENT["front"] < c.y < VENT["back"]):
            continue
        pts = [Vector((l[uv].uv.x * SIZE, l[uv].uv.y * SIZE)) for l in f.loops]
        fx0, fx1 = int(max(min(p_.x for p_ in pts) - 1, 0)), int(min(max(p_.x for p_ in pts) + 2, SIZE))
        fy0, fy1 = int(max(min(p_.y for p_ in pts) - 1, 0)), int(min(max(p_.y for p_ in pts) + 2, SIZE))
        own = np.zeros((max(fy1 - fy0, 0), max(fx1 - fx0, 0)), dtype=bool)
        vent_faces.append((fy0, fy1, fx0, fx1, own))
        for i in range(1, len(pts) - 1):
            a, b_, c_ = pts[0], pts[i], pts[i + 1]
            x0, x1 = int(max(min(a.x, b_.x, c_.x) - 1, 0)), int(min(max(a.x, b_.x, c_.x) + 2, SIZE))
            y0, y1 = int(max(min(a.y, b_.y, c_.y) - 1, 0)), int(min(max(a.y, b_.y, c_.y) + 2, SIZE))
            twice = (b_.x - a.x) * (c_.y - a.y) - (c_.x - a.x) * (b_.y - a.y)
            if x1 <= x0 or y1 <= y0 or abs(twice) < 1e-9:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            w0 = ((b_.x - gx) * (c_.y - gy) - (c_.x - gx) * (b_.y - gy)) / twice
            w1 = ((c_.x - gx) * (a.y - gy) - (a.x - gx) * (c_.y - gy)) / twice
            inside = (w0 >= -0.01) & (w1 >= -0.01) & (1 - w0 - w1 >= -0.01)
            vent[y0:y1, x0:x1] |= inside
            own[y0 - fy0:y1 - fy0, x0 - fx0:x1 - fx0] |= inside
    bm.free()

    # ---- the stripes, evened out: every vent texel that is yellow at all becomes the one yellow
    yellowish = vent & (hue > 40) & (hue < 64) & (sat > 0.50) & (mx > 0.72)        # the stripes themselves, not the dull olive between them
    strong = vent & (hue > 40) & (hue < 64) & (sat > 0.50) & (mx > 0.55)
    the_yellow = np.percentile(rgb[strong], 75, axis=0) if strong.sum() > 50 else np.array([0.85, 0.75, 0.25])
    # tidy the stripe shapes a little: a texel is a stripe if most of the texels round it are
    tidy = yellowish.astype(np.int16)
    count = np.zeros_like(tidy)
    for dy in (-2, -1, 0, 1, 2):
        for dx in (-2, -1, 0, 1, 2):
            count += np.roll(tidy, (dy, dx), (0, 1))
    stripe = vent & (count >= 13)
    base = rgb.copy()
    base[stripe] = the_yellow
    # then face by face, so a stripe ends on the mesh's own edges and is never patchy: a vent face that is mostly
    # stripe becomes stripe whole; one with only a little yellow on it loses that yellow (its own other colour)
    whole, wiped = 0, 0
    for fy0, fy1, fx0, fx1, own in vent_faces:
        if own.sum() < 4:
            continue
        here_ = stripe[fy0:fy1, fx0:fx1] & own
        share = here_.sum() / own.sum()
        view = base[fy0:fy1, fx0:fx1]
        if share >= FACE_STRIPE:
            view[own] = the_yellow
            stripe[fy0:fy1, fx0:fx1] |= own
            whole += 1
        elif share > 0.0:
            rest = own & ~here_
            view[here_] = np.median(rgb[fy0:fy1, fx0:fx1][rest], axis=0)
            stripe[fy0:fy1, fx0:fx1] &= ~own
            wiped += 1
    print("DRONECORE100 vent faces %d: whole stripe %d, yellow taken off %d" % (len(vent_faces), whole, wiped))

    # ---- what glows
    blue = (hue > 160) & (hue < 205) & (sat > 0.28) & (mx > 0.52)
    yellow = ((hue > 38) & (hue < 66) & (sat > 0.45) & (mx > 0.55)) | stripe
    lit = (blue | yellow).astype(np.float32)
    lit = (lit + np.roll(lit, 1, 0) + np.roll(lit, -1, 0) + np.roll(lit, 1, 1) + np.roll(lit, -1, 1)) / 5
    glow = base * lit[..., None]

    def save(name, picture):
        img = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
        full = np.ones((SIZE, SIZE, 4), dtype=np.float32)
        full[..., :3] = picture
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
    shader.inputs["Emission Strength"].default_value = 2.0
    me.materials.clear()
    me.materials.append(mat)
    for poly in me.polygons:
        poly.material_index = 0
    print("DRONECORE100 prepared: vent texels %d, of them stripe %d (were yellow at all: %d); the yellow #%02X%02X%02X; glowing %.1f%% of the texture"
          % (vent.sum(), stripe.sum(), yellowish.sum(), *(int(round(v * 255)) for v in the_yellow), 100 * (lit > 0.5).mean()))


main()
