"""Tighten a rigged pet's weights without rigging it again: appendage bones stop pulling on the body.

  run_blender.py --blend FILE.blend pet_tighten.py --object SM_Pet_X_S1 --landmarks rig_x.json
  run_blender.py --live              pet_tighten.py --object SM_Pet_X_S1 --landmarks rig_x.json

Every bone under "extra" in the landmarks file (ears, horns, fins: each has a parent) keeps only the
vertices that lie along its own length; the rest go to its parent. This is what pet_rig.py does when it
binds, so run this only on a pet rigged before that was added, or after changing "extra_start" /
"extra_full" in the landmarks file. It changes vertex groups and nothing else: the rig and the animations
stay as they are.
"""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object", required=True)
    ap.add_argument("--landmarks", required=True)
    a = ap.parse_args(kn.tool_args())
    ob = kn.get_object(a.object)
    rig = kn.bound_rig(ob)
    spec = json.loads(Path(a.landmarks).read_text(encoding="utf-8"))
    start, full = float(spec.get("extra_start", -0.05)), float(spec.get("extra_full", 0.2))
    done, problems = {}, []
    for name, extra in spec.get("extra", {}).items():
        if "parent" not in extra:
            continue
        names = [name] + ([name[:-2] + ".R"] if name.endswith(".L") else [])
        for n in names:
            done[n] = kn.keep_to_own_length(ob, rig, n, extra["parent"], start, full)
    if not done:
        problems.append("The landmarks file has no bones under \"extra\" with a parent.")
    kn.report("Weights tightened", {"object": ob.name, "vertices_changed_per_bone": done,
                                    "kept_from": start, "full_from": full}, problems)
    kn.save_blend()


main()
