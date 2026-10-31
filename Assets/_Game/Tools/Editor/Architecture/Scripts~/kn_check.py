"""The gate a change passes before it is committed: runs the project's checks in one go and says which failed.

    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/kn_check.py"                    --quick, this checkout
    python ".../kn_check.py" --checkout C:/path/to/worktree
    python ".../kn_check.py" --only organization,dependencies     some checks only (writes no stamp)
    python ".../kn_check.py" --stamp-ok                           exit 0 when the last full --quick pass still
                                                                  matches the files as they are now

--quick (the default, under 2 minutes, Unity can stay closed) runs only the checks whose files changed against
origin/main (commits of this branch, edits and new files); --all runs every one:
  organization   the file-path rules of Validate Project Organization (organization_check.py); always, under 1 s
  dependencies   dependency_map.py --check: no new uphill link or two-way pair, assets keep their scripts;
                 when scripts, assemblies, prefabs, scenes, assets or .meta files under _Game changed
  compile        compile_check.py: every first-party assembly compiles (dotnet); when code or assemblies changed
  server         compile_check.py --server: the runtime assemblies compile as the dedicated server builds them;
                 when runtime code (not Editor/ or Tests/) or assemblies changed

The compile steps are a dotnet job: on this PC run one heavy Unity or dotnet job at a time. They build with at most
2 MSBuild nodes, and the dotnet build servers are shut down after them.

Findings that were already there when a check joined the gate are listed in known-findings.json beside this file, each
with the reason; they are reported but do not fail the gate. A new finding fails it, and so does a listed one that is
gone (take it off the list, so the list only shrinks). dependency_map.py keeps its own list, baseline.json.

When every --quick check passes, Logs/kn-check-stamp.json records the git tree of the checkout's files at that moment
(tracked and untracked, .gitignore respected). --stamp-ok compares it with the files now; a commit gate uses that.
Exits 1 when a check failed.
"""
import datetime
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]


def arg(name, default=None):
    return args[args.index(name) + 1] if name in args else default


CHECKOUT = os.path.normpath(arg("--checkout", os.path.join(HERE, *[".."] * 6)))
KNOWN = os.path.join(HERE, "known-findings.json")
STAMP = os.path.join(CHECKOUT, "Logs", "kn-check-stamp.json")
DEP_DOC = os.path.join(CHECKOUT, "Assets", "_Game", "_Docs", "Reference", "Dependency-Map.md")
QUICK = ["organization", "dependencies", "compile", "server"]


