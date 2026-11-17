# Checkpoint 022b = the DATA pass on the approved 022 look (owner: "as long as it gave you precise locations for lighting, fire and
# its color, floor lava placement"). No look changes. Run on the 022 master:
#   blender -b Crucible_022_Look.blend --python Build009/data_022b.py -- <Crucible_022b_Data.blend>
# Writes (beside the blend): FX_022.json, LAVA_022.json, COLLIDERS_022.json, GATES_022.json, WALLSTRIDE_022.json, STATS_022.json,
# Review022/top_full.png + top_arena.png (orthographic, mapping in TOPVIEW_022.json).
# Unity axes: Blender (x east, y north, z up) -> Unity (x, y, z) = (-x, z, -y), metres (the FBX convention of UNITY-IMPORT-PLAN.md).
import bpy, bmesh, sys, os, math, json, random, re
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import parts, relief
D = bpy.data; OUT = sys.argv[sys.argv.index('--') + 1]; R = os.path.dirname(OUT)
def coll(name):
    c = D.collections.get(name)
    if not c: c = D.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c
def U(v): return [round(-v[0], 3), round(v[2], 3), round(-v[1], 3)]
def B(v): return [round(v[0], 3), round(v[1], 3), round(v[2], 3)]
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
def unity_rot(m3):
    r = C @ m3 @ C.inverted()
    x = math.degrees(math.asin(max(-1, min(1, -r[1][2])))); y = math.degrees(math.atan2(r[0][2], r[2][2])); z = math.degrees(math.atan2(r[1][0], r[1][1]))
    q = r.to_quaternion()
    return [round(x, 2), round(y, 2), round(z, 2)], [round(q.x, 5), round(q.y, 5), round(q.z, 5), round(q.w, 5)]
bpy.context.view_layer.update()

# ======================================================================= 1) lights, fire, glow
FIRE = {'Covenant': (1.0, 0.42, 0.10), 'Null': (1.0, 0.12, 0.03), 'Hegemony': (0.55, 0.80, 1.0)}
PURPLE = (0.55, 0.14, 1.0)
def spec(e):
    t = e.get('fx_type'); ar = e.get('area', ''); note = ''
    pl = D.objects.get('PreviewLight_' + e.name[3:]); pw = pl.data.energy / 1.7 if pl else 0.0
    rng = round(max(5.0, min(30.0, 4.0 + math.sqrt(max(pw, 1.0)) * 0.35)), 1)
    fac = ar if ar in FIRE else None
    if t == 'BrazierFlame':
        col = FIRE[fac] if fac else PURPLE
        hero = any(k in str(e.get('note', '')) for k in ()) or False
        return col, dict(group='Fire', light='Point', intensity=2.5 if fac else 2.0, range=min(rng, 10.0), emissive_ev=4.0, vfx='flame + embers, flicker on emissive')
    if t == 'StripGlow': return parts.PALETTE[fac]['glow'] if fac else PURPLE, dict(group='Glow', light='None (emissive strip, baked)', intensity=0.0, range=0.0, emissive_ev=3.0, vfx='bloom')
    if t == 'ShrineBeam': return parts.PALETTE[fac]['glow'], dict(group='Shrine', light='Spot up + Point', intensity=6.0, range=18.0, emissive_ev=3.5, vfx='light pillar, motes')
    if t == 'LavaCracks': return (1.0, 0.2, 0.05), dict(group='Lava', light='Point', intensity=4.0 if ar == 'Heart' else 2.0, range=25.0 if ar == 'Heart' else 14.0, emissive_ev=3.5, vfx='pulse on emission, heat haze, embers (LAVA_022.json)')
    if t == 'LavaFlow': return (1.0, 0.28, 0.06), dict(group='Lava', light='Point', intensity=3.0, range=30.0, emissive_ev=3.5, vfx='heat haze, smoke (LAVA_022.json)')
    if t == 'SealChains': return (0.5, 0.3, 1.0), dict(group='Glow', light='Point', intensity=2.0, range=12.0, emissive_ev=2.5, vfx='violet shimmer between the shards')
    if t == 'BannerSway': return (0, 0, 0), dict(group='Cloth', light='None', intensity=0.0, range=0.0, emissive_ev=0.0, vfx='cloth sway (wind zone)')
    if t == 'Skybox': return (0.2, 0.1, 0.4), dict(group='Sky', light='None', intensity=0.0, range=0.0, emissive_ev=0.0, vfx='replaced in 022b by the sky dome (SkyDome_R500)')
    return (1, 1, 1), dict(group='Other', light='None', intensity=0.0, range=0.0, emissive_ev=0.0, vfx='')
def mode_for(e, s):
    t, ar, n = e.get('fx_type'), e.get('area', ''), str(FXNOTES.get(e.name, ''))
    if s['light'].startswith('None'): return 'None', False, False
    if t == 'ShrineBeam': return 'Mixed', True, False
    if t == 'LavaCracks' and ar == 'Heart': return 'Mixed', False, False
    if t == 'BrazierFlame' and ('gate mouth' in n or 'foot of the Null gate' in n or 'flanking the shrine' in n or ar == 'PurpleSealed'): return 'Realtime', False, True
    return 'Baked', False, False
FXNOTES = {}
rows = json.load(open(os.path.join(HERE, 'fx_rows.json')))
fxe = [o for o in coll('FX_Markers_For_Unity').objects if o.get('fx_type')]
# fx_rows were written in creation order = the FX_ empty order by name counter; match notes by (type, area) sequence
seq = {}
for typ, area, colour, note in rows: seq.setdefault((typ, area), []).append(note)
for o in fxe:
    m = re.match(r'FX_(.+)_(\d+)$', o.name)
    if m:
        lst = seq.get((o.get('fx_type'), o.get('area')), []); i = int(m.group(2)) - 1
        FXNOTES[o.name] = lst[i] if i < len(lst) else ''
