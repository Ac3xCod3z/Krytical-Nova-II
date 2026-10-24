"""Bake every hair's "under a helmet" look: only what hangs below the helmet's rim is kept (owner, 23 Oct 2026:
helmets are a vanity toggle; a ponytail under one shows only its lower tail, so nothing clips through).

  run_blender.py hair_under_helmet.py
        [--src Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/HairFixed]
        [--out-dir <Drive>/Art/Blender/Sidekick/HairUnderHelmet]
        [--rim 1.659,-0.146,1.522,0.162]   brow z,y and nape z,y of the helmet's rim (Dock Runner pilot cap)
        [--margin 0.01] [--shell <helmet shell .json>]

Each of our fixed hairs (02HAIR, with every body key) is imported with its rig, the faces above a cut surface are
removed (the line from the brow to the nape, `margin` below the rim, across the head), and the rest is exported under
the same name with the proven Sidekick settings, then checked against Synty's original: bind pose and every key.
A hair the helmet hides completely is reported, not exported: under a helmet that hair is simply not drawn.
"""
import argparse
import json
import sys
from pathlib import Path

import bmesh
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import kn_fbx
from sidekick_export_settings import EXPORT

KEYS = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny", "kn_bust", "kn_butt", "kn_defined")
HEAD_HALF_WIDTH = 0.20      # m: the cut reaches across the head, not out to hair hanging on the shoulders
LOOSE_SCRAP = 12            # faces
STUMP_FACES = 60            # faces, and
STUMP_SHARE = 0.5           # of its lock: a cut lock left smaller than both is a stump, not hair
TUCK = 0.05                 # m the cut sits above the rim, inside the shell, when the shell is given: the hair then
                            # comes straight out from under the rim (owner, 23 Oct: no gap between rim and hair)
FLOAT_GAP = 0.03            # m: a piece whose top stays this far below the rim hangs from nothing and goes
INSIDE = 0.006              # m hair is kept inside the shell's outer surface


def islands(faces):
    left, out = set(faces), []
    while left:
        f0 = left.pop()
        isl, st = [f0], [f0]
        while st:
            f = st.pop()
            for e in f.edges:
                for g in e.link_faces:
                    if g in left:
                        left.discard(g)
                        isl.append(g)
                        st.append(g)
        out.append(isl)
    return out


def tuck_inside(bm, mw, shell, rim, margin):
    """Hair near the rim that would show through the helmet's outer shell is drawn just inside it, in the Basis and
    in every body key alike, so no slider brings it back out."""
    inv = mw.inverted()
    layers = list(bm.verts.layers.shape.values())
    moved = 0
    for v in bm.verts:
        w = mw @ v.co
        if w.z < cut_height(rim, margin, w.y) - 0.01:
            continue
        loc, n, _i, d = shell.find_nearest(w)
        if loc is None or d > 0.06 or (w - loc).dot(n) < -INSIDE:
            continue
        off = (inv @ (loc - n * INSIDE)) - v.co
        v.co += off
        for lay in layers:
            v[lay] += off
        moved += 1
    return moved


