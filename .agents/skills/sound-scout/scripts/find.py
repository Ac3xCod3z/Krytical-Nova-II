"""Searches the catalogued library by description, by likeness to a file, and by measurements.

    python find.py "short sharp sword slash" --max-length 0.6 --top 12
    python find.py --like "pack/folder/file.wav" --top 8
    python find.py "rain" "heavy rain on a roof" --min-length 20 --json rain.json

Several descriptions are averaged into one query. --not "speech" pushes results away from a description.
"""
import argparse
import json
import sys

import numpy as np

import common


def search(texts=(), like=None, avoid=(), top=12, min_length=None, max_length=None, min_centroid=None, max_centroid=None,
           max_attack=None, min_attack=None, max_onsets=None, min_tone=None, max_tone=None, under=(), exclude=(),
           spread=0.93, rows=None, vectors=None):
    """The best matches as (score, row), most alike first, no two of them near-identical."""
    if rows is None:
        rows, vectors = common.load_catalog()
    query = np.zeros(vectors.shape[1], dtype=np.float32)
    if texts:
        query += common.embed_text(list(texts)).mean(axis=0)
    if like:
        key = common.relative(common.absolute(like))
        match = [r for r in rows if r["path"] == key]
        if not match:
            raise SystemExit("not in the catalogue: " + key)
        query += vectors[match[0]["i"]]
    if avoid:
        query -= 0.5 * common.embed_text(list(avoid)).mean(axis=0)
    norm = np.linalg.norm(query)
    scores = vectors @ (query / norm) if norm > 0 else np.zeros(len(vectors), dtype=np.float32)

    def keep(r):
        if "error" in r or r.get("silent"):
            return False
        path = r["path"].lower()
        if under and not any(u.lower() in path for u in under):
            return False
        if any(e.lower() in path for e in exclude):
            return False
        s = r["sounding"]
        return ((min_length is None or s >= min_length) and (max_length is None or s <= max_length) and
                (min_centroid is None or r["centroid"] >= min_centroid) and (max_centroid is None or r["centroid"] <= max_centroid) and
                (max_attack is None or r["attack_ms"] <= max_attack) and (min_attack is None or r["attack_ms"] >= min_attack) and
                (max_onsets is None or r["onsets"] <= max_onsets) and
                (min_tone is None or r["tone"] >= min_tone) and (max_tone is None or r["tone"] <= max_tone))

    picked = []
    for i in np.argsort(-scores):
        row = rows[int(i)]
        if not keep(row):
            continue
        if any(float(vectors[row["i"]] @ vectors[other["i"]]) > spread for _, other in picked):
            continue
        picked.append((float(scores[row["i"]]), row))
        if len(picked) >= top:
            break
    return picked


def show(results):
    print(f"{'score':>5} {'len':>6} {'cent':>5} {'att':>5} {'dec':>5} {'ons':>3} {'pitch':>5} {'tone':>4} {'loud':>5}  path")
    for score, r in results:
        print(f"{score:5.2f} {r['sounding']:6.2f} {r['centroid']:5d} {r['attack_ms']:5d} {r['decay_ms']:5d} {r['onsets']:3d} "
              f"{r['pitch']:5d} {r['tone']:4.2f} {r['loud_db']:5.1f}  {r['path']}")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("text", nargs="*")
    p.add_argument("--like")
    p.add_argument("--not", dest="avoid", action="append", default=[])
    p.add_argument("--top", type=int, default=12)
    p.add_argument("--min-length", type=float)
    p.add_argument("--max-length", type=float)
    p.add_argument("--min-centroid", type=float)
    p.add_argument("--max-centroid", type=float)
    p.add_argument("--min-attack", type=float)
    p.add_argument("--max-attack", type=float)
    p.add_argument("--max-onsets", type=int)
    p.add_argument("--min-tone", type=float)
    p.add_argument("--max-tone", type=float)
    p.add_argument("--under", action="append", default=[], help="only paths containing this")
    p.add_argument("--exclude", action="append", default=[], help="no paths containing this")
    p.add_argument("--spread", type=float, default=0.93, help="how alike two results may be (1 = identical allowed)")
    p.add_argument("--json", help="also write the result paths to this file")
    a = p.parse_args()
    if not a.text and not a.like:
        p.error("a description or --like is needed")
    results = search(a.text, a.like, a.avoid, a.top, a.min_length, a.max_length, a.min_centroid, a.max_centroid,
                     a.max_attack, a.min_attack, a.max_onsets, a.min_tone, a.max_tone, a.under, a.exclude, a.spread)
    show(results)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump([r["path"] for _, r in results], f, indent=1)


if __name__ == "__main__":
    sys.exit(main())