fx = []
for e in sorted(fxe, key=lambda o: o.name):
    col, s = spec(e); mode, shadows, flicker = mode_for(e, s)
    eu, q = unity_rot(e.matrix_world.to_3x3())
    fx.append(dict(name=e.name, type=e.get('fx_type'), area=e.get('area'), group=s['group'], note=FXNOTES.get(e.name, ''),
                   blender_pos=B(e.matrix_world.translation), unity_pos=U(e.matrix_world.translation), unity_euler=eu, unity_quat_xyzw=q,
                   colour_linear=[round(c, 3) for c in col], colour_words=e.get('fx_colour', ''),
                   light=dict(type=s['light'], mode=mode, shadows=('Soft' if shadows else 'Off'), intensity=s['intensity'], range_m=s['range'], flicker=flicker),
                   emissive=dict(colour_linear=[round(c, 3) for c in col], hdr_ev=s['emissive_ev']), vfx=s['vfx']))
moon = D.objects.get('Moon')
if moon:
    eu, q = unity_rot(moon.matrix_world.to_3x3())
    fx.append(dict(name='Moon', type='Directional', area='Sky', group='Sky', note='the only shadow-casting directional light (cool moonlight)', blender_pos=B(moon.location), unity_pos=U(moon.location),
                   unity_euler=eu, unity_quat_xyzw=q, colour_linear=list(moon.data.color), colour_words='cool blue-violet moonlight',
                   light=dict(type='Directional', mode='Mixed', shadows='Soft', intensity=0.35, range_m=0, flicker=False), emissive=dict(colour_linear=[0, 0, 0], hdr_ev=0), vfx=''))
from collections import Counter
budget = Counter((f['group'], f['light']['mode'], f['light']['shadows']) for f in fx)
FXJ = dict(checkpoint='022b', axes='Unity (x, y, z) = Blender (-x, z, -y), metres; euler = Unity order (Z, X, Y applied), degrees',
           units='intensity = URP light intensity (non-physical), range_m = light range; emissive hdr_ev = HDR exposure stops on the emission colour',
           budget={'%s | %s | shadows %s' % k: v for k, v in sorted(budget.items())}, markers=fx)
json.dump(FXJ, open(os.path.join(R, 'FX_022.json'), 'w'), indent=1)
print('FX', len(fx), dict(budget))

# ======================================================================= 2) lava paths, heat haze, embers
GROUND = [o for o in D.objects if o.type == 'MESH' and o.name.startswith(('BowlNew_Floor_', 'Bowl_CrucibleFloor_Dais', 'Outer_Ground'))]
dg = bpy.context.evaluated_depsgraph_get(); verts, polys = [], []
for o in GROUND:
    me = o.evaluated_get(dg).to_mesh(); mw = o.matrix_world; b0 = len(verts)
    verts += [mw @ v.co for v in me.vertices]; polys += [[b0 + i for i in p.vertices] for p in me.polygons]; o.evaluated_get(dg).to_mesh_clear()
gbvh = BVHTree.FromPolygons(verts, polys)
def gz(x, y, default):
    h = gbvh.ray_cast(Vector((x, y, 20.0)), Vector((0, 0, -1)), 60.0)
    return h[0].z + 0.01 if h[0] else default
def srgb2lin(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
REG = {'ArenaFloor': ((1.0, 0.07, 0.04), 5.0, -7.5), 'HeartDais': ((1.0, 0.07, 0.04), 7.0, -6.3), 'OuterGround': ((1.0, 0.2, 0.05), 6.0, -10.0)}
def dp2(P, tol):
    if len(P) < 3: return P
    a, b = Vector(P[0][:2]), Vector(P[-1][:2]); ab = b - a; L = ab.length or 1e-9
    d = [abs(ab.x * (p[1] - a.y) - ab.y * (p[0] - a.x)) / L for p in P[1:-1]]; i = max(range(len(d)), key=lambda k: d[k]) + 1
    return dp2(P[:i + 1], tol)[:-1] + dp2(P[i:], tol) if d[i - 1] > tol else [P[0], P[-1]]
raw = json.load(open(os.path.join(HERE, 'lava_paths_raw.json')))
lava = []; haze_caps = []; embers = []; smoke = []; cells = Counter()
for p in raw['paths']:
    pts = p['pts']; L = sum(math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1))
    if L < (0.8 if p['region'] != 'OuterGround' else 2.0): continue
    col, strength, z0 = REG[p['region']]; lin = [srgb2lin(c) for c in col]
    p3 = [(x, y, gz(x, y, z0)) for x, y in pts]
    w = max(0.06, p['width_m'])
    lava.append(dict(region=p['region'], quarter=p.get('quarter'), length_m=round(L, 2), width_m=round(w, 3), emission_colour_linear=[round(c, 4) for c in lin],
                     emission_hdr_intensity=round(strength * srgb2lin(min(1.0, p['emit'])), 3), points_unity=[U(v) for v in p3], points_blender=[B(v) for v in p3]))
    coarse = dp2(p3, 1.0)
    for a, b in zip(coarse, coarse[1:]):
        haze_caps.append(dict(region=p['region'], a_unity=U(a), b_unity=U(b), radius_m=round(max(1.0, w / 2 + 0.8), 2), height_m=2.5))
    step = 3.0 if p['region'] != 'OuterGround' else 14.0; acc = step * 0.5
    for a, b in zip(p3, p3[1:]):
        seg = math.dist(a[:2], b[:2])
        cells[(p['region'], int(math.floor(a[0] / 16)), int(math.floor(a[1] / 16)))] += seg
        while acc + seg >= step:
            t = (step - acc) / seg; q = Vector(a).lerp(Vector(b), t); a = tuple(q); seg -= step - acc; acc = 0.0
            if p['region'] == 'OuterGround': smoke.append(U(q))
            else: embers.append(U(q))
        acc += seg
haze_boxes = []
for (reg, cx, cy), L in cells.items():
    if L < 4.0: continue
    z0 = REG[reg][2]; c = Vector((cx * 16 + 8, cy * 16 + 8, gz(cx * 16 + 8, cy * 16 + 8, z0) + 1.5))
    haze_boxes.append(dict(region=reg, centre_unity=U(c), size_unity=[16.0, 3.0, 16.0], lava_length_m=round(L, 1)))
