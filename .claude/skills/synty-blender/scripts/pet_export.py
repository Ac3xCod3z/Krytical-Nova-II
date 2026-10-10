"""Export a finished pet or prop to FBX for Unity, and check the file.

  run_blender.py --blend FILE.blend pet_export.py --out-dir DIR [--root SM_Pet_...]
        [--actions all|none|Idle,Flap]     rigged pets only: which animations to bake in (default all)

Static pet: DIR/<root name>.fbx, the body and its child pieces, one material, metres, no rotation or scale
left on any node.

Rigged pet (the meshes have an Armature modifier): the file gets a clean copy of the rig that holds only
the deforming bones under one `root` bone. A Rigify rig's hundreds of control and helper bones stay in
Blender. Each chosen action is baked onto that clean rig, so the animation made with the controls plays
in Unity without them.
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

STATIC = dict(use_selection=True, object_types={"MESH"}, global_scale=1.0, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True, bake_space_transform=True,
              axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
              use_mesh_modifiers=False, use_custom_props=False, path_mode="AUTO", embed_textures=False)
# Baking the axis change into the data is not safe with bones, so a rigged file keeps Blender's -90 degree
# turn on the rig and mesh nodes; the model's root is clean. Leave Unity's "Bake Axis Conversion" OFF:
# with it on the pet comes in facing backwards.
RIGGED = dict(use_selection=True, object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True, bake_space_transform=False,
              axis_forward="-Z", axis_up="Y", add_leaf_bones=False, primary_bone_axis="Y",
              secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
              mesh_smooth_type="OFF", use_tspace=False, use_mesh_modifiers=False, use_custom_props=False,
              path_mode="AUTO", embed_textures=False, bake_anim_use_all_actions=False,
              bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
HELPER_PREFIXES = ("ORG-", "MCH-", "DEF-", "WGT-")


def activate(ob, mode):
    for o in bpy.context.view_layer.objects:
        o.select_set(o is ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode=mode)


def export_rig(src, name, used):
    """A copy of the rig with only the deforming bones the meshes use (and the bones between them and the
    root), each parented to its nearest deforming ancestor."""
    exp = src.copy()
    exp.data = src.data.copy()
    exp.name = exp.data.name = name
    exp.animation_data_clear()
    exp.data.animation_data_clear()
    bpy.context.scene.collection.objects.link(exp)

    activate(exp, "EDIT")
    bones = exp.data.edit_bones
    deform = {b.name for b in bones if b.use_deform}

    def bare(bone_name):
        for prefix in HELPER_PREFIXES:
            if bone_name.startswith(prefix):
                return bone_name[len(prefix):]
        return bone_name

    def deform_parent(bone):
        below = {c.name for c in bone.children_recursive}   # never pick one of the bone's own descendants
        p = bone.parent
        while p is not None:
            if p.name in deform:
                return p.name
            twin = "DEF-" + bare(p.name)     # Rigify hangs limbs off ORG- bones; their DEF- twin deforms
            if twin in deform and twin != bone.name and twin not in below:
                return twin
            p = p.parent
        return None

    parents = {n: deform_parent(bones[n]) for n in deform}
    keep = set()
    for n in used & deform:
        while n and n not in keep:
            keep.add(n)
            n = parents[n]
    deform = keep
    root = bones.get("root")
    if root is None:
        root = bones.new("root")
        root.head, root.tail = (0, 0, 0), (0, 0.2, 0)
    root.parent = None
    root.use_deform = False
    for n in deform:
        b = bones[n]
        b.use_connect = False
        b.parent = bones[parents[n]] if parents[n] else root
    for b in [b for b in bones if b.name not in deform and b.name != "root"]:
        bones.remove(b)
    bpy.ops.object.mode_set(mode="POSE")
    for pb in exp.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        if pb.name in src.pose.bones:
            c = pb.constraints.new("COPY_TRANSFORMS")
            c.target, c.subtarget = src, pb.name
    bpy.ops.object.mode_set(mode="OBJECT")
    return exp, len(deform)


def assign(ob, action):
    ob.animation_data_create()
    ob.animation_data.action = action
    slots = getattr(action, "slots", None) if action else None
    if slots and hasattr(ob.animation_data, "action_slot") and ob.animation_data.action_slot is None:
        fitting = [s for s in slots if getattr(s, "target_id_type", "OBJECT") in ("OBJECT", "UNSPECIFIED")]
        if fitting:
            ob.animation_data.action_slot = fitting[0]


def rig_actions(src, wanted):
    if wanted == "none":
        return []
    found = []
    for act in bpy.data.actions:
        curves = []
        if hasattr(act, "fcurves"):
            try:
                curves = list(act.fcurves)
            except Exception:
                curves = []
        if not curves and hasattr(act, "layers"):
            curves = [fc for layer in act.layers for strip in layer.strips
                      for bag in getattr(strip, "channelbags", []) for fc in bag.fcurves]
        names = {fc.data_path.split('"')[1] for fc in curves if fc.data_path.startswith('pose.bones["')}
        if names and names & set(src.pose.bones.keys()):
            found.append(act)
    if wanted != "all":
        ask = [w.strip() for w in wanted.split(",")]
        missing = [w for w in ask if w not in {a.name for a in found}]
        if missing:
            raise SystemExit(f"No action for this rig named {missing}. It has: {sorted(a.name for a in found)}")
        found = [a for a in found if a.name in ask]
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root")
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--actions", default="all")
    a = ap.parse_args(kn.tool_args())

    name = a.root or bpy.context.scene.get("kn_pet_root")
    if not name:
        raise SystemExit("Pass --root <object name>, or run pet_finish.py first.")
    root = kn.get_object(name)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    src = kn.bound_rig(root)
    if src:
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and kn.bound_rig(o) is src]
    else:
        meshes = [root] + [o for o in root.children_recursive if o.type == "MESH"]

    palette = bpy.context.scene.get("kn_palette")
    # Everything on the shared swatch uses one material, so Unity can share it across pets and props.
    mat_name = "M_KN_Swatch" if palette and Path(palette).name == "T_KN_Swatch.png" else "M_" + name
    mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
    if palette and Path(palette).is_file():
        mat = kn.preview_material(palette, name=mat_name, uv_layer="map1")
    swapped = []
    for o in meshes:
        swapped.append((o, [s.material for s in o.material_slots], [p.material_index for p in o.data.polygons]))
        o.data.materials.clear()
        o.data.materials.append(mat)

    out_dir = Path(a.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / (name + ".fbx")
    problems, warnings = [], []
    exp, baked, rebind, kept_bones, takes_wanted = None, [], [], 0, []
    scene = bpy.context.scene
    was = (scene.frame_current, src.animation_data.action if src and src.animation_data else None)
    try:
        if src:
            used = set()
            for o in meshes:
                names = [g.name for g in o.vertex_groups]
                used.update(names[g.group] for v in o.data.vertices for g in v.groups if g.weight > 1e-5)
            exp, kept_bones = export_rig(src, name + "_Skeleton", used)
            for o in meshes:
                mod = next(m for m in o.modifiers if m.type == "ARMATURE" and m.object is src)
                rebind.append((o, mod, o.parent, o.matrix_world.copy()))
                mod.object = exp
                if o.parent is src:
                    o.parent = exp
                    o.matrix_world = rebind[-1][3]
            actions = rig_actions(src, a.actions)
            for act in actions:
                src.animation_data_create()
                src.animation_data.action = None
                kn.zero_controls(src)      # controls this action leaves alone must not keep the last one's pose
                assign(src, act)
                start, end = (int(round(f)) for f in act.frame_range)
                activate(exp, "POSE")
                bpy.ops.pose.select_all(action="SELECT")
                bpy.ops.nla.bake(frame_start=start, frame_end=max(end, start + 1), only_selected=False,
                                 visual_keying=True, clear_constraints=False, use_current_action=False,
                                 bake_types={"POSE"})
                bpy.ops.object.mode_set(mode="OBJECT")
                result = exp.animation_data.action
                result.name = act.name + "__baked"
                baked.append(result)
                strip = exp.animation_data.nla_tracks.new().strips.new(act.name, start, result)
                strip.name = act.name
                takes_wanted.append(act.name)
                exp.animation_data.action = None
            for pb in exp.pose.bones:
                for c in list(pb.constraints):
                    pb.constraints.remove(c)
            for o in bpy.context.view_layer.objects:
                o.select_set(o in meshes or o is exp)
            bpy.context.view_layer.objects.active = exp
            bpy.ops.export_scene.fbx(filepath=str(out), bake_anim=bool(actions), **RIGGED)
        else:
            for o in bpy.context.view_layer.objects:
                o.select_set(o in meshes)
            bpy.context.view_layer.objects.active = root
            bpy.ops.export_scene.fbx(filepath=str(out), **STATIC)
    finally:
        for o, mod, parent, world in rebind:
            mod.object = src
            o.parent = parent
            o.matrix_world = world
        if exp is not None:
            data = exp.data
            bpy.data.objects.remove(exp)
            bpy.data.armatures.remove(data)
        for act in baked:
            bpy.data.actions.remove(act)
        if src:
            if was[1] is not None or src.animation_data:
                assign(src, was[1])
            scene.frame_set(was[0])
        for o, mats, indices in swapped:
            o.data.materials.clear()
            for m in mats:
                o.data.materials.append(m)
            o.data.polygons.foreach_set("material_index", indices)

    data = kn_fbx.read(out)
    if sorted(data["meshes"]) != sorted(o.name for o in meshes):
        problems.append(f"Expected meshes {[o.name for o in meshes]}, the file holds {data['meshes']}.")
    info = {"file": str(out), "size_kb": round(out.stat().st_size / 1024), "meshes": data["meshes"],
            "unit": data["unit"], "palette": palette}
    if src:
        helpers = [b for b in data["skeleton"] if b.startswith(("ORG-", "MCH-", "WGT-"))]
        if helpers:
            problems.append(f"Helper bones reached the file: {helpers[:6]}")
        scaled = [n for n in data["scaled_nodes"]]
        if scaled:
            problems.append(f"Nodes carry a scale: {scaled[:3]}. Apply scale on the rig and meshes, then export again.")
        missing = [t for t in takes_wanted if t not in data["takes"]]
        if missing:
            problems.append(f"Animations missing from the file: {missing}")
        info.update({"rig_bones_in_blender": len(src.data.bones), "bones_in_file": len(data["skeleton"]),
                     "deforming_bones_kept": kept_bones, "animations": data["takes"],
                     "unity": "Run make_unitypackage.py: it sets the import (Generic rig, Bake Axis Conversion OFF so "
                              "the pet faces +Z) and adds a controller and a prefab."})
        if not takes_wanted and a.actions != "none":
            warnings.append("No action animates this rig, so the file holds the skinned model only.")
    else:
        if data["scaled_nodes"]:
            problems.append(f"Nodes carry a scale: {data['scaled_nodes'][:3]}. Run pet_finish.py first.")
        if any(o.data.shape_keys for o in meshes):
            warnings.append("Shape keys are exported; the pet system does not drive any yet.")

    kn.report("Pet export", info, problems, warnings)
    if problems:
        sys.exit(1)


main()
