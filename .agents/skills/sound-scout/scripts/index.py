"""Catalogues the sound library: measurements and a CLAP vector for every file. Resumable; run again after adding packs.

    python index.py [--library <folder>] [--limit N]
"""
import argparse
import json
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor

import numpy as np

import common
from common import RATE

MAX_SECONDS = 180      # of a long file, this much is read
WINDOW = 10 * RATE     # what the model hears at once


def db(value):
    return float(20 * np.log10(max(float(value), 1e-6)))


def envelope(audio, hop):
    usable = len(audio) // hop * hop
    return np.abs(audio[:usable]).reshape(-1, hop).max(axis=1) if usable else np.zeros(1, dtype=np.float32)


def measure(audio):
    """What can be said about a sound in numbers. Times in seconds unless named _ms; levels in dBFS."""
    n = len(audio)
    hop = RATE // 200  # 5 ms
    env = envelope(audio, hop)
    peak = float(env.max())
    if n == 0 or peak < 1e-5:
        return {"silent": True, "length": n / RATE}
    loud = np.nonzero(env >= peak * 0.01)[0]           # within 40 dB of the peak
    first, last = int(loud[0]), int(loud[-1])
    at = int(env.argmax())
    after = np.nonzero(env[at:] < peak * 0.1)[0]
    body = audio[first * hop:(last + 1) * hop]

    # Loudest 400 ms, the figure the bake levels to.
    win = min(len(body), int(RATE * 0.4))
    power = np.cumsum(np.concatenate(([0.0], body.astype(np.float64) ** 2)))
    short_term = float(np.sqrt((power[win:] - power[:-win]).max() / win)) if win > 0 and len(body) > win else float(np.sqrt(np.mean(body ** 2)))

    # Spectrum of the sounding part: where the energy sits, and how it moves.
    size, step = 2048, 512
    piece = body[:RATE * 30]
    if len(piece) < size:
        piece = np.pad(piece, (0, size - len(piece)))
    frames = np.lib.stride_tricks.sliding_window_view(piece, size)[::step] * np.hanning(size)
    spectrum = np.abs(np.fft.rfft(frames, axis=1))
    freqs = np.fft.rfftfreq(size, 1 / RATE)
    energy = spectrum.sum(axis=1) + 1e-9
    centroid = float(((spectrum * freqs).sum(axis=1) / energy * energy).sum() / energy.sum())
    total = spectrum.sum(axis=0)
    low = float(total[freqs < 250].sum() / (total.sum() + 1e-9))
    high = float(total[freqs > 4000].sum() / (total.sum() + 1e-9))

    # Onsets: frames where the spectrum jumps. One hit, or a run of them.
    flux = np.maximum(spectrum[1:] - spectrum[:-1], 0).sum(axis=1) if len(spectrum) > 1 else np.zeros(1)
    onsets = 0
    if flux.max() > 0:
        gate = max(flux.max() * 0.3, np.median(flux) * 4)
        above = flux > gate
        onsets = int(np.count_nonzero(above[1:] & ~above[:-1]) + (1 if above[0] else 0))

    # Pitch of the loudest tenth of a second, by autocorrelation; tone 1 = a pure note, 0 = noise.
    span = int(RATE * 0.1)
    centre = min(max(at * hop, span // 2), max(span // 2, n - span // 2))
    grain = audio[max(0, centre - span // 2):centre + span // 2].astype(np.float64)
    pitch, tone = 0.0, 0.0
    if len(grain) >= span // 2:
        grain = grain - grain.mean()
        corr = np.correlate(grain, grain, "full")[len(grain) - 1:]
        if corr[0] > 0:
            corr = corr / corr[0]
            lo, hi = RATE // 2000, min(RATE // 50, len(corr) - 1)
            if hi > lo:
                lag = lo + int(corr[lo:hi].argmax())
                pitch, tone = RATE / lag, float(max(0.0, corr[lag]))

    return {
        "length": round(n / RATE, 3),
        "sounding": round((last - first + 1) * hop / RATE, 3),
        "lead": round(first * hop / RATE, 3),
        "peak_db": round(db(peak), 1),
        "loud_db": round(db(short_term), 1),
        "rms_db": round(db(np.sqrt(np.mean(body ** 2))), 1),
        "centroid": round(centroid),
        "low": round(low, 3),
        "high": round(high, 3),
        "attack_ms": round((at - first) * hop / RATE * 1000),
        "decay_ms": round((int(after[0]) if len(after) else len(env) - at) * hop / RATE * 1000),
        "onsets": onsets,
        "pitch": round(pitch),
        "tone": round(tone, 2),
    }


def windows(audio):
    """The pieces the model hears: the whole of a short sound; of a long one, three spread through it."""
    if len(audio) <= WINDOW:
        return [audio if len(audio) >= RATE // 10 else np.pad(audio, (0, RATE // 10 - len(audio)))]
    if len(audio) <= 2 * WINDOW:
        power = np.cumsum(np.concatenate(([0.0], audio.astype(np.float64) ** 2)))
        start = int((power[WINDOW:] - power[:-WINDOW]).argmax())
        return [audio[start:start + WINDOW]]
    return [audio[int((len(audio) - WINDOW) * f):][:WINDOW] for f in (0.2, 0.5, 0.8)]


def read(path):
    try:
        audio = common.decode(path, seconds=MAX_SECONDS)
        row = measure(audio)
        row["path"] = common.relative(path)
        row["bytes"] = os.path.getsize(path)
        return row, (None if row.get("silent") else windows(audio))
    except Exception as error:  # a file ffmpeg cannot read is listed, not fatal
        return {"path": common.relative(path), "error": str(error)[:200]}, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", default=common.LIBRARY)
    parser.add_argument("--limit", type=int, default=0)
    args = parser.parse_args()
    common.LIBRARY = args.library
    os.makedirs(common.CACHE, exist_ok=True)

    rows, vectors = common.load_catalog()
    vectors = list(vectors)
    known = {row["path"] for row in rows}
    todo = [p for p in common.library_files(args.library) if common.relative(p) not in known]
    if args.limit:
        todo = todo[:args.limit]
    print(f"{len(known)} catalogued, {len(todo)} to do", flush=True)
    if not todo:
        return

    def save():
        with open(common.CATALOG + ".tmp", "w", encoding="utf-8") as f:
            for row in rows:
                f.write(json.dumps(row, ensure_ascii=False) + "\n")
        np.save(common.EMBEDDINGS + ".tmp.npy", np.array(vectors, dtype=np.float32).reshape(-1, 512))
        os.replace(common.CATALOG + ".tmp", common.CATALOG)
        os.replace(common.EMBEDDINGS + ".tmp.npy", common.EMBEDDINGS)

    common.clap()
    started, done = time.time(), 0
    with ThreadPoolExecutor(max_workers=6) as pool:
        # A dozen at a time: a decoded long file is tens of megabytes, and nothing may queue up behind the model.
        batches = (todo[i:i + 12] for i in range(0, len(todo), 12))
        for row, pieces in (result for batch in batches for result in pool.map(read, batch)):
            if pieces is None:
                vector = np.zeros(512, dtype=np.float32)
            else:
                vector = common.embed_audio(pieces).mean(axis=0)
                vector /= np.linalg.norm(vector) + 1e-9
            row["i"] = len(vectors)
            vectors.append(vector)
            rows.append(row)
            done += 1
            if done % 96 == 0:
                save()
                print(f"{done}/{len(todo)}  {time.time() - started:.0f}s", flush=True)
    save()
    failed = [row["path"] for row in rows if "error" in row]
    print(f"done: {len(rows)} files, {len(failed)} unreadable, {time.time() - started:.0f}s")
    for path in failed:
        print("  unreadable:", path)


if __name__ == "__main__":
    sys.exit(main())