LJ = dict(checkpoint='022b', axes=FXJ['axes'], source='skeletons of the emission maps the floors use (lava_022.py): exact to about 4-8 cm',
          surfaces={'ArenaFloor': 'T_Floor_Arena_Quarter_Emit, emission strength 5', 'HeartDais': 'T_Floor_Dais_Emit, strength 7', 'OuterGround': 'T_Crucible_OuterRock_Emit, strength 6 (only outside the walls)'},
          counts=dict(paths=len(lava), haze_capsules=len(haze_caps), haze_boxes=len(haze_boxes), ember_points=len(embers), smoke_points=len(smoke)),
          heat_haze_note='Use haze_boxes (16 m cells over the cracks, 3 m tall) as the cheap volume set; haze_capsules hug each crack if a finer look is wanted.',
          paths=lava, haze_boxes=haze_boxes, haze_capsules=haze_caps, ember_points_unity=embers, smoke_points_unity=smoke)
json.dump(LJ, open(os.path.join(R, 'LAVA_022.json'), 'w'))
print('LAVA', LJ['counts'])

# ======================================================================= 3a) colliders (box proxies, merged per piece group)
CC = coll('Colliders_For_Unity'); BC = coll('Blockers_For_Unity')
for c_ in (CC, BC): c_.hide_render = True
SKIP = re.compile(r'Banner|PreviewFlame|LightStrip|LightCap|Chain(?!Anchor)|Rubble|Skulls|Canopy|ScaleFigure|SkyCube|SkyboxStandIn|Spikes|Finial|Awning|Flame|Decal|StripGlow|Outer_|SealChain|ChainAnchor')
GROUND_OK = re.compile(r'^(Yard_\w+_Floor$|Rim_Plateau|BowlNew_Floor_|Bowl_CrucibleFloor_Dais|Yard_\w+_LandingPad)')
HULL = re.compile(r'^(Yard_\w+_RespawnShrine|Yard_Sealed_Gatehouse|Perimeter_CornerTower)')
STAIR_OBJ = re.compile(r'^Yard_\w+_(RampartStair_\d+|GateStairs_\d+)$')   # 022b: flights -> Concord-style ramp boxes (STAIRS_022.json)
UBOX = D.meshes.get('UCX_UnitBox')
if not UBOX:
    b_ = bmesh.new(); bmesh.ops.create_cube(b_, size=1.0); UBOX = D.meshes.new('UCX_UnitBox'); b_.to_mesh(UBOX); b_.free()
def kind_of(n):
    k = re.sub(r'(_\d+)+$', '', n); k = re.sub(r'\.\d+$', '', k)
    return 'wall' if re.search(r'Wall|Coping|Gatehouse', k) and 'Lintel' not in k else k   # lintels stay apart: never bridge a gate
boxes = []; ground_list = []; hull_list = []; stair_objs = []
for o in [o for o in D.objects if o.type == 'MESH']:
    n = o.name; cn = o.users_collection[0].name if o.users_collection else ''
    if o.get('preview_only') or cn.endswith('PREVIEW_ONLY') or cn in ('Lights_PreviewOnly', 'Colliders_For_Unity', 'Blockers_For_Unity') or SKIP.search(n): continue
    if n.startswith('GateLeaf'): continue
    if GROUND_OK.match(n): o['mesh_collider_ok'] = True; ground_list.append(n); continue
    if STAIR_OBJ.match(n): stair_objs.append(o); continue
    if n.startswith('BowlNew_Tiers_') or n.startswith('BowlNew_GrandStair_'): continue      # replaced by ring / ramp proxies below
    mw = o.matrix_world; loc, rot, sca = mw.decompose(); eu = rot.to_euler()
    lo = Vector([min(v[i] for v in o.bound_box) for i in range(3)]); hi = Vector([max(v[i] for v in o.bound_box) for i in range(3)])
    size = Vector((hi - lo)) * 1.0; size = Vector((size.x * abs(sca.x), size.y * abs(sca.y), size.z * abs(sca.z)))
    if max(size) < 0.6: continue
    if re.match(r'^Yard_\w+_Rampart$', n):                   # wall-walk that bridges the gate: two solid ends + the deck over the passage (a hull would fill the gate)
        xs = [v.co for v in o.data.vertices]; lo3 = Vector([min(c[i] for c in xs) for i in range(3)]); hi3 = Vector([max(c[i] for c in xs) for i in range(3)])
        la = 0 if (hi3.x - lo3.x) >= (hi3.y - lo3.y) else 1; mid = (lo3[la] + hi3[la]) / 2; gh = 6.4
        under = [c.z for c in xs if abs(c[la] - mid) < gh - 0.2 and c.z > 2.0]; zb = min(under) if under else hi3.z - 1.0
        for (a0, a1, z0) in ((lo3[la], mid - gh, lo3.z), (mid + gh, hi3[la], lo3.z), (mid - gh, mid + gh, zb)):
            l_ = lo3.copy(); h_ = hi3.copy(); l_[la] = a0; h_[la] = a1; l_.z = z0
            boxes.append(dict(src=n, coll=cn, kind='RampartPart', yaw=None, rot=rot, ctr=mw @ ((l_ + h_) / 2), size=Vector([(h_[i] - l_[i]) * abs(sca[i]) for i in range(3)]), tilt=True))
        continue
    if HULL.match(n):
        b_ = bmesh.new(); b_.from_mesh(o.data)
        if re.search(r'Stair', n):                            # stairs: a floor-level lip 1 m past each end, so the hull starts as a ramp, not a 0.3 m wall
            xs = [v.co for v in b_.verts]; lo3 = Vector([min(c[i] for c in xs) for i in range(3)]); hi3 = Vector([max(c[i] for c in xs) for i in range(3)])
            la = 0 if ('GateStairs' in n or (hi3.x - lo3.x) >= (hi3.y - lo3.y)) else 1; sa = 1 - la   # gate thresholds climb along their local x
            for end, d_ in ((lo3[la], -1.0), (hi3[la], 1.0)):
                for sv in (lo3[sa], hi3[sa]):
                    p_ = [0.0, 0.0, lo3.z]; p_[la] = end + d_; p_[sa] = sv; b_.verts.new(p_)
        bmesh.ops.convex_hull(b_, input=b_.verts)
        me = D.meshes.new('UCX_' + n); b_.to_mesh(me); b_.free(); h = D.objects.new('UCX_' + n, me); h.matrix_world = mw.copy(); CC.objects.link(h)
        h['collider'] = 'MeshCollider convex'; hull_list.append(h.name); continue
    tilt = abs(eu.x) > 0.02 or abs(eu.y) > 0.02
    ctr = mw @ ((lo + hi) / 2)
    boxes.append(dict(src=n, coll=cn, kind=kind_of(n), yaw=round(math.degrees(eu.z) % 180, 0) if not tilt else None, rot=rot, ctr=ctr, size=size, tilt=tilt))
