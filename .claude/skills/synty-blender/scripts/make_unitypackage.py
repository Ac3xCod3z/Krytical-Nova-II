#!/usr/bin/env python3
"""Pack an exported pet into a .unitypackage with its import settings already set (plain Python, no Blender).

  python make_unitypackage.py --fbx <exported>.fbx --out-dir <folder>
        [--pet-folder Assets/_Game/Characters/Pets/<Pet>] [--swatch-folder Assets/_Game/Characters/Shared/Swatch]

The package holds the FBX, the three Krytical Nova swatch textures, the one shared material M_KN_Swatch,
an Animator Controller (one trigger per animation, each returning to rest) and a prefab, so on import:
  - the model comes in as a Generic rig with its animations, facing +Z, using M_KN_Swatch;
  - the swatch textures are point-filtered, uncompressed, without mip maps;
  - the prefab is ready to drop into a scene; nothing has to be set by hand.
The same files are also written loose under <out-dir>/Unity/ in their project paths.

Two steps. This tool writes the model, textures and material with their .meta files. Then the project's
own Unity version, in batch mode on a throwaway project in the temp folder (never the game project),
imports that, adds the controller and prefab (assets/PetPackager.cs), checks what it imported and
exports the final package. --no-unity stops after the first step.

The .meta files are made from the project's own (a Sidekick model's, Sidekick's colour map's, an existing
URP Lit material), so they match the project's Unity version. Asset GUIDs are kept in
Assets/_ArtSource/Blender~/Swatch/unity_guids.json: packing again, or packing another pet, reuses them, so
a re-import updates in place and every pet points at the same swatch material.
"""
import argparse
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent


def project_root():
    for parent in HERE.parents:
        if (parent / "Assets" / "Synty" / "SidekickCharacters").is_dir():
            return parent
    sys.exit("Project root not found.")


ROOT = project_root()
SWATCH_SRC = ROOT / "Assets" / "_ArtSource" / "Blender~" / "Swatch"
MODEL_TEMPLATE = next((ROOT / "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits").glob("**/*29ASHL*.fbx.meta"), None)
TEXTURE_TEMPLATE = ROOT / "Assets/Synty/SidekickCharacters/Resources/Textures/T_ColorMap.png.meta"
MATERIAL_TEMPLATE = ROOT / "Assets/_Game/Characters/Shared/Props/M_EmoteProps.mat"
URP_LIT = "933532a4fcc9baf4fa0491de14d08ed7"


def sub(text, pattern, replacement, count=1, need=True):
    new, n = re.subn(pattern, replacement, text, count=count, flags=re.M | re.S)
    if need and n == 0:
        sys.exit(f"The template changed shape: nothing matched {pattern!r}. Update make_unitypackage.py.")
    return new


