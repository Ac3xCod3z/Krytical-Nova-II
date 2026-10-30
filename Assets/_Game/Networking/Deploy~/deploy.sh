#!/usr/bin/env bash
# Sends a numbered build to the box and switches to it; keeps the previous one for rollback.
#   ./deploy.sh <number> [--dry-run]     e.g. ./deploy.sh 1
#   ./deploy.sh rollback [--dry-run]     back to the previous number
. "$(dirname "$0")/lib.sh"; load_conf
need ssh; need rsync
ip="${KN_IP:-$(cat "$HERE/.last-ip" 2>/dev/null || true)}"; [ -n "$ip" ] || { [ "$DRY_RUN" = 1 ] && ip=203.0.113.10 || die "No address: run create.sh first, or set KN_IP."; }
ssh_t() { run ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "$@"; }
sudo_kn() { ssh_t "sudo -u kn bash -c '$1'"; }

if [ "${1:-}" = rollback ]; then
  ssh_t 'prev=$(ls -1 /opt/kn/builds | sort -n | tail -2 | head -1); cur=$(basename "$(readlink /opt/kn/current)"); [ "$prev" != "$cur" ] || { echo "no earlier build" >&2; exit 1; }; sudo systemctl stop "kn-server@*" kn-save; sudo ln -sfn /opt/kn/builds/$prev /opt/kn/current; sudo systemctl start kn-save "kn-server@main"; echo "rolled back to $prev"'
  exit 0
fi
n="${1:?build number}"; [[ "$n" =~ ^[0-9]+$ ]] || die "the build number is a whole number"
build="${KN_BUILD_DIR:?}"; [ "$DRY_RUN" = 1 ] || [ -x "$build/KryticalNovaServer.x86_64" ] || die "No $build/KryticalNovaServer.x86_64: build it (Krytical Nova > Build > Dedicated Server (Linux))."
cert="${KN_CERT_DIR:?}"; [ "$DRY_RUN" = 1 ] || { [ -f "$cert/server.crt" ] && [ -f "$cert/server.key" ] || die "No server.crt/server.key in $cert (certificates.sh server <folder>)."; }

# The save service for Linux, published from the repo's own project.
svc="${KN_SAVESERVICE_DIR:-}"
if [ -z "$svc" ]; then svc="$(mktemp -d)/saveservice"; run dotnet publish "$HERE/../../Gameplay/Persistence/Service~/SaveService" -c Release -r linux-x64 --self-contained -o "$svc"; fi

dest="/opt/kn/builds/$n"
ssh_t "sudo install -d -o kn -g kn $dest/server $dest/saveservice"
run rsync -az --delete -e "ssh $(ssh_opts)" "$build/" "$KN_ADMIN_USER@$ip:/tmp/kn-server-$n/"
run rsync -az --delete -e "ssh $(ssh_opts)" "$svc/" "$KN_ADMIN_USER@$ip:/tmp/kn-svc-$n/"
# The settings a new box starts with, with the box's own address as the one players are given (plan item 2).
PY="$(command -v python3 || command -v python)" || die "python is needed to write the settings"
settings_out="$(mktemp -d)/server-settings.test.json"
"$PY" -c 'import json, sys; s = json.load(open(sys.argv[1])); s["publicAddress"] = sys.argv[2]; json.dump(s, open(sys.argv[3], "w"), indent=4)' \
  "$HERE/server-settings.test.json" "$ip" "$settings_out"
run scp $(ssh_opts) "$settings_out" "$KN_ADMIN_USER@$ip:/tmp/server-settings.test.json"
ssh_t "sudo rsync -a --delete /tmp/kn-server-$n/ $dest/server/ && sudo rsync -a --delete /tmp/kn-svc-$n/ $dest/saveservice/ && sudo chmod +x $dest/server/KryticalNovaServer.x86_64 $dest/saveservice/SaveService && ( [ -f $dest/server/server-settings.json ] || sudo cp /tmp/server-settings.test.json $dest/server/server-settings.json ) && sudo cp /opt/kn/kn-args.sh /opt/kn/kn-stop.sh $dest/ && echo '{\"folder\":\"/var/backups/kn\",\"offsite\":\"none\"}' | sudo tee $dest/saveservice/backup-settings.json >/dev/null && sudo chown -R kn:kn $dest"
# Certificate: the owner's CA signed it; the key stays root:kn 640 on the box.
ssh_t "sudo install -d -o root -g kn -m 750 /etc/kn/certificates"
run scp $(ssh_opts) "$cert/server.crt" "$cert/server.key" "$KN_ADMIN_USER@$ip:/tmp/"
ssh_t "sudo install -o root -g kn -m 640 /tmp/server.crt /etc/kn/certificates/server.crt && sudo install -o root -g kn -m 640 /tmp/server.key /etc/kn/certificates/server.key && rm -f /tmp/server.key /tmp/server.crt"
# Switch: stop (SIGTERM saves everyone), move the link, start.
ssh_t "sudo systemctl stop 'kn-server@*' kn-save 2>/dev/null; sudo ln -sfn $dest /opt/kn/current && sudo systemctl enable --now kn-save && sudo systemctl enable --now kn-server@main"
say "Build $n is live on $ip (test database only). Check: ./smoke-test.sh"
