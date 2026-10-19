// The server's control page. Talks only to the server that served it; every button press carries the session's check.
"use strict";

let check = null;      // the session's check token, held only in this page's memory
let lastLog = 0;
let polling = null;
const NOISE = /serialization layout|Did you #if UNITY_EDITOR|referenced script|Dedicated Server Optimizations|Nature\/Soft Occlusion|Unknown managed type|shader|memorysetup|UnloadTime|Unloading \d+|IMGUI module/i;
const $ = (id) => document.getElementById(id);

async function call(path, body) {
  const options = { method: body === undefined ? "GET" : "POST", credentials: "same-origin", headers: {} };
  if (body !== undefined) {
    options.headers["Content-Type"] = "application/json";
    if (check) options.headers["X-KN-Check"] = check;
    options.body = JSON.stringify(body);
  }
  const response = await fetch(path, options);
  let data = {};
  try { data = await response.json(); } catch (e) { /* not json */ }
  if (response.status === 401 && path !== "/api/login") { showLogin(); throw new Error("Log in first."); }
  if (!response.ok) throw new Error(data.error || ("The server said " + response.status + "."));
  return data;
}

function toast(text, bad) {
  const t = $("toast");
  t.textContent = text;
  t.classList.toggle("bad", !!bad);
  t.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => { t.hidden = true; }, 4000);
}

// The page's own question box (a browser's confirm/prompt is ugly and unreliable on phones). Resolves to the text
// typed (empty when asked for none) or null when cancelled.
// With wantsLength, it also asks how long (a ban): the choice is read from $("dialogLength").value afterwards.
const BAN_LENGTHS = [["1h", "1 hour"], ["12h", "12 hours"], ["1d", "1 day"], ["3d", "3 days"], ["1w", "1 week"], ["2w", "2 weeks"], ["30d", "30 days"], ["forever", "for good"]];
function fillLengths(select) {
  select.textContent = "";
  for (const [value, label] of BAN_LENGTHS) { const o = document.createElement("option"); o.value = value; o.textContent = label; select.appendChild(o); }
  select.value = "3d";
}
fillLengths($("dialogLength"));
fillLengths($("banLength"));

function ask(title, text, wantsReason, wantsLength) {
  return new Promise((resolve) => {
    $("dialogTitle").textContent = title;
    $("dialogText").textContent = text;
    $("dialogInput").value = "";
    $("dialogInput").hidden = !wantsReason;
    $("dialogLabel").hidden = !wantsReason;
    $("dialogLength").hidden = !wantsLength;
    $("dialogLengthLabel").hidden = !wantsLength;
    $("dialogLength").value = "3d";
    $("dialog").hidden = false;
    (wantsReason ? $("dialogInput") : $("dialogYes")).focus();
    const done = (value) => {
      $("dialog").hidden = true;
      $("dialogForm").onsubmit = null;
      $("dialogNo").onclick = null;
      resolve(value);
    };
    $("dialogForm").onsubmit = (event) => { event.preventDefault(); done($("dialogInput").value.trim()); };
    $("dialogNo").onclick = () => done(null);
  });
}

document.addEventListener("keydown", (event) => { if (event.key === "Escape" && !$("dialog").hidden) $("dialogNo").click(); });

async function act(button, path, body) {
  if (button) button.disabled = true;
  try {
    const data = await call(path, body || {});
    toast(data.answer || "Done.");
    refresh();
    return data;
  } catch (e) {
    toast(e.message, true);
  } finally {
    if (button) button.disabled = false;
  }
}

// ---------------------------------------------------------------- log in and out

function showLogin() {
  check = null;
  clearInterval(polling);
  polling = null;
  $("main").hidden = true;
  $("login").hidden = false;
  $("password").focus();
}

function showMain() {
  $("login").hidden = true;
  $("main").hidden = false;
  refresh();
  if (!polling) polling = setInterval(refresh, 2000);
}

$("loginForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  $("loginNote").textContent = "";
  try {
    const data = await call("/api/login", { password: $("password").value });
    check = data.check;
    $("password").value = "";
    showMain();
  } catch (e) {
    $("loginNote").textContent = e.message;
  }
});

