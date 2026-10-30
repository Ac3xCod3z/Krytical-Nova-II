# Deploy~: the Linux cloud TEST box (Stage 1)

Plan: `_Docs/Plans/online-linux-cloud-servers-plan.md`. These scripts make one rented Linux machine at Hetzner, put the
**test** database and the Linux server on it, and delete it again. They never touch the real saves, the real server, or
ports 7777/7778/7801 of the owner's PC. Nothing here has been run against Hetzner yet; `./lint.sh` and `--dry-run` only
check and print.

The folder ends in `~` so Unity ignores it. Run everything in **Git Bash** (or WSL) from this folder.

| Script | What it does | Costs money? |
| --- | --- | --- |
| `PREP-CHECKLIST.md` | **Read first.** What bills at Hetzner and the free account set-up (no server exists, nothing bills) | no |
| `cost-check.sh` | Lists everything billable in the project; `--expect-nothing` fails if anything is left | no |
| `lint.sh` | Syntax, JSON/YAML and a dry run of every script | no |
| `create.sh` | **Needs `--confirm-spend`**; shows the price first; one box at a time; starts the 8-hour reminder. Key, firewall and server at Hetzner (`hcloud`), then `box/box-setup.sh` on it | **yes, by the hour while the box exists** |
| `deploy.sh <n>` | Sends build number n (server, save service, certificate), switches to it, keeps the old one | no extra |
| `deploy.sh rollback` | Back to the previous build number | no |
| `smoke-test.sh` | Units up, private ports private, outside port check | no |
| `teardown.sh` | Copies the test database to this PC, checks it, then deletes server, firewall and key | stops the bill |
| `shutdown-save-test.sh` | The real-server stop test (run on the box/WSL2 with the real build): stops in time, exit 0, saved line | no |
| `box/` | What runs on the box: `box-setup.sh`, `pg-setup.sh` (Postgres 18), `kn-save.service`, `kn-server@.service`, `kn-args.sh` | |

Every script takes `--dry-run`: it prints each command that would change anything and runs none.

## What the owner does by hand, once

1. **Hetzner console** (account already made): set a **budget/billing alert** if the console offers one, and check that the
   server type in `kn.conf` (`cpx31`, 4 vCPU / 8 GB) is in stock in **Ashburn** (the public pricing page showed "unavailable"
   on every Create button on 29 Oct 2026). If not, pick another type or location and edit `kn.conf`.
2. **Project > Security > API tokens**: make a token with **Read & Write**. Put it in your terminal for this session only:
   ```bash
   export HCLOUD_TOKEN='paste-here'
   ```
   Never paste it into chat, a file in the repo, or `kn.conf`. To keep it for later, add it as a user environment variable
   `HCLOUD_TOKEN` in Windows (Settings > System > About > Advanced system settings > Environment Variables).
3. **SSH key** (Git Bash): `ssh-keygen -t ed25519 -f ~/.ssh/kn_hetzner` (a passphrase is a good idea).
4. **Install `hcloud`** (the Hetzner command-line tool): `winget install HetznerCloud.hcloud` (or download from
   github.com/hetznercloud/cli/releases). Also needs `ssh`, `scp`, `rsync` (Git Bash has ssh/scp; rsync from `pacman` in
   MSYS2 or run the scripts in WSL where all are one `apt install` away) and `python`.
5. **Certificate for the box** (the CA key stays on your PC): `bash Assets/_Game/Networking/Tools~/certificates.sh server "$HOME/Documents/KryticalNova-Secrets/server-cloud"`.
6. **Build the Linux server**: Unity menu **Krytical Nova > Build > Dedicated Server (Linux)** (about 5 minutes;
   goes to `Builds/Server/Linux/`).
7. `cp kn.conf.example kn.conf` and edit it.

## The commands, in order

