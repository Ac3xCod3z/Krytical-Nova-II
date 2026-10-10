#!/usr/bin/env python3
"""Run one of the synty-blender tools in Blender.

Headless (default):  python run_blender.py [--blend FILE.blend] TOOL.py [tool args...]
Live session:        python run_blender.py --live TOOL.py [tool args...]

Headless opens the .blend, runs the tool and saves the file. Live hands the tool to a Blender window
that is running live_bridge.py, so the artist sees the change at once (nothing is saved for them).
"""
import json
import os
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
BRIDGE_DIR = Path(tempfile.gettempdir()) / "kn_blender_bridge"


def find_blender():
    env = os.environ.get("BLENDER_EXE")
    if env and Path(env).is_file():
        return env
    roots = [Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Blender Foundation",
             Path("/Applications/Blender.app/Contents/MacOS"), Path("/usr/bin"), Path("/usr/local/bin")]
    found = []
    for root in roots:
        if root.is_dir():
            found += [p for p in root.glob("**/blender.exe")] + [p for p in root.glob("blender") if p.is_file()]
    if not found:
        sys.exit("Blender not found. Set BLENDER_EXE to blender.exe.")
    return str(sorted(found)[-1])  # newest version folder


def main():
    args = sys.argv[1:]
    live, blend = False, None
    while args and args[0] in ("--live", "--blend"):
        if args[0] == "--live":
            live, args = True, args[1:]
        else:
            blend, args = args[1], args[2:]
    if not args:
        sys.exit(__doc__)
    tool = (HERE / args[0]).resolve()
    if tool.parent != HERE or not tool.is_file():
        sys.exit(f"Unknown tool '{args[0]}'. Tools: {sorted(p.name for p in HERE.glob('*.py'))}")
    tool_args = args[1:]

    if live:
        inbox, outbox = BRIDGE_DIR / "inbox", BRIDGE_DIR / "outbox"
        if not (BRIDGE_DIR / "alive").is_file() or time.time() - (BRIDGE_DIR / "alive").stat().st_mtime > 10:
            sys.exit("No live Blender is listening. In Blender: Scripting tab > Open > "
                     f"{HERE / 'live_bridge.py'} > Run Script. Or run headless with --blend.")
        job = uuid.uuid4().hex[:12]
        tmp = inbox / (job + ".tmp")
        tmp.write_text(json.dumps({"tool": tool.name, "argv": tool_args}), encoding="utf-8")
        tmp.rename(inbox / (job + ".json"))
        result = outbox / (job + ".json")
        deadline = time.time() + 600
        while time.time() < deadline:
            if result.is_file():
                data = json.loads(result.read_text(encoding="utf-8"))
                print(data.get("output", ""))
                result.unlink()
                sys.exit(0 if data.get("ok") else 1)
            time.sleep(0.25)
        sys.exit("Timed out waiting for the live Blender session.")

    cmd = [find_blender(), "--background", "--factory-startup"]
    if tool.name.startswith("pet_"):
        # A Rigify rig drives its bones with small scripts stored in the .blend; without them a baked
        # animation comes out wrong. Only the pet tools, which open the artist's own rig files, allow them.
        cmd.append("--enable-autoexec")
    if blend:
        if not Path(blend).is_file():
            sys.exit(f"No such file: {blend}")
        cmd.append(blend)
    cmd += ["--python-exit-code", "1", "--python", str(tool), "--"] + tool_args
    sys.exit(subprocess.call(cmd))


if __name__ == "__main__":
    main()
