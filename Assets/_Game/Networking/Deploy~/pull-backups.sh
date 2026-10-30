#!/usr/bin/env bash
# Copies the box's nightly database copies (box/kn-backup.sh, /var/backups/kn) to this PC, into KN_DUMP_DIR, and checks
# each reads back. Copies already here are skipped. This is the off-box copy when the box has no rclone place.
#   ./pull-backups.sh [--dry-run]
. "$(dirname "$0")/lib.sh"; load_conf
need ssh; need scp
ip="${KN_IP:-$(cat "$HERE/.last-ip" 2>/dev/null || true)}"; [ -n "$ip" ] || { [ "$DRY_RUN" = 1 ] && ip=203.0.113.10 || die "No address (.last-ip); set KN_IP."; }
run mkdir -p "$KN_DUMP_DIR"
if [ "$DRY_RUN" = 1 ]; then
  files="krytical_nova_test_2026-01-01_04-00.dump"
else
  files="$(ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "sudo ls -1 /var/backups/kn" | grep '^krytical_nova_test_.*\.dump$' || true)"
fi
[ -n "$files" ] || { say "The box has no copies yet (the first is made at 04:00, or: ssh in and run sudo /opt/kn/kn-backup.sh)."; exit 0; }
got=0
for f in $files; do
  [ -f "$KN_DUMP_DIR/$f" ] && continue
  run ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "sudo install -m 644 /var/backups/kn/$f /tmp/$f"
  run scp $(ssh_opts) "$KN_ADMIN_USER@$ip:/tmp/$f" "$KN_DUMP_DIR/$f"
  run ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "rm -f /tmp/$f"
  if [ "$DRY_RUN" != 1 ]; then
    [ -s "$KN_DUMP_DIR/$f" ] || die "$f came over empty."
    if command -v pg_restore >/dev/null 2>&1; then pg_restore --list "$KN_DUMP_DIR/$f" >/dev/null || die "$f does not read back."; fi
  fi
  got=$((got + 1))
done
say "$got new copies in $KN_DUMP_DIR."
[ "$DRY_RUN" = 1 ] || ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "sudo cat /var/backups/kn/last-backup.txt" || true
