"""Instrumental music sketches with ACE-Step 1.5 XL SFT, run hidden in ComfyUI (the same ComfyUI as ultratex / npc-voice).

python -I compose.py <sheet.json> [--only CueA,CueB] [--takes 2] [--dry-run]

The sheet names a batch, the game slots (where a track would play) and the cues (one prompt each, with BPM, key, length
and how many takes). Every take is written to Google Drive, My Drive/KryticalNova/Audio/Music/KN-Originals/<batch>/,
as <cue>_t<n>.flac (lossless, for the game later) and <cue>_t<n>.mp3 (for listening), and gets a line in cues.json
there: the prompt, model, seed and date, so where every track came from is on record. Nothing goes into the game.
"""
import argparse, json, os, shutil, sys, time, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "ultratex", "scripts"))
import run as comfy  # the ultratex driver: start_comfy, get, post, say, log_tail, OUTPUT

MODEL = {"unet": "acestep_v1.5_xl_sft_bf16.safetensors", "clip1": "qwen_0.6b_ace15.safetensors",
         "clip2": "qwen_4b_ace15.safetensors", "vae": "ace_1.5_vae.safetensors"}
# The settings of Comfy-Org's "ACE-Step 1.5 XL SFT" template (audio_ace_step1_5_xl_sft.json).
STEPS, CFG, SHIFT = 50, 7.0, 3.0
INSTRUMENTAL = "[Instrumental]"     # the owner, 20 Oct 2026: no voices anywhere in the music
NO_VOICE_TAGS = "instrumental, no vocals"


def drive_root():
    for letter in "DEFGHIJKLMNOPQRSTUVWXYZ":
        base = os.path.join(letter + ":\\", "My Drive", "KryticalNova", "Audio", "Music")
        if os.path.isdir(base):
            return os.path.join(base, "KN-Originals")
    raise SystemExit("Google Drive (My Drive/KryticalNova/Audio/Music) is not mounted - music files only go to Drive.")


