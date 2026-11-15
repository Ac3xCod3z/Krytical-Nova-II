"""Make the KN season copies of two Synty Shader Graphs (item 21 part 3, owner's option A).

Synty's own graphs stay untouched: this reads them (from git, so a lean worktree without Assets/Synty works too) and writes copies
to Assets/_Game/Rendering/Shaders/Synty/ with new object ids, the graph path "KryticalNova/Synty" (shader names
"KryticalNova/Synty/Generic_Basic" and ".../Polygon_Triplanar"), and one Custom Function node (KNSeasonSynty, file
KNSeasonSynty.hlsl) between whatever fed Base Color and Base Color, fed by a world Normal Vector and a world Position node.
Every property keeps its reference name, so a material moved onto a copy keeps all its values.

    python make_synty_season_graphs.py [--checkout <repo>]

Run it again after Synty updates a graph; the copies' .meta guids are kept when they exist.
"""
import json
import os
import subprocess
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
CHECKOUT = os.path.normpath(sys.argv[sys.argv.index("--checkout") + 1]) if "--checkout" in sys.argv \
    else os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))
OUT_DIR = os.path.join(CHECKOUT, "Assets", "_Game", "Rendering", "Shaders", "Synty")
HLSL_META = os.path.join(OUT_DIR, "KNSeasonSynty.hlsl.meta")
GRAPHS = [
    "Assets/Synty/PolygonGeneric/Shaders/Generic_Basic.shadergraph",
    "Assets/Synty/PolygonSciFiCity/Shaders/Polygon_Triplanar.shadergraph",
]
GRAPH_PATH = "KryticalNova/Synty"
IMPORTER_SCRIPT = "625f186215c104763be7675aa2d941aa"   # Shader Graph's ScriptedImporter


def new_id():
    return uuid.uuid4().hex


def read_docs(text):
    dec = json.JSONDecoder()
    i, out = 0, []
    while True:
        while i < len(text) and text[i].isspace():
            i += 1
        if i >= len(text):
            return out
        o, i = dec.raw_decode(text, i)
        out.append(o)


def guid_of(meta):
    with open(meta, encoding="utf-8") as f:
        for line in f:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    raise SystemExit(f"no guid in {meta}")


def slot(kind, index, name, out):
    zero = {"x": 0.0, "y": 0.0, "z": 0.0}
    return {"m_SGVersion": 0, "m_Type": f"UnityEditor.ShaderGraph.{kind}", "m_ObjectId": new_id(), "m_Id": index,
            "m_DisplayName": name, "m_SlotType": 1 if out else 0, "m_Hidden": False, "m_ShaderOutputName": name,
            "m_StageCapability": 3, "m_Value": dict(zero), "m_DefaultValue": dict(zero), "m_Labels": []}


def node(kind, name, x, y, slots, version=0, **extra):
    o = {"m_SGVersion": version, "m_Type": f"UnityEditor.ShaderGraph.{kind}", "m_ObjectId": new_id(), "m_Group": {"m_Id": ""},
         "m_Name": name, "m_DrawState": {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": x, "y": y,
                                                                              "width": 208.0, "height": 302.0}},
         "m_Slots": [{"m_Id": s["m_ObjectId"]} for s in slots], "synonyms": [], "m_Precision": 0, "m_PreviewExpanded": False,
         "m_PreviewMode": 0, "m_CustomColors": {"m_SerializableColors": []}}
    o.update(extra)
    return o


def edge(out_node, out_slot, in_node, in_slot):
    return {"m_OutputSlot": {"m_Node": {"m_Id": out_node}, "m_SlotId": out_slot},
            "m_InputSlot": {"m_Node": {"m_Id": in_node}, "m_SlotId": in_slot}}


def remap_ids(text, docs):
    """New object ids everywhere (a copy must not share ids with the source graph)."""
    for o in docs:
        old = o["m_ObjectId"]
        text = text.replace(old, new_id())
    return text


