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
./teardown.sh                      # ALWAYS when the test is over: copies the test database here, then deletes the box
```

The control page (port 8090) is never opened to the internet. To see it: `ssh -i ~/.ssh/kn_hetzner -L 8090:127.0.0.1:8090 kn-admin@<address>`, then open http://127.0.0.1:8090.

## What is checked, and what is not

- Checked on the PC: shell syntax, JSON, dry runs of all four cloud scripts.
- **Not run yet:** `hcloud` calls, `box-setup.sh`, `pg-setup.sh`, the systemd units. These are proven first on WSL2
  (Ubuntu 24.04 with systemd) before any cloud money is spent (plan, advisor item 3).
- Known gaps, from the plan: the server publishing its **public address** (the `publicAddress` contract item) is not
  built, so friends type the address in F1; map servers beyond Concord stay off (`"mapServers": []`) until travelling
  exists; `kn-server@0` etc. start map servers if you add entries to `mapServers` and run `systemctl enable --now kn-server@0`.
- **Stopping saves through an explicit call, not SIGTERM** (a Unity Linux player does not save on SIGTERM): `box/kn-stop.sh` posts
  to `http://127.0.0.1:<port+1>/stop` with the save service key. The game does not have that endpoint yet (plan item 16); until it
  does, a real stop of the real build may lose up to one autosave interval. Run `shutdown-save-test.sh` with the real build.
- The box holds the **test** database only (`krytical_nova_test`); the real name `krytical_nova` is never created there.
