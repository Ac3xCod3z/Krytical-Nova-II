"""Give a pet clean, flat eyes: an almond-shaped plate on each side of the face in one swatch colour.

  run_blender.py --blend FILE.blend pet_eyes.py --object SM_Pet_X_S1 --at 0.03,-0.24,0.255
        [--width 0.03] [--height 0.014] [--tilt 12]   size in metres, tilt in degrees (outer corner up)
        [--colour Black]     a KN swatch colour name
        [--radius 0.022]     the model's own eye pieces and specks inside this distance are removed
        [--no-mirror]        by default the other side (x flipped) gets the same eye
        [--paint-only]       keep the model's own eye piece and only paint it the colour

AI models bring each eye as a small lump of mesh, mostly sunk in the head, with shading painted on it
that turns into blotches when the texture is flattened. This removes that and lays a clean eye on the
surface instead. Run it after pet_from_scan.py and before pet_finish.py; give --at in metres at the
model's current size, anywhere near the middle of one eye (it is dropped onto the face from the front).

For blinking: each eye gets an eyelid, a fur-coloured strip folded up along its upper edge. Everything
made here is in the vertex groups eye.L / eye.R (rigid with the head); the eyelid's moving edge is in
eyelid.L / eyelid.R, weighted so that one straight pull closes the eye. Where that pull starts and ends
is stored on the object as kn_eyelid_L / kn_eyelid_R (open x,y,z then shut x,y,z) for pet_rig.py.
"""
import argparse
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers

