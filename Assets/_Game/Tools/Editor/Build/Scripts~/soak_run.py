"""Runs one stage of the DOTS soak (wave 4, 12 Nov 2026) on the TEST lane, with no Editor open.

    python soak_run.py S0|S1|S2|S3|S4|S1-relevancy-on|S1-relevancy-off   run that stage, write its result page
    python soak_run.py stop                                           stop everything the last run started (its own PIDs)
    python soak_run.py --check                                        only the gates: RAM, other heavy programs, ports

What it touches: the entity TEST server (Builds/EntityServer, ports 7799 / page 8099), the TEST save service (SaveService --test,
port 7802, database krytical_nova_test) and the bot host (Builds/Bots/EntityBotHost). It never opens 7777, 7778 or 7801; the one
look at the live server is GET /status on 8090 (players, build), written at the top of the result page (lead review change 3).
Secrets: the page password is read from the test folder's settings at run time and never printed, logged or copied (the run
folder's copy of the settings has its secret keys blanked). Everything it starts runs inside one Windows job object and is stopped
by its own PIDs, never by name (memory "watchdogs kill own tree only"). Logs and CSVs stay in the run folder and go to Drive, never
to git. Max 2 runs per stage, then a person decides (owner rule): the script counts the runs of a stage in its folder.
Plain Python 3, standard library only.
"""
import ctypes
import datetime
import glob
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
from ctypes import wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, *[".."] * 6))

# ------------------------------------------------------------------ settings (numbers here, not in code below)

SETTINGS = {
    "server_root": os.path.join(PROJECT, "Builds", "EntityServer"),
    "bot_host": os.path.join(PROJECT, "Builds", "Bots", "EntityBotHost", "KnBotHost.exe"),
    "runs_root": os.path.join(PROJECT, "Builds", "Bots", "SoakRuns"),
    "drive_results": r"G:\My Drive\KryticalNova\Docs\SoakRuns",
    "soak_spots": os.path.join(PROJECT, "Assets", "_Game", "Gameplay", "Hostiles", "Data", "SoakSpots.asset"),
    "game_port": 7799, "page_port": 8099, "save_port": 7802,
    "live_status": "http://127.0.0.1:8090/status",
    "real_ports": (7777, 7778, 7801, 8090),
    # lead review change 2: a RAM gate per stage (GB free to start; the stage stops below the floor)
    "ram_start_gb": {"S0": 10, "S1": 10, "S2": 10, "S3": 10, "S4": 20},
    "ram_floor_gb": 4,
    "heavy_programs": ("Unity.exe", "Blender.exe", "blender.exe", "UniMate.exe"),
    "runs_per_stage": 2,
    "poll_seconds": 2, "ready_timeout_s": 180,
    # pass marks (design section 4)
    "tick_p99_ms": 15.0, "tick_p95_ms": 15.0, "brains_p99_102_ms": 0.3, "brains_p99_2000_ms": 2.0, "ram_flat_share": 0.05,
}

# stage: hostiles, bots, minutes, ramp (step, every s), GM lines after the bots are in, server extra args, who
STAGES = {
    "S0": dict(bots=0, minutes=5, step=0, every=0, lines=[], extra=[], who="session", hostiles="0"),
    "S1": dict(bots=50, minutes=30, step=5, every=120, lines=["gm.zone.list"], extra=[], who="session", hostiles="102 (every zone woken)"),
    "S2": dict(bots=50, minutes=15, step=0, every=0, lines=["gm.zone.respawn 2"] + ["gm.hostile.spawn {species} 50"] * 6 + ["gm.hostile.stats"],
               extra=[], who="session", hostiles="300 (cap)"),
    "S3": dict(bots=0, minutes=10, step=0, every=0, lines=[], extra=["-knbrainsoak", "2000"], who="session", hostiles="2,000 synthetic"),
    "S4": dict(bots=20, minutes=60, step=0, every=0, lines=[], extra=[], who="owner", hostiles="102 + 2 real clients fighting"),
    "S1-relevancy-on": dict(bots=50, minutes=30, step=5, every=120, lines=["gm.zone.list"], extra=[], who="session", hostiles="102", relevancy=True),
    "S1-relevancy-off": dict(bots=50, minutes=30, step=5, every=120, lines=["gm.zone.list"], extra=[], who="session", hostiles="102", relevancy=False),
}
EQUIP = ["gm.level max", "gm.skills.max", "gm.autopilot.time {seconds}"]
SECRET_KEYS = ("pagePassword", "password", "key", "secret", "token", "saveServiceKey")

