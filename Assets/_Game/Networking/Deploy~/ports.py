"""The ports of a server-settings.json, from its one table: the listed server and its mapServers (plan item 1, no ports.json).

    python ports.py server-settings.json           every game UDP port and its status port, one per line (open these)
    python ports.py server-settings.json --pages   every control page's TCP port (never open these)

The status port is the game port + 1, or the listed server's own "statusPort" when it sets one (ServerLaunchOptions);
map servers always use port + 1. The status port also carries the stop call (TCP, 127.0.0.1 only). Stops with a message,
and prints nothing, when two servers would share a port.
"""
import json
import sys

settings = json.load(open(sys.argv[1], encoding="utf-8-sig"))
servers = [("the listed server", settings.get("port", 7777), settings.get("statusPort", 0) or settings.get("port", 7777) + 1,
            settings.get("pagePort", 8090))]
for i, entry in enumerate(settings.get("mapServers", [])):
    servers.append((f"mapServers[{i}] ({entry.get('map', '?')})", entry["port"], entry["port"] + 1, entry.get("pagePort", 0)))

owner, problems = {}, []
for name, game, status, page in servers:
    for kind, port in (("game", game), ("status", status), ("control page", page)):
        if not 0 < port < 65536:
            problems.append(f"{name}: {kind} port {port} is not a port")
        elif port in owner:
            problems.append(f"{name}: {kind} port {port} is also {owner[port]}")
        else:
            owner[port] = f"the {kind} port of {name}"
if problems:
    sys.exit("server-settings.json has clashing ports:\n  " + "\n  ".join(problems))

if "--pages" in sys.argv:
    print("\n".join(str(page) for page in sorted(page for _, _, _, page in servers)))
else:
    print("\n".join(str(p) for p in sorted({p for _, game, status, _ in servers for p in (game, status)})))
