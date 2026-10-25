"""Builds the Dependency Map of Krytical Nova: who uses what, across the game's scripts, assets and tests.

It reads the project as text, outside Unity (nothing is imported, the Editor need not be open):
  1. assemblies  every .asmdef / .asmref under Assets/_Game, so each script knows the assembly it compiles into;
  2. code        every runtime and test .cs file under Assets/_Game (Editor code and the "~" programs are left out):
                 the types it declares and every first-party type it uses, and how (inherits, serialized field,
                 GetComponent, static access, FindObject, plain use);
  3. assets      every text prefab, scene and .asset: which scripts they hold (m_Script GUIDs), which data assets
                 they are wired to, and which classes they store by name ([SerializeReference] entries);
  4. tests       which runtime types each test file touches.
Then it measures: system fan-in / fan-out, the references that run "uphill" against the layer rules of
ProjectOrganization.md section 4, the references that would have to go for the systems to form a clean stack,
each type's ripple (what a change to it touches), the static hubs, the god classes, and the design-data health
(numbers fixed in code, settings fallbacks, runtime writes to shared assets, string lookups).

Usage (from the project folder):
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py"           build the map
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py" --baseline   also save today's
           uphill references and two-way system pairs as the baseline (baseline.json beside this script)
    python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py" --check      compare with the
           baseline and exit 1 when a new uphill reference or a new two-way pair has appeared
Writes out/dependency-map.json and out/dependency-map.html beside this script (out/ is not committed), and refreshes
the generated tables in Assets/_Game/_Docs/Reference/Dependency-Map.md between its <!-- map:... --> markers.
Publish out/dependency-map.html to the Dependency Map artifact (README.md beside this script has the link).
Python's standard library only.
"""
import bisect
import collections
import datetime
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, *[".."] * 6))   # Scripts~ / Architecture / Editor / Tools / _Game / Assets
GAME = os.path.join(ROOT, "Assets", "_Game")
OUT_DIR = os.path.join(HERE, "out")
DOC = os.path.join(GAME, "_Docs", "Reference", "Dependency-Map.md")
BASELINE = os.path.join(HERE, "baseline.json")
ARGS = set(a for a in sys.argv[1:] if a.startswith("--"))


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


# --- systems and layers -----------------------------------------------------------------------------------------------
# A system is a top folder of _Game, or Gameplay/<Sub>. Its rank is its layer: a system may use systems of its own
# rank or below; a use of a higher rank runs uphill (ProjectOrganization.md section 4: Core knows nothing of gameplay,
# gameplay never knows its presentation, presentation observes gameplay).
RANKS = [
    (0, "Foundation", ["Core"]),
    (1, "Infrastructure", ["Networking", "Rendering"]),
    (2, "Simulation", ["Gameplay/*", "World", "Environment"]),
    (3, "Presentation", ["UI", "GameFeel", "Audio", "VFX", "Icons"]),
    (4, "Tooling", ["Tools", "Release"]),
]


def system_of(path_in_game):
    parts = path_in_game.split("/")
    if parts[0] == "Gameplay" and len(parts) > 2:
        return "Gameplay/" + parts[1]
    return parts[0]


def rank_of(system):
    for rank, _, members in RANKS:
        for m in members:
            if m == system or (m.endswith("/*") and system.startswith(m[:-1])):
                return rank
    return 2


def is_test(path_in_game):
    return "/Tests/" in "/" + path_in_game


def is_editor(path_in_game):
    return "/Editor/" in "/" + path_in_game


# --- 1. assemblies -----------------------------------------------------------------------------------------------------
def load_json(path):
    try:
        return json.loads(read(path).lstrip("﻿"))
    except ValueError:
        return {}


