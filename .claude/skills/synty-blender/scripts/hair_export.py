"""Export the fixed hairs and facial hairs (made by creator_hair_fix.py) as Synty-named copies for the game.

  run_blender.py --blend SyntyHairFixes.blend hair_export.py
        [--out-dir Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/HairFixed]

For each fixed mesh: imports Synty's original FBX fresh (its rig with its own dangle bones, its weights, UVs, face keys),
replaces the body keys with the fixed ones by vertex index, exports with the proven Sidekick settings and checks the
file against that original: same bones and bind pose, same vertex count, every body key present.
Assets/Synty is only read.
"""
import argparse
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)
import kn_fbx
from sidekick_export_settings import EXPORT

KEYS = ("masculineFeminine", "defaultBuff", "defaultHeavy", "defaultSkinny", "kn_bust", "kn_butt", "kn_defined")
DEFAULT_OUT = "Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/HairFixed"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    a = ap.parse_args(kn.tool_args())
    out_dir = Path(a.out_dir)
    if not out_dir.is_absolute():
        out_dir = kn.project_root() / out_dir
    if "Synty" in out_dir.parts:
        raise SystemExit("Never export into Assets/Synty.")
    out_dir.mkdir(parents=True, exist_ok=True)
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}

    jobs = []
    for ob in bpy.data.objects:
        stem = ob.name.split(".")[0]          # a name already taken in the source scene gets ".001"
        if ob.type != "MESH" or not ob.data.shape_keys or stem not in index:
            continue
        ks = ob.data.shape_keys.key_blocks
        b = ks[0].data
        keys = {k: [ks[k].data[i].co - b[i].co for i in range(len(b))] for k in KEYS if k in ks}
        jobs.append((stem, keys, len(b)))
    if not jobs:
        raise SystemExit("No fixed hair meshes in this file (expected SyntyHairFixes.blend).")

    results, problems = [], []
    for stem, keys, n in sorted(jobs):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        new = kn.import_fbx(index[stem])
        arm = next(o for o in new if o.type == "ARMATURE")
        ob = next(o for o in new if o.type == "MESH")
        ob.name = ob.data.name = stem
        ob.hide_set(False)                     # some Synty parts come in hidden; a hidden object is not exported
        ob.hide_viewport = ob.hide_render = False
        me = ob.data
        if len(me.vertices) != n:
            problems.append(f"{stem}: {len(me.vertices)} vertices in Synty's file, {n} in the fix; skipped.")
            continue
        if not me.shape_keys:
            ob.shape_key_add(name="Basis", from_mix=False)
        basis = me.shape_keys.key_blocks[0]
        for k, d in keys.items():
            kb = me.shape_keys.key_blocks.get(k) or ob.shape_key_add(name=k, from_mix=False)
            for i in range(n):
                kb.data[i].co = basis.data[i].co + d[i]
        for k in KEYS:                          # every Sidekick part carries every body key, empty where it does nothing
            if k not in me.shape_keys.key_blocks:
                kb = ob.shape_key_add(name=k, from_mix=False)
        for kb in me.shape_keys.key_blocks:
            kb.value = 0.0
        out = out_dir / (stem + ".fbx")
        for o in bpy.context.view_layer.objects:
            o.select_set(o in (ob, arm))
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=str(out), **EXPORT)
        back, orig = kn_fbx.read(out), kn_fbx.read(index[stem])
        checked, bad = kn_fbx.compare_bind(back, orig)
        if bad:
            problems.append(f"{stem}: bind pose differs from Synty's original on {len(bad)} bones ({bad[:3]}).")
        missing = [k for k in KEYS if k not in back["shapes"]]
        if missing:
            problems.append(f"{stem}: keys missing from the file: {missing}")
        lost = [s for s in orig["shapes"] if s not in back["shapes"]]
        if lost:
            problems.append(f"{stem}: Synty's own keys lost: {lost[:5]}")
        if back["meshes"] != [stem]:
            problems.append(f"{stem}: the file holds {back['meshes']}.")
        biggest = max((max(v.length for v in d) for d in keys.values()), default=0.0)
        results.append({"file": out.name, "bones_checked": checked, "keys": len(back["shapes"]),
                        "largest_move_cm": round(biggest, 1)})
    kn.report("Hair export", {"folder": str(out_dir), "files": len(results),
                              "hairs": sum(1 for r in results if "_02HAIR_" in r["file"]),
                              "facial_hair": sum(1 for r in results if "_09FCHR_" in r["file"])}, problems)
    if problems:
        sys.exit(1)


main()
