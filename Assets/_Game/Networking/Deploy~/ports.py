"""Prints every game UDP port of a server-settings.json (each game port and its status port, port+1), one per line."""
import json
import sys

settings = json.load(open(sys.argv[1]))
ports = {settings["port"], settings["port"] + 1}
for entry in settings.get("mapServers", []):
    ports.update({entry["port"], entry["port"] + 1})
print("\n".join(str(p) for p in sorted(ports)))
