"""A Claude Code PreToolUse hook (Bash) that reminds a session to run kn_check before it commits or opens a PR.

It looks only at Bash commands that run `git commit` or `gh pr create` in a Krytical Nova II checkout, and asks
kn_check.py --state (a hash of the files, about 2 seconds) whether the last passing `kn_check --quick` still matches
the files. Then:

- the stamp matches: nothing is said;
- the change is high risk and the last full kn_check run, on exactly these files, failed: the command is refused,
  with the failed checks and the line to run;
- anything else (no run yet, files changed since, a medium or low change that failed): the command goes ahead, and
  the session gets one warning line with the reason and the line to run.

It never blocks because of its own trouble: if it cannot check, the command goes ahead with a note saying why.
Commits from the owner's own terminal are never seen (it is a Claude Code hook, not a git hook).

Test it by feeding it what Claude Code sends:
    echo '{"tool_name":"Bash","tool_input":{"command":"git commit -m x"},"cwd":"C:/path/to/checkout"}' | python kn_commit_hook.py
"""
import json
import os
import re
import shlex
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GATED = re.compile(r"(?:^|[;&|(]\s*|\s)(?:git(?:\s+-C\s+(\"[^\"]+\"|'[^']+'|\S+))?\s+commit\b|gh\s+pr\s+create\b)")
CD = re.compile(r"(?:^|[;&|]\s*)cd\s+(\"[^\"]+\"|'[^']+'|\S+)")
KN_CHECK = os.path.join("Assets", "_Game", "Tools", "Editor", "Architecture", "Scripts~", "kn_check.py")


def unquote(s):
    try:
        return shlex.split(s)[0]
    except ValueError:
        return s.strip("\"'")


def say(context=None, deny=None):
    out = {"hookEventName": "PreToolUse"}
    if deny:
        out["permissionDecision"] = "deny"
        out["permissionDecisionReason"] = deny
    elif context:
        out["additionalContext"] = context
    print(json.dumps({"hookSpecificOutput": out}))
    return 0


def checkout_of(command, cwd):
    """The checkout the command commits in: git -C <dir>, else the last cd before it, else the session's folder."""
    m = GATED.search(command)
    folder = unquote(m.group(1)) if m and m.group(1) else None
    if not folder:
        cds = [unquote(c) for c in CD.findall(command[:m.start()] if m else command)]
        folder = cds[-1] if cds else cwd
    if folder and not os.path.isabs(folder):
        folder = os.path.join(cwd or ".", folder)
    top = subprocess.run(["git", "-C", folder or ".", "rev-parse", "--show-toplevel"], capture_output=True, text=True)
    return os.path.normpath(top.stdout.strip()) if top.returncode == 0 else None


def main():
    try:
        event = json.load(sys.stdin)
    except ValueError:
        return 0
    command = (event.get("tool_input") or {}).get("command") or ""
    if event.get("tool_name") != "Bash" or not GATED.search(command):
        return 0
    try:
        checkout = checkout_of(command, event.get("cwd"))
        if not checkout or not os.path.exists(os.path.join(checkout, KN_CHECK)):
            return 0   # not a Krytical Nova II checkout
        script = os.path.join(checkout, KN_CHECK)
        # an older kn_check.py (a branch from before --state) ignores the unknown option and runs a full --quick,
        # compile included: never start that from a hook, only say so
        with open(script, encoding="utf-8", errors="replace") as f:
            if '"--state"' not in f.read():
                return say(context="kn_check commit reminder skipped: this checkout's kn_check.py is older than "
                                   "--state (merge main to get it); run kn_check.py --quick yourself before committing.")
        p = subprocess.run([sys.executable, script, "--checkout", checkout, "--state"],
                           capture_output=True, text=True, timeout=20)
        state = json.loads(p.stdout.strip().splitlines()[-1])
    except Exception as e:   # never block on the hook's own trouble, but say so
        return say(context=f"kn_check commit reminder could not check this change ({type(e).__name__}: {e}); "
                           f"run kn_check.py --quick yourself before committing.")

    if state.get("stamp_ok"):
        return 0
    run = f'python "{os.path.join(checkout, KN_CHECK)}"'
    last = state.get("last") or {}
    risk = state.get("risk") or "unknown"
    if last.get("tree") == state.get("tree") and last.get("failed"):
        failed = ", ".join(last["failed"])
        if risk == "high":
            return say(deny=f"kn_check --quick failed on exactly these files ({failed}) and this is a high-risk change "
                            f"(saves, economy, accounts or authority). Fix it, then run {run} --only {last['failed'][0]}.")
        return say(context=f"kn_check warning: --quick failed on these files ({failed}); risk {risk}. "
                           f"Fix it or say why in the PR. Re-run one with {run} --only {last['failed'][0]}.")
    why = "has not run on this change" if not state.get("stamp_time") else \
        f"passed at {state['stamp_time']}, but files changed since"
    return say(context=f"kn_check warning: kn_check --quick {why} (risk {risk}). Run {run} before committing "
                       f"(under 2 minutes; only the checks the change needs).")


if __name__ == "__main__":
    sys.exit(main())
