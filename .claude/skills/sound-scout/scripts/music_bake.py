"""Puts the owner's music picks into the game, with what was measured of each.

    python music_bake.py <file with the pasted KN-MUSIC line> --zone <Zone> [--round 1] [--add]
    python music_bake.py --test-beat --zone <Zone>      made-up drums at 120 beats a minute, to test with before anything is picked
    python music_bake.py --check                        measures every baked track again and compares with music.json

A pick is the very file the audition page played (levelled, Ogg Vorbis), copied to
Assets/_Game/Audio/Music/MUS_<Name>.ogg. What the game needs to keep time to it goes into music.json beside
slots.json: beats a minute (halved or doubled if the owner switched the click), the time of a beat (with the owner's
nudge), which beat is the first of the bar, where the track came from. --zone names the zone that plays the picks,
in the order they are on the page; with --add they are added to what the zone already plays. Then, in the Editor,
Krytical Nova > Setup > Music (MusicSetup.Build) makes the MusicTrack and ZoneMusic assets.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys

import numpy as np

import common
import tempo
import music_audition

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, "..", "music.json")
TARGET = os.path.join(common.PROJECT, "Assets", "_Game", "Audio", "Music")
TEST_NAME = "TestBeat"


def load_manifest():
    if os.path.exists(MANIFEST):
        with open(MANIFEST, encoding="utf-8") as f:
            return json.load(f)
    return {"tracks": [], "zones": []}


def save_manifest(manifest):
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")


def put_track(manifest, row):
    manifest["tracks"] = [t for t in manifest["tracks"] if t["name"] != row["name"]] + [row]


def set_zone(manifest, zone, names, add):
    for entry in manifest["zones"]:
        if entry["name"] == zone:
            entry["tracks"] = (entry["tracks"] if add else []) + [n for n in names if not add or n not in entry["tracks"]]
            return
    manifest["zones"].append({"name": zone, "tracks": list(names)})


def asset_name(title):
    return "".join(word[:1].upper() + word[1:] for word in re.sub(r"[^A-Za-z0-9 ]", " ", title).split())


def read_picks(path):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    at = text.find("KN-MUSIC")
    return json.loads(text[at + len("KN-MUSIC"):].strip() if at >= 0 else text)


def bake_picks(a):
    out = music_audition.OUT if a.round == 1 else os.path.join(music_audition.OUT, f"round{a.round}")
    with open(os.path.join(out, "tracks.json"), encoding="utf-8") as f:
        tracks = {t["source"]: t for t in json.load(f)}
    picks = [p for p in read_picks(a.picks) if p.get("pick")]
    if not picks:
        print("No track is ticked: nothing baked.")
        return 1
    os.makedirs(TARGET, exist_ok=True)
    manifest = load_manifest()
    names = []
    for pick in picks:
        track = tracks.get(pick["source"])
        if track is None:
            print("  not on the page:", pick["source"])
            continue
        factor = float(pick.get("factor", 1) or 1)
        name = asset_name(track["name"])
        file = f"MUS_{name}.ogg"
        shutil.copyfile(os.path.join(out, track["file"]), os.path.join(TARGET, file))
        period = 60.0 / (track["bpm"] * factor)
        first = (track["first_beat"] + float(pick.get("nudge", 0)) / 1000.0) % period
        bar = int(round((track["bar_beat"] + int(pick.get("bar", 0))) * factor)) % max(1, int(round(4 * factor)))
        put_track(manifest, {
            "name": name, "title": track["name"], "file": file, "bpm": round(track["bpm"] * factor, 4),
            "first_beat": round(first, 4), "bar_beat": bar, "beats_per_bar": 4, "seconds": track["seconds"],
            "source": pick["source"], "gain_db": track["gain_db"], "measured_bpm": track["bpm"],
            "nudge_ms": pick.get("nudge", 0), "note": pick.get("note", ""),
        })
        names.append(name)
        print(f"  {file}: {track['bpm'] * factor:.3f} beats a minute, a beat at {first:.3f} s, bar starts on beat {bar}")
    set_zone(manifest, a.zone, names, a.add)
    save_manifest(manifest)
    print(f"{len(names)} track(s) baked for zone {a.zone}. Now run Krytical Nova > Setup > Music in the Editor.")
    return 0


def bake_test_beat(a):
    """Made-up drums with a beat that is known exactly: what the game's timing is tested against."""
    bpm, first, seconds = 120.0, 0.25, 64.0
    audio = tempo.synth(bpm, first, seconds, noise=0.003)
    audio = (audio / np.max(np.abs(audio)) * 0.5).astype(np.float32)
    os.makedirs(TARGET, exist_ok=True)
    file = f"MUS_{TEST_NAME}.ogg"
    cmd = [common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-f", "f32le", "-ar", str(tempo.RATE), "-ac", "1", "-i", "-",
           "-ac", "2", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "5", os.path.join(TARGET, file)]
    subprocess.run(cmd, input=audio.tobytes(), stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
    heard = tempo.measure(common.decode(os.path.join(TARGET, file), rate=tempo.RATE))
    manifest = load_manifest()
    put_track(manifest, {
        "name": TEST_NAME, "title": "Test Beat (made-up drums)", "file": file, "bpm": bpm, "first_beat": first, "bar_beat": 0,
        "beats_per_bar": 4, "seconds": heard["seconds"], "source": "tempo.synth", "test": True,
    })
    set_zone(manifest, a.zone, [TEST_NAME], a.add)
    save_manifest(manifest)
    print(f"{file}: made at {bpm} beats a minute, first beat {first} s; read back as {heard['bpm']} and {heard['first_beat']} s.")
    return 0


def check():
    bad = 0
    for track in load_manifest()["tracks"]:
        path = os.path.join(TARGET, track["file"])
        if not os.path.exists(path):
            print(f"  MISSING {track['file']}")
            bad += 1
            continue
        heard = tempo.measure(common.decode(path, seconds=tempo.LONGEST, rate=tempo.RATE))
        period = 60.0 / track["bpm"]
        ratio = heard["bpm"] / track["bpm"]
        same_tempo = any(abs(ratio - r) < 0.002 for r in (0.5, 1.0, 2.0))
        off = abs(tempo.wrap((heard["first_beat"] - track["first_beat"]) / min(period, 60.0 / heard["bpm"]))) * min(period, 60.0 / heard["bpm"]) * 1000
        nudged = abs(float(track.get("nudge_ms", 0)))
        ok = same_tempo and off <= nudged + 5.0
        bad += 0 if ok else 1
        print(f"  {'ok ' if ok else 'BAD'} {track['file']}: kept {track['bpm']:.3f} / read {heard['bpm']:.3f} beats a minute, "
              f"first beat {off:.1f} ms apart (owner's nudge {nudged:.0f} ms), drift {heard['drift_ms']:.1f} ms")
    print("all baked tracks agree with music.json" if not bad else f"{bad} track(s) do not agree")
    return 1 if bad else 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("picks", nargs="?")
    p.add_argument("--zone", default="TestArena")
    p.add_argument("--round", type=int, default=1)
    p.add_argument("--add", action="store_true")
    p.add_argument("--test-beat", action="store_true")
    p.add_argument("--check", action="store_true")
    a = p.parse_args()
    if a.check:
        return check()
    if a.test_beat:
        return bake_test_beat(a)
    if not a.picks:
        p.error("give the file with the pasted picks, or --test-beat, or --check")
    return bake_picks(a)


if __name__ == "__main__":
    sys.exit(main())
