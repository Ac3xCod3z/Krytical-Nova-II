"""The file-path rules of Validate Project Organization, without Unity.

Mirrors Assets/_Game/Tools/Editor/Organization/ProjectOrganizationRules.cs and ProjectOrganizationValidator.cs (keep the
lists the same) so kn_check.py --quick can run them in a worktree with Unity closed:

    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/organization_check.py"
    python ".../organization_check.py" --checkout C:/path/to/worktree
    python ".../organization_check.py" --warnings          also list every warning, not only the count

Errors: type-bucket or unrecognised folders and loose files at the Assets root, scripts under _Game that no .asmdef /
.asmref covers (they would compile into Assembly-CSharp), runtime scripts that use UnityEditor without #if UNITY_EDITOR.
Warnings: files directly in _Game, unclear names, scripts outside Runtime/Editor/Data/Tests. Exits 1 on an error.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]


def arg(name, default=None):
    return args[args.index(name) + 1] if name in args else default


CHECKOUT = os.path.normpath(arg("--checkout", os.path.join(HERE, *[".."] * 6)))
ASSETS = os.path.join(CHECKOUT, "Assets")


def names(*items):
    return {i.lower() for i in items}


OWNED_ROOTS = names("_Game", "_ThirdParty", "_Plugins", "_ArtSource")
UNITY_RESERVED_ROOTS = names("Plugins", "Resources", "StreamingAssets", "Editor Default Resources", "Gizmos",
                             "WebGLTemplates", "AddressableAssetsData", "Samples", "XR", "_Recovery", "TextMesh Pro")
PINNED_VENDOR_ROOTS = names("Synty", "DownloadCache", "QuickDestruction", "FastScriptReload")
PINNED_ROOT_FILES = names("DefaultNetworkPrefabs.asset", "csc.rsp", "mcs.rsp", "link.xml")
LEGACY_BUCKET_NAMES = names("Scripts", "Prefabs", "Materials", "Textures", "Models", "Meshes", "Shaders", "Scenes",
                            "Animations", "Audio", "Art", "UI", "GameData", "Data", "Editor", "Tests", "Settings",
                            "Fonts", "Sprites", "VFX", "ScriptableObjects")
SCRIPT_FOLDER_NAMES = names("Runtime", "Editor", "Data", "Tests", "Debug", "Profiling", "Importers")

# grassFINAL_REAL, "Copy of X", "New Material", "Untitled", "X (1)", "grass2"... (same pattern as the validator)
SLOPPY_NAME = re.compile(r"(?i:_final|final_|_old$|_backup|backup_|^copy of|\bcopy$|^new |^untitled|^temp_)"
                         r"|FINAL|Final$|\(\d+\)$|^[a-z]+\d+$")
USES_EDITOR_API = re.compile(r"^\s*using\s+UnityEditor\b", re.MULTILINE)


def classify_root(name, is_folder):
    n = name.lower()
    if n in OWNED_ROOTS or n in UNITY_RESERVED_ROOTS or n in PINNED_VENDOR_ROOTS:
        return "ok"
    if not is_folder and n in PINNED_ROOT_FILES:
        return "ok"
    if is_folder and n in LEGACY_BUCKET_NAMES:
        return "bucket"
    return "unsorted"


def check(checkout=CHECKOUT):
    assets = os.path.join(checkout, "Assets")
    game = os.path.join(assets, "_Game")
    errors, warnings = [], []

    def rel(p):
        return os.path.relpath(p, checkout).replace("\\", "/")

    for entry in sorted(os.listdir(assets)):
        if entry.endswith(".meta") or entry.startswith("."):
            continue
        full = os.path.join(assets, entry)
        kind = classify_root(entry, os.path.isdir(full))
        if kind == "bucket":
            errors.append((rel(full), "type-bucket folder at the Assets root. Move its contents to _Game/<System>/ "
                                      "(ours) or _ThirdParty/<Vendor>/ (theirs)."))
        elif kind == "unsorted":
            errors.append((rel(full), "unrecognised root folder; vendor content goes in _ThirdParty/."
                           if os.path.isdir(full) else
                           "loose file at the Assets root. Give it an owning folder, or add it to PinnedRootFiles."))

    if not os.path.isdir(game):
        return errors, warnings

    for entry in sorted(os.listdir(game)):
        if os.path.isfile(os.path.join(game, entry)) and not entry.endswith(".meta"):
            warnings.append((rel(os.path.join(game, entry)), "file directly in _Game/; every asset needs a system."))

    covered = {}   # folder -> whether an .asmdef / .asmref sits in it or above it (inside Assets)

    def has_assembly(folder):
        if folder in covered:
            return covered[folder]
        if os.path.normcase(folder) == os.path.normcase(assets) or len(folder) <= len(assets):
            result = False
        else:
            try:
                own = any(f.endswith((".asmdef", ".asmref")) for f in os.listdir(folder))
            except OSError:
                own = False
            result = own or has_assembly(os.path.dirname(folder))
        covered[folder] = result
        return result

    for folder, dirs, files in os.walk(game):
        path_folder = rel(folder) + "/"
        if "/_Docs/" in path_folder:
            dirs[:] = []
            continue
        dirs[:] = [d for d in dirs if not d.endswith("~") and not d.startswith(".")]
        for f in files:
            if f.endswith(".meta"):
                continue
            path = path_folder + f
            name = os.path.splitext(f)[0]
            if SLOPPY_NAME.search(name):
                warnings.append((path, "unclear asset name; use descriptive Type_Group_Variant names."))
            if not f.endswith(".cs"):
                continue
            parents = [p.lower() for p in path.split("/")[2:-1]]
            if not any(p in SCRIPT_FOLDER_NAMES for p in parents):
                warnings.append((path, "script outside a Runtime/, Editor/, Data/ or Tests/ folder of its system."))
            if not has_assembly(folder):
                errors.append((path, "not covered by an .asmdef/.asmref, so it compiles into Assembly-CSharp. "
                                     "Add an asmref to the system's assembly."))
            if "editor" not in parents and "/Tests/" not in path:
                with open(os.path.join(folder, f), encoding="utf-8-sig", errors="replace") as fh:
                    source = fh.read()
                if USES_EDITOR_API.search(source) and "#if UNITY_EDITOR" not in source:
                    errors.append((path, "uses UnityEditor outside an Editor/ folder without #if UNITY_EDITOR; "
                                         "player builds will fail."))
    return errors, warnings


if __name__ == "__main__":
    errs, warns = check()
    for p, m in errs:
        print(f"ERROR {p}: {m}")
    if "--warnings" in args:
        for p, m in warns:
            print(f"warning {p}: {m}")
    print(f"organization: {len(errs)} error(s), {len(warns)} warning(s)"
          + ("" if "--warnings" in args or not warns else " (--warnings lists them)"))
    sys.exit(1 if errs else 0)
