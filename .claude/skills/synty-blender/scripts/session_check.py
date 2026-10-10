"""Report the state of the open Blender session and mend its temporary folder if it has gone missing.

  run_blender.py --live session_check.py

Blender keeps a per-session temp folder; installing add-ons, rendering and some importers fail with odd
errors ("cookie doesn't exist") when something has removed it. This puts it back. It also makes a user
add-ons folder that was created after Blender started loadable ("No module named ...").
"""
import os
import sys
from pathlib import Path

import addon_utils
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def main():
    temp = bpy.app.tempdir
    existed = os.path.isdir(temp)
    if not existed:
        os.makedirs(temp, exist_ok=True)
    user_addons = Path(bpy.utils.user_resource("SCRIPTS", path="addons"))
    # Blender only adds the user add-ons folder to its module path at startup, and only if the folder
    # existed then. A folder made while Blender is running is listed but cannot be loaded until this is done.
    on_path = any(Path(p) == user_addons for p in sys.path if p)
    if user_addons.is_dir() and not on_path:
        sys.path.append(str(user_addons))
    kn.report("Session", {
        "file": bpy.data.filepath, "unsaved_changes": bpy.data.is_dirty, "blender": bpy.app.version_string,
        "temp_folder": temp, "temp_folder_was_missing": not existed, "temp_folder_exists_now": os.path.isdir(temp),
        "user_addons_folder": str(user_addons), "user_addons_folder_was_loadable": on_path,
        "user_addons_folder_loadable_now": any(Path(p) == user_addons for p in sys.path if p),
        "user_addons_on_disk": sorted(p.name for p in user_addons.iterdir()) if user_addons.is_dir() else [],
        "enabled_addons": sorted(m for m in bpy.context.preferences.addons.keys()),
    })


main()
