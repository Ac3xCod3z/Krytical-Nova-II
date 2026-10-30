# Linux cloud servers: test setup and production setup

Written 29 Oct 2026 by the "server-setup" mini-boss session. Live checklist (tick items as they land).
Builds on `online-phase-08-real-servers.md` (host choice, deploy script, staging, attack rehearsal), `online-roadmap.md`
("Why it moves to real servers unchanged"), `online-going-live-runbook.md` and `online-phase-05-more-than-one-map.md`
(a server per map behind one name). **Nothing here is built yet. No money was spent and no account was made.**

## Why now

The owner can no longer forward ports on the stock Spectrum router (a limit on the rules; 7777-7780 already use four).
A bigger test (15 to 20 players) needs a real machine anyway. Phase 8 already says the Linux build moves unchanged; this
plan turns that into a repeatable script for a **test** setup and a sized **production** setup.

## What the five Google AI Mode chats said, and what holds up

The chats (26 and 27 Oct 2026) are one long conversation split in five shares: router rule limits, Unity CCD vs cheap
CDN, 28 zone servers and cost, security and code signing, and a last turn about a "migration skill" for Netcode for
Entities. (Google blocked the built-in browser with a CAPTCHA; the chats were read in the owner's Chrome. Chats 3 to 5
are long and were read in part: the router, cost, security and Entities-handoff turns in full; the code-signing and
store-order turns by their summary in chat 4.)

| Claim in the chats | Verdict | Source / why |
| --- | --- | --- |
| Run the Unity server on Linux; one project, client builds stay Windows/Android/iOS | **True** | Unity manual, Dedicated Server build; our `ServerBuildBuilder` already builds Linux |
| Hetzner gives 4 vCPU / 8 GB for about $15 a month | **False now** | Hetzner's page (29 Oct 2026): CPX32 4 vCPU/8 GB is €35.99 in the EU and CPX31 is **€62.99 in the US** (Ashburn/Hillsboro) after the April and June 2026 rises. The cheap CX range showed "unavailable" |
| 1 to 4 TB free traffic is plenty | **Partly** | EU plans include 20 TB; US plans only 1 to 5 TB, extra at about €1 per TB. Measure before choosing the US |
| Hetzner has "excellent" UDP DDoS protection | **Overstated** | Forum reports and our own phase 8 note say basic, slow to react on game UDP. OVH Game and Vultr's paid add-on ($10 per instance per month, 10 Gbps, per Vultr docs) are the ones that say so in writing |
| An EV certificate bypasses SmartScreen at once | **False since 2024** | Microsoft's code-signing options page: EV and OV count the same; Azure Artifact Signing does not give instant trust either; reputation builds per signed file over time |
| Tunnel tools (playit.gg, ngrok) let friends in without port forwarding | **Plausible, unverified** | Not tested; the game's own **Relay** mode (Unity Relay, already built) does the same job with no new tool |
| Keep saves in the save service's memory and write Postgres later ("fire and forget") | **Reject** | Breaks our rule: all-or-nothing `ServerSave.SaveTogether`, the kept ledger, the kill test. A crash would lose or duplicate items |
| Postgres over a Unix socket on the same machine, never exposed | **True** | Matches what we want for the test box and the first production box |
| Endianness code (`HostToNetworkOrder`) for cross-play | **Not needed** | The netcode package serialises the same on every platform we ship |
| Never expose the database; rate-limit packets; server decides; hash passwords | **True, already ours** | Phase 2 (limits, bans, encryption); accounts are Unity Player Accounts, not our own password table |
| 28 zone processes fit on one cheap box | **Unknown** | Each Unity server idles at a few hundred MB; Phase 7 measured caps decide; do not assume |
| Spectrum's terms forbid public servers | **Unverified** | Not checked; moot once the servers leave the house |

## Design rule: the server layer does not care which netcode is used

Three layers, only the top one knows the netcode. The sibling session `dots-migration` is moving the game to Netcode for
Entities; none of the work below waits for it, and none of it is thrown away by it.

1. **Host layer (new, netcode-free):** the Linux machine, firewall, users, folders, systemd units, backups, monitoring,
   the deploy script. It sees a game server only as *a program started with flags that listens on one UDP port, keeps a
   TCP web port for health, and saves everyone when told to stop*.
2. **World/save layer (existing, netcode-free):** Postgres, SaveService (.NET), world service (presence, signed use-once
   tickets). Plain HTTP between processes. Nothing changes when the netcode changes.
3. **Game server program (netcode-specific):** today Netcode for GameObjects. After the migration, Netcode for Entities.

**The contract between layers 1 and 3** (written down once, both netcodes must keep it; send to `dots-migration`
through Master):

- Started as `KryticalNovaServer -batchmode -nographics -role map -map <name> -port <udp> ...` with every setting from
  `server-settings.json` or flags (already true).
- One UDP game port per map server, one TCP page port per map server. A port table lives in one file
  (`ports.json`); the firewall, the units and the world service all read it.
- On `SIGTERM` it saves everyone, releases locks and exits within a set time (phase 1, item 5).
- Health: the control page's local port answers; the world service sees the 10 second presence report.
- The ticket handed over at travel is the world service's token and is carried in whatever "connect with data" the
  netcode offers (connection approval data in NGO, a connection-request RPC in Netcode for Entities). The ticket format
  and the checks stay in the world service.
- The Unity Linux Dedicated Server build is needed for both. Netcode for Entities needs the server world created from the
  `UNITY_SERVER` build; to be confirmed against the Entities package docs by `dots-migration` (not verified here).

