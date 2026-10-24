"""Read raw FBX data (no import) to check an exported file against Synty's own."""
from io_scene_fbx import parse_fbx


def read(path):
    root, _ver = parse_fbx.parse(str(path))
    top = {e.id: e for e in root.elems}
    unit = 1.0
    for e in top[b"GlobalSettings"].elems:
        if e.id == b"Properties70":
            for p in e.elems:
                if p.props[0] == b"UnitScaleFactor":
                    unit = p.props[4]
    models, clusters, channels, meshes, uv_layers, scaled = {}, {}, [], [], {}, []
    limbs, takes = [], []
    for e in top[b"Objects"].elems:
        oid, name = e.props[0], e.props[1].split(b"\x00")[0].decode()
        sub = e.props[2].decode() if len(e.props) > 2 else ""
        if e.id == b"Model":
            models[oid] = name
            if sub == "Mesh":
                meshes.append(name)
            elif sub in ("LimbNode", "Root"):
                limbs.append(name)
            for c in e.elems:
                if c.id == b"Properties70":
                    for p in c.elems:
                        if p.props[0] == b"Lcl Scaling" and any(abs(x - 1.0) > 1e-4 for x in p.props[4:7]):
                            scaled.append((name, [round(x, 4) for x in p.props[4:7]]))
        elif e.id == b"Deformer" and sub == "Cluster":
            clusters[oid] = {c.id.decode(): list(c.props[0]) for c in e.elems
                             if c.id in (b"Transform", b"TransformLink")}
        elif e.id == b"Deformer" and sub == "BlendShapeChannel":
            channels.append(name.split(".")[-1])
        elif e.id == b"AnimationStack":
            takes.append(name)
        elif e.id == b"Geometry" and sub == "Mesh":
            uv_layers[oid] = [c2.props[0].decode() for c in e.elems if c.id == b"LayerElementUV"
                              for c2 in c.elems if c2.id == b"Name"]
    bind = {}
    for c in top[b"Connections"].elems:
        if c.props[0] == b"OO" and c.props[2] in clusters and c.props[1] in models:
            bind[models[c.props[1]]] = clusters[c.props[2]].get("TransformLink")
    return {"unit": unit, "bones": sorted(set(models.values())), "bind": bind, "shapes": sorted(channels),
            "meshes": meshes, "uv_layers": [n for v in uv_layers.values() for n in v], "scaled_nodes": scaled, "skeleton": limbs, "takes": takes}


def compare_bind(ours, theirs, tolerance=0.01):
    """Bones whose bind matrix differs from the reference file. Empty means the part will skin correctly."""
    bad, checked = [], 0
    for bone, m in ours["bind"].items():
        ref = theirs["bind"].get(bone)
        if ref is None or m is None:
            continue
        checked += 1
        worst = max(abs(x - y) for x, y in zip(m, ref))
        if worst > tolerance:
            bad.append((bone, round(worst, 4)))
    return checked, bad


def bind_matrix(values):
    """A bone's bind matrix from the 16 numbers the FBX stores (translation last), as a mathutils Matrix."""
    from mathutils import Matrix
    return Matrix([values[0:4], values[4:8], values[8:12], values[12:16]]).transposed()
