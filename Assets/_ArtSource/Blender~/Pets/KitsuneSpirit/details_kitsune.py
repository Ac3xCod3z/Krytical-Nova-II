"""Kitsune Spirit: the clean-up the owner marked on the sheet of 8 Oct 2026.

  nose    the nose shape the Tripo model had (a rounded dark grey pad with two black nostrils), rebuilt clean
  collar  Tripo's lumpy collar removed; a clean solid black band built round the neck where it sat
  tag     a clean white tag hanging from the collar with a dark mark on it; the old tag and its string removed

Order for this pet: pet_from_scan.py, pet_finish.py (sizes it to 0.6 m), pet_eyes.py, this script, pet_finish.py.
  run_blender.py --blend SM_Pet_KitsuneSpirit_S1.blend run_scene_script.py --script Assets/_ArtSource/Blender~/Pets/KitsuneSpirit/details_kitsune.py
All positions are in metres on the 0.6 m model. Safe to run again.
"""
import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

NAME = "SM_Pet_KitsuneSpirit_S1"
BLACK, WHITE, INK = (0.0, 0.0, 0.0), (1.0, 1.0, 1.0), (0.0065, 0.006, 0.024)
NOSE_GREY = (0.07, 0.07, 0.07)      # dark grey, as on the Tripo model (lands on swatch Grey 05)

COLLAR_AT, COLLAR_RADIUS = Vector((0.0, -0.166, 0.198)), 0.02
# Where Tripo's collar sat, measured from it before it was removed: the middle of the ring and the way it faces.
RING_CENTRE, RING_NORMAL = Vector((0.0, -0.1645, 0.1985)), Vector((0.0, -0.63, 0.7766))
RING_WIDTH, RING_OUT, RING_IN, RING_SEGMENTS = 0.015, 0.0045, 0.004, 24
STRING_AT, STRING_RADIUS = Vector((0.0, -0.208, 0.172)), 0.022
OLD_TAG_AT, OLD_TAG_RADIUS = Vector((0.004, -0.210, 0.141)), 0.014
NOSE_AT, NOSE_RADIUS = Vector((0.0, -0.297, 0.213)), 0.032

TAG_CENTRE, TAG_W, TAG_H, TAG_Y = Vector((0.0, 0.0, 0.139)), 0.036, 0.05, -0.2145
NOSE_Z, NOSE_W, NOSE_H = 0.2205, 0.03, 0.024


def material(ob, label, rgb):
    name = f"{ob.name} {label}"
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1.0)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.6
    names = [m.name if m else None for m in ob.data.materials]
    if name not in names:
        ob.data.materials.append(mat)
        names.append(name)
    return names.index(name)


