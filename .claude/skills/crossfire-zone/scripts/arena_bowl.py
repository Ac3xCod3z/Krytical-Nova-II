# The Crucible bowl, rebuilt from the owner's overhead concept (16 Nov, his words: "only 6 or 7 rows ... not seating ... big rows of block
# that are a specific colour coming down from up top to the ground level ... a bigger fightable area at the bottom ... the grand stairs
# from the rally point should be accurate to what's inside the image").
#   - seven concentric rings of BIG stone blocks step down from the rim (z 0) to the arena floor (z -7.5); each ring is ~1.07 m high, ~3 m deep
#   - the floor is a wide flat disc, radius 82.3 m (was 72.3)
#   - four grand stairs (N, S, E, W), 14 m wide, pale stone, with stepped cheek walls of blocks either side, running a little way onto the floor
import bpy, bmesh, math, random
from mathutils import Vector

R_FLOOR = 82.3; R_RIM = 103.7; DEPTH = 7.5; TIERS = 7
STAIR_HALF = 7.0; CHEEK = 1.7; STAIR_STEPS = 25; STAIR_TREAD = 1.0
DR = (R_RIM - R_FLOOR) / TIERS; DH = DEPTH / TIERS


def _finish(bm, name, mats, up_slots=()):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    me['crucible_piece'] = name; me.use_fake_user = True
    return me


def _quad(bm, uv, pts, slot, tile=8.0):
    try: f = bm.faces.new([bm.verts.new(p) for p in pts])
    except ValueError: return
    f.material_index = slot; f.normal_update(); n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
    for l in f.loops:
        c = l.vert.co; u, v = ((c.y, c.z) if ax == 0 else (c.x, c.z) if ax == 1 else (c.x, c.y)); l[uv].uv = (u / tile, v / tile)


def tier_blocks(name, mats, seed):
    """One quadrant (between the +X and +Y stairs) of the seven block rings. mats = [top, bevel, front, gap].
    Every block is its own top / bevel / face, with a joint to its neighbours and a few centimetres of unevenness."""
    rnd = random.Random(seed); bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    P = lambda r, a, z: (r * math.cos(a), r * math.sin(a), z)
    half = STAIR_HALF + CHEEK + 0.04
    for i in range(TIERS):
        ra, rb = R_FLOOR + DR * i, R_FLOOR + DR * (i + 1); zt = -DEPTH + DH * (i + 1); zb = zt - DH
        lo = math.asin(half / ra); hi = math.pi / 2 - lo
        # dark backing behind the joints
        seg = 14
        for j in range(seg):
            a0, a1 = lo + (hi - lo) * j / seg, lo + (hi - lo) * (j + 1) / seg
            _quad(bm, uv, [P(ra + 0.10, a0, zb - 0.1), P(ra + 0.10, a1, zb - 0.1), P(ra + 0.10, a1, zt - 0.10), P(ra + 0.10, a0, zt - 0.10)], 3)
            _quad(bm, uv, [P(ra + 0.10, a0, zt - 0.10), P(ra + 0.10, a1, zt - 0.10), P(rb + 0.2, a1, zt - 0.10), P(rb + 0.2, a0, zt - 0.10)], 3)
        a = lo
        while a < hi - 1e-4:
            L = rnd.uniform(4.2, 7.4); b = a + L / ra
            if hi - b < 2.6 / ra: b = hi
            g = 0.045 / ra; a0, a1 = a + (g if a > lo else 0.0), b - (g if b < hi else 0.0)
            dz = rnd.uniform(-0.05, 0.05); dr = rnd.uniform(-0.05, 0.07); r0 = ra + dr; z1 = zt + dz; c = 0.13
            n = 2
            for k in range(n):
                p0, p1 = a0 + (a1 - a0) * k / n, a0 + (a1 - a0) * (k + 1) / n
                _quad(bm, uv, [P(r0 + c, p0, z1), P(r0 + c, p1, z1), P(rb + 0.15, p1, z1), P(rb + 0.15, p0, z1)], 0)          # top
                _quad(bm, uv, [P(r0, p0, z1 - c), P(r0, p1, z1 - c), P(r0 + c, p1, z1), P(r0 + c, p0, z1)], 1)              # chamfer
                _quad(bm, uv, [P(r0, p0, zb - 0.15), P(r0, p1, zb - 0.15), P(r0, p1, z1 - c), P(r0, p0, z1 - c)], 2)        # face
            for pa in (a0, a1):                                                                                             # joint sides
                _quad(bm, uv, [P(r0, pa, zb - 0.15), P(r0, pa, z1 - c), P(r0 + c, pa, z1), P(rb + 0.15, pa, z1), P(rb + 0.15, pa, zb - 0.15)], 2)
            a = b
    return _finish(bm, 'KN_Crucible_' + name, mats)


