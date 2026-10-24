"""Turn on one of Blender's bundled add-ons and keep it on for later sessions.

  run_blender.py --live enable_addon.py --module rigify      (in the open window, and saved to preferences)

Only add-ons that ship with Blender are accepted; nothing is downloaded or installed.
"""
import argparse
import sys
from pathlib import Path

import addon_utils
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", required=True)
    a = ap.parse_args(kn.tool_args())
    bundled = Path(bpy.utils.system_resource("SCRIPTS")) / "addons_core"
    found = next((m for m in addon_utils.modules() if m.__name__ == a.module), None)
    if found is None or bundled not in Path(found.__file__).resolve().parents:
        have = sorted(m.__name__ for m in addon_utils.modules() if bundled in Path(m.__file__).resolve().parents)
        raise SystemExit(f"'{a.module}' is not an add-on that ships with Blender. Bundled: {have}")
    was_on = addon_utils.check(a.module)[1]
    if not was_on:
        addon_utils.enable(a.module, default_set=True, persistent=True)
    bpy.ops.wm.save_userpref()
    kn.report("Add-on", {"module": a.module, "already_on": was_on, "on_now": addon_utils.check(a.module)[1],
                         "saved_to_preferences": True, "blender": bpy.app.version_string})


main()
