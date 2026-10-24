"""One character, one voice (owner, 23 Oct 2026): design each voice ONCE, then clone every line from that sample.

python -I clone.py <batch.json> --out <folder> [--only Npc,Npc] [--lines-only] [--redo id,id --seed-shift N] [--keep-open]

Stage 1 (VoiceDesign 1.7B): each npc+voice says its "sample" line -> <out>/samples/<Npc>_<V>.flac/.mp3. The sample
is the voice; it is copied into ComfyUI/input as kn_ref_<Npc>_<V>.flac.
Stage 2 (VoiceClone, 1.7B Base): every line is spoken by cloning that sample (ref audio + its text) ->
<out>/<Npc>_<V>_<n>.mp3/.flac. Same seed for every line of a voice.
Then run sv_check.py to prove every line is the same speaker as its sample; remake failures with --redo.
"""
import argparse, json, os, shutil, sys, time, uuid, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "ultratex", "scripts"))
import run as comfy

COMMON = {"device": "auto", "precision": "bf16", "language": "English", "max_new_tokens": 2048, "top_p": 1.0,
          "top_k": 50, "temperature": 0.9, "repetition_penalty": 1.05, "attention": "sdpa",
          "unload_model_after_generate": False}


def seed_of(npc, voice, shift=0):
    return 1000 + zlib.crc32(f"{npc}|{voice}".encode()) % 100000 + shift


def copy(src, dst):
    for attempt in range(5):  # G: is a streamed Google Drive: a copy can fail for a moment
        try:
            shutil.copy2(src, dst)
            return
        except OSError:
            if attempt == 4:
                raise
            time.sleep(2)


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


def run_jobs(jobs, label):
    """jobs: list of (key, prompt, dest_base). Queues all, waits, copies <dest_base>.mp3/.flac. Returns done keys."""
    queued = []
    for key, prompt, dest in jobs:
        r = comfy.post("/prompt", {"prompt": prompt, "client_id": str(uuid.uuid4())})
        if r.get("node_errors"):
            raise SystemExit(f"{key}: node errors " + json.dumps(r["node_errors"])[:1500])
        queued.append((key, r["prompt_id"], dest))
    comfy.say(f"{label}: queued {len(queued)}. Reporting every 10 s.")
    t0, done = time.time(), set()
    while len(done) < len(queued):
        time.sleep(10)
        for key, pid, dest in queued:
            if pid in done:
                continue
            h = comfy.get(f"/history/{pid}")
            if pid not in h:
                continue
            e = h[pid]
            if e.get("status", {}).get("status_str") == "error":
                msgs = [m for m in e["status"].get("messages", []) if m[0] == "execution_error"]
                raise SystemExit(f"{key} failed: " + json.dumps(msgs)[:1500])
            for f in files_of(e):
                src = os.path.join(comfy.OUTPUT, f.get("subfolder", ""), f["filename"])
                copy(src, dest + os.path.splitext(f["filename"])[1].lower())
            done.add(pid)
        comfy.say(f"  {label}: {len(done)}/{len(queued)} in {int(time.time() - t0)} s")
    return [k for k, _, _ in queued]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch"); ap.add_argument("--out", required=True)
    ap.add_argument("--only", default=""); ap.add_argument("--lines-only", action="store_true")
    ap.add_argument("--redo", default="", help="comma list of line ids (Npc_V_n) to make again")
    ap.add_argument("--seed-shift", type=int, default=0)
    ap.add_argument("--cand", action="store_true", help="redo lines go to <out>/candidates/<id>_s<shift> for best_take.py")
    ap.add_argument("--keep-open", action="store_true")
    ap.add_argument("--voices", default="", help="only these voice letters, e.g. B (samples and lines)")
    a = ap.parse_args()
    data = json.load(open(a.batch, encoding="utf-8"))["vendors"]
    only = set(filter(None, a.only.split(",")))
    redo = set(filter(None, a.redo.split(",")))
    vsel = set(filter(None, a.voices.split(",")))
    vendors = [v for v in data if not only or v["npc"] in only]
    out = os.path.abspath(a.out)
    samples = os.path.join(out, "samples")
    os.makedirs(samples, exist_ok=True)
    batch = time.strftime("%Y%m%d_%H%M%S")
    comfy.start_comfy()
    inp = os.path.join(comfy.COMFY, "input")

    # Stage 1: one designed sample per voice.
    if not a.lines_only and not redo:
        jobs = []
        for v in vendors:
            for k, instruct in v["voices"].items():
                if vsel and k not in vsel:
                    continue
                tts = {"text": v["sample"], "instruct": instruct, "model_choice": "1.7B", "seed": seed_of(v["npc"], k), **COMMON}
                prompt = {"1": {"class_type": "FB_Qwen3TTSVoiceDesign", "inputs": tts},
                          "2": {"class_type": "SaveAudioMP3", "inputs": {"audio": ["1", 0], "filename_prefix": f"npc_voice/{batch}/sample_{v['npc']}_{k}", "quality": "V0"}},
                          "3": {"class_type": "SaveAudio", "inputs": {"audio": ["1", 0], "filename_prefix": f"npc_voice/{batch}/sample_{v['npc']}_{k}"}}}
                jobs.append((f"{v['npc']}_{k}", prompt, os.path.join(samples, f"{v['npc']}_{k}")))
        run_jobs(jobs, "Samples")

    # Every sample goes into ComfyUI's input folder for LoadAudio.
    for v in vendors:
        for k in v["voices"]:
            src = os.path.join(samples, f"{v['npc']}_{k}.flac")
            if not os.path.exists(src):
                raise SystemExit(f"No sample for {v['npc']}_{k}: run without --lines-only first")
            shutil.copyfile(src, os.path.join(inp, f"kn_ref_{v['npc']}_{k}.flac"))

    # Stage 2: clone every line from its voice's sample.
    jobs = []
    for v in vendors:
        for k in v["voices"]:
            if vsel and k not in vsel:
                continue
            for i, text in enumerate(v["lines"], 1):
                text = v.get("spoken", {}).get(str(i), text)  # how the voice says it (owner pronunciation notes)
                cid = f"{v['npc']}_{k}_{i}"
                if redo and cid not in redo:
                    continue
                clone = {"target_text": text, "model_choice": "1.7B", "ref_audio": ["1", 0], "ref_text": v["sample"],
                         "seed": seed_of(v["npc"], k, a.seed_shift), "x_vector_only": False, "instruct": "",
                         "custom_model_path": "", **COMMON}
                prompt = {"1": {"class_type": "LoadAudio", "inputs": {"audio": f"kn_ref_{v['npc']}_{k}.flac"}},
                          "2": {"class_type": "FB_Qwen3TTSVoiceClone", "inputs": clone},
                          "3": {"class_type": "SaveAudioMP3", "inputs": {"audio": ["2", 0], "filename_prefix": f"npc_voice/{batch}/{cid}", "quality": "V0"}},
                          "4": {"class_type": "SaveAudio", "inputs": {"audio": ["2", 0], "filename_prefix": f"npc_voice/{batch}/{cid}"}}}
                dest = os.path.join(out, cid)
                if a.cand:
                    os.makedirs(os.path.join(out, "candidates"), exist_ok=True)
                    dest = os.path.join(out, "candidates", f"{cid}_s{a.seed_shift}")
                jobs.append((f"{cid}_s{a.seed_shift}", prompt, dest))
    if jobs:
        run_jobs(jobs, "Lines")
    if not a.keep_open:
        comfy.stop_comfy()
    print("RESULT " + json.dumps({"out": out, "lines": len(jobs)}))


if __name__ == "__main__":
    main()
