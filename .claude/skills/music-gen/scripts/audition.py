"""Builds the soundtrack audition page for a music-gen batch: every take, grouped by where it would play in the game.

    python audition.py <batch folder> --stage <folder under the scratchpad>   (the sound-scout venv: ffmpeg)

Writes <stage>/audition.html and <stage>/audio/<take>.mp3 (192k previews levelled to -14 LUFS, so takes compare fairly), and the same page beside
the takes in Drive. The page plays each take, a loop seam, a click on every beat for fight music, the transitions
(Title -> Selection, walking -> fight) and a faction coat swelling over the Selection theme. Picks are saved in the
page's own store (collection "picks", one document per cue), which Claude reads back with ArtifactData.
"""
import argparse, json, os, shutil, subprocess, sys

SCOUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "sound-scout", "scripts")
sys.path.insert(0, os.path.abspath(SCOUT))
import common  # noqa: E402

TEMPLATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "audition_template.html")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch")
    ap.add_argument("--stage", required=True)
    a = ap.parse_args()
    sheet = json.load(open(os.path.join(a.batch, "sheet.json"), encoding="utf-8"))
    book = json.load(open(os.path.join(a.batch, "cues.json"), encoding="utf-8"))
    checks_path = os.path.join(a.batch, "check.json")
    checks = json.load(open(checks_path, encoding="utf-8")) if os.path.exists(checks_path) else {}

    audio_dir = os.path.join(a.stage, "audio")
    os.makedirs(audio_dir, exist_ok=True)
    takes = []
    for row in book:
        src = os.path.join(a.batch, row["files"].get("flac") or row["files"].get("wav") or next(iter(row["files"].values())))
        dst = os.path.join(audio_dir, row["take"] + ".mp3")
        if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(src):
            subprocess.run([common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-i", src, "-ac", "2", "-ar", "44100",
                            "-af", "loudnorm=I=-14:TP=-1.5:LRA=11", "-b:a", "192k", dst], check=True)
        takes.append({**{k: row[k] for k in ("take", "cue", "slot", "tool", "seed", "bpm", "key", "seconds", "made")},
                      "src": "audio/" + row["take"] + ".mp3", "title": row.get("title", ""), "check": checks.get(row["take"], {})})

    cues = [{k: c[k] for k in ("id", "slot", "bpm", "key", "seconds", "tags")} for c in sheet["cues"]]
    data = {"batch": sheet["batch"], "title": sheet.get("title", sheet["batch"]), "note": sheet.get("note", ""),
            "slots": sorted(sheet["slots"], key=lambda s: s.get("order", 0)), "previews": sheet.get("previews", []),
            "cues": cues, "takes": takes}
    page = open(TEMPLATE, encoding="utf-8").read().replace("/*__DATA__*/null", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
    out = os.path.join(a.stage, "audition.html")
    open(out, "w", encoding="utf-8").write(page)
    shutil.copy2(out, os.path.join(a.batch, "audition.html"))
    size = sum(os.path.getsize(os.path.join(audio_dir, f)) for f in os.listdir(audio_dir))
    print("RESULT " + json.dumps({"page": out, "takes": len(takes), "audio_mb": round(size / 2**20, 1)}))


if __name__ == "__main__":
    main()
