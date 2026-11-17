# Checkpoint 021 = 020 held to the owner's world scale (16_world_scale.md: one unit = one metre, a person is 1.8 m, a made thing is true
# size and at most 25 % over; grand things are a stated ratio and never scaled more than 1.5):
#   crate largest side <= 1.5 m, barrel <= 1.25 m, brazier bowl <= 1.6 m (1.5 x on a monument pillar), banner 3.6 / 4.5 / 6.0 m (6.0 = grand,
#   3.3 persons), shrine crystal 5.5 to 7.4 m (grand, 4 persons), cages / skulls / awnings / chains at the size their pack drew them.
#   Set by the layout (fitHeight, stated): yard wall 10 m (5.6 persons), towers 15 to 16 m, gate 12 x 8 m (monumental), tier blocks 1.07 m.
# Sidekick figures (ScaleFigure_*, preview only) stand at the landings, gates, crates, shrines, tiers, stairs and the dais.
# Checkpoint 020 = 019 + the bowl rebuilt from the owner's overhead concept (arena_bowl.py): seven rings of big stone blocks instead of
# seat rows, arena floor radius 82.3 m, grand stairs with stepped cheek walls, twelve flame pillars at the floor edge.
# Checkpoint 019 = 018 + owner calls: four grand stairs only (diagonals become seats), bowl 7.5 m deep, the same stepped gate threshold
# for all three factions, landing points at yard-local x = 130, respawn stand points on the shrine platforms (LAYOUT_022.json).
# Checkpoint 018 = 017 + owner answers: shrine daises about 8 m (the spawn row at 6 m from the shrine moves to the gate side, LAYOUT_018.json),
# painted stairs up to Null's gate (a 1.2 m threshold with four steps each side).
# Checkpoint 017 = 016 + the owner's arena overhead ref: seat-step tiers, purple flame pillars at every ref spot, slab paths to the
# dais, cover in two rings, Null yard floor without lava. The eight MINOR stairs (not part of any validated route) are replaced by seats.
# Checkpoint 016 = 015 + the owner's seven close-up refs: raised heart dais with kerb blocks and menhir clusters, a crystal
# centrepiece on a stepped dais in all three Rally Points, wall tops per faction (light caps / finials / fire).
# Checkpoint 015 = 014 + Null gate fires, triangle budget under 480k (owner: heart layout stays as built).
# Checkpoint 014 = 013 + bluer Hegemony light, wandering lava, shrine dais blocks, triangle budget.
# Checkpoint 013 = 012 + the FLOOR pass (owner: floors must not repeat or look like the walls): unique floor paintings
# from floors.py on every big floor, paving on every walkable top, wall brick only on vertical faces.
# (012: second look pass on 011: denser heart, blacker Null, rounder bowl read, triangle budget): The Crucible as a STANDALONE map, Synty-first, on the validated 007 layout (Fable session, 16 Nov 2026).
# Owner: "one-to-one my concepts using the Synty assets available, and use Blender for other things"; sky = a skybox in Unity;
# live effects (flames, lava, glow, aurora, cloth) are Unity's and are marked here with FX_<Type>_<Area>_<nn> empties.
# blender -b Crucible_007_KitLayout.blend --python Build009/build_022.py -- <out.blend>
import bpy, bmesh, sys, os, math, random, re, json
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parts, synty, arena_bowl as AB

OUT = sys.argv[sys.argv.index('--') + 1]
YAW = {'Covenant': 0.0, 'Hegemony': 180.0, 'Null': -90.0}
D = bpy.data
DF = 'PolygonDarkFortress'; KN = 'PolygonKnights'; SC = 'PolygonSciFiCity'; SS = 'PolygonSciFiSpace'; SW = 'PolygonSciFiWorlds'
DFa = 'PolygonDarkFantasy'; FK = 'PolygonFantasyKingdom'; ER = 'PolygonElvenRealm'; DR = 'PolygonDungeonRealms'; NB = 'PolygonNatureBiomes'
FX = []
STYLE = {
    'Hegemony': dict(brick=(1.5, 1.53, 1.66), crown=(1.25, 1.27, 1.35), floor=(0.62, 0.63, 0.72), glowname='cool blue-white (0.36, 0.62, 1.0)', cloth='gold cloth, pale sun sigil'),
    'Covenant': dict(brick=(1.55, 1.10, 0.66), crown=(1.5, 1.05, 0.6), floor=(0.66, 0.52, 0.42), glowname='amber-gold', cloth='red cloth, gold sun sigil'),
    'Null': dict(brick=(0.31, 0.29, 0.34), crown=(0.42, 0.38, 0.44), floor=(0.34, 0.25, 0.30), glowname='red', cloth='dark red cloth, gold sigil'),
    'Neutral': dict(brick=(1.05, 0.95, 1.30), crown=(1.0, 0.9, 1.2), floor=(0.95, 0.82, 1.15), glowname='violet', cloth=''),
}


def coll(name):
    c = D.collections.get(name)
    if not c: c = D.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c


_k = {}
def K(pack, name):
    """Synty prefab mesh re-pivoted to its bounding-box centre on the ground ('pivot' keeps the offset for Unity)."""
    if (pack, name) in _k: return _k[(pack, name)]
    me = synty.kit_mesh(pack, name).copy(); me.name = 'KITC_' + name
    xs = [v.co for v in me.vertices]
    lo = Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs))); hi = Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
    off = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    me.transform(Matrix.Translation(-off)); me['pivot'] = list(off); me['dim'] = list(hi - lo); me.use_fake_user = True
    _k[(pack, name)] = me
    return me


def dim(me): return Vector(me['dim'])


_var = {}
def tint_mat(m, tag, tint=None, glow=None, glow_strength=7.0, selfglow=None, rough=None):
    """Material variant: multiply tint, constant glow colour, or texture-as-emission (preview stand-in for Unity emissive)."""
    key = m.name + '__' + tag
    if key in D.materials: return D.materials[key]
    m2 = m.copy(); m2.name = key; nt = m2.node_tree
    b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); t = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None)
    if tint:
        mx = next((n for n in nt.nodes if n.type == 'MIX'), None)
        if mx: mx.inputs[7].default_value = (*tint, 1)
        elif t:
            mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = 1.0
            mx.inputs[7].default_value = (*tint, 1); nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
        else:
            c = b.inputs['Base Color'].default_value; b.inputs['Base Color'].default_value = (c[0] * tint[0], c[1] * tint[1], c[2] * tint[2], 1)
    if glow:
        for l in list(b.inputs['Emission Color'].links): nt.links.remove(l)
        b.inputs['Emission Color'].default_value = (*glow, 1); b.inputs['Emission Strength'].default_value = glow_strength
    if selfglow and t:
        nt.links.new(t.outputs[0], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = selfglow
    if rough is not None: b.inputs['Roughness'].default_value = rough
    m2['variant_of'] = m.name; m2['variant'] = tag
    return m2


def variant(me, tag, **kw):
    key = me.name + '__' + tag
    if key in _var: return _var[key]
    m = me.copy(); m.name = key; m.use_fake_user = True
    for i, mt in enumerate(m.materials):
        if mt: m.materials[i] = tint_mat(mt, tag, **kw)
    for k in ('pivot', 'dim', 'synty_pack', 'synty_prefab'):
        if k in me: m[k] = me[k]
    m['material_variant'] = tag
    _var[key] = m
    return m


def fxmark(name, typ, loc, colour, note, area, power=0.0, lightcol=None):
    e = D.objects.new(name, None); e.empty_display_size = 0.6; e.location = loc
    coll('FX_Markers_For_Unity').objects.link(e); e['fx_type'] = typ; e['fx_colour'] = colour; e['area'] = area
    FX.append((typ, area, colour, note))
    if power:
        l = D.lights.new('PreviewLight', 'POINT'); l.energy = power * 1.7; l.shadow_soft_size = 0.6; l.use_shadow = False
        l.color = lightcol or (1, 1, 1)
        lo = D.objects.new('PreviewLight_' + name[3:], l); lo.location = loc; coll('Lights_PreviewOnly').objects.link(lo)
    return e


_fxn = {}
def fx(typ, area, loc, colour, note, power=0.0, lightcol=None):
    k = typ + '_' + area; _fxn[k] = _fxn.get(k, 0) + 1
    return fxmark('FX_%s_%02d' % (k, _fxn[k]), typ, loc, colour, note, area, power, lightcol)


def synty_surface(obj, mat_rel, pack, tile, tag=None, tint=None, rough=None):
    """A kept layout mesh (floor slab, rampart, stair, tier) gets a Synty tiling material, box-projected in metres."""
    m = synty.material(os.path.join(synty.SYN, pack, 'Materials', mat_rel), pack)['mat']
    if tag: m = tint_mat(m, tag, tint=tint, rough=rough)
    me = obj.data
    if me.users > 1: me = obj.data = me.copy()
    bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        f.material_index = 0
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y))
            l[uv].uv = (u / tile, v / tile)
    bm.to_mesh(me); bm.free()
    me.materials.clear(); me.materials.append(m); me['synty_material'] = mat_rel


def custom_surface(obj, name, base, emit, tile, tint=(1, 1, 1), strength=5.0):
    m = D.materials.get(name)
    if not m:
        m = D.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = parts._img(base)
        mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = 1.0; mx.inputs[7].default_value = (*tint, 1)
        nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color']); b.inputs['Roughness'].default_value = 0.6
        if emit:
            e = nt.nodes.new('ShaderNodeTexImage'); e.image = parts._img(emit); nt.links.new(e.outputs[0], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = strength
    me = obj.data
    if me.users > 1: me = obj.data = me.copy()
    bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        f.material_index = 0
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y))
            l[uv].uv = (u / tile, v / tile)
    bm.to_mesh(me); bm.free(); me.materials.clear(); me.materials.append(m)


FTEX = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Textures', 'Crucible013')
def fimg(name):
    p = os.path.join(FTEX, name)
    for im in D.images:
        if im.filepath and os.path.normcase(bpy.path.abspath(im.filepath)) == os.path.normcase(p): return im
    return D.images.load(p)


def floor_mat(name, tex, emit=None, strength=6.0, tint=None, rough=0.55):
    m = D.materials.get(name)
    if m: return m
    m = D.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = fimg(tex); b.inputs['Roughness'].default_value = rough; b.inputs['Specular IOR Level'].default_value = 0.3
    if tint:
        mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = 1.0; mx.inputs[7].default_value = (*tint, 1)
        nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
    else: nt.links.new(t.outputs[0], b.inputs['Base Color'])
    if emit:
        e = nt.nodes.new('ShaderNodeTexImage'); e.image = fimg(emit); nt.links.new(e.outputs[0], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = strength
    m['crucible_floor'] = tex
    return m


def brick_mat(tag=None, tint=None):
    m = synty.material(os.path.join(synty.SYN, DF, 'Materials', r'Brick\Brick_Large_01.mat'), DF)['mat']
    return tint_mat(m, tag, tint=tint) if tag else m


def floor_surface(obj, top, side, frame=None, tile=16.0):
    """Walkable tops (faces looking up) get the floor material: a unique painting mapped once by `frame` (x0, y0, w, h in the
    mesh's own metres) or the paving tile; every other face keeps the wall brick, box-projected."""
    me = obj.data
    if me.users > 1: me = obj.data = me.copy()
    bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal; up = n.z > 0.8; f.material_index = 0 if up else 1
        a = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            if up and frame: l[uv].uv = ((c.x - frame[0]) / frame[2], (c.y - frame[1]) / frame[3])
            elif up: l[uv].uv = (c.x / tile, c.y / tile)
            else:
                u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y)); l[uv].uv = (u / 5.0, v / 5.0)
    bm.to_mesh(me); bm.free()
    me.materials.clear(); me.materials.append(top); me.materials.append(side)


PAVE = {'Hegemony': (0.80, 0.82, 0.93), 'Covenant': (0.74, 0.58, 0.46), 'Null': (0.44, 0.34, 0.39), 'Tier': (0.60, 0.52, 0.76), 'Stair': (0.82, 0.74, 0.98),
        'Sealed': (0.40, 0.35, 0.50), 'Tower': (0.5, 0.44, 0.62), 'Seat': (0.34, 0.29, 0.44), 'StairLight': (0.98, 0.94, 1.06)}