def load_json(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def git(*cmd, env=None):
    return subprocess.run(["git", "-C", CHECKOUT, *cmd], capture_output=True, text=True, env=env)


def files_tree():
    """The git tree id of the checkout's files as they are now, without touching its real index."""
    index = git("rev-parse", "--git-path", "index").stdout.strip()
    index = index if os.path.isabs(index) else os.path.join(CHECKOUT, index)
    fd, temp = tempfile.mkstemp(prefix="kn-check-index-")
    os.close(fd)
    try:
        if os.path.exists(index):
            shutil.copyfile(index, temp)
        env = dict(os.environ, GIT_INDEX_FILE=temp)
        added = git("add", "-A", env=env)
        if added.returncode != 0:
            return None
        tree = git("write-tree", env=env)
        return tree.stdout.strip() if tree.returncode == 0 else None
    finally:
        os.remove(temp)


# --- the checks: each returns (passed, lines to show) ------------------------------------------------------------------

def run_script(name, *extra):
    cmd = [sys.executable, os.path.join(HERE, name), "--checkout", CHECKOUT, *extra]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines = (p.stdout + p.stderr).strip().splitlines()
    return p.returncode == 0, lines


def check_organization():
    sys.path.insert(0, HERE)
    import organization_check
    errors, warnings = organization_check.check(CHECKOUT)
    known = {k["path"]: k for k in load_json(KNOWN).get("organization", [])}
    found = {p for p, _ in errors}
    new = [(p, m) for p, m in errors if p not in known]
    gone = sorted(p for p in known if p not in found)
    lines = [f"NEW {p}: {m}" for p, m in new]
    lines += [f"GONE {p}: fixed, so take it off known-findings.json" for p in gone]
    lines.append(f"{len(errors)} error(s) ({len(errors) - len(new)} known), {len(warnings)} warning(s)")
    return not new and not gone, lines


def check_dependencies():
    # --check also rewrites the report's tables; put the report back so the gate leaves no change behind.
    before = open(DEP_DOC, "rb").read() if os.path.exists(DEP_DOC) else None
    try:
        return run_script("dependency_map.py", "--check")
    finally:
        if before is not None and open(DEP_DOC, "rb").read() != before:
            with open(DEP_DOC, "wb") as f:
                f.write(before)


CHECKS = {
    "organization": check_organization,
    "dependencies": check_dependencies,
    "compile": lambda: run_script("compile_check.py", "--maxcpu", "2"),
    "server": lambda: run_script("compile_check.py", "--server", "--maxcpu", "2"),
}
COMPILES = ("compile", "server")


def shut_build_servers():
    """dotnet keeps MSBuild and compiler servers alive after a build; stop them so the memory comes back."""
    subprocess.run(["dotnet", "build-server", "shutdown"], capture_output=True, text=True)


def changed_files():
    """Paths changed against where this branch left origin/main: commits, edits and new files. None if unknown."""
    base = git("merge-base", "HEAD", "origin/main")
    if base.returncode != 0:
        return None
    committed = git("diff", "--name-only", base.stdout.strip())
    untracked = git("ls-files", "--others", "--exclude-standard")
    if committed.returncode != 0 or untracked.returncode != 0:
        return None
    return {p for p in (committed.stdout + untracked.stdout).splitlines() if p}


CODE = (".cs", ".asmdef", ".asmref", ".rsp")
HELD = CODE + (".meta", ".prefab", ".unity", ".asset")


def in_game(p):
    return p.startswith(("Assets/_Game/", "Assets/Resources/")) or p == "Assets/DefaultNetworkPrefabs.asset"


# Which changed files each check reads; a check none of them touch is skipped (--all runs it anyway).
NEEDS = {
    "organization": lambda ch: True,
    "dependencies": lambda ch: any(in_game(p) and p.endswith(HELD) for p in ch)
                               or any(p.startswith("Assets/_Game/Tools/Editor/Architecture/Scripts~/") for p in ch),
    "compile": lambda ch: any(p.startswith("Assets/_Game/") and p.endswith(CODE) for p in ch),
    "server": lambda ch: any(p.startswith("Assets/_Game/") and p.endswith(CODE)
                             and (p.endswith((".asmdef", ".asmref")) or ("/Editor/" not in p and "/Tests/" not in p))
                             for p in ch),
}


def stamp_ok():
    stamp = load_json(STAMP)
    tree = files_tree()
    if not stamp:
        print("no kn_check stamp: run kn_check.py --quick")
        return False
    if stamp.get("tree") != tree:
        print(f"files changed since kn_check passed at {stamp.get('time')}: run kn_check.py --quick again")
        return False
    print(f"kn_check {stamp.get('tier')} passed at {stamp.get('time')} on these files")
    return True


def main():
    if "--stamp-ok" in args:
        return 0 if stamp_ok() else 1
    if "--full" in args:
        print("--full (EditMode tests, Check Icons) is not built yet; it comes with known-test-failures.json")
        return 2

    only = arg("--only")
    names = [n.strip() for n in only.split(",")] if only else QUICK
    unknown = [n for n in names if n not in CHECKS]
    if unknown:
        print(f"unknown check(s): {', '.join(unknown)}; the checks are {', '.join(QUICK)}")
        return 2

    tree_before = None if only else files_tree()
    changed = None if only or "--all" in args else changed_files()
    if changed is None and not only and "--all" not in args:
        print("could not work out the changed files against origin/main: running every check")
    elif changed is not None:
        print(f"{len(changed)} file(s) changed against origin/main")
    results, skipped = [], []
    for name in names:
        if changed is not None and not NEEDS[name](changed):
            print(f"--- {name}: skipped, nothing it reads changed")
            skipped.append(name)
            continue
        print(f"--- {name} ...", flush=True)
        start = time.time()
        passed, lines = CHECKS[name]()
        took = time.time() - start
        shown = lines if not passed else lines[-3:]
        for line in shown[-60:]:
            print("    " + line)
        print(f"--- {name}: {'passed' if passed else 'FAILED'} ({took:.0f} s)", flush=True)
        results.append((name, passed))

    if any(n in COMPILES for n, _ in results):
        shut_build_servers()
    failed = [n for n, ok in results if not ok]
    print()
    if failed:
        print(f"kn_check: FAILED {', '.join(failed)}. Re-run one with --only {failed[0]}")
        return 1
    print(f"kn_check: {len(results)} check(s) passed" + (f", {len(skipped)} skipped" if skipped else ""))

    if not only:
        tree_after = files_tree()
        if tree_before is None or tree_before != tree_after:
            print("files changed while the checks ran (another session?): no stamp written; run it again")
            return 1
        os.makedirs(os.path.dirname(STAMP), exist_ok=True)
        with open(STAMP, "w", encoding="utf-8") as f:
            json.dump({"tier": "quick", "tree": tree_after, "head": git("rev-parse", "HEAD").stdout.strip(),
                       "time": datetime.datetime.now().isoformat(timespec="seconds"),
                       "checks": [n for n, _ in results], "skipped": skipped}, f, indent=1)
        print(f"stamp written: {os.path.relpath(STAMP, CHECKOUT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
