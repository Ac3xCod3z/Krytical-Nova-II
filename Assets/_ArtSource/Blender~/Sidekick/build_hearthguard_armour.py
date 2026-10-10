"""The Hearthguard armour (the green, gold and ivory set): one Armor item, built as the Sidekick parts it
is made of. The game's Armor piece covers torso, hips, legs and the shoulder / knee attachments, and each
of those is its own Sidekick slot, so "one part" for the player is these eight meshes built together:

  Torso        10TORS  cuirass, back plate, back straps, shoulder straps, chest emblem, front clasps
  Hips         17HIPS  leather trousers to the knee, belt, tabard panels, thigh armour
  LegL / LegR  18LEGL / 19LEGR  leather trousers below the knee
  KneeL/KneeR  33AKNL / 34AKNR  knee guards (ride on the knee bone)
  PauldronL/R  29ASHL / 30ASHR  layered pauldrons (ride on the shoulder bone)

Run on a scene that holds Synty's reference body (sidekick_new_part.py made it):
  run_blender.py --live run_scene_script.py --script Assets/_ArtSource/Blender~/Sidekick/build_hearthguard_armour.py
Then fit each object: sidekick_fit.py --object Torso --name SK_KRYT_HRTH_01_10TORS_HU01, and so on (PARTS below).

The cloth and leather base of torso, hips and legs is Synty's own body surface pushed out a little, so it
fits every body shape; the plates are laid onto that surface. Metres, character facing -Y, its left is +X.
"""
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SET = "SK_KRYT_HRTH_01"
PARTS = {  # object name -> (slot, the swatch family of that slot)
    "Torso": ("10TORS", "Torso Outfit"), "Hips": ("17HIPS", "Hips Outfit"),
    "LegL": ("18LEGL", "Leg Left Outfit"), "LegR": ("19LEGR", "Leg Right Outfit"),
    "KneeL": ("33AKNL", "Knee Left Outfit"), "KneeR": ("34AKNR", "Knee Right Outfit"),
    "PauldronL": ("29ASHL", "Shoulder Left Outfit"), "PauldronR": ("30ASHR", "Shoulder Right Outfit"),
}
GREEN, IVORY, BROWN, DARK, GOLD = range(5)
# The slot's own swatches 01-04 carry green, ivory, brown leather and dark leather; gold is the shared metal.
COLOURS = [(0.10, 0.25, 0.08), (0.80, 0.72, 0.55), (0.22, 0.11, 0.05), (0.07, 0.045, 0.035), (0.85, 0.55, 0.10)]
SKIN = 0.008  # how far the cloth base stands off the body


def part_name(obj_name):
    return f"{SET}_{PARTS[obj_name][0]}_HU01"


def reference(code):
    for o in bpy.data.objects:
        if o.type == "MESH" and o.name.startswith("SK_SPEC_") and code in o.name:
            return o
    raise SystemExit(f"No reference body piece {code} in the scene; start it with sidekick_new_part.py.")


def world_mesh(ob):
    m, r = ob.matrix_world, ob.matrix_world.to_3x3()
    me = ob.data
    verts = [m @ v.co for v in me.vertices]
    normals = [(r @ Vector(n.vector)).normalized() for n in me.vertex_normals]
    return verts, normals, [tuple(p.vertices) for p in me.polygons]