def frame_box(b):
    yr = math.radians(b['yaw']); Rz = Matrix.Rotation(-yr, 3, 'Z'); c = Rz @ b['ctr']
    # size in the yaw frame: object local axes may be swapped by a 90/180 yaw remainder; the rot frame is exact for yaw mod 180
    loc_axes = b['rot'].to_matrix(); fx_ = Rz @ (loc_axes @ Vector((1, 0, 0)))
    sx, sy = (b['size'].x, b['size'].y) if abs(fx_.x) > 0.7 else (b['size'].y, b['size'].x)
    return [c.x - sx / 2, c.y - sy / 2, c.z - b['size'].z / 2], [c.x + sx / 2, c.y + sy / 2, c.z + b['size'].z / 2]
groups = {}
for b in boxes:
    if b['tilt']: groups.setdefault(('tilt', b['src'], len(groups)), []).append(b)   # every tilted / split part keeps its own box
    else: groups.setdefault((b['coll'], b['kind'], b['yaw']), []).append(b)
def vol(lo, hi): return max(0, hi[0] - lo[0]) * max(0, hi[1] - lo[1]) * max(0, hi[2] - lo[2])
merged = []
for key, bl in groups.items():
    if key[0] == 'tilt':
        b = bl[0]; merged.append(dict(key=key, kind=b['kind'], src=[b['src']], ctr=b['ctr'], size=b['size'], rot=b['rot'].to_matrix(), wall=False)); continue
    items = [(frame_box(b), [b['src']]) for b in bl]
    changed = True
    while changed:
        changed = False; i = 0
        while i < len(items):
            j = i + 1
            while j < len(items):
                (alo, ahi), asrc = items[i]; (blo, bhi), bsrc = items[j]
                ulo = [min(alo[k], blo[k]) for k in range(3)]; uhi = [max(ahi[k], bhi[k]) for k in range(3)]
                gap = max(max(alo[k], blo[k]) - min(ahi[k], bhi[k]) for k in range(3))
                ov = [max(0.0, min(ahi[k], bhi[k]) - max(alo[k], blo[k])) / max(1e-3, min(ahi[k] - alo[k], bhi[k] - blo[k])) for k in range(3)]
                twin = key[1] == 'wall' and sum(1 for k in range(3) if ov[k] >= 0.9) >= 2 and gap <= 2.6       # the two faces of one wall -> one solid wall
                if (gap <= 0.3 and vol(ulo, uhi) <= 1.12 * (vol(alo, ahi) + vol(blo, bhi))) or twin:
                    items[i] = ((ulo, uhi), asrc + bsrc); items.pop(j); changed = True
                else: j += 1
            i += 1
    yr = math.radians(key[2]); Rz = Matrix.Rotation(yr, 3, 'Z')
    for (lo, hi), src in items:
        c = Rz @ Vector([(lo[k] + hi[k]) / 2 for k in range(3)])
        merged.append(dict(key=key, kind=key[1], src=src, ctr=c, size=Vector([hi[k] - lo[k] for k in range(3)]), rot=Rz, wall=key[1] == 'wall'))
# arena: seven block rings as boxes per 15 degrees (not walk-up: blocks 1.07 m), grand stairs as ramps
RF, DRr, DH = 82.3, (103.7 - 82.3) / 7, 7.5 / 7
for q in range(4):
    for i in range(7):
        r0, r1 = RF + DRr * i, RF + DRr * (i + 1); zt = -7.5 + DH * (i + 1); rm = (r0 + r1) / 2
        half = math.asin(9.0 / r0); a0, a1 = q * math.pi / 2 + half, (q + 1) * math.pi / 2 - half; n = max(2, int((a1 - a0) / math.radians(15.0)))
        for k in range(n):
            a = a0 + (a1 - a0) * (k + 0.5) / n; L = 2 * r1 * math.sin((a1 - a0) / n / 2) + 0.2
            merged.append(dict(key=('Bowl', 'TierRing', None), kind='TierRing', src=['BowlNew_Tiers_%d' % (q + 1)], ctr=Vector((rm * math.cos(a), rm * math.sin(a), (zt - 8.5) / 2)),
                               size=Vector((r1 - r0 + 0.02, L, zt + 8.5)), rot=Matrix.Rotation(a, 3, 'Z'), wall=False))
# ---- 022b stairs (owner: Concord's stairs could not be walked; never again): every flight in Concord's form (Concord040Setup.Flights:
# x min, x max, z start, y start, z end, y end, Unity axes) + step height and count; an invisible Ground-layer ramp box whose top face runs
# through the step nosings (extended one tread down to the ground), 0.6 m thick like Concord's.
YAWD = {'Covenant': 0.0, 'Hegemony': 180.0, 'Null': -90.0}
flights = []
def add_flight(name, foot, head, y0, y1, width, n, riser, tread, kind):
    f = Vector((foot[0], foot[1], 0)); h = Vector((head[0], head[1], 0)); d = (h - f).normalized(); side = Vector((-d.y, d.x, 0))
    A = f - d * tread; A.z = y0; Bv = h.copy(); Bv.z = y1
    ua, ub = U(A), U(Bv); cs = [U(A + side * width / 2), U(A - side * width / 2), U(Bv + side * width / 2), U(Bv - side * width / 2)]
    if abs(d.y) >= abs(d.x):   # Blender y -> Unity z: Concord's exact form
        form = dict(axis='z', x_min=round(min(c[0] for c in cs), 3), x_max=round(max(c[0] for c in cs), 3), z_start=ua[2], y_start=ua[1], z_end=ub[2], y_end=ub[1])
    else:                      # Blender x -> Unity x: the same form turned 90 degrees
        form = dict(axis='x', z_min=round(min(c[2] for c in cs), 3), z_max=round(max(c[2] for c in cs), 3), x_start=ua[0], y_start=ua[1], x_end=ub[0], y_end=ub[1])
    flights.append(dict(name=name, kind=kind, concord_form=form, width_m=round(width, 2), steps=n, step_height_m=round(riser, 3), tread_m=round(tread, 3),
                        start_unity=ua, end_unity=ub, footprint_unity=cs, A=A, B=Bv, d=d, w=width))