def pave_mat(tag): return floor_mat('M_Crucible_Paving_' + tag, 'T_Crucible_Paving.jpg', tint=PAVE[tag], rough=0.6)


def syn(me, F):
    """Custom centrepiece mesh: 'ashlar' faces -> the faction's Synty brick, 'trim' faces -> the faction's paving. Other kinds keep their own material."""
    for i, m in enumerate(me.materials):
        k = m.get('crucible_kind')
        if k == 'ashlar': me.materials[i] = brick_mat(F, STYLE[F]['brick']) if F in ('Hegemony', 'Covenant', 'Null') else brick_mat('Menhir', (0.66, 0.58, 0.84))
        elif k == 'trim': me.materials[i] = pave_mat(F if F in ('Hegemony', 'Covenant', 'Null') else 'Tier')
    return me


def put_custom(me, name, loc, rot, scale, c):
    o = D.objects.new(name, me); o.location = loc; o.rotation_euler = rot; o.scale = scale; c.objects.link(o); o['crucible_piece'] = me.get('crucible_piece', me.name)
    return o


def remove(pattern):
    pat = re.compile(pattern)
    for o in list(D.objects):
        if pat.match(o.name): D.objects.remove(o, do_unlink=True)


class Yard:
    """Yard-local metres: gate wall at x=118, interior x 118..160, y -40..40. rot 0 = the piece's +Y face looks at +y."""
    def __init__(self, faction, yaw=None, name=None):
        self.f = faction; self.name = name or faction
        self.yaw = math.radians(YAW[faction] if yaw is None else yaw); self.c = coll('Yard_' + self.name); self.dress = coll('Dressing'); self.n = {}

    def w(self, x, y, z=0.0):
        c, s = math.cos(self.yaw), math.sin(self.yaw)
        return Vector((x * c - y * s, x * s + y * c, z))

    def put(self, me, kind, x, y, z=0.0, rot=0.0, scale=1.0, dressing=False, rx=0.0, ry=0.0):
        self.n[kind] = self.n.get(kind, 0) + 1
        o = D.objects.new('Yard_%s_%s_%02d' % (self.name, kind, self.n[kind]), me)
        o.location = self.w(x, y, z); o.rotation_euler = (math.radians(rx), math.radians(ry), self.yaw + math.radians(rot))
        o.scale = scale if isinstance(scale, tuple) else (scale,) * 3
        (self.dress if dressing else self.c).objects.link(o); o['faction'] = self.f
        if 'synty_prefab' in me: o['synty_prefab'] = me['synty_prefab']; o['synty_pack'] = me['synty_pack']; o['pivot'] = me.get('pivot', [0, 0, 0])
        else: o['crucible_piece'] = me.get('crucible_piece', me.name)
        if 'material_variant' in me: o['material_variant'] = me['material_variant']
        return o

    def fx(self, typ, x, y, z, note, power=0.0, colour=None, lightcol=None):
        return fx(typ, self.name, self.w(x, y, z), colour or STYLE[self.f]['glowname'], note, power, lightcol or parts.PALETTE[self.f]['glow'])


CLOTH = {'Covenant': (0.80, 0.50, 0.09), 'Null': (0.42, 0.035, 0.03)}
def canopy(F, W=6.0, Dp=3.2, tag=''):
    """022: a market canopy against a wall (concepts 03 and 04): cloth sloping from 4.3 m at the wall to 3.1 m on two posts, a
    scalloped valance, stores underneath. Local frame: X along the wall, +Y into the yard, wall face at y = 0."""
    b = parts.MB(F)
    for x in (-W / 2 + 0.25, W / 2 - 0.25): b.beam((x, Dp, 0.0), (x, Dp, 3.15), 0.2, 0.2, 'wood')
    b.beam((-W / 2, 0.12, 4.25), (W / 2, 0.12, 4.25), 0.2, 0.22, 'wood'); b.beam((-W / 2, Dp, 3.05), (W / 2, Dp, 3.05), 0.16, 0.16, 'wood')
    for x in (-W / 2 + 0.25, W / 2 - 0.25): b.beam((x, 0.12, 4.25), (x, Dp, 3.05), 0.12, 0.12, 'wood')
    b.poly([(-W / 2 - 0.15, 0.05, 4.4), (W / 2 + 0.15, 0.05, 4.4), (W / 2 + 0.15, Dp + 0.12, 3.12), (-W / 2 - 0.15, Dp + 0.12, 3.12)], 'accent')
    n = 7
    for i in range(n):
        x0 = -W / 2 - 0.15 + (W + 0.3) * i / n; x1 = -W / 2 - 0.15 + (W + 0.3) * (i + 1) / n
        b.poly([(x0, Dp + 0.12, 3.12), (x1, Dp + 0.12, 3.12), ((x0 + x1) / 2, Dp + 0.12, 2.55)], 'accent')
    for sx in (-1, 1): b.poly([(sx * (W / 2 + 0.15), 0.05, 4.4), (sx * (W / 2 + 0.15), Dp + 0.12, 3.12), (sx * (W / 2 + 0.15), 0.05, 3.12)], 'accent')
    me = b.build('KN_Crucible_Canopy_%s%s' % (F, tag))
    cm = D.materials.get('M_Crucible_CanopyCloth_' + F)
    if not cm:
        cm = D.materials.new('M_Crucible_CanopyCloth_' + F); cm.use_nodes = True; bn = cm.node_tree.nodes['Principled BSDF']
        bn.inputs['Base Color'].default_value = (*CLOTH[F], 1); bn.inputs['Roughness'].default_value = 0.9; cm.use_backface_culling = False
    for i, m in enumerate(me.materials):
        if m.get('crucible_kind') == 'accent': me.materials[i] = cm
    return me


