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
    python ".../compile_check.py" --defines "KN_TEST_LANE;KN_DEV_BOTS"   extra scripting defines, as a build's
                                               extraScriptingDefines add them (with --server for a server build's set)
    python ".../compile_check.py" --ecs-library C:/path/to/clone   where the Entities / Netcode packages and their built DLLs are
    python ".../compile_check.py" --maxcpu 2   build with at most 2 MSBuild nodes
    python ".../compile_check.py" --each       one dotnet run per assembly, the old slower way (same output)
    python ".../compile_check.py" --restore-only   restore only: must fail with "not compiled" (a test of the guard)
It prints the errors and exits 1 when any assembly fails. Warnings are not shown.

Entities and Netcode for Entities (DOTS migration, M2b-2): an assembly whose .asmdef references Unity.Entities or Unity.NetCode is
compiled WITH the packages' source generators, because without them a bad IJobEntity, ISystem or RPC compiles "green" and fails
only in Unity (spike M1, "Compile gate"). For such an assembly this script
  - adds the generator DLLs as <Analyzer> lines: Unity.Entities/SourceGenerators/*.dll (11 of them) and, for Unity.NetCode, the
    NetCodeAnalyzers.dll and NetCodeSourceGenerator.dll plus the Netcode templates as <AdditionalFiles> (without them the
    generator stops with "Missing internal Netcode package template");
  - references the packages' own built DLLs (Library/ScriptAssemblies/<Name>.dll) for every assembly the .asmdef names that the
    Editor's project files do not cover, and drops the template's package projects the .asmdef does not name;
  - finds the packages in <ecs-library>/Library/PackageCache/com.unity.<entities|netcode>@*, else in the main folder's PackageCache,
    else in the Editor install (UNITY_EDITOR_DATA, or Program Files/Unity/Hub/Editor/*/Editor/Data).
<ecs-library> is --ecs-library, else the checkout when it has Library/ScriptAssemblies, else the --projects folder. It needs one
Unity compile of that project to have happened (batch or Editor), so the DLLs exist; names that are still missing are listed at
the end. Assemblies that reference neither are compiled exactly as before.