for q in range(4):                                             # the four grand stairs (N stair leads to the sealed gate)
    u = Vector((math.cos(q * math.pi / 2), math.sin(q * math.pi / 2), 0)); nm = ('East (Covenant)', 'North (sealed gate)', 'West (Hegemony)', 'South (Null)')[q]
    add_flight('Grand stair ' + nm, u * 79.7, u * 103.7, -7.5, 0.0, 14.0, 25, 0.3, 1.0, 'grand stair')
for F, yw in YAWD.items():                                     # gate thresholds: four 0.3 m steps up from the rim, a 1.2 m platform, four down into the yard
    c_, s_ = math.cos(math.radians(yw)), math.sin(math.radians(yw)); W_ = lambda x, y: (x * c_ - y * s_, x * s_ + y * c_)
    add_flight('%s gate threshold, rim side' % F, W_(112.0, 0), W_(113.95, 0), 0.0, 1.2, 12.0, 4, 0.3, 0.65, 'gate threshold')
    add_flight('%s gate threshold, yard side' % F, W_(122.8, 0), W_(121.45, 0), 0.0, 1.2, 12.0, 4, 0.3, 0.45, 'gate threshold')
    pc = Vector((*W_((113.95 + 121.45) / 2, 0), 0.6))
    merged.append(dict(key=('Stairs', 'StairLanding', None), kind='StairLanding', src=['Yard_%s_GateStairs_01' % F], ctr=pc, size=Vector((121.45 - 113.95, 12.0, 1.2)), rot=Matrix.Rotation(math.radians(yw), 3, 'Z'), wall=False))
for o in stair_objs:                                           # rampart stairs: read from the mesh (tread levels)
    if 'GateStairs' in o.name: continue
    mw = o.matrix_world; ups = [(mw @ p.center) for p in o.data.polygons if (mw.to_3x3() @ p.normal).normalized().z > 0.9]
    lv = {}
    for c in ups: lv.setdefault(round(c.z / 0.05) * 0.05, []).append(c)
    zs = sorted(lv); n = len(zs)
    if n < 2: continue
    cen = [sum(lv[z], Vector()) / len(lv[z]) for z in zs]; d = (cen[-1] - cen[0]); d.z = 0; L = d.length; d.normalize()
    tread = L / (n - 1); riser = zs[-1] / n if zs[0] > 0.05 else (zs[-1] - zs[0]) / (n - 1)
    xs = [mw @ v.co for v in o.data.vertices]; side = Vector((-d.y, d.x, 0)); wid = max(v.dot(side) for v in xs) - min(v.dot(side) for v in xs)
    mid_side = (max(v.dot(side) for v in xs) + min(v.dot(side) for v in xs)) / 2
    foot = cen[0] - d * (tread / 2); foot = foot - side * (foot.dot(side) - mid_side); head = foot + d * tread * (n - 1)
    add_flight(o.name.replace('Yard_', '').replace('_', ' '), foot[:2], head[:2], 0.0, zs[-1], wid, n, riser, tread, 'rampart stair')
for fl in flights:
    A, Bv = fl['A'], fl['B']; dv = Bv - A; L = dv.length; dh = Vector((dv.x, dv.y, 0)).normalized(); slope = math.atan2(dv.z, Vector((dv.x, dv.y)).length)
    Rr = Matrix.Rotation(math.atan2(dh.y, dh.x), 3, 'Z') @ Matrix.Rotation(-slope, 3, 'Y'); up = Rr @ Vector((0, 0, 1))
    merged.append(dict(key=('Stairs', 'StairRamp', None), kind='StairRamp', src=[fl['name']], ctr=(A + Bv) / 2 - up * 0.3, size=Vector((L, fl['w'], 0.6)), rot=Rr, wall=False))
    lip = Bv + dh * 0.55; lip.z = Bv.z - 0.3                    # a 1 m flat lip at the head, so the ramp always reaches the landing / deck / rim
    merged.append(dict(key=('Stairs', 'StairLanding', None), kind='StairLanding', src=[fl['name'] + ' head lip'], ctr=lip, size=Vector((1.1, fl['w'], 0.6)), rot=Matrix.Rotation(math.atan2(dh.y, dh.x), 3, 'Z'), wall=False))
ucx = []
for i, m in enumerate(merged):
    o = D.objects.new('UCX_%s_%04d' % (re.sub(r'\W', '', m['kind'])[:28], i), UBOX); o.matrix_world = Matrix.Translation(m['ctr']) @ m['rot'].to_4x4() @ Matrix.Diagonal((*m['size'], 1))
    CC.objects.link(o); o['collider'] = 'BoxCollider, Ground layer (stair ramp)' if m['kind'] in ('StairRamp', 'StairLanding') else 'BoxCollider'; o['covers'] = ','.join(m['src'][:12]) + (' +%d' % (len(m['src']) - 12) if len(m['src']) > 12 else ''); m['obj'] = o; ucx.append(o)
