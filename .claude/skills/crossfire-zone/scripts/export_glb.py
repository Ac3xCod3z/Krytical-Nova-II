# Preview GLB for checkpoint 022: like Build007/export_glb.py, plus the relief maps the web viewer can show. glTF carries a
# normal map only when it feeds the BSDF straight from a UV texture: so (in memory only, the blend is not saved) uv-mapped relief
# materials skip the height bump, box-projected ones drop their maps, and the relief images go out at 1024 px.
import bpy, sys, os, json
a = sys.argv[sys.argv.index('--') + 1:]; web, checkpoint = a[0], a[1]
for im in bpy.data.images:
    if im.name.startswith('Crucible 006') and im.size[0] > 2048: im.scale(2048, max(512, im.size[1] // 2))
for m in bpy.data.materials:
    mode = m.get('relief_mapping')
    if not mode or not m.use_nodes: continue
    nt = m.node_tree; b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); nm = nt.nodes.get('Relief_NormalMap')
    for l in list(b.inputs['Normal'].links): nt.links.remove(l)
    if mode == 'uv0' and nm: nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
for im in bpy.data.images:
    if ('_Normal_2K' in im.name or '_Height_2K' in im.name) and im.size[0] > 1024: im.scale(1024, max(256, im.size[1] * 1024 // im.size[0]))
for o in list(bpy.data.objects):                                   # 022b: collider / blocker proxies are data, not art
    if o.name.startswith(('UCX_', 'Blocker_', 'TopOrtho')): bpy.data.objects.remove(o, do_unlink=True)
tmp = os.path.join(web, 'scene-review-next.glb')
bpy.ops.export_scene.gltf(filepath=tmp, export_format='GLB', export_extras=True, export_yup=True, export_apply=False,
                          export_lights=False, export_cameras=False, use_visible=False, export_image_format='AUTO')
dst = os.path.join(web, 'scene-review.glb'); os.replace(tmp, dst)
st = os.stat(dst)
json.dump({'file': 'scene-review.glb', 'checkpoint': checkpoint, 'revision': str(st.st_mtime_ns), 'bytes': st.st_size}, open(os.path.join(web, 'scene-review.json'), 'w'))
print('GLB_OK', st.st_size)
