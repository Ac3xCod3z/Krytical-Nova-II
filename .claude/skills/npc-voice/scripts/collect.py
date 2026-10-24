"""Gather a batch's finished clips from ComfyUI's output folder by name, without generating anything.

python -I collect.py <lines.csv> <batch> --out <folder>

<batch> is the npc_voice/<batch> subfolder voice.py queued into (printed as "npc_voice/<batch>/..."). Use it when
voice.py stopped while ComfyUI kept working through the queue: every <id>_NNNNN.mp3/.flac there is copied to
<out>/<id>.mp3/.flac (newest take wins) and clips.json is written. Lists the ids still missing.
"""
import argparse, csv, glob, json, os, shutil, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "ultratex", "scripts"))
import run as comfy


def copy(src, dst):
    for attempt in range(5):  # G: is a streamed Google Drive: a copy can fail for a moment
        try:
            shutil.copy2(src, dst)
            return
        except OSError:
            if attempt == 4:
                raise
            time.sleep(2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("csv"); ap.add_argument("batch"); ap.add_argument("--out", required=True)
    a = ap.parse_args()
    out_root = comfy.running_output() if comfy.comfy_up() else comfy.OUTPUT
    src_dir = os.path.join(out_root, "npc_voice", a.batch)
    os.makedirs(a.out, exist_ok=True)
    made, missing = [], []
    for r in csv.DictReader(open(a.csv, encoding="utf-8")):
        got = {}
        for ext in ("mp3", "flac"):
            takes = sorted(glob.glob(os.path.join(src_dir, f"{r['id']}_*.{ext}")), key=os.path.getmtime)
            if takes:
                copy(takes[-1], os.path.join(a.out, f"{r['id']}.{ext}"))
                got[ext] = f"{r['id']}.{ext}"
        (made if "mp3" in got else missing).append({**r, "files": got} if got else r["id"])
    json.dump(made, open(os.path.join(a.out, "clips.json"), "w", encoding="utf-8"), indent=1)
    comfy.say(f"Collected {len(made)} clips from {src_dir} -> {a.out}; missing {len(missing)}")
    if missing:
        print("MISSING " + ", ".join(missing))


if __name__ == "__main__":
    main()