def build_yard(F):
    S = STYLE[F]; Y = Yard(F); P = parts.PALETTE[F]
    remove(r'^Yard_%s_(Banner|Crate|Floodlight|Gatehouse|Tower|Wall|Brazier|Cage|Skulls)(_|$)' % F)
    # ---- kept walkable layout, Synty stone on it
    fl = D.objects.get('Yard_%s_Floor' % F)
    if fl: floor_surface(fl, floor_mat('M_Crucible_YardFloor_' + F, 'T_Floor_Yard_%s.jpg' % F, None, 5.0, rough=0.42), brick_mat(F, S['brick']), frame=(118.0, -40.0, 42.0, 80.0))
    for n in ('Rampart', 'RampartStair_01', 'RampartStair_02', 'RespawnShrine'):
        o = D.objects.get('Yard_%s_%s' % (F, n))
        if o: floor_surface(o, pave_mat(F), brick_mat(F, S['brick']))
    pad = D.objects.get('Yard_%s_LandingPad' % F)
    if pad: synty_surface(pad, r'Brick\SmoothStone_01.mat', DF, 5.0, tag=F, tint=tuple(c * 0.9 for c in S['brick'])); pad.location.z = 0.02
    # ---- Synty kit in this faction's stone
    V = lambda me, crown=False: variant(me, F, tint=S['crown'] if crown else S['brick'])
    WALL = V(K(DF, 'SM_Bld_Wall_L_01')); CORNER = V(K(DF, 'SM_Bld_Wall_L_Corner_01'))
    CREN = V(K(KN, 'SM_Bld_Castle_Tower_Wall_Top_01_Lite'), True); CROWN = V(K(KN, 'SM_Bld_Castle_Tower_Top_01_Lite'), True)
    LPAN = variant(K(SS, 'SM_Prop_Light_Panel_01'), F + 'Glow', glow=P['glow'], glow_strength=7.0)
    RUB = [V(K(FK, 'SM_Bld_Castle_DestroyedWall_RubblePile_01')), V(K(FK, 'SM_Bld_Castle_DestroyedWall_RubblePile_01'))]
    BAN = parts.banner(F, W=2.6, Hc=6.0); BANM = parts.banner(F, W=2.4, Hc=4.5); BANM.name = 'KN_Crucible_BannerMid_' + F
    BANS = parts.banner(F, W=1.8, Hc=3.6); BANS.name = 'KN_Crucible_BannerSmall_' + F
    COPE = V(K(DF, 'SM_Bld_Wall_L_Trim_02')); CAPB = V(K(DR, 'SM_Bld_Ruin_Wall_Cap_01'))
    wd, cd, pd = dim(WALL), dim(CORNER), dim(LPAN); T = 2.5; PZ = 1.3 if F == 'Hegemony' else 1.0
    FIN = syn(parts.finial(F), F)
    crs = (5.0 / dim(CREN).x, T / dim(CREN).y)

    def wall_run(x0, y0, x1, y1, face_rot, H=10.0, kind='Wall', full=False):
        L = math.hypot(x1 - x0, y1 - y0); n = max(1, round(L / 5.0)); ux, uy = (x1 - x0) / L, (y1 - y0) / L; nx, ny = -uy, ux
        sx = (L / n) / wd.x
        for i in range(n):
            cx, cy = x0 + ux * (i + 0.5) * L / n, y0 + uy * (i + 0.5) * L / n
            for row in range(int(H // 5)):
                for s in (1, -1):
                    Y.put(WALL, kind, cx + nx * s * (T / 2 - wd.y / 2), cy + ny * s * (T / 2 - wd.y / 2), row * 5.0, rot=face_rot, scale=(sx, 1, 1))
            if F != 'Hegemony' and (full or F == 'Null' or i % 2 == 0): Y.put(CREN, kind + 'Top', cx, cy, H, rot=face_rot, scale=(crs[0] * sx * wd.x / 5.0, crs[1], 1.0))
            else: Y.put(COPE, kind + 'Coping', cx, cy, H, rot=face_rot, scale=(sx * wd.x / dim(COPE).x, (T + 0.3) / dim(COPE).y, 1.0))

    def strip(x, y, z0, z1, rot, width=0.55):
        Y.put(LPAN, 'LightStrip', x, y, (z0 + z1) / 2, rot=rot, rx=90, scale=(width / pd.x, (z1 - z0) / pd.y, 1.0))

    def top(x, y, H, w, kind):
        tower = kind != 'Pilaster'; hc = dim(COPE).z
        if F == 'Hegemony':                                   # flat square cap with a glowing light cap (top-down ref 02)
            Y.put(COPE, kind + 'Cap', x, y, H, scale=(w * 1.12 / dim(COPE).x, w * 1.12 / dim(COPE).y, 1.0))
            Y.put(LPAN, 'LightCap', x, y, H + hc + 0.02, scale=(w * 0.86 / pd.x, w * 0.86 / pd.y, 1.0))
            Y.fx('StripGlow', x, y, H + hc + 1.0, 'glowing light cap on top of every Hegemony pilaster and tower (top-down ref)', 260 if tower else 0)
        elif F == 'Covenant':                                 # battlement crown on towers, a gilded pointed finial on every tower and pilaster (ref 03)
            if tower: Y.put(CROWN, kind + 'Crown', x, y, H, scale=(w * 1.12 / dim(CROWN).x, w * 1.12 / dim(CROWN).y, 1.0))
            else: Y.put(COPE, kind + 'Cap', x, y, H, scale=(w * 1.12 / dim(COPE).x, w * 1.12 / dim(COPE).y, 1.0))
            Y.put(FIN, kind + 'Finial', x, y, H + (0.0 if tower else hc), scale=(w * 0.6, w * 0.6, w * 0.72))
        else:                                                 # Null: crenellated crowns on towers, plain caps on pilasters; fire baskets are added below
            if tower: Y.put(CROWN, kind + 'Crown', x, y, H, scale=(w * 1.12 / dim(CROWN).x, w * 1.12 / dim(CROWN).y, 1.0))
            else: Y.put(COPE, kind + 'Cap', x, y, H, scale=(w * 1.12 / dim(COPE).x, w * 1.12 / dim(COPE).y, 1.0))

    def pier(x, y, tiers, s=1.0, kind='Pier', strips=(0, 90, 180, 270), crown=True, power=700, zs=1.0):
        w = cd.x * s
        for t in range(tiers):
            Y.put(CORNER, kind, x, y, t * 5.0 * zs, rot=0, scale=(s, s, zs)); Y.put(CORNER, kind, x, y, t * 5.0 * zs, rot=180, scale=(s, s, zs))
        H = tiers * 5.0 * zs
        if crown: top(x, y, H, w, kind)

        for a in strips:
            r = math.radians(a); dx, dy = -math.sin(r), math.cos(r)
            strip(x + dx * (w / 2 + 0.04), y + dy * (w / 2 + 0.04), min(2.2, H * 0.3), H - min(1.6, H * 0.2), a)
            Y.fx('StripGlow', x + dx * (w / 2 + 1.6), y + dy * (w / 2 + 1.6), H / 2, 'bloom + point light for each inset light strip on a pier or tower', power)
        return H

    def banner(me, x, y, z, rot, note='wall banner'):
        Y.put(me, 'Banner', x, y, z, rot=rot)
        Y.fx('BannerSway', x, y, z - 3.0, note + ' (cloth sway; the mesh is the rest pose)', colour=S['cloth'])

    def flame(typ, x, y, z, note, power=900):
        col = {'Hegemony': (0.7, 0.93, 1.0), 'Covenant': (1.0, 0.55, 0.12), 'Null': (1.0, 0.16, 0.06)}[F]
        Y.fx(typ, x, y, z, note, power, colour={'Hegemony': 'cool blue-white', 'Covenant': 'orange-gold fire', 'Null': 'red fire'}[F], lightcol=col)

    tow_strips = {'Hegemony': (-90, 90), 'Covenant': (-90, 90), 'Null': (-90,)}[F]
    # ---- gate wall, gate towers, lintel
    for s in (1, -1):
        wall_run(116.25, s * 11.0, 116.25, s * 41.0, -90, full=True)
        pier(115.9, s * 8.5, 3, s=5.0 / cd.x, kind='GateTower', strips=tow_strips, power=900)
        banner(BANM, 122.5, s * 10.2, 7.9, -90, 'banner on the wall-walk edge beside the gate')
    for i in range(3):
        for s in (1, -1):
            Y.put(WALL, 'GateLintel', 116.25 + s * (T / 2 - wd.y / 2), -4 + 4 * i, 8.0, rot=-90, scale=(4.0 / wd.x, 1, 0.4))
        Y.put(CREN, 'GateLintelTop', 116.25, -4 + 4 * i, 10.0, rot=-90, scale=(crs[0] * 0.8, crs[1], 1.0))
    banner(BANS, 122.5, 0, 7.9, -90, 'small banner over the gate seam')
    # ---- 022: the arena face of the gate (owner images 1 and 2; "the outside of the gates of each rally gate is plain as can be")
    xo = 116.25 - T / 2 - 0.06                                # outer face of the gate wall
    banner(BANS, xo - 0.1, 0, 7.9, 90, 'small sigil banner over the gate seam, arena side')
    for s in (1, -1):
        for yb in (14.0, 20.5, 31.0): banner(BAN, xo, s * yb, 9.3, 90, 'banner on the arena face of the gate wall')
        Y.put(COPE, 'GateTowerBand', 115.9, s * 8.5, 9.0, scale=(5.6 / dim(COPE).x, 5.6 / dim(COPE).y, 0.8))   # string course round each gate tower
        if F != 'Null':
            for yl in (17.25, 25.5): strip(xo - 0.04, s * yl, 2.0, 8.8, 90, width=0.5); Y.fx('StripGlow', xo - 1.6, s * yl, 5.0, 'inset light strip on the arena face of the gate wall', 300)
        if F == 'Hegemony':                                   # glowing light columns flanking the gate outside as well as inside
            COLo = variant(K(SC, 'SM_Prop_Pillar_03'), 'Glow', selfglow=1.6)
            Y.put(COLo, 'LightColumn', 112.6, s * 6.9, 0, rot=90); Y.fx('StripGlow', 111.4, s * 6.9, 4.0, 'light columns flanking the gate, arena side', 450)
        elif F == 'Covenant':                                 # gilded fire bowls on short pedestals flanking the gate mouth
            BRZo = K(DF, 'SM_Prop_Brazier_03')
            H = pier(112.4, s * 7.6, 1, s=0.8, kind='GateBrazierPedestal', strips=(90,), crown=False, power=400, zs=0.6) + cd.z * 0.6 - 3.0
            Y.put(BRZo, 'Brazier', 112.4, s * 7.6, H, scale=1.2); flame('BrazierFlame', 112.4, s * 7.6, H + 2.5, 'fire bowl flanking the gate mouth, arena side')
        else:                                                 # Null: chains hung on the wall and fire baskets on wall brackets
            CHo = K(DF, 'SM_Prop_Chain_01'); BRo = K(DF, 'SM_Prop_Brazier_05')
            for yc in (17.25, 25.5):
                for k in range(3): Y.put(CHo, 'Chain', xo - 0.15, s * yc, 9.0 - k * dim(CHo).z, rot=90, scale=1.0)
            Y.put(BRo, 'Brazier', 112.6, s * 7.4, 0.0, scale=1.4); flame('BrazierFlame', 112.6, s * 7.4, 3.0, 'fire basket flanking the gate mouth, arena side', 900)
    # ---- side walls, rear wall, corner towers, wall pilasters and banners
    for s in (1, -1):
        wall_run(119.0, s * 41.25, 159.0, s * 41.25, 180 if s > 0 else 0)
        for x in (128.0, 139.0, 150.0):
            pier(x, s * 38.6, 2, zs=PZ, kind='Pilaster', strips=((180,) if s > 0 else (0,)) if F != 'Null' else ())
        for x in (133.5, 144.5, 155.0):
            banner(BAN, x, s * 39.95, 9.3, 180 if s > 0 else 0)
        pier(161.0, s * 41.0, 3, s=6.0 / cd.x, kind='CornerTower', strips=(90, 180 if s > 0 else 0) if F != 'Null' else (), power=600)
        pier(116.5, s * 41.0, 3, s=6.0 / cd.x, kind='CornerTower', strips=(-90, 180 if s > 0 else 0) if F != 'Null' else (), power=600)
    wall_run(161.25, -38.0, 161.25, 38.0, 90)
    for y in (-24.0, -8.0, 8.0, 24.0):
        pier(158.6, y, 2, zs=PZ, kind='Pilaster', strips=(90,) if F != 'Null' else ())
    for y in (-32.0, -16.0, 16.0, 32.0) + ((0.0,) if F == 'Hegemony' else ()):
        banner(BAN, 159.95 if F != 'Covenant' else 159.45 - dim(K(DF, 'SM_Bld_Wall_L_Arch_01')).y / 2 - 0.08, y, 9.3, 90)   # 022 owner: Covenant banners hung behind the arch pieces; now in front of them
    gs = parts.MB(F)                                          # stepped gate threshold: four 0.3 m steps up from the yard, 1.2 m platform through the gate, four down to the rim
    for k_ in range(4):
        x0, x1, z1 = 112.0 + 0.65 * k_, 122.8 - 0.45 * k_, 0.3 * (k_ + 1)
        gs.box((x0, -6.0, 0.3 * k_ - (0.3 if k_ == 0 else 0.0)), (x1, 6.0, z1), 'ashlar', skip='-z +z')
        gs.poly([(x0, -6.0, z1), (x1, -6.0, z1), (x1, 6.0, z1), (x0, 6.0, z1)], 'trim')
    GS = syn(gs.build('KN_Crucible_GateStairs_' + F), F); GS['walkable'] = True
    Y.put(GS, 'GateStairs', 0.0, 0.0, 0.0)
    crates = []; lane = []
    # ---- faction dressing
    if F == 'Hegemony':
        COL = variant(K(SC, 'SM_Prop_Pillar_02'), 'Glow', selfglow=1.6); COL2 = variant(K(SC, 'SM_Prop_Pillar_03'), 'Glow', selfglow=1.6)
        for s in (1, -1):
            Y.put(COL, 'LightColumn', 119.0, s * 6.6, 0, rot=-90); Y.put(COL2, 'LightColumn', 123.2, s * 6.6, 0, rot=-90)
            Y.fx('StripGlow', 124.4, s * 6.6, 4.0, 'light columns flanking the gate', 500)
            # 022: the free-standing lane pylons are gone (owner: "random pillars in 2 out of the 3 rally zones that look odd");
            # ref 02 keeps the yard floor open: the light lives on the wall pilasters, stores stand along the walls
        crates = [K(SW, 'SM_Prop_Crate_04'), K(SW, 'SM_Prop_Crate_04'), K(SW, 'SM_Prop_Crate_03')]; csc = 0.83
    elif F == 'Covenant':
        BRZ = K(DF, 'SM_Prop_Brazier_03'); ARCH = V(K(DF, 'SM_Bld_Wall_L_Arch_01')); AWN = K(FK, 'SM_Prop_Awning_04')
        for s in (1, -1):
            for x in (150.0,):                                             # 022: fire braziers on stout pedestals set against the wall pilasters
                H = pier(x, s * 36.4, 1, s=0.9, kind='BrazierPedestal', strips=((0,) if s < 0 else (180,)), crown=False, power=500, zs=0.8) + cd.z * 0.8 - 4.0   # bowl on the real top   # (concept 03), not out in the yard
                Y.put(BRZ, 'Brazier', x, s * 36.4, H, scale=1.3); flame('BrazierFlame', x, s * 36.4, H + 2.6, 'big open fire in each bowl brazier against the side walls')
        for y in (-32.0, -16.0, 16.0, 32.0):                              # gothic arcade on the rear wall
            Y.put(ARCH, 'Arch', 159.45, y, 0, rot=90, scale=(1.5, 1, 1.0))
        H = pier(158.9, 0.0, 3, s=1.25, kind='ShrineSpire', strips=(90,), power=900)
        flame('BrazierFlame', 158.9, 0.0, H + 2.4, 'beacon fire on the spire behind the shrine')
        for y in (-8.0, 8.0):                                              # 022: the two shrine fires stand against the rear pilasters (concept 03)
            H = pier(156.3, y, 1, s=0.9, kind='BrazierPedestal', strips=(-90,), crown=False, power=400, zs=0.8) + cd.z * 0.8 - 4.0
            Y.put(BRZ, 'Brazier', 156.3, y, H, scale=1.3); flame('BrazierFlame', 156.3, y, H + 2.6, 'big open fire flanking the shrine spire')
        crates = [K(DFa, 'SM_Prop_Crate_02'), K(DFa, 'SM_Prop_Crate_02'), K(DFa, 'SM_Prop_Barrel_01')]; csc = 1.0
    else:  # Null
        BRZ5 = K(DF, 'SM_Prop_Brazier_05'); BRZ1 = K(DF, 'SM_Prop_Brazier_03'); SPK = K(DF, 'SM_Bld_Wall_Detail_08')
        AWR = variant(K(FK, 'SM_Prop_Awning_04'), 'NullRed', tint=(1.0, 0.13, 0.10)); CAGE = K(DF, 'SM_Prop_Cage_01'); SKULL = K(DF, 'SM_Prop_Skull_Pile_01'); CHAIN = K(DF, 'SM_Prop_Chain_01'); DBAN = K(DF, 'SM_Prop_Banner_03')
        for s in (1, -1):
            for (x, y, w) in ((115.9, s * 8.5, 5.0), (161.0, s * 41.0, 6.0), (116.5, s * 41.0, 6.0)):    # fire and spikes on every tower
                Y.put(BRZ5, 'Brazier', x, y, 15.0 + 1.6, scale=1.4); flame('BrazierFlame', x, y, 19.0, 'fire basket on top of each spiked tower', 700)
                for a in (0, 90, 180, 270):
                    r = math.radians(a); Y.put(SPK, 'Spikes', x - math.sin(r) * w * 0.52, y + math.cos(r) * w * 0.52, 12.5, rot=a, scale=(w / dim(SPK).x * 0.9, 1.4, 1.2))
            for x in (128.0, 150.0):
                Y.put(BRZ1, 'FirePit', x, s * 19.5, 0, scale=1.3); flame('BrazierFlame', x, s * 19.5, 3.4, 'large fire pit on the yard floor', 1100)
            for x in (133.5, 144.5):
                for k in range(3): Y.put(CHAIN, 'Chain', x + 4.0, s * 39.9, 9.0 - k * dim(CHAIN).z, rot=0, scale=1.0)
            Y.put(CAGE, 'Cage', 156.5, s * 22.0, 0, rot=20 * s, scale=1.0, dressing=True); Y.put(CAGE, 'Cage', 124.0, s * 30.0, 0, rot=-15 * s, scale=1.0, dressing=True)
            Y.put(SKULL, 'Skulls', 155.5, s * 26.0, 0, rot=40, scale=1.0, dressing=True); Y.put(SKULL, 'Skulls', 126.0, s * 16.5, 0, rot=10, scale=1.0, dressing=True)
            Y.put(DBAN, 'WarBanner', 149.0, s * 17.6, 0, rot=90, scale=1.0, dressing=True)
            for x in (128.0, 139.0, 150.0):                                  # fire baskets on the wall pilasters (concept 04)
                Y.put(BRZ5, 'Brazier', x, s * 38.6, 10.0 + 0.75, scale=1.4); flame('BrazierFlame', x, s * 38.6, 13.6, 'fire basket on a wall pilaster', 500)
        for y in (-24.0, -8.0, 8.0, 24.0):
            Y.put(BRZ5, 'Brazier', 158.6, y, 10.0 + 0.75, scale=1.4); flame('BrazierFlame', 158.6, y, 13.6, 'fire basket on a wall pilaster', 500)
        for y in (-9.6, 9.6):                                                # fire bowls at the gate foot
            Y.put(K(DF, 'SM_Prop_Brazier_03'), 'Brazier', 125.2, y, 0, scale=1.3); flame('BrazierFlame', 125.2, y, 3.0, 'fire bowl at the foot of the Null gate', 1400)
        for y in (-12.5, 12.5):                                              # pyre bowls flanking the red shrine crystal
            Y.put(K(DF, 'SM_Prop_Brazier_03'), 'Brazier', 150.5, y, 0, scale=1.3); flame('BrazierFlame', 150.5, y, 2.8, 'pyre bowl beside the Null shrine', 800)
        crates = [K(DFa, 'SM_Prop_Crate_01'), K(DFa, 'SM_Prop_Crate_02'), K(DFa, 'SM_Prop_Barrel_01')]; csc = 1.0
    if F in ('Covenant', 'Null'):                                            # 022: canopies with stores under them (concepts 03, 04)
        CAN = canopy(F); CANS = canopy(F, W=4.6, tag='S'); CANT = canopy(F, W=3.4, tag='T')
        spots_c = ([(159.45 - dim(K(DF, 'SM_Bld_Wall_L_Arch_01')).y / 2, y, 90, CAN) for y in (-32.0, -16.0, 16.0, 32.0)] + [(155.0, s * 40.0, 180 if s > 0 else 0, CANS) for s in (1, -1)]) if F == 'Covenant' else                   [(x, s * 40.0, 180 if s > 0 else 0, me_) for s in (1, -1) for (x, me_) in ((155.0, CANS),)]   # (x 143-147 is the walk to the rampart stair foot: kept open)
        crt = [K(DFa, 'SM_Prop_Crate_02'), K(DFa, 'SM_Prop_Crate_01'), K(DFa, 'SM_Prop_Barrel_01')]
        for (x, y, r_, me_) in spots_c:
            Y.put(me_, 'Canopy', x, y, 0.0, rot=r_, dressing=True)
            a_ = math.radians(r_); fx_, fy_ = -math.sin(a_), math.cos(a_); ax_, ay_ = math.cos(a_), math.sin(a_)
            for k_, (u_, v_, ci) in enumerate(((-1.3, 1.0, 0), (0.2, 0.9, 1), (1.5, 1.2, 2), (-1.2, 1.0, 1))):
                if k_ == 3: Y.put(crt[ci], 'Crate', x + ax_ * u_ + fx_ * v_, y + ay_ * u_ + fy_ * v_, dim(crt[0]).z, rot=r_ + 8, scale=0.95, dressing=True)
                else: Y.put(crt[ci], 'Crate', x + ax_ * u_ + fx_ * v_, y + ay_ * u_ + fy_ * v_, 0.0, rot=r_ + (k_ - 1) * 9, scale=1.0, dressing=True)
    # ---- stores: stacks clear of the 33 spawn points, the landing pad and the gate lane
    spawns = [o for o in D.objects if o.name.startswith('Spawn_' + F)]
    def clear(x, y, r=2.6):
        p = Y.w(x, y)
        return all((Vector((o.location.x, o.location.y, 0)) - p).length > r for o in spawns) and not (abs(y) < 8.5 and x < 147)
    rnd = random.Random(9 + len(F)); spots = []
    for s in (1, -1):
        spots += [(131.0, s * 17.2), (135.6, s * 17.6), (142.6, s * 18.0), (146.6, s * 18.2), (153.2, s * 18.4), (124.0, s * 17.0), (123.4, s * 12.6),
                  (125.5, s * 31.0), (151.0, s * 35.6), (156.0, s * 12.0), (128.6, s * 15.2)]
    for (x, y) in spots:
        if not clear(x, y): continue
        r0 = rnd.choice((0, 90)) + rnd.uniform(-8, 8); a = crates[0]; sc = csc * rnd.uniform(0.92, 1.08)
        Y.put(a, 'Crate', x, y, 0, rot=r0, scale=sc, dressing=True)
        if rnd.random() < 0.35: Y.put(crates[rnd.randint(0, 1)], 'Crate', x, y, dim(a).z * sc, rot=r0 + rnd.uniform(-12, 12), scale=sc * 0.95, dressing=True)
        ang = math.radians(r0 + 90); x2, y2 = x + math.cos(ang) * 2.3, y + math.sin(ang) * 2.3
        if rnd.random() < 0.6 and clear(x2, y2): Y.put(rnd.choice(crates), 'Crate', x2, y2, 0, rot=r0 + rnd.uniform(-15, 15), scale=csc * rnd.uniform(0.9, 1.05), dressing=True)
    for i in range(26):
        x, y = rnd.uniform(121, 157), rnd.uniform(-35, 35)
        if clear(x, y, 3.0): Y.put(rnd.choice(RUB), 'Rubble', x, y, 0, rot=rnd.uniform(0, 360), scale=rnd.uniform(0.1, 0.22), dressing=True)
    # ---- gate leaves: the 007 objects (names, pivots, slide) stay; braced plank leaves (no pack has a 12 m double gate)
    L = D.objects.get('GateLeaf_%s_L' % F); R = D.objects.get('GateLeaf_%s_R' % F); PC = D.objects.get('GateLeaf_%s_Portcullis' % F)
    if L: L.data = parts.gate_leaf(F, mirror=True)
    if R: R.data = parts.gate_leaf(F, mirror=False)
    if PC:                                                      # one 12 m lifting gate: both leaves in one mesh
        old = [Vector(c) for c in PC.bound_box]; wide_x = (max(c.x for c in old) - min(c.x for c in old)) > 6
        a = parts.gate_leaf(F, mirror=True); b = parts.gate_leaf(F, mirror=False); bm = bmesh.new()
        for me_, off in ((a, 3.0), (b, -3.0)):
            t = me_.copy(); t.transform(Matrix.Translation((0, off, 0))); bm.from_mesh(t); D.meshes.remove(t)
        if wide_x: bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'Z'))
        m = D.meshes.new('KN_Crucible_GatePortcullis_' + F); bm.to_mesh(m); bm.free()
        for mt in a.materials: m.materials.append(mt)
        m['crucible_piece'] = m.name; PC.data = m
    old = D.objects.get('Yard_%s_ShrineCrystal' % F)
    if old: D.objects.remove(old, do_unlink=True)
    pl = D.objects.get('Yard_%s_RespawnShrine' % F)
    if F == 'Hegemony': dm = parts.tech_plinth(F); dm.transform(Matrix.Diagonal((1.65, 1.65, 1.0, 1.0))); zc = 0.25                  # hex tech plinth, crystal floats in the beam
    else:                                                                    # two-tier round dais (three low steps, then a drum) with a lit / gilded band
        dm = parts.stepped_dais(F, [(8.0, 0.3), (7.3, 0.6), (6.6, 0.9), (4.4, 1.6), (3.4, 2.3)], n=28, band=(4.46, 1.05, 1.42, 'accent' if F == 'Covenant' else 'glow')); zc = 2.3
    syn(dm, F)
    if pl: pl.data = dm                                                      # same object, name and marker as the 007 plinth
    CRY = parts.shrine_crystal(F)
    gm = parts.mat('glow', F).copy(); gm.name = 'M_Crucible_ShrineCrystal_' + F
    gb = next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); gb.inputs['Emission Strength'].default_value = {'Hegemony': 1.25, 'Covenant': 1.7, 'Null': 1.5}[F]; gb.inputs['Roughness'].default_value = 0.25
    for i, m in enumerate(CRY.materials):
        if m.get('crucible_kind') == 'glow': CRY.materials[i] = gm
    Y.put(CRY, 'ShrineCrystal', 152.0, 0.0, zc, rot=0, scale=1.0)
    if F != 'Hegemony':                                                      # angled blocks: eight small on the dais, a ring of big ones on the floor
        BLK = syn(parts.shrine_block(F, 'accent' if F == 'Covenant' else 'glow'), F)
        for k in range(8):
            r_ = math.radians(22.5 + 45 * k); Y.put(BLK, 'ShrineBlock', 152 + 4.5 * math.cos(r_), 4.5 * math.sin(r_), 0.9, rot=22.5 + 45 * k, scale=0.85)
        for adeg in (-120, -92, -65, 65, 92, 120):               # open toward the lane and the spawn rows
            if F == 'Covenant' and adeg == 0: continue                       # the shrine spire stands there
            r_ = math.radians(adeg); Y.put(BLK, 'ShrineBlock', 152 + 8.05 * math.cos(r_), 8.05 * math.sin(r_), 0.0, rot=adeg, scale=1.15)
    flame('ShrineBeam', 152, 0, 5.0, 'respawn shrine: glowing crystal / obelisk with a light pillar, the brightest thing in the yard', 1500)
    return Y


