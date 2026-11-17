# Checkpoint 022b pictures (system Python): LavaMap_022.jpg (lava paths, heat-haze boxes, embers, smoke, every light by mode) and
# Colliders_022.jpg (box colliders, convex hulls, blockers, Wallstride faces) drawn over the orthographic top views.
import os, json, math
from PIL import Image, ImageDraw, ImageFont
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__))); RV = os.path.join(R, 'Review022')
TV = json.load(open(os.path.join(R, 'TOPVIEW_022.json'))); LJ = json.load(open(os.path.join(R, 'LAVA_022.json')))
FX = json.load(open(os.path.join(R, 'FX_022.json'))); CO = json.load(open(os.path.join(R, 'COLLIDERS_022.json'))); WS = json.load(open(os.path.join(R, 'WALLSTRIDE_022.json')))
f = ImageFont.truetype('arialbd.ttf', 30); fs = ImageFont.truetype('arial.ttf', 24)
def u2b(p): return (-p[0], -p[2])                       # Unity (x, y, z) -> Blender (x, y)
def px(tag, x, y):
    v = TV[tag]; h = v['half_extent_m']; n = v['px']
    return ((x + h) / (2 * h) * n, (h - y) / (2 * h) * n)
def base(tag, dim=0.55):
    im = Image.open(os.path.join(RV, tag + '.png')).convert('RGB'); return Image.blend(im, Image.new('RGB', im.size, (0, 0, 0)), 1 - dim)
def legend(d, items, x=30, y=30):
    d.rectangle((x - 14, y - 12, x + 760, y + 46 * len(items) + 8), fill=(10, 10, 18))
    for i, (col, txt) in enumerate(items):
        d.rectangle((x, y + 46 * i + 6, x + 34, y + 46 * i + 34), fill=col); d.text((x + 50, y + 46 * i + 4), txt, fill=(235, 235, 235), font=f)
FIRECOL = {'Realtime': (255, 255, 255), 'Mixed': (80, 220, 255), 'Baked': (255, 150, 40)}
def lava_view(tag, out, title):
    im = base(tag); d = ImageDraw.Draw(im)
    for b in LJ['haze_boxes']:
        x, y = u2b(b['centre_unity']); a = px(tag, x - 8, y + 8); c = px(tag, x + 8, y - 8); d.rectangle((a, c), outline=(255, 210, 60), width=2)
    for p in LJ['paths']:
        pts = [px(tag, *u2b(q)) for q in p['points_unity']]; w = max(2, int(p['width_m'] / (2 * TV[tag]['half_extent_m']) * TV[tag]['px'] * 1.6))
        d.line(pts, fill=(255, 40, 10), width=w)
    for q in LJ['ember_points_unity']:
        x, y = px(tag, *u2b(q)); d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=(255, 240, 120))
    for q in LJ['smoke_points_unity']:
        x, y = px(tag, *u2b(q)); d.ellipse((x - 6, y - 6, x + 6, y + 6), outline=(200, 200, 200), width=2)
    for m in FX['markers']:
        if m['light']['mode'] in ('None',) or m['type'] == 'Directional': continue
        x, y = px(tag, *u2b(m['unity_pos'])); col = FIRECOL.get(m['light']['mode'], (255, 255, 255)); r = 9 if m['light']['mode'] == 'Baked' else 14
        fc = tuple(int(c * 255) for c in m['colour_linear'][:3]); d.ellipse((x - r, y - r, x + r, y + r), fill=fc, outline=col, width=4)
    legend(d, [((255, 40, 10), 'lava crack path (LAVA_022.json)'), ((255, 210, 60), 'heat-haze box, 16 m cell'), ((255, 240, 120), 'ember spawn point'), ((200, 200, 200), 'smoke spawn point (outer flows)'),
               ((255, 255, 255), 'light ring white = Realtime (flicker, no shadow)'), ((80, 220, 255), 'light ring cyan = Mixed (shrines, heart)'), ((255, 150, 40), 'light ring orange = Baked')])
    d.text((30, im.height - 60), title, fill=(255, 220, 120), font=f); im.save(os.path.join(RV, out), quality=88); return im
def coll_view(tag, out, title):
    im = base(tag, 0.5); d = ImageDraw.Draw(im)
    for b in CO['boxes']:
        x, y = u2b(b['centre_unity']); sx, sz = b['size_m_unity'][0], b['size_m_unity'][2]; yaw = -math.radians(b['unity_euler'][1])
        cs = [(x + math.cos(yaw) * dx - math.sin(yaw) * dy, y + math.sin(yaw) * dx + math.cos(yaw) * dy) for dx, dy in ((-sx / 2, -sz / 2), (sx / 2, -sz / 2), (sx / 2, sz / 2), (-sx / 2, sz / 2))]
        col = (90, 200, 255) if b['kind'] == 'wall' else (255, 255, 255) if b['kind'] in ('StairRamp', 'StairLanding') else (120, 255, 140) if b['kind'] == 'TierRing' else (255, 200, 80)
        if b['kind'] in ('StairRamp', 'StairLanding'): d.polygon([px(tag, *c) for c in cs], fill=(255, 255, 255), outline=(255, 255, 255)); continue
        d.polygon([px(tag, *c) for c in cs], outline=col, width=2)
    for b in CO['blockers']:
        x, y = u2b(b['centre_unity']); sx, sz = b['size_m_unity'] if False else (b['size_m'][0], b['size_m'][2]); yaw = -math.radians(b['unity_euler'][1])
        cs = [(x + math.cos(yaw) * dx - math.sin(yaw) * dy, y + math.sin(yaw) * dx + math.cos(yaw) * dy) for dx, dy in ((-sx / 2, -sz / 2), (sx / 2, -sz / 2), (sx / 2, sz / 2), (-sx / 2, sz / 2))]
        d.polygon([px(tag, *c) for c in cs], outline=(255, 50, 60), width=6)
    for w in WS['faces']:
        a, b_ = u2b(w['corners_unity'][0]), u2b(w['corners_unity'][1]); d.line([px(tag, *a), px(tag, *b_)], fill=(255, 0, 255), width=7)
    legend(d, [((90, 200, 255), 'wall box colliders (merged runs)'), ((120, 255, 140), 'tier-ring boxes'), ((255, 255, 255), 'stair ramps + landings (Ground layer, STAIRS_022.json)'), ((255, 200, 80), 'other box colliders (cover, braziers, crates, towers)'),
               ((255, 50, 60), 'invisible blocker, up to 40 m'), ((255, 0, 255), 'Wallstride face (playable side)')])
    d.text((30, im.height - 60), title, fill=(255, 220, 120), font=f); im.save(os.path.join(RV, out), quality=88)
lava_view('top_full', 'LavaMap_022_full.jpg', 'LavaMap 022 - whole map (500 m square). Lava, haze, embers, smoke, every light by bake mode')
lava_view('top_arena', 'LavaMap_022.jpg', 'LavaMap 022 - arena and heart (180 m square)')
coll_view('top_full', 'Colliders_022.jpg', 'Colliders 022 - %d boxes, %d convex hulls, %d ground meshes, %d blockers, %d Wallstride faces' % (len(CO['boxes']), len(CO['convex_hulls']), len(CO['ground_mesh_colliders']), len(CO['blockers']), len(WS['faces'])))
print('overlays ok')