def make(src):
    text = subprocess.run(["git", "-C", CHECKOUT, "show", "HEAD:" + src], capture_output=True, text=True, encoding="utf-8",
                          check=True).stdout
    text = remap_ids(text, read_docs(text))
    docs = read_docs(text)
    graph = docs[0]
    by_id = {o["m_ObjectId"]: o for o in docs}
    base = next(o for o in docs if o["m_Type"].endswith("BlockNode")
                and o.get("m_SerializedDescriptor") == "SurfaceDescription.BaseColor")
    feed = [e for e in graph["m_Edges"] if e["m_InputSlot"]["m_Node"]["m_Id"] == base["m_ObjectId"]]
    if len(feed) != 1:
        raise SystemExit(f"{src}: expected one edge into Base Color, found {len(feed)}")
    feed = feed[0]
    src_node = by_id[feed["m_OutputSlot"]["m_Node"]["m_Id"]]
    pos = src_node["m_DrawState"]["m_Position"]
    x, y = pos["x"] + 260.0, pos["y"]

    hlsl_guid = guid_of(HLSL_META)
    s_alb, s_nrm, s_pos = slot("Vector3MaterialSlot", 0, "Albedo", False), slot("Vector3MaterialSlot", 1, "NormalWS", False), \
        slot("Vector3MaterialSlot", 2, "PositionWS", False)
    s_out = slot("Vector3MaterialSlot", 3, "Out", True)
    cf = node("CustomFunctionNode", "KN Season (Custom Function)", x, y, [s_alb, s_nrm, s_pos, s_out], version=1,
              m_DismissedVersion=0, m_SourceType=0, m_FunctionName="KNSeasonSynty", m_FunctionSource=hlsl_guid,
              m_FunctionSourceUsePragmas=True, m_FunctionBody="")
    cf["synonyms"] = ["code", "HLSL"]
    s_n = slot("Vector3MaterialSlot", 0, "Out", True)
    s_n["m_Value"] = {"x": 0.0, "y": 0.0, "z": 1.0}
    nrm = node("NormalVectorNode", "Normal Vector", x - 260.0, y + 320.0, [s_n], m_Space=2)
    nrm["m_PreviewMode"] = 2
    s_p = slot("Vector3MaterialSlot", 0, "Out", True)
    psn = node("PositionNode", "Position", x - 260.0, y + 460.0, [s_p], version=1, m_Space=2, m_PositionSource=0)
    psn["m_PreviewMode"] = 2

    graph["m_Edges"].remove(feed)
    graph["m_Edges"] += [
        edge(src_node["m_ObjectId"], feed["m_OutputSlot"]["m_SlotId"], cf["m_ObjectId"], 0),
        edge(nrm["m_ObjectId"], 0, cf["m_ObjectId"], 1),
        edge(psn["m_ObjectId"], 0, cf["m_ObjectId"], 2),
        edge(cf["m_ObjectId"], 3, base["m_ObjectId"], feed["m_InputSlot"]["m_SlotId"]),
    ]
    graph["m_Nodes"] += [{"m_Id": cf["m_ObjectId"]}, {"m_Id": nrm["m_ObjectId"]}, {"m_Id": psn["m_ObjectId"]}]
    graph["m_Path"] = GRAPH_PATH
    docs += [cf, s_alb, s_nrm, s_pos, s_out, nrm, s_n, psn, s_p]

    name = os.path.basename(src)
    dst = os.path.join(OUT_DIR, name)
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n\n".join(json.dumps(o, indent=4) for o in docs) + "\n\n")
    meta = dst + ".meta"
    if not os.path.exists(meta):
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n"
                    "  serializedVersion: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                    "  script: {fileID: 11500000, guid: %s, type: 3}\n" % (new_id(), IMPORTER_SCRIPT))
    print(f"{src} -> {os.path.relpath(dst, CHECKOUT)} ({GRAPH_PATH}/{os.path.splitext(name)[0]}), Base Color fed through KNSeasonSynty")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    if not os.path.exists(HLSL_META):
        raise SystemExit(f"missing {HLSL_META}")
    for g in GRAPHS:
        make(g)
