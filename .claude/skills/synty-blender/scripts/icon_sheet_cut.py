"""Cuts a generated icon sheet (pieces on a white ground, in rows) into one transparent PNG per piece, numbered for
the icon folder: python icon_sheet_cut.py <sheet> <out folder> <prefix> <Name1,Name2,...> [first number] [rows].
Enclosed white holes bigger than a highlight (a trigger guard, a cord loop) are cut back out. PIL only."""
import sys
from PIL import Image, ImageChops, ImageFilter, ImageDraw
src, out, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
names = sys.argv[4].split(',')
start = int(sys.argv[5]) if len(sys.argv) > 5 else 1
rows_wanted = int(sys.argv[6]) if len(sys.argv) > 6 else 2
im = Image.open(src).convert('RGB')
W, H = im.size
r, g, b = im.split()
mn = ImageChops.darker(ImageChops.darker(r, g), b)
dist = ImageChops.invert(mn)                      # 0 on white
mask = dist.point(lambda v: 255 if v > 18 else 0)
def profile(img, axis):
    p = img.resize((1, img.height) if axis == 1 else (img.width, 1), Image.BOX)
    return list(p.get_flattened_data() if hasattr(p, "get_flattened_data") else p.getdata())
def runs(vals, minlen, gap, thr=2):
    on = [v > thr for v in vals]; segs = []; i = 0; n = len(on)
    while i < n:
        if on[i]:
            j = i; last = i
            while j < n:
                if on[j]: last = j
                elif j - last > gap: break
                j += 1
            if last + 1 - i >= minlen: segs.append((i, last + 1))
            i = last + 1
        else: i += 1
    return segs
rows = runs(profile(mask, 1), 150, 40)
print('rows', rows)
boxes = []
for (y0, y1) in rows[:rows_wanted]:
    band = mask.crop((0, y0, W, y1))
    for (x0, x1) in runs(profile(band, 0), 120, 60, thr=1):
        bb = mask.crop((x0, y0, x1, y1)).getbbox()
        boxes.append((x0 + bb[0], y0 + bb[1], x0 + bb[2], y0 + bb[3]))
print(len(boxes), boxes)
for k, (x0, y0, x1, y1) in enumerate(boxes[:len(names)]):
    m = 12; box = (max(0, x0 - m), max(0, y0 - m), min(W, x1 + m), min(H, y1 + m))
    crop = im.crop(box); d = dist.crop(box)
    soft = d.point(lambda v: max(0, min(255, int((v - 6) * 255 / 20))))
    solid = d.point(lambda v: 255 if v > 14 else 0).filter(ImageFilter.MaxFilter(5))
    ImageDraw.floodfill(solid, (0, 0), 128)
    inside = solid.point(lambda v: 0 if v == 128 else 255)   # what the outside flood did not reach
    # Holes through the piece (a trigger guard) are background, not paint: enclosed pure-white patches bigger than
    # a highlight are cut back out.
    white = d.point(lambda v: 255 if v < 6 else 0)
    lab = white.copy(); px = lab.load(); wpx = white.load(); w_, h_ = lab.size; step = 6
    holes = Image.new('L', lab.size, 0)
    for yy in range(0, h_, step):
        for xx in range(0, w_, step):
            if px[xx, yy] != 255: continue
            ImageDraw.floodfill(lab, (xx, yy), 100)
            region = lab.point(lambda v: 255 if v == 100 else 0)
            area = region.histogram()[255]
            touches_edge = region.crop((0, 0, w_, 1)).getbbox() or region.crop((0, h_ - 1, w_, h_)).getbbox()                 or region.crop((0, 0, 1, h_)).getbbox() or region.crop((w_ - 1, 0, w_, h_)).getbbox()
            if not touches_edge and area > w_ * h_ * 0.002: holes = ImageChops.lighter(holes, region)
            lab.paste(50, mask=region)
    inside = ImageChops.subtract(inside, holes.filter(ImageFilter.MaxFilter(3)))
    alpha = ImageChops.lighter(soft, inside).filter(ImageFilter.GaussianBlur(0.8))
    piece = crop.convert('RGBA'); piece.putalpha(alpha)
    s = max(piece.size); pad = int(s * 0.06)
    c = Image.new('RGBA', (s + 2 * pad, s + 2 * pad), (0, 0, 0, 0))
    c.alpha_composite(piece, ((c.width - piece.width) // 2, (c.height - piece.height) // 2))
    c.resize((1024, 1024), Image.LANCZOS).save(f"{out}/{prefix}_{names[k]}_{start + k:02d}.png")
print('saved')
