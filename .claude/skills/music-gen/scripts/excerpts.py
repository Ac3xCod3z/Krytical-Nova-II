"""Cuts audition excerpts from the sound library into a music-gen batch, for sheets whose takes are found, not made
(ambience beds: "takes" with a library path, a start and a length).

    (sound-scout venv) python excerpts.py <sheet.json>

Writes <take>.wav (2 s fade in and out, so a preview starts and ends softly; the game's loop is cut at bake time) into
Google Drive, KN-Originals/<batch>/, a cues.json line for each with where it came from, and copies sheet.json beside
them. A take may instead name "from": a batch folder file to copy (a kept track). Then check.py and audition.py as usual.
"""
import json, os, shutil, subprocess, sys, time

SCOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "sound-scout", "scripts")
sys.path.insert(0, os.path.abspath(SCOUT))
import common  # noqa: E402

FADE = 2.0


def main():
    sheet_path = sys.argv[1]
    sheet = json.load(open(sheet_path, encoding="utf-8"))
    root = os.path.join(os.path.dirname(common.LIBRARY), "Music", "KN-Originals")
    out = os.path.join(root, sheet["batch"])
    os.makedirs(out, exist_ok=True)
    shutil.copy2(sheet_path, os.path.join(out, "sheet.json"))
    cues = {c["id"]: c for c in sheet["cues"]}
    book = []
    for t in sheet.get("takes", []):
        cue = cues[t["cue"]]
        dst = os.path.join(out, t["take"] + ".wav")
        if "from" in t:
            shutil.copy2(os.path.join(root, t["from"]), dst)
            tool, source = "Kept", t["from"]
        else:
            src = os.path.join(common.LIBRARY, t["lib"])
            length = float(t["seconds"])
            subprocess.run([common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-ss", str(t.get("start", 0)), "-t", str(length),
                            "-i", src, "-vn", "-map_metadata", "-1", "-ac", "2", "-ar", "44100",
                            "-af", f"afade=t=in:d={FADE},afade=t=out:st={max(0.0, length - FADE)}:d={FADE}", dst], check=True)
            tool, source = "Sound library (Drive SFx, found with sound-scout)", t["lib"]
        book.append({"take": t["take"], "cue": t["cue"], "slot": cue["slot"], "tool": tool, "model": "", "title": t.get("title", ""),
                     "seed": None, "tags": source, "bpm": cue["bpm"], "key": cue["key"], "seconds": t.get("seconds", cue["seconds"]),
                     "source": source, "start": t.get("start", 0), "made": time.strftime("%Y-%m-%d %H:%M"),
                     "files": {"wav": os.path.basename(dst)}})
        print("cut", t["take"], flush=True)
    json.dump(book, open(os.path.join(out, "cues.json"), "w", encoding="utf-8"), indent=1)
    print(f"RESULT {len(book)} takes -> {out}")


if __name__ == "__main__":
    main()
