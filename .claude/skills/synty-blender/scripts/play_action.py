"""Play one of a rig's actions on a loop in the open Blender window (or stop playback).

  run_blender.py --live play_action.py --rig SM_Pet_X_S1_Rig --action SpinChase
  run_blender.py --live play_action.py --stop

Puts the action on the rig, sets the timeline to the action's length and starts playback, which loops.
Live only: there is nothing to watch in a background run.
"""
import argparse
import importlib
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def window_with_view():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == "VIEW_3D":
                return window, area
    return None, None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rig")
    ap.add_argument("--action")
    ap.add_argument("--stop", action="store_true")
    a = ap.parse_args(kn.tool_args())
    if not kn.is_live():
        raise SystemExit("play_action.py only does something in a live Blender window (use --live).")
    window, area = window_with_view()
    if window is None:
        raise SystemExit("No 3D viewport is open in Blender to play in.")
    playing = window.screen.is_animation_playing
    scene = bpy.context.scene

    if a.stop:
        if playing:
            with bpy.context.temp_override(window=window, screen=window.screen, area=area):
                bpy.ops.screen.animation_cancel(restore_frame=False)
        kn.report("Playback", {"playing": False})
        return

    if not a.rig or not a.action:
        raise SystemExit("Pass --rig and --action (or --stop).")
    rig = kn.get_object(a.rig, "ARMATURE")
    action = bpy.data.actions.get(a.action)
    if action is None:
        raise SystemExit(f"No action named '{a.action}'. Actions: {sorted(x.name for x in bpy.data.actions)}")
    kn.zero_controls(rig)
    rig.animation_data_create()
    rig.animation_data.action = action
    slots = getattr(action, "slots", None)
    if slots and hasattr(rig.animation_data, "action_slot") and rig.animation_data.action_slot is None:
        rig.animation_data.action_slot = slots[0]
    first, last = (int(round(f)) for f in action.frame_range)
    # a loop's last frame is its first frame again: leave it out, or every time round one pose shows twice
    curves = getattr(action, "fcurves", None) or [fc for layer in getattr(action, "layers", []) for strip in layer.strips
                                                  for bag in getattr(strip, "channelbags", []) for fc in bag.fcurves]
    loops = last - first > 1 and bool(curves) and all(abs(fc.evaluate(first) - fc.evaluate(last)) < 1e-4 for fc in curves)         and any(abs(fc.evaluate(first) - fc.evaluate(first + 1)) > 1e-4 for fc in curves)
    scene.frame_start, scene.frame_end = first, (last - 1 if loops else last)
    scene.frame_set(first)
    if not playing:
        with bpy.context.temp_override(window=window, screen=window.screen, area=area):
            bpy.ops.screen.animation_play()
    kn.report("Playback", {"rig": rig.name, "action": action.name, "frames": f"{scene.frame_start}-{scene.frame_end}", "loops": loops,
                           "frames_per_second": scene.render.fps, "playing": window.screen.is_animation_playing,
                           "to_stop": "press Space in the viewport, or run play_action.py --stop"})


main()
