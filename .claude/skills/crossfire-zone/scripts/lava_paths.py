# Checkpoint 022b: the exact lava crack paths, read from the emission maps the floors really use (system Python: numpy, scipy, PIL).
# Each map is thinned to a 1-pixel skeleton, traced into polylines, simplified, and mapped to world metres through the same UV
# mapping the build uses:
#   arena floor  T_Floor_Arena_Quarter_Emit  quarter disc, UV = (x, y) / 82.3, repeated 4x by 90 deg rotation, z = -7.5
#   heart dais   T_Floor_Dais_Emit           frame x, y in -12.5 .. 12.5 (UV = (p + 12.5) / 25), top at z = -6.3
#   outer ground T_Crucible_OuterRock_Emit   UV = (x, y) / 170 (repeats), z = -10, kept only outside the walls
# Output: Build009/lava_paths_raw.json (world x, y per point; z and Unity axes are added in Blender by data_022b.py).
import os, json, math
import numpy as np
from PIL import Image
from scipy import ndimage as nd
HERE = os.path.dirname(os.path.abspath(__file__)); TEX = os.path.join(os.path.dirname(HERE), 'Textures')


def thin(b):
    """Zhang-Suen thinning, vectorised."""
    b = b.astype(np.uint8).copy()
    while True:
        changed = False
        for step in (0, 1):
            p = np.pad(b, 1)
            P2, P3, P4, P5, P6, P7, P8, P9 = (p[:-2, 1:-1], p[:-2, 2:], p[1:-1, 2:], p[2:, 2:], p[2:, 1:-1], p[2:, :-2], p[1:-1, :-2], p[:-2, :-2])
            nb = P2 + P3 + P4 + P5 + P6 + P7 + P8 + P9
            seq = [P2, P3, P4, P5, P6, P7, P8, P9, P2]
            A = sum(((seq[i] == 0) & (seq[i + 1] == 1)).astype(np.uint8) for i in range(8))
            if step == 0: c = (P2 * P4 * P6 == 0) & (P4 * P6 * P8 == 0)
            else: c = (P2 * P4 * P8 == 0) & (P2 * P6 * P8 == 0)
            m = (b == 1) & (nb >= 2) & (nb <= 6) & (A == 1) & c
            if m.any(): b[m] = 0; changed = True
        if not changed: return b.astype(bool)


NB8 = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]
def trace(sk):
    pts = set(zip(*np.nonzero(sk)))
    def nbrs(q): return [(q[0] + dy, q[1] + dx) for dy, dx in NB8 if (q[0] + dy, q[1] + dx) in pts]
    deg = {q: len(nbrs(q)) for q in pts}
    nodes = {q for q, d in deg.items() if d != 2}
    seen = set(); paths = []
    def walk(a, b):
        path = [a, b]; prev, cur = a, b
        while cur not in nodes:
            nx = [n for n in nbrs(cur) if n != prev and frozenset((cur, n)) not in seen]
            if not nx: break
            seen.add(frozenset((cur, nx[0]))); prev, cur = cur, nx[0]; path.append(cur)
            if cur == a: break
        return path
    for a in nodes:
        for b in nbrs(a):
            e = frozenset((a, b))
            if e in seen: continue
            seen.add(e); paths.append(walk(a, b))
    for q in pts:                                             # closed loops with no node
        for b in nbrs(q):
            if frozenset((q, b)) not in seen: seen.add(frozenset((q, b))); paths.append(walk(q, b))
    return paths


def dp(P, tol):
    if len(P) < 3: return P
    a, b = np.array(P[0], float), np.array(P[-1], float); ab = b - a; L = np.hypot(*ab) or 1e-9
    d = [abs(ab[0] * (p[1] - a[1]) - ab[1] * (p[0] - a[0])) / L for p in P[1:-1]]
    i = int(np.argmax(d)) + 1
    if d[i - 1] > tol: return dp(P[:i + 1], tol)[:-1] + dp(P[i:], tol)
    return [P[0], P[-1]]


def extract(img, thr, minlen_px):
    E = np.asarray(Image.open(img).convert('RGB'), np.float32) / 255.0; L = E.max(-1)
    m = L > thr; m = nd.binary_opening(m, iterations=1)
    dist = nd.distance_transform_edt(m); sk = thin(m)
    out = []
    for p in trace(sk):
        if len(p) < minlen_px: continue
        w = float(np.mean([dist[q] for q in p])) * 2.0; e = float(np.mean([L[q] for q in p]))
        out.append((dp(p, 1.5), w, e))
    return out, E.shape[1], E.shape[0]


def world_arena(r, c, W, H, q):
    u, v = c / W, 1 - r / H; x, y = u * 82.3, v * 82.3; a = q * math.pi / 2
    return x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)


res = {'note': 'world metres, Blender axes (x east, y north); z and Unity axes added by data_022b.py', 'paths': []}
arena, W, H = extract(os.path.join(TEX, 'Crucible013', 'T_Floor_Arena_Quarter_Emit.jpg'), 0.22, 12)
for q in range(4):
    for P, w, e in arena:
        pts = [world_arena(r, c, W, H, q) for r, c in P]
        pts = [p for p in pts if math.hypot(*p) < 82.0 and math.hypot(*p) > 12.6]   # the dais covers r < 12.5; tier blocks start at 82.3
        if len(pts) >= 2: res['paths'].append(dict(region='ArenaFloor', quarter=q + 1, width_m=round(w * 82.3 / W, 3), emit=round(e, 3), pts=[[round(x, 2), round(y, 2)] for x, y in pts]))
dais, W, H = extract(os.path.join(TEX, 'Crucible013', 'T_Floor_Dais_Emit.jpg'), 0.22, 8)
for P, w, e in dais:
    pts = [((c / W) * 25 - 12.5, (1 - r / H) * 25 - 12.5) for r, c in P]
    res['paths'].append(dict(region='HeartDais', width_m=round(w * 25 / W, 3), emit=round(e, 3), pts=[[round(x, 2), round(y, 2)] for x, y in pts]))
outer, W, H = extract(os.path.join(TEX, 'Crucible009', 'T_Crucible_OuterRock_Emit.png'), 0.30, 16)
def inside_map(x, y):
    if abs(x) <= 121 and abs(y) <= 121: return True                         # rim square + perimeter wall
    for (cx, cy, hx, hy) in ((140, 0, 23, 44), (-140, 0, 23, 44), (0, -140, 44, 23), (0, 140, 30, 30)):   # yards (sealed north smaller)
        if abs(x - cx) <= hx and abs(y - cy) <= hy: return True
    return False
for tx in range(-2, 2):
    for ty in range(-2, 2):
        for P, w, e in outer:
            seg = []; segs = []
            for r, c in P:
                x, y = (tx + c / W) * 170.0, (ty + 1 - r / H) * 170.0
                ok = -245 <= x <= 264 and -245 <= y <= 245 and not inside_map(x, y)
                if ok: seg.append((x, y))
                elif len(seg) >= 2: segs.append(seg); seg = []
                else: seg = []
            if len(seg) >= 2: segs.append(seg)
            for s_ in segs:
                res['paths'].append(dict(region='OuterGround', width_m=round(w * 170 / W, 3), emit=round(e, 3), pts=[[round(x, 2), round(y, 2)] for x, y in s_]))
json.dump(res, open(os.path.join(HERE, 'lava_paths_raw.json'), 'w'))
from collections import Counter
print('LAVA paths', Counter(p['region'] for p in res['paths']), 'points', sum(len(p['pts']) for p in res['paths']))
