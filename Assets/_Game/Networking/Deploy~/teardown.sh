#!/usr/bin/env bash
# Ends the test box: FIRST copies the test database to this PC (and checks the copy reads back), THEN deletes the server,
# firewall and key at Hetzner so the bill stops. Nothing is deleted if the copy fails.
#   HCLOUD_TOKEN=... ./teardown.sh [--dry-run] [--no-dump]     (--no-dump only when the box never held anything you need)
. "$(dirname "$0")/lib.sh"; load_conf
need hcloud; need ssh; need scp
nodump=0; for a in "$@"; do [ "$a" = --no-dump ] && nodump=1; done
[ "$DRY_RUN" = 1 ] || [ -n "${HCLOUD_TOKEN:-}" ] || die "Set HCLOUD_TOKEN first."
ip="${KN_IP:-$(cat "$HERE/.last-ip" 2>/dev/null || true)}"; [ -n "$ip" ] || { [ "$DRY_RUN" = 1 ] && ip=203.0.113.10 || die "No address (.last-ip); set KN_IP."; }
stamp="$(date +%Y-%m-%d_%H-%M)"; out="$KN_DUMP_DIR/krytical_nova_test_$stamp.dump"

if [ "$nodump" = 0 ]; then
  say "1/3 Copying the test database off the box to $out"
  run mkdir -p "$KN_DUMP_DIR"
  run ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "sudo -u postgres pg_dump -Fc --no-owner krytical_nova_test > /tmp/kn_test.dump && ls -l /tmp/kn_test.dump"
  run scp $(ssh_opts) "$KN_ADMIN_USER@$ip:/tmp/kn_test.dump" "$out"
  if [ "$DRY_RUN" != 1 ]; then
    [ -s "$out" ] || die "The copy is empty: nothing was deleted."
    if command -v pg_restore >/dev/null 2>&1; then pg_restore --list "$out" >/dev/null || die "The copy does not read back: nothing was deleted."; else say "(pg_restore not on this PC: only the size was checked)"; fi
    say "Copy kept: $out ($(wc -c < "$out") bytes)"
  fi
else
  say "1/3 Skipping the database copy (--no-dump)."
fi
say "2/3 This deletes the server $KN_NAME, firewall $KN_FIREWALL and key $KN_NAME-key at Hetzner."
if [ "$DRY_RUN" != 1 ]; then read -r -p "Type DESTROY to go on: " ans; [ "$ans" = DESTROY ] || die "Stopped. Nothing was deleted."; fi
say "3/3 Deleting"
run hcloud server delete "$KN_NAME"
run hcloud firewall delete "$KN_FIREWALL"
run hcloud ssh-key delete "$KN_NAME-key"
run rm -f "$HERE/.last-ip" "$HERE/.session"
say "Deleted. Now checking nothing billable is left:"
[ "$DRY_RUN" = 1 ] || "$HERE/cost-check.sh" --expect-nothing
