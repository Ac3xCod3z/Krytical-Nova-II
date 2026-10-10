"""Check that a rig with every control at zero holds the mesh exactly as it was modelled.

  run_blender.py --live rig_rest_check.py --rig SM_Pet_X_S1_Rig
  run_blender.py --blend FILE.blend rig_rest_check.py --rig SM_Pet_X_S1_Rig

With no action and all controls zeroed, each deforming bone should sit where it was when the mesh was
bound. Where a limb's IK settles on a different bend (a paw folding the wrong way), this lists the bones
that are off and by how much. It clears the rig's action and zeroes its controls (only the controls) to
measure.
"""
import argparse
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rig", required=True)
    a = ap.parse_args(kn.tool_args())
    rig = kn.get_object(a.rig, "ARMATURE")
    if rig.animation_data:
        rig.animation_data.action = None
    kn.zero_controls(rig)
    bpy.context.scene.frame_set(bpy.context.scene.frame_current)
    bpy.context.view_layer.update()
    off = []
    for b in rig.data.bones:
        if not b.use_deform:
            continue
        pose, rest = rig.pose.bones[b.name].matrix, b.matrix_local
        move = (pose.translation - rest.translation).length
        angle = pose.to_quaternion().rotation_difference(rest.to_quaternion()).angle
        angle = min(angle, 6.283185307 - angle)
        if move > 0.0005 or angle > 0.01:
            off.append((b.name, round(move * 1000, 1), round(angle * 57.2958, 1)))
    off.sort(key=lambda t: -(t[1] + t[2]))
    props = {}
    for pb in rig.pose.bones:
        keys = {k: (round(pb[k], 3) if isinstance(pb[k], float) else pb[k]) for k in pb.keys()
                if k in ("IK_FK", "IK_Stretch", "pole_vector", "FK_limb_follow")}
        if keys:
            props[pb.name] = keys
    problems = [f"{len(off)} deforming bones are not where the mesh was bound: the neutral rig bends the model."] if off else []
    kn.report("Rig rest check", {"rig": rig.name, "auto_run_scripts": bpy.app.autoexec_fail is False and bpy.context.preferences.filepaths.use_scripts_auto_execute,
                                 "bones_off_rest (name, mm, degrees)": off[:24], "limb_switches": props}, problems)
    if problems:
        sys.exit(1)


main()
