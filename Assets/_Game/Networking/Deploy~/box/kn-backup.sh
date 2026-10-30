#!/usr/bin/env bash
# Nightly copy of the box's TEST database (Linux backups follow-up; the save service makes no nightly copy under --test).
# Run as root by kn-backup.timer (04:00) or by hand: sudo /opt/kn/kn-backup.sh
#  1. pg_dump -Fc into /var/backups/kn/krytical_nova_test_<time>.dump, checked with pg_restore --list; a bad copy is deleted.
#  2. Only the newest KN_BACKUPS_KEPT (14) are kept.
#  3. Off the box: when /etc/kn/backup-remote names an rclone place (and /etc/kn/rclone.conf holds its login, made by the
#     owner), the copy goes there too. Otherwise pull-backups.sh on the PC fetches them; teardown.sh always copies first.
# /var/backups/kn/last-backup.txt says when the last copy was made and whether it left the box.
set -euo pipefail
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
dir=/var/backups/kn; keep="${KN_BACKUPS_KEPT:-14}"; db=krytical_nova_test
stamp="$(date -u +%Y-%m-%d_%H-%M)"; out="$dir/${db}_$stamp.dump"
install -d -o kn -g kn -m 750 "$dir"
umask 027
runuser -u postgres -- pg_dump -Fc --no-owner "$db" > "$out.part"
pg_restore --list "$out.part" >/dev/null || { rm -f "$out.part"; echo "the copy does not read back: removed" >&2; exit 1; }
mv "$out.part" "$out"; chown kn:kn "$out"
echo "copy made: $out ($(stat -c %s "$out") bytes)"
mapfile -t old < <(ls -1 "$dir"/${db}_*.dump 2>/dev/null | sort | head -n -"$keep")
for f in "${old[@]}"; do rm -f -- "$f"; echo "dropped $f"; done
offsite="no (pull-backups.sh on the PC fetches the copies)"
if [ -s /etc/kn/backup-remote ]; then
  remote="$(head -1 /etc/kn/backup-remote)"
  if command -v rclone >/dev/null && [ -f /etc/kn/rclone.conf ] && rclone --config /etc/kn/rclone.conf copy "$out" "$remote"; then
    offsite="yes ($remote)"
  else
    offsite="FAILED ($remote: rclone or /etc/kn/rclone.conf missing, or the copy failed)"
  fi
fi
printf 'last copy: %s\nfile: %s\nleft the box: %s\n' "$stamp UTC" "$(basename "$out")" "$offsite" > "$dir/last-backup.txt"
chown kn:kn "$dir/last-backup.txt"
echo "left the box: $offsite"
case "$offsite" in FAILED*) exit 1;; esac