def meta_guid(path):
    try:
        with open(path + ".meta", encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("guid:"):
                    return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return None


ASM_DIRS = {}          # folder -> assembly name
ASM_REFS = {}          # assembly name -> [referenced assembly names]
ASM_GUIDS = {}
ASMREF_COUNT = collections.Counter()
for folder, dirs, files in os.walk(GAME):
    dirs[:] = [d for d in dirs if not d.endswith("~")]
    for name in files:
        full = os.path.join(folder, name)
        if name.endswith(".asmdef"):
            d = load_json(full)
            ASM_DIRS[folder] = d.get("name", name[:-7])
            ASM_REFS[d.get("name", name[:-7])] = d.get("references", [])
            g = meta_guid(full)
            if g:
                ASM_GUIDS["GUID:" + g] = d.get("name")
for folder, dirs, files in os.walk(GAME):
    dirs[:] = [d for d in dirs if not d.endswith("~")]
    for name in files:
        if name.endswith(".asmref"):
            ref = load_json(os.path.join(folder, name)).get("reference", "")
            ref = ASM_GUIDS.get(ref, ref)
            ASM_DIRS[folder] = ref
            ASMREF_COUNT[ref] += 1
for asm, refs in ASM_REFS.items():
    ASM_REFS[asm] = [ASM_GUIDS.get(r, r) for r in refs]


def assembly_of(full):
    folder = os.path.dirname(full)
    while folder.startswith(GAME):
        if folder in ASM_DIRS:
            return ASM_DIRS[folder]
        folder = os.path.dirname(folder)
    return "Assembly-CSharp"


# --- 2. code -----------------------------------------------------------------------------------------------------------
STRIP = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|\$?@\$?"(?:[^"]|"")*"|\$?"(?:\\.|[^"\\\n])*"|\'(?:\\.|[^\'\\\n])+\'')


def blank(m):
    s = m.group(0)
    if s.startswith("//") or s.startswith("/*"):
        return re.sub(r"[^\n]", " ", s)
    return '"' + re.sub(r"[^\n]", " ", s[1:-1]) + '"' if len(s) >= 2 else s


DECL = re.compile(r"\b(class|struct|interface|enum|record(?:\s+struct|\s+class)?)\s+([A-Za-z_]\w*)\s*(<[^>{;]*>)?")
NS_DECL = re.compile(r"\bnamespace\s+([\w.]+)\s*([;{])")
USING = re.compile(r"^\s*using\s+(static\s+)?(?:(\w+)\s*=\s*)?([\w.]+)\s*;", re.M)
CHAIN = re.compile(r"(?<![\w.])(?:global::)?([A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)")
STATIC_MEMBER = re.compile(
    r"\bstatic\s+(?:(?:readonly|volatile|extern|new|unsafe|async|override|virtual|partial|public|private|protected|internal)\s+)*"
    r"(event\s+)?([\w.<>\[\],?() ]+?)\s+(\w+)\s*(;|=>|=|\(|\{|<)")
CONST_MEMBER = re.compile(r"\bconst\s+[\w.]+\s+(\w+)\s*=")
COLLECTION = re.compile(r"\b(List|Dictionary|HashSet|Queue|Stack|SortedList|SortedDictionary|LinkedList|ConcurrentDictionary)\s*<")
KEYWORDS = set("""abstract as base bool break byte case catch char checked class const continue decimal default delegate do
double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is
lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed
short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using
virtual void volatile while var dynamic async await nameof when where yield get set value add remove partial record global
init with not and or""".split())

K_INHERIT, K_SERIALIZED, K_GETCOMP, K_STATIC, K_FIND, K_USE = 1, 2, 4, 8, 16, 32

files = []           # {p, sys, rank, kind, asm, lines, text}
for folder, dirs, names in os.walk(GAME):
    dirs[:] = sorted(d for d in dirs if not d.endswith("~"))
    for name in sorted(names):
        if not name.endswith(".cs"):
            continue
        full = os.path.join(folder, name)
        pg = os.path.relpath(full, GAME).replace("\\", "/")
        if is_editor(pg) and not is_test(pg):
            continue
        raw = read(full)
        files.append({
            "p": rel(full), "pg": pg, "sys": system_of(pg), "kind": "test" if is_test(pg) else "rt",
            "asm": assembly_of(full), "lines": raw.count("\n") + 1, "raw": raw,
            "text": STRIP.sub(blank, raw), "guid": meta_guid(full),
        })


def brace_matches(text):
    match, stack = {}, []
    for i, ch in enumerate(text):
        if ch == "{":
            stack.append(i)
        elif ch == "}" and stack:
            match[stack.pop()] = i
    return match


types = []           # top-level types (nested types fold into their outermost type)
type_by_full = {}
simple_index = collections.defaultdict(list)   # simple name -> [type ids]
namespaces = set()

for fi, f in enumerate(files):
    t = f["text"]
    match = brace_matches(t)
    ns_spans = []    # (start, end, name)
    for m in NS_DECL.finditer(t):
        name = m.group(1)
        namespaces.add(name)
        if m.group(2) == ";":
            ns_spans.append((m.start(), len(t), name))
        else:
            ns_spans.append((m.start(), match.get(m.end() - 1, len(t)), name))
    f["ns"] = ns_spans[0][2] if ns_spans else ""
    decls = []
    for m in DECL.finditer(t):
        # skip "where T : class" / "new() { }" style hits: a declaration is followed by a body or ';' or a base list
        brace = t.find("{", m.end())
        semi = t.find(";", m.end())
        if brace < 0 or (0 <= semi < brace and not m.group(1).startswith("record")):
            continue
        header = t[m.end():brace]
        if "=" in header or "(" in header and not m.group(1).startswith("record"):
            continue
        end = match.get(brace, len(t))
        ns = ""
        for s, e, n in ns_spans:
            if s <= m.start() <= e:
                ns = n
        decls.append({"start": m.start(), "brace": brace, "end": end, "name": m.group(2), "kw": m.group(1).split()[0],
                      "header": header, "ns": ns})
    f["decls"] = []
    for d in decls:
        outer = None
        for o in decls:
            if o is not d and o["brace"] < d["start"] < o["end"] and (outer is None or o["brace"] > outer["brace"]):
                outer = o
        d["outer"] = outer
    for d in decls:
        top = d
        while top["outer"] is not None:
            top = top["outer"]
        d["top"] = top
    for d in decls:
        if d["outer"] is not None:
            continue
        full_name = (d["ns"] + "." if d["ns"] else "") + d["name"]
        if full_name in type_by_full:
            tid = type_by_full[full_name]
        else:
            tid = len(types)
            type_by_full[full_name] = tid
            types.append({"n": d["name"], "ns": d["ns"], "full": full_name, "kw": d["kw"], "files": [], "bases": [],
                          "nested": [], "statics": {}, "sys": f["sys"], "kind": f["kind"], "asm": f["asm"]})
            simple_index[d["name"]].append(tid)
        ty = types[tid]
        if fi not in ty["files"]:
            ty["files"].append(fi)
        base_text = d["header"].split(":", 1)[1] if ":" in d["header"] else ""
        base_text = re.split(r"\bwhere\b", base_text)[0]
        for b in re.split(r",(?![^<]*>)", base_text):
            b = re.sub(r"<.*", "", b.strip()).split(".")[-1].strip()
            if b and b not in ty["bases"]:
                ty["bases"].append(b)
        d["tid"] = tid
        ty["dl"] = ty.get("dl", 0) + t[d["start"]:d["end"]].count("\n") + 1   # its own lines, partial parts summed
        f["decls"].append(d)
    for d in decls:
        if d["outer"] is not None:
            tid = d["top"].get("tid")
            if tid is not None and d["name"] not in types[tid]["nested"]:
                types[tid]["nested"].append(d["name"])
    # static members declared directly in each top-level type body
    for d in f["decls"]:
        body = t[d["brace"] + 1:d["end"]]
        depth, start, chunks = 0, 0, []
        for i, ch in enumerate(body):   # keep only the type's own level, so nested types' statics stay theirs
            if ch == "{":
                if depth == 0:
                    chunks.append(body[start:i + 1])
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    start = i
        chunks.append(body[start:])
        own = "\n".join(chunks)
        statics = types[d["tid"]]["statics"]
        for m in STATIC_MEMBER.finditer(own):
            is_event, ty_text, name, tail = m.group(1), m.group(2), m.group(3), m.group(4)
            if name in KEYWORDS or name == types[d["tid"]]["n"]:
                continue
            if is_event:
                kind = "event"
            elif tail in ("(", "<"):
                kind = "method"
            elif tail in ("=>", "{"):
                kind = "property"
            else:
                ro = "readonly" in m.group(0).split(name)[0]
                kind = "state" if (not ro or COLLECTION.search(ty_text)) else "readonly"
            statics.setdefault(name, kind)
        for m in CONST_MEMBER.finditer(own):
            statics.setdefault(m.group(1), "const")

# namespace prefixes are namespaces too (KryticalNova, KryticalNova.UI, ...)
for ns in list(namespaces):
    parts = ns.split(".")
    for i in range(1, len(parts)):
        namespaces.add(".".join(parts[:i]))

# base classification: MonoBehaviour / NetworkBehaviour / ScriptableObject, through first-party bases too
ENGINE_BASES = {"MonoBehaviour": "mono", "NetworkBehaviour": "net", "ScriptableObject": "so",
                "ScriptableRendererFeature": "so", "UniversalRenderPipelineAsset": "so", "Editor": "editor",
                "MMF_Feedback": "feel"}


def classify(tid, seen=None):
    seen = seen or set()
    ty = types[tid]
    if "role" in ty:
        return ty["role"]
    role = "enum" if ty["kw"] == "enum" else "interface" if ty["kw"] == "interface" else "plain"
    for b in ty["bases"]:
        if b in ENGINE_BASES:
            role = ENGINE_BASES[b]
            break
        cands = [c for c in simple_index.get(b, []) if c not in seen and types[c]["kw"] == "class"]
        if cands:
            r = classify(cands[0], seen | {tid})
            if r in ("mono", "net", "so", "feel"):
                role = r
                break
    ty["role"] = role
    return role


for tid in range(len(types)):
    classify(tid)
for ty in types:
    if ty["role"] == "feel":
        ty["role"] = "plain"
        ty["feel"] = True


def visible_namespaces(f):
    vis = []
    ns = f["ns"]
    while ns:
        vis.append(ns)
        ns = ns.rsplit(".", 1)[0] if "." in ns else ""
    aliases, statics = {}, []
    for m in USING.finditer(f["text"]):
        if m.group(2):
            aliases[m.group(2)] = m.group(3)
        elif m.group(1):
            statics.append(m.group(3))
        else:
            vis.append(m.group(3))
    return vis, aliases, statics


def resolve_simple(name, f, vis):
    cands = simple_index.get(name)
    if not cands:
        return None
    if len(cands) == 1:
        c = types[cands[0]]
        return cands[0] if (not c["ns"] or c["ns"] in vis) else None
    for ns in vis:   # nearest namespace first: the file's own chain, then usings
        hit = [c for c in cands if types[c]["ns"] == ns]
        if len(hit) == 1:
            return hit[0]
    return None


def resolve_chain(segs, f, vis, aliases):
    """The type a dotted chain names, and the index of the segment after it (for static member access)."""
    if segs[0] in aliases:
        target = aliases[segs[0]]
        if target in type_by_full:
            return type_by_full[target], 1
        segs = target.split(".") + segs[1:]
    for prefix in [""] + vis:   # namespace-qualified: KryticalNova.Progression.DamageResult or Progression.DamageResult
        for k in range(len(segs) - 1, 0, -1):
            ns = ".".join(([prefix] if prefix else []) + segs[:k])
            if ns in namespaces:
                full = ns + "." + segs[k]
                if full in type_by_full:
                    return type_by_full[full], k + 1
                break
    tid = resolve_simple(segs[0], f, vis)
    if tid is not None:
        return tid, 1
    return None, 0


line_starts_cache = {}


def line_of(fi, pos):
    ls = line_starts_cache.get(fi)
    if ls is None:
        ls = [0] + [m.end() for m in re.finditer("\n", files[fi]["text"])]
        line_starts_cache[fi] = ls
    return bisect.bisect_right(ls, pos)


edges = collections.defaultdict(lambda: [0, None])     # (from type, to type) -> [kinds, first (file, line)]
file_edges = collections.defaultdict(lambda: [0, 0])   # (file, to type) -> [kinds, first line]
static_uses = collections.defaultdict(set)              # (type, member) -> {file}
static_lines = {}

for fi, f in enumerate(files):
    t = f["text"]
    vis, aliases, _ = visible_namespaces(f)
    spans = [(d["start"], d["end"], d["tid"]) for d in f["decls"]]
    lines = t.split("\n")
    for m in CHAIN.finditer(t):
        owner = None
        for s, e, tid in spans:
            if s <= m.start() <= e:
                owner = tid
                break
        if owner is None:
            continue
        segs = [s.strip() for s in m.group(1).split(".")]
        if segs[0] in KEYWORDS:
            continue
        # a bare name called like a method ("ServerSave(index)") is a method, unless it is constructed ("new X(")
        if len(segs) == 1 and t[m.end():m.end() + 1] == "(" and not re.search(r"\bnew\s*$", t[max(0, m.start() - 8):m.start()]):
            continue
        tid, nxt = resolve_chain(segs, f, vis, aliases)
        if tid is None or tid == owner:
            continue
        before = t[max(0, m.start() - 40):m.start()]
        kind = K_USE
        ln = line_of(fi, m.start())
        line = lines[ln - 1] if ln - 1 < len(lines) else ""
        d_owner = next(d for d in f["decls"] if d["tid"] == owner)
        if d_owner["start"] <= m.start() < d_owner["brace"]:
            kind = K_INHERIT
        elif re.search(r"(GetComponent\w*|TryGetComponent|AddComponent|RequireComponent)\s*(<|\(\s*typeof\s*\()\s*$", before):
            kind = K_GETCOMP
        elif re.search(r"(Find\w*Object\w*Of\w*Type|FindObjectsByType|FindFirstObjectByType|FindAnyObjectByType)\s*<\s*$", before):
            kind = K_FIND
        elif "SerializeField" in line or (ln >= 2 and "SerializeField" in lines[ln - 2] and "(" not in line):
            kind = K_SERIALIZED
        elif nxt < len(segs) and segs[nxt] in types[tid]["statics"]:
            kind = K_STATIC
            member = segs[nxt]
            static_uses[(tid, member)].add(fi)
            static_lines.setdefault((tid, member, fi), ln)
        e = edges[(owner, tid)]
        e[0] |= kind
        if e[1] is None:
            e[1] = (fi, ln)
        fe = file_edges[(fi, tid)]
        fe[0] |= kind
        if not fe[1]:
            fe[1] = ln

# --- 3. assets ---------------------------------------------------------------------------------------------------------
script_guid = {}   # .cs guid -> type id (the type named like the file, as Unity requires)
for fi, f in enumerate(files):
    if not f["guid"]:
        continue
    stem = os.path.basename(f["p"])[:-3]
    for d in f["decls"]:
        if types[d["tid"]]["n"] == stem:
            script_guid[f["guid"]] = d["tid"]

GUID_RE = re.compile(rb"guid: ([0-9a-f]{32})")
SCRIPT_RE = re.compile(rb"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})")
MANAGED_RE = re.compile(rb"type: \{class: ([\w`]+), ns: ([\w.]*), asm: ([\w.\-]+)\}")
asset_roots = [GAME, os.path.join(ROOT, "Assets", "Resources")]
asset_files = [os.path.join(ROOT, "Assets", "DefaultNetworkPrefabs.asset")]
for r in asset_roots:
    for folder, dirs, names in os.walk(r):
        dirs[:] = [d for d in dirs if not d.endswith("~")]
        for name in names:
            if name.endswith((".prefab", ".unity", ".asset")):
                asset_files.append(os.path.join(folder, name))

assets = []           # {p, k, sys, cls, scripts:set, refs:set(guid), managed:set}
asset_by_guid = {}
for full in asset_files:
    try:
        with open(full, "rb") as fh:
            data = fh.read()
    except OSError:
        continue
    if not data.startswith(b"%YAML"):
        continue
    p = rel(full)
    k = "scene" if p.endswith(".unity") else "prefab" if p.endswith(".prefab") else "asset"
    scripts = [g.decode() for g in SCRIPT_RE.findall(data)]
    a = {"p": p, "k": k, "sys": system_of(os.path.relpath(full, GAME).replace("\\", "/")) if full.startswith(GAME) else "Assets",
         "scripts": collections.Counter(script_guid[g] for g in scripts if g in script_guid),
         "refs": set(g.decode() for g in GUID_RE.findall(data)),
         "managed": collections.Counter((c.decode(), n.decode(), s.decode()) for c, n, s in MANAGED_RE.findall(data)),
         "cls": None}
    if k == "asset" and scripts:
        a["cls"] = script_guid.get(scripts[0])
    g = meta_guid(full)
    if g:
        asset_by_guid[g] = len(assets)
    assets.append(a)

held_by = collections.defaultdict(lambda: collections.Counter())   # type -> {prefab/scene/asset kind: count}
held_list = collections.defaultdict(list)                          # type -> prefab / scene paths
so_assets = collections.defaultdict(list)                          # SO type -> asset indices
data_refs = collections.defaultdict(lambda: collections.defaultdict(set))   # SO type -> {kind: set of referrer idx}
managed_types = collections.defaultdict(lambda: [0, set(), None])    # type -> [entries, assets, recorded asm]
for ai, a in enumerate(assets):
    for tid, n in a["scripts"].items():
        if a["cls"] == tid:
            continue
        held_by[tid][a["k"]] += 1
        held_list[tid].append(ai)
    if a["cls"] is not None:
        so_assets[a["cls"]].append(ai)
    for (cls, ns, asm), n in a["managed"].items():
        full = (ns + "." if ns else "") + cls.split("`")[0]
        if full in type_by_full:
            mt = managed_types[type_by_full[full]]
            mt[0] += n
            mt[1].add(ai)
            mt[2] = asm
for ai, a in enumerate(assets):
    for g in a["refs"]:
        bi = asset_by_guid.get(g)
        if bi is not None and bi != ai and assets[bi]["cls"] is not None:
            data_refs[assets[bi]["cls"]][a["k"]].add(ai)

# --- 4. tests ----------------------------------------------------------------------------------------------------------
tested_by = collections.defaultdict(set)
for (fi, tid), (k, ln) in file_edges.items():
    if files[fi]["kind"] == "test" and types[tid]["kind"] == "rt":
        tested_by[tid].add(fi)

# --- analysis ----------------------------------------------------------------------------------------------------------
RT = [i for i, ty in enumerate(types) if ty["kind"] == "rt"]
rt_set = set(RT)
fwd = collections.defaultdict(set)
rev = collections.defaultdict(set)
for (a, b), (k, _) in edges.items():
    if a in rt_set and b in rt_set:
        fwd[a].add(b)
        rev[b].add(a)


def closure(start, graph):
    seen, todo = set(), [start]
    while todo:
        n = todo.pop()
        for m in graph.get(n, ()):
            if m not in seen:
                seen.add(m)
                todo.append(m)
    seen.discard(start)
    return seen


systems = sorted(set(f["sys"] for f in files if f["kind"] == "rt"))
sys_lines = collections.Counter()
sys_files = collections.Counter()
sys_test_files = collections.Counter()
for f in files:
    if f["kind"] == "rt":
        sys_lines[f["sys"]] += f["lines"]
        sys_files[f["sys"]] += 1
    else:
        sys_test_files[f["sys"]] += 1

# system edges: (a, b) -> {files, types, refs}
sys_edges = collections.defaultdict(lambda: {"files": set(), "types": set(), "pairs": []})
for (fi, tid), (k, ln) in file_edges.items():
    f, ty = files[fi], types[tid]
    if f["kind"] != "rt" or ty["kind"] != "rt" or f["sys"] == ty["sys"]:
        continue
    se = sys_edges[(f["sys"], ty["sys"])]
    se["files"].add(fi)
    se["types"].add(tid)
    se["pairs"].append((fi, tid, k, ln))

weight = {e: len(v["files"]) for e, v in sys_edges.items()}


def eades(nodes, w):
    """Order nodes so that as few weighted edges as possible point backwards (Eades-Lin-Smyth greedy)."""
    nodes = set(nodes)
    out_w = lambda n: sum(w.get((n, m), 0) for m in nodes if m != n)
    in_w = lambda n: sum(w.get((m, n), 0) for m in nodes if m != n)
    s1, s2 = [], []
    while nodes:
        changed = True
        while changed:
            changed = False
            for n in sorted(nodes):
                if in_w(n) == 0 and n in nodes and out_w(n) >= 0 and not any(w.get((m, n), 0) for m in nodes if m != n):
                    s1.append(n)
                    nodes.discard(n)
                    changed = True
            for n in sorted(nodes):
                if not any(w.get((n, m), 0) for m in nodes if m != n):
                    s2.insert(0, n)
                    nodes.discard(n)
                    changed = True
        if nodes:
            n = max(sorted(nodes), key=lambda x: out_w(x) - in_w(x))
            s1.append(n)
            nodes.discard(n)
    return s1 + s2   # users first, foundations last


order = []
for rank, _, _ in sorted(RANKS, reverse=True):
    order += eades([s for s in systems if rank_of(s) == rank], weight)
pos = {s: i for i, s in enumerate(order)}
for (a, b), v in sys_edges.items():
    v["uphill"] = rank_of(b) > rank_of(a)
    v["back"] = pos[b] < pos[a]          # a foundation-side system reaching a user-side one: a cycle to cut
    v["twoway"] = (b, a) in sys_edges

# layers for the drawing: longest path to the bottom over the forward edges only
level = {}


def lvl(s, stack=()):
    if s in level:
        return level[s]
    downs = [b for (a, b), v in sys_edges.items() if a == s and not v["back"] and b not in stack]
    level[s] = 1 + max((lvl(b, stack + (s,)) for b in downs), default=-1)
    return level[s]


for s in systems:
    lvl(s)

fan_out = collections.Counter()
fan_in = collections.Counter()
for (a, b), v in sys_edges.items():
    fan_out[a] += len(v["types"])
    fan_in[b] += len(v["types"])

# ripple per runtime type
ripple = {}
for tid in RT:
    direct_files = set(fi for (fi, t2), _ in file_edges.items() if t2 == tid and files[fi]["kind"] == "rt"
                       and fi not in types[tid]["files"])
    ripple[tid] = direct_files
trans = {tid: closure(tid, rev) for tid in RT}

# the largest tangle: runtime types that all reach each other (strongly connected, Tarjan)
index_of, low, on_stack, stack, sccs = {}, {}, set(), [], []
sys.setrecursionlimit(10000)


def strong(v):
    index_of[v] = low[v] = len(index_of)
    stack.append(v)
    on_stack.add(v)
    for w in fwd.get(v, ()):
        if w not in index_of:
            strong(w)
            low[v] = min(low[v], low[w])
        elif w in on_stack:
            low[v] = min(low[v], index_of[w])
    if low[v] == index_of[v]:
        comp = []
        while True:
            w = stack.pop()
            on_stack.discard(w)
            comp.append(w)
            if w == v:
                break
        sccs.append(comp)


for tid in RT:
    if tid not in index_of:
        strong(tid)
tangle = max(sccs, key=len)
tangle_set = set(tangle)
tangle_sys = collections.Counter(types[t]["sys"] for t in tangle)

# god classes
type_lines = {tid: types[tid].get("dl", 0) for tid in range(len(types))}
type_out_sys = {tid: sorted(set(types[b]["sys"] for b in fwd.get(tid, ()) if types[b]["sys"] != types[tid]["sys"]))
                for tid in RT}

# hubs: static members used from other systems
hubs = []
for (tid, member), fis in static_uses.items():
    ty = types[tid]
    if ty["kind"] != "rt":
        continue
    rt_fis = [fi for fi in fis if files[fi]["kind"] == "rt" and fi not in ty["files"]]
    other = set(files[fi]["sys"] for fi in rt_fis if files[fi]["sys"] != ty["sys"])
    kind = ty["statics"].get(member, "?")
    locator = member in ("Instance", "Local", "Current", "Active", "Main", "Shared", "Default")
    risky = kind in ("event", "state") or locator
    if not rt_fis or (len(other) < 2 and len(rt_fis) < 10):
        continue
    if kind in ("const", "readonly", "method") and not locator and len(other) < 4:
        continue
    hubs.append({"t": tid, "m": member, "k": "locator" if locator else kind, "files": sorted(rt_fis),
                 "sys": sorted(other), "risk": risky})
hubs.sort(key=lambda h: (-h["risk"], -len(h["sys"]), -len(h["files"])))

# static mutable state per type (hidden global state)
global_state = []
for tid in RT:
    st = [m for m, k in types[tid]["statics"].items() if k == "state"]
    ev = [m for m, k in types[tid]["statics"].items() if k == "event"]
    if st or ev:
        global_state.append({"t": tid, "state": st, "events": ev})

# --- design data and runtime smells (line scans over runtime files) -----------------------------------------------------
NUM_CONST = re.compile(r"\b(?:const|static\s+readonly)\s+(?:float|int|double|long|short|byte|uint|ushort)\s+(\w+)\s*=\s*(-?[\d_.]+f?)\s*;")
ENGINEERING = re.compile(r"(Id|ID|Hash|Mask|Layer|Bits?|Shader|Prop|Version|Magic|Index|Count|Capacity|Size)$")
FALLBACK = re.compile(r"(\w*(?:settings|Settings|tuning|Tuning|config|Config|Data|data)\w*)\s*(?:!=\s*null\s*\?\s*[\w.]+\s*:\s*|\?\.\s*\w+\s*\?\?\s*)(-?[\d.]+f?)")
STRING_LOOKUP = re.compile(r"Type\.GetType\s*\(|\.SendMessage\s*\(|(?:^|[^.?\w])Invoke\s*\(\s*\"|Resources\.Load\w*\s*[<(]|GameObject\.Find\s*\(|Shader\.Find\s*\(|transform\.Find\s*\(|\.Find\s*\(\s*\"")
FIND_RT = re.compile(r"\b(FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType|FindObjectsByType)\b")
LOCAL_GETCOMP = re.compile(r"\bstatic\s+[\w.<>]+\s+\w+\s*=>\s*[^;]*GetComponent")
PIPELINE_TYPES = {"UniversalRenderPipelineAsset", "PanelSettings", "RenderPipelineAsset"}

hardcoded = collections.defaultdict(list)
fallbacks, string_lookups, finds, local_getcomp, so_writes = [], [], [], [], []
so_type_names = set(types[t]["n"] for t in RT if types[t]["role"] == "so") | PIPELINE_TYPES
for fi, f in enumerate(files):
    if f["kind"] != "rt":
        continue
    raw_lines = f["raw"].split("\n")
    text_lines = f["text"].split("\n")
    so_vars = set(m.group(2) for m in re.finditer(r"\b(" + "|".join(sorted(so_type_names)) + r")\s+(\w+)\s*[;=)]", f["text"]))
    for i, line in enumerate(text_lines):
        n = i + 1
        src = raw_lines[i].strip() if i < len(raw_lines) else ""
        if f["sys"].startswith("Gameplay/"):
            for m in NUM_CONST.finditer(line):
                if not ENGINEERING.search(m.group(1)) and not m.group(1).startswith("Default") and m.group(2).strip("-f") not in ("0", "1", "0.0", "1.0"):
                    hardcoded[fi].append((n, m.group(1), m.group(2)))
        if FALLBACK.search(line):
            fallbacks.append((fi, n, src))
        for m in STRING_LOOKUP.finditer(raw_lines[i] if i < len(raw_lines) else ""):
            if "//" not in raw_lines[i][:m.start()]:
                string_lookups.append((fi, n, src))
                break
        if FIND_RT.search(line):
            finds.append((fi, n, src))
        if LOCAL_GETCOMP.search(line):
            local_getcomp.append((fi, n, src))
        for v in so_vars:
            if re.search(r"\b" + re.escape(v) + r"\.\w+\s*=(?!=)", line):
                so_writes.append((fi, n, src))
                break

# --- problems: ranked list ---------------------------------------------------------------------------------------------
# Each problem gets an impact (1 to 5: how much it gets in the way of changing the game later), the number of files a
# fix would touch, and an effort (1 small, 2 medium, 3 large). Priority puts high impact first and, among equals, the
# cheaper fix first.
problems = []


def ev(fi, ln, note=""):
    return {"f": fi, "l": ln, "n": note}


def add_problem(cat, title, detail, sys_list, impact, touch, evidence, effort=None):
    if effort is None:
        effort = 1 if touch <= 3 else 2 if touch <= 12 else 3
    problems.append({"cat": cat, "title": title, "detail": detail, "sys": sys_list, "impact": impact, "touch": touch,
                     "effort": effort, "score": round(impact * 10 - effort * 3 + min(touch, 40) / 8, 1),
                     "ev": evidence[:40]})


for (a, b), v in sys_edges.items():
    if v["uphill"]:
        tnames = sorted(set(types[t]["n"] for t in v["types"]))
        add_problem("Uphill", f"{a} uses {b}",
                    f"{a} (layer {rank_of(a)}) reaches up into {b} (layer {rank_of(b)}): {len(v['files'])} file(s) use "
                    f"{', '.join(tnames[:8])}{'...' if len(tnames) > 8 else ''}. A lower layer that knows a higher one "
                    "cannot be changed, tested or split out without dragging the higher one along.",
                    [a, b], 5, len(v["files"]), [ev(fi, ln, types[t]["n"]) for fi, t, k, ln in sorted(v["pairs"])])
for (a, b), v in sys_edges.items():
    if v["back"] and not v["uphill"]:
        other = sys_edges.get((b, a))
        tnames = sorted(set(types[t]["n"] for t in v["types"]))
        add_problem("Cycle", f"{a} and {b} need each other" if other else f"{a} uses {b} against the order",
                    f"{b} uses {a} in {len(other['files'])} file(s), and {a} uses {b} back in {len(v['files'])} "
                    f"file(s) ({', '.join(tnames[:8])}{'...' if len(tnames) > 8 else ''}). Cutting the {a} -> {b} "
                    "references breaks the loop, so the two can be changed and split apart separately." if other else
                    f"{a} uses {b} ({', '.join(tnames[:8])}) although {b} sits above it in the system order: part of a "
                    "longer loop through other systems.",
                    [a, b], 4 if other else 3, len(v["files"]),
                    [ev(fi, ln, types[t]["n"]) for fi, t, k, ln in sorted(v["pairs"])])
for h in hubs:
    if not h["risk"]:
        continue
    ty = types[h["t"]]
    add_problem("Hub", f"{ty['n']}.{h['m']} ({h['k']})",
                f"A static {h['k']} used by {len(h['files'])} runtime file(s) in {len(h['sys'])} other system(s) "
                f"({', '.join(h['sys'][:8])}). Every caller is tied to this one class and to the moment it is set; a "
                "change to it touches them all, and none of them can be tested without it.",
                [ty["sys"]] + h["sys"], 4 if len(h["sys"]) >= 5 else 3, len(h["files"]),
                [ev(fi, static_lines.get((h["t"], h["m"], fi), 0)) for fi in h["files"]])
for tid in RT:
    ty = types[tid]
    if ty["role"] in ("enum", "interface"):
        continue
    if type_lines[tid] >= 700 and len(type_out_sys.get(tid, [])) >= 3:
        add_problem("God class", f"{ty['n']} ({type_lines[tid]} lines)",
                    f"Does work for {len(type_out_sys[tid])} other systems ({', '.join(type_out_sys[tid])}) and is used "
                    f"by {len(ripple[tid])} file(s). A change to one of its jobs risks the others.",
                    [ty["sys"]], 4 if len(ripple[tid]) >= 40 else 3, len(ty["files"]) + len(ripple[tid]),
                    [ev(fi, 1) for fi in ty["files"]], effort=3)
for tid in RT:
    ty = types[tid]
    if ty["role"] in ("enum",) or tested_by.get(tid):
        continue
    if len(ripple[tid]) >= 25:
        add_problem("Untested", f"{ty['n']} has no test",
                    f"{len(ripple[tid])} runtime file(s) depend on it and {len(trans[tid])} type(s) depend on it "
                    "through others, but no test touches it. A refactor here is blind.",
                    [ty["sys"]], 3, 1, [ev(ty["files"][0], 1)], effort=1 if ty["role"] in ("plain", "so") else 2)
regs = [g for g in global_state if any(re.search(r"Online|Everyone|Players|ByMember|Index|Active", s) for s in g["state"])]
if regs:
    add_problem("Duplicated state", f"{len(regs)} classes keep their own static player / object lists",
                "Each keeps its own static list or dictionary of who is online or what is active: "
                + "; ".join(f"{types[g['t']]['n']}: {', '.join(g['state'][:4])}" for g in regs[:18])
                + ". They can drift apart, and every one is global state a refactor has to find.",
                sorted(set(types[g["t"]]["sys"] for g in regs)), 3, len(regs),
                [ev(types[g["t"]]["files"][0], 1, types[g["t"]]["n"]) for g in regs])
hc_total = sum(len(v) for v in hardcoded.values())
if hc_total:
    add_problem("Design data", f"{hc_total} gameplay numbers fixed in code",
                f"Named numbers in Gameplay runtime code that a designer cannot change in the Inspector (ids, masks, "
                f"sizes, Default* seeds and 0/1 left out) across {len(hardcoded)} files. The worst files are listed.",
                sorted(set(files[fi]["sys"] for fi in hardcoded)), 3, len(hardcoded),
                [ev(fi, v[0][0], f"{len(v)} numbers: " + ", ".join(x[1] for x in v[:5]))
                 for fi, v in sorted(hardcoded.items(), key=lambda x: -len(x[1]))])
if fallbacks:
    add_problem("Design data", f"{len(fallbacks)} settings fallbacks repeat the asset's numbers in code",
                "\"settings != null ? settings.x : 8\": when a settings asset is not wired the game silently runs on "
                "a copy of the number kept in code, so a wiring mistake hides and the two copies drift apart.",
                sorted(set(files[fi]["sys"] for fi, _, _ in fallbacks)), 2, len(set(fi for fi, _, _ in fallbacks)),
                [ev(fi, n, s[:90]) for fi, n, s in fallbacks], effort=2)
if string_lookups:
    add_problem("Hidden link", f"{len(string_lookups)} lookups by name (strings)",
                "Type.GetType, Find(\"name\"), Resources.Load(\"path\"), Shader.Find, SendMessage: the compiler cannot "
                "see these, so renaming or moving the target breaks them silently, only at run time.",
                sorted(set(files[fi]["sys"] for fi, _, _ in string_lookups)), 2,
                len(set(fi for fi, _, _ in string_lookups)), [ev(fi, n, s[:90]) for fi, n, s in string_lookups])
managed_total = sum(v[0] for v in managed_types.values())
if managed_total:
    add_problem("Hidden link", f"{managed_total} asset entries store a class by name and assembly",
                "[SerializeReference] entries in prefabs record class, namespace and assembly as text: "
                + ", ".join(f"{types[t]['n']} ({v[0]})" for t, v in sorted(managed_types.items(), key=lambda x: -x[1][0])[:10])
                + ". Renaming one of these classes, changing its namespace or moving it to another assembly empties "
                "those entries unless the class carries [MovedFrom].",
                sorted(set(types[t]["sys"] for t in managed_types)), 4, len(managed_types),
                [ev(types[t]["files"][0], 1, f"{v[0]} entries in {len(v[1])} asset(s)") for t, v in managed_types.items()],
                effort=1)
if so_writes:
    add_problem("Design data", f"{len(so_writes)} runtime writes to shared assets",
                "Writing a field of a ScriptableObject or the render pipeline asset during Play changes the asset on "
                "disk in the Editor and leaks between Play sessions unless it is put back.",
                sorted(set(files[fi]["sys"] for fi, _, _ in so_writes)), 2, len(set(fi for fi, _, _ in so_writes)),
                [ev(fi, n, s[:90]) for fi, n, s in so_writes], effort=1)
if local_getcomp:
    add_problem("Runtime cost", f"{len(local_getcomp)} static accessors call GetComponent on every use",
                "\"static X Local => PlayerInventory.Local?.GetComponent<X>()\": a component search on every call, and "
                "every one of them ties its callers to PlayerInventory as the way to find the local player.",
                sorted(set(files[fi]["sys"] for fi, _, _ in local_getcomp)), 2, len(local_getcomp),
                [ev(fi, n, s[:90]) for fi, n, s in local_getcomp], effort=1)
problems.sort(key=lambda p: (-p["score"], p["title"]))
for i, p in enumerate(problems):
    p["id"] = i + 1

# --- baseline / check --------------------------------------------------------------------------------------------------
today_uphill = sorted(f"{a} -> {b}" for (a, b), v in sys_edges.items() if v["uphill"])
today_twoway = sorted(f"{a} <-> {b}" for (a, b), v in sys_edges.items() if v["twoway"] and a < b)
# scripts that prefabs, scenes or data assets hold, by the GUID in their .meta: a move that loses the .meta (or a class
# renamed away from its file name) leaves every holder with a missing script
held_guids = {f["guid"]: f["p"] for f in files if f["guid"] and f["kind"] == "rt"
              and (script_guid.get(f["guid"]) in held_by or script_guid.get(f["guid"]) in so_assets)}


def managed_problems():
    """[SerializeReference] entries whose class, namespace or assembly no longer matches, with no [MovedFrom]."""
    out = []
    seen = set()
    for a in assets:
        for (cls, ns, asm) in a["managed"]:
            key = (cls, ns, asm)
            if key in seen or not asm.startswith("KryticalNova"):   # vendor and Unity classes are not ours to guard
                continue
            seen.add(key)
            full = (ns + "." if ns else "") + cls.split("`")[0]
            tid = type_by_full.get(full)
            if tid is not None and types[tid]["asm"] == asm:
                continue
            moved = any("MovedFrom" in files[fi]["raw"] for t in simple_index.get(cls.split("`")[0], []) for fi in types[t]["files"])
            if not moved:
                where = f"now {types[tid]['asm']}" if tid is not None else "no class by that name and namespace"
                out.append(f"{full} ({asm}; {where}) in {a['p']}")
    return out


status = 0
if "--baseline" in ARGS:
    with open(BASELINE, "w", encoding="utf-8") as fh:
        json.dump({"date": datetime.date.today().isoformat(), "uphill": today_uphill, "twoway": today_twoway,
                   "heldScripts": dict(sorted(held_guids.items(), key=lambda x: x[1]))}, fh, indent=1)
    print(f"baseline saved: {len(today_uphill)} uphill system links, {len(today_twoway)} two-way pairs, "
          f"{len(held_guids)} scripts held by assets")
if "--check" in ARGS:
    if not os.path.exists(BASELINE):
        print("no baseline.json yet: run with --baseline first")
        status = 1
    else:
        base = load_json(BASELINE)
        new_up = [x for x in today_uphill if x not in base.get("uphill", [])]
        new_tw = [x for x in today_twoway if x not in base.get("twoway", [])]
        gone = [x for x in base.get("uphill", []) + base.get("twoway", []) if x not in today_uphill + today_twoway]
        for x in new_up:
            print("NEW uphill link:", x)
        for x in new_tw:
            print("NEW two-way pair:", x)
        for x in gone:
            print("fixed since the baseline:", x)
        lost = [p for g, p in base.get("heldScripts", {}).items() if g not in script_guid]
        for p in lost:
            print("LOST script that assets hold (its .meta GUID is gone, or the class no longer matches the file name):", p)
        broken = managed_problems()
        for x in broken:
            print("ASSET ENTRY WOULD EMPTY ([SerializeReference] class moved without [MovedFrom]):", x)
        status = 1 if (new_up or new_tw or lost or broken) else 0
        print("check:", "FAILED" if status else "clean")

# --- output data -------------------------------------------------------------------------------------------------------
type_files_rt = {tid: [fi for fi in types[tid]["files"]] for tid in range(len(types))}
out_types = []
for tid, ty in enumerate(types):
    outs = sorted((b, edges[(tid, b)][0]) for b in set(b for (a, b) in edges if a == tid))
    o = {"n": ty["n"], "ns": ty["ns"], "kw": ty["kw"], "role": ty["role"], "sys": ty["sys"], "k": ty["kind"],
         "asm": ty["asm"], "f": ty["files"], "lines": type_lines[tid], "out": [[b, k] for b, k in outs]}
    if ty["nested"]:
        o["nested"] = ty["nested"]
    if ty.get("feel"):
        o["feel"] = 1
    if tid in tangle_set:
        o["tg"] = 1
    if ty["kind"] == "rt":
        o["rip"] =[len(ripple[tid]), len(set(files[fi]["sys"] for fi in ripple[tid])), len(trans[tid])]
        o["tests"] = sorted(tested_by.get(tid, ()))
        if held_by.get(tid):
            o["held"] = dict(held_by[tid])
            o["heldList"] = [i for i in held_list[tid] if assets[i]["k"] != "asset"][:60]
            o["heldAssets"] = [i for i in held_list[tid] if assets[i]["k"] == "asset"][:20]
        if so_assets.get(tid):
            o["soAssets"] = len(so_assets[tid])
            o["soSample"] = so_assets[tid][:25]
            o["dataRefs"] = {k: sorted(v)[:40] for k, v in data_refs[tid].items()}
            o["dataRefCount"] = {k: len(v) for k, v in data_refs[tid].items()}
        if tid in managed_types:
            o["managed"] = [managed_types[tid][0], len(managed_types[tid][1]), managed_types[tid][2]]
        st = {m: k for m, k in ty["statics"].items() if k in ("event", "state")}
        if st:
            o["statics"] = st
    out_types.append(o)

needed_assets = set()
for o in out_types:
    for key in ("heldList", "heldAssets", "soSample"):
        needed_assets.update(o.get(key, []))
    for v in o.get("dataRefs", {}).values():
        needed_assets.update(v)
asset_map = {ai: i for i, ai in enumerate(sorted(needed_assets))}
for o in out_types:
    for key in ("heldList", "heldAssets", "soSample"):
        if key in o:
            o[key] = [asset_map[a] for a in o[key]]
    if "dataRefs" in o:
        o["dataRefs"] = {k: [asset_map[a] for a in v] for k, v in o["dataRefs"].items()}

asset_kind_count = collections.Counter(a["k"] for a in assets)
sys_rows = []
for s in systems:
    rt_types = [t for t in RT if types[t]["sys"] == s and types[t]["role"] not in ("enum", "interface")]
    touched = [t for t in rt_types if tested_by.get(t)]
    ce, ca = fan_out[s], fan_in[s]
    sys_rows.append({"id": s, "rank": rank_of(s), "layer": next(n for r, n, _ in RANKS if r == rank_of(s)),
                     "level": level[s], "order": pos[s], "files": sys_files[s], "lines": sys_lines[s],
                     "types": len([t for t in RT if types[t]["sys"] == s]), "fanIn": ca, "fanOut": ce,
                     "inst": round(ce / (ce + ca), 2) if ce + ca else 0, "testFiles": sys_test_files[s],
                     "tested": len(touched), "testable": len(rt_types),
                     "asm": sorted(set(f["asm"] for f in files if f["sys"] == s and f["kind"] == "rt")),
                     "so": len([t for t in RT if types[t]["sys"] == s and types[t]["role"] == "so"]),
                     "net": len([t for t in RT if types[t]["sys"] == s and types[t]["role"] == "net"])})

# --- the map against the compiler ---------------------------------------------------------------------------------------
# Each assembly declares the assemblies it may use. A link the map found in code that no declaration allows would not
# compile, so it means the map misread a name. A declaration with no link in code is either a reference nothing uses
# (it can go) or one the compiler needs although no line names the type: a member that returns another assembly's type
# (CS0012), which a text reader cannot see.
sys_of_asm = {a: r["id"] for r in sys_rows for a in r["asm"]}
declared = set()
for a, refs in ASM_REFS.items():
    for r in refs:
        sa, sb = sys_of_asm.get(a), sys_of_asm.get(r)
        if sa and sb and sa != sb:
            declared.add((sa, sb))
unseen = sorted(declared - set(sys_edges))
refused = sorted(set(sys_edges) - declared)

data = {
    "meta": {"generated": datetime.datetime.now().strftime("%d %b %Y, %H:%M"),
             "declared": len(declared), "unseen": len(unseen), "refused": len(refused),
             "files": len([f for f in files if f["kind"] == "rt"]), "testFiles": len([f for f in files if f["kind"] == "test"]),
             "lines": sum(f["lines"] for f in files if f["kind"] == "rt"), "types": len(RT),
             "so": len([t for t in RT if types[t]["role"] == "so"]), "net": len([t for t in RT if types[t]["role"] == "net"]),
             "mono": len([t for t in RT if types[t]["role"] == "mono"]),
             "assets": dict(asset_kind_count), "asmrefs": dict(ASMREF_COUNT),
             "ranks": [{"rank": r, "name": n, "members": m} for r, n, m in RANKS],
             "uphill": len(today_uphill), "twoway": len(today_twoway),
             "back": len([1 for v in sys_edges.values() if v["back"]]),
             "tangle": len(tangle), "tangleSys": dict(tangle_sys.most_common())},
    "assemblies": {a: [r for r in refs if r.startswith("KryticalNova")] for a, refs in ASM_REFS.items()
                   if a.startswith("KryticalNova") and ".Tests" not in a and ".Editor" not in a},
    "systems": sys_rows,
    "sysEdges": [{"a": a, "b": b, "files": len(v["files"]), "types": len(v["types"]), "uphill": v["uphill"],
                  "back": v["back"], "twoway": v["twoway"],
                  "pairs": [[fi, t, k, ln] for fi, t, k, ln in sorted(v["pairs"])][:400]}
                 for (a, b), v in sorted(sys_edges.items())],
    "unseen": [{"a": a, "b": b} for a, b in unseen],
    "refused": [{"a": a, "b": b} for a, b in refused],
    "files": [{"p": f["p"], "s": f["sys"], "k": f["kind"], "l": f["lines"], "a": f["asm"]} for f in files],
    "types": out_types,
    "assets": [{"p": assets[ai]["p"], "k": assets[ai]["k"]} for ai in sorted(needed_assets)],
    "hubs": [{"t": h["t"], "m": h["m"], "k": h["k"], "f": h["files"], "s": h["sys"], "risk": h["risk"]} for h in hubs],
    "state": [{"t": g["t"], "state": g["state"], "events": g["events"]} for g in global_state],
    "problems": problems,
    "smells": {"hardcoded": [{"f": fi, "items": v} for fi, v in sorted(hardcoded.items(), key=lambda x: -len(x[1]))],
               "fallbacks": fallbacks, "strings": string_lookups, "finds": finds, "localGetComponent": local_getcomp,
               "soWrites": so_writes},
}

os.makedirs(OUT_DIR, exist_ok=True)
with open(os.path.join(OUT_DIR, "dependency-map.json"), "w", encoding="utf-8") as fh:
    json.dump(data, fh, separators=(",", ":"))
template = read(os.path.join(HERE, "template.html"))
page = template.replace("/*__DATA__*/null", json.dumps(data, separators=(",", ":")).replace("</", "<\\/"))
with open(os.path.join(OUT_DIR, "dependency-map.html"), "w", encoding="utf-8", newline="\n") as fh:
    fh.write(page)


# --- the doc's generated tables ----------------------------------------------------------------------------------------
def md_table(head, rows):
    return "\n".join(["| " + " | ".join(head) + " |", "|" + "|".join("---" for _ in head) + "|"] +
                     ["| " + " | ".join(str(c).replace("|", "\\|") for c in r) + " |" for r in rows])


def tname(tid):
    return types[tid]["n"]


blocks = {}
blocks["summary"] = (
    f"Generated {data['meta']['generated']}: {data['meta']['files']} runtime scripts ({data['meta']['lines']:,} lines) "
    f"declaring {data['meta']['types']} types ({data['meta']['mono']} MonoBehaviours, {data['meta']['net']} "
    f"NetworkBehaviours, {data['meta']['so']} ScriptableObject classes); {data['meta']['testFiles']} test scripts; "
    f"{asset_kind_count['prefab']} prefabs, {asset_kind_count['scene']} scenes and {asset_kind_count['asset']} assets "
    f"read. {len(sys_edges)} system-to-system links, of which {data['meta']['uphill']} run uphill, "
    f"{data['meta']['twoway']} pairs of systems use each other both ways, and {data['meta']['back']} links would have "
    f"to go for the systems to form a clean stack. The largest tangle, classes that all reach each other through "
    f"some chain of uses, holds {len(tangle)} classes from {len(tangle_sys)} systems "
    f"({', '.join(f'{s} {n}' for s, n in tangle_sys.most_common(6))}, ...).")
blocks["systems"] = md_table(
    ["System", "Layer", "Scripts", "Lines", "Uses (types)", "Used by (types)", "Instability", "Tests", "Types touched by a test"],
    [[r["id"], r["layer"], r["files"], f"{r['lines']:,}", r["fanOut"], r["fanIn"], r["inst"], r["testFiles"],
      f"{r['tested']} of {r['testable']}"] for r in sorted(sys_rows, key=lambda r: (r["rank"], r["order"]))])
blocks["compiler"] = (
    f"Checked against the {len(declared)} references the assemblies declare between systems: "
    + (f"**{len(refused)} links found in code that no declaration allows** (the map misread them: "
       + ", ".join(f"{a} -> {b}" for a, b in refused) + "). " if refused else
       "every link the map found is one the compiler allows. ")
    + (f"{len(unseen)} declared references name no type in code: "
       + ", ".join(f"{a} -> {b}" for a, b in unseen)
       + ". Each is either unused and can go, or needed by the compiler for a type reached through another "
         "assembly's member (error CS0012 when removed), which reading the code as text cannot see." if unseen else
       "Every declared reference is used in code."))
blocks["uphill"] = md_table(
    ["From", "Uses (higher layer)", "Files", "Types it uses", "First place"],
    [[a, b, len(v["files"]), ", ".join(sorted(set(tname(t) for t in v["types"])))[:160],
      f"`{files[v['pairs'][0][0]]['p'].replace('Assets/_Game/', '')}:{v['pairs'][0][3]}`"]
     for (a, b), v in sorted(sys_edges.items(), key=lambda x: -len(x[1]["files"])) if v["uphill"]]) or "None."
blocks["cycles"] = md_table(
    ["Cut this link", "Files", "Types it uses", "The other way (files)"],
    [[f"{a} -> {b}", len(v["files"]), ", ".join(sorted(set(tname(t) for t in v["types"])))[:160],
      len(sys_edges[(b, a)]["files"]) if (b, a) in sys_edges else "longer loop"]
     for (a, b), v in sorted(sys_edges.items(), key=lambda x: -len(x[1]["files"])) if v["back"] and not v["uphill"]]) or "None."
top_rip = sorted(RT, key=lambda t: (-len(ripple[t]), -len(trans[t])))[:25]
blocks["ripple"] = md_table(
    ["Type", "System", "Kind", "Files using it", "Systems", "Types reached through others", "Tests", "Prefabs / scenes holding it"],
    [[tname(t), types[t]["sys"], types[t]["role"], len(ripple[t]), len(set(files[fi]["sys"] for fi in ripple[t])),
      len(trans[t]), len(tested_by.get(t, ())), sum(v for k, v in held_by.get(t, {}).items() if k != "asset")] for t in top_rip])
blocks["hubs"] = md_table(
    ["Static member", "Kind", "Runtime files", "Other systems"],
    [[f"{tname(h['t'])}.{h['m']}", h["k"], len(h["files"]), ", ".join(h["sys"])] for h in hubs if h["risk"]][:30]) or "None."
gods = sorted([t for t in RT if type_lines[t] >= 700 and types[t]["role"] not in ("enum", "interface")],
              key=lambda t: -type_lines[t])
blocks["gods"] = md_table(
    ["Class", "System", "Lines", "Other systems it uses", "Files using it"],
    [[tname(t), types[t]["sys"], type_lines[t], ", ".join(type_out_sys.get(t, [])) or "-", len(ripple[t])] for t in gods])
so_rows = sorted([t for t in RT if types[t]["role"] == "so"], key=lambda t: -len(so_assets.get(t, [])))
blocks["data"] = md_table(
    ["ScriptableObject class", "System", "Assets", "Read by (runtime files)", "Wired from prefabs / scenes / other assets"],
    [[tname(t), types[t]["sys"], len(so_assets.get(t, [])), len(ripple[t]),
      " / ".join(str(len(data_refs[t].get(k, ()))) for k in ("prefab", "scene", "asset"))] for t in so_rows])
blocks["smells"] = "\n".join([
    f"- **Gameplay numbers fixed in code:** {hc_total} in {len(hardcoded)} files. Most: "
    + ", ".join(f"`{files[fi]['p'].replace('Assets/_Game/', '')}` ({len(v)})" for fi, v in sorted(hardcoded.items(), key=lambda x: -len(x[1]))[:8]) + ".",
    f"- **Settings fallbacks** (`settings != null ? settings.x : 8`): {len(fallbacks)}.",
    f"- **Runtime writes to shared assets** (ScriptableObject or pipeline fields): {len(so_writes)} — "
    + ", ".join(f"`{files[fi]['p'].replace('Assets/_Game/', '')}:{n}`" for fi, n, _ in so_writes[:8]) + ".",
    f"- **Lookups by name** (Type.GetType, Find(\"...\"), Resources.Load, SendMessage): {len(string_lookups)}.",
    f"- **Asset entries that store a class by name and assembly** ([SerializeReference]): {managed_total} in "
    f"{len(set().union(*[v[1] for v in managed_types.values()])) if managed_types else 0} assets.",
    f"- **Static accessors that call GetComponent on every use:** {len(local_getcomp)}.",
    f"- **FindObject calls in runtime code:** {len(finds)}.",
])
EFFORT = {1: "small", 2: "medium", 3: "large"}
blocks["problems"] = md_table(
    ["#", "Kind", "Problem", "Systems", "Impact (1-5)", "Files a fix touches", "Effort"],
    [[p["id"], p["cat"], p["title"], ", ".join(p["sys"][:5]) + (" ..." if len(p["sys"]) > 5 else ""), p["impact"],
      p["touch"], EFFORT[p["effort"]]] for p in problems[:40]])

if os.path.exists(DOC):
    doc = read(DOC)
    for name, body in blocks.items():
        doc = re.sub(r"(<!-- map:%s -->\n)[\s\S]*?(<!-- /map:%s -->)" % (name, name),
                     lambda m: m.group(1) + body + "\n" + m.group(2), doc)
    with open(DOC, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(doc)

print(data["meta"]["files"], "runtime scripts,", data["meta"]["testFiles"], "test scripts,", data["meta"]["types"],
      "runtime types,", data["meta"]["so"], "ScriptableObject classes")
print(dict(asset_kind_count), "assets read;", sum(len(v) for v in so_assets.values()), "ScriptableObject assets")
print(len(sys_edges), "system links:", data["meta"]["uphill"], "uphill,", data["meta"]["twoway"], "two-way pairs,",
      data["meta"]["back"], "to cut for a clean stack")
print(f"against the compiler: {len(declared)} declared references between systems; {len(refused)} links in code no "
      f"declaration allows (the map misread them){': ' + ', '.join(f'{a} -> {b}' for a, b in refused) if refused else ''}; "
      f"{len(unseen)} declared with no type named in code: {', '.join(f'{a} -> {b}' for a, b in unseen) or 'none'}")
print(len(problems), "problems ranked; page:", rel(os.path.join(OUT_DIR, "dependency-map.html")))
sys.exit(status)