class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.hang_layer = self.bm.verts.layers.int.new("hang")
        self.hanging = 0

    def hang(self, first_vert_index):
        """Mark every vertex made since first_vert_index as one hanging piece (see sidekick_fit.py)."""
        self.hanging += 1
        self.bm.verts.ensure_lookup_table()
        for v in self.bm.verts[first_vert_index:]:
            v[self.hang_layer] = self.hanging

    def face(self, points, material):
        verts = [p if isinstance(p, bmesh.types.BMVert) else self.bm.verts.new(p) for p in points]
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = material
        return f

    def base_from_body(self, code, colour_of):
        """The body piece itself, pushed out by SKIN: the cloth or leather layer. colour_of(centre, normal)."""
        verts, normals, polys = world_mesh(reference(code))
        new = [self.bm.verts.new(v + n * SKIN) for v, n in zip(verts, normals)]
        for poly in polys:
            centre = sum((verts[i] for i in poly), Vector()) / len(poly)
            normal = sum((normals[i] for i in poly), Vector()).normalized()
            self.face([new[i] for i in poly], colour_of(centre, normal))

    def slab(self, top, bottom, material):
        """A closed plate from a grid of top points and the matching grid of points beneath them."""
        rows, cols = len(top), len(top[0])
        tv = [[self.bm.verts.new(p) for p in row] for row in top]
        bv = [[self.bm.verts.new(p) for p in row] for row in bottom]
        for r in range(rows - 1):
            for c in range(cols - 1):
                self.face([tv[r][c], tv[r][c + 1], tv[r + 1][c + 1], tv[r + 1][c]], material)
                self.face([bv[r][c], bv[r + 1][c], bv[r + 1][c + 1], bv[r][c + 1]], material)
        for c in range(cols - 1):
            self.face([tv[0][c], bv[0][c], bv[0][c + 1], tv[0][c + 1]], material)
            self.face([tv[-1][c], tv[-1][c + 1], bv[-1][c + 1], bv[-1][c]], material)
        for r in range(rows - 1):
            self.face([tv[r][0], tv[r + 1][0], bv[r + 1][0], bv[r][0]], material)
            self.face([tv[r][-1], bv[r][-1], bv[r + 1][-1], tv[r + 1][-1]], material)

    def prism(self, outline, depth_dir, depth, material):
        top = [self.bm.verts.new(p) for p in outline]
        bot = [self.bm.verts.new(p - depth_dir * depth) for p in outline]
        n = len(top)
        if n <= 4:
            self.face(top, material)
            self.face(list(reversed(bot)), material)
        else:
            for i in range(1, n - 1):
                self.face([top[0], top[i], top[i + 1]], material)
                self.face([bot[0], bot[i + 1], bot[i]], material)
        for i in range(n):
            self.face([top[i], bot[i], bot[(i + 1) % n], top[(i + 1) % n]], material)

    def finish(self, family):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(self.name)
        names = [f"{family} 01", f"{family} 02", f"{family} 03", f"{family} 04", "Jewellery Metal 01"]
        for mat_name, colour in zip(names, COLOURS):
            mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
            mat.use_fake_user = True
            mat.diffuse_color = (*colour, 1.0)
            if mat.node_tree is None:
                mat.use_nodes = True
            bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf:
                bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
                bsdf.inputs["Roughness"].default_value = 0.45 if "Metal" in mat_name else 0.85
                bsdf.inputs["Metallic"].default_value = 0.6 if "Metal" in mat_name else 0.0
            me.materials.append(mat)
        bm.verts.ensure_lookup_table()
        tags = [v[self.hang_layer] for v in bm.verts]
        bm.verts.layers.int.remove(self.hang_layer)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(self.name, me)
        for n in range(1, self.hanging + 1):
            ob.vertex_groups.new(name=f"hang_{n:02d}").add([i for i, t in enumerate(tags) if t == n], 1.0, "REPLACE")
        (bpy.data.collections.get("SK_Work") or bpy.context.scene.collection).objects.link(ob)
        return sum(len(p.vertices) - 2 for p in me.polygons)


