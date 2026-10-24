"""Image-to-video with Wan 2.2 TI2V 5B, run hidden in ComfyUI (the same ComfyUI as the ultratex skill).

python -I wan.py <picture> --prompt "<what moves and how>" --name <Name_v01> --out <folder>
                 [--seconds 5] [--size auto|1280x704|704x1280|960x960] [--steps 20] [--cfg 5] [--seed N] [--shift 8]

The picture is the first frame; the prompt says what happens after it. 24 fps; length is rounded to Wan's 4n+1 frames.
Writes <out>/<name>.mp4 (and <name>_first.png, the first frame as Wan saw it). Frames are saved and encoded with
ffmpeg, so the result never depends on ComfyUI's video encoder settings. Nothing goes into the game.
"""
import argparse, glob, json, os, shutil, subprocess, sys, time, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
SKILL = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(SKILL, "..", "ultratex", "scripts"))
import run as comfy  # the ultratex driver: start_comfy, get, post, say, log_tail, COMFY

NEGATIVE = open(os.path.join(SKILL, "negative.txt"), encoding="utf-8").read()
FPS = 24


def pick_size(image, size):
    if size != "auto":
        w, h = (int(v) for v in size.lower().split("x"))
        return w, h
    from struct import unpack
    with open(image, "rb") as f:  # PNG / JPEG header read, no imaging library needed
        head = f.read(26)
        if head[:8] == b"\x89PNG\r\n\x1a\n":
            iw, ih = unpack(">II", head[16:24])
        else:
            f.seek(0)
            data = f.read()
            i = 2
            while i < len(data):
                if data[i] != 0xFF:
                    i += 1
                    continue
                marker = data[i + 1]
                if marker in (0xC0, 0xC1, 0xC2):
                    ih, iw = unpack(">HH", data[i + 5:i + 9])
                    break
                i += 2 + unpack(">H", data[i + 2:i + 4])[0]
            else:
                iw, ih = 1280, 704
    area = 1280 * 704  # the 5B model's native size; keep the picture's shape at that many pixels
    ratio = iw / ih
    w = int(round((area * ratio) ** 0.5 / 32) * 32)
    h = int(round((area / ratio) ** 0.5 / 32) * 32)
    return max(w, 256), max(h, 256)


def graph(img_in, prompt, w, h, frames, a, prefix):
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "wan2.2_ti2v_5B_fp16.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "wan2.2_vae.safetensors"}},
        "4": {"class_type": "LoadImage", "inputs": {"image": img_in}},
        "5": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["1", 0], "shift": a.shift}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": NEGATIVE}},
        "8": {"class_type": "Wan22ImageToVideoLatent", "inputs": {"vae": ["3", 0], "width": w, "height": h, "length": frames,
                                                                   "batch_size": 1, "start_image": ["4", 0]}},
        "9": {"class_type": "KSampler", "inputs": {"model": ["5", 0], "seed": a.seed, "steps": a.steps, "cfg": a.cfg,
                                                   "sampler_name": "uni_pc", "scheduler": "simple", "positive": ["6", 0],
                                                   "negative": ["7", 0], "latent_image": ["8", 0], "denoise": 1.0}},
        "10": {"class_type": "VAEDecode", "inputs": {"samples": ["9", 0], "vae": ["3", 0]}},
        "11": {"class_type": "SaveImage", "inputs": {"images": ["10", 0], "filename_prefix": prefix}},
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image"); ap.add_argument("--prompt", required=True)
    ap.add_argument("--name", required=True); ap.add_argument("--out", required=True)
    ap.add_argument("--seconds", type=float, default=5.0); ap.add_argument("--size", default="auto")
    ap.add_argument("--steps", type=int, default=20); ap.add_argument("--cfg", type=float, default=5.0)
    ap.add_argument("--seed", type=int, default=1); ap.add_argument("--shift", type=float, default=8.0)
    ap.add_argument("--keep-open", action="store_true", help="leave ComfyUI running (only between back-to-back runs)")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    w, h = pick_size(a.image, a.size)
    frames = int(round(a.seconds * FPS / 4)) * 4 + 1
    comfy.say(f"{a.name}: {w}x{h}, {frames} frames ({frames / FPS:.1f} s at {FPS} fps), {a.steps} steps")

    comfy.start_comfy()
    img_in = f"{a.name}_start{os.path.splitext(a.image)[1]}"
    shutil.copy2(a.image, os.path.join(comfy.COMFY, "input", img_in))
    prefix = f"wan/{a.name}/frame"
    res = comfy.post("/prompt", {"prompt": graph(img_in, a.prompt, w, h, frames, a, prefix), "client_id": str(uuid.uuid4())})
    if res.get("node_errors"):
        raise SystemExit("Node errors: " + json.dumps(res["node_errors"], indent=1))
    pid = res["prompt_id"]
    comfy.say(f"Queued (prompt {pid[:8]}). Reporting every 10 s.")
    t0 = time.time()
    while True:
        time.sleep(10)
        hist = comfy.get(f"/history/{pid}")
        if pid in hist:
            st = hist[pid].get("status", {})
            if st.get("status_str") == "error":
                msgs = [m for m in st.get("messages", []) if m[0] == "execution_error"]
                raise SystemExit("ComfyUI run failed: " + json.dumps(msgs)[:3000])
            break
        comfy.say(f"  running {int(time.time() - t0)} s: {comfy.log_tail()}")
    comfy.say(f"Frames done in {int(time.time() - t0)} s. Encoding the MP4...")

    imgs = hist[pid]["outputs"]["11"]["images"]
    src_dir = os.path.join(comfy.OUTPUT, imgs[0].get("subfolder", ""))
    names = sorted(i["filename"] for i in imgs)
    work = os.path.join(out, f"_{a.name}_frames")
    os.makedirs(work, exist_ok=True)
    for k, n in enumerate(names):
        shutil.copy2(os.path.join(src_dir, n), os.path.join(work, f"{k:05d}.png"))
    shutil.copy2(os.path.join(work, "00000.png"), os.path.join(out, f"{a.name}_first.png"))
    mp4 = os.path.join(out, f"{a.name}.mp4")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-i", os.path.join(work, "%05d.png"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "16", "-preset", "slow", "-movflags", "+faststart", mp4],
                   check=True)
    shutil.rmtree(work)
    comfy.say(f"Saved {mp4} ({len(names)} frames)")
    if not a.keep_open:
        comfy.stop_comfy()  # owner, 23 Oct 2026: never leave ComfyUI holding the card
    print("RESULT " + json.dumps({"mp4": mp4, "frames": len(names), "size": [w, h], "seconds": round(len(names) / FPS, 2)}))


if __name__ == "__main__":
    main()
