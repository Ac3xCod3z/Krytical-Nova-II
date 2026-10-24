"""Draws sounds as pictures: per file a waveform and a mel spectrogram, all on one time and level scale.

    python sheet.py out.png file1.wav file2.mp3 ...      (paths relative to the library, or absolute)
    python sheet.py out.png --json shortlist.json [--seconds 3]

Reading it: left to right is time (ticks every 0.1 s under 2 s, else every second), bottom to top is low to high
pitch (40 Hz to 16 kHz), brightness is level over a 70 dB range. A click is a thin vertical line; a whoosh a soft
cloud that rises and falls; a tone horizontal lines; a clang a vertical line with horizontal lines trailing from it.
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

import common
from common import RATE

BANDS, ROW, WAVE, LABEL = 96, 96, 40, 14
SIZE, HOP = 2048, 256


def mel_filter():
    def mel(f): return 2595 * np.log10(1 + f / 700)
    def hz(m): return 700 * (10 ** (m / 2595) - 1)
    edges = hz(np.linspace(mel(40), mel(16000), BANDS + 2))
    freqs = np.fft.rfftfreq(SIZE, 1 / RATE)
    bank = np.zeros((BANDS, len(freqs)), dtype=np.float32)
    for b in range(BANDS):
        lo, mid, hi = edges[b], edges[b + 1], edges[b + 2]
        bank[b] = np.clip(np.minimum((freqs - lo) / (mid - lo), (hi - freqs) / (hi - mid)), 0, None)
    return bank


def colour(x):
    """0..1 to a dark-blue, magenta, yellow, white ramp."""
    stops = np.array([[0, 0, 8], [40, 10, 90], [150, 30, 110], [235, 100, 50], [255, 210, 80], [255, 255, 235]], dtype=np.float32)
    pos = np.clip(x, 0, 1) * (len(stops) - 1)
    lo = np.floor(pos).astype(int).clip(0, len(stops) - 2)
    t = (pos - lo)[..., None]
    return (stops[lo] * (1 - t) + stops[lo + 1] * t).astype(np.uint8)


def draw(paths, out, seconds=None, width=1100):
    bank = mel_filter()
    clips = []
    for path in paths:
        audio = common.decode(common.absolute(path), seconds=seconds or 60)
        loud = np.nonzero(np.abs(audio) > np.abs(audio).max() * 0.003)[0] if len(audio) and np.abs(audio).max() > 0 else []
        if len(loud):
            audio = audio[max(0, loud[0] - RATE // 100):loud[-1] + RATE // 20]  # the sounding part
        clips.append((path, audio))
    longest = max(len(a) for _, a in clips) / RATE
    span = seconds or longest
    per_second = (width - 8) / span
    height = (LABEL + WAVE + ROW + 6) * len(clips)
    sheet = Image.new("RGB", (width, height), (16, 16, 20))
    pen = ImageDraw.Draw(sheet)

    for n, (path, audio) in enumerate(clips):
        top = n * (LABEL + WAVE + ROW + 6)
        peak = float(np.abs(audio).max()) if len(audio) else 0.0
        rms = float(np.sqrt(np.mean(audio ** 2))) if len(audio) else 0.0
        label = f"{n + 1}. {common.relative(common.absolute(path))}   {len(audio) / RATE:.2f}s  peak {20 * np.log10(max(peak, 1e-6)):.0f} dB  rms {20 * np.log10(max(rms, 1e-6)):.0f} dB"
        pen.text((4, top), label[:180], fill=(230, 230, 230))
        px = max(1, int(len(audio) / RATE * per_second))
        if len(audio) < SIZE:
            audio = np.pad(audio, (0, SIZE - len(audio)))

        # Waveform, drawn at its own peak so its shape shows; the label carries the level.
        cols = np.array_split(audio[:int(span * RATE)], px)
        mid = top + LABEL + WAVE // 2
        scale = (WAVE // 2 - 1) / max(peak, 1e-6)
        for x, col in enumerate(cols):
            if len(col):
                pen.line([(4 + x, mid - col.max() * scale), (4 + x, mid - col.min() * scale)], fill=(120, 220, 255))

        frames = np.lib.stride_tricks.sliding_window_view(audio, SIZE)[::HOP] * np.hanning(SIZE)
        power = (np.abs(np.fft.rfft(frames, axis=1)) ** 2) @ bank.T
        level = 10 * np.log10(power + 1e-10)
        level = np.clip((level - (level.max() - 70)) / 70, 0, 1)            # top 70 dB of this sound
        picture = Image.fromarray(colour(level.T[::-1]))
        sheet.paste(picture.resize((px, ROW), Image.BILINEAR), (4, top + LABEL + WAVE))

        tick = 0.1 if span <= 2 else 1.0 if span <= 30 else 10.0
        t = 0.0
        while t <= span:
            x = 4 + int(t * per_second)
            pen.line([(x, top + LABEL + WAVE + ROW), (x, top + LABEL + WAVE + ROW + 3)], fill=(200, 200, 200))
            t += tick
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    sheet.save(out)
    print(out, f"{len(clips)} sounds, {span:.2f}s across")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("out")
    p.add_argument("files", nargs="*")
    p.add_argument("--json")
    p.add_argument("--seconds", type=float, help="time across the sheet (default: the longest sound)")
    a = p.parse_args()
    paths = list(a.files)
    if a.json:
        with open(a.json, encoding="utf-8") as f:
            paths += json.load(f)
    if not paths:
        p.error("no files")
    draw(paths, a.out, a.seconds)


if __name__ == "__main__":
    sys.exit(main())
