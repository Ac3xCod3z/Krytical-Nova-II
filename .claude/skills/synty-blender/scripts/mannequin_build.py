"""Build the Synty Mannequin Modeling scene: every lineage's bare body, male and female, side by side in T-pose.

  run_blender.py mannequin_build.py --out "G:/My Drive/KryticalNova/Art/Blender/Sidekick/SyntyMannequinModeling.blend"
        [--spacing 2.0]        metres between figures
        [--no-sliders]         leave out the Krytical Nova body sliders (kn_bust, kn_butt, kn_defined, kn_height)

Each mannequin is one copy of the Sidekick rig (named Rig_<Lineage>_<M|F>) with the parts the game shows for a
player with no gear on (LineageDefinition.bareBody + the first ready-made head of that sex, + the chest wrap on a
feminine body). Every part keeps its Synty shape keys. On top of those, the scene adds:

  kn_bust      feminine torso (and wrap): fuller bust                  0..1
  kn_butt      feminine hips: fuller glutes                           0..1
  kn_defined   masculine human and elf body: sharper muscle creases   0..1
  kn_height    custom property on each rig, drives the root bone's scale (0.90 .. 1.10)

The sculpts are procedural (fields of displacement on the Synty feminine / muscular shape) so they can be rebuilt
and re-tuned; AMPLITUDES below hold the numbers.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

BODY = ["10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR",
        "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR"]
NEW_KEYS = ("kn_bust", "kn_butt", "kn_defined")
HEIGHT_RANGE = (0.90, 1.10)

# Lineages as the game bakes them (CharacterDataBaker.Specs + Lineage_*.asset, 22 Oct 2026).
LINEAGES = [
    {"name": "Civilian", "species": "HU", "bare": "SK_HUMN_BASE_01_{slot}_HU01", "wrap": "SK_HUMN_BASE_01_38WRAP_HU01",
     "humanoid": True,
     "M": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_HUMN_BASE_04_02HAIR_HU01", "SK_HUMN_BASE_01_03EBRL_HU01",
           "SK_HUMN_BASE_01_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_HUMN_BASE_01_07EARL_HU01", "SK_HUMN_BASE_01_08EARR_HU01", "SK_HUMN_BASE_01_35NOSE_HU01",
           "SK_HUMN_BASE_01_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"],
     "F": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_SCFI_CIVL_03_02HAIR_HU01", "SK_HUMN_BASE_03_03EBRL_HU01",
           "SK_HUMN_BASE_03_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_HUMN_BASE_05_07EARL_HU01", "SK_HUMN_BASE_05_08EARR_HU01", "SK_HUMN_BASE_01_35NOSE_HU01",
           "SK_HUMN_BASE_02_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"]},
    {"name": "Soldier", "species": "HU", "bare": "SK_HUMN_BASE_01_{slot}_HU01", "wrap": "SK_HUMN_BASE_01_38WRAP_HU01",
     "humanoid": True,
     "M": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_SCFI_SOLD_03_02HAIR_HU01", "SK_HUMN_BASE_07_03EBRL_HU01",
           "SK_HUMN_BASE_07_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_HUMN_BASE_01_07EARL_HU01", "SK_HUMN_BASE_01_08EARR_HU01", "SK_SCFI_SOLD_02_09FCHR_HU01",
           "SK_HUMN_BASE_08_35NOSE_HU01", "SK_HUMN_BASE_01_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"],
     "F": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_SCFI_SOLD_04_02HAIR_HU01", "SK_HUMN_BASE_01_03EBRL_HU01",
           "SK_HUMN_BASE_01_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_SCFI_SOLD_01_07EARL_HU01", "SK_SCFI_SOLD_01_08EARR_HU01", "SK_HUMN_BASE_01_35NOSE_HU01",
           "SK_SCFI_SOLD_01_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"]},
    {"name": "Viking", "species": "HU", "bare": "SK_HUMN_BASE_01_{slot}_HU01", "wrap": "SK_HUMN_BASE_01_38WRAP_HU01",
     "humanoid": True,
     "M": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_HUMN_BASE_04_02HAIR_HU01", "SK_HUMN_BASE_01_03EBRL_HU01",
           "SK_HUMN_BASE_01_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_HUMN_BASE_01_07EARL_HU01", "SK_HUMN_BASE_01_08EARR_HU01", "SK_VIKG_WARR_08_09FCHR_HU01",
           "SK_HUMN_BASE_01_35NOSE_HU01", "SK_HUMN_BASE_01_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"],
     "F": ["SK_HUMN_BASE_01_01HEAD_HU01", "SK_HUMN_BASE_07_02HAIR_HU01", "SK_HUMN_BASE_08_03EBRL_HU01",
           "SK_HUMN_BASE_08_04EBRR_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01",
           "SK_HUMN_BASE_01_07EARL_HU01", "SK_HUMN_BASE_01_08EARR_HU01", "SK_HUMN_BASE_07_35NOSE_HU01",
           "SK_HUMN_BASE_01_36TETH_HU01", "SK_HUMN_BASE_01_37TONG_HU01"]},
    {"name": "Elf", "species": "EV", "bare": "SK_ELVN_BASE_01_{slot}_EV01", "wrap": "SK_ELVN_BASE_01_38WRAP_EV01",
     "humanoid": True,
     "M": ["SK_ELVN_BASE_01_01HEAD_EV01", "SK_ELVN_WARR_02_02HAIR_HU01", "SK_ELVN_WARR_02_03EBRL_HU01",
           "SK_ELVN_WARR_02_04EBRR_HU01", "SK_ELVN_BASE_01_05EYEL_EV01", "SK_ELVN_BASE_01_06EYER_EV01",
           "SK_ELVN_WARR_02_07EARL_EV01", "SK_ELVN_WARR_02_08EARR_EV01", "SK_ELVN_BASE_04_35NOSE_EV01",
           "SK_ELVN_BASE_01_36TETH_EV01", "SK_ELVN_BASE_01_37TONG_EV01"],
     "F": ["SK_ELVN_BASE_01_01HEAD_EV01", "SK_ELVN_WARR_01_02HAIR_HU01", "SK_ELVN_WARR_01_03EBRL_HU01",
           "SK_ELVN_WARR_01_04EBRR_HU01", "SK_ELVN_BASE_01_05EYEL_EV01", "SK_ELVN_BASE_01_06EYER_EV01",
           "SK_ELVN_WARR_02_07EARL_EV01", "SK_ELVN_WARR_02_08EARR_EV01", "SK_ELVN_BASE_01_35NOSE_EV01",
           "SK_ELVN_BASE_01_36TETH_EV01", "SK_ELVN_BASE_01_37TONG_EV01"]},
    {"name": "Skeleton", "species": "SN", "bare": "SK_SKTN_BASE_01_{slot}_SN01", "wrap": None, "humanoid": False,
     "M": ["SK_SKTN_BASE_01_01HEAD_SN01", "SK_SKTN_BASE_01_05EYEL_SN01", "SK_SKTN_BASE_01_06EYER_SN01",
           "SK_SKTN_BASE_01_36TETH_SN01"],
     "F": ["SK_SKTN_BASE_01_01HEAD_SN01", "SK_SKTN_BASE_01_05EYEL_SN01", "SK_SKTN_BASE_01_06EYER_SN01",
           "SK_SKTN_BASE_01_36TETH_SN01"]},
    {"name": "Robot", "species": "RO", "bare": "SK_SCFI_ROBO_04_{slot}_RO01", "wrap": None, "humanoid": False,
     "M": ["SK_ROBO_BASE_15_01HEAD_RO01"],
     "F": ["SK_ROBO_BASE_12_01HEAD_RO01"]},
]

# Slider sculpt numbers (metres, world space). Tune here, rebuild, look again.
AMPLITUDES = {
    "bust": {"push": 0.045, "sigma": 0.085, "z_lo": 1.17, "z_hi": 1.44},
    "butt": {"push": 0.042, "sigma": 0.095, "z_lo": 0.76, "z_hi": 1.02},
    "defined": {"gain": 1.5, "clamp": 0.011},
}


# ----------------------------------------------------------------------------------------------- import cache

class PartCache:
    """Each distinct FBX is imported once; mannequins get copies of its mesh."""

    def __init__(self):
        self.index = {p.stem: p for p in (kn.sidekick_root() / "Resources" / "Meshes").glob("**/SK_*.fbx")}
        self.meshes = {}
        self.extra_bones = {}       # stem -> [{name, parent, head, tail, roll}] bones beyond the 88 of the rig
        self.template_rig = None
        self.missing = []

    def mesh(self, stem):
        if stem in self.meshes:
            return self.meshes[stem]
        path = self.index.get(stem)
        if path is None:
            self.missing.append(stem)
            return None
        new = kn.import_fbx(path)
        arm = next((o for o in new if o.type == "ARMATURE"), None)
        mesh = next((o for o in new if o.type == "MESH"), None)
        if mesh is None:
            self.missing.append(stem)
            return None
        mesh.name = stem                      # some files carry another name inside (SK_ROBO_BASE_15 is SK_SCFI_ROBO_15)
        mesh.data.name = stem
        mesh.data.materials.clear()
        if arm is not None:
            used = {g.name for g in mesh.vertex_groups}
            if self.template_rig is not None:
                extras = []
                bpy.context.view_layer.objects.active = arm
                bpy.ops.object.mode_set(mode="EDIT")
                for eb in arm.data.edit_bones:
                    if eb.name in self.template_rig.data.bones:
                        continue
                    # only bones the mesh uses (and their extra parents); "transform1" style leftovers stay out
                    if eb.name not in used and not any(c.name in used for c in eb.children_recursive):
                        continue
                    extras.append({"name": eb.name, "parent": eb.parent.name if eb.parent else None,
                                   "head": eb.head.copy(), "tail": eb.tail.copy(), "roll": eb.roll})
                bpy.ops.object.mode_set(mode="OBJECT")
                if extras:
                    self.extra_bones[stem] = extras
            if self.template_rig is None and len(arm.data.bones) == 88:
                self.template_rig = arm
                arm.name = "SK_RigTemplate"
                arm.data.name = "SK_RigTemplate"
            else:
                bpy.data.objects.remove(arm)
        mesh.parent = None
        mesh.matrix_world = Matrix.Identity(4)
        for mod in list(mesh.modifiers):
            mesh.modifiers.remove(mod)
        mesh.hide_render = mesh.hide_viewport = True
        self.meshes[stem] = mesh
        return mesh


def give_bones(rig, extras):
    """Add a part's own bones (hair_dyn_01_01_c and the like) to a mannequin rig, where they are missing."""
    todo = [b for b in extras if b["name"] not in rig.data.bones]
    if not todo:
        return []
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    ebs = rig.data.edit_bones
    added = []
    for b in todo:
        eb = ebs.new(b["name"])
        eb.head, eb.tail, eb.roll = b["head"], b["tail"], b["roll"]
        if b["parent"] and b["parent"] in ebs:
            eb.parent = ebs[b["parent"]]
        added.append(b["name"])
    bpy.ops.object.mode_set(mode="OBJECT")
    return added


