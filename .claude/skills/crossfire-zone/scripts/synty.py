# Synty kit loader (Fable session, 16 Nov 2026).
# A Synty FBX carries no usable material: the PREFAB says which .mat each submesh uses, and the .mat says which texture
# (pack atlas on UV0, or a world-projected "triplanar" brick that ignores UVs). This reads prefab -> .mat -> textures and
# builds the matching Blender material, so every piece shows its own pack's look. Read-only on the game repo.
import bpy, bmesh, os, re, json, math
from mathutils import Vector

SYN = r"C:\Users\dlaco\Krytical Nova II\Assets\Synty"
HERE = os.path.dirname(os.path.abspath(__file__))
_IDX_FILE = os.path.join(HERE, 'synty_guid_index.json')
_idx = None
_mesh_cache = {}
_mat_cache = {}
TRI_SCALE = 0.25      # metres -> triplanar UV, multiplied by the material's tiling


def index():
    """guid -> asset path for every asset under Assets/Synty (cached on disk)."""
    global _idx
    if _idx is not None: return _idx
    if os.path.exists(_IDX_FILE):
        _idx = json.load(open(_IDX_FILE)); return _idx
    _idx = {}
    for dp, _, fs in os.walk(SYN):
        for f in fs:
            if f.endswith('.meta') and f[:-5].lower().endswith(('.fbx', '.mat', '.png', '.tga', '.psd', '.jpg', '.prefab')):
                try:
                    head = open(os.path.join(dp, f), encoding='utf-8', errors='ignore').read(200)
                except OSError: continue
                m = re.search(r'guid: ([0-9a-f]{32})', head)
                if m: _idx[m.group(1)] = os.path.join(dp, f[:-5])
    json.dump(_idx, open(_IDX_FILE, 'w'))
    return _idx


def find_prefab(pack, name):
    for dp, _, fs in os.walk(os.path.join(SYN, pack)):
        if name + '.prefab' in fs: return os.path.join(dp, name + '.prefab')
    raise FileNotFoundError(pack + '/' + name)


def parse_prefab(path):
    """[(gameobject name, fbx path, [mat paths])] for every non-LOD mesh renderer in the prefab."""
    idx = index()
    t = open(path, encoding='utf-8', errors='ignore').read()
    docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', t, flags=re.M)
    gos, filters, rends = {}, {}, {}
    for i in range(1, len(docs), 3):
        typ, fid, body = docs[i], docs[i + 1], docs[i + 2]
        if typ == '1':
            m = re.search(r'm_Name: (.*)', body); gos[fid] = m.group(1).strip() if m else ''
        elif typ in ('33', '23', '137'):
            g = re.search(r'm_GameObject: \{fileID: (-?\d+)', body)
            if not g: continue
            if typ == '33':
                m = re.search(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]+)', body)
                if m: filters[g.group(1)] = m.group(2)
            else:
                sec = re.search(r'm_Materials:\s*\n((?:\s*- \{.*\}\s*\n)+)', body)
                rends[g.group(1)] = re.findall(r'guid: ([0-9a-f]+)', sec.group(1)) if sec else []
                if typ == '137':
                    m = re.search(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]+)', body)
                    if m: filters[g.group(1)] = m.group(2)
    out = []
    src = re.search(r'm_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+)', t)
    if not filters and src and idx.get(src.group(1), '').lower().endswith('.fbx'):
        # prefab variant of the model: material overrides are listed per renderer (target fileID) and slot
        ov = {}
        for m in re.finditer(r'target: \{fileID: (-?\d+),[^}]*\}\s*propertyPath: m_Materials\.Array\.data\[(\d+)\]\s*value:[^\n]*\n\s*objectReference: \{fileID: \d+, guid: ([0-9a-f]+)', t):
            ov.setdefault(m.group(1), {})[int(m.group(2))] = idx.get(m.group(3))
        groups = [[d.get(i) for i in range(max(d) + 1)] for d in ov.values()]
        groups.sort(key=len, reverse=True)
        return [('*%d' % i, idx[src.group(1)], g) for i, g in enumerate(groups)] or [('*0', idx[src.group(1)], [])]
    for g, fbx in filters.items():
        name = gos.get(g, '')
        if re.search(r'_LOD[1-9]', name): continue
        out.append((name, idx.get(fbx), [idx.get(x) for x in rends.get(g, [])]))
    return out


