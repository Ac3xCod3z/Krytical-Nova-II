"""Turn a dense, textured model (Tripo or any AI / scan output) into the flat-colour low-poly style.

  run_blender.py --blend FILE.blend pet_from_scan.py --object "translucent fox 3d model"
        [--tris 2000]        triangle target (Synty's drones are 650 to 2,200)
        [--colours 8]        how many flat colours to keep (Synty's drones use 5 to 10)
        [--smooth 2]         passes that tidy stray single-face specks of colour

What it does: keeps the original as "<object>_source" (hidden), makes a reduced copy, and gives every face
of the copy one flat colour picked from the original's texture, grouped into a small palette. One material
per colour, named Colour 01.. with the colour as its viewport colour. Run pet_finish.py afterwards: it
bakes those materials into the palette texture and sets the hard edges.

Run it again with other numbers as often as needed; it always starts from the kept source.
"""
import argparse
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib

import kn_common as kn

importlib.reload(kn)      # a live Blender session keeps modules loaded; always use the current helpers


def base_colour_image(ob):
    for slot in ob.material_slots:
        mat = slot.material
        if not mat or not mat.node_tree:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED" and node.inputs["Base Color"].is_linked:
                src = node.inputs["Base Color"].links[0].from_node
                if src.type == "TEX_IMAGE" and src.image:
                    return src.image
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                return node.image
    return None


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def kmeans(points, weights, k, rounds=40):
    """Weighted k-means with a fixed, spread-out start, so the same model always gives the same palette."""
    centres = [points[np.argmax(weights)]]
    for _ in range(1, k):
        d = np.min([((points - c) ** 2).sum(1) for c in centres], axis=0)
        centres.append(points[np.argmax(d * weights)])
    centres = np.array(centres)
    for _ in range(rounds):
        label = np.argmin(((points[:, None, :] - centres[None, :, :]) ** 2).sum(2), axis=1)
        new = centres.copy()
        for i in range(k):
            pick = label == i
            if pick.any():
                new[i] = (points[pick] * weights[pick, None]).sum(0) / weights[pick].sum()
        if np.allclose(new, centres, atol=1e-5):
            break
        centres = new
    return centres, np.argmin(((points[:, None, :] - centres[None, :, :]) ** 2).sum(2), axis=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--object", required=True)
    ap.add_argument("--tris", type=int, default=2000)
    ap.add_argument("--colours", type=int, default=8)
    ap.add_argument("--smooth", type=int, default=2)
    ap.add_argument("--name", help="name for the converted model (default: the object's own name)")
    ap.add_argument("--bold", type=float, default=1.0,
                    help="1 keeps the texture's colours; 2 doubles their saturation and deepens the contrast")
    ap.add_argument("--merge", type=float, default=0.085,
                    help="colours closer than this (0..1) become one, so a soft gradient is one flat colour")
    ap.add_argument("--features", type=int, default=10,
                    help="small markings (eyes, nose) kept as their own little pieces of surface; 0 for none")
    a = ap.parse_args(kn.tool_args())

    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    name = a.object[:-7] if a.object.endswith("_source") else a.object
    source = bpy.data.objects.get(name + "_source")
    if source is None:
        source = kn.get_object(name)
        source.name = name + "_source"
        source.data.name = name + "_source"
    out_name = a.name or name
    for stale in {name, out_name}:
        old = bpy.data.objects.get(stale)
        if old is not None and old is not source and old.type == "MESH":
            me_old = old.data
            bpy.data.objects.remove(old)
            bpy.data.meshes.remove(me_old)
    name = out_name

    image = base_colour_image(source)
    src_me = source.data
    if image is None or not src_me.uv_layers:
        raise SystemExit("The model has no base colour texture or no UVs to take colours from.")
    w, h = image.size
    px = np.empty(w * h * 4, np.float32)
    image.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[:, :, :3]

    # The original surface, in world space, with its UVs.
    m = source.matrix_world
    s_verts = [m @ v.co for v in src_me.vertices]
    src_me.calc_loop_triangles()
    uv = src_me.uv_layers.active.data
    s_tris = [tuple(t.vertices) for t in src_me.loop_triangles]
    s_uvs = [tuple(uv[l].uv.copy() for l in t.loops) for t in src_me.loop_triangles]
    bvh = BVHTree.FromPolygons(s_verts, s_tris)

    def colour_at(point):
        loc, _n, idx, _d = bvh.find_nearest(point)
        tri = s_tris[idx]
        bw = poly_3d_calc([s_verts[i] for i in tri], loc)
        u = sum(s_uvs[idx][i][0] * bw[i] for i in range(3)) % 1.0
        v = sum(s_uvs[idx][i][1] * bw[i] for i in range(3)) % 1.0
        return px[min(h - 1, int(v * h)), min(w - 1, int(u * w))]

    # The reduced copy: weld the seams the UVs cut, then collapse to the target.
    bm = bmesh.new()
    bm.from_mesh(src_me)
    bm.transform(m)
    size = max(source.dimensions) or 1.0
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    low_me = bpy.data.meshes.new(name)
    bm.to_mesh(low_me)
    bm.free()
    low = bpy.data.objects.new(name, low_me)
    for col in source.users_collection:
        col.objects.link(low)
    start = len(low_me.polygons)
    if start > a.tris:
        mod = low.modifiers.new("Reduce", "DECIMATE")
        mod.ratio = a.tris / start
        mod.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
        reduced = bpy.data.meshes.new_from_object(low.evaluated_get(dg))
        low.modifiers.clear()
        low.data = reduced
        bpy.data.meshes.remove(low_me)
        low_me = reduced
        low_me.name = name
    while low_me.uv_layers:
        low_me.uv_layers.remove(low_me.uv_layers[0])
    for attr in [x for x in low_me.color_attributes]:
        low_me.color_attributes.remove(attr)

    # One colour per face: the middle of several samples taken across it, so a stray pixel cannot decide.
    verts = [v.co for v in low_me.vertices]
    face_colour = np.zeros((len(low_me.polygons), 3), np.float32)
    area = np.zeros(len(low_me.polygons), np.float32)
    for p in low_me.polygons:
        corners = [verts[i] for i in p.vertices]
        centre = sum(corners, Vector()) / len(corners)
        samples = [centre] + [centre.lerp(c, 0.55) for c in corners] + \
                  [corners[i].lerp(corners[(i + 1) % len(corners)], 0.5).lerp(centre, 0.3) for i in range(len(corners))]
        face_colour[p.index] = np.median(np.array([colour_at(q) for q in samples]), axis=0)
        area[p.index] = max(p.area, 1e-9)

    # Find many colours first, then fold together the ones that are nearly the same. Left alone, a soft
    # gradient eats the whole palette as bands; folded, it is one flat colour and small distinct ones survive.
    k = max(2, min(max(a.colours * 2, 12), len(low_me.polygons)))
    centres, label = kmeans(face_colour, area, k)
    weight = np.array([area[label == i].sum() for i in range(k)])
    alive = [i for i in range(k) if weight[i] > 0]
    while len(alive) > 1:
        pairs = [(float(np.linalg.norm(centres[i] - centres[j])), i, j) for n, i in enumerate(alive) for j in alive[n + 1:]]
        dist, i, j = min(pairs)
        if dist > a.merge and len(alive) <= a.colours:
            break
        centres[i] = (centres[i] * weight[i] + centres[j] * weight[j]) / (weight[i] + weight[j])
        weight[i] += weight[j]
        label[label == j] = i
        alive.remove(j)
    remap = {old: new for new, old in enumerate(alive)}
    label = np.array([remap[int(l)] for l in label])
    centres = centres[alive]
    k = len(alive)

    # Tidy: a face that disagrees with most of its neighbours takes their colour.
    bm = bmesh.new()
    bm.from_mesh(low_me)
    bm.faces.ensure_lookup_table()
    for _ in range(max(0, a.smooth)):
        new = label.copy()
        for f in bm.faces:
            near = [e2.index for e in f.edges for e2 in e.link_faces if e2 is not f]
            if len(near) >= 2:
                votes = np.bincount(label[near], minlength=k)
                best = int(votes.argmax())
                if best != label[f.index] and votes[best] >= max(2, len(near) - 1) and votes[label[f.index]] == 0:
                    new[f.index] = best
        label = new
    bm.free()

    # Small markings the reduced mesh is too coarse to carry (eyes, a nose, a spot): where the original's
    # colour differs clearly from the flat colour now there, that bit of the original surface is kept as
    # its own small piece, lifted a hair off the model. Synty models eyes as geometry for the same reason.
    features = []
    if a.features > 0:
        low_bvh = BVHTree.FromPolygons([v.co for v in low_me.vertices], [tuple(p.vertices) for p in low_me.polygons])
        marked, key_of = [], {}
        for ti, tri in enumerate(s_tris):
            pts = [s_verts[i] for i in tri]
            centre = (pts[0] + pts[1] + pts[2]) / 3
            col = np.median(np.array([colour_at(centre)] + [colour_at(centre.lerp(q, 0.5)) for q in pts]), axis=0)
            hit = low_bvh.find_nearest(centre)
            if hit[2] is not None and np.linalg.norm(col - centres[label[hit[2]]]) > 0.2:
                marked.append((ti, col))
        owner = {}
        for ti, _c in marked:
            for vi in s_tris[ti]:
                owner.setdefault(tuple(round(c / (size * 1e-4)) for c in s_verts[vi]), []).append(ti)
        seen, groups = set(), []
        colour_of = dict(marked)
        for ti, _c in marked:
            if ti in seen:
                continue
            stack, group = [ti], []
            seen.add(ti)
            while stack:
                cur = stack.pop()
                group.append(cur)
                for vi in s_tris[cur]:
                    for other in owner[tuple(round(c / (size * 1e-4)) for c in s_verts[vi])]:
                        if other not in seen:
                            seen.add(other)
                            stack.append(other)
            groups.append(group)

        def tri_area(ti):
            p0, p1, p2 = (s_verts[i] for i in s_tris[ti])
            return (p1 - p0).cross(p2 - p0).length / 2

        total = float(area.sum())
        sized = [(sum(tri_area(t) for t in g), g) for g in groups]
        sized = [(ar, g) for ar, g in sized if total * 2e-5 < ar < total * 0.02 and len(g) >= 2]
        sized.sort(key=lambda x: -x[0])
        bm = bmesh.new()
        bm.from_mesh(low_me)
        extra_labels = []
        for ar, g in sized[:a.features]:
            mean = np.mean(np.array([colour_of[t] for t in g]), axis=0)
            near = int(np.argmin(np.linalg.norm(centres - mean, axis=1)))
            if np.linalg.norm(centres[near] - mean) > 0.12:
                centres = np.vstack([centres, mean])
                near = len(centres) - 1
            made = {}
            for t in g:
                p0, p1, p2 = (s_verts[i] for i in s_tris[t])
                normal = (p1 - p0).cross(p2 - p0)
                normal = normal.normalized() if normal.length > 0 else Vector((0, 0, 1))
                corners = []
                for vi in s_tris[t]:
                    key = tuple(round(c / (size * 1e-4)) for c in s_verts[vi])
                    if key not in made:
                        made[key] = bm.verts.new(s_verts[vi] + normal * size * 0.003)
                    corners.append(made[key])
                if len(set(corners)) == 3:
                    try:
                        bm.faces.new(corners)
                        extra_labels.append(near)
                    except ValueError:
                        pass
            features.append({"triangles": len(g), "area_cm2": round(ar * 1e4, 1)})
        bm.to_mesh(low_me)
        bm.free()
        k = len(centres)
        if extra_labels:
            label = np.concatenate([label, np.array(extra_labels, dtype=label.dtype)])
            extra = len(extra_labels)
            face_colour = np.vstack([face_colour, centres[np.array(extra_labels)]])
            area = np.concatenate([area, np.array([p.area for p in low_me.polygons][-extra:], np.float32)])

    # Biggest colour first; one material each.
    share = np.array([area[label == i].sum() for i in range(k)])
    order = [i for i in np.argsort(-share) if share[i] > 0]
    low_me.materials.clear()
    palette = []
    for rank, i in enumerate(order):
        rgb = np.array([float(x) for x in area[label == i] @ face_colour[label == i] / share[i]])
        if a.bold != 1.0:
            import colorsys
            hh, ss, vv = colorsys.rgb_to_hsv(*[float(c) for c in rgb])
            ss = min(1.0, ss * a.bold)
            vv = min(1.0, max(0.0, 0.5 + (vv - 0.5) * (1 + 0.35 * (a.bold - 1))))
            rgb = np.array(colorsys.hsv_to_rgb(hh, ss, vv))
        # Texture pixels are stored sRGB-encoded; a material's colour is linear.
        encoded = image.colorspace_settings.name == "sRGB" and not image.is_float
        lin = srgb_to_linear(rgb) if encoded else rgb
        mat_name = f"Colour {rank + 1:02d}"
        mat = bpy.data.materials.get(f"{name} {mat_name}") or bpy.data.materials.new(f"{name} {mat_name}")
        mat.diffuse_color = (float(lin[0]), float(lin[1]), float(lin[2]), 1.0)
        if mat.node_tree is None:
            mat.use_nodes = True
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (float(lin[0]), float(lin[1]), float(lin[2]), 1.0)
            bsdf.inputs["Roughness"].default_value = 0.85
        low_me.materials.append(mat)
        shown = rgb if encoded else np.array([c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055 for c in rgb])
        hexa = "#%02X%02X%02X" % tuple(int(round(255 * float(c))) for c in np.clip(shown, 0, 1))
        palette.append({"material": mat.name, "colour": hexa, "share_pct": round(100 * float(share[i] / share.sum()), 1)})
    rank_of = {i: r for r, i in enumerate(order)}
    low_me.polygons.foreach_set("material_index", [rank_of[int(l)] for l in label])
    low_me.update()

    source.hide_set(True) if source.name in bpy.context.view_layer.objects else None
    source.hide_render = True
    kn.save_blend()
    kn.report("Pet from scan", {
        "object": low.name, "source_kept_as": source.name, "texture": f"{image.name} {w}x{h}",
        "triangles": {"before": len(s_tris), "after": kn.tri_count(low_me), "asked": a.tris},
        "palette": palette, "small_markings_kept": features, "size_m": [round(x, 3) for x in low.dimensions],
        "next": "render_sheet.py to look at it; pet_finish.py --name SM_Pet_<Name>_S1 when the colours are right."})


main()
