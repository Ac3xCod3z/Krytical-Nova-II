"""Builds the Encyclopedia of Nova page from the docs.

The source is Assets/_Game/_Docs/Reference/12Sky/01_Systems_Build_List.md (systems, What is left, open questions,
glossary, background, change log). Every other .md file under Assets/_Game/_Docs goes into the page's Library, and
every mention of a doc, a section of one ("gear spec section 4", "`Plans/hostiles-plan.md`, section 8") or a 12Sky
help page becomes a link that opens it there.

Usage (from the project folder):  python Assets/_Game/_Docs/Encyclopedia~/build_encyclopedia.py [out.html]
The page is written to Assets/_Game/_Docs/Encyclopedia~/out/encyclopedia.html unless told otherwise; publish that
file to the Encyclopedia of Nova artifact (see README.md beside this script).
"""
import datetime
import html
import json
import os
import re
import sys
from urllib.parse import quote

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS_ROOT = os.path.normpath(os.path.join(HERE, ".."))
SOURCE_ID = "Reference/12Sky/01_Systems_Build_List"
TRANSCRIPTION_ID = "Reference/12Sky/03_Full_Transcription"
GEAR_SPEC_ID = "Reference/12Sky/02_Gear_System_Spec"
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "out", "encyclopedia.html")


# --- the Library: every doc ------------------------------------------------------------------------------------------
def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n")


DOCS = {}
for folder, dirs, files in os.walk(DOCS_ROOT):
    dirs[:] = sorted(d for d in dirs if not d.endswith("~"))   # Unity-ignored folders: pictures, this tool
    for name in sorted(files):
        if not name.endswith(".md"):
            continue
        full = os.path.join(folder, name)
        doc_id = os.path.relpath(full, DOCS_ROOT).replace("\\", "/")[:-3]
        text = read(full)
        title = next((l[2:].strip() for l in text.split("\n") if l.startswith("# ")), name[:-3])
        top = doc_id.split("/")[0] if "/" in doc_id else "Project"
        DOCS[doc_id] = {"title": title, "path": "Assets/_Game/_Docs/" + doc_id + ".md", "cat": top, "md": text}

BY_BASE = {}
for doc_id in DOCS:
    base = doc_id.rsplit("/", 1)[-1].lower()
    BY_BASE[base] = None if base in BY_BASE else doc_id   # an ambiguous file name resolves to nothing
DESIGN_BY_NUMBER = {m.group(1): d for d in DOCS for m in [re.match(r"Design/(\d\d)_", d)] if m}


def resolve(ref):
    """A doc's id from a path as the docs write it (relative, from Assets/, or a bare file name), else None."""
    ref = ref.strip().split("#")[0].replace("\\", "/")
    if not ref.lower().endswith(".md"):
        return None
    ref = ref[:-3]
    while ref.startswith("../") or ref.startswith("./"):
        ref = ref.split("/", 1)[1]
    for prefix in ("Assets/_Game/_Docs/", "_Game/_Docs/", "_Docs/"):
        if ref.startswith(prefix):
            ref = ref[len(prefix):]
    if ref in DOCS:
        return ref
    for doc_id in DOCS:
        if doc_id.endswith("/" + ref):
            return doc_id
    return BY_BASE.get(ref.rsplit("/", 1)[-1].lower())


def doc_href(doc_id, kind=None, value=None):
    href = "#doc/" + quote(doc_id, safe="")
    if kind:
        href += "/" + kind + "/" + quote(str(value), safe="")
    return href


# --- inline markdown -------------------------------------------------------------------------------------------------
MAXROW = 0
TAG = re.compile(r"(<a\b.*?</a>|<[^>]+>|\x00\d+\x00)", re.S)


def outside_tags(text, fn):
    """Applies fn to the plain-text stretches of text: not inside a link, a tag or a code placeholder."""
    return "".join(part if TAG.fullmatch(part) else fn(part) for part in TAG.split(text))


def link_rows(text):
    def number(n):
        value = n.group(0)
        return '<a class="rl" href="#row-%s">%s</a>' % (value, value) if 0 < int(value) <= MAXROW else value

    return re.sub(r"\b([Rr]ows? )((?:\d+(?:, and |, | and | to )?)+)", lambda m: m.group(1) + re.sub(r"\d+", number, m.group(2)), text)