# gate leaves: a box collider parented to each leaf, so it slides with it
GATES = []
for g in [o for o in D.objects if o.name.startswith('GateLeaf_')]:
    lo = Vector([min(v[i] for v in g.bound_box) for i in range(3)]); hi = Vector([max(v[i] for v in g.bound_box) for i in range(3)])
    o = D.objects.new('UCX_' + g.name, UBOX); o.parent = g; o.matrix_parent_inverse = Matrix.Identity(4)
    o.location = (lo + hi) / 2; o.scale = hi - lo; CC.objects.link(o); o['collider'] = 'BoxCollider (kinematic, moves with the leaf)'
    sl = Vector((g.get('slide_x', 0.0), g.get('slide_y', 0.0), g.get('slide_up', 0.0)))
    eu, q = unity_rot(g.matrix_world.to_3x3())
    GATES.append(dict(name=g.name, faction=g.get('muster_gate', 'Sealed' if g.get('sealed') else ''), motion='sealed (never opens)' if g.get('sealed') else ('slide up (portcullis)' if sl.z else 'slide sideways into the wall'),
                      pivot_note='object origin = bottom centre of the leaf in its CLOSED place; the slide axis runs through it',
                      closed_pos_unity=U(g.matrix_world.translation), open_pos_unity=U(g.matrix_world.translation + sl), slide_vector_unity=U(sl), slide_distance_m=round(sl.length, 2),
                      unity_euler=eu, size_m=[round(v, 2) for v in (hi - lo)], opens_when='Crossfire starts (all three faction gates together); suggested 2.5 s ease-in-out',
                      blender_pos=B(g.matrix_world.translation)))
json.dump(dict(checkpoint='022b', axes=FXJ['axes'], gates=GATES), open(os.path.join(R, 'GATES_022.json'), 'w'), indent=1)
print('COLLIDERS boxes', len(ucx), 'hulls', len(hull_list), 'ground mesh colliders', len(ground_list), 'gates', len(GATES))

# ======================================================================= 3c) Wallstride faces + invisible blockers
ART = [o for o in D.objects if o.type == 'MESH' and (o.get('mesh_collider_ok') or o.name.startswith('Outer_Ground'))]
verts, polys = [], []
for o in ART:
    me = o.evaluated_get(dg).to_mesh(); mw = o.matrix_world; b0 = len(verts)
    verts += [mw @ v.co for v in me.vertices]; polys += [[b0 + i for i in p.vertices] for p in me.polygons]; o.evaluated_get(dg).to_mesh_clear()
fbvh = BVHTree.FromPolygons(verts, polys)
def playable(p):
    h = fbvh.ray_cast(Vector((p.x, p.y, 4.0)), Vector((0, 0, -1)), 30.0)
    return h[0] is not None and h[0].z > -8.6, (h[0].z if h[0] else None)
wall_faces = []; blockers = []; cut_by_stairs = []
for m in merged:
    if not m['wall']: continue
    s = m['size']; c = m['ctr']; Rz = m['rot']
    if s.z < 4.0 or max(s.x, s.y) < 4.0: continue
    along = 'x' if s.x >= s.y else 'y'; L = max(s.x, s.y); T = min(s.x, s.y)
    ax = Rz @ (Vector((1, 0, 0)) if along == 'x' else Vector((0, 1, 0))); nrm = Rz @ (Vector((0, 1, 0)) if along == 'x' else Vector((1, 0, 0)))
    open_sides = 0
    for sd in (1, -1):
        n = nrm * sd; fc = c + n * (T / 2); ok, gzv = playable(fc + n * 1.5)
        if not ok: continue
        open_sides += 1; z0 = max(c.z - s.z / 2, gzv); z1 = c.z + s.z / 2
        keep = [(-L / 2, L / 2)]                                # cut out every stair flight within 2.5 m of this face (Concord: stairs are never Wallstride)
        for fl in flights:
            pts_ = [Vector((fl['A'].x, fl['A'].y, 0)) + sv * fl['w'] / 2 * Vector((-fl['d'].y, fl['d'].x, 0)) for sv in (1, -1)] + [Vector((fl['B'].x, fl['B'].y, 0)) + sv * fl['w'] / 2 * Vector((-fl['d'].y, fl['d'].x, 0)) for sv in (1, -1)]
            fc0 = Vector((fc.x, fc.y, 0))
            if min(abs((q_ - fc0).dot(n)) for q_ in pts_) > 2.5 or min((q_ - fc0).dot(n) for q_ in pts_) < -0.5: continue
            a_, b_ = min((q_ - fc0).dot(ax) for q_ in pts_) - 0.5, max((q_ - fc0).dot(ax) for q_ in pts_) + 0.5
            keep = [piece for (k0, k1) in keep for piece in ((k0, min(k1, a_)), (max(k0, b_), k1)) if piece[1] - piece[0] > 0.01]
            cut_by_stairs.append(dict(collider=m['obj'].name, stair=fl['name']))
        for (k0, k1) in keep:
            if k1 - k0 < 4.0: continue
            corners = [fc + ax * k0 + Vector((0, 0, z0 - fc.z)), fc + ax * k1 + Vector((0, 0, z0 - fc.z)), fc + ax * k1 + Vector((0, 0, z1 - fc.z)), fc + ax * k0 + Vector((0, 0, z1 - fc.z))]
            wall_faces.append(dict(collider=m['obj'].name, length_m=round(k1 - k0, 2), height_m=round(z1 - z0, 2), base_z_unity=round(z0, 2), normal_unity=U(n), corners_unity=[U(v) for v in corners],
                                   area=m['key'][0]))
    if open_sides == 1:                                       # one side is outside the map: an invisible blocker up to 40 m on top
        top = c.z + s.z / 2; h = 40.0 - top
        if h > 1.0:
            o = D.objects.new('Blocker_%04d' % len(blockers), UBOX); o.matrix_world = Matrix.Translation(Vector((c.x, c.y, top + h / 2))) @ Rz.to_4x4() @ Matrix.Diagonal((s.x + 0.4, s.y + 0.4, h, 1))
            BC.objects.link(o); o['collider'] = 'BoxCollider, invisible, blocks players only'; o.display_type = 'WIRE'
            blockers.append(dict(name=o.name, centre_unity=U(o.matrix_world.translation), size_m=[round(s.x + 0.4, 2), round(h, 2), round(s.y + 0.4, 2)], unity_euler=unity_rot(Rz)[0]))
sg = D.objects.get('GateLeaf_Sealed_North')
if sg:                                                         # the sealed gate mouth (shards are climbable art): blocker from the ground
    o = D.objects.new('Blocker_SealedGate', UBOX); o.matrix_world = Matrix.Translation(Vector((0, 116.0, 20.0))) @ Matrix.Diagonal((16.0, 6.0, 40.0, 1)); BC.objects.link(o); o.display_type = 'WIRE'
    blockers.append(dict(name=o.name, centre_unity=U(o.matrix_world.translation), size_m=[16.0, 40.0, 6.0], unity_euler=[0, 0, 0]))