class Surface:
    """Lays points onto the body: (angle round an upright axis, height) -> the body's surface there."""

    def __init__(self, codes, axis):
        verts, polys = [], []
        for code in codes:
            v, _n, p = world_mesh(reference(code))
            polys += [tuple(len(verts) + i for i in poly) for poly in p]
            verts += v
        self.bvh = BVHTree.FromPolygons(verts, polys)
        self.axis = Vector((axis[0], axis[1], 0))

    def at(self, angle_deg, z, off=0.0, axis=None):
        """angle 0 = front, 90 = the character's left, 180 = back."""
        a = math.radians(angle_deg)
        out = Vector((math.sin(a), -math.cos(a), 0))
        centre = (Vector((axis[0], axis[1], 0)) if axis else self.axis) + Vector((0, 0, z))
        hit = self.bvh.ray_cast(centre + out * 1.5, -out)[0]
        if hit is None:
            hit = centre + out * 0.10
        return hit + out * off

    def plate(self, part, grid, off, material, axis=None, sink=0.004, detail=3):
        """A plate following the body. grid = rows of (angle, z); each cell is cut into detail x detail
        so a big plate bends with the surface instead of cutting through it."""
        fine = []
        for r0, r1 in zip(grid[:-1], grid[1:]):
            for i in range(detail + (r1 is grid[-1])):
                t = i / detail
                row = []
                for (a0, z0), (a1, z1), (b0, y0), (b1, y1) in zip(r0[:-1], r0[1:], r1[:-1], r1[1:]):
                    for j in range(detail):
                        u = j / detail
                        top_a, top_z = a0 + (a1 - a0) * u, z0 + (z1 - z0) * u
                        bot_a, bot_z = b0 + (b1 - b0) * u, y0 + (y1 - y0) * u
                        row.append((top_a + (bot_a - top_a) * t, top_z + (bot_z - top_z) * t))
                row.append((r0[-1][0] + (r1[-1][0] - r0[-1][0]) * t, r0[-1][1] + (r1[-1][1] - r0[-1][1]) * t))
                fine.append(row)
        grid = fine
        top = [[self.at(a, z, off, axis) for a, z in row] for row in grid]
        bottom = [[self.at(a, z, -sink, axis) for a, z in row] for row in grid]
        part.slab(top, bottom, material)


def mirror(grid):
    return [[(-a, z) for a, z in reversed(row)] for row in grid]


