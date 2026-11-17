# Floor paintings for The Crucible (system python + PIL/numpy):  python floors.py <outdir>
# Owner, 16 Nov: "work on not having the floors look so repeatable ... like it used the same texture for the ground as it
# did for the walls." So every big floor gets ONE unique painting that covers it once (no tiling at all): large worn
# flagstones of many sizes, a border course where the floor meets the walls, wear paths, grime, cracks, dirt, and the
# faction's own palette. The arena floor is laid out radially around the dais, with lava cracks running along its joints.
import sys, os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
INK = 24


def lowfreq(h, w, period, seed, octaves=1):
    rs = np.random.RandomState(seed); acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        p = max(4, period / (2 ** o))
        small = (rs.rand(int(h / p) + 3, int(w / p) + 3) * 255).astype(np.uint8)
        acc += amp * np.asarray(Image.fromarray(small).resize((w, h), Image.BICUBIC), np.float32) / 255.0; tot += amp; amp *= 0.5
    return acc / tot


class Floor:
    def __init__(self, w, h, ppm, seed):
        self.w, self.h, self.ppm = w, h, ppm; self.rnd = random.Random(seed); self.seed = seed
        self.tone = Image.new('L', (w, h), INK); self.dt = ImageDraw.Draw(self.tone)
        self.hi = Image.new('L', (w, h), 0); self.dh = ImageDraw.Draw(self.hi)
        self.sh = Image.new('L', (w, h), 0); self.ds = ImageDraw.Draw(self.sh)
        self.hue = Image.new('RGB', (w, h), (128, 128, 128)); self.dc = ImageDraw.Draw(self.hue)
        self.ink = Image.new('L', (w, h), 0); self.di = ImageDraw.Draw(self.ink)
        self.mul = np.ones((h, w), np.float32)

    def stone(self, pts, tone=None, spread=0.075, base=0.66):
        r = self.rnd
        t = base + r.gauss(0, spread) if tone is None else tone
        u = r.random()
        if tone is None and u < 0.07: t -= 0.16              # a replaced / stained stone
        elif tone is None and u > 0.95: t += 0.10
        t = max(0.25, min(0.95, t))
        self.dt.polygon(pts, fill=int(t * 255))
        self.dc.polygon(pts, fill=(128 + r.randint(-9, 9), 128 + r.randint(-6, 6), 128 + r.randint(-9, 9)))
        cx = sum(p[0] for p in pts) / len(pts); cy = sum(p[1] for p in pts) / len(pts)
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            mx, my = (a[0] + b[0]) / 2 - cx, (a[1] + b[1]) / 2 - cy; l = math.hypot(mx, my) or 1
            d = (mx / l) * -0.7 + (my / l) * 0.7             # light from the top-left of the finished (flipped) picture
            def inn(p, k=3.0): return (p[0] + (cx - p[0]) / max(1, math.hypot(cx - p[0], cy - p[1])) * k, p[1] + (cy - p[1]) / max(1, math.hypot(cx - p[0], cy - p[1])) * k)
            if d > 0.3: self.dh.line([inn(a), inn(b)], fill=255, width=3)
            elif d < -0.3: self.ds.line([inn(a), inn(b)], fill=255, width=4)
        if r.random() < 0.10:                                # a cracked stone
            self.crack((cx + r.uniform(-20, 20), cy + r.uniform(-20, 20)), r.uniform(0, 6.28), r.randint(3, 6), 2)
        if r.random() < 0.12:                                # chipped corner
            p = r.choice(pts); s = r.uniform(8, 22)
            self.di.polygon([p, (p[0] + (cx - p[0]) * s / 60, p[1]), (p[0], p[1] + (cy - p[1]) * s / 60)], fill=255)

    def crack(self, p, a, n, w=2, step=(14, 34)):
        pts = [p]; x, y = p
        for _ in range(n):
            a += self.rnd.uniform(-0.7, 0.7); x += math.cos(a) * self.rnd.uniform(*step); y += math.sin(a) * self.rnd.uniform(*step); pts.append((x, y))
        self.di.line(pts, fill=255, width=w)

    def rect_layout(self, x0, y0, x1, y1, smin, smax, joint=0.05, jit=0.035):
        """Guillotine-split a metre rectangle into flagstones of many sizes; return jittered quads in px."""
        r = self.rnd; rects = []
        def split(a, b, c, d):
            w, h = c - a, d - b
            if (w <= smax and h <= smax and r.random() < 0.55) or (w <= smin * 1.6 and h <= smin * 1.6): rects.append((a, b, c, d)); return
            if w > h * r.uniform(0.75, 1.3) and w > smin * 1.6:
                m = a + w * r.uniform(0.3, 0.7); split(a, b, m, d); split(m, b, c, d)
            elif h > smin * 1.6:
                m = b + h * r.uniform(0.3, 0.7); split(a, b, c, m); split(a, m, c, d)
            else: rects.append((a, b, c, d))
        split(x0, y0, x1, y1)
        P = self.ppm; j = joint / 2
        for (a, b, c, d) in rects:
            q = [(a + j, b + j), (c - j, b + j), (c - j, d - j), (a + j, d - j)]
            yield [((x + r.uniform(-jit, jit)) * P[0], (y + r.uniform(-jit, jit)) * P[1]) for x, y in q]

    def blotch(self, x, y, rx, ry, k):
        """Soft dirt / wear patch (px units), multiplies the tone by k at its centre."""
        x0, y0, x1, y1 = int(max(0, x - rx * 2)), int(max(0, y - ry * 2)), int(min(self.w, x + rx * 2)), int(min(self.h, y + ry * 2))
        if x1 <= x0 or y1 <= y0: return
        yy, xx = np.mgrid[y0:y1, x0:x1]
        g = np.exp(-(((xx - x) / rx) ** 2 + ((yy - y) / ry) ** 2))
        self.mul[y0:y1, x0:x1] *= 1 + (k - 1) * g

    def finish(self, colour, name, wear_period_m=11.0, wear=0.26, extra=None, quality=91):
        g = np.asarray(self.tone, np.float32) / 255
        hi = np.asarray(self.hi.filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255; sh = np.asarray(self.sh.filter(ImageFilter.GaussianBlur(1.5)), np.float32) / 255
        stone = g > (INK / 255 + 0.04)
        g = g + (hi * 0.075 - sh * 0.11) * stone
        pp = (self.ppm[0] + self.ppm[1]) / 2
        g *= 1 - wear / 2 + wear * lowfreq(self.h, self.w, wear_period_m * pp, self.seed + 1, 3)
        g *= self.mul
        g += (lowfreq(self.h, self.w, 0.35 * pp, self.seed + 2) - 0.5) * 0.07 * stone + (np.random.RandomState(self.seed).rand(self.h, self.w).astype(np.float32) - 0.5) * 0.03 * stone
        k = np.asarray(self.ink, np.float32) / 255
        g = g * (1 - k * 0.8)
        hue = (np.asarray(self.hue, np.float32) - 128) / 255 * 0.5
        rgb = np.clip(g[..., None] / 0.66 * np.array(colour, np.float32)[None, None, :] / 255 + hue * g[..., None], 0, 1)
        if extra is not None: rgb = extra(rgb)
        Image.fromarray(np.flipud((np.clip(rgb, 0, 1) * 255).astype(np.uint8))).save(os.path.join(out, name), quality=quality)
        print(name, self.w, self.h)


def yard(F, colour, seed):
    """42 x 80 m muster yard in yard-local metres: x 118 (gate wall) .. 160 (rear wall), y -40 .. 40. u = (x-118)/42, v = (y+40)/80."""
    W, H = 2048, 4096; f = Floor(W, H, (W / 42.0, H / 80.0), seed); r = f.rnd
    mx = lambda x: (x - 118) * f.ppm[0]; my = lambda y: (y + 40) * f.ppm[1]
    def put(x0, y0, x1, y1, smin, smax, **kw):
        for q in f.rect_layout(x0 - 118, y0 + 40, x1 - 118, y1 + 40, smin, smax): f.stone(q, **kw)
    B = 1.5
    # border course where the floor meets the four walls: long kerb stones, a shade darker
    put(118, -40, 160, -40 + B, 1.2, 4.6, base=0.56); put(118, 40 - B, 160, 40, 1.2, 4.6, base=0.56)
    put(118, -40 + B, 118 + B, 40 - B, 1.2, 4.6, base=0.56); put(160 - B, -40 + B, 160, 40 - B, 1.2, 4.6, base=0.56)
    # the processional lane from the shrine to the gate: bigger, paler, smoother slabs, edged by a kerb line
    put(118 + B, -6.5, 146.5, 6.5, 2.2, 4.8, base=0.72, spread=0.045)
    put(118 + B, -7.6, 146.5, -6.5, 0.9, 3.2, base=0.54); put(118 + B, 6.5, 146.5, 7.6, 0.9, 3.2, base=0.54)
    # shrine apron
    put(146.5, -9.5, 160 - B, 9.5, 1.6, 3.6, base=0.70, spread=0.05)
    # the two muster fields
    put(118 + B, -40 + B, 146.5, -7.6, 1.3, 4.2); put(118 + B, 7.6, 146.5, 40 - B, 1.3, 4.2)
    put(146.5, -40 + B, 160 - B, -9.5, 1.3, 4.2); put(146.5, 9.5, 160 - B, 40 - B, 1.3, 4.2)
    # grime against the walls, wear on the lane and at the gate, dirt and scuffs everywhere else
    for i in range(140):
        t = r.random(); e = r.choice('NSEW'); d = abs(r.gauss(0, 1.3))
        x, y = ((118 + 42 * t, 40 - d) if e == 'N' else (118 + 42 * t, -40 + d) if e == 'S' else (160 - d, -40 + 80 * t) if e == 'E' else (118 + d, -40 + 80 * t))
        f.blotch(mx(x), my(y), r.uniform(40, 130), r.uniform(40, 130), r.uniform(0.62, 0.85))
    for i in range(70): f.blotch(mx(r.uniform(119, 159)), my(r.uniform(-39, 39)), r.uniform(30, 150), r.uniform(30, 150), r.uniform(0.74, 0.93))
    for i in range(26): f.blotch(mx(r.uniform(119, 146)), my(r.gauss(0, 3.0)), r.uniform(60, 170), r.uniform(50, 110), r.uniform(1.04, 1.13))
    for i in range(10): f.blotch(mx(r.uniform(118, 123)), my(r.gauss(0, 4.0)), r.uniform(50, 120), r.uniform(60, 140), r.uniform(0.7, 0.85))     # gate threshold scuffed dark
    for i in range(46): f.crack((r.uniform(0, W), r.uniform(0, H)), r.uniform(0, 6.28), r.randint(4, 12), r.choice((2, 2, 3)))
    for i in range(240):                                                                                                                        # grit
        x, y = r.uniform(0, W), r.uniform(0, H); s = r.uniform(1.5, 4.5); f.di.ellipse([x - s, y - s, x + s, y + s], fill=r.randint(120, 255))
    emit = None
    def extra(rgb):
        nonlocal emit
        if F == 'Covenant':                                   # gold inlay: rays from the shrine to the gate and a ring round the shrine (concept 03)
            lay = Image.new('L', (W, H), 0); d = ImageDraw.Draw(lay); edge = Image.new('L', (W, H), 0); de = ImageDraw.Draw(edge)
            def ln(p, q, w=0.26):
                de.line([(mx(p[0]), my(p[1])), (mx(q[0]), my(q[1]))], fill=255, width=int(w * f.ppm[0]) + 6); d.line([(mx(p[0]), my(p[1])), (mx(q[0]), my(q[1]))], fill=255, width=int(w * f.ppm[0]))
            for yy in (-1.5, 1.5): ln((143.0, yy), (119.6, yy))
            for ang in (150, 165, 195, 210, 125, 235):
                a = math.radians(ang); ln((152 + 9.5 * math.cos(a), 9.5 * math.sin(a)), (152 + 30 * math.cos(a), max(-38, min(38, 30 * math.sin(a)))), 0.2)
            for rr, w in ((9.5, 0.3), (12.0, 0.16)):
                box = [mx(152 - rr), my(-rr), mx(152 + rr), my(rr)]
                de.ellipse(box, outline=255, width=int(w * f.ppm[0]) + 6); d.ellipse(box, outline=255, width=int(w * f.ppm[0]))
            L = np.asarray(lay, np.float32)[..., None] / 255; E = np.asarray(edge, np.float32)[..., None] / 255
            gold = np.array((0.80, 0.56, 0.16), np.float32) * (0.75 + 0.5 * lowfreq(H, W, 60, 9)[..., None])
            rgb = rgb * (1 - E * 0.75); rgb = rgb * (1 - L) + gold * L
        if F == 'Null-lava-removed-by-owner':                 # (owner 16 Nov: no lava cracks in Null's Rally Point) red fissures crawling out from the shrine pyre (concept 04) + their emission map
            lay = Image.new('L', (W, H), 0); d = ImageDraw.Draw(lay); rr = random.Random(seed + 5)
            def walk(x, y, a, n, w, depth=0):
                pts = [(mx(x), my(y))]
                for i in range(n):
                    a += rr.uniform(-0.6, 0.6); x += math.cos(a) * rr.uniform(0.8, 2.0); y += math.sin(a) * rr.uniform(0.8, 2.0); pts.append((mx(x), my(y)))
                    if depth < 2 and rr.random() < 0.2: walk(x, y, a + rr.choice((-1, 1)) * rr.uniform(0.5, 1.1), n // 2, max(2, w - 2), depth + 1)
                d.line(pts, fill=255, width=w, joint='curve')
            for k in range(9): walk(152 + 6 * math.cos(k * 0.7 + 1.9), 6 * math.sin(k * 0.7 + 1.9), k * 0.7 + 1.9 + rr.uniform(-0.3, 0.3), rr.randint(8, 18), 6)
            for k in range(7): walk(rr.uniform(121, 158), rr.uniform(-37, 37), rr.uniform(0, 6.28), rr.randint(4, 9), 4)
            core = np.asarray(lay, np.float32) / 255; glow = np.asarray(lay.filter(ImageFilter.GaussianBlur(9)), np.float32) / 255
            rgb = rgb * (1 - core[..., None] * 0.9); rgb[..., 0] += glow * 0.22
            e = np.clip(core + glow * 0.7, 0, 1)
            emit = np.stack([e, e * 0.13, e * 0.05], -1)
        return rgb
    f.finish(colour, 'T_Floor_Yard_%s.jpg' % F, extra=extra)
    if emit is not None:
        Image.fromarray(np.flipud((emit * 255).astype(np.uint8))).resize((1024, 2048), Image.LANCZOS).save(os.path.join(out, 'T_Floor_Yard_%s_Emit.jpg' % F), quality=90)


def arena():
    """Quarter of the 72.3 m arena disc (local x, y >= 0, centre at 0,0), repeated four times by rotation. Radial flagstones."""
    N = 4096; R = 82.3; P = N / R; f = Floor(N, N, (P, P), 77); r = f.rnd
    rings = [12.2]
    while rings[-1] < 84: rings.append(rings[-1] + r.uniform(2.8, 5.6))
    joints = []                                                # per ring: list of joint angles (0 .. pi/2)
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]; rm = (r0 + r1) / 2; n = max(3, int(round((math.pi / 2 * rm) / r.uniform(3.4, 6.6))))
        js = [0.0] + sorted((i + r.uniform(-0.28, 0.28)) * (math.pi / 2) / n for i in range(1, n)) + [math.pi / 2]
        joints.append(js); jw = 0.055
        for i in range(len(js) - 1):
            a0, a1 = js[i] + jw / rm / 2, js[i + 1] - jw / rm / 2
            radial = [(r0 + jw / 2, r1 - jw / 2)]
            if (r1 - r0) > 4.0 and r.random() < 0.22:         # some courses split in two
                m = r0 + (r1 - r0) * r.uniform(0.4, 0.6); radial = [(r0 + jw / 2, m - jw / 2), (m + jw / 2, r1 - jw / 2)]
            for (ra, rb) in radial:
                steps = max(2, int((a1 - a0) * rm / 0.7))
                pts = [((ra + r.uniform(-0.02, 0.02)) * math.cos(a0 + (a1 - a0) * s / steps) * P, (ra) * math.sin(a0 + (a1 - a0) * s / steps) * P) for s in range(steps + 1)]
                pts += [((rb) * math.cos(a1 - (a1 - a0) * s / steps) * P, (rb) * math.sin(a1 - (a1 - a0) * s / steps) * P) for s in range(steps + 1)]
                f.stone(pts, base=0.64 - 0.10 * math.exp(-(rm - 12) / 14.0))          # sootier near the dais
    # the paths (overhead ref): big pale slabs in a straight band from each grand stair foot to the dais; the quarter's two edges are the axes
    for (bx0, by0, bx1, by1) in ((13.2, 0.0, 82.3, 4.3), (0.0, 13.2, 4.3, 82.3)):
        box = [bx0 * P, by0 * P, bx1 * P, by1 * P]
        f.dt.rectangle(box, fill=INK); f.dh.rectangle(box, fill=0); f.ds.rectangle(box, fill=0); f.di.rectangle(box, fill=0)
        for q in f.rect_layout(bx0, by0, bx1, by1, 1.7, 4.2): f.stone(q, base=0.77, spread=0.04)
    # wear: the eight stair routes arrive along the axes (0, 90 deg) and the diagonal (45 deg): paler, polished paths
    for i in range(90):
        a = r.choice((0.0, math.pi / 2)); d = r.uniform(14, 82); o = r.gauss(0, 2.2)
        x, y = d * math.cos(a) - o * math.sin(a), d * math.sin(a) + o * math.cos(a)
        f.blotch(x * P, y * P, r.uniform(80, 220), r.uniform(80, 220), r.uniform(1.04, 1.12))
    for i in range(160): f.blotch(r.uniform(0, N), r.uniform(0, N), r.uniform(40, 260), r.uniform(40, 260), r.uniform(0.70, 0.93))
    for i in range(70): f.crack((r.uniform(0, N), r.uniform(0, N)), r.uniform(0, 6.28), r.randint(4, 12), r.choice((2, 3)))
    for i in range(500):
        x, y = r.uniform(0, N), r.uniform(0, N); s = r.uniform(1.5, 5); f.di.ellipse([x - s, y - s, x + s, y + s], fill=r.randint(120, 255))
    # lava: fissures that follow the joints outward from the heart, branching, dying out toward the tiers (concept 01)
    lay = Image.new('L', (N, N), 0); d = ImageDraw.Draw(lay); rr = random.Random(5)
    def pol(rad, a): return (rad * math.cos(a) * P, rad * math.sin(a) * P)
    def arc(k, a, b_, w):
        steps = max(2, int(abs(b_ - a) * rings[k] / 0.6))
        d.line([pol(rings[k] + rr.uniform(-0.06, 0.06), a + (b_ - a) * s / steps) for s in range(steps + 1)], fill=255, width=w, joint='curve')
    def run(k, a, w, life, drift):
        """Walk outward ring by ring: slide along the ring joint (often a long way), then cross one course through a radial joint."""
        while 0 <= k < len(joints) and life > 0 and w >= 3:
            drift = max(-0.30, min(0.30, drift * 0.6 + rr.uniform(-0.22, 0.22)))            # persistent sideways wander
            want = max(0.03, min(math.pi / 2 - 0.03, a + drift))
            nxt = min(joints[k], key=lambda j: abs(j - want))
            arc(k, a, nxt, w); a = nxt
            if rr.random() < 0.16:                                                          # a side run along the ring that dies out
                arc(k, a, max(0.03, min(math.pi / 2 - 0.03, a + rr.choice((-1, 1)) * rr.uniform(0.10, 0.34))), max(3, w - 4))
            mid = pol((rings[k] + rings[k + 1]) / 2 + rr.uniform(-0.4, 0.4), a + rr.uniform(-0.012, 0.012))
            d.line([pol(rings[k], a), mid, pol(rings[k + 1], a)], fill=255, width=w, joint='curve')
            if rr.random() < 0.30 and w > 4: run(k + 1, a, w - rr.choice((2, 3, 4)), life - rr.randint(1, 3), rr.choice((-1, 1)) * rr.uniform(0.12, 0.30))
            k += 1; life -= 1
            if rr.random() < 0.22: w -= 2
    for a0 in (0.30, 0.74, 1.12, 1.42): run(0, a0 + rr.uniform(-0.08, 0.08), rr.choice((7, 8, 9)), rr.randint(3, 6), rr.uniform(-0.2, 0.2))
    for i in range(3):                                                                      # short isolated cracks further out
        run(rr.randint(3, len(joints) - 3), rr.uniform(0.05, 1.5), rr.choice((4, 5, 6)), rr.randint(1, 3), rr.uniform(-0.3, 0.3))
    core = np.asarray(lay, np.float32) / 255; glow = np.asarray(lay.filter(ImageFilter.GaussianBlur(14)), np.float32) / 255
    wide = np.asarray(lay.filter(ImageFilter.GaussianBlur(46)), np.float32) / 255
    def extra(rgb):
        rgb = rgb * (1 - core[..., None] * 0.92)
        rgb[..., 0] += glow * 0.30 + wide * 0.22; rgb[..., 1] += glow * 0.03
        return rgb
    f.finish((150, 134, 138), 'T_Floor_Arena_Quarter.jpg', extra=extra)
    e = np.clip(core * 1.0 + glow * 0.85, 0, 1)
    Image.fromarray(np.flipud((np.stack([e, e * 0.07, e * 0.04], -1) * 255).astype(np.uint8))).resize((2048, 2048), Image.LANCZOS).save(os.path.join(out, 'T_Floor_Arena_Quarter_Emit.jpg'), quality=90)
    print('arena emit')


def rim():
    """Quarter of the rim square (local 0..118), repeated four times. Only the part outside the bowl (r > 104) shows."""
    N = 4096; P = N / 118.0; f = Floor(N, N, (P, P), 31); r = f.rnd
    # kerb ring round the lip of the bowl: the round shape must read from above
    r0, r1 = 103.6, 106.4; n = 44
    for i in range(n):
        a0, a1 = (math.pi / 2) * i / n + 0.0006, (math.pi / 2) * (i + 1) / n - 0.0006
        pts = [(r0 * math.cos(a) * P, r0 * math.sin(a) * P) for a in (a0, (a0 + a1) / 2, a1)] + [(r1 * math.cos(a) * P, r1 * math.sin(a) * P) for a in (a1, (a0 + a1) / 2, a0)]
        f.stone(pts, base=0.80, spread=0.04)
    # paving: only cells that reach outside the kerb are drawn (the inside is under the bowl)
    for q in f.rect_layout(0, 0, 118, 118, 1.8, 5.2):
        cx = sum(p[0] for p in q) / 4 / P; cy = sum(p[1] for p in q) / 4 / P
        if math.hypot(cx, cy) > 106.4 + 1.2: f.stone(q, base=0.60)
        elif math.hypot(cx, cy) > 106.4 - 2.0:
            f.stone([p for p in q], base=0.60)
    # redraw the kerb over the paving cells that crossed it
    for i in range(n):
        a0, a1 = (math.pi / 2) * i / n + 0.0006, (math.pi / 2) * (i + 1) / n - 0.0006
        box = [(r0 * math.cos(a) * P, r0 * math.sin(a) * P) for a in (a0, (a0 + a1) / 2, a1)] + [(r1 * math.cos(a) * P, r1 * math.sin(a) * P) for a in (a1, (a0 + a1) / 2, a0)]
        f.dt.polygon([(p[0] * 1.0, p[1] * 1.0) for p in box], fill=INK); f.stone([(cx_ * 0.999, cy_ * 0.999) for cx_, cy_ in box], base=0.80, spread=0.04)
    for i in range(260): f.blotch(r.uniform(0, N), r.uniform(0, N), r.uniform(60, 320), r.uniform(60, 320), r.uniform(0.62, 0.92))
    for i in range(60): f.blotch(r.uniform(0.8, 1.0) * N, r.uniform(0, N), r.uniform(60, 160), r.uniform(60, 160), r.uniform(0.6, 0.8)); f.blotch(r.uniform(0, N), r.uniform(0.8, 1.0) * N, r.uniform(60, 160), r.uniform(60, 160), r.uniform(0.6, 0.8))
    for i in range(120): f.crack((r.uniform(0, N), r.uniform(0, N)), r.uniform(0, 6.28), r.randint(4, 14), r.choice((2, 3)))
    f.finish((84, 74, 106), 'T_Floor_Rim_Quarter.jpg', wear=0.34)


def paving():
    """16 m tile of varied paving for the smaller walkable tops (tier treads, stair treads, wall-walks, shrine steps). Wraps."""
    N = 2048; P = N / 16.0; f = Floor(N, N, (P, P), 13); r = f.rnd
    for q in f.rect_layout(0, 0, 16, 16, 0.9, 2.6): f.stone(q, base=0.70)
    for i in range(50):
        x, y = r.uniform(0, N), r.uniform(0, N); rx, ry, k = r.uniform(40, 160), r.uniform(40, 160), r.uniform(0.78, 0.95)
        for ox in (-N, 0, N):
            for oy in (-N, 0, N): f.blotch(x + ox, y + oy, rx, ry, k)
    for i in range(22): f.crack((r.uniform(100, N - 100), r.uniform(100, N - 100)), r.uniform(0, 6.28), r.randint(3, 8), 2)
    f.finish((200, 200, 204), 'T_Crucible_Paving.jpg', wear=0.0)


which = sys.argv[2:] or ['yards', 'arena', 'rim', 'paving']
if 'yards' in which:
    yard('Hegemony', (150, 156, 178), 101); yard('Covenant', (138, 110, 90), 202); yard('Null', (86, 68, 78), 303)
if 'arena' in which: arena()
if 'rim' in which: rim()
if 'paving' in which: paving()


def dais():
    """The 25 m heart dais seen from above (ref 01): radial wedge flagstones in rings that follow its real steps (r 12.5 / 11.3 / 10.1 / 8.9),
    a darker inner ring, and lava fissures branching from the centre across the whole dais. u = (x+12.5)/25, v = (y+12.5)/25."""
    N = 2048; P = N / 25.0; C = N / 2; f = Floor(N, N, (P, P), 91); r = f.rnd
    rings = [0.0, 1.5, 3.4, 5.4, 6.9, 8.9, 10.1, 11.3, 12.5]
    base = [0.50, 0.56, 0.58, 0.50, 0.70, 0.62, 0.66, 0.72]
    joints = []
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]; rm = (r0 + r1) / 2; n = 6 if k == 0 else max(8, int(round(2 * math.pi * rm / r.uniform(2.0, 3.0))))
        off = r.uniform(0, 6.28); js = [off + 2 * math.pi * (i + r.uniform(-0.18, 0.18)) / n for i in range(n)]; joints.append(js); jw = 0.07
        for i in range(n):
            a0, a1 = js[i] + jw / max(rm, 0.6) / 2, js[(i + 1) % n] + (2 * math.pi if i == n - 1 else 0) - jw / max(rm, 0.6) / 2
            steps = max(2, int((a1 - a0) * rm / 0.5)); ra, rb = r0 + (jw / 2 if k else 0.0), r1 - jw / 2
            pts = ([(C, C)] if k == 0 else [(C + ra * math.cos(a0 + (a1 - a0) * s / steps) * P, C + ra * math.sin(a0 + (a1 - a0) * s / steps) * P) for s in range(steps + 1)])
            pts += [(C + rb * math.cos(a1 - (a1 - a0) * s / steps) * P, C + rb * math.sin(a1 - (a1 - a0) * s / steps) * P) for s in range(steps + 1)]
            f.stone(pts, base=base[k], spread=0.05)
    for i in range(70): f.blotch(r.uniform(0, N), r.uniform(0, N), r.uniform(40, 200), r.uniform(40, 200), r.uniform(0.72, 0.94))
    for i in range(30): f.crack((r.uniform(0, N), r.uniform(0, N)), r.uniform(0, 6.28), r.randint(4, 10), 2)
    lay = Image.new('L', (N, N), 0); d = ImageDraw.Draw(lay); rr = random.Random(17)
    def pol(rad, a): return (C + rad * math.cos(a) * P, C + rad * math.sin(a) * P)
    def near(js, a): return min(js, key=lambda j: abs(((j - a + math.pi) % (2 * math.pi)) - math.pi))
    def arc(rad, a, b_, w):
        dlt = ((b_ - a + math.pi) % (2 * math.pi)) - math.pi; steps = max(2, int(abs(dlt) * max(rad, 1) / 0.4))
        d.line([pol(rad + rr.uniform(-0.04, 0.04), a + dlt * s / steps) for s in range(steps + 1)], fill=255, width=w, joint='curve')
        return a + dlt
    def run(k, a, w, drift):
        while k < len(joints) and w >= 3:
            drift = max(-0.5, min(0.5, drift * 0.5 + rr.uniform(-0.35, 0.35)))
            a = arc(rings[k], a, near(joints[k], a + drift), w)
            d.line([pol(rings[k], a), pol((rings[k] + rings[k + 1]) / 2 + rr.uniform(-0.2, 0.2), a + rr.uniform(-0.02, 0.02)), pol(rings[k + 1], a)], fill=255, width=w, joint='curve')
            if rr.random() < 0.34 and w > 5: run(k + 1, a, w - rr.choice((3, 4)), rr.choice((-1, 1)) * rr.uniform(0.2, 0.5))
            k += 1
            if rr.random() < 0.3: w -= 2
    for i in range(6): d.line([pol(0, 0), pol(rings[1], joints[0][i])], fill=255, width=12)
    for i in (0, 1, 2, 3, 4, 5): run(1, joints[0][i], rr.choice((10, 12, 13)), rr.uniform(-0.3, 0.3))
    core = np.asarray(lay, np.float32) / 255; glow = np.asarray(lay.filter(ImageFilter.GaussianBlur(11)), np.float32) / 255; wide = np.asarray(lay.filter(ImageFilter.GaussianBlur(40)), np.float32) / 255
    def extra(rgb):
        rgb = rgb * (1 - core[..., None] * 0.92); rgb[..., 0] += glow * 0.32 + wide * 0.26; rgb[..., 1] += glow * 0.03
        return rgb
    f.finish((100, 86, 122), 'T_Floor_Dais.jpg', extra=extra, wear=0.2)
    e = np.clip(core + glow * 0.9, 0, 1)
    Image.fromarray(np.flipud((np.stack([e, e * 0.07, e * 0.04], -1) * 255).astype(np.uint8))).resize((1024, 1024), Image.LANCZOS).save(os.path.join(out, 'T_Floor_Dais_Emit.jpg'), quality=90)
    print('dais emit')


if 'dais' in sys.argv[2:] or len(sys.argv) <= 2: dais()
