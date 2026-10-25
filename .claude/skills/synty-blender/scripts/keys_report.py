"""How far each body shape key moves every gear piece of a set, beside the bare body part under it.

  blender -b <piece scene.blend> --python keys_report.py -- [--set DOCK]

The Dock Runner gloves' keys (transferred from Tripo) moved fingers up to 9 cm where the bare hand moves 4, and their
bust / butt keys moved glove points the body never moves: at the test character's settings the fingers splintered
in game (owner, 24 Oct 2026). A piece's key should move about as far as the body under it; a key the body part has
at zero (bust on a hand) should be zero on the piece. Lines marked !! are worth a look; glove_rebuild.py rebuilds a
glove's keys from the bare hand.
"""
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
code = argv[argv.index("--set") + 1] if "--set" in argv else ""


def deltas(ob):
    kb = ob.data.shape_keys.key_blocks if ob.data.shape_keys else None
    if kb is None:
        return {}
    base = np.array([v.co[:] for v in kb["Basis"].data])
    return {k.name: np.linalg.norm(np.array([v.co[:] for v in k.data]) - base, axis=1) for k in kb[1:]}


# the bare body: SK_HUMN_BASE_01_<slot>_HU01, or a robot's SK_SCFI_ROBO_04_<slot>_RO01 (any SK_ part that is not ours)
bodies = {o.name.split("_")[4]: o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SK_")
          and not o.name.startswith("SK_KRYT_") and len(o.name.split("_")) > 5}
for ob in bpy.data.objects:
    if ob.type != "MESH" or not ob.name.startswith("SK_KRYT_" + code):
        continue
    slot = ob.name.split("_")[4] if len(ob.name.split("_")) > 4 else ""
    body = next((b for s, b in bodies.items() if s == slot), None)
    mine, theirs = deltas(ob), deltas(body) if body else {}
    print(f"KEYS {ob.name}  (body: {body.name if body else 'none'})")
    for name, d in mine.items():
        t = theirs.get(name)
        tmax = float(t.max()) if t is not None else 0.0
        flag = "!!" if (tmax < 1e-4 and d.max() > 0.2) or (tmax > 0 and d.max() > 1.5 * tmax + 0.5) else "  "
        print(f"KEYS {flag} {name:20s} piece max {d.max():7.3f}  p95 {np.percentile(d, 95):7.3f}   body max {tmax:7.3f}")
