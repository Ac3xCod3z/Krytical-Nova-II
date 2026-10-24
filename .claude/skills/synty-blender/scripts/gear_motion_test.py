"""Motion test for fitted gear: Synty's own Sidekick clips played on the rig, the armor measured frame by frame.

  run_blender.py --blend "<Gear/<Piece>.blend>" gear_motion_test.py [--clips dance,guitar,stretch,run,jump]
        [--video out.mp4] [--every 2] [--size 420] [--female]

For every sampled frame: how many armor vertices sink into the body they cover (deeper than 5 mm) beyond what is
inside at rest, and the worst depth. The body parts the armor replaces are hidden in the game, so this is the armor
collapsing into the shape it should keep, the thing that shows as broken gear in an emote or a fight.
The video shows the clips one after another, front three-quarter, with the clip name and frame burnt in.
"""
import argparse
import math
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)

CLIPS = {
    "dance": "AnimationEmotesAndTaunts/Animations/Sidekick/{sex}/Dance/A_MOD_EMOT_Dance_ChestPump_{s}.fbx",
    "guitar": "AnimationEmotesAndTaunts/Animations/Sidekick/{sex}/Celebrate/A_MOD_EMOT_Celebrate_AirGuitar_{s}.fbx",
    "dust": "AnimationEmotesAndTaunts/Animations/Sidekick/{sex}/Celebrate/A_MOD_EMOT_Celebrate_DustShoulder_{s}.fbx",
    "stretch": "AnimationIdles/Animations/Sidekick/{sex}/Stretch/Actions/A_MOD_IDL_Stretch_Arms_{s}.fbx",
    "shoulders": "AnimationIdles/Animations/Sidekick/{sex}/Stretch/Actions/A_MOD_IDL_Stretch_Shoulders_{s}.fbx",
    "run": "AnimationBaseLocomotion/Animations/Sidekick/{sex}/Locomotion/Run/A_MOD_BL_Run_F_{s}.fbx",
    "jump": "AnimationBaseLocomotion/Animations/Sidekick/{sex}/InAir/A_MOD_BL_Jump_Running_{s}.fbx",
}


def load_action(path, rig):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    act = None
    for o in new:
        if o.animation_data and o.animation_data.action:
            act = o.animation_data.action
    for o in new:
        bpy.data.objects.remove(o)
    if act is None:
        return None
    rig.animation_data_create()
    rig.animation_data.action = act
    if hasattr(rig.animation_data, "action_slot") and getattr(act, "slots", None):
        try:
            rig.animation_data.action_slot = act.slots[0]
        except Exception:
            pass
    return act