def model_meta(guid, remaps):
    """remaps: (material name inside the FBX, guid of the material asset it becomes), one per material."""
    t = MODEL_TEMPLATE.read_text(encoding="utf-8")
    t = sub(t, r"^guid: \w+$", f"guid: {guid}")
    t = sub(t, r"^  externalObjects:.*?(?=^  materials:)",
            "  externalObjects:\n" + "".join(
                "  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
                f"      name: {name}\n    second: {{fileID: 2100000, guid: {material_guid}, type: 2}}\n"
                for name, material_guid in remaps))
    t = sub(t, r"^    materialImportMode: \d+$", "    materialImportMode: 2")
    t = sub(t, r"^    isReadable: \d+$", "    isReadable: 0")
    t = sub(t, r"^    keepQuads: \d+$", "    keepQuads: 0")
    t = sub(t, r"^    weldVertices: \d+$", "    weldVertices: 1")
    # Off on purpose. With it on, a model that faces -Y in Blender comes into Unity facing -Z (backwards);
    # with it off it faces +Z, its left on -X, as Unity expects. Measured in Unity 6000.6.
    t = sub(t, r"^    bakeAxisConversion: \d+$", "    bakeAxisConversion: 0")
    t = sub(t, r"^    maxBonesPerVertex: \d+$", "    maxBonesPerVertex: 4")
    t = sub(t, r"^  importAnimation: \d+$", "  importAnimation: 1")
    t = sub(t, r"^  humanDescription:.*?(?=^  lastHumanDescriptionAvatarSource:)",
            "  humanDescription:\n    serializedVersion: 3\n    human: []\n    skeleton: []\n    armTwist: 0.5\n"
            "    foreArmTwist: 0.5\n    upperLegTwist: 0.5\n    legTwist: 0.5\n    armStretch: 0.05\n    legStretch: 0.05\n"
            "    feetSpacing: 0\n    globalScale: 1\n    rootMotionBoneName: \n    hasTranslationDoF: 0\n"
            "    hasExtraRoot: 0\n    skeletonHasParents: 1\n")
    t = sub(t, r"^  lastHumanDescriptionAvatarSource: [^\n]*$", "  lastHumanDescriptionAvatarSource: {instanceID: 0}")
    t = sub(t, r"^  animationType: \d+$", "  animationType: 2")            # Generic
    t = sub(t, r"^  avatarSetup: \d+$", "  avatarSetup: 1")                # create from this model
    t = sub(t, r"^  remapMaterialsIfMaterialImportModeIsNone: \d+$", "  remapMaterialsIfMaterialImportModeIsNone: 1")
    return t


def texture_meta(guid, srgb, painted=False):
    """painted: a model's own painted texture (smooth filtering, mip maps, normal compression) rather than
    a swatch of flat colour cells (point filtered, no mip maps, uncompressed)."""
    t = TEXTURE_TEMPLATE.read_text(encoding="utf-8")
    t = sub(t, r"^guid: \w+$", f"guid: {guid}")
    if painted:
        t = sub(t, r"^    enableMipMap: \d+$", "    enableMipMap: 1")
        t = sub(t, r"^    sRGBTexture: \d+$", f"    sRGBTexture: {1 if srgb else 0}")
        t = sub(t, r"^  isReadable: \d+$", "  isReadable: 0")
        t = sub(t, r"^    filterMode: -?\d+$", "    filterMode: 2")          # trilinear
        t = sub(t, r"^    textureCompression: \d+$", "    textureCompression: 1", count=0)
        return t
    t = sub(t, r"^    enableMipMap: \d+$", "    enableMipMap: 0")
    t = sub(t, r"^    sRGBTexture: \d+$", f"    sRGBTexture: {1 if srgb else 0}")
    t = sub(t, r"^  isReadable: \d+$", "  isReadable: 0")
    t = sub(t, r"^    filterMode: -?\d+$", "    filterMode: 0")          # point
    t = sub(t, r"^    textureCompression: \d+$", "    textureCompression: 0", count=0)   # none, every platform
    return t


def material(swatch_guid, emission_guid):
    t = MATERIAL_TEMPLATE.read_text(encoding="utf-8")
    if URP_LIT not in t:
        sys.exit("The material template is no longer a URP Lit material. Update make_unitypackage.py.")
    t = sub(t, r"^  m_Name: \S+$", "  m_Name: M_KN_Swatch")
    t = sub(t, r"^  m_ValidKeywords: \[\]$", "  m_ValidKeywords:\n  - _EMISSION")
    t = sub(t, r"^  m_LightmapFlags: \d+$", "  m_LightmapFlags: 2")
    for slot in ("_BaseMap", "_MainTex"):
        t = sub(t, rf"^(    - {slot}:\n        m_Texture: )[^\n]*$", rf"\g<1>{{fileID: 2800000, guid: {swatch_guid}, type: 3}}")
    t = sub(t, r"^(    - _EmissionMap:\n        m_Texture: )[^\n]*$", rf"\g<1>{{fileID: 2800000, guid: {emission_guid}, type: 3}}")
    t = sub(t, r"^    - _EmissionColor: [^\n]*$", "    - _EmissionColor: {r: 1, g: 1, b: 1, a: 1}")
    t = sub(t, r"^    - _Smoothness: [\d.]+$", "    - _Smoothness: 0.15")
    return t


