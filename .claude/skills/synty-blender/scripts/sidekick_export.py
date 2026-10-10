"""Export a fitted Sidekick part to FBX and check the file against Synty's own.

  run_blender.py --blend FILE.blend sidekick_export.py --out-dir DIR [--object SK_...]

Writes DIR/<part name>.fbx: the rig plus this one mesh, one material, centimetre units, the same bind
pose as Synty's files. Then reads the FBX back and compares it with SK_BaseModel.fbx.
"""
import argparse
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers
import kn_fbx

# These settings were proven by sending a Synty part through Blender and comparing the raw FBX with the
# original: same unit, same bind matrices, same vertex numbers. FBX_SCALE_NONE is the one that matters;
# the other scale options write a 0.01 scale onto the nodes and the part no longer matches the rig.
EXPORT = dict(use_selection=True, object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_NONE", use_space_transform=True, bake_space_transform=False,
              axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y",
              secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
              mesh_smooth_type="OFF", use_tspace=False, bake_anim=False, use_mesh_modifiers=False,
              use_custom_props=False, path_mode="AUTO", embed_textures=False)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object")
    ap.add_argument("--out-dir", required=True)
    a = ap.parse_args(kn.tool_args())

    spec = kn.load_spec()
    name = a.object or bpy.context.scene.get("kn_part_name")
    if not name:
        raise SystemExit("Pass --object <part name>.")
    src = kn.get_object(name)
    arm = kn.find_armature()
    if arm is None or src.parent != arm:
        raise SystemExit(f"'{name}' is not bound to the Sidekick rig. Run sidekick_fit.py first.")
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    # Export a copy with one material, as Synty's parts have; the working mesh keeps its swatch materials.
    src.name = name + "__src"
    src.data.name = name + "__src"
    copy = src.copy()
    copy.data = src.data.copy()
    copy.name = copy.data.name = name
    bpy.context.scene.collection.objects.link(copy)
    mat = bpy.data.materials.get(kn.PREVIEW_MATERIAL) or kn.preview_material(
        kn.colour_map_path(), uv_layer=spec["uv"]["swatch_layer"])
    copy.data.materials.clear()
    copy.data.materials.append(mat)
    for p in copy.data.polygons:
        p.material_index = 0
    for mod in copy.modifiers:
        if mod.type == "ARMATURE":
            mod.object = arm
    for g in [g for g in copy.vertex_groups if g.name.startswith("hang")]:
        copy.vertex_groups.remove(g)

    out_dir = Path(a.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / (name + ".fbx")
    hidden = (arm.hide_get(), arm.hide_viewport)
    arm.hide_set(False)
    arm.hide_viewport = False
    try:
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        copy.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = copy
        bpy.ops.export_scene.fbx(filepath=str(out), **EXPORT)
    finally:
        temp = copy.data
        bpy.data.objects.remove(copy)
        bpy.data.meshes.remove(temp)
        src.name = name
        src.data.name = name
        arm.hide_set(hidden[0])
        arm.hide_viewport = hidden[1]

    # Read the file back.
    problems, warnings = [], []
    uv = src.data.uv_layers.get(spec["uv"]["swatch_layer"])
    if uv:
        named = kn.cell_names(spec)
        stray = sum(1 for p in src.data.polygons
                    if (min(15, int(uv.data[p.loop_indices[0]].uv[0] * 16)),
                        min(15, int(uv.data[p.loop_indices[0]].uv[1] * 16))) not in named)
        if stray:
            warnings.append(f"{stray} faces sit on a swatch with no name; the colour tools cannot recolour them. "
                            "Did sidekick_fit.py finish without problems?")
    ours = kn_fbx.read(out)
    ref_path = kn.base_model_path()
    checked, bad = kn_fbx.compare_bind(ours, kn_fbx.read(ref_path))
    if checked == 0:
        # Attach bones carry no mesh in the base model; compare with a Synty part of the same slot.
        slot = name.split("_")[4]
        for cand in sorted((kn.sidekick_root() / "Resources" / "Meshes").glob(f"**/SK_*_{slot}_*.fbx")):
            checked, bad = kn_fbx.compare_bind(ours, kn_fbx.read(cand))
            if checked:
                ref_path = cand
                break
    if checked == 0:
        warnings.append("No bone of this part could be compared with a Synty file.")
    if bad:
        problems.append(f"Bind pose differs from Synty's on {len(bad)} bones (first: {bad[:4]}). "
                        "The rig was moved or edited; start again from sidekick_new_part.py.")
    if abs(ours["unit"] - 1.0) > 1e-6:
        problems.append(f"File unit is {ours['unit']}, Synty's is 1 (centimetres).")
    if ours["scaled_nodes"]:
        problems.append(f"Nodes carry a scale: {ours['scaled_nodes'][:3]}")
    if ours["meshes"] != [name]:
        problems.append(f"The file should hold the one mesh '{name}', it holds {ours['meshes']}.")
    missing = [k for k in kn.BODY_KEYS if k not in ours["shapes"]]
    if missing:
        problems.append(f"Body shape keys missing from the file: {missing}")
    if not ours["uv_layers"] or ours["uv_layers"][0] != spec["uv"]["swatch_layer"]:
        problems.append(f"First UV layer should be '{spec['uv']['swatch_layer']}', the file has {ours['uv_layers']}.")
    rig_bones = {b["name"] for b in spec["bones"]}
    new_bones = [b for b in ours["bind"] if b not in rig_bones]
    if new_bones:
        warnings.append(f"Bones the base rig does not have: {new_bones}. The game must add them or they fall to the root.")

    kn.report("Sidekick export", {
        "file": str(out), "size_kb": round(out.stat().st_size / 1024), "mesh": ours["meshes"],
        "shape_keys": ours["shapes"], "uv_layers": ours["uv_layers"], "unit": ours["unit"],
        "bind_pose_checked_bones": checked, "compared_with": str(ref_path.name)}, problems, warnings)
    if problems:
        sys.exit(1)


main()
