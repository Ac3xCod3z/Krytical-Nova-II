"""Measure every clip of a batch: length, level and how much of it is voiced. I cannot hear; this is how I check.

Run with ComfyUI's Python (it has soundfile and numpy):
  C:/AI/ComfyUI-Easy-Install/ComfyUI-Easy-Install/python_embeded/python.exe -I measure.py <clips folder> [<lines.csv>]

With the CSV it also flags clips whose pace does not fit their words (too slow: padded or repeated; too fast: cut
short), peaks under -14 dB or clipping, and less than 40 % voiced. Prints one line per clip, then FLAGGED lines.
"""
import csv, glob, os, sys

import numpy as np
import soundfile as sf

folder = sys.argv[1]
words = {}
if len(sys.argv) > 2:
    words = {r["id"]: len(r["text"].split()) for r in csv.DictReader(open(sys.argv[2], encoding="utf-8"))}
flags, n = [], 0
for f in sorted(glob.glob(os.path.join(folder, "*.flac"))):
    cid = os.path.splitext(os.path.basename(f))[0]
    x, sr = sf.read(f)
    x = x if x.ndim == 1 else x.mean(1)
    dur = len(x) / sr
    rms = 20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-9)
    peak = 20 * np.log10(np.abs(x).max() + 1e-9)
    hop = sr // 50
    frames = x[:len(x) // hop * hop].reshape(-1, hop)
    voiced = (np.sqrt((frames ** 2).mean(1)) > 0.01).mean() * 100
    n += 1
    print(f"{cid:32s} {dur:5.2f}s rms={rms:5.1f}dB peak={peak:5.1f}dB voiced={voiced:3.0f}%")
    why = []
    if cid in words:
        wps = words[cid] / dur
        if wps < 1.3:
            why.append(f"slow/long {wps:.1f} words/s")
        if wps > 4.2:
            why.append(f"fast/cut {wps:.1f} words/s")
    if peak < -14:
        why.append(f"quiet (peak {peak:.1f} dB)")
    if peak > -0.3:
        why.append(f"clipping (peak {peak:.1f} dB)")
    if voiced < 40:
        why.append(f"only {voiced:.0f}% voiced")
    if why:
        flags.append(f"FLAGGED {cid} ({words.get(cid, '?')} words, {dur:.1f}s): " + ", ".join(why))
print(f"{n} clips measured, {len(flags)} flagged")
print("\n".join(flags))
