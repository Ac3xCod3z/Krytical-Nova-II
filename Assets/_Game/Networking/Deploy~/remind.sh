#!/usr/bin/env bash
# Started in the background by create.sh: after KN_MAX_HOURS (default 8) pops a Windows message once, and again every hour,
# until .session is gone (teardown.sh removes it). It only REMINDS; it never deletes anything.
. "$(dirname "$0")/lib.sh"
[ -f "$HERE/.session" ] || exit 0
max=$(sed -n 3p "$HERE/.session"); start=$(sed -n 2p "$HERE/.session")
sleep $(( start + max*3600 - $(date +%s) > 0 ? start + max*3600 - $(date +%s) : 0 ))
while [ -f "$HERE/.session" ]; do
  msg="The Krytical Nova TEST box has been running over $max hours and is billing. Run teardown.sh (or cost-check.sh)."
  powershell.exe -NoProfile -Command "Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('$msg','Hetzner box still running')" >/dev/null 2>&1 || say "$msg"
  sleep 3600
done