side_walls = []                                                # stair side walls (grand-stair cheeks, threshold sides, the wall behind each rampart stair): NOT Wallstride
for fl in flights:
    sd = Vector((-fl['d'].y, fl['d'].x, 0))
    for sv in (1, -1): side_walls.append(dict(stair=fl['name'], from_unity=U(fl['A'] + sd * sv * fl['w'] / 2), to_unity=U(fl['B'] + sd * sv * fl['w'] / 2)))
SJ = dict(checkpoint='022b', axes=FXJ['axes'], rule='Concord040Setup.Flights form: (x min, x max, z start, y start, z end, y end) for flights along Unity z; flights along Unity x give (z min, z max, x start, y start, x end, y end). Each flight gets an invisible BoxCollider on the Ground layer, 0.6 m thick, its top face through the step nosings (start = one tread before the first riser, at ground height; end = the last nosing, at the landing height). Stairs and their side walls are excluded from the Wallstride bake (SurfaceGraphBuilder ConcordStairFootprints).',
          flights=[{k: v for k, v in fl.items() if k not in ('A', 'B', 'd', 'w')} for fl in flights],
          ring_steps=[dict(name='Heart dais (Bowl_CrucibleFloor_Dais)', centre_unity=[0.0, -7.5, 0.0], note='round dais 1.2 m high with eight ways up between kerb blocks: plain MeshCollider (walkable, validated); no ramp needed'),
                      dict(name='Covenant and Null respawn daises', note='round steps 0.3 / 0.6 / 0.9 m to the 0.9 m platform, drum to 2.3 m: convex hull collider (UCX_Yard_*_RespawnShrine) works as a cone ramp to the platform'),
                      dict(name='Hegemony tech plinth', note='hex plinth: convex hull collider')],
          side_walls_not_wallstride=side_walls, wallstride_faces_cut_by_stairs=cut_by_stairs)
json.dump(SJ, open(os.path.join(R, 'STAIRS_022.json'), 'w'), indent=1)
print('STAIRS flights', len(flights), 'wall faces cut', len(cut_by_stairs))
json.dump(dict(checkpoint='022b', axes=FXJ['axes'], note='Wallstride may use these flat wall faces (from the merged wall colliders, playable side only, 4 m+ tall and long).',
               faces=wall_faces), open(os.path.join(R, 'WALLSTRIDE_022.json'), 'w'))
print('WALLSTRIDE faces', len(wall_faces), 'BLOCKERS', len(blockers))
json.dump(dict(checkpoint='022b', axes=FXJ['axes'],
               rules='UCX_* with collider=BoxCollider -> BoxCollider from the object transform (unit cube x scale); UCX_* convex meshes -> MeshCollider convex; objects with mesh_collider_ok -> plain MeshCollider (simple ground); every other art mesh: NO collider. Blocker_* -> invisible BoxCollider on a player-only layer.',
               boxes=[dict(name=m['obj'].name, kind=m['kind'], centre_unity=U(m['ctr']), size_m_unity=[round(m['size'].x, 3), round(m['size'].z, 3), round(m['size'].y, 3)],
                           unity_euler=unity_rot(m['rot'])[0], covers=m['obj']['covers']) for m in merged],
               convex_hulls=hull_list, ground_mesh_colliders=ground_list, blockers=blockers), open(os.path.join(R, 'COLLIDERS_022.json'), 'w'))

# ======================================================================= 3d) sky box out: sky dome + ring of peaks + far outer floor
for o in list(D.objects):
    if o.name.startswith(('SkyCubeFace_', 'SkyboxStandIn')): D.objects.remove(o, do_unlink=True)
DOME_R = 500.0
sk = coll('SkyDome'); bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=24, radius=DOME_R, calc_uvs=True)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -40.0], context='VERTS'); bmesh.ops.reverse_faces(bm, faces=bm.faces)
dme = D.meshes.new('KN_Crucible_SkyDome_R500'); bm.to_mesh(dme); bm.free()
smat = D.materials.new('M_Crucible_SkyDome_Aurora'); smat.use_nodes = True; nt = smat.node_tree; nt.nodes.clear()
em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial'); ti = nt.nodes.new('ShaderNodeTexImage')
ti.image = D.images.load(os.path.join(R, 'Textures', 'Crucible_Sky.png')); nt.links.new(ti.outputs[0], em.inputs[0]); em.inputs[1].default_value = 1.0; nt.links.new(em.outputs[0], out.inputs[0])
dme.materials.append(smat); so = D.objects.new('SkyDome_R500', dme); sk.objects.link(so)
so['sky_dome_radius_m'] = DOME_R; so['note'] = 'unlit aurora sky dome; camera far clip >= 600 m; the dome follows the camera height in Unity is NOT needed (map is 360 m)'; so['non_colliding'] = True
near = coll('Outer_Ground_NonPlayable'); rnd = random.Random(2202); npk = 0
LSEAM = D.materials.get('M_Crucible_PeakLavaSeam')
if not LSEAM:
    LSEAM = D.materials.new('M_Crucible_PeakLavaSeam'); LSEAM.use_nodes = True; bn = LSEAM.node_tree.nodes['Principled BSDF']
    bn.inputs['Base Color'].default_value = (0.25, 0.03, 0.01, 1); bn.inputs['Emission Color'].default_value = (1.0, 0.12, 0.02, 1); bn.inputs['Emission Strength'].default_value = 1.6
    LSEAM['crucible_kind'] = 'lava'; LSEAM['note'] = 'lava seam on the far peaks: dark red, low glow (Unity: emissive, no light)'
def rockify(me):
    for i, m in enumerate(me.materials):
        if m.get('crucible_kind') == 'rock': me.materials[i] = relief.relief_copy(m, tile=12.0)
        elif m.get('crucible_kind') == 'lava': me.materials[i] = relief.relief_copy(parts.mat('rock', 'Neutral'), tile=12.0)   # no seams on the far peaks: they read as slabs; lava glow stays on the ground