THIN_BOT_NOTE = (
    "What thin bots do NOT measure (lead review change 4): the server plays them, so this soak does not load the command / input "
    "path of real players, prediction and rollback, lag compensation on real aim, or any client-side cost, and it OVER-counts "
    "Autopilot's server cost (every bot hunts by it). \"N bots pass\" is a floor for snapshots, brains, saves and the ledger, not "
    "for N real players: the 1-3 full KnTestClient bots and S4's two real clients cover the rest, and the 60 / 150 / 250 ms and "
    "prediction numbers stay owed to real clients.")


def say(text):
    print(time.strftime("%H:%M:%S"), text, flush=True)


# ------------------------------------------------------------------ the PC

class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [("dwLength", wintypes.DWORD), ("dwMemoryLoad", wintypes.DWORD), ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong), ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong), ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


def free_gb():
    m = MEMORYSTATUSEX()
    m.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    return m.ullAvailPhys / (1024 ** 3)


def heavy_running():
    out = subprocess.run(["tasklist", "/fo", "csv", "/nh"], capture_output=True, text=True).stdout
    names = {line.split('","')[0].strip('"') for line in out.splitlines() if line}
    return sorted(n for n in names if n in SETTINGS["heavy_programs"])


def port_free(port):
    for kind in (socket.SOCK_STREAM, socket.SOCK_DGRAM):
        s = socket.socket(socket.AF_INET, kind)
        try:
            s.bind(("127.0.0.1", port))
        except OSError:
            return False
        finally:
            s.close()
    return True


def live_server_look():
    """The one allowed look at the live server: GET /status on 8090 (players, build). Nothing else is read or sent."""
    try:
        with urllib.request.urlopen(SETTINGS["live_status"], timeout=4) as r:
            data = json.loads(r.read().decode("utf-8", "replace") or "{}")
        players = data.get("players")
        count = len(players) if isinstance(players, list) else players
        return True, count, data.get("build") or data.get("version")
    except Exception:
        return False, None, None


def gates(stage):
    problems = []
    need = SETTINGS["ram_start_gb"].get(stage.split("-")[0], 10)
    free = free_gb()
    if free < need:
        problems.append(f"{free:.1f} GB free RAM, the stage needs {need} GB to start (quit Reallusion Hub / Razer etc.: the owner decides)")
    heavy = heavy_running()
    if heavy:
        problems.append("other heavy programs run: " + ", ".join(heavy))
    for port in (SETTINGS["game_port"], SETTINGS["page_port"], SETTINGS["save_port"]):
        if not port_free(port):
            problems.append(f"port {port} is in use (an old test server or save service still running?)")
    return problems, free, heavy


# ------------------------------------------------------------------ one job object: everything started dies with it

