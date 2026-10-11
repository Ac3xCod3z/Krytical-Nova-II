"""Finish a pet or prop model in Synty's POLYGON style: clean transforms, palette texture, shading, checks.

  run_blender.py --blend FILE.blend pet_finish.py --name SM_Pet_Emberwing_S1
        [--objects A,B]         default: every mesh in the file
        [--size 0.6]            longest side in metres (Synty's drones are 0.7 to 2.5 m)
        [--pivot centre|base|keep]   where the model's origin goes (pets hover: centre)
        [--sharp-angle 30] [--join] [--palette-dir DIR] [--max-tris 2500]

One material per colour while modelling. Each material is matched to the closest colour of the shared
Krytical Nova swatch (make_swatch.py) and every face's UVs are put on that colour, which is how Synty's
packs are textured: one small texture for everything. --own-palette bakes a palette just for this model. Moving pieces (wings, rotors)
stay separate objects under the body, each turning about its own origin. Safe to run again.

A pet bound to a rig (Rigify or any armature) is left where its rig holds it: it gets the palette, the
shading and the weight checks, but is not moved, resized or joined.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers

GRID = 16


def linear_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def write_png(path, cell_px, colours):
    size = GRID * cell_px
    px = [0.0, 0.0, 0.0, 1.0] * (size * size)
    for index, rgb in enumerate(colours):
        cu, cv = index % GRID, GRID - 1 - index // GRID
        for y in range(cv * cell_px, (cv + 1) * cell_px):
            for x in range(cu * cell_px, (cu + 1) * cell_px):
                i = (y * size + x) * 4
                px[i], px[i + 1], px[i + 2] = (linear_to_srgb(c) for c in rgb[:3])
    img = bpy.data.images.new(Path(path).stem, size, size, alpha=False)
    img.pixels = px
    img.filepath_raw = str(path)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--objects")
    ap.add_argument("--size", type=float)
    ap.add_argument("--pivot", choices=("centre", "base", "keep"), default="centre")
    ap.add_argument("--sharp-angle", type=float, default=30.0)
    ap.add_argument("--join", action="store_true")
    ap.add_argument("--palette-dir")
    ap.add_argument("--cell-px", type=int, default=4)
    ap.add_argument("--max-tris", type=int, default=2500)
    ap.add_argument("--assign", help='choose swatch colours by hand: "Colour 01=Fur 03,Colour 02=Orange Bold 5" '
                                     "(the material name may be given without the object's name in front)")
    ap.add_argument("--swatch", choices=("kn", "synty"), default="kn",
                    help="which page of the shared swatch to match colours to: ours, or Synty's own sci-fi colours")
    ap.add_argument("--own-palette", action="store_true",
                    help="bake this model's own palette texture instead of using the shared KN swatch")
    a = ap.parse_args(kn.tool_args())

    problems, warnings = [], []
    if a.objects:
        meshes = [kn.get_object(n.strip()) for n in a.objects.split(",")]
    else:
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No mesh in the file.")
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    rigs = {kn.bound_rig(o) for o in meshes} - {None}
    if len(rigs) > 1:
        raise SystemExit(f"The meshes are bound to more than one rig: {sorted(r.name for r in rigs)}. Use one rig per pet.")
    rig = rigs.pop() if rigs else None
    if rig:
        meshes = [o for o in meshes if kn.bound_rig(o) is rig]
    for o in meshes:
        if o.data.users > 1:
            o.data = o.data.copy()
        other = [m.name for m in o.modifiers if m.type != "ARMATURE"]
        if other:
            warnings.append(f"{o.name}: modifiers are not exported; apply them first ({', '.join(other)}).")

    # The body is the mesh that is already the parent of the others, else the biggest one.
    def volume(o):
        d = o.dimensions
        return d.x * d.y * d.z

    roots = [o for o in meshes if o.parent not in meshes]
    root = max(roots, key=volume)

    if rig:
        # A skinned pet: the mesh must stay where its rig holds it, so nothing is moved or resized here.
        bpy.context.view_layer.update()
        for o in [rig] + meshes:
            loc, rot, scale = o.matrix_world.decompose()
            if any(abs(x - 1) > 1e-3 for x in scale) or rot.angle > 1e-3:
                warnings.append(f"{o.name} carries a rotation or scale. Apply it (Ctrl+A) on the mesh and the "
                                "metarig before generating the rig; a scaled rig comes into Unity scaled.")
        if a.size or a.join:
            warnings.append("--size and --join are ignored for a rigged pet: size the model and metarig before "
                            "generating the rig.")
        finish_look(a, meshes, root, problems, warnings, rig)
        return

    # 1. Clean transforms: rotation and scale go into the mesh, only a position stays on each object.
    bpy.context.view_layer.update()
    world = {o: o.matrix_world.copy() for o in meshes}
    for o in meshes:
        o.parent = None
    for o in meshes:
        loc, rot, scale = world[o].decompose()
        m = Matrix.LocRotScale(None, rot, scale)
        o.data.transform(m)
        if m.determinant() < 0:
            o.data.flip_normals()
        o.matrix_world = Matrix.Translation(loc)
    bpy.context.view_layer.update()

    if a.join and len(meshes) > 1:
        with bpy.context.temp_override(active_object=root, selected_editable_objects=meshes, selected_objects=meshes):
            bpy.ops.object.join()
        meshes = [root]
        bpy.context.view_layer.update()

    def all_points():
        return [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]

    # 2. Size.
    pts = all_points()
    lo = Vector((min(p[i] for p in pts) for i in range(3)))
    hi = Vector((max(p[i] for p in pts) for i in range(3)))
    if a.size:
        factor = a.size / max(hi - lo)
        origin = root.matrix_world.translation.copy()
        for o in meshes:
            o.data.transform(Matrix.Scale(factor, 4))
            o.matrix_world = Matrix.Translation(origin + (o.matrix_world.translation - origin) * factor)
        bpy.context.view_layer.update()
        pts = all_points()
        lo = Vector((min(p[i] for p in pts) for i in range(3)))
        hi = Vector((max(p[i] for p in pts) for i in range(3)))

    # 3. The body's origin, and the whole model to the centre of the world.
    if a.pivot != "keep":
        target = (lo + hi) / 2
        if a.pivot == "base":
            target.z = lo.z
        shift = root.matrix_world.translation - target
        root.data.transform(Matrix.Translation(shift))
        root.matrix_world = Matrix.Translation(target)
        bpy.context.view_layer.update()
    offset = root.matrix_world.translation.copy()
    for o in meshes:
        o.matrix_world = Matrix.Translation(o.matrix_world.translation - offset)
    bpy.context.view_layer.update()
    for o in meshes:
        if o is not root:
            o.parent = root
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_basis = Matrix.Translation(o.matrix_world.translation - root.matrix_world.translation)
    bpy.context.view_layer.update()
    finish_look(a, meshes, root, problems, warnings, None)


def finish_look(a, meshes, root, problems, warnings, rig):
    """Palette, shading, names, checks and the report: the same for a static pet and a rigged one."""

    def all_points():
        return [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]

    # 4. Colour. With the shared KN swatch each material goes to the swatch cell closest to its colour, and
    #    every model uses the one swatch texture. Otherwise one cell per material in a palette of its own.
    swatch = None if a.own_palette else kn.load_swatch(a.swatch)
    chosen = {}
    if a.assign:
        if not swatch:
            raise SystemExit("--assign needs the shared swatch (run make_swatch.py, and do not pass --own-palette).")
        by_name = {kn.cell_key(c["name"]): c for c in swatch["cells"]}
        for pair in a.assign.split(","):
            left, _eq, right = pair.partition("=")
            if kn.cell_key(right) not in by_name:
                raise SystemExit(f"'{right.strip()}' is not a swatch colour. See KN_Swatch.json for the names.")
            chosen[kn.cell_key(left)] = by_name[kn.cell_key(right)]
    order, colours, emission, lit = {}, [], [], False
    cell_of, mapping = {}, {}
    for o in meshes:
        me = o.data
        uv = me.uv_layers.get("map1") or (me.uv_layers[0] if me.uv_layers else me.uv_layers.new(name="map1"))
        uv.name = "map1"
        me.uv_layers.active = uv
        per_poly = [None] * len(me.polygons)
        for p in me.polygons:
            mat = me.materials[p.material_index] if p.material_index < len(me.materials) else None
            if mat is None:
                problems.append(f"{o.name}: faces without a material; every face needs a colour.")
                break
            key = kn.cell_key(mat.name)
            if key not in order:
                if len(order) >= GRID * GRID:
                    raise SystemExit("More than 256 colours; that is not a palette.")
                order[key] = len(order)
                col, emis = kn.material_colour(mat)
                colours.append(col)
                emission.append(emis)
                lit = lit or any(c > 0 for c in emis)
                if swatch:
                    metal = False
                    if mat.node_tree:
                        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                        metal = bool(bsdf and bsdf.inputs["Metallic"].default_value > 0.5)
                    glow = any(c > 0 for c in emis)
                    hit, dist = kn.nearest_swatch(swatch, emis if glow else col, glow=glow, metal=metal)
                    picked = next((c for k2, c in chosen.items() if key == k2 or key.endswith(k2)), None)
                    if picked:
                        hit, dist = picked, 0.0
                        glow = picked["glow"]
                    mapping[key] = (mat.name, hit, dist)
                    # Show the swatch's colour from now on, so the viewport and the sheets are what the game draws.
                    lin = tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in hit["rgb"])
                    mat.diffuse_color = (*lin, 1.0)
                    if mat.node_tree:
                        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                        if bsdf:
                            bsdf.inputs["Emission Color" if glow else "Base Color"].default_value = (*lin, 1.0)
            index = order[key]
            cell = (index % GRID, GRID - 1 - index // GRID)
            if swatch:
                cell = (mapping[key][1]["u"], mapping[key][1]["v"])
            per_poly[p.index] = cell
            point = kn.cell_uv(*cell, grid=GRID)
            for li in p.loop_indices:
                uv.data[li].uv = point
        cell_of[o] = per_poly

    base = a.name
    tex = base[3:] if base.startswith("SM_") else base
    out_dir = Path(a.palette_dir) if a.palette_dir else Path(bpy.data.filepath).parent if bpy.data.filepath else None
    files = {}
    if swatch:
        files["swatch"] = swatch["image"]
        bpy.context.scene["kn_palette"] = swatch["image"]
        far = [f"{m} -> {c['name']}" for m, c, d in mapping.values() if d > 0.45]
        if far:
            warnings.append(f"No close swatch colour for: {far}. Pick another colour, or add one to a free cell "
                            "in make_swatch.py.")
    elif out_dir and colours:
        out_dir.mkdir(parents=True, exist_ok=True)
        files["palette"] = str(out_dir / f"T_{tex}_Palette.png")
        write_png(files["palette"], a.cell_px, colours)
        if lit:
            files["emission"] = str(out_dir / f"T_{tex}_Emission.png")
            write_png(files["emission"], a.cell_px, emission)
        files["colours"] = str(out_dir / f"T_{tex}_Palette.json")
        names = {}
        for o in meshes:
            for m in o.data.materials:
                if m:
                    names.setdefault(kn.cell_key(m.name), m.name)
        with open(files["colours"], "w", encoding="utf-8") as fh:
            json.dump([{"cell": [i % GRID, GRID - 1 - i // GRID], "material": names[k]} for k, i in order.items()], fh, indent=1)
        bpy.context.scene["kn_palette"] = files["palette"]
    elif not out_dir:
        warnings.append("The file is not saved, so no palette texture was written. Pass --palette-dir.")

    # 5. Shading and names.
    hard = sum(kn.set_hard_edges(o.data, a.sharp_angle, cell_of[o]) for o in meshes)
    for o in meshes:
        if o is root:
            o.name = base
        elif not o.name.startswith(base + "_"):
            o.name = base + "_" + o.name.replace(" ", "_")
        o.data.name = o.name
    bpy.context.scene["kn_pet_root"] = root.name
    skin = {}
    if rig:
        rig.name = base + "_Rig"
        deform = {b.name for b in rig.data.bones if b.use_deform}
        loose = heavy = 0
        used = set()
        for o in meshes:
            names = [g.name for g in o.vertex_groups]
            stray = sorted(n for n in names if n not in deform and n in rig.data.bones)
            if stray:
                warnings.append(f"{o.name}: weighted to bones that do not deform and will not be exported: {stray[:6]}")
            for v in o.data.vertices:
                on = [names[g.group] for g in v.groups if g.weight > 1e-5 and names[g.group] in deform]
                used.update(on)
                loose += not on
                heavy += len(on) > 4
        if loose:
            problems.append(f"{loose} vertices are not weighted to any deforming bone; they will stay behind when the pet moves.")
        if heavy:
            warnings.append(f"{heavy} vertices are on more than four bones; Unity keeps the strongest four.")
        skin = {"rig": rig.name, "deform_bones": len(deform), "deform_bones_used": len(used),
                "control_and_helper_bones_left_out_of_export": len(rig.data.bones) - len(deform)}

    # Checks.
    tris = sum(kn.tri_count(o.data) for o in meshes)
    if tris > a.max_tris:
        warnings.append(f"{tris} triangles; Synty's drones run 650 to 2,200.")
    ngons = sum(1 for o in meshes for p in o.data.polygons if len(p.vertices) > 4)
    if ngons:
        warnings.append(f"{ngons} faces have more than four sides.")
    if not base.startswith("SM_"):
        warnings.append("The project names mesh assets SM_<Group>_<Variant>.")
    if len(colours) > 24:
        warnings.append(f"{len(colours)} colours. Synty's drones use 5 to 10; fewer, bolder colours read better.")
    pts = all_points()
    dims = [round(max(p[i] for p in pts) - min(p[i] for p in pts), 3) for i in range(3)]

    kn.save_blend()
    kn.report("Pet finish", {
        "root": root.name, "pieces": [o.name for o in meshes if o is not root], "size_m": dims, "triangles": tris,
        "skin": skin,
        "colours": len(colours), "hard_edges": hard, "files": files,
        "swatch_colours": {m: f"{c['name']} {c['colour']}" for m, c, _d in mapping.values()},
        "next": "render_sheet.py to look at it, then pet_export.py."}, problems, warnings)
    if problems:
        sys.exit(1)


main()