for i in range(18):                                            # far peaks: a jagged ring inside the dome (concept 05)
    a = 2 * math.pi * i / 18 + rnd.uniform(-0.07, 0.07); r = rnd.uniform(330, 410)
    me = parts.mountain(500 + i, R=rnd.uniform(40, 62), H=rnd.uniform(95, 190)); rockify(me)
    o = D.objects.new('Outer_Peak_%02d' % (i + 1), me); o.location = (r * math.cos(a), r * math.sin(a), -12.0); o.rotation_euler = (0, 0, a - math.pi / 2); near.objects.link(o); o['non_colliding'] = True; npk += 1
for i in range(28):                                            # near crags between the walls and the peaks
    a = 2 * math.pi * i / 28 + rnd.uniform(-0.08, 0.08); r = rnd.uniform(215, 290)
    me = parts.mountain(600 + i, R=rnd.uniform(10, 18), H=rnd.uniform(22, 48)); rockify(me)
    o = D.objects.new('Outer_Crag_%02d' % (i + 1), me); o.location = (r * math.cos(a), r * math.sin(a), -11.0); o.rotation_euler = (0, 0, a - math.pi / 2); near.objects.link(o); o['non_colliding'] = True
bm = bmesh.new(); n = 64; ring_o = [bm.verts.new((DOME_R * math.cos(2 * math.pi * k / n), DOME_R * math.sin(2 * math.pi * k / n), -12.5)) for k in range(n)]
ring_i = [bm.verts.new((200 * math.cos(2 * math.pi * k / n), 200 * math.sin(2 * math.pi * k / n), -12.5)) for k in range(n)]
for k in range(n): bm.faces.new([ring_i[k], ring_o[k], ring_o[(k + 1) % n], ring_i[(k + 1) % n]])
uvl = bm.loops.layers.uv.new('UVMap')
for f in bm.faces:
    for l in f.loops: l[uvl].uv = (l.vert.co.x / 170.0, l.vert.co.y / 170.0)
fme = D.meshes.new('KN_Crucible_OuterGroundFar'); bm.to_mesh(fme); bm.free()
og = D.objects.get('Outer_Ground')
if og and og.data.materials: fme.materials.append(og.data.materials[0])
fo = D.objects.new('Outer_Ground_Far', fme); near.objects.link(fo); fo['non_playable'] = True
for o in coll('FX_Markers_For_Unity').objects:
    if o.get('fx_type') == 'Skybox': o['fx_type'] = 'SkyDome'; o['note'] = 'sky dome R500 (SkyDome_R500), unlit aurora; peaks are geometry (Outer_Peak_*)'
print('SKY dome', DOME_R, 'peaks', npk)

# ======================================================================= 4) stats
def tri(me): return sum(len(p.vertices) - 2 for p in me.polygons)
per = Counter(); objs = Counter(); pairs = 0; mats = set(); meshes = set(); heavy = Counter()
for o in D.objects:
    if o.type != 'MESH': continue
    cn = o.users_collection[0].name if o.users_collection else '?'; t = tri(o.data)
    per[cn] += t; objs[cn] += 1; meshes.add(o.data.name)
    if not (cn.endswith('PREVIEW_ONLY') or cn in ('Colliders_For_Unity', 'Blockers_For_Unity')):
        pairs += len([m for m in o.data.materials if m]); mats.update(m.name for m in o.data.materials if m); heavy[o.data.name] += t
imgs = []
for im in D.images:
    if im.size[0] == 0 or im.users == 0: continue
    imgs.append(dict(name=im.name, size=list(im.size), mb_rgba8_mips=round(im.size[0] * im.size[1] * 4 * 1.333 / 1e6, 1)))
relief_stems = sorted({m.get('relief_source') for m in D.materials if m.get('relief_source')})
mem = dict(relief_maps=len(relief_stems), at_2K_MB_BC5_BC4=round(len(relief_stems) * 2048 * 2048 * 1.5 * 1.333 / 1e6, 1), at_8K_MB_BC5_BC4=round(len(relief_stems) * 8192 * 8192 * 1.5 * 1.333 / 1e6, 1))
ST = dict(checkpoint='022b', tris_per_collection=dict(per.most_common()), objects_per_collection=dict(objs.most_common()), unique_meshes=len(meshes),
          materials_in_use=len(mats), object_material_pairs_draw_estimate=pairs, heaviest_meshes_by_total_tris=heavy.most_common(25), images=sorted(imgs, key=lambda d: -d['mb_rgba8_mips']),
          relief_texture_memory=mem)
json.dump(ST, open(os.path.join(R, 'STATS_022.json'), 'w'), indent=1)
print('STATS draws~', pairs, 'materials', len(mats), 'tris', sum(per.values()))

# ======================================================================= 5) orthographic top views for the overlays
sc = bpy.context.scene; cam_d = D.cameras.new('TopOrtho'); cam_d.type = 'ORTHO'; cam = D.objects.new('TopOrtho_PREVIEW_ONLY', cam_d); sc.collection.objects.link(cam)
cam.rotation_euler = (0, 0, 0); cam_d.clip_end = 2000; sc.camera = cam
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_percentage = 100; sc.eevee.taa_render_samples = 16
for c_ in (CC, BC, sk): c_.hide_render = True
views = {}
for tag, half, px in (('top_full', 270.0, 2700), ('top_arena', 90.0, 2700)):
    cam.location = (0, 0, 400); cam_d.ortho_scale = 2 * half; sc.render.resolution_x = sc.render.resolution_y = px
    sc.render.filepath = os.path.join(R, 'Review022', tag + '.png'); bpy.ops.render.render(write_still=True)
    views[tag] = dict(centre_blender=[0, 0], half_extent_m=half, px=px, note='pixel (col,row) -> blender x = -half + (col+0.5)/px*2*half, y = half - (row+0.5)/px*2*half')
json.dump(views, open(os.path.join(R, 'TOPVIEW_022.json'), 'w'), indent=1)
D.objects.remove(cam, do_unlink=True)
for c_ in (CC, BC): c_.hide_render = True
for o in list(CC.objects) + list(BC.objects): o.hide_render = True
sk.hide_render = False
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('DATA022B saved', OUT)