def build_torso(body):
    p = Part("Torso")

    def colour(c, n):
        front = n.y < -0.25
        if front and abs(c.x) < 0.125 and c.z > 1.27:
            return GREEN
        return BROWN

    p.base_from_body("10TORS", colour)

    # 02 cuirass: the gold V collar, the gold edge under the ivory breastplate, side ribs
    collar = [[(52, 1.44), (34, 1.41), (17, 1.37), (0, 1.33)], [(52, 1.39), (34, 1.355), (17, 1.312), (0, 1.265)]]
    under = [[(44, 1.19), (22, 1.135), (0, 1.085)], [(44, 1.15), (22, 1.095), (0, 1.04)]]
    rib = [[(40, 1.275), (58, 1.25)], [(40, 1.235), (58, 1.215)]]
    for grid in (collar, mirror(collar)):
        body.plate(p, grid, 0.034, GOLD)
    for grid in (under, mirror(under), rib, mirror(rib)):
        body.plate(p, grid, 0.031, GOLD)
    # a raised ivory breastplate between them
    body.plate(p, [[(-36, 1.30), (0, 1.25), (36, 1.30)], [(-40, 1.20), (0, 1.17), (40, 1.20)],
                   [(-20, 1.13), (0, 1.075), (20, 1.13)]], 0.022, IVORY)

    # 04 (from the mantle) the ancestral emblem on the breast: spike and two blades
    c = body.at(0, 1.375, 0.05)
    front = Vector((0, -1, 0))
    for outline in ([(0.0, 0.075), (-0.028, 0.0), (0.0, -0.085), (0.028, 0.0)],
                    [(-0.014, -0.03), (-0.012, -0.068), (-0.058, -0.006), (-0.082, 0.056)],
                    [(0.014, -0.03), (0.082, 0.056), (0.058, -0.006), (0.012, -0.068)]):
        p.prism([Vector((c.x + x, c.y, c.z + z)) for x, z in outline], front, 0.02, GOLD)
    green_gem = [(0.0, 0.04), (-0.013, 0.0), (0.0, -0.045), (0.013, 0.0)]
    p.prism([Vector((c.x + x, c.y - 0.006, c.z + z)) for x, z in green_gem], front, 0.01, GREEN)

    # 03 back plate: a gold yoke across the shoulder blades, leather straps crossed beneath it
    body.plate(p, [[(140, 1.36), (180, 1.36), (220, 1.36)], [(138, 1.22), (180, 1.22), (222, 1.22)],
                   [(150, 1.10), (180, 1.06), (210, 1.10)]], 0.02, IVORY)
    yoke = [[(126, 1.425), (154, 1.425), (180, 1.425)], [(126, 1.385), (154, 1.33), (180, 1.262)]]
    for grid in (yoke, mirror(yoke)):
        body.plate(p, grid, 0.03, GOLD)
    cross = [[(134, 1.37), (163, 1.29), (193, 1.19), (224, 1.085)], [(134, 1.322), (163, 1.242), (193, 1.142), (224, 1.04)]]
    for grid in (cross, mirror(cross)):
        body.plate(p, grid, 0.038, DARK)
    body.plate(p, [[(170, 1.262), (180, 1.282), (190, 1.262)], [(170, 1.212), (180, 1.19), (190, 1.212)]], 0.048, GOLD)

    # 03 (from the mantle) the shoulder yoke: a strap over each shoulder, clasp to back plate
    core = Vector((0.0, 0.01, 1.30))

    def strap(points, width, thick, material):
        sections = []
        for i, pt in enumerate(points):
            ahead = points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]
            out = (pt - core).normalized()
            side = ahead.cross(out).normalized()
            out = side.cross(ahead).normalized()
            sections.append([p.bm.verts.new(pt + side * (sx * width / 2) + out * oz)
                             for sx, oz in ((-1, -0.006), (1, -0.006), (1, thick), (-1, thick))])
        for a, b in zip(sections[:-1], sections[1:]):
            for k in range(4):
                p.face([a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]], material)
        p.face(list(reversed(sections[0])), material)
        p.face(sections[-1], material)

    def frame(normal):
        normal = normal.normalized()
        side = normal.cross(Vector((0, 0, 1))).normalized()
        return normal, side, side.cross(normal)

    def disc(centre, normal, radius, thick):
        normal, side, up = frame(normal)

        def ring(r, h):
            return [p.bm.verts.new(centre + (side * math.cos(a) + up * math.sin(a)) * r + normal * h)
                    for a in (math.pi / 8 + i * math.pi / 4 for i in range(8))]

        rings = [ring(radius, -0.008), ring(radius, thick * 0.6), ring(radius * 0.74, thick * 0.6),
                 ring(radius * 0.60, thick * 0.25), ring(radius * 0.36, thick * 1.5)]
        for a, b in zip(rings[:-1], rings[1:]):
            for i in range(8):
                j = (i + 1) % 8
                p.face([a[i], a[j], b[j], b[i]], GOLD)
        tip = p.bm.verts.new(centre + normal * thick * 2.1)
        back = p.bm.verts.new(centre - normal * 0.012)
        for i in range(8):
            j = (i + 1) % 8
            p.face([rings[-1][i], rings[-1][j], tip], GOLD)
            p.face([rings[0][j], rings[0][i], back], GOLD)

    def tail(centre, normal, lean, width, length, material, thick=0.008):
        normal, side, _up = frame(normal)
        down = (Vector((0, 0, -1)) + side * lean).normalized()
        outline = [centre - side * width / 2, centre + side * width / 2,
                   centre + side * width / 2 + down * length * 0.78, centre + down * length,
                   centre - side * width / 2 + down * length * 0.78]
        p.prism([q + normal * thick for q in outline], normal, thick + 0.004, material)

    for s in (1, -1):
        clasp = Vector((s * 0.150, -0.118, 1.418))
        over = [clasp, Vector((s * 0.156, -0.07, 1.462)), Vector((s * 0.160, 0.02, 1.482)),
                Vector((s * 0.152, 0.10, 1.47)), body.at(180 - s * 40, 1.40, 0.012)]
        strap(over, 0.05, 0.014, BROWN)
        n = Vector((s * 0.55, -1.0, 0.2))
        tail(clasp + Vector((0, 0, -0.012)), n, s * 0.5, 0.05, 0.12, GREEN, 0.006)
        tail(clasp + n.normalized() * 0.004, n, -s * 0.12, 0.034, 0.14, DARK)
        disc(clasp + n.normalized() * 0.012, n, 0.048, 0.016)
    return p