def link_bare_refs(text):
    def gear(m):
        out = '<a class="dl" href="%s">%s%s%s</a>' % (doc_href(GEAR_SPEC_ID, "sec", m.group(3)), m.group(1), m.group(2), m.group(3))
        if m.group(5):
            out += m.group(4)[:-len(m.group(5))] + '<a class="dl" href="%s">%s</a>' % (doc_href(GEAR_SPEC_ID, "sec", m.group(5)), m.group(5))
        return out

    text = re.sub(r"\b([Gg]ear spec)(,? sections? )(\d+[a-z]?)((?: and | / |, )(\d+[a-z]?))?", gear, text)
    text = re.sub(r"\b([Gg]ear spec)\b(?![^<]*</a>)", lambda m: '<a class="dl" href="%s">%s</a>' % (doc_href(GEAR_SPEC_ID), m.group(1)), text)

    def design(m):
        def one(number, label):
            d = DESIGN_BY_NUMBER.get(number)
            return '<a class="dl" href="%s">%s</a>' % (doc_href(d), label) if d else label
        out = one(m.group(2), m.group(1) + m.group(2))
        if m.group(4):
            out += m.group(3) + one(m.group(4), m.group(4))
        return out

    text = re.sub(r"\b([Dd]esigns? )(\d\d)\b(?:( / )(\d\d)\b)?", design, text)
    lore = DOCS.get("Design/Lore-Bible")
    if lore:
        text = re.sub(r"\b(Core Lore Bible|Lore Bible)\b", lambda m: '<a class="dl" href="%s">%s</a>' % (doc_href("Design/Lore-Bible"), m.group(1)), text)
    return text


SECTION_AFTER = re.compile(r"\x00(\d+)\x00(?:(,? sections? )(\d+[a-z]?(?:-\d+)?)\b|( )(\d+[a-z])\b|(,? )\"([^\"<]{2,60})\")?")


def inline(text):
    text = html.escape(text, quote=False)
    codes = []

    def keep(match):
        codes.append(match.group(1))
        return "\x00%d\x00" % (len(codes) - 1)

    text = re.sub(r"`([^`]+)`", keep, text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])", r"<em>\1</em>", text)
    text = outside_tags(text, link_rows)
    text = outside_tags(text, link_bare_refs)

    def restore(m):
        code = codes[int(m.group(1))]
        rest = m.group(0)[len("\x00%s\x00" % m.group(1)):]
        doc = resolve(html.unescape(code))
        if not doc:
            return "<code>" + code + "</code>" + rest
        if m.group(3):
            href = doc_href(doc, "sec", m.group(3))
        elif m.group(5):
            href = doc_href(doc, "sec", m.group(5))
        elif m.group(7):
            href = doc_href(doc, "name", m.group(7))
        else:
            href = doc_href(doc)
        return '<a class="dl" href="%s"><code>%s</code>%s</a>' % (href, code, rest)

    return SECTION_AFTER.sub(restore, text)


def plain(text):
    return re.sub(r"[`*]", "", text)


def attr(text):
    return html.escape(plain(text).lower(), quote=True)


def paras(text):
    """A long cell reads better broken where a bold lead-in starts a new thought."""
    return "".join("<p>%s</p>" % inline(part) for part in re.split(r"(?<=[.:)]) (?=\*\*[A-Z])", text) if part.strip())


def cells(line):
    out, cur, in_code = [], "", False
    for ch in line.strip():
        if ch == "`":
            in_code = not in_code
        if ch == "|" and not in_code:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    out.append(cur.strip())
    return out[1:-1]


def table(rows):
    out = ['<div class="tablewrap"><table><thead><tr>']
    out += ["<th>%s</th>" % inline(c) for c in cells(rows[0])]
    out.append("</tr></thead><tbody>")
    for row in rows[2:]:
        out.append("<tr>" + "".join("<td>%s</td>" % inline(c) for c in cells(row)) + "</tr>")
    out.append("</tbody></table></div>")
    return "".join(out)


