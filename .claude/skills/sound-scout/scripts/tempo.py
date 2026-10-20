"""Measures the beat of music tracks, for mechanics that are timed to music (Cadence).

    python tempo.py scan [--workers 3] [--root <folder>]   every track under the music library; resumable
    python tempo.py show <file> [<file> ...]               one track, measured now, in full
    python tempo.py list [--min-bpm 90 --max-bpm 150 --max-drift 15 --under <text>]   the steady ones, steadiest first
    python tempo.py selftest                               made-up tracks with a known beat: is the measuring right?

What is measured, per track: beats a minute; the time of the first beat; which beat is the first of the bar
(a guess); how far the beat wanders from an even grid over the track (drift, in ms: a live drummer or a tempo
change shows here); and how much of the track has a clear beat at all. A mechanic needs a track with small
drift: the game keeps time by arithmetic, not by listening.

The music library is My Drive/KryticalNova/Audio/Music in Google Drive (before 20 Oct 2026 ~/Downloads/Music; env KN_MUSIC_LIBRARY); it is only read. Results go to
Library/KryticalNova.SoundIndex/music/tempo.jsonl. Claude cannot hear: the owner checks a measured beat by ear,
with a click on every beat (the music audition page, and gm.music.click in the game).
"""
import argparse
import json
import os
import sys
from concurrent.futures import ProcessPoolExecutor, as_completed

import numpy as np

import common

MUSIC = common.in_drive_or_downloads("KN_MUSIC_LIBRARY", ("Audio", "Music"), "Music")
OUT = os.path.join(common.CACHE, "music")
TEMPO = os.path.join(OUT, "tempo.jsonl")

RATE = 22050
N_FFT = 512                 # 23 ms: short, so that an onset is placed to a few ms
HOP = 64                    # 2.9 ms between frames
ENV_RATE = RATE / HOP
LOW_HZ = 200.0              # under this is kick and bass
MIN_BPM, MAX_BPM = 60.0, 200.0
HOME_LOW, HOME_HIGH = 80.0, 160.0   # a beat a player can keep: slower or faster readings are doubled or halved into this
SPECTRUM_SIZE = 1 << 22     # 0.005 beats a minute between points
FOLD_BINS = 256
CLEAR = 2.0                 # a stretch has a clear beat when the folded peak stands this far over the average
WINDOW_BEATS, STEP_BEATS = 32, 16
SHORTEST, LONGEST = 45.0, 720.0     # seconds: under this it is a sting, over this only the start is read
STEADY_MS = 15.0


