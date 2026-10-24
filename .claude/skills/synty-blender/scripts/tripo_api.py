"""Tripo's API from the command line: one picture -> four views -> a P2 model with an 8K texture.

  python tripo_api.py check
  python tripo_api.py views --image concept.png --out <folder>
  python tripo_api.py model --views-task <task_id> --out <folder> [--quad] [--faces 20000] [--quality extreme]
  python tripo_api.py model --front f.png --back b.png [--left l.png] [--right r.png] --out <folder> ...
  python tripo_api.py task <task_id>

`views` uploads the picture and runs Tripo's Image to Multiview, then saves front/left/back/right PNGs and a contact
sheet: look at them before paying for a model. `model` runs Multiview to Model on the P2 model (Preview,
P2-20260801), reusing a views task or uploading the view files, and saves every file the task returns (a GLB, its
texture maps, a preview render). Every step writes tripo_<step>.json beside its files with the task id, credits and
settings.

The key is read from the TRIPO_API_KEY user setting, else from the owner's key file; it is only ever sent in the
Authorization header to openapi.tripo3d.ai, never printed, logged or written anywhere.
Python's own HTTPS uses Windows' certificate store, so Norton's inspection does not break it.
"""
import argparse
import json
import mimetypes
import os
import ssl
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

# the owner's developer account (developers.tripo3d.ai) lives here; openapi.tripo3d.com from the docs rejects its key
API = "https://openapi.tripo3d.ai/v3"
KEY_FILE = Path.home() / "Documents" / "Game Docs" / "ClaudeTripo api key.txt"
P2 = "P2-20260801"
CTX = ssl.create_default_context()


def key():
    k = os.environ.get("TRIPO_API_KEY", "").strip()
    if not k and os.name == "nt":
        try:                                    # a user setting made after this shell started
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as h:
                k = str(winreg.QueryValueEx(h, "TRIPO_API_KEY")[0]).strip()
        except OSError:
            k = ""
    if not k and KEY_FILE.is_file():
        # the owner's file carries a label line too: the secret key is the word that starts tsk_
        import re
        found = re.findall(r"\btsk_[A-Za-z0-9_\-]+", KEY_FILE.read_text(encoding="utf-8"))
        k = found[0] if found else ""
    if not k:
        raise SystemExit("No Tripo key: set TRIPO_API_KEY or put it in " + str(KEY_FILE))
    return k


def call(method, path, body=None, raw=None, ctype="application/json"):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Authorization", "Bearer " + key())
    if data is not None:
        req.add_header("Content-Type", ctype)
    try:
        with urllib.request.urlopen(req, context=CTX, timeout=120) as r:
            out = json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:400]
        raise SystemExit(f"Tripo {method} {path} -> HTTP {e.code}: {detail}")
    if out.get("code", 0) != 0:
        raise SystemExit(f"Tripo {method} {path} -> {json.dumps(out)[:400]}")
    return out.get("data", out)


def upload(path):
    path = Path(path)
    b = "----kn" + uuid.uuid4().hex
    mt = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    body = (f"--{b}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
            f"Content-Type: {mt}\r\n\r\n").encode() + path.read_bytes() + f"\r\n--{b}--\r\n".encode()
    return call("POST", "/files", raw=body, ctype="multipart/form-data; boundary=" + b)["file_token"]


def wait(task_id, every=5, limit=1800):
    t0 = time.time()
    last = None
    while True:
        d = call("GET", f"/tasks/{task_id}")
        st, pr = d.get("status"), d.get("progress")
        if (st, pr) != last:
            print(f"  {task_id}: {st} {pr}%", flush=True)
            last = (st, pr)
        if st == "success":
            return d
        if st in ("failed", "cancelled"):
            raise SystemExit(f"Task {task_id} {st}: {d.get('error_code')} {d.get('error_message')}")
        if time.time() - t0 > limit:
            raise SystemExit(f"Task {task_id} still {st} after {limit} s")
        time.sleep(every)


def fetch(url, dest):
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, context=CTX, timeout=600) as r:
        dest.write_bytes(r.read())
    return dest


