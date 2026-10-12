"""Kitsune Spirit, the 50 %, 75 % and 100 % forms: the artist's own Tripo models, kept as they are.

  run_blender.py --blend SM_Pet_KitsuneSpirit_50.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/prep_kitsune_stage.py
  then  fit_rig_kitsune_stage.py (writes rig_kitsune_NN.json), pet_rig.py --landmarks rig_kitsune_NN.json, bind_kitsune_tails.py
  (not pet_from_scan.py / pet_finish.py: the owner chose the imported look over flat colours, 12 Oct 2026)

The stage is read from the file's name. The file holds the model as it came, "KitsuneSpirit50%" and so on: it is
kept, hidden and untouched, and this script starts from it each time, so it is safe to run again.

  thinned  half its triangles go; the shape is kept
  size     set by the body's length, nose to rump (STAGES "body_m"); it stands on the ground at the origin
  UV map   "map1", made by angle (Blender's Smart UV Project). NOT the painter's layout the 25 % form has.
  texture  the model's own colours carried over to the new map from the full model:  T_Pet_KitsuneSpirit_NN.png
  glow     T_Pet_KitsuneSpirit_NN_Emission.png, on black: the tails (faint at the root, full at the tip), the eyes,
           and on the 100 % form the flame tufts and the fire painted on the body. The 100 % form's tails are different (the owner, 12 Oct 2026): a
           faint cool light all over, for an ethereal rim (the rim is the material's own), and fire at the tips
  tails    every tail is a piece of mesh of its own, or two or three joined at the root. Each tail is found from
           its tip; the line down its middle is stored on the object as "kn_tails" (for fit_rig_kitsune_stage.py),
           and which tail each vertex belongs to as the attribute "kn_tail_id" (for bind_kitsune_tails.py)
  eyes     50 % only ("eye_whole"): its eyes are a pale green, and picking the glow by colour lit only the bright
           part of each, with a ragged edge and dark holes (the owner, 12 Oct 2026: "it isn't clean"). So there the
           eye is everything inside the outline drawn tight round that bright part, seen from straight in front of
           the eye (the fox's right eye takes the left one's outline, mirrored), and it is lit in the eye's own
           colour, palest in the middle, deeper and dimmer towards a soft edge: nothing outside the socket. A third bake holds
           where on the model each texel lies, to do it. (Tried and dropped: painting the eye afresh in the
           texture; laying a clean eye-shaped piece over it; lighting whatever near it was green, which also lit
           the pale fur under the eye.)
  eyelids  the eyes are painted on the head (no eyeballs). Each gets an eyelid: a smooth eye-shaped sheet, a little
           larger than the painted eye, draped on the head over it, in the colour of the fur round the eye. It
           hangs on an axle from corner to corner of the eye, inside the head, and rests swung up about it, out of
           sight inside the head. Vertex groups eyelid.L / eyelid.R; pet_rig.py makes the eyelid bones (they lie
           along the axle) from kn_eyelid_L / _R. TO BLINK: turn the eyelid control about its own Y by
           kn_lid_turn_L / _R degrees to shut the eye (0 = open); any turn between is a half-shut eye.
  100 %    the tails have a material of their own, "Tails", so that Unity can draw them half see-through; the
           flame tufts are the vertex group "kn_fire"
"""
import heapq
import json
import math
import random
import re
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
PICTURE = 2048
THIN = 0.5
TAIL_BONES = 5
LID_ACROSS, LID_ALONG = 6, 10           # the eyelid's faces: from top to bottom, and from corner to corner
LID_SPECK = 0.05                        # how small the eyelid is kept, inside the head, until a blink
# "body_m" is the body's length, nose to rump. The owner, 12 Oct 2026: each form is 0.1 bigger than the one before
# (the 25 % form's body is 0.40 m).
# measures below are in the model's own units as it came (facing -y, standing on z = 0)
STAGES = {
    "50": {"body_m": 0.50, "tails": 3, "head": (-0.36, 0.44), "fire": [],
           "eye": (0.22, 0.36, 0.97, 1.03), "eye_whole": True, "eye_mirror": True},          # its eyes are a pale green: greener than they are red
    "75": {"body_m": 0.60, "tails": 6, "head": (-0.33, 0.50), "fire": [], "eye": (0.3, 0.45, 0.84, 0.92)},
    "100": {"body_m": 0.70, "tails": 9, "head": (-0.55, 0.66), "tails_see_through": 0.6,
            "tail_fire": True, "tail_rim": (0.72, 0.88, 1.0),
            # the flame tufts: (name, y from, y to, z from, z to), on both sides
            "fire": [("shoulder", -0.34, -0.10, 0.655, 0.92), ("foreleg", -0.33, -0.215, 0.19, 0.39),
                     ("hindleg", 0.10, 0.215, 0.18, 0.39)]},
}


def islands_of(bm):
    seen, out = {}, []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, group = [v], []
        seen[v.index] = len(out)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen[o.index] = len(out)
                    stack.append(o)
        out.append(group)
    return out


def walk(sources, allowed):
    """Distance along the surface from the nearest of `sources` to every vertex of `allowed` (a set of indices)."""
    dist = {v.index: 0.0 for v in sources}
    heap = [(0.0, v.index, v) for v in sources]
    heapq.heapify(heap)
    while heap:
        d, _i, v = heapq.heappop(heap)
        if d > dist.get(v.index, 1e18):
            continue
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index not in allowed:
                continue
            nd = d + e.calc_length()
            if nd < dist.get(o.index, 1e18):
                dist[o.index] = nd
                heapq.heappush(heap, (nd, o.index, o))
    return dist


