#!/usr/bin/env bash
# Checks the box from outside and inside. Read-only. Needs: ssh, nc (netcat), and nmap if you want the full port scan.
. "$(dirname "$0")/lib.sh"; load_conf
ip="${KN_IP:-$(cat "$HERE/.last-ip" 2>/dev/null || true)}"; [ -n "$ip" ] || { [ "$DRY_RUN" = 1 ] && ip=203.0.113.10 || die "No address; set KN_IP."; }
fail=0; ok() { say "  ok    $*"; }; bad() { say "  FAIL  $*"; fail=1; }
inside() { ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "$@"; }
# The ports come from the same table create.sh opened (server-settings.json, plan item 1): game UDP ports stay open,
# every control page stays closed.
settings="$HERE/server-settings.test.json"
udp=$(game_ports "$settings" | paste -sd, -)
pages=$(game_ports "$settings" --pages | tr '\n' ' ')
private="5432|7802|$(printf '%s' "$pages" | xargs | tr ' ' '|')"
if [ "$DRY_RUN" = 1 ]; then say "[dry-run] would check units, local ports ($private private), and the outside scan of UDP $udp on $ip"; exit 0; fi
say "Inside the box:"
inside 'systemctl is-active kn-save' | grep -q '^active' && ok "save service running" || bad "save service not running"
inside 'systemctl is-active kn-server@main' | grep -q '^active' && ok "game server running" || bad "game server not running"
inside "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7802/health" | grep -q 401 && ok "save service answers (401 without the key = good)" || bad "save service does not answer on 7802"
inside "ss -ltn | awk '{print \$4}' | grep -E ':($private)\$' | grep -v '^127\.\|^\[::1\]'" | grep -q . && bad "a private port listens on a public address" || ok "${private//|/, } are not on a public address"
say "From here (outside):"
for p in 5432 7801 7802 $pages; do
  nc -z -w 3 "$ip" "$p" 2>/dev/null && bad "TCP $p is reachable from outside" || ok "TCP $p closed"
done
nc -z -w 3 "$ip" 22 2>/dev/null && ok "SSH reachable (key only)" || bad "SSH not reachable"
command -v nmap >/dev/null && nmap -Pn -sU -p "$udp" "$ip" | sed 's/^/    /' || say "  (install nmap for a UDP scan of the game ports)"
[ $fail = 0 ] && say "SMOKE TEST PASSED" || { say "SMOKE TEST FAILED"; exit 1; }