class Job:
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000

    def __init__(self):
        k = ctypes.windll.kernel32
        self.k = k
        self.handle = k.CreateJobObjectW(None, None)

        class LIMIT(ctypes.Structure):
            _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64), ("LimitFlags", wintypes.DWORD),
                        ("MinimumWorkingSetSize", ctypes.c_size_t), ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                        ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD), ("SchedulingClass", wintypes.DWORD)]

        class IO(ctypes.Structure):
            _fields_ = [(n, ctypes.c_ulonglong) for n in ("a", "b", "c", "d", "e", "f")]

        class EXT(ctypes.Structure):
            _fields_ = [("Basic", LIMIT), ("Io", IO), ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                        ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]

        info = EXT()
        info.Basic.LimitFlags = self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        k.SetInformationJobObject(self.handle, 9, ctypes.byref(info), ctypes.sizeof(info))   # 9 = extended limit information
        self.processes = []

    def start(self, name, args, cwd, log_path):
        log = open(log_path, "ab")
        p = subprocess.Popen(args, cwd=cwd, stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
        h = self.k.OpenProcess(0x1F0FFF, False, p.pid)
        self.k.AssignProcessToJobObject(self.handle, h)
        self.k.CloseHandle(h)
        self.processes.append((name, p))
        say(f"started {name} (pid {p.pid})")
        return p

    def stop(self):
        for name, p in reversed(self.processes):
            if p.poll() is None:
                say(f"stopping {name} (pid {p.pid})")
        self.k.TerminateJobObject(self.handle, 0)
        self.k.CloseHandle(self.handle)


def stop_last_run():
    """'stop': the PIDs the last run wrote down, each with its own tree; never anything by name."""
    files = sorted(glob.glob(os.path.join(SETTINGS["runs_root"], "*", "pids.txt")))
    if not files:
        say("no run to stop")
        return
    for line in open(files[-1], encoding="utf-8"):
        parts = line.split()
        if len(parts) == 2 and parts[1].isdigit():
            subprocess.run(["taskkill", "/T", "/F", "/PID", parts[1]], capture_output=True)
            say(f"stopped {parts[0]} (pid {parts[1]}) if it still ran")


# ------------------------------------------------------------------ the test server, its page, its save service

def server_folder():
    folders = [f for f in glob.glob(os.path.join(SETTINGS["server_root"], "*")) if glob.glob(os.path.join(f, "*.exe"))
               and os.path.exists(os.path.join(f, "server-settings.json"))]
    if not folders:
        raise SystemExit("no test-lane entity server in " + SETTINGS["server_root"] + " (build it first: a heavy job, the lead's slot)")
    return folders[0]


def server_exe(folder):
    exes = [e for e in glob.glob(os.path.join(folder, "*.exe")) if "UnityCrashHandler" not in e]
    return exes[0]


def save_service_exe():
    found = glob.glob(os.path.join(SETTINGS["server_root"], "**", "SaveService.exe"), recursive=True)
    if not found:
        raise SystemExit("no SaveService.exe copy beside the entity server")
    return found[0]


def prepare_settings(folder, run_folder, relevancy):
    """Adds devBots (and the run's performance log folder, relevancy) to the TEST folder's settings; refuses real ports."""
    path = os.path.join(folder, "server-settings.json")
    data = json.load(open(path, encoding="utf-8-sig"))
    if data.get("port") in SETTINGS["real_ports"] or data.get("pagePort") in SETTINGS["real_ports"] or "7801" in str(data.get("saveService", "")):
        raise SystemExit("these settings name the real server's ports: not a test-lane folder, nothing started")
    data["devBots"] = True
    data["performanceLogFolder"] = run_folder
    if relevancy is not None:
        data["relevancy"] = relevancy
    json.dump(data, open(path, "w", encoding="utf-8"), indent=2)
    blank = {k: ("" if any(s.lower() in k.lower() for s in SECRET_KEYS) else v) for k, v in data.items()}
    json.dump(blank, open(os.path.join(run_folder, "server-settings.copy.json"), "w", encoding="utf-8"), indent=2)
    return data


class Page:
    """The TEST server's control page (8099). The password is read here and never printed."""

    def __init__(self, settings):
        self.base = f"http://127.0.0.1:{SETTINGS['page_port']}"
        self.password = settings.get("pagePassword") or ""
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor())
        self.check = None

    def _send(self, path, body=None):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method="POST" if data else "GET",
                                     headers={"Origin": self.base, "Content-Type": "application/json"})
        if self.check:
            req.add_header("X-KN-Check", self.check)
        with self.opener.open(req, timeout=8) as r:
            return r.read().decode("utf-8", "replace")

    def login(self):
        if self.check is None and self.password:
            answer = self._send("/api/login", {"password": self.password})
            self.check = (re.search(r'"check"\s*:\s*"([^"]*)"', answer) or [None, ""])[1]

    def status(self):
        try:
            self.login()
            return self._send("/api/status")
        except Exception:
            return None

    def command(self, line):
        try:
            self.login()
            return self._send("/api/command", {"line": line})
        except Exception as e:
            return "FAILED: " + type(e).__name__