def build_hips(body, legs):
    p = Part("Hips")
    p.base_from_body("17HIPS", lambda c, n: DARK)

    # 04 belt and straps
    ring = [a * 22.5 for a in range(17)]
    body.plate(p, [[(a, 1.004) for a in ring], [(a, 0.95) for a in ring]], 0.02, BROWN)
    slant = [-70, -45, -20, 5, 30, 55, 78]
    body.plate(p, [[(a, 0.985 - (a + 70) * 0.00062) for a in slant], [(a, 0.945 - (a + 70) * 0.00062) for a in slant]],
               0.031, BROWN)
    for angle, half, z in ((0, 9, 0.977), (40, 7, 0.925)):
        body.plate(p, [[(angle - half, z + 0.036), (angle + half, z + 0.036)], [(angle - half, z - 0.036), (angle + half, z - 0.036)]],
                   0.038, GOLD)
        body.plate(p, [[(angle - half * 0.5, z + 0.019), (angle + half * 0.5, z + 0.019)],
                       [(angle - half * 0.5, z - 0.019), (angle + half * 0.5, z - 0.019)]], 0.043, BROWN)

    # 05 tabard panels: pointed banners hung from the belt, each a gold back with coloured layers on it
    def banner(angle, w_top, w_bot, z_top, z_shoulder, z_tip, r_top, r_bot, layers, emblem=False):
        a = math.radians(angle)
        out = Vector((math.sin(a), -math.cos(a), 0))
        side = Vector((math.cos(a), math.sin(a), 0))
        axis = Vector((0, 0.02, 0))
        first = len(p.bm.verts)

        def point(s, z, lift=0.0):
            r = r_top + (r_bot - r_top) * (z_top - z) / (z_top - z_tip)
            return axis + out * (r + lift) + side * s + Vector((0, 0, z))

        slope = (out * (r_top - r_bot) / (z_top - z_tip) + Vector((0, 0, 1))).normalized()
        normal = side.cross(slope).normalized()
        if normal.dot(out) < 0:
            normal = -normal
        for i, (material, inset) in enumerate(layers):
            wt, wb = w_top - inset, w_bot - inset
            zs, zt = z_shoulder + inset * 0.5, z_tip + inset * 1.9
            lift = 0.005 * i
            outline = [point(-wt, z_top, lift), point(wt, z_top, lift), point(wb, zs, lift), point(0, zt, lift), point(-wb, zs, lift)]
            p.prism(outline, normal, 0.012 + lift, material)
        if emblem:
            lift = 0.005 * len(layers) + 0.002
            z0 = z_shoulder + 0.15
            for shape in ([(0.0, 0.10), (-0.02, 0.03), (0.0, -0.04), (0.02, 0.03)],
                          [(0.0, -0.045), (0.0, -0.085), (0.052, -0.02), (0.058, 0.03)],
                          [(0.0, -0.045), (-0.058, 0.03), (-0.052, -0.02), (0.0, -0.085)]):
                p.prism([point(s, z0 + dz, lift) for s, dz in shape], normal, 0.012, GOLD)
        p.hang(first)

    trio = [(GOLD, 0.0), (IVORY, 0.011), (GREEN, 0.027)]
    duo = [(GOLD, 0.0), (GREEN, 0.012)]
    banner(0, 0.078, 0.098, 0.975, 0.50, 0.385, 0.158, 0.20, trio, emblem=True)
    banner(180, 0.078, 0.098, 0.975, 0.50, 0.385, 0.168, 0.215, trio, emblem=True)
    for s in (1, -1):
        banner(s * 52, 0.062, 0.078, 0.972, 0.63, 0.535, 0.196, 0.24, duo)
        banner(s * 102, 0.07, 0.085, 0.972, 0.60, 0.50, 0.222, 0.262, duo)
        banner(s * 146, 0.062, 0.078, 0.972, 0.63, 0.535, 0.20, 0.245, duo)

    # 07 thigh armour: a gold shield with a green diamond on the front-outside of each thigh
    for s in (1, -1):
        axis, c = (s * 0.098, 0.028), s * 24
        shield = [[(c - 24, 0.80), (c, 0.818), (c + 24, 0.80)], [(c - 33, 0.72), (c, 0.72), (c + 33, 0.72)],
                  [(c - 25, 0.63), (c, 0.625), (c + 25, 0.63)], [(c - 3, 0.565), (c, 0.555), (c + 3, 0.565)]]
        legs.plate(p, shield, 0.024, GOLD, axis)
        legs.plate(p, [[(c, 0.785), (c + 15, 0.71)], [(c - 15, 0.71), (c, 0.61)]], 0.033, GREEN, axis)
    return p