def own_material(name, texture_guid=None, colour=None, glow=False, emission_guid=None):
    """A URP Lit material for a model that keeps its own look: its painted texture, or one flat colour.
    emission_guid: an emission map of its own (what glows, on black); its strength is then the material's
    Emission colour in Unity, white to begin with."""
    t = MATERIAL_TEMPLATE.read_text(encoding="utf-8")
    if URP_LIT not in t:
        sys.exit("The material template is no longer a URP Lit material. Update make_unitypackage.py.")
    t = sub(t, r"^  m_Name: \S+$", f"  m_Name: {name}")
    tex = f"{{fileID: 2800000, guid: {texture_guid}, type: 3}}" if texture_guid else "{fileID: 0}"
    for slot in ("_BaseMap", "_MainTex"):
        t = sub(t, rf"^(    - {slot}:\n        m_Texture: )[^\n]*$", rf"\g<1>{tex}")
    r, g, b = colour or (1.0, 1.0, 1.0)
    for prop in ("_BaseColor", "_Color"):
        t = sub(t, rf"^    - {prop}: [^\n]*$", f"    - {prop}: {{r: {r:.4f}, g: {g:.4f}, b: {b:.4f}, a: 1}}")
    if glow:
        t = sub(t, r"^  m_ValidKeywords: \[\]$", "  m_ValidKeywords:\n  - _EMISSION")
        t = sub(t, r"^  m_LightmapFlags: \d+$", "  m_LightmapFlags: 2")
        t = sub(t, r"^    - _EmissionColor: [^\n]*$",
                f"    - _EmissionColor: {{r: {r * 2:.4f}, g: {g * 2:.4f}, b: {b * 2:.4f}, a: 1}}")
    if emission_guid:
        t = sub(t, r"^  m_ValidKeywords: \[\]$", "  m_ValidKeywords:\n  - _EMISSION")
        t = sub(t, r"^  m_LightmapFlags: \d+$", "  m_LightmapFlags: 2")
        t = sub(t, r"^(    - _EmissionMap:\n        m_Texture: )[^\n]*$", rf"\g<1>{{fileID: 2800000, guid: {emission_guid}, type: 3}}")
        t = sub(t, r"^    - _EmissionColor: [^\n]*$", "    - _EmissionColor: {r: 1, g: 1, b: 1, a: 1}")
    t = sub(t, r"^    - _Smoothness: [\d.]+$", "    - _Smoothness: 0.3")
    return t


def see_through(text, alpha):
    """A URP Lit material made Transparent (alpha blended), its base colour's alpha set. Written from what URP saves
    for such a material; check it in Unity."""
    for prop in ("_BaseColor", "_Color"):
        text = re.sub(rf"^(    - {prop}: \{{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: )[\d.]+\}}", rf"\g<1>{alpha:.2f}}}", text, flags=re.M)
    for key, value in (("_Surface", 1), ("_Blend", 0), ("_SrcBlend", 5), ("_DstBlend", 10), ("_SrcBlendAlpha", 1),
                       ("_DstBlendAlpha", 10), ("_ZWrite", 0), ("_ZWriteControl", 0)):
        text = re.sub(rf"^    - {key}: [\d.]+$", f"    - {key}: {value}", text, flags=re.M)
    text = re.sub(r"^  m_CustomRenderQueue: -?\d+$", "  m_CustomRenderQueue: 3000", text, flags=re.M)
    if "_SURFACE_TYPE_TRANSPARENT" not in text:
        if re.search(r"^  m_ValidKeywords: \[\]$", text, flags=re.M):
            text = re.sub(r"^  m_ValidKeywords: \[\]$", "  m_ValidKeywords:\n  - _SURFACE_TYPE_TRANSPARENT", text, flags=re.M)
        else:
            text = re.sub(r"^  m_ValidKeywords:\n", "  m_ValidKeywords:\n  - _SURFACE_TYPE_TRANSPARENT\n", text, count=1, flags=re.M)
    if re.search(r"^  stringTagMap: \{\}$", text, flags=re.M):
        text = re.sub(r"^  stringTagMap: \{\}$", "  stringTagMap:\n    RenderType: Transparent", text, flags=re.M)
    else:
        text = re.sub(r"^(  stringTagMap:\n    RenderType: )\w+$", r"\g<1>Transparent", text, flags=re.M)
    return text


