"""Compiles every first-party assembly of a checkout with dotnet, without opening Unity.

Unity writes a .csproj per assembly into the project folder of the Editor that has the project open. This script
copies the first-party ones (KryticalNova.*) into <checkout>/Temp/compile-check/, swaps their file lists for the
checkout's own scripts (worked out from its .asmdef / .asmref files, so moved and new scripts count), points their
references at each other, and builds them. Vendor and package projects are used from the Editor's folder as they are.

Use it to check a refactor in a worktree while the owner's Editor stays open on the main folder:
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/compile_check.py"            this checkout
    python ".../compile_check.py" --checkout C:/path/to/worktree --projects C:/path/to/main/folder
    python ".../compile_check.py" --server     also compile the runtime assemblies as the dedicated server does
                                               (UNITY_SERVER defined, Editor-only code left out)
It prints the errors and exits 1 when any assembly fails. Warnings are not shown.

What it cannot see: an assembly that exists only in the checkout (a new .asmdef) is made from its .asmdef's
references and the runtime project's settings; package or vendor code that changed in the checkout is compiled from
the Editor's folder instead.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]


def arg(name, default=None):
    return args[args.index(name) + 1] if name in args else default


CHECKOUT = os.path.normpath(arg("--checkout", os.path.join(HERE, *[".."] * 6)))
SERVER = "--server" in args


def main_folder():
    out = subprocess.run(["git", "-C", CHECKOUT, "worktree", "list", "--porcelain"], capture_output=True, text=True).stdout
    first = next((l[9:] for l in out.splitlines() if l.startswith("worktree ")), CHECKOUT)
    return os.path.normpath(first)


PROJECTS = os.path.normpath(arg("--projects", main_folder()))
GAME = os.path.join(CHECKOUT, "Assets", "_Game")
OUT = os.path.join(CHECKOUT, "Temp", "compile-check")
os.makedirs(OUT, exist_ok=True)


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        try:
            return json.load(f)
        except ValueError:
            return {}


# --- which assembly each script of the checkout compiles into ----------------------------------------------------------
asm_dirs, asmdefs, guid_names = {}, {}, {}
for root in (GAME,):
    for folder, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        for name in files:
            if name.endswith(".asmdef"):
                d = load_json(os.path.join(folder, name))
                asm_dirs[folder] = d.get("name")
                asmdefs[d.get("name")] = d
                try:
                    g = next(l.split(":", 1)[1].strip() for l in open(os.path.join(folder, name + ".meta")) if l.startswith("guid:"))
                    guid_names["GUID:" + g] = d.get("name")
                except (OSError, StopIteration):
                    pass
for folder, dirs, files in os.walk(GAME):
    dirs[:] = [d for d in dirs if not d.endswith("~")]
    for name in files:
        if name.endswith(".asmref"):
            ref = load_json(os.path.join(folder, name)).get("reference", "")
            asm_dirs[folder] = guid_names.get(ref, ref)

scripts = {}
for folder, dirs, files in os.walk(GAME):
    dirs[:] = [d for d in dirs if not d.endswith("~")]
    for name in files:
        if name.endswith(".cs"):
            f = folder
            while f.startswith(GAME) and f not in asm_dirs:
                f = os.path.dirname(f)
            scripts.setdefault(asm_dirs.get(f, "Assembly-CSharp"), []).append(os.path.join(folder, name))

# --- the project files ------------------------------------------------------------------------------------------------
existing = {n[:-7] for n in os.listdir(PROJECTS) if n.endswith(".csproj")}
ours = sorted(a for a in set(asmdefs) | {a for a in scripts if a.startswith("KryticalNova")} if a.startswith("KryticalNova"))
ITEM = re.compile(r'(<(?:Compile|None|Content|AdditionalFiles|EmbeddedResource) Include=")([^"]+)(")')
PROJREF = re.compile(r'<ProjectReference Include="([^"]+)" />')


def absolute(path, base):
    return path if os.path.isabs(path) else os.path.normpath(os.path.join(base, path))


def make(asm):
    """Write the checkout's version of one first-party project; return its path, or None if it has no scripts."""
    if not scripts.get(asm):
        return None
    if asm in existing:
        template_name = asm
    elif ".Tests" in asm:   # a new test assembly: a test project brings NUnit and the test runner
        template_name = "KryticalNova.Combat.Tests.Editor"
    elif ".Editor" in asm:
        template_name = "KryticalNova.Editor"
    else:
        template_name = "KryticalNova.Runtime"
    with open(os.path.join(PROJECTS, template_name + ".csproj"), encoding="utf-8-sig") as f:
        text = f.read()
    # drop the Editor's file list; the checkout's comes below
    text = re.sub(r"<(Compile|None|Content|AdditionalFiles|EmbeddedResource) Include=\"[^\"]*\"\s*/>\s*", "", text)
    text = re.sub(r"<AssemblyName>[^<]*</AssemblyName>", lambda m: f"<AssemblyName>{asm}</AssemblyName>", text)
    # Unity hands an assembly only the assemblies it names, not theirs in turn: so must this check
    text = text.replace("</AssemblyName>", "</AssemblyName>\n    <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>", 1)
    obj = os.path.join(OUT, "obj", asm) + os.sep
    text = re.sub(r"<BaseIntermediateOutputPath>[^<]*</BaseIntermediateOutputPath>", lambda m: f"<BaseIntermediateOutputPath>{obj}</BaseIntermediateOutputPath>", text)
    bin_dir = os.path.join(OUT, "bin") + os.sep
    text = re.sub(r"<OutputPath>[^<]*</OutputPath>", lambda m: f"<OutputPath>{bin_dir}</OutputPath>", text)
    text = re.sub(r'<HintPath>(?!\w:)([^<]+)</HintPath>', lambda m: "<HintPath>" + absolute(m.group(1), PROJECTS) + "</HintPath>", text)
    text = re.sub(r'<Analyzer Include="(?!\w:)([^"]+)"', lambda m: '<Analyzer Include="' + absolute(m.group(1), PROJECTS) + '"', text)
    if SERVER:
        text = re.sub(r"<DefineConstants>([^<]*)</DefineConstants>", lambda m: "<DefineConstants>" + ";".join(
            [d for d in m.group(1).split(";") if not d.startswith("UNITY_EDITOR")] + ["UNITY_SERVER"]) + "</DefineConstants>", text)
    # the template's references to packages and vendors stand (Unity points at their built DLLs where it made no
    # project for them); our own assemblies come from the checkout's .asmdef below
    refs = PROJREF.findall(text)
    if asm in asmdefs:   # the checkout's .asmdef decides which of our assemblies it references
        wanted = {guid_names.get(r, r) for r in asmdefs[asm].get("references", [])}
        refs = [r for r in refs if not r.startswith("KryticalNova") or r[:-7] in wanted]
        refs += [w + ".csproj" for w in wanted if w.startswith("KryticalNova") and w + ".csproj" not in refs]
    text = re.sub(r"\s*<ProjectReference Include=\"[^\"]+\" />", "", text)
    lines = []
    for r in refs:
        name = r[:-7]
        target = os.path.join(OUT, r) if name.startswith("KryticalNova") else os.path.join(PROJECTS, r)
        lines.append(f'    <ProjectReference Include="{target}" />')
    compile_items = "\n".join(f'    <Compile Include="{p}" />' for p in sorted(scripts[asm]))
    block = f"  <ItemGroup>\n{compile_items}\n  </ItemGroup>\n  <ItemGroup>\n" + "\n".join(lines) + "\n  </ItemGroup>\n"
    text = text.replace('  <Import Project="Sdk.targets"', block + '  <Import Project="Sdk.targets"', 1)
    path = os.path.join(OUT, asm + ".csproj")
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    return path


