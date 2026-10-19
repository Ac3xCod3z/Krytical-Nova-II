"""Runs the EditMode tests of a checkout in a batch Unity, without the owner's Editor, and reads out the results.

The checkout must not be open in an Editor (Unity refuses a project another Editor holds). Use it on a worktree:
the first run imports the whole project into that worktree's Library (about 10 to 25 minutes, less with the
Accelerator's shared import results); later runs import only what changed.

    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/run_editmode_tests.py"                 this checkout
    python ".../run_editmode_tests.py" --checkout C:/path/to/worktree
    python ".../run_editmode_tests.py" --filter "KryticalNova.Items"     only tests whose full name matches
    python ".../run_editmode_tests.py" --read                           only read the last results again

Unity runs below normal priority and uses the Unity Accelerator at 127.0.0.1:10443 like the background publisher.
The results go to <checkout>/Logs/editmode-results.xml and the log to <checkout>/Logs/editmode-tests.log. Prints the
totals and every failure with its message; exits 1 when a test failed or the run did not finish.
"""
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]


def arg(name, default=None):
    return args[args.index(name) + 1] if name in args else default


CHECKOUT = os.path.normpath(arg("--checkout", os.path.join(HERE, *[".."] * 6)))
RESULTS = os.path.join(CHECKOUT, "Logs", "editmode-results.xml")   # not Temp: Unity empties Temp when it quits
LOG = os.path.join(CHECKOUT, "Logs", "editmode-tests.log")
ACCELERATOR = "127.0.0.1:10443"


def unity_exe():
    with open(os.path.join(CHECKOUT, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8") as f:
        version = re.search(r"m_EditorVersion:\s*(\S+)", f.read()).group(1)
    path = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "Unity", "Hub", "Editor", version, "Editor", "Unity.exe")
    if not os.path.exists(path):
        sys.exit(f"Unity {version} is not installed at {path}")
    return path


def is_open():
    lock = os.path.join(CHECKOUT, "Temp", "UnityLockfile")
    if not os.path.exists(lock):
        return False
    try:
        os.remove(lock)   # a lock left by an Editor that has quit can be removed; an open Editor holds it
        return False
    except OSError:
        return True


def run():
    if is_open():
        sys.exit(f"{CHECKOUT} is open in a Unity Editor. Close it, or run the tests on a worktree.")
    os.makedirs(os.path.dirname(RESULTS), exist_ok=True)
    if os.path.exists(RESULTS):
        os.remove(RESULTS)
    cmd = [unity_exe(), "-batchmode", "-projectPath", CHECKOUT, "-runTests", "-testPlatform", "EditMode",
           "-testResults", RESULTS, "-logFile", LOG, "-buildTarget", "Win64",
           "-EnableCacheServer", "-cacheServerEndpoint", ACCELERATOR, "-cacheServerEnableDownload", "true",
           "-cacheServerEnableUpload", "true", "-cacheServerWaitForConnection", "10000",
           "-cacheServerEnableImportResultCaching"]
    if arg("--filter"):
        cmd += ["-testFilter", arg("--filter")]
    print("Running EditMode tests in", CHECKOUT, "(log:", LOG + ")", flush=True)
    below_normal = 0x00004000 if os.name == "nt" else 0
    code = subprocess.run(cmd, creationflags=below_normal).returncode
    print("Unity exited with", code)


def read():
    if not os.path.exists(RESULTS):
        tail = ""
        if os.path.exists(LOG):
            with open(LOG, encoding="utf-8", errors="replace") as f:
                lines = f.read().splitlines()
            tail = "\n".join(l for l in lines[-400:] if "error" in l.lower() or "exception" in l.lower())[-3000:]
        sys.exit("No results were written: the run did not finish (compile errors stop it). From the log:\n" + tail)
    root = ET.parse(RESULTS).getroot()
    a = root.attrib
    print(f"{a.get('passed', '?')} passed, {a.get('failed', '?')} failed, {a.get('skipped', '?')} skipped, "
          f"{a.get('total', '?')} in all ({float(a.get('duration', 0)):.0f} s)")
    failed = 0
    for case in root.iter("test-case"):
        if case.attrib.get("result") == "Failed":
            failed += 1
            msg = case.find("./failure/message")
            print(f"FAILED {case.attrib.get('fullname')}\n    {(msg.text or '').strip()[:500] if msg is not None else ''}")
    return 1 if failed or a.get("result", "").startswith("Failed") else 0


if "--read" not in args:
    run()
sys.exit(read())