Consequence: build and test the whole Linux setup on today's NGO build first. When the Entities build exists, run the same
test script against it; if a flag or port differs, that is a bug to fix in the build, not in the scripts.

## Where to host (decision for the owner)

Players are in the US (the owner is on Spectrum). Latency matters more than the last euro. Findings (29 Oct 2026, vendor
pages; confirm in the vendor console before buying):

| Option | 4 vCPU / 8 GB class | Traffic | Flood protection | Notes |
| --- | --- | --- | --- | --- |
| Hetzner Cloud, EU (Falkenstein, Nuremberg, Helsinki) | CPX32 €35.99 | 20 TB | basic, game UDP weak | Cheapest, but 90 to 130 ms from the US east coast: fine to *test scripts*, bad to *play* |
| Hetzner Cloud, US (Ashburn, Hillsboro) | CPX31 €62.99 | 3 TB | basic | Good API, billed by the hour, destroy after a test costs cents; dearer than assumed. CX/CAX cheap lines showed "unavailable" |
| OVHcloud US VPS | about $5 to $20 (VPS-1 4 vCore/8 GB listed near $5 to $6.50, third party) | "unlimited", 400 Mbps public | anti-DDoS included, up to 3 Gbps (their VPS page) | Cheapest in the US; data-centre list and terms need confirming; API is weaker than Hetzner's |
| Vultr | $40 regular, $48 AMD | 4 to 6 TB | add-on $10 per instance | Good US spread, API and Terraform provider |
| Managed (Edgegap, AWS GameLift) | by the minute | n/a | included | Later, when Lines must start by themselves (phase 8) |

**Recommendation.** Write the scripts so the host is one small module (Terraform provider + one cloud-init file); the
rest is plain Linux. Use **Hetzner for the test setup** (best API, hourly billing, the owner already picked it), run
the first test in a US location, and **decide the production host after Stage 2 measures** (latency from US players,
traffic per player, a flood rehearsal). Likely production answer: OVH US or Vultr + DDoS add-on for the game servers, with
Postgres and the world service on a second small machine on a private network. If Hetzner US stays at €63 per 4 vCPU box,
OVH wins on price; if the owner values one vendor and API, Hetzner wins. Both stay open until measured.

## Stages and monthly cost (rounded; excludes VAT; check before buying)

| Stage | What | Size | Cost |
| --- | --- | --- | --- |
| 0 Today | Owner's PC, Relay mode, no router rules | none | $0 |
| 1 Test box | One Linux machine: Postgres (test database), SaveService `--test`, 2 to 3 map servers, test saves only | 2 to 3 vCPU, 4 GB (CPX21 US €32.49, EU CPX22 €19.99) | pennies per test if destroyed after (hourly), about €20 to €32 if left on |
| 2 Stress test (15 to 20 players) | Same, all maps the build has, Phase 7 bots on top, flood rehearsal | 4 vCPU / 8 GB | €36 EU to €63 US a month, or a few euros for a weekend |
| 3 Beta (single region) | Machine A: game servers. Machine B: Postgres + save/world service, private network. Staging copy by script (hourly) | A 8 vCPU/16 GB, B 2 vCPU/4 GB | about $100 to $200 a month (US prices), plus backup storage (a few dollars) and domain |
| 4 Launch | More game machines per region, a Line per busy map, managed DDoS in front, offsite backups | measured | decided at Stage 3 with real numbers |

Other lines to price: a domain (about $12 a year), backup storage (Google Drive copy already exists; add an object
bucket, a few dollars), code-signing certificate for the launcher (phase 9), Cloudflare free plan for the web side.

## How the owner tests without router port-forwarding

- [ ] **Now, free:** switch the running server to **Relay** mode (control page, "Switch to Relay"). It hides the address and
  needs nothing on the router. It does not help when Spectrum itself is down. Works for the existing 2 to 5 testers.
- [ ] **Better:** Stage 1 test box. Friends and the owner join the cloud box over the internet; the owner's PC runs only
  the Editor and Unity. No router, no home address shown.
- [ ] **Admin access without opening anything:** the control page (8090) and Postgres are never public. Use SSH
  key-only, and an SSH tunnel (`ssh -L 8090:localhost:8090`) from the PC to see the control page.