def main():
    ob = bpy.data.objects[NAME]
    me = ob.data
    black, white, ink = material(ob, "Black", BLACK), material(ob, "White", WHITE), material(ob, "Mark", INK)
    nose = material(ob, "Nose", NOSE_GREY)

    # what an earlier run of this script made goes first
    made = ob.vertex_groups.get("kn_details")
    old = set()
    if made:
        old = {v.index for v in me.vertices if any(e.group == made.index for e in v.groups)}
        ob.vertex_groups.remove(made)

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    if old:
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in old], context="VERTS")
    bm.faces.ensure_lookup_table()

    seen, islands = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, group = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            group.append(cur)
            for e in cur.edges:
                for n in e.link_faces:
                    if n.index not in seen:
                        seen.add(n.index)
                        stack.append(n)
        islands.append(group)
    body = max(islands, key=len)

    counts = {"collar_faces": 0, "string_pieces": 0, "old_tag": 0, "nose_specks": 0}
    remove = []
    for group in islands:
        if group is body or len(group) > len(bm.faces) * 0.2:
            continue
        verts = {v for f in group for v in f.verts}
        mid = sum((v.co for v in verts), Vector()) / len(verts)
        if (mid - COLLAR_AT).length < COLLAR_RADIUS and len(group) >= 50:
            remove.extend(group)
            counts["collar_faces"] += len(group)
        elif (mid - OLD_TAG_AT).length < OLD_TAG_RADIUS:
            remove.extend(group)
            counts["old_tag"] += 1
        elif (mid - STRING_AT).length < STRING_RADIUS and len(group) < 100:
            remove.extend(group)
            counts["string_pieces"] += 1
        elif (mid - NOSE_AT).length < NOSE_RADIUS and len(group) < 20:
            remove.extend(group)
            counts["nose_specks"] += 1
    if remove:
        bmesh.ops.delete(bm, geom=list(set(remove)), context="FACES")

    # grey slivers the texture left beside the eyes, and the dark patch under the old nose: give each the
    # fur colour of the faces round it
    bm.faces.ensure_lookup_table()
    fur = [i for i, m in enumerate(me.materials) if m and m.name.endswith(("Colour 01", "Colour 02"))]
    counts["face_slivers_recoloured"] = 0
    for _pass in range(3):
        for f in bm.faces:
            if f.material_index in fur or f.material_index in (black, white, ink, nose):
                continue
            mat = me.materials[f.material_index]
            if mat is None or mat.name.endswith("Eye"):
                continue
            c = f.calc_center_median()
            near_eye = min((c - Vector((sx * 0.031, -0.25, 0.2555))).length for sx in (1, -1)) <= 0.05
            near_nose = (c - NOSE_AT).length <= 0.035      # the dark patch Tripo painted where its nose was
            if not (near_eye or near_nose):
                continue
            near = [n.material_index for e in f.edges for n in e.link_faces if n is not f and n.material_index in fur]
            if near:
                f.material_index = max(set(near), key=near.count)
                counts["face_slivers_recoloured"] += 1

    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    before = set(bm.verts)          # everything that is not made below
    bvh = BVHTree.FromBMesh(bm)

    def face(points, slot):
        f = bm.faces.new([p if isinstance(p, bmesh.types.BMVert) else bm.verts.new(p) for p in points])
        f.material_index = slot
        return f

    def slab(outline_xz, y_front, thick, slot):
        """A thin closed plate facing the front (-Y)."""
        front = [bm.verts.new(Vector((x, y_front, z))) for x, z in outline_xz]
        back = [bm.verts.new(Vector((x, y_front + thick, z))) for x, z in outline_xz]
        n = len(front)
        mid_f = bm.verts.new(sum((v.co for v in front), Vector()) / n)
        mid_b = bm.verts.new(sum((v.co for v in back), Vector()) / n)
        for i in range(n):
            j = (i + 1) % n
            face([mid_f, front[i], front[j]], slot)
            face([mid_b, back[j], back[i]], slot)
            face([front[i], back[i], back[j], front[j]], slot)

    # the collar: an even band that follows the neck, the same on both sides
    import math
    normal = RING_NORMAL.normalized()
    across = Vector((1, 0, 0))
    across = (across - normal * across.dot(normal)).normalized()
    forward = normal.cross(across)

    def neck(angle):
        d = across * math.cos(angle) + forward * math.sin(angle)
        hit = bvh.ray_cast(RING_CENTRE + d * 0.3, -d)[0]
        return (hit - RING_CENTRE).length if hit is not None else 0.045

    n_seg = RING_SEGMENTS
    radii = [(neck(2 * math.pi * k / n_seg) + neck(math.pi - 2 * math.pi * k / n_seg)) / 2 for k in range(n_seg)]
    radii = [(radii[k - 1] + 2 * radii[k] + radii[(k + 1) % n_seg]) / 4 for k in range(n_seg)]   # even out lumps
    sections = []
    for k in range(n_seg):
        ang = 2 * math.pi * k / n_seg
        d = across * math.cos(ang) + forward * math.sin(ang)
        inner, outer = radii[k] - RING_IN, radii[k] + RING_OUT
        sections.append([bm.verts.new(RING_CENTRE + d * r + normal * (side * RING_WIDTH / 2))
                         for r, side in ((inner, 1), (outer, 1), (outer, -1), (inner, -1))])
    for k in range(n_seg):
        a_sec, b_sec = sections[k], sections[(k + 1) % n_seg]
        for q in range(4):
            face([a_sec[q], a_sec[(q + 1) % 4], b_sec[(q + 1) % 4], b_sec[q]], black)

    # the tag: a rectangle with its corners cut, and a three-bar mark with a stem
    cx, cz, w, h, c = TAG_CENTRE.x, TAG_CENTRE.z, TAG_W / 2, TAG_H / 2, 0.006
    slab([(cx - w + c, cz + h), (cx + w - c, cz + h), (cx + w, cz + h - c), (cx + w, cz - h + c),
          (cx + w - c, cz - h), (cx - w + c, cz - h), (cx - w, cz - h + c), (cx - w, cz + h - c)], TAG_Y, 0.002, white)
    bar_w, bar_h, gap = 0.0095, 0.0018, 0.011
    for dz in (gap, 0.0, -gap):
        slab([(cx - bar_w, cz + dz + bar_h), (cx + bar_w, cz + dz + bar_h), (cx + bar_w, cz + dz - bar_h),
              (cx - bar_w, cz + dz - bar_h)], TAG_Y - 0.0008, 0.001, ink)
    slab([(cx - bar_h, cz + gap), (cx + bar_h, cz + gap), (cx + bar_h, cz - gap), (cx - bar_h, cz - gap)],
         TAG_Y - 0.0008, 0.001, ink)
    # a short black loop joining the tag to the collar
    slab([(cx - 0.004, cz + h + 0.012), (cx + 0.004, cz + h + 0.012), (cx + 0.004, cz + h - 0.002),
          (cx - 0.004, cz + h - 0.002)], TAG_Y + 0.0002, 0.002, black)

    # collar and tag are closed shapes: face them outwards now, before the nose (an open cap) is added
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if all(v not in before for v in f.verts)])

    # the nose: a rounded triangle laid on the tip of the muzzle from the front
    def on_muzzle(x, z):
        hit = bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))[0]
        if hit is None:      # past the edge of the muzzle: stay level with the tip
            hit = bvh.ray_cast(Vector((0.0, -1.0, NOSE_Z)), Vector((0, 1, 0)))[0] or Vector((x, NOSE_AT.y, z))
            hit = Vector((x, hit.y, z))
        return hit + Vector((0, -0.003, 0))

    # Shaped like the nose the Tripo model had: a rounded pad, wide at the top and narrowing to a soft point,
    # dark grey, with two black nostrils low on it. It lies ON the tip of the muzzle, following the muzzle's
    # own roundness and standing only a millimetre or two proud, like skin. (A pad standing in front of the
    # muzzle looked stuck on, and one centred below the tip hung off it; the tip is at z 0.221.)
    nw, nh = NOSE_W / 2, NOSE_H / 2
    pad = [(0.0, 1.0), (0.45, 0.95), (0.85, 0.7), (1.0, 0.3), (0.85, -0.15), (0.5, -0.6), (0.0, -1.0),
           (-0.5, -0.6), (-0.85, -0.15), (-1.0, 0.3), (-0.85, 0.7), (-0.45, 0.95)]

    def skin(x, z, lift):
        hit = bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))[0]
        if hit is None:
            hit = Vector((x, NOSE_AT.y, z))
        # the same on both sides: take the mirrored point too and keep the one further forward
        other = bvh.ray_cast(Vector((-x, -1.0, z)), Vector((0, 1, 0)))[0]
        y = min(hit.y, other.y) if other is not None else hit.y
        return Vector((x, y - lift, z))

    def ring(scale, lift):
        return [bm.verts.new(skin(x * nw * scale, NOSE_Z + z * nh * scale, lift)) for x, z in pad]

    # The rim stands 2 mm clear so the muzzle's facets cannot cut into its outline; a short lip runs from
    # the rim back under the surface so no gap shows from the side.
    lip_v, rim_v, mid_v = ring(1.05, -0.003), ring(1.0, 0.002), ring(0.6, 0.0037)
    centre_v = bm.verts.new(skin(0.0, NOSE_Z, 0.0046))
    n_pad = len(pad)
    made_nose = []
    for k in range(n_pad):
        k2 = (k + 1) % n_pad
        made_nose.append(face([lip_v[k], lip_v[k2], rim_v[k2], rim_v[k]], nose))
        made_nose.append(face([rim_v[k], rim_v[k2], mid_v[k2], mid_v[k]], nose))
        made_nose.append(face([mid_v[k], mid_v[k2], centre_v], nose))
    for sx in (1, -1):
        cx_n, cz_n = sx * nw * 0.36, NOSE_Z - nh * 0.32
        ring_n = [bm.verts.new(skin(cx_n + dx * nw * 0.17, cz_n + dz * nh * 0.14, 0.0047))
                  for dx, dz in ((1, 0), (0.5, 0.87), (-0.5, 0.87), (-1, 0), (-0.5, -0.87), (0.5, -0.87))]
        hub = bm.verts.new(skin(cx_n, cz_n, 0.0049))
        for k in range(6):
            made_nose.append(face([hub, ring_n[k], ring_n[(k + 1) % 6]], black))
    for f in made_nose:
        f.normal_update()
        out = f.calc_center_median() - Vector((0.0, NOSE_AT.y + 0.02, NOSE_Z))
        if f.normal.dot(out) < 0:
            f.normal_flip()

    bm.verts.index_update()
    new = [v.index for v in bm.verts if v not in before]
    bm.to_mesh(me)
    bm.free()
    me.update()
    ob.vertex_groups.new(name="kn_details").add(new, 1.0, "REPLACE")
    print("KITSUNE details:", counts, "| triangles now", sum(len(p.vertices) - 2 for p in me.polygons))


main()
