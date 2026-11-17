# blender -b <checkpoint.blend> --python export_objects.py -- <checkpoint number>
# Writes CRUCIBLE_<n>_OBJECTS.json (every object with what Unity needs to place it) and IMPORT-REPORT_<n>.md (the summary).
import bpy, sys, os, json, math, collections
from mathutils import Vector
cp = sys.argv[sys.argv.index('--') + 1]
R = os.path.dirname(bpy.data.filepath)
D = bpy.data
def r3(v, n=3): return [round(float(x), n) for x in v]
def tris(me): return sum(len(p.vertices) - 2 for p in me.polygons)
def wbox(o):
    c = [o.matrix_world @ Vector(v) for v in o.bound_box]
    return [min(p[i] for p in c) for i in range(3)], [max(p[i] for p in c) for i in range(3)]
bpy.context.view_layer.update()
objs = []; by_coll = collections.OrderedDict(); prefabs = collections.Counter(); customs = collections.Counter(); tri_by = collections.Counter(); kept = collections.Counter()
total = 0
for o in sorted(D.objects, key=lambda o: o.name):
    coll = o.users_collection[0].name if o.users_collection else ''
    if coll == 'Lights_PreviewOnly' or o.get('preview_only') or (o.parent and o.parent.get('preview_only')): continue
    e = dict(name=o.name, collection=coll, type=o.type, position=r3(o.matrix_world.translation), rotation_deg=r3([math.degrees(a) for a in o.matrix_world.to_euler()], 2), scale=r3(o.matrix_world.to_scale()))
    if o.type == 'MESH':
        t = tris(o.data); total += t; lo, hi = wbox(o); e['size_m'] = r3([hi[i] - lo[i] for i in range(3)], 2); e['tris'] = t; e['mesh'] = o.data.name
        e['materials'] = [m.name for m in o.data.materials if m]
        if 'synty_prefab' in o or 'synty_prefab' in o.data:
            pk = o.get('synty_pack') or o.data.get('synty_pack'); pf = o.get('synty_prefab') or o.data.get('synty_prefab')
            e['kind'] = 'synty'; e['synty_pack'] = pk; e['synty_prefab'] = pf; e['pivot'] = r3(o.get('pivot', o.data.get('pivot', [0, 0, 0])))
            if 'material_variant' in o.data: e['material_variant'] = o.data['material_variant']
            prefabs[(pk, pf, o.data.get('material_variant', ''))] += 1; tri_by[pf] += t
        elif 'crucible_piece' in o or 'crucible_piece' in o.data:
            e['kind'] = 'custom'; e['piece'] = o.get('crucible_piece') or o.data.get('crucible_piece'); customs[e['piece']] += 1; tri_by[e['piece']] += t
        else:
            e['kind'] = 'layout'; kept[o.data.name.split('.')[0]] += 1; tri_by['(kept 007 layout meshes)'] += t
    else:
        e['kind'] = 'marker'
        for k in o.keys():
            if k not in ('_RNA_UI',) and isinstance(o[k], (int, float, str)): e[k] = o[k]
    objs.append(e); by_coll.setdefault(coll, collections.Counter())[e['kind']] += 1
mats = []
for m in D.materials:
    if not m.users or not m.use_nodes: continue
    d = dict(name=m.name)
    for k in ('variant_of', 'variant', 'synty_material', 'synty_pack', 'crucible_floor'):
        if k in m: d[k] = m[k]
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None); mx = next((n for n in m.node_tree.nodes if n.type == 'MIX'), None)
    if mx: d['tint'] = r3(mx.inputs[7].default_value[:3])
    if b:
        d['emission_strength'] = round(b.inputs['Emission Strength'].default_value, 2); d['emission_colour'] = r3(b.inputs['Emission Color'].default_value[:3])
        d['textures'] = [os.path.basename(n.image.filepath) for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image]
    mats.append(d)
lay = json.load(open(os.path.join(R, 'LAYOUT_%s.json' % cp) if os.path.exists(os.path.join(R, 'LAYOUT_%s.json' % cp)) else os.path.join(R, 'LAYOUT_007.json')))
val = json.load(open(os.path.join(R, 'VALIDATION_%s.json' % cp)))
json.dump(dict(checkpoint=cp, map='The Crucible', standalone=True, origin='Crucible heart; z=0 is the rim/yard level, arena floor z=-15', units='metres, Blender axes (X east, Y north, Z up)',
               layout=lay, validation=val, objects=objs, materials=mats), open(os.path.join(R, 'CRUCIBLE_%s_OBJECTS.json' % cp), 'w'), indent=0)
L = ["# The Crucible - import report, checkpoint %s" % cp, "",
     "Blend: `Crucible_%s_*.blend` (built by `Build009\\build_%s.py` from the validated 007 layout). Full data: `CRUCIBLE_%s_OBJECTS.json` (every object: name, collection," % (cp, cp, cp),
     "kind, position, rotation, scale, size, tris, Synty pack/prefab/pivot/material variant, or custom piece; all materials with tints/emission/textures; the 007 layout JSON; the validation).", "",
     "## Totals", "- Objects exported: %d (meshes %d, markers %d). Triangles: %d at Synty LOD0." % (len(objs), sum(1 for e in objs if e['type'] == 'MESH'), sum(1 for e in objs if e['kind'] == 'marker'), total),
     "- Synty placements: %d from %d distinct prefab+variant pairs. Custom placements: %d from %d pieces. Kept 007 layout meshes: %d." % (sum(prefabs.values()), len(prefabs), sum(customs.values()), len(customs), sum(kept.values())),
     "- Validation: %s (landing to heart %s m, spread %s m)." % ('PASS' if val['pass'] else 'FAIL', val['fairness']['grand_route_lengths_m'], val['fairness']['spread_m']), "",
     "## Size and bounds", "- Standalone map, origin at the Crucible heart. Playable square about 360 x 360 m: perimeter wall at +-118 m, yards out to +-163 m. Rim and yards at z = 0, arena floor at z = -7.5 (disc r = 82.3 m), seven rings of 1.07 m stone blocks to r = 103.7 m, four grand stairs (N, S, E, W) as the routes.",
     "- Yards 42 x 80 m each: Covenant east (+x), Hegemony west (-x), Null south (-y); sealed north yard 40 x 50 m. `MAP_Bounds_Crucible` marks a 500 x 500 x 240 m box. Outer ground plane at z = -10 out to +-245 m (non-playable).", "",
     "## Collections", "| Collection | synty | custom | layout | markers |", "|---|---|---|---|---|"]
for c, k in by_coll.items(): L.append("| %s | %d | %d | %d | %d |" % (c, k['synty'], k['custom'], k['layout'], k['marker']))
L += ["", "## Synty prefabs used (pack / prefab / material variant: count)", ""]
for (pk, pf, v), n in sorted(prefabs.items()): L.append("- %s / %s%s: %d" % (pk.replace('Polygon', ''), pf, (' [' + v + ']') if v else '', n))
L += ["", "## Custom pieces (made in Blender because no pack has them)", ""]
for p_, n in sorted(customs.items()): L.append("- %s: %d" % (p_, n))
L += ["", "## Kept 007 layout meshes (walkable shapes, unchanged; new materials only)", ""] + ["- %s: %d" % (k, n) for k, n in sorted(kept.items())]
L += ["", "## Biggest triangle costs", ""] + ["- %s: %d" % (k, n) for k, n in tri_by.most_common(14)]
open(os.path.join(R, 'IMPORT-REPORT_%s.md' % cp), 'w').write('\n'.join(L) + '\n')
print('EXPORTED', len(objs), total)
