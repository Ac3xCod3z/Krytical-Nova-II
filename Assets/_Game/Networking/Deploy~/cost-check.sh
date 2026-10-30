#!/usr/bin/env bash
# Lists EVERYTHING in the Hetzner project that can bill, so nothing is left running. Read-only.
#   HCLOUD_TOKEN=... ./cost-check.sh [--expect-nothing]
# --expect-nothing: exits 1 when anything billable exists (teardown.sh uses it). Free things (SSH keys, firewalls, networks) are listed too.
. "$(dirname "$0")/lib.sh"; load_conf 2>/dev/null || true
need hcloud
expect=0; for a in "$@"; do [ "$a" = --expect-nothing ] && expect=1; done
if [ "$DRY_RUN" = 1 ]; then
  say "[dry-run] would list: servers, volumes, snapshots+backups, primary IPs, floating IPs, load balancers; then the session age"; exit 0
fi
[ -n "${HCLOUD_TOKEN:-}" ] || die "Set HCLOUD_TOKEN first."
billable=0
section() { # name, command...
  local name="$1"; shift; local out; out="$("$@" 2>&1)" || { say "$name: could not list ($out)"; billable=1; return; }
  local rows; rows=$(printf '%s\n' "$out" | tail -n +2 | grep -c . || true)
  if [ "$rows" -gt 0 ]; then say "== $name ($rows) BILLS"; printf '%s\n' "$out"; billable=$((billable+rows)); else say "== $name: none"; fi
}
section "Servers (billed even when powered off)" hcloud server list
section "Volumes (billed per GB per month, even unattached)" hcloud volume list
section "Snapshots (billed per GB per month)" hcloud image list --type snapshot
section "Server backups (billed 20% of the server price)" hcloud image list --type backup
section "Primary IPs (IPv4 billed even when unassigned)" hcloud primary-ip list
section "Floating IPs" hcloud floating-ip list
section "Load balancers" hcloud load-balancer list
say "(free, not counted: SSH keys, firewalls, networks, labels)"
hcloud ssh-key list 2>/dev/null | tail -n +2 | grep -c . | sed 's/^/ ssh keys left: /'
hcloud firewall list 2>/dev/null | tail -n +2 | grep -c . | sed 's/^/ firewalls left: /'
if [ -f "$HERE/.session" ]; then
  name=$(sed -n 1p "$HERE/.session"); start=$(sed -n 2p "$HERE/.session"); max=$(sed -n 3p "$HERE/.session")
  hours=$(( ($(date +%s) - start) / 3600 ))
  say "Session: $name running about $hours h (reminder at ${max:-8} h)."
  [ "$hours" -ge "${max:-8}" ] && say "WARNING: past $max hours. Tear it down (./teardown.sh) unless you are still testing."
fi
if [ "$billable" -gt 0 ]; then say "NOT CLEAN: $billable billable item(s) above."; [ "$expect" = 1 ] && exit 1; else say "CLEAN: nothing billable in this project."; fi
