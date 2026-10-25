"""Writes the island numbers onto island_map.py's pictures when Blender's Python had no Pillow (plain Python).
  python label_islands.py <prefix>"""
import glob
import sys
from PIL import Image, ImageDraw

prefix = sys.argv[1]
for lab in glob.glob(prefix + "_*_labels.txt"):
    png = lab.replace("_labels.txt", ".png")
    im = Image.open(png).convert("RGB")
    dr = ImageDraw.Draw(im)
    for line in open(lab):
        i, x, y = line.split()
        dr.text((float(x) * im.width, float(y) * im.height), i, fill=(0, 0, 0), stroke_width=2, stroke_fill=(255, 255, 255))
    im.save(png)
    print("labelled", png)