def parse_mat(path):
    t = open(path, encoding='utf-8', errors='ignore').read()
    idx = index()
    tex = {}
    for m in re.finditer(r'- (_\w+):\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]+).*\n\s*m_Scale: \{x: ([-\d.e]+), y: ([-\d.e]+)', t):
        p = idx.get(m.group(2))
        if p and os.path.exists(p): tex[m.group(1)] = (p, float(m.group(3)), float(m.group(4)))
    sect = t.split('m_Floats:')[-1]
    floats = {m.group(1): float(m.group(2)) for m in re.finditer(r'- (_\w+): ([-\d.e]+)\s*$', sect, flags=re.M)}
    cols = {m.group(1): tuple(float(m.group(i)) for i in (2, 3, 4, 5))
            for m in re.finditer(r'- (_\w+): \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}', t)}
    return tex, floats, cols


def _image(path):
    for im in bpy.data.images:
        if os.path.normcase(bpy.path.abspath(im.filepath)) == os.path.normcase(path): return im
    im = bpy.data.images.load(path)
    im.name = os.path.basename(os.path.dirname(os.path.dirname(os.path.dirname(path)))) + '_' + os.path.basename(path) \
        if 'Textures' in path else os.path.basename(path)
    return im


def _first(d, *keys):
    for k in keys:
        if k in d: return d[k]


def _bsdf_mat(name, base=None, tint=(1, 1, 1), emit=None, emit_col=(1, 1, 1), emit_strength=0.0, uvmap=None,
              closest=True, rough=0.85, tiling=(1, 1)):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs[0], out.inputs[0])
    b.inputs['Roughness'].default_value = rough; b.inputs['Specular IOR Level'].default_value = 0.15
    b.inputs['Base Color'].default_value = (*tint[:3], 1)
    src = None
    if uvmap or tiling != (1, 1):
        uv = nt.nodes.new('ShaderNodeUVMap')
        if uvmap: uv.uv_map = uvmap
        mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (tiling[0], tiling[1], 1)
        nt.links.new(uv.outputs[0], mp.inputs[0]); src = mp.outputs[0]
    if base:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = _image(base)
        t.interpolation = 'Closest' if closest else 'Linear'
        if src: nt.links.new(src, t.inputs[0])
        if tuple(round(c, 3) for c in tint[:3]) != (1, 1, 1):
            mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'
            mx.inputs[0].default_value = 1.0; mx.inputs[7].default_value = (*tint[:3], 1)
            nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
        else:
            nt.links.new(t.outputs[0], b.inputs['Base Color'])
    if emit_strength > 0:
        b.inputs['Emission Strength'].default_value = emit_strength
        b.inputs['Emission Color'].default_value = (*emit_col[:3], 1)
        if emit:
            e = nt.nodes.new('ShaderNodeTexImage'); e.image = _image(emit); e.interpolation = 'Closest'
            if src: nt.links.new(src, e.inputs[0])
            nt.links.new(e.outputs[0], b.inputs['Emission Color'])
    return mat


def material(mat_path, pack):
    """Blender material(s) for a Unity .mat. Returns dict(kind='uv'|'tri', mat=..., top=..., tiling=...)."""
    if mat_path in _mat_cache: return _mat_cache[mat_path]
    key = pack.replace('Polygon', '') + '/' + os.path.basename(mat_path)[:-4]
    tex, fl, col = parse_mat(mat_path)
    tint = _first(col, '_Color_Tint', '_BaseColor', '_Color') or (1, 1, 1, 1)
    side = tex.get('_Triplanar_Texture_Side')
    BASEKEYS = ('_Base_Texture', '_BaseMap', '_MainTex', '_Main_Texture', '_Albedo_Map', '_Texture', '_Albedo', '_MainTexture')
    if side and fl.get('_Enable_Triplanar_Texture', 0) >= 0.5 and not _first(tex, *BASEKEYS):
        top = tex.get('_Triplanar_Texture_Top', side)
        ts = col.get('_Triplanar_Tiling_Side', (1, 1, 0, 0)); tt = col.get('_Triplanar_Tiling_Top', (1, 1, 0, 0))
        r = dict(kind='tri',
                 mat=_bsdf_mat(key, side[0], tint, uvmap='UV_Tri', closest=False),
                 top=_bsdf_mat(key + '_Top', top[0], tint, uvmap='UV_Tri', closest=False),
                 tiling=(ts[0] or 1, tt[0] or 1))
    else:
        base = _first(tex, *BASEKEYS)
        em = _first(tex, '_Emission_Texture', '_EmissionMap', '_Emission_Map', '_Emission')
        emc = _first(col, '_Emission_Color', '_EmissionColor') or (1, 1, 1, 1)
        es = 0.0
        if em and (fl.get('_Enable_Emission_Texture', 0) or fl.get('_Enable_Emission', 0) or '_EmissionMap' in tex):
            es = 3.0
        elif not base and max(emc[:3]) > 0 and ('glow' in key.lower() or 'emiss' in key.lower() or 'light' in key.lower()):
            es = 3.0
        tl = (base[1], base[2]) if base else (1, 1)
        bt = col.get('_Base_Tiling')
        if base and bt and tl == (1, 1): tl = (bt[0] or 1, bt[1] or 1)
        n = key.lower()
        if not base and 'glass' in n: tint = (0.08, 0.1, 0.14, 1)
        r = dict(kind='uv', mat=_bsdf_mat(key, base[0] if base else None, tint, em[0] if em else None, emc, es,
                                          closest=(tl == (1, 1)), tiling=tl))
    for k in ('mat', 'top'):
        if r.get(k): r[k]['synty_material'] = os.path.basename(mat_path)[:-4]; r[k]['synty_pack'] = pack
    _mat_cache[mat_path] = r
    return r


