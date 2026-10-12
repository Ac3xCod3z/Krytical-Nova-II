"""Picks, from the tracks tempo.py found steady, the ones worth the owner's ear for a purpose.

    python music_shortlist.py [--want "intense battle music" ...] [--avoid "calm happy music" ...]
                              [--min-bpm 95 --max-bpm 160] [--count 28] [--per-pack 5] [--out shortlist.json]

Every steady track is scored against the descriptions with the same audio model the sound search uses (two
ten-second pieces of each, from a third and two thirds of the way in), and the best are kept, a few from each pack
so that no one pack fills the list. The model is weak on mood words: this only orders the list, and the list is
wide on purpose. The owner chooses. Scores are cached in Library/KryticalNova.SoundIndex/music/moods.npz.
"""
import argparse
import json
import os
import subprocess
import sys

import numpy as np

import common
import tempo

MOODS = os.path.join(tempo.OUT, "moods.npz")
DEFAULT_OUT = os.path.join(tempo.OUT, "shortlist.json")
WANT = ["intense action battle music with driving drums", "aggressive electronic combat music", "fast energetic fight scene soundtrack"]
AVOID = ["calm relaxing gentle music", "happy cheerful playful music"]


def piece(path, start, seconds=10.0):
    cmd = [common.ffmpeg(), "-nostdin", "-v", "error", "-ss", f"{start:.2f}", "-i", path, "-t", str(seconds),
           "-ac", "1", "-ar", str(common.RATE), "-f", "f32le", "-"]
    raw = subprocess.run(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def vectors(rows):
    """{path: unit vector}: the average of two pieces of each track. Cached; only new tracks are read."""
    known = {}
    if os.path.exists(MOODS):
        with np.load(MOODS, allow_pickle=False) as saved:
            known = dict(zip(saved["paths"].tolist(), saved["vectors"]))
    todo = [r for r in rows if r["path"] not in known]
    for n, row in enumerate(todo):
        path = os.path.join(tempo.MUSIC, row["path"])
        clips = [c for c in (piece(path, row["seconds"] * at) for at in (0.33, 0.66)) if len(c) > common.RATE]
        if not clips:
            continue
        v = common.embed_audio(clips).mean(axis=0)
        known[row["path"]] = v / (np.linalg.norm(v) + 1e-9)
        if (n + 1) % 20 == 0 or n + 1 == len(todo):
            print(f"heard {n + 1}/{len(todo)}", flush=True)
            np.savez(MOODS, paths=np.array(list(known.keys())), vectors=np.stack(list(known.values())))
    return known


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--want", action="append")
    p.add_argument("--avoid", action="append")
    p.add_argument("--min-bpm", type=float, default=95.0)
    p.add_argument("--max-bpm", type=float, default=160.0)
    p.add_argument("--max-drift", type=float, default=tempo.STEADY_MS)
    p.add_argument("--min-seconds", type=float, default=90.0)
    p.add_argument("--count", type=int, default=28)
    p.add_argument("--per-pack", type=int, default=5)
    p.add_argument("--alternates", action="store_true", help="also the other mixes and copies of a piece")
    p.add_argument("--out", default=DEFAULT_OUT)
    a = p.parse_args()

    # The packs hold other mixes of the same piece (no melody, minimal) and plain copies: one of each is enough to choose from.
    rows = [r for r in tempo.steady(list(tempo.load().values()), a.min_bpm, a.max_bpm, a.max_drift)
            if r["seconds"] >= a.min_seconds and (a.alternates or not any(part in r["path"] for part in ("/Alternates for", "filename-duplicates")))]
    heard = vectors(rows)
    want = common.embed_text(a.want or WANT).mean(axis=0)
    avoid = common.embed_text(a.avoid or AVOID).mean(axis=0)
    for row in rows:
        v = heard.get(row["path"])
        row["score"] = float(v @ want - v @ avoid) if v is not None else -9.0
    rows.sort(key=lambda r: -r["score"])

    chosen, per_pack = [], {}
    for row in rows:
        pack = row["path"].split("/")[0]
        if per_pack.get(pack, 0) >= a.per_pack:
            continue
        per_pack[pack] = per_pack.get(pack, 0) + 1
        chosen.append(row)
        if len(chosen) >= a.count:
            break

    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(chosen, f, indent=1)
    for row in chosen:
        print(f"{row['score']:+.3f}  {row['bpm']:7.2f} bpm  drift {row['drift_ms']:4.1f} ms  {row['seconds']:5.0f}s  {row['path']}")
    print(f"{len(chosen)} of {len(rows)} steady tracks -> {a.out}")


if __name__ == "__main__":
    sys.exit(main())
