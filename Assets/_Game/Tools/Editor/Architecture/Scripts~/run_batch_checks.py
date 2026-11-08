"""Runs Validate Project Organization and Check Icons of a checkout in a batch Unity, without the owner's Editor.

    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/run_batch_checks.py"                    this checkout
    python ".../run_batch_checks.py" --checkout C:/path/to/worktree
    python ".../run_batch_checks.py" --read                           only read the last results again

The Unity side is KryticalNova.EditorTools.Checks.BatchChecks.Run (Tools/Editor/Checks/BatchChecks.cs): the same
checks as the two menu commands, with no dialog. Like run_editmode_tests.py it needs the checkout closed in the Editor,
runs below normal priority through the Accelerator, inside its own job object (own_tree.py: asked to quit below 2 GB
free for 15 s, forced after 60 s or at once below 1 GB), and shuts the dotnet build servers down after.

Results: <checkout>/Logs/batch-checks.txt (check, severity, path, message; tab-separated, ending in "done"), log:
<checkout>/Logs/batch-checks.log. Prints every finding; exits 1 on an error, 2 when the run did not finish or a check
broke.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import own_tree   # noqa: E402
import run_editmode_tests as tests   # noqa: E402  (shares --checkout, the Unity path and the open-Editor check)

CHECKOUT = tests.CHECKOUT
RESULTS = os.path.join(CHECKOUT, "Logs", "batch-checks.txt")
LOG = os.path.join(CHECKOUT, "Logs", "batch-checks.log")
METHOD = "KryticalNova.EditorTools.Checks.BatchChecks.Run"


def run():
    if tests.is_open():
        sys.exit(f"{CHECKOUT} is open in a Unity Editor. Close it, or run the checks on a worktree.")
    os.makedirs(os.path.dirname(RESULTS), exist_ok=True)
    if os.path.exists(RESULTS):
        os.remove(RESULTS)
    cmd = [tests.unity_exe(), "-batchmode", "-projectPath", CHECKOUT, "-executeMethod", METHOD, "-logFile", LOG,
           "-buildTarget", "Win64", "-EnableCacheServer", "-cacheServerEndpoint", tests.ACCELERATOR,
           "-cacheServerEnableDownload", "true", "-cacheServerEnableUpload", "true",
           "-cacheServerWaitForConnection", "10000", "-cacheServerEnableImportResultCaching"]
    print("Running the batch checks in", CHECKOUT, "(log:", LOG + ")", flush=True)
    try:
        code, why = own_tree.run(cmd)
    finally:
        own_tree.shut_build_servers()
    if why:
        if os.path.exists(RESULTS):
            os.remove(RESULTS)
        sys.exit(f"STOPPED: {why}. The checks did not finish.")
    print("Unity exited with", code)


def parse(path=None):
    """[(check, severity, path, message)] from a finished run; None when the run did not finish."""
    path = path or RESULTS
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    if not lines or lines[-1] != "done":
        return None
    return [tuple((line.split("\t") + ["", "", "", ""])[:4]) for line in lines[:-1]]


def read():
    findings = parse()
    if findings is None:
        sys.exit("No finished results: the run did not finish (compile errors stop it). See " + LOG)
    for check, severity, path, message in findings:
        print(f"{check} {severity.upper()} {path + ': ' if path else ''}{message[:500]}")
    for check in ("organization", "icons"):
        errors = sum(1 for c, s, _, _ in findings if c == check and s == "error")
        print(f"{check}: {errors} error(s), {sum(1 for c, *_ in findings if c == check) - errors} other finding(s)")
    if any(s == "broke" for _, s, _, _ in findings):
        return 2
    return 1 if any(s == "error" for _, s, _, _ in findings) else 0


if __name__ == "__main__":
    if "--read" not in sys.argv[1:]:
        run()
    sys.exit(read())
