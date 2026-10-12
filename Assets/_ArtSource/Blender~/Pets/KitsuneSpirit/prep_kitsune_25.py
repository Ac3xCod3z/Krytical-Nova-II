"""Kitsune Spirit, the 25 % form: the artist's own Tripo model, kept as it is, with a clean UV map to paint on.

  run_blender.py --blend SM_Pet_KitsuneSpirit_25.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/prep_kitsune_25.py
  then  pet_rig.py --landmarks rig_kitsune_25.json, anim_kitsune_25.py
  (not pet_finish.py: it would put the model on the swatch and its texture would be lost; not pet_eyes.py: see eyes)

The owner's word, 11 Oct 2026: use the imported model, not a rebuilt one; unwrap its UVs so the texture can be
fixed by hand; keep the clean eyes. So the mesh and its look are not changed. What is done:

  size     0.66 m long, standing on the ground at the origin
  thinned  the owner asked for the whole model to be thinned (11 Oct 2026): about half its triangles go, and nearly
           all of the eyeballs' (only a sliver of each shows). Its shape is kept; its texture is not touched by this,
           because the texture is read from the full model (see texture)
  eyes     the model's own eyeballs stay where they are, in their sockets, so the eyes have exactly the shape the
           model came with. They are given one flat dark material (Eye) in place of their painted shading.
           (pet_eyes.py's flat eye plates were tried first; the owner asked for eyes that follow the original shape.)
  eyelids  each eye gets an eyelid: a segment of a shell a little larger than the eyeball (the shape of a segment of
           an orange, from one corner of the eye to the other), in the fur's colour. At rest it sits inside the
           head just above the socket; turned about the eye's own long axis it rolls down over the eyeball and
           shuts the eye, corners and all. Vertex groups eye.L / eye.R (rigid with the head) and
           eyelid.L / eyelid.R; pet_rig.py makes the eyelid bones from kn_eyelid_L / _R stored on the object, and
           kn_lid_turn_L / _R is how many degrees shut the eye (read by anim_kitsune_25.py)
  UV map   the model came with Tripo's packed map: hundreds of scraps, no use for painting. It gets a new map,
           "map1", cut the way a painter would: each leg a sleeve cut down its inner side, each tail cut along its
           underside, each ear cut up its back, the head cut under the jaw, the body cut along the belly; the scarf
           and the talisman by angle. The islands are packed at one scale.
  texture  the model's own texture is carried over to the new map (nothing is repainted): every texel of the new
           map takes the colour the full, unthinned model has at that spot of the surface. The colour is bled past
           each island's edge so no seam shows:  T_Pet_KitsuneSpirit_25.png  (2048 x 2048)
  glow     the tail tips, on black, as a second picture so their glow can be set in Unity:
           T_Pet_KitsuneSpirit_25_Emission.png
  UV sheet T_Pet_KitsuneSpirit_25_UVLayout.png: the islands drawn over the texture, to find one's way when painting

To paint: edit T_Pet_KitsuneSpirit_25.png (or paint on the model in Blender's Texture Paint), save it over the
same file. Running this script again makes the texture afresh from the Tripo model: keep a copy of painted work.
Safe to run again: it starts from the untouched model kept in the file.
"""
import heapq
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SRC = "KitsuneSpirit25%"
NAME = "SM_Pet_KitsuneSpirit_25"
SIZE_M = 0.66
PICTURE = 2048
BLEED = 12                   # texels of colour past each island's edge
# where the parts are, in the model's own measures as it came (1.0 long, facing -y, standing on z = 0)
LEG_TOP, LEG_SPLIT = 0.175, -0.08
EAR_BASE, EAR_OUT = 0.47, 0.03
HEAD_FRONT, HEAD_LOW = -0.335, 0.28
TAIL_TIP_Y = 0.385
THIN = {"body": 0.50, "eyes": 0.30}      # the share of its triangles each keeps
EYE_COLOUR = (0.006, 0.006, 0.008)       # the eyeballs: one flat near-black (linear)
LID_LARGER, LID_GAP = 1.05, 0.0004       # the eyelid shell: this much larger than the eyeball, plus this many metres
LID_MARGIN = 7.0                         # degrees the lid reaches past the showing eye, above and below
LID_ACROSS, LID_ALONG = 5, 10            # the lid's faces: across its width, and from corner to corner



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