GROUPS = ("eye.L", "eye.R", "eyelid.L", "eyelid.R")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object", required=True)
    ap.add_argument("--at", required=True, help="x,y,z near the middle of one eye")
    ap.add_argument("--width", type=float, default=0.03)
    ap.add_argument("--height", type=float, default=0.014)
    ap.add_argument("--tilt", type=float, default=12.0)
    ap.add_argument("--no-mirror", action="store_true")
    ap.add_argument("--radius", type=float, default=0.022)
    ap.add_argument("--colour", default="Black")
    ap.add_argument("--paint-only", action="store_true")
    ap.add_argument("--lift", type=float, default=0.0012, help="how far the eye stands off the face, metres")
    a = ap.parse_args(kn.tool_args())

    ob = kn.get_object(a.object)
    me = ob.data
    if any(abs(s - 1) > 1e-4 for s in ob.scale) or ob.rotation_euler.to_quaternion().angle > 1e-4:
        raise SystemExit("Apply the object's rotation and scale first (pet_finish.py does).")
    swatch = kn.load_swatch()
    cell = next((c for c in (swatch["cells"] if swatch else []) if kn.cell_key(c["name"]) == kn.cell_key(a.colour)), None)
    if cell is None:
        raise SystemExit(f"'{a.colour}' is not a KN swatch colour (run make_swatch.py; names are in KN_Swatch.json).")
    lin = tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in cell["rgb"])
    mat_name = f"{ob.name} Eye"
    mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
    mat.diffuse_color = (*lin, 1.0)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
        bsdf.inputs["Roughness"].default_value = 1.0      # flat black, no shine
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.0
    if mat.name not in [m.name for m in me.materials if m]:
        me.materials.append(mat)
    slot = [m.name if m else None for m in me.materials].index(mat.name)

    x, y, z = (float(v) for v in a.at.split(","))
    origin = ob.matrix_world.translation
    centres = [Vector((x, y, z)) - origin] + ([] if a.no_mirror else [Vector((-x, y, z)) - origin])

    # Earlier eyes made by this tool go first, so it can be run again with other numbers.
    old = set()
    for g in ob.vertex_groups:
        if g.name in ("eye.L", "eye.R"):
            old.update(v.index for v in me.vertices if any(e.group == g.index for e in v.groups))
    for name in GROUPS:
        g = ob.vertex_groups.get(name)
        if g:
            ob.vertex_groups.remove(g)

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

    report, remove = [], []
    for centre in centres:
        cleared = painted = 0
        for group in islands:
            if len(group) >= len(bm.faces) * 0.25:      # never the body itself
                continue
            verts = {v for f in group for v in f.verts}
            mid = sum((v.co for v in verts), Vector()) / len(verts)
            if (mid - centre).length > a.radius:
                continue
            if a.paint_only and len(group) >= 20:
                for f in group:
                    f.material_index = slot
                painted += len(group)
            else:
                remove.extend(group)
                cleared += 1
        report.append({"at": [round(c, 4) for c in centre + origin], "pieces_removed": cleared, "faces_painted": painted})
    if remove:
        bmesh.ops.delete(bm, geom=list(set(remove)), context="FACES")

    made = {}        # new vertex -> (side, unused)
    lid_weight = {}  # moving eyelid vertex -> how much of the eyelid bone's pull it takes
    if not a.paint_only:
        import numpy as np
        bm.faces.ensure_lookup_table()
        bvh = BVHTree.FromBMesh(bm)
        w, h = a.width / 2, a.height / 2
        tilt = math.radians(a.tilt)
        # A pointed lens: two shallow arcs between the inner and the outer corner. (along, across, upper edge?)
        steps = 6
        upper = [(-1 + 2 * k / steps, 1 - (-1 + 2 * k / steps) ** 2, True) for k in range(1, steps)]
        lower = [(1 - 2 * k / steps, -0.85 * (1 - (1 - 2 * k / steps) ** 2), False) for k in range(1, steps)]
        outline = [(-1.0, 0.0, False)] + upper + [(1.0, 0.0, False)] + lower

        # Work the eye out once, on the +x side, and mirror it, so both eyes are the same shape.
        base = centres[0] if centres[0].x >= 0 else Vector((-centres[0].x, centres[0].y, centres[0].z))
        samples = []
        for dx in (-w, -w / 2, 0, w / 2, w):
            for dz in (-h, 0, h):
                found = bvh.ray_cast(Vector((base.x + dx, base.y - 0.5, base.z + dz)), Vector((0, 1, 0)))
                if found[0] is not None:
                    samples.append((found[0], found[1] if found[1].y < 0 else -found[1]))
        if not samples:
            raise SystemExit("No surface in front of --at; give a point near the middle of one eye.")
        normal = sum((n for _p, n in samples), Vector()).normalized()
        hit = bvh.ray_cast(Vector((base.x, base.y - 0.5, base.z)), Vector((0, 1, 0)))[0] or samples[0][0]
        side = normal.cross(Vector((0, 0, 1))).normalized()
        if side.x < 0:
            side = -side                              # "along" runs from the nose side to the outer side
        up = side.cross(normal)
        if up.z < 0:
            up = -up

        def flat(along, across):
            ca, sa = math.cos(tilt), math.sin(tilt)
            return hit + side * ((along * w) * ca - (across * h) * sa) + up * ((along * w) * sa + (across * h) * ca)

        def height(point):
            found = bvh.ray_cast(point + normal * 0.03, -normal)[0]
            return (found - point).dot(normal) if found is not None else 0.0

        # The face bends round the head: follow that bend as one smooth curve, and ignore its lumps.
        grid = [(-1.1 + 2.2 * i / 14, -1.1 + 2.2 * k / 8) for i in range(15) for k in range(9)]
        hs = np.array([height(flat(al, ac)) for al, ac in grid])
        A = np.array([[1.0, al, al * al] for al, _ac in grid])
        coef = np.linalg.lstsq(A, hs, rcond=None)[0]
        over = float(np.max(hs - A @ coef))            # the highest lump above that curve

        def on_face(along, across):
            bend = float(coef[0] + coef[1] * along + coef[2] * along * along)
            return flat(along, across) + normal * (bend + max(over, 0.0) + a.lift)

        # Nothing of the face may stand proud of the eye: any vertex under the eye that would poke through
        # it (a brow ridge, the rim of the old socket) is pressed back to just beneath it. Both sides.
        pressed = 0
        for v in list(bm.verts):
            for flip in ((1.0, -1.0) if len(centres) > 1 else ((1.0,) if centres[0].x >= 0 else (-1.0,))):
                q = Vector((v.co.x * flip, v.co.y, v.co.z)) - hit
                al0, ac0 = q.dot(side) / w, q.dot(up) / h
                ca, sa = math.cos(-tilt), math.sin(-tilt)
                al, ac = (al0 * w * ca - ac0 * h * sa) / w, (al0 * w * sa + ac0 * h * ca) / h
                if abs(al) > 1.2 or abs(ac) > 1.4:
                    continue
                limit = float(coef[0] + coef[1] * al + coef[2] * al * al) + max(over, 0.0) + a.lift - 0.0012
                above = q.dot(normal) - limit
                if above > 0:
                    moved = v.co - Vector((normal.x * flip, normal.y, normal.z)) * above
                    v.co = moved
                    pressed += 1
        report[0]["face_points_pressed_under_the_eyes"] = pressed

        points = [(on_face(al, ac), upper_edge) for al, ac, upper_edge in outline]
        middle = on_face(0.0, 0.05)

        # The eyelid: a fur-coloured strip folded up along the upper edge of the eye. Its lower edge is the
        # part that moves; pulled down by an eyelid bone it slides over the eye and closes it. Each point of
        # that edge is weighted by how far it has to travel (most in the middle, nothing at the corners), so
        # one straight pull lands the whole edge on the lower rim of the eye.
        bm.faces.ensure_lookup_table()
        nearest = bvh.find_nearest(hit)
        lid_slot = bm.faces[nearest[2]].material_index if nearest[2] is not None else 0
        lid_lift = 0.0009
        arc = [(-1.0, 0.0)] + [(al, ac) for al, ac, upper_edge in outline if upper_edge] + [(1.0, 0.0)]
        lid = []
        for al, ac in arc:
            open_at = on_face(al, ac + 0.10) + normal * lid_lift            # fixed edge, just above the eye
            edge_at = on_face(al, max(ac - 0.07, 0.0) if abs(al) < 1 else 0.0) + normal * lid_lift
            lid.append((open_at, edge_at, 1.0 - al * al))
        lid_open = on_face(0.0, 1.0 - 0.07) + normal * lid_lift
        lid_shut = on_face(0.0, -0.85) + normal * lid_lift
        for index, centre in enumerate(centres):
            left = centre.x >= 0
            flip = 1.0 if left else -1.0
            rim = []
            for point, upper_edge in points:
                v = bm.verts.new(Vector((point.x * flip, point.y, point.z)))
                made[v] = ("L" if left else "R", upper_edge)
                rim.append(v)
            mid = bm.verts.new(Vector((middle.x * flip, middle.y, middle.z)))
            made[mid] = ("L" if left else "R", False)
            facing = Vector((normal.x * flip, normal.y, normal.z))
            for k in range(len(rim)):
                f = bm.faces.new([mid, rim[k], rim[(k + 1) % len(rim)]])
                f.normal_update()
                if f.normal.dot(facing) < 0:
                    f.normal_flip()
                f.material_index = slot
            report[index]["eye_made"] = {"width_m": a.width, "height_m": a.height, "triangles": len(rim)}

            fixed, moving = [], []
            for open_at, edge_at, weight in lid:
                v = bm.verts.new(Vector((open_at.x * flip, open_at.y, open_at.z)))
                made[v] = ("L" if left else "R", False)
                fixed.append(v)
                m = bm.verts.new(Vector((edge_at.x * flip, edge_at.y, edge_at.z)))
                made[m] = ("L" if left else "R", False)
                lid_weight[m] = weight
                moving.append(m)
            for k in range(len(fixed) - 1):
                f = bm.faces.new([fixed[k], fixed[k + 1], moving[k + 1], moving[k]])
                f.normal_update()
                if f.normal.dot(facing) < 0:
                    f.normal_flip()
                f.material_index = lid_slot
            name = "kn_eyelid_L" if left else "kn_eyelid_R"
            ob[name] = [lid_open.x * flip, lid_open.y, lid_open.z, lid_shut.x * flip, lid_shut.y, lid_shut.z]

    bm.verts.index_update()
    members = {name: [] for name in GROUPS}
    lids = {name: [] for name in GROUPS}
    for v, (side_name, _unused) in made.items():
        members["eye." + side_name].append(v.index)
        if v in lid_weight:
            lids["eyelid." + side_name].append((v.index, lid_weight[v]))
    bm.to_mesh(me)
    bm.free()
    me.update()
    for name, indices in members.items():
        if indices:
            ob.vertex_groups.new(name=name).add(indices, 1.0, "REPLACE")
    for name, pairs in lids.items():
        if pairs:
            group = ob.vertex_groups.new(name=name)
            for index, weight in pairs:
                group.add([index], max(weight, 0.0), "REPLACE")

    problems = [f"{r['at']}: {r['problem']}" for r in report if "problem" in r]
    kn.save_blend()
    kn.report("Pet eyes", {"object": ob.name, "colour": f"{cell['name']} {cell['colour']}", "eyes": report,
                           "next": "pet_finish.py to put the eye colour on the swatch."}, problems)
    if problems:
        sys.exit(1)


main()
