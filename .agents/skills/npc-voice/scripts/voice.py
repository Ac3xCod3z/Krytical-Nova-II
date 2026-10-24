"""NPC voice lines with Qwen3-TTS VoiceDesign, run hidden in ComfyUI (the same ComfyUI as the ultratex skill).

python -I voice.py <lines.csv> --out <folder> [--seed-base 1000]
python -I voice.py --npc Armorer --voice A --instruct "<voice description>" --text "<line>" --out <folder>

lines.csv columns: id, npc, voice, instruct, text[, seed]. One clip per row: <out>/<id>.mp3 (for listening) and
<out>/<id>.flac (lossless, for the game later). Every clip of one npc+voice uses the same seed, so the voice holds.
Writes <out>/clips.json (what was made) for the audition page. Nothing goes into the game.
"""
import argparse, csv, json, os, shutil, sys, time, uuid, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "ultratex", "scripts"))
import run as comfy  # the ultratex driver: start_comfy, get, post, say, log_tail, COMFY

NODE = "FB_Qwen3TTSVoiceDesign"


def prompt_for(row, prefix):
    tts = {"text": row["text"], "instruct": row["instruct"], "model_choice": "1.7B", "device": "auto",
           "precision": "bf16", "language": "English", "seed": int(row["seed"]), "max_new_tokens": 2048,
           "top_p": 1.0, "top_k": 50, "temperature": 0.9, "repetition_penalty": 1.05, "attention": "sdpa",
           "unload_model_after_generate": False}
    return {"1": {"class_type": NODE, "inputs": tts},
            "2": {"class_type": "SaveAudioMP3", "inputs": {"audio": ["1", 0], "filename_prefix": prefix, "quality": "V0"}},
            "3": {"class_type": "SaveAudio", "inputs": {"audio": ["1", 0], "filename_prefix": prefix}}}


def files_of(entry):
    out = []

    def walk(x):
        if isinstance(x, dict):
            if "filename" in x:
                out.append(x)
            for v in x.values():
                walk(v)
        elif isinstance(x, list):
            for v in x:
                walk(v)
    walk(entry.get("outputs", {}))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("csv", nargs="?")
    ap.add_argument("--out", required=True)
    ap.add_argument("--npc"); ap.add_argument("--voice", default="A"); ap.add_argument("--instruct"); ap.add_argument("--text")
    ap.add_argument("--seed-base", type=int, default=1000)
    a = ap.parse_args()

    if a.csv:
        rows = list(csv.DictReader(open(a.csv, encoding="utf-8")))
    else:
        rows = [{"id": f"{a.npc}_{a.voice}_{int(time.time())}", "npc": a.npc, "voice": a.voice, "instruct": a.instruct, "text": a.text}]
    for r in rows:
        if not r.get("seed"):
            # Same npc + voice -> same seed, so one character keeps one voice across its lines.
            r["seed"] = a.seed_base + zlib.crc32(f"{r['npc']}|{r['voice']}".encode()) % 100000
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    batch = time.strftime("%Y%m%d_%H%M%S")

    comfy.start_comfy()
    queued = []
    for r in rows:
        res = comfy.post("/prompt", {"prompt": prompt_for(r, f"npc_voice/{batch}/{r['id']}"), "client_id": str(uuid.uuid4())})
        if res.get("node_errors"):
            raise SystemExit("Node errors: " + json.dumps(res["node_errors"], indent=1))
        queued.append((r, res["prompt_id"]))
    comfy.say(f"Queued {len(queued)} lines. Reporting every 10 s.")

    t0, done, made = time.time(), set(), []
    while len(done) < len(queued):
        time.sleep(10)
        for r, pid in queued:
            if pid in done:
                continue
            h = comfy.get(f"/history/{pid}")
            if pid not in h:
                continue
            entry = h[pid]
            if entry.get("status", {}).get("status_str") == "error":
                msgs = [m for m in entry["status"].get("messages", []) if m[0] == "execution_error"]
                raise SystemExit(f"{r['id']} failed: " + json.dumps(msgs)[:2000])
            done.add(pid)
            got = {}
            for f in files_of(entry):
                src = os.path.join(comfy.COMFY, "output", f.get("subfolder", ""), f["filename"])
                ext = os.path.splitext(f["filename"])[1].lower()
                dst = os.path.join(out, r["id"] + ext)
                shutil.copy2(src, dst)
                got[ext.lstrip(".")] = os.path.basename(dst)
            made.append({**r, "files": got})
            comfy.say(f"  {len(done)}/{len(queued)} done: {r['id']} ({', '.join(got.values())})")
        if len(done) < len(queued):
            comfy.say(f"  running {int(time.time() - t0)} s: {comfy.log_tail()}")

    json.dump(made, open(os.path.join(out, "clips.json"), "w", encoding="utf-8"), indent=1)
    comfy.say(f"All {len(made)} lines in {int(time.time() - t0)} s -> {out}")
    print("RESULT " + json.dumps({"out": out, "clips": len(made)}))


if __name__ == "__main__":
    main()
