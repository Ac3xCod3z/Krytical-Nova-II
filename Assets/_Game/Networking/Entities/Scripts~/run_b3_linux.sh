#!/usr/bin/env bash
# B3, the 2-client gate of the entity netcode by the Linux route (DOTS plan section 43). Runs INSIDE WSL Ubuntu.
# Test lane only: game 7797, page 7798, the TEST save service (port 7802, database krytical_nova_test). Never 7777/7778/7801/8090.
#
#   KN_SAVE_SERVICE=http://<windows-host-ip>:7802 KN_SAVE_KEY_FILE=/path/to/test-key.txt ./run_b3_linux.sh
#
# Needs the two builds of KnLinuxTestBuild in the clone (Builds/KnGameServerLinux, Builds/KnGameClientLinux). Copies them into
# ~/kn-b3 (Linux file system: exec bits, speed), starts the server, then the bots:
#   bot 1  walks, and is killed with kill -9 mid-walk          (its save must be written before its ghost goes)
#   bot 2  joins late and walks to the end                     (late joiner sees bot 1 and client A)
#   bot 3  uses bot 1's number while bot 1 is in               (a second login of one account: must be refused, exit 3)
# Client A (the clone's Editor in Play mode, by hand) joins <WSL IP>:7797 during the WAIT step the script prints.
# Then POST /stop (loopback, X-KN-Key = the test key; never printed), and a report: results, [KnClick] pairs, the server's
# leave/save lines. The key file is read only by curl's header and by the server; nothing here echoes it.
set -u
CLONE="${KN_CLONE:-/mnt/c/Users/dlaco/Krytical Nova II DOTS}"
GAME_PORT=7797; PAGE_PORT=7798
SAVE_SERVICE="${KN_SAVE_SERVICE:-}"; KEY_FILE="${KN_SAVE_KEY_FILE:-}"
WAIT_FOR_A="${KN_WAIT_FOR_A:-60}"          # seconds to let client A join before the bots start
WALK="${KN_WALK:-0,0;6,4;12,0;6,-4}"       # ground points (x,z metres) near the map's spawn; set for the map in use
ROOT="$HOME/kn-b3"; RUN="$ROOT/runs/$(date +%Y%m%d-%H%M%S)"
REPORT="$RUN/b3-report.md"

die() { echo "STOP: $*"; exit 2; }
for p in 7777 7778 7801 8090; do [ "$p" = "$GAME_PORT" ] || [ "$p" = "$PAGE_PORT" ] && die "real-lane port $p"; done
case "$SAVE_SERVICE" in *:7802*) ;; *) die "KN_SAVE_SERVICE must be the TEST save service on port 7802 (got '${SAVE_SERVICE}')";; esac
[ -r "$KEY_FILE" ] || die "KN_SAVE_KEY_FILE is not readable"
ss -lun | grep -q ":$GAME_PORT " && die "UDP $GAME_PORT is in use"
ss -ltn | grep -q ":$PAGE_PORT " && die "TCP $PAGE_PORT is in use"

