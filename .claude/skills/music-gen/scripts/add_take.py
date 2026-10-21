"""Records a take made outside ComfyUI (Flow Music in Chrome) in a batch's cues.json, beside the ACE-Step takes.

python -I add_take.py <batch folder> <file in the batch folder> --cue CalloutFight --tool "Google Flow Music"
       --model "Lyria 3.5" --prompt "<what was sent>" [--title "Battle Rhythm"] [--credits 5]
       [--plan "Google AI Plus (Flow Music Starter, commercial use)"] [--take CalloutFight_flow1]

The cue's slot, BPM, key and length come from the batch's sheet.json; check.py and audition.py then treat the take
like any other.
"""
import argparse, json, os, time


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch"); ap.add_argument("file")
    ap.add_argument("--cue", required=True); ap.add_argument("--tool", default="Google Flow Music")
    ap.add_argument("--model", default=""); ap.add_argument("--prompt", required=True)
    ap.add_argument("--title", default=""); ap.add_argument("--credits", type=float)
    ap.add_argument("--plan", default="Google AI Plus (Flow Music Starter, commercial use)")
    ap.add_argument("--take")
    a = ap.parse_args()

    sheet = json.load(open(os.path.join(a.batch, "sheet.json"), encoding="utf-8"))
    cue = next(c for c in sheet["cues"] if c["id"] == a.cue)
    name = os.path.basename(a.file)
    if not os.path.exists(os.path.join(a.batch, name)):
        raise SystemExit(f"{name} is not in {a.batch}: move the download there first")
    take = a.take or os.path.splitext(name)[0]
    path = os.path.join(a.batch, "cues.json")
    book = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else []
    book = [r for r in book if r["take"] != take]
    ext = os.path.splitext(name)[1].lower().lstrip(".")
    book.append({"take": take, "cue": a.cue, "slot": cue["slot"], "tool": a.tool, "model": a.model, "title": a.title,
                 "plan": a.plan, "credits": a.credits, "seed": None, "tags": a.prompt, "bpm": cue["bpm"], "key": cue["key"],
                 "seconds": cue["seconds"], "made": time.strftime("%Y-%m-%d %H:%M"),
                 "files": {ext: name}})
    json.dump(book, open(path, "w", encoding="utf-8"), indent=1)
    print(f"RESULT recorded {take} ({a.tool}) in {path}")


if __name__ == "__main__":
    main()
