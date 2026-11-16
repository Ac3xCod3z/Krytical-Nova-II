"""Flags a static reset inside a BeforeSceneLoad startup hook (the startup race, 15 Nov 2026).

A built entity server makes its worlds in Entities' own BeforeSceneLoad bootstrap, in no fixed order with our
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] methods. A reset there (`= null`, `= 0`, `.Clear()`...)
can wipe what the server world has just set (HostilePool.BodyOf, PR 313); a blind install can replace it (NetRole, PR 304). The Editor
makes its worlds later and never shows it. The rule: reset statics at SubsystemRegistration; a BeforeSceneLoad hook only adds (its own
handler with -= then +=, or an install into an empty slot). Blind installs (`X.Install(`, `X.Use(`, `X.Slot = new`) are WARN lines.

A line that must stay is marked `// startup-race: ok <why>` (on the line or on the hook's attribute line).

Usage (from the project folder):
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/startup_race.py"          the .cs files changed against origin/main
    python ".../startup_race.py" --all                                                   every .cs file under Assets/_Game
    python ".../startup_race.py" --checkout <folder>                                     another checkout (kn_check passes it)
Exit 1 when a reset is found. Python's standard library only.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]
CHECKOUT = os.path.normpath(args[args.index("--checkout") + 1]) if "--checkout" in args else os.path.normpath(os.path.join(HERE, *[".."] * 6))
GAME = os.path.join(CHECKOUT, "Assets", "_Game")

HOOK = re.compile(r"\[\s*(?:UnityEngine\.)?RuntimeInitializeOnLoadMethod\s*\(\s*(?:UnityEngine\.)?RuntimeInitializeLoadType\.BeforeSceneLoad\s*\)\s*\]")
RESET = re.compile(r"(?<![=!<>+\-*/])=\s*(null|default|0|0f|-1|false|string\.Empty)\s*[;,)]|\.Clear\s*\(\s*\)")
# a local declared and set on the same line (string server = null;, for (int i = 0; ...)) is not a static
LOCAL = re.compile(r"^\s*(?:for\s*\(\s*)?(?:var|bool|int|uint|long|float|double|string|[A-Z]\w*(?:<[^>]*>)?(?:\[\])?)\??\s+[a-z_]\w*\s*=")
INSTALL = re.compile(r"\b[A-Z]\w*\.(Install|Use)\s*\(|\b[A-Z]\w*\.[A-Z]\w*\s*=\s*new\b")
WAIVER = "startup-race: ok"

# Known on main and handled elsewhere: path -> why. Only shrinks.
KNOWN = {
    "Assets/_Game/Gameplay/Persistence/Entities/Runtime/KnPlayHere.cs": "Editor Play-here only: its worlds are made later, by StartPlayHere",
}


def changed_files():
    def git(*cmd):
        return subprocess.run(["git", "-C", CHECKOUT, *cmd], capture_output=True, text=True, encoding="utf-8", errors="replace")
    base = git("merge-base", "HEAD", "origin/main")
    if base.returncode != 0:
        return None
    out = git("diff", "--name-only", "--diff-filter=AM", base.stdout.strip()).stdout + git("ls-files", "--others", "--exclude-standard").stdout
    return sorted({p for p in out.splitlines() if p.startswith("Assets/_Game/") and p.endswith(".cs")})


def all_files():
    found = []
    for folder, dirs, files in os.walk(GAME):
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        for name in files:
            if name.endswith(".cs"):
                found.append(os.path.relpath(os.path.join(folder, name), CHECKOUT).replace("\\", "/"))
    return sorted(found)


def code(line):
    """The line without its // comment (a waiver lives there) and without string contents."""
    line = re.sub(r'"(?:\\.|[^"\\])*"', '""', line)
    return line.split("//", 1)[0]


def hook_bodies(lines):
    """(attribute line index, [(line index, text)]) for each BeforeSceneLoad hook."""
    for i, line in enumerate(lines):
        if not HOOK.search(code(line)):
            continue
        body, depth, opened = [], 0, False
        for j in range(i + 1, min(len(lines), i + 400)):
            text = code(lines[j])
            if not opened and "=>" in text and "{" not in text.split("=>", 1)[0]:
                # an expression-bodied hook: up to its ';'
                k = j
                while k < len(lines):
                    body.append((k, lines[k]))
                    if ";" in code(lines[k]):
                        break
                    k += 1
                break
            if "{" in text:
                opened = True
            if opened:
                body.append((j, lines[j]))
                depth += text.count("{") - text.count("}")
                if depth <= 0:
                    break
        yield i, body


def scan(paths):
    resets, installs = [], []
    for rel in paths:
        full = os.path.join(CHECKOUT, rel)
        if not os.path.exists(full):
            continue
        with open(full, encoding="utf-8-sig", errors="replace") as f:
            lines = f.read().splitlines()
        for attr, body in hook_bodies(lines):
            waived_hook = WAIVER in lines[attr]
            for j, text in body:
                if waived_hook or WAIVER in text:
                    continue
                c = code(text)
                if RESET.search(c) and not LOCAL.search(c):
                    resets.append((rel, j + 1, text.strip()))
                elif INSTALL.search(c) and not re.search(r"\bif\s*\(", c):
                    installs.append((rel, j + 1, text.strip()))
    return resets, installs


def main():
    paths = all_files() if "--all" in args else changed_files()
    if paths is None:
        print("could not work out the changed files against origin/main: scanning every file")
        paths = all_files()
    resets, installs = scan(paths)
    new = [r for r in resets if r[0] not in KNOWN]
    known = [r for r in resets if r[0] in KNOWN]
    for rel, line, text in new:
        print(f"NEW {rel}:{line}: a reset in a BeforeSceneLoad hook ({text}); move it to a SubsystemRegistration hook")
    for rel, line, text in known:
        print(f"known {rel}:{line}: {KNOWN[rel]}")
    for rel, line, text in installs:
        print(f"WARN {rel}:{line}: an install in a BeforeSceneLoad hook ({text}); install only into an empty slot, or re-assert after the first scene")
    print(f"{len(paths)} file(s) read, {len(new)} new reset(s) in BeforeSceneLoad hooks, {len(installs)} install warning(s)")
    return 1 if new else 0


if __name__ == "__main__":
    sys.exit(main())