def blocks(body):
    out, i = [], 0
    while i < len(body):
        line = body[i]
        if not line.strip():
            i += 1
        elif line.startswith("### "):
            out.append("<h3>%s</h3>" % inline(line[4:]))
            i += 1
        elif line.startswith("|"):
            j = i
            while j < len(body) and body[j].startswith("|"):
                j += 1
            out.append(table(body[i:j]))
            i = j
        elif line.startswith("- "):
            items = []
            while i < len(body) and body[i].startswith("- "):
                items.append("<li>%s</li>" % inline(body[i][2:]))
                i += 1
            out.append("<ul>%s</ul>" % "".join(items))
        elif re.match(r"^\d+\. ", line):
            items = []
            while i < len(body) and re.match(r"^\d+\. ", body[i]):
                items.append("<li>%s</li>" % inline(re.sub(r"^\d+\. ", "", body[i])))
                i += 1
            out.append("<ol>%s</ol>" % "".join(items))
        else:
            out.append("<p>%s</p>" % inline(line))
            i += 1
    return "\n".join(out)


def page_links(pages):
    """The 12Sky help pages a system cites, each a link to that page of the transcription."""
    if "not in" in pages or TRANSCRIPTION_ID not in DOCS:
        return inline(pages)
    return re.sub(r"\b([1-9]|[12]\d|3[01])\b(?!\s*%)",
                  lambda m: '<a class="dl" href="%s">%s</a>' % (doc_href(TRANSCRIPTION_ID, "page", m.group(1)), m.group(1)),
                  html.escape(pages, quote=False))


# --- the source document ---------------------------------------------------------------------------------------------
lines = DOCS[SOURCE_ID]["md"].split("\n")
sections, name = {}, "_top"
for line in lines:
    if line.startswith("## "):
        name = line[3:].strip()
        sections[name] = []
    else:
        sections.setdefault(name, []).append(line)


def key_of(prefix, required=True):
    for key in sections:
        if key.startswith(prefix):
            return key
    if required:
        raise SystemExit("missing section " + prefix)
    return None


master = sections[key_of("Master build list")]
systems = []
for row in [l for l in master if re.match(r"^\| \d+ \|", l)]:
    c = cells(row)
    if len(c) != 6:
        raise SystemExit("row with %d cells: %s" % (len(c), row[:80]))
    systems.append(c)
MAXROW = max(int(c[0]) for c in systems)

# Shown in the order a plan is read: what is under way, what is not started, ideas, then what is done.
STATUS = [("Partial", "partial", "Partly built", "started, not finished"),
          ("To build", "to-build", "To build", "designed, not started"),
          ("Candidate", "candidate", "Candidates", "ideas with no detailed design"),
          ("Complete", "complete", "Complete", "working in the game")]
known = {s[0] for s in STATUS}
for c in systems:
    if c[2] not in known:
        raise SystemExit("unknown status %r in row %s" % (c[2], c[0]))
counts = {s[0]: sum(1 for c in systems if c[2] == s[0]) for s in STATUS}


def strip(c):
    num, title, status, pages, built, left = c
    slug = next(s[1] for s in STATUS if s[0] == status)
    ref = ""
    at = title.find(" (")
    if at > 0 and not re.search(r"\)\s*\+", title[at:]):
        title, ref = title[:at], title[at + 1:]
    return (
        '<article class="strip" id="row-%s" data-text="%s">'
        '<button class="top" type="button" aria-expanded="false">'
        '<span class="num">%s</span>'
        '<span class="what"><span class="name">%s</span>%s<span class="peek"><b>Left:</b> %s</span></span>'
        '<span class="stamp %s">%s</span></button>'
        '<div class="build" hidden>'
        '<div class="blk todo"><h3>Left to build</h3><div class="clip" data-max="7.5">%s</div></div>'
        '<div class="blk"><h3>In the game today</h3><div class="clip" data-max="7.5">%s</div></div>'
        '<p class="tip">12Sky help pages: %s</p>'
        "</div></article>"
        % (num, attr(" ".join([num, title, ref, status, built, left])), num, inline(title),
           '<span class="ref">%s</span>' % html.escape(plain(ref), quote=False) if ref else "",
           html.escape(plain(left), quote=False), slug, status, paras(left), paras(built), page_links(pages)))


