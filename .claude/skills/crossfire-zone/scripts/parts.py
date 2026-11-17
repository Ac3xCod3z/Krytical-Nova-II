# The Crucible - custom fortress kit (Fable session). Pieces are modelled to the concept paintings: ashlar walls,
# light-strip pilasters, braced wooden gate, sigil banners, supply crates. Every piece is one small Mesh that is
# instanced by named objects. UV0 is a box projection in metres / tile size, so the tiling stone needs no shader tricks
# (works the same in Blender, the GLB preview and Unity).
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

TEX = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Textures', 'Crucible009')

PALETTE = {
    'Hegemony': dict(ashlar=(0.56, 0.58, 0.66), trim=(0.74, 0.76, 0.84), floor=(0.34, 0.35, 0.44), glow=(0.36, 0.62, 1.0),
                     glow_power=9.0, accent=(0.80, 0.50, 0.08), iron=(0.07, 0.075, 0.09), crate=(0.075, 0.08, 0.10),
                     wood=(0.62, 0.58, 0.58)),
    'Covenant': dict(ashlar=(0.62, 0.50, 0.36), trim=(0.80, 0.62, 0.36), floor=(0.52, 0.44, 0.40), glow=(1.0, 0.62, 0.12),
                     glow_power=9.0, accent=(0.85, 0.55, 0.10), iron=(0.10, 0.08, 0.07), crate=(0.30, 0.20, 0.12),
                     wood=(0.75, 0.55, 0.40)),
    'Neutral': dict(ashlar=(0.16, 0.14, 0.21), trim=(0.24, 0.21, 0.30), floor=(0.20, 0.18, 0.26), glow=(0.62, 0.22, 1.0),
                    glow_power=12.0, accent=(1.0, 0.16, 0.03), iron=(0.05, 0.05, 0.06), crate=(0.1, 0.1, 0.12),
                    wood=(0.3, 0.26, 0.26)),
    'Null': dict(ashlar=(0.20, 0.19, 0.22), trim=(0.28, 0.26, 0.30), floor=(0.30, 0.27, 0.32), glow=(1.0, 0.10, 0.06),
                 glow_power=10.0, accent=(0.55, 0.08, 0.06), iron=(0.05, 0.05, 0.06), crate=(0.12, 0.10, 0.11),
                 wood=(0.34, 0.28, 0.28)),
}
TILE = {'ashlar': 6.0, 'trim': 6.0, 'floor': 7.0, 'wood': 2.0}
_mats = {}


def _img(name):
    p = os.path.join(TEX, name)
    for im in bpy.data.images:
        if im.filepath and os.path.normcase(bpy.path.abspath(im.filepath)) == os.path.normcase(p): return im
    return bpy.data.images.load(p)


def mat(kind, faction):
    """Shared material per (kind, faction). kind: ashlar trim floor wood iron accent glow crate banner"""
    key = 'M_Crucible_%s_%s' % (kind.capitalize(), faction)
    if key in _mats: return _mats[key]
    P = PALETTE[faction]
    m = bpy.data.materials.new(key); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs[0], out.inputs[0])
    b.inputs['Specular IOR Level'].default_value = 0.25
    rough, metal, tex, tint = 0.8, 0.0, None, (1, 1, 1)
    if kind in ('ashlar', 'trim'): tex, tint, rough = 'T_Crucible_Ashlar.png', P[kind], 0.82
    elif kind == 'floor': tex, tint, rough = 'T_Crucible_Flagstone.png', P['floor'], 0.38
    elif kind == 'wood': tex, tint, rough = 'T_Crucible_Planks.png', tuple(c * 1.9 for c in P['wood']), 0.75
    elif kind == 'banner': tex, rough = 'T_Banner_%s.png' % faction, 0.9
    elif kind == 'iron': tint, rough, metal = P['iron'], 0.5, 0.6
    elif kind == 'accent': tint, rough, metal = P['accent'], 0.38, 0.75
    elif kind == 'crate': tint, rough, metal = P['crate'], 0.7, 0.0
    elif kind == 'rock': tint, rough = (0.035, 0.03, 0.055), 0.9
    elif kind == 'lava':
        tint = (1.0, 0.2, 0.03)
        b.inputs['Emission Color'].default_value = (1.0, 0.16, 0.02, 1); b.inputs['Emission Strength'].default_value = 7.0
    elif kind == 'glow':
        tint = P['glow']
        b.inputs['Emission Color'].default_value = (*P['glow'], 1); b.inputs['Emission Strength'].default_value = P['glow_power']
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    b.inputs['Base Color'].default_value = (*tint, 1)
    if tex:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = _img(tex)
        if tuple(tint) != (1, 1, 1):
            mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'
            mx.inputs[0].default_value = 1.0; mx.inputs[7].default_value = (*tint, 1)
            nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
        else:
            nt.links.new(t.outputs[0], b.inputs['Base Color'])
    if kind == 'banner': m.use_backface_culling = False
    m['crucible_kind'] = kind; m['crucible_faction'] = faction
    _mats[key] = m
    return m


