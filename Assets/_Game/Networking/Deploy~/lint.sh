#!/usr/bin/env bash
# Checks the scripts without touching any cloud: syntax, shellcheck (if installed), YAML/JSON validity, and a dry run of each script.
cd "$(dirname "$0")"; fail=0
for f in *.sh box/*.sh; do bash -n "$f" && echo "syntax ok   $f" || fail=1; done
if command -v shellcheck >/dev/null; then shellcheck -x -S warning *.sh box/*.sh && echo "shellcheck ok" || fail=1; else echo "(shellcheck not installed: skipped)"; fi
PY=$(command -v python3 || command -v python)
"$PY" -c 'import json; json.load(open("server-settings.test.json")); print("json ok      server-settings.test.json")' || fail=1
"$PY" -c '
try:
    import yaml
except ImportError:
    print("(pyyaml not installed: yaml skipped)"); raise SystemExit
yaml.safe_load(open("cloud-init.yaml").read().replace("@@SSH_PUBKEY@@","k").replace("@@ADMIN_USER@@","u")); print("yaml ok      cloud-init.yaml")' || fail=1
for s in create deploy teardown smoke-test cost-check pull-backups; do echo "--- dry run: $s"; KN_DEPLOY_CONF=/nonexistent ./$s.sh ${s/deploy/1} --dry-run >/dev/null && echo "dry run ok  $s" || { echo "dry run FAILED $s"; fail=1; }; done
exit $fail