built = [p for p in (make(a) for a in ours) if p]
if SERVER:   # the server build has no Editor or test assemblies, nor the ones marked !UNITY_SERVER
    def on_server(p):
        name = os.path.basename(p)[:-7]
        return (".Editor" not in name and ".Tests" not in name
                and "!UNITY_SERVER" not in asmdefs.get(name, {}).get("defineConstraints", []))
    built = [p for p in built if on_server(p)]
# a traversal project builds them all in one dotnet run
trav = os.path.join(OUT, "all.proj")
with open(trav, "w", encoding="utf-8") as f:
    f.write('<Project Sdk="Microsoft.Build.Traversal/4.1.0">\n  <ItemGroup>\n'
            + "".join(f'    <ProjectReference Include="{p}" />\n' for p in built) + "  </ItemGroup>\n</Project>\n")
failed, errors = [], []
for p in built:
    r = subprocess.run(["dotnet", "build", p, "-nologo", "-v", "q", "-clp:ErrorsOnly;NoSummary", "-p:WarningLevel=0"],
                       capture_output=True, text=True)
    errs = sorted(set(re.sub(r"\s*\[[^\]]*\]$", "", l.strip()) for l in (r.stdout + r.stderr).splitlines() if ": error " in l))
    if r.returncode != 0:
        failed.append(os.path.basename(p)[:-7])
        errors += [e for e in errs if e not in errors] or [f"{os.path.basename(p)}: build failed ({r.returncode})"]
for e in errors[:60]:
    print(e.replace(CHECKOUT + os.sep, ""))
if len(errors) > 60:
    print(f"... and {len(errors) - 60} more")
print(f"{len(built) - len(failed)} of {len(built)} assemblies compiled{' (server defines)' if SERVER else ''}"
      + (f"; failed: {', '.join(failed)}" if failed else ""))
sys.exit(1 if failed else 0)