$("logout").addEventListener("click", async () => {
  try { await call("/api/logout", {}); } catch (e) { /* gone anyway */ }
  showLogin();
});

// ---------------------------------------------------------------- what the server says

function minutes(seconds) {
  if (seconds < 0) return "-";
  const h = Math.floor(seconds / 3600), m = Math.floor((seconds % 3600) / 60);
  return h > 0 ? h + " h " + m + " min" : m + " min";
}

function cell(row, text, className) {
  const td = document.createElement("td");
  td.textContent = text;
  if (className) td.className = className;
  row.appendChild(td);
  return td;
}

function copyText(text) {
  const area = document.createElement("textarea");
  area.value = text;
  document.body.appendChild(area);
  area.select();
  let ok = false;
  try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
  area.remove();
  toast(ok ? "Copied: " + text : "Copy it by hand: " + text, !ok);
}

let shownAddresses = "";
function showAddresses(list, port) {
  const key = list.join(",") + ":" + port;
  if (key === shownAddresses) return;
  shownAddresses = key;
  const box = $("addresses");
  box.textContent = "";
  for (const address of list) {
    const row = document.createElement("div");
    row.className = "row";
    const addr = document.createElement("span");
    addr.className = "addr";
    addr.textContent = address + " : " + port;
    const copy = document.createElement("button");
    copy.className = "small cyan";
    copy.type = "button";
    copy.textContent = "Copy";
    copy.addEventListener("click", () => copyText(address + ":" + port));
    row.append(addr, copy);
    box.appendChild(row);
  }
}

function showPlayers(players) {
  const rows = $("playerRows");
  rows.textContent = "";
  $("noPlayers").hidden = players.length > 0;
  for (const p of players) {
    const row = document.createElement("tr");
    const name = cell(row, p.name);
    if (p.gm) {
      const tag = document.createElement("span");
      tag.className = "gm-tag";
      tag.textContent = "GM";
      tag.title = "Signed in as GM " + p.gm;
      name.prepend(tag);
    }
    cell(row, String(p.level));
    cell(row, p.faction, "hide-phone");
    cell(row, p.map, "hide-phone");
    // Lag, not cheating: ping, and hits too late to count (or on a target already gone) in the last 5 minutes.
    const late = (p.lateHits || 0) + (p.goneTargets || 0);
    const lag = cell(row, (p.ping >= 0 ? p.ping + " ms" : "-") + (late ? " · " + late + " late" : ""));
    lag.title = (p.lateHits || 0) + " hits after their swing closed, " + (p.goneTargets || 0) + " on a target already gone (last 5 minutes)";
    if (p.ping > 250 || late >= 20) lag.className = "lagging";
    const td = cell(row, "");
    const kick = document.createElement("button");
    kick.className = "small red";
    kick.type = "button";
    kick.textContent = "Kick";
    kick.addEventListener("click", async () => {
      const reason = await ask("Kick " + p.name + "?", p.name + " is sent away from the server. They can join again.", true);
      if (reason === null) return;
      act(kick, "/api/kick", { id: String(p.id), reason: reason });
    });
    td.appendChild(kick);
    // GMs are not banned (unbind them first); the server says so too.
    if (!p.gm) {
      const ban = document.createElement("button");
      ban.className = "small red";
      ban.type = "button";
      ban.textContent = "Ban";
      ban.addEventListener("click", async () => {
        const reason = await ask("Ban " + p.name + "?", p.name + "'s account is kept off this server and they are sent away now. They read the reason.", true, true);
        if (reason === null) return;
        act(ban, "/api/ban", { id: String(p.id), text: $("dialogLength").value, reason: reason });
      });
      td.appendChild(ban);
    }
    rows.appendChild(row);
  }
}

