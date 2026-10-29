#!/usr/bin/env bash
# Postgres 18 (PGDG repository; Ubuntu 24.04 itself ships 16) with the TEST database only. Idempotent. Run as root.
# The real database name krytical_nova is never created here, so a save service started without --test cannot find it.
set -euo pipefail
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 1; }
if ! dpkg -s postgresql-18 >/dev/null 2>&1; then
  install -d /usr/share/postgresql-common/pgdg
  tmp=$(mktemp); curl -fsSL -o "$tmp" https://www.postgresql.org/media/keys/ACCC4CF8.asc
  install -m 644 "$tmp" /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc; rm -f "$tmp"  # apt reads it as the _apt user
  . /etc/os-release
  echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq postgresql-18
fi
conf=$(ls -d /etc/postgresql/18/*/ | head -1)
cat > "${conf}conf.d/90-kn.conf" <<CONF
listen_addresses = 'localhost'
password_encryption = scram-sha-256
CONF
if ! grep -q 'kn-test' "${conf}pg_hba.conf"; then
  sed -i '0,/^local/s//# kn-test\nlocal   krytical_nova_test   krytical_save   scram-sha-256\nlocal/' "${conf}pg_hba.conf"
fi
systemctl restart postgresql
password_file=/etc/kn/save-service.json
if [ ! -f "$password_file" ]; then
  pw=$(openssl rand -hex 24)
  sudo -u postgres psql -qc "CREATE ROLE krytical_save WITH LOGIN PASSWORD '$pw'" 2>/dev/null || sudo -u postgres psql -qc "ALTER ROLE krytical_save WITH PASSWORD '$pw'"
  # "database" is krytical_nova: the service adds _test itself under --test.
  umask 077
  printf '{"host":"/var/run/postgresql","port":5432,"database":"krytical_nova","user":"krytical_save","password":"%s"}\n' "$pw" > "$password_file"
  chown root:kn "$password_file"; chmod 640 "$password_file"
fi
sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='krytical_nova_test'" | grep -q 1 \
  || sudo -u postgres psql -qc "CREATE DATABASE krytical_nova_test OWNER krytical_save ENCODING 'UTF8'"
echo "postgres ok: krytical_nova_test only"
