"""UltraTex texturing, driven from the command line (ComfyUI runs hidden in the background).

python -I run.py <mesh> <reference.png> --name KitsuneFox_UltraTex_v01 --out <folder>
                 [--res 1024] [--bake 2048] [--seed 0] [--yaw 0] [--keep-uvs]
                 [--set NODE_ID.input=value ...] [--no-open] [--dry-run]

Steps: start ComfyUI if it is not up -> prep the mesh to GLB (Blender) -> copy mesh + picture into
ComfyUI/input -> turn the installed workflow into an API prompt -> queue it -> report progress every
10 s -> copy the GLB + maps into --out -> build the .blend + review sheet (Blender) -> open Blender.
"""
import argparse, json, os, shutil, subprocess, sys, time, uuid, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
SKILL = os.path.dirname(HERE)
CFG = json.load(open(os.path.join(SKILL, "config.json"), encoding="utf-8"))
ROOT = CFG["comfy_root"]
COMFY = os.path.join(ROOT, "ComfyUI")
URL = f"http://127.0.0.1:{CFG['port']}"
WORK = os.path.join(SKILL, ".work")


def _drive_outputs():
    """Owner, 20 Oct 2026: the AI results live in Google Drive (My Drive/KryticalNova/AI Outputs); the models stay in C:/AI."""
    for letter in "DEFGHIJKLMNOPQRSTUVWXYZ":
        base = os.path.join(letter + ":\\", "My Drive", "KryticalNova")
        if os.path.isdir(base):
            return os.path.join(base, "AI Outputs", "ComfyUI")
    return None


# Where ComfyUI writes what it makes (--output-directory): Google Drive when it is there, else ComfyUI's own output folder.
OUTPUT = CFG.get("output_dir") or _drive_outputs() or os.path.join(COMFY, "output")
LOG = os.path.join(WORK, "comfyui.log")
os.makedirs(WORK, exist_ok=True)
sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # ComfyUI logs emoji; the Windows console code page cannot


def say(msg):
    print(time.strftime("[%H:%M:%S] ") + msg, flush=True)


def get(path, tries=4):
    for i in range(tries):
        try:
            with urllib.request.urlopen(URL + path, timeout=60) as r:
                return json.loads(r.read())
        except (ConnectionError, urllib.error.URLError, TimeoutError):
            if i == tries - 1:
                raise
            time.sleep(3)