def box_uv(me, slots_tri):
    """Write UV_Tri (box projection in metres * tiling) for faces whose slot is triplanar; up faces go to the top slot.
    slots_tri: {slot index: (top slot index, side tiling, top tiling)}"""
    bm = bmesh.new(); bm.from_mesh(me)
    uvl = bm.loops.layers.uv.get('UV_Tri') or bm.loops.layers.uv.new('UV_Tri')
    for f in bm.faces:
        if f.material_index not in slots_tri: continue
        top_i, ts, tt = slots_tri[f.material_index]
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        if a == 2 and n.z > 0.7 and top_i is not None:
            f.material_index = top_i; s = TRI_SCALE * tt
        else: s = TRI_SCALE * ts
        for l in f.loops:
            c = l.vert.co
            u, v = ((c.y, c.z) if a == 0 else (c.x, c.z) if a == 1 else (c.x, c.y))
            l[uvl].uv = (u * s, v * s)
    bm.to_mesh(me); bm.free()


def kit_mesh(pack, name):
    """One joined, transform-applied Mesh for Synty prefab <pack>/<name>, materials bound the way the prefab binds them."""
    k = pack + '/' + name
    if k in _mesh_cache: return _mesh_cache[k]
    parts = parse_prefab(find_prefab(pack, name))
    if not parts: raise RuntimeError('no mesh in prefab ' + k)
    fbxs = []
    for _, f, _ in parts:
        if f and f not in fbxs: fbxs.append(f)
    by_name = {n: mats for n, _, mats in parts}
    root_mats = parts[0][2]
    before = set(bpy.data.objects)
    for f in fbxs[:1]:
        bpy.ops.import_scene.fbx(filepath=f, use_custom_props=False)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH' and 'collision' not in o.name.lower() and not re.search(r'_LOD[1-9]', o.name)]
    wanted = [o for o in meshes if o.name.split('.')[0] in by_name]
    if wanted: meshes = wanted
    for o in meshes:
        mats = by_name.get(o.name.split('.')[0])
        if mats is None:
            same = [m for n, _, m in parts if n.startswith('*') and len(m) == len(o.data.materials)]
            mats = same[0] if same else root_mats
        tri = {}
        nslots = len(o.data.materials)
        for i in range(nslots):
            mp = mats[i] if i < len(mats) else (mats[-1] if mats else None)
            if not mp: continue
            r = material(mp, pack)
            o.data.materials[i] = r['mat']
            if r['kind'] == 'tri':
                o.data.materials.append(r['top'])
                tri[i] = (len(o.data.materials) - 1, r['tiling'][0], r['tiling'][1])
        o['_tri'] = json.dumps(tri)
    for o in bpy.context.selected_objects: o.select_set(False)
    # per-object: apply transform, then box-UV in final (piece-local, metre) space
    for o in meshes:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
        o.data.transform(mw); o.matrix_world.identity()
        tri = {int(a): tuple(b) for a, b in json.loads(o['_tri']).items()}
        if tri: box_uv(o.data, tri)
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    me = o.data; me.name = 'KIT_' + name
    me['synty_pack'] = pack; me['synty_prefab'] = name
    for x in list(bpy.data.objects):
        if x not in before: bpy.data.objects.remove(x, do_unlink=True)
    me.use_fake_user = True
    _mesh_cache[k] = me
    return me


def place(me, name, loc, rot_z_deg=0.0, scale=1.0, coll=None, **props):
    o = bpy.data.objects.new(name, me)
    o.location = loc; o.rotation_euler = (0, 0, math.radians(rot_z_deg))
    o.scale = scale if isinstance(scale, (tuple, list)) else (scale,) * 3
    (coll or bpy.context.scene.collection).objects.link(o)
    if 'synty_prefab' in me: o['synty_prefab'] = me['synty_prefab']; o['synty_pack'] = me['synty_pack']
    for k, v in props.items(): o[k] = v
    return o


def tris(me): return sum(len(p.vertices) - 2 for p in me.polygons)
