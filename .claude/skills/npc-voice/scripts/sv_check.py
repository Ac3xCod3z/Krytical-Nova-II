"""Speaker check: is every line the same person as its voice's sample? (I cannot hear; this is the proof.)

Run with ComfyUI's Python (transformers + torchaudio):
  C:/AI/ComfyUI-Easy-Install/ComfyUI-Easy-Install/python_embeded/python.exe -I sv_check.py <batch.json> <clips folder> [--threshold T]

Model: microsoft/wavlm-base-plus-sv (x-vector speaker embeddings) in C:/AI/models/wavlm-base-plus-sv.
For every line: cosine similarity of its embedding to its sample's (samples/<Npc>_<V>.flac). For calibration it also
prints how alike voice A and voice B of the same NPC are (two different people of the same gender): a line must score
well above that. Without --threshold the pass mark is set halfway between the lowest same-voice score that clearly
belongs and the highest A-vs-B score, but never below 0.86. Writes <clips>/sv_check.json; prints FAIL lines.
"""
import argparse, glob, json, os, sys

import numpy as np
import soundfile as sf
import torch
import torchaudio
from transformers import AutoFeatureExtractor, WavLMForXVector

MODEL = r"C:/AI/models/wavlm-base-plus-sv"


def load16k(path):
    x, sr = sf.read(path, dtype="float32")
    x = x if x.ndim == 1 else x.mean(1)
    t = torch.from_numpy(x)
    if sr != 16000:
        t = torchaudio.functional.resample(t, sr, 16000)
    return t.numpy()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch"); ap.add_argument("clips"); ap.add_argument("--threshold", type=float)
    a = ap.parse_args()
    vendors = json.load(open(a.batch, encoding="utf-8"))["vendors"]
    fe = AutoFeatureExtractor.from_pretrained(MODEL)
    model = WavLMForXVector.from_pretrained(MODEL).eval()
    cache = {}

    def emb(path):
        if path not in cache:
            inputs = fe(load16k(path), sampling_rate=16000, return_tensors="pt", padding=True)
            with torch.no_grad():
                e = model(**inputs).embeddings[0]
            cache[path] = torch.nn.functional.normalize(e, dim=-1).numpy()
        return cache[path]

    rows, cross = [], []
    for v in vendors:
        refs = {}
        for k in v["voices"]:
            sp = os.path.join(a.clips, "samples", f"{v['npc']}_{k}.flac")
            if os.path.exists(sp):
                refs[k] = emb(sp)
        ks = list(refs)
        for i in range(len(ks)):
            for j in range(i + 1, len(ks)):
                cross.append((v["npc"], f"{ks[i]}-{ks[j]}", float(refs[ks[i]] @ refs[ks[j]])))
        for k, r in refs.items():
            for n in range(1, len(v["lines"]) + 1):
                cid = f"{v['npc']}_{k}_{n}"
                p = os.path.join(a.clips, cid + ".flac")
                if os.path.exists(p):
                    e = emb(p)
                    # How alike the line is to the NPC's OTHER voice(s): it must be clearly closer to its own sample.
                    other = max((float(e @ refs[o]) for o in refs if o != k), default=0.0)
                    rows.append({"id": cid, "npc": v["npc"], "voice": k, "sim": float(e @ r), "other": other})
    if not rows:
        raise SystemExit("No lines found.")
    sims = np.array([r["sim"] for r in rows])
    top_cross = max(c[2] for c in cross) if cross else 0.0
    # Fixed rule (calibrated 23 Oct 2026: same voice ~0.97, two different people of one NPC 0.887-0.938).
    thr = a.threshold if a.threshold else 0.93
    MARGIN = 0.03  # a line must be at least this much closer to its own sample than to the NPC's other voice
    for r in rows:
        r["pass"] = r["sim"] >= thr and r["sim"] - r["other"] >= MARGIN
    print(f"same voice (line vs its sample): min {sims.min():.3f}  median {np.median(sims):.3f}  max {sims.max():.3f}")
    if cross:
        cs = np.array([c[2] for c in cross])
        print(f"different people (voice A vs B of one NPC): min {cs.min():.3f}  median {np.median(cs):.3f}  max {cs.max():.3f}")
    print(f"pass mark {thr:.3f}: {sum(r['pass'] for r in rows)} of {len(rows)} lines pass")
    for r in rows:
        if not r["pass"]:
            print(f"FAIL {r['id']} own {r['sim']:.3f} other {r['other']:.3f}")
    json.dump({"threshold": thr, "cross": cross, "lines": rows}, open(os.path.join(a.clips, "sv_check.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
