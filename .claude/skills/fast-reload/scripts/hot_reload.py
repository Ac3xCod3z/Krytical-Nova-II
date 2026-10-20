"""Hot-reload changed scripts into the running Play session with Fast Script Reload (FSR).

Usage:
    python hot_reload.py --state                 # prints PLAYING / EDIT / BUSY and exits 0
    python hot_reload.py <file.cs> [<file.cs>...] # queues the files with FSR and waits for its verdict

What it does:
    1. Asks the open Editor whether it is in Play mode (never touches Play, never refreshes).
    2. Not playing: prints NOT-PLAYING and exits 2 (compile the normal way instead: AssetDatabase.Refresh).
    3. Playing: queues each file with FastScriptReloadManager.AddFileChangeToProcess and runs
       TriggerReloadForChangedFiles, then reads the project's Logs/Editor.log for FSR's verdict:
       "Hot-reload completed" -> exit 0; a compile error or a failure -> prints it, exit 1; nothing in 30 s -> exit 3.

The FSR file watcher does not work in this project (and its CustomPolling watcher froze the Editor once), so every
change is queued by hand here. Never call AssetDatabase.Refresh while the Editor is playing.
"""
import os
import subprocess
import sys
import tempfile
import time

PROJECT = r"C:\Users\dlaco\Krytical Nova II"
LOG = os.path.join(PROJECT, "Logs", "Editor.log")


def unity_eval(code: str, timeout: int = 25) -> str:
    with tempfile.NamedTemporaryFile("w", suffix=".cs", delete=False, encoding="utf-8") as f:
        f.write(code)
        path = f.name
    try:
        out = subprocess.run(
            ["unity", "command", "--project-path", PROJECT, "--result-only", "eval_file", "--file", path],
            capture_output=True, text=True, timeout=timeout, shell=(os.name == "nt"))
        return (out.stdout or "") + (out.stderr or "")
    except subprocess.TimeoutExpired:
        return "TIMEOUT"
    finally:
        os.unlink(path)


def state() -> str:
    r = unity_eval('return UnityEditor.EditorApplication.isPlaying ? "PLAYING" : '
                   '(UnityEditor.EditorApplication.isCompiling ? "BUSY" : "EDIT");')
    for word in ("PLAYING", "EDIT", "BUSY"):
        if word in r:
            return word
    return "UNKNOWN: " + r.strip()[-300:]


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    now = state()
    if args[0] == "--state":
        print(now)
        return 0
    if now != "PLAYING":
        print(f"NOT-PLAYING ({now}): compile the normal way (AssetDatabase.Refresh) instead.")
        return 2

    files = [os.path.abspath(a).replace("\\", "/") for a in args]
    for f in files:
        if not os.path.isfile(f):
            print(f"missing file: {f}")
            return 1
    start = os.path.getsize(LOG) if os.path.exists(LOG) else 0
    queue = "".join(f'm.AddFileChangeToProcess(@"{f}");' for f in files)
    r = unity_eval("var m = FastScriptReload.Editor.FastScriptReloadManager.Instance;" + queue +
                   'm.TriggerReloadForChangedFiles(); return "queued";')
    if "queued" not in r:
        print("Could not queue with FSR: " + r.strip()[-400:])
        return 1

    deadline = time.time() + 30
    while time.time() < deadline:
        time.sleep(1)
        with open(LOG, "r", encoding="utf-8", errors="replace") as log:
            log.seek(start)
            text = log.read()
        for line in text.splitlines():
            low = line.lower()
            if "hot-reload completed" in low:
                print("HOT-RELOADED: " + line.strip())
                return 0
            if ("compilation error" in low or "error when updating files" in low or "error cs" in low
                    or "won't be compiled" in low or "will not be performed" in low):
                tail = [l for l in text.splitlines() if "error" in l.lower()][:12]
                print("FSR FAILED:\n" + "\n".join(tail))
                return 1
    print("NO VERDICT in 30 s: check the Console; the change is on disk and compiles normally when Play stops.")
    return 3


if __name__ == "__main__":
    sys.exit(main())
