"""Build a working scene for a new Sidekick part.

  run_blender.py sidekick_new_part.py --slot 10TORS --name SK_KRYT_HEGE_01_10TORS_HU01 --out FILE.blend
                                      [--import-mesh model.fbx|.obj|.glb]

Brings in Synty's base body and rig as a locked reference, makes one material per colour swatch the slot
normally uses, and saves the .blend. The artist models (or pastes) the part into this file.
"""
import argparse
import re
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers

COMMON_CELLS = ["Metal 01", "Metal 02", "Metal 03", "Metal 04", "Plastic 01", "Rubber 01", "Strap 01",
                "Glass 01", "Glow 01", "Skin 01"]


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--out")
    ap.add_argument("--import-mesh")
    a = ap.parse_args(kn.tool_args())

    spec = kn.load_spec()
    if a.slot not in spec["slots"]:
        raise SystemExit(f"Unknown slot '{a.slot}'. Slots: {sorted(spec['slots'])}")
    if not re.match(spec["part_name_regex"], a.name) or a.name.split("_")[4] != a.slot:
        raise SystemExit(f"'{a.name}' is not a valid part name for slot {a.slot}. "
                         "Pattern: SK_<PACK>_<SET>_<VV>_<slot>_<SP>01, e.g. SK_KRYT_HEGE_01_10TORS_HU01")
    if not kn.is_live():
        if not a.out:
            raise SystemExit("--out FILE.blend is required in a headless run.")
        bpy.ops.wm.read_factory_settings(use_empty=True)

    if kn.find_armature() is None:
        new = kn.import_fbx(kn.base_model_path())
        ref = kn.ensure_collection(kn.REF_COLLECTION)
        mat = kn.preview_material(kn.colour_map_path(), uv_layer=spec["uv"]["swatch_layer"])
        for ob in new:
            kn.move_to_collection(ob, ref)
            if ob.type == "MESH":
                # The base model's facial hair sits 100x out of place; it is no use as a reference.
                if "09FCHR" in ob.name:
                    bpy.data.objects.remove(ob)
                    continue
                ob.data.materials.clear()
                ob.data.materials.append(mat)
                ob.hide_select = True
            elif ob.type == "ARMATURE":
                ob.name = "SK_Rig"
                ob.show_in_front = False
                ob.display_type = "WIRE"

    # One material per swatch the slot normally uses, coloured like Synty's default colour map.
    img = bpy.data.images.load(str(kn.colour_map_path()), check_existing=True)
    px = list(img.pixels)
    w = img.size[0]
    cells = kn.cell_lookup(spec)
    made = []
    for cell_name in spec["slots"][a.slot]["own_cells"] + COMMON_CELLS:
        if kn.cell_key(cell_name) not in cells:
            continue
        u, v = cells[kn.cell_key(cell_name)]
        i = ((v * 2) * w + u * 2) * 4
        mat = bpy.data.materials.get(cell_name) or bpy.data.materials.new(cell_name)
        mat.diffuse_color = (srgb_to_linear(px[i]), srgb_to_linear(px[i + 1]), srgb_to_linear(px[i + 2]), 1.0)
        mat.use_fake_user = True
        made.append(cell_name)

    scene = bpy.context.scene
    scene["kn_part_name"] = a.name
    scene["kn_slot"] = a.slot
    work = kn.ensure_collection("SK_Work")

    imported = []
    if a.import_mesh:
        ext = Path(a.import_mesh).suffix.lower()
        before = set(bpy.data.objects)
        if ext == ".fbx":
            bpy.ops.wm.fbx_import(filepath=a.import_mesh)
        elif ext == ".obj":
            bpy.ops.wm.obj_import(filepath=a.import_mesh)
        elif ext in (".glb", ".gltf"):
            bpy.ops.import_scene.gltf(filepath=a.import_mesh)
        else:
            raise SystemExit(f"Cannot import '{ext}'. Use .fbx, .obj or .glb.")
        for ob in [o for o in bpy.data.objects if o not in before]:
            kn.move_to_collection(ob, work)
            imported.append((ob.name, ob.type))

    kn.save_blend(a.out)
    slot = spec["slots"][a.slot]
    kn.report("New Sidekick part scene", {
        "file": a.out or bpy.data.filepath, "part": a.name, "slot": a.slot + " (" + slot["name"] + ")",
        "triangles_synty_uses": slot["tris"], "swatch_materials": made, "imported": imported,
        "next": "Model the part round the reference body, give every face one of the swatch materials, "
                "then run sidekick_fit.py --object <its name>."})


main()
