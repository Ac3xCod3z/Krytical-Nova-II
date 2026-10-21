"""Puts the owner's approved music into the game: levels it, makes fight music steady, writes Ogg files and what the
game needs to know of each into the sound-scout skill's music.json.

    (sound-scout venv) python bake.py <plan.json> [--dry-run]

The plan names each piece: its game name, title, source file in Drive (KN-Originals), and its role:
  fight   a Cadence track: the longest stretch with a steady beat is kept (cut on beats, short fades), then the beat
          is measured again; a track whose kept stretch is shorter than --min-fight seconds is left out
  menu    title, outro, selection or faction coat: played whole, looped by the menu music player
  explore a zone's walking-about music: played whole, round and round
and the zones (ZoneMusic) or menu slots it goes to. Every file is levelled to the same loudness, so no track is
louder than the next. Then, in the Editor: Krytical Nova > Setup > Music.
"""
import argparse, json, os, subprocess, sys

import numpy as np

SCOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "sound-scout", "scripts")
sys.path.insert(0, os.path.abspath(SCOUT))
import common  # noqa: E402
import tempo   # noqa: E402

TARGET = os.path.join(common.PROJECT, "Assets", "_Game", "Audio", "Music")
MANIFEST = os.path.join(SCOUT, "..", "music.json")
LUFS = -16.0          # every piece the same loudness; the mixer's Music group and the track volume do the rest
CEILING = -1.0        # dBFS peak after levelling
EDGE_FADE = 0.03      # seconds of fade where a fight track is cut, so the cut does not click
RATE = 44100          # what the earlier music used; long 48 kHz Vorbis files failed Unity's FMOD import (21 Oct 2026)


def measure_lufs(path):
    err = subprocess.run([common.ffmpeg(), "-nostdin", "-hide_banner", "-i", path, "-af", "ebur128", "-f", "null", "-"],
                         capture_output=True, text=True, errors="replace").stderr
    tail = err[err.rfind("Summary:"):]
    import re
    m = re.search(r"I:\s+(-?[\d.]+) LUFS", tail)
    return float(m.group(1)) if m else None


def steady_stretch(path):
    """(start, end, bpm) of the longest run of the track whose beat sits on one grid, in seconds of the file."""
    audio = common.decode(path, rate=tempo.RATE)
    m = tempo.measure(audio)
    period = 60.0 / m["bpm"]
    env_all, env_low = tempo.envelopes(audio)
    env = env_all + 2 * env_low
    _, _, times, off, clear = tempo.beat_grid(env, period)
    half = tempo.WINDOW_BEATS * period / 2
    best, run = (0, -1), []
    for i in range(len(times)):
        ok = clear[i] and (not run or abs(tempo.wrap((off[i] - off[run[0]]) / period)) * period * 1000 <= 12.0)
        if ok:
            run.append(i)
        else:
            run = [i] if clear[i] else []
        if run and (run[-1] - run[0]) > (best[1] - best[0]):
            best = (run[0], run[-1])
    if best[1] < best[0]:
        return 0.0, len(audio) / tempo.RATE, m["bpm"]
    start = max(0.0, times[best[0]] - half)
    end = min(len(audio) / tempo.RATE, times[best[1]] + half)
    return start, end, m["bpm"]


