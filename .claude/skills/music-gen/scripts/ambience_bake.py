"""Bakes zone ambience into the game as seamless loops, and writes what the game needs to place them.

    (sound-scout venv) python ambience_bake.py <plan.json> [--dry-run]

Each piece in the plan names a library file (Drive SFx), the stretch of it to use, a loudness, and where it plays:
  "bed"   a 2D loop under the whole zone: "when" is always, day or night (the zone player crossfades day and night
          with the sun's height)
  "point" a 3D loop on scene objects (an anchor rule: objects named..., or the children of an object whose names
          start with...), louder as the player walks up to it
The loop is made seamless: its last <crossfade> seconds are faded over its first ones (equal power), so the end
runs straight into the start with no seam. Every file is levelled to its own loudness (beds under the music,
points louder because distance takes them down). Writes Assets/_Game/Audio/Ambience/Zones/AMB_<Name>.ogg (the AMB_
prefix makes AudioSetup stream them) and the sound-scout skill's ambience.json; then in the Editor
Krytical Nova > Setup > Ambience.
"""
import argparse, json, os, re, subprocess, sys, tempfile

import numpy as np
import soundfile

SCOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "sound-scout", "scripts")
sys.path.insert(0, os.path.abspath(SCOUT))
import common  # noqa: E402

TARGET = os.path.join(common.PROJECT, "Assets", "_Game", "Audio", "Ambience", "Zones")
MANIFEST = os.path.join(SCOUT, "..", "ambience.json")
RATE = 44100
CEILING = -2.0   # dBFS peak, as the sound-scout skill bakes everything


def lufs_of(path):
    err = subprocess.run([common.ffmpeg(), "-nostdin", "-hide_banner", "-i", path, "-af", "ebur128", "-f", "null", "-"],
                         capture_output=True, text=True, errors="replace").stderr
    m = re.search(r"I:\s+(-?[\d.]+) LUFS", err[err.rfind("Summary:"):])
    return float(m.group(1)) if m else None


def seamless(audio, crossfade):
    """The stretch, shortened by the crossfade, with its tail laid over its head: sample n-1 runs into sample 0."""
    x = int(crossfade * RATE)
    if len(audio) < 4 * x:
        raise ValueError("too short for its crossfade")
    t = np.linspace(0.0, np.pi / 2, x, dtype=np.float32)[:, None]
    head, tail = audio[:x], audio[-x:]
    out = audio[:-x].copy()
    out[:x] = head * np.sin(t) + tail * np.cos(t)      # equal power: the loudness holds through the join
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("plan")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    plan = json.load(open(a.plan, encoding="utf-8"))
    os.makedirs(TARGET, exist_ok=True)
    zones = {}
    for z in plan["zones"]:
        zrow = {"name": z["name"], "scene": z["scene"], "beds": [], "points": []}
        for p in z["pieces"]:
            src = os.path.join(common.LIBRARY, p["lib"])
            name = p["name"]
            dst = os.path.join(TARGET, f"AMB_{name}.ogg")
            with tempfile.TemporaryDirectory() as tmp:
                # 1. The stretch, levelled and limited. Any filter with a delay (the limiter has one) must come before
                #    the loop is made, or it shifts the join and the loop clicks (21 Oct 2026).
                raw = os.path.join(tmp, "raw.wav")
                cut = ["-ss", str(p.get("start", 0))] + (["-t", str(p["seconds"])] if p.get("seconds") else [])
                subprocess.run([common.ffmpeg(), "-nostdin", "-v", "error", "-y"] + cut + ["-i", src, "-vn", "-map_metadata", "-1",
                                "-ac", "2", "-ar", str(RATE), "-c:a", "pcm_f32le", raw], check=True)
                gain = p["lufs"] - (lufs_of(raw) or p["lufs"])
                levelled = os.path.join(tmp, "levelled.wav")
                subprocess.run([common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-i", raw,
                                "-af", f"volume={gain:.2f}dB,alimiter=limit={10 ** (CEILING / 20):.4f}:level=disabled",
                                "-c:a", "pcm_f32le", levelled], check=True)
                audio, _ = soundfile.read(levelled, dtype="float32", always_2d=True)
                # The limiter's look-ahead leaves its first few ms quiet: start the loop past it.
                audio = audio[int(0.05 * RATE):]
                # 2. The seamless loop. 3. Encoded with no filter at all, so the join stays where it was made.
                loop = seamless(audio, p.get("crossfade", 3.0))
                if a.dry_run:
                    print(f"{z['name']:<20} {name:<26} {p['kind']:<5} {len(loop) / RATE:6.1f} s  gain {gain:+.1f} dB")
                    continue
                wav = os.path.join(tmp, "loop.wav")
                soundfile.write(wav, loop, RATE, subtype="FLOAT")
                subprocess.run([common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-i", wav, "-vn", "-map_metadata", "-1",
                                "-c:a", "libvorbis", "-q:a", "5", dst], check=True)
            row = {"name": name, "file": os.path.basename(dst), "volume": p.get("volume", 1.0), "source": p["lib"],
                   "start": p.get("start", 0), "seconds": round(len(loop) / RATE, 2), "lufs": p["lufs"]}
            if p["kind"] == "bed":
                row["when"] = p.get("when", "always")
                zrow["beds"].append(row)
            else:
                row.update({k: p[k] for k in ("anchorNames", "anchorParent", "anchorPrefix", "anchorContains") if k in p})
                row["minDistance"] = p.get("minDistance", 5.0)
                row["maxDistance"] = p.get("maxDistance", 40.0)
                zrow["points"].append(row)
            print(f"baked {z['name']:<20} {name:<26} {p['kind']:<5} {row['seconds']:6.1f} s", flush=True)
        zones[z["name"]] = zrow
    if a.dry_run:
        return
    manifest = {"zones": list(zones.values())}
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")
    print(f"RESULT {sum(len(z['beds']) + len(z['points']) for z in zones.values())} loops -> {MANIFEST}")


if __name__ == "__main__":
    main()