- [ ] **Optional:** Tailscale free Personal plan (up to 6 users, per Tailscale's plans page, non-commercial) so
  a few trusted testers reach a home PC server by a private address. Not needed once Stage 1 exists.
- [ ] Skip playit.gg/ngrok unless Relay proves too slow: a third party sees all game traffic and their free-tier UDP limits
  were not verified.
- The current Windows dedicated server on the owner's PC stays the place to try a change before it goes to the box.

## The test setup (repeatable, scripted)

Tool choice: **Terraform (or OpenTofu) with the official `hetznercloud/hcloud` provider, plus one cloud-init file and a
shell deploy script.** Reasons: one command creates and destroys the whole thing; the firewall is code; cloud-init
makes the machine identical every time; no extra agents run on the box. Ansible, Kubernetes and Docker orchestration are
not used (no new machinery; same rule as CLAUDE.md for the game code). Plain systemd runs the processes. Files go in
`Assets/_Game/Networking/Deploy~/` (the `~` keeps them out of Unity, like `ServerApp~`).

Planned layout (names are proposals):

```
Deploy~/
  terraform/        main.tf (server, firewall, ssh key, volume), variables.tf, outputs.tf (ip)
  cloud-init.yaml   users, packages, ufw, postgres, dotnet runtime, directories, systemd templates
  ports.json        one table: map -> udp port, page port
  deploy.sh         copy a numbered build, write settings from ports.json, restart units, roll back
  smoke-test.sh     ports answer, only game ports open (nmap from outside), save service up, one bot joins
  README.md         the owner's steps in plain words
```

Checklist:

- [x] **A. Linux server build verified on Linux** (the build exists: `ServerBuildBuilder`, `start-server.sh`). Run it
  once on a throwaway VM; confirm flags, `SIGTERM` save, ports, no display. Fix gaps in the build, not the script.
  Done 29 Oct 2026 in WSL2 with the Mono build (PR 166): stop paths and the player save-loss test passed ("what was measured" below);
  IL2CPP does not fit on this PC.
- [x] **B. SaveService for Linux:** `dotnet publish -r linux-x64 --self-contained` of `Persistence/Service~/SaveService`;
  check Npgsql, paths, the `--test` mode (port 7802, database `krytical_nova_test`), the backup job (it uses Windows
  paths and Google Drive today; on Linux write dumps to `/var/backups/kn` and push them to a bucket).
  Done 29 Oct 2026 (PR 165, proven in WSL2 on the test database); the Linux backup job is still a follow-up (item 9).
- [x] **C. cloud-init + units:** user `kn` (no login), folders `/opt/kn/builds/<n>`, `/etc/kn/*.env` (root, mode 600; the
  secrets live here, never in git), systemd `kn-save.service` and a template `kn-map@.service` (one instance per map,
  `Restart=on-failure`, `KillSignal=SIGTERM`, `TimeoutStopSec` long enough to save). Done 29 Oct 2026 (PR 167, proven in WSL2).
- [x] **D. Postgres 18 on the box:** listens on the Unix socket and localhost only; `krytical_nova_test` for the test
  setup; real database name never created on the test box. Nightly `pg_dump` plus the restore drill into
  `krytical_nova_drill`. Postgres 18 proven in WSL2 29 Oct 2026 (PR 167); the nightly `pg_dump` is still a follow-up.
- [ ] **E. Terraform:** server, SSH key, Hetzner Cloud Firewall (below), optional volume for the database, outputs the
  address. Token read from an environment variable the owner sets (never in a file in the repo).
- [ ] **F. `deploy.sh`:** numbered builds, symlink swap, restart, roll back to the previous number.
- [ ] **G. `smoke-test.sh` + bot test:** the existing bot player (`Builds/Bots`) joins the cloud box from the PC; port
  scan from outside shows only the game UDP ports.
- [ ] **H. Teardown:** `terraform destroy` removes the box so the bill stops; a volume or snapshot keeps the test database
  if wanted.
- [ ] **I. Client side:** server list shows one entry, Nova (phase 5 rule); the server publishes its address by itself
  (phase 5 `ServerReach`); a tester who never saw the owner's PC joins.

## Security, firewall, ports, DDoS

Honest limit: nobody can promise "impregnable". The aim is a small attack surface, nothing secret on the game servers,
and a plan for the day a flood comes.

- **Ports (public):** UDP one per map server (7777, 7779, ... from `ports.json`), TCP 443 for the world service's public
  face later. **Never public:** 22 (SSH) except from the owner's address or through a tunnel, 5432, 7801/7802 (save service),
  8090+ (control pages).
- **Two firewalls:** the cloud firewall (Terraform) in front, `ufw` on the machine behind it; both default-deny.
- **Access:** SSH keys only, root login off, `fail2ban`, `unattended-upgrades` for security patches, one non-root `kn`
  user for the game, a separate Postgres role per service with only the rights it needs.
- **Secrets:** `/etc/kn` (mode 600) written by the owner or a one-time script, never in git, never in logs, never sent to
  chat (CLAUDE.md save rules). The save service key in `Documents\KryticalNova-Secrets` is not copied by me.
- **Game rules already built (phase 2):** server decides, speed limits, encryption, bans, ticket handover. Keep them; add no
  trust in the client. The Google advice to compile server-only code out of clients (`UNITY_SERVER`) is already how builds
  work and is worth keeping.
- **DDoS:** the machine's own firewall cannot stop a flood that fills the link. Host-level protection is the only fix.
  Plan: Stage 1 and 2 accept basic protection. Before any public beta, rehearse a flood (phase 8, item 8) on the chosen
  host and get, in writing, what they do. If Hetzner is too weak for game UDP, move the game machines (not the database)
  to a host with game protection; the scripts keep working because only the Terraform module changes. Web side
  (website, Stripe return page, world service HTTP) goes behind Cloudflare's free plan; **Cloudflare free does not protect
  game UDP.**
- **Stripe:** Checkout/Elements only, so card data never touches our servers; webhook endpoint verifies Stripe's signature;
  the store lives on a separate small web service, not on a game machine (see `online-phase-10-stripe-readiness.md`).
- **Windows SmartScreen / signing:** sign the launcher and client with an OV certificate or Azure Artifact Signing;
  expect reputation to take time either way; EV does not skip it. Detail stays in phase 9.

## Postgres and save service on Linux

- Same program, same rules: SaveService is the only program that talks to the database; ledger, `SaveTogether` and
  migrations are untouched. Do not adopt the chat's "write to memory first, Postgres later".
- Test box: Postgres and SaveService on the same machine, Unix socket (Npgsql `Host=/var/run/postgresql`). Production:
  second machine on a private network; the database port is open only to that network.
- Tuning to measure, not guess: `shared_buffers` about a quarter of RAM, connection pooling in the service (already
  one process), WAL on a separate volume if the writes show up in Phase 7 numbers.
- Backups: nightly `pg_dump` to local disk and a bucket (14 days), restore drill into a scratch database every release,
  point-in-time recovery (WAL archiving) before launch. The `--restore-real` last resort stays the owner's only.

## Map servers behind one name

Phase 5 already decided it: one entry, Nova. On Linux: one `kn-map@<map>` unit per map from `ports.json`; the world
service (inside SaveService) holds presence and signs tickets; only Concord's server publishes to the list; players
get the destination's address and a 30 second, use-once ticket. At 28 maps on a few machines the only new piece is the
port table and the unit template. Idle maps: start them all (simplest) until Phase 7 shows RAM per idle map; start-on-demand
comes with automatic Lines (phase 8, item 9), not before.

## Monitoring and backups

- [ ] Health: systemd restarts; `smoke-test.sh` on a timer; the world service list of servers up is the live view.
- [ ] Metrics: the existing performance log (`PerformanceLogs/perf_*.txt`) per map; a small `node_exporter`-free start:
  CPU, RAM, disk, UDP in/out per interface from `vnstat`, and the provider's traffic graph.
- [ ] Alerts: one message to the owner's phone when a unit is down, disk is above 80 percent, traffic passes 70 percent of the
  allowance, or the nightly backup did not run (a free ntfy or email; no paid service for now).
- [ ] Backups: as above; **never run anything against the real database or real server from a session.**

## Work breakdown and who does it

| # | Item | Who | Needs the owner |
| --- | --- | --- | --- |
| 1 | Settle the contract and ports table; send to `dots-migration` | this session + Master | no |
| 2 | Linux SaveService publish and paths | nested session | no |
| 3 | `Deploy~/` terraform + cloud-init + deploy and smoke scripts | nested session | account + token (below) |
| 4 | Stage 1 run, bot test from the PC | this session | friends online for 20 min |
| 5 | Stage 2 measurements and host decision | this session | decision on host |
| 6 | Flood rehearsal, signed-certificate plan, Stripe side | later (phases 8 to 10) | accounts, money |

## What the owner must do by hand (nothing was done for them)

- [ ] Choose the first test host (see decisions) and make the account; add a payment method; set a **billing limit**.
- [ ] Create a Hetzner project and an **API token** (read and write); store it as an environment variable
  `HCLOUD_TOKEN` on the PC; never paste it into chat.
- [ ] Make an SSH key pair (a session can show the command); add the public key to the project.
- [ ] Confirm which plans are in stock in the console (the pricing page said "unavailable" on every Create button).
- [ ] Choose a domain name (optional until Stage 3).
- [ ] When the test is over, run `terraform destroy` (the README will say how) or ask a session to.

## Done when

- A friend who has never touched the owner's PC joins the Linux box and plays; nothing on the router is used.
- The whole box is built from nothing, and destroyed, by scripts in one sitting.
- Only the game UDP ports answer from outside; Postgres, SSH and control pages do not.
- The same scripts run the Netcode for Entities server build with only a flag or port change.
- Stage 2 numbers (latency from US, traffic per player, RAM per map) are written down and the production host chosen.

## Risks

- Hetzner US prices and stock changed twice in 2026; re-check on the day.
- Game-UDP flood protection at Hetzner is weak; do not open a public beta there without the rehearsal.
- Traffic: US plans include far less than EU; a few hundred players can pass 3 TB. Measure at Stage 2.
- Windows-only code paths (Google Drive backup, file paths) in the save service must be proven on Linux.
- Netcode for Entities headless server behaviour is unverified here; `dots-migration` must confirm.

## Decision 29 Oct 2026 and cost guards

**The real server stays on Windows with router port-forwarding for now.** The cloud box is a **test tool only**, used in hours.
- [x] Hetzner billing facts read from Hetzner's docs (29 Oct 2026) and written in `Deploy~/PREP-CHECKLIST.md`: hourly with a monthly
  cap; **a powered-off server still bills**; snapshots, volumes, Primary IPv4 (even unassigned), Floating IPs and server backups bill;
  Cloud Firewalls are free; only outgoing traffic past the allowance bills and the alerts do **not** cap it; **no spending limit
  exists**; new accounts may need an ID or a card prepayment (a credit, not a fee).
- [x] Cost guards in `Deploy~`: `create.sh` refuses without `--confirm-spend`, prints Hetzner's hourly and monthly price first,
  refuses a second box, records the start (`.session`) and starts `remind.sh` (pop-up after `KN_MAX_HOURS`, default 8, then hourly;
  it only reminds); `cost-check.sh` lists every billable resource (servers, volumes, snapshots, backups, Primary and Floating IPs,
  load balancers) and the session age; `teardown.sh` ends with `cost-check.sh --expect-nothing`.
- [ ] Owner does the zero-cost prep (PREP-CHECKLIST items 1 to 10); nothing is created until the owner says go.

## Advisor review (Opus Plan agent, 29 Oct 2026): changes that now override the text above

Where this section and the text above disagree, this section wins. Order of work: contract (1, 2, 10) → WSL2 proof
(3, 6, 7, 8) → teardown and backup (9) → `hcloud` Stage 1 → Stage 2 measurements and host choice → Terraform and
production layout (11, 12).

- [x] 1. **No `ports.json`.** The port table is `mapServers` in `server-settings.json` (phase 5, milestone 1). Scripts read it
  (for example with `jq`); the world service keeps learning addresses from the 10 second presence reports.
  Done 30 Oct 2026: `ports.py` (firewall in `create.sh`, the outside check in `smoke-test.sh`, which also checks every map
  server's control page is closed) and `jq` on the box (`kn-args.sh`, `kn-stop.sh`) all read that table; the listed server's
  own `statusPort` is honoured; `ports.py` refuses a table where two servers share a port.
- [ ] 2. **Listen address vs public address** joins the contract: the server binds 0.0.0.0, and the address the world
  service hands out and `ServerReach` publishes must be the public IPv4 (a `publicAddress` setting), never 127.0.0.1 or a
  private one. The cloud box needs a public IPv4 (priced separately at Hetzner).
- [x] 3. **WSL2 first, $0.** Prove the Linux server build, Linux SaveService publish, Postgres, systemd units, SIGTERM save
  and `deploy.sh` in Ubuntu on WSL2 (systemd on) before any account. Rent cloud hours only for the outside port scan, the
  bot join from the PC and the friend test.
- [ ] 4. **RAM:** phase 5 says 1 to 1.5 GB per map server; "a few hundred MB" above is wrong until Phase 7 measures. Stage 1
  is 8 GB, or Concord only.
- [ ] 5. **Stage 1 scope:** travelling (phase 5, milestone 3) is not built, so Stage 1 is **one playable map (Concord)**
  with Transit Row running but unreachable. Phase 8 needs Phase 7; Stage 2's numbers count as the Phase 7 measurements.
- [ ] 6. **Linux server pitfalls (item A):** cap the server frame rate (no vsync in batchmode, an uncapped server eats a
  core); prove SIGTERM runs the save-everyone path, `KillMode=mixed`, measured `TimeoutStopSec`; `-logFile` to a known path
  or journald with rotation (also `perf_*.txt`); pin one Ubuntu LTS; Mono, or the Linux IL2CPP toolchain if IL2CPP.
- [ ] 7. **Postgres:** "18" means the PGDG apt repository (Ubuntu 24.04 ships 16); match the Windows major version; import
  with `pg_dump -Fc --no-owner` and a set locale (Windows collation names do not exist on Linux); prove in WSL2.
- [ ] 8. **Database login:** pick one and write it down: peer login over the socket with one role, or scram passwords over
  the socket with a role per service.
- [ ] 9. **Teardown never loses the database:** `teardown.sh` runs `pg_dump` and copies it off the box (Object Storage or
  `rclone` to the existing Drive copy) before destroying; the credential is one more secret in `/etc/kn`.
- [ ] 10. **SaveService is never on public 443.** It holds the database; the public face must be a separate thin process.
  Check that control pages and 7801/7802 bind to localhost or the private address (not only blocked by the firewall),
  and what `SaveService --test` relaxes before using it on a public host.
- [ ] 11. **No cross-vendor database link by accident:** game and database machines come from the same vendor (private
  network), or a WireGuard link plus TLS; the HTTP between map servers and SaveService is plain HTTP with a key today.
  This replaces "move the game machines to another host, keep the database" above.
- [ ] 12. **Stage 1 uses the `hcloud` CLI** (`create.sh`, `destroy.sh` plus the same cloud-init), no Terraform state file.
  Add Terraform when the production host is chosen. `vnstat`, alerts and the smoke-test timer wait for Stage 2.
- [ ] 13. **SSH from a changing home IP:** key-only plus fail2ban open to the world, or a rule the create script refreshes
  from the current IP; Tailscale on the box only as a deliberate choice.
- [ ] 14. **Unverified prices:** Hetzner US €62.99 and OVH "$5 to $6.50, 4 vCore/8 GB" (third party, likely another
  product) must be re-checked in the vendor console. The host decision rests on measured latency, traffic per player and a
  written flood policy (phase 8).
- [ ] 15. **Relay mode** helps only Concord until travelling lands (map servers are forced Direct). The Netcode for
  Entities contract line stays unverified and is owned by `dots-migration`.

- [ ] 16. **Saving on stop must not rely on SIGTERM (DOTS Linux spike, 29 Oct 2026):** a Unity Linux player exits in about
  0.2 s on SIGTERM and `Application.wantsToQuit` is **not** called, so nothing is saved. The contract now has an **explicit
  stop path**: `POST http://127.0.0.1:<port+1>/stop` with the header `X-KN-Key` (the save service key), bound to 127.0.0.1,
  which saves everyone, releases locks and then `Application.Quit(0)`. `kn-server@.service` runs `box/kn-stop.sh` as
  `ExecStop`, which calls it and waits for the game to exit (up to 85 s, `TimeoutStopSec=90`); if the call is missing or
  refused it exits 0 and systemd sends SIGTERM, so a build with a signal handler (DOTS is testing one) still works. Same
  contract for Netcode for GameObjects and for Entities.
  - [x] Scripts: `box/kn-stop.sh`, the unit's `ExecStop`, `shutdown-save-test.sh`; proven in WSL2 **with stubs only**
    (stub with `/stop`: 2 s, exit 0, saved line; stub with SIGTERM only: falls back, exit 0).
  - [x] **Game code:** on main (commit f7911ed7c): the `/stop` endpoint on the status port (127.0.0.1, key-checked)
    that runs the same save-all as the control page's Stop, then quits with exit 0. Owner: networking/server session.
  - [ ] **Real-server shutdown-save test:** run `sudo ./shutdown-save-test.sh` with the real Linux build (WSL2, then the cloud
    box): passes only if the unit stops within 90 s, exit 0, and the real "everyone saved" log line appears (set
    `KN_SAVED_PATTERN` to that line). Also kill-test the stop (power-off style `systemctl kill -s KILL`) and confirm no save is
    lost beyond the autosave interval. **WSL2 passed 29 Oct 2026** ("what was measured" below: PATH A, PATH B, `kill -9`);
    the cloud box run is still open.

## Remote mode for the Krytical Nova Server app (design only; owner request 29 Oct 2026)

Goal: the owner follows and runs the cloud box from the same Windows app they use today, with no new program. **Local
mode stays exactly as it is** (the app starts the save service and the server on the PC; it is the way to test a change).
Built only after the Hetzner test box exists and only on Master's word.

**What the app is today** (`Networking/ServerApp~`, WinForms on .NET Framework 4.7.2, no NuGet packages, about 1200 lines):
one tab per game server (`GameServer`: a log pane, a state, a command box, START / RESTART / STOP / CONTROL PAGE), a SAVE
SERVICE tab, a DATABASE tab (Postgres Windows service, backups list, BACK UP NOW). Everything runs through `Hosted`, a
supervised child process whose output lines fill the log. A 3-second `Check()` reads the save service's `/health` with the
service key (`Documents\KryticalNova-Secrets\save-service-key.txt`) and shows the pills.

**The one idea:** remote mode is the same tabs fed by **`ssh.exe` child processes** instead of local programs. Windows 10/11
has OpenSSH built in, so the app needs no library (net472 has none), and `Hosted` already supervises a child and streams its
lines. The app never opens the private key itself: `ssh` uses the owner's key file (and ssh-agent for a passphrase).

| Piece | How |
| --- | --- |
| Connection | `ssh -N -L 18090:127.0.0.1:8090 -L 18092:127.0.0.1:8092 -L 17802:127.0.0.1:7802 -i <key> kn-admin@<ip>` held by one `Hosted`; reconnect on drop. Only port 22 is open to the world, so everything else goes through this tunnel |
| Settings | A new "Remote" section of the app's own settings (host, user, key file path, ports from `server-settings.json` fetched once over ssh). Nothing secret is stored; the key file is only a path |
| Service key | Read once per connection with `ssh ... sudo cat /etc/kn/save-service-key.txt`, **held in memory only**, never written, logged or shown (CLAUDE.md secrets rule). Used as `X-KN-Key` on calls through the tunnel to the save service |
| Switch | A "Local / Remote" choice at start. Remote never starts a local server, and never offers the real-database tools unless the box says it is the real one |

**Tabs in remote mode** (same layout, different source):

| Tab | Shows | Source |
| --- | --- | --- |
| Each game server (CONCORD, maps) | State, players, uptime, live log, command box | state and players from the status port and journal; log = `ssh ... journalctl -fu kn-server@<n> -o cat` in a `Hosted`; commands through the control page's own call over the tunnel |
| Buttons | START, RESTART, STOP | `systemctl start|restart|stop kn-server@<n>`; **STOP/RESTART use the unit's `ExecStop`, which calls the planned `/stop` save endpoint** (plan item 16), so the app never kills a process itself |
| CONTROL PAGE | Opens the live control page | `http://127.0.0.1:18090` through the tunnel (its own password applies) |
| SAVE SERVICE | Health, key accepted, test or real, saves and size, **last backup time and whether it left the box** | `/health` through the tunnel; log = `journalctl -fu kn-save` |
| DATABASE | Read-only viewer: size, document and ledger counts, newest backups, a few fixed queries (saves per account, ledger kinds, locks) | fixed queries run by `ssh ... sudo -u postgres psql -At -c ...` with a **read-only role**; port 5432 is **never** tunnelled or opened. No free-text SQL in the first version |
| DEPLOY (new) | Current build number, builds on the box, DEPLOY and ROLLBACK buttons, a "pull a backup" button | wraps `Deploy~/deploy.sh <n>` and `deploy.sh rollback` (run in WSL/Git Bash as a `Hosted`, their output shown live), `smoke-test.sh`, and `scp` of the newest dump into `Documents\KryticalNova-Backups\cloud-test`. Deploy and rollback ask for a typed confirmation |

**What runs where.** On the box: Postgres, SaveService, the game servers, `kn-stop.sh`, the units, the firewall. On the PC:
the app, the ssh tunnel, and (for deploy) the Deploy~ scripts. The box needs **nothing new installed** for remote mode except
one read-only Postgres role and (item 16) the `/stop` endpoint; no agent, no web console, no extra open port.

**Security.** Only SSH (key only) is exposed; the control pages, save service and Postgres stay on 127.0.0.1 on the box and are
reached through the tunnel. The app stores no key and no password; the service key lives in memory for the session. Host key is
pinned on first connect (`StrictHostKeyChecking=accept-new`, then the entry in `known_hosts` must match). A tunnel that drops
shows RED pills, never a stale green. Destructive actions (stop, deploy, rollback) show which box and which database
(TEST/REAL) first. The app refuses remote real-database tools until the plan's production stage.

**Effort and order** (each step shippable alone; about 5 to 8 working days, mostly WinForms plumbing):
- [ ] R1. Remote settings + connection supervisor (`ssh -N -L`, reconnect, pills for TUNNEL / SAVES / SERVER). ~1 day
- [ ] R2. Server tabs from the tunnel: state, players, live journal log, START/RESTART/STOP via systemctl, CONTROL PAGE. ~1.5 days
- [ ] R3. SAVE SERVICE tab over the tunnel: health, last backup time, off-box status; service key in memory only. ~1 day
- [ ] R4. DATABASE tab: read-only role (added to `pg-setup.sh`), fixed queries, pull-a-backup button. ~1.5 days
- [ ] R5. DEPLOY tab: build list, DEPLOY/ROLLBACK/SMOKE wrapping `Deploy~`, typed confirmation. ~1.5 days
- [ ] R6. Self-test mode (`--selftest`, like today's) against the **measure/test box only**, never the real server; update
  `Server-Guide.md` and the Encyclopedia. ~0.5 day

**Depends on:** the Hetzner test box (Stage 1), the game's `/stop` endpoint (item 16, for a safe STOP/RESTART; until then these
buttons stay disabled in remote mode rather than risk a lost save), and the read-only DB role. **Not in version 1:**
free-text SQL, editing anything in the database, a multi-box list, user accounts for other GMs.

## Findings for a later cleanup (Linux server work)

- [ ] **Headless font exception.** On start a dedicated server logs `ArgumentNullException ... parameter name: shader` from
  `TextCore.Text.FontAsset.CreateFontAssetFromFamilyName <- TextSettings.GetOSFontAssetList` ("no shaders were included in the build
  because Dedicated Server Optimizations is enabled"): UI Toolkit looks up OS fallback fonts in a build with no shaders. Seen on the
  Linux server (WSL2, 16 Oct build) **and in `Logs/TestServer-7796.log` from the Windows server**, so it is not Linux-only. Harmless so
  far (the server keeps running); fix by not creating UI Toolkit text on the server, or by filtering it in the log.
  **Fixed in code 30 Oct 2026, not yet seen in a server log:** every `UIDocument` is switched off on a server, in the build's copy of each
  scene (`ServerSceneStripper`) and as scenes load (`DedicatedServerOptimizer`); the UI assembly is not in a server build, so nothing reads them.
- [ ] A Unity Linux player does not run `Application.wantsToQuit` on SIGTERM (plan item 16); the DOTS branch adds a libc SIGTERM
  handler and the `/stop` endpoint. Prove both with `shutdown-save-test.sh` on the real build.

## Linux server build and stop: what was measured (29 Oct 2026)

- **Stale build harness (16 Oct Mono build, WSL2, systemd, test DB):** `shutdown-save-test.sh` with no `/stop` falls back to SIGTERM; Unity exits in
  under 1 s with exit 0 and **no save line, so the test fails as it should**. With stubs, the explicit `/stop` path passes (2 s, exit 0, saved line)
  and the SIGTERM-only path passes. `kill -9` under the unit: systemd restarts it, listening again after 7.1 s. Real save-loss check: open until the
  new build and a bot exist.
- **Build memory:** three batch IL2CPP builds in the build checkout were killed by the watchdog (peak Unity family 9.3, 12.3, 12.8 GB; min available RAM
  2.0, 1.0, 0.3 GB). Run 3 died compiling `ConvGeneric.compute` (Sentis). Cause: the server build still compiles shaders because "Enable Dedicated Server
  Optimizations" does not apply to builds through the Scriptable Build Pipeline, which Addressables use (Unity staff, 6 Oct 2025, discussions.unity.com
  /t/912884). No shader-compiler cap flag is documented; `-job-worker-count N` is reported to limit compilers (discussions.unity.com/t/914306), unverified.
- **Fix written, not yet proven:** `Tools/Editor/Build/ServerShaderStrip.cs` strips all shader variants and compute kernels, only for the **Linux**
  Dedicated Server build made by `ServerBuildBuilder` (which builds no Addressables with the player: `DoNotBuildWithPlayer`, so no stripped bundle can reach
  a client); `KN_KEEP_SERVER_SHADERS=1` switches it off. The Windows server (the live one) is left as it is until the Linux result is proven.
- [x] **Runs 4 and 5 (29 Oct 2026, build checkout, strip active):** IL2CPP (run 4) was still killed at a 15.0 GB Unity-family peak (min available 515 MB)
  in the IL2CPP/Bee step, after shaders, assets and the player data were done: IL2CPP of this game needs 15+ GB headroom on this PC. **Mono (run 5) SUCCEEDED**:
  08:27:30 to 08:34:03 (7 min; Unity reported a 5 min 10 s build), peak 14.7 GB, min available 1.3 GB, output 679 MB, errors 2 (was 117), log line
  `[ServerShaderStrip] stripped 0 shader variants and 1910 compute shader kernels`. The strip did not break the server (it ran in WSL).
- [x] **Real-build stop tests in WSL2 (Mono build + stop seam f7911ed7c, test DB, files off, port 17777):** PATH A `systemctl stop` -> `kn-stop.sh` -> `POST /stop`:
  stopped in 2 s, exit 0, `[Server] Saved. Stopping.` found, explicit path used, **PASSED**. PATH B `kill -TERM` straight to the game (libc handler): process gone in
  1.5 s, `Stopping: The server was asked to stop (SIGTERM). Saving every player and the world...` then `Saved. Stopping.`, exit 0. `kill -9`: systemd restarted it
  (listening again, save service healthy, the world document still there). Found while testing: `Restart=always` restarted the game after a clean admin stop; the
  unit now uses `Restart=on-failure`.
- [x] **Run 6 (29 Oct 2026, 16:29-16:34, commit 233ac1460, owner at the PC, `-job-worker-count 2`):** IL2CPP killed again by the watchdog (min available 1.1 GB,
  Unity process tree peak **17.1 GB**, 4 minutes). No compile error; it dies where IL2CPP/Bee starts. Three failed IL2CPP tries (runs 4, 6 and the first batch)
  say the same thing: **IL2CPP of this game needs about 17 GB on top of what the PC already holds (31 GB total), so it does not fit here while the owner works.**
  Decision (owner via Master, 29 Oct): no more IL2CPP tries on this PC. Options kept: (A) retry with >= 20 GB available at the start (everything closed);
  (B) **now:** tests use the Mono Linux server (save behaviour does not depend on the backend); (C) the IL2CPP release server is built on a bigger machine or a
  Hetzner build box later (a CCX-class box with 32 GB is enough for a one-hour job).
- **Build numbers (bot vs server):** both read `Builds/Release/Store/manifest.json` through `ReleasePublisher.LastReleaseBuild()` (build 12, written 25 Oct) and
  stamp `bundleVersion` "0.1.0 build 12", so a bot client built in the same checkout matches the Mono server built there (08:33 the same day); a differing
  number is turned away at join.
- [x] **Player save-loss test on the Linux server (29 Oct 2026, 19:40-20:05, owner by hand):** Mono Linux server (build 12) in WSL2 on the TEST database,
  test certificate pair, the owner in the Editor (Play from the Title) through "This PC (testing)" with `Library/KryticalNova.TestSession` (port 17777,
  join, scratch save) and a UDP relay. New character "Linuxtest" made through the server roster. Compared every save document through the save service.
  - **A (play 2 min):** 30 documents written (account, 19 character parts, world names/contacts); Creds 34, level 1 / 6 XP, pack 9001x5 9002x4 11984x1.
  - **B (clean stop, `systemctl stop` -> POST /stop):** "Saved. Stopping." 36 ms after the stop began, unit down in 4 s, exit 0; the stop wrote the final
    spot. Rejoin: **Creds, level/XP, pack and equipment all kept (PASS).** The player came back at the spawn point (see findings).
  - **C (`kill -9`, `Restart=on-failure`):** systemd restarted the server by itself, listening again in 8 s; the saved documents were byte-for-byte the
    same before and after the kill (nothing half-written). Rejoin: owner reports nothing missing (PASS). The owner stood still for about two minutes before
    the kill, so everything had been autosaved already: the worst case (actions in the last 60 s, `PlayerSaveAgent.autosaveSeconds`) was not exercised.
- **Findings:**
  - **Position is not restored on join, by design today:** `PlayerSaveAgent` (lines 360-373) writes map and x/y/z "for when there is more than one map",
    but nothing reads them back; `PlayerSpawnService` puts every join on the map's next `PlayerSpawnPoint`, and the next save then stores the spawn spot.
    Same on the Windows server. "Rejoin where you stood" would be a separate small feature.
  - **Bots cannot join a roster-keeping server yet** (fixed in code 30 Oct 2026, not yet run: the bot asks the server's roster): `MeasureBot.EnsureCharacter` makes its character in the local store; since milestone 5 the server
    keeps the roster ("That character is not on this server"). Without `-profile` a bot also uses the default local profile (it picked the owner's own
    local character, read only). Fix later (development builds only): bots pass `-profile bot<n>` and create their character through the server roster.
  - **Bot client build:** a build checkout left on the Server subtarget compiles `UNITY_SERVER` into the "client" (it started a dedicated server on 7777).
    `ServerBuildBatch.BuildBotClientPlayer` forces the Player subtarget; proof before any run: the four server-only types (`ServerPerformanceLog`,
    `ServerLifetime`, `ServerControlPage`, `ServerReach`) must be absent from `KryticalNova.Networking.dll`.
  - **Windows -> WSL2:** NAT mode forwards only TCP to `127.0.0.1`, so game UDP needs the WSL IP or a relay (the test used a small Python relay on
    127.0.0.1:17777/17778). The WSL IP can change after `wsl --shutdown`; read it fresh. The box's `ufw` must allow the test ports (17777-17778/udp).
    The relay first died when the server was killed (Windows reports the ICMP "port unreachable" as WSAECONNRESET); fixed in the relay.
  - **Norton Behavioral Protection** froze freshly built Windows exes (IDP.Generic) even with folder exclusions; tests ran with it off for the test only.
  - **IL2CPP** of this game needs about 17 GB on this PC (three tries killed); the release Linux server is built elsewhere later.
- [ ] Still unproven: an IL2CPP Linux build; the Linux server with the real (production) certificate; bots through the server roster; loss of actions in the
  last 60 s before a crash (expected at most one autosave interval). Follow-up: a bot (once it joins through the roster) that earns or picks up
  something and is killed with `kill -9` within 10 s of the action; compare against the documents before the action.
- Case C: the owner confirmed the changes after the last rejoin (Creds 109 -> 1, a purchase and a weapon swap) were their own actions.


## Change log

- 29 Oct 2026 (end): PRs 165 (SaveService on Linux), 166 (server shader strip, batch build, bot client) and 167 (`Deploy~`
  scripts and this plan) merged into main; items A to D, 3 and the `/stop` game code ticked. Open items are carried by the
  server follow-ups session.
- 29 Oct 2026 (later): item 16 added after the DOTS spike found Unity does not save on SIGTERM; WSL2 proof of box setup,
  Postgres 18, SaveService and units passed (game server was a stub).
- 29 Oct 2026: plan written from five Google AI Mode chats plus vendor pages (Hetzner, OVH, Vultr, Tailscale, Microsoft).
  Skills `/skill-doctor` and `/advisor` were not available as invocable skills; an Opus Plan subagent reviewed the plan
  the same day (section above). No scripts are written yet.