class MB:
    """Tiny mesh builder: quads/ngons with a material kind each; UV0 = box projection / tile (or explicit)."""
    def __init__(self, faction):
        self.f = faction; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.kinds = []

    def _mi(self, kind):
        if kind not in self.kinds: self.kinds.append(kind)
        return self.kinds.index(kind)

    def poly(self, pts, kind, uvs=None, uvoff=(0.0, 0.0)):
        vs = [self.bm.verts.new(p) for p in pts]
        try: f = self.bm.faces.new(vs)
        except ValueError: return
        f.material_index = self._mi(kind)
        f.normal_update()
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i])); t = TILE.get(kind, 1.0)
        for i, l in enumerate(f.loops):
            if uvs: l[self.uv].uv = uvs[i]; continue
            c = l.vert.co
            u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y))
            l[self.uv].uv = (u / t + uvoff[0], v / t + uvoff[1])

    def box(self, lo, hi, kind, skip='', uvoff=(0.0, 0.0)):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        F = {'-x': [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)], '+x': [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
             '-y': [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], '+y': [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
             '-z': [(x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)], '+z': [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]}
        for k, p in F.items():
            if k not in skip.split(): self.poly(p, kind, uvoff=uvoff)

    def prism(self, ring, z0, z1, kind, cap=True, bottom=False, top_ring=None):
        """Extrude a CCW xy ring from z0 to z1 (optionally to a different top ring = taper)."""
        tr = top_ring or ring; n = len(ring)
        for i in range(n):
            a, b = ring[i], ring[(i + 1) % n]; c, d = tr[(i + 1) % n], tr[i]
            self.poly([(a[0], a[1], z0), (b[0], b[1], z0), (c[0], c[1], z1), (d[0], d[1], z1)], kind)
        if cap: self.poly([(p[0], p[1], z1) for p in tr], kind)
        if bottom: self.poly([(p[0], p[1], z0) for p in reversed(ring)], kind)

    def cham(self, lo, hi, c, kind, cap=True):
        """Box with chamfered vertical edges (cut-stone silhouette)."""
        x0, y0, z0 = lo; x1, y1, z1 = hi
        ring = [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c), (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]
        self.prism(ring, z0, z1, kind, cap=cap)

    def beam(self, p, q, w, d, kind, up=(0, 0, 1)):
        """Rectangular bar from p to q, width w (across), depth d (along 'thick' axis = cross(dir, across))."""
        p, q = Vector(p), Vector(q); dr = (q - p).normalized()
        thick = Vector((1, 0, 0)) if abs(dr.x) < 0.9 else Vector((0, 1, 0))
        thick = (thick - dr * thick.dot(dr)).normalized(); across = dr.cross(thick).normalized()
        a, t = across * w / 2, thick * d / 2
        c = [p - a - t, p + a - t, p + a + t, p - a + t]; e = [x + (q - p) for x in c]
        for i in range(4):
            self.poly([c[i], c[(i + 1) % 4], e[(i + 1) % 4], e[i]], kind)
        self.poly(list(reversed(c)), kind); self.poly(e, kind)

    def cyl(self, x, y, z0, z1, r, kind, n=8, cap=True):
        ring = [(x + r * math.cos(2 * math.pi * i / n), y + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        self.prism(ring, z0, z1, kind, cap=cap)

    def build(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for k in self.kinds: me.materials.append(mat(k, self.f))
        me['crucible_piece'] = name; me.use_fake_user = True
        return me


# ---------------------------------------------------------------- pieces. Local frame: X = along the wall,
# +Y = the face the players see (yard side), Z up, origin on the ground at the piece centre.

def wall(faction, L=10.0, H=10.0, T=3.0, name=None):
    b = MB(faction)
    b.box((-L / 2, -T / 2, 0), (L / 2, T / 2, H - 0.7), 'ashlar', skip='-x +x -z +z')
    b.box((-L / 2, -T / 2 - 0.3, 0), (L / 2, T / 2 + 0.3, 1.1), 'trim', skip='-x +x -z')        # plinth course
    b.box((-L / 2, -T / 2 - 0.25, H - 0.7), (L / 2, T / 2 + 0.25, H), 'trim', skip='-x +x')      # cap course
    n = max(1, int(L // 3.3)); step = L / n
    for i in range(n):                                                                         # merlons
        c = -L / 2 + step * (i + 0.5)
        b.cham((c - 1.25, T / 2 - 0.9, H), (c + 1.25, T / 2 + 0.15, H + 0.75), 0.12, 'ashlar')
    return b.build(name or 'KN_Crucible_Wall_%dm_%s' % (round(L), faction))


def pilaster(faction, W=4.0, D=2.2, H=13.0, sides=True, name=None, freestanding=False):
    """Square stone pier with inset vertical light strips (the Hegemony look in concept 02). Back at y=-D/2 unless freestanding."""
    b = MB(faction); w = W / 2
    y0, y1 = (-W / 2, W / 2) if freestanding else (-D / 2, D / 2)
    b.cham((-w - 0.35, y0 - 0.35, 0), (w + 0.35, y1 + 0.35, 1.3), 0.25, 'trim')                 # plinth
    b.cham((-w - 0.15, y0 - 0.15, 1.3), (w + 0.15, y1 + 0.15, 1.75), 0.2, 'ashlar')
    b.cham((-w, y0, 1.75), (w, y1, H - 2.6), 0.22, 'ashlar', cap=False)                         # shaft
    b.cham((-w - 0.3, y0 - 0.3, H - 2.6), (w + 0.3, y1 + 0.3, H - 1.5), 0.25, 'trim')           # cap, stepped
    b.cham((-w + 0.25, y0 + 0.25, H - 1.5), (w - 0.25, y1 - 0.25, H - 0.5), 0.2, 'ashlar')
    b.cham((-w + 0.8, y0 + 0.8, H - 0.5), (w - 0.8, y1 - 0.8, H), 0.15, 'trim')
    faces = [(0, 1)] + ([(1, 0), (-1, 0)] if sides else []) + ([(0, -1)] if freestanding else [])
    for dx, dy in faces:
        sw = 0.34 if (dx == 0) else 0.26                         # strip half width
        z0, z1 = 3.0, H - 3.4
        if dx == 0:
            yf = (y1 if dy > 0 else y0); s = 1 if dy > 0 else -1
            b.box((-sw - 0.22, min(yf, yf + s * 0.07), z0 - 0.25), (sw + 0.22, max(yf, yf + s * 0.07), z1 + 0.25), 'iron')    # dark frame
            b.box((-sw, min(yf, yf + s * 0.13), z0), (sw, max(yf, yf + s * 0.13), z1), 'glow')
            b.box((-w + 0.5, min(yf + s * 0.3, yf + s * 0.38), H - 2.35), (w - 0.5, max(yf + s * 0.3, yf + s * 0.38), H - 1.8), 'glow')  # cap bar
        else:
            xf = dx * w; yc = (y0 + y1) / 2
            b.box((min(xf, xf + dx * 0.07), yc - sw - 0.2, z0 + 1.0), (max(xf, xf + dx * 0.07), yc + sw + 0.2, z1 - 0.5), 'iron')
            b.box((min(xf, xf + dx * 0.13), yc - sw, z0 + 1.2), (max(xf, xf + dx * 0.13), yc + sw, z1 - 0.7), 'glow')
    return b.build(name or 'KN_Crucible_Pilaster_%s' % faction)


def gate_tower(faction, W=5.5, D=8.0, H=15.0):
    """Big tower flanking the muster gate. Origin at tower centre; +Y = yard side."""
    b = MB(faction); w = W / 2; d = D / 2
    b.cham((-w - 0.4, -d - 0.4, 0), (w + 0.4, d + 0.4, 1.5), 0.3, 'trim')
    b.cham((-w, -d, 1.5), (w, d, H - 3.2), 0.3, 'ashlar', cap=False)
    b.cham((-w - 0.2, -d - 0.2, 7.9), (w + 0.2, d + 0.2, 8.5), 0.3, 'trim', cap=True)            # belt course
    b.cham((-w - 0.45, -d - 0.45, H - 3.2), (w + 0.45, d + 0.45, H - 1.9), 0.3, 'trim')
    b.cham((-w + 0.2, -d + 0.2, H - 1.9), (w - 0.2, d - 0.2, H - 0.7), 0.25, 'ashlar')
    for sx in (-1, 1):
        for sy in (-1, 1):                                                                     # corner merlons
            cx, cy = sx * (w - 0.75), sy * (d - 0.75)
            b.cham((cx - 0.75, cy - 0.75, H - 0.7), (cx + 0.75, cy + 0.75, H + 0.5), 0.15, 'trim')
    for s in (1, -1):                                                                          # front and back strips
        yf = s * d
        for cx in (-1.2, 1.2):
            b.box((cx - 0.5, min(yf, yf + s * 0.07), 3.0), (cx + 0.5, max(yf, yf + s * 0.07), 7.4), 'iron')
            b.box((cx - 0.3, min(yf, yf + s * 0.13), 3.25), (cx + 0.3, max(yf, yf + s * 0.13), 7.15), 'glow')
        b.box((-0.55, min(yf, yf + s * 0.07), 9.0), (0.55, max(yf, yf + s * 0.07), H - 3.7), 'iron')
        b.box((-0.34, min(yf, yf + s * 0.13), 9.25), (0.34, max(yf, yf + s * 0.13), H - 3.95), 'glow')
        b.box((-w + 0.6, min(yf + s * 0.45, yf + s * 0.53), H - 2.85), (w - 0.6, max(yf + s * 0.45, yf + s * 0.53), H - 2.25), 'glow')
    for s in (1, -1):                                                                          # side strips
        xf = s * w
        b.box((min(xf, xf + s * 0.07), -0.5, 9.0), (max(xf, xf + s * 0.07), 0.5, H - 3.7), 'iron')
        b.box((min(xf, xf + s * 0.13), -0.3, 9.25), (max(xf, xf + s * 0.13), 0.3, H - 3.95), 'glow')
    return b.build('KN_Crucible_GateTower_%s' % faction)


def gate_lintel(faction, L=12.0, T=4.0, z0=8.0, H=11.0):
    b = MB(faction)
    b.box((-L / 2, -T / 2, z0), (L / 2, T / 2, H - 0.7), 'ashlar', skip='-x +x')
    b.box((-L / 2, -T / 2 - 0.3, z0), (L / 2, T / 2 + 0.3, z0 + 0.8), 'trim', skip='-x +x')
    b.box((-L / 2, -T / 2 - 0.25, H - 0.7), (L / 2, T / 2 + 0.25, H), 'trim', skip='-x +x')
    for s in (1, -1):
        yf = s * (T / 2 + 0.3)
        b.box((-1.1, min(yf, yf + s * 0.25), z0 - 0.5), (1.1, max(yf, yf + s * 0.25), z0 + 1.3), 'accent')     # keystone bracket
        for cx in (-4.2, 4.2):
            b.box((cx - 0.45, min(yf, yf + s * 0.2), z0 - 0.3), (cx + 0.45, max(yf, yf + s * 0.2), z0 + 1.0), 'accent')
        b.box((-3.2, min(yf - s * 0.02, yf + s * 0.1), z0 + 1.45), (3.2, max(yf - s * 0.02, yf + s * 0.1), z0 + 1.8), 'glow')
    for i in range(4):
        c = -L / 2 + L / 4 * (i + 0.5)
        b.cham((c - 1.0, -T / 2, H), (c + 1.0, -T / 2 + 1.0, H + 1.0), 0.12, 'ashlar')
        b.cham((c - 1.0, T / 2 - 1.0, H), (c + 1.0, T / 2, H + 1.0), 0.12, 'ashlar')
    return b.build('KN_Crucible_GateLintel_%s' % faction)


def gate_leaf(faction, mirror=False, W=6.0, H=8.0):
    """Braced plank gate leaf, both faces detailed. Local frame matches the 007 leaf: width along Y (-3..3), thickness X, z 0..8."""
    b = MB(faction); h = W / 2; s = -1 if mirror else 1
    b.box((-0.12, -h, 0), (0.12, h, H), 'wood')
    for fx in (0.12, -0.12):
        o = 0.16 if fx > 0 else -0.16
        x0, x1 = min(fx, fx + o), max(fx, fx + o)
        for z in (0.0, 3.7, 7.4):                                   # rails
            b.box((x0, -h, z), (x1, h, z + 0.6), 'iron')
        for y in (-h, h - 0.55):                                    # stiles
            b.box((x0, y, 0.6), (x1, y + 0.55, 7.4), 'iron')
        xm = fx + o / 2
        b.beam((xm, -s * (h - 0.55), 0.6), (xm, s * (h - 0.55), 3.7), 0.5, abs(o), 'iron')   # diagonal braces
        b.beam((xm, s * (h - 0.55), 4.3), (xm, -s * (h - 0.55), 7.4), 0.5, abs(o), 'iron')
        o2 = 0.26 if fx > 0 else -0.26
        a0, a1 = min(fx, fx + o2), max(fx, fx + o2)
        for z in (1.3, 4.0, 6.5):                                   # gold strap hinges on the outer edge
            yo = s * h
            b.box((a0, min(yo, yo - s * 1.9), z), (a1, max(yo, yo - s * 1.9), z + 0.42), 'accent')
        yi = -s * h                                                 # gold lock plates at the meeting edge
        b.box((a0, min(yi, yi + s * 0.7), 3.4), (a1, max(yi, yi + s * 0.7), 4.6), 'accent')
        b.box((a0, min(yi, yi + s * 0.5), 0.0), (a1, max(yi, yi + s * 0.5), 0.9), 'accent')
        b.box((a0, min(yi, yi + s * 0.5), 7.1), (a1, max(yi, yi + s * 0.5), 8.0), 'accent')
    return b.build('KN_Crucible_GateLeaf_%s_%s' % (faction, 'R' if mirror else 'L'))


def banner(faction, W=3.0, Hc=7.0):
    """Hanging sigil banner on an iron bracket. Origin = top centre of the bar, cloth hangs down in the XZ plane, faces +Y."""
    b = MB(faction)
    b.beam((-W / 2 - 0.35, 0.55, 0), (W / 2 + 0.35, 0.55, 0), 0.16, 0.16, 'iron')
    for x in (-W / 2 - 0.15, W / 2 + 0.15):
        b.beam((x, 0.0, 0), (x, 0.62, 0), 0.14, 0.14, 'iron')
        b.beam((x, 0.0, -0.9), (x, 0.5, -0.05), 0.1, 0.1, 'iron')
        b.box((x - 0.14, 0.42, -0.14), (x + 0.14, 0.68, 0.14), 'accent')
    y = 0.5; pts = [(-W / 2, y, -0.1), (-W / 2, y + 0.06, -Hc * 0.5), (-W / 2, y, -Hc * 0.86), (0, y + 0.1, -Hc), (W / 2, y, -Hc * 0.86), (W / 2, y + 0.06, -Hc * 0.5), (W / 2, y, -0.1)]
    uvs = [(0, 1), (0, 0.5), (0, 0.14), (0.5, 0), (1, 0.14), (1, 0.5), (1, 1)]
    # fan as 3 quads/tri so the slight fold stays planar enough
    b.poly([pts[0], pts[1], pts[5], pts[6]], 'banner', [uvs[0], uvs[1], uvs[5], uvs[6]])
    b.poly([pts[1], pts[2], pts[4], pts[5]], 'banner', [uvs[1], uvs[2], uvs[4], uvs[5]])
    b.poly([pts[2], pts[3], pts[4]], 'banner', [uvs[2], uvs[3], uvs[4]])
    return b.build('KN_Crucible_Banner_%s' % faction)


def crate(faction, sx=2.0, sy=1.1, sz=1.0, name='A'):
    """Supply crate: dark case, raised frame, accent corner brackets and latch (concept 02 foreground)."""
    b = MB(faction); x, y = sx / 2, sy / 2; f = 0.12; e = 0.05
    b.box((-x + e, -y + e, e), (x - e, y - e, sz - e), 'crate', skip='-z')
    for (a, c) in ((-x, -x + f), (x - f, x)):                           # end frames
        b.box((a, -y, 0), (c, y, sz), 'crate', skip='-z')
    for (a, c) in ((-y, -y + f), (y - f, y)):
        b.box((-x + f, a, 0), (x - f, c, f), 'crate', skip='-z -x +x'); b.box((-x + f, a, sz - f), (x - f, c, sz), 'crate', skip='-x +x')
    lid = sz * 0.68
    b.box((-x - 0.01, -y - 0.01, lid), (x + 0.01, y + 0.01, lid + 0.06), 'iron', skip='-z +z')      # lid seam
    k = 0.24
    for cx in (-1, 1):
        for cy in (-1, 1):
            for cz in (0, 1):                                           # 8 corner brackets
                px = cx * x; py = cy * y; pz = cz * sz
                lo = (min(px, px - cx * k) - 0.02 * (cx < 0) , min(py, py - cy * k) - 0.02 * (cy < 0), min(pz, pz + (0.3 if cz == 0 else -0.3)))
                hi = (max(px, px - cx * k) + 0.02 * (cx > 0), max(py, py - cy * k) + 0.02 * (cy > 0), max(pz, pz + (0.3 if cz == 0 else -0.3)) + (0.02 if cz else 0))
                b.box(lo, hi, 'accent', skip='-z' if cz == 0 else '')
    for s in (1, -1):                                                   # latch + handle plate
        yf = s * y
        b.box((-0.22, min(yf, yf + s * 0.05), lid - 0.22), (0.22, max(yf, yf + s * 0.05), lid + 0.2), 'accent')
    me = b.build('KN_Crucible_Crate%s_%s' % (name, faction)); me['h'] = sz
    return me


def pipe(faction, H=9.0, r=0.38):
    b = MB(faction)
    b.cyl(0, 0, 0, H, r, 'iron', n=8)
    for z in (0.0, H * 0.33, H * 0.66, H - 0.5):
        b.cyl(0, 0, z, z + 0.5, r + 0.14, 'iron', n=8)
    b.cyl(0, 0, H * 0.33 + 0.5, H * 0.33 + 0.75, r + 0.16, 'accent', n=8)
    b.box((-0.3, -0.9, H * 0.66), (0.3, 0, H * 0.66 + 0.4), 'iron')
    b.box((-0.3, -0.9, 0.9), (0.3, 0, 1.3), 'iron')
    return b.build('KN_Crucible_Pipe_%s' % faction)


def rubble(faction, seed, size=0.5):
    rnd = random.Random(seed); b = MB(faction)
    n = 6; ring = []; top = []
    for i in range(n):
        a = 2 * math.pi * i / n + rnd.uniform(-0.3, 0.3); r = size * rnd.uniform(0.6, 1.1)
        ring.append((r * math.cos(a), r * math.sin(a))); top.append((r * 0.6 * math.cos(a + 0.2) + size * 0.1, r * 0.6 * math.sin(a + 0.2)))
    b.prism(ring, 0, size * rnd.uniform(0.5, 0.9), 'floor', top_ring=top)
    return b.build('KN_Crucible_Rubble%d_%s' % (seed, faction))


def retexture(obj, kind, faction):
    """Give an existing layout mesh (floor slab, rampart, stair) the new stone, box-projected in its own space."""
    me = obj.data
    if me.users > 1: me = obj.data = me.copy()
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify(); t = TILE.get(kind, 4.0)
    for f in bm.faces:
        f.material_index = 0
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y))
            l[uv].uv = (u / t, v / t)
    bm.to_mesh(me); bm.free()
    me.materials.clear(); me.materials.append(mat(kind, faction))


def mountain(seed, R=34.0, H=165.0):
    """Backdrop peak cluster: jagged black rock spikes with a lava glow at the foot (non-playable)."""
    rnd = random.Random(seed); b = MB('Neutral')
    for i in range(rnd.randint(4, 7)):
        cx, cy = rnd.uniform(-R, R), rnd.uniform(-R * 0.5, R * 0.5)
        r = R * rnd.uniform(0.5, 0.95); h = H * rnd.uniform(0.3, 1.0) * (1.0 - abs(cx) / (R * 1.6))
        n = rnd.randint(5, 7); a0 = rnd.uniform(0, 6.28)
        ring = [(cx + r * rnd.uniform(0.75, 1.1) * math.cos(a0 + 6.2832 * k / n), cy + r * rnd.uniform(0.75, 1.1) * math.sin(a0 + 6.2832 * k / n)) for k in range(n)]
        ax, ay = cx + rnd.uniform(-r, r) * 0.35, cy + rnd.uniform(-r, r) * 0.3; t = r * 0.16
        mid = [((p[0] - cx) * 0.72 + cx + (ax - cx) * 0.4, (p[1] - cy) * 0.72 + cy + (ay - cy) * 0.4) for p in ring]
        top = [(ax + t * math.cos(6.2832 * k / n), ay + t * math.sin(6.2832 * k / n)) for k in range(n)]
        b.prism(ring, -12, h * 0.62, 'rock', cap=False, top_ring=mid); b.prism(mid, h * 0.62, h, 'rock', top_ring=top)
    for i in range(rnd.randint(2, 4)):                      # lava seams on the slope facing the arena (-Y local)
        x = rnd.uniform(-R, R) * 0.8; w = rnd.uniform(1.5, 3.2); z = rnd.uniform(25, 75); y = -R * 0.66
        pts = [(x, y, -8), (x + w, y, -8), (x + w * 1.6 + rnd.uniform(-9, 9), y + 9, z), (x + w * 0.8 + rnd.uniform(-9, 9), y + 9, z)]
        b.poly(pts, 'lava')
    return b.build('KN_Crucible_Mountain%d' % seed)


def tris(me): return sum(len(p.vertices) - 2 for p in me.polygons)


# ================================================================ centrepieces from the owner's close-up refs (16 Nov)
def _ring(r, n, a0=0.0, sx=1.0, sy=1.0, ox=0.0, oy=0.0):
    return [(ox + r * sx * math.cos(a0 + 2 * math.pi * k / n), oy + r * sy * math.sin(a0 + 2 * math.pi * k / n)) for k in range(n)]


def stepped_dais(faction, steps, n=24, name='ShrineDais', riser='ashlar', tread='trim', band=None, a0=0.0):
    """Round (or n-sided) stepped dais. steps = [(radius, top height)] from the outside in; risers and treads get their own material."""
    b = MB(faction); z0 = 0.0
    for r, z in steps:
        ring = _ring(r, n, a0)
        b.prism(ring, z0, z, riser, cap=False)
        b.poly([(p[0], p[1], z) for p in ring], tread)
        z0 = z
    if band:                                            # a lit / gilded band round a drum: (radius, z0, z1, kind)
        b.prism(_ring(band[0], n, a0), band[1], band[2], band[3], cap=False)
    return b.build('KN_Crucible_%s_%s' % (name, faction))


def shrine_block(faction, inlay='glow'):
    """Angled buttress block that rings the shrine dais (refs 03 / 04): tall at the inner end, sloping outward, lit or gilded slot on the slope."""
    b = MB(faction)
    A = [(0.0, -0.8, 0.0), (0.0, 0.8, 0.0), (2.3, 0.85, 0.0), (2.3, -0.85, 0.0)]
    T = [(0.0, -0.62, 1.9), (0.0, 0.62, 1.9), (0.55, 0.66, 1.9), (2.3, 0.72, 0.72), (2.3, -0.72, 0.72), (0.55, -0.66, 1.9)]
    b.poly([A[0], T[0], T[1], A[1]], 'ashlar')
    b.poly([A[1], T[1], T[2], T[3], A[2]], 'ashlar'); b.poly([A[3], T[4], T[5], T[0], A[0]], 'ashlar')
    b.poly([A[2], T[3], T[4], A[3]], 'ashlar')
    b.poly([T[0], T[5], T[2], T[1]], 'trim'); b.poly([T[5], T[4], T[3], T[2]], 'trim')
    b.poly([(0.75, -0.26, 1.80), (2.12, -0.3, 0.875), (2.12, 0.3, 0.875), (0.75, 0.26, 1.80)], inlay)
    b.poly([(2.32, -0.3, 0.12), (2.32, 0.3, 0.12), (2.32, 0.3, 0.6), (2.32, -0.3, 0.6)], inlay)
    return b.build('KN_Crucible_ShrineBlock_%s' % faction)


def shrine_crystal(faction):
    """The glowing centrepiece of each Rally Point: Covenant gold obelisk, Null red shard, Hegemony slim cyan crystal."""
    b = MB(faction)
    if faction == 'Covenant':                           # tall six-sided obelisk with a long point (ref 03)
        b.prism(_ring(0.95, 6, 0.52), 0.0, 5.0, 'glow', cap=False, top_ring=_ring(0.80, 6, 0.52))
        b.prism(_ring(0.80, 6, 0.52), 5.0, 7.2, 'glow', top_ring=_ring(0.03, 6, 0.52))
        b.prism(_ring(1.25, 6, 0.52), 0.0, 0.5, 'accent')
    elif faction == 'Null':                             # jagged leaning shard with two splinters (ref 04)
        b.prism(_ring(1.05, 5, 0.3, 1.0, 0.75), 0.0, 3.2, 'glow', cap=False, top_ring=_ring(0.85, 5, 0.3, 1.0, 0.7, 0.25, 0.1))
        b.prism(_ring(0.85, 5, 0.3, 1.0, 0.7, 0.25, 0.1), 3.2, 7.4, 'glow', top_ring=_ring(0.03, 5, 0.3, 1, 1, 0.55, 0.2))
        b.prism(_ring(0.5, 4, 0.9, 1, 0.7, -0.95, 0.35), 0.0, 1.6, 'glow', cap=False, top_ring=_ring(0.4, 4, 0.9, 1, 0.7, -1.05, 0.4))
        b.prism(_ring(0.4, 4, 0.9, 1, 0.7, -1.05, 0.4), 1.6, 3.4, 'glow', top_ring=_ring(0.02, 4, 0.9, 1, 1, -1.3, 0.5))
        b.prism(_ring(0.42, 4, 0.2, 1, 0.8, 0.9, -0.7), 0.0, 1.2, 'glow', cap=False, top_ring=_ring(0.34, 4, 0.2, 1, 0.8, 1.0, -0.75))
        b.prism(_ring(0.34, 4, 0.2, 1, 0.8, 1.0, -0.75), 1.2, 2.6, 'glow', top_ring=_ring(0.02, 4, 0.2, 1, 1, 1.2, -0.9))
    else:                                               # Hegemony: slim double-pointed crystal floating in the beam (ref 02)
        b.prism(_ring(0.03, 6), 0.9, 1.9, 'glow', cap=False, top_ring=_ring(0.5, 6))
        b.prism(_ring(0.5, 6), 1.9, 4.6, 'glow', cap=False, top_ring=_ring(0.42, 6))
        b.prism(_ring(0.42, 6), 4.6, 6.4, 'glow', top_ring=_ring(0.03, 6))
    return b.build('KN_Crucible_ShrineCrystal_%s' % faction)


def tech_plinth(faction='Hegemony'):
    """Hegemony shrine: low hexagonal tech plinth with corner lugs and a lit emitter in the middle (ref 02)."""
    b = MB(faction); a0 = math.pi / 6
    for r, z0, z1, k in ((4.6, 0.0, 0.3, 'crate'), (4.0, 0.3, 0.6, 'crate'), (3.4, 0.6, 0.9, 'crate')):
        ring = _ring(r, 6, a0); b.prism(ring, z0, z1, k, cap=False); b.poly([(p[0], p[1], z1) for p in ring], 'trim')
    b.prism(_ring(2.0, 6, a0), 0.9, 1.05, 'iron'); b.prism(_ring(1.55, 6, a0), 1.05, 1.12, 'glow')
    b.prism(_ring(3.45, 6, a0), 0.62, 0.74, 'glow', cap=False)
    for k in range(6):
        a = a0 + k * math.pi / 3; cx, cy = 4.3 * math.cos(a), 4.3 * math.sin(a)
        ring = [(cx + 0.75 * math.cos(a + t) * (1.0 if i % 2 == 0 else 0.55), cy + 0.75 * math.sin(a + t) * (1.0 if i % 2 == 0 else 0.55)) for i, t in enumerate((0.0, 1.5708, 3.1416, 4.7124))]
        b.prism(ring, 0.0, 1.15, 'iron'); b.prism([(cx + (p[0] - cx) * 0.5, cy + (p[1] - cy) * 0.5) for p in ring], 1.15, 1.3, 'accent')
    return b.build('KN_Crucible_ShrinePlinth_%s' % faction)


def menhir(seed, H=6.0):
    """Angular leaning standing stone with a chiselled top (ref 01): the broken spires round the heart."""
    rnd = random.Random(seed); b = MB('Neutral'); n = rnd.choice((4, 5, 5, 6))
    L, W = rnd.uniform(0.75, 1.15), rnd.uniform(0.42, 0.62)
    base = [(L * math.cos(2 * math.pi * k / n + 0.3) * rnd.uniform(0.85, 1.1), W * math.sin(2 * math.pi * k / n + 0.3) * rnd.uniform(0.85, 1.1)) for k in range(n)]
    ox, oy = rnd.uniform(-0.2, 0.2), rnd.uniform(-0.12, 0.12)
    mid = [(p[0] * 0.86 + ox, p[1] * 0.86 + oy) for p in base]
    top = [(p[0] * rnd.uniform(0.25, 0.6) + ox * 2.2, p[1] * rnd.uniform(0.4, 0.7) + oy * 2.2) for p in base]
    zt = [H * rnd.uniform(0.80, 1.0) for _ in range(n)]; zt[rnd.randrange(n)] = H
    b.prism(base, -0.4, H * 0.58, 'ashlar', cap=False, top_ring=mid)
    for i in range(n):
        j = (i + 1) % n
        b.poly([(mid[i][0], mid[i][1], H * 0.58), (mid[j][0], mid[j][1], H * 0.58), (top[j][0], top[j][1], zt[j]), (top[i][0], top[i][1], zt[i])], 'ashlar')
    cx = sum(p[0] for p in top) / n; cy = sum(p[1] for p in top) / n; cz = sum(zt) / n
    for i in range(n):
        j = (i + 1) % n; b.poly([(top[i][0], top[i][1], zt[i]), (top[j][0], top[j][1], zt[j]), (cx, cy, cz)], 'trim')
    return b.build('KN_Crucible_Menhir%d' % seed)


def dais_ring_block(r0=11.55, r1=13.1, span=0.1676, H=0.95):
    """One thick curved kerb block of the heart dais (ref 01). Built at angle 0; rotate about the heart to place."""
    b = MB('Neutral'); m = 4
    def arc(r, sp): return [(r * math.cos(-sp / 2 + sp * k / m), r * math.sin(-sp / 2 + sp * k / m)) for k in range(m + 1)]
    ring = arc(r0, span) + list(reversed(arc(r1, span)))
    c = 0.14; top = arc(r0 + c, span - 2 * c / r0) + list(reversed(arc(r1 - c, span - 2 * c / r1)))
    b.prism(ring, -0.2, H - c, 'ashlar', cap=False)
    b.prism(ring, H - c, H, 'ashlar', cap=False, top_ring=top)
    b.poly([(p[0], p[1], H) for p in top], 'trim')
    return b.build('KN_Crucible_DaisRingBlock')


def finial(faction='Covenant'):
    """Stepped pointed tower top with a gilded spike (Covenant wall tops, top-down ref 03). Unit base 1 x 1 m; scale to the tower."""
    b = MB(faction)
    b.box((-0.5, -0.5, 0.0), (0.5, 0.5, 0.22), 'trim', skip='-z')
    b.prism([(-0.42, -0.42), (0.42, -0.42), (0.42, 0.42), (-0.42, 0.42)], 0.22, 1.25, 'ashlar', top_ring=[(-0.08, -0.08), (0.08, -0.08), (0.08, 0.08), (-0.08, 0.08)])
    b.prism([(-0.06, -0.06), (0.06, -0.06), (0.06, 0.06), (-0.06, 0.06)], 1.25, 2.0, 'accent', top_ring=[(-0.01, -0.01), (0.01, -0.01), (0.01, 0.01), (-0.01, 0.01)])
    b.box((-0.13, -0.13, 1.42), (0.13, 0.13, 1.58), 'accent')
    return b.build('KN_Crucible_Finial_%s' % faction)
