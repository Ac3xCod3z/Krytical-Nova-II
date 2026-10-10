"""Build the Krytical Nova swatch: one small texture of named flat colours shared by everything we model
that is not a Sidekick part (pets, props, weapons), the way Synty's packs share one texture.

  run_blender.py make_swatch.py [--out-dir DIR] [--cell-px 4]

Writes KN_Swatch.json (every cell's name, place and colour), T_KN_Swatch.png, T_KN_Swatch_Emission.png
(the glow cells) and T_KN_Swatch_Metallic.png (the metal row) into Assets/_ArtSource/Blender~/Swatch/.
The layout is fixed here, in code, so the same cell always means the same colour; add colours only in
cells that are still free, never by moving one, or every model already mapped to it changes colour.

16 x 16 cells, row 15 at the top:
  15  Colour Bible (Rendering-Bible.md 6b) and paper / black / white
  14  Grey 01-16, dark to light
  13  Fur 01-16, cream to dark brown
  12  Gold, Silver, Bronze, Iron, four shades each
  11..0  one hue per row: Bold 1-6, Soft 1-6, Pastel 1-2, Glow 1-2
"""
import argparse
import colorsys
import json
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers

GRID = 16
BIBLE = [("Ink", "#14122B"), ("Shadow Indigo", "#4C52BF"), ("Sky Blue", "#1F6ECC"), ("Horizon", "#ADE0F2"),
         ("Neon Cyan", "#4FE3F0"), ("Neon Magenta", "#E04FB8"), ("Signal Yellow", "#FFC93C"),
         ("Sunset Coral", "#FF9E66"), ("Concrete", "#8C8A94"), ("Ruin Concrete", "#4A4752"),
         ("Paper", "#F4EEDF"), ("Black", "#000000"), ("White", "#FFFFFF")]
HUES = [("Red", 0), ("Orange", 24), ("Amber", 40), ("Yellow", 54), ("Lime", 84), ("Green", 125), ("Teal", 165),
        ("Cyan", 188), ("Blue", 214), ("Indigo", 244), ("Violet", 274), ("Magenta", 318)]
FUR = ["#FFF6E8", "#E8CFA8", "#C99766", "#9A6238", "#6B3F22", "#3A2416"]
METALS = [("Gold", "#5A3E0C", "#FFD866"), ("Silver", "#4A4E57", "#E8ECF2"), ("Bronze", "#3D2412", "#D99A5B"),
          ("Iron", "#16181C", "#7B808A")]


def from_hex(h):
    return tuple(int(h[i:i + 2], 16) / 255 for i in (1, 3, 5))


def to_hex(c):
    return "#%02X%02X%02X" % tuple(int(round(255 * max(0.0, min(1.0, x)))) for x in c)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ramp(stops, n):
    out = []
    for i in range(n):
        x = i / (n - 1) * (len(stops) - 1)
        k = min(int(x), len(stops) - 2)
        out.append(lerp(stops[k], stops[k + 1], x - k))
    return out


def cells():
    out = []

    def add(name, u, v, rgb, glow=False, metal=False):
        out.append({"name": name, "u": u, "v": v, "colour": to_hex(rgb), "glow": glow, "metal": metal})

    for i, (name, h) in enumerate(BIBLE):
        add(name, i, 15, from_hex(h))
    for i in range(GRID):
        g = 0.04 + 0.96 * i / (GRID - 1)
        add(f"Grey {i + 1:02d}", i, 14, (g, g, g))
    for i, rgb in enumerate(reversed(ramp([from_hex(h) for h in FUR], GRID))):
        add(f"Fur {GRID - i:02d}", GRID - 1 - i, 13, rgb)
    for m, (name, dark, light) in enumerate(METALS):
        for i, rgb in enumerate(ramp([from_hex(dark), from_hex(light)], 4)):
            add(f"{name} {i + 1}", m * 4 + i, 12, rgb, metal=True)
    for row, (name, hue) in enumerate(HUES):
        v = 11 - row
        h = hue / 360
        for i in range(6):
            t = i / 5
            add(f"{name} Bold {i + 1}", i, v, colorsys.hsv_to_rgb(h, 0.92 - 0.2 * t, 0.26 + 0.74 * t))
            add(f"{name} Soft {i + 1}", 6 + i, v, colorsys.hsv_to_rgb(h, 0.5 - 0.18 * t, 0.3 + 0.66 * t))
        add(f"{name} Pastel 1", 12, v, colorsys.hsv_to_rgb(h, 0.24, 1.0))
        add(f"{name} Pastel 2", 13, v, colorsys.hsv_to_rgb(h, 0.11, 1.0))
        add(f"{name} Glow 1", 14, v, colorsys.hsv_to_rgb(h, 0.85, 1.0), glow=True)
        add(f"{name} Glow 2", 15, v, colorsys.hsv_to_rgb(h, 0.5, 1.0), glow=True)
    return out


def write_png(path, cell_px, table, pick):
    size = GRID * cell_px
    px = [0.0, 0.0, 0.0, 1.0] * (size * size)
    for c in table:
        rgb = pick(c)
        if rgb is None:
            continue
        for y in range(c["v"] * cell_px, (c["v"] + 1) * cell_px):
            for x in range(c["u"] * cell_px, (c["u"] + 1) * cell_px):
                i = (y * size + x) * 4
                px[i], px[i + 1], px[i + 2] = rgb
    img = bpy.data.images.new(Path(path).stem, size, size, alpha=False)
    img.pixels = px
    img.filepath_raw = str(path)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir")
    ap.add_argument("--cell-px", type=int, default=4)
    a = ap.parse_args(kn.tool_args())
    out = Path(a.out_dir) if a.out_dir else kn.swatch_dir()
    out.mkdir(parents=True, exist_ok=True)
    table = cells()
    taken = {(c["u"], c["v"]) for c in table}
    assert len(taken) == len(table), "two colours in one cell"
    with open(out / "KN_Swatch.json", "w", encoding="utf-8") as fh:
        json.dump({"_about": "The Krytical Nova swatch. Made by the synty-blender skill's make_swatch.py; change it "
                             "there. v counts from the bottom.", "grid": GRID, "cell_px": a.cell_px, "cells": table},
                  fh, indent=1)
    write_png(out / "T_KN_Swatch.png", a.cell_px, table, lambda c: from_hex(c["colour"]))
    write_png(out / "T_KN_Swatch_Emission.png", a.cell_px, table, lambda c: from_hex(c["colour"]) if c["glow"] else None)
    write_png(out / "T_KN_Swatch_Metallic.png", a.cell_px, table, lambda c: (1.0, 1.0, 1.0) if c["metal"] else None)
    kn.report("KN swatch", {"folder": str(out), "cells_named": len(table), "cells_free": GRID * GRID - len(table),
                            "size_px": GRID * a.cell_px})


main()
