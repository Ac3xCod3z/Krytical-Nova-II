"""Writes MountMaterialise.shadergraph (the shared mount appear/vanish shader) without opening Unity.

Nobody builds Shader Graphs by clicking here (owner and Master, 6 Nov 2026), so the graph is put together from the
JSON of graphs already in the project. Every node, slot, property and block comes from a working graph, gets new
ids and is wired by this script:
  - URP Lit target, Lit blocks:            AllIn1ShaderNodes Demo/Graphs/GRPH_Standard.shadergraph
  - Rim sub-graph node, alpha clip block:  AllIn1ShaderNodes Demo/Graphs/GRPH_Demo_FadeBurn.shadergraph
  - plain math nodes:                       other graphs in the project (see TEMPLATES)
The property names match MountMaterialiser.cs and MountMaterialise.md. Run it from anywhere:
    python make_mount_materialise.py
It overwrites ../MountMaterialise.shadergraph and keeps the .meta (one is made with a new GUID the first time).
"""
import copy
import json
import os
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", ".."))
OUT = os.path.normpath(os.path.join(HERE, "..", "MountMaterialise.shadergraph"))
DEMO = os.path.join(ASSETS, "Plugins", "AllIn1ShaderNodes", "Demo", "Graphs")
STANDARD = os.path.join(DEMO, "GRPH_Standard.shadergraph")
FADEBURN = os.path.join(DEMO, "GRPH_Demo_FadeBurn.shadergraph")

TEMPLATES = {
    "PositionNode": os.path.join(ASSETS, "FastScriptReload", "Samples", "Basic", "Point", "Point URP.shadergraph"),
    "SaturateNode": os.path.join(ASSETS, "FastScriptReload", "Samples", "Basic", "Point", "Point URP.shadergraph"),
    "SubtractNode": os.path.join(DEMO, "GRPH_Demo_UVEffects_Twist.shadergraph"),
    "LerpNode": os.path.join(DEMO, "GRPH_Demo_ChomperToon.shadergraph"),
    "OneMinusNode": os.path.join(DEMO, "GRPH_ProceduralShape.shadergraph"),
    "DotProductNode": os.path.join(ASSETS, "Synty", "PNB_Core", "Shaders", "Aurora.shadergraph"),
    "TilingAndOffsetNode": os.path.join(ASSETS, "Synty", "PNB_Core", "Shaders", "Aurora.shadergraph"),
    "DivideNode": os.path.join(ASSETS, "Synty", "PNB_Core", "Shaders", "Water.shadergraph"),
    "MultiplyNode": FADEBURN,
    "AddNode": FADEBURN,
    "SampleTexture2DNode": FADEBURN,
    "NormalVectorNode": FADEBURN,
    "ViewDirectionNode": FADEBURN,
}
VECTOR3_PROPERTY_SOURCE = os.path.join(DEMO, "GRPH_Demo_ChomperCrazyFresnel.shadergraph")

_cache = {}


def load(path):
    if path not in _cache:
        text = open(path, encoding="utf-8").read()
        dec, i, objs = json.JSONDecoder(), 0, []
        while i < len(text):
            while i < len(text) and text[i].isspace():
                i += 1
            if i >= len(text):
                break
            obj, i = dec.raw_decode(text, i)
            objs.append(obj)
        _cache[path] = (objs, {o["m_ObjectId"]: o for o in objs})
    return _cache[path]


def new_id():
    return uuid.uuid4().hex


objects = []        # everything written after GraphData
nodes = []          # node object ids
edges = []
properties = []
x_cursor = {"v": 0}


class Node:
    def __init__(self, obj, slots):
        self.obj = obj
        self.slots = {s["m_Id"]: s for s in slots}

    @property
    def id(self):
        return self.obj["m_ObjectId"]

    def set(self, slot, value):
        self.slots[slot]["m_Value"] = value
        return self


def place(obj, x, y):
    obj["m_Group"] = {"m_Id": ""}
    obj["m_DrawState"] = {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": float(x), "y": float(y),
                                                              "width": 0.0, "height": 0.0}}


def clone_node(node, by, x, y, register=True):
    n = copy.deepcopy(node)
    n["m_ObjectId"] = new_id()
    place(n, x, y)
    slots = []
    for ref in n["m_Slots"]:
        s = copy.deepcopy(by[ref["m_Id"]])
        s["m_ObjectId"] = new_id()
        ref["m_Id"] = s["m_ObjectId"]
        slots.append(s)
    objects.append(n)
    objects.extend(slots)
    if register:
        nodes.append(n["m_ObjectId"])
    return Node(n, slots)


