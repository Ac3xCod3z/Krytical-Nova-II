"""Builds the page the user auditions candidates on, with previews shaped exactly as the bake would shape them.

    python audition.py [--shortlist <shortlist.json>] [--only "Hit" --only "Hurt"]

shortlist.json: {"<slot>": ["library/relative/path.wav", ...]}. Slots and what they are for come from slots.json.
The page is Library/KryticalNova.SoundIndex/audition/index.html; "Copy my picks" gives JSON to paste back.
"""
import argparse
import html
import json
import os
import sys

import soundfile

import common
import shape

HERE = os.path.dirname(os.path.abspath(__file__))
SLOTS = os.path.join(HERE, "..", "slots.json")
OUT = os.path.join(common.CACHE, "audition")
DEFAULT_SHORTLIST = os.path.join(common.CACHE, "shortlist.json")

PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Krytical Nova sound audition</title>
<style>
:root { color-scheme: dark; }
body { background:#14151a; color:#e8e8ee; font:15px/1.45 system-ui, sans-serif; margin:0; padding:0 16px 120px; }
header { position:sticky; top:0; background:#14151a; padding:12px 0; border-bottom:1px solid #333; z-index:2; display:flex; gap:12px; align-items:center; flex-wrap:wrap; }
h1 { font-size:18px; margin:0 12px 0 0; }
h2 { font-size:13px; letter-spacing:.12em; text-transform:uppercase; color:#8fd; margin:28px 0 8px; }
.slot { background:#1c1e26; border:1px solid #2c2f3a; border-radius:8px; padding:12px; margin:10px 0; }
.slot.done { border-color:#3a7; }
.slot h3 { margin:0; font-size:16px; display:inline; }
.purpose { color:#aab; margin:2px 0 8px; }
.row { display:flex; gap:8px; align-items:center; padding:3px 0; flex-wrap:wrap; }
.row.cur .name::after { content:" (first pick)"; color:#fc6; }
button { background:#2b3040; color:#e8e8ee; border:1px solid #47506a; border-radius:6px; padding:5px 12px; cursor:pointer; font:inherit; }
button:hover { background:#38405a; }
button.main { background:#245; border-color:#5ad; }
button.playing { background:#375; }
.name { color:#99a; font-size:12px; word-break:break-all; flex:1 1 300px; }
.len { color:#778; font-size:12px; width:54px; text-align:right; }
input[type=text] { background:#111; color:#e8e8ee; border:1px solid #47506a; border-radius:6px; padding:5px 8px; width:min(560px,100%); font:inherit; }
label { cursor:pointer; user-select:none; }
#status { color:#8fd; }
</style></head><body>
<header><h1>Sound audition</h1>
<button class="main" id="copy">Copy my picks</button><button id="stop">Stop all</button>
<span id="status"></span></header>
<p>Tick every candidate you want a slot to use (several ticks = variations the game rotates through). <b>Play as the game will</b>
plays your ticked ones (or all, if none are ticked) at the slot's real volume and random pitch, never the same one twice running.
A note tells me what to look for instead. Slots you leave unticked keep the first picks.</p>
<div id="slots"></div>
<script>
const DATA = __DATA__;
const KEY = "kn.audition.v1";
let state = {};
try { state = JSON.parse(localStorage.getItem(KEY) || "{}"); } catch (e) {}
const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) {} };
const live = new Set();
const lastPlayed = {};
function play(slot, cand, asGame, button) {
  const a = new Audio(cand.file);
  a.loop = !!slot.loop && !asGame ? true : !!slot.loop;
  a.volume = asGame ? Math.min(1, slot.volume) : 0.6;
  if (asGame && !slot.loop) {
    const semis = slot.lowest + Math.random() * (slot.highest - slot.lowest);
    a.preservesPitch = false; a.mozPreservesPitch = false; a.webkitPreservesPitch = false;
    a.playbackRate = Math.pow(2, semis / 12);
  }
  live.add(a);
  if (button) button.classList.add("playing");
  a.onended = a.onpause = () => { live.delete(a); if (button) button.classList.remove("playing"); };
  a.play();
}
function stopAll() { for (const a of [...live]) { a.pause(); } live.clear(); }
document.getElementById("stop").onclick = stopAll;
const host = document.getElementById("slots");
let group = "";
for (const slot of DATA) {
  if (slot.group !== group) { group = slot.group; const h = document.createElement("h2"); h.textContent = group; host.appendChild(h); }
  const st = state[slot.name] = state[slot.name] || { picks: [], note: "" };
  const box = document.createElement("div"); box.className = "slot";
  const mark = () => box.classList.toggle("done", st.picks.length > 0 || st.note.trim() !== "");
  box.innerHTML = `<h3></h3> <button class="main game">Play as the game will</button><div class="purpose"></div>`;
  box.querySelector("h3").textContent = slot.name;
  box.querySelector(".purpose").textContent = slot.purpose + (slot.loop ? "  (loops: listen for the seam)" : "");
  box.querySelector(".game").onclick = (e) => {
    if (slot.loop) stopAll();
    let pool = slot.candidates.filter(c => st.picks.includes(c.source));
    if (!pool.length) pool = slot.candidates;
    let pick = pool[Math.floor(Math.random() * pool.length)];
    if (pool.length > 1) while (pick.source === lastPlayed[slot.name]) pick = pool[Math.floor(Math.random() * pool.length)];
    lastPlayed[slot.name] = pick.source;
    play(slot, pick, true, e.target);
  };
  slot.candidates.forEach((cand, i) => {
    const row = document.createElement("div"); row.className = "row" + (cand.current ? " cur" : "");
    row.innerHTML = `<label><input type="checkbox"> ${i + 1}</label><button>Play</button><span class="len"></span><span class="name"></span>`;
    const tick = row.querySelector("input");
    tick.checked = st.picks.includes(cand.source);
    tick.onchange = () => { st.picks = slot.candidates.filter((c, n) => box.querySelectorAll("input[type=checkbox]")[n].checked).map(c => c.source); save(); mark(); };
    row.querySelector("button").onclick = (e) => { if (slot.loop) stopAll(); play(slot, cand, false, e.target); };
    row.querySelector(".len").textContent = cand.seconds.toFixed(2) + "s";
    row.querySelector(".name").textContent = cand.source + (cand.tip ? "   [" + cand.tip + "]" : "");
    box.appendChild(row);
  });
  const note = document.createElement("input"); note.type = "text"; note.placeholder = "Note (optional): none fit, want something more ...";
  note.value = st.note; note.oninput = () => { st.note = note.value; save(); mark(); };
  const wrap = document.createElement("div"); wrap.className = "row"; wrap.appendChild(note); box.appendChild(wrap);
  mark(); host.appendChild(box);
}
document.getElementById("copy").onclick = async () => {
  const out = {};
  for (const slot of DATA) { const st = state[slot.name]; if (st.picks.length || st.note.trim()) out[slot.name] = { picks: st.picks, note: st.note.trim() }; }
  const text = "KN-PICKS " + JSON.stringify(out);
  const status = document.getElementById("status");
  try { await navigator.clipboard.writeText(text); status.textContent = Object.keys(out).length + " slot(s) copied. Paste into the chat."; }
  catch (e) { window.prompt("Copy this and paste it into the chat:", text); }
};
</script></body></html>
"""


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--shortlist", default=DEFAULT_SHORTLIST)
    p.add_argument("--only", action="append", default=[])
    p.add_argument("--round", type=int, default=1, help="2 and up: a page of its own in audition/round<N>, with its own ticks")
    a = p.parse_args()
    global OUT
    if a.round > 1:
        OUT = os.path.join(OUT, f"round{a.round}")
    with open(SLOTS, encoding="utf-8") as f:
        slots = json.load(f)
    with open(a.shortlist, encoding="utf-8") as f:
        shortlist = json.load(f)

    data, failed = [], []
    for slot in slots:
        name = slot["name"]
        if name not in shortlist:
            continue
        fresh = not a.only or name in a.only   # --only reshapes those slots; the rest keep their previews
        folder = os.path.join(OUT, "media", name.replace(" ", ""))
        os.makedirs(folder, exist_ok=True)
        if fresh:   # previews are numbered by place in the shortlist: an old list's files must not linger
            for old in os.listdir(folder):
                os.remove(os.path.join(folder, old))
        candidates = []
        for n, entry in enumerate(shortlist[name]):
            source = entry if isinstance(entry, str) else entry.get("id") or entry["path"]
            layers = None if isinstance(entry, str) else entry.get("layers")
            longest = slot.get("max_seconds") if isinstance(entry, str) else entry.get("max_seconds", slot.get("max_seconds"))
            start = None if isinstance(entry, str) else entry.get("start")
            file = os.path.join(folder, f"{n + 1:02d}.wav")
            tip = "" if isinstance(entry, str) else entry.get("tip", "")
            if not fresh and os.path.exists(file):
                candidates.append({"file": os.path.relpath(file, OUT).replace("\\", "/"), "source": source, "tip": tip,
                                   "seconds": soundfile.info(file).duration, "current": not isinstance(entry, str) and bool(entry.get("current"))})
                continue
            try:
                if slot.get("loop"):
                    audio = shape.loop(source, start=start)
                elif layers:
                    audio = shape.mix(layers, stereo=slot.get("stereo", False), max_seconds=longest)
                else:
                    audio = shape.one_shot(source, stereo=slot.get("stereo", False), max_seconds=longest, start=start)
            except Exception as error:
                failed.append(f"{name}: {source}: {error}")
                continue
            shape.write(file, audio)
            candidates.append({"file": os.path.relpath(file, OUT).replace("\\", "/"), "source": source, "tip": tip,
                               "seconds": len(audio) / shape.RATE, "current": not isinstance(entry, str) and bool(entry.get("current"))})
        data.append({"name": name, "group": slot["group"], "purpose": slot["purpose"], "loop": bool(slot.get("loop")),
                     "volume": slot["volume"], "lowest": slot.get("lowest", 0), "highest": slot.get("highest", 0), "candidates": candidates})
    page = os.path.join(OUT, "index.html")
    with open(page, "w", encoding="utf-8") as f:
        f.write(PAGE.replace("__DATA__", json.dumps(data).replace("</", "<\\/")).replace("kn.audition.v1", f"kn.audition.round{a.round}"))
    print(page, f"{len(data)} slots, {sum(len(s['candidates']) for s in data)} candidates")
    for line in failed:
        print("  failed:", line)


if __name__ == "__main__":
    sys.exit(main())
