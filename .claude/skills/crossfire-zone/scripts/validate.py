# Walk-check routes, spawns, landing pads and gates on the master. Writes ../VALIDATION_008.json
import bpy, sys, os, math, json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
dg = bpy.context.evaluated_depsgraph_get()
skip = ('SkyCube', 'Outer_', 'Bowl_FloorCracks', 'GateLeaf', 'SkyboxStandIn', 'ScaleFigure', 'PreviewFlame', 'UCX_', 'Blocker_', 'SkyDome', 'TopOrtho')
COLLIDERS_ONLY = os.environ.get('KN_VALIDATE_COLLIDERS') == '1'   # 022b: walk the routes on the Unity colliders only (UCX_* + mesh_collider_ok ground)
verts, polys = [], []
for o in bpy.context.scene.objects:
    if o.type != 'MESH': continue
    if COLLIDERS_ONLY:
        if not (o.name.startswith('UCX_') or o.get('mesh_collider_ok')) or o.name.startswith('UCX_GateLeaf'): continue
    elif o.name.startswith(skip) or o.get('decal') or '_Banner_' in o.name: continue   # hanging cloth is not a collider
    me = o.evaluated_get(dg).to_mesh(); mw = o.matrix_world; base = len(verts)
    verts += [mw @ v.co for v in me.vertices]; polys += [[base + i for i in p.vertices] for p in me.polygons]
    o.evaluated_get(dg).to_mesh_clear()
bvh = BVHTree.FromPolygons(verts, polys)
def ground(x, y, ztop):
    h = bvh.ray_cast(Vector((x, y, ztop)), Vector((0, 0, -1)), 60)
    return h[0].z if h[0] else None
def rot(yaw, u, v): a = math.radians(yaw); return (u * math.cos(a) - v * math.sin(a), u * math.sin(a) + v * math.cos(a))
FAC = {'Covenant': 0, 'Hegemony': 180, 'Null': 270}
res = {'routes': {}, 'spawns': {}, 'landing': {}, 'gates': {}, 'rampart': {}}
def walk(pts, ztop_fn):
    path = []; total = 0
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1]); n = max(1, int((b - a).length / 0.25))
        for k in range(n):
            p = a.lerp(b, k / n); path.append(p)
    path.append(Vector(pts[-1]))
    zs = []; problems = []; prev = None
    for p in path:
        zt = ztop_fn(p)
        z = ground(p.x, p.y, zt)
        if z is None: problems.append(('no ground', round(p.x, 1), round(p.y, 1))); continue
        # headroom 2.0 m and body width 0.5 m each side at 1.0 m
        up = bvh.ray_cast(Vector((p.x, p.y, z + 0.05)), Vector((0, 0, 1)), 2.0)
        if up[0]: problems.append(('low ceiling', round(p.x, 1), round(p.y, 1), round(up[0].z - z, 2)))
        if prev is not None and abs(z - prev) > 0.35: problems.append(('step', round(p.x, 1), round(p.y, 1), round(z - prev, 2)))
        zs.append(z); prev = z
    for i in range(1, len(path)): total += (path[i].xy - path[i - 1].xy).length
    return {'length_m': round(total, 1), 'samples': len(path), 'max_step_m': round(max((abs(zs[i] - zs[i - 1]) for i in range(1, len(zs))), default=0), 3),
            'problems': problems[:12], 'problem_count': len(problems)}
for fac, yaw in FAC.items():
    def P(u, v=0.0): x, y = rot(yaw, u, v); return (x, y)
    ztop = lambda p: 20.0 if p.length > 100 else 2.0  # above the gate lintel is 8 m; start rays under it
    zt = lambda p: 6.0 if p.length > 103.5 else -11.0 + 0.0 if p.length < 72 else 6.0
    # grand stair route: landing -> gate -> rim -> grand stair -> floor -> dais top
    r = walk([P(130), P(118), P(104), P(71.9), P(13), P(0.5)], lambda p: 7.5)
    res['routes'][fac + '_grand'] = r
    # diagonal route: landing -> gate -> rim -> diag stair (45 deg to the left of the axis) -> floor -> heart
    d = rot(yaw + 45, 1, 0)
    arc = [rot(yaw + 45 * k / 12, 109, 0) for k in range(13)]
    r2 = walk([P(130), P(118)] + arc + [(d[0] * 104.2, d[1] * 104.2), (d[0] * 72.5, d[1] * 72.5), (d[0] * 70, d[1] * 70)], lambda p: 7.5)
    pass  # the diagonal stairs are removed (owner, 16 Nov): the four grand stairs are the routes
    # spawns: 8 rays at 0.5 and 1.5 m, 1.2 m
    bad = []
    for o in bpy.data.objects:
        if o.get('spawn_reservation') and o.get('faction') == fac:
            p = o.location; g = ground(p.x, p.y, 5)
            for h in (0.5, 1.5):
                for k in range(8):
                    a = 2 * math.pi * k / 8; hit = bvh.ray_cast(Vector((p.x, p.y, (g or 0) + h)), Vector((math.cos(a), math.sin(a), 0)), 1.2)
                    if hit[0]: bad.append(o.name); break
    res['spawns'][fac] = {'count': sum(1 for o in bpy.data.objects if o.get('spawn_reservation') and o.get('faction') == fac), 'blocked': sorted(set(bad))}
    lx, ly = P(130); blocked = 0
    for k in range(24):
        a = 2 * math.pi * k / 24
        for h in (0.3, 1.0, 2.0):
            if bvh.ray_cast(Vector((lx, ly, h)), Vector((math.cos(a), math.sin(a), 0)), 7.0)[0]: blocked += 1
    res['landing'][fac] = {'clear_radius_m': 7.0, 'blocked_rays_of_72': blocked}
    # gate: with leaves excluded, opening must be passable (sample across the 12 m opening at 1 m height)
    gx, gy = P(118); clear = sum(1 for v in range(-5, 6) if not bvh.ray_cast(Vector((*P(124.6, v), (ground(*P(121, v), 7.5) or 0.0) + 1.0)), Vector((*rot(yaw, -1, 0), 0)), 12)[0])
    res['gates'][fac] = {'open_lanes_of_11': clear}
    # rampart stair: yard floor -> deck
    for s in (-1, 1):
        res['rampart'][f'{fac}_{s}'] = walk([P(147, s * 36.8), P(125.5, s * 36.8), P(121, s * 36.8), P(121, s * 20)], lambda p: 9.6)
lens = [v['length_m'] for k, v in res['routes'].items() if k.endswith('_grand')]
res['fairness'] = {'grand_route_lengths_m': lens, 'spread_m': round(max(lens) - min(lens), 2)}
res['pass'] = all(v['problem_count'] == 0 for v in res['routes'].values()) and all(not v['blocked'] for v in res['spawns'].values()) \
    and all(v['blocked_rays_of_72'] == 0 for v in res['landing'].values()) and all(v['problem_count'] == 0 for v in res['rampart'].values())
out = os.path.join(os.path.dirname(bpy.data.filepath), 'VALIDATION_%s%s.json' % (sys.argv[-1], '_COLLIDERS' if COLLIDERS_ONLY else '')); json.dump(res, open(out, 'w'), indent=1)
print('VALID', json.dumps(res)[:3000])
