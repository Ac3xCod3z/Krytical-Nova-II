"""Let Claude drive this Blender window. Run once per session: Scripting tab > Open > this file > Run Script.

While it runs, Blender looks twice a second for a job left by "run_blender.py --live" and runs that tool
on the open file. Only the tools that sit next to this file can be run. Nothing is saved for you, and a
tool's changes cannot be undone with Ctrl+Z, so save before asking for one. Restart Blender to stop it.
"""
import contextlib
import io
import json
import runpy
import sys
import tempfile
import traceback
from pathlib import Path

import bpy

def _tools_folder():
    """Where this file really is. Run from Blender's text editor, __file__ points inside the .blend."""
    candidates = []
    if "__file__" in globals():
        candidates.append(Path(__file__))
    candidates += [Path(bpy.path.abspath(t.filepath)) for t in bpy.data.texts
                   if t.filepath and Path(t.filepath).name == "live_bridge.py"]
    for path in candidates:
        if path.is_file() and (path.parent / "run_blender.py").is_file():
            return path.resolve().parent
    raise RuntimeError("Open live_bridge.py from the skill's scripts folder (Text > Open), then Run Script.")


HERE = _tools_folder()
BRIDGE_DIR = Path(tempfile.gettempdir()) / "kn_blender_bridge"
INBOX, OUTBOX = BRIDGE_DIR / "inbox", BRIDGE_DIR / "outbox"


def run_job(job_path):
    """Run one job file and write its result. Returns the result dict."""
    result = {"ok": False, "output": ""}
    buffer = io.StringIO()
    old_argv = sys.argv
    try:
        job = json.loads(job_path.read_text(encoding="utf-8"))
        tool = (HERE / job["tool"]).resolve()
        if tool.parent != HERE or tool.suffix != ".py" or not tool.is_file():
            raise RuntimeError(f"Not a synty-blender tool: {job['tool']}")
        sys.argv = ["blender", "--"] + [str(x) for x in job.get("argv", [])]
        for cached in ("kn_common", "kn_fbx"):      # tools must see the helpers as they are on disk now
            sys.modules.pop(cached, None)
        with contextlib.redirect_stdout(buffer):
            try:
                runpy.run_path(str(tool), run_name="__main__")
                result["ok"] = True
            except SystemExit as stop:
                result["ok"] = stop.code in (None, 0)
                if isinstance(stop.code, str):
                    print(stop.code)
    except Exception:
        buffer.write(traceback.format_exc())
    finally:
        sys.argv = old_argv
        result["output"] = buffer.getvalue()
        job_path.unlink(missing_ok=True)
        tmp = OUTBOX / (job_path.stem + ".tmp")
        tmp.write_text(json.dumps(result), encoding="utf-8")
        tmp.replace(OUTBOX / (job_path.stem + ".json"))
    return result


def tick():
    try:
        (BRIDGE_DIR / "alive").write_text("1")
        for job_path in sorted(INBOX.glob("*.json")):
            run_job(job_path)
    except Exception:
        traceback.print_exc()
    return 0.5


def start():
    INBOX.mkdir(parents=True, exist_ok=True)
    OUTBOX.mkdir(parents=True, exist_ok=True)
    old = bpy.app.driver_namespace.get("kn_bridge")
    if old and bpy.app.timers.is_registered(old):
        bpy.app.timers.unregister(old)
    bpy.app.driver_namespace["kn_bridge"] = tick
    bpy.app.timers.register(tick, first_interval=0.5, persistent=True)
    print(f"synty-blender live bridge is listening ({BRIDGE_DIR}).")


if __name__ == "__main__":
    start()
