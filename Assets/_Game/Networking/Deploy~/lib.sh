# Shared helpers for the Deploy~ scripts. Source it; do not run it.
# DRY_RUN=1 (or --dry-run on any script) prints every command that would change something and runs none of them.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DRY_RUN="${DRY_RUN:-0}"
for arg in "$@"; do [ "$arg" = "--dry-run" ] && DRY_RUN=1; done

say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

# Prints the command in dry-run mode, runs it otherwise.
run() {
  if [ "$DRY_RUN" = 1 ]; then printf '[dry-run] %s\n' "$*"; else "$@"; fi
}

load_conf() {
  local conf="${KN_DEPLOY_CONF:-$HERE/kn.conf}"
  if [ -f "$conf" ]; then . "$conf"; elif [ "$DRY_RUN" = 1 ]; then . "$HERE/kn.conf.example"; else die "No $conf: copy kn.conf.example to kn.conf and edit it."; fi
  : "${KN_NAME:?}" "${KN_LOCATION:?}" "${KN_TYPE:?}" "${KN_IMAGE:?}" "${KN_SSH_KEY:?}" "${KN_ADMIN_USER:?}"
}

need() { command -v "$1" >/dev/null 2>&1 || { [ "$DRY_RUN" = 1 ] && say "(note: $1 is not installed here; fine for a dry run)" || die "$1 is not installed."; }; }

# The game ports of a server-settings.json: "udp:<port>" for every game port and its status port (port+1).
game_ports() {
  PY="$(command -v python3 || command -v python)" || die "python is needed to read the ports"
  "$PY" "$HERE/ports.py" "$1"
}

ssh_opts() { printf '%s' "-i $KN_SSH_KEY -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10"; }
