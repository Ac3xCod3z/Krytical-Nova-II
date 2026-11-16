"""Flags a visual assembly that the dedicated server build compiles (owner rule, 15 Nov 2026: no visual code in the server build).

The server build compiles every first-party assembly under Assets/_Game that is not an Editor or test assembly and has no
`!UNITY_SERVER` define constraint (as compile_check.py --server). This lists those whose name or folder says they draw, play
sound or present (Rendering, Audio, UI, GameFeel, Presentation, Icons, Vfx, Effects, Camera, Minimap, Nameplate, Sky, Weather,
Vegetation, Grass, Foliage, Animation, Visual), and those that hold a `Presentation/` folder of scripts.
The fix is a `!UNITY_SERVER` define constraint, or moving what the server needs down a layer first (Dependency-Map rules).

Assemblies already known (KNOWN below, with the plan) do not fail; a new one does. A known one that has left the server
is a WARN line (take it off the list). The list only shrinks.

Usage (from the project folder):
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/server_visual.py"                  check
    python ".../server_visual.py" --list                                                       also print every server assembly
    python ".../server_visual.py" --checkout <folder>                                          another checkout (kn_check passes it)
Python's standard library only.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]
CHECKOUT = os.path.normpath(args[args.index("--checkout") + 1]) if "--checkout" in args else os.path.normpath(os.path.join(HERE, *[".."] * 6))
GAME = os.path.join(CHECKOUT, "Assets", "_Game")

VISUAL = re.compile(r"(Rendering|Audio|\bUI\b|GameFeel|Presentation|Icons?|Vfx|VFX|Effects|Camera|Minimap|Nameplate|Sky|Weather|"
                    r"Vegetation|Grass|Foliage|Animation|Visual)")

# Known on main (15 Nov 2026), with the plan; only shrinks.
KNOWN = {
    "KryticalNova.Audio": "the server judges Cadence beats through MusicConductor (BeatClock, MusicStateLink): move the beat clock and "
                          "music state down first, keep only the players in Audio",
    "KryticalNova.Rendering": "10 server assemblies call it (LOD, glow, occluder fade, DangleBones): Core seams first, then !UNITY_SERVER",
    "KryticalNova.World": "World/Runtime/Presentation (sky, weather, foliage presenters) to split into KryticalNova.World.Presentation",
}


def strip_comments(text):
    return re.sub(r"//[^\n]*", "", text)


def assemblies():
    found = []
    for folder, dirs, files in os.walk(GAME):
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        for name in files:
            if name.endswith(".asmdef"):
                path = os.path.join(folder, name)
                with open(path, encoding="utf-8-sig") as f:
                    data = json.loads(strip_comments(f.read()))
                found.append((data.get("name", ""), os.path.relpath(folder, CHECKOUT).replace("\\", "/"), data))
    return found


def on_server(name, data):
    return (".Editor" not in name and ".Tests" not in name
            and "!UNITY_SERVER" not in data.get("defineConstraints", [])
            and data.get("includePlatforms") != ["Editor"])


def presentation_folders(folder):
    """Presentation/ folders with scripts inside an assembly's folder (not in a nested assembly of its own)."""
    root = os.path.join(CHECKOUT, folder)
    hits = []
    for sub, dirs, files in os.walk(root):
        if sub != root and any(f.endswith(".asmdef") for f in files):
            dirs[:] = []
            continue
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        if os.path.basename(sub) == "Presentation" and any(f.endswith(".cs") for f in files):
            hits.append(os.path.relpath(sub, CHECKOUT).replace("\\", "/"))
    return hits


def main():
    server = [(n, f, d) for n, f, d in assemblies() if on_server(n, d)]
    flagged = {}
    for name, folder, data in server:
        why = []
        if VISUAL.search(name) or VISUAL.search(folder.replace("Assets/_Game/", "")):
            why.append("its name or folder says it is visual")
        why += [f"{p} holds presentation scripts" for p in presentation_folders(folder)]
        if why:
            flagged[name] = (folder, "; ".join(why))
    if "--list" in args:
        for name, folder, _ in sorted(server):
            print(f"server: {name} ({folder})")
    new = sorted(n for n in flagged if n not in KNOWN)
    gone = sorted(n for n in KNOWN if n not in flagged)
    for n in new:
        print(f"NEW {n} ({flagged[n][0]}): {flagged[n][1]}. The server build compiles it: add !UNITY_SERVER to its defineConstraints "
              f"(move what the server needs down a layer first)")
    for n in sorted(n for n in flagged if n in KNOWN):
        print(f"known {n}: {KNOWN[n]}")
    for n in gone:
        print(f"WARN GONE {n}: no longer a visual assembly in the server build, so take it off KNOWN in server_visual.py")
    print(f"{len(server)} assemblies in the server build, {len(flagged)} visual ({len(new)} new, {len(gone)} gone from the known list)")
    return 1 if new else 0   # a known one that left only warns: parallel PRs (grass) must not fail each other


if __name__ == "__main__":
    sys.exit(main())