def soup(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    V, T = [], []
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        b = len(V)
        V += [o.matrix_world @ v.co for v in me.vertices]
        me.calc_loop_triangles()
        T += [tuple(b + i for i in t.vertices) for t in me.loop_triangles]
        ev.to_mesh_clear()
    return V, T


def inside_set(armor, tree, depth=0.005, near=0.06):
    V, _T = soup(armor)
    out = {}
    for i, p in enumerate(V):
        loc, n, _x, dist = tree.find_nearest(p)
        if loc is None or dist > near:
            continue
        d = (p - loc).dot(n)
        if d < -depth:
            out[i] = -d
    return out


# Pieces worn over others (owner's brief, 23 Oct 2026: cape vs torso, gloves vs sleeves, boots vs legs): each outer
# slot's vertices sinking into what lies under it, the set's own part in a slot or, where the set has none, the bare
# body part there (the cape behind the legs and over the lowering arms, a glove's cuff on the forearm).
LAYERS = {"24ABAC": ("10TORS", "17HIPS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "18LEGL", "19LEGR"),
          "15HNDL": ("11AUPL", "13ALWL"), "16HNDR": ("12AUPR", "14ALWR"),
          "20FOTL": ("17HIPS", "18LEGL"), "21FOTR": ("17HIPS", "19LEGR"),
          "22AHED": ("01HEAD",)}                     # a helmet over the bare head


def through(outer, inner, depth=0.003, near=0.03):
    """What actually shows: points of the inner surface (corners and face middles) standing out through the outer
    piece, found by looking in from outside along the inner surface's normal. Ends of a limb hidden inside a glove or a
    boot never count, unlike sinking, where the palm and the sole sat behind the forearm's and the calf's hidden ends."""
    V, T = soup([outer])
    tree = BVHTree.FromPolygons(V, T)
    IV, IT = soup(inner)
    acc = [Vector() for _ in IV]
    pts = []
    for t in IT:
        a_, b_, c_ = (IV[i] for i in t)
        fn = (b_ - a_).cross(c_ - a_)
        if fn.length < 1e-12:
            continue
        for i in t:
            acc[i] += fn
        pts.append(((a_ + b_ + c_) / 3, fn.normalized()))
    pts += [(p, n.normalized()) for p, n in zip(IV, acc) if n.length > 1e-12]
    n_out, worst = 0, 0.0
    for p, n in pts:
        loc, _n, _i, _d = tree.ray_cast(p + n * near, -n, 2 * near)
        if loc is None:
            continue
        d = (p - loc).dot(n)
        if d > depth:
            n_out += 1
            worst = max(worst, d)
    return n_out, worst


def layer_pairs(armor, body):
    by_slot = {o.name.split("_")[4]: o for o in armor}
    bare = {o.name.split("_")[4]: o for o in body}
    out = []
    for outer, unders in LAYERS.items():
        if outer not in by_slot:
            continue
        inner = [by_slot.get(s) or bare.get(s) for s in unders]
        inner = [o for o in inner if o is not None]
        if inner:
            out.append((outer, by_slot[outer], inner))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clips", default="dance,guitar,dust,stretch,shoulders,run,jump")
    ap.add_argument("--video")
    ap.add_argument("--every", type=int, default=2)
    ap.add_argument("--size", type=int, default=420)
    ap.add_argument("--female", action="store_true")
    ap.add_argument("--shape", default="", help='body sliders on top, e.g. "kn_bust=1,kn_butt=1"')
    a = ap.parse_args(kn.tool_args())
    if a.video:
        a.video = str(Path(a.video).resolve())
    rig = bpy.data.objects["SK_Rig"]
    armor = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SK_KRYT_")]
    covered = {o.name.split("_")[4] for o in armor}
    body = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig and not o.name.startswith("SK_KRYT_")]
    hidden_body = [o for o in body if o.name.split("_")[4] in covered]
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent is None:
            o.hide_render = True                       # the source piece
    for o in hidden_body:
        o.hide_render = True
    sex = "Feminine" if a.female else "Masculine"
    s = "Femn" if a.female else "Masc"
    values = {"masculineFeminine": 1.0 if a.female else 0.0, "defaultBuff": 0.5}
    values.update({k: float(v) for k, v in (kv.split("=") for kv in a.shape.split(",") if kv)})
    for o in body + armor:
        ks = o.data.shape_keys
        if ks:
            for kb in ks.key_blocks[1:]:
                kb.value = values.get(kb.name, 0.0)
    syn = kn.project_root() / "Assets" / "Synty"
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = sc.render.resolution_y = a.size
    sc.view_settings.view_transform = "Standard"
    if sc.world is None:
        w = bpy.data.worlds.new("kn_w")
        w.use_nodes = True
        w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1)
        sc.world = w
    if not any(o.type == "LIGHT" for o in sc.objects):
        for rot in ((50, 0, -30), (60, 0, 150)):
            l = bpy.data.objects.new("kn_sun", bpy.data.lights.new("kn_sun", "SUN"))
            l.data.energy = 2.5
            l.rotation_euler = [math.radians(r) for r in rot]
            sc.collection.objects.link(l)
    cam = sc.camera
    if cam is None:
        cd = bpy.data.cameras.new("kn_c")
        cam = bpy.data.objects.new("kn_c", cd)
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.3
    cu = bpy.data.curves.new("kn_cap", type="FONT")
    cap = bpy.data.objects.new("kn_cap", cu)
    cap.parent = cam
    cu.size = 0.09
    mat = bpy.data.materials.new("kn_cap_mat")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (1, 1, 1, 1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 3
    cu.materials.append(mat)
    sc.collection.objects.link(cap)
    cap.location = (-1.1, 1.05, -1)

    # rest baseline
    rig.animation_data_clear()
    bpy.context.view_layer.update()
    V, T = soup(hidden_body)
    base = set(inside_set(armor, BVHTree.FromPolygons(V, T)))
    pairs = layer_pairs(armor, body)

    def layer_inside():
        got = {}
        for outer, ob, inner in pairs:
            V2, T2 = soup(inner)
            got[outer] = inside_set([ob], BVHTree.FromPolygons(V2, T2))
        return got
    layer_base = {k: set(v) for k, v in layer_inside().items()}
    through_base = {k: through(ob, inner)[0] for k, ob, inner in pairs}
    tmp = Path(tempfile.mkdtemp(prefix="kn_mt_"))
    frame_i, results = 0, {}
    for name in [c.strip() for c in a.clips.split(",")]:
        path = syn / CLIPS[name].format(sex=sex, s=s)
        if not path.is_file():
            results[name] = "clip not found"
            continue
        act = load_action(path, rig)
        if act is None:
            results[name] = "no action in the clip"
            continue
        f0, f1 = (int(x) for x in act.frame_range)
        worst_n, worst_d, worst_f = 0, 0.0, f0
        lw = {k: [0, 0.0, f0] for k in layer_base}
        tw = {k: [0, 0.0, f0] for k in layer_base}
        for f in range(f0, f1 + 1, a.every):
            sc.frame_set(f)
            V, T = soup(hidden_body)
            ins = inside_set(armor, BVHTree.FromPolygons(V, T))
            extra = {i: d for i, d in ins.items() if i not in base}
            if len(extra) > worst_n:
                worst_n, worst_f = len(extra), f
            if extra:
                worst_d = max(worst_d, max(extra.values()))
            n_layer = 0
            for k, got in layer_inside().items():
                ex = {i: d for i, d in got.items() if i not in layer_base[k]}
                n_layer += len(ex)
                if len(ex) > lw[k][0]:
                    lw[k][0], lw[k][2] = len(ex), f
                if ex:
                    lw[k][1] = max(lw[k][1], max(ex.values()))
            for k, ob_, inner_ in pairs:
                n_t, d_t = through(ob_, inner_)
                n_t = max(0, n_t - through_base[k])
                if n_t > tw[k][0]:
                    tw[k][0], tw[k][2] = n_t, f
                if n_t:
                    tw[k][1] = max(tw[k][1], d_t)
            if a.video:
                hips = rig.matrix_world @ rig.pose.bones["pelvis"].head
                d = Vector((0.7, -1, 0.25)).normalized()
                cam.location = Vector((hips.x, hips.y, 0.95)) + d * 20
                cam.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
                cu.body = f"{name}  {f}  inside: {len(extra)}" + (f"  layers: {n_layer}" if pairs else "")
                sc.render.filepath = str(tmp / f"f_{frame_i:05d}.png")
                bpy.ops.render.render(write_still=True)
                frame_i += 1
        results[name] = {"frames": f"{f0}-{f1}", "worst_frame": worst_f, "vertices_sunk_in": worst_n,
                         "deepest_cm": round(100 * worst_d, 1)}
        if pairs:
            results[name]["layers"] = {k: {"vertices_sunk_in": v[0], "deepest_cm": round(100 * v[1], 1),
                                           "worst_frame": v[2], "inner_points_through": tw[k][0],
                                           "through_cm": round(100 * tw[k][1], 1), "through_frame": tw[k][2]}
                                       for k, v in lw.items()}
    if a.video and frame_i:
        Path(a.video).parent.mkdir(parents=True, exist_ok=True)
        subprocess.check_call([shutil.which("ffmpeg"), "-y", "-loglevel", "error", "-framerate", "30", "-i",
                               str(tmp / "f_%05d.png"), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags",
                               "+faststart", a.video])
    shutil.rmtree(tmp, ignore_errors=True)
    kn.report("Gear motion test", {"sex": sex, "shape": values, "armor_vertices": sum(len(o.data.vertices) for o in armor),
                                   "inside_at_rest": len(base),
                                   "layers": {k: f"{o.name.split('_')[4]} over {', '.join(i.name.split('_')[4] for i in inn)}"
                                              f" ({len(layer_base[k])} inside, {through_base[k]} through at rest)" for k, o, inn in pairs},
                                   "clips": results, "video": a.video})


main()