def save_outputs(d, out, stem):
    """Every URL in the task's output, downloaded (the links expire)."""
    files = {}
    for k, v in (d.get("output") or {}).items():
        if isinstance(v, str) and v.startswith("http"):
            ext = Path(v.split("?")[0]).suffix or ".bin"
            name = f"{stem}_{k.replace('_url', '')}{ext}"
            files[k] = str(fetch(v, out / name).name)
    return files


def record(out, step, info):
    (out / f"tripo_{step}.json").write_text(json.dumps(info, indent=1), encoding="utf-8")


def contact_sheet(out, names):
    try:
        import subprocess
        ins = []
        for n in names:
            ins += ["-i", str(out / n)]
        filt = "".join(f"[{i}]scale=-1:512[v{i}];" for i in range(len(names))) + \
               "".join(f"[v{i}]" for i in range(len(names))) + f"hstack={len(names)}"
        subprocess.run(["ffmpeg", "-v", "error", "-y", *ins, "-filter_complex", filt, str(out / "views_sheet.png")],
                       check=True)
        return "views_sheet.png"
    except Exception as e:                       # the views are still saved one by one
        return f"(no sheet: {e})"


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("check")
    v = sub.add_parser("views")
    v.add_argument("--image", required=True)
    v.add_argument("--out", required=True)
    m = sub.add_parser("model")
    m.add_argument("--views-task")
    for s in ("front", "back", "left", "right"):
        m.add_argument("--" + s)
    m.add_argument("--out", required=True)
    m.add_argument("--quality", default="extreme", choices=("fast", "standard", "detailed", "extreme"))
    m.add_argument("--quad", action="store_true")
    m.add_argument("--faces", type=int)
    m.add_argument("--no-pbr", action="store_true")
    m.add_argument("--seed", type=int)
    t = sub.add_parser("task")
    t.add_argument("task_id")
    a = ap.parse_args()

    if a.cmd == "check":
        b = call("GET", "/account/balance")
        print(f"Key accepted. API credits: {b.get('balance')} (frozen {b.get('frozen')})")
        return

    if a.cmd == "task":
        print(json.dumps(call("GET", f"/tasks/{a.task_id}"), indent=1))
        return

    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    if a.cmd == "views":
        tok = upload(a.image)
        tid = call("POST", "/generation/image-to-multiview", {"input": tok})["task_id"]
        print("Image to Multiview:", tid)
        d = wait(tid)
        files = save_outputs(d, out, "view")
        sheet = contact_sheet(out, [files[k] for k in ("front_view_url", "left_view_url", "back_view_url",
                                                        "right_view_url") if k in files])
        record(out, "views", {"task_id": tid, "source": str(a.image), "files": files, "sheet": sheet,
                              "credits": d.get("credits_consumed")})
        print(json.dumps({"task_id": tid, "files": files, "sheet": sheet, "credits": d.get("credits_consumed")},
                         indent=1))
        return

    if a.views_task:
        inputs = [{"task_id": a.views_task}]
    else:
        if not a.front:
            raise SystemExit("Pass --views-task, or at least --front plus one more view.")
        inputs = [{s: upload(getattr(a, s))} for s in ("front", "left", "back", "right") if getattr(a, s)]
        if len(inputs) < 2:
            raise SystemExit("Multiview needs the front and at least one more view.")
    body = {"inputs": inputs, "model": P2, "texture": True, "pbr": not a.no_pbr, "texture_quality": a.quality}
    if a.quad:
        body["quad"] = True
    if a.faces:
        body["face_limit"] = a.faces
    if a.seed is not None:
        body["model_seed"] = a.seed
    tid = call("POST", "/generation/multiview-to-model", body)["task_id"]
    print("Multiview to Model (P2):", tid)
    d = wait(tid, every=10, limit=3600)
    files = save_outputs(d, out, "model")
    settings = {k: v for k, v in body.items() if k != "inputs"}
    record(out, "model", {"task_id": tid, "inputs": a.views_task or "uploaded views", "settings": settings,
                          "files": files, "credits": d.get("credits_consumed")})
    print(json.dumps({"task_id": tid, "files": files, "credits": d.get("credits_consumed")}, indent=1))


if __name__ == "__main__":
    main()