// ---- Bans (online phase 2, milestone 4).
let shownBans = "";
function showBans(bans) {
  const key = JSON.stringify(bans);
  if (key === shownBans) return;
  shownBans = key;
  const rows = $("banRows");
  rows.textContent = "";
  $("noBans").hidden = bans.length > 0;
  for (const b of bans) {
    const row = document.createElement("tr");
    cell(row, b.character || "(no character)");
    const account = cell(row, b.account.substring(0, 8) + "…", "hide-phone");
    account.title = b.account;
    cell(row, b.left);
    cell(row, b.by + (b.reason ? " · " + b.reason : "") + " · " + b.since, "hide-phone");
    const td = cell(row, "");
    const unban = document.createElement("button");
    unban.className = "small ghost";
    unban.type = "button";
    unban.textContent = "Unban";
    unban.addEventListener("click", async () => {
      const who = b.character || b.account;
      if (await ask("Unban " + who + "?", who + " may join again at once.", false) === null) return;
      act(unban, "/api/unban", { id: b.account });
    });
    td.appendChild(unban);
    rows.appendChild(row);
  }
}

$("banForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const who = $("banWho").value.trim();
  if (!who) return toast("Type the character name or account id.", true);
  const length = $("banLength").value;
  const label = $("banLength").options[$("banLength").selectedIndex].textContent;
  const reason = await ask("Ban " + who + " (" + label + ")?", who + "'s account is kept off this server, and sent away if they are on.", true);
  if (reason === null) return;
  const data = await act(event.submitter, "/api/ban", { id: who, text: length, reason: reason });
  if (data && /banned/.test(data.answer || "")) $("banWho").value = "";
});

// The table is rebuilt only when it changed, so a player list picked from is not reset under the pointer every 2 seconds.
let shownGms = "";
function showGms(gms, max, players) {
  const key = JSON.stringify(gms) + "|" + players.map((p) => p.id + p.name + (p.gm || "")).join(",");
  if (key === shownGms) return;
  shownGms = key;
  const rows = $("gmRows");
  rows.textContent = "";
  $("noGms").hidden = gms.length > 0;
  $("gmForm").hidden = gms.length >= max;
  $("gmFull").hidden = gms.length < max;
  for (const g of gms) {
    const row = document.createElement("tr");
    cell(row, g.name);
    const account = cell(row, "");
    if (g.bound) {
      account.textContent = "bound ";
      const unbind = document.createElement("button");
      unbind.className = "small ghost";
      unbind.type = "button";
      unbind.textContent = "Unbind";
      unbind.addEventListener("click", async () => {
        if (await ask("Unbind " + g.name + "?", "Their account stops being GM" + (g.online ? ", at once." : "."), false) === null) return;
        act(unbind, "/api/gm/unbind", { name: g.name });
      });
      account.appendChild(unbind);
    } else {
      // Only players not already a GM, who are in the game now.
      const free = players.filter((p) => !p.gm);
      const pick = document.createElement("select");
      pick.innerHTML = "<option value=''>" + (free.length ? "pick the player..." : "nobody to bind is on") + "</option>";
      for (const p of free) { const o = document.createElement("option"); o.value = String(p.id); o.textContent = p.name; pick.appendChild(o); }
      pick.disabled = free.length === 0;
      const bind = document.createElement("button");
      bind.className = "small cyan";
      bind.type = "button";
      bind.textContent = "Bind";
      bind.addEventListener("click", async () => {
        if (!pick.value) return toast("Pick the player who is " + g.name + ".", true);
        const who = pick.options[pick.selectedIndex].textContent;
        if (await ask("Bind " + g.name + " to " + who + "?", who + "'s account becomes GM " + g.name + " now and every time they join.", false) === null) return;
        act(bind, "/api/gm/bind", { name: g.name, id: pick.value });
      });
      account.append(pick, bind);
    }
    cell(row, g.online ? "in the game" : "-", g.online ? "on" : "off");
    const td = cell(row, "");
    const remove = document.createElement("button");
    remove.className = "small red";
    remove.type = "button";
    remove.textContent = "Remove";
    remove.addEventListener("click", async () => {
      const text = g.online ? g.name + " is in the game now: their GM rights end at once." : g.name + " is no longer a GM here.";
      if (await ask("Remove GM " + g.name + "?", text, false) === null) return;
      act(remove, "/api/gm/remove", { name: g.name });
    });
    td.appendChild(remove);
    rows.appendChild(row);
  }
}

