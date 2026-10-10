"""Bakes the chosen library sounds into the project, shaped and levelled, under the names the game's slots look for.

    python bake.py            (bake everything in picks.json's "bake" plan, then check)
    python bake.py --check    (only re-measure what is in Assets/_Game/Audio)

picks.json holds what the user chose ("slots", with their notes) and the plan made from it ("bake"):
    "<Slot>" or "<Slot>_<Set>": [ entry, ... ]
An entry is {"path"} or {"layers": [{"path", "gain", "delay", "pitch", "tilt", "max_seconds"}]}, with optional
"pitch" (semitones), "tilt" (dB per octave, + brighter), "max_seconds", "start". A set (Death_Null) is a group of a
slot's files that the game chooses between itself: here the dying player's faction.

Files are SFX_<Slot>[_<Set>]_<nn>.wav in the slot's folder (slots.json), loops AMB_Weather_<Name>.wav. Existing files
of the same name are overwritten (their .meta, so their GUID, stays); every other SFX_ / AMB_ file in the audio
folders is deleted with its .meta. sources.json records where each file came from.
Afterwards run Krytical Nova > Setup > Sounds in the Editor.
"""
import glob
import json
import os
import sys

import numpy as np

import common
import shape

HERE = os.path.dirname(os.path.abspath(__file__))
SLOTS = os.path.join(HERE, "..", "slots.json")
PICKS = os.path.join(HERE, "..", "picks.json")
SOURCES = os.path.join(HERE, "..", "sources.json")
AUDIO = os.path.join(common.PROJECT, "Assets", "_Game", "Audio")


def key(name):
    return name.replace(" ", "").replace("-", "")


def check():
    """Every baked file: level under the ceiling, starts at once, and a loop's end meets its start."""
    bad, count = 0, 0
    for path in sorted(glob.glob(os.path.join(AUDIO, "**", "*.wav"), recursive=True)):
        count += 1
        audio, rate = shape.soundfile.read(path, dtype="float32", always_2d=True)
        mono = audio.mean(axis=1)
        peak = 20 * np.log10(max(float(np.abs(audio).max()), 1e-6))
        lead = int(np.argmax(np.abs(mono) > np.abs(mono).max() * 0.01)) / rate
        loop = os.path.basename(path).startswith("AMB_")
        notes = []
        if peak > shape.PEAK_CEILING + 0.1:
            notes.append(f"peak {peak:.1f} dB over the ceiling")
        if lead > 0.02 and not loop:
            notes.append(f"{lead * 1000:.0f} ms of silence first")
        if loop:
            jump = float(np.abs(audio[0] - audio[-1]).max())
            usual = float(np.percentile(np.abs(np.diff(audio, axis=0)), 99.9))
            if jump > usual:
                notes.append(f"loop seam jumps by {jump:.3f} (usual step {usual:.3f})")
        if notes:
            bad += 1
            print("  CHECK", os.path.relpath(path, AUDIO), "; ".join(notes))
    print(f"check: {count} files,", "all clean" if not bad else f"{bad} to look at")
    return bad


def describe(entry):
    if "layers" in entry:
        return " + ".join(os.path.basename(layer["path"]) for layer in entry["layers"])
    extra = "".join(f" {k} {entry[k]:+g}" for k in ("pitch", "tilt") if entry.get(k))
    return entry["path"] + extra


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--check":
        return check()
    plan = json.load(open(PICKS, encoding="utf-8"))["bake"]
    slots = {s["name"]: s for s in json.load(open(SLOTS, encoding="utf-8"))}
    written, sources = set(), {}

    for name, entries in plan.items():
        slot_name, _, group = name.partition("_")
        slot = slots[slot_name]
        folder = os.path.join(AUDIO, *slot["folder"].split("/"))
        os.makedirs(folder, exist_ok=True)
        stereo = slot.get("stereo", False)
        for n, entry in enumerate(entries):
            longest = entry.get("max_seconds", slot.get("max_seconds"))
            if slot.get("loop"):
                file = "AMB_" + key(slot_name).replace("Weather", "Weather_") + ".wav"
                audio = shape.loop(entry["path"], start=entry.get("start"))
            else:
                file = f"SFX_{key(slot_name)}{'_' + group if group else ''}_{n + 1:02d}.wav"
                if "layers" in entry:
                    audio = shape.mix(entry["layers"], stereo=stereo, max_seconds=longest, pitch=entry.get("pitch", 0.0), tilt=entry.get("tilt", 0.0))
                else:
                    audio = shape.one_shot(entry["path"], stereo=stereo, max_seconds=longest, start=entry.get("start"),
                                           pitch=entry.get("pitch", 0.0), tilt=entry.get("tilt", 0.0))
            path = os.path.join(folder, file)
            shape.write(path, audio)
            written.add(os.path.normcase(os.path.abspath(path)))
            sources[slot["folder"] + "/" + file] = {"slot": slot_name, "from": describe(entry)}
        print(f"{name}: {len(entries)}")

    removed = 0
    for folder in {os.path.join(AUDIO, *s["folder"].split("/")) for s in slots.values()}:
        for old in glob.glob(os.path.join(folder, "*.wav")):
            if os.path.normcase(os.path.abspath(old)) not in written and os.path.basename(old).startswith(("SFX_", "AMB_")):
                for stale in (old, old + ".meta"):
                    if os.path.exists(stale):
                        os.remove(stale)
                removed += 1
    with open(SOURCES, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(sources.items())), f, indent=1)
    print(f"{len(written)} files written, {removed} old ones removed")
    return check()


if __name__ == "__main__":
    sys.exit(main())
