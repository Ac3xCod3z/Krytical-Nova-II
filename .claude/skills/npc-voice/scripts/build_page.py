"""Build the vendor voice audition page from a batch JSON and the clips made so far.

python -I build_page.py <batch.json> <clips folder> <page folder> [--code <code_changes.html>]

Copies every <npc>_<voice>_<n>.mp3 found in <clips folder> into <page folder> (artifact files must sit beside the
page, inside the working directory or the scratchpad) and writes <page folder>/audition.html. Clips not made yet
show as "still making"; run it again and republish as more arrive. Picks save to the page's db: picks/<npc> =
{voice, keep: [line numbers], note, newLine}.
"""
import argparse, glob, html, json, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = os.path.join(HERE, "audition_template.html")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch"); ap.add_argument("clips"); ap.add_argument("page")
    ap.add_argument("--code", help="an HTML fragment shown in the 'Code changes' section")
    a = ap.parse_args()
    data = json.load(open(a.batch, encoding="utf-8"))
    os.makedirs(a.page, exist_ok=True)
    have = []
    # Speaker check results (sv_check.py), shown per line; scrapped lines sit in <clips>/scrapped.
    sv = {}
    svp = os.path.join(a.clips, "sv_check.json")
    if os.path.exists(svp):
        sv = {r["id"]: round(r["sim"], 3) for r in json.load(open(svp, encoding="utf-8"))["lines"]}
    scrapped = sorted(os.path.splitext(f)[0] for f in os.listdir(os.path.join(a.clips, "scrapped"))
                      if f.endswith(".mp3")) if os.path.isdir(os.path.join(a.clips, "scrapped")) else []
    for v in data["vendors"]:
        for k in v["voices"]:
            sp = os.path.join(a.clips, "samples", f"{v['npc']}_{k}.mp3")
            if os.path.exists(sp):
                shutil.copyfile(sp, os.path.join(a.page, f"sample_{v['npc']}_{k}.mp3"))
                have.append(f"sample_{v['npc']}_{k}")
        for k in v["voices"]:
            for i in range(1, len(v["lines"]) + 1):
                cid = f"{v['npc']}_{k}_{i}"
                src = os.path.join(a.clips, cid + ".mp3")
                if os.path.exists(src):
                    dst = os.path.join(a.page, cid + ".mp3")
                    if not os.path.exists(dst) or os.path.getsize(dst) != os.path.getsize(src):
                        shutil.copyfile(src, dst)
                    have.append(cid)
    code = open(a.code, encoding="utf-8").read() if a.code else ""
    page = open(TEMPLATE, encoding="utf-8").read()
    page = page.replace("/*DATA*/null", json.dumps(data["vendors"], ensure_ascii=False))
    page = page.replace("/*HAVE*/[]", json.dumps(have))
    page = page.replace("/*SV*/{}", json.dumps(sv))
    page = page.replace("/*SCRAPPED*/[]", json.dumps(scrapped))
    page = page.replace("<!--CODE-->", code)
    total = sum(len(v["voices"]) * len(v["lines"]) for v in data["vendors"])
    lines_made = sum(1 for h in have if not h.startswith("sample_"))
    page = page.replace("<!--COUNT-->", f"{lines_made} of {total} lines made" + (f", {len(scrapped)} scrapped" if scrapped else ""))
    open(os.path.join(a.page, "audition.html"), "w", encoding="utf-8").write(page)
    print(json.dumps({"page": os.path.join(a.page, "audition.html"), "clips": len(have), "total": total,
                      "files": [c + ".mp3" for c in have]}))


if __name__ == "__main__":
    main()
