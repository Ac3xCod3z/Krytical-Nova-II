#!/usr/bin/env bash
# Starts one game server from the shared server-settings.json: "main" is the listed server, a number is that entry of
# mapServers. The ports come from the one table in server-settings.json; nothing is repeated here.
set -euo pipefail
cd /opt/kn/current/server
settings=/opt/kn/current/server/server-settings.json
inst="${1:?instance}"
common=(-batchmode -nographics -logFile -)
if [ "$inst" = main ]; then
  exec ./KryticalNovaServer.x86_64 "${common[@]}"
fi
map=$(jq -r ".mapServers[$inst].map" "$settings"); port=$(jq -r ".mapServers[$inst].port" "$settings"); page=$(jq -r ".mapServers[$inst].pagePort" "$settings")
[ "$map" != null ] || { echo "No mapServers entry $inst" >&2; exit 1; }
exec ./KryticalNovaServer.x86_64 "${common[@]}" -role map -map "$map" -port "$port" -pageport "$page"
