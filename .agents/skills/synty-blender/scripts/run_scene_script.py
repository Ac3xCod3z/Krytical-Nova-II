"""Run a part's own build script on the open scene, so a rebuild shows up in a live Blender window.

  run_blender.py --live run_scene_script.py --script Assets/_ArtSource/Blender~/Sidekick/build_mantle_blockout.py
  run_blender.py --blend FILE.blend run_scene_script.py --script <same>

Only scripts kept under the project's Assets/_ArtSource folder are run: that is where a part's build script
lives, next to its .blend.
"""
import argparse
import runpy
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--script", required=True)
    a = ap.parse_args(kn.tool_args())
    root = kn.project_root()
    script = Path(a.script)
    script = (script if script.is_absolute() else root / script).resolve()
    allowed = (root / "Assets" / "_ArtSource").resolve()
    if allowed not in script.parents or script.suffix != ".py" or not script.is_file():
        raise SystemExit(f"Not a build script under {allowed}: {script}")
    runpy.run_path(str(script), run_name="__main__")
    kn.save_blend()
    kn.report("Scene script", {"script": str(script)})


main()