# ======================================================================================= whole map
# 1) every Synty piece the 007 build placed gets its real prefab material (same pivot, so nothing moves)
fixed = 0
for me in list(D.meshes):
    if me.name.startswith('KIT_') and 'synty_prefab' in me and me.users and 'Crystal' not in me.name:
        try: raw = synty.kit_mesh(me['synty_pack'], me['synty_prefab'])
        except Exception as e: print('KITMISS', me.name, e); continue
        for o in [o for o in D.objects if o.data == me]: o.data = raw; fixed += 1
print('REBOUND', fixed)
# neutral (arena, perimeter, sealed north) stone is the dark violet-grey of concepts 01/05
for m in D.materials:
    if m.name.startswith(('DarkFortress/Brick_', 'DarkFortress/SmoothStone')) and '__' not in m.name:
        for n in m.node_tree.nodes:
            if n.type == 'MIX': n.inputs[7].default_value = (*STYLE['Neutral']['brick'], 1)

remove(r'^Bowl_Bones_')
# perimeter battlement pieces from the 007 build (565 tris x 104): swapped in place for the plain Dark Fortress coping (budget)
bpy.context.view_layer.update(); cope0 = synty.kit_mesh(DF, 'SM_Bld_Wall_L_Trim_02'); nsw = 0
def bb(me):
    xs = [v.co for v in me.vertices]
    return Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs))), Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
nlo, nhi = bb(cope0)
for o in [o for o in D.objects if o.type == 'MESH' and o.data.get('synty_prefab') == 'SM_Bld_Wall_L_Trim_04' and not o.name.startswith('Yard_')]:
    olo, ohi = bb(o.data); r_ = Vector(((ohi.x - olo.x) / max(1e-3, nhi.x - nlo.x), (ohi.y - olo.y) / max(1e-3, nhi.y - nlo.y), 1.0))
    delta = Vector((olo.x - nlo.x * r_.x, olo.y - nlo.y * r_.y, olo.z - nlo.z))
    o.matrix_world = o.matrix_world @ Matrix.Translation(delta) @ Matrix.Diagonal((r_.x, r_.y, r_.z, 1.0)); o.data = cope0; o['synty_prefab'] = 'SM_Bld_Wall_L_Trim_02'; nsw += 1