def cut_height(rim, margin, y):
    zf, yf, zb, yb = rim
    return zf + (y - yf) * (zb - zf) / (yb - yf) - margin


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/HairFixed")
    ap.add_argument("--out-dir", default=str(kn.art_root() / "Sidekick" / "HairUnderHelmet"))
    ap.add_argument("--rim", default="1.659,-0.146,1.522,0.162")
    ap.add_argument("--margin", type=float, default=0.01)
    ap.add_argument("--shell", help="the helmet's outer shell as JSON triangles (world, metres); hair is tucked inside it",
                    default=str(kn.art_root() / "Sidekick" / "HairUnderHelmet" / "DockRunner_Helmet_shell.json"))
    a = ap.parse_args(kn.tool_args())
    rim = [float(x) for x in a.rim.split(",")]
    shell = None
    if a.shell:
        from mathutils import Vector
        from mathutils.bvhtree import BVHTree
        tris = json.loads(Path(a.shell).read_text())["tris"]
        shell = BVHTree.FromPolygons([Vector(p) for t in tris for p in t], [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tris))])
    src = Path(a.src)
    src = src if src.is_absolute() else kn.project_root() / src
    out_dir = Path(a.out_dir)
    if "Synty" in out_dir.parts:
        raise SystemExit("Never export into Assets/Synty.")
    out_dir.mkdir(parents=True, exist_ok=True)
    originals = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}

    results, hidden, problems = [], [], []
    for path in sorted(src.glob("*_02HAIR_*.fbx")):
        stem = path.stem
        bpy.ops.wm.read_factory_settings(use_empty=True)
        new = kn.import_fbx(path)
        arm = next(o for o in new if o.type == "ARMATURE")
        ob = next(o for o in new if o.type == "MESH")
        ob.name = ob.data.name = stem
        ob.hide_set(False)
        ob.hide_viewport = ob.hide_render = False
        me = ob.data
        mw = ob.matrix_world
        before = len(me.polygons)
        bm = bmesh.new()
        bm.from_mesh(me)                    # the vertex positions are the Basis here: the keys survive the round trip
        island_of = {f: i for i, isl in enumerate(islands(bm.faces)) for f in isl}
        size = {}
        for i in island_of.values():
            size[i] = size.get(i, 0) + 1
        # the cut sits TUCK above the rim, so its hard edge is hidden up inside the shell (owner, 23 Oct 2026: the
        # straight cut showed under the rim on the long hairs)
        gone = []
        for f in bm.faces:
            c = mw @ f.calc_center_median()
            if abs(c.x) < HEAD_HALF_WIDTH and c.z > cut_height(rim, a.margin, c.y) + (TUCK if shell else 0.0):
                gone.append(f)
        cut = {island_of[f] for f in gone}
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        # what the cut leaves floating goes too: a tiny loose bit, or a small stump of a lock whose top was cut
        # away (the owner's circles: a floating bead, flaps and lumps on the back)
        scraps = []
        for isl in islands(bm.faces):
            i = island_of[isl[0]]
            top = max((mw @ v.co for f in isl for v in f.verts), key=lambda p: p.z)
            floating = shell is not None and i in cut and top.z < cut_height(rim, a.margin, top.y) - FLOAT_GAP
            if len(isl) < LOOSE_SCRAP or floating or \
                    (i in cut and len(isl) < STUMP_FACES and len(isl) < STUMP_SHARE * size[i]):
                scraps.extend(isl)
        bmesh.ops.delete(bm, geom=scraps, context="FACES")
        if shell:
            tucked = tuck_inside(bm, mw, shell, rim, a.margin)
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(me)
        bm.free()
        me.update()
        if not me.polygons:
            hidden.append(stem)
            continue
        for kb in me.shape_keys.key_blocks:
            kb.value = 0.0
        out = out_dir / (stem + ".fbx")
        for o in bpy.context.view_layer.objects:
            o.select_set(o in (ob, arm))
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=str(out), **EXPORT)
        back, orig = kn_fbx.read(out), kn_fbx.read(originals[stem])
        _checked, bad = kn_fbx.compare_bind(back, orig)
        if bad:
            problems.append(f"{stem}: bind pose differs from Synty's original on {len(bad)} bones ({bad[:3]}).")
        missing = [k for k in KEYS if k not in back["shapes"]]
        if missing:
            problems.append(f"{stem}: keys missing from the file: {missing}")
        results.append({"file": out.name, "faces": f"{before} -> {len(me.polygons)}", "tucked": tucked if shell else 0,
                        "kept": round(len(me.polygons) / before, 2)})
    kn.report("Hair under a helmet", {"folder": str(out_dir), "exported": len(results),
                                      "hidden_completely": hidden, "hairs": results}, problems)
    if problems:
        sys.exit(1)


main()
