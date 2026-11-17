# Checkpoint 022 (owner: "our materials are using normal and height maps so we can have variance in the ground ... rocks walls
# pillars, pretty much all the rock like texture materials"). System Python (numpy, scipy, PIL), not Blender.
# For every stone texture the map uses: an 8K normal map (OpenGL / Y+, what Unity and Blender read) and an 8K height map,
# plus 2K copies of both that the Blender preview loads (keeps RAM lean). The 8K files are the ones Unity imports.
# Height = albedo luminance, high-passed so tint and painted light never become slopes ("no lighting baked in"); glowing
# lava cracks (the _Emit map) are pressed DOWN into the stone. Synty's own normal map is used where the pack ships one.
#   python relief_maps.py            (skips maps that already exist)
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage as nd
Image.MAX_IMAGE_PIXELS = None
HERE = os.path.dirname(os.path.abspath(__file__)); TEX = os.path.join(os.path.dirname(HERE), 'Textures')
SYN = r"C:\Users\dlaco\Krytical Nova II\Assets\Synty"
OUT = os.path.join(TEX, 'Crucible022'); os.makedirs(OUT, exist_ok=True)
DFE = os.path.join(SYN, 'PolygonDarkFortress', 'Textures', 'Env')
C13 = os.path.join(TEX, 'Crucible013'); C09 = os.path.join(TEX, 'Crucible009')
# stem: (albedo, synty normal or None, emit or None, relief strength, feature size px at source res)
SRC = {
    'Brick_Large_Texture_01': (os.path.join(DFE, 'Brick_Large_Texture_01.png'), os.path.join(DFE, 'Brick_Large_Normals_01.png'), None, 1.0, 24),
    'Brick_Medium_Texture_01': (os.path.join(DFE, 'Brick_Medium_Texture_01.png'), os.path.join(DFE, 'Brick_Medium_Normals_01.png'), None, 1.0, 20),
    'SmoothStone_Dark_Texture_01': (os.path.join(DFE, 'SmoothStone_Dark_Texture_01.png'), os.path.join(DFE, 'SmoothStone_Normals_01.png'), None, 1.0, 24),
    'SmoothStone_Texture_01': (os.path.join(DFE, 'SmoothStone_Texture_01.png'), os.path.join(DFE, 'SmoothStone_Normals_01.png'), None, 1.0, 24),
    'Rock_Cliff_Texture_01': (os.path.join(DFE, 'Rock_Cliff_Texture_01.png'), os.path.join(DFE, 'Rock_Cliff_Normals_01.png'), None, 1.0, 32),
    'Wall_Brick_01': (os.path.join(SYN, 'PolygonFantasyKingdom', 'Textures', 'Castle', 'Wall_Brick_01.png'), None, None, 3.0, 14),
    'T_Crucible_Paving': (os.path.join(C13, 'T_Crucible_Paving.jpg'), None, None, 3.0, 14),
    'T_Floor_Arena_Quarter': (os.path.join(C13, 'T_Floor_Arena_Quarter.jpg'), None, os.path.join(C13, 'T_Floor_Arena_Quarter_Emit.jpg'), 3.0, 18),
    'T_Floor_Rim_Quarter': (os.path.join(C13, 'T_Floor_Rim_Quarter.jpg'), None, None, 3.0, 18),
    'T_Floor_Dais': (os.path.join(C13, 'T_Floor_Dais.jpg'), None, os.path.join(C13, 'T_Floor_Dais_Emit.jpg'), 3.0, 14),
    'T_Floor_Yard_Hegemony': (os.path.join(C13, 'T_Floor_Yard_Hegemony.jpg'), None, None, 3.0, 14),
    'T_Floor_Yard_Covenant': (os.path.join(C13, 'T_Floor_Yard_Covenant.jpg'), None, None, 3.0, 14),
    'T_Floor_Yard_Null': (os.path.join(C13, 'T_Floor_Yard_Null.jpg'), None, os.path.join(C13, 'T_Floor_Yard_Null_Emit.jpg'), 3.0, 14),
    'T_Crucible_OuterRock': (os.path.join(C09, 'T_Crucible_OuterRock.png'), None, os.path.join(C09, 'T_Crucible_OuterRock_Emit.png'), 3.0, 16),
}
BIG = 8192; SMALL = 2048


def size_for(w, h):
    s = BIG / max(w, h); return int(round(w * s)), int(round(h * s))


def lum(im): a = np.asarray(im.convert('RGB'), dtype=np.float32) / 255.0; return a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11


def height_from(albedo, emit, feat):
    """Height 0..1 at source resolution: luminance minus its broad average (removes tint / painted light), joints and cracks low."""
    L = lum(albedo)
    hp = L - nd.gaussian_filter(L, feat * 4, mode='wrap')
    hp = nd.gaussian_filter(hp, 1.2, mode='wrap')
    if emit is not None:
        E = nd.gaussian_filter(lum(emit.resize(albedo.size, Image.BILINEAR)), 2.0, mode='wrap')
        hp = hp - 0.6 * E / max(1e-3, E.max())
    lo, hi = np.percentile(hp, 1), np.percentile(hp, 99)
    return np.clip((hp - lo) / max(1e-4, hi - lo), 0, 1)


def normal_from(H, strength):
    gx = nd.sobel(H, axis=1, mode='wrap'); gy = nd.sobel(H, axis=0, mode='wrap')
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(H)          # image rows run down: +gy flips for OpenGL Y+
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / n, ny / n, nz / n], -1) * 0.5 + 0.5


def save(arr, path, mode):
    if mode == 'L': Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8), 'L').save(path, optimize=False, compress_level=6)
    else: Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB').save(path, compress_level=6)


def run(stem, force=False):
    alb, nrm, emi, strength, feat = SRC[stem]
    outs = {k: os.path.join(OUT, '%s_%s.png' % (stem, k)) for k in ('Normal_8K', 'Height_8K', 'Normal_2K', 'Height_2K')}
    if not force and all(os.path.exists(p) for p in outs.values()): print('SKIP', stem); return
    A = Image.open(alb); E = Image.open(emi) if emi else None
    H = height_from(A, E, feat)
    W8 = size_for(*A.size); W2 = (W8[0] * SMALL // BIG, W8[1] * SMALL // BIG)
    Hi = Image.fromarray(H.astype(np.float32), 'F')
    H8 = np.asarray(Hi.resize(W8, Image.BICUBIC), dtype=np.float32)
    if nrm:
        N = Image.open(nrm).convert('RGB')
        N8 = np.asarray(N.resize(W8, Image.BICUBIC), dtype=np.float32) / 255.0
    else:
        N8 = normal_from(H8, strength * 2.0 * (W8[0] / A.size[0]))
    save(N8, outs['Normal_8K'], 'RGB'); save(H8, outs['Height_8K'], 'L')
    Image.open(outs['Normal_8K']).resize(W2, Image.LANCZOS).save(outs['Normal_2K'])
    Image.open(outs['Height_8K']).resize(W2, Image.LANCZOS).save(outs['Height_2K'])
    print('MAPS', stem, A.size, '->', W8, 'synty normal' if nrm else 'normal from height', round(os.path.getsize(outs['Normal_8K']) / 1e6, 1), 'MB')


if __name__ == '__main__':
    only = sys.argv[1:]
    for s in SRC:
        if not only or s in only: run(s, force=bool(only))
