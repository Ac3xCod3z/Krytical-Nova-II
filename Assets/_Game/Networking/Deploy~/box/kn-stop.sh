#!/usr/bin/env bash
# ExecStop of kn-server@.service. A Unity Linux player does NOT run Application.wantsToQuit on SIGTERM (found in the DOTS
# Linux spike, 29 Oct 2026), so nothing may rely on SIGTERM to save. The explicit path is the game's own stop call:
#   POST http://127.0.0.1:<statusPort>/stop   with the header X-KN-Key: <the save service key>
# (needs the server to offer it: plan item "explicit stop path"; bound to 127.0.0.1, key-protected). It saves everyone and
# then quits with exit 0. If the call is not there (an older build) or refused, this script exits 0 and systemd falls back to
# SIGTERM, which a build with a signal handler may also turn into the same save. Never fails the stop.
set -uo pipefail
inst="${1:?instance}"
base=/opt/kn/current/server/server-settings.json
# The status port: the listed server's own "statusPort" when it sets one, otherwise the game port + 1 (as the game does).
if [ "$inst" = main ]; then status=$(jq -r 'if (.statusPort // 0) > 0 then .statusPort else (.port // 7777) + 1 end' "$base")
else status=$(jq -r ".mapServers[$inst].port + 1" "$base"); fi
key=$(cat /etc/kn/save-service-key.txt 2>/dev/null || true)
code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' -X POST -H "X-KN-Key: $key" "http://127.0.0.1:$status/stop") || code=000
echo "[kn-stop] POST /stop on $status -> $code"
if [ "$code" != 200 ] && [ "$code" != 202 ]; then echo "[kn-stop] no explicit stop path; falling back to SIGTERM"; exit 0; fi
# Wait for the game to save and exit by itself (the unit's TimeoutStopSec bounds this).
# systemd gives ExecStop the game's process id in MAINPID (the unit is "deactivating" now, so is-active cannot be used).
for _ in $(seq 1 85); do
  { [ -n "${MAINPID:-}" ] && kill -0 "$MAINPID" 2>/dev/null; } || { echo "[kn-stop] game exited"; exit 0; }
  sleep 1
done
echo "[kn-stop] game still running after 85 s; systemd will now send SIGTERM"
exit 0