def render(src, dst, gain_db, start=None, end=None):
    af = [f"volume={gain_db:.2f}dB", f"alimiter=limit={10 ** (CEILING / 20):.4f}:level=disabled"]
    cmd = [common.ffmpeg(), "-nostdin", "-v", "error", "-y"]
    if start is not None:
        cmd += ["-ss", f"{start:.4f}", "-to", f"{end:.4f}"]
        af.append(f"afade=t=in:d={EDGE_FADE}")
        af.append(f"afade=t=out:st={max(0.0, end - start - EDGE_FADE):.4f}:d={EDGE_FADE}")
    # -vn and no tags: Flow's WAVs carry cover art, which ffmpeg would add to the .ogg as a picture stream that
    # Unity's FMOD cannot import (21 Oct 2026: "FSBTool ERROR: Internal error from FMOD sub-system").
    cmd += ["-i", src, "-vn", "-map_metadata", "-1", "-ac", "2", "-ar", str(RATE), "-af", ",".join(af), "-c:a", "libvorbis", "-q:a", "5", dst]
    subprocess.run(cmd, check=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("plan")
    ap.add_argument("--min-fight", type=float, default=60.0)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    plan = json.load(open(a.plan, encoding="utf-8"))
    manifest = json.load(open(MANIFEST, encoding="utf-8")) if os.path.exists(MANIFEST) else {"tracks": [], "zones": []}
    os.makedirs(TARGET, exist_ok=True)

    baked, left_out = [], []
    for item in plan["pieces"]:
        src = item["source"]
        name = item["name"]
        dst = os.path.join(TARGET, f"MUS_{name}.ogg")
        lufs = measure_lufs(src)
        gain = LUFS - lufs if lufs is not None else 0.0
        start = end = None
        if item["role"] == "fight":
            start, end, bpm = steady_stretch(src)
            if end - start < a.min_fight:
                left_out.append(f"{name}: steady stretch only {end - start:.0f} s")
                continue
        if a.dry_run:
            print(f"{name:<24} {item['role']:<7} gain {gain:+.1f} dB" + (f"  keep {start:.1f}-{end:.1f} s ({end - start:.0f} s)" if start is not None else ""))
            continue
        render(src, dst, gain, start, end)
        row = {"name": name, "title": item["title"], "file": os.path.basename(dst), "role": item["role"],
               "source": os.path.relpath(src, os.path.dirname(os.path.dirname(src))).replace("\\", "/"),
               "gain_db": round(gain, 1)}
        if item["role"] == "fight":
            beat = tempo.measure(common.decode(dst, rate=tempo.RATE))
            if beat.get("drift_ms", 999) > tempo.STEADY_MS:
                left_out.append(f"{name}: still drifts {beat.get('drift_ms')} ms after the cut")
                os.remove(dst)
                continue
            bpm, bar = beat["bpm"], int(beat.get("bar_beat", 0) or 0)
            hint = item.get("bpm")
            if hint and abs(bpm * 2 - hint) < abs(bpm - hint):     # the reader folds fast tempos to half time
                bpm, bar = bpm * 2, (bar * 2) % 4
            beat["bpm"], beat["bar_beat"] = bpm, bar
            row.update({"bpm": beat["bpm"], "first_beat": beat["first_beat"], "bar_beat": int(beat.get("bar_beat", 0) or 0),
                        "beats_per_bar": 4, "seconds": beat["seconds"], "drift_ms": beat["drift_ms"],
                        "kept": [round(start, 2), round(end, 2)]})
        else:
            row.update({"bpm": item.get("bpm", 120.0), "first_beat": 0.0, "bar_beat": 0, "beats_per_bar": 4})
        manifest["tracks"] = [t for t in manifest["tracks"] if t["name"] != name] + [row]
        baked.append(row)
        print(f"baked {name:<24} {item['role']:<7} gain {gain:+.1f} dB" +
              (f"  {row['bpm']:.2f} BPM, drift {row['drift_ms']} ms, kept {row['kept'][0]}-{row['kept'][1]} s" if item["role"] == "fight" else ""), flush=True)

    if a.dry_run:
        print("left out:", left_out)
        return
    names = {r["name"] for r in baked}
    for zone, tracks in plan.get("zones", {}).items():
        entry = next((z for z in manifest["zones"] if z["name"] == zone), None)
        if entry is None:
            entry = {"name": zone}
            manifest["zones"].append(entry)
        entry["tracks"] = [t for t in tracks.get("combat", []) if t in names]
        entry["explore"] = [t for t in tracks.get("explore", []) if t in names]
    if "menu" in plan:
        manifest["menu"] = {k: v for k, v in plan["menu"].items() if v in names}
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")
    print("left out:", left_out or "none")
    print(f"RESULT {len(baked)} baked, {len(left_out)} left out -> {MANIFEST}")


if __name__ == "__main__":
    main()
