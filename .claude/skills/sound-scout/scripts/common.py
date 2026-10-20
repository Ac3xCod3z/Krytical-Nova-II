"""Shared by the sound-scout scripts: where things are, decoding through ffmpeg, the catalogue, the CLAP model."""
import glob
import json
import os
import shutil
import subprocess

import numpy as np

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
CACHE = os.path.join(PROJECT, "Library", "KryticalNova.SoundIndex")
def in_drive_or_downloads(env, drive_parts, downloads_name):
    """A library: the env override, else Google Drive's My Drive/KryticalNova/... (moved there 20 Oct 2026, streamed),
    else the old ~/Downloads/<name>."""
    if os.environ.get(env):
        return os.environ[env]
    for letter in "DEFGHIJKLMNOPQRSTUVWXYZ":
        candidate = os.path.join(letter + ":\\", "My Drive", "KryticalNova", *drive_parts)
        if os.path.isdir(candidate):
            return candidate
    return os.path.join(os.path.expanduser("~"), "Downloads", downloads_name)


LIBRARY = in_drive_or_downloads("KN_SOUND_LIBRARY", ("Audio", "SFx"), "SFx")
CATALOG = os.path.join(CACHE, "catalog.jsonl")
EMBEDDINGS = os.path.join(CACHE, "embeddings.npy")
RATE = 48000
EXTENSIONS = (".wav", ".mp3", ".ogg", ".flac", ".aif", ".aiff", ".m4a")
MODEL = "laion/clap-htsat-unfused"

os.environ.setdefault("HF_HOME", os.path.join(CACHE, "hf"))
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
os.environ.setdefault("TRANSFORMERS_VERBOSITY", "error")


def ffmpeg():
    found = shutil.which("ffmpeg")
    if found:
        return found
    hits = glob.glob(os.path.join(os.path.expanduser("~"), "AppData", "Local", "Microsoft", "WinGet", "Packages",
                                  "Gyan.FFmpeg*", "*", "bin", "ffmpeg.exe"))
    if not hits:
        raise RuntimeError("ffmpeg not found: winget install --id Gyan.FFmpeg -e --source winget")
    return hits[0]


def decode(path, seconds=None, channels=1, rate=RATE):
    """The file as float32, shape (samples,) for mono or (samples, channels)."""
    cmd = [ffmpeg(), "-nostdin", "-v", "error", "-i", path]
    if seconds:
        cmd += ["-t", str(seconds)]
    cmd += ["-ac", str(channels), "-ar", str(rate), "-f", "f32le", "-"]
    raw = subprocess.run(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True).stdout
    audio = np.frombuffer(raw, dtype=np.float32)
    return audio if channels == 1 else audio.reshape(-1, channels)


def library_files(root=None):
    root = root or LIBRARY
    out = []
    for folder, _, names in os.walk(root):
        if "__MACOSX" in folder:
            continue
        for name in names:
            if name.lower().endswith(EXTENSIONS) and not name.startswith("._"):
                out.append(os.path.join(folder, name))
    return sorted(out)


def load_catalog():
    """(rows, embeddings): one row per file, embeddings[row['i']] is its vector."""
    if not os.path.exists(CATALOG) or not os.path.exists(EMBEDDINGS):
        return [], np.zeros((0, 512), dtype=np.float32)
    with open(CATALOG, encoding="utf-8") as f:
        rows = [json.loads(line) for line in f if line.strip()]
    return rows, np.load(EMBEDDINGS)


def relative(path):
    try:
        return os.path.relpath(path, LIBRARY).replace("\\", "/")
    except ValueError:
        return path.replace("\\", "/")


def absolute(path):
    return path if os.path.isabs(path) else os.path.join(LIBRARY, path)


_model = None


def clap():
    """(model, processor), loaded once. CPU: the graphics card is the Editor's."""
    global _model
    if _model is None:
        import torch
        from transformers import ClapModel, ClapProcessor
        torch.set_grad_enabled(False)
        _model = (ClapModel.from_pretrained(MODEL).eval(), ClapProcessor.from_pretrained(MODEL))
    return _model


def _vectors(output):
    import torch
    if not isinstance(output, torch.Tensor):
        output = output.pooler_output if getattr(output, "pooler_output", None) is not None else output[0]
    output = torch.nn.functional.normalize(output, dim=-1)
    return output.cpu().numpy().astype(np.float32)


def embed_audio(clips):
    """One unit vector per clip (each a mono float32 array at RATE, at most ten seconds)."""
    model, processor = clap()
    try:
        inputs = processor(audio=clips, sampling_rate=RATE, return_tensors="pt", padding=True)
    except TypeError:
        inputs = processor(audios=clips, sampling_rate=RATE, return_tensors="pt", padding=True)
    return _vectors(model.get_audio_features(**inputs))


def embed_text(texts):
    model, processor = clap()
    inputs = processor(text=texts, return_tensors="pt", padding=True)
    return _vectors(model.get_text_features(**inputs))
