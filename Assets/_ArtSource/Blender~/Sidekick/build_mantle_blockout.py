"""The green-and-gold mantle (back attachment), built from numbers so it can be rebuilt and tuned.

Run on a scene made by the synty-blender skill's sidekick_new_part.py:
  run_blender.py --live run_scene_script.py --script Assets/_ArtSource/Blender~/Sidekick/build_mantle_blockout.py
Makes (or replaces) the object "Mantle"; fit it afterwards with sidekick_fit.py --object Mantle.

Metres, character facing -Y. Each face gets a swatch material; the colours set here are only what Blender
shows, the game takes the colour from the swatch. Part numbers are the concept sheet's.
"""
import math

import bmesh
import bpy
from mathutils import Vector

NAME = "Mantle"
# swatch material -> colour of the concept (linear RGB), for looking at it in Blender
MATERIALS = {
    "Back Attachment 01": (0.10, 0.25, 0.08),   # green cloth, mantle outer
    "Back Attachment 02": (0.80, 0.72, 0.55),   # ivory cloth, lining
    "Jewellery Metal 01": (0.85, 0.55, 0.10),   # gold plate
    "Strap 01": (0.22, 0.11, 0.05),             # brown leather, yoke
    "Strap 02": (0.09, 0.05, 0.03),             # dark leather, clasp tails
}
GREEN, IVORY, GOLD, BROWN, DARK = range(5)

# Cape columns across the back (u = -1 left edge ... 0 centre ... 1 right edge) and the hem height at each.
# Tips at 0, +-0.45, +-0.9; notches between them: the stepped, pointed hem of the concept.
U = [-1.0, -0.9, -0.675, -0.45, -0.225, 0.0, 0.225, 0.45, 0.675, 0.9, 1.0]
HEM = {0.0: 0.07, 0.225: 0.33, 0.45: 0.17, 0.675: 0.42, 0.9: 0.29, 1.0: 0.52}
NOTCH = {0.225, 0.675}
TIP = {0.0, 0.45, 0.9}
T = [0.0, 0.12, 0.35, 0.6, 0.82, 1.0]   # rows from the shoulders (0) to the hem (1)
THICK = 0.014
EDGE_U = 0.085      # width of the gold edging down the sides, in u
EDGE_T = 0.085      # depth of the gold edging along the hem, in t
RAISE = 0.011       # how far gold plates stand off the cloth


def lerp(a, b, t):
    return a + (b - a) * t


def hem_z(u):
    u = abs(u)
    keys = sorted(HEM)
    for k0, k1 in zip(keys, keys[1:]):
        if k0 <= u <= k1:
            return lerp(HEM[k0], HEM[k1], (u - k0) / (k1 - k0))
    return HEM[keys[-1]]


def fold(u, t):
    """Pleats: the cloth is pulled in above each notch and pushed out above each tip, more so lower down."""
    u = abs(u)
    keys = sorted(NOTCH | TIP | {1.0})
    amount = {k: (-0.11 if k in NOTCH else 0.04 if k in TIP else -0.03) for k in keys}
    for k0, k1 in zip(keys, keys[1:]):
        if k0 <= u <= k1:
            return 1.0 + lerp(amount[k0], amount[k1], (u - k0) / (k1 - k0)) * t
    return 1.0


def cape(u, t, off=0.0):
    """A point on the cape: an ellipse round the body that widens and wraps forward as it falls."""
    s = t ** 0.85
    theta = u * math.radians(lerp(48, 98, t))
    f = fold(u, t)
    a = (lerp(0.225, 0.47, s) + off) * f
    b = (lerp(0.195, 0.41, s) + off) * f
    z = lerp(1.455 - 0.045 * (1 - abs(u)) ** 1.5, hem_z(u), t)
    return Vector((a * math.sin(theta), b * math.cos(theta), z))


