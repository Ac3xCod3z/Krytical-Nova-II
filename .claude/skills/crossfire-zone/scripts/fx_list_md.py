# Writes ../UNITY-FX-LIST.md (checkpoint 022b) from FX_022.json + LAVA_022.json
import os, json
from collections import Counter, defaultdict
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FX = json.load(open(os.path.join(R, 'FX_022.json'))); LJ = json.load(open(os.path.join(R, 'LAVA_022.json')))
M = FX['markers']; L = []
w = L.append
w('# The Crucible - Unity FX and lighting list (checkpoint 022b)\n')
w('Every light, fire, glow and shrine marker of the approved 022 look, with its exact place. The full data (Blender and Unity positions, rotation as euler and quaternion, linear colour, intensity, range, bake mode, shadows, flicker, VFX) is **`FX_022.json`**; lava paths, heat haze, embers and smoke are **`LAVA_022.json`**; the picture is **`LavaMap_022.jpg`** (arena) and **`LavaMap_022_full.jpg`** (whole map).\n')
w('- Axes: Unity (x, y, z) = Blender (-x, z, -y), metres; the Crucible heart is (0, 0, 0) at rim level, arena floor y = -7.5. Euler in Unity order (Z, X, Y), degrees.')
w('- Colours are LINEAR RGB (Unity colour picker in linear, or convert to sRGB for the hex field). Light intensity = URP intensity; emissive "EV" = HDR exposure stops on the emission colour.')
w('- Marker names `FX_<Type>_<Area>_<nn>` are the empties in the blend (collection FX_Markers_For_Unity). `PreviewFlames_PREVIEW_ONLY` cones are preview stand-ins: never import them.\n')
w('## Light budget (60 fps on mid hardware)\n')
w('| Group | Mode | Shadows | Count | Why |')
w('|---|---|---|---|---|')
why = {('Sky', 'Mixed'): 'the moon: the one shadowed directional light', ('Shrine', 'Mixed'): 'three respawn shrines: soft shadow, low-res (256), range 18 m', ('Lava', 'Mixed'): 'heart lava glow: realtime on characters, baked on the floor',
       ('Fire', 'Realtime'): 'hero fires where players stand (gate mouths, Null gate foot, Covenant shrine, sealed stair): small unshadowed light (range <= 10 m) with flicker', ('Fire', 'Baked'): 'every other fire: baked into lightmap / APV probes; flicker lives on the emissive + flame VFX',
       ('Lava', 'Baked'): 'crack and outer-flow glow: baked + emissive pulse', ('Glow', 'Baked'): 'sealed-gate shimmer', ('Glow', 'None'): 'light strips: emissive only (bloom), they light nothing realtime', ('Cloth', 'None'): 'banner sway (wind zone), no light', ('Sky', 'None'): 'sky dome (unlit)'}
for (g, mo, sh), n in sorted(Counter((m['group'], m['light']['mode'], m['light']['shadows']) for m in M).items()):
    w('| %s | %s | %s | %d | %s |' % (g, mo, sh, n, why.get((g, mo), '')))
w('\nRules: at most 1 shadowed directional + 3 shadowed spot/point lights in the whole map, and never more than about 8 realtime lights touching one view (URP Forward+). Use APV / light probes for the baked fires on moving characters. Flicker = animate the flame emissive and, on Realtime fires only, light intensity +-15 % at 6-10 Hz with noise. Bake with the flame emissives on (Contribute GI), strips at EV 3.\n')
def table(title, rows):
    w('## ' + title + '\n'); w('| Marker | Unity position (x, y, z) | Linear colour | Light | Intensity | Range m | Mode | Shadows | Flicker |'); w('|---|---|---|---|---|---|---|---|---|')
    for m in rows:
        li = m['light']; w('| %s | %s | %s | %s | %s | %s | %s | %s | %s |' % (m['name'], tuple(m['unity_pos']), tuple(m['colour_linear']), li['type'], li['intensity'], li['range_m'], li['mode'], li['shadows'], 'yes' if li['flicker'] else ''))
    w('')
table('Moon, shrines, heart lava, sealed gate', [m for m in M if m['group'] in ('Sky', 'Shrine') or (m['group'] == 'Lava' and m['light']['mode'] == 'Mixed') or m['type'] == 'SealChains'])
table('Fires (every brazier, fire pit, fire basket and purple flame pillar)', [m for m in M if m['group'] == 'Fire'])
table('Lava point lights (baked)', [m for m in M if m['group'] == 'Lava' and m['light']['mode'] != 'Mixed'])
w('## Light strips and banners (no realtime light)\n')
by = defaultdict(list)
for m in M:
    if m['group'] in ('Glow', 'Cloth') and m['type'] != 'SealChains': by[(m['type'], m['area'])].append(m)
w('| Type | Area | Count | Linear colour | Emissive EV | Positions |'); w('|---|---|---|---|---|---|')
for (t, a), rows in sorted(by.items()): w('| %s | %s | %d | %s | %s | FX_022.json (%s .. %s) |' % (t, a, len(rows), tuple(rows[0]['colour_linear']), rows[0]['emissive']['hdr_ev'], rows[0]['name'], rows[-1]['name']))
c = LJ['counts']
w('\n## Lava, heat haze, embers, smoke (LAVA_022.json)\n')
w('- %d crack paths read from the emission maps the floors use (arena floor, heart dais, outer ground; exact to 4-8 cm): each is a polyline in Unity axes with width (m), linear emission colour and HDR intensity. Region totals: %s.' % (c['paths'], dict(Counter(p['region'] for p in LJ['paths']))))
w('- Heat haze: %d boxes (16 m cells over the cracks, 3 m tall) = the cheap set; %d capsules that hug each crack for a finer look.' % (c['haze_boxes'], c['haze_capsules']))
w('- %d ember spawn points (every 3 m on the arena and dais cracks) and %d smoke spawn points (every 14 m on the outer flows).' % (c['ember_points'], c['smoke_points']))
w('- The floor itself already glows from its _Emit map; the paths are for VFX, decals, haze and gameplay (damage zones if wanted).\n')
w('## Sky (022b)\n- The skybox is gone: an unlit aurora sky dome, radius 500 m (`SkyDome_R500`), with the far peaks as geometry inside it (`Outer_Peak_*`, 330-410 m out, 95-190 m tall; `Outer_Crag_*` 215-290 m). Camera far clip at least 600 m.')
open(os.path.join(R, 'UNITY-FX-LIST.md'), 'w', encoding='utf-8').write('\n'.join(L))
print('FX md lines', len(L))
