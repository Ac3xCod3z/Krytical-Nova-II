"""Builds the page the owner picks music on, with a click on every measured beat so the measuring can be judged by ear.

    python music_audition.py [--shortlist <shortlist.json>] [--round 1]

shortlist.json is what music_shortlist.py writes. Every track is levelled and written once as Ogg Vorbis into
Library/KryticalNova.SoundIndex/audition/music/media, and the beat is measured again on that very file: it is the
file the page plays and the file music_bake.py puts into the game, so what the owner approves by ear is what the
game keeps time to. Serve the audition folder (python -m http.server 8765 --bind 127.0.0.1) and open
http://127.0.0.1:8765/music/ . "Copy my picks" gives a line starting KN-MUSIC to paste back.
"""
import argparse
import json
import os
import re
import subprocess
import sys

import common
import tempo
import music_shortlist

OUT = os.path.join(common.CACHE, "audition", "music")
PICKS_KEY = "kn.music.round"
LEVEL_DB = -18.0        # the level every track is brought to, measured over the whole track
CEILING_DB = -1.0       # and no peak above this


def clean_name(path):
    """A track's title from its file name: without the catalogue numbers and codes the packs put round it."""
    name = os.path.splitext(os.path.basename(path))[0]
    name = re.sub(r"^(AAM\d+_)?\d+[-_ ]+", "", name)                    # leading track number
    name = re.sub(r"[-_ ]+(FULL|Full)([-_ ]+\w+)?$", "", name)          # "-FULL-SM454", " - Full", "_Full"
    name = re.sub(r"[-_ ][A-Z]{2,4}\d{4,}$", "", name)                  # "-DHT015001"
    words = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", name.replace("-", " ").replace("_", " "))
    return re.sub(r"\s+", " ", words).strip()