```bash
cd Assets/_Game/Networking/Deploy~
bash lint.sh                       # nothing touches the cloud
./create.sh --dry-run              # read what it would do
./create.sh                        # makes the box (money starts), about 3 minutes
./deploy.sh 1                      # sends build 1 and starts it
./smoke-test.sh                    # all ok?
# play: in the game press F1, type the box's address (printed by create.sh) and port 7777, JOIN
./deploy.sh 2                      # a newer build; ./deploy.sh rollback goes back
./pull-backups.sh                  # the box's nightly database copies to this PC (KN_DUMP_DIR)
./teardown.sh                      # ALWAYS when the test is over: copies the test database here, then deletes the box
```

The control page (port 8090) is never opened to the internet: on the box it listens on 127.0.0.1 only (`pageAddress`,
written by `deploy.sh`; plan item 10), so the firewall is not its only guard. To see it: `ssh -i ~/.ssh/kn_hetzner -L 8090:127.0.0.1:8090 kn-admin@<address>`, then open http://127.0.0.1:8090.

## What is checked, and what is not

- Checked on the PC: shell syntax, JSON, dry runs of all four cloud scripts.
- **Not run yet:** the `hcloud` calls. `box-setup.sh`, `pg-setup.sh` and the systemd units were proven on WSL2 (Ubuntu
  24.04 with systemd, 29 Oct 2026) before any cloud money is spent (plan, advisor item 3).
- **The box's address:** `deploy.sh` writes the box's IP into `publicAddress` in the settings it installs, so the server
  list entry `auto` and the world service hand out that address (never 127.0.0.1 or a private one; plan item 2).
- **Nothing private listens on the box's public address (plan item 10):** the control page binds 127.0.0.1
  (`pageAddress`), the save service refuses to start on any address but this machine or a private network (even if
  `--urls` or `ASPNETCORE_URLS` say otherwise), Postgres listens on localhost, and `/stop` on loopback.
- Known gaps, from the plan: map servers beyond Concord stay off (`"mapServers": []`) until travelling
  exists; `kn-server@0` etc. start map servers if you add entries to `mapServers` and run `systemctl enable --now kn-server@0`.
- **Stopping saves through an explicit call, not SIGTERM** (a Unity Linux player does not save on SIGTERM): `box/kn-stop.sh` posts
  to `http://127.0.0.1:<status port>/stop` with the save service key (the status port is the game port + 1, or the listed
  server's `statusPort`). The game has the endpoint (commit f7911ed7c) and the stop was proven in WSL2 on 29 Oct 2026; the
  cloud-box run of `shutdown-save-test.sh` is still open.
- **Ports come from one table:** `server-settings.json` (the listed server and its `mapServers`), read by `ports.py`
  for the firewall (`create.sh`), the outside check (`smoke-test.sh`), and with `jq` on the box (`box/kn-args.sh`,
  `box/kn-stop.sh`). There is no `ports.json`. `ports.py` refuses a table where two servers would share a port.
- The box holds the **test** database only (`krytical_nova_test`); the real name `krytical_nova` is never created there.
- **Nightly copies of the test database (Linux backups):** `kn-backup.timer` runs `box/kn-backup.sh` at 04:00: `pg_dump -Fc`
  into `/var/backups/kn`, checked with `pg_restore --list`, the newest 14 kept; `/var/backups/kn/last-backup.txt` says when
  and whether it left the box. Off the box: `pull-backups.sh` fetches new copies to `KN_DUMP_DIR` on this PC; or the owner
  puts an rclone login in `/etc/kn/rclone.conf` and the place in `/etc/kn/backup-remote` (for example `drive:KryticalNova-Backups/cloud-test`),
  and every copy goes there too. `teardown.sh` still copies the database to this PC before it deletes anything.
  Written 30 Oct 2026, not yet run on a box or in WSL2.
- **Logs (plan item 6):** the servers and the save service write to the journal (`-logFile -`; read with
  `journalctl -u kn-server@main` or `-u kn-save`), which `box-setup.sh` caps at 2 GB and 30 days. The servers'
  performance logs go to `/var/log/kn/performance` (`performanceLogFolder`), outside the build folders so a new build
  keeps them; a new file each day, the newest 30 per server kept. The frame rate is capped at 30 by the game.
