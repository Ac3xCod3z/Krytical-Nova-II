"""Run a rig's actions in the open scene and measure that each one moves the rig and comes back to rest.

  run_blender.py --live test_actions.py --rig SM_Pet_X_S1_Rig [--actions Blink,SpinChase] [--leave SpinChase]

For each action it steps through every frame, records how far the deforming bones travel from the rest
pose, and how far they are from rest on the last frame. Afterwards the rig is left on --leave (default: the
last action) at its first frame, with the timeline set to that action's range, so pressing Space plays it.
"""
import argparse
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def assign(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    slots = getattr(action, "slots", None)
    if slots and hasattr(rig.animation_data, "action_slot") and rig.animation_data.action_slot is None:
        rig.animation_data.action_slot = slots[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rig", required=True)
    ap.add_argument("--actions")
    ap.add_argument("--leave")
    ap.add_argument("--loops", default="Idle,Walk,Run,FrontRoll",
                    help="actions that loop, comma separated: these must end on their own first frame, not on rest")
    a = ap.parse_args(kn.tool_args())
    rig = kn.get_object(a.rig, "ARMATURE")
    scene = bpy.context.scene
    names = [n.strip() for n in a.actions.split(",")] if a.actions else sorted(
        act.name for act in bpy.data.actions if not act.name.endswith("__baked"))
    deform = [b.name for b in rig.data.bones if b.use_deform]
    loops = {n.strip() for n in a.loops.split(",") if n.strip()}
    problems, results = [], {}

    def snapshot():
        bpy.context.view_layer.update()
        return {n: rig.pose.bones[n].matrix.copy() for n in deform}

    def apart(p, q):
        worst_move, worst_turn = 0.0, 0.0
        for n in deform:
            worst_move = max(worst_move, (p[n].translation - q[n].translation).length)
            angle = p[n].to_quaternion().rotation_difference(q[n].to_quaternion()).angle
            worst_turn = max(worst_turn, min(angle, 6.283185307 - angle))     # the short way round
        return worst_move, worst_turn

    rig.animation_data_create()
    rig.animation_data.action = None
    kn.zero_controls(rig)
    rest = snapshot()

    for name in names:
        act = bpy.data.actions.get(name)
        if act is None:
            problems.append(f"No action named '{name}'.")
            continue
        rig.animation_data.action = None
        kn.zero_controls(rig)      # or controls this action leaves alone keep the pose of the one before
        assign(rig, act)
        first, last = (int(round(f)) for f in act.frame_range)
        peak_move = peak_turn = 0.0
        for frame in range(first, last + 1):
            scene.frame_set(frame)
            move, turn = apart(snapshot(), rest)
            peak_move, peak_turn = max(peak_move, move), max(peak_turn, turn)
        end_pose = snapshot()
        end_move, end_turn = apart(end_pose, rest)
        scene.frame_set(first)
        start_pose = snapshot()
        start_move, start_turn = apart(start_pose, rest)
        if name in loops:
            seam_move, seam_turn = apart(end_pose, start_pose)
            results[name] = {"frames": f"{first}-{last}", "loops": True, "most_a_bone_moves_cm": round(peak_move * 100, 1),
                             "most_a_bone_turns_deg": round(peak_turn * 57.2958, 1),
                             "last_frame_off_first_mm": round(seam_move * 1000, 2)}
            if peak_move < 1e-4 and peak_turn < 1e-3:
                problems.append(f"{name}: nothing moves.")
            if seam_move > 0.0005 or seam_turn > 0.01:
                problems.append(f"{name}: the last frame is not the first frame, so the loop will jump.")
            continue
        results[name] = {"frames": f"{first}-{last}", "most_a_bone_moves_cm": round(peak_move * 100, 1),
                         "most_a_bone_turns_deg": round(peak_turn * 57.2958, 1),
                         "off_rest_at_start_mm": round(start_move * 1000, 2), "off_rest_at_end_mm": round(end_move * 1000, 2)}
        if peak_move < 1e-4 and peak_turn < 1e-3:
            problems.append(f"{name}: nothing moves.")
        if end_move > 0.001 or end_turn > 0.02 or start_move > 0.001 or start_turn > 0.02:
            problems.append(f"{name}: does not start or end on the rest pose.")

    rig.animation_data.action = None
    kn.zero_controls(rig)
    leave = a.leave or (names[-1] if names else None)
    act = bpy.data.actions.get(leave) if leave else None
    if act is not None:
        assign(rig, act)
        first, last = (int(round(f)) for f in act.frame_range)
        scene.frame_start, scene.frame_end = first, last
        scene.frame_set(first)
    kn.report("Action test", {"rig": rig.name, "actions": results, "left_on": leave,
                              "to_watch": "Press Space in the 3D viewport to play; pick another action in the "
                                          "Action Editor (Dope Sheet > Action Editor)."}, problems)
    if problems:
        sys.exit(1)


main()