def node(kind, x, y, **fields):
    objs, by = load(TEMPLATES[kind])
    template = next(o for o in objs if o["m_Type"] == "UnityEditor.ShaderGraph." + kind)
    n = clone_node(template, by, x, y)
    n.obj.update(fields)
    return n


def link(out_node, out_slot, in_node, in_slot):
    edges.append({"m_OutputSlot": {"m_Node": {"m_Id": out_node.id}, "m_SlotId": out_slot},
                  "m_InputSlot": {"m_Node": {"m_Id": in_node.id}, "m_SlotId": in_slot}})


def vec4(x, y=0.0, z=0.0, w=0.0):
    return {"x": float(x), "y": float(y), "z": float(z), "w": float(w)}


# ---------------------------------------------------------------- properties

def _property_template(kind):
    if kind == "Vector3":
        objs, by = load(VECTOR3_PROPERTY_SOURCE)
    else:
        objs, by = load(FADEBURN)
    t = "UnityEditor.ShaderGraph.Internal." + kind + "ShaderProperty"
    prop = next(o for o in objs if o["m_Type"] == t)
    pnode = next(o for o in objs if o["m_Type"] == "UnityEditor.ShaderGraph.PropertyNode"
                 and o["m_Property"]["m_Id"] == prop["m_ObjectId"])
    return prop, pnode, by


def prop(kind, ref, value, **fields):
    template, _, _ = _property_template(kind)
    p = copy.deepcopy(template)
    p["m_ObjectId"] = new_id()
    p["m_Guid"] = {"m_GuidSerialized": str(uuid.uuid4())}
    p["m_Name"] = ref
    p["m_DefaultRefNameVersion"] = 1
    p["m_RefNameGeneratedByDisplayName"] = ref
    p["m_DefaultReferenceName"] = ref
    p["m_OverrideReferenceName"] = ""
    p["m_Hidden"] = False
    p["m_Value"] = value
    if kind == "Vector1":
        p["m_FloatType"] = 0  # a plain number, not a slider (the template's slider range would clamp it)
    p.update(fields)
    objects.append(p)
    properties.append(p)
    return p


def get(p, x, y):
    """A Property node reading p."""
    kind = p["m_Type"].split(".")[-1].replace("ShaderProperty", "")
    _, pnode, by = _property_template(kind)
    n = clone_node(pnode, by, x, y)
    n.obj["m_Property"] = {"m_Id": p["m_ObjectId"]}
    for s in n.slots.values():
        s["m_DisplayName"] = p["m_Name"]
    return n


def texture_value():
    return {"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}", "m_Guid": ""}


def color(r, g, b, a=1.0):
    return {"r": float(r), "g": float(g), "b": float(b), "a": float(a)}