let currentMode = "direct";
function showReach(s) {
  currentMode = s.mode || "direct";
  const relay = currentMode === "relay";
  $("modeTag").textContent = relay ? "RELAY" : "DIRECT";
  $("modeTag").classList.toggle("relay", relay);
  $("switchMode").textContent = relay ? "Switch to Direct" : "Switch to Relay";
  const where = s.publicAddress ? "This PC's internet address: " + s.publicAddress + "." : "This PC's internet address: not looked up yet.";
  $("reachNote").textContent = relay
    ? (s.relayNote || "Asking Unity Relay for a place...") + " No router work needed."
    : where + " Players come to it on UDP ports " + s.port + " and " + s.statusPort + ", which the router forwards to this PC.";
  $("autoNote").textContent = s.autoNote || "";
}

function showList(list, published, statusPort, publicAddress) {
  const rows = $("listRows");
  rows.textContent = "";
  $("noList").hidden = list.length > 0;
  for (const s of list) {
    const row = document.createElement("tr");
    cell(row, s.name);
    cell(row, s.auto ? "auto (this server" + (publicAddress ? ": " + publicAddress : "") + ")" : s.address);
    cell(row, String(s.port), "hide-phone");
    const td = cell(row, "");
    const remove = document.createElement("button");
    remove.className = "small red";
    remove.type = "button";
    remove.textContent = "Remove";
    remove.addEventListener("click", async () => {
      if (await ask("Remove " + s.name + "?", "It leaves the list here; players stop seeing it once you PUBLISH LIST.", false) === null) return;
      act(remove, "/api/list/remove", { id: String(s.id) });
    });
    td.appendChild(remove);
    rows.appendChild(row);
  }
  $("listPublished").textContent = "Last published: " + published;
  $("statusPortNote").textContent = "This server tells the Servers window it is on, its players and its build on UDP port " + statusPort +
    " (the game port + 1). In Direct mode the router forwards both ports to this PC; in Relay mode players see RELAY instead.";
}

async function refresh() {
  try {
    const s = await call("/api/status");
    $("serverName").textContent = s.name;
    document.title = s.name;
    $("chipPlayers").textContent = s.players.length + " / " + s.maxPlayers + " players";
    $("chipUptime").textContent = "up " + minutes(s.uptime);
    $("chipMap").textContent = s.map;
    $("chipLoad").textContent = s.load || "-";
    $("chipLoad").classList.toggle("warn", (s.loadSlow || 0) > 0);
    $("chipSaves").textContent = s.saves || "-";
    $("chipSaves").classList.toggle("down", s.savesOk === false);
    $("chipBackup").textContent = s.nextBackup >= 0 ? "next backup in " + minutes(s.nextBackup) : "backups at start and stop";
    $("lastBackup").textContent = "Last backup: " + s.lastBackup + " · " + s.backupsKept + " kept";
    const pill = $("pill");
    pill.className = "pill";
    if (s.stopping) { pill.textContent = "STOPPING"; pill.classList.add("down"); }
    else if (s.shutdownIn >= 0) { pill.textContent = (s.restarting ? "RESTART IN " : "STOP IN ") + s.shutdownIn + " S"; pill.classList.add("warn"); }
    else if (s.listening) pill.textContent = "ONLINE";
    else { pill.textContent = "STARTING"; pill.classList.add("warn"); }
    $("cancel").hidden = s.shutdownIn < 0;
    $("countdown").textContent = s.shutdownIn >= 0 ? (s.restarting ? "Restarting" : "Stopping") + " in " + s.shutdownIn + " seconds. Players are being told." : "";
    showAddresses(s.addresses, s.port);
    showPlayers(s.players);
    showGms(s.gms || [], s.maxGms || 3, s.players || []);
    showBans(s.bans || []);
    showReach(s);
    showList(s.serverList || [], s.listPublished || "-", s.statusPort || 0, s.publicAddress || "");
    await refreshLog();
    await refreshFlags();
  } catch (e) {
    if (check) { $("pill").textContent = "NO ANSWER"; $("pill").className = "pill down"; }
  }
}

