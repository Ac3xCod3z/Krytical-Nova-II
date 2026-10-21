"""Measures every take of a music-gen batch, so a bad one is flagged before the owner listens. Claude cannot hear.

    python check.py <batch folder>        (the sound-scout venv: numpy, ffmpeg, the CLAP model)

Per take: length, integrated loudness (EBU R128 through ffmpeg), peak, silence at the head and tail, the beat
(sound-scout's tempo.measure: BPM, first beat, drift), the loop seam (level jump from the last half second into the
first), and how much the CLAP model hears a singing voice against instrumental music (the owner: instrumental only).
Writes check.json beside cues.json and prints one line per take with its flags.
"""
import json, os, re, subprocess, sys

import numpy as np

SCOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "sound-scout", "scripts")
sys.path.insert(0, os.path.abspath(SCOUT))
import common  # noqa: E402  (sound-scout: decode, ffmpeg, CLAP)
import tempo   # noqa: E402

SILENT_DB = -50.0
VOICE_TEXTS = ["a person singing, vocals, a singing voice with lyrics", "instrumental music with no singing and no voice"]


def loudness(path):
    err = subprocess.run([common.ffmpeg(), "-nostdin", "-hide_banner", "-i", path, "-af", "ebur128=peak=true", "-f", "null", "-"],
                         stdin=subprocess.DEVNULL, capture_output=True, text=True, errors="replace").stderr
    tail = err[err.rfind("Summary:"):]
    i = re.search(r"I:\s+(-?[\d.]+) LUFS", tail)
    p = re.search(r"Peak:\s+(-?[\d.]+) dBFS", tail)
    return (float(i.group(1)) if i else None), (float(p.group(1)) if p else None)


def db(x):
    return 20 * np.log10(max(float(x), 1e-9))


def edges(audio, rate):
    frame = int(rate * 0.05)
    n = len(audio) // frame
    lv = np.array([db(np.sqrt(np.mean(audio[i * frame:(i + 1) * frame] ** 2))) for i in range(n)])
    loud = np.where(lv > SILENT_DB)[0]
    if not len(loud):
        return len(audio) / rate, len(audio) / rate
    return loud[0] * 0.05, (n - 1 - loud[-1]) * 0.05


def seam(audio, rate):
    half = int(rate * 0.5)
    if len(audio) < 4 * half:
        return None
    return round(abs(db(np.sqrt(np.mean(audio[-half:] ** 2))) - db(np.sqrt(np.mean(audio[:half] ** 2)))), 1)


def voice_share(audio, rate):
    """Share of 10 s windows where CLAP rates 'singing voice' over 'instrumental'."""
    win = rate * 10
    clips = [audio[s:s + win] for s in range(0, max(1, len(audio) - win + 1), win)][:12]
    a = common.embed_audio([c.astype(np.float32) for c in clips])
    t = common.embed_text(VOICE_TEXTS)
    sims = a @ t.T
    return round(float(np.mean(sims[:, 0] > sims[:, 1])), 2)


def bpm_ok(read, want):
    return any(abs(read * f - want) <= max(1.0, 0.03 * want) for f in (1.0, 2.0, 0.5))


def main():
    folder = sys.argv[1]
    book = json.load(open(os.path.join(folder, "cues.json"), encoding="utf-8"))
    sheet = json.load(open(os.path.join(folder, "sheet.json"), encoding="utf-8"))
    kinds = {s["slot"]: s["kind"] for s in sheet["slots"]}
    out = {}
    for row in book:
        f = row["files"].get("flac") or row["files"].get("wav") or row["files"].get("mp3")
        path = os.path.join(folder, f)
        audio = common.decode(path, rate=common.RATE)
        lufs, peak = loudness(path)
        head, tail = edges(audio, common.RATE)
        m = {"seconds": round(len(audio) / common.RATE, 2), "lufs": lufs, "peak_db": peak, "head_s": head, "tail_s": tail,
             "seam_db": seam(audio, common.RATE), "voice": voice_share(audio, common.RATE)}
        if m["seconds"] >= tempo.SHORTEST:
            beat = tempo.measure(common.decode(path, rate=tempo.RATE))
            m.update({k: beat.get(k) for k in ("bpm", "first_beat", "drift_ms", "clear")})
        kind = kinds.get(row["slot"], "loop")
        flags = []
        if m["voice"] >= 0.25:
            flags.append(f"VOICE? {int(m['voice'] * 100)}% of windows")
        # Peak and loudness are only reported: bake.py sets the level. The seam is only reported too: a sketch ends
        # with a fade, and the loop point is cut at bake time.
        if head > 1.0:
            flags.append(f"{head:.1f}s silent head")
        if m["seconds"] < float(row["seconds"]) - 2.0:      # longer is fine: it is trimmed at bake time
            flags.append(f"length {m['seconds']}s vs {row['seconds']}s")
        if m.get("bpm") and not bpm_ok(m["bpm"], float(row["bpm"])):     # the game uses the measured BPM; 3 % is fine
            flags.append(f"BPM {m['bpm']} vs {row['bpm']}")
        if kind == "fight" and (m.get("drift_ms") is None or m["drift_ms"] > tempo.STEADY_MS):
            flags.append(f"beat drifts {m.get('drift_ms')} ms (Cadence needs <= {tempo.STEADY_MS})")
        m["flags"] = flags
        out[row["take"]] = m
        print(f"{row['take']:<18} {m['seconds']:>6}s  {lufs} LUFS  peak {peak}  bpm {m.get('bpm')}  drift {m.get('drift_ms')}  "
              f"seam {m['seam_db']}  voice {m['voice']}  {'OK' if not flags else '; '.join(flags)}", flush=True)
    json.dump(out, open(os.path.join(folder, "check.json"), "w", encoding="utf-8"), indent=1)
    bad = sum(1 for m in out.values() if m["flags"])
    print(f"RESULT {len(out)} takes, {bad} flagged -> {os.path.join(folder, 'check.json')}")


if __name__ == "__main__":
    main()
