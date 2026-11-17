"""compile_check's compile-slot guard and shared lock (16 Nov 2026). Runs in a second, compiles nothing.

    python test_compile_slot_guard.py

A slot held by another session refuses the compile (exit 3) before any dotnet runs; the lock lives at the fixed path (here the
KN_COMPILE_LOCK override), never under the shell's TEMP.
"""
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "compile_check.py")


def run(slot_line, *extra, me=None):
    folder = tempfile.mkdtemp(prefix="kn-slot-test-")
    slot = os.path.join(folder, "compile-slot.txt")
    with open(slot, "w", encoding="utf-8") as f:
        f.write(slot_line + "\nolder lines\n")
    env = dict(os.environ, KN_COMPILE_SLOT=slot, KN_COMPILE_LOCK=os.path.join(folder, "locks", "compile.lock"))
    env.pop("KN_SESSION", None)
    if me:
        env["KN_SESSION"] = me
    r = subprocess.run([sys.executable, SCRIPT, *extra], env=env, capture_output=True, text=True, timeout=60)
    return r, folder


def main():
    failures = []

    r, _ = run("warbots-spawn-b (local_7f75001e) HOLDS since 21:57 16 Nov: #339 EditMode on b18-19", me="crossfire-b")
    if r.returncode != 3 or "warbots-spawn-b holds the slot" not in r.stdout:
        failures.append(f"another session's hold must refuse with exit 3, got {r.returncode}: {r.stdout[-300:]}")

    r, _ = run("warbots-spawn-b (local_7f75001e) HOLDS since 21:57", "--as", "crossfire-b")
    if r.returncode != 3:
        failures.append(f"--as naming someone else must refuse, got {r.returncode}")

    r, _ = run("warbots-spawn-b (local_7f75001e) HOLDS since 21:57")
    if r.returncode != 3 or "--as" not in r.stdout:
        failures.append(f"an unnamed caller must be refused and told to name itself, got {r.returncode}: {r.stdout[-300:]}")

    # pure rules, by import of the two functions' text (the module itself runs a compile on import)
    src = open(SCRIPT, encoding="utf-8").read()
    start = src.index("def slot_holder(")
    end = src.index('if "--ignore-slot" not in sys.argv')
    ns = {}
    exec(src[start:end], ns)
    holder, refuses = ns["slot_holder"], ns["slot_refuses"]
    checks = [
        (holder("FREE since 22:10"), None),
        (holder(""), None),
        (holder("crossfire-b (local_53d2243f) HOLDS since 23:20: compile + EditMode"), "crossfire-b"),
        (holder("2026-11-16 18:00 DONE warbots: EditMode 44/0"), None),
        (refuses("crossfire-b (local_x) HOLDS since 23:20", "crossfire-b"), None),
        (refuses("FREE", ""), None),
        (refuses("warbots-spawn-b (local_y) HOLDS", "crossfire-b") is not None, True),
    ]
    for i, (got, want) in enumerate(checks):
        if got != want:
            failures.append(f"pure check {i}: got {got!r}, want {want!r}")

    if failures:
        print("FAILED:\n  " + "\n  ".join(failures))
        return 1
    print(f"compile slot guard: {3 + len(checks)} checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
