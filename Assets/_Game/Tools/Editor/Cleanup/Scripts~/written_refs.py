"""
The cleanup's written-reference check, run outside Unity so the Editor stays free (owner, 18 Oct 2026).

Reads the project's files directly: every GUID written in a YAML, JSON or .meta file, followed file by file from the
roots (everything outside the folders the cleanup may touch, the build scenes, what ProjectSettings and the embedded
packages point at, and vendor files our code loads by path or by name). Nothing here asks Unity, so it is an
independent second opinion beside Maintainer and Unity's own dependencies.

Usage (from the project folder):
  python Assets/_Game/Tools/Editor/Cleanup/Scripts~/written_refs.py scan
      writes Library/KryticalNova.Cleanup/WrittenUsed.txt: every vendor file something in use writes a reference to
  python Assets/_Game/Tools/Editor/Cleanup/Scripts~/written_refs.py after
      writes Library/KryticalNova.Cleanup/StillReferenced.txt: removed GUIDs (Removal Log) that something in the
      project still points at; the Editor restores those
"""
import os
import re
import sys
import time

ROOT = os.getcwd().replace("\\", "/")
OUT = os.path.join(ROOT, "Library", "KryticalNova.Cleanup")

# Must match ProjectCleanupScan.Candidates / Roots / InterfaceKeep / IsCandidate.
CANDIDATES = ["Assets/Synty/", "Assets/_ThirdParty/", "Assets/_Recovery/", "Assets/QuickDestruction/Demo/",
              "Assets/TextMesh Pro/Examples & Extras/"]
KEEP_ROOTS = ["Assets/_Game/", "Assets/Synty/SidekickCharacters/"]
INTERFACE_KEEP = ["/Prefabs/", "/Sprites/", "/Fonts/"]
KEEP_SEGMENTS = ["/Resources/", "/Plugins/", "/Editor/", "/StreamingAssets/"]

TEXT_EXT = {".unity", ".prefab", ".asset", ".mat", ".controller", ".overridecontroller", ".anim", ".mask", ".playable",
            ".signal", ".physicmaterial", ".physicsmaterial", ".lighting", ".spriteatlas", ".spriteatlasv2",
            ".shadergraph", ".shadersubgraph", ".vfx", ".terrainlayer", ".brush", ".flare", ".rendertexture",
            ".cubemap", ".guiskin", ".fontsettings", ".json", ".inputactions", ".preset", ".mixer", ".giparams",
            ".uss", ".uxml", ".tss", ".asmdef", ".asmref", ".index"}
GUID_REF = re.compile(rb'guid\\?"?\s*:\s*\\?"?([0-9a-f]{32})')
META_GUID = re.compile(rb"^guid:\s*([0-9a-f]{32})", re.M)
VENDOR_LITERAL = re.compile(r'"(Assets/(?:Synty|_ThirdParty|QuickDestruction)/[^"]+)"')
QUOTED_NAME = re.compile(r'"([A-Za-z0-9_\-\.\(\) ]{4,96})"')


def is_candidate(path):
    if not any(path.startswith(c) for c in CANDIDATES):
        return False
    if any(path.startswith(r) for r in KEEP_ROOTS):
        return False
    if path.startswith("Assets/Synty/Interface") and any(path.find(k) > 0 for k in INTERFACE_KEEP):
        return False
    if any(s in path for s in KEEP_SEGMENTS):
        return False
    return True


def walk_assets():
    """guid -> path, path -> is_folder, for everything under Assets (Unity skips names ending in ~ or starting with .)."""
    guid_to_path, files = {}, []
    for base in ["Assets"] + [os.path.join("Packages", d) for d in os.listdir("Packages") if os.path.isdir(os.path.join("Packages", d))]:
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if not d.endswith("~") and not d.startswith(".")]
            for name in filenames:
                if not name.endswith(".meta"):
                    continue
                meta = os.path.join(dirpath, name)
                asset = meta[:-5].replace("\\", "/")
                try:
                    with open(meta, "rb") as f:
                        m = META_GUID.search(f.read(512))
                except OSError:
                    continue
                if not m:
                    continue
                guid_to_path[m.group(1).decode()] = asset
                if asset.startswith("Assets/") and os.path.isfile(asset):
                    files.append(asset)
    return guid_to_path, files


def guids_in(path, into):
    try:
        with open(path, "rb") as f:
            data = f.read()
    except OSError:
        return
    for m in GUID_REF.finditer(data):
        into.add(m.group(1).decode())


def is_text(path):
    if os.path.splitext(path)[1].lower() in TEXT_EXT:
        return True
    try:
        with open(path, "rb") as f:
            return f.read(5) == b"%YAML"
    except OSError:
        return False