def slot_of(stem):
    return stem.split("_")[4]


# ----------------------------------------------------------------------------------------------- sculpting

def shaped_positions(me, key_values):
    """World-free (mesh local) positions of the mesh with the given shape keys mixed in."""
    basis = me.shape_keys.key_blocks[0]
    co = [basis.data[i].co.copy() for i in range(len(me.vertices))]
    for name, w in key_values.items():
        kb = me.shape_keys.key_blocks.get(name)
        if kb is None or w == 0.0:
            continue
        for i in range(len(co)):
            co[i] += (kb.data[i].co - basis.data[i].co) * w
    return co


def normals_for(me, co):
    """Vertex normals of the mesh if its vertices sat at `co` (mesh local)."""
    tmp = me.copy()
    flat = [c for v in co for c in v]
    tmp.vertices.foreach_set("co", flat)
    tmp.update()
    n = [v.normal.copy() for v in tmp.vertices]
    bpy.data.meshes.remove(tmp)
    return n


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))) if b != a else 1.0
    return t * t * (3 - 2 * t)


def add_key(ob, name):
    me = ob.data
    if not me.shape_keys:
        ob.shape_key_add(name="Basis", from_mix=False)
    kb = me.shape_keys.key_blocks.get(name) or ob.shape_key_add(name=name, from_mix=False)
    kb.slider_min, kb.slider_max, kb.value = 0.0, 1.0, 0.0
    return kb