print('COPING swapped', nsw)
dmg0 = synty.kit_mesh(DF, 'SM_Bld_Wall_L_Damaged_03'); dlo, dhi = bb(dmg0); nsw = 0          # sealed yard: 2.2k-tri damaged walls -> 1.1k-tri ones
for o in [o for o in D.objects if o.type == 'MESH' and o.data.get('synty_prefab') == 'SM_Bld_Wall_L_Damaged_01']:
    olo, ohi = bb(o.data); r_ = Vector(((ohi.x - olo.x) / max(1e-3, dhi.x - dlo.x), 1.0, (ohi.z - olo.z) / max(1e-3, dhi.z - dlo.z)))
    delta = Vector((olo.x - dlo.x * r_.x, (olo.y + ohi.y) / 2 - (dlo.y + dhi.y) / 2, olo.z - dlo.z * r_.z))
    o.matrix_world = o.matrix_world @ Matrix.Translation(delta) @ Matrix.Diagonal((r_.x, r_.y, r_.z, 1.0)); o.data = dmg0; o['synty_prefab'] = 'SM_Bld_Wall_L_Damaged_03'; nsw += 1
print('DAMAGED swapped', nsw)
LAY = json.load(open(os.path.join(os.path.dirname(OUT), 'LAYOUT_007.json')))
for F in ('Hegemony', 'Covenant', 'Null'):
    yw = math.radians(YAW[F]); c_, s_ = math.cos(yw), math.sin(yw); pts = []
    for o in sorted([o for o in D.objects if o.name.startswith('Spawn_' + F)], key=lambda o: o.name):
        lx, ly = o.location.x * c_ + o.location.y * s_, -o.location.x * s_ + o.location.y * c_
        if abs(lx - 146.0) < 0.2: lx = 137.0
        o.location = (lx * c_ - ly * s_, lx * s_ + ly * c_, o.location.z); pts.append([round(o.location.x, 3), round(o.location.y, 3), round(o.location.z, 3)])
    LAY['yards'][F]['spawn_points_33'] = pts
LAY['yards']['Null']['gate_threshold'] = dict(height_m=1.2, steps_each_side=4, step_m=0.3, tread_m=0.65, note='painted stairs up to the Null gate; Covenant and Hegemony gates stay level')
LAY['arena']['tiers'] = dict(rows=30, riser_height_m=0.5, r_inner_m=72.3, r_outer_m=103.7, note='amphitheatre seat rows replace the four 3 m terraces; the eight minor stairs are removed; grand and diagonal stairs unchanged')
LAY['changes_since_007'] = ['spawn row at yard-local x=146 moved to x=137 (rows 137/140/143) so the shrine dais can be 8 m', 'Null gate threshold raised 1.2 m with stairs each side',
                            'tier terraces -> 30 seat rows of 0.5 m', '8 minor stairs removed', 'floor-crack decal removed (lava is in the floor material)']
LAND = 130.0
for F in ('Hegemony', 'Covenant', 'Null'):
    yw = math.radians(YAW[F]); c_, s_ = math.cos(yw), math.sin(yw); W_ = lambda x, y: (x * c_ - y * s_, x * s_ + y * c_)
    for nm in ('MARK_Landing_' + F, 'Yard_%s_LandingPad' % F):                 # arrivals land 7 m in front of the front spawn row, not among it
        o = D.objects.get(nm)
        if o: o.location.x, o.location.y = W_(LAND, 0.0)
    LAY['yards'][F]['landing_point'] = [round(W_(LAND, 0.0)[0], 3), round(W_(LAND, 0.0)[1], 3), 0.0]
    stand = {}
    for tag, (dx, dy) in (('Front', (-5.5, 0.0)), ('Left', (0.0, 5.5)), ('Right', (0.0, -5.5)), ('Rear', (5.5, 0.0))):   # free 0.9 m platform ring of the shrine dais
        x, y = W_(152.0 + dx, dy); e = D.objects.new('MARK_RespawnStand_%s_%s' % (F, tag), None); e.location = (x, y, 0.9); e.empty_display_size = 0.5
        coll('Markers_For_Unity').objects.link(e); e['faction'] = F; e['respawn_stand'] = tag; stand['RespawnStand_%s_%s' % (F, tag)] = [round(x, 3), round(y, 3), 0.9]
    LAY['yards'][F]['respawn_stand_points'] = stand
    LAY['yards'][F]['respawn_shrine_note'] = 'respawn_shrine is the crystal at the dais centre; players stand on respawn_stand_points (platform at 0.9 m)'
    LAY['yards'][F]['gate_threshold'] = dict(height_m=1.2, steps_each_side=4, step_m=0.3, yard_side_tread_m=0.45, arena_side_tread_m=0.65, platform_x_local_m=[114.6, 121.0], note='the same stepped threshold on all three gates')
RAISE = 7.5
LAY['arena']['floor_z'] = -7.5; LAY['arena']['heart_dais']['center'][2] = LAY['arena']['heart_dais']['center'][2] + RAISE
LAY['arena']['floor_radius_m'] = 82.3
LAY['arena']['tiers'] = dict(block_rings=7, block_height_m=round(7.5 / 7, 3), ring_depth_m=round((103.7 - 82.3) / 7, 3), r_inner_m=82.3, r_outer_m=103.7,
                             note='seven concentric rings of big stone blocks (not seating, not walk-up: 1.07 m each); the four grand stairs (N, S, E, W) are the routes')
LAY['arena']['grand_stairs'] = dict(count=4, width_m=14.0, steps=25, riser_m=0.3, tread_m=1.0, head_r_m=103.7, foot_r_m=79.7, cheek_walls_m=1.7)
LAY['arena'].pop('risers_r_m', None); LAY['arena'].pop('riser_height_m', None); LAY['arena'].pop('terrace_depth_m', None)
LAY['changes_since_007'] += ['bowl depth halved: arena floor at z = -7.5 (was -15)', 'arena floor radius 82.3 m (was 72.3); tiers = seven rings of big blocks; new grand stairs with cheek walls', 'the four diagonal stairs removed (seats); the grand stairs are the only routes',
                             'landing points moved from yard-local x=134 to x=130', 'respawn stand points added on each shrine platform', 'the 1.2 m gate threshold is on all three gates']
json.dump(LAY, open(os.path.join(os.path.dirname(OUT), 'LAYOUT_022.json'), 'w'), indent=1)
for F in ('Hegemony', 'Covenant', 'Null'): build_yard(F)

# 2) the arena bowl: kept shapes, Synty stone; braziers, spires and cover swapped to pack pieces
N = STYLE['Neutral']
for o in list(D.objects):
    if o.type != 'MESH': continue
    n = o.name
    if n.startswith('Bowl_Floor_'): D.objects.remove(o, do_unlink=True)
    elif n == 'Bowl_FloorCracks_Decal': D.objects.remove(o, do_unlink=True)
    elif n.startswith('Rim_Plateau'): floor_surface(o, floor_mat('M_Crucible_RimFloor', 'T_Floor_Rim_Quarter.jpg', rough=0.65), brick_mat('Rim', (0.6, 0.52, 0.74)), frame=(0.0, 0.0, 118.0, 118.0))
    elif n.startswith('Bowl_TierWedge'): floor_surface(o, pave_mat('Tier'), brick_mat('Tier', (1.0, 0.88, 1.25)))
    elif n.startswith('Bowl_Stair'): D.objects.remove(o, do_unlink=True)
    elif n.startswith('Yard_Sealed_Floor'): floor_surface(o, pave_mat('Sealed'), brick_mat())
    elif n.startswith(('Perimeter_CornerTower', 'Yard_Sealed_Gatehouse')): floor_surface(o, pave_mat('Tower'), brick_mat())
    elif n == 'Bowl_CrucibleFloor_Dais':
        floor_surface(o, floor_mat('M_Crucible_Dais', 'T_Floor_Dais.jpg', 'T_Floor_Dais_Emit.jpg', 7.0, rough=0.5), brick_mat('Stair', (1.25, 1.12, 1.5)), frame=(-12.5, -12.5, 25.0, 25.0))