// ---- Flags (online phase 2, milestone 3): foul play, live. A repeat comes back with a new id and a higher count, and
// replaces its card (keyed by player, request, reason and action), moving it to the top.
let lastFlag = 0;
const flagCards = new Map();
let flagTotal = 0;

async function refreshFlags() {
  const data = await call("/api/flags?after=" + lastFlag);
  if (!data.flags.length) return;
  lastFlag = data.last;
  const list = $("flags");
  $("flagsEmpty").hidden = true;
  for (const f of data.flags) {
    const key = [f.connection, f.call, f.why, f.action].join("|");
    let card = flagCards.get(key);
    if (!card) { card = document.createElement("div"); flagCards.set(key, card); flagTotal++; }
    card.className = "flag fresh" + (f.action === "Disconnected" ? " kick" : "");
    card.dataset.search = (f.character + " " + f.account + " " + f.map + " " + f.call).toLowerCase();
    card.replaceChildren();
    const when = document.createElement("span"); when.className = "when"; when.textContent = f.time;
    const who = document.createElement("span"); who.className = "who"; who.textContent = f.character + " ";
    const acct = document.createElement("small"); acct.textContent = "account " + f.account + " · connection " + f.connection; who.appendChild(acct);
    const act = document.createElement("span"); act.className = "act"; act.textContent = f.action;
    if (f.count > 1) { const n = document.createElement("span"); n.className = "count"; n.textContent = "x" + f.count; who.appendChild(n); }
    const detail = document.createElement("span"); detail.className = "detail"; detail.textContent = f.what + " — " + f.why + ".";
    const where = document.createElement("span"); where.className = "where"; where.textContent = f.map + " at (" + f.where + ") · request " + f.call;
    card.append(when, who, act, detail, where);
    list.insertBefore(card, $("flagsEmpty").nextSibling);
    setTimeout(() => card.classList.remove("fresh"), 1300);
  }
  while (flagCards.size > 300) { const [k, c] = flagCards.entries().next().value; c.remove(); flagCards.delete(k); }
  $("flagCount").textContent = flagTotal + (flagTotal === 1 ? " flag" : " flags");
  $("flagCount").className = "chip warn";
  filterFlags();
}

function filterFlags() {
  const q = $("flagFilter").value.trim().toLowerCase();
  for (const card of flagCards.values()) card.hidden = !!q && !card.dataset.search.includes(q);
}
$("flagFilter").addEventListener("input", filterFlags);
$("flagClear").addEventListener("click", () => {
  for (const card of flagCards.values()) card.remove();
  flagCards.clear(); flagTotal = 0;
  $("flagsEmpty").hidden = false; $("flagCount").textContent = "none yet"; $("flagCount").className = "chip";
});

async function refreshLog() {
  const data = await call("/api/log?after=" + lastLog);
  if (!data.lines.length) return;
  lastLog = data.last;
  const log = $("log");
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 30;
  for (const line of data.lines) {
    const div = document.createElement("div");
    const text = line.text;
    const server = /^(\[Server\]|\[Backup\]|\[Save\]|\[GM\]|\[GM log\]|\[Access\]|\[Economy\]|\[Flag\]|\[DedicatedServerBoot\]|> )/.test(text);
    // A server leaves out everything that only draws or plays sound, and Unity says so at length: not a problem.
    const noise = !server && NOISE.test(text);
    div.textContent = text;
    if (server) div.className = "srv";
    else if (text.startsWith("(error)") && !noise) div.className = "err";
    else div.dataset.noise = "1";
    div.hidden = !!div.dataset.noise && !$("everything").checked;
    log.appendChild(div);
  }
  while (log.childElementCount > 600) log.firstElementChild.remove();
  if (atBottom) log.scrollTop = log.scrollHeight;
}

