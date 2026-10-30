#!/usr/bin/env bash
# The real-server shutdown-save test (run on the box or in WSL2, with the REAL Linux server build, not a stub):
# starts a game server unit, stops it the way systemd will, and checks the save happened and the exit was clean and in time.
#   sudo ./shutdown-save-test.sh [instance]          (default: main)
# Passes only when ALL hold: the unit stops within 90 s, exit status 0, and the journal has the "saved" line.
# KN_SAVED_PATTERN is the log line that proves "everyone saved" (default matches "[Server] ... saved"); set it to the real line.
set -uo pipefail
inst="${1:-main}"; unit="kn-server@$inst"; pattern="${KN_SAVED_PATTERN:-\[Server\].*[Ss]aved}"
[ "$(id -u)" = 0 ] || { echo "run as root"; exit 2; }
systemctl start "$unit"; sleep 20      # let it open the world and the save connection
since=$(date '+%Y-%m-%d %H:%M:%S')
t0=$(date +%s); systemctl stop "$unit"; secs=$(( $(date +%s) - t0 ))
status=$(systemctl show "$unit" -p ExecMainStatus --value); result=$(systemctl show "$unit" -p Result --value)
journalctl -u "$unit" --since "$since" --no-pager -o cat | tail -12
fail=0
[ "$secs" -le 90 ] && echo "ok    stopped in $secs s" || { echo "FAIL  took $secs s"; fail=1; }
{ [ "$status" = 0 ] && [ "$result" = success ]; } && echo "ok    exit 0" || { echo "FAIL  exit $status ($result)"; fail=1; }
journalctl -u "$unit" --since "$since" --no-pager -o cat | grep -Eq "$pattern" && echo "ok    saved line found" || { echo "FAIL  no line matching $pattern"; fail=1; }
journalctl -u "$unit" --since "$since" --no-pager -o cat | grep -Eq 'kn-stop] POST /stop on [0-9]+ -> 20[02]' && echo "info  explicit stop path used" || echo "info  explicit stop path NOT used (SIGTERM fallback)"
[ $fail = 0 ] && echo "SHUTDOWN-SAVE TEST PASSED" || { echo "SHUTDOWN-SAVE TEST FAILED"; exit 1; }