remove(r'^Bowl_TierWedge_'); remove(r'^Bowl_Stair_Minor_')
def seat_sector(name, lo, hi, rows=30, seg=10, r0=72.3, r1=103.7):
    """Amphitheatre seating between two stairs: `rows` steps of 0.5 m from the arena floor (-15) to the rim (0), same slope as the stairs beside it."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); dz = 7.5 / rows; dr = (r1 - r0) / rows
    def quad(pts, up):
        fc = bm.faces.new([bm.verts.new(p) for p in pts]); fc.material_index = 0 if up else 1; fc.normal_update(); n = fc.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        for l in fc.loops:
            c = l.vert.co; u, v = ((c.y, c.z) if ax == 0 else (c.x, c.z) if ax == 1 else (c.x, c.y)); l[uv].uv = (u / (16.0 if up else 5.0), v / (16.0 if up else 5.0))
    P3 = lambda r, a, z: (r * math.cos(a), r * math.sin(a), z)
    for i in range(rows):
        ra, rb = r0 + dr * i, r0 + dr * (i + 1); z = -7.5 + dz * (i + 1)
        for j in range(seg):
            a0, a1 = lo(ra) + (hi(ra) - lo(ra)) * j / seg, lo(ra) + (hi(ra) - lo(ra)) * (j + 1) / seg
            b0, b1 = lo(rb) + (hi(rb) - lo(rb)) * j / seg, lo(rb) + (hi(rb) - lo(rb)) * (j + 1) / seg
            quad([P3(ra, a0, z), P3(ra, a1, z), P3(rb, b1, z), P3(rb, b0, z)], True)                    # tread
            quad([P3(ra, a0, z - dz), P3(ra, a1, z - dz), P3(ra, a1, z), P3(ra, a0, z)], False)        # riser
        for (fa, fb) in ((lo(ra), lo(rb)), (hi(ra), hi(rb))):                                         # end walls beside the stairs
            quad([P3(ra, fa, -8.1), P3(rb, fb, -8.1), P3(rb, fb, z), P3(ra, fa, z)], False)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for fc in bm.faces:
        if fc.material_index == 0 and fc.normal.z < 0: fc.normal_flip()
    me = D.meshes.new('KN_Crucible_' + name); bm.to_mesh(me); bm.free()
    me.materials.append(pave_mat('Seat')); me.materials.append(brick_mat('Seat', (0.5, 0.44, 0.64))); me['crucible_piece'] = me.name; me['walkable'] = True
    return me
def flat(name, col, rough=0.85):
    m = D.materials.get(name) or D.materials.new(name); m.use_nodes = True; b_ = m.node_tree.nodes['Principled BSDF']
    b_.inputs['Base Color'].default_value = (*col, 1); b_.inputs['Roughness'].default_value = rough; b_.inputs['Specular IOR Level'].default_value = 0.2
    return m
# the tier blocks are one dark stone colour (owner: "big rows of block that are a specific colour"), a shade lighter on top and on the chamfer
TB = [flat('M_Crucible_TierBlock_Top', (0.085, 0.072, 0.135)), flat('M_Crucible_TierBlock_Edge', (0.16, 0.135, 0.25)), flat('M_Crucible_TierBlock_Face', (0.042, 0.036, 0.07)),
      flat('M_Crucible_TierBlock_Joint', (0.008, 0.007, 0.014))]
STM = [pave_mat('StairLight'), brick_mat('StairLight', (1.7, 1.6, 1.95)), flat('M_Crucible_StairCheek_Top', (0.36, 0.33, 0.46)), flat('M_Crucible_StairCheek_Side', (0.20, 0.18, 0.27))]
GST = AB.grand_stair('GrandStair', STM); GST['walkable'] = True
FLQ = AB.floor_quarter('ArenaFloorQuarter', floor_mat('M_Crucible_ArenaFloor', 'T_Floor_Arena_Quarter.jpg', 'T_Floor_Arena_Quarter_Emit.jpg', 5.0, rough=0.5)); FLQ['walkable'] = True
for q in range(4):
    rz = (0, 0, q * math.pi / 2)
    put_custom(AB.tier_blocks('TierBlocks_Q%d' % (q + 1), TB, seed=40 + q), 'BowlNew_Tiers_%d' % (q + 1), (0, 0, 0), rz, (1, 1, 1), coll('Bowl'))
    put_custom(GST, 'BowlNew_GrandStair_%d' % (q + 1), (0, 0, 0), rz, (1, 1, 1), coll('Bowl'))
    put_custom(FLQ, 'BowlNew_Floor_%d' % (q + 1), (0, 0, 0), rz, (1, 1, 1), coll('Bowl'))
fx('LavaCracks', 'Heart', (0, 0, -13.6), 'red-orange', 'glowing lava cracks across the central dais and radiating over the arena floor (concept 01)', 800, (1.0, 0.2, 0.05))
for k in range(8):
    a = math.radians(45 * k + 22.5)
    fx('LavaCracks', 'ArenaFloor', (40 * math.cos(a), 40 * math.sin(a), -14.9), 'red-orange', 'floor crack glow, pulsing; the arena floor material carries the crack emission map', 500, (1.0, 0.2, 0.05))

PED = variant(K(DR, 'SM_Bld_Ruin_Pillar_01'), 'Arena', tint=(0.5, 0.45, 0.62)); BRZ = K(DF, 'SM_Prop_Brazier_03'); BRZI = K(DF, 'SM_Prop_Brazier_02_Insert_01')
SPIRES = [K(DFa, 'SM_Env_Cliff_Basalt_01'), K(DFa, 'SM_Env_Cliff_Basalt_03')]; SLAB = variant(K(DFa, 'SM_Env_Rock_01'), 'Arena', tint=(0.5, 0.45, 0.66))
# (021's COVER list of castle-wall ruins is gone: see cover_stone below)
# 022 (owner: "the random brick walls look out of place", image 3): no castle-wall or brick ruins in the arena any more; every
# piece of cover is dark cut stone (ref 01: tall leaning slabs + stacked rough blocks), in the tier blocks' own dark stone
CSF = flat('M_Crucible_CoverStone_Face', (0.05, 0.043, 0.08)); CST = flat('M_Crucible_CoverStone_Top', (0.12, 0.10, 0.18))
def cover_stone(seed, kind='stack'):
    r = random.Random(seed); b = parts.MB('Neutral')
    if kind == 'stack':      # one big cut block with a smaller one on it and sometimes one leaning beside it
        L, W, H = r.uniform(1.6, 2.6), r.uniform(1.1, 1.6), r.uniform(1.0, 1.5)
        b.cham((-L / 2, -W / 2, -0.2), (L / 2, W / 2, H), 0.12, 'ashlar')
        l, w, h = r.uniform(0.7, 1.2), r.uniform(0.6, 1.0), r.uniform(0.6, 1.0); ox, oy = r.uniform(-L / 2 + l / 2, L / 2 - l / 2), r.uniform(-W / 2 + w / 2, W / 2 - w / 2)
        b.cham((ox - l / 2, oy - w / 2, H), (ox + l / 2, oy + w / 2, H + h), 0.08, 'ashlar')
        if r.random() < 0.6: b.cham((L / 2 + 0.06, -w / 2, -0.2), (L / 2 + 0.06 + l * 0.8, w / 2, h * 0.9), 0.08, 'ashlar')
    elif kind == 'low':      # a low long block (the rectangular cover groups of the overhead ref)
        L, W, H = r.uniform(2.2, 3.4), r.uniform(0.9, 1.3), r.uniform(0.8, 1.2)
        b.cham((-L / 2, -W / 2, -0.2), (L / 2, W / 2, H), 0.1, 'ashlar')
    else:                    # a fallen chunk
        s = r.uniform(0.6, 1.0); b.cham((-s, -s * 0.7, -0.15), (s, s * 0.7, s * 1.1), 0.1, 'ashlar')
    me = b.build('KN_Crucible_CoverStone_%s%d' % (kind.capitalize(), seed)); me.materials.clear(); me.materials.append(CSF); me.materials.append(CST)
    for p in me.polygons: p.material_index = 1 if p.normal.z > 0.7 else 0
    xs = [v.co for v in me.vertices]; me['dim'] = [max(c.x for c in xs) - min(c.x for c in xs), max(c.y for c in xs) - min(c.y for c in xs), max(c.z for c in xs) - min(c.z for c in xs)]
    return me
STONES = [cover_stone(s, 'stack') for s in (1, 2, 3, 4)]; LOWS = [cover_stone(s, 'low') for s in (5, 6, 7)]; CHUNKS = [cover_stone(s, 'chunk') for s in (8, 9, 10)]
rnd = random.Random(21); bowl = coll('Bowl'); rim = coll('Rim_And_Perimeter')
def wbox(o):
    c = [o.matrix_world @ Vector(v) for v in o.bound_box]
    lo = Vector((min(p.x for p in c), min(p.y for p in c), min(p.z for p in c))); hi = Vector((max(p.x for p in c), max(p.y for p in c), max(p.z for p in c)))
    return lo, hi
bpy.context.view_layer.update()
def place(me, name, loc, rot, scale, c, **props):
    o = D.objects.new(name, me); o.location = loc; o.rotation_euler = rot; o.scale = scale; c.objects.link(o)
    o['synty_prefab'] = me['synty_prefab']; o['synty_pack'] = me['synty_pack']; o['pivot'] = me.get('pivot', [0, 0, 0])
    if 'material_variant' in me: o['material_variant'] = me['material_variant']
    for k, v in props.items(): o[k] = v
    return o
BPC = variant(K(DF, 'SM_Bld_Wall_L_Corner_01'), 'Arena', tint=(0.6, 0.54, 0.75)); bw = dim(BPC).x
COPN = variant(K(DF, 'SM_Bld_Wall_L_Trim_02'), 'Arena', tint=(0.74, 0.66, 0.92))
nb = 0
def flame_pillar(name, base, c, H=4.6, w=2.1, area='PurpleArena', power=420):
    """Square stone pillar with a wider cap and a brazier bowl (the purple flame pillars of the overhead ref). Flame = FX marker."""
    global nb
    nb += 1; base = Vector(base)
    for rr in (0, math.pi): place(BPC, name + ('_PillarA' if rr == 0 else '_PillarB'), base, (0, 0, rr), (w / bw, w / bw, H / 5.0), c)
    place(COPN, name + '_Cap', base + Vector((0, 0, H)), (0, 0, 0), (w * 1.25 / dim(COPN).x, w * 1.25 / dim(COPN).y, 1.0), c)
    place(BRZ, name, base + Vector((0, 0, H + dim(COPN).z)), (0, 0, 0), (min(1.5, w * 0.95 / dim(BRZ).x),) * 3, c)
    fx('BrazierFlame', area, base + Vector((0, 0, H + dim(COPN).z + 2.4)), 'violet / purple fire', 'purple flame on every pillar: ring round the arena floor, pairs at the foot and head of every stair, the rim and the perimeter (overhead ref)', power, (0.6, 0.2, 1.0))
for o in sorted([o for o in D.objects if o.name.startswith(('Bowl_Brazier_', 'Perimeter_Brazier_'))], key=lambda o: o.name):
    lo, hi = wbox(o); base = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)); nm = o.name; c = o.users_collection[0]; D.objects.remove(o, do_unlink=True)
    if base.z < -10 and 60 < math.hypot(base.x, base.y) < 76:                 # the pair at a grand stair foot: out to the new foot, just outside the cheek walls
        k_ = round(math.atan2(base.y, base.x) / (math.pi / 2)); ux, uy = math.cos(k_ * math.pi / 2), math.sin(k_ * math.pi / 2); sd = 1 if (-uy * base.x + ux * base.y) > 0 else -1
        base = Vector((ux * 77.9 - uy * sd * 9.9, uy * 77.9 + ux * sd * 9.9, base.z))
    flame_pillar(nm, base, c, area='PurpleArena' if nm.startswith('Bowl') else 'PurplePerimeter', power=320)
for q in range(4):                                    # the ring at the edge of the arena floor (four per quadrant, clear of the stair lines)
    for adeg in (20.0, 45.0, 70.0):
        a = math.radians(90 * q + adeg); flame_pillar('Bowl_FloorRingBrazier_%02d' % (q * 4 + int(adeg) // 20 + 1), (74.0 * math.cos(a), 74.0 * math.sin(a), -15.0), coll('Bowl'), H=5.6, w=2.5, power=520)
MEN = [parts.menhir(sd) for sd in (1, 2, 3, 4, 5, 6)]
for me_ in MEN:                                       # 022: smooth dark slabs (ref 01), no brick coursing on the standing stones
    me_.materials.clear(); me_.materials.append(CSF); me_.materials.append(CST)
    for pg in me_.polygons: pg.material_index = 1 if pg.normal.z > 0.6 else 0
def slab(name, base, H, c, lean=11):
    """Angular standing stone (custom menhir mesh, 6 m tall at scale 1), leaning a little: the broken spires of ref 01."""
    k = H / 6.0; w = k * rnd.uniform(0.95, 1.45)
    return put_custom(rnd.choice(MEN), name, Vector(base), (math.radians(rnd.uniform(-lean, lean)), math.radians(rnd.uniform(-lean, lean)), rnd.uniform(0, 6.28)), (w, w, k), c)
DRB = syn(parts.dais_ring_block(), 'Neutral')
for k in range(8):                                    # two thick curved kerb blocks between each pair of the eight ways onto the dais
    for da in (-5.3, 5.3):
        put_custom(DRB, 'Bowl_DaisRingBlock_%02d%s' % (k + 1, 'a' if da < 0 else 'b'), (0, 0, -15.0), (0, 0, math.radians(22.5 + 45 * k + da)), (1, 1, 1), coll('Bowl'))
ns = 0
for o in sorted([o for o in D.objects if o.name.startswith('Bowl_Column_')], key=lambda o: o.name):
    lo, hi = wbox(o); base = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)); H = hi.z - lo.z; nm = o.name; c = o.users_collection[0]; D.objects.remove(o, do_unlink=True)
    ns += 1; slab(nm.replace('Column', 'Spire'), base, H * rnd.uniform(0.9, 1.3), c)
nc = 0
for o in sorted([o for o in D.objects if o.name.startswith(('Bowl_Cover_', 'Bowl_RuinWall'))], key=lambda o: o.name):   # 022: 007's holed brick ruin walls too
    lo, hi = wbox(o); d = hi - lo; base = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)); rz = o.rotation_euler.z; nm = o.name; c = o.users_collection[0]
    od = Vector(o.dimensions); D.objects.remove(o, do_unlink=True)
    me = STONES[nc % len(STONES)]; nd = dim(me); nc += 1
    lx, ly = max(od.x, od.y), min(od.x, od.y); long_is_x = od.x >= od.y
    sx = max(0.8, min(1.8, lx / max(nd.x, nd.y))); sz = max(0.8, min(1.6, od.z / nd.z))
    put_custom(me, nm, base, (0, 0, rz + (0 if (nd.x >= nd.y) == long_is_x else math.pi / 2)), (sx, sx, sz), c)
# dense ruin field between the eight stair routes (concept 01): slabs, broken pillars, wall stumps, rubble; routes stay 7 m clear
# (021's RUIN list of brick ruins is gone)
def off_route(x, y, margin=7.0):
    for k in range(4):
        a = math.radians(90 * k); d = abs(-math.sin(a) * x + math.cos(a) * y)
        if d < margin and (x * math.cos(a) + y * math.sin(a)) > 0: return False
    return math.hypot(x, y) > 17.0
taken = [Vector((o.location.x, o.location.y, 0)) for o in D.objects if o.name.startswith(('Bowl_Cover', 'Bowl_Spire', 'Bowl_Brazier'))]
nx = 0
for i in range(900):
    if nx >= 56: break
    a = rnd.uniform(0, 6.2832); r = 18 + 56 * math.sqrt(rnd.random()); x, y = r * math.cos(a), r * math.sin(a); p = Vector((x, y, 0))
    if not off_route(x, y) or any((p - t).length < 4.2 for t in taken): continue
    taken.append(p); nx += 1
    if rnd.random() < 0.45: slab('Bowl_Spire_X%03d' % nx, (x, y, -15.0), rnd.uniform(5.0, 12.5), bowl); ns += 1
    else:
        me = rnd.choice(STONES + LOWS); s_ = rnd.uniform(0.95, 1.45)
        put_custom(me, 'Bowl_Ruin_X%03d' % nx, (x, y, -15.0), (math.radians(rnd.uniform(-4, 4)), math.radians(rnd.uniform(-4, 4)), rnd.uniform(0, 6.28)), (s_, s_, s_ * rnd.uniform(0.9, 1.25)), bowl)
# menhir clusters round the dais (ref 01): one tall stone, two or three shorter ones, fallen blocks at their feet
for k in range(8):
    a = math.radians(22.5 + 45 * k + rnd.uniform(-5, 5)); r = rnd.uniform(19.5, 24.5); cx, cy = r * math.cos(a), r * math.sin(a)
    slab('Bowl_Menhir_C%02d_1' % (k + 1), (cx, cy, -15.0), rnd.uniform(6.5, 8.8), bowl, lean=7)
    for j in range(rnd.randint(2, 3)):
        b_ = rnd.uniform(0, 6.28); d_ = rnd.uniform(1.5, 2.6)
        if off_route(cx + d_ * math.cos(b_), cy + d_ * math.sin(b_), 5.5): slab('Bowl_Menhir_C%02d_%d' % (k + 1, j + 2), (cx + d_ * math.cos(b_), cy + d_ * math.sin(b_), -15.0), rnd.uniform(2.8, 5.2), bowl, lean=14)
    for j in range(3):
        b_ = rnd.uniform(0, 6.28); d_ = rnd.uniform(2.6, 4.2); me = rnd.choice(CHUNKS); s_ = rnd.uniform(0.8, 1.3)
        if off_route(cx + d_ * math.cos(b_), cy + d_ * math.sin(b_), 5.5): put_custom(me,'Bowl_MenhirRubble_C%02d_%d' % (k + 1, j + 1), (cx + d_ * math.cos(b_), cy + d_ * math.sin(b_), -15.0), (0, 0, rnd.uniform(0, 6.28)), (s_, s_, s_), bowl)
# cover in two rings between the dais and the floor edge (overhead ref): menhir clusters alternate with groups of low rectangular blocks
# (021's BLKA ruin wall caps are gone: LOWS / CHUNKS)
def block_group(tag, cx, cy):
    for j in range(rnd.randint(2, 3)):
        b_ = rnd.uniform(0, 6.28); d_ = rnd.uniform(0.0, 2.8) if j else 0.0; x, y = cx + d_ * math.cos(b_), cy + d_ * math.sin(b_)
        if off_route(x, y, 5.5): put_custom(rnd.choice(LOWS if j == 0 else LOWS + CHUNKS), 'Bowl_CoverBlock_%s_%d' % (tag, j + 1), (x, y, -15.0), (0, 0, rnd.uniform(0, 6.28)), (rnd.uniform(0.85, 1.15), rnd.uniform(0.85, 1.15), rnd.uniform(0.85, 1.3)), bowl)
def menhir_cluster(tag, cx, cy, tall):
    slab('Bowl_Menhir_%s_1' % tag, (cx, cy, -15.0), tall, bowl, lean=8)
    for j in range(rnd.randint(1, 3)):
        b_ = rnd.uniform(0, 6.28); d_ = rnd.uniform(1.5, 2.6); x, y = cx + d_ * math.cos(b_), cy + d_ * math.sin(b_)
        if off_route(x, y, 5.5): slab('Bowl_Menhir_%s_%d' % (tag, j + 2), (x, y, -15.0), rnd.uniform(2.8, 5.0), bowl, lean=14)
for k in range(8):                                    # inner ring, beside the clusters placed above
    a = math.radians(22.5 + 45 * k + rnd.choice((-12, 12))); r = rnd.uniform(27, 32); block_group('I%02d' % (k + 1), r * math.cos(a), r * math.sin(a))
for k in range(16):                                   # outer ring
    a = math.radians(11.25 + 22.5 * k + rnd.uniform(-2, 2)); r = rnd.uniform(46, 53); x, y = r * math.cos(a), r * math.sin(a)
    if k % 2 == 0: menhir_cluster('O%02d' % (k + 1), x, y, rnd.uniform(5.5, 8.0))
    else: block_group('O%02d' % (k + 1), x, y)
for k in range(16):                                   # third ring, near the floor edge
    if k % 4 == 0: continue                           # the four stair lanes
    a = math.radians(22.5 * k + rnd.uniform(-4, 4)); r = rnd.uniform(61, 67); x, y = r * math.cos(a), r * math.sin(a)
    if k % 2 == 0: block_group('E%02d' % (k + 1), x, y)
    else: menhir_cluster('E%02d' % (k + 1), x, y, rnd.uniform(5.0, 7.5))
# rock in the four dead corners of the rim square, so the bowl reads round from above (concept 05); the rim ring r < 118 stays clear
for sx in (1, -1):
    for sy in (1, -1):
        for j in range(7):
            x, y = sx * rnd.uniform(86, 112), sy * rnd.uniform(86, 112)
            if math.hypot(x, y) < 124: continue
            s_ = rnd.uniform(3.0, 6.5)
            place(SLAB, 'Rim_CornerRock_%s%s_%02d' % ('E' if sx > 0 else 'W', 'N' if sy > 0 else 'S', j + 1), (x, y, -0.5), (0, 0, rnd.uniform(0, 6.28)), (s_, s_, s_ * rnd.uniform(0.8, 1.6)), rim)
print('ARENA braziers', nb, 'spires', ns, 'cover', nc)

bpy.context.view_layer.update(); nup = 0
for o in list(D.objects):
    if not o.name.startswith('Bowl_') or o.type != 'MESH': continue        # (BowlNew_* pieces are built at their final height)
    if o.name.startswith('Bowl_Stair_Grand'): o.scale.z *= 0.5; o['steps'] = '50 x 0.15 m'; continue
    lo, hi = wbox(o)
    if lo.z < -6.0: o.location.z += RAISE; nup += 1
for o in list(coll('FX_Markers_For_Unity').objects) + list(coll('Lights_PreviewOnly').objects):
    if o.location.z < -1.0 and math.hypot(o.location.x, o.location.y) < 80: o.location.z += RAISE
mh = D.objects.get('MARK_CrucibleHeart')
if mh: mh.location.z += RAISE
print('BOWL raised', nup)
# 3) sealed north yard (Ronin, expansion): chained black shards over the gate; no banners, no faction colour
SY = Yard('Neutral', yaw=90.0, name='Sealed')
# 022 (owner image 4: "look at the 4th gate and how its blocked off with chains and such i want this look"): the gate mouth is
# choked by a pile of huge black shards that rises above the wall, bound by heavy iron chains crossed from tower to tower,
# purple fires on posts at the stair head. Yard-local frame: gate wall at x = 116.25, outside (arena side) is x < 116.
def shard_mesh(seed, H):
    r = random.Random(seed); b = parts.MB('Neutral'); n = r.choice((4, 5, 5, 6)); R = r.uniform(1.6, 2.4) * (H / 10.0) ** 0.5   # chunky slabs, not needles (owner image 4)
    base = [(R * math.cos(2 * math.pi * k / n + r.uniform(-0.25, 0.25)) * r.uniform(0.75, 1.2), R * math.sin(2 * math.pi * k / n + r.uniform(-0.25, 0.25)) * r.uniform(0.6, 1.0)) for k in range(n)]
    tx, ty = r.uniform(-0.35, 0.35) * R, r.uniform(-0.3, 0.3) * R; zm = H * r.uniform(0.45, 0.62)
    mid = [(p[0] * r.uniform(0.55, 0.8) + tx * 0.5, p[1] * r.uniform(0.55, 0.8) + ty * 0.5) for p in base]
    b.prism(base, -1.0, zm, 'ashlar', cap=False, top_ring=mid)
    for i in range(n):
        j = (i + 1) % n; b.poly([(mid[i][0], mid[i][1], zm), (mid[j][0], mid[j][1], zm), (tx, ty, H)], 'ashlar')
    me = b.build('KN_Crucible_SealShard_%d' % seed); me.materials.clear(); me.materials.append(CSF); me.materials.append(CST)
    for pg in me.polygons: pg.material_index = 1 if pg.normal.z > 0.5 else 0
    return me
remove(r'^Yard_Sealed_Rubble_')                               # 007's block rubble on the stair head hid the pile
SHM = [shard_mesh(70 + i, h) for i, h in enumerate((16.0, 13.0, 11.0, 9.0, 7.0, 5.0, 20.0))]
for i, (x, y, z, k, sc, tx, ty) in enumerate((
        (115.2, 0.0, 0.0, 0, 1.12, 4, 0), (115.6, -3.6, 0.0, 1, 1.05, -6, -9), (115.4, 3.8, 0.0, 1, 1.0, 5, 10), (116.4, -1.8, 2.0, 2, 1.0, 2, -4),
        (116.2, 2.0, 1.5, 2, 1.05, -3, 6), (114.6, -6.4, 0.0, 3, 1.0, -4, -16), (114.6, 6.6, 0.0, 3, 1.0, 6, 15), (113.8, -1.2, 0.0, 4, 1.0, 12, -5),
        (113.9, 1.9, 0.0, 4, 0.9, 14, 8), (117.2, -5.2, 3.0, 3, 0.9, -8, -12), (117.0, 5.4, 3.0, 3, 0.9, 8, 12), (112.8, -4.2, 0.0, 5, 1.0, 20, -18),
        (112.9, 4.4, 0.0, 5, 0.95, 18, 20), (111.8, 0.6, 0.0, 5, 0.8, 35, 6), (117.6, 0.2, 5.0, 2, 1.0, 0, 3),
        (117.8, -2.6, 2.0, 6, 1.0, -5, -6), (118.2, 3.0, 2.5, 6, 0.9, 4, 9), (118.6, 7.6, 4.0, 1, 1.0, 3, 18), (118.6, -7.4, 4.0, 1, 1.0, -3, -18),
        (113.6, -2.9, 0.0, 3, 1.1, 10, -8), (113.7, 3.1, 0.0, 3, 1.05, 9, 9), (114.4, 0.4, 0.0, 2, 0.9, 6, 2), (112.6, -1.6, 0.0, 4, 0.9, 22, -6), (112.7, 2.2, 0.0, 4, 0.85, 20, 7))):
    SY.put(SHM[k], 'SealShard', x, y, z, rot=rnd.uniform(0, 360), scale=sc, rx=ty * 0.6, ry=tx)
IRON = parts.mat('iron', 'Neutral')
def chain_mesh(name, pts, link=0.95, wide=0.5, bar=0.13):
    """Heavy chain along a polyline (each link an open box ring, every other one turned 90 degrees)."""
    b = parts.MB('Neutral'); pts = [Vector(p) for p in pts]; k = 0
    for a_, c_ in zip(pts, pts[1:]):
        seg = c_ - a_; L = seg.length; d = seg.normalized(); n_ = max(1, int(L / (link * 0.78)))
        side0 = d.cross(Vector((0, 0, 1))); side0 = side0.normalized() if side0.length > 1e-3 else Vector((1, 0, 0)); up0 = side0.cross(d).normalized()
        for i in range(n_):
            p = a_ + seg * ((i + 0.5) / n_); sd = side0 if k % 2 == 0 else up0; k += 1
            e0, e1 = p - d * link / 2, p + d * link / 2; o = sd * (wide / 2 - bar / 2)
            b.beam(e0 + o, e1 + o, bar, bar, 'iron'); b.beam(e0 - o, e1 - o, bar, bar, 'iron')
            b.beam(e0 - sd * wide / 2, e0 + sd * wide / 2, bar, bar, 'iron'); b.beam(e1 - sd * wide / 2, e1 + sd * wide / 2, bar, bar, 'iron')
    me = b.build(name); me.materials.clear(); me.materials.append(IRON)
    return me
def sag(p, q, drop, n=8):
    return [tuple(Vector(p).lerp(Vector(q), t / n) - Vector((0, 0, drop * 4 * (t / n) * (1 - t / n)))) for t in range(n + 1)]
xc = 113.3                                                    # chains hang in front of the pile, from tower to tower
for i, (p, q, dr) in enumerate((((xc, -8.6, 13.2), (xc, 8.6, 2.6), 0.6), ((xc, 8.6, 13.2), (xc, -8.6, 2.6), 0.6), ((xc, -8.6, 10.2), (xc, 8.6, 10.2), 1.8),
                                ((xc, -8.6, 6.4), (xc, 8.6, 6.4), 1.4), ((xc - 0.2, -8.6, 13.6), (xc - 0.2, 8.6, 6.0), 1.0), ((xc - 0.2, 8.6, 13.6), (xc - 0.2, -8.6, 6.0), 1.0))):
    wp = [tuple(SY.w(*c)) for c in sag(p, q, dr)]
    me = chain_mesh('KN_Crucible_SealChain_%d' % (i + 1), wp); o = put_custom(me, 'Yard_Sealed_SealChain_%02d' % (i + 1), (0, 0, 0), (0, 0, 0), (1, 1, 1), SY.c); o['faction'] = 'Neutral'
ANC = parts.MB('Neutral'); ANC.box((-0.5, -0.5, -0.5), (0.5, 0.5, 0.5), 'iron'); ANCm = ANC.build('KN_Crucible_ChainAnchor')
for (y, z) in ((-8.6, 13.4), (8.6, 13.4), (-8.6, 10.2), (8.6, 10.2), (-8.6, 6.3), (8.6, 6.3), (-8.6, 2.6), (8.6, 2.6)):
    SY.put(ANCm, 'ChainAnchor', xc + 0.2, y, z, scale=(0.5, 0.9, 0.9))
for sd in (1, -1):                                            # purple fires on posts at the head of the north stair, beside the shards
    xw, yw_ = SY.w(110.6, sd * 8.8)[:2]; flame_pillar('Yard_Sealed_GateBrazier_%s' % ('L' if sd > 0 else 'R'), (xw, yw_, 0.0), SY.c, H=4.0, w=1.9, area='PurpleSealed', power=380)
fx('SealChains', 'SealedNorth', SY.w(113.0, 0, 9.0), 'cold violet', 'heavy iron chains crossed over the black shard pile that seals the north gate, faint violet shimmer between the shards (owner image 4)', 600, (0.5, 0.3, 1.0))

# 4) outside the walls: only the near ground and near rock. The far view, the peaks and the aurora are a SKYBOX in Unity.
og = D.objects.get('Outer_Ground')
if og: custom_surface(og, 'M_Crucible_OuterGround_LavaCracked', 'T_Crucible_OuterRock.png', 'T_Crucible_OuterRock_Emit.png', 170.0, tint=(0.8, 0.7, 0.95), strength=6.0)
remove(r'^Outer_Spire_'); near = coll('Outer_Ground_NonPlayable'); ROCK = K(DFa, 'SM_Env_Rock_01'); nr = 0
for i in range(26):
    a = 2 * math.pi * i / 26 + rnd.uniform(-0.08, 0.08); r = rnd.uniform(178, 205)
    x, y = r * math.cos(a), r * math.sin(a)
    me = ROCK; s = rnd.uniform(6, 12); nr += 1
    place(me, 'Outer_Rock_%02d' % nr, (x, y, -11), (0, 0, rnd.uniform(0, 6.28)), (s, s * rnd.uniform(0.6, 1.0), s * rnd.uniform(1.0, 2.6)), near, non_colliding=True)
    if i % 2 == 0: fx('LavaFlow', 'OuterGround', (x * 0.9, y * 0.9, -9.8), 'orange-red', 'lava fissures and slow lava rivers in the black rock outside the walls (concepts 05, 11); the ground mesh carries a crack emission map', 0)
fxmark('FX_Skybox_01', 'Skybox', (0, 0, 150), 'purple + green aurora on deep blue night', 'SKYBOX (cubemap), not geometry: night sky with bright green and violet aurora curtains reaching high overhead, stars, and on the horizon a ring of jagged black peaks with red lava glow at their feet, plus the ruined extractor on the north-east horizon (concept 05). Lighting is set in Unity.', 'Sky')
sky = D.objects.get('SkyCube_NON_COLLIDING')
if sky: sky.scale.z = 2.4; sky.location.z = 29.4; sky['preview_only'] = True; sky.name = 'SkyboxStandIn_PREVIEW_ONLY'
# standalone map: origin at the Crucible heart, own bounds
b = D.objects.new('MAP_Bounds_Crucible', None); b.empty_display_type = 'CUBE'; b.scale = (250, 250, 120); b.location = (0, 0, 60)
coll('Markers_For_Unity').objects.link(b); b['map'] = 'The Crucible'; b['standalone'] = True; b['playable_half_extent_m'] = 180; b['origin'] = 'Crucible heart (0,0,0 = rim level)'

for i, (x, y, z, p) in enumerate(((0, 0, 34, 30000), (58, 58, 26, 12000), (-58, 58, 26, 12000), (58, -58, 26, 12000), (-58, -58, 26, 12000))):
    l = D.lights.new('PreviewArenaFill', 'POINT'); l.energy = p; l.color = (0.92, 0.84, 1.0); l.shadow_soft_size = 8; l.use_shadow = True
    lo = D.objects.new('PreviewLight_ArenaFill_%02d' % i, l); lo.location = (x, y, z); coll('Lights_PreviewOnly').objects.link(lo)
# ---- the measure: 1.8 m Sidekick figures (the owner's mannequin scene, Soldier Male), preview only, never exported to Unity
figlib = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'scale_figure.blend')
if os.path.exists(figlib):
    with D.libraries.load(figlib, link=False) as (src, dst): dst.meshes = [m for m in src.meshes if m.startswith('KN_ScaleFigure')]
    FIG = dst.meshes[0]; fc = coll('ScaleFigures_PREVIEW_ONLY')
    for nm, loc, face in (('Heg_Landing', (-130, 0, 0), 0), ('Heg_GateThreshold', (-119.6, 2.0, 1.2), 0), ('Heg_Crates', (-131.0, -15.0, 0), 200), ('Heg_Shrine', (-146.5, 0, 0.9), 180),
                          ('Cov_Landing', (130, 0, 0), 180), ('Cov_Shrine', (146.5, 0, 0.9), 0), ('Cov_Crates', (131.0, 15.0, 0), 20), ('Null_Landing', (0, -130, 0), 90), ('Null_Shrine', (0, -146.5, 0.9), 270),
                          ('Null_GateThreshold', (2.0, -119.6, 1.2), 90), ('Arena_TierFoot', (70.0, 40.4, -7.5), 210), ('Arena_FlamePillar', (50.6, 54.6, -7.5), 225), ('Arena_Dais', (0.0, -3.0, -6.3), 270),
                          ('Arena_StairFootEast', (78.6, 3.0, -7.5), 180), ('Arena_Menhirs', (17.0, 9.0, -7.5), 200), ('Rim_StairHeadEast', (106.0, 3.0, 0.0), 180)):
        o = D.objects.new('ScaleFigure_' + nm, FIG); o.location = loc; o.rotation_euler = (0, 0, math.radians(face + 90)); fc.objects.link(o); o['preview_only'] = True; o['height_m'] = 1.8
sc = bpy.context.scene
moon = D.objects.get('Moon')
if moon: moon.data.energy = 0.35; moon.data.color = (0.62, 0.6, 1.0); moon.rotation_euler = (math.radians(52), 0, math.radians(200))
bg = sc.world.node_tree.nodes.get('Background')
if bg: bg.inputs[0].default_value = (0.16, 0.07, 0.30, 1); bg.inputs[1].default_value = 0.55
# ---- 022: preview-only flame stand-ins at every brazier FX marker, so the review shots read like the concepts (Unity's live fire
# replaces them: collection PreviewFlames_PREVIEW_ONLY, preview_only = True, never imported)
FLC = {'Covenant': (1.0, 0.42, 0.04), 'Null': (1.0, 0.08, 0.02), 'Hegemony': (0.5, 0.8, 1.0)}
def flame_mesh(key, col):
    b = parts.MB('Neutral'); Hf, Wf = 1.9, 1.0
    for k in range(3):
        a = math.pi * k / 3; ca, sa = math.cos(a) * Wf / 2, math.sin(a) * Wf / 2
        b.poly([(-ca, -sa, 0.0), (ca, sa, 0.0), (ca * 0.55, sa * 0.55, Hf * 0.45), (0, 0, Hf), (-ca * 0.55, -sa * 0.55, Hf * 0.45)], 'glow')
    me = b.build('KN_PreviewFlame_' + key); me.materials.clear()
    m = D.materials.get('M_PreviewFlame_' + key) or flat('M_PreviewFlame_' + key, col, 0.5)
    bn = m.node_tree.nodes['Principled BSDF']; bn.inputs['Emission Color'].default_value = (*col, 1); bn.inputs['Emission Strength'].default_value = 3.2
    m.use_backface_culling = False; me.materials.append(m); me['preview_only'] = True
    return me
FLM = {}; pfc = coll('PreviewFlames_PREVIEW_ONLY'); npf = 0
for e in [o for o in coll('FX_Markers_For_Unity').objects if o.get('fx_type') == 'BrazierFlame']:
    ar = e.get('area', ''); key = ar if ar in FLC else 'Purple'
    if key not in FLM: FLM[key] = flame_mesh(key, FLC.get(key, (0.55, 0.12, 1.0)))
    o = D.objects.new('PreviewFlame_%s_%03d' % (key, npf), FLM[key]); o.location = e.location - Vector((0, 0, 2.2)); o['preview_only'] = True
    pfc.objects.link(o); npf += 1
print('PREVIEW flames', npf)
# ---- 022: normal + height maps on every stone material (owner: "variance in the ground so they dont feel flat ... rocks walls
# pillars, pretty much all the rock like texture materials"). Textured stone: maps on the texture's own UV; flat-colour stone
# (tier blocks, stair cheeks, cover stones) and atlas rocks (cliffs, spires, shards, rim rocks): Synty's rock maps box-projected in metres.
import relief
used = {}
for o in D.objects:
    if o.type != 'MESH' or o.get('preview_only') or o.name.startswith(('SkyboxStandIn', 'ScaleFigure')): continue
    for m in o.data.materials:
        if m: used.setdefault(m.name, m)
nuv = sum(1 for m in list(used.values()) if relief.uv_relief(m))
nflat = sum(1 for m in list(used.values()) if m.name.startswith(('M_Crucible_TierBlock_', 'M_Crucible_StairCheek_', 'M_Crucible_CoverStone_')) and relief.box_relief(m, tile=3.0))
ROCKY = re.compile(r'Rock|Cliff|Basalt|Boulder'); nrock = 0
for me in {o.data for o in D.objects if o.type == 'MESH' and ROCKY.search(str(o.get('synty_prefab', '')))}:
    for i, m in enumerate(me.materials):
        if m and not m.get('relief_mapping'): me.materials[i] = relief.relief_copy(m, tile=4.0); nrock += 1
plain = sorted(n for n, m in used.items() if not m.get('relief_mapping') and re.search(r'Brick|Stone|Paving|Floor|Rock|Tier|Stair|Wall', n))
print('RELIEF uv', nuv, 'flat', nflat, 'rock slots', nrock, 'stone-named without maps', plain[:20])
json.dump(FX, open(os.path.join(os.path.dirname(OUT), 'Build009', 'fx_rows.json'), 'w'))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
syn = sum(1 for o in D.objects if 'synty_prefab' in o); cus = sum(1 for o in D.objects if 'crucible_piece' in o)
print('BUILD022 objects', len(D.objects), 'synty-placed', syn, 'custom-placed', cus, 'fx', len(FX), 'meshes', len([m for m in D.meshes if m.users]),
      'tris', sum(parts.tris(o.data) for o in D.objects if o.type == 'MESH'))
