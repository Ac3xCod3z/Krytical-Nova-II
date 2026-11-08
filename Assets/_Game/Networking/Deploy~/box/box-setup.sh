#!/usr/bin/env bash
# Prepares a fresh Ubuntu 24.04 machine (a cloud box, or WSL2 with systemd) to run the Krytical Nova TEST setup. Idempotent.
# Run as root from the folder it lives in:  sudo bash box-setup.sh [--ports "7777 7778 7779 7780"]
set -euo pipefail
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
here="$(cd "$(dirname "$0")" && pwd)"
ports=""
while [ $# -gt 0 ]; do case "$1" in --ports) ports="$2"; shift 2;; *) shift;; esac; done

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq jq curl ca-certificates gnupg rsync ufw fail2ban unattended-upgrades openssl netcat-openbsd

id kn >/dev/null 2>&1 || useradd --system --create-home --home-dir /var/lib/kn --shell /usr/sbin/nologin kn
install -d -o root -g kn -m 750 /etc/kn
install -d -o kn -g kn -m 750 /opt/kn /opt/kn/builds /var/lib/kn /var/backups/kn /var/log/kn /var/log/kn/performance

# The save service key: made once, on the box, never leaves it. The game server and the service read the same file.
[ -f /etc/kn/save-service-key.txt ] || { umask 077; openssl rand -hex 32 > /etc/kn/save-service-key.txt; chown root:kn /etc/kn/save-service-key.txt; chmod 640 /etc/kn/save-service-key.txt; }

bash "$here/pg-setup.sh"

install -m 755 "$here/kn-args.sh" /opt/kn/kn-args.sh
install -m 755 "$here/kn-stop.sh" /opt/kn/kn-stop.sh
install -m 755 "$here/kn-backup.sh" /opt/kn/kn-backup.sh
install -m 644 "$here/kn-save.service" "$here/kn-server@.service" "$here/kn-backup.service" "$here/kn-backup.timer" /etc/systemd/system/
# The servers' and the save service's logs go to the journal (-logFile -); it keeps at most 2 GB and 30 days (plan item 6).
install -d /etc/systemd/journald.conf.d
printf '[Journal]
Storage=persistent
SystemMaxUse=2G
MaxRetentionSec=30day
' > /etc/systemd/journald.conf.d/90-kn.conf
systemctl restart systemd-journald
systemctl daemon-reload
# The test database is copied every night at 04:00 (kn-backup.sh; the save service makes none under --test).
systemctl enable --now kn-backup.timer >/dev/null

# Firewall behind the cloud firewall: deny in, allow SSH and the game's UDP ports only. Nothing else, ever.
ufw --force reset >/dev/null
ufw default deny incoming >/dev/null; ufw default allow outgoing >/dev/null
ufw allow 22/tcp >/dev/null
for p in $ports; do ufw allow "$p/udp" >/dev/null; done
ufw --force enable >/dev/null
systemctl enable --now fail2ban >/dev/null 2>&1 || true
echo "box ready. game ports open: ${ports:-none yet}"