def main():
    stage = re.search(r"_(\d+)\.blend$", bpy.data.filepath).group(1)
    spec = STAGES[stage]
    src = bpy.data.objects["KitsuneSpirit%s%%" % stage]
    name = "SM_Pet_KitsuneSpirit_" + stage
    for o in list(bpy.data.objects):
        if o is not src:
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    src.hide_set(False)
    src.hide_render = False

    low = src.copy()
    low.data = src.data.copy()
    low.name = name
    low.data.name = name
    bpy.context.scene.collection.objects.link(low)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = low
    low.select_set(True)
    low.modifiers.new("thin", "DECIMATE").ratio = THIN
    bpy.ops.object.modifier_apply(modifier="thin")
    me = low.data

    # ---- a small piece that is a single sheet (a paper tag) is given a back: seen from behind it was not there
    bm = bmesh.new()
    bm.from_mesh(me)
    backed = 0
    for g in sorted(islands_of(bm), key=lambda g: -len(g))[1:]:
        faces = list({f for v in g for f in v.link_faces})
        edges = {e for f in faces for e in f.edges}
        if len(faces) <= 80 and sum(1 for e in edges if len(e.link_faces) == 1) >= 0.2 * len(edges):
            made = bmesh.ops.duplicate(bm, geom=faces)
            bmesh.ops.reverse_faces(bm, faces=[x for x in made["geom"] if isinstance(x, bmesh.types.BMFace)])
            backed += 1
    # ... and so is any other face that can be seen from behind from outside the model (an open edge of the import)
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    look_tree = BVHTree.FromBMesh(bm)
    reach = max((v.co.length for v in bm.verts)) * 4.0
    ways = []
    for i in range(160):
        z = 1 - 2 * (i + 0.5) / 160
        r_ = math.sqrt(1 - z * z)
        ways.append(Vector((r_ * math.cos(i * 2.399963), r_ * math.sin(i * 2.399963), z)))
    by_place = {}
    for f in bm.faces:
        by_place.setdefault(tuple(round(c, 5) for c in f.calc_center_median()), []).append(f)
    shown = []
    in_tail = {v.index for g in islands_of(bm) if min(x.co.y for x in g) > 0.0 for v in g}      # (not in the tails: a
    # loose scrap of mesh there would be taken for a tail of its own)
    for f in list(bm.faces):
        c = f.calc_center_median()
        if f.verts[0].index in in_tail:
            continue
        if any(g is not f and g.normal.dot(f.normal) < -0.9 for g in by_place[tuple(round(x, 5) for x in c)]):
            continue
        count = 0
        for d in ways:
            _hit, normal, index, _dist = look_tree.ray_cast(c + d * reach, -d)
            if index == f.index and normal.dot(d) < -0.15:
                count += 1
        if count >= 2:
            shown.append(f)
    if shown:
        made = bmesh.ops.duplicate(bm, geom=shown)
        bmesh.ops.reverse_faces(bm, faces=[x for x in made["geom"] if isinstance(x, bmesh.types.BMFace)])
    bm.to_mesh(me)
    bm.free()
    print("  single sheets given a back: %d; other faces seen from behind given a back: %d" % (backed, len(shown)))

    # ---- the parts
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    isl = sorted(islands_of(bm), key=lambda g: -len(g))
    body = isl[0]
    body_ids = {v.index for v in body}
    tails_isl = [g for g in isl[1:] if min(v.co.y for v in g) > 0.0]
    body_len = max(v.co.y for v in body) - min(v.co.y for v in body)
    scale = spec["body_m"] / body_len
    height = max(v.co.z for v in bm.verts)
    body_bm = bmesh.new()
    body_bm.from_mesh(me)
    bmesh.ops.delete(body_bm, geom=[v for v in body_bm.verts if v.index not in body_ids], context="VERTS")
    body_tree = BVHTree.FromBMesh(body_bm)

    def inside_body(p):
        hit = body_tree.find_nearest(p)
        return hit[0] is not None and (p - hit[0]).dot(hit[1]) < 0.0

    # ---- the tails: tips, which tail each vertex is of, and the line down each one's middle
    tail_id = [0] * len(bm.verts)
    ramp = [0.0] * len(bm.verts)
    per_island = []                                          # [island ids, [(share, tip vertex, distance from tip)]]
    for g in tails_isl:
        ids = {v.index for v in g}
        y0, y1 = min(v.co.y for v in g), max(v.co.y for v in g)          # every tail leaves the rump at its front end
        roots = [v for v in g if v.co.y < y0 + 0.06 * (y1 - y0)]
        d_root = walk(roots, ids)
        first = max(g, key=lambda v: d_root.get(v.index, 0.0))
        longest = d_root[first.index]
        tips = [(9.0, first, walk([first], ids))]
        while len(tips) < 6:
            def score(v):       # how far v is along a branch of its own, off the way from the root to each tip found
                return min((d_root.get(v.index, 0.0) + d.get(v.index, 0.0) - d_root[t.index]) / 2 for _s, t, d in tips)
            nxt = max(g, key=score)
            if score(nxt) < 0.12 * longest:
                break
            tips.append((score(nxt) / longest, nxt, walk([nxt], ids)))
        per_island.append((ids, tips))
    # a ragged tip also branches; the tails are the branches that run longest on their own
    shares = sorted((t[0] for _ids, tips in per_island for t in tips), reverse=True)
    least = shares[spec["tails"] - 1]
    found = []                                               # (tip vertex, island ids, distance from tip, the island's tips)
    for ids, tips in per_island:
        tips = [(t, d) for share, t, d in tips if share >= least]
        for tip, d in tips:
            found.append((tip, ids, d, tips))
    print("  tail branches, as a share of the tail's length:", [round(x, 2) for x in shares if x < 9.0])
    assert len(found) == spec["tails"], "found %d tails, expected %d" % (len(found), spec["tails"])
    found.sort(key=lambda f: (-round(f[0].co.x, 2), f[0].co.z))        # from the fox's left (+x) to its right
    chains = []
    for k, (tip, ids, d, tips) in enumerate(found, 1):
        mine = [bm.verts[i] for i in ids if all(d.get(i, 1e9) <= d2.get(i, 1e9) for _t, d2 in tips)]
        far = max(d.get(v.index, 0.0) for v in mine)
        bands = 14
        line = [tip.co.copy()]
        for b in range(bands):
            lo, hi = far * b / bands, far * (b + 1) / bands
            ring = [v.co for v in mine if lo <= d.get(v.index, 0.0) < hi]
            if len(ring) >= 3:
                c = sum(ring, Vector()) / len(ring)
                line.append(c)
        line.reverse()                                       # root to tip
        for _ in range(2):                                   # ease the kinks out
            line = [line[0]] + [(line[i - 1] + line[i] * 2 + line[i + 1]) / 4 for i in range(1, len(line) - 1)] + [line[-1]]
        lengths = [0.0]
        for i in range(1, len(line)):
            lengths.append(lengths[-1] + (line[i] - line[i - 1]).length)
        joints = []
        for j in range(TAIL_BONES + 1):
            want = lengths[-1] * j / TAIL_BONES
            i = max(1, next(i for i in range(1, len(line)) if lengths[i] >= want - 1e-9))
            t = (want - lengths[i - 1]) / max(lengths[i] - lengths[i - 1], 1e-9)
            joints.append(line[i - 1].lerp(line[i], t))
        chains.append(joints)
        for v in mine:
            tail_id[v.index] = k
            ramp[v.index] = 1.0 - d.get(v.index, far) / far

    # ---- what glows, as a colour on each vertex: red the tails, green "this is the head", blue the fire
    head_y, head_z = spec["head"]
    glow = me.color_attributes.new("kn_glow", "FLOAT_COLOR", "POINT")
    spot_at = me.color_attributes.new("kn_where", "FLOAT_COLOR", "POINT")     # where the vertex is, as a colour
    box_lo = Vector([min(v.co[k] for v in bm.verts) for k in range(3)])
    box_size = Vector([max(v.co[k] for v in bm.verts) - box_lo[k] for k in range(3)])
    for v in bm.verts:
        spot_at.data[v.index].color = (*[(v.co[k] - box_lo[k]) / box_size[k] for k in range(3)], 1.0)
    glow = me.color_attributes["kn_glow"]
    fire_ids = {}
    rnd = random.Random(7)
    rough = [rnd.random() for _ in bm.verts]
    rough = [(rough[v.index] * 2 + sum(rough[e.other_vert(v).index] for e in v.link_edges)) / (2 + len(v.link_edges)) for v in bm.verts]
    for v in bm.verts:
        r = (0.12 + 0.88 * ramp[v.index] ** 1.3) if tail_id[v.index] else 0.0
        g = 1.0 if (not tail_id[v.index] and v.co.y < head_y and v.co.z > head_z) else 0.0
        if tail_id[v.index]:
            g = 0.45 * rough[v.index]                        # on a tail: an unevenness, for the ragged edge of the fire
        b = 0.0
        if v.index in body_ids:
            b = 0.5
            for tag, y0, y1, z0, z1 in spec["fire"]:
                if y0 <= v.co.y <= y1 and z0 <= v.co.z <= z1:
                    b = 1.0
                    fire_ids.setdefault(tag, []).append(v.index)
        glow.data[v.index].color = (r, g, b, 1.0)
    attr = me.attributes.new("kn_tail_id", "INT", "POINT")
    for i, k in enumerate(tail_id):
        attr.data[i].value = k
    tail_faces = {f.index for f in bm.faces if tail_id[f.verts[0].index]}
    bm.free()
    body_bm.free()
    if fire_ids:
        vg = low.vertex_groups.new(name="kn_fire")
        for tag, ids in fire_ids.items():
            vg.add(ids, 1.0, "REPLACE")

    # ---- the UV map, the texture and the glow picture
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="map1")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    for im in list(bpy.data.images):
        if im.name.startswith("T_Pet_KitsuneSpirit_" + stage):
            bpy.data.images.remove(im)
    img = bpy.data.images.new("T_Pet_KitsuneSpirit_" + stage, PICTURE, PICTURE)
    marks = bpy.data.images.new("kn_marks", PICTURE, PICTURE, is_data=True)
    for m in list(bpy.data.materials):
        if m.name.startswith(name + " "):
            bpy.data.materials.remove(m)

    emission = bpy.data.images.new("T_Pet_KitsuneSpirit_%s_Emission" % stage, PICTURE, PICTURE)

    def material(label, alpha=1.0, rim=None):
        mat = bpy.data.materials.new(name + " " + label)
        mat.use_nodes = True
        nt = mat.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Alpha"].default_value = alpha
        if alpha < 1.0:
            mat.surface_render_method = "BLENDED"
        glow_tex = nt.nodes.new("ShaderNodeTexImage")
        glow_tex.image = emission
        if rim:                                              # a pale light on the edges that face away: Unity's rim, previewed
            edge = nt.nodes.new("ShaderNodeLayerWeight")
            edge.inputs["Blend"].default_value = 0.5
            tint = nt.nodes.new("ShaderNodeMixRGB")
            tint.blend_type = "MULTIPLY"
            tint.inputs[0].default_value = 1.0
            tint.inputs[2].default_value = (*rim, 1.0)
            add = nt.nodes.new("ShaderNodeMixRGB")
            add.blend_type = "ADD"
            add.inputs[0].default_value = 1.0
            thin = nt.nodes.new("ShaderNodeMath")            # thin and soft: only where the surface turns well away
            thin.operation = "POWER"
            thin.inputs[1].default_value = 3.0
            nt.links.new(edge.outputs["Facing"], thin.inputs[0])
            half = nt.nodes.new("ShaderNodeMath")
            half.operation = "MULTIPLY"
            half.inputs[1].default_value = 0.55
            nt.links.new(thin.outputs[0], half.inputs[0])
            nt.links.new(half.outputs[0], tint.inputs[1])
            nt.links.new(glow_tex.outputs["Color"], add.inputs[1])
            nt.links.new(tint.outputs["Color"], add.inputs[2])
            nt.links.new(add.outputs["Color"], bsdf.inputs["Emission Color"])
        else:
            nt.links.new(glow_tex.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 2.0
        nt.nodes.active = tex
        return mat

    me.materials.clear()
    me.materials.append(material("Body"))
    if "tails_see_through" in spec:
        me.materials.append(material("Tails", spec["tails_see_through"], spec.get("tail_rim")))
        for p in me.polygons:
            p.material_index = 1 if p.index in tail_faces else 0
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    bake = sc.render.bake
    bake.use_selected_to_active = True
    bake.use_pass_direct = bake.use_pass_indirect = False
    bake.use_pass_color = True
    bake.cage_extrusion = height * 0.012
    bake.max_ray_distance = height * 0.03
    bake.margin = 12
    src.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(type="DIFFUSE")
    src.select_set(False)
    # the marks: the same faces, lit only by the vertex colour
    mark_mat = bpy.data.materials.new("kn_marks")
    mark_mat.use_nodes = True
    nt = mark_mat.node_tree
    nt.nodes.clear()
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "kn_glow"
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = marks
    nt.links.new(col.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    nt.nodes.active = tex
    kept = list(me.materials)
    slots = [p.material_index for p in me.polygons]
    me.materials.clear()
    me.materials.append(mark_mat)
    bake.use_selected_to_active = False
    bpy.ops.object.bake(type="EMIT")
    where = bpy.data.images.new("kn_where", PICTURE, PICTURE, is_data=True, float_buffer=True)
    tex.image = where
    col.layer_name = "kn_where"
    bpy.ops.object.bake(type="EMIT")
    me.materials.clear()
    for m in kept:
        me.materials.append(m)
    for p, s in zip(me.polygons, slots):
        p.material_index = s
    bpy.data.materials.remove(mark_mat)

    px = np.array(img.pixels[:], dtype=np.float32).reshape(PICTURE, PICTURE, 4)
    mk = np.array(marks.pixels[:], dtype=np.float32).reshape(PICTURE, PICTURE, 4)
    rgb = px[..., :3]
    hi, lo_ = rgb.max(axis=2), rgb.min(axis=2)
    sat = (hi - lo_) / np.maximum(hi, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    def step(x, a, c):
        return np.clip((x - a) / (c - a), 0.0, 1.0)

    # the eyes: bright, strongly coloured yellow to green (green not far below red, blue well below both), on the head
    sat0, sat1, gr0, gr1 = spec.get("eye", (0.45, 0.6, 0.78, 0.88))
    eye = step(sat, sat0, sat1) * step(hi, 0.5, 0.65) * step(g / np.maximum(r, 1e-6), gr0, gr1) * (mk[..., 1] > 0.5)
    # painted fire (the owner, 12 Oct 2026: the fire painted on the shoulders, chest, legs and haunches glows too):
    # brighter than the fur, a little yellower, and not the white of the ruff. Not on the head.
    flame = step(hi, 0.87, 0.92) * step(sat, 0.16, 0.24) * step(g / np.maximum(r, 1e-6), 0.685, 0.72) * (mk[..., 1] < 0.5)
    fire = np.where(mk[..., 2] > 0.75, np.maximum(flame, 0.65), np.where(mk[..., 2] > 0.25, flame * 0.9, 0.0)) if spec["fire"] else 0.0
    at = np.array(where.pixels[:], dtype=np.float32).reshape(PICTURE, PICTURE, 4)[..., :3] * np.array(box_size, dtype=np.float32) \
        + np.array(box_lo, dtype=np.float32)
    bpy.data.images.remove(where)
    eye_fill = None
    if spec.get("eye_whole"):
        # The whole eye, and nothing outside its socket: the bright part found by colour is ragged and has holes,
        # but it does reach the eye's true edge all round. So the eye is taken as everything inside the outline
        # drawn tight round that bright part (seen flat, from straight in front of the eye), and is lit evenly in
        # the eye's own colour. (Taking in whatever near it was green was tried first: it also lit the pale fur
        # under the eye, outside the socket.)
        whole = np.zeros_like(eye)
        deep_in = np.zeros_like(eye)
        lit_ = (eye > 0.5)
        eye_fill = np.median(rgb[lit_ & (hi > np.percentile(hi[lit_], 40.0))], axis=0)
        for sign in (1.0, -1.0):
            core = lit_ & (at[..., 0] * sign > 0.0)
            if core.sum() < 20:
                continue
            pts3 = at[core].astype(np.float64)
            middle_ = np.median(pts3, axis=0)
            far_ = np.linalg.norm(pts3 - middle_, axis=1)
            pts3 = pts3[far_ < np.percentile(far_, 98.0)]     # a stray speck must not stretch the outline
            reach = np.percentile(np.linalg.norm(pts3 - middle_, axis=1), 90.0)
            _w, axes = np.linalg.eigh(np.cov((pts3 - middle_).T))
            out_, tall_, long__ = axes[:, 0], axes[:, 1], axes[:, 2]
            flat2 = np.c_[(pts3 - middle_) @ long__, (pts3 - middle_) @ tall_]
            order = flat2[np.lexsort((flat2[:, 1], flat2[:, 0]))]

            def half_hull(points):
                hull = []
                for q in points:
                    while len(hull) >= 2 and (hull[-1][0] - hull[-2][0]) * (q[1] - hull[-2][1]) - (hull[-1][1] - hull[-2][1]) * (q[0] - hull[-2][0]) <= 0:
                        hull.pop()
                    hull.append(q)
                return hull
            ring = half_hull(order)[:-1] + half_hull(order[::-1])[:-1]          # anticlockwise
            near_ = np.linalg.norm(at - middle_, axis=2) < 2.5 * reach
            rel = (at[near_] - middle_).astype(np.float64)
            x2, y2, depth = rel @ long__, rel @ tall_, rel @ out_
            inside = np.abs(depth) < 0.7 * reach
            # ... and on towards the eye's two corners, where the painted eye is in shadow and was not picked up:
            # there, only what is green (the dark rim and the fur are not)
            cx_, cy_ = float(np.mean([q[0] for q in ring])), float(np.mean([q[1] for q in ring]))
            x2w, y2w = cx_ + (x2 - cx_) / 1.5, cy_ + (y2 - cy_) / 1.06
            wider = inside.copy()
            for k in range(len(ring)):
                (ax_, ay_), (bx_, by_) = ring[k], ring[(k + 1) % len(ring)]
                inside &= (bx_ - ax_) * (y2 - ay_) - (by_ - ay_) * (x2 - ax_) >= 0.0
                wider &= (bx_ - ax_) * (y2w - ay_) - (by_ - ay_) * (x2w - ax_) >= 0.0
            green = (g[near_] > 0.95 * r[near_]) & (sat[near_] > 0.12) & (hi[near_] > 0.08)
            picked = inside | (wider & green)
            # That shape is drawn afresh with a smooth edge and no holes: laid on a small grid as seen from in front
            # of the eye, gaps closed, holes filled, the edge rounded off; each texel then asks the grid.
            def redraw(xq, yq, pick):
                """The picked points' shape drawn afresh: how far inside its soft edge each point is, and how deep in."""
                cells = 72
                gx0, gx1 = xq[pick].min(), xq[pick].max()
                gy0, gy1 = yq[pick].min(), yq[pick].max()
                size_ = (gx1 - gx0) / cells
                pad_ = 6
                nx, ny = cells + 2 * pad_, int((gy1 - gy0) / size_) + 1 + 2 * pad_
                field = np.zeros((ny, nx), dtype=np.float32)
                ix = np.clip(((xq[pick] - gx0) / size_).astype(int) + pad_, 0, nx - 1)
                iy = np.clip(((yq[pick] - gy0) / size_).astype(int) + pad_, 0, ny - 1)
                field[iy, ix] = 1.0

                def spread(f_, grow):
                    out = f_.copy()
                    for dy_, dx_ in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                        moved = np.roll(np.roll(f_, dy_, axis=0), dx_, axis=1)
                        out = np.maximum(out, moved) if grow else np.minimum(out, moved)
                    return out
                for _ in range(3):
                    field = spread(field, True)
                for _ in range(3):
                    field = spread(field, False)
                outside = np.zeros_like(field, dtype=bool)        # what can be reached from the border is not a hole
                stack = [(0, 0)]
                outside[0, 0] = True
                while stack:
                    cy__, cx__ = stack.pop()
                    for dy_, dx_ in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                        yy, xx = cy__ + dy_, cx__ + dx_
                        if 0 <= yy < ny and 0 <= xx < nx and not outside[yy, xx] and field[yy, xx] < 0.5:
                            outside[yy, xx] = True
                            stack.append((yy, xx))
                field = np.where(outside, 0.0, 1.0).astype(np.float32)
                for _ in range(2):                                # a nick or a spur of a cell or two goes
                    field = spread(field, False)
                for _ in range(2):
                    field = spread(field, True)
                for _ in range(6):                                # the edge rounded off
                    field = (field * 4 + np.roll(field, 1, 0) + np.roll(field, -1, 0) + np.roll(field, 1, 1) + np.roll(field, -1, 1)) / 8.0
                fx = np.clip((xq - gx0) / size_ + pad_ - 0.5, 0, nx - 1.001)
                fy = np.clip((yq - gy0) / size_ + pad_ - 0.5, 0, ny - 1.001)
                x_, y_ = fx.astype(int), fy.astype(int)
                tx, ty = fx - x_, fy - y_
                value = (field[y_, x_] * (1 - tx) * (1 - ty) + field[y_, x_ + 1] * tx * (1 - ty)
                         + field[y_ + 1, x_] * (1 - tx) * ty + field[y_ + 1, x_ + 1] * tx * ty)
                broad = field.copy()                              # the same shape, blurred wide: how far in from the edge
                for _ in range(16):
                    broad = (broad * 4 + np.roll(broad, 1, 0) + np.roll(broad, -1, 0) + np.roll(broad, 1, 1) + np.roll(broad, -1, 1)) / 8.0
                far_in = (broad[y_, x_] * (1 - tx) * (1 - ty) + broad[y_, x_ + 1] * tx * (1 - ty)
                          + broad[y_ + 1, x_] * (1 - tx) * ty + broad[y_ + 1, x_ + 1] * tx * ty)
                kept.clear()
                kept.extend((field, gx0, gy0, size_))
                return value, far_in

            kept = []
            value, far_in = redraw(x2, y2, picked)
            field0_, gx0_, gy0_, size0_ = kept
            good = np.abs(depth) < 0.7 * reach
            patch = np.where(good, step(value, 0.3, 0.7), 0.0).astype(np.float32)      # a soft edge, not a stepped one
            deep_in[near_] = np.maximum(deep_in[near_], np.where(good, far_in, 0.0).astype(np.float32))
            whole[near_] = np.maximum(whole[near_], patch)
            if spec.get("eye_mirror"):
                # The other eye takes this one's shape, mirrored: on the 50 % form the fox's right eye is built
                # differently (a loose eyeball in the socket) and its own shape came out broken.
                flip = np.array([-1.0, 1.0, 1.0])
                middle2 = middle_ * flip
                near2 = np.linalg.norm(at - middle2, axis=2) < 2.5 * reach
                rel2 = (at[near2] - middle2).astype(np.float64)
                xm, ym, dm = rel2 @ (long__ * flip), rel2 @ (tall_ * flip), rel2 @ (out_ * flip)
                fx = np.clip((xm - gx0_) / size0_ + 6 - 0.5, 0, field0_.shape[1] - 1.001)
                fy = np.clip((ym - gy0_) / size0_ + 6 - 0.5, 0, field0_.shape[0] - 1.001)
                x_, y_ = fx.astype(int), fy.astype(int)
                tx, ty = fx - x_, fy - y_
                first = (field0_[y_, x_] * (1 - tx) * (1 - ty) + field0_[y_, x_ + 1] * tx * (1 - ty)
                         + field0_[y_ + 1, x_] * (1 - tx) * ty + field0_[y_ + 1, x_ + 1] * tx * ty)
                # ... but never onto fur (the two sockets differ a little), and then drawn afresh as the first was,
                # so that what the fur cut away leaves a smooth edge and not a ragged one
                fur_ = (g[near2] < 0.93 * r[near2]) & (hi[near2] > 0.35)
                good = np.abs(dm) < 1.1 * reach
                value, far_in = redraw(xm, ym, (first > 0.5) & good & ~fur_)
                patch2 = np.where(good, step(value, 0.3, 0.7), 0.0).astype(np.float32)
                deep_in[near2] = np.maximum(deep_in[near2], np.where(good, far_in, 0.0).astype(np.float32))
                whole[near2] = np.maximum(whole[near2], patch2)
                break
        whole *= (mk[..., 1] > 0.5)
        eye = whole if spec.get("eye_mirror") else np.maximum(eye, whole)
    strength = np.maximum(np.maximum(mk[..., 0], eye), fire)
    out_px = np.zeros_like(px)
    out_px[..., :3] = rgb * strength[..., None]
    if eye_fill is not None:
        # The eye lit in its own colour, as the 75 % and 100 % forms' painted eyes are lit: palest and brightest in
        # the middle, a deeper yellow and a little dimmer towards the edge, and fading out at the edge itself.
        # (Lit flat and full to a hard edge it looked cut out, and the edge showed the texels as steps.)
        middle_w = step(deep_in, 0.55, 0.97)
        edge_rgb = eye_fill * np.array([1.0, 0.90, 0.45], dtype=np.float32)
        colour = edge_rgb + (eye_fill - edge_rgb) * middle_w[..., None]
        shown = eye > 0.02
        out_px[shown, :3] = (colour * ((0.72 + 0.28 * middle_w) * eye)[..., None])[shown]
    if spec["fire"]:                                         # the body's fire is painted pale: warm it towards orange
        warm = np.asarray(fire)[..., None] * (strength == np.asarray(fire))[..., None]
        out_px[..., :3] *= 1.0 - warm * (1.0 - np.array([1.0, 0.74, 0.42], dtype=np.float32))
    if spec.get("tail_fire"):
        # the tails of the 100 % form: a faint cool light all over (the rim itself is the material's), and fire at
        # the tips, as the 75 % form's tails are painted: deep orange where it starts, pale yellow at the very tip,
        # with a ragged edge
        on_tail = mk[..., 0] > 0.06
        along = np.clip((mk[..., 0] - 0.12) / 0.88, 0.0, 1.0) ** (1.0 / 1.3)
        along = along + 0.42 * (mk[..., 1] / 0.45 - 0.5)
        burn = step(along, 0.56, 0.63)
        hot = step(along, 0.66, 1.0)[..., None]
        flame_rgb = np.array([1.0, 0.36, 0.07], dtype=np.float32) * (1.0 - hot) + np.array([1.0, 0.86, 0.5], dtype=np.float32) * hot
        cool = np.array(spec["tail_rim"], dtype=np.float32) * 0.16
        tail_rgb = np.maximum(cool[None, None, :] * (0.6 + 0.4 * rgb), flame_rgb * burn[..., None])
        out_px[..., :3] = np.where(on_tail[..., None], tail_rgb, out_px[..., :3])
    out_px[..., 3] = 1.0
    bpy.data.images.remove(marks)

    # ---- the eyelids. These models have no eyeballs: the eyes are painted on the head. So each lid is a thin piece
    # of the head's own surface, lifted a hair off it, in the fur's colour. Shut, it lies exactly over the painted
    # eye. It is modelled turned up above the eye (about the middle of the head's curve there) and shrunk to a speck
    # inside the head, where it stays; a blink grows it (kn_lid_grow) and turns it down (kn_lid_turn degrees).
    from mathutils.interpolate import poly_3d_calc
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bm.verts.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    tree = BVHTree.FromBMesh(bm)
    ids = [d.value for d in me.attributes["kn_tail_id"].data]
    spots = {"L": [], "R": []}                               # points of the head's surface that are painted eye
    owner = {}                                               # ... and the face each lies on
    tried, painted = {}, {}                                  # per face: points tried, points that are eye
    for f in bm.faces:
        if any(ids[v.index] or v.co.y >= head_y or v.co.z <= head_z for v in f.verts):
            continue
        loops = list(f.loops)
        for k in range(1, len(loops) - 1):
            tri = (loops[0], loops[k], loops[k + 1])
            for i in range(7):
                for j in range(7 - i):
                    w = ((i + 0.33) / 7.0, (j + 0.33) / 7.0)
                    w = (w[0], w[1], 1.0 - w[0] - w[1])
                    at = sum((l[uv].uv * x for l, x in zip(tri, w)), Vector((0.0, 0.0)))
                    tried[f] = tried.get(f, 0) + 1
                    if eye[min(PICTURE - 1, int(at.y * PICTURE)), min(PICTURE - 1, int(at.x * PICTURE))] > 0.5:
                        co = sum((l.vert.co * x for l, x in zip(tri, w)), Vector())
                        spots["L" if co.x > 0 else "R"].append((co, f.normal.copy()))
                        owner[co.to_tuple(6)] = f
                        painted[f] = painted.get(f, 0) + 1
    lid_verts, lid_report = {}, {}
    head_verts = [v for v in bm.verts if not ids[v.index]]      # as the model is now, before any lid is added
    head_faces = list(bm.faces)
    bm.normal_update()
    for side in ("L", "R"):
        pts = spots[side]
        # the eye is the largest patch of eye colour that is not up in the ears (a gold ear rim can pass for eye colour)
        cell = 0.012 * height
        boxes = {}
        for item in pts:
            boxes.setdefault(tuple(int(math.floor(c / cell)) for c in item[0]), []).append(item)
        patches, left = [], set(boxes)
        while left:
            stack, patch = [left.pop()], []
            while stack:
                x, y, z = stack.pop()
                patch.extend(boxes[(x, y, z)])
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        for dz in (-1, 0, 1):
                            n = (x + dx, y + dy, z + dz)
                            if n in left:
                                left.discard(n)
                                stack.append(n)
            patches.append(patch)
        ear_from = head_z + 0.62 * (height - head_z) if "ears_from" not in spec else spec["ears_from"]
        patches = [q for q in patches if sum(i[0].z for i in q) / len(q) < ear_from] or patches
        patches.sort(key=len, reverse=True)
        print("  %s eye: patches of eye colour %s" % (side, [(len(q), tuple(round(c, 2) for c in sum((i[0] for i in q), Vector()) / len(q))) for q in patches[:5]]))
        pts = patches[0] if patches else []
        if len(pts) < 12:
            raise SystemExit("The %s eye was not found in the glow map (%d points): cannot place its eyelid." % (side, len(pts)))
        middle = sum((p for p, _n in pts), Vector()) / len(pts)
        normal = sum((n for _p, n in pts), Vector()).normalized()
        flat = np.array([list((p - middle) - normal * (p - middle).dot(normal)) for p, _n in pts])
        long_ = Vector(np.linalg.eigh(flat.T @ flat)[1][:, -1]).normalized()
        length = max(abs((p - middle).dot(long_)) for p, _n in pts) * 2.0
        # the middle of the head's curve round the eye: the lid turns about it
        near = np.array([list(v.co) for v in head_verts if (v.co - middle).length < 1.6 * length])
        radius, centre = 0.0, None
        if len(near) >= 8:
            A = np.c_[2.0 * near, np.ones(len(near))]
            sol = np.linalg.lstsq(A, (near ** 2).sum(axis=1), rcond=None)[0]
            centre = Vector(sol[:3])
            radius = math.sqrt(max(sol[3] + centre.length_squared, 0.0))
        if centre is None or not (0.9 * length < radius < 5.0 * length) or (middle - centre).dot(normal) < 0.5 * radius:
            radius = 2.2 * length
            centre = middle - normal * radius
        f_ = (middle - centre).normalized()
        long_ = (long_ - f_ * long_.dot(f_)).normalized()
        up = f_.cross(long_).normalized()
        if up.z < 0:
            up, long_ = -up, -long_
        axis = up.cross(f_).normalized()                     # turning about this, by a positive angle, shuts the eye
        seen = [(p - centre).normalized() for p, _n in pts]
        round_ = [math.atan2(q.dot(up), q.dot(f_)) for q in seen]
        along = [math.asin(max(-1.0, min(1.0, q.dot(long_)))) for q in seen]
        # The eyelid (the owner, 12 Oct 2026: the snapping lid "is jank and looks bad"). It is a smooth eye-shaped
        # sheet, a little larger than the painted eye, draped on the head over the eye: that is the eye shut. It
        # hangs on an axle that runs from corner to corner of the eye, a way behind it inside the head, and is
        # modelled swung up about that axle until it lies inside the head above the eye. To blink it swings down:
        # it comes out from under the brow and slides down over the eye, as a lid does. No growing, no snapping.
        # (Before this: the head's own faces over the eye, copied and kept as a speck until grown. It only fitted
        # fully shut, so it could only snap, and it showed as an angular patch.)
        a_lo, a_hi = min(along), max(along)
        mid_r = sum(round_) / len(round_)

        def way(a, r):
            return long_ * math.sin(a) + (f_ * math.cos(r) + up * math.sin(r)) * math.cos(a)

        xs = np.array([(q - middle).dot(long_) for q, _n in pts])
        ys = np.array([(q - middle).dot(up) for q, _n in pts])
        x0, x1 = np.percentile(xs, 1.0), np.percentile(xs, 99.0)
        xc, half = 0.5 * (x0 + x1), 0.5 * (x1 - x0) * 1.30
        y_at0 = float(np.median(ys[xs < x0 + 0.15 * (x1 - x0)]))
        y_at1 = float(np.median(ys[xs > x1 - 0.15 * (x1 - x0)]))
        inner_ = np.abs(xs - xc) < 0.3 * half
        base_c = 0.5 * (y_at0 + y_at1)
        h_top = (np.percentile(ys[inner_], 99.0) - base_c) * 1.45 + 0.003 * height
        h_bot = (base_c - np.percentile(ys[inner_], 1.0)) * 1.45 + 0.003 * height
        NU, NV = 12, 6
        gap = 0.0011 * height
        flat_, raw = [], []
        for iu in range(NU + 1):
            u_ = (-1.0 + 2.0 * iu / NU) * 0.99
            base = y_at0 + (y_at1 - y_at0) * (u_ + 1.0) / 2.0
            arch = max(1.0 - u_ ** 2, 0.06)                   # a blunt corner, not a point
            column, heights = [], []
            for iv in range(NV + 1):
                at_ = middle + long_ * (xc + half * u_) + up * (base + (-h_bot + (h_top + h_bot) * iv / NV) * arch)
                hit = tree.ray_cast(at_ + f_ * 0.8 * half, -f_)[0]
                heights.append((hit - at_).dot(f_) if hit is not None else 0.0)
                column.append(at_)
            flat_.append(column)
            raw.append(heights)
        smooth = [row[:] for row in raw]
        for _ in range(5):                                    # draped over the socket, not pressed into every dent of it
            nxt = [row[:] for row in smooth]
            for iu in range(NU + 1):
                for iv in range(NV + 1):
                    near_ = [smooth[a_][b_] for a_, b_ in ((iu - 1, iv), (iu + 1, iv), (iu, iv - 1), (iu, iv + 1)) if 0 <= a_ <= NU and 0 <= b_ <= NV]
                    nxt[iu][iv] = max(raw[iu][iv], 0.5 * smooth[iu][iv] + 0.5 * sum(near_) / len(near_))
            smooth = nxt
        shut_at = [[flat_[iu][iv] + f_ * (smooth[iu][iv] + gap) for iv in range(NV + 1)] for iu in range(NU + 1)]
        # the axle's depth and the swing: the first pair that puts the whole lid inside the head when it is up
        tall = h_top + h_bot
        best = None
        for depth in (0.9 * tall, 1.3 * tall, 1.8 * tall, 2.4 * tall):
            for turn in (70.0, 85.0, 100.0, 115.0):
                pivot = middle + up * (base_c + 0.5 * (h_top - h_bot)) - f_ * depth
                swing_up = Quaternion(axis, -math.radians(turn))
                showing = 0
                for column in shut_at:
                    for q in column:
                        r_ = pivot + swing_up @ (q - pivot)
                        near_p, near_n, _i, _d = tree.find_nearest(r_)
                        if near_p is None or (r_ - near_p).dot(near_n) > -0.0004 * height:
                            showing += 1
                if best is None or showing < best[0]:
                    best = (showing, depth, turn, pivot)
                if showing == 0:
                    break
            if best[0] == 0:
                break
        showing, depth, turn, pivot = best
        swing_up = Quaternion(axis, -math.radians(turn))
        made = [[bm.verts.new(pivot + swing_up @ (q - pivot)) for q in column] for column in shut_at]
        faces = [bm.faces.new((made[iu][iv], made[iu + 1][iv], made[iu + 1][iv + 1], made[iu][iv + 1]))
                 for iu in range(NU) for iv in range(NV)]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        if sum((f.normal for f in faces), Vector()).dot(swing_up @ f_) < 0.0:
            bmesh.ops.reverse_faces(bm, faces=faces)
        outer, inner = made, []
        # the fur's colour: read all round the eye, just outside it, and the spot nearest the middle one of those
        # colours is used (one spot read on the brow landed on a pale marking on the 75 % form)
        found = []
        for k in range(28):
            t = 2.0 * math.pi * k / 28
            ring = way(0.5 * (a_lo + a_hi) + 0.80 * (a_hi - a_lo) * math.cos(t), mid_r + 0.95 * (max(round_) - min(round_)) * math.sin(t))
            at, _n, index, _d = tree.ray_cast(centre + ring * radius * 1.3, -ring)
            if at is None:
                continue
            face = head_faces[index]
            weights = poly_3d_calc([v.co for v in face.verts], at)
            where = sum((l[uv].uv * x for l, x in zip(face.loops, weights)), Vector((0.0, 0.0)))
            colour = rgb[min(PICTURE - 1, int(where.y * PICTURE)), min(PICTURE - 1, int(where.x * PICTURE))]
            if eye[min(PICTURE - 1, int(where.y * PICTURE)), min(PICTURE - 1, int(where.x * PICTURE))] < 0.2:
                found.append((where, colour))
        typical = np.median(np.array([c for _w, c in found]), axis=0)
        spot = min(found, key=lambda wc: float(((wc[1] - typical) ** 2).sum()))[0]
        mine = [v for grid in (outer, inner) for row in grid for v in row]
        for f in faces:
            f.material_index = 0
            f.smooth = True
            for l in f.loops:
                l[uv].uv = spot
        lid_verts[side] = mine
        low["kn_eyelid_" + side] = [*(pivot * scale), *(pivot * scale + axis * 0.02)]
        low["kn_lid_turn_" + side] = turn
        lid_report[side] = "lid %.0f x %.0f mm, axle %.0f mm behind the eye, swings %.0f deg, %d of its %d points show when it is up" % (
            2 * half * scale * 1000, tall * scale * 1000, depth * scale * 1000, turn, showing, (NU + 1) * (NV + 1))
    low["kn_lid_grow"] = 1.0                                 # (the lid is no longer grown: kept for older readers of it)
    bm.verts.index_update()
    lid_index = {side: [v.index for v in lid_verts[side]] for side in lid_verts}
    bm.to_mesh(me)
    bm.free()
    for side, index in lid_index.items():
        low.vertex_groups.new(name="eyelid." + side).add(index, 1.0, "REPLACE")
    print("  eyelids:", lid_report)
    img.pixels.foreach_set(px.ravel())
    emission.pixels.foreach_set(out_px.ravel())
    for im, tag in ((img, ""), (emission, "_Emission")):
        im.filepath_raw = str(HERE / ("T_Pet_KitsuneSpirit_%s%s.png" % (stage, tag)))
        im.file_format = "PNG"
        im.save()

    # ---- size, shading, what the next steps need
    low.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(scale=True)
    for p in me.polygons:
        p.use_smooth = True
    low["kn_tails"] = json.dumps([[[round(c * scale, 4) for c in j] for j in chain] for chain in chains])
    low["kn_scale"] = scale
    low["kn_fire"] = json.dumps({tag: [round(c * scale, 4) for c in (y0, y1, z0, z1)] for tag, y0, y1, z0, z1 in spec["fire"]})
    src.hide_set(True)
    src.hide_render = True
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    dims = [round(d, 3) for d in low.dimensions]
    print("KITSUNE %s: %d triangles, %s m, scale %.4f, %d tails, eye texels %d, fire texels %d"
          % (stage, tris, dims, scale, len(chains), int((eye > 0.5).sum()), int((np.asarray(fire) > 0.3).sum()) if spec["fire"] else 0))
    for k, chain in enumerate(chains, 1):
        print("  tail %d  root (%.3f, %.3f, %.3f)  tip (%.3f, %.3f, %.3f)" % (k, *(chain[0] * scale), *(chain[-1] * scale)))


main()
