#!/usr/bin/env bash
# Stage 1: makes the cloud TEST box at Hetzner with the hcloud CLI, then prepares it. Costs money while it exists (by the hour).
#   HCLOUD_TOKEN=... ./create.sh [--dry-run]
# --dry-run prints every hcloud/ssh command and runs none of them (works without hcloud, a token or a config file).
. "$(dirname "$0")/lib.sh"; load_conf
need hcloud; need ssh; need scp
[ "$DRY_RUN" = 1 ] || [ -n "${HCLOUD_TOKEN:-}" ] || die "Set HCLOUD_TOKEN (a read+write token of the Hetzner project) in this terminal first."
[ "$DRY_RUN" = 1 ] || [ -f "$KN_SSH_KEY.pub" ] || die "No public key $KN_SSH_KEY.pub (ssh-keygen -t ed25519 -f $KN_SSH_KEY)."

# ---- cost guard: nothing is created without --confirm-spend, and the price is shown first
confirm=0; for a in "$@"; do [ "$a" = --confirm-spend ] && confirm=1; done
price_hourly="$KN_EST_HOURLY_EUR"; price_monthly="$KN_EST_MONTHLY_EUR"; price_src="estimate from kn.conf.example (not Hetzner's answer)"
if [ "$DRY_RUN" != 1 ] && [ -n "${HCLOUD_TOKEN:-}" ]; then
  PY="$(command -v python3 || command -v python)"
  if got="$(hcloud server-type describe "$KN_TYPE" -o json 2>/dev/null | "$PY" "$HERE/price.py" "$KN_LOCATION")"; then
    read -r price_hourly price_monthly <<< "$got"; price_src="Hetzner's price for $KN_TYPE in $KN_LOCATION (gross, as the API reports it)"
  else die "Could not read the price of $KN_TYPE in $KN_LOCATION from Hetzner (is the type and location right?). Nothing was created."; fi
fi
say "COST: $KN_TYPE in $KN_LOCATION = $price_hourly per hour, capped at $price_monthly per month ($price_src)."
say "      Billed by the started hour. A powered-OFF server is billed the same. Only DELETING it stops the bill. Traffic past the included amount is extra."
say "      This box will be reminded at $KN_MAX_HOURS hours. Plan: create, test, teardown in hours."
if [ "$confirm" != 1 ]; then
  [ "$DRY_RUN" = 1 ] && say "[dry-run] a real run needs --confirm-spend" || die "Refusing to create anything without --confirm-spend (this starts billing). Re-run: ./create.sh --confirm-spend"
fi
[ "$DRY_RUN" = 1 ] || [ ! -f "$HERE/.session" ] || die "A box is already recorded in .session (started $(sed -n 2p "$HERE/.session")). Run ./cost-check.sh, then ./teardown.sh. Only one test box at a time."

settings="$HERE/server-settings.test.json"
ports=$(game_ports "$settings" | tr '\n' ' ')
say "Game UDP ports to open: $ports"

work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
pub="$(cat "$KN_SSH_KEY.pub" 2>/dev/null || echo 'ssh-ed25519 AAAA-dry-run')"
sed -e "s|@@SSH_PUBKEY@@|$pub|" -e "s|@@ADMIN_USER@@|$KN_ADMIN_USER|" "$HERE/cloud-init.yaml" > "$work/user-data.yaml"

# Cloud firewall: SSH (key only) and the game's UDP ports. Nothing else, so Postgres, the save service and the control pages
# are not reachable from outside whatever the box does.
{
  printf '[{"direction":"in","protocol":"tcp","port":"22","source_ips":['
  IFS=, read -ra cidrs <<< "$KN_ADMIN_CIDR"; first=1
  for c in "${cidrs[@]}"; do [ $first = 1 ] || printf ','; printf '"%s"' "$c"; first=0; done
  printf ']}'
  for p in $ports; do printf ',{"direction":"in","protocol":"udp","port":"%s","source_ips":["0.0.0.0/0","::/0"]}' "$p"; done
  printf ']\n'
} > "$work/rules.json"
say "Firewall rules:"; cat "$work/rules.json"

run hcloud ssh-key create --name "$KN_NAME-key" --public-key-from-file "$KN_SSH_KEY.pub" --label purpose=kn-test
run hcloud firewall create --name "$KN_FIREWALL" --rules-file "$work/rules.json" --label purpose=kn-test
run hcloud server create --name "$KN_NAME" --type "$KN_TYPE" --image "$KN_IMAGE" --location "$KN_LOCATION" \
    --ssh-key "$KN_NAME-key" --firewall "$KN_FIREWALL" --user-data-from-file "$work/user-data.yaml" --label purpose=kn-test

if [ "$DRY_RUN" = 1 ]; then
  say "[dry-run] ip=\$(hcloud server ip $KN_NAME); wait for ssh; scp box/ ; sudo bash box-setup.sh --ports \"$ports\""
  exit 0
fi
ip="$(hcloud server ip "$KN_NAME")"; say "Server address: $ip"
say "Waiting for SSH and the first boot..."
for i in $(seq 1 60); do
  if ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" 'test -f /var/lib/kn-cloud-init-done' 2>/dev/null; then break; fi
  sleep 5; [ "$i" = 60 ] && die "The box did not finish its first boot in 5 minutes."
done
scp $(ssh_opts) -r "$HERE/box" "$KN_ADMIN_USER@$ip:/tmp/kn-box"
ssh $(ssh_opts) "$KN_ADMIN_USER@$ip" "sudo bash /tmp/kn-box/box-setup.sh --ports '$ports'"
echo "$ip" > "$HERE/.last-ip"
# Record the start (name, epoch, max hours) for cost-check.sh and the reminder.
printf '%s
%s
%s
' "$KN_NAME" "$(date +%s)" "$KN_MAX_HOURS" > "$HERE/.session"
"$HERE/remind.sh" >/dev/null 2>&1 &
say "Box is ready at $ip. Next: ./deploy.sh <build number>   (it sends the server, the save service and the certificate)."
