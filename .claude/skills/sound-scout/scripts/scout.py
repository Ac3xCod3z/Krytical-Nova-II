"""Runs every slot's descriptions from slots.json against the catalogue and writes the shortlist the audition page shows.

    python scout.py [--only "Hit"] [--extra 4] [--sheets]

Per slot: the best matches within its measurements (twice what it wants, plus --extra), then any paths listed for it
in keep.json (sounds to carry over, marked "first pick" on the page). --sheets also draws one picture per slot.
A slot already in the shortlist is replaced only when named with --only, so hand edits to other slots survive.
"""
import argparse
import json
import os
import sys

import common
import find
import sheet

HERE = os.path.dirname(os.path.abspath(__file__))
SLOTS = os.path.join(HERE, "..", "slots.json")
SHORTLIST = os.path.join(common.CACHE, "shortlist.json")
KEEP = os.path.join(common.CACHE, "keep.json")
# Never a sound effect: music construction kits, instrument samples, spoken lines.
NOT_EFFECTS = ["Music Production Bundle", "Modern Music Kits", "DRUMKITS", "ABYSS - Dark Cinematic", "_Voice_", "Shooter Radio"]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--only", action="append", default=[])
    p.add_argument("--extra", type=int, default=3)
    p.add_argument("--sheets", action="store_true")
    a = p.parse_args()
    with open(SLOTS, encoding="utf-8") as f:
        slots = json.load(f)
    shortlist = json.load(open(SHORTLIST, encoding="utf-8")) if os.path.exists(SHORTLIST) else {}
    keep = json.load(open(KEEP, encoding="utf-8")) if os.path.exists(KEEP) else {}
    rows, vectors = common.load_catalog()
    by_path = {r["path"]: r for r in rows}

    for slot in slots:
        name = slot["name"]
        if a.only and name not in a.only:
            continue
        if not a.only and name in shortlist:
            continue
        loop = slot.get("loop")
        results = find.search(slot["queries"], avoid=slot.get("avoid", ()), top=slot["want"] * 2 + a.extra,
                              min_length=slot.get("min_length"), max_length=slot.get("max_length"),
                              min_centroid=slot.get("min_centroid"), max_centroid=slot.get("max_centroid"),
                              min_attack=slot.get("min_attack"), max_attack=slot.get("max_attack"),
                              max_onsets=slot.get("max_onsets"), exclude=NOT_EFFECTS,
                              spread=0.97 if loop else 0.93, rows=rows, vectors=vectors)
        entries = [dict(e if isinstance(e, dict) else {"path": e}, current=True) for e in keep.get(name, [])]
        entries = [e for e in entries if e["path"] in by_path]
        kept = {e["path"] for e in entries}
        entries += [{"path": r["path"], "score": round(score, 3)} for score, r in results if r["path"] not in kept]
        shortlist[name] = entries
        print(f"\n== {name}: {slot['purpose']}")
        find.show([(e.get("score", 0), by_path[e["path"]]) for e in entries])
        if a.sheets:
            seconds = None if loop else min(slot.get("max_seconds") or 99, max(by_path[e["path"]]["sounding"] for e in entries) + 0.1)
            try:
                sheet.draw([e["path"] for e in entries], os.path.join(common.CACHE, "sheets", name.replace(" ", "") + ".png"), seconds)
            except Exception as error:
                print("  no sheet:", error)
    with open(SHORTLIST, "w", encoding="utf-8") as f:
        json.dump(shortlist, f, indent=1)


if __name__ == "__main__":
    sys.exit(main())