def material_meta(guid):
    return (f"fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n"
            "  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def find_unity():
    version = ""
    pv = ROOT / "ProjectSettings" / "ProjectVersion.txt"
    if pv.is_file():
        m = re.search(r"m_EditorVersion: (\S+)", pv.read_text(encoding="utf-8"))
        version = m.group(1) if m else ""
    hub = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"
    exact = hub / version / "Editor" / "Unity.exe"
    if exact.is_file():
        return exact, version
    return None, version


def unity_pass(unity, project, base_package, fbx_project_path, out_package, guids):
    """Import the base package into a throwaway project, let Unity add the controller and the prefab,
    check the import and export the final package. Returns Unity's report text."""
    project = Path(project)
    log_dir = project.parent
    log_dir.mkdir(parents=True, exist_ok=True)

    def run(*args, log):
        cmd = [str(unity), "-batchmode", "-nographics", "-quit", "-logFile", str(log_dir / log)] + list(args)
        return subprocess.call(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    if not (project / "ProjectSettings").is_dir():
        run("-createProject", str(project), log="kn_create.log")
    game = project / "Assets" / "_Game"
    if game.is_dir():
        shutil.rmtree(game)
    meta = project / "Assets" / "_Game.meta"
    if meta.is_file():
        meta.unlink()
    editor = project / "Assets" / "Editor"
    editor.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(HERE.parent / "assets" / "PetPackager.cs", editor / "PetPackager.cs")
    run("-projectPath", str(project), "-importPackage", str(base_package), log="kn_import.log")
    report = log_dir / "kn_pet_report.txt"
    if report.is_file():
        report.unlink()
    stem = Path(fbx_project_path).stem
    pet = stem.replace("SM_Pet_", "")
    pet_name = pet.rsplit("_S", 1)[0]
    args = ["-projectPath", str(project), "-executeMethod", "PetPackager.Run", "-knModel", fbx_project_path,
            "-knOut", str(out_package), "-knReport", str(report),
            "-knControllerGuid", guids(f"AC_Pet_{pet_name}.controller"), "-knPrefabGuid", guids(f"Pet_{pet}.prefab")]
    run(*args, log="kn_pack.log")
    return (report.read_text(encoding="utf-8") if report.is_file() else "FAILED: Unity wrote no report; see "
            + str(log_dir / "kn_pack.log")), game


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-unity", action="store_true",
                    help="only write the hand-made package (model, swatch, material); no controller, prefab or import check")
    ap.add_argument("--unity-project", help="throwaway Unity project to use (default: one in the temp folder)")
    ap.add_argument("--fbx", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--pet-folder")
    ap.add_argument("--swatch-folder", default="Assets/_Game/Characters/Shared/Swatch")
    ap.add_argument("--package-name", help="file name of the package without .unitypackage (default: the FBX's name)")
    ap.add_argument("--own-texture", help="the model keeps its own painted texture (this image) instead of the swatch")
    ap.add_argument("--swatch-page", choices=("kn", "synty"), default="kn",
                    help="which page of the shared swatch the model is on (pet_finish.py --swatch). On either page, "
                         '--material "name in the FBX=#RRGGBB[:glow]" adds a flat material of the pet\'s own beside it')
    ap.add_argument("--emission-texture", help="with --own-texture: the model's own emission map (what glows, on black)")
    ap.add_argument("--material", action="append", default=[],
                    help='with --own-texture, one per material in the FBX: "name in the FBX=texture" for the painted '
                         'texture, or "name in the FBX=#RRGGBB" / "=#RRGGBB:glow" for one flat colour')
    a = ap.parse_args()
    fbx = Path(a.fbx)
    if not fbx.is_file():
        sys.exit(f"No such file: {fbx}")
    for needed in (MODEL_TEMPLATE, TEXTURE_TEMPLATE, MATERIAL_TEMPLATE, SWATCH_SRC / "T_KN_Swatch.png"):
        if needed is None or not Path(needed).is_file():
            sys.exit(f"Missing: {needed}")
    pet = fbx.stem.replace("SM_Pet_", "").rsplit("_S", 1)[0]
    pet_folder = a.pet_folder or f"Assets/_Game/Characters/Pets/{pet}"

    guid_file = SWATCH_SRC / "unity_guids.json"
    guids = json.loads(guid_file.read_text(encoding="utf-8")) if guid_file.is_file() else {}

    def guid(key):
        if key not in guids:
            guids[key] = uuid.uuid4().hex
        return guids[key]

    items = []      # (project path, bytes, meta text)
    swatch_folder = a.swatch_folder.rstrip("/")
    if a.own_texture:
        texture = Path(a.own_texture)
        tex_name = f"T_Pet_{pet}{texture.suffix.lower()}"
        items.append((f"{pet_folder}/{tex_name}", texture.read_bytes(), texture_meta(guid(tex_name), True, painted=True)))
        em_guid = None
        if a.emission_texture:
            em = Path(a.emission_texture)
            em_name = f"T_Pet_{pet}_Emission{em.suffix.lower()}"
            items.append((f"{pet_folder}/{em_name}", em.read_bytes(), texture_meta(guid(em_name), True, painted=True)))
            em_guid = guid(em_name)
        remaps, flat = [], 0
        for spec in a.material:
            fbx_name, _, kind = spec.partition("=")
            if kind == "texture" or kind.startswith("texture:"):
                # "texture:Tails" is a second material with the same maps, M_Pet_<pet>_Tails, so that part of the model
                # can be set up differently in Unity; "texture:Tails:0.6" also makes it see-through to that much
                _t, _, rest_ = kind.partition(":")
                suffix, _, see = rest_.partition(":")
                mat_name = f"M_Pet_{pet}" + (f"_{suffix}" if suffix else "")
                text = own_material(mat_name, texture_guid=guid(tex_name), emission_guid=em_guid)
                if see:
                    text = see_through(text, float(see))
            else:
                hexa, _, extra = kind.partition(":")
                rgb = tuple(int(hexa.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4))
                flat += 1
                mat_name = f"M_Pet_{pet}_" + "".join(w.capitalize() for w in fbx_name.replace(fbx.stem, "").split()) or f"M_Pet_{pet}_{flat}"
                text = own_material(mat_name, colour=rgb, glow=extra == "glow")
            items.append((f"{pet_folder}/{mat_name}.mat", text.encode("utf-8"), material_meta(guid(mat_name + ".mat"))))
            remaps.append((fbx_name, guid(mat_name + ".mat")))
        if not remaps:
            sys.exit("--own-texture needs a --material for every material in the FBX.")
        items.append((f"{pet_folder}/{fbx.name}", fbx.read_bytes(), model_meta(guid(fbx.name), remaps)))
    synty = a.swatch_page == "synty" and not a.own_texture
    shared = () if a.own_texture else (("T_KN_Swatch_Synty.png", True),) if synty else (
        ("T_KN_Swatch.png", True), ("T_KN_Swatch_Emission.png", True), ("T_KN_Swatch_Metallic.png", False))
    for name, srgb in shared:
        items.append((f"{swatch_folder}/{name}", (SWATCH_SRC / name).read_bytes(), texture_meta(guid(name), srgb)))
    if not a.own_texture:
        if synty:           # the Synty page: flat colours only, no glow or metal cells
            swatch_mat = "M_KN_Swatch_Synty"
            mat_text = own_material(swatch_mat, texture_guid=guid("T_KN_Swatch_Synty.png"))
        else:
            swatch_mat = "M_KN_Swatch"
            mat_text = material(guid("T_KN_Swatch.png"), guid("T_KN_Swatch_Emission.png"))
        items.append((f"{swatch_folder}/{swatch_mat}.mat", mat_text.encode("utf-8"), material_meta(guid(swatch_mat + ".mat"))))
        remaps = [(swatch_mat, guid(swatch_mat + ".mat"))]
        for spec in a.material:                 # the pet's own flat materials beside the swatch
            fbx_name, _, kind = spec.partition("=")
            hexa, _, extra = kind.partition(":")
            rgb = tuple(int(hexa.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4))
            mat_name = f"M_Pet_{pet}_" + "".join(w.capitalize() for w in fbx_name.replace(fbx.stem, "").split())
            text = own_material(mat_name, colour=rgb, glow=extra == "glow")
            items.append((f"{pet_folder}/{mat_name}.mat", text.encode("utf-8"), material_meta(guid(mat_name + ".mat"))))
            remaps.append((fbx_name, guid(mat_name + ".mat")))
        items.append((f"{pet_folder}/{fbx.name}", fbx.read_bytes(), model_meta(guid(fbx.name), remaps)))
    guid_file.write_text(json.dumps(guids, indent=1), encoding="utf-8")

    out_dir = Path(a.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    package = out_dir / f"{a.package_name or fbx.stem}.unitypackage"
    with tarfile.open(package, "w:gz") as tar:
        def add(name, data):
            info = tarfile.TarInfo(name)
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))
        for path, data, meta in items:
            g = re.search(r"^guid: (\w+)$", meta, re.M).group(1)
            add(f"{g}/asset", data)
            add(f"{g}/asset.meta", meta.encode("utf-8"))
            add(f"{g}/pathname", path.encode("utf-8"))

    loose = out_dir / "Unity"
    for path, data, meta in items:
        target = loose / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        Path(str(target) + ".meta").write_text(meta, encoding="utf-8", newline="\n")

    result = {"package": str(package), "assets": [p for p, _d, _m in items], "loose_copy": str(loose),
              "guids_kept_in": str(guid_file), "made_by": "this tool (not checked in Unity)"}
    unity, version = find_unity()
    if a.no_unity:
        pass
    elif unity is None:
        result["warning"] = f"Unity {version} was not found, so there is no controller or prefab and the import was not checked."
    else:
        project = a.unity_project or str(Path(tempfile.gettempdir()) / "kn_unity_pkg" / "PkgProject")
        base = out_dir / (fbx.stem + "_base.unitypackage")
        package.replace(base)
        report, game = unity_pass(unity, project, base, f"{pet_folder}/{fbx.name}", package, guid)
        guid_file.write_text(json.dumps(guids, indent=1), encoding="utf-8")
        ok = report.strip().endswith("OK") and package.is_file()
        if ok:
            base.unlink()
            shutil.rmtree(loose, ignore_errors=True)
            shutil.copytree(game, loose / "Assets" / "_Game")
            result["made_by"] = f"Unity {version}, after importing and checking it in a throwaway project"
            result["assets"] = sorted(str(p.relative_to(loose)).replace("\\", "/") for p in loose.rglob("*")
                                      if p.is_file() and p.suffix != ".meta")
        else:
            base.replace(package)
            result["warning"] = "The Unity step failed; the package is the hand-made one without controller or prefab."
        result["unity_report"] = report.strip().splitlines()
    result["size_kb"] = round(package.stat().st_size / 1024)
    print(json.dumps(result, indent=1))
    if "warning" in result:
        sys.exit(1)


if __name__ == "__main__":
    main()