$("everything").addEventListener("change", () => {
  for (const div of $("log").querySelectorAll("[data-noise]")) div.hidden = !$("everything").checked;
});

// ---------------------------------------------------------------- buttons

$("save").addEventListener("click", (e) => act(e.currentTarget, "/api/save", {}));
$("backup").addEventListener("click", (e) => act(e.currentTarget, "/api/backup", {}));
$("announceForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const text = $("announceText").value.trim();
  if (!text) return toast("Type the announcement first.", true);
  const data = await act(event.submitter, "/api/announce", { text: text });
  if (data) $("announceText").value = "";
});
async function shutdown(button, restart) {
  const seconds = Math.max(0, Math.min(3600, parseInt($("seconds").value, 10) || 0));
  const what = restart ? "Restart" : "Stop";
  const text = restart
    ? "In " + seconds + " seconds everyone is saved, a backup is made, and the server stops and starts again. Players are told as it counts down."
    : "In " + seconds + " seconds everyone is saved, a backup is made, and the server stops. Players are told as it counts down. Start it again with Start Server.bat.";
  const reason = await ask(what + " the server?", text, true);
  if (reason === null) return;
  act(button, "/api/stop", { seconds: seconds, reason: reason, restart: restart });
}
$("restart").addEventListener("click", (e) => shutdown(e.currentTarget, true));
$("stop").addEventListener("click", (e) => shutdown(e.currentTarget, false));
$("cancel").addEventListener("click", (e) => act(e.currentTarget, "/api/cancel", {}));
$("listForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = await act(event.submitter, "/api/list/add",
    { name: $("listName").value.trim(), address: $("listAddress").value.trim(), port: parseInt($("listPort").value, 10) || 7777 });
  if (data && / is on the list\./.test(data.answer)) { $("listName").value = ""; $("listAddress").value = ""; }
});
$("listAuto").addEventListener("click", () => {
  $("listAddress").value = "auto";
  if (!$("listName").value.trim()) $("listName").value = $("serverName").textContent.slice(0, 32);
});
$("switchMode").addEventListener("click", async (e) => {
  const toRelay = currentMode !== "relay";
  const seconds = Math.max(0, Math.min(3600, parseInt($("seconds").value, 10) || 0));
  const text = toRelay
    ? "Players will come through Unity Relay: no router work, and your address stays hidden. The server saves everyone, restarts in " + seconds + " seconds and publishes the list with its Relay code by itself."
    : "Players will come straight to this PC through the router's forwarded ports. The server saves everyone, restarts in " + seconds + " seconds and publishes the list with this PC's address by itself.";
  if (await ask(toRelay ? "Switch to Relay?" : "Switch to Direct?", text, false) === null) return;
  act(e.currentTarget, "/api/mode", { text: toRelay ? "relay" : "direct", seconds: seconds });
});
$("publishList").addEventListener("click", async (e) => {
  const button = e.currentTarget;
  if (await ask("Publish the list?", "Every game's Servers window shows exactly this list within a few minutes.", false) === null) return;
  button.textContent = "Publishing...";
  await act(button, "/api/list/publish", {});
  button.textContent = "Publish list";
});
$("gmForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const name = $("gmName").value.trim();
  if (!name) return toast("Give the GM a name.", true);
  const data = await act(event.submitter, "/api/gm/add", { name: name });
  if (data && / is a GM\./.test(data.answer)) $("gmName").value = "";
});
$("commandForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const line = $("commandLine").value.trim();
  if (!line) return;
  const button = event.submitter;
  button.disabled = true;
  try {
    const data = await call("/api/command", { line: line });
    $("commandAnswer").textContent = "> " + line + "\n" + data.answer;
  } catch (e) {
    $("commandAnswer").textContent = "> " + line + "\n" + e.message;
  } finally {
    button.disabled = false;
  }
});

// ---------------------------------------------------------------- start

(async () => {
  try {
    const data = await call("/api/session");
    check = data.check;
    showMain();
  } catch (e) {
    showLogin();
  }
})();