def path_or_name_loaded(candidates):
    """Vendor files _Game code or JSON names by path (an interpolated part = any one folder) or by file name."""
    literals, names = [], set()
    for dirpath, dirnames, filenames in os.walk("Assets/_Game"):
        dirnames[:] = [d for d in dirnames if not d.endswith("~")]
        for name in filenames:
            if not (name.endswith(".cs") or name.endswith(".json")):
                continue
            try:
                text = open(os.path.join(dirpath, name), encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            for m in VENDOR_LITERAL.finditer(text):
                parts = re.split(r"\{[^}]*\}", m.group(1))
                pattern = "^" + "[^/]+".join(re.escape(p) for p in parts)
                if len(pattern) - 1 > len("Assets/_ThirdParty/"):
                    literals.append(re.compile(pattern, re.I))
            for m in QUOTED_NAME.finditer(text):
                names.add(m.group(1).strip().lower())
    hits = []
    for p in candidates:
        base = os.path.basename(p)
        if base.lower() in names or os.path.splitext(base)[0].lower() in names or any(r.match(p) for r in literals):
            hits.append(p)
    return hits


def roots_from_settings(guid_to_path):
    guids = set()
    for dirpath, _, filenames in os.walk("ProjectSettings"):
        for name in filenames:
            if name.endswith(".asset"):
                guids_in(os.path.join(dirpath, name), guids)
    for d in os.listdir("Packages"):
        base = os.path.join("Packages", d)
        if not os.path.isdir(base):
            continue
        for dirpath, _, filenames in os.walk(base):
            for name in filenames:
                full = os.path.join(dirpath, name)
                if name.endswith(".meta") or os.path.splitext(name)[1].lower() in TEXT_EXT:
                    guids_in(full, guids)
    scenes = []
    try:
        for line in open("ProjectSettings/EditorBuildSettings.asset", encoding="utf-8", errors="ignore"):
            line = line.strip()
            if line.startswith("path: Assets/"):
                scenes.append(line[6:])
    except OSError:
        pass
    return [guid_to_path[g] for g in guids if g in guid_to_path and guid_to_path[g].startswith("Assets/")] + scenes


def closure(roots, guid_to_path, removed=None):
    reached, queue, broken = set(), [], set()
    for r in roots:
        if r not in reached:
            reached.add(r)
            queue.append(r)
    i, t0 = 0, time.time()
    while i < len(queue):
        path = queue[i]
        i += 1
        if i % 5000 == 0:
            print(f"  {i:,} files read, {len(reached):,} reached, {time.time() - t0:.0f} s", flush=True)
        found = set()
        guids_in(path + ".meta", found)
        if is_text(path):
            guids_in(path, found)
        for g in found:
            if removed is not None and g in removed and not os.path.exists(guid_to_path.get(g, "") or "\0"):
                broken.add(g)
            target = guid_to_path.get(g)
            if target and target.startswith("Assets/") and target not in reached and os.path.isfile(target):
                reached.add(target)
                queue.append(target)
    return reached, broken


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "scan"
    os.makedirs(OUT, exist_ok=True)
    t0 = time.time()
    guid_to_path, files = walk_assets()
    print(f"{len(files):,} asset files, {len(guid_to_path):,} GUIDs ({time.time() - t0:.0f} s)", flush=True)

    candidates = [p for p in files if is_candidate(p)]
    roots = [p for p in files if not is_candidate(p)]
    loaded = path_or_name_loaded(candidates)
    roots += loaded + roots_from_settings(guid_to_path)
    print(f"{len(candidates):,} files the cleanup may touch; {len(roots):,} roots, {len(loaded):,} of them loaded by path or name", flush=True)

    if mode == "after":
        removed = set()
        table = os.path.join("Assets/_Game/_Docs/Cleanup/Lists~/Removed-Assets.tsv")
        if os.path.exists(table):
            for line in open(table, encoding="utf-8"):
                c = line.rstrip("\n").split("\t")
                if len(c) >= 3 and len(c[2]) == 32 and not line.startswith(("#", "date\t")):
                    removed.add(c[2])
                    if c[2] not in guid_to_path:
                        guid_to_path[c[2]] = c[3]   # gone now; a reference to it is what we look for
        _, broken = closure(roots, guid_to_path, removed)
        with open(os.path.join(OUT, "StillReferenced.txt"), "w", encoding="utf-8", newline="\n") as f:
            for g in sorted(broken):
                f.write(g + "\n")
        print(f"after-check: {len(broken):,} removed file(s) still referenced ({time.time() - t0:.0f} s)")
        return

    reached, _ = closure(roots, guid_to_path)
    used = sorted(p for p in reached if is_candidate(p))
    with open(os.path.join(OUT, "WrittenUsed.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write(f"# written-reference check {time.strftime('%Y-%m-%d %H:%M')}: {len(used):,} of {len(candidates):,} candidate files are used\n")
        for p in used:
            f.write(p + "\n")
    print(f"done: {len(used):,} of {len(candidates):,} candidate files are used by something in use ({time.time() - t0:.0f} s)")


if __name__ == "__main__":
    main()
