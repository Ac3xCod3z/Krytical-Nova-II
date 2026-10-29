"""Counts the direct Netcode role reads left in rule code (DOTS migration, seam NetRole) and prints them.

Rule code should ask `NetRole` (Core) instead of reading IsServer / IsOwner / IsSpawned / IsClient / IsHost /
OwnerClientId / DisconnectClient off a network object. This script lists every remaining read in runtime code under
Assets/_Game, leaving out the places that are allowed to know Netcode: Networking/ (the adapters), Core/Runtime/NetRole.cs,
Editor code, tests and the "~" programs. Comment lines are skipped.

Usage (from the project folder):
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/netrole_audit.py"             print the list and the count
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/netrole_audit.py" --baseline   save today's count (netrole_baseline.json)
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/netrole_audit.py" --check      exit 1 when the count grew
Python's standard library only.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, *[".."] * 6))
GAME = os.path.join(ROOT, "Assets", "_Game")
BASELINE = os.path.join(HERE, "netrole_baseline.json")
PATTERN = re.compile(r"\b(IsServer|IsOwner|IsSpawned|IsClient|IsHost|OwnerClientId|DisconnectClient)\b")
ALLOWED = ("Networking/", "Core/Runtime/NetRole.cs")


def scan():
    hits = []
    for folder, dirs, files in os.walk(GAME):
        dirs[:] = [d for d in dirs if not d.endswith("~") and d not in ("Editor", "Tests")]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(folder, name)
            rel = os.path.relpath(path, GAME).replace("\\", "/")
            if rel.startswith(ALLOWED[0]) or rel == ALLOWED[1]:
                continue
            with open(path, encoding="utf-8", errors="replace") as f:
                for number, line in enumerate(f, 1):
                    code = line.split("//")[0]
                    for m in PATTERN.finditer(code):
                        hits.append((rel, number, m.group(1), line.strip()))
    return hits


def main():
    hits = scan()
    per_file = {}
    for rel, number, word, text in hits:
        per_file.setdefault(rel, []).append((number, word, text))
    for rel in sorted(per_file, key=lambda r: (-len(per_file[r]), r)):
        print(f"{len(per_file[rel]):3d}  {rel}")
        if "--verbose" in sys.argv:
            for number, word, text in per_file[rel]:
                print(f"       {number}: {text[:110]}")
    print(f"\nDirect reads in rule code: {len(hits)} in {len(per_file)} files")
    if "--baseline" in sys.argv:
        with open(BASELINE, "w", encoding="utf-8") as f:
            json.dump({"reads": len(hits), "files": len(per_file)}, f, indent=2)
        print("baseline saved")
    if "--check" in sys.argv and os.path.exists(BASELINE):
        with open(BASELINE, encoding="utf-8") as f:
            base = json.load(f)["reads"]
        if len(hits) > base:
            print(f"FAIL: {len(hits)} reads, baseline {base}")
            sys.exit(1)
        print(f"ok: {len(hits)} <= baseline {base}")


main()
