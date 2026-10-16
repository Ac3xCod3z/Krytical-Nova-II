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
function ask(title, text, wantsReason) {
  return new Promise((resolve) => {
    $("dialogTitle").textContent = title;
    $("dialogText").textContent = text;
    $("dialogInput").value = "";
    $("dialogInput").hidden = !wantsReason;
    $("dialogLabel").hidden = !wantsReason;
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
    rows.appendChild(row);
  }
}

function showGms(gms, max) {
  const rows = $("gmRows");
  rows.textContent = "";
  $("noGms").hidden = gms.length > 0;
  $("gmForm").hidden = gms.length >= max;
  $("gmFull").hidden = gms.length < max;
  for (const g of gms) {
    const row = document.createElement("tr");
    cell(row, g.name);
    cell(row, g.online ? "in the game" : "-", g.online ? "on" : "off");
    const td = cell(row, "");
    const remove = document.createElement("button");
    remove.className = "small red";
    remove.type = "button";
    remove.textContent = "Remove";
    remove.addEventListener("click", async () => {
      const text = g.online ? g.name + " is in the game now: their GM rights end at once." : g.name + " can no longer use a GM password here.";
      if (await ask("Remove GM " + g.name + "?", text, false) === null) return;
      act(remove, "/api/gm/remove", { name: g.name });
    });
    td.appendChild(remove);
    rows.appendChild(row);
  }
}

function showList(list, published, statusPort) {
  const rows = $("listRows");
  rows.textContent = "";
  $("noList").hidden = list.length > 0;
  for (const s of list) {
    const row = document.createElement("tr");
    cell(row, s.name);
    cell(row, s.address);
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
    " (the game port + 1). From outside the house both ports are forwarded (step 5).";
}

async function refresh() {
  try {
    const s = await call("/api/status");
    $("serverName").textContent = s.name;
    document.title = s.name;
    $("chipPlayers").textContent = s.players.length + " / " + s.maxPlayers + " players";
    $("chipUptime").textContent = "up " + minutes(s.uptime);
    $("chipMap").textContent = s.map;
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
    showGms(s.gms || [], s.maxGms || 3);
    showList(s.serverList || [], s.listPublished || "-", s.statusPort || 0);
    await refreshLog();
  } catch (e) {
    if (check) { $("pill").textContent = "NO ANSWER"; $("pill").className = "pill down"; }
  }
}

async function refreshLog() {
  const data = await call("/api/log?after=" + lastLog);
  if (!data.lines.length) return;
  lastLog = data.last;
  const log = $("log");
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 30;
  for (const line of data.lines) {
    const div = document.createElement("div");
    const text = line.text;
    const server = /^(\[Server\]|\[Backup\]|\[Save\]|\[GM\]|\[DedicatedServerBoot\]|> )/.test(text);
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
$("publishList").addEventListener("click", async (e) => {
  const button = e.currentTarget;
  if (await ask("Publish the list?", "Every game's Servers window shows exactly this list within a few minutes.", false) === null) return;
  button.textContent = "Publishing...";
  await act(button, "/api/list/publish", {});
  button.textContent = "Publish list";
});
$("gmForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const name = $("gmName").value.trim(), password = $("gmPassword").value;
  if (!name) return toast("Give the GM a name.", true);
  if (password.length < 8) return toast("A GM password needs at least 8 characters.", true);
  const data = await act(event.submitter, "/api/gm/add", { name: name, password: password });
  if (data && / is a GM\./.test(data.answer)) { $("gmName").value = ""; $("gmPassword").value = ""; }
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