def rig_frame(rig):
    """The rig's world matrix without its placement: rig-local metres, Z up, front -Y, feet at the origin."""
    M = rig.matrix_world.copy()
    M.translation = Vector((0, 0, 0))
    return M


def write_deltas(ob, name, deltas_world):
    """Store rig-frame metre deltas into a key (converted to the mesh's own space)."""
    kb = add_key(ob, name)
    basis = ob.data.shape_keys.key_blocks[0]
    to_local = rig_frame(ob.parent).inverted().to_3x3()
    moved = 0
    for i, d in enumerate(deltas_world):
        kb.data[i].co = basis.data[i].co + (to_local @ d)
        moved += d.length > 1e-4
    return moved


def centres_from_field(points, weights, split_x=True):
    """Weighted centroids of a point cloud, one each side of x = 0."""
    out = []
    for side in ((-1, 1) if split_x else (0,)):
        acc, tot = Vector((0, 0, 0)), 0.0
        for p, w in zip(points, weights):
            if side and (p.x * side) <= 0:
                continue
            acc += p * w
            tot += w
        if tot > 0:
            out.append(acc / tot)
    return out


def sculpt_bust(parts, rig):
    """Feminine chest: push out along the normal around the two points Synty's feminine key already raises."""
    A = AMPLITUDES["bust"]
    torso = next((p for p in parts if slot_of(p.name) == "10TORS"), None)
    if torso is None:
        return 0
    M = rig_frame(rig)
    R = M.to_3x3()
    me = torso.data
    basis = [v.co.copy() for v in me.shape_keys.key_blocks[0].data]
    fem = shaped_positions(me, {"masculineFeminine": 1.0})
    pts, wts = [], []
    for b, f in zip(basis, fem):
        pw = M @ f
        if A["z_lo"] <= pw.z <= A["z_hi"] and pw.y < 0:
            d = (R @ (f - b))
            if d.y < -0.002:                   # moved forward (front is -Y)
                pts.append(pw)
                wts.append(-d.y)
    centres = centres_from_field(pts, wts)
    if len(centres) != 2:
        centres = [Vector((-0.09, -0.12, 1.33)), Vector((0.09, -0.12, 1.33))]
    moved = 0
    torso_field = None
    for ob in sorted(parts, key=lambda p: slot_of(p.name) != "10TORS"):      # torso first, the wrap copies it
        if slot_of(ob.name) not in ("10TORS", "38WRAP"):
            continue
        me = ob.data
        co = shaped_positions(me, {"masculineFeminine": 1.0})
        nrm = normals_for(me, co)
        deltas = []
        if slot_of(ob.name) == "10TORS":
            for c, n in zip(co, nrm):
                pw, nw = M @ c, (R @ n).normalized()
                dist = min((pw - ctr).length for ctr in centres)
                w = math.exp(-(dist * dist) / (2 * A["sigma"] ** 2))
                w *= max(0.0, -nw.y) ** 0.5
                w *= smoothstep(A["z_lo"], A["z_lo"] + 0.05, pw.z) * (1 - smoothstep(A["z_hi"] - 0.05, A["z_hi"], pw.z))
                deltas.append(nw * (A["push"] * w) + Vector((0, 0, -0.004)) * w)
            from mathutils.kdtree import KDTree
            tree = KDTree(len(co))
            for i, c in enumerate(co):
                tree.insert(M @ c, i)
            tree.balance()
            torso_field = (tree, deltas)
        else:
            tree, tdel = torso_field
            for c, n in zip(co, nrm):
                pw, nw = M @ c, (R @ n).normalized()
                _p, i, _d = tree.find(pw)
                d = tdel[i]
                deltas.append(d + nw * (d.length * 0.12))          # ride the skin, a touch outside it
        moved += write_deltas(ob, "kn_bust", deltas)
    return moved