def _band_edges():
    edges = np.unique(np.round(np.geomspace(1, N_FFT // 2 + 1, 33)).astype(int))
    return edges[:-1]       # reduceat sums from each index to the next, the last to the end


def envelopes(audio):
    """(all, low): how much new sound arrives in each frame. Frame n is centred on sample n * HOP."""
    peak = float(np.max(np.abs(audio))) if len(audio) else 0.0
    if peak > 0:
        audio = audio / peak
    padded = np.concatenate([np.zeros(N_FFT // 2, np.float32), audio.astype(np.float32), np.zeros(N_FFT, np.float32)])
    frames = np.lib.stride_tricks.sliding_window_view(padded, N_FFT)[::HOP]
    window = np.hanning(N_FFT).astype(np.float32)
    starts = _band_edges()
    low_bands = int(np.sum(starts * RATE / N_FFT < LOW_HZ))
    bands = np.empty((len(frames), len(starts)), np.float32)
    for at in range(0, len(frames), 8192):
        magnitude = np.abs(np.fft.rfft(frames[at:at + 8192] * window, axis=1)).astype(np.float32)
        bands[at:at + 8192] = np.add.reduceat(magnitude, starts, axis=1)
    loud = np.log1p(bands)
    rise = np.maximum(0.0, np.diff(loud, axis=0, prepend=loud[:1]))
    return _flatten(rise.sum(axis=1)), _flatten(rise[:, :low_bands].sum(axis=1))


def _flatten(env):
    """Takes the one-second running average off, so a loud passage counts no more than a quiet one."""
    width = int(ENV_RATE)
    total = np.cumsum(np.concatenate([[0.0], env.astype(np.float64)]))
    left = np.clip(np.arange(len(env)) - width // 2, 0, len(env))
    right = np.clip(np.arange(len(env)) + width // 2, 0, len(env))
    mean = (total[right] - total[left]) / np.maximum(1, right - left)
    return np.maximum(0.0, env - mean).astype(np.float32)


class Spectrum:
    """How strongly the envelope repeats at each rate."""

    def __init__(self, env):
        self.magnitude = np.abs(np.fft.rfft(env - env.mean(), SPECTRUM_SIZE))
        self.step = ENV_RATE / SPECTRUM_SIZE     # Hz between points

    def at(self, hz):
        x = np.asarray(hz, dtype=np.float64) / self.step
        i = np.floor(x).astype(np.int64)
        part = x - i
        return self.magnitude[i] * (1 - part) + self.magnitude[i + 1] * part


def rough_tempo(spectrum):
    """(bpm, as first read): the rate that explains the track best, brought into the range a player can keep."""
    bpm = np.arange(MIN_BPM, MAX_BPM, 0.02)
    hz = bpm / 60.0
    score = spectrum.at(hz) + 0.5 * spectrum.at(2 * hz) + 0.25 * spectrum.at(4 * hz)
    likely = np.exp(-0.5 * (np.log2(bpm / 120.0) / 0.7) ** 2)
    read = float(bpm[np.argmax(score * likely)])
    home = read
    while home < HOME_LOW:
        home *= 2
    while home >= HOME_HIGH:
        home /= 2
    return home, read


def fine_tempo(spectrum, bpm, reach=0.4):
    """The same, to a hundredth: the faster repeats (halves, quarters of the beat) place it more finely."""
    near = np.arange(bpm - reach, bpm + reach, 0.001)
    hz = near / 60.0
    score = spectrum.at(hz) + spectrum.at(2 * hz) + spectrum.at(4 * hz)
    return float(near[np.argmax(score)])


def fold(env, first_index, period):
    """The envelope laid over itself beat on beat: where in the beat the onsets fall. Smoothed over about 5 ms."""
    t = (np.arange(len(env)) + first_index) / ENV_RATE
    x = (t / period % 1.0) * FOLD_BINS
    i = np.floor(x).astype(np.int64) % FOLD_BINS
    part = x - np.floor(x)
    folded = np.bincount(i, env * (1 - part), FOLD_BINS) + np.bincount((i + 1) % FOLD_BINS, env * part, FOLD_BINS)
    sigma = max(0.5, 0.005 / period * FOLD_BINS)
    reach = np.minimum(np.arange(FOLD_BINS), FOLD_BINS - np.arange(FOLD_BINS))
    kernel = np.exp(-0.5 * (reach / sigma) ** 2)
    return np.real(np.fft.ifft(np.fft.fft(folded) * np.fft.fft(kernel / kernel.sum())))


def peak_of(folded):
    """(where in the beat, 0 to 1; how far the peak stands over the average)."""
    k = int(np.argmax(folded))
    before, here, after = folded[k - 1], folded[k], folded[(k + 1) % FOLD_BINS]
    bend = before - 2 * here + after
    shift = 0.5 * (before - after) / bend if bend < 0 else 0.0
    return ((k + shift) / FOLD_BINS) % 1.0, float(here / (folded.mean() + 1e-9))


def wrap(x):
    return (x + 0.5) % 1.0 - 0.5


def beat_grid(env, period):
    """The whole track's beat place, and stretch by stretch how far the beat there sits from it (seconds)."""
    place, strength = peak_of(fold(env, 0, period))
    window = int(WINDOW_BEATS * period * ENV_RATE)
    step = int(STEP_BEATS * period * ENV_RATE)
    times, off, clear = [], [], []
    for start in range(0, max(1, len(env) - window + 1), max(1, step)):
        here, stands = peak_of(fold(env[start:start + window], start, period))
        times.append((start + window / 2) / ENV_RATE)
        off.append(wrap(here - place) * period)
        clear.append(stands >= CLEAR)
    return place, strength, np.array(times), np.array(off), np.array(clear, dtype=bool)


def settle(env, period):
    """Corrects the period by the slope of the stretches' offsets: a beat that falls later and later is a longer period."""
    for _ in range(3):
        _, _, times, off, clear = beat_grid(env, period)
        if clear.sum() < 4:
            break
        slope = np.polyfit(times[clear], off[clear], 1)[0]
        period *= 1.0 + slope
        if abs(slope) < 2e-7:
            break
    return period


def drift_of(off, clear):
    """(worst, nine in ten) distance of a clear stretch's beat from the grid, in ms."""
    if not clear.any():
        return 999.0, 999.0
    far = np.abs(off[clear] - np.median(off[clear])) * 1000.0
    return float(far.max()), float(np.percentile(far, 90))


_bias = None


def bias():
    """
    How far ahead of a sound's start this method places it (the window sees an onset before its centre reaches it).
    Measured once on made-up drums and taken off every reading.
    """
    global _bias
    if _bias is None:
        _bias = 0.0
        audio = synth(120.0, 0.25, 40.0, noise=0.0)
        env, low = envelopes(audio)
        place, _ = peak_of(fold(env + 2 * low, 0, 0.5))
        _bias = float(wrap(place - 0.25 / 0.5) * 0.5)
    return _bias


def measure(audio):
    """Everything about one track's beat, as a dict. audio: mono float32 at RATE."""
    seconds = len(audio) / RATE
    out = {"seconds": round(seconds, 2)}
    if seconds < SHORTEST:
        out["skipped"] = "short"
        return out
    level = float(np.sqrt(np.mean(audio.astype(np.float64) ** 2)))
    out["rms_db"] = round(20 * np.log10(max(level, 1e-9)), 1)
    out["peak_db"] = round(20 * np.log10(max(float(np.max(np.abs(audio))), 1e-9)), 1)

    env_all, env_low = envelopes(audio)
    env = env_all + 2 * env_low         # kick and bass say where the beat is more surely than hats do
    spectrum = Spectrum(env)
    home, read = rough_tempo(spectrum)
    period = settle(env, 60.0 / fine_tempo(spectrum, home))

    # Produced music is nearly always a whole number of beats a minute: take it when it fits as well.
    place, strength, times, off, clear = beat_grid(env, period)
    worst, most = drift_of(off, clear)
    whole = round(60.0 / period)
    if whole and abs(60.0 / period - whole) < 0.06:
        w_place, w_strength, w_times, w_off, w_clear = beat_grid(env, 60.0 / whole)
        w_worst, w_most = drift_of(w_off, w_clear)
        if w_worst <= worst + 3.0:
            period, place, strength, off, clear, worst, most = 60.0 / whole, w_place, w_strength, w_off, w_clear, w_worst, w_most

    first = (place * period - bias()) % period
    beats = np.arange(first, seconds, period)
    out.update({
        "bpm": round(60.0 / period, 3),
        "read_bpm": round(read, 2),
        "first_beat": round(float(first), 4),
        "drift_ms": round(worst, 1),
        "drift90_ms": round(most, 1),
        "clear": round(float(clear.mean()) if len(clear) else 0.0, 2),
        "strength": round(strength, 2),
    })
    out.update(bar_guess(env_all, env_low, beats))
    return out


def bar_guess(env_all, env_low, beats, per_bar=4):
    """
    Which beat is the first of the bar. Two readings: the beat that carries the most low sound, when one of the
    four clearly does; otherwise the beat the track comes in on. A guess either way: the owner's ear decides.
    """
    reach = int(0.025 * ENV_RATE)

    def strength(env):
        index = np.round(beats * ENV_RATE).astype(np.int64)
        index = index[(index >= reach) & (index < len(env) - reach)]
        if not len(index):
            return np.zeros(0)
        return np.array([env[i - reach:i + reach + 1].max() for i in index])

    low, everything = strength(env_low), strength(env_all)
    if len(low) < per_bar * 4:
        return {"bar_beat": 0, "bar_from": "none"}
    by_place = np.array([low[k::per_bar].mean() for k in range(per_bar)])
    order = np.argsort(by_place)[::-1]
    if by_place[order[0]] > 1.25 * by_place[order[1]]:
        return {"bar_beat": int(order[0]), "bar_from": "accent"}
    floor = 0.5 * np.median(everything)
    for k in range(len(everything) - 1):
        if everything[k] >= floor and everything[k + 1] >= floor:
            return {"bar_beat": int(k % per_bar), "bar_from": "entry"}
    return {"bar_beat": 0, "bar_from": "none"}


def music_files(root=None):
    return common.library_files(root or MUSIC)


def relative(path, root=None):
    return os.path.relpath(path, root or MUSIC).replace("\\", "/")


def measure_file(path, root=None):
    stat = os.stat(path)
    row = {"path": relative(path, root), "size": stat.st_size, "mtime": int(stat.st_mtime)}
    try:
        row.update(measure(common.decode(path, seconds=LONGEST, rate=RATE)))
    except Exception as error:      # one unreadable file must not stop a scan of hundreds
        row["error"] = f"{type(error).__name__}: {error}"[:300]
    return row


def load():
    """{relative path: row}, the latest reading of each."""
    rows = {}
    if os.path.exists(TEMPO):
        with open(TEMPO, encoding="utf-8") as f:
            for line in f:
                if line.strip():
                    row = json.loads(line)
                    rows[row["path"]] = row
    return rows


def scan(root, workers):
    os.makedirs(OUT, exist_ok=True)
    known = load()
    todo = []
    for path in music_files(root):
        stat = os.stat(path)
        old = known.get(relative(path, root))
        if not old or old.get("size") != stat.st_size or old.get("mtime") != int(stat.st_mtime) or "error" in old:
            todo.append(path)
    print(f"{len(known)} measured already, {len(todo)} to measure", flush=True)
    bias()      # measured here so a failure shows before the workers start
    done = 0
    with open(TEMPO, "a", encoding="utf-8") as out, ProcessPoolExecutor(max_workers=workers) as pool:
        for job in as_completed([pool.submit(measure_file, path, root) for path in todo]):
            row = job.result()
            out.write(json.dumps(row) + "\n")
            out.flush()
            done += 1
            if done % 20 == 0 or done == len(todo):
                print(f"{done}/{len(todo)}", flush=True)
    print("done:", TEMPO)


def steady(rows, min_bpm=0.0, max_bpm=999.0, max_drift=STEADY_MS, min_clear=0.6, under=None):
    keep = [r for r in rows if "bpm" in r and min_bpm <= r["bpm"] <= max_bpm and r["drift_ms"] <= max_drift
            and r["clear"] >= min_clear and (not under or under.lower() in r["path"].lower())]
    return sorted(keep, key=lambda r: (r["drift_ms"], -r["strength"]))


def line(row):
    if "error" in row:
        return f"  error  {row['path']}: {row['error']}"
    if "bpm" not in row:
        return f"  {row.get('skipped', '?'):>6} {row['seconds']:6.1f}s  {row['path']}"
    return (f"{row['bpm']:8.3f} bpm  first {row['first_beat']:6.3f}s  bar {row['bar_beat']} ({row['bar_from']:6})  "
            f"drift {row['drift_ms']:6.1f} ms  clear {row['clear']:.2f}  strength {row['strength']:5.2f}  "
            f"{row['seconds']:6.1f}s  {row['path']}")


def synth(bpm, first, seconds, noise=0.02, drift=0.0, seed=7):
    """Made-up drums with a known beat: kick on every beat, snare on 2 and 4, a hat on every half beat."""
    rng = np.random.default_rng(seed)
    audio = (rng.standard_normal(int(seconds * RATE)) * noise).astype(np.float32)
    period = 60.0 / bpm

    def put(sound, at):
        i = int(round(at * RATE))
        if 0 <= i < len(audio) - len(sound):
            audio[i:i + len(sound)] += sound

    t = np.arange(int(0.15 * RATE)) / RATE
    kick = (np.sin(2 * np.pi * (50 * t + 70 * (1 - np.exp(-t * 30)) / 30)) * np.exp(-t * 22)).astype(np.float32)  # 120 Hz falling to 50
    t = np.arange(int(0.12 * RATE)) / RATE
    snare = (rng.standard_normal(len(t)) * np.exp(-t * 30) * 0.5).astype(np.float32)
    t = np.arange(int(0.03 * RATE)) / RATE
    hat = (rng.standard_normal(len(t)) * np.exp(-t * 150) * 0.2).astype(np.float32)
    beat = 0
    at = first
    while at < seconds:
        put(kick, at)
        if beat % 2 == 1:
            put(snare, at)
        put(hat, at)
        put(hat, at + period / 2)
        beat += 1
        at = first + beat * period * (1.0 + drift * beat)
    return audio


def selftest():
    print(f"bias taken off every reading: {bias() * 1000:.1f} ms")
    ok = True
    for bpm, first, drift in ((120.0, 0.137, 0.0), (128.0, 0.300, 0.0), (93.0, 0.051, 0.0), (140.0, 0.222, 0.0),
                             (174.0, 0.100, 0.0), (126.37, 0.180, 0.0), (120.0, 0.137, 0.00004)):
        row = measure(synth(bpm, first, 150.0, drift=drift))
        home = bpm
        while home >= HOME_HIGH:
            home /= 2
        period = 60.0 / bpm     # a halved reading may sit on either set of the made beats
        miss_bpm = abs(row["bpm"] - home)
        miss_first = abs(wrap((row["first_beat"] - first) / period) * period) * 1000
        wandering = drift > 0
        good = (row["drift_ms"] > STEADY_MS) if wandering else (miss_bpm < 0.01 and miss_first < 5.0 and row["drift_ms"] <= STEADY_MS)
        ok &= good
        print(f"{'ok ' if good else 'BAD'} made {bpm:7.2f} first {first:.3f}{' (speeds down)' if wandering else ''}: "
              f"read {row['bpm']:8.3f} (off {miss_bpm:.3f}), first beat off {miss_first:4.1f} ms, drift {row['drift_ms']:5.1f} ms, "
              f"clear {row['clear']:.2f}, strength {row['strength']:.1f}")
    print("selftest passed" if ok else "selftest FAILED")
    return 0 if ok else 1


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="what", required=True)
    s = sub.add_parser("scan")
    s.add_argument("--root", default=MUSIC)
    s.add_argument("--workers", type=int, default=3)
    s = sub.add_parser("show")
    s.add_argument("files", nargs="+")
    s = sub.add_parser("list")
    s.add_argument("--min-bpm", type=float, default=0.0)
    s.add_argument("--max-bpm", type=float, default=999.0)
    s.add_argument("--max-drift", type=float, default=STEADY_MS)
    s.add_argument("--min-clear", type=float, default=0.6)
    s.add_argument("--under")
    s.add_argument("--all", action="store_true", help="every reading, not only the steady ones")
    sub.add_parser("selftest")
    a = p.parse_args()
    if a.what == "scan":
        scan(a.root, a.workers)
    elif a.what == "show":
        for file in a.files:
            path = file if os.path.isabs(file) else os.path.join(MUSIC, file)
            print(line(measure_file(path)))
    elif a.what == "list":
        rows = list(load().values())
        chosen = sorted(rows, key=lambda r: r["path"]) if a.all else steady(rows, a.min_bpm, a.max_bpm, a.max_drift, a.min_clear, a.under)
        for row in chosen:
            print(line(row))
        print(f"{len(chosen)} of {len(rows)}")
    else:
        return selftest()
    return 0


if __name__ == "__main__":
    sys.exit(main())