groups_html, score_html, nav_sys = [], [], []
for status, slug, label, note in STATUS:
    rows = [c for c in systems if c[2] == status]
    is_open = status in ("Partial", "To build")
    groups_html.append(
        '<section id="s-%s" class="grp sysgrp%s"><h2><button class="cat" type="button" aria-expanded="%s"><b class="%s">%s</b>'
        '<span class="n">%d</span><small>%s</small><span class="chev" aria-hidden="true">&#9660;</span></button></h2>'
        '<div class="body"%s>%s</div></section>'
        % (slug, " open" if is_open else "", "true" if is_open else "false", slug, label, len(rows), note,
           "" if is_open else " hidden", "\n".join(strip(c) for c in rows)))
    nav_sys.append('<a href="#s-%s">%s <span>%d</span></a>' % (slug, label, len(rows)))
for status, slug, label, note in sorted(STATUS, key=lambda s: ["Complete", "Partial", "To build", "Candidate"].index(s[0])):
    if counts[status]:
        score_html.append('<a class="seg %s" href="#s-%s" style="flex-grow:%d"><b>%d</b><span>%s</span></a>'
                          % (slug, slug, counts[status], counts[status], label))

# What is left: one panel per numbered point.
left_key = key_of("What is left")
intro, chunks, cur = [], [], None
for line in sections[left_key]:
    m = re.match(r"^\*\*(\d+)\. (.+?)\*\*\s*(.*)$", line)
    if m:
        cur = [m.group(1), m.group(2).rstrip(".:"), [m.group(3)] if m.group(3) else []]
        chunks.append(cur)
    elif cur:
        cur[2].append(line)
    else:
        intro.append(line)
plan_html = ['<div class="prose">%s</div>' % blocks(intro)]
for number, title, body in chunks:
    plan_html.append('<div class="plan"><div class="planhead"><span class="big">%s</span><h3>%s</h3></div><div class="prose clip" data-max="12">%s</div></div>'
                     % (number, inline(title), blocks(body)))
left_note = re.sub(r"^What is left\s*", "", left_key).strip("() ")

# Open questions.
open_items, done_items, open_intro = [], [], []
for line in sections[key_of("Assumptions and open questions")]:
    m = re.match(r"^- \[( |x)\] (.*)$", line)
    if m:
        (done_items if m.group(1) == "x" else open_items).append('<li><div class="clip" data-max="4.5">%s</div></li>' % inline(m.group(2)))
    elif line.strip():
        open_intro.append(line)
open_html = ('<div class="prose">%s</div><ul class="asks">%s</ul>'
             '<details class="fold"><summary>Settled: %d decisions already made</summary><ul class="asks done">%s</ul></details>'
             % (blocks(open_intro), "".join(open_items), len(done_items), "".join(done_items)))

# Glossary: every term with what it is for.
glossary = sections[key_of("Glossary")]
grows = [l for l in glossary if l.startswith("|")][2:]
gloss_intro = blocks([l for l in glossary if not l.startswith("|")])
by_group, order = {}, []
for row in grows:
    c = cells(row)
    if len(c) != 5:
        raise SystemExit("glossary row with %d cells (a term needs its 'What it is for'): %s" % (len(c), row[:80]))
    if not c[4].strip():
        raise SystemExit("glossary term with no 'What it is for': " + c[0])
    group = c[3].split(",")[0].strip()
    if group not in by_group:
        by_group[group] = []
        order.append(group)
    by_group[group].append(
        '<div class="term" data-text="%s"><div class="kn">%s</div><div class="use">%s</div><div class="ts">%s</div><div class="code">%s</div></div>'
        % (attr(" ".join(c)), inline(c[0]), inline(c[4]), inline(c[1]), inline(c[2]) if c[2] not in ("—", "-", "") else '<span class="none">not in code yet</span>'))