def sculpt_butt(parts, rig):
    A = AMPLITUDES["butt"]
    M = rig_frame(rig)
    R = M.to_3x3()
    moved = 0
    for ob in parts:
        if slot_of(ob.name) != "17HIPS":
            continue
        me = ob.data
        co = shaped_positions(me, {"masculineFeminine": 1.0})
        nrm = normals_for(me, co)
        pts, wts = [], []
        for c, n in zip(co, nrm):
            pw, nw = M @ c, (R @ n).normalized()
            if A["z_lo"] <= pw.z <= A["z_hi"] and nw.y > 0.3:
                pts.append(pw)
                wts.append(nw.y)
        centres = centres_from_field(pts, wts)
        if len(centres) != 2:
            centres = [Vector((-0.085, 0.1, 0.9)), Vector((0.085, 0.1, 0.9))]
        deltas = []
        for c, n in zip(co, nrm):
            pw, nw = M @ c, (R @ n).normalized()
            dist = min((pw - ctr).length for ctr in centres)
            w = math.exp(-(dist * dist) / (2 * A["sigma"] ** 2))
            w *= max(0.0, nw.y) ** 0.5
            w *= smoothstep(A["z_lo"], A["z_lo"] + 0.05, pw.z) * (1 - smoothstep(A["z_hi"] - 0.04, A["z_hi"], pw.z))
            deltas.append(nw * (A["push"] * w))
        moved += write_deltas(ob, "kn_butt", deltas)
    return moved


