"""Cut a slatted vent into a front plate of a hard-surface piece (owner, 24 Oct 2026, Scrap Sentinel head: "the
mouth is missing the vent"; Tripo's Smart Mesh flattened the art's chin vent into one plate).

  blender -b <scene.blend> --python panel_vent.py -- --object <name> --rect x0,x1,z0,z1 [--depth 0.02]
        [--slats 3] [--colour 0.12,0.12,0.13]

--rect is the vent's place as fractions of the piece's own front view (x 0..1 left to right as seen from the front,
z 0..1 bottom to top), read off the art. The front plate under it is cut along the rectangle, the inside pushed back
--depth (walls come with it), and --slats horizontal slats are set across the opening, slightly sunk. Every new face
takes the texel of the piece's own texture nearest to --colour, so the fit pass colours it as the art's dark grey.
Saves the blend.
"""
import argparse
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--object", required=True)
    p.add_argument("--rect", required=True)
    p.add_argument("--depth", type=float, default=0.02)
    p.add_argument("--slats", type=int, default=3)
    p.add_argument("--colour", default="0.12,0.12,0.13")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def texel_uv(ob, rgb):
    img = next(n.image for m in ob.data.materials if m and m.node_tree
               for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    w, h = img.size
    step = max(1, w // 512)
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)[::step, ::step, :3]
    d = ((px - np.array(rgb, np.float32)) ** 2).sum(-1)
    r, c = np.unravel_index(np.argmin(d), d.shape)
    return ((c * step + 0.5) / w, (r * step + 0.5) / h)


def main():
    a = args()
    ob = bpy.data.objects[a.object]
    me = ob.data
    x0f, x1f, z0f, z1f = (float(t) for t in a.rect.split(","))
    uvc = texel_uv(ob, [float(t) for t in a.colour.split(",")])
    bm = bmesh.new()
    bm.from_mesh(me)
    lo = Vector([min(v.co[k] for v in bm.verts) for k in range(3)])
    hi = Vector([max(v.co[k] for v in bm.verts) for k in range(3)])
    x0, x1 = lo.x + (hi.x - lo.x) * x0f, lo.x + (hi.x - lo.x) * x1f
    z0, z1 = lo.z + (hi.z - lo.z) * z0f, lo.z + (hi.z - lo.z) * z1f

    def front_faces():
        bm.faces.ensure_lookup_table()
        out = []
        for f in bm.faces:
            c = f.calc_center_median()
            if f.normal.y < -0.5 and x0 - 0.06 < c.x < x1 + 0.06 and z0 - 0.06 < c.z < z1 + 0.06:
                out.append(f)
        return out
    # the front-most plate over the rectangle: cut it along the rectangle's four sides
    for co, no in (((x0, 0, 0), (1, 0, 0)), ((x1, 0, 0), (1, 0, 0)), ((0, 0, z0), (0, 0, 1)), ((0, 0, z1), (0, 0, 1))):
        faces = front_faces()
        geom = list({e for f in faces for e in f.edges}) + faces + list({v for f in faces for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector(co), plane_no=Vector(no))
    bm.faces.ensure_lookup_table()
    inside = [f for f in bm.faces if f.normal.y < -0.5
              and x0 < f.calc_center_median().x < x1 and z0 < f.calc_center_median().z < z1]
    if not inside:
        raise SystemExit("No front plate inside the rectangle.")
    front_y = min(f.calc_center_median().y for f in inside)
    inside = [f for f in inside if f.calc_center_median().y < front_y + 0.01]   # the outer plate only
    ext = bmesh.ops.extrude_face_region(bm, geom=inside)
    new_verts = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=new_verts, vec=Vector((0, a.depth, 0)))
    new_faces = {f for v in new_verts for f in v.link_faces}
    bmesh.ops.delete(bm, geom=inside, context="FACES")
    # slats across the opening, a little behind the plate face
    uvl = bm.loops.layers.uv.active
    made = list(new_faces)
    h = (z1 - z0) / (2 * a.slats + 1)
    for i in range(a.slats):
        za = z0 + h * (2 * i + 1)
        zb = za + h
        ya, yb = front_y + a.depth * 0.35, front_y + a.depth * 0.95
        inset = (x1 - x0) * 0.06
        r = bmesh.ops.create_cube(bm, size=1.0)
        for v in r["verts"]:
            v.co = Vector((x0 + inset if v.co.x < 0 else x1 - inset,
                           ya if v.co.y < 0 else yb,
                           za if v.co.z < 0 else zb))
        made += list({f for v in r["verts"] for f in v.link_faces})
    for f in made:
        if f.is_valid:
            for l in f.loops:
                l[uvl].uv = uvc
    bm.normal_update()
    bm.to_mesh(me)
    me.update()
    bpy.ops.wm.save_mainfile()
    print("VENT", {"rect_m": [round(x0, 3), round(x1, 3), round(z0, 3), round(z1, 3)], "front_y": round(front_y, 3),
                   "faces_cut": len(inside), "uv": [round(t, 4) for t in uvc]})


main()