def write_preview(source, target, rms_db, peak_db):
    gain = min(LEVEL_DB - rms_db, CEILING_DB - peak_db)
    cmd = [common.ffmpeg(), "-nostdin", "-v", "error", "-y", "-i", source, "-vn", "-af", f"volume={gain:.2f}dB",
           "-ac", "2", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "5", target]
    subprocess.run(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
    return gain


PAGE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Krytical Nova music audition</title>
<style>
:root { color-scheme: dark; }
body { background:#14151a; color:#e8e8ee; font:15px/1.45 system-ui, sans-serif; margin:0; padding:0 16px 120px; }
header { position:sticky; top:0; background:#14151a; padding:12px 0; border-bottom:1px solid #333; z-index:2; display:flex; gap:12px; align-items:center; flex-wrap:wrap; }
h1 { font-size:18px; margin:0 12px 0 0; }
h2 { font-size:13px; letter-spacing:.12em; text-transform:uppercase; color:#8fd; margin:28px 0 8px; }
.track { background:#1c1e26; border:1px solid #2c2f3a; border-radius:8px; padding:10px 12px; margin:8px 0; }
.track.picked { border-color:#3a7; }
.track.live { background:#20242f; }
.row { display:flex; gap:10px; align-items:center; flex-wrap:wrap; padding:2px 0; }
.name { font-size:16px; font-weight:600; min-width:220px; }
.facts { color:#99a; font-size:12px; }
.src { color:#667; font-size:11px; word-break:break-all; }
button { background:#2b3040; color:#e8e8ee; border:1px solid #47506a; border-radius:6px; padding:5px 12px; cursor:pointer; font:inherit; }
button:hover { background:#38405a; }
button.main { background:#245; border-color:#5ad; }
button.on { background:#375; border-color:#5c8; }
button.small { padding:3px 8px; font-size:13px; }
input[type=text] { background:#111; color:#e8e8ee; border:1px solid #47506a; border-radius:6px; padding:5px 8px; width:min(520px,100%); font:inherit; }
input[type=range].seek { flex:1 1 260px; }
label { cursor:pointer; user-select:none; }
.dim { color:#99a; font-size:13px; }
.val { display:inline-block; min-width:54px; text-align:center; color:#fc6; font-variant-numeric:tabular-nums; }
#status { color:#8fd; }
ol { margin:6px 0 0 18px; padding:0; } li { margin:2px 0; }
</style></head><body>
<header><h1>Music audition: Cadence</h1>
<button class="main" id="copy">Copy my picks</button><button id="stop">Stop</button>
<label class="dim">music <input type="range" id="musicVol" min="0" max="1" step="0.05" value="0.7"></label>
<label class="dim">click <input type="range" id="clickVol" min="0" max="1" step="0.05" value="0.5"></label>
<span id="status"></span></header>
<p>These are the tracks with the steadiest beat that the search rated closest to fight music, a few from each pack. For each one:</p>
<ol>
<li><b>Tick</b> it if you would fight to it.</li>
<li>With <b>Click on the beat</b> on, listen for whether the click sits on the beat. If it runs twice too fast or twice too slow, switch <b>&frac12;</b> or <b>&times;2</b>.</li>
<li>If the click is always a little early or late, <b>nudge</b> it until it sits right.</li>
<li>The <b>high click</b> should fall on the "one" of each bar. If it does not, press <b>shift</b> until it does.</li>
</ol>
<p class="dim">Drag the bar to jump: check the middle and the end as well as the start. A note tells me anything else. I read your ticks from this page, or press Copy my picks and paste the line.</p>
<div id="tracks"></div>
<script>
const DATA = __DATA__;
const KEY = "__KEY__";
let state = {};
try { state = JSON.parse(localStorage.getItem(KEY) || "{}"); } catch (e) {}
const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) {} };
const status = (text) => { document.getElementById("status").textContent = text; };
const mod = (a, n) => ((a % n) + n) % n;

let ctx = null, music = null, clicks = null, cur = null;
let held = null;      // the one decoded track kept in memory
function audio() {
  if (!ctx) {
    ctx = new AudioContext();
    music = ctx.createGain(); music.connect(ctx.destination);
    clicks = ctx.createGain(); clicks.connect(ctx.destination);
    music.gain.value = +document.getElementById("musicVol").value;
    clicks.gain.value = +document.getElementById("clickVol").value;
  }
  if (ctx.state === "suspended") ctx.resume();
}
document.getElementById("musicVol").oninput = (e) => { if (music) music.gain.value = +e.target.value; };
document.getElementById("clickVol").oninput = (e) => { if (clicks) clicks.gain.value = +e.target.value; };

async function decoded(track) {
  if (held && held.file === track.file) return held.buffer;
  held = null;
  const data = await (await fetch(track.file)).arrayBuffer();
  const buffer = await ctx.decodeAudioData(data);
  held = { file: track.file, buffer };
  return buffer;
}

function grid(track) {
  const st = state[track.source];
  return { period: 60 / track.bpm / st.factor, first: track.first_beat + st.nudge / 1000, perBar: Math.max(1, Math.round(4 * st.factor)),
           one: Math.round((track.bar_beat + st.bar) * st.factor) };
}

function beep(when, high) {
  const osc = ctx.createOscillator(), gain = ctx.createGain();
  osc.frequency.value = high ? 1760 : 1175;
  gain.gain.setValueAtTime(0.0001, when);
  gain.gain.exponentialRampToValueAtTime(1, when + 0.002);
  gain.gain.exponentialRampToValueAtTime(0.0001, when + 0.045);
  osc.connect(gain); gain.connect(clicks);
  osc.start(when); osc.stop(when + 0.06);
}

function position() { return cur ? cur.from + Math.max(0, ctx.currentTime - cur.t0) : 0; }

// Clicks are booked a quarter of a second ahead on the audio clock, the clock the music itself plays on.
function schedule() {
  if (!cur) return;
  const pos = position();
  cur.view.seek.value = pos; cur.view.time.textContent = clock(pos) + " / " + clock(cur.track.seconds);
  const st = state[cur.track.source];
  if (!st.click) { cur.next = null; return; }
  const g = grid(cur.track), until = pos + 0.25;
  let k = cur.next != null ? cur.next : Math.ceil((pos + 0.02 - g.first) / g.period);
  for (; g.first + k * g.period < until; k++) {
    const at = g.first + k * g.period;
    if (at < pos) continue;
    beep(cur.t0 + (at - cur.from), mod(k - g.one, g.perBar) === 0);
  }
  cur.next = k;
}
// After a change to the click, start booking again past what is already booked.
function rebook() { if (cur) { const g = grid(cur.track); cur.next = Math.ceil((position() + 0.25 - g.first) / g.period); } }

function stop() {
  if (!cur) return;
  clearInterval(cur.timer);
  try { cur.source.onended = null; cur.source.stop(); } catch (e) {}
  cur.view.box.classList.remove("live"); cur.view.play.classList.remove("on"); cur.view.play.textContent = "Play";
  cur = null; status("");
}
document.getElementById("stop").onclick = stop;

async function play(track, view, from) {
  stop(); audio();
  status("Loading " + track.name + " ...");
  const token = {}; play.token = token;
  let buffer;
  try { buffer = await decoded(track); } catch (e) { status("Could not load " + track.name + ": " + e); return; }
  if (play.token !== token) return;     // another track was asked for meanwhile
  const source = ctx.createBufferSource();
  source.buffer = buffer; source.connect(music);
  const t0 = ctx.currentTime + 0.12;
  from = Math.max(0, Math.min(from || 0, buffer.duration - 1));
  source.start(t0, from);
  cur = { track, view, source, t0, from, next: null };
  source.onended = () => { if (cur && cur.source === source) stop(); };
  view.box.classList.add("live"); view.play.classList.add("on"); view.play.textContent = "Playing";
  status(track.name);
  schedule(); cur.timer = setInterval(schedule, 50);
}

const clock = (s) => Math.floor(s / 60) + ":" + String(Math.floor(s % 60)).padStart(2, "0");
const host = document.getElementById("tracks");
let pack = "";
for (const track of DATA) {
  if (track.pack !== pack) { pack = track.pack; const h = document.createElement("h2"); h.textContent = pack; host.appendChild(h); }
  const st = state[track.source] = Object.assign({ pick: false, click: true, factor: 1, nudge: 0, bar: 0, note: "" }, state[track.source] || {});
  const box = document.createElement("div"); box.className = "track";
  box.innerHTML = `
    <div class="row"><label><input type="checkbox" class="pick"> use</label><button class="main play">Play</button>
      <span class="name"></span><span class="facts"></span></div>
    <div class="row"><input type="range" class="seek" min="0" step="0.1" value="0"><span class="dim time"></span></div>
    <div class="row dim">
      <label><input type="checkbox" class="click"> Click on the beat</label>
      <span>speed <button class="small f" data-f="0.5">&frac12;</button><button class="small f" data-f="1">as measured</button><button class="small f" data-f="2">&times;2</button></span>
      <span>nudge <button class="small n" data-n="-10">earlier</button><span class="val nudge"></span><button class="small n" data-n="10">later</button></span>
      <span>high click <button class="small bar">shift</button><span class="val barv"></span></span>
    </div>
    <div class="row"><input type="text" class="note" placeholder="Note (optional)"></div>
    <div class="src"></div>`;
  const view = { box, play: box.querySelector(".play"), seek: box.querySelector(".seek"), time: box.querySelector(".time") };
  const paint = () => {
    box.classList.toggle("picked", st.pick);
    box.querySelector(".facts").textContent = (track.bpm * st.factor).toFixed(track.bpm % 1 ? 2 : 0) + " beats a minute  ·  " + clock(track.seconds) +
      "  ·  beat wanders " + track.drift_ms.toFixed(0) + " ms at most";
    box.querySelector(".nudge").textContent = (st.nudge > 0 ? "+" : "") + st.nudge + " ms";
    box.querySelector(".barv").textContent = "beat " + (mod(track.bar_beat + st.bar, 4) + 1);
    box.querySelectorAll(".f").forEach(b => b.classList.toggle("on", +b.dataset.f === st.factor));
  };
  box.querySelector(".name").textContent = track.name;
  box.querySelector(".src").textContent = track.source;
  view.seek.max = track.seconds; view.time.textContent = "0:00 / " + clock(track.seconds);
  const pick = box.querySelector(".pick"); pick.checked = st.pick;
  pick.onchange = () => { st.pick = pick.checked; save(); paint(); };
  const click = box.querySelector(".click"); click.checked = st.click;
  click.onchange = () => { st.click = click.checked; save(); if (cur && cur.track === track) rebook(); };
  box.querySelectorAll(".f").forEach(b => b.onclick = () => { st.factor = +b.dataset.f; save(); paint(); if (cur && cur.track === track) rebook(); });
  box.querySelectorAll(".n").forEach(b => b.onclick = () => { st.nudge += +b.dataset.n; save(); paint(); if (cur && cur.track === track) rebook(); });
  box.querySelector(".bar").onclick = () => { st.bar = mod(st.bar + 1, 4); save(); paint(); if (cur && cur.track === track) rebook(); };
  const note = box.querySelector(".note"); note.value = st.note; note.oninput = () => { st.note = note.value; save(); };
  view.play.onclick = () => { if (cur && cur.track === track) stop(); else play(track, view, +view.seek.value >= track.seconds - 2 ? 0 : +view.seek.value); };
  view.seek.onchange = () => { if (cur && cur.track === track) play(track, view, +view.seek.value); };
  paint(); host.appendChild(box);
}
document.getElementById("copy").onclick = async () => {
  const out = [];
  for (const track of DATA) {
    const st = state[track.source];
    if (st.pick || st.note.trim() || st.factor !== 1 || st.nudge || st.bar) out.push({ source: track.source, pick: st.pick, factor: st.factor, nudge: st.nudge, bar: st.bar, note: st.note.trim() });
  }
  const text = "KN-MUSIC " + JSON.stringify(out);
  try { await navigator.clipboard.writeText(text); status(out.filter(t => t.pick).length + " track(s) picked, copied. Paste into the chat."); }
  catch (e) { window.prompt("Copy this and paste it into the chat:", text); }
};
</script></body></html>
"""


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--shortlist", default=music_shortlist.DEFAULT_OUT)
    p.add_argument("--round", type=int, default=1)
    a = p.parse_args()
    out = OUT if a.round == 1 else os.path.join(OUT, f"round{a.round}")
    media = os.path.join(out, "media")
    os.makedirs(media, exist_ok=True)
    with open(a.shortlist, encoding="utf-8") as f:
        shortlist = json.load(f)
    for old in os.listdir(media):       # previews are numbered by place in the shortlist: an old list's files must not linger
        os.remove(os.path.join(media, old))

    data, failed = [], []
    for n, row in enumerate(shortlist):
        source = os.path.join(tempo.MUSIC, row["path"])
        file = os.path.join(media, f"{n + 1:02d}.ogg")
        try:
            gain = write_preview(source, file, row["rms_db"], row["peak_db"])
            heard = tempo.measure(common.decode(file, seconds=tempo.LONGEST, rate=tempo.RATE))    # the beat of the file that is played
        except Exception as error:
            failed.append(f"{row['path']}: {error}")
            continue
        if "bpm" not in heard:
            failed.append(f"{row['path']}: no beat read on the preview")
            continue
        moved = abs(tempo.wrap((heard["first_beat"] - row["first_beat"]) * heard["bpm"] / 60.0)) * 60.0 / heard["bpm"] * 1000.0
        data.append({
            "file": os.path.relpath(file, out).replace("\\", "/"), "source": row["path"], "name": music_shortlist_name(row["path"]),
            "pack": row["path"].split("/")[1] if "/" in row["path"] else "",
            "seconds": heard["seconds"], "bpm": heard["bpm"], "first_beat": heard["first_beat"], "bar_beat": heard["bar_beat"],
            "bar_from": heard["bar_from"], "drift_ms": heard["drift_ms"], "gain_db": round(gain, 2), "score": round(row.get("score", 0.0), 3),
        })
        print(f"{n + 1:02d}  {heard['bpm']:8.3f} bpm (source {row['bpm']:8.3f})  first beat {heard['first_beat']:.3f}s, {moved:4.1f} ms from the source's  "
              f"gain {gain:+5.1f} dB  {data[-1]['name']}", flush=True)

    data.sort(key=lambda t: (t["pack"], -t["score"]))
    with open(os.path.join(out, "tracks.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    page = os.path.join(out, "index.html")
    with open(page, "w", encoding="utf-8") as f:
        f.write(PAGE.replace("__DATA__", json.dumps(data).replace("</", "<\\/")).replace("__KEY__", f"{PICKS_KEY}{a.round}"))
    print(page, f"{len(data)} tracks")
    for line in failed:
        print("  failed:", line)


def music_shortlist_name(path):
    return clean_name(path)


if __name__ == "__main__":
    sys.exit(main())