def prompt_for(cue, seed, prefix):
    tags = cue["tags"].strip().rstrip(".")
    if "no vocals" not in tags.lower():
        tags += ", " + NO_VOICE_TAGS
    # The LM picks its own tempo unless the words say it too (Vol1: 96 asked, 120 made).
    tags += f", {int(cue['bpm'])} BPM, {cue['key']}"
    enc = {"clip": ["2", 0], "tags": tags, "lyrics": cue.get("structure") or INSTRUMENTAL, "seed": seed,
           "bpm": int(cue["bpm"]), "duration": float(cue["seconds"]), "timesignature": str(cue.get("beats_per_bar", 4)),
           "language": "en", "keyscale": cue["key"], "generate_audio_codes": True, "cfg_scale": 2.0,
           "temperature": 0.85, "top_p": 1.0, "top_k": 0, "min_p": 0.0}
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": MODEL["unet"], "weight_dtype": "default"}},
        "2": {"class_type": "DualCLIPLoader", "inputs": {"clip_name1": MODEL["clip1"], "clip_name2": MODEL["clip2"], "type": "ace", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": MODEL["vae"]}},
        "4": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["1", 0], "shift": SHIFT}},
        "5": {"class_type": "TextEncodeAceStepAudio1.5", "inputs": enc},
        "6": {"class_type": "EmptyAceStep1.5LatentAudio", "inputs": {"seconds": float(cue["seconds"]), "batch_size": 1}},
        "7": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["5", 0]}},
        "8": {"class_type": "KSampler", "inputs": {"model": ["4", 0], "positive": ["5", 0], "negative": ["7", 0], "latent_image": ["6", 0],
                                                   "seed": seed, "steps": STEPS, "cfg": CFG, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "9": {"class_type": "VAEDecodeAudio", "inputs": {"samples": ["8", 0], "vae": ["3", 0]}},
        "10": {"class_type": "SaveAudio", "inputs": {"audio": ["9", 0], "filename_prefix": prefix}},
        "11": {"class_type": "SaveAudioMP3", "inputs": {"audio": ["9", 0], "filename_prefix": prefix, "quality": "V0"}},
    }



def output_dirs():
    """Where ComfyUI may have written: the Drive folder run.py starts it with, or ComfyUI's own output folder when an
    older start (without --output-directory) is still running."""
    return [d for d in dict.fromkeys([comfy.OUTPUT, os.path.join(comfy.COMFY, "output")]) if os.path.isdir(d)]


def find_made(take):
    """The newest .flac and .mp3 ComfyUI saved for a take (named <take>_<counter>.<ext> under music_gen/<stamp>/)."""
    best = {}
    for root in output_dirs():
        gen = os.path.join(root, "music_gen")
        if not os.path.isdir(gen):
            continue
        for stamp in os.listdir(gen):
            folder = os.path.join(gen, stamp)
            if not os.path.isdir(folder):
                continue
            for name in os.listdir(folder):
                stem, ext = os.path.splitext(name)
                if ext.lower() in (".flac", ".mp3") and stem.rsplit("_", 1)[0] == take:
                    path = os.path.join(folder, name)
                    if ext.lower() not in best or os.path.getmtime(path) > os.path.getmtime(best[ext.lower()]):
                        best[ext.lower()] = path
    return best


def collect(take, out):
    got = {}
    for ext, src in find_made(take).items():
        dst = os.path.join(out, take + ext)
        shutil.copy2(src, dst)
        got[ext.lstrip(".")] = os.path.basename(dst)
    return got


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("sheet")
    ap.add_argument("--only", help="comma-separated cue ids")
    ap.add_argument("--takes", type=int, help="override every cue's take count")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    sheet = json.load(open(a.sheet, encoding="utf-8"))
    cues = [c for c in sheet["cues"] if not a.only or c["id"] in a.only.split(",")]
    out = os.path.join(drive_root(), sheet["batch"])
    os.makedirs(out, exist_ok=True)
    shutil.copy2(a.sheet, os.path.join(out, "sheet.json"))    # the slots and prompts travel with the takes

    book_path = os.path.join(out, "cues.json")
    book = json.load(open(book_path, encoding="utf-8")) if os.path.exists(book_path) else []
    for r in book:      # a take recorded without its files: copy them now if ComfyUI made them
        if not r.get("files"):
            r["files"] = collect(r["take"], out)
            if r["files"]:
                comfy.say(f"  repaired {r['take']}: {', '.join(r['files'].values())}")
    book = [r for r in book if r.get("files")]
    json.dump(book, open(book_path, "w", encoding="utf-8"), indent=1)
    have = {r["take"] for r in book}

    jobs = []
    for c in cues:
        for t in range(1, (a.takes or c.get("takes", 2)) + 1):
            take = f"{c['id']}_t{t}"
            if take in have:
                continue
            jobs.append((c, t, take, int(c.get("seed", 1000)) + 7919 * t))
    if a.dry_run:
        print(json.dumps(prompt_for(jobs[0][0], jobs[0][3], "x"), indent=1) if jobs else "nothing to make")
        print(f"{len(jobs)} takes -> {out}")
        return
    if not jobs:
        comfy.say("Every take of the sheet is already made.")
        return

    comfy.start_comfy()
    stamp = time.strftime("%Y%m%d_%H%M%S")
    queued = []
    for c, t, take, seed in jobs:
        res = comfy.post("/prompt", {"prompt": prompt_for(c, seed, f"music_gen/{stamp}/{take}"), "client_id": str(uuid.uuid4())})
        if res.get("node_errors"):
            raise SystemExit("Node errors: " + json.dumps(res["node_errors"], indent=1))
        queued.append((c, t, take, seed, res["prompt_id"]))
    comfy.say(f"Queued {len(queued)} takes ({sum(float(c['seconds']) for c, *_ in queued) / 60:.1f} min of music). Reporting every 15 s.")

    t0, done = time.time(), set()
    while len(done) < len(queued):
        time.sleep(15)
        for c, t, take, seed, pid in queued:
            if pid in done:
                continue
            h = comfy.get(f"/history/{pid}")
            if pid not in h:
                continue
            entry = h[pid]
            if entry.get("status", {}).get("status_str") == "error":
                msgs = [m for m in entry["status"].get("messages", []) if m[0] == "execution_error"]
                raise SystemExit(f"{take} failed: " + json.dumps(msgs)[:2000])
            if not entry.get("status", {}).get("completed", True):
                continue
            done.add(pid)
            got = collect(take, out)
            if not got:
                raise SystemExit(f"{take}: ComfyUI finished but its files are in none of {output_dirs()}")
            book.append({"take": take, "cue": c["id"], "slot": c["slot"], "tool": "ACE-Step 1.5 XL SFT (local ComfyUI)",
                         "model": MODEL["unet"], "licence": "MIT", "seed": seed, "steps": STEPS, "cfg": CFG,
                         "tags": c["tags"], "bpm": c["bpm"], "key": c["key"], "seconds": c["seconds"],
                         "made": time.strftime("%Y-%m-%d %H:%M"), "files": got})
            json.dump(book, open(book_path, "w", encoding="utf-8"), indent=1)
            comfy.say(f"  {len(done)}/{len(queued)} done: {take} ({int(time.time() - t0)} s)")
        if len(done) < len(queued):
            comfy.say(f"  running {int(time.time() - t0)} s: {comfy.log_tail()}")

    comfy.say(f"All {len(queued)} takes in {int(time.time() - t0)} s -> {out}")
    print("RESULT " + json.dumps({"out": out, "takes": len(queued)}))


if __name__ == "__main__":
    main()