def grand_stair(name, mats):
    """Grand stair along +X from the rim (r 103.7, z 0) down to the floor, 14 m wide, 25 steps of 0.3 m with 1 m treads (foot at r 79.7,
    2.6 m onto the floor), flanked by stepped cheek walls of big blocks. mats = [tread, riser, cheek top, cheek side]."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); w = STAIR_HALF
    for j in range(STAIR_STEPS):
        x0 = R_RIM - j * STAIR_TREAD; z0 = -0.3 * j; z1 = z0 - 0.3
        _quad(bm, uv, [(x0, -w, z1), (x0, w, z1), (x0, w, z0), (x0, -w, z0)], 1, 5.0)                                        # riser
        if j < STAIR_STEPS - 1:
            x1 = x0 - STAIR_TREAD
            _quad(bm, uv, [(x1, -w, z1), (x0, -w, z1), (x0, w, z1), (x1, w, z1)], 0, 16.0)                                   # tread
    _quad(bm, uv, [(R_RIM, -w, 0.0), (R_RIM + 0.9, -w, 0.0), (R_RIM + 0.9, w, 0.0), (R_RIM, w, 0.0)], 0, 16.0)      # head landing, laps onto the rim
    foot = R_RIM - (STAIR_STEPS - 1) * STAIR_TREAD
    for s in (-1, 1):                                                                                                       # cheek walls
        y0, y1 = s * w, s * (w + CHEEK); ya, yb = min(y0, y1), max(y0, y1)
        spans = [(foot - 0.6, R_FLOOR, -DEPTH)] + [(R_FLOOR + DR * i, R_FLOOR + DR * (i + 1), -DEPTH + DH * (i + 1)) for i in range(TIERS)]
        for (xa, xb, zt) in spans:
            top = zt + 0.95; c = 0.12
            _quad(bm, uv, [(xa + c, ya + c, top), (xb - c, ya + c, top), (xb - c, yb - c, top), (xa + c, yb - c, top)], 2)
            for (pa, pb) in (((xa, ya), (xb, ya)), ((xb, ya), (xb, yb)), ((xb, yb), (xa, yb)), ((xa, yb), (xa, ya))):
                _quad(bm, uv, [(pa[0], pa[1], -DEPTH - 0.3), (pb[0], pb[1], -DEPTH - 0.3), (pb[0], pb[1], top - c), (pa[0], pa[1], top - c)], 3, 5.0)
            ring_o = [(xa, ya), (xb, ya), (xb, yb), (xa, yb)]; ring_i = [(xa + c, ya + c), (xb - c, ya + c), (xb - c, yb - c), (xa + c, yb - c)]
            for k in range(4):
                _quad(bm, uv, [(*ring_o[k], top - c), (*ring_o[(k + 1) % 4], top - c), (*ring_i[(k + 1) % 4], top), (*ring_i[k], top)], 2)
    return _finish(bm, 'KN_Crucible_' + name, mats)


def floor_quarter(name, mat):
    """Quarter of the arena floor disc (local x, y >= 0), radius 82.3 m + a lip under the first block ring, at z = -7.5. UV = (x, y) / 82.3."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); n = 28; R = R_FLOOR + 0.5
    vs = [bm.verts.new((0, 0, -DEPTH))] + [bm.verts.new((R * math.cos(math.pi / 2 * k / n), R * math.sin(math.pi / 2 * k / n), -DEPTH)) for k in range(n + 1)]
    f = bm.faces.new(vs)
    for l in f.loops: l[uv].uv = (l.vert.co.x / R_FLOOR, l.vert.co.y / R_FLOOR)
    for k in range(n):                                                                                                      # skirt
        a, b = vs[k + 1], vs[k + 2]
        q = bm.faces.new([bm.verts.new((a.co.x, a.co.y, -DEPTH - 1.0)), bm.verts.new((b.co.x, b.co.y, -DEPTH - 1.0)), bm.verts.new(b.co), bm.verts.new(a.co)])
        for l in q.loops: l[uv].uv = (l.vert.co.x / R_FLOOR, l.vert.co.y / R_FLOOR)
    return _finish(bm, 'KN_Crucible_' + name, [mat])