def sculpt_defined(parts, rig):
    """Masculine definition: sharpen the creases the muscular shape already has (an unsharp mask along normals)."""
    A = AMPLITUDES["defined"]
    M = rig_frame(rig)
    R = M.to_3x3()
    moved = 0
    for ob in parts:
        if slot_of(ob.name) not in ("10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR"):
            continue
        me = ob.data
        co = shaped_positions(me, {"defaultBuff": 1.0})
        nrm = normals_for(me, co)
        neigh = [[] for _ in co]
        for e in me.edges:
            a, b = e.vertices
            neigh[a].append(b)
            neigh[b].append(a)
        edge_faces = {}
        for f in me.polygons:
            for ek in f.edge_keys:
                edge_faces[ek] = edge_faces.get(ek, 0) + 1
        rim = {i for ek, n in edge_faces.items() if n == 1 for i in ek}
        near_rim = {j for i in rim for j in neigh[i]} - rim
        deltas = []
        for i, (c, n) in enumerate(zip(co, nrm)):
            if not neigh[i]:
                deltas.append(Vector((0, 0, 0)))
                continue
            mean = sum((co[j] for j in neigh[i]), Vector((0, 0, 0))) / len(neigh[i])
            d = R @ (c - mean)                   # world metres
            nw = (R @ n).normalized()
            along = d.dot(nw) * A["gain"]
            along = max(-A["clamp"], min(A["clamp"], along))
            along *= 0.0 if i in rim else 0.5 if i in near_rim else 1.0
            deltas.append(nw * along)
        moved += write_deltas(ob, "kn_defined", deltas)
    return moved


def drive_keys(rig, parts):
    """One slider per mannequin: rig properties kn_bust / kn_butt / kn_defined drive that key on every part."""
    for k in NEW_KEYS:
        rig[k] = 0.0
        rig.id_properties_ui(k).update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0)
        for p in parts:
            ks = p.data.shape_keys
            if not ks or k not in ks.key_blocks:
                continue
            fc = ks.key_blocks[k].driver_add("value")
            drv = fc.driver
            drv.type = "AVERAGE"
            var = drv.variables.new()
            var.name = "v"
            var.type = "SINGLE_PROP"
            var.targets[0].id_type = "OBJECT"
            var.targets[0].id = rig
            var.targets[0].data_path = f'["{k}"]'


def add_height(rig):
    rig["kn_height"] = 1.0
    ui = rig.id_properties_ui("kn_height")
    ui.update(min=HEIGHT_RANGE[0], max=HEIGHT_RANGE[1], soft_min=HEIGHT_RANGE[0], soft_max=HEIGHT_RANGE[1],
              description="Height: uniform scale of the root bone (every part follows)")
    pb = rig.pose.bones.get("root")
    if pb is None:
        return False
    for axis in range(3):
        fc = pb.driver_add("scale", axis)
        drv = fc.driver
        drv.type = "SCRIPTED"
        var = drv.variables.new()
        var.name = "h"
        var.type = "SINGLE_PROP"
        var.targets[0].id_type = "OBJECT"
        var.targets[0].id = rig
        var.targets[0].data_path = '["kn_height"]'
        drv.expression = "h"
    return True


# ----------------------------------------------------------------------------------------------- scene