def post(path, data):
    req = urllib.request.Request(URL + path, json.dumps(data).encode(), {"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        raise SystemExit("ComfyUI refused the prompt:\n" + e.read().decode(errors="replace"))


def log_tail():
    try:
        with open(LOG, "rb") as f:
            f.seek(max(0, os.path.getsize(LOG) - 4000))
            lines = [l for l in f.read().decode(errors="replace").splitlines() if l.strip()]
        return lines[-1][-160:] if lines else ""
    except OSError:
        return ""


def comfy_up():
    try:
        get("/system_stats")
        return True
    except Exception:
        return False


def running_output():
    """Where the ComfyUI that is already up really writes: its own --output-directory, else its default output folder.
    A ComfyUI started by another session or the EZi launcher may not use the Drive folder this file would pass."""
    try:
        argv = get("/system_stats")["system"].get("argv", [])
    except Exception:
        return None
    if "--output-directory" in argv:
        p = argv[argv.index("--output-directory") + 1]
        return p if os.path.isabs(p) else os.path.join(ROOT, p)
    return os.path.join(COMFY, "output")


def stop_comfy(force=False):
    """Owner, 23 Oct 2026: shut ComfyUI down when a job is done, so it does not sit holding VRAM and RAM.
    Stops every ComfyUI main.py of this install (whoever started it) once its queue is empty; with force, even if not."""
    if not force and comfy_up():
        try:
            q = get("/queue")
            if q.get("queue_running") or q.get("queue_pending"):
                say("ComfyUI still has work queued - left running.")
                return False
        except Exception:
            pass
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
          "Where-Object { $_.CommandLine -match 'ComfyUI\\\\main.py' } | ForEach-Object { $_.ProcessId }")
    pids = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout.split()
    for pid in pids:
        subprocess.run(["taskkill", "/PID", pid, "/T", "/F"], capture_output=True)
    say(f"ComfyUI stopped ({len(pids)} process{'es' if len(pids) != 1 else ''})." if pids else "ComfyUI was not running.")
    return True


def start_comfy():
    global OUTPUT
    if comfy_up():
        OUTPUT = running_output() or OUTPUT
        say(f"ComfyUI is already running (writes to {OUTPUT}).")
        return
    py = os.path.join(ROOT, "python_embeded", "python.exe")
    args = [py, "-s", os.path.join(COMFY, "main.py"), "--windows-standalone-build", "--listen", "127.0.0.1",
            "--port", str(CFG["port"]), "--max-upload-size", "2048", "--output-directory", OUTPUT] + CFG.get("extra_args", [])
    os.makedirs(OUTPUT, exist_ok=True)
    say("Starting ComfyUI in the background (log: .work/comfyui.log)...")
    log = open(LOG, "ab")
    subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT,
                     creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    t0 = time.time()
    while not comfy_up():
        if time.time() - t0 > 600:
            raise SystemExit("ComfyUI did not come up in 10 minutes - see .work/comfyui.log")
        time.sleep(10)
        say(f"  waiting for ComfyUI ({int(time.time() - t0)} s): {log_tail()}")
    s = get("/system_stats")
    dev = s.get("devices", [{}])[0]
    say(f"ComfyUI up: {dev.get('name')}, VRAM {round(dev.get('vram_total', 0) / 2**30, 1)} GB")


def blender(script, *args):
    cmd = [CFG["blender"], "-b", "--factory-startup", "--python", os.path.join(HERE, script), "--", *args]
    out = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
    keep = [l for l in out.stdout.splitlines() if l.startswith(("PREP_INFO", "BLEND_SAVED", "SHEET_SAVED"))]
    if out.returncode or "Traceback" in out.stdout + out.stderr:
        raise SystemExit(f"Blender {script} failed:\n" + (out.stdout + out.stderr)[-3000:])
    return keep


# ---------------------------------------------------------------- UI workflow -> API prompt
WIDGET_TYPES = {"INT", "FLOAT", "STRING", "BOOLEAN", "COMBO", "LOAD_3D"}  # LOAD_3D: the 3D loader's viewport widget
DROP = ("Note", "MarkdownNote", "Preview3D", "Preview3DAdvanced", "Reroute")


def to_api(ui, info):
    nodes = {n["id"]: n for n in ui["nodes"]}
    links = {l[0]: l for l in ui["links"]}  # id, from_node, from_slot, to_node, to_slot, type

    def source(link_id):
        l = links[link_id]
        n = nodes[l[1]]
        if n["type"] == "Reroute":
            return source(n["inputs"][0]["link"])
        return [str(l[1]), l[2]]

    api = {}
    for n in ui["nodes"]:
        if n["type"] in DROP or n.get("mode") in (2, 4):
            continue
        spec = info.get(n["type"])
        if spec is None:
            raise SystemExit(f"Node type {n['type']} is not installed in ComfyUI.")
        req, opt = spec["input"].get("required", {}), spec["input"].get("optional", {})
        order = spec.get("input_order", {}).get("required", list(req)) + spec.get("input_order", {}).get("optional", list(opt))
        allspec = {**req, **opt}
        linked = {i["name"]: i["link"] for i in n.get("inputs", []) if i.get("link") is not None}
        wv = n.get("widgets_values") or []
        inputs, k = {}, 0
        for name in order:
            s = allspec[name]
            t = s[0]
            is_widget = isinstance(t, list) or t in WIDGET_TYPES
            if is_widget:
                if isinstance(wv, dict):
                    val = wv.get(name)
                elif k < len(wv):
                    val = wv[k]
                    k += 1
                    extra = s[1] if len(s) > 1 and isinstance(s[1], dict) else {}
                    if extra.get("control_after_generate") or (t == "INT" and name in ("seed", "noise_seed")):
                        k += 1  # skip the "fixed / randomize" control value
                else:
                    val = None
                if val is not None:
                    inputs[name] = val
            if name in linked:
                inputs[name] = source(linked[name])
        api[str(n["id"])] = {"class_type": n["type"], "inputs": inputs, "_meta": {"title": n.get("title") or n["type"]}}
    # Drop inputs that point at dropped nodes.
    for node in api.values():
        for key, v in list(node["inputs"].items()):
            if isinstance(v, list) and len(v) == 2 and isinstance(v[0], str) and v[0] not in api:
                del node["inputs"][key]
    return api


def set_by_class(api, cls, name, value):
    hit = [k for k, v in api.items() if v["class_type"] == cls]
    for k in hit:
        api[k]["inputs"][name] = value
    return hit


def collect(entry, out_dir, name):
    files = []

    def walk(x):
        if isinstance(x, dict):
            if "filename" in x:
                files.append(x)
            for v in x.values():
                walk(v)
        elif isinstance(x, list):
            for v in x:
                walk(v)
    walk(entry.get("outputs", {}))
    got = {}
    for f in files:
        if f.get("type", "output") != "output":
            continue
        src = os.path.join(OUTPUT, f.get("subfolder", ""), f["filename"])
        low = f["filename"].lower()
        if low.endswith(".glb"):
            dst = f"{name}_textured.glb"; key = "glb"
        elif "base_color" in low or "albedo" in low:
            dst = f"T_{name}_Albedo.png"; key = "albedo"
        elif "rough" in low:
            dst = f"T_{name}_Roughness.png"; key = "roughness"
        elif "metal" in low:
            dst = f"T_{name}_Metallic.png"; key = "metallic"
        elif "orm" in low:
            dst = f"T_{name}_ORM.png"; key = "orm"
        else:
            continue
        shutil.copy2(src, os.path.join(out_dir, dst))
        got[key] = os.path.join(out_dir, dst)
    return got


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mesh"); ap.add_argument("image")
    ap.add_argument("--name", required=True); ap.add_argument("--out", required=True)
    ap.add_argument("--res", type=int, default=1024, choices=[1024, 2048, 4096], help="view resolution (1024 test, 2048 final)")
    ap.add_argument("--bake", type=int, default=2048, choices=[1024, 2048, 4096], help="texture size")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--yaw", type=float, default=0.0)
    ap.add_argument("--keep-uvs", action="store_true")
    ap.add_argument("--set", action="append", default=[], help="NODE_ID.input=value (JSON value)")
    ap.add_argument("--no-open", action="store_true"); ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--keep-open", action="store_true", help="leave ComfyUI running (only between back-to-back runs)")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)

    say("Preparing the mesh in Blender...")
    glb = os.path.join(out, f"{a.name}_untextured.glb")
    prep_args = [os.path.abspath(a.mesh), glb, "--yaw", str(a.yaw)] + (["--keep-uvs"] if a.keep_uvs else [])
    for l in blender("prep_mesh.py", *prep_args):
        say("  " + l)

    start_comfy()
    os.makedirs(os.path.join(COMFY, "input", "3d"), exist_ok=True)
    mesh_in = f"{a.name}.glb"
    img_in = f"{a.name}_ref{os.path.splitext(a.image)[1]}"
    shutil.copy2(glb, os.path.join(COMFY, "input", "3d", mesh_in))
    shutil.copy2(a.image, os.path.join(COMFY, "input", img_in))

    wf_path = os.path.join(COMFY, "user", "default", "workflows", "PixelArtistry", CFG["workflow"])
    if not os.path.exists(wf_path):
        wf_path = CFG["workflow_fallback"]
    ui = json.load(open(wf_path, encoding="utf-8"))
    api = to_api(ui, get("/object_info"))
    set_by_class(api, "Load3DAdvanced", "model_file", "3d/" + mesh_in)
    set_by_class(api, "LoadImage", "image", img_in)
    set_by_class(api, "UnetLoaderGGUF", "unet_name", CFG["unet"])
    if CFG.get("clip"):
        # The GGUF loader misreads the fp8 Qwen3-8B .safetensors as the 4B shape; the stock loader
        # (what the Standard workflow uses for this file) reads it right.
        for n in api.values():
            if n["class_type"] == "CLIPLoaderGGUF":
                if CFG["clip"].endswith(".gguf"):
                    n["inputs"]["clip_name"] = CFG["clip"]
                else:
                    n["class_type"] = "CLIPLoader"
                    n["inputs"] = {"clip_name": CFG["clip"], "type": "flux2", "device": "default"}
    for k, n in api.items():
        for inp in list(n["inputs"]):
            if inp in CFG["res_inputs"]:
                n["inputs"][inp] = a.res
            if inp in CFG["bake_inputs"]:
                n["inputs"][inp] = a.bake
            if inp in CFG["seed_inputs"]:
                n["inputs"][inp] = a.seed
        if n["class_type"] in CFG.get("uv_classes", []) and a.keep_uvs:
            n["inputs"][CFG["uv_mode_input"]] = "keep_existing"
        if n["class_type"] in ("SaveGLB", "SaveImage"):
            pre = n["inputs"].get("filename_prefix", "ultratex/out")
            n["inputs"]["filename_prefix"] = f"ultratex/{a.name}/" + os.path.basename(pre)
    for s in a.set:
        key, val = s.split("=", 1)
        nid, inp = key.split(".", 1)
        try:
            val = json.loads(val)
        except ValueError:
            pass
        api[nid]["inputs"][inp] = val
    json.dump(api, open(os.path.join(WORK, "last_prompt.json"), "w"), indent=1)
    if a.dry_run:
        say("Dry run: prompt written to .work/last_prompt.json")
        return

    r = post("/prompt", {"prompt": api, "client_id": str(uuid.uuid4())})
    if r.get("node_errors"):
        raise SystemExit("Node errors: " + json.dumps(r["node_errors"], indent=1))
    pid = r["prompt_id"]
    say(f"Queued (prompt {pid[:8]}). Reporting every 10 s.")
    t0 = time.time()
    while True:
        time.sleep(10)
        h = get(f"/history/{pid}")
        if pid in h:
            entry = h[pid]
            st = entry.get("status", {})
            if st.get("status_str") == "error":
                msgs = [m for m in st.get("messages", []) if m[0] == "execution_error"]
                raise SystemExit("ComfyUI run failed: " + json.dumps(msgs, indent=1)[:3000])
            break
        say(f"  running {int(time.time() - t0)} s: {log_tail()}")
    say(f"Finished in {int(time.time() - t0)} s. Collecting files...")
    got = collect(entry, out, a.name)
    say("  " + ", ".join(f"{k}: {os.path.basename(v)}" for k, v in got.items()))
    if "glb" not in got:
        raise SystemExit("No GLB came out - check .work/comfyui.log")

    blend = os.path.join(out, f"{a.name}.blend")
    sheet = os.path.join(out, f"{a.name}_sheet.png")
    extra = ["--yaw", str(a.yaw)]
    for k in ("albedo", "roughness", "metallic"):
        if k in got:
            extra += [f"--{k}", got[k]]
    for l in blender("to_blend.py", got["glb"], blend, sheet, *extra):
        say("  " + l)
    if not a.no_open:
        subprocess.Popen([CFG["blender"], blend])
        say("Opened in Blender.")
    if not a.keep_open:
        stop_comfy()
    print("RESULT " + json.dumps({"blend": blend, "sheet": sheet, **got}))


if __name__ == "__main__":
    main()