def main():
    names = {NAME, bpy.context.scene.get("kn_part_name", NAME)}
    for old in [o for o in bpy.data.objects if o.name in names and o.type == "MESH"]:
        me_old = old.data
        bpy.data.objects.remove(old)
        bpy.data.meshes.remove(me_old)

    me = bpy.data.meshes.new(NAME)
    for mat_name, colour in MATERIALS.items():
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

    bm = bmesh.new()

    def face(points, material):
        f = bm.faces.new([p if isinstance(p, bmesh.types.BMVert) else bm.verts.new(p) for p in points])
        f.material_index = material
        return f

    def prism(outline, depth_dir, depth, material):
        """A closed plate: `outline` is its top face, pushed back along depth_dir to make walls and a bottom.
        Outlines of more than four points are fanned so no face has more than four sides."""
        top = [bm.verts.new(p) for p in outline]
        bot = [bm.verts.new(p - depth_dir * depth) for p in outline]
        n = len(top)
        if n <= 4:
            face(top, material)
            face(list(reversed(bot)), material)
        else:
            for i in range(1, n - 1):
                face([top[0], top[i], top[i + 1]], material)
                face([bot[0], bot[i + 1], bot[i]], material)
        for i in range(n):
            face([top[i], bot[i], bot[(i + 1) % n], top[(i + 1) % n]], material)

    def plate(points_ut, material=GOLD, raise_by=RAISE, inside=False):
        """A raised plate lying on the cape, given in cape coordinates (u, t)."""
        sign = -1.0 if inside else 1.0
        off = (-THICK - raise_by) if inside else raise_by
        outline = [cape(u, t, off) for u, t in points_ut]
        centre = sum(outline, Vector()) / len(outline)
        out = Vector((centre.x, centre.y, 0)).normalized() * sign
        prism(outline, out, raise_by + 0.004, material)

    # --- 01 mantle outer + 02 lining: one closed cloth shell --------------------------------------------
    outer = [[bm.verts.new(cape(u, t, 0.0)) for u in U] for t in T]
    inner = [[bm.verts.new(cape(u, t, -THICK)) for u in U] for t in T]
    cols, rows = len(U), len(T)
    for r in range(rows - 1):
        for c in range(cols - 1):
            face([outer[r][c], outer[r][c + 1], outer[r + 1][c + 1], outer[r + 1][c]], GREEN)
            face([inner[r][c], inner[r + 1][c], inner[r + 1][c + 1], inner[r][c + 1]], IVORY)
    for c in range(cols - 1):
        face([outer[0][c], inner[0][c], inner[0][c + 1], outer[0][c + 1]], GOLD)
        face([outer[-1][c], outer[-1][c + 1], inner[-1][c + 1], inner[-1][c]], GOLD)
    for r in range(rows - 1):
        face([outer[r][0], outer[r + 1][0], inner[r + 1][0], inner[r][0]], GOLD)
        face([outer[r][-1], inner[r][-1], inner[r + 1][-1], outer[r + 1][-1]], GOLD)

    # --- 05 border strips: raised gold edging, outside and on the lining --------------------------------
    for inside in (False, True):
        for sign in (1, -1):                       # down each side, ending in a blade point at the hem
            u0, u1 = sign * (1.0 - EDGE_U), sign * 1.0
            for t0, t1 in zip(T[:-1], T[1:]):
                plate([(u0, t0), (u1, t0), (u1, t1), (u0, t1)], inside=inside)
        steps = [-1 + EDGE_U] + [u for u in U[1:-1] if abs(u) < 1 - EDGE_U] + [1 - EDGE_U]
        for ua, ub in zip(steps[:-1], steps[1:]):  # along the hem
            plate([(ua, 1 - EDGE_T), (ub, 1 - EDGE_T), (ub, 1.0), (ua, 1.0)], inside=inside)
    # the second, thinner gold line that follows the hem inside the edging (outer face only)
    steps = [-0.78] + [u for u in U if abs(u) < 0.78] + [0.78]
    for ua, ub in zip(steps[:-1], steps[1:]):
        plate([(ua, 0.80), (ub, 0.80), (ub, 0.845), (ua, 0.845)], raise_by=0.007)

    # --- the emblem worked into the back of 01: a tall spike over two pairs of swept wings --------------
    plate([(0.0, 0.20), (0.075, 0.36), (0.0, 0.47), (-0.075, 0.36)], raise_by=0.014)
    for t0, reach in ((0.49, 0.27), (0.615, 0.34)):
        for s in (1, -1):
            pts = [(0.0, t0 + 0.135), (0.0, t0 + 0.04), (s * reach, t0 - 0.085), (s * reach * 0.86, t0 + 0.025)]
            plate(pts if s > 0 else list(reversed(pts)), raise_by=0.012)

    # 03 shoulder yoke and 04 ancestral emblem moved to the armour's torso (build_hearthguard_armour.py):
    # they must grow with the chest, and a back attachment cannot.
    clasp_back = {s: cape(s * 0.80, 0.035, 0.006) for s in (1, -1)}

    # --- 06 clasps at the cape's top corners: a rimmed gold disc with a boss and a leather tail ----------
    def frame(normal):
        normal = normal.normalized()
        side = normal.cross(Vector((0, 0, 1))).normalized()
        return normal, side, side.cross(normal)

    def disc(centre, normal, radius, thick):
        normal, side, up = frame(normal)

        def ring(r, h):
            return [bm.verts.new(centre + (side * math.cos(a) + up * math.sin(a)) * r + normal * h)
                    for a in (math.pi / 8 + i * math.pi / 4 for i in range(8))]

        rings = [ring(radius, -0.004), ring(radius, thick * 0.6), ring(radius * 0.74, thick * 0.6),
                 ring(radius * 0.60, thick * 0.25), ring(radius * 0.36, thick * 1.5)]
        for a, b in zip(rings[:-1], rings[1:]):
            for i in range(8):
                j = (i + 1) % 8
                face([a[i], a[j], b[j], b[i]], GOLD)
        tip = bm.verts.new(centre + normal * thick * 2.1)
        back = bm.verts.new(centre - normal * 0.008)
        for i in range(8):
            j = (i + 1) % 8
            face([rings[-1][i], rings[-1][j], tip], GOLD)
            face([rings[0][j], rings[0][i], back], GOLD)

    def tail(centre, normal, lean, width, length, material, thick=0.008):
        """A pointed tab hanging from a clasp; lean tilts it sideways."""
        normal, side, _up = frame(normal)
        down = (Vector((0, 0, -1)) + side * lean).normalized()
        outline = [centre - side * width / 2, centre + side * width / 2,
                   centre + side * width / 2 + down * length * 0.78, centre + down * length,
                   centre - side * width / 2 + down * length * 0.78]
        prism([p + normal * thick for p in outline], normal, thick, material)

    for s in (1, -1):
        cb = clasp_back[s]
        nb = Vector((cb.x, cb.y, 0.15 * cb.length))
        tail(cb + nb.normalized() * 0.004, nb, s * 0.15, 0.036, 0.12, DARK)
        disc(cb + nb.normalized() * 0.012, nb, 0.05, 0.016)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()

    ob = bpy.data.objects.new(NAME, me)
    work = bpy.data.collections.get("SK_Work")
    (work or bpy.context.scene.collection).objects.link(ob)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    ngons = sum(1 for p in me.polygons if len(p.vertices) > 4)
    print(f"MANTLE built: {len(me.vertices)} vertices, {tris} triangles, {ngons} faces over four sides")


main()