gchips = ['<button type="button" class="val on" data-g="all">All<small>%d</small></button>' % len(grows)]
gloss_html = []
for group in order:
    gchips.append('<button type="button" class="val" data-g="%s">%s<small>%d</small></button>' % (html.escape(group, quote=True), html.escape(group), len(by_group[group])))
    gloss_html.append('<div class="gg" data-g="%s"><h3 class="gh">%s</h3>%s</div>' % (html.escape(group, quote=True), html.escape(group), "\n".join(by_group[group])))

changelog_key = key_of("Change log", required=False)
changelog = blocks(sections[changelog_key]) if changelog_key else "<p>No change log yet.</p>"

# Library cards, by folder.
CATS = [("Reference", "Reference packs: the Twelve Sky cross-reference, the art breakdown"),
        ("Design", "Design: how each system is meant to work, the Lore Bible, the Rendering Bible"),
        ("Plans", "Plans: how a system was or will be built, step by step"),
        ("Project", "Project: how the repo is organised")]
library_html, nav_lib = [], 0
for cat, label in CATS:
    ids = [d for d in DOCS if DOCS[d]["cat"] == cat and d != SOURCE_ID]
    if not ids:
        continue
    cards = []
    for d in ids:
        doc = DOCS[d]
        first = next((l for l in doc["md"].split("\n") if l.strip() and not l.startswith(("#", "|", "!", "-", ">", "```"))), "")
        first = plain(re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", first))
        if len(first) > 190:
            first = first[:187].rsplit(" ", 1)[0] + "…"
        cards.append('<a class="doccard" href="%s" data-doc="%s"><b>%s</b><span class="dpath">%s · %d KB</span><span class="dsum">%s</span></a>'
                     % (doc_href(d), html.escape(d, quote=True), html.escape(doc["title"]), html.escape(doc["path"]),
                        max(1, len(doc["md"].encode("utf-8")) // 1024), html.escape(first)))
        nav_lib += 1
    library_html.append('<div class="libcat"><h3 class="gh">%s</h3><p class="tip">%s</p><div class="docgrid">%s</div></div>'
                        % (cat, html.escape(label.split(": ", 1)[1]), "\n".join(cards)))

payload = {d: {"title": DOCS[d]["title"], "path": DOCS[d]["path"], "md": DOCS[d]["md"]} for d in DOCS if d != SOURCE_ID}
docs_json = json.dumps(payload, ensure_ascii=False).replace("</", "<\\/")

template = read(os.path.join(HERE, "template.html"))
today = datetime.date.today()
fill = {
    "{{TOTAL}}": str(len(systems)),
    "{{SCORE}}": "".join(score_html),
    "{{NAV_SYS}}": "".join(nav_sys),
    "{{PLAN_COUNT}}": str(len(chunks)),
    "{{PLAN_NOTE}}": inline(left_note),
    "{{PLAN}}": "\n".join(plan_html),
    "{{GROUPS}}": "\n".join(groups_html),
    "{{OPEN_COUNT}}": str(len(open_items)),
    "{{OPEN}}": open_html,
    "{{GLOSS_COUNT}}": str(len(grows)),
    "{{GLOSS_INTRO}}": gloss_intro,
    "{{GCHIPS}}": "".join(gchips),
    "{{GLOSSARY}}": "\n".join(gloss_html),
    "{{VARIANCE}}": blocks(sections[key_of("Variance notes")]),
    "{{CANDIDATES}}": blocks(sections[key_of("Candidates not on the original list")]),
    "{{CHANGELOG}}": changelog,
    "{{LIB_COUNT}}": str(nav_lib),
    "{{LIBRARY}}": "\n".join(library_html),
    "{{DOCS_JSON}}": docs_json,
    "{{BUILT}}": "%d %s %d" % (today.day, today.strftime("%b"), today.year),
}
for key, value in fill.items():
    template = template.replace(key, value)
if "{{" in template.replace(docs_json, ""):
    raise SystemExit("unfilled placeholder")
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write(template)
links = len(re.findall(r'class="dl"', template))
print("%d systems %s | %d plan points | %d open, %d settled | %d terms in %d groups | %d docs in the Library, %d doc links | %d KB -> %s"
      % (len(systems), counts, len(chunks), len(open_items), len(done_items), len(grows), len(order), nav_lib, links,
         len(template.encode("utf-8")) // 1024, OUT))