def path(verts, start, goal, cost):
    """The cheapest way from one vertex to another along edges between these vertices; cost(edge) weighs each."""
    best, back, heap, count = {start: 0.0}, {}, [(0.0, 0, start)], 0
    while heap:
        d, _n, v = heapq.heappop(heap)
        if v is goal:
            break
        if d > best.get(v, 1e18):
            continue
        for e in v.link_edges:
            o = e.other_vert(v)
            if o not in verts:
                continue
            nd = d + cost(e)
            if nd < best.get(o, 1e18):
                best[o] = nd
                back[o] = (v, e)
                count += 1
                heapq.heappush(heap, (nd, count, o))
    edges, v = [], goal
    while v in back:
        v, e = back[v]
        edges.append(e)
    return edges


def main():
    here = Path(bpy.data.filepath).parent
    scene = bpy.context.scene
    source = bpy.data.objects.get(SRC + "_source") or bpy.data.objects[SRC]
    source.name = SRC + "_source"
    source.data.name = SRC + "_source"
    for o in list(bpy.data.objects):
        if o is not source:
            bpy.data.objects.remove(o, do_unlink=True)
    for mat in list(bpy.data.materials):
        if mat.name.startswith(NAME):
            bpy.data.materials.remove(mat)
    for img in list(bpy.data.images):
        if img.name.startswith("T_Pet_KitsuneSpirit_25"):
            bpy.data.images.remove(img)
    for action in list(bpy.data.actions):                     # the rig is made again after this, and its clips with it
        bpy.data.actions.remove(action)
    for key in list(scene.keys()):
        if key.startswith("kn_"):
            del scene[key]
    painted = next(n.image for m in source.data.materials for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    source.hide_viewport = source.hide_render = True
    source.use_fake_user = True

    me = source.data.copy()
    me.name = NAME
    ob = bpy.data.objects.new(NAME, me)
    scene.collection.objects.link(ob)
    ob.matrix_world = source.matrix_world.copy()

    me.uv_layers.active.name = "tripo"                      # the map it came with (not used on the thinned model)

    # ---- the full model, kept to read colours from: its faces, their corners and where each corner is on its texture
    full = bmesh.new()
    full.from_mesh(source.data)
    full.transform(source.matrix_world)
    for v in full.verts:
        v.co = v.co * SIZE_M
    full_uv = full.loops.layers.uv.active
    eyeballs = set()
    for group in islands_of(full):
        pts = [v.co for f in group for v in f.verts]
        mid = sum(pts, Vector()) / len(pts) / SIZE_M
        if len(group) < 800 and abs(mid.x) > 0.02 and mid.y < -0.36 and 0.34 < mid.z < 0.43:
            eyeballs.update(group)
    skin = bmesh.new()                                       # ... without the eyeballs, whose paint must not land on the sockets
    twin_, skin_faces = {}, []
    for f in full.faces:
        if f in eyeballs:
            continue
        for v in f.verts:
            if v not in twin_:
                twin_[v] = skin.verts.new(v.co)
        try:
            skin.faces.new([twin_[v] for v in f.verts])
            skin_faces.append(([v.co.copy() for v in f.verts], [l[full_uv].uv.copy() for l in f.loops]))
        except ValueError:
            pass
    skin.faces.ensure_lookup_table()
    skin_tree = BVHTree.FromBMesh(skin)

    # ---- thinned
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.index_update()
    eye_now = set()
    for group in islands_of(bm):
        pts = [v.co for f in group for v in f.verts]
        mid = sum(pts, Vector()) / len(pts)
        if len(group) < 800 and abs(mid.x) > 0.02 and mid.y < -0.36 and 0.34 < mid.z < 0.43:
            eye_now.update(v.index for f in group for v in f.verts)
    before_tris = sum(len(f.verts) - 2 for f in bm.faces)
    eye_tris = sum(len(f.verts) - 2 for f in bm.faces if f.verts[0].index in eye_now)
    bm.free()
    ob.vertex_groups.new(name="kn_thin_eyes").add(sorted(eye_now), 1.0, "REPLACE")
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.view_layer.objects:
        o.select_set(o is ob)
    # the modifier's ratio is a share of the whole mesh, though it only thins the group it is given: so each pass is
    # told the share of the WHOLE that is left when its own part has been thinned to what it should keep
    after_eyes = before_tris - eye_tris * (1.0 - THIN["eyes"])
    after_body = after_eyes - (before_tris - eye_tris) * (1.0 - THIN["body"])
    for inverted, ratio in ((False, after_eyes / before_tris), (True, after_body / after_eyes)):
        thin = ob.modifiers.new("Thin", "DECIMATE")
        thin.decimate_type = "COLLAPSE"
        thin.ratio = ratio
        thin.vertex_group = "kn_thin_eyes"
        thin.invert_vertex_group = inverted
        bpy.ops.object.modifier_apply(modifier=thin.name)
    ob.vertex_groups.remove(ob.vertex_groups["kn_thin_eyes"])
    me = ob.data

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.transform(ob.matrix_world)
    ob.matrix_world.identity()

    # ---- the pieces
    kind = {}
    for group in islands_of(bm):
        pts = [v.co for f in group for v in f.verts]
        mid = sum(pts, Vector()) / len(pts)
        if len(group) < 800 and abs(mid.x) > 0.02 and mid.y < -0.36 and 0.34 < mid.z < 0.43:
            what = "eye"
        elif len(group) < 400 and abs(mid.x) < 0.03 and mid.y < -0.3 and mid.z < 0.27:
            what = "tag"
        elif mid.y > 0.12:
            what = "tails"
        elif len(group) < 600:
            what = "scarf"
        else:
            what = "body"
        for f in group:
            kind[f] = what

    # ---- which part each face belongs to
    part = {}
    for f in bm.faces:
        c = f.calc_center_median()
        side = "L" if c.x >= 0 else "R"
        if kind[f] == "eye":
            part[f] = "eye." + side
        elif kind[f] == "tails":
            part[f] = "tail." + side
        elif kind[f] != "body":
            part[f] = kind[f]
        elif c.z < LEG_TOP:
            part[f] = ("front_leg." if c.y < LEG_SPLIT else "back_leg.") + side
        elif c.z > EAR_BASE and abs(c.x) > EAR_OUT:
            part[f] = "ear." + side
        elif c.y < HEAD_FRONT and c.z > HEAD_LOW:
            part[f] = "head"
        else:
            part[f] = "body"
    # a part is one connected piece: stray faces go with what surrounds them
    for _ in range(4):
        for f in bm.faces:
            near = [part[n] for e in f.edges for n in e.link_faces if n is not f]
            if near and part[f] not in near:
                part[f] = max(set(near), key=near.count)

    for e in bm.edges:
        e.seam = len({part[f] for f in e.link_faces}) > 1
    verts_of = {}
    for f in bm.faces:
        verts_of.setdefault(part[f], set()).update(f.verts)

    def cut(name, start, goal, cost):
        vs = verts_of.get(name)
        if not vs:
            return 0
        a, b = min(vs, key=start), min(vs, key=goal)
        edges = path(vs, a, b, cost)
        for e in edges:
            e.seam = True
        return len(edges)

    def mid_of(e):
        return (e.verts[0].co + e.verts[1].co) / 2

    cuts = {}
    for side, sign in (("L", 1.0), ("R", -1.0)):
        for leg in ("front_leg.", "back_leg."):               # a sleeve: down the inner side, from the top to the sole
            name = leg + side
            cuts[name] = cut(name, lambda v: -v.co.z + abs(v.co.x) * 2, lambda v: v.co.z + abs(v.co.x),
                             lambda e: e.calc_length() * (1.0 + 30.0 * abs(mid_of(e).x)))
        name = "tail." + side                                  # along the underside, from the base to the tip
        cuts[name] = cut(name, lambda v: v.co.y + v.co.z, lambda v: -v.co.y,
                         lambda e: e.calc_length() * (1.0 + 12.0 * max(mid_of(e).z - 0.2, 0.0)))
        name = "ear." + side                                   # up the back of the ear
        cuts[name] = cut(name, lambda v: v.co.z - v.co.y, lambda v: -v.co.z,
                         lambda e: e.calc_length() * (1.0 + 20.0 * max(-0.3 - mid_of(e).y, 0.0)))
    cuts["head"] = cut("head", lambda v: v.co.z - v.co.y + abs(v.co.x) * 3, lambda v: v.co.y + abs(v.co.x),      # under the jaw, to the nose
                       lambda e: e.calc_length() * (1.0 + 25.0 * abs(mid_of(e).x) + 6.0 * max(mid_of(e).z - 0.3, 0.0)))
    cuts["body"] = cut("body", lambda v: v.co.y + v.co.z + abs(v.co.x) * 3, lambda v: -v.co.y + v.co.z + abs(v.co.x) * 3,   # along the belly
                       lambda e: e.calc_length() * (1.0 + 25.0 * abs(mid_of(e).x) + 8.0 * max(mid_of(e).z - 0.19, 0.0)))

    for v in bm.verts:                                        # its size
        v.co = v.co * SIZE_M
    new_uv = bm.loops.layers.uv.new("map1")
    bm.faces.ensure_lookup_table()
    bm.faces.index_update()
    by_angle = [f.index for f in bm.faces if part[f] in ("scarf", "tag")]
    eye_faces = {f.index: part[f][-1] for f in bm.faces if part[f].startswith("eye.")}
    eye_verts = {side: sorted({v.index for f in bm.faces if part[f] == "eye." + side for v in f.verts}) for side in ("L", "R")}
    tails_tip = [f.index for f in bm.faces if part[f].startswith("tail.") and f.calc_center_median().y > TAIL_TIP_Y * SIZE_M]
    detail = sorted({v.index for f in bm.faces if part[f] == "tag" for v in f.verts})
    tally = {}
    for f in bm.faces:
        tally[part[f]] = tally.get(part[f], 0) + 1
    bm.to_mesh(me)
    bm.free()
    me.uv_layers.active = me.uv_layers["map1"]
    me.uv_layers["map1"].active_render = True

    # ---- unwrap
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.view_layer.objects:
        o.select_set(o is ob)
    angle = set(by_angle)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for poly in me.polygons:
        poly.select = poly.index not in angle and poly.index not in eye_faces
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.unwrap(method="ANGLE_BASED", margin=0.002)
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for poly in me.polygons:
        poly.select = poly.index in angle
    bpy.ops.object.mode_set(mode="EDIT")
    if angle:
        bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.002)
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for poly in me.polygons:                                  # the eyeballs are one flat material: no room on the map for them
        poly.select = poly.index not in eye_faces
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=0.006)
    bpy.ops.object.mode_set(mode="OBJECT")
    for poly in me.polygons:
        if poly.index in eye_faces:
            for li in poly.loop_indices:
                me.uv_layers["map1"].data[li].uv = (0.002, 0.002)

    # ---- the texture, carried over: every texel of the new map reads the old map at the same spot of the same face
    w, h = painted.size
    src_px = np.empty(w * h * 4, dtype=np.float32)
    painted.pixels.foreach_get(src_px)
    src_px = src_px.reshape(h, w, 4)[..., :3]
    base = np.zeros((PICTURE, PICTURE, 3), dtype=np.float32)
    done = np.zeros((PICTURE, PICTURE), dtype=bool)
    glow_mask = np.zeros((PICTURE, PICTURE), dtype=bool)
    tips = set(tails_tip)
    uv_new = me.uv_layers["map1"].data
    from mathutils.interpolate import poly_3d_calc as corner_weights
    for poly in me.polygons:
        if poly.index in eye_faces:
            continue
        loops = list(poly.loop_indices)
        for i in range(1, len(loops) - 1):
            tri = (loops[0], loops[i], loops[i + 1])
            a, b, c = (Vector((uv_new[l].uv.x * PICTURE, uv_new[l].uv.y * PICTURE)) for l in tri)
            pa, pb_, pc = (me.vertices[me.loops[l].vertex_index].co for l in tri)
            x0, x1 = int(max(min(a.x, b.x, c.x) - 1, 0)), int(min(max(a.x, b.x, c.x) + 2, PICTURE))
            y0, y1 = int(max(min(a.y, b.y, c.y) - 1, 0)), int(min(max(a.y, b.y, c.y) + 2, PICTURE))
            twice = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)
            if x1 <= x0 or y1 <= y0 or abs(twice) < 1e-9:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            w0 = ((b.x - gx) * (c.y - gy) - (c.x - gx) * (b.y - gy)) / twice
            w1 = ((c.x - gx) * (a.y - gy) - (a.x - gx) * (c.y - gy)) / twice
            w2 = 1.0 - w0 - w1
            inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
            if not inside.any():
                continue
            view, mask = base[y0:y1, x0:x1], done[y0:y1, x0:x1]
            for yy, xx in zip(*np.nonzero(inside)):         # each texel: its place on the thinned model, then on the full one
                place = pa * float(w0[yy, xx]) + pb_ * float(w1[yy, xx]) + pc * float(w2[yy, xx])
                at, _n, index, _d = skin_tree.find_nearest(place)
                corners, uvs = skin_faces[index]
                weights = corner_weights(corners, at)
                u = min(max(sum(uv_.x * w_ for uv_, w_ in zip(uvs, weights)), 0.0), 0.99999)
                v = min(max(sum(uv_.y * w_ for uv_, w_ in zip(uvs, weights)), 0.0), 0.99999)
                view[yy, xx] = src_px[int(v * h), int(u * w)]
            mask |= inside
            if poly.index in tips:
                glow_mask[y0:y1, x0:x1] |= inside
    filled = done.copy()
    for _ in range(BLEED):                                    # colour past each island's edge
        grown = filled.copy()
        total = np.zeros_like(base)
        count = np.zeros((PICTURE, PICTURE), dtype=np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            near = np.roll(filled, (dy, dx), (0, 1))
            total += np.roll(base, (dy, dx), (0, 1)) * near[..., None]
            count += near
            grown |= near
        new = grown & ~filled
        base[new] = total[new] / np.maximum(count[new], 1.0)[..., None]
        filled = grown
    if not filled.all():
        base[~filled] = np.median(base[done], axis=0)
    glow = base * glow_mask[..., None]

    def save(name, picture):
        img = bpy.data.images.new(name, PICTURE, PICTURE, alpha=False)
        full = np.ones((PICTURE, PICTURE, 4), dtype=np.float32)
        full[..., :3] = picture
        img.pixels.foreach_set(full.ravel())
        img.filepath_raw = str(here / (name + ".png"))
        img.file_format = "PNG"
        img.save()
        img.pack()
        return img

    base_img = save("T_Pet_KitsuneSpirit_25", base)
    glow_img = save("T_Pet_KitsuneSpirit_25_Emission", glow)

    # the islands drawn over the texture, to find one's way when painting
    sheet = base * 0.55 + 0.25
    lines = np.zeros((PICTURE, PICTURE), dtype=bool)
    for poly in me.polygons:
        if poly.index in eye_faces:
            continue
        loops = list(poly.loop_indices)
        for i, l in enumerate(loops):
            p, q = uv_new[l].uv, uv_new[loops[(i + 1) % len(loops)]].uv
            n = int(max(abs(q.x - p.x), abs(q.y - p.y)) * PICTURE) + 1
            xs = np.clip((np.linspace(p.x, q.x, n) * PICTURE).astype(np.int32), 0, PICTURE - 1)
            ys = np.clip((np.linspace(p.y, q.y, n) * PICTURE).astype(np.int32), 0, PICTURE - 1)
            lines[ys, xs] = True
    sheet[lines] = (0.05, 0.05, 0.05)
    edge = done & ~(np.roll(done, 1, 0) & np.roll(done, -1, 0) & np.roll(done, 1, 1) & np.roll(done, -1, 1))
    sheet[edge] = (1.0, 0.25, 0.1)
    layout = save("T_Pet_KitsuneSpirit_25_UVLayout", sheet)
    bpy.data.images.remove(layout)                           # a file to look at, not part of the model

    me.uv_layers.remove(me.uv_layers["tripo"])
    mat = bpy.data.materials.new(NAME + " Body")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes["Principled BSDF"]
    shader.inputs["Roughness"].default_value = 0.8
    for img, socket in ((base_img, "Base Color"), (glow_img, "Emission Color")):
        node = nodes.new("ShaderNodeTexImage")
        node.image = img
        links.new(node.outputs["Color"], shader.inputs[socket])
        if socket == "Base Color":
            nodes.active = node
    shader.inputs["Emission Strength"].default_value = 1.5
    eye_mat = bpy.data.materials.new(NAME + " Eye")
    eye_mat.diffuse_color = (*EYE_COLOUR, 1.0)
    eye_mat.use_nodes = True
    eye_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*EYE_COLOUR, 1.0)
    eye_mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.35
    me.materials.clear()
    me.materials.append(mat)
    me.materials.append(eye_mat)
    for poly in me.polygons:
        poly.material_index = 1 if poly.index in eye_faces else 0
    ob.vertex_groups.new(name="kn_details").add(detail, 1.0, "REPLACE")
    for side in ("L", "R"):
        ob.vertex_groups.new(name="eye." + side).add(eye_verts[side], 1.0, "REPLACE")

    # ---- the eyelids
    lid_report = {}
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bm.verts.ensure_lookup_table()
    layer = bm.loops.layers.uv.active
    head = bmesh.new()                                       # everything but the eyeballs: what the eyes sit in
    twin, back = {}, []
    for f in bm.faces:
        if f.index in eye_faces:
            continue
        for v in f.verts:
            if v not in twin:
                twin[v] = head.verts.new(v.co)
        try:
            head.faces.new([twin[v] for v in f.verts])
            back.append(f)
        except ValueError:
            pass
    head.normal_update()
    head.faces.ensure_lookup_table()
    head_tree = BVHTree.FromBMesh(head)
    from mathutils.interpolate import poly_3d_calc
    made = {"L": [], "R": []}
    for side in ("L", "R"):
        ball = bmesh.new()                                   # the eyeball as it came, not the thinned one: its true shape
        twin2 = {}
        for f in eyeballs:
            if (f.calc_center_median().x >= 0) != (side == "L"):
                continue
            for v in f.verts:
                if v not in twin2:
                    twin2[v] = ball.verts.new(v.co)
            try:
                ball.faces.new([twin2[v] for v in f.verts])
            except ValueError:
                pass
        ball_tree = BVHTree.FromBMesh(ball)
        pts = [v.co.copy() for v in ball.verts]
        c = sum(pts, Vector()) / len(pts)
        seen = []                                            # the part of the eyeball that shows: outside the head's surface
        for p in pts:
            at, normal, _i, _d = head_tree.find_nearest(p)
            if at is not None and (p - at).dot(normal) > 0.0002:
                seen.append((p - c).normalized())
        if len(seen) < 6:
            raise SystemExit(f"The {side} eyeball does not show outside the head: cannot place its eyelid.")
        f_ = sum(seen, Vector()).normalized()                # the way the eye looks out
        flat = np.array([list(q - f_ * q.dot(f_)) for q in seen])
        long_ = Vector(np.linalg.eigh(flat.T @ flat)[1][:, -1])     # corner to corner
        long_ = (long_ - f_ * long_.dot(f_)).normalized()
        up = f_.cross(long_).normalized()
        if up.z < 0:
            up, long_ = -up, -long_

        # The lid is a segment of a shell round the eyeball, like a segment of an orange, running from one corner of
        # the eye to the other. (Half a shell was tried first: it cannot cover the corners, which lie on the axis it
        # turns about.) Angles are measured round the eye's long axis, from straight out (0) upwards.
        angles = [math.degrees(math.atan2(q.dot(up), q.dot(f_))) for q in seen]
        low, high = min(angles) - LID_MARGIN, max(angles) + LID_MARGIN
        wide = high - low                                    # the lid is as wide as the showing eye, and a margin each side
        axis = up.cross(f_).normalized()                     # turning about this, by a positive angle, shuts the eye

        def at_shell(direction):
            hit = ball_tree.ray_cast(c, direction)[0]
            reach = (hit - c).length if hit is not None else sum((p - c).length for p in pts) / len(pts)
            return c + direction * (reach * LID_LARGER + LID_GAP)

        ends = [bm.verts.new(at_shell(long_)), bm.verts.new(at_shell(-long_))]      # the eye's two corners
        middle = bm.verts.new(c)
        columns = []
        for k in range(LID_ACROSS + 1):                      # at rest it lies just above the showing eye, inside the head
            round_ = math.radians(high + wide * k / LID_ACROSS)
            out = f_ * math.cos(round_) + up * math.sin(round_)
            columns.append([bm.verts.new(at_shell(long_ * math.cos(t) + out * math.sin(t)))
                            for t in (math.pi * j / LID_ALONG for j in range(1, LID_ALONG))])
        faces = []
        for a_, b_ in zip(columns[:-1], columns[1:]):
            faces.append(bm.faces.new((ends[0], a_[0], b_[0])))
            for j in range(len(a_) - 1):
                faces.append(bm.faces.new((a_[j], a_[j + 1], b_[j + 1], b_[j])))
            faces.append(bm.faces.new((ends[1], b_[-1], a_[-1])))
        for column in (columns[0], columns[-1]):             # its two flat sides, to the middle of the eyeball: a closed piece
            line = [ends[0]] + column + [ends[1]]
            for j in range(len(line) - 1):
                faces.append(bm.faces.new((middle, line[j], line[j + 1])))
        rings = columns
        top = ends[0]
        open_at, shut_at = wide, 0.0
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        # the fur's colour: one flat spot of the texture, read on the brow just above the eye
        brow, _n, index, _d = head_tree.find_nearest(c + (f_ * 0.9 + up * 0.9).normalized() * ((pts[0] - c).length * 1.25))
        face = back[index]
        weights = poly_3d_calc([v.co for v in face.verts], brow)
        spot = sum((l[layer].uv * w_ for l, w_ in zip(face.loops, weights)), Vector((0, 0)))
        for f in faces:
            f.material_index = 0
            f.smooth = True
            for l in f.loops:
                l[layer].uv = spot
        made[side] = ends + [middle] + [v for ring in rings for v in ring]
        showing = 0
        for v in made[side]:
            at, normal, _i, _d = head_tree.find_nearest(v.co)
            if at is not None and (v.co - at).dot(normal) > 0.0003:
                showing += 1
        ob["kn_eyelid_" + side] = [*c, *(c + axis * 0.02)]
        ob["kn_lid_turn_" + side] = float(wide)
        lid_report[side] = {"eye showing vertices": len(seen), "showing from (degrees)": round(low + LID_MARGIN, 1), "to": round(high - LID_MARGIN, 1),
                            "turn to shut (degrees)": round(wide, 1), "lid vertices outside the head at rest": showing}
        ball.free()
    head.free()
    bm.verts.index_update()
    lid_index = {side: [v.index for v in made[side]] for side in made}
    bm.to_mesh(me)
    bm.free()
    full.free()
    skin.free()
    for side in ("L", "R"):
        ob.vertex_groups.new(name="eyelid." + side).add(lid_index[side], 1.0, "REPLACE")

    islands = 0
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.loops.layers.uv.active
    seen = set()
    for f in bm.faces:
        if f in seen:
            continue
        islands += 1
        stack = [f]
        seen.add(f)
        while stack:
            cur = stack.pop()
            for l in cur.loops:
                for o in l.vert.link_loops:
                    if o.face not in seen and (o[layer].uv - l[layer].uv).length < 1e-5 and any(e in o.face.edges and not e.seam for e in cur.edges):
                        seen.add(o.face)
                        stack.append(o.face)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.free()
    print("KITSUNE25 eyelids:", lid_report)
    print("KITSUNE25 thinned: %d triangles before" % before_tris)
    print("KITSUNE25 prepared: %d triangles; faces a part %s; edges in each cut %s; UV islands %d; texture covers %.0f%% of the picture; "
          "open edges (the model's own) %d; size %s"
          % (tris, tally, cuts, islands, 100.0 * done.mean(), open_edges, tuple(round(d, 3) for d in ob.dimensions)))


main()
