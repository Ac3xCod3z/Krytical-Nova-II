"""Pick the best take of each failed line by speaker match, or scrap the line (owner rule: one character, one voice).

Run with ComfyUI's Python:
  .../python_embeded/python.exe -I best_take.py <batch.json> <clips folder>

For every <clips>/candidates/<Npc>_<V>_<n>_s<k>.flac and the current <Npc>_<V>_<n>.flac, scores each against the
voice's sample (and the NPC's other voice) with the same rule as sv_check.py (>= 0.93 own, >= 0.03 closer than the
other voice). The best passing take replaces the current line; a line with no passing take is moved to
<clips>/scrapped/ so it never reaches the audition page. Prints what it kept and what it scrapped.
"""
import glob, json, os, re, shutil, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sv_check as sv  # noqa: E402  (same model and loading code)

import torch  # noqa: E402
from transformers import AutoFeatureExtractor, WavLMForXVector  # noqa: E402

THR, MARGIN = 0.93, 0.03


def main():
    batch, clips = sys.argv[1], sys.argv[2]
    fe = AutoFeatureExtractor.from_pretrained(sv.MODEL)
    model = WavLMForXVector.from_pretrained(sv.MODEL).eval()

    def emb(path):
        inputs = fe(sv.load16k(path), sampling_rate=16000, return_tensors="pt", padding=True)
        with torch.no_grad():
            e = model(**inputs).embeddings[0]
        return torch.nn.functional.normalize(e, dim=-1).numpy()

    vendors = {v["npc"]: v for v in json.load(open(batch, encoding="utf-8"))["vendors"]}
    groups = {}
    for c in glob.glob(os.path.join(clips, "candidates", "*_s*.flac")):
        cid = re.sub(r"_s\d+$", "", os.path.splitext(os.path.basename(c))[0])
        groups.setdefault(cid, []).append(c)
    scrapdir = os.path.join(clips, "scrapped")
    for cid, cands in sorted(groups.items()):
        npc, voice, _ = cid.rsplit("_", 2)
        refs = {k: emb(os.path.join(clips, "samples", f"{npc}_{k}.flac")) for k in vendors[npc]["voices"]}
        takes = cands + ([os.path.join(clips, cid + ".flac")] if os.path.exists(os.path.join(clips, cid + ".flac")) else [])
        scored = []
        words = len(vendors[npc]["lines"][int(cid.rsplit("_", 1)[1]) - 1].split())
        for t in takes:
            dur = len(sv.load16k(t)) / 16000
            if not 1.0 <= words / dur <= 4.2:  # a take spoken too fast is likely cut short; too slow, padded
                continue
            e = emb(t)
            own = float(e @ refs[voice])
            other = max((float(e @ refs[o]) for o in refs if o != voice), default=0.0)
            scored.append((own, other, t))
        scored.sort(reverse=True)
        own, other, best = scored[0] if scored else (0.0, 0.0, None)
        if best and own >= THR and own - other >= MARGIN:
            if os.path.abspath(best) != os.path.abspath(os.path.join(clips, cid + ".flac")):
                base = os.path.splitext(best)[0]
                for ext in (".flac", ".mp3"):
                    shutil.copyfile(base + ext, os.path.join(clips, cid + ext))
            print(f"KEPT {cid} {own:.3f} (other voice {other:.3f}) from {os.path.basename(best)}")
        else:
            os.makedirs(scrapdir, exist_ok=True)
            for ext in (".flac", ".mp3"):
                p = os.path.join(clips, cid + ext)
                if os.path.exists(p):
                    shutil.move(p, os.path.join(scrapdir, cid + ext))
            print(f"SCRAPPED {cid}: best take {own:.3f} (other voice {other:.3f}) - no take is clearly the same speaker")


if __name__ == "__main__":
    main()