def build_leg(side_name, code):
    p = Part(side_name)
    p.base_from_body(code, lambda c, n: DARK)
    return p


def build_knee(name, s, legs):
    p = Part(name)
    axis, c = (s * 0.106, 0.024), s * 6
    guard = [[(c - 4, 0.535), (c, 0.55), (c + 4, 0.535)], [(c - 30, 0.49), (c, 0.495), (c + 30, 0.49)],
             [(c - 33, 0.425), (c, 0.425), (c + 33, 0.425)], [(c - 20, 0.355), (c, 0.335), (c + 20, 0.355)]]
    legs.plate(p, guard, 0.03, GOLD, axis, sink=0.0)
    legs.plate(p, [[(c, 0.51), (c + 15, 0.44)], [(c - 15, 0.44), (c, 0.365)]], 0.04, GREEN, axis, sink=-0.02)
    return p


def build_pauldron(name, s):
    """Three overlapping leaf plates stepping out along the shoulder, each gold with a green heart."""
    p = Part(name)
    y0 = 0.045
    for x0, x1, top, half in ((0.10, 0.255, 1.508, 0.125), (0.165, 0.315, 1.494, 0.118), (0.225, 0.37, 1.478, 0.108)):
        xm = x0 + (x1 - x0) * 0.45

        def leaf(scale, lift, material):
            def pt(x, y, z):
                return Vector((s * (xm + (x - xm) * scale), y0 + (y - y0) * scale, z + lift * (1 if z > top - 0.06 else 0.4)))

            root, tip = pt(x0, y0, top - 0.012), pt(x1 + 0.015, y0, top - 0.022)
            apex = pt(xm, y0, top + 0.02)
            # front and back hang down over the arm, each ending in a point
            front = [pt(x0 + 0.01, y0 - half * 0.75, top - 0.06), pt(xm, y0 - half, top - 0.135), pt(x1, y0 - half * 0.7, top - 0.07)]
            back = [pt(x1, y0 + half * 0.7, top - 0.07), pt(xm, y0 + half, top - 0.135), pt(x0 + 0.01, y0 + half * 0.75, top - 0.06)]
            rim = [root] + front + [tip] + back
            under = Vector((s * xm, y0, top - 0.085))
            for a, b in zip(rim, rim[1:] + rim[:1]):
                p.face([a, b, apex], material)
                p.face([b, a, under], material)

        leaf(1.0, 0.0, GOLD)
        leaf(0.66, 0.014, GREEN)
    return p


def main():
    names = set(PARTS) | {part_name(n) for n in PARTS}
    for old in [o for o in bpy.data.objects if o.name in names and o.type == "MESH"]:
        me_old = old.data
        bpy.data.objects.remove(old)
        bpy.data.meshes.remove(me_old)

    body = Surface(["10TORS", "17HIPS"], (0.0, 0.02))
    legs = Surface(["17HIPS", "18LEGL", "19LEGR"], (0.0, 0.02))
    built = [build_torso(body), build_hips(body, legs), build_leg("LegL", "18LEGL"), build_leg("LegR", "19LEGR"),
             build_knee("KneeL", 1, legs), build_knee("KneeR", -1, legs),
             build_pauldron("PauldronL", 1), build_pauldron("PauldronR", -1)]
    for part in built:
        tris = part.finish(PARTS[part.name][1])
        print(f"ARMOUR {part.name} -> {part_name(part.name)}: {tris} triangles")


main()
