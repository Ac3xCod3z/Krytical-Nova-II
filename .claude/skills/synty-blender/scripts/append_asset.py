"""Bring a finished asset from its own .blend into the open scene (a copy; the asset's file is not changed).

  run_blender.py --live append_asset.py --from "G:/My Drive/KryticalNova/Art/Blender/Pets/KitsuneSpirit/SM_Pet_KitsuneSpirit_S1.blend"

Brings the meshes, the rig that drives them, the rig's control shapes (kept in a hidden collection) and its
actions. Left behind: "<name>_source" originals (they carry the heavy scan texture) and metarigs.
It only adds things to the open scene: no file is written and no scene is created or removed.
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
    ap.add_argument("--from", dest="source", required=True)
    ap.add_argument("--replace", action="store_true",
                    help="first remove the copy of this asset that is already in the scene (objects, control "
                         "shapes and actions of the same names)")
    a = ap.parse_args(kn.tool_args())
    path = Path(a.source)
    if not path.is_absolute():
        path = kn.project_root() / path
    if not path.is_file():
        raise SystemExit(f"No such file: {path}")
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        wanted = [n for n in src.objects if not n.endswith("_source") and not n.endswith("_Metarig")]
        clash = [n for n in wanted if n in bpy.data.objects and not n.startswith("WGT-")]
        if a.replace:
            for n in wanted:
                old = bpy.data.objects.get(n)
                if old is not None:
                    bpy.data.objects.remove(old)
            for n in src.actions:
                old = bpy.data.actions.get(n)
                if old is not None:
                    bpy.data.actions.remove(old)
            old = bpy.data.collections.get("WGTS_" + path.stem)
            if old is not None and not old.objects:
                bpy.data.collections.remove(old)
            clash = []
        if clash:
            raise SystemExit(f"Already in this scene: {clash}. Delete or rename them first; nothing was added.")
        dst.objects = wanted
        dst.actions = list(src.actions)
        dst.texts = [t for t in src.texts if t.endswith("_ui.py") or t == "rig_ui.py"]

    scene_col = bpy.context.view_layer.active_layer_collection.collection
    widgets = None
    shown, hidden = [], 0
    for ob in dst.objects:
        if ob is None:
            continue
        if ob.name.startswith("WGT-"):
            if widgets is None:
                widgets = bpy.data.collections.new("WGTS_" + path.stem)
                scene_col.children.link(widgets)
            widgets.objects.link(ob)
            hidden += 1
        else:
            scene_col.objects.link(ob)
            shown.append(ob.name)
    if widgets is not None:
        widgets.hide_viewport = True
        widgets.hide_render = True
    for act in dst.actions:
        if act is not None:
            act.use_fake_user = True

    kn.report("Appended", {"from": str(path), "objects": shown, "control_shapes_hidden": hidden,
                           "actions": sorted(a2.name for a2 in dst.actions if a2 is not None),
                           "note": "To use Rigify's own panel on this rig, open the asset's file instead: the panel "
                                   "comes from a script that only runs when its file is opened."})


main()