What it cannot see: an assembly that exists only in the checkout (a new .asmdef) is made from its .asmdef's
references and the runtime project's settings; package or vendor code that changed in the checkout is compiled from
the Editor's folder instead.
"""
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]


def arg(name, default=None):
    return args[args.index(name) + 1] if name in args else default


CHECKOUT = os.path.normpath(arg("--checkout", os.path.join(HERE, *[".."] * 6)))
SERVER = "--server" in args
EXTRA_DEFINES = [d for d in (arg("--defines", "") or "").split(";") if d.strip()]
MAXCPU = arg("--maxcpu")   # e.g. 2: fewer MSBuild nodes, so a compile shares the PC with other jobs


def main_folder():
    out = subprocess.run(["git", "-C", CHECKOUT, "worktree", "list", "--porcelain"], capture_output=True, text=True).stdout
    first = next((l[9:] for l in out.splitlines() if l.startswith("worktree ")), CHECKOUT)
    return os.path.normpath(first)


PROJECTS = os.path.normpath(arg("--projects", main_folder()))
ECS_LIB = os.path.normpath(arg("--ecs-library", CHECKOUT if os.path.isdir(os.path.join(CHECKOUT, "Library", "ScriptAssemblies")) else PROJECTS))
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
# --- Entities / Netcode for Entities: source generators and the packages' built DLLs -------------------------------------
ECS_ROOTS = ("Unity.Entities", "Unity.NetCode")
ECS_EXTRA = ("Unity.Burst", "Unity.Collections", "Unity.Mathematics", "Unity.Entities", "Unity.Transforms")   # the generated code uses these
ENTITIES_GENERATORS = ["Common.dll", "JobEntityGenerator.dll", "SystemGenerator.Common.dll", "SystemGenerator.EntityQueryBulkOperations.dll",
                       "SystemGenerator.LambdaJobs.dll", "SystemGenerator.SystemAPI.Query.dll", "SystemGenerator.SystemAPI.QueryBuilder.dll",
                       "SystemGenerator.SystemAPI.dll", "SystemGenerator.dll", "Unity.Entities.Analyzer.CodeFixes.dll", "Unity.Entities.Analyzer.dll"]
NETCODE_GENERATORS = ["NetCodeAnalyzers.dll", "NetCodeSourceGenerator.dll"]
SCRIPT_DLLS = os.path.join(ECS_LIB, "Library", "ScriptAssemblies")
MISSING_ECS = set()


def package_dir(prefix):
    """The folder of package com.unity.<prefix>: this project's cache, the main folder's cache, then the Editor install."""
    import glob
    patterns = [os.path.join(ECS_LIB, "Library", "PackageCache", prefix + "@*"), os.path.join(PROJECTS, "Library", "PackageCache", prefix + "@*")]
    data = os.environ.get("UNITY_EDITOR_DATA")
    builtin = os.path.join("Resources", "PackageManager", "BuiltInPackages", prefix)
    if data:
        patterns.append(os.path.join(data, builtin))
    patterns += [os.path.join("C:\\", "Program Files", "Unity", "Hub", "Editor", "*", "Editor", "Data", builtin),
                 os.path.expanduser(os.path.join("~", "Unity", "Hub", "Editor", "*", "Editor", "Data", builtin))]
    for pattern in patterns:
        found = sorted(glob.glob(pattern))
        if found:
            return found[-1]
    return None


def ecs_names(asm):
    """The names of the assemblies this .asmdef references (our own by their .asmdef name) and whether it is an Entities one."""
    names = {guid_names.get(r, r) for r in asmdefs.get(asm, {}).get("references", [])}
    return names, any(n.startswith(ECS_ROOTS) for n in names)


def ecs_generators(names):
    """<Analyzer> and <AdditionalFiles> lines for an assembly that references Unity.Entities and/or Unity.NetCode."""
    lines = []
    entities = package_dir("com.unity.entities")
    netcode = package_dir("com.unity.netcode")
    if any(n.startswith("Unity.Entities") or n.startswith("Unity.NetCode") for n in names):
        folder = os.path.join(entities, "Unity.Entities", "SourceGenerators") if entities else None
        found = [os.path.join(folder, d) for d in ENTITIES_GENERATORS if folder and os.path.exists(os.path.join(folder, d))]
        if not found and folder and os.path.isdir(folder):
            found = [os.path.join(folder, d) for d in sorted(os.listdir(folder)) if d.endswith(".dll")]
        if not found:
            MISSING_ECS.add("Entities source generators (com.unity.entities package not found)")
        lines += [f'    <Analyzer Include="{d}" />' for d in found]
    if any(n.startswith("Unity.NetCode") for n in names):
        folder = os.path.join(netcode, "Runtime", "SourceGenerators") if netcode else None
        found = [os.path.join(folder, d) for d in NETCODE_GENERATORS if folder and os.path.exists(os.path.join(folder, d))]
        if len(found) != len(NETCODE_GENERATORS):
            MISSING_ECS.add("Netcode source generators (com.unity.netcode package not found)")
        lines += [f'    <Analyzer Include="{d}" />' for d in found]
        if folder:
            lines.append(f'    <AdditionalFiles Include="{os.path.join(folder, "Templates", "**", "*.NetCodeSourceGenerator.additionalfile")}" />')
    return lines


def ecs_references(names, present):
    """<Reference> lines for the package DLLs an Entities assembly names that the template does not already have."""
    lines = []
    for n in sorted(names | set(ECS_EXTRA)):
        if n.startswith("KryticalNova") or n in present:
            continue
        dll = os.path.join(SCRIPT_DLLS, n + ".dll")
        if os.path.exists(dll):
            lines.append(f'    <Reference Include="{n}"><HintPath>{dll}</HintPath></Reference>')
        elif n in names:
            MISSING_ECS.add(n)
    return lines

ITEM = re.compile(r'(<(?:Compile|None|Content|AdditionalFiles|EmbeddedResource) Include=")([^"]+)(")')
PROJREF = re.compile(r'<ProjectReference Include="([^"]+)" />')


def absolute(path, base):
    return path if os.path.isabs(path) else os.path.normpath(os.path.join(base, path))


uses = {}   # assembly -> our assemblies it references (a build of it builds those first)


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
    if EXTRA_DEFINES:
        text = re.sub(r"<DefineConstants>([^<]*)</DefineConstants>",
                      lambda m: "<DefineConstants>" + ";".join(m.group(1).split(";") + EXTRA_DEFINES) + "</DefineConstants>", text)
    # the template's references to packages and vendors stand (Unity points at their built DLLs where it made no
    # project for them); our own assemblies come from the checkout's .asmdef below
    refs = PROJREF.findall(text)
    if asm in asmdefs:   # the checkout's .asmdef decides which of our assemblies it references
        wanted = {guid_names.get(r, r) for r in asmdefs[asm].get("references", [])}
        refs = [r for r in refs if not r.startswith("KryticalNova") or r[:-7] in wanted]
        refs += [w + ".csproj" for w in wanted if w.startswith("KryticalNova") and w + ".csproj" not in refs]
    ecs_lines = []
    names, is_ecs = ecs_names(asm)
    if is_ecs:   # the packages' projects the Editor wrote stay only where this .asmdef names them; the rest come as built DLLs
        refs = [r for r in refs if r.startswith("KryticalNova") or r[:-7] in names]
        present = {r[:-7] for r in refs} | set(re.findall(r'<Reference Include="([^",]+)', text))
        ecs_lines = ecs_references(names, present) + ecs_generators(names)
    text = re.sub(r"\s*<ProjectReference Include=\"[^\"]+\" />", "", text)
    lines = []
    uses[asm] = [r[:-7] for r in refs if r.startswith("KryticalNova")]
    for r in refs:
        name = r[:-7]
        target = os.path.join(OUT, r) if name.startswith("KryticalNova") else os.path.join(PROJECTS, r)
        lines.append(f'    <ProjectReference Include="{target}" />')
    compile_items = "\n".join(f'    <Compile Include="{p}" />' for p in sorted(scripts[asm]))
    # pinned last, so they win over the template: the output lands at bin/<asm>.dll, where the single run looks for it
    pinned = (f"  <PropertyGroup>\n    <OutputPath>{bin_dir}</OutputPath>\n"
              "    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>\n  </PropertyGroup>\n")
    block = f"  <ItemGroup>\n{compile_items}\n  </ItemGroup>\n  <ItemGroup>\n" + "\n".join(lines) + "\n  </ItemGroup>\n"
    if ecs_lines:
        block += "  <ItemGroup>\n" + "\n".join(ecs_lines) + "\n  </ItemGroup>\n"
    block += pinned
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
BUILD = ["-nologo", "-v", "q", "-clp:ErrorsOnly;NoSummary", "-p:WarningLevel=0"] + ([f"-maxcpucount:{MAXCPU}"] if MAXCPU else [])
ERROR_PROJECT = re.compile(r"\[([^\]]+?\.csproj)[^\]]*\]$")
failed, errors = [], []
if "--each" in args:   # the old way, one dotnet run per assembly: slower, for when the single run misbehaves
    for p in built:
        r = subprocess.run(["dotnet", "build", p] + BUILD, capture_output=True, text=True)
        errs = sorted(set(re.sub(r"\s*\[[^\]]*\]$", "", l.strip()) for l in (r.stdout + r.stderr).splitlines() if ": error " in l))
        if r.returncode != 0:
            failed.append(os.path.basename(p)[:-7])
            errors += [e for e in errs if e not in errors] or [f"{os.path.basename(p)}: build failed ({r.returncode})"]
else:
    # one dotnet run builds them all (start-up paid once); a plain MSBuild project, so no SDK has to be downloaded.
    # ErrorAndContinue: an assembly that fails does not stop the others, so every error is reported.
    batch = os.path.join(OUT, "all.proj")
    items = "".join(f'    <Ours Include="{p}" />\n' for p in built)
    with open(batch, "w", encoding="utf-8") as f:
        f.write('<Project DefaultTargets="Build">\n  <ItemGroup>\n' + items + '  </ItemGroup>\n'
                '  <Target Name="Restore">\n    <MSBuild Projects="@(Ours)" Targets="Restore" BuildInParallel="true" />\n  </Target>\n'
                '  <Target Name="Build">\n    <MSBuild Projects="@(Ours)" Targets="Build" BuildInParallel="true"'
                ' ContinueOnError="ErrorAndContinue" />\n  </Target>\n</Project>\n')
    started = time.time()
    # --restore-only runs the restore step alone, to prove the guard below fails a run that compiled nothing
    r = subprocess.run(["dotnet", "build", batch] + BUILD + (["-t:Restore"] if "--restore-only" in args else []),
                       capture_output=True, text=True)
    own = {}   # assembly -> its own errors, from the project named at the end of each error line
    for l in (r.stdout + r.stderr).splitlines():
        if ": error " not in l:
            continue
        m = ERROR_PROJECT.search(l.strip())
        name = os.path.basename(m.group(1))[:-7] if m else "all"
        e = re.sub(r"\s*\[[^\]]*\]$", "", l.strip())
        if e not in own.setdefault(name, []):
            own[name].append(e)
    # a clean run must have written every assembly with no error of its own: the project files are rewritten above,
    # so each one is compiled again on every run, never skipped as up to date (catches a run that only restored).
    # Checked before the line below, so an assembly that was not compiled fails the ones that use it too.
    for p in built:
        name = os.path.basename(p)[:-7]
        dll = os.path.join(OUT, "bin", name + ".dll")
        if name not in own and (not os.path.exists(dll) or os.path.getmtime(dll) < started - 1):
            own[name] = [f"{name}: not compiled (no output from this run)"]
    # as with one run per assembly, an assembly also counts as failed when one it references failed
    bad = set(own)
    grew = True
    while grew:
        grew = False
        for p in built:
            name = os.path.basename(p)[:-7]
            if name not in bad and any(u in bad for u in uses.get(name, [])):
                bad.add(name)
                grew = True
    failed = [os.path.basename(p)[:-7] for p in built if os.path.basename(p)[:-7] in bad]
    for name in sorted(own):
        errors += [e for e in own[name] if e not in errors]
    if r.returncode != 0 and not errors:
        failed = failed or ["all"]
        errors.append(f"all.proj: build failed ({r.returncode})")
for e in errors[:60]:
    print(e.replace(CHECKOUT + os.sep, ""))
if len(errors) > 60:
    print(f"... and {len(errors) - 60} more")
if MISSING_ECS:
    print("Entities/Netcode inputs not found (compile once in Unity so Library/ScriptAssemblies exists, or pass --ecs-library): "
          + ", ".join(sorted(MISSING_ECS)))
# a define change recompiles everything in Unity: said here, never failed on (define_guard.py)
sys.path.insert(0, HERE)
import define_guard
for w in define_guard.check(CHECKOUT):
    print("WARN " + w)
print(f"{len(built) - len(failed)} of {len(built)} assemblies compiled{' (server defines)' if SERVER else ''}"
      + (f" (+{';'.join(EXTRA_DEFINES)})" if EXTRA_DEFINES else "")
      + (f"; failed: {', '.join(failed)}" if failed else ""))
sys.exit(1 if failed else 0)
