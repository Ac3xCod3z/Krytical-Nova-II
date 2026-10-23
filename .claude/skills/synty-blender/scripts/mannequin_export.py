"""Export the bare-body parts with the Krytical Nova sliders (kn_bust, kn_butt, kn_defined) as Synty-named copies.

  run_blender.py --blend SyntyMannequinModeling.blend mannequin_export.py
        [--out-dir Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody]
        [--packs HUMN_BASE_01:HU01:CivilianF:CivilianM,ELVN_BASE_01:EV01:ElfF:ElfM]

For every body slot (10TORS..21FOTR, 38WRAP) of each pack: imports Synty's original FBX fresh (so the rig, bind pose,
UVs, weights and Synty's own shape keys are exactly Synty's), adds the three new keys copied by vertex index from the
mannequin scene (bust and butt from the feminine mannequin, definition from the masculine one), exports with the
proven Sidekick settings, reads the file back and checks it against Synty's original of that part. Assets/Synty is only read.
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

SLOTS = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR",
         "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR", "38WRAP"]
NEW = {"kn_bust": "F", "kn_butt": "F", "kn_defined": "M"}
DEFAULT_OUT = "Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes/BareBody"
DEFAULT_PACKS = "HUMN_BASE_01:HU01:CivilianF:CivilianM,ELVN_BASE_01:EV01:ElfF:ElfM"


def deltas_from_scene(stem, tag):
    """{key: [Vector]} of the new keys on the mannequin copy of `stem`, or {} if that mannequin has no such part."""
    ob = bpy.data.objects.get(f"{stem}.{tag}")
    if ob is None or not ob.data.shape_keys:
        return {}, 0
    ks = ob.data.shape_keys.key_blocks
    b = ks[0].data
    out = {}
    for k in NEW:
        if k in ks:
            out[k] = [ks[k].data[i].co - b[i].co for i in range(len(b))]
    return out, len(b)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    ap.add_argument("--packs", default=DEFAULT_PACKS)
    a = ap.parse_args(kn.tool_args())
    if kn.is_live():
        raise SystemExit("Run headless: this tool clears the scene between parts.")
    out_dir = Path(a.out_dir)
    if not out_dir.is_absolute():
        out_dir = kn.project_root() / out_dir
    if "Synty" in out_dir.parts:
        raise SystemExit("Never export into Assets/Synty.")
    out_dir.mkdir(parents=True, exist_ok=True)
    index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}

    # 1. Read everything needed from the mannequin scene while it is loaded.
    jobs = []
    for chunk in a.packs.split(","):
        pack, sp, fem, masc = chunk.split(":")
        for slot in SLOTS:
            stem = f"SK_{pack}_{slot}_{sp}"
            if stem not in index:
                continue
            fd, fn = deltas_from_scene(stem, fem)
            md, mn = deltas_from_scene(stem, masc)
            keys = {}
            for k, sex in NEW.items():
                src = fd if sex == "F" else md
                if k in src:
                    keys[k] = src[k]
            jobs.append((stem, keys, fn or mn))

    # 2. One fresh import per part.
    results, problems, warnings = [], [], []
    for stem, keys, scene_verts in jobs:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        new = kn.import_fbx(index[stem])
        arm = next(o for o in new if o.type == "ARMATURE")
        ob = next(o for o in new if o.type == "MESH")
        ob.name = ob.data.name = stem
        me = ob.data
        if scene_verts and len(me.vertices) != scene_verts:
            problems.append(f"{stem}: {len(me.vertices)} vertices in Synty's file, {scene_verts} in the scene; skipped.")
            continue
        if not me.shape_keys:
            ob.shape_key_add(name="Basis", from_mix=False)
        basis = me.shape_keys.key_blocks[0]
        moved = {}
        for k in NEW:                      # every part carries all three, empty where the slider does nothing
            kb = me.shape_keys.key_blocks.get(k) or ob.shape_key_add(name=k, from_mix=False)
            d = keys.get(k)
            biggest = 0.0
            for i in range(len(me.vertices)):
                delta = d[i] if d else Vector((0, 0, 0))
                kb.data[i].co = basis.data[i].co + delta
                biggest = max(biggest, delta.length)
            moved[k] = round(biggest, 2)
        for kb in me.shape_keys.key_blocks:
            kb.value = 0.0                 # Unity would take a non-zero value as the shape's default weight
        out = out_dir / (stem + ".fbx")
        for o in bpy.context.view_layer.objects:
            o.select_set(o in (ob, arm))
        bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=str(out), **EXPORT)

        back = kn_fbx.read(out)
        # Against the Synty original of the same part: some originals already differ from SK_BaseModel on a few
        # bones (thighs, upper arms, props), and the copy must match what the game loads today, not the base.
        checked, bad = kn_fbx.compare_bind(back, kn_fbx.read(index[stem]))
        if bad:
            problems.append(f"{stem}: bind pose differs from Synty's original on {len(bad)} bones ({bad[:3]}).")
        if abs(back["unit"] - 1.0) > 1e-6 or back["scaled_nodes"]:
            problems.append(f"{stem}: unit {back['unit']}, scaled nodes {back['scaled_nodes'][:2]}.")
        if back["meshes"] != [stem]:
            problems.append(f"{stem}: the file holds {back['meshes']}.")
        missing = [k for k in kn.BODY_KEYS + tuple(NEW) if k not in back["shapes"]]
        if missing:
            problems.append(f"{stem}: keys missing from the file: {missing}")
        if not back["uv_layers"] or back["uv_layers"][0] != "map1":
            problems.append(f"{stem}: first UV layer is {back['uv_layers'][:1]}.")
        results.append({"file": out.name, "kb": round(out.stat().st_size / 1024), "bones_checked": checked,
                        "new_keys_max_cm": moved})
    kn.report("Bare-body export", {"folder": str(out_dir), "files": len(results), "parts": results},
              problems, warnings)
    if problems:
        sys.exit(1)


main()