mkdir -p "$RUN/server" "$RUN/bots"
for side in Server Client; do
  src="$CLONE/Builds/KnGame${side}Linux"; dst="$ROOT/${side,,}"
  [ -d "$src" ] || die "no build at $src (run KnLinuxTestBuild.Build$side first)"
  rm -rf "$dst"; cp -r "$src" "$dst"; chmod +x "$dst"/*.x86_64
done
SERVER_EXE="$ROOT/server/KnGameServer.x86_64"; CLIENT_EXE="$ROOT/client/KnGameClient.x86_64"
# the server's settings beside its exe: test save service, its key file, bots on, plain UDP (no certificate on the test lane)
cat > "$ROOT/server/server-settings.json" <<JSON
{ "port": $GAME_PORT, "pagePort": $PAGE_PORT, "saveService": "$SAVE_SERVICE", "saveServiceKeyFile": "$KEY_FILE",
  "devBots": true, "plainTransport": true, "serverName": "KN B3 test lane" }
JSON

echo "server starting (log $RUN/server/player.log)"
"$SERVER_EXE" -batchmode -nographics -port $GAME_PORT -pageport $PAGE_PORT -devbots -plaintransport \
  -knout "$RUN/server" -logFile "$RUN/server/player.log" &
SERVER_PID=$!
for i in $(seq 1 60); do curl -fs "http://127.0.0.1:$PAGE_PORT/status" >/dev/null && break; sleep 1; done
curl -fs "http://127.0.0.1:$PAGE_PORT/status" >/dev/null || { kill $SERVER_PID 2>/dev/null; die "the server page never answered"; }
WSL_IP=$(hostname -I | awk '{print $1}')
echo "WAIT: client A (the Editor, Play) joins $WSL_IP:$GAME_PORT now; bots start in $WAIT_FOR_A s"
sleep "$WAIT_FOR_A"

bot() { # number, seconds, extra label
  "$CLIENT_EXE" -batchmode -nographics -kntestclient "$1" -connect 127.0.0.1 -port $GAME_PORT -seconds "$2" \
    -walk "$WALK" -out "$RUN/bots" -logFile "$RUN/bots/player-$3.log" &
  echo $!
}
B1=$(bot 1 120 b1); sleep 15
B3=$(bot 1 30 b3dup)                      # same account as bot 1, while bot 1 is in
B2=$(bot 2 60 b2late); sleep 20
echo "kill -9 bot 1 (pid $B1) mid-walk"; kill -9 "$B1"
wait "$B3"; B3_EXIT=$?
wait "$B2"; B2_EXIT=$?
sleep 5                                   # the server sees bot 1's connection close (transport timeout)

echo "POST /stop"
STOP_CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "X-KN-Key: $(cat "$KEY_FILE")" "http://127.0.0.1:$PAGE_PORT/stop")
for i in $(seq 1 30); do kill -0 $SERVER_PID 2>/dev/null || break; sleep 1; done
kill -0 $SERVER_PID 2>/dev/null && { kill -9 $SERVER_PID; SERVER_EXIT="killed (did not stop in 30 s)"; } || { wait $SERVER_PID; SERVER_EXIT=$?; }

{
  echo "# B3 Linux run $(basename "$RUN")"
  echo "- server exit: $SERVER_EXIT (POST /stop -> $STOP_CODE); bot 2 (late) exit $B2_EXIT (want 0); bot 3 (second login) exit $B3_EXIT (want 3)"
  for f in "$RUN"/bots/*.result; do [ -f "$f" ] && echo "- $(basename "$f"): $(cat "$f")"; done
  echo; echo "## Click starts (server tick minus owner tick, per owner NetworkId and seq; bots only, client A's are in the Editor log)"
  SERVER_CLICKS=$(cat "$RUN"/server/kn-trace-*.txt 2>/dev/null | grep -o 'server owner=[0-9]* seq=[0-9]* tick=[0-9]*')
  for log in "$RUN"/bots/kntestclient-*.log; do
    [ -f "$log" ] || continue
    echo "- $(basename "$log"):"
    grep -o 'client owner=[0-9]* seq=[0-9]* tick=[0-9]*' "$log" | while read -r _ o s t; do
      ct=${t#tick=}
      st=$(echo "$SERVER_CLICKS" | grep "$o $s " | tail -1 | sed 's/.*tick=//')
      echo "  $o $s: owner tick $ct, server ${st:-none}, diff $([ -n "$st" ] && echo $((st - ct)) || echo '?')"
    done
  done
  echo; echo "## Server lines about leaving and saving (read the order for bot 1: save before its ghost goes)"
  grep -hiE 'leav|left|save|ghost|refus|turned away|exception' "$RUN"/server/*.log "$RUN"/server/kn-trace-*.txt | tail -60 | sed 's/^/    /'
  echo; echo "## Exceptions anywhere"; cat "$RUN"/server/*.log "$RUN"/bots/*.log 2>/dev/null | grep -c 'Exception'
} > "$REPORT"
echo "report: $REPORT"; cat "$REPORT"