def build():
    std_objs, std_by = load(STANDARD)
    fb_objs, fb_by = load(FADEBURN)
    graph = copy.deepcopy(std_objs[0])

    # --- the Lit target (URP only), alpha clipped so the mount never sorts against its rider
    std_universal = next(std_by[t["m_Id"]] for t in std_objs[0]["m_ActiveTargets"]
                         if std_by[t["m_Id"]]["m_Type"].endswith("UniversalTarget"))
    target = copy.deepcopy(std_universal)
    sub = copy.deepcopy(std_by[std_universal["m_ActiveSubTarget"]["m_Id"]])
    target["m_ObjectId"], sub["m_ObjectId"] = new_id(), new_id()
    target["m_ActiveSubTarget"] = {"m_Id": sub["m_ObjectId"]}
    target["m_AlphaClip"] = True
    target["m_RenderFace"] = 2
    target["m_CastShadows"] = True
    objects.extend([target, sub])

    # --- the blocks
    def block(by, name):
        b = next(o for o in by.values() if o["m_Type"] == "UnityEditor.ShaderGraph.BlockNode" and o["m_Name"] == name)
        return clone_node(b, by, 0, 0, register=False)

    v_pos = block(std_by, "VertexDescription.Position")
    v_norm = block(std_by, "VertexDescription.Normal")
    v_tan = block(std_by, "VertexDescription.Tangent")
    f_base = block(std_by, "SurfaceDescription.BaseColor")
    f_normal = block(std_by, "SurfaceDescription.NormalTS")
    f_metal = block(std_by, "SurfaceDescription.Metallic")
    f_smooth = block(std_by, "SurfaceDescription.Smoothness")
    f_emit = block(std_by, "SurfaceDescription.Emission")
    f_occ = block(std_by, "SurfaceDescription.Occlusion")
    f_alpha = block(std_by, "SurfaceDescription.Alpha")
    f_clip = block(fb_by, "SurfaceDescription.AlphaClipThreshold")
    f_clip.set(0, 0.5)
    vertex_blocks = [v_pos, v_norm, v_tan]
    fragment_blocks = [f_base, f_normal, f_metal, f_smooth, f_emit, f_occ, f_alpha, f_clip]

    # --- properties (names = MountMaterialiser.cs; the URP Lit ones are copied from the mount's own material)
    base_map = prop("Texture2D", "_BaseMap", texture_value(), m_DefaultType=0, isMainTexture=True)
    base_color = prop("Color", "_BaseColor", color(1, 1, 1), m_ColorMode=0, isMainColor=True)
    bump_map = prop("Texture2D", "_BumpMap", texture_value(), m_DefaultType=3)
    emission_map = prop("Texture2D", "_EmissionMap", texture_value(), m_DefaultType=0)
    emission_color = prop("Color", "_EmissionColor", color(0, 0, 0), m_ColorMode=1)
    metallic = prop("Vector1", "_Metallic", 0.0)
    smoothness = prop("Vector1", "_Smoothness", 0.5)
    spawn = prop("Vector1", "_Spawn", 1.0)
    edge_a = prop("Color", "_EdgeColorA", color(0.2, 3.0, 3.2), m_ColorMode=1)
    edge_b = prop("Color", "_EdgeColorB", color(3.0, 0.25, 0.2), m_ColorMode=1)
    burn_width = prop("Vector1", "_BurnWidth", 0.08)
    rim_strength = prop("Vector1", "_RimStrength", 1.5)
    rim_min = prop("Vector1", "_RimMin", 0.4)
    rim_max = prop("Vector1", "_RimMax", 1.0)
    fade_tex = prop("Texture2D", "_FadeTex", texture_value(), m_DefaultType=2)
    fade_tiling = prop("Vector2", "_FadeTiling", {"x": 2.0, "y": 2.0, "z": 0.0, "w": 0.0})
    direction_weight = prop("Vector1", "_DirectionWeight", 0.85)  # height leads, noise breaks the edge (owner, 6 Nov)
    sweep_origin = prop("Vector3", "_SweepOrigin", vec4(0, 0, 0))
    sweep_axis = prop("Vector3", "_SweepAxis", vec4(0, 1, 0))
    sweep_length = prop("Vector1", "_SweepLength", 2.0)
    inflate = prop("Vector1", "_Inflate", 0.015)
    prop("Vector1", "_PatternMode", 0.0)    # read by the scanline/glitch pass (not wired yet, see the .md)
    prop("Vector1", "_PatternUntil", 0.6)

    # --- the surface: the mount's own look
    t_base = node("SampleTexture2DNode", -900, -600)
    link(get(base_map, -1150, -600), 0, t_base, 1)
    base_mul = node("MultiplyNode", -600, -600)
    link(t_base, 0, base_mul, 0)
    link(get(base_color, -800, -480), 0, base_mul, 1)
    link(base_mul, 2, f_base, 0)

    t_norm = node("SampleTexture2DNode", -900, -380, m_TextureType=1, m_NormalMapSpace=0)
    link(get(bump_map, -1150, -380), 0, t_norm, 1)
    link(t_norm, 0, f_normal, 0)
    link(get(metallic, -600, -260), 0, f_metal, 0)
    link(get(smoothness, -600, -220), 0, f_smooth, 0)

    # --- the sweep: 0 at the model's feet (or front), 1 at its top, in world space
    pos_ws = node("PositionNode", -1700, 0, m_Space=2)
    rel = node("SubtractNode", -1450, 0)
    link(pos_ws, 0, rel, 0)
    link(get(sweep_origin, -1700, 80), 0, rel, 1)
    along = node("DotProductNode", -1250, 0)
    link(rel, 2, along, 0)
    link(get(sweep_axis, -1450, 120), 0, along, 1)
    per = node("DivideNode", -1050, 0)
    link(along, 2, per, 0)
    link(get(sweep_length, -1250, 140), 0, per, 1)
    sweep = node("SaturateNode", -850, 0)
    link(per, 2, sweep, 0)

    # --- the noise the dissolve eats through
    uv = node("TilingAndOffsetNode", -1450, 260)
    link(get(fade_tiling, -1700, 300), 0, uv, 1)
    t_noise = node("SampleTexture2DNode", -1200, 260)
    link(get(fade_tex, -1450, 420), 0, t_noise, 1)
    link(uv, 3, t_noise, 2)

    mask = node("LerpNode", -650, 120)
    link(t_noise, 4, mask, 0)
    link(sweep, 1, mask, 1)
    link(get(direction_weight, -850, 200), 0, mask, 2)

    # threshold = _Spawn * (1 + _BurnWidth): at 1 the whole body is solid and no edge is left
    width_plus = node("AddNode", -650, 320)
    link(get(burn_width, -850, 320), 0, width_plus, 0)
    width_plus.set(1, vec4(1.0))
    threshold = node("MultiplyNode", -450, 320)
    link(get(spawn, -650, 440), 0, threshold, 0)
    link(width_plus, 2, threshold, 1)
    ahead = node("SubtractNode", -250, 200)     # > 0 where the body has formed
    link(threshold, 2, ahead, 0)
    link(mask, 3, ahead, 1)
    alpha = node("AddNode", 0, 200)
    link(ahead, 2, alpha, 0)
    alpha.set(1, vec4(0.4999))
    link(alpha, 2, f_alpha, 0)

    # --- the burning edge: 1 right at the edge, 0 a burn width inside
    edge_per = node("DivideNode", -50, 380)
    link(ahead, 2, edge_per, 0)
    link(get(burn_width, -250, 460), 0, edge_per, 1)
    edge_sat = node("SaturateNode", 150, 380)
    link(edge_per, 2, edge_sat, 0)
    edge = node("OneMinusNode", 300, 380)
    link(edge_sat, 1, edge, 0)
    edge_col = node("MultiplyNode", 450, 380)
    link(edge, 1, edge_col, 0)
    link(get(edge_a, 300, 480), 0, edge_col, 1)

    # --- the rim (AllIn1 Rim, wired as in the FadeBurn demo), only while see-through
    rim_template = next(o for o in fb_objs if o.get("m_Name") == "Rim" and o["m_Type"].endswith("SubGraphNode"))
    rim = clone_node(rim_template, fb_by, -250, 600)
    rim.set(1137359251, {"x": 0.0, "y": 0.0, "z": 0.0})
    link(node("ViewDirectionNode", -500, 560), 0, rim, 689837318)
    link(node("NormalVectorNode", -500, 640, m_Space=2), 0, rim, 1359319663)
    link(get(rim_min, -500, 720), 0, rim, 821204913)
    link(get(rim_max, -500, 760), 0, rim, 344603274)
    link(get(edge_b, -500, 800), 0, rim, 623826588)
    unformed = node("OneMinusNode", -50, 760)
    link(get(spawn, -250, 840), 0, unformed, 0)
    rim_k = node("MultiplyNode", 150, 760)
    link(unformed, 1, rim_k, 0)
    link(get(rim_strength, -50, 880), 0, rim_k, 1)
    rim_col = node("MultiplyNode", 300, 640)
    link(rim, 1, rim_col, 0)
    link(rim_k, 2, rim_col, 1)

    # --- emission = the mount's own + the edge + the rim (Bloom makes the HDR edge glow)
    t_emit = node("SampleTexture2DNode", -900, -140)
    link(get(emission_map, -1150, -140), 0, t_emit, 1)
    own_emit = node("MultiplyNode", -600, -140)
    link(t_emit, 0, own_emit, 0)
    link(get(emission_color, -800, -40), 0, own_emit, 1)
    emit1 = node("AddNode", 600, 200)
    link(own_emit, 2, emit1, 0)
    link(edge_col, 2, emit1, 1)
    emit2 = node("AddNode", 750, 300)
    link(emit1, 2, emit2, 0)
    link(rim_col, 2, emit2, 1)
    link(emit2, 2, f_emit, 0)

    # --- the swell: the surface puffs out by _Inflate as it starts forming (until _Spawn 0.2)
    pos_os = node("PositionNode", -900, -1100, m_Space=0)
    nrm_os = node("NormalVectorNode", -900, -1000, m_Space=0)
    early = node("SubtractNode", -900, -880)
    early.set(0, vec4(0.2))
    link(get(spawn, -1150, -860), 0, early, 1)
    early_per = node("DivideNode", -700, -880)
    link(early, 2, early_per, 0)
    early_per.set(1, vec4(0.2))
    early_sat = node("SaturateNode", -500, -880)
    link(early_per, 2, early_sat, 0)
    puff = node("MultiplyNode", -350, -880)
    link(early_sat, 1, puff, 0)
    link(get(inflate, -500, -780), 0, puff, 1)
    offset = node("MultiplyNode", -200, -1000)
    link(nrm_os, 0, offset, 0)
    link(puff, 2, offset, 1)
    moved = node("AddNode", 0, -1060)
    link(pos_os, 0, moved, 0)
    link(offset, 2, moved, 1)
    link(moved, 2, v_pos, 0)

    # --- the graph
    category = copy.deepcopy(next(o for o in fb_objs if o["m_Type"].endswith("CategoryData")))
    category["m_ObjectId"] = new_id()
    category["m_Name"] = ""
    category["m_ChildObjectList"] = [{"m_Id": p["m_ObjectId"]} for p in properties]
    objects.append(category)

    graph["m_ObjectId"] = new_id()
    graph["m_Properties"] = [{"m_Id": p["m_ObjectId"]} for p in properties]
    graph["m_Keywords"] = []
    graph["m_Dropdowns"] = []
    graph["m_CategoryData"] = [{"m_Id": category["m_ObjectId"]}]
    graph["m_Nodes"] = [{"m_Id": i} for i in nodes] + [{"m_Id": b.id} for b in vertex_blocks + fragment_blocks]
    graph["m_GroupDatas"] = []
    graph["m_StickyNoteDatas"] = []
    graph["m_Edges"] = edges
    graph["m_VertexContext"] = {"m_Position": {"x": 900.0, "y": -1000.0}, "m_Blocks": [{"m_Id": b.id} for b in vertex_blocks]}
    graph["m_FragmentContext"] = {"m_Position": {"x": 1100.0, "y": -200.0}, "m_Blocks": [{"m_Id": b.id} for b in fragment_blocks]}
    graph["m_Path"] = "Krytical Nova/Mounts"
    graph["m_OutputNode"] = {"m_Id": ""}
    graph["m_ActiveTargets"] = [{"m_Id": target["m_ObjectId"]}]

    # Every reference must point at an object in the file.
    ids = {o["m_ObjectId"] for o in objects} | {graph["m_ObjectId"]}
    for e in edges:
        assert e["m_OutputSlot"]["m_Node"]["m_Id"] in ids and e["m_InputSlot"]["m_Node"]["m_Id"] in ids
    for o in objects:
        for ref in o.get("m_Slots", []):
            assert ref["m_Id"] in ids, o["m_Type"]
        if "m_Property" in o:
            assert o["m_Property"]["m_Id"] in ids
    slot_ids = {(n, s["m_Id"]) for n in ids for s in []}
    by_id = {o["m_ObjectId"]: o for o in objects}
    for e in edges:
        for end in ("m_OutputSlot", "m_InputSlot"):
            n = by_id[e[end]["m_Node"]["m_Id"]]
            have = {by_id[r["m_Id"]]["m_Id"] for r in n["m_Slots"]}
            assert e[end]["m_SlotId"] in have, (n["m_Type"], e[end]["m_SlotId"], have)

    text = "\n\n".join(json.dumps(o, indent=4) for o in [graph] + objects) + "\n\n"
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    meta = OUT + ".meta"
    if not os.path.exists(meta):
        demo_meta = open(FADEBURN + ".meta", encoding="utf-8").read()
        demo_meta = demo_meta.split("AssetOrigin:")[0]  # ours, not the Asset Store package's
        lines = [("guid: " + new_id()) if l.startswith("guid:") else l for l in demo_meta.splitlines()]
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
    print(f"wrote {OUT}: {len(nodes)} nodes, {len(edges)} edges, {len(properties)} properties")


if __name__ == "__main__":
    build()