def wait_for_page(page, what):
    started = time.time()
    while time.time() - started < SETTINGS["ready_timeout_s"]:
        if page.status() is not None:
            say(f"{what}: the page answers")
            return True
        say(f"{what}: waiting for the page ({time.time() - started:.0f} s)")
        time.sleep(SETTINGS["poll_seconds"])
    return False


def bots_in_status(status):
    return re.findall(r'\{"id":(\d+),"name":"(Knbot\d+)"', status or "")


# ------------------------------------------------------------------ the result page

def csv_rows(run_folder):
    files = sorted(glob.glob(os.path.join(run_folder, "server_*.csv")))
    if not files:
        return []
    import csv
    with open(files[-1], newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def num(row, key):
    try:
        return float(row.get(key) or "")
    except ValueError:
        return None


def judge(stage, run_folder, bot_exit, minutes):
    rows = csv_rows(run_folder)
    lines = []

    def mark(name, ok, detail):
        lines.append(f"| {name} | {'PASS' if ok else ('n/a' if ok is None else 'FAIL')} | {detail} |")

    after = [r for r in rows[12:]]   # 5 s windows: skip the first minute
    p99 = [v for v in (num(r, "tick_p99_ms") for r in rows) if v is not None]
    p95 = [v for v in (num(r, "busy_p95_ms") for r in rows) if v is not None]
    brains = [v for v in (num(r, "brains_ms") for r in rows) if v is not None]
    slow = sum(int(num(r, "slow_frames") or 0) for r in after)
    ram = [v for v in (num(r, "private_mb") for r in rows) if v is not None]
    mark("Whole-server frame busy time p99 < 15 ms (tick_p99_ms is the frame's busy time, not one simulation tick's)", (max(p99) < SETTINGS["tick_p99_ms"]) if p99 else None, f"max p99 {max(p99):.2f} ms" if p99 else "no CSV")
    if stage.startswith("S1"):
        mark("Tick p95 < 15 ms (102 Hostiles + 50 bots)", (max(p95) < SETTINGS["tick_p95_ms"]) if p95 else None, f"max p95 {max(p95):.2f} ms" if p95 else "no CSV")
        mark("Brains < 0.3 ms at 102 (longest tick of each second)", (max(brains) < SETTINGS["brains_p99_102_ms"]) if brains else None,
             f"max {max(brains):.3f} ms" if brains else "no brains column")
    if stage == "S3":
        soak = re.findall(r"p99 ([0-9.]+) ms", read_text(os.path.join(run_folder, "server.log")))
        worst = max(float(v) for v in soak[1:]) if len(soak) > 1 else None
        mark("Brains p99 < 2 ms at 2,000 ([KnSoak] lines, first left out)", (worst < SETTINGS["brains_p99_2000_ms"]) if worst is not None else None,
             f"worst p99 {worst:.3f} ms" if worst is not None else "no [KnSoak] line")
    mark("Slow server frames (> 33.3 ms) after the first minute = 0", slow == 0 if after else None, f"{slow}")
    if minutes >= 60 and len(ram) >= 120:
        early = sum(ram[120:240]) / max(1, len(ram[120:240]))
        late = sum(ram[-120:]) / 120
        mark("RAM flat (last 10 min within 5 % of minutes 10-20)", abs(late - early) <= early * SETTINGS["ram_flat_share"], f"{early:.0f} -> {late:.0f} MB")
    else:
        mark("RAM flat", None, "only for a 60-minute run")
    errors = len(re.findall(r"Exception|\[Error\]", read_text(os.path.join(run_folder, "server.log"))))
    host = read_text(os.path.join(run_folder, "bots", "botshost.result")).strip()
    mark("No exceptions in the server log", errors == 0, f"{errors} lines")
    mark("Bot host exit 0", None if bot_exit is None else bot_exit == 0, host or ("no bot host in this stage" if bot_exit is None else "no result file"))
    mark("Economy (TEST ledger)", None, "read the TEST database's ledger lines for this run (krytical_nova_test only)")
    return lines


def read_text(path):
    try:
        return open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def write_result(stage, run_folder, pc, lines, gm_answers):
    live_up, live_players, live_build = pc["live"]
    for_record = not (live_up and live_players)
    out = [f"# Soak {stage}: {os.path.basename(run_folder)}", "",
           "## What the PC was doing (lead review change 3)", "",
           f"- Live server (GET /status on 8090): {'up' if live_up else 'not answering'}"
           + (f", {live_players} players, build {live_build}" if live_up else ""),
           f"- Free RAM: {pc['free_start']:.1f} GB at the start, {pc['free_low']:.1f} GB lowest",
           f"- Other heavy programs: {', '.join(pc['heavy']) or 'none'}",
           f"- **{'For the record' if for_record else 'NOT for the record: live players were in; never compare it before / after'}**"
           + (" (needs the owner's window: \"nobody is playing\")" if for_record else ""), "",
           "## Pass marks", "", "| Measure | Verdict | Number |", "|---|---|---|"] + lines + ["",
           "## GM lines sent (answers shortened)", ""] + [f"- `{l}`: {a[:160]}" for l, a in gm_answers] + ["",
           "## What this soak does not measure", "", THIN_BOT_NOTE, ""]
    path = os.path.join(run_folder, "soak-result.md")
    open(path, "w", encoding="utf-8").write("\n".join(out))
    try:
        os.makedirs(SETTINGS["drive_results"], exist_ok=True)
        dest = os.path.join(SETTINGS["drive_results"], os.path.basename(run_folder))
        shutil.copytree(run_folder, dest, ignore=shutil.ignore_patterns("server-settings.json"), dirs_exist_ok=True)
        say("copied the run folder to " + dest)
    except OSError as e:
        say("could not copy to Drive: " + str(e))
    say("result page: " + path)


# ------------------------------------------------------------------ one stage

def soak_centre():
    text = read_text(SETTINGS["soak_spots"])
    m = re.search(r"centre:\s*\{x:\s*([-0-9.eE]+),\s*y:\s*([-0-9.eE]+)\}", text)
    return f"{float(m.group(1)):.1f},{float(m.group(2)):.1f}" if m else None


def run_stage(stage):
    spec = STAGES[stage]
    os.makedirs(SETTINGS["runs_root"], exist_ok=True)
    done = glob.glob(os.path.join(SETTINGS["runs_root"], f"*-{stage}"))
    if len(done) >= SETTINGS["runs_per_stage"]:
        raise SystemExit(f"{stage} ran {len(done)} times already: a person decides what next (owner rule, max 2 runs)")
    problems, free, heavy = gates(stage)
    if problems:
        for p in problems:
            say("GATE: " + p)
        raise SystemExit("gates not met: nothing started")
    pc = {"live": live_server_look(), "free_start": free, "free_low": free, "heavy": heavy}

    run_folder = os.path.join(SETTINGS["runs_root"], datetime.datetime.now().strftime("%Y%m%d-%H%M") + "-" + stage)
    os.makedirs(os.path.join(run_folder, "bots"))
    folder = server_folder()
    settings = prepare_settings(folder, run_folder, spec.get("relevancy"))
    page = Page(settings)
    job = Job()
    pids = open(os.path.join(run_folder, "pids.txt"), "w", encoding="utf-8")
    bot_exit = None
    gm_answers = []
    try:
        save = job.start("save service (TEST)", [save_service_exe(), "--test"], os.path.dirname(save_service_exe()), os.path.join(run_folder, "saveservice.log"))
        pids.write(f"saveservice {save.pid}\n"); pids.flush()
        extra = list(spec["extra"])
        if "-knbrainsoak" in extra and soak_centre():
            extra += ["-knbrainsoakat", soak_centre()]
        server = job.start("test server", [server_exe(folder), "-batchmode", "-nographics", "-devbots", "-logFile", os.path.join(run_folder, "server.log")] + extra,
                           folder, os.path.join(run_folder, "server.out.log"))
        pids.write(f"server {server.pid}\n"); pids.flush()
        if not wait_for_page(page, "test server"):
            raise SystemExit("the test server's page never answered")
        seconds = spec["minutes"] * 60
        host = None
        if spec["bots"] > 0:
            args = [SETTINGS["bot_host"], "-batchmode", "-nographics", "-knbots", str(spec["bots"]), "-port", str(SETTINGS["game_port"]),
                    "-connect", "127.0.0.1", "-seconds", str(seconds), "-out", os.path.join(run_folder, "bots")]
            if spec["step"]:
                args += ["-knbotstep", str(spec["step"]), "-knbotevery", str(spec["every"])]
            host = job.start("bot host", args, os.path.dirname(SETTINGS["bot_host"]), os.path.join(run_folder, "bots", "host.out.log"))
            pids.write(f"bothost {host.pid}\n"); pids.flush()
        equipped = set()
        sent_lines = False
        started = time.time()
        while time.time() - started < seconds + 30:
            free = free_gb()
            pc["free_low"] = min(pc["free_low"], free)
            if free < SETTINGS["ram_floor_gb"]:
                say(f"STOP: {free:.1f} GB free, under the {SETTINGS['ram_floor_gb']} GB floor")
                break
            if server.poll() is not None:
                say(f"the test server ended (exit {server.returncode})")
                break
            status = page.status()
            for bot_id, name in bots_in_status(status):
                if bot_id in equipped:
                    continue
                gm_answers.append((f"gm.target {bot_id}", page.command(f"gm.target {bot_id}")))
                for line in EQUIP:
                    page.command(line.format(seconds=seconds))
                equipped.add(bot_id)
            if not sent_lines and (spec["bots"] == 0 or len(equipped) >= min(spec["bots"], spec["step"] or spec["bots"])):
                species = "prowler"
                for line in spec["lines"]:
                    l = line.format(species=species)
                    gm_answers.append((l, page.command(l)))
                sent_lines = True
            if host is not None and host.poll() is not None:
                bot_exit = host.returncode
                say(f"the bot host ended (exit {bot_exit})")
                if time.time() - started >= seconds:
                    break
            say(f"{stage}: {time.time() - started:.0f}/{seconds} s, bots equipped {len(equipped)}, free RAM {free:.1f} GB")
            time.sleep(10)
        if host is not None and bot_exit is None and host.poll() is not None:
            bot_exit = host.returncode
    finally:
        pids.close()
        job.stop()
    write_result(stage, run_folder, pc, judge(stage, run_folder, bot_exit, spec["minutes"]), gm_answers)


def main():
    args = sys.argv[1:]
    if not args:
        raise SystemExit(__doc__)
    if args[0] == "stop":
        stop_last_run()
    elif args[0] == "--check":
        problems, free, heavy = gates(args[1] if len(args) > 1 else "S1")
        say(f"free RAM {free:.1f} GB, heavy programs: {', '.join(heavy) or 'none'}")
        for p in problems:
            say("GATE: " + p)
        sys.exit(1 if problems else 0)
    elif args[0] in STAGES:
        run_stage(args[0])
    else:
        raise SystemExit("unknown stage " + args[0] + "; one of " + ", ".join(STAGES))


if __name__ == "__main__":
    main()