def label(text, location, col):
    cu = bpy.data.curves.new("Label_" + text, type="FONT")
    cu.body = text
    cu.size = 0.11
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("Label_" + text, cu)
    ob.location = location
    ob.rotation_euler = (math.radians(90), 0, 0)
    col.objects.link(ob)
    return ob


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out")
    ap.add_argument("--spacing", type=float, default=2.0)
    ap.add_argument("--no-sliders", action="store_true")
    a = ap.parse_args(kn.tool_args())
    if kn.is_live():
        raise SystemExit("Build this scene headless (it creates a file); the live bridge must not write files.")
    if not a.out:
        raise SystemExit("--out FILE.blend is required.")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.length_unit = "METERS"

    cache = PartCache()
    spec = kn.load_spec()
    preview = kn.preview_material(kn.colour_map_path(), uv_layer=spec["uv"]["swatch_layer"])
    src_col = kn.ensure_collection("SK_Source (imported once, hidden)")
    manifest, problems, warnings = [], [], []
    index = 0
    for lin in LINEAGES:
        for sex in ("M", "F"):
            tag = f"{lin['name']}{sex}"
            stems = [lin["bare"].format(slot=s) for s in BODY] + lin[sex]
            if sex == "F" and lin["wrap"]:
                stems.append(lin["wrap"])
            meshes = [(s, cache.mesh(s)) for s in stems]
            if cache.template_rig is None:
                raise SystemExit("No 88-bone rig came in with the first parts.")
            col = kn.ensure_collection(f"{lin['name']} {'Male' if sex == 'M' else 'Female'}")
            rig = cache.template_rig.copy()
            rig.data = cache.template_rig.data.copy()
            rig.name = f"Rig_{tag}"
            rig.data.name = rig.name
            rig.location = (index * a.spacing, 0, 0)
            rig.display_type = "WIRE"
            col.objects.link(rig)
            parts = []
            for stem, src in meshes:
                if src is None:
                    continue
                ob = src.copy()
                ob.data = src.data.copy()
                ob.name = f"{stem}.{tag}"
                ob.data.name = ob.name
                ob.hide_render = ob.hide_viewport = False
                ob.parent = rig
                ob.matrix_parent_inverse = Matrix.Identity(4)
                ob.matrix_basis = Matrix.Identity(4)
                ob.modifiers.new("Armature", "ARMATURE").object = rig
                ob.data.materials.append(preview)
                col.objects.link(ob)
                if stem in cache.extra_bones:
                    give_bones(rig, cache.extra_bones[stem])
                missing_bones = [g.name for g in ob.vertex_groups if g.name not in rig.data.bones]
                if missing_bones:
                    warnings.append(f"{ob.name}: bones not in the rig (ignored): {missing_bones}")
                parts.append(ob)
            # The body the game shows: masculineFeminine 0 or 100, buff 50 (creator's neutral muscle).
            for ob in parts:
                keys = ob.data.shape_keys
                if not keys:
                    continue
                if "masculineFeminine" in keys.key_blocks:
                    keys.key_blocks["masculineFeminine"].value = 1.0 if sex == "F" else 0.0
                if "defaultBuff" in keys.key_blocks:
                    keys.key_blocks["defaultBuff"].value = 0.5
            bpy.context.view_layer.update()
            sliders = {}
            if not a.no_sliders:
                body_parts = [p for p in parts if slot_of(p.name) in BODY or slot_of(p.name) == "38WRAP"]
                for p in body_parts:
                    for k in NEW_KEYS:
                        add_key(p, k)          # every body part carries every slider, empty where it does nothing
                if sex == "F":
                    sliders["kn_bust"] = sculpt_bust(body_parts, rig) if lin["humanoid"] else 0
                    sliders["kn_butt"] = sculpt_butt(body_parts, rig) if lin["humanoid"] else 0
                elif lin["humanoid"]:
                    sliders["kn_defined"] = sculpt_defined(body_parts, rig)
                sliders["kn_height"] = add_height(rig)
                drive_keys(rig, body_parts)
            label(f"{lin['name']} {'M' if sex == 'M' else 'F'}", (index * a.spacing, 0.0, -0.18), col)
            manifest.append({"tag": tag, "lineage": lin["name"], "sex": sex, "species": lin["species"],
                             "rig": rig.name, "collection": col.name, "parts": [p.name for p in parts],
                             "sliders": sliders, "x": index * a.spacing})
            index += 1

    for src in cache.meshes.values():
        kn.move_to_collection(src, src_col)
    kn.move_to_collection(cache.template_rig, src_col)
    cache.template_rig.hide_render = cache.template_rig.hide_viewport = True
    src_col.hide_render = True
    src_col.hide_viewport = True
    scene["kn_mannequins"] = json.dumps(manifest)
    scene["kn_scene"] = "Synty Mannequin Modeling"
    if cache.missing:
        problems.append("Parts not found in Assets/Synty/SidekickCharacters: " + ", ".join(sorted(set(cache.missing))))
    kn.save_blend(a.out)
    kn.report("Synty Mannequin Modeling scene", {
        "file": a.out, "mannequins": [(m["tag"], len(m["parts"]), m["sliders"]) for m in manifest],
        "distinct_parts_imported": len(cache.meshes), "spacing_m": a.spacing,
        "sliders": {"kn_bust": "feminine 10TORS + 38WRAP", "kn_butt": "feminine 17HIPS",
                    "kn_defined": "masculine human/elf body parts", "kn_height": f"rig property, {HEIGHT_RANGE}"}},
        problems, warnings)


main()
