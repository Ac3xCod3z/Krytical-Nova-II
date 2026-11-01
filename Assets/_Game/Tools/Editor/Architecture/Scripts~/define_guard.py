"""Warns (never fails) when the scripting defines moved, because a define change recompiles every script (60-80 s).

The Sentis package (com.unity.ai.inference, AnalyticsDefineManager) adds SENTIS_ANALYTICS_ENABLED to the Standalone
defines when Editor analytics are on and takes it off when they are off, so each switch between an Editor window and a
batch Unity used to change the defines and recompile everything. FORCE_SENTIS_ANALYTICS in ProjectSettings (owner,
31 Oct 2026) pins it. This guard says when:
  - the Standalone or Server define line lacks FORCE_SENTIS_ANALYTICS,
  - the define lines on disk differ from the committed ones (something rewrote them),
  - a Unity log says "Requested script compilation because: Define symbols changed".

    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/define_guard.py"            this checkout
    python ".../define_guard.py" --checkout C:/path/to/worktree

kn_check.py (organization step) and compile_check.py print its warnings. Exits 0 always.
"""
import glob
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
SETTINGS = "ProjectSettings/ProjectSettings.asset"
PINNED = "FORCE_SENTIS_ANALYTICS"
TARGETS = ("Standalone", "Server")
CHANGED = "Requested script compilation because: Define symbols changed"
LOG_DAYS = 2   # older logs were read before; only recent ones are news


def define_lines(text):
    """{target: define line} of the scriptingDefineSymbols block."""
    block = re.search(r"^  scriptingDefineSymbols:\r?\n((?:    .*\r?\n)+)", text, re.M)
    lines = {}
    if block:
        for line in block.group(1).splitlines():
            name, _, value = line.strip().partition(": ")
            lines[name] = value.strip()
    return lines


def recent_logs(checkout):
    local = os.environ.get("LOCALAPPDATA", "")
    candidates = [os.path.join(local, "Unity", "Editor", "Editor.log"), os.path.join(local, "Unity", "Editor", "Editor-prev.log")]
    candidates += glob.glob(os.path.join(checkout, "Builds", "Release", "*.log"))
    candidates += glob.glob(os.path.join(checkout, "Builds", "Release", "publish-run", "*.log"))
    since = time.time() - LOG_DAYS * 86400
    return [p for p in candidates if os.path.isfile(p) and os.path.getmtime(p) >= since]


def check(checkout):
    warnings = []
    path = os.path.join(checkout, SETTINGS)
    if not os.path.isfile(path):
        return warnings
    with open(path, encoding="utf-8", errors="replace") as f:
        now = define_lines(f.read())
    for target in TARGETS:
        if PINNED not in now.get(target, "").split(";"):
            warnings.append(f"{SETTINGS}: the {target} defines lack {PINNED}; the Sentis package will flip "
                            "SENTIS_ANALYTICS_ENABLED between Editor and batch runs and recompile everything each time.")
    committed = subprocess.run(["git", "-C", checkout, "show", "HEAD:" + SETTINGS], capture_output=True,
                               text=True, encoding="utf-8", errors="replace")
    if committed.returncode == 0:
        was = define_lines(committed.stdout)
        for target in TARGETS:
            if now.get(target) != was.get(target):
                warnings.append(f"{SETTINGS}: the {target} define line differs from the committed one (a package or "
                                f"tool rewrote it): now '{now.get(target)}', committed '{was.get(target)}'.")
    for log in recent_logs(checkout):
        try:
            with open(log, encoding="utf-8", errors="replace") as f:
                count = sum(1 for line in f if CHANGED in line)
        except OSError:
            continue
        if count:
            warnings.append(f"{log}: {count} full recompile(s) because the define symbols changed; find what changed "
                            "them (the define lines above, or a package's [InitializeOnLoad]).")
    return warnings


if __name__ == "__main__":
    args = sys.argv[1:]
    checkout = os.path.normpath(args[args.index("--checkout") + 1] if "--checkout" in args else os.path.join(HERE, *[".."] * 6))
    found = check(checkout)
    for line in found:
        print("WARN " + line)
    print(f"define guard: {len(found)} warning(s)")
    sys.exit(0)
