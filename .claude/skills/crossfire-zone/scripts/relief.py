# Checkpoint 022: normal + height maps on every stone material (maps made by relief_maps.py).
# Blender loads the 2K copies; each material records the 8K files Unity imports (normal_map_8k, height_map_8k) and how they
# are mapped (relief_mapping: 'uv0' = same UV as the colour texture; 'triplanar_world' = box projection in metres, for
# pieces on a colour atlas or a flat colour: rocks, spires, tier blocks, cover stones).
import bpy, os
TEX = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Textures', 'Crucible022')
STEMS = ('Brick_Large_Texture_01', 'Brick_Medium_Texture_01', 'SmoothStone_Dark_Texture_01', 'SmoothStone_Texture_01', 'Rock_Cliff_Texture_01',
         'Wall_Brick_01', 'T_Crucible_Paving', 'T_Floor_Arena_Quarter', 'T_Floor_Rim_Quarter', 'T_Floor_Dais', 'T_Floor_Yard_Hegemony',
         'T_Floor_Yard_Covenant', 'T_Floor_Yard_Null', 'T_Crucible_OuterRock')
DONE = []


def _img(stem, kind):
    p = os.path.join(TEX, '%s_%s_2K.png' % (stem, kind))
    for im in bpy.data.images:
        if im.filepath and os.path.normcase(bpy.path.abspath(im.filepath)) == os.path.normcase(p): return im
    im = bpy.data.images.load(p); im.colorspace_settings.name = 'Non-Color'
    return im


def _stem_of(im):
    b = os.path.splitext(os.path.basename(bpy.path.abspath(im.filepath) or im.name))[0]
    for s in STEMS:
        if b == s or b.endswith('_' + s) or b.endswith(s): return s
    return None


def _wire(m, stem, vec_src, strength, bump, projection='FLAT'):
    nt = m.node_tree; b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = _img(stem, 'Normal'); tn.name = 'Relief_Normal'; tn.projection = projection; tn.projection_blend = 0.25
    th = nt.nodes.new('ShaderNodeTexImage'); th.image = _img(stem, 'Height'); th.name = 'Relief_Height'; th.projection = projection; th.projection_blend = 0.25
    if vec_src is not None: nt.links.new(vec_src, tn.inputs['Vector']); nt.links.new(vec_src, th.inputs['Vector'])
    nm = nt.nodes.new('ShaderNodeNormalMap'); nm.name = 'Relief_NormalMap'; nm.inputs['Strength'].default_value = strength
    nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
    bp = nt.nodes.new('ShaderNodeBump'); bp.name = 'Relief_Bump'; bp.inputs['Strength'].default_value = bump; bp.inputs['Distance'].default_value = 0.06
    nt.links.new(th.outputs['Color'], bp.inputs['Height']); nt.links.new(nm.outputs['Normal'], bp.inputs['Normal'])
    nt.links.new(bp.outputs['Normal'], b.inputs['Normal'])
    m['normal_map_8k'] = os.path.join(TEX, stem + '_Normal_8K.png'); m['height_map_8k'] = os.path.join(TEX, stem + '_Height_8K.png')
    DONE.append(m.name)


def uv_relief(m, strength=1.0, bump=0.35):
    """Stone material with a colour texture: the maps follow that texture's own UV / mapping."""
    if not m or not m.use_nodes or m.get('relief_mapping'): return False
    nt = m.node_tree; b = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if not b or b.inputs['Normal'].is_linked: return False
    for n in nt.nodes:
        if n.type != 'TEX_IMAGE' or not n.image or n.name.startswith('Relief_'): continue
        if n.image.colorspace_settings.name == 'Non-Color': continue
        st = _stem_of(n.image)
        if not st: continue
        if not any(l.to_socket.name in ('Base Color', 'A', 'B') or l.to_node.type == 'MIX' for l in n.outputs[0].links): continue
        vec = n.inputs['Vector'].links[0].from_socket if n.inputs['Vector'].is_linked else None
        _wire(m, st, vec, strength, bump); m['relief_mapping'] = 'uv0'; m['relief_source'] = st
        return True
    return False


def box_relief(m, stem='Rock_Cliff_Texture_01', tile=4.0, strength=1.0, bump=0.45):
    """Atlas / flat-colour stone: the rock maps box-projected in world metres (Unity: world-space triplanar detail normal + height)."""
    if not m or not m.use_nodes or m.get('relief_mapping'): return False
    nt = m.node_tree; b = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if not b or b.inputs['Normal'].is_linked: return False
    tc = nt.nodes.new('ShaderNodeNewGeometry'); mp = nt.nodes.new('ShaderNodeMapping'); mp.name = 'Relief_Mapping'
    mp.inputs['Scale'].default_value = (1.0 / tile,) * 3; nt.links.new(tc.outputs['Position'], mp.inputs['Vector'])   # world metres: same size on every scaled rock
    _wire(m, stem, mp.outputs['Vector'], strength, bump, projection='BOX'); m['relief_mapping'] = 'triplanar_world'; m['relief_tile_m'] = tile; m['relief_source'] = stem
    return True


def relief_copy(m, tag='Relief', **kw):
    """A copy of a shared atlas material for the rock pieces only (crates and props keep the plain atlas)."""
    key = m.name + '__' + tag
    if key in bpy.data.materials: return bpy.data.materials[key]
    m2 = m.copy(); m2.name = key; box_relief(m2, **kw)
    return m2
